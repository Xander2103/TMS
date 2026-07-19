/*
 * App-shell service worker: makes the portal installable and instant to open.
 * Deliberately NO offline data sync — API calls are always network-only; the UI
 * shows an offline banner and blocks mutations while disconnected.
 */
const SHELL_CACHE = 'ts-shell-v1'
const ASSET_CACHE = 'ts-assets-v1'
const SHELL_URLS = ['/', '/index.html', '/manifest.webmanifest', '/favicon.svg']

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(SHELL_CACHE)
      .then((cache) => cache.addAll(SHELL_URLS))
      .then(() => self.skipWaiting()),
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(keys.filter((key) => key !== SHELL_CACHE && key !== ASSET_CACHE).map((key) => caches.delete(key))),
      )
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', (event) => {
  const request = event.request
  if (request.method !== 'GET') return

  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return

  // Data is never served from cache: the app must not present stale business state.
  if (url.pathname.startsWith('/api/')) return

  // Navigations: network-first, cached shell as the offline fallback.
  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request)
        .then((response) => {
          const copy = response.clone()
          caches.open(SHELL_CACHE).then((cache) => cache.put('/index.html', copy)).catch(() => {})
          return response
        })
        .catch(() => caches.match('/index.html')),
    )
    return
  }

  // Hashed build assets: cache-first (immutable by filename).
  if (url.pathname.startsWith('/assets/') || url.pathname.startsWith('/icons/') || SHELL_URLS.includes(url.pathname)) {
    event.respondWith(
      caches.match(request).then(
        (cached) =>
          cached ??
          fetch(request).then((response) => {
            const copy = response.clone()
            caches.open(ASSET_CACHE).then((cache) => cache.put(request, copy)).catch(() => {})
            return response
          }),
      ),
    )
  }
})
