using ConSimVR.Core.Math;
using UnityEngine;
using CorePose = ConSimVR.Core.Math.Pose;
using NVector3 = System.Numerics.Vector3;

namespace ConSimVR
{
    /// <summary>
    /// Decides where the VR seat sits in the world each frame. The recentred head pose is applied relative to this anchor.
    /// </summary>
    internal static class CameraRig
    {
        public static CorePose ComputeAnchor(Camera camera)
        {
            if (VrApi.TryGetProvidedAnchor(camera, out var providedPosition, out var providedRotation))
                return new CorePose(providedPosition.ToCore(), providedRotation.ToCore());

            var transform = camera.transform;
            var position = transform.position.ToCore();
            var rotation = transform.rotation.ToCore();
            var type = GameState.CurrentCameraType;

            System.Numerics.Quaternion anchorRotation;
            if (type == WbCameraType.MACHINE_INTERIOR && VrConfig.FollowVehicleTilt.Value)
            {
                // Heading from the (mouse-look-free) cab camera, tilt from the cab itself.
                var mount = GameState.ActiveCameraMount;
                var up = mount != null ? mount.up.ToCore() : NVector3.UnitY;
                anchorRotation = VrRotation.LevelWithUp(rotation, up);
            }
            else
            {
                // On foot, in third person, or with tilt disabled: keep the horizon level.
                anchorRotation = VrRotation.YawOnly(rotation);
            }

            var seat = new CorePose(
                new NVector3(VrConfig.SeatOffsetRight.Value, VrConfig.SeatOffsetUp.Value, VrConfig.SeatOffsetForward.Value),
                System.Numerics.Quaternion.Identity);
            return new CorePose(position, anchorRotation) * seat;
        }
    }
}
