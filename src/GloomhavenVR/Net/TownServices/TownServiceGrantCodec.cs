using System;

namespace GloomhavenVR.Net.TownServices;

internal enum TownGrantKind : byte { Request = 1, Grant = 2, Busy = 3, Release = 4 }

/// <summary>One small ReliableOrdered control event. The public town manifest's TLV92 is
/// presentation state; it is never an acknowledgement of a native gameplay transaction.</summary>
internal readonly struct TownGrantMessage
{
    internal readonly TownGrantKind Kind;
    internal readonly byte Service;
    internal readonly int Player;
    internal readonly uint Session, Nonce, Epoch;
    internal TownGrantMessage(TownGrantKind kind, byte service, int player, uint session, uint nonce, uint epoch)
    { Kind = kind; Service = service; Player = player; Session = session; Nonce = nonce; Epoch = epoch; }
}

internal static class TownServiceGrantCodec
{
    // 23/24 belong to map-button tooltips. Keep this packet distinct from all cosmetic lanes.
    internal const byte MessageType = 25;
    internal const int Size = 24;
    internal static byte[] Write(in TownGrantMessage message)
    {
        if (!Valid(in message)) throw new ArgumentOutOfRangeException(nameof(message));
        byte[] bytes = new byte[Size];
        bytes[0] = 0x31; bytes[1] = 0x52; bytes[2] = 0x56; bytes[3] = 0x47;
        bytes[4] = 3; bytes[5] = MessageType;
        bytes[6] = (byte)message.Kind; bytes[7] = message.Service;
        Write32(bytes, 8, (uint)message.Player); Write32(bytes, 12, message.Session);
        Write32(bytes, 16, message.Nonce); Write32(bytes, 20, message.Epoch);
        return bytes;
    }

    internal static bool TryRead(byte[]? bytes, int length, out TownGrantMessage message)
    {
        message = default;
        if (bytes == null || length != Size || bytes.Length < length
            || bytes[0] != 0x31 || bytes[1] != 0x52 || bytes[2] != 0x56 || bytes[3] != 0x47
            || bytes[4] != 3 || bytes[5] != MessageType) return false;
        message = new TownGrantMessage((TownGrantKind)bytes[6], bytes[7],
            (int)Read32(bytes, 8), Read32(bytes, 12), Read32(bytes, 16), Read32(bytes, 20));
        return Valid(in message);
    }

    private static bool Valid(in TownGrantMessage m) => m.Kind >= TownGrantKind.Request
        && m.Kind <= TownGrantKind.Release && m.Service >= 1 && m.Service <= 3
        && m.Player > 0 && m.Session != 0 && m.Nonce != 0
        && (m.Kind == TownGrantKind.Request || m.Kind == TownGrantKind.Release || m.Epoch != 0);
    private static void Write32(byte[] b, int offset, uint value)
    { for (int i = 0; i < 4; i++) b[offset + i] = (byte)(value >> (8 * i)); }
    private static uint Read32(byte[] b, int offset) => (uint)(b[offset] | b[offset + 1] << 8
        | b[offset + 2] << 16 | b[offset + 3] << 24);
}
