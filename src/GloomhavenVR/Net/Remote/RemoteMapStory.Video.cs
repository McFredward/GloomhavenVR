using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static partial class RemoteMapStory
{
    // Reuse the shared-window movement election, wire coordinate frame and interpolation.
    // Video has its own additive record: legacy21's kinds, count and byte layout stay frozen.
    private static readonly Local VideoLocal = new();
    private static readonly Dictionary<int, PeerEntry> VideoPeers = new();
    private static readonly Dictionary<int, byte> VideoStamp = new();
    private static readonly Dictionary<int, float> VideoStampAt = new();

    internal static void ResetVideoPose()
    {
        VideoLocal.Reset();
        VideoPeers.Clear();
        VideoStamp.Clear();
        VideoStampAt.Clear();
    }

    internal static void ObserveVideoPose(int sender, in SharedWindowEntry entry)
    {
        VideoPeers[sender] = new PeerEntry(in entry, Time.unscaledTime);
        NoteStamp(sender, in entry, VideoStamp, VideoStampAt, Time.unscaledTime);
    }

    internal static void ForgetVideoPose(int sender) => Forget(sender, VideoPeers, VideoStamp, VideoStampAt);

    internal static bool TryVideoInitialPose(int owner, uint key, out Vector3 pos, out Quaternion rot, out float size)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        size = 1;
        if (!VideoPeers.TryGetValue(owner, out PeerEntry entry) || !entry.HasPose
            || entry.ContentKey != key || Time.unscaledTime - entry.At > PeerStaleSeconds) return false;
        size = NetProtocol.DecodeStorySize(entry.SizeCode);
        return ToWorld(entry.Frame, entry.Pose.Position, entry.Pose.Rotation, out pos, out rot);
    }

    internal static void SampleVideoPose(uint key, bool source, ref SharedWindowEntry entry)
    {
        SetVideoIdentity(key);
        entry.ContentKey = key;
        TrackFrame(SharedWindowKind.Video, VideoLocal, reset: key == 0);
        // Unlike old map windows, a video's first visible placement is shared too. The native
        // source establishes it once; subsequent ownership comes from actual grabs, unchanged.
        if (source && VideoLocal.HaveBaseline && VideoLocal.FollowingPeer == 0
            && (VideoLocal.SwapFrame == int.MinValue || Time.frameCount - VideoLocal.SwapFrame > IdentitySettleFrames))
            VideoLocal.PoseOwned = true;
        WritePose(SharedWindowKind.Video, VideoLocal, ref entry);
    }

    internal static void ResolveVideoPose(uint key)
    {
        SetVideoIdentity(key);
        PruneStale(VideoPeers, VideoStamp, VideoStampAt);
        if (key == 0) return;
        ResolvePose(SharedWindowKind.Video, VideoLocal, key, VideoPeers, VideoStampAt);
    }

    private static void SetVideoIdentity(uint key)
    {
        if (VideoLocal.Key == key) return;
        VideoLocal.Reset();
        VideoLocal.Key = key;
    }
}
