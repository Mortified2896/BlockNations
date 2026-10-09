import { randomToken, fingerprint } from "./tokens.mjs";

const REVIEW_ORIGIN = "https://feedback.moneymattersmedia.com";

// This namespace is reachable only through the Worker binding. Browser routes
// never forward requests to it. Review cookies stay in private server storage;
// the game has no binding to the article database or Google OAuth secrets.
export class AccessStore {
  constructor(ctx, env) {
    this.sql = ctx.storage.sql;
    this.env = env;
    this.sql.exec(`CREATE TABLE IF NOT EXISTS game_testers (
      auth_user_id TEXT PRIMARY KEY, email TEXT NOT NULL, display_name TEXT NOT NULL,
      status TEXT NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','approved','rejected','disabled')),
      review_status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
      approved_at TEXT, reviewed_by TEXT
    );
    CREATE TABLE IF NOT EXISTS game_sessions (
      id TEXT PRIMARY KEY, auth_user_id TEXT NOT NULL, review_cookie TEXT NOT NULL,
      expires_at INTEGER NOT NULL, created_at INTEGER NOT NULL
    );
    CREATE INDEX IF NOT EXISTS game_sessions_expiry ON game_sessions(expires_at);
    CREATE INDEX IF NOT EXISTS game_sessions_user ON game_sessions(auth_user_id,created_at);
    CREATE TABLE IF NOT EXISTS game_handoffs (
      code_hash TEXT PRIMARY KEY, uid TEXT NOT NULL, sid TEXT NOT NULL, audience TEXT NOT NULL,
      attempt_hash TEXT NOT NULL, session_expiry INTEGER NOT NULL, expires_at INTEGER NOT NULL
    );
    CREATE INDEX IF NOT EXISTS game_handoffs_expiry ON game_handoffs(expires_at);
    CREATE TABLE IF NOT EXISTS game_feedback (
      id TEXT PRIMARY KEY, uid TEXT NOT NULL, origin TEXT NOT NULL,
      category TEXT NOT NULL, description TEXT NOT NULL, version TEXT NOT NULL,
      platform TEXT NOT NULL, screen TEXT NOT NULL, mode TEXT NOT NULL, turn INTEGER NOT NULL,
      screenshot TEXT NOT NULL, created_at TEXT NOT NULL
    );
    CREATE INDEX IF NOT EXISTS game_feedback_created ON game_feedback(created_at);
    CREATE INDEX IF NOT EXISTS game_feedback_user ON game_feedback(uid,created_at);`);
  }

  tester(uid) {
    return this.sql.exec("SELECT * FROM game_testers WHERE auth_user_id=?", uid).toArray()[0] ?? null;
  }

  remember(user) {
    const now = new Date().toISOString();
    this.sql.exec(`INSERT INTO game_testers(auth_user_id,email,display_name,review_status,created_at,updated_at)
      VALUES(?,?,?,?,?,?) ON CONFLICT(auth_user_id) DO UPDATE SET email=excluded.email,display_name=excluded.display_name,review_status=excluded.review_status
      WHERE email<>excluded.email OR display_name<>excluded.display_name OR review_status<>excluded.review_status`,
    user.auth_user_id, user.email, user.display_name, user.status, now, now);
    return this.tester(user.auth_user_id);
  }

  async resolve(id) {
    const session = this.sql.exec("SELECT * FROM game_sessions WHERE id=? AND expires_at>?", id, Date.now()).toArray()[0];
    if (!session) return null;
    const response = await this.env.REVIEW.fetch(new Request(REVIEW_ORIGIN + "/api/me", {
      headers: { Cookie: session.review_cookie },
    }));
    if ([401, 403].includes(response.status)) {
      this.sql.exec("DELETE FROM game_sessions WHERE id=?", id);
      return null;
    }
    if (!response.ok) throw new Error("Review authentication unavailable");
    const { user } = await response.json();
    if (!user || user.auth_user_id !== session.auth_user_id) throw new Error("Review identity mismatch");
    // Better Auth may refresh its host-only session cookie. Keep that refresh
    // private; never send Review cookies on the BlockNations hostname.
    const refresh = response.headers.getSetCookie().find(value => /^(?:__Secure-|__Host-)?better-auth\.session_token=/.test(value));
    if (refresh && refresh.split(";")[0] !== session.review_cookie) {
      this.sql.exec("UPDATE game_sessions SET review_cookie=? WHERE id=?", refresh.split(";")[0], id);
    }
    const tester = this.remember(user);
    const denied = ["rejected", "disabled"].includes(tester.status);
    const reviewApproved = user.status === "approved";
    return {
      auth_user_id: user.auth_user_id, email: user.email, display_name: user.display_name,
      status: denied ? tester.status : reviewApproved || tester.status === "approved" ? "approved" : "pending",
      review_status: user.status, game_status: tester.status,
      can_administer: user.status === "approved" && user.role === "admin",
    };
  }

  async fetch(request) {
    const operation = new URL(request.url).pathname;
    if (request.method !== "POST") return new Response(null, { status: 405 });
    const input = await request.json();
    if (operation === "/feedback-submit") {
      // First successful write wins; retrying an acknowledged or timed-out send
      // cannot silently replace its contents or attach a second screenshot.
      const existing = this.sql.exec("SELECT uid FROM game_feedback WHERE id=?", input.id).toArray()[0];
      if (existing) return Response.json({ limited: existing.uid !== input.uid });
      const since = new Date(Date.now() - 3600000).toISOString();
      const recent = this.sql.exec("SELECT COUNT(*) count FROM game_feedback WHERE uid=? AND created_at>?", input.uid, since).toArray()[0].count;
      if (recent >= 10) return Response.json({ limited: true });
      this.sql.exec("INSERT INTO game_feedback VALUES(?,?,?,?,?,?,?,?,?,?,?,?)", input.id, input.uid, input.origin,
        input.category, input.description, input.version, input.platform, input.screen, input.mode, input.turn,
        input.screenshot, new Date().toISOString());
      return Response.json({ saved: true });
    }
    if (operation === "/feedback-list") {
      if (!Number.isSafeInteger(input.offset) || input.offset < 0) return new Response(null, { status: 400 });
      return Response.json(this.sql.exec(`SELECT f.id,f.origin,f.category,f.description,f.version,f.platform,f.screen,f.mode,f.turn,f.created_at,
        (f.screenshot<>'') has_screenshot,t.display_name FROM game_feedback f LEFT JOIN game_testers t ON t.auth_user_id=f.uid
        ORDER BY f.created_at DESC,f.id LIMIT 26 OFFSET ?`, input.offset).toArray());
    }
    if (operation === "/feedback-image") {
      return Response.json(this.sql.exec("SELECT screenshot FROM game_feedback WHERE id=?", input.id).toArray()[0] ?? null);
    }
    if (operation === "/register") {
      const { user, reviewCookie, expiresAt } = input;
      if (!user?.auth_user_id || typeof reviewCookie !== "string" || !reviewCookie || reviewCookie.length > 4096 ||
          !Number.isSafeInteger(expiresAt) || expiresAt <= Date.now() || expiresAt > Date.now() + 30 * 86400000) {
        return new Response(null, { status: 400 });
      }
      this.remember(user);
      this.sql.exec("DELETE FROM game_sessions WHERE expires_at<=?", Date.now());
      // Bound session retention for repeated sign-in by an authenticated user.
      this.sql.exec(`DELETE FROM game_sessions WHERE auth_user_id=? AND id NOT IN
        (SELECT id FROM game_sessions WHERE auth_user_id=? ORDER BY created_at DESC LIMIT 19)`, user.auth_user_id, user.auth_user_id);
      const id = randomToken();
      this.sql.exec("INSERT INTO game_sessions VALUES(?,?,?,?,?)", id, user.auth_user_id, reviewCookie, expiresAt, Date.now());
      return Response.json({ id });
    }
    if (operation === "/resolve") return Response.json(await this.resolve(input.id));
    if (operation === "/issue") {
      const code = randomToken();
      this.sql.exec("DELETE FROM game_handoffs WHERE expires_at<=?", Date.now());
      this.sql.exec("INSERT INTO game_handoffs VALUES(?,?,?,?,?,?,?)", await fingerprint(code),
        input.uid, input.sid, input.audience, input.attempt, input.sessionExp, input.exp);
      return Response.json({ code });
    }
    if (operation === "/consume") {
      // Consume atomically only for the initiating browser and exact game origin.
      // A mismatched request cannot burn the legitimate browser's code.
      const result = this.sql.exec(`DELETE FROM game_handoffs WHERE code_hash=?
        AND audience=? AND attempt_hash=? AND expires_at>? RETURNING uid,sid,session_expiry sessionExp`,
      await fingerprint(input.code), input.audience, input.attempt, Date.now()).toArray();
      return Response.json(result[0] ?? null);
    }
    if (operation === "/logout") {
      this.sql.exec("DELETE FROM game_sessions WHERE id=?", input.id);
      return Response.json({ ok: true });
    }
    if (operation === "/tester") return Response.json(this.tester(input.uid));
    if (operation === "/list") {
      const offset = input.offset ?? 0;
      if (!Number.isSafeInteger(offset) || offset < 0) return new Response(null, { status: 400 });
      return Response.json(this.sql.exec("SELECT * FROM game_testers ORDER BY created_at DESC,auth_user_id LIMIT 51 OFFSET ?", offset).toArray());
    }
    if (operation === "/decide") {
      if (!input.uid || !["pending", "approved", "rejected", "disabled"].includes(input.status)) return new Response(null, { status: 400 });
      const now = new Date().toISOString();
      const result = this.sql.exec(`UPDATE game_testers SET status=?,updated_at=?,approved_at=?,reviewed_by=?
        WHERE auth_user_id=? RETURNING auth_user_id`, input.status, now, input.status === "approved" ? now : null, input.actor, input.uid).toArray();
      return Response.json({ found: result.length === 1 });
    }
    return new Response(null, { status: 404 });
  }
}

export async function storeCall(env, operation, input) {
  const store = env.ACCESS_STORE.get(env.ACCESS_STORE.idFromName("testers"));
  const response = await store.fetch(new Request("https://access.internal/" + operation, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(input),
  }));
  if (!response.ok) throw new Error("Access storage unavailable");
  return response.json();
}
