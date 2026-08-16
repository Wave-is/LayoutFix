param(
    [ValidateRange(1, 100)]
    [int]$Runs = 10,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $workspace 'tests\LayoutFix.WindowsE2E\LayoutFix.WindowsE2E.csproj'
$localDotnet = Join-Path $workspace '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}
Push-Location $workspace
try {
    if (-not $NoBuild) {
        & $dotnet build `
            $project `
            -c Release `
            --no-restore `
            -p:TreatWarningsAsErrors=true
        if ($LASTEXITCODE -ne 0) {
            throw "Windows E2E build failed with exit code $LASTEXITCODE."
        }
    }

    $harness = 'tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64\LayoutFix.WindowsE2E.exe'
    if (-not (Test-Path -LiteralPath $harness)) {
        throw "Windows E2E harness is missing: $harness"
    }

    for ($iteration = 1; $iteration -le $Runs; $iteration++) {
        $output = & $harness '--selection-ownership-test' 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join [Environment]::NewLine
        if ($exitCode -ne 0 -or
            $text -notmatch 'selection_ownership=pass' -or
            $text -notmatch 'keyboard_safe=True' -or
            $text -notmatch 'mouse_safe=True' -or
            $text -notmatch 'hook_recovery=True' -or
            $text -notmatch 'clipboard_preserved=True') {
            throw (
                "Selection ownership cold run $iteration/$Runs failed with " +
                "exit code $exitCode.`n$text")
        }
    }

    Write-Output "selection_ownership=pass cold_runs=$Runs keyboard=safe mouse=safe duplicate_text=safe hook_recovery=safe clipboard=preserved"
}
finally {
    Pop-Location
}
