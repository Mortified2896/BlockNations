// Only content-addressed game files can be retained. Every cache hit needs a
// fresh online access check; HTML, APIs, sessions and saves are never cached.
const GAME_CACHE = "blocknations-static-v1";
const MAX_GAME_FILES = 8; // Enough for two complete four-file releases.

async function gameFile(request, event) {
  let cache, cached;
  try {
    cache = await caches.open(GAME_CACHE);
    cached = await cache.match(request);
  } catch { /* Storage can be unavailable; normal authorized downloads work. */ }
  if (cached) {
    const headers = new Headers(request.headers);
    for (const name of ["If-None-Match", "If-Modified-Since", "Range"]) headers.delete(name);
    // This bypasses both browser HTTP cache and the service worker. No offline
    // fallback: a failed access check must never return retained game bytes.
    const check = await fetch(new Request(request, { method: "HEAD", headers, cache: "no-store", redirect: "error" }));
    if (check.status === 200 && check.headers.get("ETag") === cached.headers.get("ETag")) return cached;
    try { await cache.delete(request); } catch { }
  }
  const response = await fetch(new Request(request, { cache: "no-store" }));
  if (cache && response.status === 200 && response.headers.has("ETag") &&
      response.headers.get("Cache-Control") === "private, no-cache, must-revalidate") {
    const copy = response.clone();
    event.waitUntil((async () => {
      try {
        await cache.put(request, copy);
        const keys = await cache.keys();
        for (const key of keys.slice(0, Math.max(0, keys.length - MAX_GAME_FILES))) await cache.delete(key);
      } catch { /* Quota errors must not prevent the game loading. */ }
    })());
  }
  return response;
}

self.addEventListener("fetch", event => {
  const url = new URL(event.request.url);
  if (event.request.method !== "GET" || event.request.headers.has("Range") || url.origin !== self.location.origin ||
      !/^\/Build\/[a-f0-9]{32}\.(?:loader\.js|(?:data|wasm|framework\.js)\.(?:br|gz|unityweb))$/.test(url.pathname)) return;
  event.respondWith(gameFile(event.request, event));
});

self.addEventListener("install", event => event.waitUntil(self.skipWaiting()));
self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));
self.addEventListener("push", event => {
  let payload = {};
  try { payload = event.data?.json() ?? {}; } catch { }
  let url = new URL("/multiplayer", self.location.origin);
  try {
    const candidate = new URL(payload.url, self.location.origin);
    if (candidate.origin === self.location.origin && ["/", "/multiplayer"].includes(candidate.pathname)) url = candidate;
  } catch { }
  event.waitUntil(self.registration.showNotification("Block Nations", {
    body: String(payload.body ?? "You have an update in Block Nations.").slice(0, 200),
    icon: "/icons/app-192.png", badge: "/icons/app-192.png", tag: String(payload.tag ?? "blocknations").slice(0, 120),
    data: { url: url.href },
  }));
});
self.addEventListener("notificationclick", event => {
  event.notification.close();
  event.waitUntil((async () => {
    const target = new URL(event.notification.data?.url ?? "/multiplayer", self.location.origin);
    if (target.origin !== self.location.origin) return;
    const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
    const existing = windows.find(client => new URL(client.url).origin === self.location.origin);
    if (existing) { await existing.navigate(target.href); await existing.focus(); }
    else await self.clients.openWindow(target.href);
  })());
});
