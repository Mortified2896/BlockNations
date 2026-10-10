mergeInto(LibraryManager.library, {
  CopyToClipboardWithResult: function (strPtr, receiverPtr, methodPtr) {
    var text = UTF8ToString(strPtr);
    var receiver = UTF8ToString(receiverPtr);
    var method = UTF8ToString(methodPtr);
    var finish = function (ok) { window.blockNations.SendMessage(receiver, method, ok ? "1" : "0"); };
    var fallback = function () {
      var textarea = document.createElement("textarea");
      var previousFocus = document.activeElement;
      try {
        textarea.value = text;
        textarea.setAttribute("readonly", "");
        textarea.style.position = "fixed";
        textarea.style.left = "-9999px";
        document.body.appendChild(textarea);
        textarea.select();
        finish(document.execCommand("copy"));
      } catch (e) {
        finish(false);
      } finally {
        textarea.remove();
        if (previousFocus && previousFocus.focus) previousFocus.focus();
      }
    };
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(function () { finish(true); }, fallback);
      } else {
        fallback();
      }
    } catch (e) {
      fallback();
    }
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
