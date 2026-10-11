using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class PresentationSaturationVectors
{
    internal static void Run(Harness t)
    {
        BoardDeadlines(t);
        Saturated(t, 90, false);
        Saturated(t, 18, false);
        Saturated(t, 90, true);
        Saturated(t, 18, true);
    }
    private static void Saturated(Harness t, int framesPerSecond, bool activeTown)
    {
        t.Case("presentation transport: all maximum-size streams at " + framesPerSecond + " Hz with announcements, town=" + activeTown);
        var scheduler = new ExtrasSendScheduler(32, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        // Town proof uses a representative 1 KiB presence packet; every cosmetic stream retains
        // its maximum. The original no-town case continues to fill all 32 maxima concurrently.
        var payloads = new List<byte[]> { Packet(NetProtocol.MsgExtras, activeTown ? 1024 : ExtrasFragments.MaxSnapshotBytes),
            Packet(NetProtocol.MsgUseBarAnimation, UseBarAnimationCodec.MaxSize),
            Packet(NetProtocol.MsgCardPlume, CardPlumeCodec.MaxSize), Packet(NetProtocol.MsgNativeBoard, NativeBoardCodec.MaxSize),
            Packet(NetProtocol.MsgCardAppearance, CardAppearanceCodec.MaxSize),
            Packet(NetProtocol.MsgNativeDecisionPrompt, NativeDecisionPromptCodec.MaxSize),
            Packet(NetProtocol.MsgItemAppearance, ItemAppearanceCodec.MaxSize),
            Packet(NetProtocol.MsgMapButtonTooltip, MapButtonTooltipCodec.MaxSize) };
        var assemblers = new Dictionary<int, ExtrasFragments>();
        var oldAssemblers = new Dictionary<int, ExtrasFragments>();
        var completed = new HashSet<int>(); var oldCompleted = new HashSet<int>();
        int[] types = { NetProtocol.MsgExtras, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgCardPlume, NetProtocol.MsgNativeBoard, NetProtocol.MsgCardAppearance, NetProtocol.MsgNativeDecisionPrompt, NetProtocol.MsgItemAppearance, NetProtocol.MsgMapButtonTooltip };
        int[] envelopes = { NetProtocol.MsgExtrasFragments, NetProtocol.MsgUseBarAnimationFragments,
            NetProtocol.MsgCardPlumeFragments, NetProtocol.MsgNativeBoardFragments,
            NetProtocol.MsgCardAppearanceFragments, NetProtocol.MsgNativeDecisionPromptFragments, NetProtocol.MsgItemAppearanceFragments, NetProtocol.MsgMapButtonTooltipFragments };
        for (int i = 0; i < types.Length; i++)
        {
            assemblers.Add(-envelopes[i], new ExtrasFragments((byte)types[i], (byte)envelopes[i], payloads[i].Length,
                i == 0 ? 5 : ExtrasFragments.PresentationAssemblyLifetime,
                lifetimeSizeBase: i == 3 ? NativeBoardCodec.LegacyMaxSize : 0));
            oldAssemblers.Add(-envelopes[i], new ExtrasFragments((byte)types[i], (byte)envelopes[i], payloads[i].Length));
        }
        for (int slot = 8; slot < 32; slot++)
        {
            payloads.Add(Packet(NetProtocol.MsgNativeUseBar, NativeUseBarPacket.MaxSize));
            assemblers.Add(slot, new ExtrasFragments(NetProtocol.MsgNativeUseBar, NetProtocol.MsgNativeUseBarFragments,
                NativeUseBarPacket.MaxSize, ExtrasFragments.PresentationAssemblyLifetime));
            oldAssemblers.Add(slot, new ExtrasFragments(NetProtocol.MsgNativeUseBar, NetProtocol.MsgNativeUseBarFragments,
                NativeUseBarPacket.MaxSize));
        }
        byte[] announcement = ExtrasVersionAnnouncement.Write(NetProtocol.ModBuild, "0.0.0");
        // As with every saturated payload here, town is an opaque transport upper bound.
        // A real lane identity keeps all three existing town turns populated without altering
        // the scheduler or any other stream's original deadline.
        byte[] town = Packet(TownServiceCodec.MessageType, TownServiceFrame.MaxBytes);
        var townIdentity = new TownServiceFrame { Service = 1, Session = 1, Module = 4, Sequence = 1, HighPriority = true };
        var townReceiver = new ExtrasFragments(TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
            TownServiceFrame.MaxBytes, TownServiceFragments.AssemblyLifetime);
        bool townComplete = false;
        int oldAt32 = -1;
        double horizon = Math.Ceiling(ExtrasFragments.PresentationAssemblyLifetime * NativeBoardCodec.MaxSize / NativeBoardCodec.LegacyMaxSize) + 1;
        double nextEnqueue = 0, nextAnnouncement = 0, previousEvent = -1;
        for (int frame = 0; frame < framesPerSecond * horizon; frame++)
        {
            double now = frame / (double)framesPerSecond;
            if (frame == framesPerSecond * 32)
                oldAt32 = completed.Count - (completed.Contains(-NetProtocol.MsgNativeBoardFragments) ? 1 : 0);
            if (now >= nextEnqueue)
            {
                for (int i = 0; i < types.Length; i++) scheduler.Enqueue(payloads[i], payloads[i].Length);
                for (int slot = 8; slot < 32; slot++) scheduler.Enqueue(payloads[slot - 8 + types.Length], payloads[slot - 8 + types.Length].Length, slot);
                if (activeTown) scheduler.Enqueue(town, town.Length, identity: townIdentity);
                nextEnqueue = now + .1;
            }
            byte[]? packet = scheduler.NextBatch(now, now >= nextAnnouncement ? announcement : null);
            if (packet == null) continue;
            t.True(previousEvent < 0 || now - previousEvent >= .05 - .000001, "one bounded event per cadence, no catch-up burst");
            previousEvent = now;
            t.True(packet.Length <= ExtrasFragments.MaxDatagramBytes, "combined event remains within Bolt payload budget");
            if (ReferenceEquals(packet, announcement)) { nextAnnouncement = now + 1; continue; }
            byte[][] pages = NetPacket.PeekType(packet, packet.Length) == NetProtocol.MsgPresentationBatch
                && PresentationBatch.TryRead(packet, packet.Length, out byte[][]? batch) ? batch! : new[] { packet };
            foreach (byte[] page in pages)
            {
                int type = NetPacket.PeekType(page, page.Length);
                if (type == TownServiceCodec.FragmentType)
                {
                    if (townReceiver.Accept(123, page, page.Length, now) is byte[] fullTown)
                    { townComplete = true; t.True(Equal(town, fullTown), "occupied town turns retain atomic unchanged bytes"); }
                    continue;
                }
                int key = type == NetProtocol.MsgNativeUseBarFragments ? ExtrasFragments.NativeSlotStream(page, page.Length) : -type;
                if (assemblers[key].Accept(123, page, page.Length, now) is byte[] full)
                {
                    completed.Add(key);
                    byte[] original = key >= 8 ? payloads[key - 8 + types.Length] : payloads[Array.IndexOf(envelopes, -key)];
                    t.True(Equal(full, original), "saturation never splices or truncates an atomic snapshot");
                }
                if (oldAssemblers[key].Accept(123, page, page.Length, now) != null) oldCompleted.Add(key);
            }
        }
        t.Equal(31, oldAt32, "older presentation streams retain their original32-second completion bound");
        t.Equal(32, completed.Count, "large native board completes under unchanged sustained stream fairness");
        t.True(completed.Contains(-NetProtocol.MsgNativeBoardFragments), "large board survives the new maximum frame duration");
        if (activeTown)
        {
            t.True(townComplete, "all three occupied town turns still make progress");
        }
        t.True(oldCompleted.Count < completed.Count, "legacy five-second cosmetic assembly deadline loses valid queued frames");
    }
    private static ExtrasFragments BoardReceiver(bool scaled = true) => new(NetProtocol.MsgNativeBoard,
        NetProtocol.MsgNativeBoardFragments, NativeBoardCodec.MaxSize, ExtrasFragments.PresentationAssemblyLifetime,
        lifetimeSizeBase: scaled ? NativeBoardCodec.LegacyMaxSize : 0);
    private static byte[][] BoardPages(byte[] bytes, ulong sequence = 33, bool compress = false) => ExtrasFragments.Encode(bytes,
        bytes.Length, sequence, NetProtocol.MsgNativeBoard, NetProtocol.MsgNativeBoardFragments, NativeBoardCodec.MaxSize, compress);
    private static byte[]? FinishAt(ExtrasFragments receiver, byte[][] pages, double finish)
    {
        receiver.Accept(123, pages[0], pages[0].Length, 0);
        byte[]? completed = null;
        for (int i = 1; i < pages.Length; i++) completed = receiver.Accept(123, pages[i], pages[i].Length, finish) ?? completed;
        return completed;
    }
    private static void BoardDeadlines(Harness t)
    {
        t.Case("only larger encoded native boards extend their original first-fragment deadline");
        byte[] legacy = Packet(NetProtocol.MsgNativeBoard, NativeBoardCodec.LegacyMaxSize);
        byte[] large = Packet(NetProtocol.MsgNativeBoard, NativeBoardCodec.MaxSize);
        byte[][] smallPages = BoardPages(legacy), largePages = BoardPages(large);
        t.True(Equal(legacy, FinishAt(BoardReceiver(), smallPages, 32)!), "legacy board accepts the original32s boundary");
        t.True(FinishAt(BoardReceiver(), smallPages, 32.001) == null, "legacy board still expires immediately beyond32s");
        t.True(FinishAt(BoardReceiver(false), largePages, 43) == null, "negative control: original32s assembler discards valid large queued board");
        t.True(Equal(large, FinishAt(BoardReceiver(), largePages, 43)!), "larger board accepts unchanged atomic pages after43s");
        double maximum = 32 * large.Length / (double)NativeBoardCodec.LegacyMaxSize;
        t.True(maximum < 52 && Equal(large, FinishAt(BoardReceiver(), largePages, maximum)!), "U16 maximum retains finite size-derived51.2s boundary");
        t.True(FinishAt(BoardReceiver(), largePages, maximum + .001) == null, "oversized-duration board expires beyond its exact size budget");
        var active = BoardReceiver(); active.Accept(123, largePages[0], largePages[0].Length, 0);
        active.Accept(123, largePages[1], largePages[1].Length, 30);
        byte[]? activity = null;
        for (int i = 2; i < largePages.Length; i++) activity = active.Accept(123, largePages[i], largePages[i].Length, maximum + .001) ?? activity;
        t.True(activity == null, "later fragments never reset or extend the original start time");
        foreach (bool forget in new[] { false, true })
        {
            if (forget) active.Forget(123); else active.Clear();
            byte[]? recovered = FinishAt(active, largePages, 40);
            t.True(recovered != null && Equal(large, recovered), "clear/forget removes expired assembly lifetime state");
        }
        byte[] packedRaw = Packet(NetProtocol.MsgNativeBoard, NativeBoardCodec.MaxSize);
        Array.Clear(packedRaw, 30000, packedRaw.Length - 30000);
        byte[][] packed = BoardPages(packedRaw, compress: true);
        t.True(packed.Length > 1 && NetPacket.PeekType(packed[0], packed[0].Length) == NetProtocol.MsgPresentationCompression,
            "compressed control uses actual packed transport grammar");
        t.True(Equal(packedRaw, FinishAt(BoardReceiver(), packed, 32)!), "compressed large-raw board remains valid and byte exact at32s");
        t.True(FinishAt(BoardReceiver(), packed, 32.001) == null, "small packed board retains32s even when raw output is65535 bytes");
        var presence = new ExtrasFragments(); byte[] rawPresence = Packet(NetProtocol.MsgExtras, ExtrasFragments.MaxSnapshotBytes);
        t.True(Equal(rawPresence, FinishAt(presence, ExtrasFragments.Encode(rawPresence, rawPresence.Length, 33), 5)!),
            "presence accepts its original exact five-second boundary");
        presence = new ExtrasFragments();
        t.True(FinishAt(presence, ExtrasFragments.Encode(rawPresence, rawPresence.Length, 33), 5.001) == null,
            "presence retains its original five-second deadline");
        var other = new ExtrasFragments(NetProtocol.MsgCardAppearance, NetProtocol.MsgCardAppearanceFragments,
            CardAppearanceCodec.MaxSize, ExtrasFragments.PresentationAssemblyLifetime);
        byte[] rawOther = Packet(NetProtocol.MsgCardAppearance, CardAppearanceCodec.MaxSize);
        t.True(FinishAt(other, ExtrasFragments.Encode(rawOther, rawOther.Length, 33,
            NetProtocol.MsgCardAppearance, NetProtocol.MsgCardAppearanceFragments, rawOther.Length), 32.001) == null,
            "other large presentation streams retain32s without opt-in");
        foreach (int invalid in new[] { -1, NativeBoardCodec.MaxSize + 1 })
        { bool rejected = false; try { _ = new ExtrasFragments(snapshotLimit: NativeBoardCodec.MaxSize, lifetimeSizeBase: invalid); }
          catch (ArgumentOutOfRangeException) { rejected = true; } t.True(rejected, "invalid lifetime size base is rejected"); }
    }
    private static byte[] Packet(byte type, int length)
    {
        // Transport treats the bounded inner payload as opaque. Fill the entire allowed envelope
        // to prove a stronger upper bound than any current compressed native descriptor.
        var result = new byte[length]; new Random(type).NextBytes(result); int at = 0;
        AvatarSerializer.WriteU32(result, ref at, NetProtocol.Magic);
        result[at++] = NetProtocol.Version; result[at] = type; return result;
    }
    private static bool Equal(byte[] a, byte[] b)
    { if (a == null || b == null || a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
}
