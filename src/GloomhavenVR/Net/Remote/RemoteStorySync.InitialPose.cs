using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static partial class RemoteStorySync
{
    /// <summary>One current-opening pose, applied before the follower becomes visible. Adopt
    /// the same baseline here: the first visible shared snap is not another player's new drag.</summary>
    internal static bool TryInitialPose(out Vector3 position, out Quaternion rotation, out float factor)
    {
        position = Vector3.zero; rotation = Quaternion.identity; factor = 1f;
        UICharacterStoryBox? box = Box();
        uint key = box != null ? HashDialog(box.dialogs) : 0u;
        if (key == 0) return false;
        int best = 0;
        float latest = float.NegativeInfinity;
        bool initial = true;
        foreach (var pair in Peers)
        {
            PeerStory peer = pair.Value;
            if (peer.Key != key || !peer.HasPose || Time.unscaledTime - peer.At > PeerStaleSeconds
                || !MapStoryLifecycle.MatchesPose(false, pair.Key, peer.OpeningEpoch, peer.OpeningToken)) continue;
            if (peer.Initial && !SharedWindowSpawnAuthority.AcceptInitial(pair.Key, peer.Context, 1)) continue;
            if (!PeerPoseChangedAt.TryGetValue(pair.Key, out float at)) continue;
            if (best != 0 && peer.Initial && !initial) continue;
            if ((!peer.Initial && initial) || at > latest)
            { best = pair.Key; latest = at; initial = peer.Initial; }
        }
        if (best == 0) return false;
        PeerStory owner = Peers[best];
        position = owner.Pose.Position; rotation = owner.Pose.Rotation;
        if (!owner.Canonical && !TryToWorld(position, rotation, out position, out rotation)) return false;
        factor = SharedWindowSizeLaw.SharedGrabFactor(NetProtocol.DecodeStorySize(owner.SizeCode));
        _lastFramePos = position; _lastFrameRot = rotation; _lastFrameSize = factor;
        _haveFrameBaseline = true; _localMoving = _localPoseOwned = false;
        _followingPeer = best; _followedStamp = owner.PoseStamp; _followedStampValid = true;
        PoseTrack.Reset(); _poseTrackPeer = 0;
        return true;
    }
}
