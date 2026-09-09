param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$PackageDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'package'),
    [string]$ZipPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'manifest.json'

& (Join-Path $PSScriptRoot 'BuildPackage.ps1') -Configuration $Configuration -PackageDirectory $PackageDirectory
if (-not $?) {
    throw 'Package build failed.'
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $ZipPath = Join-Path $root "$($manifest.name)-$($manifest.version_number).zip"
}

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path (Join-Path $PackageDirectory '*') -DestinationPath $ZipPath -CompressionLevel Optimal

$inspectionDirectory = "$PackageDirectory.zip-inspection"
if (Test-Path $inspectionDirectory) {
    Remove-Item $inspectionDirectory -Recurse -Force
}
try {
    Expand-Archive -Path $ZipPath -DestinationPath $inspectionDirectory
    & (Join-Path $PSScriptRoot 'ValidatePackage.ps1') -PackageDirectory $inspectionDirectory
    if (-not $?) {
        throw 'ZIP inspection failed.'
    }
}
finally {
    if (Test-Path $inspectionDirectory) {
        Remove-Item $inspectionDirectory -Recurse -Force
    }
}

Write-Output "Thunderstore ZIP created: $ZipPath"
