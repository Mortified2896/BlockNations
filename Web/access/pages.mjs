export function escape(value) {
  return String(value).replace(/[&<>"']/g, c => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  })[c]);
}

export function page(title, content, { status = 200, script = "", formOrigins = "'self'" } = {}) {
  const nonce = btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(24))));
  const html = `<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><link rel="icon" href="data:,"><title>${escape(title)} · Block Nations</title>
<style nonce="${nonce}">
*{box-sizing:border-box}body{margin:0;min-height:100svh;display:grid;place-items:center;padding:24px;background:#e8eee2;color:#192b24;font-family:system-ui,-apple-system,sans-serif}
main{width:min(100%,440px);padding:38px 32px;background:#fffef8;border:1px solid #cad4c3;border-radius:18px;box-shadow:0 16px 60px #1a332418}
.mark{display:flex;gap:5px;margin-bottom:24px}.mark span{width:20px;height:20px;background:#426848;border-radius:3px}.mark span:nth-child(2){background:#abc45b}.mark span:nth-child(3){background:#d7b965}
.eyebrow{font-size:12px;letter-spacing:.12em;text-transform:uppercase;color:#52634c;font-weight:650}h1{font-size:28px;letter-spacing:-.03em;margin:12px 0 18px;line-height:1.15}p{line-height:1.6;font-size:15px;overflow-wrap:anywhere}a{color:#315c3d;text-underline-offset:3px}
.button,button{display:flex;justify-content:center;align-items:center;min-height:48px;width:100%;margin-top:24px;padding:12px 16px;background:#315c3d;color:white;border:0;border-radius:8px;font:600 15px system-ui;text-decoration:none;cursor:pointer}
button:disabled{opacity:.65;cursor:wait}.secondary{background:#edf1e8;color:#315c3d;margin-top:12px}a:focus-visible,button:focus-visible{outline:3px solid #9da942;outline-offset:4px}.note{font-size:13px;color:#65705d;margin-top:24px}.tester{border-top:1px solid #cad4c3;padding-top:16px;margin-top:24px}#error{color:#9e321c}form{margin:0}@media(max-width:420px){main{padding:30px 24px}body{padding:18px}}
</style></head><body><main><div class="mark" aria-hidden="true"><span></span><span></span><span></span></div>
<div class="eyebrow">Block Nations · Private playtest</div><h1>${escape(title)}</h1>${content}</main>
${script ? `<script nonce="${nonce}">${script}</script>` : ""}</body></html>`;
  return new Response(html, {
    status,
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      "Content-Security-Policy": `default-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'; img-src data:; connect-src 'self'; form-action ${formOrigins}; base-uri 'none'; frame-ancestors 'none'`,
    },
  });
}

export function loginPage() {
  return page("Welcome, tester", `<p>This playtest is for friends and invited testers. Sign in with Google to continue.</p>
<a class="button" href="/auth/login">Continue with Google</a>
<p class="note">Already approved on the Review website? Your approval works here too. New accounts wait for approval before they can play.</p>`);
}

export function accountPage(user) {
  const signedIn = `<p class="note">Signed in as ${escape(user.email)}</p>`;
  const logout = `<form method="post" action="/auth/logout"><button class="secondary">Sign out of Block Nations</button></form>`;
  if (user.status === "approved") {
    return page("You're ready to play", `<p>Welcome, ${escape(user.display_name)}. Your tester account is approved.</p><a class="button" href="/">Play Block Nations</a>${user.can_administer ? '<a class="button secondary" href="/admin/testers">Manage playtest access</a>' : ""}${signedIn}${logout}`);
  }
  if (user.status === "pending") {
    return page("Waiting for approval", `<p>Your request has been saved. Jo needs to approve your account before you can play.</p><p>You can come back to this page to check. You won't need to register again.</p><a class="button" href="/access">Check approval</a>${signedIn}${logout}`);
  }
  return page("Access unavailable", `<p>This account doesn't currently have access to the playtest. Contact Jo if you think this is a mistake.</p>${signedIn}${logout}`, { status: 403 });
}

export function googlePage(callback, failed = false) {
  return page("Sign in with Google", `<p>Use the same Google account you use on the Review website. Your existing approval will carry over.</p><button id="google">Continue with Google</button><p id="error" role="alert">${failed ? "Sign-in wasn't completed. Please try again." : ""}</p><p class="note">New testers wait for Jo's approval before playing.</p>`, {
    script: `document.getElementById('google').addEventListener('click', async function () {
this.disabled = true; const error = document.getElementById('error'); error.textContent = '';
try { const response = await fetch('/api/auth/sign-in/social', {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({provider:'google',callbackURL:${JSON.stringify(callback)},errorCallbackURL:${JSON.stringify(callback + "&login_error=1")},disableRedirect:true})});
const result = await response.json(); if (!response.ok || !result.url || new URL(result.url).hostname !== 'accounts.google.com') throw new Error(); window.location.assign(result.url);
} catch { error.textContent = 'Sign-in could not start. Please try again.'; this.disabled = false; }
});`,
  });
}
