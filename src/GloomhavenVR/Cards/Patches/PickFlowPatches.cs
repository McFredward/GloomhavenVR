using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards.Patches;

// ---------------------------------------------------------------------------
// THE PICK FLOW'S OWN BEGINNING AND END (2026-09-05 items 9 + 10).
//
// USER REPORT, verbatim, both halves of one defect:
//
//   (9) "Der Hinweis 'Wähle eine Karte zum verlieren' bleibt auch nach Abschluss des
//        Verbrennens noch da stehen. Das soll dann weg in dem Moment in dem die Karte(n)
//        verbrannt wurden."
//
//   (10) "Beim zweiten Mal Schaden nachdem ich eine Karte verbrannt habe, wollte ich den
//         Schaden nehmen, das hat aber nicht funktioniert. Stattdessen wollte mich das Spiel
//         zwingen noch eine Karte abzulegen. ... Wenn der Verbrennen-Flow vorbei ist muss
//         sich das Controllboard zwingend sofort anpassen!"
//
// WHAT THE 2026-09-05 HARDWARE LOG ACTUALLY SHOWS (host Player.log, ~90 fps):
//
//   232092  DECISION DOCK: ... #0=OFFERED+HOVER+PRESS/role burn-available   <- he presses "burn 1"
//   232105  Switch hand Mindthief from Mindthief        <- the GAME's own SwitchHand log line
//   232113  PICK GATE (LoseCard): pick=OPEN (maxCardsSelected = 1), field=0
//   232117  Pick banner: "Testo: Wähle 1 Karte(n) zum Verlieren"            <- correct, flow is live
//   232721  Pick commit (LoseCard): 'ParasiticInfluence' -> SelectCard accepted
//   233583  BURN CARD [owner/pile-watch]: this client burned 'ParasiticInfluence'  <- FLOW ENDS
//   -------- frame ~212950. Nothing stands the banner down. --------
//   235073  TAKE-DAMAGE item place ARMED                                    <- damage event #2
//   239838  DECISION DOCK: ... #1=OFFERED+HOVER+PRESS/role take-damage      <- he presses "take damage"
//   239872  uGUI click: 'Receive Damage'  +  TAKE-DAMAGE item place END     <- the press DID register
//   239896  [Board] [Focus] the game is waiting on NOBODY
//   240034  PICK GATE (LoseCard): pick=OPEN (maxCardsSelected = 1), field=0 <- STILL "open"
//   240037  Pick banner: "Testo: Wähle 1 Karte(n) zum Verlieren"            <- WE re-raise it
//   240040  Pick banner SENT: ...                                          <- and push it to the peer
//   -------- frame ~217640, i.e. 4,690 frames / ~52 s after the burn --------
//   243318  Pick banner SENT: placard hidden      <- NOT the fix: he merely LOOKED at 'Testi'
//   243395  Pick banner: "Testo: Wähle 1 Karte(n) zum Verlieren"  <- and it came straight back
//   244474  Switch hand Mindthief from Mindthief   <- the CO-PLAYER'S TURN BOUNDARY
//   244493  Pick banner SENT: placard hidden       <- only NOW, frame ~220800, ~87 s after the burn
//
// So the game did NOT force him to discard: THE MOD DID. Nothing in the game asked for a
// second card; our own banner asked, because the two terms it reads are leftovers.
//
// THE CAUSE, from the game's own source and not from inference:
//
//   * `CardsHandUI.cardHandMode` and `CardsHandUI.maxCardsSelected` are written in exactly
//     one place — `CardsHandUI.UpdateView` (CardsHandUI.cs:572, fields assigned :576/:581).
//   * `TakeDamagePanel.ResetAndHide` (TakeDamagePanel.cs:1042) — the ONE method every close
//     of the damage decision funnels through (:838, :1179, :1192 and the take-damage button)
//     — hides its own window, resets its toggles and nulls `actorBeingAttacked`. IT NEVER
//     TOUCHES `CardsHandManager` OR ANY `CardsHandUI`. Read the body: there is no term for it.
//   * So after the flow both fields keep whatever the last `Show` left. Here that was
//     `TakeDamagePanel.PreviewAvailableCards` (:538) → `Show(..., LoseCard, ..., Hand, 1, ...)`,
//     which is why the log reads `LoseCard` / `1` for a minute and a half.
//
// WHERE MODBUILD 448 WAS HALF RIGHT, AND WHERE THIS FILE CORRECTS IT. 448 added
// `CardsGameApi.PickIsOpen` because it believed the panel's close path re-Shows the hand with
// `maxCardsSelected = 0` and that reading the count RAW would therefore catch the leftover.
// The cited line, TakeDamagePanel.cs:520, is `PreviewFullHand` — a HOVER PREVIEW, not a reset,
// and in VR its hover route is deliberately dead (`TakeDamagePanel_BurnHover_Skip`). Nothing
// writes a zero on close, so the count belt reads 1 and passes. That is 448's own first
// falsifier firing verbatim: "pick=OPEN standing for minutes after the last Pick commit: the
// game leaves a nonzero count behind too, so the count is not the right term." All four host
// and all four peer PICK GATE lines of the round read `pick=OPEN`; not one reads CLOSED.
//
// THE REMEDY IS AN EDGE, NOT A STATE. No field on the hand distinguishes "a pick is being
// asked for" from "a pick was asked for once"; both terms are latches and both stayed set.
// What DOES exist is the game's own beginning and end of the flow, and each is a single call:
//
//   OPEN  — `CardsHandUI.UpdateView(..., maxCardsSelected > 0, ...)`. The one writer of both
//           latched fields, so nothing can open a pick behind its back: every overload of
//           `CardsHandManager.Show` (:739 and :813), `ShowCoroutine` (:838) and the push/pop
//           restore (:1262) all end in it.
//   END   — two calls, both already choke points this codebase names:
//           (a) the COMMIT. `CardsHandUI.OnLoseCardClick` is the only commit callback of the
//               lose/discard confirm popup (wired CardsHandUI.cs:2085) and covers all three of
//               its branches — ability LoseCards, ability DiscardCards and the damage burn. The
//               player has answered; what follows is animation.
//           (b) the BURN'S LAST FRAME. The damage-burn branch ends
//                   StartCoroutine(AnimateCardsLost(selectedCardsUI, delegate {
//                       GameState.PlayerAvoidingDamage(avoidDamageOption2);
//                       Hide();                       // CardsHandUI.cs:2354
//                   }));
//               so `CardsHandUI.Hide()` runs when the lost-card animation COMPLETES. That is
//               item 9's edge word for word: "in dem Moment in dem die Karte(n) verbrannt
//               wurden". It is also the end of a flow that never reaches a commit at all.
//
// This file owns those three edges and nothing else. It writes no game state, changes no
// selection, and reads no field it does not print. The board re-reads the answer on its very
// next tick — `CardsDriver.UpdatePickStatus` runs every frame — which is what makes the
// user's "muss sich sofort anpassen" a number rather than a promise: see the
// `PICK FLOW END` line for the two timestamps.
//
// DELIBERATELY NOT COVERED, and named so the next round can decide rather than re-derive: the
// two ABILITY-driven branches of `OnLoseCardClick` end their animation with
// `SetMode(currentMode, Any, { CardPileType.None }, ...)` instead of `Hide()`
// (CardsHandUI.cs:2308/:2327). Their commit edge (a) covers them; their animation-end edge has
// a different signature (an empty selectable set, not a hidden hand) and no hardware report
// has asked for it. It is one more disarm site the day one does.
// ---------------------------------------------------------------------------

/// <summary>
/// IS A MODAL CARD PICK ACTUALLY IN FLIGHT — as an EDGE-DRIVEN latch, because no STATE on the
/// game's hand can answer it (see this file's header for the measurement that decided that).
///
/// <para>Armed by the game's own <c>CardsHandUI.UpdateView</c> when it opens a pick that wants
/// at least one card; disarmed by the game's own commit callback and by the game's own
/// <c>CardsHandUI.Hide</c> at the end of the lost-card animation. Read through
/// <see cref="CardsGameApi.PickIsOpen"/>, which ANDs it with ModBuild 448's raw-count belt —
/// the two are independent and neither is sufficient alone: the count catches a pick the game
/// zeroed without hiding anything, the latch catches the far commoner case of a pick the game
/// answered and then simply walked away from.</para>
///
/// <para>SCOPED TO THE HAND THAT OPENED IT (2026-09-06 item 6b). The sentence that stood here read
/// "one pick is modal by construction (the game's confirm popup locks every card while it is
/// open), so there is never a second flow to keep apart", and the 2026-09-06 hardware round
/// falsifies it. Both patches below are on the TYPE, so they fire for EVERY <c>CardsHandUI</c> in
/// the scene, and in multiplayer this client builds a hand object for every character in the
/// party, not only its own. The host's log proves the leak on its own terms: it recorded FIVE
/// <c>PICK FLOW END</c> lines naming TWO different hands (<c>'Player handMindthief'</c> once,
/// <c>'Player handBrute'</c> four times) in a session in which it ran ZERO picks of its own — its
/// <c>Pick commit</c>, <c>PICK GATE</c> and <c>Pick fan source</c> counts are all 0. Every one of
/// those five edges was a FOREIGN character's burn moving this client's single global latch. A
/// foreign hand hiding could therefore end a live LOCAL pick, and a foreign <c>UpdateView</c>
/// could arm one where nothing was being asked of this player. So the latch now records WHICH
/// hand opened it and only that hand's END edges can close it; <see cref="LiveFor"/> is the term
/// every gate reads.</para>
/// </summary>
internal static class PickFlowWatch
{
    /// <summary>True between the game's OPEN edge and the first of its END edges.</summary>
    private static bool _live;

    /// <summary>
    /// <c>GetInstanceID</c> of the <c>CardsHandUI</c> whose <c>UpdateView</c> armed the live flow,
    /// or 0 when the arming hand could not be identified. Only this hand's END edges close the
    /// flow, and <see cref="LiveFor"/> answers TRUE only for it — see the class remarks for the
    /// host log that made the global latch a defect.
    /// </summary>
    private static int _openedOn;

    /// <summary>Name of <see cref="_openedOn"/> for the log lines (decoration, never a gate).</summary>
    private static string _openedOnName = "?";

    /// <summary>Bumped once per OPEN edge — the ARM half of the board's one-line-per-edge report.</summary>
    private static int _armSeq;

    /// <summary><c>Time.frameCount</c> of the most recent OPEN edge.</summary>
    private static int _openedFrame = -1;

    /// <summary>Unscaled time of the OPEN edge that armed the live flow.</summary>
    private static float _openedAt;

    /// <summary>Unscaled time of the most recent END edge (negative before the first one).</summary>
    private static float _endedAt = -1f;

    /// <summary><c>Time.frameCount</c> of the most recent END edge.</summary>
    private static int _endedFrame = -1;

    /// <summary>Which of the three edges ended the flow — printed verbatim by the board's line.</summary>
    private static string _endedBy = "no pick flow has ended yet this session";

    /// <summary>How long the flow that just ended had been live.</summary>
    private static float _lastFlowSeconds;

    /// <summary>
    /// Bumped once per END edge. The board compares it against the sequence number it last
    /// REPORTED, so its <c>PICK FLOW END</c> line prints once per flow and never per frame —
    /// and so a flow that ends while no banner is up is still counted rather than lost.
    /// </summary>
    private static int _endSeq;

    /// <summary>
    /// Is a pick flow live right now, for ANY hand? Kept for the one caller that has no hand to ask
    /// about (<see cref="CardsGameApi.PickIsOpen"/>'s no-hand fallback). Every gate that HAS a hand
    /// must use <see cref="LiveFor"/> instead — see the class remarks.
    /// </summary>
    internal static bool Live => _live;

    /// <summary>
    /// Is a pick flow live FOR THIS HAND? The term every gate reads. False for a hand that did not
    /// open the flow, so a co-player's burn can neither arm nor disarm this client's board.
    /// </summary>
    internal static bool LiveFor(CardsHandUI? hand)
    {
        if (!_live)
            return false;
        if (_openedOn == 0)
            return true; // the arming hand could not be identified — fall back to the old global answer
        try
        {
            return hand != null && hand.GetInstanceID() == _openedOn;
        }
        catch (Exception)
        {
            return false; // a hand mid-teardown owns no flow
        }
    }

    /// <summary>The hand that opened the live (or last) flow, for the log lines.</summary>
    internal static string OpenedOnName => _openedOnName;

    /// <summary>Monotone counter of OPEN edges — the board's "have I reported this ARM yet" key.</summary>
    internal static int ArmSeq => _armSeq;

    /// <summary><c>Time.frameCount</c> at the last OPEN edge, or -1.</summary>
    internal static int OpenedFrame => _openedFrame;

    /// <summary>Unscaled time of the last OPEN edge.</summary>
    internal static float OpenedAt => _openedAt;

    /// <summary>Unscaled time of the last END edge, or a negative number if there has been none.</summary>
    internal static float EndedAt => _endedAt;

    /// <summary><c>Time.frameCount</c> at the last END edge, or -1.</summary>
    internal static int EndedFrame => _endedFrame;

    /// <summary>The edge that ended the last flow, as a phrase for the log line.</summary>
    internal static string EndedBy => _endedBy;

    /// <summary>How long the last flow stayed live, in seconds.</summary>
    internal static float LastFlowSeconds => _lastFlowSeconds;

    /// <summary>Monotone counter of END edges — the board's "have I reported this one yet" key.</summary>
    internal static int EndSeq => _endSeq;

    /// <summary>
    /// OPEN edge. Called from the <c>CardsHandUI.UpdateView</c> postfix with the count the game
    /// itself passed. A zero is an explicit stand-down and ends the flow; anything above zero
    /// opens one. Re-arming an already-live flow keeps the original open stamp so
    /// <see cref="LastFlowSeconds"/> measures the whole pick and not its last redraw.
    /// </summary>
    internal static void NoteViewDriven(CardsHandUI? hand, int maxCardsSelected)
    {
        if (maxCardsSelected > 0)
        {
            if (_live)
                return;
            _live = true;
            _openedAt = Time.unscaledTime;
            _openedFrame = Time.frameCount;
            _openedOn = 0;
            _openedOnName = "?";
            try
            {
                if (hand != null)
                {
                    _openedOn = hand.GetInstanceID();
                    if (hand.gameObject != null)
                        _openedOnName = hand.gameObject.name;
                }
            }
            catch (Exception)
            {
                // an unreadable hand still opened a flow — LiveFor falls back to the global answer
            }
            _armSeq++;
            return;
        }

        // A ZERO IS A STAND-DOWN ONLY FOR THE HAND THAT OPENED THE FLOW. Every non-pick Show of
        // every OTHER character's hand passes maxCardsSelected = 0, so without the ownership test
        // a co-player's ordinary card-selection redraw ended this player's live burn pick.
        if (!OwnsLiveFlow(hand))
            return;
        NoteEnd("the game re-drove the hand's view asking for 0 cards (CardsHandUI.UpdateView)");
    }

    /// <summary>END edge (a): the player answered and the game's commit is about to run.</summary>
    internal static void NoteCommitAccepted(CardsHandUI? hand, int wanted)
    {
        if (!OwnsLiveFlow(hand))
            return;
        NoteEnd($"the player COMMITTED the pick — CardsHandUI.OnLoseCardClick accepted {wanted} card(s)");
    }

    /// <summary>
    /// Is this the hand whose OPEN edge armed the live flow? An unidentified owner (<c>_openedOn</c>
    /// == 0) accepts anything, which is the pre-2026-09-06 behaviour and the only safe fallback: a
    /// flow whose owner we never learned must still be closable.
    /// </summary>
    private static bool OwnsLiveFlow(CardsHandUI? hand)
    {
        if (_openedOn == 0)
            return true;
        try
        {
            return hand != null && hand.GetInstanceID() == _openedOn;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// END edge (b): the game hid the hand that was answering. For the damage burn this is the
    /// completion callback of <c>AnimateCardsLost</c> (CardsHandUI.cs:2354), i.e. the frame the
    /// card has finished burning — item 9's edge exactly.
    /// </summary>
    internal static void NoteHandHidden(CardsHandUI? hand, string who)
    {
        // ONLY THE HAND THAT OPENED THE FLOW MAY END IT. In multiplayer this client owns a
        // CardsHandUI for every character in the party and the patch is on the TYPE, so a
        // CO-PLAYER's hand being hidden used to close this player's live pick — see the class
        // remarks for the host log that recorded five of exactly those foreign edges.
        if (!OwnsLiveFlow(hand))
            return;
        NoteEnd($"the game HID the hand '{who}' — CardsHandUI.Hide, which for a damage burn is " +
                "AnimateCardsLost's completion callback, i.e. the frame the card finished burning");
    }

    private static void NoteEnd(string why)
    {
        if (!_live)
            return; // idempotent: three edges may fire for one flow, only the first one is the end
        _live = false;
        _endedAt = Time.unscaledTime;
        _endedFrame = Time.frameCount;
        _endedBy = why;
        _lastFlowSeconds = _endedAt - _openedAt;
        _endSeq++;
        _openedOn = 0; // the flow is over; nothing may be "its" hand until the next OPEN edge
    }

    /// <summary>Module shutdown / hot reload — leave no latch behind for the next session.</summary>
    internal static void Reset()
    {
        _live = false;
        _endedAt = -1f;
        _endedFrame = -1;
        _endedBy = "no pick flow has ended yet this session";
        _lastFlowSeconds = 0f;
        _endSeq = 0;
        _openedOn = 0;
        _openedOnName = "?";
        _armSeq = 0;
        _openedFrame = -1;
    }
}

/// <summary>
/// OPEN edge. Verified target: the root overload <c>public void UpdateView(CardHandMode mode,
/// bool enableLongCardSelection, CardPileType filterType, List&lt;CardPileType&gt;
/// selectableCardTypes, int maxCardsSelected, bool fadeUnselectableCards, bool
/// highlightSelectableCards, CardActionsCommand resetCardActions, bool forceUseCurrentRoundCards,
/// Action&lt;AbilityCardUI&gt; overrideCardCallback, Func&lt;CAbilityCard, bool&gt; cardFilter)</c>
/// (CardsHandUI.cs:572) — the ONE writer of <c>cardHandMode</c> (:576) and
/// <c>maxCardsSelected</c> (:581), which every shorter overload and every
/// <c>CardsHandManager.Show</c> path delegates to. Postfix only: the game's own view is built
/// first and we record what it asked for.
/// </summary>
[HarmonyPatch(typeof(CardsHandUI), nameof(CardsHandUI.UpdateView),
    typeof(CardHandMode), typeof(bool), typeof(CardPileType), typeof(List<CardPileType>),
    typeof(int), typeof(bool), typeof(bool), typeof(CardsHandUI.CardActionsCommand),
    typeof(bool), typeof(Action<AbilityCardUI>), typeof(Func<CAbilityCard, bool>))]
internal static class CardsHandUI_UpdateView_PickFlowOpen
{
    private static void Postfix(CardsHandUI __instance, int maxCardsSelected)
        => PickFlowWatch.NoteViewDriven(__instance, maxCardsSelected);
}

/// <summary>
/// END edge (b). Verified target: <c>public void Hide()</c> (CardsHandUI.cs:460) →
/// <c>ShowOrHideInternal(show: false)</c> (:512) → <c>gameObject.SetActive(false)</c>. The mod
/// never calls it and never writes that active bit (the 2D hand is suppressed by pinning the
/// window's CanvasGroup alpha, see HandSuppressionPatches.cs), so every invocation is the
/// game's own.
/// </summary>
[HarmonyPatch(typeof(CardsHandUI), nameof(CardsHandUI.Hide))]
internal static class CardsHandUI_Hide_PickFlowEnd
{
    private static void Postfix(CardsHandUI __instance)
    {
        string who = "?";
        try
        {
            if (__instance != null && __instance.gameObject != null)
                who = __instance.gameObject.name;
        }
        catch (Exception)
        {
            // a destroyed hand still ended its flow — the name is decoration, the edge is not
        }
        PickFlowWatch.NoteHandHidden(__instance, who);
    }
}
