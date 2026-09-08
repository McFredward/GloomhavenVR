using System;
using System.Collections.Generic;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class PresentationBoundaryVectors
{
    internal static void Run(Harness t)
    {
        t.Case("presentation sender: clear/reopen survives an already waiting initial pose");
        var queue = new ExtrasSendQueue(8, NetProtocol.MsgNativeUseBar, NetProtocol.MsgNativeUseBarFragments,
            preserveFirst: true, snapshotLimit: NativeUseBarPacket.MaxSize, sequenceStride: 32);
        var buffer = new byte[NativeUseBarPacket.MaxSize];
        var received = new List<NativeUseBarSnapshot>();
        var pending = new List<NativeUseBarSnapshot>();
        for (int i = 0; i < 10; i++)
        {
            NativeUseBarSnapshot frame = Native(i, i == 5);
            int length = NativeUseBarPacket.Write(frame, buffer);
            queue.Enqueue(buffer, length, frame);
            PresentationPending.Append(pending, frame, PresentationPending.SameNativeIdentity);
            t.True(pending.Count <= 4, "receive burst remains bounded");
        }
        var assembler = new ExtrasFragments(NetProtocol.MsgNativeUseBar, NetProtocol.MsgNativeUseBarFragments, NativeUseBarPacket.MaxSize);
        for (int i = 0; i < 30; i++)
        {
            byte[]? page = queue.Next(i * .051);
            if (page == null) continue;
            byte[]? complete = assembler.Accept(123, page, page.Length, i * .051);
            if (complete != null && NativeUseBarPacket.TryRead(complete, complete.Length, out NativeUseBarSnapshot? frame)) received.Add(frame!);
        }
        CheckNative(t, received, "sender"); CheckNative(t, pending, "receiver");

        t.Case("presentation sender: active-bonus slot absence survives partial row changes");
        var bonusQueue = new ExtrasSendQueue(1, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments, preserveFirst: true);
        var bonusPending = new List<UseBarAnimationSnapshot>();
        var bonusBuffer = new byte[UseBarAnimationCodec.MaxSize];
        for (int i = 0; i < 10; i++)
        {
            var frame = new UseBarAnimationSnapshot(i, i == 5
                ? new[] { Bonus(1) } : new[] { Bonus(0), Bonus(1) });
            int length = UseBarAnimationCodec.Write(frame, bonusBuffer);
            bonusQueue.Enqueue(bonusBuffer, length, frame);
            PresentationPending.Append(bonusPending, frame, PresentationPending.SameBonusIdentity);
        }
        var bonusAssembler = new ExtrasFragments(NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        var bonuses = new List<UseBarAnimationSnapshot>();
        for (int i = 0; i < 30; i++)
        {
            byte[]? page = bonusQueue.Next(i * .051); if (page == null) continue;
            byte[]? complete = bonusAssembler.Accept(123, page, page.Length, i * .051);
            if (complete != null && UseBarAnimationCodec.TryRead(complete, complete.Length, out UseBarAnimationSnapshot? frame)) bonuses.Add(frame!);
        }
        t.True(bonuses.Count == 4 && bonuses[1].SampleTime == 5 && bonuses[1].States.Length == 1
            && bonuses[2].SampleTime == 6 && bonuses[2].States.Length == 2 && bonuses[3].SampleTime == 9,
            "sender preserves the last absence and its immediate successor");
        t.True(bonusPending.Count == 4 && bonusPending[1].States.Length == 1 && bonusPending[2].SampleTime == 6,
            "receiver preserves a partial row absence, not just an entirely empty row");

        t.Case("presentation pending: a delivered plume episode cannot disappear in a receive burst");
        var plumes = new List<CardPlumeSnapshot>();
        for (int i = 0; i < 10; i++)
        {
            var emitter = new CardPlumeState { ActorId = 1, FaceCode = 32, ListCount = 1,
                Episode = i < 5 ? 1u : 2u, LocalRotation = UnityEngine.Quaternion.identity };
            var plume = new CardPlumeSnapshot(i, i == 5 ? Array.Empty<CardPlumeState>() : new[] { emitter });
            PresentationPending.Append(plumes, plume, PresentationPending.SamePlumeIdentity);
        }
        t.True(plumes.Count == 4 && plumes[0].SampleTime == 0 && plumes[1].States.Length == 0
            && plumes[2].SampleTime == 6 && plumes[3].SampleTime == 9,
            "received burst retains first episode, clear, new episode and final frame");

        t.Case("presentation pending: latest replacement boundary remains ordered under repeated changes");
        var identities = new List<int>();
        foreach (int value in new[] { 1, 1, 2, 2, 3, 3, 1, 1, 1 })
            PresentationPending.Append(identities, value, (a, b) => a == b);
        t.True(identities.Count == 4 && identities[0] == 1 && identities[1] == 3 && identities[2] == 1,
            "return to an older identity still includes its different predecessor");
    }
    private static void CheckNative(Harness t, List<NativeUseBarSnapshot> frames, string path) =>
        t.True(frames.Count == 4 && frames[0].SampleTime == 0 && frames[1].SampleTime == 5 && frames[1].State == null
            && frames[2].SampleTime == 6 && frames[2].State != null && frames[3].SampleTime == 9,
            path + " retains opening, clear, reopening and latest pose in order");
    private static NativeUseBarSnapshot Native(int time, bool clear) => new(time, 1, 0, clear ? null : new NativeUseBarState {
        Bar = 1, Slot = 0, ActorId = 123, ModelKind = NativeUseBarModelKind.DummyInfusion,
        SlotIdentity = 1, WidgetState = new UseBarWidgetState { Slot = 0 } });
    private static UseBarAnimationState Bonus(byte slot) => new() {
        Slot = slot, ActorId = 123, SlotIdentity = (ushort)(slot + 1), Entries = Array.Empty<UseBarAnimationValue>() };
}
