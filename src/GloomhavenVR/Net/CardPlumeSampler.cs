using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Reads only live native smoke bound to actual VR cards, in the owner's board frame.</summary>
internal static class CardPlumeSampler
{
    private static readonly List<BurnCardFx> s_live = new();
    private static readonly List<CardPlumeState> s_states = new();
    private static readonly List<ItemsPile.ItemChip> s_items = new();
    private static readonly Dictionary<ParticleSystem, NativeSmokeActivation> s_activations = new();
    private static readonly HashSet<ParticleSystem> s_seenEmitters = new();
    private static readonly List<ParticleSystem> s_pruneEmitters = new();
    private static CardPlumeState[] s_previous = Array.Empty<CardPlumeState>();
    private static bool s_missingAddressLogged;

    internal static CardPlumeState[] Sample()
    {
        s_states.Clear();
        s_seenEmitters.Clear();
        Transform? board = PlayTray.Current?.Root;
        if (board != null)
        {
            BurnCardFx.CopyLive(s_live);
            for (int i = 0; i < s_live.Count; i++)
            {
                BurnCardFx binding = s_live[i];
                ParticleSystem? smoke = binding.LiveSmoke;
                VRCard? card = binding.OwnerCard;
                CPlayerActor? actor = card != null && card.GameCard != null
                    ? card.GameCard.PlayerActor : null;
                if (smoke == null || card == null || actor == null
                    || !card.gameObject.activeInHierarchy) continue;
                LocalRigSampler.NameCard(actor, card, out byte code, out byte count);
                if (code == 0 && card.GameCard?.AbilityCard != null)
                {
                    List<CAbilityCard>? round = actor.CharacterClass?.RoundAbilityCards;
                    int seat = round != null ? round.IndexOf(card.GameCard.AbilityCard) : -1;
                    if (seat >= 0 && seat < 32 && round!.Count <= 255)
                    {
                        code = (byte)((CardPlumeState.RoundList << NetProtocol.HeldFaceListShift) | seat);
                        count = (byte)round.Count;
                    }
                }
                if (code == 0)
                {
                    if (!s_missingAddressLogged)
                    {
                        s_missingAddressLogged = true;
                        VRLog.Warn("Net", "Native card smoke has no public positional address; frame omitted.");
                    }
                    continue;
                }
                ParticleSystem[] emitters = binding.NativeEmitters;
                for (int j = 0; j < emitters.Length; j++)
                {
                    ParticleSystem emitter = emitters[j];
                    if (emitter == null || !emitter.gameObject.activeInHierarchy
                        || (!emitter.isPlaying && !emitter.isPaused && !emitter.IsAlive(false))) continue;
                    if (j > byte.MaxValue) throw new InvalidOperationException("Native ability emitter index exceeds wire domain.");
                    Add(board, actor, code, count, (byte)j, emitter);
                }
            }
            ItemsPile.ItemChip.CopySmokeChips(s_items);
            for (int i = 0; i < s_items.Count; i++)
            {
                ItemsPile.ItemChip chip = s_items[i];
                CPlayerActor? actor = chip != null ? chip.SmokeActor : null;
                List<CItem>? items = actor?.Inventory?.AllItems;
                int seat = items != null && chip != null && chip.Item != null ? items.IndexOf(chip.Item) : -1;
                if (seat < 0 || seat >= NetProtocol.HeldFaceIndexUnknown || items!.Count > 255) continue;
                byte code = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListItems, seat);
                ParticleSystem[] emitters = chip!.NativeSmokeSystems;
                for (int j = 0; j < emitters.Length; j++)
                {
                    ParticleSystem smoke = emitters[j];
                    if (smoke == null || !smoke.gameObject.activeInHierarchy
                        || (!smoke.isPlaying && !smoke.isPaused && !smoke.IsAlive(false))) continue;
                    if (j > byte.MaxValue) throw new InvalidOperationException("Native item emitter index exceeds wire domain.");
                    Add(board, actor!, code, (byte)items.Count, (byte)j, smoke);
                }
            }
        }
        s_pruneEmitters.Clear();
        foreach (ParticleSystem smoke in s_activations.Keys)
            if (!s_seenEmitters.Contains(smoke)) s_pruneEmitters.Add(smoke);
        for (int i = 0; i < s_pruneEmitters.Count; i++)
        {
            NativeSmokeActivation activation = s_activations[s_pruneEmitters[i]];
            if (activation != null) UnityEngine.Object.Destroy(activation);
            s_activations.Remove(s_pruneEmitters[i]);
        }
        // Registry iteration order is not a wire identity. Stable ordering permits a true
        // unchanged snapshot to reuse its immutable array after all native reads have completed.
        s_states.Sort((a, b) => a.ActorId != b.ActorId ? a.ActorId.CompareTo(b.ActorId)
            : a.FaceCode != b.FaceCode ? a.FaceCode.CompareTo(b.FaceCode)
            : a.EmitterIndex.CompareTo(b.EmitterIndex));
        bool same = s_previous.Length == s_states.Count;
        for (int i = 0; same && i < s_states.Count; i++)
            same = CardPlumeState.Same(s_previous[i], s_states[i]);
        if (!same) s_previous = s_states.ToArray();
        return s_previous;
    }

    private static void Add(Transform board, CPlayerActor actor, byte code, byte count,
        byte emitter, ParticleSystem smoke)
    {
        ParticleSystem.MainModule main = smoke.main;
        Vector3 boardScale = board.lossyScale;
        Vector3 smokeScale = smoke.transform.lossyScale;
        if (Mathf.Abs(boardScale.x) < 1e-6f || Mathf.Abs(boardScale.y) < 1e-6f
            || Mathf.Abs(boardScale.z) < 1e-6f) return;
        s_seenEmitters.Add(smoke);
        if (!s_activations.TryGetValue(smoke, out NativeSmokeActivation? activation) || activation == null)
        {
            activation = smoke.gameObject.AddComponent<NativeSmokeActivation>();
            s_activations[smoke] = activation;
        }
        var state = new CardPlumeState
        {
            ActorId = NetFigures.StableActorId(actor), FaceCode = code, ListCount = count,
            EmitterIndex = emitter, Episode = activation.Episode, RandomSeed = smoke.randomSeed, Age = smoke.time,
            StartSizeMultiplier = main.startSizeMultiplier, StartSpeedMultiplier = main.startSpeedMultiplier,
            PlaybackRate = !smoke.isPaused && (smoke.isPlaying || smoke.IsAlive(false))
                ? main.simulationSpeed * (main.useUnscaledTime ? 1f : Time.timeScale) : 0f,
            Flags = (byte)((smoke.isPlaying ? CardPlumeState.Playing : 0)
                | (smoke.isPaused ? CardPlumeState.Paused : 0)
                | (smoke.isEmitting ? CardPlumeState.Emitting : 0)
                | (main.loop ? CardPlumeState.Looping : 0)
                | ((int)main.simulationSpace << 4) | ((int)main.scalingMode << 6)),
            Color = main.startColor.color,
            LocalPosition = board.InverseTransformPoint(smoke.transform.position),
            LocalRotation = Quaternion.Inverse(board.rotation) * smoke.transform.rotation,
            EmitterLocalScale = smoke.transform.localScale,
            LocalScale = new Vector3(smokeScale.x / boardScale.x,
                smokeScale.y / boardScale.y, smokeScale.z / boardScale.z),
        };
        Transform? custom = main.customSimulationSpace;
        if (main.simulationSpace == ParticleSystemSimulationSpace.Custom && custom != null)
        {
            state.CustomSpacePresent = true;
            state.CustomPosition = board.InverseTransformPoint(custom.position);
            state.CustomRotation = Quaternion.Inverse(board.rotation) * custom.rotation;
            Vector3 customScale = custom.lossyScale;
            state.CustomScale = new Vector3(customScale.x / boardScale.x,
                customScale.y / boardScale.y, customScale.z / boardScale.z);
        }
        if (!state.Validate())
            throw new InvalidOperationException("Native smoke exceeds the validated presentation frame domain.");
        s_states.Add(state);
    }

    internal static void Reset()
    {
        s_live.Clear();
        s_items.Clear();
        foreach (NativeSmokeActivation activation in s_activations.Values)
            if (activation != null) UnityEngine.Object.Destroy(activation);
        s_activations.Clear();
        s_seenEmitters.Clear();
        s_pruneEmitters.Clear();
        s_states.Clear();
        s_previous = Array.Empty<CardPlumeState>();
        s_missingAddressLogged = false;
    }
}

/// <summary>Tracks actual native activation, including pooled disable/re-enable between samples.
/// ParticleSystem.time wraps at duration; a loop is never a new activation or a reason to clear.</summary>
internal sealed class NativeSmokeActivation : MonoBehaviour
{
    internal uint Episode { get; private set; }
    private void OnEnable() => Episode = BurnCardFx.NextEpisode();
}
