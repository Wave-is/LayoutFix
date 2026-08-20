param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.15'
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = (Resolve-Path (Join-Path $workspace 'artifacts')).Path
$testDirectory = [IO.Path]::GetFullPath((Join-Path $artifacts "installer-process-guard-$([Guid]::NewGuid().ToString('N'))"))
$requiredPrefix = $artifacts + [IO.Path]::DirectorySeparatorChar
if (-not $testDirectory.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe installer process-guard directory: $testDirectory"
}

$e2e = Join-Path $workspace 'tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64\LayoutFix.WindowsE2E.exe'
if (-not (Test-Path -LiteralPath $e2e)) {
    throw 'Windows E2E executable is missing; build it before the installer process guard.'
}

$readyPath = Join-Path $testDirectory 'host.ready'
$goPath = Join-Path $testDirectory 'host.go'
$logPath = Join-Path $testDirectory 'host.log'
$lifecycleDirectory = Join-Path $artifacts "installer-e2e-$Version"
$testInstaller = Join-Path $workspace 'Output\LayoutFix_Setup_Test.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$beforeRunValue = (Get-ItemProperty -Path $runKey -Name LayoutFix -ErrorAction SilentlyContinue).LayoutFix
$hostProcess = $null

try {
    if ((Test-Path -LiteralPath $lifecycleDirectory) -or
        (Test-Path -LiteralPath $testInstaller)) {
        throw 'Installer process-guard precondition found stale lifecycle artifacts.'
    }
    New-Item -ItemType Directory -Path $testDirectory | Out-Null
    $hostProcess = Start-Process $e2e -ArgumentList @(
        '--logger-write-host',
        $logPath,
        'INSTALLER_GUARD',
        '1',
        $readyPath,
        $goPath
    ) -WindowStyle Hidden -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath $readyPath) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 50
    }
    if (-not (Test-Path -LiteralPath $readyPath)) {
        throw 'Installer process-guard host did not become ready.'
    }

    $rejected = $false
    try {
        & (Join-Path $PSScriptRoot 'Run-InstallerE2E.ps1') `
            -Version $Version `
            -ProtectedProcessName 'LayoutFix.WindowsE2E'
    }
    catch {
        if ($_.Exception.Message -notlike '*refuses to close or replace*') {
            throw
        }
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'Installer lifecycle accepted an existing protected process.'
    }
    if ((Test-Path -LiteralPath $lifecycleDirectory) -or
        (Test-Path -LiteralPath $testInstaller)) {
        throw 'Installer process guard ran after a filesystem mutation.'
    }
    $afterRunValue = (Get-ItemProperty -Path $runKey -Name LayoutFix -ErrorAction SilentlyContinue).LayoutFix
    if ($beforeRunValue -ne $afterRunValue) {
        throw 'Installer process guard changed the user autostart value.'
    }

    "installer_existing_process_guard=pass decision=fail-closed filesystem=unchanged registry=unchanged"
}
finally {
    if (Test-Path -LiteralPath $testDirectory) {
        Set-Content -LiteralPath $goPath -Value 'go' -ErrorAction SilentlyContinue
    }
    if ($null -ne $hostProcess -and -not ($hostProcess.WaitForExit(5000))) {
        $hostProcess.Kill()
        $hostProcess.WaitForExit()
    }
    if (Test-Path -LiteralPath $testDirectory) {
        Remove-Item -LiteralPath $testDirectory -Recurse -Force
    }
}
