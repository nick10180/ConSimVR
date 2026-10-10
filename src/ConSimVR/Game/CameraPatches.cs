using HarmonyLib;
using UnityEngine;
using Wb.Camera;
using Wb.Camera.Character;
using Wb.Camera.Machine;
using Wb.MachineSystem;

namespace ConSimVR
{
    /// <summary>
    /// Game-side camera behaviour for VR: default to first-person/cab cameras, keep the game camera neutral
    /// (no mouse pitch, no head bob, no automatic look-back) so the headset does the looking. Nothing here applies
    /// once VR is stopped (F9), so flat play behaves exactly like the unmodded game.
    /// </summary>
    [HarmonyPatch]
    internal static class CameraPatches
    {
        /// <summary>VR was started (the session may still be waiting for the headset): steer camera choices.</summary>
        private static bool VrStarted => VrSession.Instance != null;

        /// <summary>Frames are going to the headset: suppress look input and camera motion the head replaces.</summary>
        private static bool VrRunning => VrApi.IsVrActive;

        /// <summary>
        /// Machines keep a camera set per machine state (Drive, Function1, ...). Swap third-person requests for the cab
        /// camera when this state has one. States without one (e.g. a truck's loading crane in Function1) keep the game's
        /// choice; ConSimVR.RemoteOperator handles those.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(WbMachineCameraController), nameof(WbMachineCameraController.ActivateCamera),
            new[] { typeof(WbMachineState), typeof(WbCameraType), typeof(bool), typeof(bool) })]
        private static void MachineActivateCamera(WbMachineCameraController __instance, WbMachineState machineState, ref WbCameraType cameraType)
        {
            if (!VrStarted || !VrConfig.ForceFirstPerson.Value || !GameState.IsThirdPersonGameplay(cameraType))
                return;

            if (__instance.TryGetCameraForStateAndType(machineState, WbCameraType.MACHINE_INTERIOR, out AWbCameraBehaviour interior) && GameState.IsAlive(interior))
                cameraType = WbCameraType.MACHINE_INTERIOR;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WbCharacterCameraController), nameof(WbCharacterCameraController.ActivateCamera), new[] { typeof(WbCameraType) })]
        private static void CharacterActivateCamera(ref WbCameraType cameraType)
        {
            if (VrStarted && VrConfig.ForceFirstPerson.Value && cameraType == WbCameraType.CHARACTER_THIRD_PERSON)
                cameraType = WbCameraType.CHARACTER_FIRST_PERSON;
        }

        /// <summary>
        /// Mouse/stick look input in VR. In a cab both axes are dropped (the head looks around, the cab sets the heading).
        /// On foot only pitch is dropped: horizontal mouse still turns the character, which is how you change walking
        /// direction; the VR view follows that heading while the head supplies pitch.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(WbCameraManager), nameof(WbCameraManager.GetCameraInput))]
        private static void GetCameraInput(ref Vector2 __result)
        {
            if (!VrRunning || !VrConfig.BlockMouseLook.Value)
                return;
            var type = GameState.CurrentCameraType;
            if (type == WbCameraType.MACHINE_INTERIOR)
                __result = Vector2.zero;
            else if (type == WbCameraType.CHARACTER_FIRST_PERSON)
                __result = new Vector2(__result.x, 0f);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WbCharacterFirstPersonCameraBehaviour), nameof(WbCharacterFirstPersonCameraBehaviour.UpdateCamera))]
        private static void CharacterFirstPersonUpdate(WbCharacterFirstPersonCameraBehaviour __instance)
        {
            if (VrRunning && VrConfig.DisableHeadBob.Value && __instance.isNoiseMovementEnabled)
                __instance.isNoiseMovementEnabled = false;
        }

        /// <summary>The cab camera can swing round to look over a shoulder when reversing; in VR the user turns their head instead.</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(WbMachineFirstPersonCameraBehaviour), nameof(WbMachineFirstPersonCameraBehaviour.UpdateCamera))]
        private static void MachineFirstPersonUpdate(WbMachineFirstPersonCameraBehaviour __instance)
        {
            if (!VrRunning)
                return;
            if (__instance.enableLookBack)
                __instance.enableLookBack = false;
            if (__instance.lookBackAutomatically)
                __instance.lookBackAutomatically = false;
        }

        /// <summary>Switch the current pawn to first person, e.g. when VR starts mid-game.</summary>
        public static void SwitchToFirstPersonNow()
        {
            if (!VrConfig.ForceFirstPerson.Value)
                return;
            var manager = GameState.CameraManager;
            if (manager == null)
                return;
            var type = manager.currentCameraType;
            if (type == WbCameraType.CHARACTER_THIRD_PERSON)
                manager.SwitchToCamera(WbCameraType.CHARACTER_FIRST_PERSON);
            else if (type == WbCameraType.MACHINE_SMART || type == WbCameraType.MACHINE_EXTERNAL)
                manager.SwitchToCamera(WbCameraType.MACHINE_INTERIOR);
        }
    }
}
