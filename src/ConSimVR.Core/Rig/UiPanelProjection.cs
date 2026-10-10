using System.Numerics;
using ConSimVR.Core.Math;

namespace ConSimVR.Core.Rig
{
    /// <summary>
    /// Where a world point appears on the floating VR UI panel, seen from the viewer's eye. Used to place HUD markers
    /// that the game positions with Camera.WorldToScreenPoint: the panel shows the desktop screen, so returning panel
    /// pixel coordinates puts the marker exactly over the target as the player sees it.
    /// Semantics match WorldToScreenPoint: pixels from the bottom-left, z is the distance in front of the viewer
    /// (negative behind, with x/y mirrored through the centre like a pinhole projection).
    /// </summary>
    public static class UiPanelProjection
    {
        /// <param name="panel">Panel centre and orientation in world space (Unity convention, panel faces -Z towards the viewer).</param>
        /// <param name="widthMeters">Panel width in world units.</param>
        /// <param name="heightMeters">Panel height in world units.</param>
        /// <param name="eye">Viewer position in world space.</param>
        /// <param name="target">World point to place.</param>
        public static Vector3 ToScreen(Pose panel, float widthMeters, float heightMeters, Vector3 eye, Vector3 target, int screenWidth, int screenHeight)
        {
            var inverse = Quaternion.Inverse(panel.Rotation);
            var eyeLocal = Vector3.Transform(eye - panel.Position, inverse);
            var direction = Vector3.Transform(target - eye, inverse);

            float dz = direction.Z;
            if (dz > -1e-5f && dz < 1e-5f)
                dz = dz < 0f ? -1e-5f : 1e-5f;
            float t = -eyeLocal.Z / dz;
            float x = eyeLocal.X + direction.X * t;
            float y = eyeLocal.Y + direction.Y * t;

            return new Vector3(
                (x / widthMeters + 0.5f) * screenWidth,
                (y / heightMeters + 0.5f) * screenHeight,
                direction.Z);
        }
    }
}
