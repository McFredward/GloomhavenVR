using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Analytic work/attention phase; no native transaction or private character state.</summary>
internal struct TownActivityPose
{
    internal float WorkClock, TransitionAge, FromBlend;
    internal bool Engaged;
}
internal struct TownActivityState
{
    internal bool Active;
    internal uint Epoch, Sequence;
    internal float Clock;
    internal TownActivityPose Merchant, Temple, Enchantress;
    // The resident author alone advances this transition. Recomputing it from a
    // visitor's locally received offer would give every observer a different pose.
    internal float MerchantOfferingBlend;
    internal TownActivityPose At(int index) => index == 0 ? Merchant : index == 1 ? Temple : Enchantress;
    internal void Set(int index, TownActivityPose pose)
    { if (index == 0) Merchant = pose; else if (index == 1) Temple = pose; else Enchantress = pose; }
}

/// <summary>Additive81: active1/epoch4/sequence4/clock4, three13-byte occupation phases,
/// then the author's quantized merchant offering blend (one byte).
/// Existing records79/80 and their dedicated packets remain byte-identical.</summary>
internal static class TownActivityCodec
{
    internal const int MaxPayload = 53;
    internal const int PacketBytes = 6 + 2 + MaxPayload + 2 + TownFaceCodec.MaxPayload;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool Valid(in TownActivityState state)
    {
        if (!state.Active) return true;
        if (state.Epoch == 0 || !Finite(state.Clock) || state.Clock < 0f || state.Clock > 10000000f
            || !Finite(state.MerchantOfferingBlend) || state.MerchantOfferingBlend < 0f
            || state.MerchantOfferingBlend > 1f) return false;
        for (int n = 0; n < 3; n++)
        {
            TownActivityPose p = state.At(n);
            if (!Finite(p.WorkClock) || p.WorkClock < 0f || p.WorkClock > 10000000f
                || !Finite(p.TransitionAge) || p.TransitionAge < 0f || p.TransitionAge > .65f
                || !Finite(p.FromBlend) || p.FromBlend < 0f || p.FromBlend > 1f) return false;
        }
        return true;
    }
    internal static bool Write(byte[] buffer, ref int offset, in TownActivityState state)
    {
        int length = state.Active ? MaxPayload : 1;
        if (buffer == null || !Valid(in state) || offset < 0 || offset > buffer.Length - length - 2) return false;
        buffer[offset++] = NetProtocol.ExtIdTownActivity; buffer[offset++] = (byte)length;
        buffer[offset++] = state.Active ? (byte)1 : (byte)0;
        if (!state.Active) return true;
        AvatarSerializer.WriteU32(buffer, ref offset, state.Epoch);
        AvatarSerializer.WriteU32(buffer, ref offset, state.Sequence);
        AvatarSerializer.WriteF32(buffer, ref offset, state.Clock);
        for (int n = 0; n < 3; n++)
        {
            TownActivityPose p = state.At(n);
            AvatarSerializer.WriteF32(buffer, ref offset, p.WorkClock);
            AvatarSerializer.WriteF32(buffer, ref offset, p.TransitionAge);
            AvatarSerializer.WriteF32(buffer, ref offset, p.FromBlend);
            buffer[offset++] = p.Engaged ? (byte)1 : (byte)0;
        }
        buffer[offset++] = (byte)Mathf.RoundToInt(state.MerchantOfferingBlend * 255f);
        return true;
    }
    internal static bool TryRead(byte[] buffer, int offset, int length, out TownActivityState state)
    {
        state = default;
        if (buffer == null || (length != 1 && length != MaxPayload) || offset < 0 || offset > buffer.Length - length) return false;
        byte flag = buffer[offset++];
        if (flag > 1 || length != (flag == 1 ? MaxPayload : 1)) return false;
        var read = new TownActivityState { Active = flag == 1 };
        if (!read.Active) return true;
        read.Epoch = AvatarSerializer.ReadU32(buffer, ref offset);
        read.Sequence = AvatarSerializer.ReadU32(buffer, ref offset);
        read.Clock = AvatarSerializer.ReadF32(buffer, ref offset);
        for (int n = 0; n < 3; n++)
        {
            var p = new TownActivityPose { WorkClock = AvatarSerializer.ReadF32(buffer, ref offset),
                TransitionAge = AvatarSerializer.ReadF32(buffer, ref offset), FromBlend = AvatarSerializer.ReadF32(buffer, ref offset) };
            byte target = buffer[offset++];
            if (target > 1) return false;
            p.Engaged = target == 1;
            read.Set(n, p);
        }
        read.MerchantOfferingBlend = buffer[offset++] / 255f;
        if (!Valid(in read)) return false;
        state = read; return true;
    }
    internal static bool Matches(in TownActivityState activity, in TownFaceState face) => activity.Active == face.Active
        && (!activity.Active || (activity.Epoch == face.Epoch && activity.Sequence == face.Sequence && activity.Clock == face.Clock));
    internal static int WritePacket(byte[] buffer, in TownActivityState state, in TownFaceState face)
    {
        if (buffer == null || buffer.Length < PacketBytes || !state.Active || !Valid(in state)
            || !TownFaceCodec.Valid(in face) || !Matches(in state, in face)) return 0;
        int offset = 0;
        AvatarSerializer.WriteU32(buffer, ref offset, NetProtocol.Magic);
        buffer[offset++] = NetProtocol.Version; buffer[offset++] = NetProtocol.MsgTownActivity;
        return TownFaceCodec.Write(buffer, ref offset, in face) && Write(buffer, ref offset, in state) ? offset : 0;
    }
    internal static bool ReadPacket(byte[] bytes, int length, out TownActivityState state, out TownFaceState face)
    {
        state = default; face = default;
        const int activityHeader = 8 + TownFaceCodec.MaxPayload;
        if (bytes == null || length != PacketBytes || length > bytes.Length || NetPacket.PeekType(bytes, length) != NetProtocol.MsgTownActivity
            || bytes[6] != NetProtocol.ExtIdTownFace || bytes[7] != TownFaceCodec.MaxPayload
            || bytes[activityHeader] != NetProtocol.ExtIdTownActivity || bytes[activityHeader + 1] != MaxPayload
            || !TownFaceCodec.TryRead(bytes, 8, TownFaceCodec.MaxPayload, out TownFaceState readFace)
            || !TryRead(bytes, activityHeader + 2, MaxPayload, out TownActivityState readActivity)
            || !readActivity.Active || !Matches(in readActivity, in readFace)) return false;
        state = readActivity; face = readFace; return true;
    }
}
