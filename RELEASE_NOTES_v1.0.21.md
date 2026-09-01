# LayoutFix v1.0.21

This release makes manual layout correction substantially more reliable in Chromium and in the verified native filename/name fields of Adobe applications.

## Fixed

- Manual correction in supported Chrome and Edge fields now prefers exact UI Automation text capture and verification when available, avoiding unnecessary clipboard retries and intermittent first-press failures.
- Fast duplicate shortcut events are coalesced across the operation-completion boundary, preventing a second transaction and spurious `LF-HK-004` messages.
- Premiere Pro native New Project and inline-name fields use a bounded, verified paste adapter instead of synchronous cross-process text replacement.
- Photoshop Save As and supported Premiere/After Effects native fields no longer receive a post-replacement Windows layout-switch message that could leave Adobe unresponsive.
- Clipboard snapshots are restored after verified Adobe paste operations, including supported complex Chromium and OLE formats.
- Manual correction diagnostics now distinguish capture, target validation, replacement, verification and timing without logging typed or selected text.

## Performance

- The hotkey path avoids redundant clipboard work for directly readable controls.
- The verified browser matrix completed with internal action latency between 70 and 227 ms; Adobe direct replacement verification completed between 59 and 267 ms in the live release-candidate runs.

## Verification

- 571/571 automated tests passed: 198 Core, 215 Windows component and 158 integration tests.
- The isolated Edge/Chrome compatibility matrix passed 14/14 cases, covering `input`, `textarea`, `contenteditable`, caret fallback, held modifiers, duplicate hotkeys and sibling keyboard layouts.
- Premiere Pro 2026 live tests passed `ghbdtn → привет`, `руддщ → hello`, ordinary `Scroll Lock` without a prior selection, New Project creation and repeated `Ctrl+S`; Premiere remained responsive.
- Photoshop 2026 Save As passed ordinary `Scroll Lock` without a prior selection and completed a real PSD save; Photoshop remained responsive.
- Published smoke and startup lifecycle tests verified isolated profiles, warmed dictionaries, operational hooks, unchanged autostart and clipboard state, and clean shutdown.

## Upgrade notes

- Existing settings, dictionaries, translation history and diagnostic preferences are preserved by the installer update path.
- Automatic correction remains disabled by default. Manual correction is the primary verified workflow.
- In supported Adobe native fields, LayoutFix corrects the text but intentionally does not switch the Windows input language afterward; this avoids the Adobe hang reproduced during v1.0.21 testing.

## Known limitations

- LayoutFix cannot inject input into an elevated application from a non-elevated process; both applications must run at the same integrity level.
- Text editing directly on Adobe canvas surfaces remains unsupported when Adobe does not expose a verifiable editable selection.
- Windows 10, real RDP reconnect, mixed-DPI multi-monitor and the broader Office/Electron compatibility matrix remain external test gates.

The installer is intentionally unsigned and is published as a normal release with an adjacent SHA-256 checksum.
