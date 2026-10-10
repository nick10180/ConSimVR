using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using PassList = Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<UnityEngine.Camera, UnityEngine.Rendering.HighDefinition.XRPass>>;

namespace ConSimVR
{
    /// <summary>
    /// Takes over HDRP's XRSystem.SetupFrame while a VR frame is active.
    ///
    /// HDRP's intended hook for this is XRSystem.SetCustomLayout, but its delegate receives the XRLayout struct by value
    /// and Il2CppInterop's delegate trampolines wrap non-blittable struct arguments as if they were boxed objects, which
    /// would read garbage. With the XR SDK branch compiled out of this build, SetupFrame is only: release last frame's
    /// passes, then give each camera its layout passes or the empty (non-XR) pass. This prefix does exactly that, calling
    /// XRPass.Create / AddView / AddPassToFrame from managed code, a direction Il2CppInterop marshals correctly.
    /// </summary>
    [HarmonyPatch]
    internal static class HdrpPatches
    {
        private static int s_failures;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(XRSystem), nameof(XRSystem.SetupFrame))]
        private static bool SetupFrame(XRSystem __instance, Il2CppReferenceArray<Camera> cameras, ref PassList __result)
        {
            var renderer = StereoRenderer.Active;
            if (renderer == null || !renderer.HasFrame || cameras == null)
                return true;

            try
            {
                var passes = __instance.framePasses;
                if (passes.Count > 0)
                    __instance.ReleaseFrame();

                for (int i = 0; i < cameras.Length; i++)
                {
                    var camera = cameras[i];
                    if (camera == null)
                        continue;
                    if (!renderer.TryAddPasses(__instance, camera))
                        __instance.AddPassToFrame(camera, __instance.emptyPass);
                }

                __result = passes;
                return false;
            }
            catch (Exception e)
            {
                // Let HDRP's own SetupFrame run (it releases anything added above): a plain non-VR frame.
                if (s_failures++ < 5)
                    Plugin.Log.LogError($"XRSystem.SetupFrame override failed: {e}");
                return true;
            }
        }
    }
}
