# 外卖打印系统

轻量级 Windows 桌面程序，用于读取并展示 Rocket Now Manager 订单。当前版本为 `0.1.0`。

## 0.1.0 可以做什么

- 点击“刷新订单”后打开 Rocket Now Manager 官方页面。
- 未登录或登录过期时，可以直接在官方页面完成登录。
- 使用 WebView2 保存 Rocket 登录会话，并支持 Edge 的密码保存和自动填充。
- 读取 Rocket Manager 当前订单页面实际返回的订单数据。
- 显示订单号、下单时间、状态、主要商品和金额。
- 主要商品采用 Rocket 页面风格的摘要，例如 `ヨンジーガムロ(楊枝甘露)の他5点`。
- 点击“详情”可查看订单中的完整商品、选项和数量。
- 以单个 Windows EXE 发布，无需解压其他文件。

## 0.1.0 已知限制

- **不能在 App 中指定 Rocket 店铺。** 读取结果取决于 Rocket Manager 页面当前选择的店铺。
- **不能在 App 中指定 Rocket 查询日期。** 当前 UI 上的日期不会改变 Rocket Manager 页面的查询范围。
- 读取的是 Rocket 当前订单页面请求返回的数据，因此会受到 Rocket 页面筛选条件和分页范围影响。
- 当前版本不包含订单打印、自动打印或打印机设置功能。
- 刷新时必须显示并加载 Rocket Manager 窗口，暂不支持完全隐藏的后台读取。

## 使用方法

1. 运行 `FoodDeliveryPrintingSystem-0.1.0.exe`。
2. 点击右上角“刷新订单”。
3. 程序会打开 Rocket Now Manager。
4. 如未登录，请在 Rocket 官方页面登录。
5. 等待 Rocket 订单页面加载，程序会读取该页面返回的订单并显示在主界面。

如果需要指定店铺或日期，请先在 Rocket Manager 页面中调整相应筛选条件，再让页面重新查询。

## 技术方案

程序使用 Windows 原生 .NET Framework 和 Microsoft Edge WebView2，不捆绑 Electron、Chromium、Node.js 或 `node_modules`。

## 构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

构建脚本会按需下载微软官方 WebView2 SDK，并生成：

```text
dist\FoodDeliveryPrintingSystem-0.1.0.exe
```

## 数据安全

- 登录会话只保存在本机 WebView2 用户目录。
- Cookie、登录令牌、请求调试文件和订单数据不会提交到 GitHub。
- 请勿将包含 Cookie、`unify-token` 或其他登录令牌的 F12 请求信息公开分享。
