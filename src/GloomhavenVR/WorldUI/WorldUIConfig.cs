using BepInEx.Configuration;
using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Phase-3c configuration, bound into the module's own file. P5 (MISSION A.9): now
/// created through the canonical <see cref="ModuleConfig.Create"/> helper —
/// <c>BepInEx/config/dev.gloomhavenvr.worldui.cfg</c> (renamed from the pre-P5
/// <c>worldui.gloomhavenvr.cfg</c>). Every physicalized surface is individually
/// toggleable (ARCHITECTURE §7) plus a <see cref="Master"/> switch over all of them;
/// entries are read live, so flips take effect on the next relevant rebuild.
/// </summary>
internal static class WorldUIConfig
{
    private static ConfigFile? _file;

    // ---- master ------------------------------------------------------------------------
    /// <summary>Master switch over ALL WorldUI surfaces (in-VR settings panel binds this).</summary>
    internal static ConfigEntry<bool> Master = null!;

    // ---- surfaces (each individually toggleable) ---------------------------------------
    internal static ConfigEntry<bool> ButtonCluster = null!;
    internal static ConfigEntry<bool> InitiativeTrack = null!;
    internal static ConfigEntry<bool> ElementBoard = null!;
    internal static ConfigEntry<bool> CombatLog = null!;
    internal static ConfigEntry<bool> Objectives = null!;
    internal static ConfigEntry<bool> Dialogs = null!;
    internal static ConfigEntry<bool> StatPanels = null!;
    internal static ConfigEntry<bool> PropInfoCards = null!;
    internal static ConfigEntry<bool> EnemyReveal = null!;
    internal static ConfigEntry<bool> DecisionDock = null!;
    internal static ConfigEntry<bool> TrayNativeControls = null!;
    internal static ConfigEntry<bool> ActorBars = null!;

    /// <summary>Actor bars keep a fixed board-space size (no distance growth) — test #14 item 4.</summary>
    internal static ConfigEntry<bool> BarFixedSize = null!;
    internal static ConfigEntry<bool> WristHud = null!;
    internal static ConfigEntry<bool> FlatScreen = null!;
    internal static ConfigEntry<bool> Tooltips = null!;

    /// <summary>User #7c: enable/disable the action-phase element/ability explanation hints
    /// (the world-space tooltip presentation). Wired to the in-VR settings panel; read live.</summary>
    internal static ConfigEntry<bool> ActionElementHints = null!;

    // ---- behavior ----------------------------------------------------------------------
    /// <summary>Keep the game in mouse mode so `Game` (not `Game_gamepad`) scenes load.</summary>
    internal static ConfigEntry<bool> ForceMouseMode = null!;

    /// <summary>World-canvas scale in millimeters per uGUI pixel (at diorama scale 1).</summary>
    internal static ConfigEntry<float> CanvasScaleMm = null!;

    /// <summary>Initiative track 3D depth effect: max total front-to-back z spread (px), live-tunable.</summary>
    internal static ConfigEntry<float> InitiativeDepthMaxSpreadPx = null!;

    /// <summary>
    /// User #11b/#14: the docked decision row's vertical gap. It now drives the docked
    /// widget block's PLACEMENT (a quantity the mod owns): the interactive-widget block top
    /// is docked this many uGUI px (in the row's own scale) BELOW the board's lower edge /
    /// prompt line. Smaller = the buttons ride UP toward the prompt; larger = they drop.
    /// Live-applied by <see cref="Surfaces.DecisionDockSurface"/> (re-runs placement on
    /// change). Range 0–120 (widened from 60 so the stepper closes the observed gap).
    /// </summary>
    internal static ConfigEntry<float> DecisionRowGapPx = null!;

    /// <summary>
    /// User ("Ich will die Größe der Infotafeln, die beim Mouseover erscheinen, einstellen
    /// können"): world SIZE factor of the hover INFO panels — the board-hover cards the game
    /// raises through <c>UITextInfoPanel</c> ("2 Gold", "Geschlossene Tür", chest, obstacle,
    /// pressure plate …) and <c>UIPropInfoPanel</c> (trap / quest item), plus the card-action
    /// element hint on the tooltip canvas. Live-tunable in the debug menu (Anzeige →
    /// Infotafel-Größe); read on every placement tick, so the panel resizes immediately /
    /// at the latest on the next hover. Default <see cref="DefaultHoverInfoScale"/> = the
    /// pre-existing hard-coded factor, so nothing changes until the dial is touched.
    /// </summary>
    internal static ConfigEntry<float> HoverInfoScale = null!;

    /// <summary>
    /// The hover-info size factor that was HARD-CODED before the dial existed
    /// (<c>PropInfoSurface.PlaceWatch</c>: <c>PanelLayout.WorldScale * 0.6f</c>). It is the
    /// entry's default AND the normalization base for the tooltip canvas in
    /// <see cref="WorldTooltips"/>, so ONE dial scales every mouseover info panel and the
    /// factory value reproduces today's sizes EXACTLY (0.6 / 0.6 = 1x on the tooltip path).
    /// </summary>
    internal const float DefaultHoverInfoScale = Defaults.HoverInfoScale;

    /// <summary>Last hover-info factor we logged (change-dedup for the hardware log).</summary>
    private static float _loggedHoverInfoScale = float.NaN;

    /// <summary>
    /// Read the live hover-info size factor AND log it once per CHANGE. Both consumers
    /// (<see cref="Surfaces.PropInfoSurface"/> and <see cref="WorldTooltips"/>) read through
    /// here every placement tick, so the log proves in the hardware log WHICH size was actually
    /// applied to a shown panel — a value written into the cfg but never applied (panel not
    /// converted, hints toggled off) produces no line. Deduped because the callers run per frame.
    /// </summary>
    internal static float HoverInfoScaleLive()
    {
        float v = HoverInfoScale.Value;
        if (float.IsNaN(_loggedHoverInfoScale) || System.Math.Abs(v - _loggedHoverInfoScale) > 0.001f)
        {
            _loggedHoverInfoScale = v;
            VRLog.Info("WorldUI",
                $"Hover info panel size applied: {v:0.00}x (default {DefaultHoverInfoScale:0.00}x) — " +
                "mouseover info panels (TextInfoPanel/PropInfoPanel) and the card element hint.");
        }
        return v;
    }

    /// <summary>
    /// User (hardware, "Die Gegnerinfo spawnt meist genau hinter dem Controllboard"): how far the
    /// enemy round reveal must clear the CONTROL BOARD's top edge, in real metres measured AT THE
    /// BOARD (the sight-line gap the player sees between the board's top edge and the bottom of
    /// the reveal). Live-tunable in the debug menu (Panels -> Initiative); read on every reveal
    /// spawn/follow by <see cref="Surfaces.EnemyRevealSurface"/>.
    /// </summary>
    internal static ConfigEntry<float> EnemyRevealBoardClearance = null!;

    /// <summary>Auto-show the floating 2D screen while no scenario runs (Menu2D mode).</summary>
    internal static ConfigEntry<bool> FlatScreenAutoShow = null!;

    /// <summary>Item 9: flat monitor mirrors ONLY the HMD left eye (no 2D-menu composite).</summary>
    internal static ConfigEntry<bool> DesktopMirrorLeftEye = null!;

    /// <summary>Item 10: wrist overview HUD pose, live-tunable in the debug menu's "Wrist" category.</summary>
    internal static ConfigEntry<float> WristHudPitch = null!;
    internal static ConfigEntry<float> WristHudYaw = null!;
    internal static ConfigEntry<float> WristHudRoll = null!;
    internal static ConfigEntry<float> WristHudOffsetX = null!;
    internal static ConfigEntry<float> WristHudOffsetY = null!;
    internal static ConfigEntry<float> WristHudOffsetZ = null!;

    /// <summary>Show the intro (pre-menu scenes) on the floating screen in VR too.</summary>
    internal static ConfigEntry<bool> ShowIntro = null!;

    /// <summary>Flat-screen width in real-world meters (test #6: 2.2 m default).</summary>
    internal static ConfigEntry<float> ScreenWidth = null!;

    /// <summary>Flat-screen distance from the head in real-world meters.</summary>
    internal static ConfigEntry<float> ScreenDistance = null!;

    /// <summary>Freeze the pointer from trigger-press to release so uGUI sees a CLICK, not a tremor drag.</summary>
    internal static ConfigEntry<bool> ClickLatch = null!;

    /// <summary>Disable the physical desktop mouse while VR runs so only the VR laser drives the pointer.</summary>
    internal static ConfigEntry<bool> SuppressPhysicalMouse = null!;

    /// <summary>Opacity multiplier for the campaign map's Wind/Clouds ambiance particles (0 = invisible, 1 = full).</summary>
    internal static ConfigEntry<float> MapWindOpacity = null!;

    /// <summary>Ray movement (degrees off the press direction) that opens the click latch into a drag.</summary>
    internal static ConfigEntry<float> DragUnlockDegrees = null!;

    /// <summary>The ray must stay beyond <see cref="DragUnlockDegrees"/> this long before the latch opens.</summary>
    internal static ConfigEntry<float> DragUnlockSeconds = null!;

    /// <summary>Fingertip poke on the flat screen clicks at the poked position.</summary>
    internal static ConfigEntry<bool> PokeClick = null!;

    /// <summary>
    /// User #12: push-in confirmation depth (mm) for fingertip pokes on converted flat
    /// uGUI buttons — plane contact only ARMS (pointerDown, pressed visual); the click
    /// fires once the fingertip pushed this far THROUGH the plane. 0 = legacy instant
    /// click on contact. Read live per tick by <c>Hands.Interact.PokeInteractor</c>.
    /// </summary>
    internal static ConfigEntry<float> PokePressDepthMm = null!;

    /// <summary>
    /// User #13b: decision-dock buttons use the DELIBERATE v1 poke press — contact only
    /// arms (pressed visual), the click fires on the conscious WITHDRAWAL back out of
    /// the plane, sweep-throughs cancel silently. Read live per press by
    /// <c>Hands.Interact.PokeInteractor</c> for canvases the decision dock registered
    /// in <c>Hands.Interact.DeliberatePokeSurfaces</c>; every other surface keeps the
    /// <see cref="PokePressDepthMm"/> push-in behaviour.
    /// </summary>
    internal static ConfigEntry<bool> DecisionPokeDeliberate = null!;

    /// <summary>Click delivery: "execute" (ExecuteEvents, default) | "virtualmouse" | "both".</summary>
    internal static ConfigEntry<string> ClickMode = null!;

    /// <summary>Legacy: world panels re-orient with the rig yaw per frame (HUD-like). Default false (P6).</summary>
    internal static ConfigEntry<bool> PanelsFollowView = null!;

    /// <summary>Hover hex-hint (prop-info) panels drift to the center of view with lazy damped motion while shown.</summary>
    internal static ConfigEntry<bool> HexHintFollowView = null!;

    // ---- combat log panel layout (test #19: movable/scalable/pinnable like the tray) -----
    /// <summary>Combat log anchor mode: true = re-derive from the seat anchor, false = pinned in the world.</summary>
    internal static ConfigEntry<bool> CombatLogFollow = null!;

    /// <summary>Combat log offset from the table anchor along the seat forward, real meters.</summary>
    internal static ConfigEntry<float> CombatLogForward = null!;

    /// <summary>Combat log offset to the seat right, real meters.</summary>
    internal static ConfigEntry<float> CombatLogRight = null!;

    /// <summary>Combat log height above the table plane, real meters.</summary>
    internal static ConfigEntry<float> CombatLogUp = null!;

    /// <summary>Combat log size multiplier (two-hand resize; clamped 0.5–2).</summary>
    internal static ConfigEntry<float> CombatLogScale = null!;

    /// <summary>User hid the combat log via its X button / settings toggle — do not auto-appear.</summary>
    internal static ConfigEntry<bool> CombatLogUserClosed = null!;

    /// <summary>Modal fallback style: "window" (float only the dialog window) | "screen" (full flat screen).</summary>
    internal static ConfigEntry<string> ModalStyle = null!;

    /// <summary>Self-rescue: hold the non-dominant A/X in a scenario to toggle the flat screen.</summary>
    internal static ConfigEntry<bool> ManualScreenChord = null!;

    /// <summary>Hold duration (seconds) for <see cref="ManualScreenChord"/>.</summary>
    internal static ConfigEntry<float> ManualScreenChordSeconds = null!;

    /// <summary>Demote fullscreen SolidColor clears of non-base captured cameras to Depth (test #10).</summary>
    internal static ConfigEntry<bool> DemoteOverlaySolidClears = null!;

    /// <summary>Split the flat screen into a UI glass layer over a stereo background layer (test #18).</summary>
    internal static ConfigEntry<bool> ScreenLayerSplit = null!;

    /// <summary>VR loading indicator: the game's rotating spinner in front of the HMD during loads,
    /// flat screen suppressed, background loading priority lowered (smaller hitches).</summary>
    internal static ConfigEntry<bool> LoadingIndicator = null!;

    /// <summary>True when clicks go through ExecuteEvents (ClickMode execute/both).</summary>
    internal static bool ExecuteClicks =>
        !string.Equals(ClickMode.Value, "virtualmouse", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>True when button state goes through the virtual mouse (ClickMode virtualmouse/both).</summary>
    internal static bool VirtualMouseButtons =>
        !string.Equals(ClickMode.Value, "execute", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Part 10 kill-switch: float UNKNOWN in-scenario windows (IDs not enrolled in
    /// FallbackIds / the polls / any claim) after a short grace, instead of letting them
    /// wait invisibly on the hidden 2D stack (the ItemCardPicker silent-deadlock class).
    /// The explicit enrollments (story/level-message/dialog/reward polls, the
    /// GlobalErrorMessage poll) are NOT gated by this — they stay on regardless.
    /// </summary>
    internal static ConfigEntry<bool> CatchAllModals = null!;

    /// <summary>True when fallback windows float individually ([WorldUI] ModalStyle != "screen").</summary>
    internal static bool ModalWindowStyle =>
        !string.Equals(ModalStyle.Value, "screen", System.StringComparison.OrdinalIgnoreCase);

    // ---- on-screen keyboard --------------------------------------------------------------
    /// <summary>Show the game's own on-screen keyboard whenever a text field takes focus.</summary>
    internal static ConfigEntry<bool> KeyboardEnabled = null!;

    /// <summary>Capitalise the first letter of each word (the game's keyboard has no shift key).</summary>
    internal static ConfigEntry<bool> KeyboardAutoCase = null!;

    // ---- dev ---------------------------------------------------------------------------
    /// <summary>Spawn the world-panel layout with dummy content on the desktop.</summary>
    internal static ConfigEntry<bool> DevShowAllPanels = null!;

    /// <summary>Apply the real canvas conversions in dev mode without an HMD.</summary>
    internal static ConfigEntry<bool> DevForceConvert = null!;

    internal static void Bind()
    {
        if (_file != null)
            return;

        _file = ModuleConfig.Create("worldui");

        Master = _file.Bind("WorldUI", "Master", Defaults.Master,
            "Master switch for the whole physicalized interface (all surfaces below AND the " +
            "floating 2D screen). Off = the game's own 2D screen-space UI stays untouched.");
        ButtonCluster = _file.Bind("WorldUI", "ButtonCluster", Defaults.ButtonCluster,
            "Physical Ready/Undo/Skip buttons at the table edge.");
        InitiativeTrack = _file.Bind("WorldUI", "InitiativeTrack", Defaults.InitiativeTrack,
            "Initiative track as a world-space panel above the table.");
        ElementBoard = _file.Bind("WorldUI", "ElementBoard", Defaults.ElementBoard,
            "Element infusion board docked on the control board's left column, below the " +
            "objectives panel (floating world panel only as the no-tray fallback).");
        CombatLog = _file.Bind("WorldUI", "CombatLog", Defaults.CombatLog,
            "Combat log as a world-space panel at the table's far side.");
        Objectives = _file.Bind("WorldUI", "Objectives", Defaults.Objectives,
            "Scenario objectives as a world-space panel at the table's far side.");
        Dialogs = _file.Bind("WorldUI", "Dialogs", Defaults.Dialogs,
            "Confirmation dialogs as world-space modals in front of the HMD (poke yes/no).");
        StatPanels = _file.Bind("WorldUI", "StatPanels", Defaults.StatPanels,
            "Actor/monster stat panels as world panels near the table (opened by the game / Phase-3a poke).");
        PropInfoCards = _file.Bind("WorldUI", "PropInfoCards", Defaults.PropInfoCards,
            "Hover prop-info cards (closed doors/chests, traps, terrain, quest items — the " +
            "game's TextInfoPanel/PropInfoPanel popups) as a small passive world panel low in view.");
        EnemyReveal = _file.Bind("WorldUI", "EnemyReveal", Defaults.EnemyReveal,
            "Enemy round reveal (the monster ability cards shown after everyone confirmed " +
            "their card selection) as a display-only world panel floating above the board " +
            "while the game shows it, instead of hidden on the control board.");
        DecisionDock = _file.Bind("WorldUI", "DecisionDock", Defaults.DecisionDock,
            "In-scenario decision/confirmation prompts (take-damage burn choice, the burn-" +
            "confirm 'burn this / choose another card' dialog, and any other prompt in the " +
            "DecisionDock registry) render their REAL game widgets — the actual buttons/" +
            "toggles, so labels/localization/enable-states are native — docked in a reserved " +
            "zone BELOW the two cards on the control board while the prompt is open, instead " +
            "of a floating flat window (test #22, generalizes the test-#21 take-damage dock). " +
            "The card fan stays available for follow-up picks (no ModalUI). Off = the generic " +
            "modal fallback floats the whole window as before. (Renamed from 'TakeDamageBoard'.)");
        TrayNativeControls = _file.Bind("WorldUI", "TrayNativeControls", Defaults.TrayNativeControls,
            "Dock the game's REAL Continue/Confirm (ReadyButton), Undo (UndoButton) and short-rest " +
            "(ShortRest) widgets onto the control board — the actual in-game buttons with their native " +
            "sprite, localized label and enable/disable states, reusing the DecisionDock docking " +
            "mechanism — in place of the mod-drawn CONFIRM/UNDO board buttons and short-rest token " +
            "(test #23 item 4). Long rest has no discrete uGUI widget (chosen via the card fan + " +
            "Continue), so it stays a mod token. " +
            "DEFAULT OFF (test #27 item 3): docking the real widgets only WHILE they are game-side " +
            "active, then releasing back to the mod button when they hide, made the board buttons " +
            "visibly FLICKER between the flat in-game widget and the 3D mod button. The mod-drawn " +
            "board buttons now wear the sampled native game skin themselves (NativeButtonSkin, test " +
            "#26), so leaving this OFF gives every board button ONE permanent, uniform game-styled " +
            "look with no swap. On = re-enable the real-widget docking (accepts the flicker).");
        ActorBars = _file.Bind("WorldUI", "ActorBars", Defaults.ActorBars,
            "True world-space HP/effect bars above the miniatures (replaces the screen-projected bars).");
        BarFixedSize = _file.Bind("WorldUI", "BarFixedSize", Defaults.BarFixedSize,
            "Actor HP/effect bars keep a FIXED board-space size — they scale only with the " +
            "diorama, like the miniatures themselves (test #14: the old distance compensation " +
            "grew bars up to 2.5x when stepping away, which read as the bars 'growing'). " +
            "Off = legacy behavior: bars gently grow with head distance to stay readable.");
        WristHud = _file.Bind("WorldUI", "WristHud", Defaults.WristHud,
            "Compact character status (HP/XP/conditions/gold) on the non-dominant wrist, look-at activated.");
        FlatScreen = _file.Bind("WorldUI", "FlatScreen", Defaults.FlatScreen,
            "Floating 2D screen mirroring the UICamera for menus/merchant/level-up + ray pointer.");
        Tooltips = _file.Bind("WorldUI", "Tooltips", Defaults.Tooltips,
            "Re-anchor the game's tooltip canvas in world space near the poking fingertip.");
        ActionElementHints = _file.Bind("WorldUI", "ActionElementHints", Defaults.ActionElementHints,
            "Show the game's action-phase element/ability explanation hint (the card-action " +
            "tooltip) as a world-space panel pinned to the control board's TOP-LEFT corner " +
            "while a scenario runs. A short hover grace keeps it from flickering away on tiny " +
            "movements off the hovered element. Off = the hint is never flipped to world space " +
            "and never shown in VR (the vanilla 2D menu tooltip is unaffected). Wired to the " +
            "in-VR settings panel and read live, so toggling takes effect without a restart.");

        CatchAllModals = _file.Bind("WorldUI", "CatchAllModals", Defaults.CatchAllModals,
            "Deadlock insurance: any UNKNOWN game window that opens during a scenario (an ID " +
            "the mod has not enrolled explicitly — scene-serialized IDs are invisible in code, " +
            "so future game patches can always add one) is floated as a grabbable VR window " +
            "with an X after a ~2-tick grace, instead of waiting invisibly on the hidden 2D " +
            "stack while the game blocks on it (the ItemCardPicker silent-deadlock class). " +
            "Each floated unknown window logs one warning naming it, so it can be enrolled " +
            "explicitly later. Off = only explicitly enrolled windows are handled (pre-catch-" +
            "all behavior); the manual A/X screen chord remains the universal rescue.");
        ForceMouseMode = _file.Bind("WorldUI", "ForceMouseMode", Defaults.ForceMouseMode,
            "Keep InputManager in mouse mode while VR runs so the 'Game' (not 'Game_gamepad') " +
            "scene variants load and buttons commit without gamepad long-press flows.");
        CanvasScaleMm = _file.Bind("WorldUI", "CanvasScaleMm", Defaults.CanvasScaleMm,
            "World-canvas scale: millimeters per uGUI pixel at diorama scale 1 (default 1 px = 1 mm).");
        InitiativeDepthMaxSpreadPx = _file.Bind("WorldUI", "InitiativeDepthMaxSpreadPx", Defaults.InitiativeDepthMaxSpreadPx,
            "Initiative track 3D depth effect: the MAXIMUM total front-to-back z spread (uGUI " +
            "pixels) between the shallowest and deepest initiative portrait. The authored row " +
            "depth is compressed proportionally to land at this cap (never amplified). Higher = " +
            "stronger recession; 0 = flat. Live-tunable in the debug menu (Panels -> Initiative). " +
            "Range 0..40.");
        DecisionRowGapPx = _file.Bind("WorldUI", "DecisionRowGapPx", Defaults.DecisionRowGapPx, new ConfigDescription(
            "Vertical gap (uGUI pixels, in the docked row's own scale) between the control " +
            "board's lower edge — where the game draws the decision prompt line, e.g. " +
            "'Schadensphase: Erleide entweder Schaden …' — and the TOP of the docked " +
            "interactive widget block (the 'Schaden erhalten' / burn buttons, the burn-confirm " +
            "options, the short-rest Ja/Nein). The mod owns where this block docks, so this " +
            "value drives the block's PLACEMENT directly: LOWER it to pull the buttons UP toward " +
            "the prompt (closing the gap), RAISE it to drop them. Applied to every docked " +
            "decision row and re-applied live to an open dock the instant it changes. The grab " +
            "bar is never overlapped (the block always sits at least its clearance below it). " +
            "Range 0-120.",
            new AcceptableValueRange<float>(0f, 120f)));
        HoverInfoScale = _file.Bind("WorldUI", "HoverInfoScale", Defaults.HoverInfoScale,
            new ConfigDescription(
                "SIZE factor of the hover INFO panels — the little cards the game raises while the " +
                "pointer/fingertip hovers a board field ('2 Gold', 'Geschlossene Tür', chest, " +
                "obstacle, pressure plate, trap, quest item …) plus the card-action element hint. " +
                "The factor multiplies the panel's world scale, i.e. it scales the WHOLE panel " +
                "(frame + text) uniformly on top of the diorama/board scale, so the hint keeps its " +
                "proportions and stays readable at any board size and viewing distance — it is a " +
                "zoom, not a re-layout, and the panel's placement (which is derived from its own " +
                "measured extents) follows automatically. Default 0.6 = the size before this dial " +
                "existed, so nothing changes until it is tuned; raise it if the hover cards read " +
                "too small in the HMD. Live-tunable in the debug menu (Anzeige -> Infotafel-Größe): " +
                "the value is read on every placement tick, so an open panel resizes immediately and " +
                "the next hover comes up at the new size — no restart. Range 0.2-2.",
                new AcceptableValueRange<float>(0.2f, 2f)));
        EnemyRevealBoardClearance = _file.Bind("WorldUI", "EnemyRevealBoardClearance", Defaults.EnemyRevealBoardClearance,
            new ConfigDescription(
                "How far the ENEMY ROUND REVEAL (the monster ability cards shown after everyone " +
                "confirmed their selection) must clear the CONTROL BOARD's top edge, in real " +
                "metres measured AT THE BOARD. The player is normally LOOKING DOWN at the control " +
                "board when the reveal appears, so a purely gaze-anchored spawn lands right " +
                "behind/inside the board and is unreadable; the reveal is therefore lifted just " +
                "far enough that the player's line of sight to its BOTTOM edge passes this far " +
                "above the board's real (rendered) top edge. Because the gap is measured at the " +
                "board and the reveal floats further away, the on-screen gap is proportionally " +
                "larger. 0 = graze the top edge; raise it if the reveal still reads too close to " +
                "the board. Live-tunable in the debug menu (Panels -> Initiative); applies to the " +
                "next reveal spawn / lazy-follow step. Range 0-0.5.",
                new AcceptableValueRange<float>(0f, 0.5f)));
        FlatScreenAutoShow = _file.Bind("WorldUI", "FlatScreenAutoShow", Defaults.FlatScreenAutoShow,
            "Automatically show the floating 2D screen while no scenario runs (main menu, map) " +
            "and hide it in scenario modes.");
        DesktopMirrorLeftEye = _file.Bind("WorldUI", "DesktopMirrorLeftEye", Defaults.DesktopMirrorLeftEye,
            "Flat monitor mirrors ONLY the HMD's LEFT eye: pins XRSettings.gameViewRenderMode to " +
            "LeftEye and skips the desktop 2D-menu composite blit, so the desktop is a clean " +
            "single-eye mirror in every state. Off = legacy (2D-menu composite during menus; " +
            "uncontrolled default XR mirror otherwise).");
        WristHudPitch = _file.Bind("WorldUI", "WristHudPitch", Defaults.WristHudPitch,
            "Wrist overview HUD tilt (pitch, degrees) on top of the flat-on-hand base.");
        WristHudYaw = _file.Bind("WorldUI", "WristHudYaw", Defaults.WristHudYaw, "Wrist overview HUD yaw (degrees).");
        WristHudRoll = _file.Bind("WorldUI", "WristHudRoll", Defaults.WristHudRoll, "Wrist overview HUD roll (degrees).");
        WristHudOffsetX = _file.Bind("WorldUI", "WristHudOffsetX", Defaults.WristHudOffsetX,
            "Wrist overview HUD offset along wrist X, real meters.");
        WristHudOffsetY = _file.Bind("WorldUI", "WristHudOffsetY", Defaults.WristHudOffsetY,
            "Wrist overview HUD offset out the back of the hand (wrist +Y), real meters.");
        WristHudOffsetZ = _file.Bind("WorldUI", "WristHudOffsetZ", Defaults.WristHudOffsetZ,
            "Wrist overview HUD offset toward the fingers (wrist +Z), real meters.");
        // NB: the WristHud CLASS is shadowed here by the WristHud config field (bool toggle),
        // so qualify the type to reach its static pose-config refs (item 10 wiring).
        global::GloomhavenVR.WorldUI.WristHud.PitchEntry = WristHudPitch;
        global::GloomhavenVR.WorldUI.WristHud.YawEntry = WristHudYaw;
        global::GloomhavenVR.WorldUI.WristHud.RollEntry = WristHudRoll;
        global::GloomhavenVR.WorldUI.WristHud.OffsetXEntry = WristHudOffsetX;
        global::GloomhavenVR.WorldUI.WristHud.OffsetYEntry = WristHudOffsetY;
        global::GloomhavenVR.WorldUI.WristHud.OffsetZEntry = WristHudOffsetZ;
        ShowIntro = _file.Bind("WorldUI", "ShowIntro", Defaults.ShowIntro,
            "Show the game's intro (logos/video, pre-menu scenes) on the floating screen in VR " +
            "too. Off = old behavior: intro plays on the desktop only and the HMD shows a " +
            "'starting...' indicator in the void.");
        ScreenWidth = _file.Bind("WorldUI", "ScreenWidth", Defaults.ScreenWidth,
            "Width of the floating 2D screen in real-world meters (16:9, height follows). " +
            "Replaces the pre-test-#6 'FlatScreenWidth' key (1.4 m read too small at 1.6 m).");
        ScreenDistance = _file.Bind("WorldUI", "ScreenDistance", Defaults.ScreenDistance,
            "Distance from the head to the floating 2D screen in real-world meters.");
        ClickLatch = _file.Bind("WorldUI", "ClickLatch", Defaults.ClickLatch,
            "Freeze the virtual-mouse position from trigger-press (or fingertip contact) until " +
            "release, so press and release land on the SAME pixel and uGUI registers a click — " +
            "sub-degree hand tremor otherwise moves the projected pixel dozens of px and turns " +
            "every click into a no-op drag. Deliberate movement past DragUnlockDegrees for " +
            "DragUnlockSeconds opens the latch into a real drag (scroll lists keep working).");
        SuppressPhysicalMouse = _file.Bind("WorldUI", "SuppressPhysicalMouse", Defaults.SuppressPhysicalMouse,
            "While VR is running, disable the physical desktop mouse in the InputSystem so its " +
            "(stale) desktop position can no longer hover or select map/menu elements behind your " +
            "back — only the VR laser drives the pointer. The mouse is re-enabled when VR stops.");
        MapWindOpacity = _file.Bind("WorldUI", "MapWindOpacity", Defaults.MapWindOpacity,
            "Opacity of the campaign map's drifting Wind/Clouds ambiance particles (0..1). The game's " +
            "flat map camera post-processes/masks these so they read as subtle; the VR forward capture " +
            "does not, so at full strength they render as thick translucent streaks that smear across " +
            "the location icons. 0.3 keeps a subtle drift; 1 = original strength; 0 = fully hidden.");
        DragUnlockDegrees = _file.Bind("WorldUI", "DragUnlockDegrees", Defaults.DragUnlockDegrees,
            "How far (degrees) the ray must move off its press direction to open the click " +
            "latch into a drag.");
        DragUnlockSeconds = _file.Bind("WorldUI", "DragUnlockSeconds", Defaults.DragUnlockSeconds,
            "How long (seconds) the ray must stay beyond DragUnlockDegrees before the latch " +
            "opens (filters single-frame tremor spikes).");
        PokeClick = _file.Bind("WorldUI", "PokeClick", Defaults.PokeClick,
            "Poking the floating 2D screen with the index fingertip clicks at the poked " +
            "position (press on plane contact, release on withdraw; latch rules as above). " +
            "The screen may be out of arm's reach at the default distance — lean/step in, " +
            "or reduce [WorldUI] ScreenDistance.");
        PokePressDepthMm = _file.Bind("WorldUI", "PokePressDepthMm", Defaults.PokePressDepthMm, new ConfigDescription(
            "Push-in depth in MILLIMETERS a fingertip must travel THROUGH a flat (converted " +
            "uGUI) button's canvas plane before the click fires. Touching the plane only ARMS " +
            "the press: the button shows its pressed visual (pointerDown) with a light haptic " +
            "tick; pushing past this depth fires the click with a stronger pulse; retracting " +
            "before reaching it cancels silently — no click, no penalty, re-armed after pulling " +
            "back out of the plane. Guards against accidental presses from brushing a panel " +
            "without making deliberate presses tedious. 0 = legacy instant click on plane " +
            "contact. Laser clicks are unaffected. Range 0-30.",
            new AcceptableValueRange<float>(0f, 30f)));
        DecisionPokeDeliberate = _file.Bind("WorldUI", "DecisionPokeDeliberate", Defaults.DecisionPokeDeliberate,
            "DECISION buttons (the docked take-damage burn choice, the burn-confirm dialog, " +
            "the short-rest Ja/Nein) demand a DELIBERATE physical press: touching the button " +
            "only ARMS it (pressed visual + light haptic tick); the click fires when the " +
            "fingertip is consciously WITHDRAWN back out of the plane past the release depth; " +
            "sweeping the hand through the button or leaving it sideways cancels silently — " +
            "no click, no penalty. Guards the costly, irreversible decision prompts against " +
            "accidental instant triggers. Applies ONLY to the physical poke on decision-dock " +
            "buttons; every other converted surface keeps the PokePressDepthMm push-in press, " +
            "and laser clicks are unaffected. Off = decision buttons press like everything else.");
        ClickMode = _file.Bind("WorldUI", "ClickMode", Defaults.ClickMode,
            "How a latched click on the floating screen is delivered. 'execute' (default): " +
            "directly via uGUI ExecuteEvents on the raycast target — the same mechanism the " +
            "game's own BaseButtons.clickButton uses; immune to input-module edge-visibility " +
            "quirks (hardware test #7: virtual-mouse button edges produced no clicks). " +
            "'virtualmouse': press/release through the virtual mouse device only. " +
            "'both': both paths (may double-fire — diagnostic use only). Deliberate drags " +
            "always go through the virtual mouse regardless of mode.");

        CombatLogFollow = _file.Bind("WorldUI", "CombatLogFollowSeat", Defaults.CombatLogFollowSeat,
            "Combat log panel anchor mode (the panel's own FOLLOW/PINNED pin button flips " +
            "this). False (PINNED, default): the panel is STATIC IN THE WORLD — placed once " +
            "from the persisted offsets on scenario entry, then frozen until grabbed. " +
            "True (FOLLOW): the panel re-derives its place from the table anchor + seat " +
            "yaw (moves with recenters and the diorama like the other world panels; " +
            "orientation still only re-derives on recenter, never per frame). Replaces " +
            "the test-#19 'CombatLogFollow' key: its follow default plus the per-tick " +
            "yaw billboard read as the panel tracking the head (test #20).");
        CombatLogForward = _file.Bind("WorldUI", "CombatLogForward", Defaults.CombatLogForward,
            "Combat log panel offset from the table anchor along the seat forward, real " +
            "meters (default = the old arc slot: azimuth 56° at 1.10 m). Persisted " +
            "automatically when the panel's grab bar is released.");
        CombatLogRight = _file.Bind("WorldUI", "CombatLogRight", Defaults.CombatLogRight,
            "Combat log panel offset to the seat right, real meters (grab-persisted).");
        CombatLogUp = _file.Bind("WorldUI", "CombatLogUp", Defaults.CombatLogUp,
            "Combat log panel height above the table plane, real meters (grab-persisted).");
        CombatLogScale = _file.Bind("WorldUI", "CombatLogScale", Defaults.CombatLogScale,
            "Combat log panel size multiplier (two-hand grab resize; clamped 0.5-2).");
        CombatLogUserClosed = _file.Bind("WorldUI", "CombatLogUserClosed", Defaults.CombatLogUserClosed,
            "The user hid the combat log via its top-right X button (or the in-VR settings " +
            "'Kampflog anzeigen' toggle). While true the panel releases back to its 2D home and " +
            "does NOT auto-reappear in a scenario; the settings toggle clears it and re-shows the " +
            "panel. Kept separate from [WorldUI] CombatLog (the feature master) so re-showing " +
            "never disturbs the feature toggle or the persisted layout.");
        PanelsFollowView = _file.Bind("WorldUI", "PanelsFollowView", Defaults.PanelsFollowView,
            "LEGACY (pre-test-#8) behavior: the world panels (initiative track, element " +
            "board, combat log, objectives, button cluster) re-derive their placement from " +
            "the live rig yaw every frame, so they swing around the table with every snap " +
            "turn / world grab — perceived as a floating HUD. Default false: panels are " +
            "FIXED IN THE WORLD at the table and re-anchor only on rig rebuild or recenter.");
        HexHintFollowView = _file.Bind("WorldUI", "HexHintFollowView", Defaults.HexHintFollowView,
            "While a board-field hover hint (the TextInfoPanel/PropInfoPanel popups, e.g. " +
            "'Geschlossene Tür') is shown, drift it to a comfortable reading spot near the " +
            "CENTER of the player's field of view with LAZY (critically-damped) motion that " +
            "follows the head and settles, instead of leaving it at PropInfoSurface's fixed " +
            "table dock. Either way it stays upright and billboards toward the head. Off = " +
            "keep the dock position and only re-face it to the head.");
        ModalStyle = _file.Bind("WorldUI", "ModalStyle", Defaults.ModalStyle,
            "How in-scenario 2D fallback windows (story boxes, events, tutorials, ESC " +
            "menu, rewards, choice dialogs, ...) are made operable in VR (P8, test #12). " +
            "'window' (default): only THAT window is converted to a world-space panel " +
            "floating in front of the HMD — poke AND laser clickable — and restored to " +
            "its 2D home when it closes; the full flat screen appears only when a " +
            "specific window fails to convert (reason logged). 'screen': pre-P8 " +
            "behavior — the full 2D desktop composite appears for every fallback " +
            "window. The manual A/X chord always summons the full screen regardless.");
        ManualScreenChord = _file.Bind("WorldUI", "ManualScreenChord", Defaults.ManualScreenChord,
            "Self-rescue chord: HOLD the NON-dominant lower face button (A or X) for " +
            "ManualScreenChordSeconds during a scenario to toggle the floating 2D screen " +
            "(full desktop UI + pointer) — always available when a 2D window is open that " +
            "VR does not show. Short holds still toggle the settings panel; that chord " +
            "fires on RELEASE (before the screen threshold) so the two never collide.");
        ManualScreenChordSeconds = _file.Bind("WorldUI", "ManualScreenChordSeconds", Defaults.ManualScreenChordSeconds,
            "Hold duration (seconds) of the non-dominant A/X for the manual flat-screen " +
            "toggle.");
        DemoteOverlaySolidClears = _file.Bind("WorldUI", "DemoteOverlaySolidClears", Defaults.DemoteOverlaySolidClears,
            "While the floating 2D screen captures the game's cameras into its RenderTexture, " +
            "demote FULLSCREEN SolidColor clears of NON-base captured cameras (e.g. the campaign " +
            "map's depth-5 'Video Camera', whose clear is only the black backdrop behind fullscreen " +
            "videos — GH VideoCamera.PlayFullscreenVideo) to Depth-only, so they can never wipe the " +
            "composited map/UI to black. Viewport-limited (sub-rect) cameras keep their clear. " +
            "Disable for vanilla-exact clears (black letterbox backdrop during videos).");
        ScreenLayerSplit = _file.Bind("WorldUI", "ScreenLayerSplit", Defaults.ScreenLayerSplit,
            "Render the floating 2D screen as TWO layers (hardware test #18): the game's UI " +
            "cameras — whose Screen-Space-Camera canvases only ever render through their " +
            "assigned camera, so stereo mirror cameras can never reproduce them and the menu " +
            "went one-eyed — draw onto a transparent 'glass' quad shown identically to both " +
            "eyes at the screen plane, while 3D scene cameras and videos render a background " +
            "layer a few cm behind it with per-eye stereo depth. Off (or on any failure): " +
            "single-RT fallback — one flat mono screen in both eyes, never one-eyed.");
        LoadingIndicator = _file.Bind("WorldUI", "LoadingIndicator", Defaults.LoadingIndicator,
            "While the flat game shows its loading screen (scene transitions, scenario " +
            "start), float the game's own ROTATING LOADING SPINNER — the icon only, not the " +
            "hints/progress screen — in front of the HMD on the black void, hide the " +
            "floating 2D screen for the duration, and lower Unity's background loading " +
            "priority so head/hand rendering hitches less (the load itself takes slightly " +
            "longer). A few residual single-frame freezes remain (the game's per-transition " +
            "GC pause and synchronous asset-assembly frames cannot be split). Off = vanilla " +
            "behavior: the HMD shows a motionless void during loads.");

        KeyboardEnabled = _file.Bind("Keyboard", "Enabled", Defaults.Keyboard_Enabled,
            "Show the game's own on-screen keyboard whenever a text field takes focus, so a party " +
            "can be named without reaching for a physical keyboard. The keyboard is the game's " +
            "(UIKeyboard) rather than a mod-drawn one, so it carries the game's art and its " +
            "per-language layouts; the game only ever shows it in gamepad mode, which VR never " +
            "uses. Off = text fields need a real keyboard.");
        KeyboardAutoCase = _file.Bind("Keyboard", "AutoCapitalise", Defaults.AutoCapitalise,
            "Capitalise the first letter of each word typed on the on-screen keyboard and lower " +
            "the rest. The game's keyboard emits key CODES and maps letters to their upper-case " +
            "name, so typing straight through produces 'MY BRAVE PARTY'; this gives 'My Brave " +
            "Party'. Off = every letter arrives exactly as the game's keyboard produces it.");

        DevShowAllPanels = _file.Bind("WorldUI", "DevShowAllPanels", Defaults.DevShowAllPanels,
            "DEV: spawn the world-panel layout with dummy content on the desktop (no HMD needed).");
        DevForceConvert = _file.Bind("WorldUI", "DevForceConvert", Defaults.DevForceConvert,
            "DEV: apply the real canvas conversions in dev mode without an HMD (this moves the " +
            "game's 2D panels into world space — the desktop view changes accordingly).");
    }

    /// <summary>User #7c: whether the world-space action-element hint presentation is enabled (read live).</summary>
    internal static bool ActionElementHintsEnabled => ActionElementHints.Value;

    /// <summary>True while WorldUI physicalization should be applied to live game UI.</summary>
    internal static bool ConversionActive =>
        Master.Value && (VRSession.IsRunning || (Plugin.DevMode.Value && DevForceConvert.Value));
}
