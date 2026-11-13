param([string]$AppPath = (Join-Path $env:LOCALAPPDATA 'Programs\LayoutFix\LayoutFix.exe'))

# Read-only. Report flags, not user names, paths, or shortcut arguments.
$app = [IO.Path]::GetFullPath($AppPath)
$layers = 'Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers'
$result = [ordered]@{
    Installed = [IO.File]::Exists($app)
    Version = if ([IO.File]::Exists($app)) { (Get-Item -LiteralPath $app).VersionInfo.FileVersion }
    EnvironmentRunAsAdmin = $env:__COMPAT_LAYER -match '(?i)\bRUNASADMIN\b'
}
foreach ($hive in @('HKCU', 'HKLM')) {
    $key = Get-Item -LiteralPath ("${hive}:\$layers") -ErrorAction SilentlyContinue
    $result["${hive}RunAsAdmin"] = $null -ne $key -and [string]$key.GetValue($app) -match '(?i)\bRUNASADMIN\b'
}
$shortcuts = foreach ($location in @('DesktopDirectory','CommonDesktopDirectory','Programs','CommonPrograms','Startup','CommonStartup')) {
    $folder = [Environment]::GetFolderPath($location)
    if (!$folder -or !(Test-Path -LiteralPath $folder)) { continue }
    foreach ($link in Get-ChildItem -LiteralPath $folder -Filter '*LayoutFix*.lnk' -Recurse -File -ErrorAction SilentlyContinue) {
        $header = [IO.File]::ReadAllBytes($link.FullName)
        if ($header.Length -ge 76 -and [BitConverter]::ToUInt32($header,0) -eq 76) {
            [pscustomobject]@{ Location=$location; RunAsAdmin=([BitConverter]::ToUInt32($header,20) -band 0x2000) -ne 0 }
        }
    }
}
$result.Shortcuts = @($shortcuts | Where-Object { $_ })
$result | ConvertTo-Json -Depth 3
