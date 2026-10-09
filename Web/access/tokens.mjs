// Host-only cookies and audience-bound, encrypted handoffs. No Google token or
// Review session cookie is copied into the browser response or the game build.
const encoder = new TextEncoder();
const context = encoder.encode("BlockNations access v1");

function encode(bytes) {
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function decode(value) {
  if (!/^[\w-]+$/.test(value)) throw new Error("Invalid encoding");
  return Uint8Array.from(atob(value.replace(/-/g, "+").replace(/_/g, "/")), c => c.charCodeAt(0));
}

async function key(secret) {
  if (typeof secret !== "string" || secret.length < 43) throw new Error("Missing access secret");
  const digest = await crypto.subtle.digest("SHA-256", encoder.encode(secret));
  return crypto.subtle.importKey("raw", digest, "AES-GCM", false, ["encrypt", "decrypt"]);
}

export function randomToken() {
  return encode(crypto.getRandomValues(new Uint8Array(32)));
}

export async function fingerprint(value) {
  return encode(new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(value))));
}

export async function seal(secret, claims) {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const bytes = await crypto.subtle.encrypt(
    { name: "AES-GCM", iv, additionalData: context },
    await key(secret), encoder.encode(JSON.stringify({ ...claims, v: 1 })),
  );
  return encode(iv) + "." + encode(new Uint8Array(bytes));
}

export async function open(secret, token, kind, audience, now = Date.now()) {
  try {
    if (typeof token !== "string" || token.length > 4096) return null;
    const parts = token.split(".");
    if (parts.length !== 2) return null;
    const iv = decode(parts[0]);
    if (iv.length !== 12) return null;
    const bytes = await crypto.subtle.decrypt(
      { name: "AES-GCM", iv, additionalData: context }, await key(secret), decode(parts[1]),
    );
    const claims = JSON.parse(new TextDecoder().decode(bytes));
    if (claims.v !== 1 || claims.kind !== kind || claims.aud !== audience ||
        !Number.isSafeInteger(claims.exp) || claims.exp <= now) return null;
    return claims;
  } catch {
    return null;
  }
}

export function cookie(request, name) {
  const matches = (request.headers.get("Cookie") ?? "").split(";")
    .map(part => part.trim()).filter(part => part.startsWith(name + "="));
  // Reject ambiguous cookies rather than trusting whichever a sibling supplied.
  return matches.length === 1 ? matches[0].slice(name.length + 1) : null;
}

export const SESSION_COOKIE = "__Host-blocknations_session";
export const ATTEMPT_COOKIE = "__Host-blocknations_attempt";

export function setCookie(name, value, maxAge) {
  return `${name}=${value}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${Math.max(0, Math.floor(maxAge))}`;
}
