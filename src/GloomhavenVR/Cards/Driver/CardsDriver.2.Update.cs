using System.Collections.Generic;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

// CardsDriver part 2 of 6 (see CardsDriver.1.Core.cs for the split map and its rules).
// Regions: board pose guard (issue C), handlers, update, per-frame tick attribution guard,
// fan diagnostics, card audio.
//
// ORDER THAT LEAVES THIS FILE: TickInteractionsAndStatus calls the laser paths BEFORE
// UpdateHandContactArbitration, which lives in part 3. Arbitration reads what the laser paths
// published this frame; reordering the tick calls "since they are all independent" is the
// documented way to break it (INVARIANTS-Cards §3).
// ORDER THAT STAYS IN THIS FILE: TickBoardPoseWatch runs LAST in Update.

internal sealed partial class CardsDriver
{
    // ------------------------------------------------------------ board pose guard (issue C) --

    // The board's last-known pose, tracked in PARENT-LOCAL space (world pose changes caused
    // by the rig/anchor moving — recenter, diorama scale — are NOT board moves and must not
    // trip the watchdog). Doubles as the carry-over pose for a tray rebuilt outside the
    // board-switch path (TryRestoreCarriedPose).
    private Transform? _watchRoot;
    private Transform? _watchParent;
    private Vector3 _watchLocalPos;
    private Quaternion _watchLocalRot = Quaternion.identity;
    private Vector3 _watchScale = Vector3.one;
    private bool _watchValid;
    private bool _watchGrabbed;
    private WorldUI.PanelGrabHandle? _watchHandle;

    /// <summary>Set by sanctioned re-pose paths right before they move the board; consumed
    /// (and cleared) by <see cref="TickBoardPoseWatch"/> the same frame. A pose change with
    /// no expected trigger pending logs a Warn — the "no silent recompute remains" proof.</summary>
    private string? _expectedPoseChange;

    /// <summary>
    /// Announce a sanctioned board re-pose from OUTSIDE this class — currently
    /// <see cref="PlayTray"/>'s apparent-size clamp, which legitimately writes the tray's scale and
    /// would otherwise be reported as an UNSANCTIONED recompute. Valid for exactly one frame, like
    /// every other sanctioned trigger; a no-op when no driver is up.
    /// </summary>
    internal static void NoteExpectedPoseChange(string what)
    {
        CardsDriver? driver = Instance;
        if (driver != null)
            driver._expectedPoseChange = what;
    }

    /// <summary>User escape hatch pending: a "Board zurückholen" request came in. Per the P2
    /// threading rule the caller only sets the flag; <see cref="Update"/> performs the re-home on
    /// the main thread with a reliably valid head pose.</summary>
    private bool _recallBoard;

    /// <summary>True once the "no hand anchor while in a scenario" state has been logged, so the
    /// per-frame path reports the board going away exactly once per occurrence.</summary>
    private bool _anchorLossLogged;

    /// <summary>
    /// USER ESCAPE HATCH "Board zurückholen": bring the control board back in front of the player
    /// NOW, whatever mode it is in and without waiting for the watchdog's dwell timer.
    /// Deliberately a static request rather than a direct call — a settings-panel action runs off a
    /// UI callback and the board pose may only be written from the driver's Update (P2 threading
    /// rule), which also sanctions the move for the issue-C pose watchdog. No-op with no live
    /// driver (no scenario / hands down).
    ///
    /// <para><b>WIRED SINCE 2026-09-06</b>, and the two claims this paragraph used to make were
    /// both false by then. It said "NOT WIRED TO A BUTTON YET" and it said "the options tab exposes
    /// config DIALS, not actions" — but <c>VROptionsTab</c> has carried ACTION rows since the
    /// combat-log spawn button (<c>CuratedEntry.Press</c>, <c>VROptionsTab.4.Curated.cs</c>), so
    /// nothing was ever blocking. The caller is now the first row of <b>Brett &amp; Karten ▸
    /// Steuerbrett</b>, above the board's own dials: that is the page a player opens when the
    /// control board is the problem, and it is reached from the PAUSE MENU (<c>VRMenuEntry</c>),
    /// which is a controller button and not a thing on the board — so the recovery is reachable in
    /// exactly the state it exists for, a board that cannot be found.</para>
    ///
    /// <para>The <c>.planning/refactor/MENU-STRUCTURE.md</c> row this used to cite ("Komfort ▸ Neu
    /// zentrieren · Board zurückholen") is a proposal against <c>SettingsPanel.*</c>, a menu that no
    /// longer exists, and its "Neu zentrieren" half was NOT built: recentring is a controller chord
    /// (<c>VRRigDriver.RequestRecenter</c>), not a menu row, so there was no neighbour to sit
    /// beside.</para>
    ///
    /// <para><b>MULTIPLAYER: THIS IS NOT CLIENT-LOCAL</b>, checked on the send path rather than
    /// assumed. <c>NetAvatarDriver</c> samples the live board transform every packet
    /// (<c>extras.Board.Position/Rotation</c> + <c>BoardScale</c>) and <c>RemoteControlBoard</c>
    /// seats each peer's mirror at exactly that world pose, so a recall MOVES THE BOARD FOR
    /// EVERYONE, the same way a hand grab does. That is the 1:1 ruling working, not a leak: it
    /// needs no wire field, no new record and no gate, because the pose is already on the wire.
    /// </para>
    /// </summary>
    internal static void RequestBoardRecall()
    {
        CardsDriver? driver = Instance;
        if (driver == null)
        {
            // A PRESS THAT LEAVES NO TRACE IS THE ONE THING THIS LOG MAY NOT DO
            // (the "all off" row of VROptionsTab.9.TestTriggers.cs, verbatim, and it applies here
            // for the same reason): the row is reachable from the MAIN MENU, where there is no
            // driver and the
            // press is correctly inert. Without this line "I pressed it and nothing happened" and
            // "the button is broken" produce the same empty log.
            // HW-VERIFY
            VRLog.Note("Cards", "BOARD RECALL PRESSED — NO LIVE DRIVER (no scenario, or the hands "
                                + "are down), so the press is a NO-OP: no pose is written and "
                                + "nothing goes on the wire. This is the CORRECT reading in the "
                                + "main menu; the same line during a scenario is the defect.");
            return;
        }

        // Read-only, all of it — the pose write happens in Update (P2 threading rule), and an
        // instrument that moved the board would be measuring itself.
        Transform? root = driver._tray.Root;
        Vector3 seat = CardsConfig.TrayOffset
                       + CardsConfig.BoardPosOffset(CardsConfig.CurrentBoard).Value;
        // HW-VERIFY
        VRLog.Note("Cards", "BOARD RECALL PRESSED — a live driver took it; the re-home runs in the "
                            + "next Update. Board was at "
                            + (root != null ? root.position.ToString("F2") : "NO ROOT (no board built yet)")
                            + $", mode {(CardsConfig.TrayFollow.Value ? "FOLGEN" : "FIXIERT")}. It will be "
                            + $"re-seated at the saved head-relative layout ({seat.x:F2} m right, "
                            + $"{-seat.y:F2} m down, {seat.z:F2} m forward) — TrayOffset plus this "
                            + "board's BoardPosOffset, i.e. what PlaceAtHead uses for every seat "
                            + "except the very first of a session — in front of the "
                            + "player; the resolved world pose is on the 'CONTROL BOARD RECOVERED' "
                            + "line of the SAME frame, and its absence there means the request was "
                            + "swallowed between here and Update.");
        driver._recallBoard = true;
    }

    /// <summary>
    /// ISSUE C watchdog: track the board root's parent-local pose every frame and log each
    /// change with its trigger:
    /// - <c>initial</c> — the first placement of a freshly built board;
    /// - <c>rebuild-restored (…)</c> — a rebuild/board-switch/session-resume that restored
    ///   the previous pose;
    /// - <c>user-grab</c> — the player moved/resized the tray by its grab bar (tracked
    ///   silently while gripped, one summary line on release);
    /// - <c>user-settings (…)</c> — orientation/scale tuning from the settings panel;
    /// - anything else — <c>Warn UNSANCTIONED</c>, i.e. a game event moved the board.
    /// Allocation-free in steady state; hidden boards (deferred placement, hands down) are
    /// not watched — SetVisible only shows a placed board, so the first visible frame IS
    /// the placement.
    /// </summary>
    private void TickBoardPoseWatch()
    {
        Transform? root = _tray.Root;
        if (root == null)
        {
            // Root gone (board switch teardown / shutdown): keep the last baseline — it is
            // the carry-over pose for TryRestoreCarriedPose — but drop the per-root refs.
            _watchRoot = null;
            _watchHandle = null;
            _watchGrabbed = false;
            return;
        }
        if (!_tray.IsVisible)
        {
            _watchGrabbed = false;
            return; // hidden = not (yet) placed or parked away — nothing to prove
        }

        if (!ReferenceEquals(_watchRoot, root))
        {
            // Fresh (or rebuilt) board root just became visible: this IS a placement.
            _watchRoot = root;
            _watchHandle = root.GetComponentInChildren<WorldUI.PanelGrabHandle>(true);
            Baseline(root);
            LogBoardPose(_expectedPoseChange ?? "initial", root);
            _expectedPoseChange = null;
            return;
        }

        bool grabbed = _watchHandle != null && _watchHandle.IsGrabbed;
        if (grabbed)
        {
            _watchGrabbed = true;
            Baseline(root); // user is moving it — follow silently, summarize on release
            _expectedPoseChange = null;
            return;
        }
        if (_watchGrabbed)
        {
            _watchGrabbed = false;
            Baseline(root);
            LogBoardPose("user-grab", root);
            _expectedPoseChange = null;
            return;
        }

        // Pin/follow toggles re-parent with worldPositionStays — re-baseline the local pose
        // silently (the world pose did not move, so it is not a placement).
        if (!ReferenceEquals(root.parent, _watchParent))
        {
            Baseline(root);
            _expectedPoseChange = null;
            return;
        }

        bool moved =
            (root.localPosition - _watchLocalPos).sqrMagnitude > 0.005f * 0.005f
            || Quaternion.Angle(root.localRotation, _watchLocalRot) > 0.5f
            || Mathf.Abs(root.localScale.x - _watchScale.x) > 0.005f * Mathf.Max(_watchScale.x, 0.01f);
        if (moved)
        {
            string? trigger = _expectedPoseChange;
            Baseline(root);
            if (trigger != null)
            {
                LogBoardPose(trigger, root);
            }
            else
            {
                VRLog.Warn("Cards", "Board pose changed WITHOUT a sanctioned trigger (UNSANCTIONED " +
                                    "recompute — this must never happen; report this log). " +
                                    DescribeBoardPose(root));
            }
        }
        _expectedPoseChange = null; // expected triggers are valid for exactly one frame
    }

    private void Baseline(Transform root)
    {
        _watchParent = root.parent;
        _watchLocalPos = root.localPosition;
        _watchLocalRot = root.localRotation;
        _watchScale = root.localScale;
        _watchValid = true;
    }

    private void LogBoardPose(string trigger, Transform root) =>
        VRLog.Info("Cards", $"Board pose [{trigger}]: {DescribeBoardPose(root)}");

    private static string DescribeBoardPose(Transform root) =>
        $"world pos {root.position}, yaw {root.eulerAngles.y:F0}°, scale {root.localScale.x:F2}×.";

    /// <summary>
    /// ISSUE C: session-resume re-assert that KEEPS the board pose. The user requirement is
    /// absolute — fixed or follow mode, the board never moves without explicit user action —
    /// so an HMD doff/don only re-shows the board where it already is. The single exception
    /// is a genuinely LOST pose (non-finite, stranded far beyond reach, or fallen below the
    /// floor — a frozen/zeroed head pose during the presence loss can produce these), where
    /// staying put would leave the board unusable: only then is a recovery re-seat allowed.
    /// Mirrors PlayTray.ReassertPlacement's PINNED "lost" criteria, applied to BOTH modes.
    /// </summary>
    private void ReassertBoardKeepingPose()
    {
        if (_tray.Root == null)
            return;

        bool hadPose = _tray.TryCapturePose(out Vector3 pos, out Quaternion rot, out Vector3 scale);
        bool lost = true;
        Camera? head = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
        if (hadPose && head != null)
        {
            Transform root = _tray.Root!;
            float s = root.parent != null ? root.parent.lossyScale.x : 1f;
            Vector3 delta = pos - head.transform.position;
            var horizontal = new Vector3(delta.x, 0f, delta.z);
            bool finite = !(float.IsNaN(pos.x) || float.IsInfinity(pos.x)
                            || float.IsNaN(pos.y) || float.IsInfinity(pos.y)
                            || float.IsNaN(pos.z) || float.IsInfinity(pos.z));
            // NON-FINITE ONLY (user ruling 2026-08-03): the distance/below-the-floor criteria
            // that used to sit here were a second automatic mover — a pinned board across the
            // room is exactly what pinning it produces. See PlayTray.2.Watchdog.cs.
            lost = !finite;
            _ = horizontal; // kept for the diagnostic below, no longer a verdict
            _ = s;
        }

        if (lost)
        {
            // Recovery only: the pose is unusable — PlayTray's re-assert may re-seat near
            // the head (FOLLOW) / snap back (PINNED). Sanctioned, and says so in the log.
            _expectedPoseChange = "resume-recovery (pose was lost)";
            _tray.ReassertPlacement("session resume — pose lost");
            return;
        }

        // Pose is sane: restore it verbatim (marks the tray placed + re-pins as needed) and
        // let the queued Rebuild re-assert visibility — shown again, exactly where it was.
        _expectedPoseChange = "rebuild-restored (session resume)";
        _tray.RestorePose(pos, rot, scale);
        VRLog.Info("Cards", "Session resume: control board pose PRESERVED (no re-seat — the board " +
                            "never moves without explicit user action).");
    }

    /// <summary>
    /// ISSUE C: carry the board pose over a tray rebuild that is NOT a board switch (the
    /// switch path captures its own pose in <see cref="RebuildBoard"/>). If the factory had
    /// to re-create the tray root while the watchdog still holds a valid parent-local pose
    /// under the SAME parent, re-apply it so the new root spawns exactly where the old one
    /// stood instead of re-placing at the head. Returns true when a pose was restored.
    /// </summary>
    private bool TryRestoreCarriedPose()
    {
        Transform? root = _tray.Root;
        if (!_watchValid || root == null || _watchParent == null
            || !ReferenceEquals(root.parent, _watchParent))
            return false;
        Vector3 pos = _watchParent.TransformPoint(_watchLocalPos);
        Quaternion rot = _watchParent.rotation * _watchLocalRot;
        _expectedPoseChange = "rebuild-restored (carried pose)";
        _tray.RestorePose(pos, rot, _watchScale);
        return true;
    }

    private void OnDestroy()
    {
        ClearLaserHover();
        ClearBoardHover();
        ClearBrowseHover();
        ClearItemFanHover();
        ClearActiveHover();
        ClearInitiativeTodo(); // item 6: clear any lingering initiative to-do glow on teardown
        _liveGrabs.Clear();
        _flyingToPile.Clear(); // issue 5: no fly survives a driver teardown
        _lastHalfCards.Clear();
        _lastTrayCards.Clear(); // issue 1
        _lastVisibleCards.Clear();
        _lastCardWorldPos.Clear(); // issue 1
        _lastCardWorldRot.Clear();
        _burnWatchHand = null; // issue B
        _knownBurntWidgets.Clear();
        _burnHoldSince.Clear(); // artwork holds die with the driver — no orphaned release later
        _burnHoldLogged.Clear();
        CardFlightLedger.Reset(); // one scenario's flight ordinals never accuse the next one's first
        _loggedStaleHandCard.Clear(); // item 10 model-belt dedupe dies with the driver
        _fanOriginCards.Clear();
        _fanOrder.Clear();
        _insertGap = -1;
        _insertHighlightCard = null;
        VRCard.InteractionBlockedHand = null;
        VRCard.HandArbitrationHand = null;
        _handContactWinner = null;
        _gateContactWinner = null; // gate-hand dock election dies with the driver too
        _contactSuppressed.Clear(); // flags themselves die with the cards (OnDisable clears)
        _emptyFanHint.Destroy(); // task #9: ghost placard teardown
        // The deferred face restores are moot from here: _factory.Dispose() below restores EVERY
        // adopted face, which is a superset of what this list names. Dropping it explicitly so the
        // queue cannot outlive the driver that drains it and hand a dead CardsHandUI to a rebuilt one.
        _pendingFaceRestore.Clear();
        _fan.Destroy();
        _browser.Destroy();
        _half.Destroy();
        _rest.Destroy();
        _active.Destroy(); // before the tray — the column lives under its ActiveMount
        _piles.Destroy(); // before the tray — the stacks live under its PileMount
        _tray.Destroy();
        _factory.Dispose(); // restores every adopted face
        CardActionQueue.Clear();
    }

    // ------------------------------------------------------------------ handlers --

    private void OnModeChanged(VRModeChange change)
    {
        _dirty = true;
        // ISSUE C (the board must NEVER jump): the control board is deliberately NOT
        // re-anchored on ANY mode change any more. The previous policy re-seated FOLLOW
        // boards on "genuinely new decision points" (To == CardSelection/HalfSelection
        // arriving from TableIdle/Menu2D/CardSelection) — exactly the transition every
        // turn CONFIRM produces. The hardware log caught it five times in one session
        // ("[Cards] Control board placed" after each "CONFIRM → ReadyButton clicked"):
        // PlaceAtHead recomputes the pose from the CURRENT head yaw/position, so the
        // board visibly jumped whenever the player had turned or moved since the last
        // placement (log: yaw -9° → 42° across one confirm). Policy now: the FIRST
        // placement (or an explicit user action — tray grab, board switch restore,
        // settings orientation tuning, lost-pose recovery after an HMD doff/don)
        // computes a pose; a game event never does. The item-8/item-4 modal round-trip
        // laundering detection that used to guard this block is obsolete with it.
        // The floating half-selection layout keeps its per-turn re-anchor — it is
        // head-relative ephemera (re-docked/re-shown per decision), not the persistent
        // board the user manually places.
        bool modalRoundTrip = change.From == VRMode.TableIdle
                              && _prevFrom == VRMode.ModalUI && _prevTo == VRMode.TableIdle;
        if ((change.To == VRMode.CardSelection || change.To == VRMode.HalfSelection)
            && change.From != VRMode.BoardTargeting && change.From != VRMode.ModalUI
            && !modalRoundTrip)
        {
            _half.InvalidatePlacement();
        }
        _prevFrom = change.From;
        _prevTo = change.To;
        // A dialog owns the scene (test #21 C): an open pile browse would float
        // behind/through it — close, the stacks stay for re-opening afterwards.
        if (change.To == VRMode.ModalUI)
            CloseBrowser("modal dialog opened");
    }

    private void OnHandShown(HandShownEvent e) => _dirty = true;

    /// <summary>
    /// Item 3: the HMD was doffed and re-donned (or the runtime resumed) — VRPresenceWatch raised
    /// SessionResumed. On an OpenXR presence loss the head pose can freeze/zero, and on re-wear the
    /// board could otherwise sit hidden or stranded far away. Flag a re-assert for the next Update
    /// (the head pose is reliably valid there) and force a rebuild in case the hands/board were torn
    /// down while doffed. The actual re-placement runs on the main thread in <see cref="Update"/>
    /// (<see cref="PlayTray.ReassertPlacement"/>), per the P2 threading rule.
    /// </summary>
    private void OnSessionResumed(SessionResumedEvent e)
    {
        _reassertTray = true;
        _dirty = true;
        e.Recovered.Add("control board placement re-asserted near the head");
    }

    private void OnCardSelectionChanged(CardSelectionEvent e) => _dirty = true;

    private void OnHandsChanged() => _dirty = true;

    /// <summary>
    /// Live control-board switch ([Cards] Board, from the VR settings panel). The tray
    /// early-returns in <see cref="PlayTray.EnsureBuilt"/> while its root exists, so a
    /// mere <see cref="_dirty"/> rebuild would keep the old board — the switch needs a
    /// full teardown first. Per the P2 threading rule handlers only set a flag; the
    /// teardown + rebuild runs on the main thread in <see cref="Update"/>
    /// (<see cref="RebuildBoard"/>), which also re-parks the seated cards so they survive
    /// the tray's DestroyImmediate.
    /// </summary>
    private void OnBoardChanged(object sender, System.EventArgs e)
    {
        _boardChanged = true;
        VRLog.Info("Cards", $"[Cards] Control board switched to '{CardsConfig.Board.Value}' — " +
                            "tearing down and rebuilding the tray.");
    }

    private void OnHandDestroying(CardsHandUI hand)
    {
        // Pool safety: give every adopted face back BEFORE the game recycles.
        List<AbilityCardUI> cards = hand.cardsUI;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null)
                continue;
            VRCard? card = _factory.Find(cards[i]);
            if (card != null)
                _half.DestroyZonesFor(card);
        }
        _factory.ReleaseHand(hand);
        _tray.ClearSlots();
        // Hand reorder: the hand's cards died — drop the persisted VR order + any pending reorder
        // (session-only; ids don't survive a hand teardown).
        _fanOrder.Clear();
        _fanOriginCards.Clear();
        ClearFanInsertion();
        _fieldCards.Clear(); // the hand's VRCards just died — no dead refs on the field
        _pickExitFlown.Clear(); // …and with them every claim on a pick-exit flight
        _pickReturnFlight.Clear(); // …and the restart's return flight, which names the same cards
        _pickReturnSettleAt = 0f;  // …and its landing deadline, which would rebuild for nobody
        _flyingToPile.Clear(); // issue 5: the hand's cards (any mid-flight) just died
        _lastHalfCards.Clear();
        _lastTrayCards.Clear(); // issue 1: slot occupants die with the hand's cards
        _lastVisibleCards.Clear();
        _lastFieldCards.Clear(); // event-discard exit: the same rule for the pick-recess snapshot —
                                 // a dead VRCard is still a live reference in a HashSet, and the
                                 // pick-field pre-filter is a membership test on exactly this set
        _lastCardWorldPos.Clear(); // issue 1: last-known poses die with the hand's cards
        _lastCardWorldRot.Clear();
        _dockAnimSuppressed = true; // issue 2: the next hand's cards populate silently (no storm)
        _burnWatchHand = null; // issue B: re-baseline the burnt set for the next hand
        _knownBurntWidgets.Clear();
        _burnHoldSince.Clear(); // the held widgets died with the hand — never release into a slab
        _burnHoldLogged.Clear();
        _loggedStaleHandCard.Clear(); // ditto the item 10 model-belt dedupe (widgets are recycled)
        _shortRestCard = null; // ditto the sacrifice display (item 1d, reversibility)
        _shortRestPresented = null;
        if (_browseHand == hand)
            CloseBrowser("hand destroyed");
        if (_boundHand == hand)
            _boundHand = null;
        _dirty = true;
    }

    private void OnCardRecycling(AbilityCardUI widget)
    {
        VRCard? card = _factory.Find(widget);
        if (card != null)
        {
            // Issue 1: capture the card's true world pose BEFORE it is destroyed, so a damage-burn
            // that lands this widget in the burnt pile can still fly a slab from where it really was.
            if (card.GameCard != null && card.gameObject.activeInHierarchy)
            {
                _lastCardWorldPos[widget] = card.transform.position;
                _lastCardWorldRot[widget] = card.transform.rotation;
            }
            if (ReferenceEquals(card, _laserHover))
                ClearLaserHover();
            if (ReferenceEquals(card, _browseHover))
                ClearBrowseHover();
            if (ReferenceEquals(card, _activeHover))
                ClearActiveHover();
            _half.DestroyZonesFor(card);
            _fan.Remove(card);
            _browser.Remove(card);
            _active.Remove(card); // feature 6: drop from the active grid if the widget recycled
            ClearActiveHighlight(card);
            if (_fieldCards.Remove(card))
            {
                _pickExitFlown.Remove(card);
                RelayoutField();
            }
            if (ReferenceEquals(card, _shortRestCard)) // sacrifice widget recycled under us
            {
                _shortRestCard = null;
                _shortRestPresented = null;
            }
            _tray.RemoveCard(card);
            _pickReturnFlight.Remove(card); // a dead card cannot fly back out of the discard stack
            _flyingToPile.Remove(card); // recycled mid-flight: drop the stale fly ref (card dies)
            _lastHalfCards.Remove(card);
            _lastTrayCards.Remove(card); // issue 1: no dead refs across a recycle
            _lastVisibleCards.Remove(card);
            _liveGrabs.Remove(card); // recycled mid-grab: its release must not route a drop
            if (_fanOriginCards.Remove(card) && ReferenceEquals(_insertHighlightCard, card))
                ClearFanInsertion(); // reorder subject recycled under us
        }
        _factory.ReleaseWidget(widget);
        _dirty = true;
    }

    // ------------------------------------------------------------------ update --

    /// <summary>
    /// Perf attribution wrapper (2026-07 perf pass): the cards driver owns the card fan, the tray,
    /// the piles and every board widget, i.e. the busiest per-frame block in the mod that was NOT
    /// routed through TickGuard (it carries its own bespoke throw guard around the interaction
    /// tail). Measuring it here — with a scope that does NOT alter exception flow — is what lets
    /// the [Perf] STEPS line say whether a head-turn spike lives in the cards or somewhere else.
    /// </summary>
    private void Update()
    {
        using (Core.PerfMonitor.Scope("Cards.Driver"))
            UpdateBody();
    }

    private void UpdateBody()
    {
        CardActionQueue.Pump();
        HandSuppression.Tick();
        // Card-art warm-up + its falsifier. DELIBERATELY ABOVE the hands-down bail-out below: the
        // whole point is to load a hand's card art while nothing of it is on screen, and "the hands
        // are down" is exactly such a moment. Costs two Count == 0 tests when there is nothing
        // queued — it is edge-fed by CardFace.Adopt, it never scans anything. See CardArtPrewarm.
        CardArtPrewarm.Pump();
        // Character-swap exchange tail: park each outgoing card once its own flight has landed, and
        // give a deferred focus hand its borrowed faces back once the wave has drained. FIRST in the
        // update, ahead of the anchor bail-out, because a borrowed hand must be handed back even on
        // the frames this method does nothing else — the hands going down mid-exchange is exactly
        // such a frame, and it would otherwise leave a character's 2D hand adopted indefinitely. The
        // cost of being here rather than after the fan's tick is that a card is parked one frame
        // after it stops moving, at the gather point, shrunk and off the end of the arc.
        DrainSwapExit();

        Transform? anchor = AnchorParent();
        if (anchor == null)
        {
            // Hands (and rig) are down — nothing physical can exist.
            if (_fan.IsOpen)
                _fan.Close();
            ClearFanInsertion();
            CloseBrowser("hands down");
            ClearLaserHover();
            ClearBoardHover();
            ClearBrowseHover();
            ClearItemFanHover();
            ClearActiveHover();
            ClearInitiativeTodo(); // item 6: drop the initiative to-do glow while hands are down
            // "THE BOARD IS GONE" DIAGNOSTIC. The control board root is a CHILD of the hands root
            // (AnchorParent), so whenever the hands go away the board goes with it — hidden here,
            // and outright destroyed when HandsDriver tears the hands root down (that happens on a
            // rig teardown: anchor camera destroyed/disabled, scene load, rig kind change). To the
            // player that is indistinguishable from the board "just vanishing", and the watchdog
            // below cannot help because there is no root left to inspect. Losing the anchor DURING
            // a scenario is therefore always worth one loud line, so the next hardware log can tell
            // "board gone because the hands/rig went" apart from "board gone because it drifted".
            if (!_anchorLossLogged && CardsGameApi.InScenario)
            {
                _anchorLossLogged = true;
                VRLog.Warn("Cards", "CONTROL BOARD HIDDEN — the hand/rig anchor disappeared mid-scenario " +
                                    "(VRHands are down, i.e. the rig root went away). The board lives under " +
                                    "that anchor, so it is hidden (and destroyed if the hands root was torn " +
                                    "down); it re-builds and re-places in front of the player as soon as the " +
                                    "hands return. If the board was reported missing around this timestamp, " +
                                    "THIS is the cause, not a pose drift.");
            }
            _tray.SetVisible(false);
            _half.SetVisible(false);
            return;
        }

        if (_anchorLossLogged)
        {
            _anchorLossLogged = false;
            VRLog.Info("Cards", "Control board anchor restored (hands are back) — the board rebuilds and " +
                                "re-places in front of the player this frame.");
        }

        if (_boardChanged)
        {
            _boardChanged = false;
            RebuildBoard();
        }

        if (_dirty)
        {
            _dirty = false;
            Rebuild(anchor);
        }

        // Deferred initial placement (test #17): retries until the head has a
        // real tracked pose — the tray stays hidden meanwhile.
        _tray.TickPlacement();

        // Item 3: a presence regain (HMD re-donned) re-asserts the board placement so it is never
        // gone or stranded far after taking the headset off and back on. Runs here on the main
        // thread with a reliably-valid head pose (the handler only set the flag).
        // ISSUE C: the re-assert now PRESERVES the board's pose unless it is genuinely lost —
        // see ReassertBoardKeepingPose (PlayTray.ReassertPlacement would re-seat a FOLLOW board
        // at the head, which is a silent move without user action).
        if (_reassertTray)
        {
            _reassertTray = false;
            ReassertBoardKeepingPose();
        }

        // LOST-BOARD WATCHDOG (incident: "the control board was gone after walking around the
        // room and briefly taking the headset off"). Unlike the presence-regain path above this
        // is UNCONDITIONAL and per-frame — the incident log proves the doff/don produced no
        // presence edge and no XR session state change at all (SteamVR/OpenXR stayed FOCUSED
        // throughout), so an event-driven recovery had nothing to react to. Rationale, envelope
        // and the pin-holder housekeeping it also performs: PlayTray.TickLostWatchdog.
        bool lost = _tray.TickLostWatchdog(out string lostWhy);
        // The watchdog's pin housekeeping may legitimately have re-posed the board (holder rescale /
        // tracking-origin carry) — sanction it so the issue-C pose watch reports it instead of
        // Warning about a silent recompute.
        string? pinMove = _tray.ConsumePinHousekeepingMove();
        if (pinMove != null)
            _expectedPoseChange = pinMove;
        if (lost)
        {
            _expectedPoseChange = "lost-board recovery (watchdog)"; // sanctioned move (issue C watchdog)
            _tray.RecoverLostBoard(lostWhy);
        }

        // User escape hatch "Board zurückholen": an explicit, always available "bring it back"
        // that does not wait for the watchdog dwell timer. The caller is the first row of
        // Brett & Karten ▸ Steuerbrett — see RequestBoardRecall for why the menu is the surface.
        if (_recallBoard)
        {
            _recallBoard = false;
            _expectedPoseChange = "user-recall (settings button)"; // sanctioned move (issue C watchdog)
            _tray.RecoverLostBoard("USER REQUESTED the board back (settings → recall)");
        }

        // Debug-menu / hand-edited per-board tuning live-applies here (Part F).
        ApplyBoardTuning();

        // ISSUE C proof instrument: every board pose change is logged with its trigger
        // (initial / rebuild-restored / user-grab / user-settings) — an UNSANCTIONED
        // recompute logs a Warn, so the next hardware log can prove none remain.
        TickBoardPoseWatch();

        // GLOBAL hand-fan geometry live-apply ("Fan" debug category): re-lay the open fan at the
        // new step/arc/radius/hover-split. Independent of the control board (fan is not tray-bound),
        // so it runs even with no tray. The relayout re-reads the config, so grab/hover geometry
        // (which derives from the SAME radius/step) stays aligned with the new width.
        if (_applyFan)
        {
            _applyFan = false;
            _fan.ApplyLayout();
            VRLog.Info("Cards", $"Debug live-apply [Fan]: step {CardsConfig.FanPerCardStepDegrees.Value:F0}°, " +
                                $"arc {CardsConfig.FanArcSweepDegrees.Value:F0}°, radius {CardsConfig.FanEffectiveRadius.Value:F3} m, " +
                                $"split ×{CardsConfig.FanHoverSplitScale.Value:F2}, " +
                                $"faceViewer {CardsConfig.FanFaceViewer.Value * 100f:F0}%, " +
                                $"gazeFollow {CardsConfig.FanGazeApexFollow.Value * 100f:F0}%.");
        }

        // Post-rebuild per-frame interaction + status path (hover / laser / fingertip /
        // slot / initiative ticks). ISOLATED + ATTRIBUTED, mirroring WorldUIModule.TickGuard:
        // Unity logs an unhandled MonoBehaviour.Update exception to Player.log as an ANONYMOUS
        // per-frame "NullReferenceException" with NO stack in this Player build — a flood that
        // is impossible to attribute to a subsystem. Wrapping this path catches the FIRST throw
        // WITH its stack + a [Cards] tag, throttles repeats to one line / 10 s, and never lets a
        // single throwing tick starve the Rebuild / CardActionQueue path above (the reopen
        // guarantee). NOT a claimed root-cause fix: the change-deduped "fan state" line at the
        // end of this path keeps logging all session in the reference log, which proves this
        // path already runs to completion every frame — so this guard is the attribution net the
        // NEXT hardware run uses to CONFIRM or EXONERATE Cards as the flood's source (a silent
        // guard exonerates Cards and redirects the hunt to the other raw MonoBehaviour.Updates).
        try
        {
            TickInteractionsAndStatus();
        }
        catch (System.Exception ex)
        {
            NoteTickThrow(ex);
        }
    }

    /// <summary>
    /// The per-frame interaction + status tick path, split out of <see cref="Update"/> so the
    /// whole path can be isolated by the attribution guard there. Contains no top-level early
    /// return — every branch falls through to <see cref="LogFanState"/>.
    /// </summary>
    private void TickInteractionsAndStatus()
    {
        // FRAME-ORDER CardsDriver.TickInteractionsAndStatus [UpdateHeldCardTransfer, UpdatePalmGate, UpdateLaserContactStandDown, UpdateFanLaser, UpdateBoardFanHandTrigger, UpdateBoardLaser, UpdateBrowseLaser, UpdateItemFanLaser, UpdateActiveLaser, UpdateHandContactArbitration, UpdateFanHoverSplit, UpdateOverlayGate, UpdateSlotHighlight, UpdateFanInsertion]
        //   FOUR of these adjacencies are stated in prose and, until this marker, checked by
        //   nothing. One of them was explicitly named as unguarded by the refactor that created
        //   this file: CardsDriver.1.Core.cs says "SPLITTING A FILE DOES NOT SPLIT THE CALL ORDER
        //   … worth naming here because neither compiler nor guard will."
        //
        //   * UpdateHeldCardTransfer before UpdatePalmGate — a dominant-to-gate hand-to-hand
        //     transfer must count as "the gate hand holds a card" in the very frame it happens,
        //     or the fan opens for one frame through the just-received card.
        //   * every laser path before UpdateHandContactArbitration — arbitration reads the hover
        //     those paths publish. This is the coupling that crosses the part-2/part-3 cut, and
        //     "these are all independent, let me tidy the tick calls" is exactly how it breaks
        //     (INVARIANTS-Cards §3).
        //   * UpdateOverlayGate before both overlay paths — it is the game-state gate (results
        //     window / narrator dialog / scenario end) they consult.
        //   * UpdateSlotHighlight before UpdateFanInsertion — insertion's precedence check reads
        //     the slot the highlight just resolved.
        //
        // BEFORE the palm gate on purpose: a dominant→gate hand-to-hand transfer must count as
        // "the gate hand holds a card" in the very frame it happens, so the fan block engages
        // without a one-frame open fan through the just-received card.
        UpdateHeldCardTransfer();
        UpdatePalmGate();

        // Modal COMMIT-block: while a BLOCKING modal floats (story/results/durability — NOT the
        // player-reachable pause/ESC/Options family), nothing behind it may be COMMITTED. Keyed on
        // BlockingWindowModalActive, NOT WindowModalActive: the reachable menus (NonBlockingMenus)
        // must impose ZERO restrictions — the user keeps grabbing cards / picking hexes with the
        // pause menu open (explicit requirement). WindowModalActive was the bug here twice over:
        // it is "ANY floated window", so (a) an open ESC menu froze all card input, and (b) a
        // CLOSED menu whose sticky float hadn't been released yet STILL counted as open ("I closed
        // the menu but cards stayed dead"). Exemptions live OUTSIDE this driver and stay
        // untouched: the tray's PanelGrabHandle / panel-grab and the modal window host itself.
        //
        // LASER RULING (user 2026-08: "der Laser ist ausnahmslos da und collidet"): the block no
        // longer skips the laser paths or clears hovers — the beam, its clamp onto cards/tray
        // elements and all hover feedback stay LIVE under every modal. What the block still does
        // is the COMMIT layer only (decision table: WorldUI.ModalFallback.HardCommitLockActive):
        // cards are forced non-poke/non-grab (BlockCardInteractions → CanGrab false, so every
        // laser pluck / rescue grab / fingertip select refuses itself at its existing CanGrab /
        // PokeSelectEnabled seams) and the non-card commits inside the laser paths (tray element
        // presses, item-chip plucks, click-away dismiss) check _modalInputBlocked directly.
        // Releases automatically — the next Rebuild restores each card's zone poke/grab flags.
        bool modalBlock = WorldUI.ModalFallback.BlockingWindowModalActive;
        if (modalBlock != _modalInputBlocked)
        {
            _modalInputBlocked = modalBlock;
            if (modalBlock)
            {
                // Name the CULPRIT (user report 2026-08-02): "blocking modal open" alone could not
                // tell a hardware log which window latched the gate — the MP player picker took a
                // whole session to pin. DescribeBlockingWindows allocates, so it runs only here,
                // on the state EDGE.
                // HW-VERIFY: THE line the 2026-09-02 report is decided by. If it names
                // 'GloomhavenVR.OptionsTabWindow' the ModBuild 341 classification did not take and
                // the VR settings window is still being read as a blocking game modal. Edge-gated
                // (fires only when _modalInputBlocked changes), never per frame. Promoted
                // Info -> Note in ModBuild 341; the TEXT is byte-identical, only the tier moved.
                VRLog.Note("Cards", "Modal commit-block ENGAGED — BLOCKING modal open (not the pause/options " +
                                    "family): card/tray COMMITS gated off; laser beam, collision and hover stay live. " +
                                    $"Blocking window(s): {WorldUI.ModalFallback.DescribeBlockingWindows()}.");
            }
            else
            {
                // HW-VERIFY: the release half of the pair above — an ENGAGED with no RELEASED is
                // the gate outliving its edge, which is exactly what the ModBuild 340 log shows
                // (line 9530 engages and nothing releases it for the rest of the session).
                VRLog.Note("Cards", "Modal commit-block RELEASED — blocking modal closed: restoring card poke/grab commits.");
                _dirty = true; // Rebuild re-applies each card's zone Grabbable/PokeSelectEnabled next frame
            }
        }

        if (modalBlock)
            BlockCardInteractions();
        // FIRST in the laser block (user report 2026-08-08): a hand physically INSIDE a card of a
        // fan/pile has no laser at all — the beam would leave through the card and grab/press
        // whatever stands behind it. Publishes a per-hand stand-down that every path below reads
        // through dom.Ray.Active, and that the hand interactors read the same way.
        UpdateLaserContactStandDown();
        UpdateFanLaser();
        // BEFORE the board laser on purpose: a hand physically in contact with a card in the
        // board-anchored browse arc / item fan owns that hand's trigger, for BOTH hands. Running
        // it after would let the board laser spend the pull as a board click first — the exact
        // "the card highlights but the trigger does nothing" the user reported.
        UpdateBoardFanHandTrigger();
        UpdateBoardLaser();
        UpdateBrowseLaser();
        UpdateItemFanLaser(); // item fan: same geometric+sticky pick as the browse fan above
        UpdateActiveLaser();
        // Issue A/B: elect the ONE fan/dock card the free hand is in contact with (closest,
        // with incumbent hysteresis) — every other card's hand-driven lift drops and the
        // grab follows the same winner. Runs after the laser paths so the laser-hovered
        // card of THIS frame is never suppressed (laser plucks stay untouched).
        UpdateHandContactArbitration();
        UpdateFanHoverSplit();
        UpdateOverlayGate(); // B/C: game-state gate (results window / narrator dialog / scenario end) before both overlay paths
        UpdateSlotHighlight();
        UpdateFanInsertion(); // after the slot highlight so its precedence check reads a fresh slot
        // Task #9: never linger under a modal — and never linger next to an OPEN fan either.
        // The placard says "you have no hand cards"; the moment cards arrive and the fan opens
        // (a draw landing mid-fade, or the gate re-opening onto a now-populated hand) that
        // statement is false and the fan itself is the better answer. Otherwise Tick both
        // re-poses it on its hand and advances the fade.
        if (_modalInputBlocked || _fan.IsOpen)
            _emptyFanHint.Hide();
        else
            _emptyFanHint.Tick();
        _fan.Tick();
        _half.Tick();
        _browser.Tick(); // held reading fan follows the grabbing hand (item 5)

        CardsHandUI? hand = CurrentHand();
        // The character the BOARD is presenting (focus override applied). Resolved once per tick
        // and only handed to surfaces that DISPLAY — never to a path that can reach a game seam.
        CardsHandUI? presented = Board.CharacterFocus.PresentedHand(hand);
        PollModeChange(_fakeActive ? null : hand); // deadlock safety: rebuild on any game card-mode change
        // ITEM 10 (burned card still on the fan): rebuild the moment the PRESENTED character's
        // hand-pile card set changes. DELIBERATELY OUTSIDE the _tray.IsVisible gate below — the fan
        // hangs off the palm, not off the board, and a hand whose tray is not up yet must still
        // never show a card the character no longer holds. See PollHandCards for why nothing else
        // in the ~12 rebuild triggers watched this set.
        PollHandCards(_fakeActive ? null : presented);
        if (_tray.IsVisible)
        {
            _tray.TickStatus(_fakeActive ? null : hand);
            _rest.TickStatus(_fakeActive ? null : hand);
            _piles.TickStatus(_fakeActive ? null : hand, _fakeActive ? null : presented);
            // feature 6: rebuild the active area when its set changes — watched on the PRESENTED
            // hand, the same one UpdateActive builds from (see PollActive's doc for why the two
            // halves reading different characters is the item-pile defect shape).
            PollActive(_fakeActive ? null : presented);
            TickBurnToPile(_fakeActive ? null : hand); // issue B: fly damage-burned cards into the burnt pile
        }
        else
        {
            _burnWatchHand = null; // tray hidden — re-baseline the burnt set when it returns
        }
        UpdateWantedSlots(_fakeActive ? null : hand); // test #28: steady "wanted slot" hint
        UpdatePickStatus(_fakeActive ? null : hand);  // event-discard: pick banner + CONFIRM/UNDO keycap overrides
        // Item-surrender pick (event consume/refresh mali): pump runs whenever the tray exists,
        // independent of the pile-stack visibility gate inside _piles.TickStatus — the demand
        // can arrive at scenario start before any pile UI has shown.
        if (_tray.IsVisible)
            _piles.TickItemDemand(_fakeActive ? null : hand);
        UpdateInitiativeTodo(); // item 6: glow the initiative-order characters who still owe cards
        // Item 8b: the cross-client "which card is active" census. DELIBERATELY OUTSIDE the
        // _tray.IsVisible gate above — the whole point of the line is that a seat which is drawing
        // NOTHING still reports, and a board that is not up is exactly such a seat. Per-frame and
        // self-throttling: it prints on a change and otherwise once every ActiveCardSet.
        // RestateSeconds.
        ActiveCardSet.PrintIfDue();

        TickPickReturnSettle(); // pick restart: re-arm the returned cards once the reverse flight lands
        PollShortRest(_fakeActive ? null : hand); // redraw-swaps ShortRestedCard with no mode change
        if (!_fakeActive)
        {
            PumpLongRestTurn(); // long-rest turn: drive the game's own PERFORM LONG REST flow (re-armed every tick)
            PumpSelectionHandSwitch(); // hand-switch watchdog: presented hand re-converges on the selected
                                       // character if the game's portrait-click SwitchHand edge was swallowed
                                       // ("Optionsmenü darf das Spielgeschehen nie beeinflussen", user 2026-08)
        }
        LogLongRestState(_fakeActive ? null : hand); // test #28: prove the long-rest state transitions
        LogFanState(hand);
        LogActionSelectionState(_fakeActive ? null : hand); // second-character action deadlock diagnostic
        TickTakeDamageSelection(); // task #6: select the attacked character during a take-damage decision
        TickTakeDamageOptions();   // 2026-08-07: the burn options must stay OFFERED for the whole decision
    }

    // ------------------------------------------------- per-frame tick attribution guard --

    /// <summary>
    /// Attribute + throttle an exception thrown by the per-frame interaction/status path.
    /// Turns the otherwise ANONYMOUS, stackless per-frame NullReferenceException flood Unity
    /// would write for an unhandled Update throw into a single traced [Cards] Error (subsystem +
    /// message + stack) plus a throttled repeat summary — so the root deref is finally
    /// attributable from Player.log alone, without swallowing the bug silently.
    ///
    /// <para><b>THE BOOKKEEPING MOVED TO <see cref="Core.TickGuard.NoteThrow"/> ON 2026-09-05, and
    /// the two defects it fixes are both about READING the line, not writing it</b> (redundancy
    /// survey R30(b) and R30(c)):</para>
    /// <list type="number">
    ///   <item>The counter used to live on THIS DRIVER INSTANCE (<c>_tickThrowCount</c>) while
    ///   <c>TickGuard</c>'s is static. Two lines with the same <c>is still throwing (N time(s) so
    ///   far)</c> wording therefore carried N values counted over different populations, and the
    ///   project's triage procedure compares exactly those numbers. Now there is one store.</item>
    ///   <item>It keyed the WHOLE tick path. <c>TickGuard</c> keys per named sub-step, so two
    ///   throwing subsystems inside Cards used to read as one storm with one count. The key is now
    ///   <c>Cards.TickInteractionsAndStatus/&lt;throwing method&gt;</c> via
    ///   <see cref="Core.TickGuard.SubStepOf"/> — the one try/catch here isolates a path rather
    ///   than a step, so the sub-step comes off the exception instead of off a call site.</item>
    /// </list>
    ///
    /// <para>The wording of both lines is byte-identical to what it has always been; the shared
    /// method takes this site's own two sentences and supplies only the opener and the repeat
    /// shape, which are what a reader greps for.</para>
    /// </summary>
    private void NoteTickThrow(System.Exception ex)
    {
        Core.TickGuard.NoteThrow(
            "Cards",
            Core.TickGuard.SubStepOf(ex, "Cards.TickInteractionsAndStatus"), ex,
            "Per-frame Cards tick",
            "the Rebuild / CardActionQueue path is never starved by it. This is the source of any "
            + "anonymous per-frame NullReferenceException flood attributed to the Cards subsystem.",
            "Fix the deref; the tick stays isolated.");
    }

    // ------------------------------------------------------- THE FAN'S ONE DECISION POINT --
    //
    // ITEM 3 (user 2026-09-02, verbatim): "Der Fächer muss IMMER angezeigt werden, egal in welchem
    // Status das Spiel gerade ist. Die einzige valide Ausnahme ist, wenn dem Spieler kein Character
    // zugewiesen wurde im Multiplayer."
    //
    // THIS IS THE THIRD ROUND IN WHICH A FAN-GATING RULE SURPRISED HIM, and all three had the same
    // shape: a VR MODE that silently dropped the fan's ENABLING CAPABILITY.
    //   * round 1 — VRMode.BoardTargeting carried Ray|Poke only, so a move/attack confirmation
    //     killed the fan for every character (fixed by the one-shot policy grant in
    //     CardsDriver.OnEnable, which enumerated that ONE mode);
    //   * round 2 — VRMode.ModalUI has no PalmGate either, and an options window was raising the
    //     ModalUI lock (ModBuild 341/344 reclassified the mod's own menu family out of "blocking");
    //   * round 3 — this report.
    // Each fix named the mode that had been hit. That is whack-a-mole: the fan's visibility was
    // OWNED BY A TABLE IT DOES NOT CONTROL (Core.Events.VRModeStateMachine.InteractorPolicy), and
    // any mode added there without a PalmGate bit silently deletes the hand fan.
    //
    // SO THE OWNERSHIP MOVES HERE. The driver ASSERTS the palm-gate capability whenever it has a
    // fan to show (EnsureFanCapability below) instead of hoping the mode row grants it. The mask is
    // applied on the mode-change EDGE only (Hands.HandsDriver.ApplyMode ← VRModeStateMachine.
    // ModeChanged), and PalmGate.Enabled's setter early-returns on no change, so this is an
    // idempotent per-frame re-assert, not a write war. Nothing else consults PalmGate.Changed, and
    // the map room drives THIS SAME fan (WorldUI.MapRoom.MapRoomHand hands its loadout to this
    // driver), so there is no second consumer to surprise.
    //
    // THE FULL LIST OF THINGS THAT CAN STILL WITHHOLD THE FAN — and every one of them is now named
    // in the log by FanBlocker, so the next report does not need a fourth round to find out which:
    //   1. HandsDown      — no hands/rig anchor at all (UpdateBody's anchor bail-out). Not a game
    //                       state: nothing can be drawn.
    //   2. NoGateHand     — the non-dominant VRHand does not exist yet.
    //   3. NoCards        — the mod built no fan cards. THIS is where "no character assigned" lands,
    //                       and it is the user's one sanctioned exception. It is also where every
    //                       UPSTREAM refusal ends up, which is why FanBlocker is not the whole
    //                       story for it — CharacterFocus.ResolveHand's SELECTION FLOOR is what
    //                       keeps a hand bound when the GAME presents a foreign one, and Rebuild
    //                       fills the fan in EVERY CardHandMode (FillHandFan's `default:` arm).
    //   4. GateDisabled   — the mode's interactor mask has no PalmGate. AS OF THIS BUILD THIS CAN
    //                       NO LONGER HAPPEN while a fan exists: EnsureFanCapability re-arms it and
    //                       says so once. The term is kept so the log can still report a re-arm.
    //   5. GateHandHolds  — the fan hand is holding a card (user ruling 2026-08-04). A gesture, not
    //                       a game state.
    //   6. PalmDown       — [Cards] RevealMode=tilt and the wrist is not rolled up. A gesture.
    // Nothing else. There is no phase gate, no mode gate and no MP gate on the fan any more.

    private (CardHandMode? mode, int widgets, int fanBuffer, bool gateEnabled, bool revealed,
        bool open, bool boundHand, VRMode vrMode, FanBlocker blocker)? _lastFanState;

    /// <summary>Why the hand fan is not on screen this frame — the SINGLE classifier, shared by the
    /// gate that acts (<see cref="UpdatePalmGate"/>) and the line that reports
    /// (<see cref="LogFanState"/>), so the two can never tell different stories.</summary>
    private enum FanBlocker
    {
        /// <summary>The fan is open. Nothing is withholding it.</summary>
        None,
        /// <summary>No hands/rig anchor — see UpdateBody's anchor bail-out.</summary>
        HandsDown,
        /// <summary>The non-dominant hand does not exist yet.</summary>
        NoGateHand,
        /// <summary>The mod built no fan cards. The user's one sanctioned exception (no character
        /// assigned) lands here — and so would any upstream refusal, which is why it is loud.</summary>
        NoCards,
        /// <summary>The VR mode's interactor mask carries no PalmGate. Cannot survive a frame any
        /// more — <see cref="EnsureFanCapability"/> re-arms it.</summary>
        GateDisabled,
        /// <summary>The fan hand is holding a card (user ruling 2026-08-04).</summary>
        GateHandHolds,
        /// <summary>RevealMode=tilt and the palm is not rolled up. A gesture, not a state.</summary>
        PalmDown,
    }

    /// <summary>
    /// THE reveal predicate — <c>RevealMode=always</c> needs no gesture at all, tilt mode
    /// additionally holds the fan open while the dominant laser is on it (plucking must never
    /// collapse the fan mid-reach). Extracted because it used to be written out TWICE, once in
    /// <see cref="UpdatePalmGate"/> and once in <see cref="LogFanState"/> — a mirrored predicate
    /// whose two copies would eventually disagree, at which point the diagnostic would be reporting
    /// a state the gate never had.
    /// </summary>
    private bool FanRevealed(PalmGate? gate) =>
        gate != null && (CardsConfig.RevealAlways
            ? gate.Enabled
            : gate.Enabled && (gate.IsOpen || _laserHover != null));

    /// <summary>Classify the frame: which term (if any) is keeping the fan off screen. Order is
    /// most-structural first, so the answer names the ROOT refusal rather than a consequence.</summary>
    private FanBlocker FanBlockerOf(PalmGate? gate, bool allowFan, bool gateHandHolds)
    {
        if (_fan.IsOpen)
            return FanBlocker.None;
        if (AnchorParent() == null)
            return FanBlocker.HandsDown;
        if (_gateHand == null || gate == null)
            return FanBlocker.NoGateHand;
        if (!allowFan)
            return FanBlocker.NoCards;
        if (!gate.Enabled)
            return FanBlocker.GateDisabled;
        if (gateHandHolds)
            return FanBlocker.GateHandHolds;
        return FanBlocker.PalmDown;
    }

    /// <summary>Change-dedup for the Note-tier WITHHELD verdict, so a steady state prints once and a
    /// real transition always prints. <c>inScenario</c> is a KEY MEMBER and not merely a filter: it
    /// is one of the terms that decides whether the line is worth saying at all, so leaving it out
    /// would let a menu state latch the key and then swallow the identical in-scenario state — the
    /// exact frame the report is about.</summary>
    private (FanBlocker blocker, bool hadCards, VRMode mode, bool inScenario)? _loggedFanBlocker;

    // ------------------------------------------------------------------ fan diagnostics --

    /// <summary>
    /// Test #16 diagnostic (change-deduped Info, [Cards] style): everything the fan's
    /// visibility depends on, in one line. The #16 hardware log proved the rebuild
    /// side healthy ("Rebuild: … fan=10" all session) while the user saw NO cards —
    /// the reveal gating (palm gate disabled by a stuck ModalUI) was only visible in
    /// Debug lines the LogOutput capture drops. With this line, any future "fan never
    /// showed" is attributable from LogOutput.log alone.
    ///
    /// <para>…AS LONG AS THE CAPTURE IS AT DEBUG, WHICH THE 2026-09-02 MULTIPLAYER DROP WAS NOT.
    /// The co-player's LogOutput.log ran at the shipped default (<c>LogLevel = Info</c>), and this
    /// whole census is <c>VRLog.Info</c> = Debug tier, so his 91 MB-equivalent of evidence contained
    /// ZERO <c>fan state:</c> lines and ZERO <c>PalmGate</c> lines — the one question his test was
    /// run to answer was unanswerable from his drop by construction. The census stays where it is
    /// (per-change, it is far too chatty for the shipped tier); what was promoted is the VERDICT:
    /// <see cref="LogFanWithheld"/> writes one <c>VRLog.Note</c> line naming the blocking TERM,
    /// change-deduped, which is exactly the field a report needs and nothing more.</para>
    /// </summary>
    private void LogFanState(CardsHandUI? hand)
    {
        CardHandMode? mode = hand != null ? CardsGameApi.Mode(hand) : null;
        PalmGate? gate = _gateHand != null ? _gateHand.PalmGate : null;
        bool gateEnabled = gate != null && gate.Enabled;
        bool revealed = FanRevealed(gate);
        bool allowFan = _fanBuffer.Count > 0 || _fan.Cards.Count > 0 || _fan.HasLeavingCards;
        bool gateHandHolds = _gateHand != null && _gateHand.Grabber.Held != null;
        FanBlocker blocker = FanBlockerOf(gate, allowFan, gateHandHolds);

        LogFanWithheld(blocker, allowFan);

        var state = (mode, widgets: _widgetBuffer.Count, fanBuffer: _fanBuffer.Count, gateEnabled,
            revealed, open: _fan.IsOpen, boundHand: _boundHand != null,
            vrMode: VRModeStateMachine.CurrentMode, blocker);
        if (_lastFanState.HasValue && _lastFanState.Value == state)
            return;
        _lastFanState = state;

        VRLog.Info("Cards", $"fan state: mode={(mode.HasValue ? mode.Value.ToString() : "none")}, " +
                            $"widgets={state.widgets}, fanBuffer={state.fanBuffer}, " +
                            $"gateEnabled={gateEnabled}, revealed={revealed}, open={_fan.IsOpen}, " +
                            $"boundHand={_boundHand != null}, withheldBy={blocker} " +
                            $"(vrMode={state.vrMode}).");
    }

    /// <summary>
    /// THE ONE LINE A "the fan is gone" REPORT IS DECIDED BY, at the shipped log tier. Names the
    /// blocking TERM, not a fraction and not a pile of booleans a reader has to re-derive the rule
    /// from — the 2026-09-02 co-player drop had 296 mod lines and could not answer which of six
    /// independent terms was false, because every one of them lived in a Debug-tier line.
    /// <para>ONLY THE STRUCTURAL BLOCKERS ARE REPORTED HERE, and that restriction is what keeps the
    /// line worth reading. <c>PalmDown</c> and <c>GateHandHolds</c> are GESTURES — the player
    /// lowering their wrist or picking a card up — and they flip several dozen times a session (36
    /// palm-gate cycles in the 2026-09-02 host log alone). Reporting them at the shipped tier would
    /// bury the one transition a report is about under seventy lines of the player using the
    /// feature correctly. They stay in the Debug-tier census, which carries every term.</para>
    ///
    /// <para>Change-deduped on (structural blocker, had cards, mode, in scenario), so a steady state
    /// costs one line and every structural transition — including the RETURN to a shown fan — is
    /// always printed. The pair is the whole diagnostic: a WITHHELD with no SHOWN after it is a fan
    /// that never came back.</para>
    /// </summary>
    private void LogFanWithheld(FanBlocker blocker, bool allowFan)
    {
        // A gesture is not a withholding. Collapse both onto None so the pair below brackets only
        // the states the player cannot fix by moving their hand.
        FanBlocker structural = blocker switch
        {
            FanBlocker.PalmDown or FanBlocker.GateHandHolds => FanBlocker.None,
            _ => blocker,
        };
        bool inScenario = CardsGameApi.InScenario;
        var key = (blocker: structural, hadCards: allowFan,
                   mode: VRModeStateMachine.CurrentMode, inScenario);
        if (_loggedFanBlocker.HasValue && _loggedFanBlocker.Value.Equals(key))
            return;
        bool hadStructural = _loggedFanBlocker.HasValue
                             && _loggedFanBlocker.Value.blocker != FanBlocker.None;
        _loggedFanBlocker = key;
        // Do not narrate the pre-scenario menu: with no hands and no cards there is no fan to
        // withhold, and one line per main-menu mode change is noise at the shipped tier.
        if (structural != FanBlocker.None && !allowFan && !inScenario)
            return;

        if (structural == FanBlocker.None)
        {
            if (!hadStructural)
                return; // never withheld in the first place — nothing to release
            // HW-VERIFY: the RELEASE half. A WITHHELD line with no RELEASED line after it is a fan
            // that never came back — which is exactly the 2026-09-02 report ("nachdem er 'Auswahl
            // beenden' angeklickt hat den Fächer nicht mehr sehen").
            VRLog.Note("Cards", "Hand fan WITHHOLD RELEASED — nothing structural is keeping the fan " +
                                "off any more; from here it is the player's own wrist roll ([Cards] " +
                                $"RevealMode) that decides. (cards built={allowFan}, " +
                                $"vrMode={VRModeStateMachine.CurrentMode}).");
            return;
        }

        string why = blocker switch
        {
            FanBlocker.HandsDown => "the hands/rig anchor is gone (nothing can be drawn at all)",
            FanBlocker.NoGateHand => "the non-dominant VRHand does not exist yet",
            FanBlocker.NoCards => "the mod built NO fan cards this frame. STANDING RULING 2026-09-06: "
                                  + "\"Der Hand-Fächer soll immer sichtbar sein. Die einzige Ausnahme "
                                  + "ist, wenn einem Spieler kein Charakter zugewiesen wurde\" — so "
                                  + "boundHand=NO is the ONE reading of this line that is not a defect, "
                                  + "and boundHand=yes is ALWAYS one. The refusal is then upstream of the "
                                  + "gate: Rebuild fills the fan in every CardHandMode, so look at "
                                  + "CharacterFocus.ResolveHand / the SELECTION GUARD line. "
                                  + $"boundHand={(_boundHand != null ? "yes" : "NO")}, "
                                  + $"boundMode={(_boundHand != null ? CardsGameApi.Mode(_boundHand).ToString() : "none")}"
                                  + $", pickFlowLiveForThisHand={CardsGameApi.PickFlowLive(_boundHand)}"
                                  + " — the sentence that used to stand here (\"in a PICK mode the fan is "
                                  + "the CANDIDATE set, not the hand, so a fully-satisfied pick empties it "
                                  + "by design\") was the defect's own alibi: the 2026-09-06 co-player log "
                                  + "carries 15 of these lines at boundHand=yes and boundMode=LoseCard, "
                                  + "each within a dozen lines of a PICK GATE reading pick=CLOSED. A pick "
                                  + "whose flow has ENDED now falls back to FillHandFan, so boundMode being "
                                  + "a pick mode is no longer an excuse for an empty fan",
            FanBlocker.GateDisabled => "the VR mode's interactor mask carries no PalmGate — this must "
                                       + "not be reachable while a fan exists; EnsureFanCapability "
                                       + "re-arms it and logs a RE-ARMED line",
            FanBlocker.GateHandHolds => "the fan hand is holding a card (user ruling 2026-08-04: the "
                                        + "fan stays blocked while the non-dominant hand holds one)",
            _ => "[Cards] RevealMode=tilt and the palm is not rolled up — a gesture, not a game state "
                 + "(set RevealMode=always to remove the gesture entirely)",
        };
        // HW-VERIFY: THE line that decides a "the fan is not shown" report. It names WHICH of the six
        // independent terms is false; every earlier round had to guess because the census that
        // carries them all is Debug-tier and the tester's capture is not.
        VRLog.Note("Cards", $"Hand fan WITHHELD by {blocker} — {why}. " +
                            $"(cards built={allowFan}, vrMode={VRModeStateMachine.CurrentMode}, " +
                            $"inScenario={inScenario}).");
    }

    // Change-dedup for the ActionSelection click-gate diagnostic (second-character deadlock);
    // references (not strings) so the steady-state check is allocation-free.
    private (object? owner, object? current, bool top, bool bottom, bool valid, int halves,
        bool preview)? _lastActionGate;

    /// <summary>
    /// Second-character action-deadlock diagnostic (change-deduped): prove from LogOutput.log
    /// ALONE whether the currently DOCKED action cards are actually clickable for THIS turn.
    /// The deadlock signature is <c>owner==current=False</c> — the mod docked the previous
    /// character's cards, so the game's own <c>OnAbilityClick</c> guard
    /// (<c>Choreographer.CurrentActor != playerActor</c>, FullAbilityCard.cs:635) silently
    /// rejects every laser/poke click and the turn never advances; a healthy turn logs
    /// <c>owner==current=True, valid, interactable</c>. A docked-but-empty half buffer is
    /// logged as a WARN. Logged once per state change, never per frame.
    /// </summary>
    private void LogActionSelectionState(CardsHandUI? hand)
    {
        if (hand == null || CardsGameApi.Mode(hand) != CardHandMode.ActionSelection)
        {
            _lastActionGate = null;
            return;
        }

        FullAbilityCard? probe = null;
        for (int i = 0; i < _halfBuffer.Count; i++)
        {
            FullAbilityCard? f = _halfBuffer[i] != null ? _halfBuffer[i].FullCard : null;
            if (f != null)
            {
                probe = f;
                break;
            }
        }

        object? current = CardsGameApi.CurrentTurnActor();
        object? owner = probe != null ? CardsGameApi.CardOwner(probe) : null;
        bool valid = probe != null && (CardsGameApi.IsHalfPlayable(probe, CBaseCard.ActionType.TopAction)
                                       || CardsGameApi.IsHalfPlayable(probe, CBaseCard.ActionType.BottomAction));
        bool top = probe != null && probe.IsInteractable(CBaseCard.ActionType.TopAction, considerSelection: false);
        bool bottom = probe != null && probe.IsInteractable(CBaseCard.ActionType.BottomAction, considerSelection: false);

        // The preview latch joins the dedup tuple (2026-08-08 deadlock): without it the gate
        // line was deduped into silence exactly when it mattered, because every OTHER field
        // stayed healthy while the All-Cards preview refused every click.
        var state = (owner, current, top, bottom, valid, halves: _halfBuffer.Count,
                     preview: CardsHandManager.Instance != null
                              && CardsHandManager.Instance.IsFullCardPreviewShowing);
        if (_lastActionGate.HasValue && _lastActionGate.Value.Equals(state))
            return;
        _lastActionGate = state;

        if (probe == null)
            VRLog.Warn("Cards", $"ActionSelection: NO action card docked (halves={_halfBuffer.Count}) — nothing " +
                                "clickable this turn (re-dock pending or wrong hand resolved).");
        else
            VRLog.Info("Cards", $"ActionSelection gate: {CardsGameApi.DescribeActionGate(probe)} (halves={_halfBuffer.Count}).");
    }

    /// <summary>Cards live in the same scaled space as the hands (rig root in VR, sim camera in dev).</summary>
    private static Transform? AnchorParent()
    {
        VRHand? any = VRHands.Left != null ? VRHands.Left : VRHands.Right;
        if (any == null)
            return null;
        Transform handsRoot = any.transform.parent; // "GloomhavenVR.Hands"
        return handsRoot != null ? handsRoot : any.transform;
    }

    private CardsHandUI? CurrentHand()
    {
        if (!CardsGameApi.InScenario)
            return null;
        // Second-character action deadlock: during ActionSelection the game keys the
        // top/bottom click-gate on Choreographer.CurrentActor (FullAbilityCard.cs:635), so we
        // must present THAT actor's hand — not CardsHandManager.CurrentHand, which can lag a
        // same-mode turn hand-off and leave us docking the previous character's cards (every
        // click then silently rejected). Outside ActionSelection, fall back to the presented
        // hand as before.
        // Item-surrender pick (event consume/refresh mali): while the game's ItemCardPicker is
        // open for a locally-controlled actor, present THAT actor's hand — at scenario start no
        // CardsHandManager.Show has run, so ActiveHand may be null and no surface (tray/piles/
        // item fan) would exist for the demanded selection (the item twin of the discard fix).
        // Goal-chest forfeit (flow 1): the same rule for ItemRewardLosePicker — the pick has NO
        // owning actor (a shared party reward), so ANY local hand anchors the surfaces; without
        // this, a forfeit arriving during a REMOTE actor's turn would leave the deciding host
        // with no presented hand and the deadlock would survive.
        // Requirement A (deciding-actor hand, the generalized ItemPickHand pattern): the game
        // raises its interactive decision flows ONE at a time (each is a blocking SRL phase
        // step released only by StepComplete — the Choreographer's message queue is a plain
        // FIFO renderer, Choreographer.cs:1645/2344), so a simple priority chain here IS
        // "switch as each flow arrives". Two more deciding-actor flows join the chain:
        //  - TakeDamageHand (FIRST — the panel is modal over whatever turn is running): the
        //    attacked/burning character's hand while an open take-damage decision is locally
        //    controlled, so the item fan holds THAT character's shield items and the burn pick
        //    stays on the paying hand. Ahead of ActionSelectionHand deliberately: while the
        //    panel is open no ability click is possible anyway, and the decision can target a
        //    DIFFERENT local character than the acting one.
        //  - InitiativeAdjustHand: the boots' ± phase walks the party one actor at a time and
        //    the game deliberately never SwitchHands there (InitiativeTrackPlayerAvatar.cs:24),
        //    so CurrentHand is stale — the reported "fan showed the other character's items".
        // All entries are null OUTSIDE their flow, so normal presentation — including manual
        // portrait switching via the initiative track — is untouched between decisions.
        // The chain itself lives in CardsGameApi.DecidingHand so a SECOND reader can ask the
        // question this method answers implicitly — "is the presented hand a DECIDING claim, or
        // merely the fallback ActiveHand?" (PlayTray.ConfirmCapsForeignView's attribution test).
        CardsHandUI? hand = CardsGameApi.DecidingHand() ?? CardsGameApi.ActiveHand();
        return hand != null && CardsGameApi.IsLocalHand(hand) ? hand : null;
    }

    // THE HAND THE BOARD IS PRESENTING is CurrentHand() run through
    // Board.CharacterFocus.PresentedHand — the same decision tree Rebuild's ResolveHand makes, minus
    // the latching, so it may be asked from a per-frame path (Update's pile-status feed) and from an
    // interaction callback (CardsDriver.OpenBrowser). Rebuild keeps the single LATCHING call.
    //
    // USE IT FOR WHAT IS DISPLAYED, NEVER FOR WHAT IS COMMITTED. Every path that can reach a game
    // seam (confirm/undo, rests, action play, item use, the pick flows) keeps reading CurrentHand(),
    // because those belong to the hand the GAME presents — writing them against a merely-watched
    // character is exactly the wrong-hand class of bug OnCardReleased's WRONG-HAND BELT exists for.

    private void UpdatePalmGate()
    {
        // Fan trigger: palm gate on the NON-dominant hand (Demeo: the off hand holds
        // the deck, the dominant hand interacts).
        VRHand? gateHand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        if (gateHand != _gateHand)
            _gateHand = gateHand;
        // P7 (test #10) narrowed by the 2026-08-04 general rule: the fan-owning hand is excluded
        // from the ability fan's OWN cards only — its palm sits inside the fan and its own
        // proximity hover made two fan cards flip-flop highlights forever. Every other card
        // exempts itself via VRCard.AllowsGateHand (stamped !inFan per Rebuild), so BOTH hands
        // hover/highlight/grab everywhere else; this static only tells VRCard WHICH hand the
        // fan-card veto refuses.
        VRCard.InteractionBlockedHand = _gateHand;
        if (_gateHand == null)
        {
            if (_fan.IsOpen)
                _fan.Close();
            ClearLaserHover();
            return;
        }

        // Live-tunable gate feel. The roll MEASURE itself is documented once, at
        // PalmGate's class doc (Hands/Interact/PalmGate.cs) — read it there, do NOT restate
        // it here: this comment used to describe roll gate v3 (the "Demeo roll dot" measure),
        // which v4 replaced after it failed on hardware, and a reader of this file had no way
        // to tell. All that belongs here is what the driver forwards.
        // Thresholds are degrees on that measure: [Cards] RevealEnterDegrees/RevealExitDegrees,
        // defaults 60° enter / 45° exit — a hysteresis dead band so the gate cannot chatter.
        // Live-tunable from the debug menu's Fan category.
        PalmGate gate = _gateHand.PalmGate;
        gate.EnterDegrees = CardsConfig.RevealEnterDegrees.Value;
        gate.ExitDegrees = CardsConfig.RevealExitDegrees.Value;
        // G5 (DEMEO-HANDS-CARDS §4): while the dominant hand holds something the gate stays
        // put so a pluck never re-triggers the fan mid-reach ([Cards] RevealIgnoreWhenGrabbing).
        gate.IgnoreWhenHandBusy = CardsConfig.RevealIgnoreWhenGrabbing.Value;

        // …OR a character-swap exchange is still flying cards OUT of it (2026-08-09). Switching to a
        // character whose hand is EMPTY — everything burnt, or a long rest — leaves both counts at
        // zero on the very frame the outgoing hand sets off, so without this term the fan would be
        // closed underneath its own exchange and CardFan.FinishSwap would snap the whole wave to the
        // gather point: a pop, in the one case where the animation is the ONLY thing that explains
        // where the cards went. The fan closes by itself the moment the wave has drained, and by then
        // it holds nothing, so the close is silent.
        bool allowFan = _fanBuffer.Count > 0 || _fan.Cards.Count > 0 || _fan.HasLeavingCards;
        // ITEM 3 (2026-09-02): the fan's ENABLING CAPABILITY is asserted here, by the driver that
        // owns the fan, and no longer merely hoped for from the mode table. Read the block above
        // LogFanState for the three rounds this closes and why a per-mode grant is the wrong shape.
        EnsureFanCapability(gate, allowFan);
        // FAN BLOCK while the gate hand HOLDS something (user ruling 2026-08-04: "Wird eine Karte
        // in die nicht-dominante Hand genommen, wird - solange sie in der Hand ist - der Faecher
        // blockiert"). The gate hand can now take cards (general both-hands rule), and a fan
        // opening through the very card that hand is holding is nonsense. Deliberately its OWN
        // predicate rather than PalmGate.IgnoreWhenHandBusy: that suppression is an optional
        // config ([Cards] RevealIgnoreWhenGrabbing) and RevealAlways / the tilt-mode laser-hold
        // (_laserHover) bypass gate.IsOpen entirely — this rule must hold in every mode. Both
        // edges ride the NORMAL animations below: holding while open -> _fan.Close() (reverse
        // fan-in + hide sound); releasing -> Grabber.Held clears and the existing reveal gate
        // decides the reopen from the live wrist roll ("je nach Rotation der Hand" — PalmGate
        // keeps measuring while suppressed, so the verdict is instant), via _fan.Open()'s
        // fan-out reveal. No new timer, no snap-hide.
        bool gateHandHolds = _gateHand.Grabber.Held != null;
        // RevealMode=always: no gesture at all while a card phase is live. Tilt mode additionally
        // HOLDS the fan open while the dominant laser is on it — plucking must never collapse the
        // fan mid-reach. ONE definition, shared with the diagnostic (see FanRevealed): this used to
        // be written out here AND in LogFanState, so the two could drift and the log would then be
        // reporting a reveal state the gate never had.
        bool revealed = FanRevealed(gate);
        bool shouldOpen = allowFan && revealed && !gateHandHolds;
        if (shouldOpen && !_fan.IsOpen)
        {
            _fan.Open(_gateHand);
            // Demeo plays MotherbrainAudio.OnCardHandShow with the fan animation
            // (CardHandView.cs:682) — edge-triggered here, once per reveal. This is the ONLY
            // place the fan becomes visible (_fan.Open has exactly one caller — verified),
            // so hooking the sound here covers every open path: initial reveal, re-open,
            // and the RevealAlways auto-open at phase start.
            PlayFanEdgeSound(open: true);
        }
        else if (!shouldOpen && _fan.IsOpen)
        {
            _fan.Close();
            // Demeo mirrors with OnCardHandHide (CardHandView.cs:691) — softer item by default.
            // (The two other _fan.Close() sites are teardown paths — hands down / gate hand
            // lost — where a sound would be wrong; this is the only player-facing close.)
            PlayFanEdgeSound(open: false);
        }

        // Task #9 (empty-fan feedback), re-ruled 2026-08-08: the placard may claim an empty hand
        // ONLY when the character genuinely has none. See MaybeShowEmptyFanHint.
        if (revealed && !_gateWasRevealed)
            MaybeShowEmptyFanHint(allowFan, gateHandHolds);
        _gateWasRevealed = revealed;
    }

    /// <summary>Change-dedup for the capability RE-ARM line: one line per mode that had dropped the
    /// palm gate, not one per frame while that mode is up.</summary>
    private VRMode? _loggedGateRearmMode;

    /// <summary>
    /// ITEM 3, THE STRUCTURAL HALF: the hand fan owns its own enabling capability.
    ///
    /// <para>Read the block above <see cref="LogFanState"/> for the three rounds this closes. The
    /// short version: <c>PalmGate.Enabled</c> is written from a table this driver does not control
    /// (<c>Core.Events.VRModeStateMachine.InteractorPolicy</c> / <c>HandPolicy</c>, applied by
    /// <c>Hands.HandsDriver.ApplyMode</c>), and any VR mode whose row omits the PalmGate bit
    /// silently deletes the fan for as long as that mode is up — with no error, no warning and
    /// nothing in a shipped-tier log. Two modes still omit it today (<c>Menu2D</c>,
    /// <c>ModalUI</c>), a third had to be patched by hand (<c>BoardTargeting</c>, the one-shot grant
    /// in <c>OnEnable</c>), and there is nothing stopping the fourth.</para>
    ///
    /// <para>WHY THIS AND NOT ANOTHER PER-MODE GRANT. A grant enumerates the modes that exist
    /// TODAY; the defect is that the fan's visibility is decided somewhere else at all. Asserting
    /// the capability at the point of use makes the rule "the fan is shown in every game state"
    /// true by construction rather than by an up-to-date table — which is precisely what the user
    /// asked for ("egal in welchem Status das Spiel gerade ist").</para>
    ///
    /// <para>WHY IT IS SAFE. <b>It is gated on this client having a local hand at all</b> — cards
    /// built, or a bound <c>CardsHandUI</c> whose actor is under this client's control. That is
    /// exactly the shape of the 2026-09-06 STANDING RULING ("Der Hand-Fächer soll immer sichtbar
    /// sein. Die einzige Ausnahme ist, wenn einem Spieler kein Charakter zugewiesen wurde"): the
    /// one sanctioned exception has no bound hand, so it is untouched, and so is the main menu.
    /// The cards term alone was too narrow in one case the ruling names — a local character whose
    /// hand is genuinely EMPTY built no cards, so the gate stayed disabled, so <c>FanRevealed</c>
    /// stayed false, so the empty-hand placard's own reveal edge never fired either and the player
    /// rolling his wrist got nothing at all, not even the "keine Handkarten" plate. It grants no
    /// INTERACTION:
    /// what a card may DO is decided per card by the rebuild's grabbable/inspect funnel and by
    /// <c>VRCard.CanGrab</c>, both unchanged — the palm gate only decides whether the fan is SEEN.
    /// It cannot fight the mode machine: <c>ApplyMode</c> writes the mask on the mode-change EDGE
    /// only, and <c>PalmGate.Enabled</c>'s setter early-returns when the value is unchanged, so the
    /// steady state costs one bool compare per frame. And nothing else in the mod consumes this
    /// gate — <c>PalmGate.Changed</c> has no subscribers, and the map room drives this very fan
    /// through this very driver, so there is no second surface to surprise.</para>
    /// </summary>
    private void EnsureFanCapability(PalmGate gate, bool allowFan)
    {
        // A LOCAL CHARACTER IS THE TERM, not "cards were built" — see the remarks. _boundHand is
        // the hand Rebuild last bound, and CurrentHand()/CharacterFocus only ever hand it a hand
        // this client controls (CardsGameApi.IsLocalHand), so it IS "a character is assigned".
        bool haveLocalHand = allowFan || _boundHand != null;
        if (!haveLocalHand || gate.Enabled)
        {
            if (!haveLocalHand)
                _loggedGateRearmMode = null; // next drop announces itself
            return;
        }

        VRMode mode = VRModeStateMachine.CurrentMode;
        gate.Enabled = true;
        if (_loggedGateRearmMode == mode)
            return;
        _loggedGateRearmMode = mode;
        // HW-VERIFY: proof that the class of bug reported three times is now self-healing. If this
        // line names a mode, that mode's interactor row is missing its PalmGate bit and WOULD have
        // eaten the hand fan; the fan is shown anyway. It is not an error — it is the assertion
        // doing its job — but the named mode is worth adding to the table for the other interactors.
        VRLog.Note("Cards", $"Hand fan capability RE-ARMED in vrMode={mode}: that mode's interactor " +
                            "policy carries no PalmGate, which used to delete the hand fan for as " +
                            "long as that mode was up (the BoardTargeting and ModalUI reports). " +
                            "The fan owns its own gate now — it is SHOWN in every game " +
                            "state, and nothing about what a card may DO changed.");
    }

    /// <summary>Change-dedup for the "the model has cards but the mod has no widgets yet" line,
    /// so a hand that stays mid-build does not repeat it once per gate opening.</summary>
    private string? _loggedEmptyHandDeferral;

    /// <summary>
    /// THE "KEINE HANDKARTEN" RULE (user ruling 2026-08-08: "'Keine Handkarten' soll wirklich nur
    /// dann kommen, wenn der Character auch wirklich keine Handkarten hat, egal in welcher Phase —
    /// ansonsten sollen die Handkarten angezeigt werden").
    ///
    /// <para>The placard used to be raised from a MOD-side fact — the fan buffer is empty — which
    /// is a statement about what the mod BUILT this frame, not about the character. It now asks the
    /// game's own model (<c>CCharacterClass.HandAbilityCards</c> via
    /// <c>CharacterFocus.ModelHandCardCount</c>) and this client's live widget list
    /// (<c>CharacterFocus.HandWidgetCount</c>). Three outcomes, and only the first shows anything:</para>
    /// <list type="number">
    /// <item>model 0 AND widgets 0 ⇒ the character really holds no hand cards — placard, with the
    ///   counts in the log so the claim is checkable;</item>
    /// <item>model &gt; 0 (or widgets &gt; 0) but the fan is empty ⇒ the mod has nothing BUILT yet,
    ///   which is never the same statement. No placard: a rebuild is requested and the fan opens on
    ///   its own the moment the cards exist (wait/retry, not a wrong verdict). Logged with both
    ///   counts, change-deduped;</item>
    /// <item>a PICK mode ⇒ the fan is the pick CANDIDATE set (burn/discard/recover), not the hand,
    ///   so an empty one says nothing about the hand and the placard has no business firing. The
    ///   old gate said <c>Mode == CardsSelection</c>, which excluded the pick modes but ALSO
    ///   excluded every other phase — the reason the rule now reads "not a pick mode" instead.</item>
    /// </list>
    /// </summary>
    private void MaybeShowEmptyFanHint(bool allowFan, bool gateHandHolds)
    {
        if (allowFan || gateHandHolds || _gateHand == null || _modalInputBlocked || _fakeActive
            || _boundHand == null)
            return;
        CardHandMode mode = CardsGameApi.Mode(_boundHand);
        // ITEM 6b: LIVE. Outcome 3 is true only while a pick is actually being asked for; a pick
        // whose flow ended shows the HAND again (see the Rebuild fallback), so suppressing the
        // placard on the LATCHED mode left the player with neither cards nor an explanation.
        if (PickFlowLive(_boundHand))
            return; // outcome 3 — the fan is a candidate set, not the hand

        int inModel = Board.CharacterFocus.ModelHandCardCount(_boundHand);
        int widgets = Board.CharacterFocus.HandWidgetCount(_boundHand);
        string who = Board.CharacterFocus.Describe(_boundHand.PlayerActor);

        if (inModel > 0 || widgets > 0)
        {
            // Outcome 2: the hand is NOT empty — never say it is. Ask for a rebuild instead; the
            // fan fill (CardsDriver.FillHandFan) runs in every non-pick mode, so the cards appear
            // as soon as their widgets do.
            _dirty = true;
            string note = $"{who}|{inModel}|{widgets}|{mode}";
            if (_loggedEmptyHandDeferral != note)
            {
                _loggedEmptyHandDeferral = note;
                VRLog.Info("Cards", $"Empty fan SUPPRESSED for '{who}': the placard was NOT shown " +
                                    $"because the hand is not empty — the model names {inModel} hand " +
                                    $"card(s) (CCharacterClass.HandAbilityCards) and this client holds " +
                                    $"{widgets} live hand widget(s), but the mod had built 0 fan card(s) " +
                                    $"this frame (mode={mode}, phase={PhaseManager.PhaseType}, " +
                                    $"readOnlyView={Board.CharacterFocus.ReadOnlyView}). A rebuild is " +
                                    "requested; the fan opens by itself once the cards are built. " +
                                    "'Keine Handkarten' may only ever mean an empty MODEL.");
            }
            return;
        }

        _loggedEmptyHandDeferral = null;
        _emptyFanHint.Show(_gateHand);
        PlayFanEdgeSound(open: false); // the soft hide tick, same listener-anchored path
        VRLog.Info("Cards", $"Empty fan: palm gate opened and '{who}' GENUINELY has no hand cards — " +
                            $"model hand count 0 (CCharacterClass.HandAbilityCards), live hand widgets " +
                            $"0, fan buffer 0 (mode={mode}, phase={PhaseManager.PhaseType}, " +
                            $"readOnlyView={Board.CharacterFocus.ReadOnlyView}). Ghost \"no hand cards\" " +
                            "placard shown at the fan spot (fades ~1.5 s). If this line ever appears " +
                            "with a non-zero model count, the placard is wrong and the counts say why.");
    }

    // ------------------------------------------------------------------ card audio --

    // Fan-edge fallback items when the CONFIGURED item is empty/unknown at call time —
    // names the GAME itself plays (verified in decompiled sources: PlaySound_CardUI_SelectCard
    // FullAbilityCard.cs:590, PlaySound_UICardTabSelect
    // UIPartyCharacterEnhancementAbilityCardsDisplay.cs:190, PlaySound_UIButtonSelect ubiquitous).
    private static readonly string[] FanOpenSoundFallbacks =
        { "PlaySound_CardUI_SelectCard", "PlaySound_UIButtonSelect" };
    private static readonly string[] FanCloseSoundFallbacks =
        { "PlaySound_UICardTabSelect", "PlaySound_UIButtonSelect" };

    /// <summary>Throttle for the FAN SOUND proof lines (gate chatter can flip edges fast).</summary>
    private float _lastFanSoundLog = -10f;

    /// <summary>
    /// Fan reveal/hide sound — the ROOT-CAUSE fix for "sound on close but never on open"
    /// (hardware log build 07621c087: zero sound lines, close audible, open silent).
    /// The old path played BOTH edges through the POSITIONAL overload
    /// <c>AudioController.Play(item, gateHand.transform, null, attachToParent: false)</c>.
    /// The mod never touches the AudioListener, so it rides the GAME's 2D camera — not the
    /// VR head/hands. A 2D item (the close default 'PlaySound_UICardTabSelect', a UI tab
    /// sound) ignores position and stayed audible; a 3D-configured in-world item (the open
    /// default 'PlaySound_EnemyCardDraw', the initiative-track enemy-reveal effect) played
    /// at the gate-hand transform attenuates against the far-away listener to nothing —
    /// silently, because <c>AudioController.Play</c> "succeeds". Fix: play the fan edges
    /// LISTENER-ANCHORED via <c>AudioController.Play(item)</c> (listener pos + forward) —
    /// the exact call shape the game uses for BOTH default items
    /// (<c>AudioControllerUtils.PlaySound</c>, InitiativeTrack.cs:387,
    /// UIPartyCharacterEnhancementAbilityCardsDisplay.cs:190), immune to any listener/rig
    /// placement. Both edges log a throttled proof line
    /// (<c>FAN SOUND open/close: item '…' valid=… played=…</c>): valid =
    /// <c>IsValidAudioID</c> at call time, played = <c>Play</c> returned an AudioObject
    /// (null ⇒ audio disabled / MinTimeBetweenPlayCalls throttle). An empty/unknown
    /// configured item falls back to the first verified-valid game item and says so.
    ///
    /// <para>THE RESOLVE-VALIDATE-PLAY BODY NOW LIVES IN <see cref="GameAudio.PlayListenerAnchored"/>
    /// (ModBuild 19, initiative-refusal-sound task). It was extracted, not copied: the listener
    /// trap above is the kind of knowledge a second call site re-learns on hardware if it is
    /// allowed to write its own version, and this repo lints mirrored constants
    /// (<c>scripts/check-mirrors.sh</c>) precisely because copies drift. Behaviour is unchanged —
    /// same order, same <c>IsValidAudioID</c> gate, same fallback note text, same
    /// <c>AudioController.Play(item)</c> call, same catch-all — so the FAN SOUND proof line below
    /// still reads exactly as it did in every previous hardware log.</para>
    /// </summary>
    private void PlayFanEdgeSound(bool open)
    {
        // [Cards] CardSoundsEnabled is the everyday master switch over ALL mod card sounds
        // (menu overhaul ruling 6). An AND on top of the configured item strings — never a
        // rewrite of them — so a hand-picked audio item survives toggling the switch.
        if (!CardsConfig.CardSoundsEnabled.Value)
            return;
        string edge = open ? "open" : "close";
        string configured = (open ? CardsConfig.FanRevealSound.Value : CardsConfig.FanHideSound.Value) ?? string.Empty;
        string[] fallbacks = open ? FanOpenSoundFallbacks : FanCloseSoundFallbacks;
        bool played = GameAudio.PlayListenerAnchored(configured, fallbacks,
            out string item, out bool valid, out string note);

        float now = Time.unscaledTime;
        if (now - _lastFanSoundLog >= 0.25f)
        {
            _lastFanSoundLog = now;
            VRLog.Info("Cards", $"FAN SOUND {edge}: item '{item}' valid={valid} played={played}{note}.");
        }
    }

    // One-shot warn guard per configured item name, so a typo in any [Cards] *Sound entry
    // logs once instead of every play.
    private static string? _warnedCardSound;

    /// <summary>
    /// Card interaction sound (fan reveal/hide, grab, slot place, take-back) via the game's
    /// own ClockStone audio system — the exact call shape the game uses for card UI sounds
    /// (<c>AudioController.Play("PlaySound_CardUI_...", transform, null,
    /// attachToParent: false)</c>, FullAbilityCard.cs:590), positioned at the given transform.
    /// Direct reference like every other game call in this module (publicized refs).
    /// Guarded: an empty/unknown item or a not-yet-alive audio controller (main menu, scene
    /// load) is skipped silently — audio must never break the interaction path. Edge-triggered
    /// by the callers (grab/release/reveal transitions), never per frame.
    /// </summary>
    internal static void PlayCardSound(string item, Transform at)
    {
        // Everyday master switch over all mod card sounds (menu overhaul ruling 6) — every
        // caller of this helper passes one of the [Cards] *Sound strings, so one gate here
        // covers grab, place and take-back without touching the strings themselves.
        if (!CardsConfig.CardSoundsEnabled.Value)
            return;
        if (string.IsNullOrEmpty(item))
            return;
        try
        {
            if (!AudioController.IsValidAudioID(item))
            {
                if (_warnedCardSound != item)
                {
                    _warnedCardSound = item;
                    VRLog.Warn("Cards", $"Card sound '{item}' is not a known audio item (yet?) — skipped. " +
                                        "Known card/UI items: PlaySound_EnemyCardDraw, PlaySound_CardUI_SelectCard, " +
                                        "PlaySound_UICardTabSelect, PlaySound_CardUI_DiscardedCard, PlaySound_CardUI_BurnedCard, " +
                                        "PlaySound_UIButtonSelect, PlaySound_UIUndoHex, PlaySound_ScenarioUIUndo, " +
                                        "PlaySound_ScenarioUI_TileConfirm.");
                }
                return;
            }
            AudioController.Play(item, at, null, attachToParent: false);
        }
        catch (System.Exception ex)
        {
            if (_warnedCardSound != item)
            {
                _warnedCardSound = item;
                VRLog.Warn("Cards", $"Card sound '{item}' failed to play: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
