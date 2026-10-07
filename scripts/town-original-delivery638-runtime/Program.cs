using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WireTests;

namespace GloomhavenVR.TownOriginalDelivery638;

/// <summary>Reflection is the exact native boundary used by production SendToken.
/// The fake channel deliberately loses the first unreliable event. Reliable
/// delivery is simulated; this proves selection/dependency, not Bolt latency.</summary>
internal static class Program
{
    private static int Main()
    {
        var t = new Harness(); TownOriginalDelivery638Vectors.Run(t);
        var sender = new BoundSender();
        t.Case("Production SendToken reflection flags and exception cleanup");
        foreach (int type in new[] { NetProtocol.MsgRig, NetProtocol.MsgExtras, NetProtocol.MsgTownMotion })
        {
            byte[] packet = Packet(type, 6); FakeNative.Last = null; sender.Send(packet);
            InspectNative(t, sender, packet, expectedUnreliable: true);
        }
        foreach (int lane in new[] { 0, 1, 2 })
        foreach (bool compressed in new[] { false, true })
        {
            TownServiceFrame frame = Frame(lane);
            byte[] original = TownServiceCodec.Write(frame);
            byte[][] pages = Pages(original, lane, compressed, 1);
            t.True(pages.Length > 1, "first original really spans multiple native events");
            foreach (byte[] page in pages)
            {
                FakeNative.Last = null; sender.Send(page);
                InspectNative(t, sender, page, expectedUnreliable: false);
                t.True(page.Length <= 864, "original retains the native event cap");
                t.Equal(compressed ? NetProtocol.MsgPresentationCompression : TownServiceCodec.FragmentType,
                    NetPacket.PeekType(page, page.Length), "actual original codec exercises its requested envelope");
            }
            // A native batch may hold a complete short town page alongside ordinary
            // presence. Classify the whole event, without a second native send.
            byte[] shortOriginal = TownServiceCodec.Write(Frame(lane, 32));
            byte[] shortPage = Pages(shortOriginal, lane, false, 2)[0];
            byte[] batch = PresentationBatch.Write(new List<byte[]> { Packet(NetProtocol.MsgExtras, 6), shortPage });
            sender.Send(batch); InspectNative(t, sender, batch, expectedUnreliable: false);
            if (compressed)
            {
                byte[] tail = pages[pages.Length - 1];
                t.True(tail.Length + 17 <= 864, "real compressed tail admits a mixed native batch");
                if (tail.Length + 17 <= 864)
                {
                    batch = PresentationBatch.Write(new List<byte[]> { Packet(NetProtocol.MsgExtras, 6), tail });
                    sender.Send(batch); InspectNative(t, sender, batch, expectedUnreliable: false);
                }
            }
            ProveFirstPageLoss(t, sender, original, pages, lane, compressed);
        }
        foreach (bool throws in new[] { false, true })
        {
            FakeNative.Throw = throws; bool failed = false;
            try { sender.Send(Packet(TownServiceCodec.MessageType, 6)); }
            catch (TargetInvocationException) { failed = true; }
            t.Equal(throws, failed, "native reflection exception is observed without swallowing it");
            t.True(sender.Unreliable, "reusable native argument resets after either outcome");
        }
        FakeNative.Throw = false;
        t.Case("Production NextBatch preserves budget, 50 ms clock and no catch-up");
        var scheduler = new BoundScheduler();
        for (int i = 0; i < 10; i++) scheduler.Add(Packet(NetProtocol.MsgExtras, 430));
        byte[]? first = scheduler.NextBatch(0); t.True(first != null && first.Length <= 864, "first bounded event exists");
        t.True(scheduler.NextBatch(.049) == null, "no second native event before 50 ms");
        t.True(scheduler.NextBatch(.050001) != null, "next event is admitted after 50 ms");
        t.True(scheduler.NextBatch(20) != null, "long frame admits exactly one event");
        t.True(scheduler.NextBatch(20) == null, "late tick cannot burst to catch up");
        scheduler = new BoundScheduler();
        for (int i = 0; i < 80; i++) scheduler.Add(Packet(NetProtocol.MsgExtras, 6));
        byte[]? crowded = null;
        try { crowded = scheduler.NextBatch(0); }
        catch (ArgumentException) { /* A mutated bound must fail an assertion, not abort the proof. */ }
        t.True(crowded != null && crowded.Length <= 864 && crowded[6] == 32, "existing 32-child batch bound remains exact");
        return t.Report();
    }

    private static void InspectNative(Harness t, BoundSender sender, byte[] expected, bool expectedUnreliable)
    {
        t.True(FakeNative.Last != null, "native sender is invoked through its real reflection signature");
        t.Equal(expectedUnreliable, FakeNative.Last!.Unreliable, "canBeUnreliable selects the dependency channel");
        t.True(ReferenceEquals(expected, FakeNative.Last.Token.Bytes), "selector neither copies nor mutates native payload");
        t.True(!FakeNative.Last.Token.Compress, "native CustomDataToken compression remains disabled");
        t.True(sender.Unreliable, "reusable native argument is restored after send");
        t.True(FakeNative.Last.Action == 109 && !FakeNative.Last.Broadcast
            && FakeNative.Last.Target == -932 && FakeNative.Last.From == 0 && FakeNative.Last.To == 0
            && !FakeNative.Last.Final, "all native authority/target arguments are unchanged");
    }

    private static void ProveFirstPageLoss(Harness t, BoundSender sender, byte[] original, byte[][] pages, int lane, bool compressed)
    {
        t.Case("First-original fragment dependency survives one lost unreliable event");
        var receiver = new TownServiceFragments(); byte[]? assembled = null;
        // Native retransmission is modeled only at this explicit channel boundary:
        // the first event is lost, then retransmitted iff the actual flag says reliable.
        for (int i = 0; i < pages.Length; i++)
        {
            sender.Send(pages[i]);
            if (i == 0) continue;
            byte[]? result = receiver.Accept(7, pages[i], pages[i].Length, i * .05);
            if (result != null) assembled = result;
        }
        sender.Send(pages[0]);
        if (!FakeNative.Last!.Unreliable)
            assembled = receiver.Accept(7, pages[0], pages[0].Length, pages.Length * .05) ?? assembled;
        t.True(assembled != null, "actual selected flag permits immediate complete original despite a first-event loss");
        if (assembled != null) t.Wire(original, assembled, assembled.Length, "recovered original is byte exact");
        // Historical control always drops the first UDP event. Every subsequent
        // page arrives, but no original exists for later deltas to reference.
        var lossy = new TownServiceFragments(); bool incomplete = true;
        for (int i = 1; i < pages.Length; i++)
            incomplete &= lossy.Accept(8, pages[i], pages[i].Length, i * .05) == null;
        t.True(incomplete, "unreliable historical control cannot assemble the original when page zero is lost");
        TownServiceFrame next = Frame(lane); next.Sequence = 2; next.Nodes[0].Values[TownServiceProperty.LegacyText].Text[0] += " changed";
        TownServiceFrame basis = Frame(lane); TownServiceFrame delta = TownServiceDelta.Create(basis, next);
        byte[] patch = TownServiceCodec.Write(delta); byte[]? deliveredDelta = null;
        foreach (byte[] page in Pages(patch, lane, compressed, 2))
            deliveredDelta = lossy.Accept(8, page, page.Length, 1) ?? deliveredDelta;
        t.True(deliveredDelta != null && TownServiceCodec.TryRead(deliveredDelta, deliveredDelta.Length, out TownServiceFrame? decoded)
            && decoded!.BaseSequence == 1, "later cumulative delta still depends on the missing original");
    }

    private static byte[][] Pages(byte[] original, int lane, bool compressed, ulong generation)
    {
        ulong marker = lane == 2 ? TownServiceFragments.StockLaneMarker : (ulong)(lane << 16);
        return ExtrasFragments.Encode(original, original.Length, marker | generation << 17 | 41,
            TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: compressed);
    }

    private static TownServiceFrame Frame(int lane, int chars = 4000)
    {
        var random = new Random(271828 + lane); var text = new char[chars];
        for (int i = 0; i < text.Length; i++) text[i] = (char)('a' + random.Next(26));
        var node = new TownServiceNode { Binding = 1 };
        node.Values.Add(TownServiceProperty.LegacyText, new TownServiceValue { Text = new[] { new string(text) } });
        return new TownServiceFrame { PublicCatalog = lane == 1, VisitorStock = lane == 2,
            Service = 1, Session = 1, Sequence = 1, Module = 41, Template = 1, Structure = 42,
            TemplateAddress = lane == 2 ? "merchant.heldstock|" : "item.41|", Visible = true,
            Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f }, Nodes = new[] { node } };
    }

    private static byte[] Packet(int type, int length)
    { var packet = new byte[length]; int at = 0; AvatarSerializer.WriteU32(packet, ref at, NetProtocol.Magic);
      packet[4] = NetProtocol.Version; packet[5] = (byte)type; return packet; }
}

public sealed class FakeToken
{
    internal readonly byte[] Bytes; internal readonly bool Compress;
    public FakeToken(byte[] bytes, bool compress) { Bytes = bytes; Compress = compress; }
}
internal sealed class CapturedCall
{
    internal FakeToken Token = null!; internal int Action, Target, From, To;
    internal bool Unreliable, Broadcast, Final;
}
internal static class FakeNative
{
    internal static CapturedCall? Last; internal static bool Throw;
    public static void Send(int action, FakeToken token, bool canBeUnreliable, bool broadcast, int target, int from, int to, bool final)
    { Last = new CapturedCall { Action = action, Token = token, Unreliable = canBeUnreliable,
        Broadcast = broadcast, Target = target, From = from, To = to, Final = final };
      if (Throw) throw new InvalidOperationException("native boundary throw"); }
}
