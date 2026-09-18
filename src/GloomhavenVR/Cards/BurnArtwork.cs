using Chronos;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// MB489: owner-side native completion. The full-card shader and owning-hand loss sequence
/// both finish before release; an actively running timeline has no wall-clock cutoff. Peers
/// consume the owner's addressed semantic release and original appearance output. The older
/// investigation below records why latched FX state and viewer-local handles were insufficient.
///
/// ONE COMPLETION SIGNAL FOR EVERY BURN, ON EVERY BOARD — the single place that answers "is the
/// game's burn artwork still on this card?", "has it ever been painted on this card?" and "paint
/// the settled end-state now".
///
/// <para>WHY IT EXISTS. The 2026-09-07 round asked for the burn sequence a THIRD time, verbatim:
/// "Bei der kurzen Rast wenn eine Karte verbrannt wurde, wurde nicht ausreichend gewartet bis die
/// Verbrennen Animation fertig ist. Erst dann soll die Flug-Animation starten und es weitergehen.
/// Das soll so synchron mit den anderen Spielern sein … Prüfe das nochmal bei allen
/// Verbrennen-Flows!" The enumeration that request asks for came back with SIX producers and THREE
/// different completion signals, which is why fixing one flow per round never converged:</para>
/// <list type="number">
///   <item><description><c>CardsDriver.TryTakeBurnFlightSlot</c> — the game's own running
///   <c>CardEffects.coroutine</c> ANDed with <c>HasEffect(BurnCard|LostMode)</c>, bounded by a
///   0.5 s start grace and a 3 s ceiling. Drives the short-rest sacrifice, the rebuild park sweep,
///   the burnt-pile watch and the card-back slab fallback.</description></item>
///   <item><description>NOTHING AT ALL — <c>CardsDriver.TryStartFlyToPile</c> (a played card whose
///   model fate is Lost/PermanentlyLost at turn clear) actively CLEARS the pending hold, and
///   <c>FlushBurnHolds</c> deliberately bypasses the gate on a hand switch. Both are real burns
///   under the standing ruling and neither waits.</description></item>
///   <item><description>THE OWNER'S RECESS OCCUPANCY — <c>RemoteBurnFx.Drive</c> held its mirrored
///   presentation for exactly as long as the mirrored recess kept DRAWING the card. That is a proxy
///   for the artwork, not the artwork, and the ModBuild 474 logs measure the difference on all three
///   burns of the session (see <see cref="Released"/>).</description></item>
/// </list>
///
/// <para>THIS TYPE IS THE FIRST SIGNAL, LIFTED OUT SO THERE IS ONE COPY. It holds the predicate and
/// both bounds, so the owner's hold and every mirror evaluate the SAME expression over the SAME
/// widget on the SAME machine — which is what makes "synchron mit den anderen Spielern" a property
/// of the code rather than of two dials that happen to agree.</para>
///
/// <para>THE MODEL IS LOCAL, WHICH IS WHY A MIRROR MAY ASK AT ALL. A peer's burnt card is not
/// replicated presentation: <c>CCharacterClass.LostAbilityCards</c> is host-replicated and this
/// client walks it already (<c>RemoteBurnFx.Watch</c>, <c>RemotePileFronts.Resolve</c>), and the
/// <c>AbilityCardUI</c> it names is a real widget on THIS machine with its own
/// <see cref="CardEffects"/>. No wire field is owed for any of this and none is added.</para>
///
/// <para>NOTHING HERE WRITES GAME STATE. <see cref="Playing"/>, <see cref="Latched"/> and
/// <see cref="SettledBurnPainted"/> are read-only. <see cref="TrySettleBurnLook"/> writes only
/// PRESENTATION, and it writes it by running the game's OWN
/// <c>CardEffects.BurnCardTimeline(burnAnim: false, …)</c> — the no-ramp arm the game itself uses
/// for the settled end state — rather than by rebuilding an approximation of it. It never touches
/// <c>toggledEffects</c>, never starts a coroutine, and the game's own <c>RestoreCard()</c> (which
/// resets <c>_GreyOut</c>/<c>_Flow</c>/<c>_Dissolve</c>/<c>_Burn</c>/<c>_FXAnim</c> to zero,
/// CardEffects.cs:466-505) undoes it in full if the card is ever recovered.</para>
/// </summary>
internal static class BurnArtwork
{
    /// <summary>
    /// How long a burn may wait for its artwork to START before the flight goes anyway, in seconds.
    ///
    /// <para>MIRRORED FROM <c>CardsDriver.BurnEffectStartGraceSeconds</c> and it must stay equal to
    /// it: the whole point of this type is that the owner's hold and every mirror release on one
    /// expression. The ModBuild 474 peer log measures it directly — the short-rest sacrifice
    /// 'ABILITY_CARD_SpareDagger' reported <c>BURN ANIM: … (effect not started yet)</c> and then
    /// <c>BURN HOLD: … waited 0,50s</c>, i.e. this constant WAS the entire wait for that flow.</para>
    /// </summary>
    internal const float StartGraceSeconds = 0.5f;

    /// <summary>
    /// Historical maximum-hold reference retained for legacy expiry/settle consumers. MB489 no
    /// longer treats it as permission to cut a running shader or owning-hand loss sequence.
    /// </summary>
    internal const float MaxHoldSeconds = 3f;

    /// <summary>Settled value of <c>_GreyOut</c> at the end of the game's burn timeline; anything at
    /// or above half of it is "this card has been painted", anything below is "the timeline never
    /// ran on this client". Read-only discriminator, see <see cref="SettledBurnPainted"/>.</summary>
    private const float PaintedGreyOut = 0.5f;

    /// <summary>
    /// The <c>_GreyOut</c> a COMPLETED burn ramp leaves behind, minus float slack.
    /// <c>BurnCardTimeline</c>'s animated arm drives <c>_GreyOut</c> to <c>Mathf.Clamp01(dTime)</c>
    /// and runs until <c>burnTime = 2f</c> seconds of global-clock time have passed
    /// (CardEffects.cs:511/571), and its no-ramp arm writes a literal 1 (:600). So a LATCHED burn
    /// whose handle is gone and whose paint sits below this needs settled native output.
    /// Cancellation is one possible cause; elapsed-clock termination can also precede the
    /// accumulated delta-time paint. <see cref="PaintProgress"/> excludes the retained spent
    /// display floor and cannot, by itself, prove what the player saw.
    /// </summary>
    internal const float FinishedGreyOut = 0.98f;

    private static readonly int GreyOutId = Shader.PropertyToID("_GreyOut");
    private static readonly int[] BurnStartChannels = { GreyOutId, Shader.PropertyToID("_Flow"), Shader.PropertyToID("_Dissolve") };

    private sealed class BurnStartRecord
    {
        internal readonly SpentBurnContinuity<ScenarioRuleLibrary.CAbilityCard> Continuity = new();
        internal float[] Channels = System.Array.Empty<float>(), RawChannels = System.Array.Empty<float>();
        internal ScenarioRuleLibrary.CAbilityCard? Card;
        internal float RawGrey = -1f, LastDrawnGrey = -1f;
        internal bool HasFloor;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CardEffects, BurnStartRecord> BurnStarts = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CardEffects, NativeBurnEnumerator> BurnTimelines = new();

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CardEffects, NativeBurnEpisode<ScenarioRuleLibrary.CAbilityCard>> BurnEpisodes = new();
    private sealed class ModelBurnHistory
    {
        internal NativeBurnEnumerator? Running;
        internal bool Completed, LeftRecoveryPile;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScenarioRuleLibrary.CAbilityCard, ModelBurnHistory> ModelBurns = new();

    [HarmonyPatch]
    internal static class AbilityCardUI_Init_HistoricalBurn_Patch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(AbilityCardUI)))
            {
                var parameters = method.GetParameters();
                if (method.Name == nameof(AbilityCardUI.Init) && parameters.Length > 1
                    && parameters[1].ParameterType == typeof(ScenarioRuleLibrary.CPlayerActor)) return method;
            }
            throw new System.MissingMethodException(nameof(AbilityCardUI), nameof(AbilityCardUI.Init));
        }

        private static void Prefix(ScenarioRuleLibrary.CAbilityCard abilityCard, ScenarioRuleLibrary.CPlayerActor playerActor)
        {
            try
            {
                // SpawnCards initializes every original widget as Hand, even when the model
                // was already lost before this hand existed. Its subsequent SetPile(Lost)
                // is reconstruction, not a fresh loss. An already running original episode
                // remains authoritative if another widget is initialized during it.
                if (abilityCard == null || !Durable(abilityCard, playerActor)) return;
                var history = ModelBurns.GetValue(abilityCard, _ => new());
                if (history.Running != null && !history.Running.Finished) return;
                history.Completed = history.LeftRecoveryPile = true;
            }
            catch { /* Historical presentation classification must not block native Init. */ }
        }
    }

    private static ModelBurnHistory? ReadHistory(ScenarioRuleLibrary.CAbilityCard card,
        ScenarioRuleLibrary.CPlayerActor? owner, bool resetting)
    {
        if (!ModelBurns.TryGetValue(card, out var history)) return null;
        if (Recovered(card, owner) && history.LeftRecoveryPile
            || resetting && history.Completed && !Durable(card, owner))
        { history.Running = null; ModelBurns.Remove(card); return null; }
        history.LeftRecoveryPile |= !Recovered(card, owner);
        return history;
    }

    private static FullAbilityCard? ResolveOwner(CardEffects fx, out AbilityCardUI? widget)
    {
        FullAbilityCard? full = fx.GetComponent<FullAbilityCard>();
        if (full == null) full = fx.GetComponentInParent<FullAbilityCard>();
        widget = CardFace.OwnerOf(full);
        if (widget == null && full != null)
        {
            AbilityCardUI? parent = full.GetComponentInParent<AbilityCardUI>();
            if (parent != null && ReferenceEquals(parent.fullAbilityCard, full)) widget = parent;
        }
        return full;
    }

    private static bool Recovered(ScenarioRuleLibrary.CAbilityCard card, ScenarioRuleLibrary.CPlayerActor? owner)
    {
        if (owner?.CharacterClass != null)
            return owner.CharacterClass.HandAbilityCards.Contains(card)
                || owner.CharacterClass.RoundAbilityCards.Contains(card);
        return card.CurrentCardPile == ScenarioRuleLibrary.CBaseCard.ECardPile.Hand
            || card.CurrentCardPile == ScenarioRuleLibrary.CBaseCard.ECardPile.Round;
    }

    private static bool Durable(ScenarioRuleLibrary.CAbilityCard card, ScenarioRuleLibrary.CPlayerActor? owner)
    {
        if (owner?.CharacterClass != null)
            return owner.CharacterClass.LostAbilityCards.Contains(card)
                || owner.CharacterClass.PermanentlyLostAbilityCards.Contains(card);
        return card.CurrentCardPile == ScenarioRuleLibrary.CBaseCard.ECardPile.Lost
            || card.CurrentCardPile == ScenarioRuleLibrary.CBaseCard.ECardPile.PermanentlyLost;
    }

    private static bool PreservePlayback(CardEffects fx, bool resetting)
    {
        FullAbilityCard? full = ResolveOwner(fx, out var widget);
        var card = widget != null ? widget.AbilityCard : full?.AbilityCard;
        if (card == null) { BurnEpisodes.Remove(fx); return false; }
        var owner = widget != null ? widget.PlayerActor : full?.playerActor;
        ReadHistory(card, owner, resetting);
        if (!BurnEpisodes.TryGetValue(fx, out var episode)) return false;
        if (BurnTimelines.TryGetValue(fx, out var timeline) && timeline.Finished)
            episode.CancelIfRunning();
        return episode.Preserve(card, Recovered(card, owner), Durable(card, owner), resetting);
    }

    /// <summary>Restore a genuinely recovered original even if native SetPile already cached Hand.
    /// Build 530: Spellweaver returned four Lost cards, but their original burn paint remained.
    /// SetPile only calls RestoreCard on a widget pile edge; an earlier protected reset can consume
    /// that edge before model recovery. Observe original membership before drawing or publishing,
    /// not just native reset requests. Initial Hand/Round action burns have no completed departure
    /// and keep their timeline. No card-specific ability names or game-state writes are involved.</summary>
    private sealed class RecoveryReset
    {
        internal ScenarioRuleLibrary.CAbilityCard? Card;
        internal bool FailureLogged;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CardEffects, RecoveryReset> RecoveryResets = new();

    internal static void ReconcileRecoveredAppearance(CardEffects? fx)
    {
        if (fx == null) return;
        try
        {
            FullAbilityCard? full = ResolveOwner(fx, out var widget);
            var card = widget != null ? widget.AbilityCard : full?.AbilityCard;
            var owner = widget != null ? widget.PlayerActor : full?.playerActor;
            // A stale Hand stamp also exists on real lost cards. Missing owner metadata is
            // a temporary binding gap, never sufficient evidence to erase their presentation.
            if (owner?.CharacterClass == null) return;
            RecoveryResets.TryGetValue(fx, out var pending);
            if (pending != null && pending.Card != null && !ReferenceEquals(pending.Card, card))
            { RecoveryResets.Remove(fx); pending = null; }
            if (card == null || !Recovered(card, owner) || Durable(card, owner)) return;
            bool departed = BurnEpisodes.TryGetValue(fx, out var episode) && episode.IsRecovered(card, recovered: true);
            departed |= ModelBurns.TryGetValue(card, out var history) && history.LeftRecoveryPile;
            departed |= pending != null && ReferenceEquals(pending.Card, card);
            if (!departed) return;
            pending ??= RecoveryResets.GetValue(fx, _ => new());
            pending.Card = card;
            // Retire before native RestoreCard: old callbacks cannot resurrect an episode or its
            // spent floor. Retain separate retry evidence until the entire native reset succeeds.
            RetireBurnPlayback(fx);
            ModelBurns.Remove(card);
            fx.RestoreCard();
            RecoveryResets.Remove(fx);
        }
        catch (System.Exception ex)
        {
            var pending = RecoveryResets.GetValue(fx, _ => new());
            if (!pending.FailureLogged)
                Core.VRLog.Warn("Cards", $"Recovered card artwork reset deferred: {ex.Message}; original presentation will retry.");
            pending.FailureLogged = true;
        }
    }

    /// <summary>Actual pool recycle and scene teardown retire ownership before native reset.</summary>
    internal static void RetireBurnPlayback(CardEffects? fx)
    {
        if (fx == null) return;
        BurnPlaybackTrace.Event(fx, "retire-original");
        if (BurnTimelines.TryGetValue(fx, out var playback))
        {
            FullAbilityCard? full = ResolveOwner(fx, out var widget);
            var card = widget != null ? widget.AbilityCard : full?.AbilityCard;
            if (card != null && ModelBurns.TryGetValue(card, out var history)
                && ReferenceEquals(history.Running, playback)) history.Running = null;
        }
        BurnEpisodes.Remove(fx);
        BurnTimelines.Remove(fx);
        BurnStarts.Remove(fx);
        Forget(fx);
    }

    // FinalizeShortRest starts BurnCard before ShortRestPlayer moves the model to Lost.
    // SetPile then requests LostMode, and RefreshPile requests its no-ramp arm. All three
    // native calls reset paint and stop the previous coroutine before starting another.
    // Preserve the first original iterator instead: the task names are aliases for one
    // presentation episode, not permission to replay it. No game model/callback is skipped.
    private static bool AllowEffect(CardEffects fx, bool active, CardEffects.FXTask effect)
    {
        bool burn = effect == CardEffects.FXTask.BurnCard || effect == CardEffects.FXTask.LostMode;
        if (!PreservePlayback(fx, resetting: !burn))
        {
            // Native short-rest hover asks for BurnCard(false): this is the fully burnt
            // no-ramp preview, not cancellation. Clicking then restores that fiery preview
            // to zero and starts the real two-second burn, which looks like a restart.
            // Keep the uncommitted discarded offer in its existing spent appearance. Only
            // the confirmation callback starts burn playback; redraw and gameplay stay native.
            if (!active && effect == CardEffects.FXTask.BurnCard && IsShortRestPreview(fx)) return false;
            return true;
        }
        if (burn && active) fx.toggledEffects.Add(effect);
        // Keep the original burn latch through RefreshPile(false) while it is playing.
        // The first real recovery/reset clears the complete native set normally.
        return false;
    }

    private static bool IsShortRestPreview(CardEffects fx)
    {
        FullAbilityCard? full = ResolveOwner(fx, out var widget);
        var card = widget != null ? widget.AbilityCard : full?.AbilityCard;
        var owner = widget != null ? widget.PlayerActor : full?.playerActor;
        var manager = CardsHandManager.Instance;
        if (card == null || owner?.CharacterClass == null || manager == null
            || !owner.CharacterClass.DiscardedAbilityCards.Contains(card)) return false;
        var hand = manager.GetHand(owner);
        return hand != null && ReferenceEquals(hand.ShortRestedCard, card);
    }

    [HarmonyPatch(typeof(CardEffects), nameof(CardEffects.ToggleEffect))]
    internal static class ToggleEffect_PreserveSpentStart_Patch
    {
        private static bool Prefix(CardEffects __instance, bool active, CardEffects.FXTask effect)
        {
            try
            {
                if (!AllowEffect(__instance, active, effect))
                { BurnPlaybackTrace.Event(__instance, "effect-held", effect, active); return false; }
                BurnPlaybackTrace.Event(__instance, "effect-allowed", effect, active);
                FullAbilityCard? full = ResolveOwner(__instance, out var widget);
                ClearRecoveredSpentBurnStart(__instance, full);
                if (!active || effect != CardEffects.FXTask.BurnCard && effect != CardEffects.FXTask.LostMode) return true;
                PreserveSpentBurnStart(__instance, widget != null ? widget.AbilityCard : full?.AbilityCard,
                    widget != null ? widget.PlayerActor : full?.playerActor, beforeReset: true);
            }
            catch (System.Exception ex)
            {
                Core.VRLog.Warn("Cards", $"Could not retain native spent burn start: {ex.Message}");
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(CardEffects), nameof(CardEffects.ToggleAdditiveEffect))]
    internal static class ToggleAdditiveEffect_PreservePlayback_Patch
    {
        private static bool Prefix(CardEffects __instance, bool active, CardEffects.FXTask effect)
        {
            try
            {
                bool allowed = AllowEffect(__instance, active, effect);
                BurnPlaybackTrace.Event(__instance, allowed ? "additive-effect-allowed" : "additive-effect-held", effect, active);
                return allowed;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(CardEffects), nameof(CardEffects.RestoreCard))]
    internal static class RestoreCard_PreservePlayback_Patch
    {
        private static bool Prefix(CardEffects __instance)
        {
            try
            {
                if (!PreservePlayback(__instance, resetting: true))
                { BurnPlaybackTrace.Event(__instance, "restore-allowed"); return true; }
                BurnPlaybackTrace.Event(__instance, "restore-held");
                if (BurnEpisodes.TryGetValue(__instance, out var episode)) episode.RequestRestore();
                return false;
            }
            catch { return true; }
        }
    }

    private static void ClearRecoveredSpentBurnStart(CardEffects fx, FullAbilityCard? full)
    {
        ResolveOwner(fx, out var widget);
        var card = widget != null ? widget.AbilityCard : full?.AbilityCard;
        var cards = (widget != null ? widget.PlayerActor : full?.playerActor)?.CharacterClass;
        if (card == null || cards == null || cards.HandAbilityCards.Contains(card) || cards.RoundAbilityCards.Contains(card)
            || BurnStarts.TryGetValue(fx, out var record) && !ReferenceEquals(record.Card, card))
            BurnStarts.Remove(fx);
    }

    /// <summary>Retire unused history on detach, without cutting an original ongoing burn.</summary>
    internal static void ReleaseSpentBurnStart(CardEffects? fx)
    {
        if (fx == null) return;
        FullAbilityCard? full = fx.GetComponent<FullAbilityCard>();
        if (full == null) full = fx.GetComponentInParent<FullAbilityCard>();
        ClearRecoveredSpentBurnStart(fx, full);
        // A card can outlive its VR wrapper while the native timeline continues. Keep its raw
        // progress and floor until completion; a recycled idle source has no such ownership.
        if (!Playing(fx)) BurnStarts.Remove(fx);
    }

    [HarmonyPatch(typeof(CardEffects), nameof(CardEffects.BurnCardTimeline))]
    internal static class BurnCardTimeline_PreserveSpentStart_Patch
    {
        private static System.Collections.IEnumerator FollowOriginal(ModelBurnHistory history,
            NativeBurnEnumerator primary, CardEffects fx, ScenarioRuleLibrary.CAbilityCard card,
            ScenarioRuleLibrary.CPlayerActor? owner, System.Func<bool> stillOwned)
        {
            // The driver's layout barrier keeps the original visible until its artwork ends.
            // A native replacement widget may nevertheless be constructed in the background.
            // Join that original clock; never advance it or start a competing second ramp.
            while (ReferenceEquals(history.Running, primary) && !primary.Finished)
            {
                if (!FollowerIsCurrent(history, fx, card, owner, stillOwned)) yield break;
                yield return new WaitForEndOfFrame();
            }
            if (!FollowerIsCurrent(history, fx, card, owner, stillOwned)) yield break;
            var settle = fx.BurnCardTimeline(burnAnim: false, playOnDisabled: true);
            try { while (settle.MoveNext()) yield return settle.Current; }
            finally { (settle as System.IDisposable)?.Dispose(); }
            if (SettledBurnPainted(fx, out float grey) && grey >= FinishedGreyOut)
            {
                history.Completed = true;
                var episode = BurnEpisodes.GetValue(fx, _ => new());
                episode.Observe(card, Recovered(card, owner), running: true);
                episode.Observe(card, Recovered(card, owner), running: false);
            }
        }

        private static bool FollowerIsCurrent(ModelBurnHistory history, CardEffects fx,
            ScenarioRuleLibrary.CAbilityCard card, ScenarioRuleLibrary.CPlayerActor? owner, System.Func<bool> stillOwned)
        {
            if (!stillOwned()) return false;
            FullAbilityCard? full = ResolveOwner(fx, out var widget);
            var currentCard = widget != null ? widget.AbilityCard : full?.AbilityCard;
            return ReferenceEquals(currentCard, card) && ReferenceEquals(ReadHistory(card, owner, resetting: false), history);
        }

        private static void Prefix(CardEffects __instance, bool burnAnim, ref bool playOnDisabled)
        {
            if (!burnAnim || playOnDisabled) return;
            try
            {
                // Build 533 Debug capture: FinalizeShortRest starts the ORIGINAL burn while
                // its full face is temporarily inactive (frame 2841). Native playback bails
                // synchronously, so neither an episode nor completion is recorded. The VR
                // face returns next frame; a no-ramp settle then precedes SetPile(Lost)'s
                // second animated burn, visibly resetting the already burning card.
                // Choreographer owns this coroutine, not the disabled face. Let the game's
                // original iterator paint through that hierarchy transition, using its own
                // existing playOnDisabled path. Do not activate hidden UI or run clone FX.
                FullAbilityCard? full = ResolveOwner(__instance, out var widget);
                if (full != null && widget != null && ReferenceEquals(widget.fullAbilityCard, full)
                    && widget.AbilityCard != null) playOnDisabled = true;
            }
            catch { /* An unresolved original keeps the game's normal playback behavior. */ }
        }

        private static void Postfix(CardEffects __instance, bool burnAnim, ref System.Collections.IEnumerator __result)
        {
            try { __result = Wrap(__instance, burnAnim, __result); }
            catch { /* Keep the original iterator if presentation interception cannot be built. */ }
        }

        private static System.Collections.IEnumerator Wrap(CardEffects __instance, bool burnAnim,
            System.Collections.IEnumerator original)
        {
            FullAbilityCard? initialFull = ResolveOwner(__instance, out var initialWidget);
            var initialCard = initialWidget != null ? initialWidget.AbilityCard : initialFull?.AbilityCard;
            var initialOwner = initialWidget != null ? initialWidget.PlayerActor : initialFull?.playerActor;
            ModelBurnHistory? initialHistory = initialCard != null
                ? ReadHistory(initialCard, initialOwner, resetting: false) : null;
            if (burnAnim && initialCard != null && Durable(initialCard, initialOwner)
                && initialHistory?.Completed == true)
            {
                // Use the original no-ramp output on a recreated historical card; never
                // manufacture another flame sequence just because the UI object changed.
                return __instance.BurnCardTimeline(burnAnim: false, playOnDisabled: true);
            }
            bool followsOriginal = burnAnim && initialCard != null && initialHistory?.Running != null
                && !initialHistory.Running.Finished;
            NativeBurnEnumerator? playback = null;
            System.Collections.IEnumerator native = original;
            if (followsOriginal)
                native = FollowOriginal(initialHistory!, initialHistory!.Running!, __instance, initialCard!, initialOwner,
                    () => BurnTimelines.TryGetValue(__instance, out var current) && ReferenceEquals(current, playback));
            bool firstStep = true;
            playback = new NativeBurnEnumerator(native,
                () =>
                {
                    if (firstStep)
                    {
                        // A new genuine action supersedes an earlier failed recovery reset. Its
                        // early Hand/Round phase must not be erased by that obsolete retry latch.
                        if (burnAnim) BurnPlaybackTrace.Begin(__instance, followsOriginal);
                        if (burnAnim && !followsOriginal) RecoveryResets.Remove(__instance);
                        BurnTimelines.Remove(__instance);
                        BurnTimelines.Add(__instance, playback!);
                    }
                    if (BurnTimelines.TryGetValue(__instance, out var current)
                        && ReferenceEquals(current, playback)) RestoreNativeBurnChannels(__instance);
                },
                running =>
                {
                    // A retired/recycled face may still have an old enumerator disposed by
                    // Unity. Only the currently owned iterator may publish this episode.
                    if (!BurnTimelines.TryGetValue(__instance, out var current)
                        || !ReferenceEquals(current, playback)) return;
                    if (firstStep)
                    {
                        firstStep = false;
                        if (burnAnim && running && !followsOriginal) Net.CardAppearanceSampler.ObserveNativeBurnStart(__instance);
                        BurnPlaybackTrace.Event(__instance, burnAnim ? "first-native-step" : "native-settle");
                    }
                    if (burnAnim && !running) BurnPlaybackTrace.Event(__instance, "native-terminal-step");
                    // A native no-ramp settle is already authoritative completion. It must not
                    // inherit an earlier cosmetic floor's raw in-progress reading.
                    if (!burnAnim)
                    {
                        if (BurnStarts.TryGetValue(__instance, out var settled)) settled.HasFloor = false;
                        if (initialCard != null && ReadHistory(initialCard, initialOwner, resetting: false)?.Completed == true
                            && SettledBurnPainted(__instance, out float grey) && grey >= FinishedGreyOut)
                        {
                            var episode = BurnEpisodes.GetValue(__instance, _ => new());
                            episode.Observe(initialCard, Recovered(initialCard, initialOwner), running: true);
                            episode.Observe(initialCard, Recovered(initialCard, initialOwner), running: false);
                        }
                        return;
                    }
                    FullAbilityCard? full = ResolveOwner(__instance, out var widget);
                    var card = widget != null ? widget.AbilityCard : full?.AbilityCard;
                    if (card != null)
                    {
                        var owner = widget != null ? widget.PlayerActor : full?.playerActor;
                        var episode = BurnEpisodes.GetValue(__instance, _ => new());
                        episode.Observe(card, Recovered(card, owner), running);
                        if (followsOriginal) return;
                        var history = ModelBurns.GetValue(card, _ => new());
                        history.LeftRecoveryPile |= !Recovered(card, owner);
                        if (running) history.Running = playback;
                        else if (ReferenceEquals(history.Running, playback))
                        { history.Completed = true; history.Running = null; }
                        // SetPile(Activated) asks once, while the old card is still burning.
                        // Honor that native reset after the final step, never halfway through.
                        if (!running && episode.TakeDeferredRestore(Durable(card, owner))) __instance.RestoreCard();
                    }
                    // The native iterator's terminal step only clears its handle; it does not
                    // write final shader values. RestoreNativeBurnChannels removed our spent
                    // floor before this step, so treating MoveNext(false) as idle exposes the
                    // raw (possibly still near-zero after a clock jump) card for a frame. Keep
                    // the same spent floor through completion without restarting the artwork.
                    // A legitimate deferred activation reset clears the latch and stays clean.
                    PreserveSpentBurnStart(__instance, card, widget != null ? widget.PlayerActor : full?.playerActor,
                        beforeReset: false, nativeStep: running || Latched(__instance));
                },
                ex => Core.VRLog.Warn("Cards", $"Could not preserve native burn step: {ex.Message}"));
            return playback;
        }
    }
    private static void RestoreNativeBurnChannels(CardEffects fx)
    {
        if (!BurnStarts.TryGetValue(fx, out var record) || !record.HasFloor || fx.imgComp == null) return;
        var images = fx.imgComp;
        if (record.RawChannels.Length != images.Length * BurnStartChannels.Length) return;
        for (int image = 0; image < images.Length; image++)
            for (int property = 0; property < BurnStartChannels.Length; property++)
            {
                Material? material = images[image] != null ? images[image].material : null;
                float value = record.RawChannels[image * BurnStartChannels.Length + property];
                if (!float.IsNaN(value) && material != null && material.HasProperty(BurnStartChannels[property]))
                    material.SetFloat(BurnStartChannels[property], value);
            }
        record.HasFloor = false;
    }

    /// <summary>Keep the same model's actual spent wash through the native reset and ramp.</summary>
    internal static void PreserveSpentBurnStart(CardEffects? fx, AbilityCardUI? widget)
        => PreserveSpentBurnStart(fx, widget != null ? widget.AbilityCard : null,
            widget != null ? widget.PlayerActor : null, beforeReset: false);

    private static void PreserveSpentBurnStart(CardEffects? fx, ScenarioRuleLibrary.CAbilityCard? card,
        ScenarioRuleLibrary.CPlayerActor? owner, bool beforeReset, bool nativeStep = false)
    {
        if (fx == null) return;
        if (card == null || owner?.CharacterClass == null || fx.imgComp == null)
        { BurnStarts.Remove(fx); return; }
        bool spent = owner.CharacterClass.DiscardedAbilityCards.Contains(card);
        bool active = owner.CharacterClass.ActivatedCards.Contains(card);
        bool lost = owner.CharacterClass.LostAbilityCards.Contains(card)
            || owner.CharacterClass.PermanentlyLostAbilityCards.Contains(card);
        bool activeBurn = active && (beforeReset || nativeStep || fx.coroutine != null && Latched(fx));
        if (!spent && !lost && !activeBurn) { BurnStarts.Remove(fx); return; }
        var record = BurnStarts.GetValue(fx, _ => new BurnStartRecord());
        if (!ReferenceEquals(record.Card, card))
        {
            record.Continuity.Clear(); record.HasFloor = false; record.Card = card;
        }
        // Activated cards may legitimately return to blue. Only their actual immediate
        // pre-burn output is retained; historical max-wash belongs exclusively to discard.
        if (beforeReset && active && !spent) record.Continuity.Clear();
        bool burning = !beforeReset && (nativeStep || (spent || lost || activeBurn) && fx.gameObject.activeInHierarchy
            && fx.coroutine != null && Latched(fx));
        Image[] images = fx.imgComp;
        if (record.Channels.Length != images.Length * BurnStartChannels.Length)
        {
            record.Channels = new float[images.Length * BurnStartChannels.Length];
            record.RawChannels = new float[record.Channels.Length];
        }
        float grey = -1f;
        for (int image = 0; image < images.Length; image++)
            for (int property = 0; property < BurnStartChannels.Length; property++)
            {
                Material? material = images[image] != null ? images[image].material : null;
                float value = material != null && material.HasProperty(BurnStartChannels[property])
                    ? material.GetFloat(BurnStartChannels[property]) : float.NaN;
                record.Channels[image * BurnStartChannels.Length + property] = value;
                if (property == 0 && value > grey) grey = value;
            }
        // Never let the cosmetic floor masquerade as native completion or hide a bailed handle.
        // Repeated LateUpdate/sampler reads of our own write retain the last native observation.
        // The iterator wrapper restores raw channels before every native step, so a native final
        // jump to exactly the floor is still observed, including after a coarse/paused frame.
        if (!record.HasFloor || grey != record.LastDrawnGrey)
        {
            record.RawGrey = grey;
            System.Array.Copy(record.Channels, record.RawChannels, record.Channels.Length);
        }
        if (beforeReset) { record.RawGrey = 0f; record.HasFloor = false; }
        record.Continuity.Apply(card, spent || beforeReset, burning, record.Channels);
        if (!burning) return;
        record.LastDrawnGrey = -1f;
        for (int image = 0; image < images.Length; image++)
            for (int property = 0; property < BurnStartChannels.Length; property++)
            {
                float value = record.Channels[image * BurnStartChannels.Length + property];
                Material? material = images[image] != null ? images[image].material : null;
                if (!float.IsNaN(value) && material != null && material.HasProperty(BurnStartChannels[property])
                    && material.GetFloat(BurnStartChannels[property]) < value)
                { material.SetFloat(BurnStartChannels[property], value); record.HasFloor = true; }
                if (property == 0 && value > record.LastDrawnGrey) record.LastDrawnGrey = value;
            }
    }


    /// <summary>
    /// The <see cref="CardEffects"/> that paints one pile/hand widget, or null. THE ONE PLACE that
    /// knows the hop — <c>AbilityCardUI</c> does not own the component, its
    /// <c>fullAbilityCard</c> does, and every caller here starts from the widget because that is
    /// what <c>CardsGameApi.GetPileWidgets</c> hands back.
    /// </summary>
    internal static CardEffects? EffectsOf(AbilityCardUI? widget)
    {
        FullAbilityCard? full = widget != null ? widget.fullAbilityCard : null;
        return EffectsOf(full);
    }

    /// <summary>The <see cref="CardEffects"/> that paints one adopted card face, or null.</summary>
    internal static CardEffects? EffectsOf(FullAbilityCard? full) => full != null ? full.cardEffects : null;

    /// <summary>
    /// TRUE while the game is PLAYING its own burn/lost timeline on this widget — the picture, not
    /// the state.
    ///
    /// <para>BOTH TERMS ARE LOAD-BEARING and the reason is recorded in
    /// <c>CardsDriver.BurnArtworkActive</c>, whose body this is: <c>HasEffect</c> alone is
    /// <c>toggledEffects.Contains(task)</c> over a <see cref="System.Collections.Generic.HashSet{T}"/>
    /// that never clears for a LOST card, so it is a latched display state with no end and it made
    /// every burn on ModBuild 447 run the full ceiling; <c>coroutine</c> alone is also the GHOST
    /// (discard) timeline's handle, so the state test is what says the running timeline is a burn.
    /// </para>
    ///
    /// <para><b>THERE IS A THIRD TERM, AND IT COSTS EVERY BURN ON EVERY BOARD WITHOUT IT
    /// (2026-09-07 review, D2).</b> <c>CardEffects.BurnCard</c> is
    /// <c>coroutine = StartCoroutine(BurnCardTimeline(...))</c> (CardEffects.cs:450), and Unity
    /// runs a coroutine body up to its first <c>yield</c> SYNCHRONOUSLY inside
    /// <c>StartCoroutine</c>. The timeline's very first statement is
    /// <c>if (!gameObject.activeInHierarchy &amp;&amp; !playOnDisabled) { coroutine = null; yield
    /// break; }</c> (CardEffects.cs:508-513) — so on a bail the field is nulled and then the OUTER
    /// assignment writes the finished handle straight back over the null. <c>coroutine</c> is
    /// non-null for ever, and both terms above read TRUE for ever.</para>
    ///
    /// <para><b>AND IT IS THE COMMON CASE, NOT A RARITY.</b> In VR the game's 2D hand is
    /// <c>SetActive(false)</c> for most of a session (<c>CardsHandUI.Hide</c> →
    /// <c>ShowOrHideInternal(false)</c>, CardsHandUI.cs:514) and <c>CardsHandManager.SwitchHand</c>
    /// deactivates every hand that is not the presented one (CardsHandManager.cs:635). Any burn
    /// latched on a hand that is not the presented one therefore takes the trap, which in a party
    /// is most of them — and because <c>RemoteBurnFx.Drive</c> evaluates THIS method over the
    /// owner's widget, every mirror inherits the same stuck reading for ever. The cost is the 3 s
    /// <see cref="MaxHoldSeconds"/> ceiling per burn (~+1 s of flight delay plus a 3 s wanted-slot
    /// suppression) and a <c>BURN HOLD</c> line printing its own pre-registered INERT reading.</para>
    ///
    /// <para><b>THE DISCRIMINATOR IS THE PAINT, NOT THE HANDLE.</b> A genuinely running ramp writes
    /// <c>_GreyOut = Mathf.Clamp01(dTime)</c> on its FIRST loop iteration, which executes before the
    /// timeline's first <c>yield</c> and therefore before <c>coroutine</c> is even assigned
    /// (CardEffects.cs:565-571). A BAILED timeline writes nothing at all. So a handle that is
    /// non-null while <see cref="PaintProgress"/> still reads a hard zero, after the same
    /// <see cref="StartGraceSeconds"/> the release expression already grants a burn to start, is a
    /// bailed timeline and not a running one. A reading of -1 is UNKNOWN (the widget was never
    /// <c>Initialize</c>d, or the game changed shape) and is never treated as unpainted — the
    /// answer then stays "playing", i.e. the behaviour that shipped.</para>
    /// </summary>
    internal static bool Playing(CardEffects? fx)
    {
        if (fx == null)
            return false;
        try
        {
            bool burning = fx.HasEffect(CardEffects.FXTask.BurnCard)
                           || fx.HasEffect(CardEffects.FXTask.LostMode);
            if (!burning || fx.coroutine == null)
            {
                Forget(fx);
                return false;
            }
            // StartCoroutine can return a non-null handle after a synchronous native bail or
            // settle. Observe the actual iterator even when pre-existing spent paint is nonzero;
            // neither the stale handle nor the shader's latched end state proves it is running.
            if (BurnTimelines.TryGetValue(fx, out var playback) && playback.Started)
                return !playback.Finished;
            return !HandleIsABailedTimeline(fx);
        }
        catch
        {
            return false; // a game-side shape change must never strand a card on a board
        }
    }

    /// <summary>
    /// A paint reading at or below this is "the timeline never touched this widget". The ramp's
    /// first iteration already writes <c>deltaTime / 2f</c>, so any running burn clears it within a
    /// frame; the slack is float noise only.
    /// </summary>
    private const float BailedPaintCeiling = 0.001f;

    /// <summary>
    /// First native-clock time each <see cref="CardEffects"/> was OBSERVED holding a burn handle, so the
    /// <see cref="StartGraceSeconds"/> below is measured rather than assumed. Entries are dropped
    /// the moment the widget stops holding one (<see cref="Forget"/>), so the table's size tracks
    /// the burns in flight — a party's worth of card widgets at the very most.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<int, float> HandleFirstSeen = new();

    /// <summary>Hard cap on <see cref="HandleFirstSeen"/>. Reaching it means an id is leaking; the
    /// whole table is dropped rather than grown, which costs at most one extra grace per burn.</summary>
    private const int HandleTableCap = 256;

    /// <summary>
    /// Is this widget's non-null <c>CardEffects.coroutine</c> the corpse of a timeline that BAILED
    /// on an inactive object? See <see cref="Playing"/> for the mechanism and the two source lines.
    /// FALSE for everything it cannot establish, which is the reading that shipped.
    /// </summary>
    private static bool HandleIsABailedTimeline(CardEffects fx)
    {
        int id = fx.GetInstanceID();
        if (Timekeeper.instance == null) return false;
        // The native burn advances on this clock. A paused game must not turn its zero-progress
        // but live coroutine into a supposed bail merely because wall time passed.
        float now = Timekeeper.instance.m_GlobalClock.time;
        if (!HandleFirstSeen.TryGetValue(id, out float since))
        {
            if (HandleFirstSeen.Count >= HandleTableCap)
                HandleFirstSeen.Clear();
            HandleFirstSeen[id] = now;
            return false; // first sight — it has not had its grace yet
        }
        if (now - since < StartGraceSeconds)
            return false;
        float paint = PaintProgress(fx);
        return paint >= 0f && paint <= BailedPaintCeiling;
    }

    /// <summary>Drop a widget's start stamp once it no longer holds a burn handle.</summary>
    private static void Forget(CardEffects fx)
    {
        try
        {
            HandleFirstSeen.Remove(fx.GetInstanceID());
        }
        catch
        {
            // a widget mid-teardown owns no stamp worth chasing
        }
    }

    /// <summary>
    /// TRUE when the game has LATCHED this widget as burnt/lost, whether or not it ever painted it.
    ///
    /// <para>THIS IS THE HALF THAT SURVIVES A BAIL, and that is the whole of report item 10.
    /// <c>ToggleEffect</c> adds to <c>toggledEffects</c> and only THEN starts
    /// <c>BurnCardTimeline</c>, whose very first statement is
    /// <c>if (!gameObject.activeInHierarchy &amp;&amp; !playOnDisabled) { coroutine = null; yield
    /// break; }</c> (CardEffects.cs:508-513). A card burned while this client's 2D card UI was
    /// presenting a DIFFERENT character therefore comes out latched and unpainted, and nothing ever
    /// paints it afterwards.</para>
    /// </summary>
    internal static bool Latched(CardEffects? fx)
    {
        if (fx == null)
            return false;
        try
        {
            return fx.HasEffect(CardEffects.FXTask.BurnCard)
                   || fx.HasEffect(CardEffects.FXTask.LostMode);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Has the game's burn timeline actually PAINTED this widget? Reads <c>_GreyOut</c> off the
    /// widget's own face images — the one property both arms of <c>BurnCardTimeline</c> write and
    /// <c>RestoreCard</c> clears.
    /// </summary>
    /// <param name="greyOut">The value read, or -1 when no image could be read at all (the widget
    /// was never <c>Initialize</c>d, or the game changed shape). -1 is UNKNOWN and must never be
    /// treated as unpainted.</param>
    /// <returns>True only for a positive reading at or above <see cref="PaintedGreyOut"/>.</returns>
    internal static bool SettledBurnPainted(CardEffects? fx, out float greyOut)
    {
        greyOut = -1f;
        if (fx == null)
            return false;
        try
        {
            Image[]? imgs = fx.imgComp;
            if (imgs == null)
                return false;
            for (int i = 0; i < imgs.Length; i++)
            {
                Image? img = imgs[i];
                Material? mat = img != null ? img.material : null;
                if (mat == null || !mat.HasProperty(GreyOutId))
                    continue;
                float v = mat.GetFloat(GreyOutId);
                if (v > greyOut)
                    greyOut = v;
            }
        }
        catch
        {
            greyOut = -1f;
            return false;
        }
        return greyOut >= PaintedGreyOut;
    }

    /// <summary>
    /// HOW FAR THE BURN RAMP ACTUALLY GOT ON THIS WIDGET, 0..1 — or -1 when nothing could be read.
    ///
    /// <para>THE FIELD THAT SEPARATES "FINISHED" FROM "CANCELLED", which no handle can.
    /// <c>CardEffects.coroutine</c> is null after the timeline's last statement (CardEffects.cs:618)
    /// AND after every <c>RestoreCard()</c> / <c>ToggleAdditiveEffect</c>, which stop it and null it
    /// (:469-472, :404-407). Two histories, one reading — and this project's own logs report the
    /// wrong one: ModBuild 476's peer log 60504 says <c>BURN HOLD: 'ABILITY_CARD_ProvokingRoar'
    /// waited 0,69s … (released by: ARTWORK END — the game's own BurnCardTimeline handle went
    /// null)</c> about a ramp whose duration is a hard-coded 2 s. A completed ramp cannot be 0.69 s
    /// long, so that line names an END it cannot observe. <c>_GreyOut</c> can: it IS the ramp's
    /// progress variable.</para>
    /// </summary>
    internal static float PaintProgress(CardEffects? fx)
    {
        SettledBurnPainted(fx, out float greyOut);
        if (fx != null && BurnStarts.TryGetValue(fx, out var record) && record.HasFloor
            && greyOut == record.LastDrawnGrey) return record.RawGrey;
        return greyOut;
    }

    /// <summary>
    /// Paint the SETTLED burn look on a widget the game latched but never painted, by running the
    /// game's own timeline in its no-ramp arm. Returns true when the paint ran.
    ///
    /// <para>THE REAL THING, NOT AN APPROXIMATION — the standing ruling. <c>BurnCardTimeline</c>
    /// takes <c>burnAnim</c>; with it FALSE the method writes the full static burn block
    /// (<c>_Burn</c>, <c>_Burn_ColourTint</c>, <c>_Flow_Offset</c>, <c>_Flow_Speed</c>, the noise
    /// tiling, the dissolve gradient and the whole <c>fgFx</c> overlay), then the end state
    /// (<c>_GreyOut</c>=1, <c>_Flow</c>=1, <c>_Dissolve</c>=<c>mc_Dissolve</c>, <c>_FXAnim</c>=0.5,
    /// texts to mid-grey) and returns — WITHOUT A SINGLE <c>yield</c> (CardEffects.cs:592-616). So
    /// draining the enumerator here runs the whole of it in this call, on this frame, with no
    /// coroutine and no ramp. A ramp would be worse than the bug: a burn STARTING in a browse fan
    /// minutes later is a picture the owner never had.</para>
    ///
    /// <para>REFUSED WHILE THE GAME IS BUSY. If a timeline is running (<c>coroutine != null</c>)
    /// this does nothing — cutting across the game's own ramp is exactly the write war the project
    /// has a standing ruling against. It also refuses a widget the game never
    /// <c>Initialize</c>d, because the no-ramp arm indexes <c>txtAffected</c> and
    /// <c>imgComp</c>.</para>
    ///
    /// <para>AND IT SUPPRESSES THE PARTICLE. The no-ramp arm calls <c>SpawnParticle()</c>; the
    /// owner's card sprayed its embers at BURN TIME, so a puff in a fan opened later is, again, a
    /// picture nobody had. <c>HideParticle()</c> is the game's own undo for it and runs immediately
    /// after.</para>
    /// </summary>
    internal static bool TrySettleBurnLook(CardEffects? fx)
    {
        if (fx == null || fx.coroutine != null)
            return false;
        try
        {
            if (fx.imgComp == null || fx.txtAffected == null)
                return false; // never Initialize()d — the no-ramp arm would index a null array
            System.Collections.IEnumerator settle = fx.BurnCardTimeline(burnAnim: false, playOnDisabled: true);
            while (settle.MoveNext())
            {
                // burnAnim:false never yields; the guard is against a game-side shape change
                // turning this into a real coroutine and spinning here forever.
                break;
            }
            fx.HideParticle();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Paint the SETTLED GHOST look — the cold grey-out the game puts on a DISCARDED card — by
    /// running the game's own timeline in its no-ramp arm. Returns true when the paint ran.
    ///
    /// <para>THE SIBLING OF <see cref="TrySettleBurnLook"/>, TERM FOR TERM, and it exists for the
    /// same reason: <c>GhostOutOnTimeline(ghostAnim: false)</c> writes the constant block and then
    /// the settled end state (<c>_GreyOut</c>=1, <c>_Flow</c>=1, <c>_Dissolve</c>=<c>mc_Dissolve</c>,
    /// <c>_FXAnim</c>=0.5, every affected text lerped fully to white with its vertex gradient off)
    /// and yields exactly once, at the very end (CardEffects.cs:712-737). So draining one
    /// <c>MoveNext</c> here runs the whole of it on this frame, with no coroutine and no ramp — the
    /// game's own numbers, never an approximation of them.</para>
    ///
    /// <para>WHY IT IS OWED (ModBuild 479, user item 4): <i>"Einmal grau bleibt die Karte (remote
    /// UND lokal) grau solange sie im aktiven Stapel liegt."</i> A Discard-bound ACTIVATED card is
    /// grey because the game ghosted it when the action resolved, and
    /// <c>FullAbilityCard.SetPile(Activated)</c> then calls <c>RestoreCard()</c> at the next hand
    /// refresh and wipes it — NOT unconditionally, as this line used to say: the three FX arms sit
    /// inside <c>if (cardPile != newCardPile &amp;&amp; cardEffects != null)</c>
    /// (FullAbilityCard.cs:313-315), so the wipe needs the WIDGET's own last value to change.
    /// The BURN half of that wipe already has a remedy
    /// (<see cref="TrySettleBurnLook"/>, <c>BurnLookPolicy</c> rule 1a); the GHOST half had none,
    /// which is the defect measured on both machines this round — <c>ACTIVE WASH</c> for
    /// <c>ABILITY_CARD_TheMindsWeakness</c> reads <c>_GreyOut 1.00</c> and then <c>_GreyOut
    /// 0.00</c> for the same Discard-bound activated card.</para>
    ///
    /// <para>REFUSED ON THE SAME THREE CONDITIONS. A running timeline (never cut across the game's
    /// own ramp), a widget the game never <c>Initialize</c>d (the no-ramp arm indexes
    /// <c>txtAffected</c> and <c>imgComp</c>), and — caught rather than tested, because the field is
    /// private — a widget with no <c>fgFx</c> overlay, which the arm dereferences unguarded. The
    /// refusal happens BEFORE any material is written in that last case: the unguarded
    /// <c>fgFx.material</c> write stands ahead of the whole else-branch.</para>
    ///
    /// <para>AND IT SUPPRESSES THE PARTICLE for the reason <see cref="TrySettleBurnLook"/> does: the
    /// no-ramp arm calls <c>SpawnParticle()</c>, and the owner's card puffed its smoke when the
    /// action resolved. A puff at a round boundary is a picture nobody had.</para>
    /// </summary>
    internal static bool TrySettleGhostLook(CardEffects? fx)
    {
        if (fx == null || fx.coroutine != null)
            return false;
        try
        {
            if (fx.imgComp == null || fx.txtAffected == null)
                return false; // never Initialize()d — the no-ramp arm would index a null array
            System.Collections.IEnumerator settle = fx.GhostOutOnTimeline(ghostAnim: false);
            while (settle.MoveNext())
            {
                // ghostAnim:false yields exactly once, AFTER the whole end state is written; the
                // guard is against a game-side shape change turning this into a real ramp and
                // spinning here forever.
                break;
            }
            fx.HideParticle();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The native loss sequence has a second timeline outside FullAbilityCard FX.
    /// Its two flags distinguish a running sequence from the game's cancelled-but-latched flag.
    /// Read the card owner's hand, never the current displayed character.</summary>
    internal static bool LosingCards(AbilityCardUI? widget)
    {
        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI? hand = widget?.PlayerActor != null && manager != null
            ? manager.GetHand(widget.PlayerActor) : null;
        return hand != null && hand.gameObject.activeInHierarchy
            && hand.AnimatingLostCards && hand.animatedLosingCard;
    }

    /// <summary>
    /// THE ONE RELEASE EXPRESSION. May a burn whose artwork state is <paramref name="playing"/> and
    /// which has been held for <paramref name="held"/> seconds fly now?
    ///
    /// <para>This is <c>CardsDriver.TryTakeBurnFlightSlot</c>'s condition, character for character,
    /// so that a mirror asking it gets the owner's answer rather than an equivalent-looking one.
    /// The ModBuild 474 logs measure what "equivalent-looking" cost: the owner's hold and the
    /// mirror's hold for the SAME burn, both clocked from the same host-replicated pile change,
    /// never once agreed —</para>
    /// <list type="bullet">
    ///   <item><description><c>ShieldBash</c> — owner 0.67 s (remote/Player.log 151241), mirror
    ///   1.00 s (Player.log 170102). Mirror LATE by 0.33 s.</description></item>
    ///   <item><description><c>SpareDagger</c>, the short-rest sacrifice — owner 0.50 s
    ///   (remote/Player.log 177185), mirror <b>0.02 s</b> (Player.log 197139, "their recess never
    ///   drew this card at all"). Mirror EARLY by 0.48 s: the card never lay still at all, which is
    ///   the reported defect in its own words.</description></item>
    ///   <item><description><c>FeedbackLoop</c> — owner 2.01 s (Player.log 249415), mirror 0.56 s
    ///   (remote/Player.log 225114). Mirror EARLY by 1.45 s.</description></item>
    /// </list>
    /// <para>Three burns, three different signs and magnitudes: that is a design fault and not a
    /// dial, which is why the remedy is one expression and not a tuned offset.</para>
    /// </summary>
    internal static bool Released(bool playing, float held)
    {
        return BurnFlightCompletion.MayRelease(playing, false, held, StartGraceSeconds);
    }
}
