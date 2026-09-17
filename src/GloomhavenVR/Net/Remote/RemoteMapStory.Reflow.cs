using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static partial class RemoteMapStory
{
    static RemoteMapStory()
    {
        SharedWindowReflowBridge.CanArrange = CanArrangeSharedWindows;
        SharedWindowReflowBridge.Begin = BeginSharedReflow;
        SharedWindowReflowBridge.Revision = SharedReflowRevision;
        SharedWindowReflowBridge.End = EndSharedReflow;
    }

    // Opening-time room making is authored once, then travels on the ordinary shared pose
    // stream. Record 77 distinguishes an automatic movement from a hand, including a stationary
    // remote grip which cannot be inferred from unchanged record-21 coordinates.
    private readonly struct ReflowPeer
    {
        internal ReflowPeer(bool host, byte held, byte automatic, float at)
        { Host = host; Held = held; Automatic = automatic; At = at; }
        internal readonly bool Host;
        internal readonly byte Held, Automatic;
        internal readonly float At;
    }

    private static readonly Dictionary<int, ReflowPeer> ReflowPeers = new();
    private static int _reflowOwner;
    private static float _reflowOwnerSince;
    private static byte _sentWindowHeld, _sentWindowAutomatic;
    private static bool _reflowSendPending;

    private static Local? ReflowLocal(SharedWindowKind kind) => kind switch
    {
        SharedWindowKind.MapStory => StoryLocal,
        SharedWindowKind.QuestConfirm => QuestLocal,
        SharedWindowKind.Encounter => EncounterLocal,
        _ => null,
    };

    private static byte ReflowBit(SharedWindowKind kind) => kind switch
    {
        SharedWindowKind.MapStory => NetProtocol.SharedWindowMotionMapStoryBit,
        SharedWindowKind.QuestConfirm => NetProtocol.SharedWindowMotionQuestConfirmBit,
        SharedWindowKind.Encounter => NetProtocol.SharedWindowMotionEncounterBit,
        _ => 0,
    };

    private static byte WindowHeldMask()
    {
        byte mask = 0;
        if (SharedWindows.TryGetGrab(SharedWindowKind.MapStory, out GrabbableModal? story)
            && story != null && story.IsGrabbed) mask |= 1;
        if (SharedWindows.TryGetGrab(SharedWindowKind.QuestConfirm, out GrabbableModal? quest)
            && quest != null && quest.IsGrabbed) mask |= 2;
        if (SharedWindows.TryGetGrab(SharedWindowKind.Encounter, out GrabbableModal? encounter)
            && encounter != null && encounter.IsGrabbed) mask |= 4;
        return mask;
    }

    private static byte WindowAutomaticMask() => (byte)((StoryLocal.Reflow ? 1 : 0)
        | (QuestLocal.Reflow ? 2 : 0) | (EncounterLocal.Reflow ? 4 : 0));

    internal static bool SharedReflowMoving => MapRoomDriver.Active && WindowAutomaticMask() != 0;

    internal static bool SharedReflowSendDue => MapRoomDriver.Active
        && (_reflowSendPending || WindowHeldMask() != _sentWindowHeld
            || WindowAutomaticMask() != _sentWindowAutomatic);

    private static void SampleReflow(ref PresenceState extras)
    {
        extras.HasSharedWindowMotion = true;
        extras.SharedWindowHeldMask = _sentWindowHeld = WindowHeldMask();
        extras.SharedWindowReflowMask = _sentWindowAutomatic = WindowAutomaticMask();
        _reflowSendPending = false;
    }

    private static void ResetReflow()
    {
        ReflowPeers.Clear();
        _reflowOwner = 0;
        _reflowOwnerSince = 0;
        _sentWindowHeld = _sentWindowAutomatic = 0;
        _reflowSendPending = false;
    }

    private static void ObserveReflow(int sender, in PresenceState presence)
    {
        if (!presence.HasMapRoom || (presence.MapRoomFlags & NetProtocol.MapRoomInRoomBit) == 0)
        { ReflowPeers.Remove(sender); return; }
        // A participant without explicit grip metadata cannot safely be rearranged. Older
        // builds are normally refused by the handshake; withholding layout also covers a gap.
        byte held = presence.HasSharedWindowMotion ? presence.SharedWindowHeldMask : (byte)7;
        byte automatic = presence.HasSharedWindowMotion ? presence.SharedWindowReflowMask : (byte)0;
        ReflowPeers[sender] = new ReflowPeer(
            (presence.MapRoomFlags & NetProtocol.MapRoomHostBit) != 0, held, automatic,
            Time.unscaledTime);
        if ((held & 1) != 0) CancelReflow(StoryLocal);
        if ((held & 2) != 0) CancelReflow(QuestLocal);
        if ((held & 4) != 0) CancelReflow(EncounterLocal);
    }

    private static void ObserveReflowPose(int sender, SharedWindowKind kind,
        in SharedWindowEntry entry, Dictionary<int, byte> stamps)
    {
        Local? local = ReflowLocal(kind);
        if (local == null || !local.Reflow || (entry.Flags & NetProtocol.SharedPoseBit) == 0
            || entry.ContentKey != local.Key) return;
        bool automatic = ReflowPeers.TryGetValue(sender, out ReflowPeer peer)
            && (peer.Automatic & ReflowBit(kind)) != 0;
        if (!automatic && (!stamps.TryGetValue(sender, out byte old) || old != entry.PoseStamp))
            CancelReflow(local);
    }

    /// <summary>Prefer the participating native host, otherwise the lowest fresh map-VR ID.
    /// The short stable-membership interval prevents opening frames racing the first census.
    /// This is only a decoration admission gate; it never gates a native window or action.</summary>
    internal static bool CanArrangeSharedWindows()
    {
        if (!MapRoomDriver.Active) return false;
        if (!FFSNetwork.IsOnline) return true;
        int localId = NetPlayerActors.LocalPlayerId();
        if (localId <= 0) return false;
        int elected = localId;
        bool host = FFSNetwork.IsHost;
        float now = Time.unscaledTime;
        bool held = WindowHeldMask() != 0;
        foreach (KeyValuePair<int, ReflowPeer> pair in ReflowPeers)
        {
            if (now - pair.Value.At > PeerStaleSeconds) continue;
            held |= pair.Value.Held != 0;
            if ((pair.Value.Host && !host) || (pair.Value.Host == host && pair.Key < elected))
            { elected = pair.Key; host = pair.Value.Host; }
        }
        if (_reflowOwner != elected)
        { _reflowOwner = elected; _reflowOwnerSince = now; }
        return !held && elected == localId && now - _reflowOwnerSince >= 0.35f;
    }

    internal static int SharedReflowRevision(SharedWindowKind kind) => ReflowLocal(kind)?.ReflowRevision ?? 0;

    internal static bool BeginSharedReflow(SharedWindowKind kind)
    {
        Local? local = ReflowLocal(kind);
        if (local == null || !CanArrangeSharedWindows() || local.Key == 0
            || !SharedWindows.TryGetGrab(kind, out GrabbableModal? grab) || grab == null
            || grab.IsGrabbed || !SharedWindowFrame.TryRead(grab, out Vector3 pos,
                out Quaternion rot, out float size)) return false;
        if (local.Reflow) return true;
        if (SyncIdentity(kind, local, grab, pos, rot, size)
            || (local.SwapFrame != int.MinValue && Time.frameCount - local.SwapFrame <= IdentitySettleFrames))
            return false;
        local.FramePos = pos; local.FrameRot = rot; local.FrameSize = size;
        local.HaveBaseline = true;
        local.Reflow = true;
        local.Moving = local.PoseOwned = true;
        local.FollowingPeer = local.PoseTrackPeer = 0;
        local.FollowedStampValid = false;
        local.MoveSettleAt = Time.unscaledTime + MoveSettleSeconds;
        unchecked { local.PoseStamp++; }
        _reflowSendPending = true;
        ModalFallback.NoteSharedAnchorSpent(kind, "opening-time animated window arrangement");
        return true;
    }

    internal static void EndSharedReflow(SharedWindowKind kind)
    {
        Local? local = ReflowLocal(kind);
        if (local == null || !local.Reflow) return;
        if (!SharedWindows.TryGetGrab(kind, out GrabbableModal? current)
            || !ReferenceEquals(current, local.Grab))
        { CancelReflow(local); return; }
        local.Reflow = false;
        local.Moving = false;
        local.PoseOwned = true;
        local.MoveSettleAt = Time.unscaledTime;
        if (SharedWindows.TryGetGrab(kind, out GrabbableModal? grab) && grab != null
            && SharedWindowFrame.TryRead(grab, out Vector3 pos, out Quaternion rot, out float size))
        { local.FramePos = pos; local.FrameRot = rot; local.FrameSize = size; }
        unchecked { local.PoseStamp++; }
        _reflowSendPending = true; // the exact endpoint and cleared automatic flag travel together
    }

    private static void CancelReflow(Local local)
    {
        if (!local.Reflow) return;
        local.Reflow = false;
        local.Moving = local.PoseOwned = false;
        local.FollowingPeer = local.PoseTrackPeer = 0;
        // The last visible tween sample may be newer than the last sent sample. Baseline it
        // now, otherwise TrackFrame would mistake the unsent remainder for a new local grab
        // and immediately reclaim ownership from the hand that cancelled the animation.
        if (local.Grab != null && SharedWindowFrame.TryRead(local.Grab, out Vector3 pos,
                out Quaternion rot, out float size))
        { local.FramePos = pos; local.FrameRot = rot; local.FrameSize = size; }
        unchecked { local.ReflowRevision++; }
        _reflowSendPending = true;
    }

    private static bool VisibleStoryPose() => SharedWindows.TryGetGrab(SharedWindowKind.MapStory,
        out GrabbableModal? grab) && grab != null
        && SharedWindowFrame.TryRead(grab, out _, out _, out _);
}
