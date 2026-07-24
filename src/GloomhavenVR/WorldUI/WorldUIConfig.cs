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

    // ---- behavior ----------------------------------------------------------------------
    /// <summary>Keep the game in mouse mode so `Game` (not `Game_gamepad`) scenes load.</summary>
    internal static ConfigEntry<bool> ForceMouseMode = null!;

    /// <summary>World-canvas scale in millimeters per uGUI pixel (at diorama scale 1).</summary>
    internal static ConfigEntry<float> CanvasScaleMm = null!;

    /// <summary>Initiative track 3D depth effect: max total front-to-back z spread (px), live-tunable.</summary>
    internal static ConfigEntry<float> InitiativeDepthMaxSpreadPx = null!;

    /// <summary>
    /// User #11b: target vertical gap (uGUI px) between a docked decision row's prompt text
    /// ("Erleide entweder Schaden…") and its widgets ("Schaden erhalten" …). Live-applied by
    /// <see cref="Surfaces.DecisionDockSurface"/> while a row is docked; a later settings-panel
    /// phase adds a stepper for it. Range 0–60.
    /// </summary>
    internal static ConfigEntry<float> DecisionRowGapPx = null!;

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

    /// <summary>True when clicks go through ExecuteEvents (ClickMode execute/both).</summary>
    internal static bool ExecuteClicks =>
        !string.Equals(ClickMode.Value, "virtualmouse", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>True when button state goes through the virtual mouse (ClickMode virtualmouse/both).</summary>
    internal static bool VirtualMouseButtons =>
        !string.Equals(ClickMode.Value, "execute", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>True when fallback windows float individually ([WorldUI] ModalStyle != "screen").</summary>
    internal static bool ModalWindowStyle =>
        !string.Equals(ModalStyle.Value, "screen", System.StringComparison.OrdinalIgnoreCase);

    // ---- settings panel (MISSION B) ------------------------------------------------------
    /// <summary>Hold the non-dominant A/X this long to toggle the settings panel (0 = off).</summary>
    internal static ConfigEntry<float> SettingsChordHoldSeconds = null!;

    // ---- settings panel layout (test #24: movable/scalable/pinnable like the combat log) --
    /// <summary>Settings panel anchor mode: true = re-derive from the seat anchor, false = pinned in the world.</summary>
    internal static ConfigEntry<bool> SettingsFollow = null!;

    /// <summary>Settings panel offset from the table anchor along the seat forward, real meters.</summary>
    internal static ConfigEntry<float> SettingsForward = null!;

    /// <summary>Settings panel offset to the seat right, real meters.</summary>
    internal static ConfigEntry<float> SettingsRight = null!;

    /// <summary>Settings panel height above the table plane, real meters.</summary>
    internal static ConfigEntry<float> SettingsUp = null!;

    /// <summary>Settings panel size multiplier (two-hand resize; clamped 0.5–2).</summary>
    internal static ConfigEntry<float> SettingsScale = null!;

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

        Master = _file.Bind("WorldUI", "Master", true,
            "Master switch for the whole physicalized interface (all surfaces below AND the " +
            "floating 2D screen). Off = the game's own 2D screen-space UI stays untouched.");
        ButtonCluster = _file.Bind("WorldUI", "ButtonCluster", true,
            "Physical Ready/Undo/Skip buttons at the table edge.");
        InitiativeTrack = _file.Bind("WorldUI", "InitiativeTrack", true,
            "Initiative track as a world-space panel above the table.");
        ElementBoard = _file.Bind("WorldUI", "ElementBoard", true,
            "Element infusion board docked on the control board's left column, below the " +
            "objectives panel (floating world panel only as the no-tray fallback).");
        CombatLog = _file.Bind("WorldUI", "CombatLog", true,
            "Combat log as a world-space panel at the table's far side.");
        Objectives = _file.Bind("WorldUI", "Objectives", true,
            "Scenario objectives as a world-space panel at the table's far side.");
        Dialogs = _file.Bind("WorldUI", "Dialogs", true,
            "Confirmation dialogs as world-space modals in front of the HMD (poke yes/no).");
        StatPanels = _file.Bind("WorldUI", "StatPanels", true,
            "Actor/monster stat panels as world panels near the table (opened by the game / Phase-3a poke).");
        PropInfoCards = _file.Bind("WorldUI", "PropInfoCards", true,
            "Hover prop-info cards (closed doors/chests, traps, terrain, quest items — the " +
            "game's TextInfoPanel/PropInfoPanel popups) as a small passive world panel low in view.");
        EnemyReveal = _file.Bind("WorldUI", "EnemyReveal", true,
            "Enemy round reveal (the monster ability cards shown after everyone confirmed " +
            "their card selection) as a display-only world panel floating above the board " +
            "while the game shows it, instead of hidden on the control board.");
        DecisionDock = _file.Bind("WorldUI", "DecisionDock", true,
            "In-scenario decision/confirmation prompts (take-damage burn choice, the burn-" +
            "confirm 'burn this / choose another card' dialog, and any other prompt in the " +
            "DecisionDock registry) render their REAL game widgets — the actual buttons/" +
            "toggles, so labels/localization/enable-states are native — docked in a reserved " +
            "zone BELOW the two cards on the control board while the prompt is open, instead " +
            "of a floating flat window (test #22, generalizes the test-#21 take-damage dock). " +
            "The card fan stays available for follow-up picks (no ModalUI). Off = the generic " +
            "modal fallback floats the whole window as before. (Renamed from 'TakeDamageBoard'.)");
        TrayNativeControls = _file.Bind("WorldUI", "TrayNativeControls", false,
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
        ActorBars = _file.Bind("WorldUI", "ActorBars", true,
            "True world-space HP/effect bars above the miniatures (replaces the screen-projected bars).");
        BarFixedSize = _file.Bind("WorldUI", "BarFixedSize", true,
            "Actor HP/effect bars keep a FIXED board-space size — they scale only with the " +
            "diorama, like the miniatures themselves (test #14: the old distance compensation " +
            "grew bars up to 2.5x when stepping away, which read as the bars 'growing'). " +
            "Off = legacy behavior: bars gently grow with head distance to stay readable.");
        WristHud = _file.Bind("WorldUI", "WristHud", true,
            "Compact character status (HP/XP/conditions/gold) on the non-dominant wrist, look-at activated.");
        FlatScreen = _file.Bind("WorldUI", "FlatScreen", true,
            "Floating 2D screen mirroring the UICamera for menus/merchant/level-up + ray pointer.");
        Tooltips = _file.Bind("WorldUI", "Tooltips", true,
            "Re-anchor the game's tooltip canvas in world space near the poking fingertip.");

        ForceMouseMode = _file.Bind("WorldUI", "ForceMouseMode", true,
            "Keep InputManager in mouse mode while VR runs so the 'Game' (not 'Game_gamepad') " +
            "scene variants load and buttons commit without gamepad long-press flows.");
        CanvasScaleMm = _file.Bind("WorldUI", "CanvasScaleMm", 1.0f,
            "World-canvas scale: millimeters per uGUI pixel at diorama scale 1 (default 1 px = 1 mm).");
        InitiativeDepthMaxSpreadPx = _file.Bind("WorldUI", "InitiativeDepthMaxSpreadPx", 10f,
            "Initiative track 3D depth effect: the MAXIMUM total front-to-back z spread (uGUI " +
            "pixels) between the shallowest and deepest initiative portrait. The authored row " +
            "depth is compressed proportionally to land at this cap (never amplified). Higher = " +
            "stronger recession; 0 = flat. Live-tunable in the debug menu (Panels -> Initiative). " +
            "Range 0..40.");
        DecisionRowGapPx = _file.Bind("WorldUI", "DecisionRowGapPx", 12f, new ConfigDescription(
            "Target vertical gap in uGUI pixels between a docked decision prompt's text block " +
            "(e.g. 'Erleide entweder Schaden …') and its button/toggle row (e.g. 'Schaden " +
            "erhalten') on the control board. The authored 2D dialog spacing is COMPRESSED down " +
            "to this value (rows authored tighter than the target are left alone — the row is " +
            "never spread apart). Applied to every docked decision row (take-damage/burn choice, " +
            "burn-confirm dialog, short-rest Yes/No, …) and live-reapplied to an open dock when " +
            "changed.",
            new AcceptableValueRange<float>(0f, 60f)));
        FlatScreenAutoShow = _file.Bind("WorldUI", "FlatScreenAutoShow", true,
            "Automatically show the floating 2D screen while no scenario runs (main menu, map) " +
            "and hide it in scenario modes.");
        DesktopMirrorLeftEye = _file.Bind("WorldUI", "DesktopMirrorLeftEye", true,
            "Flat monitor mirrors ONLY the HMD's LEFT eye: pins XRSettings.gameViewRenderMode to " +
            "LeftEye and skips the desktop 2D-menu composite blit, so the desktop is a clean " +
            "single-eye mirror in every state. Off = legacy (2D-menu composite during menus; " +
            "uncontrolled default XR mirror otherwise).");
        WristHudPitch = _file.Bind("WorldUI", "WristHudPitch", 0f,
            "Wrist overview HUD tilt (pitch, degrees) on top of the flat-on-hand base.");
        WristHudYaw = _file.Bind("WorldUI", "WristHudYaw", 0f, "Wrist overview HUD yaw (degrees).");
        WristHudRoll = _file.Bind("WorldUI", "WristHudRoll", 0f, "Wrist overview HUD roll (degrees).");
        WristHudOffsetX = _file.Bind("WorldUI", "WristHudOffsetX", 0f,
            "Wrist overview HUD offset along wrist X, real meters.");
        WristHudOffsetY = _file.Bind("WorldUI", "WristHudOffsetY", 0.015f,
            "Wrist overview HUD offset out the back of the hand (wrist +Y), real meters.");
        WristHudOffsetZ = _file.Bind("WorldUI", "WristHudOffsetZ", 0.01f,
            "Wrist overview HUD offset toward the fingers (wrist +Z), real meters.");
        // NB: the WristHud CLASS is shadowed here by the WristHud config field (bool toggle),
        // so qualify the type to reach its static pose-config refs (item 10 wiring).
        global::GloomhavenVR.WorldUI.WristHud.PitchEntry = WristHudPitch;
        global::GloomhavenVR.WorldUI.WristHud.YawEntry = WristHudYaw;
        global::GloomhavenVR.WorldUI.WristHud.RollEntry = WristHudRoll;
        global::GloomhavenVR.WorldUI.WristHud.OffsetXEntry = WristHudOffsetX;
        global::GloomhavenVR.WorldUI.WristHud.OffsetYEntry = WristHudOffsetY;
        global::GloomhavenVR.WorldUI.WristHud.OffsetZEntry = WristHudOffsetZ;
        ShowIntro = _file.Bind("WorldUI", "ShowIntro", true,
            "Show the game's intro (logos/video, pre-menu scenes) on the floating screen in VR " +
            "too. Off = old behavior: intro plays on the desktop only and the HMD shows a " +
            "'starting...' indicator in the void.");
        ScreenWidth = _file.Bind("WorldUI", "ScreenWidth", 2.2f,
            "Width of the floating 2D screen in real-world meters (16:9, height follows). " +
            "Replaces the pre-test-#6 'FlatScreenWidth' key (1.4 m read too small at 1.6 m).");
        ScreenDistance = _file.Bind("WorldUI", "ScreenDistance", 1.6f,
            "Distance from the head to the floating 2D screen in real-world meters.");
        ClickLatch = _file.Bind("WorldUI", "ClickLatch", true,
            "Freeze the virtual-mouse position from trigger-press (or fingertip contact) until " +
            "release, so press and release land on the SAME pixel and uGUI registers a click — " +
            "sub-degree hand tremor otherwise moves the projected pixel dozens of px and turns " +
            "every click into a no-op drag. Deliberate movement past DragUnlockDegrees for " +
            "DragUnlockSeconds opens the latch into a real drag (scroll lists keep working).");
        DragUnlockDegrees = _file.Bind("WorldUI", "DragUnlockDegrees", 2.0f,
            "How far (degrees) the ray must move off its press direction to open the click " +
            "latch into a drag.");
        DragUnlockSeconds = _file.Bind("WorldUI", "DragUnlockSeconds", 0.15f,
            "How long (seconds) the ray must stay beyond DragUnlockDegrees before the latch " +
            "opens (filters single-frame tremor spikes).");
        PokeClick = _file.Bind("WorldUI", "PokeClick", true,
            "Poking the floating 2D screen with the index fingertip clicks at the poked " +
            "position (press on plane contact, release on withdraw; latch rules as above). " +
            "The screen may be out of arm's reach at the default distance — lean/step in, " +
            "or reduce [WorldUI] ScreenDistance.");
        PokePressDepthMm = _file.Bind("WorldUI", "PokePressDepthMm", 12f, new ConfigDescription(
            "Push-in depth in MILLIMETERS a fingertip must travel THROUGH a flat (converted " +
            "uGUI) button's canvas plane before the click fires. Touching the plane only ARMS " +
            "the press: the button shows its pressed visual (pointerDown) with a light haptic " +
            "tick; pushing past this depth fires the click with a stronger pulse; retracting " +
            "before reaching it cancels silently — no click, no penalty, re-armed after pulling " +
            "back out of the plane. Guards against accidental presses from brushing a panel " +
            "without making deliberate presses tedious. 0 = legacy instant click on plane " +
            "contact. Laser clicks are unaffected. Range 0-30.",
            new AcceptableValueRange<float>(0f, 30f)));
        DecisionPokeDeliberate = _file.Bind("WorldUI", "DecisionPokeDeliberate", true,
            "DECISION buttons (the docked take-damage burn choice, the burn-confirm dialog, " +
            "the short-rest Ja/Nein) demand a DELIBERATE physical press: touching the button " +
            "only ARMS it (pressed visual + light haptic tick); the click fires when the " +
            "fingertip is consciously WITHDRAWN back out of the plane past the release depth; " +
            "sweeping the hand through the button or leaving it sideways cancels silently — " +
            "no click, no penalty. Guards the costly, irreversible decision prompts against " +
            "accidental instant triggers. Applies ONLY to the physical poke on decision-dock " +
            "buttons; every other converted surface keeps the PokePressDepthMm push-in press, " +
            "and laser clicks are unaffected. Off = decision buttons press like everything else.");
        ClickMode = _file.Bind("WorldUI", "ClickMode", "execute",
            "How a latched click on the floating screen is delivered. 'execute' (default): " +
            "directly via uGUI ExecuteEvents on the raycast target — the same mechanism the " +
            "game's own BaseButtons.clickButton uses; immune to input-module edge-visibility " +
            "quirks (hardware test #7: virtual-mouse button edges produced no clicks). " +
            "'virtualmouse': press/release through the virtual mouse device only. " +
            "'both': both paths (may double-fire — diagnostic use only). Deliberate drags " +
            "always go through the virtual mouse regardless of mode.");

        CombatLogFollow = _file.Bind("WorldUI", "CombatLogFollowSeat", false,
            "Combat log panel anchor mode (the panel's own FOLLOW/PINNED pin button flips " +
            "this). False (PINNED, default): the panel is STATIC IN THE WORLD — placed once " +
            "from the persisted offsets on scenario entry, then frozen until grabbed. " +
            "True (FOLLOW): the panel re-derives its place from the table anchor + seat " +
            "yaw (moves with recenters and the diorama like the other world panels; " +
            "orientation still only re-derives on recenter, never per frame). Replaces " +
            "the test-#19 'CombatLogFollow' key: its follow default plus the per-tick " +
            "yaw billboard read as the panel tracking the head (test #20).");
        CombatLogForward = _file.Bind("WorldUI", "CombatLogForward", 0.62f,
            "Combat log panel offset from the table anchor along the seat forward, real " +
            "meters (default = the old arc slot: azimuth 56° at 1.10 m). Persisted " +
            "automatically when the panel's grab bar is released.");
        CombatLogRight = _file.Bind("WorldUI", "CombatLogRight", 0.91f,
            "Combat log panel offset to the seat right, real meters (grab-persisted).");
        CombatLogUp = _file.Bind("WorldUI", "CombatLogUp", 0.45f,
            "Combat log panel height above the table plane, real meters (grab-persisted).");
        CombatLogScale = _file.Bind("WorldUI", "CombatLogScale", 1.0f,
            "Combat log panel size multiplier (two-hand grab resize; clamped 0.5-2).");
        CombatLogUserClosed = _file.Bind("WorldUI", "CombatLogUserClosed", false,
            "The user hid the combat log via its top-right X button (or the in-VR settings " +
            "'Kampflog anzeigen' toggle). While true the panel releases back to its 2D home and " +
            "does NOT auto-reappear in a scenario; the settings toggle clears it and re-shows the " +
            "panel. Kept separate from [WorldUI] CombatLog (the feature master) so re-showing " +
            "never disturbs the feature toggle or the persisted layout.");
        PanelsFollowView = _file.Bind("WorldUI", "PanelsFollowView", false,
            "LEGACY (pre-test-#8) behavior: the world panels (initiative track, element " +
            "board, combat log, objectives, button cluster) re-derive their placement from " +
            "the live rig yaw every frame, so they swing around the table with every snap " +
            "turn / world grab — perceived as a floating HUD. Default false: panels are " +
            "FIXED IN THE WORLD at the table and re-anchor only on rig rebuild or recenter.");
        HexHintFollowView = _file.Bind("WorldUI", "HexHintFollowView", true,
            "While a board-field hover hint (the TextInfoPanel/PropInfoPanel popups, e.g. " +
            "'Geschlossene Tür') is shown, drift it to a comfortable reading spot near the " +
            "CENTER of the player's field of view with LAZY (critically-damped) motion that " +
            "follows the head and settles, instead of leaving it at PropInfoSurface's fixed " +
            "table dock. Either way it stays upright and billboards toward the head. Off = " +
            "keep the dock position and only re-face it to the head.");
        ModalStyle = _file.Bind("WorldUI", "ModalStyle", "window",
            "How in-scenario 2D fallback windows (story boxes, events, tutorials, ESC " +
            "menu, rewards, choice dialogs, ...) are made operable in VR (P8, test #12). " +
            "'window' (default): only THAT window is converted to a world-space panel " +
            "floating in front of the HMD — poke AND laser clickable — and restored to " +
            "its 2D home when it closes; the full flat screen appears only when a " +
            "specific window fails to convert (reason logged). 'screen': pre-P8 " +
            "behavior — the full 2D desktop composite appears for every fallback " +
            "window. The manual A/X chord always summons the full screen regardless.");
        ManualScreenChord = _file.Bind("WorldUI", "ManualScreenChord", true,
            "Self-rescue chord: HOLD the NON-dominant lower face button (A or X) for " +
            "ManualScreenChordSeconds during a scenario to toggle the floating 2D screen " +
            "(full desktop UI + pointer) — always available when a 2D window is open that " +
            "VR does not show. Short holds still toggle the settings panel; that chord " +
            "fires on RELEASE (before the screen threshold) so the two never collide.");
        ManualScreenChordSeconds = _file.Bind("WorldUI", "ManualScreenChordSeconds", 2f,
            "Hold duration (seconds) of the non-dominant A/X for the manual flat-screen " +
            "toggle. Must be longer than [SettingsPanel] ChordHoldSeconds.");
        DemoteOverlaySolidClears = _file.Bind("WorldUI", "DemoteOverlaySolidClears", true,
            "While the floating 2D screen captures the game's cameras into its RenderTexture, " +
            "demote FULLSCREEN SolidColor clears of NON-base captured cameras (e.g. the campaign " +
            "map's depth-5 'Video Camera', whose clear is only the black backdrop behind fullscreen " +
            "videos — GH VideoCamera.PlayFullscreenVideo) to Depth-only, so they can never wipe the " +
            "composited map/UI to black. Viewport-limited (sub-rect) cameras keep their clear. " +
            "Disable for vanilla-exact clears (black letterbox backdrop during videos).");
        ScreenLayerSplit = _file.Bind("WorldUI", "ScreenLayerSplit", true,
            "Render the floating 2D screen as TWO layers (hardware test #18): the game's UI " +
            "cameras — whose Screen-Space-Camera canvases only ever render through their " +
            "assigned camera, so stereo mirror cameras can never reproduce them and the menu " +
            "went one-eyed — draw onto a transparent 'glass' quad shown identically to both " +
            "eyes at the screen plane, while 3D scene cameras and videos render a background " +
            "layer a few cm behind it with per-eye stereo depth. Off (or on any failure): " +
            "single-RT fallback — one flat mono screen in both eyes, never one-eyed.");

        SettingsChordHoldSeconds = _file.Bind("SettingsPanel", "ChordHoldSeconds", 0.6f,
            "Hold the NON-dominant lower face button (A or X) this many seconds to toggle the " +
            "in-VR settings panel — works in the menu too. 0 disables the chord. " +
            "(Recenter stays on B+Y held on BOTH hands.)");

        SettingsFollow = _file.Bind("SettingsPanel", "FollowSeat", false,
            "Settings panel anchor mode (the panel's own FOLLOW/PINNED pin button flips this). " +
            "False (PINNED, default): the panel is placed once in view when opened, then frozen " +
            "in the world until grabbed. True (FOLLOW): it re-derives its place from the table " +
            "anchor + seat yaw every tick (moves with recenters and the diorama). Either way the " +
            "pose is clamped into the forward field of view on every open, so it can never appear " +
            "out of view.");
        SettingsForward = _file.Bind("SettingsPanel", "Forward", 0.6f,
            "Settings panel offset from the table anchor along the seat forward, real meters " +
            "(grab-persisted). Only a starting point — the panel is re-clamped in front of the " +
            "head each time it is opened.");
        SettingsRight = _file.Bind("SettingsPanel", "Right", 0f,
            "Settings panel offset to the seat right, real meters (grab-persisted).");
        SettingsUp = _file.Bind("SettingsPanel", "Up", -0.05f,
            "Settings panel height above the table plane, real meters (grab-persisted).");
        SettingsScale = _file.Bind("SettingsPanel", "Scale", 1.0f,
            "Settings panel size multiplier (two-hand grab resize; clamped 0.5-2).");

        DevShowAllPanels = _file.Bind("WorldUI", "DevShowAllPanels", false,
            "DEV: spawn the world-panel layout with dummy content on the desktop (no HMD needed).");
        DevForceConvert = _file.Bind("WorldUI", "DevForceConvert", false,
            "DEV: apply the real canvas conversions in dev mode without an HMD (this moves the " +
            "game's 2D panels into world space — the desktop view changes accordingly).");
    }

    /// <summary>True while WorldUI physicalization should be applied to live game UI.</summary>
    internal static bool ConversionActive =>
        Master.Value && (VRSession.IsRunning || (Plugin.DevMode.Value && DevForceConvert.Value));
}
