# LayoutFix v1.0.23

This release addresses the specific capture and translator failures observed in the September support logs and introduces verified GitHub updates. It does not claim universal compatibility with every text field.

## Fixed

- In Chromium/Electron, a transient non-editable `Pane/View` at the moment of a hotkey now gets a bounded retry (up to 120 ms). Only a subsequently verified writable target is accepted; secure or ambiguous targets still fail closed.
- Opening the translator no longer performs three clipboard-copy attempts before showing its window. The translator is prepared on the UI thread and reused, reducing the measured warm-window opening to 197–225 ms in the verified E2E scenario.
- The default translator shortcut is now `Ctrl+Alt+Space`, freeing Chrome's `Ctrl+Shift+T` reopen-tab shortcut. Only the old default shortcut migrates; customized shortcuts are preserved.
- The settings sidebar is explicitly repainted when switching tabs. EN/RU/UK snapshots at 150% now verify that all seven navigation entries are actually painted, not merely positioned correctly.
- Diagnostic logs can be directed to an absolute local, mapped-drive or UNC folder. An unavailable network folder falls back to the private local log path without blocking a hotkey.

## GitHub updates

- The application checks the latest stable release in the background and downloads the matching installed or portable asset. It verifies the GitHub SHA-256 digest, version and package boundaries before offering an update in the tray and About page.
- Apply a prepared update with **Restart and update**, or let it apply at the next launch. Portable replacement performs a production smoke test and restores the previous installation directory if that test fails.
- This release includes `LayoutFix_Setup.exe` and `LayoutFix_Portable_win-x64.zip`, each with an adjacent SHA-256 checksum. The existing v1.0.22 application has no built-in updater, so the first upgrade to v1.0.23 must be performed manually; subsequent stable releases can use the in-app updater.

## Verification and limitations

- 621 automated Core, Windows component and integration tests passed. The isolated portable-update E2E replaced a real published build and ran its production smoke test. Startup lifecycle preserved the user profile, autostart and clipboard; six General/About EN/RU/UK snapshots at 150% passed.
- The September WavePC failures were diagnosed from logs made **before** these fixes. There is no post-upgrade log from that PC yet, so recurrence there cannot be ruled out. If an unsafe/unsupported target does not expose a verifiable editable selection, LayoutFix deliberately does not modify it.
- Adobe canvas text without a verifiable selection, the broader Office/Electron matrix, Windows 10, actual RDP reconnect, mixed-DPI monitors and live billable online translation remain unverified or unsupported as detailed in `READINESS.md`. Automatic correction remains opt-in; manual correction is the primary verified workflow.
- The installer is intentionally unsigned. Windows SmartScreen may warn about an unknown publisher; verify the downloaded checksum before the first manual install.
