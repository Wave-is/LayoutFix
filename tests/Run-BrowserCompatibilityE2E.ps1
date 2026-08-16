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

foreach ($browser in @('edge', 'chrome')) {
    foreach ($target in @('input', 'textarea', 'contenteditable')) {
        for ($iteration = 1; $iteration -le $Runs; $iteration++) {
            if (Test-Path $resultPath) {
                Remove-Item -LiteralPath $resultPath -Force
            }
            & $harness "--${browser}-test" $target
            $exitCode = $LASTEXITCODE
            if ($exitCode -ne 0) {
                $details = if (Test-Path $resultPath) {
                    Get-Content $resultPath -Raw
                } else {
                    'The harness did not create a result log.'
                }
                throw "$browser $target iteration $iteration/$Runs failed with exit code $exitCode.`n$details"
            }
        }
    }
}

$profileLeaks = @(
    Get-ChildItem `
        -LiteralPath ([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) `
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
