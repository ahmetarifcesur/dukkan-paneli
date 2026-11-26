'use strict';
/* Kayıt formları. Doğrulamanın asıl yeri sunucudur; burada sadece hızlı geri bildirim veriyoruz. */

const supplierOptions = () => [{ value: '', label: '— Seçilmedi —' }, ...state.suppliers.map(s => ({ value: s.id, label: s.name }))];
const vatOptions = () => VAT_RATES.map(r => ({ value: r, label: `%${r}` }));
const toggleField = (form, name, show) => { const el = form.querySelector(`[data-field="${name}"]`); if (el) el.hidden = !show; };
const intOrNull = v => (v === '' || v == null ? null : Number(v));

function txForm(type, tx) {
  const t = tx || { type, amount: null, category: CATEGORIES[type][0], date: todayISO(), note: '', vatAmount: 0 };
  const rate = tx ? (VAT_RATES.find(r => vatOf(tx.amount, r) === tx.vatAmount) ?? 0) : 0;
  const cats = type => { const c = [...CATEGORIES[type]]; if (t.category && t.type === type && !c.includes(t.category)) c.unshift(t.category); return c; };
  openForm({
    title: tx ? 'İşlemi düzenle' : type === 'Gelir' ? 'Gelir ekle' : 'Gider ekle',
    fields: [
      { name: 'type', label: 'Tür', type: 'select', options: ['Gelir', 'Gider'], value: t.type },
      { name: 'amount', label: 'Tutar (₺, KDV dahil)', type: 'number', min: 0.01, step: '0.01', value: t.amount },
      { name: 'category', label: 'Kategori', type: 'select', options: cats(t.type), value: t.category },
      { name: 'vatRate', label: 'KDV oranı', type: 'select', options: vatOptions(), value: rate, hint: 'Faturalı giderlerde indirilecek KDV için' },
      { name: 'date', label: 'Tarih', type: 'date', value: t.date },
      { name: 'note', label: 'Açıklama', value: t.note, full: true, maxlength: 120, placeholder: 'İsteğe bağlı' },
    ],
    onChange: (form, e) => {
      if (e.target.name !== 'type') return;
      form.elements.category.innerHTML = cats(e.target.value).map(c => `<option>${esc(c)}</option>`).join('');
    },
    onSubmit: async v => {
      if (!(v.amount > 0)) return 'Tutar sıfırdan büyük olmalı.';
      const body = { type: v.type, amount: v.amount, vatRate: Number(v.vatRate), category: v.category, date: v.date || null, note: v.note };
      await mutate(tx ? 'PUT' : 'POST', tx ? `/api/cash/${tx.id}` : '/api/cash', body);
      const r = perRange('fin');
      if (currentView === 'finans' && (v.date < r.from || v.date > r.to)) {
        const p = ui.periods.fin;
        if (p.mode === 'aralik') p.mode = 'gun';
        p.date = v.date;
        render();
      }
    },
  });
}

function productForm(p) {
  const x = p || { name: '', category: '', unit: 'adet', stock: 0, capacity: 100, minStock: 10, cost: null, price: null, vatRate: state.settings.defaultVatRate, supplierId: null };
  openForm({
    title: p ? 'Ürünü düzenle' : 'Yeni ürün',
    fields: [
      { name: 'name', label: 'Ürün adı', value: x.name, full: true, maxlength: 80 },
      { name: 'category', label: 'Kategori', value: x.category, datalist: [...new Set(state.products.map(q => q.category).filter(Boolean))], maxlength: 40 },
      { name: 'unit', label: 'Birim', value: x.unit, datalist: UNITS, maxlength: 15 },
      p ? { type: 'info', html: `Mevcut stok: <b>${num(p.stock)} ${esc(p.unit)}</b>. Stoğu değiştirmek için ürün listesindeki <b>Giriş</b> veya <b>Fire / sayım / düzeltme</b> işlemlerini kullanın; böylece her değişiklik hareket geçmişine yazılır.` }
        : { name: 'stock', label: 'Açılış stoğu', type: 'number', min: 0, step: 'any', value: x.stock },
      { name: 'capacity', label: 'Depo kapasitesi', type: 'number', min: 1, step: 'any', value: x.capacity, hint: 'Rafta/depoda tutulabilecek en fazla miktar' },
      { name: 'minStock', label: 'Kritik stok seviyesi', type: 'number', min: 0, step: 'any', value: x.minStock, hint: 'Bu seviyeye inince uyarı verilir' },
      { name: 'cost', label: 'Alış fiyatı (₺, KDV dahil)', type: 'number', min: 0, step: '0.01', value: x.cost },
      { name: 'price', label: 'Satış fiyatı (₺, KDV dahil)', type: 'number', min: 0, step: '0.01', value: x.price },
      { name: 'vatRate', label: 'KDV oranı', type: 'select', options: vatOptions(), value: x.vatRate },
      { name: 'supplierId', label: 'Tedarikçi', type: 'select', options: supplierOptions(), value: x.supplierId ?? '' },
    ],
    onSubmit: async v => {
      if (!v.name) return 'Ürün adı gerekli.';
      const body = { ...v, vatRate: Number(v.vatRate), supplierId: intOrNull(v.supplierId), stock: p ? null : v.stock };
      await mutate(p ? 'PUT' : 'POST', p ? `/api/products/${p.id}` : '/api/products', body);
    },
  });
}

function stockInForm(p) {
  const owner = isOwner();
  const free = Math.max(0, round2(p.capacity - p.stock));
  openForm({
    title: `Stok girişi: ${p.name}`,
    submitText: 'Girişi kaydet',
    fields: [
      { type: 'info', html: `Stok: <b>${num(p.stock)} / ${num(p.capacity)} ${esc(p.unit)}</b> · Boş kapasite: <b>${num(free)} ${esc(p.unit)}</b>` },
      { name: 'quantity', label: `Miktar (${p.unit})`, type: 'number', min: 0.01, max: free, step: 'any', value: free ? Math.min(free, Math.max(1, round2(p.capacity * 0.5 - p.stock))) : '' },
      owner ? { name: 'unitCost', label: 'Birim alış fiyatı (₺)', type: 'number', min: 0, step: '0.01', value: p.cost } : { type: 'info', html: 'Alış fiyatı olarak ürünün kayıtlı maliyeti kullanılır.' },
      { name: 'supplierId', label: 'Tedarikçi', type: 'select', options: supplierOptions(), value: p.supplierId ?? '' },
      { name: 'date', label: 'Tarih', type: 'date', value: todayISO() },
      ...(owner ? [
        { name: 'credit', label: 'Vadeli / veresiye alım (tedarikçiye borç yaz, kasadan para çıkmaz)', type: 'checkbox', value: false },
        { name: 'dueDate', label: 'Vade tarihi', type: 'date', value: toISO(addDays(new Date(), 30)), hidden: true },
      ] : []),
      { name: 'note', label: 'Not', value: '', full: true, maxlength: 120, placeholder: 'ör. irsaliye no' },
    ],
    onChange: form => { if (form.elements.credit) toggleField(form, 'dueDate', form.elements.credit.checked); },
    onSubmit: async v => {
      if (!(v.quantity > 0)) return 'Miktar sıfırdan büyük olmalı.';
      if (v.quantity > free) return `Kapasite aşılıyor: en fazla ${num(free)} ${p.unit} eklenebilir.`;
      await mutate('POST', `/api/products/${p.id}/stock-in`, {
        quantity: v.quantity, unitCost: owner ? v.unitCost : null, supplierId: intOrNull(v.supplierId), date: v.date || null,
        credit: !!v.credit, dueDate: v.credit ? v.dueDate || null : null, note: v.note,
      });
    },
  });
}

function adjustForm(p) {
  const labels = { Fire: 'Fire / zayi miktarı', Iade: 'Tedarikçiye iade miktarı', Sayim: 'Sayılan (gerçek) miktar', Duzeltme: 'Düzeltme miktarı (+ / −)' };
  const hints = {
    Fire: 'Bozulan, kırılan, son kullanma tarihi geçen ürün. Stoktan düşer, raporda fire kaybı olarak görünür.',
    Iade: 'Tedarikçiye geri gönderilen ürün. Stoktan düşer.',
    Sayim: 'Rafta saydığınız gerçek miktarı girin; fark otomatik hesaplanır.',
    Duzeltme: 'Hatalı girişi düzeltmek için artı ya da eksi miktar girin (ör. -2). Açıklama zorunludur.',
  };
  openForm({
    title: `Stok düzeltme: ${p.name}`,
    fields: [
      { type: 'info', html: `Mevcut stok: <b>${num(p.stock)} ${esc(p.unit)}</b> · Kapasite: ${num(p.capacity)}` },
      { name: 'type', label: 'İşlem', type: 'select', options: Object.keys(labels).map(k => ({ value: k, label: MOVE_TYPES[k] })), value: 'Fire', full: true },
      { name: 'quantity', label: labels.Fire, type: 'number', step: 'any', value: '' },
      { name: 'date', label: 'Tarih', type: 'date', value: todayISO() },
      { name: 'note', label: 'Açıklama', value: '', full: true, maxlength: 120 },
    ],
    onChange: (form, e) => {
      const t = form.elements.type.value;
      form.querySelector('[data-field="quantity"] > span').textContent = labels[t];
      let hint = form.querySelector('.adjust-hint');
      if (!hint) { hint = document.createElement('small'); hint.className = 'adjust-hint muted full'; form.querySelector('[data-field="type"]').appendChild(hint); }
      hint.textContent = hints[t];
      if (e.target.name === 'type' && t === 'Sayim') form.elements.quantity.value = p.stock;
    },
    onSubmit: async v => {
      if (v.quantity === null) return 'Miktar girin.';
      await mutate('POST', `/api/products/${p.id}/adjust`, { type: v.type, quantity: v.quantity, note: v.note, date: v.date || null });
    },
  });
}

function pickProduct(title, filter, next) {
  const list = state.products.filter(filter);
  if (!list.length) { toast(state.products.length ? 'Uygun ürün yok.' : 'Önce ürün ekleyin.', 'error'); return; }
  openForm({
    title, submitText: 'Devam',
    fields: [{ name: 'productId', label: 'Ürün', type: 'select', full: true, options: list.map(p => ({ value: p.id, label: `${p.name} — stok ${num(p.stock)} ${p.unit}` })), value: list[0].id }],
    onSubmit: v => { const p = productById(Number(v.productId)); return p ? () => next(p) : 'Ürün bulunamadı.'; },
  });
}

function supplierForm(s) {
  const x = s || { name: '', phone: '', email: '', note: '' };
  openForm({
    title: s ? 'Tedarikçiyi düzenle' : 'Yeni tedarikçi',
    fields: [
      { name: 'name', label: 'Firma adı', value: x.name, full: true, maxlength: 80 },
      { name: 'phone', label: 'Telefon', type: 'tel', value: x.phone, maxlength: 25 },
      { name: 'email', label: 'E-posta', type: 'email', value: x.email, maxlength: 80 },
      { name: 'note', label: 'Not', type: 'textarea', value: x.note, full: true, maxlength: 200 },
    ],
    onSubmit: async v => {
      if (!v.name) return 'Firma adı gerekli.';
      await mutate(s ? 'PUT' : 'POST', s ? `/api/suppliers/${s.id}` : '/api/suppliers', v);
    },
  });
}

function debtForm(kind, d) {
  const x = d || { kind, party: '', amount: null, openingPaid: 0, date: todayISO(), dueDate: toISO(addDays(new Date(), 30)), note: '' };
  const linked = d?.linked;
  openForm({
    title: d ? 'Kaydı düzenle' : kind === 'Borc' ? 'Yeni borç' : 'Yeni alacak',
    fields: [
      linked ? { type: 'info', html: `Bu kayıt ${d.saleNo ? `<b>${esc(d.saleNo)}</b> numaralı satıştan` : 'bir stok girişinden'} oluştu; tutar ve tür değiştirilemez.` } : null,
      { name: 'kind', label: 'Tür', type: 'select', options: [{ value: 'Borc', label: 'Borç (ben ödeyeceğim)' }, { value: 'Alacak', label: 'Alacak (bana ödenecek)' }], value: x.kind, disabled: linked },
      { name: 'party', label: 'Kişi / firma', value: x.party, datalist: state.suppliers.map(s => s.name), maxlength: 80 },
      { name: 'amount', label: 'Toplam tutar (₺)', type: 'number', min: 0.01, step: '0.01', value: x.amount, disabled: linked },
      { name: 'openingPaid', label: 'Önceden ödenmiş kısım (₺)', type: 'number', min: 0, step: '0.01', value: x.openingPaid, hint: 'Sistem kullanılmadan önce yapılmış ödemeler. Yeni ödemeleri "Öde / Tahsil et" ile girin.' },
      { name: 'date', label: 'Kayıt tarihi', type: 'date', value: x.date },
      { name: 'dueDate', label: 'Vade tarihi', type: 'date', value: x.dueDate || '', hint: 'Boş bırakılırsa vadesiz' },
      { name: 'note', label: 'Açıklama', value: x.note, full: true, maxlength: 120 },
    ].filter(Boolean),
    onSubmit: async v => {
      if (!v.party) return 'Kişi / firma adı gerekli.';
      const body = { kind: linked ? d.kind : v.kind, party: v.party, amount: linked ? d.amount : v.amount, openingPaid: v.openingPaid ?? 0, date: v.date || null, dueDate: v.dueDate || null, note: v.note };
      if (!(body.amount > 0)) return 'Tutar sıfırdan büyük olmalı.';
      await mutate(d ? 'PUT' : 'POST', d ? `/api/debts/${d.id}` : '/api/debts', body);
      ui.debtTab = body.kind;
      if (currentView === 'borclar') render();
    },
  });
}

function payForm(d) {
  const isB = d.kind === 'Borc';
  openForm({
    title: isB ? `Ödeme yap: ${d.party}` : `Tahsilat: ${d.party}`,
    submitText: isB ? 'Ödemeyi kaydet' : 'Tahsilatı kaydet',
    fields: [
      { type: 'info', html: `Toplam ${money(d.amount)} · ${isB ? 'Ödenen' : 'Tahsil edilen'} ${money(d.paid)} · <b>Kalan ${money(d.remaining)}</b>` },
      { name: 'amount', label: 'Tutar (₺)', type: 'number', min: 0.01, max: d.remaining, step: '0.01', value: d.remaining },
      { name: 'date', label: 'Tarih', type: 'date', value: todayISO() },
      { name: 'record', label: isB ? 'Kasadan çıkış olarak gidere yaz' : 'Kasaya giriş olarak gelire yaz', type: 'checkbox', value: true },
    ],
    onSubmit: async v => {
      if (!(v.amount > 0)) return 'Tutar sıfırdan büyük olmalı.';
      if (round2(v.amount) > d.remaining) return `Tutar kalan miktardan (${money(d.remaining)}) büyük olamaz.`;
      await mutate('POST', `/api/debts/${d.id}/payments`, { amount: v.amount, date: v.date || null, record: v.record });
    },
  });
}

function eventForm(ev, date) {
  const x = ev || { title: '', date: date || todayISO(), time: '', type: 'diger', note: '' };
  openForm({
    title: ev ? 'Etkinliği düzenle' : 'Yeni etkinlik',
    fields: [
      { name: 'title', label: 'Başlık', value: x.title, full: true, maxlength: 80, placeholder: 'ör. Tedarikçi teslimatı' },
      { name: 'date', label: 'Tarih', type: 'date', value: x.date },
      { name: 'time', label: 'Saat', type: 'time', value: x.time, hint: 'İsteğe bağlı' },
      { name: 'type', label: 'Tür', type: 'select', options: Object.entries(EVENT_TYPES).map(([value, label]) => ({ value, label })), value: x.type },
      ev ? null : { name: 'repeat', label: 'Tekrar', type: 'select', options: [{ value: 0, label: 'Tekrar yok' }, { value: 3, label: '3 ay boyunca her ay' }, { value: 6, label: '6 ay boyunca her ay' }, { value: 12, label: '12 ay boyunca her ay' }], value: 0 },
      { name: 'note', label: 'Not', type: 'textarea', value: x.note, full: true, maxlength: 200 },
    ].filter(Boolean),
    onSubmit: async v => {
      if (!v.title) return 'Başlık gerekli.';
      if (!v.date) return 'Tarih seçin.';
      await mutate(ev ? 'PUT' : 'POST', ev ? `/api/events/${ev.id}` : '/api/events', { ...v, repeat: Number(v.repeat || 0) });
      ui.calSelected = v.date; ui.calMonth = monthOf(v.date);
      if (currentView === 'takvim') render();
    },
  });
}

function userForm(u) {
  openForm({
    title: u ? `Kullanıcı: ${u.userName}` : 'Yeni kullanıcı',
    fields: [
      u ? { type: 'info', html: `Kullanıcı adı: <b>${esc(u.userName)}</b>` } : { name: 'userName', label: 'Kullanıcı adı', value: '', maxlength: 32, autocomplete: 'off', hint: 'Küçük harf, rakam, nokta, tire' },
      { name: 'displayName', label: 'Görünen ad', value: u?.displayName || '', maxlength: 60 },
      { name: 'role', label: 'Rol', type: 'select', options: [{ value: 'Calisan', label: 'Çalışan' }, { value: 'Sahip', label: 'Sahip (tam yetki)' }], value: u?.role || 'Calisan' },
      u ? { name: 'isActive', label: 'Hesap aktif', type: 'checkbox', value: u.isActive } : null,
      { name: 'password', label: u ? 'Yeni şifre (değiştirmek için)' : 'Şifre', type: 'password', value: '', autocomplete: 'new-password', hint: 'En az 8 karakter' },
    ].filter(Boolean),
    onSubmit: async v => {
      if (!u && (v.password || '').length < 8) return 'Şifre en az 8 karakter olmalı.';
      if (u) await mutate('PUT', `/api/users/${u.id}`, { displayName: v.displayName, role: v.role, isActive: v.isActive, newPassword: v.password || null });
      else await mutate('POST', '/api/users', { userName: v.userName, displayName: v.displayName, role: v.role, password: v.password });
    },
  });
}
