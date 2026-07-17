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

    /// <summary>Auto-show the floating 2D screen while no scenario runs (Menu2D mode).</summary>
    internal static ConfigEntry<bool> FlatScreenAutoShow = null!;

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

    /// <summary>Click delivery: "execute" (ExecuteEvents, default) | "virtualmouse" | "both".</summary>
    internal static ConfigEntry<string> ClickMode = null!;

    /// <summary>Legacy: world panels re-orient with the rig yaw per frame (HUD-like). Default false (P6).</summary>
    internal static ConfigEntry<bool> PanelsFollowView = null!;

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
        FlatScreenAutoShow = _file.Bind("WorldUI", "FlatScreenAutoShow", true,
            "Automatically show the floating 2D screen while no scenario runs (main menu, map) " +
            "and hide it in scenario modes.");
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
        PanelsFollowView = _file.Bind("WorldUI", "PanelsFollowView", false,
            "LEGACY (pre-test-#8) behavior: the world panels (initiative track, element " +
            "board, combat log, objectives, button cluster) re-derive their placement from " +
            "the live rig yaw every frame, so they swing around the table with every snap " +
            "turn / world grab — perceived as a floating HUD. Default false: panels are " +
            "FIXED IN THE WORLD at the table and re-anchor only on rig rebuild or recenter.");
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
