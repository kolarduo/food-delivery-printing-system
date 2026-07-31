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
        readonly WebView2 ui = new WebView2();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        CoreWebView2Environment environment;
        Form rocketForm;
        WebView2 rocket;
        bool refreshing;
        bool navigatingToOrders;

        public MainForm()
        {
            Text = "外卖打印系统";
            Width = 1180;
            Height = 760;
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            ui.Dock = DockStyle.Fill;
            Controls.Add(ui);
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
                    await RefreshOrdersAsync();
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
            rocket.CoreWebView2.WebResourceResponseReceived += RocketResponseReceived;
            rocket.CoreWebView2.NavigationCompleted += RocketNavigationCompleted;
        }

        async Task RefreshOrdersAsync()
        {
            if (refreshing)
            {
                if (rocketForm != null) { rocketForm.Show(); rocketForm.Activate(); }
                return;
            }

            refreshing = true;
            SendStatus("正在打开 Rocket Manager…", "info");
            await EnsureRocketAsync();
            rocketForm.Show();
            rocketForm.Activate();
            navigatingToOrders = true;
            rocket.CoreWebView2.Navigate(OrdersUrl);
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
            }
        }

        async void RocketResponseReceived(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (e.Request.Uri.IndexOf(OrderApiPart, StringComparison.OrdinalIgnoreCase) < 0) return;
            try
            {
                CoreWebView2WebResourceResponseView response = e.Response;
                if (response.StatusCode < 200 || response.StatusCode >= 300) return;
                Stream stream = await response.GetContentAsync();
                string body;
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    body = await reader.ReadToEndAsync();

                Dictionary<string, object> root = json.DeserializeObject(body) as Dictionary<string, object>;
                IEnumerable content = FindOrderContent(root);
                List<Dictionary<string, object>> orders = new List<Dictionary<string, object>>();
                if (content != null)
                {
                    foreach (object value in content)
                    {
                        Dictionary<string, object> order = value as Dictionary<string, object>;
                        if (order == null) continue;
                        Dictionary<string, object> store = Get(order, "store") as Dictionary<string, object>;
                        string storeName = ToText(Get(store, "storeName"));
                        if (storeName.IndexOf("川崎", StringComparison.OrdinalIgnoreCase) < 0 &&
                            storeName.IndexOf("kawasaki", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        orders.Add(Normalize(order));
                    }
                }

                Dictionary<string, object> message = new Dictionary<string, object>();
                message["type"] = "orders";
                message["orders"] = orders;
                message["capturedAt"] = DateTime.UtcNow.ToString("o");
                ui.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));
                refreshing = false;
                rocketForm.Hide();
                Show();
                Activate();
                SendStatus("已读取 Kawasaki 店 " + orders.Count + " 条订单", "ok");
            }
            catch (Exception ex)
            {
                refreshing = false;
                SendStatus("订单数据解析失败：" + ex.Message, "warn");
            }
        }

        Dictionary<string, object> Normalize(Dictionary<string, object> order)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["id"] = ToText(Get(order, "abbrOrderId"));
            result["date"] = Get(order, "createdAt");
            result["status"] = ToText(Get(order, "status"));
            result["amount"] = Get(order, "totalAmount");
            result["items"] = ItemSummary(Get(order, "items") as IEnumerable);
            result["note"] = ToText(Get(order, "note"));
            result["raw"] = order;
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
