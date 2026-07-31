# 外卖打印系统

轻量级 Windows 桌面程序，用于读取、展示并打印 Rocket Now Manager 订单。当前版本为 `0.2.0`。

## 0.2.0 可以做什么

- 点击“刷新订单”后打开 Rocket Now Manager 官方页面。
- 未登录或登录过期时，可直接在官方页面完成登录。
- 使用 WebView2 保存 Rocket 登录会话，并支持 Edge 的密码保存和自动填充。
- 读取 Rocket Manager 默认订单查询结果，并自动翻页读取订单。
- 按所选日期提前停止读取：读到早于所选日期的订单后，不再继续翻页。
- 订单读取完成后，可在软件内按日期筛选。
- 可按店铺筛选，支持“全店铺”以及同时勾选一个或多个店铺。
- 默认筛选川崎店，仍可手动切换到其它店铺或全店铺。
- 切换日期或店铺只筛选已经读到的软件数据，不会重新打开 Rocket Manager。
- 进入订单查询后，Rocket Manager 窗口会最小化，主界面显示“正在查询订单”提示框。
- 显示订单号、下单时间、状态、主要商品和金额。
- 主要商品采用 Rocket 页面风格的摘要，例如 `ヨンジーガムロ(楊枝甘露)の他5点`。
- 点击“详情”可查看订单中的完整商品、选项和数量。
- 在详情窗口点击“打印”，可打开系统打印界面选择打印机或预览。
- 打印格式按窄长标签纸设计：主商品大号粗体，选项/小料小号显示在对应商品下方。
- 以单个 Windows EXE 发布，无需解压其他文件。

## 使用方法

1. 运行 `FoodDeliveryPrintingSystem-0.2.0.exe`。
2. 点击右上角“刷新订单”。
3. 程序会打开 Rocket Now Manager，并按页面默认条件逐页读取订单。
4. 如未登录，请在 Rocket 官方页面登录。
5. 登录后或已登录时，Rocket 窗口会最小化，主界面显示正在查询。
6. 使用“日期”和“店铺”在软件内筛选订单。
7. 点击订单“详情”，再点击“打印”预览或选择打印机。

## 当前限制

- 软件读取的是 Rocket Manager 页面默认查询范围内的数据；超出该范围的订单不会被读取。
- 当前版本支持手动打印单个订单，暂不支持自动打印或批量打印。
- 刷新时仍需要加载 Rocket Manager 页面；登录时会显示 Rocket 窗口，查询时会最小化。

## 技术方案

程序使用 Windows 原生 .NET Framework 和 Microsoft Edge WebView2，不捆绑 Electron、Chromium、Node.js 或 `node_modules`。

## 构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

构建脚本会按需下载微软官方 WebView2 SDK，并生成：

```text
dist\FoodDeliveryPrintingSystem-0.2.0.exe
```

## 数据安全

- 登录会话只保存在本机 WebView2 用户目录。
- Cookie、登录令牌、请求调试文件和订单数据不会提交到 GitHub。
- 请勿将包含 Cookie、`unify-token` 或其他登录令牌的 F12 请求信息公开分享。
