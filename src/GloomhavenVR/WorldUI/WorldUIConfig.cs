using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Phase-3c configuration, bound into its own file
/// (<c>BepInEx/config/worldui.gloomhavenvr.cfg</c>) so the WorldUI worker does not
/// touch the frozen <see cref="Plugin"/> surface. Every physicalized surface is
/// individually toggleable (ARCHITECTURE §7); all entries are read live, so flipping
/// them in the config manager takes effect on the next relevant rebuild.
/// </summary>
internal static class WorldUIConfig
{
    private static ConfigFile? _file;

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

    /// <summary>Flat-screen width in real-world meters.</summary>
    internal static ConfigEntry<float> FlatScreenWidth = null!;

    // ---- dev ---------------------------------------------------------------------------
    /// <summary>Spawn the world-panel layout with dummy content on the desktop.</summary>
    internal static ConfigEntry<bool> DevShowAllPanels = null!;

    /// <summary>Apply the real canvas conversions in dev mode without an HMD.</summary>
    internal static ConfigEntry<bool> DevForceConvert = null!;

    internal static void Bind()
    {
        if (_file != null)
            return;

        _file = new ConfigFile(Path.Combine(Paths.ConfigPath, "worldui.gloomhavenvr.cfg"), true);

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
        FlatScreenWidth = _file.Bind("WorldUI", "FlatScreenWidth", 1.4f,
            "Width of the floating 2D screen in real-world meters.");

        DevShowAllPanels = _file.Bind("WorldUI", "DevShowAllPanels", false,
            "DEV: spawn the world-panel layout with dummy content on the desktop (no HMD needed).");
        DevForceConvert = _file.Bind("WorldUI", "DevForceConvert", false,
            "DEV: apply the real canvas conversions in dev mode without an HMD (this moves the " +
            "game's 2D panels into world space — the desktop view changes accordingly).");
    }

    /// <summary>True while WorldUI physicalization should be applied to live game UI.</summary>
    internal static bool ConversionActive =>
        Core.VRSession.IsRunning || (Plugin.DevMode.Value && DevForceConvert.Value);
}
