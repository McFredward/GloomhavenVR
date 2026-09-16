using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static partial class RemoteMapStory
{
    private static readonly Local RewardLocal = new();
    private static readonly RewardPoseHandshake RewardHandshake = new();
    private static readonly RewardPoseHandoff RewardHandoff = new();
    private static readonly Dictionary<int, PeerEntry> RewardPeers = new(), RewardCandidates = new();
    private static readonly Dictionary<int, byte> RewardStamp = new();
    private static readonly Dictionary<int, float> RewardStampAt = new();
    private static readonly Dictionary<int, bool> RewardMoved = new(), RewardUnavailable = new(), RewardReady = new();
    private static readonly List<int> RewardParticipants = new();
    private static uint _rewardSentKey;
    private static bool _rewardSentPose, _rewardMoved, _rewardSentUnavailable, _rewardSentReady, _rewardWasPending;
    private static int _rewardPlayerId;

    private static uint RewardKey => RewardShowcase.Window != null ? RewardShowcase.ContentKey : 0;
    internal static bool RewardSendDue
    {
        get
        {
            uint key = RewardKey;
            if (RewardHandshake.Changed) return true;
            if (key != _rewardSentKey || (key != 0 && RewardShowcasePlacement.LocalPlacementFailed != _rewardSentUnavailable)) return true;
            if (key == 0 || RewardShowcasePlacement.LocalPlacementFailed) return false;
            bool pending = RewardShowcasePlacement.LocalRevealPending;
            if (_rewardSentPose && (!pending != _rewardSentReady)) return true;
            if (RewardInitialOwner(key, _rewardPlayerId) != _rewardPlayerId || RewardLocal.FollowingPeer != 0) return false;
            if (RewardLocal.SwapFrame != int.MinValue && Time.frameCount - RewardLocal.SwapFrame <= IdentitySettleFrames) return false;
            if (!SharedWindows.TryGetGrab(SharedWindowKind.RewardShowcase, out GrabbableModal? grab) || grab == null
                || !SharedWindowFrame.TryRead(grab, out Vector3 position, out Quaternion rotation, out float size,
                    allowUnrevealed: true)) return false;
            return !_rewardSentPose || (pending && (!position.Equals(RewardLocal.FramePos)
                || !rotation.Equals(RewardLocal.FrameRot) || size != RewardLocal.FrameSize));
        }
    }

    private static void ResetRewardPose()
    {
        RewardHandshake.Reset();
        RewardLocal.Reset(); RewardHandoff.Reset(); RewardPeers.Clear(); RewardCandidates.Clear(); RewardStamp.Clear();
        RewardStampAt.Clear(); RewardMoved.Clear(); RewardUnavailable.Clear(); RewardReady.Clear(); RewardParticipants.Clear();
        _rewardSentKey = 0; _rewardSentPose = _rewardMoved = _rewardSentUnavailable = _rewardSentReady = _rewardWasPending = false;
        _rewardPlayerId = 0; RewardShowcasePlacement.ClearInitialPose(); RewardShowcasePlacement.ClearInitialAuthority();
    }
    private static void SetRewardIdentity(uint key)
    {
        RewardHandshake.SetLocalKey(key);
        if (RewardLocal.Key == key) return;
        RewardLocal.Reset(); RewardHandoff.Reset(); RewardLocal.Key = key; _rewardMoved = false; _rewardSentPose = false;
        _rewardWasPending = _rewardSentReady = false;
    }
    internal static void SampleReward(ref PresenceState presence, int localPlayerId)
    {
        _rewardPlayerId = localPlayerId;
        uint key = RewardKey;
        SetRewardIdentity(key);
        RefreshRewardHandshake(localPlayerId);
        presence.RewardPoseHandshake = RewardHandshake.Sample();
        presence.HasRewardPoseHandshake = presence.RewardPoseHandshake.Key != 0 || presence.RewardPoseHandshake.Count != 0;
        _rewardSentKey = key;
        _rewardSentUnavailable = key != 0 && RewardShowcasePlacement.LocalPlacementFailed;
        if (key == 0) return;
        bool pending = RewardShowcasePlacement.LocalRevealPending;
        TrackFrame(SharedWindowKind.RewardShowcase, RewardLocal, reset: false);
        // Native fit can move an invisible frame. It is not a human claim on the shared pose.
        if (pending || (_rewardWasPending && (RewardLocal.Grab == null || !RewardLocal.Grab.IsGrabbed)))
        { RewardLocal.Moving = false; RewardLocal.PoseOwned = false; RewardLocal.MoveSettleAt = 0f; }
        _rewardWasPending = pending;
        _rewardMoved |= RewardLocal.Moving;
        if (!pending && RewardLocal.Moving) RewardHandoff.LocalMove();
        if (RewardLocal.HaveBaseline && RewardLocal.FollowingPeer == 0
            && RewardInitialOwner(key, localPlayerId) == localPlayerId
            && (RewardLocal.SwapFrame == int.MinValue || Time.frameCount - RewardLocal.SwapFrame > IdentitySettleFrames))
            RewardLocal.PoseOwned = true;
        var state = new RewardWindowState { Unavailable = _rewardSentUnavailable, Window = new SharedWindowEntry {
            ContentKey = key, Flags = NetProtocol.SharedOpenBit, Page = NetProtocol.StoryPageNone } };
        if (!state.Unavailable && (!pending || RewardInitialOwner(key, localPlayerId) == localPlayerId))
            WritePose(SharedWindowKind.RewardShowcase, RewardLocal, ref state.Window);
        _rewardSentPose = (state.Window.Flags & NetProtocol.SharedPoseBit) != 0;
        state.Moved = _rewardMoved && _rewardSentPose;
        state.Ready = _rewardSentReady = _rewardSentPose && !pending;
        presence.HasRewardWindow = true; presence.RewardWindow = state;
    }
    internal static void ObserveReward(int sender, in PresenceState presence)
    {
        if (sender <= 0) return;
        if (presence.HasRewardPoseHandshake)
            RewardHandshake.Observe(sender, in presence.RewardPoseHandshake, Time.unscaledTime);
        else RewardHandshake.Forget(sender);
        if (!presence.HasRewardWindow || !RewardWindowCodec.Valid(in presence.RewardWindow))
        { ForgetReward(sender); return; }
        SharedWindowEntry entry = presence.RewardWindow.Window;
        if (RewardPeers.TryGetValue(sender, out PeerEntry previous) && previous.ContentKey != entry.ContentKey)
            ForgetReward(sender);
        RewardPeers[sender] = new PeerEntry(in entry, Time.unscaledTime);
        RewardMoved[sender] = presence.RewardWindow.Moved;
        RewardUnavailable[sender] = presence.RewardWindow.Unavailable;
        RewardReady[sender] = presence.RewardWindow.Ready;
        NoteStamp(sender, in entry, RewardStamp, RewardStampAt, Time.unscaledTime);
    }
    private static void ForgetReward(int sender)
    { Forget(sender, RewardPeers, RewardStamp, RewardStampAt); RewardMoved.Remove(sender); RewardUnavailable.Remove(sender); RewardReady.Remove(sender); }

    internal static void ResolveReward(int localPlayerId)
    {
        _rewardPlayerId = localPlayerId;
        uint key = RewardKey;
        SetRewardIdentity(key);
        RefreshRewardHandshake(localPlayerId);
        PruneStale(RewardPeers, RewardStamp, RewardStampAt);
        Scratch.Clear(); foreach (int peer in RewardMoved.Keys) if (!RewardPeers.ContainsKey(peer)) Scratch.Add(peer);
        foreach (int peer in Scratch) { RewardMoved.Remove(peer); RewardUnavailable.Remove(peer); RewardReady.Remove(peer); }
        int initialOwner = RewardInitialOwner(key, localPlayerId);
        if (key != 0) RewardShowcasePlacement.SetInitialAuthority(key, localPlayerId <= 0 || initialOwner == 0 || initialOwner == localPlayerId);
        else RewardShowcasePlacement.ClearInitialAuthority();
        PublishRewardInitialPose(key);
        if (key == 0) return;
        // A disconnected/closed pose owner cannot leave surviving participants unable to
        // publish the held final pose to somebody who joins later.
        if (RewardLocal.FollowingPeer != 0 && (!RewardPeers.TryGetValue(RewardLocal.FollowingPeer, out PeerEntry following)
            || !RewardPosePolicy.Matches(key, following.ContentKey, following.HasPose)))
            RewardLocal.FollowingPeer = 0;
        SelectRewardCandidates(key, localPlayerId);
        ResolvePose(SharedWindowKind.RewardShowcase, RewardLocal, key, RewardCandidates, RewardStampAt);
        // Preserve a human-positioned endpoint if its publisher leaves; a late initial
        // placement must not replace it after another participant takes over publishing.
        if (RewardLocal.FollowingPeer != 0 && RewardMoved.TryGetValue(RewardLocal.FollowingPeer, out bool moved))
            _rewardMoved |= moved;
    }
    private static void RefreshRewardHandshake(int localPlayerId)
    {
        NetAvatarDriver.CollectRewardPosePeers(RewardParticipants);
        RewardHandshake.Refresh(localPlayerId, RewardParticipants, Time.unscaledTime, PeerStaleSeconds);
    }
    private static int RewardInitialOwner(uint key, int localPlayerId)
    {
        NetAvatarDriver.CollectRewardPosePeers(RewardParticipants);
        // A late lower-id observer inherits an already visible window. It must not elect its
        // own still-hidden local home over the established public endpoint.
        int established = _rewardSentReady && _rewardSentPose && localPlayerId > 0 ? localPlayerId : int.MaxValue;
        foreach (int peer in RewardParticipants)
            if (RewardPeers.TryGetValue(peer, out PeerEntry visible) && visible.ContentKey == key && visible.HasPose
                && RewardReady.TryGetValue(peer, out bool ready) && ready)
                established = RewardPosePolicy.ConsiderInitialOwner(established, peer, false);
        if (established != int.MaxValue) return established;
        int owner = RewardPosePolicy.ConsiderInitialOwner(int.MaxValue, localPlayerId,
            key != 0 && (RewardShowcasePlacement.LocalPlacementFailed || RewardHandshake.LocalDeclined(key)));
        foreach (int peer in RewardParticipants)
        {
            bool unavailable = RewardHandshake.PeerDeclined(peer, key)
                || (RewardPeers.TryGetValue(peer, out PeerEntry state) && state.ContentKey == key
                    && RewardUnavailable.TryGetValue(peer, out bool failed) && failed);
            owner = RewardPosePolicy.ConsiderInitialOwner(owner, peer, unavailable);
        }
        if (owner != int.MaxValue) return owner;
        // The original publisher can finish or leave while late native copies still stand.
        // If every candidate declined its initial role, promote an ACTUAL remaining window;
        // an absent peer that sent a decline still cannot originate a pose. The same lowest-id
        // rule runs on all survivors, and no native confirmation or gameplay lock is changed.
        owner = RewardPosePolicy.ConsiderInitialOwner(int.MaxValue, localPlayerId,
            key == 0 || RewardKey != key || RewardShowcasePlacement.LocalPlacementFailed);
        foreach (int peer in RewardParticipants)
            if (RewardPeers.TryGetValue(peer, out PeerEntry remaining) && remaining.ContentKey == key)
                owner = RewardPosePolicy.ConsiderInitialOwner(owner, peer,
                    RewardUnavailable.TryGetValue(peer, out bool unavailable) && unavailable);
        return owner == int.MaxValue ? 0 : owner;
    }
    private static void SelectRewardCandidates(uint key, int localPlayerId)
    {
        bool anyMoved = false;
        int initialOwner = RewardInitialOwner(key, localPlayerId);
        foreach (var pair in RewardPeers)
        {
            if (!RewardPosePolicy.Matches(key, pair.Value.ContentKey, pair.Value.HasPose)) continue;
            anyMoved |= RewardMoved.TryGetValue(pair.Key, out bool moved) && moved;
        }
        RewardCandidates.Clear();
        foreach (var pair in RewardPeers)
        {
            if (!RewardPosePolicy.Matches(key, pair.Value.ContentKey, pair.Value.HasPose)) continue;
            bool moved = RewardMoved.TryGetValue(pair.Key, out bool value) && value;
            if (RewardPosePolicy.Eligible(_rewardMoved, anyMoved, moved, initialOwner, pair.Key))
                RewardCandidates[pair.Key] = pair.Value;
        }
    }
    private static void PublishRewardInitialPose(uint key)
    {
        RewardShowcasePlacement.ClearInitialPose();
        if (key == 0)
        {
            float newest = float.NegativeInfinity;
            foreach (var pair in RewardPeers)
                if (pair.Value.HasPose && pair.Value.At > newest)
                { newest = pair.Value.At; key = pair.Value.ContentKey; }
        }
        if (TryRewardInitialPose(key, out Vector3 position, out Quaternion rotation, out float size, out bool ready))
            RewardShowcasePlacement.SetInitialPose(key, position, rotation, size, ready);
    }
    private static bool TryRewardInitialPose(uint key, out Vector3 position, out Quaternion rotation, out float size, out bool ready)
    {
        position = Vector3.zero; rotation = Quaternion.identity; size = 1f; ready = false;
        SelectRewardCandidates(key, _rewardPlayerId);
        int best = 0; float latest = float.NegativeInfinity;
        foreach (var pair in RewardCandidates)
        {
            if (Time.unscaledTime - pair.Value.At > PeerStaleSeconds) continue;
            if (RewardStampAt.TryGetValue(pair.Key, out float at) && at > latest) { latest = at; best = pair.Key; }
        }
        if (best == 0) return false;
        PeerEntry owner = RewardCandidates[best];
        ready = RewardReady.TryGetValue(best, out bool value) && value;
        size = NetProtocol.DecodeStorySize(owner.SizeCode);
        return ToWorld(owner.Frame, owner.Pose.Position, owner.Pose.Rotation, out position, out rotation, scenarioFrame: true);
    }
}
