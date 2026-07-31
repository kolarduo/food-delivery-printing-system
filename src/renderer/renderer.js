const $ = (id) => document.getElementById(id);
let orders = [];
let stores = new Map();
let selectedStores = new Set();
let storeSelectionChanged = false;
let currentDetailOrder = null;
const DEFAULT_STORE_KEYWORD = '川崎';
const ORDER_CACHE_KEY = 'rocketOrderCacheV1';

function todayKey() {
  return dateKey(Date.now());
}

function localDate(value) {
  if (!value) return '';
  const d = new Date(Number(value) || value);
  return Number.isNaN(d.valueOf()) ? String(value) : d.toLocaleString('zh-CN', {
    timeZone: 'Asia/Tokyo',
    hour12: false,
  });
}

function dateKey(value) {
  const d = new Date(Number(value) || value);
  if (Number.isNaN(d.valueOf())) return String(value).slice(0, 10);
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'Asia/Tokyo',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(d);
  const values = Object.fromEntries(parts.map((part) => [part.type, part.value]));
  return `${values.year}-${values.month}-${values.day}`;
}

function storeInfo(order) {
  const id = String(order.storeId ?? '').trim();
  const name = String(order.storeName ?? '').trim();
  return {
    key: id ? `id:${id}` : name ? `name:${name}` : 'unknown',
    label: name || (id ? `店铺 ${id}` : '未知店铺'),
  };
}

function orderKey(order) {
  return String(order.cacheKey || order.id || `${order.date || ''}:${order.items || ''}`).trim();
}

function sortOrders(values) {
  return values.sort((left, right) => {
    const leftTime = Number(left.date) || new Date(left.date).valueOf() || 0;
    const rightTime = Number(right.date) || new Date(right.date).valueOf() || 0;
    return rightTime - leftTime;
  });
}

function loadCachedOrders() {
  try {
    const cached = JSON.parse(localStorage.getItem(ORDER_CACHE_KEY) || '[]');
    return Array.isArray(cached) ? cached : [];
  } catch {
    return [];
  }
}

function saveCachedOrders(values) {
  try {
    localStorage.setItem(ORDER_CACHE_KEY, JSON.stringify(sortOrders(values).slice(0, 1000)));
  } catch {
    // If localStorage is full, the current screen can still show the merged data.
  }
}

function mergeOrders(primary, secondary) {
  const map = new Map();
  for (const order of [...secondary, ...primary]) {
    const key = orderKey(order);
    if (key) map.set(key, order);
  }
  return sortOrders([...map.values()]);
}

function setOrders(nextOrders, shouldCache) {
  orders = sortOrders(nextOrders);
  if (shouldCache) saveCachedOrders(orders);
  syncStores();
  render();
}

function syncStores() {
  const previousKeys = new Set(stores.keys());
  const previouslyAllSelected = previousKeys.size === 0 ||
    [...previousKeys].every((key) => selectedStores.has(key));
  const nextStores = new Map();

  for (const order of orders) {
    const store = storeInfo(order);
    nextStores.set(store.key, store.label);
  }

  stores = new Map([...nextStores.entries()].sort((left, right) =>
    left[1].localeCompare(right[1], 'zh-CN')));

  if (!storeSelectionChanged) {
    const defaultStores = [...stores].filter(([, label]) =>
      label.includes(DEFAULT_STORE_KEYWORD)).map(([key]) => key);
    selectedStores = new Set(defaultStores.length ? defaultStores : stores.keys());
  } else if (previouslyAllSelected) {
    selectedStores = new Set(stores.keys());
  } else {
    selectedStores = new Set([...selectedStores].filter((key) => stores.has(key)));
  }

  renderStoreOptions();
}

function renderStoreOptions() {
  const options = $('storeOptions');
  options.replaceChildren();

  for (const [key, label] of stores) {
    const choice = document.createElement('label');
    choice.className = 'storeChoice';
    const checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    checkbox.checked = selectedStores.has(key);
    checkbox.dataset.storeKey = key;
    checkbox.onchange = () => {
      storeSelectionChanged = true;
      if (checkbox.checked) selectedStores.add(key);
      else selectedStores.delete(key);
      updateStoreControls();
      render();
    };
    const name = document.createElement('span');
    name.textContent = label;
    choice.append(checkbox, name);
    options.appendChild(choice);
  }

  $('storeEmpty').hidden = stores.size > 0;
  updateStoreControls();
}

function updateStoreControls() {
  const allSelected = stores.size > 0 && selectedStores.size === stores.size;
  const all = $('storeAll');
  all.checked = allSelected;
  all.indeterminate = selectedStores.size > 0 && !allSelected;

  for (const checkbox of $('storeOptions').querySelectorAll('input[type="checkbox"]')) {
    checkbox.checked = selectedStores.has(checkbox.dataset.storeKey);
  }

  if (!stores.size) $('storeSummary').textContent = '川崎店';
  else if (allSelected) $('storeSummary').textContent = `全店铺（${stores.size}）`;
  else if (!selectedStores.size) $('storeSummary').textContent = '未选择店铺';
  else if (selectedStores.size === 1) {
    const key = [...selectedStores][0];
    $('storeSummary').textContent = stores.get(key) || '已选 1 家店铺';
  } else $('storeSummary').textContent = `已选 ${selectedStores.size} 家店铺`;
}

function setLoading(isLoading) {
  $('loadingOverlay').hidden = !isLoading;
  $('refresh').disabled = isLoading;
}

function render() {
  const selectedDate = $('date').value;
  const filtered = orders.filter((order) => {
    const matchesDate = !selectedDate || dateKey(order.date) === selectedDate;
    const matchesStore = selectedStores.has(storeInfo(order).key);
    return matchesDate && matchesStore;
  });

  $('count').textContent = filtered.length;
  $('empty').style.display = filtered.length ? 'none' : 'block';
  $('empty').textContent = orders.length
    ? '没有符合当前日期和店铺条件的订单。'
    : '尚未读取订单。点击右上角“刷新订单”开始读取。';
  $('rows').replaceChildren();

  for (const order of filtered) {
    const row = document.createElement('tr');
    const amount = order.amount === '' || order.amount == null
      ? '—' : `¥${Number(order.amount).toLocaleString('ja-JP')}`;
    const values = [
      order.id || '—', localDate(order.date) || '—', order.status || '—',
      order.items || '—', amount,
    ];
    for (const value of values) {
      const cell = document.createElement('td');
      cell.textContent = value;
      row.appendChild(cell);
    }
    const actionCell = document.createElement('td');
    const details = document.createElement('button');
    details.textContent = '详情';
    details.onclick = () => showDetails(order);
    actionCell.appendChild(details);
    row.appendChild(actionCell);
    $('rows').appendChild(row);
  }
}

function showDetails(order) {
  currentDetailOrder = order;
  const list = $('detailList');
  list.replaceChildren();
  for (const item of order.detailItems || []) {
    const group = document.createElement('section');
    group.className = 'itemGroup';
    const title = document.createElement('h3');
    title.textContent = `${item.name || '商品'} ×${item.quantity || 1}`;
    group.appendChild(title);
    for (const option of item.options || []) {
      const row = document.createElement('div');
      row.className = 'optionRow';
      row.textContent = option.quantity > 1
        ? `${option.name} ×${option.quantity}` : option.name;
      group.appendChild(row);
    }
    list.appendChild(group);
  }
  if (!list.children.length) list.textContent = '没有商品明细';
  $('details').showModal();
}

function appendText(parent, tagName, className, text) {
  const element = document.createElement(tagName);
  element.className = className;
  element.textContent = text;
  parent.appendChild(element);
  return element;
}

function preparePrintLabel(order) {
  const area = $('printArea');
  area.replaceChildren();
  const label = document.createElement('article');
  label.className = 'labelTicket';

  const meta = document.createElement('div');
  meta.className = 'labelMeta';
  appendText(meta, 'div', 'labelMetaLine', order.id ? `注文番号 ${order.id}` : '注文番号');
  appendText(meta, 'div', 'labelMetaLine', `注文時間 ${localDate(order.date)}`);
  label.appendChild(meta);

  for (const item of order.detailItems || []) {
    const block = document.createElement('section');
    block.className = 'labelItem';
    appendText(block, 'div', 'labelItemName', `${item.name || '商品'} ×${item.quantity || 1}`);
    for (const option of item.options || []) {
      const name = option.quantity > 1
        ? `${option.name} ×${option.quantity}` : option.name;
      appendText(block, 'div', 'labelOption', name || '');
    }
    label.appendChild(block);
  }

  if (!(order.detailItems || []).length) {
    appendText(label, 'div', 'labelItemName', '没有商品明细');
  }

  area.appendChild(label);
}

function printCurrentOrder() {
  if (!currentDetailOrder) return;
  preparePrintLabel(currentDetailOrder);
  window.print();
}

$('refresh').onclick = () => {
  $('status').textContent = '正在打开 Rocket Manager…';
  const knownOrderKeys = loadCachedOrders().map(orderKey).filter(Boolean);
  window.chrome.webview.postMessage({
    type: 'refresh',
    date: $('date').value,
    knownOrderKeys,
  });
};
$('date').onchange = render;
$('storeToggle').onclick = () => {
  const menu = $('storeMenu');
  menu.hidden = !menu.hidden;
  $('storeToggle').setAttribute('aria-expanded', String(!menu.hidden));
};
$('storeAll').onchange = () => {
  storeSelectionChanged = true;
  selectedStores = $('storeAll').checked ? new Set(stores.keys()) : new Set();
  updateStoreControls();
  render();
};
document.addEventListener('click', (event) => {
  if (event.target.closest('.storeFilter')) return;
  $('storeMenu').hidden = true;
  $('storeToggle').setAttribute('aria-expanded', 'false');
});
$('close').onclick = () => $('details').close();
$('print').onclick = printCurrentOrder;

window.chrome.webview.addEventListener('message', (event) => {
  const message = event.data;
  if (message.type === 'orders') {
    setOrders(mergeOrders(message.orders || [], loadCachedOrders()), true);
    $('last').textContent = localDate(message.capturedAt);
  }
  if (message.type === 'status') {
    $('status').textContent = message.message;
    $('status').className = `status-${message.kind || 'info'}`;
  }
  if (message.type === 'loading') {
    setLoading(Boolean(message.loading));
  }
});

$('date').value = todayKey();
setOrders(loadCachedOrders(), false);
setLoading(false);
