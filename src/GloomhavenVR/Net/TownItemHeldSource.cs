namespace GloomhavenVR.Net;

/// <summary>Public map item provenance, sampled atomically with one held pose. Owned
/// items use the native character list; stock uses its public catalog item identity.
/// No local instance id or viewer-selected character is an address.</summary>
internal readonly struct TownItemHeldSource
{
    internal const byte Owned = 1, Stock = 2;
    internal const int PayloadSize = 13;
    internal readonly byte Kind;
    internal readonly uint CharacterKey;
    internal readonly int ItemId;
    internal readonly ushort Seat, Count;
    internal TownItemHeldSource(byte kind, uint key, int itemId, ushort seat, ushort count)
    { Kind = kind; CharacterKey = key; ItemId = itemId; Seat = seat; Count = count; }
    internal bool Validate() => ItemId > 0 && (Kind == Owned
        ? CharacterKey != 0 && Count > 0 && Seat < Count
        : Kind == Stock && CharacterKey == 0 && Seat == 0 && Count == 0);
    internal bool Same(TownItemHeldSource other) => Kind == other.Kind && CharacterKey == other.CharacterKey
        && ItemId == other.ItemId && Seat == other.Seat && Count == other.Count;
    internal void Write(byte[] buffer, ref int at)
    {
        buffer[at++] = Kind;
        WriteU32(buffer, ref at, CharacterKey);
        WriteU32(buffer, ref at, (uint)ItemId);
        buffer[at++] = (byte)Seat; buffer[at++] = (byte)(Seat >> 8);
        buffer[at++] = (byte)Count; buffer[at++] = (byte)(Count >> 8);
    }
    private static void WriteU32(byte[] bytes, ref int at, uint value)
    { for (int shift = 0; shift < 32; shift += 8) bytes[at++] = (byte)(value >> shift); }
    private static uint ReadU32(byte[] bytes, ref int at)
    { uint value = 0; for (int shift = 0; shift < 32; shift += 8) value |= (uint)bytes[at++] << shift; return value; }
    internal static bool TryRead(byte[] buffer, int at, int length, out TownItemHeldSource source)
    {
        source = default;
        if (length != PayloadSize || at < 0 || at > buffer.Length - length) return false;
        byte kind = buffer[at++]; uint key = ReadU32(buffer, ref at);
        int item = (int)ReadU32(buffer, ref at);
        ushort seat = (ushort)(buffer[at++] | buffer[at++] << 8);
        ushort count = (ushort)(buffer[at++] | buffer[at++] << 8);
        source = new TownItemHeldSource(kind, key, item, seat, count);
        return source.Validate();
    }
}
