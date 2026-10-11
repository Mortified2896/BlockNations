mergeInto(LibraryManager.library, {
  AdvancedCanvasPixelRatio: function() {
    const canvas = document.getElementById('unity-canvas');
    return canvas && canvas.clientWidth ? canvas.width / canvas.clientWidth : window.devicePixelRatio || 1;
  },
  AdvancedPublishState: function(json) {
    window.advancedGameState = JSON.parse(UTF8ToString(json));
    window.dispatchEvent(new CustomEvent('advanced-game-state', {detail:window.advancedGameState}));
  }
});
