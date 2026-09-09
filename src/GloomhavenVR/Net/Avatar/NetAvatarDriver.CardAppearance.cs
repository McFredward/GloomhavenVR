using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private readonly byte[] _appearanceBuffer = new byte[CardAppearanceCodec.MaxSize];
    private CardAppearanceState[]? _sentAppearances;
    private CardAppearanceSnapshot? _appearanceSnapshot;
    private float _nextAppearanceRefresh, _appearanceSourceTime;
    private readonly Dictionary<int, List<CardAppearanceSnapshot>> _pendingAppearances = new();

    private void TickCardAppearanceSend(float now)
    {
        CardAppearanceState[] states = CardAppearanceSampler.Sample();
        bool changed = !ReferenceEquals(states, _sentAppearances);
        if (changed || _appearanceSnapshot == null)
        {
            if (_appearanceSnapshot != null && now - _appearanceSnapshot.SampleTime > .25f
                && _appearanceSourceTime > _appearanceSnapshot.SampleTime)
                SendCardAppearance(new CardAppearanceSnapshot(_appearanceSourceTime, _appearanceSnapshot.States));
            _appearanceSnapshot = new CardAppearanceSnapshot(now, states);
            _sentAppearances = states;
        }
        if (changed || now >= _nextAppearanceRefresh)
        {
            if (!changed) _appearanceSnapshot = new CardAppearanceSnapshot(now, states);
            SendCardAppearance(_appearanceSnapshot);
            _nextAppearanceRefresh = now + .5f;
        }
        _appearanceSourceTime = now;
    }

    private void SendCardAppearance(CardAppearanceSnapshot snapshot)
    {
        int length = CardAppearanceCodec.Write(snapshot, _appearanceBuffer);
        if (length > 0) _transport.Send(_appearanceBuffer, length);
    }

    private bool QueueCardAppearance(int sender, byte[] buffer, int length)
    {
        if (!CardAppearanceCodec.TryRead(buffer, length, out CardAppearanceSnapshot? frame)) return false;
        if (!_pendingAppearances.TryGetValue(sender, out List<CardAppearanceSnapshot>? samples))
        {
            if (_pendingAppearances.Count >= 8) return true;
            samples = new List<CardAppearanceSnapshot>(4);
            _pendingAppearances.Add(sender, samples);
        }
        if (samples.Count == 0 || frame!.SampleTime > samples[samples.Count - 1].SampleTime)
            PresentationPending.Append(samples, frame!, CardAppearanceSnapshot.SameIdentity);
        return true;
    }

    private void ApplyCardAppearance()
    {
        foreach (var pair in _pendingAppearances)
        {
            if (pair.Value.Count == 0) continue;
            try { CardAppearanceMirror.Set(pair.Key, pair.Value[0]); }
            catch (Exception e) { LogPhaseError($"Apply native card appearance from player {pair.Key}", e); }
            pair.Value.RemoveAt(0);
        }
    }

    private void ForgetCardAppearance(int sender)
    {
        _pendingAppearances.Remove(sender);
        CardAppearanceMirror.Remove(sender);
    }

    private void ResetCardAppearance()
    {
        foreach (int sender in _pendingAppearances.Keys) CardAppearanceMirror.Remove(sender);
        _pendingAppearances.Clear();
        _sentAppearances = null;
        _appearanceSnapshot = null;
        _nextAppearanceRefresh = _appearanceSourceTime = 0;
        CardAppearanceSampler.Reset();
    }

    internal static void MirrorCharacterCardFlight(RemoteAvatar sender, byte endpoints, byte flags, CardFlightSource source)
    {
        NetAvatarDriver? driver = _instance;
        if (driver == null) return;
        foreach (RemoteAvatar viewer in driver._avatars.Values)
        {
            if (ReferenceEquals(viewer, sender)) continue;
            CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(viewer, out _);
            if (actor != null && NetFigures.StableActorId(actor) == source.ActorId)
                viewer.PlayMirroredCardFlight(endpoints, flags, source);
        }
    }

    private static readonly List<int> DecisionClaimants = new();

    /// <summary>Resolve control from the game's partitioned player registry, never viewer focus.</summary>
    internal static bool TryGetCharacterDecisionOwner(CPlayerActor actor, out RemoteAvatar? owner)
    {
        owner = null;
        NetAvatarDriver? driver = _instance;
        if (driver == null || actor == null) return false;
        DecisionClaimants.Clear();
        if (CardsGameApi.ClaimantPlayerIds(actor, DecisionClaimants) <= 0) return false;
        foreach (int playerId in DecisionClaimants)
            if (playerId != driver._transport.LocalPlayerId && driver._avatars.TryGetValue(playerId, out owner))
                return true;
        owner = null;
        return false;
    }
}
