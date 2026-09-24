using System.Collections.Generic;
using GloomhavenVR.Net;
namespace UnityEngine
{
    internal struct Vector3 { internal static Vector3 zero => default; }
    internal struct Quaternion { internal static Quaternion identity => default; }
    internal static class Time { internal static float unscaledTime; internal static int frameCount = 100; }
}
namespace GloomhavenVR.WorldUI
{
    internal static partial class PostQuestRewardSync
    {
        private static MapStoryOpeningLedger Ledger = new();
        private static object? _opening;
        private static bool _consumed;
        internal static uint CurrentKey;
        internal static object? Opening => _opening;
        internal static bool SendDue => Ledger.Changed;
        internal static void Reset() { Ledger = new(); _opening = null; CurrentKey = 0; _consumed = false; }
        internal static void TestOpen(uint key)
        { if (_opening != null) Ledger.Finish(_opening); CurrentKey = key; _opening = new(); Ledger.Open(_opening, key, key, 1, new[] { 1 }); }
        internal static MapStoryOpening[] SampleCompletions() => Ledger.Sample(_opening);
        internal static void ObserveCompletions(int sender, MapStoryOpening[] entries) => Ledger.Observe(sender, entries);
    }
    internal enum SharedWindowKind { RewardShowcase }
    internal sealed class GrabbableModal { internal bool IsGrabbed => false; }
    internal static class RewardShowcase { internal static object? Window; internal static uint ContentKey; }
    internal static class RewardShowcasePlacement
    {
        internal static bool LocalPlacementFailed, LocalRevealPending = true, MayReveal;
        internal static void ClearInitialPose() { }
        internal static void ClearInitialAuthority() => MayReveal = false;
        internal static void SetInitialAuthority(uint key, bool reveal) => MayReveal = reveal;
        internal static void SetInitialPose(uint key, UnityEngine.Vector3 p, UnityEngine.Quaternion r, float s, bool ready) { }
    }
    internal static class SharedWindows
    {
        internal static bool TryGetGrab(SharedWindowKind kind, out GrabbableModal? grab) { grab = new(); return true; }
    }
}
namespace GloomhavenVR.Net
{
    internal static class AvatarSerializer
    {
        internal static void WriteU32(byte[] data, ref int at, uint value)
        { for (int n = 0; n < 4; n++) data[at++] = (byte)(value >> (8 * n)); }
        internal static uint ReadU32(byte[] data, ref int at)
        { uint value = 0; for (int n = 0; n < 4; n++) value |= (uint)data[at++] << (8 * n); return value; }
    }
    internal static class NetProtocol
    {
        internal const byte ExtIdRewardPoseHandshake = 74, SharedOpenBit = 1, SharedPoseBit = 2, StoryPageNone = 255;
        internal static float DecodeStorySize(byte size) => size;
    }
    internal struct RigPose { internal UnityEngine.Vector3 Position; internal UnityEngine.Quaternion Rotation; }
    internal struct SharedWindowEntry { internal uint ContentKey; internal byte Flags, Page, SizeCode, Frame, PoseStamp; internal RigPose Pose; }
    internal struct RewardWindowState { internal bool Unavailable, Moved, Ready; internal SharedWindowEntry Window; }
    internal struct PresenceState { internal bool HasRewardWindow, HasRewardPoseHandshake, HasRewardContinuation; internal MapStoryOpening[]? RewardContinuationEntries; internal RewardWindowState RewardWindow; internal RewardPoseHandshakeState RewardPoseHandshake; }
    internal static class RewardWindowCodec { internal static bool Valid(in RewardWindowState state) => state.Window.ContentKey != 0; }
    internal sealed class RewardPoseHandoff { internal void Reset() { } internal void LocalMove() { } }
    internal static class RewardPosePolicy
    {
        internal static bool Matches(uint key, uint peerKey, bool pose) => key != 0 && key == peerKey && pose;
        internal static int ConsiderInitialOwner(int current, int peer, bool unavailable) => peer > 0 && !unavailable && peer < current ? peer : current;
        internal static bool Eligible(bool localMoved, bool anyMoved, bool moved, int initialOwner, int peer) => moved || (!localMoved && !anyMoved && initialOwner == peer);
    }
    internal static class NetPlayerActors { internal static int LocalId = 2; internal static int LocalPlayerId() => LocalId; }
    internal static class NetAvatarDriver
    {
        internal static readonly List<int> LivePeers = new();
        internal static void CollectRewardPosePeers(List<int> into) { into.Clear(); into.AddRange(LivePeers); }
    }
    internal static class SharedWindowFrame
    {
        internal static bool TryRead(GloomhavenVR.WorldUI.GrabbableModal grab, out UnityEngine.Vector3 p, out UnityEngine.Quaternion r, out float size, bool allowUnrevealed)
        { p = default; r = default; size = 1; return true; }
    }
    internal static partial class RemoteMapStory
    {
        private const int IdentitySettleFrames = 2;
        private const float PeerStaleSeconds = 2;
        private static readonly List<int> Scratch = new();
        private sealed class Local
        {
            internal uint Key;
            internal bool Moving, PoseOwned, HaveBaseline;
            internal float MoveSettleAt, FrameSize;
            internal int SwapFrame = int.MinValue, FollowingPeer;
            internal UnityEngine.Vector3 FramePos;
            internal UnityEngine.Quaternion FrameRot;
            internal GloomhavenVR.WorldUI.GrabbableModal? Grab;
            internal void Reset() { Key = 0; Moving = PoseOwned = HaveBaseline = false; MoveSettleAt = FrameSize = 0; FollowingPeer = 0; SwapFrame = int.MinValue; FramePos = default; FrameRot = default; Grab = null; }
        }
        private readonly struct PeerEntry
        {
            internal readonly uint ContentKey;
            internal readonly float At;
            internal readonly bool HasPose;
            internal readonly byte Frame, SizeCode;
            internal readonly RigPose Pose;
            internal PeerEntry(in SharedWindowEntry e, float at) { ContentKey = e.ContentKey; At = at; HasPose = (e.Flags & 2) != 0; Frame = e.Frame; SizeCode = e.SizeCode; Pose = e.Pose; }
        }
        private static void TrackFrame(GloomhavenVR.WorldUI.SharedWindowKind kind, Local local, bool reset) { local.HaveBaseline = true; }
        private static void WritePose(GloomhavenVR.WorldUI.SharedWindowKind kind, Local local, ref SharedWindowEntry entry) { if (local.PoseOwned) entry.Flags |= 2; entry.Frame = 2; entry.SizeCode = 1; entry.PoseStamp = 0; entry.Pose = new RigPose { Position = default, Rotation = default }; }
        private static void NoteStamp(int sender, in SharedWindowEntry entry, Dictionary<int, byte> stamps, Dictionary<int, float> at, float now) { stamps[sender] = entry.PoseStamp; at[sender] = now; }
        private static void Forget(int sender, Dictionary<int, PeerEntry> peers, Dictionary<int, byte> stamps, Dictionary<int, float> at) { peers.Remove(sender); stamps.Remove(sender); at.Remove(sender); }
        private static void PruneStale(Dictionary<int, PeerEntry> peers, Dictionary<int, byte> stamps, Dictionary<int, float> at) { }
        private static void ResolvePose(GloomhavenVR.WorldUI.SharedWindowKind kind, Local local, uint key, Dictionary<int, PeerEntry> peers, Dictionary<int, float> stamps) { }
        private static bool ToWorld(byte frame, UnityEngine.Vector3 p, UnityEngine.Quaternion r, out UnityEngine.Vector3 pos, out UnityEngine.Quaternion rot, bool scenarioFrame) { pos = p; rot = r; return true; }
        internal static void TestReset() => ResetRewardPose();
        internal static int TestPeerCount => RewardPeers.Count;
        internal static int TestOwner(uint key, int id) => RewardInitialOwner(key, id);
    }
}
