using BepInEx.Configuration;
using GloomhavenVR.Core;

namespace GloomhavenVR.Board;

/// <summary>
/// [Board] config section (Phase 3a). P5 (MISSION A.9): bound against the module's OWN
/// config file (<c>BepInEx/config/dev.gloomhavenvr.board.cfg</c>, via
/// <see cref="ModuleConfig.Create"/>) — the canonical module-config pattern; the
/// pre-P5 <c>FindObjectOfType&lt;Plugin&gt;().Config</c> lookup is retired.
/// </summary>
internal static class BoardConfig
{
    /// <summary>Disable fingertip near-touch picking; always use the far ray (desktop/dev testing).</summary>
    public static ConfigEntry<bool> ForceFarMode = null!;

    /// <summary>Fingertip-to-board distance (real meters) below which near-touch picking takes over.</summary>
    public static ConfigEntry<float> TouchRange = null!;

    /// <summary>Snap the projected pick point (game cursor) to the hovered hex's center.</summary>
    public static ConfigEntry<bool> SnapToHexCenter = null!;

    /// <summary>Haptic tick when the pick lands on a new valid interactable.</summary>
    public static ConfigEntry<bool> HoverHaptics = null!;

    /// <summary>Thumbstick |x| needed to rotate the AoE pattern one 60° step.</summary>
    public static ConfigEntry<float> AoeFlickThreshold = null!;

    /// <summary>Seconds between AoE rotation steps while the stick is held past the threshold.</summary>
    public static ConfigEntry<float> AoeRepeatInterval = null!;

    private static ConfigFile? _file;

    public static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("board");

        ForceFarMode = config.Bind(
            "Board", "ForceFarMode", false,
            "Disable fingertip near-touch picking and always use the far ray. " +
            "Useful for desktop/dev testing ([Dev] SimulateHands) where the fake hands " +
            "never reach the board.");
        TouchRange = config.Bind(
            "Board", "TouchRange", 0.10f,
            "How close (real meters, scaled by the diorama) the index fingertip must be " +
            "above the board before near-touch picking takes over from the far ray.");
        SnapToHexCenter = config.Bind(
            "Board", "SnapToHexCenter", false,
            "Snap the projected pick point (the virtual game cursor) to the hovered hex's " +
            "center — steadies hover/tooltip anchoring on small hexes.");
        HoverHaptics = config.Bind(
            "Board", "HoverHaptics", true,
            "Haptic tick on the picking hand when the pick moves onto a new valid board target.");
        AoeFlickThreshold = config.Bind(
            "Board", "AoeFlickThreshold", 0.6f,
            "Thumbstick horizontal deflection (0.2-0.95) that rotates an active AoE pattern " +
            "one 60 degree step (left = counter-clockwise, right = clockwise).");
        AoeRepeatInterval = config.Bind(
            "Board", "AoeRepeatInterval", 0.35f,
            "Seconds between AoE rotation steps while the stick stays deflected. Values below " +
            "0.3 fight the game's own direction latch in RotateAOEClockwise (it ignores " +
            "direction changes within 0.3 s).");
    }
}
