#target photoshop

app.displayDialogs = DialogModes.NO;
app.bringToFront();

app.documents.add(
    64,
    64,
    72,
    "LayoutFix_Save_Dialog_E2E",
    NewDocumentMode.RGB,
    DocumentFill.WHITE);

// The Windows E2E driver invokes Photoshop's real Ctrl+Shift+S shortcut after
// this startup script returns. Restore dialogs so that user path is observable.
app.displayDialogs = DialogModes.ALL;
