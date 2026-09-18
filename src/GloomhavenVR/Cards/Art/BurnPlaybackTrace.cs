using System;
using Chronos;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>Bounded Debug evidence for the unresolved build-532 short-rest flash.
/// Observe the actual CanvasRenderer binding without asking materialForRendering to create or
/// update a stencil copy. This class never writes a material or changes native playback.</summary>
internal static class BurnPlaybackTrace
{
    private const int SessionLineLimit = 128, EpisodeLineLimit = 18;
    private static int s_lines;
    private sealed class Trace
    {
        internal float Started, LastEvent;
        internal int Lines;
        internal bool Sampled;
        internal Material? Base, Drawn, FlameBase, FlameDrawn;
        internal readonly HashSet<(string, CardEffects.FXTask?, bool?)> Events = new();
        internal float BaseBurn, DrawnBurn, DrawnGrey, FlameBaseAnim, FlameDrawnAnim;
    }
    private static readonly ConditionalWeakTable<CardEffects, Trace> Traces = new();

    internal static void Begin(CardEffects fx, bool follower)
    {
        if (!VRLog.WantsDebug || s_lines >= SessionLineLimit) return;
        var trace = Traces.GetValue(fx, _ => new Trace());
        trace.Started = trace.LastEvent = Time.unscaledTime;
        trace.Lines = 0;
        trace.Sampled = false;
        trace.Events.Clear();
        Event(fx, follower ? "begin-follower" : "begin-original");
    }

    internal static void Event(CardEffects fx, string transition, CardEffects.FXTask? effect = null, bool? active = null)
    {
        if (!VRLog.WantsDebug || s_lines >= SessionLineLimit
            || !Traces.TryGetValue(fx, out var trace) || trace.Lines >= EpisodeLineLimit) return;
        bool important = transition == "native-terminal-step" || transition.StartsWith("renderer-", StringComparison.Ordinal);
        if (!important && (trace.Lines >= EpisodeLineLimit - 6 || !trace.Events.Add((transition, effect, active)))) return;
        // Keep the completed tail observable, without tracing this card's unrelated later UI.
        if (Time.unscaledTime - trace.LastEvent > 3f && fx.coroutine == null && !important) return;
        try
        {
            trace.LastEvent = Time.unscaledTime;
            trace.Lines++;
            s_lines++;
            FullAbilityCard? full = fx.GetComponent<FullAbilityCard>() ?? fx.GetComponentInParent<FullAbilityCard>();
            var widget = CardFace.OwnerOf(full);
            var card = widget != null ? widget.AbilityCard : full?.AbilityCard;
            var actor = widget != null ? widget.PlayerActor : full?.playerActor;
            var cards = actor?.CharacterClass;
            var clock = Timekeeper.instance != null ? Timekeeper.instance.m_GlobalClock : null;
            Image? plate = fx._headerImage;
            Image? flame = fx._uiFxOverlay;
            VRLog.Info("Cards", $"BURN NATIVE TRACE: {transition}; effect={effect}, requested={active}, frame={Time.frameCount}, fx={fx.GetInstanceID()}, " +
                $"elapsed={Time.unscaledTime - trace.Started:F3}, card='{card?.Name ?? "<unresolved>"}', " +
                $"active={fx.gameObject.activeInHierarchy}, handle={fx.coroutine != null}, " +
                $"burn={fx.HasEffect(CardEffects.FXTask.BurnCard)}, lost={fx.HasEffect(CardEffects.FXTask.LostMode)}, " +
                $"ghost={fx.HasEffect(CardEffects.FXTask.DiscardMode)}, hand={card != null && cards?.HandAbilityCards.Contains(card) == true}, " +
                $"round={card != null && cards?.RoundAbilityCards.Contains(card) == true}, discard={card != null && cards?.DiscardedAbilityCards.Contains(card) == true}, " +
                $"lostPile={card != null && (cards?.LostAbilityCards.Contains(card) == true || cards?.PermanentlyLostAbilityCards.Contains(card) == true)}, " +
                $"clock={clock?.time:F3}/{clock?.deltaTime:F4}, raw={BurnArtwork.PaintProgress(fx):F3}; " +
                $"plate base[{Describe(plate != null ? plate.material : null)}] draw[{Describe(Drawn(plate))}]; " +
                $"flame base[{DescribeFlame(flame != null ? flame.material : null)}] draw[{DescribeFlame(Drawn(flame))}]. " +
                $"Bounded Debug evidence {s_lines}/{SessionLineLimit}; material bindings are not headset pixels.");
        }
        catch { /* Diagnostic observation must never affect a native callback. */ }
    }

    internal static void Sample(CardEffects? fx)
    {
        if (!VRLog.WantsDebug || s_lines >= SessionLineLimit || fx == null
            || !Traces.TryGetValue(fx, out var trace) || trace.Lines >= EpisodeLineLimit
            || Time.unscaledTime - trace.LastEvent > 3f && fx.coroutine == null) return;
        try
        {
            Image? plate = fx._headerImage;
            Material? source = plate != null ? plate.material : null;
            Material? drawn = Drawn(plate);
            Image? flame = fx._uiFxOverlay;
            Material? flameSource = flame != null ? flame.material : null;
            Material? flameDrawn = Drawn(flame);
            float flameBaseAnim = Read(flameSource, "_FXAnim"), flameDrawnAnim = Read(flameDrawn, "_FXAnim");
            float baseBurn = Read(source, "_Burn"), drawnBurn = Read(drawn, "_Burn"), drawnGrey = Read(drawn, "_GreyOut");
            bool changed = trace.Sampled && (!ReferenceEquals(source, trace.Base) || !ReferenceEquals(drawn, trace.Drawn)
                || !ReferenceEquals(flameSource, trace.FlameBase) || !ReferenceEquals(flameDrawn, trace.FlameDrawn));
            bool rewound = trace.Sampled && (baseBurn + .01f < trace.BaseBurn || drawnBurn + .01f < trace.DrawnBurn
                || drawnGrey + .01f < trace.DrawnGrey || flameBaseAnim + .01f < trace.FlameBaseAnim
                || flameDrawnAnim + .01f < trace.FlameDrawnAnim);
            if (changed || rewound) Event(fx, changed ? "renderer-binding-change" : "renderer-paint-rewind");
            trace.Base = source;
            trace.Drawn = drawn;
            trace.FlameBase = flameSource;
            trace.FlameDrawn = flameDrawn;
            trace.FlameBaseAnim = flameBaseAnim;
            trace.FlameDrawnAnim = flameDrawnAnim;
            trace.BaseBurn = baseBurn;
            trace.DrawnBurn = drawnBurn;
            trace.DrawnGrey = drawnGrey;
            trace.Sampled = true;
        }
        catch { /* A disappearing original is normal during hand teardown. */ }
    }

    private static Material? Drawn(Graphic? graphic) => graphic != null && graphic.canvasRenderer != null
        && graphic.canvasRenderer.materialCount > 0 ? graphic.canvasRenderer.GetMaterial() : null;
    private static float Read(Material? material, string property) => material != null && material.HasProperty(property)
        ? material.GetFloat(property) : float.NaN;
    private static string Describe(Material? material) => material == null ? "none"
        : $"id={material.GetInstanceID()}, grey={Read(material, "_GreyOut"):F3}, flow={Read(material, "_Flow"):F3}, dissolve={Read(material, "_Dissolve"):F3}, burn={Read(material, "_Burn"):F3}";
    private static string DescribeFlame(Material? material) => material == null ? "none"
        : $"id={material.GetInstanceID()}, anim={Read(material, "_FXAnim"):F3}, queue={material.renderQueue}";
}
