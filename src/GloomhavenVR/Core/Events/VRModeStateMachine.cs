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

    /// <summary>
    /// True while the mod stands the player inside a ROOM OF ITS OWN that is not a scenario board —
    /// today exactly the 3D map room (<c>WorldUI.MapRoom.MapRoomDriver</c>, which pushes this from
    /// its Engage/StandDown so <c>Core</c> keeps no reference to <c>WorldUI</c>'s lifetime).
    /// </summary>
    public static bool ModRoomStands => _modRoom;

    /// <summary>
    /// "THE PLAYER IS STANDING AT A TABLE IN A ROOM" — the premise a scenario board used to be
    /// asked as a proxy for, everywhere in the mod that means *presence* rather than *game state*.
    ///
    /// <para>WHY IT EXISTS. The composition above forces <see cref="VRMode.Menu2D"/> wherever no
    /// Choreographer is alive, and the campaign map screen has none by construction — which is
    /// exactly why <c>MapRoomDriver</c>'s own gate is a POSITIVE MapChoreographer signal. So the 3D
    /// map room was Menu2D, and every subsystem that stands down there stood down: flight ("no
    /// scene to fly through"), world grab ("no table exists"), snap turn ("there is no board in
    /// front of you"), the environment, the haunt, and — ModBuild 178 — the whole floated-window
    /// layer. Each of those sentences is TRUE OF THE FLAT 2D MENU and FALSE in the map room, which
    /// builds precisely the thing they assume is absent. They were never wrong about the menu; they
    /// were reading the wrong fact.</para>
    ///
    /// <para>TWO CONSUMERS, ONE PREDICATE. Sites that key off the MODE (interactor policy,
    /// <c>ButtonCluster</c>, <c>WorldTooltips</c>, <c>BoardPick</c>, the locomotion guards) get it
    /// through the composition above — the map room resolves to <see cref="VRMode.TableIdle"/>, or
    /// <see cref="VRMode.ModalUI"/> while a window floats. Sites that ask about PRESENCE directly
    /// (<c>ModalFallback</c>, <c>SkyAlternative</c>, <c>Haunt</c>) read this property. There is no
    /// third mechanism, and nothing asks the question twice.</para>
    ///
    /// <para>WHAT IS DELIBERATELY *NOT* AFFECTED: the flat screen. It does not consult the mode at
    /// all in the map room — <c>FlatScreen.ScreenWanted</c> returns false on
    /// <c>MapRoomDriver.Active</c> before the mode is ever tested, because the room and the flat
    /// map render must never both own the parchment. ModBuild 177 assumed the opposite and kept
    /// the map room in Menu2D to "protect" a screen that was already off; that reasoning was wrong
    /// on a fact, and the effect was a room with no UI in it at all.</para>
    /// </summary>
    public static bool TableInFrontOfPlayer => ScenarioBoardExists || _modRoom;

    /// <summary>
    /// Push input for <see cref="ModRoomStands"/> (main thread), from the room's own
    /// Engage/StandDown. Recomputes: the map room's effective mode is
    /// <see cref="VRMode.TableIdle"/>, not <see cref="VRMode.Menu2D"/>.
    /// </summary>
    internal static void SetModRoom(bool standing)
    {
        if (standing == _modRoom)
            return;
        _modRoom = standing;
        VRLog.Info("Mode", $"Mod room {(standing ? "STANDS" : "gone")} — TableInFrontOfPlayer is now "
                           + $"{TableInFrontOfPlayer} (scenario board: {ScenarioBoardExists}). "
                           + "The player is in a room of the mod's own, so locomotion, the "
                           + "environment, the floated-window layer and the interactor policy all "
                           + "treat it as one.");
        Recompute();
    }

    /// <summary>
    /// True while the Choreographer sits in one of the <see cref="TargetingStates"/> (the board
    /// is actively waiting for a waypoint/focus/push/pull/tile pick). Additive query for the
    /// fingertip-ping arbitration (BoardClickDriver.SelectionPhaseActive): it exposes the RAW
    /// targeting input rather than <see cref="CurrentMode"/> because the composition masks
    /// targeting behind ModalUI (priority Menu2D &gt; ModalUI &gt; BoardTargeting) — a modal
    /// floating over an active tile selection must still count as "selection phase", or a
    /// fingertip touch under it would ping where the game expects a pick.
    /// </summary>
    public static bool TargetingActive => _targeting;

    /// <summary>
    /// Is <paramref name="state"/> one of the <see cref="TargetingStates"/> — i.e. does that
    /// Choreographer wait-state mean "the board wants a pick"? The TABLE, asked about a state the
    /// caller read itself, as opposed to <see cref="TargetingActive"/>, which is this class's
    /// EVENT-DRIVEN MIRROR of the same table applied to the live state.
    ///
    /// <para>Why both exist. The mirror is the right answer for presentation policy (interactor
    /// sets, ping arbitration): it is cheap, it is what <see cref="CurrentMode"/> is composed from,
    /// and it is deliberately force-cleared by a superseding flow message
    /// (<see cref="OnMessage"/>). But a gate that TAKES SOMETHING AWAY from the player must not be
    /// able to fire on a stale mirror, so <c>Board.CharacterFocus.PinnedActor</c> reads
    /// <c>Choreographer.m_WaitState.m_State</c> straight from the game and brings it here to be
    /// classified. One table, two readers — the alternative was a second copy of these five enum
    /// members in Board, which is exactly the drift <c>scripts/check-mirrors.sh</c> exists to
    /// catch.</para>
    /// </summary>
    public static bool IsTargetingState(Choreographer.ChoreographerStateType state) =>
        TargetingStates.Contains(state);

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
    /// Test #19: the DOMINANT hand additionally always gets Ray — guaranteed centrally
    /// in <see cref="InteractorsFor(VRMode, HandRole)"/>, so rows here without Ray only
    /// remove the NON-dominant laser.
    /// </summary>
    private static readonly Dictionary<VRMode, Interactors> InteractorPolicy = new()
    {
        { VRMode.Menu2D, Interactors.Ray | Interactors.Poke },
        { VRMode.TableIdle, Interactors.Poke | Interactors.Grab | Interactors.PalmGate },
        { VRMode.CardSelection, Interactors.Poke | Interactors.Grab | Interactors.PalmGate },
        { VRMode.HalfSelection, Interactors.Poke | Interactors.Grab | Interactors.PalmGate },
        { VRMode.BoardTargeting, Interactors.Ray | Interactors.Poke },
        // ModalUI gained Grab (test #15): the tray dashboard's grab handle must keep
        // working while a dialog floats (move/scale the tray mid-dialog). CARD grabs
        // stay blocked there via the per-grabbable gate (VRCard.CanGrab refuses in
        // ModalUI; the tray's PanelGrabHandle accepts) — the interactor only provides the
        // primitive, per-object policy decides who takes the grip.
        { VRMode.ModalUI, Interactors.Poke | Interactors.Ray | Interactors.Grab },
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

    /// <summary>Backing latch for <see cref="ModRoomStands"/>. NOT cleared by <see cref="Detach"/>
    /// or by the scenario poll: it is owned by the room that raised it, and that room's own
    /// StandDown is the only thing that knows when it is gone (a rig teardown, a scene change and
    /// an MR toggle all route through it).</summary>
    private static bool _modRoom;

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
    /// Effective interactor set for a mode, both-hands view. Where per-hand overrides
    /// exist this is their UNION — prefer <see cref="InteractorsFor(VRMode, HandRole)"/>
    /// for anything hand-specific.
    /// </summary>
    public static Interactors InteractorsFor(VRMode mode) =>
        InteractorsFor(mode, HandRole.Dominant) | InteractorsFor(mode, HandRole.NonDominant);

    /// <summary>Effective interactor set for one hand role in a mode (P5).</summary>
    public static Interactors InteractorsFor(VRMode mode, HandRole role)
    {
        Interactors set = HandPolicy.TryGetValue((mode, role), out Interactors overrideSet)
            ? overrideSet
            : InteractorPolicy.TryGetValue(mode, out Interactors s) ? s : Interactors.All;
        // Laser persistence (hardware test #19): the DOMINANT hand's far ray is
        // unconditionally part of its set, in EVERY mode — the tables above only
        // distribute Poke/Grab/PalmGate and the non-dominant ray. The selection laser
        // silently vanished mid-attack because a single-target attack waits in
        // Choreographer state WaitingForCardSelection — not a TargetingStates member —
        // so the mode stayed HalfSelection, whose policy carried no Ray. Desktop
        // parity: the mouse can always point/click and the game gates by state, so an
        // always-on dominant ray is exactly the mouse contract. Transient suppression
        // (held object, pose loss) is level-derived inside RayInteractor.Active,
        // never via this mask.
        if (role == HandRole.Dominant)
            set |= Interactors.Ray;
        // ONE LASER, AND IT BELONGS TO THE ACTIVE HAND (user ruling 2026-08-03: "Aktuell habe ich
        // immer zwei Laser aus beiden Händen! Das will ich nicht. Nur die aktive Hand (rechts oder
        // Linkshänder-Modus) soll einen Laser haben."). Three separate paths used to hand the OFF
        // hand a ray — the Menu2D and ModalUI mode rows list Ray for both hands, and the old
        // [Hands] RayAlwaysOn override (since deleted as a provable no-op) was applied regardless
        // of role — so this is enforced HERE, once, after everything else has had its say. The
        // tables above keep their meaning for the dominant hand; for the non-dominant one the ray
        // is simply not on offer, in any mode, under any config.
        if (role == HandRole.NonDominant)
            set &= ~Interactors.Ray;
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
            !_inScenario && !_modRoom ? VRMode.Menu2D :
            _modal || _auxModal ? VRMode.ModalUI :
            _targeting ? VRMode.BoardTargeting :
            // THE MAP ROOM HAS NO FLOW OF ITS OWN. _flowMode is a CARD/TURN state driven by
            // Choreographer messages; the map screen sends none, so whatever it holds here is a
            // leftover from the last scenario. Name the map room's mode explicitly instead of
            // inheriting that: TableIdle is "you are standing at a table, spectating", which is
            // exactly true here. (It is also what _flowMode resets to on scenario exit, so this
            // changes no observed value today — it removes the DEPENDENCE on that reset.)
            !_inScenario ? VRMode.TableIdle :
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
