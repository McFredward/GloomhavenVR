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
    { internal ulong Sequence; internal uint Session; internal byte Service; internal bool VisitorStock; internal TownServiceFrame Frame = null!; }
    private readonly Dictionary<int, Dictionary<uint, TownPacket>> _pendingTown = new();
    private readonly Dictionary<int, List<TownPacket>> _pendingTownVoice = new();
    private readonly Dictionary<int, List<TownServiceMotionPacket>> _pendingTownMotion = new();
    private readonly byte[] _activityBuffer = new byte[TownActivityCodec.PacketBytes];
    private float _nextFaceSend, _nextTownCapture;
    private void SendTownServices()
    {
        TownServiceGrantSync.Tick(_transport, UnityEngine.Time.unscaledTime);
        // Original numeric motion and rig packets already run at15 Hz. Sampling and
        // serializing every render frame only rebuilt discarded snapshots between those
        // sends, especially for the complete public cabinet. Keep the final-writer seam
        // and one current sample per transport interval; never run catch-up work.
        float now = UnityEngine.Time.unscaledTime;
        if (now >= _nextTownCapture)
        {
            _nextTownCapture = now + 1f / 15f;
            using var capture = PerfMonitor.Scope("Net.Town.Capture");
            try { TownServiceMirror.Capture((bytes, length, identity) => _transport.Send(bytes, length, identity)); }
            catch (Exception error) { LogPhaseError("Sample native town services", error); }
        }
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
            if (voice.Exists(packet => packet.Sequence == frame.Sequence && packet.Session == frame.Session
                && packet.Service == frame.Service && packet.VisitorStock == frame.VisitorStock)) return true;
            if (voice.Count >= 16) voice.RemoveAt(0);
            voice.Add(new TownPacket { Sequence = frame.Sequence, Session = frame.Session, Service = frame.Service,
                VisitorStock = frame.VisitorStock, Frame = frame });
            return true;
        }
        if (!_pendingTown.TryGetValue(sender, out Dictionary<uint, TownPacket>? pending))
        {
            if (_pendingTown.Count >= 8) return true;
            pending = new Dictionary<uint, TownPacket>(); _pendingTown.Add(sender, pending);
        }
        // Preserve the complete baseline separately from the latest cumulative delta when
        // several completed fragments arrive before the main-thread presentation pass.
        // Reference-only clocks may fail cold admission. Their complete repair
        // with the same source sequence must survive until the main-thread pass.
        uint key = frame!.Module | (frame.BaseSequence != 0 ? 65536u : 0u) | (frame.PublicCatalog ? 131072u : 0u) | (frame.VisitorStock ? 262144u : 0u)
            | (frame.CatalogBank?.Updates.Length == 0 ? 524288u : 0u);
        if (pending.TryGetValue(key, out TownPacket? previous) && frame.Sequence <= previous.Sequence) return true;
        if (pending.Count >= 6 * TownServiceFrame.MaxModules + 5 && !pending.ContainsKey(key)) return true;
        pending[key] = new TownPacket { Sequence = frame.Sequence, Frame = frame };
        return true;
    }
    private bool QueueTownMotion(int sender, byte[] bytes, int length)
    {
        if (sender <= 0 || !TownServiceMotionCodec.TryRead(bytes, length, out TownServiceMotionPacket? packet)) return false;
        if (!_pendingTownMotion.TryGetValue(sender, out List<TownServiceMotionPacket>? pending))
        { if (_pendingTownMotion.Count >= 8) return true;
          pending = new List<TownServiceMotionPacket>(4); _pendingTownMotion.Add(sender, pending); }
        // Different packets contain different bindings. Keep a bounded set until
        // the main-thread pass, then the mirror coalesces by original affinity.
        if (pending.Exists(before => before.Sequence == packet!.Sequence)) return true;
        if (pending.Count >= 32) pending.RemoveAt(0);
        pending.Add(packet!); return true;
    }
    private void ApplyTownServices()
    {
        foreach (var peer in _pendingTown)
        {
            foreach (TownPacket packet in peer.Value.Values) TownServiceMirror.ReceiveParsed(peer.Key, packet.Frame);
            peer.Value.Clear();
        }
        foreach (var peer in _pendingTownVoice)
        {
            peer.Value.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            foreach (TownPacket packet in peer.Value) TownServiceMirror.ReceiveParsed(peer.Key, packet.Frame);
            peer.Value.Clear();
        }
        foreach (var peer in _pendingTownMotion)
        {
            peer.Value.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            foreach (TownServiceMotionPacket packet in peer.Value) TownServiceMirror.ReceiveMotion(peer.Key, packet);
            peer.Value.Clear();
        }
        if (TownServiceMirror.SharedFrameForRemote != null) TownServiceMirror.TickRemote(TownServiceMirror.SharedFrameForRemote);
    }
    private void ForgetTownServices(int peer) { _pendingTown.Remove(peer); _pendingTownVoice.Remove(peer); _pendingTownMotion.Remove(peer); TownServiceGrantSync.ForgetPeer(peer); TownServiceMirror.RemovePeer(peer); RemoteTownResidents.Forget(peer); RemoteTownFaces.Forget(peer); RemoteTownActivities.Forget(peer); }
    private void ResetTownServices() { _pendingTown.Clear(); _pendingTownVoice.Clear(); _pendingTownMotion.Clear(); TownServiceGrantSync.Reset(); TownServiceMirror.ResetNetwork(); RemoteTownResidents.Reset(); RemoteTownFaces.Reset(); RemoteTownActivities.Reset(); _nextFaceSend = _nextTownCapture = 0f; }
}
