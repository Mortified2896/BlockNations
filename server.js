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

const DATA_ROOT = path.resolve(__dirname, "data", "PlayByPost", "Turns");

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

function validateSubmitInput(body) {
  if (!body || typeof body !== "object" || Array.isArray(body)) {
    return { ok: false };
  }

  const { gameId, seq, json } = body;
  if (!isValidGameId(gameId) || !isValidSeq(seq) || !isValidTurnJson(json)) {
    return { ok: false };
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

async function shouldTreatRenameAsRace(err, destFile) {
  if (!err) return false;
  if (err.code === "EEXIST" || err.code === "ENOTEMPTY") return true;
  if (err.code === "EPERM") {
    return await fileExists(destFile);
  }
  return false;
}

app.post("/pbp/turn", async (req, res) => {
  const validated = validateSubmitInput(req.body);
  if (!validated.ok) {
    return sendInvalidInput(res);
  }
  const { gameId, seq, json } = validated.value;

  const byteLength = Buffer.byteLength(json, "utf8");
  if (byteLength > JSON_BYTE_CAP) {
    return sendInvalidInput(res);
  }

  const gameHash = sha256Hex(gameId);
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

  const tmpPath = path.join(
    gameDir,
    `turn_${seq}.json.tmp.${process.pid}.${crypto.randomBytes(6).toString("hex")}`
  );

  try {
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

app.get("/pbp/turn/next", async (req, res) => {
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

app.get("/healthz", (req, res) => {
  res.status(200).json({ ok: true });
});

app.get("/health", (req, res) => {
  res.status(200).send("ok");
});

app.use((err, req, res, next) => {
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

server.on("error", (err) => {
  if (err && err.code === "EADDRINUSE") {
    console.error(`[pbp] port in use: ${HOST}:${PORT}`);
  } else {
    console.error("[pbp] server error", err);
  }
  process.exit(1);
});
