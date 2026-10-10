import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { Miniflare, convertV4MiniflareOptions } from "miniflare";
import worker from "../access/worker.mjs";
import { seal, SESSION_COOKIE } from "../access/tokens.mjs";
import { WEB_MATCH_VERSION } from "./store.mjs";

const GAME = "https://blocknations.moneymattersmedia.com";
const PREVIEW = "https://blocknations-web-preview.johannes-gaebler.workers.dev";
let mf, env;
const identities = new Map();
before(async () => {
  mf = new Miniflare(convertV4MiniflareOptions({
    modules: await Promise.all(["service.mjs", "store.mjs", "push.mjs"].map(async name => ({
      type: "ESModule", path: new URL("./" + name, import.meta.url).pathname,
      contents: await readFile(new URL("./" + name, import.meta.url), "utf8"),
    }))), compatibilityDate: "2026-10-08",
    durableObjects: { MULTIPLAYER_STORE: { className: "MultiplayerStore", useSQLite: true } },
  }));
  env = {
    MULTIPLAYER_STORE: await mf.getDurableObjectNamespace("MULTIPLAYER_STORE"),
    SESSION_SECRET: "synthetic-only-secret-" + crypto.randomUUID() + crypto.randomUUID(),
    GAME_ORIGIN: GAME, GAME_ORIGINS: JSON.stringify([GAME, PREVIEW]),
    REVIEW: { fetch: () => Response.json({}) }, ASSETS: { fetch: () => new Response("synthetic game bytes") },
    ACCESS_STORE: {
      idFromName: name => name,
      get: () => ({ fetch: async request => {
        const data = await request.json(), path = new URL(request.url).pathname;
        if (path === "/resolve") return Response.json(identities.get(data.id) ?? null);
        assert.equal(path, "/player-search");
        const query = data.query.toLowerCase();
        return Response.json([...identities.values()].filter(user => user.status === "approved" &&
          (query.includes("@") ? user.email.toLowerCase() === query : user.display_name.toLowerCase().includes(query))).slice(0, 8));
      } }),
    },
  };
});
after(async () => { await mf?.dispose(); });

async function person(name, status = "approved") {
  const sid = crypto.randomUUID(), uid = "synthetic-" + sid;
  const user = { auth_user_id: uid, display_name: name, email: sid + "@example.test", status };
  identities.set(sid, user);
  const session = await seal(env.SESSION_SECRET, { kind: "session", aud: GAME, uid, sid, exp: Date.now() + 86400000 });
  return { user, sid, cookie: SESSION_COOKIE + "=" + session };
}
async function call(actor, path, body, options = {}) {
  const environment = options.environment ?? env;
  const headers = new Headers({ Cookie: actor?.cookie ?? "" });
  if (body !== undefined) {
    headers.set("Content-Type", "application/json");
    headers.set("Origin", options.origin ?? environment.GAME_ORIGIN);
    headers.set("X-BlockNations-Web", "1");
  }
  const response = await worker.fetch(new Request(environment.GAME_ORIGIN + "/api/multiplayer/" + path, {
    method: body !== undefined ? "POST" : "GET", headers, body: body !== undefined ? JSON.stringify(body) : undefined,
  }), environment);
  return { status: response.status, headers: response.headers, ...(await response.json()) };
}
async function find(actor) { return call(actor, "find", { version: WEB_MATCH_VERSION }); }
function opening(matchId) {
  return { version: "3", protocolVersion: 5, appVersion: "1.0.4", gameId: matchId, mode: "PlayByPost",
    aiRecruitVariant: "Default", mapSizePreset: "Standard7", boardWidth: 7, boardHeight: 7,
    seatCount: 2, currentTurnSeatIndex: 0, isPlayerTurn: true, transportSeq: 2, turnNumber: 1,
    seatGold: [3, 2], playerGold: 3, aiGold: 2, visibilityRadius: 2, gameOver: false, hasWinnerSeatIndex: false,
    seats: [0,1].map(seatIndex => ({ seatIndex, state: "Unclaimed", claimedPlayerId: "forged-id", typedDisplayName: "forged-name" })),
    cities: [{ x: 1, y: 1, ownerSeatIndex: 0, isPlayerOwned: true, hasRecruitedThisTurn: false },
      { x: 5, y: 5, ownerSeatIndex: 1, isPlayerOwned: false, hasRecruitedThisTurn: false }],
    units: [], tiles: Array.from({ length: 49 }, (_, n) => ({ x: n % 7, y: Math.floor(n / 7), seenSeatIndices: [], playerSeen: false, opponentSeen: false })) };
}
async function startPair() {
  const a = await person("Alice Test"), b = await person("Bob Test");
  const first = await find(a), second = await find(b);
  assert.equal(first.match.id, second.match.id);
  const id = first.match.id;
  const initial = await call(a, "matches/" + id + "/initialize", { json: JSON.stringify(opening(id)) });
  assert.equal(initial.status, 200);
  return { a, b, id, initial };
}
function next(snapshot) {
  const state = structuredClone(snapshot);
  state.transportSeq++;
  state.currentTurnSeatIndex = state.transportSeq % 2;
  state.turnNumber = Math.floor(state.transportSeq / 2);
  state.isPlayerTurn = state.currentTurnSeatIndex === 0;
  return state;
}
async function submit(actor, id, state, baseSeq) {
  return call(actor, "matches/" + id + "/turn", { baseSeq, json: JSON.stringify(state) });
}

test("multiplayer requires a currently approved authenticated account", async () => {
  assert.equal((await call(null, "lobby")).status, 403);
  const pending = await person("Pending", "pending");
  assert.equal((await find(pending)).status, 403);
  const player = await person("Approved");
  assert.equal((await call(player, "session")).status, 200);
  identities.get(player.sid).status = "disabled";
  assert.equal((await call(player, "lobby")).status, 403);
});
test("Google names preserve spaces and Unicode; private identity and email stay on the server", async () => {
  const actor = await person("Zoë 王 Test");
  const result = await call(actor, "session");
  assert.equal(result.player.name, "Zoë 王 Test");
  assert.match(result.player.id, /^[a-f0-9]{32}$/);
  assert.doesNotMatch(JSON.stringify(result), /@example|auth_user_id|synthetic-/);
  assert.equal(result.headers.get("Cache-Control"), "private, no-store");
});
test("cross-origin mutations and forged browser identity are rejected or ignored", async () => {
  const actor = await person("Origin Test");
  assert.equal((await call(actor, "find", { version: WEB_MATCH_VERSION }, { origin: "https://evil.example" })).status, 403);
  const result = await call(actor, "find", { version: WEB_MATCH_VERSION, actor: { id: "a".repeat(32) } });
  const identity = await call(actor, "session");
  assert.equal(result.match.players[0].id, identity.player.id);
  await call(actor, "matches/" + result.match.id + "/cancel", {});
});
test("find creates one open match, retries reuse it, and the next player joins it", async () => {
  const a = await person("Queue One"), b = await person("Queue Two");
  const first = await find(a), repeated = await find(a), second = await find(b);
  assert.equal(repeated.match.id, first.match.id);
  assert.equal(second.match.id, first.match.id);
  assert.equal(second.match.seatIndex, 1);
  assert.equal(second.match.status, "active");
  assert.equal(second.match.players.length, 2);
  assert.equal(second.match.boardSize, 7);
  assert.equal((await call(a, "matches/" + first.match.id + "/cancel", {})).status, 409);
});
test("simultaneous finds reserve each seat once and do not overfill a match", async () => {
  const players = await Promise.all(Array.from({ length: 6 }, (_, i) => person("Concurrent " + i)));
  const results = await Promise.all(players.map(find));
  assert.ok(results.every(result => result.status === 200));
  const counts = new Map();
  for (const result of results) counts.set(result.match.id, (counts.get(result.match.id) ?? 0) + 1);
  assert.deepEqual([...counts.values()].sort(), [2,2,2]);
});
test("either reserved member can initialize; concurrent initialization keeps one opening", async () => {
  const a = await person("Initial Host"), b = await person("Initial Guest");
  const match = await find(a); await find(b);
  const json = JSON.stringify(opening(match.match.id));
  const [one, two] = await Promise.all([call(a, "matches/" + match.match.id + "/initialize", { json }), call(b, "matches/" + match.match.id + "/initialize", { json })]);
  assert.equal(one.status, 200); assert.equal(two.status, 200);
  assert.equal(one.json, two.json);
  assert.equal(one.match.revision, two.match.revision);
  const state = JSON.parse(one.json);
  assert.ok(state.seats.every(seat => seat.state === "Active" && seat.claimedPlayerId !== "forged-id"));
  assert.equal(state.playerOneTypedDisplayName, "Initial Host");
  assert.equal(state.playerTwoTypedDisplayName, "Initial Guest");
});
test("outsiders cannot view, initialize, submit or cancel a private match", async () => {
  const { id, initial } = await startPair(), outsider = await person("Outsider");
  for (const [path, body] of [["", undefined], ["/next?afterSeq=-1", undefined], ["/initialize", { json: initial.json }], ["/turn", { json: initial.json, baseSeq: 2 }], ["/cancel", {}]]) {
    assert.equal((await call(outsider, "matches/" + id + path, body)).status, 404);
  }
});
test("turn ownership uses account seats; accepted turns survive reconnect and contain canonical names", async () => {
  const { a, b, id, initial } = await startPair();
  const state = next(JSON.parse(initial.json));
  assert.equal((await submit(b, id, state, 2)).status, 403);
  state.seats[1].claimedPlayerId = "forged-owner"; state.seats[1].typedDisplayName = "forged-name";
  const saved = await submit(a, id, state, 2);
  assert.equal(saved.status, 200); assert.equal(saved.seq, 3);
  assert.equal(JSON.parse(saved.json).seats[1].typedDisplayName, "Bob Test");
  const reconnect = await call(b, "matches/" + id);
  assert.equal(reconnect.json, saved.json);
  assert.equal((await call(b, "matches/" + id + "/next?afterSeq=2")).seq, 3);
  assert.equal((await call(b, "matches/" + id + "/next?afterSeq=3")).error, "NO_TURN");
  const reply = await submit(b, id, next(JSON.parse(reconnect.json)), 3);
  assert.equal(reply.seq, 4); assert.equal(reply.match.currentTurnSeatIndex, 0);
});
test("lost response retries are idempotent; stale tabs cannot replace committed turns", async () => {
  const { a, id, initial } = await startPair();
  const state = next(JSON.parse(initial.json));
  const first = await submit(a, id, state, 2), retry = await submit(a, id, state, 2);
  assert.equal(first.json, retry.json); assert.equal(first.match.revision, retry.match.revision);
  state.seatGold[0]++;
  const stale = await submit(a, id, state, 2);
  assert.equal(stale.status, 409); assert.equal(stale.error, "CONFLICT");
  assert.equal((await call(a, "matches/" + id)).json, first.json);
});
test("incompatible or malformed snapshots and sequence jumps fail without modifying the match", async () => {
  const { a, id, initial } = await startPair();
  for (const change of [state => state.seatCount=4, state => state.boardWidth=11, state => state.protocolVersion=4,
    state => state.gameId="0".repeat(32), state => state.currentTurnSeatIndex=8, state => state.tiles.pop(),
    state => state.seatGold[0]=-1, state => state.appVersion="older", state => { state.transportSeq=5; state.turnNumber=2; }]) {
    const state = next(JSON.parse(initial.json)); change(state);
    assert.ok((await submit(a, id, state, 2)).status >= 400);
  }
  assert.equal((await call(a, "matches/" + id)).seq, 2);
});
test("winning snapshots may skip an eliminated seat; finished matches remain readable and cannot advance", async () => {
  const { a, b, id, initial } = await startPair();
  const state = JSON.parse(initial.json);
  state.gameOver=true; state.hasWinnerSeatIndex=true; state.winnerSeatIndex=0;
  state.seats[1].state="Eliminated"; state.transportSeq=4; state.turnNumber=2;
  const result = await submit(a, id, state, 2);
  assert.equal(result.status, 200); assert.equal(result.match.status, "finished");
  assert.equal((await call(b, "matches/" + id)).match.winnerSeatIndex, 0);
  assert.equal((await submit(a, id, next(state), 4)).status, 409);
});
test("a waiting match can be cancelled and another find creates a fresh match", async () => {
  const actor = await person("Cancel Test"), first = await find(actor);
  assert.equal((await call(actor, "matches/" + first.match.id + "/cancel", {})).status, 200);
  assert.equal((await call(actor, "matches/" + first.match.id)).error, "MATCH_CANCELLED");
  const second = await find(actor); assert.notEqual(first.match.id, second.match.id);
  await call(actor, "matches/" + second.match.id + "/cancel", {});
});
test("player search matches literal names or exact emails, hides addresses and excludes unapproved users", async () => {
  const actor = await person("Searcher"), target = await person("Directory 王 Alice"), disabled = await person("Directory Hidden", "disabled");
  const names = await call(actor, "search?q=" + encodeURIComponent("Directory"));
  assert.equal(names.players.length, 1); assert.equal(names.players[0].name, target.user.display_name);
  assert.doesNotMatch(JSON.stringify(names), /@example|auth_user_id/);
  assert.equal((await call(actor, "search?q=" + encodeURIComponent(target.user.email))).players.length, 1);
  assert.equal((await call(actor, "search?q=" + encodeURIComponent("@example.test"))).players.length, 0);
  assert.equal((await call(actor, "search?q=Di")).status, 400);
  assert.ok(disabled);
});
test("friend requests require recipient acceptance and remain symmetric across reloads", async () => {
  const a = await person("Friend Sender"), b = await person("Friend Receiver");
  const target = (await call(b, "session")).player;
  const sent = await call(a, "friend-request", { playerId: target.id });
  assert.equal((await call(a, "friend-request", { playerId: target.id })).friendshipId, sent.friendshipId);
  assert.equal((await call(a, "friend-answer", { id: sent.friendshipId, action: "accept" })).status, 403);
  const inbox = await call(b, "lobby"); assert.equal(inbox.friendships[0].incoming, true);
  assert.equal((await call(b, "friend-answer", { id: sent.friendshipId, action: "accept" })).status, 200);
  assert.equal((await call(a, "lobby")).friendships[0].status, "accepted");
  assert.equal((await call(b, "lobby")).friendships[0].status, "accepted");
  await call(b, "friend-answer", { id: sent.friendshipId, action: "remove" });
  assert.equal((await call(a, "lobby")).friendships.length, 0);
});
test("a match opponent can be befriended or invited without exposing their email", async () => {
  const { a, b, id } = await startPair();
  const match = (await call(a, "matches/" + id)).match;
  const opponent = match.players[1];
  assert.equal((await call(a, "friend-request", { playerId: opponent.id })).status, 200);
  const invite = await call(a, "invite", { playerId: opponent.id });
  assert.equal((await call(b, "lobby")).invites[0].id, invite.inviteId);
});
test("accepted invitations create a private two-seat match once, with sender and recipient identities", async () => {
  const a = await person("Invite Sender"), b = await person("Invite Recipient"), outsider = await person("Invite Outsider");
  const target = (await call(b, "session")).player;
  const sent = await call(a, "invite", { playerId: target.id });
  assert.equal((await call(a, "invite", { playerId: target.id })).inviteId, sent.inviteId);
  assert.equal((await call(a, "invite-answer", { id: sent.inviteId, action: "accept" })).status, 403);
  assert.equal((await call(outsider, "invite-answer", { id: sent.inviteId, action: "accept" })).status, 404);
  const [accepted, retry] = await Promise.all([call(b, "invite-answer", { id: sent.inviteId, action: "accept" }), call(b, "invite-answer", { id: sent.inviteId, action: "accept" })]);
  assert.equal(accepted.match.id, retry.match.id); assert.equal(accepted.match.kind, "invite");
  assert.equal(accepted.match.players[0].name, "Invite Sender"); assert.equal(accepted.match.seatIndex, 1);
  assert.equal((await call(a, "lobby")).matches[0].id, accepted.match.id);
});
test("preview and production matches and social requests are isolated", async () => {
  const actor = await person("Environment Test");
  const environment = { ...env, GAME_ORIGIN: PREVIEW };
  const session = await seal(env.SESSION_SECRET, { kind: "session", aud: PREVIEW, uid: actor.user.auth_user_id, sid: actor.sid, exp: Date.now() + 86400000 });
  const previewActor = { ...actor, cookie: SESSION_COOKIE + "=" + session };
  const before = await call(previewActor, "lobby", undefined, { environment });
  assert.equal(before.matches.length, 0); assert.equal(before.friendships.length, 0);
  const game = await find(actor);
  assert.equal((await call(previewActor, "matches/" + game.match.id, undefined, { environment })).status, 404);
  await call(actor, "matches/" + game.match.id + "/cancel", {});
});
test("lobby HTML renders accessible controls and escapes the Google display name", async () => {
  const actor = await person('<script>alert("x")</script>');
  const response = await worker.fetch(new Request(GAME + "/multiplayer", { headers: { Cookie: actor.cookie } }), env);
  const html = await response.text();
  assert.equal(response.status, 200);
  assert.match(html, /Find online match/); assert.match(html, /Name or email address/);
  assert.match(html, /&lt;script&gt;/); assert.doesNotMatch(html, /<script>alert/);
  assert.match(response.headers.get("Content-Security-Policy"), /script-src 'nonce-/);
  assert.match(html, /type="module" src="\/PWA.js"/);
});
test("the install and push shell is public but game assets and multiplayer data remain gated", async () => {
  for (const path of ["/sw.js", "/PWA.js", "/manifest.webmanifest", "/icons/app-192.png", "/icons/app-512.png"]) {
    const response = await worker.fetch(new Request(GAME + path), env);
    assert.equal(response.status, 200, path);
    if (path === "/sw.js") assert.equal(response.headers.get("Service-Worker-Allowed"), "/");
  }
  for (const path of ["/Build/game.wasm.unityweb", "/release.json", "/api/multiplayer/lobby", "/multiplayer"]) {
    const response = await worker.fetch(new Request(GAME + path), env);
    assert.equal(response.status, 403, path);
    assert.equal(response.headers.get("Cache-Control"), "private, no-store");
  }
});
test("mutations require both the origin and explicit browser header and accept only JSON objects", async () => {
  const actor = await person("Mutation Guard");
  for (const [headers, body, status] of [
    [{ Origin: GAME, "Content-Type": "application/json" }, "{}", 403],
    [{ Origin: GAME, "X-BlockNations-Web": "1", "Content-Type": "text/plain" }, "{}", 400],
    [{ Origin: GAME, "X-BlockNations-Web": "1", "Content-Type": "application/json" }, "[]", 400],
  ]) {
    const response = await worker.fetch(new Request(GAME + "/api/multiplayer/find", {
      method: "POST", headers: { Cookie: actor.cookie, ...headers }, body,
    }), env);
    assert.equal(response.status, status);
  }
});
test("the host can save the first turn before someone joins the waiting match", async () => {
  const host = await person("Asynchronous Host"), guest = await person("Later Guest");
  const found = await find(host), id = found.match.id;
  const initial = await call(host, "matches/" + id + "/initialize", { json: JSON.stringify(opening(id)) });
  assert.equal(initial.match.status, "waiting");
  const saved = await submit(host, id, next(JSON.parse(initial.json)), 2);
  assert.equal(saved.status, 200);
  assert.equal(saved.seq, 3);
  assert.equal(saved.match.status, "waiting");
  const joined = await find(guest);
  assert.equal(joined.match.id, id);
  assert.equal(joined.match.currentTurnSeatIndex, 1);
  const restored = await call(guest, "matches/" + id);
  assert.equal(restored.seq, 3);
  assert.equal(JSON.parse(restored.json).seats[1].state, "Active");
  assert.equal(JSON.parse(restored.json).seats[1].typedDisplayName, "Later Guest");
});
