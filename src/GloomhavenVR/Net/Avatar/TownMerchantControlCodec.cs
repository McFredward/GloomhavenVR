using System;
using System.IO;

namespace GloomhavenVR.Net.TownServices;

internal enum TownMerchantControlKind : byte { Request = 1, State = 2 }
internal enum TownMerchantControlOperation : byte
{ Category = 1, Page = 2, Snapshot = 3, CrankGrab = 4, CrankDrag = 5, CrankRelease = 6, CrankCancel = 7 }

/// <summary>Public cabinet input and its original cosmetic drawer clock. This never
/// authorizes a native purchase, changes character ownership or contains artwork.</summary>
internal readonly struct TownMerchantControlMessage
{
    internal readonly TownMerchantControlKind Kind;
    internal readonly TownMerchantControlOperation Operation;
    internal readonly sbyte Value;
    internal readonly uint Session, Sequence, RequestSequence, Epoch;
    internal readonly int Requester;
    internal readonly int CrankOwner;
    internal readonly float CrankLeadAngle;
    internal readonly TownRackState? Clock;
    internal TownMerchantControlMessage(TownMerchantControlKind kind, TownMerchantControlOperation operation,
        sbyte value, uint session, uint sequence, int requester, uint requestSequence, uint epoch, TownRackState? clock = null,
        int crankOwner = 0, float crankLeadAngle = 0f)
    { Kind = kind; Operation = operation; Value = value; Session = session; Sequence = sequence;
      Requester = requester; RequestSequence = requestSequence; Epoch = epoch; Clock = clock;
      CrankOwner = crankOwner; CrankLeadAngle = crankLeadAngle; }
}

internal static class TownMerchantControlCodec
{
    // Additive GVR1 v3 message27/record104. Historical native records78/85/102/103 remain unchanged.
    internal const byte MessageType = 27, RecordId = 104;
    internal const int Size = 62;
    internal static byte[] Write(in TownMerchantControlMessage message)
    {
        if (!Valid(in message)) throw new ArgumentOutOfRangeException(nameof(message));
        using var stream = new MemoryStream(Size);
        using var writer = new BinaryWriter(stream);
        writer.Write(NetProtocol.Magic); writer.Write(NetProtocol.Version); writer.Write(MessageType);
        writer.Write(RecordId); writer.Write((ushort)(Size - 9)); writer.Write((byte)1);
        writer.Write((byte)message.Kind); writer.Write((byte)message.Operation); writer.Write(message.Value);
        writer.Write(message.Session); writer.Write(message.Sequence); writer.Write(message.Requester);
        writer.Write(message.RequestSequence);
        TownRackState? clock = message.Clock;
        writer.Write(clock?.Turn ?? 0); writer.Write(clock?.Elapsed ?? 0f); writer.Write(clock?.LeadAngle ?? 0f);
        writer.Write(clock?.Page ?? 0); writer.Write(clock?.From ?? 0); writer.Write(clock?.To ?? 0);
        writer.Write(clock?.PageCount ?? 0); writer.Write(clock?.ScrollDirection ?? 0); writer.Write(message.Epoch);
        writer.Write(message.CrankOwner); writer.Write(message.CrankLeadAngle);
        return stream.ToArray();
    }
    internal static bool TryRead(byte[]? bytes, int length, out TownMerchantControlMessage message)
    {
        message = default;
        if (bytes == null || length != Size || bytes.Length < length) return false;
        using var stream = new MemoryStream(bytes, 0, length, false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != NetProtocol.Magic || reader.ReadByte() != NetProtocol.Version
            || reader.ReadByte() != MessageType || reader.ReadByte() != RecordId
            || reader.ReadUInt16() != Size - 9 || reader.ReadByte() != 1) return false;
        var kind = (TownMerchantControlKind)reader.ReadByte();
        var operation = (TownMerchantControlOperation)reader.ReadByte();
        sbyte value = reader.ReadSByte(); uint session = reader.ReadUInt32(), sequence = reader.ReadUInt32();
        int requester = reader.ReadInt32(); uint request = reader.ReadUInt32();
        var clock = new TownRackState { Cassette = true, Turn = reader.ReadUInt32(), Elapsed = reader.ReadSingle(),
            LeadAngle = reader.ReadSingle(), Page = reader.ReadUInt16(), From = reader.ReadUInt16(),
            To = reader.ReadUInt16(), PageCount = reader.ReadUInt16(), ScrollDirection = reader.ReadSByte() };
        uint epoch = reader.ReadUInt32();
        int crankOwner = reader.ReadInt32(); float crankLead = reader.ReadSingle();
        if (kind == TownMerchantControlKind.Request && (clock.Turn != 0 || clock.Elapsed != 0f
            || clock.LeadAngle != 0f || clock.Page != 0 || clock.From != 0 || clock.To != 0
            || clock.PageCount != 0 || clock.ScrollDirection != 0)) return false;
        message = new TownMerchantControlMessage(kind, operation, value, session, sequence, requester,
            request, epoch, kind == TownMerchantControlKind.State ? clock : null, crankOwner, crankLead);
        return Valid(in message);
    }
    internal static bool Newer(uint next, uint previous) => next != previous && unchecked(next - previous) < 0x80000000u;
    private static bool Valid(in TownMerchantControlMessage message)
    {
        if (message.Sequence == 0 || message.Requester < 0 || message.CrankOwner < 0
            || float.IsNaN(message.CrankLeadAngle) || float.IsInfinity(message.CrankLeadAngle)
            || message.CrankLeadAngle < 0f || message.CrankLeadAngle > 35.01f) return false;
        bool request = message.Kind == TownMerchantControlKind.Request;
        if (request)
            return message.Clock == null && message.Requester > 0 && message.RequestSequence == message.Sequence
                && message.Epoch == 0 && message.CrankOwner == 0
                && (message.Operation == TownMerchantControlOperation.Category
                    ? message.Value >= 0 && message.Value < 6 && message.CrankLeadAngle == 0f
                    : message.Operation == TownMerchantControlOperation.Page
                        ? (message.Value == -1 || message.Value == 1) && message.CrankLeadAngle == 0f
                        : message.Value == 0 && (message.Operation == TownMerchantControlOperation.CrankDrag
                            || message.Operation == TownMerchantControlOperation.CrankRelease
                            || message.CrankLeadAngle == 0f && (message.Operation == TownMerchantControlOperation.CrankGrab
                                || message.Operation == TownMerchantControlOperation.CrankCancel)));
        TownRackState? clock = message.Clock;
        return message.Kind == TownMerchantControlKind.State && message.Operation == TownMerchantControlOperation.Snapshot
            && message.Value == 0 && message.Session != 0 && message.Epoch != 0
            && (message.CrankOwner != 0 || message.CrankLeadAngle == 0f)
            && (message.Requester == 0 ? message.RequestSequence == 0 : message.RequestSequence != 0)
            && clock != null && clock.Cassette && clock.Page <= 1535 && clock.From <= 1535 && clock.To <= 1535
            && clock.PageCount >= 1 && clock.PageCount <= 256 && clock.ScrollDirection >= -1 && clock.ScrollDirection <= 1
            && (clock.ScrollDirection == 0 || clock.From / 256 == clock.To / 256)
            && !float.IsNaN(clock.Elapsed) && !float.IsInfinity(clock.Elapsed)
            && clock.Elapsed >= 0f && clock.Elapsed <= TownRackState.TurnDuration + .001f
            && !float.IsNaN(clock.LeadAngle) && !float.IsInfinity(clock.LeadAngle)
            && clock.LeadAngle >= 0f && clock.LeadAngle <= 35.01f;
    }
}
