using ConSimVR.Core.Math;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;
using CorePose = ConSimVR.Core.Math.Pose;

namespace ConSimVR
{
    /// <summary>Conversions between ConSimVR.Core math types and the IL2CPP Unity structs.</summary>
    internal static class UnityConvert
    {
        public static Vector3 ToUnity(this NVector3 v) => new(v.X, v.Y, v.Z);
        public static Quaternion ToUnity(this NQuaternion q) => new(q.X, q.Y, q.Z, q.W);
        public static NVector3 ToCore(this Vector3 v) => new(v.x, v.y, v.z);
        public static NQuaternion ToCore(this Quaternion q) => new(q.x, q.y, q.z, q.w);

        public static CorePose PoseOf(Transform t) => new(t.position.ToCore(), t.rotation.ToCore());

        public static Matrix4x4 ToUnity(this in Matrix4 m) => new()
        {
            m00 = m.m00, m10 = m.m10, m20 = m.m20, m30 = m.m30,
            m01 = m.m01, m11 = m.m11, m21 = m.m21, m31 = m.m31,
            m02 = m.m02, m12 = m.m12, m22 = m.m22, m32 = m.m32,
            m03 = m.m03, m13 = m.m13, m23 = m.m23, m33 = m.m33,
        };
    }
}
