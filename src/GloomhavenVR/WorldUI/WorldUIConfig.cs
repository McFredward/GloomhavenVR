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
    internal static ConfigEntry<bool> PhaseBanner = null!;
    internal static ConfigEntry<bool> Dialogs = null!;
    internal static ConfigEntry<bool> StatPanels = null!;
    internal static ConfigEntry<bool> ActorBars = null!;
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

    // ---- settings panel (MISSION B) ------------------------------------------------------
    /// <summary>Show the small gear pokeable next to the button cluster (scenario only).</summary>
    internal static ConfigEntry<bool> SettingsGearButton = null!;

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
            "Element infusion board as a world-space panel near the initiative track.");
        CombatLog = _file.Bind("WorldUI", "CombatLog", true,
            "Combat log as a world-space panel at the table's far side.");
        Objectives = _file.Bind("WorldUI", "Objectives", true,
            "Scenario objectives as a world-space panel at the table's far side.");
        PhaseBanner = _file.Bind("WorldUI", "PhaseBanner", true,
            "Phase banner (round/turn announcements) as a brief HMD-anchored toast.");
        Dialogs = _file.Bind("WorldUI", "Dialogs", true,
            "Confirmation dialogs as world-space modals in front of the HMD (poke yes/no).");
        StatPanels = _file.Bind("WorldUI", "StatPanels", true,
            "Actor/monster stat panels as world panels near the table (opened by the game / Phase-3a poke).");
        ActorBars = _file.Bind("WorldUI", "ActorBars", true,
            "True world-space HP/effect bars above the miniatures (replaces the screen-projected bars).");
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

        SettingsGearButton = _file.Bind("SettingsPanel", "GearButton", true,
            "Show a small 'SET' gear pokeable at the table edge (next to Ready/Undo/Skip) " +
            "that opens the in-VR settings panel.");
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
