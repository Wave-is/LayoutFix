[CmdletBinding()]
param([ValidateRange(1, 10)][int]$Runs = 3, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $NoBuild) {
    & "$repo\.dotnet\dotnet.exe" build "$repo\tests\LayoutFix.WindowsE2E\LayoutFix.WindowsE2E.csproj" -c Release -r win-x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'E2E build failed.' }
}
$harness = "$repo\tests\LayoutFix.WindowsE2E\bin\Release\net8.0-windows\win-x64\LayoutFix.WindowsE2E.exe"
$result = Join-Path (Split-Path $harness) 'windows-e2e-result.txt'
$evidence = Join-Path $repo ('artifacts\qa\chromium-group-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$failures = [Collections.Generic.List[string]]::new()
$count = 0
foreach ($browser in @('chrome', 'edge')) {
    foreach ($mode in @('selected', 'caret', 'holdshift', 'readonly')) {
        foreach ($iteration in 1..$Runs) {
            $target = if ($mode -eq 'readonly') { 'readonlygroup' } else { 'group' }
            $arguments = @("--$browser-test", $target)
            if ($mode -in @('caret', 'holdshift')) { $arguments += $mode }
            $process = Start-Process -FilePath $harness -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
            $label = "$browser-$mode-$iteration"
            Copy-Item -LiteralPath $result -Destination (Join-Path $evidence "$label.log")
            $details = Get-Content -LiteralPath $result -Raw
            $expected = if ($mode -eq 'readonly') {
                'browser-readonly:rejected=True;unchanged=True;clipboardPreserved=True'
            } else { 'browser-first-press:success=True' }
            $passed = $process.ExitCode -eq 0 -and $details.Contains($expected)
            if (-not $passed) { $failures.Add("$label exit=$($process.ExitCode)") }
            $count++
            Write-Output "group_case=$label passed=$passed exit=$($process.ExitCode)"
        }
    }
}
Write-Output "group_summary cases=$count failures=$($failures.Count) evidence=$evidence"
if ($failures.Count -ne 0) { throw ($failures -join '; ') }
