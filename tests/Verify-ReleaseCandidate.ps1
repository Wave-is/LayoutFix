[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [switch]$AllowUnsigned,

    [Parameter(Mandatory = $false)]
    [switch]$RequireClean
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installer = Join-Path $workspace 'Output\LayoutFix_Setup.exe'
$checksumFile = "$installer.sha256"
$publishDirectory = Join-Path $workspace 'src\LayoutFix\bin\Release\net8.0-windows\win-x64\publish'
$payload = Join-Path $publishDirectory 'LayoutFix.exe'

function Assert-ReleaseCondition([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-Git([string[]]$Arguments) {
    $output = @(& git -C $workspace @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Git verification failed: $($Arguments[0])."
    }
    return $output
}

Assert-ReleaseCondition (Test-Path -LiteralPath $installer -PathType Leaf) `
    'Release installer is missing.'
Assert-ReleaseCondition (Test-Path -LiteralPath $checksumFile -PathType Leaf) `
    'Release checksum is missing.'
Assert-ReleaseCondition (Test-Path -LiteralPath $payload -PathType Leaf) `
    'Published payload is missing.'

[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $workspace 'Directory.Build.props')
$version = [string]($buildProperties.Project.PropertyGroup.Version | Select-Object -First 1)
Assert-ReleaseCondition ($version -match '^\d+\.\d+\.\d+$') `
    'Build version is missing or invalid.'

$installerVersion = (Get-Item -LiteralPath $installer).VersionInfo.ProductVersion.Trim()
Assert-ReleaseCondition ($installerVersion -eq $version) `
    "Installer version $installerVersion does not match build version $version."

$checksumLine = (Get-Content -LiteralPath $checksumFile -Raw).Trim()
Assert-ReleaseCondition ($checksumLine -match '^([0-9a-fA-F]{64}) \*LayoutFix_Setup\.exe$') `
    'Release checksum file has an invalid format.'
$declaredHash = $Matches[1].ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-ReleaseCondition ($actualHash -eq $declaredHash) `
    'Release installer does not match its SHA-256 checksum.'

$payloadVersion = (Get-Item -LiteralPath $payload).VersionInfo.ProductVersion.Trim()
$payloadPattern = '^' + [regex]::Escape($version) + '\+([0-9a-fA-F]{40})$'
Assert-ReleaseCondition ($payloadVersion -match $payloadPattern) `
    'Published payload does not contain a full source commit.'
$sourceCommit = $Matches[1].ToLowerInvariant()

$null = Invoke-Git @('cat-file', '-e', "$sourceCommit`^{commit}")
$null = Invoke-Git @('merge-base', '--is-ancestor', $sourceCommit, 'HEAD')
if ($RequireClean) {
    $status = @(Invoke-Git @('status', '--porcelain=v1', '--untracked-files=normal'))
    Assert-ReleaseCondition ($status.Count -eq 0) `
        'Working tree is not clean.'
}

$firstPartyBinaries = @(
    $payload,
    (Join-Path $publishDirectory 'LayoutFix.Core.dll'),
    (Join-Path $publishDirectory 'LayoutFix.Infrastructure.dll'),
    (Join-Path $publishDirectory 'translation-worker\LayoutFix.TranslationWorker.dll')
)
foreach ($binary in $firstPartyBinaries) {
    Assert-ReleaseCondition (Test-Path -LiteralPath $binary -PathType Leaf) `
        'A first-party payload binary is missing.'
    $binaryVersion = (Get-Item -LiteralPath $binary).VersionInfo.ProductVersion.Trim()
    Assert-ReleaseCondition ($binaryVersion -eq $payloadVersion) `
        'First-party payload binaries do not share one source version.'
}

$privateNames = @('settings.json', 'translation_history.json', 'layoutfix.log')
$privateOrDebugFiles = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
    $_.Name -in $privateNames -or $_.Extension -eq '.pdb'
})
Assert-ReleaseCondition ($privateOrDebugFiles.Count -eq 0) `
    'Published payload contains private or debug files.'

$dictionaryCount = @(Get-ChildItem -LiteralPath (Join-Path $publishDirectory 'Dictionaries') `
    -Filter '*.txt' -File).Count
$localeCount = @(Get-ChildItem -LiteralPath (Join-Path $publishDirectory 'locales') `
    -Filter '*.json' -File).Count
Assert-ReleaseCondition ($dictionaryCount -eq 27) `
    "Published payload contains $dictionaryCount dictionaries instead of 27."
Assert-ReleaseCondition ($localeCount -eq 22) `
    "Published payload contains $localeCount locales instead of 22."

$mainRuntimeRoots = @(Get-ChildItem -LiteralPath (Join-Path $publishDirectory 'runtimes') `
    -Directory -ErrorAction SilentlyContinue)
$workerRuntimeRoots = @(Get-ChildItem `
    -LiteralPath (Join-Path $publishDirectory 'translation-worker\runtimes') `
    -Directory | ForEach-Object Name)
Assert-ReleaseCondition ($mainRuntimeRoots.Count -eq 0) `
    'Main payload contains unexpected platform runtime roots.'
Assert-ReleaseCondition (
    $workerRuntimeRoots.Count -eq 1 -and $workerRuntimeRoots[0] -eq 'win-x64') `
    'Translation worker runtime isolation is invalid.'

$signature = Get-AuthenticodeSignature -LiteralPath $installer
$hasTrustedSignature = $signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid
$hasTimestamp = $null -ne $signature.TimeStamperCertificate
$signedAndTimestamped = $hasTrustedSignature -and $hasTimestamp
$stableEligible = $signedAndTimestamped -or $AllowUnsigned
if (-not $stableEligible) {
    $reason = if (-not $hasTrustedSignature) {
        "authenticode-$($signature.Status.ToString().ToLowerInvariant())"
    }
    else {
        'authenticode-timestamp-missing'
    }
    throw "Stable release gate blocked: $reason."
}

$signatureStatus = if ($signedAndTimestamped) { 'signed' } else { 'unsigned-approved' }
"release_artifact=pass version=$version channel=stable signature=$signatureStatus " +
    "source_commit=$sourceCommit sha256=$actualHash dictionaries=$dictionaryCount " +
    "locales=$localeCount privacy=pass runtime_isolation=pass"
