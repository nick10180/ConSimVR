using System;
using System.Numerics;

namespace ConSimVR.Core.Math
{
    /// <summary>Builds one culling frustum that encloses both eye frusta (Unity convention).</summary>
    public static class CullingFrustum
    {
        private const float MaxHalfAngle = MathF.PI * 0.5f - 1e-4f;
        private const float MinForward = 1e-3f;   // edge directions (eye-space z = 1) must keep this much culling-forward component

        /// <summary>
        /// Combines two eye frusta (poses in any common Unity-convention space) into a single culling frustum.
        /// <para>
        /// Orientation is the halfway rotation between the eyes. The origin is the eye midpoint pushed back along
        /// -forward by the smallest t >= 0 for which the combined tangents (min left/down, max right/up of both eyes'
        /// edge directions expressed in the culling orientation) contain both eye origins, hence both eye cones.
        /// </para>
        /// <para>
        /// cullNear/cullFar start from near + t / far + t and are widened (never narrowed) to cover the eyes' actual
        /// near/far plane corners. For parallel eyes in a plane perpendicular to forward the corners lie exactly at
        /// near + t and far + t; with canted eyes the outer near corners are closer to the culling origin, so cullNear
        /// shrinks slightly to keep them inside. cullProjection = Frustum(L*cullNear, R*cullNear, D*cullNear, U*cullNear, cullNear, cullFar).
        /// </para>
        /// Returns false (and identity/default outputs) for degenerate input: non-positive near, far &lt;= near,
        /// inverted or >= 90 degree FOV angles, an edge direction not pointing forward in culling space, or
        /// combined tangents that do not straddle the forward axis (L &gt;= 0, R &lt;= 0, D &gt;= 0, U &lt;= 0).
        /// </summary>
        public static bool TryCombine(Pose leftEye, Fov leftFov, Pose rightEye, Fov rightFov, float near, float far,
                                      out Pose cullPose, out Matrix4 cullProjection, out float cullNear, out float cullFar)
        {
            cullPose = Pose.Identity;
            cullProjection = Matrix4.Identity;
            cullNear = near;
            cullFar = far;

            if (!(near > 0f) || !(far > near) || !float.IsFinite(far)) return false;
            if (!IsValid(leftFov) || !IsValid(rightFov)) return false;

            // Halfway rotation (normalized lerp on the same hemisphere is exact for t = 0.5).
            Quaternion lq = leftEye.Rotation, rq = rightEye.Rotation;
            Quaternion sum = Quaternion.Dot(lq, rq) < 0f ? lq - rq : lq + rq;
            float sumLen = sum.Length();
            if (!(sumLen > 1e-6f)) return false;
            Quaternion cullRot = sum * (1f / sumLen);
            Quaternion toCull = Quaternion.Conjugate(cullRot);
            Vector3 mid = (leftEye.Position + rightEye.Position) * 0.5f;

            // Both eyes in culling space, relative to the midpoint.
            Eye left = new Eye(toCull * lq, Vector3.Transform(leftEye.Position - mid, toCull), leftFov);
            Eye right = new Eye(toCull * rq, Vector3.Transform(rightEye.Position - mid, toCull), rightFov);

            float l = float.PositiveInfinity, r = float.NegativeInfinity;
            float d = float.PositiveInfinity, u = float.NegativeInfinity;
            if (!left.AccumulateTangents(ref l, ref r, ref d, ref u)) return false;
            if (!right.AccumulateTangents(ref l, ref r, ref d, ref u)) return false;
            if (!(l < 0f) || !(r > 0f) || !(d < 0f) || !(u > 0f)) return false;

            float t = 0f;
            t = left.RequiredBackOffset(t, l, r, d, u);
            t = right.RequiredBackOffset(t, l, r, d, u);
            if (!float.IsFinite(t)) return false;

            float nearDepth = near + t;
            float farDepth = far + t;
            left.WidenDepthRange(t, near, far, ref nearDepth, ref farDepth);
            right.WidenDepthRange(t, near, far, ref nearDepth, ref farDepth);
            if (!(nearDepth > 0f) || !(farDepth > nearDepth)) return false;

            Vector3 forward = Vector3.Transform(Vector3.UnitZ, cullRot);
            cullPose = new Pose(mid - forward * t, cullRot);
            cullNear = nearDepth;
            cullFar = farDepth;
            cullProjection = Matrix4.Frustum(l * nearDepth, r * nearDepth, d * nearDepth, u * nearDepth, nearDepth, farDepth);
            return true;
        }

        private static bool IsValid(Fov fov)
        {
            return InRange(fov.AngleLeft) && InRange(fov.AngleRight) && InRange(fov.AngleUp) && InRange(fov.AngleDown)
                && fov.AngleLeft < fov.AngleRight && fov.AngleDown < fov.AngleUp;
        }

        private static bool InRange(float angle) => angle > -MaxHalfAngle && angle < MaxHalfAngle;

        /// <summary>One eye in culling space: rotation, origin relative to the eye midpoint, and its four edge directions.</summary>
        private readonly struct Eye
        {
            private readonly Vector3 _origin;
            private readonly Vector3 _ld, _rd, _lu, _ru;   // edge directions with eye-space z = 1, in culling space

            public Eye(Quaternion rotation, Vector3 origin, Fov fov)
            {
                _origin = origin;
                float tl = MathF.Tan(fov.AngleLeft), tr = MathF.Tan(fov.AngleRight);
                float td = MathF.Tan(fov.AngleDown), tu = MathF.Tan(fov.AngleUp);
                _ld = Vector3.Transform(new Vector3(tl, td, 1f), rotation);
                _rd = Vector3.Transform(new Vector3(tr, td, 1f), rotation);
                _lu = Vector3.Transform(new Vector3(tl, tu, 1f), rotation);
                _ru = Vector3.Transform(new Vector3(tr, tu, 1f), rotation);
            }

            public bool AccumulateTangents(ref float l, ref float r, ref float d, ref float u)
            {
                return Accumulate(_ld, ref l, ref r, ref d, ref u)
                    && Accumulate(_rd, ref l, ref r, ref d, ref u)
                    && Accumulate(_lu, ref l, ref r, ref d, ref u)
                    && Accumulate(_ru, ref l, ref r, ref d, ref u);
            }

            /// <summary>Smallest backward offset (at least <paramref name="t"/>) that puts this eye's origin inside the combined cone.</summary>
            public float RequiredBackOffset(float t, float l, float r, float d, float u)
            {
                Vector3 p = _origin;
                t = MathF.Max(t, p.X / l - p.Z);
                t = MathF.Max(t, p.X / r - p.Z);
                t = MathF.Max(t, p.Y / d - p.Z);
                t = MathF.Max(t, p.Y / u - p.Z);
                return t;
            }

            /// <summary>Widens [nearDepth, farDepth] to cover this eye's near and far plane corners (culling-space depth).</summary>
            public void WidenDepthRange(float t, float near, float far, ref float nearDepth, ref float farDepth)
            {
                float z0 = _origin.Z + t;
                float minZ = MathF.Min(MathF.Min(_ld.Z, _rd.Z), MathF.Min(_lu.Z, _ru.Z));
                float maxZ = MathF.Max(MathF.Max(_ld.Z, _rd.Z), MathF.Max(_lu.Z, _ru.Z));
                nearDepth = MathF.Min(nearDepth, z0 + near * minZ);
                farDepth = MathF.Max(farDepth, z0 + far * maxZ);
            }

            private static bool Accumulate(Vector3 dir, ref float l, ref float r, ref float d, ref float u)
            {
                if (!(dir.Z > MinForward)) return false;
                float x = dir.X / dir.Z, y = dir.Y / dir.Z;
                if (x < l) l = x;
                if (x > r) r = x;
                if (y < d) d = y;
                if (y > u) u = y;
                return true;
            }
        }
    }
}
