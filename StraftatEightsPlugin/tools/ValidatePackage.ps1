param(
    [string]$PackageDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'package')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'manifest.json'
$projectPath = Join-Path $root 'StraftatEightsPlugin.csproj'
$errors = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path $manifestPath)) {
    $errors.Add('manifest.json is missing.')
}
if (-not (Test-Path $projectPath)) {
    $errors.Add('StraftatEightsPlugin.csproj is missing.')
}
if ($errors.Count -eq 0) {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    [xml]$project = Get-Content $projectPath -Raw
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode) {
        $errors.Add('The project Version property is missing.')
    }
    elseif ($manifest.version_number -ne $versionNode.InnerText) {
        $errors.Add("Manifest version '$($manifest.version_number)' does not match project version '$($versionNode.InnerText)'.")
    }
}

if (-not (Test-Path $PackageDirectory)) {
    $errors.Add("Package directory '$PackageDirectory' does not exist.")
}
else {
    $requiredFiles = @('manifest.json', 'README.md', 'icon.png', 'StraftatEightsPlugin.dll')
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path (Join-Path $PackageDirectory $file))) {
            $errors.Add("Required package file is missing: $file")
        }
    }

    $forbiddenNames = @(
        'Assembly-CSharp.dll',
        'FishNet.Runtime.dll',
        'MyceliumNetworkingForStraftat.dll',
        'BepInEx.dll',
        'UnityEngine.dll'
    )
    foreach ($file in Get-ChildItem $PackageDirectory -Recurse -File) {
        if ($forbiddenNames -contains $file.Name) {
            $errors.Add("Game or dependency assembly must not ship: $($file.Name)")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Package validation passed: $PackageDirectory"
