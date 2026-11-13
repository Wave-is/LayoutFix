Download **LayoutFix_Setup.exe** for a normal Windows installation. Existing installed copies can update in place; no uninstall is needed. The ZIP is for existing portable copies.

- Manual layout correction now recognizes additional writable Chromium editors and handles explicit selections and the word around the caret more reliably.
- During an installed update, Setup waits for LayoutFix to exit before replacing files. If installation fails, the verified package remains available for manual retry instead of triggering a restart loop.
- LayoutFix rechecks for releases during long sessions and verifies the installer checksum before launching it. Setup writes its update log to `%LOCALAPPDATA%\LayoutFix\Updates\last-installer.log`.

The installer is per-user and unsigned. An intermittent administrator prompt reported on another PC was not reproduced in the Windows upgrade test.
