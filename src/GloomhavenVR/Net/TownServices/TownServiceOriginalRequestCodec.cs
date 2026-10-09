using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

internal sealed class TownServiceOriginalRequestMessage
{
    internal readonly int OriginPeer;
    internal readonly byte Service, Lane;
    internal readonly uint Session;
    internal readonly TownServiceOriginalReceiptEntry[] Entries;
    internal TownServiceOriginalRequestMessage(int originPeer, byte service, uint session,
        byte lane, TownServiceOriginalReceiptEntry[] entries)
    { OriginPeer = originPeer; Service = service; Session = session; Lane = lane; Entries = entries; }
}

/// <summary>Additive record114 in the existing message28 metadata channel. A
/// request names only the rejected original: it cannot acknowledge that original,
/// grant a service, or accept a transaction. Private merchant/mage originals only.</summary>
internal static class TownServiceOriginalRequestCodec
{
    internal const int MaxEntries = 20;
    internal const int MaxSize = 864;
    private const byte Schema = 1;
    private const int HeaderBytes = 12, EntryBytes = 10;

    internal static byte[] Write(int originPeer, byte service, uint session, byte lane,
        IReadOnlyList<TownServiceOriginalReceiptEntry> entries)
    {
        if (originPeer <= 0 || !ValidService(service) || session == 0 || lane != 0
            || entries == null || entries.Count == 0 || entries.Count > MaxEntries)
            throw new ArgumentException("Invalid original request metadata.");
        for (int i = 0; i < entries.Count; i++)
        {
            if (!ValidEntry(entries[i])) throw new ArgumentException("Invalid original request identity.");
            for (int previous = 0; previous < i; previous++)
                if (entries[previous].Module == entries[i].Module)
                    throw new ArgumentException("Duplicate original request identity.");
        }
        int count = entries.Count;
        var bytes = new byte[8 + HeaderBytes + count * EntryBytes];
        WriteNumber(bytes, 0, NetProtocol.Magic, 4);
        bytes[4] = NetProtocol.Version; bytes[5] = NetProtocol.MsgTownOriginalReceipt;
        bytes[6] = NetProtocol.ExtIdTownOriginalRequest;
        bytes[7] = (byte)(HeaderBytes + count * EntryBytes);
        bytes[8] = Schema;
        WriteNumber(bytes, 9, (uint)originPeer, 4);
        bytes[13] = service;
        WriteNumber(bytes, 14, session, 4);
        bytes[18] = lane; bytes[19] = (byte)count;
        for (int i = 0, at = 20; i < count; i++, at += EntryBytes)
        { WriteNumber(bytes, at, entries[i].Module, 2); WriteNumber(bytes, at + 2, entries[i].Sequence, 8); }
        return bytes;
    }

    internal static bool TryRead(byte[]? bytes, int length, out TownServiceOriginalRequestMessage? message)
    {
        message = null;
        if (bytes == null || length < 8 + HeaderBytes + EntryBytes || length > bytes.Length
            || length > MaxSize || NetPacket.PeekType(bytes, length) != NetProtocol.MsgTownOriginalReceipt)
            return false;
        TownServiceOriginalRequestMessage? found = null;
        for (int at = 6; at < length;)
        {
            if (length - at < 2) return false;
            int id = bytes[at++], size = bytes[at++];
            if (size > length - at || id == NetProtocol.ExtIdTownOriginalReceipt) return false;
            if (id == NetProtocol.ExtIdTownOriginalRequest)
            {
                if (found != null || size < HeaderBytes + EntryBytes || bytes[at] != Schema) return false;
                int origin = (int)ReadNumber(bytes, at + 1, 4);
                byte service = bytes[at + 5], lane = bytes[at + 10];
                uint session = (uint)ReadNumber(bytes, at + 6, 4);
                int count = bytes[at + 11];
                if (origin <= 0 || !ValidService(service) || session == 0 || lane != 0
                    || count == 0 || count > MaxEntries || size != HeaderBytes + count * EntryBytes) return false;
                var entries = new TownServiceOriginalReceiptEntry[count];
                for (int i = 0, entryAt = at + HeaderBytes; i < count; i++, entryAt += EntryBytes)
                {
                    entries[i] = new TownServiceOriginalReceiptEntry((ushort)ReadNumber(bytes, entryAt, 2),
                        ReadNumber(bytes, entryAt + 2, 8));
                    if (!ValidEntry(entries[i])) return false;
                    for (int previous = 0; previous < i; previous++)
                        if (entries[previous].Module == entries[i].Module) return false;
                }
                found = new TownServiceOriginalRequestMessage(origin, service, session, lane, entries);
            }
            at += size;
        }
        message = found;
        return found != null;
    }

    private static bool ValidService(byte service) => service is 1 or 3;
    private static bool ValidEntry(in TownServiceOriginalReceiptEntry entry) => entry.Sequence != 0
        && entry.Module < TownServiceFrame.VoiceModule && entry.Module != TownServiceFrame.UrgentBundleStream;
    private static void WriteNumber(byte[] bytes, int at, ulong value, int count)
    { for (int i = 0; i < count; i++) bytes[at + i] = (byte)(value >> (i * 8)); }
    private static ulong ReadNumber(byte[] bytes, int at, int count)
    { ulong value = 0; for (int i = 0; i < count; i++) value |= (ulong)bytes[at + i] << (i * 8); return value; }
}
