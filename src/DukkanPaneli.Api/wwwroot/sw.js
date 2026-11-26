// Dükkan Paneli service worker: uygulama kabuğunu önbelleğe alır (bağlantı kesilse de açılır)
// ve bildirime tıklanınca ilgili ekranı açar. API yanıtları asla önbelleğe alınmaz.
const CACHE = 'dukkan-shell-v3';
const SHELL = ['/', '/index.html', '/style.css', '/js/core.js', '/js/views-ops.js', '/js/views-admin.js', '/js/forms.js', '/js/main.js', '/manifest.webmanifest', '/icons/icon-192.png', '/icons/icon-512.png'];

self.addEventListener('install', e => {
  e.waitUntil(caches.open(CACHE).then(c => c.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', e => {
  e.waitUntil(
    caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', e => {
  const url = new URL(e.request.url);
  if (e.request.method !== 'GET' || url.origin !== location.origin || url.pathname.startsWith('/api/')) return;
  // Önce ağ (güncel sürüm), ağ yoksa önbellek.
  e.respondWith(
    fetch(e.request)
      .then(res => {
        if (res.ok) { const copy = res.clone(); caches.open(CACHE).then(c => c.put(e.request, copy)); }
        return res;
      })
      .catch(() => caches.match(e.request).then(r => r || caches.match('/index.html')))
  );
});

self.addEventListener('notificationclick', e => {
  e.notification.close();
  const link = (e.notification.data && e.notification.data.link) || '#dashboard';
  e.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(list => {
      for (const c of list) { c.navigate('/' + link); return c.focus(); }
      return self.clients.openWindow('/' + link);
    })
  );
});
