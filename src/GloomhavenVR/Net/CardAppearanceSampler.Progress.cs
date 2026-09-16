using System.Collections.Generic;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;
namespace GloomhavenVR.Net;

internal static partial class CardAppearanceSampler
{
    private sealed class NativeBurnProgress
    {
        internal AbilityCardUI? Widget;
        internal FullAbilityCard? Full;
        internal CPlayerActor Actor = null!;
        internal CAbilityCard Card = null!;
        internal CardAppearanceState Identity = null!;
    }
    private static readonly List<NativeBurnProgress> NativeBurns = new(CardBurnCompletionHistory.CountMax);

    internal static void ObserveNativeBurnStart(CardEffects effects)
    {
        if (effects == null || !NetAvatarDriver.CanPublishNativePresentation) return;
        var full = effects.GetComponent<FullAbilityCard>();
        if (full == null) full = effects.GetComponentInParent<FullAbilityCard>();
        if (full == null || full.playerActor == null || full.AbilityCard == null) return;
        var widget = effects.GetComponentInParent<AbilityCardUI>();
        TrackBurnProgress(full.playerActor, full.AbilityCard, widget, full);
    }

    // Admission state comes from the actual controlled owner's native iterator/loss sequence.
    // This is deliberately independent of whether the incoming card already has a VR wrapper.
    internal static void ObserveBurnProgress(AbilityCardUI widget, FullAbilityCard? full = null)
    {
        if (!NetAvatarDriver.CanPublishNativePresentation) return;
        var actor = widget != null ? widget.PlayerActor : null;
        var card = widget != null ? widget.AbilityCard : null;
        if (actor == null || card == null) return;
        bool running = BurnArtwork.Playing(BurnArtwork.EffectsOf(full))
            || BurnArtwork.Playing(BurnArtwork.EffectsOf(widget)) || BurnArtwork.LosingCards(widget);
        if (running) TrackBurnProgress(actor, card, widget, full);
    }

    private static void TrackBurnProgress(CPlayerActor actor, CAbilityCard card, AbilityCardUI? widget, FullAbilityCard? full)
    {
        if (!CardsGameApi.ControlsActor(actor)) return;
        foreach (var tracked in NativeBurns)
            if (ReferenceEquals(tracked.Card, card)) { if (full != null) tracked.Full = full; return; }
        if (NativeBurns.Count >= CardBurnCompletionHistory.CountMax) return;
        var identity = new CardAppearanceState { ActorId = NetFigures.StableActorId(actor) };
        if (!CardAppearanceProvenance.Capture(identity, actor, card)) return;
        NativeBurns.Add(new NativeBurnProgress { Widget = widget, Full = full, Actor = actor, Card = card, Identity = identity });
        NetCardFx.NoteBurnProgress(identity, Time.unscaledTime, running: true);
    }

    private static void RefreshBurnProgress()
    {
        if (!NetAvatarDriver.CanPublishNativePresentation) { ClearBurnProgress(); return; }
        for (int i = NativeBurns.Count - 1; i >= 0; i--)
        {
            var tracked = NativeBurns[i];
            bool running = tracked.Actor != null && CardsGameApi.ControlsActor(tracked.Actor)
                && ReferenceEquals(CardAppearanceProvenance.Resolve(tracked.Identity), tracked.Card)
                && (BurnArtwork.Playing(BurnArtwork.EffectsOf(tracked.Full))
                    || BurnArtwork.Playing(BurnArtwork.EffectsOf(tracked.Widget)) || BurnArtwork.LosingCards(tracked.Widget));
            if (running) continue;
            NetCardFx.NoteBurnProgress(tracked.Identity, Time.unscaledTime, running: false);
            NativeBurns.RemoveAt(i);
        }
    }
    private static void ClearBurnProgress()
    {
        foreach (var tracked in NativeBurns) NetCardFx.NoteBurnProgress(tracked.Identity, Time.unscaledTime, running: false);
        NativeBurns.Clear();
    }
}
