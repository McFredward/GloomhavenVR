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

    /// <summary>User escape hatch pending: the VR settings "Board zurückholen" button was pressed.
    /// Per the P2 threading rule the UI handler only sets the flag; <see cref="Update"/> performs
    /// the re-home on the main thread with a reliably valid head pose.</summary>
    private bool _recallBoard;

    /// <summary>True once the "no hand anchor while in a scenario" state has been logged, so the
    /// per-frame path reports the board going away exactly once per occurrence.</summary>
    private bool _anchorLossLogged;

    /// <summary>
    /// USER ESCAPE HATCH (settings → Komfort → "Board zurückholen"): bring the control board back
    /// in front of the player NOW, whatever mode it is in and without waiting for the watchdog's
    /// dwell timer. Deliberately a static request rather than a direct call — the settings panel
    /// runs off a UI callback and the board pose may only be written from the driver's Update
    /// (P2 threading rule), which also sanctions the move for the issue-C pose watchdog.
    /// No-op with no live driver (no scenario / hands down).
    /// </summary>
    internal static void RequestBoardRecall()
    {
        if (Instance != null)
            Instance._recallBoard = true;
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
            lost = !finite || horizontal.magnitude > 6f * s || delta.y < -2f * s;
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
        _fanOriginCards.Clear();
        _fanOrder.Clear();
        _insertGap = -1;
        _insertHighlightCard = null;
        VRCard.InteractionBlockedHand = null;
        VRCard.HandArbitrationHand = null;
        _handContactWinner = null;
        _contactSuppressed.Clear(); // flags themselves die with the cards (OnDisable clears)
        _emptyFanHint.Destroy(); // task #9: ghost placard teardown
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
        _flyingToPile.Clear(); // issue 5: the hand's cards (any mid-flight) just died
        _lastHalfCards.Clear();
        _lastTrayCards.Clear(); // issue 1: slot occupants die with the hand's cards
        _lastVisibleCards.Clear();
        _lastCardWorldPos.Clear(); // issue 1: last-known poses die with the hand's cards
        _lastCardWorldRot.Clear();
        _dockAnimSuppressed = true; // issue 2: the next hand's cards populate silently (no storm)
        _burnWatchHand = null; // issue B: re-baseline the burnt set for the next hand
        _knownBurntWidgets.Clear();
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
                RelayoutField();
            if (ReferenceEquals(card, _shortRestCard)) // sacrifice widget recycled under us
            {
                _shortRestCard = null;
                _shortRestPresented = null;
            }
            _tray.RemoveCard(card);
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

        // User escape hatch (VR settings → Komfort → "Board zurückholen"): an explicit, always
        // available "bring it back" that does not wait for the watchdog dwell timer.
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
        UpdatePalmGate();

        // Modal input-block: while a BLOCKING modal floats (story/results/durability — NOT the
        // player-reachable pause/ESC/Options family), nothing behind it may be clicked. Force every
        // card non-poke/non-grab and skip all card/board/browse/active laser picks (clearing any
        // live hover). Keyed on BlockingWindowModalActive, NOT WindowModalActive: the reachable
        // menus (NonBlockingMenus) must impose ZERO restrictions — the user keeps grabbing cards /
        // picking hexes with the pause menu open (explicit requirement). WindowModalActive was the
        // bug here twice over: it is "ANY floated window", so (a) an open ESC menu froze all card
        // input, and (b) a CLOSED menu whose sticky float hadn't been released yet STILL counted as
        // open ("I closed the menu but cards stayed dead"). Exemptions live OUTSIDE this driver and
        // stay untouched: the tray's PanelGrabHandle / panel-grab and the modal window host itself.
        // Releases automatically — the next Rebuild restores each card's zone poke/grab flags.
        bool modalBlock = WorldUI.ModalFallback.BlockingWindowModalActive;
        if (modalBlock != _modalInputBlocked)
        {
            _modalInputBlocked = modalBlock;
            if (modalBlock)
            {
                VRLog.Info("Cards", "Modal input-block ENGAGED — BLOCKING modal open (not the pause/options " +
                                    "family): cards made non-poke/non-grab and all card/board laser picks gated off.");
            }
            else
            {
                VRLog.Info("Cards", "Modal input-block RELEASED — blocking modal closed: restoring card poke/grab + laser picks.");
                _dirty = true; // Rebuild re-applies each card's zone Grabbable/PokeSelectEnabled next frame
            }
        }

        if (modalBlock)
        {
            BlockCardInteractions();
            ClearLaserHover();
            ClearBoardHover();
            ClearBrowseHover();
            ClearItemFanHover();
            ClearActiveHover();
        }
        else
        {
            UpdateFanLaser();
            UpdateBoardLaser();
            UpdateBrowseLaser();
            UpdateItemFanLaser(); // item fan: same geometric+sticky pick as the browse fan above
            UpdateActiveLaser();
        }
        // Issue A/B: elect the ONE fan/dock card the free hand is in contact with (closest,
        // with incumbent hysteresis) — every other card's hand-driven lift drops and the
        // grab follows the same winner. Runs after the laser paths so the laser-hovered
        // card of THIS frame is never suppressed (laser plucks stay untouched).
        UpdateHandContactArbitration();
        UpdateFanHoverSplit();
        UpdateOverlayGate(); // B/C: game-state gate (results window / narrator dialog / scenario end) before both overlay paths
        UpdateSlotHighlight();
        UpdateFanInsertion(); // after the slot highlight so its precedence check reads a fresh slot
        if (_modalInputBlocked)
            _emptyFanHint.Hide(); // task #9: never linger under a modal
        else
            _emptyFanHint.Tick();
        _fan.Tick();
        _half.Tick();
        _browser.Tick(); // held reading fan follows the grabbing hand (item 5)

        CardsHandUI? hand = CurrentHand();
        PollModeChange(_fakeActive ? null : hand); // deadlock safety: rebuild on any game card-mode change
        if (_tray.IsVisible)
        {
            _tray.TickStatus(_fakeActive ? null : hand);
            _rest.TickStatus(_fakeActive ? null : hand);
            _piles.TickStatus(_fakeActive ? null : hand);
            PollActive(_fakeActive ? null : hand); // feature 6: rebuild the active area when its set changes
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
    }

    // ------------------------------------------------- per-frame tick attribution guard --

    // Throttle state for the Cards per-frame tick guard (see the try/catch in Update). Same
    // contract as WorldUIModule.TickGuard: first throw logged once WITH its stack, further
    // throws summarized at most once / 10 s so an every-frame throw can't itself flood the log.
    private bool _tickThrowOpened;
    private float _tickThrowLastLog;
    private long _tickThrowCount;

    /// <summary>
    /// Attribute + throttle an exception thrown by the per-frame interaction/status path.
    /// Turns the otherwise ANONYMOUS, stackless per-frame NullReferenceException flood Unity
    /// would write for an unhandled Update throw into a single traced [Cards] Error (subsystem +
    /// message + stack) plus a throttled repeat summary — so the root deref is finally
    /// attributable from Player.log alone, without swallowing the bug silently.
    /// </summary>
    private void NoteTickThrow(System.Exception ex)
    {
        _tickThrowCount++;
        float now = Time.unscaledTime;
        if (!_tickThrowOpened)
        {
            _tickThrowOpened = true;
            _tickThrowLastLog = now;
            VRLog.Error("Cards", "Per-frame Cards tick threw and was ISOLATED — the Rebuild / " +
                                 "CardActionQueue path is never starved by it. This is the source of " +
                                 "any anonymous per-frame NullReferenceException flood attributed to " +
                                 $"the Cards subsystem. {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        else if (now - _tickThrowLastLog >= 10f)
        {
            _tickThrowLastLog = now;
            VRLog.Error("Cards", $"Per-frame Cards tick is still throwing ({_tickThrowCount} time(s) so far) — " +
                                 $"latest {ex.GetType().Name}: {ex.Message}. Fix the deref; the tick stays isolated.");
        }
    }

    // ------------------------------------------------------------------ fan diagnostics --

    private (CardHandMode? mode, int widgets, int fanBuffer, bool gateEnabled, bool revealed,
        bool open, bool boundHand, VRMode vrMode)? _lastFanState;

    /// <summary>
    /// Test #16 diagnostic (change-deduped Info, [Cards] style): everything the fan's
    /// visibility depends on, in one line. The #16 hardware log proved the rebuild
    /// side healthy ("Rebuild: … fan=10" all session) while the user saw NO cards —
    /// the reveal gating (palm gate disabled by a stuck ModalUI) was only visible in
    /// Debug lines the LogOutput capture drops. With this line, any future "fan never
    /// showed" is attributable from LogOutput.log alone.
    /// </summary>
    private void LogFanState(CardsHandUI? hand)
    {
        CardHandMode? mode = hand != null ? CardsGameApi.Mode(hand) : null;
        PalmGate? gate = _gateHand != null ? _gateHand.PalmGate : null;
        bool gateEnabled = gate != null && gate.Enabled;
        bool revealed = gate != null && (CardsConfig.RevealAlways
            ? gate.Enabled
            : gate.Enabled && (gate.IsOpen || _laserHover != null));

        var state = (mode, widgets: _widgetBuffer.Count, fanBuffer: _fanBuffer.Count, gateEnabled,
            revealed, open: _fan.IsOpen, boundHand: _boundHand != null,
            vrMode: VRModeStateMachine.CurrentMode);
        if (_lastFanState.HasValue && _lastFanState.Value == state)
            return;
        _lastFanState = state;

        VRLog.Info("Cards", $"fan state: mode={(mode.HasValue ? mode.Value.ToString() : "none")}, " +
                            $"widgets={state.widgets}, fanBuffer={state.fanBuffer}, " +
                            $"gateEnabled={gateEnabled}, revealed={revealed}, open={_fan.IsOpen}, " +
                            $"boundHand={_boundHand != null} (vrMode={state.vrMode}).");
    }

    // Change-dedup for the ActionSelection click-gate diagnostic (second-character deadlock);
    // references (not strings) so the steady-state check is allocation-free.
    private (object? owner, object? current, bool top, bool bottom, bool valid, int halves)? _lastActionGate;

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

        var state = (owner, current, top, bottom, valid, halves: _halfBuffer.Count);
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
        CardsHandUI? hand = CardsGameApi.TakeDamageHand()
                            ?? CardsGameApi.ActionSelectionHand()
                            ?? CardsGameApi.ItemPickHand()
                            ?? CardsGameApi.LoseRewardPickHand()
                            ?? CardsGameApi.InitiativeAdjustHand()
                            ?? CardsGameApi.ActiveHand();
        return hand != null && CardsGameApi.IsLocalHand(hand) ? hand : null;
    }

    private void UpdatePalmGate()
    {
        // Fan trigger: palm gate on the NON-dominant hand (Demeo: the off hand holds
        // the deck, the dominant hand interacts).
        VRHand? gateHand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        if (gateHand != _gateHand)
            _gateHand = gateHand;
        // P7 (test #10): the fan-owning hand is COMPLETELY excluded from card
        // hover/highlight/grab/poke — its palm sits inside the fan and its own
        // proximity hover made two cards flip-flop highlights forever. Only the
        // free (dominant) hand interacts with cards, by laser or proximity.
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

        bool allowFan = _fanBuffer.Count > 0 || _fan.Cards.Count > 0;
        // RevealMode=always: no gesture at all while a card phase is live (gate.Enabled
        // is the mode policy). Tilt mode additionally HOLDS the fan open while the
        // dominant laser is on it — plucking must never collapse the fan mid-reach.
        bool revealed = CardsConfig.RevealAlways
            ? gate.Enabled
            : gate.Enabled && (gate.IsOpen || _laserHover != null);
        bool shouldOpen = allowFan && revealed;
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

        // Task #9 (empty-fan feedback): the palm rolled open but there is nothing to
        // fan — show the ghost "no hand cards" placard at the fan spot so the gesture
        // visibly worked (it used to show NOTHING, which read as a bug). Edge-triggered
        // on the reveal gesture; only in the real hand-fan context (CardsSelection with
        // a bound hand, no modal, not the dev fake hand) so pick flows / dialogs, where
        // an empty fan is expected, never flash it.
        if (revealed && !_gateWasRevealed && !allowFan
            && !_modalInputBlocked && !_fakeActive && _boundHand != null
            && CardsGameApi.Mode(_boundHand) == CardHandMode.CardsSelection)
        {
            _emptyFanHint.Show(_gateHand);
            PlayFanEdgeSound(open: false); // the soft hide tick, same listener-anchored path
            VRLog.Info("Cards", "Empty fan: palm gate opened with ZERO hand cards — ghost " +
                                "\"no hand cards\" placard shown at the fan spot (fades ~1.5 s).");
        }
        _gateWasRevealed = revealed;
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
    /// </summary>
    private void PlayFanEdgeSound(bool open)
    {
        string edge = open ? "open" : "close";
        string configured = (open ? CardsConfig.FanRevealSound.Value : CardsConfig.FanHideSound.Value) ?? string.Empty;
        string item = configured;
        bool valid = false;
        bool played = false;
        string note = string.Empty;
        try
        {
            valid = item.Length > 0 && AudioController.IsValidAudioID(item);
            if (!valid)
            {
                string[] fallbacks = open ? FanOpenSoundFallbacks : FanCloseSoundFallbacks;
                for (int i = 0; i < fallbacks.Length; i++)
                {
                    if (AudioController.IsValidAudioID(fallbacks[i]))
                    {
                        item = fallbacks[i];
                        valid = true;
                        note = $" — configured '{configured}' empty/unknown, fell back to verified game item";
                        break;
                    }
                }
            }
            if (valid)
                played = AudioController.Play(item) != null;
        }
        catch (System.Exception ex)
        {
            note = $" — threw {ex.GetType().Name}: {ex.Message}";
        }

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
