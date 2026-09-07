param(
    [string]$ProjectPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'StraftatEightsPlugin.csproj'),
    [string]$GameManagedDir,
    [string]$BepInExPluginsDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$baselinePath = Join-Path $PSScriptRoot 'DependencyBaseline.json'
$errors = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path $ProjectPath)) {
    $errors.Add("Project file is missing: $ProjectPath")
}
if (-not (Test-Path $baselinePath)) {
    $errors.Add("Dependency baseline is missing: $baselinePath")
}
if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

[xml]$project = Get-Content $ProjectPath -Raw
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json

function Get-ProjectDefault([string]$name) {
    $node = $project.SelectSingleNode("/Project/PropertyGroup/$name")
    if ($null -eq $node) {
        throw "Project property is missing: $name"
    }
    return $node.InnerText
}

if ([string]::IsNullOrWhiteSpace($GameManagedDir)) {
    $GameManagedDir = if ([string]::IsNullOrWhiteSpace($env:GameManagedDir)) {
        Get-ProjectDefault 'GameManagedDir'
    } else {
        $env:GameManagedDir
    }
}
if ([string]::IsNullOrWhiteSpace($BepInExPluginsDir)) {
    $BepInExPluginsDir = if ([string]::IsNullOrWhiteSpace($env:BepInExPluginsDir)) {
        Get-ProjectDefault 'BepInExPluginsDir'
    } else {
        $env:BepInExPluginsDir
    }
}

$assemblyPaths = [ordered]@{
    'Assembly-CSharp' = Join-Path $GameManagedDir 'Assembly-CSharp.dll'
    'FishNet.Runtime' = Join-Path $GameManagedDir 'FishNet.Runtime.dll'
    'ComputerysModdingUtilities' = Join-Path $GameManagedDir 'ComputerysModdingUtilities.dll'
    'MyceliumNetworkingForStraftat' = Join-Path $BepInExPluginsDir 'straftatmodding-MyceliumNetworking\MyceliumNetworkingForStraftat.dll'
}

foreach ($entry in $assemblyPaths.GetEnumerator()) {
    if (-not (Test-Path $entry.Value)) {
        $errors.Add("Required assembly is missing: $($entry.Key) at $($entry.Value)")
        continue
    }

    try {
        $identity = [System.Reflection.AssemblyName]::GetAssemblyName($entry.Value).FullName
    }
    catch {
        $errors.Add("Could not read assembly identity: $($entry.Value)")
        continue
    }

    $expected = $baseline.Assemblies.PSObject.Properties[$entry.Key].Value
    if ($identity -ne $expected) {
        $errors.Add("Assembly identity mismatch for $($entry.Key): expected '$expected', found '$identity'.")
    }
    else {
        Write-Output "Assembly verified: $identity"
    }
}

$assetsPath = Join-Path $root 'obj\project.assets.json'
if (-not (Test-Path $assetsPath)) {
    $errors.Add("Restore assets are missing: $assetsPath")
}
else {
    $assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
    $resolvedPackages = @($assets.libraries.PSObject.Properties.Name)
    foreach ($package in $baseline.Packages) {
        if ($resolvedPackages -notcontains $package) {
            $errors.Add("Resolved package is missing or changed: $package")
        }
        else {
            Write-Output "Package verified: $package"
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output 'Dependency validation passed.'
