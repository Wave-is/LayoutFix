[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$Runs = 10,

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

$harnessDirectory = Join-Path $repoRoot 'tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64'
$harness = Join-Path $harnessDirectory 'LayoutFix.WindowsE2E.exe'
$resultPath = Join-Path $harnessDirectory 'windows-e2e-result.txt'
if (-not (Test-Path $harness)) {
    throw "Windows E2E harness is missing: $harness"
}

$modes = @(
    @{
        Argument = '--partial-replacement-test'
        Evidence = 'partial-replacement:failureObserved=True;rollbackObserved=True'
        Label = 'restore'
    },
    @{
        Argument = '--partial-replacement-input-race-test'
        Evidence = 'partial-replacement-race:inputObserved=True;rollbackSkipped=True'
        Label = 'same-target-race'
    },
    @{
        Argument = '--partial-replacement-mouse-race-test'
        Evidence = 'partial-replacement-mouse-race:inputObserved=True;rollbackSkipped=True'
        Label = 'same-target-mouse-race'
    },
    @{
        Argument = '--partial-replacement-xbutton-race-test'
        Evidence = 'partial-replacement-xbutton-race:inputObserved=True;rollbackSkipped=True'
        Label = 'same-target-xbutton-race'
    }
)

foreach ($mode in $modes) {
    for ($iteration = 1; $iteration -le $Runs; $iteration++) {
        & $harness $mode.Argument
        $exitCode = $LASTEXITCODE
        $result = if (Test-Path $resultPath) {
            Get-Content -LiteralPath $resultPath -Raw
        } else {
            ''
        }
        if ($exitCode -ne 0 -or
            $result -notmatch [regex]::Escape($mode.Evidence) -or
            $result -notmatch 'verify:completed=1;requested=1;exit=0') {
            throw (
                "Partial replacement $($mode.Label) cold run $iteration/$Runs failed " +
                "with exit code $exitCode.`n" + $result)
        }
    }
}

Write-Output "partial_input=pass cold_runs_per_mode=$Runs manual_replacement=restored same_target_keyboard_race=safe same_target_mouse_race=safe same_target_xbutton_race=safe clipboard=preserved"
