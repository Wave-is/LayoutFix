[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$Runs = 10,

    [ValidateRange(0, 1000)]
    [int]$SoakIterations = 0,

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
if (-not (Test-Path $harness)) {
    throw "Windows E2E harness is missing: $harness"
}

for ($iteration = 1; $iteration -le $Runs; $iteration++) {
    $output = @(& $harness '--auto-correction-test' $SoakIterations 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw (
            "Auto-correction cold run $iteration/$Runs failed with exit code $exitCode.`n" +
            ($output -join [Environment]::NewLine))
    }
    if (-not ($output -match 'auto-correction:result=0;')) {
        throw (
            "Auto-correction cold run $iteration/$Runs did not emit a success result.`n" +
            ($output -join [Environment]::NewLine))
    }
    if (-not ($output -match (
        "auto-correction:soak=pass;completed=$SoakIterations;requested=$SoakIterations"))) {
        throw (
            "Auto-correction cold run $iteration/$Runs did not complete its soak matrix.`n" +
            ($output -join [Environment]::NewLine))
    }
}

$leftovers = @(Get-ChildItem `
    -LiteralPath ([IO.Path]::GetTempPath()) `
    -Directory `
    -Filter 'LayoutFix.AutoCorrectionE2E.*' `
    -ErrorAction SilentlyContinue)
if ($leftovers.Count -ne 0) {
    throw "Auto-correction E2E left temporary directories: $($leftovers.FullName -join ', ')"
}

$protectedHostAlias = Join-Path (Split-Path -Parent $harness) 'pwsh.exe'
if (Test-Path -LiteralPath $protectedHostAlias) {
    throw "Auto-correction E2E left its protected-process apphost alias: $protectedHostAlias"
}

Write-Output "auto_correction=pass cold_runs=$Runs soak_iterations_per_run=$SoakIterations target=winforms-edit protected_process=pwsh-blocked protected_host_layout=en-exact protected_acronym=tls-preserved frequent_source_token=ofc-preserved frequent_cyrillic_source=pgt-preserved expanded_source_corpus=sush-preserved lower_frequency_source=utv-preserved long_tail_en=rtv-preserved long_tail_ru=vfv-preserved expanded_technical_token=gtk-preserved shared_ru_uk=strong-resolved-common-rejected punctuation=apostrophe technical_token=preserved short_token=preserved rare_short=preserved common_short=corrected held_modifier=safe partial_backspace=restored post_correction_layout=stable target_recheck_race=safe cancelled_layout=restored cross_target_rollback=safe"
