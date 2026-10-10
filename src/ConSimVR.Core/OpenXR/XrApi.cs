using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ConSimVR.Core.OpenXR
{
    /// <summary>
    /// OpenXR entry points resolved through the Khronos loader's xrGetInstanceProcAddr. Global functions are
    /// resolved with XR_NULL_HANDLE on load; instance functions (including the XR_KHR_D3D11_enable extension
    /// function) by <see cref="LoadInstanceFunctions"/> after xrCreateInstance. Field names mirror the C API.
    /// XRAPI_CALL is __stdcall on Windows (identical to the platform default on x64).
    /// </summary>
    public sealed unsafe class XrApi
    {
        public IntPtr LibraryHandle { get; private set; }
        public string LoaderPath { get; private set; }
        /// <summary>Instance the instance-level pointers were resolved for (0 = none). Used by ResultToString.</summary>
        public ulong Instance { get; private set; }

        // ---- loader export
        public delegate* unmanaged[Stdcall]<ulong, byte*, IntPtr*, XrResult> xrGetInstanceProcAddr;

        // ---- global (resolved with XR_NULL_HANDLE)
        public delegate* unmanaged[Stdcall]<byte*, uint, uint*, XrExtensionProperties*, XrResult> xrEnumerateInstanceExtensionProperties;
        public delegate* unmanaged[Stdcall]<XrInstanceCreateInfo*, ulong*, XrResult> xrCreateInstance;

        // ---- instance
        public delegate* unmanaged[Stdcall]<ulong, XrResult> xrDestroyInstance;
        public delegate* unmanaged[Stdcall]<ulong, XrInstanceProperties*, XrResult> xrGetInstanceProperties;
        public delegate* unmanaged[Stdcall]<ulong, XrEventDataBuffer*, XrResult> xrPollEvent;
        public delegate* unmanaged[Stdcall]<ulong, XrResult, byte*, XrResult> xrResultToString;
        public delegate* unmanaged[Stdcall]<ulong, XrSystemGetInfo*, ulong*, XrResult> xrGetSystem;
        public delegate* unmanaged[Stdcall]<ulong, ulong, XrSystemProperties*, XrResult> xrGetSystemProperties;
        public delegate* unmanaged[Stdcall]<ulong, ulong, XrViewConfigurationType, uint, uint*, XrViewConfigurationView*, XrResult> xrEnumerateViewConfigurationViews;
        public delegate* unmanaged[Stdcall]<ulong, ulong, XrGraphicsRequirementsD3D11KHR*, XrResult> xrGetD3D11GraphicsRequirementsKHR;

        // ---- session
        public delegate* unmanaged[Stdcall]<ulong, XrSessionCreateInfo*, ulong*, XrResult> xrCreateSession;
        public delegate* unmanaged[Stdcall]<ulong, XrResult> xrDestroySession;
        public delegate* unmanaged[Stdcall]<ulong, XrSessionBeginInfo*, XrResult> xrBeginSession;
        public delegate* unmanaged[Stdcall]<ulong, XrResult> xrEndSession;
        public delegate* unmanaged[Stdcall]<ulong, XrResult> xrRequestExitSession;

        // ---- spaces
        public delegate* unmanaged[Stdcall]<ulong, XrReferenceSpaceCreateInfo*, ulong*, XrResult> xrCreateReferenceSpace;
        public delegate* unmanaged[Stdcall]<ulong, XrResult> xrDestroySpace;
        public delegate* unmanaged[Stdcall]<ulong, ulong, long, XrSpaceLocation*, XrResult> xrLocateSpace;

        // ---- swapchains
        public delegate* unmanaged[Stdcall]<ulong, uint, uint*, long*, XrResult> xrEnumerateSwapchainFormats;
        public delegate* unmanaged[Stdcall]<ulong, XrSwapchainCreateInfo*, ulong*, XrResult> xrCreateSwapchain;
        public delegate* unmanaged[Stdcall]<ulong, XrResult> xrDestroySwapchain;
        public delegate* unmanaged[Stdcall]<ulong, uint, uint*, XrSwapchainImageBaseHeader*, XrResult> xrEnumerateSwapchainImages;
        public delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageAcquireInfo*, uint*, XrResult> xrAcquireSwapchainImage;
        public delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageWaitInfo*, XrResult> xrWaitSwapchainImage;
        public delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageReleaseInfo*, XrResult> xrReleaseSwapchainImage;

        // ---- frame loop
        public delegate* unmanaged[Stdcall]<ulong, XrFrameWaitInfo*, XrFrameState*, XrResult> xrWaitFrame;
        public delegate* unmanaged[Stdcall]<ulong, XrFrameBeginInfo*, XrResult> xrBeginFrame;
        public delegate* unmanaged[Stdcall]<ulong, XrFrameEndInfo*, XrResult> xrEndFrame;
        public delegate* unmanaged[Stdcall]<ulong, XrViewLocateInfo*, XrViewState*, uint, uint*, XrView*, XrResult> xrLocateViews;

        private XrApi() { }

        /// <summary>
        /// Loads the loader DLL from <paramref name="loaderPath"/> (made absolute) and resolves the global functions.
        /// The library is intentionally never freed: runtimes keep threads and hooks alive inside the process.
        /// </summary>
        public static bool TryLoad(string loaderPath, out XrApi api, out string error)
        {
            api = null;
            if (string.IsNullOrEmpty(loaderPath))
            {
                error = "OpenXR loader path is empty.";
                return false;
            }

            string fullPath;
            try { fullPath = Path.GetFullPath(loaderPath); }
            catch (Exception e)
            {
                error = $"Invalid OpenXR loader path '{loaderPath}': {e.Message}";
                return false;
            }

            if (!File.Exists(fullPath))
            {
                error = $"OpenXR loader not found at '{fullPath}'.";
                return false;
            }

            if (!NativeLibrary.TryLoad(fullPath, out IntPtr lib))
            {
                error = $"Failed to load the OpenXR loader '{fullPath}' (wrong architecture or missing dependency?).";
                return false;
            }

            if (!NativeLibrary.TryGetExport(lib, "xrGetInstanceProcAddr", out IntPtr gipa) || gipa == IntPtr.Zero)
            {
                NativeLibrary.Free(lib);
                error = $"'{fullPath}' does not export xrGetInstanceProcAddr; is it really openxr_loader.dll?";
                return false;
            }

            var a = new XrApi { LibraryHandle = lib, LoaderPath = fullPath };
            a.xrGetInstanceProcAddr = (delegate* unmanaged[Stdcall]<ulong, byte*, IntPtr*, XrResult>)(void*)gipa;

            var missing = new List<string>();
            a.xrEnumerateInstanceExtensionProperties = (delegate* unmanaged[Stdcall]<byte*, uint, uint*, XrExtensionProperties*, XrResult>)
                a.Resolve(XrConstants.NullHandle, "xrEnumerateInstanceExtensionProperties", missing);
            a.xrCreateInstance = (delegate* unmanaged[Stdcall]<XrInstanceCreateInfo*, ulong*, XrResult>)
                a.Resolve(XrConstants.NullHandle, "xrCreateInstance", missing);

            if (missing.Count > 0)
            {
                error = "OpenXR loader is missing global functions: " + string.Join(", ", missing);
                return false;
            }

            api = a;
            error = null;
            return true;
        }

        /// <summary>Resolves every instance-level function used by <see cref="OpenXrRuntime"/> for <paramref name="instance"/>.</summary>
        public bool LoadInstanceFunctions(ulong instance, out string error)
        {
            var m = new List<string>();
            xrDestroyInstance = (delegate* unmanaged[Stdcall]<ulong, XrResult>)Resolve(instance, "xrDestroyInstance", m);
            xrGetInstanceProperties = (delegate* unmanaged[Stdcall]<ulong, XrInstanceProperties*, XrResult>)Resolve(instance, "xrGetInstanceProperties", m);
            xrPollEvent = (delegate* unmanaged[Stdcall]<ulong, XrEventDataBuffer*, XrResult>)Resolve(instance, "xrPollEvent", m);
            xrResultToString = (delegate* unmanaged[Stdcall]<ulong, XrResult, byte*, XrResult>)Resolve(instance, "xrResultToString", m);
            xrGetSystem = (delegate* unmanaged[Stdcall]<ulong, XrSystemGetInfo*, ulong*, XrResult>)Resolve(instance, "xrGetSystem", m);
            xrGetSystemProperties = (delegate* unmanaged[Stdcall]<ulong, ulong, XrSystemProperties*, XrResult>)Resolve(instance, "xrGetSystemProperties", m);
            xrEnumerateViewConfigurationViews = (delegate* unmanaged[Stdcall]<ulong, ulong, XrViewConfigurationType, uint, uint*, XrViewConfigurationView*, XrResult>)
                Resolve(instance, "xrEnumerateViewConfigurationViews", m);
            xrGetD3D11GraphicsRequirementsKHR = (delegate* unmanaged[Stdcall]<ulong, ulong, XrGraphicsRequirementsD3D11KHR*, XrResult>)
                Resolve(instance, "xrGetD3D11GraphicsRequirementsKHR", m);

            xrCreateSession = (delegate* unmanaged[Stdcall]<ulong, XrSessionCreateInfo*, ulong*, XrResult>)Resolve(instance, "xrCreateSession", m);
            xrDestroySession = (delegate* unmanaged[Stdcall]<ulong, XrResult>)Resolve(instance, "xrDestroySession", m);
            xrBeginSession = (delegate* unmanaged[Stdcall]<ulong, XrSessionBeginInfo*, XrResult>)Resolve(instance, "xrBeginSession", m);
            xrEndSession = (delegate* unmanaged[Stdcall]<ulong, XrResult>)Resolve(instance, "xrEndSession", m);
            xrRequestExitSession = (delegate* unmanaged[Stdcall]<ulong, XrResult>)Resolve(instance, "xrRequestExitSession", m);

            xrCreateReferenceSpace = (delegate* unmanaged[Stdcall]<ulong, XrReferenceSpaceCreateInfo*, ulong*, XrResult>)Resolve(instance, "xrCreateReferenceSpace", m);
            xrDestroySpace = (delegate* unmanaged[Stdcall]<ulong, XrResult>)Resolve(instance, "xrDestroySpace", m);
            xrLocateSpace = (delegate* unmanaged[Stdcall]<ulong, ulong, long, XrSpaceLocation*, XrResult>)Resolve(instance, "xrLocateSpace", m);

            xrEnumerateSwapchainFormats = (delegate* unmanaged[Stdcall]<ulong, uint, uint*, long*, XrResult>)Resolve(instance, "xrEnumerateSwapchainFormats", m);
            xrCreateSwapchain = (delegate* unmanaged[Stdcall]<ulong, XrSwapchainCreateInfo*, ulong*, XrResult>)Resolve(instance, "xrCreateSwapchain", m);
            xrDestroySwapchain = (delegate* unmanaged[Stdcall]<ulong, XrResult>)Resolve(instance, "xrDestroySwapchain", m);
            xrEnumerateSwapchainImages = (delegate* unmanaged[Stdcall]<ulong, uint, uint*, XrSwapchainImageBaseHeader*, XrResult>)
                Resolve(instance, "xrEnumerateSwapchainImages", m);
            xrAcquireSwapchainImage = (delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageAcquireInfo*, uint*, XrResult>)Resolve(instance, "xrAcquireSwapchainImage", m);
            xrWaitSwapchainImage = (delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageWaitInfo*, XrResult>)Resolve(instance, "xrWaitSwapchainImage", m);
            xrReleaseSwapchainImage = (delegate* unmanaged[Stdcall]<ulong, XrSwapchainImageReleaseInfo*, XrResult>)Resolve(instance, "xrReleaseSwapchainImage", m);

            xrWaitFrame = (delegate* unmanaged[Stdcall]<ulong, XrFrameWaitInfo*, XrFrameState*, XrResult>)Resolve(instance, "xrWaitFrame", m);
            xrBeginFrame = (delegate* unmanaged[Stdcall]<ulong, XrFrameBeginInfo*, XrResult>)Resolve(instance, "xrBeginFrame", m);
            xrEndFrame = (delegate* unmanaged[Stdcall]<ulong, XrFrameEndInfo*, XrResult>)Resolve(instance, "xrEndFrame", m);
            xrLocateViews = (delegate* unmanaged[Stdcall]<ulong, XrViewLocateInfo*, XrViewState*, uint, uint*, XrView*, XrResult>)Resolve(instance, "xrLocateViews", m);

            if (m.Count > 0)
            {
                error = "OpenXR runtime did not provide: " + string.Join(", ", m);
                return false;
            }

            Instance = instance;
            error = null;
            return true;
        }

        /// <summary>Forget the instance after xrDestroyInstance so ResultToString stops using it.</summary>
        public void ClearInstance() => Instance = XrConstants.NullHandle;

        private void* Resolve(ulong instance, string name, List<string> missing)
        {
            int len = Encoding.ASCII.GetByteCount(name);
            byte* cname = stackalloc byte[len + 1];
            fixed (char* chars = name)
                Encoding.ASCII.GetBytes(chars, name.Length, cname, len);
            cname[len] = 0;

            IntPtr fn = IntPtr.Zero;
            XrResult r = xrGetInstanceProcAddr(instance, cname, &fn);
            if (r.IsFailure() || fn == IntPtr.Zero)
            {
                missing.Add($"{name} ({ResultName(r)})");
                return null;
            }
            return (void*)fn;
        }

        /// <summary>
        /// Enumerates the instance extensions of the active runtime (goes through the loader, so it loads the
        /// runtime DLL but creates no instance).
        /// </summary>
        public XrResult EnumerateInstanceExtensions(out List<string> names)
        {
            names = new List<string>();
            uint count = 0;
            XrResult r = xrEnumerateInstanceExtensionProperties(null, 0, &count, null);
            if (r.IsFailure()) return r;
            if (count == 0) return r;

            var props = new XrExtensionProperties[count];
            for (int i = 0; i < props.Length; i++) props[i].Type = XrStructureType.ExtensionProperties;
            fixed (XrExtensionProperties* p = props)
            {
                r = xrEnumerateInstanceExtensionProperties(null, count, &count, p);
                if (r.IsFailure()) return r;
                for (int i = 0; i < count && i < props.Length; i++)
                    names.Add(XrStrings.FromUtf8(p[i].ExtensionName, XrConstants.MaxExtensionNameSize));
            }
            return r;
        }

        /// <summary>
        /// "XR_ERROR_X (-n)" via xrResultToString when an instance exists; falls back to a name derived from the
        /// <see cref="XrResult"/> enum before instance creation.
        /// </summary>
        public string ResultToString(XrResult result)
        {
            ulong instance = Instance;
            if (instance != XrConstants.NullHandle && xrResultToString != null)
            {
                byte* buf = stackalloc byte[XrConstants.MaxResultStringSize];
                buf[0] = 0;
                if (xrResultToString(instance, result, buf).IsSuccess() && buf[0] != 0)
                    return $"{XrStrings.FromUtf8(buf, XrConstants.MaxResultStringSize)} ({(int)result})";
            }
            return $"{ResultName(result)} ({(int)result})";
        }

        /// <summary>Converts e.g. <c>ErrorRuntimeUnavailable</c> to <c>XR_ERROR_RUNTIME_UNAVAILABLE</c> without a runtime.</summary>
        public static string ResultName(XrResult result)
        {
            if (!Enum.IsDefined(typeof(XrResult), result))
                return (int)result < 0 ? "XR_UNKNOWN_FAILURE" : "XR_UNKNOWN_SUCCESS";

            string pascal = result.ToString();
            var sb = new StringBuilder("XR_", pascal.Length + 12);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (i > 0 && char.IsUpper(c)) sb.Append('_');
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }
    }

    /// <summary>UTF-8 helpers for the fixed-size char arrays in OpenXR structs.</summary>
    public static unsafe class XrStrings
    {
        /// <summary>Decodes up to <paramref name="capacity"/> bytes, stopping at the first NUL (never reads past capacity).</summary>
        public static string FromUtf8(byte* p, int capacity)
        {
            if (p == null || capacity <= 0) return string.Empty;
            int len = 0;
            while (len < capacity && p[len] != 0) len++;
            return len == 0 ? string.Empty : Encoding.UTF8.GetString(p, len);
        }

        /// <summary>
        /// Writes <paramref name="value"/> as NUL-terminated UTF-8 into a buffer of <paramref name="capacity"/> bytes,
        /// truncating on a code point boundary. Returns the number of bytes written excluding the terminator.
        /// </summary>
        public static int ToUtf8(string value, byte* dst, int capacity)
        {
            if (dst == null || capacity <= 0) return 0;
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            int n = System.Math.Min(bytes.Length, capacity - 1);
            // If the first dropped byte is a continuation byte, back off to the start of that code point.
            while (n > 0 && n < bytes.Length && (bytes[n] & 0xC0) == 0x80) n--;
            for (int i = 0; i < n; i++) dst[i] = bytes[i];
            dst[n] = 0;
            return n;
        }
    }
}
