[CmdletBinding()]
param(
    [ValidateRange(1, 10)]
    [int]$Runs = 2,

    [ValidateRange(30, 180)]
    [int]$StartupTimeoutSeconds = 120,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$photoshopPaths = @(
    'C:\Program Files\Adobe\Adobe Photoshop 2026\Photoshop.exe'
    'C:\Program Files\Adobe\Adobe Photoshop 2025\Photoshop.exe'
)
$photoshopPath = @($photoshopPaths | Where-Object { Test-Path -LiteralPath $_ }) |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($photoshopPath)) {
    throw "Photoshop is not installed at a supported path: $($photoshopPaths -join ', ')"
}

$fixture = Join-Path $PSScriptRoot 'fixtures\PhotoshopSaveDialogFixture.jsx'
$e2eProject = Join-Path $workspace 'tests\LayoutFix.WindowsE2E\LayoutFix.WindowsE2E.csproj'
$e2eDirectory = Join-Path $workspace `
    'tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64'
$e2eExecutable = Join-Path $e2eDirectory 'LayoutFix.WindowsE2E.exe'
$e2eResult = Join-Path $e2eDirectory 'windows-e2e-result.txt'
if (-not (Test-Path -LiteralPath $fixture)) {
    throw "Photoshop Save As fixture is missing: $fixture"
}
if (@(Get-Process -Name Photoshop -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Photoshop is already running. Refusing to touch an existing user session.'
}

if (-not $NoBuild) {
    $dotnet = Join-Path $workspace '.dotnet\dotnet.exe'
    & $dotnet build $e2eProject -c Release -r win-x64 --no-restore `
        -p:TreatWarningsAsErrors=true /nodeReuse:false
    if ($LASTEXITCODE -ne 0) {
        throw "Windows E2E build failed with exit code $LASTEXITCODE."
    }
}
if (-not (Test-Path -LiteralPath $e2eExecutable)) {
    throw "Windows E2E executable is missing: $e2eExecutable"
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class LayoutFixPhotoshopInput
{
    private const byte VkControl = 0x11;
    private const byte VkShift = 0x10;
    private const byte VkS = 0x53;
    private const uint KeyUp = 0x0002;
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);

    [DllImport("user32.dll")]
    private static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);

    public static bool OpenSaveAs(IntPtr window)
    {
        ShowWindowAsync(window, SwRestore);
        var foreground = GetForegroundWindow();
        var callerThread = GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(window, out _);
        var attachedForeground = foregroundThread != 0 && foregroundThread != callerThread &&
            AttachThreadInput(callerThread, foregroundThread, true);
        var attachedTarget = targetThread != 0 && targetThread != callerThread &&
            targetThread != foregroundThread && AttachThreadInput(callerThread, targetThread, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
            Thread.Sleep(300);
        }
        finally
        {
            if (attachedTarget)
                AttachThreadInput(callerThread, targetThread, false);
            if (attachedForeground)
                AttachThreadInput(callerThread, foregroundThread, false);
        }

        if (GetForegroundWindow() != window)
            return false;

        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkShift, 0, 0, UIntPtr.Zero);
        keybd_event(VkS, 0, 0, UIntPtr.Zero);
        keybd_event(VkS, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkShift, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero);
        return true;
    }
}
'@

function Get-ProcessWindows {
    param([Parameter(Mandatory)][int]$ProcessId)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    return @(
        [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            $condition) | Where-Object {
                try {
                    $_.Current.NativeWindowHandle -ne 0 -and
                        -not $_.Current.IsOffscreen
                }
                catch {
                    $false
                }
            }
    )
}

function Find-PhotoshopSaveDialog {
    param([Parameter(Mandatory)][int]$ProcessId)

    foreach ($window in (Get-ProcessWindows -ProcessId $ProcessId)) {
        try {
            $current = $window.Current
            if ($current.ProcessId -ne $ProcessId -or
                ($current.ClassName -ne '#32770' -and
                 $current.Name -notmatch 'Save|Сохран')) {
                continue
            }

            $edits = $window.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Edit)))
            $filename = @($edits | Where-Object {
                try {
                    $edit = $_.Current
                    $pattern = $null
                    $edit.ProcessId -eq $ProcessId -and
                        $edit.NativeWindowHandle -ne 0 -and
                        $edit.AutomationId -eq '1001' -and
                        -not $edit.IsPassword -and
                        $_.TryGetCurrentPattern(
                            [System.Windows.Automation.ValuePattern]::Pattern,
                            [ref]$pattern) -and
                        -not ([System.Windows.Automation.ValuePattern]$pattern).Current.IsReadOnly
                }
                catch {
                    $false
                }
            })
            if ($filename.Count -eq 1) {
                return @($window, $filename[0])
            }
        }
        catch {
        }
    }
    return @()
}

function Find-PhotoshopMainWindow {
    param([Parameter(Mandatory)][int]$ProcessId)

    return @(
        Get-ProcessWindows -ProcessId $ProcessId | Where-Object {
            try {
                $current = $_.Current
                $current.ProcessId -eq $ProcessId -and
                    $current.ClassName -eq 'Photoshop' -and
                    $current.NativeWindowHandle -ne 0 -and
                    $current.Name -match 'LayoutFix_Save_Dialog_E2E'
            }
            catch {
                $false
            }
        }
    )
}

function Get-PhotoshopWindowDiagnostics {
    param([Parameter(Mandatory)][int]$ProcessId)

    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($window in (Get-ProcessWindows -ProcessId $ProcessId)) {
        try {
            $current = $window.Current
            $safeName = ($current.Name -replace '[^\p{L}\p{N} ._()\-]', '_')
            $lines.Add(
                "window class=$($current.ClassName) type=$($current.ControlType.ProgrammaticName) " +
                "handle=0x$('{0:X}' -f $current.NativeWindowHandle) name=$safeName")
            $edits = $window.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Edit)))
            foreach ($editElement in $edits) {
                try {
                    $edit = $editElement.Current
                    $valuePatternObject = $null
                    $hasValue = $editElement.TryGetCurrentPattern(
                        [System.Windows.Automation.ValuePattern]::Pattern,
                        [ref]$valuePatternObject)
                    $readOnly = $hasValue -and
                        ([System.Windows.Automation.ValuePattern]$valuePatternObject).Current.IsReadOnly
                    $safeEditName = ($edit.Name -replace '[^\p{L}\p{N} ._()\-]', '_')
                    $lines.Add(
                        "edit automationId=$($edit.AutomationId) class=$($edit.ClassName) " +
                        "handle=0x$('{0:X}' -f $edit.NativeWindowHandle) password=$($edit.IsPassword) " +
                        "valuePattern=$hasValue readOnly=$readOnly name=$safeEditName")
                }
                catch {
                    $lines.Add("edit metadata unavailable type=$($_.Exception.GetType().Name)")
                }
            }
        }
        catch {
            $lines.Add("window metadata unavailable type=$($_.Exception.GetType().Name)")
        }
    }
    return $lines -join [Environment]::NewLine
}

function Close-VerifiedSaveDialog {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Dialog,
        [Parameter(Mandatory)]
        [int]$ProcessId,
        [Parameter(Mandatory)]
        [int]$ExpectedHandle
    )

    $current = $Dialog.Current
    if ($current.ProcessId -ne $ProcessId -or
        $current.NativeWindowHandle -ne $ExpectedHandle) {
        throw 'Photoshop Save As dialog identity changed; refusing to close it.'
    }

    $cancelCondition = New-Object System.Windows.Automation.OrCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            '2')),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            'Cancel')),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            'Отмена')))
    $buttons = $Dialog.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $cancelCondition)
    foreach ($button in $buttons) {
        try {
            $pattern = $null
            if ($button.Current.ProcessId -eq $ProcessId -and
                $button.Current.IsEnabled -and
                $button.TryGetCurrentPattern(
                    [System.Windows.Automation.InvokePattern]::Pattern,
                    [ref]$pattern)) {
                ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
                return
            }
        }
        catch {
        }
    }
    throw 'The verified Photoshop Save As dialog did not expose a Cancel button.'
}

for ($run = 1; $run -le $Runs; $run++) {
    if (@(Get-Process -Name Photoshop -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Photoshop appeared before controlled run $run; refusing to continue."
    }

    $startedProcess = $null
    try {
        $startedProcess = Start-Process `
            -FilePath $photoshopPath `
            -ArgumentList @('-r', $fixture) `
            -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
        $mainWindow = @()
        while ([DateTime]::UtcNow -lt $deadline -and -not $startedProcess.HasExited) {
            $mainWindow = @(Find-PhotoshopMainWindow -ProcessId $startedProcess.Id)
            if ($mainWindow.Count -eq 1 -and $startedProcess.Responding) {
                break
            }
            Start-Sleep -Milliseconds 500
        }
        if ($mainWindow.Count -ne 1) {
            $diagnostics = Get-PhotoshopWindowDiagnostics -ProcessId $startedProcess.Id
            throw (
                "Photoshop did not expose the controlled unsaved document within " +
                "$StartupTimeoutSeconds seconds.`n$diagnostics")
        }

        $mainWindowCurrent = $mainWindow[0].Current
        if ($mainWindowCurrent.ProcessId -ne $startedProcess.Id -or
            $mainWindowCurrent.NativeWindowHandle -eq 0) {
            throw 'Photoshop controlled document identity changed before Save As.'
        }
        if (-not [LayoutFixPhotoshopInput]::OpenSaveAs(
            [IntPtr]$mainWindowCurrent.NativeWindowHandle)) {
            throw 'Could not bring the controlled Photoshop document to the foreground.'
        }

        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        $target = @()
        while ([DateTime]::UtcNow -lt $deadline -and -not $startedProcess.HasExited) {
            $target = @(Find-PhotoshopSaveDialog -ProcessId $startedProcess.Id)
            if ($target.Count -eq 2 -and $startedProcess.Responding) {
                break
            }
            Start-Sleep -Milliseconds 500
        }
        if ($target.Count -ne 2) {
            $diagnostics = Get-PhotoshopWindowDiagnostics -ProcessId $startedProcess.Id
            throw (
                "Photoshop did not expose a verified Save As filename field within " +
                "45 seconds. No screen image was captured.`n$diagnostics")
        }

        $dialog = $target[0]
        $filename = $target[1]
        $dialogHandle = [int]$dialog.Current.NativeWindowHandle
        $filenameHandle = [int]$filename.Current.NativeWindowHandle
        $valuePatternObject = $null
        if (-not $filename.TryGetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern,
            [ref]$valuePatternObject)) {
            throw 'Photoshop filename field lost ValuePattern before setup.'
        }
        $valuePattern = [System.Windows.Automation.ValuePattern]$valuePatternObject
        $valuePattern.SetValue('TEST')
        $filename.SetFocus()
        if ($valuePattern.Current.Value -ne 'TEST') {
            throw 'Photoshop filename field did not accept the TEST sentinel.'
        }

        & $e2eExecutable `
            '--existing-text-app-test' `
            ([string]$dialogHandle) `
            ([string]$filenameHandle) `
            'ScrollLock' `
            '145'
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            $details = if (Test-Path -LiteralPath $e2eResult) {
                Get-Content -LiteralPath $e2eResult -Raw
            }
            else {
                'The Windows E2E harness did not produce a result log.'
            }
            throw "Photoshop Save As E2E failed with exit code $exitCode.`n$details"
        }

        $startedProcess.Refresh()
        if (-not $startedProcess.Responding) {
            throw 'Photoshop stopped responding after the Scroll Lock correction.'
        }
        if ($valuePattern.Current.Value -ne 'TEST') {
            throw 'Photoshop filename field was not restored after verification.'
        }

        Close-VerifiedSaveDialog `
            -Dialog $dialog `
            -ProcessId $startedProcess.Id `
            -ExpectedHandle $dialogHandle
        $closeDeadline = [DateTime]::UtcNow.AddSeconds(10)
        while ([DateTime]::UtcNow -lt $closeDeadline) {
            if (@(Find-PhotoshopSaveDialog -ProcessId $startedProcess.Id).Count -eq 0) {
                break
            }
            Start-Sleep -Milliseconds 200
        }

        "photoshop_save_dialog run=$run result=pass hotkey=ScrollLock field=native-edit process=responding"
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
}

if (@(Get-Process -Name Photoshop -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Controlled Photoshop Save As E2E left a Photoshop process running.'
}

"photoshop_save_dialog=pass cold_runs=$Runs hotkey=ScrollLock clipboard=preserved"
