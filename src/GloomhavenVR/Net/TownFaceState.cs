using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>One cosmetic facial sample. Angles are degrees in the station optical frame;
/// voice IDs identify bundled presentation clips, never native dialogue or game state.</summary>
internal struct TownFacePose
{
    internal float HeadPitch, HeadYaw, HeadRoll, LeftPitch, LeftYaw, RightPitch, RightYaw;
    internal ushort Cue;
    internal uint Generation;
    internal float SpeechAge;
    internal byte Jaw, Wide, Round;
}
internal struct TownFaceState
{
    internal bool Active;
    internal uint Epoch, Sequence;
    internal float Clock;
    internal TownFacePose Merchant, Temple, Enchantress;
    internal TownFacePose At(int index) => index == 0 ? Merchant : index == 1 ? Temple : Enchantress;
    internal void Set(int index, TownFacePose pose)
    { if (index == 0) Merchant = pose; else if (index == 1) Temple = pose; else Enchantress = pose; }
}

/// <summary>Additive80: active1, epoch4, sequence4, shared face clock4, three27-byte facial samples.
/// The same bounded record serves the fast map-only stream and presence recovery.
/// Record79 and every existing message remain byte-identical.</summary>
internal static class TownFaceCodec
{
    internal const int MaxPayload = 94;
    internal const int PacketBytes = 6 + 2 + MaxPayload;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool Valid(in TownFaceState state)
    {
        if (!state.Active) return true;
        if (state.Epoch == 0 || !Finite(state.Clock) || state.Clock < 0f || state.Clock > 10000000f) return false;
        for (int n = 0; n < 3; n++)
        {
            TownFacePose p = state.At(n);
            if (!Angle(p.HeadPitch, 25f) || !Angle(p.HeadYaw, 55f) || !Angle(p.HeadRoll, 8f)
                || !Angle(p.LeftPitch, 20f) || !Angle(p.RightPitch, 20f)
                || !Angle(p.LeftYaw, 28f) || !Angle(p.RightYaw, 28f)
                || !Finite(p.SpeechAge) || p.SpeechAge < 0f || p.SpeechAge > 3600f
                || (p.Cue == 0 && (p.Jaw != 0 || p.Wide != 0 || p.Round != 0))) return false;
        }
        return true;
    }
    private static bool Angle(float value, float limit) => Finite(value) && Mathf.Abs(value) <= limit;
    private static void WriteAngle(byte[] buffer, ref int offset, float value)
        => AvatarSerializer.WriteI16(buffer, ref offset, (short)Mathf.RoundToInt(value * 100f));
    private static float ReadAngle(byte[] buffer, ref int offset) => AvatarSerializer.ReadI16(buffer, ref offset) / 100f;
    internal static bool Write(byte[] buffer, ref int offset, in TownFaceState state)
    {
        int length = state.Active ? MaxPayload : 1;
        if (buffer == null || !Valid(in state) || offset < 0 || offset > buffer.Length - length - 2) return false;
        buffer[offset++] = NetProtocol.ExtIdTownFace; buffer[offset++] = (byte)length;
        buffer[offset++] = state.Active ? (byte)1 : (byte)0;
        if (!state.Active) return true;
        AvatarSerializer.WriteU32(buffer, ref offset, state.Epoch);
        AvatarSerializer.WriteU32(buffer, ref offset, state.Sequence);
        AvatarSerializer.WriteF32(buffer, ref offset, state.Clock);
        for (int n = 0; n < 3; n++)
        {
            TownFacePose p = state.At(n);
            WriteAngle(buffer, ref offset, p.HeadPitch); WriteAngle(buffer, ref offset, p.HeadYaw); WriteAngle(buffer, ref offset, p.HeadRoll);
            WriteAngle(buffer, ref offset, p.LeftPitch); WriteAngle(buffer, ref offset, p.LeftYaw);
            WriteAngle(buffer, ref offset, p.RightPitch); WriteAngle(buffer, ref offset, p.RightYaw);
            buffer[offset++] = (byte)p.Cue; buffer[offset++] = (byte)(p.Cue >> 8);
            AvatarSerializer.WriteU32(buffer, ref offset, p.Generation);
            AvatarSerializer.WriteF32(buffer, ref offset, p.SpeechAge);
            buffer[offset++] = p.Jaw; buffer[offset++] = p.Wide; buffer[offset++] = p.Round;
        }
        return true;
    }
    internal static bool TryRead(byte[] buffer, int offset, int length, out TownFaceState state)
    {
        state = default;
        if (buffer == null || (length != 1 && length != MaxPayload) || offset < 0 || offset > buffer.Length - length) return false;
        byte flag = buffer[offset++];
        if (flag > 1 || length != (flag == 1 ? MaxPayload : 1)) return false;
        var read = new TownFaceState { Active = flag == 1 };
        if (!read.Active) return true;
        read.Epoch = AvatarSerializer.ReadU32(buffer, ref offset);
        read.Sequence = AvatarSerializer.ReadU32(buffer, ref offset);
        read.Clock = AvatarSerializer.ReadF32(buffer, ref offset);
        for (int n = 0; n < 3; n++)
        {
            var p = new TownFacePose { HeadPitch = ReadAngle(buffer, ref offset), HeadYaw = ReadAngle(buffer, ref offset),
                HeadRoll = ReadAngle(buffer, ref offset), LeftPitch = ReadAngle(buffer, ref offset), LeftYaw = ReadAngle(buffer, ref offset),
                RightPitch = ReadAngle(buffer, ref offset), RightYaw = ReadAngle(buffer, ref offset) };
            p.Cue = (ushort)(buffer[offset++] | buffer[offset++] << 8);
            p.Generation = AvatarSerializer.ReadU32(buffer, ref offset);
            p.SpeechAge = AvatarSerializer.ReadF32(buffer, ref offset);
            p.Jaw = buffer[offset++]; p.Wide = buffer[offset++]; p.Round = buffer[offset++];
            read.Set(n, p);
        }
        if (!Valid(in read)) return false;
        state = read; return true;
    }
    internal static int WritePacket(byte[] buffer, in TownFaceState state)
    {
        int size = state.Active ? PacketBytes : 9;
        if (buffer == null || buffer.Length < size || !Valid(in state)) return 0;
        int offset = 0;
        AvatarSerializer.WriteU32(buffer, ref offset, NetProtocol.Magic);
        buffer[offset++] = NetProtocol.Version; buffer[offset++] = NetProtocol.MsgTownFace;
        return Write(buffer, ref offset, in state) ? offset : 0;
    }
    internal static bool ReadPacket(byte[] bytes, int length, out TownFaceState state)
    {
        state = default;
        return bytes != null && length >= 9 && length <= bytes.Length && NetPacket.PeekType(bytes, length) == NetProtocol.MsgTownFace
            && bytes[6] == NetProtocol.ExtIdTownFace && length == bytes[7] + 8
            && TryRead(bytes, 8, bytes[7], out state);
    }
}
