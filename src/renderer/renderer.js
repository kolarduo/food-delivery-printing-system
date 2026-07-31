const $ = (id) => document.getElementById(id);
let orders = [];
let stores = new Map();
let selectedStores = new Set();
let storeSelectionChanged = false;
const DEFAULT_STORE_KEYWORD = '川崎';

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
  $('total').textContent = orders.length;
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

$('refresh').onclick = () => {
  $('status').textContent = '正在打开 Rocket Manager…';
  window.chrome.webview.postMessage({ type: 'refresh', date: $('date').value });
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

window.chrome.webview.addEventListener('message', (event) => {
  const message = event.data;
  if (message.type === 'orders') {
    orders = message.orders || [];
    syncStores();
    $('last').textContent = localDate(message.capturedAt);
    render();
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
setLoading(false);
render();
