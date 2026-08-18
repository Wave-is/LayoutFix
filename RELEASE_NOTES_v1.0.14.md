# LayoutFix v1.0.14 — Release Candidate

Follow-up to v1.0.13: fixes the same class of layout bug at Windows display scaling above 100%.

## Fix

- **Settings, Translator and hotkey-editor windows overlapped controls at 125–150%+ Windows display scaling** (reported on a 4K monitor). The app is per-monitor DPI-aware, so text renders proportionally larger in device pixels as scaling increases, but these hand-built windows position every control with literal pixel coordinates that never adjusted for that — v1.0.13's fix computed control positions from actual measured label widths, but that alone doesn't help once the *whole window's* pixel budget (row spacing, margins, overall size) stays fixed while the DPI-scaled text inside it grows. All three windows now declare an explicit 96 DPI design baseline (`AutoScaleMode.Dpi`), so the framework uniformly rescales the entire built layout to match the real monitor scale instead of leaving it fixed at design-time pixel counts.

## Verification

All 528 tests pass in Debug and Release. The repository's CI snapshot suite (renders and captures the Settings window across every tab in English, Russian and Ukrainian) was re-run locally and confirms byte-for-byte equivalent rendering at 100% scaling — this is the standard, Microsoft-documented mechanism for exactly this scenario, applied as a uniform one-time transform, so it does not change anything at the 96 DPI baseline. Actual confirmation at 150%+ scaling depends on a real high-DPI display, which isn't available in this environment; verification there is pending user confirmation.
