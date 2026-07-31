# 外卖打印系统

轻量 Windows 桌面程序，读取 Rocket Now Manager 的 Kawasaki 店订单。当前版本为 `0.2.0`。

## 技术方案

程序使用 Windows 原生 .NET Framework 和系统已安装的 Microsoft Edge WebView2。发布包不再捆绑 Electron、Chromium、Node.js 或 `node_modules`，因此体积从约 80MB 降至约 1–2MB。

## 当前功能

- 保留原有 HTML/CSS 桌面 UI
- 按日期筛选 Kawasaki 店订单
- 点击“刷新订单”立即打开 Rocket Now 官方页面
- 未登录时直接在官方页面输入账号密码
- 登录成功后读取真实 `queryByPage` 订单接口
- 显示订单号、日期、状态、商品、金额和原始详情
- 登录会话仅保存在本机 WebView2 用户目录

标签打印功能将在后续版本中加入。

## 构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

构建脚本会按需下载微软官方 WebView2 SDK，并在 `dist-lite\food-delivery-printing-system-0.2.0` 生成轻量发布包。

## 数据安全

请求调试文件、Cookie、登录令牌、订单数据、SDK 文件和构建产物均不会提交到 GitHub。
