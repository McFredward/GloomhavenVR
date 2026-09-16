using System;
using System.Collections.Generic;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class PresentationSaturationVectors
{
    internal static void Run(Harness t)
    {
        Saturated(t, 90);
        Saturated(t, 18);
    }
    private static void Saturated(Harness t, int framesPerSecond)
    {
        t.Case("presentation transport: all maximum-size streams at " + framesPerSecond + " Hz with announcements");
        var scheduler = new ExtrasSendScheduler(32, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        var payloads = new List<byte[]> { Packet(NetProtocol.MsgExtras, ExtrasFragments.MaxSnapshotBytes),
            Packet(NetProtocol.MsgUseBarAnimation, UseBarAnimationCodec.MaxSize),
            Packet(NetProtocol.MsgCardPlume, CardPlumeCodec.MaxSize), Packet(NetProtocol.MsgNativeBoard, NativeBoardCodec.MaxSize),
            Packet(NetProtocol.MsgCardAppearance, CardAppearanceCodec.MaxSize),
            Packet(NetProtocol.MsgNativeDecisionPrompt, NativeDecisionPromptCodec.MaxSize),
            Packet(NetProtocol.MsgItemAppearance, ItemAppearanceCodec.MaxSize) };
        var assemblers = new Dictionary<int, ExtrasFragments>();
        var oldAssemblers = new Dictionary<int, ExtrasFragments>();
        var completed = new HashSet<int>(); var oldCompleted = new HashSet<int>();
        int[] types = { NetProtocol.MsgExtras, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgCardPlume, NetProtocol.MsgNativeBoard, NetProtocol.MsgCardAppearance, NetProtocol.MsgNativeDecisionPrompt, NetProtocol.MsgItemAppearance };
        int[] envelopes = { NetProtocol.MsgExtrasFragments, NetProtocol.MsgUseBarAnimationFragments,
            NetProtocol.MsgCardPlumeFragments, NetProtocol.MsgNativeBoardFragments,
            NetProtocol.MsgCardAppearanceFragments, NetProtocol.MsgNativeDecisionPromptFragments, NetProtocol.MsgItemAppearanceFragments };
        for (int i = 0; i < types.Length; i++)
        {
            assemblers.Add(-envelopes[i], new ExtrasFragments((byte)types[i], (byte)envelopes[i], payloads[i].Length,
                i == 0 ? 5 : ExtrasFragments.PresentationAssemblyLifetime));
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
        double nextEnqueue = 0, nextAnnouncement = 0, previousEvent = -1;
        for (int frame = 0; frame < framesPerSecond * 32; frame++)
        {
            double now = frame / (double)framesPerSecond;
            if (now >= nextEnqueue)
            {
                for (int i = 0; i < types.Length; i++) scheduler.Enqueue(payloads[i], payloads[i].Length);
                for (int slot = 8; slot < 32; slot++) scheduler.Enqueue(payloads[slot - 8 + types.Length], payloads[slot - 8 + types.Length].Length, slot);
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
        t.Equal(31, completed.Count, "all 24 native slots and seven other streams complete under sustained saturation");
        t.True(oldCompleted.Count < completed.Count, "legacy five-second cosmetic assembly deadline loses valid queued frames");
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
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
}
