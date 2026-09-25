using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>The three permanent map residents, expressed in the common parchment frame.
/// Contains only cosmetic poses and animation clocks, never transactions or item identities.</summary>
internal struct TownResidentPose
{
    internal RigPose Pose;
    internal float Scale, Age, ActorFloorOffset, FurnitureBottom;
    internal byte Visibility, Clip;
}

/// <summary>Two edge control points of one anchored cloth runner. Metres and metres/second
/// in the station frame. The owner integrates contact; observers only replay these values.</summary>
internal struct TownClothRunnerState
{
    internal Vector2 Left, Right, LeftVelocity, RightVelocity;
}

internal struct TownResidentsState
{
    internal bool Active;
    internal bool HasCloth;
    internal TownResidentPose Merchant, Temple, Enchantress;
    internal TownClothRunnerState TempleLeft, TempleRight, EnchantressCloth;
    internal TownResidentPose At(int index) => index == 0 ? Merchant : index == 1 ? Temple : Enchantress;
    internal void Set(int index, TownResidentPose pose)
    { if (index == 0) Merchant = pose; else if (index == 1) Temple = pose; else Enchantress = pose; }
}

/// <summary>Additive79: active1; when active, three pose20/scale4/age4/visibility1/clip1/actorFloor4/furnitureBottom4 entries.
/// An optional 24-byte tail holds three cloth runners, each with four signed millimetre
/// displacements and four signed 2 mm/s velocities. The 115-byte prefix is unchanged.
/// Idle0 and greeting1 are sampled from the same authored clips on all clients.</summary>
internal static class TownResidentsCodec
{
    internal const int LegacyPayload = 115;
    internal const int MaxPayload = 139;
    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    internal static bool Valid(in TownResidentsState state)
    {
        if (!state.Active) return true;
        if (state.HasCloth && (!ValidCloth(state.TempleLeft) || !ValidCloth(state.TempleRight)
            || !ValidCloth(state.EnchantressCloth))) return false;
        for (int n = 0; n < 3; n++)
        {
            TownResidentPose entry = state.At(n);
            Vector3 p = entry.Pose.Position; Quaternion q = entry.Pose.Rotation;
            double norm = (double)q.x*q.x + (double)q.y*q.y + (double)q.z*q.z + (double)q.w*q.w;
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || p.sqrMagnitude > 10000f
                || !Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w)
                || System.Math.Abs(norm - 1d) > .01d || !Finite(entry.Scale) || entry.Scale <= .01f || entry.Scale > 10f
                || !Finite(entry.ActorFloorOffset) || Mathf.Abs(entry.ActorFloorOffset) > .5f
                || !Finite(entry.FurnitureBottom) || Mathf.Abs(entry.FurnitureBottom) > .5f
                || !Finite(entry.Age) || entry.Age < 0f || entry.Age > 10000000f || entry.Clip > 1) return false;
        }
        return true;
    }
    internal static bool ValidCloth(in TownClothRunnerState runner)
    {
        return ValidPoint(runner.Left) && ValidPoint(runner.Right)
            && ValidSpeed(runner.LeftVelocity) && ValidSpeed(runner.RightVelocity);
    }
    private static bool ValidPoint(Vector2 point) => Finite(point.x) && Finite(point.y)
        && Mathf.Abs(point.x) <= .1f && Mathf.Abs(point.y) <= .1f;
    private static bool ValidSpeed(Vector2 point) => Finite(point.x) && Finite(point.y)
        && Mathf.Abs(point.x) <= .25f && Mathf.Abs(point.y) <= .25f;
    private static byte Position(float metres) => unchecked((byte)(sbyte)Mathf.RoundToInt(Mathf.Clamp(metres, -.1f, .1f) * 1000f));
    private static byte Velocity(float metresPerSecond) => unchecked((byte)(sbyte)Mathf.RoundToInt(Mathf.Clamp(metresPerSecond, -.25f, .25f) * 500f));
    private static float ReadPosition(byte value) => unchecked((sbyte)value) * .001f;
    private static float ReadVelocity(byte value) => unchecked((sbyte)value) * .002f;
    internal static void WriteCloth(byte[] buffer, ref int offset, in TownClothRunnerState runner)
    {
        buffer[offset++] = Position(runner.Left.x); buffer[offset++] = Position(runner.Left.y);
        buffer[offset++] = Position(runner.Right.x); buffer[offset++] = Position(runner.Right.y);
        buffer[offset++] = Velocity(runner.LeftVelocity.x); buffer[offset++] = Velocity(runner.LeftVelocity.y);
        buffer[offset++] = Velocity(runner.RightVelocity.x); buffer[offset++] = Velocity(runner.RightVelocity.y);
    }
    internal static TownClothRunnerState ReadCloth(byte[] buffer, ref int offset) => new()
    {
        Left = new Vector2(ReadPosition(buffer[offset++]), ReadPosition(buffer[offset++])),
        Right = new Vector2(ReadPosition(buffer[offset++]), ReadPosition(buffer[offset++])),
        LeftVelocity = new Vector2(ReadVelocity(buffer[offset++]), ReadVelocity(buffer[offset++])),
        RightVelocity = new Vector2(ReadVelocity(buffer[offset++]), ReadVelocity(buffer[offset++]))
    };
    internal static bool Write(byte[] buffer, ref int offset, in TownResidentsState state)
    {
        int length = state.Active ? state.HasCloth ? MaxPayload : LegacyPayload : 1;
        if (!Valid(in state) || offset < 0 || offset > buffer.Length - length - 2) return false;
        buffer[offset++] = NetProtocol.ExtIdTownResidents; buffer[offset++] = (byte)length;
        buffer[offset++] = state.Active ? (byte)1 : (byte)0;
        if (!state.Active) return true;
        for (int n = 0; n < 3; n++)
        {
            TownResidentPose entry = state.At(n);
            AvatarSerializer.WritePoseShared(buffer, ref offset, in entry.Pose);
            AvatarSerializer.WriteF32(buffer, ref offset, entry.Scale);
            AvatarSerializer.WriteF32(buffer, ref offset, entry.Age);
            buffer[offset++] = entry.Visibility; buffer[offset++] = entry.Clip;
            AvatarSerializer.WriteF32(buffer, ref offset, entry.ActorFloorOffset);
            AvatarSerializer.WriteF32(buffer, ref offset, entry.FurnitureBottom);
        }
        if (state.HasCloth)
        { WriteCloth(buffer, ref offset, in state.TempleLeft); WriteCloth(buffer, ref offset, in state.TempleRight);
            WriteCloth(buffer, ref offset, in state.EnchantressCloth); }
        return true;
    }
    internal static bool TryRead(byte[] buffer, int offset, int length, out TownResidentsState state)
    {
        state = default;
        if ((length != 1 && length != LegacyPayload && length != MaxPayload) || offset < 0 || offset > buffer.Length - length) return false;
        byte flags = buffer[offset++];
        if (flags > 1 || flags == 0 && length != 1 || flags == 1 && length == 1) return false;
        var read = new TownResidentsState { Active = flags == 1, HasCloth = length == MaxPayload };
        if (!read.Active) return true;
        for (int n = 0; n < 3; n++)
        {
            int raw = offset + 12;
            double norm = 0d;
            for (int k = 0; k < 4; k++)
            { double q = AvatarSerializer.ReadI16(buffer, ref raw) / 32767d; norm += q * q; }
            // The legacy pose reader normalizes arbitrary nonzero input. Reject malformed
            // new-record rotations before that repair can disguise them as valid poses.
            if (System.Math.Abs(norm - 1d) > .01d) return false;
            var entry = new TownResidentPose();
            AvatarSerializer.ReadPoseShared(buffer, ref offset, out entry.Pose);
            entry.Scale = AvatarSerializer.ReadF32(buffer, ref offset);
            entry.Age = AvatarSerializer.ReadF32(buffer, ref offset);
            entry.Visibility = buffer[offset++]; entry.Clip = buffer[offset++];
            entry.ActorFloorOffset = AvatarSerializer.ReadF32(buffer, ref offset);
            entry.FurnitureBottom = AvatarSerializer.ReadF32(buffer, ref offset);
            read.Set(n, entry);
        }
        if (read.HasCloth)
        { read.TempleLeft = ReadCloth(buffer, ref offset); read.TempleRight = ReadCloth(buffer, ref offset);
            read.EnchantressCloth = ReadCloth(buffer, ref offset); }
        if (!Valid(in read)) return false;
        state = read; return true;
    }
}
