<#
.SYNOPSIS
  Builds Release and produces the two release zips in dist\:
    ConSimVR-v<version>.zip           BepInEx 6 IL2CPP + ConSimVR; extract into the game folder.
    ConSimVR-v<version>-mod-only.zip  ConSimVR only, for an existing BepInEx 6 be.788 install.
#>
param(
    [string]$GameDir = "E:\SteamLibrary\steamapps\common\Construction Simulator",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (-not $SkipBuild) {
    dotnet build (Join-Path $root "ConSimVR.sln") -c Release -p:GameDir="$GameDir"
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$dist = Join-Path $root "dist"
$staging = Join-Path $dist "staging"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force $dist | Out-Null

function Stage-Mod([string]$target) {
    $patchers = Join-Path $target "BepInEx\patchers\ConSimVR"
    $plugins = Join-Path $target "BepInEx\plugins\ConSimVR"
    New-Item -ItemType Directory -Force $patchers, $plugins | Out-Null
    Copy-Item (Join-Path $root "src\ConSimVR.Il2CppFix\bin\Release\ConSimVR.Il2CppFix.dll") $patchers
    foreach ($file in "ConSimVR.dll", "ConSimVR.Core.dll", "openxr_loader.dll") {
        Copy-Item (Join-Path $root "src\ConSimVR\bin\Release\$file") $plugins
    }
    Copy-Item (Join-Path $root "src\ConSimVR.RemoteOperator\bin\Release\ConSimVR.RemoteOperator.dll") $plugins
    Copy-Item (Join-Path $root "packaging\ConSimVR-README.txt") $target
    Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $target
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $target "ConSimVR-LICENSE.txt")
}

# Mod only
$modOnly = Join-Path $staging "mod-only"
Stage-Mod $modOnly

# Full: unmodified BepInEx be.788 + mod
$full = Join-Path $staging "full"
$bepinex = Join-Path $root "vendor\bepinex"
if (-not (Test-Path (Join-Path $bepinex "winhttp.dll"))) { throw "vendor\bepinex is missing (extract BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788)." }
New-Item -ItemType Directory -Force $full | Out-Null
Get-ChildItem -Force $bepinex | ForEach-Object { Copy-Item -Recurse -Force $_.FullName $full }
Stage-Mod $full

Add-Type -AssemblyName System.IO.Compression.FileSystem
Remove-Item -Force (Join-Path $dist "SHA256SUMS.txt") -ErrorAction SilentlyContinue
$outputs = @{
    "ConSimVR-v$version.zip" = $full
    "ConSimVR-v$version-mod-only.zip" = $modOnly
}
foreach ($name in $outputs.Keys) {
    $zip = Join-Path $dist $name
    if (Test-Path $zip) { Remove-Item -Force $zip }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($outputs[$name], $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    "{0}  {1}" -f $hash, $name | Add-Content (Join-Path $dist "SHA256SUMS.txt")
    Write-Host ("{0}  {1:N1} MB  sha256 {2}" -f $name, ((Get-Item $zip).Length / 1MB), $hash)
}
Remove-Item -Recurse -Force $staging
