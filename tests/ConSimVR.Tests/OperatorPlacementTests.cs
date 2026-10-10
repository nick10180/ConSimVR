using System;
using System.Numerics;
using ConSimVR.Core.Math;
using ConSimVR.Core.Rig;
using Xunit;

namespace ConSimVR.Tests
{
    public class OperatorPlacementTests
    {
        private static void Near(Vector3 expected, Vector3 actual) =>
            Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

        [Fact]
        public void Places_operator_on_the_camera_side_at_distance_and_target_height()
        {
            var target = new Vector3(10f, 2f, 10f);
            var camera = new Vector3(10f, 15f, -40f); // high and far behind (-Z)
            Near(new Vector3(10f, 2f, 3f), OperatorPlacement.PlaceAround(target, camera, 7f, 0f));
        }

        [Fact]
        public void Orbit_is_clockwise_seen_from_above()
        {
            // Unity yaw +90 turns -Z into -X.
            Near(new Vector3(-5f, 0f, 0f), OperatorPlacement.PlaceAround(Vector3.Zero, new Vector3(0f, 0f, -1f), 5f, 90f));
        }

        [Fact]
        public void Facing_is_level_and_points_at_the_target()
        {
            var rotation = OperatorPlacement.FacingYaw(new Vector3(0f, 1.7f, -5f), new Vector3(0f, 8f, 0f));
            Near(new Vector3(0f, 0f, 1f), Vector3.Transform(Vector3.UnitZ, rotation));
            Assert.InRange(MathF.Abs(VrRotation.YawDegrees(OperatorPlacement.FacingYaw(Vector3.Zero, new Vector3(3f, 0f, 0f))) - 90f), 0f, 1e-3f);
        }

        [Fact]
        public void Yaw_frame_round_trip_ignores_tilt()
        {
            var tilted = Quaternion.CreateFromYawPitchRoll(0.7f, 0.3f, -0.2f);
            var offset = new Vector3(4f, 0f, -2f);
            var local = OperatorPlacement.ToYawFrame(offset, tilted);
            Near(offset, OperatorPlacement.FromYawFrame(local, tilted));
            Assert.True(MathF.Abs(local.Y) < 1e-4f);
        }
    }
}
