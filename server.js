const crypto = require("crypto");
const express = require("express");
const cors = require("cors");
const fs = require("fs");
const path = require("path");

const fsp = fs.promises;

const HOST = process.env.HOST || "0.0.0.0";
const PORT = Number.parseInt(process.env.PORT || "8080", 10);
const JSON_BODY_LIMIT = "2mb";
const JSON_BYTE_CAP = 2_000_000;
const SEQ_MAX_DIGITS = 12;
const GAME_ID_MAX_LEN = 128;
const RATE_LIMIT_WINDOW_MS = 60_000;
const RATE_LIMIT_GET_NEXT_MAX = 60;
const RATE_LIMIT_POST_TURN_MAX = 20;
const RATE_LIMIT_POST_STATUS_MAX = 30;
const RATE_LIMIT_POST_CLAIM_MAX = 20;
const RATE_LIMIT_CLEANUP_INTERVAL_MS = 5 * 60_000;
const STALE_TEMP_FILE_AGE_MS = 3_600_000;
const PBP_API_KEY_HEADER_NAME = "X-BlockNations-Api-Key";
const PBP_SHARED_SECRET_ENV_KEY = "PBP_SHARED_SECRET";
const STATUS_BATCH_MAX = 50;
const PLAYER_ID_MAX_LEN = 128;
const CLAIMS_FILE_NAME = "claims.json";

const DATA_ROOT = path.resolve(__dirname, "data", "PlayByPost", "Turns");
const rateLimitBuckets = new Map();
const claimLocksByGameHash = new Map();

if (!Number.isInteger(PORT) || PORT <= 0 || PORT >= 65536) {
  console.error(
    `[pbp] invalid PORT: env=${process.env.PORT ?? "<unset>"} parsed=${PORT}`
  );
  process.exit(1);
}

const app = express();
app.use((req, res, next) => {
  console.log(`[req] ${req.method} ${req.url}`);
  next();
});
app.use(cors());
app.use(express.json({ limit: JSON_BODY_LIMIT, strict: true }));

function logStartup() {
  const hasAuthSecret =
    typeof process.env[PBP_SHARED_SECRET_ENV_KEY] === "string" &&
    process.env[PBP_SHARED_SECRET_ENV_KEY].length > 0;
  if (!hasAuthSecret) {
    console.warn(
      `[pbp] WARNING: ${PBP_SHARED_SECRET_ENV_KEY} is not set. Protected PBp routes will return 401 until configured.`
    );
  }
  console.log(
    `[pbp] dataRoot=${DATA_ROOT} host=${HOST} port=${PORT} jsonBodyLimit=${JSON_BODY_LIMIT} jsonByteCap=${JSON_BYTE_CAP}`
  );
}

function sha256Hex(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}

function isValidGameId(gameId) {
  if (typeof gameId !== "string") {
    return false;
  }

  const trimmed = gameId.trim();
  if (gameId !== trimmed) {
    return false;
  }

  return trimmed.length > 0 && trimmed.length <= GAME_ID_MAX_LEN;
}

function isValidSeq(seq) {
  return (
    typeof seq === "number" &&
    Number.isSafeInteger(seq) &&
    seq > 0 &&
    String(seq).length <= SEQ_MAX_DIGITS
  );
}

function isValidTurnJson(json) {
  return typeof json === "string" && json.trim().length > 0;
}

function isValidPlayerId(playerId) {
  if (typeof playerId !== "string") {
    return false;
  }

  const trimmed = playerId.trim();
  return trimmed.length > 0 && trimmed.length <= PLAYER_ID_MAX_LEN;
}

function getConfiguredPbpSharedSecret() {
  const raw = process.env[PBP_SHARED_SECRET_ENV_KEY];
  if (typeof raw !== "string" || raw.length === 0) {
    return null;
  }
  return raw;
}

function timingSafeEqualUtf8(a, b) {
  const aBytes = Buffer.from(a, "utf8");
  const bBytes = Buffer.from(b, "utf8");
  if (aBytes.length !== bBytes.length) {
    return false;
  }
  return crypto.timingSafeEqual(aBytes, bBytes);
}

function sendUnauthorized(res) {
  return res.status(401).json({ ok: false, error: "UNAUTHORIZED" });
}

function logTurnStage(stage, details) {
  const suffix = details ? ` ${details}` : "";
  console.log(`[pbp-turn] ${new Date().toISOString()} ${stage}${suffix}`);
}

function requirePbpApiKey(req, res, next) {
  const shouldLogTurnAuth = req.path === "/pbp/turn";
  const expectedSecret = getConfiguredPbpSharedSecret();
  if (!expectedSecret) {
    if (shouldLogTurnAuth) {
      logTurnStage("auth_deny", "reason=missing_server_secret");
    }
    return sendUnauthorized(res);
  }

  const providedHeaderValue = req.get(PBP_API_KEY_HEADER_NAME);
  if (typeof providedHeaderValue !== "string" || providedHeaderValue.length === 0) {
    if (shouldLogTurnAuth) {
      logTurnStage("auth_deny", "reason=missing_header");
    }
    return sendUnauthorized(res);
  }

  if (!timingSafeEqualUtf8(providedHeaderValue, expectedSecret)) {
    if (shouldLogTurnAuth) {
      logTurnStage("auth_deny", "reason=secret_mismatch");
    }
    return sendUnauthorized(res);
  }

  if (shouldLogTurnAuth) {
    logTurnStage("auth_allow");
  }
  next();
}

function parseAfter(value) {
  if (value === undefined) {
    return { ok: true, value: -1 };
  }

  if (typeof value !== "string") {
    return { ok: false };
  }

  if (!/^-?\d+$/.test(value)) {
    return { ok: false };
  }

  if (value !== "-1" && value.length > SEQ_MAX_DIGITS) {
    return { ok: false };
  }

  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < -1) {
    return { ok: false };
  }

  if (parsed >= 0 && String(parsed).length > SEQ_MAX_DIGITS) {
    return { ok: false };
  }

  return { ok: true, value: parsed };
}

function sendInvalidInput(res) {
  return res.status(400).json({ ok: false, error: "INVALID_INPUT" });
}

function getRateLimitClientIp(req) {
  if (typeof req.ip === "string" && req.ip.length > 0) {
    return req.ip;
  }
  if (typeof req.socket?.remoteAddress === "string" && req.socket.remoteAddress.length > 0) {
    return req.socket.remoteAddress;
  }
  return "unknown";
}

function cleanupRateLimitBuckets(now = Date.now()) {
  for (const [key, entry] of rateLimitBuckets) {
    if (!entry || entry.resetAt <= now) {
      rateLimitBuckets.delete(key);
    }
  }
}

function checkRateLimit(req, routeKey, maxRequests, windowMs) {
  const now = Date.now();
  const bucketKey = `${routeKey}|${getRateLimitClientIp(req)}`;
  const existing = rateLimitBuckets.get(bucketKey);

  if (!existing || existing.resetAt <= now) {
    rateLimitBuckets.set(bucketKey, { count: 1, resetAt: now + windowMs });
    return null;
  }

  if (existing.count >= maxRequests) {
    return Math.max(1, Math.ceil((existing.resetAt - now) / 1000));
  }

  existing.count += 1;
  return null;
}

function enforceRateLimit(req, res, routeKey, maxRequests) {
  const retryAfterSeconds = checkRateLimit(req, routeKey, maxRequests, RATE_LIMIT_WINDOW_MS);
  if (retryAfterSeconds === null) {
    return false;
  }
  res.set("Retry-After", String(retryAfterSeconds));
  res.status(429).json({ ok: false, error: "RATE_LIMITED" });
  return true;
}

function validateSubmitInput(body) {
  if (!body || typeof body !== "object" || Array.isArray(body)) {
    return { ok: false, reason: "body_not_object" };
  }

  const { gameId, seq, json } = body;
  if (!isValidGameId(gameId)) {
    return { ok: false, reason: "invalid_game_id" };
  }
  if (!isValidSeq(seq)) {
    return { ok: false, reason: "invalid_seq" };
  }
  if (!isValidTurnJson(json)) {
    return { ok: false, reason: "invalid_json_payload" };
  }

  return { ok: true, value: { gameId, seq, json } };
}

function validateFetchInput(query) {
  if (!query || typeof query !== "object" || Array.isArray(query)) {
    return { ok: false };
  }

  const gameId = query.gameId;
  const after = query.after;
  if (Array.isArray(gameId) || Array.isArray(after)) {
    return { ok: false };
  }

  const afterParsed = parseAfter(after);
  if (!isValidGameId(gameId) || !afterParsed.ok) {
    return { ok: false };
  }

  return { ok: true, value: { gameId, after: afterParsed.value } };
}

function isValidKnownSeq(knownSeq) {
  if (typeof knownSeq !== "number" || !Number.isSafeInteger(knownSeq) || knownSeq < -1) {
    return false;
  }

  if (knownSeq >= 0 && String(knownSeq).length > SEQ_MAX_DIGITS) {
    return false;
  }

  return true;
}

function validateStatusInput(body) {
  if (!body || typeof body !== "object" || Array.isArray(body)) {
    return { ok: false };
  }

  const games = body.games;
  if (!Array.isArray(games) || games.length === 0 || games.length > STATUS_BATCH_MAX) {
    return { ok: false };
  }

  const normalized = [];
  for (const game of games) {
    if (!game || typeof game !== "object" || Array.isArray(game)) {
      return { ok: false };
    }

    const { gameId, knownSeq } = game;
    if (!isValidGameId(gameId) || !isValidKnownSeq(knownSeq)) {
      return { ok: false };
    }

    normalized.push({ gameId, knownSeq });
  }

  return { ok: true, value: { games: normalized } };
}

function validateClaimInput(body) {
  if (!body || typeof body !== "object" || Array.isArray(body)) {
    return { ok: false };
  }

  const { gameId, playerId, typedDisplayName } = body;
  if (!isValidGameId(gameId) || !isValidPlayerId(playerId)) {
    return { ok: false };
  }

  if (typedDisplayName !== undefined && typeof typedDisplayName !== "string") {
    return { ok: false };
  }

  return {
    ok: true,
    value: {
      gameId,
      playerId: playerId.trim(),
      typedDisplayName: typeof typedDisplayName === "string" ? typedDisplayName.trim() : ""
    }
  };
}

function logDecision(gameHash, seq, bytes, decision, extra) {
  const msg = `[pbp] ${new Date().toISOString()} ${decision} game=${gameHash} seq=${seq} bytes=${bytes}${
    extra ? ` ${extra}` : ""
  }`;
  console.log(msg);
}

async function fileExists(filePath) {
  try {
    await fsp.access(filePath, fs.constants.F_OK);
    return true;
  } catch (err) {
    if (err && err.code === "ENOENT") {
      return false;
    }
    throw err;
  }
}

async function safeUnlink(filePath) {
  try {
    await fsp.unlink(filePath);
  } catch {
    // best-effort
  }
}

async function cleanupStaleTempFiles(gameDir) {
  const tempFileRegex = /^turn_\d{1,12}\.json\.tmp\.\d+\.[0-9a-f]{12}$/;

  let entries;
  try {
    entries = await fsp.readdir(gameDir, { withFileTypes: true });
  } catch (err) {
    if (err && err.code === "ENOENT") {
      return;
    }
    console.warn(`[pbp] temp cleanup readdir failed dir=${gameDir} code=${err?.code || "UNKNOWN"}`);
    return;
  }

  const now = Date.now();
  for (const entry of entries) {
    if (!entry.isFile() || !tempFileRegex.test(entry.name)) {
      continue;
    }

    const tempPath = path.join(gameDir, entry.name);
    try {
      const stat = await fsp.stat(tempPath);
      if (!stat.isFile()) {
        continue;
      }
      if (now - stat.mtimeMs <= STALE_TEMP_FILE_AGE_MS) {
        continue;
      }

      await safeUnlink(tempPath);
    } catch (err) {
      if (err && err.code === "ENOENT") {
        continue;
      }
      console.warn(
        `[pbp] temp cleanup failed dir=${gameDir} file=${entry.name} code=${err?.code || "UNKNOWN"}`
      );
    }
  }
}

async function shouldTreatRenameAsRace(err, destFile) {
  if (!err) return false;
  if (err.code === "EEXIST" || err.code === "ENOTEMPTY") return true;
  if (err.code === "EPERM") {
    return await fileExists(destFile);
  }
  return false;
}

function normalizeSeatCount(seatCount) {
  if (!Number.isSafeInteger(seatCount) || seatCount < 2) {
    return 2;
  }

  return Math.min(seatCount, 4);
}

function normalizeCurrentTurnSeatIndex(snapshot) {
  if (!snapshot || typeof snapshot !== "object") {
    return 0;
  }

  const seatCount = normalizeSeatCount(snapshot.seatCount);
  if (Number.isSafeInteger(snapshot.currentTurnSeatIndex)) {
    return Math.min(Math.max(snapshot.currentTurnSeatIndex, 0), seatCount - 1);
  }

  return snapshot.isPlayerTurn ? 0 : 1;
}

function normalizeSeatState(state) {
  switch (state) {
    case "Active":
    case "Eliminated":
    case "Resigned":
      return state;
    default:
      return "Unclaimed";
  }
}

function buildSeatMetadataFromSnapshot(snapshot) {
  const seatCount = normalizeSeatCount(snapshot?.seatCount);
  const seats = Array.from({ length: seatCount }, (_, seatIndex) => ({
    seatIndex,
    state: "Unclaimed",
    claimedPlayerId: "",
    typedDisplayName: ""
  }));

  if (Array.isArray(snapshot?.seats)) {
    for (const rawSeat of snapshot.seats) {
      if (!rawSeat || !Number.isSafeInteger(rawSeat.seatIndex)) {
        continue;
      }

      const seatIndex = rawSeat.seatIndex;
      if (seatIndex < 0 || seatIndex >= seatCount) {
        continue;
      }

      seats[seatIndex] = {
        seatIndex,
        state: normalizeSeatState(rawSeat.state),
        claimedPlayerId: typeof rawSeat.claimedPlayerId === "string" ? rawSeat.claimedPlayerId.trim() : "",
        typedDisplayName: typeof rawSeat.typedDisplayName === "string" ? rawSeat.typedDisplayName.trim() : ""
      };
    }
  }

  if (seats[0] && typeof snapshot?.playerOneTypedDisplayName === "string" && !seats[0].typedDisplayName) {
    seats[0].typedDisplayName = snapshot.playerOneTypedDisplayName.trim();
  }

  if (seats[1] && typeof snapshot?.playerTwoTypedDisplayName === "string" && !seats[1].typedDisplayName) {
    seats[1].typedDisplayName = snapshot.playerTwoTypedDisplayName.trim();
  }

  return { seatCount, seats };
}

async function readTurnSeqs(gameDir) {
  let entries;
  try {
    entries = await fsp.readdir(gameDir, { withFileTypes: true });
  } catch (err) {
    if (err && err.code === "ENOENT") {
      return [];
    }

    throw err;
  }

  const fileRegex = /^turn_(\d+)\.json$/;
  const seqs = [];
  for (const entry of entries) {
    if (!entry.isFile()) continue;

    const match = fileRegex.exec(entry.name);
    if (!match) continue;

    const digits = match[1];
    if (digits.length > SEQ_MAX_DIGITS) continue;

    const seq = Number(digits);
    if (!Number.isSafeInteger(seq) || seq <= 0) continue;

    seqs.push(seq);
  }

  seqs.sort((a, b) => a - b);
  return seqs;
}

async function readLatestTurnSnapshot(gameId) {
  const gameHash = sha256Hex(gameId);
  const gameDir = path.join(DATA_ROOT, gameHash);
  const seqs = await readTurnSeqs(gameDir);
  if (seqs.length <= 0) {
    return { gameHash, gameDir, latestSeq: 0, snapshot: null };
  }

  const latestSeq = seqs[seqs.length - 1];
  const latestPath = path.join(gameDir, `turn_${latestSeq}.json`);
  const json = await fsp.readFile(latestPath, "utf8");
  let snapshot;
  try {
    snapshot = JSON.parse(json);
  } catch (err) {
    console.error(`[pbp] parse latest snapshot failed game=${gameHash} seq=${latestSeq}`, err);
    throw err;
  }

  return { gameHash, gameDir, latestSeq, snapshot };
}

function getClaimsPath(gameDir) {
  return path.join(gameDir, CLAIMS_FILE_NAME);
}

async function readClaims(gameDir) {
  const claimsPath = getClaimsPath(gameDir);
  try {
    const json = await fsp.readFile(claimsPath, "utf8");
    const parsed = JSON.parse(json);
    if (!parsed || !Array.isArray(parsed.claims)) {
      return [];
    }

    return parsed.claims
      .filter((claim) => claim && Number.isSafeInteger(claim.seatIndex) && isValidPlayerId(claim.playerId))
      .map((claim) => ({
        seatIndex: claim.seatIndex,
        playerId: claim.playerId.trim(),
        typedDisplayName: typeof claim.typedDisplayName === "string" ? claim.typedDisplayName.trim() : ""
      }));
  } catch (err) {
    if (err && err.code === "ENOENT") {
      return [];
    }

    throw err;
  }
}

async function writeClaims(gameDir, claims) {
  await fsp.mkdir(gameDir, { recursive: true });
  await fsp.writeFile(
    getClaimsPath(gameDir),
    JSON.stringify({ version: 1, claims }, null, 2),
    "utf8"
  );
}

async function withClaimLock(gameHash, operation) {
  const previous = claimLocksByGameHash.get(gameHash) || Promise.resolve();
  let release;
  const current = new Promise((resolve) => {
    release = resolve;
  });
  claimLocksByGameHash.set(gameHash, current);

  await previous;
  try {
    return await operation();
  } finally {
    if (claimLocksByGameHash.get(gameHash) === current) {
      claimLocksByGameHash.delete(gameHash);
    }
    release();
  }
}

app.post("/pbp/turn", requirePbpApiKey, async (req, res) => {
  logTurnStage("request_accepted");

  if (enforceRateLimit(req, res, "POST /pbp/turn", RATE_LIMIT_POST_TURN_MAX)) {
    logTurnStage("rate_limited", `route=POST_/pbp/turn`);
    return;
  }

  const validated = validateSubmitInput(req.body);
  if (!validated.ok) {
    logTurnStage("invalid_input", `reason=${validated.reason || "unknown"}`);
    return sendInvalidInput(res);
  }
  const { gameId, seq, json } = validated.value;

  const byteLength = Buffer.byteLength(json, "utf8");
  const gameHash = sha256Hex(gameId);
  logTurnStage("validated", `game=${gameHash} seq=${seq} bytes=${byteLength}`);

  if (byteLength > JSON_BYTE_CAP) {
    logTurnStage("body_too_large", `game=${gameHash} seq=${seq} bytes=${byteLength} cap=${JSON_BYTE_CAP}`);
    return sendInvalidInput(res);
  }

  const gameDir = path.join(DATA_ROOT, gameHash);
  const destFile = path.join(gameDir, `turn_${seq}.json`);
  const incomingBytes = Buffer.from(json, "utf8");

  try {
    if (await fileExists(destFile)) {
      const existingBytes = await fsp.readFile(destFile);
      if (existingBytes.equals(incomingBytes)) {
        logDecision(gameHash, seq, byteLength, "duplicate");
        return res.status(200).json({ ok: true, alreadyHad: true });
      }

      logDecision(gameHash, seq, byteLength, "conflict");
      return res.status(409).json({ ok: false, error: "SEQ_CONFLICT" });
    }
  } catch (err) {
    console.error(`[pbp] read/check failed game=${gameHash} seq=${seq} code=${err.code || "UNKNOWN"}`, err);
    return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
  }

  try {
    await fsp.mkdir(gameDir, { recursive: true });
  } catch (err) {
    if (!(err && err.code === "EEXIST")) {
      console.error(`[pbp] mkdir failed game=${gameHash} seq=${seq} code=${err.code || "UNKNOWN"}`, err);
      return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
    }
  }

  await cleanupStaleTempFiles(gameDir);

  const tmpPath = path.join(
    gameDir,
    `turn_${seq}.json.tmp.${process.pid}.${crypto.randomBytes(6).toString("hex")}`
  );

  try {
    logTurnStage("write_start", `game=${gameHash} seq=${seq} bytes=${byteLength}`);
    await fsp.writeFile(tmpPath, incomingBytes, { flag: "wx" });

    try {
      await fsp.rename(tmpPath, destFile);
      logDecision(gameHash, seq, byteLength, "stored");
      return res.status(200).json({ ok: true });
    } catch (err) {
      const race = await shouldTreatRenameAsRace(err, destFile);
      if (race) {
        try {
          const existingBytes = await fsp.readFile(destFile);
          if (existingBytes.equals(incomingBytes)) {
            logDecision(gameHash, seq, byteLength, "duplicate");
            await safeUnlink(tmpPath);
            return res.status(200).json({ ok: true, alreadyHad: true });
          }

          logDecision(gameHash, seq, byteLength, "conflict");
          await safeUnlink(tmpPath);
          return res.status(409).json({ ok: false, error: "SEQ_CONFLICT" });
        } catch (readErr) {
          console.error(
            `[pbp] race read failed game=${gameHash} seq=${seq} code=${readErr.code || "UNKNOWN"}`,
            readErr
          );
          await safeUnlink(tmpPath);
          return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
        }
      }

      console.error(`[pbp] rename failed game=${gameHash} seq=${seq} code=${err.code || "UNKNOWN"}`, err);
      await safeUnlink(tmpPath);
      return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
    }
  } catch (err) {
    console.error(`[pbp] write failed game=${gameHash} seq=${seq} code=${err.code || "UNKNOWN"}`, err);
    await safeUnlink(tmpPath);
    return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
  }
});

app.get("/pbp/turn/next", requirePbpApiKey, async (req, res) => {
  if (enforceRateLimit(req, res, "GET /pbp/turn/next", RATE_LIMIT_GET_NEXT_MAX)) {
    return;
  }

  const validated = validateFetchInput(req.query);
  if (!validated.ok) {
    return sendInvalidInput(res);
  }
  const { gameId, after } = validated.value;
  const gameHash = sha256Hex(gameId);
  const gameDir = path.join(DATA_ROOT, gameHash);

  let entries;
  try {
    entries = await fsp.readdir(gameDir, { withFileTypes: true });
  } catch (err) {
    if (err && err.code === "ENOENT") {
      return res.set("Content-Type", "text/plain").status(200).end("NO_TURN");
    }
    console.error(`[pbp] readdir failed game=${gameHash} code=${err.code || "UNKNOWN"}`, err);
    return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
  }

  const fileRegex = /^turn_(\d+)\.json$/;
  let bestSeq = null;
  let bestName = null;

  for (const entry of entries) {
    if (!entry.isFile()) continue;
    const match = fileRegex.exec(entry.name);
    if (!match) continue;

    const digits = match[1];
    if (digits.length > SEQ_MAX_DIGITS) continue;

    const seq = Number(digits);
    if (!Number.isSafeInteger(seq)) continue;
    if (seq <= after) continue;

    if (bestSeq === null || seq < bestSeq) {
      bestSeq = seq;
      bestName = entry.name;
    }
  }

  if (bestSeq === null || !bestName) {
    return res.set("Content-Type", "text/plain").status(200).end("NO_TURN");
  }

  const bestPath = path.join(gameDir, bestName);
  try {
    const bytes = await fsp.readFile(bestPath);
    if (!bytes || bytes.length === 0) {
      console.error(`[pbp] empty turn file game=${gameHash} seq=${bestSeq}`);
      return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
    }
    const json = bytes.toString("utf8");
    return res.status(200).json({ seq: bestSeq, json });
  } catch (err) {
    console.error(`[pbp] read turn failed game=${gameHash} seq=${bestSeq} code=${err.code || "UNKNOWN"}`, err);
    return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
  }
});

app.post("/pbp/game/claim", requirePbpApiKey, async (req, res) => {
  if (enforceRateLimit(req, res, "POST /pbp/game/claim", RATE_LIMIT_POST_CLAIM_MAX)) {
    return;
  }

  const validated = validateClaimInput(req.body);
  if (!validated.ok) {
    return sendInvalidInput(res);
  }

  const { gameId, playerId, typedDisplayName } = validated.value;
  const gameHash = sha256Hex(gameId);

  try {
    const result = await withClaimLock(gameHash, async () => {
      const latest = await readLatestTurnSnapshot(gameId);
      if (!latest.snapshot) {
        return { ok: false, error: "NO_TURN" };
      }

      const { seatCount, seats } = buildSeatMetadataFromSnapshot(latest.snapshot);
      const claims = await readClaims(latest.gameDir);

      for (const seat of seats) {
        if (isValidPlayerId(seat.claimedPlayerId) &&
            !claims.some((claim) => claim.playerId === seat.claimedPlayerId)) {
          claims.push({
            seatIndex: seat.seatIndex,
            playerId: seat.claimedPlayerId,
            typedDisplayName: seat.typedDisplayName || ""
          });
        }
      }

      const existing = claims.find((claim) => claim.playerId === playerId);
      if (existing) {
        await writeClaims(latest.gameDir, claims);
        return { ok: true, seatIndex: existing.seatIndex, alreadyClaimed: true };
      }

      const occupiedSeats = new Set(claims.map((claim) => claim.seatIndex));
      let openSeatIndex = -1;
      for (let seatIndex = 0; seatIndex < seatCount; seatIndex++) {
        if (occupiedSeats.has(seatIndex)) {
          continue;
        }

        if (normalizeSeatState(seats[seatIndex]?.state) !== "Unclaimed") {
          continue;
        }

        openSeatIndex = seatIndex;
        break;
      }

      if (openSeatIndex < 0) {
        return { ok: false, error: "GAME_FULL" };
      }

      claims.push({
        seatIndex: openSeatIndex,
        playerId,
        typedDisplayName
      });
      await writeClaims(latest.gameDir, claims);
      return { ok: true, seatIndex: openSeatIndex, alreadyClaimed: false };
    });

    if (!result.ok) {
      return res.status(409).json({ ok: false, error: result.error });
    }

    return res.status(200).json(result);
  } catch (err) {
    console.error(`[pbp] claim failed game=${gameHash} code=${err?.code || "UNKNOWN"}`, err);
    return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
  }
});

app.post("/pbp/turn/status", requirePbpApiKey, async (req, res) => {
  if (enforceRateLimit(req, res, "POST /pbp/turn/status", RATE_LIMIT_POST_STATUS_MAX)) {
    return;
  }

  const validated = validateStatusInput(req.body);
  if (!validated.ok) {
    return sendInvalidInput(res);
  }

  const requestedGames = validated.value.games;
  const seqsByGameId = new Map();
  const turnSeatByGameId = new Map();

  try {
    const uniqueGameIds = new Set(requestedGames.map((g) => g.gameId));

    for (const gameId of uniqueGameIds) {
      const latest = await readLatestTurnSnapshot(gameId);
      const seqs = latest.latestSeq > 0 ? await readTurnSeqs(latest.gameDir) : [];
      seqsByGameId.set(gameId, seqs);
      turnSeatByGameId.set(
        gameId,
        latest.snapshot ? normalizeCurrentTurnSeatIndex(latest.snapshot) : -1
      );
    }
  } catch (err) {
    console.error(`[pbp] status scan failed code=${err?.code || "UNKNOWN"}`, err);
    return res.status(500).json({ ok: false, error: "SERVER_ERROR" });
  }

  const games = requestedGames.map(({ gameId, knownSeq }) => {
    const seqs = seqsByGameId.get(gameId) || [];
    const hasAnyTurn = seqs.length > 0;
    const latestSeq = hasAnyTurn ? seqs[seqs.length - 1] : 0;

    let nextSeqAfterKnown = 0;
    for (const seq of seqs) {
      if (seq > knownSeq) {
        nextSeqAfterKnown = seq;
        break;
      }
    }

    return {
      gameId,
      knownSeq,
      hasAnyTurn,
      latestSeq,
      nextSeqAfterKnown,
      hasNewerThanKnown: nextSeqAfterKnown > 0,
      turnSeat: hasAnyTurn ? (turnSeatByGameId.get(gameId) ?? -1) : -1
    };
  });

  return res.status(200).json({ ok: true, games });
});

app.get("/healthz", (req, res) => {
  res.status(200).json({ ok: true });
});

app.get("/health", (req, res) => {
  res.status(200).send("ok");
});

app.use((err, req, res, _next) => {
  if (req && req.path === "/pbp/turn") {
    if (err && err.type === "entity.too.large") {
      logTurnStage("body_parse_error", `reason=entity_too_large limit=${JSON_BODY_LIMIT}`);
    } else if (err instanceof SyntaxError) {
      logTurnStage("body_parse_error", "reason=invalid_json");
    } else if (err && err.status === 400) {
      logTurnStage("body_parse_error", `reason=http_400 type=${err.type || "unknown"}`);
    }
  }

  if (err && (err.type === "entity.too.large" || err instanceof SyntaxError)) {
    return res.status(400).json({ ok: false, error: "INVALID_INPUT" });
  }
  if (err && err.status === 400) {
    return res.status(400).json({ ok: false, error: "INVALID_INPUT" });
  }
  console.error("[pbp] unhandled error", err);
  res.status(500).json({ ok: false, error: "SERVER_ERROR" });
});

const server = app.listen(PORT, HOST, () => {
  logStartup();
  console.log(`[pbp] listening on ${HOST}:${PORT}`);
});

const rateLimitCleanupTimer = setInterval(() => {
  cleanupRateLimitBuckets();
}, RATE_LIMIT_CLEANUP_INTERVAL_MS);
if (typeof rateLimitCleanupTimer.unref === "function") {
  rateLimitCleanupTimer.unref();
}

server.on("error", (err) => {
  if (err && err.code === "EADDRINUSE") {
    console.error(`[pbp] port in use: ${HOST}:${PORT}`);
  } else {
    console.error("[pbp] server error", err);
  }
  process.exit(1);
});
