import { test } from "node:test";
import assert from "node:assert/strict";
import { installGameNavigation } from "./navigation.mjs";

function fixture() {
  const listeners = new Map(), calls = [], frames = [], navigations = [];
  const origin = "https://game.test";
  const host = {
    location: { origin, href: origin + "/", assign: path => navigations.push(path) },
    history: { pushState() {}, replaceState() {} },
    blockNationsScene: "MainMenu", blockNationsMenuReceiver: "RegisteredMenu",
    addEventListener: (name, handler) => listeners.set(name, handler),
    fetch: async () => Response.json({ ok: true }),
    document: { body: { append: frame => frames.push(frame) }, createElement: () => ({
      style: {}, contentWindow: {}, setAttribute() {}, focus() {}, remove() { this.removed = true; },
    }) },
  };
  const elements = { game: {}, canvas: { focus() {} }, accountBar: {}, statusPanel: { hidden: true }, statusMessage: {},
    statusLink: { href: origin + "/multiplayer", textContent: "Return to multiplayer lobby",
      addEventListener: (name, handler) => listeners.set("link:" + name, handler) } };
  const navigation = installGameNavigation({ SendMessage: (...args) => calls.push(args) }, elements, host);
  return { navigation, host, elements, frames, calls, navigations, clickStatus: () => listeners.get("link:click")({ preventDefault() {} }), message: event => listeners.get("message")(event) };
}

test("only the active same-origin lobby can open an authenticated match through the registered menu", async () => {
  const f = fixture();
  await f.navigation.openLobby();
  const frame = f.frames[0], matchId = "0123456789abcdef0123456789abcdef";
  for (const event of [
    { origin: "https://other.test", source: frame.contentWindow, data: { type: "bn-open-match", matchId } },
    { origin: f.host.location.origin, source: {}, data: { type: "bn-open-match", matchId } },
    { origin: f.host.location.origin, source: frame.contentWindow, data: { type: "bn-open-match", matchId: "../private" } },
  ]) f.message(event);
  assert.equal(f.calls.length, 0);
  f.message({ origin: f.host.location.origin, source: frame.contentWindow, data: { type: "bn-open-match", matchId } });
  assert.deepEqual(f.calls, [["RegisteredMenu", "OpenWebMatchFromBrowser", matchId]]);
  assert.equal(f.navigations.length, 0);
  assert.equal(frame.removed, true);
  assert.equal(f.elements.game.inert, false);
});

test("returning from the lobby reuses the game document and restores input", async () => {
  const f = fixture();
  await f.navigation.openLobby();
  assert.equal(f.elements.game.inert, true);
  const frame = f.frames[0];
  f.message({ origin: f.host.location.origin, source: frame.contentWindow, data: { type: "bn-lobby-close" } });
  assert.equal(f.elements.game.inert, false);
  assert.equal(f.elements.accountBar.inert, false);
  assert.equal(f.navigations.length, 0);
});

test("a gameplay-to-lobby transition uses the existing exit action before showing the lobby", async () => {
  const f = fixture();
  f.host.blockNationsExitReceiver = "RegisteredExit";
  f.navigation.sceneChanged("SampleScene");
  await f.navigation.openLobby();
  assert.deepEqual(f.calls, [["RegisteredExit", "QuitToMainMenu"]]);
  assert.equal(f.frames.length, 0);
  f.navigation.sceneChanged("MainMenu");
  f.navigation.registerMenu("NewMenu");
  await f.navigation.openLobby();
  assert.equal(f.frames.length, 1);
});

test("expired admission uses top-level reauthentication without embedding a login page", async () => {
  const f = fixture();
  f.host.fetch = async () => new Response(null, { status: 403 });
  await f.navigation.openLobby();
  assert.deepEqual(f.navigations, ["/multiplayer"]);
  assert.equal(f.frames.length, 0);
});


test("a temporary lobby failure lets the player return to the game and retry", async () => {
  const f = fixture();
  f.host.fetch = async () => new Response(null, { status: 503 });
  await f.navigation.openLobby();
  assert.equal(f.elements.statusPanel.hidden, false);
  assert.equal(f.elements.statusLink.textContent, "Back to game");
  f.clickStatus();
  assert.equal(f.elements.statusPanel.hidden, true);
  assert.equal(f.elements.statusLink.href, f.host.location.origin + "/multiplayer");
  f.host.fetch = async () => Response.json({ ok: true });
  await f.navigation.openLobby();
  assert.equal(f.frames.length, 1);
  assert.equal(f.navigations.length, 0);
});
