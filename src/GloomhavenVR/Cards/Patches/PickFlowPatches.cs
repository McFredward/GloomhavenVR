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
//   OPEN  — `CardsHandUI.UpdateView(<a PICK mode>, ..., maxCardsSelected > 0, ...)`. The one
//           writer of both latched fields, so nothing can open a pick behind its back: every
//           overload of `CardsHandManager.Show` (:739 and :813), `ShowCoroutine` (:838) and the
//           push/pop restore (:1262) all end in it.
//           THE MODE TERM IS NOT DECORATION AND ITS ABSENCE COST A WHOLE TEST SESSION — see
//           `PickFlowWatch.NoteViewDriven`. The sentence that stood here read
//           "`CardsHandUI.UpdateView(..., maxCardsSelected > 0, ...)`" with no mode at all, and
//           ordinary card selection asks for TWO cards on EVERY hand in the party, every round.
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
//               BUT `AnimateCardsLost` IS NOT THE ONLY CALLER OF `Hide()`, and reading it as if
//               it were trapped the player for good — see `PickFlowWatch.NoteHandHidden` for the
//               2026-09-07 D1 deadlock and for the two terms that now tell an ANSWER from
//               `CardsHandManager.SwitchHand`'s PRESENTATION hide.
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
/// <para>Armed by the game's own <c>CardsHandUI.UpdateView</c> when it drives a hand into a
/// <see cref="CardsGameApi.IsPickMode"/> MODE that wants at least one card — the mode term is
/// load-bearing, because <c>CardHandMode.CardsSelection</c> asks every hand in the party for two
/// cards every round and used to arm this latch (2026-09-07 deadlock, see
/// <see cref="NoteViewDriven"/>); disarmed by the game's own commit callback and by the game's own
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
    /// How many OPEN edges were DECLINED because the hand belongs to a character this client does
    /// not control. This is the count that proves the construction rule below is load-bearing: in
    /// multiplayer the game opens a co-player's burn on THIS machine's copy of their
    /// <c>CardsHandUI</c> too (the peer's 2026-09-07 log recorded exactly that as
    /// <c>BURN FLOW ARM #6 … on hand 'Player handMindthief'</c>), and every one of those used to
    /// be able to seize this client's latch.
    /// </summary>
    private static int _foreignArmsDeclined;

    /// <summary>Name of the last declined hand — the change key for that line.</summary>
    private static string _lastDeclinedName = "";

    /// <summary>
    /// How many times <see cref="HealOwnerIfRefusingAnOpenPick"/> has had to correct the latch this
    /// session. ZERO is the only healthy reading — see that method.
    /// </summary>
    private static int _heals;

    /// <summary>
    /// How many <c>CardsHandUI.Hide()</c> calls on the flow-owning hand were identified as
    /// <c>CardsHandManager.SwitchHand</c> PRESENTATION hides and therefore SUSPENDED the flow
    /// instead of ending it (2026-09-07 review, D1). See <see cref="NoteHandHidden"/>.
    /// </summary>
    private static int _suspends;

    /// <summary>
    /// <c>GetInstanceID</c> of the hand whose ANSWER the player has handed the game and whose
    /// consequences have not been established yet, or 0. See <see cref="AnswerOutstandingFor"/>:
    /// this is a different fact from <see cref="_live"/> and outlives it on purpose.
    /// </summary>
    private static int _answeredOn;

    /// <summary>Unscaled time of that answer.</summary>
    private static float _answeredAt;

    /// <summary>Monotone count of answers taken this session — log decoration only.</summary>
    private static int _answerSeq;

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
    /// OPEN edge. Called from the <c>CardsHandUI.UpdateView</c> postfix with the MODE and the
    /// count the game itself passed. A pick opens only when the game asks a PICK MODE for at
    /// least one card; anything else re-drives the owning hand's view and therefore stands its
    /// flow down. Re-arming the SAME hand keeps the original open stamp so
    /// <see cref="LastFlowSeconds"/> measures the whole pick and not its last redraw.
    ///
    /// <para><b>THE MODE TERM IS THE FIX FOR THE 2026-09-07 DEADLOCK, AND THE COUNT ALONE WAS
    /// NEVER THE QUESTION.</b> This class asks "is a MODAL CARD PICK in flight" and the count
    /// answered "is the game asking for any cards at all" — which ordinary card selection does,
    /// two of them, every single round: <c>Choreographer</c>'s
    /// <c>PlayerToSelectAbilityCardsOrLongRest</c> handler runs
    /// <c>CardsHandManager.ShowCoroutine(CardHandMode.CardsSelection, …, maxCardsSelected: 2, …)</c>
    /// (Choreographer.cs:3602) and that overload drives EVERY hand in <c>cardHandsUI</c>
    /// (CardsHandManager.cs:856 in <c>ShowCoroutine</c>, :831 in the sibling <c>Show</c>), not
    /// just the local one. So the first hand in the party's list armed a "burn flow" at scenario
    /// load, and on the host that hand was the CO-PLAYER's:
    /// <c>BURN FLOW ARM #1 … at t=28.732s (frame 1655) on hand 'Player handBrute'</c>
    /// (2026-09-07 host Player.log:4587, twelve lines after the game's own <c>ShowHandManager</c>).
    /// Only the owning hand's END edges can close a flow, and the host never drives a foreign hand
    /// with a count of zero — the per-actor <c>Show</c> overload returns after the matching actor
    /// (CardsHandManager.cs:761) — so that flow stood for the WHOLE session: ZERO
    /// <c>PICK FLOW END</c> lines in 306,556 log lines. The peer's log carries five, because there
    /// the first hand IS the local character. A pick mode never opens with a foreign owner again.
    /// </para>
    ///
    /// <para><b>AND A LATER GENUINE OPEN EDGE NOW TAKES THE FLOW OVER.</b> The line that stood
    /// here was <c>if (_live) return;</c> — written for the same hand redrawing its own pick, but
    /// it also refused to move ownership when a DIFFERENT hand was genuinely asked for a pick.
    /// That is what turned a stale foreign arm into a permanent refusal of every local pick:
    /// <c>PICK GATE (LoseCard): pick=CLOSED … openEdgeOutstanding=True,
    /// flowArmedOn='Player handBrute', thisHandOwnsTheFlow=False</c> (host Player.log:289743),
    /// which is item 6b's own pre-registered falsifier firing word for word. Two hands can never
    /// be mid-pick at once (the game's confirm popup is modal), so the newer OPEN edge is by
    /// construction the real one and the standing flow is by construction stale.</para>
    /// </summary>
    internal static void NoteViewDriven(CardsHandUI? hand, CardHandMode mode, int maxCardsSelected)
    {
        // A MODAL PICK, not merely "the game wants cards": CardsSelection asks for 2 and is not a
        // pick at all. Same predicate the whole mod already gates on (CardsGameApi.PickFlowLive
        // ANDs it with the hand's latched mode), so narrowing the ARM to it narrows no consumer.
        if (maxCardsSelected > 0 && CardsGameApi.IsPickMode(mode))
        {
            int id = 0;
            string name = "?";
            try
            {
                if (hand != null)
                {
                    id = hand.GetInstanceID();
                    if (hand.gameObject != null)
                        name = hand.gameObject.name;
                }
            }
            catch (Exception)
            {
                // an unreadable hand still opened a flow — LiveFor falls back to the global answer
            }

            // ---- UNREACHABLE BY CONSTRUCTION, WHICH IS BETTER THAN DETECTED ------------------
            // A pick opened on a character this client does not CONTROL can never be a pick this
            // player is being asked to make, and this client's latch exists to answer only that
            // question. Both edge patches are on the TYPE, so in multiplayer the game drives a
            // co-player's burn through this postfix on our machine as well — that is how a foreign
            // hand came to own the flow in the first place. Declining the arm removes the whole
            // ownership-disagreement class rather than healing it after the fact; the watchdog in
            // HealOwnerIfRefusingAnOpenPick is the belt behind this brace, and its firing count is
            // the measurement that says whether this rule missed a route.
            //
            // THE REFUSAL IS ONE-WAY. An arm we cannot CLASSIFY is armed, never declined: losing a
            // genuine local pick would reproduce the very softlock this fixes, whereas an extra
            // arm is only ever a stale latch the stand-down branch below clears on the next
            // non-pick redraw of that hand.
            if (!MayThisClientPickFor(hand))
            {
                _foreignArmsDeclined++;
                if (name != _lastDeclinedName)
                {
                    _lastDeclinedName = name;
                    // HW-VERIFY: grep token "PICK ARM DECLINED". WORKING = one or more of these in
                    // any multiplayer session in which a co-player burns, discards or recovers a
                    // card, each naming THEIR hand, and NO "PICK OWNER HEAL" line anywhere in the
                    // same log. INERT = zero of these in a multiplayer session that contains a
                    // peer's own burn — the ownership test is not reaching this postfix and the
                    // 2026-09-07 deadlock's route is open again. NOT A DEFECT BY ITSELF: this line
                    // is the rule working, and its count is expected to grow with the party's
                    // activity. Single-player prints it never (IsLocalHand is unconditionally true
                    // offline), which is also the correct reading there.
                    VRLog.Note("Cards", $"PICK ARM DECLINED (#{_foreignArmsDeclined} this session): the " +
                                        $"game opened a {mode} pick for {maxCardsSelected} card(s) on " +
                                        $"'{name}', a character this client does not control, so it did " +
                                        "NOT arm this board's pick latch. A foreign hand owning the " +
                                        "latch is what deadlocked the 2026-09-07 long rest for a whole " +
                                        "session: only the owning hand's END edges can close a flow, and " +
                                        "this client never drives a foreign hand to a stand-down. The " +
                                        "peer keeps picking normally on their own machine — this line is " +
                                        "about OUR latch and nothing else.");
                }
                return; // NOT a stand-down: falling through would end OUR OWN live pick
            }

            if (_live)
            {
                if (id == 0 || _openedOn == 0 || id == _openedOn)
                    return; // the same hand redrawing its own live pick — keep the original stamp
                NoteEnd($"a pick opened on a DIFFERENT hand '{name}', so the flow standing on " +
                        $"'{_openedOnName}' was STALE and is handed over — two hands cannot be " +
                        "mid-pick at once (the game's confirm popup is modal), so the newer OPEN " +
                        "edge is the real one");
            }

            _live = true;
            _openedAt = Time.unscaledTime;
            _openedFrame = Time.frameCount;
            _openedOn = id;
            _openedOnName = name;
            _armSeq++;
            return;
        }

        // A STAND-DOWN IS ONLY FOR THE HAND THAT OPENED THE FLOW. Every non-pick Show of every
        // OTHER character's hand passes maxCardsSelected = 0, so without the ownership test a
        // co-player's ordinary card-selection redraw ended this player's live burn pick.
        if (!OwnsLiveFlow(hand))
            return;
        NoteEnd(maxCardsSelected > 0
            ? $"the game re-drove the hand's view into {mode}, which is not a pick mode " +
              "(CardsHandUI.UpdateView)"
            : "the game re-drove the hand's view asking for 0 cards (CardsHandUI.UpdateView)");
    }

    /// <summary>
    /// END edge (a): the player answered and the game's commit is about to run.
    ///
    /// <para>IT ALSO RECORDS THE ANSWER, WHICH IS A DIFFERENT FACT FROM THE FLOW ENDING and the
    /// 2026-09-07 deadlock is what taught the difference. Ending the flow says "this board has
    /// nothing to ask"; <see cref="AnswerOutstandingFor"/> says "the player has already given the
    /// game a card and the game has not visibly done anything with it yet". Only the second one
    /// can stop the mod's own long-rest pump from re-opening the very step he just answered — see
    /// <c>CardsDriver.PumpLongRestTurn</c>. The record is deliberately NOT cleared by
    /// <see cref="NoteEnd"/>: the commit edge IS an end edge, so clearing it there would make it
    /// always false.</para>
    /// </summary>
    internal static void NoteCommitAccepted(CardsHandUI? hand, int wanted)
    {
        NoteAnswerTaken(hand);
        if (!OwnsLiveFlow(hand))
            return;
        NoteEnd($"the player COMMITTED the pick — CardsHandUI.OnLoseCardClick accepted {wanted} card(s)");
    }

    /// <summary>
    /// END edge (d), NEW 2026-09-07 — <c>CardsHandUI.HandleLongRest</c>, the ONE choke point of
    /// both long-rest commit routes, and the close of a class the file header above named and
    /// deliberately left open ("It is one more disarm site the day one does").
    ///
    /// <para><b>WHY IT IS NOT COVERED BY (a).</b> Edge (a) is a prefix on
    /// <c>CardsHandUI.OnLoseCardClick</c>, which is the OWNING client's route only. Every OTHER
    /// client replays the same long rest through <c>CardsHandUI.ProxyLongRest</c>
    /// (CardsHandUI.cs:2819), which calls <c>HandleLongRest</c> DIRECTLY: it never touches
    /// <c>OnLoseCardClick</c> (so not edge a) and never calls <c>Hide()</c> (so not edge b). Its
    /// only remaining exit is the <c>ShowLongRested</c> inside <c>AnimateCardsLost</c>'s completion
    /// delegate (CardsHandUI.cs:2427-2434), and that is guarded
    /// <c>if (IsShown &amp;&amp; PhaseManager.PhaseType == ActionSelection)</c> — evaluated once,
    /// SECONDS later, after a <c>WaitUntil(animations.Count == 0)</c>. A phase that has moved on by
    /// then makes the guard false, nothing re-drives the view, and NONE of the three edges ever
    /// fires. That is a structurally reachable permanent arm on every observing client, and this
    /// edge is what closes it.</para>
    ///
    /// <para>It also fires on the owner, one call after edge (a). <see cref="NoteEnd"/> is
    /// idempotent per flow, so the second one is free.</para>
    /// </summary>
    internal static void NoteLongRestAnswered(CardsHandUI? hand)
    {
        NoteAnswerTaken(hand);
        if (!OwnsLiveFlow(hand))
            return;
        NoteEnd("the game took this hand's LONG REST answer — CardsHandUI.HandleLongRest, which " +
                "runs GameState.PlayerLongRested synchronously and then animates. This is END edge " +
                "(d) and it is the ONLY edge a non-owning client's copy of a long rest ever reaches");
    }

    /// <summary>
    /// The player has handed the game a card for this hand. Records WHICH hand and WHEN; the flow
    /// latch is a separate question (see <see cref="NoteCommitAccepted"/>'s remarks).
    /// </summary>
    private static void NoteAnswerTaken(CardsHandUI? hand)
    {
        _answeredAt = Time.unscaledTime;
        _answerSeq++;
        _answeredOn = 0;
        try
        {
            if (hand != null)
                _answeredOn = hand.GetInstanceID();
        }
        catch (Exception)
        {
            // an unreadable hand still answered — the TIME is the load-bearing half
        }
    }

    /// <summary>
    /// Has this hand handed the game an answer that the game has not visibly acted on yet, and how
    /// long ago? False when this hand has never answered. The CALLER owns the staleness rule — this
    /// only reports the fact and its age, because "long enough" is a question about the animation
    /// the caller is waiting out, not about the latch.
    /// </summary>
    internal static bool AnswerOutstandingFor(CardsHandUI? hand, out float secondsSince)
    {
        secondsSince = 0f;
        if (_answeredOn == 0 || hand == null)
            return false;
        try
        {
            if (hand.GetInstanceID() != _answeredOn)
                return false;
        }
        catch (Exception)
        {
            return false;
        }
        secondsSince = Time.unscaledTime - _answeredAt;
        return true;
    }

    /// <summary>Forget the recorded answer — the caller has established the game acted on it.</summary>
    internal static void ClearAnswer()
    {
        _answeredOn = 0;
        _answeredAt = 0f;
    }

    /// <summary>How many answers the player has handed the game this session (log decoration).</summary>
    internal static int AnswerSeq => _answerSeq;

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
    /// END edge (b): the game hid the hand that was answering — BUT ONLY WHEN THE HIDE IS AN
    /// ANSWER. For the damage burn it is the completion callback of <c>AnimateCardsLost</c>
    /// (CardsHandUI.cs:2354), i.e. the frame the card has finished burning — item 9's edge exactly.
    ///
    /// <para><b>THE SENTENCE THAT USED TO STAND HERE WAS WRONG AND IT TRAPPED THE PLAYER
    /// (2026-09-07 deadlock review, D1).</b> It said the hide "is the completion callback of
    /// <c>AnimateCardsLost</c>". That is ONE of its callers. The other is
    /// <c>CardsHandManager.SwitchHand(CPlayerActor)</c>, which calls <c>item.Hide()</c> on every
    /// NON-target hand (CardsHandManager.cs:635) — pure presentation, and it means the OPPOSITE of
    /// an answer: the pick is still open, the game still holds <c>currentMode == LoseCard</c> and
    /// <c>maxCardsSelected == 1</c>, and it is merely showing somebody else's tab.</para>
    ///
    /// <para><b>TWO ROUTES, ONE OF THEM NEEDING NO PLAYER ACTION AT ALL.</b>
    /// SINGLE PLAYER: damage opens the burn pick, the player clicks a teammate's portrait on the
    /// docked initiative track (<c>InitiativeTrackPlayerAvatar.Select</c> → <c>SwitchHand</c>,
    /// InitiativeTrackPlayerAvatar.cs:26, whose gate excludes only StartTurn /
    /// ActionSelectionPhaseStart / CheckForInitiativeAdjustments), his own hand is hidden and the
    /// flow ended. Clicking back calls <c>CardsHandUI.Show()</c>, and <c>Show()</c>
    /// (CardsHandUI.cs:481-505) does NOT call <c>UpdateView</c> except in one narrow
    /// online / not-under-my-control / <c>shortRested</c> / selection-phase branch — so the OPEN
    /// edge never re-fires, the banner, the drop field, the wanted-slot overlays and the pick fan
    /// fill all stay off, and no legal input remains.
    /// MULTIPLAYER: <c>Choreographer</c>'s <c>SelectLoseCards</c> handler runs
    /// <c>CardsHandManager.Instance.Show(m_ActorLosingCards, LoseCard|DiscardCard, …)</c>
    /// unconditionally on EVERY client (Choreographer.cs:7880), and that overload calls
    /// <c>SwitchHand(actor)</c> first (CardsHandManager.cs:757). A CO-PLAYER BEING ASKED TO DISCARD
    /// THEREFORE ENDED MY LIVE PICK, with no input from me.</para>
    ///
    /// <para><b>THE REMEDY IS A SUSPEND, NOT A LIVENESS HEAL.</b> A presentation hide stands the
    /// flow down from VIEW and leaves it LIVE, so coming back to the tab resumes it — there is
    /// nothing to re-arm and no phantom pick can be conjured. (Arming liveness inside
    /// <see cref="HealOwnerIfRefusingAnOpenPick"/> instead was checked and rejected: it would
    /// re-arm a phantom pick after a long-rest commit, because <c>HandleLongRest</c>'s delegate
    /// calls <c>ShowLongRested</c> and never <c>Hide()</c>, leaving the hand active with
    /// <c>currentMode</c> latched <c>LoseCard</c>.)</para>
    ///
    /// <para><b>THE DISCRIMINATOR, AND WHY THIS ONE.</b> Two independent terms, read off the game
    /// at the instant of the hide:</para>
    /// <list type="number">
    ///   <item><description><c>CardsHandManager.Instance.CurrentHand</c>. Inside <c>SwitchHand</c>
    ///   the loop is ordered <c>OrderBy(it =&gt; it.PlayerActor != cPlayer ? 1 : 0)</c>, so the
    ///   MATCHING hand comes first and <c>currentHand = item</c> is assigned BEFORE any other hand
    ///   is hidden (CardsHandManager.cs:617-636). A hide whose <c>CurrentHand</c> is a DIFFERENT,
    ///   non-null hand is therefore a tab switch by construction, while <c>AnimateCardsLost</c>'s
    ///   own <c>Hide()</c> leaves <c>currentHand == hand</c>.</description></item>
    ///   <item><description><c>CardsHandUI.AnimatingLostCards</c>. <c>AnimateCardsLost</c> sets it
    ///   true at its head (CardsHandUI.cs:1012), invokes the completion callback that calls
    ///   <c>Hide()</c> at :1113, and only THEN clears it at :1115 — so it is TRUE for exactly the
    ///   hide that IS an answer, and it does not depend on the ordering above at
    ///   all.</description></item>
    /// </list>
    /// <para>The flow is suspended only when term 1 says "tab switch" AND term 2 says "not
    /// mid-answer". Every other reading falls through to the END edge, i.e. to the behaviour that
    /// shipped — so a wrong reading of either term costs a suspension that does not happen, never
    /// an end edge that should have fired. A prefix/postfix depth flag on
    /// <c>SwitchHand(CPlayerActor)</c> is the more explicit alternative and was NOT taken: it is a
    /// new <c>[HarmonyPatch]</c> class, <c>docs/PATCH-INVENTORY.md</c> is closed to this lane, and
    /// <c>scripts/patch-inventory.sh check</c> fails on the resulting drift. Term 2 already removes
    /// the ordering dependence that was the depth flag's only real advantage.</para>
    /// </summary>
    internal static void NoteHandHidden(CardsHandUI? hand, string who)
    {
        // ONLY THE HAND THAT OPENED THE FLOW MAY END IT. In multiplayer this client owns a
        // CardsHandUI for every character in the party and the patch is on the TYPE, so a
        // CO-PLAYER's hand being hidden used to close this player's live pick — see the class
        // remarks for the host log that recorded five of exactly those foreign edges.
        if (!OwnsLiveFlow(hand))
            return;

        if (HideIsATabSwitch(hand, out string presenting))
        {
            _suspends++;
            // HW-VERIFY: grep token "PICK FLOW SUSPENDED". WORKING = this line present whenever a
            // hand tab is switched (a portrait click, or a co-player's discard prompt) while a pick
            // of this player's is open, AND a later "BURN FLOW TEARDOWN" for the SAME flow whose
            // EndedBy is a COMMIT rather than a hide. INERT = a "BURN FLOW TEARDOWN … torn down by
            // the game HID the hand" line whose presentedHand names a DIFFERENT hand — that is D1
            // firing and this discriminator not reaching it. NOT A DEFECT BY ITSELF: a suspension
            // is the correct reading of a tab switch, and its count grows with party activity.
            VRLog.Note("Cards", $"PICK FLOW SUSPENDED (#{_suspends} this session): the game hid " +
                                $"'{who}' while a pick of this player's was still open on it, but " +
                                $"CardsHandManager is now presenting '{presenting}' and this hand " +
                                "is NOT mid-answer (AnimatingLostCards=false) — so the hide is " +
                                "CardsHandManager.SwitchHand's presentation hide " +
                                "(CardsHandManager.cs:635), NOT AnimateCardsLost's completion " +
                                "callback. The flow stays LIVE and resumes when this tab comes " +
                                "back. Treating this hide as an answer IS the 2026-09-07 D1 " +
                                "deadlock: CardsHandUI.Show() does not re-drive UpdateView, so no " +
                                "OPEN edge ever came back and the player was left with " +
                                "currentMode=LoseCard, maxCardsSelected=1 and no legal input. In " +
                                "multiplayer it needed NO player action at all — Choreographer's " +
                                "SelectLoseCards runs CardsHandManager.Show(theirActor, …) on " +
                                "every client and that calls SwitchHand first.");
            return;
        }

        NoteEnd($"the game HID the hand '{who}' — CardsHandUI.Hide, which for a damage burn is " +
                "AnimateCardsLost's completion callback, i.e. the frame the card finished burning");
    }

    /// <summary>
    /// Is this <c>CardsHandUI.Hide()</c> a PRESENTATION hide from <c>CardsHandManager.SwitchHand</c>
    /// rather than the end of an answer? See <see cref="NoteHandHidden"/> for both terms and for
    /// why an unreadable answer is NO (i.e. falls through to the END edge that shipped).
    /// </summary>
    /// <param name="presenting">Name of the hand the manager is presenting instead, for the log.</param>
    private static bool HideIsATabSwitch(CardsHandUI? hand, out string presenting)
    {
        presenting = "?";
        if (hand == null)
            return false;
        try
        {
            // Term 2 first: it is the cheaper read and it OVERRIDES term 1. A hand whose lost-card
            // animation is still running is answering, whatever the manager happens to present.
            if (hand.AnimatingLostCards)
                return false;

            CardsHandManager manager = CardsHandManager.Instance;
            if (manager == null)
                return false;
            CardsHandUI current = manager.CurrentHand;
            if (current == null || ReferenceEquals(current, hand))
                return false; // nobody else is being presented — this is the game closing the hand

            if (current.gameObject != null)
                presenting = current.gameObject.name;
            return true;
        }
        catch (Exception)
        {
            return false; // unreadable — never withhold an END edge on a guess
        }
    }

    /// <summary>How many presentation hides were suspended rather than ended this session.</summary>
    internal static int Suspends => _suspends;

    /// <summary>
    /// Name of the hand <c>CardsHandManager</c> is presenting right now, or "none"/"?" — log
    /// decoration for the teardown line, never a gate. D1's falsifier is a <c>BURN FLOW TEARDOWN</c>
    /// whose <c>EndedBy</c> is a HIDE while this names a DIFFERENT hand.
    /// </summary>
    internal static string PresentedHandName
    {
        get
        {
            try
            {
                CardsHandManager manager = CardsHandManager.Instance;
                CardsHandUI? current = manager != null ? manager.CurrentHand : null;
                if (current == null)
                    return "none";
                return current.gameObject != null ? current.gameObject.name : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }
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

    /// <summary>
    /// MAY THIS CLIENT BE THE ONE PICKING FOR THIS HAND — the mod's own existing control
    /// predicate (<see cref="CardsGameApi.IsLocalHand"/>: <c>!FFSNetwork.IsOnline ||
    /// PlayerActor.IsUnderMyControl</c>), asked in the ONE direction that is safe to act on.
    ///
    /// <para>TRUE is the answer for everything it cannot classify — a null hand, a hand whose
    /// <c>PlayerActor</c> has not been wired yet, a throw out of the network layer. Declining an
    /// arm we are unsure about would silently lose a genuine local pick, which is the softlock this
    /// whole change exists to remove; arming one we are unsure about costs at worst a stale latch
    /// that the next non-pick redraw of that hand stands down.</para>
    /// </summary>
    private static bool MayThisClientPickFor(CardsHandUI? hand)
    {
        try
        {
            if (hand == null || hand.PlayerActor == null)
                return true; // unclassifiable — never refuse on a guess
            return CardsGameApi.IsLocalHand(hand);
        }
        catch (Exception)
        {
            return true; // unclassifiable — same direction, deliberately
        }
    }

    /// <summary>
    /// THE INVARIANT: the mod must never be refusing a pick the GAME genuinely has open. User
    /// ruling 2026-09-07, on being offered a visible escape hatch instead: <em>"Nein das will ich
    /// nicht. Sorge einfach dafür das so etwas nicht vorkommt."</em> — so there is no fallback
    /// surface anywhere; the state simply may not persist. Returns true when it corrected
    /// something, which the caller turns into a rebuild so the healed frame is the SAME frame.
    ///
    /// <para><b>WHAT IT WILL AND WILL NOT TOUCH.</b> It corrects an OWNERSHIP disagreement and
    /// never a LIVENESS one. With <c>_live</c> false, a refusal is USUALLY
    /// <see cref="CardsGameApi.PickIsOpen"/> correctly reading a mode/count pair the game left
    /// latched behind a finished flow — the 2026-09-05 items 9+10 defect, where the banner stood
    /// 87 s past the burn. Healing that would put the defect straight back, so the first term below
    /// is <c>_live</c> and the method is blind to everything else.</para>
    ///
    /// <para><b>THE SENTENCE THAT STOOD HERE — "with <c>_live</c> false there is no outstanding
    /// OPEN edge at all" — WAS FALSE, and the 2026-09-07 review named the direction (D1).</b> A
    /// <c>SwitchHand</c> presentation hide used to run END edge (b) on a pick the game still had
    /// open, and <c>CardsHandUI.Show()</c> does not re-drive <c>UpdateView</c>, so <c>_live</c>
    /// went false with a genuine OPEN edge outstanding and no route back. THAT IS FIXED AT THE
    /// EDGE, IN <see cref="NoteHandHidden"/>, AND DELIBERATELY NOT HERE: a liveness heal in this
    /// method would re-arm a PHANTOM pick after a long-rest commit, because
    /// <c>CardsHandUI.HandleLongRest</c>'s completion delegate calls <c>ShowLongRested</c> and
    /// never <c>Hide()</c> (CardsHandUI.cs:2427-2434), leaving the hand ACTIVE with
    /// <c>currentMode</c> latched <c>LoseCard</c> and <c>maxCardsSelected</c> at 1 — all four
    /// terms below would hold and this method would ask the player to burn a second card. The
    /// first term stays <c>_live</c>; the fix belongs where the false end edge is.</para>
    ///
    /// <para><b>IT DOES NOT VALIDATE ITSELF.</b> The decision to hand the flow over is taken from
    /// the GAME's own state on the hand the board is presenting — its latched pick mode, its
    /// <c>maxCardsSelected</c>, its <c>ShowOrHideInternal</c> active bit and its
    /// <c>IsUnderMyControl</c>. The only thing our own latch contributes is the single fact that is
    /// wrong by definition when all four of those hold: that some OTHER hand owns the flow. This
    /// promotes <see cref="CardsGameApi.PickHandIsPresented"/> from the report-only measurement
    /// item 10 shipped it as — "so the NEXT round can see whether this bit tracks the flow better
    /// than the count did, WITHOUT a build having bet on it" — to a gate term. It is the next
    /// round, and this is the bet, stated rather than slipped in: it can only make the watchdog
    /// fire LESS, so a wrong reading of it costs a heal that does not happen, never a heal that
    /// should not have.</para>
    ///
    /// <para><b>IT WRITES NO GAME STATE.</b> Only this class's own fields move.</para>
    ///
    /// <para><b>IT SHOULD NEVER FIRE.</b> The arm-side construction rule above (a foreign hand can
    /// no longer take the latch) removes the only route the 2026-09-07 log exhibits. A firing means
    /// a route nobody has enumerated, and its line says so.</para>
    /// </summary>
    internal static bool HealOwnerIfRefusingAnOpenPick(CardsHandUI? hand)
    {
        if (hand == null || !_live)
            return false; // no OPEN edge outstanding: the refusal is a LIVENESS answer and CORRECT
        if (LiveFor(hand))
            return false; // this hand already owns the flow — nothing is being refused

        CardHandMode mode;
        int want;
        bool shown;
        bool mine;
        try
        {
            mode = CardsGameApi.Mode(hand);
            if (!CardsGameApi.IsPickMode(mode))
                return false; // the game has no pick open on this hand
            want = hand.MaxSelectedCards;
            if (want <= 0)
                return false; // …and is asking it for no cards
            shown = CardsGameApi.PickHandIsPresented(hand);
            if (!shown)
                return false; // the game is not even showing this hand
            mine = CardsGameApi.IsLocalHand(hand);
            if (!mine)
                return false; // not ours to pick for — a refusal here is correct
        }
        catch (Exception)
        {
            return false; // an unreadable hand is never evidence that we are refusing wrongly
        }

        string stale = _openedOnName;
        int id = 0;
        string name = "?";
        try
        {
            id = hand.GetInstanceID();
            if (hand.gameObject != null)
                name = hand.gameObject.name;
        }
        catch (Exception)
        {
            // the heal still has to happen; the names are decoration
        }

        NoteEnd($"the OWNER WATCHDOG released it — the game had a {mode} pick open on '{name}' " +
                $"while this flow was standing on '{stale}'");

        _live = true;
        _openedAt = Time.unscaledTime;
        _openedFrame = Time.frameCount;
        _openedOn = id;
        _openedOnName = name;
        _armSeq++;
        _heals++;

        // HW-VERIFY: grep token "PICK OWNER HEAL". WORKING = the line is ABSENT from the whole log.
        // That is the only healthy reading: the arm-side rule above is supposed to make this state
        // unreachable, so this watchdog is a belt that should measure zero. ONE firing = a route to
        // foreign ownership that the construction rule does not cover, and the heal bought the
        // player his turn back while naming it. TWO OR MORE IN ONE SESSION IS A NEW DEFECT REPORT,
        // NOT A SUCCESS — it means the mod is silently carrying the session on its watchdog, and
        // the number in this line is the count to quote. Read it together with "PICK ARM DECLINED":
        // heals=0 with declines>0 is the construction rule doing the work by itself, which is what
        // this build predicts. FALSIFIER for the whole design: a heal whose stale owner is a hand
        // this client DOES control — that is not the 2026-09-07 class at all, it is our own END
        // edges failing to fire, and the fix would be in the three END patches, not here.
        VRLog.Note("Cards", $"PICK OWNER HEAL #{_heals}: the mod was REFUSING a pick the game " +
                            $"genuinely has open. The game asks '{name}' for {want} card(s) in " +
                            $"{mode} (hand shown={shown}, under my control={mine}), while this " +
                            $"board's pick latch was still standing on '{stale}' — so " +
                            "CardsGameApi.PickIsOpen answered false and the player could neither " +
                            "see the candidate pile nor place a card. The game is the authority on " +
                            "whether a pick is open, so the latch is wrong by definition: released " +
                            "and re-armed on this hand in the same frame, no game state written. " +
                            "THIS LINE SHOULD NEVER APPEAR — the arm-side control test is supposed " +
                            "to make it unreachable. Once is a route nobody enumerated; twice in " +
                            $"one session is a NEW defect report ({_foreignArmsDeclined} foreign " +
                            "arm(s) were declined this session).");
        return true;
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
        _foreignArmsDeclined = 0;
        _lastDeclinedName = "";
        _heals = 0;
        _suspends = 0;
        _answeredOn = 0;
        _answeredAt = 0f;
        _answerSeq = 0;
    }
}

/// <summary>
/// END edge (d). Verified target: <c>private void HandleLongRest(CAbilityCard cardToBurn)</c>
/// (CardsHandUI.cs:2423) — the single choke point of BOTH long-rest commit routes: the owning
/// client's <c>OnLoseCardClick</c> long-rest branch (:2371) and every other client's
/// <c>ProxyLongRest</c> (:2825). Its first statement is
/// <c>GameState.PlayerLongRested(cardToBurn)</c>, so by the time this prefix returns the rules have
/// already taken the answer; everything after it is animation.
///
/// <para>Prefix rather than postfix so the edge is recorded even if the body throws — the body's
/// tail is <c>StartCoroutine(AnimateCardsLost(...))</c>, and a coroutine refused on an inactive
/// object is precisely the failure this project has already paid two builds for.</para>
/// </summary>
[HarmonyPatch(typeof(CardsHandUI), "HandleLongRest")]
internal static class CardsHandUI_HandleLongRest_PickFlowEnd
{
    private static void Prefix(CardsHandUI __instance) => PickFlowWatch.NoteLongRestAnswered(__instance);
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
    private static void Postfix(CardsHandUI __instance, CardHandMode mode, int maxCardsSelected)
        => PickFlowWatch.NoteViewDriven(__instance, mode, maxCardsSelected);
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
