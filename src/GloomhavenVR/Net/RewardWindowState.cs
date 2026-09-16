using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Shared pose of a reward already shown by the native game on every client.
/// The opaque event key is only a match guard; no reward identity or continuation travels here.</summary>
internal struct RewardWindowState
{
    public bool Moved, Unavailable, Ready;
    public SharedWindowEntry Window;
}

/// <summary>Additive73: eventKey4, flags1 (pose1, moved2, unavailable4, ready8), optional stamp1/size1/frame1/pose20.
/// Absence means this peer has no reward window. Legacy shared-window record21 stays frozen.</summary>
internal static class RewardWindowCodec
{
    internal const int MinPayload = 5, MaxPayload = 28;
    internal static bool Valid(in RewardWindowState state)
    {
        SharedWindowEntry entry = state.Window;
        bool pose = (entry.Flags & NetProtocol.SharedPoseBit) != 0;
        if (entry.ContentKey == 0 || (entry.Flags & ~(NetProtocol.SharedOpenBit | NetProtocol.SharedPoseBit)) != 0
            || (entry.Flags & NetProtocol.SharedOpenBit) == 0 || (state.Moved && !pose)
            || (state.Ready && !pose) || (state.Unavailable && (pose || state.Moved || state.Ready))) return false;
        if (!pose) return true;
        Vector3 p = entry.Pose.Position; Quaternion q = entry.Pose.Rotation;
        double norm = (double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z + (double)q.w * q.w;
        return (entry.Frame == NetProtocol.SharedFrameParchment || entry.Frame == NetProtocol.RewardFrameScenario)
            && entry.SizeCode >= NetProtocol.StorySizeMinCode && entry.SizeCode <= NetProtocol.StorySizeMaxCode
            && Finite(p.x) && Finite(p.y) && Finite(p.z) && Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w)
            && System.Math.Abs(norm - 1d) <= .01d;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool Write(byte[] buffer, ref int offset, in RewardWindowState state)
    {
        if (!Valid(in state)) return false;
        bool pose = (state.Window.Flags & NetProtocol.SharedPoseBit) != 0;
        int length = pose ? MaxPayload : MinPayload;
        if (offset < 0 || offset > buffer.Length - length - 2) return false;
        buffer[offset++] = NetProtocol.ExtIdRewardWindow; buffer[offset++] = (byte)length;
        AvatarSerializer.WriteU32(buffer, ref offset, state.Window.ContentKey);
        buffer[offset++] = (byte)((pose ? 1 : 0) | (state.Moved ? 2 : 0) | (state.Unavailable ? 4 : 0) | (state.Ready ? 8 : 0));
        if (pose)
        {
            buffer[offset++] = state.Window.PoseStamp; buffer[offset++] = state.Window.SizeCode;
            buffer[offset++] = state.Window.Frame;
            AvatarSerializer.WritePoseShared(buffer, ref offset, in state.Window.Pose);
        }
        return true;
    }
    internal static bool TryRead(byte[] buffer, int offset, int length, out RewardWindowState state)
    {
        state = default;
        if ((length != MinPayload && length != MaxPayload) || offset < 0 || offset > buffer.Length - length) return false;
        var read = new RewardWindowState();
        read.Window.ContentKey = AvatarSerializer.ReadU32(buffer, ref offset);
        byte flags = buffer[offset++];
        if ((flags & ~15) != 0 || ((flags & 1) != 0 ? MaxPayload : MinPayload) != length) return false;
        read.Moved = (flags & 2) != 0;
        read.Unavailable = (flags & 4) != 0;
        read.Ready = (flags & 8) != 0;
        read.Window.Flags = NetProtocol.SharedOpenBit;
        read.Window.Page = NetProtocol.StoryPageNone;
        if ((flags & 1) != 0)
        {
            read.Window.Flags |= NetProtocol.SharedPoseBit;
            read.Window.PoseStamp = buffer[offset++]; read.Window.SizeCode = buffer[offset++];
            read.Window.Frame = buffer[offset++];
            // The legacy pose decoder repairs an all-zero quaternion to identity. A new
            // record must reject that malformed source rather than move a native window.
            int quaternion = offset + 12;
            bool nonzero = false;
            for (int n = 0; n < 8; n++) nonzero |= buffer[quaternion + n] != 0;
            if (!nonzero) return false;
            AvatarSerializer.ReadPoseShared(buffer, ref offset, out read.Window.Pose);
        }
        if (!Valid(in read)) return false;
        state = read;
        return true;
    }
}

/// <summary>Initial placement cannot steal a real drag. Before any movement, all participants
/// select the same lowest player id; later movement uses the existing shared last-mover clock.</summary>
internal static class RewardPosePolicy
{
    internal static bool Matches(uint key, uint peerKey, bool hasPose)
        => key != 0 && peerKey == key && hasPose;
    internal static int ConsiderInitialOwner(int current, int peerId, bool unavailable)
        => peerId > 0 && !unavailable && peerId < current ? peerId : current;
    internal static bool Eligible(bool localMoved, bool anyPeerMoved, bool peerMoved, int initialOwner, int peerId)
        => peerMoved || (!localMoved && !anyPeerMoved && peerId == initialOwner);
}
