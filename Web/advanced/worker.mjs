import access from "../access/worker.mjs";

// A scoped route ahead of the main game custom domain. Reuses admission and
// the existing host-only session, but serves only the independent advanced export.
export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname !== "/advanced" && !url.pathname.startsWith("/advanced/"))
      return new Response("Not found", { status: 404 });
    if (url.pathname === "/advanced")
      return new Response(null, { status: 308, headers: { Location: "/advanced/", "Cache-Control": "private, no-store" } });
    const scoped = { ...env, ASSETS: { fetch(assetRequest) {
      const assetURL = new URL(assetRequest.url);
      assetURL.pathname = assetURL.pathname.slice("/advanced".length);
      if (assetURL.pathname === "/") assetURL.pathname = "/index.html";
      return env.ASSETS.fetch(new Request(assetURL, assetRequest));
    } } };
    const response = await access.fetch(request, scoped);
    if (url.pathname === "/advanced/" && response.status === 403)
      return new Response(null, { status: 303, headers: { Location: "/access", "Cache-Control": "private, no-store" } });
    return response;
  },
};
