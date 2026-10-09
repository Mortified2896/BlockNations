import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { Miniflare, convertV4MiniflareOptions } from "miniflare";
import worker from "./worker.mjs";
import { open, seal, fingerprint, SESSION_COOKIE, ATTEMPT_COOKIE } from "./tokens.mjs";
import { storeCall } from "./store.mjs";

const GAME = "https://blocknations.moneymattersmedia.com";
const PREVIEW = "https://blocknations-web-preview.johannes-gaebler.workers.dev";
const REVIEW = "https://feedback.moneymattersmedia.com";
const protectedPaths = ["/index.html", "/Build/game.loader.js", "/Build/game.framework.js.unityweb", "/Build/game.data.unityweb", "/Build/game.wasm.unityweb", "/release.json", "/not-found"];
let mf, env;
let assetsRead = 0;
const reviewCalls = [];

before(async () => {
  mf = new Miniflare(convertV4MiniflareOptions({
    modules: await Promise.all(["service.mjs", "store.mjs", "tokens.mjs"].map(async name => ({
      type: "ESModule", path: new URL("./" + name, import.meta.url).pathname,
      contents: await readFile(new URL("./" + name, import.meta.url), "utf8"),
    }))),
    compatibilityDate: "2026-10-08", d1Databases: ["REVIEW_DB"],
    durableObjects: { ACCESS_STORE: { className: "AccessStore", useSQLite: true } },
    serviceBindings: { REVIEW: request => env.REVIEW.fetch(request) },
  }));
  const review = await mf.getD1Database("REVIEW_DB");
  await review.exec(`CREATE TABLE auth_user(id TEXT PRIMARY KEY,emailVerified INTEGER NOT NULL); CREATE TABLE auth_session(id TEXT PRIMARY KEY,userId TEXT NOT NULL,expiresAt INTEGER NOT NULL); CREATE TABLE auth_account(userId TEXT NOT NULL,providerId TEXT NOT NULL); CREATE TABLE users(auth_user_id TEXT PRIMARY KEY,status TEXT NOT NULL,role TEXT NOT NULL,email TEXT NOT NULL,display_name TEXT NOT NULL);`);
  env = {
    REVIEW_DB: review, ACCESS_STORE: await mf.getDurableObjectNamespace("ACCESS_STORE"),
    SESSION_SECRET: "synthetic-only-secret-" + crypto.randomUUID() + crypto.randomUUID(),
    GAME_ORIGIN: GAME, GAME_ORIGINS: JSON.stringify([GAME, PREVIEW]),
    ASSETS: { fetch: async request => {
      assetsRead++;
      return new Response("game bytes:" + new URL(request.url).pathname, { headers: { "Content-Type": "application/octet-stream", "Cache-Control": "public, max-age=3600" } });
    } },
    REVIEW: { fetch: async request => {
      const path = new URL(request.url).pathname;
      reviewCalls.push(path);
      const sid = (request.headers.get("Cookie") ?? "").replace("__Secure-better-auth.session_token=", "");
      const session = await review.prepare("SELECT * FROM auth_session WHERE id=? AND expiresAt>?").bind(sid, Date.now()).first();
      if (path === "/api/auth/get-session") {
        if (!session) return Response.json(null);
        const user = await review.prepare("SELECT * FROM auth_user WHERE id=?").bind(session.userId).first();
        return Response.json({ session: { id: sid, expiresAt: new Date(session.expiresAt).toISOString() }, user: { id: session.userId, emailVerified: user.emailVerified === 1 } });
      }
      assert.equal(path, "/api/me");
      if (!session) return Response.json({ error: "Sign in required" }, { status: 401 });
      const verified = await review.prepare("SELECT emailVerified FROM auth_user WHERE id=?").bind(session.userId).first();
      const google = await review.prepare("SELECT userId FROM auth_account WHERE userId=? AND providerId='google'").bind(session.userId).first();
      if (!verified?.emailVerified || !google) return Response.json({ error: "Google identity required" }, { status: 401 });
      const user = await review.prepare("SELECT * FROM users WHERE auth_user_id=?").bind(session.userId).first();
      return Response.json({ user });
    } },
  };
});
after(async () => { await mf?.dispose(); });

async function identity(name, status = "pending", role = "reviewer") {
  const uid = "synthetic-" + name; const sid = "session-" + name;
  await env.REVIEW_DB.prepare("INSERT INTO auth_user VALUES(?,1)").bind(uid).run();
  await env.REVIEW_DB.prepare("INSERT INTO auth_session VALUES(?,?,?)").bind(sid, uid, Date.now() + 86400000).run();
  await env.REVIEW_DB.prepare("INSERT INTO auth_account VALUES(?,'google')").bind(uid).run();
  await env.REVIEW_DB.prepare("INSERT INTO users VALUES(?,?,?,?,?)").bind(uid, status, role, name + "@example.test", name).run();
  return { uid, sid };
}

function request(path, { session = "", origin = GAME, method = "GET", body, sourceOrigin, headers = {}, environment = env } = {}) {
  const values = new Headers(headers);
  if (session) values.set("Cookie", session);
  if (sourceOrigin) values.set("Origin", sourceOrigin);
  if (body !== undefined) values.set("Content-Type", "application/x-www-form-urlencoded");
  return worker.fetch(new Request(origin + path, { method, headers: values, body }), environment);
}

function responseCookie(response, name) {
  return response.headers.getSetCookie().find(value => value.startsWith(name + "=")).split(";")[0];
}

async function beginSignIn(person, environment = env) {
  const start = await request("/auth/login", { origin: environment.GAME_ORIGIN, environment });
  assert.equal(start.status, 303);
  const attemptCookie = responseCookie(start, ATTEMPT_COOKIE);
  const authorization = await worker.fetch(new Request(start.headers.get("Location"), { headers: { Cookie: "__Secure-better-auth.session_token=" + person.sid } }), env);
  assert.equal(authorization.status, 303);
  const callbackURL = new URL(authorization.headers.get("Location"));
  const token = callbackURL.searchParams.get("code");
  assert.equal(callbackURL.origin, environment.GAME_ORIGIN);
  return { attemptCookie, token, start };
}

async function signIn(person, environment = env) {
  const { attemptCookie, token, start } = await beginSignIn(person, environment);
  const result = await request("/auth/callback?code=" + token, {
    origin: environment.GAME_ORIGIN, environment,
    session: attemptCookie,
  });
  assert.equal(result.status, 303);
  assert.ok(["/", "/access"].includes(result.headers.get("Location")));
  return { cookie: responseCookie(result, SESSION_COOKIE), attemptCookie, token, start, result };
}

test("anonymous visitors see sign-in and cannot fetch any game payload", async () => {
  const before = assetsRead;
  const landing = await request("/");
  assert.equal(landing.status, 200);
  assert.match(await landing.text(), /Welcome, tester/);
  for (const path of protectedPaths.filter(path => path !== "/index.html")) {
    const response = await request(path);
    assert.equal(response.status, 403, path);
    assert.equal(response.headers.get("Cache-Control"), "private, no-store");
    assert.doesNotMatch(await response.text(), /game bytes/);
  }
  assert.match(await (await request("/index.html")).text(), /Continue with Google/);
  assert.equal(assetsRead, before);
});

test("cold sign-in uses existing Google auth endpoint and registered Review callback", async () => {
  const start = await request("/auth/login");
  const response = await worker.fetch(new Request(start.headers.get("Location")), env);
  const text = await response.text();
  assert.match(text, /Sign in with Google/);
  assert.match(text, /\/api\/auth\/sign-in\/social/);
  assert.match(text, /provider:'google'/);
  assert.match(text, /callbackURL:/);
  assert.doesNotMatch(text, /game bytes|GOOGLE_CLIENT_SECRET/);
  const cookie = start.headers.get("Set-Cookie");
  assert.match(cookie, /__Host-blocknations_attempt=/);
  assert.match(cookie, /HttpOnly; Secure; SameSite=Lax/);
  assert.doesNotMatch(cookie, /Domain=/);
});

test("approved reviewers enter without additional approval or Google login", async () => {
  const person = await identity("reviewer", "approved");
  const result = await signIn(person);
  assert.equal(result.result.headers.get("Location"), "/");
  assert.match(await (await request("/access", { session: result.cookie })).text(), /You&#39;re ready to play/);
  const asset = await request("/Build/game.wasm.unityweb", { session: result.cookie });
  assert.equal(asset.status, 200);
  assert.match(await asset.text(), /game bytes/);
  assert.equal(asset.headers.get("Cache-Control"), "private, no-store");
  assert.match(result.result.headers.getSetCookie().find(cookie => cookie.startsWith(SESSION_COOKIE)), /HttpOnly; Secure; SameSite=Lax/);
  const tester = await storeCall(env, "tester", { uid: person.uid });
  assert.equal(tester.status, "pending"); // Derived access, no copied approval.
  assert.ok(reviewCalls.includes("/api/auth/get-session"));
});

test("new players remain pending across reload and repeated sign-in", async () => {
  const person = await identity("new-player");
  const login = await signIn(person);
  assert.equal(login.result.headers.get("Location"), "/access");
  assert.match(await (await request("/", { session: login.cookie })).text(), /Waiting for approval/);
  for (const path of protectedPaths.filter(path => path !== "/index.html")) assert.equal((await request(path, { session: login.cookie })).status, 403, path);
  await signIn(person);
  assert.equal((await storeCall(env, "list", {})).filter(tester => tester.auth_user_id === person.uid).length, 1);
  assert.equal((await env.REVIEW_DB.prepare("SELECT status FROM users WHERE auth_user_id=?").bind(person.uid).first()).status, "pending");
});

test("game approval is immediate but never changes article approval or role", async () => {
  const player = await identity("game-only"); const playerLogin = await signIn(player);
  const owner = await identity("owner", "approved", "admin"); const ownerLogin = await signIn(owner);
  assert.equal((await request("/admin/testers", { session: playerLogin.cookie })).status, 403);
  const queue = await request("/admin/testers", { session: ownerLogin.cookie });
  assert.equal(queue.status, 200);
  assert.match(await queue.text(), /Waiting for game approval/);
  const approved = await request("/admin/testers/decision", {
    session: ownerLogin.cookie, method: "POST", sourceOrigin: GAME,
    body: new URLSearchParams({ auth_user_id: player.uid, status: "approved" }),
  });
  assert.equal(approved.status, 303);
  assert.equal((await request("/Build/game.data.unityweb", { session: playerLogin.cookie })).status, 200);
  const review = await env.REVIEW_DB.prepare("SELECT status,role FROM users WHERE auth_user_id=?").bind(player.uid).first();
  assert.deepEqual(review, { status: "pending", role: "reviewer" });
  assert.equal((await request("/admin/testers", { session: playerLogin.cookie })).status, 403);
  assert.match(await (await request("/admin/testers", { session: ownerLogin.cookie })).text(), /Approved for the game only/);
});

test("game disable overrides automatic reviewer access and denies already issued cookies", async () => {
  const reviewer = await identity("disabled-reviewer", "approved"); const login = await signIn(reviewer);
  const owner = await identity("disable-owner", "approved", "admin"); const ownerLogin = await signIn(owner);
  assert.equal((await request("/Build/game.loader.js", { session: login.cookie })).status, 200);
  const decision = await request("/admin/testers/decision", { session: ownerLogin.cookie, method: "POST", sourceOrigin: GAME, body: new URLSearchParams({ auth_user_id: reviewer.uid, status: "disabled" }) });
  assert.equal(decision.status, 303);
  assert.equal((await request("/Build/game.loader.js", { session: login.cookie })).status, 403);
  assert.equal((await env.REVIEW_DB.prepare("SELECT status FROM users WHERE auth_user_id=?").bind(reviewer.uid).first()).status, "approved");
  await signIn(reviewer);
  assert.equal((await storeCall(env, "tester", { uid: reviewer.uid })).status, "disabled");
});

test("review revocation removes inherited access; independent game approval survives it", async () => {
  const reviewer = await identity("revoked-review", "approved"); const login = await signIn(reviewer);
  await env.REVIEW_DB.prepare("UPDATE users SET status='disabled' WHERE auth_user_id=?").bind(reviewer.uid).run();
  assert.equal((await request("/Build/game.loader.js", { session: login.cookie })).status, 403);
  await storeCall(env, "decide", { uid: reviewer.uid, status: "approved", actor: "synthetic-owner" });
  assert.equal((await request("/Build/game.loader.js", { session: login.cookie })).status, 200);
});

test("rejected accounts, expired sessions and Review logout cannot download game files", async () => {
  const person = await identity("session-check", "approved"); const login = await signIn(person);
  await storeCall(env, "decide", { uid: person.uid, status: "rejected", actor: "synthetic-owner" });
  assert.equal((await request("/release.json", { session: login.cookie })).status, 403);
  await storeCall(env, "decide", { uid: person.uid, status: "pending", actor: "synthetic-owner" });
  await env.REVIEW_DB.prepare("UPDATE auth_session SET expiresAt=? WHERE id=?").bind(Date.now() - 1, person.sid).run();
  assert.equal((await request("/release.json", { session: login.cookie })).status, 403);
  await env.REVIEW_DB.prepare("DELETE FROM auth_session WHERE id=?").bind(person.sid).run();
  assert.equal((await request("/release.json", { session: login.cookie })).status, 403);
});

test("unverified accounts and non-Google identities fail closed", async () => {
  const person = await identity("identity-check", "approved"); const login = await signIn(person);
  await env.REVIEW_DB.prepare("UPDATE auth_user SET emailVerified=0 WHERE id=?").bind(person.uid).run();
  assert.equal((await request("/release.json", { session: login.cookie })).status, 403);
  await env.REVIEW_DB.prepare("UPDATE auth_user SET emailVerified=1 WHERE id=?").bind(person.uid).run();
  await env.REVIEW_DB.prepare("DELETE FROM auth_account WHERE userId=?").bind(person.uid).run();
  assert.equal((await request("/release.json", { session: login.cookie })).status, 403);
});

test("handoffs require the initiating cookie, exact origin, correct audience and expiry", async () => {
  const person = await identity("handoff", "approved"); const login = await beginSignIn(person);
  const path = "/auth/callback?code=" + login.token;
  assert.equal((await request(path)).status, 400);
  assert.equal((await request(path, { session: login.attemptCookie, sourceOrigin: "https://evil.test" })).status, 403);
  assert.equal((await request(path, { method: "POST", session: login.attemptCookie })).status, 403);
  const preview = { ...env, GAME_ORIGIN: PREVIEW };
  assert.equal((await request(path, { session: login.attemptCookie, origin: PREVIEW, environment: preview })).status, 400);
  const other = await request("/auth/login");
  assert.equal((await request(path, { session: responseCookie(other, ATTEMPT_COOKIE) })).status, 400);
  assert.equal((await request(path + "forged", { session: login.attemptCookie })).status, 400);
  const result = await request(path, { session: login.attemptCookie });
  assert.equal(result.status, 303); // Invalid requests did not burn the code.
  assert.ok(result.headers.getSetCookie().some(cookie => cookie.startsWith(ATTEMPT_COOKIE) && cookie.includes("Max-Age=0")));
  assert.equal((await request(path, { session: login.attemptCookie })).status, 400); // One use even with the old cookie.
  const expired = await storeCall(env, "issue", { uid: person.uid, sid: "synthetic", audience: GAME,
    attempt: await fingerprint(login.attemptCookie.split("=")[1]), sessionExp: Date.now() + 60000, exp: Date.now() - 1 });
  assert.equal(await storeCall(env, "consume", { code: expired.code, audience: GAME, attempt: await fingerprint(login.attemptCookie.split("=")[1]) }), null);
  assert.equal((await request("/auth/callback?code=" + expired.code, { session: login.attemptCookie })).status, 400);
});

test("forged, duplicate, wrong-purpose and wrong-host session cookies are rejected", async () => {
  const person = await identity("forged", "approved"); const login = await signIn(person);
  for (const value of [SESSION_COOKIE + "=forged", login.cookie + "bad", login.cookie + "; " + login.cookie, SESSION_COOKIE + "=" + login.token]) {
    assert.equal((await request("/release.json", { session: value })).status, 403);
  }
  assert.equal((await request("/release.json", { session: login.cookie, environment: { ...env, GAME_ORIGIN: PREVIEW }, origin: PREVIEW })).status, 403);
  assert.equal((await request("/release.json", { session: login.cookie, origin: "https://alternate.test" })).status, 404);
  assert.equal((await request("/Build/game.loader.js", { session: login.cookie, origin: REVIEW })).status, 404);
});

test("preview shares identity and admission, but gets its own host-only session", async () => {
  const person = await identity("preview", "approved");
  const preview = { ...env, GAME_ORIGIN: PREVIEW };
  const login = await signIn(person, preview);
  assert.equal((await request("/release.json", { session: login.cookie, origin: PREVIEW, environment: preview })).status, 200);
  assert.equal((await request("/release.json", { session: login.cookie })).status, 403);
});

test("admin mutations reject CSRF, unapproved admins and invalid targets without approval changes", async () => {
  const owner = await identity("csrf-owner", "approved", "admin"); const login = await signIn(owner);
  const player = await identity("csrf-player"); await signIn(player);
  const body = new URLSearchParams({ auth_user_id: player.uid, status: "approved" });
  for (const sourceOrigin of [undefined, "https://evil.test", REVIEW]) {
    assert.equal((await request("/admin/testers/decision", { session: login.cookie, sourceOrigin, method: "POST", body })).status, 403);
  }
  assert.equal((await request("/admin/testers/decision", { session: login.cookie, sourceOrigin: GAME, method: "POST", body: new URLSearchParams({ auth_user_id: "missing", status: "approved" }) })).status, 404);
  assert.equal((await request("/admin/testers/decision", { session: login.cookie, sourceOrigin: GAME, method: "POST", body: new URLSearchParams({ auth_user_id: player.uid, status: "admin" }) })).status, 400);
  await env.REVIEW_DB.prepare("UPDATE users SET status='pending' WHERE auth_user_id=?").bind(owner.uid).run();
  assert.equal((await request("/admin/testers/decision", { session: login.cookie, sourceOrigin: GAME, method: "POST", body })).status, 403);
  assert.equal((await storeCall(env, "tester", { uid: player.uid })).status, "pending");
});

test("game logout is same-origin and does not alter shared identity or approvals", async () => {
  const person = await identity("logout", "approved"); const login = await signIn(person);
  assert.equal((await request("/auth/logout", { session: login.cookie })).status, 403);
  assert.equal((await request("/auth/logout", { session: login.cookie, method: "POST", sourceOrigin: "https://evil.test" })).status, 403);
  const logout = await request("/auth/logout", { session: login.cookie, method: "POST", sourceOrigin: GAME });
  assert.equal(logout.status, 303);
  assert.ok(logout.headers.getSetCookie().some(cookie => cookie.startsWith(SESSION_COOKIE) && cookie.includes("Max-Age=0")));
  assert.ok(await env.REVIEW_DB.prepare("SELECT id FROM auth_session WHERE id=?").bind(person.sid).first());
  assert.equal((await request("/release.json", { session: login.cookie })).status, 403);
});

test("database/auth outages and missing configuration never fall back to public assets", async () => {
  const before = assetsRead;
  assert.equal((await request("/Build/game.wasm.unityweb", { environment: { ...env, SESSION_SECRET: undefined } })).status, 503);
  const person = await identity("outage", "approved"); const login = await signIn(person);
  const broken = { ...env, ACCESS_STORE: { idFromName() { throw new Error("synthetic storage outage"); } } };
  assert.equal((await request("/Build/game.wasm.unityweb", { session: login.cookie, environment: broken })).status, 503);
  assert.equal(assetsRead, before);
});

test("encrypted claims bind purpose, audience and expiry without exposing identity", async () => {
  const token = await seal(env.SESSION_SECRET, { kind: "session", aud: GAME, uid: "private-identity", exp: Date.now() + 60000 });
  assert.doesNotMatch(token, /private-identity/);
  assert.equal((await open(env.SESSION_SECRET, token, "session", GAME)).uid, "private-identity");
  assert.equal(await open(env.SESSION_SECRET, token, "handoff", GAME), null);
  assert.equal(await open(env.SESSION_SECRET, token, "session", PREVIEW), null);
  assert.equal(await open(env.SESSION_SECRET, token, "session", GAME, Date.now() + 60001), null);
  assert.equal(await open(env.SESSION_SECRET + "other", token, "session", GAME), null);
  assert.ok(await fingerprint(token));
});

test("both deployment configs route every static asset through the gate", async () => {
  for (const name of ["wrangler.jsonc", "wrangler.production.jsonc"]) {
    const config = JSON.parse(await readFile(new URL("../" + name, import.meta.url), "utf8"));
    assert.equal(config.assets.run_worker_first, true);
    assert.equal(config.assets.binding, "ASSETS");
    assert.equal(config.main, "access/worker.mjs");
    assert.equal(config.preview_urls, false);
    assert.ok(config.durable_objects.bindings.some(binding => binding.name === "ACCESS_STORE"));
    assert.equal(config.d1_databases, undefined); // No access to sensitive articles.
  }
});
