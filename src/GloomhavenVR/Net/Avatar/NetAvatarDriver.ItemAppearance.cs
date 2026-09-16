using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    internal static bool CanPublishNativePresentation => _instance != null && _instance.isActiveAndEnabled
        && !NetSession.FlatNetMode && Core.VRSession.IsRunning && _instance._transport.IsOnline
        && _instance._transport.LocalPlayerId > 0;

    private readonly byte[] _itemAppearanceBuffer = new byte[ItemAppearanceCodec.MaxSize];
    private ItemAppearanceState[]? _sentItemAppearances;
    private ItemAppearanceSnapshot? _itemAppearanceSnapshot;
    private float _nextItemAppearanceRefresh, _itemAppearanceSourceTime;
    private readonly Dictionary<int, List<ItemAppearanceSnapshot>> _pendingItemAppearances = new();

    private void TickItemAppearanceSend(float now)
    {
        ItemAppearanceState[] states = ItemAppearanceSampler.Sample();
        bool changed = !ReferenceEquals(states, _sentItemAppearances);
        if (changed || _itemAppearanceSnapshot == null)
        {
            if (_itemAppearanceSnapshot != null && now - _itemAppearanceSnapshot.SampleTime > .25f
                && _itemAppearanceSourceTime > _itemAppearanceSnapshot.SampleTime)
                SendItemAppearance(new ItemAppearanceSnapshot(_itemAppearanceSourceTime, _itemAppearanceSnapshot.States));
            _itemAppearanceSnapshot = new ItemAppearanceSnapshot(now, states);
            _sentItemAppearances = states;
        }
        if (changed || now >= _nextItemAppearanceRefresh)
        {
            if (!changed) _itemAppearanceSnapshot = new ItemAppearanceSnapshot(now, states);
            SendItemAppearance(_itemAppearanceSnapshot);
            _nextItemAppearanceRefresh = now + .5f;
        }
        _itemAppearanceSourceTime = now;
    }

    private void SendItemAppearance(ItemAppearanceSnapshot snapshot)
    {
        int length = ItemAppearanceCodec.Write(snapshot, _itemAppearanceBuffer);
        if (length > 0) _transport.Send(_itemAppearanceBuffer, length, snapshot);
    }

    private bool QueueItemAppearance(int sender, byte[] buffer, int length)
    {
        if (!ItemAppearanceCodec.TryRead(buffer, length, out ItemAppearanceSnapshot? frame)) return false;
        if (!_pendingItemAppearances.TryGetValue(sender, out List<ItemAppearanceSnapshot>? samples))
        {
            if (_pendingItemAppearances.Count >= 8) return true;
            samples = new List<ItemAppearanceSnapshot>(4);
            _pendingItemAppearances.Add(sender, samples);
        }
        if (samples.Count == 0 || frame!.SampleTime > samples[samples.Count - 1].SampleTime)
            PresentationPending.Append(samples, frame!, ItemAppearanceSnapshot.SameIdentity);
        return true;
    }

    private void ApplyItemAppearance()
    {
        foreach (var pair in _pendingItemAppearances)
        {
            if (pair.Value.Count == 0) continue;
            try { ItemAppearanceMirror.Set(pair.Key, pair.Value[0]); }
            catch (Exception e) { LogPhaseError($"Apply native item appearance from player {pair.Key}", e); }
            pair.Value.RemoveAt(0);
        }
    }

    private void ForgetItemAppearance(int sender)
    {
        _pendingItemAppearances.Remove(sender);
        ItemAppearanceMirror.Remove(sender);
    }

    private void ResetItemAppearance()
    {
        foreach (int sender in _pendingItemAppearances.Keys) ItemAppearanceMirror.Remove(sender);
        _pendingItemAppearances.Clear();
        _sentItemAppearances = null;
        _itemAppearanceSnapshot = null;
        _nextItemAppearanceRefresh = _itemAppearanceSourceTime = 0;
        ItemAppearanceSampler.Reset();
    }

}
