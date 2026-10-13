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
