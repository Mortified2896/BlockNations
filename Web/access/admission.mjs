import { page, escape } from "./pages.mjs";
import { storeCall } from "./store.mjs";

export async function adminPage(request, env) {
  const parameter = new URL(request.url).searchParams.get("page") ?? "0";
  if (!/^\d{1,4}$/.test(parameter)) return new Response("Invalid page.", { status: 400 });
  const current = Number(parameter);
  const rows = await storeCall(env, "list", { offset: current * 50 });
  const testers = rows.slice(0, 50);
  const cards = testers.map(tester => {
    const reviewApproved = tester.review_status === "approved";
    const denied = ["rejected", "disabled"].includes(tester.status);
    const label = denied ? tester.status : reviewApproved ? "Approved reviewer · automatic game access" : tester.status === "approved" ? "Approved for the game only" : "Waiting for game approval";
    const decisions = (denied ? [["pending", "Restore to pending"], ["approved", "Approve game access"]]
      : tester.status === "pending" && !reviewApproved ? [["approved", "Approve game access"], ["rejected", "Reject request"]]
      : [["disabled", "Disable game access"]]).map(([status, label]) =>
      `<form method="post" action="/admin/testers/decision"><input type="hidden" name="auth_user_id" value="${escape(tester.auth_user_id)}"><input type="hidden" name="status" value="${status}"><button class="${status === "approved" ? "" : "secondary"}">${label}</button></form>`,
    ).join("");
    return `<article class="tester"><p><strong>${escape(tester.display_name)}</strong><br>${escape(tester.email)}</p><p class="note">${escape(label)}</p>${decisions}</article>`;
  }).join("");
  return page("Manage playtest access", `<p>Approving here unlocks only Block Nations. Article access still needs separate approval on the Review website.</p>
<p class="note">Approved reviewers can play automatically. You can disable their game access here independently.</p>
${cards || "<p>No playtest requests yet.</p>"}
${current > 0 ? `<a class="button secondary" href="/admin/testers?page=${current - 1}">Newer requests</a>` : ""}
${rows.length > 50 ? `<a class="button secondary" href="/admin/testers?page=${current + 1}">Older requests</a>` : ""}
<a class="button secondary" href="/access">Back to your account</a>`);
}

export async function decide(request, env, actor) {
  if (request.method !== "POST" || request.headers.get("Origin") !== env.GAME_ORIGIN ||
      !request.headers.get("Content-Type")?.startsWith("application/x-www-form-urlencoded")) {
    return new Response("Request origin rejected.", { status: 403 });
  }
  const reader = request.body?.getReader();
  if (!reader) return new Response("Missing decision.", { status: 400 });
  let size = 0; const chunks = [];
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > 2048) { await reader.cancel(); return new Response("Decision too large.", { status: 413 }); }
    chunks.push(value);
  }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  const form = new URLSearchParams(new TextDecoder().decode(bytes));
  const uid = form.get("auth_user_id"); const status = form.get("status");
  if (!uid || uid.length > 255 || !["pending", "approved", "rejected", "disabled"].includes(status)) return new Response("Invalid decision.", { status: 400 });
  const result = await storeCall(env, "decide", { status, actor: actor.auth_user_id, uid });
  if (!result.found) return new Response("Tester not found.", { status: 404 });
  return new Response(null, { status: 303, headers: { Location: "/admin/testers" } });
}
