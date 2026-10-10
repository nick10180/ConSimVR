using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConSimVR.Core.OpenXR
{
    /// <summary>An OpenXR swapchain and its D3D11 images.</summary>
    public sealed class XrSwapchainInfo
    {
        public ulong Handle;
        public long Format;   // DXGI_FORMAT
        public int Width, Height;
        public IntPtr[] Images; // ID3D11Texture2D*, owned by the runtime

        /// <summary>An image was acquired and waited and not yet released. Guarded by the runtime's lock.</summary>
        internal bool ImageAcquired;
    }

    /// <summary>One eye of the projection layer. Pose/fov exactly as returned by <see cref="OpenXrRuntime.LocateViews"/>.</summary>
    public struct ProjectionView
    {
        public XrPosef Pose;
        public XrFovf Fov;
        public ulong Swapchain;
        public int Width, Height;
    }

    /// <summary>A world-locked quad (e.g. a UI panel) in the LOCAL reference space.</summary>
    public struct QuadLayer
    {
        public ulong Swapchain;
        public int Width, Height;
        public XrPosef Pose;              // in the LOCAL reference space
        public float WidthMeters, HeightMeters;
        public bool BlendAlpha;           // XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT
        public bool UnpremultipliedAlpha; // XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT
    }

    /// <summary>
    /// Minimal OpenXR runtime wrapper: one instance, one HMD system, one D3D11 session, LOCAL + VIEW spaces and
    /// a PRIMARY_STEREO frame loop.
    /// <para>Threading: setup, <see cref="PollEvents"/>, <see cref="WaitFrame"/>, <see cref="LocateViews"/>,
    /// <see cref="LocateHead"/> and <see cref="Dispose"/> run on the main thread; <see cref="BeginFrame"/>,
    /// <see cref="AcquireWaitImage"/>, <see cref="ReleaseImage"/> and <see cref="EndFrame"/> on the render thread.
    /// Session transitions and the render-thread calls share one lock; WaitFrame never takes it because it blocks
    /// for vsync.</para>
    /// <para>Every successful <see cref="WaitFrame"/> must be followed by one <see cref="BeginFrame"/> and
    /// <see cref="EndFrame"/> (with zero layers if nothing was rendered): the runtime blocks the next xrWaitFrame
    /// until the previous frame has begun.</para>
    /// <para>Per-frame methods never throw; failures are logged (first time, then every 300th) and return false.</para>
    /// </summary>
    public sealed unsafe class OpenXrRuntime : IDisposable
    {
        private const int LogEvery = 300;
        private const int MaxEventsPerPoll = 64;

        private readonly string _loaderPath;
        private readonly IVrLog _log;
        private readonly object _lock = new object();

        private XrApi _api;
        private ulong _instance;
        private ulong _systemId;
        private ulong _session;
        private ulong _localSpace;
        private ulong _viewSpace;
        private readonly List<XrSwapchainInfo> _swapchains = new List<XrSwapchainInfo>();
        private IReadOnlyList<long> _swapchainFormats = Array.Empty<long>();
        private XrEventDataBuffer* _eventBuffer;

        private volatile XrSessionState _sessionState = XrSessionState.Unknown;
        private volatile bool _sessionRunning;
        private volatile bool _exitRequested;
        private volatile bool _disposed;

        // Frame pairing. _pendingWaits: successful xrWaitFrame calls not yet consumed by BeginFrame (incremented on
        // the main thread, consumed under _lock). _frameBegun: xrBeginFrame succeeded and EndFrame is still owed.
        private int _pendingWaits;
        private bool _frameBegun;

        // Failure counters for rate-limited logging, one per call site.
        private int _failPollEvent, _failWaitFrame, _failBeginFrame, _failEndFrame, _failEndFrameArgs;
        private int _failAcquire, _failWaitImage, _failRelease, _failImageState, _failLocateViews, _failLocateHead;

        public OpenXrRuntime(string loaderPath, IVrLog log)
        {
            _loaderPath = loaderPath;
            _log = log ?? new ConsoleVrLog();
        }

        // ------------------------------------------------------------------ properties

        public string RuntimeName { get; private set; } = string.Empty;
        public string SystemName { get; private set; } = string.Empty;
        /// <summary>Adapter LUID required by the runtime (LowPart | HighPart &lt;&lt; 32).</summary>
        public ulong AdapterLuid { get; private set; }
        /// <summary>Minimum D3D_FEATURE_LEVEL required by the runtime (e.g. 0xb000).</summary>
        public int MinFeatureLevel { get; private set; }
        /// <summary>Recommended per-eye image width (the larger of the two eyes).</summary>
        public int RecommendedWidth { get; private set; }
        public int RecommendedHeight { get; private set; }
        /// <summary>DXGI formats supported for swapchains, in runtime preference order. Valid after CreateSession.</summary>
        public IReadOnlyList<long> SwapchainFormats => _swapchainFormats;

        public XrSessionState SessionState => _sessionState;
        public bool IsSessionRunning => _sessionRunning;
        public bool ExitRequested => _exitRequested;

        /// <summary>Raised from <see cref="PollEvents"/> (main thread) on XR_TYPE_EVENT_DATA_REFERENCE_SPACE_CHANGE_PENDING.</summary>
        public event Action ReferenceSpaceChangePending;

        // ------------------------------------------------------------------ setup (main thread)

        public bool CreateInstance(string applicationName, out string error)
        {
            if (_disposed) { error = "OpenXrRuntime is disposed."; return false; }
            if (_instance != XrConstants.NullHandle) { error = "OpenXR instance already created."; return false; }

            if (_api == null)
            {
                if (!XrApi.TryLoad(_loaderPath, out XrApi api, out error))
                {
                    _log.Error(error);
                    return false;
                }
                _api = api;
                _log.Info($"OpenXR loader: {_api.LoaderPath}");
            }

            // The extension must be offered by the active runtime before we ask for it.
            XrResult r = _api.EnumerateInstanceExtensions(out List<string> extensions);
            if (r.IsFailure())
            {
                string hint = r == XrResult.ErrorRuntimeUnavailable
                    ? " No active OpenXR runtime is configured or it failed to load (e.g. set SteamVR as the OpenXR runtime)."
                    : string.Empty;
                return Fail(out error, $"xrEnumerateInstanceExtensionProperties failed: {ResultToString(r)}.{hint}");
            }
            _log.Info($"OpenXR runtime extensions ({extensions.Count}): {string.Join(" ", extensions)}");
            if (!extensions.Contains(XrConstants.KhrD3D11EnableExtensionName))
                return Fail(out error, $"The active OpenXR runtime does not support {XrConstants.KhrD3D11EnableExtensionName} (Direct3D 11), which is required.");

            var ci = new XrInstanceCreateInfo { Type = XrStructureType.InstanceCreateInfo };
            string appName = string.IsNullOrWhiteSpace(applicationName) ? "ConSimVR" : applicationName;
            XrStrings.ToUtf8(appName, ci.ApplicationInfo.ApplicationName, XrConstants.MaxApplicationNameSize);
            ci.ApplicationInfo.ApplicationVersion = 1;
            XrStrings.ToUtf8("ConSimVR", ci.ApplicationInfo.EngineName, XrConstants.MaxEngineNameSize);
            ci.ApplicationInfo.EngineVersion = 1;
            ci.ApplicationInfo.ApiVersion = XrConstants.MakeVersion(1, 0, 0);

            byte* extName = stackalloc byte[64];
            XrStrings.ToUtf8(XrConstants.KhrD3D11EnableExtensionName, extName, 64);
            byte** extNames = stackalloc byte*[1];
            extNames[0] = extName;
            ci.EnabledExtensionCount = 1;
            ci.EnabledExtensionNames = extNames;

            ulong instance = XrConstants.NullHandle;
            r = _api.xrCreateInstance(&ci, &instance);
            if (r.IsFailure() || instance == XrConstants.NullHandle)
                return Fail(out error, $"xrCreateInstance failed: {ResultToString(r)}");
            _instance = instance;

            if (!_api.LoadInstanceFunctions(instance, out error) || !InitializeSystem(out error))
            {
                _log.Error(error);
                DestroyInstanceAfterFailure();
                return false;
            }

            if (_eventBuffer == null)
                _eventBuffer = (XrEventDataBuffer*)Marshal.AllocHGlobal(sizeof(XrEventDataBuffer));

            error = null;
            return true;
        }

        private bool InitializeSystem(out string error)
        {
            var ip = new XrInstanceProperties { Type = XrStructureType.InstanceProperties };
            XrResult r = _api.xrGetInstanceProperties(_instance, &ip);
            if (r.IsSuccess())
            {
                RuntimeName = XrStrings.FromUtf8(ip.RuntimeName, XrConstants.MaxRuntimeNameSize);
                _log.Info($"OpenXR runtime: {RuntimeName} {XrConstants.VersionToString(ip.RuntimeVersion)}");
            }
            else
            {
                RuntimeName = "(unknown)";
                _log.Warn($"xrGetInstanceProperties failed: {ResultToString(r)}");
            }

            var gi = new XrSystemGetInfo { Type = XrStructureType.SystemGetInfo, FormFactor = XrFormFactor.HeadMountedDisplay };
            ulong systemId = 0;
            r = _api.xrGetSystem(_instance, &gi, &systemId);
            if (r.IsFailure() || systemId == 0)
            {
                string hint = r == XrResult.ErrorFormFactorUnavailable
                    ? " No headset is available right now; connect/power on the HMD and check the runtime sees it."
                    : string.Empty;
                error = $"xrGetSystem(HEAD_MOUNTED_DISPLAY) failed: {ResultToString(r)}.{hint}";
                return false;
            }
            _systemId = systemId;

            var sp = new XrSystemProperties { Type = XrStructureType.SystemProperties };
            r = _api.xrGetSystemProperties(_instance, _systemId, &sp);
            if (r.IsSuccess())
            {
                SystemName = XrStrings.FromUtf8(sp.SystemName, XrConstants.MaxSystemNameSize);
                _log.Info($"OpenXR system: {SystemName} (vendor 0x{sp.VendorId:X4}), max swapchain " +
                          $"{sp.GraphicsProperties.MaxSwapchainImageWidth}x{sp.GraphicsProperties.MaxSwapchainImageHeight}, " +
                          $"max layers {sp.GraphicsProperties.MaxLayerCount}, tracking orientation={sp.TrackingProperties.OrientationTracking != 0} " +
                          $"position={sp.TrackingProperties.PositionTracking != 0}");
            }
            else
            {
                SystemName = "(unknown)";
                _log.Warn($"xrGetSystemProperties failed: {ResultToString(r)}");
            }

            // Mandatory before xrCreateSession (otherwise XR_ERROR_GRAPHICS_REQUIREMENTS_CALL_MISSING).
            var req = new XrGraphicsRequirementsD3D11KHR { Type = XrStructureType.GraphicsRequirementsD3D11KHR };
            r = _api.xrGetD3D11GraphicsRequirementsKHR(_instance, _systemId, &req);
            if (r.IsFailure())
            {
                error = $"xrGetD3D11GraphicsRequirementsKHR failed: {ResultToString(r)}";
                return false;
            }
            AdapterLuid = req.AdapterLuid.ToUInt64();
            MinFeatureLevel = req.MinFeatureLevel;
            _log.Info($"OpenXR D3D11 requirements: adapter LUID 0x{AdapterLuid:X16}, min feature level 0x{MinFeatureLevel:X}");

            uint count = 0;
            r = _api.xrEnumerateViewConfigurationViews(_instance, _systemId, XrViewConfigurationType.PrimaryStereo, 0, &count, null);
            if (r.IsFailure() || count < 2)
            {
                error = r.IsFailure()
                    ? $"xrEnumerateViewConfigurationViews(PRIMARY_STEREO) failed: {ResultToString(r)}"
                    : $"PRIMARY_STEREO reports {count} views; expected 2.";
                return false;
            }
            var views = new XrViewConfigurationView[count];
            for (int i = 0; i < views.Length; i++) views[i].Type = XrStructureType.ViewConfigurationView;
            fixed (XrViewConfigurationView* pv = views)
                r = _api.xrEnumerateViewConfigurationViews(_instance, _systemId, XrViewConfigurationType.PrimaryStereo, count, &count, pv);
            if (r.IsFailure() || count < 2)
            {
                error = $"xrEnumerateViewConfigurationViews(PRIMARY_STEREO) failed: {ResultToString(r)} (count {count})";
                return false;
            }
            RecommendedWidth = (int)System.Math.Max(views[0].RecommendedImageRectWidth, views[1].RecommendedImageRectWidth);
            RecommendedHeight = (int)System.Math.Max(views[0].RecommendedImageRectHeight, views[1].RecommendedImageRectHeight);
            _log.Info($"OpenXR views: left {views[0].RecommendedImageRectWidth}x{views[0].RecommendedImageRectHeight}, " +
                      $"right {views[1].RecommendedImageRectWidth}x{views[1].RecommendedImageRectHeight} " +
                      $"(max {views[0].MaxImageRectWidth}x{views[0].MaxImageRectHeight}, samples {views[0].RecommendedSwapchainSampleCount})");

            error = null;
            return true;
        }

        private void DestroyInstanceAfterFailure()
        {
            if (_instance != XrConstants.NullHandle && _api.xrDestroyInstance != null)
            {
                XrResult r = _api.xrDestroyInstance(_instance);
                if (r.IsFailure()) _log.Warn($"xrDestroyInstance failed: {ResultToString(r)}");
            }
            _instance = XrConstants.NullHandle;
            _systemId = 0;
            _api.ClearInstance();
        }

        public bool CreateSession(IntPtr d3d11Device, out string error)
        {
            if (_disposed) { error = "OpenXrRuntime is disposed."; return false; }
            if (_instance == XrConstants.NullHandle || _systemId == 0) { error = "CreateInstance must succeed before CreateSession."; return false; }
            if (_session != XrConstants.NullHandle) { error = "OpenXR session already created."; return false; }
            if (d3d11Device == IntPtr.Zero) { error = "D3D11 device is null."; return false; }

            var binding = new XrGraphicsBindingD3D11KHR { Type = XrStructureType.GraphicsBindingD3D11KHR, Device = d3d11Device };
            var sci = new XrSessionCreateInfo { Type = XrStructureType.SessionCreateInfo, Next = &binding, SystemId = _systemId };
            ulong session = XrConstants.NullHandle;
            XrResult r = _api.xrCreateSession(_instance, &sci, &session);
            if (r.IsFailure() || session == XrConstants.NullHandle)
            {
                string hint = r == XrResult.ErrorGraphicsDeviceInvalid
                    ? $" The D3D11 device must be on the adapter with LUID 0x{AdapterLuid:X16} and support feature level 0x{MinFeatureLevel:X}."
                    : string.Empty;
                return Fail(out error, $"xrCreateSession failed: {ResultToString(r)}.{hint}");
            }

            ulong local = XrConstants.NullHandle, view = XrConstants.NullHandle;
            if (!CreateReferenceSpace(session, XrReferenceSpaceType.Local, out local, out error) ||
                !CreateReferenceSpace(session, XrReferenceSpaceType.View, out view, out error))
            {
                _log.Error(error);
                if (local != XrConstants.NullHandle) _api.xrDestroySpace(local);
                _api.xrDestroySession(session);
                return false;
            }

            uint count = 0;
            r = _api.xrEnumerateSwapchainFormats(session, 0, &count, null);
            long[] formats = Array.Empty<long>();
            if (r.IsSuccess() && count > 0)
            {
                formats = new long[count];
                fixed (long* pf = formats)
                    r = _api.xrEnumerateSwapchainFormats(session, count, &count, pf);
                if (r.IsSuccess() && count < formats.Length) Array.Resize(ref formats, (int)count);
            }
            if (r.IsFailure())
            {
                _api.xrDestroySpace(view);
                _api.xrDestroySpace(local);
                _api.xrDestroySession(session);
                return Fail(out error, $"xrEnumerateSwapchainFormats failed: {ResultToString(r)}");
            }

            lock (_lock)
            {
                _session = session;
                _localSpace = local;
                _viewSpace = view;
                _swapchainFormats = Array.AsReadOnly(formats);
                _sessionState = XrSessionState.Unknown;
                _sessionRunning = false;
                _pendingWaits = 0;
                _frameBegun = false;
            }
            _log.Info($"OpenXR session created; swapchain formats (DXGI): {string.Join(", ", formats)}");
            error = null;
            return true;
        }

        private bool CreateReferenceSpace(ulong session, XrReferenceSpaceType type, out ulong space, out string error)
        {
            var info = new XrReferenceSpaceCreateInfo
            {
                Type = XrStructureType.ReferenceSpaceCreateInfo,
                ReferenceSpaceType = type,
                PoseInReferenceSpace = XrPosef.Identity,
            };
            ulong s = XrConstants.NullHandle;
            XrResult r = _api.xrCreateReferenceSpace(session, &info, &s);
            space = r.IsSuccess() ? s : XrConstants.NullHandle;
            error = r.IsSuccess() ? null : $"xrCreateReferenceSpace({type}) failed: {ResultToString(r)}";
            return r.IsSuccess() && s != XrConstants.NullHandle;
        }

        public XrSwapchainInfo CreateSwapchain(long format, int width, int height, out string error)
        {
            if (_disposed) { error = "OpenXrRuntime is disposed."; return null; }
            if (_session == XrConstants.NullHandle) { error = "CreateSession must succeed before CreateSwapchain."; return null; }
            if (width <= 0 || height <= 0) { error = $"Invalid swapchain size {width}x{height}."; return null; }

            var ci = new XrSwapchainCreateInfo
            {
                Type = XrStructureType.SwapchainCreateInfo,
                UsageFlags = XrSwapchainUsageFlags.ColorAttachment | XrSwapchainUsageFlags.TransferDst | XrSwapchainUsageFlags.Sampled,
                Format = format,
                SampleCount = 1,
                Width = (uint)width,
                Height = (uint)height,
                FaceCount = 1,
                ArraySize = 1,
                MipCount = 1,
            };
            ulong handle = XrConstants.NullHandle;
            XrResult r = _api.xrCreateSwapchain(_session, &ci, &handle);
            if (r.IsFailure() || handle == XrConstants.NullHandle)
            {
                Fail(out error, $"xrCreateSwapchain(format {format}, {width}x{height}) failed: {ResultToString(r)}");
                return null;
            }

            uint count = 0;
            r = _api.xrEnumerateSwapchainImages(handle, 0, &count, null);
            IntPtr[] textures = null;
            if (r.IsSuccess() && count > 0)
            {
                var images = new XrSwapchainImageD3D11KHR[count];
                for (int i = 0; i < images.Length; i++) images[i].Type = XrStructureType.SwapchainImageD3D11KHR;
                fixed (XrSwapchainImageD3D11KHR* pi = images)
                    r = _api.xrEnumerateSwapchainImages(handle, count, &count, (XrSwapchainImageBaseHeader*)pi);
                if (r.IsSuccess())
                {
                    textures = new IntPtr[System.Math.Min(count, (uint)images.Length)];
                    for (int i = 0; i < textures.Length; i++) textures[i] = images[i].Texture;
                }
            }
            if (r.IsFailure() || textures == null || textures.Length == 0)
            {
                _api.xrDestroySwapchain(handle);
                Fail(out error, $"xrEnumerateSwapchainImages failed: {ResultToString(r)} (count {count})");
                return null;
            }

            var info = new XrSwapchainInfo { Handle = handle, Format = format, Width = width, Height = height, Images = textures };
            lock (_lock) _swapchains.Add(info);
            _log.Info($"OpenXR swapchain 0x{handle:X}: format {format}, {width}x{height}, {textures.Length} images");
            error = null;
            return info;
        }

        /// <summary>
        /// Destroys one swapchain (e.g. to resize it). The caller must make sure the render thread no longer uses it.
        /// </summary>
        public void DestroySwapchain(XrSwapchainInfo swapchain)
        {
            if (swapchain == null)
                return;
            lock (_lock)
            {
                if (!_swapchains.Remove(swapchain) || _disposed || _api == null)
                    return;
                XrResult r = _api.xrDestroySwapchain(swapchain.Handle);
                if (r.IsFailure())
                    _log.Warn($"xrDestroySwapchain failed: {ResultToString(r)}");
            }
        }

        // ------------------------------------------------------------------ per frame, main thread

        public void PollEvents()
        {
            if (_disposed || _api == null || _instance == XrConstants.NullHandle || _eventBuffer == null) return;

            for (int i = 0; i < MaxEventsPerPoll; i++)
            {
                XrEventDataBuffer* ev = _eventBuffer;
                ev->Type = XrStructureType.EventDataBuffer;
                ev->Next = null;
                XrResult r = _api.xrPollEvent(_instance, ev);
                if (r == XrResult.EventUnavailable) return;
                if (r.IsFailure())
                {
                    LogFailure("xrPollEvent", r, ref _failPollEvent);
                    return;
                }

                switch (ev->Type)
                {
                    case XrStructureType.EventDataSessionStateChanged:
                        OnSessionStateChanged((XrEventDataSessionStateChanged*)ev);
                        break;

                    case XrStructureType.EventDataInstanceLossPending:
                        _log.Warn("OpenXR instance loss pending; requesting exit.");
                        _exitRequested = true;
                        break;

                    case XrStructureType.EventDataReferenceSpaceChangePending:
                    {
                        var e = (XrEventDataReferenceSpaceChangePending*)ev;
                        _log.Info($"OpenXR reference space change pending ({e->ReferenceSpaceType}).");
                        Action handler = ReferenceSpaceChangePending;
                        if (handler != null)
                        {
                            try { handler(); }
                            catch (Exception ex) { _log.Error("ReferenceSpaceChangePending handler threw: " + ex); }
                        }
                        break;
                    }

                    case XrStructureType.EventDataEventsLost:
                        _log.Warn($"OpenXR lost {((XrEventDataEventsLost*)ev)->LostEventCount} events.");
                        break;
                }

                if (_disposed) return; // a handler disposed us
            }
        }

        private void OnSessionStateChanged(XrEventDataSessionStateChanged* e)
        {
            XrSessionState state = e->State;
            _sessionState = state;
            _log.Info($"OpenXR session state: {state}");

            switch (state)
            {
                case XrSessionState.Ready:
                    lock (_lock) BeginSessionLocked();
                    break;
                case XrSessionState.Stopping:
                    lock (_lock) EndSessionLocked();
                    break;
                case XrSessionState.Exiting:
                case XrSessionState.LossPending:
                    _exitRequested = true;
                    break;
            }
        }

        private void BeginSessionLocked()
        {
            if (_disposed || _session == XrConstants.NullHandle || _sessionRunning) return;
            var bi = new XrSessionBeginInfo
            {
                Type = XrStructureType.SessionBeginInfo,
                PrimaryViewConfigurationType = XrViewConfigurationType.PrimaryStereo,
            };
            XrResult r = _api.xrBeginSession(_session, &bi);
            if (r.IsFailure())
            {
                _log.Error($"xrBeginSession failed: {ResultToString(r)}");
                return;
            }
            _pendingWaits = 0;
            _frameBegun = false;
            _sessionRunning = true;
            _log.Info("OpenXR session running.");
        }

        private void EndSessionLocked()
        {
            if (_disposed || _session == XrConstants.NullHandle) return;
            _sessionRunning = false;
            _pendingWaits = 0;
            _frameBegun = false;
            XrResult r = _api.xrEndSession(_session);
            if (r.IsFailure()) _log.Error($"xrEndSession failed: {ResultToString(r)}");
            else _log.Info("OpenXR session ended.");
        }

        /// <summary>xrWaitFrame. Blocks until the runtime wants the next frame; holds no lock while blocking.</summary>
        public bool WaitFrame(out XrFrameState frameState)
        {
            frameState = default;
            frameState.Type = XrStructureType.FrameState;
            if (_disposed || !_sessionRunning || _api == null) return false;

            var wi = new XrFrameWaitInfo { Type = XrStructureType.FrameWaitInfo };
            var fs = new XrFrameState { Type = XrStructureType.FrameState };
            XrResult r = _api.xrWaitFrame(_session, &wi, &fs);
            if (r.IsFailure())
            {
                LogFailure("xrWaitFrame", r, ref _failWaitFrame);
                return false;
            }

            frameState = fs;
            Interlocked.Increment(ref _pendingWaits);
            return true;
        }

        /// <summary>Locates the PRIMARY_STEREO views in LOCAL space. <paramref name="views"/> needs at least 2 elements.</summary>
        public bool LocateViews(long displayTime, Span<XrView> views, out ulong viewStateFlags)
        {
            viewStateFlags = 0;
            if (_disposed || _api == null || _session == XrConstants.NullHandle || views.Length < 2 || displayTime <= 0) return false;

            var li = new XrViewLocateInfo
            {
                Type = XrStructureType.ViewLocateInfo,
                ViewConfigurationType = XrViewConfigurationType.PrimaryStereo,
                DisplayTime = displayTime,
                Space = _localSpace,
            };
            var vs = new XrViewState { Type = XrStructureType.ViewState };
            for (int i = 0; i < views.Length; i++)
            {
                views[i] = default;
                views[i].Type = XrStructureType.View;
            }

            uint count = 0;
            XrResult r;
            fixed (XrView* pv = views)
                r = _api.xrLocateViews(_session, &li, &vs, (uint)views.Length, &count, pv);
            if (r.IsFailure())
            {
                LogFailure("xrLocateViews", r, ref _failLocateViews);
                return false;
            }

            viewStateFlags = (ulong)vs.ViewStateFlags;
            return count >= 2;
        }

        /// <summary>
        /// Locates the VIEW space (head) in LOCAL space. Returns false unless the orientation is valid; the position
        /// is zeroed when the runtime reports it invalid.
        /// </summary>
        public bool LocateHead(long displayTime, out XrPosef pose)
        {
            pose = XrPosef.Identity;
            if (_disposed || _api == null || _viewSpace == XrConstants.NullHandle || displayTime <= 0) return false;

            var loc = new XrSpaceLocation { Type = XrStructureType.SpaceLocation };
            XrResult r = _api.xrLocateSpace(_viewSpace, _localSpace, displayTime, &loc);
            if (r.IsFailure())
            {
                LogFailure("xrLocateSpace(VIEW in LOCAL)", r, ref _failLocateHead);
                return false;
            }
            if ((loc.LocationFlags & XrSpaceLocationFlags.OrientationValid) == 0) return false;

            pose = loc.Pose;
            if ((loc.LocationFlags & XrSpaceLocationFlags.PositionValid) == 0) pose.Position = default;
            return true;
        }

        // ------------------------------------------------------------------ render thread

        /// <summary>xrBeginFrame for the oldest unconsumed WaitFrame. XR_FRAME_DISCARDED counts as success.</summary>
        public bool BeginFrame()
        {
            lock (_lock)
            {
                if (_disposed || _api == null || _session == XrConstants.NullHandle) return false;
                if (_pendingWaits <= 0) return false; // no successful WaitFrame to pair with
                Interlocked.Decrement(ref _pendingWaits);
                if (!_sessionRunning) return false;

                var bi = new XrFrameBeginInfo { Type = XrStructureType.FrameBeginInfo };
                XrResult r = _api.xrBeginFrame(_session, &bi);
                if (r.IsFailure())
                {
                    LogFailure("xrBeginFrame", r, ref _failBeginFrame);
                    return false;
                }

                _frameBegun = true;
                return true;
            }
        }

        /// <summary>xrAcquireSwapchainImage + xrWaitSwapchainImage(XR_INFINITE_DURATION).</summary>
        public bool AcquireWaitImage(XrSwapchainInfo swapchain, out int imageIndex)
        {
            imageIndex = -1;
            if (swapchain == null) return false;

            lock (_lock)
            {
                if (_disposed || _api == null || swapchain.Handle == XrConstants.NullHandle) return false;
                if (swapchain.ImageAcquired)
                {
                    if (ShouldLog(ref _failImageState, out int n)) _log.Warn($"AcquireWaitImage: swapchain 0x{swapchain.Handle:X} still has an unreleased image (x{n}).");
                    return false;
                }

                var ai = new XrSwapchainImageAcquireInfo { Type = XrStructureType.SwapchainImageAcquireInfo };
                uint index = 0;
                XrResult r = _api.xrAcquireSwapchainImage(swapchain.Handle, &ai, &index);
                if (r.IsFailure())
                {
                    LogFailure("xrAcquireSwapchainImage", r, ref _failAcquire);
                    return false;
                }

                var wi = new XrSwapchainImageWaitInfo { Type = XrStructureType.SwapchainImageWaitInfo, Timeout = XrConstants.InfiniteDuration };
                r = _api.xrWaitSwapchainImage(swapchain.Handle, &wi);
                if (r != XrResult.Success) // XR_TIMEOUT_EXPIRED is a success code but means the image is not ready
                {
                    LogFailure("xrWaitSwapchainImage", r, ref _failWaitImage);
                    return false;
                }

                swapchain.ImageAcquired = true;
                imageIndex = (int)index;
                return true;
            }
        }

        public bool ReleaseImage(XrSwapchainInfo swapchain)
        {
            if (swapchain == null) return false;

            lock (_lock)
            {
                if (_disposed || _api == null || swapchain.Handle == XrConstants.NullHandle) return false;
                if (!swapchain.ImageAcquired)
                {
                    if (ShouldLog(ref _failImageState, out int n)) _log.Warn($"ReleaseImage: swapchain 0x{swapchain.Handle:X} has no acquired image (x{n}).");
                    return false;
                }
                swapchain.ImageAcquired = false;

                var ri = new XrSwapchainImageReleaseInfo { Type = XrStructureType.SwapchainImageReleaseInfo };
                XrResult r = _api.xrReleaseSwapchainImage(swapchain.Handle, &ri);
                if (r.IsFailure())
                {
                    LogFailure("xrReleaseSwapchainImage", r, ref _failRelease);
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// xrEndFrame with an optional stereo projection layer (2 views) and an optional quad, both in LOCAL space,
        /// OPAQUE blend mode. Zero layers is valid (and required to keep the frame loop paired when nothing rendered).
        /// </summary>
        public bool EndFrame(long displayTime, ReadOnlySpan<ProjectionView> projectionViews, bool hasQuad, in QuadLayer quad)
        {
            lock (_lock)
            {
                if (!_frameBegun) return false;
                _frameBegun = false;
                if (_disposed || _api == null || _session == XrConstants.NullHandle || !_sessionRunning) return false;

                XrCompositionLayerProjectionView* views = stackalloc XrCompositionLayerProjectionView[2];
                XrCompositionLayerBaseHeader** layers = stackalloc XrCompositionLayerBaseHeader*[2];
                XrCompositionLayerProjection projection = default;
                XrCompositionLayerQuad quadLayer = default;
                uint layerCount = 0;

                if (projectionViews.Length == 2)
                {
                    bool valid = true;
                    for (int i = 0; i < 2; i++)
                    {
                        ref readonly ProjectionView v = ref projectionViews[i];
                        if (v.Swapchain == XrConstants.NullHandle || v.Width <= 0 || v.Height <= 0) valid = false;
                        views[i] = default;
                        views[i].Type = XrStructureType.CompositionLayerProjectionView;
                        views[i].Pose = v.Pose;
                        views[i].Fov = v.Fov;
                        views[i].SubImage.Swapchain = v.Swapchain;
                        views[i].SubImage.ImageRect.Extent.Width = v.Width;
                        views[i].SubImage.ImageRect.Extent.Height = v.Height;
                    }

                    if (valid)
                    {
                        projection.Type = XrStructureType.CompositionLayerProjection;
                        projection.Space = _localSpace;
                        projection.ViewCount = 2;
                        projection.Views = views;
                        layers[layerCount++] = (XrCompositionLayerBaseHeader*)&projection;
                    }
                    else
                    {
                        if (ShouldLog(ref _failEndFrameArgs, out int n)) _log.Warn($"EndFrame: projection view without swapchain or size; projection layer skipped (x{n}).");
                    }
                }
                else if (projectionViews.Length != 0)
                {
                    if (ShouldLog(ref _failEndFrameArgs, out int n)) _log.Warn($"EndFrame: {projectionViews.Length} projection views (expected 0 or 2); projection layer skipped (x{n}).");
                }

                if (hasQuad)
                {
                    if (quad.Swapchain != XrConstants.NullHandle && quad.Width > 0 && quad.Height > 0 &&
                        quad.WidthMeters > 0f && quad.HeightMeters > 0f)
                    {
                        quadLayer.Type = XrStructureType.CompositionLayerQuad;
                        quadLayer.LayerFlags =
                            (quad.BlendAlpha ? XrCompositionLayerFlags.BlendTextureSourceAlpha : XrCompositionLayerFlags.None) |
                            (quad.UnpremultipliedAlpha ? XrCompositionLayerFlags.UnpremultipliedAlpha : XrCompositionLayerFlags.None);
                        quadLayer.Space = _localSpace;
                        quadLayer.EyeVisibility = XrEyeVisibility.Both;
                        quadLayer.SubImage.Swapchain = quad.Swapchain;
                        quadLayer.SubImage.ImageRect.Extent.Width = quad.Width;
                        quadLayer.SubImage.ImageRect.Extent.Height = quad.Height;
                        quadLayer.Pose = quad.Pose;
                        quadLayer.Size.Width = quad.WidthMeters;
                        quadLayer.Size.Height = quad.HeightMeters;
                        layers[layerCount++] = (XrCompositionLayerBaseHeader*)&quadLayer;
                    }
                    else
                    {
                        if (ShouldLog(ref _failEndFrameArgs, out int n)) _log.Warn($"EndFrame: quad without swapchain or size; quad layer skipped (x{n}).");
                    }
                }

                var ei = new XrFrameEndInfo
                {
                    Type = XrStructureType.FrameEndInfo,
                    DisplayTime = displayTime,
                    EnvironmentBlendMode = XrEnvironmentBlendMode.Opaque,
                    LayerCount = layerCount,
                    Layers = layerCount > 0 ? layers : null,
                };
                XrResult r = _api.xrEndFrame(_session, &ei);
                if (r.IsFailure())
                {
                    LogFailure("xrEndFrame", r, ref _failEndFrame);
                    return false;
                }
                return true;
            }
        }

        // ------------------------------------------------------------------ teardown

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _sessionRunning = false;
                _pendingWaits = 0;
                _frameBegun = false;

                if (_api != null)
                {
                    foreach (XrSwapchainInfo sc in _swapchains)
                    {
                        if (sc.Handle != XrConstants.NullHandle)
                        {
                            XrResult r = _api.xrDestroySwapchain(sc.Handle);
                            if (r.IsFailure()) _log.Warn($"xrDestroySwapchain failed: {ResultToString(r)}");
                        }
                        sc.Handle = XrConstants.NullHandle;
                        sc.ImageAcquired = false;
                    }
                    _swapchains.Clear();

                    DestroySpace(ref _viewSpace);
                    DestroySpace(ref _localSpace);

                    if (_session != XrConstants.NullHandle)
                    {
                        XrResult r = _api.xrDestroySession(_session);
                        if (r.IsFailure()) _log.Warn($"xrDestroySession failed: {ResultToString(r)}");
                        _session = XrConstants.NullHandle;
                    }

                    if (_instance != XrConstants.NullHandle)
                    {
                        if (_api.xrDestroyInstance != null)
                        {
                            XrResult r = _api.xrDestroyInstance(_instance);
                            if (r.IsFailure()) _log.Warn($"xrDestroyInstance failed: {ResultToString(r)}");
                        }
                        _instance = XrConstants.NullHandle;
                        _api.ClearInstance();
                    }
                }

                if (_eventBuffer != null)
                {
                    Marshal.FreeHGlobal((IntPtr)_eventBuffer);
                    _eventBuffer = null;
                }
            }
        }

        private void DestroySpace(ref ulong space)
        {
            if (space == XrConstants.NullHandle) return;
            XrResult r = _api.xrDestroySpace(space);
            if (r.IsFailure()) _log.Warn($"xrDestroySpace failed: {ResultToString(r)}");
            space = XrConstants.NullHandle;
        }

        // ------------------------------------------------------------------ helpers

        private string ResultToString(XrResult result) => _api != null ? _api.ResultToString(result) : XrApi.ResultName(result);

        private bool Fail(out string error, string message)
        {
            error = message;
            _log.Error(message);
            return false;
        }

        /// <summary>Logs a failing call the first time and every 300th time after that.</summary>
        private void LogFailure(string function, XrResult result, ref int counter)
        {
            if (result == XrResult.ErrorInstanceLost || result == XrResult.ErrorSessionLost)
                _exitRequested = true;

            if (ShouldLog(ref counter, out int n))
                _log.Error(n == 1 ? $"{function} failed: {ResultToString(result)}" : $"{function} failed: {ResultToString(result)} ({n} failures so far)");
        }

        /// <summary>Counts an occurrence; true the first time and every 300th time. Check before formatting a message.</summary>
        private static bool ShouldLog(ref int counter, out int count)
        {
            count = Interlocked.Increment(ref counter);
            return count == 1 || count % LogEvery == 0;
        }
    }
}
