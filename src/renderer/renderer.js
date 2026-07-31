const $ = (id) => document.getElementById(id);
let orders = [];

function todayKey() {
  const now = new Date();
  const offset = now.getTimezoneOffset() * 60000;
  return new Date(now - offset).toISOString().slice(0, 10);
}

function localDate(value) {
  if (!value) return '';
  const d = new Date(Number(value) || value);
  return Number.isNaN(d.valueOf()) ? String(value) : d.toLocaleString('zh-CN');
}

function dateKey(value) {
  const d = new Date(Number(value) || value);
  if (Number.isNaN(d.valueOf())) return String(value).slice(0, 10);
  const offset = d.getTimezoneOffset() * 60000;
  return new Date(d - offset).toISOString().slice(0, 10);
}

function render() {
  const selectedDate = $('date').value;
  const filtered = orders.filter((order) => !selectedDate || dateKey(order.date) === selectedDate);
  $('count').textContent = filtered.length;
  $('empty').style.display = filtered.length ? 'none' : 'block';
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
    details.onclick = () => {
      $('json').textContent = JSON.stringify(order.raw, null, 2);
      $('details').showModal();
    };
    actionCell.appendChild(details);
    row.appendChild(actionCell);
    $('rows').appendChild(row);
  }
}

$('refresh').onclick = () => {
  $('status').textContent = '正在打开 Rocket Manager…';
  window.chrome.webview.postMessage({ type: 'refresh' });
};
$('date').onchange = render;
$('close').onclick = () => $('details').close();

window.chrome.webview.addEventListener('message', (event) => {
  const message = event.data;
  if (message.type === 'orders') {
    orders = message.orders || [];
    $('last').textContent = localDate(message.capturedAt);
    render();
  }
  if (message.type === 'status') {
    $('status').textContent = message.message;
    $('status').className = `status-${message.kind || 'info'}`;
  }
});

$('date').value = todayKey();
render();
