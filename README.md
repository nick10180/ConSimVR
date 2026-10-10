# ConSimVR: native OpenXR VR for Construction Simulator

Native stereo VR for **Construction Simulator** (weltenbauer, Unity 2020.3.49f1, HDRP, IL2CPP, D3D11), delivered as BepInEx 6 plugins:

| Component | Kind | What it does |
|---|---|---|
| `ConSimVR.Il2CppFix` | BepInEx preloader patcher | Makes BepInEx/Il2CppInterop work on this game at all (see below). |
| `ConSimVR` | BepInEx plugin | OpenXR session, per-eye HDRP rendering, head tracking, UI panel, first-person default. |
| `ConSimVR.RemoteOperator` | BepInEx plugin (optional) | Stand beside the machine for modes operated from outside, e.g. the flatbed truck's loading crane. |
| `ConSimVR.Core` | library | Engine-independent OpenXR bindings, D3D11 COM interop, VR math. Unit tested. |

## Install

**From a release (recommended):** download `ConSimVR-v1.0.0.zip` from [Releases](https://github.com/nick10180/ConSimVR/releases) and extract it into the game folder (Steam > Construction Simulator > Manage > Browse local files), so `BepInEx\`, `dotnet\` and `winhttp.dll` sit next to `ConSim.exe`. If you already run BepInEx 6 IL2CPP be.788, use `ConSimVR-v1.0.0-mod-only.zip` instead.

Then start SteamVR (or any active OpenXR runtime with D3D11 support) and the game. The first launch with BepInEx takes a few extra minutes while it generates interop assemblies. VR starts automatically once the game is up (`AutoStart`), or press **F9**. Tested with game build 1.0.0.4349.

Uninstall: delete `BepInEx\plugins\ConSimVR` and `BepInEx\patchers\ConSimVR` (and BepInEx itself if you like). No game files are modified.

**From source:** `powershell -File tools\install.ps1` builds and installs into the game (pass `-GameDir "..."` if needed; `tools\uninstall.ps1 [-RemoveBepInEx]` removes it). Building needs the game installed with BepInEx's generated interop assemblies (`GameDir` in `Directory.Build.props`). `tools\package.ps1` produces the release zips.

## Controls

| Key | Action |
|---|---|
| F9 | Start / stop VR |
| F8 | Recenter (seated position = your current head pose) |
| F10 | Show / hide the UI panel |
| F11 | Diagnostics: save the left eye, its swapchain image and the UI as PNGs in `BepInEx\plugins\ConSimVR` |
| F6 | *(RemoteOperator)* Switch between standing beside the machine and the cab (current machine mode) |
| F7 | *(RemoteOperator)* Back to the default spot beside the machine |
| Numpad 4 / 6 | *(RemoteOperator)* Walk around the machine |
| Numpad 8 / 2 | *(RemoteOperator)* Step closer / farther |

All keys and options are in `BepInEx\config\com.consimvr.vr.cfg` and `com.consimvr.remoteoperator.cfg`. Eyes render at the runtime's recommended size by default (`Rendering.RenderScale = 1`, `Rendering.MaxEyeDimension = 0` = no cap). With `Debug.Diagnostics` on, a frame-statistics line is logged every 2 seconds.

## How it works

**Rendering.** The game's HDRP was built without its XR-SDK code (`ENABLE_XR_MODULE` off), so Unity's OpenXR plugin could not drive it. HDRP's XR *pass* machinery (`XRSystem`, `XRPass`) survived, though. Its intended hook, `XRSystem.SetCustomLayout`, can't be used from Il2CppInterop: the delegate takes a non-blittable struct by value, which the delegate trampolines mis-marshal. Instead a Harmony prefix re-implements this build's (tiny) `XRSystem.SetupFrame` during VR frames. Each frame the plugin:

1. on `RenderPipelineManager.beginFrameRendering` (main thread): `xrWaitFrame`, `xrLocateViews` at the predicted display time, then computes eye world poses = *anchor* x *recentred head*;
2. in `XRSystem.SetupFrame`: gives the game camera two multi-pass XR passes (one per eye) with those view/projection matrices, a shared culling frustum enclosing both eyes, and an offscreen eye `RenderTexture` as target;
3. on Unity's **render thread** (`GL.IssuePluginEvent` into an `[UnmanagedCallersOnly]` callback): `xrBeginFrame`, copies the eye textures into the OpenXR swapchains with the game's own D3D11 device (`CopyResource`), then `xrEndFrame`.

The game camera's transform is never touched; head tracking lives only in the eye matrices, so game logic is unaffected.

**Camera.** Third-person requests are replaced with the first-person/cab camera wherever the current pawn or machine state has one (`WbMachineCameraController.ActivateCamera`, `WbCharacterCameraController.ActivateCamera`). While VR runs, mouse look in a cab, vertical mouse look on foot (horizontal still turns you), head bob and automatic look-back are suppressed so your head does the looking. Stopping VR (F9) restores normal flat-screen camera behaviour. In a cab the view tilts with the machine (`FollowVehicleTilt`); on foot the horizon stays level. While VR runs, motion blur, depth of field, lens distortion, chromatic aberration and panini projection are off on the VR camera (`Rendering.DisableUncomfortableEffects`).

**Remote-operated modes.** Machines keep a camera set per `WbMachineState` (`Drive`, `Function1`, ...). The flatbed truck's loading crane (`Function1`) has no operator seat: the game keeps showing the driving cab, though a real one is operated by remote. With `ConSimVR.RemoteOperator` installed you stand on the ground beside the machine instead, ~7 m from the hook and facing it. The rule: the mode's cab camera is the same one used for driving, and a hook is active in that mode (or the mode has no cab camera at all). Machines with a dedicated operator camera, like a mobile crane's crane cab, stay first person. You follow the machine but never turn on your own; look around with your head. F6 overrides the rule for the current machine mode, and the log line `Machine '...' mode ...` says why it decided.

**UI.** Screen-space UI does not render into the eyes. The HUD's blurred-world panel backgrounds are switched to a flat colour (`UI.FlatBackgrounds`), and a mouse cursor is drawn into the panel (`UI.ShowCursor`) because the Windows cursor is not part of the captured image. With `UI.Mode = Panel` the desktop back buffer is cleared to transparent black each frame, the UI draws on top, and the result is captured (`ScreenCapture.CaptureScreenshotIntoRenderTexture`) and shown as an OpenXR quad layer floating in front of you. The mouse keeps working because it is still the desktop screen. The desktop window then shows only the UI. Set `UI.Mode = Off` for a left-eye mirror on the desktop instead (no UI in the headset).

**Ghosting fix (VR and flat play).** The game shows ghost trails on bright edges and reflections under fast camera motion. It ships TAA with motion-vector rejection 0.35 and anti-flicker 0.35, and PBR Accumulation SSR on its global quality volume; both leave trails. With `Rendering.ReduceTaaGhosting`, if the camera uses TAA it is switched to High quality, which enables HDRP 10's motion-vector rejection and anti-ringing, with anti-flicker off (anti-flicker keeps bright history alive). With `Rendering.DisableSsrAccumulation`, screen-space reflections in PBR Accumulation mode are switched to Approximation. The game's original values are logged once (`Game camera anti-aliasing: ...`, `SSR on volume ...`). Choosing SMAA in the game's graphics settings removes TAA ghosting entirely, at the cost of more shimmer.

### Why the Il2CppFix patcher exists

`GameAssembly.dll` is protected (Denuvo-style sections), and every `il2cpp_*` export is wrapped as `call <init-wait stub>; jmp <real body>`. Il2CppInterop finds the libil2cpp functions it must hook by walking calls out of those exports. On this build that walk throws (`Sequence contains more than one element`, so no plugin loads). Worse, for two hooks it would silently pick the wrong function (the stub itself, and a signature false positive). The patcher resolves the six targets by following the wrapper and validating each function's shape, then hands them to Il2CppInterop through Harmony prefixes. It does not modify or bypass the protection, and on unwrapped builds it does nothing. `ProtectedExportResolverTests` checks the resolution against the installed `GameAssembly.dll`.

## Build and test

```
dotnet build ConSimVR.sln -c Release          # needs the game + BepInEx interop assemblies (GameDir in Directory.Build.props)
dotnet test tests/ConSimVR.Tests              # math, OpenXR struct layouts, D3D11 copy on the real GPU, resolver vs GameAssembly.dll
```

The tests confirm the components (bindings, interop, math, address resolution) and deliberately stop short of in-headset behaviour. That needs the game and a headset.

## Known limitations / next steps

- Multi-pass stereo (the game's shaders have no single-pass-instanced variants), so GPU cost is about two renders per frame. Lower `RenderScale` if needed.
- While VR runs the game is kept **windowed** (`General.WindowMode`); exclusive fullscreen turned the headset image white. The game's own mode comes back when VR stops. The UI panel follows window resizes.
- The UI panel is fixed in front of the seat.
- No motion-controller input yet: play with keyboard, gamepad or wheel.
- Navigation markers (destination dot and distance) are placed on the UI panel where the line from your head to the target crosses it (`UI.AlignWorldMarkers`), so they sit over the destination; outside the panel the game's edge arrow points the way. Other screen-space world icons (cab interaction prompts) are not aligned yet.

## Licence

MIT, see [LICENSE](LICENSE). The release package bundles BepInEx and the Khronos OpenXR loader under their own licences; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Not affiliated with weltenbauer. or astragon.
