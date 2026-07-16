using System;
using System.Collections.Generic;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Core.Events;

/// <summary>Top-level VR interaction modes (ARCHITECTURE §8). FROZEN Phase-2 API.</summary>
internal enum VRMode
{
    /// <summary>No scenario loaded (main menu / map screens) — 2D UI + virtual mouse.</summary>
    Menu2D,

    /// <summary>Scenario running, not our decision point — spectate, world-grab, palm gives hand.</summary>
    TableIdle,

    /// <summary>Round-start card selection (pick 2 / rest) — fan + tray + ready button.</summary>
    CardSelection,

    /// <summary>Our turn, choosing a card half — played cards poke-able.</summary>
    HalfSelection,

    /// <summary>An action needs board targets — ray/touch picking, AoE rotation.</summary>
    BoardTargeting,

    /// <summary>The game locked its UI behind a modal (dialog/banner) — everything else waits.</summary>
    ModalUI
}

/// <summary>Which interaction primitives are active (per-mode policy). FROZEN Phase-2 API.</summary>
[Flags]
internal enum Interactors
{
    None = 0,
    Poke = 1 << 0,
    Ray = 1 << 1,
    Grab = 1 << 2,
    PalmGate = 1 << 3,
    All = Poke | Ray | Grab | PalmGate
}

/// <summary>
/// Which role a physical hand plays for the per-hand interactor policy (P5, MISSION A.4).
/// Dominant = the <c>[Hands] PrimaryHand</c> config hand (default Right).
/// </summary>
internal enum HandRole
{
    Dominant,
    NonDominant
}

/// <summary>Mode transition payload.</summary>
internal readonly struct VRModeChange
{
    public readonly VRMode From;
    public readonly VRMode To;

    public VRModeChange(VRMode from, VRMode to)
    {
        From = from;
        To = to;
    }
}

/// <summary>
/// The VR mode state machine (ARCHITECTURE §8). FROZEN Phase-2 API.
///
/// Driven entirely by <see cref="VREvents"/> (Choreographer message pump + state,
/// UI lock) plus a cheap per-frame scenario-presence poll (<see cref="Tick"/>).
/// Composition model — three independent inputs produce the effective mode:
/// <code>
///   inScenario  (Choreographer.s_Choreographer alive)        false → Menu2D
///   modal       (UIManager.ToggleLockUI observed, OR the     true  → ModalUI
///                P6 aux input: SetAuxModal — WorldUI's
///                catch-all fallback for unconverted windows)
///   targeting   (ChoreographerStateType ∈ TargetingStates)   true  → BoardTargeting
///   otherwise   → flow mode (TableIdle / CardSelection / HalfSelection from message map)
/// </code>
///
/// Menu2D therefore covers EVERYTHING before an actual combat scenario — main menu,
/// campaign/world map, guildmaster, merchant, level-up — because only the scenario
/// scene owns a Choreographer (see <see cref="ScenarioBoardExists"/>).
/// Priority: Menu2D &gt; ModalUI &gt; BoardTargeting &gt; flow.
///
/// EXTENSION (Phase-3 workers): the mapping tables are data — call
/// <see cref="MapMessage"/>, <see cref="SetTargetingState"/> and
/// <see cref="SetInteractorPolicy"/> from your module Init to refine behavior;
/// do NOT patch this class. All events fire on the main thread.
/// </summary>
internal static class VRModeStateMachine
{
    /// <summary>Current effective mode. Starts (and resets to) <see cref="VRMode.Menu2D"/>.</summary>
    public static VRMode CurrentMode { get; private set; } = VRMode.Menu2D;

    /// <summary>Fired on every effective-mode change (main thread, after CurrentMode updated).</summary>
    public static event Action<VRModeChange>? ModeChanged;

    /// <summary>
    /// THE canonical "an actual scenario board exists right now" signal (P6): the
    /// scenario Choreographer scene object is alive. Set in its Awake, nulled in its
    /// OnDestroy (decompiled GH.Runtime/Choreographer.cs:659,715); it only ever exists
    /// in the 'Game'/'Game_gamepad' scenario scenes (BOARD-INPUT.md §scenes).
    /// Deliberately NOT <c>CameraController.s_CameraController</c> — the orbit camera
    /// ALSO exists on the campaign/world map (decompiled ClickTrackerMap.cs:78 raycasts
    /// map locations through it), which was the test-#8 giant-map bug — and NOT
    /// <c>SaveData…CurrentGameState == EGameState.Scenario</c>, which derives from the
    /// adventure MapState phase (GlobalData.cs:563) and flips during loading/travel
    /// before any board exists. Rig, WorldUI and the mode machine all read THIS.
    /// </summary>
    public static bool ScenarioBoardExists => Choreographer.s_Choreographer != null;

    // ---- data-driven mapping tables -------------------------------------------------

    /// <summary>Engine message → flow mode (only TableIdle/CardSelection/HalfSelection make sense here).</summary>
    private static readonly Dictionary<CMessageData.MessageType, VRMode> MessageModeMap = new()
    {
        // Round start: pick 2 cards or rest (CARDS.md §4).
        { CMessageData.MessageType.PlayerToSelectAbilityCardsOrLongRest, VRMode.CardSelection },
        { CMessageData.MessageType.PlayersHaveSelectedAbilityCardsOrLongRest, VRMode.TableIdle },
        // A turn's action selection: the two played cards are poke-able (CARDS.md §5).
        { CMessageData.MessageType.StartTurn, VRMode.HalfSelection },
        { CMessageData.MessageType.ActionSelection, VRMode.HalfSelection },
        { CMessageData.MessageType.ActionSelectionPhaseStart, VRMode.HalfSelection },
        // Turn/round boundaries and rests fall back to spectating.
        { CMessageData.MessageType.EndTurn, VRMode.TableIdle },
        { CMessageData.MessageType.EndRound, VRMode.TableIdle },
        { CMessageData.MessageType.NextRound, VRMode.TableIdle },
        { CMessageData.MessageType.PlayerLongRested, VRMode.TableIdle },
        { CMessageData.MessageType.PlayerShortRested, VRMode.TableIdle },
    };

    /// <summary>Choreographer wait-states that mean "the board wants targets".</summary>
    private static readonly HashSet<Choreographer.ChoreographerStateType> TargetingStates = new()
    {
        Choreographer.ChoreographerStateType.WaitingForPlayerWaypointSelection,
        Choreographer.ChoreographerStateType.WaitingForAreaAttackFocusSelection,
        Choreographer.ChoreographerStateType.WaitingForPlayerPushWaypointSelection,
        Choreographer.ChoreographerStateType.WaitingForPlayerPullWaypointSelection,
        Choreographer.ChoreographerStateType.WaitingForTileSelected,
    };

    /// <summary>
    /// Per-mode interactor policy (both hands, unless a <see cref="HandPolicy"/> override
    /// exists for a role). Ray-always-on config is applied in <see cref="InteractorsFor(VRMode)"/>.
    /// P5: Menu2D gained Poke (the in-VR settings panel is poke-driven and reachable from
    /// the menu once the menu rig exists).
    /// </summary>
    private static readonly Dictionary<VRMode, Interactors> InteractorPolicy = new()
    {
        { VRMode.Menu2D, Interactors.Ray | Interactors.Poke },
        { VRMode.TableIdle, Interactors.Poke | Interactors.Grab | Interactors.PalmGate },
        { VRMode.CardSelection, Interactors.Poke | Interactors.Grab | Interactors.PalmGate },
        { VRMode.HalfSelection, Interactors.Poke | Interactors.Grab | Interactors.PalmGate },
        { VRMode.BoardTargeting, Interactors.Ray | Interactors.Poke },
        { VRMode.ModalUI, Interactors.Poke | Interactors.Ray },
    };

    /// <summary>
    /// Per-hand overrides on top of <see cref="InteractorPolicy"/> (P5, MISSION A.4).
    /// The FINAL per-mode/per-hand matrix is documented in docs/INTERFACES-P2.md §4.
    ///
    /// - CardSelection: the dominant hand keeps the far RAY (hero placement, board picks
    ///   during selection — P3a wish) and drops the palm gate (the fan lives on the
    ///   non-dominant palm); the non-dominant hand owns PalmGate/fan and shows NO laser,
    ///   so the beam never blinds the card fan (P3b wish).
    /// - BoardTargeting: only the dominant hand carries the ray/laser — the far pick
    ///   (BoardPick) exclusively consumes VRHands.PrimaryPick anyway; the non-dominant
    ///   hand keeps Poke for near-touch.
    /// </summary>
    private static readonly Dictionary<(VRMode, HandRole), Interactors> HandPolicy = new()
    {
        { (VRMode.CardSelection, HandRole.Dominant), Interactors.Poke | Interactors.Grab | Interactors.Ray },
        { (VRMode.CardSelection, HandRole.NonDominant), Interactors.Poke | Interactors.Grab | Interactors.PalmGate },
        { (VRMode.BoardTargeting, HandRole.Dominant), Interactors.Ray | Interactors.Poke },
        { (VRMode.BoardTargeting, HandRole.NonDominant), Interactors.Poke },
    };

    // ---- composed state -------------------------------------------------------------

    private static VRMode _flowMode = VRMode.TableIdle;
    private static bool _inScenario;
    private static bool _targeting;
    private static bool _modal;
    private static bool _auxModal;
    private static bool _attached;

    // ---- frozen query/extension surface ----------------------------------------------

    /// <summary>Set/override which flow mode an engine message maps to (Phase-3 extension point).</summary>
    public static void MapMessage(CMessageData.MessageType type, VRMode mode) => MessageModeMap[type] = mode;

    /// <summary>Mark/unmark a Choreographer state as board-targeting (Phase-3 extension point).</summary>
    public static void SetTargetingState(Choreographer.ChoreographerStateType state, bool isTargeting)
    {
        if (isTargeting) TargetingStates.Add(state);
        else TargetingStates.Remove(state);
    }

    /// <summary>Replace the interactor set for a mode (Phase-3/4 extension point). Applies to both hands unless a hand override exists.</summary>
    public static void SetInteractorPolicy(VRMode mode, Interactors set) => InteractorPolicy[mode] = set;

    /// <summary>Set/replace a per-hand override for a mode (P5 extension point, MISSION A.4).</summary>
    public static void SetHandInteractorPolicy(VRMode mode, HandRole role, Interactors set) =>
        HandPolicy[(mode, role)] = set;

    /// <summary>Remove a per-hand override (the mode-wide policy applies again).</summary>
    public static void ClearHandInteractorPolicy(VRMode mode, HandRole role) =>
        HandPolicy.Remove((mode, role));

    /// <summary>
    /// P6 extension point (catch-all modal fallback): a second, module-driven input into
    /// the modal composition, OR'd with the observed <c>UIManager.ToggleLockUI</c> state.
    /// WorldUI asserts it while an unconverted game window that expects interaction is
    /// open during a scenario (the window may not lock the UI at all — tutorials/events —
    /// yet the player must see and click it). Cleared automatically on scenario exit.
    /// Idempotent; safe to call every frame.
    /// </summary>
    public static void SetAuxModal(bool on)
    {
        if (_auxModal == on)
            return;
        _auxModal = on;
        Recompute();
    }

    /// <summary>
    /// Effective interactor set for a mode, both-hands view (honors the RayAlwaysOn
    /// config override). Where per-hand overrides exist this is their UNION — prefer
    /// <see cref="InteractorsFor(VRMode, HandRole)"/> for anything hand-specific.
    /// </summary>
    public static Interactors InteractorsFor(VRMode mode) =>
        InteractorsFor(mode, HandRole.Dominant) | InteractorsFor(mode, HandRole.NonDominant);

    /// <summary>Effective interactor set for one hand role in a mode (P5). Honors RayAlwaysOn.</summary>
    public static Interactors InteractorsFor(VRMode mode, HandRole role)
    {
        Interactors set = HandPolicy.TryGetValue((mode, role), out Interactors overrideSet)
            ? overrideSet
            : InteractorPolicy.TryGetValue(mode, out Interactors s) ? s : Interactors.All;
        if (Plugin.RayAlwaysOn.Value)
            set |= Interactors.Ray;
        return set;
    }

    // ---- lifecycle (VREventsModule only) ----------------------------------------------

    internal static void Attach()
    {
        if (_attached)
            return;
        _attached = true;
        VREvents.ChoreographerMessage += OnMessage;
        VREvents.ChoreographerStateChanged += OnChoreoState;
        VREvents.UiLockChanged += OnUiLock;
    }

    internal static void Detach()
    {
        if (!_attached)
            return;
        _attached = false;
        VREvents.ChoreographerMessage -= OnMessage;
        VREvents.ChoreographerStateChanged -= OnChoreoState;
        VREvents.UiLockChanged -= OnUiLock;

        _flowMode = VRMode.TableIdle;
        _inScenario = false;
        _targeting = false;
        _modal = false;
        _auxModal = false;
        Recompute();
    }

    /// <summary>
    /// Per-frame scenario-presence poll (cheap: one static null check). The scenario
    /// Choreographer is a scene object — its death is the robust "back to menu" signal.
    /// </summary>
    internal static void Tick()
    {
        // Unity-null check: destroyed MonoBehaviours compare equal to null.
        bool inScenario = ScenarioBoardExists;
        if (inScenario == _inScenario)
            return;

        _inScenario = inScenario;
        if (!inScenario)
        {
            // Leaving the scenario clears all scenario-scoped inputs.
            _flowMode = VRMode.TableIdle;
            _targeting = false;
            _modal = false;
            _auxModal = false;
        }
        Recompute();
    }

    // ---- event handlers ---------------------------------------------------------------

    private static void OnMessage(ChoreoMessageEvent e)
    {
        if (!MessageModeMap.TryGetValue(e.Type, out VRMode mode))
            return;
        _flowMode = mode;
        // A new flow decision point supersedes a stale targeting sub-state.
        if (mode == VRMode.CardSelection || mode == VRMode.TableIdle)
            _targeting = false;
        Recompute();
    }

    private static void OnChoreoState(ChoreoStateEvent e)
    {
        bool targeting = TargetingStates.Contains(e.State);
        if (targeting == _targeting)
            return;
        _targeting = targeting;
        Recompute();
    }

    private static void OnUiLock(UiLockEvent e)
    {
        if (e.Locked == _modal)
            return;
        _modal = e.Locked;
        Recompute();
    }

    private static void Recompute()
    {
        VRMode effective =
            !_inScenario ? VRMode.Menu2D :
            _modal || _auxModal ? VRMode.ModalUI :
            _targeting ? VRMode.BoardTargeting :
            _flowMode;

        if (effective == CurrentMode)
            return;

        VRMode from = CurrentMode;
        CurrentMode = effective;
        VRLog.Debug("Mode", $"{from} -> {effective}");

        Action<VRModeChange>? handler = ModeChanged;
        if (handler == null)
            return;
        try
        {
            handler(new VRModeChange(from, effective));
        }
        catch (Exception ex)
        {
            VRLog.Error("Mode", $"ModeChanged subscriber threw: {ex}");
        }
    }
}
