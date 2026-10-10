using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace ConSimVR
{
    /// <summary>
    /// Fixes for the game's ghosting of bright edges and reflections under fast camera motion, in VR and flat play:
    /// HDRP 10's TAA only rejects stale history (motion vector rejection, anti-ringing) at High quality, and its
    /// anti-flicker keeps bright history alive; SSR in PBR Accumulation mode averages reflections over frames. The game's
    /// original values are logged once so the cause can be identified.
    /// </summary>
    internal sealed class RenderQualityTweaks
    {
        private readonly HashSet<IntPtr> _loggedCameras = new();
        private readonly HashSet<IntPtr> _changedProfiles = new();
        private float _nextCheck;

        /// <summary>Main thread, every frame; does its work every two seconds (the game re-applies settings on loads).</summary>
        public void Update()
        {
            if (Time.realtimeSinceStartup < _nextCheck)
                return;
            _nextCheck = Time.realtimeSinceStartup + 2f;
            try
            {
                TuneTaa(GameState.MainCamera);
                DisableSsrAccumulation();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Render quality tweaks failed and are turned off: {e.Message}");
                _nextCheck = float.MaxValue;
            }
        }

        private void TuneTaa(Camera camera)
        {
            if (camera == null)
                return;
            var data = camera.GetComponent<HDAdditionalCameraData>();
            if (!GameState.IsAlive(data))
                return;

            if (_loggedCameras.Add(camera.Pointer))
                Plugin.Log.LogInfo($"Game camera anti-aliasing: {data.antialiasing}, TAA quality {data.TAAQuality}, motion vector rejection {data.taaMotionVectorRejection:0.##}, " +
                                   $"anti-flicker {data.taaAntiFlicker:0.##}, anti-ringing {data.taaAntiHistoryRinging}, history sharpening {data.taaHistorySharpening:0.##}, sharpen {data.taaSharpenStrength:0.##}");

            if (!VrConfig.ReduceTaaGhosting.Value || data.antialiasing != HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing)
                return;
            bool changed = false;
            if (data.TAAQuality != HDAdditionalCameraData.TAAQualityLevel.High)
            {
                data.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
                changed = true;
            }
            if (data.taaMotionVectorRejection < 1f)
            {
                data.taaMotionVectorRejection = 1f;
                changed = true;
            }
            if (data.taaAntiFlicker > 0f)
            {
                data.taaAntiFlicker = 0f;
                changed = true;
            }
            if (!data.taaAntiHistoryRinging)
            {
                data.taaAntiHistoryRinging = true;
                changed = true;
            }
            if (changed)
                Plugin.Log.LogInfo("TAA set to High quality with full motion vector rejection, anti-ringing on and anti-flicker off (Rendering.ReduceTaaGhosting).");
        }

        private void DisableSsrAccumulation()
        {
            if (!VrConfig.DisableSsrAccumulation.Value)
                return;
            foreach (var volume in UnityEngine.Object.FindObjectsOfType<Volume>())
            {
                var profile = volume != null ? volume.sharedProfile : null;
                if (profile == null || _changedProfiles.Contains(profile.Pointer))
                    continue;
                var components = profile.components;
                for (int i = 0; i < components.Count; i++)
                {
                    var ssr = components[i]?.TryCast<ScreenSpaceReflection>();
                    if (ssr == null)
                        continue;
                    var algorithm = ssr.usedAlgorithm;
                    Plugin.Log.LogInfo($"SSR on volume '{volume.name}': enabled {ssr.enabled.value} (override {ssr.enabled.overrideState}), " +
                                       $"algorithm {algorithm.value} (override {algorithm.overrideState}), accumulation {ssr.accumulationFactor.value:0.##}");
                    if (algorithm.value == ScreenSpaceReflectionAlgorithm.PBRAccumulation)
                    {
                        algorithm.value = ScreenSpaceReflectionAlgorithm.Approximation;
                        algorithm.overrideState = true;
                        Plugin.Log.LogInfo($"SSR on volume '{volume.name}' switched from PBR Accumulation to Approximation (Rendering.DisableSsrAccumulation).");
                    }
                }
                _changedProfiles.Add(profile.Pointer);
            }
        }
    }
}
