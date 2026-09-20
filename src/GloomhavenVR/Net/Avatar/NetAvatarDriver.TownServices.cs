using System;
using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private sealed class TownPacket
    { internal ulong Sequence; internal byte[] Bytes = null!; }
    private readonly Dictionary<int, Dictionary<uint, TownPacket>> _pendingTown = new();
    private void SendTownServices() => TownServiceMirror.Capture((bytes, length, identity) => _transport.Send(bytes, length, identity));
    private bool QueueTownService(int sender, byte[] bytes, int length)
    {
        if (!TownServiceCodec.TryRead(bytes, length, out TownServiceFrame? frame)) return false;
        if (!_pendingTown.TryGetValue(sender, out Dictionary<uint, TownPacket>? pending))
        {
            if (_pendingTown.Count >= 8) return true;
            pending = new Dictionary<uint, TownPacket>(); _pendingTown.Add(sender, pending);
        }
        // Preserve the complete baseline separately from the latest cumulative delta when
        // several completed fragments arrive before the main-thread presentation pass.
        uint key = frame!.Module | (frame.BaseSequence != 0 ? 65536u : 0u);
        if (pending.TryGetValue(key, out TownPacket? previous) && frame.Sequence <= previous.Sequence) return true;
        if (pending.Count >= 2 * TownServiceFrame.MaxModules + 1 && !pending.ContainsKey(key)) return true;
        byte[] copy = new byte[length]; Buffer.BlockCopy(bytes, 0, copy, 0, length); pending[key] = new TownPacket { Sequence = frame.Sequence, Bytes = copy };
        return true;
    }
    private void ApplyTownServices()
    {
        foreach (var peer in _pendingTown)
        {
            foreach (TownPacket packet in peer.Value.Values) TownServiceMirror.Receive(peer.Key, packet.Bytes, packet.Bytes.Length);
            peer.Value.Clear();
        }
        if (TownServiceMirror.SharedFrameForRemote != null) TownServiceMirror.TickRemote(TownServiceMirror.SharedFrameForRemote);
    }
    private void ForgetTownServices(int peer) { _pendingTown.Remove(peer); TownServiceMirror.RemovePeer(peer); }
    private void ResetTownServices() { _pendingTown.Clear(); TownServiceMirror.ResetNetwork(); }
}
