'use strict';

/* =========================================================
   Dükkan Paneli — istemci
   Veriler sunucudaki SQLite veritabanında tutulur; bu dosya ASP.NET Core API'yi çağırır.
   ========================================================= */

/* ---------- Yardımcılar ---------- */
const THEME_KEY = 'dukkanPaneli.theme';
const CART_KEY = 'dukkanPaneli.cart';
const SEEN_KEY = 'dukkanPaneli.seenNotifications';
const $ = (s, r = document) => r.querySelector(s);
const pad = n => String(n).padStart(2, '0');
// toISOString() UTC'ye çevirdiği için Türkiye saatinde bir önceki günü verebilir; yerel tarih kullanıyoruz.
const toISO = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const parseISO = s => { const [y, m, d] = String(s).split('-').map(Number); return new Date(y, m - 1, d); };
const todayISO = () => toISO(new Date());
const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };
const monthOf = s => String(s).slice(0, 7);
const shiftMonth = (key, n) => { const [y, m] = key.split('-').map(Number); const d = new Date(y, m - 1 + n, 1); return `${d.getFullYear()}-${pad(d.getMonth() + 1)}`; };
const daysBetween = (a, b) => Math.round((parseISO(b) - parseISO(a)) / 86400000);
const round2 = n => Math.round((Number(n) + Number.EPSILON) * 100) / 100;
const sumBy = (arr, f) => arr.reduce((a, x) => a + (Number(f(x)) || 0), 0);
const pct = (cur, prev) => (prev === 0 ? null : ((cur - prev) / Math.abs(prev)) * 100);
const vatOf = (gross, rate) => (rate > 0 ? round2((gross * rate) / (100 + rate)) : 0);

const MONTHS = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık'];
const MONTHS_SHORT = ['Oca', 'Şub', 'Mar', 'Nis', 'May', 'Haz', 'Tem', 'Ağu', 'Eyl', 'Eki', 'Kas', 'Ara'];
const DAYS_SHORT = ['Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt', 'Paz'];
const monthLabel = key => { const [y, m] = key.split('-').map(Number); return `${MONTHS[m - 1]} ${y}`; };
const moneyFmt = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', minimumFractionDigits: 0, maximumFractionDigits: 2 });
const money = n => moneyFmt.format(Number(n) || 0);
const numFmt = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 3 });
const num = n => numFmt.format(Number(n) || 0);
const fmtDate = s => (s ? parseISO(s).toLocaleDateString('tr-TR', { day: 'numeric', month: 'short', year: 'numeric' }) : '—');
const fmtTime = s => (s ? new Date(s).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—');
const fmtClock = s => (s ? new Date(s).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }) : '');
const longDate = s => parseISO(s).toLocaleDateString('tr-TR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const emptyState = msg => `<div class="empty">${esc(msg)}</div>`;
const shortMoney = v => (Math.abs(v) >= 1e6 ? `${num(round2(v / 1e6))} Mn` : Math.abs(v) >= 1e3 ? `${num(Math.round(v / 1e3))} bin` : num(v));
const saleNo = id => `F-${String(id).padStart(6, '0')}`;
const store = {
  get(k, def) { try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : def; } catch { return def; } },
  set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch { /* özel pencere vb. */ } },
};

const ic = d => `<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${d}</svg>`;
const ICON = {
  edit: ic('<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>'),
  trash: ic('<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M19 6l-1 14H6L5 6"/>'),
  cart: ic('<circle cx="9" cy="20" r="1.5"/><circle cx="18" cy="20" r="1.5"/><path d="M2 3h3l2.6 12.4a1.5 1.5 0 0 0 1.5 1.1h8.8a1.5 1.5 0 0 0 1.5-1.2L21 7H6"/>'),
  inbox: ic('<path d="M12 3v12M7 10l5 5 5-5"/><path d="M4 17v3h16v-3"/>'),
  list: ic('<path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/>'),
  sliders: ic('<path d="M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6"/>'),
  lock: ic('<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>'),
  receipt: ic('<path d="M5 3h14v18l-3-2-2 2-2-2-2 2-2-2-3 2z"/><path d="M9 8h6M9 12h6"/>'),
  x: ic('<path d="M18 6 6 18M6 6l12 12"/>'),
  undo: ic('<path d="M9 14 4 9l5-5"/><path d="M4 9h11a5 5 0 0 1 0 10h-3"/>'),
};

const CATEGORIES = {
  Gelir: ['Satış', 'Tahsilat', 'Hizmet', 'Diğer gelir'],
  Gider: ['Tedarik', 'Kira', 'Personel', 'Elektrik/Su', 'Vergi', 'Bakım', 'Pazarlama', 'Borç ödemesi', 'Satış iadesi', 'Diğer gider'],
};
const EVENT_TYPES = { odeme: 'Ödeme', teslimat: 'Teslimat', toplanti: 'Toplantı', kampanya: 'Kampanya', diger: 'Diğer' };
const UNITS = ['adet', 'kg', 'paket', 'litre', 'koli', 'metre'];
const VAT_RATES = [0, 1, 10, 20];
const PAY_TR = { Nakit: 'Nakit', Kart: 'Kart', Veresiye: 'Veresiye' };
const MOVE_TYPES = { Acilis: 'Açılış', Giris: 'Giriş', Satis: 'Satış', SatisIptal: 'Satış iptali', Fire: 'Fire / zayi', Iade: 'Tedarikçiye iade', Sayim: 'Sayım', Duzeltme: 'Düzeltme' };

/* ---------- API istemcisi ---------- */
class ApiError extends Error {
  constructor(message, status, details) { super(message); this.status = status; this.details = details; }
}
async function api(method, url, body) {
  const headers = { 'X-Requested-With': 'dukkan' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  let res;
  try {
    res = await fetch(url, { method, headers, credentials: 'same-origin', body: body !== undefined ? JSON.stringify(body) : undefined });
  } catch {
    throw new ApiError('Sunucuya ulaşılamadı. İnternet bağlantınızı kontrol edin.', 0);
  }
  const data = (res.headers.get('content-type') || '').includes('json') ? await res.json().catch(() => null) : null;
  if (res.status === 401 && !url.startsWith('/api/auth/')) {
    state = null;
    showLogin('Oturumunuzun süresi doldu, lütfen tekrar giriş yapın.');
    throw new ApiError('Oturum kapandı.', 401);
  }
  if (!res.ok) throw new ApiError(data?.error || `İstek başarısız oldu (${res.status}).`, res.status, data?.details);
  return data;
}

/* ---------- Durum ---------- */
let state = null;
let currentView = null;
const isOwner = () => state?.me?.role === 'Sahip';

async function reload() {
  state = await api('GET', '/api/bootstrap');
  // Sepette artık olmayan ürünleri temizle.
  ui.cart = ui.cart.filter(c => state.products.some(p => p.id === c.productId));
}

const newPeriod = mode => ({ mode, date: todayISO(), from: toISO(addDays(new Date(), -6)), to: todayISO() });
const ui = {
  periods: { fin: newPeriod('ay'), sales: newPeriod('gun'), rep: newPeriod('ay'), mov: newPeriod('ay') },
  finType: 'all', finQuery: '',
  prodQuery: '', prodLowOnly: false,
  debtTab: 'Borc', debtShowClosed: false,
  calMonth: monthOf(todayISO()), calSelected: todayISO(),
  cart: store.get(CART_KEY, []), posQuery: '', posCat: '', posPay: 'Nakit', posCustomer: '', posDue: '', posDiscount: '', posNote: '', posDate: '',
  movType: '',
  hist: { from: '', to: '', user: '', q: '', page: 1, open: {} },
  notifs: [],
};
const saveCart = () => store.set(CART_KEY, ui.cart);

/* ---------- Hesaplamalar ---------- */
function totalsFor(from, to) {
  const tx = (state.cash || []).filter(t => t.date >= from && t.date <= to);
  const gelir = sumBy(tx.filter(t => t.type === 'Gelir'), t => t.amount);
  const gider = sumBy(tx.filter(t => t.type === 'Gider'), t => t.amount);
  return { gelir: round2(gelir), gider: round2(gider), net: round2(gelir - gider), count: tx.length };
}
const monthRange = key => { const [y, m] = key.split('-').map(Number); return { from: `${key}-01`, to: toISO(new Date(y, m, 0)) }; };
function monthTotals(key, untilDay) {
  const r = monthRange(key);
  return totalsFor(r.from, untilDay ? `${key}-${pad(untilDay)}` : r.to);
}
const openDebts = kind => (state.debts || []).filter(d => d.kind === kind && d.remaining > 0);
const isOverdue = d => d.remaining > 0 && d.dueDate && d.dueDate < todayISO();
const isLow = p => p.stock <= p.minStock;
const supplierById = id => state.suppliers.find(s => s.id === id);
const productById = id => state.products.find(p => p.id === id);

function calendarItems(from, to) {
  const items = [];
  state.events.forEach(ev => {
    if (ev.date >= from && ev.date <= to) items.push({ kind: 'event', id: ev.id, date: ev.date, time: ev.time || '', title: ev.title, type: ev.type, note: ev.note });
  });
  (state.debts || []).forEach(d => {
    if (d.dueDate && d.remaining > 0 && d.dueDate >= from && d.dueDate <= to)
      items.push({ kind: 'debt', id: d.id, date: d.dueDate, time: '', title: d.party, type: d.kind === 'Borc' ? 'vade-borc' : 'vade-alacak', note: money(d.remaining), debtKind: d.kind });
  });
  return items.sort((a, b) => a.date.localeCompare(b.date) || (a.time || '99').localeCompare(b.time || '99'));
}
const itemsOn = date => calendarItems(date, date);

/* ---------- Dönem seçici (gün / ay / yıl / aralık) ---------- */
const PER_MODES = { gun: 'Gün', ay: 'Ay', yil: 'Yıl', aralik: 'Tarih aralığı' };
const PER_PREV = { gun: 'Önceki gün', ay: 'Önceki ay', yil: 'Önceki yıl', aralik: 'Önceki dönem' };

function perRange(k) {
  const p = ui.periods[k], d = parseISO(p.date), y = d.getFullYear(), m = d.getMonth();
  if (p.mode === 'gun') return { from: p.date, to: p.date };
  if (p.mode === 'yil') return { from: `${y}-01-01`, to: `${y}-12-31` };
  if (p.mode === 'aralik') { const [from, to] = [p.from, p.to].sort(); return { from, to }; }
  return { from: toISO(new Date(y, m, 1)), to: toISO(new Date(y, m + 1, 0)) };
}
function perPrev(k) {
  const p = ui.periods[k], { from, to } = perRange(k), d = parseISO(from);
  if (p.mode === 'ay') return { from: toISO(new Date(d.getFullYear(), d.getMonth() - 1, 1)), to: toISO(new Date(d.getFullYear(), d.getMonth(), 0)) };
  if (p.mode === 'yil') return { from: `${d.getFullYear() - 1}-01-01`, to: `${d.getFullYear() - 1}-12-31` };
  const len = daysBetween(from, to) + 1;
  return { from: toISO(addDays(d, -len)), to: toISO(addDays(d, -1)) };
}
function perLabel(k) {
  const p = ui.periods[k], { from, to } = perRange(k);
  if (p.mode === 'gun') return longDate(p.date);
  if (p.mode === 'yil') return `${p.date.slice(0, 4)} yılı`;
  if (p.mode === 'aralik') return `${fmtDate(from)} – ${fmtDate(to)} (${daysBetween(from, to) + 1} gün)`;
  return monthLabel(monthOf(p.date));
}
const perQuery = k => { const r = perRange(k); return `from=${r.from}&to=${r.to}`; };
function yearOptions(sel) {
  const cy = new Date().getFullYear();
  const years = new Set([cy - 2, cy - 1, cy, cy + 1, sel, ...(state.cash || []).map(t => Number(t.date.slice(0, 4)))]);
  return [...years].sort((a, b) => b - a).map(y => `<option value="${y}" ${y === sel ? 'selected' : ''}>${y}</option>`).join('');
}
function perToolbar(k) {
  const p = ui.periods[k];
  const att = f => `class="date-in per-input" data-period="${k}" data-field="${f}"`;
  const picker = p.mode === 'gun' ? `<input type="date" ${att('day')} value="${p.date}" aria-label="Gün seç">`
    : p.mode === 'ay' ? `<input type="month" ${att('mon')} value="${monthOf(p.date)}" aria-label="Ay seç">`
    : p.mode === 'yil' ? `<select ${att('year')} aria-label="Yıl seç">${yearOptions(Number(p.date.slice(0, 4)))}</select>`
    : `<input type="date" ${att('from')} value="${p.from}" aria-label="Başlangıç"><span class="muted">–</span><input type="date" ${att('to')} value="${p.to}" aria-label="Bitiş">`;
  const r = perRange(k), t = todayISO(), current = t >= r.from && t <= r.to;
  const step = s => `<button class="icon-btn" data-action="per-step" data-period="${k}" data-step="${s}" aria-label="${s < 0 ? PER_PREV[p.mode] : 'Sonraki'}">${s < 0 ? '‹' : '›'}</button>`;
  return `<div class="seg" role="group" aria-label="Dönem türü">${Object.entries(PER_MODES).map(([v, l]) => `<button class="${p.mode === v ? 'on' : ''}" data-action="per-mode" data-period="${k}" data-mode="${v}">${l}</button>`).join('')}</div>
    <div class="month-nav">${p.mode !== 'aralik' ? step(-1) : ''}${picker}${p.mode !== 'aralik' ? step(1) : ''}${current ? '' : `<button class="btn sm ghost" data-action="per-step" data-period="${k}" data-step="0">Bugün</button>`}</div>`;
}

/* ---------- Görünümler ve yönlendirme ---------- */
const VIEWS = {
  dashboard: { title: 'Genel Bakış', owner: true, render: () => renderDashboard() },
  satis: { title: 'Satış', render: () => renderPos() },
  satislar: { title: 'Satışlar & Fişler', render: () => renderSales() },
  finans: { title: 'Gelir & Gider', owner: true, render: () => renderFinance() },
  urunler: { title: 'Ürünler & Stok', render: () => renderProducts() },
  hareketler: { title: 'Stok Hareketleri', render: () => renderMovements() },
  tedarikciler: { title: 'Tedarikçiler', owner: true, render: () => renderSuppliers() },
  borclar: { title: 'Borç & Alacak', owner: true, render: () => renderDebts() },
  takvim: { title: 'Takvim', render: () => renderCalendar() },
  raporlar: { title: 'Raporlar', owner: true, render: () => renderReports() },
  gecmis: { title: 'İşlem Geçmişi', owner: true, render: () => renderHistory() },
  ayarlar: { title: 'Ayarlar', render: () => renderSettings() },
};

function route() {
  if (!state) return;
  let v = location.hash.replace('#', '');
  if (!VIEWS[v] || (VIEWS[v].owner && !isOwner())) {
    v = isOwner() ? 'dashboard' : 'satis';
    history.replaceState(null, '', '#' + v);
  }
  const changed = v !== currentView;
  currentView = v;
  render();
  if (changed) window.scrollTo(0, 0);
}

let renderToken = 0;
async function render() {
  if (!state || !currentView) return;
  const v = VIEWS[currentView];
  const el = $('#view');
  const token = ++renderToken;
  $('#view-title').textContent = v.title;
  $('#shop-name').textContent = state.settings.shopName;
  $('#today-label').textContent = longDate(todayISO());
  $('#user-name').textContent = state.me.displayName;
  $('#user-role').textContent = isOwner() ? 'Sahip' : 'Çalışan';
  document.title = `${v.title} · ${state.settings.shopName}`;
  document.querySelectorAll('.nav a').forEach(a => {
    a.hidden = a.hasAttribute('data-owner') && !isOwner();
    const on = a.dataset.view === currentView;
    a.classList.toggle('active', on);
    if (on) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
  });
  updateBadges();
  let out;
  try { out = v.render(); } catch (e) { console.error(e); out = emptyState('Bu ekran yüklenirken hata oluştu: ' + e.message); }
  if (out instanceof Promise) {
    if (el.dataset.view !== currentView) el.innerHTML = '<div class="empty">Yükleniyor…</div>';
    el.dataset.view = currentView;
    try {
      const html = await out;
      if (token === renderToken) el.innerHTML = html;
    } catch (e) {
      if (e.status !== 401 && token === renderToken) el.innerHTML = emptyState(e.message);
    }
  } else {
    el.innerHTML = out;
    el.dataset.view = currentView;
  }
}

function updateBadges() {
  const setBadge = (view, n, cls, title) => {
    const b = $(`.nav a[data-view="${view}"] .badge`);
    if (!b) return;
    b.textContent = n; b.hidden = !n; b.className = 'badge ' + cls; b.title = title;
  };
  setBadge('urunler', state.products.filter(isLow).length, 'warn', 'Kritik stokta ürün');
  setBadge('borclar', (state.debts || []).filter(isOverdue).length, 'danger', 'Vadesi geçmiş kayıt');
  setBadge('takvim', itemsOn(todayISO()).length, 'info', 'Bugünkü kayıtlar');
}

const deltaHTML = (p, invert = false) => {
  if (p === null || !Number.isFinite(p)) return '';
  const good = invert ? p <= 0 : p >= 0;
  return `<span class="delta ${good ? 'up' : 'down'}">${p >= 0 ? '▲' : '▼'} %${num(Math.round(Math.abs(p)))}</span>`;
};
const kpi = (label, value, sub = '', cls = '') => `<div class="kpi"><span class="kpi-label">${label}</span><strong class="kpi-value ${cls}">${value}</strong><span class="kpi-sub">${sub}</span></div>`;
const signCls = v => (v > 0 ? 'pos' : v < 0 ? 'neg' : '');
const iconBtn = (action, id, icon, title, extra = '') => `<button class="icon-btn sm ${extra}" data-action="${action}" data-id="${id}" title="${esc(title)}" aria-label="${esc(title)}">${icon}</button>`;

/* ---------- Kimlik doğrulama ekranları ---------- */
const LOGO = '<span class="logo" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M4 9h16l-1.5-5h-13z"/><path d="M5 9v11h14V9"/><path d="M10 20v-6h4v6"/></svg></span>';

function showAuth(html) {
  $('#app').hidden = true;
  const a = $('#auth');
  a.hidden = false;
  a.innerHTML = `<div class="auth-card">${html}</div>`;
  a.querySelector('input')?.focus();
}
function showLogin(message, shopName) {
  showAuth(`${LOGO}<h1>${esc(shopName || document.title.split(' · ').pop() || 'Dükkan Paneli')}</h1><p>Devam etmek için giriş yapın.</p>
    <form id="login-form" class="form-grid" novalidate>
      <label class="field full"><span>Kullanıcı adı</span><input name="userName" autocomplete="username" autocapitalize="none" required></label>
      <label class="field full"><span>Şifre</span><input name="password" type="password" autocomplete="current-password" required></label>
      <p class="form-error full" ${message ? '' : 'hidden'}>${esc(message || '')}</p>
      <button class="btn primary block full" type="submit">Giriş yap</button>
    </form>`);
}
function showSetup(tokenRequired = false) {
  showAuth(`${LOGO}<h1>Dükkan Paneli kurulumu</h1><p>İlk kez açılıyor. Dükkan sahibi hesabını oluşturun; çalışan hesaplarını sonra Ayarlar'dan ekleyebilirsiniz.</p>
    <form id="setup-form" class="form-grid" novalidate>
      <label class="field full"><span>Dükkan adı</span><input name="shopName" maxlength="60" required placeholder="ör. Merkez Şarküteri"></label>
      <label class="field full"><span>Adınız</span><input name="displayName" maxlength="60" autocomplete="name"></label>
      <label class="field full"><span>Kullanıcı adı</span><input name="userName" maxlength="32" autocomplete="username" autocapitalize="none" required><small>Küçük harf, rakam, nokta, tire (3-32 karakter)</small></label>
      <label class="field full"><span>Şifre</span><input name="password" type="password" autocomplete="new-password" required><small>En az 8 karakter</small></label>
      <label class="field full"><span>Şifre (tekrar)</span><input name="password2" type="password" autocomplete="new-password" required></label>
      ${tokenRequired ? '<label class="field full"><span>Kurulum anahtarı</span><input name="setupToken" type="password" autocomplete="off" required><small>Sunucuda Setup__Token ile tanımlanan anahtar</small></label>' : ''}
      <label class="field full check"><input type="checkbox" name="loadDemo" checked><span>Denemek için örnek verileri yükle</span></label>
      <p class="form-error full" hidden></p>
      <button class="btn primary block full" type="submit">Kurulumu tamamla</button>
    </form>`);
}
async function enterApp(goHome = false) {
  await reload();
  // Yeni girişte kullanıcının rolüne göre ana ekrana git; sayfa yenilemede bulunduğu ekranda kalsın.
  if (goHome) history.replaceState(null, '', '#' + (isOwner() ? 'dashboard' : 'satis'));
  $('#auth').hidden = true;
  $('#auth').innerHTML = '';
  $('#app').hidden = false;
  currentView = null;
  route();
  startNotifications();
}
async function boot() {
  try {
    const st = await api('GET', '/api/auth/status');
    if (st.needsSetup) return showSetup(st.setupTokenRequired);
    if (!st.user) return showLogin('', st.shopName);
    await enterApp();
  } catch (e) {
    if (e.status !== 401) showAuth(`${LOGO}<h1>Bağlantı hatası</h1><p>${esc(e.message)}</p><button class="btn primary block" data-action="retry-boot">Tekrar dene</button>`);
  }
}

/* ---------- Form altyapısı ---------- */
function fieldHTML(f) {
  if (f.type === 'info') return `<div class="field full info">${f.html}</div>`;
  const id = 'f_' + f.name;
  const attrs = [`id="${id}"`, `name="${f.name}"`, f.min != null && `min="${f.min}"`, f.max != null && `max="${f.max}"`,
    f.step != null && `step="${f.step}"`, f.placeholder && `placeholder="${esc(f.placeholder)}"`, f.maxlength && `maxlength="${f.maxlength}"`,
    f.datalist && `list="dl_${f.name}"`, f.disabled && 'disabled', f.autocomplete && `autocomplete="${f.autocomplete}"`].filter(Boolean).join(' ');
  const val = f.value ?? '';
  const hidden = f.hidden ? ' hidden' : '';
  if (f.type === 'checkbox') return `<label class="field full check"${hidden}><input type="checkbox" ${attrs} ${f.value ? 'checked' : ''}><span>${esc(f.label)}</span></label>`;
  let input;
  if (f.type === 'select') {
    input = `<select ${attrs}>${f.options.map(o => {
      const v = typeof o === 'object' ? o.value : o, l = typeof o === 'object' ? o.label : o;
      return `<option value="${esc(v)}" ${String(v) === String(val) ? 'selected' : ''}>${esc(l)}</option>`;
    }).join('')}</select>`;
  } else if (f.type === 'textarea') input = `<textarea ${attrs} rows="2">${esc(val)}</textarea>`;
  else input = `<input type="${f.type || 'text'}" ${attrs} value="${esc(val)}">`;
  const dl = f.datalist ? `<datalist id="dl_${f.name}">${f.datalist.map(o => `<option value="${esc(o)}"></option>`).join('')}</datalist>` : '';
  return `<label class="field ${f.full ? 'full' : ''}" for="${id}" data-field="${f.name}"${hidden}><span>${esc(f.label)}</span>${input}${dl}${f.hint ? `<small>${esc(f.hint)}</small>` : ''}</label>`;
}

function openPanel(title, html, wide = false) {
  const dlg = $('#modal');
  if (dlg.open) dlg.close();
  dlg.classList.toggle('wide', wide);
  $('#modal-title').textContent = title;
  $('#modal-body').innerHTML = `<div class="panel-body">${html}</div>`;
  dlg.showModal();
}

/**
 * Form açar. onSubmit(values) async olabilir: hata metni döndürür ya da hata fırlatırsa form açık kalır
 * ve mesaj gösterilir; fonksiyon döndürürse form kapandıktan sonra çağrılır.
 */
function openForm({ title, fields, submitText = 'Kaydet', onSubmit, onChange, wide = false }) {
  const dlg = $('#modal');
  if (dlg.open) dlg.close();
  dlg.classList.toggle('wide', wide);
  $('#modal-title').textContent = title;
  $('#modal-body').innerHTML = `<form id="modal-form" class="form-grid" autocomplete="off" novalidate>${fields.map(fieldHTML).join('')}
    <p class="form-error full" role="alert" hidden></p>
    <div class="form-actions full"><button type="button" class="btn ghost" data-action="close-modal">Vazgeç</button><button type="submit" class="btn primary">${esc(submitText)}</button></div></form>`;
  const form = $('#modal-form');
  const showError = msg => { const p = form.querySelector('.form-error'); p.textContent = msg; p.hidden = false; };
  form.onsubmit = async e => {
    e.preventDefault();
    const vals = {};
    fields.forEach(f => {
      if (!f.name) return;
      const el = form.elements[f.name];
      if (f.type === 'checkbox') vals[f.name] = el.checked;
      else if (f.type === 'number') vals[f.name] = el.value === '' ? null : Number(el.value);
      else vals[f.name] = el.value.trim();
    });
    const btn = form.querySelector('button[type=submit]');
    btn.disabled = true;
    let res;
    try { res = await onSubmit(vals); } catch (err) { if (err.status === 401) return; res = err.message; } finally { btn.disabled = false; }
    if (typeof res === 'string') return showError(res);
    if (dlg.open) dlg.close();
    if (typeof res === 'function') res();
  };
  form.onchange = onChange ? e => onChange(form, e) : null;
  onChange?.(form, { target: {} });
  dlg.showModal();
  form.querySelector('input:not([type=checkbox]):not([disabled]), select, textarea')?.focus();
}

let confirmCb = null;
function confirmDialog(text, onYes, yesLabel = 'Sil') {
  $('#confirm-text').textContent = text;
  $('#confirm-yes').textContent = yesLabel;
  $('#confirm-error').hidden = true;
  confirmCb = onYes;
  $('#confirm').showModal();
}

function toast(msg, kind = 'ok', auditId = null) {
  const el = document.createElement('div');
  el.className = 'toast ' + kind;
  const canUndo = auditId && isOwner();
  el.innerHTML = `<span>${esc(msg)}</span>${canUndo ? `<button class="toast-btn" data-action="undo" data-id="${auditId}">${ICON.undo} Geri al</button>` : ''}`;
  $('#toasts').appendChild(el);
  setTimeout(() => { el.classList.add('hide'); setTimeout(() => el.remove(), 300); }, canUndo ? 7000 : 3000);
}

/** Sunucuda değişiklik yapar, verileri yeniler ve "Geri al" düğmeli bildirim gösterir. */
async function mutate(method, url, body, message) {
  const res = await api(method, url, body);
  await reload();
  render();
  if (message !== false) toast(message || res?.summary || 'Kaydedildi', 'ok', res?.undoable ? res.auditId : null);
  return res;
}

async function undo(auditId) {
  try {
    const r = await api('POST', `/api/history/${auditId}/undo`);
    await reload();
    render();
    toast(r.summary, 'ok', r.auditId);
  } catch (e) {
    if (e.status === 401) return;
    openPanel('Geri alınamadı', `<p>${esc(e.message)}</p>${Array.isArray(e.details) && e.details.length ? `<p class="muted">Bu kayıtları sonradan değiştiren işlemler:</p><ul class="blockers">${e.details.map(d => `<li>${esc(d)}</li>`).join('')}</ul><p class="muted">İşlem Geçmişi ekranından bunları en yeniden başlayarak geri alabilirsiniz.</p>` : ''}
      <div class="form-actions"><a class="btn" href="#gecmis" data-action="close-modal-link">İşlem Geçmişi</a><button class="btn primary" data-action="close-modal">Tamam</button></div>`);
  }
}

function download(url) {
  const a = document.createElement('a');
  a.href = url; a.download = '';
  document.body.appendChild(a); a.click(); a.remove();
}
function printHTML(html) {
  $('#print-area').innerHTML = html;
  document.body.classList.add('printing');
  const done = () => { document.body.classList.remove('printing'); window.removeEventListener('afterprint', done); };
  window.addEventListener('afterprint', done);
  window.print();
}

/* ---------- Sıralanabilir tablolar ----------
   <table data-sort="anahtar"> olan tablolarda sütun başlığına tıklayınca satırlar sıralanır:
   artan → azalan → ilk sıra. Hücrede data-v varsa ona göre (sayı ya da ISO tarih), yoksa görünen
   metne göre sıralar. Seçim ui.sort'ta tutulur; ekran yeniden çizilince aynı sıralama uygulanır. */
ui.sort = {};
const NO_SORT = new Set(['', 'İşlem']);

function sortValue(td) {
  if (!td) return '';
  if ('v' in td.dataset) {
    const v = td.dataset.v, n = Number(v);
    return v !== '' && Number.isFinite(n) ? n : v;
  }
  const c = td.cloneNode(true);
  c.querySelectorAll('.sub').forEach(x => x.remove());
  const t = c.textContent.trim();
  return t === '—' ? '' : t;
}
function compareValues(a, b) {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return String(a).localeCompare(String(b), 'tr', { numeric: true, sensitivity: 'base' });
}

function applySort(table) {
  const s = ui.sort[table.dataset.sort];
  table.querySelectorAll('thead th').forEach((th, i) => {
    if (!th.classList.contains('sortable')) return;
    const dir = s && s.col === i ? s.dir : 0;
    th.setAttribute('aria-sort', dir > 0 ? 'ascending' : dir < 0 ? 'descending' : 'none');
  });
  const body = table.tBodies[0];
  if (!body) return;
  // İlk sırayı satırlara yaz; "sıralamayı kaldır" ve eşit değerler için kullanılır.
  const rows = [...body.rows].map((r, i) => {
    if (!('i' in r.dataset)) r.dataset.i = i;
    return { r, i: Number(r.dataset.i), v: s ? sortValue(r.cells[s.col]) : '' };
  });
  rows.sort((a, b) => {
    if (!s) return a.i - b.i;
    const ea = a.v === '', eb = b.v === '';
    if (ea !== eb) return ea ? 1 : -1; // boş değerler yönden bağımsız hep sonda
    return (ea ? 0 : compareValues(a.v, b.v) * s.dir) || a.i - b.i;
  });
  body.append(...rows.map(x => x.r));
}

// Yeni çizilen tabloların başlıklarını tıklanabilir yapar ve kayıtlı sıralamayı uygular.
function enhanceTables(root) {
  root.querySelectorAll('table[data-sort]:not([data-ready])').forEach(table => {
    table.dataset.ready = '1';
    table.querySelectorAll('thead th').forEach((th, i) => {
      if (NO_SORT.has(th.textContent.trim()) || th.classList.contains('nosort')) return;
      th.classList.add('sortable');
      th.innerHTML = `<button type="button" class="th-sort" data-col="${i}">${th.innerHTML}<span class="sort-ind" aria-hidden="true"></span></button>`;
    });
    applySort(table);
  });
}
new MutationObserver(() => enhanceTables(document.body)).observe(document.body, { childList: true, subtree: true });

document.addEventListener('click', e => {
  const btn = e.target.closest('.th-sort');
  if (!btn) return;
  const table = btn.closest('table'), key = table.dataset.sort, col = Number(btn.dataset.col);
  const cur = ui.sort[key];
  // artan → azalan → kaldır
  ui.sort[key] = !cur || cur.col !== col ? { col, dir: 1 } : cur.dir === 1 ? { col, dir: -1 } : undefined;
  applySort(table);
});
