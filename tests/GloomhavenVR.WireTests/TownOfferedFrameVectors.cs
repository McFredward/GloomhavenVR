using System;
using System.IO;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownOfferedFrameVectors
{
    private static TownServiceMotionPacket Packet(TownServiceMotionEntry entry)
    { var packet = new TownServiceMotionPacket { Sequence = 1, SampleTime = 2f }; packet.Entries.Add(entry); return packet; }

    internal static void Run(Harness t)
    {
        t.Case("Additive109 preserves exact source native print/canvas affinity without altering record97");
        t.Equal(GloomhavenVR.Net.NetProtocol.ExtIdTownOfferedFrame, TownServiceMotionCodec.OfferedFrameRecordId,
            "public protocol109 constant and isolated numeric grammar agree");
        var relation = new TownServiceMotionEntry { Kind = 9, Lane = 0, Service = 3, Session = 7,
            Module = 3, Structure = 4, Binding = 5, Visible = true, HasCanvasFrame = true, OfferedLocalScale = true,
            OfferedModule = 6, OfferedStructure = 8, OfferedBinding = 9,
            Numbers = new[] {.125f,-.25f,.5f,0f,0f,0f,1f,.75f,1.25f,1f},
            CanvasPose = new[] {.1f,.2f,.3f,0f,0f,0f,1f,2f,3f,4f} };
        byte[] bytes = TownServiceMotionCodec.Write(Packet(relation));
        t.Wire(Hex.Bytes("31 52 56 47 03 1A 61 0D 00 01 00 00 00 00 00 00 00 00 00 00 40 6D 72 09 00 03 07 00 00 00 00 00 00 00 03 00 04 00 00 00 05 00 00 00 01 01 01 06 00 08 00 00 00 09 00 00 00 00 00 00 3E 00 00 80 BE 00 00 00 3F 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F 00 00 40 3F 00 00 A0 3F 00 00 80 3F CD CC CC 3D CD CC 4C 3E 9A 99 99 3E 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F 00 00 00 40 00 00 40 40 00 00 80 40"), bytes, bytes.Length,
            "independent Python struct vector binds exact109 source pose canvas and identity grammar");
        t.Equal(TownServiceMotionCodec.EntryBytes(relation), bytes.Length - 21, "exact relation budget includes every source float");
        t.True(TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var read), "exact source relation reads atomically");
        var got = read!.Entries[0];
        t.True(got.Kind == 9 && got.Binding == 5 && got.OfferedModule == 6 && got.OfferedStructure == 8
            && got.OfferedBinding == 9 && got.OfferedLocalScale && got.HasCanvasFrame, "native and physical identities retain affinity");
        for (int i = 0; i < 10; i++)
        { t.Equal(relation.Numbers[i], got.Numbers[i], "source fit retains exact pose bytes");
          t.Equal(relation.CanvasPose[i], got.CanvasPose[i], "source canvas retains exact pose bytes"); }
        foreach (int length in new[] {0, 21, 22, 23, 47, bytes.Length - 1})
            t.True(!TownServiceMotionCodec.TryRead(bytes, length, out _), "truncated109 cannot admit partial geometry");
        byte[] wrong = (byte[])bytes.Clone(); wrong[21] = 97;
        t.True(!TownServiceMotionCodec.TryRead(wrong, wrong.Length, out _), "109 cannot extend or reinterpret old97 grammar");
        var mixed = Packet(relation);
        mixed.Entries.Add(new TownServiceMotionEntry { Kind = 6, Service = 1, Session = 7, CueReady = true });
        wrong = TownServiceMotionCodec.Write(mixed); wrong[21] = 110;
        t.True(TownServiceMotionCodec.TryRead(wrong, wrong.Length, out var skipped) && skipped!.Entries.Count == 1
            && skipped.Entries[0].Kind == 6,
            "an unknown additive relation is skipped without changing the original envelope");
        var duplicate = Packet(relation); duplicate.Entries.Add(relation);
        bool rejected = false; try { TownServiceMotionCodec.Write(duplicate); } catch (InvalidDataException) { rejected = true; }
        t.True(rejected, "duplicate source relations cannot overwrite one another in a packet");
        foreach (Action<TownServiceMotionEntry> damage in new Action<TownServiceMotionEntry>[] {
            e => e.Numbers[0] = float.NaN, e => e.Numbers[6] = 0, e => e.Numbers[7] = 0,
            e => e.CanvasPose[8] = float.PositiveInfinity, e => e.CanvasPose[9] = .00000001f,
            e => e.Lane = 1, e => e.Service = 1, e => e.PublicClaim = 1, e => e.Binding = 0,
            e => e.OfferedModule = e.Module, e => e.OfferedStructure = 0, e => e.OfferedBinding = 0,
            e => e.HasCanvasFrame = false })
        {
            TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var baseline);
            damage(baseline!.Entries[0]); rejected = false;
            try { TownServiceMotionCodec.Write(baseline); } catch (InvalidDataException) { rejected = true; }
            t.True(rejected, "invalid scales poses canvas or cross-owner identities are rejected before serialization");
        }
        relation.Visible = false; relation.HasCanvasFrame = relation.OfferedLocalScale = false;
        relation.OfferedModule = 0; relation.OfferedStructure = relation.OfferedBinding = 0;
        bytes = TownServiceMotionCodec.Write(Packet(relation));
        t.True(TownServiceMotionCodec.TryRead(bytes, bytes.Length, out read) && !read!.Entries[0].Visible,
            "explicit withdrawal leaves no stale physical print affinity on a returned card");
        t.Equal(76, TownServiceMotionCodec.EntryBytes(relation), "withdrawal has bounded independent numeric cost");
    }
}
