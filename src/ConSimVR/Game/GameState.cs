using System;
using Wb.Camera;
using Wb.Camera.Machine;
using Wb.Core;
using UnityEngine;

namespace ConSimVR
{
    /// <summary>Read-only access to the game's camera state. Main thread only.</summary>
    internal static class GameState
    {
        public static bool IsAlive(UnityEngine.Object obj) => obj != null && !obj.WasCollected && obj;

        public static WbCameraManager CameraManager
        {
            get
            {
                try
                {
                    var manager = WbManager<WbCameraManager>.Instance;
                    return IsAlive(manager) ? manager : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>The camera the game renders the world with (Cinemachine output camera).</summary>
        public static Camera MainCamera
        {
            get
            {
                var manager = CameraManager;
                var camera = manager != null ? manager.mainCam : null;
                if (IsAlive(camera))
                    return camera;
                camera = Camera.main;
                return IsAlive(camera) ? camera : null;
            }
        }

        public static WbCameraType CurrentCameraType
        {
            get
            {
                var manager = CameraManager;
                return manager != null ? manager.currentCameraType : WbCameraType.UNDEFINED;
            }
        }

        public static bool IsFirstPerson(WbCameraType type) =>
            type == WbCameraType.CHARACTER_FIRST_PERSON || type == WbCameraType.MACHINE_INTERIOR;

        public static bool IsThirdPersonGameplay(WbCameraType type) =>
            type == WbCameraType.CHARACTER_THIRD_PERSON
            || type == WbCameraType.MACHINE_SMART
            || type == WbCameraType.MACHINE_EXTERNAL
            || type == WbCameraType.MACHINE_LOOK_BACK;

        /// <summary>The active machine camera controller, when the player is operating a machine.</summary>
        public static WbMachineCameraController ActiveMachineController
        {
            get
            {
                var manager = CameraManager;
                var controller = manager != null ? manager.activeController : null;
                if (!IsAlive(controller))
                    return null;
                var machineController = controller.TryCast<WbMachineCameraController>();
                return IsAlive(machineController) ? machineController : null;
            }
        }

        /// <summary>
        /// Transform the first-person camera is mounted on (the cab or the character), used for the cab's up axis.
        /// Null when no camera behaviour is active.
        /// </summary>
        public static Transform ActiveCameraMount
        {
            get
            {
                var machine = ActiveMachineController;
                var behaviour = machine != null ? machine.currentCamera : null;
                if (!IsAlive(behaviour))
                    return null;
                var transform = behaviour.transform;
                return transform.parent != null ? transform.parent : transform;
            }
        }
    }
}
