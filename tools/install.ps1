<#
.SYNOPSIS
  Builds ConSimVR and installs it (plus BepInEx if missing) into Construction Simulator.

.PARAMETER GameDir
  Game install folder. Defaults to the Steam library path used during development.

.PARAMETER SkipBuild
  Install the existing Release build output without rebuilding.

.PARAMETER NoRemoteOperator
  Do not install the ConSimVR.RemoteOperator companion plugin.
#>
param(
    [string]$GameDir = "E:\SteamLibrary\steamapps\common\Construction Simulator",
    [switch]$SkipBuild,
    [switch]$NoRemoteOperator
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path (Join-Path $GameDir "ConSim.exe"))) {
    throw "ConSim.exe not found in '$GameDir'. Pass -GameDir."
}

# BepInEx 6 IL2CPP (bleeding edge) - only copied when not installed yet. It never overwrites game files.
$bepinex = Join-Path $GameDir "BepInEx\core\BepInEx.Unity.IL2CPP.dll"
if (-not (Test-Path $bepinex)) {
    $vendor = Join-Path $root "vendor\bepinex"
    if (-not (Test-Path (Join-Path $vendor "winhttp.dll"))) {
        throw "BepInEx is not installed and vendor\bepinex is missing. Extract BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788 into vendor\bepinex."
    }
    Write-Host "Installing BepInEx into $GameDir"
    Copy-Item -Recurse -Force (Join-Path $vendor "*") $GameDir
    Copy-Item -Force (Join-Path $vendor ".doorstop_version") $GameDir -ErrorAction SilentlyContinue
}

$interop = Join-Path $GameDir "BepInEx\interop\Wb.GameScripts.dll"
if (-not (Test-Path $interop)) {
    Write-Warning "BepInEx interop assemblies are missing. Launch the game once (with the Il2CppFix patcher installed) so BepInEx generates them, then rerun this script."
}

if (-not $SkipBuild) {
    dotnet build (Join-Path $root "ConSimVR.sln") -c Release -p:GameDir="$GameDir"
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$patcherDir = Join-Path $GameDir "BepInEx\patchers\ConSimVR"
$pluginDir = Join-Path $GameDir "BepInEx\plugins\ConSimVR"
New-Item -ItemType Directory -Force $patcherDir, $pluginDir | Out-Null

Copy-Item -Force (Join-Path $root "src\ConSimVR.Il2CppFix\bin\Release\ConSimVR.Il2CppFix.dll") $patcherDir
$pluginOut = Join-Path $root "src\ConSimVR\bin\Release"
foreach ($file in "ConSimVR.dll", "ConSimVR.Core.dll", "openxr_loader.dll") {
    $source = Join-Path $pluginOut $file
    if (Test-Path $source) { Copy-Item -Force $source $pluginDir }
}
if (-not $NoRemoteOperator) {
    $remote = Join-Path $root "src\ConSimVR.RemoteOperator\bin\Release\ConSimVR.RemoteOperator.dll"
    if (Test-Path $remote) { Copy-Item -Force $remote $pluginDir }
}

Write-Host "Installed:"
Get-ChildItem $patcherDir, $pluginDir | ForEach-Object { Write-Host "  $($_.FullName)" }
