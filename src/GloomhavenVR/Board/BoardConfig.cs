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
    /// <summary>
    /// Grip-gated direct fingertip touch on board hexes (the gesture the tutorial teaches):
    /// hold the GRIP, put the index fingertip on a hex, and the game receives the SAME click
    /// the laser trigger would have sent on that hex.
    /// </summary>
    public static ConfigEntry<bool> TouchTilesWithFingertip = null!;

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

        // The old [Board] ForceFarMode override is DELETED (2026-08 dead-settings sweep). It
        // predated the GRIP gate — back when the near pick existed unconditionally, "far only"
        // was the sane shipped state and every tuned cfg on disk carried `true`, which silently
        // kept the fingertip feature dead. Since TouchTilesWithFingertip the near pick only
        // exists while the grip is held; that single switch replaced the override for good.
        TouchTilesWithFingertip = config.Bind(
            "Board", "TouchTilesWithFingertip", Defaults.TouchTilesWithFingertip,
            "Touch a highlighted hex directly with your index fingertip to commit the same " +
            "action the laser click commits. Only ever fires while the GRIP button is held " +
            "(make a fist and stick the index finger out), so accidental brushes across the " +
            "board can never trigger anything. One commit per hex entry; leave the hex, lift " +
            "the finger or let go of the grip to arm the next one.");
        TouchRange = config.Bind(
            "Board", "TouchRange", Defaults.TouchRange,
            "How close (real meters, scaled by the diorama) the index fingertip must be " +
            "above the board before near-touch picking takes over from the far ray.");
        SnapToHexCenter = config.Bind(
            "Board", "SnapToHexCenter", Defaults.SnapToHexCenter,
            "Snap the projected pick point (the virtual game cursor) to the hovered hex's " +
            "center — steadies hover/tooltip anchoring on small hexes.");
        HoverHaptics = config.Bind(
            "Board", "HoverHaptics", Defaults.HoverHaptics,
            "Haptic tick on the picking hand when the pick moves onto a new valid board target.");
        AoeFlickThreshold = config.Bind(
            "Board", "AoeFlickThreshold", Defaults.AoeFlickThreshold,
            "Thumbstick horizontal deflection (0.2-0.95) that rotates an active AoE pattern " +
            "one 60 degree step (left = counter-clockwise, right = clockwise).");
        AoeRepeatInterval = config.Bind(
            "Board", "AoeRepeatInterval", Defaults.AoeRepeatInterval,
            "Seconds between AoE rotation steps while the stick stays deflected. Values below " +
            "0.3 fight the game's own direction latch in RotateAOEClockwise (it ignores " +
            "direction changes within 0.3 s).");
    }
}
