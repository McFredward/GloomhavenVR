using System.Collections.Generic;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Phase-3b orchestrator (one MonoBehaviour, created by <see cref="CardsModule"/>):
/// - pumps <see cref="CardActionQueue"/> (one blocking game call per frame max),
/// - enforces <see cref="HandSuppression"/>,
/// - reacts to the P2 bus/mode machine + Cards signals by rebuilding the card zones
///   (fan / tray / half layout) from authoritative game state,
/// - routes interactions (grab-drop, pokes, palm gate) into the verified game APIs.
///
/// Everything here runs on the main thread; handlers only set dirty flags or enqueue
/// — per-frame work happens in <see cref="Update"/> (P2 threading rules).
/// </summary>
internal sealed class CardsDriver : MonoBehaviour
{
    private readonly VRCardFactory _factory = new();
    private readonly CardFan _fan = new();
    private readonly PlayTray _tray = new();
    private readonly RestControls _rest = new();
    private readonly HalfSelection _half = new();
    private readonly PileViewer _piles = new();
    private readonly PileBrowser _browser = new();
    private readonly ActivePileViewer _active = new();

    // Reused buffers (no per-frame allocations).
    private readonly List<AbilityCardUI> _widgetBuffer = new(24);
    private readonly List<AbilityCardUI> _pileWidgetBuffer = new(16);
    private readonly List<AbilityCardUI> _activeWidgetBuffer = new(8);
    private readonly List<VRCard> _fanBuffer = new(24);
    private readonly List<VRCard> _halfBuffer = new(4);
    private readonly List<VRCard> _browseBuffer = new(16);
    private readonly List<VRCard> _activeBuffer = new(8);
    private readonly List<VRCard> _fakeCards = new(12);
    private int _activeSignature = int.MinValue; // change-gate for the active-card set (feature 6)

    private bool _dirty;
    private bool _modalInputBlocked; // menu-open gate: while set, cards are inert + card/board laser picks are off
    // Item 4: previous VRMode transition, to detect the laundered ModalUI→TableIdle→HalfSelection
    // reveal-confirm round-trip (see OnModeChanged) and NOT re-seat the board on it.
    private VRMode _prevFrom = VRMode.Menu2D;
    private VRMode _prevTo = VRMode.Menu2D;
    private bool _boardChanged; // [Cards] Board switched — tear down + rebuild the tray next frame
    private bool _reassertTray;  // item 3: presence regained (HMD re-donned) — re-assert board placement next frame
    // PART D: the outgoing board's world pose, captured on a SWITCH so the new board re-appears
    // in the EXACT same place instead of re-anchoring to the head.
    private bool _hasSwitchPose;
    private Vector3 _switchPos;
    private Quaternion _switchRot = Quaternion.identity;
    private Vector3 _switchScale = Vector3.one;
    private bool _fakeActive;
    private VRHand? _gateHand;

    // Task #9 (empty-fan feedback): ghost placard shown when the palm gate opens on an
    // empty hand; _gateWasRevealed edge-detects the gesture so the hint fires once per
    // roll, never per frame.
    private readonly EmptyFanHint _emptyFanHint = new();
    private bool _gateWasRevealed;
    private CardsHandUI? _boundHand;

    // ------------------------------------------------------------------ lifecycle --

    private void OnEnable()
    {
        VRModeStateMachine.ModeChanged += OnModeChanged;
        VREvents.CardSelectionChanged += OnCardSelectionChanged;
        VREvents.HandShown += OnHandShown;
        VREvents.SessionResumed += OnSessionResumed; // item 3: re-assert the board after an HMD doff/don
        CardsSignals.HandDestroying += OnHandDestroying;
        CardsSignals.CardRecycling += OnCardRecycling;
        VRHands.HandsChanged += OnHandsChanged;
        CardsConfig.Board.SettingChanged += OnBoardChanged;
        SubscribeBoardTuning(true); // PART F: per-board tuning entries live-apply (menu + hand-edited cfg)

        // GLOBAL hand-fan geometry ("Fan" debug category) — not per-board, so subscribed once.
        CardsConfig.FanPerCardStepDegrees.SettingChanged += OnFanTuningChanged;
        CardsConfig.FanArcSweepDegrees.SettingChanged += OnFanTuningChanged;
        CardsConfig.FanEffectiveRadius.SettingChanged += OnFanTuningChanged;
        CardsConfig.FanHoverSplitScale.SettingChanged += OnFanTuningChanged;

        _tray.SwapRequested += OnSwapRequested;
        _tray.ConfirmRequested += OnConfirmRequested;
        _tray.UndoRequested += OnUndoRequested;
        _rest.ShortRestRequested += OnShortRestRequested;
        _rest.LongRestRequested += OnLongRestRequested;
        _half.PlayRequested += OnPlayRequested;
        _piles.PokeToggled += OnPileTogglePoked;
        _piles.GrabOpened += OnPileGrabOpened;
        _piles.GrabReleased += OnPileGrabReleased;

        _dirty = true;
    }

    private void OnDisable()
    {
        VRModeStateMachine.ModeChanged -= OnModeChanged;
        VREvents.CardSelectionChanged -= OnCardSelectionChanged;
        VREvents.HandShown -= OnHandShown;
        VREvents.SessionResumed -= OnSessionResumed;
        CardsSignals.HandDestroying -= OnHandDestroying;
        CardsSignals.CardRecycling -= OnCardRecycling;
        VRHands.HandsChanged -= OnHandsChanged;
        CardsConfig.Board.SettingChanged -= OnBoardChanged;
        SubscribeBoardTuning(false);

        CardsConfig.FanPerCardStepDegrees.SettingChanged -= OnFanTuningChanged;
        CardsConfig.FanArcSweepDegrees.SettingChanged -= OnFanTuningChanged;
        CardsConfig.FanEffectiveRadius.SettingChanged -= OnFanTuningChanged;
        CardsConfig.FanHoverSplitScale.SettingChanged -= OnFanTuningChanged;
    }

    // ------------------------------------------------------------------ board tuning (Part F) --

    // Debug-menu / hand-edited per-board offsets live-apply through a dirty-flag consumed in
    // Update (the P2 threading rule: handlers only set flags). Position changes apply in place;
    // size/diameter changes rebuild just the affected buttons.
    private bool _applyControlOffsets;   // rest + confirm/undo X/Y/Z offset + group spacing (instant)
    private bool _applyControlRebuild;   // rest diameter / confirm-undo size / button SHAPE (rebuild the buttons)
    private bool _applyOverlayOffset;    // slot/wanted glow offset
    private bool _applyInitiativeOffset; // initiative-track mount position
    private bool _applyOrientation;      // board tilt / yaw / scale / pos-offset
    private bool _applyActive;           // active-cards mount offset / card scale / grid spacing
    private bool _applyPiles;            // discard/burn pile mount offset / scale / inter-pile spacing
    private bool _applyObjectives;       // items 4/6: objectives ('Aufgaben') dock offset / scale
    private bool _applyElements;         // items 4/6: element infusion ('Elemente') dock offset / scale
    private bool _applyHudWidgets;       // items 4/6: gear / follow-pin / round-readout offsets (in place)
    private bool _applyCluster;          // items 4/6: turn-flow ButtonCluster offset / scale
    private bool _applyDecision;         // item C: shared decision-dock offset / scale
    private bool _applyFan;              // GLOBAL hand-fan geometry (step / arc / radius / hover-split)

    /// <summary>Subscribe/unsubscribe every per-board tuning entry's SettingChanged (both boards' menu AND cfg edits live-apply).</summary>
    private void SubscribeBoardTuning(bool subscribe)
    {
        foreach (ControlBoard b in System.Enum.GetValues(typeof(ControlBoard)))
        {
            if (subscribe)
            {
                CardsConfig.RestButtonOffset(b).SettingChanged += OnControlOffsetChanged;
                CardsConfig.ConfirmUndoOffset(b).SettingChanged += OnControlOffsetChanged;
                CardsConfig.RestButtonSpacing(b).SettingChanged += OnControlOffsetChanged;   // spacing = in-place move
                CardsConfig.GenericButtonSpacing(b).SettingChanged += OnControlOffsetChanged;
                CardsConfig.RestButtonDiameter(b).SettingChanged += OnControlSizeChanged;
                CardsConfig.ConfirmUndoSize(b).SettingChanged += OnControlSizeChanged;
                CardsConfig.RestButtonShape(b).SettingChanged += OnControlSizeChanged;        // shape = rebuild the caps
                CardsConfig.GenericButtonShape(b).SettingChanged += OnControlSizeChanged;
                CardsConfig.SlotOverlayOffset(b).SettingChanged += OnOverlayOffsetChanged;
                CardsConfig.SlotOverlaySpacing(b).SettingChanged += OnOverlayOffsetChanged; // item 1: overlay pair spacing
                CardsConfig.InitiativeOffset(b).SettingChanged += OnInitiativeOffsetChanged;
                CardsConfig.BoardTilt(b).SettingChanged += OnOrientationChanged;
                CardsConfig.BoardYaw(b).SettingChanged += OnOrientationChanged;
                CardsConfig.BoardScale(b).SettingChanged += OnOrientationChanged;
                CardsConfig.BoardPosOffset(b).SettingChanged += OnOrientationChanged;
                CardsConfig.ActiveOffset(b).SettingChanged += OnActiveTuningChanged;
                CardsConfig.ActiveCardScale(b).SettingChanged += OnActiveTuningChanged;
                CardsConfig.ActiveGridSpacing(b).SettingChanged += OnActiveTuningChanged;
                CardsConfig.PileOffset(b).SettingChanged += OnPilesTuningChanged;
                CardsConfig.PileScale(b).SettingChanged += OnPilesTuningChanged;
                CardsConfig.PileSpacing(b).SettingChanged += OnPilesTuningChanged;
                CardsConfig.ObjectivesOffset(b).SettingChanged += OnObjectivesTuningChanged;
                CardsConfig.ObjectivesScale(b).SettingChanged += OnObjectivesTuningChanged;
                CardsConfig.ElementsOffset(b).SettingChanged += OnElementsTuningChanged;
                CardsConfig.ElementsScale(b).SettingChanged += OnElementsTuningChanged;
                CardsConfig.VRSettingsOffset(b).SettingChanged += OnHudWidgetTuningChanged;
                CardsConfig.PinOffset(b).SettingChanged += OnHudWidgetTuningChanged;
                CardsConfig.ReadoutOffset(b).SettingChanged += OnHudWidgetTuningChanged;
                CardsConfig.ClusterOffset(b).SettingChanged += OnClusterTuningChanged;
                CardsConfig.ClusterScale(b).SettingChanged += OnClusterTuningChanged;
                CardsConfig.DecisionOffset(b).SettingChanged += OnDecisionTuningChanged;
                CardsConfig.DecisionScale(b).SettingChanged += OnDecisionTuningChanged;
            }
            else
            {
                CardsConfig.RestButtonOffset(b).SettingChanged -= OnControlOffsetChanged;
                CardsConfig.ConfirmUndoOffset(b).SettingChanged -= OnControlOffsetChanged;
                CardsConfig.RestButtonSpacing(b).SettingChanged -= OnControlOffsetChanged;
                CardsConfig.GenericButtonSpacing(b).SettingChanged -= OnControlOffsetChanged;
                CardsConfig.RestButtonDiameter(b).SettingChanged -= OnControlSizeChanged;
                CardsConfig.ConfirmUndoSize(b).SettingChanged -= OnControlSizeChanged;
                CardsConfig.RestButtonShape(b).SettingChanged -= OnControlSizeChanged;
                CardsConfig.GenericButtonShape(b).SettingChanged -= OnControlSizeChanged;
                CardsConfig.SlotOverlayOffset(b).SettingChanged -= OnOverlayOffsetChanged;
                CardsConfig.SlotOverlaySpacing(b).SettingChanged -= OnOverlayOffsetChanged; // item 1: overlay pair spacing
                CardsConfig.InitiativeOffset(b).SettingChanged -= OnInitiativeOffsetChanged;
                CardsConfig.BoardTilt(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.BoardYaw(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.BoardScale(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.BoardPosOffset(b).SettingChanged -= OnOrientationChanged;
                CardsConfig.ActiveOffset(b).SettingChanged -= OnActiveTuningChanged;
                CardsConfig.ActiveCardScale(b).SettingChanged -= OnActiveTuningChanged;
                CardsConfig.ActiveGridSpacing(b).SettingChanged -= OnActiveTuningChanged;
                CardsConfig.PileOffset(b).SettingChanged -= OnPilesTuningChanged;
                CardsConfig.PileScale(b).SettingChanged -= OnPilesTuningChanged;
                CardsConfig.PileSpacing(b).SettingChanged -= OnPilesTuningChanged;
                CardsConfig.ObjectivesOffset(b).SettingChanged -= OnObjectivesTuningChanged;
                CardsConfig.ObjectivesScale(b).SettingChanged -= OnObjectivesTuningChanged;
                CardsConfig.ElementsOffset(b).SettingChanged -= OnElementsTuningChanged;
                CardsConfig.ElementsScale(b).SettingChanged -= OnElementsTuningChanged;
                CardsConfig.VRSettingsOffset(b).SettingChanged -= OnHudWidgetTuningChanged;
                CardsConfig.PinOffset(b).SettingChanged -= OnHudWidgetTuningChanged;
                CardsConfig.ReadoutOffset(b).SettingChanged -= OnHudWidgetTuningChanged;
                CardsConfig.ClusterOffset(b).SettingChanged -= OnClusterTuningChanged;
                CardsConfig.ClusterScale(b).SettingChanged -= OnClusterTuningChanged;
                CardsConfig.DecisionOffset(b).SettingChanged -= OnDecisionTuningChanged;
                CardsConfig.DecisionScale(b).SettingChanged -= OnDecisionTuningChanged;
            }
        }
    }

    private void OnControlOffsetChanged(object sender, System.EventArgs e) => _applyControlOffsets = true;
    private void OnControlSizeChanged(object sender, System.EventArgs e) => _applyControlRebuild = true;
    private void OnOverlayOffsetChanged(object sender, System.EventArgs e) => _applyOverlayOffset = true;
    private void OnInitiativeOffsetChanged(object sender, System.EventArgs e) => _applyInitiativeOffset = true;
    private void OnOrientationChanged(object sender, System.EventArgs e) => _applyOrientation = true;
    private void OnActiveTuningChanged(object sender, System.EventArgs e) => _applyActive = true;
    private void OnPilesTuningChanged(object sender, System.EventArgs e) => _applyPiles = true;
    private void OnObjectivesTuningChanged(object sender, System.EventArgs e) => _applyObjectives = true;
    private void OnElementsTuningChanged(object sender, System.EventArgs e) => _applyElements = true;
    private void OnHudWidgetTuningChanged(object sender, System.EventArgs e) => _applyHudWidgets = true;
    private void OnClusterTuningChanged(object sender, System.EventArgs e) => _applyCluster = true;
    private void OnDecisionTuningChanged(object sender, System.EventArgs e) => _applyDecision = true;
    private void OnFanTuningChanged(object sender, System.EventArgs e) => _applyFan = true;

    /// <summary>
    /// PART F: consume the per-board tuning dirty flags on the main thread and re-apply the
    /// matching element to the live board (offsets in place, sizes by rebuild, orientation via
    /// ReapplyOrientation). Every apply logs the element + the new value. No-op with no board.
    /// </summary>
    private void ApplyBoardTuning()
    {
        if (_tray.Root == null)
            return;
        ControlBoard b = CardsConfig.CurrentBoard;

        if (_applyControlRebuild)
        {
            _applyControlRebuild = false;
            _applyControlOffsets = false; // the rebuild re-reads the offsets from config
            _rest.Destroy();
            _tray.RebuildAttachedControls(); // rebuilds Confirm/Undo AND purges the dead rest laser targets
            _rest.EnsureBuilt(_tray);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: rebuilt Generic Confirm/Undo ({CardsConfig.GenericButtonShape(b).Value}, " +
                                $"size {CardsConfig.ConfirmUndoSize(b).Value:F3} m) + Rest buttons ({CardsConfig.RestButtonShape(b).Value}, " +
                                $"diameter {CardsConfig.RestButtonDiameter(b).Value:F3} m).");
        }
        if (_applyControlOffsets)
        {
            _applyControlOffsets = false;
            _rest.SetOffset(CardsConfig.RestButtonOffset(b).Value, CardsConfig.RestButtonSpacing(b).Value);
            _tray.SetConfirmUndoOffset(CardsConfig.ConfirmUndoOffset(b).Value, CardsConfig.GenericButtonSpacing(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: rest offset {CardsConfig.RestButtonOffset(b).Value} " +
                                $"(spacing {CardsConfig.RestButtonSpacing(b).Value:F3} m), confirm/undo offset " +
                                $"{CardsConfig.ConfirmUndoOffset(b).Value} (spacing {CardsConfig.GenericButtonSpacing(b).Value:F3} m).");
        }
        if (_applyOverlayOffset)
        {
            _applyOverlayOffset = false;
            _tray.SetOverlayOffset(CardsConfig.SlotOverlayOffset(b).Value, CardsConfig.SlotOverlaySpacing(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: slot overlay offset {CardsConfig.SlotOverlayOffset(b).Value} " +
                                $"(spacing {CardsConfig.SlotOverlaySpacing(b).Value:F3} m).");
        }
        if (_applyInitiativeOffset)
        {
            _applyInitiativeOffset = false;
            _tray.SetInitiativeOffset(CardsConfig.InitiativeOffset(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: initiative offset {CardsConfig.InitiativeOffset(b).Value}.");
        }
        if (_applyOrientation)
        {
            _applyOrientation = false;
            _expectedPoseChange = "user-settings (orientation tuning)"; // sanctioned move (issue C watchdog)
            _tray.ReapplyOrientation();
            VRLog.Info("Cards", $"Debug live-apply [{b}]: orientation tilt {CardsConfig.BoardTilt(b).Value:F0}°, " +
                                $"yaw {CardsConfig.BoardYaw(b).Value:F0}°, scale {CardsConfig.BoardScale(b).Value:F2}×, " +
                                $"posOffset {CardsConfig.BoardPosOffset(b).Value}.");
        }
        if (_applyActive)
        {
            _applyActive = false;
            _tray.SetActiveOffset(CardsConfig.ActiveOffset(b).Value); // move the mount in place
            _active.ApplyLayout();                                    // re-lay from the per-board scale + grid step
            VRLog.Info("Cards", $"Debug live-apply [{b}]: active offset {CardsConfig.ActiveOffset(b).Value}, " +
                                $"card scale {CardsConfig.ActiveCardScale(b).Value:F2}×, grid step {CardsConfig.ActiveGridSpacing(b).Value}.");
        }
        if (_applyPiles)
        {
            _applyPiles = false;
            _tray.SetPileOffset(CardsConfig.PileOffset(b).Value); // move the mount in place
            _piles.ApplyLayout();                                 // re-seat both stacks (scale + inter-pile spacing)
            VRLog.Info("Cards", $"Debug live-apply [{b}]: pile offset {CardsConfig.PileOffset(b).Value}, " +
                                $"scale {CardsConfig.PileScale(b).Value:F2}×, spacing {CardsConfig.PileSpacing(b).Value:F3} m.");
        }
        if (_applyObjectives)
        {
            _applyObjectives = false;
            // Move + resize the docked panel in place: the surface pose-follows the mount's
            // position AND lossyScale each tick, so no panel reconvert is needed.
            _tray.SetObjectivesLayout(CardsConfig.ObjectivesOffset(b).Value, CardsConfig.ObjectivesScale(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: objectives offset {CardsConfig.ObjectivesOffset(b).Value}, " +
                                $"scale {CardsConfig.ObjectivesScale(b).Value:F2}×.");
        }
        if (_applyElements)
        {
            _applyElements = false;
            _tray.SetElementsLayout(CardsConfig.ElementsOffset(b).Value, CardsConfig.ElementsScale(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: elements offset {CardsConfig.ElementsOffset(b).Value}, " +
                                $"scale {CardsConfig.ElementsScale(b).Value:F2}×.");
        }
        if (_applyHudWidgets)
        {
            _applyHudWidgets = false;
            _tray.SetVRSettingsOffset(CardsConfig.VRSettingsOffset(b).Value);
            _tray.SetPinOffset(CardsConfig.PinOffset(b).Value);
            _tray.SetReadoutOffset(CardsConfig.ReadoutOffset(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: gear offset {CardsConfig.VRSettingsOffset(b).Value}, " +
                                $"pin offset {CardsConfig.PinOffset(b).Value}, readout offset {CardsConfig.ReadoutOffset(b).Value}.");
        }
        if (_applyCluster)
        {
            _applyCluster = false;
            _tray.SetClusterLayout(CardsConfig.ClusterOffset(b).Value, CardsConfig.ClusterScale(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: cluster offset {CardsConfig.ClusterOffset(b).Value}, " +
                                $"scale {CardsConfig.ClusterScale(b).Value:F2}×.");
        }
        if (_applyDecision)
        {
            _applyDecision = false;
            _tray.SetDecisionLayout(CardsConfig.DecisionOffset(b).Value, CardsConfig.DecisionScale(b).Value);
            VRLog.Info("Cards", $"Debug live-apply [{b}]: decision-dock offset {CardsConfig.DecisionOffset(b).Value}, " +
                                $"scale {CardsConfig.DecisionScale(b).Value:F2}×.");
        }
    }

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
        ClearActiveHover();
        ClearInitiativeTodo(); // item 6: clear any lingering initiative to-do glow on teardown
        _liveGrabs.Clear();
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
            _liveGrabs.Remove(card); // recycled mid-grab: its release must not route a drop
            if (_fanOriginCards.Remove(card) && ReferenceEquals(_insertHighlightCard, card))
                ClearFanInsertion(); // reorder subject recycled under us
        }
        _factory.ReleaseWidget(widget);
        _dirty = true;
    }

    // ------------------------------------------------------------------ update --

    private void Update()
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
            ClearActiveHover();
            ClearInitiativeTodo(); // item 6: drop the initiative to-do glow while hands are down
            _tray.SetVisible(false);
            _half.SetVisible(false);
            return;
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
                                $"split ×{CardsConfig.FanHoverSplitScale.Value:F2}.");
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
            ClearActiveHover();
        }
        else
        {
            UpdateFanLaser();
            UpdateBoardLaser();
            UpdateBrowseLaser();
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
        }
        UpdateWantedSlots(_fakeActive ? null : hand); // test #28: steady "wanted slot" hint
        UpdateInitiativeTodo(); // item 6: glow the initiative-order characters who still owe cards

        PollShortRest(_fakeActive ? null : hand); // redraw-swaps ShortRestedCard with no mode change
        if (!_fakeActive)
            PumpLongRestTurn(); // long-rest turn: drive the game's own PERFORM LONG REST flow (re-armed every tick)
        LogLongRestState(_fakeActive ? null : hand); // test #28: prove the long-rest state transitions
        LogFanState(hand);
        LogActionSelectionState(_fakeActive ? null : hand); // second-character action deadlock diagnostic
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
        CardsHandUI? hand = CardsGameApi.ActionSelectionHand() ?? CardsGameApi.ActiveHand();
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

        // Live-tunable gate feel (roll gate v3): PalmGate measures Demeo's own roll dot —
        // the hand's RIGHT axis tilt toward world up — asin-mapped to DEGREES (0 knuckles-up
        // flat, 90 palm fully toward the face; pitch/yaw of the arm are irrelevant BY
        // CONSTRUCTION). Thresholds: [Cards] RevealEnterDegrees/RevealExitDegrees, defaults
        // 60° enter / 45° exit — a comfortable supination with a hysteresis dead band so the
        // gate cannot chatter. Live-tunable from the debug menu's Fan category.
        PalmGate gate = _gateHand.PalmGate;
        gate.EnterDegrees = CardsConfig.RevealEnterDegrees.Value;
        gate.ExitDegrees = CardsConfig.RevealExitDegrees.Value;
        gate.UseDevicePalmNormal = !_gateHand.IsSimulated; // sim hands pose the rig directly
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
    private static void PlayCardSound(string item, Transform at)
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

    // ------------------------------------------------------------------ fan laser --

    private VRCard? _laserHover;

    /// <summary>
    /// Fan-grab reliability (T2): unscaled-time deadline until which the LAST laser-hovered
    /// fan card still wins the trigger after the beam slips off it. The trigger PULL itself
    /// jerks the aim ray (hardware: grabs out of the open fan missed every 2nd-3rd try) —
    /// the exact frame of TriggerDown the ray often no longer touches the narrow card
    /// strip, the hover cleared, and the trigger fell through to nothing (the proximity
    /// fallback was ALSO deferred: our own fan clamp from the previous frame keeps
    /// <c>Ray.HasFreshUiHit</c> fresh, and ProximityGrabber yields on that flag). A short
    /// grace keeps the highlighted card the trigger's owner across the pull.
    /// </summary>
    private float _laserHoverGraceUntil;

    /// <summary>How long (s, unscaled) a slipped-off fan hover still owns the trigger.
    /// Long enough to bridge a trigger-pull jerk (a few frames), short enough that the
    /// beam clamp visibly releases as soon as the player genuinely points away.</summary>
    private const float FanHoverGraceSeconds = 0.15f;

    /// <summary>One-shot session log guard for the fan grab-rescue confirmation line.</summary>
    private static bool s_loggedFanRescue;

    /// <summary>
    /// Demeo pluck (P6): the dominant hand's laser highlights fan cards (pop + one
    /// haptic tick per card change) and TriggerDown pulls the pointed card into the
    /// dominant hand (released on TriggerUp). Proximity grab keeps working unchanged.
    ///
    /// T2 (fan grab misses ~every 2nd-3rd try): when the ray does NOT land on a fan card
    /// this frame, two rescue paths mirror the tray's LIFT-PRIORITY accept (task #2)
    /// before the hover is dropped — the card the player was visibly promised wins the
    /// trigger instead of the pull falling through:
    /// 1. Proximity lift-priority: the dominant hand's proximity HIGHLIGHT is a fan card
    ///    (the popped card under the reaching hand — the affordance promise). Beam clamps
    ///    to it, TriggerDown grabs exactly it. Reaching into the fan previously lost the
    ///    trigger to Ray.HasFreshUiHit arbitration (see ProximityGrabber.Tick): the fan
    ///    clamp raises the flag every hovered frame, so the proximity path NEVER fired
    ///    while the laser was anywhere near the fan.
    /// 2. Hover grace: the last laser-hovered card still wins for a short window
    ///    (<see cref="FanHoverGraceSeconds"/>) after the beam slips off — the trigger
    ///    pull itself jerks the ray off the narrow card strip on the press frame.
    /// Both yield to a live game-UI hit (RayUgui) exactly like the tray pattern, so a
    /// UI click can never double-fire with a grab; the beam clamp keeps suppressing the
    /// board far-click (Cards ticks before Board). Single-winner by construction.
    /// </summary>
    private void UpdateFanLaser()
    {
        VRHand? dom = VRHands.Primary;
        if (!_fan.IsOpen || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Enabled || dom.Grabber.Held != null)
        {
            ClearLaserHover();
            return;
        }

        PickPose pick = dom.Ray.Current;
        if (!_fan.TryRaycast(pick.Origin, pick.Direction, _laserHover, out VRCard? card, out Vector3 point, out float dist)
            || card == null
            || (dom.RayUgui.HasHit && dom.RayUgui.HitDistance < dist))
        {
            // T2 rescue paths (see method doc): the highlighted/just-hovered fan card wins
            // the trigger even though the ray misses it this frame. Yields to a live game-UI
            // hit like the tray lift-priority accept.
            if (!dom.RayUgui.HasHit)
            {
                VRCard? rescue = null;
                if (dom.Grabber.Highlighted is VRCard prox && _fan.Contains(prox) && !prox.IsHeld)
                    rescue = prox; // 1. the popped card under the reaching hand
                else if (_laserHover != null && !_laserHover.IsHeld && _fan.Contains(_laserHover)
                         && Time.unscaledTime <= _laserHoverGraceUntil)
                    rescue = _laserHover; // 2. trigger-pull jerk grace

                if (rescue != null)
                {
                    // A stale laser pop on a DIFFERENT card than the rescue winner drops now.
                    if (_laserHover != null && !ReferenceEquals(_laserHover, rescue))
                        ClearLaserHover();
                    // Clamp the beam onto the winner (telegraphs the grab target, keeps the
                    // board far-click + proximity double-path suppressed via HasFreshUiHit).
                    dom.Ray.UiHitOverride = rescue.transform.position;
                    if (dom.TriggerDown && rescue.CanGrab)
                    {
                        if (!s_loggedFanRescue)
                        {
                            s_loggedFanRescue = true;
                            VRLog.Info("Cards", "Fan grab RESCUE active (T2): highlighted fan card " +
                                                "won a trigger whose ray missed the card strip " +
                                                "(lift-priority / pull-jerk grace).");
                        }
                        ClearLaserHover();
                        dom.Grabber.ForceGrab(rescue, releaseOnTriggerUp: true);
                    }
                    return;
                }
            }
            ClearLaserHover();
            return;
        }

        if (!ReferenceEquals(card, _laserHover))
        {
            ClearLaserHover();
            _laserHover = card;
            card.SetLaserHover(true);
            dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on card change
        }
        _laserHoverGraceUntil = Time.unscaledTime + FanHoverGraceSeconds; // refresh the pull-jerk grace

        // Clamp the visible beam to the card — also raises Ray.HasFreshUiHit, which
        // suppresses the board far-click for this trigger press.
        dom.Ray.UiHitOverride = point;

        if (dom.TriggerDown && card.CanGrab)
        {
            VRCard grab = card;
            ClearLaserHover();
            dom.Grabber.ForceGrab(grab, releaseOnTriggerUp: true);
        }
    }

    private void ClearLaserHover()
    {
        if (_laserHover == null)
            return;
        _laserHover.SetLaserHover(false);
        _laserHover = null;
    }

    // ------------------------------------------------------------------ fan hover split --

    private int _fanHoverIndex = -1;

    /// <summary>
    /// G6/G2 (DEMEO-HANDS-CARDS §5 Group D): drive the fan's whole-hand hover SPLIT from
    /// EITHER input source. The dominant-hand laser hover is primary (our controller path);
    /// only when no card is laser-hovered does the dominant hand's proximity highlight —
    /// its finger near a fan card, Demeo-style — split the fan instead. Purely the VISUAL
    /// split: pluck/select still route through <see cref="UpdateFanLaser"/> and the
    /// proximity grabber unchanged. The index is resolved against the fan's OWN card order
    /// (<see cref="CardFan.Cards"/> — the exact list <c>SetHovered</c> indexes into, so it
    /// cannot drift from the driver's <c>_fanBuffer</c> after a mid-frame grab/return) and
    /// pushed only on change. Allocation-free.
    /// </summary>
    private void UpdateFanHoverSplit()
    {
        if (!_fan.IsOpen)
        {
            if (_fanHoverIndex != -1)
            {
                _fanHoverIndex = -1;
                _fan.SetHovered(-1);
            }
            return;
        }

        // Precedence: laser wins whenever a fan card is laser-hovered (primary controller
        // path); the hand-contact arbitration winner fills in when the laser hovers nothing
        // (issue A: the split always opens around the ONE lifted card — the proximity
        // highlight follows the same winner via AllowsHand, so it stays the fallback for
        // the first frame after a winner change).
        VRCard? hovered = _laserHover;
        if (hovered == null && _handContactWinner != null && _fan.Contains(_handContactWinner))
            hovered = _handContactWinner;
        if (hovered == null)
        {
            VRHand? dom = VRHands.Primary;
            if (dom != null && dom != _gateHand && dom.Grabber.Held == null
                && dom.Grabber.Highlighted is VRCard proximityCard && _fan.Contains(proximityCard))
                hovered = proximityCard;
        }

        int index = hovered != null ? FanIndexOf(hovered) : -1;
        if (index != _fanHoverIndex)
        {
            _fanHoverIndex = index;
            _fan.SetHovered(index);
        }
    }

    /// <summary>Index of <paramref name="card"/> in the fan's card order, or -1. Allocation-free.</summary>
    private int FanIndexOf(VRCard card)
    {
        IReadOnlyList<VRCard> cards = _fan.Cards;
        for (int i = 0; i < cards.Count; i++)
        {
            if (ReferenceEquals(cards[i], card))
                return i;
        }
        return -1;
    }

    // ------------------------------------------- hand-contact single winner (issue A/B) --

    /// <summary>Fingertip contact reach (m, scale 1) — mirrors CardFan.FingertipHoverReach.</summary>
    private const float ContactTipReach = 0.035f;

    /// <summary>Palm contact reach (m, scale 1) — mirrors ProximityGrabber.ReachMeters.</summary>
    private const float ContactPalmReach = 0.13f;

    /// <summary>Incumbent hysteresis (m, scale 1): a rival card must be this much CLOSER to the
    /// hand than the currently lifted card to steal the lift — the winner cannot flutter at
    /// strip boundaries while the hand sweeps through the fan.</summary>
    private const float ContactStickyMargin = 0.02f;

    /// <summary>The single card the free hand is currently "in contact with" (null = none).</summary>
    private VRCard? _handContactWinner;

    /// <summary>Cards currently pop-suppressed by the arbitration — cleared and re-filled every
    /// tick so a card leaving the fan/dock pools can never keep a stale suppression.</summary>
    private readonly List<VRCard> _contactSuppressed = new(24);

    /// <summary>One-shot session log guard for the arbitration confirmation line.</summary>
    private static bool s_loggedContactArbitration;

    /// <summary>
    /// USER ISSUE A (fan sweep lifts several cards) + B (dock highlight fights): per-tick
    /// SINGLE-WINNER arbitration over every card the free (dominant) hand can touch — the
    /// open fan's cards plus the slot-docked/pick-field cards. Among all cards in contact
    /// range (index tip within <see cref="ContactTipReach"/> OR palm within
    /// <see cref="ContactPalmReach"/> of the card's grab collider), exactly ONE wins: the
    /// closest by hand distance, with a <see cref="ContactStickyMargin"/> hysteresis bonus
    /// for the incumbent so the lift never flutters at strip boundaries. Every other pool
    /// card is suppressed (<see cref="VRCard.SetHandPopSuppressed"/>): its hand-driven pop
    /// drops immediately AND it refuses the hand in <c>AllowsHand</c>, so the
    /// ProximityGrabber's highlight — and therefore the trigger grab — lands on the same
    /// single winner. The laser-hovered fan/tray card is never suppressed (the laser path
    /// already arbitrates itself and its pluck must keep working). Allocation-free.
    /// </summary>
    private void UpdateHandContactArbitration()
    {
        VRHand? dom = VRHands.Primary;
        VRCard.HandArbitrationHand = dom;

        VRCard? winner = null;
        if (dom != null && !ReferenceEquals(dom, _gateHand) && dom.HasPose
            && dom.Grabber.Held == null && !_modalInputBlocked)
        {
            Vector3 tip = dom.Rig.IndexTip.position;
            Vector3 palm = dom.Rig.PalmCenter.position;
            float scale = dom.WorldScale;
            float tipReach = ContactTipReach * scale;
            float palmReach = ContactPalmReach * scale;
            float sticky = ContactStickyMargin * scale;
            float best = float.MaxValue;

            if (_fan.IsOpen)
            {
                IReadOnlyList<VRCard> fanCards = _fan.Cards;
                for (int i = 0; i < fanCards.Count; i++)
                    ScoreContact(fanCards[i], tip, palm, tipReach, palmReach, sticky, ref winner, ref best);
            }
            if (_tray.IsVisible)
            {
                ScoreContact(_tray.Occupant(0), tip, palm, tipReach, palmReach, sticky, ref winner, ref best);
                ScoreContact(_tray.Occupant(1), tip, palm, tipReach, palmReach, sticky, ref winner, ref best);
                for (int i = 0; i < _fieldCards.Count; i++)
                    ScoreContact(_fieldCards[i], tip, palm, tipReach, palmReach, sticky, ref winner, ref best);
            }
        }

        if (!ReferenceEquals(winner, _handContactWinner))
        {
            _handContactWinner = winner;
            if (winner != null && !s_loggedContactArbitration)
            {
                s_loggedContactArbitration = true;
                VRLog.Info("Cards", "Hand-contact SINGLE-WINNER arbitration active (issue A/B): only " +
                                    "the closest touched fan/dock card lifts; sweeping the hand can " +
                                    "no longer raise multiple cards, and the grab follows the winner.");
            }
        }

        // Re-derive the suppression set from scratch every tick (stale-flag proof: a card
        // that left the pools mid-frame is cleared here or by its own OnDisable).
        for (int i = 0; i < _contactSuppressed.Count; i++)
        {
            if (_contactSuppressed[i] != null)
                _contactSuppressed[i].SetHandPopSuppressed(false);
        }
        _contactSuppressed.Clear();
        if (winner == null)
            return;
        if (_fan.IsOpen)
        {
            IReadOnlyList<VRCard> fanCards = _fan.Cards;
            for (int i = 0; i < fanCards.Count; i++)
                SuppressContactLoser(fanCards[i], winner);
        }
        if (_tray.IsVisible)
        {
            SuppressContactLoser(_tray.Occupant(0), winner);
            SuppressContactLoser(_tray.Occupant(1), winner);
            for (int i = 0; i < _fieldCards.Count; i++)
                SuppressContactLoser(_fieldCards[i], winner);
        }
    }

    /// <summary>Contact score of <paramref name="card"/>: min distance of index tip / palm center
    /// to its grab collider, incumbent bonus applied; out of both reaches = no candidate.</summary>
    private void ScoreContact(VRCard? card, Vector3 tip, Vector3 palm, float tipReach,
        float palmReach, float sticky, ref VRCard? winner, ref float best)
    {
        if (card == null || card.IsHeld)
            return;
        if (!card.TryFingertipDistance(tip, out float tipDist)
            || !card.TryFingertipDistance(palm, out float palmDist))
            return;
        if (tipDist > tipReach && palmDist > palmReach)
            return;
        float score = Mathf.Min(tipDist, palmDist);
        if (ReferenceEquals(card, _handContactWinner))
            score -= sticky; // hysteresis: the current lift holds until a rival is decisively closer
        if (score < best)
        {
            best = score;
            winner = card;
        }
    }

    /// <summary>Suppress a pool card that lost the contact arbitration. The laser-hovered
    /// fan/tray card is exempt — laser hover/pluck must keep working unchanged.</summary>
    private void SuppressContactLoser(VRCard? card, VRCard winner)
    {
        if (card == null || card.IsHeld || ReferenceEquals(card, winner)
            || ReferenceEquals(card, _laserHover) || ReferenceEquals(card, _trayCardHover))
            return;
        card.SetHandPopSuppressed(true);
        _contactSuppressed.Add(card);
    }

    // ------------------------------------------------------------------ board laser --

    private IPokeable? _boardHover;
    private VRHand? _boardHoverHand;
    private VRCard? _trayCardHover;

    /// <summary>
    /// P7 (test #10): laser support for every control-board element — the dominant
    /// hand's ray is tested geometrically against the tray's registered pokeables
    /// (Collider.Raycast works on triggers, no physics-layer coupling) and against
    /// the two slotted cards. Hover clamps the beam (UiHitOverride, which also
    /// suppresses the board far-click); TriggerDown pokes the element or plucks the
    /// card into the hand. Fan laser wins when both apply. No allocations.
    /// </summary>
    private void UpdateBoardLaser()
    {
        VRHand? dom = VRHands.Primary;
        if (!_tray.IsVisible || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Enabled || dom.Grabber.Held != null || _laserHover != null)
        {
            ClearBoardHover();
            return;
        }

        // Task #2 (tray-card grab radius) — LIFT-PRIORITY accept. The proximity highlight
        // (ProximityGrabber, 0.13 m palm reach off the card collider) is exactly what
        // hover-LIFTS a slotted card, so the accept volume for the trigger-grab is the
        // hover-lift radius itself — the lift IS the affordance promise. Without this,
        // the grab often failed even though the card was visibly lifted: the trigger is
        // shared with the laser click, and the beam near-missing onto a board element
        // (CONFIRM/UNDO/rest/pile — grab attempts point INTO the board by nature) or
        // onto the OTHER slotted card swallowed the pull as an element press / wrong-
        // card pluck (ProximityGrabber defers on Ray.HasFreshUiHit). While the free
        // hand's highlight IS a tray-docked card: suppress the board hover/click for
        // the frame, clamp the beam onto the lifted card (telegraphs the winner), and
        // grab exactly that card on TriggerDown. Single-winner by construction —
        // Highlighted is unique per hand and only a SLOTTED card takes this path; fan
        // cards, figures and non-lifted cards are untouched. Yields to a live game-UI
        // hit (RayUgui) like every path below so a docked game-widget click can never
        // double-fire with a grab.
        // Task #2 follow-up: pick/field cards docked in a slot recess (LoseCard/recover
        // flows) take the same lift-priority accept as the two played-slot occupants —
        // they live in the same recesses, carry the same dock grab apron, and suffered
        // the same trigger fall-through to board actions.
        if (dom.Grabber.Highlighted is VRCard lifted
            && (_tray.ContainsCard(lifted) || _fieldCards.Contains(lifted))
            && !dom.RayUgui.HasHit)
        {
            ClearBoardHover();
            dom.Ray.UiHitOverride = lifted.transform.position;
            if (dom.TriggerDown && lifted.CanGrab)
            {
                VRLog.Info("Cards", "Board: hover-LIFTED slot card trigger-grabbed " +
                                    "(lift-priority accept — beam near-miss suppressed).");
                dom.Grabber.ForceGrab(lifted, releaseOnTriggerUp: true);
            }
            return;
        }

        PickPose pick = dom.Ray.Current;
        var ray = new Ray(pick.Origin, pick.Direction);
        float maxDist = 3f * dom.WorldScale;

        IPokeable? best = null;
        Vector3 bestPoint = default;
        float bestDist = maxDist;
        var targets = _tray.LaserTargets;
        for (int i = 0; i < targets.Count; i++)
        {
            Collider col = targets[i].Collider;
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                continue;
            if (col.Raycast(ray, out RaycastHit hit, bestDist))
            {
                best = targets[i].Target;
                bestPoint = hit.point;
                bestDist = hit.distance;
            }
        }

        // Slotted cards: pluck them back with the laser, like fan cards.
        bool cardWins = _tray.TryRaycastCards(pick.Origin, pick.Direction,
            out VRCard? card, out Vector3 cardPoint, out float cardDist) && cardDist < bestDist;

        // The game's own UI (RayUgui) closer than everything → neither hovers.
        float nearest = cardWins ? cardDist : best != null ? bestDist : float.PositiveInfinity;
        if (float.IsPositiveInfinity(nearest) || (dom.RayUgui.HasHit && dom.RayUgui.HitDistance < nearest))
        {
            ClearBoardHover();
            return;
        }

        if (cardWins)
        {
            ClearBoardPokeHover();
            if (!ReferenceEquals(card, _trayCardHover))
            {
                ClearTrayCardHover();
                _trayCardHover = card;
                card!.SetLaserHover(true);
                dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on change
            }
            dom.Ray.UiHitOverride = cardPoint;
            if (dom.TriggerDown && card!.CanGrab)
            {
                VRCard grab = card;
                ClearTrayCardHover();
                VRLog.Info("Cards", "Board: slotted card laser-plucked.");
                dom.Grabber.ForceGrab(grab, releaseOnTriggerUp: true);
            }
            return;
        }

        ClearTrayCardHover();
        if (!ReferenceEquals(best, _boardHover))
        {
            ClearBoardPokeHover();
            _boardHover = best;
            _boardHoverHand = dom;
            best!.OnPokeEnter(dom); // elements do their own hover haptic/tint
        }
        dom.Ray.UiHitOverride = bestPoint;
        if (dom.TriggerDown)
        {
            // Route through Press for buttons so the log carries source=laser and
            // rejected presses explain their gate (test #14); other pokeables (badge,
            // rest tokens) keep the plain OnPoke path.
            if (best is PlayTray.BoardButton button)
            {
                // Item 8: any board button press is a foreign interaction (the Cards
                // events cover CONFIRM/UNDO/rest via their handlers regardless of
                // input modality; this also catches board buttons with no Cards event,
                // e.g. settings/recenter, on the laser path). Pile stacks are NOT
                // BoardButtons — they route through OnPoke below and manage the browse.
                ForeignInteraction("board button");
                button.Press(dom, "laser");
            }
            else if (best is PileViewer.PileStack pile)
            {
                // Pile stacks: dedicated laser path — OnPoke now carries the finger's
                // entry-only re-arm gate (double-trigger fix), which must never block a
                // deliberate second laser click while the beam rests on the stack.
                pile.LaserToggle(dom);
            }
            else
            {
                VRLog.Info("Cards", $"Board: laser click → {(best as MonoBehaviour)?.name ?? best!.ToString()}.");
                best!.OnPoke(dom);
            }
        }
    }

    private void ClearBoardHover()
    {
        ClearBoardPokeHover();
        ClearTrayCardHover();
    }

    private void ClearBoardPokeHover()
    {
        if (_boardHover == null)
            return;
        if (_boardHoverHand != null)
            _boardHover.OnPokeExit(_boardHoverHand);
        _boardHover = null;
        _boardHoverHand = null;
    }

    private void ClearTrayCardHover()
    {
        if (_trayCardHover == null)
            return;
        _trayCardHover.SetLaserHover(false);
        _trayCardHover = null;
    }

    // ------------------------------------------------------------------ browse laser --

    private VRCard? _browseHover;

    /// <summary>
    /// Item 5 (laser-selectable pile browse): the dominant hand's ray highlights an
    /// open browse arc's cards and TriggerDown plucks the pointed card into the hand
    /// to read it close (released on TriggerUp → returns to the arc, no game state).
    /// Yields to the fan laser and the board laser — those are real interactions; the
    /// browse is a passive read layered on top. Mirrors <see cref="UpdateFanLaser"/>.
    /// </summary>
    private void UpdateBrowseLaser()
    {
        VRHand? dom = VRHands.Primary;
        if (!_browser.IsOpen || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Enabled || dom.Grabber.Held != null
            || _laserHover != null || _trayCardHover != null || _boardHover != null)
        {
            ClearBrowseHover();
            return;
        }

        PickPose pick = dom.Ray.Current;
        if (!_browser.TryRaycast(pick.Origin, pick.Direction, _browseHover, out VRCard? card, out Vector3 point, out float dist)
            || card == null
            || (dom.RayUgui.HasHit && dom.RayUgui.HitDistance < dist))
        {
            ClearBrowseHover();
            // ISSUE #7 click-away dismiss: a TRIGGER press that is NOT on a browse card —
            // empty space, the game board/UI, or anything the ray misses here — closes the
            // pile, through the SAME ForeignInteraction path board/card/rest presses already
            // use. Reached only when nothing else is hovered (the guard above yields to a
            // hovered fan/board/tray target, whose own grab/press routes ForeignInteraction),
            // so a real interaction still closes it there; and a trigger ONTO a browse card
            // takes the pluck path below instead, so grabbing/inspecting a pile card is unaffected.
            if (dom.TriggerDown)
                ForeignInteraction("click-away (trigger off the pile)");
            return;
        }

        if (!ReferenceEquals(card, _browseHover))
        {
            ClearBrowseHover();
            _browseHover = card;
            card.SetLaserHover(true);
            dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on card change
        }

        dom.Ray.UiHitOverride = point; // clamp beam + suppress board far-click
        if (dom.TriggerDown && card.CanGrab)
        {
            VRCard grab = card;
            ClearBrowseHover();
            dom.Grabber.ForceGrab(grab, releaseOnTriggerUp: true);
        }
    }

    private void ClearBrowseHover()
    {
        if (_browseHover == null)
            return;
        _browseHover.SetLaserHover(false);
        _browseHover = null;
    }

    // ------------------------------------------------------------------ active laser --

    private VRCard? _activeHover;

    /// <summary>
    /// Feature 6 (laser-interactable active cards): the dominant hand's ray highlights the
    /// active grid's cards and TriggerDown plucks the pointed card into the hand to read it
    /// close (released on TriggerUp → returns to the grid, no game state — see
    /// <see cref="OnCardReleased"/>). Lowest priority of the laser paths: yields to the fan
    /// (<see cref="_laserHover"/>), the tray cards/board (<see cref="_trayCardHover"/>/
    /// <see cref="_boardHover"/>) and the pile browse (<see cref="_browseHover"/>) — the
    /// active grid is a passive read layered on top, like the browse arc. Mirrors
    /// <see cref="UpdateBrowseLaser"/>.
    /// </summary>
    private void UpdateActiveLaser()
    {
        VRHand? dom = VRHands.Primary;
        if (!_active.IsShown || dom == null || dom == _gateHand || !dom.HasPose
            || !dom.Ray.Enabled || dom.Grabber.Held != null
            || _laserHover != null || _trayCardHover != null || _boardHover != null || _browseHover != null)
        {
            ClearActiveHover();
            return;
        }

        PickPose pick = dom.Ray.Current;
        if (!_active.TryRaycast(pick.Origin, pick.Direction, _activeHover, out VRCard? card, out Vector3 point, out float dist)
            || card == null
            || (dom.RayUgui.HasHit && dom.RayUgui.HitDistance < dist))
        {
            ClearActiveHover();
            return;
        }

        if (!ReferenceEquals(card, _activeHover))
        {
            ClearActiveHover();
            _activeHover = card;
            card.SetLaserHover(true);
            dom.SendHaptic(HapticPreset.HoverTick); // debounced: only on card change
        }

        dom.Ray.UiHitOverride = point; // clamp beam + suppress board far-click
        if (dom.TriggerDown && card.CanGrab)
        {
            VRCard grab = card;
            ClearActiveHover();
            dom.Grabber.ForceGrab(grab, releaseOnTriggerUp: true);
        }
    }

    private void ClearActiveHover()
    {
        if (_activeHover == null)
            return;
        _activeHover.SetLaserHover(false);
        _activeHover = null;
    }

    // ------------------------------------------------------------------ modal input-block --

    /// <summary>
    /// Menu-open gate (see <see cref="Update"/>): while a modal window floats
    /// (<see cref="WorldUI.ModalFallback.WindowModalActive"/>) force EVERY card
    /// non-poke/non-grab so nothing behind the menu can be plucked or fingertip-selected.
    /// A card already HELD when the menu opens is left alone (it stays held, like the
    /// dialog-open grab gate in <see cref="VRCard.CanGrab"/>). The normal per-card flags
    /// are restored by the next <see cref="Rebuild"/> once the menu closes.
    /// </summary>
    private void BlockCardInteractions()
    {
        IReadOnlyList<VRCard> all = _factory.All;
        for (int i = 0; i < all.Count; i++)
        {
            VRCard card = all[i];
            if (card == null || card.IsHeld)
                continue;
            card.PokeSelectEnabled = false;
            card.Grabbable = false;
        }
    }

    // ------------------------------------------------------------------ slot snap preview --

    private int _snapHighlightSlot = -1;
    private VRCard? _snapHighlightCard;
    private VRCard? _fieldHighlightCard; // pick counterpart of _snapHighlightCard (test #28)
    private int _fieldHighlightSlot = -1; // the slot _fieldHighlightCard is telegraphed into

    /// <summary>
    /// Test #13: while a card is HELD near the tray, glow the slot it would snap
    /// into on release (same accept/divert rules as OnCardReleased) and tick a
    /// haptic when the target slot changes — the drop is telegraphed, never a
    /// guess. Toggles/haptics only on change; no per-frame allocations.
    /// Test #15: the glowing slot is also THE authoritative drop target — the
    /// release path accepts it directly (see OnCardReleased), so what glows is
    /// what drops, even when the release gesture moves the hand out of radius.
    /// </summary>
    private void UpdateSlotHighlight()
    {
        // B/C: game-state overlay gate (results window / narrator dialog / scenario end —
        // see UpdateOverlayGate): no placement can commit, so no slot telegraph may glow
        // either — clear both the pick-mode and the snap highlight state and bail. The
        // read-only viewer-card suppression below stays untouched (it handles a
        // different case: browse/active cards that never slot).
        if (_overlayGateBlocked)
        {
            _tray.SetHighlightedSlot(-1);
            _fieldHighlightCard = null;
            _fieldHighlightSlot = -1;
            _snapHighlightSlot = -1;
            _snapHighlightCard = null;
            return;
        }
        // Pick modes (test #28): the candidate homes into the WANTED slot recess —
        // same telegraph contract as the play slots (glow-at-release is the primary
        // accept rule, haptic tick on edge), but the transient gold glow tracks the
        // wanted (next-empty) pick slot. Only a FRESH candidate telegraphs; a slotted
        // pick card being re-dropped does not (its release stays/unselects directly).
        CardsHandUI? pickHand = _fakeActive ? null : CurrentHand();
        bool pickMode = _tray.IsVisible && pickHand != null && IsPickMode(CardsGameApi.Mode(pickHand));
        if (pickMode)
        {
            VRCard? pickHeld = HeldCard(out VRHand? pickHolder);
            if (pickHeld != null && IsReadOnlyViewerCard(pickHeld))
            {
                pickHeld = null; // T2: browse/active viewer cards never slot — no telegraph
                pickHolder = null;
            }
            int want = PickTargetSlot();
            bool onField = pickHeld != null && _fieldCards.Contains(pickHeld);
            bool near = pickHeld != null && pickHolder != null && !onField && want >= 0
                && _tray.SlotNear(pickHeld.transform.position, pickHolder.Rig.PalmCenter.position) >= 0;
            int glowSlot = near ? want : -1;
            _tray.SetHighlightedSlot(glowSlot);
            VRCard? target = glowSlot >= 0 ? pickHeld : null;
            if (!ReferenceEquals(target, _fieldHighlightCard))
            {
                _fieldHighlightCard = target;
                _fieldHighlightSlot = glowSlot;
                if (target != null && pickHolder != null)
                    pickHolder.SendHaptic(HapticPreset.HoverTick); // debounced: only on edge
            }
            _snapHighlightSlot = -1;
            _snapHighlightCard = null;
            return;
        }
        if (_fieldHighlightCard != null)
        {
            _fieldHighlightCard = null;
            _fieldHighlightSlot = -1;
        }

        int slot = -1;
        VRHand? holder = null;
        VRCard? held = null;
        if (_tray.IsVisible)
        {
            held = HeldCard(out holder);
            // T2: a card plucked out of the pile BROWSE arc (or the active column) is a READ —
            // its release ALWAYS returns it to the viewer (see OnCardReleased's browse/active
            // early-outs), so glowing a tray slot for it telegraphs a drop that cannot happen.
            // No telegraph, no highlight-accept, no haptic for read-only viewer cards.
            if (held != null && IsReadOnlyViewerCard(held))
            {
                held = null;
                holder = null;
            }
            if (held != null && holder != null)
            {
                slot = _tray.SlotNear(held.transform.position, holder.Rig.PalmCenter.position);
                // Task #4b GATE ("what glows is what drops"): a fan card that the release
                // path would REFUSE (fewer than 2 playable cards — the player must rest,
                // see OnCardReleased) must not telegraph a snap either.
                if (slot >= 0 && !_tray.ContainsCard(held)
                    && pickHand != null && CardsGameApi.MustRestInsteadOfPlay(pickHand))
                    slot = -1;
                // Mirror the release-time targeting (item 27.1 + item 2):
                // - a HELD TRAY card NOW glows its own origin slot too — hovering the slot
                //   it came from telegraphs the RESTORE (releasing there re-seats it), so
                //   the origin, the other slot (reorder/swap) and the void are all valid;
                // - a FAN card hovering an OCCUPIED slot telegraphs a SWAP into that very
                //   slot (occupant → hand), so the glow stays put — no divert, both-
                //   occupied included.
                // What glows is what drops, origin included.
            }
        }
        _snapHighlightCard = slot >= 0 ? held : null;

        // PlayTray dedupes the visual toggle itself (safe across tray rebuilds);
        // the driver-side cache only edges the haptic.
        _tray.SetHighlightedSlot(slot);
        if (slot != _snapHighlightSlot)
        {
            _snapHighlightSlot = slot;
            if (slot >= 0 && holder != null)
                holder.SendHaptic(HapticPreset.HoverTick); // debounced: only on slot change
        }
    }

    /// <summary>
    /// T2: cards adopted by a read-only pile viewer — the discard/burnt BROWSE arc or the
    /// active-cards column. Their releases always return them to the viewer, never into the
    /// select/slot seams, so no drop telegraph may ever glow for them.
    /// </summary>
    private bool IsReadOnlyViewerCard(VRCard card) => _browser.Contains(card) || _active.Contains(card);

    private static VRCard? HeldCard(out VRHand? holder)
    {
        holder = null;
        VRHand? left = VRHands.Left;
        if (left != null && left.Grabber.Held is VRCard heldLeft)
        {
            holder = left;
            return heldLeft;
        }
        VRHand? right = VRHands.Right;
        if (right != null && right.Grabber.Held is VRCard heldRight)
        {
            holder = right;
            return heldRight;
        }
        return null;
    }

    // ------------------------------------------------------------- hand fan reorder --

    // Persisted VR fan order (session-only), keyed by AbilityCardUI.CardInstanceID. Applied to
    // _fanBuffer every Rebuild (before _fan.SetCards) so it OVERRIDES the game's own SortCards
    // re-sort — a pure VR-presentation reorder with zero gameplay effect. Pruned to the present
    // hand each Rebuild so stale ids (id reuse across scenarios) never accumulate.
    private readonly List<int> _fanOrder = new(24);
    private readonly List<VRCard> _fanReorderScratch = new(24);

    // Cards plucked OUT of the fan and still held — eligible for a reorder commit on release.
    private readonly HashSet<VRCard> _fanOriginCards = new();

    // Live insertion telegraph while a fan-originating card is held (mirrors _snapHighlightSlot):
    // the open gap (0..n, -1 = none) and the card it belongs to, so "what glows is what drops."
    private int _insertGap = -1;
    private VRCard? _insertHighlightCard;

    /// <summary>Stable CardInstanceID key for a fan card (int.MinValue = no game card).</summary>
    private static int FanId(VRCard? card) =>
        card != null && card.GameCard != null ? card.GameCard.CardInstanceID : int.MinValue;

    /// <summary>
    /// Stage A: stable-reorder <see cref="_fanBuffer"/> to match the persisted <see cref="_fanOrder"/>
    /// just before <c>_fan.SetCards</c>. Any card whose id is not yet tracked keeps its game-relative
    /// order and is registered (appended); tracked ids no longer in the hand are pruned. This is the
    /// single seam that overrides the game's re-sort. Allocation-free steady state (reused scratch).
    /// </summary>
    private void ReorderFanBuffer()
    {
        int n = _fanBuffer.Count;
        if (n == 0)
            return;

        // Register newcomers (append, preserving current game-relative order among unknowns).
        for (int i = 0; i < n; i++)
        {
            int id = FanId(_fanBuffer[i]);
            if (id != int.MinValue && !_fanOrder.Contains(id))
                _fanOrder.Add(id);
        }
        // Prune tracked ids absent from the current hand (id reuse / hand change hygiene).
        for (int k = _fanOrder.Count - 1; k >= 0; k--)
        {
            int id = _fanOrder[k];
            bool present = false;
            for (int i = 0; i < n; i++)
            {
                if (FanId(_fanBuffer[i]) == id) { present = true; break; }
            }
            if (!present)
                _fanOrder.RemoveAt(k);
        }

        // Emit in _fanOrder order (stable), then any leftover (null-id) card in original order.
        _fanReorderScratch.Clear();
        _fanReorderScratch.AddRange(_fanBuffer);
        _fanBuffer.Clear();
        for (int k = 0; k < _fanOrder.Count; k++)
        {
            int id = _fanOrder[k];
            for (int i = 0; i < _fanReorderScratch.Count; i++)
            {
                VRCard c = _fanReorderScratch[i];
                if (c != null && FanId(c) == id) { _fanBuffer.Add(c); break; }
            }
        }
        for (int i = 0; i < _fanReorderScratch.Count; i++)
        {
            VRCard c = _fanReorderScratch[i];
            if (c != null && !_fanBuffer.Contains(c))
                _fanBuffer.Add(c);
        }
    }

    /// <summary>
    /// Stage D: while a fan-originating OR tray-originating card is held over the OPEN fan and NOT
    /// over a board slot, resolve the nearest inter-card gap and telegraph it (open the gap + gold
    /// overlay + a debounced haptic on edge), caching it for the release commit. Board-slot
    /// telegraph wins so slot-play and fan-reorder never both glow. Clears whenever nothing
    /// eligible is held. Tray-origin (T1): a card lifted OFF a play slot inserts at the gap too —
    /// the release runs the normal take-back (UnselectCard) and then splices the card's id into
    /// the persisted order at the gap (see the tray take-back branch of OnCardReleased).
    /// </summary>
    private void UpdateFanInsertion()
    {
        // Only the REAL hand fan reorders (CardsSelection). Pick-mode "fans" (discard/burnt piles)
        // reuse the same _fan but must not telegraph a reorder gap — their releases route through
        // HandlePickRelease, never the commit path.
        CardsHandUI? reorderHand = _fakeActive ? null : CurrentHand();
        if (!_fan.IsOpen || reorderHand == null || CardsGameApi.Mode(reorderHand) != CardHandMode.CardsSelection)
        {
            ClearFanInsertion();
            return;
        }
        VRCard? held = HeldCard(out VRHand? holder);
        // Eligible: plucked out of the fan (reorder) OR lifted off a tray slot (T1 — take-back
        // straight into a chosen fan position). Board slot telegraph (UpdateSlotHighlight ran
        // first) wins outright.
        bool eligible = held != null && (_fanOriginCards.Contains(held) || _tray.ContainsCard(held));
        if (held == null || holder == null || !eligible || _snapHighlightSlot >= 0)
        {
            ClearFanInsertion();
            return;
        }

        int gap = _fan.NearestGap(held.transform.position);
        _insertHighlightCard = gap >= 0 ? held : null;
        if (gap != _insertGap)
        {
            _insertGap = gap;
            _fan.SetInsertionGap(gap);
            if (gap >= 0)
                holder.SendHaptic(HapticPreset.HoverTick); // debounced: only on gap change
        }
    }

    /// <summary>Drop any live insertion telegraph (fan closed / nothing eligible held / committed).</summary>
    private void ClearFanInsertion()
    {
        if (_insertGap != -1)
        {
            _insertGap = -1;
            _fan.SetInsertionGap(-1);
        }
        _insertHighlightCard = null;
    }

    /// <summary>
    /// Stage E commit: insert the held card's id into <see cref="_fanOrder"/> at the visual gap
    /// (translated to the persisted order, preserving slotted-card ids), then re-add it to the fan
    /// and rebuild so <see cref="ReorderFanBuffer"/> reproduces the new order everywhere. Session-only.
    /// </summary>
    private void CommitFanInsertion(VRCard card, int gap)
    {
        int id = FanId(card);
        if (id == int.MinValue)
        {
            _fan.Add(card);
            return;
        }
        IReadOnlyList<VRCard> fan = _fan.Cards; // current fan order (the held card is already out)
        _fanOrder.Remove(id);
        int insertAt = _fanOrder.Count;
        if (fan.Count == 0)
        {
            insertAt = _fanOrder.Count;
        }
        else if (gap < fan.Count)
        {
            int idx = _fanOrder.IndexOf(FanId(fan[gap]));      // before the card now to its right
            insertAt = idx >= 0 ? idx : _fanOrder.Count;
        }
        else
        {
            int idx = _fanOrder.IndexOf(FanId(fan[fan.Count - 1])); // after the last fan card
            insertAt = idx >= 0 ? idx + 1 : _fanOrder.Count;
        }
        _fanOrder.Insert(insertAt, id);
        _fan.Add(card);
        _dirty = true;
    }

    // ------------------------------------------------------------------ rebuild --

    /// <summary>
    /// Live board switch executor: re-park every card currently seated ON the tray back
    /// to the factory pool, THEN tear the tray down so the next <see cref="Rebuild"/>
    /// loads the newly selected prefab. Slot occupants / pick-field / short-rest cards
    /// are parented under the tray root, so <see cref="PlayTray.Destroy"/>'s
    /// DestroyImmediate would otherwise destroy those factory-owned VRCards and orphan
    /// their adopted game faces — re-parenting them to the pool first keeps them (and
    /// their faces) alive; the forced rebuild re-seats them from authoritative state.
    /// </summary>
    private void RebuildBoard()
    {
        // The poke-toggle pile browse fan is parented under the board root (so it inherits the
        // board's live scale/pose) — a board switch DestroyImmediates that root. Close the browse
        // FIRST (it does not touch the cards' parents), so the park loop below still catches the
        // adopted browse cards as children of trayRoot and re-parks them out before the teardown.
        CloseBrowser("board rebuilt");
        Transform? trayRoot = _tray.Root;
        if (trayRoot != null)
        {
            for (int i = 0; i < _factory.All.Count; i++)
            {
                VRCard card = _factory.All[i];
                if (card != null && !card.IsHeld && card.transform.IsChildOf(trayRoot))
                    _factory.Park(card);
            }
        }
        // PART D: capture the outgoing board's world pose BEFORE Destroy so the new board keeps
        // the EXACT same location (Rebuild re-applies it after EnsureBuilt instead of PlaceAtHead).
        _hasSwitchPose = _tray.TryCapturePose(out _switchPos, out _switchRot, out _switchScale);
        _tray.Destroy();
        _dirty = true;
    }

    private void Rebuild(Transform anchor)
    {
        CardsHandUI? hand = CurrentHand();

        if (hand == null)
        {
            _hasSwitchPose = false; // no board to re-pose without a hand
            RebuildFakeOrClear(anchor);
            return;
        }
        if (_fakeActive)
            ClearFakeCards();
        _boundHand = hand;

        bool hadTrayRoot = _tray.Root != null;
        _tray.EnsureBuilt(_factory, anchor);
        // PART D: on a board SWITCH, re-apply the captured pose (the new board spawns in the exact
        // same place) instead of PlaceAtHead. A genuine first build has no captured pose and places
        // at the head as usual.
        if (_hasSwitchPose)
        {
            _hasSwitchPose = false;
            _expectedPoseChange = "rebuild-restored (board switch)"; // sanctioned (issue C watchdog)
            _tray.RestorePose(_switchPos, _switchRot, _switchScale);
        }
        else if (!hadTrayRoot && TryRestoreCarriedPose())
        {
            // ISSUE C: the tray root was re-created OUTSIDE the board-switch path (e.g. torn
            // down externally) — carry the previous pose over instead of re-placing at the
            // head. The factory re-creates the object; the pose survives.
            VRLog.Info("Cards", "Tray rebuilt — previous board pose carried over (no re-place at head).");
        }
        _rest.EnsureBuilt(_tray);
        _half.EnsureBuilt(anchor);
        _half.DockTo(_tray); // action selection lives on the control board (test #19)

        // Pile viewer (test #21): stacks exist whenever an active local hand does.
        if (CardsConfig.PileViewer.Value)
        {
            _piles.EnsureBuilt(_tray);
            _piles.SetVisible(true);
        }
        else
        {
            _piles.SetVisible(false);
            CloseBrowser("[Cards] PileViewer off");
        }

        CardHandMode mode = CardsGameApi.Mode(hand);
        CardsGameApi.GetCards(hand, _widgetBuffer);

        _fanBuffer.Clear();
        _halfBuffer.Clear();

        // Test #15: the tray is the central DASHBOARD — visible for the whole
        // scenario (initiative track, objectives, confirm/undo, settings), not only
        // during card selection. Cards remain grabbable only in CardsSelection.
        bool trayVisible = true;
        bool halfVisible = false;
        bool grabbable = false;

        switch (mode)
        {
            case CardHandMode.CardsSelection:
                // Item B (mode-desync lock): CardsHandUI.currentMode STAYS CardsSelection
                // after the player confirms — it is only re-driven by the next
                // CardsHandManager.Show(...), which never runs during the enemy turn — so
                // the raw mode is a stale trap. The hardware repro sat in this stale
                // CardsSelection fan all through the enemy turn with the played cards still
                // reclaimable and the fan bound. Gate the whole INTERACTIVE selection on the
                // game's own phase (CardsGameApi.IsSelectionPhase == the exact
                // SelectAbilityCardsOrLongRest gate the game uses, CardsHandUI.cs:1516/1564):
                // - selecting → the grabbable hand fan + free slot placement, as before;
                // - locked (confirmed / enemy turn / any non-selection phase) → NO hand fan,
                //   nothing grabbable (grabbable stays false), and the two played cards stay
                //   DOCKED read-only in their slots. The fan unbinds (empty buffer) and no
                //   card is reclaimable until the next real card-selection phase.
                bool selecting = CardsGameApi.IsSelectionPhase(hand);
                grabbable = selecting;
                if (selecting)
                {
                    for (int i = 0; i < _widgetBuffer.Count; i++)
                    {
                        AbilityCardUI widget = _widgetBuffer[i];
                        if (widget.AbilityCard == null || widget.IsLongRest)
                            continue;
                        if (widget.CardType == CardPileType.Hand)
                            _fanBuffer.Add(AdoptedCard(widget));
                    }
                }
                _tray.SyncFromGameState(hand, _factory);
                // Tray occupants were created by the sync — hook + re-adopt them too (both
                // while selecting AND locked, so the docked played cards keep their face).
                for (int slot = 0; slot < 2; slot++)
                {
                    VRCard? occupant = _tray.Occupant(slot);
                    if (occupant == null)
                        continue;
                    HookCard(occupant);
                    if (occupant.NeedsFace && occupant.GameCard != null)
                        occupant.AttachGameCard(occupant.GameCard);
                }
                LogSelectionLock(hand, selecting);
                break;

            case CardHandMode.LoseCard:
            case CardHandMode.DiscardCard:
            case CardHandMode.RecoverDiscardedCard:
            case CardHandMode.RecoverLostCard:
            case CardHandMode.IncreaseCardLimit:
                // Modal card picks (long-rest burn, avoid-damage, discards,
                // recovers): the 2D UI is click-to-select. VR (item 10): the
                // candidates are GRABBABLE ONLY — the card must be PLACED into a
                // control-board slot to select it. Poke-to-select is deliberately
                // NOT armed here: merely touching a hand card must never commit it
                // (a fingertip within 8 mm used to fire SelectCard with no board
                // placement). The only commit path is the deliberate slot drop
                // (HandlePickRelease → TryCommitPick → CardsHandUI.SelectCard).
                grabbable = true;
                // Field occupants stay valid only while the game still reports them
                // selected (an undo / "choose other card" returns them to the fan).
                // Task #11 free swap: while a pick-reopen (cancel → re-select) is in
                // flight the game momentarily reports EVERYTHING deselected — do not
                // prune on that transient or the still-placed card would snap to the
                // fan mid-swap; the reopen's completion re-runs this with final state.
                for (int i = _fieldCards.Count - 1; i >= 0; i--)
                {
                    VRCard occupant = _fieldCards[i];
                    if (occupant == null || occupant.GameCard == null
                        || (!_pickReopenBusy && !occupant.GameCard.IsSelected))
                        _fieldCards.RemoveAt(i);
                }
                // Item 9: the SELECTABLE widgets become the fan. In CardsSelection the
                // fan is the real hand; in the burn-two-discarded flow the game marks
                // the DISCARD-pile widgets selectable (CardHandMode.LoseCard, pile
                // Discarded, count 2 — AbilityCardUI.SetMode), so the exact same fan
                // becomes the discard pile, picked exactly like hand cards through the
                // one authoritative TryCommitPick → SelectCard seam. Track the source
                // pile for the change-deduped Info line below.
                CardPileType pickSource = CardPileType.None;
                // Task #11 (b): while the game's "Karten verbrennen / Wähle eine andere
                // Karte" confirm popup is open it flips EVERY widget unselectable
                // (OnCardSelected LoseCard branch, CardsHandUI.cs:2046-2052) — which
                // used to empty the fan the moment the required cards were placed. The
                // eligible cards must stay browsable/swappable through the whole flow,
                // so while that popup is open the fan keeps the widgets of the pick
                // source pile (the game's own selectableCardTypes) that are not
                // currently selected. Grabbing one triggers the reopen seam
                // (OnCardGrabbed → game's own "choose another card"), which restores
                // real selectability before any commit can run.
                bool confirmOpen = CardsGameApi.IsPickConfirmDialogOpen(hand);
                for (int i = 0; i < _widgetBuffer.Count; i++)
                {
                    AbilityCardUI widget = _widgetBuffer[i];
                    if (widget.AbilityCard == null || widget.IsLongRest)
                        continue;
                    bool eligible = widget.IsSelectable
                        || (confirmOpen && !widget.IsSelected && CardsGameApi.IsPickEligible(hand, widget));
                    if (!eligible)
                        continue;
                    if (pickSource == CardPileType.None)
                        pickSource = widget.CardType;
                    VRCard card = AdoptedCard(widget);
                    if (!_fieldCards.Contains(card))
                        _fanBuffer.Add(card);
                }
                LogPickSource(mode, pickSource);
                RelayoutField();
                break;

            case CardHandMode.ActionSelection:
                halfVisible = true;
                CollectRoundCards(hand, _halfBuffer);
                break;

            default:
                break;
        }

        if (mode != CardHandMode.CardsSelection)
            _tray.ClearSlots(); // stale occupancy must not pin cards outside CardsSelection

        // Drop field (test #21 B): exists ONLY during a pick mode (C); leaving the
        // mode clears its occupants — the zone loop below parks them.
        bool pick = IsPickMode(mode);

        // Short-rest sacrifice overlay (test #25, item 1d; test #28 seating): while the
        // game presents the randomly lost card's burn/redraw choice (ShortRestedCard !=
        // null; the choice itself is the docked DialogPopup), lay that card physically
        // in the LEFT slot recess (Slot1) — the same left-slot home the avoid-damage
        // burn uses — DISPLAY-ONLY. Guarded off during the pick modes (which own the
        // slots) so the two flows can never collide, and off when the tray is hidden.
        // The short-rest random path stays in CardHandMode.CardsSelection, so this
        // simply overlays the unchanged hand fan. Rebuild is the sole executor;
        // PollShortRest keeps it live on redraw.
        CAbilityCard? shortRested = pick ? null : CardsGameApi.ShortRestedCard(hand);
        bool shortRest = shortRested != null && trayVisible;

        // Slots stay physically visible (fixed asset, test #28) — no field to toggle;
        // the driver only marks pick flows so CONFIRM mirrors the mode's confirm.
        _tray.SetPickActive(pick && trayVisible);
        if (shortRest)
            PresentShortRestCard(hand, shortRested!);
        else
            RemoveShortRestCard();

        if (!pick)
        {
            _fieldCards.Clear();
            _loggedPickSource = null; // re-entering a pick mode logs its source afresh (item 9)
        }

        // Pile browse (test #21): refresh content or close — BEFORE the zone flags
        // below so freshly closed browse cards park in this same pass.
        UpdateBrowser(hand, mode);

        // ACTIVE CARDS area (feature 6): refresh the permanently-shown active-card column
        // — BEFORE the zone flags below so its cards are marked in-zone and kept out of the
        // park sweep (like the browse arc), and their active-half highlight is set here.
        UpdateActive(hand);

        // Configure cards per zone; everything else parks invisibly.
        for (int i = 0; i < _factory.All.Count; i++)
        {
            VRCard card = _factory.All[i];
            if (card == null || card.IsHeld)
                continue;
            bool inFan = _fanBuffer.Contains(card);
            bool inHalf = _halfBuffer.Contains(card);
            bool inTray = _tray.SlotOf(card) >= 0;
            bool inBrowse = _browser.Contains(card);
            bool inField = _fieldCards.Contains(card);
            bool inActive = _active.Contains(card); // feature 6: shown in the active-cards column
            // Short-rest sacrifice card: its own zone. Never in any of the above, so
            // its Grabbable/PokeSelect resolve to false here (display-only) — we only
            // keep it OUT of the park sweep so PresentShortRestCard's centre home holds.
            bool inShortRest = ReferenceEquals(card, _shortRestCard);

            // Task #2 follow-up: the slot-dock grab apron (under/around-grab accept,
            // VRCard.SetDockGrabPad) is TRUE exactly for slot-docked cards — tray
            // occupants and pick/field cards in the recesses — and FALSE everywhere
            // else. Central re-assert every Rebuild (idempotent) so no dock/undock
            // path can leave a stale apron on a fan/browse/parked card.
            card.SetDockGrabPad(inTray || inField);

            // Item 10: poke-select is never armed on hand cards — touching a card
            // must not auto-select it; a card is committed only by placing it into a
            // board slot. Kept as an explicit reset so a previously pokeable card is
            // disarmed on rebuild.
            card.PokeSelectEnabled = false;
            // Field occupants stay grabbable: plucking one back off the field and
            // releasing it elsewhere unselects through the game's own seam. Browse
            // cards are grabbable too (item 5) — but purely to pull one close and
            // read it; the release routes back to the arc, never to a game seam.
            // Active cards (feature 6) are grabbable for the same read-only reason:
            // pluck one to read it, release returns it to the column, never a game seam.
            card.Grabbable = (inFan && grabbable) || (inTray && grabbable) || inField || inBrowse || inActive;
            if (!inFan)
                card.ResetColliderRegion(); // fan strips only apply while fanned
            if (!inActive)
                ClearActiveHighlight(card); // clear any stale active-region highlight on reused cards

            if (!inFan && !inHalf && !inTray && !inBrowse && !inField && !inShortRest && !inActive)
                _factory.Park(card);
        }

        // Hand reorder (Stage A): apply the persisted VR order to the real hand fan only —
        // overrides the game's SortCards. Pick-mode "fans" (discard/burnt piles) are transient
        // and keep game order.
        if (mode == CardHandMode.CardsSelection)
            ReorderFanBuffer();
        _fan.SetCards(_fanBuffer);
        _tray.SetVisible(trayVisible);
        _half.SetVisible(halfVisible);
        if (halfVisible)
            _half.SetCards(_halfBuffer);

        VRLog.Debug("Cards", $"Rebuild: mode={mode} fan={_fanBuffer.Count} tray={trayVisible} half={_halfBuffer.Count}.");
    }

    private VRCard AdoptedCard(AbilityCardUI widget)
    {
        VRCard card = _factory.GetOrCreate(widget);
        if (card.NeedsFace)
            card.AttachGameCard(widget); // re-adopt after a dialog yielded the face
        HookCard(card);
        return card;
    }

    private void CollectRoundCards(CardsHandUI hand, List<VRCard> into)
    {
        // The AUTHORITATIVE per-character selector is the acting hand's own round pile
        // (IsInRound → CharacterClass.RoundAbilityCards) — always exactly the two played
        // cards of THIS actor. The phase machine's pair (CardsActionControlller.topCard/
        // bottomCard) is a static singleton that can lag a same-mode turn hand-off, so it is
        // only ADDED (covers the extra-turn pile, where cards are not in RoundAbilityCards) —
        // never used ALONE, so a stale pair can no longer make us dock the previous
        // character's cards (the second-character deadlock).
        CardsGameApi.GetActionCards(out FullAbilityCard? first, out FullAbilityCard? second);
        for (int i = 0; i < _widgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _widgetBuffer[i];
            if (widget.AbilityCard == null || widget.IsLongRest)
                continue;
            bool isActionCard =
                CardsGameApi.IsInRound(hand, widget.AbilityCard) ||
                (first != null && widget.fullAbilityCard == first) ||
                (second != null && widget.fullAbilityCard == second);
            if (isActionCard)
            {
                VRCard card = AdoptedCard(widget);
                if (!into.Contains(card))
                    into.Add(card);
            }
        }
    }

    private readonly HashSet<VRCard> _hooked = new();

    private void HookCard(VRCard card)
    {
        if (!_hooked.Add(card))
            return;
        card.Released += OnCardReleased;
        card.Grabbed += OnCardGrabbed;
        card.Poked += OnCardPoked;
    }

    // ------------------------------------------------------------------ interactions --

    /// <summary>
    /// Drop state machine (test #14): a slot placement may fire EXACTLY ONCE per
    /// real user release. Cards enter on OnCardGrabbed (the only way a hand gets a
    /// card) and leave on the matching OnCardReleased — any Released event without
    /// a live grab session (double-fire, stale event after a rebuild/hot reload) is
    /// dropped before it can reach the slot logic.
    /// </summary>
    private readonly HashSet<VRCard> _liveGrabs = new();

    private void OnCardGrabbed(VRCard card, VRHand hand)
    {
        _liveGrabs.Add(card);
        // More-card-sounds: soft pick tick on every card grab (fan pluck, slot pluck, pile/
        // active read-grab — proximity and laser alike). Edge-triggered by nature: Grabbed
        // fires exactly once per grab session. The game plays nothing of its own here (a
        // physical VR grab has no game call), so no stacking.
        PlayCardSound(CardsConfig.CardGrabSound.Value, card.transform);
        // Item 8: grabbing a hand/tray/field card while a browse is open is a foreign
        // interaction. Grabbing a BROWSE card is part of the browse (read close), so
        // it is exempt — only the arc's own cards may be plucked without dismissing.
        if (!_browser.Contains(card))
            ForeignInteraction("card grabbed");
        // Accident window (test #19): a pluck FROM a slot or the pick field means
        // the hand is working right next to CONFIRM — arm the suppression guard.
        if (_tray.SlotOf(card) >= 0 || _fieldCards.Contains(card))
            _tray.NoteSlotActivity();
        // Task #11 (free swap): grabbing ANY pick card (a placed one off the tray OR a
        // fresh candidate from the fan) while the game's burn/lose CONFIRM popup is
        // open re-opens the selection through the game's own "choose another card"
        // seam — see ReopenPickSelection. Without this the take-back ran UnselectCard
        // under a live popup, a state the 2D game forbids (it locks all cards while
        // the popup shows), and the stale popup's commit then indexed an empty
        // selectedCardsUI → GlobalErrorMessage → dumped to the main menu.
        MaybeReopenPickSelection(card);
        if (_fan.Contains(card))
        {
            _fanOriginCards.Add(card); // reorder: eligible for a fan-gap commit on release
            _fan.Remove(card);
        }
        // Tray occupancy stays until the release decides select/unselect/swap.
    }

    private void OnCardReleased(VRCard card, VRHand hand, Vector3 velocity)
    {
        if (!_liveGrabs.Remove(card))
        {
            VRLog.Warn("Cards", $"Release without live grab ignored ({card.name}) — drop path is once-per-release.");
            return;
        }

        // Hand reorder: was this card plucked out of the fan? (consumed here, used by the void
        // release branch below to commit into a gap or cancel to origin).
        bool fanOrigin = _fanOriginCards.Remove(card);

        if (_fakeActive)
        {
            RouteFakeRelease(card, hand);
            return;
        }

        // Pile-browse card (item 5): plucked out for a close read — return it to the
        // reading arc, NEVER into the select/slot seams below (these are discard/burnt
        // cards, not hand cards; committing them would be wrong). Purely informational.
        if (_browser.IsOpen && _browser.Contains(card))
        {
            _browser.Add(card);
            return;
        }

        // Active-card (feature 6): plucked out to read close — return it to the active
        // column, NEVER into the select/slot seams below (active cards are informational,
        // not selectable; committing one would be wrong). Same read-only contract as the
        // pile browse arc.
        if (_active.IsShown && _active.Contains(card))
        {
            _active.Add(card);
            return;
        }

        CardsHandUI? gameHand = CurrentHand();
        if (gameHand == null || card.GameCard == null)
        {
            _fan.Add(card);
            return;
        }

        // Pick modes (test #21 B): the drop field is the only target — the slot
        // logic below is CardsSelection-only.
        if (IsPickMode(CardsGameApi.Mode(gameHand)))
        {
            HandlePickRelease(card, hand, gameHand);
            return;
        }

        // Test #15 accept rules, in priority order:
        // 1. HIGHLIGHT: the slot that was GLOWING for this card at release wins —
        //    hardware logs showed the release gesture consistently moving the hand
        //    just out of radius (3.2–6 m vs 2.74 m at diorama scale ~23) while the
        //    glow HAD triggered; what glows is what drops, guaranteed.
        // 2. RADIUS fallback: generous dual-sample capture (test #13) — card center
        //    AND holding-hand palm both count, whichever is nearest.
        int highlightSlot = ReferenceEquals(_snapHighlightCard, card) ? _snapHighlightSlot : -1;
        int slot = _tray.SlotNear(card.transform.position, hand.Rig.PalmCenter.position,
            out float d1, out float d2, out float radius);
        bool wasInTray = _tray.ContainsCard(card);
        CAbilityCard ability = card.GameCard.AbilityCard;

        // Item 2 (return-to-origin): a card plucked OUT of a slot may be dropped back
        // onto the SAME slot it came from to RESTORE it there — the origin slot is now
        // ALWAYS a valid drop target for its own card (the glow telegraphs it too, see
        // UpdateSlotHighlight). The OLD rule force-excluded the origin here (highlight/
        // radius reset to -1 whenever they resolved to the source slot), so the only way
        // back into the tray was the OTHER slot — restoring a card to its exact original
        // slot was impossible (the reported bug). To UNSELECT / take a card back to the
        // fan the player releases it AWAY from BOTH slots (slot < 0 → the take-back path
        // below), which the free-placement flow already implies. No self-exclusion now:
        // origin, other slot and the void are all reachable, so free movement is intact.

        string rule = highlightSlot >= 0 ? "highlight" : slot >= 0 ? "radius" : "none";
        if (highlightSlot >= 0)
            slot = highlightSlot;

        // Task #4b GATE: card selection needs TWO cards (or a declared rest). When the
        // hand+round pool has fewer than 2 playable cards the requirement is
        // unsatisfiable (exact game rule mirrored in CardsGameApi.MustRestInsteadOfPlay:
        // IsCardSelectionReady needs RoundAbilityCards >= 2 || LongRest,
        // CPlayerActorExtensions.cs:5; hand+round < 2 is the rule engine's own
        // exhaustion formula, CCharacterClass.cs:1766) — the player must rest instead.
        // Refuse the fan→slot drop with the existing return-home glide. Tray-origin
        // drops (reorder / take-back) stay allowed: they never grow the round pile.
        if (slot >= 0 && !wasInTray && CardsGameApi.MustRestInsteadOfPlay(gameHand))
        {
            VRLog.Info("Cards", $"Drop REFUSED ({hand.Side}): only " +
                                $"{CardsGameApi.PlayableCardCount(gameHand)} playable card(s) left — " +
                                "selection needs TWO cards; rest instead (short/long). " +
                                $"'{ability.Name}' returns to the fan.");
            _fan.Add(card); // refuse/return-home path — the card glides back
            return;
        }

        // THE one log line per real drop (test #14; #15 adds the accepting rule;
        // item 27.1 adds the fan→occupied swap outcome). A fan card landing on an
        // occupied slot swaps: the newcomer takes the slot, the occupant → hand.
        string outcome =
            slot < 0 ? (wasInTray ? "take back to fan." : "return to fan.")
            : wasInTray ? $"reorder to slot {slot + 1}."
            : _tray.Occupant(slot) != null ? $"swap into slot {slot + 1} (occupant → hand)."
            : $"play into slot {slot + 1}.";
        VRLog.Info("Cards", $"Drop ({hand.Side}): slot1 {d1:F2} m, slot2 {d2:F2} m, radius {radius:F2} m, " +
                            $"rule={rule} → {outcome}");

        // Accident window (test #19): every drop/take-back touching the slots arms
        // the tray's CONFIRM guard — the release gesture is exactly what brushed
        // CONFIRM in the hardware log.
        if (slot >= 0 || wasInTray)
            _tray.NoteSlotActivity();

        if (slot >= 0 && !wasInTray && _tray.Occupant(slot) == null)
        {
            // Fan → empty slot: play the card. The snap itself is PlaceCard's SetHome —
            // a quick local lerp into the slot (CardLerpSpeed) — plus a click pulse
            // so the zap is felt, not just seen (test #13).
            // Task #5 (double sound): NO mod place sound here — the queued SelectCard's
            // AbilityCardUI.ToggleSelect plays the card's serialized profile click for a
            // locally-controlled hand (mouseDownAudioItem, AbilityCardUI.cs:1182-1185),
            // and our thunk on top made every slot placement sound doubled. The game's
            // click IS the placement sound on all Select/Unselect paths.
            hand.SendHaptic(HapticPreset.ClickPulse);
            // SelectCard is the spin-wait path — queued; outcome verified against
            // the authoritative round pile afterwards.
            _tray.PlaceCard(card, slot);
            CardsHandUI handRef = gameHand;
            CardActionQueue.Enqueue(
                () => CardsGameApi.SelectCard(handRef, ability),
                () =>
                {
                    if (!CardsGameApi.IsInRound(handRef, ability))
                    {
                        VRLog.Info("Cards", $"Select rejected for {ability.Name} — returning to fan.");
                        _tray.RemoveCard(card);
                        _fan.Add(card);
                    }
                    ReconcileInitiative(handRef);
                    _dirty = true;
                });
        }
        else if (slot >= 0 && !wasInTray)
        {
            // (b) Fan → OCCUPIED slot: SWAP. The newcomer takes the slot and the card
            // it displaces returns to the hand — the whole point being a swap even when
            // BOTH slots are full (trade one of two played cards). Unselect the occupant
            // FIRST so the round pile (max two) has room, THEN select the newcomer; both
            // queued so they serialize one-per-frame in that order. Verified against the
            // authoritative round pile like the plain play, newcomer bounced to the fan
            // on rejection.
            hand.SendHaptic(HapticPreset.ClickPulse);
            // Task #5: no mod sound — the queued Unselect+Select pair below already plays
            // the game's own profile clicks (ToggleSelect both ways); ours made a third.
            VRCard displaced = _tray.Occupant(slot)!;
            CAbilityCard? displacedAbility = displaced.GameCard?.AbilityCard;
            _tray.RemoveCard(displaced);
            _fan.Add(displaced);
            _tray.PlaceCard(card, slot);
            CardsHandUI handRef = gameHand;
            if (displacedAbility != null)
                CardActionQueue.Enqueue(
                    () => CardsGameApi.UnselectCard(handRef, displacedAbility),
                    () => _dirty = true);
            CardActionQueue.Enqueue(
                () => CardsGameApi.SelectCard(handRef, ability),
                () =>
                {
                    if (!CardsGameApi.IsInRound(handRef, ability))
                    {
                        VRLog.Info("Cards", $"Swap select rejected for {ability.Name} — returning to fan.");
                        _tray.RemoveCard(card);
                        _fan.Add(card);
                    }
                    ReconcileInitiative(handRef);
                    _dirty = true;
                });
        }
        else if (slot >= 0 && wasInTray)
        {
            // Tray → tray: physical reorder. If the other slot is occupied this is an
            // initiative swap; a lone card just changes slots visually.
            hand.SendHaptic(HapticPreset.ClickPulse); // snap feedback (test #13)
            PlayCardSound(CardsConfig.CardPlaceSound.Value, card.transform); // more-card-sounds: placing thunk
            int oldSlot = _tray.SlotOf(card);
            if (slot != oldSlot && _tray.Occupant(slot) != null)
            {
                VRCard other = _tray.Occupant(slot)!;
                _tray.PlaceCard(other, oldSlot);
                _tray.PlaceCard(card, slot);
                OnSwapRequested();
            }
            else
            {
                _tray.PlaceCard(card, slot);
                CardsHandUI handRef = gameHand;
                CardActionQueue.Enqueue(() => ReconcileInitiative(handRef), () => _dirty = true);
            }
        }
        else if (wasInTray)
        {
            // Tray → elsewhere: take the card back.
            // Task #5: no mod sound — the queued UnselectCard plays the card's serialized
            // profile click via AbilityCardUI.ToggleSelect (deselect path, AbilityCardUI.cs:1184);
            // the soft undo click on top doubled it.
            // T1 (tray → fan position): if the fan's insertion gap was glowing for THIS card at
            // release, the take-back lands at that gap instead of the game-sorted position —
            // same "what glows is what drops" contract as the fan-origin reorder. The gap and its
            // neighbour ids are captured NOW (the fan may change while the unselect is queued);
            // the _fanOrder splice runs in the unselect COMPLETION so the rebuild it triggers
            // sees the card back in the game hand — splicing earlier would race ReorderFanBuffer's
            // prune (the id is not in the hand until the unselect lands) and lose the position.
            int gap = ReferenceEquals(_insertHighlightCard, card) ? _insertGap : -1;
            int insertBeforeId = int.MinValue; // id of the card right of the gap (insert before it)
            int insertAfterId = int.MinValue;  // id of the last fan card (gap past the end)
            if (gap >= 0)
            {
                IReadOnlyList<VRCard> fanNow = _fan.Cards;
                if (gap < fanNow.Count)
                    insertBeforeId = FanId(fanNow[gap]);
                else if (fanNow.Count > 0)
                    insertAfterId = FanId(fanNow[fanNow.Count - 1]);
                ClearFanInsertion();
                hand.SendHaptic(HapticPreset.ClickPulse);
                VRLog.Info("Cards", $"Fan reorder ({hand.Side}): TRAY card take-back committed to fan " +
                                    $"gap {gap} (unselect queued; session-only VR order).");
            }
            _tray.RemoveCard(card);
            _fan.Add(card);
            int cardId = FanId(card);
            CardsHandUI handRef = gameHand;
            CardActionQueue.Enqueue(
                () => CardsGameApi.UnselectCard(handRef, ability),
                () =>
                {
                    if (gap >= 0 && cardId != int.MinValue)
                    {
                        _fanOrder.Remove(cardId);
                        int at = _fanOrder.Count;
                        if (insertBeforeId != int.MinValue)
                        {
                            int idx = _fanOrder.IndexOf(insertBeforeId);
                            at = idx >= 0 ? idx : _fanOrder.Count;
                        }
                        else if (insertAfterId != int.MinValue)
                        {
                            int idx = _fanOrder.IndexOf(insertAfterId);
                            at = idx >= 0 ? idx + 1 : _fanOrder.Count;
                        }
                        _fanOrder.Insert(at, cardId);
                    }
                    _dirty = true;
                });
        }
        else if (fanOrigin && ReferenceEquals(_insertHighlightCard, card) && _insertGap >= 0)
        {
            // Hand reorder COMMIT: released over an open gap — insert at that index. "What glows
            // is what drops," identical to the slot rule. Pure VR presentation (no game call).
            int gap = _insertGap;
            ClearFanInsertion();
            hand.SendHaptic(HapticPreset.ClickPulse);
            CommitFanInsertion(card, gap);
            VRLog.Info("Cards", $"Fan reorder ({hand.Side}): card committed to fan gap {gap} (session-only VR order).");
        }
        else
        {
            // Released in the void. A fan-originating card NOT over a gap CANCELS: _fanOrder still
            // holds its original position, so the rebuild re-seats it at its origin index (return-
            // to-origin). Non-fan cards just animate back into the fan as before.
            ClearFanInsertion();
            _fan.Add(card); // animated return
            if (fanOrigin)
                _dirty = true; // rebuild re-applies _fanOrder → snaps back to the original index
        }
    }

    private void OnCardPoked(VRCard card, VRHand hand)
    {
        if (_fakeActive || card.GameCard == null)
            return;
        CardsHandUI? gameHand = CurrentHand();
        if (gameHand == null)
            return;
        // Modal pick fallback (test #21 B): poke commits through the SAME seam as
        // the drop field — one guard, no double-commit.
        TryCommitPick(card, gameHand, "poke");
    }

    // ------------------------------------------------------------------ pick flows --

    /// <summary>Cards physically laid onto the pick drop field (selected candidates).</summary>
    private readonly List<VRCard> _fieldCards = new(4);

    /// <summary>
    /// Short-rest sacrifice display (test #25, item 1d): the randomly lost card laid
    /// physically at the board centre while the docked burn/redraw DialogPopup decides
    /// its fate — the same sacrifice display as the avoid-damage burn. Its OWN path,
    /// deliberately separate from <see cref="_fieldCards"/>: DISPLAY-ONLY, never
    /// grabbable / poke-select / droppable (the docked choice commits burn/redraw).
    /// <see cref="_shortRestPresented"/> is the change-dedup key (the CAbilityCard
    /// currently shown) — on REDRAW the game swaps it for the alternate card and this
    /// path re-adopts the new one.
    /// </summary>
    private VRCard? _shortRestCard;
    private CAbilityCard? _shortRestPresented;

    /// <summary>Change-dedup for the pick-fan source line (item 9): (mode, source pile).</summary>
    private (CardHandMode mode, CardPileType source)? _loggedPickSource;

    /// <summary>
    /// Item 9: name where the pick fan's candidates come from — the REAL HAND
    /// (avoid-damage lose-1, card-limit) vs the DISCARD pile (burn-two-discarded,
    /// recover-discard) vs the BURNT pile (recover-lost) — change-deduped to one line
    /// per (mode, source) change. Proves from the log alone that "burn two discarded"
    /// really turned the discard pile into the selectable hand fan.
    /// </summary>
    private void LogPickSource(CardHandMode mode, CardPileType source)
    {
        var key = (mode, source);
        if (_loggedPickSource.HasValue && _loggedPickSource.Value == key)
            return;
        _loggedPickSource = key;
        string name = source switch
        {
            CardPileType.Hand => "real hand",
            CardPileType.Discarded => "discard pile",
            CardPileType.Lost or CardPileType.Permalost => "burnt pile",
            CardPileType.None => "none (no selectable cards)",
            _ => source.ToString(),
        };
        VRLog.Info("Cards", $"Pick fan source ({mode}): {name} — the selectable cards ARE the hand fan " +
                            "(picked through the one TryCommitPick → CardsHandUI.SelectCard seam).");
    }

    /// <summary>
    /// One pick commit may be in flight at a time (test #21 B): poke and drop both
    /// funnel into <see cref="TryCommitPick"/>, and a second request is dropped
    /// until the queued SelectCard resolved — the once-per-action guard pattern of
    /// the test #19 half-selection accident fixes.
    /// </summary>
    private bool _pickCommitBusy;

    /// <summary>
    /// Task #11 (free swap): true while a pick REOPEN — the queued cancel of the
    /// game's burn/lose confirm popup plus the re-select of the cards that stay
    /// placed — is in flight. Guards the Rebuild field prune (everything reads
    /// deselected mid-cancel) and lets <see cref="TryCommitPick"/> queue a select for
    /// a card whose stale IsSelected has not been cleared yet.
    /// </summary>
    private bool _pickReopenBusy;

    /// <summary>Scratch for the abilities that must be re-selected after a reopen cancel.</summary>
    private readonly List<CAbilityCard> _reopenKeep = new(4);

    /// <summary>
    /// Task #11 (crash + free swap): the game's 2D pick flow shows the
    /// "Karten verbrennen" / "Wähle eine andere Karte" popup the moment the required
    /// number of cards is selected and LOCKS every card until an option resolves — so
    /// its commit callback may safely index <c>selectedCardsUI[0]/[1]</c>. VR free
    /// placement broke that invariant: plucking a card back off the tray while the
    /// popup was open ran UnselectCard underneath it, and the popup's commit then
    /// crashed (IndexOutOfRange → GlobalErrorMessage → main menu; Player.log 59952-60089).
    ///
    /// The honest VR equivalent of that pluck is the popup's own CANCEL option
    /// ("choose another card"): the instant a pick card is grabbed while the popup is
    /// open, queue the game's <c>DialogPopup.Cancel()</c> (which deselects ALL cards
    /// and restores selectability — the exact 2D path) and then re-select the cards
    /// that remain physically placed, excluding the grabbed one. Result: the popup can
    /// NEVER be open with an incomplete selection, swapping works indefinitely (place
    /// the last card → popup reopens through the game's own OnCardSelected), and the
    /// commit affordance only exists while the required cards are actually placed.
    /// All game calls are the game's own local-selection seams (network-synced by the
    /// game itself) — no game state is faked.
    /// </summary>
    private void MaybeReopenPickSelection(VRCard card)
    {
        CardsHandUI? hand = CurrentHand();
        if (hand == null || _pickReopenBusy || card.GameCard == null)
            return;
        if (!IsPickMode(CardsGameApi.Mode(hand)))
            return;
        if (!CardsGameApi.IsPickConfirmDialogOpen(hand))
            return;
        // Grab-time reopen applies to PLACED field cards only (take it back / re-seat
        // it): the choice must reopen the instant the placement is physically undone.
        // Grabbing a FAN card leaves the popup alone — a swap only commits on the
        // release INTO a slot (BeginPickSwapReopen from HandlePickRelease), so a
        // change-of-mind void release changes nothing. Browse/active cards are
        // read-only and never reach this.
        if (!_fieldCards.Contains(card))
            return;

        // Keep every OTHER placed card selected.
        _reopenKeep.Clear();
        for (int i = 0; i < _fieldCards.Count; i++)
        {
            VRCard placed = _fieldCards[i];
            if (placed == null || ReferenceEquals(placed, card) || placed.GameCard == null)
                continue;
            _reopenKeep.Add(placed.GameCard.AbilityCard);
        }
        EnqueuePickReopen(hand, "placed card grabbed back");
    }

    /// <summary>
    /// Task #11 (free swap, fan → slot while the popup is open): a fan candidate was
    /// dropped into a slot while the confirm popup still showed the full selection.
    /// Reopen through the game's cancel seam first, keeping all placements EXCEPT the
    /// most recent one (its slot goes to the incoming card — it returns to the fan);
    /// the caller then queues the incoming card's select, which re-completes the
    /// selection and re-opens the popup with the swapped set. Runs before the commit
    /// so the game never sees a select while the popup locks the cards.
    /// </summary>
    private void BeginPickSwapReopen(CardsHandUI hand, VRCard incoming)
    {
        if (_pickReopenBusy || !CardsGameApi.IsPickConfirmDialogOpen(hand))
            return;

        _reopenKeep.Clear();
        for (int i = 0; i < _fieldCards.Count - 1; i++) // all but the most recent placement
        {
            VRCard placed = _fieldCards[i];
            if (placed == null || ReferenceEquals(placed, incoming) || placed.GameCard == null)
                continue;
            _reopenKeep.Add(placed.GameCard.AbilityCard);
        }
        // Physically free the displaced placement now — the queued cancel deselects it
        // and it is not re-selected, so it returns to the fan instead of waiting for
        // the reconcile prune.
        if (_fieldCards.Count > 0)
        {
            VRCard displaced = _fieldCards[_fieldCards.Count - 1];
            if (displaced != null && !ReferenceEquals(displaced, incoming))
            {
                _fieldCards.RemoveAt(_fieldCards.Count - 1);
                RelayoutField();
                _fan.Add(displaced);
            }
        }
        EnqueuePickReopen(hand, "fan card swap-in");
    }

    /// <summary>Queue the actual reopen: the game's own "choose another card"
    /// (DialogPopup.Cancel → deselect all + restore selectability) followed by the
    /// re-selects of <see cref="_reopenKeep"/>. Serialized one call per frame by
    /// <see cref="CardActionQueue"/> (the select paths spin-wait).</summary>
    private void EnqueuePickReopen(CardsHandUI hand, string why)
    {
        _pickReopenBusy = true;
        VRLog.Info("Cards", $"Pick reopen ({why}): burn/lose confirm popup open → pressing the game's " +
                            "own \"choose another card\" (DialogPopup.Cancel) and re-selecting " +
                            $"{_reopenKeep.Count} still-placed card(s). The popup can never stay open " +
                            "with an incomplete selection.");
        CardsHandUI handRef = hand;
        CardActionQueue.Enqueue(() => CardsGameApi.CancelPickConfirmDialog());
        for (int i = 0; i < _reopenKeep.Count; i++)
        {
            CAbilityCard keep = _reopenKeep[i];
            CardActionQueue.Enqueue(() => CardsGameApi.SelectCard(handRef, keep));
        }
        CardActionQueue.Enqueue(() => { }, () =>
        {
            _pickReopenBusy = false;
            _dirty = true;
        });
    }

    /// <summary>
    /// Release routing for the modal pick modes (test #28: the candidate homes into
    /// the LEFT slot recess, not a centre field). Accept rules mirror the play slots
    /// (test #15): the glow that telegraphed the drop wins, the generous capture
    /// radius is the fallback. Accepting a fan card lays it into the wanted slot and
    /// commits the selection; releasing a SLOTTED pick card anywhere else takes the
    /// pick back through the game's own UnselectCard seam.
    /// </summary>
    private void HandlePickRelease(VRCard card, VRHand hand, CardsHandUI gameHand)
    {
        bool wasOnField = _fieldCards.Contains(card);
        int nearSlot = _tray.SlotNear(card.transform.position, hand.Rig.PalmCenter.position,
            out float d1, out float d2, out float radius);
        bool highlight = ReferenceEquals(_fieldHighlightCard, card) && _fieldHighlightSlot >= 0;
        bool accept = highlight || nearSlot >= 0;
        string rule = highlight ? "highlight" : nearSlot >= 0 ? "radius" : "none";
        // Landing slot: a fresh candidate goes to the wanted (next empty) slot; a
        // re-dropped pick card keeps its own index/recess.
        int target = wasOnField ? _fieldCards.IndexOf(card) : PickTargetSlot();

        // THE one log line per real pick drop (the test #14 contract).
        string where = target >= 0 ? "slot " + (target + 1) : "the overflow spot beside slot 2";
        VRLog.Info("Cards", $"Drop ({hand.Side}): slot1 {d1:F2} m, slot2 {d2:F2} m, radius {radius:F2} m, rule={rule} → " +
                            (accept
                                ? (wasOnField ? "stay in " + where + "." : "select into " + where + ".")
                                : (wasOnField ? "take back (unselect)." : "return to fan.")));

        // Accident window (test #19): the slots sit directly above the cluster's
        // Ready and beside the tray CONFIRM — every drop/take-back touching them
        // arms the confirm suppression, exactly like the play slots.
        if (accept || wasOnField)
            _tray.NoteSlotActivity();

        if (accept)
        {
            hand.SendHaptic(HapticPreset.ClickPulse); // snap feedback (test #13)
            // Task #11 (free swap): a fan candidate dropped into a slot while the
            // confirm popup shows the completed selection — reopen through the game's
            // own "choose another card" first (displaces the most recent placement),
            // THEN queue this card's select; the popup reopens with the swapped set.
            if (!wasOnField)
                BeginPickSwapReopen(gameHand, card);
            // Task #5 (double sound): a commit queues CardsHandUI.SelectCard, whose
            // AbilityCardUI.ToggleSelect plays the card's own serialized profile click
            // (mouseDownAudioItem, AbilityCardUI.cs:1184) one frame later — our thunk on
            // top made TWO sounds per placement. Play ours ONLY for the game-silent
            // re-drop of an already-placed card; the commit lets the game's click be
            // the one placement sound.
            // Re-seating a field card that a reopen deselected (grab-back → change of
            // mind → drop back into the slot) must ALSO commit, even after the reopen
            // queue already drained — its widget reads unselected then.
            bool willCommit = !wasOnField || _pickReopenBusy
                              || (card.GameCard != null && !card.GameCard.IsSelected);
            if (!willCommit)
                PlayCardSound(CardsConfig.CardPlaceSound.Value, card.transform);
            PlaceOnField(card);
            // Commit through the one seam. Also on a re-drop DURING a pick reopen
            // (task #11): the reopen's cancel deselected this card, so re-seating it
            // must re-select it — the queued select lands after the cancel resolves.
            if (willCommit)
                TryCommitPick(card, gameHand, wasOnField ? "re-drop" : "drop-slot");
        }
        else if (wasOnField)
        {
            // Task #5: the queued UnselectCard below plays the game's own profile click
            // (ToggleSelect deselect path) — no mod sound on top. During a pick reopen
            // the card is already deselected (the game plays nothing), so the soft undo
            // click keeps audible feedback for that case.
            if (_pickReopenBusy)
                PlayCardSound(CardsConfig.CardTakeBackSound.Value, card.transform);
            _fieldCards.Remove(card);
            RelayoutField();
            _fan.Add(card);
            AbilityCardUI widget = card.GameCard!;
            CAbilityCard ability = widget.AbilityCard;
            CardsHandUI handRef = gameHand;
            CardActionQueue.Enqueue(
                () => CardsGameApi.UnselectCard(handRef, ability),
                () =>
                {
                    VRLog.Info("Cards", $"Pick take-back ({CardsGameApi.Mode(handRef)}): '{CardsGameApi.CardName(widget)}' " +
                                        "via drop-slot → CardsHandUI.UnselectCard.");
                    _dirty = true;
                });
        }
        else
        {
            _fan.Add(card); // released in the void: animated return
        }
    }

    /// <summary>
    /// THE pick commit — both input paths (field drop, poke fallback) land here and
    /// nowhere else. Skips are logged: an already-selected widget (the game's
    /// SelectCard would no-op anyway) and a commit still in flight (no
    /// double-commit). The outcome is verified from the widget state after the
    /// queued call resolved.
    /// </summary>
    private void TryCommitPick(VRCard card, CardsHandUI gameHand, string seam)
    {
        AbilityCardUI? widget = card.GameCard;
        if (widget == null)
            return;
        // During a pick reopen (task #11) the widget's IsSelected is stale — the queued
        // cancel is about to deselect everything — so the commit must be queued anyway
        // (CardsHandUI.SelectCard itself no-ops on a genuinely selected card).
        if (widget.IsSelected && !_pickReopenBusy)
        {
            VRLog.Info("Cards", $"Pick commit skipped (seam={seam}) — '{CardsGameApi.CardName(widget)}' " +
                                "is already selected (no double-commit).");
            return;
        }
        if (_pickCommitBusy)
        {
            VRLog.Info("Cards", $"Pick commit ignored (seam={seam}) — a commit is already in flight " +
                                "(no double-commit, test #19 guard pattern).");
            return;
        }
        _pickCommitBusy = true;
        CAbilityCard ability = widget.AbilityCard;
        CardsHandUI handRef = gameHand;
        CardActionQueue.Enqueue(
            () => CardsGameApi.SelectCard(handRef, ability),
            () =>
            {
                _pickCommitBusy = false;
                bool selected = widget != null && widget.IsSelected;
                VRLog.Info("Cards", $"Pick commit ({CardsGameApi.Mode(handRef)}): '{(widget != null ? CardsGameApi.CardName(widget) : "?")}' " +
                                    $"via {seam} → CardsHandUI.SelectCard {(selected ? "accepted" : "rejected")}.");
                if (!selected && _fieldCards.Remove(card))
                {
                    RelayoutField();
                    _fan.Add(card);
                }
                _dirty = true;
            });
    }

    private void PlaceOnField(VRCard card)
    {
        if (!_fieldCards.Contains(card))
            _fieldCards.Add(card);
        RelayoutField();
    }

    /// <summary>Dedup key for the &gt;2-pick overflow log (item 28); -1 = not logged.</summary>
    private int _loggedFieldOverflow = -1;

    /// <summary>
    /// Home the pick candidates into the SLOT RECESSES (test #28): the first into the
    /// LEFT slot (Slot1), the second into the RIGHT slot (Slot2 — the burn-two-discard
    /// flows), any rare extra laid BESIDE Slot2 (logged once, never silently capped).
    /// Held cards are never re-homed (the phantom-ACCEPT lesson, see PlayTray.PlaceCard).
    /// </summary>
    private void RelayoutField()
    {
        int n = _fieldCards.Count;
        if (n <= 2)
            _loggedFieldOverflow = -1; // back within the two slots — re-arm the overflow log
        for (int i = 0; i < n; i++)
        {
            VRCard card = _fieldCards[i];
            if (card == null || card.IsHeld)
                continue;
            if (_tray.PlacePickCard(card, i) < 0 && i >= 2 && i != _loggedFieldOverflow)
            {
                _loggedFieldOverflow = i;
                VRLog.Info("Cards", $"Pick: {n} cards laid — extra card #{i + 1} placed BESIDE Slot2 " +
                                    "(both recesses full; graceful fallback, no cap).");
            }
        }
    }

    /// <summary>
    /// The slot the next pick candidate should land in (test #28): Slot1 while none is
    /// laid, Slot2 once the first is (the two-card burn flows). -1 once the game's
    /// authoritative wanted count (<see cref="CardsGameApi.PickCardsWanted"/>) is met —
    /// so a ONE-card burn stops pulsing/telegraphing the right slot the moment the left
    /// one is filled, while a two-card burn still wants both. Extras fall back beside
    /// Slot2 and get no dedicated slot glow.
    /// </summary>
    private int PickTargetSlot() =>
        _fieldCards.Count < CardsGameApi.PickCardsWanted() ? _fieldCards.Count : -1;

    // -------------------------------------------------------------- short rest --

    /// <summary>
    /// Present the short-rested card in the LEFT slot recess (test #25, item 1d;
    /// test #28 seating). Adopts the sacrifice widget through the SAME
    /// <see cref="AdoptedCard"/> path as every other physical card and homes it into
    /// Slot1 exactly like a single-card pick candidate (<see cref="PlayTray.PlacePickCard"/>)
    /// — so it reads exactly like the avoid-damage sacrifice. DISPLAY-ONLY: forced non-grabbable /
    /// non-poke (the zone loop resolves the same, this makes the intent explicit and
    /// covers the frames between a poll-driven swap and the next Rebuild). The card's
    /// live face is re-claimed off the docked DialogPopup automatically by
    /// <c>CardFace.Maintain</c> (CardFace.cs:210) — the popup's own buttons still
    /// commit the choice. Change-deduped Info line on present / redraw-swap.
    /// </summary>
    private void PresentShortRestCard(CardsHandUI hand, CAbilityCard lost)
    {
        AbilityCardUI? widget = CardsGameApi.ShortRestedCardWidget(hand);
        if (widget == null || widget.AbilityCard == null)
        {
            // The sacrifice's widget is not resolvable this frame (rare mid-swap) —
            // drop any stale display; the next poll re-presents once it exists.
            RemoveShortRestCard();
            return;
        }

        VRCard card = AdoptedCard(widget);
        bool changed = !ReferenceEquals(lost, _shortRestPresented);
        if (!ReferenceEquals(card, _shortRestCard))
        {
            if (_shortRestCard != null)
                _factory.Park(_shortRestCard); // redraw: park the previous sacrifice
            _shortRestCard = card;
        }

        card.Grabbable = false;      // display-only — the docked choice commits, not a drop
        card.PokeSelectEnabled = false;

        // Task #4b (collision safety): the sacrifice docks into the LEFT recess — if the
        // mod still shows a played card in EITHER slot it is stale by definition here
        // (PerformShortRest ran DeselectAllCards game-side, CardsHandUI.cs:771-area), so
        // re-home it to the fan BEFORE docking; the two must never overlap in a recess.
        // Normally SyncFromGameState already evicted the occupancy and the closed-fan
        // adopt in CardFan.SetCards re-parks the physical card; this covers any ordering
        // where the sync lags the present by a frame.
        for (int s = 0; s < 2; s++)
        {
            VRCard? stale = _tray.Occupant(s);
            if (stale == null || ReferenceEquals(stale, card))
                continue;
            _tray.RemoveCard(stale);
            _fan.Add(stale);
            VRLog.Info("Cards", $"Short rest: stale occupant re-homed from slot {s + 1} to the fan " +
                                "before docking the sacrifice (collision safety).");
        }

        card.gameObject.SetActive(true);
        _tray.PlacePickCard(card, 0); // LEFT slot recess (test #28) — display-only sacrifice

        if (changed)
        {
            bool swap = _shortRestPresented != null;
            VRLog.Info("Cards", $"Short rest: {(swap ? "REDREW —" : "presenting")} sacrificed card " +
                                $"'{CardsGameApi.CardName(widget)}' in the left slot " +
                                "(display-only; burn/redraw commits via the docked choice).");
            _shortRestPresented = lost;
        }
    }

    /// <summary>
    /// Tear down the short-rest sacrifice display (choice resolved / ShortRestedCard
    /// went null / mode-hand-scenario change). Parks the card (its face stays adopted
    /// for the pile viewer to reuse — the game restores it on hand teardown); the zone
    /// loop re-parks it harmlessly thereafter. Change-deduped Info line.
    /// </summary>
    private void RemoveShortRestCard()
    {
        if (_shortRestCard == null && _shortRestPresented == null)
            return;
        if (_shortRestCard != null)
        {
            _factory.Park(_shortRestCard);
            _shortRestCard = null;
        }
        if (_shortRestPresented != null)
        {
            VRLog.Info("Cards", "Short rest: sacrificed card removed from the board centre " +
                                "(choice resolved / short rest ended).");
            _shortRestPresented = null;
        }
    }

    /// <summary>
    /// Redraw watchdog (test #25, item 1d): <c>PerformFinalShortRest</c> re-points
    /// ShortRestedCard at the alternate card WITHOUT a mode / selection change, so
    /// nothing else would mark the driver dirty. Poll the accessor (guarded exactly
    /// like Rebuild — off during pick modes) and flip dirty on any present / swap /
    /// remove edge; Rebuild is the sole executor. Allocation-free, no-op when steady.
    /// </summary>
    private void PollShortRest(CardsHandUI? hand)
    {
        CAbilityCard? current = null;
        if (hand != null && !IsPickMode(CardsGameApi.Mode(hand)))
            current = CardsGameApi.ShortRestedCard(hand);
        if (!ReferenceEquals(current, _shortRestPresented))
            _dirty = true;
    }

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
            // The round wants up to two ability cards — mark the still-empty play
            // slots until they are filled. Off once the player chose long/short rest
            // (no cards wanted), already locked the selection in, or the selection phase
            // ended while the mode lingered stale (item B — no wanted hint after confirm).
            if (CardsGameApi.IsSelectionPhase(hand)
                && !CardsGameApi.IsLongRestSelected(hand)
                && !CardsGameApi.IsShortRestSelected(hand)
                && !CardsGameApi.IsSelectionReady(hand))
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
                int want = CardsGameApi.SelectionCardsStillWanted(hand);
                if (want > 0 && _tray.Occupant(0) == null)
                {
                    mask |= 1;
                    want--;
                }
                if (want > 0 && _tray.Occupant(1) == null)
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
            int want = CardsGameApi.PickCardsWanted();
            for (int i = _fieldCards.Count; i < want && i < 2; i++)
                mask |= 1 << i;
        }
        _tray.SetWantedSlots(mask);
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
                                $"{PhaseManager.PhaseType} (not selection): played cards docked read-only, fan unbound, " +
                                "nothing reclaimable until the next card-selection phase.");
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

    // Browse state (test #21): what was open when, so any mode/hand change closes
    // it deterministically (C: browse fans never survive a context switch).
    private bool _browseHeld;
    private CardsHandUI? _browseHand;
    private CardHandMode _browseMode;

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
            CloseBrowser("poked again");
            return;
        }
        OpenBrowser(kind, held: false, hand);
    }

    private void OnPileGrabOpened(PileKind kind, VRHand hand) => OpenBrowser(kind, held: true, hand);

    private void OnPileGrabReleased(PileKind kind, VRHand hand)
    {
        if (_browseHeld)
            CloseBrowser("grip released");
    }

    private void OpenBrowser(PileKind kind, bool held, VRHand? hand)
    {
        CardsHandUI? gameHand = CurrentHand();
        Transform? anchor = AnchorParent();
        if (gameHand == null || anchor == null || !CardsConfig.PileViewer.Value)
            return;
        CardHandMode mode = CardsGameApi.Mode(gameHand);
        if (IsPickMode(mode) || VRModeStateMachine.CurrentMode == VRMode.ModalUI)
            return; // modal pick flows / dialogs own the scene — browsing is non-modal only
        _browseHeld = held;
        _browseHand = gameHand;
        _browseMode = mode;
        // Held grab (item 5): the arc becomes a reading fan pinned to the grabbing
        // hand — "the pile in my hand". Poke-toggle stays a fixed head-relative wall.
        _browser.Open(kind, anchor, held ? hand : null);
        VRLog.Info("Cards", $"Pile browse OPEN: {kind} ({(held ? "held in hand" : "toggled")}, mode={mode}).");
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
    }

    private void CloseBrowser(string reason)
    {
        if (!_browser.IsOpen)
            return;
        VRLog.Info("Cards", $"Pile browse CLOSE ({reason}).");
        _browseHeld = false;
        _browseHand = null;
        ClearBrowseHover();
        _browser.Close();
        _dirty = true; // next rebuild parks the browsed cards
    }

    /// <summary>
    /// Rebuild-time browse refresh: close on any context change (mode/hand — C),
    /// otherwise mirror the authoritative pile into the arc. Content comes from the
    /// same widgets the 2D pile viewer re-parents (see CardsGameApi.GetPileWidgets),
    /// adopted read-only — Grabbable/PokeSelect stay off via the zone-flag loop.
    /// </summary>
    private void UpdateBrowser(CardsHandUI hand, CardHandMode mode)
    {
        if (!_browser.IsOpen)
            return;
        if (hand != _browseHand || mode != _browseMode)
        {
            CloseBrowser($"context change (mode={mode}, handSwitch={hand != _browseHand})");
            return;
        }

        bool burnt = _browser.Kind == PileKind.Burnt;
        CardsGameApi.GetPileWidgets(hand, burnt, _pileWidgetBuffer);
        _browseBuffer.Clear();
        for (int i = 0; i < _pileWidgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _pileWidgetBuffer[i];
            if (widget.AbilityCard == null || widget.IsLongRest)
                continue;
            _browseBuffer.Add(AdoptedCard(widget));
        }
        if (_browseBuffer.Count == 0)
        {
            CloseBrowser("pile empty");
            return;
        }
        PileKind kind = burnt ? PileKind.Burnt : PileKind.Discard;
        _browser.SetCards(_browseBuffer, $"{PileViewer.Caption(kind)} ({_browseBuffer.Count})");
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
        if (!CardsConfig.ActivePile.Value)
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
        _fieldCards.Clear();
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
