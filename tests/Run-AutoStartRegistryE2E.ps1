$ErrorActionPreference = 'Stop'

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = (Resolve-Path (Join-Path $workspace 'artifacts')).Path
$testDirectory = [IO.Path]::GetFullPath((Join-Path $artifacts 'autostart registry e2e'))
$requiredPrefix = $artifacts + [IO.Path]::DirectorySeparatorChar
if (-not $testDirectory.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe autostart E2E directory: $testDirectory"
}
if (Test-Path -LiteralPath $testDirectory) {
    throw "Autostart E2E directory already exists: $testDirectory"
}

$sourceDirectory = Join-Path $workspace 'tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64'
$sourceExecutable = Join-Path $sourceDirectory 'LayoutFix.WindowsE2E.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw "Windows E2E harness is not built: $sourceExecutable"
}

$registryPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$valueName = 'LayoutFix'
$previousValue = $null
$previousKind = $null
$hadPreviousValue = $false

$existingKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath, $false)
try {
    if ($null -ne $existingKey -and $existingKey.GetValueNames() -contains $valueName) {
        $hadPreviousValue = $true
        $previousValue = $existingKey.GetValue(
            $valueName,
            $null,
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $previousKind = $existingKey.GetValueKind($valueName)
    }
}
finally {
    if ($null -ne $existingKey) {
        $existingKey.Dispose()
    }
}

try {
    Copy-Item -LiteralPath $sourceDirectory -Destination $testDirectory -Recurse
    $testExecutable = Join-Path $testDirectory 'LayoutFix.WindowsE2E.exe'
    & $testExecutable --autostart-registry-test
    if ($LASTEXITCODE -ne 0) {
        throw "Autostart registry E2E failed with exit code $LASTEXITCODE."
    }
}
finally {
    try {
        $restoreKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath, $true)
        try {
            if ($hadPreviousValue -and $null -ne $previousValue -and $null -ne $previousKind) {
                $restoreKey.SetValue($valueName, $previousValue, $previousKind)
            }
            else {
                $restoreKey.DeleteValue($valueName, $false)
            }
        }
        finally {
            $restoreKey.Dispose()
        }
    }
    finally {
        if (Test-Path -LiteralPath $testDirectory) {
            Remove-Item -LiteralPath $testDirectory -Recurse -Force
        }
    }
}
