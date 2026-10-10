using System;
using System.Numerics;
using ConSimVR.Core.Math;
using ConSimVR.Core.Rig;
using Xunit;

namespace ConSimVR.Tests
{
    public class UiPanelProjectionTests
    {
        // 1.6 m x 0.9 m panel, 1.4 m in front of an eye at the origin, 1600x900 screen.
        private static readonly Pose Panel = new(new Vector3(0f, 0f, 1.4f), Quaternion.Identity);

        private static Vector3 Project(Vector3 target, Vector3? eye = null) =>
            UiPanelProjection.ToScreen(Panel, 1.6f, 0.9f, eye ?? Vector3.Zero, target, 1600, 900);

        [Fact]
        public void Straight_ahead_lands_in_the_centre_whatever_the_distance()
        {
            var near = Project(new Vector3(0f, 0f, 5f));
            var far = Project(new Vector3(0f, 0f, 500f));
            Assert.Equal(800f, near.X, 2);
            Assert.Equal(450f, near.Y, 2);
            Assert.Equal(800f, far.X, 2);
            Assert.True(far.Z > 0f);
        }

        [Fact]
        public void Direction_maps_through_the_panel_plane()
        {
            // 0.4 m right per 1.4 m forward hits the panel 0.4 m right of centre = a quarter width.
            var p = Project(new Vector3(40f, 0f, 140f));
            Assert.Equal(1200f, p.X, 1);
            Assert.Equal(450f, p.Y, 1);
        }

        [Fact]
        public void Moving_the_head_shifts_the_marker_like_parallax()
        {
            var centred = Project(new Vector3(0f, 0f, 100f));
            var shifted = Project(new Vector3(0f, 0f, 100f), new Vector3(0.1f, 0f, 0f));
            Assert.True(shifted.X > centred.X); // the eye moved right; the far target still projects ~0.1 m right on the panel
        }

        [Fact]
        public void Behind_the_viewer_reports_negative_depth_and_mirrors()
        {
            var behindRight = Project(new Vector3(3f, 0f, -10f));
            Assert.True(behindRight.Z < 0f);
            Assert.True(behindRight.X < 800f); // pinhole mirror, like Camera.WorldToScreenPoint
        }
    }
}
