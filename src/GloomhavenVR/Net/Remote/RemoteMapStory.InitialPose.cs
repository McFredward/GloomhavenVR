using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static partial class RemoteMapStory
{
    internal static bool TryInitialPose(SharedWindowKind kind, out Vector3 position,
        out Quaternion rotation, out float factor)
    {
        position = Vector3.zero; rotation = Quaternion.identity; factor = 1f;
        Local local;
        Dictionary<int, PeerEntry> peers;
        Dictionary<int, float> stamps;
        uint key;
        switch (kind)
        {
            case SharedWindowKind.MapStory:
                UICharacterStoryBox? box = MapBox();
                key = box != null ? RemoteStorySync.HashDialog(box.dialogs) : 0;
                local = StoryLocal; peers = StoryPeers; stamps = StoryStampAt;
                break;
            case SharedWindowKind.QuestConfirm:
                UIQuestPopup? quest = QuestPopup();
                key = quest != null ? QuestKey(quest) : 0;
                local = QuestLocal; peers = QuestPeers; stamps = QuestStampAt;
                break;
            case SharedWindowKind.Encounter:
                UIEventPanel? encounter = EventPanel();
                key = encounter != null ? EncounterKey(encounter) : 0;
                local = EncounterLocal; peers = EncounterPeers; stamps = EncounterStampAt;
                break;
            default: return false;
        }
        if (key == 0) return false;
        bool requireOpeningReceipt = kind is SharedWindowKind.QuestConfirm or SharedWindowKind.Encounter;
        if (requireOpeningReceipt)
        {
            if (!SharedWindows.TryGetGrab(kind, out GrabbableModal? openingGrab) || openingGrab == null) return false;
            if (!ReferenceEquals(local.InitialGrab, openingGrab))
            {
                local.InitialGrab = openingGrab;
                local.InitialReceiptFrame = Time.frameCount;
            }
        }
        int best = 0; float latest = float.NegativeInfinity; bool initial = true;
        foreach (var pair in peers)
        {
            PeerEntry peer = pair.Value;
            if (peer.ContentKey != key || !peer.HasPose || Time.unscaledTime - peer.At > PeerStaleSeconds) continue;
            // Reopening the same native quest/event is a new physical float. Its first reveal
            // cannot reuse an older cached pose merely because the content key matches. Wait
            // for a normal current-opening snapshot; no arbitrary grace timeout or head refloat.
            if (requireOpeningReceipt && unchecked(peer.ReceiptFrame - local.InitialReceiptFrame) < 0) continue;
            if (kind == SharedWindowKind.MapStory
                && !MapStoryLifecycle.MatchesPose(true, pair.Key, peer.OpeningEpoch, peer.OpeningToken)) continue;
            if (peer.Initial && !SharedWindowSpawnAuthority.AcceptInitial(pair.Key, peer.Context, (int)kind)) continue;
            if (!stamps.TryGetValue(pair.Key, out float at)) continue;
            if (best != 0 && peer.Initial && !initial) continue;
            if ((!peer.Initial && initial) || at > latest)
            { best = pair.Key; latest = at; initial = peer.Initial; }
        }
        if (best == 0) return false;
        PeerEntry owner = peers[best];
        if (!ToWorld(owner.Frame, owner.Pose.Position, owner.Pose.Rotation, out position, out rotation,
                scenarioFrame: kind == SharedWindowKind.RewardShowcase)) return false;
        factor = SharedWindowSizeLaw.SharedGrabFactor(NetProtocol.DecodeStorySize(owner.SizeCode));
        // SyncIdentity's next pass must not discard this adoption as a window replacement.
        if (SharedWindows.TryGetGrab(kind, out GrabbableModal? grab) && grab != null)
        {
            local.Grab = grab;
            local.LastWindow = SharedWindows.WindowOf(kind);
            local.SwapFrame = int.MinValue;
        }
        local.Key = key;
        local.FramePos = position; local.FrameRot = rotation; local.FrameSize = factor;
        local.HaveBaseline = true; local.Moving = local.PoseOwned = false;
        local.FollowingPeer = best; local.FollowedStamp = owner.PoseStamp; local.FollowedStampValid = true;
        local.PoseTrack.Reset(); local.PoseTrackPeer = 0;
        return true;
    }
}
