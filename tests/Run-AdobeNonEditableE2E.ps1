param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Photoshop', 'Premiere', 'AfterEffects')]
    [string]$Application,

    [ValidateRange(1, 20)]
    [int]$Runs = 3,

    [ValidateRange(30, 180)]
    [int]$StartupTimeoutSeconds = 120,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$e2eProject = Join-Path $workspace 'tests\LayoutFix.WindowsE2E\LayoutFix.WindowsE2E.csproj'
$e2eExecutable = Join-Path $workspace 'tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64\LayoutFix.WindowsE2E.exe'
$catalog = @{
    Photoshop = @{
        Path = 'C:\Program Files\Adobe\Adobe Photoshop 2025\Photoshop.exe'
        ProcessName = 'Photoshop'
        WindowName = 'Adobe Photoshop'
    }
    Premiere = @{
        Path = 'C:\Program Files\Adobe\Adobe Premiere Pro 2025\Adobe Premiere Pro.exe'
        ProcessName = 'Adobe Premiere Pro'
        WindowName = 'Adobe Premiere Pro'
    }
    AfterEffects = @{
        Path = 'C:\Program Files\Adobe\Adobe After Effects 2025\Support Files\AfterFX.exe'
        ProcessName = 'AfterFX'
        WindowName = 'Adobe After Effects'
    }
}
$target = $catalog[$Application]

if (-not (Test-Path -LiteralPath $target.Path)) {
    throw "$Application is not installed at the expected path: $($target.Path)"
}

$existing = @(Get-Process -Name $target.ProcessName -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
    throw "$Application is already running. Refusing to touch an existing user session."
}

if (-not $NoBuild) {
    $dotnet = Join-Path $workspace '.dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) {
        $dotnet = 'dotnet'
    }
    & $dotnet build $e2eProject -c Release -r win-x64 --no-restore -p:TreatWarningsAsErrors=true /nodeReuse:false
    if ($LASTEXITCODE -ne 0) {
        throw "Windows E2E build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $e2eExecutable)) {
    throw "Windows E2E executable is missing: $e2eExecutable"
}

Add-Type -AssemblyName UIAutomationClient

if ($Application -eq 'AfterEffects') {
    Add-Type -AssemblyName System.Drawing
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LayoutFixAdobeE2ENative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    public const uint MouseLeftDown = 0x0002;
    public const uint MouseLeftUp = 0x0004;
}
'@
}

function Save-AfterEffectsRecoveryDiagnostic {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Dialog
    )

    $bounds = $Dialog.Current.BoundingRectangle
    $width = [int]$bounds.Width
    $height = [int]$bounds.Height
    if ($width -le 0 -or $height -le 0) {
        return $null
    }

    $path = Join-Path ([IO.Path]::GetTempPath()) `
        'LayoutFix-AfterEffects-unknown-recovery.png'
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(
            [int]$bounds.X,
            [int]$bounds.Y,
            0,
            0,
            $bitmap.Size)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        return $path
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Invoke-AfterEffectsRecoveryClick {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Dialog,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedWidth,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedHeight,

        [Parameter(Mandatory = $true)]
        [int]$RelativeX,

        [Parameter(Mandatory = $true)]
        [int]$RelativeY
    )

    $handle = [IntPtr]$Dialog.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero -or
        $Dialog.Current.ProcessId -ne $startedProcess.Id -or
        $Dialog.Current.Name -ne 'UI_MessageBox' -or
        $Dialog.Current.ClassName -ne '#32770') {
        throw 'After Effects recovery dialog identity changed; refusing UI input.'
    }

    $rect = New-Object LayoutFixAdobeE2ENative+Rect
    if (-not [LayoutFixAdobeE2ENative]::GetWindowRect($handle, [ref]$rect)) {
        throw 'Could not read the After Effects recovery dialog bounds.'
    }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -ne $ExpectedWidth -or $height -ne $ExpectedHeight) {
        throw "Unexpected After Effects recovery dialog geometry: ${width}x${height}."
    }

    $cursor = New-Object LayoutFixAdobeE2ENative+Point
    if (-not [LayoutFixAdobeE2ENative]::GetCursorPos([ref]$cursor)) {
        throw 'Could not preserve the current mouse cursor position.'
    }

    try {
        $foregroundDeadline = [DateTime]::UtcNow.AddSeconds(3)
        do {
            [void][LayoutFixAdobeE2ENative]::BringWindowToTop($handle)
            [void][LayoutFixAdobeE2ENative]::SetForegroundWindow($handle)
            if ([LayoutFixAdobeE2ENative]::GetForegroundWindow() -eq $handle) {
                break
            }

            $foregroundWindow = [LayoutFixAdobeE2ENative]::GetForegroundWindow()
            $foregroundProcessId = [uint32]0
            $foregroundThread = [LayoutFixAdobeE2ENative]::GetWindowThreadProcessId(
                $foregroundWindow,
                [ref]$foregroundProcessId)
            $currentThread = [LayoutFixAdobeE2ENative]::GetCurrentThreadId()
            $attached = $false
            try {
                if ($foregroundThread -ne 0 -and $foregroundThread -ne $currentThread) {
                    $attached = [LayoutFixAdobeE2ENative]::AttachThreadInput(
                        $currentThread,
                        $foregroundThread,
                        $true)
                }
                [void][LayoutFixAdobeE2ENative]::BringWindowToTop($handle)
                [void][LayoutFixAdobeE2ENative]::SetForegroundWindow($handle)
            }
            finally {
                if ($attached) {
                    [void][LayoutFixAdobeE2ENative]::AttachThreadInput(
                        $currentThread,
                        $foregroundThread,
                        $false)
                }
            }
            Start-Sleep -Milliseconds 100
        }
        while ([DateTime]::UtcNow -lt $foregroundDeadline)

        if ([LayoutFixAdobeE2ENative]::GetForegroundWindow() -ne $handle) {
            throw 'After Effects recovery dialog did not become foreground; refusing UI input.'
        }

        if (-not [LayoutFixAdobeE2ENative]::SetCursorPos(
            $rect.Left + $RelativeX,
            $rect.Top + $RelativeY)) {
            throw 'Could not position the mouse over the verified recovery action.'
        }
        Start-Sleep -Milliseconds 100
        [LayoutFixAdobeE2ENative]::mouse_event(
            [LayoutFixAdobeE2ENative]::MouseLeftDown,
            0,
            0,
            0,
            [UIntPtr]::Zero)
        [LayoutFixAdobeE2ENative]::mouse_event(
            [LayoutFixAdobeE2ENative]::MouseLeftUp,
            0,
            0,
            0,
            [UIntPtr]::Zero)
    }
    finally {
        [void][LayoutFixAdobeE2ENative]::SetCursorPos($cursor.X, $cursor.Y)
    }
}

$startedProcess = $null
try {
    $startedProcess = Start-Process -FilePath $target.Path -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $window = $null
    $lastHandledRecoveryDialog = $null
    $unknownRecoveryDialogKey = $null
    $unknownRecoveryDialogFirstSeen = [DateTime]::MinValue
    $afterEffectsRecoveryPhase = 'initial'
    while ([DateTime]::UtcNow -lt $deadline -and -not $startedProcess.HasExited) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $startedProcess.Id)
        $candidates = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            $condition)
        $visibleWindows = @($candidates | Where-Object {
            try {
                $_.Current.NativeWindowHandle -ne 0 -and
                    -not $_.Current.IsOffscreen
            }
            catch {
                $false
            }
        })
        $startedProcess.Refresh()
        $window = @($visibleWindows | Where-Object {
            try {
                if ($Application -eq 'AfterEffects') {
                    $_.Current.NativeWindowHandle -eq $startedProcess.MainWindowHandle -and
                        $_.Current.ClassName -like 'AE_CApplication_*' -and
                        $startedProcess.MainWindowTitle -like "*$($target.WindowName)*"
                }
                else {
                    $_.Current.Name -like "*$($target.WindowName)*"
                }
            }
            catch {
                $false
            }
        } | Select-Object -First 1)
        $blockingDialog = @($visibleWindows | Where-Object {
            try {
                $_.Current.Name -eq 'UI_MessageBox'
            }
            catch {
                $false
            }
        } | Select-Object -First 1)
        if ($blockingDialog.Count -gt 0) {
            if ($Application -ne 'AfterEffects') {
                throw "$Application startup is blocked by an Adobe modal dialog (UI_MessageBox). " +
                    'Resolve the application startup/recovery prompt before counting this compatibility gate.'
            }

            $dialogHandle = [long]$blockingDialog[0].Current.NativeWindowHandle
            $dialogBounds = $blockingDialog[0].Current.BoundingRectangle
            $dialogKey = "${dialogHandle}:$([int]$dialogBounds.Width)x$([int]$dialogBounds.Height)"
            if ($dialogKey -ne $lastHandledRecoveryDialog) {
                if ($afterEffectsRecoveryPhase -eq 'initial' -and
                    [int]$dialogBounds.Width -eq 546 -and
                    [int]$dialogBounds.Height -eq 348) {
                    $unknownRecoveryDialogKey = $null
                    Invoke-AfterEffectsRecoveryClick `
                        -Dialog $blockingDialog[0] `
                        -ExpectedWidth 546 `
                        -ExpectedHeight 348 `
                        -RelativeX 461 `
                        -RelativeY 296
                    $lastHandledRecoveryDialog = $dialogKey
                    $afterEffectsRecoveryPhase = 'normal-continue'
                    'aftereffects_recovery action=continue-normal result=accepted'
                }
                elseif ($afterEffectsRecoveryPhase -eq 'initial' -and
                    [int]$dialogBounds.Width -eq 739 -and
                    [int]$dialogBounds.Height -eq 348) {
                    $unknownRecoveryDialogKey = $null
                    Invoke-AfterEffectsRecoveryClick `
                        -Dialog $blockingDialog[0] `
                        -ExpectedWidth 739 `
                        -ExpectedHeight 348 `
                        -RelativeX 132 `
                        -RelativeY 297
                    $lastHandledRecoveryDialog = $dialogKey
                    $afterEffectsRecoveryPhase = 'safe-mode-selected'
                    'aftereffects_recovery action=safe-mode result=accepted'
                }
                elseif ($afterEffectsRecoveryPhase -eq 'safe-mode-selected' -and
                    [int]$dialogBounds.Width -eq 492 -and
                    [int]$dialogBounds.Height -eq 484) {
                    $unknownRecoveryDialogKey = $null
                    Invoke-AfterEffectsRecoveryClick `
                        -Dialog $blockingDialog[0] `
                        -ExpectedWidth 492 `
                        -ExpectedHeight 484 `
                        -RelativeX 426 `
                        -RelativeY 432
                    $lastHandledRecoveryDialog = $dialogKey
                    $afterEffectsRecoveryPhase = 'safe-mode-confirmed'
                    'aftereffects_recovery action=confirm result=accepted'
                }
                else {
                    if ($unknownRecoveryDialogKey -ne $dialogKey) {
                        $unknownRecoveryDialogKey = $dialogKey
                        $unknownRecoveryDialogFirstSeen = [DateTime]::UtcNow
                    }
                    elseif ([DateTime]::UtcNow - $unknownRecoveryDialogFirstSeen -ge
                        [TimeSpan]::FromSeconds(15)) {
                        $diagnosticPath = Save-AfterEffectsRecoveryDiagnostic `
                            -Dialog $blockingDialog[0]
                        throw "After Effects startup exposed a stable unknown recovery dialog: " +
                            "$([int]$dialogBounds.Width)x$([int]$dialogBounds.Height). " +
                            "Diagnostic: $diagnosticPath"
                    }
                }
            }
            $window = $null
            Start-Sleep -Milliseconds 500
            continue
        }
        $lastHandledRecoveryDialog = $null
        $unknownRecoveryDialogKey = $null
        if ($window.Count -gt 0) {
            $startedProcess.Refresh()
            if ($startedProcess.Responding) {
                break
            }
        }
        $window = $null
        Start-Sleep -Milliseconds 500
    }

    if ($null -eq $window -or $window.Count -eq 0) {
        throw "$Application did not expose a responding top-level window within $StartupTimeoutSeconds seconds."
    }

    $handle = [long]$window[0].Current.NativeWindowHandle
    for ($run = 1; $run -le $Runs; $run++) {
        & $e2eExecutable --noneditable-target-test $handle
        if ($LASTEXITCODE -ne 0) {
            throw "$Application non-editable E2E failed on run $run with exit code $LASTEXITCODE."
        }
        "adobe_noneditable application=$Application run=$run result=pass"
    }
}
finally {
    if ($null -ne $startedProcess) {
        try {
            if (-not $startedProcess.HasExited) {
                [void]$startedProcess.CloseMainWindow()
                if (-not $startedProcess.WaitForExit(15000)) {
                    $startedProcess.Kill($true)
                    [void]$startedProcess.WaitForExit(10000)
                }
            }
        }
        catch {
        }
        $startedProcess.Dispose()
    }
}
