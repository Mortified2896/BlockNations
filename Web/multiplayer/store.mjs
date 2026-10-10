import { validSubscription, sendPush } from "./push.mjs";

// A private, origin-scoped SQLite Durable Object. Identity is supplied only by
// the Google admission Worker, never by a browser's request body.
const ID = /^[a-f0-9]{32}$/;
export const WEB_MATCH_VERSION = "web-pbp-1";
const STATES = ["Active", "Eliminated", "Resigned"];

class Fault extends Error {
  constructor(error, status = 400) { super(error); this.status = status; }
}
function require(value, error, status = 400) { if (!value) throw new Fault(error, status); }
export function displayName(value) {
  return String(value ?? "").replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, 32) || "Player";
}
const freshId = () => crypto.randomUUID().replaceAll("-", "");

export class MultiplayerStore {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
    this.sql = ctx.storage.sql;
    this.sql.exec(`CREATE TABLE IF NOT EXISTS players (
      id TEXT PRIMARY KEY, name TEXT NOT NULL, email TEXT NOT NULL, updated INTEGER NOT NULL);
    CREATE TABLE IF NOT EXISTS matches (
      id TEXT PRIMARY KEY, kind TEXT NOT NULL, status TEXT NOT NULL,
      version TEXT NOT NULL, created INTEGER NOT NULL, updated INTEGER NOT NULL,
      seq INTEGER NOT NULL DEFAULT 2, revision INTEGER NOT NULL DEFAULT 1,
      snapshot TEXT, last_actor TEXT, last_base INTEGER, last_payload TEXT);
    CREATE INDEX IF NOT EXISTS match_queue ON matches(status,kind,created);
    CREATE TABLE IF NOT EXISTS seats (
      match_id TEXT NOT NULL, seat INTEGER NOT NULL, player_id TEXT NOT NULL,
      PRIMARY KEY(match_id,seat), UNIQUE(match_id,player_id));
    CREATE INDEX IF NOT EXISTS player_matches ON seats(player_id,match_id);
    CREATE TABLE IF NOT EXISTS friendships (
      id TEXT PRIMARY KEY, a TEXT NOT NULL, b TEXT NOT NULL, requester TEXT NOT NULL,
      status TEXT NOT NULL, updated INTEGER NOT NULL, UNIQUE(a,b));
    CREATE TABLE IF NOT EXISTS invitations (
      id TEXT PRIMARY KEY, sender TEXT NOT NULL, recipient TEXT NOT NULL,
      status TEXT NOT NULL, match_id TEXT, created INTEGER NOT NULL);
    CREATE INDEX IF NOT EXISTS invitation_inbox ON invitations(recipient,status);
    CREATE TABLE IF NOT EXISTS limits (player_id TEXT NOT NULL, bucket TEXT NOT NULL,
      window INTEGER NOT NULL, count INTEGER NOT NULL, PRIMARY KEY(player_id,bucket));
    CREATE TABLE IF NOT EXISTS subscriptions (endpoint TEXT PRIMARY KEY,player_id TEXT NOT NULL,
      session_id TEXT NOT NULL,subscription TEXT NOT NULL,updated INTEGER NOT NULL);
    CREATE INDEX IF NOT EXISTS push_players ON subscriptions(player_id);
    CREATE TABLE IF NOT EXISTS notifications (id TEXT PRIMARY KEY,endpoint TEXT NOT NULL,
      player_id TEXT NOT NULL,payload TEXT NOT NULL,attempts INTEGER NOT NULL DEFAULT 0,
      next_attempt INTEGER NOT NULL,created INTEGER NOT NULL);`);
  }

  one(query, ...args) { return this.sql.exec(query, ...args).toArray()[0] ?? null; }
  all(query, ...args) { return this.sql.exec(query, ...args).toArray(); }
  player(id) { return this.one("SELECT id,name FROM players WHERE id=?", id); }
  remember(player) {
    require(ID.test(player?.id ?? "") && typeof player.name === "string" && typeof player.email === "string", "INVALID_IDENTITY");
    this.sql.exec(`INSERT INTO players VALUES(?,?,?,?) ON CONFLICT(id) DO UPDATE SET
      name=excluded.name,email=excluded.email,updated=excluded.updated
      WHERE name<>excluded.name OR email<>excluded.email`, player.id, displayName(player.name), player.email.toLowerCase(), Date.now());
  }
  limit(id, bucket, maximum) {
    const window = Math.floor(Date.now() / 60000);
    const current = this.one("SELECT * FROM limits WHERE player_id=? AND bucket=?", id, bucket);
    require(!current || current.window !== window || current.count < maximum, "RATE_LIMITED", 429);
    this.sql.exec(`INSERT INTO limits VALUES(?,?,?,1) ON CONFLICT(player_id,bucket) DO UPDATE SET
      window=excluded.window,count=CASE WHEN limits.window=excluded.window THEN limits.count+1 ELSE 1 END`, id, bucket, window);
  }
  match(id, actor) {
    require(ID.test(id ?? ""), "INVALID_MATCH");
    const match = this.one("SELECT m.*,s.seat FROM matches m JOIN seats s ON s.match_id=m.id WHERE m.id=? AND s.player_id=?", id, actor);
    require(match, "MATCH_NOT_FOUND", 404);
    return match;
  }
  members(id) {
    return this.all("SELECT s.seat,p.id,p.name FROM seats s JOIN players p ON p.id=s.player_id WHERE s.match_id=? ORDER BY s.seat", id);
  }
  summary(match, actor) {
    const players = this.members(match.id);
    const state = match.snapshot ? JSON.parse(match.snapshot) : null;
    return { id: match.id, kind: match.kind, status: match.status, version: match.version,
      seatIndex: players.find(p => p.id === actor)?.seat ?? -1, players, seq: match.seq,
      revision: match.revision, ready: Boolean(state), currentTurnSeatIndex: state?.currentTurnSeatIndex ?? 0,
      turnNumber: state?.turnNumber ?? 1, winnerSeatIndex: state?.hasWinnerSeatIndex ? state.winnerSeatIndex : -1,
      created: match.created, updated: match.updated, boardSize: 7 };
  }
  canonical(match) {
    if (!match.snapshot) return null;
    const snapshot = JSON.parse(match.snapshot);
    const members = this.members(match.id);
    snapshot.seats = [0, 1].map(seatIndex => {
      const member = members.find(player => player.seat === seatIndex);
      const state = snapshot.seats?.find(seat => seat.seatIndex === seatIndex)?.state;
      return { seatIndex, state: member ? STATES.includes(state) ? state : "Active" : "Unclaimed",
        claimedPlayerId: member?.id ?? "", typedDisplayName: member?.name ?? "" };
    });
    snapshot.playerOneTypedDisplayName = snapshot.seats[0].typedDisplayName;
    snapshot.playerTwoTypedDisplayName = snapshot.seats[1].typedDisplayName;
    return JSON.stringify(snapshot);
  }
  create(actor, opponent = null) {
    const id = freshId(), now = Date.now();
    this.sql.exec("INSERT INTO matches(id,kind,status,version,created,updated) VALUES(?,?,?,?,?,?)", id,
      opponent ? "invite" : "public", opponent ? "active" : "waiting", WEB_MATCH_VERSION, now, now);
    this.sql.exec("INSERT INTO seats VALUES(?,0,?)", id, actor);
    if (opponent) this.sql.exec("INSERT INTO seats VALUES(?,1,?)", id, opponent);
    return this.match(id, actor);
  }
  notify(playerId, eventId, body, path = "/multiplayer", origin) {
    if (!playerId || !this.env.VAPID_PUBLIC_KEY || !this.env.VAPID_PRIVATE_KEY) return;
    const subscriptions = this.all("SELECT endpoint FROM subscriptions WHERE player_id=?", playerId);
    for (const subscription of subscriptions) {
      this.sql.exec("INSERT OR IGNORE INTO notifications(id,endpoint,player_id,payload,next_attempt,created) VALUES(?,?,?,?,?,?)",
        eventId + ":" + subscription.endpoint, subscription.endpoint, playerId,
        JSON.stringify({ title: "Block Nations", body, url: origin + path, tag: eventId }), Date.now(), Date.now());
    }
  }
  async scheduleNotifications() {
    if (!this.one("SELECT id FROM notifications LIMIT 1")) return;
    const scheduled = await this.ctx.storage.getAlarm();
    if (!scheduled) await this.ctx.storage.setAlarm(Date.now() + 1000);
  }
  async alarm() {
    const rows = this.all(`SELECT n.*,s.subscription FROM notifications n LEFT JOIN subscriptions s ON s.endpoint=n.endpoint AND s.player_id=n.player_id
      WHERE next_attempt<=? ORDER BY created LIMIT 20`, Date.now());
    for (const row of rows) {
      if (!row.subscription || row.created < Date.now() - 86400000) {
        this.sql.exec("DELETE FROM notifications WHERE id=?", row.id); continue;
      }
      let status = 0;
      try { status = await sendPush(JSON.parse(row.subscription), JSON.parse(row.payload), this.env); } catch { }
      if (status === 404 || status === 410) this.sql.exec("DELETE FROM subscriptions WHERE endpoint=? AND player_id=?", row.endpoint, row.player_id);
      if ((status >= 200 && status < 300) || status === 404 || status === 410 || row.attempts >= 3) {
        this.sql.exec("DELETE FROM notifications WHERE id=?", row.id);
      } else {
        this.sql.exec("UPDATE notifications SET attempts=attempts+1,next_attempt=? WHERE id=?", Date.now() + 30000 * 2 ** row.attempts, row.id);
      }
    }
    const next = this.one("SELECT MIN(next_attempt) time FROM notifications");
    if (next?.time) await this.ctx.storage.setAlarm(Math.max(Date.now() + 1000, next.time));
  }
  validateSnapshot(snapshot, match) {
    require(snapshot && typeof snapshot === "object" && !Array.isArray(snapshot), "INVALID_SNAPSHOT");
    require(snapshot.gameId === match.id && snapshot.mode === "PlayByPost" && snapshot.protocolVersion === 5 &&
      typeof snapshot.appVersion === "string" && snapshot.appVersion.length <= 80 && snapshot.appVersion.length > 0 &&
      snapshot.boardWidth === 7 && snapshot.boardHeight === 7 && snapshot.mapSizePreset === "Standard7" && snapshot.seatCount === 2,
    "INCOMPATIBLE_SNAPSHOT", 409);
    require(Number.isSafeInteger(snapshot.turnNumber) && snapshot.turnNumber >= 1 && snapshot.turnNumber <= 1000000 &&
      [0, 1].includes(snapshot.currentTurnSeatIndex) && snapshot.transportSeq === snapshot.turnNumber * 2 + snapshot.currentTurnSeatIndex &&
      snapshot.isPlayerTurn === (snapshot.currentTurnSeatIndex === 0) && typeof snapshot.gameOver === "boolean", "INVALID_TURN");
    require(Array.isArray(snapshot.seats) && snapshot.seats.length === 2 &&
      snapshot.seats.every((seat, i) => seat?.seatIndex === i && ["Unclaimed", ...STATES].includes(seat.state)), "INVALID_SEATS");
    require(Array.isArray(snapshot.seatGold) && snapshot.seatGold.length === 2 &&
      snapshot.seatGold.every(gold => Number.isSafeInteger(gold) && gold >= 0 && gold <= 1000000), "INVALID_GOLD");
    require(Array.isArray(snapshot.tiles) && snapshot.tiles.length === 49 && Array.isArray(snapshot.cities) &&
      snapshot.cities.length <= 49 && Array.isArray(snapshot.units) && snapshot.units.length <= 196, "INVALID_BOARD");
    const inBoard = (x, y) => Number.isSafeInteger(x) && x >= 0 && x < 7 && Number.isSafeInteger(y) && y >= 0 && y < 7;
    require(snapshot.tiles.every(tile => inBoard(tile?.x, tile?.y)) &&
      new Set(snapshot.tiles.map(tile => tile.x + "," + tile.y)).size === 49 &&
      snapshot.cities.every(city => inBoard(city?.x, city?.y) && [0,1].includes(city.ownerSeatIndex)) &&
      snapshot.units.every(unit => Number.isFinite(unit?.x) && Number.isFinite(unit?.y) && Math.abs(unit.x) <= 10 && Math.abs(unit.y) <= 10 && [0,1].includes(unit.ownerSeatIndex)), "INVALID_BOARD");
    if (snapshot.gameOver) require(snapshot.hasWinnerSeatIndex === true && [0,1].includes(snapshot.winnerSeatIndex), "INVALID_WINNER");
  }

  operate(operation, input) {
    if (operation === "push-session-revoke") {
      require(typeof input.sessionId === "string" && input.sessionId.length >= 32, "INVALID_SESSION");
      this.sql.exec("DELETE FROM subscriptions WHERE session_id=?", input.sessionId);
      return { saved: true };
    }
    const actor = input.actor.id, data = input.data ?? {};
    this.remember(input.actor);
    for (const player of input.directory ?? []) this.remember(player);
    this.limit(actor, operation === "turn" ? "turn" : operation === "next" ? "poll" : "lobby", operation === "next" ? 90 : operation === "turn" ? 30 : 90);
    if (operation === "session") return { player: this.player(actor), version: WEB_MATCH_VERSION };
    if (operation === "push-config") return { publicKey: this.env.VAPID_PUBLIC_KEY ?? "",
      enabled: Boolean(this.env.VAPID_PUBLIC_KEY && this.env.VAPID_PRIVATE_KEY) };
    if (["push-subscribe", "push-unsubscribe", "push-test"].includes(operation)) {
      this.limit(actor, "push", 10);
      if (operation === "push-subscribe") {
        require(this.env.VAPID_PRIVATE_KEY && this.env.VAPID_PUBLIC_KEY, "PUSH_UNAVAILABLE", 503);
        require(validSubscription(data.subscription) && input.actor.sessionId, "INVALID_SUBSCRIPTION");
        const endpoint = data.subscription.endpoint;
        require(this.one("SELECT COUNT(*) count FROM subscriptions WHERE player_id=? AND endpoint<>?", actor, endpoint).count < 5, "TOO_MANY_DEVICES", 409);
        this.sql.exec(`INSERT INTO subscriptions VALUES(?,?,?,?,?) ON CONFLICT(endpoint) DO UPDATE SET
          player_id=excluded.player_id,session_id=excluded.session_id,subscription=excluded.subscription,updated=excluded.updated`,
        endpoint, actor, input.actor.sessionId, JSON.stringify(data.subscription), Date.now());
      } else if (operation === "push-unsubscribe") {
        require(typeof data.endpoint === "string", "INVALID_SUBSCRIPTION");
        this.sql.exec("DELETE FROM subscriptions WHERE endpoint=? AND player_id=?", data.endpoint, actor);
      } else {
        require(this.one("SELECT endpoint FROM subscriptions WHERE player_id=?", actor), "NOT_SUBSCRIBED", 409);
        this.notify(actor, "test:" + freshId(), "Notifications are enabled. We'll let you know when it's your turn or a request arrives.", "/multiplayer", input.origin);
      }
      return { saved: true };
    }
    if (operation === "search") {
      return { players: (input.directory ?? []).filter(player => player.id !== actor).slice(0, 8).map(player => this.player(player.id)) };
    }
    if (operation === "lobby") {
      const matches = this.all(`SELECT m.* FROM matches m JOIN seats s ON s.match_id=m.id
        WHERE s.player_id=? AND m.status<>'cancelled' ORDER BY m.updated DESC LIMIT 100`, actor).map(match => this.summary(match, actor));
      const links = this.all("SELECT * FROM friendships WHERE (a=? OR b=?) AND status IN ('pending','accepted') ORDER BY updated DESC LIMIT 100", actor, actor);
      const friendships = links.map(link => ({ id: link.id, status: link.status,
        incoming: link.requester !== actor, player: this.player(link.a === actor ? link.b : link.a) }));
      const invites = this.all("SELECT * FROM invitations WHERE (sender=? OR recipient=?) AND status IN ('pending','accepted') ORDER BY created DESC LIMIT 100", actor, actor)
        .map(invite => ({ id: invite.id, status: invite.status, incoming: invite.recipient === actor,
          player: this.player(invite.recipient === actor ? invite.sender : invite.recipient), matchId: invite.match_id }));
      return { player: this.player(actor), matches, friendships, invites, version: WEB_MATCH_VERSION };
    }
    if (operation === "find") {
      require(data.version === WEB_MATCH_VERSION, "UPDATE_REQUIRED", 409);
      const owned = this.one(`SELECT m.* FROM matches m JOIN seats s ON s.match_id=m.id
        WHERE s.player_id=? AND m.kind='public' AND m.status='waiting' AND m.version=? LIMIT 1`, actor, WEB_MATCH_VERSION);
      if (owned) return { match: this.summary(owned, actor) };
      require(this.one("SELECT COUNT(*) count FROM matches m JOIN seats s ON s.match_id=m.id WHERE s.player_id=? AND m.status IN ('active','waiting')", actor).count < 20, "TOO_MANY_MATCHES", 409);
      const waiting = this.one(`SELECT m.* FROM matches m WHERE m.kind='public' AND m.status='waiting' AND m.version=?
        AND NOT EXISTS(SELECT 1 FROM seats s WHERE s.match_id=m.id AND s.player_id=?) ORDER BY m.created,m.id LIMIT 1`, WEB_MATCH_VERSION, actor);
      const match = waiting ?? this.create(actor);
      if (waiting) {
        this.sql.exec("INSERT INTO seats VALUES(?,1,?)", waiting.id, actor);
        this.sql.exec("UPDATE matches SET status='active',revision=revision+1,updated=? WHERE id=?", Date.now(), waiting.id);
        const host = this.members(waiting.id).find(player => player.seat === 0);
        this.notify(host.id, "joined:" + waiting.id, "Another player joined your online match.", "/?match=" + waiting.id, input.origin);
        if (waiting.seq % 2 === 1) this.notify(actor, "turn:" + waiting.id + ":" + waiting.seq, "It's your turn in your new match.", "/?match=" + waiting.id, input.origin);
      }
      return { match: this.summary(this.match(match.id, actor), actor) };
    }
    if (operation === "match" || operation === "next") {
      const match = this.match(data.matchId, actor);
      require(match.status !== "cancelled", "MATCH_CANCELLED", 409);
      if (operation === "next") {
        require(Number.isSafeInteger(data.afterSeq) && data.afterSeq >= -1, "INVALID_SEQUENCE");
        if (!match.snapshot || match.seq <= data.afterSeq) return { ok: false, error: "NO_TURN" };
      }
      return { match: this.summary(match, actor), json: this.canonical(match), seq: match.seq };
    }
    if (operation === "cancel") {
      const match = this.match(data.matchId, actor);
      require(match.status === "waiting" && match.seat === 0, "MATCH_ALREADY_STARTED", 409);
      this.sql.exec("UPDATE matches SET status='cancelled',updated=? WHERE id=?", Date.now(), match.id);
      return { cancelled: true };
    }
    if (operation === "initialize" || operation === "turn") {
      const match = this.match(data.matchId, actor);
      require(["waiting", "active"].includes(match.status) || (operation === "turn" && match.status === "finished"), "MATCH_CLOSED", 409);
      require(typeof data.json === "string" && new TextEncoder().encode(data.json).length <= 2000000, "INVALID_SNAPSHOT");
      let snapshot;
      try { snapshot = JSON.parse(data.json); } catch { throw new Fault("INVALID_SNAPSHOT"); }
      this.validateSnapshot(snapshot, match);
      if (operation === "initialize") {
        if (!match.snapshot) {
          require(snapshot.transportSeq === 2 && !snapshot.gameOver && snapshot.units.length === 0 &&
            snapshot.cities.length === 2 && snapshot.cities.some(city => city.ownerSeatIndex === 0 && city.x === 1 && city.y === 1) &&
            snapshot.cities.some(city => city.ownerSeatIndex === 1 && city.x === 5 && city.y === 5) &&
            snapshot.seatGold[0] === 3 && snapshot.seatGold[1] === 2, "INVALID_OPENING");
          this.sql.exec("UPDATE matches SET snapshot=?,revision=revision+1,updated=? WHERE id=?", data.json, Date.now(), match.id);
        }
      } else {
        require(Number.isSafeInteger(data.baseSeq), "INVALID_SEQUENCE");
        // A lost response can be retried without applying a turn twice. Other
        // writes from stale tabs never overwrite the accepted snapshot.
        if (!(match.last_actor === actor && match.last_base === data.baseSeq && match.last_payload === data.json)) {
          require(match.snapshot && match.status !== "finished" && match.seq === data.baseSeq, "CONFLICT", 409);
          const previous = JSON.parse(match.snapshot);
          require(previous.currentTurnSeatIndex === match.seat, "NOT_YOUR_TURN", 403);
          require(snapshot.appVersion === previous.appVersion, "UPDATE_REQUIRED", 409);
          require(snapshot.transportSeq === match.seq + 1 ||
            (snapshot.gameOver && snapshot.transportSeq === match.seq + 2), "INVALID_SEQUENCE", 409);
          for (let seat = 0; seat < 2; seat++) {
            const oldState = previous.seats[seat].state, newState = snapshot.seats[seat].state;
            require(!["Resigned","Eliminated"].includes(oldState) || newState === oldState, "INVALID_SEATS");
            require(newState !== "Resigned" || oldState === "Resigned" || seat === match.seat, "INVALID_SEATS");
          }
          this.sql.exec(`UPDATE matches SET snapshot=?,seq=?,revision=revision+1,status=?,updated=?,last_actor=?,last_base=?,last_payload=? WHERE id=?`,
            data.json, snapshot.transportSeq, snapshot.gameOver ? "finished" : match.status, Date.now(), actor, data.baseSeq, data.json, match.id);
          const recipient = this.members(match.id).find(player => player.seat === (snapshot.gameOver ? 1 - match.seat : snapshot.currentTurnSeatIndex));
          this.notify(recipient?.id, "turn:" + match.id + ":" + snapshot.transportSeq,
            snapshot.gameOver ? "Your match has finished. Open it to see the result." : "It's your turn in Block Nations.", "/?match=" + match.id, input.origin);
        }
      }
      const saved = this.match(match.id, actor);
      return { match: this.summary(saved, actor), json: this.canonical(saved), seq: saved.seq };
    }
    if (operation === "friend-request" || operation === "invite") {
      const target = data.playerId;
      require(ID.test(target ?? "") && target !== actor && this.player(target), "PLAYER_NOT_FOUND", 404);
      this.limit(actor, "social-send", 15);
      if (operation === "friend-request") {
        const [a, b] = [actor, target].sort();
        const existing = this.one("SELECT * FROM friendships WHERE a=? AND b=?", a, b);
        if (existing?.status === "pending" || existing?.status === "accepted") return { friendshipId: existing.id };
        const id = existing?.id ?? freshId();
        this.sql.exec(`INSERT INTO friendships VALUES(?,?,?,?,'pending',?) ON CONFLICT(a,b) DO UPDATE SET
          requester=excluded.requester,status='pending',updated=excluded.updated`, id, a, b, actor, Date.now());
        this.notify(target, "friend:" + id + ":" + Date.now(), "You received a friend request.", "/multiplayer", input.origin);
        return { friendshipId: id };
      }
      const existing = this.one("SELECT id FROM invitations WHERE sender=? AND recipient=? AND status='pending'", actor, target);
      if (existing) return { inviteId: existing.id };
      require(this.one("SELECT COUNT(*) count FROM invitations WHERE sender=? AND status='pending'", actor).count < 20, "TOO_MANY_INVITES", 409);
      const id = freshId();
      this.sql.exec("INSERT INTO invitations VALUES(?,?,?,'pending',NULL,?)", id, actor, target, Date.now());
      this.notify(target, "invite:" + id, "You received an invitation to a 7×7 match.", "/multiplayer", input.origin);
      return { inviteId: id };
    }
    if (operation === "friend-answer") {
      require(ID.test(data.id ?? "") && ["accept","decline","remove"].includes(data.action), "INVALID_REQUEST");
      const link = this.one("SELECT * FROM friendships WHERE id=? AND (a=? OR b=?)", data.id, actor, actor);
      require(link, "REQUEST_NOT_FOUND", 404);
      if (data.action !== "remove") require(link.status === "pending" && link.requester !== actor, "NOT_RECIPIENT", 403);
      this.sql.exec("UPDATE friendships SET status=?,updated=? WHERE id=?", data.action === "accept" ? "accepted" : "declined", Date.now(), link.id);
      return { saved: true };
    }
    if (operation === "invite-answer") {
      require(ID.test(data.id ?? "") && ["accept","decline","cancel"].includes(data.action), "INVALID_REQUEST");
      const invite = this.one("SELECT * FROM invitations WHERE id=?", data.id);
      require(invite && (invite.sender === actor || invite.recipient === actor), "REQUEST_NOT_FOUND", 404);
      require(data.action === "cancel" ? invite.sender === actor : invite.recipient === actor, "NOT_RECIPIENT", 403);
      if (invite.status === "accepted" && data.action === "accept") return { match: this.summary(this.match(invite.match_id, actor), actor) };
      require(invite.status === "pending", "INVITE_CLOSED", 409);
      if (data.action === "accept") {
        for (const player of [invite.sender, invite.recipient]) {
          require(this.one("SELECT COUNT(*) count FROM matches m JOIN seats s ON s.match_id=m.id WHERE s.player_id=? AND m.status IN ('active','waiting')", player).count < 20, "TOO_MANY_MATCHES", 409);
        }
      }
      const match = data.action === "accept" ? this.create(invite.sender, invite.recipient) : null;
      this.sql.exec("UPDATE invitations SET status=?,match_id=? WHERE id=?", match ? "accepted" : "declined", match?.id ?? null, invite.id);
      if (match) this.notify(invite.sender, "accepted:" + invite.id, "Your match invitation was accepted. Your game is ready.", "/?match=" + match.id, input.origin);
      return match ? { match: this.summary(match, actor) } : { saved: true };
    }
    throw new Fault("NOT_FOUND", 404);
  }

  async fetch(request) {
    if (request.method !== "POST") return new Response(null, { status: 405 });
    try {
      const input = await request.json();
      const result = this.ctx.storage.transactionSync(() => this.operate(new URL(request.url).pathname.slice(1), input));
      this.ctx.waitUntil(this.scheduleNotifications());
      return Response.json({ ok: true, ...result });
    } catch (error) {
      if (error instanceof Fault) return Response.json({ ok: false, error: error.message }, { status: error.status });
      return Response.json({ ok: false, error: "STORAGE_UNAVAILABLE" }, { status: 503 });
    }
  }
}
