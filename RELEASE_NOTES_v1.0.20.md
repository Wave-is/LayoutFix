# LayoutFix v1.0.20

This bugfix release fixes intermittent manual layout correction failures reported from Chrome diagnostics.

## Fixed

- Fixed a false safety cancellation where LayoutFix counted its own handled shortcut as new input in the target document.
- Modifier-only key transitions no longer invalidate a transaction, so shortcuts such as `Shift+Scroll Lock` remain stable while Chrome completes selection capture.
- A quick duplicate press of the same shortcut is now coalesced without an `LF-HK-001` error popup; a genuinely conflicting action is still rejected safely.
- Input-change diagnostics now record expected and observed keyboard/mouse generations, without logging selected text, clipboard contents or window titles.

## Verification

- 564/564 automated tests passed: 197 Core, 212 Windows components and 155 integration tests.
- Real isolated Chrome tests passed for `input`, `textarea` and `contenteditable` fields with production clipboard capture, replacement verification and restoration.
- A dedicated Chrome `contenteditable` regression sent a second discrete shortcut during capture and verified that it was coalesced, the first correction completed, and neither `input-changed-*` nor `LF-HK-001` was emitted.
- The Edge compatibility matrix passed for the same three editable target types.
- A physical Windows hook soak completed 100/100 manual corrections with exact text and clipboard preservation.
- Suppressed-key-repeat and keyboard/mouse selection-ownership E2E gates passed.

## Known limitations

- LayoutFix cannot inject input into an elevated application from a non-elevated process; both applications must run at the same integrity level.
- Text editing directly on Adobe canvas surfaces remains unsupported when Adobe does not expose a verifiable editable selection. Photoshop Save As native text fields remain supported by the dedicated manual-correction path.
- Windows 10, real RDP reconnect, mixed-DPI multi-monitor and the broader Office/Electron compatibility matrix remain external test gates.

The installer is intentionally unsigned and is published as a normal release with an adjacent SHA-256 checksum.
