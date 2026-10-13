import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";

const source = await readFile(new URL("./sw.js", import.meta.url), "utf8");
const origin = "https://game.test";
const path = "/Build/0123456789abcdef0123456789abcdef.wasm.br";

function harness() {
  const listeners = new Map(), stored = new Map(), calls = [];
  const state = { status: 200, etag: '"version-1"', offline: false, storageFailure: false };
  const cache = {
    match: async request => stored.get(request.url)?.clone(),
    put: async (request, response) => { stored.set(request.url, response); },
    delete: async request => stored.delete(request.url),
    keys: async () => [...stored.keys()].map(url => new Request(url)),
  };
  runInNewContext(source, {
    self: { location: { origin }, addEventListener: (name, listener) => listeners.set(name, listener) },
    URL, Request, Response, Headers,
    caches: { open: async () => { if (state.storageFailure) throw new Error("Storage unavailable"); return cache; } },
    fetch: async request => {
      calls.push({ url: request.url, method: request.method, cache: request.cache });
      if (state.offline) throw new Error("Offline");
      return new Response(request.method === "HEAD" ? null : state.status === 200 ? state.etag : "denied", {
        status: state.status, headers: { ETag: state.etag, "Content-Type": "application/wasm",
          "Cache-Control": state.status === 200 ? "private, no-cache, must-revalidate" : "private, no-store" },
      });
    },
  });
  async function load(url = origin + path, method = "GET", headers = {}) {
    const pending = [];
    let response;
    listeners.get("fetch")({ request: new Request(url, { method, headers }),
      respondWith: promise => { response = promise; }, waitUntil: promise => pending.push(promise) });
    if (!response) return null;
    const result = await response;
    await Promise.all(pending);
    return result;
  }
  return { load, state, stored, calls };
}

test("large game files are reused only after a fresh HEAD authorization check", async () => {
  const { load, calls } = harness();
  assert.equal(await (await load()).text(), '"version-1"');
  assert.equal(await (await load()).text(), '"version-1"');
  assert.deepEqual(calls.map(call => call.method), ["GET", "HEAD"]);
  assert.ok(calls.every(call => call.cache === "no-store"));
});

test("offline and revoked sessions never receive a previously cached game body", async () => {
  const { load, state, stored, calls } = harness();
  await load();
  state.offline = true;
  await assert.rejects(load(), /Offline/);
  assert.equal(calls.at(-1).method, "HEAD");
  state.offline = false;
  state.status = 403;
  const denied = await load();
  assert.equal(denied.status, 403);
  assert.equal(await denied.text(), "denied");
  assert.equal(stored.size, 0);
});

test("changed asset ETags replace retained bytes instead of serving a stale build", async () => {
  const { load, state, calls } = harness();
  await load();
  state.etag = '"version-2"';
  assert.equal(await (await load()).text(), '"version-2"');
  assert.deepEqual(calls.map(call => call.method), ["GET", "HEAD", "GET"]);
});

test("HTML, APIs, saves, foreign origins and unversioned files are not intercepted", async () => {
  const { load, calls } = harness();
  for (const url of [origin + "/", origin + "/api/multiplayer/session", origin + "/release.json",
    origin + "/Build/game.wasm.gz", origin + "/StreamingAssets/save.json", "https://other.test" + path]) {
    assert.equal(await load(url), null);
  }
  assert.equal(await load(origin + path, "POST"), null);
  assert.equal(await load(origin + path, "GET", { Range: "bytes=0-20" }), null);
  assert.equal(calls.length, 0);
});

test("unavailable browser storage falls back to an ordinary authorized download", async () => {
  const { load, state } = harness();
  state.storageFailure = true;
  assert.equal(await (await load()).text(), '"version-1"');
});

test("the game cache stays bounded across successive releases", async () => {
  const { load, stored } = harness();
  for (let i = 0; i < 10; i++) await load(origin + "/Build/" + i.toString(16).padStart(32, "0") + ".data.gz");
  assert.equal(stored.size, 8);
  assert.ok(!stored.has(origin + "/Build/" + "0".repeat(32) + ".data.gz"));
});
