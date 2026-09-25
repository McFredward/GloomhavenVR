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
    private readonly Dictionary<int, List<TownPacket>> _pendingTownVoice = new();
    private readonly byte[] _activityBuffer = new byte[TownActivityCodec.PacketBytes];
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
        TownActivityState activity = TownServicePopulation.PublishedActivities;
        int count = TownActivityCodec.WritePacket(_activityBuffer, in activity, in face);
        if (count > 0) _transport.Send(_activityBuffer, count);
    }
    private bool QueueTownService(int sender, byte[] bytes, int length)
    {
        if (!TownServiceCodec.TryRead(bytes, length, out TownServiceFrame? frame)) return false;
        if (frame!.Module == TownServiceFrame.VoiceModule)
        {
            if (!TownServiceVoiceRelayCodec.TryRead(frame, out _)) return true;
            if (!_pendingTownVoice.TryGetValue(sender, out List<TownPacket>? voice))
            {
                if (_pendingTownVoice.Count >= 8) return true;
                voice = new List<TownPacket>(16); _pendingTownVoice.Add(sender, voice);
            }
            if (voice.Exists(packet => packet.Sequence == frame.Sequence)) return true;
            if (voice.Count >= 16) voice.RemoveAt(0);
            byte[] eventCopy = new byte[length]; Buffer.BlockCopy(bytes, 0, eventCopy, 0, length);
            voice.Add(new TownPacket { Sequence = frame.Sequence, Bytes = eventCopy });
            return true;
        }
        if (!_pendingTown.TryGetValue(sender, out Dictionary<uint, TownPacket>? pending))
        {
            if (_pendingTown.Count >= 8) return true;
            pending = new Dictionary<uint, TownPacket>(); _pendingTown.Add(sender, pending);
        }
        // Preserve the complete baseline separately from the latest cumulative delta when
        // several completed fragments arrive before the main-thread presentation pass.
        uint key = frame!.Module | (frame.BaseSequence != 0 ? 65536u : 0u) | (frame.PublicCatalog ? 131072u : 0u);
        if (pending.TryGetValue(key, out TownPacket? previous) && frame.Sequence <= previous.Sequence) return true;
        if (pending.Count >= 4 * TownServiceFrame.MaxModules + 2 && !pending.ContainsKey(key)) return true;
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
        foreach (var peer in _pendingTownVoice)
        {
            peer.Value.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            foreach (TownPacket packet in peer.Value) TownServiceMirror.Receive(peer.Key, packet.Bytes, packet.Bytes.Length);
            peer.Value.Clear();
        }
        if (TownServiceMirror.SharedFrameForRemote != null) TownServiceMirror.TickRemote(TownServiceMirror.SharedFrameForRemote);
    }
    private void ForgetTownServices(int peer) { _pendingTown.Remove(peer); _pendingTownVoice.Remove(peer); TownServiceMirror.RemovePeer(peer); RemoteTownResidents.Forget(peer); RemoteTownFaces.Forget(peer); RemoteTownActivities.Forget(peer); }
    private void ResetTownServices() { _pendingTown.Clear(); _pendingTownVoice.Clear(); TownServiceMirror.ResetNetwork(); RemoteTownResidents.Reset(); RemoteTownFaces.Reset(); RemoteTownActivities.Reset(); _nextFaceSend = 0f; }
}
