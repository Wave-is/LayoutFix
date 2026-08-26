[CmdletBinding()]
param(
    [ValidateRange(1, 20)]
    [int]$Runs = 1,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'tests\LayoutFix.WindowsE2E\LayoutFix.WindowsE2E.csproj'
$localDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

if (-not $NoBuild) {
    & $dotnet build $project -c Release -r win-x64 --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Windows E2E harness build failed with exit code $LASTEXITCODE."
    }
}

$harness = Join-Path $repoRoot 'tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64\LayoutFix.WindowsE2E.exe'
$resultPath = Join-Path (Split-Path $harness) 'windows-e2e-result.txt'
if (-not (Test-Path $harness)) {
    throw "Windows E2E harness is missing: $harness"
}

$observedLatencies = [Collections.Generic.List[int]]::new()
$observedActionLatencies = [Collections.Generic.List[int]]::new()
function Record-BrowserLatency {
    param([Parameter(Mandatory)][string]$Case)

    $details = Get-Content -LiteralPath $resultPath -Raw
    $match = [regex]::Match(
        $details,
        'browser-first-press:success=True;elapsedMs=(?<elapsed>\d+)')
    if (-not $match.Success) {
        throw "Browser case '$Case' did not emit first-press latency evidence.`n$details"
    }

    $elapsed = [int]$match.Groups['elapsed'].Value
    $observedLatencies.Add($elapsed)
    $actionMatch = [regex]::Match(
        $details,
        'browser-action-latency:success=True;elapsedMs=(?<elapsed>\d+)')
    if (-not $actionMatch.Success) {
        throw "Browser case '$Case' did not emit internal action latency evidence.`n$details"
    }
    $actionElapsed = [int]$actionMatch.Groups['elapsed'].Value
    $observedActionLatencies.Add($actionElapsed)
    Write-Output "browser_latency case=$Case visibleMs=$elapsed actionMs=$actionElapsed"
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryPrefix = [IO.Path]::TrimEndingDirectorySeparator($temporaryRoot) +
    [IO.Path]::DirectorySeparatorChar
function Remove-IsolatedBrowserProfiles {
    $profiles = @(
        Get-ChildItem `
            -LiteralPath $temporaryRoot `
            -Directory `
            -ErrorAction SilentlyContinue | Where-Object {
                $_.Name -like 'LayoutFix.EdgeE2E.*' -or
                $_.Name -like 'LayoutFix.ChromeE2E.*'
            }
    )
    foreach ($profile in $profiles) {
        $resolvedProfile = [IO.Path]::GetFullPath($profile.FullName)
        if (-not $resolvedProfile.StartsWith(
                $temporaryPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove browser E2E profile outside the temporary directory: $resolvedProfile"
        }
        $profileProcesses = @(
            Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
                $_.Name -in @('msedge.exe', 'chrome.exe') -and
                -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
                $_.CommandLine.IndexOf(
                    $resolvedProfile,
                    [StringComparison]::OrdinalIgnoreCase) -ge 0
            }
        )
        foreach ($profileProcess in $profileProcesses) {
            Stop-Process -Id $profileProcess.ProcessId -Force -ErrorAction SilentlyContinue
        }
        for ($cleanupAttempt = 1; $cleanupAttempt -le 20; $cleanupAttempt++) {
            try {
                Remove-Item -LiteralPath $resolvedProfile -Recurse -Force
                break
            } catch {
                if ($cleanupAttempt -eq 20) {
                    throw
                }
                Start-Sleep -Milliseconds 250
            }
        }
    }
}

$maximumAttempts = 1
Remove-IsolatedBrowserProfiles
foreach ($browser in @('edge', 'chrome')) {
    foreach ($target in @('input', 'textarea', 'contenteditable')) {
        for ($iteration = 1; $iteration -le $Runs; $iteration++) {
            for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
                if (Test-Path $resultPath) {
                    Remove-Item -LiteralPath $resultPath -Force
                }
                & $harness "--${browser}-test" $target
                $exitCode = $LASTEXITCODE
                if ($exitCode -eq 0) {
                    Record-BrowserLatency "$browser-$target-selected"
                    # Chromium may keep a profile-scoped utility process alive
                    # briefly after the verified window closes. Clean only the
                    # exact LayoutFix temp profiles before leak verification.
                    Remove-IsolatedBrowserProfiles
                    break
                }

                $details = if (Test-Path $resultPath) {
                    Get-Content $resultPath -Raw
                } else {
                    'The harness did not create a result log.'
                }
                Remove-IsolatedBrowserProfiles
                if ($attempt -eq $maximumAttempts) {
                    throw "$browser $target iteration $iteration/$Runs failed on attempt $attempt/$maximumAttempts with exit code $exitCode.`n$details"
                }
                Write-Warning "$browser $target iteration $iteration/$Runs attempt $attempt/$maximumAttempts failed with exit code $exitCode; retrying with a fresh isolated profile."
            }
        }
    }
}

# The Chrome support logs that led to v1.0.20 showed a second discrete hotkey
# arriving while the first contenteditable transaction was still capturing.
# Keep that exact regression in the release gate: the duplicate must be
# coalesced and must not invalidate the first action's input ownership.
for ($iteration = 1; $iteration -le $Runs; $iteration++) {
    for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
        if (Test-Path $resultPath) {
            Remove-Item -LiteralPath $resultPath -Force
        }
        $previousDiagnostics = [Environment]::GetEnvironmentVariable(
            'LAYOUTFIX_E2E_DIAGNOSTICS',
            'Process')
        $env:LAYOUTFIX_E2E_DIAGNOSTICS = '1'
        try {
            & $harness '--chrome-test' 'contenteditable' 'duplicate'
            $exitCode = $LASTEXITCODE
        } finally {
            if ($null -eq $previousDiagnostics) {
                Remove-Item Env:LAYOUTFIX_E2E_DIAGNOSTICS -ErrorAction SilentlyContinue
            } else {
                $env:LAYOUTFIX_E2E_DIAGNOSTICS = $previousDiagnostics
            }
        }
        if ($exitCode -eq 0) {
            Record-BrowserLatency 'chrome-contenteditable-duplicate'
            Remove-IsolatedBrowserProfiles
            break
        }

        $details = if (Test-Path $resultPath) {
            Get-Content $resultPath -Raw
        } else {
            'The harness did not create a result log.'
        }
        Remove-IsolatedBrowserProfiles
        if ($attempt -eq $maximumAttempts) {
            throw "chrome contenteditable duplicate iteration $iteration/$Runs failed on attempt $attempt/$maximumAttempts with exit code $exitCode.`n$details"
        }
        Write-Warning "chrome contenteditable duplicate iteration $iteration/$Runs attempt $attempt/$maximumAttempts failed with exit code $exitCode; retrying with a fresh isolated profile."
    }
}

# The primary Scroll Lock workflow must also correct the previous word when
# there is no selection. This used to spend three 750 ms clipboard timeouts
# before even attempting the fallback, which made one press look unreliable.
foreach ($target in @('input', 'textarea', 'contenteditable')) {
    for ($iteration = 1; $iteration -le $Runs; $iteration++) {
        if (Test-Path $resultPath) {
            Remove-Item -LiteralPath $resultPath -Force
        }
        & $harness '--chrome-test' $target 'caret'
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            $details = if (Test-Path $resultPath) {
                Get-Content $resultPath -Raw
            } else {
                'The harness did not create a result log.'
            }
            Remove-IsolatedBrowserProfiles
            throw "chrome $target caret iteration $iteration/$Runs failed on the first attempt with exit code $exitCode.`n$details"
        }
        Record-BrowserLatency "chrome-$target-caret"
        Remove-IsolatedBrowserProfiles
    }
}

# Shift+Scroll is dispatched while Shift is still physically down. The
# production transaction must neutralize that modifier around its private
# Ctrl+C/replacement input and finish before the user releases Shift. Keep the
# modifier down beyond the historical two-second timeout after success so this
# gate cannot pass through the old wait-for-release behavior.
for ($iteration = 1; $iteration -le $Runs; $iteration++) {
    if (Test-Path $resultPath) {
        Remove-Item -LiteralPath $resultPath -Force
    }
    & $harness '--chrome-test' 'input' 'holdshift'
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $details = if (Test-Path $resultPath) {
            Get-Content $resultPath -Raw
        } else {
            'The harness did not create a result log.'
        }
        Remove-IsolatedBrowserProfiles
        throw "chrome Shift+Scroll held-modifier iteration $iteration/$Runs failed on the first attempt with exit code $exitCode.`n$details"
    }
    Record-BrowserLatency 'chrome-input-held-shift'
    $heldEvidence = Get-Content -LiteralPath $resultPath -Raw
    if (-not $heldEvidence.Contains('modifiersHeld=True') -or
        -not $heldEvidence.Contains('browser-hotkey:shift-held-after-success-ms=2200')) {
        Remove-IsolatedBrowserProfiles
        throw "chrome Shift+Scroll held-modifier iteration $iteration/$Runs did not emit the required evidence.`n$heldEvidence"
    }
    Remove-IsolatedBrowserProfiles
}

# Russian and Ukrainian share many physical-key outputs. The old fallback
# stopped at the first RU -> UA candidate even when it produced no visible
# change, so ordinary six-letter words failed with LF-HK-005. Exercise the
# explicit, caret and held-Shift paths with such a word in real Chromium.
$siblingCases = @(
    @{ Browser = 'edge'; Modes = @('sibling'); Label = 'selected' },
    @{ Browser = 'chrome'; Modes = @('caret', 'sibling'); Label = 'caret' },
    @{ Browser = 'chrome'; Modes = @('holdshift', 'sibling'); Label = 'held-shift' }
)
foreach ($siblingCase in $siblingCases) {
    for ($iteration = 1; $iteration -le $Runs; $iteration++) {
        if (Test-Path $resultPath) {
            Remove-Item -LiteralPath $resultPath -Force
        }
        & $harness "--$($siblingCase.Browser)-test" 'input' @($siblingCase.Modes)
        $exitCode = $LASTEXITCODE
        $details = if (Test-Path $resultPath) {
            Get-Content $resultPath -Raw
        } else {
            'The harness did not create a result log.'
        }
        if ($exitCode -ne 0 -or
            -not $details.Contains('noOpSiblingLayout=True') -or
            -not $details.Contains('browser-first-press:success=True') -or
            -not $details.Contains('Reason=layout-fallback') -or
            -not $details.Contains('SourceLayout=ru-RU; TargetLayout=en-US')) {
            Remove-IsolatedBrowserProfiles
            throw "$($siblingCase.Browser) no-op sibling $($siblingCase.Label) iteration $iteration/$Runs failed with exit code $exitCode.`n$details"
        }
        Record-BrowserLatency "$($siblingCase.Browser)-input-sibling-$($siblingCase.Label)"
        Remove-IsolatedBrowserProfiles
    }
}

$profileLeaks = @(
    Get-ChildItem `
        -LiteralPath $temporaryRoot `
        -Directory `
        -ErrorAction SilentlyContinue | Where-Object {
            $_.Name -like 'LayoutFix.EdgeE2E.*' -or
            $_.Name -like 'LayoutFix.ChromeE2E.*'
        }
)
if ($profileLeaks.Count -ne 0) {
    throw "Browser compatibility E2E left isolated profiles: $($profileLeaks.FullName -join ', ')"
}

$sortedLatencies = @($observedLatencies | Sort-Object)
$medianLatency = $sortedLatencies[[int][Math]::Floor(($sortedLatencies.Count - 1) / 2)]
$maximumLatency = $sortedLatencies[-1]
$sortedActionLatencies = @($observedActionLatencies | Sort-Object)
$medianActionLatency = $sortedActionLatencies[[int][Math]::Floor(($sortedActionLatencies.Count - 1) / 2)]
$maximumActionLatency = $sortedActionLatencies[-1]
Write-Output "browser_latency_summary samples=$($sortedLatencies.Count) visibleMedianMs=$medianLatency visibleMaxMs=$maximumLatency actionMedianMs=$medianActionLatency actionMaxMs=$maximumActionLatency"

Write-Output "browser_compatibility=pass edge_runs=$Runs chrome_runs=$Runs targets=input,textarea,contenteditable chrome_caret_runs=$Runs chrome_duplicate_contenteditable=$Runs chrome_shift_scroll_held_runs=$Runs no_op_sibling_layout_runs=$($Runs * $siblingCases.Count) first_attempt_only=true latency_gate_ms=750"
