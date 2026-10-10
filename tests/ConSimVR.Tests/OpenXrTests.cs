using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ConSimVR.Core;
using ConSimVR.Core.OpenXR;
using Xunit;

namespace ConSimVR.Tests
{
    /// <summary>
    /// Checks the hand-written OpenXR bindings against the official headers.
    /// The expected sizes/offsets/enum values were produced by clang 17 (target x86_64-pc-windows-msvc) from
    /// vendor/openxr-loader/include/openxr/openxr.h + openxr_platform.h, OpenXR 1.1.62, with
    /// XR_USE_GRAPHICS_API_D3D11 defined. The Windows SDK was not installed, so LUID (DWORD LowPart; LONG HighPart),
    /// D3D_FEATURE_LEVEL (enum) and the opaque ID3D11Device/ID3D11Texture2D were declared exactly as the SDK does.
    /// </summary>
    public class OpenXrTests
    {
        // ------------------------------------------------------------------ struct layouts

        private static unsafe void Layout<T>(int size, params (string Field, int Offset)[] fields) where T : unmanaged
        {
            string name = typeof(T).Name;
            Assert.True(Marshal.SizeOf<T>() == size, $"Marshal.SizeOf<{name}>() = {Marshal.SizeOf<T>()}, expected {size}");
            // The runtime receives pointers to the managed structs, so the managed size must match too.
            Assert.True(sizeof(T) == size, $"sizeof({name}) = {sizeof(T)}, expected {size}");
            foreach ((string field, int offset) in fields)
            {
                int actual = (int)Marshal.OffsetOf<T>(field);
                Assert.True(actual == offset, $"{name}.{field} at {actual}, expected {offset}");
            }
        }

        [Fact]
        public void Layout_math_types()
        {
            Layout<XrPosef>(28, ("Orientation", 0), ("Position", 16));
            Layout<XrFovf>(16, ("AngleLeft", 0), ("AngleDown", 12));
            Layout<XrRect2Di>(16, ("Offset", 0), ("Extent", 8));
            Layout<XrLuid>(8, ("LowPart", 0), ("HighPart", 4));
        }

        [Fact]
        public void Layout_XrApplicationInfo() =>
            Layout<XrApplicationInfo>(272, ("ApplicationName", 0), ("ApplicationVersion", 128), ("EngineName", 132),
                ("EngineVersion", 260), ("ApiVersion", 264));

        [Fact]
        public void Layout_XrInstanceCreateInfo() =>
            Layout<XrInstanceCreateInfo>(328, ("Type", 0), ("Next", 8), ("CreateFlags", 16), ("ApplicationInfo", 24),
                ("EnabledApiLayerCount", 296), ("EnabledApiLayerNames", 304), ("EnabledExtensionCount", 312),
                ("EnabledExtensionNames", 320));

        [Fact]
        public void Layout_instance_and_system_queries()
        {
            Layout<XrInstanceProperties>(152, ("RuntimeVersion", 16), ("RuntimeName", 24));
            Layout<XrExtensionProperties>(152, ("ExtensionName", 16), ("ExtensionVersion", 144));
            Layout<XrSystemGetInfo>(24, ("FormFactor", 16));
            Layout<XrSystemProperties>(304, ("SystemId", 16), ("VendorId", 24), ("SystemName", 28),
                ("GraphicsProperties", 284), ("TrackingProperties", 296));
            Layout<XrViewConfigurationView>(40, ("RecommendedImageRectWidth", 16), ("MaxSwapchainSampleCount", 36));
        }

        [Fact]
        public void Layout_XrGraphicsBindingD3D11KHR() =>
            Layout<XrGraphicsBindingD3D11KHR>(24, ("Next", 8), ("Device", 16));

        [Fact]
        public void Layout_XrGraphicsRequirementsD3D11KHR() =>
            Layout<XrGraphicsRequirementsD3D11KHR>(32, ("AdapterLuid", 16), ("MinFeatureLevel", 24));

        [Fact]
        public void Layout_XrSessionCreateInfo() =>
            Layout<XrSessionCreateInfo>(32, ("Next", 8), ("CreateFlags", 16), ("SystemId", 24));

        [Fact]
        public void Layout_XrSessionBeginInfo() =>
            Layout<XrSessionBeginInfo>(24, ("PrimaryViewConfigurationType", 16));

        [Fact]
        public void Layout_XrReferenceSpaceCreateInfo() =>
            Layout<XrReferenceSpaceCreateInfo>(48, ("ReferenceSpaceType", 16), ("PoseInReferenceSpace", 20));

        [Fact]
        public void Layout_XrSpaceLocation() =>
            Layout<XrSpaceLocation>(56, ("LocationFlags", 16), ("Pose", 24));

        [Fact]
        public void Layout_XrSwapchainCreateInfo() =>
            Layout<XrSwapchainCreateInfo>(64, ("CreateFlags", 16), ("UsageFlags", 24), ("Format", 32), ("SampleCount", 40),
                ("Width", 44), ("Height", 48), ("FaceCount", 52), ("ArraySize", 56), ("MipCount", 60));

        [Fact]
        public void Layout_swapchain_image_structs()
        {
            Layout<XrSwapchainImageD3D11KHR>(24, ("Texture", 16));
            Layout<XrSwapchainImageBaseHeader>(16, ("Next", 8));
            Layout<XrSwapchainImageAcquireInfo>(16, ("Next", 8));
            Layout<XrSwapchainImageWaitInfo>(24, ("Timeout", 16));
            Layout<XrSwapchainImageReleaseInfo>(16, ("Next", 8));
        }

        [Fact]
        public void Layout_XrFrameState()
        {
            Layout<XrFrameState>(40, ("PredictedDisplayTime", 16), ("PredictedDisplayPeriod", 24), ("ShouldRender", 32));
            Layout<XrFrameWaitInfo>(16, ("Next", 8));
            Layout<XrFrameBeginInfo>(16, ("Next", 8));
        }

        [Fact]
        public void Layout_XrViewLocateInfo() =>
            Layout<XrViewLocateInfo>(40, ("ViewConfigurationType", 16), ("DisplayTime", 24), ("Space", 32));

        [Fact]
        public void Layout_XrView()
        {
            Layout<XrView>(64, ("Pose", 16), ("Fov", 44));
            Layout<XrViewState>(24, ("ViewStateFlags", 16));
        }

        [Fact]
        public void Layout_XrSwapchainSubImage() =>
            Layout<XrSwapchainSubImage>(32, ("Swapchain", 0), ("ImageRect", 8), ("ImageArrayIndex", 24));

        [Fact]
        public void Layout_XrCompositionLayerProjectionView() =>
            Layout<XrCompositionLayerProjectionView>(96, ("Pose", 16), ("Fov", 44), ("SubImage", 64));

        [Fact]
        public void Layout_XrCompositionLayerProjection()
        {
            Layout<XrCompositionLayerProjection>(48, ("LayerFlags", 16), ("Space", 24), ("ViewCount", 32), ("Views", 40));
            Layout<XrCompositionLayerBaseHeader>(32, ("LayerFlags", 16), ("Space", 24));
        }

        [Fact]
        public void Layout_XrCompositionLayerQuad() =>
            Layout<XrCompositionLayerQuad>(112, ("LayerFlags", 16), ("Space", 24), ("EyeVisibility", 32), ("SubImage", 40),
                ("Pose", 72), ("Size", 100));

        [Fact]
        public void Layout_XrFrameEndInfo() =>
            Layout<XrFrameEndInfo>(40, ("DisplayTime", 16), ("EnvironmentBlendMode", 24), ("LayerCount", 28), ("Layers", 32));

        [Fact]
        public void Layout_events()
        {
            Layout<XrEventDataBuffer>(4016, ("Varying", 16));
            Layout<XrEventDataSessionStateChanged>(40, ("Session", 16), ("State", 24), ("Time", 32));
            Layout<XrEventDataReferenceSpaceChangePending>(72, ("Session", 16), ("ReferenceSpaceType", 24), ("ChangeTime", 32),
                ("PoseValid", 40), ("PoseInPreviousSpace", 44));
            Layout<XrEventDataInstanceLossPending>(24, ("LossTime", 16));
            Layout<XrEventDataEventsLost>(24, ("LostEventCount", 16));
        }

        // ------------------------------------------------------------------ enum spot checks

        [Fact]
        public void Enum_values_match_header()
        {
            Assert.Equal(48, (int)XrStructureType.CompositionLayerProjectionView);
            Assert.Equal(1000027000, (int)XrStructureType.GraphicsBindingD3D11KHR);
            Assert.Equal(1000027001, (int)XrStructureType.SwapchainImageD3D11KHR);
            Assert.Equal(1000027002, (int)XrStructureType.GraphicsRequirementsD3D11KHR);
            Assert.Equal(44, (int)XrStructureType.FrameState);
            Assert.Equal(18, (int)XrStructureType.EventDataSessionStateChanged);
            Assert.Equal(40, (int)XrStructureType.EventDataReferenceSpaceChangePending);
            Assert.Equal(36, (int)XrStructureType.CompositionLayerQuad);
            Assert.Equal(57, (int)XrStructureType.SwapchainImageReleaseInfo);

            Assert.Equal(5, (int)XrSessionState.Focused);
            Assert.Equal(2, (int)XrSessionState.Ready);
            Assert.Equal(6, (int)XrSessionState.Stopping);
            Assert.Equal(8, (int)XrSessionState.Exiting);

            Assert.Equal(9, (int)XrResult.FrameDiscarded);
            Assert.Equal(4, (int)XrResult.EventUnavailable);
            Assert.Equal(-35, (int)XrResult.ErrorFormFactorUnavailable);
            Assert.Equal(-51, (int)XrResult.ErrorRuntimeUnavailable);
            Assert.Equal(-50, (int)XrResult.ErrorGraphicsRequirementsCallMissing);

            Assert.Equal(2, (int)XrReferenceSpaceType.Local);
            Assert.Equal(1, (int)XrReferenceSpaceType.View);
            Assert.Equal(2, (int)XrViewConfigurationType.PrimaryStereo);
            Assert.Equal(1, (int)XrEnvironmentBlendMode.Opaque);
            Assert.Equal(1, (int)XrFormFactor.HeadMountedDisplay);
            Assert.Equal(0, (int)XrEyeVisibility.Both);

            Assert.Equal(2ul, (ulong)XrCompositionLayerFlags.BlendTextureSourceAlpha);
            Assert.Equal(4ul, (ulong)XrCompositionLayerFlags.UnpremultipliedAlpha);
            Assert.Equal(1ul, (ulong)XrSwapchainUsageFlags.ColorAttachment);
            Assert.Equal(16ul, (ulong)XrSwapchainUsageFlags.TransferDst);
            Assert.Equal(32ul, (ulong)XrSwapchainUsageFlags.Sampled);
            Assert.Equal(1ul, (ulong)XrViewStateFlags.OrientationValid);
            Assert.Equal(2ul, (ulong)XrSpaceLocationFlags.PositionValid);

            Assert.Equal(9223372036854775807L, XrConstants.InfiniteDuration);
            Assert.Equal(281474976710656ul, XrConstants.MakeVersion(1, 0, 0));
        }

        [Fact]
        public void Result_helpers()
        {
            Assert.True(XrResult.FrameDiscarded.IsSuccess());
            Assert.True(XrResult.Success.IsSuccess());
            Assert.False(XrResult.ErrorSessionLost.IsSuccess());
            Assert.Equal("XR_ERROR_RUNTIME_UNAVAILABLE", XrApi.ResultName(XrResult.ErrorRuntimeUnavailable));
            Assert.Equal("XR_ERROR_GRAPHICS_REQUIREMENTS_CALL_MISSING", XrApi.ResultName(XrResult.ErrorGraphicsRequirementsCallMissing));
            Assert.Equal("XR_FRAME_DISCARDED", XrApi.ResultName(XrResult.FrameDiscarded));
        }

        [Fact]
        public unsafe void Fixed_char_arrays_round_trip_and_never_overrun()
        {
            byte* buf = stackalloc byte[8];
            for (int i = 0; i < 8; i++) buf[i] = (byte)'A';
            Assert.Equal("AAAAAAAA", XrStrings.FromUtf8(buf, 8)); // no terminator: stops at capacity

            Assert.Equal(7, XrStrings.ToUtf8("ConSimVR!", buf, 8)); // truncated, NUL-terminated
            Assert.Equal("ConSimV", XrStrings.FromUtf8(buf, 8));

            Assert.Equal(6, XrStrings.ToUtf8("abcdé€", buf, 8)); // 'é' fits (2 bytes), '€' (3 bytes) does not
            Assert.Equal("abcdé", XrStrings.FromUtf8(buf, 8));
        }

        // ------------------------------------------------------------------ runtime guards (no OpenXR runtime involved)

        private sealed class ListLog : IVrLog
        {
            public readonly List<string> Lines = new List<string>();
            public void Info(string message) => Lines.Add("I " + message);
            public void Warn(string message) => Lines.Add("W " + message);
            public void Error(string message) => Lines.Add("E " + message);
        }

        [Fact]
        public void Frame_calls_on_fresh_runtime_return_false_without_throwing()
        {
            var log = new ListLog();
            var rt = new OpenXrRuntime(@"Z:\does\not\exist\openxr_loader.dll", log);

            Assert.False(rt.WaitFrame(out XrFrameState fs));
            Assert.Equal(XrStructureType.FrameState, fs.Type);
            Assert.False(rt.BeginFrame());
            Assert.False(rt.EndFrame(0, ReadOnlySpan<ProjectionView>.Empty, false, default));

            var views = new ProjectionView[2];
            var quad = new QuadLayer { Swapchain = 1, Width = 16, Height = 16, WidthMeters = 1, HeightMeters = 1, Pose = XrPosef.Identity };
            Assert.False(rt.EndFrame(1, views, true, in quad));

            var swapchain = new XrSwapchainInfo { Handle = 1, Width = 16, Height = 16, Images = new IntPtr[1] };
            Assert.False(rt.AcquireWaitImage(swapchain, out int index));
            Assert.Equal(-1, index);
            Assert.False(rt.AcquireWaitImage(null, out _));
            Assert.False(rt.ReleaseImage(swapchain));
            Assert.False(rt.LocateViews(1, new XrView[2], out ulong flags));
            Assert.Equal(0ul, flags);
            Assert.False(rt.LocateHead(1, out XrPosef head));
            Assert.Equal(1f, head.Orientation.W);
            rt.PollEvents();

            Assert.False(rt.IsSessionRunning);
            Assert.False(rt.ExitRequested);
            Assert.Equal(XrSessionState.Unknown, rt.SessionState);
            Assert.Empty(rt.SwapchainFormats);

            Assert.False(rt.CreateSession(new IntPtr(1), out string sessionError));
            Assert.Contains("CreateInstance", sessionError);
            Assert.Null(rt.CreateSwapchain(28, 16, 16, out string swapchainError));
            Assert.NotNull(swapchainError);

            Assert.False(rt.CreateInstance("ConSimVR tests", out string error));
            Assert.Contains("not found", error);

            rt.Dispose();
            rt.Dispose(); // idempotent
            Assert.False(rt.BeginFrame());
            Assert.False(rt.CreateInstance("ConSimVR tests", out _));
        }

        // ------------------------------------------------------------------ integration (opt-in: loads the active runtime)

        /// <summary>Runs only with CONSIMVR_XR_INTEGRATION=1, because it loads the user's active OpenXR runtime.</summary>
        public sealed class XrIntegrationFactAttribute : FactAttribute
        {
            public XrIntegrationFactAttribute()
            {
                if (Environment.GetEnvironmentVariable("CONSIMVR_XR_INTEGRATION") != "1")
                    Skip = "Set CONSIMVR_XR_INTEGRATION=1 to run (loads the active OpenXR runtime).";
            }
        }

        private static string FindLoader()
        {
            string fromEnv = Environment.GetEnvironmentVariable("CONSIMVR_XR_LOADER");
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

            string relative = Path.Combine("vendor", "openxr-loader", "x64", "bin", "openxr_loader.dll");
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return relative;
        }

        [XrIntegrationFact]
        public void Real_loader_enumerates_instance_extensions()
        {
            Assert.True(XrApi.TryLoad(FindLoader(), out XrApi api, out string error), error);

            XrResult r = api.EnumerateInstanceExtensions(out List<string> extensions);
            Assert.True(r.IsSuccess(), "xrEnumerateInstanceExtensionProperties: " + api.ResultToString(r));
            Assert.NotEmpty(extensions);
            Assert.Contains(XrConstants.KhrD3D11EnableExtensionName, extensions);
        }
    }
}
