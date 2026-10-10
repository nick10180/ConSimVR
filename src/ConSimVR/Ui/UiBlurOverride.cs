using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Wb.VisualFx;

namespace ConSimVR
{
    /// <summary>
    /// The game's HUD panels show a blurred copy of the camera image behind them: WbPostProcessingBlurCustomPass renders
    /// it during the camera render and publishes it as the global texture <c>_BlurTexture</c>, which the overlay UI samples.
    /// In VR that copy comes from one eye's render while the panel floats in front of the player, so it reads as the world
    /// from the wrong angle. While VR runs, the pass is switched off (saving two full-resolution blurs per frame) and a
    /// flat-colour texture is bound as <c>_BlurTexture</c> after each 3D render, before the overlay UI draws.
    /// </summary>
    internal sealed class UiBlurOverride
    {
        private static readonly int BlurTextureId = Shader.PropertyToID("_BlurTexture");

        private readonly Dictionary<IntPtr, (WbPostProcessingBlurCustomPass Pass, bool Enabled)> _disabled = new();
        private Texture2D _flat;
        private Color _flatColor;
        private float _nextScan;

        /// <summary>Main thread; rescans every few seconds because scenes bring their own custom pass volumes.</summary>
        public void Update()
        {
            if (!VrConfig.FlatUiBackgrounds.Value || Time.realtimeSinceStartup < _nextScan)
                return;
            _nextScan = Time.realtimeSinceStartup + 3f;
            try
            {
                foreach (var volume in UnityEngine.Object.FindObjectsOfType<CustomPassVolume>())
                {
                    var passes = volume != null ? volume.customPasses : null;
                    if (passes == null)
                        continue;
                    for (int i = 0; i < passes.Count; i++)
                    {
                        var pass = passes[i]?.TryCast<WbPostProcessingBlurCustomPass>();
                        if (pass == null || _disabled.ContainsKey(pass.Pointer))
                            continue;
                        _disabled[pass.Pointer] = (pass, pass.enabled);
                        pass.enabled = false;
                        Plugin.Log.LogInfo($"HUD background blur pass on '{volume.name}' disabled for VR; panels get a flat background.");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not disable the HUD background blur: {e.Message}");
                _nextScan = float.MaxValue;
            }
        }

        /// <summary>Main thread, after the 3D render and before the overlay UI: bind the flat background texture.</summary>
        public void BindFlatBackground()
        {
            if (!VrConfig.FlatUiBackgrounds.Value)
                return;
            var color = VrConfig.UiBackgroundColor.Value;
            if (_flat == null || _flat.WasCollected || color != _flatColor)
            {
                if (_flat == null || _flat.WasCollected)
                    _flat = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        _flat.SetPixel(x, y, color);
                _flat.Apply(false, false);
                _flatColor = color;
            }
            Shader.SetGlobalTexture(BlurTextureId, _flat);
        }

        public void Restore()
        {
            foreach (var (pass, enabled) in _disabled.Values)
            {
                try
                {
                    if (pass != null && !pass.WasCollected)
                        pass.enabled = enabled;
                }
                catch (Exception)
                {
                    // The pass went away with its scene.
                }
            }
            _disabled.Clear();
            _nextScan = 0f;
            if (_flat != null && !_flat.WasCollected)
                UnityEngine.Object.Destroy(_flat);
            _flat = null;
        }
    }
}
