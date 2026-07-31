# Rocket Now 川崎店订单监控（接口验证版）

这一版会打开独立的 Chrome 窗口。请直接在 Rocket Now 官方网页中手动输入账号和密码。程序不接收账号密码，也没有账号密码输入框。

登录成功后，选择川崎店并进入订单页面。程序会监听网页已经发出的订单接口，并把 JSON 保存到 `captured-orders`，用于开发实时订单列表和标签打印。

## 启动

电脑需要安装 Node.js 和 Google Chrome。

```powershell
cd rocket-monitor
npm install
npm start
```

首次登录后的会话保存在本机 `.rocket-profile` 中，因此通常不需要每次重新登录。这个目录包含敏感的登录会话，已经加入 `.gitignore`，不要发送给别人。

## 当前功能

- 弹出 Rocket Now 官方登录网页
- 用户在官方网页手动登录
- 保留本机登录会话
- 自动识别并保存订单相关 API 响应
- 排除 `api/v3/web/submit` 日志上报接口

下一步将根据捕获到的真实订单 JSON，加入川崎店筛选、新订单去重、实时列表和标签打印。
