import { page, escape } from "./pages.mjs";
import { storeCall } from "./store.mjs";

export const MAX_FEEDBACK_BYTES = 1500000;

export async function submitFeedback(request, env, actor) {
  if (request.method !== "POST") return Response.json({ error: "Method not allowed." }, { status: 405 });
  if (request.headers.get("Origin") !== env.GAME_ORIGIN || !request.headers.get("Content-Type")?.startsWith("application/json")) {
    return Response.json({ error: "Request origin rejected." }, { status: 403 });
  }
  const reader = request.body?.getReader();
  if (!reader) return Response.json({ error: "Missing report." }, { status: 400 });
  let size = 0; const chunks = [];
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > MAX_FEEDBACK_BYTES) {
      await reader.cancel();
      return Response.json({ error: "Report is too large. Remove the screenshot and try again." }, { status: 413 });
    }
    chunks.push(value);
  }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  let report;
  try { report = JSON.parse(new TextDecoder().decode(bytes)); } catch {
    return Response.json({ error: "Invalid report." }, { status: 400 });
  }
  if (!report || typeof report.id !== "string" || !/^[a-f0-9]{32}$/.test(report.id) || !["Bug", "Suggestion", "Other"].includes(report.category) ||
      typeof report.description !== "string" || !report.description.trim() || report.description.length > 4000 ||
      ["version", "platform", "screen", "mode"].some(key => typeof report[key] !== "string" || report[key].length > 120) ||
      !Number.isSafeInteger(report.turn) || report.turn < 0 || report.turn > 1000000 ||
      typeof report.screenshot !== "string" || report.screenshot.length > 1400000) {
    return Response.json({ error: "Invalid report." }, { status: 400 });
  }
  // Accept only the game's bounded JPEG attachment, never arbitrary HTML/SVG.
  if (report.screenshot) {
    if (!/^[A-Za-z0-9+/]+={0,2}$/.test(report.screenshot) || report.screenshot.length % 4 !== 0) {
      return Response.json({ error: "Invalid screenshot." }, { status: 400 });
    }
    let image;
    try { image = atob(report.screenshot); } catch { return Response.json({ error: "Invalid screenshot." }, { status: 400 }); }
    if (!image.startsWith("\xff\xd8\xff") || !image.endsWith("\xff\xd9")) {
      return Response.json({ error: "Invalid screenshot." }, { status: 400 });
    }
  }
  const result = await storeCall(env, "feedback-submit", {
    ...report, description: report.description.trim(), uid: actor.auth_user_id,
    origin: env.GAME_ORIGIN,
  });
  if (result.limited) return Response.json({ error: "You've sent several reports recently. Please try again later." }, { status: 429 });
  return Response.json({ id: report.id, saved: true }, { status: 201 });
}

export async function feedbackInbox(request, env) {
  const parameter = new URL(request.url).searchParams.get("page") ?? "0";
  if (!/^\d{1,4}$/.test(parameter)) return new Response("Invalid page.", { status: 400 });
  const current = Number(parameter);
  const rows = await storeCall(env, "feedback-list", { offset: current * 25 });
  const cards = rows.slice(0, 25).map(report => `<article class="tester">
<p><strong>${escape(report.category)}</strong> · ${escape(report.created_at)}<br>${escape(report.display_name)}</p>
<p>${escape(report.description).replace(/\n/g, "<br>")}</p>
<p class="note">${escape(report.version)} · ${escape(report.platform)} · ${escape(report.screen)} · ${escape(report.mode)} · Turn ${report.turn}<br>${escape(report.origin)}<br>Report ${escape(report.id)}</p>
${report.has_screenshot ? `<a href="/admin/feedback/${report.id}/screenshot" target="_blank" rel="noopener">Open screenshot</a><img class="feedback-image" src="/admin/feedback/${report.id}/screenshot" alt="Player attached screenshot" loading="lazy">` : ""}
</article>`).join("");
  return page("Player feedback", `<p>Private reports from approved testers. Screenshots contain the player's visible game view.</p>
${cards || "<p>No reports yet.</p>"}
${current > 0 ? `<a class="button secondary" href="/admin/feedback?page=${current - 1}">Newer reports</a>` : ""}
${rows.length > 25 ? `<a class="button secondary" href="/admin/feedback?page=${current + 1}">Older reports</a>` : ""}
<a class="button secondary" href="/admin/testers">Manage testers</a><a class="button secondary" href="/access">Back to your account</a>`);
}

export async function feedbackScreenshot(id, env) {
  const report = await storeCall(env, "feedback-image", { id });
  if (!report?.screenshot) return new Response("Screenshot not found.", { status: 404 });
  const bytes = Uint8Array.from(atob(report.screenshot), char => char.charCodeAt(0));
  return new Response(bytes, { headers: { "Content-Type": "image/jpeg", "Content-Disposition": 'inline; filename="feedback.jpg"' } });
}
