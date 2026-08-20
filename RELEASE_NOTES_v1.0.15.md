# LayoutFix v1.0.15

This release focuses on reliable manual layout correction and actionable, privacy-safe diagnostics.

## Improvements

- Added opt-in **Diagnostic logs for testing** and **Show diagnostic popup messages** settings. Logs capture operation IDs, safe failure stages/reasons, target application/version and control capabilities, but never selected text, clipboard contents, window titles, API keys, custom replacements, or absolute paths.
- Fixed manual correction of CRLF multiline selections: `\r\n` is now injected as one Enter while partial-input accounting remains exact.
- Long selections (128+ characters) now use a bounded clipboard-paste transaction and restore every original clipboard format even on cancellation or failure. This removes slow or truncated character-by-character replacement.
- Added a narrow Chromium fallback for Chrome/Edge text fields whose accessibility provider reports only the focused root pane. It requires matching foreground/focused window class and PID, a non-password pane, integrity checks, and a final focus recheck.
- Expanded real Windows coverage for Scroll Lock and configurable hotkeys, Edit/RichEdit controls, Chrome/Edge input/textarea/contenteditable, punctuation, numbers, emoji, tabs, CRLF, Unicode wrappers and long selections.

## Verification

- 542/542 automated tests passed.
- 250/250 physical manual-correction stress iterations passed with diagnostics enabled.
- Final release regression passed 12 manual text cases, 12 partial-input/race cases, 3 selection-ownership cases, 6 Edit/RichEdit cases, 6 browser targets and 8 configurable-hotkey cases including Scroll Lock.
- The installer remains intentionally unsigned and is published as a normal release with an adjacent SHA-256 checksum.

## Known limitation

The reported Photoshop Save As filename scenario is recorded but not claimed as verified. In isolated Photoshop 2026 runs, File → Save/Save As did not expose a filename field to the automated fixture, although Photoshop remained responsive and the owned test process was cleaned up. The bounded `Run-PhotoshopSaveDialogE2E.ps1` gate preserves a safe screenshot on failure for continued investigation.
