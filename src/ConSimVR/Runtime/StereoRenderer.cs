using System;
using ConSimVR.Core.Math;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace ConSimVR
{
    /// <summary>
    /// Renders the game camera once per eye through HDRP's XR pass system. The game's HDRP build has the XR SDK path
    /// compiled out, but XRPass/XRSystem survive: <see cref="HdrpPatches"/> takes over XRSystem.SetupFrame while a
    /// VR frame is active and gives the game camera two multi-pass XR passes (one per eye) with our view/projection
    /// matrices and our eye RenderTextures as final targets. The game camera's transform is never moved; head tracking
    /// only exists in the eye matrices.
    /// </summary>
    internal sealed class StereoRenderer
    {
        private readonly VrSession _session;
        private readonly CameraVrSettings _cameraSettings = new();
        private readonly Matrix4x4[] _view = new Matrix4x4[2];
        private readonly Matrix4x4[] _projection = new Matrix4x4[2];
        private readonly ScriptableCullingParameters[] _culling = new ScriptableCullingParameters[2];

        private Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>> _onBegin;
        private Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>> _onEnd;
        private CommandBuffer _desktop;
        private bool _attached;

        private FrameRecord _frame;
        private Camera _camera;
        private bool _renderEyes;
        private bool _sharedCulling;
        private float _savedFieldOfView;
        private int _layoutFailures;

        /// <summary>The attached renderer, if any (read by the HDRP SetupFrame patch).</summary>
        public static StereoRenderer Active { get; private set; }

        public StereoRenderer(VrSession session) => _session = session;

        /// <summary>A VR frame is in progress, so XRSystem.SetupFrame must be handled by <see cref="TryAddPasses"/>.</summary>
        public bool HasFrame => _frame != null;

        public void Attach()
        {
            if (_attached)
                return;
            _onBegin = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>>>(
                new Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>>(OnBeginFrameRendering));
            _onEnd = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>>>(
                new Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>>(OnEndFrameRendering));
            _desktop = new CommandBuffer { name = "ConSimVR desktop" };

            RenderPipelineManager.add_beginFrameRendering(_onBegin);
            // The event's add/remove accessors for endFrameRendering were stripped from this build; the backing field survived.
            RenderPipelineManager.endFrameRendering = Combine(RenderPipelineManager.endFrameRendering, _onEnd);
            Active = this;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached)
                return;
            RenderPipelineManager.remove_beginFrameRendering(_onBegin);
            RenderPipelineManager.endFrameRendering = Remove(RenderPipelineManager.endFrameRendering, _onEnd);
            if (Active == this)
                Active = null;
            _cameraSettings.Restore();
            _desktop?.Release();
            _desktop = null;
            _attached = false;
            _frame = null;
        }

        private static Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>> Combine(
            Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>> a,
            Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>> b) =>
            Il2CppSystem.Delegate.Combine(a, b)?.TryCast<Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>>>();

        private static Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>> Remove(
            Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>> a,
            Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>> b) =>
            Il2CppSystem.Delegate.Remove(a, b)?.TryCast<Il2CppSystem.Action<ScriptableRenderContext, Il2CppReferenceArray<Camera>>>();

        private static bool Contains(Il2CppReferenceArray<Camera> cameras, Camera camera)
        {
            if (cameras == null || camera == null)
                return false;
            for (int i = 0; i < cameras.Length; i++)
            {
                var c = cameras[i];
                if (c != null && c.Pointer == camera.Pointer)
                    return true;
            }
            return false;
        }

        private void OnBeginFrameRendering(ScriptableRenderContext context, Il2CppReferenceArray<Camera> cameras)
        {
            try
            {
                var camera = GameState.MainCamera;
                if (_frame != null || !Contains(cameras, camera))
                    return;

                var frame = _session.BeginStereoFrame(camera);
                if (frame == null)
                    return;

                _cameraSettings.Apply(camera);
                _frame = frame;
                _camera = camera;
                _savedFieldOfView = camera.fieldOfView;
                _renderEyes = false;
                _renderEyes = frame.ShouldRender && frame.HasEyes && PrepareEyes(camera, frame);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Stereo frame setup failed: {e}");
            }
        }

        private bool PrepareEyes(Camera camera, FrameRecord frame)
        {
            float near = camera.nearClipPlane, far = camera.farClipPlane;
            for (int eye = 0; eye < 2; eye++)
            {
                _view[eye] = VrProjection.WorldToCamera(frame.EyeWorld[eye]).ToUnity();
                _projection[eye] = VrProjection.Projection(frame.EyeFov[eye], near, far).ToUnity();
            }

            _sharedCulling = VrConfig.Culling.Value == CullingMode.Shared
                && CullingFrustum.TryCombine(frame.EyeWorld[0], frame.EyeFov[0], frame.EyeWorld[1], frame.EyeFov[1], near, far,
                    out var cullPose, out var cullProjection, out _, out _)
                && TryCull(camera, VrProjection.WorldToCamera(cullPose).ToUnity(), cullProjection.ToUnity(), out _culling[0]);

            if (_sharedCulling)
                return true;
            return TryCull(camera, _view[0], _projection[0], out _culling[0])
                && TryCull(camera, _view[1], _projection[1], out _culling[1]);
        }

        /// <summary>Culling parameters for an arbitrary frustum, taken from the camera with temporary matrix overrides.</summary>
        private static bool TryCull(Camera camera, Matrix4x4 view, Matrix4x4 projection, out ScriptableCullingParameters culling)
        {
            camera.worldToCameraMatrix = view;
            camera.projectionMatrix = projection;
            bool ok = camera.TryGetCullingParameters(false, out culling);
            camera.ResetWorldToCameraMatrix();
            camera.ResetProjectionMatrix();
            if (ok)
            {
                // HDRP derives the camera FOV (shadow culling) and XR-combined matrices from these.
                culling.stereoViewMatrix = view;
                culling.stereoProjectionMatrix = projection;
            }
            return ok;
        }

        /// <summary>
        /// Called from the XRSystem.SetupFrame patch for each camera HDRP renders this frame. Returns true when the camera
        /// is the stereo camera (its eye passes, or none when the frame is not rendered, have been added).
        /// </summary>
        public bool TryAddPasses(XRSystem xrSystem, Camera camera)
        {
            if (_frame == null || camera == null || camera.Pointer != _camera.Pointer)
                return false;
            if (!_renderEyes)
                return true; // claim the camera with no passes: nothing is rendered for it this frame
            try
            {
                var viewport = new Rect(0, 0, _session.EyeWidth, _session.EyeHeight);
                for (int eye = 0; eye < 2; eye++)
                {
                    var pass = XRPass.Create(new XRPassCreateInfo
                    {
                        multipassId = eye,
                        cullingPassId = _sharedCulling ? 0 : eye,
                        renderTarget = _session.GetEyeTexture(eye),
                        cullingParameters = _culling[_sharedCulling ? 0 : eye],
                        customMirrorView = null,
                    });
                    pass.AddView(_projection[eye], _view[eye], viewport, -1);
                    xrSystem.AddPassToFrame(camera, pass);
                }
            }
            catch (Exception e)
            {
                if (_layoutFailures++ < 5)
                    Plugin.Log.LogError($"Adding eye passes failed: {e}");
            }
            return true;
        }

        private void OnEndFrameRendering(ScriptableRenderContext context, Il2CppReferenceArray<Camera> cameras)
        {
            if (_frame == null || !Contains(cameras, _camera))
                return;
            var frame = _frame;
            _frame = null;
            try
            {
                // HDRP synced the camera FOV to the eye projection for shadow culling; give the game its own value back.
                _camera.fieldOfView = _savedFieldOfView;
                _session.EndStereoFrame(frame);
                DrawDesktop(context);
                _session.AfterStereoRender();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Stereo frame end failed: {e}");
            }
        }

        /// <summary>
        /// Safety net called at the end of every Unity frame: if HDRP never reached endFrameRendering (an exception
        /// mid-render), end the OpenXR frame anyway. A waited-but-unended frame would block the next xrWaitFrame.
        /// </summary>
        public void FlushPendingFrame()
        {
            var frame = _frame;
            if (frame == null)
                return;
            _frame = null;
            if (GameState.IsAlive(_camera))
                _camera.fieldOfView = _savedFieldOfView;
            _session.EndStereoFrame(frame);
        }

        /// <summary>
        /// The eyes render offscreen, so the window's back buffer is ours. With the UI panel it is cleared to transparent
        /// black so the screen-space UI drawn on top can be captured for the headset; otherwise it mirrors the left eye.
        /// </summary>
        private void DrawDesktop(ScriptableRenderContext context)
        {
            var target = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            _desktop.Clear();
            if (VrConfig.UiPanel.Value == UiPanelMode.Panel)
            {
                _desktop.SetRenderTarget(target);
                _desktop.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));
            }
            else
            {
                // Crop the eye vertically so it fills the window width without stretching.
                float eyeAspect = (float)_session.EyeWidth / _session.EyeHeight;
                float screenAspect = (float)Screen.width / Math.Max(1, Screen.height);
                float scaleY = Math.Min(1f, eyeAspect / screenAspect);
                _desktop.Blit(_session.LeftEyeTexture, target, new Vector2(1f, scaleY), new Vector2(0f, (1f - scaleY) * 0.5f));
            }
            context.ExecuteCommandBuffer(_desktop);
            context.Submit();
        }
    }
}
