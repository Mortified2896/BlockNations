import { test } from "node:test";
import assert from "node:assert/strict";
import { createECDH, randomBytes, createPublicKey, verify } from "node:crypto";
import ece from "http_ece";
import { pushRequest, validSubscription } from "./push.mjs";
import { MultiplayerStore, WEB_MATCH_VERSION } from "./store.mjs";
import { DatabaseSync } from "node:sqlite";
import { readFile } from "node:fs/promises";
import { Miniflare, convertV4MiniflareOptions } from "miniflare";

function fixture(endpoint = "https://fcm.googleapis.com/fcm/send/synthetic-test") {
  const receiver = createECDH("prime256v1"); receiver.generateKeys();
  const server = createECDH("prime256v1"); server.generateKeys();
  const auth = randomBytes(16);
  return { receiver, auth,
    subscription: { endpoint, keys: { p256dh: receiver.getPublicKey().toString("base64url"), auth: auth.toString("base64url") } },
    credentials: { publicKey: server.getPublicKey().toString("base64url"), privateKey: server.getPrivateKey().toString("base64url"), subject: "https://blocknations.moneymattersmedia.com" },
  };
}

test("the encrypted push payload decrypts with an independent RFC 8291 implementation", async () => {
  const data = fixture();
  const payload = { title: "Block Nations", body: "It's your turn.", url: "https://blocknations.moneymattersmedia.com/?match=test" };
  const request = await pushRequest(data.subscription, payload, data.credentials);
  assert.equal(request.headers.get("Content-Encoding"), "aes128gcm");
  const ciphertext = Buffer.from(await request.arrayBuffer());
  assert.ok(!ciphertext.includes(Buffer.from(payload.body)));
  const decrypted = ece.decrypt(ciphertext, { version: "aes128gcm", privateKey: data.receiver, authSecret: data.auth.toString("base64url") });
  assert.deepEqual(JSON.parse(decrypted.toString()), payload);
});
test("VAPID has a valid ES256 signature, correct audience, subject and bounded expiry", async () => {
  const data = fixture("https://web.push.apple.com/synthetic"), now = Date.now();
  const request = await pushRequest(data.subscription, { body: "test" }, data.credentials, now);
  const token = /^vapid t=([^,]+), k=(.+)$/.exec(request.headers.get("Authorization"));
  assert.equal(token[2], data.credentials.publicKey);
  const [header, body, signature] = token[1].split(".");
  assert.equal(JSON.parse(Buffer.from(header, "base64url").toString()).alg, "ES256");
  const claims = JSON.parse(Buffer.from(body, "base64url").toString());
  assert.equal(claims.aud, "https://web.push.apple.com"); assert.equal(claims.sub, data.credentials.subject);
  assert.equal(claims.exp, Math.floor(now / 1000) + 43200);
  const bytes = Buffer.from(data.credentials.publicKey, "base64url");
  const key = createPublicKey({ format: "jwk", key: { kty: "EC", crv: "P-256", x: bytes.subarray(1,33).toString("base64url"), y: bytes.subarray(33).toString("base64url") } });
  assert.equal(verify("sha256", Buffer.from(header + "." + body), { key, dsaEncoding: "ieee-p1363" }, Buffer.from(signature, "base64url")), true);
});
test("the Cloudflare runtime encrypts and signs push messages that independent libraries verify", async () => {
  const data = fixture(), payload = { body: "Cloudflare push acceptance" };
  const mf = new Miniflare(convertV4MiniflareOptions({
    modules: [
      { type: "ESModule", path: new URL("./push-runtime-fixture.mjs", import.meta.url).pathname,
        contents: `import { pushRequest, encode } from "./push.mjs";
        export default { async fetch(request,env) {
          const push = await pushRequest(JSON.parse(env.SUBSCRIPTION),JSON.parse(env.PAYLOAD),JSON.parse(env.CREDENTIALS));
          return Response.json({authorization:push.headers.get("Authorization"),body:encode(await push.arrayBuffer())});
        }};` },
      { type: "ESModule", path: new URL("./push.mjs", import.meta.url).pathname,
        contents: await readFile(new URL("./push.mjs", import.meta.url), "utf8") },
    ], compatibilityDate: "2026-10-08", bindings: {
      SUBSCRIPTION: JSON.stringify(data.subscription), PAYLOAD: JSON.stringify(payload), CREDENTIALS: JSON.stringify(data.credentials),
    },
  }));
  try {
    const response = await mf.dispatchFetch("https://synthetic.test/");
    assert.equal(response.status, 200);
    const result = await response.json();
    assert.deepEqual(JSON.parse(ece.decrypt(Buffer.from(result.body, "base64url"), {
      version: "aes128gcm", privateKey: data.receiver, authSecret: data.auth.toString("base64url"),
    }).toString()), payload);
    const token = /^vapid t=([^,]+), k=(.+)$/.exec(result.authorization)[1];
    const [header, body, signature] = token.split(".");
    const bytes = Buffer.from(data.credentials.publicKey, "base64url");
    const key = createPublicKey({ format: "jwk", key: { kty: "EC", crv: "P-256",
      x: bytes.subarray(1,33).toString("base64url"), y: bytes.subarray(33).toString("base64url") } });
    assert.equal(verify("sha256", Buffer.from(header+"."+body), { key, dsaEncoding: "ieee-p1363" }, Buffer.from(signature, "base64url")), true);
  } finally { await mf.dispose(); }
});
test("push subscriptions reject arbitrary servers, private addresses, credentials and invalid key shapes", async () => {
  const data = fixture();
  assert.equal(validSubscription(data.subscription), true);
  for (const endpoint of ["http://fcm.googleapis.com/test", "https://127.0.0.1/test", "https://example.com/test", "https://fcm.googleapis.com.evil.test/test", "https://user:password@fcm.googleapis.com/test", "https://web.push.apple.com:444/test"]) {
    const subscription = { ...data.subscription, endpoint };
    assert.equal(validSubscription(subscription), false);
    await assert.rejects(pushRequest(subscription, {}, data.credentials));
  }
  assert.equal(validSubscription({ ...data.subscription, keys: { p256dh: "invalid", auth: "invalid" } }), false);
});

// The notification integration uses real SQLite and the real store/alarm code;
// only the outbound push provider is replaced, so no device receives test data.
function storeFixture() {
  const data = fixture();
  const db = new DatabaseSync(":memory:");
  const deliveries = [], pending = [];
  let providerStatus = 201, alarm = null;
  const sql = { exec(query, ...values) {
    if (!values.length && query.includes("CREATE TABLE")) { db.exec(query); return { toArray: () => [] }; }
    const statement = db.prepare(query);
    const rows = statement.all(...values);
    return { toArray: () => rows };
  } };
  const ctx = { storage: { sql, transactionSync(fn) {
    db.exec("BEGIN"); try { const result=fn(); db.exec("COMMIT"); return result; } catch(error) { db.exec("ROLLBACK"); throw error; }
  }, async getAlarm() { return alarm; }, async setAlarm(time) { alarm=time; } }, waitUntil(promise) { pending.push(promise); } };
  const env = { VAPID_PUBLIC_KEY: data.credentials.publicKey, VAPID_PRIVATE_KEY: data.credentials.privateKey,
    PUSH_TEST: { async fetch(request) {
      const ciphertext = Buffer.from(await request.arrayBuffer());
      deliveries.push(JSON.parse(ece.decrypt(ciphertext, { version:"aes128gcm",privateKey:data.receiver,authSecret:data.auth.toString("base64url") }).toString()));
      return new Response(null, { status: providerStatus });
    } } };
  const store = new MultiplayerStore(ctx, env);
  const actor = { id: "a".repeat(32), name:"Synthetic A",email:"a@example.test",sessionId:"synthetic-session-"+"a".repeat(32) };
  const other = { id:"b".repeat(32),name:"Synthetic B",email:"b@example.test",sessionId:"synthetic-session-"+"b".repeat(32) };
  async function call(operation, who=actor, input={}) {
    const response=await store.fetch(new Request("https://internal/"+operation,{method:"POST",body:JSON.stringify({actor:who,data:input,origin:"https://blocknations.moneymattersmedia.com"})}));
    await Promise.all(pending.splice(0));
    return { status:response.status,...await response.json() };
  }
  return {data,store,db,deliveries,actor,other,call,setStatus(status){providerStatus=status},alarm:()=>alarm};
}
test("asynchronous open matches remain joinable when the host returns days later", async () => {
  const f = storeFixture();
  try {
    const first = await f.call("find", f.actor, { version: WEB_MATCH_VERSION });
    f.store.sql.exec("UPDATE matches SET created=?,updated=? WHERE id=?", Date.now()-3*86400000, Date.now()-3*86400000, first.match.id);
    const second = await f.call("find", f.other, { version: WEB_MATCH_VERSION });
    assert.equal(second.match.id, first.match.id);
    assert.equal(second.match.status, "active");
    assert.equal(second.match.seatIndex, 1);
  } finally { f.db.close(); }
});
test("opt-in subscriptions deliver queued social requests once and keep private data out of notifications", async () => {
  const f=storeFixture();
  try {
    await f.call("session",f.other);
    assert.equal((await f.call("push-subscribe",f.other,{subscription:f.data.subscription})).status,200);
    const first=await f.call("friend-request",f.actor,{playerId:f.other.id});
    const retry=await f.call("friend-request",f.actor,{playerId:f.other.id});
    assert.equal(first.friendshipId,retry.friendshipId); assert.ok(f.alarm());
    await f.store.alarm();
    assert.equal(f.deliveries.length,1); assert.match(f.deliveries[0].body,/friend request/);
    assert.doesNotMatch(JSON.stringify(f.deliveries),/@example|subscription|session/);
    assert.equal(f.db.prepare("SELECT COUNT(*) count FROM notifications").get().count,0);
  } finally { f.db.close(); }
});
test("logout revokes only that session's device; transferred subscriptions cannot receive the old account's queue", async () => {
  const f=storeFixture();
  try {
    await f.call("session",f.other);
    await f.call("push-subscribe",f.other,{subscription:f.data.subscription});
    await f.call("invite",f.actor,{playerId:f.other.id});
    await f.call("push-subscribe",f.actor,{subscription:f.data.subscription});
    await f.store.alarm(); assert.equal(f.deliveries.length,0);
    await f.store.fetch(new Request("https://internal/push-session-revoke",{method:"POST",body:JSON.stringify({sessionId:f.actor.sessionId})}));
    assert.equal(f.db.prepare("SELECT COUNT(*) count FROM subscriptions").get().count,0);
  } finally { f.db.close(); }
});
test("expired endpoints are removed; provider outages leave a bounded retry without affecting saved requests", async () => {
  const f=storeFixture();
  try {
    await f.call("session",f.other);
    await f.call("push-subscribe",f.other,{subscription:f.data.subscription});
    await f.call("invite",f.actor,{playerId:f.other.id});
    f.setStatus(503); await f.store.alarm();
    assert.equal(f.db.prepare("SELECT attempts FROM notifications").get().attempts,1);
    assert.equal((await f.call("lobby",f.other)).invites.length,1);
    f.db.exec("UPDATE notifications SET next_attempt=0");
    f.setStatus(410); await f.store.alarm();
    assert.equal(f.db.prepare("SELECT COUNT(*) count FROM subscriptions").get().count,0);
    assert.equal(f.db.prepare("SELECT COUNT(*) count FROM notifications").get().count,0);
  } finally { f.db.close(); }
});
