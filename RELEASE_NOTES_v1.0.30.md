# LayoutFix 1.0.30

Shift+Scroll Lock now offers a clipboard fallback for supported selections outside Steam, including focused Chromium groups without an editable accessibility range. Where direct replacement cannot be verified, click the notification to copy the corrected result, then paste it yourself. Capturing the selection does not replace the previous clipboard contents.

Embedded controls hosted by another process are checked against the focused Windows window. Unverified embedded writes are avoided; passwords and unrelated windows are not valid fallback targets. Ordinary verified writable fields retain in-place correction.

Install or update using the per-user Windows x64 installer, LayoutFix_Setup.exe. It is unsigned; compare its SHA-256 against the adjacent checksum. No changes to automatic correction defaults.
