// Legacy prototype links now return to the actual game's main menu. The main
// game's existing Google approval gate authorizes its HTML and binary assets.
export default {
  async fetch(request) {
    const path = new URL(request.url).pathname;
    if (path === "/advanced" || path === "/advanced/" || path === "/advanced/index.html")
      return new Response(null, { status: 303, headers: { Location: "/", "Cache-Control": "private, no-store" } });
    // Retire the independent export rather than serving stale prototype bytes.
    return new Response("Not found", { status: 404, headers: { "Cache-Control": "private, no-store" } });
  },
};
