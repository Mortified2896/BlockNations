mergeInto(LibraryManager.library, {
  BN_WebSetScene: function(sceneName) {
    var scene = UTF8ToString(sceneName);
    window.blockNationsScene = scene;
    var accountBar = document.querySelector('.account-bar');
    if (accountBar) accountBar.hidden = scene !== 'MainMenu';
    if (scene !== 'SampleScene' && window.BlockNationsLive) window.BlockNationsLive.unwatchMatch();
    if (window.blockNationsNavigation) window.blockNationsNavigation.sceneChanged(scene);
  },
  BN_WebRegisterMenu: function(objectName) {
    window.blockNationsMenuReceiver = UTF8ToString(objectName);
    if (window.blockNationsNavigation) window.blockNationsNavigation.registerMenu(window.blockNationsMenuReceiver);
  },
  BN_WebRegisterExit: function(objectName) { window.blockNationsExitReceiver = UTF8ToString(objectName); },
  BN_WebNavigate: function(path) {
    var destination = UTF8ToString(path);
    if (destination === '/multiplayer' && window.blockNationsNavigation) window.blockNationsNavigation.openLobby();
    else window.location.assign(destination);
  },
  BN_WebStatus: function(message) {
    var value = UTF8ToString(message), panel = document.getElementById('multiplayer-state');
    if (!panel) return;
    document.getElementById('multiplayer-message').textContent = value;
    panel.hidden = !value;
  },
  BN_WebMatchLoaded: function(matchId) {
    if (window.BlockNationsLive) window.BlockNationsLive.watchMatch(UTF8ToString(matchId));
    window.history.replaceState({ blocknationsView: 'game' }, '', '/');
  },
  BN_WebConsumeMatchUpdate: function(matchId) {
    return window.BlockNationsLive && window.BlockNationsLive.consumeMatchUpdate(UTF8ToString(matchId)) ? 1 : 0;
  }
});
