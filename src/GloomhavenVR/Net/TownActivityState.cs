using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Analytic work/attention phase; no native transaction or private character state.</summary>
internal struct TownActivityPose
{
    // Shared by the producer and validator. Build 600 extended the visible transition
    // to .95 seconds while the wire still rejected anything after .65 seconds;
    // settled idle/attention could consequently never be sent to observers.
    internal const float TransitionSeconds = .95f;
    internal float WorkClock, TransitionAge, FromBlend;
    internal bool Engaged;
}
internal struct TownActivitySoundState
{
    internal byte Cue;
    internal uint Generation;
    internal float StartedClock;
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
    // Optional additive tail. Only the resident author evaluates visitor manifests;
    // observers replay its cover and committed blessing instead of independently
    // deciding availability at each manifest's arrival time.
    internal bool HasSharedPerformance, Interactive;
    internal float TempleUnavailableBlend, TempleBlessingStartedClock;
    internal uint TempleBlessingGeneration;
    // The owned directional fill is sampled in the shared map frame. It must not
    // change when an observer chooses another local room or mixed reality.
    internal bool HasEnvironmentLight;
    internal Vector3 EnvironmentLightDirection, EnvironmentLightColour;
    internal float EnvironmentLightIntensity;
    internal bool HasAuthoredFoley;
    internal TownActivitySoundState MerchantFoley, EnchantressFoley;
    internal TownActivityPose At(int index) => index == 0 ? Merchant : index == 1 ? Temple : Enchantress;
    internal void Set(int index, TownActivityPose pose)
    { if (index == 0) Merchant = pose; else if (index == 1) Temple = pose; else Enchantress = pose; }
}

/// <summary>Additive81: active1/epoch4/sequence4/clock4, three13-byte occupation phases,
/// then the author's quantized merchant offering blend (one byte). Optional tails
/// carry the 13-byte shared performance, 28-byte owned environment light and
/// two nine-byte authored physical sound contacts.
/// Existing records79/80 and their dedicated packets remain byte-identical.</summary>
internal static class TownActivityCodec
{
    internal const int LegacyPayload = 53;
    internal const int SharedPayload = LegacyPayload + 13;
    internal const int LightPayload = SharedPayload + 28;
    internal const int MaxPayload = LightPayload + 18;
    internal const int PacketBytes = 6 + 2 + MaxPayload + 2 + TownFaceCodec.MaxPayload;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool Valid(in TownActivityState state)
    {
        if (!state.Active) return true;
        if (state.Epoch == 0 || !Finite(state.Clock) || state.Clock < 0f || state.Clock > 10000000f
            || !Finite(state.MerchantOfferingBlend) || state.MerchantOfferingBlend < 0f
            || state.MerchantOfferingBlend > 1f) return false;
        if (state.HasSharedPerformance && (!Finite(state.TempleUnavailableBlend)
            || state.TempleUnavailableBlend < 0f || state.TempleUnavailableBlend > 1f
            || !Finite(state.TempleBlessingStartedClock)
            || state.TempleBlessingStartedClock < -30f
            || state.TempleBlessingStartedClock > state.Clock
            || state.TempleBlessingGeneration == 0 && state.TempleBlessingStartedClock != 0f)) return false;
        if (state.HasEnvironmentLight && (!state.HasSharedPerformance
            || !Finite(state.EnvironmentLightDirection.x) || !Finite(state.EnvironmentLightDirection.y)
            || !Finite(state.EnvironmentLightDirection.z)
            || Mathf.Abs(state.EnvironmentLightDirection.sqrMagnitude - 1f) > .01f
            || !Finite(state.EnvironmentLightColour.x) || !Finite(state.EnvironmentLightColour.y)
            || !Finite(state.EnvironmentLightColour.z) || state.EnvironmentLightColour.x < 0f
            || state.EnvironmentLightColour.y < 0f || state.EnvironmentLightColour.z < 0f
            || state.EnvironmentLightColour.x > 10f || state.EnvironmentLightColour.y > 10f
            || state.EnvironmentLightColour.z > 10f || !Finite(state.EnvironmentLightIntensity)
            || state.EnvironmentLightIntensity < 0f || state.EnvironmentLightIntensity > 10f)) return false;
        if (state.HasAuthoredFoley && (!state.HasEnvironmentLight
            || !ValidSound(in state.MerchantFoley, state.Clock, 1, 1)
            || !ValidSound(in state.EnchantressFoley, state.Clock, 2, 6))) return false;
        for (int n = 0; n < 3; n++)
        {
            TownActivityPose p = state.At(n);
            if (!Finite(p.WorkClock) || p.WorkClock < 0f || p.WorkClock > 10000000f
                || !Finite(p.TransitionAge) || p.TransitionAge < 0f || p.TransitionAge > TownActivityPose.TransitionSeconds
                || !Finite(p.FromBlend) || p.FromBlend < 0f || p.FromBlend > 1f) return false;
        }
        return true;
    }
    internal static bool Write(byte[] buffer, ref int offset, in TownActivityState state)
    {
        int length = state.Active ? Payload(in state) : 1;
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
        if (state.HasSharedPerformance)
        {
            buffer[offset++] = state.Interactive ? (byte)1 : (byte)0;
            AvatarSerializer.WriteF32(buffer, ref offset, state.TempleUnavailableBlend);
            AvatarSerializer.WriteU32(buffer, ref offset, state.TempleBlessingGeneration);
            AvatarSerializer.WriteF32(buffer, ref offset, state.TempleBlessingStartedClock);
        }
        if (state.HasEnvironmentLight)
        {
            AvatarSerializer.WriteF32(buffer, ref offset, state.EnvironmentLightDirection.x);
            AvatarSerializer.WriteF32(buffer, ref offset, state.EnvironmentLightDirection.y);
            AvatarSerializer.WriteF32(buffer, ref offset, state.EnvironmentLightDirection.z);
            AvatarSerializer.WriteF32(buffer, ref offset, state.EnvironmentLightColour.x);
            AvatarSerializer.WriteF32(buffer, ref offset, state.EnvironmentLightColour.y);
            AvatarSerializer.WriteF32(buffer, ref offset, state.EnvironmentLightColour.z);
            AvatarSerializer.WriteF32(buffer, ref offset, state.EnvironmentLightIntensity);
        }
        if (state.HasAuthoredFoley)
        {
            WriteSound(buffer, ref offset, in state.MerchantFoley);
            WriteSound(buffer, ref offset, in state.EnchantressFoley);
        }
        return true;
    }
    internal static bool TryRead(byte[] buffer, int offset, int length, out TownActivityState state)
    {
        state = default;
        if (buffer == null || (length != 1 && length != LegacyPayload && length != SharedPayload
            && length != LightPayload && length != MaxPayload) || offset < 0 || offset > buffer.Length - length) return false;
        byte flag = buffer[offset++];
        if (flag > 1 || flag == 0 && length != 1 || flag == 1 && length == 1) return false;
        var read = new TownActivityState { Active = flag == 1, HasSharedPerformance = length >= SharedPayload,
            HasEnvironmentLight = length >= LightPayload, HasAuthoredFoley = length == MaxPayload };
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
        if (read.HasSharedPerformance)
        {
            byte interactive = buffer[offset++];
            if (interactive > 1) return false;
            read.Interactive = interactive == 1;
            read.TempleUnavailableBlend = AvatarSerializer.ReadF32(buffer, ref offset);
            read.TempleBlessingGeneration = AvatarSerializer.ReadU32(buffer, ref offset);
            read.TempleBlessingStartedClock = AvatarSerializer.ReadF32(buffer, ref offset);
        }
        if (read.HasEnvironmentLight)
        {
            read.EnvironmentLightDirection = new Vector3(AvatarSerializer.ReadF32(buffer, ref offset),
                AvatarSerializer.ReadF32(buffer, ref offset), AvatarSerializer.ReadF32(buffer, ref offset));
            read.EnvironmentLightColour = new Vector3(AvatarSerializer.ReadF32(buffer, ref offset),
                AvatarSerializer.ReadF32(buffer, ref offset), AvatarSerializer.ReadF32(buffer, ref offset));
            read.EnvironmentLightIntensity = AvatarSerializer.ReadF32(buffer, ref offset);
        }
        if (read.HasAuthoredFoley)
        {
            read.MerchantFoley = ReadSound(buffer, ref offset);
            read.EnchantressFoley = ReadSound(buffer, ref offset);
        }
        if (!Valid(in read)) return false;
        state = read; return true;
    }
    internal static bool Matches(in TownActivityState activity, in TownFaceState face) => activity.Active == face.Active
        && (!activity.Active || (activity.Epoch == face.Epoch && activity.Sequence == face.Sequence && activity.Clock == face.Clock));
    private static bool ValidSound(in TownActivitySoundState sound, float clock, byte first, byte last) =>
        sound.Generation == 0 ? sound.Cue == 0 && sound.StartedClock == 0f
        : sound.Cue >= first && sound.Cue <= last && Finite(sound.StartedClock)
            && sound.StartedClock >= 0f && sound.StartedClock <= clock;
    private static void WriteSound(byte[] buffer, ref int offset, in TownActivitySoundState sound)
    {
        buffer[offset++] = sound.Cue;
        AvatarSerializer.WriteU32(buffer, ref offset, sound.Generation);
        AvatarSerializer.WriteF32(buffer, ref offset, sound.StartedClock);
    }
    private static TownActivitySoundState ReadSound(byte[] buffer, ref int offset) => new TownActivitySoundState
    {
        Cue = buffer[offset++], Generation = AvatarSerializer.ReadU32(buffer, ref offset),
        StartedClock = AvatarSerializer.ReadF32(buffer, ref offset)
    };
    private static int Payload(in TownActivityState state) => state.HasAuthoredFoley ? MaxPayload
        : state.HasEnvironmentLight ? LightPayload
        : state.HasSharedPerformance ? SharedPayload : LegacyPayload;
    internal static int WritePacket(byte[] buffer, in TownActivityState state, in TownFaceState face)
    {
        int packetBytes = 6 + 2 + Payload(in state) + 2 + TownFaceCodec.MaxPayload;
        if (buffer == null || buffer.Length < packetBytes || !state.Active || !Valid(in state)
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
        if (bytes == null || (length != PacketBytes && length != PacketBytes - (MaxPayload - SharedPayload)
            && length != PacketBytes - (MaxPayload - LightPayload)
            && length != PacketBytes - (MaxPayload - LegacyPayload)) || length > bytes.Length || NetPacket.PeekType(bytes, length) != NetProtocol.MsgTownActivity
            || bytes[6] != NetProtocol.ExtIdTownFace || bytes[7] != TownFaceCodec.MaxPayload
            || bytes[activityHeader] != NetProtocol.ExtIdTownActivity || bytes[activityHeader + 1] != length - activityHeader - 2
            || !TownFaceCodec.TryRead(bytes, 8, TownFaceCodec.MaxPayload, out TownFaceState readFace)
            || !TryRead(bytes, activityHeader + 2, bytes[activityHeader + 1], out TownActivityState readActivity)
            || !readActivity.Active || !Matches(in readActivity, in readFace)) return false;
        state = readActivity; face = readFace; return true;
    }
}
