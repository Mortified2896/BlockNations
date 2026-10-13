import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";
const source = (await readFile(new URL("./client.mjs", import.meta.url), "utf8")).replace("export function", "function");
function host() {
  const events = new Map();
  return { location: { origin: "https://game.test" },
    addEventListener(name, fn) { const list = events.get(name) ?? []; list.push(fn); events.set(name, list); },
    removeEventListener(name, fn) { events.set(name, (events.get(name) ?? []).filter(item => item !== fn)); },
    dispatchEvent(event) { for (const fn of events.get(event.type) ?? []) fn(event); },
  };
}
function load(window, parent = window, button = null) {
  const clicks = new Map();
  if (button) button.addEventListener = (name, fn) => clicks.set(name, fn);
  runInNewContext(source, { window, parent, location: window.location, navigator: {}, Event,
    document: { getElementById: id => id === "install-app" ? button : null } });
  return clicks;
}
test("an embedded lobby uses the top-level install prompt and releases listeners when closed", async () => {
  const top = host(); load(top);
  let prompts = 0, prevented = false;
  top.dispatchEvent({ type: "beforeinstallprompt", preventDefault() { prevented = true; }, async prompt() { prompts++; } });
  const child = host(), button = { hidden: true };
  const clicks = load(child, top, button);
  assert.equal(prevented, true);
  assert.equal(button.hidden, false);
  await clicks.get("click")();
  assert.equal(prompts, 1);
  assert.equal(button.hidden, true);
  await clicks.get("click")();
  assert.equal(prompts, 1);
  child.dispatchEvent(new Event("pagehide"));
  top.dispatchEvent({ type: "beforeinstallprompt", preventDefault() {}, async prompt() {} });
  assert.equal(button.hidden, true);
});


test("an embedded lobby can enable notifications through the top-level app", async () => {
  const top = host(), child = host(), storage = new Map(), clicks = new Map(), elements = new Map();
  let prompts = 0, subscriptions = 0;
  top.PushManager = class {};
  top.Notification = { permission: "default", async requestPermission() { prompts++; return "granted"; } };
  for (const id of ["enable-push", "disable-push", "test-push", "push-status"])
    elements.set(id, { disabled: true, hidden: false, addEventListener: (name, fn) => clicks.set(id + ":" + name, fn) });
  const worker = { pushManager: { async getSubscription() { return null; }, async subscribe() {
    subscriptions++; return { toJSON: () => ({ endpoint: "https://push.test/synthetic" }) };
  } } };
  runInNewContext(source, { window: child, parent: top, location: child.location, Event, Uint8Array, atob,
    navigator: { serviceWorker: { register: async () => worker, ready: Promise.resolve(worker) } },
    document: { getElementById: id => elements.get(id) },
    localStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value) },
    fetch: async path => ({ ok: true, json: async () => ({ ok: true, enabled: true, publicKey: "AA", player: { id: "synthetic" } }) }),
  });
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(elements.get("enable-push").disabled, false);
  assert.equal(prompts, 0);
  await clicks.get("enable-push:click")();
  assert.equal(prompts, 1);
  assert.equal(subscriptions, 1);
  assert.equal(storage.get("blocknations-notification-player"), "synthetic");
});
