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
    $topLevelFiles = @(Get-ChildItem $PackageDirectory -File | Select-Object -ExpandProperty Name)
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path (Join-Path $PackageDirectory $file))) {
            $errors.Add("Required package file is missing: $file")
        }
    }
    foreach ($file in $topLevelFiles) {
        if ($requiredFiles -notcontains $file) {
            $errors.Add("Unexpected top-level package file: $file")
        }
    }

    $iconFile = Join-Path $PackageDirectory 'icon.png'
    if (Test-Path $iconFile) {
        $iconBytes = [System.IO.File]::ReadAllBytes($iconFile)
        $pngSignature = @(137, 80, 78, 71, 13, 10, 26, 10)
        $hasPngSignature = $iconBytes.Length -ge 24
        for ($index = 0; $hasPngSignature -and $index -lt $pngSignature.Count; $index++) {
            $hasPngSignature = $iconBytes[$index] -eq $pngSignature[$index]
        }
        if (-not $hasPngSignature) {
            $errors.Add('icon.png is not a valid PNG file.')
        }
        else {
            $iconWidth = ([int]$iconBytes[16] -shl 24) -bor ([int]$iconBytes[17] -shl 16) -bor ([int]$iconBytes[18] -shl 8) -bor [int]$iconBytes[19]
            $iconHeight = ([int]$iconBytes[20] -shl 24) -bor ([int]$iconBytes[21] -shl 16) -bor ([int]$iconBytes[22] -shl 8) -bor [int]$iconBytes[23]
            if ($iconWidth -ne 256 -or $iconHeight -ne 256) {
                $errors.Add("icon.png must be 256x256 pixels; found ${iconWidth}x${iconHeight}.")
            }
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
