using System;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class CardPlumeVectors
{
    internal static void Run(Harness t)
    {
        t.Case("51: native card plume frame has independent exact bytes");
        var state = new CardPlumeState { ActorId = 42, FaceCode = 34, ListCount = 3,
            Flags = CardPlumeState.Playing, Episode = 7, RandomSeed = 9, Age = .5f, PlaybackRate = 1,
            StartSizeMultiplier = .35f, StartSpeedMultiplier = .35f,
            Color = new Color(.1f, .2f, .3f, .4f), LocalPosition = new Vector3(1, 2, 3),
            LocalRotation = Quaternion.identity, LocalScale = Vector3.one * .01f };
        var buffer = new byte[CardPlumeCodec.MaxSize];
        var frame = new CardPlumeSnapshot(2, new[] { state });
        int length = CardPlumeCodec.Write(frame, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 05 33 5e 00 01 00 00 00 40 2a 00 00 00 22 03 01 00 07 00 00 00 09 00 00 00 00 00 00 3f 00 00 80 3f 33 33 b3 3e 33 33 b3 3e cd cc cc 3d cd cc 4c 3e 9a 99 99 3e cd cc cc 3e 00 00 80 3f 00 00 00 40 00 00 40 40 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3f 0a d7 23 3c 0a d7 23 3c 0a d7 23 3c"),
            buffer, length, "native time, color, emitter recipe and board-local pose preserve their values");
        t.True(CardPlumeCodec.TryRead(buffer, length, out CardPlumeSnapshot? read)
            && read!.SampleTime == 2 && CardPlumeState.Same(state, read.States[0]), "golden smoke frame parses");
        var golden = new byte[length]; Array.Copy(buffer, golden, length);
        state.Age = 7;
        t.Equal(.5f, frame.States[0].Age, "snapshot retains the actual published frame");
        state.Age = .5f;
        var maximum = new CardPlumeState[CardPlumeState.CountMax];
        for (int i = 0; i < maximum.Length; i++)
        { maximum[i] = state.Snapshot(); maximum[i].ActorId = i + 1; maximum[i].Flags |= 32;
            maximum[i].CustomSpacePresent = true; maximum[i].CustomRotation = Quaternion.identity;
            maximum[i].CustomScale = Vector3.one; }
        length = CardPlumeCodec.Write(new CardPlumeSnapshot(3, maximum), buffer);
        t.Equal(CardPlumeCodec.MaxEncodedBytes, length, "maximum native emitters fit the documented bound");
        t.True(length < CardPlumeCodec.MaxSize, "smoke leaves room within its dedicated transport buffer");
        for (int n = 0; n < length; n++)
            t.True(!CardPlumeCodec.TryRead(buffer, n, out _), "a torn smoke frame cannot publish a partial live set");
        t.True(CardPlumeCodec.TryRead(buffer, length, out read) && read!.States.Length == 64, "whole maximum smoke frame publishes atomically");
        var fragments = ExtrasFragments.Encode(buffer, length, 1, NetProtocol.MsgCardPlume,
            NetProtocol.MsgCardPlumeFragments, CardPlumeCodec.MaxSize);
        var assembler = new ExtrasFragments(NetProtocol.MsgCardPlume, NetProtocol.MsgCardPlumeFragments, CardPlumeCodec.MaxSize);
        byte[]? assembled = null;
        for (int i = fragments.Length - 1; i >= 0; i--)
        {
            t.True(fragments[i].Length <= ExtrasFragments.MaxDatagramBytes, "plume event stays below the Bolt budget");
            assembled = assembler.Accept(4, fragments[i], fragments[i].Length, .01 * i) ?? assembled;
        }
        t.True(assembled != null && CardPlumeCodec.TryRead(assembled, assembled.Length, out read)
            && read!.States.Length == 64, "large smoke frame survives reverse fragment order");
        var duplicate = new byte[golden.Length + 96];
        Array.Copy(golden, duplicate, golden.Length); Array.Copy(golden, 6, duplicate, golden.Length, 96);
        t.True(CardPlumeCodec.TryRead(duplicate, duplicate.Length, out _), "identical repeated emitter record is inert");
        duplicate[duplicate.Length - 1] ^= 1;
        t.True(!CardPlumeCodec.TryRead(duplicate, duplicate.Length, out _), "conflicting emitter duplicate poisons the entire frame");
        state.FaceCode = (byte)((CardPlumeState.RoundList << NetProtocol.HeldFaceListShift) | 1);
        state.ListCount = 2;
        t.True(state.Validate(), "plume-only round source is addressable");
        t.True(!NetProtocol.HeldFaceNamesCard(state.FaceCode), "record36 retains its original six-list domain");
        state.LocalRotation = default;
        t.True(!state.Validate(), "zero quaternion cannot enter native particle placement");
        length = CardPlumeCodec.Write(new CardPlumeSnapshot(4, Array.Empty<CardPlumeState>()), buffer);
        t.Equal(14, length, "complete clear frame remains small");
        t.True(CardPlumeCodec.TryRead(buffer, length, out read) && read!.States.Length == 0, "clear withdraws all finished emitters");

        t.Case("presentation batches retain independent pages within the event cap");
        var batcher = new ExtrasSendScheduler(1000, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        byte[] SlotPage(byte n) => new byte[] { 0x31, 0x52, 0x56, 0x47, 3, 7, n };
        batcher.Enqueue(SlotPage(1), 7, 8); batcher.Enqueue(SlotPage(2), 7, 9);
        byte[] batch = batcher.NextBatch(0)!;
        t.True(PresentationBatch.TryRead(batch, batch.Length, out byte[][]? children) && children!.Length == 2,
            "two small independent slot envelopes share one event");
        t.True(ExtrasFragments.NativeSlotStream(children![0], children[0].Length) == 8
            && ExtrasFragments.NativeSlotStream(children[1], children[1].Length) == 9, "batching retains stream identities");
        for (int i = 0; i < batch.Length; i++)
            t.True(!PresentationBatch.TryRead(batch, i, out _), "a torn batch cannot dispatch its valid prefix");
        t.True(batcher.NextBatch(.01) == null, "coalescing never adds a catch-up event");
        var nested = (byte[])batch.Clone(); nested[14] = NetProtocol.MsgPresentationBatch;
        t.True(!PresentationBatch.TryRead(nested, nested.Length, out _), "nested batch is rejected atomically");
        var announcement = new byte[] { 0x31, 0x52, 0x56, 0x47, 3, 1 };
        t.True(ReferenceEquals(announcement, batcher.NextBatch(.06, announcement)), "version announcement stays directly recognizable");

        t.Case("native slot envelopes keep each slot's generation independent");
        byte[] Native(byte tail) => new byte[] { 0x31, 0x52, 0x56, 0x47, 3, 7, tail };
        var scheduler = new ExtrasSendScheduler(1000, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        scheduler.Enqueue(Native(1), 7, 8); scheduler.Enqueue(Native(2), 7, 9);
        byte[]? first = scheduler.Next(0), second = scheduler.Next(.06);
        t.True(first != null && ExtrasFragments.NativeSlotStream(first, first.Length) == 8, "first auxiliary slot owns stream8");
        t.True(second != null && ExtrasFragments.NativeSlotStream(second, second.Length) == 9, "second auxiliary slot does not overwrite first waiting slot");
        t.True(scheduler.Next(.061) == null, "extra streams still share the same no-burst cadence");
    }
}
