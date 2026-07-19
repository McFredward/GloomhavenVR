using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// [FigureGrab] config section (P8). Bound against a module-owned config file
/// (<c>BepInEx/config/dev.gloomhavenvr.figuregrab.cfg</c>) via
/// <see cref="ModuleConfig.Create"/> — self-contained, so this worker never has to edit
/// the shared <see cref="BoardConfig"/>. Feature toggle plus the in-hand pose tunables
/// (held scale / offset / tilt) that need a hardware pass to feel right.
/// </summary>
internal static class FigureGrabConfig
{
    /// <summary>Master toggle for grabbing board figures into the hand.</summary>
    public static ConfigEntry<bool> GrabFigures = null!;

    /// <summary>
    /// Inspection zoom applied ON TOP of the figure's preserved board world-scale while
    /// held (1 = same size it is on the board, just in your hand; &gt;1 enlarges it).
    /// </summary>
    public static ConfigEntry<float> HeldScale = null!;

    /// <summary>Held offset toward the fingertips (GrabAnchor-local Z).</summary>
    public static ConfigEntry<float> HeldOffsetForward = null!;

    /// <summary>Held offset out of the palm (GrabAnchor-local Y).</summary>
    public static ConfigEntry<float> HeldOffsetUp = null!;

    /// <summary>Held tilt about the GrabAnchor X axis (degrees) — angle the mini toward the face.</summary>
    public static ConfigEntry<float> HeldTiltDegrees = null!;

    /// <summary>GrabAnchor-local held position (see the three offset entries).</summary>
    internal static Vector3 HeldOffset => new(0f, HeldOffsetUp.Value, HeldOffsetForward.Value);

    /// <summary>GrabAnchor-local held rotation (tilt only).</summary>
    internal static Vector3 HeldEuler => new(HeldTiltDegrees.Value, 0f, 0f);

    private static ConfigFile? _file;

    public static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("figuregrab");

        GrabFigures = config.Bind(
            "FigureGrab", "GrabFigures", true,
            "Grab a board figure (hero OR monster) into your hand with the GRIP button to " +
            "inspect it up close — pure immersion, no gameplay effect. Release to snap it " +
            "back to its board cell.");
        HeldScale = config.Bind(
            "FigureGrab", "HeldScale", 1.5f,
            "Inspection zoom applied on top of the figure's board world-scale while held " +
            "(1 = board size in your hand; higher enlarges it). Tune on hardware.");
        HeldOffsetForward = config.Bind(
            "FigureGrab", "HeldOffsetForward", 0f,
            "Held position offset toward the fingertips (grab-anchor local Z). Tune on hardware.");
        HeldOffsetUp = config.Bind(
            "FigureGrab", "HeldOffsetUp", 0f,
            "Held position offset out of the palm (grab-anchor local Y). Tune on hardware.");
        HeldTiltDegrees = config.Bind(
            "FigureGrab", "HeldTiltDegrees", 0f,
            "Held tilt about the grab-anchor X axis (degrees) — angle the mini toward your " +
            "face for inspection. Tune on hardware.");
    }
}
