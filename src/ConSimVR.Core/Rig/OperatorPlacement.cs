using System;
using System.Numerics;
using ConSimVR.Core.Math;

namespace ConSimVR.Core.Rig
{
    /// <summary>
    /// Geometry for the "remote operator" viewpoint: standing on the ground at a distance from what is being operated
    /// (e.g. a truck-mounted crane), facing it. Unity conventions (+Y up, +Z forward, yaw positive clockwise from above).
    /// </summary>
    public static class OperatorPlacement
    {
        /// <summary>
        /// Point on the horizontal circle of radius <paramref name="distance"/> around <paramref name="target"/>, on the
        /// side <paramref name="viewFrom"/> is on, then orbited by <paramref name="orbitDegrees"/>. Y is the target's
        /// height; the caller snaps it to the ground.
        /// </summary>
        public static Vector3 PlaceAround(Vector3 target, Vector3 viewFrom, float distance, float orbitDegrees)
        {
            var direction = new Vector3(viewFrom.X - target.X, 0f, viewFrom.Z - target.Z);
            direction = direction.LengthSquared() < 1e-6f ? new Vector3(0f, 0f, -1f) : Vector3.Normalize(direction);
            var orbit = Quaternion.CreateFromAxisAngle(Vector3.UnitY, orbitDegrees * (MathF.PI / 180f));
            direction = Vector3.Transform(direction, orbit);
            return new Vector3(target.X + direction.X * distance, target.Y, target.Z + direction.Z * distance);
        }

        /// <summary>Level rotation at <paramref name="from"/> looking horizontally towards <paramref name="to"/>.</summary>
        public static Quaternion FacingYaw(Vector3 from, Vector3 to)
        {
            var direction = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
            if (direction.LengthSquared() < 1e-6f)
                return Quaternion.Identity;
            return VrRotation.LookRotation(Vector3.Normalize(direction), Vector3.UnitY);
        }

        /// <summary>Expresses a world offset in the yaw-only frame of <paramref name="frameRotation"/> (follows heading, ignores tilt).</summary>
        public static Vector3 ToYawFrame(Vector3 worldOffset, Quaternion frameRotation) =>
            Vector3.Transform(worldOffset, Quaternion.Inverse(VrRotation.YawOnly(frameRotation)));

        /// <summary>Inverse of <see cref="ToYawFrame"/>.</summary>
        public static Vector3 FromYawFrame(Vector3 localOffset, Quaternion frameRotation) =>
            Vector3.Transform(localOffset, VrRotation.YawOnly(frameRotation));
    }
}
