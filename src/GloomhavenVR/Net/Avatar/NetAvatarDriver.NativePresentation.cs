using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private readonly byte[] _boardBuffer = new byte[NativeBoardCodec.MaxSize];
    private NativeBoardState? _sentBoard;
    private float _nextBoardRefresh, _boardSourceTime;
    private readonly Dictionary<int, List<NativeBoardState>> _pendingBoards = new();
    private readonly byte[] _plumeBuffer = new byte[CardPlumeCodec.MaxSize];
    private readonly byte[] _nativeBuffer = new byte[NativeUseBarPacket.MaxSize];
    private CardPlumeState[]? _sentPlumes;
    private CardPlumeSnapshot? _plumeSnapshot;
    private float _nextPlumeRefresh;
    private readonly NativeUseBarState?[] _sentNative = new NativeUseBarState?[32];
    private readonly NativeUseBarSnapshot?[] _nativeSnapshots = new NativeUseBarSnapshot?[32];
    private readonly float[] _nextNativeRefresh = new float[32];
    private float _nativeSourceTime;
    private readonly Dictionary<int, List<CardPlumeSnapshot>> _pendingPlumes = new();
    private readonly Dictionary<int, List<NativeUseBarSnapshot>[]> _pendingNative = new();

    private void TickNativePresentationSend(NativeUseBarState?[] states)
    {
        if (NetSession.FlatNetMode || !VRSession.IsRunning || !_transport.IsOnline
            || _transport.LocalPlayerId <= 0) return;
        using var timing = PerfMonitor.Scope("Net.Presentation.NativeSend");
        float now = Time.unscaledTime;
        try { SendTownServices(); }
        catch (Exception e) { LogPhaseError("Sample native town services", e); }
        // Capture the final owner pixels in the same LateUpdate pass as native board animation.
        // Presence consumes this immutable snapshot on its next tick, never an unfinished layout.
        try { _decisionHighlightSnapshot = NativeDecisionHighlightSampler.Sample(); }
        catch (Exception e) { _decisionHighlightSnapshot = null; LogPhaseError("Sample native decision highlight", e); }
        try { TickNativePromptSend(now); }
        catch (Exception e) { LogPhaseError("Sample native decision prompt", e); }
        try { TickItemAppearanceSend(now); }
        catch (Exception e) { LogPhaseError("Sample native item appearance", e); }
        try { TickCardAppearanceSend(now); }
        catch (Exception e) { LogPhaseError("Sample native card appearance", e); }
        try { TickNativeBoardSend(now); }
        catch (Exception e) { LogPhaseError("Sample native board", e); }
        try
        {
            CardPlumeState[] plumes = CardPlumeSampler.Sample();
            bool changed = !ReferenceEquals(plumes, _sentPlumes);
            if (changed || _plumeSnapshot == null)
            {
                _plumeSnapshot = new CardPlumeSnapshot(now, plumes);
                _sentPlumes = plumes;
            }
            if (changed || now >= _nextPlumeRefresh)
            {
                int length = CardPlumeCodec.Write(_plumeSnapshot, _plumeBuffer);
                _transport.Send(_plumeBuffer, length);
                _nextPlumeRefresh = now + .5f;
            }
        }
        catch (Exception e) { LogPhaseError("Sample native card plume", e); }
        for (int i = 8; i < 32; i++)
        {
            try
            {
                bool changed = !ReferenceEquals(states[i], _sentNative[i]);
                NativeUseBarSnapshot? previous = _nativeSnapshots[i];
                if (!changed && previous == null) continue;
                if (changed)
                {
                    if (previous != null && now - previous.SampleTime > .25f && _nativeSourceTime > previous.SampleTime)
                        SendNative(new NativeUseBarSnapshot(_nativeSourceTime, previous.Bar, previous.Slot, previous.State));
                    _nativeSnapshots[i] = new NativeUseBarSnapshot(now, (byte)(i / 8), (byte)(i % 8), states[i]);
                    _sentNative[i] = states[i];
                }
                if (changed || now >= _nextNativeRefresh[i])
                {
                    SendNative(_nativeSnapshots[i]!);
                    _nextNativeRefresh[i] = now + .5f;
                }
            }
            catch (Exception e) { LogPhaseError($"Sample native use-bar slot {i}", e); }
        }
        _nativeSourceTime = now;
    }

    private void TickNativeBoardSend(float now)
    {
        NativeBoardState? state = NativeBoardSampler.Sample();
        if (state == null) return;
        bool changed = !ReferenceEquals(state, _sentBoard);
        if (changed)
        {
            if (_sentBoard != null && now - _sentBoard.SampleTime > .25f && _boardSourceTime > _sentBoard.SampleTime)
                SendNativeBoard(_sentBoard.CopyWithTime(_boardSourceTime));
            _sentBoard = state;
        }
        if (changed || now >= _nextBoardRefresh)
        {
            SendNativeBoard(state);
            _nextBoardRefresh = now + .5f;
        }
        _boardSourceTime = now;
    }
    private void SendNativeBoard(NativeBoardState state)
    {
        int length = NativeBoardCodec.Write(state, _boardBuffer);
        _transport.Send(_boardBuffer, length, state);
    }

    private void SendNative(NativeUseBarSnapshot snapshot)
    {
        int length = NativeUseBarPacket.Write(snapshot, _nativeBuffer);
        if (length > 0) _transport.Send(_nativeBuffer, length, snapshot);
    }

    private bool QueueNativePresentation(int sender, byte[] buffer, int length)
    {
        if (NetPacket.PeekType(buffer, length) == NetProtocol.MsgNativeDecisionPrompt)
            return QueueNativePrompt(sender, buffer, length);
        if (NetPacket.PeekType(buffer, length) == NetProtocol.MsgItemAppearance)
            return QueueItemAppearance(sender, buffer, length);
        if (NetPacket.PeekType(buffer, length) == NetProtocol.MsgCardAppearance)
            return QueueCardAppearance(sender, buffer, length);
        if (NetPacket.PeekType(buffer, length) == NetProtocol.MsgNativeBoard)
        {
            if (!NativeBoardCodec.TryRead(buffer, length, out NativeBoardState? frame)) return false;
            if (!_pendingBoards.TryGetValue(sender, out List<NativeBoardState>? samples))
            {
                if (_pendingBoards.Count >= 8) return true;
                samples = new List<NativeBoardState>(4); _pendingBoards.Add(sender, samples);
            }
            if (samples.Count > 0 && frame!.SampleTime <= samples[samples.Count - 1].SampleTime) return true;
            PresentationPending.Append(samples, frame!, (a, b) => a.Generation == b.Generation);
            return true;
        }
        if (NetPacket.PeekType(buffer, length) == NetProtocol.MsgCardPlume)
        {
            if (!CardPlumeCodec.TryRead(buffer, length, out CardPlumeSnapshot? frame)) return false;
            if (!_pendingPlumes.TryGetValue(sender, out List<CardPlumeSnapshot>? samples))
            {
                if (_pendingPlumes.Count >= 8) return true;
                samples = new List<CardPlumeSnapshot>(4); _pendingPlumes.Add(sender, samples);
            }
            if (samples.Count == 0 || frame!.SampleTime > samples[samples.Count - 1].SampleTime)
                PresentationPending.Append(samples, frame!, PresentationPending.SamePlumeIdentity);
            return true;
        }
        if (!NativeUseBarPacket.TryRead(buffer, length, out NativeUseBarSnapshot? snapshot)) return false;
        if (!_pendingNative.TryGetValue(sender, out List<NativeUseBarSnapshot>[]? slots))
        {
            if (_pendingNative.Count >= 8) return true;
            slots = new List<NativeUseBarSnapshot>[32];
            for (int i = 8; i < slots.Length; i++) slots[i] = new List<NativeUseBarSnapshot>(4);
            _pendingNative.Add(sender, slots);
        }
        var queue = slots[snapshot!.Address];
        if (queue.Count > 0 && snapshot.SampleTime <= queue[queue.Count - 1].SampleTime) return true;
        PresentationPending.Append(queue, snapshot, PresentationPending.SameNativeIdentity);
        return true;
    }

    private void ApplyNativePresentation()
    {
        ApplyTownServices();
        ApplyItemAppearance();
        ApplyCardAppearance();
        ApplyNativePrompt();
        foreach (var pair in _pendingBoards)
        {
            if (pair.Value.Count == 0) continue;
            try
            {
                GetOrCreate(pair.Key)?.SetNativeBoard(pair.Value[0]);
                pair.Value.RemoveAt(0);
            }
            catch (Exception e) { LogPhaseError($"Apply native board from player {pair.Key}", e); }
        }
        foreach (var pair in _pendingPlumes)
        {
            if (pair.Value.Count == 0) continue;
            try
            {
                GetOrCreate(pair.Key)?.SetCardPlume(pair.Value[0]);
                pair.Value.RemoveAt(0);
            }
            catch (Exception e) { LogPhaseError($"Apply card plume from player {pair.Key}", e); }
        }
        foreach (var pair in _pendingNative)
        {
            try
            {
                RemoteAvatar? avatar = GetOrCreate(pair.Key);
                if (avatar == null) continue;
                for (int i = 8; i < pair.Value.Length; i++)
                {
                    var queue = pair.Value[i];
                    if (queue.Count == 0) continue;
                    avatar.SetNativeUseBar(queue[0]);
                    queue.RemoveAt(0);
                }
            }
            catch (Exception e) { LogPhaseError($"Apply native slots from player {pair.Key}", e); }
        }
    }

    private void ForgetNativePresentation(int sender)
    { ForgetTownServices(sender); _pendingPlumes.Remove(sender); _pendingNative.Remove(sender); _pendingBoards.Remove(sender); ForgetItemAppearance(sender); ForgetCardAppearance(sender); ForgetNativePrompt(sender); }

    private void ResetNativePresentation()
    {
        ResetTownServices();
        _pendingPlumes.Clear(); _pendingNative.Clear(); _pendingBoards.Clear();
        ResetItemAppearance();
        ResetCardAppearance();
        ResetNativePrompt();
        _lastSentDamageDecisionPreview = null;
        _lastSentDecisionActor = 0;
        _lastSentDecisionPending = _lastSentDecisionVisible = false;
        _sentBoard = null; _nextBoardRefresh = _boardSourceTime = 0;
        NativeBoardSampler.Reset();
        NativeDecisionHighlightSampler.Reset();
        _lastSentDecisionHighlight = _decisionHighlightSnapshot = null;
        _sentPlumes = null; _plumeSnapshot = null; _nextPlumeRefresh = _nativeSourceTime = 0;
        Array.Clear(_sentNative, 0, _sentNative.Length);
        Array.Clear(_nativeSnapshots, 0, _nativeSnapshots.Length);
        Array.Clear(_nextNativeRefresh, 0, _nextNativeRefresh.Length);
        CardPlumeSampler.Reset();
        NativeUseBarSampler.Reset();
    }
}
