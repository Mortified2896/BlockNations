mergeInto(LibraryManager.library, {
  ShowMenuVersionCopy: function (textPtr) {
    var text = UTF8ToString(textPtr);
    if (window.bnMenuVersionCleanup) window.bnMenuVersionCleanup();
    var button = document.createElement("button");
    button.id = "bn-menu-version";
    button.type = "button";
    button.textContent = text;
    button.setAttribute("aria-label", "Copy build version " + text);
    button.style.cssText = "position:absolute;top:max(8px,env(safe-area-inset-top));left:max(8px,env(safe-area-inset-left));z-index:2;min-height:44px;padding:8px;border:0;background:transparent;color:#ffffffb3;font:16px system-ui,sans-serif;cursor:pointer;touch-action:manipulation";
    var timer, copying = false, removed = false;
    var finish = function (ok) {
      if (removed) return;
      copying = false;
      button.textContent = ok ? "Copied!" : "Couldn't copy — tap to retry";
      button.setAttribute("aria-label", button.textContent);
      timer = setTimeout(function () {
        button.textContent = text;
        button.setAttribute("aria-label", "Copy build version " + text);
      }, 2000);
    };
    button.addEventListener("click", function (event) {
      event.stopPropagation();
      if (copying) return;
      clearTimeout(timer);
      copying = true;
      // Start inside the native click event, before Unity or any asynchronous work.
      // WebKit rejects clipboard writes dispatched later by Unity's frame loop.
      try {
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(text).then(function () { finish(true); }, function () { finish(false); });
        } else {
          var textarea = document.createElement("textarea");
          textarea.value = text;
          textarea.style.cssText = "position:fixed;top:0;left:0;opacity:0;font-size:16px";
          textarea.setAttribute("readonly", "");
          document.body.appendChild(textarea);
          textarea.select();
          textarea.setSelectionRange(0, text.length);
          try { finish(document.execCommand("copy")); } finally { textarea.remove(); button.focus(); }
        }
      } catch (e) { finish(false); }
    });
    button.setAttribute("aria-live", "polite");
    document.getElementById("game").appendChild(button);
    window.bnMenuVersionCleanup = function () { removed = true; clearTimeout(timer); button.remove(); };
  },
  HideMenuVersionCopy: function () {
    if (window.bnMenuVersionCleanup) window.bnMenuVersionCleanup();
    window.bnMenuVersionCleanup = null;
  },
  CopyToClipboard: function (strPtr) {
    try {
      var text = UTF8ToString(strPtr);

      // Prefer the modern async clipboard API when available.
      if (navigator && navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).catch(function () {
          // Fall back to execCommand below if the promise is rejected.
          try {
            var ta = document.createElement("textarea");
            ta.value = text;
            ta.setAttribute("readonly", "");
            ta.style.position = "fixed";
            ta.style.left = "-9999px";
            ta.style.top = "0";
            document.body.appendChild(ta);
            ta.select();
            document.execCommand("copy");
            document.body.removeChild(ta);
          } catch (e) { }
        });
        return 1;
      }

      // Legacy fallback: textarea + execCommand('copy').
      var textarea = document.createElement("textarea");
      textarea.value = text;
      textarea.setAttribute("readonly", "");
      textarea.style.position = "fixed";
      textarea.style.left = "-9999px";
      textarea.style.top = "0";
      document.body.appendChild(textarea);
      textarea.select();
      var ok = document.execCommand("copy");
      document.body.removeChild(textarea);
      return ok ? 1 : 0;
    } catch (e) {
      return 0;
    }
  }
});
