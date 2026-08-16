param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.12',

    [Parameter(Mandatory = $false)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$ProtectedProcessName = 'LayoutFix',

    [Parameter(Mandatory = $false)]
    [string]$CompilerPath = ''
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = (Resolve-Path (Join-Path $workspace 'artifacts')).Path
$testDirectory = [IO.Path]::GetFullPath((Join-Path $artifacts "installer-e2e-$Version"))
$requiredPrefix = $artifacts + [IO.Path]::DirectorySeparatorChar
if (-not $testDirectory.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe installer E2E directory: $testDirectory"
}
if (Test-Path -LiteralPath $testDirectory) {
    throw "Installer E2E directory already exists: $testDirectory"
}
if (@(Get-Process -Name $ProtectedProcessName -ErrorAction SilentlyContinue).Count -ne 0) {
    throw "Installer E2E refuses to close or replace an existing $ProtectedProcessName session."
}

$resolvedCompilerPath = if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    [IO.Path]::GetFullPath((Join-Path $workspace '.tools\inno7\ISCC.exe'))
}
elseif ([IO.Path]::IsPathRooted($CompilerPath)) {
    [IO.Path]::GetFullPath($CompilerPath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $workspace $CompilerPath))
}
if (-not (Test-Path -LiteralPath $resolvedCompilerPath -PathType Leaf)) {
    throw "Inno Setup compiler is missing: $resolvedCompilerPath"
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$existingRunValue = (Get-ItemProperty -Path $runKey -Name LayoutFix -ErrorAction SilentlyContinue).LayoutFix
$hadExistingRunValue = $null -ne $existingRunValue
$testInstaller = Join-Path $workspace 'Output\LayoutFix_Setup_Test.exe'
$windowsE2E = Join-Path $workspace 'tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64\LayoutFix.WindowsE2E.exe'
if (-not (Test-Path -LiteralPath $windowsE2E)) {
    throw 'Windows E2E executable is missing; build it before installer lifecycle.'
}
$smokeLog = Join-Path $artifacts "installer-smoke-$Version-$([Guid]::NewGuid().ToString('N')).log"
$previousSmokeLog = [Environment]::GetEnvironmentVariable('LAYOUTFIX_SMOKE_LOG', 'Process')
$userProfileDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LayoutFix'
$trackedUserProfileFiles = @(
    'settings.json',
    'settings.json.bak',
    'translation_history.json',
    'Logs\layoutfix.log'
)

function Get-UserProfileSnapshot {
    $snapshot = @{}
    foreach ($relativePath in $trackedUserProfileFiles) {
        $path = Join-Path $userProfileDirectory $relativePath
        if (Test-Path -LiteralPath $path) {
            $file = Get-Item -LiteralPath $path
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            $snapshot[$relativePath] = "$($file.Length):$hash"
        }
        else {
            $snapshot[$relativePath] = '<missing>'
        }
    }
    return $snapshot
}

function Assert-UserProfileUnchanged([hashtable]$Expected) {
    $actual = Get-UserProfileSnapshot
    foreach ($relativePath in $trackedUserProfileFiles) {
        if ($actual[$relativePath] -ne $Expected[$relativePath]) {
            throw "Installer smoke changed the user profile file: $relativePath"
        }
    }
}

function Assert-SmokeIsolation([int]$ExpectedRuns) {
    if (-not (Test-Path -LiteralPath $smokeLog)) {
        throw 'Installed smoke did not create its privacy-safe lifecycle log.'
    }
    $lines = @(Get-Content -LiteralPath $smokeLog)
    foreach ($marker in @(
        'profile:isolated',
        'dictionaries:warmed',
        'shutdown:complete',
        'profile:cleaned'
    )) {
        $count = @($lines | Where-Object {
            $_.EndsWith(" $marker", [StringComparison]::Ordinal)
        }).Count
        if ($count -ne $ExpectedRuns) {
            throw "Installed smoke marker '$marker' count was $count; expected $ExpectedRuns."
        }
    }
}

function Invoke-StartupLifecycle([string]$ApplicationPath) {
    & $windowsE2E --startup-lifecycle-test $ApplicationPath
    if ($LASTEXITCODE -ne 0) {
        throw "Installed startup lifecycle failed with exit code $LASTEXITCODE."
    }
}

function Invoke-StartupRecovery([string]$ApplicationPath) {
    & $windowsE2E --startup-recovery-test $ApplicationPath
    if ($LASTEXITCODE -ne 0) {
        throw "Installed startup recovery failed with exit code $LASTEXITCODE."
    }
}

function Invoke-SessionRecovery([string]$ApplicationPath) {
    & $windowsE2E --session-recovery-test $ApplicationPath
    if ($LASTEXITCODE -ne 0) {
        throw "Installed session recovery failed with exit code $LASTEXITCODE."
    }
}

$userProfileBefore = Get-UserProfileSnapshot
$env:LAYOUTFIX_SMOKE_LOG = $smokeLog

try {
    Push-Location $workspace
    try {
        & $resolvedCompilerPath "/DMyAppVersion=$Version" '/DLayoutFixTestInstall=1' 'installer.iss' | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Test installer compilation failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    $installArguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/TASKS=autostart',
        '/NOICONS',
        "/DIR=$testDirectory"
    )
    $install = Start-Process $testInstaller -ArgumentList $installArguments -WindowStyle Hidden -Wait -PassThru
    if ($install.ExitCode -ne 0) {
        throw "Test install failed with exit code $($install.ExitCode)."
    }

    $runValue = (Get-ItemProperty -Path $runKey -Name LayoutFix -ErrorAction Stop).LayoutFix
    $expectedRunValue = '"' + (Join-Path $testDirectory 'LayoutFix.exe') + '"'
    if ($runValue -ne $expectedRunValue) {
        throw "Installer created an invalid Run value: $runValue"
    }

    $installedApplication = Join-Path $testDirectory 'LayoutFix.exe'
    $smoke = Start-Process $installedApplication -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
    if ($smoke.ExitCode -ne 0) {
        throw "Installed smoke test failed with exit code $($smoke.ExitCode)."
    }
    Assert-SmokeIsolation 1
    Assert-UserProfileUnchanged $userProfileBefore
    Invoke-StartupLifecycle $installedApplication
    Assert-UserProfileUnchanged $userProfileBefore
    $installedVersion = (Get-Item -LiteralPath $installedApplication).VersionInfo.ProductVersion
    if ($installedVersion -notlike "$Version*") {
        throw "Installed version mismatch: $installedVersion"
    }

    $update = Start-Process $testInstaller -ArgumentList $installArguments -WindowStyle Hidden -Wait -PassThru
    if ($update.ExitCode -ne 0) {
        throw "Test update failed with exit code $($update.ExitCode)."
    }
    $updatedSmoke = Start-Process $installedApplication -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
    if ($updatedSmoke.ExitCode -ne 0) {
        throw "Updated smoke test failed with exit code $($updatedSmoke.ExitCode)."
    }
    Assert-SmokeIsolation 2
    Assert-UserProfileUnchanged $userProfileBefore
    Invoke-StartupLifecycle $installedApplication
    Assert-UserProfileUnchanged $userProfileBefore
    Invoke-StartupRecovery $installedApplication
    Assert-UserProfileUnchanged $userProfileBefore
    Invoke-SessionRecovery $installedApplication
    Assert-UserProfileUnchanged $userProfileBefore

    $uninstaller = Join-Path $testDirectory 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) {
        throw 'Test uninstaller was not created.'
    }
    $uninstall = Start-Process $uninstaller -ArgumentList @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART'
    ) -WindowStyle Hidden -Wait -PassThru
    if ($uninstall.ExitCode -ne 0) {
        throw "Test uninstall failed with exit code $($uninstall.ExitCode)."
    }
    for ($attempt = 0; $attempt -lt 40 -and (Test-Path -LiteralPath $testDirectory); $attempt++) {
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path -LiteralPath $testDirectory) {
        throw 'Test uninstall left the application directory behind.'
    }
    if ((Get-ItemProperty -Path $runKey -Name LayoutFix -ErrorAction SilentlyContinue).LayoutFix) {
        throw 'Test uninstall left the autostart Run value behind.'
    }
    Assert-UserProfileUnchanged $userProfileBefore

    "installer_lifecycle=pass version=$Version install_update_uninstall=pass smoke_profile=isolated startup_recovery=pass session_recovery=pass user_profile=unchanged"
}
finally {
    if ($hadExistingRunValue) {
        Set-ItemProperty -Path $runKey -Name LayoutFix -Value $existingRunValue
    }
    else {
        Remove-ItemProperty -Path $runKey -Name LayoutFix -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $testInstaller) {
        Remove-Item -LiteralPath $testInstaller -Force
    }
    if ($null -eq $previousSmokeLog) {
        Remove-Item Env:\LAYOUTFIX_SMOKE_LOG -ErrorAction SilentlyContinue
    }
    else {
        $env:LAYOUTFIX_SMOKE_LOG = $previousSmokeLog
    }
    if (Test-Path -LiteralPath $smokeLog) {
        Remove-Item -LiteralPath $smokeLog -Force
    }
    if (Test-Path -LiteralPath $testDirectory) {
        Remove-Item -LiteralPath $testDirectory -Recurse -Force
    }
}
