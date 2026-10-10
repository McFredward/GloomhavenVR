using System;
using System.IO;

namespace GloomhavenVR.Net.TownServices;

// Immutable source provenance, shared safely by retained frames and cumulative deltas.
// The owning network peer is supplied by the packet transport, never by this record.
internal sealed class TownCardReturnOrigin : IEquatable<TownCardReturnOrigin>
{
    internal readonly byte Service;
    internal readonly uint Session, Structure, PublicClaim, PreparationRevision;
    internal readonly ushort Module;
    internal TownCardReturnOrigin(byte service, uint session, ushort module, uint structure,
        uint publicClaim, uint preparationRevision)
    { Service = service; Session = session; Module = module; Structure = structure;
      PublicClaim = publicClaim; PreparationRevision = preparationRevision; }
    internal uint FlightRevision => PreparationRevision == uint.MaxValue ? 1 : PreparationRevision + 1;
    internal bool Matches(TownServiceFrame frame) => !frame.VisitorStock && !frame.PublicCatalog
        && frame.Service == Service && frame.Session == Session && frame.Module == Module
        && frame.Structure == Structure && frame.PublicClaim == PublicClaim;
    public bool Equals(TownCardReturnOrigin? other) => other != null && Service == other.Service
        && Session == other.Session && Module == other.Module && Structure == other.Structure
        && PublicClaim == other.PublicClaim && PreparationRevision == other.PreparationRevision;
    public override bool Equals(object? other) => Equals(other as TownCardReturnOrigin);
    public override int GetHashCode() => unchecked((((int)Session * 397 ^ Module) * 397
        ^ (int)Structure) * 397 ^ (int)PreparationRevision);
    internal static bool Same(TownCardReturnOrigin? a, TownCardReturnOrigin? b) =>
        ReferenceEquals(a, b) || a != null && a.Equals(b);
}

internal static partial class TownServiceCodec
{
    internal const byte ReturnOriginRecordId = 116;
    private static void WriteReturnOrigin(byte[] packet, ref int at, TownCardReturnOrigin origin)
    {
        packet[at++] = ReturnOriginRecordId; packet[at++] = 20;
        packet[at++] = 1; packet[at++] = origin.Service;
        WriteOriginUInt(packet, ref at, origin.Session);
        packet[at++] = (byte)origin.Module; packet[at++] = (byte)(origin.Module >> 8);
        WriteOriginUInt(packet, ref at, origin.Structure);
        WriteOriginUInt(packet, ref at, origin.PublicClaim);
        WriteOriginUInt(packet, ref at, origin.PreparationRevision);
    }
    private static void WriteOriginUInt(byte[] packet, ref int at, uint value)
    { for (int i = 0; i < 4; i++) packet[at++] = (byte)(value >> (8 * i)); }
    private static uint ReadOriginUInt(byte[] packet, ref int at)
    { uint value = 0; for (int i = 0; i < 4; i++) value |= (uint)packet[at++] << (8 * i); return value; }
    private static bool TryReadReturnOrigin(byte[] packet, int at, int length, out TownCardReturnOrigin? origin)
    {
        origin = null; if (length != 20 || packet[at++] != 1) return false;
        byte service = packet[at++]; uint session = ReadOriginUInt(packet, ref at);
        ushort module = (ushort)(packet[at++] | packet[at++] << 8);
        uint structure = ReadOriginUInt(packet, ref at), claim = ReadOriginUInt(packet, ref at);
        uint preparation = ReadOriginUInt(packet, ref at);
        origin = new TownCardReturnOrigin(service, session, module, structure, claim, preparation);
        return true;
    }
    private static void ValidateReturnOrigin(TownServiceFrame frame)
    {
        TownCardReturnOrigin? origin = frame.ReturnOrigin; if (origin == null) return;
        bool ability = frame.TemplateAddress.StartsWith("face.", StringComparison.Ordinal)
            || frame.TemplateAddress.StartsWith("map.cardbody|", StringComparison.Ordinal)
            || frame.TemplateAddress.StartsWith("map.cardbody.", StringComparison.Ordinal);
        if (!ability || frame.Module >= TownServiceFrame.UrgentBundleStream || frame.PublicCatalog
            || origin.Service != 3 || origin.Session == 0 || origin.Module >= TownServiceFrame.UrgentBundleStream
            || origin.Structure != frame.Structure || origin.PublicClaim != 0
            || (frame.VisitorStock ? frame.Service != 1 : !origin.Matches(frame)))
            throw new InvalidDataException("Invalid offered-card return origin.");
    }
}
