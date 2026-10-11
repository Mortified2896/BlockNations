mergeInto(LibraryManager.library, {
  AdvancedPublishState: function(json) {
    window.advancedGameState = JSON.parse(UTF8ToString(json));
    window.dispatchEvent(new CustomEvent('advanced-game-state', {detail:window.advancedGameState}));
  }
});
