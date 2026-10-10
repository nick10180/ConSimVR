using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ConSimVR
{
    /// <summary>
    /// Native OpenXR VR for Construction Simulator (Unity 2020.3, HDRP, IL2CPP).
    /// Requires the ConSimVR.Il2CppFix preloader patcher on this game build.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.consimvr.vr";
        public const string Name = "ConSimVR";
        public const string Version = "1.0.1";

        internal static new ManualLogSource Log;
        internal static VrLog VrLog;
        internal static string PluginDir;
        internal static readonly RenderQualityTweaks RenderQuality = new();

        public override void Load()
        {
            Log = base.Log;
            VrLog = new VrLog(Log);
            PluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            VrConfig.Bind(Config);

            if (!VrConfig.Enabled.Value)
            {
                Log.LogInfo("Disabled in config.");
                return;
            }

            var harmony = new Harmony(Guid);
            try
            {
                harmony.CreateClassProcessor(typeof(HdrpPatches)).Patch();
            }
            catch (Exception e)
            {
                Log.LogError($"HDRP patch failed; stereo rendering cannot work: {e}");
            }
            try
            {
                harmony.CreateClassProcessor(typeof(HudPatches)).Patch();
            }
            catch (Exception e)
            {
                Log.LogError($"HUD marker patch failed; navigation markers stay where the game camera would put them: {e}");
            }
            try
            {
                harmony.CreateClassProcessor(typeof(CameraPatches)).Patch();
            }
            catch (Exception e)
            {
                Log.LogError($"Camera patches failed; VR still works but the game may stay in third person: {e}");
            }

            AddComponent<VrBehaviour>();
            Log.LogInfo($"{Name} {Version} loaded. Keys: VR {VrConfig.ToggleVrKey.Value}, recenter {VrConfig.RecenterKey.Value}, UI panel {VrConfig.ToggleUiKey.Value}.");
        }
    }
}
