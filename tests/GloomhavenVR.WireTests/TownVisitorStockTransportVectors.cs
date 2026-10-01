using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownVisitorStockTransportVectors
{
    internal static void Run(Harness t)
    {
        t.Case("Private visitor, public cabinet and held stock fragments remain independent");
        foreach (bool compressed in new[] { false, true })
        {
            var frames = new[] { Frame(0), Frame(1), Frame(2) };
            var raw = new byte[3][]; var pages = new byte[3][][];
            for (int lane = 0; lane < 3; lane++)
            {
                raw[lane] = TownServiceCodec.Write(frames[lane]);
                pages[lane] = ExtrasFragments.Encode(raw[lane], raw[lane].Length,
                    (lane == 2 ? TownServiceFragments.StockLaneMarker : (ulong)(lane << 16)) | frames[lane].Module,
                    TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: compressed);
                t.True(pages[lane].Length > 1, "same module uses multiple real datagrams");
            }
            var receiver = new TownServiceFragments(); var delivered = new byte[3][];
            int maximum = Math.Max(pages[0].Length, Math.Max(pages[1].Length, pages[2].Length));
            for (int index = maximum - 1; index >= 0; index--)
                for (int lane = 2; lane >= 0; lane--)
                {
                    if (index >= pages[lane].Length) continue;
                    byte[]? packet = receiver.Accept(7, pages[lane][index], pages[lane][index].Length, .1);
                    if (packet == null) continue;
                    t.True(TownServiceCodec.TryRead(packet, packet.Length, out TownServiceFrame? decoded), "interleaved lane completes a valid original frame");
                    int identity = decoded!.VisitorStock ? 2 : decoded.PublicCatalog ? 1 : 0;
                    t.True(delivered[identity] == null, "each independent lane completes exactly once");
                    delivered[identity] = packet;
                }
            for (int lane = 0; lane < 3; lane++)
            {
                t.True(delivered[lane] != null, "same sender/session/module delivers all three presentation lanes");
                t.Wire(raw[lane], delivered[lane], raw[lane].Length, "interleaved/reordered fragments preserve complete original lane content");
                foreach (byte[] page in pages[lane])
                    t.True(receiver.Accept(7, page, page.Length, .2) == null, "completed lane cannot replay another lane");
            }
            foreach (int original in new[] { 0, 2 })
            {
                ulong wrong = original == 0 ? TownServiceFragments.StockLaneMarker : 0UL;
                byte[][] spoofed = ExtrasFragments.Encode(raw[original], raw[original].Length,
                    wrong | frames[original].Module, TownServiceCodec.MessageType,
                    TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: compressed);
                foreach (byte[] page in spoofed)
                    t.True(receiver.Accept(10 + original, page, page.Length, .21) == null,
                        "decoded stock flag must agree with its isolated fragment namespace");
            }
            receiver.Forget(7);
            int reopened = 0;
            foreach (byte[] page in pages[2])
                if (receiver.Accept(7, page, page.Length, .3) != null) reopened++;
            t.Equal(1, reopened, "peer forget clears the independently namespaced stock assembler");
            byte[][] invalid = ExtrasFragments.Encode(raw[2], raw[2].Length,
                (TownServiceFragments.StockLaneMarker | 65536UL) | frames[2].Module, TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
                TownServiceFrame.MaxBytes, compress: compressed);
            foreach (byte[] page in invalid)
                t.True(receiver.Accept(8, page, page.Length, .4) == null, "unknown fourth fragment lane is rejected");
        }
        var queue = new TownServiceSendQueue(0); var assembler = new TownServiceFragments();
        foreach (var frame in new[] { Frame(0), Frame(1), Frame(2) })
        { byte[] bytes = TownServiceCodec.Write(frame); queue.Enqueue(bytes, bytes.Length, frame); }
        var seen = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            byte[]? page = queue.Next(i * .05); if (page == null) continue;
            byte[]? packet = assembler.Accept(9, page, page.Length, i * .05); if (packet == null) continue;
            byte[][] messages = TownServiceCodec.TryReadBundle(packet, packet.Length, out byte[][]? bundled) ? bundled! : new[] { packet };
            foreach (byte[] message in messages)
                if (TownServiceCodec.TryRead(message, message.Length, out TownServiceFrame? frame))
                    seen.Add(frame!.VisitorStock ? 2 : frame.PublicCatalog ? 1 : 0);
        }
        t.Equal(3, seen.Count, "actual globally budgeted send queue delivers private/public/stock without coalescing");
        queue.Clear();
        var voices = new TownServiceSendQueue(0); var voiceReceiver = new TownServiceFragments();
        var stockVoice = VoiceFrame(true, 1, 1);
        var privateVoice = VoiceFrame(false, 3, 500);
        foreach (TownServiceFrame voice in new[] { stockVoice, privateVoice })
        { byte[] bytes = TownServiceCodec.Write(voice); voices.Enqueue(bytes, bytes.Length, voice); }
        var heard = new HashSet<bool>();
        for (int i = 0; i < 80; i++)
        {
            byte[]? page = voices.Next(i * .05); if (page == null) continue;
            byte[]? packet = voiceReceiver.Accept(12, page, page.Length, i * .05); if (packet == null) continue;
            t.True(TownServiceCodec.TryRead(packet, packet.Length, out TownServiceFrame? voice)
                && voice!.Module == TownServiceFrame.VoiceModule, "private and stock voices retain their exact cosmetic frame shape");
            heard.Add(voice!.VisitorStock);
        }
        t.Equal(2, heard.Count, "private mage session changes cannot clear an independently queued stock inspection voice");
        voices.Clear();
        foreach (ulong marker in new[] { 0UL, TownServiceFragments.StockLaneMarker })
        {
            var frame = Frame(marker == 0 ? 0 : 2);
            // One final monotonic snapshot fits; the following stride would enter
            // another namespace. A single failure must never become a migrated lane.
            ulong seed = TownServiceFragments.StockLaneMarker - 262144UL + frame.Module;
            var bounded = new ExtrasSendQueue(seed, TownServiceCodec.MessageType,
                TownServiceCodec.FragmentType, snapshotLimit: TownServiceFrame.MaxBytes,
                sequenceStride: 131072, counterMask: TownServiceFragments.StockLaneMarker - 1,
                fixedMarker: marker);
            byte[] bytes = TownServiceCodec.Write(frame);
            bounded.Enqueue(bytes, bytes.Length);
            var receiver = new TownServiceFragments(); bool arrived = false;
            for (int i = 0; i < 40; i++)
            {
                byte[]? page = bounded.Next(i * .05); if (page == null) continue;
                ulong fixedBit = (page[15] & 128) != 0 ? TownServiceFragments.StockLaneMarker : 0UL;
                t.Equal(marker, fixedBit, "near exhaustion retains the exact fixed fragment namespace");
                if (receiver.Accept(11, page, page.Length, i * .05) != null) arrived = true;
            }
            t.True(arrived, "last complete monotonic snapshot remains usable in its correct lane");
            bounded.Enqueue(bytes, bytes.Length); bool failed = false;
            try { bounded.Next(3); } catch (InvalidOperationException error)
            { failed = error.Message.Contains("namespace exhausted"); }
            t.True(failed, "counter exhaustion reports one controlled failure before namespace migration");
            bounded.Clear(); bounded.Enqueue(bytes, bytes.Length);
            t.True(bounded.Next(4) == null, "clear cannot alias a previous fragment generation after exhaustion");
            receiver.Forget(11);
            var fresh = new ExtrasSendQueue(frame.Module, TownServiceCodec.MessageType,
                TownServiceCodec.FragmentType, snapshotLimit: TownServiceFrame.MaxBytes,
                sequenceStride: 131072, counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: marker);
            fresh.Enqueue(bytes, bytes.Length); arrived = false;
            for (int i = 0; i < 40; i++)
            {
                byte[]? page = fresh.Next(4 + i * .05); if (page == null) continue;
                if (receiver.Accept(11, page, page.Length, 4 + i * .05) != null) arrived = true;
            }
            t.True(arrived, "fresh network queue and assembler recover without inheriting an exhausted lane");
        }
    }

    private static TownServiceFrame VoiceFrame(bool stock, byte service, uint session) => new()
    {
        VisitorStock = stock, Service = service, Session = session, Sequence = 1,
        Module = TownServiceFrame.VoiceModule, Template = 1, TemplateAddress = TownServiceFrame.VoiceAddress,
        Structure = 0x564F0000 | (stock ? 0u : 4u), Visible = true,
        Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f },
        Nodes = new[] { new TownServiceNode { Binding = 1 } }
    };

    private static TownServiceFrame Frame(int lane)
    {
        var node = new TownServiceNode { Binding = 1 };
        var random = new Random(271828 + lane);
        var text = new char[4000]; for (int i = 0; i < text.Length; i++) text[i] = (char)('a' + random.Next(26));
        node.Values.Add(TownServiceProperty.LegacyText, new TownServiceValue { Text = new[] { new string(text) } });
        return new TownServiceFrame { PublicCatalog = lane == 1, VisitorStock = lane == 2,
            Service = 1, Session = 1, Sequence = 1, Module = 41, Template = 1, Structure = 42,
            TemplateAddress = lane == 2 ? "merchant.heldstock|" : "item.41|", Visible = true,
            Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f }, Nodes = new[] { node } };
    }
}
