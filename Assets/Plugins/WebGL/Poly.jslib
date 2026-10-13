mergeInto(LibraryManager.library, {
  BN_PolyPixelRatio: function() {
    var canvas = document.getElementById('unity-canvas');
    return canvas && canvas.clientWidth ? canvas.width / canvas.clientWidth : window.devicePixelRatio || 1;
  },
  BN_PolySync: function(receiver) {
    var target = UTF8ToString(receiver);
    var finish = function(error) {
      if (window.blockNations) window.blockNations.SendMessage(target, 'OnPolySaved', error ? 'error' : 'ok');
    };
    if (window.__polySyncBusy) { window.__polySyncPending = true; return; }
    window.__polySyncBusy = true;
    var sync = function() {
      FS.syncfs(false, function(error) {
        if (window.__polySyncPending && !error) { window.__polySyncPending = false; sync(); return; }
        window.__polySyncBusy = false; window.__polySyncPending = false; finish(error);
      });
    };
    sync();
  },
  BN_PolyPublish: function(json) {
    try { window.blockNationsPoly = JSON.parse(UTF8ToString(json)); } catch (_) {}
  }
});
