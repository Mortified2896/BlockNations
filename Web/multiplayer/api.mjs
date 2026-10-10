import { storeCall } from "../access/store.mjs";
import { displayName } from "./store.mjs";

export async function publicPlayer(user) {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode("blocknations-player:" + user.auth_user_id)));
  return { id: [...digest.slice(0,16)].map(byte => byte.toString(16).padStart(2,"0")).join(""),
    name: displayName(user.display_name), email: user.email, sessionId: user.web_session_id ?? "" };
}

async function readJSON(request) {
  if (!request.headers.get("Content-Type")?.toLowerCase().startsWith("application/json")) return null;
  const reader = request.body?.getReader();
  if (!reader) return null;
  let size = 0;
  const chunks = [];
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > 2100000) { await reader.cancel(); return null; }
    chunks.push(value);
  }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  try {
    const data = JSON.parse(new TextDecoder().decode(bytes));
    return data && typeof data === "object" && !Array.isArray(data) ? data : null;
  } catch { return null; }
}

export async function multiplayerAPI(request, env, user) {
  if (!env.MULTIPLAYER_STORE) return Response.json({ ok: false, error: "MULTIPLAYER_UNAVAILABLE" }, { status: 503 });
  const url = new URL(request.url), suffix = url.pathname.slice("/api/multiplayer/".length);
  let operation, data = {}, directory = [];
  if (request.method === "GET") {
    if (["session", "lobby", "push-config"].includes(suffix)) operation = suffix;
    if (suffix === "search") {
      const query = (url.searchParams.get("q") ?? "").trim();
      if (query.length < 3 || query.length > 254) return Response.json({ ok: false, error: "SEARCH_TOO_SHORT" }, { status: 400 });
      operation = "search";
      const found = await storeCall(env, "player-search", { query });
      directory = await Promise.all(found.map(publicPlayer));
    }
    const match = /^matches\/([a-f0-9]{32})(?:\/(next))?$/.exec(suffix);
    if (match) {
      operation = match[2] ? "next" : "match";
      data = { matchId: match[1], afterSeq: Number(url.searchParams.get("afterSeq") ?? "-1") };
    }
  } else if (request.method === "POST") {
    if (request.headers.get("Origin") !== env.GAME_ORIGIN || request.headers.get("X-BlockNations-Web") !== "1") {
      return Response.json({ ok: false, error: "ORIGIN_REJECTED" }, { status: 403 });
    }
    data = await readJSON(request);
    if (!data) return Response.json({ ok: false, error: "INVALID_REQUEST" }, { status: 400 });
    if (["find", "friend-request", "friend-answer", "invite", "invite-answer", "push-subscribe", "push-unsubscribe", "push-test"].includes(suffix)) operation = suffix;
    const match = /^matches\/([a-f0-9]{32})\/(initialize|turn|cancel)$/.exec(suffix);
    if (match) { operation = match[2]; data.matchId = match[1]; }
  }
  if (!operation) return Response.json({ ok: false, error: "NOT_FOUND" }, { status: 404 });
  const actor = await publicPlayer(user);
  const store = env.MULTIPLAYER_STORE.get(env.MULTIPLAYER_STORE.idFromName(env.GAME_ORIGIN));
  return store.fetch(new Request("https://multiplayer.internal/" + operation, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ actor, directory, data, origin: env.GAME_ORIGIN }),
  }));
}

export async function revokePushSession(env, sid) {
  if (!env.MULTIPLAYER_STORE) return;
  const { fingerprint } = await import("../access/tokens.mjs");
  const store = env.MULTIPLAYER_STORE.get(env.MULTIPLAYER_STORE.idFromName(env.GAME_ORIGIN));
  await store.fetch(new Request("https://multiplayer.internal/push-session-revoke", {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ sessionId: await fingerprint(sid) }),
  }));
}
