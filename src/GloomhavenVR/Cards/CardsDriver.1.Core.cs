using System.Collections.Generic;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

// CardsDriver is ONE class split across SIX files. This is part 1 — read it first: it holds the
// class doc, every shared field, the lifecycle and the board-tuning subscription.
//
//   CardsDriver.1.Core.cs          header, shared fields, lifecycle, board tuning (Part F)
//   CardsDriver.2.Update.cs        board pose guard, handlers, Update, tick attribution guard,
//                                  fan diagnostics, card audio
//   CardsDriver.3.Laser.cs         fan laser, fan hover split, hand-contact arbitration,
//                                  board/browse/active laser, modal input-block, slot snap preview
//   CardsDriver.4.Rebuild.cs       hand fan reorder, Rebuild, fly-to-pile, MP card-FX anchors,
//                                  BurnSlab, HookCard
//   CardsDriver.5.Interactions.cs  interactions, pick flows, short rest
//   CardsDriver.6.Flows.cs         overlay gate, wanted-slot hint, initiative to-do, long-rest
//                                  tracing, take-damage selection, long-rest turn pump,
//                                  pile browse, active cards, dev fake hand
//
// THE FILENAMES ARE NOT DECORATION. The csproj uses the SDK's default `**/*.cs` glob, so compile
// order follows the filename sort, and a partial class's members land in metadata in compile
// order. The digits keep the six parts concatenating back into the ORIGINAL member order, which
// is what lets refactor-guard.sh prove this split changed nothing. Renaming a part so it sorts
// differently silently reorders field initializers. Do not do it.
//
// SPLITTING A FILE DOES NOT SPLIT THE CALL ORDER. One coupling now spans a cut and is worth
// naming here because neither compiler nor guard will: TickInteractionsAndStatus (part 2) fixes
// the order `laser paths → UpdateHandContactArbitration` (part 3) — arbitration must run AFTER
// the laser paths have published their hover, and "these are all independent, let me tidy the
// tick calls" is exactly how that breaks (INVARIANTS-Cards §3). The other two order-critical
// pairs stay inside one part on purpose: Rebuild before TickBurnToPile (both part 4), and
// TickBoardPoseWatch last in Update (both part 2).

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
internal sealed partial class CardsDriver : MonoBehaviour
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

    // Issue 5 (fly-to-pile): the round cards docked in the PREVIOUS rebuild (a snapshot of
    // _halfBuffer), so the park sweep can tell a just-cleared PLAYED card from any other parked
    // card and fly it into its destination pile. _flyingToPile holds the cards mid-flight so the
    // sweep leaves them untouched (no re-park, no re-launch — the fly runs exactly once per card).
    private readonly HashSet<VRCard> _lastHalfCards = new();
    // Issue 1 (user): the SELECTED cards laid in the control-board SLOTS during card selection are
    // the "cards already lying there". Switching character must DISAPPEAR (scale-down) the outgoing
    // character's slot cards and APPEAR (scale-in, in place) the incoming character's — never a
    // fly-from-below. _lastTrayCards is the previous rebuild's slot occupants (the vanish set, like
    // _lastHalfCards for the docked round cards). _lastVisibleCards is every card that was in a
    // VISIBLE zone (fan / tray / half) last rebuild: a slot card that was already visible there
    // (e.g. one the player just DROPPED in from the fan) must GLIDE, not scale-in — only a card that
    // was parked/hidden (a character switch bringing back another character's cards) appears.
    private readonly HashSet<VRCard> _lastTrayCards = new();
    private readonly HashSet<VRCard> _lastVisibleCards = new();
    private readonly HashSet<VRCard> _flyingToPile = new();
    private const float FlyToPileSeconds = 0.4f;

    // Issue 1 (fly-to-pile "suddenly somewhere else" glitch): the last-known WORLD pose of every
    // adopted card while it was still visible in a zone, keyed by its game widget. When a card is
    // burned via damage its live VR card is often already parked (position lost) or recycled by the
    // time TickBurnToPile sees it in the burnt pile — the old fallback then flew a slab FROM THE
    // DISCARD PILE, i.e. it teleported to a different place first (the glitch). Now the fallback
    // slab starts from this recorded true position/rotation instead, holding that orientation for
    // the whole flight; with no recorded pose the animation is skipped (never a teleport).
    private readonly Dictionary<AbilityCardUI, Vector3> _lastCardWorldPos = new(16);
    private readonly Dictionary<AbilityCardUI, Quaternion> _lastCardWorldRot = new(16);

    // Issue 2 (character/turn switch board cards must not pop): suppress the docked-card appear/
    // disappear animation for exactly the first Rebuild after a fresh board build / teardown (the
    // scenario-load "no storm" guard, same philosophy as the buttons' _everShown). Set true on
    // enable / board switch / hand teardown; cleared at the end of each Rebuild so every LATER
    // change (the actual character/turn switches) animates.
    private bool _dockAnimSuppressed = true;

    // Issue B (user): a card burned via the TAKE-DAMAGE decision ("burn available/discarded
    // card") is a different flow from the turn-clear round-card sweep above — the game moves it
    // straight into the character's Lost pile and recycles the widget, so it just vanished with no
    // VR animation. TickBurnToPile watches the burnt pile's widget set per hand; any card newly
    // added there that the turn-clear path did NOT already claim flies to the BURNT stack with the
    // same over-the-board arc. _knownBurntWidgets is the previous-tick baseline (re-seeded on a
    // hand change so a hand's pre-existing burnt cards never animate retroactively).
    private readonly List<AbilityCardUI> _burntWidgetBuffer = new(8);
    private readonly HashSet<AbilityCardUI> _knownBurntWidgets = new();
    private CardsHandUI? _burnWatchHand;

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

    /// <summary>One-shot guard for the BoardTargeting Grab-policy grant (survives driver rebuilds).</summary>
    private static bool s_grabPolicyGranted;

    /// <summary>The live driver, for the static request entry points (<see cref="RequestBoardRecall"/>).
    /// Null while no cards driver exists (outside VR / before the module builds it).</summary>
    private static CardsDriver? Instance;

    private void OnEnable()
    {
        Instance = this;
        // USER BUG A (hardware log 4125-4193, "during movement destination selection I
        // could not grab the control board / options / VR settings bars — vibrates but
        // won't grab; laser grab dead too"): movement-destination selection is
        // Choreographer state WaitingForPlayerWaypointSelection → VRMode.BoardTargeting,
        // and the Phase-2 BoardTargeting policy carried NO Grab for EITHER hand — a
        // pre-panel-grab-era decision. Every grab primitive dies with it: proximity grip
        // on the tray/options/settings bars, PanelGrabHandle laser-carry
        // (ProximityGrabber.ForceGrab refuses on Enabled=false — the log's eleven
        // "LASER-CARRY armed" lines with no engage) and figure plucks
        // (FigureGrabDriver also grabs through ProximityGrabber). Meanwhile
        // RayGrabDriver's hover tint + haptic never consult Grabber.Enabled — the
        // "flickers and vibrates but won't grab" symptom. Desktop parity: the mouse can
        // always drag windows during targeting; per-object gates (VRCard.CanGrab,
        // GrabVisible) keep deciding WHAT is grabbable. Granted through the mode
        // machine's documented extension API (never a patch on the frozen class).
        if (!s_grabPolicyGranted)
        {
            s_grabPolicyGranted = true;
            VRModeStateMachine.SetHandInteractorPolicy(VRMode.BoardTargeting, HandRole.Dominant,
                Core.Events.Interactors.Ray | Core.Events.Interactors.Poke | Core.Events.Interactors.Grab);
            VRModeStateMachine.SetHandInteractorPolicy(VRMode.BoardTargeting, HandRole.NonDominant,
                Core.Events.Interactors.Poke | Core.Events.Interactors.Grab);
            VRLog.Info("Cards", "BoardTargeting interactor policy now includes Grab (both hands) — " +
                                "control-board/panel bars and figure grabs stay usable during " +
                                "move/target selection (user bug A).");
        }

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
        // Card presentation (edge-read fix): per-card toe-in + gaze-following bow apex. CardFan's
        // per-frame param signature would re-lay the OPEN fan anyway; subscribing keeps them on the
        // established live-apply path so a hand-edited cfg / menu step also logs the change.
        CardsConfig.FanFaceViewer.SettingChanged += OnFanTuningChanged;
        CardsConfig.FanGazeApexFollow.SettingChanged += OnFanTuningChanged;

        _tray.SwapRequested += OnSwapRequested;
        _tray.ConfirmRequested += OnConfirmRequested;
        _tray.UndoRequested += OnUndoRequested;
        _rest.ShortRestRequested += OnShortRestRequested;
        _rest.LongRestRequested += OnLongRestRequested;
        _half.PlayRequested += OnPlayRequested;
        _piles.PokeToggled += OnPileTogglePoked;
        _piles.GrabOpened += OnPileGrabOpened;
        _piles.GrabReleased += OnPileGrabReleased;
        _piles.ItemsOpening += () => CloseBrowser("items browse opened"); // one pile fan at a time (#6)

        _dirty = true;
    }

    private void OnDisable()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null; // static request entry points go no-op again
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
        CardsConfig.FanFaceViewer.SettingChanged -= OnFanTuningChanged;
        CardsConfig.FanGazeApexFollow.SettingChanged -= OnFanTuningChanged;
    }

    // ------------------------------------------------------------------ board tuning (Part F) --

    // Debug-menu / hand-edited per-board offsets live-apply through a dirty-flag consumed in
    // Update (the P2 threading rule: handlers only set flags). Position changes apply in place;
    // size/diameter changes rebuild just the affected buttons.
    private bool _applyControlOffsets;   // rest + confirm/undo X/Y/Z offset + group spacing (instant)
    private bool _applyControlRebuild;   // rest diameter / confirm-undo size / button SHAPE (rebuild the buttons)
    private int _restTuningVersion;      // last-seen ButtonTuning.Version — rest [RestButtons] W/H/D/Travel live-rebuild
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
                CardsConfig.ItemUseSlotOffset(b).SettingChanged += OnControlOffsetChanged;   // item-use slot = in-place move
                CardsConfig.ItemCardOffset(b).SettingChanged += OnControlOffsetChanged;      // item fan/held pose (read live by ItemsPile)
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
                CardsConfig.ItemUseSlotOffset(b).SettingChanged -= OnControlOffsetChanged;
                CardsConfig.ItemCardOffset(b).SettingChanged -= OnControlOffsetChanged;
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
            _restTuningVersion = WorldUI.ButtonTuning.Version; // this rebuild already reflects current [RestButtons] geometry
            VRLog.Info("Cards", $"Debug live-apply [{b}]: rebuilt Generic Confirm/Undo ({CardsConfig.GenericButtonShape(b).Value}, " +
                                $"size {CardsConfig.ConfirmUndoSize(b).Value:F3} m) + Rest buttons ({CardsConfig.RestButtonShape(b).Value}, " +
                                $"diameter {CardsConfig.RestButtonDiameter(b).Value:F3} m).");
        }
        // [RestButtons] geometry live-apply (W/H/D/Travel): the rest keycaps read ButtonTuning in
        // EnsureBuilt, so a settings-panel stepper edit (ButtonTuning.Version bump) rebuilds JUST the
        // rest caps on their existing anchors — no restart. This is a REST-ONLY rebuild on purpose:
        // it must NOT route through PlayTray.RebuildAttachedControls (which would bump the tray's own
        // _tuningVersion and starve PlayTray.ApplyButtonTuningIfChanged of the gear/follow dashboard
        // rebuild). PurgeDeadLaserTargets drops the destroyed caps' stale laser entries; EnsureBuilt
        // re-registers the fresh ones and logs the applied W/H/D/Travel.
        else if (_restTuningVersion != WorldUI.ButtonTuning.Version)
        {
            _restTuningVersion = WorldUI.ButtonTuning.Version;
            _rest.Destroy();
            _tray.PurgeDeadLaserTargets();
            _rest.EnsureBuilt(_tray);
        }
        if (_applyControlOffsets)
        {
            _applyControlOffsets = false;
            _rest.SetOffset(CardsConfig.RestButtonOffset(b).Value, CardsConfig.RestButtonSpacing(b).Value);
            _tray.SetConfirmUndoOffset(CardsConfig.ConfirmUndoOffset(b).Value, CardsConfig.GenericButtonSpacing(b).Value);
            _tray.SetItemUseSlotOffset(CardsConfig.ItemUseSlotOffset(b).Value); // items rework: move the use slot in place
            // ItemCardOffset (req #2) is read LIVE by ItemsPile every Tick, so an open item fan moves
            // immediately with no push needed here — logged for parity with the other control offsets.
            VRLog.Info("Cards", $"Debug live-apply [{b}]: rest offset {CardsConfig.RestButtonOffset(b).Value} " +
                                $"(spacing {CardsConfig.RestButtonSpacing(b).Value:F3} m), confirm/undo offset " +
                                $"{CardsConfig.ConfirmUndoOffset(b).Value} (spacing {CardsConfig.GenericButtonSpacing(b).Value:F3} m), " +
                                $"item-use slot offset {CardsConfig.ItemUseSlotOffset(b).Value}, " +
                                $"item-card offset {CardsConfig.ItemCardOffset(b).Value}.");
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
}
