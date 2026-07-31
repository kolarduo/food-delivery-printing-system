using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FoodDeliveryPrintingSystem
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    sealed class MainForm : Form
    {
        const string OrdersUrl = "https://store.rocketnow.co.jp/merchant/management/orders";
        const string OrderApiPart = "/api/v1/merchant/web/multipleStore/order/queryByPage";
        readonly WebView2 ui = new WebView2();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        readonly Dictionary<string, Dictionary<string, object>> collectedOrders =
            new Dictionary<string, Dictionary<string, object>>();
        readonly Timer finishTimer = new Timer();
        CoreWebView2Environment environment;
        Form rocketForm;
        WebView2 rocket;
        bool refreshing;
        bool navigatingToOrders;
        bool directQueryStarted;
        string selectedDate = "";

        public MainForm()
        {
            Text = "外卖打印系统";
            Width = 1180;
            Height = 760;
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            ui.Dock = DockStyle.Fill;
            Controls.Add(ui);
            finishTimer.Interval = 1800;
            finishTimer.Tick += FinishTimerTick;
            Shown += async delegate { await InitializeAsync(); };
        }

        async Task InitializeAsync()
        {
            try
            {
                string profile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FoodDeliveryPrintingSystem", "WebView2");
                environment = await CoreWebView2Environment.CreateAsync(null, profile);
                await ui.EnsureCoreWebView2Async(environment);
                ui.CoreWebView2.Settings.AreDevToolsEnabled = false;
                ui.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                ui.CoreWebView2.WebMessageReceived += UiMessageReceived;
                string uiFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
                ui.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.local", uiFolder, CoreWebView2HostResourceAccessKind.DenyCors);
                ui.Source = new Uri("https://app.local/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序初始化失败：\n" + ex.Message, "外卖打印系统",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        async void UiMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                Dictionary<string, object> message = json.DeserializeObject(e.WebMessageAsJson) as Dictionary<string, object>;
                if (message != null && Convert.ToString(message["type"]) == "refresh")
                {
                    selectedDate = message.ContainsKey("date") ? Convert.ToString(message["date"]) : "";
                    await RefreshOrdersAsync();
                }
            }
            catch (Exception ex)
            {
                SendStatus("启动刷新失败：" + ex.Message, "warn");
            }
        }

        async Task EnsureRocketAsync()
        {
            if (rocketForm != null && !rocketForm.IsDisposed) return;

            rocketForm = new Form();
            rocketForm.Text = "Rocket Now Manager 登录 / 订单页面";
            rocketForm.Width = 1280;
            rocketForm.Height = 850;
            rocketForm.StartPosition = FormStartPosition.CenterScreen;
            rocketForm.FormClosing += delegate(object sender, FormClosingEventArgs e) {
                e.Cancel = true;
                rocketForm.Hide();
                refreshing = false;
                SendStatus("Rocket Manager 窗口已关闭。", "warn");
            };

            rocket = new WebView2();
            rocket.Dock = DockStyle.Fill;
            rocketForm.Controls.Add(rocket);
            await rocket.EnsureCoreWebView2Async(environment);
            rocket.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
            rocket.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
            rocket.CoreWebView2.NavigationCompleted += RocketNavigationCompleted;
        }

        void RocketResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!String.Equals(e.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) ||
                e.Request.Content == null) return;
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(
                    e.Request.Content, Encoding.UTF8, true, 1024, true))
                    body = reader.ReadToEnd();
                object payload = json.DeserializeObject(body);
                if (!ApplyQueryFilters(payload))
                {
                    if (e.Request.Content.CanSeek) e.Request.Content.Position = 0;
                    return;
                }
                byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(payload));
                e.Request.Content = new MemoryStream(bytes);
            }
            catch
            {
                if (e.Request.Content != null && e.Request.Content.CanSeek)
                    e.Request.Content.Position = 0;
            }
        }

        bool ApplyQueryFilters(object value)
        {
            bool changed = false;
            Dictionary<string, object> data = value as Dictionary<string, object>;
            if (data != null)
            {
                List<string> keys = new List<string>(data.Keys);
                foreach (string key in keys)
                {
                    if (String.Equals(key, "pageSize", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(key, "size", StringComparison.OrdinalIgnoreCase))
                    {
                        data[key] = 500;
                        changed = true;
                    }
                    else if (String.Equals(key, "pageNumber", StringComparison.OrdinalIgnoreCase) ||
                             String.Equals(key, "page", StringComparison.OrdinalIgnoreCase))
                    {
                        data[key] = 0;
                        changed = true;
                    }
                    else if (IsStoreIdKey(key))
                    {
                        data[key] = data[key] is IEnumerable && !(data[key] is string)
                            ? (object)new object[] { 71532 } : 71532;
                        changed = true;
                    }
                    else if (!String.IsNullOrEmpty(selectedDate) && IsDateKey(key))
                    {
                        data[key] = DateFilterValue(data[key], key);
                        changed = true;
                    }
                    else if (ApplyQueryFilters(data[key])) changed = true;
                }
                return changed;
            }
            IEnumerable values = value as IEnumerable;
            if (values != null && !(value is string))
                foreach (object child in values)
                    if (ApplyQueryFilters(child)) changed = true;
            return changed;
        }

        static bool IsStoreIdKey(string key)
        {
            string lower = key.ToLowerInvariant();
            return lower == "storeid" || lower == "storeids" ||
                   lower == "storeidlist" || lower == "selectedstoreids";
        }

        static bool IsDateKey(string key)
        {
            string lower = key.ToLowerInvariant();
            return lower == "startdate" || lower == "enddate" ||
                   lower == "fromdate" || lower == "todate" ||
                   lower == "begindate" || lower == "orderdate" ||
                   lower == "starttime" || lower == "endtime" ||
                   lower == "startat" || lower == "endat";
        }

        object DateFilterValue(object original, string key)
        {
            DateTime day;
            if (!DateTime.TryParseExact(selectedDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) return original;
            // Rocket expects startDate and endDate to be the same value: the
            // selected day's midnight in Japan, expressed as Unix milliseconds.
            DateTime value = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);

            if (original is Int32 || original is Int64 || original is Double || original is Decimal)
            {
                TimeZoneInfo tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                DateTime utcValue = TimeZoneInfo.ConvertTimeToUtc(value, tokyo);
                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return Convert.ToInt64((utcValue - epoch).TotalMilliseconds);
            }

            string text = Convert.ToString(original);
            if (text != null && text.IndexOf("T", StringComparison.Ordinal) >= 0)
                return value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
            return selectedDate;
        }

        async Task RefreshOrdersAsync()
        {
            if (refreshing)
            {
                return;
            }

            refreshing = true;
            collectedOrders.Clear();
            directQueryStarted = false;
            finishTimer.Stop();
            SendStatus("正在打开 Rocket Manager…", "info");
            await EnsureRocketAsync();
            navigatingToOrders = true;
            rocket.CoreWebView2.Navigate(OrdersUrl);
        }

        void ShowRocketLogin()
        {
            if (rocketForm == null || rocketForm.IsDisposed) return;
            rocketForm.Show();
            rocketForm.WindowState = FormWindowState.Normal;
            rocketForm.Activate();
        }

        void RocketNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                refreshing = false;
                SendStatus("Rocket Manager 页面打开失败，请检查网络后重试。", "warn");
                return;
            }

            string url = rocket.Source == null ? "" : rocket.Source.ToString();
            if (url.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                navigatingToOrders = false;
                ShowRocketLogin();
                SendStatus("请在 Rocket 官方窗口中登录。", "warn");
                return;
            }

            if (refreshing && !navigatingToOrders &&
                url.IndexOf("/merchant/management/orders", StringComparison.OrdinalIgnoreCase) < 0)
            {
                navigatingToOrders = true;
                rocket.CoreWebView2.Navigate(OrdersUrl);
            }
            else
            {
                navigatingToOrders = false;
                SendStatus("已打开订单页面，正在等待订单数据…", "info");
                if (refreshing && !directQueryStarted)
                {
                    directQueryStarted = true;
                    QueryOrdersDirectlyAsync();
                }
            }
        }

        async Task QueryOrdersDirectlyAsync()
        {
            DateTime day;
            if (!DateTime.TryParseExact(selectedDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            {
                day = DateTime.Today;
                selectedDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            object timestamp = DateFilterValue(0L, "startDate");
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["pageNumber"] = 0;
            payload["pageSize"] = 500;
            payload["storeIds"] = new object[] { 71532 };
            payload["startDate"] = timestamp;
            payload["endDate"] = timestamp;
            string body = json.Serialize(payload);
            string script = "(async()=>{try{const r=await fetch('" + OrderApiPart +
                "',{method:'POST',credentials:'include',headers:{'accept':'application/json'," +
                "'content-type':'application/json;charset=UTF-8','x-requested-with':'XMLHttpRequest'},body:" + json.Serialize(body) +
                "});const text=await r.text();return JSON.stringify({status:r.status,body:text});" +
                "}catch(e){return JSON.stringify({status:-1,body:String(e)});}})()";
            string result = await rocket.CoreWebView2.ExecuteScriptAsync(script);
            string wrapperText = json.Deserialize<string>(result);
            Dictionary<string, object> wrapper =
                json.DeserializeObject(wrapperText) as Dictionary<string, object>;
            int status = Convert.ToInt32(Get(wrapper, "status"));
            if (status == 401 || status == 403)
            {
                refreshing = false;
                ShowRocketLogin();
                SendStatus("Rocket 登录已过期，请重新登录。", "warn");
                return;
            }
            if (status < 200 || status >= 300)
            {
                refreshing = false;
                SendStatus("Rocket 订单接口请求失败（HTTP " + status + "）。", "warn");
                return;
            }
            ProcessOrderBody(ToText(Get(wrapper, "body")));
        }

        void ProcessOrderBody(string body)
        {
            try
            {
                Dictionary<string, object> root = json.DeserializeObject(body) as Dictionary<string, object>;
                IEnumerable content = FindOrderContent(root);
                int responseOrderCount = 0;
                if (content != null)
                {
                    foreach (object value in content)
                    {
                        Dictionary<string, object> order = value as Dictionary<string, object>;
                        if (order == null) continue;
                        responseOrderCount++;
                        Dictionary<string, object> normalized = Normalize(order);
                        string key = ToText(Get(order, "uniqueOrderId"));
                        if (String.IsNullOrEmpty(key)) key = ToText(Get(normalized, "id"));
                        if (String.IsNullOrEmpty(key)) continue;
                        collectedOrders[key] = normalized;
                    }
                }

                List<Dictionary<string, object>> orders =
                    new List<Dictionary<string, object>>(collectedOrders.Values);
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["type"] = "orders";
                message["orders"] = orders;
                message["capturedAt"] = DateTime.UtcNow.ToString("o");
                ui.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));
                SendStatus("Rocket 接口返回 " + responseOrderCount + " 条，已读取 " +
                    orders.Count + " 条…", "info");
                finishTimer.Stop();
                finishTimer.Start();
            }
            catch (Exception ex)
            {
                refreshing = false;
                SendStatus("订单数据解析失败：" + ex.Message, "warn");
            }
        }

        void FinishTimerTick(object sender, EventArgs e)
        {
            finishTimer.Stop();
            refreshing = false;
            if (rocketForm != null) rocketForm.Hide();
            Show();
            Activate();
            SendStatus("已读取 Kawasaki 店 " + collectedOrders.Count + " 条订单", "ok");
        }

        Dictionary<string, object> Normalize(Dictionary<string, object> order)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["id"] = ToText(Get(order, "abbrOrderId"));
            result["date"] = Get(order, "createdAt");
            result["status"] = ToText(Get(order, "status"));
            result["amount"] = Get(order, "totalAmount");
            result["items"] = ItemSummary(Get(order, "items") as IEnumerable);
            result["detailItems"] = DetailItems(Get(order, "items") as IEnumerable);
            result["note"] = ToText(Get(order, "note"));
            return result;
        }

        string ItemSummary(IEnumerable items)
        {
            if (items == null) return "";
            List<string> parts = new List<string>();
            foreach (object value in items)
            {
                Dictionary<string, object> item = value as Dictionary<string, object>;
                if (item == null) continue;
                parts.Add(ToText(Get(item, "name")) + " ×" + ToText(Get(item, "quantity")));
            }
            return String.Join("、", parts.ToArray());
        }

        List<Dictionary<string, object>> DetailItems(IEnumerable items)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            if (items == null) return result;
            foreach (object value in items)
            {
                Dictionary<string, object> item = value as Dictionary<string, object>;
                if (item == null) continue;
                Dictionary<string, object> detail = new Dictionary<string, object>();
                detail["name"] = ToText(Get(item, "name"));
                detail["quantity"] = Get(item, "quantity") ?? 1;
                List<Dictionary<string, object>> options = new List<Dictionary<string, object>>();
                IEnumerable itemOptions = Get(item, "itemOptions") as IEnumerable;
                if (itemOptions != null)
                {
                    foreach (object optionValue in itemOptions)
                    {
                        Dictionary<string, object> option = optionValue as Dictionary<string, object>;
                        if (option == null) continue;
                        Dictionary<string, object> optionDetail = new Dictionary<string, object>();
                        optionDetail["name"] = ToText(Get(option, "optionName"));
                        optionDetail["quantity"] = Get(option, "optionQuantity") ?? 1;
                        options.Add(optionDetail);
                    }
                }
                detail["options"] = options;
                result.Add(detail);
            }
            return result;
        }

        static IEnumerable FindOrderContent(Dictionary<string, object> root)
        {
            if (root == null) return null;
            object content = Get(root, "content");
            if (content is IEnumerable && !(content is string)) return (IEnumerable)content;

            Dictionary<string, object> data = Get(root, "data") as Dictionary<string, object>;
            if (data != null)
            {
                content = Get(data, "content");
                if (content is IEnumerable && !(content is string)) return (IEnumerable)content;
            }
            return null;
        }

        void SendStatus(string message, string kind)
        {
            if (ui.CoreWebView2 == null) return;
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["type"] = "status";
            payload["message"] = message;
            payload["kind"] = kind;
            ui.CoreWebView2.PostWebMessageAsJson(json.Serialize(payload));
        }

        static object Get(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.ContainsKey(key)) return null;
            return data[key];
        }

        static string ToText(object value)
        {
            return value == null ? "" : Convert.ToString(value);
        }
    }
}
