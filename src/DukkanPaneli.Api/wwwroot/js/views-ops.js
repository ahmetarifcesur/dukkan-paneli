'use strict';
/* Günlük operasyon ekranları: genel bakış, satış, fişler, ürünler, stok hareketleri, takvim. */

/* ----- Genel bakış ----- */
function renderDashboard() {
  const today = todayISO(), cm = monthOf(today), lm = shiftMonth(cm, -1), llm = shiftMonth(cm, -2);
  const dayNo = parseISO(today).getDate();
  const cur = monthTotals(cm), last = monthTotals(lm), prev = monthTotals(llm), lastSame = monthTotals(lm, dayNo);
  const borcList = openDebts('Borc'), alacakList = openDebts('Alacak');
  const stockValue = sumBy(state.products, p => p.stock * (p.cost || 0));
  const low = state.products.filter(isLow);
  const fill = state.products.length ? (sumBy(state.products, p => (p.capacity ? Math.min(1, p.stock / p.capacity) : 0)) / state.products.length) * 100 : 0;
  const week = calendarItems(today, toISO(addDays(parseISO(today), 7))).filter(i => i.kind === 'event');
  const todaySales = (state.cash || []).filter(c => c.date === today && c.category === 'Satış');

  const kpis = [
    kpi(`Bu ay gelir <small>(${MONTHS[parseISO(today).getMonth()]})</small>`, money(cur.gelir), `Geçen ay aynı dönem ${money(lastSame.gelir)} ${deltaHTML(pct(cur.gelir, lastSame.gelir))}`),
    kpi('Bu ay gider', money(cur.gider), `Geçen ay aynı dönem ${money(lastSame.gider)} ${deltaHTML(pct(cur.gider, lastSame.gider), true)}`),
    kpi('Bu ay net kâr', money(cur.net), `Geçen ay aynı dönem ${money(lastSame.net)} ${deltaHTML(pct(cur.net, lastSame.net))}`, signCls(cur.net)),
    kpi(`Geçen ayki kazanç <small>(${monthLabel(lm)})</small>`, money(last.net), `Gelir ${shortMoney(last.gelir)} · Gider ${shortMoney(last.gider)} ${deltaHTML(pct(last.net, prev.net))}`, signCls(last.net)),
    kpi('Bugünkü satış', money(sumBy(todaySales, c => c.amount)), `${todaySales.length} peşin satış · <a href="#satislar">fişler</a>`),
    kpi('Ödenecek borçlar', money(sumBy(borcList, d => d.remaining)), `${borcList.length} açık · ${borcList.filter(isOverdue).length} gecikmiş`, borcList.length ? 'neg' : ''),
    kpi('Tahsil edilecek alacaklar', money(sumBy(alacakList, d => d.remaining)), `${alacakList.length} açık · ${alacakList.filter(isOverdue).length} gecikmiş`),
    kpi('Stok değeri (maliyet)', money(stockValue), `Ortalama doluluk %${Math.round(fill)} · ${low.length} kritik ürün`),
  ].join('');

  const alerts = [];
  const payBtn = d => `<button class="btn sm" data-action="pay-debt" data-id="${d.id}">${d.kind === 'Borc' ? 'Öde' : 'Tahsil et'}</button>`;
  (state.debts || []).filter(isOverdue).forEach(d => alerts.push({ lvl: 'danger', html: `<b>${esc(d.party)}</b> ${d.kind === 'Borc' ? 'borcunun' : 'alacağının'} vadesi <b>${daysBetween(d.dueDate, today)} gün</b> geçti — kalan ${money(d.remaining)}`, btn: payBtn(d) }));
  (state.debts || []).filter(d => d.remaining > 0 && d.dueDate && d.dueDate >= today && daysBetween(today, d.dueDate) <= 7)
    .sort((a, b) => a.dueDate.localeCompare(b.dueDate)).forEach(d => {
      const diff = daysBetween(today, d.dueDate);
      alerts.push({ lvl: 'warn', html: `<b>${esc(d.party)}</b> ${d.kind === 'Borc' ? 'ödemesi' : 'tahsilatı'} ${diff === 0 ? '<b>bugün</b>' : `${diff} gün sonra`} — ${money(d.remaining)}`, btn: payBtn(d) });
    });
  low.forEach(p => alerts.push({ lvl: 'warn', html: `<b>${esc(p.name)}</b> kritik stokta: ${num(p.stock)} ${esc(p.unit)} (min. ${num(p.minStock)})`, btn: `<button class="btn sm" data-action="stock-in" data-id="${p.id}">Stok gir</button>` }));
  itemsOn(today).filter(i => i.kind === 'event').forEach(i => alerts.push({ lvl: 'info', html: `Bugün: <b>${esc(i.title)}</b>${i.time ? ' · ' + esc(i.time) : ''}`, btn: '<a class="btn sm" href="#takvim">Takvim</a>' }));
  const alertsHTML = alerts.length
    ? `<ul class="alerts">${alerts.map(a => `<li class="${a.lvl}"><span>${a.html}</span>${a.btn || ''}</li>`).join('')}</ul>`
    : '<ul class="alerts"><li class="ok-msg"><span>Her şey yolunda — bekleyen uyarı yok.</span></li></ul>';

  const upcoming = calendarItems(today, toISO(addDays(parseISO(today), 21))).slice(0, 7);
  const recent = (state.cash || []).slice(0, 6);

  return `
    <div class="quick">
      <a class="btn primary" href="#satis">${ICON.cart} Satış ekranı</a>
      <button class="btn" data-action="add-tx" data-type="Gelir">+ Gelir</button>
      <button class="btn" data-action="add-tx" data-type="Gider">+ Gider</button>
      <button class="btn" data-action="quick-stock">+ Stok girişi</button>
      <button class="btn" data-action="add-event">+ Etkinlik</button>
    </div>
    <div class="kpis">${kpis}</div>
    <div class="grid-2">
      <div class="card">
        <div class="card-head"><h3>Son 6 ay gelir / gider</h3>
          <div class="legend"><span><i style="background:var(--income)"></i>Gelir</span><span><i style="background:var(--expense)"></i>Gider</span><span><i style="background:var(--text);height:2px;vertical-align:3px"></i>Net</span></div>
        </div>
        ${monthChart(6)}
      </div>
      <div class="card"><div class="card-head"><h3>Uyarılar</h3><span class="muted">${alerts.length} kayıt</span></div>${alertsHTML}</div>
    </div>
    <div class="grid-3">
      <div class="card"><div class="card-head"><h3>Yaklaşanlar</h3><a href="#takvim">Takvime git →</a></div>
        ${upcoming.length ? `<ul class="agenda">${upcoming.map(it => agendaItem(it, true)).join('')}</ul>` : emptyState('Önümüzdeki 3 haftada kayıt yok.')}
      </div>
      <div class="card"><div class="card-head"><h3>Bu ay gider dağılımı</h3></div>${categoryBreakdown(cm)}</div>
      <div class="card"><div class="card-head"><h3>Son işlemler</h3><a href="#finans">Tümü →</a></div>
        ${recent.length ? `<ul class="mini-list">${recent.map(t => `<li><span>${esc(t.category)}<small>${fmtDate(t.date)}${t.note ? ' · ' + esc(t.note) : ''}</small></span><span class="amt ${t.type} num">${t.type === 'Gelir' ? '+' : '−'}${money(t.amount)}</span></li>`).join('')}</ul>` : emptyState('Henüz işlem yok.')}
      </div>
    </div>`;
}

function niceCeil(v) {
  if (v <= 0) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  const f = v / p;
  return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * p;
}
function monthChart(n) {
  const cm = monthOf(todayISO());
  const data = [];
  for (let i = n - 1; i >= 0; i--) { const k = shiftMonth(cm, -i); data.push({ k, ...monthTotals(k) }); }
  return barChart(data.map(d => ({ label: MONTHS_SHORT[Number(d.k.slice(5)) - 1] + (d.k === cm ? ' (bu ay)' : ''), title: monthLabel(d.k), gelir: d.gelir, gider: d.gider, net: d.net, bold: d.k === cm })), true);
}
function barChart(data, withNet) {
  const n = data.length;
  if (!n) return emptyState('Bu dönemde veri yok.');
  const W = 640, H = 250, L = 64, R = 10, T = 14, B = 30;
  const iw = W - L - R, ih = H - T - B;
  // Eksen: "yuvarlak" bir adım seç, alt ve üst sınırı bu adımın katına yuvarla (ör. -50 bin … 200 bin).
  const rawMax = Math.max(1, ...data.flatMap(d => [d.gelir, d.gider]));
  const rawMin = withNet ? Math.min(0, ...data.map(d => d.net)) : 0;
  const stepV = niceCeil((rawMax - rawMin) / 4);
  const lo = Math.floor(rawMin / stepV) * stepV;
  const maxV = lo + Math.max(4, Math.ceil((rawMax - lo) / stepV)) * stepV;
  const y = v => T + ih - ((v - lo) / (maxV - lo)) * ih;
  const gw = iw / n, bw = Math.max(2, Math.min(24, gw * 0.3));
  const every = Math.ceil(n / 12);
  let g = '';
  for (let v = lo; v <= maxV + stepV / 2; v += stepV) {
    g += `<line class="grid" x1="${L}" x2="${W - R}" y1="${y(v).toFixed(1)}" y2="${y(v).toFixed(1)}"/><text class="axis" x="${L - 8}" y="${(y(v) + 4).toFixed(1)}" text-anchor="end">${shortMoney(v)}</text>`;
  }
  const pts = [];
  data.forEach((d, i) => {
    const cx = L + gw * i + gw / 2, y0 = y(0);
    g += `<rect class="b-gelir" x="${(cx - bw - 1).toFixed(1)}" y="${y(d.gelir).toFixed(1)}" width="${bw.toFixed(1)}" height="${Math.max(0, y0 - y(d.gelir)).toFixed(1)}" rx="2"><title>${esc(d.title)} gelir: ${money(d.gelir)}</title></rect>`;
    g += `<rect class="b-gider" x="${(cx + 1).toFixed(1)}" y="${y(d.gider).toFixed(1)}" width="${bw.toFixed(1)}" height="${Math.max(0, y0 - y(d.gider)).toFixed(1)}" rx="2"><title>${esc(d.title)} gider: ${money(d.gider)}</title></rect>`;
    if (i % every === 0) g += `<text class="axis" x="${cx.toFixed(1)}" y="${H - 10}" text-anchor="middle"${d.bold ? ' font-weight="700"' : ''}>${esc(d.label)}</text>`;
    pts.push([cx, y(d.net), d]);
  });
  if (withNet) {
    g += `<polyline class="net-line" points="${pts.map(p => `${p[0].toFixed(1)},${p[1].toFixed(1)}`).join(' ')}"/>`;
    g += pts.map(p => `<circle class="net-dot" cx="${p[0].toFixed(1)}" cy="${p[1].toFixed(1)}" r="4"><title>${esc(p[2].title)} net: ${money(p[2].net)}</title></circle>`).join('');
  }
  return `<svg class="chart" viewBox="0 0 ${W} ${H}" role="img" aria-label="Gelir ve gider grafiği">${g}</svg>`;
}

function categoryBreakdown(key) {
  const map = {};
  const r = monthRange(key);
  (state.cash || []).filter(t => t.type === 'Gider' && t.date >= r.from && t.date <= r.to).forEach(t => { map[t.category] = (map[t.category] || 0) + t.amount; });
  const arr = Object.entries(map).sort((a, b) => b[1] - a[1]);
  if (!arr.length) return emptyState('Bu ay henüz gider yok.');
  const total = arr.reduce((a, x) => a + x[1], 0);
  return `<ul class="hbars">${arr.map(([c, v]) => `<li><div class="hb-top"><span>${esc(c)}</span><span class="num">${money(v)} <small>%${Math.round((v / total) * 100)}</small></span></div><div class="hb-track"><div class="hb-fill" style="width:${((v / arr[0][1]) * 100).toFixed(1)}%"></div></div></li>`).join('')}</ul>`;
}

function agendaItem(it, compact = false) {
  const typeLabel = it.kind === 'debt' ? (it.debtKind === 'Borc' ? 'Borç vadesi' : 'Alacak vadesi') : EVENT_TYPES[it.type];
  const actions = compact && it.kind === 'event' ? '' : it.kind === 'event'
    ? `${iconBtn('edit-event', it.id, ICON.edit, 'Düzenle')}${iconBtn('del-event', it.id, ICON.trash, 'Sil', 'del')}`
    : `<button class="btn sm" data-action="pay-debt" data-id="${it.id}">${it.debtKind === 'Borc' ? 'Öde' : 'Tahsil et'}</button>`;
  return `<li><span class="dot t-${it.type}"></span><span class="a-main"><b>${esc(it.title)}</b><small>${esc(typeLabel)}${it.note ? ' · ' + esc(it.note) : ''}</small></span><span class="when">${fmtDate(it.date)}${it.time ? `<br>${esc(it.time)}` : ''}</span>${actions}</li>`;
}

/* ----- Satış ekranı (sepet) ----- */
function cartLines() {
  return ui.cart.map(c => ({ ...c, p: productById(c.productId) })).filter(x => x.p)
    .map(x => ({ ...x, total: round2(x.qty * x.price), short: x.qty > x.p.stock }));
}
function cartTotals() {
  const lines = cartLines();
  const gross = round2(sumBy(lines, l => l.total));
  const discount = Math.min(Math.max(0, Number(ui.posDiscount) || 0), gross);
  const total = round2(gross - discount);
  const vat = gross > 0 ? round2(sumBy(lines, l => vatOf(l.total, l.p.vatRate)) * total / gross) : 0;
  return { lines, gross, discount, total, vat };
}
function addToCart(productId, qty = 1) {
  const p = productById(productId);
  if (!p) return;
  const line = ui.cart.find(c => c.productId === productId);
  if (line) line.qty = round2(line.qty + qty);
  else ui.cart.push({ productId, qty, price: p.price });
  saveCart();
}

function renderPos() {
  const cats = [...new Set(state.products.map(p => p.category).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'tr'));
  return `
    <div class="pos">
      <div class="card">
        <input type="search" class="search full-w" id="pos-search" placeholder="Ürün ara…" value="${esc(ui.posQuery)}" aria-label="Ürün ara">
        <div class="chips"><button class="chip-btn ${ui.posCat ? '' : 'on'}" data-action="pos-cat" data-cat="">Tümü</button>${cats.map(c => `<button class="chip-btn ${ui.posCat === c ? 'on' : ''}" data-action="pos-cat" data-cat="${esc(c)}">${esc(c)}</button>`).join('')}</div>
        <div id="pos-grid">${posGrid()}</div>
      </div>
      <div class="card cart" id="pos-cart">${cartHTML()}</div>
    </div>`;
}
function posGrid() {
  const q = ui.posQuery.trim().toLocaleLowerCase('tr');
  const list = state.products.filter(p => (!ui.posCat || p.category === ui.posCat) && (!q || `${p.name} ${p.category}`.toLocaleLowerCase('tr').includes(q)));
  if (!list.length) return emptyState(state.products.length ? 'Aramaya uyan ürün yok.' : 'Henüz ürün yok. Ürünler ekranından ekleyin.');
  return `<div class="pos-grid">${list.map(p => `<button class="tile ${isLow(p) ? 'low' : ''}" data-action="cart-add" data-id="${p.id}" ${p.stock > 0 ? '' : 'disabled'}>
      <b>${esc(p.name)}</b><span class="price">${money(p.price)}</span><small>Stok: ${num(p.stock)} ${esc(p.unit)}</small></button>`).join('')}</div>`;
}
function cartHTML() {
  const t = cartTotals();
  const owner = isOwner();
  const short = t.lines.some(l => l.short);
  const pay = ui.posPay;
  return `
    <div class="card-head"><h3>Sepet <span class="muted">(${t.lines.length})</span></h3>${t.lines.length ? '<button class="btn sm ghost" data-action="cart-clear">Temizle</button>' : ''}</div>
    ${t.lines.length ? `<ul class="cart-lines">${t.lines.map(l => `<li class="cart-line ${l.short ? 'short' : ''}">
        <b>${esc(l.p.name)}</b><span class="num"><b>${money(l.total)}</b></span>
        <div class="ctrl">
          <button class="icon-btn sm" data-action="cart-step" data-id="${l.productId}" data-step="-1" aria-label="Azalt">−</button>
          <input type="number" class="cart-qty" data-id="${l.productId}" value="${l.qty}" min="0" step="any" aria-label="Miktar">
          <button class="icon-btn sm" data-action="cart-step" data-id="${l.productId}" data-step="1" aria-label="Artır">+</button>
          <span class="muted">${esc(l.p.unit)} ×</span>
          ${owner ? `<input type="number" class="cart-price" data-id="${l.productId}" value="${l.price}" min="0" step="0.01" aria-label="Birim fiyat">` : `<span>${money(l.price)}</span>`}
          <button class="icon-btn sm del" data-action="cart-remove" data-id="${l.productId}" aria-label="Sepetten çıkar" style="margin-left:auto">${ICON.x}</button>
        </div>
        ${l.short ? `<small class="warn-text">Stokta sadece ${num(l.p.stock)} ${esc(l.p.unit)} var</small>` : ''}
      </li>`).join('')}</ul>` : emptyState('Sepet boş. Ürünlere dokunarak sepete ekleyin.')}
    <div class="cart-sum">
      <div class="row"><span>Ara toplam</span><span class="num">${money(t.gross)}</span></div>
      <div class="row"><span>İndirim (₺)</span><input type="number" class="mini-input narrow" id="pos-discount" value="${esc(ui.posDiscount)}" min="0" step="0.01" placeholder="0"></div>
      <div class="row muted"><span>Toplama dahil KDV</span><span class="num">${money(t.vat)}</span></div>
      <div class="row total"><span>Toplam</span><span class="num">${money(t.total)}</span></div>
    </div>
    <div class="pay-opts" role="group" aria-label="Ödeme türü">${Object.keys(PAY_TR).map(k => `<button class="${pay === k ? 'on' : ''}" data-action="pos-pay" data-pay="${k}">${PAY_TR[k]}</button>`).join('')}</div>
    ${pay === 'Veresiye' ? `<div class="form-grid tight">
        <label class="field"><span>Müşteri adı</span><input id="pos-customer" value="${esc(ui.posCustomer)}" maxlength="60"></label>
        <label class="field"><span>Vade</span><input type="date" id="pos-due" value="${esc(ui.posDue || toISO(addDays(new Date(), 30)))}"></label></div>`
      : `<label class="field"><span>Müşteri (isteğe bağlı)</span><input id="pos-customer" value="${esc(ui.posCustomer)}" maxlength="60"></label>`}
    ${owner ? `<label class="field" style="margin-top:8px"><span>Satış tarihi</span><input type="date" id="pos-date" value="${esc(ui.posDate || todayISO())}" max="${todayISO()}"></label>` : ''}
    <p class="form-error" id="pos-error" hidden></p>
    <button class="btn primary block" data-action="pos-complete" ${!t.lines.length || short ? 'disabled' : ''} style="margin-top:12px">Satışı tamamla · ${money(t.total)}</button>`;
}
const refreshCart = () => { const el = $('#pos-cart'); if (el) el.innerHTML = cartHTML(); };

async function completeSale(btn) {
  const t = cartTotals();
  if (!t.lines.length) return;
  btn.disabled = true;
  const err = $('#pos-error');
  try {
    const owner = isOwner();
    const res = await api('POST', '/api/sales', {
      date: owner && ui.posDate ? ui.posDate : null,
      paymentType: ui.posPay,
      customer: ui.posCustomer,
      dueDate: ui.posPay === 'Veresiye' ? (ui.posDue || toISO(addDays(new Date(), 30))) : null,
      discount: t.discount,
      lines: t.lines.map(l => ({ productId: l.productId, quantity: l.qty, unitPrice: owner ? l.price : null })),
    });
    ui.cart = []; saveCart();
    Object.assign(ui, { posCustomer: '', posDue: '', posDiscount: '', posPay: 'Nakit', posDate: '' });
    await reload();
    render();
    toast(res.summary, 'ok', res.auditId);
    showReceipt(res.data.saleId);
  } catch (e) {
    if (e.status === 401) return;
    err.textContent = e.message; err.hidden = false; btn.disabled = false;
  }
}

/* ----- Fiş ----- */
function receiptHTML(s) {
  const vatGroups = {};
  s.lines.forEach(l => { vatGroups[l.vatRate] = (vatGroups[l.vatRate] || 0) + l.vat; });
  const factor = s.gross > 0 ? s.total / s.gross : 0;
  return `<div class="receipt">
    <h3>${esc(state.settings.shopName)}</h3>
    <div class="c">BİLGİ FİŞİ</div>
    <hr>
    <div>Fiş No: ${esc(s.no)}</div>
    <div>Tarih: ${fmtDate(s.date)} ${fmtClock(s.createdAt)}</div>
    <div>Kasiyer: ${esc(s.createdBy)}</div>
    ${s.customer ? `<div>Müşteri: ${esc(s.customer)}</div>` : ''}
    ${s.isCancelled ? `<div class="void">İPTAL EDİLDİ${s.cancelReason ? ' — ' + esc(s.cancelReason) : ''}</div>` : ''}
    <hr>
    <table><tbody>${s.lines.map(l => `<tr><td colspan="2">${esc(l.productName)}</td></tr><tr><td>${num(l.quantity)} ${esc(l.unit)} × ${money(l.unitPrice)} <small>%${num(l.vatRate)}</small></td><td class="r">${money(l.lineTotal)}</td></tr>`).join('')}</tbody></table>
    <hr>
    <table><tbody>
      <tr><td>Ara toplam</td><td class="r">${money(s.gross)}</td></tr>
      ${s.discount > 0 ? `<tr><td>İndirim</td><td class="r">−${money(s.discount)}</td></tr>` : ''}
      ${Object.entries(vatGroups).filter(([r]) => Number(r) > 0).map(([r, v]) => `<tr><td>KDV %${num(r)}</td><td class="r">${money(round2(v * factor))}</td></tr>`).join('')}
      <tr class="tot"><td>TOPLAM</td><td class="r">${money(s.total)}</td></tr>
      <tr><td>Ödeme</td><td class="r">${PAY_TR[s.paymentType]}</td></tr>
    </tbody></table>
    <hr>
    <div class="c">Teşekkür ederiz</div>
    <div class="c"><small>Mali değeri yoktur.</small></div>
  </div>`;
}
async function showReceipt(id) {
  try {
    const s = await api('GET', `/api/sales/${id}`);
    openPanel(`Fiş ${s.no}`, `${receiptHTML(s)}<div class="form-actions"><button class="btn" data-action="close-modal">Kapat</button><button class="btn primary" data-action="print-receipt" data-id="${s.id}">Yazdır</button></div>`);
  } catch (e) { if (e.status !== 401) toast(e.message, 'error'); }
}

/* ----- Satışlar & fişler ----- */
async function renderSales() {
  const list = await api('GET', `/api/sales?${perQuery('sales')}`);
  const active = list.filter(s => !s.isCancelled);
  const owner = isOwner();
  const byPay = k => sumBy(active.filter(s => s.paymentType === k), s => s.total);
  return `
    <div class="toolbar">${perToolbar('sales')}</div>
    <div class="toolbar"><strong class="period-label">${esc(perLabel('sales'))}</strong><span class="spacer"></span><a class="btn primary" href="#satis">+ Yeni satış</a></div>
    <div class="kpis">
      ${kpi('Satış adedi', num(active.length), `${list.length - active.length} iptal`)}
      ${kpi('Ciro', money(sumBy(active, s => s.total)), `Nakit ${shortMoney(byPay('Nakit'))} · Kart ${shortMoney(byPay('Kart'))} · Veresiye ${shortMoney(byPay('Veresiye'))}`, 'pos')}
      ${kpi('Ortalama sepet', money(active.length ? sumBy(active, s => s.total) / active.length : 0), `İndirim toplamı ${money(sumBy(active, s => s.discount))}`)}
      ${owner ? kpi('Brüt kâr', money(sumBy(active, s => s.total - (s.costTotal || 0))), 'Satış − maliyet') : kpi('KDV toplamı', money(sumBy(active, s => s.vatTotal)), 'Satışlara dahil')}
    </div>
    <div class="card flush">${list.length ? `<div class="table-wrap"><table>
      <thead><tr><th>Fiş</th><th>Tarih</th><th>Ürünler</th><th>Ödeme</th><th>Müşteri</th><th class="r">Toplam</th><th>Kasiyer</th><th class="r">İşlem</th></tr></thead>
      <tbody>${list.map(s => `<tr class="${s.isCancelled ? 'cancelled' : ''}">
        <td><b>${esc(s.no)}</b>${s.isCancelled ? ' <span class="pill danger">İptal</span>' : ''}</td>
        <td>${fmtDate(s.date)}<span class="sub">${fmtClock(s.createdAt)}</span></td>
        <td class="wrap">${esc(s.lines.map(l => `${num(l.quantity)} ${l.unit} ${l.productName}`).join(', '))}</td>
        <td><span class="pill">${PAY_TR[s.paymentType]}</span></td>
        <td>${esc(s.customer || '—')}</td>
        <td class="r"><b>${money(s.total)}</b>${s.discount > 0 ? `<span class="sub">İnd. ${money(s.discount)}</span>` : ''}</td>
        <td>${esc(s.createdBy)}</td>
        <td class="r actions">${iconBtn('show-receipt', s.id, ICON.receipt, 'Fişi göster')}${owner && !s.isCancelled ? `<button class="btn sm danger-soft" data-action="cancel-sale" data-id="${s.id}" data-no="${s.no}">İptal</button>` : ''}</td>
      </tr>`).join('')}</tbody></table></div>` : emptyState('Bu dönemde satış yok.')}</div>`;
}

/* ----- Ürünler ----- */
function renderProducts() {
  const ps = state.products, owner = isOwner();
  const low = ps.filter(isLow).length;
  const fill = ps.length ? (sumBy(ps, p => (p.capacity ? Math.min(1, p.stock / p.capacity) : 0)) / ps.length) * 100 : 0;
  return `
    <div class="toolbar">
      <input type="search" class="search" id="prod-search" placeholder="Ürün, kategori veya tedarikçi ara…" value="${esc(ui.prodQuery)}" aria-label="Ürün ara">
      <label class="check-inline"><input type="checkbox" id="prod-low" ${ui.prodLowOnly ? 'checked' : ''}> Sadece kritik stok</label>
      <span class="spacer"></span>
      ${owner ? '<button class="btn primary" data-action="add-product">+ Ürün ekle</button>' : ''}
    </div>
    <div class="kpis">
      ${kpi('Ürün çeşidi', num(ps.length), `${low} ürün kritik stokta`, low ? 'neg' : '')}
      ${owner ? kpi('Stok değeri (maliyet)', money(sumBy(ps, p => p.stock * p.cost)), 'Son alış fiyatı üzerinden') : ''}
      ${kpi('Stok değeri (satış)', money(sumBy(ps, p => p.stock * p.price)), owner ? `Olası brüt kâr ${money(sumBy(ps, p => p.stock * (p.price - p.cost)))}` : '')}
      ${kpi('Ortalama doluluk', `%${Math.round(fill)}`, `Boş kapasite: ${num(sumBy(ps, p => Math.max(0, p.capacity - p.stock)))} birim`)}
    </div>
    <div class="card flush" id="prod-table">${productTable()}</div>
    <p class="muted note">Stok miktarı sadece hareketlerle değişir (satış, giriş, fire, sayım…). Her değişiklik "Stok Hareketleri"nde ve ürünün geçmişinde görünür.</p>`;
}
function productTable() {
  const q = ui.prodQuery.trim().toLocaleLowerCase('tr'), owner = isOwner();
  const list = state.products
    .filter(p => (!ui.prodLowOnly || isLow(p)) && (!q || `${p.name} ${p.category} ${supplierById(p.supplierId)?.name || ''}`.toLocaleLowerCase('tr').includes(q)))
    .sort((a, b) => Number(isLow(b)) - Number(isLow(a)) || a.name.localeCompare(b.name, 'tr'));
  if (!list.length) return emptyState(state.products.length ? 'Filtreye uyan ürün yok.' : 'Henüz ürün eklenmedi.');
  return `<div class="table-wrap"><table>
    <thead><tr><th>Ürün</th><th>Stok / Kapasite</th><th class="r">Min.</th>${owner ? '<th class="r">Alış</th>' : ''}<th class="r">Satış</th><th class="r">KDV</th>${owner ? '<th class="r">Marj</th>' : ''}<th>Tedarikçi</th><th class="r">İşlem</th></tr></thead>
    <tbody>${list.map(p => {
      const ratio = p.capacity ? Math.min(100, (p.stock / p.capacity) * 100) : 0;
      const cls = isLow(p) ? 'low' : ratio < 35 ? 'mid' : '';
      const margin = p.price ? Math.round(((p.price - p.cost) / p.price) * 100) : 0;
      return `<tr>
        <td><b>${esc(p.name)}</b>${isLow(p) ? ' <span class="pill danger">Kritik</span>' : ''}<span class="sub">${esc(p.category || '—')}</span></td>
        <td><div class="meter"><div class="meter-track"><div class="meter-fill ${cls}" style="width:${ratio.toFixed(1)}%"></div></div><small>${num(p.stock)} / ${num(p.capacity)} ${esc(p.unit)}</small></div></td>
        <td class="r">${num(p.minStock)}</td>
        ${owner ? `<td class="r">${money(p.cost)}</td>` : ''}
        <td class="r">${money(p.price)}</td>
        <td class="r">%${num(p.vatRate)}</td>
        ${owner ? `<td class="r">%${margin}</td>` : ''}
        <td>${esc(supplierById(p.supplierId)?.name || '—')}</td>
        <td class="r actions">
          <button class="btn sm" data-action="cart-add-go" data-id="${p.id}" ${p.stock > 0 ? '' : 'disabled title="Stok yok"'}>${ICON.cart} Sat</button>
          <button class="btn sm" data-action="stock-in" data-id="${p.id}">${ICON.inbox} Giriş</button>
          ${iconBtn('product-moves', p.id, ICON.list, 'Hareket geçmişi')}
          ${iconBtn('adjust-stock', p.id, ICON.sliders, 'Fire / sayım / düzeltme')}
          ${owner ? iconBtn('edit-product', p.id, ICON.edit, 'Düzenle') + iconBtn('del-product', p.id, ICON.trash, 'Sil', 'del') : ''}
        </td></tr>`;
    }).join('')}</tbody></table></div>`;
}

async function showProductMoves(id, at) {
  const r = await api('GET', `/api/products/${id}/movements${at ? `?at=${at}` : ''}`);
  const p = r.product;
  openPanel(`Hareket geçmişi: ${p.name}`, `
    <div class="toolbar">
      <span>Güncel stok: <b>${num(p.stock)} ${esc(p.unit)}</b></span><span class="spacer"></span>
      <label class="check-inline">Şu tarihteki stok: <input type="date" class="date-in" id="stock-at" data-id="${p.id}" value="${r.at || todayISO()}" max="${todayISO()}"></label>
      ${r.stockAt !== null && r.stockAt !== undefined ? `<b class="stock-at">${num(r.stockAt)} ${esc(p.unit)}</b>` : ''}
    </div>
    ${r.movements.length ? `<div class="table-wrap"><table>
      <thead><tr><th>Tarih</th><th>Tür</th><th class="r">Miktar</th><th class="r">Sonraki stok</th><th>Açıklama</th><th>Kaydeden</th></tr></thead>
      <tbody>${r.movements.map(m => `<tr><td>${fmtDate(m.date)}<span class="sub">${fmtClock(m.createdAt)}</span></td><td>${esc(m.typeLabel)}${m.saleNo ? ` <span class="sub">${esc(m.saleNo)}</span>` : ''}</td>
        <td class="r amt ${m.quantity >= 0 ? 'Gelir' : 'Gider'}">${m.quantity > 0 ? '+' : ''}${num(m.quantity)}</td><td class="r">${num(m.stockAfter)}</td><td class="wrap">${esc(m.note)}</td><td>${esc(m.createdBy)}</td></tr>`).join('')}</tbody></table></div>`
      : emptyState('Bu ürün için hareket yok.')}
    <div class="form-actions"><button class="btn" data-action="adjust-stock" data-id="${p.id}">Fire / sayım / düzeltme</button><button class="btn primary" data-action="close-modal">Kapat</button></div>`, true);
}

/* ----- Stok hareketleri ----- */
async function renderMovements() {
  const list = await api('GET', `/api/movements?${perQuery('mov')}${ui.movType ? `&type=${ui.movType}` : ''}`);
  const sumType = t => list.filter(m => m.type === t).length;
  return `
    <div class="toolbar">${perToolbar('mov')}</div>
    <div class="toolbar">
      <strong class="period-label">${esc(perLabel('mov'))}</strong>
      <select class="date-in" id="mov-type" aria-label="Hareket türü"><option value="">Tüm hareketler</option>${Object.entries(MOVE_TYPES).map(([k, l]) => `<option value="${k}" ${ui.movType === k ? 'selected' : ''}>${l}</option>`).join('')}</select>
    </div>
    <div class="kpis">
      ${kpi('Hareket sayısı', num(list.length), '')}
      ${kpi('Satış çıkışı', num(sumType('Satis')), 'hareket')}
      ${kpi('Stok girişi', num(sumType('Giris')), 'hareket')}
      ${kpi('Fire / iade / düzeltme', num(sumType('Fire') + sumType('Iade') + sumType('Duzeltme') + sumType('Sayim')), 'hareket')}
    </div>
    <div class="card flush">${list.length ? `<div class="table-wrap"><table>
      <thead><tr><th>Tarih</th><th>Ürün</th><th>Tür</th><th class="r">Miktar</th><th class="r">Sonraki stok</th><th>Açıklama</th><th>Kaydeden</th></tr></thead>
      <tbody>${list.map(m => `<tr><td>${fmtDate(m.date)}<span class="sub">${fmtClock(m.createdAt)}</span></td>
        <td><a href="#" data-action="product-moves" data-id="${m.productId}">${esc(m.product || '?')}</a></td>
        <td>${esc(m.typeLabel)}${m.saleNo ? `<span class="sub">${esc(m.saleNo)}</span>` : ''}</td>
        <td class="r amt ${m.quantity >= 0 ? 'Gelir' : 'Gider'}">${m.quantity > 0 ? '+' : ''}${num(m.quantity)} ${esc(m.unit || '')}</td>
        <td class="r">${num(m.stockAfter)}</td><td class="wrap">${esc(m.note)}</td><td>${esc(m.createdBy)}</td></tr>`).join('')}</tbody></table></div>`
      : emptyState('Bu dönemde hareket yok.')}</div>`;
}

/* ----- Takvim ----- */
function renderCalendar() {
  const [y, m] = ui.calMonth.split('-').map(Number);
  const first = new Date(y, m - 1, 1);
  const daysInMonth = new Date(y, m, 0).getDate();
  const offset = (first.getDay() + 6) % 7; // Pazartesi = 0
  const cells = Math.ceil((offset + daysInMonth) / 7) * 7;
  const start = addDays(first, -offset);
  const items = calendarItems(toISO(start), toISO(addDays(start, cells - 1)));
  const byDate = {};
  items.forEach(i => { (byDate[i.date] = byDate[i.date] || []).push(i); });
  const today = todayISO();
  let grid = DAYS_SHORT.map(d => `<div class="cal-dow">${d}</div>`).join('');
  for (let i = 0; i < cells; i++) {
    const d = addDays(start, i), ds = toISO(d), list = byDate[ds] || [];
    const cls = ['cal-cell', monthOf(ds) !== ui.calMonth && 'out', ds === today && 'today', ds === ui.calSelected && 'selected'].filter(Boolean).join(' ');
    grid += `<button type="button" class="${cls}" data-action="cal-select" data-date="${ds}" aria-label="${longDate(ds)}${list.length ? `, ${list.length} kayıt` : ''}">
      <span class="cal-num">${d.getDate()}</span>
      <span class="cal-chips">${list.slice(0, 3).map(it => `<span class="chip t-${it.type}">${it.time ? `<i>${esc(it.time)}</i> ` : ''}${esc(it.title)}</span>`).join('')}${list.length > 3 ? `<span class="more">+${list.length - 3} daha</span>` : ''}</span>
    </button>`;
  }
  const monthEvents = items.filter(i => monthOf(i.date) === ui.calMonth);
  const legend = Object.entries(EVENT_TYPES).map(([k, l]) => `<span class="chip t-${k}">${l}</span>`).join('') + (isOwner() ? '<span class="chip t-vade-borc">Borç vadesi</span><span class="chip t-vade-alacak">Alacak vadesi</span>' : '');
  const sel = ui.calSelected, dayList = itemsOn(sel);
  return `
    <div class="toolbar">
      <div class="month-nav">
        <button class="icon-btn" data-action="cal-month" data-step="-1" aria-label="Önceki ay">‹</button>
        <strong>${monthLabel(ui.calMonth)}</strong>
        <button class="icon-btn" data-action="cal-month" data-step="1" aria-label="Sonraki ay">›</button>
      </div>
      <button class="btn sm ghost" data-action="cal-month" data-step="0">Bugün</button>
      <span class="spacer"></span>
      <button class="btn primary" data-action="add-event" data-date="${sel}">+ Etkinlik ekle</button>
    </div>
    <div class="cal-legend">${legend}</div>
    <div class="cal-layout">
      <div class="card"><div class="cal">${grid}</div></div>
      <div class="stack">
        <div class="card">
          <div class="card-head"><h3>${longDate(sel)}</h3><button class="btn sm" data-action="add-event" data-date="${sel}">+ Ekle</button></div>
          ${dayList.length ? `<ul class="agenda">${dayList.map(it => agendaItem(it)).join('')}</ul>` : emptyState('Bu gün için kayıt yok.')}
        </div>
        <div class="card">
          <div class="card-head"><h3>${monthLabel(ui.calMonth)} ajandası</h3><span class="muted">${monthEvents.length} kayıt</span></div>
          ${monthEvents.length ? `<ul class="agenda">${monthEvents.map(it => agendaItem(it)).join('')}</ul>` : emptyState('Bu ay kayıt yok.')}
        </div>
      </div>
    </div>`;
}
