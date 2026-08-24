# LayoutFix v1.0.18

This bugfix release closes the reported Photoshop Save As manual-correction failure without weakening LayoutFix's general text-target safety rules.

## Improvements

- Added a dedicated clipboard-free adapter for the Photoshop 2026 Save As filename field.
- The adapter is limited to the exact Photoshop process, common Save dialog and focused writable native `Edit` control; it revalidates focus, selection and the final value around replacement.
- Selected filename text is read with bounded native messages and replaced only inside the proven selection. LayoutFix no longer relies on clipboard capture or a potentially slow Adobe accessibility provider in this path.
- Improved the Photoshop E2E focus driver for the modern Windows Save dialog while retaining exact process, control, enabled, password and native-focus checks.

## Verification

- 554/554 automated tests passed: 197 Core, 203 Windows components and 154 integration tests.
- The isolated Photoshop 2026 (27.9) Save As E2E passed 3/3 cold runs with physical Scroll Lock, exact `TEST → ghbdtn → привет → TEST`, a responsive Photoshop process and preserved clipboard.
- A separate final Computer Use run against the publish build repeated `ghbdtn → привет` in the real Save As filename field; the privacy-safe trace confirmed the direct adapter path and no clipboard transaction.
- Solution build, Windows E2E harness build and self-contained `win-x64` publish completed with zero warnings and errors.

## Known limitations

- Text editing directly on Photoshop, Premiere Pro and After Effects canvases remains unsupported when Adobe exposes only a pane without a verifiable selection range. LayoutFix rejects those targets instead of replacing an entire layer or guessing.
- Windows 10, elevated applications, real RDP reconnect, mixed-DPI multi-monitor and a broader Office/Electron matrix remain external compatibility gates.
- Automatic correction in Adobe applications remains disabled by default; the verified Photoshop Save As path is for the manual layout-correction hotkey.

The installer is intentionally unsigned and is published as a normal release with an adjacent SHA-256 checksum.
