using System;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace ConSimVR
{
    /// <summary>
    /// Turns off HDRP effects that are wrong or uncomfortable in a headset (motion blur from head motion, depth of
    /// field, lens distortion, chromatic aberration, panini projection) on the stereo camera through HDRP's per-camera
    /// custom frame settings, and restores the camera's own settings afterwards.
    /// </summary>
    internal sealed class CameraVrSettings
    {
        private static readonly FrameSettingsField[] s_disabled =
        {
            FrameSettingsField.MotionBlur,
            FrameSettingsField.DepthOfField,
            FrameSettingsField.LensDistortion,
            FrameSettingsField.ChromaticAberration,
            FrameSettingsField.PaniniProjection,
        };

        private HDAdditionalCameraData _data;
        private IntPtr _camera;
        private bool _savedCustom;
        private FrameSettingsOverrideMask _savedMask;
        private FrameSettings _savedSettings;

        /// <summary>Applies to <paramref name="camera"/> (restoring any previous camera first). Cheap when unchanged.</summary>
        public void Apply(Camera camera)
        {
            if (!VrConfig.DisableUncomfortableEffects.Value || camera == null || camera.Pointer == _camera)
                return;
            Restore();
            _camera = camera.Pointer;
            try
            {
                var data = camera.GetComponent<HDAdditionalCameraData>();
                if (!GameState.IsAlive(data))
                    return;
                _savedCustom = data.customRenderingSettings;
                _savedMask = data.renderingPathCustomFrameSettingsOverrideMask;
                ref FrameSettings settings = ref data.renderingPathCustomFrameSettings;
                _savedSettings = settings;

                var mask = _savedMask;
                foreach (var field in s_disabled)
                {
                    mask.mask[(uint)field] = true;
                    settings.SetEnabled(field, false);
                }
                data.renderingPathCustomFrameSettingsOverrideMask = mask;
                data.customRenderingSettings = true;
                _data = data;
                Plugin.Log.LogInfo("Disabled motion blur, depth of field, lens distortion, chromatic aberration and panini projection on the VR camera.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not change the camera's HDRP frame settings: {e.Message}");
            }
        }

        public void Restore()
        {
            if (GameState.IsAlive(_data))
            {
                try
                {
                    ref FrameSettings settings = ref _data.renderingPathCustomFrameSettings;
                    settings = _savedSettings;
                    _data.renderingPathCustomFrameSettingsOverrideMask = _savedMask;
                    _data.customRenderingSettings = _savedCustom;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Could not restore the camera's HDRP frame settings: {e.Message}");
                }
            }
            _data = null;
            _camera = IntPtr.Zero;
        }
    }
}
