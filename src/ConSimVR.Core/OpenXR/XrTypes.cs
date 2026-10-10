using System;
using System.Runtime.InteropServices;

// Hand-written OpenXR 1.0 types (x64). Values and layouts follow the Khronos headers in
// vendor/openxr-loader/include/openxr (openxr.h / openxr_platform.h, version 1.1.62); OpenXrTests checks the
// struct layouts against numbers produced by clang from those headers.
// Conventions: handles, XrSystemId and XrPath are ulong; XrBool32 is uint; XrTime/XrDuration are long;
// XrFlags64 types are [Flags] enums over ulong; char arrays are fixed byte buffers (UTF-8, NUL-terminated).
namespace ConSimVR.Core.OpenXR
{
    public static class XrConstants
    {
        public const ulong NullHandle = 0;
        public const long InfiniteDuration = 0x7fffffffffffffffL;
        public const long NoDuration = 0;

        public const int MaxExtensionNameSize = 128;
        public const int MaxSystemNameSize = 256;
        public const int MaxApplicationNameSize = 128;
        public const int MaxEngineNameSize = 128;
        public const int MaxRuntimeNameSize = 128;
        public const int MaxResultStringSize = 64;
        public const int EventDataVaryingSize = 4000;

        public const string KhrD3D11EnableExtensionName = "XR_KHR_D3D11_enable";

        /// <summary>XR_MAKE_VERSION.</summary>
        public static ulong MakeVersion(ulong major, ulong minor, ulong patch) =>
            ((major & 0xffffUL) << 48) | ((minor & 0xffffUL) << 32) | (patch & 0xffffffffUL);

        public static string VersionToString(ulong version) =>
            $"{(version >> 48) & 0xffff}.{(version >> 32) & 0xffff}.{version & 0xffffffff}";
    }

    // ---------------------------------------------------------------- enums

    public enum XrResult : int
    {
        Success = 0,
        TimeoutExpired = 1,
        SessionLossPending = 3,
        EventUnavailable = 4,
        SpaceBoundsUnavailable = 7,
        SessionNotFocused = 8,
        FrameDiscarded = 9,
        ErrorValidationFailure = -1,
        ErrorRuntimeFailure = -2,
        ErrorOutOfMemory = -3,
        ErrorApiVersionUnsupported = -4,
        ErrorInitializationFailed = -6,
        ErrorFunctionUnsupported = -7,
        ErrorFeatureUnsupported = -8,
        ErrorExtensionNotPresent = -9,
        ErrorLimitReached = -10,
        ErrorSizeInsufficient = -11,
        ErrorHandleInvalid = -12,
        ErrorInstanceLost = -13,
        ErrorSessionRunning = -14,
        ErrorSessionNotRunning = -16,
        ErrorSessionLost = -17,
        ErrorSystemInvalid = -18,
        ErrorPathInvalid = -19,
        ErrorPathCountExceeded = -20,
        ErrorPathFormatInvalid = -21,
        ErrorPathUnsupported = -22,
        ErrorLayerInvalid = -23,
        ErrorLayerLimitExceeded = -24,
        ErrorSwapchainRectInvalid = -25,
        ErrorSwapchainFormatUnsupported = -26,
        ErrorActionTypeMismatch = -27,
        ErrorSessionNotReady = -28,
        ErrorSessionNotStopping = -29,
        ErrorTimeInvalid = -30,
        ErrorReferenceSpaceUnsupported = -31,
        ErrorFileAccessError = -32,
        ErrorFileContentsInvalid = -33,
        ErrorFormFactorUnsupported = -34,
        ErrorFormFactorUnavailable = -35,
        ErrorApiLayerNotPresent = -36,
        ErrorCallOrderInvalid = -37,
        ErrorGraphicsDeviceInvalid = -38,
        ErrorPoseInvalid = -39,
        ErrorIndexOutOfRange = -40,
        ErrorViewConfigurationTypeUnsupported = -41,
        ErrorEnvironmentBlendModeUnsupported = -42,
        ErrorNameDuplicated = -44,
        ErrorNameInvalid = -45,
        ErrorActionsetNotAttached = -46,
        ErrorActionsetsAlreadyAttached = -47,
        ErrorLocalizedNameDuplicated = -48,
        ErrorLocalizedNameInvalid = -49,
        ErrorGraphicsRequirementsCallMissing = -50,
        ErrorRuntimeUnavailable = -51,
        ErrorPermissionInsufficient = -1000710000,
        ErrorExtensionDependencyNotEnabled = -1000710001,
    }

    public static class XrResultExtensions
    {
        /// <summary>XR_SUCCEEDED: every non-negative code (including XR_FRAME_DISCARDED etc.) is a success.</summary>
        public static bool IsSuccess(this XrResult result) => (int)result >= 0;

        public static bool IsFailure(this XrResult result) => (int)result < 0;
    }

    /// <summary>Only the XrStructureType values this binding uses.</summary>
    public enum XrStructureType : int
    {
        Unknown = 0,
        ExtensionProperties = 2,
        InstanceCreateInfo = 3,
        SystemGetInfo = 4,
        SystemProperties = 5,
        ViewLocateInfo = 6,
        View = 7,
        SessionCreateInfo = 8,
        SwapchainCreateInfo = 9,
        SessionBeginInfo = 10,
        ViewState = 11,
        FrameEndInfo = 12,
        EventDataBuffer = 16,
        EventDataInstanceLossPending = 17,
        EventDataSessionStateChanged = 18,
        InstanceProperties = 32,
        FrameWaitInfo = 33,
        CompositionLayerProjection = 35,
        CompositionLayerQuad = 36,
        ReferenceSpaceCreateInfo = 37,
        EventDataReferenceSpaceChangePending = 40,
        ViewConfigurationView = 41,
        SpaceLocation = 42,
        FrameState = 44,
        FrameBeginInfo = 46,
        CompositionLayerProjectionView = 48,
        EventDataEventsLost = 49,
        SwapchainImageAcquireInfo = 55,
        SwapchainImageWaitInfo = 56,
        SwapchainImageReleaseInfo = 57,
        GraphicsBindingD3D11KHR = 1000027000,
        SwapchainImageD3D11KHR = 1000027001,
        GraphicsRequirementsD3D11KHR = 1000027002,
    }

    public enum XrFormFactor : int
    {
        HeadMountedDisplay = 1,
        HandheldDisplay = 2,
    }

    public enum XrViewConfigurationType : int
    {
        PrimaryMono = 1,
        PrimaryStereo = 2,
    }

    public enum XrEnvironmentBlendMode : int
    {
        Opaque = 1,
        Additive = 2,
        AlphaBlend = 3,
    }

    public enum XrReferenceSpaceType : int
    {
        View = 1,
        Local = 2,
        Stage = 3,
        LocalFloor = 1000426000,
    }

    public enum XrSessionState : int
    {
        Unknown = 0,
        Idle = 1,
        Ready = 2,
        Synchronized = 3,
        Visible = 4,
        Focused = 5,
        Stopping = 6,
        LossPending = 7,
        Exiting = 8,
    }

    public enum XrEyeVisibility : int
    {
        Both = 0,
        Left = 1,
        Right = 2,
    }

    [Flags]
    public enum XrViewStateFlags : ulong
    {
        None = 0,
        OrientationValid = 0x1,
        PositionValid = 0x2,
        OrientationTracked = 0x4,
        PositionTracked = 0x8,
    }

    [Flags]
    public enum XrSpaceLocationFlags : ulong
    {
        None = 0,
        OrientationValid = 0x1,
        PositionValid = 0x2,
        OrientationTracked = 0x4,
        PositionTracked = 0x8,
    }

    [Flags]
    public enum XrSwapchainUsageFlags : ulong
    {
        None = 0,
        ColorAttachment = 0x1,
        DepthStencilAttachment = 0x2,
        UnorderedAccess = 0x4,
        TransferSrc = 0x8,
        TransferDst = 0x10,
        Sampled = 0x20,
        MutableFormat = 0x40,
    }

    [Flags]
    public enum XrCompositionLayerFlags : ulong
    {
        None = 0,
        CorrectChromaticAberration = 0x1,
        BlendTextureSourceAlpha = 0x2,
        UnpremultipliedAlpha = 0x4,
    }

    // ---------------------------------------------------------------- math / geometry

    [StructLayout(LayoutKind.Sequential)]
    public struct XrVector3f
    {
        public float X, Y, Z;

        public XrVector3f(float x, float y, float z) { X = x; Y = y; Z = z; }

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrQuaternionf
    {
        public float X, Y, Z, W;

        public XrQuaternionf(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

        public static XrQuaternionf Identity => new XrQuaternionf(0f, 0f, 0f, 1f);

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3}, {W:F3})";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrPosef
    {
        public XrQuaternionf Orientation;
        public XrVector3f Position;

        public XrPosef(XrQuaternionf orientation, XrVector3f position) { Orientation = orientation; Position = position; }

        public static XrPosef Identity => new XrPosef(XrQuaternionf.Identity, default);

        public override string ToString() => $"pos {Position} rot {Orientation}";
    }

    /// <summary>Field-of-view half angles in radians (left/down are normally negative).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XrFovf
    {
        public float AngleLeft, AngleRight, AngleUp, AngleDown;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrOffset2Di
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrExtent2Di
    {
        public int Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrRect2Di
    {
        public XrOffset2Di Offset;
        public XrExtent2Di Extent;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrExtent2Df
    {
        public float Width, Height;
    }

    /// <summary>Windows LUID (DWORD LowPart; LONG HighPart), 4-byte aligned.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XrLuid
    {
        public uint LowPart;
        public int HighPart;

        /// <summary>LowPart | HighPart &lt;&lt; 32, the same packing as D3D11.TryGetAdapterLuid.</summary>
        public ulong ToUInt64() => LowPart | ((ulong)(uint)HighPart << 32);
    }

    // ---------------------------------------------------------------- instance / system

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrExtensionProperties
    {
        public XrStructureType Type;
        public void* Next;
        public fixed byte ExtensionName[XrConstants.MaxExtensionNameSize];
        public uint ExtensionVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrApplicationInfo
    {
        public fixed byte ApplicationName[XrConstants.MaxApplicationNameSize];
        public uint ApplicationVersion;
        public fixed byte EngineName[XrConstants.MaxEngineNameSize];
        public uint EngineVersion;
        public ulong ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrInstanceCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public ulong CreateFlags;
        public XrApplicationInfo ApplicationInfo;
        public uint EnabledApiLayerCount;
        public byte** EnabledApiLayerNames;
        public uint EnabledExtensionCount;
        public byte** EnabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrInstanceProperties
    {
        public XrStructureType Type;
        public void* Next;
        public ulong RuntimeVersion;
        public fixed byte RuntimeName[XrConstants.MaxRuntimeNameSize];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSystemGetInfo
    {
        public XrStructureType Type;
        public void* Next;
        public XrFormFactor FormFactor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrSystemGraphicsProperties
    {
        public uint MaxSwapchainImageHeight;
        public uint MaxSwapchainImageWidth;
        public uint MaxLayerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrSystemTrackingProperties
    {
        public uint OrientationTracking; // XrBool32
        public uint PositionTracking;    // XrBool32
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSystemProperties
    {
        public XrStructureType Type;
        public void* Next;
        public ulong SystemId;
        public uint VendorId;
        public fixed byte SystemName[XrConstants.MaxSystemNameSize];
        public XrSystemGraphicsProperties GraphicsProperties;
        public XrSystemTrackingProperties TrackingProperties;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrViewConfigurationView
    {
        public XrStructureType Type;
        public void* Next;
        public uint RecommendedImageRectWidth;
        public uint MaxImageRectWidth;
        public uint RecommendedImageRectHeight;
        public uint MaxImageRectHeight;
        public uint RecommendedSwapchainSampleCount;
        public uint MaxSwapchainSampleCount;
    }

    // ---------------------------------------------------------------- D3D11 (XR_KHR_D3D11_enable)

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrGraphicsRequirementsD3D11KHR
    {
        public XrStructureType Type;
        public void* Next;
        public XrLuid AdapterLuid;
        public int MinFeatureLevel; // D3D_FEATURE_LEVEL, e.g. 0xb000 = 11_0
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrGraphicsBindingD3D11KHR
    {
        public XrStructureType Type;
        public void* Next;
        public IntPtr Device; // ID3D11Device*
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainImageD3D11KHR
    {
        public XrStructureType Type;
        public void* Next;
        public IntPtr Texture; // ID3D11Texture2D*
    }

    // ---------------------------------------------------------------- session / spaces

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSessionCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public ulong CreateFlags;
        public ulong SystemId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSessionBeginInfo
    {
        public XrStructureType Type;
        public void* Next;
        public XrViewConfigurationType PrimaryViewConfigurationType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrReferenceSpaceCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public XrReferenceSpaceType ReferenceSpaceType;
        public XrPosef PoseInReferenceSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSpaceLocation
    {
        public XrStructureType Type;
        public void* Next;
        public XrSpaceLocationFlags LocationFlags;
        public XrPosef Pose;
    }

    // ---------------------------------------------------------------- swapchains

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public ulong CreateFlags;
        public XrSwapchainUsageFlags UsageFlags;
        public long Format;
        public uint SampleCount;
        public uint Width;
        public uint Height;
        public uint FaceCount;
        public uint ArraySize;
        public uint MipCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainImageBaseHeader
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainImageAcquireInfo
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainImageWaitInfo
    {
        public XrStructureType Type;
        public void* Next;
        public long Timeout; // XrDuration
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainImageReleaseInfo
    {
        public XrStructureType Type;
        public void* Next;
    }

    // ---------------------------------------------------------------- frame loop

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrFrameWaitInfo
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrFrameState
    {
        public XrStructureType Type;
        public void* Next;
        public long PredictedDisplayTime;   // XrTime
        public long PredictedDisplayPeriod; // XrDuration
        public uint ShouldRender;           // XrBool32
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrFrameBeginInfo
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrCompositionLayerBaseHeader
    {
        public XrStructureType Type;
        public void* Next;
        public XrCompositionLayerFlags LayerFlags;
        public ulong Space;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrFrameEndInfo
    {
        public XrStructureType Type;
        public void* Next;
        public long DisplayTime;
        public XrEnvironmentBlendMode EnvironmentBlendMode;
        public uint LayerCount;
        public XrCompositionLayerBaseHeader** Layers;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrViewLocateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public XrViewConfigurationType ViewConfigurationType;
        public long DisplayTime;
        public ulong Space;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrViewState
    {
        public XrStructureType Type;
        public void* Next;
        public XrViewStateFlags ViewStateFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrView
    {
        public XrStructureType Type;
        public void* Next;
        public XrPosef Pose;
        public XrFovf Fov;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrSwapchainSubImage
    {
        public ulong Swapchain;
        public XrRect2Di ImageRect;
        public uint ImageArrayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrCompositionLayerProjectionView
    {
        public XrStructureType Type;
        public void* Next;
        public XrPosef Pose;
        public XrFovf Fov;
        public XrSwapchainSubImage SubImage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrCompositionLayerProjection
    {
        public XrStructureType Type;
        public void* Next;
        public XrCompositionLayerFlags LayerFlags;
        public ulong Space;
        public uint ViewCount;
        public XrCompositionLayerProjectionView* Views;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrCompositionLayerQuad
    {
        public XrStructureType Type;
        public void* Next;
        public XrCompositionLayerFlags LayerFlags;
        public ulong Space;
        public XrEyeVisibility EyeVisibility;
        public XrSwapchainSubImage SubImage;
        public XrPosef Pose;
        public XrExtent2Df Size;
    }

    // ---------------------------------------------------------------- events

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrEventDataBuffer
    {
        public XrStructureType Type;
        public void* Next;
        public fixed byte Varying[XrConstants.EventDataVaryingSize];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrEventDataEventsLost
    {
        public XrStructureType Type;
        public void* Next;
        public uint LostEventCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrEventDataInstanceLossPending
    {
        public XrStructureType Type;
        public void* Next;
        public long LossTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrEventDataSessionStateChanged
    {
        public XrStructureType Type;
        public void* Next;
        public ulong Session;
        public XrSessionState State;
        public long Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrEventDataReferenceSpaceChangePending
    {
        public XrStructureType Type;
        public void* Next;
        public ulong Session;
        public XrReferenceSpaceType ReferenceSpaceType;
        public long ChangeTime;
        public uint PoseValid; // XrBool32
        public XrPosef PoseInPreviousSpace;
    }
}
