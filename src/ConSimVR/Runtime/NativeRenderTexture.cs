using System;
using System.Collections.Generic;
using ConSimVR.Core.Graphics;
using UnityEngine;

namespace ConSimVR
{
    /// <summary>
    /// A RenderTexture whose D3D11 texture the render thread copies from. Survives scene loads
    /// (Resources.UnloadUnusedAssets destroys textures without DontUnloadUnusedAsset) and holds its own COM reference on
    /// the native texture, so the render thread never copies from memory Unity has freed. If Unity still loses the
    /// texture (device reset, external release), <see cref="EnsureValid"/> recreates it and swaps in the new pointer.
    /// </summary>
    internal sealed class NativeRenderTexture
    {
        private readonly string _name;
        private readonly int _width, _height;
        private readonly RenderTextureFormat _format;
        private readonly uint _requiredDxgiFamily;
        private readonly List<IntPtr> _retired = new();

        private NativeRenderTexture(string name, int width, int height, RenderTextureFormat format, uint dxgiFormat)
        {
            _name = name;
            _width = width;
            _height = height;
            _format = format;
            _requiredDxgiFamily = dxgiFormat;
        }

        public RenderTexture Texture { get; private set; }

        /// <summary>ID3D11Texture2D* with a reference held by us. Read by the render thread.</summary>
        public IntPtr Native { get; private set; }

        public static NativeRenderTexture Create(string name, int width, int height, RenderTextureFormat format, uint swapchainDxgiFormat, out string error)
        {
            var texture = new NativeRenderTexture(name, width, height, format, swapchainDxgiFormat);
            if (!texture.Allocate(out error))
            {
                texture.Destroy();
                return null;
            }
            return texture;
        }

        private bool Allocate(out string error)
        {
            Texture = new RenderTexture(_width, _height, 0, _format, RenderTextureReadWrite.sRGB)
            {
                name = _name,
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
            };
            Texture.Create();
            return AdoptNativePointer(out error);
        }

        private bool AdoptNativePointer(out string error)
        {
            IntPtr native = Texture.GetNativeTexturePtr();
            if (native == IntPtr.Zero)
            {
                error = $"{_name}: no native texture.";
                return false;
            }
            var desc = D3D11.GetDesc(native);
            if (desc.Width != _width || desc.Height != _height || !DxgiFormat.AreCopyCompatible(desc.Format, _requiredDxgiFamily))
            {
                error = $"{_name}: D3D11 texture {desc.Width}x{desc.Height} format {desc.Format} cannot be copied into a {_width}x{_height} format {_requiredDxgiFamily} swapchain.";
                return false;
            }
            if (native != Native)
            {
                D3D11.AddRef(native);
                if (Native != IntPtr.Zero)
                    _retired.Add(Native); // a queued render-thread copy may still use it; released in Destroy
                Native = native;
            }
            error = null;
            return true;
        }

        /// <summary>Main thread, once per frame: recreates the texture if Unity lost it. Returns false if unusable.</summary>
        public bool EnsureValid()
        {
            string error;
            if (!GameState.IsAlive(Texture))
            {
                Plugin.Log.LogWarning($"{_name} was destroyed; recreating it.");
                if (Allocate(out error))
                    return true;
            }
            else if (!Texture.IsCreated())
            {
                Plugin.Log.LogWarning($"{_name} lost its GPU texture; recreating it.");
                Texture.Create();
                if (AdoptNativePointer(out error))
                    return true;
            }
            else
            {
                return true;
            }
            Plugin.Log.LogError(error);
            return false;
        }

        /// <summary>Releases everything. Call only after the render thread has drained.</summary>
        public void Destroy()
        {
            if (GameState.IsAlive(Texture))
            {
                Texture.Release();
                UnityEngine.Object.Destroy(Texture);
            }
            Texture = null;
            foreach (var retired in _retired)
                D3D11.Release(retired);
            _retired.Clear();
            if (Native != IntPtr.Zero)
                D3D11.Release(Native);
            Native = IntPtr.Zero;
        }
    }
}
