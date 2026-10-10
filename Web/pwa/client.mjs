// Both the nonce-protected lobby and the Unity template load this public shell
// module. Keep it independent of the Worker bundle's generated helper names.
export function startWebApp() {
  const get = id => document.getElementById(id);
  let installPrompt;
  window.addEventListener("beforeinstallprompt", event => {
    event.preventDefault(); installPrompt = event;
    if (get("install-app")) get("install-app").hidden = false;
  });
  get("install-app")?.addEventListener("click", async () => {
    if (!installPrompt) return;
    await installPrompt.prompt(); installPrompt = null; get("install-app").hidden = true;
  });
  if (!("serviceWorker" in navigator)) return;
  const registration = navigator.serviceWorker.register("/sw.js", { scope: "/", updateViaCache: "none" });
  const enable = get("enable-push"), disable = get("disable-push"), test = get("test-push"), status = get("push-status");
  if (!enable || !status) { registration.catch(() => {}); return; }
  async function api(path, data) {
    const response = await fetch("/api/multiplayer/" + path, { method: data ? "POST" : "GET", credentials: "same-origin", cache: "no-store",
      headers: data ? { "Content-Type": "application/json", "X-BlockNations-Web": "1" } : {}, body: data ? JSON.stringify(data) : undefined });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error("Please sign in again, or try notification setup later.");
    return result;
  }
  const supported = "PushManager" in window && "Notification" in window;
  if (!supported) {
    status.textContent = "On iPhone, add this site to your Home Screen, open it from its icon, then return here to enable notifications. This browser may not support web push.";
    registration.catch(() => {}); return;
  }
  let ready, config, subscription, player;
  const mark = "blocknations-notification-player";
  function controls(active) {
    enable.hidden = active; disable.hidden = !active; test.hidden = !active;
    enable.disabled = false;
  }
  function key(value) {
    return Uint8Array.from(atob(value.replaceAll("-", "+").replaceAll("_", "/")), char => char.charCodeAt(0));
  }
  Promise.all([registration.then(() => navigator.serviceWorker.ready), api("push-config"), api("session")]).then(async values => {
    [ready, config] = values; player = values[2].player;
    if (!config.enabled) { status.textContent = "Notification delivery is temporarily unavailable. You can still check your turns in the lobby."; return; }
    subscription = await ready.pushManager.getSubscription();
    const active = subscription && Notification.permission === "granted" && localStorage.getItem(mark) === player.id;
    if (active) await api("push-subscribe", { subscription: subscription.toJSON() });
    controls(Boolean(active));
    status.textContent = active ? "Notifications are enabled on this device for your Google account." :
      Notification.permission === "denied" ? "Notifications are blocked. Allow them in this browser or device's site settings, then reload." :
      "Enable notifications for your turn, friend requests, and match invitations. Delivery depends on your browser and phone settings.";
  }).catch(() => { status.textContent = "Notification setup could not load. Please try again after reloading."; });
  enable.addEventListener("click", async () => {
    if (!ready || !config?.enabled) return;
    // Permission starts directly in the click handler; never on page load.
    const permission = Notification.requestPermission();
    enable.disabled = true;
    try {
      if (await permission !== "granted") { status.textContent = "Notifications weren't allowed. You can enable them later in device settings."; return; }
      subscription = subscription ?? await ready.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key(config.publicKey) });
      await api("push-subscribe", { subscription: subscription.toJSON() });
      localStorage.setItem(mark, player.id); controls(true);
      status.textContent = "Notifications are enabled. Send a test to check delivery on this device.";
    } catch { status.textContent = "Notifications could not be enabled. On iPhone, use the installed Home Screen app and try again."; }
    finally { enable.disabled = false; }
  });
  disable.addEventListener("click", async () => {
    disable.disabled = true;
    try {
      if (subscription) { await api("push-unsubscribe", { endpoint: subscription.endpoint }); await subscription.unsubscribe(); }
      subscription = null; localStorage.removeItem(mark); controls(false); status.textContent = "Notifications are off on this device.";
    } catch { status.textContent = "Notifications could not be turned off. Please try again."; }
    finally { disable.disabled = false; }
  });
  test.addEventListener("click", async () => {
    test.disabled = true;
    try { await api("push-test", {}); status.textContent = "A test notification is queued. It may take a few moments to arrive."; }
    catch { status.textContent = "The test could not be sent. Please enable notifications again."; }
    finally { test.disabled = false; }
  });
}
if (typeof document !== "undefined") startWebApp();
