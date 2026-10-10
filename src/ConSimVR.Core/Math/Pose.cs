using System.Globalization;
using System.Numerics;

namespace ConSimVR.Core.Math
{
    /// <summary>
    /// Rigid transform (position + unit rotation) in Unity convention (left-handed, +X right, +Y up, +Z forward).
    /// </summary>
    public readonly struct Pose
    {
        public readonly Vector3 Position;
        /// <summary>Expected to be normalized.</summary>
        public readonly Quaternion Rotation;

        public Pose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static Pose Identity => new Pose(Vector3.Zero, Quaternion.Identity);

        /// <summary>Composes <paramref name="child"/> (expressed in <paramref name="parent"/>'s space) into parent's space.</summary>
        public static Pose operator *(Pose parent, Pose child)
        {
            return new Pose(
                parent.Position + Vector3.Transform(child.Position, parent.Rotation),
                parent.Rotation * child.Rotation);
        }

        public Pose Inverse()
        {
            Quaternion inv = Quaternion.Conjugate(Rotation);
            return new Pose(Vector3.Transform(-Position, inv), inv);
        }

        public Vector3 TransformPoint(Vector3 p) => Position + Vector3.Transform(p, Rotation);

        public Vector3 TransformDirection(Vector3 d) => Vector3.Transform(d, Rotation);

        /// <summary>Rotation * (0, 0, 1).</summary>
        public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, Rotation);

        /// <summary>Rotation * (0, 1, 0).</summary>
        public Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

        /// <summary>Rotation * (1, 0, 0).</summary>
        public Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);

        /// <summary>TRS matrix with unit scale.</summary>
        public Matrix4 ToMatrix() => Matrix4.TRS(Position, Rotation, Vector3.One);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Pose(pos=({0:F4}, {1:F4}, {2:F4}), rot=({3:F5}, {4:F5}, {5:F5}, {6:F5}))",
                Position.X, Position.Y, Position.Z, Rotation.X, Rotation.Y, Rotation.Z, Rotation.W);
        }
    }
}
