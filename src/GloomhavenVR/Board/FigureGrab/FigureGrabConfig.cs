using System;
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

    /// <summary>Held lateral offset toward the thumb–index pinch (GrabAnchor-local X).</summary>
    public static ConfigEntry<float> HeldOffsetSide = null!;

    /// <summary>
    /// Hold the mini UPRIGHT (feet→head along world up), pinched between thumb and index and
    /// facing the player — like inspecting a chess piece. When false, the legacy palm pose is
    /// used (<see cref="HeldEuler"/> tilt relative to the hand, lays it flat).
    /// </summary>
    public static ConfigEntry<bool> HeldUpright = null!;

    /// <summary>Held tilt (degrees) — tip the mini toward the face for inspection (both modes).</summary>
    public static ConfigEntry<float> HeldTiltDegrees = null!;

    /// <summary>
    /// Upright mode only: extra yaw (degrees) spinning the mini's readable front toward the
    /// player. Auto-facing assumes the model's local +Z is its front; set 180 if it shows its
    /// back. Tune on hardware.
    /// </summary>
    public static ConfigEntry<float> HeldFaceYawDegrees = null!;

    /// <summary>GrabAnchor-local held position (side / up / forward toward the pinch point).</summary>
    internal static Vector3 HeldOffset => new(HeldOffsetSide.Value, HeldOffsetUp.Value, HeldOffsetForward.Value);

    /// <summary>Legacy palm-pose rotation (tilt only), relative to the GrabAnchor.</summary>
    internal static Vector3 HeldEuler => new(HeldTiltDegrees.Value, 0f, 0f);

    private static ConfigFile? _file;

    public static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("figuregrab");

        GrabFigures = config.Bind(
            "FigureGrab", "GrabFigures", true,
            "Grab a board figure (hero OR monster) into your hand with the TRIGGER to " +
            "inspect it up close — pure immersion, no gameplay effect. Release to snap it " +
            "back to its board cell.");
        HeldScale = config.Bind(
            "FigureGrab", "HeldScale", 1.5f,
            "Inspection zoom applied on top of the figure's board world-scale while held " +
            "(1 = board size in your hand; higher enlarges it). Tune on hardware.");
        HeldOffsetForward = config.Bind(
            "FigureGrab", "HeldOffsetForward", 0.03f,
            "Held position offset toward the fingertips (grab-anchor local Z) — moves the mini " +
            "out to the thumb–index pinch point. Tune on hardware.");
        HeldOffsetUp = config.Bind(
            "FigureGrab", "HeldOffsetUp", 0.03f,
            "Held position offset out of the palm (grab-anchor local Y). Tune on hardware.");
        HeldOffsetSide = config.Bind(
            "FigureGrab", "HeldOffsetSide", 0f,
            "Held lateral position offset (grab-anchor local X) toward the thumb–index pinch. " +
            "Tune on hardware.");
        HeldUpright = config.Bind(
            "FigureGrab", "HeldUpright", true,
            "Hold the mini UPRIGHT (standing, pointing up) pinched between thumb and index and " +
            "facing you, like inspecting a chess piece. False = legacy flat-on-palm pose.");
        HeldTiltDegrees = config.Bind(
            "FigureGrab", "HeldTiltDegrees", 0f,
            "Held tilt (degrees) — tip the mini toward your face for inspection. Tune on hardware.");
        HeldFaceYawDegrees = config.Bind(
            "FigureGrab", "HeldFaceYawDegrees", 0f,
            "Upright mode only: extra yaw (degrees) to spin the mini's front toward you. Set 180 " +
            "if it faces away. Tune on hardware.");

        // Live-tune hook: any held-pose tunable change re-poses the currently-held mini in-hand
        // (the in-headset debug-menu steppers), so tuning is interactive. BepInEx still persists
        // every write to dev.gloomhavenvr.figuregrab.cfg.
        void Reapply(object sender, EventArgs e) => FigureGrabbable.ReapplyAll();
        HeldScale.SettingChanged += Reapply;
        HeldOffsetForward.SettingChanged += Reapply;
        HeldOffsetUp.SettingChanged += Reapply;
        HeldOffsetSide.SettingChanged += Reapply;
        HeldUpright.SettingChanged += Reapply;
        HeldTiltDegrees.SettingChanged += Reapply;
        HeldFaceYawDegrees.SettingChanged += Reapply;
    }
}
