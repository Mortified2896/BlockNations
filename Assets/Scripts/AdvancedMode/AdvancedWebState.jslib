mergeInto(LibraryManager.library, {
  AdvancedCanvasPixelRatio: function() {
    const canvas = document.getElementById('unity-canvas');
    return canvas && canvas.clientWidth ? canvas.width / canvas.clientWidth : window.devicePixelRatio || 1;
  },
  AdvancedPublishState: function(json) {
    // Read-only diagnostics must never interrupt the game loop. Layout can
    // still be settling when a scene first starts or the canvas is resized.
    let state;
    try {
      state = JSON.parse(UTF8ToString(json));
    } catch (error) {
      console.warn('Advanced game state was not ready to publish.');
      return;
    }
    window.advancedGameState = state;
    window.dispatchEvent(new CustomEvent('advanced-game-state', {detail:window.advancedGameState}));
  }
});
