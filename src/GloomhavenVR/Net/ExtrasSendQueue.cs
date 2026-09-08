using System;

namespace GloomhavenVR.Net;

/// <summary>
/// Finish one snapshot, then send the newest waiting snapshot. One event per 50 ms avoids
/// dumping more fragments into Bolt than its two packing attempts can carry. Memory stays
/// bounded at one in-flight snapshot plus one latest snapshot; obsolete waiting data is replaced.
/// </summary>
internal sealed class ExtrasSendQueue
{
    private byte[][]? _pages;
    private int _page;
    private byte[]? _latest;
    private double _next;
    private ulong _sequence;

    internal ExtrasSendQueue(ulong sequence) => _sequence = sequence;
    internal void Clear() { _pages = null; _latest = null; _page = 0; _next = 0; }

    internal void Enqueue(byte[] snapshot, int length)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length
            || length > ExtrasFragments.MaxSnapshotBytes
            || NetPacket.PeekType(snapshot, length) != NetProtocol.MsgExtras)
            throw new ArgumentException("Invalid extras snapshot.", nameof(snapshot));
        _latest = new byte[length];
        Buffer.BlockCopy(snapshot, 0, _latest, 0, length);
    }

    internal byte[]? Next(double now)
    {
        if (now < _next) return null;
        if (_pages == null)
        {
            if (_latest == null) return null;
            _pages = ExtrasFragments.Encode(_latest, _latest.Length, ++_sequence);
            _latest = null;
            _page = 0;
        }
        byte[] result = _pages[_page++];
        if (_page == _pages.Length) _pages = null;
        _next = now + 0.05; // no catch-up burst after a slow frame
        return result;
    }
}
