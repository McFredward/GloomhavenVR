using System;
namespace UnityEngine
{
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal static Vector3 one => new(1, 1, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public override string ToString() => $"({x}, {y}, {z})";
    }
    internal struct Quaternion
    {
        internal System.Numerics.Quaternion Value;
        internal static Quaternion Yaw(float angle) => new() { Value = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, angle * MathF.PI / 180) };
        internal static Quaternion Inverse(Quaternion q) => new() { Value = System.Numerics.Quaternion.Inverse(q.Value) };
        public static Quaternion operator *(Quaternion a, Quaternion b) => new() { Value = a.Value * b.Value };
        public static Vector3 operator *(Quaternion a, Vector3 b)
        {
            var v = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(b.x, b.y, b.z), a.Value);
            return new Vector3(v.X, v.Y, v.Z);
        }
        internal Vector3 eulerAngles => new(0, MathF.Atan2(2 * Value.W * Value.Y, 1 - 2 * Value.Y * Value.Y) * 180 / MathF.PI, 0);
    }
}
