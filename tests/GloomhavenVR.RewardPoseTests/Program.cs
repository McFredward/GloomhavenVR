using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string message) { _assertions++; if (!value) throw new Exception(message); }
    private static readonly List<int> Peers = new() { 1, 2, 3, 4 };
    private static void Refresh(RewardPoseHandshake h, int id, float now = 0) => h.Refresh(id, Peers, now, 2);
    private static void Deliver(RewardPoseHandshake from, int sender, RewardPoseHandshake to, float now = 0)
    { var state = from.Sample(); to.Observe(sender, in state, now); }
    private static void Main()
    {
        var owner = new RewardPoseHandshake(); var absent = new RewardPoseHandshake();
        owner.SetLocalKey(10); Refresh(owner, 2);
        Check(!owner.PeerDeclined(1, 10), "silence is never a decline");
        Deliver(owner, 2, absent); Refresh(absent, 1);
        Check(absent.LocalDeclined(10), "nonparticipant declares inability for requested key");
        Deliver(absent, 1, owner); Refresh(owner, 2);
        Check(owner.PeerDeclined(1, 10), "explicit response releases absent lower-id election");
        Check(!owner.PeerDeclined(1, 11), "decline never applies to another chest");
        absent.SetLocalKey(10); Refresh(absent, 1);
        Check(absent.LocalDeclined(10), "late opening retains follower role");
        Deliver(absent, 1, owner);
        Check(owner.PeerDeclined(1, 10), "late native window cannot revoke its initial decline");
        owner.SetLocalKey(0); owner.SetLocalKey(10); Refresh(owner, 2);
        Check(!owner.PeerDeclined(1, 10), "old generation does not exclude source on reopened chest");
        Deliver(owner, 2, absent); Refresh(absent, 1); Deliver(absent, 1, owner);
        Check(owner.PeerDeclined(1, 10), "fresh response targets fresh generation");

        var middle = new RewardPoseHandshake(); middle.SetLocalKey(20); Refresh(middle, 3);
        Deliver(owner, 2, middle); Refresh(middle, 3); Deliver(middle, 3, owner); Refresh(owner, 2);
        Check(owner.PeerDeclined(3, 10), "different simultaneous native window explicitly declines this key");
        var state = middle.Sample();
        Check(state.Key == 20 && state.Count == 1 && state.At(0).Key == 10, "response preserves actual native presentation identity");
        Check(owner.LocalDeclined(20), "independent simultaneous key also receives decline");
        for (int id = 1; id <= 4; id++)
        {
            var request = new RewardPoseHandshakeState { Key = (uint)(30 + id), Generation = 1 };
            var receiver = new RewardPoseHandshake();
            for (int sender = 1; sender <= 4; sender++)
            { request.Key = (uint)(30 + sender); receiver.Observe(sender, in request, 0); }
            Refresh(receiver, 5);
            Check(receiver.Sample().Count == 4, "all four simultaneous request slots retained");
        }
        Refresh(owner, 2, 3);
        Check(!owner.PeerDeclined(1, 10), "stale peer response expires");
        absent.SetLocalKey(0); absent.Refresh(1, new List<int>(), 3, 2);
        Check(!absent.LocalDeclined(10), "closed local window plus departed requests clears declined role");
        owner.Reset(); owner.SetLocalKey(10); Refresh(owner, 2);
        Check(!owner.PeerDeclined(1, 10), "session reset forgets old handshake");
        Deliver(owner, 2, absent); Refresh(absent, 1); Deliver(absent, 1, owner); Refresh(owner, 2);
        Check(owner.PeerDeclined(1, 10), "rejoined healthy nonparticipant responds again");
        Codec(); Integration();
        Console.WriteLine($"Reward pose production tests: {_assertions} assertions passed.");
    }
    private static void Codec()
    {
        var state = new RewardPoseHandshakeState { Key = 0x12345678, Generation = 0x90ABCDEF, Count = 4 };
        for (int i = 0; i < 4; i++) state.Set(i, new RewardPoseDecline { Requester = i + 1, Key = (uint)(i + 20), Generation = (uint)(i + 30) });
        var bytes = new byte[100]; int offset = 0;
        Check(RewardPoseHandshakeCodec.Write(bytes, ref offset, in state) && offset == 59, "maximum bounded packet");
        Check(bytes[0] == 74 && bytes[1] == 57 && bytes[2] == 0x78 && bytes[5] == 0x12 && bytes[10] == 4, "independent header vector");
        Check(RewardPoseHandshakeCodec.TryRead(bytes, 2, 57, out var read) && read.At(3).Requester == 4 && read.Generation == state.Generation, "complete decode");
        for (int length = 0; length < 57; length++) Check(!RewardPoseHandshakeCodec.TryRead(bytes, 2, length, out _), "truncated payload rejected");
        for (int at = -10; at < 0; at++) Check(!RewardPoseHandshakeCodec.TryRead(bytes, at, 57, out _), "negative offset rejected");
        state.Second = state.First;
        offset = 0; Check(!RewardPoseHandshakeCodec.Write(bytes, ref offset, in state) && offset == 0, "duplicate requester refused atomically");
        state.Count = 0; state.Key = 0;
        Check(!RewardPoseHandshakeCodec.Valid(in state), "generation without request rejected");
        state.Generation = 0; state.Count = 5;
        Check(!RewardPoseHandshakeCodec.Valid(in state), "oversized count rejected");
        Check(!RewardPoseHandshakeCodec.TryRead(bytes, int.MaxValue, 57, out _), "overflow offset rejected");
    }
    private static void Integration()
    {
        RemoteMapStory.TestReset(); Time.unscaledTime = 0; NetAvatarDriver.LivePeers.Clear(); NetAvatarDriver.LivePeers.Add(1);
        RewardShowcase.ContentKey = 42; RewardShowcase.Window = new object();
        RewardShowcasePlacement.LocalRevealPending = true; RewardShowcasePlacement.LocalPlacementFailed = false;
        RemoteMapStory.ResolveReward(2);
        Check(!RewardShowcasePlacement.MayReveal, "production follower waits for unknown lower-id publisher");
        PresenceState outgoing = default; RemoteMapStory.SampleReward(ref outgoing, 2);
        Check(outgoing.HasRewardWindow && outgoing.HasRewardPoseHandshake && outgoing.RewardPoseHandshake.Key == 42, "production sends opening request beside original reward state");
        var absent = new RewardPoseHandshake(); absent.Observe(2, in outgoing.RewardPoseHandshake, 0); Refresh(absent, 1);
        PresenceState response = new() { HasRewardPoseHandshake = true, RewardPoseHandshake = absent.Sample() };
        RemoteMapStory.ObserveReward(1, in response); RemoteMapStory.ResolveReward(2);
        Check(RewardShowcasePlacement.MayReveal && RemoteMapStory.TestOwner(42, 2) == 2, "production explicit no-window response permits actual controller reveal");
        outgoing = default; RemoteMapStory.SampleReward(ref outgoing, 2);
        Check((outgoing.RewardWindow.Window.Flags & NetProtocol.SharedPoseBit) != 0, "promoted participant supplies shared pose before first reveal");
        RewardShowcasePlacement.LocalRevealPending = false;
        outgoing = default; RemoteMapStory.SampleReward(ref outgoing, 2);
        Check(outgoing.RewardWindow.Ready, "native reveal produces ready pose for late followers");
        RemoteMapStory.TestReset(); RewardShowcase.ContentKey = 0; RewardShowcase.Window = null;
        response = new() { HasRewardWindow = true, RewardWindow = new() { Window = new() { ContentKey = 42 } },
            HasRewardPoseHandshake = true, RewardPoseHandshake = new() { Key = 42, Generation = 1 } };
        NetAvatarDriver.LivePeers.Clear(); NetAvatarDriver.LivePeers.Add(2);
        RemoteMapStory.ObserveReward(2, in response); RemoteMapStory.ResolveReward(1);
        Check(RemoteMapStory.RewardSendDue, "nonparticipant response preempts idle presence throttle");
        outgoing = default; RemoteMapStory.SampleReward(ref outgoing, 1);
        Check(!outgoing.HasRewardWindow && outgoing.HasRewardPoseHandshake && outgoing.RewardPoseHandshake.Count == 1, "no native window still sends explicit separate decline");
        RewardShowcase.ContentKey = 42; RewardShowcase.Window = new object();
        RemoteMapStory.ResolveReward(1);
        Check(RemoteMapStory.TestOwner(42, 1) == 2 && !RewardShowcasePlacement.MayReveal, "production late opening remains follower");
        PresenceState delayedEmpty = default;
        RemoteMapStory.ObserveReward(2, in delayedEmpty); RemoteMapStory.ResolveReward(1);
        Check(!RewardShowcasePlacement.MayReveal && RemoteMapStory.TestOwner(42, 1) == 2,
            "late empty extras cannot revoke follower role while originating avatar remains live");
        NetAvatarDriver.LivePeers.Clear(); RemoteMapStory.ResolveReward(1);
        Check(RewardShowcasePlacement.MayReveal, "departed sole source cannot keep remaining panel hidden");
        outgoing = default; RemoteMapStory.SampleReward(ref outgoing, 1);
        Check((outgoing.RewardWindow.Window.Flags & NetProtocol.SharedPoseBit) != 0,
            "last surviving native copy becomes a real pose publisher after source departure");
    }
}
