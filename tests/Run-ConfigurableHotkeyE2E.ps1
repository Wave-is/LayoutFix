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
$dotnet = if (Test-Path $localDotnet) {
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
if (-not (Test-Path $harness)) {
    throw "Windows E2E harness is missing: $harness"
}

$cases = @(
    @{ Hotkey = 'Ctrl+/'; VirtualKey = 0xBF },
    @{ Hotkey = 'Ctrl+NumPad0'; VirtualKey = 0x60 },
    @{ Hotkey = 'Ctrl+0'; VirtualKey = 0x30 }
)

foreach ($case in $cases) {
    for ($iteration = 1; $iteration -le $Runs; $iteration++) {
        $output = @(& $harness '--hotkey-vk-test' `
            $case.Hotkey ([string]$case.VirtualKey) 2>&1)
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw (
                "Hotkey '$($case.Hotkey)' cold run $iteration/$Runs failed " +
                "with exit code $exitCode.`n" +
                ($output -join [Environment]::NewLine))
        }

        $result = Join-Path (Split-Path $harness) 'windows-e2e-result.txt'
        $evidence = Get-Content -LiteralPath $result -Raw
        $expectedHook = "hook:count=1;combo="
        if (-not $evidence.Contains($expectedHook) -or
            -not $evidence.Contains('verify:completed=1;requested=1;exit=0')) {
            throw (
                "Hotkey '$($case.Hotkey)' cold run $iteration/$Runs did not " +
                "emit successful hook and transaction evidence.`n$evidence")
        }
    }
}

$leftoverProcesses = @(Get-Process -Name 'LayoutFix.WindowsE2E' -ErrorAction SilentlyContinue)
if ($leftoverProcesses.Count -ne 0) {
    throw "Configurable hotkey E2E left running processes: $($leftoverProcesses.Id -join ', ')"
}

Write-Output (
    "configurable_hotkeys=pass cold_runs=$Runs cases=" +
    (($cases | ForEach-Object Hotkey) -join ','))
