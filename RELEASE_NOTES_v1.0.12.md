# LayoutFix v1.0.12 — Release Candidate

This release candidate turns the accumulated LayoutFix work into a bounded, testable Windows release. Manual layout correction is the primary supported workflow. Automatic correction remains opt-in and conservative.

## Highlights

- Safer text replacement: foreground window, focused control, selection, editability, process integrity, clipboard preservation, and target responsiveness are checked before input is injected.
- More reliable background operation: keyboard-hook callbacks only enqueue work, session unlock/reconnect recovery is bounded, and the offline translation model runs in an isolated worker process.
- Durable user data: settings, translation history, diagnostics, migrations, and retries are designed to fail without silently overwriting recoverable user data.
- More conservative automatic layout detection: 27 bundled dictionaries load on demand; ambiguous RU/UK words and common technical, protocol, format, IDE, and CLI tokens stay unchanged unless confidence is strong.
- Guarded translation: optional Qwen/Qwen2.5 and ALMA profiles reject unsupported or structurally damaged output. Numbers, dates, code, links, Markdown structure, and selected identity cases have dedicated integrity checks.
- Improved Windows compatibility: the verified matrix includes native Win32 text controls, Notepad, WordPad, Chrome, Edge, and narrow inline-rename fields in After Effects and Premiere Pro.
- Localized settings and diagnostics are available in English, Russian, and Ukrainian.

## Safety defaults

- Automatic correction is disabled by default.
- IDEs, terminals, shells, remote-desktop clients, and known-problematic Adobe targets are excluded from automatic correction by default; manual hotkeys remain available where the target passes the safety checks.
- Online translation is disabled by default, requires the user's own billable Google Cloud Translation API key, and stores that key in Windows Credential Manager.
- Translation history and diagnostic logging are disabled by default. Diagnostic output excludes typed text, clipboard contents, API keys, custom replacements, process names, and absolute paths.
- A normal process cannot inject input into an elevated target. Run LayoutFix and the target application at the same integrity level.

## Upgrade notes

- Installing v1.0.12 over an older version preserves supported user settings and applies schema migrations with safety defaults for IDE, Adobe, terminal, shell, RDP, and Citrix processes.
- The release contains `LayoutFix_Setup.exe` and `LayoutFix_Setup.exe.sha256`. Verify the checksum before installation.
- The installer is not Authenticode-signed yet. Windows SmartScreen may therefore display an unknown-publisher warning. For that reason this build is published as a pre-release, not as the stable channel.

## Known limitations

- Authenticode code signing remains an open stable-release gate.
- The current physical compatibility evidence comes from the available Windows 11 environment. Clean-current Windows 11 and Windows 10 release matrices are still required for the stable channel.
- Microsoft Office and a clean VS Code/Electron host were not available for the full physical compatibility matrix.
- After Effects and Premiere Pro inline rename are supported only through narrow verified adapters. Text on Photoshop, Premiere Pro, and After Effects canvases is intentionally rejected until a safe selection-aware adapter exists.
- Elevated targets, real RDP reconnect, multiple monitors, and mixed-DPI configurations have fail-closed safeguards and diagnostics but still need physical matrix validation.
- Offline translation quality gates cover a controlled corpus, not every sentence or language pair. Unsupported or weak results are rejected rather than inserted.
- Live online translation has contract tests but no billable production smoke test without a user-provided API key.

## Verification

The repository release workflow builds and tests the tagged source, validates the installer version and checksum, verifies the published payload and privacy/runtime-isolation contract, and publishes this version as a GitHub pre-release. Detailed evidence and all remaining gates are maintained in [READINESS.md](READINESS.md).
