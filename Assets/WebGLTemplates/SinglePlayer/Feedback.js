// Browser-native controls keep text entry, mobile keyboards and focus out of
// Unity's game input. Screenshots arrive from the rendered Unity player view.
(() => {
  const key = 'BlockNations.Feedback.Draft';
  let dismiss = null, opening = false, generation = 0;
  const style = document.createElement('style');
  style.nonce = document.currentScript?.nonce || '';
  style.textContent = `
.bn-feedback{position:fixed;inset:0;z-index:10000;margin:0;border:0;width:100%;height:100%;max-width:none;max-height:none;background:#000c;display:flex;align-items:center;justify-content:center;padding:max(12px,env(safe-area-inset-top)) max(12px,env(safe-area-inset-right)) max(12px,env(safe-area-inset-bottom)) max(12px,env(safe-area-inset-left));color:#f4f7fd}
.bn-feedback form{width:100%;max-width:480px;max-height:100%;overflow:auto;background:#182332;border-radius:12px;padding:20px;font-size:16px;line-height:1.4}
.bn-feedback h2{margin:0 0 16px;font-size:24px}.bn-feedback label{display:block;margin:12px 0 6px}.bn-feedback select,.bn-feedback textarea{display:block;width:100%;font:inherit;border:1px solid #7b8ba3;border-radius:5px;padding:10px;background:#f5f7fa;color:#161f2c}.bn-feedback textarea{min-height:100px;resize:vertical}
.bn-feedback .check{display:flex;gap:10px;align-items:center;min-height:44px}.bn-feedback input[type=checkbox]{width:20px;height:20px}.bn-feedback img{display:block;max-width:100%;height:140px;object-fit:contain;margin:8px auto}.bn-feedback button{min-height:48px;padding:10px 16px;border:1px solid #8390a3;border-radius:6px;background:#344152;color:white;font:inherit;cursor:pointer}.bn-feedback button[type=submit]{background:#3670a7}.bn-feedback button:disabled{opacity:.55;cursor:default}.bn-feedback :focus-visible{outline:3px solid #89c6ff;outline-offset:2px}.bn-feedback .actions{display:flex;justify-content:space-between;gap:12px;margin-top:16px}.bn-feedback p{font-size:14px;color:#d1def1}.bn-feedback [hidden]{display:none!important}
.bn-feedback-launcher{position:fixed;inset:auto;top:max(8px,env(safe-area-inset-top));right:max(12px,env(safe-area-inset-right));z-index:1001;margin:0;min-height:44px;padding:8px 14px;border:1px solid #8390a3;border-radius:8px;background:#192639;color:#f4f7fd;font:600 14px system-ui,sans-serif;cursor:pointer}
.bn-feedback-launcher:focus-visible{outline:3px solid #89c6ff;outline-offset:2px}
`;
  document.head.append(style);
  window.BlockNationsFeedback = {
    get isOpen() { return !!dismiss || opening; },
    close() { generation++; opening = false; dismiss?.(true); },
    open(report, onClose) {
      if (dismiss || opening) return;
      opening = true;
      const current = ++generation;
      // Register after Unity has queued its next frame. This callback then runs
      // after that render and before the browser clears/presents the GL buffer.
      setTimeout(() => requestAnimationFrame(() => {
        if (current !== generation) return;
        opening = false;
        this.openRendered(report,onClose);
      }),0);
    },
    openRendered(report, onClose, captureCanvas = true) {
      if (dismiss) return;
      // Called by Unity at the end of the rendered frame, before this overlay
      // exists. Read the game canvas directly to preserve browser color output.
      report.screenshot = '';
      try {
        if (!captureCanvas) throw new Error('This screen has no game canvas.');
        const source = document.getElementById('unity-canvas');
        const scale = Math.min(1,1280 / Math.max(source.width,source.height));
        const capture = document.createElement('canvas');
        capture.width = Math.max(1,Math.round(source.width*scale)); capture.height = Math.max(1,Math.round(source.height*scale));
        capture.getContext('2d').drawImage(source,0,0,capture.width,capture.height);
        const jpeg = capture.toDataURL('image/jpeg',0.7).split(',')[1];
        if (jpeg && jpeg.length <= 1333336) report.screenshot = jpeg;
      } catch {}
      // Browser drafts own their identity; a completed report starts fresh even
      // if an older Unity/native preference still exists on the device.
      report.id = crypto.randomUUID().replaceAll('-','');
      report.category = 'Bug'; report.description = ''; report.attempted = false;
      let stored;
      try { stored = JSON.parse(localStorage.getItem(key)); } catch {}
      if (stored && /^[a-f0-9]{32}$/.test(stored.id) && typeof stored.description === 'string') {
        report.id = stored.id; report.category = ['Bug','Suggestion','Other'].includes(stored.category) ? stored.category : 'Bug';
        report.description = stored.description.slice(0,4000); report.attempted = !!stored.attempted;
      }
      let sending = false, sent = false, pending = null;
      const previousFocus = document.activeElement;
      const overlay = document.createElement('dialog'); overlay.className = 'bn-feedback';
      overlay.setAttribute('aria-labelledby', 'bn-feedback-title');
      overlay.innerHTML = `<form>
<h2 id="bn-feedback-title">Send feedback</h2>
<p id="bn-feedback-context" hidden></p>
<label for="bn-feedback-type">Type</label><select id="bn-feedback-type"><option>Bug</option><option>Suggestion</option><option>Other</option></select>
<label for="bn-feedback-description">What happened, or what would you improve?</label><textarea id="bn-feedback-description" maxlength="4000" required></textarea>
<label class="check"><input id="bn-feedback-include" type="checkbox">Include screenshot</label><img alt="Screenshot attached to your report"><button type="button" id="bn-feedback-remove">Remove screenshot</button>
<p>The screenshot and build information will be sent with your report. Only the playtest administrators can view it.</p>
<p id="bn-feedback-status" role="status" aria-live="polite"></p><div class="actions"><button type="button" id="bn-feedback-cancel">Cancel</button><button type="submit">Send</button></div></form>`;
      const form = overlay.querySelector('form'), category = form.querySelector('select'), description = form.querySelector('textarea');
      const include = form.querySelector('input'), image = form.querySelector('img'), remove = form.querySelector('#bn-feedback-remove');
      const status = form.querySelector('#bn-feedback-status'), cancel = form.querySelector('#bn-feedback-cancel');
      const context = form.querySelector('#bn-feedback-context');
      context.hidden = false; context.textContent = report.screen;
      category.value = report.category; description.value = report.description;
      include.checked = !!report.screenshot; include.disabled = !report.screenshot;
      image.hidden = remove.hidden = !report.screenshot;
      if (report.screenshot) image.src = 'data:image/jpeg;base64,' + report.screenshot;
      else status.textContent = captureCanvas ? "Screenshot couldn't be captured. You can still send a text report." : 'You can send a text report from this screen.';
      const persist = () => { try { localStorage.setItem(key, JSON.stringify({id:report.id,category:category.value,description:description.value,attempted:report.attempted})); } catch { status.textContent = 'This browser cannot save drafts. Keep this form open until you send it.'; } };
      const changed = () => {
        if (sending || sent) return;
        if (report.attempted) { report.id = crypto.randomUUID().replaceAll('-',''); report.attempted = false; }
        pending = null; persist();
      };
      description.addEventListener('input', changed); category.addEventListener('change', changed);
      include.addEventListener('change', () => { image.hidden = !include.checked; changed(); });
      remove.addEventListener('click', () => { report.screenshot = ''; include.checked = false; include.disabled = true; image.removeAttribute('src'); image.hidden = remove.hidden = true; changed(); });
      const close = (force = false) => { if (sending && !force) return; if (!sent) persist(); overlay.close(); overlay.remove(); dismiss = null; previousFocus?.focus(); onClose?.(); };
      dismiss = close; cancel.addEventListener('click', () => close());
      overlay.addEventListener('cancel', event => { event.preventDefault(); close(); });
      overlay.addEventListener('keydown', event => {
        event.stopPropagation();
        if (event.key === 'Escape') { event.preventDefault(); close(); }
        if (event.key === 'Tab') {
          const controls = [...form.querySelectorAll('button,input,select,textarea')].filter(e => !e.disabled && !e.hidden);
          const first = controls[0], last = controls.at(-1);
          if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
          else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        }
      });
      overlay.addEventListener('keyup', event => event.stopPropagation());
      form.addEventListener('submit', async event => {
        event.preventDefault(); if (sending || sent) return;
        if (!description.value.trim()) { status.textContent = 'Please add a description.'; description.focus(); return; }
        report.attempted = true; persist();
        if (!pending) pending = JSON.stringify({...report,category:category.value,description:description.value,screenshot:include.checked ? report.screenshot : ''});
        sending = true; form.querySelectorAll('button,input,select,textarea').forEach(e => e.disabled = true); status.textContent = 'Sending…';
        const abort = new AbortController(), timeout = setTimeout(() => abort.abort(),30000);
        try {
          const response = await fetch('/api/feedback', {method:'POST',headers:{'Content-Type':'application/json'},body:pending,credentials:'same-origin',signal:abort.signal});
          const receipt = await response.json();
          if (!response.ok || !receipt.saved || receipt.id !== report.id) throw new Error(receipt.error || "Couldn't confirm the report was sent. Press Send to retry.");
          sent = true; try { localStorage.removeItem(key); } catch {}
          status.textContent = 'Feedback sent. Thank you!'; cancel.textContent = 'Done'; form.querySelector('button[type=submit]').hidden = true;
        } catch (error) { status.textContent = error.name === 'AbortError' ? 'The request timed out. Your report is kept here; press Send to retry.' : error.message; }
        finally {
          clearTimeout(timeout); sending = false; cancel.disabled = false;
          if (!sent) { form.querySelectorAll('button,input,select,textarea').forEach(e => e.disabled = false); include.disabled = !report.screenshot; }
        }
      });
      document.body.append(overlay); overlay.showModal(); description.focus();
    }
  };
  const launcher = document.createElement('button');
  launcher.type = 'button'; launcher.textContent = 'Feedback'; launcher.className = 'bn-feedback-launcher';
  launcher.setAttribute('aria-haspopup', 'dialog');
  launcher.addEventListener('click', () => {
    const lobby = location.pathname === '/multiplayer' || window.blockNationsNavigation?.isLobbyOpen;
    const receiver = window.blockNationsFeedbackReceiver;
    if (!lobby && receiver && window.blockNations) {
      window.blockNations.SendMessage(receiver, 'OpenFeedback'); return;
    }
    const report = { screen: lobby ? 'Menu/Multiplayer' : window.blockNationsScene || 'Loading',
      mode: lobby ? 'Menu' : 'None', turn: 0, version: window.blockNationsBuildVersion || 'unknown', platform: 'WebGLPlayer' };
    window.BlockNationsFeedback.openRendered(report, () => launcher.focus(), false);
  });
  // A popover stays accessible above standalone lobby dialogs. The embedded
  // lobby is a separate document below this top-level utility.
  if (typeof launcher.showPopover === 'function') {
    launcher.popover = 'manual';
    const raise = () => {
      if (launcher.matches(':popover-open')) launcher.hidePopover();
      // Modal dialogs make controls outside their subtree inert. Keep the
      // utility inside the active lobby dialog before raising its popover.
      const modal = [...document.querySelectorAll('dialog[open]:not(.bn-feedback)')].at(-1);
      (modal || document.body).append(launcher);
      launcher.showPopover();
    };
    document.body.append(launcher); raise();
    new MutationObserver(records => {
      if (records.some(record => record.target.tagName === 'DIALOG' && !record.target.classList.contains('bn-feedback'))) raise();
    }).observe(document.body, { subtree: true, attributes: true, attributeFilter: ['open'] });
  } else document.body.append(launcher);
})();
