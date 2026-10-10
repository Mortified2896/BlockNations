// Standards-based Web Push (RFC 8291 aes128gcm and RFC 8292 VAPID), using
// Workers' WebCrypto. Push credentials never enter the game or browser bundle.
const encoder = new TextEncoder();
export function decode(value) {
  return Uint8Array.from(atob(value.replaceAll("-", "+").replaceAll("_", "/")), char => char.charCodeAt(0));
}
export function encode(bytes) {
  return btoa(String.fromCharCode(...new Uint8Array(bytes))).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}
function concat(...parts) {
  const result = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
  let offset = 0;
  for (const part of parts) { result.set(part, offset); offset += part.length; }
  return result;
}
async function hmac(key, data) {
  const imported = await crypto.subtle.importKey("raw", key, { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return new Uint8Array(await crypto.subtle.sign("HMAC", imported, data));
}
async function expand(key, info, length) { return (await hmac(key, concat(info, new Uint8Array([1])))).slice(0, length); }

export function validSubscription(value) {
  try {
    const url = new URL(value?.endpoint);
    const host = url.hostname;
    const provider = host === "fcm.googleapis.com" || host === "updates.push.services.mozilla.com" ||
      host.endsWith(".push.apple.com") || host.endsWith(".notify.windows.com");
    return url.protocol === "https:" && !url.username && !url.password && !url.port && !url.hash &&
      provider && value.endpoint.length <= 2048 && /^[\w-]{87}$/.test(value.keys?.p256dh ?? "") &&
      /^[\w-]{22}$/.test(value.keys?.auth ?? "") && decode(value.keys.p256dh)[0] === 4;
  } catch { return false; }
}

export async function pushRequest(subscription, payload, credentials, now = Date.now()) {
  if (!validSubscription(subscription)) throw new Error("Invalid push subscription");
  const receiver = decode(subscription.keys.p256dh), auth = decode(subscription.keys.auth);
  const ephemeral = await crypto.subtle.generateKey({ name: "ECDH", namedCurve: "P-256" }, true, ["deriveBits"]);
  const sender = new Uint8Array(await crypto.subtle.exportKey("raw", ephemeral.publicKey));
  const receiverKey = await crypto.subtle.importKey("raw", receiver, { name: "ECDH", namedCurve: "P-256" }, false, []);
  const shared = new Uint8Array(await crypto.subtle.deriveBits({ name: "ECDH", public: receiverKey }, ephemeral.privateKey, 256));
  const ikm = await expand(await hmac(auth, shared), concat(encoder.encode("WebPush: info\0"), receiver, sender), 32);
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const prk = await hmac(salt, ikm);
  const key = await expand(prk, encoder.encode("Content-Encoding: aes128gcm\0"), 16);
  const nonce = await expand(prk, encoder.encode("Content-Encoding: nonce\0"), 12);
  const plaintext = encoder.encode(JSON.stringify(payload));
  if (plaintext.length > 3000) throw new Error("Push payload too large");
  const aes = await crypto.subtle.importKey("raw", key, "AES-GCM", false, ["encrypt"]);
  const ciphertext = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv: nonce }, aes, concat(plaintext, new Uint8Array([2]))));
  const header = new Uint8Array(21);
  header.set(salt); new DataView(header.buffer).setUint32(16, 4096); header[20] = sender.length;
  const publicKey = decode(credentials.publicKey);
  const signingKey = await crypto.subtle.importKey("jwk", { kty: "EC", crv: "P-256",
    x: encode(publicKey.slice(1, 33)), y: encode(publicKey.slice(33)), d: credentials.privateKey,
  }, { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  const token = encode(encoder.encode(JSON.stringify({ typ: "JWT", alg: "ES256" }))) + "." +
    encode(encoder.encode(JSON.stringify({ aud: new URL(subscription.endpoint).origin, exp: Math.floor(now / 1000) + 43200, sub: credentials.subject })));
  const signature = await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, signingKey, encoder.encode(token));
  return new Request(subscription.endpoint, { method: "POST", redirect: "manual", headers: {
    "Authorization": "vapid t=" + token + "." + encode(signature) + ", k=" + credentials.publicKey,
    "Content-Encoding": "aes128gcm", "Content-Type": "application/octet-stream", "TTL": "86400", "Urgency": "normal",
  }, body: concat(header, sender, ciphertext) });
}

export async function sendPush(subscription, payload, env) {
  const request = await pushRequest(subscription, payload, {
    publicKey: env.VAPID_PUBLIC_KEY, privateKey: env.VAPID_PRIVATE_KEY,
    subject: "https://blocknations.moneymattersmedia.com",
  });
  const response = await (env.PUSH_TEST ?? { fetch }).fetch(new Request(request, { signal: AbortSignal.timeout(10000) }));
  return response.status;
}
