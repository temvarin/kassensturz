// Offline support for the published app (in development, service-worker.js does nothing).
// Based on the Blazor PWA template. The build generates service-worker-assets.js: every published file with its
// SHA-256 hash, plus a version derived from all hashes. A release with any changed file therefore gets a new
// service worker, which downloads the complete new version into a fresh cache before it takes over.
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall()));
self.addEventListener('activate', event => event.waitUntil(onActivate()));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2$/, /\.png$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

// The app's base address: the folder this service worker is in.
const baseUrl = new URL('./', self.location);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall() {
  const assetsRequests = self.assetsManifest.assets
    .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
    .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
    .map(asset => new Request(new URL(asset.url, baseUrl), { integrity: asset.hash, cache: 'no-cache' }));
  await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate() {
  const cacheKeys = await caches.keys();
  await Promise.all(cacheKeys
    .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
    .map(key => caches.delete(key)));
}

async function onFetch(event) {
  let cachedResponse = null;
  if (event.request.method === 'GET') {
    // Pages of the app (e.g. /budget after a reload) are all served by index.html. Sign-in and sign-out under
    // /.auth/ are handled by the hosting platform, so those navigations must reach the network.
    const shouldServeIndexHtml = event.request.mode === 'navigate'
      && !manifestUrlList.some(url => url === event.request.url)
      && !new URL(event.request.url).pathname.startsWith('/.auth/');
    const request = shouldServeIndexHtml ? new URL('index.html', baseUrl).href : event.request;
    const cache = await caches.open(cacheName);
    cachedResponse = await cache.match(request);
  }
  return cachedResponse || fetch(event.request);
}
