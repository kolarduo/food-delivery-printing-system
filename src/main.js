const { app, BrowserWindow, ipcMain, dialog } = require('electron');
const fs = require('node:fs/promises');
const path = require('node:path');

const LOGIN_URL = 'https://store.rocketnow.co.jp/merchant/login';
const ORDERS_URL = 'https://store.rocketnow.co.jp/merchant/management/orders';
const DATA_DIR = path.join(app.getPath('userData'), 'captured-orders');
let mainWindow;
let rocketWindow;
let capturedPayloads = [];
let lastCaptureAt = null;
let loginPrompted = false;
let captureSequence = 0;
let captureWaiters = [];
const pendingResponses = new Map();

function isOrderApi(url) {
  const value = url.toLowerCase();
  return value.includes('/api/') && !value.includes('/api/v3/web/submit') &&
    (value.includes('order') || value.includes('multiplestore'));
}

function sendStatus(message, kind = 'info') {
  mainWindow?.webContents.send('status', { message, kind, lastCaptureAt });
}

function scalar(obj, names) {
  if (!obj || typeof obj !== 'object') return '';
  const wanted = names.map((x) => x.toLowerCase());
  for (const [key, value] of Object.entries(obj)) {
    if (wanted.includes(key.toLowerCase()) && ['string', 'number'].includes(typeof value)) return String(value);
  }
  return '';
}

function looksLikeOrder(obj) {
  if (!obj || Array.isArray(obj) || typeof obj !== 'object') return false;
  const keys = Object.keys(obj).map((x) => x.toLowerCase());
  return keys.some((x) => /order.?id|order.?no|order.?number/.test(x)) ||
    (keys.some((x) => /status|state/.test(x)) && keys.some((x) => /amount|price|total|store/.test(x)));
}

function collectOrders(value, output = [], seen = new Set()) {
  if (!value || typeof value !== 'object' || seen.has(value)) return output;
  seen.add(value);
  if (looksLikeOrder(value)) output.push(value);
  else for (const child of Array.isArray(value) ? value : Object.values(value)) collectOrders(child, output, seen);
  return output;
}

function normalizeOrder(order, source) {
  const dateValue = order.createdAt ?? order.created_at ?? order.orderDate ?? order.order_date ??
    order.orderedAt ?? order.ordered_at ?? order.date ?? order.createdDate ?? '';
  const storeName = order.store?.storeName ?? order.store?.store_name ??
    scalar(order, ['storeName', 'store_name', 'branchName', 'branch_name', 'restaurantName', 'restaurant_name', 'merchantName']);
  const displayId = order.abbrOrderId ?? order.abbr_order_id ?? order.uniqueOrderId ??
    scalar(order, ['orderNo', 'order_no', 'orderNumber', 'order_number', 'orderId', 'order_id', 'id']);
  const itemSummary = Array.isArray(order.items)
    ? order.items.map((item) => `${item.name || '商品'} ×${item.quantity || 1}`).join('、')
    : '';
  return {
    id: String(displayId || ''),
    store: String(storeName || ''),
    date: dateValue,
    status: scalar(order, ['statusName', 'status_name', 'orderStatus', 'order_status', 'status', 'state']),
    customer: scalar(order, ['customerName', 'customer_name', 'userName', 'user_name', 'recipientName']),
    amount: order.totalAmount ?? order.total_amount ?? order.totalPrice ?? order.total_price ?? order.amount ?? order.price ?? '',
    items: itemSummary,
    note: String(order.note || ''),
    source,
    raw: order,
  };
}

function currentOrders() {
  const unique = new Map();
  for (const payload of capturedPayloads) {
    for (const order of collectOrders(payload.data)) {
      const item = normalizeOrder(order, payload.url);
      const key = item.id || JSON.stringify(order);
      unique.set(key, item);
    }
  }
  return [...unique.values()].filter((order) => /川崎|kawasaki/i.test(order.store));
}

async function saveCapture(url, data) {
  await fs.mkdir(DATA_DIR, { recursive: true });
  const now = new Date();
  const file = path.join(DATA_DIR, `${now.toISOString().replace(/[:.]/g, '-')}.json`);
  await fs.writeFile(file, JSON.stringify({ capturedAt: now.toISOString(), url, data }, null, 2), 'utf8');
}

async function captureResponse(details) {
  if (!isOrderApi(details.url) || details.statusCode < 200 || details.statusCode >= 300) return;
  try {
    const body = await rocketWindow.webContents.debugger.sendCommand('Network.getResponseBody', { requestId: details.id });
    const data = JSON.parse(body.body);
    capturedPayloads = [...capturedPayloads.slice(-19), { url: details.url, data }];
    lastCaptureAt = new Date().toISOString();
    captureSequence += 1;
    for (const resolve of captureWaiters.splice(0)) resolve(true);
    await saveCapture(details.url, data);
    mainWindow?.webContents.send('orders', currentOrders());
    sendStatus(`已读取 ${currentOrders().length} 条订单`, 'ok');
    if (loginPrompted && rocketWindow && !rocketWindow.isDestroyed()) {
      loginPrompted = false;
      rocketWindow.hide();
      mainWindow?.show();
      mainWindow?.focus();
    }
  } catch {
    // Some cached/streamed responses cannot be retrieved by the debugger.
  }
}

async function ensureRocketWindow(show = false) {
  if (rocketWindow && !rocketWindow.isDestroyed()) {
    if (show) { rocketWindow.show(); rocketWindow.focus(); }
    return rocketWindow;
  }
  rocketWindow = new BrowserWindow({
    width: 1280, height: 850, show,
    title: 'Rocket Manager 登录 / 订单页面',
    webPreferences: { partition: 'persist:rocket-manager', contextIsolation: true },
  });
  try { rocketWindow.webContents.debugger.attach('1.3'); } catch {}
  try { await rocketWindow.webContents.debugger.sendCommand('Network.enable'); } catch {}
  rocketWindow.webContents.debugger.on('message', (_event, method, params) => {
    if (method === 'Network.responseReceived' && isOrderApi(params.response.url)) {
      pendingResponses.set(params.requestId, { ...params.response, id: params.requestId });
    }
    if (method === 'Network.loadingFinished' && pendingResponses.has(params.requestId)) {
      const details = pendingResponses.get(params.requestId);
      pendingResponses.delete(params.requestId);
      captureResponse(details);
    }
    if (method === 'Network.loadingFailed') pendingResponses.delete(params.requestId);
  });
  const handleNavigation = (_event, url) => {
    if (/\/merchant\/login|\/login(?:[/?#]|$)/i.test(url)) {
      showLoginWindow();
      return;
    }
    if (loginPrompted && url.startsWith('https://store.rocketnow.co.jp') && !url.includes('/merchant/management/orders')) {
      setTimeout(() => {
        if (rocketWindow && !rocketWindow.isDestroyed() && loginPrompted) {
          rocketWindow.loadURL(ORDERS_URL).catch(() => showLoginWindow());
        }
      }, 1200);
    }
  };
  rocketWindow.webContents.on('did-navigate', handleNavigation);
  rocketWindow.webContents.on('did-navigate-in-page', handleNavigation);
  rocketWindow.on('closed', () => { rocketWindow = null; });
  return rocketWindow;
}

function showLoginWindow() {
  if (!rocketWindow || rocketWindow.isDestroyed()) return;
  loginPrompted = true;
  rocketWindow.show();
  rocketWindow.focus();
  sendStatus('无法读取订单。请在弹出的 Rocket Manager 官方窗口中登录。', 'warn');
  for (const resolve of loginWaiters.splice(0)) resolve(false);
}

let loginWaiters = [];

function waitForOrderCapture(sequence, timeoutMs) {
  if (captureSequence > sequence) return Promise.resolve(true);
  return new Promise((resolve) => {
    const done = (value) => {
      clearTimeout(timer);
      const index = captureWaiters.indexOf(done);
      if (index >= 0) captureWaiters.splice(index, 1);
      const loginIndex = loginWaiters.indexOf(done);
      if (loginIndex >= 0) loginWaiters.splice(loginIndex, 1);
      resolve(value);
    };
    const timer = setTimeout(() => done(false), timeoutMs);
    captureWaiters.push(done);
    loginWaiters.push(done);
  });
}

async function checkLogin() {
  sendStatus('正在从 Rocket Manager 读取订单并检查登录状态…');
  const win = await ensureRocketWindow(false);
  const before = captureSequence;
  win.loadURL(ORDERS_URL).catch(() => showLoginWindow());
  const captured = await waitForOrderCapture(before, 8000);
  if (!captured) {
    showLoginWindow();
    return false;
  }
  sendStatus(`Rocket Manager 登录有效，已读取 ${currentOrders().length} 条订单`, 'ok');
  return true;
}

function createMainWindow() {
  mainWindow = new BrowserWindow({
    width: 1180, height: 760, minWidth: 900, minHeight: 600,
    title: 'Rocket 订单管理',
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true },
  });
  mainWindow.loadFile(path.join(__dirname, 'renderer', 'index.html'));
  mainWindow.once('ready-to-show', () => {
    sendStatus('点击右上角“刷新订单”开始读取 Kawasaki 店订单。');
  });
}

ipcMain.handle('get-orders', () => currentOrders());
ipcMain.handle('refresh-orders', async () => {
  return checkLogin();
});
ipcMain.handle('get-status', () => ({ lastCaptureAt, count: currentOrders().length }));

app.whenReady().then(createMainWindow);
app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit(); });
app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createMainWindow(); });
