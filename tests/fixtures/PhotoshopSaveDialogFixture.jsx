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

// Defer File > Save until this startup JSX has returned. For an unsaved
// document Photoshop opens the Save As dialog through this normal user path.
// Dialogs are suppressed only while the fixture creates its owned document;
// leaving DialogModes.NO active here prevents the very Save As UI this test
// needs to exercise.
app.displayDialogs = DialogModes.ALL;
app.scheduleTask(
    'app.runMenuItem(charIDToTypeID("save"));',
    2000,
    false);
