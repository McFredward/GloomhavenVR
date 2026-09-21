using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>The three permanent map residents, expressed in the common parchment frame.
/// Contains only cosmetic poses and animation clocks, never transactions or item identities.</summary>
internal struct TownResidentPose
{
    internal RigPose Pose;
    internal float Scale, Age;
    internal byte Visibility, Clip;
}

internal struct TownResidentsState
{
    internal bool Active;
    internal TownResidentPose Merchant, Temple, Enchantress;
    internal TownResidentPose At(int index) => index == 0 ? Merchant : index == 1 ? Temple : Enchantress;
    internal void Set(int index, TownResidentPose pose)
    { if (index == 0) Merchant = pose; else if (index == 1) Temple = pose; else Enchantress = pose; }
}

/// <summary>Additive79: active1; when active, three pose20/scale4/age4/visibility1/clip1 entries.
/// Idle0 and greeting1 are sampled from the same authored clips on all clients.</summary>
internal static class TownResidentsCodec
{
    internal const int MaxPayload = 91;
    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    internal static bool Valid(in TownResidentsState state)
    {
        if (!state.Active) return true;
        for (int n = 0; n < 3; n++)
        {
            TownResidentPose entry = state.At(n);
            Vector3 p = entry.Pose.Position; Quaternion q = entry.Pose.Rotation;
            double norm = (double)q.x*q.x + (double)q.y*q.y + (double)q.z*q.z + (double)q.w*q.w;
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || p.sqrMagnitude > 10000f
                || !Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w)
                || System.Math.Abs(norm - 1d) > .01d || !Finite(entry.Scale) || entry.Scale <= .01f || entry.Scale > 10f
                || !Finite(entry.Age) || entry.Age < 0f || entry.Age > 10000000f || entry.Clip > 1) return false;
        }
        return true;
    }
    internal static bool Write(byte[] buffer, ref int offset, in TownResidentsState state)
    {
        int length = state.Active ? MaxPayload : 1;
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
        }
        return true;
    }
    internal static bool TryRead(byte[] buffer, int offset, int length, out TownResidentsState state)
    {
        state = default;
        if ((length != 1 && length != MaxPayload) || offset < 0 || offset > buffer.Length - length) return false;
        byte flags = buffer[offset++];
        if (flags > 1 || length != (flags == 1 ? MaxPayload : 1)) return false;
        var read = new TownResidentsState { Active = flags == 1 };
        if (!read.Active) return true;
        for (int n = 0; n < 3; n++)
        {
            bool nonzero = false;
            for (int k = 0; k < 8; k++) nonzero |= buffer[offset + 12 + k] != 0;
            if (!nonzero) return false; // The legacy pose reader repairs zero quaternions.
            var entry = new TownResidentPose();
            AvatarSerializer.ReadPoseShared(buffer, ref offset, out entry.Pose);
            entry.Scale = AvatarSerializer.ReadF32(buffer, ref offset);
            entry.Age = AvatarSerializer.ReadF32(buffer, ref offset);
            entry.Visibility = buffer[offset++]; entry.Clip = buffer[offset++];
            read.Set(n, entry);
        }
        if (!Valid(in read)) return false;
        state = read; return true;
    }
}
