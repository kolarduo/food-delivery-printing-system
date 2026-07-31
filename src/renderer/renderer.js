const $ = (id) => document.getElementById(id);
let orders = [];

function localDate(value) {
  if (!value) return '';
  const d = new Date(value);
  return Number.isNaN(d.valueOf()) ? value : d.toLocaleString('zh-CN');
}
function dateKey(value) {
  const d = new Date(value);
  if (Number.isNaN(d.valueOf())) return String(value).slice(0, 10);
  const offset = d.getTimezoneOffset() * 60000;
  return new Date(d - offset).toISOString().slice(0, 10);
}
function render() {
  const selectedDate = $('date').value;
  const filtered = orders.filter((o) => !selectedDate || dateKey(o.date) === selectedDate);
  $('count').textContent = filtered.length;
  $('empty').style.display = filtered.length ? 'none' : 'block';
  $('rows').innerHTML = '';
  for (const o of filtered) {
    const tr = document.createElement('tr');
    const amount = o.amount === '' || o.amount == null ? '—' : `¥${Number(o.amount).toLocaleString('ja-JP')}`;
    for (const value of [o.id || '—', localDate(o.date) || '—', o.status || '—', o.items || '—', amount]) {
      const td = document.createElement('td'); td.textContent = value; tr.appendChild(td);
    }
    const td = document.createElement('td'), button = document.createElement('button'); button.textContent = '详情';
    button.onclick = () => { $('json').textContent = JSON.stringify(o.raw, null, 2); $('details').showModal(); }; td.appendChild(button); tr.appendChild(td); $('rows').appendChild(tr);
  }
}
function updateOrders(next) {
  orders = next;
  render();
}
$('refresh').onclick = async () => {
  $('refresh').disabled = true;
  $('refresh').textContent = '正在读取…';
  try { await window.rocket.refresh(); }
  finally { $('refresh').disabled = false; $('refresh').textContent = '刷新订单'; }
};
$('date').onchange = render; $('close').onclick = () => $('details').close();
window.rocket.onOrders(updateOrders); window.rocket.onStatus((s) => { $('status').textContent = s.message; $('status').className = `status-${s.kind}`; if(s.lastCaptureAt) $('last').textContent = localDate(s.lastCaptureAt); });
window.rocket.getOrders().then(updateOrders); window.rocket.getStatus().then((s) => { if(s.lastCaptureAt) $('last').textContent = localDate(s.lastCaptureAt); });
