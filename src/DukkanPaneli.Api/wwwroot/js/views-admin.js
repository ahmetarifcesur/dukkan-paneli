'use strict';
/* Yönetim ekranları (çoğu yalnızca dükkan sahibine açık). */

const LINK_HINT = 'Otomatik oluşan kayıt (satış, ödeme veya stok girişi). Değiştirmek için ilgili işlemi iptal edin ya da İşlem Geçmişi\'nden geri alın.';

/* ----- Gelir & gider ----- */
function filteredTx() {
  const q = ui.finQuery.trim().toLocaleLowerCase('tr');
  const { from, to } = perRange('fin');
  return (state.cash || [])
    .filter(t => t.date >= from && t.date <= to && (ui.finType === 'all' || t.type === ui.finType) && (!q || `${t.category} ${t.note}`.toLocaleLowerCase('tr').includes(q)))
    .sort((a, b) => b.date.localeCompare(a.date) || b.id - a.id);
}
function renderFinance() {
  const r = perRange('fin');
  const pr = perPrev('fin');
  // Seçilen dönem (ya da karşılaştırılan önceki dönem) elimizdeki veriden eskiyse önce onu yükle.
  if (isOwner() && state.cashFrom && pr.from < state.cashFrom) return ensureCash(pr.from).then(() => renderFinance());
  const t = totalsFor(r.from, r.to);
  const prev = totalsFor(pr.from, pr.to);
  const pl = PER_PREV[ui.periods.fin.mode];
  const segBtn = (v, l) => `<button class="${ui.finType === v ? 'on' : ''}" data-action="fin-type" data-type="${v}">${l}</button>`;
  return `
    <div class="toolbar">${perToolbar('fin')}</div>
    <div class="toolbar">
      <strong class="period-label">${esc(perLabel('fin'))}</strong>
      <div class="seg">${segBtn('all', 'Tümü')}${segBtn('Gelir', 'Gelir')}${segBtn('Gider', 'Gider')}</div>
      <input type="search" class="search" id="fin-search" placeholder="Kategori veya açıklama ara…" value="${esc(ui.finQuery)}" aria-label="İşlem ara">
      <span class="spacer"></span>
      <button class="btn ghost" data-action="export-csv">CSV indir</button>
      <button class="btn primary" data-action="add-tx" data-type="Gelir">+ Gelir</button>
      <button class="btn danger-soft" data-action="add-tx" data-type="Gider">+ Gider</button>
    </div>
    <div class="kpis">
      ${kpi('Toplam gelir', money(t.gelir), `${pl} ${money(prev.gelir)} ${deltaHTML(pct(t.gelir, prev.gelir))}`, 'pos')}
      ${kpi('Toplam gider', money(t.gider), `${pl} ${money(prev.gider)} ${deltaHTML(pct(t.gider, prev.gider), true)}`, 'neg')}
      ${kpi('Net kâr / zarar', money(t.net), `${pl} ${money(prev.net)} ${deltaHTML(pct(t.net, prev.net))}`, signCls(t.net))}
      ${kpi('İşlem sayısı', num(t.count), t.gelir ? `Kâr marjı %${Math.round((t.net / t.gelir) * 100)}` : '')}
    </div>
    <div class="card flush" id="fin-table">${finTable()}</div>
    <p class="muted note">Nakit esası: veresiye satışlar ve vadeli alımlar, tahsil edildiğinde / ödendiğinde gelir ya da gider olarak görünür. ${ICON.lock} işaretli kayıtlar otomatik oluşur.</p>`;
}
function finTable() {
  const list = filteredTx();
  if (!list.length) return emptyState('Bu filtreye uyan işlem yok.');
  return `<div class="table-wrap"><table data-sort="fin">
    <thead><tr><th>Tarih</th><th>Tür</th><th>Kategori</th><th>Açıklama</th><th class="r">Tutar</th><th class="r">İşlem</th></tr></thead>
    <tbody>${list.map(t => `<tr>
      <td data-v="${t.date}">${fmtDate(t.date)}</td>
      <td><span class="pill ${t.type}">${t.type}</span></td>
      <td>${esc(t.category)}</td>
      <td class="muted wrap">${esc(t.note)}${t.createdBy ? `<span class="sub">${esc(t.createdBy)}</span>` : ''}</td>
      <td class="r amt ${t.type}" data-v="${t.type === 'Gelir' ? t.amount : -t.amount}">${t.type === 'Gelir' ? '+' : '−'}${money(t.amount)}${t.vatAmount ? `<span class="sub">KDV ${money(t.vatAmount)}</span>` : ''}</td>
      <td class="r actions">${t.linked
        ? `${t.saleId ? iconBtn('show-receipt', t.saleId, ICON.receipt, 'Fişi göster') : ''}<span class="lock" title="${esc(LINK_HINT)}">${ICON.lock}</span>`
        : iconBtn('edit-tx', t.id, ICON.edit, 'Düzenle') + iconBtn('del-tx', t.id, ICON.trash, 'Sil', 'del')}</td>
    </tr>`).join('')}</tbody></table></div>`;
}

/* ----- Tedarikçiler ----- */
function renderSuppliers() {
  const ss = state.suppliers;
  const debtOf = s => sumBy((state.debts || []).filter(d => d.kind === 'Borc' && (d.supplierId === s.id || (!d.supplierId && d.party === s.name))), d => d.remaining);
  const rows = ss.map(s => {
    const prods = state.products.filter(p => p.supplierId === s.id);
    const debt = debtOf(s), lowCount = prods.filter(isLow).length;
    return `<tr>
      <td><b>${esc(s.name)}</b>${s.note ? `<span class="sub">${esc(s.note)}</span>` : ''}</td>
      <td>${s.phone ? `<a href="tel:${esc(s.phone.replace(/[^\d+]/g, ''))}">${esc(s.phone)}</a>` : '—'}${s.email ? `<span class="sub"><a href="mailto:${esc(s.email)}">${esc(s.email)}</a></span>` : ''}</td>
      <td class="wrap">${prods.length ? esc(prods.map(p => p.name).join(', ')) : '<span class="muted">—</span>'}${lowCount ? ` <span class="pill warn">${lowCount} kritik</span>` : ''}</td>
      <td class="r" data-v="${debt}">${debt ? `<b class="amt Gider">${money(debt)}</b>` : '<span class="muted">Borç yok</span>'}</td>
      <td class="r actions">${iconBtn('edit-supplier', s.id, ICON.edit, 'Düzenle')}${iconBtn('del-supplier', s.id, ICON.trash, 'Sil', 'del')}</td></tr>`;
  }).join('');
  return `
    <div class="toolbar"><span class="muted">${ss.length} tedarikçi · toplam borç ${money(sumBy(ss, debtOf))}</span><span class="spacer"></span><button class="btn primary" data-action="add-supplier">+ Tedarikçi ekle</button></div>
    <div class="card flush">${ss.length ? `<div class="table-wrap"><table data-sort="suppliers">
      <thead><tr><th>Tedarikçi</th><th>İletişim</th><th>Sağladığı ürünler</th><th class="r">Açık borç</th><th class="r">İşlem</th></tr></thead>
      <tbody>${rows}</tbody></table></div>` : emptyState('Henüz tedarikçi eklenmedi.')}</div>`;
}

/* ----- Borç & alacak ----- */
function debtStatus(d) {
  if (d.remaining <= 0) return '<span class="pill ok">Kapandı</span>';
  if (!d.dueDate) return '<span class="pill">Vadesiz</span>';
  const diff = daysBetween(todayISO(), d.dueDate);
  if (diff < 0) return `<span class="pill danger">${-diff} gün gecikti</span>`;
  if (diff === 0) return '<span class="pill warn">Bugün</span>';
  if (diff <= 7) return `<span class="pill warn">${diff} gün kaldı</span>`;
  return `<span class="pill">${diff} gün kaldı</span>`;
}
function renderDebts() {
  const kind = ui.debtTab, today = todayISO();
  const all = (state.debts || []).filter(d => d.kind === kind);
  const list = all.filter(d => ui.debtShowClosed || d.remaining > 0)
    .sort((a, b) => Number(a.remaining <= 0) - Number(b.remaining <= 0) || (a.dueDate || '9999').localeCompare(b.dueDate || '9999'));
  const open = all.filter(d => d.remaining > 0), overdue = open.filter(isOverdue);
  const in30 = open.filter(d => d.dueDate && d.dueDate >= today && daysBetween(today, d.dueDate) <= 30);
  const isB = kind === 'Borc';
  const segBtn = (v, l) => `<button class="${kind === v ? 'on' : ''}" data-action="debt-tab" data-tab="${v}">${l}</button>`;
  return `
    <div class="toolbar">
      <div class="seg">${segBtn('Borc', `Borçlarım (${openDebts('Borc').length})`)}${segBtn('Alacak', `Alacaklarım (${openDebts('Alacak').length})`)}</div>
      <label class="check-inline"><input type="checkbox" id="debt-closed" ${ui.debtShowClosed ? 'checked' : ''}> Kapananları göster</label>
      <span class="spacer"></span>
      <button class="btn primary" data-action="add-debt" data-kind="${kind}">+ ${isB ? 'Borç' : 'Alacak'} ekle</button>
    </div>
    <div class="kpis">
      ${kpi(isB ? 'Toplam kalan borç' : 'Toplam kalan alacak', money(sumBy(open, d => d.remaining)), `${open.length} açık kayıt`, isB ? 'neg' : 'pos')}
      ${kpi('Vadesi geçmiş', money(sumBy(overdue, d => d.remaining)), `${overdue.length} kayıt`, overdue.length ? 'neg' : '')}
      ${kpi('30 gün içinde vadesi gelen', money(sumBy(in30, d => d.remaining)), `${in30.length} kayıt`)}
      ${kpi(isB ? 'Bugüne kadar ödenen' : 'Bugüne kadar tahsil edilen', money(sumBy(all, d => d.paid)), `Toplam ${money(sumBy(all, d => d.amount))} üzerinden`)}
    </div>
    <div class="card flush">${list.length ? `<div class="table-wrap"><table data-sort="debts">
      <thead><tr><th>${isB ? 'Alacaklı' : 'Borçlu'}</th><th>Kayıt</th><th>Vade</th><th class="r">Tutar</th><th class="r">${isB ? 'Ödenen' : 'Tahsil edilen'}</th><th>Kalan</th><th>Durum</th><th class="r">İşlem</th></tr></thead>
      <tbody>${list.map(d => {
        const ratio = d.amount ? Math.min(100, (d.paid / d.amount) * 100) : 0;
        return `<tr>
          <td><b>${esc(d.party)}</b>${d.saleNo ? ` <span class="pill">${esc(d.saleNo)}</span>` : ''}${d.note ? `<span class="sub">${esc(d.note)}</span>` : ''}</td>
          <td data-v="${d.date}">${fmtDate(d.date)}</td><td data-v="${d.dueDate || ''}">${fmtDate(d.dueDate)}</td>
          <td class="r" data-v="${d.amount}">${money(d.amount)}</td>
          <td class="r" data-v="${d.paid}">${money(d.paid)}${d.payments.length ? `<span class="sub"><a href="#" data-action="debt-payments" data-id="${d.id}">${d.payments.length} ödeme</a></span>` : ''}</td>
          <td data-v="${d.remaining}"><div class="meter"><div class="meter-track"><div class="meter-fill" style="width:${ratio.toFixed(1)}%"></div></div><small><b>${money(d.remaining)}</b></small></div></td>
          <td data-v="${d.remaining <= 0 ? 1e6 : d.dueDate ? daysBetween(todayISO(), d.dueDate) : 1e5}">${debtStatus(d)}</td>
          <td class="r actions">
            ${d.remaining > 0 ? `<button class="btn sm" data-action="pay-debt" data-id="${d.id}">${isB ? 'Öde' : 'Tahsil et'}</button>` : ''}
            ${iconBtn('edit-debt', d.id, ICON.edit, 'Düzenle')}
            ${d.linked ? `<span class="lock" title="Satışa / stok girişine bağlı kayıt; silmek için ilgili işlemi iptal edin.">${ICON.lock}</span>` : iconBtn('del-debt', d.id, ICON.trash, 'Sil', 'del')}
          </td></tr>`;
      }).join('')}</tbody></table></div>` : emptyState(isB ? 'Açık borç yok.' : 'Açık alacak yok.')}</div>`;
}
function showDebtPayments(id) {
  const d = (state.debts || []).find(x => x.id === id);
  if (!d) return;
  openPanel(`Ödemeler: ${d.party}`, `
    <p class="muted">Toplam ${money(d.amount)}${d.openingPaid ? ` · Önceden ödenen ${money(d.openingPaid)}` : ''} · Kalan <b>${money(d.remaining)}</b></p>
    ${d.payments.length ? `<div class="table-wrap"><table data-sort="payments"><thead><tr><th>Tarih</th><th class="r">Tutar</th><th>Kaydeden</th><th class="r"></th></tr></thead>
      <tbody>${d.payments.map(p => `<tr><td data-v="${p.date}">${fmtDate(p.date)}</td><td class="r" data-v="${p.amount}">${money(p.amount)}</td><td>${esc(p.createdBy)}</td>
        <td class="r"><button class="btn sm danger-soft" data-action="del-payment" data-debt="${d.id}" data-id="${p.id}">İptal et</button></td></tr>`).join('')}</tbody></table></div>`
      : emptyState('Ödeme yok.')}
    <div class="form-actions"><button class="btn primary" data-action="close-modal">Kapat</button></div>`);
}

/* ----- Raporlar ----- */
async function renderReports() {
  const r = await api('GET', `/api/reports?${perQuery('rep')}`);
  const range = perRange('rep');
  const s = r.sales;
  const daily = r.daily.map(d => ({ label: fmtDate(d.date).replace(/ \d{4}$/, ''), title: fmtDate(d.date), gelir: d.gelir, gider: d.gider, net: d.gelir - d.gider }));
  const cat = type => r.byCategory.filter(c => c.type === type);
  return `
    <div class="toolbar no-print">${perToolbar('rep')}</div>
    <div class="toolbar">
      <strong class="period-label">${esc(state.settings.shopName)} · ${esc(perLabel('rep'))}</strong>
      <span class="spacer"></span>
      <a class="btn no-print" href="/api/reports/excel?from=${range.from}&to=${range.to}" download>Excel indir</a>
      <button class="btn primary no-print" data-action="print-report">Yazdır / PDF</button>
    </div>
    <div class="kpis">
      ${kpi('Satış cirosu', money(s.revenue), `${num(s.count)} satış · ${num(s.cancelled)} iptal`, 'pos')}
      ${kpi('Brüt kâr', money(s.grossProfit), `Marj %${num(s.margin)} · maliyet ${shortMoney(s.cost)}`, signCls(s.grossProfit))}
      ${kpi('Ortalama sepet', money(s.avgBasket), `İndirim ${money(s.discount)}`)}
      ${kpi('Kasa: net', money(r.totals.net), `Giren ${shortMoney(r.totals.gelir)} · Çıkan ${shortMoney(r.totals.gider)}`, signCls(r.totals.net))}
      ${kpi('Ödenecek KDV (tahmini)', money(r.vat.payable), `Hesaplanan ${shortMoney(r.vat.output)} − indirilecek ${shortMoney(r.vat.input)}`)}
      ${kpi('Fire / zayi kaybı', money(r.losses.value), `${num(r.losses.count)} hareket`, r.losses.value ? 'neg' : '')}
    </div>
    <div class="grid-2">
      <div class="card"><div class="card-head"><h3>Günlük gelir / gider</h3>
        <div class="legend"><span><i style="background:var(--income)"></i>Gelir</span><span><i style="background:var(--expense)"></i>Gider</span></div></div>
        ${barChart(daily, false)}</div>
      <div class="card"><h3>En çok satan ürünler</h3>
        ${r.topProducts.length ? `<div class="table-wrap"><table class="compact" data-sort="top"><thead><tr><th>Ürün</th><th class="r">Miktar</th><th class="r">Ciro</th><th class="r">Kâr</th></tr></thead>
          <tbody>${r.topProducts.map((p, i) => `<tr><td data-v="${esc(p.name)}">${i + 1}. ${esc(p.name)}</td><td class="r" data-v="${p.quantity}">${num(p.quantity)} ${esc(p.unit)}</td><td class="r" data-v="${p.revenue}">${money(p.revenue)}</td><td class="r" data-v="${p.profit}">${money(p.profit)}</td></tr>`).join('')}</tbody></table></div>`
          : emptyState('Bu dönemde satış yok.')}</div>
    </div>
    <div class="grid-3">
      <div class="card"><h3>Ödeme türleri</h3>${r.payments.length ? `<ul class="mini-list">${r.payments.map(p => `<li><span>${PAY_TR[p.type]}<small>${num(p.count)} satış</small></span><b>${money(p.total)}</b></li>`).join('')}</ul>` : emptyState('Veri yok.')}</div>
      <div class="card"><h3>Kasiyer bazında satış</h3>${r.bySeller.length ? `<ul class="mini-list">${r.bySeller.map(p => `<li><span>${esc(p.name)}<small>${num(p.count)} satış</small></span><b>${money(p.total)}</b></li>`).join('')}</ul>` : emptyState('Veri yok.')}</div>
      <div class="card"><h3>Kategoriler</h3>${r.byCategory.length ? `<ul class="mini-list">${['Gelir', 'Gider'].flatMap(tp => cat(tp).map(c => `<li><span>${esc(c.category)}<small>${tp} · ${num(c.count)} kayıt</small></span><b class="amt ${tp}">${money(c.total)}</b></li>`)).join('')}</ul>` : emptyState('Veri yok.')}</div>
    </div>
    <p class="muted note">Kasa rakamları nakit esasına göredir (veresiye/vadeli tutarlar ödendiğinde sayılır). Brüt kâr = satış tutarı − satılan malın alış maliyeti. KDV rakamları tahminidir; beyanname için muhasebecinize danışın.</p>`;
}

/* ----- İşlem geçmişi ----- */
const FIELD_TR = {
  Id: 'No', Name: 'Ad', Category: 'Kategori', Unit: 'Birim', Stock: 'Stok', Capacity: 'Kapasite', MinStock: 'Kritik stok', Cost: 'Alış fiyatı',
  Price: 'Satış fiyatı', VatRate: 'KDV %', SupplierId: 'Tedarikçi no', IsArchived: 'Silindi (arşiv)', Amount: 'Tutar', VatAmount: 'KDV tutarı',
  Date: 'Tarih', Note: 'Açıklama', Type: 'Tür', Quantity: 'Miktar', StockAfter: 'Sonraki stok', UnitCost: 'Birim maliyet', ProductId: 'Ürün no',
  SaleId: 'Satış no', Party: 'Kişi / firma', DueDate: 'Vade', OpeningPaid: 'Önceden ödenen', Kind: 'Tür', Title: 'Başlık', Time: 'Saat',
  Phone: 'Telefon', Email: 'E-posta', Total: 'Toplam', Gross: 'Ara toplam', Discount: 'İndirim', PaymentType: 'Ödeme', Customer: 'Müşteri',
  IsCancelled: 'İptal', CancelReason: 'İptal nedeni', CancelledAt: 'İptal zamanı', Value: 'Değer', Key: 'Anahtar', CreatedBy: 'Kaydeden',
  CreatedAt: 'Kayıt zamanı', ProductName: 'Ürün', LineTotal: 'Satır tutarı', UnitPrice: 'Birim fiyat', VatTotal: 'KDV', CostTotal: 'Maliyet',
  DebtId: 'Borç no', DebtPaymentId: 'Ödeme no', StockMovementId: 'Stok hareketi no',
};
const STATE_TR = { Added: ['Eklendi', 'ok'], Modified: ['Değişti', 'warn'], Deleted: ['Silindi', 'danger'] };

async function renderHistory() {
  const h = ui.hist;
  const qs = new URLSearchParams({ page: h.page });
  if (h.from) qs.set('from', h.from);
  if (h.to) qs.set('to', h.to);
  if (h.user) qs.set('user', h.user);
  if (h.q) qs.set('q', h.q);
  const r = await api('GET', `/api/history?${qs}`);
  const pages = Math.max(1, Math.ceil(r.total / r.pageSize));
  return `
    <div class="toolbar">
      <label class="check-inline">Başlangıç <input type="date" class="date-in hist-filter" data-field="from" value="${esc(h.from)}"></label>
      <label class="check-inline">Bitiş <input type="date" class="date-in hist-filter" data-field="to" value="${esc(h.to)}"></label>
      <select class="date-in hist-filter" data-field="user" aria-label="Kullanıcı"><option value="">Tüm kullanıcılar</option>${r.users.map(u => `<option ${u === h.user ? 'selected' : ''}>${esc(u)}</option>`).join('')}</select>
      <input type="search" class="search" id="hist-q" placeholder="Açıklamada ara (ör. ürün adı, fiş no)…" value="${esc(h.q)}">
      ${h.from || h.to || h.user || h.q ? '<button class="btn sm ghost" data-action="hist-reset">Filtreyi temizle</button>' : ''}
    </div>
    <p class="muted note">Yapılan her değişiklik burada önce/sonra değerleriyle saklanır. Yanlış bir işlemi <b>Geri al</b> ile tersine çevirebilirsiniz; geri alma da kaydedilir ve istenirse yeniden uygulanabilir. Sonradan aynı kayıtları değiştiren bir işlem varsa, veri bozulmasın diye önce o işlem geri alınmalıdır.</p>
    <div class="card flush">
      ${r.items.length ? `<ul class="hist-list">${r.items.map(histItem).join('')}</ul>` : emptyState('Kayıt bulunamadı.')}
      <div class="pager"><span class="muted">${num(r.total)} kayıt · sayfa ${r.page}/${pages}</span>
        <button class="btn sm" data-action="hist-page" data-step="-1" ${r.page <= 1 ? 'disabled' : ''}>‹ Yeni</button>
        <button class="btn sm" data-action="hist-page" data-step="1" ${r.page >= pages ? 'disabled' : ''}>Eski ›</button></div>
    </div>`;
}
function histItem(a) {
  const open = ui.hist.open[a.id];
  const badges = [
    a.undoneAt && `<span class="pill">Geri alındı</span>`,
    a.undoOfAuditId && `<span class="pill warn">Geri alma işlemi (#${a.undoOfAuditId})</span>`,
    !a.undoable && !a.undoneAt && `<span class="pill" title="Kullanıcı/sistem işlemleri geri alınamaz">Geri alınamaz</span>`,
  ].filter(Boolean).join(' ');
  return `<li class="hist-item ${a.undoneAt ? 'undone' : ''}">
    <div class="hist-top">
      <span class="muted">#${a.id}</span>
      <span class="sum">${esc(a.summary)}</span>
      ${badges}
      ${a.changeCount ? `<button class="btn sm ghost" data-action="hist-toggle" data-id="${a.id}">${open ? 'Gizle' : `Ayrıntı (${a.changeCount})`}</button>` : ''}
      ${a.undoable && !a.undoneAt ? `<button class="btn sm" data-action="hist-undo" data-id="${a.id}" data-sum="${esc(a.summary)}">${ICON.undo} ${a.undoOfAuditId ? 'Yeniden uygula' : 'Geri al'}</button>` : ''}
    </div>
    <div class="hist-meta">${fmtTime(a.at)} · ${esc(a.userName)}</div>
    ${open ? `<div class="diff">${open === true ? '<span class="muted">Yükleniyor…</span>' : diffHTML(open)}</div>` : ''}
  </li>`;
}
const showVal = v => (v == null || v === '' ? '—' : /^\d{4}-\d{2}-\d{2}T/.test(v) ? fmtTime(v) : /^\d{4}-\d{2}-\d{2}$/.test(v) ? fmtDate(v) : v);
function diffHTML(d) {
  return d.changes.map(c => {
    const [stLabel, stCls] = STATE_TR[c.state] || [c.state, ''];
    // Eklenen/silinen kayıtlarda boş alanlar gürültü yaratır.
    if (c.state !== 'Modified') c.fields = c.fields.filter(f => ![null, '', '0', 'hayır'].includes(c.state === 'Added' ? f.after : f.before));
    c.fields.forEach(f => { f.before = showVal(f.before); f.after = showVal(f.after); });
    return `<div><div class="diff-head">${esc(c.label)} #${esc(c.key)} <span class="pill ${stCls}">${stLabel}</span></div>
      <div class="table-wrap"><table><thead><tr><th>Alan</th>${c.state !== 'Added' ? '<th>Önce</th>' : ''}${c.state !== 'Deleted' ? '<th>Sonra</th>' : ''}</tr></thead>
      <tbody>${c.fields.map(f => `<tr><td>${esc(FIELD_TR[f.field] || f.field)}</td>${c.state !== 'Added' ? `<td class="${c.state === 'Modified' ? 'old' : ''}">${esc(f.before ?? '—')}</td>` : ''}${c.state !== 'Deleted' ? `<td class="${c.state === 'Modified' ? 'new' : ''}">${esc(f.after ?? '—')}</td>` : ''}</tr>`).join('')}</tbody></table></div></div>`;
  }).join('');
}

/* ----- Çalışanlar ----- */
async function renderStaff() {
  const users = await api('GET', '/api/users');
  ui.users = users; // "Düzenle" düğmesi (edit-user) kullanıcıyı buradan bulur
  const active = users.filter(u => u.isActive);
  const staffCount = active.filter(u => u.role === 'Calisan').length;
  const best = [...users].sort((a, b) => b.month.total - a.month.total)[0];
  const stats = s => `${money(s.total)}<span class="sub">${num(s.count)} satış</span>`;
  return `
    <div class="toolbar"><span class="muted">${users.length} kullanıcı · ${active.length} aktif</span><span class="spacer"></span><button class="btn primary" data-action="add-user">+ Kullanıcı ekle</button></div>
    <div class="kpis">
      ${kpi('Aktif kullanıcı', num(active.length), `${staffCount} çalışan · ${active.length - staffCount} sahip`)}
      ${kpi('Bugünkü satış', money(sumBy(users, u => u.today.total)), `${num(sumBy(users, u => u.today.count))} satış`, 'pos')}
      ${kpi('Bu ayki satış', money(sumBy(users, u => u.month.total)), `${num(sumBy(users, u => u.month.count))} satış`)}
      ${kpi('Ayın en çok satanı', best?.month.total ? esc(best.displayName) : '—', best?.month.total ? money(best.month.total) : 'Bu ay satış yok')}
    </div>
    <div class="card flush"><div class="table-wrap"><table data-sort="staff">
      <thead><tr><th>Kullanıcı</th><th>Rol</th><th>Durum</th><th class="r">Bugün</th><th class="r">Bu ay</th><th>Son satış</th><th>Son giriş</th><th class="r">İşlem</th></tr></thead>
      <tbody>${users.map(u => `<tr class="${u.isActive ? '' : 'inactive'}">
        <td data-v="${esc(u.displayName)}"><b>${esc(u.displayName)}</b>${u.id === state.me.id ? ' <span class="pill">Siz</span>' : ''}<span class="sub">${esc(u.userName)}</span></td>
        <td><span class="pill">${u.role === 'Sahip' ? 'Sahip' : 'Çalışan'}</span></td>
        <td>${u.isActive ? '<span class="pill ok">Aktif</span>' : '<span class="pill danger">Devre dışı</span>'}</td>
        <td class="r" data-v="${u.today.total}">${stats(u.today)}</td>
        <td class="r" data-v="${u.month.total}">${stats(u.month)}</td>
        <td data-v="${u.lastSaleAt || ''}">${u.lastSaleAt ? fmtTime(u.lastSaleAt) : '—'}</td>
        <td data-v="${u.lastLoginAt || ''}">${u.lastLoginAt ? fmtTime(u.lastLoginAt) : '<span class="muted">Hiç girmedi</span>'}</td>
        <td class="r actions">${iconBtn('edit-user', u.id, ICON.edit, 'Düzenle: ad, rol, durum, şifre')}</td>
      </tr>`).join('')}</tbody></table></div></div>
    <p class="muted note">Çalışanlar satış yapabilir, stok girebilir, takvimi kullanabilir; gelir-gider, borçlar, raporlar, maliyetler ve geçmiş onlara kapalıdır. İşten ayrılan birini silmek yerine <b>devre dışı</b> bırakın: geçmiş satışları korunur. Rol, durum ya da şifre değişince kişinin açık oturumları hemen kapanır. Satış rakamlarına iptal edilen satışlar dahil değildir.</p>`;
}

/* ----- Ayarlar ----- */
async function renderSettings() {
  const owner = isOwner();
  const backups = owner ? await api('GET', '/api/admin/backups') : null;
  const perm = 'Notification' in window ? Notification.permission : 'unsupported';
  const permText = { granted: 'Açık', denied: 'Engellendi (tarayıcı ayarlarından izin verin)', default: 'Kapalı', unsupported: 'Bu tarayıcı desteklemiyor' }[perm];
  return `
    <div class="grid-2 even">
      ${owner ? `<div class="card">
        <h3>Dükkan bilgileri</h3>
        <form id="settings-form" class="form-grid settings-form" novalidate>
          <label class="field full"><span>Dükkan adı</span><input name="shopName" value="${esc(state.settings.shopName)}" maxlength="60" required></label>
          <label class="field"><span>Varsayılan KDV oranı (%)</span><select name="defaultVatRate">${VAT_RATES.map(r => `<option ${r === state.settings.defaultVatRate ? 'selected' : ''}>${r}</option>`).join('')}</select></label>
          <p class="form-error full" hidden></p>
          <div class="form-actions full"><button class="btn primary" type="submit">Kaydet</button></div>
        </form>
      </div>` : ''}
      <div class="card">
        <h3>Hesabım</h3>
        <p class="muted">${esc(state.me.displayName)} · <b>${esc(state.me.userName)}</b> · ${owner ? 'Sahip' : 'Çalışan'}</p>
        <form id="password-form" class="form-grid settings-form" novalidate>
          <label class="field full"><span>Mevcut şifre</span><input type="password" name="current" autocomplete="current-password"></label>
          <label class="field"><span>Yeni şifre</span><input type="password" name="next" autocomplete="new-password"></label>
          <label class="field"><span>Yeni şifre (tekrar)</span><input type="password" name="next2" autocomplete="new-password"></label>
          <p class="form-error full" hidden></p>
          <div class="form-actions full"><button class="btn primary" type="submit">Şifreyi değiştir</button></div>
        </form>
      </div>
      ${owner ? `<div class="card">
        <h3>Kullanıcılar</h3>
        <p class="muted">Çalışan ekleme, düzenleme, devre dışı bırakma ve satış performansı artık ayrı bir sayfada.</p>
        <div class="btn-row"><a class="btn primary" href="#calisanlar">Çalışanlar sayfasına git</a></div>
      </div>` : ''}
      <div class="card">
        <h3>Bildirimler</h3>
        <p class="muted">Kritik stok, yaklaşan/geciken ödemeler ve bugünkü etkinlikler için cihaz bildirimi. Durum: <b>${permText}</b></p>
        <div class="btn-row">${perm === 'default' ? '<button class="btn primary" data-action="enable-notifications">Bildirimlere izin ver</button>' : ''}<button class="btn" data-action="show-notifications">Bildirimleri göster</button></div>
        <p class="muted note">Telefonda tarayıcı menüsünden "Ana ekrana ekle" diyerek paneli uygulama gibi kullanabilirsiniz. Bildirimler, panel açıkken veya arka planda çalışırken gelir.</p>
      </div>
      ${owner ? `<div class="card">
        <h3>Yedekleme</h3>
        ${backups.enabled
          ? `<p class="muted">Sunucu her gün otomatik yedek alır; son ${num(backups.keep)} yedek saklanır.</p>
            ${backups.files.length ? `<ul class="mini-list">${backups.files.slice(0, 5).map(f => `<li><span>${esc(f.name)}<small>${fmtTime(f.createdAt)}</small></span><b>${num(Math.max(1, Math.round(f.size / 1024)))} KB</b></li>`).join('')}</ul>` : '<p class="muted">Henüz otomatik yedek yok; ilki bir saat içinde alınır.</p>'}`
          : '<p class="muted"><b>Otomatik yedek kapalı</b> (Backup:Enabled ayarı).</p>'}
        <p class="muted note">Yedekler sunucuda <code>${esc(backups.directory)}</code> klasöründedir. Geri yüklemek için uygulamayı durdurup sunucuda <code>dotnet run --project src/DukkanPaneli.Api -- restore-backup &lt;dosya&gt;</code> çalıştırın. Bilgisayar bozulursa diye yedeği ayrıca indirip başka bir yerde saklayın.</p>
        <div class="btn-row"><a class="btn" href="/api/admin/backup" download>Şimdi yedek indir (.db)</a><a class="btn" href="/api/reports/excel?from=2000-01-01&to=2100-12-31" download>Tüm veriyi Excel'e aktar</a></div>
      </div>
      <div class="card">
        <h3>Veri</h3>
        <p class="muted">Örnek veri yükleme ve tüm verileri silme geri alınamaz (kullanıcı hesapları korunur). Önce yedek almanız önerilir.</p>
        <div class="btn-row"><button class="btn" data-action="reset-demo">Örnek verileri yükle</button><button class="btn danger-soft" data-action="clear-all">Tüm verileri sil</button></div>
      </div>` : ''}
    </div>`;
}

/* ----- Asistan ----- */
const ASSISTANT_SUGGESTIONS = [
  'Bu ay geçen aya göre nasıl gidiyor?',
  'Hangi ürünlerden sipariş vermeliyim?',
  'Kârı artırmak için ne önerirsin?',
  'Geciken alacaklarım ve borçlarım neler?',
  'Satmayan ya da az satan ürünler hangileri?',
];

async function renderAssistant() {
  ui.assistantStatus ??= await api('GET', '/api/assistant/status');
  const st = ui.assistantStatus;
  if (!st.enabled) return `
    <div class="card assistant-setup">
      <h3>Asistan henüz kurulmadı</h3>
      <p>Asistan sorularınızı yapay zekâ ile (Claude) cevaplar. Kullanmak için sunucuda bir API anahtarı tanımlanmalı:</p>
      <ol>
        <li><b>console.anthropic.com</b> adresinde hesap açın, ödeme yöntemi ekleyin ve isterseniz aylık harcama sınırı koyun.</li>
        <li><b>API Keys</b> bölümünden yeni bir anahtar oluşturun.</li>
        <li>Anahtarı sunucuda <code>ANTHROPIC_API_KEY</code> ortam değişkeni olarak tanımlayıp uygulamayı yeniden başlatın.</li>
      </ol>
      <p class="muted">Anahtar sadece sunucuda durur, tarayıcıya gönderilmez. Her soru küçük bir ücretle faturalanır.</p>
    </div>`;
  const msgs = ui.chat.map(m => `<div class="chat-msg ${m.role}${m.error ? ' error' : ''}"><div class="chat-bubble">${esc(m.text)}</div></div>`).join('');
  return `
    <div class="assistant">
      <div class="card assistant-chat">
        <div class="chat-log" id="chat-log" aria-live="polite">
          ${msgs || `<div class="chat-empty"><p>Dükkanınızla ilgili bir soru sorun. Asistan güncel satış, stok, kasa ve borç-alacak özetini okuyarak cevaplar.</p>
            <div class="chips">${ASSISTANT_SUGGESTIONS.map(s => `<button class="chip" data-action="assistant-suggest" data-q="${esc(s)}">${esc(s)}</button>`).join('')}</div></div>`}
          ${ui.chatBusy ? '<div class="chat-msg assistant"><div class="chat-bubble typing">Düşünüyor…</div></div>' : ''}
        </div>
        <div class="chat-input">
          <textarea id="chat-q" rows="2" maxlength="1000" placeholder="Sorunuzu yazın… (Enter ile gönder, Shift+Enter yeni satır)" ${ui.chatBusy ? 'disabled' : ''}></textarea>
          <button class="btn primary" data-action="assistant-send" ${ui.chatBusy ? 'disabled' : ''}>Gönder</button>
        </div>
      </div>
      <div class="toolbar assistant-foot">
        <span class="muted">Model: ${esc(st.model)} · Müşteri ve çalışan adları takma adla gönderilir. Cevaplar yanlış olabilir; önemli kararlarda rakamları kontrol edin.</span>
        <span class="spacer"></span>
        <button class="btn sm ghost" data-action="assistant-context">Hangi veriler gönderiliyor?</button>
        ${ui.chat.length ? '<button class="btn sm ghost" data-action="assistant-clear">Sohbeti temizle</button>' : ''}
      </div>
    </div>`;
}

async function assistantAsk(question) {
  const q = (question || '').trim();
  if (!q || ui.chatBusy) return;
  const history = ui.chat.filter(m => !m.error).map(m => ({ role: m.role, text: m.text }));
  ui.chat.push({ role: 'user', text: q });
  ui.chatBusy = true;
  render();
  try {
    const r = await api('POST', '/api/assistant/ask', { question: q, history });
    ui.chat.push({ role: 'assistant', text: r.answer });
  } catch (e) {
    if (e.status === 401) return;
    ui.chat.push({ role: 'assistant', text: e.message, error: true });
  } finally {
    ui.chatBusy = false;
  }
  if (currentView === 'asistan') {
    await render();
    const log = $('#chat-log'); if (log) log.scrollTop = log.scrollHeight;
    $('#chat-q')?.focus();
  }
}
