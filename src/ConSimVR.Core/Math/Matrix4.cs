using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ConSimVR.Core.Math
{
    /// <summary>
    /// 4x4 float matrix with exactly the in-memory layout of <c>UnityEngine.Matrix4x4</c> (column-major fields,
    /// <c>mRC</c> = row R, column C), so it can be bit-cast to and from Unity's type.
    /// Column-vector convention: <c>M * v</c>, and <c>A * B</c> applies B first, then A (same as Unity).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Matrix4
    {
        // Declaration order is load-bearing: it must match UnityEngine.Matrix4x4 (column-major).
        public float m00, m10, m20, m30;
        public float m01, m11, m21, m31;
        public float m02, m12, m22, m32;
        public float m03, m13, m23, m33;

        /// <summary>Element at <paramref name="row"/>, <paramref name="column"/> (both 0..3).</summary>
        public float this[int row, int column]
        {
            get
            {
                if ((uint)row > 3u || (uint)column > 3u) throw new IndexOutOfRangeException("Invalid matrix index.");
                return this[row + column * 4];
            }
            set
            {
                if ((uint)row > 3u || (uint)column > 3u) throw new IndexOutOfRangeException("Invalid matrix index.");
                this[row + column * 4] = value;
            }
        }

        /// <summary>Element by column-major index (0..15), same as Unity's <c>Matrix4x4[int]</c>.</summary>
        public unsafe float this[int index]
        {
            get
            {
                if ((uint)index >= 16u) throw new IndexOutOfRangeException("Invalid matrix index.");
                fixed (float* p = &m00) return p[index];
            }
            set
            {
                if ((uint)index >= 16u) throw new IndexOutOfRangeException("Invalid matrix index.");
                fixed (float* p = &m00) p[index] = value;
            }
        }

        public static Matrix4 Identity => new Matrix4 { m00 = 1f, m11 = 1f, m22 = 1f, m33 = 1f };

        /// <summary>Standard matrix product (column-vector convention: the result applies <paramref name="b"/> first).</summary>
        public static Matrix4 operator *(Matrix4 a, Matrix4 b)
        {
            Matrix4 r;
            r.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20 + a.m03 * b.m30;
            r.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21 + a.m03 * b.m31;
            r.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22 + a.m03 * b.m32;
            r.m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03 * b.m33;

            r.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20 + a.m13 * b.m30;
            r.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31;
            r.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32;
            r.m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33;

            r.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20 + a.m23 * b.m30;
            r.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31;
            r.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32;
            r.m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33;

            r.m30 = a.m30 * b.m00 + a.m31 * b.m10 + a.m32 * b.m20 + a.m33 * b.m30;
            r.m31 = a.m30 * b.m01 + a.m31 * b.m11 + a.m32 * b.m21 + a.m33 * b.m31;
            r.m32 = a.m30 * b.m02 + a.m31 * b.m12 + a.m32 * b.m22 + a.m33 * b.m32;
            r.m33 = a.m30 * b.m03 + a.m31 * b.m13 + a.m32 * b.m23 + a.m33 * b.m33;
            return r;
        }

        /// <summary>Returns <c>M * v</c>.</summary>
        public Vector4 Multiply(Vector4 v)
        {
            return new Vector4(
                m00 * v.X + m01 * v.Y + m02 * v.Z + m03 * v.W,
                m10 * v.X + m11 * v.Y + m12 * v.Z + m13 * v.W,
                m20 * v.X + m21 * v.Y + m22 * v.Z + m23 * v.W,
                m30 * v.X + m31 * v.Y + m32 * v.Z + m33 * v.W);
        }

        /// <summary>Transforms a point including the projective divide (like Unity's <c>MultiplyPoint</c>).</summary>
        public Vector3 MultiplyPoint(Vector3 p)
        {
            float x = m00 * p.X + m01 * p.Y + m02 * p.Z + m03;
            float y = m10 * p.X + m11 * p.Y + m12 * p.Z + m13;
            float z = m20 * p.X + m21 * p.Y + m22 * p.Z + m23;
            float w = 1f / (m30 * p.X + m31 * p.Y + m32 * p.Z + m33);
            return new Vector3(x * w, y * w, z * w);
        }

        /// <summary>Transforms a point by the affine 3x4 part only (no projective divide).</summary>
        public Vector3 MultiplyPoint3x4(Vector3 p)
        {
            return new Vector3(
                m00 * p.X + m01 * p.Y + m02 * p.Z + m03,
                m10 * p.X + m11 * p.Y + m12 * p.Z + m13,
                m20 * p.X + m21 * p.Y + m22 * p.Z + m23);
        }

        /// <summary>Transforms a direction by the 3x3 part only (no translation).</summary>
        public Vector3 MultiplyVector(Vector3 v)
        {
            return new Vector3(
                m00 * v.X + m01 * v.Y + m02 * v.Z,
                m10 * v.X + m11 * v.Y + m12 * v.Z,
                m20 * v.X + m21 * v.Y + m22 * v.Z);
        }

        /// <summary>
        /// General 4x4 inverse (cofactor expansion). Like Unity, returns the all-zero matrix when the matrix is singular.
        /// </summary>
        public Matrix4 Inverse()
        {
            float s0 = m00 * m11 - m10 * m01;
            float s1 = m00 * m12 - m10 * m02;
            float s2 = m00 * m13 - m10 * m03;
            float s3 = m01 * m12 - m11 * m02;
            float s4 = m01 * m13 - m11 * m03;
            float s5 = m02 * m13 - m12 * m03;

            float c5 = m22 * m33 - m32 * m23;
            float c4 = m21 * m33 - m31 * m23;
            float c3 = m21 * m32 - m31 * m22;
            float c2 = m20 * m33 - m30 * m23;
            float c1 = m20 * m32 - m30 * m22;
            float c0 = m20 * m31 - m30 * m21;

            float det = s0 * c5 - s1 * c4 + s2 * c3 + s3 * c2 - s4 * c1 + s5 * c0;
            if (det == 0f || !float.IsFinite(det)) return default;
            float k = 1f / det;

            Matrix4 r;
            r.m00 = (m11 * c5 - m12 * c4 + m13 * c3) * k;
            r.m01 = (-m01 * c5 + m02 * c4 - m03 * c3) * k;
            r.m02 = (m31 * s5 - m32 * s4 + m33 * s3) * k;
            r.m03 = (-m21 * s5 + m22 * s4 - m23 * s3) * k;

            r.m10 = (-m10 * c5 + m12 * c2 - m13 * c1) * k;
            r.m11 = (m00 * c5 - m02 * c2 + m03 * c1) * k;
            r.m12 = (-m30 * s5 + m32 * s2 - m33 * s1) * k;
            r.m13 = (m20 * s5 - m22 * s2 + m23 * s1) * k;

            r.m20 = (m10 * c4 - m11 * c2 + m13 * c0) * k;
            r.m21 = (-m00 * c4 + m01 * c2 - m03 * c0) * k;
            r.m22 = (m30 * s4 - m31 * s2 + m33 * s0) * k;
            r.m23 = (-m20 * s4 + m21 * s2 - m23 * s0) * k;

            r.m30 = (-m10 * c3 + m11 * c1 - m12 * c0) * k;
            r.m31 = (m00 * c3 - m01 * c1 + m02 * c0) * k;
            r.m32 = (-m30 * s3 + m31 * s1 - m32 * s0) * k;
            r.m33 = (m20 * s3 - m21 * s1 + m22 * s0) * k;
            return r;
        }

        /// <summary>Translation * Rotation * Scale, identical to Unity's <c>Matrix4x4.TRS</c>.</summary>
        public static Matrix4 TRS(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Matrix4 m = Rotate(rotation);
            m.m00 *= scale.X; m.m10 *= scale.X; m.m20 *= scale.X;
            m.m01 *= scale.Y; m.m11 *= scale.Y; m.m21 *= scale.Y;
            m.m02 *= scale.Z; m.m12 *= scale.Z; m.m22 *= scale.Z;
            m.m03 = position.X;
            m.m13 = position.Y;
            m.m23 = position.Z;
            return m;
        }

        public static Matrix4 Scale(Vector3 scale)
        {
            return new Matrix4 { m00 = scale.X, m11 = scale.Y, m22 = scale.Z, m33 = 1f };
        }

        public static Matrix4 Translate(Vector3 translation)
        {
            Matrix4 m = Identity;
            m.m03 = translation.X;
            m.m13 = translation.Y;
            m.m23 = translation.Z;
            return m;
        }

        /// <summary>Rotation matrix of a (unit) quaternion; <c>Rotate(q).MultiplyVector(v) == Vector3.Transform(v, q)</c>.</summary>
        public static Matrix4 Rotate(Quaternion rotation)
        {
            float x = rotation.X, y = rotation.Y, z = rotation.Z, w = rotation.W;
            float x2 = x + x, y2 = y + y, z2 = z + z;
            float xx = x * x2, yy = y * y2, zz = z * z2;
            float xy = x * y2, xz = x * z2, yz = y * z2;
            float wx = w * x2, wy = w * y2, wz = w * z2;

            Matrix4 m;
            m.m00 = 1f - (yy + zz); m.m01 = xy - wz;         m.m02 = xz + wy;         m.m03 = 0f;
            m.m10 = xy + wz;        m.m11 = 1f - (xx + zz);  m.m12 = yz - wx;         m.m13 = 0f;
            m.m20 = xz - wy;        m.m21 = yz + wx;         m.m22 = 1f - (xx + yy);  m.m23 = 0f;
            m.m30 = 0f;             m.m31 = 0f;              m.m32 = 0f;              m.m33 = 1f;
            return m;
        }

        /// <summary>Perspective projection, identical to Unity's <c>Matrix4x4.Frustum</c> (OpenGL clip space, z in [-1, 1]).</summary>
        public static Matrix4 Frustum(float left, float right, float bottom, float top, float near, float far)
        {
            float rl = right - left;
            float tb = top - bottom;
            float fn = far - near;
            Matrix4 m = default;
            m.m00 = 2f * near / rl;
            m.m02 = (right + left) / rl;
            m.m11 = 2f * near / tb;
            m.m12 = (top + bottom) / tb;
            m.m22 = -(far + near) / fn;
            m.m23 = -(2f * far * near) / fn;
            m.m32 = -1f;
            return m;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[{0:F5} {1:F5} {2:F5} {3:F5}] [{4:F5} {5:F5} {6:F5} {7:F5}] [{8:F5} {9:F5} {10:F5} {11:F5}] [{12:F5} {13:F5} {14:F5} {15:F5}]",
                m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33);
        }
    }
}
