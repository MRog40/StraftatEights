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
$stagingDirectory = "$PackageDirectory.staging"

if (-not (Test-Path $iconPath)) {
    throw "Package icon is missing: $iconPath"
}

& (Join-Path $PSScriptRoot 'ValidateDependencies.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Dependency validation failed with exit code $LASTEXITCODE."
}

& dotnet build $projectPath --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

if (Test-Path $stagingDirectory) {
    Remove-Item $stagingDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

$files = @(
    'manifest.json',
    'README.md'
)
foreach ($file in $files) {
    Copy-Item (Join-Path $root $file) $stagingDirectory
}
Copy-Item $iconPath (Join-Path $stagingDirectory 'icon.png')
Copy-Item (Join-Path $outputDirectory 'StraftatEightsPlugin.dll') $stagingDirectory

& (Join-Path $PSScriptRoot 'ValidatePackage.ps1') -PackageDirectory $stagingDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Package validation failed with exit code $LASTEXITCODE."
}

if (Test-Path $PackageDirectory) {
    Remove-Item $PackageDirectory -Recurse -Force
}
Move-Item $stagingDirectory $PackageDirectory

Write-Output "Package staged successfully: $PackageDirectory"