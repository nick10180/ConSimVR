using System;
using System.Collections.Generic;
using UnityEngine;

namespace ConSimVR
{
    /// <summary>
    /// Public surface for companion mods (e.g. ConSimVR.RemoteOperator).
    /// All members must be used from Unity's main thread.
    /// </summary>
    public static class VrApi
    {
        /// <summary>
        /// Supplies the world pose of the VR "seat" (the point the recentred head maps to) instead of the game camera.
        /// Return false to decline for this frame. Rotation is used as given; keep it level for standing views.
        /// </summary>
        public delegate bool AnchorProvider(Camera gameCamera, out Vector3 position, out Quaternion rotation);

        private sealed class ProviderEntry
        {
            public string Owner;
            public int Priority;
            public AnchorProvider Provider;
        }

        private static readonly List<ProviderEntry> s_providers = new();

        /// <summary>The VR mod is enabled in its config (game-side camera tweaks apply even before the headset session runs).</summary>
        public static bool IsEnabled => VrConfig.Enabled?.Value ?? false;

        /// <summary>An OpenXR session is running and frames are being submitted to the headset.</summary>
        public static bool IsVrActive => VrSession.Instance?.IsRunning ?? false;

        /// <summary>The game camera that is rendered in stereo.</summary>
        public static Camera GameCamera => GameState.MainCamera;

        /// <summary>The camera type the game currently uses.</summary>
        public static WbCameraType CurrentCameraType => GameState.CurrentCameraType;

        /// <summary>The game's camera manager, or null while it does not exist (loading).</summary>
        public static Wb.Camera.WbCameraManager CameraManager => GameState.CameraManager;

        /// <summary>The camera controller of the machine the player operates, or null when not in a machine.</summary>
        public static Wb.Camera.Machine.WbMachineCameraController ActiveMachineController => GameState.ActiveMachineController;

        /// <summary>Null- and destroyed-safe check for IL2CPP Unity objects.</summary>
        public static bool IsAlive(UnityEngine.Object obj) => GameState.IsAlive(obj);

        /// <summary>Raised on the main thread after the user recentres (or the runtime recentres).</summary>
        public static event Action Recentered;

        /// <summary>Registers or replaces an anchor provider. Higher priority wins; ties go to the latest registration.</summary>
        public static void SetAnchorProvider(string owner, AnchorProvider provider, int priority = 0)
        {
            ClearAnchorProvider(owner);
            s_providers.Add(new ProviderEntry { Owner = owner, Priority = priority, Provider = provider });
            s_providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public static void ClearAnchorProvider(string owner) => s_providers.RemoveAll(p => p.Owner == owner);

        /// <summary>Recentre on the next frame using the current head pose.</summary>
        public static void RequestRecenter() => VrSession.Instance?.RequestRecenter();

        internal static bool TryGetProvidedAnchor(Camera camera, out Vector3 position, out Quaternion rotation)
        {
            foreach (var entry in s_providers)
            {
                try
                {
                    if (entry.Provider(camera, out position, out rotation))
                        return true;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Anchor provider '{entry.Owner}' threw and was removed: {e}");
                    s_providers.Remove(entry);
                    break;
                }
            }
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        internal static void RaiseRecentered()
        {
            try { Recentered?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError($"Recentered handler threw: {e}"); }
        }
    }
}
