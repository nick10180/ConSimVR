namespace ConSimVR.Core.Math
{
    /// <summary>
    /// Field of view in radians with OpenXR <c>XrFovf</c> semantics (left/down negative for a typical HMD).
    /// The OpenXR to Unity conversion only flips Z, so the same angles apply unchanged to a Unity eye
    /// (left stays left, up stays up).
    /// </summary>
    public struct Fov
    {
        public float AngleLeft, AngleRight, AngleUp, AngleDown;

        public Fov(float angleLeft, float angleRight, float angleUp, float angleDown)
        {
            AngleLeft = angleLeft;
            AngleRight = angleRight;
            AngleUp = angleUp;
            AngleDown = angleDown;
        }

        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Fov(L={0:F4}, R={1:F4}, U={2:F4}, D={3:F4})", AngleLeft, AngleRight, AngleUp, AngleDown);
        }
    }
}
