using System;
using NQuaternion = System.Numerics.Quaternion;
using NVector = System.Numerics.Vector3;

// Explicit portable transform/time boundary. These tests verify the actual production decisions
// and exponential settle writes; they do not claim Unity lifecycle, rendering or headset pixels.
internal struct FixtureVector
{
    internal float x, y, z;
    internal FixtureVector(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    internal static FixtureVector zero => new(0, 0, 0);
    internal static FixtureVector one => new(1, 1, 1);
    internal float sqrMagnitude => x * x + y * y + z * z;
    internal static FixtureVector Lerp(FixtureVector a, FixtureVector b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
    public static FixtureVector operator +(FixtureVector a, FixtureVector b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static FixtureVector operator -(FixtureVector a, FixtureVector b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static FixtureVector operator *(FixtureVector a, float b) => new(a.x * b, a.y * b, a.z * b);
    internal NVector Native => new(x, y, z);
    internal static FixtureVector From(NVector p) => new(p.X, p.Y, p.Z);
}
internal struct FixtureRotation
{
    private readonly NQuaternion q;
    private FixtureRotation(NQuaternion q) { this.q = q; }
    internal static FixtureRotation identity => new(NQuaternion.Identity);
    internal static FixtureRotation Yaw(float radians) => new(NQuaternion.CreateFromAxisAngle(NVector.UnitY, radians));
    internal static FixtureRotation Slerp(FixtureRotation a, FixtureRotation b, float t) => new(NQuaternion.Slerp(a.q, b.q, t));
    internal static float Angle(FixtureRotation a, FixtureRotation b) => (float)(2 * Math.Acos(Math.Clamp(Math.Abs(NQuaternion.Dot(a.q, b.q)), 0, 1)) * 180 / Math.PI);
    internal static FixtureRotation Inverse(FixtureRotation a) => new(NQuaternion.Inverse(a.q));
    public static FixtureRotation operator *(FixtureRotation a, FixtureRotation b) => new(a.q * b.q);
    public static FixtureVector operator *(FixtureRotation a, FixtureVector b) => FixtureVector.From(NVector.Transform(b.Native, a.q));
}
internal sealed class FixtureTransform
{
    internal FixtureTransform? parent;
    internal FixtureVector localPosition, localScale = FixtureVector.one;
    internal FixtureRotation localRotation = FixtureRotation.identity;
    internal float Scale => (parent?.Scale ?? 1f) * localScale.x;
    internal FixtureVector position
    {
        get => parent == null ? localPosition : parent.position + parent.rotation * (localPosition * parent.Scale);
        set => localPosition = parent == null ? value : FixtureRotation.Inverse(parent.rotation) * (value - parent.position) * (1f / parent.Scale);
    }
    internal FixtureRotation rotation
    {
        get => parent == null ? localRotation : parent.rotation * localRotation;
        set => localRotation = parent == null ? value : FixtureRotation.Inverse(parent.rotation) * value;
    }
    internal void SetParent(FixtureTransform value, bool worldPositionStays)
    {
        FixtureVector at = position; FixtureRotation facing = rotation; float scale = Scale;
        parent = value;
        if (!worldPositionStays) return;
        position = at; rotation = facing; localScale = FixtureVector.one * (scale / value.Scale);
    }
}
internal sealed class FixtureObject { internal readonly FixtureTransform transform = new(); }
internal static class FixtureClock { internal static float unscaledDeltaTime = .011111111f; }
internal static class FixtureMath
{
    internal static float Min(float a, float b) => Math.Min(a, b);
    internal static float Exp(float f) => (float)Math.Exp(f);
}
namespace GloomhavenVR.Core { internal static class Defaults { internal const float CardLerpSpeed = 12f; } }
namespace GloomhavenVR.Net
{
    internal struct PresenceState
    {
        internal bool HasActiveDismissBerth, ActiveDismissGhostShown;
        internal byte ActiveDismissListCount, ActiveDismissSeat;
    }
    internal sealed partial class RemoteAvatar
    {
        internal bool ActiveDismissBerthActive, ActiveDismissGhostShown;
        internal int ActiveDismissListCount, ActiveDismissSeat = -1;
    }
}
