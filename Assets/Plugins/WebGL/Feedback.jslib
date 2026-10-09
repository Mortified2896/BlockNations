mergeInto(LibraryManager.library, {
  BlockNations_OpenFeedback: function(report, objectName) {
    var target = UTF8ToString(objectName);
    window.BlockNationsFeedback.open(JSON.parse(UTF8ToString(report)), function() {
      window.blockNations.SendMessage(target, 'OnBrowserFeedbackClosed');
    });
  },
  BlockNations_CloseFeedback: function() { window.BlockNationsFeedback.close(); }
});
