using System;
using ConSimVR.Core.OpenXR;
using UnityEngine;
using CorePose = ConSimVR.Core.Math.Pose;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace ConSimVR
{
    /// <summary>
    /// Shows the game's screen-space UI in the headset. The UI is captured from the desktop back buffer each frame
    /// (StereoRenderer clears it to transparent black first) and submitted as an OpenXR quad layer floating in front of
    /// the recentred seat. Mouse interaction keeps working because the UI is still the desktop screen.
    /// </summary>
    internal sealed class UiOverlay
    {
        private readonly VrSession _session;
        private readonly UiCursor _cursor = new();
        private NativeRenderTexture _texture;
        private int _width, _height;
        private int _capturedFrames;
        private bool _visible = true;
        private RenderTextureFormat _format;
        private bool _enabled;
        private int _pendingWidth, _pendingHeight, _settledFrames;

        public UiOverlay(VrSession session) => _session = session;

        public XrSwapchainInfo Swapchain { get; private set; }
        public int Width => _width;
        public int Height => _height;

        /// <summary>The panel is enabled, visible and has a capture texture.</summary>
        public bool IsShowing => _enabled && _visible && _texture != null;
        public IntPtr Native => _texture?.Native ?? IntPtr.Zero;

        public bool Initialize(RenderTextureFormat format, out string error)
        {
            _format = format;
            _enabled = true;
            if (VrConfig.ShowCursor.Value)
                _cursor.Create();
            return Allocate(Screen.width, Screen.height, out error);
        }

        private bool Allocate(int width, int height, out string error)
        {
            _width = width;
            _height = height;
            _texture = _session.CreateTexture("ConSimVR UI", _width, _height, _format, out error);
            if (_texture == null)
                return false;
            Swapchain = _session.CreateSwapchain(_width, _height, out error);
            if (Swapchain == null)
            {
                Destroy();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Recreates the capture texture and swapchain at the new window size. The old ones are detached first so frames
        /// still queued on the render thread skip the UI layer, and released only after the render thread has drained.
        /// </summary>
        private void Resize(int width, int height)
        {
            var oldSwapchain = Swapchain;
            var oldTexture = _texture;
            Swapchain = null;
            _texture = null;
            _capturedFrames = 0;
            RenderThread.WaitForFence(TimeSpan.FromMilliseconds(500));
            _session.DestroySwapchain(oldSwapchain);
            oldTexture?.Destroy();

            if (Allocate(width, height, out string error))
                Plugin.Log.LogInfo($"UI panel resized to {width}x{height}.");
            else
                Plugin.Log.LogWarning($"UI panel unavailable after resize: {error}");
        }

        public void ToggleVisible() => _visible = !_visible;

        /// <summary>Schedules this frame's capture; it happens at the end of the frame, after the UI is drawn.</summary>
        public void LateUpdate()
        {
            if (!_enabled)
                return;
            int width = Screen.width, height = Screen.height;
            if (width != _width || height != _height)
            {
                // Wait until the window size has settled (dragging a window edge changes it every frame).
                _capturedFrames = 0;
                if (width != _pendingWidth || height != _pendingHeight)
                {
                    _pendingWidth = width;
                    _pendingHeight = height;
                    _settledFrames = 0;
                }
                if (++_settledFrames < 15 || width <= 0 || height <= 0)
                    return;
                Resize(width, height);
            }
            bool capturing = _texture != null && _visible && _session.IsRunning && _texture.EnsureValid();
            _cursor.Update(capturing);
            if (!capturing)
            {
                _capturedFrames = 0;
                return;
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_texture.Texture);
            _capturedFrames++;
        }

        /// <summary>The quad layer for the current frame, positioned relative to the tracking-space recenter reference.</summary>
        public bool TryGetLayer(CorePose reference, out QuadLayer layer)
        {
            layer = default;
            // The frame being submitted copies the previous frame's capture, so wait for one complete capture.
            if (Swapchain == null || !_visible || _capturedFrames < 2)
                return false;

            float width = VrConfig.UiWidth.Value;
            var offset = new CorePose(new NVector3(0f, VrConfig.UiHeightOffset.Value, VrConfig.UiDistance.Value), NQuaternion.Identity);
            layer = new QuadLayer
            {
                Swapchain = Swapchain.Handle,
                Width = _width,
                Height = _height,
                Pose = VrSession.ToOpenXr(reference * offset),
                WidthMeters = width,
                HeightMeters = width * _height / _width,
                BlendAlpha = true,
                UnpremultipliedAlpha = false,
            };
            return true;
        }

        public void Destroy()
        {
            _cursor.Destroy();
            _texture?.Destroy();
            _texture = null;
            Swapchain = null; // destroyed with the OpenXR session
        }
    }
}
