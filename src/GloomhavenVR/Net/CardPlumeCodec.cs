using System;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Atomic actual smoke frames in additive TLV51. Local pose means owner-board-local,
/// including the original smoke root's authored placement. No source card art or identity travels.</summary>
internal static class CardPlumeCodec
{
    internal const int MaxSize = 12288;
    internal const int MaxEncodedBytes = 9542;
    private const int PayloadBytes = 94;

    internal static int Write(CardPlumeSnapshot snapshot, byte[] buffer)
    {
        if (snapshot == null || buffer == null || buffer.Length < MaxSize)
            throw new ArgumentException("Invalid card plume output.");
        // Validate publication again, including duplicate seat identities.
        snapshot = new CardPlumeSnapshot(snapshot.SampleTime, snapshot.States);
        int at = 0;
        AvatarSerializer.WriteU32(buffer, ref at, NetProtocol.Magic);
        buffer[at++] = NetProtocol.Version;
        buffer[at++] = NetProtocol.MsgCardPlume;
        if (snapshot.States.Length == 0)
        {
            buffer[at++] = NetProtocol.ExtIdCardPlume; buffer[at++] = 6;
            buffer[at++] = 255; buffer[at++] = 0;
            AvatarSerializer.WriteF32(buffer, ref at, snapshot.SampleTime);
        }
        for (int i = 0; i < snapshot.States.Length; i++)
        {
            CardPlumeState state = snapshot.States[i];
            buffer[at++] = NetProtocol.ExtIdCardPlume; buffer[at++] = (byte)(PayloadBytes + ((state.Flags >> 6) == 1 ? 12 : 0) + (((state.Flags >> 4) & 3) == 2 ? (state.CustomSpacePresent ? 41 : 1) : 0));
            buffer[at++] = (byte)i; buffer[at++] = (byte)snapshot.States.Length;
            AvatarSerializer.WriteF32(buffer, ref at, snapshot.SampleTime);
            AvatarSerializer.WriteI32(buffer, ref at, state.ActorId);
            buffer[at++] = state.FaceCode; buffer[at++] = state.ListCount; buffer[at++] = state.Flags;
            buffer[at++] = state.EmitterIndex;
            AvatarSerializer.WriteU32(buffer, ref at, state.Episode);
            AvatarSerializer.WriteU32(buffer, ref at, state.RandomSeed);
            AvatarSerializer.WriteF32(buffer, ref at, state.Age);
            AvatarSerializer.WriteF32(buffer, ref at, state.PlaybackRate);
            AvatarSerializer.WriteF32(buffer, ref at, state.StartSizeMultiplier);
            AvatarSerializer.WriteF32(buffer, ref at, state.StartSpeedMultiplier);
            AvatarSerializer.WriteF32(buffer, ref at, state.Color.r);
            AvatarSerializer.WriteF32(buffer, ref at, state.Color.g);
            AvatarSerializer.WriteF32(buffer, ref at, state.Color.b);
            AvatarSerializer.WriteF32(buffer, ref at, state.Color.a);
            WriteVector(buffer, ref at, state.LocalPosition);
            AvatarSerializer.WriteF32(buffer, ref at, state.LocalRotation.x);
            AvatarSerializer.WriteF32(buffer, ref at, state.LocalRotation.y);
            AvatarSerializer.WriteF32(buffer, ref at, state.LocalRotation.z);
            AvatarSerializer.WriteF32(buffer, ref at, state.LocalRotation.w);
            WriteVector(buffer, ref at, state.LocalScale);
            if ((state.Flags >> 6) == 1) WriteVector(buffer, ref at, state.EmitterLocalScale);
            if (((state.Flags >> 4) & 3) == 2)
            {
                buffer[at++] = state.CustomSpacePresent ? (byte)1 : (byte)0;
                if (state.CustomSpacePresent)
                {
                    WriteVector(buffer, ref at, state.CustomPosition);
                    AvatarSerializer.WriteF32(buffer, ref at, state.CustomRotation.x);
                    AvatarSerializer.WriteF32(buffer, ref at, state.CustomRotation.y);
                    AvatarSerializer.WriteF32(buffer, ref at, state.CustomRotation.z);
                    AvatarSerializer.WriteF32(buffer, ref at, state.CustomRotation.w);
                    WriteVector(buffer, ref at, state.CustomScale);
                }
            }
        }
        return at;
    }

    internal static bool TryRead(byte[] buffer, int length, out CardPlumeSnapshot? snapshot)
    {
        snapshot = null;
        if (buffer == null || length < 6 || length > buffer.Length || length > MaxSize
            || NetPacket.PeekType(buffer, length) != NetProtocol.MsgCardPlume) return false;
        var states = new CardPlumeState?[CardPlumeState.CountMax];
        var offsets = new int[CardPlumeState.CountMax];
        int expected = -1, received = 0;
        float time = -1;
        for (int at = 6; at < length;)
        {
            if (at + 2 > length) return false;
            int type = buffer[at++], bytes = buffer[at++], start = at, end = at + bytes;
            if (end > length) return false;
            if (type != NetProtocol.ExtIdCardPlume) { at = end; continue; }
            if (bytes != 6 && bytes != PayloadBytes && bytes != PayloadBytes + 1 && bytes != PayloadBytes + 41
                && bytes != PayloadBytes + 12 && bytes != PayloadBytes + 13 && bytes != PayloadBytes + 53) return false;
            int index = buffer[at++], count = buffer[at++];
            float stamp = AvatarSerializer.ReadF32(buffer, ref at);
            if (float.IsNaN(stamp) || float.IsInfinity(stamp) || stamp < 0
                || count > CardPlumeState.CountMax || (expected >= 0 && (expected != count || time != stamp))) return false;
            expected = count; time = stamp;
            if (count == 0)
            {
                if (bytes != 6 || index != 255 || received != 0) return false;
                continue;
            }
            if (bytes < PayloadBytes || index >= count) return false;
            if (states[index] != null)
            {
                if (buffer[offsets[index] - 1] != bytes) return false;
                for (int b = 0; b < bytes; b++) if (buffer[start + b] != buffer[offsets[index] + b]) return false;
                at = end; continue;
            }
            var state = new CardPlumeState
            {
                ActorId = AvatarSerializer.ReadI32(buffer, ref at),
                FaceCode = buffer[at++], ListCount = buffer[at++], Flags = buffer[at++], EmitterIndex = buffer[at++],
                Episode = AvatarSerializer.ReadU32(buffer, ref at),
                RandomSeed = AvatarSerializer.ReadU32(buffer, ref at),
                Age = AvatarSerializer.ReadF32(buffer, ref at),
                PlaybackRate = AvatarSerializer.ReadF32(buffer, ref at),
                StartSizeMultiplier = AvatarSerializer.ReadF32(buffer, ref at),
                StartSpeedMultiplier = AvatarSerializer.ReadF32(buffer, ref at),
                Color = new Color(AvatarSerializer.ReadF32(buffer, ref at), AvatarSerializer.ReadF32(buffer, ref at),
                    AvatarSerializer.ReadF32(buffer, ref at), AvatarSerializer.ReadF32(buffer, ref at)),
                LocalPosition = ReadVector(buffer, ref at),
                LocalRotation = new Quaternion(AvatarSerializer.ReadF32(buffer, ref at), AvatarSerializer.ReadF32(buffer, ref at),
                    AvatarSerializer.ReadF32(buffer, ref at), AvatarSerializer.ReadF32(buffer, ref at)),
                LocalScale = ReadVector(buffer, ref at),
            };
            if ((state.Flags >> 6) == 1)
            {
                if (end - at < 12) return false;
                state.EmitterLocalScale = ReadVector(buffer, ref at);
            }
            if (((state.Flags >> 4) & 3) == 2)
            {
                if (at >= end || buffer[at] > 1) return false;
                state.CustomSpacePresent = buffer[at++] != 0;
                if (state.CustomSpacePresent)
                {
                    if (end - at != 40) return false;
                    state.CustomPosition = ReadVector(buffer, ref at);
                    state.CustomRotation = new Quaternion(AvatarSerializer.ReadF32(buffer, ref at), AvatarSerializer.ReadF32(buffer, ref at),
                        AvatarSerializer.ReadF32(buffer, ref at), AvatarSerializer.ReadF32(buffer, ref at));
                    state.CustomScale = ReadVector(buffer, ref at);
                }
            }
            if (at != end || !state.Validate()) return false;
            for (int j = 0; j < states.Length; j++)
                if (states[j] != null && states[j]!.ActorId == state.ActorId && states[j]!.FaceCode == state.FaceCode && states[j]!.EmitterIndex == state.EmitterIndex)
                    return false;
            states[index] = state; offsets[index] = start; received++;
        }
        if (expected < 0 || received != expected) return false;
        var complete = new CardPlumeState[expected];
        for (int i = 0; i < expected; i++) complete[i] = states[i]!;
        snapshot = new CardPlumeSnapshot(time, complete);
        return true;
    }

    private static void WriteVector(byte[] b, ref int at, Vector3 v)
    {
        AvatarSerializer.WriteF32(b, ref at, v.x); AvatarSerializer.WriteF32(b, ref at, v.y);
        AvatarSerializer.WriteF32(b, ref at, v.z);
    }
    private static Vector3 ReadVector(byte[] b, ref int at) => new(AvatarSerializer.ReadF32(b, ref at),
        AvatarSerializer.ReadF32(b, ref at), AvatarSerializer.ReadF32(b, ref at));
}
