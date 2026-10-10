using System;
using System.Runtime.InteropServices;

namespace ConSimVR.Core.Graphics
{
    /// <summary>Minimal raw-vtable Direct3D 11 / DXGI interop.</summary>
    public static unsafe class D3D11
    {
        private static IntPtr* Vtbl(IntPtr obj) => *(IntPtr**)obj;

        private static readonly Guid IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

        public static uint AddRef(IntPtr unknown)
        {
            if (unknown == IntPtr.Zero) return 0;
            return ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(unknown)[1])(unknown);
        }

        public static uint Release(IntPtr unknown)
        {
            if (unknown == IntPtr.Zero) return 0;
            return ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(unknown)[2])(unknown);
        }

        public static IntPtr GetDevice(IntPtr deviceChild)
        {
            if (deviceChild == IntPtr.Zero) return IntPtr.Zero;
            IntPtr dev = IntPtr.Zero;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Vtbl(deviceChild)[3])(deviceChild, &dev);
            return dev;
        }

        public static IntPtr GetImmediateContext(IntPtr device)
        {
            if (device == IntPtr.Zero) return IntPtr.Zero;
            IntPtr ctx = IntPtr.Zero;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Vtbl(device)[40])(device, &ctx);
            return ctx;
        }

        public static int GetFeatureLevel(IntPtr device)
        {
            if (device == IntPtr.Zero) return 0;
            return ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(device)[37])(device);
        }

        public static void CopyResource(IntPtr context, IntPtr dst, IntPtr src)
        {
            if (context == IntPtr.Zero || dst == IntPtr.Zero || src == IntPtr.Zero) return;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Vtbl(context)[47])(context, dst, src);
        }

        /// <summary>ID3D11Device::CreateTexture2D (vtbl 5) without initial data. Returns 0 on failure.</summary>
        public static IntPtr CreateTexture2D(IntPtr device, in D3D11Texture2DDesc desc)
        {
            if (device == IntPtr.Zero) return IntPtr.Zero;
            IntPtr texture = IntPtr.Zero;
            D3D11Texture2DDesc copy = desc;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, D3D11Texture2DDesc*, void*, IntPtr*, int>)Vtbl(device)[5])(device, &copy, null, &texture);
            return hr < 0 ? IntPtr.Zero : texture;
        }

        /// <summary>ID3D11DeviceContext::Map (vtbl 14), D3D11_MAP_READ, subresource 0. Returns null on failure.</summary>
        public static byte* MapRead(IntPtr context, IntPtr resource, out uint rowPitch)
        {
            rowPitch = 0;
            if (context == IntPtr.Zero || resource == IntPtr.Zero) return null;
            // D3D11_MAPPED_SUBRESOURCE { void* pData; UINT RowPitch; UINT DepthPitch; }
            byte* mapped = stackalloc byte[16];
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, void*, int>)Vtbl(context)[14])(context, resource, 0, 1, 0, mapped);
            if (hr < 0) return null;
            rowPitch = *(uint*)(mapped + 8);
            return *(byte**)mapped;
        }

        /// <summary>ID3D11DeviceContext::Unmap (vtbl 15), subresource 0.</summary>
        public static void Unmap(IntPtr context, IntPtr resource)
        {
            if (context == IntPtr.Zero || resource == IntPtr.Zero) return;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Vtbl(context)[15])(context, resource, 0);
        }

        public static D3D11Texture2DDesc GetDesc(IntPtr texture2D)
        {
            D3D11Texture2DDesc desc = default;
            if (texture2D == IntPtr.Zero) return desc;
            ((delegate* unmanaged[Stdcall]<IntPtr, D3D11Texture2DDesc*, void>)Vtbl(texture2D)[10])(texture2D, &desc);
            return desc;
        }

        public static bool TryGetAdapterLuid(IntPtr device, out ulong luid)
        {
            luid = 0;
            if (device == IntPtr.Zero) return false;
            IntPtr dxgiDevice = IntPtr.Zero, adapter = IntPtr.Zero;
            try
            {
                Guid iid = IID_IDXGIDevice;
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Vtbl(device)[0])(device, &iid, &dxgiDevice);
                if (hr < 0 || dxgiDevice == IntPtr.Zero) return false;

                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(dxgiDevice)[7])(dxgiDevice, &adapter);
                if (hr < 0 || adapter == IntPtr.Zero) return false;

                // DXGI_ADAPTER_DESC is 304 bytes on x64; LUID at offset 296. Use a larger buffer for safety.
                byte* buf = stackalloc byte[512];
                new Span<byte>(buf, 512).Clear();
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)Vtbl(adapter)[8])(adapter, buf);
                if (hr < 0) return false;

                uint low = *(uint*)(buf + 296);
                uint high = *(uint*)(buf + 300);
                luid = (ulong)low | ((ulong)high << 32);
                return true;
            }
            finally
            {
                if (adapter != IntPtr.Zero) Release(adapter);
                if (dxgiDevice != IntPtr.Zero) Release(dxgiDevice);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
    }

    public static class D3D11Usage
    {
        public const uint Default = 0, Staging = 3;
        public const uint CpuAccessRead = 0x20000;
    }

    public static class DxgiFormat
    {
        public const uint R16G16B16A16_FLOAT = 10, R8G8B8A8_TYPELESS = 27, R8G8B8A8_UNORM = 28, R8G8B8A8_UNORM_SRGB = 29,
                          B8G8R8A8_UNORM = 87, B8G8R8A8_TYPELESS = 90, B8G8R8A8_UNORM_SRGB = 91;

        public static bool IsSrgb(uint format) => format == R8G8B8A8_UNORM_SRGB || format == B8G8R8A8_UNORM_SRGB;

        public static uint Typeless(uint format)
        {
            switch (format)
            {
                case R8G8B8A8_TYPELESS: case R8G8B8A8_UNORM: case R8G8B8A8_UNORM_SRGB: return R8G8B8A8_TYPELESS;
                case B8G8R8A8_TYPELESS: case B8G8R8A8_UNORM: case B8G8R8A8_UNORM_SRGB: return B8G8R8A8_TYPELESS;
                default: return format;
            }
        }

        public static bool AreCopyCompatible(uint a, uint b) => Typeless(a) == Typeless(b);
    }
}
