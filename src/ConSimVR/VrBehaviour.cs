using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace ConSimVR
{
    /// <summary>Injected MonoBehaviour that drives the VR session from Unity's player loop.</summary>
    public class VrBehaviour : MonoBehaviour
    {
        private const float AutoStartDelaySeconds = 8f;
        private bool _autoStartAttempted;

        public VrBehaviour(IntPtr pointer) : base(pointer) { }

        private void Start()
        {
            this.StartCoroutine(EndOfFrameLoop());
        }

        private void Update()
        {
            Plugin.VrLog.Drain();
            Plugin.RenderQuality.Update();

            if (Input.GetKeyDown(VrConfig.ToggleVrKey.Value))
            {
                if (VrSession.Instance == null)
                    StartVr();
                else
                    VrSession.Stop();
            }

            if (VrSession.Instance == null && VrConfig.AutoStart.Value && !_autoStartAttempted
                && Time.realtimeSinceStartup > AutoStartDelaySeconds && GameState.CameraManager != null)
            {
                _autoStartAttempted = true;
                StartVr();
            }

            var session = VrSession.Instance;
            if (session == null)
                return;
            if (Input.GetKeyDown(VrConfig.RecenterKey.Value))
                session.RequestRecenter();
            if (Input.GetKeyDown(VrConfig.ToggleUiKey.Value))
                session.ToggleUi();
            if (Input.GetKeyDown(VrConfig.DumpKey.Value))
                session.RequestDump();
            session.Update();
        }

        private void LateUpdate() => VrSession.Instance?.LateUpdate();

        [HideFromIl2Cpp]
        private IEnumerator EndOfFrameLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                try
                {
                    VrSession.Instance?.OnEndOfFrame();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"End-of-frame VR update failed: {e}");
                }
            }
        }

        private void OnApplicationQuit() => VrSession.Stop();

        [HideFromIl2Cpp]
        private static void StartVr()
        {
            try
            {
                if (VrSession.TryStart(out string error))
                    Plugin.Log.LogInfo("VR started; waiting for the headset session to become ready.");
                else
                    Plugin.Log.LogError($"VR failed to start: {error}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"VR failed to start: {e}");
                VrSession.Stop();
            }
        }
    }
}
