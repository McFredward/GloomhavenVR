using System;
using System.Linq;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownOfferedHover666Vectors
{
    internal static void Run(Harness t)
    {
        TownServiceFrame frame = Frame(); byte[] legacy = TownServiceCodec.Write(frame);
        frame.OfferedHover = new TownOfferedHover(0x11223344, 1f, -.5f, .25f, false);
        byte[] packet = TownServiceCodec.Write(frame);
        byte[] suffix = {117,18,1,0x44,0x33,0x22,0x11,0,0,0,0x80,0x3f,0,0,0,0xbf,0,0,0x80,0x3e};
        t.Wire(suffix, packet.Skip(legacy.Length).ToArray(), 20, "literal117 native hover recipe has no sampled phase or height");
        t.Wire(legacy, packet.Take(legacy.Length).ToArray(), legacy.Length, "absent117 legacy snapshot prefix remains byte exact");
        t.True(TownServiceCodec.TryRead(packet, packet.Length, out var decoded) && decoded!.OfferedHover!.Equals(frame.OfferedHover), "117 retains canonical shared amplitude and epoch");
        var retained = TownServiceDelta.Retain(frame); var copied = TownServiceDelta.Copy(frame);
        t.True(ReferenceEquals(frame.OfferedHover, retained.OfferedHover) && ReferenceEquals(frame.OfferedHover, copied.OfferedHover), "immutable117 survives full retained and copied originals");
        frame.Sequence++; frame.OfferedHover = null;
        t.True(TownServiceDelta.Expand(retained, TownServiceDelta.Create(retained, frame)!)!.OfferedHover == null, "cumulative delta explicitly removes native hover state");
        for (int length = legacy.Length + 1; length < packet.Length; length++)
            t.True(!TownServiceCodec.TryRead(packet, length, out _), "truncated117 cannot partially enable native hover");
        foreach (int offset in new[] {1,2,7})
        { byte[] bad = (byte[])packet.Clone(); bad[legacy.Length + offset] = 255; t.True(!TownServiceCodec.TryRead(bad, bad.Length, out _), "invalid117 length/version/canvas flag rejected"); }
        t.True(!TownServiceCodec.TryRead(packet.Concat(suffix).ToArray(), packet.Length + suffix.Length, out _), "duplicate117 rejected atomically");
        foreach (string fault in new[] {"epoch", "zero", "nan", "infinite", "huge", "stock", "public", "claim", "service", "manifest", "parent", "canvas"})
        {
            TownServiceFrame bad = Frame(); bad.OfferedHover = new TownOfferedHover(1, 0, .006f, 0, false);
            switch (fault)
            {
                case "epoch": bad.OfferedHover = new TownOfferedHover(0, 0, .006f, 0, false); break;
                case "zero": bad.OfferedHover = new TownOfferedHover(1, 0, 0, 0, false); break;
                case "nan": bad.OfferedHover = new TownOfferedHover(1, float.NaN, 1, 0, false); break;
                case "infinite": bad.OfferedHover = new TownOfferedHover(1, 0, float.PositiveInfinity, 0, false); break;
                case "huge": bad.OfferedHover = new TownOfferedHover(1, 100001, 0, 0, false); break;
                case "stock": bad.VisitorStock = true; bad.Service = 1; break;
                case "public": bad.PublicCatalog = true; break;
                case "claim": bad.PublicClaim = 1; break;
                case "service": bad.Service = 2; break;
                case "manifest": bad.Module = TownServiceFrame.ManifestModule; break;
                case "parent": bad.ParentModule = 8; break;
                case "canvas": bad.OfferedHover = new TownOfferedHover(1, 0, .006f, 0, true); break;
            }
            bool rejected = false; try { TownServiceCodec.Write(bad); } catch (System.IO.InvalidDataException) { rejected = true; }
            t.True(rejected, "117 source/private/geometry boundary rejects " + fault);
        }
        frame = Frame(); frame.OfferedHover = new TownOfferedHover(1, 0, .006f, 0, false);
        var other = TownServiceDelta.Retain(frame); other.Module++;
        var pool = new TownServiceCodec.OriginalValuePoolBuilder();
        t.True(pool.TryAdd(TownServiceCodec.Write(frame)) && pool.TryAdd(TownServiceCodec.Write(other)), "actual pooled first picture accepts117 originals");
        byte[] bytes = pool.Write(); t.True(TownServiceCodec.TryReadBundle(bytes, bytes.Length, out var members), "pooled117 originals expand without a prior network basis");
        foreach (byte[] member in members!)
            t.True(TownServiceCodec.TryRead(member, member.Length, out decoded) && TownOfferedHover.Same(decoded!.OfferedHover, frame.OfferedHover), "pooled117 serialized exactly once and retains amplitude");
    }
    private static TownServiceFrame Frame() => new() { Service = 3, Session = 666, Sequence = 1, Module = 7,
        Template = 1, Structure = 2, TemplateAddress = "face.666|", Visible = true, SampleTime = 10,
        Pose = new[] {0f,0f,0f,0f,0f,0f,1f,1f,1f,1f}, Nodes = new[] {new TownServiceNode {Binding = 1}} };
}
internal static class OfferedHover666Program
{ private static int Main() { var t = new Harness(); TownOfferedHover666Vectors.Run(t); return t.Report(); } }
