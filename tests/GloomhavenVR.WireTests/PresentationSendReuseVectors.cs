using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class PresentationSendReuseVectors
{
    internal static void Run(Harness t)
    {
        t.Case("native send reuse: four independent senders preserve byte-exact queue boundaries");
        var before = new ExtrasSendScheduler[4];
        var after = new ExtrasSendScheduler[4];
        for (int peer = 0; peer < 4; peer++)
        {
            before[peer] = Scheduler((ulong)(32 * (peer + 1)));
            after[peer] = Scheduler((ulong)(32 * (peer + 1)));
        }
        var bytes = new byte[CardAppearanceCodec.MaxSize];
        for (int frame = 0; frame < 12; frame++)
        {
            for (int peer = 0; peer < 4; peer++)
            {
                bool clear = frame == 5 + peer;
                int actor = frame < 5 ? peer + 1 : peer + 101;
                var appearance = Appearance(frame, actor, clear ? 0 : 12);
                CompareEnqueue(before[peer], after[peer], bytes,
                    CardAppearanceCodec.Write(appearance, bytes), appearance);
                var bonus = new UseBarAnimationSnapshot(frame, clear ? Array.Empty<UseBarAnimationState>() : new[] {
                    new UseBarAnimationState { ActorId = actor, Slot = 0, SlotIdentity = (ushort)actor } });
                CompareEnqueue(before[peer], after[peer], bytes,
                    UseBarAnimationCodec.Write(bonus, bytes), bonus);
                var native = new NativeUseBarSnapshot(frame, 1, (byte)peer, clear ? null : new NativeUseBarState {
                    Bar = 1, Slot = (byte)peer, ActorId = actor, ModelKind = NativeUseBarModelKind.DummyInfusion,
                    SlotIdentity = (ushort)actor, WidgetState = new UseBarWidgetState { Slot = (byte)peer } });
                CompareEnqueue(before[peer], after[peer], bytes,
                    NativeUseBarPacket.Write(native, bytes), native, native.Address);
                var board = Board(frame, clear ? 0u : (uint)actor);
                CompareEnqueue(before[peer], after[peer], bytes,
                    NativeBoardCodec.Write(board, bytes), board);
                var prompt = Prompt(frame, actor, clear);
                CompareEnqueue(before[peer], after[peer], bytes,
                    NativeDecisionPromptCodec.Write(prompt, bytes), prompt);
                // The transport must own payload bytes despite the caller's reused write buffer.
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        int delivered = 0;
        for (int tick = 0; tick < 300; tick++)
            for (int peer = 0; peer < 4; peer++)
            {
                byte[]? expected = before[peer].NextBatch(tick * .051);
                byte[]? actual = after[peer].NextBatch(tick * .051);
                t.True((expected == null) == (actual == null), "same scheduling and draining for sender " + peer);
                if (expected != null && actual != null)
                { t.Wire(expected, actual, actual.Length, "same serialized event and identity boundary"); delivered++; }
            }
        t.True(delivered >= 16, "all four queues delivered multiple complete event batches");
        for (int peer = 0; peer < 4; peer++)
        {
            after[peer].Clear();
            t.True(after[peer].NextBatch(100) == null, "session reset drops all previous presentation");
        }

        t.Case("native send reuse: warmed enqueue allocation excludes redundant decoded card graphs");
        var snapshot = Appearance(1, 1, 32);
        int length = CardAppearanceCodec.Write(snapshot, bytes);
        var decoded = Scheduler(1); var reused = Scheduler(1);
        for (int i = 0; i < 20; i++)
        { decoded.Enqueue(bytes, length); reused.Enqueue(bytes, length, identity: snapshot); }
        const int iterations = 200;
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) decoded.Enqueue(bytes, length);
        long decodedBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) reused.Enqueue(bytes, length, identity: snapshot);
        long reusedBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        t.True(reusedBytes < decodedBytes / 2, "reuse removes decoded object graphs, not required payload ownership");
        Console.WriteLine($"  Native send enqueue allocation (32 cards, {iterations} samples): decoded={decodedBytes} B; reused={reusedBytes} B. .NET harness, not Unity frame timing.");

        t.Case("native node copy: independent animation values without a discarded default array");
        var original = new CardAppearanceNode { Role = 11, Flags = 3, Binding = 0, Mask = 0x80 };
        for (int i = 0; i < original.Values.Length; i++) original.Values[i] = i * .03125f;
        CardAppearanceNode copy = original.Copy();
        t.True(copy.Role == original.Role && copy.Flags == original.Flags && copy.Binding == original.Binding
            && copy.Mask == original.Mask && !ReferenceEquals(copy.Values, original.Values), "copy retains fields and owns its values");
        for (int i = 0; i < copy.Values.Length; i++) t.True(copy.Values[i] == original.Values[i], "every native channel is copied");
        copy.Values[0] = .75f;
        t.True(original.Values[0] == 0f, "editing a copied frame never changes its predecessor");
        for (int i = 0; i < 20; i++) { GC.KeepAlive(original.Copy()); GC.KeepAlive(LegacyCopy(original)); }
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) GC.KeepAlive(LegacyCopy(original));
        long legacyCopyBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) GC.KeepAlive(original.Copy());
        long copyBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        t.True(copyBytes < legacyCopyBytes, "deep copies no longer allocate a discarded default array");
        Console.WriteLine($"  Native node copy allocation ({iterations} copies): legacy={legacyCopyBytes} B; current={copyBytes} B.");
    }

    private static CardAppearanceNode LegacyCopy(CardAppearanceNode node) => new() {
        Role = node.Role, Flags = node.Flags, Binding = node.Binding, Mask = node.Mask, Values = (float[])node.Values.Clone() };

    private static ExtrasSendScheduler Scheduler(ulong seed) => new(seed,
        NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);

    private static void CompareEnqueue(ExtrasSendScheduler before, ExtrasSendScheduler after,
        byte[] bytes, int length, object identity, int slot = -1)
    {
        if (length <= 0) throw new InvalidOperationException("Invalid native send fixture.");
        before.Enqueue(bytes, length, slot);
        after.Enqueue(bytes, length, slot, identity);
    }

    private static CardAppearanceSnapshot Appearance(int frame, int actor, int count)
    {
        var states = new CardAppearanceState[count];
        for (int i = 0; i < count; i++)
        {
            var node = new CardAppearanceNode { Role = 0, Flags = 3, Mask = 1 };
            node.Values[8] = frame / 16f;
            states[i] = new CardAppearanceState { ActorId = actor + i * 1000, FaceCode = 0x20,
                ListCount = 1, Nodes = new[] { node }, SourceActorId = actor + i * 1000, PoolCount = 1 };
        }
        return new CardAppearanceSnapshot(frame, states);
    }

    private static NativeBoardState Board(int frame, uint generation)
    {
        var elements = generation == 0 ? Array.Empty<NativeElementState>() : new NativeElementState[6];
        for (byte i = 0; i < elements.Length; i++)
        {
            var graphics = new NativeElementGraphic[7];
            for (int j = 0; j < graphics.Length; j++) graphics[j] = new NativeElementGraphic { A = frame / 16f };
            elements[i] = new NativeElementState { Sibling = i, Graphics = graphics,
                Animations = new[] { Array.Empty<UseBarAnimationValue>(), Array.Empty<UseBarAnimationValue>(), Array.Empty<UseBarAnimationValue>() } };
        }
        return new NativeBoardState(frame, frame, generation, elements);
    }

    private static float[] Rect() { var rect = new float[18]; rect[17] = 1; return rect; }
    private static NativeDecisionPromptSnapshot Prompt(int frame, int actor, bool clear) => new(frame,
        clear ? 0 : actor, clear ? null : new NativeDecisionPromptState { Rect = Rect(), Lines = new[] {
            new NativeDecisionPromptLine { Rect = Rect(),
                Tip = new NativeDecisionPromptText { Rect = Rect(), Text = actor.ToString() },
                Warning = new NativeDecisionPromptText { Rect = Rect(), Text = "warning" } } } });
}
