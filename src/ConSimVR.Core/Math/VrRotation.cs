using System;
using System.Numerics;

namespace ConSimVR.Core.Math
{
    /// <summary>Rotation helpers in Unity convention (left-handed, +Y up, +Z forward).</summary>
    public static class VrRotation
    {
        private const float Epsilon = 1e-5f;          // same threshold Unity uses in LookRotation
        private const float DegenerateSq = 1e-6f;     // squared length below which a projected heading is unusable
        private const float Rad2Deg = 180f / MathF.PI;

        /// <summary>
        /// Pure rotation about +Y that keeps the heading of <paramref name="rotation"/>'s forward vector and drops
        /// pitch and roll. When looking (almost) straight up or down, the heading is taken from the head's up vector
        /// instead, so the result stays stable. Returns identity for a degenerate (zero) quaternion.
        /// </summary>
        public static Quaternion YawOnly(Quaternion rotation)
        {
            if (!TryHeading(rotation, Vector3.UnitY, out Vector3 h)) return Quaternion.Identity;
            float half = 0.5f * MathF.Atan2(h.X, h.Z);
            return new Quaternion(0f, MathF.Sin(half), 0f, MathF.Cos(half));
        }

        /// <summary>Heading in degrees about +Y in (-180, 180]; Unity convention: 0 = facing +Z, +90 = facing +X.</summary>
        public static float YawDegrees(Quaternion rotation)
        {
            if (!TryHeading(rotation, Vector3.UnitY, out Vector3 h)) return 0f;
            return MathF.Atan2(h.X, h.Z) * Rad2Deg;
        }

        /// <summary>
        /// Same semantics as <c>UnityEngine.Quaternion.LookRotation</c>: forward maps to +Z, and +Y is aligned as
        /// closely as possible with <paramref name="up"/>. Zero forward gives identity; forward parallel to up falls
        /// back to the shortest rotation from +Z to forward (as Unity does).
        /// </summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            float fLen = forward.Length();
            if (!(fLen > Epsilon)) return Quaternion.Identity;
            Vector3 z = forward / fLen;

            Vector3 x = Vector3.Cross(up, z);
            float xLen = x.Length();
            if (!(xLen > Epsilon)) return FromToRotation(Vector3.UnitZ, z);
            x /= xLen;

            return FromBasis(x, Vector3.Cross(z, x), z);
        }

        /// <summary>
        /// Keeps the heading of <paramref name="rotation"/> (its forward projected onto the plane perpendicular to
        /// <paramref name="up"/>) and makes <paramref name="up"/> the up axis, i.e. removes tilt relative to that up.
        /// When forward is parallel to up the heading comes from the rotation's up vector instead; a zero
        /// <paramref name="up"/> falls back to world +Y.
        /// </summary>
        public static Quaternion LevelWithUp(Quaternion rotation, Vector3 up)
        {
            float upLen = up.Length();
            if (!(upLen > Epsilon)) return YawOnly(rotation);
            Vector3 u = up / upLen;

            if (!TryHeading(rotation, u, out Vector3 h))
            {
                // Degenerate rotation: pick any heading perpendicular to up.
                h = Vector3.Cross(MathF.Abs(u.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY, u);
                h = Vector3.Normalize(h);
            }

            // h and u are orthonormal: right = up x forward.
            return FromBasis(Vector3.Cross(u, h), u, h);
        }

        /// <summary>Shortest-arc rotation taking direction <paramref name="from"/> to direction <paramref name="to"/>.</summary>
        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            float fl = from.Length(), tl = to.Length();
            if (!(fl > Epsilon) || !(tl > Epsilon)) return Quaternion.Identity;
            Vector3 a = from / fl, b = to / tl;

            float d = Vector3.Dot(a, b);
            if (d < -0.999999f)
            {
                // Opposite directions: 180 degrees about an axis perpendicular to 'from' (for from = +Z this is +Y, a pure yaw).
                Vector3 axis = Vector3.Cross(a, Vector3.UnitX);
                if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(a, Vector3.UnitY);
                axis = Vector3.Normalize(axis);
                return new Quaternion(axis, 0f);
            }

            return Quaternion.Normalize(new Quaternion(Vector3.Cross(a, b), 1f + d));
        }

        /// <summary>
        /// Unit heading of the rotation's forward projected onto the plane perpendicular to unit <paramref name="up"/>.
        /// Near-parallel to up it uses the rotation's up vector: looking down along -up the top of the head points
        /// along the heading, looking up it points away from it.
        /// </summary>
        private static bool TryHeading(Quaternion rotation, Vector3 up, out Vector3 heading)
        {
            Vector3 f = Vector3.Transform(Vector3.UnitZ, rotation);
            float fUp = Vector3.Dot(f, up);
            Vector3 h = f - up * fUp;
            float lenSq = h.LengthSquared();

            if (!(lenSq >= DegenerateSq))
            {
                Vector3 headUp = Vector3.Transform(Vector3.UnitY, rotation);
                h = headUp - up * Vector3.Dot(headUp, up);
                if (fUp > 0f) h = -h;
                lenSq = h.LengthSquared();
                if (!(lenSq >= DegenerateSq))
                {
                    heading = default;
                    return false;
                }
            }

            heading = h / MathF.Sqrt(lenSq);
            return true;
        }

        /// <summary>Quaternion of the rotation whose columns are the orthonormal basis (x, y, z) (Shepperd's method).</summary>
        private static Quaternion FromBasis(Vector3 x, Vector3 y, Vector3 z)
        {
            // Column-vector rotation matrix: column 0 = x, column 1 = y, column 2 = z.
            float m00 = x.X, m10 = x.Y, m20 = x.Z;
            float m01 = y.X, m11 = y.Y, m21 = y.Z;
            float m02 = z.X, m12 = z.Y, m22 = z.Z;

            float trace = m00 + m11 + m22;
            Quaternion q;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
                q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
                q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
                q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
            return Quaternion.Normalize(q);
        }
    }
}
