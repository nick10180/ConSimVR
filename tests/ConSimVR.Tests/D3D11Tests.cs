using System;
using System.Runtime.InteropServices;
using ConSimVR.Core.Graphics;
using Xunit;

namespace ConSimVR.Tests
{
    public unsafe class D3D11Tests
    {
        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags,
            uint* featureLevels, uint numLevels, uint sdkVersion, IntPtr* device, uint* featureLevel, IntPtr* context);

        [StructLayout(LayoutKind.Sequential)]
        private struct SubresourceData { public IntPtr pSysMem; public uint SysMemPitch, SysMemSlicePitch; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Mapped { public IntPtr pData; public uint RowPitch, DepthPitch; }

        private static IntPtr CreateDevice()
        {
            IntPtr dev = IntPtr.Zero, ctx = IntPtr.Zero; uint fl;
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0, null, 0, 7, &dev, &fl, &ctx);
            if (hr < 0)
            {
                dev = IntPtr.Zero; ctx = IntPtr.Zero;
                hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0, null, 0, 7, &dev, &fl, &ctx);
            }
            Assert.True(hr >= 0, "D3D11CreateDevice failed 0x" + hr.ToString("x"));
            D3D11.Release(ctx);
            return dev;
        }

        private static IntPtr CreateTex(IntPtr dev, uint format, uint usage, uint bind, uint cpu, SubresourceData* init)
        {
            D3D11Texture2DDesc d = new D3D11Texture2DDesc
            {
                Width = 64, Height = 64, MipLevels = 1, ArraySize = 1, Format = format, SampleCount = 1,
                Usage = usage, BindFlags = bind, CPUAccessFlags = cpu
            };
            IntPtr tex = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, D3D11Texture2DDesc*, SubresourceData*, IntPtr*, int>)(*(IntPtr**)dev)[5])(dev, &d, init, &tex);
            Assert.True(hr >= 0, "CreateTexture2D failed 0x" + hr.ToString("x"));
            return tex;
        }

        [Fact]
        public void VtableCallsWorkOnRealDevice()
        {
            IntPtr dev = CreateDevice();
            IntPtr a = IntPtr.Zero, b = IntPtr.Zero, c = IntPtr.Zero, ctx = IntPtr.Zero;
            try
            {
                Assert.True(D3D11.GetFeatureLevel(dev) >= 0xb000);

                byte[] pattern = new byte[64 * 64 * 4];
                for (int i = 0; i < pattern.Length; i++) pattern[i] = (byte)((i * 7 + 3) & 0xff);
                fixed (byte* p = pattern)
                {
                    SubresourceData init = new SubresourceData { pSysMem = (IntPtr)p, SysMemPitch = 64 * 4 };
                    a = CreateTex(dev, DxgiFormat.R8G8B8A8_UNORM, 0, 8, 0, &init);
                }
                b = CreateTex(dev, DxgiFormat.R8G8B8A8_UNORM_SRGB, 0, 8, 0, null);
                c = CreateTex(dev, DxgiFormat.R8G8B8A8_UNORM, 3, 0, 0x20000, null);

                IntPtr d = D3D11.GetDevice(a);
                Assert.Equal(dev, d);
                D3D11.Release(d);

                D3D11Texture2DDesc desc = D3D11.GetDesc(b);
                Assert.Equal(64u, desc.Width);
                Assert.Equal(64u, desc.Height);
                Assert.Equal(29u, desc.Format);

                ctx = D3D11.GetImmediateContext(dev);
                Assert.NotEqual(IntPtr.Zero, ctx);
                D3D11.CopyResource(ctx, b, a);
                D3D11.CopyResource(ctx, c, b);

                Mapped m;
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, Mapped*, int>)(*(IntPtr**)ctx)[14])(ctx, c, 0, 1, 0, &m);
                Assert.True(hr >= 0);
                try
                {
                    for (int y = 0; y < 64; y++)
                        for (int x = 0; x < 256; x++)
                            Assert.Equal(pattern[y * 256 + x], ((byte*)m.pData)[y * m.RowPitch + x]);
                }
                finally
                {
                    ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)(*(IntPtr**)ctx)[15])(ctx, c, 0);
                }

                Assert.True(D3D11.TryGetAdapterLuid(dev, out ulong luid));
                Assert.NotEqual(0UL, luid);
            }
            finally
            {
                D3D11.Release(ctx); D3D11.Release(a); D3D11.Release(b); D3D11.Release(c); D3D11.Release(dev);
            }
        }

        [Fact]
        public void FormatHelpers()
        {
            Assert.True(DxgiFormat.AreCopyCompatible(29, 27));
            Assert.False(DxgiFormat.AreCopyCompatible(29, 91));
            Assert.True(DxgiFormat.IsSrgb(29));
            Assert.Equal(90u, DxgiFormat.Typeless(87));
        }
    }
}
