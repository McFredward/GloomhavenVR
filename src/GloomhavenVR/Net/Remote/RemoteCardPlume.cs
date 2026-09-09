using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net;

/// <summary>
/// The owner's actual native CardSmoke episodes, positioned in the rendered board frame.
/// Prefab modules and root-only VR scaling match BurnCardFx; colour, lifetime, playback and
/// seed come from the live owner. There is no observer lifetime, emitter cap or guessed FX edge.
/// </summary>
internal sealed class RemoteCardPlume
{
    private sealed class Host
    {
        internal GameObject Root = null!;
        internal ParticleSystem Smoke = null!;
        internal uint Episode;
        internal Transform? CustomSpace;
        internal float AppliedSample = -1f;
        internal float NativeAge;
        internal bool AuthoredEmission;
        internal CardPlumeState? PreviousPose, CurrentPose;
        internal float PreviousPoseTime, CurrentPoseTime;
        internal readonly UseBarAnimationPlaybackClock PoseClock = new();
    }

    private readonly RemoteAvatar _owner;
    private readonly Dictionary<(int Actor, byte Source, byte Face, byte Slot, ushort Identity, byte Emitter), Host> _hosts = new();
    private readonly HashSet<(int Actor, byte Source, byte Face, byte Slot, ushort Identity, byte Emitter)> _seen = new();
    private readonly List<(int Actor, byte Source, byte Face, byte Slot, ushort Identity, byte Emitter)> _prune = new();
    private readonly List<AbilityCardUI> _widgets = new();
    private CardPlumeState[]? _lastStates;
    private float _receivedAt;
    private static GameObject? s_prefab;
    private bool _logged;

    internal RemoteCardPlume(RemoteAvatar owner) => _owner = owner;

    internal void Tick(CardPlumeState[]? states, float sampleTime)
    {
        if (!ReferenceEquals(states, _lastStates))
        {
            _lastStates = states;
            _receivedAt = Time.unscaledTime;
        }
        _seen.Clear();
        if (states != null && RemoteBoardGate.ShowBoardSurface(_owner)
            && _owner.TryDrawnBoardPose(out Vector3 boardPosition, out Quaternion boardRotation,
                out float boardScale))
        {
            for (int i = 0; i < states.Length; i++)
            {
                CardPlumeState state = states[i];
                if (!CanShow(state)) continue;
                var key = (state.ActorId, state.Source, state.FaceCode, state.BonusSlot, state.BonusIdentity, state.EmitterIndex);
                _seen.Add(key);
                if (!_hosts.TryGetValue(key, out Host? host) || host.Root == null)
                {
                    host = Create(state);
                    if (host == null) continue;
                    _hosts[key] = host;
                }
                ApplyPose(host, state, sampleTime, boardPosition, boardRotation, boardScale);
                Apply(host, state, sampleTime);
            }
        }
        _prune.Clear();
        foreach (var key in _hosts.Keys)
            if (!_seen.Contains(key)) _prune.Add(key);
        for (int i = 0; i < _prune.Count; i++)
        {
            DestroyHost(_hosts[_prune[i]]);
            _hosts.Remove(_prune[i]);
        }
    }

    private static void ApplyPose(Host host, CardPlumeState state, float sampleTime,
        Vector3 boardPosition, Quaternion boardRotation, float boardScale)
    {
        bool reset = host.CurrentPose == null || host.CurrentPose.Episode != state.Episode;
        if (reset)
        {
            host.PreviousPose = host.CurrentPose = state;
            host.PreviousPoseTime = host.CurrentPoseTime = sampleTime;
            host.PoseClock.Reset(sampleTime, Time.unscaledTime);
        }
        else if (host.CurrentPoseTime != sampleTime)
        {
            host.PreviousPose = host.CurrentPose;
            host.PreviousPoseTime = host.CurrentPoseTime;
            host.CurrentPose = state;
            host.CurrentPoseTime = sampleTime;
        }
        host.PoseClock.Advance(Time.unscaledTime, sampleTime);
        float progress = host.PoseClock.Progress(host.PreviousPoseTime, host.CurrentPoseTime);
        CardPlumeState previous = host.PreviousPose!;
        // Interpolate the owner's board-relative samples, then compose the observer's currently
        // drawn board frame. Neither packet arrival nor local board motion restarts an episode.
        Transform t = host.Root.transform;
        t.SetPositionAndRotation(boardPosition + boardRotation
            * (Vector3.Lerp(previous.LocalPosition, state.LocalPosition, progress) * boardScale),
            boardRotation * Quaternion.Slerp(previous.LocalRotation, state.LocalRotation, progress));
        Vector3 worldScale = Vector3.Lerp(previous.LocalScale, state.LocalScale, progress) * boardScale;
        bool localScaling = (state.Flags >> 6) == (int)ParticleSystemScalingMode.Local;
        if (localScaling)
        {
            float scaleProgress = (previous.Flags >> 6) == (int)ParticleSystemScalingMode.Local ? progress : 1f;
            Vector3 emitterScale = Vector3.Lerp(previous.EmitterLocalScale, state.EmitterLocalScale, scaleProgress);
            host.Smoke.transform.localScale = emitterScale;
            // Local mode deliberately ignores parent scaling for particle size. The bridge
            // retains the sampled world transform while this child retains its actual local scale.
            t.localScale = new Vector3(ParentScale(worldScale.x, emitterScale.x),
                ParentScale(worldScale.y, emitterScale.y), ParentScale(worldScale.z, emitterScale.z));
        }
        else
        {
            host.Smoke.transform.localScale = Vector3.one;
            t.localScale = worldScale;
        }
        if (state.CustomSpacePresent)
        {
            if (host.CustomSpace == null)
                host.CustomSpace = new GameObject("GloomhavenVR.NativeSmokeCustomSpace").transform;
            float customProgress = previous.CustomSpacePresent ? progress : 1f;
            host.CustomSpace.SetPositionAndRotation(boardPosition + boardRotation
                * (Vector3.Lerp(previous.CustomPosition, state.CustomPosition, customProgress) * boardScale),
                boardRotation * Quaternion.Slerp(previous.CustomRotation, state.CustomRotation, customProgress));
            host.CustomSpace.localScale = Vector3.Lerp(previous.CustomScale, state.CustomScale, customProgress)
                * boardScale;
        }
        else if (host.CustomSpace != null)
        {
            Object.Destroy(host.CustomSpace.gameObject);
            host.CustomSpace = null;
        }
        ParticleSystem.MainModule main = host.Smoke.main;
        main.customSimulationSpace = host.CustomSpace;
    }

    private static float ParentScale(float world, float local) => Mathf.Abs(local) > 1e-20f ? world / local : 1f;

    private void Apply(Host host, CardPlumeState state, float sampleTime)
    {
        ParticleSystem smoke = host.Smoke;
        bool newEpisode = host.Episode != state.Episode;
        bool newSample = host.AppliedSample != sampleTime;
        float age = state.Age + Mathf.Max(0f, Time.unscaledTime - _receivedAt) * state.PlaybackRate;
        bool restart = newEpisode;
        ParticleSystem.MainModule main = smoke.main;
        if (newEpisode)
        {
            smoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            smoke.useAutoRandomSeed = false;
            smoke.randomSeed = state.RandomSeed;
        }
        else if (newSample)
        {
            // Correct the native duration-relative clock without clearing living particles.
            // A lower age is an ordinary loop wrap, not a new episode or random stream.
            smoke.time = state.Age;
            host.NativeAge = state.Age;
        }
        if (newEpisode || newSample)
        {
            // Item and child colours/gradients remain exactly authored. Only the ability root's
            // colour is overwritten at runtime by CardEffects.SpawnParticle.
            if (state.Source == CardPlumeState.CardSource
                && NetProtocol.HeldFaceList(state.FaceCode) != NetProtocol.HeldFaceListItems && state.EmitterIndex == 0)
                main.startColor = state.Color;
            main.simulationSpace = (ParticleSystemSimulationSpace)((state.Flags >> 4) & 3);
            main.scalingMode = (ParticleSystemScalingMode)((state.Flags >> 6) & 3);
            main.startSizeMultiplier = state.StartSizeMultiplier;
            main.startSpeedMultiplier = state.StartSpeedMultiplier;
            main.loop = (state.Flags & CardPlumeState.Looping) != 0;
        }
        // Native particle.time already includes the owner's simulation speed. Advance this copy
        // explicitly on that clock and keep automatic playback paused, so the viewer's local
        // pause/timeScale cannot freeze another player's plume between received snapshots.
        main.simulationSpeed = 1f;
        ParticleSystem.EmissionModule emission = smoke.emission;
        emission.enabled = host.AuthoredEmission && (newEpisode || (state.Flags & CardPlumeState.Emitting) != 0);
        smoke.Simulate(restart ? age : Mathf.Max(0f, age - host.NativeAge),
            withChildren: false, restart: restart, fixedTimeStep: false);
        emission.enabled = host.AuthoredEmission && (state.Flags & CardPlumeState.Emitting) != 0;
        smoke.Pause(false);
        host.NativeAge = age;
        host.Episode = state.Episode;
        host.AppliedSample = sampleTime;
        if (newEpisode && !_logged)
        {
            _logged = true;
            VRLog.Info("Net", $"Native card smoke [player {_owner.PlayerId}]: owner episode, colour, "
                + "seed, playback clock and board-relative pose applied to the original prefab.");
        }
    }

    private Host? Create(CardPlumeState state)
    {
        GameObject? bridge = null;
        bool retained = false;
        try
        {
            bridge = new GameObject("GloomhavenVR.RemoteCardPlumeFrame");
            bridge.SetActive(false);
            GameObject? root;
            if (state.Source == CardPlumeState.BonusTooltipSource
                || NetProtocol.HeldFaceList(state.FaceCode) == NetProtocol.HeldFaceListItems)
                root = CloneItemEmitter(state, bridge.transform);
            else
            {
                if (s_prefab == null) s_prefab = GlobalSettings.Instance.VisualEffects.CardSmoke;
                ParticleSystem[]? originals = s_prefab != null
                    ? s_prefab.GetComponentsInChildren<ParticleSystem>(true) : null;
                root = originals != null && state.EmitterIndex < originals.Length
                    ? Object.Instantiate(originals[state.EmitterIndex].gameObject, bridge.transform, false) : null;
            }
            if (root == null) return null;
            root.name = "GloomhavenVR.RemoteCardPlume";
            StripOtherEmitters(root);
            ParticleSystem smoke = root.GetComponent<ParticleSystem>();
            if (smoke == null)
            {
                Object.Destroy(root);
                return null;
            }

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            VRLayers.Apply(bridge);
            root.SetActive(true);
            bridge.SetActive(true);
            retained = true;
            return new Host { Root = bridge, Smoke = smoke, AuthoredEmission = smoke.emission.enabled };
        }
        catch (Exception e)
        {
            if (!_logged)
            {
                _logged = true;
                VRLog.Warn("Net", $"Native card smoke prefab unavailable: {e.GetType().Name}.");
            }
            return null;
        }
        finally
        {
            if (!retained && bridge != null) Object.Destroy(bridge);
        }
    }

    private static GameObject? CloneItemEmitter(CardPlumeState state, Transform inactiveParent)
    {
        CPlayerActor? actor = RemoteBoardFocus.ActorById(state.ActorId);
        CItem? item;
        if (state.Source == CardPlumeState.BonusTooltipSource)
        {
            CActiveBonus? bonus = actor != null ? UseBarSlotSymbol.ResolveBonusModel(actor, state.BonusIdentity, out _) : null;
            item = bonus?.Layout == null ? bonus?.BaseCard as CItem : null;
        }
        else
        {
            List<CItem>? items = actor?.Inventory?.AllItems;
            int seat = NetProtocol.HeldFaceIndex(state.FaceCode);
            item = items != null && items.Count == state.ListCount && seat < items.Count ? items[seat] : null;
        }
        if (item == null || ObjectPool.instance == null) return null;
        GameObject? holder = null;
        GameObject? card = null;
        try
        {
            holder = new GameObject("GloomhavenVR.ItemSmokeBorrow");
            holder.SetActive(false);
            holder.transform.SetParent(ObjectPool.instance.transform, false);
            card = ObjectPool.SpawnCard(item.ID, ObjectPool.ECardType.Item, holder.transform,
                resetLocalScale: true, resetToMiddle: true, resetLocalRotation: false, activate: false);
            if (card == null) return null;
            ParticleSystem[] systems = card.GetComponentsInChildren<ParticleSystem>(true);
            if (state.EmitterIndex >= systems.Length) return null;
            return Object.Instantiate(systems[state.EmitterIndex].gameObject, inactiveParent, false);
        }
        finally
        {
            try { if (card != null) RemoteItemCardSource.ReturnBorrowed(item.ID, card); }
            finally { if (holder != null) Object.Destroy(holder); }
        }
    }

    private static void StripOtherEmitters(GameObject clone)
    {
        // Each sampled ordinal owns exactly one emitter. Keeping its descendant systems would
        // duplicate their particles when their own entries render, even if this copy is a child
        // of the originally serialized particle subtree.
        // Construction stays beneath an inactive owned parent until all game/observer scripts
        // are gone. Only native particle modules and rendering components execute on this copy.
        MonoBehaviour[] controllers = clone.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < controllers.Length; i++) Object.DestroyImmediate(controllers[i]);
        Animator[] animators = clone.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++) Object.DestroyImmediate(animators[i]);
        Animation[] animations = clone.GetComponentsInChildren<Animation>(true);
        for (int i = 0; i < animations.Length; i++) Object.DestroyImmediate(animations[i]);
        ParticleSystem[] all = clone.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = all.Length - 1; i > 0; i--)
        {
            ParticleSystemRenderer renderer = all[i].GetComponent<ParticleSystemRenderer>();
            if (renderer != null) Object.DestroyImmediate(renderer);
            Object.DestroyImmediate(all[i]);
        }
    }

    private bool CanShow(CardPlumeState state)
    {
        CPlayerActor? actor = RemoteBoardFocus.ActorById(state.ActorId);
        if (actor == null) return false;
        if (state.Source == CardPlumeState.BonusTooltipSource)
        {
            int slot = state.BonusSlot;
            if (_owner.UseBarSlotIds == null || slot >= _owner.UseBarSlotIds.Length
                || !state.MatchesTooltipOwner(NetFigures.StableActorId(RemoteBoardFocus.DisplayedActor(_owner, out _)),
                    _owner.UseBarSlotIds[slot]) || _owner.UseBarSlotStates == null
                || slot >= _owner.UseBarSlotStates.Length) return false;
            CActiveBonus? bonus = UseBarSlotSymbol.ResolveBonusModel(actor, state.BonusIdentity, out _);
            if (bonus == null || bonus.Layout != null || bonus.BaseCard is not CItem) return false;
            UseBarWidgetState? descriptor = null;
            if (_owner.UseBarWidgetStates != null)
                foreach (UseBarWidgetState value in _owner.UseBarWidgetStates)
                    if (value.Slot == slot) { descriptor = value; break; }
            return RemoteUseBarTooltip.IsShown(_owner.UseBarSlotStates[slot], descriptor);
        }
        int list = NetProtocol.HeldFaceList(state.FaceCode);
        int seat = NetProtocol.HeldFaceIndex(state.FaceCode);
        CAbilityCard? card = null;
        try
        {
            if (list == NetProtocol.HeldFaceListActive)
            {
                int count = 0;
                List<CBaseCard>? active = actor.CharacterClass?.ActivatedCards;
                if (active == null) return false;
                for (int i = 0; i < active.Count; i++)
                {
                    if (active[i] is not CAbilityCard ability) continue;
                    if (count++ == seat) card = ability;
                }
                if (count != state.ListCount) return false;
            }
            else if (list == CardPlumeState.RoundList)
            {
                List<CAbilityCard>? round = actor.CharacterClass?.RoundAbilityCards;
                if (round == null || round.Count != state.ListCount) return false;
                card = round[seat];
            }
            else if (list == NetProtocol.HeldFaceListItems)
            {
                List<CItem>? items = actor.Inventory?.AllItems;
                return items != null && items.Count == state.ListCount && seat < items.Count
                    && items[seat] != null
                    && RevealGate.CardFaces(RevealGate.PeerCardPopulation.ItemCard, actor, scenarioEstablished: true)
                        != RevealGate.CardFaceSource.None;
            }
            else
            {
                CardsHandUI? hand = CardsHandManager.Instance?.GetHand(actor);
                if (hand == null) return false;
                _widgets.Clear();
                if (list == NetProtocol.HeldFaceListDiscard || list == NetProtocol.HeldFaceListBurnt)
                    CardsGameApi.GetPileArcWidgets(hand, list == NetProtocol.HeldFaceListBurnt, _widgets);
                else if (list == NetProtocol.HeldFaceListHand && hand.cardsUI != null)
                {
                    for (int i = 0; i < hand.cardsUI.Count; i++)
                        if (CardsGameApi.HandFanMember(hand.cardsUI[i], actor)) _widgets.Add(hand.cardsUI[i]);
                }
                if (_widgets.Count != state.ListCount || seat >= _widgets.Count) return false;
                card = _widgets[seat].AbilityCard;
            }
            if (card == null) return false;
            RevealGate.PeerCardPopulation population = list == NetProtocol.HeldFaceListActive
                || list == NetProtocol.HeldFaceListBurnt || list == NetProtocol.HeldFaceListDiscard
                ? RevealGate.PeerCardPopulation.AlreadyPublic : RevealGate.PeerCardPopulation.Selectable;
            return RevealGate.CardFaces(population, actor, card.CardInstanceID, scenarioEstablished: true, out _) != RevealGate.CardFaceSource.None;
        }
        catch (Exception) { return false; }
    }

    private static void DestroyHost(Host host)
    {
        if (host.Root != null) Object.Destroy(host.Root);
        if (host.CustomSpace != null) Object.Destroy(host.CustomSpace.gameObject);
    }

    internal void Destroy()
    {
        foreach (Host host in _hosts.Values) DestroyHost(host);
        _hosts.Clear();
        _lastStates = null;
    }
}
