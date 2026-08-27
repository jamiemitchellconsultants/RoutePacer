self.importScripts('./service-worker-assets.js');
const cacheNamePrefix = 'routepacer-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const base = new URL('/', self.origin);
const assets = self.assetsManifest.assets.map(asset => new Request(new URL(asset.url, base), { integrity: asset.hash, cache: 'no-cache' }));
self.addEventListener('install', event => event.waitUntil(caches.open(cacheName).then(cache => cache.addAll(assets))));
self.addEventListener('activate', event => event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName).map(key => caches.delete(key))))));
self.addEventListener('fetch', event => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.origin || url.pathname.startsWith('/api') || url.pathname.startsWith('/health') || (url.pathname === '/open' && url.search)) return;
  if (request.mode === 'navigate') event.respondWith(fetch(request).catch(() => caches.match('/index.html')));
  else event.respondWith(caches.open(cacheName).then(async cache => { const cached = await cache.match(request); const network = fetch(request).then(response => { if (response.ok) cache.put(request, response.clone()); return response; }).catch(() => cached); return cached || network; }));
});
