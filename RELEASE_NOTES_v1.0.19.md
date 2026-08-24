# LayoutFix v1.0.19

This bugfix release rebuilds the Settings layout for high-DPI Windows displays and replaces the old square tray-language badge.

## Improvements

- Replaced fragile hand-positioned rows with shared DPI-aware layout containers across General, Languages, Exceptions and Auto-Translate.
- Aligned every settings toggle and language selector to a consistent column and vertically centered labels, status text and controls.
- Rebuilt installed-language cards so the language badge, name, keyboard-layout subtitle, active state and toggle cannot overlap or clip.
- Reflowed process-exception actions; the safety-default restore action now has its own row and remains usable in narrower windows.
- Fixed clipped Dictionary and localized Auto-Translate controls, including the longer Ukrainian model-status caption.
- Preserved secondary and warning colors when switching themes instead of repainting those labels as primary text.
- Replaced the outlined square `RU`/`EN` tray icon with a rounded, readable state badge; manual, automatic and hook-recovery states remain blue, orange and red, and flag icons use the same rounded treatment.

## Verification

- 561/561 automated tests passed: 197 Core, 210 Windows components and 154 integration tests.
- A 21-case Settings snapshot matrix passed for all seven tabs in English, Russian and Ukrainian.
- Layout assertions now fail on clipped child controls, truncated button captions, off-center toggles and inconsistent toggle or language-selector columns.
- Tray-icon rendering passed at 16, 24 and 32 pixels for text, flags and all three operating states.
- A real Computer Use pass clicked through all seven tabs on the measured 4K, 150% DPI (144 DPI), PerMonitorV2 Windows session.
- Settings persistence, recovery, migration, diagnostics privacy, 128 concurrent saves, 120 concurrent history operations, 2,000 concurrent log writes and autostart registry restoration passed.
- Solution build, Windows E2E harness build and the NuGet vulnerability audit completed cleanly.

## Known limitations

- Text editing directly on Photoshop, Premiere Pro and After Effects canvases remains unsupported when Adobe exposes only a pane without a verifiable selection range. LayoutFix rejects those targets instead of replacing an entire layer or guessing.
- Windows 10, elevated applications, real RDP reconnect, mixed-DPI multi-monitor and a broader Office/Electron matrix remain external compatibility gates.
- Automatic correction in Adobe applications remains disabled by default; the verified Photoshop Save As path remains available through the manual layout-correction hotkey.

The installer is intentionally unsigned and is published as a normal release with an adjacent SHA-256 checksum.
