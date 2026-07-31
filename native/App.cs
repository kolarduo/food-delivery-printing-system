using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
        const int MaximumPages = 1000;
        readonly WebView2 ui = new WebView2();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        readonly Dictionary<string, Dictionary<string, object>> collectedOrders =
            new Dictionary<string, Dictionary<string, object>>();
        readonly HashSet<int> processedPages = new HashSet<int>();
        readonly Timer finishTimer = new Timer();
        readonly Timer pageTimeoutTimer = new Timer();
        CoreWebView2Environment environment;
        Form rocketForm;
        WebView2 rocket;
        bool refreshing;
        bool navigatingToOrders;
        bool pagingFinished;
        bool partialResult;
        int scannedOrderCount;
        int observedPageSize;

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
            pageTimeoutTimer.Interval = 12000;
            pageTimeoutTimer.Tick += PageTimeoutTimerTick;
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
                finishTimer.Stop();
                pageTimeoutTimer.Stop();
                pagingFinished = true;
                refreshing = false;
                Show();
                Activate();
                SendStatus("Rocket Manager 窗口已关闭。", "warn");
            };

            rocket = new WebView2();
            rocket.Dock = DockStyle.Fill;
            rocketForm.Controls.Add(rocket);
            rocketForm.Show();
            rocketForm.Activate();
            await rocket.EnsureCoreWebView2Async(environment);
            rocket.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
            rocket.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
            rocket.CoreWebView2.WebResourceResponseReceived += RocketResponseReceived;
            rocket.CoreWebView2.NavigationCompleted += RocketNavigationCompleted;
        }

        async Task RefreshOrdersAsync()
        {
            if (refreshing)
            {
                return;
            }

            refreshing = true;
            collectedOrders.Clear();
            processedPages.Clear();
            pagingFinished = false;
            partialResult = false;
            scannedOrderCount = 0;
            observedPageSize = 0;
            finishTimer.Stop();
            pageTimeoutTimer.Stop();
            SendStatus("正在打开 Rocket Manager 并读取默认范围的全部订单…", "info");
            await EnsureRocketAsync();
            rocketForm.Show();
            rocketForm.WindowState = FormWindowState.Normal;
            rocketForm.Activate();
            navigatingToOrders = true;
            rocket.CoreWebView2.Navigate(OrdersUrl);
            pageTimeoutTimer.Start();
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
                SendStatus("Rocket Manager 页面打开失败，请检查网络后重试。", "warn");
                partialResult = true;
                FinishPaging();
                return;
            }

            string url = rocket.Source == null ? "" : rocket.Source.ToString();
            if (url.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                pageTimeoutTimer.Stop();
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
                if (refreshing && processedPages.Count == 0)
                {
                    pageTimeoutTimer.Stop();
                    pageTimeoutTimer.Start();
                }
            }
        }

        async void RocketResponseReceived(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (!refreshing || e.Request.Uri.IndexOf(OrderApiPart,
                StringComparison.OrdinalIgnoreCase) < 0) return;
            try
            {
                if (e.Response.StatusCode < 200 || e.Response.StatusCode >= 300)
                {
                    SendStatus("Rocket 订单请求失败（HTTP " +
                        e.Response.StatusCode + "）。", "warn");
                    partialResult = true;
                    FinishPaging();
                    return;
                }
                Stream stream = await e.Response.GetContentAsync();
                string body;
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    body = await reader.ReadToEndAsync();
                await ProcessOrderPageAsync(body);
            }
            catch (Exception ex)
            {
                SendStatus("Rocket 页面响应读取失败：" + ex.Message, "warn");
                partialResult = true;
                FinishPaging();
            }
        }

        async Task ProcessOrderPageAsync(string body)
        {
            Dictionary<string, object> root =
                json.DeserializeObject(body) as Dictionary<string, object>;
            if (root == null || pagingFinished) return;

            int pageNumber = ToInt(Get(root, "pageNumber"), processedPages.Count);
            if (processedPages.Contains(pageNumber)) return;
            pageTimeoutTimer.Stop();
            processedPages.Add(pageNumber);

            IEnumerable content = FindOrderContent(root);
            int pageOrderCount = 0;
            if (content != null)
            {
                foreach (object value in content)
                {
                    Dictionary<string, object> order = value as Dictionary<string, object>;
                    if (order == null) continue;
                    pageOrderCount++;

                    Dictionary<string, object> normalized = Normalize(order);
                    string key = ToText(Get(order, "uniqueOrderId"));
                    if (String.IsNullOrEmpty(key)) key = ToText(Get(order, "orderId"));
                    if (String.IsNullOrEmpty(key)) key = ToText(Get(normalized, "id"));
                    if (!String.IsNullOrEmpty(key)) collectedOrders[key] = normalized;
                }
            }

            scannedOrderCount += pageOrderCount;
            if (pageOrderCount > observedPageSize) observedPageSize = pageOrderCount;

            PublishCollectedOrders();
            int totalElements = ToInt(Get(root, "totalElements"),
                ToInt(Get(root, "total"), 0));
            int lastPageNumber = ToInt(Get(root, "lastPageNumber"), -1);
            bool reachedReportedLastPage =
                lastPageNumber >= 0 && pageNumber >= lastPageNumber;
            bool reachedTotal = totalElements > 0 &&
                scannedOrderCount >= totalElements;
            bool shortPage = observedPageSize > 0 &&
                pageOrderCount < observedPageSize;
            bool reachedSafetyLimit = processedPages.Count >= MaximumPages;
            bool hasMore = pageOrderCount > 0 && !reachedReportedLastPage &&
                !reachedTotal && !shortPage && !reachedSafetyLimit;

            if (!hasMore)
            {
                if (reachedSafetyLimit)
                {
                    partialResult = true;
                    SendStatus("已达到分页安全上限，保留当前读取结果。", "warn");
                }
                FinishPaging();
                return;
            }

            SendStatus("正在读取第 " + (pageNumber + 2) + " 页，已读取 " +
                collectedOrders.Count + " 条…", "info");
            await Task.Delay(650);
            bool clicked = await ClickNextPageWithRetryAsync();
            if (!clicked)
            {
                partialResult = true;
                SendStatus("无法继续翻页，已保留当前读取结果。", "warn");
                FinishPaging();
            }
            else
            {
                if (!pagingFinished)
                {
                    pageTimeoutTimer.Stop();
                    pageTimeoutTimer.Start();
                }
            }
        }

        async Task<bool> ClickNextPageWithRetryAsync()
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                string result = await rocket.CoreWebView2.ExecuteScriptAsync(
                    "(()=>{" +
                    "const usable=e=>{if(!e)return false;const s=getComputedStyle(e);" +
                    "return !e.disabled&&e.getAttribute('aria-disabled')!=='true'&&" +
                    "s.display!=='none'&&s.visibility!=='hidden'&&e.offsetParent!==null&&" +
                    "!/disabled|hide-btn/.test(String(e.className));};" +
                    "const selectors=['button.pagination-btn.next-btn'," +
                    "'button[aria-label*=next i]','button[aria-label*=\\u6b21]'," +
                    "'button[title*=next i]','button[title*=\\u6b21]'," +
                    "'[role=button][aria-label*=next i]'," +
                    "'[role=button][aria-label*=\\u6b21]'];" +
                    "let b=null;for(const q of selectors){const e=document.querySelector(q);" +
                    "if(usable(e)){b=e;break;}}" +
                    "if(!b){b=[...document.querySelectorAll('button,[role=button]')].find(e=>{" +
                    "if(!usable(e))return false;const t=(e.textContent||'').trim();" +
                    "const c=String(e.className);return /^(next|>|\\u203a|\\u00bb|\\u6b21|\\u6b21\\u3078|\\u6b21\\u306e\\u30da\\u30fc\\u30b8)$/i.test(t)||" +
                    "/(^|[-_ ])next([-_ ]|$)/i.test(c);});}" +
                    "if(!b)return 0;b.scrollIntoView({block:'center'});b.click();return 1;})()");
                if (String.Equals(result, "1", StringComparison.Ordinal)) return true;
                await Task.Delay(500);
            }
            return false;
        }

        void PageTimeoutTimerTick(object sender, EventArgs e)
        {
            pageTimeoutTimer.Stop();
            if (!refreshing || pagingFinished) return;
            SendStatus(processedPages.Count == 0
                ? "订单数据等待超时，请确认 Rocket 页面已正常加载。"
                : "下一页等待超时，已保留当前读取结果。", "warn");
            partialResult = true;
            FinishPaging();
        }

        void PublishCollectedOrders()
        {
            List<Dictionary<string, object>> orders =
                new List<Dictionary<string, object>>(collectedOrders.Values);
            orders.Sort(delegate(Dictionary<string, object> left,
                Dictionary<string, object> right) {
                return ToLong(Get(right, "date")).CompareTo(ToLong(Get(left, "date")));
            });
            Dictionary<string, object> message = new Dictionary<string, object>();
            message["type"] = "orders";
            message["orders"] = orders;
            message["capturedAt"] = DateTime.UtcNow.ToString("o");
            ui.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));
        }

        void FinishPaging()
        {
            if (pagingFinished) return;
            pagingFinished = true;
            pageTimeoutTimer.Stop();
            PublishCollectedOrders();
            finishTimer.Stop();
            finishTimer.Start();
        }

        void FinishTimerTick(object sender, EventArgs e)
        {
            finishTimer.Stop();
            pageTimeoutTimer.Stop();
            refreshing = false;
            if (rocketForm != null) rocketForm.Hide();
            Show();
            Activate();
            if (partialResult)
                SendStatus("读取未完成，已保留 " + collectedOrders.Count + " 条订单，请重试。", "warn");
            else
                SendStatus("已读取默认范围全部 " + collectedOrders.Count + " 条订单", "ok");
        }

        Dictionary<string, object> Normalize(Dictionary<string, object> order)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["id"] = ToText(Get(order, "abbrOrderId"));
            result["date"] = Get(order, "createdAt");
            result["status"] = ToText(Get(order, "status"));
            result["amount"] = Get(order, "totalAmount");
            Dictionary<string, object> store =
                Get(order, "store") as Dictionary<string, object>;
            result["storeId"] = Get(order, "storeId") ?? Get(store, "storeId");
            string storeName = ToText(Get(order, "storeName"));
            if (String.IsNullOrEmpty(storeName)) storeName = ToText(Get(store, "storeName"));
            result["storeName"] = storeName;
            result["items"] = ItemSummary(Get(order, "items") as IEnumerable);
            result["detailItems"] = DetailItems(Get(order, "items") as IEnumerable);
            result["note"] = ToText(Get(order, "note"));
            return result;
        }

        string ItemSummary(IEnumerable items)
        {
            if (items == null) return "";
            string firstName = "";
            int itemCount = 0;
            foreach (object value in items)
            {
                Dictionary<string, object> item = value as Dictionary<string, object>;
                if (item == null) continue;
                string name = ToText(Get(item, "name"));
                if (String.IsNullOrEmpty(name)) continue;
                if (itemCount == 0) firstName = name;
                itemCount++;
            }
            if (itemCount <= 1) return firstName;
            return firstName + "の他" + (itemCount - 1) + "点";
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

        static int ToInt(object value, int fallback)
        {
            if (value == null) return fallback;
            int result;
            return Int32.TryParse(Convert.ToString(value), out result) ? result : fallback;
        }

        static long ToLong(object value)
        {
            if (value == null) return 0;
            long result;
            return Int64.TryParse(Convert.ToString(value), out result) ? result : 0;
        }
    }
}
