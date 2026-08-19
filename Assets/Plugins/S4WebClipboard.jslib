mergeInto(LibraryManager.library, {
  S4ClipboardWrite: function (textPointer) {
    var text = UTF8ToString(textPointer);
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).catch(function (error) {
        console.warn("Could not write to the system clipboard.", error);
      });
    }
  },

  S4ClipboardRead: function (receiverPointer) {
    var receiver = UTF8ToString(receiverPointer);
    if (!navigator.clipboard || !navigator.clipboard.readText) return;
    navigator.clipboard.readText().then(function (text) {
      if (text && window.s4UnityInstance) {
        window.s4UnityInstance.SendMessage(receiver, "ReceiveClipboardText", text);
      }
    }).catch(function (error) {
      console.warn("Could not read the system clipboard.", error);
    });
  },

  S4ClipboardInstallPasteHandler: function (receiverPointer) {
    window.s4ClipboardReceiver = UTF8ToString(receiverPointer);
    if (window.s4ClipboardHandlerInstalled) return;
    window.s4ClipboardHandlerInstalled = true;
    document.addEventListener("paste", function (event) {
      var clipboard = event.clipboardData || window.clipboardData;
      var text = clipboard ? clipboard.getData("text/plain") : "";
      if (!text || !window.s4UnityInstance) return;
      window.s4UnityInstance.SendMessage(
        window.s4ClipboardReceiver,
        "ReceiveClipboardText",
        text
      );
      event.preventDefault();
    }, true);
  }
});
