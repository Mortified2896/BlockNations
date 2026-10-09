import { open, seal, fingerprint, randomToken, cookie, setCookie, SESSION_COOKIE, ATTEMPT_COOKIE } from "./tokens.mjs";
import { page, loginPage, accountPage, googlePage } from "./pages.mjs";
import { adminPage, decide } from "./admission.mjs";
import { storeCall } from "./store.mjs";

const REVIEW_ORIGIN = "https://feedback.moneymattersmedia.com";
const BROKER_PATH = "/blocknations-auth/authorize";
const ATTEMPT_LIFETIME = 15 * 60 * 1000;
const HANDOFF_LIFETIME = 90 * 1000;
const SESSION_LIFETIME = 30 * 24 * 60 * 60 * 1000;

function authPath(env) {
  const path = env.AUTH_PATH ?? BROKER_PATH;
  if (!/^\/blocknations-auth(?:-preview)?\/authorize$/.test(path)) throw new Error("Invalid auth path");
  return path;
}

function json(value, status) {
  return new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json; charset=utf-8" } });
}

function redirect(location) {
  return new Response(null, { status: 303, headers: { Location: location } });
}

function secured(response) {
  const result = new Response(response.body, response);
  // All protected bytes must pass authorization again, even after logout/revocation.
  result.headers.set("Cache-Control", "private, no-store");
  result.headers.set("Vary", "Cookie");
  result.headers.set("Referrer-Policy", "no-referrer");
  result.headers.set("X-Content-Type-Options", "nosniff");
  result.headers.set("X-Frame-Options", "DENY");
  result.headers.set("X-Robots-Tag", "noindex, nofollow, noarchive");
  result.headers.set("Strict-Transport-Security", "max-age=31536000");
  return result;
}

function ready(env) {
  return env.SESSION_SECRET?.length >= 43 && env.ACCESS_STORE && env.REVIEW && env.ASSETS;
}

function gameOrigins(env) {
  const origins = JSON.parse(env.GAME_ORIGINS);
  if (!Array.isArray(origins) || !origins.includes(env.GAME_ORIGIN) || origins.some(origin =>
    typeof origin !== "string" || !origin.startsWith("https://") || new URL(origin).origin !== origin)) throw new Error("Invalid origins");
  return origins;
}

async function profile(env, uid, sid) {
  if (typeof uid !== "string" || typeof sid !== "string" || !uid || !sid) return null;
  const user = await storeCall(env, "resolve", { id: sid });
  return user?.auth_user_id === uid ? user : null;
}

async function gameProfile(request, env) {
  const session = await open(env.SESSION_SECRET, cookie(request, SESSION_COOKIE), "session", env.GAME_ORIGIN);
  return session ? profile(env, session.uid, session.sid) : null;
}

function withReviewCookies(response, sources) {
  for (const source of sources) {
    for (const value of source.headers.getSetCookie()) response.headers.append("Set-Cookie", value);
  }
  return response;
}

async function beginLogin(env, chooseAccount = false) {
  const ticket = await seal(env.SESSION_SECRET, {
    kind: "attempt", aud: REVIEW_ORIGIN, game: env.GAME_ORIGIN,
    nonce: randomToken(), chooseAccount, exp: Date.now() + ATTEMPT_LIFETIME,
  });
  const response = redirect(REVIEW_ORIGIN + authPath(env) + "?ticket=" + encodeURIComponent(ticket));
  response.headers.append("Set-Cookie", setCookie(ATTEMPT_COOKIE, ticket, ATTEMPT_LIFETIME / 1000));
  return response;
}

async function authorize(request, env) {
  const url = new URL(request.url);
  if (request.method !== "GET" || url.pathname !== authPath(env)) return json({ error: "Not found." }, 404);
  const ticket = url.searchParams.get("ticket");
  const attempt = await open(env.SESSION_SECRET, ticket, "attempt", REVIEW_ORIGIN);
  if (!attempt || !gameOrigins(env).includes(attempt.game) || typeof attempt.nonce !== "string") {
    return page("Sign-in expired", '<p>Please return to Block Nations and start sign-in again.</p>', { status: 400 });
  }
  const failed = url.searchParams.has("login_error");
  // This marker only selects the page flow. Identity still comes exclusively
  // from a verified Review session and the ordinary one-use handoff below.
  const returnedFromGoogle = url.searchParams.get("google_return") === "1";
  url.searchParams.delete("login_error");
  if (attempt.chooseAccount && (!returnedFromGoogle || failed)) {
    url.searchParams.set("google_return", "1");
    return googlePage(url.href, failed, true);
  }
  // The browser sends its host-only Review cookie only on this hostname. The
  // service binding verifies it inside Review; its Google secrets stay there.
  const headers = new Headers({ Cookie: request.headers.get("Cookie") ?? "" });
  for (const name of ["cf-connecting-ip", "user-agent"]) {
    if (request.headers.has(name)) headers.set(name, request.headers.get(name));
  }
  const sessionResponse = await env.REVIEW.fetch(new Request(REVIEW_ORIGIN + "/api/auth/get-session", { headers }));
  if (!sessionResponse.ok) throw new Error("Review sign-in unavailable");
  const identity = await sessionResponse.json();
  if (!identity?.user?.emailVerified || !identity?.session?.id || !identity?.user?.id) {
    return withReviewCookies(googlePage(url.href, failed, Boolean(attempt.chooseAccount)), [sessionResponse]);
  }
  // /api/me idempotently creates the normal pending Review profile for newcomers.
  const meResponse = await env.REVIEW.fetch(new Request(REVIEW_ORIGIN + "/api/me", { headers }));
  if (!meResponse.ok) throw new Error("Review profile unavailable");
  const me = await meResponse.json();
  if (me?.user?.auth_user_id !== identity.user.id) throw new Error("Identity mismatch");
  const sessionExpiry = Date.parse(identity.session.expiresAt);
  if (!Number.isFinite(sessionExpiry) || sessionExpiry <= Date.now()) throw new Error("Expired Review session");
  let reviewCookie = (request.headers.get("Cookie") ?? "").split(";").map(value => value.trim())
    .find(value => /^(?:__Secure-|__Host-)?better-auth\.session_token=/.test(value));
  for (const source of [sessionResponse, meResponse]) {
    const refresh = source.headers.getSetCookie().find(value => /^(?:__Secure-|__Host-)?better-auth\.session_token=/.test(value));
    if (refresh) reviewCookie = refresh.split(";")[0];
  }
  const sessionExp = Math.min(sessionExpiry, Date.now() + SESSION_LIFETIME);
  const stored = await storeCall(env, "register", { user: me.user, reviewCookie, expiresAt: sessionExp });
  const handoff = await storeCall(env, "issue", {
    audience: attempt.game, attempt: await fingerprint(ticket),
    uid: identity.user.id, sid: stored.id,
    sessionExp,
    exp: Math.min(attempt.exp, Date.now() + HANDOFF_LIFETIME),
  });
  return withReviewCookies(redirect(attempt.game + "/auth/callback?code=" + handoff.code), [sessionResponse, meResponse]);
}

async function callback(request, env) {
  if (request.method !== "GET" || (request.headers.has("Origin") && request.headers.get("Origin") !== REVIEW_ORIGIN)) {
    return json({ error: "Sign-in response rejected." }, 403);
  }
  const attemptToken = cookie(request, ATTEMPT_COOKIE);
  const attempt = await open(env.SESSION_SECRET, attemptToken, "attempt", REVIEW_ORIGIN);
  const code = new URL(request.url).searchParams.get("code");
  if (!attempt || attempt.game !== env.GAME_ORIGIN || !/^[\w-]{43}$/.test(code ?? "")) {
    return page("Sign-in expired", '<p>Please return to the playtest and sign in again.</p><a class="button" href="/auth/login">Try again</a>', { status: 400 });
  }
  const handoff = await storeCall(env, "consume", { code, audience: env.GAME_ORIGIN, attempt: await fingerprint(attemptToken) });
  if (!handoff ||
      !Number.isSafeInteger(handoff.sessionExp) || handoff.sessionExp <= Date.now() ||
      handoff.sessionExp > Date.now() + SESSION_LIFETIME) {
    return page("Sign-in expired", '<p>Please return to the playtest and sign in again.</p><a class="button" href="/auth/login">Try again</a>', { status: 400 });
  }
  const user = await profile(env, handoff.uid, handoff.sid);
  if (!user) return json({ error: "Sign-in could not be verified." }, 400);
  const session = await seal(env.SESSION_SECRET, {
    kind: "session", aud: env.GAME_ORIGIN, uid: handoff.uid, sid: handoff.sid, exp: handoff.sessionExp,
  });
  const response = redirect(user.status === "approved" ? "/" : "/access");
  response.headers.append("Set-Cookie", setCookie(ATTEMPT_COOKIE, "", 0));
  response.headers.append("Set-Cookie", setCookie(SESSION_COOKIE, session, (handoff.sessionExp - Date.now()) / 1000));
  return response;
}

async function handle(request, env) {
  if (!ready(env)) return page("Playtest temporarily unavailable", '<p>Please try again shortly.</p>', { status: 503 });
  const url = new URL(request.url);
  if (url.origin === REVIEW_ORIGIN) return authorize(request, env);
  if (url.origin !== env.GAME_ORIGIN || !gameOrigins(env).includes(url.origin)) return json({ error: "Not found." }, 404);
  if (url.pathname === "/auth/callback") return callback(request, env);
  if (url.pathname === "/auth/switch-account") {
    if (request.method !== "POST" || request.headers.get("Origin") !== env.GAME_ORIGIN) return json({ error: "Request origin rejected." }, 403);
    const session = await open(env.SESSION_SECRET, cookie(request, SESSION_COOKIE), "session", env.GAME_ORIGIN);
    if (session) await storeCall(env, "logout", { id: session.sid });
    const response = await beginLogin(env, true);
    response.headers.append("Set-Cookie", setCookie(SESSION_COOKIE, "", 0));
    return response;
  }
  if (url.pathname === "/auth/logout") {
    if (request.method !== "POST" || request.headers.get("Origin") !== env.GAME_ORIGIN) return json({ error: "Request origin rejected." }, 403);
    const session = await open(env.SESSION_SECRET, cookie(request, SESSION_COOKIE), "session", env.GAME_ORIGIN);
    if (session) await storeCall(env, "logout", { id: session.sid });
    const response = redirect("/access");
    response.headers.append("Set-Cookie", setCookie(SESSION_COOKIE, "", 0));
    response.headers.append("Set-Cookie", setCookie(ATTEMPT_COOKIE, "", 0));
    return response;
  }
  if (url.pathname.startsWith("/admin/")) {
    const actor = await gameProfile(request, env);
    if (!actor?.can_administer) return json({ error: "Approved Review administrator access required." }, 403);
    if (url.pathname === "/admin/testers/decision") return decide(request, env, actor);
    if (url.pathname === "/admin/testers" && request.method === "GET") return adminPage(request, env);
    return json({ error: "Not found." }, 404);
  }
  if (!["GET", "HEAD"].includes(request.method)) return json({ error: "Method not allowed." }, 405);
  if (url.pathname === "/auth/login") {
    return beginLogin(env);
  }
  const user = await gameProfile(request, env);
  if (url.pathname === "/access" || ((!user || user.status !== "approved") && ["/", "/index.html"].includes(url.pathname))) {
    return user ? accountPage(user) : loginPage();
  }
  if (!user || user.status !== "approved") return json({ error: "Approved tester access required." }, 403);
  // No game HTML, loader, data, Wasm, metadata or alternate asset route is public.
  return env.ASSETS.fetch(request);
}

export default {
  async fetch(request, env) {
    let response;
    try {
      response = await handle(request, env);
    } catch {
      // Never log cookies, provider data, handoff envelopes or private profiles.
      response = page("Playtest temporarily unavailable", '<p>Please try again shortly. If this persists, contact Jo.</p>', { status: 503 });
    }
    response = secured(response);
    return request.method === "HEAD" ? new Response(null, response) : response;
  },
};
