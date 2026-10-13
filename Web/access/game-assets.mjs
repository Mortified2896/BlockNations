export function isVersionedGameAsset(pathname) {
  return /^\/Build\/[a-f0-9]{32}\.(?:loader\.js|(?:data|wasm|framework\.js)\.(?:br|gz|unityweb))$/.test(pathname);
}

// Call only after admission. Error pages must never acquire a game encoding.
export function gameAssetResponse(request, response) {
  const match = /^\/Build\/[^/]+\.(data|wasm|framework\.js)\.(br|gz|unityweb)$/.exec(new URL(request.url).pathname);
  if (!match || ![200, 304].includes(response.status)) return response;
  const headers = new Headers(response.headers);
  headers.set("Content-Encoding", match[2] === "br" ? "br" : "gzip");
  headers.set("Content-Type", match[1] === "wasm" ? "application/wasm" :
    match[1] === "framework.js" ? "application/javascript" : "application/octet-stream");
  // These are already compressed build bytes: Workers must not encode again.
  return new Response(response.body, { status: response.status, headers, encodeBody: "manual" });
}
