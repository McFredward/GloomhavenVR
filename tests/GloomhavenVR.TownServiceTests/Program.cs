using System;
using System.IO;
using System.IO.Compression;
using GloomhavenVR.Net.TownServices;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string text)
    { _checks++; if (!value) throw new Exception(text); }
    private static void Main()
    {
        TownVoiceScheduleCases.Run();
        TownGrantCases.Run();
        DonationCommitClock();
        CatalogAndStockLanes();
        TownServiceFrame original = Make(256);
        byte[] bytes = TownServiceCodec.Write(original);
        Check(Convert.ToHexString(bytes, 0, 6) == "315256470313", "independent canonical GVR1 little-endian header");
        Check(TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? read), "complete256-node module decodes");
        Check(read!.Nodes.Length == 256 && read.Nodes[255].Values[TownServiceProperty.TmpText].Text[0] == "Original item255: Äöü ß — shield", "Unicode original text survives");
        Check(read.Module == 62000, "stable ushort pool IDs are independent of simultaneous-module budget");
        for (int i = 0; i < bytes.Length; i++) Check(!TownServiceCodec.TryRead(bytes, i, out _), "every truncated prefix rejected");
        byte[] foreign = (byte[])bytes.Clone(); foreign[4] = 4; Check(!TownServiceCodec.TryRead(foreign, foreign.Length, out _), "foreign version rejected");
        byte[] extra = new byte[bytes.Length + 4]; Array.Copy(bytes, extra, bytes.Length); extra[^4] = 200; extra[^3] = 2;
        Check(TownServiceCodec.TryRead(extra, extra.Length, out _), "unknown additive record skipped");
        original.Nodes[0].Values[TownServiceProperty.Graphic].Numbers[0] = float.NaN;
        RejectWrite(original, "non-finite graphic output rejected");
        original = Make(256);
        TownServiceFrame animation = TownServiceDelta.Copy(original); animation.Sequence = 2;
        animation.Nodes[14].Values[TownServiceProperty.Graphic].Numbers[1] = .25f;
        TownServiceFrame delta = TownServiceDelta.Create(original, animation);
        byte[] moving = TownServiceCodec.Write(delta);
        Check(TownServiceCodec.TryRead(moving, moving.Length, out TownServiceFrame? received), "cumulative delta decodes");
        Check(TownServiceDelta.Expand(null, received!) == null, "missing baseline never applies guessed content");
        TownServiceFrame expanded = TownServiceDelta.Expand(original, received!)!;
        Check(expanded.Nodes[14].Values[TownServiceProperty.Graphic].Numbers[1] == .25f, "changed appearance applied");
        Check(expanded.Nodes[15].Values[TownServiceProperty.Graphic].Numbers[1] == .8f, "unchanged original appearance retained");
        Check(ReferenceEquals(expanded.Nodes[15], original.Nodes[15]), "unchanged immutable baseline node reused without dictionary allocation");
        Check(!ReferenceEquals(expanded.Nodes[14], original.Nodes[14]) && original.Nodes[14].Values[TownServiceProperty.Graphic].Numbers[1] == .8f,
            "changed node overlays a fresh dictionary without mutating original baseline");
        TownServiceFrame later = TownServiceDelta.Copy(animation); later.Sequence = 7;
        later.Nodes[15].Values[TownServiceProperty.Graphic].Numbers[1] = .6f;
        expanded = TownServiceDelta.Expand(original, TownServiceDelta.Create(original, later))!;
        Check(expanded.Nodes[14].Values[TownServiceProperty.Graphic].Numbers[1] == .25f
            && expanded.Nodes[15].Values[TownServiceProperty.Graphic].Numbers[1] == .6f, "loss of every intermediate delta still converges");
        TownServiceFrame wrong = TownServiceDelta.Copy(original); wrong.Session++;
        Check(TownServiceDelta.Expand(wrong, delta) == null, "old-session delta cannot change new-session widgets");
        wrong = TownServiceDelta.Copy(original); wrong.Nodes[14].Binding = 90000;
        Check(TownServiceDelta.Expand(wrong, delta) == null, "different native topology rejected");
        var manifest = new TownServiceFrame { Service = 3, Session = 22, Sequence = 9, Module = TownServiceFrame.ManifestModule,
            Visible = true, Modules = new ushort[] { 1, 65000 }, Pose = Pose() };
        byte[] manifestBytes = TownServiceCodec.Write(manifest);
        Check(TownServiceCodec.TryRead(manifestBytes, manifestBytes.Length, out _), "bounded manifest permits stable high module IDs");
        manifest.Modules = new ushort[] { 2, 1 }; RejectWrite(manifest, "unsorted manifest rejected");
        manifest.Modules = new ushort[] { 1, 1 }; RejectWrite(manifest, "duplicate manifest module rejected");
        manifest.Modules = Array.Empty<ushort>(); manifest.Visible = false;
        Check(TownServiceCodec.TryRead(TownServiceCodec.Write(manifest), TownServiceCodec.Write(manifest).Length, out _), "close manifest self-contained");
        var random = new Random(537);
        for (int i = 0; i < 10000; i++)
        {
            byte[] fuzz = new byte[random.Next(0, 1000)]; random.NextBytes(fuzz);
            Check(!TownServiceCodec.TryRead(fuzz, fuzz.Length, out _), "random foreign input inert");
        }
        TownServiceFrame row = Make(16); byte[] rowBytes = TownServiceCodec.Write(row);
        Console.WriteLine($"Town service codec: {_checks} assertions. Synthetic256-node full={bytes.Length}B/deflate{Compressed(bytes)}B; one-property animation delta={moving.Length}B/deflate{Compressed(moving)}B;16-node row={rowBytes.Length}B/deflate{Compressed(rowBytes)}B.");
    }
    private static int Compressed(byte[] bytes)
    { using var output = new MemoryStream(); using (var zip = new DeflateStream(output, CompressionLevel.Fastest, true)) zip.Write(bytes); return (int)output.Length; }
    private static void RejectWrite(TownServiceFrame frame, string message)
    { try { TownServiceCodec.Write(frame); } catch (InvalidDataException) { Check(true, message); return; } Check(false, message); }
    private static float[] Pose() => new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f };
    private static void DonationCommitClock()
    {
        var frame = new TownServiceFrame { Service = 2, Session = 7, Sequence = 9,
            Module = TownServiceFrame.ManifestModule, Visible = true, Pose = Pose(),
            TempleDonationKnown = true, TempleDonationRevision = 1,
            HasTempleDonationCommitAge = true, TempleDonationCommitAge = .5f };
        byte[] bytes = TownServiceCodec.Write(frame);
        Check(Convert.ToHexString(bytes, bytes.Length - 7, 7) == "5D05010000003F",
            "donation clock has an independent additive little-endian vector");
        Check(TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? read)
            && read!.HasTempleDonationCommitAge && read.TempleDonationCommitAge == .5f,
            "late observers retain the owner's committed blessing age");
        Check(TownServiceDelta.Copy(frame).TempleDonationCommitAge == .5f,
            "snapshot copying retains explicit donation timing");
        byte[] malformed = new byte[bytes.Length + 7]; Array.Copy(bytes, malformed, bytes.Length);
        Array.Copy(bytes, bytes.Length - 7, malformed, bytes.Length, 7);
        Check(!TownServiceCodec.TryRead(malformed, malformed.Length, out _), "duplicate donation clocks are rejected");
        foreach (float invalid in new[] { -1f, 31f, float.NaN, float.PositiveInfinity })
        { frame.TempleDonationCommitAge = invalid; RejectWrite(frame, "invalid source donation age rejected"); }
        frame.TempleDonationCommitAge = .5f; frame.TempleDonationKnown = false;
        RejectWrite(frame, "clock alone cannot invent a donation");
        frame.TempleDonationKnown = true; frame.TempleDonationCommitAge = 0f; frame.HasTempleDonationCommitAge = false;
        bytes = TownServiceCodec.Write(frame);
        Check(TownServiceCodec.TryRead(bytes, bytes.Length, out read) && !read!.HasTempleDonationCommitAge,
            "legacy manifest remains a valid age-unknown baseline");
    }
    private static void CatalogAndStockLanes()
    {
        TownServiceFrame frame = Make(1);
        frame.PublicCatalog = true; frame.TemplateAddress = "merchant.rack|original";
        frame.Rack = new TownRackState { Cassette = true, Crank = 2,
            Layout = new[] { new TownCatalogSlot(7, 12), new TownCatalogSlot(19, 3073) } };
        byte[] bytes = TownServiceCodec.Write(frame);
        Check(Convert.ToHexString(bytes, bytes.Length - 17, 17) == "5E0F010200070000000C0013000000010C",
            "complete placement has an independent little-endian additive vector");
        Check(TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? read)
            && TownCatalogLayout.Same(frame.Rack.Layout, read!.Rack!.Layout),
            "cold-page cabinet placement survives transport exactly");
        Check(TownCatalogLayout.Same(frame.Rack.Layout, TownServiceDelta.Copy(frame).Rack!.Layout),
            "copied cabinet clocks preserve exact future slot layout");
        frame.Rack.Layout = new TownCatalogSlot[TownCatalogLayout.MaxLayout];
        for (int i = 0; i < frame.Rack.Layout.Length; i++) frame.Rack.Layout[i] = new TownCatalogSlot(i + 1, (ushort)i);
        bytes = TownServiceCodec.Write(frame);
        Check(TownServiceCodec.TryRead(bytes, bytes.Length, out read)
            && read!.Rack!.Layout!.Length == TownCatalogLayout.MaxLayout,
            "late-game placement reassembles across multiple TLV records");
        frame.Rack.Layout = Array.Empty<TownCatalogSlot>(); bytes = TownServiceCodec.Write(frame);
        Check(TownServiceCodec.TryRead(bytes, bytes.Length, out read) && read!.Rack!.Layout!.Length == 0,
            "known empty placement remains distinct from an unknown legacy layout");
        frame.PublicCatalog = false; RejectWrite(frame, "private cabinet cannot inject shared placements");
        frame = Make(1); frame.VisitorStock = true;
        bytes = TownServiceCodec.Write(frame);
        Check(Convert.ToHexString(bytes, bytes.Length - 3, 3) == "5F0101",
            "visitor-held stock has an independent additive lane vector");
        Check(TownServiceCodec.TryRead(bytes, bytes.Length, out read) && read!.VisitorStock,
            "third stock lane remains separate from private interaction and public cabinet");
        TownServiceFrame later = TownServiceDelta.Copy(frame); later.Sequence = 2;
        Check(TownServiceDelta.Expand(frame, TownServiceDelta.Create(frame, later))!.VisitorStock,
            "held-stock cumulative deltas retain their independent lane");
        later.VisitorStock = false;
        Check(!TownServiceDelta.Compatible(frame, later), "one lane's baseline cannot expand another lane's delta");
        var duplicate = new byte[bytes.Length + 3]; Array.Copy(bytes, duplicate, bytes.Length);
        Array.Copy(bytes, bytes.Length - 3, duplicate, bytes.Length, 3);
        Check(!TownServiceCodec.TryRead(duplicate, duplicate.Length, out _), "duplicate visitor-stock markers rejected");
        frame.PublicCatalog = true; RejectWrite(frame, "stock presentation cannot impersonate public authorship");
        frame.PublicCatalog = false; frame.TransactionActive = true;
        RejectWrite(frame, "stock presentation cannot claim an NPC transaction");
        frame.TransactionActive = false; frame.Service = 2;
        RejectWrite(frame, "merchant stock cannot enter temple gameplay or presentation scope");
    }
    private static TownServiceFrame Make(int count)
    {
        var frame = new TownServiceFrame { Service = 1, Session = 4, Sequence = 1, Module = 62000, Template = 3,
            Structure = 42, Visible = true, Pose = Pose(), Nodes = new TownServiceNode[count] };
        for (int i = 0; i < count; i++)
        {
            var node = new TownServiceNode { Binding = (uint)(i + 1) };
            node.Values.Add(TownServiceProperty.Transform, new TownServiceValue { Numbers = new[] { i * 10f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 1f, 1f, .5f, .5f, 500f, 100f } });
            node.Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { 1f } });
            node.Values.Add(TownServiceProperty.Graphic, new TownServiceValue { Numbers = new[] { 1f, .8f, .7f, .6f, 1f } });
            if (i % 4 == 3) node.Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Numbers = new float[40], Text = new[] { "Original item" + i + ": Äöü ß — shield", "originalFont" } });
            var material = new TownServiceValue { Numbers = new float[154], Text = new string[62] };
            material.Text[0] = "shader|native"; material.Text[1] = "GLOW";
            for (int p = 0; p < 30; p++) { material.Text[2 + p * 2] = "_Property" + p; material.Text[3 + p * 2] = string.Empty; }
            node.Values.Add(TownServiceProperty.TextMaterial, material);
            frame.Nodes[i] = node;
        }
        return frame;
    }
}
