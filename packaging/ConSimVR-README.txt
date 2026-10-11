ConSimVR - native OpenXR VR for Construction Simulator
======================================================

Requirements
------------
- Construction Simulator (2022, Steam). Tested with game build 1.0.0.4349.
- Windows 10/11 x64, a D3D11-capable GPU (the game's default renderer).
- An OpenXR runtime with D3D11 support set as active, e.g. SteamVR.
- Internet access on the first launch (BepInEx downloads Unity base libraries once).

Install (full package)
----------------------
1. Open the game folder: Steam > Construction Simulator > right click > Manage > Browse local files.
2. Extract the whole zip into that folder, so BepInEx\, dotnet\ and winhttp.dll sit next to ConSim.exe.
   (Already have BepInEx 6 IL2CPP be.788 installed? Use the "mod-only" zip instead.)
3. Start SteamVR, then the game. The FIRST launch takes a few extra minutes while BepInEx
   generates its interop assemblies; later launches are normal.
4. VR starts by itself once the game is up (or press F9). While VR runs the game is kept windowed.

Controls
--------
F9    start / stop VR
F8    recenter (your current head pose becomes the seat position)
F10   show / hide the UI panel
F11   diagnostics: save the eye/UI images as PNGs in BepInEx\plugins\ConSimVR
F6    remote crane view: switch between standing beside the machine and the cab
F7    remote crane view: back to the default spot
Numpad 4 / 6 / 8 / 2   remote crane view: walk around / step closer / step back

Settings: BepInEx\config\com.consimvr.vr.cfg and com.consimvr.remoteoperator.cfg
(created on first launch; edit with the game closed).

Uninstall
---------
Delete BepInEx\plugins\ConSimVR and BepInEx\patchers\ConSimVR.
To remove BepInEx too, delete BepInEx\, dotnet\, winhttp.dll, doorstop_config.ini and .doorstop_version.
No game files are modified.

Troubleshooting
---------------
Log: BepInEx\LogOutput.log (lines from "ConSimVR"). Please attach it to bug reports.
- White headset at loading screens is SteamVR's own loading view; it returns once the game renders.
- Nothing in the headset: check the OpenXR runtime is active (SteamVR > Settings > OpenXR).

Source, issues and docs: https://github.com/nick10180/ConSimVR
