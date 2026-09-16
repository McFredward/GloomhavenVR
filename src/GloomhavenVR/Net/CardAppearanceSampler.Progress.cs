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
        internal bool CompletedWithoutFlight, ExpectedFlight;
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
            if (ReferenceEquals(tracked.Card, card))
            {
                if (full != null) tracked.Full = full;
                if (tracked.CompletedWithoutFlight)
                {
                    tracked.CompletedWithoutFlight = tracked.ExpectedFlight = false;
                    NetCardFx.NoteBurnProgress(tracked.Identity, Time.unscaledTime, running: true);
                }
                return;
            }
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
            bool valid = tracked.Actor != null && CardsGameApi.ControlsActor(tracked.Actor)
                && ReferenceEquals(CardAppearanceProvenance.Resolve(tracked.Identity), tracked.Card);
            if (!valid)
            { NetCardFx.ForgetBurnCompletion(tracked.Identity); NativeBurns.RemoveAt(i); continue; }
            if (tracked.CompletedWithoutFlight)
            {
                var cards = tracked.Actor!.CharacterClass;
                if (cards.HandAbilityCards.Contains(tracked.Card) || cards.RoundAbilityCards.Contains(tracked.Card)
                    || (!cards.LostAbilityCards.Contains(tracked.Card) && !cards.PermanentlyLostAbilityCards.Contains(tracked.Card)))
                { NetCardFx.ForgetBurnCompletion(tracked.Identity); NativeBurns.RemoveAt(i); }
                continue;
            }
            bool running = BurnArtwork.Playing(BurnArtwork.EffectsOf(tracked.Full))
                || BurnArtwork.Playing(BurnArtwork.EffectsOf(tracked.Widget)) || BurnArtwork.LosingCards(tracked.Widget);
            bool expected = CardsDriver.ExpectsBurnFlight(tracked.Card);
            tracked.ExpectedFlight |= expected;
            if (running || expected) continue;
            // A normal visible burn must finish through its real release, even when capture fails.
            // Offscreen native completion is durable so coalescing cannot erase its entire lifetime.
            if (!tracked.ExpectedFlight && NetCardFx.CompleteBurnWithoutFlight(tracked.Identity, Time.unscaledTime))
                tracked.CompletedWithoutFlight = true;
            else
            { NetCardFx.NoteBurnProgress(tracked.Identity, Time.unscaledTime, running: false); NativeBurns.RemoveAt(i); }
        }
    }
    private static void ClearBurnProgress()
    {
        foreach (var tracked in NativeBurns) NetCardFx.ForgetBurnCompletion(tracked.Identity);
        NativeBurns.Clear();
    }
}
