using System;
using UnityEngine;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Core;
using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private sealed class TownPacket
    { internal ulong Sequence; internal byte[] Bytes = null!; }
    private readonly Dictionary<int, Dictionary<uint, TownPacket>> _pendingTown = new();
    private readonly byte[] _faceBuffer = new byte[TownFaceCodec.PacketBytes];
    private float _nextFaceSend;
    private void SendTownServices()
    {
        try { TownServiceMirror.Capture((bytes, length, identity) => _transport.Send(bytes, length, identity)); }
        catch (Exception error) { LogPhaseError("Sample native town services", error); }
        // Native widget capture is independent of resident facial motion.

        if (NetSession.FlatNetMode || !VRSession.IsRunning || !_transport.IsOnline || _transport.LocalPlayerId <= 0
            || !TownServicePopulation.IsFaceAuthor || UnityEngine.Time.unscaledTime < _nextFaceSend) return;
        TownFaceState face = TownServicePopulation.PublishedFaces;
        if (!face.Active) return;
        _nextFaceSend = UnityEngine.Time.unscaledTime + 1f / 15f;
        int count = TownFaceCodec.WritePacket(_faceBuffer, in face);
        if (count > 0) _transport.Send(_faceBuffer, count);
    }
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
    private void ForgetTownServices(int peer) { _pendingTown.Remove(peer); TownServiceMirror.RemovePeer(peer); RemoteTownResidents.Forget(peer); RemoteTownFaces.Forget(peer); }
    private void ResetTownServices() { _pendingTown.Clear(); TownServiceMirror.ResetNetwork(); RemoteTownResidents.Reset(); RemoteTownFaces.Reset(); _nextFaceSend = 0f; }
}
