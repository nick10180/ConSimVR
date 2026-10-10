namespace ConSimVR.Core.Math
{
    /// <summary>Recentering: re-express tracked poses relative to a yaw-only reference taken from the head.</summary>
    public static class Recenter
    {
        /// <summary>Reference pose for recentering: the head position with only its heading (no pitch/roll).</summary>
        public static Pose ReferenceFromHead(Pose headTracking)
        {
            return new Pose(headTracking.Position, VrRotation.YawOnly(headTracking.Rotation));
        }

        /// <summary>Expresses a tracking-space pose relative to <paramref name="reference"/>: reference.Inverse() * tracked.</summary>
        public static Pose Apply(Pose reference, Pose tracked)
        {
            return reference.Inverse() * tracked;
        }
    }
}
