export function escape(value) {
  return String(value).replace(/[&<>"']/g, c => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  })[c]);
}

// Account and game screens share document security, not their visual shell.
export function htmlPage(title, content, { status = 200, script = "", style = "", moduleScript = "", classicScripts = [], formOrigins = "'self'", connectOrigins = "'self'", allowSameOriginFrame = false } = {}) {
  const nonce = btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(24))));
  const html = `<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><link rel="icon" href="data:,"><title>${escape(title)} · Block Nations</title>
<link rel="manifest" href="/manifest.webmanifest"><link rel="apple-touch-icon" href="/icons/app-192.png"><meta name="theme-color" content="#314d79"><meta name="apple-mobile-web-app-capable" content="yes">
<style nonce="${nonce}">${style}</style></head><body>${content}
${classicScripts.map(src => `<script nonce="${nonce}" src="${escape(src)}"></script>`).join("")}
${script ? `<script nonce="${nonce}">${script}</script>` : ""}${moduleScript ? `<script nonce="${nonce}" type="module" src="${escape(moduleScript)}"></script>` : ""}</body></html>`;
  return new Response(html, {
    status,
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      "X-Frame-Options": allowSameOriginFrame ? "SAMEORIGIN" : "DENY",
      "Content-Security-Policy": `default-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'; img-src 'self' data:; connect-src ${connectOrigins}; worker-src 'self'; manifest-src 'self'; form-action ${formOrigins}; base-uri 'none'; frame-ancestors ${allowSameOriginFrame ? "'self'" : "'none'"}`,
    },
  });
}
