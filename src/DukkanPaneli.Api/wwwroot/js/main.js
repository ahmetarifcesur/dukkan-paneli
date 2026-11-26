'use strict';
/* Eylemler, olay dinleyicileri, bildirimler ve uygulamanın başlatılması. */

const findIn = (coll, id) => (state[coll] || []).find(x => x.id === Number(id));
const debtById = id => findIn('debts', id);
const safe = fn => async (...args) => { try { await fn(...args); } catch (e) { if (e.status !== 401) toast(e.message, 'error'); } };

/* ---------- Bildirimler ---------- */
let notifTimer = null;
async function pollNotifications() {
  if (!state) return;
  try { ui.notifs = await api('GET', '/api/notifications'); } catch { return; }
  const count = ui.notifs.length, bell = $('#bell-count');
  bell.textContent = count > 9 ? '9+' : count;
  bell.hidden = !count;
  if (!('Notification' in window) || Notification.permission !== 'granted') return;
  const seen = new Set(store.get(SEEN_KEY, []));
  const fresh = ui.notifs.filter(n => !seen.has(n.key));
  if (!fresh.length) return;
  try {
    const reg = 'serviceWorker' in navigator ? await navigator.serviceWorker.getRegistration() : null;
    for (const n of fresh.slice(0, 5)) {
      const opts = { body: n.body, tag: n.key, icon: '/icons/icon-192.png', badge: '/icons/icon-192.png', data: { link: n.link } };
      if (reg) await reg.showNotification(n.title, opts); else new Notification(n.title, opts);
    }
  } catch (e) { console.warn('Bildirim gösterilemedi', e); }
  fresh.forEach(n => seen.add(n.key));
  store.set(SEEN_KEY, [...seen].slice(-300));
}
function startNotifications() {
  clearInterval(notifTimer);
  pollNotifications();
  notifTimer = setInterval(pollNotifications, 2 * 60 * 1000);
}
function showNotifications() {
  const list = ui.notifs;
  const perm = 'Notification' in window ? Notification.permission : 'unsupported';
  openPanel('Bildirimler', `
    ${list.length ? `<ul class="alerts">${list.map(n => `<li class="${n.level}"><span><b>${esc(n.title)}</b> — ${esc(n.body)}</span><a class="btn sm" href="${esc(n.link)}" data-action="close-modal-link">Git</a></li>`).join('')}</ul>`
      : '<ul class="alerts"><li class="ok-msg"><span>Bekleyen bildirim yok.</span></li></ul>'}
    <div class="form-actions">${perm === 'default' ? '<button class="btn" data-action="enable-notifications">Cihaz bildirimlerini aç</button>' : ''}<button class="btn primary" data-action="close-modal">Kapat</button></div>`);
}

/* ---------- Eylemler ---------- */
const actions = {
  'retry-boot': () => boot(),
  'close-modal': () => $('#modal').close(),
  'close-modal-link': (ds, el) => { $('#modal').close(); location.hash = el.getAttribute('href'); },
  'confirm-no': () => { confirmCb = null; $('#confirm').close(); },
  'confirm-yes': async (ds, el) => {
    const cb = confirmCb;
    if (!cb) return $('#confirm').close();
    el.disabled = true;
    try { await cb(); confirmCb = null; $('#confirm').close(); }
    catch (e) { if (e.status === 401) { $('#confirm').close(); return; } const p = $('#confirm-error'); p.textContent = e.message; p.hidden = false; }
    finally { el.disabled = false; }
  },
  'toggle-theme': () => {
    const root = document.documentElement;
    const isDark = root.dataset.theme ? root.dataset.theme === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches;
    root.dataset.theme = isDark ? 'light' : 'dark';
    store.set(THEME_KEY, root.dataset.theme);
  },
  'logout': safe(async () => {
    await api('POST', '/api/auth/logout');
    clearInterval(notifTimer);
    const shop = state?.settings?.shopName;
    state = null; currentView = null;
    showLogin('', shop);
  }),
  'undo': ds => undo(Number(ds.id)),
  'show-notifications': () => showNotifications(),

  // Asistan
  'assistant-send': () => assistantAsk($('#chat-q')?.value),
  'assistant-suggest': ds => assistantAsk(ds.q),
  'assistant-clear': () => { ui.chat = []; render(); },
  'assistant-context': async () => {
    try {
      const r = await api('GET', '/api/assistant/context');
      openPanel('Asistana gönderilen özet', `<p class="muted">Her soruda bu metin ve sorunuz Anthropic'e (Claude) gönderilir. Müşteri ve çalışan adları takma adla yazılır, cevapta gerçek adlarına geri çevrilir.</p>
        <pre class="context-preview">${esc(r.text)}</pre><div class="form-actions"><button class="btn primary" data-action="close-modal">Kapat</button></div>`, true);
    } catch (e) { if (e.status !== 401) toast(e.message, 'error'); }
  },
  'enable-notifications': safe(async () => {
    if (!('Notification' in window)) return toast('Bu tarayıcı bildirimleri desteklemiyor.', 'error');
    const r = await Notification.requestPermission();
    toast(r === 'granted' ? 'Bildirimler açıldı' : 'Bildirim izni verilmedi', r === 'granted' ? 'ok' : 'error');
    if (r === 'granted') { store.set(SEEN_KEY, []); pollNotifications(); }
    if ($('#modal').open) $('#modal').close();
    if (currentView === 'ayarlar') render();
  }),

  // Dönem seçici
  'per-mode': ds => {
    const p = ui.periods[ds.period];
    if (ds.mode === 'aralik' && p.mode !== 'aralik') { const r = perRange(ds.period); p.from = r.from; p.to = r.to; }
    p.mode = ds.mode; render();
  },
  'per-step': ds => {
    const p = ui.periods[ds.period], s = Number(ds.step);
    if (s === 0) Object.assign(p, newPeriod(p.mode));
    else {
      const d = parseISO(p.date);
      // Ay/yıl kaydırırken gün taşmasını önlemek için ayın 1'ine geçiyoruz.
      if (p.mode === 'gun') p.date = toISO(addDays(d, s));
      else if (p.mode === 'ay') p.date = toISO(new Date(d.getFullYear(), d.getMonth() + s, 1));
      else if (p.mode === 'yil') p.date = toISO(new Date(d.getFullYear() + s, 0, 1));
    }
    render();
  },

  // Gelir / gider
  'add-tx': ds => txForm(ds.type),
  'edit-tx': ds => { const t = findIn('cash', ds.id); if (t) txForm(t.type, t); },
  'del-tx': ds => { const t = findIn('cash', ds.id); if (t) confirmDialog(`${fmtDate(t.date)} tarihli ${money(t.amount)} tutarındaki ${t.type.toLowerCase()} kaydı silinsin mi? (İşlem Geçmişi'nden geri alınabilir)`, () => mutate('DELETE', `/api/cash/${t.id}`)); },
  'fin-type': ds => { ui.finType = ds.type; render(); },
  // Excel, "=", "+", "-", "@" ile başlayan hücreyi formül sanar; kullanıcının yazdığı metin formül olarak
  // çalışmasın diye başına ' ekliyoruz (CSV/formül enjeksiyonu).
  'export-csv': () => {
    const csvText = s => (/^[=+\-@\t\r]/.test(s ?? '') ? `'${s}` : s);
    const list = filteredTx();
    if (!list.length) return toast('Dışa aktarılacak işlem yok.', 'error');
    const rows = [['Tarih', 'Tür', 'Kategori', 'Açıklama', 'Tutar', 'KDV']].concat(list.map(t => [t.date, t.type, csvText(t.category), csvText(t.note), String(t.type === 'Gelir' ? t.amount : -t.amount).replace('.', ','), String(t.vatAmount || 0).replace('.', ',')]));
    const csv = rows.map(r => r.map(c => `"${String(c ?? '').replace(/"/g, '""')}"`).join(';')).join('\r\n');
    const r = perRange('fin');
    const url = URL.createObjectURL(new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url; a.download = `gelir-gider-${r.from === r.to ? r.from : `${r.from}_${r.to}`}.csv`;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },

  // Satış ekranı
  'pos-cat': ds => { ui.posCat = ds.cat; render(); },
  'cart-add': ds => { addToCart(Number(ds.id)); refreshCart(); },
  'cart-add-go': ds => { addToCart(Number(ds.id)); location.hash = '#satis'; toast('Sepete eklendi'); },
  'cart-step': ds => {
    const line = ui.cart.find(c => c.productId === Number(ds.id));
    if (!line) return;
    const p = productById(line.productId);
    const step = p?.unit === 'kg' ? 0.5 : 1;
    line.qty = round2(Math.max(0, line.qty + Number(ds.step) * step));
    if (line.qty === 0) ui.cart = ui.cart.filter(c => c !== line);
    saveCart(); refreshCart();
  },
  'cart-remove': ds => { ui.cart = ui.cart.filter(c => c.productId !== Number(ds.id)); saveCart(); refreshCart(); },
  'cart-clear': () => { ui.cart = []; saveCart(); refreshCart(); },
  'pos-pay': ds => { ui.posPay = ds.pay; refreshCart(); },
  'pos-complete': (ds, el) => completeSale(el),
  'show-receipt': ds => showReceipt(Number(ds.id)),
  'print-receipt': safe(async ds => { const s = await api('GET', `/api/sales/${ds.id}`); printHTML(receiptHTML(s)); }),
  'cancel-sale': ds => confirmDialog(`${ds.no} numaralı satış iptal edilsin mi? Ürünler stoğa geri döner, gelir kaydı silinir (aynı gün) ya da iade gideri yazılır.`, () => mutate('POST', `/api/sales/${ds.id}/cancel`, { reason: '' }), 'İptal et'),

  // Ürünler
  'add-product': () => productForm(),
  'edit-product': ds => { const p = productById(Number(ds.id)); if (p) productForm(p); },
  'del-product': ds => { const p = productById(Number(ds.id)); if (p) confirmDialog(`"${p.name}" ürünü silinsin mi? Geçmiş satış ve hareketler korunur; İşlem Geçmişi'nden geri alınabilir.`, () => mutate('DELETE', `/api/products/${p.id}`)); },
  'stock-in': ds => { const p = productById(Number(ds.id)); if (p) stockInForm(p); },
  'adjust-stock': ds => { const p = productById(Number(ds.id)); if (p) adjustForm(p); },
  'quick-stock': () => pickProduct('Stok girişi — ürün seçin', () => true, stockInForm),
  'product-moves': safe(ds => showProductMoves(Number(ds.id))),

  // Tedarikçiler
  'add-supplier': () => supplierForm(),
  'edit-supplier': ds => { const s = findIn('suppliers', ds.id); if (s) supplierForm(s); },
  'del-supplier': ds => {
    const s = findIn('suppliers', ds.id); if (!s) return;
    const n = state.products.filter(p => p.supplierId === s.id).length;
    confirmDialog(`"${s.name}" silinsin mi?${n ? ` ${n} ürünün tedarikçi bilgisi boşaltılacak.` : ''} Borç kayıtları korunur.`, () => mutate('DELETE', `/api/suppliers/${s.id}`));
  },

  // Borç & alacak
  'add-debt': ds => debtForm(ds.kind || 'Borc'),
  'edit-debt': ds => { const d = debtById(ds.id); if (d) debtForm(d.kind, d); },
  'del-debt': ds => { const d = debtById(ds.id); if (d) confirmDialog(`"${d.party}" kaydı (${money(d.amount)}) silinsin mi?`, () => mutate('DELETE', `/api/debts/${d.id}`)); },
  'pay-debt': ds => { const d = debtById(ds.id); if (d && d.remaining > 0) payForm(d); },
  'debt-payments': ds => showDebtPayments(Number(ds.id)),
  'del-payment': ds => {
    $('#modal').close();
    confirmDialog('Bu ödeme iptal edilsin mi? Bağlı gelir/gider kaydı da silinir.', () => mutate('DELETE', `/api/debts/${ds.debt}/payments/${ds.id}`), 'İptal et');
  },
  'debt-tab': ds => { ui.debtTab = ds.tab; render(); },

  // Takvim
  'add-event': ds => eventForm(null, ds.date),
  'edit-event': ds => { const e = findIn('events', ds.id); if (e) eventForm(e); },
  'del-event': ds => { const e = findIn('events', ds.id); if (e) confirmDialog(`"${e.title}" etkinliği silinsin mi?`, () => mutate('DELETE', `/api/events/${e.id}`)); },
  'cal-month': ds => {
    const s = Number(ds.step);
    if (s === 0) { ui.calMonth = monthOf(todayISO()); ui.calSelected = todayISO(); }
    else { ui.calMonth = shiftMonth(ui.calMonth, s); ui.calSelected = `${ui.calMonth}-01`; }
    render();
  },
  'cal-select': ds => { ui.calSelected = ds.date; ui.calMonth = monthOf(ds.date); render(); },

  // Raporlar
  'print-report': () => printHTML(`<div class="print-report">${$('#view').innerHTML}</div>`),

  // İşlem geçmişi
  'hist-toggle': safe(async ds => {
    const id = Number(ds.id), h = ui.hist;
    if (h.open[id]) { delete h.open[id]; return render(); }
    h.open[id] = true; render();
    h.open[id] = await api('GET', `/api/history/${id}`);
    render();
  }),
  'hist-undo': ds => confirmDialog(`Bu işlem geri alınsın mı?\n\n"${ds.sum}"\n\nGeri alma da geçmişe kaydedilir ve istenirse yeniden uygulanabilir.`, async () => { $('#confirm').close(); await undo(Number(ds.id)); }, 'Geri al'),
  'hist-page': ds => { ui.hist.page = Math.max(1, ui.hist.page + Number(ds.step)); render(); },
  'hist-reset': () => { Object.assign(ui.hist, { from: '', to: '', user: '', q: '', page: 1 }); render(); },

  // Ayarlar
  'add-user': () => userForm(),
  'edit-user': ds => { const u = (ui.users || []).find(x => x.id === Number(ds.id)); if (u) userForm(u); },
  'reset-demo': () => confirmDialog('Mevcut tüm işletme verileri silinip örnek veriler yüklensin mi? Bu işlem geri alınamaz.', () => mutate('POST', '/api/admin/demo'), 'Yükle'),
  'clear-all': () => confirmDialog('TÜM işletme verileri kalıcı olarak silinsin mi? Bu işlem geri alınamaz; önce yedek almanız önerilir.', () => mutate('POST', '/api/admin/clear')),
};

/* ---------- Olay dinleyicileri ---------- */
document.addEventListener('click', e => {
  const el = e.target.closest('[data-action]');
  if (!el || el.disabled) return;
  const fn = actions[el.dataset.action];
  if (fn) { e.preventDefault(); fn(el.dataset, el); }
});
['#modal', '#confirm'].forEach(sel => $(sel).addEventListener('click', e => { if (e.target === e.currentTarget) e.currentTarget.close(); }));
$('#confirm').addEventListener('close', () => { confirmCb = null; });

let histTimer = null;
document.addEventListener('input', e => {
  const t = e.target;
  if (t.id === 'fin-search') { ui.finQuery = t.value; $('#fin-table').innerHTML = finTable(); }
  if (t.id === 'prod-search') { ui.prodQuery = t.value; $('#prod-table').innerHTML = productTable(); }
  if (t.id === 'pos-search') { ui.posQuery = t.value; $('#pos-grid').innerHTML = posGrid(); }
  if (t.id === 'pos-customer') ui.posCustomer = t.value;
  if (t.id === 'hist-q') { clearTimeout(histTimer); histTimer = setTimeout(() => { ui.hist.q = t.value.trim(); ui.hist.page = 1; render(); }, 400); }
});
document.addEventListener('change', e => {
  const t = e.target, val = t.value;
  if (t.classList.contains('per-input')) {
    const p = ui.periods[t.dataset.period], f = t.dataset.field;
    const okDate = /^\d{4}-\d{2}-\d{2}$/.test(val);
    if (f === 'day' && okDate) p.date = val;
    else if (f === 'mon' && /^\d{4}-\d{2}$/.test(val)) p.date = `${val}-01`;
    else if (f === 'year') p.date = `${val}-01-01`;
    else if ((f === 'from' || f === 'to') && okDate) p[f] = val;
    else return;
    return render();
  }
  if (t.classList.contains('hist-filter')) { ui.hist[t.dataset.field] = val; ui.hist.page = 1; return render(); }
  if (t.id === 'prod-low') { ui.prodLowOnly = t.checked; $('#prod-table').innerHTML = productTable(); }
  if (t.id === 'debt-closed') { ui.debtShowClosed = t.checked; render(); }
  if (t.id === 'mov-type') { ui.movType = val; render(); }
  if (t.id === 'stock-at' && /^\d{4}-\d{2}-\d{2}$/.test(val)) safe(showProductMoves)(Number(t.dataset.id), val);
  // Sepet
  if (t.classList.contains('cart-qty') || t.classList.contains('cart-price')) {
    const line = ui.cart.find(c => c.productId === Number(t.dataset.id));
    const n = Number(val);
    if (line && Number.isFinite(n) && n >= 0) {
      if (t.classList.contains('cart-qty')) line.qty = round2(n); else line.price = round2(n);
      if (line.qty === 0) ui.cart = ui.cart.filter(c => c !== line);
      saveCart();
    }
    refreshCart();
  }
  if (t.id === 'pos-discount') { ui.posDiscount = val; refreshCart(); }
  if (t.id === 'pos-due') ui.posDue = val;
  if (t.id === 'pos-date') ui.posDate = val;
});

const formError = (form, msg) => { const p = form.querySelector('.form-error'); p.textContent = msg; p.hidden = !msg; };
document.addEventListener('submit', async e => {
  const f = e.target;
  if (!['login-form', 'setup-form', 'settings-form', 'password-form'].includes(f.id)) return;
  e.preventDefault();
  const v = Object.fromEntries(new FormData(f));
  const btn = f.querySelector('button[type=submit]');
  btn.disabled = true;
  try {
    if (f.id === 'login-form') {
      await api('POST', '/api/auth/login', { userName: v.userName, password: v.password });
      await enterApp(true);
    } else if (f.id === 'setup-form') {
      if (v.password !== v.password2) return formError(f, 'Şifreler eşleşmiyor.');
      await api('POST', '/api/auth/setup', { shopName: v.shopName, displayName: v.displayName, userName: v.userName, password: v.password, loadDemo: f.elements.loadDemo.checked, setupToken: v.setupToken || null });
      await enterApp(true);
    } else if (f.id === 'settings-form') {
      await mutate('PUT', '/api/settings', { shopName: v.shopName, defaultVatRate: Number(v.defaultVatRate) });
    } else if (f.id === 'password-form') {
      if (v.next !== v.next2) return formError(f, 'Yeni şifreler eşleşmiyor.');
      await api('POST', '/api/auth/change-password', { current: v.current, next: v.next });
      f.reset(); formError(f, '');
      toast('Şifre değiştirildi');
    }
  } catch (err) {
    if (err.status !== 401 || f.id === 'login-form') formError(f, err.message);
  } finally {
    btn.disabled = false;
  }
});

window.addEventListener('hashchange', route);
document.addEventListener('visibilitychange', () => { if (!document.hidden && state) pollNotifications(); });

/* ---------- Başlat ---------- */
const savedTheme = store.get(THEME_KEY, null);
if (savedTheme) document.documentElement.dataset.theme = savedTheme;
if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(err => console.warn('Service worker kaydedilemedi', err));
boot();

document.addEventListener('keydown', e => {
  if (e.target.id === 'chat-q' && e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
    e.preventDefault();
    assistantAsk(e.target.value);
  }
});
