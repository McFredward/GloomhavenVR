using System;
using System.IO;

namespace GloomhavenVR.Net.TownServices;

// Immutable owner-measured amplitude in the shared map frame. The source
// strips only Place's intrinsic displacement; its physical base/facing and
// finite settle remain ordinary authored geometry. No phase is transmitted.
internal sealed class TownOfferedHover : IEquatable<TownOfferedHover>
{
    internal readonly uint Epoch;
    internal readonly float X, Y, Z;
    internal readonly bool CanvasFollowsHover;
    internal TownOfferedHover(uint epoch, float x, float y, float z, bool canvasFollowsHover)
    { Epoch = epoch; X = x; Y = y; Z = z; CanvasFollowsHover = canvasFollowsHover; }
    public bool Equals(TownOfferedHover? other) => other != null && Epoch == other.Epoch
        && X == other.X && Y == other.Y && Z == other.Z && CanvasFollowsHover == other.CanvasFollowsHover;
    public override bool Equals(object? other) => Equals(other as TownOfferedHover);
    public override int GetHashCode() => unchecked((int)Epoch * 397 ^ X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode());
    internal static bool Same(TownOfferedHover? a, TownOfferedHover? b) => ReferenceEquals(a, b) || a != null && a.Equals(b);
}

internal static partial class TownServiceCodec
{
    internal const byte OfferedHoverRecordId = NetProtocol.ExtIdTownOfferedHover;
    private static void WriteOfferedHover(byte[] packet, ref int at, TownOfferedHover hover)
    {
        packet[at++] = OfferedHoverRecordId; packet[at++] = 18; packet[at++] = 1;
        WriteOriginUInt(packet, ref at, hover.Epoch); packet[at++] = hover.CanvasFollowsHover ? (byte)1 : (byte)0;
        foreach (float value in new[] { hover.X, hover.Y, hover.Z })
        { byte[] bytes = BitConverter.GetBytes(value); Buffer.BlockCopy(bytes, 0, packet, at, 4); at += 4; }
    }
    private static bool TryReadOfferedHover(byte[] packet, int at, int length, out TownOfferedHover? hover)
    {
        hover = null; if (length != 18 || packet[at++] != 1) return false;
        uint epoch = ReadOriginUInt(packet, ref at); byte canvas = packet[at++]; if (canvas > 1) return false;
        float x = BitConverter.ToSingle(packet, at), y = BitConverter.ToSingle(packet, at + 4), z = BitConverter.ToSingle(packet, at + 8);
        hover = new TownOfferedHover(epoch, x, y, z, canvas != 0); return true;
    }
    private static void ValidateOfferedHover(TownServiceFrame frame)
    {
        TownOfferedHover? hover = frame.OfferedHover; if (hover == null) return;
        Finite(hover.X); Finite(hover.Y); Finite(hover.Z);
        float magnitude = hover.X * hover.X + hover.Y * hover.Y + hover.Z * hover.Z;
        if (hover.Epoch == 0 || frame.PublicCatalog || frame.VisitorStock || frame.PublicClaim != 0
            || frame.Service is not (1 or 3) || frame.Module >= TownServiceFrame.UrgentBundleStream
            || frame.ParentModule != TownServiceFrame.ManifestModule || frame.Structure == 0
            || hover.CanvasFollowsHover && !frame.HasCanvasFrame
            || float.IsNaN(magnitude) || float.IsInfinity(magnitude) || magnitude <= 0f || magnitude > 10000000000f)
            throw new InvalidDataException("Invalid native offered hover recipe.");
    }
}
