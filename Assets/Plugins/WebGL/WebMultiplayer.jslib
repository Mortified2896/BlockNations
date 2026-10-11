mergeInto(LibraryManager.library, {
  BN_WebSetScene: function(sceneName) {
    var scene = UTF8ToString(sceneName);
    window.blockNationsScene = scene;
    var accountBar = document.querySelector('.account-bar');
    if (accountBar) accountBar.hidden = scene !== 'MainMenu';
  },
  BN_WebNavigate: function(path) { window.location.assign(UTF8ToString(path)); },
  BN_WebStatus: function(message) {
    var value = UTF8ToString(message), panel = document.getElementById('multiplayer-state');
    if (!panel) return;
    document.getElementById('multiplayer-message').textContent = value;
    panel.hidden = !value;
  },
  BN_WebMatchLoaded: function() {
    window.history.replaceState(null, '', '/');
  }
});
