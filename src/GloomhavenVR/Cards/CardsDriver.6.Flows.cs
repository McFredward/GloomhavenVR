using System.Collections.Generic;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
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
        else if (IsPickMode(mode))
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

    // ------------------------------------------------------ pick progress + confirm routing --

    // Change-gate for UpdatePickStatus: the full input tuple of the strings it builds
    // (plus the tray root's identity, so a board switch/teardown re-pushes the banner
    // to the freshly built tray). The strings (Format/concat) are only rebuilt when
    // any input changed — never per frame.
    private (CardHandMode mode, int total, int locked, int placed, bool dialog, bool reopen,
             CPlayerActor? actor, string lang, int trayId)? _pickStatusKey;

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
            line += " — " + ItemFanOpenHint();
        _tray.SetPickStatus(who.Length > 0 ? who + ": " + line : line, null, null);
        return true;
    }

    /// <summary>
    /// "Open the items pile" hint for the MANDATORY item-demand banner. The mod no longer raises
    /// the item fan for the player (see <see cref="ItemsPile"/>'s class doc — the auto-open was
    /// what made the fan unclosable), so a blocking demand has to name the affordance.
    ///
    /// The string belongs in <c>Core.Loc</c>'s table under <c>item_fan_open_hint</c>; that file is
    /// shared, so this build carries the wording INLINE and prefers the table entry the moment it
    /// exists (<c>Loc.Mod</c> returns the id itself for an unknown key — that is the probe here).
    /// Delete this fallback once the key is in the table.
    /// </summary>
    private static string ItemFanOpenHint()
    {
        const string key = "item_fan_open_hint";
        string localized = Core.Loc.Mod(key);
        if (!string.Equals(localized, key, System.StringComparison.Ordinal))
            return localized;
        return Core.Loc.CurrentLanguage == "German"
            ? "tippe den Gegenstände-Stapel an, um den Fächer zu öffnen"
            : "tap the items pile to open the fan";
    }

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

    private void UpdatePickStatus(CardsHandUI? hand)
    {
        if (UpdateItemDemandStatus(hand))
            return; // the item-surrender demand owns the banner while its picker is open
        if (UpdatePanelDecisionStatus())
            return; // a floating-panel decision (doom / distribute) owns the banner
        if (hand == null || !_tray.IsVisible || !IsPickMode(CardsGameApi.Mode(hand)))
        {
            if (_pickStatusKey.HasValue)
            {
                _pickStatusKey = null;
                _tray.SetPickStatus(null, null, null);
            }
            return;
        }

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
            banner = Compose(who, line);
            // Batch lock available (>1 full batch still outstanding + the live batch is
            // full): the CONFIRM keycap becomes "WEITER" and routes to TryLockPickBatch.
            if (total - locked > 2 && placed >= 2 && !_pickReopenBusy)
                confirmLabel = Core.Loc.Mod("pick_batch_next");
        }
        _tray.SetPickStatus(banner, confirmLabel, undoLabel);

        static string Compose(string who, string line) =>
            who.Length > 0 ? who + ": " + line : line;
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

        switch (fanMode)
        {
            case CardFan.FanMode.Interactive:
                VRLog.Info("Cards", "Hand fan INTERACTIVE — grab, laser-pluck and slot placement are all live.");
                break;
            case CardFan.FanMode.Inspect:
                VRLog.Info("Cards", "Hand fan INSPECT-ONLY — the GRAB is ALLOWED (proximity, laser pluck, " +
                                    "hand-to-hand transfer: pick a card up and read it, in any phase). The " +
                                    $"PLACEMENT is refused by: {why}. A release returns the card HOME to the fan " +
                                    "with no game call whatsoever (VRCard.InspectOnly → CardsDriver." +
                                    "OnCardReleased's inspection branch, which runs before CurrentHand() is even " +
                                    "resolved). Poke-select stays off, as in every hand-fan state.");
                break;
            default:
                VRLog.Info("Cards", "Hand fan PICTURE — neither grabbable nor laser-clickable. This is the " +
                                    "FOREIGN-character case and the only one left: the hand belongs to another " +
                                    "player (CardsGameApi.IsLocalHand false), so the read-only focus guarantee " +
                                    "stays absolute — no VR input can reach a game call for a character we are " +
                                    $"only watching. Placement gate: {why}.");
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
            // (this is the literal "die Option wurde nicht angeboten").
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
    private float _longRestPumpNextTry;   // throttle for the actual game calls (state reads stay per-tick)
    private bool _longRestPumpQueued;     // at most one queued advance in flight
    private bool _longRestPumpAnnounced;  // change-deduped "turn arrived" log

    private void PumpLongRestTurn()
    {
        CardsHandUI? hand = CardsGameApi.LongRestTurnHand();
        if (hand == null)
        {
            _longRestPumpAnnounced = false;
            return;
        }
        if (CardsGameApi.Mode(hand) == CardHandMode.LoseCard)
            return; // burn step live — the existing pick flow owns it from here
        if (WorldUI.ModalFallback.BlockingWindowModalActive)
            return; // never advance the turn under a blocking modal (story/results/…)
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
        string step = "";
        CardActionQueue.Enqueue(
            () =>
            {
                // Re-resolve INSIDE the queued action: it runs a frame later, behind any
                // pending card selects, and every gate is re-checked against fresh state.
                CardsHandUI? fresh = CardsGameApi.LongRestTurnHand();
                if (fresh != null)
                    advanced = CardsGameApi.TryAdvanceLongRestTurn(fresh, out step);
            },
            () =>
            {
                _longRestPumpQueued = false;
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

    private (bool selected, bool losing, bool done)? _longRestState;

    /// <summary>
    /// Prove the long-rest state machine from the log alone (test #28, item 3),
    /// change-deduped:
    /// (1) long rest SELECTED in card selection (initiative 99, heal pending),
    /// (2) the BURN step is live — <c>CardHandMode.LoseCard</c> while
    ///     <c>CharacterClass.LongRest</c> — lay a discarded card into the left slot,
    /// (3) RESOLVED — <c>HasLongRested</c> (card burnt, +2 heal applied).
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
        var state = (selected, losing, done);
        if (_longRestState.HasValue && _longRestState.Value == state)
            return;
        _longRestState = state;

        if (losing)
            VRLog.Info("Cards", "Long rest: BURN step active (CardHandMode.LoseCard, LongRest set) — " +
                                "lay a discarded card into the left slot to lose it; the docked Confirm commits it.");
        else if (done)
            VRLog.Info("Cards", "Long rest: RESOLVED — chosen card burnt, +2 heal applied (HasLongRested).");
        else if (selected)
            VRLog.Info("Cards", "Long rest: SELECTED in card selection (initiative 99, heal pending) — " +
                                "waiting for this actor's turn to choose the card to lose.");
    }

    /// <summary>
    /// Enforce "slot 0 = initiative": if the game's initiative card is not the slot-0
    /// occupant (e.g. cards were dropped right-to-left), issue the game's own swap.
    /// </summary>
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
        if (initiative != null && initiative != slot0.GameCard.AbilityCard)
        {
            if (CardsGameApi.SwapInitiative(hand))
                VRLog.Debug("Cards", "Initiative reconciled to slot order.");
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
        if (pickHand != null && IsPickMode(CardsGameApi.Mode(pickHand)))
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
        CardsHandUI? pickHand = CurrentHand();
        if (pickHand != null && CardsGameApi.IsPickConfirmDialogOpen(pickHand))
        {
            CardActionQueue.Enqueue(
                () => CardsGameApi.CancelPickConfirmDialog(),
                () =>
                {
                    VRLog.Info("Cards", "Board: UNDO → pick confirm dialog CANCEL (the game's own " +
                                        "\"choose another card\") — all picks reopened, candidates back in the fan.");
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
    /// das darf nicht blockieren." So there is NO turn key, NO phase whitelist and NO "is it your
    /// character" test here, and there must never be one again: a pile is public, readable
    /// information (vanilla lets anyone open ANY player's full card overview from the initiative
    /// track). Exactly two refusals survive, and both are OWNERSHIP-OF-THE-WIDGETS refusals rather
    /// than permission refusals:</para>
    /// <list type="number">
    /// <item>A live modal PICK flow of the character the GAME drives (<see cref="IsPickMode"/>):
    ///   those flows re-purpose the very <c>AbilityCardUI</c> widgets of the discard pile as the
    ///   pick fan (<c>CardHandMode.LoseCard</c> on <c>CardPileType.Discarded</c>), so an arc
    ///   borrowing them at the same time would fight the pick for one visual per card. A READ-ONLY
    ///   focus view is exempt for the same reason <c>Rebuild</c> forces its <c>pick</c> false: the
    ///   watched character's <c>CardsHandUI.currentMode</c> is a stale leftover of its own last
    ///   decision and owns nothing here.</item>
    /// <item>A real modal dialog (<see cref="VRMode.ModalUI"/>) — it owns the scene and the input.</item>
    /// </list>
    /// </summary>
    /// <param name="readOnly">True while the presented character is a FOCUS override, i.e. one the
    /// game is not driving. Passed in rather than read off <c>CharacterFocus.ReadOnlyView</c>
    /// because that flag is latched by the edge-driven rebuild: the open path needs the LIVE
    /// answer, the rebuild path already holds this rebuild's freshly latched one.</param>
    private static bool BrowseAllowed(CardHandMode mode, bool readOnly, out string? refusal)
    {
        if (!readOnly && IsPickMode(mode))
        {
            refusal = $"a modal pick flow owns the pile widgets right now (mode={mode}) — the same " +
                      "cards are the pick fan, and one card has exactly one visual";
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

    /// <summary>The modal pick modes (poke-select fan flows; drop-field flows since test #21).</summary>
    private static bool IsPickMode(CardHandMode mode) =>
        mode == CardHandMode.LoseCard
        || mode == CardHandMode.DiscardCard
        || mode == CardHandMode.RecoverDiscardedCard
        || mode == CardHandMode.RecoverLostCard
        || mode == CardHandMode.IncreaseCardLimit;

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
        if (presentedHand == null || anchor == null || !CardsConfig.PileViewer.Value)
            return;
        CardHandMode mode = CardsGameApi.Mode(presentedHand);
        // Derived here rather than read off CharacterFocus.ReadOnlyView, which is latched by the
        // edge-driven rebuild: a poke landing in the frame between the focus click and its rebuild
        // would otherwise be judged against the PREVIOUS view's answer. Same definition, live.
        bool readOnly = !ReferenceEquals(presentedHand, gameHand);
        if (!BrowseAllowed(mode, readOnly, out string? refusal))
        {
            VRLog.Info("Cards", $"PILE BROWSE REFUSED: {kind} — {refusal} (trigger: {trigger}).");
            return;
        }
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
        VRLog.Info("Cards", $"PILE BROWSE OPEN: {kind} of " +
                            $"'{Board.CharacterFocus.Describe(presentedHand.PlayerActor)}' " +
                            $"(mode={mode}, focusView={readOnly}) — " +
                            $"trigger: {trigger}. The pile is openable in EVERY phase and for EVERY " +
                            "character the board can present — whose turn it is has no say (user " +
                            "ruling 2026-08-08); it follows a focus switch instead of closing.");
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
        VRLog.Info("Cards", $"PILE BROWSE CLOSE: {_browser.Kind} — trigger: {reason}.");
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
        if (!BrowseAllowed(mode, Board.CharacterFocus.ReadOnlyView, out string? refusal))
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
            if (widget.AbilityCard == null || widget.IsLongRest)
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
            if (BoardOwnsCardVisual(card) || CardEnRouteToPile(card))
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
    /// are resolved (<see cref="CardsGameApi.GetActiveHalves"/>) and highlighted. Empty /
    /// [Cards] ActivePile off → the area shows nothing. The zone-flag loop keeps these
    /// cards grabbable-to-read and out of the park sweep; their release routes back to the
    /// column (never a game seam). Logs the active count change-deduped.
    /// </summary>
    private void UpdateActive(CardsHandUI hand)
    {
        // #5: while ANOTHER actor is taking its turn (an enemy, or another character), the control board
        // shows NO cards — only the cards of the character whose turn it currently is. The active pile is
        // otherwise drawn every frame regardless of turn; gate it on this being the local character's own
        // action turn OR the shared card-selection phase (where everyone picks at once). The round/played
        // cards are already gated the same way (IsActionTurn, CardsDriver Rebuild ActionSelection case).
        if (!CardsConfig.ActivePile.Value
            || (!CardsGameApi.IsActionTurn(hand) && !CardsGameApi.IsSelectionPhase(hand)))
        {
            _active.SetVisible(false);
            _activeBuffer.Clear();
            _active.SetCards(_activeBuffer); // clear its list so Contains()/park stay accurate
            if (_loggedActiveCount != -1)
                _loggedActiveCount = -1;
            return;
        }

        _active.EnsureBuilt(_tray);
        CardsGameApi.GetActivePileWidgets(hand, _activeWidgetBuffer);
        _activeBuffer.Clear();
        for (int i = 0; i < _activeWidgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _activeWidgetBuffer[i];
            if (widget.AbilityCard == null || widget.IsLongRest)
                continue;
            VRCard card = AdoptedCard(widget);
            CardsGameApi.GetActiveHalves(hand, widget.AbilityCard, out bool top, out bool bottom);
            SetActiveHighlight(card, top, bottom); // native game action-region highlight
            _activeBuffer.Add(card);
        }

        _active.SetCards(_activeBuffer);
        _active.SetVisible(_activeBuffer.Count > 0);

        if (_loggedActiveCount != _activeBuffer.Count)
        {
            _loggedActiveCount = _activeBuffer.Count;
            VRLog.Info("Cards", $"Active cards: {_activeBuffer.Count} shown in the ACTIVE area " +
                                "(authoritative CardPileType.Active pile; active halves highlighted).");
        }
    }

    /// <summary>
    /// Drive the NATIVE game action-region highlight on a card's active half/halves
    /// (feature 6) — the exact mouse-over visual. The mod re-parents the live
    /// <see cref="FullAbilityCard"/> rect onto the VR card's world canvas, so
    /// <c>FullAbilityCard.ToggleHighlightHover</c> (the animated <c>CardActionHighlight</c>
    /// pulse) / <c>UntoggleHighlightHover</c> render on the VR card automatically. A side
    /// resolved active shows its region; an inactive side is explicitly untoggled so a
    /// reused card carries no stale highlight. <c>isDefault:false</c> = a normal ability
    /// region.
    /// </summary>
    private static void SetActiveHighlight(VRCard card, bool top, bool bottom)
    {
        FullAbilityCard? full = card.FullCard;
        if (full == null)
            return;
        if (top)
            full.ToggleHighlightHover(active: true, isTopSide: true, isDefault: false);
        else
            full.UntoggleHighlightHover(isTopSide: true);
        if (bottom)
            full.ToggleHighlightHover(active: true, isTopSide: false, isDefault: false);
        else
            full.UntoggleHighlightHover(isTopSide: false);
    }

    /// <summary>Clear the native action-region highlight on both halves (feature 6).</summary>
    private static void ClearActiveHighlight(VRCard card)
    {
        FullAbilityCard? full = card.FullCard;
        if (full == null)
            return;
        full.UntoggleHighlightHover(isTopSide: true);
        full.UntoggleHighlightHover(isTopSide: false);
    }

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

    /// <summary>Cheap change-gate hash of the active-card set (ids) + round. 0 = none / disabled.</summary>
    private int ActiveSignature(CardsHandUI? hand)
    {
        if (hand == null || !CardsConfig.ActivePile.Value)
            return 0;
        CardsGameApi.GetActivePileWidgets(hand, _activeWidgetBuffer);
        int sig = 17;
        for (int i = 0; i < _activeWidgetBuffer.Count; i++)
            sig = sig * 31 + _activeWidgetBuffer[i].CardID;
        // Bonus half-activity can shift at a round boundary without the card set changing.
        sig = sig * 31 + CardsGameApi.RoundNumber();
        return sig;
    }

    // ------------------------------------------------------------------ dev fake hand --

    private void RebuildFakeOrClear(Transform anchor)
    {
        // No active local hand: no piles to show or browse, no pick field (test #21
        // C — the stacks hide, an open browse closes, field occupants clear; all
        // return with the next active hand).
        _piles.SetVisible(false);
        _active.SetVisible(false); // feature 6: no active hand → no active-cards area
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
        RemoveShortRestCard(); // sacrifice display never survives losing the active hand (item 1d)

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
            out float d1, out float d2, out float radius);
        if (slot >= 0 && _tray.Occupant(slot) != null && _tray.Occupant(slot) != card)
        {
            int other = 1 - slot;
            slot = _tray.Occupant(other) == null ? other : -1;
        }
        string rule = highlightSlot >= 0 ? "highlight" : slot >= 0 ? "radius" : "none";
        if (highlightSlot >= 0)
            slot = highlightSlot; // test #15: what glows is what drops (see OnCardReleased)
        VRLog.Info("Cards", $"Drop ({hand.Side}, fake): slot1 {d1:F2} m, slot2 {d2:F2} m, radius {radius:F2} m, " +
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
}
