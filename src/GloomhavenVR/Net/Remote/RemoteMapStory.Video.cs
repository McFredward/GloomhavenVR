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

    internal static void ObserveVideoPose(int sender, in SharedWindowEntry entry, SharedWindowPoseContext? context = null, float referenceScale = 0f)
    {
        bool accepted = SharedWindowMoves.Accepted(sender, SharedWindowKind.Video, entry.ContentKey);
        var incoming = new PeerEntry(in entry, Time.unscaledTime, context: context,
            kind: (int)SharedWindowKind.Video, referenceScale: referenceScale, allowPose: accepted);
        if (!accepted && VideoPeers.TryGetValue(sender, out PeerEntry previous) && previous.ContentKey == entry.ContentKey
            && SharedWindowMoves.PoseStillCurrent(sender, SharedWindowKind.Video, entry.ContentKey))
            incoming = new PeerEntry(in incoming, in previous);
        VideoPeers[sender] = incoming;
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

    internal static bool TryVideoSharedInitialPose(out Vector3 position, out Quaternion rotation, out float factor)
    {
        position = Vector3.zero; rotation = Quaternion.identity; factor = 1f;
        uint key = VideoLocal.Key;
        if (key == 0) return false;
        int best = 0; float latest = float.NegativeInfinity; bool initial = true;
        foreach (var pair in VideoPeers)
        {
            PeerEntry peer = pair.Value;
            if (peer.ContentKey != key || !peer.HasPose || Time.unscaledTime - peer.At > PeerStaleSeconds) continue;
            if (!SharedWindowMoves.MayFollow(pair.Key, SharedWindowKind.Video, key)) continue;
            if (peer.Initial && !SharedWindowSpawnAuthority.AcceptInitial(pair.Key, peer.Context, (int)SharedWindowKind.Video)) continue;
            if (!VideoStampAt.TryGetValue(pair.Key, out float at)) continue;
            if (best != 0 && peer.Initial && !initial) continue;
            if ((!peer.Initial && initial) || at > latest)
            { best = pair.Key; latest = at; initial = peer.Initial; }
        }
        if (best == 0) return false;
        PeerEntry owner = VideoPeers[best];
        if (!ToWorld(owner.Frame, owner.Pose.Position, owner.Pose.Rotation, out position, out rotation)) return false;
        factor = SharedWindowSizeLaw.SharedGrabFactor(NetProtocol.DecodeStorySize(owner.SizeCode));
        if (SharedWindows.TryGetGrab(SharedWindowKind.Video, out GrabbableModal? grab) && grab != null)
        {
            grab.ApplySharedReferenceWorldScale(owner.ReferenceScale);
            VideoLocal.Grab = grab;
            VideoLocal.LastWindow = SharedWindows.WindowOf(SharedWindowKind.Video);
            VideoLocal.SwapFrame = int.MinValue;
        }
        VideoLocal.FramePos = position; VideoLocal.FrameRot = rotation; VideoLocal.FrameSize = factor;
        VideoLocal.HaveBaseline = true; VideoLocal.Moving = VideoLocal.PoseOwned = false;
        VideoLocal.FollowingPeer = best; VideoLocal.FollowedStamp = owner.PoseStamp; VideoLocal.FollowedStampValid = true;
        VideoLocal.PoseTrack.Reset(); VideoLocal.PoseTrackPeer = 0;
        return true;
    }

    internal static void SampleVideoPose(uint key, bool source, ref SharedWindowEntry entry)
    {
        SetVideoIdentity(key);
        entry.ContentKey = key;
        TrackFrame(SharedWindowKind.Video, VideoLocal, reset: key == 0);
        // Playback-source election is independent of physical placement. Even a host showing
        // another player's mirror authors the first pose; only a real later grab owns a move.
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
