using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

// CardsDriver part 6 of 6 (see CardsDriver.1.Core.cs for the split map and its rules).
// Regions: overlay gate, wanted-slot hint, initiative to-do (item 6), long-rest tracing,
// take-damage selection (task #6), long-rest turn pump, pile browse, active cards,
// dev fake hand.

internal sealed partial class CardsDriver
{
    // ---------------------------------------------------------------- overlay gate --

    // B/C: cached result of CardsGameApi.IsCardCommitBlocked, computed ONCE per tick
    // (UpdateOverlayGate, before both overlay paths) so UpdateSlotHighlight and
    // UpdateWantedSlots read the same frame-consistent verdict.
    private bool _overlayGateBlocked;

    // Throttle for the gate-flip diagnostic: at most one line per second even if the
    // signals chatter (e.g. the story queue closing one message and opening the next).
    private float _overlayGateNextLogAt;

    /// <summary>
    /// User reports B/C: while the victory/defeat ("Sieg"/"Niederlage") results window
    /// is up, the scenario-start narrator is telling the story, or the rule engine has
    /// ended the scenario, NO yellow slot overlay may be shown — cards cannot be placed.
    /// Reads the game's own blocker state via
    /// <see cref="CardsGameApi.IsCardCommitBlocked"/> (UIResultsManager.IsShown /
    /// StoryController.IsVisible+DisplayDelayInEffect /
    /// ActionProcessor.CurrentPhase==ScenarioEnded — never the mod's WorldUI), caches
    /// the verdict for this tick and logs ONE throttled diagnostic line per flip with
    /// the blocking reason so hardware logs can verify the gate.
    /// </summary>
    private void UpdateOverlayGate()
    {
        bool blocked = CardsGameApi.InScenario && CardsGameApi.IsCardCommitBlocked(out string reason);
        if (blocked == _overlayGateBlocked)
            return;
        _overlayGateBlocked = blocked;
        if (Time.unscaledTime < _overlayGateNextLogAt)
            return; // flip applied either way — only the log line is throttled
        _overlayGateNextLogAt = Time.unscaledTime + 1f;
        if (blocked)
        {
            CardsGameApi.IsCardCommitBlocked(out reason); // reason only assigned on a blocked result
            VRLog.Info("Cards", $"Overlay gate CLOSED — {reason}: wanted-slot overlays and snap glow suppressed " +
                                "(no card can be placed right now).");
        }
        else
        {
            VRLog.Info("Cards", "Overlay gate OPEN — results window / narrator dialog / scenario-end blockers " +
                                "cleared: slot overlays follow the normal placement rules again.");
        }
    }

    // ------------------------------------------------------------- wanted-slot hint --

    /// <summary>
    /// Steady "wanted slot" hint (test #28, item 2): mark the slot(s) the game is
    /// currently waiting for. Normal selection → the still-EMPTY play slot(s) the
    /// round expects a card in (both when none placed, the remaining one after the
    /// first). Single-card pick flows → the LEFT slot (then Slot2 for the rare
    /// two-card burn). Cleared once the requirement is met (both filled / readied /
    /// long or short rest chosen) or the flow ends. Cheap + change-gated in PlayTray.
    /// </summary>
    private void UpdateWantedSlots(CardsHandUI? hand)
    {
        if (!CardsConfig.WantedSlotHint.Value || !_tray.IsVisible || hand == null)
        {
            _tray.SetWantedSlots(0);
            return;
        }
        // B/C: game-state overlay gate (results window / narrator dialog / scenario end —
        // see UpdateOverlayGate). Placing is impossible, so NO wanted-slot overlay may
        // pulse — neither the CardsSelection pair nor the pick-mode positions below.
        if (_overlayGateBlocked)
        {
            _tray.SetWantedSlots(0);
            return;
        }
        // THE OVERLAY BELONGS TO THE CHARACTER ON THE BOARD, NOT TO THE ONE THE GAME IS ASKING
        // (user report 2026-08-24 — see PlacementIsOffered). Switching character during a forced
        // discard leaves the recesses pulsing "lay a card here" for a hand whose cards this board
        // does not even make grabbable.
        if (!PlacementIsOffered(hand))
        {
            _tray.SetWantedSlots(0);
            return;
        }
        // TASK #9 (BUG A/B): during a short rest the game presents a burn/redraw choice for a
        // RANDOMLY sacrificed card — shown display-only in the LEFT slot (PresentShortRestCard),
        // committed via the docked dialog. NO card placement into either slot is expected, so the
        // "wanted slot" glow must stay OFF. Without this gate the CardsSelection branch below lit
        // BOTH empty slots (a short rest is not IsShortRestSelected once its confirm ran
        // Select(false), and no card sits in _occupants — PlacePickCard leaves them empty), and the
        // pulsing teal glow behind the sacrificed card made it appear to glitch (BUG B: the user's
        // "tied to the overlay" — the glow drew over/around the display card). IsShortRestChoosing
        // is true exactly while ShortRestedCard != null, i.e. the whole burn/redraw choice.
        if (CardsGameApi.IsShortRestChoosing(hand))
        {
            _tray.SetWantedSlots(0);
            return;
        }
        // ITEM 7 (2026-09-07) — THE OVERLAYS FOR THE NEXT SELECTION ARRIVED ON TOP OF THE BURN.
        // Verbatim: "Nachdem der Spieler eine Karte im Kontext der kurzen Rast verbrannt hat,
        // sieht er direkt schon die Overlays für die kommende Auswahl - das kollidiert mit der
        // Verbennen Animation. Ich will das diese erst abgewartet wird und danach erscheinen die
        // Overlays zur weiteren Auswahl." Re-stated the round before as a SEQUENCE: "Das Spiel
        // muss warten bis die Verbrennen animation fertig abgespielt wurde, dann die
        // Fluganimation inklusive verschwinden der Karte und dann ist der Flow beendet."
        //
        // THE GATE ONE LINE UP IS THE WHOLE CAUSE, and it is not wrong so much as EARLY. Its term
        // is the game's ShortRestedCard, which CardsHandUI.FinalizeShortRest nulls at the moment
        // the player COMMITS the sacrifice (CardsHandUI.cs:973) — before the game's own 2 s
        // BurnCardTimeline has started, and a whole hold-plus-flight before the card is gone. On
        // that frame IsShortRestChoosing goes false, the mode is still CardsSelection,
        // IsSelectionReady is false and SelectionCardsStillWanted is 2, so the branch below lights
        // BOTH teal recess overlays — one of them around and behind the card that is lying there
        // burning. That is his picture.
        //
        // THIS IS NOT A SECOND WAITER. There is exactly one completion signal in the burn path and
        // it already existed before this round: TryTakeBurnFlightSlot's artwork hold
        // (CardsDriver.4.Rebuild.cs:3409), whose release term is BurnArtworkActive — the game's own
        // running CardEffects.coroutine handle ANDed with HasEffect(BurnCard|LostMode) — bounded by
        // BurnEffectStartGraceSeconds (0.5 s) and BurnEffectMaxHoldSeconds (3 s); and then the
        // flight itself, which ends when FlyToPile's completion callback parks the card. The pair
        // of sets those two states live in is (_burnHoldSince, _flyingToPile), and reading THAT
        // pair is what BoardStillOwnsACardsExit does. It is the same pair CardEnRouteToPile and
        // PileArrivalsPending's cheap exit already read for the pile LABELS
        // (CardsDriver.4.Rebuild.cs:2936 / :2973) — the deferred discard/burnt counts the user
        // already accepted are gated on precisely this, so the overlays are now late by exactly
        // the amount the pile numbers are already late by, and for the same reason. No new clock,
        // no new state, no second sequencer on one animation.
        if (BoardStillOwnsACardsExit(out int holds, out int flights))
        {
            LogOverlayHeldByExit(holds, flights);
            _tray.SetWantedSlots(0);
            return;
        }
        _overlayExitHeldSince = 0f;
        // Re-arm the change-gate with the hold: without this, a later burst whose (holds, flights)
        // pair happens to repeat an earlier one prints NOTHING, and a silent instrument is how the
        // 57.92 s latch above went unreported for a whole session.
        _loggedOverlayExit = null;
        // ITEM 6b (2026-09-06) — A DEAD FLOW MAY NOT PULSE A SLOT. His words: "Beim zweiten Schaden
        // nach einer direkten Verbrennung hat das Overlay zum Verbrennen schon geblinkt, obwohl die
        // Entscheidung noch nicht getroffen wurde". The pick branch below used to read IsPickMode
        // ALONE — the game's LATCHED CardsHandUI.currentMode, which stays LoseCard for minutes after
        // a burn because TakeDamagePanel.ResetAndHide touches no CardsHandUI at all. So the burn
        // slot kept pulsing across the whole of the next damage prompt, before that prompt had asked
        // for anything. The liveness edge is checked ONCE here, for every branch, because the mask
        // this method writes is also the mask that rides the wire (NetAvatarDriver.TickExtrasSend
        // reads PlayTray.WantedSlotMask into the board-UI record): a stale pulse on the owner is a
        // stale pulse on every observer's mirrored board, so gating it here fixes both.
        bool pickLive = PickFlowLive(hand);
        CardHandMode mode = CardsGameApi.Mode(hand);
        int mask = 0;
        if (mode == CardHandMode.CardsSelection)
        {
            // GRAB-EDGE RE-ARM (user report, hardware MP test 2026-08: "Wenn man eine Karte …
            // wieder herunternimmt, ist keine Kartenoverlay sichtbar … erst wenn man die Karte
            // loslässt. Ich möchte dass das Overlay SOFORT wieder erscheint"). A placed round
            // card that is physically LIFTED back off its recess counts as NOT placed from the
            // instant the grab starts — even though (a) PlayTray keeps it in _occupants until the
            // release routes select/unselect/swap, and (b) the GAME still counts it in
            // RoundAbilityCards (the deselect only lands on release), which makes both
            // SelectionCardsStillWanted AND IsSelectionReady report the pre-grab state for the
            // whole hold. Held occupants therefore (1) bypass the ready gate, (2) add themselves
            // back onto the wanted count, and (3) read as EMPTY for the per-slot fill below — so
            // their recess re-arms the pulsing hint on the grab edge, not the release edge.
            // MULTIPLAYER: this same _wantedMask rides the board-UI record (WantedSlotMask,
            // occupancy already flips at the grab edge via OccupiedSlotMask's IsHeld test), and a
            // board-UI change PRE-EMPTS the 5 Hz rate gate (NetAvatarDriver.TickExtrasSend) — so
            // the peer's remote board shows the overlay the same instant, per the user's "auch
            // auf dem Remote-Board" requirement.
            VRCard? occ0 = _tray.Occupant(0);
            VRCard? occ1 = _tray.Occupant(1);
            bool held0 = occ0 != null && occ0.IsHeld;
            bool held1 = occ1 != null && occ1.IsHeld;

            // The round wants up to two ability cards — mark the still-empty play
            // slots until they are filled. Off once the player chose long/short rest
            // (no cards wanted), already locked the selection in (unless a placed card is
            // being physically lifted right now — see the grab-edge note above), or the
            // selection phase ended while the mode lingered stale (item B — no wanted
            // hint after confirm).
            if (CardsGameApi.IsSelectionPhase(hand)
                && !CardsGameApi.IsLongRestSelected(hand)
                && !CardsGameApi.IsShortRestSelected(hand)
                && (held0 || held1 || !CardsGameApi.IsSelectionReady(hand)))
            {
                // TASK #4: overlays glow ONLY for positions where a card CAN actually be
                // placed. Previously every empty slot glowed unconditionally, so with ONE
                // unplayable hand card left (the last-card gate: MustRestInsteadOfPlay
                // refuses all placement — commit 1034ab6 suppressed the SNAP glow but not
                // this steady hint) BOTH overlays still pulsed. The wanted count now comes
                // from the same game state the placement gate reads —
                // min(2 - roundCards, handCards), 0 when the player must rest, the
                // maxCardsSelected remainder for extra-turn picks — and only that many
                // still-empty slots light up (1 required → 1 overlay, 0 placeable → 0).
                // A LIFTED card is still counted in roundCards by the game, so it is added
                // back onto the wanted count here (grab-edge rule above).
                int want = CardsGameApi.SelectionCardsStillWanted(hand)
                           + (held0 ? 1 : 0) + (held1 ? 1 : 0);
                if (want > 0 && (occ0 == null || held0))
                {
                    mask |= 1;
                    want--;
                }
                if (want > 0 && (occ1 == null || held1))
                    mask |= 2;
            }
        }
        else if (pickLive)
        {
            // Task #11 (a): glow EVERY still-unfilled pick position — placement order is
            // irrelevant to the game (it only counts selections), so a two-card burn
            // pulses BOTH slot overlays until both cards are laid, then none; a one-card
            // burn pulses the left slot only. Previously only the single "next" slot
            // glowed, which read as "the other slot is not a target".
            // EVENT-DISCARD BATCHING: the wanted count is the LIVE BATCH — min(2, total
            // still owed after the locked batches) — and only cards of the live batch
            // (beyond the locked prefix) count as placed, so a "discard 3" pulses both
            // slots, then (after WEITER) the left slot for the final card.
            int locked = Mathf.Clamp(_pickLockedCount, 0, _fieldCards.Count);
            int want = Mathf.Min(2, CardsGameApi.PickCardsWanted() - locked);
            for (int i = _fieldCards.Count - locked; i < want && i < 2; i++)
                mask |= 1 << i; // i ≥ 0: locked is clamped to the field count above
            // GRAB-EDGE RE-ARM (same rule as the CardsSelection branch): a pick card lifted
            // back off its recess stays in _fieldCards until the release routes the take-back,
            // so its seat re-arms the hint HERE, the instant the grab starts.
            for (int i = locked; i < _fieldCards.Count && i - locked < 2; i++)
            {
                VRCard placed = _fieldCards[i];
                if (placed != null && placed.IsHeld)
                    mask |= 1 << (i - locked);
            }
        }
        _tray.SetWantedSlots(mask);
    }

    /// <summary>Unscaled time the current exit-hold started, 0 while nothing is being held. Only
    /// <see cref="LogOverlayHeldByExit"/> writes and reads it — it is a log cadence, never a
    /// gate.</summary>
    private float _overlayExitHeldSince;

    /// <summary>Change-dedup for the exit-hold line: the (holds, flights) pair last printed.</summary>
    private (int holds, int flights)? _loggedOverlayExit;

    /// <summary>
    /// IS THE MOD STILL PHYSICALLY MOVING A CARD OFF THIS BOARD INTO A PILE? The one question
    /// item 7's overlay gate has to ask, answered from the two sets that already carry the
    /// burn path's ownership and from nothing else:
    ///
    /// <list type="bullet">
    /// <item><c>_burnHoldSince</c> — the artwork hold <see cref="TryTakeBurnFlightSlot"/> keeps a
    ///   card in WHILE the game's own burn timeline plays on it, released on
    ///   <c>BurnArtworkActive</c> going false (or the 3 s ceiling / a hand switch's
    ///   <c>FlushBurnHolds</c> / the prune, all of which land the flight rather than dropping
    ///   it).</item>
    /// <item><c>_flyingToPile</c> — the arc itself, cleared by the flight's own completion
    ///   callback the frame it parks the card.</item>
    /// </list>
    ///
    /// <para><b>IT USED TO LATCH THE OVERLAYS OFF, AND ON 2026-09-07 IT DID — FOR 57.92 s AND THEN
    /// FOR THE REST OF THE SESSION.</b> The sentence that stood here claimed it could not, on the
    /// strength of the region header above <c>CardEnRouteToPile</c>
    /// (CardsDriver.4.Rebuild.cs) — "an entry for a card nobody will ever land is not
    /// representable" — and said this method "inherits that argument whole". IT DOES NOT, AND THE
    /// ARGUMENT WAS NEVER ABOUT THIS METHOD. The header's self-healing claim is about
    /// <see cref="PileArrivalsPending"/>, which RECOMPUTES from live objects every frame; the two
    /// lines below read the raw <c>Count</c> of two bookkeeping containers. Forty lines under that
    /// header, <see cref="CardEnRouteToPile"/>'s own doc says the opposite in as many words: "the
    /// set can hold a stale entry for a card something else parked (Park cancels the fly without
    /// running the completion callback), so it is a HINT, NEVER THE TRUTH". SIX of the fourteen
    /// <c>_factory.Park</c> call sites do not drop the membership — five of them reachable with a
    /// live entry — and <c>VRCard.CancelFly</c> is documented "Does NOT run the callback", so a
    /// parked mid-flight card strands its entry for the life of the scenario. (The number read
    /// "eleven of the fourteen" until it was recounted on 2026-09-07: eight sites DO drop it. The
    /// conclusion is unchanged and the recount is recorded rather than quietly edited, because a
    /// wrong number in a paragraph that is otherwise right is how this project's false assertions
    /// survive.)</para>
    ///
    /// <para>THE MEASUREMENT (host Player.log, ModBuild 474). <c>OVERLAY HELD BY EXIT</c> fired
    /// five times all session; the last three read <c>held=55.51s</c> (:248926),
    /// <c>held=57.52s</c> (:249420) and <c>held=57.92s</c> (:249468) — against the 3.40 s ceiling
    /// (<c>BurnEffectMaxHoldSeconds</c> + <c>FlyToPileSeconds</c>) that this file's own
    /// "STILL BEYOND THE INSTRUMENT" note declares unreachable. The line is change-gated on the
    /// pair, and 9,237 further lines run to the end of the log without another one: the count was
    /// stuck at 2, not merely unsampled. Only ONE <c>PILE FLIGHT</c> existed in the window
    /// (:249417); the other two were leftovers from the discard cluster at :241957-:242071, four
    /// of whose nine locally-animated starts never logged a <c>reached the … pile — parked</c>
    /// line anywhere in the session. The hold had been standing since t≈2102 s — BEFORE
    /// <c>BURN FLOW ARM #1</c> at t=2109.684 — so the wanted-slot overlays were suppressed for the
    /// WHOLE of that pick. That is user item 1's first symptom, verbatim: <em>"Das erste komische
    /// was anders war ist, dass das Overlay nicht geblinkt ist."</em></para>
    ///
    /// <para>SO THE GATE NOW ASKS THE LIVE QUESTION AND PRUNES WHAT IT FINDS DEAD, which is the
    /// self-healing property the header promises rather than a citation of it. A membership whose
    /// card is gone, parked, or no longer flying is dropped ON SIGHT here — this is the only place
    /// that reads the set as a COUNT, so it is the only place that has to. No third set, no timer,
    /// no ledger: the same two containers, filtered by the same <c>IsFlying</c>/<c>IsParked</c>
    /// terms <see cref="CardEnRouteToPile"/> already trusts as the truth.</para>
    ///
    /// <para>DELIBERATELY NOT NARROWED TO THE SHORT-REST SACRIFICE. The card is handed to the
    /// burn path by <c>RemoveShortRestCard</c>, which nulls <c>_shortRestCard</c> in the same
    /// breath — after that the mod's only handle on it is the WIDGET key in
    /// <c>_burnHoldSince</c>, so a card-scoped question could not be asked at the moment it
    /// matters. Asking the board-wide one instead also covers the two other flights that can be
    /// in the air when a selection phase opens (a turn-clear and a pick-page exit), and for those
    /// the answer is the same one the user gave for the burn: the overlay for the NEXT choice
    /// does not belong on a board that is still clearing the LAST one.</para>
    /// </summary>
    /// <summary>Reused scratch for the stale-flight prune below (allocation-free steady state).</summary>
    private readonly List<VRCard> _exitFlightPruneScratch = new(4);

    /// <summary>
    /// How many flight memberships this gate has dropped as stale since the driver came up. It is
    /// the falsifier for the fix: a session that reaches this gate at all and reads 0 here has no
    /// leak, and a nonzero reading NAMES the leak that used to latch the overlays off.
    /// </summary>
    private int _exitFlightsPruned;

    private bool BoardStillOwnsACardsExit(out int holds, out int flights)
    {
        // PRUNE FIRST, COUNT SECOND. A membership is evidence that a flight STARTED, never that
        // one is still running: VRCard.CancelFly is documented "Does NOT run the callback", so
        // every Park that does not drop the entry itself leaves one behind for ever. Asking the
        // live objects the same way CardEnRouteToPile does makes this count self-healing instead
        // of merely claiming to be.
        if (_flyingToPile.Count > 0)
        {
            _exitFlightPruneScratch.Clear();
            foreach (VRCard flying in _flyingToPile)
            {
                // ONE TERM, AND IT IS THE ONE CardEnRouteToPile ALREADY CALLS THE TRUTH:
                // VRCard.IsFlying. Destroyed, cancelled, parked, landed — every way a flight can
                // end clears it, so "not flying" is "this exit is OVER, however it ended". Nothing
                // narrower is needed and nothing wider is safe: IsParked/IsHeld would each eat a
                // live arc in some board pose, while IsFlying cannot.
                if (flying == null || !flying.IsFlying)
                    _exitFlightPruneScratch.Add(flying!);
            }
            for (int i = 0; i < _exitFlightPruneScratch.Count; i++)
            {
                if (_flyingToPile.Remove(_exitFlightPruneScratch[i]))
                    _exitFlightsPruned++;
            }
            _exitFlightPruneScratch.Clear();
        }
        holds = _burnHoldSince.Count;
        flights = _flyingToPile.Count;
        return holds > 0 || flights > 0;
    }

    /// <summary>
    /// The item-7 falsifier. Change-gated on the (holds, flights) pair, so a hold that spans
    /// 180 frames prints once — a held instrument reading as dead is a shape this project has
    /// already paid for, so the line states its own cadence and prints the elapsed hold when the
    /// pair changes again.
    /// </summary>
    private void LogOverlayHeldByExit(int holds, int flights)
    {
        float now = Time.unscaledTime;
        if (_overlayExitHeldSince <= 0f)
            _overlayExitHeldSince = now;
        var key = (holds, flights);
        if (_loggedOverlayExit.HasValue && _loggedOverlayExit.Value == key)
            return;
        _loggedOverlayExit = key;
        // HW-VERIFY: report item 7 (2026-09-07). Grep token: OVERLAY HELD BY EXIT.
        //
        // WORKING = at least one line per short-rest burn, its FIRST reading printing
        // held=0.00s with holds>=1, and the LAST reading of that burst printing a `held=` value
        // at or above the game's own 2 s BurnCardTimeline — i.e. the overlays waited out the
        // artwork AND the 0.40 s flight. The gap the item asks for is this line's `held=` at the
        // burst's end: it IS the delay between the completion signal and the overlays.
        // INERT = zero of these lines in a session that contains a `BURN HOLD:` line. The gate is
        // then not being reached and the overlays are lighting on FinalizeShortRest again, which
        // is the 472 behaviour verbatim.
        // STILL BEYOND THE INSTRUMENT = lines whose `held=` climbs past
        // BurnEffectMaxHoldSeconds + FlyToPileSeconds (3.40 s) without the burst ending. THAT
        // READING HAPPENED — 55.51 / 57.52 / 57.92 s on 2026-09-07 — and the sentence that used to
        // stand here ("nothing in the two sets can do that") was wrong; the gate read the raw set
        // COUNT while the set is only ever a hint. `staleFlightsPruned=` below is the number of
        // memberships this gate has dropped as dead; it is the direct falsifier for that fix.
        // WORKING now also requires held= to fall back under 3.40 s once a burst ends. A held=
        // above it WITH staleFlightsPruned climbing means the prune is running and something is
        // still adding faster than flights land; a held= above it with staleFlightsPruned=0 means
        // the flights really ARE live and the lead is VRCard.FlyToPile's tick, not this gate.
        VRLog.Note("Cards", $"OVERLAY HELD BY EXIT: the wanted-slot overlays for the NEXT selection are " +
                            $"suppressed because this board is still moving {holds + flights} card(s) off it " +
                            $"— {holds} inside the game's burn artwork (TryTakeBurnFlightSlot's hold, released " +
                            $"on CardEffects.coroutine going null) and {flights} in flight to a pile " +
                            $"(staleFlightsPruned={_exitFlightsPruned} this session — memberships dropped " +
                            "because VRCard.IsFlying said the arc was already over; a nonzero count IS the " +
                            "2026-09-07 leak that suppressed this player's overlays for a whole pick). " +
                            $"held={now - _overlayExitHeldSince:F2}s so far. This is the SAME completion " +
                            "signal the flight itself waits on, not a second one: the user's order is burn " +
                            "artwork, then the flight with the card disappearing, THEN the next choice's " +
                            "overlays. Change-gated on the (holds, flights) pair — one line per change, so a " +
                            "long hold looks quiet on purpose; the burst's LAST held= is the gap to read.");
    }

    // ------------------------------------------------------ pick progress + confirm routing --

    // Change-gate for UpdatePickStatus: the full input tuple of the strings it builds
    // (plus the tray root's identity, so a board switch/teardown re-pushes the banner
    // to the freshly built tray). The strings (Format/concat) are only rebuilt when
    // any input changed — never per frame.
    private (CardHandMode mode, int total, int locked, int placed, bool dialog, bool reopen,
             CPlayerActor? actor, string lang, int trayId)? _pickStatusKey;

    /// <summary>
    /// The <see cref="Patches.PickFlowWatch.EndSeq"/> value this driver has already REPORTED, so
    /// <see cref="ReportPickFlowEnd"/> prints once per flow rather than once per frame of the
    /// (permanent) cleared state. Starts at 0, which is also the watch's value before any flow has
    /// ended, so a session with no pick prints nothing.
    /// </summary>
    private int _reportedPickFlowEnd;

    /// <summary>
    /// The <see cref="Patches.PickFlowWatch.ArmSeq"/> value this driver has already reported, so
    /// <see cref="ReportBurnFlowArm"/> prints one ARM line per flow rather than one per frame.
    /// </summary>
    private int _reportedPickFlowArm;

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-05 items 9 + 10). The user's ruling was a TIMING one —
    /// "Wenn der Verbrennen-Flow vorbei ist muss sich das Controllboard zwingend sofort anpassen!"
    /// — so this line records the flow's END EDGE and the board's RE-READ as two separate
    /// timestamps and prints the gap between them. Next round "immediately" is a number, not an
    /// impression. Grep token: <c>PICK FLOW END</c>.
    ///
    /// <para>WHAT IT REPLACES. On 2026-09-05 the gap was ~87 s (7,850 frames at ~90 fps): the burn
    /// completed at <c>Player.log:233583</c> (<c>BURN CARD [owner/pile-watch]</c>, frame ~212,950)
    /// and the banner it belonged to was still up — and still being pushed to the peer — through an
    /// entire second damage prompt and its take-damage press (<c>:239872</c>), re-raising itself at
    /// <c>:240037</c>, until the CO-PLAYER'S turn boundary ran the game's own <c>SwitchHand</c> at
    /// <c>:244474</c>. The gap was not a slow re-read: nothing was re-reading, because the two terms
    /// the board consulted are latches the game never clears.</para>
    ///
    /// <para>PROOF the fix landed: one <c>PICK FLOW END</c> line per burn, with
    /// <c>boardCaughtUpAfter</c> in MILLISECONDS and <c>framesLate</c> at 0 or 1 — the board re-reads
    /// on the tick after the edge because <c>UpdatePickStatus</c> runs every frame. Read it together
    /// with the <c>Pick banner SENT: placard hidden</c> line that must follow it within the same
    /// handful of frames: that is the PEER's copy going down, which is the 1:1 half of the fix.</para>
    ///
    /// <para>FALSIFIERS — three, and they say different things. (1) NO <c>PICK FLOW END</c> line at
    /// all after a burn whose <c>BURN CARD</c> line is present: no END edge fired, so the latch is
    /// still armed and the fix is INERT — the game reached neither the commit prefix nor
    /// <c>CardsHandUI.Hide</c>, and the next suspect is a fourth exit from
    /// <c>AnimateCardsLost</c>. (2) The line present with a <c>boardCaughtUpAfter</c> of whole
    /// SECONDS: the edge is right and the re-read is genuinely slow — a different defect, in the
    /// driver's tick and not in this gate. (3) The line present and a <c>Pick banner</c> line
    /// AFTER it naming the same actor: the banner has a second writer that does not consult
    /// <see cref="CardsGameApi.PickIsOpen"/>, and the gate is too narrow rather than the latch too
    /// wide.</para>
    /// </summary>
    /// <summary>
    /// ITEM 6b (2026-09-06) — THE FLOW'S TWO EDGES, EACH WITH ITS THREE SYMPTOMS. His ruling:
    /// <em>"Gewährleiste, dass der Verbrennen-Flow nach dem erfolgreichen Verbrennen einer Karte
    /// vollständig endet. Erhält der Charakter unmittelbar danach wieder Schaden, ist das eine
    /// völlig neue Situation und hat NICHTS mehr mit dem davor zu tun."</em> Grep token:
    /// <c>BURN FLOW</c>.
    ///
    /// <para>He reported the three symptoms TOGETHER — a blinking burn overlay, a burn slot that
    /// would not take a card, and a hand fan that would not open — and they turned out to be one
    /// state read by three gates that did not consult the teardown. So one line prints all three
    /// and the reader never has to correlate: <c>overlayBlinking</c> is the wanted-slot mask this
    /// board is publishing (it also rides the wire, so it is the OBSERVER's blink as well),
    /// <c>targetArmed</c> is the tray's pick state plus the drop field's occupancy, and
    /// <c>fanCards</c> is what the hand fan is holding.</para>
    ///
    /// <para>WHAT THE READINGS MEAN. An ARM line must be followed by a TEARDOWN line for the SAME
    /// hand, and the TEARDOWN line must read <c>overlayBlinking=0 targetArmed=False/0</c> and
    /// <c>fanCards</c> equal to the character's hand size. A second damage event must then produce
    /// a FRESH ARM whose <c>armSeq</c> is one higher and which inherits nothing. A TEARDOWN with a
    /// nonzero overlay mask or an armed target is this defect back, and it names which of the three
    /// gates leaked. An ARM whose <c>hand</c> is not this player's character is the foreign-edge
    /// leak <see cref="Patches.PickFlowWatch"/>'s remarks describe, and means the ownership test
    /// let a co-player's burn through.</para>
    /// </summary>
    private void ReportBurnFlowArm()
    {
        int arm = Patches.PickFlowWatch.ArmSeq;
        if (arm == _reportedPickFlowArm)
            return;
        _reportedPickFlowArm = arm;
        // HW-VERIFY: grep token "BURN FLOW ARM". PROOF = one line per damage decision, with an
        // armSeq one higher than the previous flow's and inheritedOverlay/inheritedTarget both
        // clear — a new damage event is a new situation. FALSIFIER = an ARM line whose inherited*
        // fields are nonzero (the previous flow's teardown did not reach that surface), or an ARM
        // naming a hand this client does not control (a foreign CardsHandUI armed our latch).
        VRLog.Note("Cards", $"BURN FLOW ARM #{arm}: a modal card pick opened at " +
                            $"t={Patches.PickFlowWatch.OpenedAt:F3}s (frame " +
                            $"{Patches.PickFlowWatch.OpenedFrame}) on hand " +
                            $"'{Patches.PickFlowWatch.OpenedOnName}' — armed by the game's own " +
                            "CardsHandUI.UpdateView driving that hand into a PICK MODE and asking " +
                            "for at least one card, which is the ONE writer of " +
                            "cardHandMode/maxCardsSelected. The MODE half of that sentence is new " +
                            "in this build: without it, CardHandMode.CardsSelection (two cards, " +
                            "every hand in the party, every round) armed this line at scenario " +
                            "load on a FOREIGN hand and deadlocked every later local pick — the " +
                            "2026-09-07 host log's ARM #1 named 'Player handBrute' at t=28.7s and " +
                            "no PICK FLOW END ever followed it. INHERITED FROM THE PREVIOUS " +
                            $"FLOW (all three must be clear): inheritedOverlayBlinking=" +
                            $"{_tray.WantedSlotMask}, inheritedTargetArmed={_fieldCards.Count} " +
                            $"card(s) in the drop field, inheritedLockedBatches={_pickLockedCount}. " +
                            "A second damage event immediately after a burn is a COMPLETELY NEW " +
                            "situation (user ruling 2026-09-06) and must inherit nothing.");
    }

    /// <summary>The ARM sequence number this board has already reported as unpaired.</summary>
    private int _reportedUnpairedArm;

    /// <summary>
    /// THE PAIRING INVARIANT, AS A LINE INSTEAD OF AS A HOPE — user item 1, 2026-09-07: <em>"Finde
    /// heraus was los war und sorge dafür, dass so etwas niemals auftritt."</em>
    ///
    /// <para><b>THE PASS CONDITION IS A COUNT IDENTITY.</b> In a healthy log,
    /// <c>grep -c '] [Cards] BURN FLOW ARM'</c> equals <c>grep -c '] [Cards] PICK FLOW END'</c>,
    /// with at most ONE ARM outstanding at the end of the session (the pick the player was making
    /// when he quit). The 2026-09-07 host log read 2 and 1: <c>ARM #2</c> at t=2159.306s had no
    /// <c>END #2</c>, and finding that cost the whole session, because nothing in 9,356 further
    /// lines said "an ARM is still outstanding".</para>
    ///
    /// <para><b>WHAT AN UNPAIRED ARM NOW TRIGGERS.</b> This line, once per ARM, with its own token
    /// — <c>PICK FLOW UNPAIRED</c> — and nothing else. It is DELIBERATELY NOT A REMEDY. Standing
    /// the flow down here would be a lie in exactly the case that produced it: on 2026-09-07 the
    /// game genuinely did have that pick open (the mod's own pump had just re-opened it, see
    /// <see cref="PumpLongRestTurn"/>), and hiding the banner would have left the player with a
    /// live request and no way to see it — a degraded surface, which the standing ruling refuses.
    /// The state is made impossible at the ARM side instead; this is the falsifier that says
    /// whether that worked.</para>
    ///
    /// <para>The bound is generous on purpose. A legitimate pick can stand for a long time — flow
    /// #1 of the same session was live 47.92 s while the player read his discard pile — so this
    /// says nothing about whether the flow is WRONG, only that it is OLD and still open. Read it
    /// beside the <c>LONG REST RE-DRIVE</c> pair and the <c>BURN COMMIT HANG</c> line: those name
    /// causes, this one names the symptom and gives it a grep.</para>
    /// </summary>
    private void ReportUnpairedPickFlow()
    {
        int arm = Patches.PickFlowWatch.ArmSeq;
        if (arm == 0 || arm == _reportedUnpairedArm || arm == Patches.PickFlowWatch.EndSeq)
            return; // no flow, already said, or the ARM is paired — the healthy readings
        float live = Time.unscaledTime - Patches.PickFlowWatch.OpenedAt;
        if (live < UnpairedArmSeconds)
            return;
        _reportedUnpairedArm = arm;
        // HW-VERIFY: grep token "PICK FLOW UNPAIRED".
        // WORKING = absent, OR present at most once at the very end of a session for the pick the
        // player was actually making. The count identity above is the real reading.
        // INERT = a log with an unpaired ARM (count(BURN FLOW ARM) - count(PICK FLOW END) > 1) and
        // NO line here: this reporter is not being reached, and the next deadlock is silent again.
        // STILL BEYOND THE INSTRUMENT = this line naming a flow the player CAN still answer. It
        // cannot tell "stuck" from "slow"; it only says an ARM has stood for a long time. The line
        // that decides which is the LONG REST RE-DRIVE pair, or a BURN CARD with no END after it.
        VRLog.Note("Cards", $"PICK FLOW UNPAIRED: BURN FLOW ARM #{arm} on " +
                            $"'{Patches.PickFlowWatch.OpenedOnName}' has been standing {live:F1}s with no " +
                            $"PICK FLOW END (END seq is {Patches.PickFlowWatch.EndSeq}). THE PASS CONDITION " +
                            "FOR THIS FLOW IS A COUNT IDENTITY — every ARM has an END — and it is currently " +
                            "broken. The mod is asking this player for a card and the four END edges " +
                            "(OnLoseCardClick commit, CardsHandUI.Hide, a non-pick UpdateView on the owning " +
                            "hand, and HandleLongRest) have all not fired. This line REPORTS and repairs " +
                            "NOTHING on purpose: on 2026-09-07 the unpaired ARM was a pick the game really " +
                            "did have open, so standing the banner down would have hidden a live request " +
                            "instead of answering it. One line per ARM; it does not repeat.");
    }

    /// <summary>How long an ARM may stand unpaired before it is worth a line. Generously above the
    /// 47.92 s a real 2026-09-07 pick took, so a slow reader is never accused of a deadlock.</summary>
    private const float UnpairedArmSeconds = 90f;

    private void ReportPickFlowEnd()
    {
        int seq = Patches.PickFlowWatch.EndSeq;
        if (seq == _reportedPickFlowEnd)
            return;
        _reportedPickFlowEnd = seq;
        // HW-VERIFY: grep token "BURN FLOW TEARDOWN". PROOF = one line per flow, immediately after
        // its BURN FLOW ARM, reading overlayBlinking=0, targetArmed=False/0 cards and fanCards
        // equal to the character's hand size — the three symptoms he reported, all down, measured
        // rather than promised. FALSIFIER = any of the three nonzero here: that names exactly which
        // gate still reads the game's latched CardsHandUI.currentMode instead of
        // CardsGameApi.PickFlowLive, and it is this defect back.
        // SECOND FALSIFIER, NEW 2026-09-07 (review item D1): presentedHand. A teardown whose
        // EndedBy is "the game HID the hand" while presentedHand names a DIFFERENT hand is D1
        // firing — CardsHandManager.SwitchHand's PRESENTATION hide being read as an answer, which
        // leaves the game holding currentMode=LoseCard with no way back in. That pair must never
        // appear; PickFlowWatch.NoteHandHidden suspends instead, and says so with its own token
        // (PICK FLOW SUSPENDED). The suspends count below is that rule's activity reading.
        VRLog.Note("Cards", $"BURN FLOW TEARDOWN #{seq} (flow #{Patches.PickFlowWatch.ArmSeq} on " +
                            $"'{Patches.PickFlowWatch.OpenedOnName}'): torn down by " +
                            $"{Patches.PickFlowWatch.EndedBy}. PRESENTED HAND AT THE TEARDOWN — " +
                            $"presentedHand='{Patches.PickFlowWatch.PresentedHandName}' " +
                            $"(CardsHandManager.CurrentHand), suspends={Patches.PickFlowWatch.Suspends} " +
                            "this session. A hide-driven teardown naming a DIFFERENT presented hand " +
                            "is the D1 deadlock. ALL THREE SYMPTOMS AFTER THE " +
                            $"TEARDOWN — overlayBlinking={_tray.WantedSlotMask} (the wanted-slot " +
                            "mask this board publishes; it rides the board-UI record, so this is " +
                            "also every observer's mirrored blink), targetArmed=" +
                            $"{_tray.PickActive}/{_fieldCards.Count} card(s) in the drop field, " +
                            $"fanCards={_fan.Cards.Count} (the hand fan is NEVER suppressed by this " +
                            "flow any more — standing ruling 2026-09-06: it is always openable, the " +
                            "only exception being a player with no character assigned). Zero, " +
                            "False/0 and the character's hand size are the passing readings.");
        float endedAt = Patches.PickFlowWatch.EndedAt;
        float now = Time.unscaledTime;
        // HW-VERIFY: grep token "PICK FLOW END". PROOF = one line per burn with framesLate 0-1 and
        // a "Pick banner SENT: placard hidden" line just after it. FALSIFIER = no line at all after
        // a BURN CARD line (the fix is inert, no END edge fired), or boardCaughtUpAfter in whole
        // seconds (the edge is right, the re-read is slow). See this method's doc for the third.
        VRLog.Note("Cards", $"PICK FLOW END #{seq}: the pick flow ended at t={endedAt:F3}s " +
                            $"(frame {Patches.PickFlowWatch.EndedFrame}) and this board re-read it at " +
                            $"t={now:F3}s (frame {Time.frameCount}) — boardCaughtUpAfter=" +
                            $"{(now - endedAt) * 1000f:F0} ms, framesLate=" +
                            $"{Time.frameCount - Patches.PickFlowWatch.EndedFrame}. The flow had been " +
                            $"live {Patches.PickFlowWatch.LastFlowSeconds:F2}s and was ended by " +
                            $"{Patches.PickFlowWatch.EndedBy}. The banner and the CONFIRM/UNDO keycap " +
                            "overrides are down and record 7 stops riding, so every peer's mirrored " +
                            "placard goes down with it. THIS IS THE USER'S 'sofort': on 2026-09-05 the " +
                            "same gap was ~87 s because nothing was re-reading at all — the game latches " +
                            "CardsHandUI.cardHandMode and maxCardsSelected and TakeDamagePanel.ResetAndHide " +
                            "clears neither, so only the CO-PLAYER'S turn boundary ever cleared it.");
    }

    /// <summary>
    /// EVENT-DISCARD VR FLOW (pre-scenario "Begegnungen" mali, and every other modal card
    /// pick): drive the board's pick banner + the CONFIRM/UNDO keycap overrides each tick.
    /// The banner names the AFFECTED CHARACTER and the requirement in the player's language
    /// (Loc.Mod — e.g. "Seuchen Harald: Wähle 2 von 3 Karten zum Abwerfen — Schritt 1/2"),
    /// the keycap labels mirror the game's own dialog options while its confirm popup is
    /// open ("Karten abwerfen" / "Wähle eine andere Karte") and offer "WEITER" while a
    /// &gt;2-card requirement still owes a batch. Cleared whenever no pick flow is live.
    /// Cheap: a handful of int reads; every string is rebuilt only when its inputs
    /// changed (SetPickStatus change-gates on the banner text).
    /// </summary>
    // Change-gate for the item-surrender/forfeit banner (see UpdateItemDemandStatus).
    private (CPlayerActor? actor, int wanted, int selected, bool refreshing, bool loseReward,
             bool canSelect, bool fanOpen, string lang, int trayId)? _itemStatusKey;

    /// <summary>
    /// Item-surrender pick (event consume/refresh mali) AND the goal-chest forfeit (flow 1):
    /// while the game's ItemCardPicker demands items, the pick banner shows WHO owes WHAT —
    /// the picker's own game-localized hint texts when it has them (the GUI_CONSUME_ITEMS_TITLE
    /// format incl. the slot type / GUI_CHOOSE_ITEM_TO_LOSE), a Loc.Mod fallback otherwise,
    /// plus the selection progress. The forfeit banner shows on EVERY client: the deciding
    /// host sees the ask + progress; a guest sees the game's own wait-for-host tip (per-pick
    /// selection is not networked in that flow, so a guest progress count would lie). Returns
    /// true while it owns the banner (the card-pick branch then stands down). The COMMIT
    /// affordance is the item-use slot's own button ("ITEM ABGEBEN"/"BELOHNUNG ABGEBEN"),
    /// not the tray CONFIRM — so no keycap overrides are pushed here.
    /// </summary>
    private bool UpdateItemDemandStatus(CardsHandUI? hand)
    {
        ItemCardPicker? picker = null;
        CPlayerActor? actor = null;
        bool refreshing = false;
        bool loseReward = false;
        bool canSelect = true;
        if (_tray.IsVisible)
        {
            if (hand != null)
            {
                picker = CardsGameApi.OpenItemPicker(out actor, out refreshing);
                if (picker != null && (actor == null || !ReferenceEquals(hand.PlayerActor, actor)))
                    picker = null; // demand for a different (remote) actor — not ours to banner
            }
            if (picker == null)
            {
                // Flow 1 (goal-chest forfeit): no owning actor/hand — banner regardless.
                picker = CardsGameApi.OpenLoseRewardPicker(out canSelect);
                loseReward = picker != null;
                actor = null;
                refreshing = false;
            }
        }
        if (picker == null)
        {
            if (_itemStatusKey.HasValue)
            {
                _itemStatusKey = null;
                _pickStatusKey = null; // let the card branch (or the clear path) repopulate
                _tray.SetPickStatus(null, null, null);
            }
            return false;
        }

        int wanted = CardsGameApi.ItemPickWanted(picker);
        int selected = CardsGameApi.ItemPickSelectedCount(picker);
        // The fan state is part of the key: the banner carries the "open the items stack" hint
        // only while the fan is closed (2026-08-07 — the mod no longer opens it for the player,
        // so a MANDATORY demand must say out loud where the candidates are).
        bool fanOpen = _piles.ItemsBrowseOpen;
        var key = (actor, wanted, selected, refreshing, loseReward, canSelect, fanOpen,
                   Core.Loc.CurrentLanguage,
                   _tray.Root != null ? _tray.Root.GetInstanceID() : 0);
        if (_itemStatusKey.HasValue && _itemStatusKey.Value.Equals(key))
            return true;
        _itemStatusKey = key;
        _pickStatusKey = null; // the banner is ours now; a later card pick re-pushes its own

        string who = actor != null ? CardsGameApi.ActorLabel(actor) : string.Empty;
        // Forfeit wording: the picker's own hint MESSAGE is the specific ask (Show passes
        // GUI_CHOOSE_ITEM_TO_LOSE for the decider, the wait-for-host tip for a guest —
        // ItemRewardLosePicker.GetHintMessage); the surrender flows keep their TITLE first.
        string what = (loseReward
                          ? CardsGameApi.ItemPickHintMessage(picker) ?? CardsGameApi.ItemPickHintTitle(picker)
                          : CardsGameApi.ItemPickHintTitle(picker))
                      ?? Core.Loc.Mod(loseReward ? "item_lose_reward_demand"
                                    : refreshing ? "item_refresh_demand" : "item_surrender_demand");
        string line;
        if (loseReward && !canSelect)
        {
            line = what; // a guest cannot act and sees no (lying) progress — just the wait tip
        }
        else
        {
            try
            {
                line = what + " — " + string.Format(Core.Loc.Mod("pick_progress"), selected, wanted);
            }
            catch (System.FormatException)
            {
                line = $"{what} — {selected}/{wanted}";
            }
        }
        // MANDATORY demand + closed fan = the player must be told where to get the candidates.
        // The mod stopped auto-raising the item fan (see ItemsPile's class doc); this hint is what
        // replaces it, and it disappears the moment the fan is up.
        if (canSelect && !fanOpen)
            line += " — " + Core.Loc.Mod("item_fan_open_hint");
        _tray.SetPickStatus(who.Length > 0 ? who + ": " + line : line, null, null);
        return true;
    }

    // The "open the items pile" hint the MANDATORY item-demand banner appends is Loc.Mod
    // "item_fan_open_hint" and nothing else. It used to go through a private ItemFanOpenHint()
    // that probed the table (Loc.Mod returns the id itself for an unknown key) and fell back to
    // an inline EN/DE pair, because the wording landed in a build where Loc.cs was another lane's
    // file. Its own doc said "delete this fallback once the key is in the table"; the key has been
    // in Loc.cs since, so the fallback was a second, silently divergeable copy of a player-facing
    // sentence. The mod no longer raises the item fan by itself (see ItemsPile's class doc — the
    // auto-open was what made the fan unclosable), which is why a blocking demand names the
    // affordance at all.

    // Change-gate for the floating-panel decision banner (flows 2-4: doom picker /
    // distribute-points select+assign — see UpdatePanelDecisionStatus).
    private (int kind, string? title, int selected, int wanted, string lang, int trayId)? _panelStatusKey;

    /// <summary>
    /// FLOATING-PANEL decisions (flows 2-4): while the game shows the doom
    /// <c>UIAbilityCardPicker</c> (doom-slot replace / transfer-dooms) or a
    /// <c>UIScenarioDistributePointsManager</c> popup (choose-hero-to-burn-a-card /
    /// redistribute damage) — panels the WorldUI surfaces float pokeable in front of the
    /// HMD (<see cref="WorldUI.Surfaces.DoomPickerSurface"/> /
    /// <see cref="WorldUI.Surfaces.DistributePointsSurface"/>) — the board banner points
    /// the player at the floating panel and at the board CONFIRM that commits (the game's
    /// own ReadyButton: the tray keycap + ButtonCluster mirror it live, so the commit is
    /// already reachable; this banner is the wayfinding). Returns true while it owns the
    /// banner. No keycap overrides: the CONFIRM/UNDO keycaps already carry the game's own
    /// live ReadyButton/UndoButton labels ("End selection", "Reset", …) via TickStatus.
    /// </summary>
    private bool UpdatePanelDecisionStatus()
    {
        int kind = 0; // 0 none, 1 doom picker, 2 distribute select/assign
        int selected = 0, wanted = 0;
        string? title = null;
        if (_tray.IsVisible && CardsGameApi.InScenario)
        {
            if (CardsGameApi.DoomPickerState(out selected, out wanted))
                kind = 1;
            else if (CardsGameApi.DistributePanelState(out title, out _))
                kind = 2;
        }
        if (kind == 0)
        {
            if (_panelStatusKey.HasValue)
            {
                _panelStatusKey = null;
                _pickStatusKey = null; // let the card branch (or the clear path) repopulate
                _tray.SetPickStatus(null, null, null);
            }
            return false;
        }

        var key = (kind, title, selected, wanted, Core.Loc.CurrentLanguage,
                   _tray.Root != null ? _tray.Root.GetInstanceID() : 0);
        if (_panelStatusKey.HasValue && _panelStatusKey.Value.Equals(key))
            return true;
        _panelStatusKey = key;
        _pickStatusKey = null; // the banner is ours now; a later card pick re-pushes its own

        string line;
        if (kind == 1)
        {
            line = Core.Loc.Mod("doom_pick");
            try
            {
                line += " — " + string.Format(Core.Loc.Mod("pick_progress"), selected, wanted);
            }
            catch (System.FormatException)
            {
                line += $" — {selected}/{wanted}";
            }
        }
        else
        {
            // The popup's own game-localized title IS the ask ("Choose a hero to prevent the
            // damage…" / the redistribute card's name); the mod adds only the wayfinding.
            string hint = Core.Loc.Mod("panel_float_hint");
            line = !string.IsNullOrEmpty(title) ? title + " — " + hint : hint;
        }
        _tray.SetPickStatus(line, null, null);
        return true;
    }

    /// <summary>The rest census's change gate: (a rest is chosen, cards physically in the two
    /// recesses, cards the model still lists for the round). Written and read by
    /// <see cref="ReportRestBoardClear"/> alone.</summary>
    private (bool resting, int recesses, int round)? _restBoardKey;

    /// <summary>
    /// ITEM 6 (2026-09-07) — DID PRESSING A REST TAKE THE LAID CARDS OFF THIS BOARD?
    ///
    /// <para>Verbatim: "Im Test hatte der Spieler eine Karte bereits liegen und hat dann auf
    /// kurze Rast gedrückt. In diesem Moment muss das Spiel die Karten wieder vom Board abräumen
    /// die dort liegen. Selbes gilt für lange Rast."</para>
    ///
    /// <para>THE DESTINATION IS THE GAME'S, NOT A CHOICE THIS MOD MAKES, and it is the same for
    /// both rests: BACK INTO THE HAND. The long rest deselects every other selected card inside
    /// <c>CardsHandUI.OnCardSelected</c>'s <c>IsLongRest</c> branch (CardsHandUI.cs:1946-1949);
    /// the short rest does it with <c>DeselectAllCards()</c> inside <c>PerformShortRest</c>
    /// (CardsHandUI.cs:770), which the game runs on the YES of its own confirmation dialog and
    /// NOT on the press — <c>ShortRest.Select</c> only shows that dialog (ShortRest.cs:207-234).
    /// Either deselect reaches <c>OnCardDeselected</c>, whose non-long-rest arm is
    /// <c>MoveAbilityCard(RoundAbilityCards → HandAbilityCards)</c> (CardsHandUI.cs:2239-2242).
    /// Neither <c>GameState.PlayerShortRested</c> nor <c>PlayerLongRested</c> touches
    /// <c>RoundAbilityCards</c> at all. So a laid card is not discarded and not kept: it is
    /// un-chosen, and the board must show a hand card again.</para>
    ///
    /// <para>WHY THIS SHIPS AS A MEASUREMENT AND NOT AS A REMEDY, said plainly. The mod's board
    /// already follows that model move, and by two independent routes:
    /// <c>PlayTray.SyncFromGameState</c>'s eviction frees the recess the frame
    /// <c>RoundAbilityCards</c> stops naming the card, and the rebuild then either re-homes it
    /// into the fan (<c>FillHandFan</c> → <c>HandFanMember</c>, true again once the widget's
    /// <c>cardType</c> reverts to <c>Hand</c> in <c>AbilityCardUI.ToggleSelect</c>) or parks it
    /// (the zone sweep, for which <c>inTray</c> is now false). The edge is not missed either:
    /// <c>PollHandCards</c>' signature is folded over <c>HandFanMember</c> precisely so that a
    /// model move marks the driver dirty. Reading the ModBuild 472 pair does not close it and
    /// cannot: the session contains no instance of the reported gesture — every rest edge in it
    /// (host Player.log 159889 / 208746 / 209426 / 209725) is preceded by a board-UI record with
    /// <c>recess=False</c>, i.e. an EMPTY board. So the honest statement is that the evidence
    /// does not name the cause, and the thing this build adds is the reading that WOULD name it,
    /// beside the one release line the eviction now prints.</para>
    ///
    /// <para>It measures the PICTURE and the MODEL side by side on purpose. The recess count is
    /// <c>PlayTray.OccupiedSlotMask</c>'s question — does the slot anchor physically parent a
    /// live, un-held card — which is also the bit a peer's mirrored board is driven from
    /// (extension record 4), so one reading covers "the local board" and "every mirror" at once.
    /// The round count is the rules model. They are supposed to fall together.</para>
    /// </summary>
    private void ReportRestBoardClear(CardsHandUI? hand)
    {
        if (hand == null || hand.PlayerActor == null)
        {
            _restBoardKey = null;
            return;
        }
        bool resting;
        int round;
        try
        {
            resting = CardsGameApi.IsShortRestSelected(hand)
                      || CardsGameApi.IsLongRestSelected(hand)
                      || CardsGameApi.IsShortRestChoosing(hand);
            round = hand.PlayerActor.CharacterClass.RoundAbilityCards.Count;
        }
        catch (System.Exception)
        {
            return; // a half-torn hand answers nothing; the next frame re-asks
        }
        int mask = _tray.OccupiedSlotMask;
        int recesses = ((mask & 1) != 0 ? 1 : 0) + ((mask & 2) != 0 ? 1 : 0);
        var key = (resting, recesses, round);
        if (_restBoardKey.HasValue && _restBoardKey.Value == key)
            return;
        bool wasResting = _restBoardKey.HasValue && _restBoardKey.Value.resting;
        int wasRecesses = _restBoardKey.HasValue ? _restBoardKey.Value.recesses : 0;
        _restBoardKey = key;
        if (!resting && !wasResting)
            return; // ordinary card selection — not this item's window
        // HW-VERIFY: report item 6 (2026-09-07). Grep token: REST BOARD.
        //
        // WORKING = for every rest, a line with recesses>0 on the edge the rest is chosen,
        // followed within a second by one reading recesses=0 AND round=0. `left=` on that second
        // line is the count the item asks for: how many cards left the board.
        // INERT = a burst that ends on recesses>0 with round=0 — the model un-chose the cards and
        // the board kept drawing them, which is the report verbatim; the lead is then
        // PlayTray.SyncFromGameState's eviction (it prints `SLOT RELEASED` at Note tier since this
        // build, so that token's ABSENCE beside such a line says the eviction never ran) or the
        // zone sweep that should have parked the card.
        // ALSO INERT, and a different defect: a burst that ends recesses=0 with round>0 — the
        // board dropped cards the rules model still counts for the round.
        // STILL BEYOND THE INSTRUMENT = a rest taken with recesses=0 and round=0 throughout. That
        // is the ModBuild 472 session in full (every rest edge in it reads that way), and it says
        // only that the gesture was not performed — never that it works.
        VRLog.Note("Cards", $"REST BOARD ({(resting ? "rest chosen" : "rest window closed")}): " +
                            $"recesses={recesses} (physically drawn, the same bit peers mirror), " +
                            $"round={round} (CCharacterClass.RoundAbilityCards), " +
                            $"left={System.Math.Max(0, wasRecesses - recesses)} since the last reading. " +
                            "Both rests un-choose every laid card and the GAME moves it " +
                            "RoundAbilityCards → HandAbilityCards (OnCardDeselected), so the board must end " +
                            "this burst at recesses=0 AND round=0. Ending it with recesses>0 and round=0 is " +
                            "the reported defect; recesses=0 with round>0 is its mirror image and a " +
                            "different one. Change-gated on the triple, so one line per real transition.");
    }

    private void UpdatePickStatus(CardsHandUI? hand)
    {
        // ITEM 6b: BOTH EDGES ARE REPORTED BEFORE ANY EARLY RETURN. The END report used to sit
        // inside the "nothing live wants the banner" arm below, so a flow that ended while the
        // item-surrender or floating-panel banner owned the placard was counted but never printed.
        // These two are seq-gated (one line per edge, never per frame) and read nothing they do
        // not print.
        ReportBurnFlowArm();
        ReportPickFlowEnd();
        ReportUnpairedPickFlow();
        // ITEM 6 (2026-09-07) rides the same per-frame, pre-early-return spot for the same
        // reason: a rest edge can be taken while the item-surrender or floating-panel banner
        // owns the placard, and a census that only runs when the pick banner does would miss
        // exactly the sessions worth reading. Seq/change-gated like its two neighbours.
        ReportRestBoardClear(hand);
        if (UpdateItemDemandStatus(hand))
        {
            _exhaustedStatusKey = null; // another owner holds the placard; re-push ours when it lets go
            return;                     // the item-surrender demand owns the banner while its picker is open
        }
        if (UpdatePanelDecisionStatus())
        {
            _exhaustedStatusKey = null;
            return;                     // a floating-panel decision (doom / distribute) owns the banner
        }
        // THE PICK OVERLAY IS SHOWN ONLY WHERE A CARD CAN ACTUALLY BE LAID DOWN (user report
        // 2026-08-24) — see PlacementIsOffered for the rule and its evidence. The banner, the
        // CONFIRM/UNDO keycap overrides and the recess hint all stand or fall together: they are
        // one promise, and half of it would be worse than none.
        // ITEM 11d: ...AND THE GAME MUST ACTUALLY BE ASKING. IsPickMode reads
        // CardsHandUI.currentMode, which the game LATCHES: it stays LoseCard after the damage
        // decision closes (the same latch this file's ActionSelection branch documents for the
        // played cards). Without the count term the banner outlived its flow, and
        // PickCardsWanted()'s `max > 0 ? max : 2` floor then fabricated a requirement out of the
        // game's own zero - hardware log 2026-09-05: `Pick banner: "Testo: Waehle 2 Karte(n) zum
        // Verlieren - 1/2 gewaehlt"` with no pick source and no commit anywhere near it, while it
        // was no longer his turn. CardsGameApi.PickIsOpen reads that count RAW, with no floor.
        if (hand == null || !_tray.IsVisible || !PickFlowLive(hand)
            || !PlacementIsOffered(hand))
        {
            if (_pickStatusKey.HasValue)
            {
                _pickStatusKey = null;
                _tray.SetPickStatus(null, null, null);
            }
            // LAST OWNER, AND ONLY WHEN NOTHING LIVE WANTS THE PLACARD: an exhausted character's
            // standing state. Deliberately below every decision above — a live ask always outranks
            // a statement of fact, and this one is true for the rest of the scenario.
            UpdateExhaustedStatus(hand);
            return;
        }

        _exhaustedStatusKey = null; // a live card pick owns the placard from here on
        CardHandMode mode = CardsGameApi.Mode(hand);
        int total = CardsGameApi.PickCardsWanted();
        int locked = Mathf.Clamp(_pickLockedCount, 0, _fieldCards.Count);
        int batchWant = Mathf.Clamp(total - locked, 0, 2);
        int placed = Mathf.Max(0, _fieldCards.Count - locked);
        bool dialogOpen = CardsGameApi.IsPickConfirmDialogOpen(hand);
        var key = (mode, total, locked, placed, dialogOpen, _pickReopenBusy,
                   hand.PlayerActor, Core.Loc.CurrentLanguage,
                   _tray.Root != null ? _tray.Root.GetInstanceID() : 0);
        if (_pickStatusKey.HasValue && _pickStatusKey.Value.Equals(key))
            return; // nothing changed — keep the tray's cached strings
        _pickStatusKey = key;
        string who = hand.PlayerActor != null ? CardsGameApi.ActorLabel(hand.PlayerActor) : string.Empty;

        string verb = mode switch
        {
            CardHandMode.DiscardCard => Core.Loc.Mod("pick_verb_discard"),
            CardHandMode.LoseCard => Core.Loc.Mod("pick_verb_lose"),
            CardHandMode.RecoverDiscardedCard or CardHandMode.RecoverLostCard => Core.Loc.Mod("pick_verb_recover"),
            _ => Core.Loc.Mod("pick_verb_select"),
        };
        int totalSteps = total > 2 ? (total + 1) / 2 : 1;
        int step = Mathf.Clamp(locked / 2 + 1, 1, totalSteps);

        string banner;
        string? confirmLabel = null;
        string? undoLabel = null;
        string occasionTerm = "not resolved — the game's confirm popup owns the banner";
        if (dialogOpen)
        {
            // All required cards are selected — the game's confirm popup is up. The tray
            // CONFIRM presses its commit option, UNDO its "choose another card"; both
            // keycaps carry the popup's OWN (game-localized) option labels.
            banner = Compose(who, Core.Loc.Mod("pick_confirm_hint"));
            confirmLabel = CardsGameApi.PickDialogOptionLabel(cancel: false)
                           ?? Core.Loc.Game("GUI_CONFIRM", "Confirm");
            undoLabel = CardsGameApi.PickDialogOptionLabel(cancel: true)
                        ?? Core.Loc.Game("GUI_CHOOSE_OTHER_CARD", "Choose another card");
        }
        else
        {
            string line;
            try
            {
                line = totalSteps > 1
                    ? string.Format(Core.Loc.Mod("pick_status_step"), batchWant, total, verb, step, totalSteps)
                    : string.Format(Core.Loc.Mod("pick_status"), total, verb);
                if (placed > 0 && batchWant > 0)
                    line += " — " + string.Format(Core.Loc.Mod("pick_progress"), placed, batchWant);
            }
            catch (System.FormatException)
            {
                line = $"{placed}/{total}"; // a malformed Loc entry must never kill the tick
            }
            // THE OCCASION, WHEN THE GAME NAMES IT. Deliberately on the ASK and not on
            // pick_confirm_hint above: the ask is the line the player reads while he is wondering
            // WHY he is being asked, and the confirm hint is already the longest composed German
            // line on this wire record (107 B with a 5-char name) — leaving it untouched means
            // this clause cannot move it any closer to the 160-byte cap.
            (string? occasionKey, string term) = ResolvePickOccasion(hand, mode, total);
            occasionTerm = term;
            if (occasionKey != null)
                line += " (" + Core.Loc.Mod(occasionKey) + ")";
            banner = Compose(who, line);
            // Batch lock available (>1 full batch still outstanding + the live batch is
            // full): the CONFIRM keycap becomes "WEITER" and routes to TryLockPickBatch.
            if (total - locked > 2 && placed >= 2 && !_pickReopenBusy)
                confirmLabel = Core.Loc.Mod("pick_batch_next");
        }
        _tray.SetPickStatus(banner, confirmLabel, undoLabel);

        // HW-VERIFY: report item "the banner says what to do and never why" (2026-09-07).
        // Grep token: PICK OCCASION.
        //
        // WORKING = every long-rest burn reads `occasion=long rest` with
        // `term=CCharacterClass.LongRest && GameState.InternalCurrentActor==actor`, every
        // damage-driven loss reads `occasion=damage`, and EVERY line reads `bytes=<n>/160` with
        // n < 160. In the 2026-09-07 host log the two losses at lines 243479 and 249351 were
        // BYTE-IDENTICAL; working means those two now differ in exactly this clause.
        // INERT = `occasion=<none>` on a loss the user can name, with `term=` saying which phase
        // fell through — that names the branch to add, and the clause is simply absent (the line
        // is never wrong, only terse).
        // STILL BEYOND THE INSTRUMENT = `bytes=160/160`. EncodePickBannerText truncates by
        // dropping trailing characters and logs nothing, so at exactly the cap the tail may
        // already have been eaten and this line cannot tell you what was cut; the peer-side
        // `RemotePickBanner` Alert is the reading that decides it.
        // Change-gated by _pickStatusKey above (one line per real banner change), and the KEY
        // ALREADY CARRIES the occasion transitively: mode, total, locked, placed and dialogOpen
        // move whenever the flow does. A long rest opening behind a damage loss changes `locked`
        // and `placed`, which is the transition the two identical banners came from.
        VRLog.Note("Cards", $"PICK OCCASION: mode={mode}, occasion="
            + $"{(occasionTerm.Length > 0 ? occasionTerm : "n/a")} — banner \"{banner}\" is "
            + $"{System.Text.Encoding.UTF8.GetByteCount(banner)}/{Net.NetProtocol.PickBannerTextMaxBytes} "
            + "bytes on extension record 7, the SAME string every peer's mirror of this board "
            + "draws. The occasion mirrors CardsHandUI.OnLoseCardClick's own commit branch, so it "
            + "can never name an occasion the game will not act on.");

        static string Compose(string who, string line) =>
            who.Length > 0 ? who + ": " + line : line;
    }

    /// <summary>
    /// WHY the game is asking for this card — the OCCASION, as a <see cref="Core.Loc"/> id plus
    /// the NAME of the term that resolved it, or <c>null</c> when the game names none.
    ///
    /// <para><b>THE MODE IS NOT THE OCCASION, AND THAT IS THE WHOLE DEFECT.</b>
    /// <c>CardHandMode.LoseCard</c> is the same value for a long rest's burn and for a
    /// damage-driven loss, so the composed banner read identically for both — user 2026-09-07:
    /// "Mich hat verwirrt, dass kein Overlay angezeigt wurde… Eventuell wäre es besser wenn der
    /// Text oben kurz die lange Rast erwähnt." Two losses back to back from two unrelated causes
    /// looked like one banner repeating, and a diagnosable defect looked like a stuck game.</para>
    ///
    /// <para><b>IT MIRRORS <c>CardsHandUI.OnLoseCardClick</c> (CardsHandUI.cs:2295-2410) BRANCH
    /// FOR BRANCH, DELIBERATELY.</b> That method is the ONE commit callback for every lose/discard
    /// pick, and its phase/ability/long-rest tree is the game's own classifier: whichever branch it
    /// will take is what the card loss IS. Reading the same terms in the same order means this can
    /// never name an occasion the game will not then act on — and it is why this is a read of the
    /// existing authority rather than a second, parallel notion of "why".</para>
    ///
    /// <para><b>THE OBVIOUS TERMS ARE ALL WRONG, AND THE 2026-09-07 LOG PROVES IT.</b>
    /// <c>CCharacterClass.LongRest</c> is set the moment the long rest is CHOSEN in card selection
    /// and stays set until it resolves, so it is true during a damage loss suffered in the same
    /// round — which is exactly that session: the host's damage loss at log line 243479 and the
    /// long rest's burn at 249351 BOTH read "LongRest set". <c>HasLongRested</c> is false for both.
    /// <c>LongRestTurnHand()</c> returns the hand for both. The phase split below is what separates
    /// them, and the game's own wire actions confirm it: <c>#166 BurnAvailableCard @
    /// TakeDamageConfirmation</c> for the first, <c>#168 ConfirmAction @ LongRest</c> for the
    /// second. DAMAGE IS THEREFORE TESTED BY PHASE, NEVER BY THE ABSENCE OF A LONG REST.</para>
    ///
    /// <para>An <c>ActionPhaseType</c> read would ALSO separate the two and is what the log shows
    /// most directly — but <c>Choreographer</c> sets those two phases only inside
    /// <c>if (FFSNetwork.IsOnline)</c> (Choreographer.cs:3992, :5491, :14590), so it is blind in
    /// single player. The classifier below reads <c>PhaseManager.PhaseType</c>, which the game
    /// keeps in both, and needs no online/offline split.</para>
    ///
    /// <para>WHAT IT WILL NOT NAME, on purpose. An ability-driven loss cannot be attributed
    /// further: a monster ability, a trap, a scenario-rule effect and a played card all arrive as
    /// one <c>CAbility</c> through the same branch, and nothing on the pick records which — so it
    /// says "ability" and stops. A plain short rest never opens a pick at all (the game burns a
    /// RANDOM card, CardsHandUI.cs:756); only the IMPROVED one does. Anything else returns null and
    /// the banner keeps today's wording: a clause that guesses would be a new defect of exactly the
    /// shape this one is.</para>
    /// </summary>
    private static (string? locId, string term) ResolvePickOccasion(CardsHandUI hand, CardHandMode mode, int total)
    {
        if (mode != CardHandMode.LoseCard && mode != CardHandMode.DiscardCard)
            return (null, $"<none> — mode {mode} is not a lose/discard ask");
        try
        {
            CPlayerActor? actor = hand.PlayerActor;
            CCharacterClass? klass = actor != null ? actor.CharacterClass : null;
            if (actor == null || klass == null)
                return (null, "<none> — the hand carries no CPlayerActor/CCharacterClass");

            CPhase.PhaseType phase = PhaseManager.PhaseType;
            switch (phase)
            {
                // OnLoseCardClick :2300 — the ability branch, then avoid-damage as its else.
                case CPhase.PhaseType.StartTurn:
                case CPhase.PhaseType.Action:
                case CPhase.PhaseType.EndTurn:
                case CPhase.PhaseType.EndRound:
                {
                    CAbility? ability = Choreographer.s_Choreographer != null
                        ? Choreographer.s_Choreographer.m_CurrentAbility : null;
                    if (ability != null && ability.AbilityType == CAbility.EAbilityType.LoseCards)
                        return ("pick_occasion_ability", $"phase {phase}, CAbility.EAbilityType.LoseCards");
                    if (ability != null && ability.AbilityType == CAbility.EAbilityType.DiscardCards)
                        return ("pick_occasion_ability", $"phase {phase}, CAbility.EAbilityType.DiscardCards");
                    // The else IS avoid-damage — but only where the game will actually run it:
                    // it calls Lose1HandCardToAvoidAttack / Lose2DiscardCardsToAvoidAttack on
                    // maxCardsSelected 1 or 2 and does NOTHING for any other count (:2342-2351),
                    // so any other count is a pick this branch would not commit and must not name.
                    // `total` is the SAME PickCardsWanted() the banner prints, so the clause and
                    // the count in the line beside it can never disagree.
                    if (total == 1 || total == 2)
                        return ("pick_occasion_damage", $"phase {phase}, no lose/discard ability, {total} card(s) — the avoid-damage branch");
                    return (null, $"<none> — phase {phase}, no ability and {total} card(s), a count the avoid-damage branch would not commit");
                }

                // OnLoseCardClick :2367 — the long rest, then avoid-damage as its else.
                case CPhase.PhaseType.ActionSelection:
                case CPhase.PhaseType.EndTurnLoot:
                case CPhase.PhaseType.StartRoundEffects:
                {
                    if (klass.LongRest && ReferenceEquals(GameState.InternalCurrentActor, actor))
                        return ("pick_occasion_long_rest",
                                "CCharacterClass.LongRest && GameState.InternalCurrentActor==actor");
                    if (total == 1 || total == 2)
                        return ("pick_occasion_damage", $"phase {phase}, not this actor's long rest, {total} card(s) — the avoid-damage branch");
                    return (null, $"<none> — phase {phase}, not a long rest and {total} card(s), a count the avoid-damage branch would not commit");
                }

                // OnLoseCardClick :2405 — the IMPROVED short rest, the only short rest that picks.
                case CPhase.PhaseType.SelectAbilityCardsOrLongRest:
                    if (klass.ImprovedShortRest && klass.LongRest)
                        return ("pick_occasion_short_rest",
                                "CCharacterClass.ImprovedShortRest && LongRest — the improved short rest");
                    return (null, $"<none> — phase {phase} without the improved-short-rest pair");

                default:
                    return (null, $"<none> — phase {phase} reaches no branch of OnLoseCardClick");
            }
        }
        catch (System.Exception ex)
        {
            // A banner must never be worth an exception: fall through to today's wording.
            return (null, $"<none> — {ex.GetType().Name} reading the game's phase/ability state");
        }
    }

    // Change-gate for the exhausted placard: (actor, language, tray instance). Null = we are not
    // holding the placard, which is also what makes the release below fire exactly once.
    private (int actor, string lang, int trayId)? _exhaustedStatusKey;

    /// <summary>
    /// AN EMPTY BOARD MUST SAY WHY IT IS EMPTY (user 2026-09-06 #9, and the standing rule that
    /// there is never an empty window). A dead character's board has had its cards taken by
    /// <c>CharacterFocus.BoardCarriesCards</c>, its rest discs by <c>RestControls.RestUiOffered</c>
    /// and — new this build — its card-selection commit by
    /// <c>PlayTray.SelectionCapRefusedByDeath</c>. What is left is the scenario dashboard, which is
    /// correct and which the user read as "leer": a board that is legitimately empty because its
    /// owner is dead looks exactly like a board that failed to populate. This placard is the
    /// difference, and it is the whole reason the three subtractions above are allowed to be
    /// silent.
    ///
    /// <para>IT RIDES THE EXISTING SEAM ON PURPOSE. <c>PlayTray.SetPickStatus</c> is the board's one
    /// placard, and its text is already sent verbatim as extension record
    /// <c>NetProtocol.ExtIdPickBanner</c> in the OWNER's language
    /// (<c>Net.Remote.RemotePickBanner</c>) — so every peer's mirror of this board carries the same
    /// sentence in the same place with no new wire field and no second verdict to drift. A FLAT
    /// (unmodded) player receives no extension record at all and is unaffected.</para>
    ///
    /// <para>PRECEDENCE: last. Every live decision above owns the placard first; those all clear
    /// <see cref="_exhaustedStatusKey"/> so this re-pushes the moment they let go.</para>
    /// </summary>
    private void UpdateExhaustedStatus(CardsHandUI? hand)
    {
        bool exhausted = _tray.IsVisible && hand != null
                         && !Board.CharacterFocus.BoardCarriesCards(hand);
        if (!exhausted)
        {
            if (_exhaustedStatusKey.HasValue)
            {
                _exhaustedStatusKey = null;
                _tray.SetPickStatus(null, null, null);
            }
            return;
        }

        CPlayerActor? actor = hand!.PlayerActor;
        var key = (actor != null ? Net.NetFigures.StableActorId(actor) : 0,
                   Core.Loc.CurrentLanguage,
                   _tray.Root != null ? _tray.Root.GetInstanceID() : 0);
        if (_exhaustedStatusKey.HasValue && _exhaustedStatusKey.Value.Equals(key))
            return; // already saying exactly this — SetPickStatus would no-op anyway
        _exhaustedStatusKey = key;

        string who = actor != null ? CardsGameApi.ActorLabel(actor) : string.Empty;
        string line;
        try
        {
            line = string.Format(Core.Loc.Mod("board_exhausted"), who);
        }
        catch (System.FormatException)
        {
            line = Core.Loc.Mod("board_exhausted"); // a malformed Loc entry must never kill the tick
        }
        _tray.SetPickStatus(line, null, null);
    }

    // ------------------------------------------------------- initiative to-do (item 6) --

    /// <summary>Player actors currently glowing on the initiative track (still owe cards this selection).</summary>
    private readonly HashSet<CActor> _todoHighlighted = new();
    private readonly List<CActor> _todoPending = new(8);
    private readonly List<CActor> _todoClearScratch = new(8);

    /// <summary>
    /// Item 6: during card selection, glow each player character who still needs to place ability
    /// cards on the game's OWN initiative track, so the user sees the to-do (e.g. both cards are
    /// placed for the current merc but another character still owes theirs). Reuses the native
    /// pending-player emphasis — <c>InitiativeTrackActorAvatar.PlayEffect(Active)</c>, the exact
    /// glow <c>InitiativeTrack.OnCardHover</c> applies to a not-yet-selected player
    /// (InitiativeTrack.cs:220-230) — so it matches the game's look and self-clears when the card
    /// set changes. Event-driven (recomputed each Update off the same state CardSelectionChanged /
    /// mode changes flip) and allocation-light: reused buffers, party-sized sets, and PlayEffect
    /// self-guards redundant same-effect calls so this settles to a per-frame no-op (a re-apply
    /// only fires when the game reset the entry's effect during its own refresh — self-healing).
    /// "Owes cards" = <c>RoundAbilityCards.Count &lt; 2 &amp;&amp; !LongRest</c>, the game's own
    /// not-ready core condition (CPlayerActorExtensions.IsCardSelectionReady;
    /// InitiativeTrackActorAvatar.cs:215).
    /// </summary>
    private void UpdateInitiativeTodo()
    {
        _todoPending.Clear();
        bool selecting = CardsGameApi.InScenario
                         && PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest;
        if (selecting)
        {
            CardsHandManager mgr = CardsHandManager.Instance;
            InitiativeTrack track = InitiativeTrack.Instance;
            if (mgr != null && track != null)
            {
                List<CardsHandUI> hands = mgr.CardHandsUI;
                for (int i = 0; i < hands.Count; i++)
                {
                    CardsHandUI h = hands[i];
                    CPlayerActor? actor = h != null ? h.PlayerActor : null;
                    if (actor == null)
                        continue;
                    CCharacterClass cc = actor.CharacterClass;
                    if (!cc.LongRest && cc.RoundAbilityCards.Count < 2)
                        _todoPending.Add(actor);
                }
            }
        }

        // Clear entries no longer pending (cards placed / confirmed / left selection).
        if (_todoHighlighted.Count > 0)
        {
            _todoClearScratch.Clear();
            foreach (CActor a in _todoHighlighted)
                if (!_todoPending.Contains(a))
                    _todoClearScratch.Add(a);
            for (int i = 0; i < _todoClearScratch.Count; i++)
            {
                CActor a = _todoClearScratch[i];
                _todoHighlighted.Remove(a);
                SetInitiativeTodo(a, false);
            }
        }

        // (Re-)apply the pending glow to every character still owing cards.
        for (int i = 0; i < _todoPending.Count; i++)
        {
            CActor a = _todoPending[i];
            _todoHighlighted.Add(a);
            SetInitiativeTodo(a, true);
        }
    }

    /// <summary>Item 6: drive the native initiative-track glow on one actor's entry (Active on / None off).</summary>
    private static void SetInitiativeTodo(CActor actor, bool on)
    {
        InitiativeTrack track = InitiativeTrack.Instance;
        if (track == null)
            return;
        InitiativeTrackActorBehaviour entry = track.FindInitiativeTrackActor(actor);
        InitiativeTrackActorAvatar? avatar = entry != null ? entry.Avatar : null;
        if (avatar == null)
            return;
        avatar.PlayEffect(on
            ? InitiativeTrackActorAvatar.InitiativeEffects.Active
            : InitiativeTrackActorAvatar.InitiativeEffects.None);
    }

    /// <summary>Item 6: clear every to-do glow (scenario/mode exit, hands down, teardown).</summary>
    private void ClearInitiativeTodo()
    {
        if (_todoHighlighted.Count == 0)
            return;
        foreach (CActor a in _todoHighlighted)
            SetInitiativeTodo(a, false);
        _todoHighlighted.Clear();
    }

    // ------------------------------------------------------------- long-rest tracing --

    private (CardHandMode? mode, bool selecting, CPlayerActor? actor, int actionSig)? _lastPolledMode;

    /// <summary>
    /// Deadlock safety net (test #28, item 3): the long-rest "lose a card" step enters
    /// <c>CardHandMode.LoseCard</c> during the actor's OWN turn (the Choreographer's
    /// perform-long-rest path), which need not raise any of the mod's rebuild events —
    /// without a rebuild the pick fan would never appear and the flow would deadlock.
    /// Item B extends this to the SELECTION-LOCK edge: after the player confirms,
    /// <c>CardsHandUI.currentMode</c> stays <c>CardsSelection</c> (stale) while the game
    /// phase leaves <c>SelectAbilityCardsOrLongRest</c> — a transition the raw-mode poll
    /// alone would MISS, leaving the grabbable fan bound through the enemy turn. So the
    /// change-gate also keys on <see cref="CardsGameApi.IsSelectionPhase"/> so the lock
    /// rebuild fires the moment selection ends even when the mode never changes.
    /// SECOND-CHARACTER ACTION DEADLOCK: the turn also hands off from one character to the
    /// next WITHIN <c>CardHandMode.ActionSelection</c> — the mode never changes AND
    /// <c>IsSelectionPhase</c> stays false, so neither key above catches it. Without a
    /// rebuild the mod keeps the first character's cards docked, and the game rejects every
    /// click on them (owner != Choreographer.CurrentActor, FullAbilityCard.cs:635). So the
    /// gate ALSO keys on the acting hand's <c>PlayerActor</c> and an ActionSelection context
    /// signature (<see cref="CardsGameApi.ActionSelectionSignature"/> — acting actor + the
    /// phase machine's card pair + phase), forcing a re-dock of the NEW actor's cards.
    /// Cheap: two enum/ref reads + one folded int, change-gated.
    /// </summary>
    private void PollModeChange(CardsHandUI? hand)
    {
        var state = (mode: hand != null ? CardsGameApi.Mode(hand) : (CardHandMode?)null,
                     selecting: hand != null && CardsGameApi.IsSelectionPhase(hand),
                     actor: hand != null ? hand.PlayerActor : null,
                     actionSig: CardsGameApi.ActionSelectionSignature());
        if (!_lastPolledMode.HasValue || !_lastPolledMode.Value.Equals(state))
        {
            _lastPolledMode = state;
            _dirty = true;
        }
    }

    // Change-dedup for the selection-lock diagnostic (Item B): last logged (mode, selecting).
    private bool? _loggedSelecting;

    /// <summary>
    /// Item B diagnostic (change-deduped Info): prove from the log alone WHY the
    /// CardsSelection layout is or is not interactive. When <paramref name="selecting"/>
    /// is false while the hand's mode is still <c>CardsSelection</c>, the played cards are
    /// LOCKED (confirmed / enemy turn / non-selection phase) — the exact stale-mode state
    /// that used to leave the fan bound and the cards reclaimable through the enemy turn.
    /// </summary>
    private void LogSelectionLock(CardsHandUI hand, bool selecting)
    {
        if (_loggedSelecting == selecting)
            return;
        _loggedSelecting = selecting;
        if (selecting)
            VRLog.Info("Cards", "Selection UNLOCKED — SelectAbilityCardsOrLongRest phase: hand fan grabbable, " +
                                "free slot placement live.");
        else
            VRLog.Info("Cards", $"Selection LOCKED — mode still CardsSelection but phase is " +
                                $"{PhaseManager.PhaseType} (not selection): played cards docked read-only, and the " +
                                "hand fan is SHOWN (user ruling 2026-08-08 — the cards are visible in every phase; " +
                                "the lock removes the PLACEMENT, not the sight and no longer the touch). The cards " +
                                "stay grabbable for INSPECTION (user ruling 2026-08-08 — 'aus der Hand nehmen um " +
                                "sie sich genau anzuschauen … soll niemals blockiert sein'); a release returns them " +
                                "home and no card can be played until the next card-selection phase. Poke-select " +
                                "stays off, as in every hand-fan state.");
    }

    // ------------------------------------------------- fan interaction mode (inspection) --

    /// <summary>Change-dedup for the fan-mode diagnostic: last logged (mode, refusal reason).</summary>
    private (CardFan.FanMode mode, string why)? _loggedFanMode;

    /// <summary>
    /// WHY THE LAST PLACEMENT WAS REFUSED, in one short phrase — set every rebuild by
    /// <see cref="LogFanMode"/> and quoted verbatim by the inspection-release line in
    /// <c>OnCardReleased</c>, so a hardware log shows the split the 2026-08-08 ruling asked for:
    /// the GRAB was allowed, the PLACEMENT was refused, and by WHICH gate. Never a gate itself.
    /// </summary>
    private string _placementRefusal = "none (the fan is fully interactive)";

    /// <summary>
    /// THE ONE LINE that makes "kann ich die Karte anfassen?" answerable from the log alone
    /// (user ruling 2026-08-08). Change-deduped on (mode, reason) so a per-frame rebuild is silent
    /// while every genuine transition — selection opening/closing, a turn starting, a focus switch
    /// — is logged exactly once. Names the gate that refused the PLACEMENT and states plainly that
    /// the GRAB itself was not refused, which is the whole point of the split.
    /// </summary>
    private void LogFanMode(CardsHandUI hand, CardFan.FanMode fanMode, bool readOnly, bool grabbable,
        CardHandMode mode)
    {
        // The gate that refused the placement, in the order the rebuild evaluates them.
        string why =
            fanMode == CardFan.FanMode.Interactive ? "none (the fan is fully interactive)"
            : readOnly
                ? "read-only focus view (Board.CharacterFocus.ReadOnlyView — the game presents a " +
                  $"different character; looking at '{Board.CharacterFocus.Describe(hand.PlayerActor)}')"
            : mode == CardHandMode.CardsSelection
                ? $"selection LOCKED (CardsGameApi.IsSelectionPhase false — phase {PhaseManager.PhaseType})"
            : mode == CardHandMode.ActionSelection
                ? "action turn (CardHandMode.ActionSelection — the hand is a picture while the " +
                  "character acts; cards are played from the board slots, not from the fan)"
            : $"hand mode {mode} has no placement target";
        _placementRefusal = why;

        if (_loggedFanMode.HasValue && _loggedFanMode.Value.mode == fanMode
            && _loggedFanMode.Value.why == why)
            return;
        _loggedFanMode = (fanMode, why);

        // WHOSE HAND, BESIDE WHAT THE FAN PERMITS. Report item 10 is a question about the pair, not
        // about either half: the defect was an INTERACTIVE fan over a character this client does not
        // control, and neither the mode alone nor the owner alone names it. Printed once per change,
        // on the same line, so one grep settles it.
        // HW-VERIFY: report item 10 (2026-09-07). Grep token: HAND FAN OWNER.
        VRLog.Note("Cards", $"HAND FAN OWNER: '{CardsGameApi.ActorLabel(hand.PlayerActor)}' — this "
            + $"client {(CardsGameApi.IsLocalHand(hand) ? "CONTROLS" : "does NOT control")} it, and "
            + $"the fan below is {fanMode}. THE ONLY COMBINATION THAT IS A DEFECT IS "
            + "'does NOT control' beside 'Interactive' — that is report item 10 (a teammate's "
            + "character playable from this board) and it must never appear. 'does NOT control' "
            + "beside 'Inspect' is CORRECT and is the 2026-09-07 ruling: a focused teammate's card "
            + "may be lifted and read, never placed. Note this line describes THIS board's own fan "
            + "only; the fan drawn at a PEER'S AVATAR is Net.Remote.RemoteHandFan, a different class "
            + "that holds no VRCard and no AbilityCardUI at all, so it cannot yield a real card to "
            + "anybody and never appears here.");

        switch (fanMode)
        {
            case CardFan.FanMode.Interactive:
                // HW-VERIFY: report item 10 (2026-09-07). Grep token: Hand fan INTERACTIVE.
                VRLog.Note("Cards", "Hand fan INTERACTIVE — grab, laser-pluck and slot placement are all live.");
                break;
            case CardFan.FanMode.Inspect:
                // HW-VERIFY: report item 10 (2026-09-07). Grep token: Hand fan INSPECT-ONLY.
                VRLog.Note("Cards", "Hand fan INSPECT-ONLY — the GRAB is ALLOWED (proximity, laser pluck, " +
                                    "hand-to-hand transfer: pick a card up and read it, in any phase). The " +
                                    $"PLACEMENT is refused by: {why}. A release returns the card HOME to the fan " +
                                    "with no game call whatsoever (VRCard.InspectOnly → CardsDriver." +
                                    "OnCardReleased's inspection branch, which runs before CurrentHand() is even " +
                                    "resolved). Poke-select stays off, as in every hand-fan state.");
                break;
            default:
                // HW-VERIFY: report item 10 (2026-09-07). Grep token: Hand fan PICTURE.
                VRLog.Note("Cards", "Hand fan PICTURE — neither grabbable nor laser-clickable. This is now the " +
                                    "COVERED case and the only one left: the fronts of this hand may not be " +
                                    "drawn at all (RevealGate.ShowRoundCardFronts false — the secret selection " +
                                    "window, for a character this client does not control), so there is nothing " +
                                    "to lift and the lift is refused. A FOREIGN hand OUTSIDE that window is no " +
                                    "longer a PICTURE: it reports INSPECT, by the user's 2026-09-07 ruling that " +
                                    "a focused teammate's card may be taken into the hand to be read. The " +
                                    "read-only guarantee is unchanged and still structural — it rests on " +
                                    "VRCard.InspectOnly, whose release branch runs BEFORE CurrentHand() and " +
                                    $"writes no game state. Placement gate: {why}.");
                break;
        }
    }

    // Change-dedup for the action-turn board-clear diagnostic (task #5): last logged actionTurn.
    private bool? _loggedActionTurn;

    /// <summary>
    /// Task #5 diagnostic (change-deduped Info): prove from the log alone when the two played
    /// cards are docked on the control board vs cleared during the ActionSelection phase, and
    /// which actor drove the transition. Docked only on this character's own action turn;
    /// cleared the moment an enemy (or any other actor) is up. Logged once per state change.
    /// </summary>
    private void LogActionTurnLock(CardsHandUI hand, bool actionTurn)
    {
        if (_loggedActionTurn == actionTurn)
            return;
        _loggedActionTurn = actionTurn;
        CActor? current = CardsGameApi.CurrentTurnActor();
        string who = current != null ? CardsGameApi.ActorLabel(current) : "none";
        if (actionTurn)
            VRLog.Info("Cards", $"Control board: played cards DOCKED — this character's own action turn (current actor '{who}').");
        else
            VRLog.Info("Cards", $"Control board: played cards CLEARED — own turn over, current actor is '{who}' " +
                                "(not this character): board empty during the enemy/other turn.");
    }

    // ---------------------------------------------- take-damage selection (task #6) --

    // Task #6: while a take-damage decision is open for a character THIS client controls, drive
    // the game's SELECTED actor to that character so the wrist HUD (and any other selection-
    // driven UI) show it; restore the pre-decision selection when it closes. Edge-detected so
    // the game's own selection path (InitiativeTrack.Select) fires once per open, not per frame.
    private bool _damageSelActive;
    private CActor? _damageSelSubject; // the attacked actor we forced-selected
    private CActor? _damageSelPrev;    // selection captured before we took over (restored on close)

    /// <summary>
    /// Drive/restore the game's selected actor around an open take-damage decision. When a
    /// decision opens for a locally controlled attacked character
    /// (<see cref="CardsGameApi.DrivableTakeDamageSubject"/> — MP-guarded to IsUnderMyControl),
    /// select that character through the game's own initiative-track selection seam so the wrist
    /// overlay shows it. On close, restore the previous selection — but only if our forced
    /// selection still stands, so a selection the game itself moved on to is never clobbered.
    /// Runs inside the isolated per-frame tick guard (attributed if it throws).
    /// </summary>
    private void TickTakeDamageSelection()
    {
        if (!CardsGameApi.InScenario)
        {
            _damageSelActive = false;
            _damageSelSubject = null;
            _damageSelPrev = null;
            return;
        }

        CPlayerActor? subject = CardsGameApi.DrivableTakeDamageSubject();
        if (subject != null)
        {
            if (!_damageSelActive)
            {
                _damageSelActive = true;
                _damageSelPrev = CardsGameApi.SelectedActor();
                _damageSelSubject = subject;
                if (CardsGameApi.SelectActor(subject))
                    VRLog.Info("Cards", $"Take-damage decision: selected the attacked character " +
                                        $"'{CardsGameApi.ActorLabel(subject)}' (previous selection " +
                                        $"'{(_damageSelPrev != null ? CardsGameApi.ActorLabel(_damageSelPrev) : "none")}') — " +
                                        "wrist HUD and selection-driven UI now follow it.");
            }
            else if (!ReferenceEquals(_damageSelSubject, subject))
            {
                // A new/re-targeted decision opened before the previous one released — re-point.
                _damageSelSubject = subject;
                if (CardsGameApi.SelectActor(subject))
                    VRLog.Info("Cards", $"Take-damage decision: selection re-pointed to the attacked " +
                                        $"character '{CardsGameApi.ActorLabel(subject)}'.");
            }
            else
            {
                // Re-assert if something moved selection off the attacked actor while the
                // decision is still open (SelectActor no-ops when already selected).
                CActor? cur = CardsGameApi.SelectedActor();
                if (cur == null || !ReferenceEquals(cur, subject))
                    CardsGameApi.SelectActor(subject);
            }
        }
        else if (_damageSelActive)
        {
            _damageSelActive = false;
            CActor? cur = CardsGameApi.SelectedActor();
            CActor? subj = _damageSelSubject;
            CActor? prev = _damageSelPrev;
            _damageSelSubject = null;
            _damageSelPrev = null;
            if (subj != null && cur != null && ReferenceEquals(cur, subj)
                && prev != null && !ReferenceEquals(prev, subj))
            {
                if (CardsGameApi.SelectActor(prev))
                    VRLog.Info("Cards", $"Take-damage decision closed: selection restored to " +
                                        $"'{CardsGameApi.ActorLabel(prev)}'.");
            }
            else
            {
                VRLog.Info("Cards", "Take-damage decision closed: leaving current selection as-is " +
                                    "(the game changed it, or there was no prior selection to restore).");
            }
        }
    }

    // ------------------------------------------- take-damage decision surface (2026-08-07) --

    // User report 2026-08-07: "beim ZWEITEN Schaden auf einen ANDEREN eigenen Charakter wurde die
    // Option '1 Karte verbrennen' NICHT mehr angeboten — ich musste den Schaden nehmen." The game
    // itself always OFFERS the burn choices: TakeDamagePanel.DisplayButtons only ever toggles all
    // three widgets together, and UpdateCardRemovalOptionVisuals decides pressability from live
    // model truth (TakeDamagePanel.cs:407-420, verified in decompiled/):
    //
    //     hasEnoughAvailableCards = actorToShowCardsFor.CharacterClass.HandAbilityCards.Count > 0
    //     hasEnoughDiscardedCards = actorToShowCardsFor.CharacterClass.DiscardedAbilityCards.Count > 1
    //     <toggle>.interactable    = hasEnough… && ThisPlayerHasTakeDamageControl
    //
    // The catch: that formula runs ONCE, inside Show(). Everything afterwards only WRITES the
    // states, and two game paths latch them off for the rest of the decision:
    //   * SelectItemState.Enter → TakeDamagePanel.SetDisableVisualState() saves the current states
    //     and forces all three to interactable=false plus canvasGroupVisbility.alpha=0; Exit
    //     restores the SAVED snapshot and then calls DisplayButtons(false). Re-entering that state
    //     before the matching Exit therefore snapshots the ALREADY-DISABLED state, and the restore
    //     writes "all dead" permanently — with no Show() left to recompute it. The mod drives item
    //     slots during exactly this decision (the shield place), so it can and does walk that state.
    //   * ResetAndHide races (the TAKE-DAMAGE SAFETY swallow at LogOutput.log:21510 is that same
    //     teardown arriving out of order) leave the row docked but dead.
    //
    // FIX — re-assert, do not re-implement: while the panel window is genuinely OPEN, recompute the
    // game's OWN formula every frame and write it back when it disagrees. Idempotent, no new
    // policy, no game state written beyond the three presentation flags the game itself owns, and
    // MP-safe by construction: ThisPlayerHasTakeDamageControl is a factor, so a proxy client can
    // never be handed a pressable option it must not have. The dedicated log line is the anchor the
    // next hardware test needs — it states, per change, exactly WHY each option is (un)pressable.
    private (bool avail, bool disc, bool ctrl, int hand, int discard)? _loggedDamageOptions;

    private void TickTakeDamageOptions()
    {
        TakeDamagePanel panel = Singleton<TakeDamagePanel>.IsInitialized
            ? Singleton<TakeDamagePanel>.Instance
            : null!;
        if (panel == null || !panel.IsOpen || panel.actorBeingAttacked == null)
        {
            _loggedDamageOptions = null; // next decision logs its surface afresh
            return;
        }

        CPlayerActor? cards = panel.actorToShowCardsFor;
        CCharacterClass? klass = cards != null ? cards.CharacterClass : null;
        int inHand = klass != null ? klass.HandAbilityCards.Count : 0;
        int discarded = klass != null ? klass.DiscardedAbilityCards.Count : 0;
        bool control = panel.ThisPlayerHasTakeDamageControl;
        bool wantAvail = inHand > 0 && control;      // the game's own hasEnoughAvailableCards gate
        bool wantDisc = discarded > 1 && control;    // the game's own hasEnoughDiscardedCards gate

        bool repaired = false;
        // The panel's whole widget block must be visible while the decision is open — a stale
        // SetDisableVisualState (alpha 0) makes the docked row invisible even though it is docked.
        if (panel.canvasGroupVisbility != null && panel.canvasGroupVisbility.alpha < 0.999f)
        {
            panel.canvasGroupVisbility.alpha = 1f;
            repaired = true;
        }
        repaired |= ReassertDamageOption(panel.burnAvailableCardsToggle, panel.burnAvailableCardsCanvasGroup,
            wantAvail, inHand > 0);
        repaired |= ReassertDamageOption(panel.burnDiscardedCardsToggle, panel.burnDiscardedCardsCanvasGroup,
            wantDisc, discarded > 1);
        if (panel.takeDamageButton != null)
        {
            if (!panel.takeDamageButton.gameObject.activeSelf)
            {
                panel.takeDamageButton.gameObject.SetActive(true);
                repaired = true;
            }
            if (panel.takeDamageButton.interactable != control)
            {
                panel.takeDamageButton.interactable = control;
                repaired = true;
            }
        }

        var state = (wantAvail, wantDisc, control, inHand, discarded);
        if (!repaired && _loggedDamageOptions.HasValue && _loggedDamageOptions.Value.Equals(state))
            return;
        _loggedDamageOptions = state;
        VRLog.Info("Cards", "DECISION SURFACE (take-damage): " +
                            $"burn-1-available = {(wantAvail ? "OFFERED" : "greyed")} (hand={inHand}), " +
                            $"burn-2-discarded = {(wantDisc ? "OFFERED" : "greyed")} (discard={discarded}), " +
                            $"receive-damage = {(control ? "OFFERED" : "greyed")}, takeDamageControl={control}" +
                            (repaired
                                ? " — RE-ASSERTED: the game had latched one or more of these off mid-decision " +
                                  "(SelectItemState save/restore or a ResetAndHide race); restored from the " +
                                  "panel's own formula, no policy invented."
                                : " — matches the game's own gate, nothing to repair."));
    }

    /// <summary>Write the game's own interactable/dim state back onto one take-damage option
    /// widget. Returns true when something actually had to be repaired (drives the log line).
    /// <paramref name="modelAllows"/> is the pure model half of the gate — it drives the DIM
    /// (alpha 0.7 vs 1), exactly as <c>UpdateCardRemovalOptionVisuals</c> does, so a proxy client
    /// still sees which options the character HAS while none of them are pressable.</summary>
    private static bool ReassertDamageOption(UnityEngine.UI.Toggle? toggle, CanvasGroup? group,
        bool wantInteractable, bool modelAllows)
    {
        if (toggle == null)
            return false;
        bool repaired = false;
        if (!toggle.gameObject.activeSelf)
        {
            // DisplayButtons(false) from a stale DamageScenarioState/SelectItemState exit while the
            // decision is still open — the option would be missing from the docked row entirely
            // (this is the literal "die Option wurde nicht angeboten" — "the option was not
            // offered").
            toggle.gameObject.SetActive(true);
            repaired = true;
        }
        if (toggle.interactable != wantInteractable)
        {
            toggle.interactable = wantInteractable;
            repaired = true;
        }
        float wantAlpha = modelAllows ? 1f : 0.7f;
        if (group != null && !Mathf.Approximately(group.alpha, wantAlpha))
        {
            group.alpha = wantAlpha;
            repaired = true;
        }
        return repaired;
    }

    // ---------------------------------------------------------- long-rest turn pump --

    // Long-rest stuck fix (log build 0a2767928, line 2340 ff.): when the long-rester's
    // turn arrived, the game parked itself on TWO hidden 2D widgets (the LongRest-
    // ConfirmationButton toggle + the inactive "PERFORM LONG REST" ReadyButton) and the
    // LoseCard burn step never activated — the player saw only "mode=ActionSelection,
    // halves=0" and dead board clicks. The pump below re-evaluates the FULL game state
    // every tick (never edge-detected: a transition arriving in any order re-arms it)
    // and, while the rest is pending on this actor's own turn, drives the game's own
    // two-step flow through CardsGameApi.TryAdvanceLongRestTurn — heal +2 and item
    // refresh then run natively in GameState.PlayerLongRested when the burn commits.
    //
    // ─── 2026-09-07, USER ITEM 1: THIS PUMP RE-OPENED A STEP THE PLAYER HAD ALREADY ANSWERED ───
    //
    // His words: "Ich konnte bestätigen die Karte zu verbrennen und die Animationen waren korrekt.
    // Doch danach ging es nicht weiter - das Spiel wollte weiterhin von mir dass ich Karte
    // verbrenne." The host log says it in six lines, and every one of them is this pump's:
    //
    //   248928  PICK FLOW END #1   t=2157.606s   ended by "the player COMMITTED the pick"
    //   249089  Rebuild: mode=ActionSelection            <- the game re-drove the view MID-ANIMATION
    //   249091  LONG REST FLOW: SELECTED                 <- so `losing` went false: mode != LoseCard
    //   249256  Long rest: auto-advanced — confirmation toggled
    //   249336  Long rest: auto-advanced — 'PERFORM LONG REST' ReadyButton clicked
    //   249349  BURN FLOW ARM #2   t=2159.306s           <- 1.70 s after his commit. Never ended.
    //   249415  BURN HOLD: waited 2.01s (artwork finished)  <- the FIRST burn was still playing
    //
    // THE GATE BELOW WAS THE WHOLE CAUSE. `Mode(hand) == LoseCard` is CardsHandUI.currentMode — a
    // latch this codebase documents at length, and the game clears it for the ~2 s in which it
    // animates the answer away. LongRestTurnHand()'s own terms (`LongRest && !HasLongRested`) also
    // both still hold across that window, because the rest is not resolved until the commit lands.
    // So for those two seconds every gate this pump owns reads "the rest is pending and no burn
    // step is open" — which is true of a rest that has NEVER been answered and of one that was
    // answered 1.7 s ago, and the pump could not tell them apart. It clicked PERFORM LONG REST
    // again and the game opened a SECOND burn step over the discard pile.
    //
    // THE COST IS NOT COSMETIC. Had he laid a card into that second step, he would have lost TWO
    // cards for one long rest. The standing banner was the visible half; the re-open was the
    // dangerous half.
    //
    // THE RULE NOW. An UNANSWERED rest is driven exactly as before — that is what this pump is for
    // and the original stuck-long-rest bug is unchanged. Once the player has ANSWERED, the pump
    // holds while the game is visibly acting on the answer (CardsHandUI.AnimatingLostCards, the
    // game's own flag, bounded by LongRestAnimatingCeilingSeconds because CancelAnimateCardLost
    // latches it true) and for at least LongRestAnswerSettleSeconds, and may then re-drive, saying
    // so with its own token. It is not a timer standing in for a signal: the signal is
    // PickFlowWatch.AnswerOutstandingFor, an EDGE recorded at the game's own commit choke point.
    // `ClearAnswer` at the top means a rest that resolves normally leaves nothing behind for the
    // next one.
    //
    // ─── 2026-09-07 REVIEW, D3: THE ONE-SHOT COULD BE SPENT WITHOUT EVER MAKING A GAME CALL ───
    //
    // Two shapes, both fixed above and both worth naming because they recur:
    //   (1) The retry was latched on the CLOCK (`_longRestRetryOpenedAt` + 3 s) rather than on the
    //       step it exists to perform, and execution then fell through to a
    //       `BlockingWindowModalActive` return. A blocking modal standing for those three seconds
    //       closed the window with ZERO game calls, and the retry read spent for good — the
    //       "gated remedy never ran" shape. The modal check now sits ABOVE the answer block, so a
    //       modal DEFERS the window instead of consuming it, and the window is marked spent only
    //       once TryAdvanceLongRestTurn reports the ReadyButton click itself.
    //   (2) LongRestAnswerSettleSeconds is a number about the MOD's flight, standing in for the
    //       GAME's `WaitUntil(animations.Count == 0)`. It is now a FLOOR beside the game's own
    //       animation flag rather than the whole answer. See that constant.
    private float _longRestPumpNextTry;   // throttle for the actual game calls (state reads stay per-tick)
    private bool _longRestPumpQueued;     // at most one queued advance in flight
    private bool _longRestPumpAnnounced;  // change-deduped "turn arrived" log
    private float _longRestRetryOpenedAt; // unscaled time the current post-answer re-drive opened; 0 = none
    private int _longRestHoldPhaseLogged; // 0 none, 1 "settling", 2 "spent" — one line per phase
    private int _longRestRetryAttempts;   // how many re-drive windows this answer has opened
    private bool _longRestRetryReadyClicked; // a re-drive actually reached the ReadyButton click

    /// <summary>
    /// The FLOOR under which an answered long rest is never re-driven, in seconds. It is the mod's
    /// own worst case for the flight that carries the answer — the artwork hold ceiling plus the
    /// flight.
    ///
    /// <para><b>IT IS A FLOOR AND NOT THE WAIT, AND SAYING OTHERWISE WAS THE ModBuild 475 DEFECT
    /// (2026-09-07 review, D3).</b> 3.4 s is a number about the MOD's own presentation. The wait it
    /// stands in for is the GAME's <c>WaitUntil(() =&gt; animations.Count == 0)</c>
    /// (CardsHandUI.cs:1103), bounded by serialized LeanTween durations
    /// (<c>discardedCardsMoveTime</c>, <c>postAnimationWaitTime</c>, <c>animationSpeed</c>) that
    /// this mod never reads. A constant can only ever be a guess about that, and this one is the
    /// last route by which the mod can re-answer a question the player already answered. So the
    /// gate now ALSO reads the game's own <c>AnimatingLostCards</c> flag
    /// (<see cref="CardsGameApi.AnimatingLostCards"/>): while the animation is running the pump
    /// holds however long it takes, and this constant only says how long it holds when the game is
    /// NOT visibly doing anything.</para>
    /// </summary>
    private const float LongRestAnswerSettleSeconds = BurnEffectMaxHoldSeconds + FlyToPileSeconds;

    /// <summary>
    /// The ceiling on the <c>AnimatingLostCards</c> hold, in seconds. That flag is a HOLD signal
    /// with no end of its own — <c>CancelAnimateCardLost</c> stops the coroutine without ever
    /// clearing it (CardsHandUI.cs:1154-1178) — so a latched-true flag must not be able to hold the
    /// remedy off for the rest of the session. Generous against any plausible tween chain and still
    /// far short of a lost turn.
    /// </summary>
    private const float LongRestAnimatingCeilingSeconds = 20f;

    /// <summary>
    /// How long ONE post-answer re-drive window stays open. <c>TryAdvanceLongRestTurn</c> is a
    /// two-step flow throttled to one game call every 0.5 s, so the allowance has to be a WINDOW:
    /// a single-tick allowance would toggle the long-rest confirmation and never reach the
    /// ReadyButton click, leaving the turn half-driven.
    /// </summary>
    private const float LongRestRetryWindowSeconds = 3f;

    /// <summary>
    /// How many re-drive windows one answered rest may open. A window that expires WITHOUT the
    /// ReadyButton click made no game call at all, so it re-opens rather than counting as the one
    /// remedy (D3, the "gated remedy never ran" shape); this bounds that loop. Reaching it is a
    /// defect report in its own right — see the <c>LONG REST RE-DRIVE ONCE</c> line.
    /// </summary>
    private const int LongRestRetryMaxAttempts = 3;

    private void PumpLongRestTurn()
    {
        CardsHandUI? hand = CardsGameApi.LongRestTurnHand();
        if (hand == null)
        {
            _longRestPumpAnnounced = false;
            // The rest resolved (or this actor's turn ended): nothing about the answer that
            // belonged to it may survive into the next one.
            _longRestRetryOpenedAt = 0f;
            _longRestHoldPhaseLogged = 0;
            _longRestRetryAttempts = 0;
            _longRestRetryReadyClicked = false;
            Patches.PickFlowWatch.ClearAnswer();
            return;
        }
        if (CardsGameApi.Mode(hand) == CardHandMode.LoseCard)
        {
            // The burn step IS open right now, so the pick flow owns it — but note that this term
            // is a LATCH and goes false for the seconds in which the game animates an answer away.
            // It is therefore necessary and NOT sufficient; the answer test below is the other half.
            return;
        }
        // A BLOCKING MODAL DEFERS THE WINDOW, IT DOES NOT CONSUME IT — and this check used to sit
        // BELOW the answer block, which is the "gated remedy never ran" shape (2026-09-07 review,
        // D3). A story/results modal standing for the retry window's three seconds closed the
        // window with ZERO game calls, and the retry then read spent for the rest of the turn.
        // Read from the top: nothing about the answer may be decided while the turn cannot be
        // advanced at all.
        if (WorldUI.ModalFallback.BlockingWindowModalActive)
            return; // never advance the turn under a blocking modal (story/results/…)
        if (Patches.PickFlowWatch.AnswerOutstandingFor(hand, out float sinceAnswer))
        {
            float nowAnswer = Time.unscaledTime;
            // THE RETRY IS A WINDOW, NOT A TICK. TryAdvanceLongRestTurn is a TWO-step flow
            // (confirmation toggle, then the ReadyButton click) throttled to one game call every
            // 0.5 s, so a retry that latched itself off after its FIRST tick would toggle the
            // confirmation and never click READY — a half-drive, which is worse than none.
            //
            // AND IT IS SPENT BY THE STEP, NOT BY THE CLOCK (D3). The line that stood here read
            // `retrySpent = _longRestRetryOpenedAt > 0f && now - _longRestRetryOpenedAt >= window`
            // — a latch on elapsed time, for a remedy whose whole purpose is a game call. A window
            // that expired without ever reaching the ReadyButton click (a modal, a throttle tick
            // that fell the wrong side of a frame, a ReadyButton that was not armed yet) burned
            // the one re-drive on nothing. The retry is now spent only once
            // TryAdvanceLongRestTurn has reported the READY CLICK; an expired window with no
            // click closes and may be re-opened, up to LongRestRetryMaxAttempts, which is what
            // keeps a genuinely stuck game from being re-driven for ever.
            bool retryWindowExpired = _longRestRetryOpenedAt > 0f
                                      && nowAnswer - _longRestRetryOpenedAt >= LongRestRetryWindowSeconds;
            if (retryWindowExpired && !_longRestRetryReadyClicked)
            {
                _longRestRetryOpenedAt = 0f; // no game call landed — the window is re-openable
                _longRestHoldPhaseLogged = 0;
            }
            bool retrySpent = _longRestRetryReadyClicked
                              || _longRestRetryAttempts >= LongRestRetryMaxAttempts;
            bool retryRunning = _longRestRetryOpenedAt > 0f && !retryWindowExpired;
            // THE SETTLE TERM IS THE GAME'S OWN ANIMATION FLAG NOW, NOT ONLY A CONSTANT. See
            // LongRestAnswerSettleSeconds: 3.4 s is the MOD's flight ceiling, and the wait it
            // stands in for is the game's `WaitUntil(animations.Count == 0)` (CardsHandUI.cs:1103),
            // bounded by serialized tween durations this mod cannot read. While
            // CardsHandUI.AnimatingLostCards is true the game is visibly acting on the answer and
            // the pump may not touch it, however long that takes — bounded only by
            // LongRestAnimatingCeilingSeconds, because that flag latches true for ever when
            // CancelAnimateCardLost runs (CardsHandUI.cs:1154-1178 never clears it).
            bool stillAnimating = CardsGameApi.AnimatingLostCards(hand)
                                  && sinceAnswer < LongRestAnimatingCeilingSeconds;
            if (!retryRunning
                && (sinceAnswer < LongRestAnswerSettleSeconds || stillAnimating || retrySpent))
            {
                int phase = retrySpent ? 2 : 1;
                if (_longRestHoldPhaseLogged != phase)
                {
                    _longRestHoldPhaseLogged = phase;
                    // HW-VERIFY: grep token "LONG REST RE-DRIVE HELD".
                    // WORKING = one of these per long rest the player answers, and NO
                    // "BURN FLOW ARM #n" within LongRestAnswerSettleSeconds of a "PICK FLOW END
                    // #n-1" anywhere in the log. That pair — an ARM 1.70 s after an END on the
                    // same hand — IS the 2026-09-07 deadlock and this line is what now stands
                    // between them.
                    // INERT = zero of these lines in a session that contains a long rest the
                    // player answered (grep "LONG REST FLOW: BURN step active" and a following
                    // "PICK FLOW END"): the answer edge is not reaching this gate and the pump can
                    // re-open an answered step again.
                    // STILL BEYOND THE INSTRUMENT = this line present, then "LONG REST RE-DRIVE
                    // ONCE" below it, and STILL no "LONG REST FLOW: RESOLVED" — the game did not
                    // take the answer at all, which is upstream of this mod (see the report).
                    VRLog.Note("Cards", "LONG REST RE-DRIVE HELD: this player answered his long rest " +
                                        $"{sinceAnswer:F2}s ago (answer #{Patches.PickFlowWatch.AnswerSeq}) and the " +
                                        "game has not resolved it yet, so the PERFORM LONG REST drive is HELD " +
                                        $"{(retrySpent ? $"for good — its re-drives are spent ({_longRestRetryAttempts} attempt(s), readyClicked={_longRestRetryReadyClicked})" : stillAnimating ? "while CardsHandUI.AnimatingLostCards is still true — the game is visibly acting on the answer" : $"until {LongRestAnswerSettleSeconds:F2}s have passed")}. " +
                                        "On 2026-09-07 it was NOT held: CardsHandUI.currentMode had gone " +
                                        "back to ActionSelection while the game animated the answer away, " +
                                        "every gate here read 'rest pending, no burn step open', and the pump " +
                                        "clicked PERFORM LONG REST 1.70s after his commit — opening a SECOND " +
                                        "burn step over the discard pile that no END edge could ever close. " +
                                        "A second answered step would have cost him a SECOND card for one rest.");
                }
                return;
            }
            // retryRunning ⇒ fall straight through: the one open window's two-step advance is
            // mid-flight and TryAdvanceLongRestTurn logs each step it actually performs.
            if (!retryRunning)
            {
                // The settle window has passed, the game is no longer animating, and it still has
                // not taken the answer. This is the stuck state the pump exists for, so it opens a
                // retry window — loudly, because a retry after an answer re-opens a step the player
                // has already made.
                _longRestRetryOpenedAt = nowAnswer;
                _longRestRetryAttempts++;
                _longRestHoldPhaseLogged = 0;
                // HW-VERIFY: grep token "LONG REST RE-DRIVE ONCE".
                // WORKING = absent. The settle window plus the game's own AnimatingLostCards flag
                // cover every route by which a rest resolves normally, so it never reaches here.
                // INERT/DEFECT = present. It means the game did not act on an answer it was given.
                // Read the attempt number in the line: attempt 1 is the mod buying the turn back
                // while naming the state. An attempt 2 or 3 means the window before it opened and
                // NEVER REACHED THE READY CLICK — that is a real reading now rather than an
                // impossibility, because before 2026-09-07 the retry latched itself spent on the
                // CLOCK and a window that made no game call at all still counted (D3).
                // A LongRestRetryMaxAttempts-th line with no "LONG REST FLOW: RESOLVED" after it is
                // upstream of this mod (see the report).
                VRLog.Note("Cards", $"LONG REST RE-DRIVE ONCE (attempt {_longRestRetryAttempts} of " +
                                    $"{LongRestRetryMaxAttempts}): {sinceAnswer:F2}s after this player answered his " +
                                    "long rest the game has still not resolved it (LongRest set, HasLongRested " +
                                    "false, CardsHandUI.currentMode is no longer LoseCard, and " +
                                    "CardsHandUI.AnimatingLostCards is false), which is past the " +
                                    $"{LongRestAnswerSettleSeconds:F2}s its own burn animation can possibly need. " +
                                    "Driving PERFORM LONG REST one more time so the turn is not lost — a re-drive " +
                                    "is only ever marked SPENT once the ReadyButton click itself lands, and it " +
                                    "re-opens a step the player has " +
                                    "already made, so if it appears the fault is UPSTREAM of this pump: the game's " +
                                    "OnLoseCardClick took the damage-avoidance branch instead of the long-rest one " +
                                    "(CardsHandUI.cs:2369 needs LongRest && GameState.InternalCurrentActor == " +
                                    "playerActor, and this mod writes neither term).");
            }
        }
        if (!_longRestPumpAnnounced)
        {
            _longRestPumpAnnounced = true;
            VRLog.Info("Cards", "Long rest: this actor's turn ARRIVED with the rest still pending — driving the " +
                                "game's own PERFORM LONG REST flow (2D confirmation toggle + ReadyButton are " +
                                "unreachable in VR; detection re-armed every tick until the burn step opens).");
        }
        float now = Time.unscaledTime;
        if (_longRestPumpQueued || now < _longRestPumpNextTry)
            return;
        _longRestPumpNextTry = now + 0.5f;
        _longRestPumpQueued = true;
        bool advanced = false;
        bool readyClicked = false;
        string step = "";
        CardActionQueue.Enqueue(
            () =>
            {
                // Re-resolve INSIDE the queued action: it runs a frame later, behind any
                // pending card selects, and every gate is re-checked against fresh state.
                CardsHandUI? fresh = CardsGameApi.LongRestTurnHand();
                if (fresh != null)
                    advanced = CardsGameApi.TryAdvanceLongRestTurn(fresh, out step, out readyClicked);
            },
            () =>
            {
                _longRestPumpQueued = false;
                if (readyClicked && _longRestRetryOpenedAt > 0f)
                {
                    // THE RETRY IS SPENT BY THIS CALL AND BY NOTHING ELSE (D3). Only the second
                    // step re-opens the burn step the player already answered, so only the second
                    // step may consume the allowance — a window that expired without reaching it
                    // made no game call and is re-openable.
                    _longRestRetryReadyClicked = true;
                    _longRestHoldPhaseLogged = 0;
                }
                if (advanced)
                {
                    VRLog.Info("Cards", $"Long rest: auto-advanced — {step}.");
                    _dirty = true; // burn step opening changes the fan/tray → rebuild
                }
            });
    }

    // ------------------------------------------------- selection-follow hand switch --

    // HAND-SWITCH WATCHDOG (regression ruling, user 2026-08: "Optionsmenü darf das
    // Spielgeschehen nie beeinflussen" — the presence of the options menu must have ZERO
    // influence on gameplay). Reported (intermittent, one hardware session): with the
    // options menu open, an initiative-track portrait click switched the SELECTED character
    // but the to-be-placed cards (hand fan + board slots) kept showing the previous
    // character. Audit result: the mod's own state-following carries NO menu gate anywhere —
    // the CardsHandManager.ShowHands postfix (HandShown → _dirty) and PollModeChange's actor
    // signature both fire on every SwitchHand, and the only menu-keyed Cards gates
    // (BlockingWindowModalActive / ModalUI) are input-hit-testing only and exclude the
    // pause/options family (NonBlockingMenus) by construction. The one way presentation and
    // selection can stay diverged is the game's own EDGE-triggered coupling being swallowed:
    // InitiativeTrackPlayerAvatar.Select runs InitiativeTrack.Select (selection moves) but
    // skips CardsHandManager.SwitchHand behind its LastMessage/IsShown gates
    // (InitiativeTrackPlayerAvatar.cs:21-28) — a lost edge with no vanilla retry. This pump
    // is the LEVEL-triggered safety net: whenever selection and the presented hand stay
    // diverged during the card-selection phase — menu open or not — it drives the game's OWN
    // SwitchHand seam so the cards re-converge. All vanilla gates are replicated in
    // CardsGameApi.SelectionHandDrift, so the pump can never perform a switch the game
    // itself would have refused; it is deliberately NOT gated on any menu/window state (the
    // ruling above). Genuine blockers (story/results — never the options family) defer it.
    private float _selSwitchNextTry;   // throttle for the queued game call (reads stay per-tick)
    private bool _selSwitchQueued;     // at most one queued switch in flight
    private int _selSwitchDriftFrame = -1; // first frame the drift was seen (-1 = none)

    private void PumpSelectionHandSwitch()
    {
        CPlayerActor? drift = CardsGameApi.SelectionHandDrift();
        if (drift == null)
        {
            _selSwitchDriftFrame = -1; // converged (or a vanilla gate holds) — re-arm
            return;
        }
        if (WorldUI.ModalFallback.BlockingWindowModalActive)
            return; // genuine blockers only (story/results/…) — the pause/options family is
                    // NonBlockingMenus and can never raise this gate (the ruling above).
        int frame = Time.frameCount;
        if (_selSwitchDriftFrame < 0)
        {
            _selSwitchDriftFrame = frame;
            return; // same-frame divergence is normal (Select runs before SwitchHand) —
                    // only drift that PERSISTS across frames is a swallowed edge
        }
        if (frame - _selSwitchDriftFrame < 2)
            return;
        float now = Time.unscaledTime;
        if (_selSwitchQueued || now < _selSwitchNextTry)
            return;
        _selSwitchNextTry = now + 0.5f;
        _selSwitchQueued = true;
        bool switched = false;
        string who = CardsGameApi.ActorLabel(drift);
        CardActionQueue.Enqueue(
            () =>
            {
                // Re-resolve INSIDE the queued action (it runs a frame later, behind any
                // pending card selects): the drift may have healed or a gate risen meanwhile.
                CPlayerActor? fresh = CardsGameApi.SelectionHandDrift();
                if (fresh != null)
                    switched = CardsGameApi.SwitchHandTo(fresh);
            },
            () =>
            {
                _selSwitchQueued = false;
                if (switched)
                {
                    VRLog.Warn("Cards", "Hand-switch watchdog: presented hand had DIVERGED from the " +
                                        $"selected character '{who}' during card selection (the game's " +
                                        "portrait-click SwitchHand edge was swallowed) — drove the game's " +
                                        "own SwitchHand to re-converge the fan/board cards. Ruling: the " +
                                        "options menu must never influence gameplay ('Optionsmenü darf das " +
                                        "Spielgeschehen nie beeinflussen', user 2026-08), so this net runs " +
                                        "regardless of any open menu.");
                    _dirty = true; // the ShowHands postfix also set it — belt and braces
                }
            });
    }

    private (bool selected, bool losing, bool done, bool pickRefused)? _longRestState;

    /// <summary>
    /// Prove the long-rest state machine from the log alone (test #28, item 3),
    /// change-deduped:
    /// (1) long rest SELECTED in card selection (initiative 99, heal pending),
    /// (2) the BURN step is live — <c>CardHandMode.LoseCard</c> while
    ///     <c>CharacterClass.LongRest</c> — lay a discarded card into the left slot,
    /// (3) RESOLVED — <c>HasLongRested</c> (card burnt, +2 heal applied).
    ///
    /// <para>A FOURTH TERM SINCE 2026-09-07, and it is the one the deadlock needed: whether the
    /// mod's OWN pick gate is refusing the burn step it just announced. State (2) was printed
    /// truthfully while the player sat stranded — the burn step WAS active — and the sentence
    /// "lay a discarded card into the left slot" was, that session, a promise the mod did not
    /// keep. <see cref="LongRestPickRefusal"/> names the term that broke it, and
    /// <c>pickRefused</c> is in the dedup key so a refusal that arrives after the announcement
    /// gets its own line instead of being swallowed as "no change".</para>
    /// </summary>
    private void LogLongRestState(CardsHandUI? hand)
    {
        if (hand == null)
        {
            _longRestState = null;
            return;
        }
        bool selected = CardsGameApi.IsLongRestSelected(hand);
        bool losing = CardsGameApi.IsLongResting(hand)
                      && CardsGameApi.Mode(hand) == CardHandMode.LoseCard;
        bool done = CardsGameApi.HasLongRested(hand);
        // THE BURN STEP IS OPEN AND THE MOD ITSELF IS REFUSING IT. This is the state the
        // 2026-09-07 deadlock left the player in and NO line said so: the game had opened
        // CardHandMode.LoseCard over the discard pile, and CardsDriver's pick branch answered
        // PickIsOpen=false, fell back to FillHandFan and left him with no discard candidates and
        // no placement target. Carried in the dedup key so the refusal gets its own line rather
        // than hiding behind an unchanged (selected, losing, done).
        bool pickRefused = losing && !CardsGameApi.PickFlowLive(hand);
        var state = (selected, losing, done, pickRefused);
        if (_longRestState.HasValue && _longRestState.Value == state)
            return;
        _longRestState = state;

        if (losing)
        {
            (string? _, string occasion) = ResolvePickOccasion(hand, CardHandMode.LoseCard,
                                                              CardsGameApi.PickCardsWanted());
            // HW-VERIFY: grep token "LONG REST FLOW". WORKING = one line per OPEN LOSE PICK taken
            // while a long rest is pending — which is NOT one line per long rest, because a damage
            // loss in the same round satisfies this gate too (2026-09-07, host lines 243480 and
            // 249352); read `occasion=` to tell which, and expect a long rest to produce a line
            // reading `occasion=…LongRest && GameState.InternalCurrentActor==actor`. That line
            // reads `pickRefusedBy=<nothing>` — the pick opened and the mod's own gate
            // accepted it — followed within a handful of lines by
            // `Pick fan source (LoseCard): discard pile`. INERT (the instrument never ran) = zero
            // LONG REST FLOW lines in a session whose log contains `STATE: Halted @ LongRest`;
            // the long-rest pump never reached the burn step and the fault is upstream, in
            // CardsGameApi.TryAdvanceLongRestTurn. BROKEN, and this is the deadlock reproducing =
            // `pickRefusedBy=` naming a term: `armed on the FOREIGN hand '<name>'` is the
            // 2026-09-07 cause verbatim (another character's CardsHandUI owning PickFlowWatch),
            // `no OPEN edge is outstanding` is a pick the game never announced, `the game asks for
            // 0 card(s)` is a hand the game re-drove behind the burn step. Both of the first two
            // are now unreachable by construction and a reading of either is a NEW defect, not
            // this one returning — and the FOREIGN one is unreachable twice over, because
            // PickFlowWatch.HealOwnerIfRefusingAnOpenPick runs earlier in the SAME Update and would
            // have corrected the latch before this line was reached, leaving a `PICK OWNER HEAL`
            // line above this one. A foreign refusal printed here with NO heal beside it means the
            // watchdog's own gate refused, and its five terms are then what to read.
            // STILL BEYOND THE INSTRUMENT: a refusal
            // that happens with pickRefusedBy=<nothing> — the fan then had its candidates and
            // something further down (the palm gate, the slot target) ate them, and the reading
            // that decides it is the `fan state:` line's withheldBy/mode pair on the same frame.
            // 2026-09-07: THIS LINE USED TO ASSERT "BURN step active" AND WAS WRONG ONCE PER
            // SESSION. Its terms are `IsLongResting && Mode == LoseCard`, and CCharacterClass
            // .LongRest is set from the moment the rest is CHOSEN in card selection — so a DAMAGE
            // loss suffered in the same round satisfies both. The host log carries it verbatim:
            // line 243480 printed "BURN step active" for the damage loss the user described
            // ("erst habe ich den Schaden bekommen und DANACH… wegen der langen Rast"), while the
            // game was in TakeDamageConfirmation and committed it as `#166 BurnAvailableCard @
            // TakeDamageConfirmation`. The real burn step was 5,800 lines later at 249351/#168.
            // It now reports the occasion ResolvePickOccasion RESOLVED instead of asserting one,
            // so the two readings are told apart by the same authority the banner uses.
            VRLog.Note("Cards", "LONG REST FLOW: a LoseCard pick is open while LongRest is set — " +
                                $"occasion={occasion}. THAT IS NOT ALWAYS THE BURN STEP: LongRest " +
                                "stays set from card selection until the rest resolves, so a damage " +
                                "loss in the same round reads here too — the occasion is what tells " +
                                "them apart. Lay a discarded card into the left slot to lose it; the " +
                                "docked Confirm commits it. Candidates SHOULD be the " +
                                $"{CardsGameApi.DiscardedCount(hand)} card(s) in the discard pile. " +
                                $"Mod pick gate: pickRefusedBy={LongRestPickRefusal(hand)}.");
        }
        else if (done)
            VRLog.Note("Cards", "LONG REST FLOW: RESOLVED — chosen card burnt, +2 heal applied (HasLongRested).");
        else if (selected)
            VRLog.Note("Cards", "LONG REST FLOW: SELECTED in card selection (initiative 99, heal pending) — " +
                                "waiting for this actor's turn to choose the card to lose.");
    }

    /// <summary>
    /// WHY THE MOD IS REFUSING THE LONG REST'S BURN PICK, as the NAME of the term that said no and
    /// the VALUE it read — or the empty phrase when nothing is refusing.
    ///
    /// <para>Every term here is a read of <see cref="CardsGameApi.PickFlowLive"/>'s own operands,
    /// in the order that method evaluates them, so the answer can never disagree with the gate it
    /// describes. It writes nothing and is called at most once per long-rest state change.</para>
    ///
    /// <para>The 2026-09-07 session is what this exists for: the burn step opened — that build
    /// worded the line <c>Long rest: BURN step active</c>, host Player.log:289747 — and the player
    /// could neither place a card nor see his seven discards, and the ONLY line that named the
    /// cause was a <c>PICK GATE</c> falsifier clause four lines earlier that nobody was looking
    /// for. A long rest that does not start must now say so in its own words.</para>
    /// </summary>
    private static string LongRestPickRefusal(CardsHandUI hand)
    {
        try
        {
            CardHandMode mode = CardsGameApi.Mode(hand);
            if (!CardsGameApi.IsPickMode(mode))
                return $"the hand's mode is {mode}, which is not a pick mode";
            if (!Patches.PickFlowWatch.Live)
                return "no OPEN edge is outstanding — the game never announced this pick "
                       + "(PickFlowWatch.Live=False)";
            if (!Patches.PickFlowWatch.LiveFor(hand))
                return $"the pick flow is armed on the FOREIGN hand '{Patches.PickFlowWatch.OpenedOnName}', "
                       + "so this hand does not own it (PickFlowWatch.LiveFor=False)";
            int want = hand.MaxSelectedCards;
            if (want <= 0)
                return $"the game asks for {want} card(s) (CardsHandUI.MaxSelectedCards)";
            return "<nothing — the gate accepts this pick>";
        }
        catch (System.Exception ex)
        {
            return $"<unreadable: {ex.GetType().Name}>";
        }
    }

    /// <summary>
    /// Enforce "slot 0 = initiative": if the game's initiative card is not the slot-0
    /// occupant (e.g. cards were dropped right-to-left), issue the game's own swap.
    /// </summary>
    /// <summary>A model card's name for a log line, never null and never thrown from.</summary>
    private static string CardName(CAbilityCard? card)
    {
        if (card == null)
            return "<none>";
        try { return string.IsNullOrEmpty(card.Name) ? "<unnamed>" : card.Name; }
        catch (System.Exception) { return "<unreadable>"; }
    }

    /// <summary>A model card's initiative for a log line, or -1 when it cannot be read.</summary>
    private static int CardInit(CAbilityCard? card)
    {
        if (card == null)
            return -1;
        try { return card.Initiative; }
        catch (System.Exception) { return -1; }
    }

    private void ReconcileInitiative(CardsHandUI hand)
    {
        if (hand == null || hand.PlayerActor == null)
            return;
        if (hand.PlayerActor.CharacterClass.RoundAbilityCards.Count != 2)
            return;
        VRCard? slot0 = _tray.Occupant(0);
        if (slot0 == null || slot0.GameCard == null)
            return;
        CAbilityCard? initiative = CardsGameApi.InitiativeCard(hand);
        CAbilityCard? inSlot0 = slot0.GameCard.AbilityCard;
        if (initiative != null && initiative != inSlot0)
        {
            if (CardsGameApi.SwapInitiative(hand))
            {
                // HW-VERIFY: report item 4 (2026-09-06). Grep token: INITIATIVE FOLLOWS THE RECESS.
                //
                // THIS IS THE MOD WRITING GAME STATE, and the round of 2026-09-06 is the reason it
                // now says so out loud. CardsGameApi.SwapInitiative calls the game's own
                // AbilityCardUI.SwapInitiative(), which swaps Initiative/SubInitiative, reverses
                // RoundAbilityCards, updates the initiative track and NETWORKS the change - so the
                // card the game treats as leading is decided here, by what is lying in recess 0.
                // It is deliberate (a VR player expects the card he puts on the left to lead) and
                // it is phase-guarded to SelectAbilityCardsOrLongRest, so it cannot fire after the
                // confirm. But it means recess 0 is an INPUT and not a display, and anything that
                // seats a card there without the player - PlayTray.PlaceRoundCardIfMissing is the
                // one such path, and it now prints 'RECESS SEATED BY THE MOD' - changes a
                // replicated gameplay fact.
                //
                // THE CO-PLAYER'S CLAIM, verbatim: "nach der Bestaetigung ... er die falsche Karte
                // als Initiative hat die er dort nicht hingelegt hatte." Promoted from VRLog.Debug
                // because at the shipped default tier the one line that records the game's
                // initiative being rewritten was not being written at all: his 2026-09-06 log
                // carries it once and the host's three times, and only because both were running
                // with the Cards tier turned up.
                VRLog.Note("Cards", "INITIATIVE FOLLOWS THE RECESS: the LEFT recess (slot 0) holds "
                    + $"'{CardName(inSlot0)}' (initiative {CardInit(inSlot0)}) while the game had "
                    + $"'{CardName(initiative)}' (initiative {CardInit(initiative)}) as the leading "
                    + "card, so THE GAME WAS REWRITTEN TO FOLLOW THE CARD — AbilityCardUI"
                    + ".SwapInitiative() swapped Initiative/SubInitiative, reversed "
                    + "RoundAbilityCards, updated the track and networked it. The physical "
                    + "placement wins and this player's initiative is now the left recess's "
                    + "number. THE DIRECTION IS THE WHOLE POINT (report item 4, 2026-09-06): "
                    + "nothing here reorders the two recesses, so a player who sees his cards "
                    + "'verdreht' after confirming was not swapped by this line — but if a card he "
                    + "did not put on the left was sitting there, this is what turned that into "
                    + "his real initiative. Read it with 'RECESS SEATED BY THE MOD' (did anything "
                    + "but the player fill that recess?) and 'Slot order SENT' (which two cards "
                    + "are in the two recesses, by name). This is guarded to the selection phase, "
                    + "so it can never change an initiative after the confirm. It prints ONLY on "
                    + "the machine that placed the cards — a watcher's log can never answer the "
                    + "question, which is why the co-player's own log is the evidence that "
                    + "settles it.");
            }
        }
    }

    private void OnSwapRequested()
    {
        ForeignInteraction("tray initiative swap");
        CardsHandUI? hand = CurrentHand();
        if (hand == null)
            return;
        CardsHandUI handRef = hand;
        CardActionQueue.Enqueue(
            () => CardsGameApi.SwapInitiative(handRef),
            () =>
            {
                _tray.SyncFromGameState(handRef, _factory);
                _dirty = true;
            });
    }

    private void OnConfirmRequested()
    {
        // Confirm OR revoke (test #19), decided INSIDE the queued action so it
        // serializes behind pending card selects and reads the freshest state:
        // - active hand already confirmed → the game's own un-ready path
        //   (UIReadyToggle.ReadyUp(false) → GameActionType.UnreadyPlayer);
        // - online card selection, not yet readied → ready-up via the same toggle
        //   (the 2D UI shows the toggle INSTEAD of the ReadyButton there);
        // - everything else → the ReadyButton dispatch (Pass/StepComplete — no
        //   spin-wait, ScenarioRuleClient.Pass only messages the SRL).
        // Every outcome logs the RESOLVED game state.
        ForeignInteraction("tray CONFIRM");

        // EVENT-DISCARD DEADLOCK FIX (pre-scenario "Begegnungen" mali; MP hardware log,
        // remote Player.log:11706ff): the game's burn/discard confirm DialogPopup used to
        // be the ONLY commit affordance, and reaching for it kept trigger-grabbing the
        // placed pick card instead (lift-priority fallback) — which auto-cancelled the
        // popup through the reopen seam, forever. The tray CONFIRM is now the pick
        // flow's confirm: while the popup is open it presses the popup's OWN commit
        // option (game callback → OnLoseCardClick → the game's networked per-card
        // GameActions); while a >2-card requirement still owes cards it locks the
        // current batch of two instead (pure VR bookkeeping, see TryLockPickBatch).
        CardsHandUI? pickHand = CurrentHand();
        // ITEM 6b: LIVE, not merely "the mode is still a pick mode". The board CONFIRM must not
        // press a commit option — or lock a batch — for a flow that already ended.
        if (pickHand != null && PickFlowLive(pickHand))
        {
            if (CardsGameApi.IsPickConfirmDialogOpen(pickHand))
            {
                CardActionQueue.Enqueue(
                    () =>
                    {
                        bool fired = CardsGameApi.ConfirmPickDialog();
                        VRLog.Info("Cards", "Board: CONFIRM → pick confirm dialog commit option " +
                                            (fired ? "pressed (game's own OnLoseCardClick path — outcome networked by the game)."
                                                   : "not pressable (dialog closed before the queued press)."));
                    },
                    () => _dirty = true);
                return;
            }
            if (TryLockPickBatch(pickHand))
                return;
            // Not a dialog/batch state (e.g. the recover flows arm the ReadyButton) —
            // fall through to the normal dispatch below.
        }

        CardActionQueue.Enqueue(
            () =>
            {
                CardsHandUI? hand = CurrentHand();
                if (hand != null && CardsGameApi.IsConfirmed(hand))
                {
                    bool revoked = CardsGameApi.SetReady(false);
                    VRLog.Info("Cards", $"Board: CONFIRM → ready {(revoked ? "REVOKED" : "revoke rejected")} " +
                                        $"({CardsGameApi.DescribeReadyState()}).");
                }
                else if (CardsGameApi.ReadyToggleAvailable())
                {
                    bool readied = CardsGameApi.SetReady(true);
                    VRLog.Info("Cards", $"Board: CONFIRM → ready toggle {(readied ? "READIED" : "rejected")} " +
                                        $"({CardsGameApi.DescribeReadyState()}).");
                }
                else
                {
                    bool fired = CardsGameApi.ClickReady();
                    VRLog.Info("Cards", $"Board: CONFIRM → ReadyButton {(fired ? "clicked" : "rejected (not interactable)")} " +
                                        $"({CardsGameApi.DescribeReadyState()}).");
                }
            },
            () => _dirty = true);
    }

    private void OnUndoRequested()
    {
        ForeignInteraction("tray UNDO");
        // EVENT-DISCARD DEADLOCK FIX: while the pick confirm dialog is open, UNDO is the
        // dialog's own CANCEL ("Wähle eine andere Karte") — the game deselects every
        // pick and the candidates return to the fan for a fresh choice. Queued: the
        // cancel callback runs DeselectAllCards (the spin-wait path).
        //
        // PICK RESTART (user report 2026-08-24: "Wird der gedrückt soll die Auswahl auf der ersten
        // Seite nochmal komplett von anfang an beginnen"). The game's cancel deselects EVERYTHING,
        // including the cards a >2-card discard had already locked into earlier VR pages — so the
        // mod's page bookkeeping has to go back to page 1 in the same breath, and the pages that
        // already FLEW into the discard stack have to come back out of it (DrainPickReturnFlight).
        // Both happen in the completion callback, i.e. only once the cancel has ACTUALLY landed:
        // resetting the page count while the picks are still selected would leave RelayoutField
        // trying to seat three cards in two recesses for a frame.
        CardsHandUI? pickHand = CurrentHand();
        if (pickHand != null && CardsGameApi.IsPickConfirmDialogOpen(pickHand))
        {
            bool pressed = false;
            CardActionQueue.Enqueue(
                () => pressed = CardsGameApi.CancelPickConfirmDialog(),
                () =>
                {
                    if (!pressed)
                    {
                        // THE FALSIFIER FOR THE 247 DEFECT. This line used to be printed
                        // unconditionally, which is why nine dead presses read exactly like nine
                        // working ones in the hardware log (LogOutput.log:3978-4028).
                        VRLog.Warn("Cards", "Board: UNDO → pick confirm dialog CANCEL did NOT fire — " +
                                            "no cancel option was pressable on the open DialogPopup " +
                                            "(see the [Cards] 'Pick confirm CANCEL' line above for which " +
                                            "test refused). NOTHING changed: the picks are still selected, " +
                                            "the popup is still open and the VR pages are untouched.");
                        _dirty = true;
                        return;
                    }
                    int returning = ArmPickRestart();
                    VRLog.Info("Cards", "Board: UNDO → pick confirm dialog CANCEL (the game's own " +
                                        "\"choose another card\") PRESSED — the game deselected every pick " +
                                        "and the candidates are back in the fan. VR side: the selection " +
                                        "RESTARTS AT PAGE 1 (locked batches dropped, both recesses cleared) " +
                                        $"and {returning} card(s) that had already flown into the Discard " +
                                        "stack are queued to fly back OUT of it on the next rebuild " +
                                        "(see 'Pick restart RETURN').");
                    _dirty = true;
                });
            return;
        }
        CardActionQueue.Enqueue(
            () =>
            {
                bool fired = CardsGameApi.ClickUndo();
                VRLog.Info("Cards", $"Board: UNDO → UndoButton {(fired ? "clicked" : "rejected (not interactable)")}.");
            },
            () => _dirty = true);
    }

    /// <summary>
    /// The turn-flow SKIP keycap's press. Queued through <c>CardActionQueue</c> exactly like its
    /// CONFIRM and UNDO siblings — the commit is a game action and every game action on this board
    /// goes through the one queue, so a skip can never overtake a confirm the player pressed a frame
    /// earlier.
    ///
    /// <para>NO PICK-FLOW BRANCH, and that is a fact about the game rather than an omission: the
    /// event-discard pick flow overrides the CONFIRM and UNDO caps because the confirm dialog's two
    /// options have to be reachable in VR, and it has exactly two. The skip widget is not part of
    /// that dialog at all — <c>m_SkipButton</c> is a turn-flow control the Choreographer raises on
    /// skippable ability steps — so its press has one meaning in every state it is shown in.</para>
    /// </summary>
    private void OnSkipRequested()
    {
        ForeignInteraction("tray SKIP");
        CardActionQueue.Enqueue(
            () =>
            {
                bool fired = CardsGameApi.ClickSkip();
                VRLog.Info("Cards", $"Board: SKIP → SkipButton {(fired ? "clicked" : "rejected")} " +
                                    $"({CardsGameApi.DescribeSkipGate()}).");
            },
            () => _dirty = true);
    }

    private void OnShortRestRequested()
    {
        ForeignInteraction("short rest toggle");
        CardsHandUI? hand = CurrentHand();
        if (hand == null)
            return;
        CardsHandUI handRef = hand;
        CardActionQueue.Enqueue(() => CardsGameApi.ToggleShortRest(handRef), () => _dirty = true);
    }

    private void OnLongRestRequested()
    {
        ForeignInteraction("long rest toggle");
        CardsHandUI? hand = CurrentHand();
        if (hand == null)
            return;
        CardsHandUI handRef = hand;
        CardActionQueue.Enqueue(() => CardsGameApi.ToggleLongRest(handRef), () => _dirty = true);
    }

    private void OnPlayRequested(VRCard card, CBaseCard.ActionType type)
    {
        ForeignInteraction("action play");
        FullAbilityCard? full = card.FullCard;
        if (full == null)
            return;
        // No spin-wait in OnAbilityClick, but queue anyway: serializes with pending
        // selects and keeps game entries out of interaction callbacks.
        CardActionQueue.Enqueue(() => CardsGameApi.PlayHalf(full, type), () => _dirty = true);
    }

    // ------------------------------------------------------------------ pile browse --

    // Browse state (test #21): WHICH character's pile the open arc is currently about, and the mode
    // it was resolved in. Both are the FOCUS-RESOLVED values (CharacterFocus.PresentedHand /
    // ResolveHand), never the game's raw CurrentHand — mixing the two is what made an open fan close
    // itself on a phantom "hand switch" (see UpdateBrowser). They are now a RE-TARGET record rather
    // than a close trigger: test #21's "browse fans never survive a context switch" was superseded by
    // the 2026-08-08 ruling that the fan must follow the focused character instead of vanishing.
    // The _browseHeld flag that used to sit here died with the stack pinch-grab (PileStack.CanGrab,
    // 2026-08-06) — every browse fan is poke/laser-toggled and board-anchored now.
    private CardsHandUI? _browseHand;
    private CardHandMode _browseMode;

    /// <summary>
    /// MAY A DISCARD/BURNT BROWSE ARC BE UP RIGHT NOW? The ONE predicate both the open path
    /// (<see cref="OpenBrowser"/>) and the per-rebuild refresh (<see cref="UpdateBrowser"/>) ask, so
    /// a fan can never be refused an open it would have survived, nor closed out of a state it was
    /// allowed to open in — the two used to be different tests, which is half of why an open fan
    /// died in the same breath as it was raised.
    ///
    /// <para>USER RULING 2026-08-08: "Die Verbrannt-Piles und Abgeworfen-Piles sollen jederzeit EGAL
    /// wer gerade dran ist öffenbar sein und die Karten sollen auch nehmbar sein um sie anzugucken,
    /// das darf nicht blockieren." RE-STATED AND WIDENED 2026-09-05, after a multiplayer hardware
    /// test: "Man soll die Kartenfächer (Items, verbrannt und abgeworfen) IMMER anschauen können.
    /// Aktuell kann man die Fächer nicht öffnen, wenn eine Entscheidung getroffen werden muss (zB
    /// wegen Schaden), das ist aber wichtig für die Entscheidung." So there is NO turn key, NO phase
    /// whitelist and NO "is it your character" test here, and there must never be one again: a pile
    /// is public, readable information (vanilla lets anyone open ANY player's full card overview
    /// from the initiative track).</para>
    ///
    /// <para>WHAT WENT WRONG, and it went wrong in TWO independent ways at once. The pick-flow
    /// refusal below used to be <c>!readOnly &amp;&amp; IsPickMode(mode)</c> — a MODE test with no
    /// pile term and no liveness term — and the co-player's log shows what that costs: with the
    /// take-damage decision open he poked the BURNT stack eleven times and got eleven
    /// "PILE BROWSE REFUSED: Burnt — a modal pick flow owns the pile widgets right now
    /// (mode=LoseCard)". Both halves of that sentence were wrong for his state.</para>
    ///
    /// <list type="number">
    /// <item>NO PILE TERM. A pick flow does not borrow "the piles", it borrows the widgets the
    ///   GAME marked selectable, and which pile those come from is a per-flow parameter, not a
    ///   property of the mode: <c>TakeDamagePanel</c> alone drives <c>CardHandMode.LoseCard</c>
    ///   with <c>selectableCardType</c> = <c>Hand</c> (burn one available card, :538),
    ///   <c>Discarded</c> (burn two discarded, :575) and <c>Any</c> (full-hand preview, :520). So a
    ///   burn-from-HAND pick borrows nothing at all from the burnt pile, and the mode alone can
    ///   never say otherwise.</item>
    /// <item>NO LIVENESS TERM. <c>CardsHandUI.currentMode</c> is STICKY — the same stale-mode trap
    ///   this file already documents for <c>CardsSelection</c> and <c>ActionSelection</c>.
    ///   <c>TakeDamagePanel.HideCardPreview</c> ends a preview with
    ///   <c>CardsHandManager.Hide()</c>, and <c>Hide</c> (CardsHandManager.cs:899) deactivates the
    ///   hand objects without touching <c>currentMode</c>. It therefore reads <c>LoseCard</c> long
    ///   after any pick is over, and a mode-only gate refuses on a flow that is not running.</item>
    /// </list>
    ///
    /// <para>SO THE REFUSAL NOW ASKS THE OWNERSHIP QUESTION IT ALWAYS MEANT TO ASK, of the ONE
    /// authority that can answer it: <see cref="PickOwnsPileVisuals"/> counts how many of THIS
    /// pile's cards the pick fan and the drop field are actually holding right now. That set is
    /// live (Rebuild refills <c>_fanBuffer</c> before <see cref="UpdateBrowser"/> runs), it is
    /// per-pile by construction, and it is empty whenever the mode is a stale leftover — one
    /// classifier, both surfaces, exactly like <see cref="BoardOwnsCardVisual"/>.</para>
    ///
    /// <para>Two refusals survive, and both are OWNERSHIP-OF-THE-WIDGETS refusals rather than
    /// permission refusals:</para>
    /// <list type="number">
    /// <item>A live modal PICK flow that is holding EVERY card of THIS pile in its own fan/drop
    ///   field. A card has exactly one visual (the face is the game's own <c>FullAbilityCard</c>
    ///   rect re-parented onto the VR card, <c>VRCardFactory.GetOrCreate</c> is 1:1 with the
    ///   widget), so an arc cannot show a card the pick is already showing. This is not "you may
    ///   not look": every one of those cards is laid out in front of the player AS the pick fan,
    ///   which is why refusing costs the player nothing. A PARTIAL overlap does NOT refuse — the
    ///   arc opens and <see cref="UpdateBrowser"/> simply leaves the borrowed ones out, the same
    ///   way it already leaves out a played/active/slotted card. A READ-ONLY focus view is exempt
    ///   for the same reason <c>Rebuild</c> forces its <c>pick</c> false: the watched character's
    ///   <c>currentMode</c> is a stale leftover of its own last decision and owns nothing here.</item>
    /// <item>A real modal dialog (<see cref="VRMode.ModalUI"/>) — it owns the scene and the input.
    ///   Note that a take-damage decision is NOT one: the co-player's refusals named the pick
    ///   reason, so <c>VRMode</c> was not <c>ModalUI</c> while that panel was up.</item>
    /// </list>
    /// </summary>
    /// <param name="kind">WHICH pile the browse is about. The term the gate never had: without it
    /// every pick mode refused every pile, including piles the live flow borrows nothing from.</param>
    /// <param name="hand">The hand whose pile is being browsed — the FOCUS-RESOLVED one, the same
    /// hand the arc will read its widgets from, so the gate and the fill can never disagree.</param>
    /// <param name="readOnly">True while the presented character is a FOCUS override, i.e. one the
    /// game is not driving. Passed in rather than read off <c>CharacterFocus.ReadOnlyView</c>
    /// because that flag is latched by the edge-driven rebuild: the open path needs the LIVE
    /// answer, the rebuild path already holds this rebuild's freshly latched one.</param>
    private bool BrowseAllowed(PileKind kind, CardsHandUI? hand, CardHandMode mode, bool readOnly,
                               out string? refusal)
    {
        // ITEM 6b: the liveness term joins the pile term. A pick whose flow has ENDED owns no
        // widget, so it may not refuse a browse — and with an empty pick fan `owned >= inPile`
        // was true for an EMPTY pile, which refused the arc for a dead flow.
        if (!readOnly && PickFlowLive(hand)
            && PickOwnsPileVisuals(kind, hand, out int owned, out int inPile)
            && owned >= inPile)
        {
            refusal = $"the live pick flow is already showing ALL {inPile} card(s) of this pile as its " +
                      $"own fan/drop field (mode={mode}), and a card has exactly one visual — so the " +
                      "arc has nothing left to add that is not already laid out in front of you. " +
                      "This is the only surviving pick refusal: a pick that borrows a DIFFERENT " +
                      "pile, or only SOME of this one, no longer refuses anything";
            return false;
        }
        if (VRModeStateMachine.CurrentMode == VRMode.ModalUI)
        {
            refusal = "a modal dialog owns the scene (VRMode.ModalUI)";
            return false;
        }
        refusal = null;
        return true;
    }

    /// <summary>Widget scratch for <see cref="PickOwnsPileVisuals"/>. Its OWN buffer rather than
    /// <c>_pileWidgetBuffer</c>: that one belongs to <see cref="UpdateBrowser"/>'s fill, which runs
    /// immediately after the gate asked this question, and the open path asks it from outside
    /// Rebuild entirely.</summary>
    private readonly List<AbilityCardUI> _pileGateBuffer = new(16);

    /// <summary>
    /// HOW MANY OF THIS PILE'S CARDS IS THE LIVE PICK FLOW HOLDING? The pile term
    /// <see cref="BrowseAllowed"/> never had, answered from the mod's own record of what the pick
    /// is showing rather than from the mode: <c>_fanBuffer</c> (the cards Rebuild put in the pick
    /// fan — in a pick mode that is exactly the widgets the game marked selectable, Rebuild's
    /// <c>eligible</c> test) and <c>_fieldCards</c> (the drop-field occupants).
    ///
    /// <para>WHY THAT SET AND NOT THE GAME'S <c>selectableCardTypes</c>. The take-damage panel's
    /// full-hand preview passes <c>CardPileType.Any</c> (TakeDamagePanel.cs:520), which marks every
    /// non-supply widget selectable — so the game's own pile list answers "all of them" for a state
    /// in which nothing is being picked. <c>_fanBuffer</c> answers with what is PHYSICALLY on the
    /// board, which is the only thing an arc can collide with.</para>
    ///
    /// <para>FRESHNESS. Rebuild clears and refills <c>_fanBuffer</c> before it calls
    /// <see cref="UpdateBrowser"/>, so the rebuild path reads this rebuild's value; the open path
    /// reads the last rebuild's, which is the same staleness <see cref="BoardOwnsCardVisual"/> is
    /// documented to rely on and is re-checked on the very next rebuild.</para>
    ///
    /// <para>Read-only: no game state is read beyond the pile widget list, none is written, nothing
    /// goes on the wire.</para>
    /// </summary>
    /// <param name="owned">Cards of this pile whose one visual the pick fan / drop field holds.</param>
    /// <param name="inPile">Cards the pile has at all (long-rest placeholders are not cards).</param>
    private bool PickOwnsPileVisuals(PileKind kind, CardsHandUI? hand, out int owned, out int inPile)
    {
        owned = 0;
        inPile = 0;
        if (hand == null || kind == PileKind.Items)
            return false; // the items fan owns no AbilityCardUI widget — it cannot collide with a pick
        CardsGameApi.GetPileWidgets(hand, kind == PileKind.Burnt, _pileGateBuffer);
        for (int i = 0; i < _pileGateBuffer.Count; i++)
        {
            AbilityCardUI widget = _pileGateBuffer[i];
            if (widget == null || widget.AbilityCard == null || widget.IsLongRest)
                continue;
            inPile++;
            // No VR card yet ⇒ nothing on the board can be holding its visual: the pick fan only
            // ever contains cards it adopted through AdoptedCard, which creates them.
            VRCard? card = _factory.Find(widget);
            if (card == null)
                continue;
            if (_fanBuffer.Contains(card) || _fieldCards.Contains(card))
                owned++;
        }
        return owned > 0;
    }

    /// <summary>
    /// MAY THE BOARD OFFER A CARD PLACEMENT RIGHT NOW — i.e. can a card actually be laid down FOR
    /// THE CHARACTER THE BOARD IS PRESENTING? Every "put a card here" overlay is gated on this and
    /// nothing else: the pulsing recess hint (<see cref="UpdateWantedSlots"/>), the pick banner and
    /// the CONFIRM/UNDO keycap overrides (<see cref="UpdatePickStatus"/>).
    ///
    /// <para>USER REPORT 2026-08-24 (verbatim): "Wenn man einen Character auswählt der die
    /// Entscheidung aktuell gerade nicht treffen muss bzw. nicht vom Mod her am Zug ist (Oben steht
    /// Character-Name: Wähle 2 von 3 Karten […]) - dann soll auch das Kartenoverlay nicht angezeigt
    /// werden. <b>Generell gilt die Regel, dass das nur angezeigt werden soll wenn für diesen
    /// ausgewählten Character auch tatsächlich Karten hingelegt werden können.</b>" The general rule
    /// is the sentence in bold, so this predicate answers the general question; owner-vs-selected is
    /// merely the instance that exposed it.</para>
    ///
    /// <para>WHAT WENT WRONG, and why the two halves of the board disagreed. The pick FIELD is built
    /// by <c>Rebuild</c> from <c>CharacterFocus.ResolveHand</c> and is forced off for a focus view
    /// (<c>bool pick = !readOnly &amp;&amp; IsPickMode(mode)</c>, CardsDriver.4.Rebuild.cs:766) — so
    /// switching character during a forced discard already removed the placeable field and made
    /// every card non-grabbable (<c>commitGrab</c> requires <c>!readOnly</c>, :924). But the banner
    /// and the recess hint are driven from the per-frame tick with the GAME's hand
    /// (<c>UpdatePickStatus(hand)</c> / <c>UpdateWantedSlots(hand)</c> off <c>CurrentHand()</c>,
    /// CardsDriver.2.Update.cs:762-763), which never had a focus term at all. Result: the board kept
    /// pulsing "lay a card here" and kept the banner up — naming the PICK'S OWNER, not the character
    /// on the board — over a hand that could not place anything. The hardware log shows exactly that
    /// pairing: the banner is pushed once (LogOutput.log:1210, "Hilde Die 2Te: Wähle 2 von 3 Karten
    /// …") and is never re-pushed or cleared across the focus switches at :1545/:1581/:1652/…, each
    /// of which logs "[Cards] [Focus] presenting '…' READ-ONLY".</para>
    ///
    /// <para>THE TEST IS THE LIVE TWIN OF <c>Rebuild</c>'s <c>readOnly</c>, term for term: is the
    /// hand the board PRESENTS the same object as the hand the GAME drives? Derived here rather than
    /// read off <c>CharacterFocus.ReadOnlyView</c>, which is latched by the edge-driven rebuild — a
    /// per-frame surface asking that flag would answer with the PREVIOUS rebuild's view for the frame
    /// after a focus click. <c>OnPileToggled</c> already derives it the same way
    /// (<c>bool readOnly = !ReferenceEquals(presentedHand, gameHand)</c>, this file), so the two
    /// cannot drift.</para>
    ///
    /// <para>IT IS NOT A TURN TEST AND MUST NEVER BECOME ONE. "Whose turn is it" has no say here —
    /// the 2026-08-08 ruling ("das Wechseln darf nie blockiert sein") stands, and the character
    /// switch this gate reacts to keeps working exactly as ModBuild 247 shipped it. This only stops
    /// the board from ADVERTISING a placement it will refuse.</para>
    ///
    /// <para>MULTIPLAYER: a pure local-view question (which character THIS client's board shows).
    /// The peer mirror carries the wanted-slot mask and the banner from the sender's own board, so a
    /// suppressed overlay is suppressed identically for every viewer of that board, and nothing new
    /// goes on the wire.</para>
    /// </summary>
    private bool PlacementIsOffered(CardsHandUI? gameHand)
    {
        if (gameHand == null)
            return false;
        CardsHandUI? presented = Board.CharacterFocus.PresentedHand(gameHand);
        bool offered = ReferenceEquals(presented, gameHand);
        LogPlacementOffer(gameHand, presented, offered);
        return offered;
    }

    /// <summary>Change-gated key for <see cref="LogPlacementOffer"/>: (offered, game actor id,
    /// presented actor id). One line per transition, never per frame.</summary>
    private (bool offered, int owner, int shown)? _loggedPlacementOffer;

    /// <summary>
    /// THE FALSIFIER FOR <see cref="PlacementIsOffered"/>. It prints the two identities the verdict
    /// is made of — who the game is asking, who the board is showing — so a hardware log can convict
    /// this gate either way: an overlay still up while the line says <c>offered=False</c> means the
    /// suppression did not reach that surface, and a MISSING overlay while it says
    /// <c>offered=True</c> means the cause is elsewhere entirely. A line that never appears at all
    /// means the pick tick is not running.
    /// </summary>
    private void LogPlacementOffer(CardsHandUI gameHand, CardsHandUI? presented, bool offered)
    {
        CPlayerActor? owner = gameHand.PlayerActor;
        CPlayerActor? shown = presented != null ? presented.PlayerActor : null;
        // While the answer is OPEN the identities are irrelevant (they are the same hand by
        // definition), so an ordinary game-driven character switch does NOT produce a line — the
        // key collapses to the verdict alone. A WITHHELD answer keys on both identities, because
        // "which pair is it refusing for" is exactly the question a report would ask.
        var key = offered
            ? (true, 0, 0)
            : (false, owner != null ? owner.ID : 0, shown != null ? shown.ID : 0);
        if (_loggedPlacementOffer.HasValue && _loggedPlacementOffer.Value.Equals(key))
            return;
        _loggedPlacementOffer = key;
        VRLog.Info("Cards", $"Placement offer {(offered ? "OPEN" : "WITHHELD")}: the game is presenting " +
                            $"'{Board.CharacterFocus.Describe(owner)}' and the control board is showing " +
                            $"'{Board.CharacterFocus.Describe(shown)}'" +
                            (offered
                                ? " — the same hand, so the board may offer a placement: the wanted-slot " +
                                  "hint and the pick banner/keycaps are live."
                                : " — a DIFFERENT hand, so no card of the shown character can be laid down " +
                                  "here (Rebuild builds a focus view read-only: no drop field, nothing " +
                                  "grabbable). The wanted-slot hint and the pick banner/keycap overrides " +
                                  "are therefore withheld — the board must never advertise a placement it " +
                                  "will refuse (user ruling 2026-08-24). The character switch itself is " +
                                  "untouched.") +
                            " Local view only — no game state read or written, nothing on the wire.");
    }

    /// <summary>
    /// The modal pick MODES (poke-select fan flows; drop-field flows since test #21) — a latch the
    /// game never clears. Forwards to <see cref="CardsGameApi.IsPickMode"/> so the mod has one
    /// definition. Ask <see cref="PickFlowLive"/> instead wherever the question is "is the game
    /// asking for a card RIGHT NOW"; this one answers TRUE for minutes after a burn is over.
    /// </summary>
    private static bool IsPickMode(CardHandMode mode) => CardsGameApi.IsPickMode(mode);

    /// <summary>
    /// IS A MODAL PICK LIVE FOR THIS HAND — the one term every gate in this driver reads, so the
    /// burn flow's teardown reaches all of its surfaces at once rather than three of ten. See
    /// <see cref="CardsGameApi.PickFlowLive"/> for the mechanism and the hardware evidence.
    /// </summary>
    private static bool PickFlowLive(CardsHandUI? hand) => CardsGameApi.PickFlowLive(hand);

    private void OnPileTogglePoked(PileKind kind, VRHand hand)
    {
        if (_browser.IsOpen && _browser.Kind == kind)
        {
            CloseBrowser($"USER stack toggle ({hand.Side}) — poked/clicked the open {kind} stack again");
            return;
        }
        OpenBrowser(kind, $"USER stack toggle ({hand.Side})");
    }

    // NOTE: OnPileGrabOpened/OnPileGrabReleased (pinch-to-browse-while-held) are gone — the
    // stack grab that raised them was removed for ALL pile kinds (PileStack.CanGrab, user
    // report 2026-08-06). The poke/laser toggle above is the only way a browse fan opens.

    /// <summary>
    /// Raise the discard/burnt browse arc. <paramref name="trigger"/> names WHO asked and is
    /// logged verbatim — the never-auto-open audit trail (user report 2026-08-07). There is
    /// exactly ONE caller, <see cref="OnPileTogglePoked"/>, i.e. a deliberate stack poke or
    /// laser click: no game event, decision phase or flow may open a pile fan. If a future
    /// hardware log ever shows this line with a non-USER trigger, that caller is the bug.
    /// </summary>
    private void OpenBrowser(PileKind kind, string trigger)
    {
        // THE HAND THE BOARD IS PRESENTING, not the one the game presents. THIS LINE IS THE FIX for
        // "Beim Test konnte ich den Fächer eines Characters der nicht am Zug war nicht öffnen"
        // (user, hardware ModBuild 89). The browser used to latch CurrentHand() here while
        // Rebuild's UpdateBrowser is handed CharacterFocus.ResolveHand(CurrentHand()) — so the
        // instant a focus view was open the two disagreed BY CONSTRUCTION and the very next rebuild
        // closed the fan as a "context change". The hardware log shows it three times in a row:
        // "PILE BROWSE OPEN: Discard (mode=ActionSelection)" immediately followed by
        // "PILE BROWSE CLOSE: Discard — trigger: context change (mode=CardsSelection,
        // handSwitch=True)" (LogOutput.log:5443-5451) — two different characters' hands and two
        // different characters' stale CardHandModes, compared as if they were one.
        CardsHandUI? gameHand = CurrentHand();
        CardsHandUI? presentedHand = Board.CharacterFocus.PresentedHand(gameHand);
        Transform? anchor = AnchorParent();
        if (presentedHand == null || anchor == null)
            return;
        CardHandMode mode = CardsGameApi.Mode(presentedHand);
        // Derived here rather than read off CharacterFocus.ReadOnlyView, which is latched by the
        // edge-driven rebuild: a poke landing in the frame between the focus click and its rebuild
        // would otherwise be judged against the PREVIOUS view's answer. Same definition, live.
        bool readOnly = !ReferenceEquals(presentedHand, gameHand);
        if (!BrowseAllowed(kind, presentedHand, mode, readOnly, out string? refusal))
        {
            // HW-VERIFY: the anchored line for "the burnt/discard fan would not open during a
            // decision". A refusal here now has to name a pile the pick is holding IN FULL; a
            // refusal naming a pile the pick borrows nothing from would mean PickOwnsPileVisuals
            // is reading the wrong set.
            VRLog.Note("Cards", $"PILE BROWSE REFUSED: {kind} — {refusal} (trigger: {trigger}).");
            return;
        }
        // ONE PILE FAN AT A TIME, AND ONLY ONCE THIS ONE IS ACTUALLY OPENING. The eviction used to
        // sit in PileViewer.DispatchPoke, i.e. BEFORE this gate ran, so a refused discard/burnt poke
        // still tore the item fan down and gave nothing back — which is how the co-player's log
        // reads ("ITEM FAN CLOSE — trigger: discard/burnt stack poked (Discard)" followed by a
        // PILE BROWSE REFUSED). Moving it here makes a refusal cost the player nothing.
        if (_piles.ItemsBrowseOpen)
            _piles.CloseItemsBrowse($"{kind} browse arc opening — one pile fan at a time (both fans " +
                                    "share the board-top anchor, so they would draw through each other)");
        _browseHand = presentedHand;
        _browseMode = mode;
        // Requirement 5 (emerge): pass the pile stack's world position so the arc's cards
        // fly OUT of the stack instead of popping in (existing home-lerp does the easing).
        Vector3? emergeFrom = _piles.TryGetPileWorld(kind, out Vector3 pileWorld, out _)
            ? pileWorld : (Vector3?)null;
        _browser.Open(kind, anchor, emergeFrom);
        // Fresh fan → fresh borrow ledger (the -1 sentinel makes the first refresh always log).
        _browseBorrowed = -1;
        _browseLeftOnBoard = 0;
        // HW-VERIFY: the positive half of "die Fächer müssen IMMER aufgehen". One line per
        // deliberate stack poke — bounded by the player's hands, never per frame. A poke that
        // produces NEITHER this line nor a REFUSED line means the poke never reached the driver,
        // which is a different defect from the gate.
        VRLog.Note("Cards", $"PILE BROWSE OPEN: {kind} of " +
                            $"'{Board.CharacterFocus.Describe(presentedHand.PlayerActor)}' " +
                            $"(mode={mode}, focusView={readOnly}) — " +
                            $"trigger: {trigger}. The pile is openable in EVERY phase and for EVERY " +
                            "character the board can present — whose turn it is has no say (user " +
                            "ruling 2026-08-08); it follows a focus switch instead of closing. A " +
                            "mode= naming a pick flow here is EXPECTED and correct: the mode alone " +
                            "no longer refuses anything — only a pick flow physically holding " +
                            "EVERY card of THIS pile does.");
        _dirty = true; // content fills in Rebuild.UpdateBrowser
    }

    /// <summary>
    /// Close-on-foreign-interaction watchdog (test #22, item 8): while a pile browse
    /// is open, ANY interaction that is not part of the browse itself dismisses it —
    /// grabbing a hand/tray card, pressing a board button, a rest toggle, an action
    /// play. Every foreign-interaction seam funnels through this ONE close path
    /// (logged with its trigger) instead of scattering CloseBrowser calls across the
    /// handlers. Lifecycle closes (hands-down, hand destroyed, mode/dialog change,
    /// pile emptied) keep their own paths — those are reversibility guarantees (item
    /// D / test #21 C), not user interactions.
    /// </summary>
    private void ForeignInteraction(string source)
    {
        if (_browser.IsOpen)
            CloseBrowser($"foreign interaction: {source}");
        // Requirement 4: the item fan closes on the SAME foreign-interaction / click-away seams the
        // discard/burnt ability browser does (board button, ability-card grab, rest toggle, action
        // play, initiative swap, click-away). Item-fan-OWN interactions (poke the item stack, grab an
        // item chip, drop into the use slot) never route through here, so the fan stays open for them.
        if (_piles.ItemsBrowseOpen)
            _piles.CloseItemsBrowse($"foreign interaction: {source}");
    }

    /// <summary>
    /// Does the CONTROL BOARD currently own this card's ONE physical visual?
    ///
    /// WHY THIS EXISTS: a game ability card has exactly one VRCard visual (the face is the
    /// game's own live <c>FullAbilityCard</c> rect re-parented onto the VR card — it cannot be
    /// in two places at once), while the same underlying card can be in two LOGICAL places at
    /// once. The reported bug is exactly that overlap: once this turn's two played cards have
    /// resolved, the game has already moved them into <c>DiscardedAbilityCards</c>, yet the
    /// board must keep showing them lying in front of the player until the turn ends. Opening
    /// the DISCARD fan therefore listed them as fan cards, and closing the fan swept them into
    /// the stack — the board went empty mid-turn (user report, ModBuild 18, tutorial).
    ///
    /// So: a card the board is showing is NOT the fan's to borrow, lay out, lift or put away.
    /// The zones are the ones the rebuild's own park sweep treats as "on the board": the docked
    /// played/round cards (<see cref="_halfBuffer"/>), a card lying in a play slot, a pick-mode
    /// drop-field occupant, the short-rest sacrifice card, and the ACTIVE column. All of these
    /// are resolved for the current rebuild before the browser refresh runs (see the ordering
    /// note in Rebuild), and they keep their last-rebuild value between rebuilds — which is
    /// what the close path (outside Rebuild) needs. Read-only: no game state is touched.
    /// </summary>
    private bool BoardOwnsCardVisual(VRCard card) =>
        _halfBuffer.Contains(card)              // this turn's played (round) cards, docked on the board
        || _tray.SlotOf(card) >= 0              // a selected card lying in a play slot
        || _fieldCards.Contains(card)           // pick-mode drop-field occupant
        || ReferenceEquals(card, _shortRestCard) // short-rest sacrifice card in the left recess
        || _active.Contains(card);              // the permanently-shown ACTIVE column (feature 6)

    // Fan-borrow bookkeeping for the close-path Info line (requirement 5): how many pile cards
    // the LAST browse refresh actually borrowed into the arc, and how many it deliberately left
    // on the control board. Reset on open; re-stamped on every refresh.
    private int _browseBorrowed;
    private int _browseLeftOnBoard;
    private float _nextBrowseLedgerLogAt; // unscaled-time throttle for the close ledger line

    private void CloseBrowser(string reason)
    {
        if (!_browser.IsOpen)
            return;
        // HW-VERIFY: the other half of the same question — a fan that opened and then vanished
        // reads here, with the reason verbatim. One line per close, and a close needs an open.
        VRLog.Note("Cards", $"PILE BROWSE CLOSE: {_browser.Kind} — trigger: {reason}.");
        _browseHand = null;
        ClearBrowseHover();
        // Requirement 2 (collapse-into-stack for discard/burnt): before the browser closes + the
        // rebuild parks the adopted cards, fly each card DOWN into ITS OWN pile stack (reverse of the
        // emerge). The park sweep skips IsFlying cards, so the fly runs to completion and its callback
        // parks each card on arrival — exactly the item fan's collapse feel, for the ability browser.
        StartBrowseCollapse();
        _browser.Close();
        _dirty = true; // next rebuild parks any browsed cards that did not launch a collapse fly
    }

    /// <summary>
    /// Requirement 2: launch a fly-to-pile collapse for every card in the closing browse arc, into the
    /// browsed pile's OWN stack (discard fan → discard stack, burnt fan → burnt stack). Each card is
    /// re-parented OUT of the browser root first (worldPositionStays) so it keeps animating after the
    /// root deactivates on Close; the shared <see cref="_flyingToPile"/> set keeps it out of the park
    /// sweep, and the completion callback hands the adopted face back via <see cref="VRCardFactory.Park"/>
    /// — same lifecycle as the played-card fly-to-pile. No-op (cards park instantly, as before) when the
    /// target stack is off/not built.
    /// </summary>
    private void StartBrowseCollapse()
    {
        PileKind? kind = _browser.Kind;
        if (kind == null)
            return;
        if (!_piles.TryGetPileWorld(kind.Value, out Vector3 worldPos, out float slabWidth))
            return; // pile offscreen / not built → the rebuild park sweep hides the cards instantly

        Transform? anchor = AnchorParent();
        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        IReadOnlyList<VRCard> cards = _browser.Cards;
        int launched = 0;
        int skippedBoardOwned = 0;
        int borrowed = cards.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            VRCard card = cards[i];
            // A HELD card is skipped here (the collapse never yanks a card out of the player's
            // hand) - that is the moment its pile loan outlives this ledger, and exactly why the
            // origin travels ON the card (VRCard.PileOrigin): its eventual release routes it back
            // to this pile via ReturnCardToPile instead of falling into the hand-fan routing.
            if (card == null || card.IsHeld || card.IsFlying || _flyingToPile.Contains(card)
                || !card.gameObject.activeInHierarchy)
                continue;

            // A CARD THE BOARD IS STILL SHOWING IS NOT THE BROWSER'S TO PUT AWAY. Belt-and-braces:
            // UpdateBrowser already refuses to borrow a board-owned visual into the arc, so this
            // list should hold only the fan's own cards. It can still go stale between the last
            // rebuild and this close (a card that became a docked played / active card in the
            // meantime), and closing a fan must clear THAT fan — never the cards lying on the
            // board. Such a card is still re-parented out, so it survives the browser root
            // deactivating, and the rebuild that follows re-homes it into its board zone.
            if (anchor != null)
                card.transform.SetParent(anchor, worldPositionStays: true); // survive the root deactivation
            if (BoardOwnsCardVisual(card))
            {
                skippedBoardOwned++;
                continue;
            }
            _flyingToPile.Add(card);
            VRCard flying = card;
            PileKind dest = kind.Value;
            card.FlyToPile(worldPos, slabWidth, FlyToPileSeconds, arcUp, () =>
            {
                _flyingToPile.Remove(flying);
                _factory.Park(flying);
                VRLog.Info("Cards", $"Browse collapse: '{flying.name}' reached the {dest} stack — parked.");
            }, minArc);
            launched++;
        }
        if (launched > 0 || skippedBoardOwned > 0)
            VRLog.Info("Cards", $"Browse collapse: {launched} {kind.Value} card(s) fly back into their stack " +
                                $"({FlyToPileSeconds:F2}s) before parking — the discard/burnt fan collapses like the item fan. " +
                                $"{skippedBoardOwned} card(s) left alone because the control board is still showing them " +
                                "(closing a fan clears that fan, not the cards lying on the board).");

        // Requirement 5 (borrow ledger, throttled): ONE line that proves from the hardware log alone
        // that a fan only ever puts away what it itself borrowed. borrowed = the arc's own visuals at
        // close; returned = the ones actually flown into the stack (+ any board-owned straggler the
        // guard above caught); left on the board = the pile cards this fan REFUSED to borrow because
        // the board is showing them (this turn's played cards, active column, slot/field occupants).
        // Throttled so a pathological open/close loop cannot flood the log; a normal user close is
        // far slower than the window, so every real close still prints.
        float now = Time.unscaledTime;
        if (now >= _nextBrowseLedgerLogAt)
        {
            _nextBrowseLedgerLogAt = now + 0.25f;
            VRLog.Info("Cards", $"Pile fan ledger ({kind.Value}): borrowed {borrowed} card visual(s), " +
                                $"returned {launched} to the stack ({skippedBoardOwned} straggler(s) handed back to the " +
                                $"board instead); {_browseLeftOnBoard} pile card(s) were NEVER borrowed because the " +
                                "control board is showing them (this turn's played cards stay put until the turn ends).");
        }
    }

    /// <summary>
    /// Release routing for a card that is ON LOAN from a pile stack (<see cref="VRCard.PileOrigin"/>):
    /// back into the open browse arc when it is still browsing that pile, otherwise a fly into the
    /// pile's own stack (the same arc + park lifecycle as <see cref="StartBrowseCollapse"/>), with an
    /// instant park when the stack is off / not built. NEVER the hand fan.
    ///
    /// ROOT CAUSE (user report 2026-08-04, "abgeworfene Karte im Handfaecher"): before this path
    /// existed, a pile card whose browse arc had closed UNDERNEATH the hold fell through
    /// OnCardReleased's browse branch (browser closed -> IsOpen false / list cleared) into the
    /// normal hand-card routing, whose void case is "_fan.Add(card)" - a DISCARDED card entered the
    /// hand fan (hardware log: "Pile browse CLOSE (foreign interaction: click-away ...)" with ledger
    /// "borrowed 1 ... returned 0" at 3951-3952, then "Drop (Right): ... rule=none -> return to fan"
    /// at 4028, fan n=4 -> n=5). The next rebuild reconciled it away again (the game's hand pile
    /// never contained it), which is exactly the reported "grabbed it again, released, vanished".
    ///
    /// No NetCardFx report on purpose: the browse collapse does not report either - the peer's view
    /// of the arc is driven by the browse extras block, and a hand-release flight into a stack the
    /// peer never saw borrowed would render a phantom flight. The wire format is untouched.
    /// </summary>
    private void ReturnCardToPile(VRCard card, PileKind kind, VRHand hand)
    {
        // The arc is still open on this very pile: the ordinary pluck-return (item 5).
        if (_browser.IsOpen && _browser.Kind == kind)
        {
            _browser.Add(card);
            return;
        }
        // Already animating into a stack - its own completion callback parks it; never double-launch.
        if (card.IsFlying || _flyingToPile.Contains(card))
            return;
        // Survive any parent teardown while flying (the hand's grab anchor releases this frame),
        // exactly like the collapse re-parents cards out of the deactivating browser root.
        Transform? anchor = AnchorParent();
        if (anchor != null)
            card.transform.SetParent(anchor, worldPositionStays: true);
        if (_piles.TryGetPileWorld(kind, out Vector3 worldPos, out float slabWidth)
            && card.gameObject.activeInHierarchy)
        {
            _flyingToPile.Add(card);
            VRCard flying = card;
            card.FlyToPile(worldPos, slabWidth, FlyToPileSeconds, BoardUp(), () =>
            {
                _flyingToPile.Remove(flying);
                _factory.Park(flying);
                VRLog.Info("Cards", $"Pile-origin return: '{flying.name}' reached the {kind} stack - parked.");
            }, BoardArcMin());
            VRLog.Info("Cards", $"Pile-origin return ({hand.Side}): '{card.name}' released while its {kind} " +
                                $"browse is closed - flying back into the {kind} stack ({FlyToPileSeconds:F2}s), " +
                                "never into the hand fan (discarded/burnt cards are not hand cards).");
        }
        else
        {
            _factory.Park(card);
            VRLog.Info("Cards", $"Pile-origin return ({hand.Side}): '{card.name}' released while its {kind} " +
                                $"browse is closed and the stack is off/not built - parked instantly, " +
                                "never into the hand fan (discarded/burnt cards are not hand cards).");
        }
        _dirty = true; // the next rebuild re-asserts every zone from game truth
    }

    /// <summary>
    /// Rebuild-time browse refresh: RE-TARGET the open arc onto whichever character the board is
    /// presenting, and mirror that character's authoritative pile into it. Content comes from the
    /// same widgets the 2D pile viewer re-parents (see CardsGameApi.GetPileWidgets), adopted
    /// read-only.
    ///
    /// <para>WHAT THIS METHOD USED TO DO, AND WHY IT WAS THE BUG. It closed the fan on ANY change
    /// of hand or mode ("context change", test #21 item C). Two things made that fatal rather than
    /// merely strict. First, <see cref="OpenBrowser"/> latched <c>CurrentHand()</c> while this
    /// method is handed <c>CharacterFocus.ResolveHand(CurrentHand())</c>, so with a focus view open
    /// the very first rebuild after the open ALWAYS saw a "hand switch" that never happened —
    /// three consecutive open/close pairs in the hardware log, LogOutput.log:5443-5451. Second,
    /// even with that repaired, closing on a genuine character switch is the wrong behaviour now:
    /// the user's ruling is that the fan must re-target ("Je nachdem welcher Character im Fokus ist
    /// soll auch das richtige Pile im Fächer geöffnet werden"), not vanish.</para>
    ///
    /// <para>So the fan now survives every switch and simply changes what it is about. It closes on
    /// exactly three things: the pile it is showing became empty, the browse gate shut
    /// (<see cref="BrowseAllowed"/> — a modal pick flow or a modal dialog), or somebody asked it to
    /// (a stack toggle, a foreign interaction, a board/scenario teardown). MULTIPLAYER follows for
    /// free and by construction: the extras browse block carries the pile KIND and the arc's card
    /// COUNT, while the peer resolves WHICH character through record 22
    /// (<c>Net.RemoteBoardFocus.DisplayedActor</c>) and reads the card fronts from that character's
    /// own host-replicated pile — the same list, in the same order, that fills the arc here.</para>
    /// </summary>
    private void UpdateBrowser(CardsHandUI hand, CardHandMode mode)
    {
        if (!_browser.IsOpen)
            return;
        // ReadOnlyView is exact here: this rebuild's own ResolveHand latched it a few lines ago.
        if (!BrowseAllowed(_browser.Kind ?? PileKind.Discard, hand, mode,
                           Board.CharacterFocus.ReadOnlyView, out string? refusal))
        {
            CloseBrowser(refusal!);
            return;
        }
        // RE-TARGET, never close. A character switch (focus click, turn hand-off) or a mode change
        // re-points the SAME arc at the newly presented character's pile; the content fill below
        // does the actual work, and the zone loop that runs after this rebuild parks whatever the
        // previous character had lent it.
        if (!ReferenceEquals(hand, _browseHand) || mode != _browseMode)
        {
            VRLog.Info("Cards", $"PILE BROWSE RE-TARGET: {_browser.Kind} now shows " +
                                $"'{Board.CharacterFocus.Describe(hand.PlayerActor)}' " +
                                $"(was '{Board.CharacterFocus.Describe(_browseHand?.PlayerActor)}', " +
                                $"mode {_browseMode} → {mode}). The fan stays OPEN across a character " +
                                "switch by user ruling — it follows the focus instead of closing, and " +
                                "the previous character's borrowed visuals are parked by this same " +
                                "rebuild's zone sweep.");
            _browseHand = hand;
            _browseMode = mode;
            _browseBorrowed = -1; // fresh borrow ledger for the new character
            _browseLeftOnBoard = 0;
            // The new character's visuals are freshly built at the factory pool, so without this
            // they would sail into the arc from the pool root. Re-seat them on the pile stack and
            // let the existing home lerp fly them up — the same emerge the first open gets.
            if (_piles.TryGetPileWorld(_browser.Kind ?? PileKind.Discard, out Vector3 stackWorld, out _))
                _browser.SeedEmerge(stackWorld);
        }

        bool burnt = _browser.Kind == PileKind.Burnt;
        PileKind kind = burnt ? PileKind.Burnt : PileKind.Discard;
        CardsGameApi.GetPileWidgets(hand, burnt, _pileWidgetBuffer);
        _browseBuffer.Clear();
        int pileCount = 0;    // cards the GAME has in this pile (the truth behind the title count)
        int leftOnBoard = 0;  // of those, the ones whose visual the board is showing right now
        for (int i = 0; i < _pileWidgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _pileWidgetBuffer[i];
            // THE ARC'S MEMBERSHIP TEST, AND IT IS HALF OF A WIRE CONTRACT. `arrived` below becomes
            // the browse block's COUNT, which a peer uses as the number of slabs to fill from its own
            // walk of this same pile — so this test must be the SAME EXPRESSION on both machines or
            // the peer's slab i stops naming the owner's card i (multiplayer report item 5c). It is
            // shared rather than duplicated for exactly that reason; see
            // CardsGameApi.PileWidgetIsArcMember.
            if (!CardsGameApi.PileWidgetIsArcMember(widget))
                continue;
            pileCount++;
            VRCard card = AdoptedCard(widget);
            // THE FAN ONLY EVER BORROWS ITS OWN VISUALS. A card the control board is currently
            // showing — above all this turn's two PLAYED cards, which the game has already moved
            // into the discard pile while they must stay lying in front of the player until the
            // turn ends — is not the fan's. Borrowing it made it a fan card in every sense: the
            // arc re-homed it, the hand sweep lifted it, it became grabbable-as-browse, and the
            // collapse-on-close flew it into the stack, which is the reported bug (the board went
            // empty the moment the discard fan was opened and closed again). Skipping it here is
            // the single point where that ownership is decided; every downstream browse path
            // (layout, hover, pluck-return, collapse) then simply never sees it.
            // …AND A CARD THAT IS STILL ON ITS WAY IN IS NOT THE FAN'S EITHER (pile-arrival rule,
            // user report "der Pile … soll sich erst unmittelbar aktualisieren, wenn die jeweiligen
            // Karten IN den Pile fliegen"). BoardOwnsCardVisual names the zones a card is PARKED in;
            // it says nothing about a card mid-flight, mid-vanish or held by its burn artwork —
            // which is precisely the state a card is in for the ~0.4 s after the model discarded it.
            // Borrowing one of those would have the arc snatch a flying card out of its own
            // animation and lay it in the fan (a browse opened mid-flight was exactly that), and it
            // would also make the arc disagree with the stack label, which now defers those same
            // cards (CardsDriver.PendingPileArrivals). One classifier, both surfaces.
            // …AND A CARD THE LIVE PICK FLOW IS SHOWING IS NOT THE FAN'S EITHER. This is the
            // clause that lets BrowseAllowed stop refusing on a PARTIAL overlap: a pick that
            // borrows only some of this pile (or a different pile entirely) no longer costs the
            // player the whole fan — the arc opens and quietly leaves the borrowed cards where the
            // pick has them. _fieldCards is already covered by BoardOwnsCardVisual; the pick FAN
            // was the one board zone the browse filter never knew about, which is why the mode gate
            // had to stand in for it with a blanket refusal.
            if (BoardOwnsCardVisual(card) || CardEnRouteToPile(card) || _fanBuffer.Contains(card))
            {
                leftOnBoard++;
                continue;
            }
            // PILE-ORIGIN MARKER (discard-in-hand-fan bug 2026-08-04): the borrow is the single
            // point where a pile card becomes a fan visual, so this is the single stamping
            // point. The marker lives on the CARD (see VRCard.PileOrigin) because the browser's
            // own list - the release routing's previous only origin record - is cleared by
            // PileBrowser.Close() while the card can still be in the player's hand, after which
            // the release routed the discard card into the HAND fan (hardware log 3951/4028).
            // Retired by the Rebuild zone loop / OnDisable once game truth re-homes the card.
            card.PileOrigin = kind;
            _browseBuffer.Add(card);
        }
        // ARRIVED = the pile as the PLAYER sees it: the model's cards minus the ones whose visual is
        // still on the board / in flight. It is `_browseBuffer.Count` by construction (the loop
        // above put every arrived card in it and counted every other one into leftOnBoard), named
        // here so the two statements below read as the one rule they are.
        int arrived = pileCount - leftOnBoard;
        // THE TITLE IS THE ARRIVED COUNT, NOT THE MODEL COUNT (user report: "So kann es sein, dass
        // zwar im Pile '1' steht, wenn man ihn aber öffnen will nichts angezeigt wird. Das soll so
        // nicht sein"). It used to be the TRUE model size deliberately — "the player is told what
        // the pile holds, even when some of those cards are physically on the board" — but that is
        // the very mismatch the report is about, and the stack LABEL next to it now defers by the
        // same rule (PileViewer.TickStatus → CardsDriver.PendingPileArrivals). A title that
        // disagreed with both the arc under it and the stack it came out of has nobody left to be
        // right for.
        //
        // …AND THE CLOSE FOLLOWS IT. Auto-closing on the model count would leave an arc open and
        // empty under a "(2)" while both cards still lie on the board; auto-closing on the arrived
        // count closes it exactly when there is nothing to look at, which is also when the stack
        // reads 0 — so "empty" means the same thing on the stack, in the title and in the fan. The
        // pile is not made un-openable by this: it is openable in every phase (BrowseAllowed has no
        // count gate at all), and it fills the instant the cards land.
        if (arrived <= 0)
        {
            CloseBrowser(pileCount == 0
                ? "pile empty"
                : $"pile empty for now — all {pileCount} card(s) the model lists are still on their " +
                  "way in (lying on the board / burning / in flight); the fan re-opens with them " +
                  "the moment they land");
            return;
        }
        _browser.SetCards(_browseBuffer, $"{PileViewer.Caption(kind)} ({arrived})");

        // Borrow ledger for the close-path Info line (requirement 5), plus a change-gated line here
        // so the log also shows what the OPEN fan decided to borrow.
        if (_browseBorrowed != _browseBuffer.Count || _browseLeftOnBoard != leftOnBoard)
        {
            _browseBorrowed = _browseBuffer.Count;
            _browseLeftOnBoard = leftOnBoard;
            VRLog.Info("Cards", $"Pile fan content ({kind}): borrowed {_browseBuffer.Count} of {pileCount} pile " +
                                $"card(s) into the arc; {leftOnBoard} left on the control board (a played/active/" +
                                "slotted card keeps its one visual on the board — the fan never takes it).");
        }
    }

    // ------------------------------------------------------------------ active cards --

    // The active-card set last shown, for the change-deduped Info line (feature 6).
    private int _loggedActiveCount = int.MinValue;

    /// <summary>
    /// Rebuild-time refresh of the ACTIVE CARDS column (feature 6): mirror the character's
    /// active-ability pile (<c>CardPileType.Active</c>) into the permanently-shown column
    /// off the board's right edge. Cards are adopted read-only through the SAME
    /// <see cref="AdoptedCard"/> path as the pile browse; the active HALF/halves of each
    /// are resolved (<see cref="CardsGameApi.GetActiveHalves"/>) and highlighted. Empty →
    /// the area shows nothing (always on — user ruling 2026-08-11). The zone-flag loop keeps these
    /// cards grabbable-to-read and out of the park sweep; their release routes back to the
    /// column (never a game seam). Logs the active count change-deduped.
    /// </summary>
    private void UpdateActive(CardsHandUI hand)
    {
        // THERE IS NO TURN GATE ON THIS COLUMN, and that removal is user item 4 (2026-09-07):
        // "Die aktive Karte ist lokal mal aufgetaucht und wieder verschwunden als ein anderer
        // Spieler dran war. Auch hier hat das remote board sie richtig angezeigt." Plus, of the
        // same report: "Auf jeden Fall muss es auch lokal bei dem jeweiligen Character direkt nach
        // einer Aktivierung direkt angezeigt werden."
        //
        // WHAT USED TO BE HERE, and why it was wrong. The gate read
        // `!IsPresentedActorTurn(hand) && !IsSelectionPhase(hand)` and, when it closed, hid the
        // whole column and reported SUPPRESSED. It was task #5 applied to the wrong surface. #5 is
        // "clear the control board after the own turn" and it is about the two PLAYED (round) cards
        // docked in the board's recesses — cards that belong to ONE turn and must not still be lying
        // there while an enemy is up. That requirement has its own choke point and keeps it:
        // Board.CharacterFocus.RoundCardDock, consulted from CardsDriver.4's ActionSelection case.
        // An ACTIVE card is the opposite kind of object: it is active precisely BECAUSE it outlives
        // the turn that played it, and it stays active across turns and rounds until its bonus
        // expires. There is no game state in which "it is not your turn" makes an active card not
        // active, so there is none in which the column that shows it may be empty. This method's own
        // summary above has said so since 2026-08-11 — "Empty → the area shows nothing (always on —
        // user ruling 2026-08-11)" — and the gate contradicted that sentence for every build it
        // stood.
        //
        // MEASURED, ModBuild 470, the pair of logs from the 2026-09-07 session. One card was active,
        // 151:ABILITY_CARD_TheMindsWeakness, continuously. The peer's mirror of that board
        // (Net.RemoteActiveCards, which has never had a turn gate) changed its belief exactly ONCE,
        // at its frame 53308, and held that same picture for the remaining 75 census lines of the
        // session. The owner's own board over the same span logged "Active cards: 1 shown" TEN
        // separate times — ten re-appearances, so nine disappearances — and its census row read
        // SUPPRESSED [… turn gate] in between, e.g. host frame 80618 drew [151:…] and host frame
        // 80955 was SUPPRESSED, 337 frames later, with the mirror still drawing it.
        //
        // NOTHING IS ON THE WIRE HERE AND NOTHING NEEDED TO BE. Both surfaces resolve the identical
        // in-memory list on the SAME machine in the SAME frame: this method reaches it through
        // CardsGameApi.GetActivePileWidgets → ActiveCardSet.ActivatedCards(actor), and
        // RemoteActiveCards.Refresh calls ActiveCardSet.ActivatedCards(actor) directly. The rules
        // model of every actor is simulated on every client, so the "remote board" is a SECOND VIEW
        // OF THE SAME LOCAL MODEL rather than a mirror of replicated data. That is why the peer's
        // picture could be right while the owner's was wrong with no replication lag to blame, and
        // it is why deleting a gate — not adding a field — is the whole fix.
        //
        // THE SELECTION PHASE IS UNAFFECTED because it was already inside the gate's OR. An active
        // card is public by construction and the mirror draws its FRONT in every phase
        // (RevealGate.PeerCardPopulation.AlreadyPublic, RemoteActiveCards.Refresh); the owner's
        // column now agrees with that in every phase too, which is the standing face ruling read the
        // only way that keeps the two boards 1:1.
        _active.EnsureBuilt(_tray);
        CardsGameApi.GetActivePileWidgets(hand, _activeWidgetBuffer);
        _activeBuffer.Clear();
        _activeModelBuffer.Clear();
        _activeFlightBuffer.Clear();
        _activeIdBuffer.Clear();
        for (int i = 0; i < _activeWidgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _activeWidgetBuffer[i];
            if (widget.AbilityCard == null || widget.IsLongRest)
                continue;
            VRCard card = AdoptedCard(widget);
            CardsGameApi.GetActiveHalves(hand, widget.AbilityCard, out bool top, out bool bottom);
            SetActiveHighlight(card, top, bottom); // native game action-region highlight
            _activeBuffer.Add(card);
            _activeModelBuffer.Add(widget.AbilityCard);
            // THE KEY IS CardInstanceID, NOT CardID. AbilityCardUI.CardID is the card DATA id
            // (AbilityCardUI.cs:22, and the game itself never trusts it alone — its own membership
            // tests read `e.ID == CardID && e.CardInstanceID == CardInstanceID`, AbilityCardUI.cs:197).
            // A data id repeats across scenarios and characters, so a mark left standing when a
            // scenario ended would silently suppress the SAME card's flight in the next one. The
            // instance id is the identity RevealGate and RemoteControlBoard's departed-face memory
            // already key on, so this surface now agrees with them.
            int instanceId;
            try { instanceId = widget.AbilityCard.CardInstanceID; }
            catch { continue; }
            _activeIdBuffer.Add(instanceId);

            // ─── THE END-OF-TURN FLIGHT INTO THE ACTIVE COLUMN (user item 7, 2026-09-06 late) ───
            // "Wenn eine Karte aktiv ist, soll ihre Animation am Ende des Zuges auch in den
            // Aktiv-Stapel gehen, wo sie dann angezeigt wird (remote und lokal). So wie die anderen
            // Karten ihre Animation im Abgelegt- oder Verbrannt-Stapel haben."
            //
            // THE TRIGGER IS THE GAME'S OWN END-OF-TURN PILE MOVE, not a widget-list edge. The
            // game's drain is CCharacterClass.DiscardRoundAbilityCards (CCharacterClass.cs:505),
            // which routes each round card through MoveAbilityCardToPile (CCharacterClass.cs:418);
            // that method's FIRST test is `if (abilityCard.ActiveBonuses.Count > 0) eCardPile =
            // Activated`, and the Activated branch moves the card out of RoundAbilityCards into
            // m_ActivatedCards. The observable, per-card form of exactly that transition is "this
            // card was lying in one of my board's ROUND RECESSES on the previous rebuild, and the
            // active list holds it now" — which is what _lastHalfCards (snapshotted at the top of
            // Rebuild, before any of this pass's zone work) answers.
            //
            // IT REPLACES A TRIGGER THAT FIRED ON THE WRONG EVENT AND FIRED IT REPEATEDLY. The
            // previous test was `!_activeShown.Contains(widget.CardID)`, an edge on the game's
            // active-pile WIDGET list — and _activeShown was cleared by the turn gate this method
            // used to open with, which fired on every turn that was not this character's. So a card
            // that went active once re-flew every time the column came back up. That gate is GONE as
            // of user item 4 (2026-09-07, see the head of this method), so the column no longer
            // hides at all and the replay has lost its trigger as well as its cure — the prune below
            // is kept because it is keyed on the only event that may ever drop a mark (the card
            // leaving the active pile) and is therefore correct with or without a gate. MEASURED,
            // ModBuild 462, both logs of the
            // 2026-09-06 session: the host's own board had exactly ONE active card all session
            // ('ACTIVE SET ... own board [151:ABILITY_CARD_TheMindsWeakness]', the only non-empty
            // own-board picture in the log), its 'Active cards: N shown' line toggled 1/0 nine
            // times as the gate opened and shut — and the co-player's log carries THREE
            // '[Net] Remote card FX [player 1]: Board -> Active playing' events for that one card.
            // Three flights, one activation. The new trigger cannot repeat: a card is in
            // _lastHalfCards only for the single rebuild that follows its leaving the dock.
            //
            // IT CANNOT COLLIDE WITH THE BURN, and the game guarantees that rather than this code.
            // MoveAbilityCard (CCharacterClass.cs:273) removes from the source list before adding to
            // the destination, so ActivatedCards / LostAbilityCards / PermanentlyLostAbilityCards /
            // DiscardedAbilityCards are mutually exclusive at every instant, and the Activated
            // branch WINS over the action's own pile while ActiveBonuses stand. A card that burns
            // only after its effects is therefore in ActivatedCards at end of turn — this flight —
            // and moves to LostAbilityCards later, when the bonus expires, which is a second,
            // strictly later transition out of the ACTIVE COLUMN and thus never in _lastHalfCards.
            // The burn flight is the burn path's (TryStartBurnFly / RemoteBurnFx) and stays there.
            // Both animations happen, in that order, and never both at once.
            //
            // THE ORIGIN ANCHOR IS THE 461 LADDER, term for term. The line here used to read
            // `SlotAnchor(_tray.SlotOf(card))` — the EXACT expression ModBuild 461 replaced in
            // TryStartFlyToPile because SlotOf answers off the _occupants bookkeeping the rebuild
            // has already evicted the card from, so it returns -1 by construction and every flight
            // went out as CardFxAnchor.Board. That is why all three of 462's mirrored active
            // flights read 'Board -> Active' on the co-player, and why two of the three drew a card
            // BACK ('CAUSE = NEVER LATCHED'): RemoteControlBoard.TryTakeDepartedFace gates its live
            // latch on `slot >= 0`, so a Board origin can never reach the face. Naming the recess
            // fixes the start point and the face together, with nothing to change on the receiver.
            bool leftARecess = _lastHalfCards.Contains(card);
            if (leftARecess && !_activeFlown.Contains(instanceId)
                && !card.IsHeld && !card.IsFlying && !card.IsVanishing
                && card.gameObject.activeInHierarchy)
            {
                int seat = _tray.RecessSeatOfCard(card);
                if (seat < 0)
                    seat = _tray.SlotOf(card);
                // The card has NOT moved yet — nothing below _active.SetCards has run, and the
                // eviction that empties _occupants is bookkeeping that reparents nothing — so the
                // live transform IS the recess seat the owner is looking at. That world pose is the
                // flight's start; the SEMANTIC anchor beside it is what peers resolve against their
                // copy of this board.
                int modelId;
                string modelName;
                try
                {
                    modelId = widget.AbilityCard.ID;
                    modelName = widget.AbilityCard.Name ?? "?";
                }
                catch { modelId = -1; modelName = "?"; }
                _activeFlightBuffer.Add(new ActiveFlight(card, modelId, modelName, instanceId,
                    seat, card.transform.position,
                    card.transform.lossyScale.x * CardsConfig.CardWidth.Value));
            }
        }

        // THE HOME CELL FIRST, THEN THE ARC INTO IT. VRCard.FlyFromPile flies to "the home pose the
        // layout already asserted", so SetCards (which relayouts with instant:false and therefore
        // calls SetHome on every column card) has to run before the launch below. This ordering is
        // the same one CardsDriver.4's pick-restart return uses.
        _active.SetCards(_activeBuffer);
        _active.SetVisible(_activeBuffer.Count > 0);

        LaunchActiveFlights();

        // ALREADY-FLOWN MARKS SURVIVE THE TURN GATE and are pruned to the cards that are STILL
        // active. A card that stays active for five rounds keeps its mark through every hide/show,
        // so it never re-flies; a card that leaves the active pile (its bonus expired and it went
        // to the lost pile) drops out, so a genuinely new activation of it would fly again.
        _activeFlown.IntersectWith(_activeIdBuffer);

        // The comparable half of the answer: what THIS seat believes is active for THIS character,
        // beside every other seat's belief about the same character. See ActiveCardSet.
        ActiveCardSet.Report(hand.PlayerActor, ActiveCardSet.Belief.OwnBoard, _activeModelBuffer);

        if (_loggedActiveCount != _activeBuffer.Count)
        {
            _loggedActiveCount = _activeBuffer.Count;
            VRLog.Info("Cards", $"Active cards: {_activeBuffer.Count} shown in the ACTIVE area " +
                                "(authoritative CCharacterClass.ActivatedCards, via ActiveCardSet; " +
                                "active halves highlighted).");
        }
    }

    /// <summary>
    /// The cards whose END-OF-TURN flight into the active column has already been played, keyed on
    /// <c>CAbilityCard.CardInstanceID</c> (the game's own per-instance identity, not the repeating
    /// card DATA id), so the flight runs exactly ONCE per activation.
    ///
    /// <para>IT REPLACES <c>_activeShown</c>, WHICH WAS CLEARED BY A TURN GATE AND THEREFORE
    /// REPLAYED. Its own doc said so in as many words — "Cleared whenever the column is hidden, so
    /// the flight replays when it comes back up" — and read as a description of intended behaviour
    /// rather than as the defect it was. That gate hid the column on every turn that was not the
    /// presented character's, so on a two-player scenario that was every other turn. This set is
    /// instead PRUNED to the ids still in the active list (see the IntersectWith at the end of
    /// <see cref="UpdateActive"/>): a mark is dropped only when the card actually leaves the active
    /// pile, which is the only event after which a new activation — and so a new flight — is
    /// correct.</para>
    ///
    /// <para>THE GATE ITSELF IS GONE (user item 4, 2026-09-07 — the column vanished when another
    /// player's turn began while the peer's mirror kept drawing it correctly). The prune stays: it
    /// was never a workaround for the gate, it is the right rule for the mark, and it is what keeps
    /// this set finite now that the only wholesale clear left is the no-hand teardown in
    /// <c>HideBoardContent</c>.</para>
    /// </summary>
    private readonly HashSet<int> _activeFlown = new(8);

    /// <summary>The current pass's active card INSTANCE ids, for the prune above. A field rather
    /// than a local so the per-rebuild walk allocates nothing.</summary>
    private readonly HashSet<int> _activeIdBuffer = new(8);

    /// <summary>
    /// One end-of-turn flight into the active column, captured while the card is still sitting in
    /// its round recess and launched after the column has asserted its home cell.
    /// </summary>
    private readonly struct ActiveFlight
    {
        public readonly VRCard Card;
        /// <summary>The game model's own card id and name, captured at the recess rather than read
        /// inside the log call: <c>ACTIVE SET</c> prints the same <c>id:name</c> pair on every
        /// client, so this line and a peer's board belief are comparable as literal strings.</summary>
        public readonly int ModelId;
        public readonly string Name;
        /// <summary><c>CAbilityCard.CardInstanceID</c> — the once-per-activation key.</summary>
        public readonly int CardId;
        /// <summary>The board recess the card left (0/1), or -1 when neither
        /// <c>RecessSeatOfCard</c> nor <c>SlotOf</c> could name one.</summary>
        public readonly int Seat;
        /// <summary>Where the card physically was when the model moved it — the flight's start.</summary>
        public readonly Vector3 FromWorld;
        /// <summary>…and how wide it was drawn there, so the arc's scale ramp starts at the size the
        /// owner was actually looking at rather than at a nominal.</summary>
        public readonly float FromWidth;

        public ActiveFlight(VRCard card, int modelId, string name, int cardId, int seat,
                            Vector3 fromWorld, float fromWidth)
        {
            Card = card;
            ModelId = modelId;
            Name = name;
            CardId = cardId;
            Seat = seat;
            FromWorld = fromWorld;
            FromWidth = fromWidth;
        }
    }

    /// <summary>The flights captured this pass (almost always 0 or 1; a turn-clear can activate
    /// both round cards).</summary>
    private readonly List<ActiveFlight> _activeFlightBuffer = new(2);

    /// <summary>
    /// Fly each newly-activated card from its round recess into the cell the active column just
    /// gave it, and tell peers to play the identical arc against their copy of this board.
    ///
    /// <para>THE OWNER NOW ARCS, WHICH IS WHAT "so wie die anderen Karten" ASKS FOR. Before this the
    /// owner's only motion was <c>ActivePileViewer.Relayout</c>'s <c>SetHome(instant: false)</c> —
    /// the ordinary straight-line home lerp — while every observer played
    /// <c>RemoteCardFx</c>'s smoothstep chord plus a sine bow along WORLD up. Same 0.4 s, different
    /// CURVE, so "remote und lokal" was false on the one term the user named. The comment that stood
    /// here asserted the opposite ("The owner already glides ... exactly as a fan does") and was a
    /// hypothesis: a fan glide is not a pile arc, and the pile arc is the shape the item compares
    /// itself to. <see cref="VRCard.FlyFromPile"/> is the existing helper for "arc from a world
    /// point into the home pose the layout already set" — the same one the pick-restart return and
    /// the short-rest offer use — so this is a second CALLER, not a second arc.</para>
    ///
    /// <para>THE TWO SIDES ARE THE SAME NUMBERS BY CONSTRUCTION: the duration is
    /// <see cref="FlyToPileSeconds"/> = 0.4 s and <c>NetProtocol.CardFxSeconds</c> = 0.4 s; the bow
    /// is <c>VRCard.FlyArcHeightFraction</c> = 0.55 and <c>RemoteCardFx.ArcFraction</c> = 0.55, both
    /// along world up; the floor is <see cref="BoardArcMin"/> and <c>RemoteCardFx</c>'s
    /// <c>CardHeight × MinArcCardHeights × boardScale</c>, which is the same expression against the
    /// owner's own synced card height.</para>
    ///
    /// <para>…AND SINCE 2026-09-07 THE SAME CURVE, WHICH THOSE THREE TERMS NEVER IMPLIED. The
    /// paragraph above lists duration, arc fraction and floor. All three agreed — and had agreed for
    /// eight hardware rounds — while the OWNER eased with SMOOTHERSTEP and bowed on that eased term
    /// and the RECEIVER eased with plain SMOOTHSTEP and bowed with sin(pi*t) on the RAW t. Same
    /// duration, same peak, 0.336 × arc apart at t = 0.25; at t = 0.10 the owner is at 3.4 % of peak
    /// lift and the mirror was at 30.9 %, and the owner's launch vertical velocity is ZERO by
    /// construction against the mirror's ~3.5 m/s. The card LEAPT off the recess on a peer's board
    /// and EASED off it on its owner's. Three matching numbers are not a matching curve, and the
    /// sentence in the log line below used to say they were.</para>
    ///
    /// <para>The mirrors now CALL <c>VRCard.SmootherStep</c> and <c>VRCard.FlyArcOffset</c> through
    /// <c>Net.RemoteFlightCurve</c>, and <c>scripts/check-mirrors.sh</c>'s "mirrored flight ease"
    /// and "mirrored flight bow" groups fail the build on a further copy.</para>
    /// </summary>
    private void LaunchActiveFlights()
    {
        if (_activeFlightBuffer.Count == 0)
            return;
        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        for (int i = 0; i < _activeFlightBuffer.Count; i++)
        {
            ActiveFlight f = _activeFlightBuffer[i];
            VRCard card = f.Card;
            if (card == null || card.IsHeld || card.IsFlying || card.IsVanishing)
                continue;
            // Marked BEFORE the launch, so a throw below can never leave the card eligible to fly
            // again on the very next rebuild — a repeated flight is the defect this replaces.
            _activeFlown.Add(f.CardId);
            Net.CardFxAnchor origin = SlotAnchor(f.Seat);
            card.FlyFromPile(f.FromWorld, f.FromWidth, FlyToPileSeconds, arcUp, minArc);
            // The SECOND CALLER of the shipped mirrored-flight machine (Net.NetCardFx ->
            // RemoteCardFx), never a second implementation: the same outbox, the same 2-byte
            // endpoint pair, the same receiver. CardFxAnchor.Active (ModBuild 462) resolves on the
            // receiver to RemoteControlBoard.AnchorLocal(...) => layout.ActiveMount — the sender's
            // own active-matrix mount — and an older peer's NetCardFx.Clamp degrades it to the board
            // centre rather than dropping the packet. ReportCardFx suppresses the whole thing under
            // a read-only character focus, which is right: that flight belongs to the watched
            // character's board, not to ours.
            ReportCardFx(origin, Net.CardFxAnchor.Active);
            // HW-VERIFY: user item 7 (2026-09-06 late), the OWNER's half. Grep token: ACTIVE FLIGHT.
            // Its other half prints on every OTHER machine as '[Net] FLIGHT FACE ... -> Active'.
            // Read the two side by side — that comparison is the only way "remote und lokal gleich"
            // is decidable without a video, so the five fields below are the five that line names
            // too: the CARD, the EVENT that triggered the flight, the ORIGIN anchor, the DESTINATION
            // anchor, and the FRAME it started.
            //
            //  WORKING = "recess 1"/"recess 2" here and "their Slot0 -> Active"/"Slot1 -> Active"
            //            there, ONE line per card per activation, and that peer's FLIGHT FACE
            //            reading FRONT. In ModBuild 462 the same session produced 3 wire events for
            //            1 activation, all "Board -> Active", 2 of 3 drawn as a BACK.
            //  INERT   = this line absent while '[Cards] Active cards: N shown' rises from 0 — the
            //            card reached the column without the end-of-turn transition being seen, so
            //            _lastHalfCards did not hold it (it was not lying in a recess: activated
            //            from the hand fan, from a pick field, or the column re-populated on a focus
            //            switch rather than on a turn ending). No flight is correct for those, and
            //            AS OF ModBuild 473 none is drawn: ActivePileViewer.Relayout SEATS an
            //            arriving card instantly (grep token ACTIVE SEAT) and glides only cards
            //            already standing in the column. Until that build the sentence here read
            //            "No flight is CORRECT for those" and stopped — an assertion about what
            //            OUGHT to happen, read for two builds as a statement of what did. A glide
            //            always happened: the session behind report item 1 filled the column six
            //            times against one legitimate end-of-turn arc, so five arrivals flew
            //            unlogged and this comment covered them. The claim is now true on purpose
            //            rather than by accident, and ACTIVE SEAT is what makes it falsifiable.
            //  BEYOND  = this line present with NO FLIGHT FACE line on any peer: the event was lost
            //            on the unreliable extras channel (read '[Net] CARD FX OUTBOX' here against
            //            that peer's '[Net] CARD FX LOST', which stood at 1 of 7 this session). A
            //            lost event is a MISSING animation only — the card is already seated in
            //            every peer's active matrix from the host-replicated ActivatedCards, so
            //            nothing is stranded mid-flight and nothing is drawn wrongly.
            VRLog.Note("Cards", $"ACTIVE FLIGHT: card {f.ModelId}:{f.Name} (widget {f.CardId}) "
                + "went ACTIVE at the END OF THE TURN — the "
                + "game's own CCharacterClass.DiscardRoundAbilityCards drain moved it out of "
                + "RoundAbilityCards into ActivatedCards (MoveAbilityCardToPile takes the Activated "
                + "branch while ActiveBonuses stand), and this client saw that as the card leaving "
                + "its round recess. ORIGIN "
                + (f.Seat >= 0
                    ? $"recess {f.Seat + 1} -> wire anchor {origin}, so every peer's mirrored flight "
                      + "starts at their copy of that recess — the point this player is watching the "
                      + "card leave"
                    : $"the board CENTRE (no recess: RecessSeatOfCard and SlotOf both answered -1) "
                      + $"-> wire anchor {origin}, which is the honest last resort and not the "
                      + "pre-461 bookkeeping defect")
                + $". DESTINATION wire anchor {Net.CardFxAnchor.Active} = this board's active-card "
                + $"matrix. FRAME {Time.frameCount}, {FlyToPileSeconds:F2}s, arc floor "
                + $"{minArc:F3} m over the board — the SAME duration (NetProtocol.CardFxSeconds), "
                + "the SAME 0.55 arc fraction and the SAME world-up bow the receiver replays, and "
                + "since 2026-09-07 the SAME EASE: this flight runs VRCard.SmootherStep with "
                + "VRCard.FlyArcOffset on that eased term, and the mirror CALLS both through "
                + "Net.RemoteFlightCurve. THIS LINE USED TO END 'so the two machines draw one "
                + "curve' AFTER THOSE THREE TERMS, and it was false for eight rounds: three "
                + "mirrors eased with plain smoothstep over a raw-t sine bow, 0.336 x arc away "
                + "from this one at t=0.25. Compare the receiver's 'FLIGHT CURVE' line, which "
                + "prints the chord fraction and the lift at t=0.25 — NEVER compare the arc PEAK, "
                + "which is identical under every symmetric ease and is exactly why eight rounds "
                + "of matching arc readings never contradicted the divergence. "
                + "ONE LINE PER ACTIVATION: the mark is kept "
                + "for the whole activation and dropped only when the card leaves the active "
                + "pile, which is the replay ModBuild 462 shipped (3 events for 1 activation). The "
                + "column that used to hide between turns no longer does (user item 4), so this "
                + "line should now appear once per activation and never again for that card.");
        }
        _activeFlightBuffer.Clear();
    }

    /// <summary>Model-side twin of <c>_activeBuffer</c> — the same cards as <c>CAbilityCard</c>, for
    /// the census (which compares game-card identity across clients, never VR widgets).</summary>
    private readonly List<CAbilityCard> _activeModelBuffer = new(8);

    /// <summary>
    /// Drive the NATIVE game action-region highlight on a card's active half/halves
    /// (feature 6) — the exact mouse-over visual. The mod re-parents the live
    /// <see cref="FullAbilityCard"/> rect onto the VR card's world canvas, so
    /// <c>FullAbilityCard.ToggleHighlightHover</c> (the animated <c>CardActionHighlight</c>
    /// pulse) / <c>UntoggleHighlightHover</c> render on the VR card automatically. A side
    /// resolved active shows its region; an inactive side is explicitly untoggled so a
    /// reused card carries no stale highlight.
    ///
    /// <para><b>ASSERTED THROUGH <c>Cards.Art.ActionHighlightDriver</c>, WHICH NEVER RESTARTS A
    /// PULSE THAT IS ALREADY RUNNING (user item 7, 2026-09-07).</b> These two methods used to call
    /// <c>ToggleHighlightHover</c>/<c>UntoggleHighlightHover</c> unconditionally on every
    /// <c>UpdateActive</c>, i.e. on every <c>Rebuild</c>, and <c>CardActionHighlight.ShowHover</c>
    /// is NOT idempotent: it cancels the running LeanTween chain and re-seeds the alpha to
    /// <c>fromAlfa</c>. The authored cycle is 1.0 s (<c>fromAlfa 1 / toAlfa 0.3 /
    /// hoverDuration 0.5</c>) — his "ruhige Frequenz" — but the DELIVERED period was the rebuild
    /// interval whenever that fell under a 0.5 s leg, so the blink ran fast exactly while the board
    /// was busy. That is a term which varies DURING a session, which is what "ploetzlich viel
    /// hoeher" requires and a wrong constant cannot supply. The mirrored twin
    /// (<c>RemoteBoardCard.ApplyHalf</c>) has carried this gate since it was written and its own
    /// comment names the symptom verbatim; the local path never had it.</para>
    /// </summary>
    private static void SetActiveHighlight(VRCard card, bool top, bool bottom)
        => card.SetActionHighlight(top, bottom);

    /// <summary>Clear the native action-region highlight on both halves (feature 6).</summary>
    private static void ClearActiveHighlight(VRCard card)
        => card.SetActionHighlight(top: false, bottom: false);

    /// <summary>
    /// Active-set watchdog (feature 6): active cards/halves change during a turn (a bonus
    /// starts or expires) without any of the mod's rebuild events. Poll a cheap signature
    /// of the active pile + round and flip dirty on any edge; <see cref="UpdateActive"/> is
    /// the sole executor. Allocation-free, no-op when steady.
    ///
    /// <para>FED THE PRESENTED HAND, because that is the hand its executor renders: Rebuild calls
    /// <see cref="UpdateActive"/> with <c>CharacterFocus.ResolveHand(CurrentHand())</c>. Found while
    /// fixing the item pile's twin of this (user report 2026-08-09, see
    /// <c>PileViewer.TickStatus</c>) — a DISPLAY surface whose change-gate watched the GAME's hand
    /// while its build read the FOCUSED one is the same defect shape, and a watchdog that watches
    /// the wrong character simply stops firing: the focused character's active set could change (a
    /// bonus expiring at a round boundary during somebody else's turn) with nothing marking the
    /// board dirty. Low blast radius — <see cref="UpdateActive"/> shows nothing at all unless it is
    /// that character's own action turn or the shared selection phase — but the two halves must read
    /// the same hand or the signature compares two different characters' piles across a focus
    /// switch, which is a spurious dirty in the other direction.</para>
    /// </summary>
    private void PollActive(CardsHandUI? hand)
    {
        int sig = ActiveSignature(hand);
        if (sig != _activeSignature)
        {
            _activeSignature = sig;
            _dirty = true;
        }
    }

    /// <summary>
    /// Cheap change-gate hash of the active-card set (ids) + round. 0 = none.
    ///
    /// <para>IT HASHES THE MODEL NOW, NOT THE WIDGETS — and that is the other half of the
    /// "unmittelbar" defect (user item 8b). It used to call <c>GetActivePileWidgets</c> and hash
    /// <c>AbilityCardUI.CardID</c>, which back then meant hashing the widgets whose
    /// <c>CardType</c> cache the game had already re-stamped. A watchdog whose signature is
    /// downstream of the very refresh it is supposed to trigger cannot fire until that refresh has
    /// happened: on the owner's own board it fired LATE, and on a focused peer's hand — whose 2D
    /// view no client but the owner ever refreshes — it never fired at all. <c>ActiveCardSet</c>
    /// reads <c>CCharacterClass.ActivatedCards</c> instead, so the dirty edge lands on the frame
    /// THE RULES moved the card rather than on the frame a 2D view happened to refresh.</para>
    ///
    /// <para>WHICH FRAME THAT IS, CORRECTED — the sentence that used to end the paragraph above was
    /// wrong, and it is the twin of the one <c>ActiveCardSet.IsActive</c> now carries the
    /// correction for. It said the rules append to that list "at the instant of activation", naming
    /// <c>CCharacterClass.ActivateCard</c>. <c>ActivateCard</c> has exactly two call sites in the
    /// whole game (<c>CActiveBonus.cs:401</c> and <c>:406</c>) and both sit inside a branch guarded
    /// by <c>baseCard.CardType</c> being an ITEM, an ATTACK MODIFIER or an enemy AURA — an ability
    /// card can never reach it. An ability card enters <c>m_ActivatedCards</c> at
    /// <c>CCharacterClass.cs:467</c>, the <c>ECardPile.Activated</c> branch of
    /// <c>MoveAbilityCardToPile</c>, reached from <c>DiscardRoundAbilityCard</c> — the END-OF-TURN
    /// drain. The watchdog is unaffected (it hashes the list either way); what was false was the
    /// claim about WHEN the edge arrives, and this file is one of the places a reader would have
    /// gone looking for it.</para>
    /// </summary>
    private int ActiveSignature(CardsHandUI? hand)
    {
        if (hand == null)
            return 0;
        int sig = ActiveCardSet.Signature(hand.PlayerActor);
        // Bonus half-activity can shift at a round boundary without the card set changing.
        sig = sig * 31 + CardsGameApi.RoundNumber();
        return sig;
    }

    // -------------------------------------------------------- hand-fan membership watchdog --
    //
    // ITEM 10 (user 2026-09-02, verbatim): "Die gerade verbrannte Karte ist beim Test auf dem
    // Handfächer zu sehen direkt nach dem der Mitspieler sie verbrannt hat. Das darf unter keinen
    // Umständen der Fall sein!"
    //
    // ROOT CAUSE, AND IT IS A MISSING WATCHDOG, NOT A WRONG FILTER. FillHandFan already drops a
    // burned card — its `widget.CardType != CardPileType.Hand` test is correct the moment the game
    // has moved the widget. What was missing is anything that makes the mod LOOK. Rebuild runs on
    // `_dirty` alone, and before this poll NOT ONE of the ~12 things that raise `_dirty` watched the
    // hand-pile card SET:
    //   * HandShownEvent            — CardsHandManager.Show; a burn does not show a hand;
    //   * CardSelectionEvent        — select/deselect, not a pile move;
    //   * PollModeChange            — (mode, selecting, actor, actionSig): no card-set term;
    //   * PollActive                — hashes the ACTIVE pile only;
    //   * everything else           — input, focus, mode, session, teardown.
    // So a burn left the card on the fan until some UNRELATED event happened to dirty the driver —
    // a hover, a grab, a mode change, a focus switch. "Directly after the co-player burned it" is
    // exactly that window, and it has no upper bound.
    //
    // WHY THE PRESENTED HAND, NOT THE GAME'S: this is the watchdog for a DISPLAY surface, and its
    // executor (FillHandFan, from Rebuild) builds from CharacterFocus.ResolveHand(CurrentHand()).
    // PollActive's doc states the general rule and the defect shape it was written for — "a watchdog
    // that watches the wrong character simply stops firing". A teammate's hand changing while we are
    // FOCUSED on them is precisely the multiplayer case item 10 was reported from.
    //
    // WHY A SET SIGNATURE AND NOT A COUNT: a burn that lands in the same frame as a draw leaves the
    // count unchanged.
    //
    // WHY THE SIGNATURE IS ORDER-INDEPENDENT (sum + xor + count, not a positional 31-fold like
    // ActiveSignature's): the game re-sorts cardsUI on its own (CardsHandUI.SortCards), and the fan
    // deliberately does NOT follow that order — CardsDriver.4.Rebuild's stage-A reorder re-applies
    // the player's persisted _fanOrder over the game's, precisely so a game sort cannot shuffle the
    // fan under his hand. A positional hash would therefore fire a full Rebuild on an event the fan
    // is designed to ignore, once per sort, for no visible change. Membership is the question this
    // watchdog asks, so membership is what it hashes.
    //
    // COST: one O(cardsUI) walk per frame over a list of ~10-30, no allocation, no game state
    // touched. It is the same shape and the same price as PollActive, which has run per frame since
    // feature 6.

    /// <summary>
    /// Hand-fan membership watchdog (item 10): flip <c>_dirty</c> the moment the presented
    /// character's HAND-pile card set changes — a burn, a loss, a discard, a draw, a card given
    /// away — none of which raises any of the mod's rebuild events. <see cref="FillHandFan"/> is
    /// the sole executor, unchanged. Allocation-free, no-op when steady.
    /// </summary>
    private void PollHandCards(CardsHandUI? hand)
    {
        int sig = HandCardSignature(hand);
        if (sig != _handSignature)
        {
            _handSignature = sig;
            _dirty = true;
        }
    }

    /// <summary>Cheap ORDER-INDEPENDENT change-gate hash of the HAND-pile widget SET (see the region
    /// header for why order must not enter it). 0 = none. Never throws: a half-torn hand answers 0,
    /// which re-fires once when it comes back — the safe direction, because a missed edge is the
    /// defect this exists for.</summary>
    private int HandCardSignature(CardsHandUI? hand)
    {
        if (hand == null)
            return 0;
        try
        {
            CardsGameApi.GetCards(hand, _handSigBuffer);
            int sum = 0;
            int xor = 0;
            int count = 0;
            for (int i = 0; i < _handSigBuffer.Count; i++)
            {
                AbilityCardUI w = _handSigBuffer[i];
                if (w == null || w.AbilityCard == null || w.IsLongRest)
                    continue;
                if (w.CardType != CardPileType.Hand)
                    continue;
                // THE WATCHDOG MUST ASK THE EXECUTOR'S QUESTION, or it stops firing exactly when it
                // matters (user 2026-09-04, the burned card that came back). `CardType` is a UI
                // latch the game writes when it re-runs CardsHandUI.UpdateCards; the authoritative
                // move (CCharacterClass.MoveAbilityCardToPile) happens FIRST. Hashing the latch
                // alone leaves the signature unchanged for the whole widget-lags-model window — so
                // nothing is marked dirty, no rebuild runs, and the belt that would have dropped the
                // card never gets to run at all. Asking the model here means the edge is the MOVE,
                // not the game's later bookkeeping.
                //
                // INTEGRATOR NOTE (2026-09-05): this used to call FillHandFan's private
                // CardLeftTheHand. That belt was folded into the ONE membership expression both
                // arcs now share (CardsGameApi.HandFanMember, report item 5b) in the same build, so
                // the watchdog asks that expression directly. The point of the change was that the
                // executor and the watchdog must not be able to hold two opinions; calling the
                // shared expression is that point taken one step further, not a substitution.
                if (!CardsGameApi.HandFanMember(w, w.PlayerActor ?? hand.PlayerActor))
                    continue;
                int id = w.CardID;
                unchecked { sum += id; }
                xor ^= id;
                count++;
            }
            // 0 is reserved for "no hand" — an empty hand must not collide with it, or a character
            // whose hand really is empty would re-fire a rebuild on every hand teardown/return.
            unchecked { return (((sum * 31) ^ xor) * 31) + count + 1; }
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    // ------------------------------------------------------------------ dev fake hand --

    private void RebuildFakeOrClear(Transform anchor)
    {
        // No active local hand: no piles to show or browse, no pick field (test #21
        // C — the stacks hide, an open browse closes, field occupants clear; all
        // return with the next active hand).
        _piles.SetVisible(false);
        _active.SetVisible(false); // feature 6: no active hand → no active-cards area
        // THE ONE PLACE THE FLOWN MARKS ARE DROPPED WHOLESALE, and since user item 4 (2026-09-07)
        // the ONLY place the active column is emptied by anything other than an empty active list.
        // UpdateActive used to open with a turn gate that hid the column every turn that was not
        // this character's, and that gate's clear WAS the replay defect. This path is different in
        // kind: it means there is no hand and no character at all (scenario teardown, or the board
        // with nobody assigned), so no mark can still be describing a card that is on a board.
        // Keeping them here is the stale-mark risk the CardInstanceID key already narrows; clearing
        // them here removes it outright.
        _activeFlown.Clear();
        _activeBuffer.Clear();
        _active.SetCards(_activeBuffer);
        _loggedActiveCount = int.MinValue;
        CloseBrowser(CardsGameApi.InScenario ? "no active hand" : "scenario ended");
        _tray.SetPickActive(false);
        _tray.SetWantedSlots(0);
        _tray.SetPickStatus(null, null, null); // event-discard banner/keycap overrides never outlive the hand
        _pickStatusKey = null;
        _fieldCards.Clear();
        _pickLockedCount = 0;
        _pickExitFlown.Clear();
        RemoveShortRestCard(); // sacrifice display never survives losing the active hand (item 1d)

        // OFF-SCENARIO HAND SOURCE (the 3D map room's loadout fan). THE ONE SEAM that lets a
        // non-scenario surface drive THIS fan instead of building a second one — see the region at
        // the end of this file for the whole contract and why it lives here. Placed after the
        // housekeeping above (piles/active/field/short-rest all cleared, which is exactly what the
        // map phase wants) and before the dev fake hand, which it is mutually exclusive with.
        if (TryRebuildOffScenarioFan())
            return;

        bool wantFake = Plugin.DevMode.Value && CardsConfig.DevFakeHand.Value > 0 && !CardsGameApi.InScenario;
        if (!wantFake)
        {
            if (_fakeActive)
                ClearFakeCards();

            _fanBuffer.Clear();
            _fan.SetCards(_fanBuffer);
            _half.SetVisible(false);
            if (CardsGameApi.InScenario)
            {
                // Dashboard (test #15): a scenario without an ACTIVE local hand
                // (other players' turns, in-between phases) keeps the tray up —
                // initiative track/objectives/status stay readable; slots empty.
                _tray.EnsureBuilt(_factory, anchor);
                _rest.EnsureBuilt(_tray);
                _tray.ClearSlots();
                _tray.SetVisible(true);
            }
            else
            {
                _tray.SetVisible(false);
                if (_boundHand != null)
                {
                    _factory.Clear(); // scenario/hand gone: restore faces, drop cards
                    _boundHand = null;
                }
            }
            return;
        }

        if (!_fakeActive)
        {
            _fakeActive = true;
            int n = Mathf.Clamp(CardsConfig.DevFakeHand.Value, 1, 12);
            for (int i = 0; i < n; i++)
            {
                VRCard card = _factory.CreateBlank();
                card.BuildPlaceholderFace(i);
                HookCard(card);
                _fakeCards.Add(card);
            }
            VRLog.Info("Cards", $"Dev fake hand: {n} placeholder cards spawned.");
        }

        _tray.EnsureBuilt(_factory, anchor);
        _rest.EnsureBuilt(_tray);
        _tray.SetVisible(true);
        _half.SetVisible(false);

        _fanBuffer.Clear();
        for (int i = 0; i < _fakeCards.Count; i++)
        {
            VRCard card = _fakeCards[i];
            if (card == null || card.IsHeld || _tray.SlotOf(card) >= 0)
                continue;
            card.Grabbable = true;
            _fanBuffer.Add(card);
        }
        _fan.SetCards(_fanBuffer);
    }

    private void RouteFakeRelease(VRCard card, VRHand hand)
    {
        int highlightSlot = ReferenceEquals(_snapHighlightCard, card) ? _snapHighlightSlot : -1;
        int slot = _tray.SlotNear(card.transform.position, hand.Rig.PalmCenter.position,
            out PlayTray.SlotProbe probe);
        if (slot >= 0 && _tray.Occupant(slot) != null && _tray.Occupant(slot) != card)
        {
            int other = 1 - slot;
            slot = _tray.Occupant(other) == null ? other : -1;
        }
        string rule = highlightSlot >= 0 ? "highlight" : slot >= 0 ? "radius" : "none";
        if (highlightSlot >= 0)
            slot = highlightSlot; // test #15: what glows is what drops (see OnCardReleased)
        VRLog.Info("Cards", $"Drop ({hand.Side}, fake): {probe.Describe()}, " +
                            $"rule={rule} → " + (slot >= 0 ? $"slot {slot + 1}." : "fan."));
        if (slot >= 0 || _tray.ContainsCard(card))
            _tray.NoteSlotActivity(); // accident window (test #19), fake-mode parity
        if (slot >= 0)
        {
            hand.SendHaptic(HapticPreset.ClickPulse); // snap feedback (test #13)
            _tray.PlaceCard(card, slot); // the Drop line above is the announcement
        }
        else
        {
            _tray.RemoveCard(card);
            _fan.Add(card);
        }
        _dirty = true;
    }

    private void ClearFakeCards()
    {
        _fakeActive = false;
        for (int i = 0; i < _fakeCards.Count; i++)
        {
            if (_fakeCards[i] != null)
                Destroy(_fakeCards[i].gameObject);
        }
        _fakeCards.Clear();
        _tray.ClearSlots();
    }

    // ------------------------------------------------- off-scenario hand source (map room) --
    //
    // WHAT THIS IS, AND WHY IT IS HERE RATHER THAN IN A SECOND FAN CLASS.
    //
    // USER RULING (2026-08-21, on the map room's card hand): "Ich will das es sich hier 1:1
    // genauso verhält wie im Szenario selber. Am Besten nutzt du auch die selben Code Segmente.
    // Es soll sich nicht vom Szenario unterscheiden wie sich die Karten verhalten!"
    //
    // WHAT ACTUALLY KEPT THIS MACHINERY OUT OF THE MAP PHASE — it was never the palm gate.
    // The campaign map resolves to VRMode.TableIdle (VRModeStateMachine.Recompute, the
    // `!_inScenario ? VRMode.TableIdle` arm, reachable because MapRoomDriver pushes SetModRoom),
    // and TableIdle's interactor row is Poke|Grab|PalmGate — so PalmGate.Enabled is TRUE on the
    // map today and the wrist roll is ALREADY being measured. What is missing is CARDS:
    //   * CurrentHand() returns null off-scenario (CardsDriver.2.Update.cs, `if
    //     (!CardsGameApi.InScenario) return null;` — InScenario is Choreographer.s_Choreographer,
    //     and the campaign map has none by construction);
    //   * so Rebuild takes its `hand == null` arm and lands in RebuildFakeOrClear, which clears
    //     _fanBuffer and hands the fan an EMPTY list;
    //   * so UpdatePalmGate's `allowFan` (_fanBuffer.Count > 0 || _fan.Cards.Count > 0 ||
    //     _fan.HasLeavingCards) is false and `shouldOpen` can never become true.
    // Give the fan CARDS off-scenario and every one of the reveal, roll, animation, hover-split,
    // laser, fingertip-pop, exchange and audio paths runs unchanged, because they are literally
    // the same lines. That is the whole of this seam.
    //
    // THE CONTRACT, deliberately as small as it can be:
    //   * the SOURCE owns the VRCards' lifetime (creation, faces, destruction). It publishes a
    //     list; the driver copies it into _fanBuffer, hooks the cards and hands them to _fan.
    //   * the driver stamps NOTHING per card. CardFan.SetMode(FanMode.Inspect) + the fan's own
    //     StampMembership already write Grabbable=true, InspectOnly=true, AllowsGateHand=false and
    //     PokeSelectEnabled=false — the exact verdicts a scenario read-only hand gets.
    //   * the source must call <see cref="DropOffScenarioFan"/> BEFORE it destroys any card, so
    //     the fan is never left holding a destroyed object for a frame.
    //
    // INSPECTION-ONLY IS STRUCTURAL AND UNCHANGED IN STRENGTH. The cards carry no AbilityCardUI
    // (GameCard is null — nothing ever calls AttachGameCard on them), so:
    //   * OnCardPoked returns on its first line (`card.GameCard == null`);
    //   * MaybeReopenPickSelection returns on `CurrentHand() == null` AND on `GameCard == null`;
    //   * OnCardReleased hits `if (card.InspectOnly)` — which sits BEFORE CurrentHand() — and
    //     returns the card home with no game call; and even if that flag were cleared, the very
    //     next branch is `gameHand == null || card.GameCard == null → _fan.Add(card); return;`.
    //   * every remaining commit seam (SelectCard/UnselectCard, tray slots, pick field, initiative
    //     reconcile) is reached only through a resolved CardsHandUI, which does not exist here.
    // Two independent belts, both structural.

    /// <summary>
    /// The cards an off-scenario surface wants THIS fan to show (null / empty = none). Set and
    /// cleared by the owning surface (today <c>WorldUI.MapRoom.MapRoomHand</c>); read once per
    /// rebuild. The list and the objects in it belong to the SOURCE — the driver never destroys
    /// them.
    /// </summary>
    internal static IReadOnlyList<VRCard>? OffScenarioFanCards;

    /// <summary>
    /// One-shot: the next set replaces ANOTHER character's hand, so the rebuild plays the
    /// character-swap exchange (<c>CardFan.BeginSwapOut</c> + <c>SetCards(swap: true)</c>) rather
    /// than a plain re-layout. Consumed by the rebuild that acts on it — the same edge
    /// <see cref="Rebuild"/> derives from <c>CharacterFocus.PresentedActorId</c> in a scenario,
    /// which the map phase has no equivalent of.
    /// </summary>
    internal static bool OffScenarioFanSwap;

    /// <summary>
    /// The INITIATIVE of each card in <see cref="OffScenarioFanCards"/>, index-aligned with it
    /// (null = the source published none). The one thing an off-scenario card cannot be asked for
    /// directly: it carries no <c>AbilityCardUI</c> by construction (see the inspection-only
    /// guarantee above), so <c>VRCard.GameCard</c> — where a scenario card's initiative is read
    /// from — is null for its whole life.
    ///
    /// <para>Consumed ONLY by <see cref="FanInitiative"/>, i.e. only by the order diagnostic. The
    /// driver does NOT re-sort with it: the publishing source owns the fan's order (the map room
    /// sorts its loadout in <c>MapRoomHand.ResolveLoadout</c>, the scenario inherits the game's own
    /// <c>cardsUI.Sort()</c>), and a second sorting authority here could only ever drift from the
    /// first. This is the instrument, not the mechanism.</para>
    ///
    /// <para>MULTIPLAYER: local only. An initiative is a number about a card the local player is
    /// looking at; nothing here is sent, and card identity never goes on the wire.</para>
    /// </summary>
    internal static IReadOnlyList<int>? OffScenarioFanInitiatives;

    /// <summary>Sentinel for "this card's initiative could not be resolved" — never a real
    /// initiative. Published by a source that lost a card's model, rendered as <c>?</c> and
    /// EXCLUDED from the sortedness verdict by <see cref="LogFanOrder"/>.</summary>
    internal const int NoInitiative = int.MinValue;

    /// <summary>
    /// The bundled <c>CardBacking</c> prefab the factory builds every scenario card on, or null
    /// (procedural fallback). Exposed so an off-scenario source's cards are built from the SAME
    /// asset — a map-room card whose back differs from a scenario card's is exactly the "it does
    /// not behave like the scenario" the seam exists to remove. Null before the driver exists.
    /// </summary>
    internal static GameObject? CardBackingPrefab =>
        Instance != null ? Instance._factory.GetBackingPrefab() : null;

    /// <summary>
    /// Hand the fan back, SYNCHRONOUSLY. The source calls this before destroying its cards (and on
    /// stand-down), so no frame can observe the fan holding a destroyed object and no stale
    /// subscription can outlive the objects it points at. Idempotent; a no-op with no driver.
    /// </summary>
    internal static void DropOffScenarioFan(string reason)
    {
        OffScenarioFanCards = null;
        OffScenarioFanInitiatives = null;
        OffScenarioFanSwap = false;
        Instance?.ReleaseOffScenarioFan(reason);
    }

    /// <summary>True while the fan is showing an off-scenario source's cards — the state line's
    /// proof that the map room really is driving THIS fan and not a copy of it.</summary>
    internal static bool OffScenarioFanActive { get; private set; }

    /// <summary>
    /// Fill the fan from <see cref="OffScenarioFanCards"/>. Returns false when there is no source
    /// (then <see cref="RebuildFakeOrClear"/> continues exactly as it did before this seam
    /// existed). Only ever runs off-scenario: in a scenario the game's own hand wins, always.
    /// </summary>
    private bool TryRebuildOffScenarioFan()
    {
        IReadOnlyList<VRCard>? source = OffScenarioFanCards;
        if (CardsGameApi.InScenario || source == null || source.Count == 0)
        {
            if (OffScenarioFanActive)
                ReleaseOffScenarioFan("the off-scenario source went away");
            return false;
        }

        // A scenario we just left still owns adopted faces — hand them back before anything else,
        // exactly as the plain !wantFake branch below does.
        if (_boundHand != null)
        {
            _factory.Clear();
            _boundHand = null;
        }
        _tray.SetVisible(false);
        _half.SetVisible(false);
        if (_fakeActive)
            ClearFakeCards();

        bool swap = OffScenarioFanSwap;
        OffScenarioFanSwap = false;
        // Same three conditions the scenario swap edge carries (Rebuild's `handSwap`): the fan must
        // be OPEN — a hand nobody is holding up has nothing to exchange — and there must be cards
        // on one side or the other to move.
        if (swap && _fan.IsOpen && (_fan.Count > 0 || source.Count > 0))
            _fan.BeginSwapOut();
        else
            swap = false;

        _fanBuffer.Clear();
        for (int i = 0; i < source.Count; i++)
        {
            VRCard card = source[i];
            if (card == null)
                continue;
            HookCard(card);
            _fanBuffer.Add(card);
        }

        // INSPECT, not Interactive: grab it, carry it, hand it over, read it — and the release
        // returns it home without touching a game seam. Set BEFORE SetCards so the first frame is
        // already correct (the fan's own contract, see CardFan.SetMode).
        _fan.SetMode(CardFan.FanMode.Inspect);
        _fan.SetCards(_fanBuffer, swap);
        // SINGLE-CARD ARRIVAL. Runs AFTER SetCards for the same reason the scenario's own dock
        // APPEAR does (Rebuild, "Issue 2 APPEAR", CardsDriver.4.Rebuild.cs:853-864): PlayAppear
        // snaps the card to its HOME, so the layout must already have asserted one.
        MaterializeNewOffScenarioCards(swap);
        // ORDER PROOF for the map-room hand — the fan the 2026-08-22 report is about. The order is
        // the SOURCE's (MapRoomHand.ResolveLoadout sorts the loadout by initiative before it builds
        // or diffs anything), so a NOT SORTED verdict here names a source that stopped sorting, not
        // a layout that stopped obeying.
        LogFanOrder("map-room hand", swap
            ? "a character exchange on the loadout fan"
            : "a loadout publish (grep 'MAP-ROOM HAND DIFF' for which card joined or left)");
        _offScenarioLast.Clear();
        _offScenarioLast.AddRange(_fanBuffer);
        _placementRefusal = "the off-scenario (map-room) fan is inspection-only by construction — "
                            + "its cards hold no game widget, so no commit seam exists for them";
        OffScenarioFanActive = true;
        return true;
    }

    // ---------------------------------------------------- single-card join / leave (ModBuild 193) --
    //
    // USER ASK (2026-08-21, item 8): "Wenn man eine Karte ändert während man seinen Kartenfächer in
    // der Hand betrachtet soll die Karte per Animation auftauchen oder verschwinden damit der Fächer
    // immer aktuell ist."
    //
    // The character-swap seam above answers "the WHOLE hand was exchanged". Ticking ONE card on or
    // off in UIPartyCharacterAbilityCardsDisplay is not that, and playing the exchange wipe for it
    // would move eleven cards to report one — the same "two unrelated animations glued together"
    // failure the exchange region's own header argues against. So the source (WorldUI.MapRoom.
    // MapRoomHand) DIFFS its loadout and asks for exactly two things, and BOTH are the scenario's
    // own vocabulary rather than a new one:
    //
    //   JOIN  → <c>VRCard.PlayAppear</c>, the "emerge from dust" materialize a scenario plays for a
    //           docked action card / a slot occupant that appears for the newly active character
    //           (CardsDriver.4.Rebuild.cs:860 and :880). Same method, same 0.28 s, same dust.
    //           The card's PLACE comes from CardFan.SetCards → Relayout(instant: false), which is
    //           also what re-lays the survivors out around it — they glide, they do not jump.
    //   LEAVE → <c>CardFan.Remove</c> (the fan's own single-card exit: "remaining cards close the
    //           gap", CardFan.cs:460) + <c>VRCard.Vanish</c>, the "crumble to dust" a scenario plays
    //           for a card that leaves every zone with NO pile to fly to (CardsDriver.4.Rebuild.cs:794).
    //           A pile flight is deliberately NOT reused: the map phase has no discard or burnt pile
    //           to fly to, and inventing a destination would be inventing an animation.
    //
    // WHAT IS NOT TOUCHED, because the fan is OPEN IN HIS HAND while this runs: the palm gate is
    // never poked (allowFan reads _fanBuffer/_fan.Cards, both of which stay non-empty across a
    // one-card diff, so no open/close edge is generated); a HELD card is never removed here (the
    // source defers it, and the belt below refuses one anyway); and the hover election needs no
    // help — UpdateFanHoverSplit re-resolves its index from _fan.Cards every frame and
    // CardFan.Remove re-stamps the fingertip scan itself.

    /// <summary>
    /// The off-scenario cards the fan adopted on the PREVIOUS rebuild. Exactly the role
    /// <c>_lastHalfCards</c> / <c>_lastVisibleCards</c> play for the scenario's dock appear: a card
    /// in the new set but not in this one is a card that JUST JOINED, and only those materialize.
    /// </summary>
    private readonly List<VRCard> _offScenarioLast = new(12);

    /// <summary>True while the shared fan is OPEN with an off-scenario source's cards in it — the
    /// source's own "is he looking at it right now" test, so it can pick the animation the user
    /// will actually see and say so in its log.</summary>
    internal static bool OffScenarioFanIsOpen =>
        OffScenarioFanActive && Instance != null && Instance._fan.IsOpen;

    /// <summary>True while a character exchange is still in the air on the off-scenario fan. The
    /// swap wave owns every card's pose while it runs, so a single-card join must not also drive
    /// one — see <see cref="MaterializeNewOffScenarioCards"/>.</summary>
    internal static bool OffScenarioFanExchanging =>
        OffScenarioFanActive && Instance != null && Instance._fan.HasLeavingCards;

    /// <summary>
    /// ONE card leaves the off-scenario fan: the survivors close the gap and the card crumbles to
    /// dust where it sat. The SOURCE still owns its lifetime — this neither destroys nor parks it;
    /// it hides it when the crumble ends, and the source destroys it on its own sweep.
    ///
    /// <para>A HELD card is refused outright. The standing rule ("while a card IsHeld this code
    /// writes nothing a hold depends on", <c>CardFan.StampMembership</c>) makes taking a card out of
    /// the player's own hand the one thing this may never do — <c>VRCard.Vanish</c> refuses a held
    /// card by itself, and the source defers the removal until the release, so this is a belt.</para>
    /// </summary>
    internal static void OffScenarioFanLeave(VRCard card) => Instance?.LeaveOffScenarioFan(card);

    private void LeaveOffScenarioFan(VRCard card)
    {
        if (card == null || card.IsHeld)
            return;
        // A card that is about to be inert must not keep the beam's pop. The next frame would clear
        // it anyway (the pull-jerk grace is gated on _fan.Contains), but a vanishing card holding a
        // laser highlight for a frame reads as the fan lagging behind the menu.
        if (ReferenceEquals(card, _laserHover))
            ClearLaserHover();
        _fan.Remove(card);          // CardFan's own single-card exit — the survivors glide the gap shut
        card.Released -= OnCardReleased;
        card.Grabbed -= OnCardGrabbed;
        card.Poked -= OnCardPoked;
        _hooked.Remove(card);
        _liveGrabs.Remove(card);
        _fanOriginCards.Remove(card);
        _offScenarioLast.Remove(card);
        VRCard leaving = card;
        // Vanish resets alpha and re-enables the body when it ENDS (a pooled scenario card is about
        // to be hidden by its park callback), so an off-scenario card must be hidden by ours or it
        // would snap back to fully visible for the moment before the source destroys it.
        card.Vanish(() =>
        {
            if (leaving != null)
                leaving.gameObject.SetActive(false);
        });
        // The fan's content changed OUTSIDE a publish (CardFan.Remove above), so the order line is
        // emitted here too — otherwise the last thing the log said about this fan would still name
        // the card that is now crumbling away.
        LogFanOrder("map-room hand", $"'{card.name}' left the loadout and is crumbling out");
    }

    /// <summary>
    /// Materialize every card that is in the fan now and was not in it on the previous off-scenario
    /// rebuild. The three suppressions are the scenario's own, one for one:
    /// <list type="bullet">
    /// <item>THE FAN IS CLOSED — nobody is looking, and the palm-gate reveal owns that entrance
    /// (the same argument that gates the character exchange on <c>_fan.IsOpen</c>).</item>
    /// <item>THE FIRST ADOPTION — <c>_offScenarioLast</c> is empty, i.e. this is the fan being
    /// populated rather than a card joining it. Verbatim the scenario's "no storm" guard
    /// (<c>_dockAnimSuppressed</c>).</item>
    /// <item>AN EXCHANGE IS RUNNING — the swap blend writes every incoming card's pose every frame,
    /// so a second animation on the same transform would fight it.</item>
    /// </list>
    /// </summary>
    private void MaterializeNewOffScenarioCards(bool swap)
    {
        if (swap || !_fan.IsOpen || _offScenarioLast.Count == 0 || _fan.HasLeavingCards)
            return;
        int appeared = 0;
        for (int i = 0; i < _fanBuffer.Count; i++)
        {
            VRCard card = _fanBuffer[i];
            if (card == null || card.IsHeld || card.IsFlying || card.IsVanishing)
                continue;
            if (_offScenarioLast.Contains(card))
                continue;
            card.PlayAppear();
            appeared++;
        }
        if (appeared == 0)
            return;
        VRLog.Info("Cards", $"Off-scenario fan JOIN: {appeared} card(s) materialize into the OPEN "
                            + $"fan (VRCard.PlayAppear — the scenario's own dust appear, "
                            + $"{VRCard.DockAppearSeconds:F2}s), and the {_fanBuffer.Count - appeared} "
                            + "card(s) already there keep their slabs and glide to their new arc "
                            + "slots (CardFan.SetCards -> Relayout). READ THIS AS: the loadout gained "
                            + "a card while the hand was up. If the map-room hand reports an ADD and "
                            + "this line is missing, the fan was CLOSED, the exchange was still in "
                            + "the air, or this was the fan's first adoption — all three are "
                            + "deliberate suppressions, see MaterializeNewOffScenarioCards.");
    }

    /// <summary>
    /// Give the fan up: empty it, unhook every card and drop every driver-side reference to them,
    /// so the source may destroy them in the same frame. Leaves the fan in the state a fresh
    /// scenario expects (Interactive, empty), which is what <see cref="Rebuild"/>'s null-hand arm
    /// also asserts.
    /// </summary>
    private void ReleaseOffScenarioFan(string reason)
    {
        if (!OffScenarioFanActive)
            return;
        OffScenarioFanActive = false;

        for (int i = 0; i < _fanBuffer.Count; i++)
        {
            VRCard card = _fanBuffer[i];
            if (card == null)
                continue;
            card.Released -= OnCardReleased;
            card.Grabbed -= OnCardGrabbed;
            card.Poked -= OnCardPoked;
            _hooked.Remove(card);
            _liveGrabs.Remove(card);
            _fanOriginCards.Remove(card);
        }
        OffScenarioFanInitiatives = null;   // the keys pointed at cards nobody holds any more
        _fanBuffer.Clear();
        _offScenarioLast.Clear();   // the next adoption is a FIRST one again: no join storm
        _fan.SetCards(_fanBuffer);
        _fan.SetMode(CardFan.FanMode.Interactive);
        _placementRefusal = "none (the fan is fully interactive)";
        VRLog.Info("Cards", $"Off-scenario hand source released ({reason}) — the fan is empty and "
                            + "every card of it is unhooked, so the source may destroy them now. "
                            + "The palm gate closes it on the next tick (allowFan goes false).");
    }
}
