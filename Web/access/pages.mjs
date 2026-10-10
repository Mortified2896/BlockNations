export function escape(value) {
  return String(value).replace(/[&<>"']/g, c => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  })[c]);
}

export function page(title, content, { status = 200, script = "", style = "", moduleScript = "", formOrigins = "'self'" } = {}) {
  const nonce = btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(24))));
  const html = `<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><link rel="icon" href="data:,"><title>${escape(title)} · Block Nations</title>
<link rel="manifest" href="/manifest.webmanifest"><link rel="apple-touch-icon" href="/icons/app-192.png"><meta name="theme-color" content="#314d79"><meta name="apple-mobile-web-app-capable" content="yes">
<style nonce="${nonce}">
:root{color-scheme:light;--ink:#202020;--muted:#696969;--line:#e9e9e9;--paper:#fff;--sans:Inter,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;--serif:Georgia,"Times New Roman",serif;font-synthesis:none;text-rendering:optimizeLegibility}
*{box-sizing:border-box}[hidden]{display:none!important}body{margin:0;min-height:100svh;display:flex;flex-direction:column;background:var(--paper);color:var(--ink);font-family:var(--sans)}
.site-header,.site-footer{width:min(calc(100% - 80px),1120px);margin-inline:auto;display:flex;justify-content:space-between;align-items:center;gap:20px}
.site-header{padding:24px 0;border-bottom:1px solid var(--line)}.wordmark{font-family:var(--serif);font-size:32px;font-weight:700;letter-spacing:-.09em;text-decoration:none;line-height:1}.site-name{font-size:13px;color:var(--muted)}
main{flex:1;width:min(calc(100% - 80px),480px);margin-inline:auto;padding:80px 0}.eyebrow{font-size:11px;letter-spacing:.12em;text-transform:uppercase;color:var(--muted);font-weight:550}
h1{font-family:var(--serif);font-size:42px;font-weight:500;letter-spacing:-.045em;margin:18px 0 24px;line-height:1.12;text-wrap:pretty}p{line-height:1.65;font-size:15px;overflow-wrap:anywhere;color:var(--muted)}a{color:var(--ink);text-underline-offset:4px}
.button,button{display:flex;justify-content:center;align-items:center;min-height:48px;width:100%;margin-top:28px;padding:12px 16px;background:var(--ink);color:var(--paper);border:1px solid var(--ink);border-radius:2px;font-family:inherit;font-size:14px;font-weight:600;text-decoration:none;cursor:pointer}
.button:hover,button:hover{background:#3a3a3a;border-color:#3a3a3a}button:disabled{opacity:.65;cursor:wait}.secondary{background:var(--paper);color:var(--ink);border-color:var(--line);margin-top:12px}.secondary:hover{background:#f6f6f6;border-color:#bdbdbd}
a:focus-visible,button:focus-visible{outline:2px solid var(--ink);outline-offset:5px}.note{font-size:13px;color:var(--muted);margin-top:24px}.tester{border-top:1px solid var(--line);padding-top:16px;margin-top:28px}.tester strong{color:var(--ink)}.feedback-image{display:block;max-width:100%;height:auto;margin-top:16px;border:1px solid var(--line)}#error{color:var(--ink);font-weight:600}#error:empty{display:none}form{margin:0}
.site-footer{border-top:1px solid var(--line);padding:24px 0;font-size:12px;color:var(--muted)}@media(max-width:600px){.site-header,.site-footer{width:calc(100% - 44px)}.site-header{padding:20px 0}.wordmark{font-size:27px}.site-name{font-size:12px}main{width:calc(100% - 44px);padding:56px 0 64px}h1{font-size:36px}.site-footer{font-size:11px;flex-wrap:wrap;gap:10px}}
${style}</style></head><body><header class="site-header"><a class="wordmark" href="https://moneymattersmedia.com/" aria-label="Money Matters Media home">MMM.</a><span class="site-name">Block Nations</span></header><main>
<div class="eyebrow">Block Nations · Private playtest</div><h1>${escape(title)}</h1>${content}</main>
<footer class="site-footer"><span>Money Matters Media</span><span>Friends &amp; invited testers</span></footer>
${script ? `<script nonce="${nonce}">${script}</script>` : ""}${moduleScript ? `<script nonce="${nonce}" type="module" src="${escape(moduleScript)}"></script>` : ""}</body></html>`;
  return new Response(html, {
    status,
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      "Content-Security-Policy": `default-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'; img-src 'self' data:; connect-src 'self'; worker-src 'self'; manifest-src 'self'; form-action ${formOrigins}; base-uri 'none'; frame-ancestors 'none'`,
    },
  });
}

export function loginPage() {
  return page("Welcome, tester", `<p>This playtest is for friends and invited testers. Sign in with Google to continue.</p>
<a class="button" href="/auth/login">Continue with Google</a>
<form method="post" action="/auth/switch-account"><button class="secondary">Use a different Google account</button></form>
<p class="note">Already approved on the Review website? Your approval works here too. New accounts wait for approval before they can play.</p>`);
}

export function accountPage(user) {
  const signedIn = `<p class="note">Signed in as ${escape(user.email)}</p>`;
  const accountActions = `<form method="post" action="/auth/switch-account"><button class="secondary">Switch Google account</button></form><form method="post" action="/auth/logout"><button class="secondary">Sign out of Block Nations</button></form>`;
  if (user.status === "approved") {
    return page("You're ready to play", `<p>Welcome, ${escape(user.display_name)}. Your tester account is approved.</p><a class="button" href="/">Play Block Nations</a>${user.can_administer ? '<a class="button secondary" href="/admin/testers">Manage playtest access</a><a class="button secondary" href="/admin/feedback">Player feedback</a>' : ""}${signedIn}${accountActions}`);
  }
  if (user.status === "pending") {
    return page("Waiting for approval", `<p>Your request has been saved. Jo needs to approve your account before you can play.</p><p>You can come back to this page to check. You won't need to register again.</p><a class="button" href="/access">Check approval</a>${signedIn}${accountActions}`);
  }
  return page("Access unavailable", `<p>This account doesn't currently have access to the playtest. Contact Jo if you think this is a mistake.</p>${signedIn}${accountActions}`, { status: 403 });
}

export function googlePage(callback, failed = false, chooseAccount = false) {
  return page(chooseAccount ? "Switch Google account" : "Sign in with Google", `<p>${chooseAccount ? "Choose the Google account you want to use. This also changes the account signed in on the Review website in this browser." : "Use the same Google account you use on the Review website. Your existing approval will carry over."}</p><button id="google">${chooseAccount ? "Choose Google account" : "Continue with Google"}</button><p id="error" role="alert">${failed ? "Sign-in wasn't completed. Please try again." : ""}</p><p class="note">New testers wait for Jo's approval before playing.</p>`, {
    script: `document.getElementById('google').addEventListener('click', async function () {
this.disabled = true; const error = document.getElementById('error'); error.textContent = '';
try { const response = await fetch('/api/auth/sign-in/social', {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({provider:'google',callbackURL:${JSON.stringify(callback)},errorCallbackURL:${JSON.stringify(callback + "&login_error=1")},disableRedirect:true${chooseAccount ? ",additionalParams:{prompt:'select_account'}" : ""}})});
const result = await response.json(); if (!response.ok || !result.url || new URL(result.url).hostname !== 'accounts.google.com') throw new Error(); window.location.assign(result.url);
} catch { error.textContent = 'Sign-in could not start. Please try again.'; this.disabled = false; }
});${chooseAccount && !failed ? "document.getElementById('google').click();" : ""}`,
  });
}
