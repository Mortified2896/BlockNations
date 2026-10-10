// Push only. Never cache or intercept protected game assets, Google sessions,
// match snapshots or admission responses.
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
