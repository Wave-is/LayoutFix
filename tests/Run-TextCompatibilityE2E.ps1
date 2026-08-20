[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$Runs = 10,

    [switch]$IncludeWordPad,

    [switch]$NoBuild,

    [ValidateRange(0, [long]::MaxValue)]
    [long]$ExistingTextAppHandle = 0
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

function Invoke-CompatibilityHarness {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$Label
    )

    & $harness @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $details = if (Test-Path $resultPath) {
            Get-Content $resultPath -Raw
        } else {
            'The harness did not create a result log.'
        }
        throw "$Label failed with exit code $exitCode.`n$details"
    }
}

foreach ($kind in @('edit', 'richedit')) {
    for ($iteration = 1; $iteration -le $Runs; $iteration++) {
        Invoke-CompatibilityHarness `
            -Arguments @('--external-edit-test', $kind) `
            -Label "$kind iteration $iteration/$Runs"
    }
}

$wordPadStatus = 'not-requested'
if ($IncludeWordPad) {
    $wordPadPath = 'C:\Program Files\Windows NT\Accessories\wordpad.exe'
    if (-not (Test-Path $wordPadPath)) {
        throw "WordPad is not installed at the expected path: $wordPadPath"
    }

    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $testDirectory = [IO.Path]::GetFullPath(
        [IO.Path]::Combine($systemTemp, "LayoutFix.WordPadE2E.$([Guid]::NewGuid().ToString('N'))"))
    if (-not $testDirectory.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Resolved WordPad test directory is outside the system temporary directory.'
    }

    [IO.Directory]::CreateDirectory($testDirectory) | Out-Null
    $testFile = Join-Path $testDirectory 'sentinel.txt'
    [IO.File]::WriteAllText($testFile, 'TEST', [Text.UTF8Encoding]::new($false))
    $wordPad = $null
    try {
        $wordPad = Start-Process $wordPadPath -ArgumentList @("`"$testFile`"") -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 100
            $wordPad.Refresh()
        } while (
            $wordPad.MainWindowHandle -eq 0 -and
            -not $wordPad.HasExited -and
            [DateTime]::UtcNow -lt $deadline)

        if ($wordPad.HasExited -or $wordPad.MainWindowHandle -eq 0) {
            throw 'The controlled WordPad process did not expose a main window.'
        }

        Invoke-CompatibilityHarness `
            -Arguments @('--existing-text-app-test', $wordPad.MainWindowHandle.ToInt64().ToString()) `
            -Label 'WordPad RichEdit compatibility'
        $wordPadStatus = 'pass'
    } finally {
        if ($null -ne $wordPad) {
            try { $wordPad.Refresh() } catch { }
            if (-not $wordPad.HasExited) {
                Stop-Process -Id $wordPad.Id -Force -ErrorAction SilentlyContinue
            }
            $wordPad.Dispose()
        }
        if (Test-Path $testDirectory) {
            [IO.Directory]::Delete($testDirectory, $true)
        }
    }
}

if ($ExistingTextAppHandle -ne 0) {
    Invoke-CompatibilityHarness `
        -Arguments @('--existing-text-app-test', $ExistingTextAppHandle.ToString()) `
        -Label 'existing text application compatibility'
}

Write-Output (
    "text_compatibility=pass edit_runs=$Runs rich_edit_runs=$Runs " +
    "wordpad=$wordPadStatus existing_target=$($ExistingTextAppHandle -ne 0)")
