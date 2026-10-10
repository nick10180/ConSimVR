using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ConSimVR.Core.Math;
using Xunit;

namespace ConSimVR.Tests
{
    public class VrMathTests
    {
        private const float Eps = 1e-4f;
        private const float Deg2Rad = MathF.PI / 180f;

        // ---------- Matrix4 ----------

        [Fact]
        public void Matrix4_LayoutMatchesUnityMatrix4x4()
        {
            Assert.Equal(64, Unsafe.SizeOf<Matrix4>());
            Assert.Equal(0, (int)Marshal.OffsetOf<Matrix4>(nameof(Matrix4.m00)));
            Assert.Equal(4, (int)Marshal.OffsetOf<Matrix4>(nameof(Matrix4.m10)));
            Assert.Equal(16, (int)Marshal.OffsetOf<Matrix4>(nameof(Matrix4.m01)));
            Assert.Equal(60, (int)Marshal.OffsetOf<Matrix4>(nameof(Matrix4.m33)));

            var m = new Matrix4();
            m[1, 0] = 5f;
            m[2, 3] = 7f;
            Assert.Equal(5f, m.m10);
            Assert.Equal(7f, m.m23);
            Assert.Equal(7f, m[14]); // column-major index row + 4 * column
        }

        [Fact]
        public void Matrix4_TrsInverseRoundTrip()
        {
            Matrix4 trs = Matrix4.TRS(new Vector3(1.5f, -2f, 3f), Euler(20f, 35f, -10f), new Vector3(2f, 0.5f, 1.25f));
            AssertNear(Matrix4.Identity, trs * trs.Inverse());
            AssertNear(Matrix4.Identity, trs.Inverse() * trs);

            var p = new Vector3(0.3f, 4f, -7f);
            AssertNear(p, trs.Inverse().MultiplyPoint3x4(trs.MultiplyPoint3x4(p)));

            // TRS == Translate * Rotate * Scale.
            AssertNear(Matrix4.Translate(new Vector3(1.5f, -2f, 3f)) * Matrix4.Rotate(Euler(20f, 35f, -10f)) * Matrix4.Scale(new Vector3(2f, 0.5f, 1.25f)), trs);

            // General (non-affine) inverse.
            Matrix4 proj = Matrix4.Frustum(-0.12f, 0.08f, -0.09f, 0.1f, 0.1f, 100f);
            AssertNear(Matrix4.Identity, proj * proj.Inverse());
        }

        [Fact]
        public void Matrix4_ProductMatchesPoseComposition()
        {
            var a = new Pose(new Vector3(1f, 2f, 3f), Euler(10f, 50f, 5f));
            var b = new Pose(new Vector3(-0.5f, 0.25f, 2f), Euler(-30f, -20f, 15f));

            AssertNear(a.ToMatrix() * b.ToMatrix(), (a * b).ToMatrix());

            var p = new Vector3(0.7f, -1.1f, 2.2f);
            AssertNear(a.TransformPoint(b.TransformPoint(p)), (a * b).TransformPoint(p));
            AssertNear(a.TransformPoint(p), a.ToMatrix().MultiplyPoint3x4(p));
            AssertNear(a.TransformDirection(p), a.ToMatrix().MultiplyVector(p));
            AssertPoseNear(Pose.Identity, a.Inverse() * a);
            AssertNear(a.ToMatrix().Inverse(), a.Inverse().ToMatrix());

            // Basis vectors match Unity's forward/up/right.
            var yaw90 = new Pose(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 90f * Deg2Rad));
            AssertNear(Vector3.UnitX, yaw90.Forward);
            AssertNear(-Vector3.UnitZ, yaw90.Right);
            AssertNear(Vector3.UnitY, yaw90.Up);
        }

        // ---------- XrConvert ----------

        [Fact]
        public void XrConvert_LeftTurnAboutYStaysALeftTurn()
        {
            // OpenXR +90 degrees about +Y turns left: forward -Z becomes -X.
            Quaternion xr = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 90f * Deg2Rad);
            AssertNear(new Vector3(-1f, 0f, 0f), Vector3.Transform(new Vector3(0f, 0f, -1f), xr));

            Pose unity = XrConvert.ToUnity(xr, Vector3.Zero);
            AssertNear(new Vector3(-1f, 0f, 0f), Vector3.Transform(Vector3.UnitZ, unity.Rotation));
            AssertNear(-90f, VrRotation.YawDegrees(unity.Rotation), 1e-3f);
        }

        [Fact]
        public void XrConvert_RoundTripAndZFlip()
        {
            var xrPos = new Vector3(0.1f, 1.6f, -0.4f);
            Quaternion xrRot = Quaternion.Normalize(new Quaternion(0.1f, -0.3f, 0.2f, 0.9f));

            Pose unity = XrConvert.ToUnity(xrRot, xrPos);
            AssertNear(new Vector3(0.1f, 1.6f, 0.4f), unity.Position);

            XrConvert.ToOpenXr(unity, out Quaternion backRot, out Vector3 backPos);
            Assert.Equal(xrPos, backPos);
            Assert.Equal(xrRot, backRot);

            // Converting the transformed point equals transforming the converted point.
            var v = new Vector3(0.3f, -0.2f, -1.5f);
            Vector3 xrWorld = xrPos + Vector3.Transform(v, xrRot);
            AssertNear(new Vector3(xrWorld.X, xrWorld.Y, -xrWorld.Z), unity.TransformPoint(new Vector3(v.X, v.Y, -v.Z)));
        }

        // ---------- Projection / view ----------

        [Fact]
        public void Projection_Symmetric90MatchesFrustumFormula()
        {
            const float n = 0.1f, f = 1000f;
            float q = MathF.PI / 4f;
            Matrix4 p = VrProjection.Projection(new Fov(-q, q, q, -q), n, f);

            var expected = new Matrix4
            {
                m00 = 1f, m11 = 1f,
                m22 = -(f + n) / (f - n), m23 = -2f * f * n / (f - n),
                m32 = -1f,
            };
            AssertNear(expected, p);
            AssertNear(Matrix4.Frustum(-n, n, -n, n, n, f), p);
        }

        [Fact]
        public void Projection_AsymmetricFovEdgesMapToNdcBounds()
        {
            const float n = 0.05f, f = 500f;
            var fov = new Fov(-0.87f, 0.75f, 0.80f, -0.90f);
            Matrix4 p = VrProjection.Projection(fov, n, f);

            float l = n * MathF.Tan(fov.AngleLeft), r = n * MathF.Tan(fov.AngleRight);
            float b = n * MathF.Tan(fov.AngleDown), t = n * MathF.Tan(fov.AngleUp);
            AssertNear(2f * n / (r - l), p.m00, 1e-5f);
            AssertNear((r + l) / (r - l), p.m02, 1e-5f);
            AssertNear(2f * n / (t - b), p.m11, 1e-5f);
            AssertNear((t + b) / (t - b), p.m12, 1e-5f);
            Assert.Equal(-1f, p.m32);

            // Through the full Unity pipeline: eye pose -> worldToCamera -> projection.
            var eye = new Pose(new Vector3(2f, 1.5f, -3f), Euler(15f, -40f, 5f));
            Matrix4 viewProj = p * VrProjection.WorldToCamera(eye);
            const float depth = 5f;
            Vector3 Ndc(float x, float y, float z) => viewProj.MultiplyPoint(eye.TransformPoint(new Vector3(x, y, z)));

            AssertNear(-1f, Ndc(depth * MathF.Tan(fov.AngleLeft), 0f, depth).X, 1e-4f);
            AssertNear(1f, Ndc(depth * MathF.Tan(fov.AngleRight), 0f, depth).X, 1e-4f);
            AssertNear(-1f, Ndc(0f, depth * MathF.Tan(fov.AngleDown), depth).Y, 1e-4f);
            AssertNear(1f, Ndc(0f, depth * MathF.Tan(fov.AngleUp), depth).Y, 1e-4f);
            AssertNear(-1f, Ndc(0f, 0f, n).Z, 1e-3f);
            AssertNear(1f, Ndc(0f, 0f, f).Z, 1e-3f);
        }

        [Fact]
        public void WorldToCamera_FollowsUnityConvention()
        {
            AssertNear(Matrix4.Scale(new Vector3(1f, 1f, -1f)), VrProjection.WorldToCamera(Pose.Identity));

            var eye = new Pose(new Vector3(-0.032f, 1.7f, 0.5f), Euler(-10f, 60f, 3f));
            Matrix4 view = VrProjection.WorldToCamera(eye);
            AssertNear(new Vector3(0f, 0f, -1f), view.MultiplyPoint3x4(eye.TransformPoint(Vector3.UnitZ)));
            AssertNear(new Vector3(1f, 0f, 0f), view.MultiplyPoint3x4(eye.TransformPoint(Vector3.UnitX)));
            AssertNear(Matrix4.Scale(new Vector3(1f, 1f, -1f)) * eye.ToMatrix().Inverse(), view);
        }

        // ---------- VrRotation / Recenter ----------

        [Fact]
        public void YawOnly_RemovesPitchAndRoll()
        {
            AssertRotationNear(Yaw(30f), VrRotation.YawOnly(Euler(25f, 30f, -15f)));
            AssertRotationNear(Yaw(-120f), VrRotation.YawOnly(Euler(-40f, -120f, 10f)));

            // Straight down / straight up (gimbal lock for the forward vector) still keeps the heading.
            AssertRotationNear(Yaw(30f), VrRotation.YawOnly(Euler(90f, 30f, 0f)));
            AssertRotationNear(Yaw(30f), VrRotation.YawOnly(Euler(-90f, 30f, 0f)));

            AssertNear(30f, VrRotation.YawDegrees(Euler(25f, 30f, -15f)), 1e-3f);
            Assert.Equal(0f, VrRotation.YawOnly(Euler(25f, 30f, -15f)).X);
            Assert.Equal(0f, VrRotation.YawOnly(Euler(25f, 30f, -15f)).Z);
        }

        [Fact]
        public void LookRotation_MatchesUnity()
        {
            Quaternion right = VrRotation.LookRotation(Vector3.UnitX, Vector3.UnitY);
            AssertRotationNear(new Quaternion(0f, MathF.Sqrt(0.5f), 0f, MathF.Sqrt(0.5f)), right);
            AssertNear(90f, VrRotation.YawDegrees(right), 1e-3f);

            AssertRotationNear(Quaternion.Identity, VrRotation.LookRotation(Vector3.UnitZ * 3f, Vector3.UnitY));
            // Unity: LookRotation(forward, right) == Euler(0, 0, -90).
            AssertRotationNear(Euler(0f, 0f, -90f), VrRotation.LookRotation(Vector3.UnitZ, Vector3.UnitX));

            // Reconstructs an arbitrary rotation from its forward and up.
            Quaternion q = Euler(33f, -71f, 12f);
            AssertRotationNear(q, VrRotation.LookRotation(Vector3.Transform(Vector3.UnitZ, q), Vector3.Transform(Vector3.UnitY, q)));

            // Degenerate inputs: zero forward -> identity; forward parallel to up -> from-to rotation of +Z.
            AssertRotationNear(Quaternion.Identity, VrRotation.LookRotation(Vector3.Zero, Vector3.UnitY));
            AssertNear(Vector3.UnitY, Vector3.Transform(Vector3.UnitZ, VrRotation.LookRotation(Vector3.UnitY, Vector3.UnitY)));
        }

        [Fact]
        public void LevelWithUp_UsesTiltedUpAndKeepsHeading()
        {
            Vector3 up = Vector3.Normalize(new Vector3(0.3f, 1f, 0.1f));
            Quaternion head = Euler(30f, 40f, 20f);
            Quaternion level = VrRotation.LevelWithUp(head, up);

            Vector3 f = Vector3.Transform(Vector3.UnitZ, head);
            Vector3 expectedForward = Vector3.Normalize(f - up * Vector3.Dot(f, up));
            AssertNear(up, Vector3.Transform(Vector3.UnitY, level));
            AssertNear(expectedForward, Vector3.Transform(Vector3.UnitZ, level));
            AssertNear(1f, level.Length(), 1e-5f);

            // Looking straight along 'up': heading falls back to the head's up vector.
            AssertRotationNear(Yaw(40f), VrRotation.LevelWithUp(Euler(-90f, 40f, 0f), Vector3.UnitY));
            AssertRotationNear(Yaw(40f), VrRotation.LevelWithUp(Euler(90f, 40f, 0f), Vector3.UnitY));
        }

        [Fact]
        public void Recenter_HeadMapsToOriginFacingForward()
        {
            var head = new Pose(new Vector3(0.4f, 1.7f, -0.3f), Euler(12f, 30f, 0f));
            Pose reference = Recenter.ReferenceFromHead(head);
            AssertNear(head.Position, reference.Position);
            AssertRotationNear(Yaw(30f), reference.Rotation);

            Pose recentered = Recenter.Apply(reference, head);
            AssertNear(Vector3.Zero, recentered.Position);
            AssertNear(0f, VrRotation.YawDegrees(recentered.Rotation), 1e-3f);
            AssertRotationNear(Euler(12f, 0f, 0f), recentered.Rotation); // pitch is kept

            // Stepping 1 m along the recentered heading lands on +Z.
            var stepped = new Pose(head.Position + Vector3.Transform(Vector3.UnitZ, Yaw(30f)), head.Rotation);
            AssertNear(Vector3.UnitZ, Recenter.Apply(reference, stepped).Position);
        }

        // ---------- CullingFrustum ----------

        [Theory]
        [InlineData(0f)]
        [InlineData(5f)]
        public void CullingFrustum_ContainsBothEyeFrusta(float cantDegrees)
        {
            const float near = 0.05f, far = 1000f;
            var head = new Pose(new Vector3(3f, 1.7f, -2f), Euler(-15f, 70f, 8f));
            Pose leftEye = head * new Pose(new Vector3(-0.032f, 0f, 0f), Yaw(-cantDegrees));
            Pose rightEye = head * new Pose(new Vector3(0.032f, 0f, 0f), Yaw(cantDegrees));
            var leftFov = new Fov(-0.87f, 0.75f, 0.80f, -0.90f);
            var rightFov = new Fov(-0.75f, 0.87f, 0.80f, -0.90f);

            Assert.True(CullingFrustum.TryCombine(leftEye, leftFov, rightEye, rightFov, near, far,
                out Pose cullPose, out Matrix4 cullProj, out float cullNear, out float cullFar));

            float t = Vector3.Distance(cullPose.Position, (leftEye.Position + rightEye.Position) * 0.5f);
            Assert.InRange(t, 0f, 0.05f); // pushed back only a few centimetres
            Assert.True(cullNear > 0f && cullNear <= near + t + 1e-5f);
            Assert.True(cullFar >= far + t - 1e-3f);
            if (cantDegrees == 0f) AssertNear(near + t, cullNear, 2e-5f); // parallel eyes: exactly the spec's near + t

            Matrix4 cullViewProj = cullProj * VrProjection.WorldToCamera(cullPose);
            AssertEyeInside(leftEye, leftFov, near, far, cullViewProj);
            AssertEyeInside(rightEye, rightFov, near, far, cullViewProj);
        }

        [Fact]
        public void CullingFrustum_DegenerateInputReturnsFalse()
        {
            var fov = new Fov(-0.8f, 0.8f, 0.8f, -0.8f);
            var left = new Pose(new Vector3(-0.032f, 0f, 0f), Quaternion.Identity);
            var right = new Pose(new Vector3(0.032f, 0f, 0f), Quaternion.Identity);

            // Eyes looking in opposite directions: edge directions point backwards in the combined orientation.
            var rightBackwards = new Pose(right.Position, Yaw(180f));
            Assert.False(CullingFrustum.TryCombine(left, fov, rightBackwards, fov, 0.1f, 100f, out _, out _, out _, out _));
            // Combined frustum entirely to one side (L >= 0).
            var rightOnly = new Fov(0.1f, 0.8f, 0.8f, -0.8f);
            Assert.False(CullingFrustum.TryCombine(left, rightOnly, right, rightOnly, 0.1f, 100f, out _, out _, out _, out _));
            // Inverted FOV and bad clip planes.
            Assert.False(CullingFrustum.TryCombine(left, new Fov(0.8f, -0.8f, 0.8f, -0.8f), right, fov, 0.1f, 100f, out _, out _, out _, out _));
            Assert.False(CullingFrustum.TryCombine(left, fov, right, fov, 0f, 100f, out _, out _, out _, out _));
            Assert.False(CullingFrustum.TryCombine(left, fov, right, fov, 1f, 0.5f, out _, out _, out _, out _));
            // Sanity: the same eyes with valid input succeed.
            Assert.True(CullingFrustum.TryCombine(left, fov, right, fov, 0.1f, 100f, out _, out _, out _, out _));
        }

        private static void AssertEyeInside(Pose eye, Fov fov, float near, float far, Matrix4 cullViewProj)
        {
            const float ndcEps = 1e-3f;
            float tl = MathF.Tan(fov.AngleLeft), tr = MathF.Tan(fov.AngleRight);
            float td = MathF.Tan(fov.AngleDown), tu = MathF.Tan(fov.AngleUp);
            float[] depths = { near, near * 1.01f, 0.1f, 0.3f, 1f, 3.7f, 25f, 180f, far * 0.999f, far };
            const int steps = 8;

            foreach (float depth in depths)
            {
                for (int i = 0; i <= steps; i++)
                {
                    for (int j = 0; j <= steps; j++)
                    {
                        float x = tl + (tr - tl) * i / steps;
                        float y = td + (tu - td) * j / steps;
                        Vector3 world = eye.TransformPoint(new Vector3(x, y, 1f) * depth);
                        Vector4 clip = cullViewProj.Multiply(new Vector4(world, 1f));
                        Assert.True(clip.W > 0f, $"behind culling camera: depth={depth} i={i} j={j}");
                        Vector3 ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
                        Assert.True(MathF.Abs(ndc.X) <= 1f + ndcEps && MathF.Abs(ndc.Y) <= 1f + ndcEps && MathF.Abs(ndc.Z) <= 1f + ndcEps,
                            $"outside culling frustum: ndc={ndc} depth={depth} i={i} j={j}");
                    }
                }
            }
        }

        // ---------- helpers ----------

        /// <summary>Unity's Quaternion.Euler(x, y, z) in degrees: roll about Z, then pitch about X, then yaw about Y.</summary>
        private static Quaternion Euler(float pitch, float yaw, float roll)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * Deg2Rad)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch * Deg2Rad)
                 * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll * Deg2Rad);
        }

        private static Quaternion Yaw(float degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * Deg2Rad);

        private static void AssertNear(float expected, float actual, float eps)
        {
            Assert.True(MathF.Abs(expected - actual) <= eps, $"Expected {expected}, got {actual}");
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, float eps = Eps)
        {
            Assert.True((expected - actual).Length() <= eps, $"Expected {expected}, got {actual}");
        }

        private static void AssertRotationNear(Quaternion expected, Quaternion actual, float eps = Eps)
        {
            if (Quaternion.Dot(expected, actual) < 0f) actual = -actual; // q and -q are the same rotation
            bool ok = MathF.Abs(expected.X - actual.X) <= eps && MathF.Abs(expected.Y - actual.Y) <= eps
                   && MathF.Abs(expected.Z - actual.Z) <= eps && MathF.Abs(expected.W - actual.W) <= eps;
            Assert.True(ok, $"Expected {expected}, got {actual}");
        }

        private static void AssertPoseNear(Pose expected, Pose actual)
        {
            AssertNear(expected.Position, actual.Position);
            AssertRotationNear(expected.Rotation, actual.Rotation);
        }

        private static void AssertNear(Matrix4 expected, Matrix4 actual, float eps = Eps)
        {
            for (int i = 0; i < 16; i++)
            {
                float e = expected[i], a = actual[i];
                Assert.True(MathF.Abs(e - a) <= eps * MathF.Max(1f, MathF.Abs(e)), $"Element {i}: expected {expected}, got {actual}");
            }
        }
    }
}
