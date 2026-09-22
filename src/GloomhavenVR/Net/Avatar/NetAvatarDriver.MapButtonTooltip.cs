using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI.MapRoom;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private readonly byte[] _mapTooltipBuffer = new byte[MapButtonTooltipCodec.MaxSize];
    private byte[]? _sentMapTooltip;
    private bool _mapTooltipAnnounced;
    private float _nextMapTooltipRefresh;
    private float _mapTooltipSourceTime, _mapTooltipSampleTime;
    private readonly Dictionary<int, List<MapButtonTooltipSnapshot>> _pendingMapTooltips = new();
    private readonly Dictionary<int, float> _lastMapTooltipTime = new();

    private void TickMapButtonTooltipSend(float now)
    {
        byte[]? payload = MapButtonTooltipPresentation.Capture();
        bool changed = !_mapTooltipAnnounced || !ReferenceEquals(payload, _sentMapTooltip);
        if (changed && _mapTooltipAnnounced && now - _mapTooltipSampleTime > .25f
            && _mapTooltipSourceTime > _mapTooltipSampleTime)
        {
            // End a stationary hold immediately before the next native animation sample.
            // Otherwise interpolation would spread the new movement over the refresh gap.
            var hold = new MapButtonTooltipSnapshot(_mapTooltipSourceTime, _sentMapTooltip);
            int holdLength = MapButtonTooltipCodec.Write(hold, _mapTooltipBuffer);
            if (holdLength > 0) _transport.Send(_mapTooltipBuffer, holdLength, hold);
        }
        _mapTooltipSourceTime = now;
        if (!changed && now < _nextMapTooltipRefresh) return;
        var snapshot = new MapButtonTooltipSnapshot(now, payload);
        int length = MapButtonTooltipCodec.Write(snapshot, _mapTooltipBuffer);
        if (length == 0) return;
        _transport.Send(_mapTooltipBuffer, length, snapshot);
        _sentMapTooltip = payload;
        _mapTooltipAnnounced = true;
        _mapTooltipSampleTime = now;
        // Retry visible pictures for late arrivals; a hidden heartbeat is infrequent and
        // guarantees cleanup after a lost close without generating routine log traffic.
        _nextMapTooltipRefresh = now + (payload == null ? 2f : .5f);
    }

    private bool QueueMapButtonTooltip(int sender, byte[] buffer, int length)
    {
        if (!MapButtonTooltipCodec.TryRead(buffer, length, out MapButtonTooltipSnapshot? snapshot)) return false;
        if (_lastMapTooltipTime.TryGetValue(sender, out float last) && snapshot!.SampleTime <= last) return true;
        if (!_pendingMapTooltips.TryGetValue(sender, out var samples))
        {
            if (_pendingMapTooltips.Count >= 8) return true;
            samples = new List<MapButtonTooltipSnapshot>(4);
            _pendingMapTooltips.Add(sender, samples);
        }
        _lastMapTooltipTime[sender] = snapshot!.SampleTime;
        PresentationPending.Append(samples, snapshot, MapButtonTooltipSnapshot.SameIdentity);
        return true;
    }

    private void ApplyMapButtonTooltip()
    {
        foreach (var pair in _pendingMapTooltips)
        {
            if (pair.Value.Count == 0) continue;
            MapButtonTooltipSnapshot sample = pair.Value[0];
            pair.Value.RemoveAt(0);
            try { MapButtonTooltipPresentation.Receive(pair.Key, sample.Payload, sample.SampleTime); }
            catch (Exception e) { LogPhaseError("Apply map button tooltip", e); }
        }
        try { MapButtonTooltipPresentation.Tick(); }
        catch (Exception e) { LogPhaseError("Update map button tooltip", e); }
    }

    private void ForgetMapButtonTooltip(int sender)
    {
        _pendingMapTooltips.Remove(sender);
        _lastMapTooltipTime.Remove(sender);
        MapButtonTooltipPresentation.Remove(sender);
    }

    private void ResetMapButtonTooltip()
    {
        _pendingMapTooltips.Clear(); _lastMapTooltipTime.Clear();
        _sentMapTooltip = null; _mapTooltipAnnounced = false; _nextMapTooltipRefresh = 0;
        _mapTooltipSourceTime = _mapTooltipSampleTime = 0;
        MapButtonTooltipPresentation.Reset();
    }
}
