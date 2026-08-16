(function () {
    function isOwnedFixtureProject(project) {
        if (!project || project.file) {
            return false;
        }
        if (project.numItems === 0) {
            return true;
        }
        if (project.numItems !== 1) {
            return false;
        }

        var existingComp = project.item(1);
        if (!(existingComp instanceof CompItem) ||
            existingComp.name !== "LayoutFix_E2E_Editable" ||
            existingComp.numLayers !== 1) {
            return false;
        }

        var existingLayer = existingComp.layer(1);
        var existingSourceText = existingLayer.property("ADBE Text Properties")
            .property("ADBE Text Document");
        return existingLayer.name === "TEST" &&
            existingSourceText.value.text === "ghbdtn";
    }

    app.beginSuppressDialogs();
    try {
        if (app.project) {
            if (!isOwnedFixtureProject(app.project)) {
                throw new Error(
                    "Refusing to replace a project that is not the isolated LayoutFix E2E fixture.");
            }
            app.project.close(CloseOptions.DO_NOT_SAVE_CHANGES);
        }
        if (!app.newProject()) {
            throw new Error("Could not create the isolated LayoutFix E2E project.");
        }

        var comp = app.project.items.addComp(
            "LayoutFix_E2E_Editable",
            1920,
            1080,
            1.0,
            5.0,
            30.0);
        var textLayer = comp.layers.addText("ghbdtn");
        textLayer.name = "TEST";
        textLayer.property("ADBE Transform Group")
            .property("ADBE Position")
            .setValue([960, 540]);

        if (!comp.openInViewer()) {
            throw new Error("Could not open the isolated LayoutFix E2E composition.");
        }
    }
    finally {
        app.endSuppressDialogs(false);
    }
}());
