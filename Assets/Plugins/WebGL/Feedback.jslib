mergeInto(LibraryManager.library, {
  BlockNations_RegisterFeedback: function(objectName) {
    window.blockNationsFeedbackReceiver = UTF8ToString(objectName);
  },
  BlockNations_UnregisterFeedback: function(objectName) {
    if (window.blockNationsFeedbackReceiver === UTF8ToString(objectName)) window.blockNationsFeedbackReceiver = '';
  },
  BlockNations_OpenFeedback: function(report, objectName) {
    var target = UTF8ToString(objectName);
    window.BlockNationsFeedback.open(JSON.parse(UTF8ToString(report)), function() {
      window.blockNations.SendMessage(target, 'OnBrowserFeedbackClosed');
    });
  },
  BlockNations_CloseFeedback: function() { window.BlockNationsFeedback.close(); }
});
