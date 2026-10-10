<#
.SYNOPSIS
  Removes ConSimVR from Construction Simulator. With -RemoveBepInEx also removes BepInEx itself.
#>
param(
    [string]$GameDir = "E:\SteamLibrary\steamapps\common\Construction Simulator",
    [switch]$RemoveBepInEx
)

$ErrorActionPreference = "Stop"

foreach ($dir in "BepInEx\plugins\ConSimVR", "BepInEx\patchers\ConSimVR") {
    $path = Join-Path $GameDir $dir
    if (Test-Path $path) {
        Remove-Item -Recurse -Force $path
        Write-Host "Removed $path"
    }
}
$config = Join-Path $GameDir "BepInEx\config"
Get-ChildItem $config -Filter "com.consimvr.*.cfg" -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item -Force $_.FullName
    Write-Host "Removed $($_.FullName)"
}

if ($RemoveBepInEx) {
    # Only the files BepInEx 6 adds next to ConSim.exe; game files are untouched.
    foreach ($item in "BepInEx", "dotnet", "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt") {
        $path = Join-Path $GameDir $item
        if (Test-Path $path) {
            Remove-Item -Recurse -Force $path
            Write-Host "Removed $path"
        }
    }
}
