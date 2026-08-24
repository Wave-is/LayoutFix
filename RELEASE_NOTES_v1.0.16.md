# LayoutFix v1.0.16

This bugfix release focuses on reliable manual layout correction in real applications and correct 4K/150% Settings layout.

## Improvements

- Fixed held/repeated shortcut handling so suppressed key repeats no longer invalidate the active text transaction or dispatch duplicate corrections.
- Complex clipboard values are now cloned by supported value type and restored exactly, including bitmap and private application streams. Unknown value types still fail closed before text or clipboard is changed, and private format names remain absent from diagnostics.
- Premiere inline rename now recognizes both observed main-window classes and replaces the exact proven full or partial native `Edit` selection through a bounded direct operation without touching the clipboard.
- Manual previous-word fallback can retry the exact Adobe direct adapter while preserving focus and selection safety checks.
- Reworked Settings sizing for 4K at 150%: sidebar labels, hotkey rows, exception headings, translation privacy text, API-key controls and model actions no longer overlap.
- Added localized Windows-layout labels for Russian and Ukrainian Settings.

## Verification

- 548/548 automated tests passed: 197 Core, 203 Windows components and 148 integration tests.
- A diagnostic physical hotkey soak passed 100/100 with exact text and clipboard verification and privacy-safe logs.
- Three consecutive 12-case manual-correction matrices passed 36/36; real Win32 `Edit` and `RichEdit`, suppressed-repeat and complex-clipboard E2E gates also passed.
- Seven Russian Settings pages passed visual boundary/overlap checks at system DPI 144 (150%) on a 4K display.

## Known limitations

- The Photoshop Save As filename scenario remains unverified. In the isolated Photoshop 2026 fixture, Photoshop did not expose a distinct filename `Edit`, so LayoutFix deliberately sent no correction hotkey; the owned process was removed and no user file was touched.
- The isolated Premiere live gate did not expose a responding top-level window within its bounded startup timeout. The process was removed without sending input. The exact Premiere native-Edit profile is covered by integration tests, but this run is not claimed as a new live Adobe pass.
- Elevated targets, real RDP reconnect and mixed-DPI multi-monitor configurations remain external compatibility gates.

The installer is intentionally unsigned and is published as a normal release with an adjacent SHA-256 checksum. Automatic correction remains disabled by default; manual correction is the primary workflow.
