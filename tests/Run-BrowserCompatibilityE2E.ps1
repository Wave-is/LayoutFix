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

$maximumAttempts = 3
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
                Write-Warning "$browser $target iteration $iteration/$Runs attempt $attempt/$maximumAttempts failed with exit code $exitCode; retrying once with a fresh isolated profile."
            }
        }
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

Write-Output "browser_compatibility=pass edge_runs=$Runs chrome_runs=$Runs targets=input,textarea,contenteditable"
