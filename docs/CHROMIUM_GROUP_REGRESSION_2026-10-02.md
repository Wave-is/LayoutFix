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

Final production-source automated suite: 640/640 passed (221 Core, 252 Windows,
167 integration). Published portable smoke: exit 0; 27 dictionaries, 22 locales,
translation worker present, no settings/history/logs/PDBs in the package.

The targeted three-round browser run completed 24 cases: all 18 correction cases
visibly succeeded on the first press, but three exceeded the 400 ms internal
latency gate (430, 489 and 427 ms). Five read-only cases rejected correction and
preserved both text and clipboard. One Edge read-only case lost foreground focus
and was invalid, not a safety pass. Its log shows a no-change action in the other
application. The harness now refuses browser input without exact foreground
ownership, and the negative fixture requires a focused Group before the shortcut.
Read-only confirmation evidence is kept separately under
`artifacts/qa/chromium-group-readonly-confirmation`.

The negative harness was also corrected to wait for completion rather than
sampling diagnostics after exactly one second; this avoids falsely reporting a
still-running rejection as a safety failure. It tolerates the asynchronous log
file not existing yet and does not synchronously flush on the WinForms thread
while waiting. Final exact-focused Chrome and Edge negative tests both passed:
rejected=True, unchanged=True, clipboardPreserved=True. Evidence:
`artifacts/qa/chromium-group-readonly-verified/*-message-pump.log`.

The portable payload is available at
`\\192.168.1.8\ShareAgents\layoutfix\v1.0.24-preview.1`.
Main executable, app assembly, Infrastructure and Core SHA-256 hashes match the
local published payload. ProductVersion identifies production-source commit
`01aa910682ffed5bf58390e540c0af03bb7a31a3`; later commits only harden the test harness
and document evidence, and do not alter the delivered production code.

The initial expanded broad matrix corrected an ordinary Chrome input on the
first press in 463 ms, but failed the stricter internal action latency gate:
449 ms versus 400 ms. This failure is preserved in
`artifacts/qa/browser-20261002-095249/chrome-input-latency-failure.log` and is not
classified as a passing matrix. It is separate from the persistent Group refusal.

This is a portable testing build, not a new public release or a claim of universal
compatibility. The original failing editor on WavePC still needs post-build
confirmation; the logs identify its accessibility shape, not its page/DOM.
