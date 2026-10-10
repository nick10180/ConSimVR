using System;
using ConSimVR.Core.Math;
using ConSimVR.Core.Rig;
using UnityEngine;
using Wb.Camera;
using Wb.Camera.Machine;
using Wb.MachineSystem;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace ConSimVR.RemoteOperator
{
    /// <summary>
    /// Anchor provider that stands the VR player on the ground beside the machine while it is in a secondary mode that
    /// is really operated from outside, e.g. the flatbed truck's loading crane. The game keeps showing the driving cab
    /// camera in those modes (there is no operator seat), so the rule is: the mode's cab camera is the same one used for
    /// driving, and a hook is active in that mode. Machines with a dedicated operator camera (a mobile crane's crane cab)
    /// stay in first person. A toggle key overrides the rule per machine mode.
    /// The position follows the machine; the view never turns on its own, the player looks around with their head.
    /// </summary>
    internal sealed class RemoteOperator
    {
        public static RemoteOperator Instance;

        // Decision cache for the current machine mode.
        private IntPtr _decisionMachine;
        private WbMachineState _decisionState;
        private bool _decision;
        private bool? _override;
        private Transform _hook;

        // Placement, all relative to the machine root in its yaw frame.
        private IntPtr _placedMachine;
        private NVector3 _targetInMachineFrame;
        private NVector3 _bearingInMachineFrame;
        private NVector3 _offsetInMachineFrame;
        private NQuaternion _facingInMachineFrame;
        private float _orbitDegrees;
        private float _distance;
        private float _groundY;
        private int _nextGroundCheckFrame;
        private bool _resetPlacement;

        public bool IsActive { get; private set; }

        /// <summary>Switch between standing outside and the cab for the current machine mode.</summary>
        public void Toggle()
        {
            if (_decisionMachine == IntPtr.Zero)
                return;
            _override = !(_override ?? _decision);
            RemoteOperatorPlugin.Logger.LogInfo(_override.Value ? "Remote operator view forced on for this machine mode." : "Remote operator view forced off for this machine mode.");
        }

        /// <summary>Back to the default spot beside the machine.</summary>
        public void ResetPlacement() => _resetPlacement = true;

        public void Orbit(float degrees)
        {
            _orbitDegrees += degrees;
            Reposition();
        }

        public void Step(float metres)
        {
            _distance = Math.Clamp(_distance + metres, 2f, 40f);
            Reposition();
        }

        public bool TryGetAnchor(Camera gameCamera, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;

            var controller = VrApi.IsVrActive ? VrApi.ActiveMachineController : null;
            var machine = controller != null ? controller.machine : null;
            if (!VrApi.IsAlive(machine) || !ShouldOperateRemotely(controller, machine))
            {
                Deactivate();
                return false;
            }

            var root = machine.transform;
            var rootPosition = root.position.ToNumerics();
            var rootRotation = root.rotation.ToNumerics();
            if (!IsActive || root.Pointer != _placedMachine || _resetPlacement)
                Place(machine, root, rootPosition, rootRotation);

            var operatorPosition = rootPosition + OperatorPlacement.FromYawFrame(_offsetInMachineFrame, rootRotation);
            if (Time.frameCount >= _nextGroundCheckFrame)
            {
                _groundY = GroundHeight(operatorPosition, root);
                _nextGroundCheckFrame = Time.frameCount + 15;
            }

            position = new Vector3(operatorPosition.X, _groundY + RemoteOperatorPlugin.EyeHeight.Value, operatorPosition.Z);
            var facing = VrRotation.YawOnly(rootRotation) * _facingInMachineFrame;
            rotation = new Quaternion(facing.X, facing.Y, facing.Z, facing.W);
            return true;
        }

        private bool ShouldOperateRemotely(WbMachineCameraController controller, WbMachine machine)
        {
            var state = machine.CurrentMachineState;
            if (machine.Pointer != _decisionMachine || state != _decisionState)
            {
                _decisionMachine = machine.Pointer;
                _decisionState = state;
                _override = null;
                _decision = Decide(controller, machine, state);
            }
            return _override ?? _decision;
        }

        private bool Decide(WbMachineCameraController controller, WbMachine machine, WbMachineState state)
        {
            var mode = RemoteOperatorPlugin.Mode.Value;
            bool hasCab = controller.TryGetCameraForStateAndType(state, WbCameraType.MACHINE_INTERIOR, out AWbCameraBehaviour stateCab) && VrApi.IsAlive(stateCab);
            bool sharesDriveCab = false;
            if (hasCab && state != WbMachineState.Drive)
                sharesDriveCab = controller.TryGetCameraForStateAndType(WbMachineState.Drive, WbCameraType.MACHINE_INTERIOR, out AWbCameraBehaviour driveCab)
                    && VrApi.IsAlive(driveCab) && driveCab.Pointer == stateCab.Pointer;
            _hook = FindActiveHook(machine);

            bool decision = mode switch
            {
                OperatorMode.Off => false,
                OperatorMode.AnySecondaryMode => !hasCab || sharesDriveCab,
                _ => !hasCab || (sharesDriveCab && _hook != null),
            };
            RemoteOperatorPlugin.Logger.LogInfo(
                $"Machine '{machine.name}' mode {state}: cab camera {(hasCab ? "yes" : "no")}, same as driving cab {sharesDriveCab}, active hook {_hook != null} -> " +
                $"{(decision ? "stand beside the machine" : "stay in the cab")} ({RemoteOperatorPlugin.ToggleKey.Value} toggles).");
            return decision;
        }

        private static Transform FindActiveHook(WbMachine machine)
        {
            foreach (var hook in machine.GetComponentsInChildren<WbHookComponent>(true))
            {
                if (!VrApi.IsAlive(hook) || !hook.IsStateActive)
                    continue;
                var hookObject = hook.hookObject;
                return VrApi.IsAlive(hookObject) ? hookObject.transform : hook.transform;
            }
            return null;
        }

        private void Place(WbMachine machine, Transform root, NVector3 rootPosition, NQuaternion rootRotation)
        {
            bool fresh = !IsActive || root.Pointer != _placedMachine || _resetPlacement;
            var target = VrApi.IsAlive(_hook) ? _hook.position.ToNumerics() : MachineCentre(machine, root);
            _targetInMachineFrame = OperatorPlacement.ToYawFrame(target - rootPosition, rootRotation);
            if (fresh)
            {
                // Default: stand off the machine's side, level with the operated part.
                float side = RemoteOperatorPlugin.Side.Value == OperatorSide.Left ? -1f : 1f;
                _bearingInMachineFrame = new NVector3(side, 0f, 0f);
                _orbitDegrees = 0f;
                _distance = RemoteOperatorPlugin.Distance.Value;
            }
            _placedMachine = root.Pointer;
            _resetPlacement = false;
            IsActive = true;
            Reposition();
        }

        /// <summary>Recomputes position and facing from the stored bearing, orbit and distance (machine frame).</summary>
        private void Reposition()
        {
            if (!IsActive)
                return;
            var from = _targetInMachineFrame + _bearingInMachineFrame;
            var operatorLocal = OperatorPlacement.PlaceAround(_targetInMachineFrame, from, _distance, _orbitDegrees);
            _offsetInMachineFrame = operatorLocal;
            _facingInMachineFrame = OperatorPlacement.FacingYaw(operatorLocal, _targetInMachineFrame);
            _nextGroundCheckFrame = 0;
        }

        private static NVector3 MachineCentre(WbMachine machine, Transform root)
        {
            var centre = machine.CenterTransform;
            return (VrApi.IsAlive(centre) ? centre : root).position.ToNumerics();
        }

        /// <summary>Ground height below <paramref name="point"/>, ignoring the machine's own colliders.</summary>
        private static float GroundHeight(NVector3 point, Transform machineRoot)
        {
            var origin = new Vector3(point.X, point.Y + 50f, point.Z);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                    break;
                if (!hit.transform.IsChildOf(machineRoot))
                    return hit.point.y;
                origin = hit.point + Vector3.down * 0.05f;
            }
            return machineRoot.position.y;
        }

        private void Deactivate()
        {
            if (IsActive)
                RemoteOperatorPlugin.Logger.LogInfo("Remote operator view off.");
            IsActive = false;
            _placedMachine = IntPtr.Zero;
            if (!VrApi.IsVrActive || VrApi.ActiveMachineController == null)
                _decisionMachine = IntPtr.Zero;
        }
    }

    internal static class NumericsExtensions
    {
        public static NVector3 ToNumerics(this Vector3 v) => new(v.x, v.y, v.z);
        public static NQuaternion ToNumerics(this Quaternion q) => new(q.x, q.y, q.z, q.w);
    }
}
