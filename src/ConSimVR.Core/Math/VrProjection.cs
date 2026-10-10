using System;
using System.Numerics;

namespace ConSimVR.Core.Math
{
    /// <summary>Per-eye camera matrices in Unity's conventions.</summary>
    public static class VrProjection
    {
        /// <summary>
        /// Off-axis projection for an OpenXR FOV in Unity's <c>Camera.projectionMatrix</c> convention
        /// (OpenGL clip space, camera looks down -Z, NDC z in [-1, 1]); equal to <see cref="Matrix4.Frustum"/>
        /// with l = n*tan(left), r = n*tan(right), b = n*tan(down), t = n*tan(up).
        /// </summary>
        public static Matrix4 Projection(Fov fov, float near, float far)
        {
            return Matrix4.Frustum(
                near * MathF.Tan(fov.AngleLeft),
                near * MathF.Tan(fov.AngleRight),
                near * MathF.Tan(fov.AngleDown),
                near * MathF.Tan(fov.AngleUp),
                near,
                far);
        }

        /// <summary>
        /// Unity <c>Camera.worldToCameraMatrix</c> for an eye at <paramref name="eyeWorld"/>:
        /// Scale(1, 1, -1) * Inverse(TRS(position, rotation, one)). Camera space looks down -Z.
        /// </summary>
        public static Matrix4 WorldToCamera(Pose eyeWorld)
        {
            Matrix4 m = eyeWorld.Inverse().ToMatrix();
            // Pre-multiplying by Scale(1, 1, -1) negates the third row.
            m.m20 = -m.m20;
            m.m21 = -m.m21;
            m.m22 = -m.m22;
            m.m23 = -m.m23;
            return m;
        }
    }
}
