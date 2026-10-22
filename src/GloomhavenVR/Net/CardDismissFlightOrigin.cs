using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>An explicitly dismissed original card's immutable pool address and launch geometry.
/// The pose and width are relative to the owner's item-use berth, sampled once at launch;
/// no per-frame animation transforms or private card identifiers are transmitted.</summary>
internal sealed class CardDismissFlightOrigin
{
    internal readonly int SourceActorId;
    internal readonly ushort PoolSeat, PoolCount;
    internal readonly RigPose BerthPose;
    internal readonly float Width;

    internal CardDismissFlightOrigin(int sourceActorId, ushort poolSeat, ushort poolCount,
        RigPose berthPose, float width)
    {
        SourceActorId = sourceActorId; PoolSeat = poolSeat; PoolCount = poolCount;
        BerthPose = berthPose; Width = width;
    }

    internal bool Valid
        => SourceActorId != 0 && PoolCount > 0 && PoolCount <= 32768
            && (PoolSeat & 32767) < PoolCount && GeometryValid;

    internal bool GeometryValid
    {
        get
        {
            Vector3 p = BerthPose.Position;
            Quaternion q = BerthPose.Rotation;
            float magnitude = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return Finite(p.x) && Finite(p.y) && Finite(p.z)
                && Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w)
                && magnitude >= 0.000001f && magnitude <= 4f
                && Finite(Width) && Width >= 0.0001f && Width <= 1000000f;
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
