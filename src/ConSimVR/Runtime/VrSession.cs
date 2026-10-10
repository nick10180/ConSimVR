using System;
using System.IO;
using ConSimVR.Core.Graphics;
using ConSimVR.Core.Math;
using ConSimVR.Core.OpenXR;
using UnityEngine;
using CorePose = ConSimVR.Core.Math.Pose;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace ConSimVR
{
    /// <summary>Per-frame data handed from the main thread to the render thread.</summary>
    internal sealed class FrameRecord
    {
        public long DisplayTime;
        public bool ShouldRender;
        public bool HasEyes;
        public bool HasQuad;
        public readonly ProjectionView[] Views = new ProjectionView[2];
        public QuadLayer Quad;
        public volatile bool Begun;

        /// <summary>Eye poses in Unity world space for this frame (main thread only).</summary>
        public readonly CorePose[] EyeWorld = new CorePose[2];
        public readonly Fov[] EyeFov = new Fov[2];
    }

    /// <summary>
    /// Owns the OpenXR session and everything submitted to it. Main-thread methods drive xrWaitFrame and pose
    /// prediction; <see cref="OnRenderThread"/> runs xrBeginFrame/xrEndFrame and copies the eye and UI textures into
    /// the runtime's swapchains with the game's own D3D11 device.
    /// </summary>
    internal sealed class VrSession
    {
        private const int FrameSlots = 4;
        private const string ApplicationName = "Construction Simulator VR";

        public static VrSession Instance { get; private set; }

        private readonly OpenXrRuntime _xr;
        private readonly FrameRecord[] _frames = new FrameRecord[FrameSlots];
        private readonly XrSwapchainInfo[] _eyeSwapchains = new XrSwapchainInfo[2];
        private readonly NativeRenderTexture[] _eyeTextures = new NativeRenderTexture[2];
        private readonly StereoRenderer _renderer;
        private readonly UiOverlay _ui;
        private readonly UiBlurOverride _blurOverride = new();

        private IntPtr _device;
        private IntPtr _context; // render thread only
        private int _frameCounter;
        private int _lastXrFrameUnityFrame = -1;
        private bool _recenterRequested = true;
        private CorePose _reference = CorePose.Identity;
        private int _savedVSync;
        private bool _savedRunInBackground;
        private bool _settingsApplied;
        private bool _wasRunning;
        private bool _hasViewer;
        private CorePose _lastAnchor;
        private NVector3 _lastHeadWorld;
        private float _lastWorldScale = 1f;
        private int _nextWindowModeCheck;
        private FullScreenMode? _savedWindowMode;

        private struct FrameStats
        {
            public int Waited, Stereo, UiOnly, Begun, Ended, EndFailed, EyeFrames, QuadFrames, CopyFailed;
        }

        private const float StatsInterval = 2f;
        private FrameStats _stats;
        private float _nextStatsTime;
        private int _statsFrame;
        private volatile bool _dumpRequested;
        private readonly System.Collections.Generic.HashSet<string> _loggedDescs = new();

        public int EyeWidth { get; private set; }
        public int EyeHeight { get; private set; }
        public long SwapchainFormat { get; private set; }
        public RenderTexture LeftEyeTexture => _eyeTextures[0]?.Texture;
        public RenderTexture GetEyeTexture(int eye) => _eyeTextures[eye]?.Texture;
        public bool IsRunning => _xr.IsSessionRunning;
        public CorePose TrackingReference => _reference;

        private VrSession()
        {
            string loader = Path.Combine(Plugin.PluginDir, "openxr_loader.dll");
            _xr = new OpenXrRuntime(loader, Plugin.VrLog);
            for (int i = 0; i < FrameSlots; i++)
                _frames[i] = new FrameRecord();
            _renderer = new StereoRenderer(this);
            _ui = new UiOverlay(this);
        }

        /// <summary>Creates the OpenXR instance, session, swapchains and eye textures. Main thread.</summary>
        public static bool TryStart(out string error)
        {
            if (Instance != null)
            {
                error = null;
                return true;
            }

            var session = new VrSession();
            if (!session.Initialize(out error))
            {
                session.Destroy();
                return false;
            }
            Instance = session;
            return true;
        }

        /// <summary>Stops VR and releases everything. Main thread.</summary>
        public static void Stop()
        {
            var session = Instance;
            if (session == null)
                return;
            Instance = null; // render-thread events processed from now on are ignored
            // Wait until the render thread has worked through every frame we queued before releasing what it uses.
            if (!RenderThread.WaitForFence(TimeSpan.FromSeconds(1)))
                Plugin.Log.LogWarning("Render thread did not drain within 1s; shutting OpenXR down anyway.");
            session.Destroy();
        }

        private bool Initialize(out string error)
        {
            if (!_xr.CreateInstance(ApplicationName, out error))
                return false;
            Plugin.Log.LogInfo($"OpenXR runtime: {_xr.RuntimeName}, system: {_xr.SystemName}, recommended eye size {_xr.RecommendedWidth}x{_xr.RecommendedHeight}");

            if (!AcquireDevice(out error))
                return false;
            if (!_xr.CreateSession(_device, out error))
                return false;
            if (!ChooseSwapchainFormat(out var textureFormat, out error))
                return false;

            float scale = VrConfig.RenderScale.Value;
            int maxDimension = VrConfig.MaxEyeDimension.Value;
            float largest = Math.Max(_xr.RecommendedWidth, _xr.RecommendedHeight) * scale;
            if (maxDimension > 0 && largest > maxDimension)
            {
                Plugin.Log.LogInfo($"Clamping eye resolution from {_xr.RecommendedWidth * scale:0}x{_xr.RecommendedHeight * scale:0} to at most {maxDimension} (Rendering.MaxEyeDimension).");
                scale *= maxDimension / largest;
            }
            EyeWidth = MakeEven(_xr.RecommendedWidth * scale);
            EyeHeight = MakeEven(_xr.RecommendedHeight * scale);
            for (int eye = 0; eye < 2; eye++)
            {
                _eyeTextures[eye] = CreateTexture($"ConSimVR Eye {eye}", EyeWidth, EyeHeight, textureFormat, out error);
                if (_eyeTextures[eye] == null)
                    return false;
                _eyeSwapchains[eye] = _xr.CreateSwapchain(SwapchainFormat, EyeWidth, EyeHeight, out error);
                if (_eyeSwapchains[eye] == null)
                    return false;
            }
            Plugin.Log.LogInfo($"Eye render targets: {EyeWidth}x{EyeHeight}, swapchain format {SwapchainFormat}");

            EnforceWindowMode();
            if (VrConfig.UiPanel.Value == UiPanelMode.Panel && !_ui.Initialize(textureFormat, out error))
                Plugin.Log.LogWarning($"UI panel unavailable: {error}");

            _xr.ReferenceSpaceChangePending += RequestRecenter;
            _renderer.Attach();

            _savedVSync = QualitySettings.vSyncCount;
            _savedRunInBackground = Application.runInBackground;
            QualitySettings.vSyncCount = 0;       // the headset paces frames via xrWaitFrame
            Application.runInBackground = true;   // keep running while the headset has focus
            _settingsApplied = true;
            error = null;
            return true;
        }

        private bool AcquireDevice(out string error)
        {
            // Any texture's native pointer leads back to the device Unity renders with.
            var probe = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            probe.Create();
            IntPtr native = probe.GetNativeTexturePtr();
            _device = native != IntPtr.Zero ? D3D11.GetDevice(native) : IntPtr.Zero;
            probe.Release();
            UnityEngine.Object.Destroy(probe);

            if (_device == IntPtr.Zero)
            {
                error = "Could not get the game's D3D11 device. VR needs the D3D11 renderer (start the game without -force-d3d12/-force-vulkan).";
                return false;
            }

            int featureLevel = D3D11.GetFeatureLevel(_device);
            if (featureLevel < _xr.MinFeatureLevel)
                Plugin.Log.LogWarning($"Game D3D11 feature level 0x{featureLevel:X} is below the runtime minimum 0x{_xr.MinFeatureLevel:X}.");
            if (D3D11.TryGetAdapterLuid(_device, out ulong luid) && _xr.AdapterLuid != 0 && luid != _xr.AdapterLuid)
                Plugin.Log.LogWarning("The game renders on a different GPU than the one the headset is attached to; expect poor performance or a failed session.");

            error = null;
            return true;
        }

        private bool ChooseSwapchainFormat(out RenderTextureFormat textureFormat, out string error)
        {
            foreach (long format in _xr.SwapchainFormats)
            {
                if (format == DxgiFormat.R8G8B8A8_UNORM_SRGB)
                {
                    SwapchainFormat = format;
                    textureFormat = RenderTextureFormat.ARGB32;
                    error = null;
                    return true;
                }
                if (format == DxgiFormat.B8G8R8A8_UNORM_SRGB)
                {
                    SwapchainFormat = format;
                    textureFormat = RenderTextureFormat.BGRA32;
                    error = null;
                    return true;
                }
            }
            textureFormat = default;
            error = "The OpenXR runtime offers no 8-bit sRGB swapchain format.";
            return false;
        }

        internal NativeRenderTexture CreateTexture(string name, int width, int height, RenderTextureFormat format, out string error) =>
            NativeRenderTexture.Create(name, width, height, format, (uint)SwapchainFormat, out error);

        internal XrSwapchainInfo CreateSwapchain(int width, int height, out string error) =>
            _xr.CreateSwapchain(SwapchainFormat, width, height, out error);

        internal void DestroySwapchain(XrSwapchainInfo swapchain) => _xr.DestroySwapchain(swapchain);

        /// <summary>Keeps the game out of exclusive fullscreen while VR runs (checked about once a second).</summary>
        private void EnforceWindowMode()
        {
            var option = VrConfig.WindowMode.Value;
            if (option == WindowModeOption.Unchanged || Time.frameCount < _nextWindowModeCheck)
                return;
            _nextWindowModeCheck = Time.frameCount + 60;
            var desired = option == WindowModeOption.Borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var current = Screen.fullScreenMode;
            if (current == desired)
                return;
            _savedWindowMode ??= current;
            Screen.fullScreenMode = desired;
            Plugin.Log.LogInfo($"Display mode {current} -> {desired} for VR (General.WindowMode).");
        }

        private static int MakeEven(float value) => Math.Max(2, ((int)Math.Round(value) + 1) & ~1);

        public void RequestRecenter() => _recenterRequested = true;

        public void ToggleUi() => _ui.ToggleVisible();

        /// <summary>
        /// Screen position (pixels, like Camera.WorldToScreenPoint) at which a world point appears on the VR UI panel from
        /// the player's head, using the latest predicted head pose. False when the panel is not showing.
        /// </summary>
        public bool TryProjectToUiPanel(Vector3 world, out Vector3 screen)
        {
            screen = default;
            if (!_hasViewer || !_xr.IsSessionRunning || !_ui.IsShowing)
                return false;
            float scale = _lastWorldScale;
            float width = VrConfig.UiWidth.Value * scale;
            float height = width * _ui.Height / Math.Max(1, _ui.Width);
            var offset = new NVector3(0f, VrConfig.UiHeightOffset.Value, VrConfig.UiDistance.Value) * scale;
            var panel = new CorePose(_lastAnchor.TransformPoint(offset), _lastAnchor.Rotation);
            screen = Core.Rig.UiPanelProjection.ToScreen(panel, width, height, _lastHeadWorld, world.ToCore(), Screen.width, Screen.height).ToUnity();
            return true;
        }

        /// <summary>Main thread, after the stereo camera rendered and before the overlay UI draws.</summary>
        public void AfterStereoRender() => _blurOverride.BindFlatBackground();

        // ------------------------------------------------------------------ main thread

        /// <summary>Called from Update: pumps OpenXR events and reacts to session state.</summary>
        public void Update()
        {
            _xr.PollEvents();
            if (_xr.ExitRequested)
            {
                Plugin.Log.LogInfo("OpenXR runtime requested exit; stopping VR.");
                Stop();
                return;
            }

            if (_xr.IsSessionRunning && !_wasRunning)
            {
                Plugin.Log.LogInfo("OpenXR session running.");
                CameraPatches.SwitchToFirstPersonNow();
                RequestRecenter();
            }
            _wasRunning = _xr.IsSessionRunning;
            EnforceWindowMode();
            if (_wasRunning)
                _blurOverride.Update();
            if (_wasRunning && VrConfig.Diagnostics.Value)
                LogStats();
        }

        public void LateUpdate() => _ui.LateUpdate();

        /// <summary>
        /// Starts an OpenXR frame for the stereo camera: waits for the runtime's frame timing, predicts the eye poses and
        /// queues xrBeginFrame on the render thread. Returns null when no frame should be rendered now.
        /// </summary>
        public FrameRecord BeginStereoFrame(Camera camera)
        {
            if (!_xr.IsSessionRunning || _lastXrFrameUnityFrame == Time.frameCount)
                return null;

            var frame = WaitFrame();
            if (frame == null)
                return null;

            // A waited frame must always be begun and ended, or the next xrWaitFrame blocks forever.
            try
            {
                if (frame.ShouldRender && _eyeTextures[0].EnsureValid() && _eyeTextures[1].EnsureValid())
                    frame.HasEyes = LocateEyes(frame, CameraRig.ComputeAnchor(camera));
                AddUiQuad(frame);
            }
            catch (Exception e)
            {
                frame.HasEyes = false;
                Plugin.Log.LogError($"Eye pose update failed: {e}");
            }
            _stats.Stereo++;
            RenderThread.Issue(RenderThread.Op.BeginFrame, Slot(frame));
            return frame;
        }

        /// <summary>Queues xrEndFrame after everything rendered for <paramref name="frame"/>.</summary>
        public void EndStereoFrame(FrameRecord frame) => RenderThread.Issue(RenderThread.Op.EndFrame, Slot(frame));

        /// <summary>
        /// End of the Unity frame. When the stereo camera did not render this frame (loading screens, menus without a
        /// world camera) still submit a frame with just the UI panel so the headset never stalls.
        /// </summary>
        public void OnEndOfFrame()
        {
            _renderer.FlushPendingFrame();
            if (!_xr.IsSessionRunning || _lastXrFrameUnityFrame == Time.frameCount)
                return;
            var frame = WaitFrame();
            if (frame == null)
                return;
            _stats.UiOnly++;
            try
            {
                AddUiQuad(frame);
            }
            finally
            {
                RenderThread.Issue(RenderThread.Op.BeginFrame, Slot(frame));
                RenderThread.Issue(RenderThread.Op.EndFrame, Slot(frame));
            }
        }

        private FrameRecord WaitFrame()
        {
            if (!_xr.WaitFrame(out var state))
                return null;
            _lastXrFrameUnityFrame = Time.frameCount;
            _stats.Waited++;
            var frame = _frames[_frameCounter++ % FrameSlots];
            frame.DisplayTime = state.PredictedDisplayTime;
            frame.ShouldRender = state.ShouldRender != 0;
            frame.HasEyes = false;
            frame.HasQuad = false;
            return frame;
        }

        private int Slot(FrameRecord frame) => Array.IndexOf(_frames, frame);

        private bool LocateEyes(FrameRecord frame, CorePose anchor)
        {
            Span<XrView> views = stackalloc XrView[2];
            if (!_xr.LocateViews(frame.DisplayTime, views, out ulong flags))
                return false;
            const ulong orientationValid = 0x1, positionValid = 0x2;
            if ((flags & orientationValid) == 0)
                return false;

            if (_recenterRequested && (flags & positionValid) != 0 && _xr.LocateHead(frame.DisplayTime, out var head))
            {
                _reference = Recenter.ReferenceFromHead(ToUnity(head));
                _recenterRequested = false;
                VrApi.RaiseRecentered();
            }

            float worldScale = VrConfig.WorldScale.Value;
            for (int eye = 0; eye < 2; eye++)
            {
                var local = Recenter.Apply(_reference, ToUnity(views[eye].Pose));
                var scaled = new CorePose(local.Position * worldScale, local.Rotation);
                frame.EyeWorld[eye] = anchor * scaled;
                if (eye == 1)
                {
                    _lastAnchor = anchor;
                    _lastHeadWorld = (frame.EyeWorld[0].Position + frame.EyeWorld[1].Position) * 0.5f;
                    _lastWorldScale = worldScale;
                    _hasViewer = true;
                }
                frame.EyeFov[eye] = new Fov(views[eye].Fov.AngleLeft, views[eye].Fov.AngleRight, views[eye].Fov.AngleUp, views[eye].Fov.AngleDown);
                frame.Views[eye] = new ProjectionView
                {
                    Pose = views[eye].Pose,
                    Fov = views[eye].Fov,
                    Swapchain = _eyeSwapchains[eye].Handle,
                    Width = EyeWidth,
                    Height = EyeHeight,
                };
            }
            return true;
        }

        private void AddUiQuad(FrameRecord frame)
        {
            if (!_ui.TryGetLayer(_reference, out frame.Quad))
                return;
            frame.HasQuad = true;
        }

        internal static CorePose ToUnity(XrPosef pose) => XrConvert.ToUnity(
            new NQuaternion(pose.Orientation.X, pose.Orientation.Y, pose.Orientation.Z, pose.Orientation.W),
            new NVector3(pose.Position.X, pose.Position.Y, pose.Position.Z));

        internal static XrPosef ToOpenXr(CorePose pose)
        {
            XrConvert.ToOpenXr(pose, out var rotation, out var position);
            return new XrPosef(
                new XrQuaternionf(rotation.X, rotation.Y, rotation.Z, rotation.W),
                new XrVector3f(position.X, position.Y, position.Z));
        }

        // ------------------------------------------------------------------ render thread

        public void OnRenderThread(RenderThread.Op op, int slot)
        {
            if (slot >= FrameSlots)
                return;
            var frame = _frames[slot];
            switch (op)
            {
                case RenderThread.Op.BeginFrame:
                    frame.Begun = _xr.BeginFrame();
                    if (frame.Begun)
                        _stats.Begun++;
                    break;
                case RenderThread.Op.EndFrame:
                    if (frame.Begun)
                        SubmitFrame(frame);
                    frame.Begun = false;
                    break;
            }
        }

        private void SubmitFrame(FrameRecord frame)
        {
            if (_context == IntPtr.Zero)
                _context = D3D11.GetImmediateContext(_device);

            bool dump = _dumpRequested;
            bool eyes = frame.ShouldRender && frame.HasEyes;
            for (int eye = 0; eye < 2 && eyes; eye++)
                eyes = CopyToSwapchain(_eyeSwapchains[eye], _eyeTextures[eye].Native, eye == 0 ? "eye0" : null, dump);
            if (eyes)
                _stats.EyeFrames++;

            bool quad = frame.ShouldRender && frame.HasQuad && CopyToSwapchain(_ui.Swapchain, _ui.Native, "ui", dump);
            if (quad)
                _stats.QuadFrames++;
            if (dump && eyes)
                _dumpRequested = false;

            var views = new ReadOnlySpan<ProjectionView>(frame.Views, 0, eyes ? 2 : 0);
            if (_xr.EndFrame(frame.DisplayTime, views, quad, in frame.Quad))
                _stats.Ended++;
            else
                _stats.EndFailed++;
        }

        private bool CopyToSwapchain(XrSwapchainInfo swapchain, IntPtr source, string label, bool dump)
        {
            if (swapchain == null || source == IntPtr.Zero || !_xr.AcquireWaitImage(swapchain, out int index))
            {
                _stats.CopyFailed++;
                return false;
            }
            IntPtr image = swapchain.Images[index];
            D3D11.CopyResource(_context, image, source);

            if (label != null && !_loggedDescs.Contains(label))
            {
                _loggedDescs.Add(label);
                Plugin.VrLog.Info($"[{label}] source texture: {Describe(D3D11.GetDesc(source))}");
                Plugin.VrLog.Info($"[{label}] swapchain image: {Describe(D3D11.GetDesc(image))}");
            }
            if (dump && label != null)
            {
                Dump(source, $"{label}-source");
                Dump(image, $"{label}-swapchain");
            }
            return _xr.ReleaseImage(swapchain);
        }

        private static string Describe(D3D11Texture2DDesc d) =>
            $"{d.Width}x{d.Height} format {d.Format} mips {d.MipLevels} array {d.ArraySize} samples {d.SampleCount} usage {d.Usage} bind 0x{d.BindFlags:X} misc 0x{d.MiscFlags:X}";

        private void Dump(IntPtr texture, string name)
        {
            string path = Path.Combine(Plugin.PluginDir, $"dump-{name}.png");
            TextureReadback.TryDump(_device, _context, texture, path, 1024, out string summary);
            Plugin.VrLog.Info($"Dump {name}: {summary} -> {path}");
        }

        /// <summary>Saves the next frame's left eye texture and swapchain image (and the UI) as PNGs next to the plugin.</summary>
        public void RequestDump() => _dumpRequested = true;

        private void LogStats()
        {
            float now = Time.realtimeSinceStartup;
            if (_nextStatsTime == 0f)
                _nextStatsTime = now + StatsInterval;
            if (now < _nextStatsTime)
                return;
            float seconds = StatsInterval + (now - _nextStatsTime);
            _nextStatsTime = now + StatsInterval;
            var s = _stats;
            _stats = default;
            Plugin.Log.LogInfo(
                $"VR {s.Waited / seconds:0.#} fps (game {(Time.frameCount - _statsFrame) / seconds:0.#} fps): stereo {s.Stereo}, ui-only {s.UiOnly}, " +
                $"begun {s.Begun}, ended {s.Ended}, end failed {s.EndFailed}, eye frames {s.EyeFrames}, ui frames {s.QuadFrames}, copy failed {s.CopyFailed}, state {_xr.SessionState}");
            _statsFrame = Time.frameCount;
        }

        // ------------------------------------------------------------------ teardown

        private void Destroy()
        {
            _renderer.Detach();
            _blurOverride.Restore();
            _xr.ReferenceSpaceChangePending -= RequestRecenter;
            _xr.Dispose();
            _ui.Destroy();
            for (int eye = 0; eye < 2; eye++)
            {
                _eyeTextures[eye]?.Destroy();
                _eyeTextures[eye] = null;
            }
            if (_context != IntPtr.Zero)
                D3D11.Release(_context);
            if (_device != IntPtr.Zero)
                D3D11.Release(_device);
            _context = _device = IntPtr.Zero;
            if (_savedWindowMode.HasValue)
            {
                Screen.fullScreenMode = _savedWindowMode.Value;
                _savedWindowMode = null;
            }
            if (_settingsApplied)
            {
                QualitySettings.vSyncCount = _savedVSync;
                Application.runInBackground = _savedRunInBackground;
                _settingsApplied = false;
            }
        }
    }
}
