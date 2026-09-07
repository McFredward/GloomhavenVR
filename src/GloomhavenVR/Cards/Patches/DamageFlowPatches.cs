using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.Events;

namespace GloomhavenVR.Cards.Patches;

// ---------------------------------------------------------------------------
// Damage-negation / burn flow hardening (tasks #11 + #10).
//
// The 2D game keeps its lose/discard commit safe by CONSTRUCTION: the
// "Karten verbrennen" DialogPopup only opens the moment
// selectedCardsUI.Count == maxCardsSelected, and while it is open every card is
// locked unselectable (CardsHandUI.OnCardSelected, CardsHandUI.cs:2029-2119) —
// so OnLoseCardClick may blindly index selectedCardsUI[0]/[1]
// (GameState.Lose2DiscardCardsToAvoidAttack(..., selectedCardsUI[0].AbilityCard,
// selectedCardsUI[1].AbilityCard), CardsHandUI.cs:2344). VR free card placement
// broke that invariant (cards plucked back while the popup showed); the commit
// then threw, the catch block routed into GlobalErrorMessage /
// ErrorHandlingUnloadSceneAndLoadMainMenu, and the scenario was torn down
// (EndScenarioSafely — the reproduced "crash to menu", Player.log 59952-60089).
// CardsDriver now cancels the popup through the game's own "choose another
// card" path the instant a pick card is grabbed; the prefix below is the
// DEFENSIVE last line: the commit REFUSES an incomplete selection instead of
// forwarding it into the game. Local UI only — no game state is faked, nothing
// is sent on the wire when the gate refuses.
// ---------------------------------------------------------------------------

/// <summary>
/// Task #11 (c): honest commit gate. Verified target: <c>private void
/// OnLoseCardClick()</c> (CardsHandUI.cs:2295) — the ONLY commit callback of the
/// lose/discard confirm popup (wired at CardsHandUI.cs:2085) and of the
/// ability-driven lose/discard flows. Mirrors the 2D invariant exactly: the
/// commit is legal only with <c>selectedCardsUI.Count == maxCardsSelected</c>.
/// On refusal the take-damage row is brought back so the player is never
/// stranded without an affordance.
/// </summary>
[HarmonyPatch(typeof(CardsHandUI), "OnLoseCardClick")]
internal static class CardsHandUI_OnLoseCardClick_Gate
{
    private static bool Prefix(CardsHandUI __instance)
    {
        int need = __instance.maxCardsSelected; // private, publicized
        int have = __instance.SelectedCards != null ? __instance.SelectedCards.Count : 0;
        if (need <= 0 || have >= need)
        {
            // ModBuild 351 — the selection is legal; now make sure the commit can actually RUN.
            // See BurnCommitRescue: the game's commit body is a StartCoroutine on THIS component,
            // and Unity refuses that outright on an inactive GameObject.
            BurnCommitRescue.EnsureHandCanRunItsCoroutine(__instance);
            BurnCommitWatch.Arm(__instance);
            // ITEMS 9 + 10 (2026-09-05) — END EDGE (a) of the pick flow. This prefix is the ONLY
            // commit callback of the lose/discard confirm popup and covers all three of its
            // branches, so it is the one place that means "the player has answered" for every
            // pick the game can open. Everything after it is animation. Nothing on the hand
            // records this: `cardHandMode` and `maxCardsSelected` both survive the whole flow and
            // stood for ~87 s of the 2026-09-05 hardware round while our banner kept asking for a
            // card. See Patches/PickFlowPatches.cs for the log excerpt and the other two edges.
            PickFlowWatch.NoteCommitAccepted(__instance, need);
            return true; // complete selection — run the game's commit unchanged
        }

        VRLog.Warn("Cards", $"Burn/lose commit REFUSED: {have}/{need} card(s) selected — the game " +
                            "would have indexed an incomplete selection (the crash-to-menu path). " +
                            "Selection stays open; place the required card(s) and confirm again.");
        try
        {
            // The popup already hid itself before this callback — restore the damage
            // row so the flow visibly continues (the cancel path does the same).
            Singleton<TakeDamagePanel>.Instance?.ToggleVisibility(visible: true);
        }
        catch (System.Exception ex)
        {
            VRLog.Debug("Cards", $"Commit-gate panel restore skipped: {ex.GetType().Name}.");
        }
        return false; // skip the game's commit — LOCAL refusal only
    }
}

// ---------------------------------------------------------------------------
// ModBuild 351 — the abandoned two-player session of ModBuild 348.
//
// WHAT HAPPENED, from both drops (local Player.log + remote/Player.log, 2026-09-02):
//
//   remote (the burning player, PlayerID 2, Barbar/Brute)
//     260453  STATE: Halted @ TakeDamageConfirmation      <- damage #1
//     262913  Switch hand Brute from Brute                <- CardsHandManager.Show ran
//     272807  OnLoseCardClick        -> #38 sent, coroutine RAN
//     273021  STATE: Halted @ NONE                        <- flow advanced. WORKED.
//     273363  STATE: Halted @ TakeDamageConfirmation      <- damage #2
//             (no "Switch hand" line anywhere in this window)
//     286699  OnLoseCardClick
//     286702  "Coroutine couldn't be started because the the game object
//              'Player handBrute' is inactive!"
//             -> #39 sent, but NO further STATE line for the rest of the session.
//     295892  the same line again for the player's retry (#40).
//
//   local (host)
//     328260 / 344117  ProcessFreely @ TakeDamageConfirmation, then Halted @ NONE
//                      after #38 and #39 — the host applied both burns fine.
//     369961  Received GameAction #40 (BurnAvailableCard @ TakeDamageConfirmation)
//     370124  [Net] DESYNC STALL: BurnAvailableCard waiting for TakeDamageConfirmation
//             while this client is in NONE — and it NEVER cleared.
//
// THE MECHANISM. `CardsHandUI.OnLoseCardClick` (CardsHandUI.cs:2340-2362) does the rule
// change first (`GameState.Lose1HandCardToAvoidAttack`) and then hands the REST of the
// flow to a coroutine on itself:
//
//     StartCoroutine(AnimateCardsLost(selectedCardsUI, delegate {
//         GameState.PlayerAvoidingDamage(avoidDamageOption2);   // <- advances the rules
//         Hide();                                               // <- SetActive(false)
//     }));
//
// `Hide()` -> `ShowOrHideInternal(false)` -> `base.gameObject.SetActive(false)`
// (CardsHandUI.cs:460/514). So a SUCCESSFUL burn leaves the hand object INACTIVE, and
// `MonoBehaviour.StartCoroutine` on an inactive GameObject is refused by Unity with
// exactly the line above — it logs, it does not throw. The rule change had already
// happened, the wire action had already been sent, and the ONE call that advances the
// local rule engine — `GameState.PlayerAvoidingDamage` — never ran. The client stayed in
// TakeDamageConfirmation for ever; every peer's copy of the burn then queued behind a
// phase that never opened. Nothing threw, so nothing was reported: `FFSNetwork.HandleDesync`
// never fired on either machine and the ActionProcessor's own 5 s deadline never fired
// either, because a HALTED processor returns from `TryProcessNextAction` before the
// incorrect-action counter is ever touched (ActionProcessor.cs:279, 370-394).
//
// WHY THE FIRST BURN WORKED AND THE SECOND DID NOT. In 2D the hand is put back on screen
// by `TakeDamagePanel.PreviewAvailableCards()` -> `CardsHandManager.Show(actor, LoseCard,
// …)` -> `SwitchHand` -> `hand.Show()` (TakeDamagePanel.cs:530-537, CardsHandManager.cs:645/
// 725), reached from the burn toggle's CLICK **or its mere mouse HOVER**
// (TakeDamagePanel.cs:466-512). Damage #1 went through that path — the "Switch hand" line
// proves it. Damage #2 did not: the player picked the card straight out of the VR fan.
// The hover half of that path is deliberately suppressed by this very file
// (TakeDamagePanel_BurnHover_Skip), so in VR there is exactly one way left to re-activate
// the hand and it is optional. The mod is therefore implicated: it removed one of the two
// vanilla routes into `PreviewAvailableCards` and its fan lets the pick be made without
// either.
//
// THE FIX HERE is deliberately the smallest thing that cannot be wrong: give the game's
// own commit an object it is allowed to start a coroutine on. Nothing about the pick, the
// mode, the selection or the card is touched, and the game's own `Hide()` at the end of
// AnimateCardsLost puts the object back exactly where it was. The hand stays invisible
// throughout: HandSuppression pins the whole hand window's CanvasGroup to alpha 0 every
// frame, and a burn holds that pin down (HandSuppression.BurnActive).
// ---------------------------------------------------------------------------

/// <summary>
/// Re-activate the game's own hand object so <c>CardsHandUI.OnLoseCardClick</c> can start
/// its <c>AnimateCardsLost</c> coroutine. Runs only when the object is actually inactive,
/// which in a healthy flow is never.
/// </summary>
internal static class BurnCommitRescue
{
    /// <summary>Guard against an unbounded walk if the hierarchy is ever cyclic/deep.</summary>
    private const int MaxAncestors = 16;

    private static readonly List<string> Reactivated = new();

    internal static void EnsureHandCanRunItsCoroutine(CardsHandUI hand)
    {
        try
        {
            if (hand == null)
                return;
            GameObject go = hand.gameObject;
            if (go == null || go.activeInHierarchy)
                return; // the healthy case — nothing to do, nothing logged

            Reactivated.Clear();
            Transform? t = go.transform;
            for (int i = 0; i < MaxAncestors && t != null; i++, t = t.parent)
            {
                if (t.gameObject.activeSelf)
                    continue;
                Reactivated.Add(t.gameObject.name);
                t.gameObject.SetActive(true);
            }

            bool live = go.activeInHierarchy;
            string names = Reactivated.Count > 0 ? string.Join(", ", Reactivated.ToArray()) : "none";

            // HW-VERIFY
            VRLog.Note("Cards",
                $"BURN COMMIT RESCUE on '{go.name}': the game's own hand object was INACTIVE at the " +
                "lose/burn commit, so CardsHandUI.OnLoseCardClick's StartCoroutine(AnimateCardsLost) " +
                "would have been refused by Unity ('Coroutine couldn't be started because the the game " +
                "object ... is inactive!') and the callback that advances the rules — " +
                "GameState.PlayerAvoidingDamage + Hide() — would never have run. That is the ModBuild 348 " +
                "abandoned session: the card is already lost and the BurnAvailableCard action is already " +
                "on the wire, but this client never leaves TakeDamageConfirmation and every peer's copy " +
                "of the burn queues behind a phase that never opens. Nothing throws, so nothing else " +
                $"reports it. Re-activated {Reactivated.Count} object(s): {names}. Hand is now live: {live}. " +
                "The pick, the mode, the selection and the card are untouched; the game's own Hide() at " +
                "the end of AnimateCardsLost puts the object back. IF THIS LINE APPEARS, the burn was " +
                "rescued — the underlying cause is that TakeDamagePanel.PreviewAvailableCards never ran " +
                "for this damage prompt (no 'Switch hand' line above it).");
        }
        catch (System.Exception ex)
        {
            VRLog.Alert("Cards",
                $"BURN COMMIT RESCUE threw ({ex.GetType().Name}: {ex.Message}) — the game's commit is " +
                "about to run on a possibly inactive hand; if the damage prompt hangs, this is why.");
        }
    }
}

/// <summary>
/// The falsifier for the rescue above, and the line that will name the cause if the burn
/// deadlock ever comes back in a shape the rescue does not cover. Armed by every legal
/// lose/burn commit; it reports ONCE if the local rule engine is still sitting in
/// <c>TakeDamageConfirmation</c> some seconds later, which is the exact fingerprint of
/// "the commit ran, the action went out, and nothing advanced". Ticked from
/// <c>HandSuppression.Tick</c> (CardsDriver, every frame).
///
/// <para><b>IT WATCHES THE DAMAGE BURN AND ONLY THE DAMAGE BURN — 2026-09-07 MEASURED THAT THE
/// HARD WAY.</b> <see cref="Arm"/> is called from EVERY legal <c>OnLoseCardClick</c>, but
/// <see cref="Tick"/>'s first act is to disarm unless the phase is
/// <c>TakeDamageConfirmation</c>. A LONG REST's lose-a-card commit runs in
/// <c>ActionSelection</c>, so the watch armed and disarmed on the same frame and reported
/// nothing — and that session's deadlock WAS a long rest whose commit never resolved. Anchored,
/// the host log carries ZERO <c>[Cards] BURN COMMIT HANG</c> lines (the 17 raw grep hits are
/// <c>[Net] DESYNC STALL</c> lines QUOTING this token inside their own body, which is why the
/// anchor matters).</para>
///
/// <para>THE PHASE TERM IS NOT WIDENED, DELIBERATELY. "Still in the phase it committed in" is
/// the right fingerprint for the damage burn because <c>TakeDamageConfirmation</c> exists only
/// to be left; it is the WRONG one for a long rest, whose commit happens in the middle of an
/// <c>ActionSelection</c> that correctly persists for the rest of the turn — so a generalised
/// term would fire on every healthy long rest. The long-rest commit has its own watch instead,
/// on its own resolution term (<c>CCharacterClass.HasLongRested</c>) and with its own token:
/// <c>LONG REST RE-DRIVE HELD</c> / <c>LONG REST RE-DRIVE ONCE</c> in
/// <c>CardsDriver.PumpLongRestTurn</c>. Read the two together — between them every burn commit
/// this mod can route has a watch, and neither pretends to cover the other's flow.</para>
///
/// <para><b>AND SINCE 2026-09-07 THERE IS A SECOND, NETWORK-INDEPENDENT HALF (review item D5).</b>
/// The phase watch above is online-only by construction, and <see cref="Arm"/> used to return early
/// on <c>!FFSNetwork.IsOnline</c> for the WHOLE method — so single player had no instrument for the
/// burn-commit hang at all. The second half watches the game's own <c>AnimatingLostCards</c> flag
/// from the commit until it comes back down, which is the fingerprint of the REAL unbounded wait:
/// <c>AnimateCardsLost</c>'s <c>WaitUntil(() =&gt; animations.Count == 0)</c> (CardsHandUI.cs:1103).
/// Token: <c>BURN ANIM STUCK</c>. It reports and repairs nothing.</para>
/// </summary>
internal static class BurnCommitWatch
{
    /// <summary>How long a commit may leave the client in TakeDamageConfirmation before it is a hang.</summary>
    private const float StuckSeconds = 8f;

    /// <summary>
    /// How long <c>CardsHandUI.AnimatingLostCards</c> may stand true after a commit before the
    /// animation that carries the answer is called hung. Generous against any serialized tween
    /// chain — <c>discardedCardsMoveTime</c> and <c>postAnimationWaitTime</c> are dials this mod
    /// cannot read — and still far short of a lost session.
    /// </summary>
    private const float AnimatingStuckSeconds = 20f;

    /// <summary>
    /// How long the watch waits for <c>AnimatingLostCards</c> to come UP before concluding this
    /// commit branch simply does not animate (the ability branches can take
    /// <c>InstantCardLost</c>). Reaching it disarms SILENTLY: "no animation" is not a finding.
    /// </summary>
    private const float AnimatingArrivalSeconds = 5f;

    private static bool _armed;
    private static bool _reported;
    private static float _armedAt;

    /// <summary>The hand that committed, for the network-independent half of the watch.</summary>
    private static CardsHandUI? _hand;
    private static bool _animArmed;
    private static bool _animSeenRunning;
    private static bool _animReported;
    private static float _animArmedAt;
    private static float _animRunningSince;

    /// <summary>
    /// Arm both halves at the game's own commit.
    ///
    /// <para><b>THE `!FFSNetwork.IsOnline` EARLY RETURN THAT USED TO STAND HERE LEFT SINGLE PLAYER
    /// WITH NO INSTRUMENT AT ALL for the burn-commit hang (2026-09-07 review, D5).</b> It is
    /// correct about the PHASE watch — <c>FFSNet.ActionProcessor.CurrentPhase</c> only means
    /// anything online — but it was applied to the whole method, so an offline commit that never
    /// resolved produced not one line. The phase half is still gated (inside <see cref="Tick"/>,
    /// where it belongs); the second half below is network-independent.</para>
    ///
    /// <para><b>THE SECOND HALF WATCHES THE REAL UNBOUNDED WAIT.</b> Every branch of
    /// <c>OnLoseCardClick</c> that animates ends in <c>StartCoroutine(AnimateCardsLost(...))</c>,
    /// and that coroutine's own bound is <c>yield return new WaitUntil(() =&gt;
    /// animations.Count == 0)</c> (CardsHandUI.cs:1103) — a wait on a LeanTween list with no
    /// timeout of any kind. Everything the flow depends on runs AFTER it:
    /// <c>onCompleteCallback</c> (<c>GameState.PlayerAvoidingDamage</c> + <c>Hide()</c>, or
    /// <c>ShowLongRested</c>), the UI-lock release, and <c>AnimatingLostCards = false</c> at :1115.
    /// So <c>AnimatingLostCards</c> standing true is the exact fingerprint of that wait not
    /// returning, and it is readable offline and online alike.</para>
    ///
    /// <para><b>IT IS AN INSTRUMENT AND NOTHING ELSE.</b> No remedy: forcing the game's
    /// <c>animations</c> list empty would write game state from presentation code, which this
    /// project forbids outright.</para>
    /// </summary>
    internal static void Arm(CardsHandUI? hand)
    {
        try
        {
            _hand = hand;
            _animArmed = hand != null;
            _animSeenRunning = false;
            _animReported = false;
            _animArmedAt = Time.unscaledTime;
            _animRunningSince = 0f;

            if (!FFSNetwork.IsOnline)
                return; // the PHASE half only: the action processor's phase means nothing offline
            _armed = true;
            _reported = false;
            _armedAt = Time.unscaledTime;
        }
        catch (System.Exception ex)
        {
            VRLog.Debug("Cards", $"Burn commit watch could not arm: {ex.GetType().Name}.");
        }
    }

    internal static void Tick()
    {
        TickAnimation();
        if (!_armed)
            return;
        try
        {
            if (FFSNet.ActionProcessor.CurrentPhase != FFSNet.ActionPhaseType.TakeDamageConfirmation)
            {
                if (_reported)
                {
                    // HW-VERIFY
                    VRLog.Note("Cards",
                        $"BURN COMMIT HANG CLEARED after {Time.unscaledTime - _armedAt:0.0}s — the client " +
                        "left TakeDamageConfirmation after all. The session was not lost.");
                }
                _armed = false;
                _reported = false;
                return;
            }

            if (_reported || Time.unscaledTime - _armedAt < StuckSeconds)
                return;

            _reported = true;
            // HW-VERIFY
            VRLog.Alert("Cards",
                $"BURN COMMIT HANG: {Time.unscaledTime - _armedAt:0.0}s after a lose/burn commit this " +
                "client is STILL in ActionPhaseType.TakeDamageConfirmation. The card has already been " +
                "moved to the lost pile and BurnAvailableCard has already been sent, but " +
                "GameState.PlayerAvoidingDamage never ran, so nothing here or on any peer can advance: " +
                "the other clients' copies of this burn queue behind a phase that never opens and NOTHING " +
                "reports it (no exception, so no HandleDesync; a HALTED ActionProcessor never reaches the " +
                "5 s incorrect-action deadline either). The known cause is CardsHandUI.OnLoseCardClick's " +
                "StartCoroutine being refused on an inactive hand object — look for a 'Coroutine couldn't " +
                "be started' line, and for a 'BURN COMMIT RESCUE' line just above this one. If the RESCUE " +
                "line is present and this one still fired, the coroutine started and something ELSE " +
                "swallowed it. THE NEXT SUSPECT IS THE WaitUntil AT CardsHandUI.cs:1103, NOT " +
                "OnDisable: 'CardsHandUI.OnDisable -> CancelAnimateCardLost -> StopAllCoroutines' " +
                "stood here for several builds and CANNOT be the cause — CancelAnimateCardLost " +
                "(CardsHandUI.cs:1154-1178) releases the UI lock and INVOKES " +
                "onAnimateCardLostCallback before nulling it, so that path advances the flow " +
                "rather than swallowing it. AnimateCardsLost's own 'yield return new WaitUntil(() " +
                "=> animations.Count == 0)' has no timeout of any kind, and everything the rules " +
                "need runs after it. Read the BURN ANIM STUCK line below for that half. Reported " +
                "once per commit.");
        }
        catch (System.Exception ex)
        {
            _armed = false;
            VRLog.Warn("Cards", $"Burn commit watch threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The network-independent half — see <see cref="Arm"/>. Watches the game's own
    /// <c>AnimatingLostCards</c> flag from the commit until it comes back down, and reports ONCE if
    /// it does not. Writes nothing.
    /// </summary>
    private static void TickAnimation()
    {
        if (!_animArmed)
            return;
        try
        {
            CardsHandUI? hand = _hand;
            if (hand == null)
            {
                Disarm();
                return;
            }

            float now = Time.unscaledTime;
            bool animating = hand.AnimatingLostCards;

            if (!_animSeenRunning)
            {
                if (animating)
                {
                    _animSeenRunning = true;
                    _animRunningSince = now;
                    return;
                }
                if (now - _animArmedAt >= AnimatingArrivalSeconds)
                    Disarm(); // this commit branch does not animate — no claim, no line
                return;
            }

            if (!animating)
            {
                if (_animReported)
                {
                    // HW-VERIFY: grep token "BURN ANIM STUCK CLEARED".
                    VRLog.Note("Cards",
                        $"BURN ANIM STUCK CLEARED after {now - _animRunningSince:0.0}s — " +
                        "CardsHandUI.AnimatingLostCards came back down after all, so the " +
                        "WaitUntil(animations.Count == 0) returned and the flow's completion " +
                        "callback ran. The turn was not lost.");
                }
                Disarm();
                return;
            }

            if (_animReported || now - _animRunningSince < AnimatingStuckSeconds)
                return;

            _animReported = true;
            // HW-VERIFY: grep token "BURN ANIM STUCK".
            // WORKING = absent from every log, offline and online alike.
            // INERT = a session in which the player reports a burn that never finished and this
            // line is missing: the arm is not reaching the commit, and single player is blind
            // again — which is exactly what the `!FFSNetwork.IsOnline` early return in Arm() used
            // to guarantee (2026-09-07 review, D5).
            // STILL BEYOND THE INSTRUMENT = it cannot say WHICH LeanTween never completed, only
            // that the list never emptied. It is a report, never a remedy: clearing the game's
            // animation list would be presentation code writing game state.
            VRLog.Alert("Cards",
                $"BURN ANIM STUCK: {now - _animRunningSince:0.0}s after this client's lose/burn commit, " +
                "CardsHandUI.AnimatingLostCards is STILL true on " +
                $"'{HandName(hand)}'. AnimateCardsLost is parked, and its only bound is " +
                "'yield return new WaitUntil(() => animations.Count == 0)' (CardsHandUI.cs:1103) — a " +
                "wait on a LeanTween list with no timeout. Everything the flow needs comes AFTER it: " +
                "the UI-lock release, onCompleteCallback (GameState.PlayerAvoidingDamage + Hide(), or " +
                "ShowLongRested) and AnimatingLostCards = false. So the card is gone, the answer is " +
                "given, and nothing advances. THIS HALF OF THE WATCH RUNS OFFLINE TOO — the phase " +
                "half above cannot, because FFSNet.ActionProcessor.CurrentPhase only means anything " +
                "online, and gating the WHOLE watch on that left single player with no instrument at " +
                "all. Reported once per commit; no remedy is shipped, deliberately.");
        }
        catch (System.Exception ex)
        {
            Disarm();
            VRLog.Warn("Cards", $"Burn animation watch threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Disarm()
    {
        _animArmed = false;
        _animSeenRunning = false;
        _animReported = false;
        _hand = null;
    }

    private static string HandName(CardsHandUI hand)
    {
        try
        {
            return hand.gameObject != null ? hand.gameObject.name : "?";
        }
        catch (System.Exception)
        {
            return "?";
        }
    }
}

// ---------------------------------------------------------------------------
// Task #10: hover-triggered burn theatrics.
//
// Two game paths animate big on a mere pointer HOVER, which the VR laser
// triggers constantly while aiming across the docked rows:
//
// 1. TakeDamagePanel wires its burn toggles' mouse enter/exit to
//    PreviewAvailableCards/PreviewDiscardedCards (TakeDamagePanel.cs:466-512)
//    — each hover SHOWS the whole card hand (CardsHandManager.Show), each
//    un-hover hides it: in VR the entire fan bursts open/closed while the beam
//    sweeps the row. The CLICK path (BurnAvailableCard/BurnDiscardedCards →
//    OnSelectedToggle → Preview*) is untouched — a deliberate toggle still
//    previews the cards.
//
// 2. The card-content DialogPopup options carry onMouseEnter/onMouseExit
//    actions that run the card's burn/ghost dissolve timeline both ways
//    (CardEffects.ToggleAdditiveEffect → BurnCard(burnAnim)/GhostOutOn —
//    BOTH directions start a full-card coroutine timeline, CardEffects.cs:404-446;
//    wired via DialogPopup.Show's onMouseEnter/onMouseExit listeners,
//    DialogPopup.cs:212-219). Every laser pass over "Karten verbrennen" churned
//    a theatrical dissolve on the dock card (the repeated "Burn/ghost effect
//    playing" spam, Player.log 59884-59907).
//
// Both hover paths are suppressed; the ACTUAL burn (commit → AnimateCardsLost /
// FinalizeShortRest / TryPlayBurnAnimation) still plays on the dock card, where
// BurnCardFx bounds the world-space smoke card-local (×0.35 size/speed) — the
// retained, card-sized burn effect.
// ---------------------------------------------------------------------------

/// <summary>Skip the burn toggles' hover-preview (enter) handlers — VR laser hover
/// must not open the full hand preview. Clicks still preview.</summary>
[HarmonyPatch]
internal static class TakeDamagePanel_BurnHover_Skip
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseEnterBurnOne))]
    private static bool SkipEnterOne() => false;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseEnterBurnTwo))]
    private static bool SkipEnterTwo() => false;

    // The exits are skipped symmetrically: with the enters gone, ResetPreviewing on
    // exit would still re-run BurnAvailableCard/BurnDiscardedCards for a toggled
    // option (TakeDamagePanel.ResetPreviewing:648-668) — a hidden hover side effect.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseExitBurnOne))]
    private static bool SkipExitOne() => false;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseExitBurnTwo))]
    private static bool SkipExitTwo() => false;
}

/// <summary>
/// Strip the hover (mouse enter/exit) listeners off the card-content dialog's
/// option buttons right after the game wires them — they only drive the
/// burn/ghost dissolve churn described above. Target: the List overload
/// <c>public void Show(List&lt;GameObject&gt; content, DialogOption[] options,
/// bool allowHide, int cancelOption, UnityAction cancelAction)</c>
/// (DialogPopup.cs:157) — the single-GameObject overload delegates to it, and
/// the string-content overloads (plain text dialogs) are left untouched.
/// Click/keyboard/controller activation of every option is unaffected.
/// </summary>
[HarmonyPatch(typeof(DialogPopup), nameof(DialogPopup.Show),
    typeof(List<GameObject>), typeof(DialogOption[]), typeof(bool), typeof(int), typeof(UnityAction))]
internal static class DialogPopup_Show_HoverStrip
{
    private static bool s_logged;

    private static void Postfix(DialogPopup __instance)
    {
        List<InputButton> buttons = __instance.optionButtons; // private, publicized
        if (buttons == null)
            return;
        for (int i = 0; i < buttons.Count; i++)
        {
            InputButton button = buttons[i];
            if (button == null || button.ExtendedButton == null)
                continue;
            button.ExtendedButton.onMouseEnter.RemoveAllListeners();
            button.ExtendedButton.onMouseExit.RemoveAllListeners();
        }
        if (!s_logged)
        {
            s_logged = true;
            VRLog.Info("Cards", "Dialog option hover FX stripped (task #10): laser hover over the " +
                                "docked burn/lose choice no longer runs the card dissolve timeline; " +
                                "the real burn still plays card-sized on the dock (BurnCardFx).");
        }
    }
}
