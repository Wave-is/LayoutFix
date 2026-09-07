# LayoutFix v1.0.22

This focused maintenance release fixes previous-word layout correction in Chromium controls that expose an inconsistent UI Automation selection.

## Fixed

- `Scroll Lock` previous-word correction no longer stops with `LF-HK-004` when a verified writable Chromium field reports UI Automation support but returns an empty selection after `Ctrl+Shift+Left`.
- LayoutFix now uses a bounded, isolated clipboard copy for that exact fallback path and restores the complete clipboard snapshot afterward.
- The selected source text is verified again through the same fallback immediately before replacement, so a changed or collapsed selection is still rejected safely.

## Verification

- 572/572 automated tests passed: 198 Core, 215 Windows component and 159 integration tests.
- A dedicated regression reproduces a permanently empty UI Automation selection and verifies capture, exact pre-replacement comparison, replacement and clipboard restoration.
- Three cold isolated Chrome previous-word runs completed on the first hotkey press in 187–246 ms, with internal action latency of 166–175 ms.
- Secure-target and multi-format Windows clipboard E2E tests passed.
- The self-contained portable build passed its smoke test and isolated startup lifecycle test.

## Upgrade notes

- Existing settings, dictionaries, translation history and diagnostic preferences are preserved by the installer update path.
- Automatic correction remains disabled by default. Manual correction is the primary verified workflow.
- In supported Adobe native fields, LayoutFix corrects the text but intentionally does not switch the Windows input language afterward; this avoids the Adobe hang reproduced during v1.0.21 testing.

## Known limitations

- LayoutFix cannot inject input into an elevated application from a non-elevated process; both applications must run at the same integrity level.
- Text editing directly on Adobe canvas surfaces remains unsupported when Adobe does not expose a verifiable editable selection.
- Windows 10, real RDP reconnect, mixed-DPI multi-monitor and the broader Office/Electron compatibility matrix remain external test gates.

The installer is intentionally unsigned and is published as a normal release with an adjacent SHA-256 checksum.
