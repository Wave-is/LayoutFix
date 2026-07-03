Неообходимо создать программу для исправления ошибок раскладок. Некий аналог ПунтоСвитчер.
Пользователь не программист, общение ведем на русском языке.
# 📋 LayoutFix — Technical Roadmap for AI Agent

## 1. Role and Context

You are a Senior C#/.NET Developer with deep expertise in WinAPI, desktop applications, and Clean Architecture. You will develop a Windows desktop utility called **LayoutFix** from scratch.

**Working protocol:**
- Work strictly phase-by-phase. Do NOT proceed to the next phase without explicit approval.
- At the start of each phase, propose an implementation plan (classes, WinAPI, tests).
- After approval, write the code.
- If you encounter an architectural dead-end — STOP and ask the user. Do NOT invent broken code.
- Write unit tests for all Core logic.
- All public methods must have XML documentation.
- Follow SOLID, Clean Architecture, Dependency Injection.

---

## 2. Hard Constraints (Non-Negotiable)

| Constraint | Requirement |
|---|---|
| Platform | Windows 10 / 11 only |
| Framework | .NET 8, C# 12, Windows Forms |
| Architecture | Clean Architecture, SOLID, DI via `Microsoft.Extensions.DependencyInjection` |
| Offline | No network calls, no telemetry, no ads, no auto-updates |
| Dictionaries | None. Only character mapping tables |
| Auto-detection | None at the OS level. Only text-based heuristic inside the app |
| Auto-switching | None. The app NEVER switches keyboard layout automatically |
| Portability | Single folder, no installer, no registry writes (except optional autorun) |
| RAM | ≤ 30 MB |
| CPU idle | ~0% |
| Startup | < 300 ms |
| Dependencies | Minimal NuGet. Prefer hand-written P/Invoke |
| Unicode | Full support everywhere |

---

## 3. Technology Stack

| Component | Choice |
|---|---|
| Runtime | .NET 8 LTS |
| UI | Windows Forms (tray-only app, one settings window) |
| Language | C# 12 (primary constructors, file-scoped namespaces, collection expressions) |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Serialization | `System.Text.Json` (built-in) |
| Logging | Custom `FileLogger` (no Serilog/NLog) |
| Testing | xUnit + Moq |
| P/Invoke | `System.Runtime.InteropServices` (manual) |

**Forbidden:**
- `Vanara.*` (unless explicitly approved for a specific dead-end)
- `Serilog`, `NLog`, `log4net`
- `Humanizer`
- `Hardcodet.NotifyIcon.Wpf` (WPF-only)
- Any UI framework other than WinForms

---

## 4. Project Structure

```
LayoutFix.sln
├── src/
│   ├── LayoutFix/                    (WinForms exe, entry point)
│   │   ├── Program.cs
│   │   ├── AppHost.cs                (DI composition root)
│   │   ├── UI/
│   │   │   ├── TrayManager.cs
│   │   │   ├── Forms/
│   │   │   │   └── SettingsForm.cs
│   │   │   └── Controls/
│   │   │       └── HotkeyInputControl.cs
│   │   ├── Infrastructure/
│   │   │   ├── WinApi/
│   │   │   │   ├── User32.cs
│   │   │   │   ├── Ole32.cs
│   │   │   │   ├── Kernel32.cs
│   │   │   │   └── InputStructures.cs
│   │   │   ├── Services/
│   │   │   │   ├── SettingsService.cs
│   │   │   │   ├── LoggingService.cs
│   │   │   │   └── StartupService.cs
│   │   │   └── Json/
│   │   │       └── LayoutJsonContext.cs
│   │   ├── layouts/
│   │   │   ├── en-US.json
│   │   │   ├── ru-RU.json
│   │   │   └── uk-UA.json
│   │   └── LayoutFix.csproj
│   │
│   ├── LayoutFix.Core/               (Class library, framework-agnostic)
│   │   ├── Interfaces/
│   │   │   ├── ILayoutConverter.cs
│   │   │   ├── IClipboardManager.cs
│   │   │   ├── IHotkeyManager.cs
│   │   │   ├── ISettingsService.cs
│   │   │   ├── IKeyboardLayoutManager.cs
│   │   │   ├── ITextTransformer.cs
│   │   │   ├── INumberToTextConverter.cs
│   │   │   └── IUndoManager.cs
│   │   ├── Models/
│   │   │   ├── Layout.cs
│   │   │   ├── HotkeyAction.cs
│   │   │   ├── HotkeyCombo.cs
│   │   │   ├── HotkeyScheme.cs
│   │   │   ├── AppSettings.cs
│   │   │   └── UndoEntry.cs
│   │   ├── Services/
│   │   │   ├── LayoutConverter.cs
│   │   │   ├── KeyboardLayoutManager.cs
│   │   │   ├── TextTransformer.cs
│   │   │   ├── TransliterationService.cs
│   │   │   ├── NumberToTextConverter.cs
│   │   │   └── UndoManager.cs
│   │   └── LayoutFix.Core.csproj
│   │
│   └── LayoutFix.Infrastructure/     (Class library, OS integration)
│       ├── Clipboard/
│       │   ├── ClipboardManager.cs
│       │   ├── Win32ClipboardSnapshot.cs
│       │   └── InputSimulator.cs
│       ├── Hotkeys/
│       │   ├── HotkeyManager.cs
│       │   ├── LowLevelKeyboardHook.cs
│       │   └── SequenceDetector.cs
│       ├── Layouts/
│       │   └── WindowsLayoutProvider.cs
│       └── LayoutFix.Infrastructure.csproj
│
└── tests/
    └── LayoutFix.Tests/
        ├── LayoutConverterTests.cs
        ├── AutoDetectionTests.cs
        ├── TransliterationTests.cs
        ├── NumberToTextTests.cs
        └── LayoutFix.Tests.csproj
```

---

## 5. Data Formats

### 5.1. Layout JSON (`layouts/*.json`)

```json
{
  "code": "ru-RU",
  "displayName": "Русский",
  "keys": {
    "q": "й", "w": "ц", "e": "у", "r": "к", "t": "е", "y": "н",
    "u": "г", "i": "ш", "o": "щ", "p": "з", "[": "х", "]": "ъ",
    "a": "ф", "s": "ы", "d": "в", "f": "а", "g": "п", "h": "р",
    "j": "о", "k": "л", "l": "д", ";": "ж", "'": "э",
    "z": "я", "x": "ч", "c": "с", "v": "м", "b": "и", "n": "т",
    "m": "ь", ",": "б", ".": "ю", "`": "ё",
    "/": ".", "?": ",", "\\": "э"
  },
  "shiftKeys": {
    "1": "!", "2": "\"", "3": "№", "4": ";", "5": "%",
    "6": ":", "7": "?", "8": "*", "9": "(", "0": ")",
    "-": "_", "=": "+", "`": "Ё",
    "@": "\"", "#": "№", "$": ";", "^": ":", "&": "?"
  }
}
```

### 5.2. Settings JSON (`settings.json`)

```json
{
  "version": 1,
  "hotkeyScheme": "PuntoClassic",
  "customHotkeys": {},
  "layoutOrder": ["en-US", "ru-RU", "uk-UA"],
  "useWindowsLayoutList": true,
  "scrollLockMode": "Smart",
  "soundEnabled": false,
  "notificationsEnabled": true,
  "autorunMode": "None",
  "loggingEnabled": false,
  "transliterationTable": "GOST"
}
```

### 5.3. Log format (`logs/YYYY-MM-DD.log`)

```
[2026-07-03 14:22:01.123] [INFO ] Hotkey pressed: ScrollLock
[2026-07-03 14:22:01.145] [INFO ] Auto-detected direction: en-US -> ru-RU
[2026-07-03 14:22:01.201] [WARN ] Clipboard restore failed, format CF_HTML lost
```

**Critical:** Logs MUST NOT contain user text. Only metadata (hotkey names, layout codes, operation durations, error codes).

---

## 6. Required WinAPI

### 6.1. Clipboard (Ole32)

```csharp
[DllImport("ole32.dll")] static extern int OleGetClipboard(out IDataObject ppDataObject);
[DllImport("ole32.dll")] static extern int OleSetClipboard(IDataObject pDataObj);
[DllImport("ole32.dll")] static extern int OleFlushClipboard();
```

### 6.2. Keyboard Hook (User32)

```csharp
const int WH_KEYBOARD_LL = 13;
const int WM_KEYDOWN = 0x0100;
const int WM_KEYUP = 0x0101;
const int WM_SYSKEYDOWN = 0x0104;

delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
[DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
[DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
```

### 6.3. Input Simulation (User32)

```csharp
[DllImport("user32.dll")] static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
[DllImport("user32.dll")] static extern short GetKeyState(int nVirtKey);
```

### 6.4. Keyboard Layouts (User32)

```csharp
[DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);
[DllImport("user32.dll")] static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);
[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
[DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
```

### 6.5. Module Handle (Kernel32)

```csharp
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string lpModuleName);
```

---

## 7. Risk Matrix and Mitigations

### 🔴 Critical Risks

**R1. Clipboard formatting loss**
- Symptom: User copies formatted table from Word → after LayoutFix operation → plain text only.
- Root cause: Using `Clipboard.GetText()` instead of `IDataObject`.
- Mitigation: Use `OleGetClipboard` → clone all formats (`CF_UNICODETEXT`, `CF_HTML`, `CF_RTF`, `CF_BITMAP`, `CF_DIB`, `CF_HDROP`, custom) → `OleSetClipboard`.
- Test: Copy from Word with table+image → run operation → paste back → verify formatting preserved.

**R2. Scroll Lock handling**
- Symptom: `RegisterHotKey` ignores Scroll Lock or works only in foreground.
- Root cause: Scroll Lock is a toggle key, handled specially by Windows.
- Mitigation: Use `WH_KEYBOARD_LL` only. Intercept `WM_KEYDOWN`/`WM_KEYUP`, check `vkCode == 0x91`.
- Pitfall: Forgetting `UnhookWindowsHookEx` on exit freezes Windows. Always wrap in `try/finally` in `Dispose()`.

**R3. Clipboard race condition**
- Symptom: User presses Ctrl+C right after our operation → gets stale text.
- Mitigation: Restore clipboard in the same thread as the operation. Use `SendMessageTimeout`.

### 🟡 Medium Risks

**R4. App refuses Ctrl+C**
- Symptom: 1C, terminals, games ignore emulated keystrokes.
- Mitigation: 300 ms timeout waiting for clipboard change. If unchanged → cancel operation + notification "Could not get text".

**R5. Conflict with other tools**
- Symptom: Punto Switcher / Caramba Switcher also hook ScrollLock.
- Mitigation: Detect known competitors by process name → show warning in tray.

**R6. Undo stack overflow**
- Mitigation: Cap at 50 entries. Use `LinkedList<UndoEntry>` for O(1) removal from both ends.

### 🟢 Low Risks

**R7. JSON encoding issues** → Read only UTF-8, validate on load.
**R8. DPI scaling** → `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` in `Program.cs`.

---

## 8. Phase-by-Phase Plan

### Phase 0: Environment Setup (2h)

**Goal:** Project skeleton that builds cleanly.

**Tasks:**
1. Create solution with 4 projects: `LayoutFix`, `LayoutFix.Core`, `LayoutFix.Infrastructure`, `LayoutFix.Tests`.
2. Create `Directory.Build.props`:
   ```xml
   <PropertyGroup>
     <TargetFramework>net8.0-windows</TargetFramework>
     <Nullable>enable</Nullable>
     <ImplicitUsings>enable</ImplicitUsings>
     <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
     <LangVersion>12</LangVersion>
   </PropertyGroup>
   ```
3. Add `.editorconfig` (4-space indent, UTF-8, file-scoped namespaces).
4. Add `.gitignore` for .NET.
5. Configure Release publish profile:
   ```xml
   <PublishTrimmed>true</PublishTrimmed>
   <TrimMode>partial</TrimMode>
   <SelfContained>true</SelfContained>
   <DebugType>none</DebugType>
   ```

**DoD:**
- [ ] `dotnet build` passes with 0 warnings
- [ ] `dotnet test` runs (0 tests)
- [ ] Folder structure matches §4

---

### Phase 1: Foundation and DI (6h)

**Goal:** App launches, shows tray icon, DI container works.

**Tasks:**
1. Define all interfaces in `LayoutFix.Core/Interfaces/` (see §4).
2. Define models in `LayoutFix.Core/Models/`:
   - `Layout` (Code, DisplayName, Keys, ShiftKeys)
   - `HotkeyAction` (enum: FixLayout, ChangeCase, Transliterate, NumberToText, PastePlain, SwitchLayout, ConvertToLayoutN, SwitchToLayoutN, Undo)
   - `HotkeyCombo` (VirtualKey + modifiers, with `ToString()` and `Parse()`)
   - `HotkeyScheme` (preset name + dictionary of combo→action)
   - `AppSettings` (mirrors `settings.json`)
3. Implement `SettingsService`:
   - Load/save `settings.json` via `System.Text.Json` with source generator.
   - Create default file on first run.
4. Implement DI composition root in `AppHost.cs`.
5. Implement minimal `TrayManager` with `NotifyIcon` and context menu:
   - "Fix selected text"
   - "Fix last word"
   - separator
   - "Settings..."
   - "About..."
   - separator
   - "Exit"
6. Implement `Program.cs`:
   - `SingleInstance` check via `Mutex`.
   - Hide console, run message loop.
   - Graceful shutdown on "Exit".

**DoD:**
- [ ] App launches, tray icon visible
- [ ] Context menu opens and all items clickable
- [ ] "Exit" terminates process cleanly
- [ ] `settings.json` created with defaults on first run
- [ ] Second instance shows notification and exits

---

### Phase 2: Layout Engine (8h)

**Goal:** Converter works without UI or clipboard.

**Tasks:**
1. Implement `KeyboardLayoutManager`:
   - `LoadAll()` reads `layouts/*.json`.
   - `GetInstalledWindowsLayouts()` calls `GetKeyboardLayoutList`, extracts LCID from HKL, maps to loaded layouts by culture code.
   - `GetLayoutOrder()` returns user-ordered list from settings.
2. Implement `LayoutConverter`:
   - `ConvertTo(string text, Layout target, Layout source)` — character-by-character mapping preserving case.
   - `AutoConvert(string text, IReadOnlyList<Layout> activeLayouts)`:
     - For each layout, count how many characters of `text` belong to its character set.
     - Source = layout with max score. Target = first different layout in user order.
     - If top-2 scores differ by < 20% of text length → return `null` (ambiguous).
3. Implement `TextTransformer`:
   - `ChangeCase(string text)` cycles: lower → UPPER → Title → iNVERSE → lower.
4. Implement `TransliterationService` with GOST 7.79-2000 table (RU↔EN bidirectional).
5. Implement `NumberToTextConverter` for RU/EN/UA:
   - Handle 0..999,999,999,999.
   - Respect grammatical gender ("одна тысяча", "два миллиона").
   - Respect plural forms ("1 тысяча", "2 тысячи", "5 тысяч").
6. Write xUnit tests:
   - `ghbdtn` → `привет`
   - `Ghbdtn` → `Привет`
   - `руддщ` → `hello`
   - `привет, мир!` → `ghbdtn, vbh!`
   - Auto-detect for `ghbdtn` → `привет`
   - Auto-detect returns `null` for `abc` (ambiguous)
   - Auto-detect returns `null` for mixed `hello мир`
   - Case cycling produces 4 distinct forms
   - Transliteration `Privet` ↔ `Привет`
   - Numbers: 0, 1, 25, 125, 1000, 2000, 5000, 1000000, 123456789

**DoD:**
- [ ] All JSON layouts load correctly
- [ ] All unit tests pass
- [ ] Auto-detection works for clear cases
- [ ] Auto-detection returns null for ambiguous/mixed text

---

### Phase 3: Clipboard and OS Integration (12h) ⚠️ Most Complex

**Goal:** Transparently swap buffer content without losing any format.

**Tasks:**
1. Implement `Win32ClipboardSnapshot`:
   - `Capture()`: call `OleGetClipboard`, enumerate formats via `IEnumFORMATETC`, read each via `IDataObject.GetData`, store as `byte[]` keyed by format ID.
   - `Restore()`: build new `DataObject`, populate with saved bytes, call `OleSetClipboard`.
   - Implement `IDisposable` to free global memory handles.
2. Implement `InputSimulator`:
   - `SendInput` wrapper for key combinations.
   - `Copy()`: Ctrl+C.
   - `Paste()`: Ctrl+V.
   - `SelectLastWord()`: Ctrl+Shift+Left.
   - `SelectAll()`: Ctrl+A.
   - Small delays (50-100 ms) between keystrokes for app responsiveness.
3. Implement `ClipboardManager`:
   - `ExecuteWithClipboardPreservationAsync<T>(Func<string, Task<T>> action)`:
     1. Capture snapshot.
     2. `SelectLastWord()` + `Copy()`.
     3. Wait up to 300 ms for clipboard change (poll with `IsClipboardFormatAvailable`).
     4. If no change → throw `ClipboardException`.
     5. Read text, invoke `action`.
     6. Set result text, `Paste()`.
     7. Wait 100 ms.
     8. Restore snapshot in `finally`.
   - `GetSelectedTextAsync()`: same as above but returns text without pasting back.
4. Implement `WindowsLayoutSwitcher`:
   - `SwitchToNext()`: cycle through user-ordered list via `ActivateKeyboardLayout`.
   - `SwitchToIndex(int n)`: activate n-th layout.
   - Target the foreground window via `GetForegroundWindow` + `SendMessage(WM_INPUTLANGCHANGEREQUEST)`.
5. Write integration tests (require manual verification):
   - Copy formatted table from Word → operate → paste back → formatting preserved.
   - Copy HTML from browser → operate → paste back → HTML preserved.
   - Copy image → operate → paste back → image preserved.
   - Copy file from Explorer → operate → paste back → file preserved.
   - Operate in 1C-like app → graceful failure with notification.

**DoD:**
- [ ] Unit tests for snapshot (mocked WinAPI)
- [ ] Integration tests pass for Word, Excel, browser, Explorer
- [ ] If app refuses Ctrl+C → operation cancelled, no notification spam, no clipboard corruption
- [ ] Snapshot correctly restores all formats including custom

---

### Phase 4: Hotkey System (8h)

**Goal:** Reliable global hotkeys including Scroll Lock and sequences.

**Tasks:**
1. Implement `LowLevelKeyboardHook`:
   - Install `WH_KEYBOARD_LL` in `Initialize()`.
   - Callback reads `vkCode` and modifiers (via `GetKeyState` for Shift/Ctrl/Alt/Win).
   - Return `(IntPtr)1` to swallow handled keys, else `CallNextHookEx`.
   - `Dispose()` MUST call `UnhookWindowsHookEx` in `finally`.
2. Implement `HotkeyManager`:
   - Load scheme from settings.
   - Map `HotkeyCombo` → `HotkeyAction`.
   - Raise `HotkeyPressed` event.
3. Implement `SequenceDetector` for combos like `Shift+ScrollLock+1`:
   - Buffer recent key events with timestamps.
   - Drop events older than 500 ms.
   - Match patterns:
     - `ScrollLock` → FixLayout
     - `Shift+ScrollLock` → ChangeCase
     - `Alt+ScrollLock` → Transliterate
     - `Ctrl+ScrollLock` → NumberToText
     - `Shift+ScrollLock+N` (N=1..9) → ConvertToLayoutN
     - `Alt+ScrollLock+N` (N=1..9) → SwitchToLayoutN
     - `Win+ScrollLock` → PastePlain
4. Implement preset schemes:
   - `PuntoClassic` (default, uses Scroll Lock)
   - `Logitech` (uses F13-F20 or user-defined)
   - `Custom` (fully user-defined)
5. Wire hotkey events to Core services via `AppHost`.

**DoD:**
- [ ] Scroll Lock triggers layout fix
- [ ] Shift+Scroll Lock cycles case
- [ ] Alt+Scroll Lock transliterates
- [ ] Ctrl+Scroll Lock converts number to text
- [ ] Shift+Scroll Lock+1..9 converts to n-th layout
- [ ] Alt+Scroll Lock+1..9 switches Windows layout to n-th
- [ ] Custom hotkeys from settings work
- [ ] Hook is released on exit (verify via Process Explorer — no orphan hooks)

---

### Phase 5: UI and Settings (8h)

**Goal:** Full-featured settings window, notifications.

**Tasks:**
1. Implement `SettingsForm` (WinForms, tabbed):
   - **General tab:**
     - Hotkey scheme dropdown (PuntoClassic / Logitech / Custom)
     - Scroll Lock mode (FixLastWord / SwitchLayout / Smart)
     - Checkboxes: autorun, sound, notifications, logging
     - Autorun mode (None / StartupFolder / Registry)
   - **Layouts tab:**
     - `DataGridView` or `ListBox` with user-ordered layouts.
     - Buttons ▲/▼ for reordering + Drag-and-Drop.
     - Checkbox "Use Windows layout list" (syncs with OS).
     - "Add custom layout" button → opens file picker for JSON.
   - **Hotkeys tab:**
     - List of actions with `HotkeyInputControl` next to each.
     - Conflict detection (highlight duplicates in red).
   - **About tab:** version, license, link.
2. Implement `HotkeyInputControl`:
   - Custom `TextBox` subclass.
   - `OnKeyDown` records pressed keys, shows preview.
   - `OnKeyUp` finalizes combo when all modifiers released.
   - Validates: at least one modifier required (except for F13+).
3. Implement notification system:
   - Use `NotifyIcon.ShowBalloonTip` as fallback.
   - Prefer custom borderless form near tray (WinForms, fade-in/out, 2s timeout).
   - Messages: "Layout fixed", "Case changed", "Transliteration done", "Could not detect direction", "Could not get text".
4. Implement `StartupService`:
   - `StartupFolder`: create/remove `.lnk` in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`.
   - `Registry`: write/remove `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\LayoutFix`.
5. Implement `LoggingService`:
   - File per day in `logs\YYYY-MM-DD.log`.
   - Levels: Info, Warn, Error.
   - Never log user text.
   - Thread-safe via `Channel<string>` + background writer.

**DoD:**
- [ ] Settings window opens from tray
- [ ] All settings save and apply immediately (no restart)
- [ ] Drag-and-Drop reordering works
- [ ] Hotkey recording works, conflicts highlighted
- [ ] Notifications show/hide based on setting
- [ ] Autorun works in both modes
- [ ] Logs written only when enabled, no user text inside

---

### Phase 6: Advanced Features (8h)

**Goal:** Undo, paste-plain, polish.

**Tasks:**
1. Implement `UndoManager`:
   - `LinkedList<UndoEntry>` capped at 50.
   - `Record(originalText, newText, rollbackAction)`.
   - `TryUndo()` pops and invokes rollback.
   - Rollback = restore original text to clipboard + paste.
2. Implement paste-plain:
   - Read `CF_UNICODETEXT` only from clipboard.
   - Set back as plain text.
   - Paste.
3. Wire all actions through `UndoManager.Record` before execution.
4. Add "Undo last action" hotkey (default: `Ctrl+Z` when app is focused, or configurable).
5. Implement graceful error handling:
   - All WinAPI calls wrapped in try/catch.
   - All exceptions logged.
   - User sees notification, not crash.

**DoD:**
- [ ] Undo reverts last operation even if clipboard was changed since
- [ ] Paste-plain strips all formatting
- [ ] No unhandled exceptions (verify via stress test: 100 rapid operations)
- [ ] Undo stack never exceeds 50 entries

---

### Phase 7: Testing and Release (6h)

**Goal:** Production-ready portable build.

**Tasks:**
1. Execute test plan:

| # | Test Case | Expected |
|---|---|---|
| 1 | Copy `ghbdtn` in Notepad, press ScrollLock | `привет` inserted |
| 2 | Copy formatted Word table with image, press ScrollLock | Formatting preserved |
| 3 | Copy HTML from browser, press ScrollLock | HTML preserved |
| 4 | Press ScrollLock without selection | Last word fixed |
| 5 | Press Shift+ScrollLock on `hello` | Cycles: `HELLO` → `Hello` → `hELLO` → `hello` |
| 6 | Press Shift+ScrollLock+2 | Text converted to 2nd layout in list |
| 7 | Press Alt+ScrollLock+3 | Windows layout switched to 3rd |
| 8 | Run in 1C-like app | Notification "Could not get text", clipboard untouched |
| 9 | 1-hour session, 100 operations | RAM < 30 MB, no leaks |
| 10 | Exit via tray | Hook released, clipboard intact |

2. Measure performance:
   - Startup time via `Stopwatch` in `Program.cs` → must be < 300 ms.
   - RAM via `Process.WorkingSet64` → must be < 30 MB.
   - CPU via Task Manager during idle → must be ~0%.
3. Verify no network: `netstat -ano | findstr LayoutFix` → empty.
4. Verify no registry writes (except autorun if enabled).
5. Build portable release:
   ```
   dotnet publish -c Release -r win-x64 --self-contained true -o .\publish
   ```
6. Final folder size must be < 40 MB (with self-contained runtime).
7. Write `README.md`:
   - Description
   - Installation (just unzip)
   - Default hotkeys table
   - How to add custom layout
   - How to add custom transliteration table
   - Troubleshooting

**DoD:**
- [ ] All 10 test cases pass
- [ ] Portable zip ready for distribution
- [ ] README.md complete
- [ ] Code reviewed for SOLID compliance
- [ ] All public methods have XML docs

---

## 9. Build Commands

```bash
# Debug build
dotnet build -c Debug

# Run tests
dotnet test

# Release publish (portable)
dotnet publish src/LayoutFix/LayoutFix.csproj -c Release -r win-x64 --self-contained true -o publish

# Release publish (framework-dependent, smaller)
dotnet publish src/LayoutFix/LayoutFix.csproj -c Release -r win-x64 --self-contained false -o publish-lite

# Run with metrics
dotnet run --project src/LayoutFix/LayoutFix.csproj -c Release
```

---

## 10. Interaction Protocol

1. **Start of each phase:** You propose a plan listing classes, WinAPI, tests.
2. **After user approval:** You write the code.
3. **On errors:** User sends error messages, you fix.
4. **On dead-end:** You STOP and ask. Never guess.
5. **Phase completion:** You confirm DoD checklist, user verifies, we move on.

**First action:** Confirm you understood this document. Propose the Phase 0 plan.