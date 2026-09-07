param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$PackageDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'package')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root 'StraftatEightsPlugin.csproj'
$outputDirectory = Join-Path $root "bin\$Configuration\netstandard2.1"
$iconPath = Join-Path (Split-Path -Parent $root) 'icon.png'

if (-not (Test-Path $iconPath)) {
    throw "Package icon is missing: $iconPath"
}

& dotnet build $projectPath --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

if (Test-Path $PackageDirectory) {
    Remove-Item $PackageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $PackageDirectory -Force | Out-Null

$files = @(
    'manifest.json',
    'README.md'
)
foreach ($file in $files) {
    Copy-Item (Join-Path $root $file) $PackageDirectory
}
Copy-Item $iconPath (Join-Path $PackageDirectory 'icon.png')
Copy-Item (Join-Path $outputDirectory 'StraftatEightsPlugin.dll') $PackageDirectory

& (Join-Path $PSScriptRoot 'ValidatePackage.ps1') -PackageDirectory $PackageDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Package validation failed with exit code $LASTEXITCODE."
}

Write-Output "Package staged successfully: $PackageDirectory"