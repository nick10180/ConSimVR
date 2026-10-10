using System.Numerics;

namespace ConSimVR.Core.Math
{
    /// <summary>
    /// Conversion between OpenXR space (right-handed, +X right, +Y up, -Z forward) and Unity space
    /// (left-handed, +X right, +Y up, +Z forward). Mirroring Z maps position (x, y, z) to (x, y, -z)
    /// and rotation (x, y, z, w) to (-x, -y, z, w). The mapping is its own inverse.
    /// </summary>
    public static class XrConvert
    {
        public static Pose ToUnity(Quaternion xrRotation, Vector3 xrPosition)
        {
            return new Pose(MirrorZ(xrPosition), MirrorZ(xrRotation));
        }

        public static void ToOpenXr(Pose unityPose, out Quaternion xrRotation, out Vector3 xrPosition)
        {
            xrRotation = MirrorZ(unityPose.Rotation);
            xrPosition = MirrorZ(unityPose.Position);
        }

        /// <summary>Converts a position or direction between OpenXR and Unity (either way).</summary>
        public static Vector3 MirrorZ(Vector3 v) => new Vector3(v.X, v.Y, -v.Z);

        /// <summary>Converts a rotation between OpenXR and Unity (either way).</summary>
        public static Quaternion MirrorZ(Quaternion q) => new Quaternion(-q.X, -q.Y, q.Z, q.W);
    }
}
