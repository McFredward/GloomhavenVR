using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray part 5 of 7 (see PlayTray.1.Core.cs for the split map and its rules).
// Region: status (TickStatus).

internal sealed partial class PlayTray
{
    // ------------------------------------------------------------------ status --

    // Last shown round number (change-gated; int.MinValue = never). Rebuilding the
    // string only on CHANGE avoids a per-frame ToString allocation AND a per-frame TMP
    // text assignment — every rewrite re-triggers TMP's auto-size layout (test #13).
    private int _roundShown = int.MinValue;

    // Confirmed-state label, built once — TickStatus runs per frame and the lookup would
    // allocate every tick (badge/round lesson, test #13). See <see cref="UnreadyLabel"/>
    // for what it says and why it is no longer the game's own string.
    private string? _confirmedLabel;

    // Language the follow/gear labels + cached round/confirmed strings were built in.
    // The TickStatus guard re-localizes them on an actual game-language change (live follow).
    private string _labelLang = string.Empty;

    // Change-gate for the confirm-ownership line (see <see cref="ConfirmCapsForeignView"/>): the
    // last (hidden, owner, focused, recoverable) tuple we logged, folded into one int from the
    // game's own actor ids so the per-frame resolve builds no string at all. 0 = nothing yet.
    private int _capOwnerLogKey;

    /// <summary>Update round readout, badge, confirm/undo button states + labels (each frame while visible; cheap).</summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        // ButtonTuning live-apply (user #9): geometry entries rebuild the keycaps in place.
        ApplyButtonTuningIfChanged();

        // Task #2 follow-up: re-assert the slot-dock grab apron every tick (idempotent
        // flag check inside SetDockGrabPad) — a card that entered occupancy while HELD
        // (PlaceCard skips the held card, "the release path homes it") gets its apron
        // the moment it rests in the slot, regardless of which path homed it.
        for (int s = 0; s < 2; s++)
        {
            VRCard? occ = _occupants[s];
            if (occ != null && !occ.IsHeld)
                occ.SetDockGrabPad(true);
        }

        // Live language following: the follow/gear labels are set at events only and the
        // round/confirmed strings are cached, so on an ACTUAL language change re-label the
        // frame buttons and invalidate the caches (the per-tick logic below re-localizes the
        // round readout + confirm/undo labels via CardsGameApi/Loc.Game). Change-gated so the
        // TMP writes never happen per frame.
        string lang = Core.Loc.CurrentLanguage;
        if (lang != _labelLang)
        {
            _labelLang = lang;
            _confirmedLabel = null;     // rebuild "✓ READY" in the new language
            _roundShown = int.MinValue; // force the round readout to re-localize
            if (_followToggle != null)
                _followToggle.SetLabel(CardsConfig.TrayFollow.Value
                    ? Core.Loc.Mod("follow") : Core.Loc.Mod("pinned"));
            // The toggle's word now lives in the BOARD, not on the cap, so the language change has
            // to reach the engraving as well — this is the "es kann lokalisiert sein" half of the
            // requirement, and it is the reason the caption is TMP text laid into the board rather
            // than pixels in the board's own atlas.
            RefreshFollowEngraving();
        }

        // Round readout (test #18): the PhaseBanner world conversion is GONE — the
        // round number lives on the dashboard instead, read from the same state the
        // banner showed (CardsGameApi.RoundNumber). Change-gated: TMP rewrites
        // re-trigger auto-size layout (the badge flicker lesson, test #13).
        if (_roundLabel != null)
        {
            int round = CardsGameApi.RoundNumber();
            if (round != _roundShown)
            {
                _roundShown = round;
                string text;
                if (round <= 0)
                {
                    text = "-";
                }
                else
                {
                    // The banner's own text: GUI_START_ROUND_BANNER is "Runde {0}"
                    // (PhaseBannerHandler.ShowStartRound). Guard the Format — a
                    // malformed localization must not kill the status tick.
                    try
                    {
                        text = string.Format(
                            Core.Loc.Game("GUI_START_ROUND_BANNER", "Round {0}"), round);
                    }
                    catch (System.FormatException)
                    {
                        text = $"Round {round}";
                    }
                }
                _roundLabel.text = text;
                // The HUD font is harvested off a live game widget and can arrive AFTER the board
                // was built, which would leave the number's carve unstyled for the rest of the
                // session on a board built early. Re-applying on this change-gated path is cheap
                // and idempotent, and the peer's mirror of this readout does exactly the same.
                BoardEngraving.Restyle(_roundLabel, CardsConfig.CurrentBoard);
                VRLog.Info("Cards", $"Board: round readout → '{text}'.");
            }
        }

        // Test #24 item 3: the mod-drawn initiative badge over slot 0 is GONE — the
        // current initiative already reads on the docked initiative track (top edge),
        // so the redundant number circle was removed. Physical card swap still swaps
        // initiative (CardsDriver drives it on the slot gesture). We still read the
        // ready state here for the CONFIRM accent below.
        bool ready = hand != null && CardsGameApi.IsSelectionReady(hand);

        // Solo-host card-selection rescue (bug #5b): when hosting online with no other
        // players the game leaves BOTH commit affordances dead (SP ReadyButton
        // deactivated, MP ready toggle forced non-interactable until someone connects),
        // so the round can never advance. Re-run the game's own re-enable each tick while
        // stuck; a no-op in every other case (≥2 players, offline, other phases). Once it
        // flips the toggle interactable the CONFIRM visibility below surfaces the button.
        CardsGameApi.EnsureSoloHostSelectionCommittable();

        // ONE CHARACTER OWNS THE CONFIRM (user, ModBuild 84 hardware test: "Der 'Fortfahren'
        // button erscheint bei allen Characteren — das macht keinen Sinn. Nur beim Character der
        // Entscheidung treffen soll, soll auch der entsprechende Button erscheinen."). Resolved
        // ONCE per tick and applied to BOTH keycaps below; see the method for the owner rule and
        // the deadlock argument.
        bool foreignView = ConfirmCapsForeignView(hand);

        // ---- A PLACED ITEM OWNS THE GENERIC CLUSTER (user, ModBuild 103) ----------------------
        //
        // Verbatim: "Wenn ein Gegenstand abgelegt wurde sollen bei den allgemeinen Buttons erstmal
        // NUR noch der 'Benutzen' Button zu sehen sein und die anderen Buttons die zuvor da waren
        // verschwinden - wird der Gegenstand wieder aus dem Overlay entfernt soll der
        // Benutzen-Button wieder verschwinden und die Buttons die eventuell zuvor da waren sollen
        // wieder angezeigt werden."
        //
        // SUPPRESSION, NOT SAVE-AND-RESTORE, and the distinction is the whole design. The literal
        // reading — snapshot the visible set on placement, put that same set back on removal — is
        // wrong in the one case that actually happens: the game may add or withdraw a decision
        // WHILE the card lies in the recess (a damage prompt arrives, an undo window closes), and
        // restoring the snapshot would then either resurrect a button whose action no longer
        // exists or swallow one that appeared meanwhile. Here the branches below are simply not
        // reached while a card is placed, so on removal each cap is decided FRESH by its own live
        // owner on the very next tick. "The buttons that were there before" therefore means "the
        // buttons that belong there now", which is the only reading that cannot go stale.
        //
        // The USE cap itself is untouched: it is a separate member of the same cluster, driven by
        // SetItemUseConfirm (PlayTray.4.Slots.cs) off the same placement, so "only USE remains" is
        // what these two hides leave behind. Both go through BoardButton.SetVisible, i.e. the
        // authored crumble on the way out and the assemble-out-of-dust on the way back — the
        // standing no-pop rule, and the defect an earlier round of this same area was reported for
        // ("die Knöpfe verschwinden ohne die Animation").
        //
        // THE SKIP CAP IS INSIDE THIS RULE NOW, not beside it. It used to be a member of the
        // separate WorldUI.ButtonCluster, which stood down on a COPY of this predicate read back out
        // of PlayTray (`Cards.PlayTray.Current?.ItemUseCapShown == true`). The cluster is gone and
        // the cap is a generic keycap on seat 2, so "only USE remains" is one hide next to the other
        // two rather than the same rule written twice in two files.
        if (_itemUseActive)
        {
            _confirm?.SetVisible(false);
            _undo?.SetVisible(false);
            _skip?.SetVisible(false);
            return;
        }

        // Test #23 item 4: the REAL ReadyButton / UndoButton dock at these same
        // positions when the native-controls surface is active. While a native
        // widget holds, its mod-drawn twin hides (they overlap) and its state mirror
        // is skipped; when it undocks (feature off / widget hidden / no tray) the mod
        // button reappears with its full state logic — never a missing control.
        // Item 7: hide the mod Confirm ONLY when a REAL, VISIBLE native Continue replaces
        // it. In the "all cards of all characters placed" state the native ReadyButton
        // stays docked + interactable while the game drives its canvasGroup alpha to ~0
        // (VR hides the 2D stack) — docked but not rendering. Gating on ContinueDocked
        // alone hid the mod Confirm too, leaving nothing visible yet still pressable
        // (the docked host's raycaster). Gate on docked AND visible so the mod Confirm
        // shows whenever it is the only thing the player can actually see/press; the
        // docked-but-invisible host's raycaster is stood down in TrayControlDockSurface.
        if (_confirm != null
            && WorldUI.Surfaces.TrayControlDockSurface.ContinueDocked
            && WorldUI.Surfaces.TrayControlDockSurface.ContinueVisible)
        {
            _confirm.SetVisible(false);
        }
        else if (_confirm != null && foreignView)
        {
            // The confirm belongs to the character the GAME presents, and the player is looking
            // at somebody else. Hidden — ahead of the pick override on purpose: the pick branch's
            // press acts on CurrentHand() too (CardsDriver.OnConfirmRequested re-reads it), so
            // showing it here would offer the viewed character a button that commits ANOTHER
            // character's pick. The way back is one portrait click; ConfirmCapsForeignView has
            // already proven it exists before returning true.
            _confirm.SetVisible(false);
        }
        else if (_confirm != null && _pickConfirmLabel != null)
        {
            // EVENT-DISCARD pick flow: the driver overrode the CONFIRM keycap — either the
            // game's confirm dialog is open (label = ITS commit option, e.g. "Karten
            // abwerfen"; the press routes to DialogPopup's own callback) or a >2-card
            // requirement has a full batch to lock ("WEITER"). Always shown + accented:
            // this is the flow's ONLY reachable commit affordance in VR (the deadlock fix).
            _confirm.SetVisible(true);
            _confirm.SetState(true, accent: true);
            _confirm.SetLabel(_pickConfirmLabel);
        }
        else if (_confirm != null)
        {
            // Ready-state mirror (test #19): while THIS hand's player has confirmed
            // (online card selection — the only game state where a confirm persists
            // and is revocable, see CardsGameApi.ReadyToggle), the button flips to
            // a distinct gold "✓ …" state; pressing it then REVOKES through the
            // game's own un-ready path (CardsDriver.OnConfirmRequested). Switching
            // the active hand re-reads the state each tick, so the display always
            // tracks the shown character. Offline the game has no confirmed-waiting
            // state (END SELECTION starts the round at once) — normal affordance.
            bool confirmed = hand != null && CardsGameApi.IsConfirmed(hand);
            bool canConfirm = hand != null
                              && (CardsGameApi.CanConfirm() || CardsGameApi.ReadyToggleAvailable());
            // Item 7 ("wenn es nicht drückbar ist dann soll es dort auch nicht erscheinen"):
            // only SHOW confirm when its action is actually possible right now — a valid
            // confirmable selection exists (canConfirm) or the player has confirmed and can
            // revoke (confirmed). Otherwise HIDE it (not merely disable), so an unpressable
            // button never appears; it returns the instant the action becomes possible.
            bool show = canConfirm || confirmed;
            _confirm.SetVisible(show);
            if (show)
            {
                // Pick flows (test #28): during a single-card pick, CONFIRM is the mode's
                // mirrored confirm affordance (the ReadyButton path the recover flows arm) —
                // accent it as soon as the game reports it fireable.
                _confirm.SetState(true,
                    accent: (ready || _pickActive) && canConfirm && !confirmed, confirmed: confirmed);
                _confirm.SetLabel(confirmed
                    ? _confirmedLabel ??= UnreadyLabel()
                    : CardsGameApi.ReadyToggleAvailable() && !CardsGameApi.CanConfirm()
                        ? Core.Loc.Game("GUI_END_SELECTION", "END SELECTION")
                        : CardsGameApi.ConfirmLabel());
            }
        }
        if (_undo != null && WorldUI.Surfaces.TrayControlDockSurface.UndoDocked)
        {
            _undo.SetVisible(false);
        }
        else if (_undo != null && foreignView)
        {
            // Same owner, same rule: CardsGameApi.CanUndo/ClickUndo read the ONE global
            // Choreographer.m_UndoButton, and OnUndoRequested's pick branch re-reads CurrentHand()
            // — so an UNDO press always reverts the acting character's step, never the viewed
            // one's. Strictly weaker than the confirm case for deadlock purposes: undo is a
            // revert, never the only way forward, and it returns with the same portrait click.
            _undo.SetVisible(false);
        }
        else if (_undo != null && _pickUndoLabel != null)
        {
            // EVENT-DISCARD pick flow: UNDO becomes the confirm dialog's own cancel
            // ("Wähle eine andere Karte") while it is open — the reachable stand-in for
            // the 2D popup's second option.
            _undo.SetVisible(true);
            _undo.SetState(true, accent: false);
            _undo.SetLabel(_pickUndoLabel);
        }
        else if (_undo != null)
        {
            // Item 7: only SHOW undo when there is actually something to undo — hide it
            // (not just disable) otherwise; it reappears the instant an undo is available.
            bool canUndo = hand != null && CardsGameApi.CanUndo();
            _undo.SetVisible(canUndo);
            if (canUndo)
            {
                _undo.SetState(true, accent: false);
                _undo.SetLabel(CardsGameApi.UndoLabel());
            }
        }

        // ---- the turn-flow SKIP, seat 2 ------------------------------------------------------
        //
        // ONE CHARACTER OWNS THE SKIP (user, hardware ModBuild 96): "…bleibt trotzdem der 'Angriff
        // überspringen' bzw. 'Bewegung überspringen' Knopf noch am Board sichtbar, obwohl ein
        // anderer Character ausgewählt wurde — er soll wie die anderen Buttons auch nur bei dem
        // Character angezeigt werden, der diese Wahl aktuell treffen muss." That rule used to live
        // in WorldUI.ButtonCluster as a term-for-term RE-DERIVATION of ConfirmCapsForeignView,
        // rebuilt from the same static CardsGameApi members because the original was private to this
        // class. It is the same `foreignView` local the two caps above use now — the duplicate is
        // gone with the cluster, which is the strongest form of "they cannot disagree".
        //
        // VISIBLE means the GAME is showing it, not that its GameObject is active — see
        // CardsGameApi.SkipShown for the measured reason (the Choreographer raises m_SkipButton on
        // EVERY client and hides it for the non-acting ones through the canvas alpha).
        //
        // AND IT IS DIMMED RATHER THAN HIDDEN WHEN DEAD, which is where it legitimately differs from
        // Confirm and Undo: those two are hidden when unpressable (item 7, "wenn es nicht drückbar
        // ist dann soll es dort auch nicht erscheinen") because their own predicates ignore the
        // widget's alpha and can therefore report "not pressable" while the game is still showing
        // the control. The skip's visibility IS the game's own alpha, so a visible-but-dead skip is
        // the game's own picture and the cap reproduces it — and the state the peer mirror draws
        // from the cap-state byte.
        //
        // ACCENTED WHENEVER SHOWN, which is the retired cluster cap's own behaviour ported rather
        // than a new choice: a ButtonCluster.PhysicalButton had exactly TWO looks, its authored
        // accent and that accent lerped toward dark wood, and MirrorSkip only ever handed it
        // visible+interactable. Passing accent:false here would rest the cap at the shared parchment
        // idle instead and silently retire the slate-blue the player recognises the control by. The
        // peer's ApplyCapStates passes the same true for the same reason, so the two agree.
        if (_skip != null && foreignView)
        {
            _skip.SetVisible(false);
        }
        else if (_skip != null)
        {
            bool shown = hand != null && CardsGameApi.SkipShown();
            _skip.SetVisible(shown);
            if (shown)
            {
                _skip.SetState(CardsGameApi.CanSkip(), accent: true);
                _skip.SetLabel(CardsGameApi.SkipLabel());
            }
        }
    }

    /// <summary>
    /// ONE CHARACTER OWNS THE CONFIRM — the keycap edition of the rule
    /// <c>WorldUI.Surfaces.DecisionDockSurface.UpdateFocusVisibility</c> already applies to the
    /// decision row and <c>WorldUI.Surfaces.UseBarsSurface.UpdateFocusVisibility</c> to the use
    /// bars. True while the board's CONFIRM/UNDO keycaps must be HIDDEN because the action they
    /// fire does not belong to the character the player is currently looking at.
    ///
    /// <para>THE BUG (user, ModBuild 84): free character focus lets the player look at a character
    /// the game is not acting on, and the CONFIRM keycap appeared on EVERY such view. It had to:
    /// its whole visibility test was <c>CardsGameApi.CanConfirm() || ReadyToggleAvailable()</c>,
    /// and <see cref="CardsGameApi.CanConfirm"/> reads the ONE global
    /// <c>Choreographer.readyButton</c> (via <c>CardsGameApi.CanConfirm</c>/<c>ReadyButton</c>) — a single scenario-wide
    /// widget with no character in it at all. So the same "Fortfahren" was offered on four boards.</para>
    ///
    /// <para>THE OWNER, read from the dispatch rather than guessed. <paramref name="hand"/> is the
    /// hand the GAME presents — <c>CardsDriver.CurrentHand()</c>, already ownership-gated by
    /// <c>CardsGameApi.IsLocalHand</c> and already resolved through the
    /// deciding-actor priority chain (TakeDamage → ActionSelection → ItemPick → LoseRewardPick →
    /// InitiativeAdjust → ActiveHand). It is NOT the focus-resolved hand: focus is applied in
    /// <c>CardsDriver.Rebuild</c> (<c>CharacterFocus.ResolveHand</c>), not on this path. And it is exactly the object every CONFIRM/UNDO press re-reads:
    /// <c>CardsDriver.OnConfirmRequested</c> resolves <c>CurrentHand()</c> for the pick-dialog
    /// commit, for <c>TryLockPickBatch</c> and for the <c>IsConfirmed</c>/<c>SetReady</c> branch,
    /// and its last branch fires the global <c>ReadyButton</c>, which the game only ever arms for
    /// the character it is itself acting on. So <c>hand.PlayerActor</c> is not "a reasonable
    /// attribution" — it is, literally, the character a press would act upon.</para>
    ///
    /// <para>WHY NOT <c>CharacterFocus.TurnActor</c>, the obvious candidate. The two differ in
    /// precisely the flows that matter: a take-damage burn, an item-surrender demand or the boots'
    /// initiative adjustment can be owed by a DIFFERENT local character than the one at turn (the
    /// reason <c>CurrentHand</c> has that priority chain at all), and
    /// <c>DecisionDockSurface.PromptOwner</c> attributes those very prompts to the deciding
    /// character, not to the turn. Keying on the turn would hide the confirm from the one
    /// character who owes the decision — a deadlock, and a disagreement with the decision row
    /// docked right next to it. Keying on the presented hand agrees with that surface in every
    /// flow it resolves.</para>
    ///
    /// <para>WHY <c>CharacterFocus.Focused</c> AND NOT <c>ReadOnlyView</c>. Same reason
    /// <c>CharacterFocus.LocalMark</c> gives for its own per-frame read: <c>ReadOnlyView</c> is
    /// latched by <c>ResolveHand</c>, which runs on the edge-driven REBUILD, while
    /// <see cref="TickStatus"/> runs every frame — a per-frame consumer must not read a value that
    /// is only recomputed on rebuilds. <c>Focused</c> is a plain field written by
    /// <c>TryFocus</c>/<c>Clear</c>, so "the player has taken an explicit focus override" is always
    /// current, and comparing it to the owner reconstructs the same predicate live. Both docked
    /// surfaces above use exactly this pair (<c>Focused</c> + resolved owner) for the same reason.</para>
    ///
    /// <para>FAIL-OPEN, FOUR WAYS — every clause is a reason to SHOW:</para>
    /// <list type="bullet">
    /// <item>no focus override (<c>Focused == null</c>) ⇒ shown. Merely following the game is never
    ///   "looking elsewhere", so a player who never touches the focus feature sees byte-for-byte
    ///   what ModBuild 84 showed them;</item>
    /// <item>no presented hand (<c>owner == null</c>) ⇒ shown by this gate, i.e. the pre-existing
    ///   logic decides — and it already hides the keycap, since both <c>canConfirm</c> and
    ///   <c>canUndo</c> require <c>hand != null</c>;</item>
    /// <item>the owner cannot be focused right now (<c>CharacterFocus.CanFocus</c> false: exhausted,
    ///   no live scenario, or the secret card-selection window) ⇒ shown. This is the DEADLOCK
    ///   INTERLOCK: the keycap is only ever hidden when the very click that brings it back is
    ///   available. See below.</item>
    /// <item>the confirm is NOT ATTRIBUTABLE to any character ⇒ shown, on every board. The
    ///   ModBuild 85 follow-up; see the next paragraph.</item>
    /// </list>
    ///
    /// <para>NOT ATTRIBUTABLE ⇒ EVERYBODY'S (user, ModBuild 85 hardware test: "In der Phase in dem
    /// man die Karten abgelegt hat und dann die Gegnerinformation angezeigt wird hat nach deinem Fix
    /// nur ein einziger character den 'Fortfahren' Knopf - aber in dieser Phase macht eine
    /// Differenzierung weniger Sinn, da kein fester Character gerade am Zug ist und nur dieser
    /// fortfahren kann. In diesem Fall soll jeder Character den Fortfahren Knopf haben."). The
    /// reported step is the ENEMY-INFORMATION reveal, and it is <c>CPhase.PhaseType.
    /// MonsterClassesSelectAbilityCards</c>: the Choreographer's <c>MonsterClassesToSelectAbilityCards</c>
    /// branch runs <c>m_CurrentActor = null</c> and then arms the global ReadyButton as
    /// <c>EREADYBUTTONCONTINUE</c> / "GUI_CONTINUE" with
    /// <c>disregardTurnControlForInteractability: true</c> (Choreographer.cs:3708/3715-3717), while
    /// the initiative track drives the same state through the reveal animation
    /// (InitiativeTrack.cs:401/483/504, all <c>AlternativeAction(…, EREADYBUTTONCONTINUE,
    /// GUI_CONTINUE)</c>). A press there lands in <c>ReadyButton.OnClickInternal</c>'s
    /// <c>state &gt;= EREADYBUTTONCONTINUE</c> arm, i.e. <c>ScenarioRuleClient.StepComplete()</c>
    /// (ReadyButton.cs:317-323) — a PARTY-WIDE step advance, not one character's action. The user's
    /// own diagnosis is literally the game's line of code: nobody is at turn
    /// (<c>m_CurrentActor = null</c> ⇒ <c>Choreographer.CurrentPlayerActor</c> null ⇒
    /// <c>CharacterFocus.TurnActor</c> null), so the confirm cannot belong to anybody, so it belongs
    /// to everybody.</para>
    ///
    /// <para>THE PREDICATE, and why it is not a phase name. "Nobody at turn" ALONE would be wrong:
    /// during an ENEMY's turn <c>m_CurrentActor</c> is that enemy, so <c>CurrentPlayerActor</c> is
    /// null too — and a take-damage burn or an item-surrender demand raised in that window DOES have
    /// exactly one owner, the character the gate exists to protect. What separates the two is
    /// visible in <see cref="CardsGameApi.DecidingHand"/>: an owned decision CLAIMS the presented
    /// hand through the deciding-actor chain, while the enemy-information step claims nothing and
    /// <see cref="CardsGameApi.ActiveHand"/> supplies a leftover character tab. So the gate asks the
    /// general question — <b>is this confirm attributable at all?</b> — as
    /// <c>TurnActor != null || DecidingHand() != null</c>, and falls open when it is not. That
    /// covers the reported step, every other between-turns "Continue" (round-start effects, the
    /// end-of-round advance, the post-enemy-animation proceed) and nothing that has an owner. It
    /// can only ever turn a HIDE into a SHOW: <c>foreign</c> gains a conjunct, so no state that
    /// showed the keycap before can stop showing it.</para>
    ///
    /// <para>MULTIPLAYER, and why there is NO host/client branch here. In that phase the game itself
    /// disarms the press for a non-host client, twice over:
    /// <c>ReadyButton.CheckButtonInteractability</c> forces <c>readyButton.interactable = false</c>
    /// whenever <c>FFSNetwork.IsOnline &amp;&amp; IsClient &amp;&amp; PhaseManager.CurrentPhase.Type
    /// == MonsterClassesSelectAbilityCards</c> (ReadyButton.cs:500-501), and
    /// <c>OnClickInternal</c> early-returns on the same test even if a click got through
    /// (ReadyButton.cs:255-258); the Choreographer additionally passes <c>interactable:
    /// !FFSNetwork.IsClient</c> when it arms the button (Choreographer.cs:3717) and shows the client
    /// a "wait for host" tip instead (Choreographer.cs:3727). <see cref="CardsGameApi.CanConfirm"/>
    /// reads <c>ReadyButton.IsInteractable</c> ⇒ <c>readyButton.interactable</c>
    /// (ReadyButton.cs:87), so <c>canConfirm</c> is already false on every non-host client and the
    /// existing <c>show = canConfirm || confirmed</c> test hides the keycap there with no extra
    /// work. On the HOST it is true for every one of that host's characters — which is exactly the
    /// user's second sentence ("Im Multiplayer muss der host fortfahren drücken, daher soll das auch
    /// bei jedem Character des Hosts in der Phase angezeigt werden"). Duplicating the game's host
    /// test in the mod would only risk disagreeing with it.</para>
    ///
    /// <para>DEADLOCK ARGUMENT (the critical review point). The keycap is hidden only when
    /// <c>CharacterFocus.CanFocus(owner)</c> is true, i.e. the owner is a live player character AND
    /// <c>CharacterFocus.Open</c> holds. A click on that character's initiative-track portrait then
    /// runs <c>InitiativeTrackPlayerAvatar_OnClick_Guard</c> → <c>CharacterFocus.TryFocus(owner)</c>,
    /// which under exactly those two conditions CANNOT refuse (TryFocus rejects only a non-player,
    /// a dead actor, a closed <c>Refusal</c> gate, or a <c>FOCUS PIN</c> naming a different
    /// character — and <c>CanFocus</c> asserts every one of those four for this same actor, which is
    /// why the pin was added to it rather than to TryFocus alone). After it, <c>Focused == owner</c>, this method returns false on the very next
    /// tick, and the keycap materializes again with its unchanged label; a frame later
    /// <c>ResolveHand</c> additionally drops the override altogether ("the game now presents the
    /// focused character"), restoring full interactivity. The 2026-08-08 ruling that a character
    /// switch may NEVER be blocked is what makes this an absolute rather than a probable escape:
    /// the focus branch of the click guard returns false unconditionally once TryFocus takes, so
    /// no phase, prompt, modal or wait-state can stand between the player and that portrait. And
    /// the state is legible before they even look for the button — <c>Board.FocusCue</c> paints the
    /// board red while <c>LocalMark == AtTurnWrong</c>, i.e. precisely while you own the character
    /// at turn and are looking at another one. Belt: nothing here writes game state, so even a
    /// hidden keycap leaves the game's own <c>ReadyButton</c>/<c>UndoButton</c> fully armed and the
    /// 2D path (and every other commit affordance) untouched.</para>
    ///
    /// <para>…AND WHY THE ATTRIBUTION CLAUSE MAKES THAT ARGUMENT STRONGER RATHER THAN WEAKER. In the
    /// enemy-information step the interlock held only barely: <c>CharacterFocus.Refusal</c> refuses
    /// nothing but <c>SelectAbilityCardsOrLongRest</c>, so <c>recoverable</c> was TRUE there and the
    /// keycap really did hide (the user's report) — yet the game raises a full-track raycast blocker
    /// over that very reveal (<c>enemyCardsBlocker.raycastTarget = true</c>,
    /// InitiativeTrack.cs:747, cleared again only in <c>PostEnemyCardAnimationProceed</c>, :519) and
    /// deselects every player portrait one line earlier, and <c>InitiativeTrack.IsSelectable</c> is
    /// hard-false for the whole phase (InitiativeTrack.cs:114-127). The mod's own bypasses
    /// (<c>InitiativeTrackPlayerAvatar_OnClick_Guard</c>, <c>InteractabilityManager_PortraitFocus-
    /// Bypass</c>) route around the interceptor and vanilla's select, but a <c>Graphic</c> in front
    /// of the portraits is a plain uGUI raycast the laser has to get past. Falling open removes the
    /// question entirely: in the one phase where the recovery click is least certain, the keycap is
    /// never taken away in the first place.</para>
    ///
    /// <para>MULTIPLAYER (standing rule — a peer must see EXACTLY what the owner sees). The hide
    /// runs through <c>BoardButton.SetVisible(false)</c>, which clears <c>BoardButton._logicalVisible</c>
    /// (PlayTray.7.Nested.cs) — and <c>PlayTray.ConfirmControlShown</c>/<c>UndoControlShown</c>
    /// are defined as that very flag (their native-dock alternative is permanently false:
    /// <c>TrayControlDockSurface.ContinueDocked/ContinueVisible/UndoDocked</c> all return
    /// <c>false</c>). <c>NetAvatarDriver</c> reads them for <c>BoardUiConfirmBit</c>/
    /// <c>BoardUiUndoBit</c> and drops the <c>ExtIdCapLabels</c> confirm string when the cap is not
    /// shown, so a hidden keycap disappears on every peer's mirrored board through the records that
    /// already exist. NO wire change, no new field, no card identity — and, because the mirror is
    /// sampled from the same flag the local renderer obeys, the two cannot disagree.</para>
    ///
    /// <para>Cheap and silent: a reference compare plus two int reads per tick, and the log line is
    /// change-gated on an id tuple so no string is built unless the state actually moved.</para>
    /// </summary>
    private bool ConfirmCapsForeignView(CardsHandUI? hand)
    {
        CPlayerActor? owner = hand != null ? hand.PlayerActor : null;
        CPlayerActor? focused = Board.CharacterFocus.Focused;
        // The way back must EXIST before we take the button away (see the deadlock argument).
        bool recoverable = owner != null && Board.CharacterFocus.CanFocus(owner);
        // Is this confirm anybody's at all? Somebody at turn, or a decision flow that has CLAIMED
        // the presented hand — otherwise the press is a party-wide step advance and belongs to
        // every character (see "NOT ATTRIBUTABLE ⇒ EVERYBODY'S"). TurnActor is a single field read
        // and short-circuits the chain in the common case, so the walk only runs while nobody is
        // at turn; every entry of it is a phase compare or a window null check.
        bool attributable = Board.CharacterFocus.TurnActor != null
                            || CardsGameApi.DecidingHand() != null;
        bool foreign = focused != null && owner != null
                       && !ReferenceEquals(focused, owner)
                       && recoverable
                       && attributable;

        // Change-gate on the game's own actor ids (CActor.ID — the identity its network paths
        // send), so a per-frame resolve allocates nothing and the log only moves on a transition.
        int key = (foreign ? 1 : 2) * 31 + (owner != null ? owner.ID : 0);
        key = key * 31 + (focused != null ? focused.ID : 0);
        key = key * 31 + (recoverable ? 1 : 0);
        key = key * 31 + (attributable ? 1 : 0);
        if (key == _capOwnerLogKey)
            return foreign;
        _capOwnerLogKey = key;

        string ownerName = Board.CharacterFocus.Describe(owner);
        string focusName = Board.CharacterFocus.Describe(focused);
        if (foreign)
            VRLog.Info("Cards", $"Board: CONFIRM/UNDO keycaps HIDDEN — the confirm belongs to " +
                                $"'{ownerName}' (the hand the game presents; every press path " +
                                $"re-reads it) and the player is looking at '{focusName}' " +
                                "(focus override). Nothing in the game was touched: the " +
                                "ReadyButton/UndoButton stay armed, and one portrait click on " +
                                $"'{ownerName}' brings the keycaps straight back.");
        else
            VRLog.Info("Cards", "Board: CONFIRM/UNDO keycaps NOT hidden by the owner gate — " +
                                (owner == null
                                    ? "no presented hand this tick, so the normal state logic " +
                                      "decides (it already hides both without a hand)."
                                    : focused == null
                                        ? $"owner '{ownerName}' is in view (no focus override — " +
                                          "following the game)."
                                        : !recoverable
                                            ? $"owner '{ownerName}' cannot be focused right now " +
                                              "(exhausted / no live scenario / card selection), so " +
                                              "the keycaps stay reachable rather than strand the " +
                                              $"player while looking at '{focusName}'."
                                            : !attributable
                                                ? "the confirm is NOT ATTRIBUTABLE to any " +
                                                  "character — nobody is at turn " +
                                                  "(Choreographer.CurrentPlayerActor null) and no " +
                                                  "decision flow claims the presented hand, so the " +
                                                  "press is a PARTY-WIDE step advance and the " +
                                                  "keycaps show on EVERY character's board " +
                                                  $"(presented hand '{ownerName}', looking at " +
                                                  $"'{focusName}'). phase={PhaseManager.PhaseType}, " +
                                                  $"{CardsGameApi.DescribeConfirmGate()}"
                                                : $"owner '{ownerName}' is the character in view."));
        return foreign;
    }

    /// <summary>
    /// The CONFIRM keycap's label while THIS hand's player has already confirmed — i.e. the state
    /// in which pressing it REVOKES the ready status (<c>CardsGameApi.SetReady(false)</c> →
    /// <c>UIReadyToggle.ReadyUp(false)</c> → the game's <c>UnreadyPlayer</c> action).
    ///
    /// ROOT CAUSE OF THE REPORT (user, MP hardware test: "Im Multiplayer steht nach 'Auswahl
    /// beenden' → 'Mach dich bereit!' — das ist nicht klar, was der Knopf aussagt. Wenn man ihn
    /// drückt, kehrt man zurück zu 'Auswahl beenden', also eigentlich entfernt er doch wieder den
    /// Ready-Status."): this keycap used to show the GAME's <c>GUI_READY</c> string, which is DE
    /// "Mach dich bereit!" / EN "Get ready!". That string is vanilla's label for the OPPOSITE
    /// action — it is what the out-of-scenario lobby toggles show while you are NOT ready
    /// (<c>UIReadyToggle.Initialize</c>'s default <c>readyTextLoc</c>, used by the loadout,
    /// retirement and map screens). Vanilla's SCENARIO card-selection toggle never shows it at
    /// all: <c>UIScenarioMultiplayerController.InitializeReadyToggleForCardSelection</c> passes
    /// <c>GUI_END_SELECTION</c> for the not-ready state and <c>GUI_CANCEL</c> for the ready state,
    /// and <c>UIReadyToggle</c> picks between them at <c>_buttonText.SetTextKey(isOn ?
    /// unreadyTextLoc : readyTextLoc)</c>. So the keycap was showing a command to do the thing it
    /// actually undoes.
    ///
    /// WHY A MOD STRING RATHER THAN VANILLA'S <c>GUI_CANCEL</c>: on a tray keycap the label is the
    /// only context there is. Bare "Abbrechen"/"Cancel" next to a card selection reads as "abort
    /// the whole thing", and this keycap's OTHER state already says "Auswahl beenden" — so the
    /// pair is written as one toggle on the same noun: end the selection ⇄ change the selection.
    /// That names the effect (the press puts you back in front of your cards, and the keycap
    /// itself flips back to "Auswahl beenden"), keeps the game's short imperative-noun tone, and
    /// fits the keycap, which "Bereit-Status aufheben" does not. The "you ARE ready" readout is
    /// carried by the keycap's own gold CONFIRMED state (<c>SetState(confirmed: true)</c>) — the
    /// old "✓ " prefix never reached the player anyway: the tray font has no U+2713 and
    /// <c>PhysicalButton.SetLabel</c> strips it ("BUTTON LABEL: stripped un-renderable glyph(s)
    /// [U+2713]" in every hardware log), so it is gone from the string too.
    ///
    /// Loc key <c>confirm_unready</c> — probed through <see cref="Core.Loc.Mod"/> (which returns
    /// the id itself for a key that is not in the table yet) with an inline EN/DE fallback, the
    /// same shape <c>DecisionDockSurface.ItemFanOpenHint</c> uses. The peers' mirrored control
    /// caps show whatever this returns: the wording is shipped as text on the existing
    /// <c>ConfirmControlLabel</c> extension, so nothing new goes on the wire.
    /// </summary>
    private static string UnreadyLabel()
    {
        const string key = "confirm_unready";
        string localized = Core.Loc.Mod(key);
        if (!string.Equals(localized, key, System.StringComparison.Ordinal))
            return localized;
        return Core.Loc.CurrentLanguage == "German" ? "Auswahl ändern" : "Change selection";
    }

    // -------------------------------------------------- pick banner + keycap overrides --

    /// <summary>
    /// EVENT-DISCARD VR FLOW: driver-pushed pick status. <paramref name="banner"/> is the
    /// progress line shown on the hovering placard above the board's top edge (null hides
    /// it); <paramref name="confirmLabel"/>/<paramref name="undoLabel"/> override the
    /// CONFIRM/UNDO keycap labels while a pick confirm affordance is live (null returns
    /// each keycap to its normal game-state logic in <see cref="TickStatus"/>). The
    /// banner text write is change-gated (TMP rewrites re-trigger auto-size layout —
    /// the badge/round lesson, test #13); the label overrides are plain field stores
    /// consumed by the per-tick keycap logic.
    /// </summary>
    /// <summary>
    /// Is the pick confirm dialog's COMMIT option live on the board as a PHYSICAL keycap right now
    /// — the green "KARTEN ABWERFEN" cap in redundanter_knopf.jpg?
    ///
    /// <para>USER RULING 2026-08-24 (verbatim): "Wenn man alle 3 Karten abgelegt hat sieht man den
    /// 'Karten ablegen' Knopf sowie auch einen identischen Knopf unten in der Entscheidungsleiste.
    /// Der ist redundant und kann raus, da es ja als physischer Knopf existiert." The decision dock
    /// reads THIS — the keycap's own live state, never a re-derivation of it — before it stands its
    /// docked row down for the pick confirm, so the row can only ever disappear where the physical
    /// control has actually taken over. If the keycap is not offered (no tray, foreign-character
    /// view, no placement offered) this reads false and the dock presents exactly as before: a
    /// decision must never become unanswerable, which is the standing rule of that surface.</para>
    ///
    /// <para>Both terms are the ones <see cref="TickStatus"/> itself obeys: the driver's pick
    /// override is armed (<c>_pickConfirmLabel</c>, set only while
    /// <c>CardsGameApi.IsPickConfirmDialogOpen</c> — see <c>CardsDriver.UpdatePickStatus</c>) AND
    /// the cap ended the tick logically visible.</para>
    /// </summary>
    internal bool PickCommitCapOffered =>
        IsVisible && _pickConfirmLabel != null && _confirm != null && _confirm.LogicalVisible;

    internal void SetPickStatus(string? banner, string? confirmLabel, string? undoLabel)
    {
        _pickConfirmLabel = confirmLabel;
        _pickUndoLabel = undoLabel;
        if (banner == _pickBannerText)
            return;
        _pickBannerText = banner;
        if (banner == null)
        {
            if (_pickBannerRoot != null && _pickBannerRoot.activeSelf)
                _pickBannerRoot.SetActive(false);
            return;
        }
        EnsurePickBanner();
        if (_pickBannerLabel != null)
            _pickBannerLabel.text = banner;
        if (_pickBannerRoot != null && !_pickBannerRoot.activeSelf)
            _pickBannerRoot.SetActive(true);
        VRLog.Info("Cards", $"Pick banner: \"{banner}\".");
    }

    /// <summary>
    /// Build the pick progress placard lazily: a parchment strip hovering ABOVE the
    /// board's top/back edge (clear of the docked initiative track at y≈0.10 and the
    /// round readout), facing the player like every board element (_boardFaceFrame).
    /// Same collider-free plate+TMP construction as the round readout / EmptyFanHint —
    /// purely visual, mod layer only; child of the tray root so a board switch destroys
    /// and re-creates it with the board.
    /// </summary>
    private void EnsurePickBanner()
    {
        if (_pickBannerRoot != null || _root == null)
            return;

        _pickBannerRoot = new GameObject("PickStatusBanner");
        _pickBannerRoot.transform.SetParent(_root, worldPositionStays: false);
        _pickBannerRoot.transform.localPosition = PickBannerLocalPosition();
        _pickBannerRoot.transform.localRotation = _boardFaceFrame;
        Core.VRLayers.Apply(_pickBannerRoot);

        var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = "Plate";
        Object.Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(_pickBannerRoot.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(0.44f, 0.055f, 1f);
        plate.transform.localPosition = new Vector3(0f, 0f, 0.004f); // behind the text, toward the board
        Core.VRLayers.Apply(plate);
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.85f, 0.78f, 0.62f, 0.85f) }; // parchment
            plate.GetComponent<MeshRenderer>().sharedMaterial = mat;
            WorldUI.MrBacking.Opacify(mat); // 0.85 parchment lets the room shimmer through in MR
        }

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(_pickBannerRoot.transform, worldPositionStays: false);
        Core.VRLayers.Apply(textGo);
        _pickBannerLabel = textGo.AddComponent<TextMeshPro>();
        _pickBannerLabel.alignment = TextAlignmentOptions.Center;
        _pickBannerLabel.color = new Color(0.24f, 0.17f, 0.10f); // ink brown on parchment
        Core.TmpFit.Fit(_pickBannerLabel, 0.42f, 0.048f, maxFontSize: 0.30f, wrap: true);

        // The placard is the "Statustafel" of the 2026-08-04 report: a Sprites/Default quad +
        // plain TMP, both transparent and depth-less at sortingOrder 0 — any converted panel
        // (order >= 100 on the distance ladder) painted straight over it even when the panel
        // was BEHIND the board. Ride the board's furniture order group instead.
        AdoptFurniture(_pickBannerRoot);

        _pickBannerRoot.SetActive(false);
    }

    /// <summary>
    /// Where the pick placard sits, board-local: the historical spot just above the board's top
    /// edge PLUS the per-board <see cref="CardsConfig.PickBannerOffset"/> (user request
    /// 2026-08-03 — the default lands right under the docked initiative track, which is exactly
    /// the collision the user wants to be able to dial out). Static and public to the assembly so
    /// the MULTIPLAYER mirror can place a peer's placard by the same rule
    /// (<c>Net.RemotePickBanner</c>) instead of duplicating the arithmetic.
    /// </summary>
    internal static readonly Vector3 PickBannerBase = new(0f, BoardH * 0.5f + 0.10f, -0.02f);

    /// <summary>
    /// The TOOLTIP AREA's origin in board-local metres: the board's AUTHORED top-LEFT corner,
    /// slightly proud toward the viewer. This is the fixed reading spot every board-owned tooltip
    /// parks at (user request 2026-08-04: one unified "Tooltip"-Bereich, default top-left,
    /// per-board adjustable). The LOCAL presentation (<c>WorldUI.WorldTooltips</c>) refines the
    /// corner from the tray's MEASURED renderer extents (the visible frame overhangs the authored
    /// plate) — this constant is the authored-plate mirror of that spot, so the MULTIPLAYER board
    /// (<c>Net.RemoteBoardLayout.TooltipMount</c>) can seat a peer's tooltip by the same rule
    /// without a live measurement, exactly like <see cref="PickBannerBase"/> does for the placard.
    /// </summary>
    internal static readonly Vector3 TooltipAreaBase =
        new(-BoardW * 0.5f, BoardH * 0.5f, -0.02f);

    internal static Vector3 PickBannerLocalPosition() =>
        PickBannerBase + CardsConfig.PickBannerOffset(CardsConfig.CurrentBoard).Value;

    /// <summary>The placard line currently shown, or null while it is hidden. Read by
    /// <c>Net.NetAvatarDriver</c> to put it on the wire (extension record 7) so a peer's remote
    /// board carries the same line at the same seat — user request 2026-08-03 ("Dieser Text soll
    /// auch synchronisiert werden an der jeweiligen richtigen Position im MP").</summary>
    internal string? PickBannerText => _pickBannerRoot != null && _pickBannerRoot.activeSelf
        ? _pickBannerText
        : null;

    /// <summary>Live-apply for a debug-menu / hand edit of the placard offset (CardsDriver Part F).
    /// No rebuild: the placard is a plain child transform, so moving it is a single write.</summary>
    internal void SetPickBannerOffset()
    {
        if (_pickBannerRoot != null)
            _pickBannerRoot.transform.localPosition = PickBannerLocalPosition();
    }
}
