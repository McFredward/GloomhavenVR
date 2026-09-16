using System;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class RewardWindowVectors
{
    internal static void Run(Harness t, string root)
    {
        t.Case("reward window: additive73 golden and unchanged legacy omission");
        var state = new RewardWindowState { Window = new SharedWindowEntry {
            ContentKey = 0x12345678, Flags = NetProtocol.SharedOpenBit, Page = NetProtocol.StoryPageNone } };
        var presence = new PresenceState { HasRewardWindow = true, RewardWindow = state };
        var bytes = new byte[PresenceSerializer.MaxSize];
        int length = PresenceSerializer.Write(in presence, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 49 05 78 56 34 12 00"), bytes, length,
            "reward existence carries only its opaque event key, never content or continuation");
        t.True(PresenceSerializer.TryRead(bytes, length, out var decoded) && decoded.HasRewardWindow
            && decoded.RewardWindow.Window.ContentKey == 0x12345678, "presence dispatch recognizes73");
        var closed = new PresenceState();
        length = PresenceSerializer.Write(in closed, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, length, "closed reward leaves old extras unchanged");
        t.True(PresenceSerializer.TryRead(bytes, length, out decoded) && !decoded.HasRewardWindow,
            "omitted record cannot retain the preceding open state");

        t.Case("reward window: full pose, bounded malformed input and atomic write");
        state.Window.Flags |= NetProtocol.SharedPoseBit;
        state.Window.PoseStamp = 255; state.Window.SizeCode = NetProtocol.StorySizeDefaultCode;
        state.Window.Frame = NetProtocol.RewardFrameScenario;
        state.Window.Pose = new RigPose { Position = new Vector3(1, 2, 3), Rotation = Quaternion.identity };
        state.Moved = true; state.Ready = true;
        int end = 0;
        t.True(RewardWindowCodec.Write(bytes, ref end, in state), "scenario shared-frame pose writes");
        t.Equal(30, end, "record73 costs at most30 bytes including TLV");
        t.True(RewardWindowCodec.TryRead(bytes, 2, end - 2, out var pose) && pose.Moved && pose.Ready
            && pose.Window.PoseStamp == 255 && pose.Window.Frame == NetProtocol.RewardFrameScenario
            && pose.Window.Pose.Position.z == 3, "pose, size, movement and event identity decode together");
        for (int n = 0; n < end - 2; n++)
            t.True(!RewardWindowCodec.TryRead(bytes, 2, n, out _), "truncated full pose refuses " + n);
        foreach (int at in new[] { -1, bytes.Length, int.MaxValue })
            t.True(!RewardWindowCodec.TryRead(bytes, at, 28, out _), "invalid offset refuses " + at);
        t.True(!RewardWindowCodec.TryRead(bytes, 2, 29, out _), "trailing unknown payload is not a valid73 frame");
        foreach (var edit in new (int At, byte Value, string Label)[] {
            (6, 128, "unknown flags"), (6, 2, "movement without pose"), (6, 8, "ready without pose"), (6, 5, "unavailable pose"), (8, 0, "invalid size"), (9, 255, "unknown coordinate frame"), (9, 0, "per-viewer seat frame is not a shared reward frame") })
        {
            var corrupt = (byte[])bytes.Clone(); corrupt[edit.At] = edit.Value;
            t.True(!RewardWindowCodec.TryRead(corrupt, 2, 28, out _), edit.Label);
        }
        var invalid = (byte[])bytes.Clone(); Array.Clear(invalid, 2, 4);
        t.True(!RewardWindowCodec.TryRead(invalid, 2, 28, out _), "zero event key cannot match another window");
        invalid = (byte[])bytes.Clone(); Array.Clear(invalid, 22, 8);
        t.True(!RewardWindowCodec.TryRead(invalid, 2, 28, out _), "zero quaternion cannot move a window");
        invalid = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, invalid, 10, 4);
        t.True(!RewardWindowCodec.TryRead(invalid, 2, 28, out _), "nonfinite position refuses");
        int cursor = 1;
        t.True(!RewardWindowCodec.Write(new byte[30], ref cursor, in state) && cursor == 1,
            "insufficient capacity does not partially write the record");
        var badState = state; badState.Window.Pose.Rotation = new Quaternion(0, 0, 0, 0);
        cursor = 0;
        t.True(!RewardWindowCodec.Write(bytes, ref cursor, in badState) && cursor == 0, "invalid source refuses atomically");
        t.True(4044 + 30 <= ExtrasFragments.MaxSnapshotBytes && PresenceSerializer.MaxSize - (4044 + 30) >= 257,
            "maximum previous records plus reward retain reassembly bound and local spare-record margin");
        state.Window.Frame = NetProtocol.SharedFrameParchment; end = 0;
        t.True(RewardWindowCodec.Write(bytes, ref end, in state) && RewardWindowCodec.TryRead(bytes, 2, 28, out pose)
            && pose.Window.Frame == NetProtocol.SharedFrameParchment, "map frame uses the same additive grammar");

        t.Case("reward window: scenario pose survives independent viewer seats and zoom");
        Vector3 commonPosition = new(12, 5, -7);
        Vector3 firstSeat = new(2, 0, 3), secondSeat = new(-9, 1, 4);
        Quaternion firstYaw = Quaternion.identity, secondYaw = new(0, .70710677f, 0, .70710677f);
        const float firstScale = 12f, secondScale = 20f;
        Vector3 firstView = firstYaw * (commonPosition - firstSeat) / firstScale;
        Vector3 ownerWorld = firstSeat + firstYaw * (firstView * firstScale);
        WorldAnchor.Instance.ToAnchor(ownerWorld, Quaternion.identity, out Vector3 sharedPosition, out Quaternion sharedRotation);
        WorldAnchor.Instance.ToWorld(sharedPosition, sharedRotation, out Vector3 receiverWorld, out _);
        Vector3 secondView = new Quaternion(0, -.70710677f, 0, .70710677f) * (receiverWorld - secondSeat) / secondScale;
        t.True((secondSeat + secondYaw * (secondView * secondScale) - commonPosition).sqrMagnitude < .00001f,
            "different viewer origins, yaw and scale project one common scenario location");
        Vector3 wrongSeatRelative = secondSeat + secondYaw * (firstView * secondScale);
        t.True((wrongSeatRelative - commonPosition).sqrMagnitude > 1f,
            "negative control: reusing sender's seat-relative pose visibly moves the reward on the receiver's board");

        t.Case("reward window: unavailable publisher hands off without a timeout");
        var unavailable = new RewardWindowState { Unavailable = true, Window = new SharedWindowEntry {
            ContentKey = 0x12345678, Flags = NetProtocol.SharedOpenBit } };
        end = 0;
        t.True(RewardWindowCodec.Write(bytes, ref end, in unavailable), "explicit conversion failure is publishable");
        t.Wire(Hex.Bytes("49 05 78 56 34 12 04"), bytes, end, "unavailable is a status bit, never a native close command");
        t.True(RewardWindowCodec.TryRead(bytes, 2, 5, out var failure) && failure.Unavailable,
            "failure survives decode so another ready peer may publish");
        unavailable.Window.Flags |= NetProtocol.SharedPoseBit; unavailable.Window.Pose = state.Window.Pose;
        unavailable.Window.SizeCode = state.Window.SizeCode; unavailable.Window.Frame = state.Window.Frame;
        t.True(!RewardWindowCodec.Valid(in unavailable), "failed publisher cannot claim a ready pose");
        var pendingPose = state; pendingPose.Ready = false; end = 0;
        t.True(RewardWindowCodec.Write(bytes, ref end, in pendingPose)
            && RewardWindowCodec.TryRead(bytes, 2, 28, out var pendingDecoded) && !pendingDecoded.Ready,
            "pending pose can be applied invisibly without releasing another client's first reveal");
        int leader = RewardPosePolicy.ConsiderInitialOwner(int.MaxValue, 2, false);
        leader = RewardPosePolicy.ConsiderInitialOwner(leader, 1, false);
        t.Equal(1, leader, "known lower-id peer retains authority while native conversion is pending");
        leader = RewardPosePolicy.ConsiderInitialOwner(int.MaxValue, 2, false);
        leader = RewardPosePolicy.ConsiderInitialOwner(leader, 1, true);
        t.Equal(2, leader, "explicit failure promotes the next participant without elapsed-time guesses");
        t.Equal(2, RewardPosePolicy.ConsiderInitialOwner(int.MaxValue, 2, false),
            "removed peer is absent from the live membership election");

        t.Case("reward window: first reveal cannot rewind through hidden interpolation history");
        var track = new SharedWindowPoseTrack();
        var handoff = new RewardPoseHandoff();
        var localHome = new RigPose { Position = Vector3.zero, Rotation = Quaternion.identity };
        var pendingTarget = new RigPose { Position = new Vector3(8, 0, 0), Rotation = Quaternion.identity };
        var readyTarget = new RigPose { Position = new Vector3(10, 0, 0), Rotation = Quaternion.identity };
        track.Seed(localHome, 1f, 0f);
        RigPose hidden = handoff.Sample(track, pendingTarget, 1.2f, .03f, true, out float visibleSize);
        t.True(hidden.Position.x == 8 && visibleSize == 1.2f, "hidden receive directly seeds the peer endpoint");
        // WorldUI can reveal the final Ready endpoint while ResolvePose is still in identity
        // settling. No Sample call occurs during that early return, so the latch remains armed.
        RigPose visible = handoff.Sample(track, readyTarget, 1.4f, .05f, false, out visibleSize);
        t.True(visible.Position.x == 10 && visibleSize == 1.4f,
            "first successful visible resolve preserves the exact final pose and size shown by WorldUI");
        track.Reset(); track.Seed(localHome, 1f, 0f); handoff.Reset();
        visible = handoff.Sample(track, readyTarget, 1.4f, .05f, false, out visibleSize);
        t.True(visible.Position.x == 10, "handoff also works when no hidden resolve ran before reveal");
        var nextTarget = readyTarget; nextTarget.Position.x = 12;
        visible = handoff.Sample(track, nextTarget, 1.6f, .1f, false, out visibleSize);
        t.True(visible.Position.x == 10 && visibleSize == 1.4f,
            "subsequent real visible movement retains interpolation instead of snapping");
        visible = handoff.Sample(track, nextTarget, 1.6f, .3f, false, out visibleSize);
        t.True(visible.Position.x == 12 && visibleSize == 1.6f, "visible motion reaches its final endpoint");
        handoff.Reset(); handoff.LocalMove(); track.Reset(); track.Seed(localHome, 1f, 0f);
        visible = handoff.Sample(track, readyTarget, 1.4f, .05f, false, out _);
        t.True(visible.Position.x == 0, "a real local move consumes startup so later peer drags interpolate normally");
        track.Reset(); track.Seed(localHome, 1f, 0f);
        track.Sample(pendingTarget, 1.2f, .03f, out _);
        RigPose oldBehavior = track.Sample(readyTarget, 1.4f, .05f, out _);
        t.True(oldBehavior.Position.x < 10, "negative control: the original hidden-home history rewinds the already revealed endpoint");

        t.Case("reward window: lifecycle and initial-pose election protect real movement");
        t.True(!RewardPosePolicy.Matches(0, 1, true) && !RewardPosePolicy.Matches(2, 1, true)
            && !RewardPosePolicy.Matches(1, 1, false) && RewardPosePolicy.Matches(1, 1, true),
            "closed, predecessor and no-pose entries cannot apply to the next reward");
        t.True(RewardPosePolicy.Eligible(false, false, false, 1, 1)
            && !RewardPosePolicy.Eligible(false, false, false, 1, 2), "only the deterministic initial owner wins before movement");
        t.True(!RewardPosePolicy.Eligible(true, false, false, 1, 1), "late initial placement cannot steal a local drag");
        t.True(!RewardPosePolicy.Eligible(false, true, false, 1, 1)
            && RewardPosePolicy.Eligible(false, true, true, 1, 2), "a remote drag beats lower-id initial placement");
        t.True(RewardPosePolicy.Eligible(true, true, true, 1, 2), "subsequent real movement remains eligible for shared last-mover election");
        // The separate moved flag, rather than stamp !=0, keeps these rules true after byte wrap.
        state.Window.PoseStamp = 0; end = 0;
        t.True(RewardWindowCodec.Write(bytes, ref end, in state) && RewardWindowCodec.TryRead(bytes, 2, 28, out pose)
            && pose.Moved && pose.Window.PoseStamp == 0, "wrapped movement is never mistaken for an initial placement");
        string code = Regex.Replace(File.ReadAllText(Path.Combine(root,
            "src/GloomhavenVR/Net/Remote/RemoteMapStory.Reward.cs")), @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        t.True(ForgetsAbsent(code), "production receiver forgets omitted reward entries and their clocks");
        t.True(!ForgetsAbsent(code.Replace("{ ForgetReward(sender); return; }", "{ return; }")),
            "negative control: retaining a closed peer is rejected");
        t.True(code.Contains("previous.ContentKey != entry.ContentKey") && code.Contains("RewardHandoff.Reset(); RewardLocal.Key = key;"),
            "new event clears peer stamp provenance and local interpolation baseline");
        t.True(code.Contains("SetInitialAuthority(key") && code.Contains("ClearInitialAuthority()")
            && code.Contains("state.Ready = _rewardSentReady = _rewardSentPose && !pending")
            && code.Contains("if (established != int.MaxValue) return established")
            && code.Contains("RewardShowcasePlacement.LocalPlacementFailed != _rewardSentUnavailable"),
            "production advertises readiness changes urgently and resets reveal authority on teardown");
        string driver = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs"));
        t.True(driver.Contains("VersionGuard.PeerBuild(pair.Key) >= NetProtocol.RewardWindowMinPeerBuild"),
            "older VR peers cannot hold the new reward readiness gate indefinitely");
        string shared = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/RemoteMapStory.cs"));
        t.True(shared.Contains("if (scenarioFrame && frame == NetProtocol.RewardFrameScenario)")
            && shared.Contains("WorldAnchor.Instance.ToAnchor(worldPos, worldRot, out localPos, out localRot)")
            && shared.Contains("WorldAnchor.Instance.ToWorld(localPos, localRot, out worldPos, out worldRot)"),
            "reward scenario poses use the avatar's common game-world anchor, never a viewer's orbit focus or seat");
        t.True(NetProtocol.SharedFrameMax == 1 && NetProtocol.RewardFrameScenario == 2,
            "new reward coordinate frame does not widen the frozen legacy21 grammar");
        t.True(code.Contains("ResolvePose(SharedWindowKind.RewardShowcase")
            && code.Contains("return ToWorld(owner.Frame") && code.Contains("WritePose(SharedWindowKind.RewardShowcase"),
            "production uses existing shared coordinate and interpolation paths");
    }
    private static bool ForgetsAbsent(string code) => code.Contains("if (!presence.HasRewardWindow")
        && code.Contains("{ ForgetReward(sender); return; }")
        && code.Contains("Forget(sender, RewardPeers, RewardStamp, RewardStampAt)");
}
