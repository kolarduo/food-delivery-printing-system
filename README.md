# 外卖打印系统

Rocket Now Manager 订单读取与管理桌面程序。当前版本为 `0.1.0`。

## 当前功能

- 独立 Windows 桌面 UI
- 按日期和店铺筛选订单
- 点击“刷新订单”后从 Rocket Manager 读取订单
- 登录状态失效时打开 Rocket 官方登录窗口
- 登录成功后自动继续读取订单
- 显示订单号、日期、店铺、状态、商品、金额和原始详情
- 在本机保存 Rocket 登录会话，不保存明文账号密码

标签打印功能将在后续版本中加入。

## 本地运行

电脑需要安装 Node.js。

```powershell
npm install
npm start
```

## 生成 Windows EXE

```powershell
npm run dist
```

输出文件位于 `dist` 目录。

## 数据安全

登录会话、订单接口响应、依赖目录、构建产物以及包含请求 Cookie 或令牌的调试文本均已加入 `.gitignore`。不要把登录 Cookie、`unify-token` 或订单原始数据提交到仓库。
