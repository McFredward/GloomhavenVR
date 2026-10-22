using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Additive source frame for the unchanged legacy window poses. Build715 divided a pose by
/// its author's zoom/seat, then multiplied it by the observer's different zoom/seat. Both the
/// packet and the millimetre audit looked correct while the actual shared objects diverged.
/// Record119 supplies the AUTHOR's frame atomically with records19/21/72/73; old records keep
/// their exact grammar and coordinate meaning. New receivers can recover one game-world pose.
/// </summary>
internal struct SharedWindowPoseContext
{
    internal const int PayloadBytes = 22;
    internal const byte InitialMask = 63;
    internal bool Host;
    internal byte Initial;
    internal Vector3 Origin;
    internal float Yaw, Scale;

    internal bool Valid => (Initial & ~InitialMask) == 0 && Finite(Origin.x)
        && Finite(Origin.y) && Finite(Origin.z) && Finite(Yaw) && Yaw >= 0f && Yaw <= 360f
        && Finite(Scale) && Scale >= 0.0001f && Scale <= 1000000f;

    internal static byte KindBit(int kind) => kind >= 1 && kind <= 6 ? (byte)(1 << (kind - 1)) : (byte)0;
    internal bool IsInitial(int kind) => (Initial & KindBit(kind)) != 0;

    internal bool ToWorld(in RigPose relative, out RigPose world)
    {
        world = default;
        if (!Valid) return false;
        // Quaternion.Euler calls a native engine ECall. This yaw-only construction is
        // ordinary deterministic arithmetic, usable by the actual wire golden harness too.
        double halfYaw = Yaw * System.Math.PI / 360d;
        var yaw = new Quaternion(0f, (float)System.Math.Sin(halfYaw), 0f, (float)System.Math.Cos(halfYaw));
        world = new RigPose { Position = Origin + yaw * (relative.Position * Scale),
            Rotation = yaw * relative.Rotation };
        return Finite(world.Position.x) && Finite(world.Position.y) && Finite(world.Position.z);
    }

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static bool Write(byte[] bytes, ref int at, in SharedWindowPoseContext context)
    {
        if (!context.Valid || at < 0 || at > bytes.Length - PayloadBytes - 2) return false;
        bytes[at++] = NetProtocol.ExtIdSharedWindowPoseContext;
        bytes[at++] = PayloadBytes;
        bytes[at++] = context.Host ? (byte)1 : (byte)0;
        bytes[at++] = context.Initial;
        AvatarSerializer.WriteF32(bytes, ref at, context.Origin.x);
        AvatarSerializer.WriteF32(bytes, ref at, context.Origin.y);
        AvatarSerializer.WriteF32(bytes, ref at, context.Origin.z);
        AvatarSerializer.WriteF32(bytes, ref at, context.Yaw);
        AvatarSerializer.WriteF32(bytes, ref at, context.Scale);
        return true;
    }

    internal static bool TryRead(byte[] bytes, int at, int length, out SharedWindowPoseContext context)
    {
        context = default;
        if (length < PayloadBytes || at < 0 || at > bytes.Length - length) return false;
        byte flags = bytes[at++];
        if (flags > 1) return false;
        var read = new SharedWindowPoseContext { Host = flags != 0, Initial = bytes[at++] };
        read.Origin = new Vector3(AvatarSerializer.ReadF32(bytes, ref at),
            AvatarSerializer.ReadF32(bytes, ref at), AvatarSerializer.ReadF32(bytes, ref at));
        read.Yaw = AvatarSerializer.ReadF32(bytes, ref at);
        read.Scale = AvatarSerializer.ReadF32(bytes, ref at);
        if (!read.Valid) return false;
        context = read;
        return true;
    }
}
