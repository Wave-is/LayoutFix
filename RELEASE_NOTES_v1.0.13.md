# LayoutFix v1.0.13 — Release Candidate

This release fixes real Settings-window layout bugs and closes several localization gaps discovered while reviewing the interface end to end.

## Fixes

- **Text no longer overlaps or hides behind controls.** Several Settings rows positioned a control at a fixed pixel offset next to an auto-sized label. Any translation longer than the original English string (a very common case across the app's 22 supported UI languages) could render with its tail hidden behind the neighboring switch, combo box, or text field. Every affected row (General, Translate, and the hotkey-name popup) now positions its control relative to the label's actual rendered width, and grows to fit instead of silently clipping.
- **The About tab could not be fully scrolled.** Its content (logo, description, diagnostics report, copy button) was placed with fixed coordinates and no scroll container, so parts of it became unreachable at smaller window heights, higher DPI, or larger Windows text scaling. It's now wrapped in the same auto-scrolling panel used by the other tabs.
- **The hotkey-editor dialog was hardcoded to Russian** regardless of the selected UI language, and showed the internal action key (e.g. `FixLayoutSelected`) instead of its localized name. Both are now fully localized (English/Russian/Ukrainian) and the dialog sizes itself to fit its content.
- **The tray icon's context menu and tooltip were never localized** — always English regardless of the configured UI language. They now use the same localization service as the rest of the app.
- Several Translate-tab strings ("Translation Language 1/2/3", the translation-history toggle) were hardcoded in English instead of going through localization.
- The Settings window and sidebar are slightly larger to give longer translations more room.

## Improvements

- Changing the interface language now offers to restart LayoutFix immediately instead of just showing an informational message.
- The About tab has a "View on GitHub" link.

## Verification

All 528 existing unit/integration tests pass unchanged. The positioning fix was additionally verified by measuring the real rendered width of every affected label (via `Control.GetPreferredSize`) using the actual English, Russian, and Ukrainian strings shipped in `locales/*.json`, confirming no overlap in any of the three fully localized languages. The repository's CI pipeline renders and snapshots the Settings window across all seven tabs in English, Russian, and Ukrainian on a clean Windows runner for every push.
