[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$Runs = 3,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'tests\LayoutFix.WindowsE2E\LayoutFix.WindowsE2E.csproj'
$localDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

if (-not $NoBuild) {
    & $dotnet build $project -c Release -r win-x64 --no-restore `
        -p:TreatWarningsAsErrors=true /nodeReuse:false
    if ($LASTEXITCODE -ne 0) {
        throw "Windows E2E harness build failed with exit code $LASTEXITCODE."
    }
}

$harness = Join-Path $repoRoot `
    'tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64\LayoutFix.WindowsE2E.exe'
if (-not (Test-Path -LiteralPath $harness)) {
    throw "Windows E2E harness is missing: $harness"
}

$result = Join-Path (Split-Path $harness) 'windows-e2e-result.txt'
$expectedCases = @(
    'lowercase',
    'title-case',
    'uppercase',
    'phrase',
    'reverse-phrase',
    'punctuation',
    'numbers',
    'multiline',
    'tab',
    'emoji',
    'unicode-wrappers',
    'long-selection'
)

for ($iteration = 1; $iteration -le $Runs; $iteration++) {
    $startedAt = [DateTime]::UtcNow
    $output = @(& $harness '--manual-correction-matrix' 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $result)) {
        $evidence = if (Test-Path -LiteralPath $result) {
            Get-Content -LiteralPath $result -Raw
        } else {
            '<result file missing>'
        }
        throw (
            "Manual correction matrix cold run $iteration/$Runs failed " +
            "with exit code $exitCode.`n" +
            ($output -join [Environment]::NewLine) + "`n" +
            $evidence)
    }

    $resultFile = Get-Item -LiteralPath $result
    $evidence = Get-Content -LiteralPath $result -Raw
    if ($resultFile.LastWriteTimeUtc -lt $startedAt -or
        -not $evidence.Contains('verify:completed=12;requested=12;exit=0') -or
        -not $evidence.Contains('finish:0') -or
        -not $evidence.Contains('privacy=True')) {
        throw (
            "Manual correction matrix cold run $iteration/$Runs did not " +
            "emit fresh successful transaction and privacy evidence.`n$evidence")
    }

    foreach ($case in $expectedCases) {
        if (-not $evidence.Contains("manual-case:id=$case;")) {
            throw (
                "Manual correction matrix cold run $iteration/$Runs " +
                "did not execute case '$case'.`n$evidence")
        }
    }
}

$leftoverProcesses = @(
    Get-Process -Name 'LayoutFix.WindowsE2E' -ErrorAction SilentlyContinue
)
if ($leftoverProcesses.Count -ne 0) {
    throw (
        "Manual correction matrix left running processes: " +
        ($leftoverProcesses.Id -join ', '))
}

Write-Output (
    "manual_correction_matrix=pass cold_runs=$Runs cases_per_run=" +
    "$($expectedCases.Count) clipboard=preserved diagnostics=privacy-safe")
