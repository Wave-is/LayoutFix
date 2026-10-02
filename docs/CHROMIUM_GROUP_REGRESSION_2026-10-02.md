# Chromium Group editor correction — 1.0.24-preview.1

WavePC logs from 2 October show 11 consecutive Chrome correction failures at
11:46–11:47 on 1.0.23. The keyboard shortcut was received, but capture was
rejected before conversion: focused UIA Group, TextPattern present, no
ValuePattern, `uia-target-not-editable` / `LF-HK-004`.

The previous contenteditable fixture forced `role=textbox`, so it could not
detect this regression. A new real-browser `role=group` contenteditable fixture
reproduced the old rejection (first-press E2E exit 3) before the production fix.

The fix supports only focused Chromium Group editors in the expected process,
with an enabled, focusable, non-password target and explicitly writable document
and single selection ranges. Unknown, mixed and read-only attributes are denied.
The exact verified range is read directly, and existing integrity, active-window,
input-ownership and replacement verification checks remain in place. Diagnostics
record range attributes and decision, never the selected text.

Run `pwsh -File tests/Run-ChromiumGroupRegression.ps1 -Runs 3` for first-attempt
selected text, caret/previous-word, held Shift and read-only Group tests in both
Chrome and Edge. Individual evidence is saved under `artifacts/qa/chromium-group-*`.
The normal browser compatibility matrix now includes Group editors too.

## Known verification limitation

The initial expanded broad matrix corrected an ordinary Chrome input on the
first press in 463 ms, but failed the stricter internal action latency gate:
449 ms versus 400 ms. This failure is preserved in
`artifacts/qa/browser-20261002-095249/chrome-input-latency-failure.log` and is not
classified as a passing matrix. It is separate from the persistent Group refusal.

This is a portable testing build, not a new public release or a claim of universal
compatibility. The original failing editor on WavePC still needs post-build
confirmation; the logs identify its accessibility shape, not its page/DOM.
