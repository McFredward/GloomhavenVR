using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// [Hands] curl/fist tunables in the module's OWN config file
/// (<c>BepInEx/config/dev.gloomhavenvr.hands.cfg</c>, canonical
/// <see cref="ModuleConfig.Create"/> pattern — the legacy seat/style entries stay in
/// the main plugin cfg, this file owns the FIST investigation knobs).
///
/// Why these exist (on-device "mostly no fist" investigation): the runtime fist chain
/// is  raw XR grip/trigger float → curl target → exponential smoothing → per-joint
/// Euler-X on the rig bones.  Range can be lost at TWO places only:
///  1. INPUT — Quest 3 via Virtual Desktop often plateaus the analog grip below 1.0
///     at a comfortable squeeze, so the curl target never reaches full range.
///     <see cref="CurlInputFullAt"/> remaps the raw value so `raw &gt;= CurlInputFullAt`
///     is a FULL curl.
///  2. APPLICATION — the per-joint max angles may read visually insufficient on the
///     styled rigs. <see cref="CurlProximal"/>/<see cref="CurlMiddle"/>/<see cref="CurlTip"/>
///     expose the 75/95/65-degree defaults for live tuning (the thumb's 25/45/60 scale
///     proportionally with them).
/// <see cref="TestFist"/> separates the two: it forces curl = 1.0 on every finger of
/// both hands regardless of input, so the in-headset look answers "is the RIG capable
/// of a fist?" independent of what the controller delivers.
///
/// All reads go through the *Safe accessors so <see cref="FingerCurler"/> (also used by
/// Net.RemoteAvatar) never throws before <see cref="Bind"/> ran.
/// </summary>
internal static class HandsConfig
{
    /// <summary>Debug: force a full fist (curl 1.0, all fingers, both hands) regardless of input.</summary>
    public static ConfigEntry<bool> TestFist = null!;

    /// <summary>Full-curl angle (degrees) of the proximal/root finger joint. Default 75.</summary>
    public static ConfigEntry<float> CurlProximal = null!;

    /// <summary>Full-curl angle (degrees) of the middle finger joint. Default 95.</summary>
    public static ConfigEntry<float> CurlMiddle = null!;

    /// <summary>Full-curl angle (degrees) of the tip/distal finger joint. Default 65.</summary>
    public static ConfigEntry<float> CurlTip = null!;

    /// <summary>Raw grip/trigger value that already counts as a FULL curl (input remap).</summary>
    public static ConfigEntry<float> CurlInputFullAt = null!;

    private const float DefaultCurlInputFullAt = 0.85f;

    // Current full-curl defaults (2026-07 fist fix: 65/80/50 read visibly open even at
    // curl 1.0 on hardware — see FingerCurler.DefaultFingerMaxAngles). The OLD trio is
    // kept for the one-time saved-config migration in Bind.
    private const float DefaultCurlProximal = 75f;
    private const float DefaultCurlMiddle = 95f;
    private const float DefaultCurlTip = 65f;
    private const float OldCurlProximal = 65f;
    private const float OldCurlMiddle = 80f;
    private const float OldCurlTip = 50f;

    private static ConfigFile? _file;

    /// <summary>True while the TestFist debug override is on (false before Bind).</summary>
    public static bool TestFistActive
    {
        get
        {
            try
            {
                return TestFist != null && TestFist.Value;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The remap ceiling, clamped to a sane range (default before Bind).</summary>
    public static float CurlInputFullAtSafe()
    {
        try
        {
            return CurlInputFullAt != null
                ? Mathf.Clamp(CurlInputFullAt.Value, 0.3f, 1f)
                : DefaultCurlInputFullAt;
        }
        catch
        {
            return DefaultCurlInputFullAt;
        }
    }

    /// <summary>The three configured per-joint max angles (fallback = the FingerCurler defaults).</summary>
    public static Vector3 FingerMaxAnglesSafe(Vector3 fallback)
    {
        try
        {
            if (CurlProximal == null || CurlMiddle == null || CurlTip == null)
                return fallback;
            return new Vector3(
                Mathf.Clamp(CurlProximal.Value, 0f, 130f),
                Mathf.Clamp(CurlMiddle.Value, 0f, 130f),
                Mathf.Clamp(CurlTip.Value, 0f, 130f));
        }
        catch
        {
            return fallback;
        }
    }

    public static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("hands");

        TestFist = config.Bind(
            "Hands", "TestFist", false,
            "DEBUG: force a FULL fist (curl 1.0 on all five fingers of both hands) regardless " +
            "of controller input. Toggle this on to see the maximum fist the current rig can " +
            "produce — if this fist looks right but squeezing the controller does not close the " +
            "hand, the loss is in the INPUT (grip value never reaching full range); if even " +
            "this fist stays open, the loss is in the rig/angles (raise CurlProximal/CurlMiddle/CurlTip).");
        CurlProximal = config.Bind(
            "Hands", "CurlProximal", DefaultCurlProximal,
            "Full-curl rotation (degrees, local X) of each finger's PROXIMAL (root/knuckle) " +
            "joint at curl 1.0. The thumb's proximal angle scales proportionally " +
            "(default thumb 25 at finger 75). Live-tunable.");
        CurlMiddle = config.Bind(
            "Hands", "CurlMiddle", DefaultCurlMiddle,
            "Full-curl rotation (degrees, local X) of each finger's MIDDLE joint at curl 1.0. " +
            "The thumb's middle angle scales proportionally (default thumb 45 at finger 95). " +
            "Live-tunable.");
        CurlTip = config.Bind(
            "Hands", "CurlTip", DefaultCurlTip,
            "Full-curl rotation (degrees, local X) of each finger's TIP (distal) joint at " +
            "curl 1.0. The thumb's tip angle scales proportionally (default thumb 60 at " +
            "finger 65). Live-tunable.");

        // ONE-TIME MIGRATION: a cfg written by an older build has the OLD 65/80/50
        // defaults SAVED, and BepInEx returns saved values over new defaults — the
        // angle raise would silently never land for existing installs. If all three
        // still sit exactly on the old defaults (i.e. the user never hand-tuned them),
        // lift them to the new defaults; any hand-tuned value is left alone.
        if (Mathf.Approximately(CurlProximal.Value, OldCurlProximal)
            && Mathf.Approximately(CurlMiddle.Value, OldCurlMiddle)
            && Mathf.Approximately(CurlTip.Value, OldCurlTip))
        {
            CurlProximal.Value = DefaultCurlProximal;
            CurlMiddle.Value = DefaultCurlMiddle;
            CurlTip.Value = DefaultCurlTip;
            VRLog.Info("Hands", "[Hands] Curl angles migrated from the old 65/80/50 defaults to " +
                                $"{DefaultCurlProximal:0}/{DefaultCurlMiddle:0}/{DefaultCurlTip:0} " +
                                "(2026-07 fist fix — a hand-tuned config would have been left alone).");
        }
        CurlInputFullAt = config.Bind(
            "Hands", "CurlInputFullAt", DefaultCurlInputFullAt,
            "Raw analog grip/trigger value (0.3-1.0) that already counts as a FULL curl: " +
            "curl = raw / this, clamped to 1. Quest 3 controllers via Virtual Desktop often " +
            "plateau the analog grip below 1.0 at a comfortable full squeeze — lower this if " +
            "the '[Hands] squeeze released: peak grip=...' log line shows your peak never " +
            "reaches 1.0. Set 1.0 for the raw, unremapped input.");

        // The toggle is the user's A/B lever during hardware tests — make every flip
        // land in the log so the session record shows WHICH fist was being judged.
        TestFist.SettingChanged += (_, _) =>
            VRLog.Info("Hands", $"[Hands] TestFist is now {(TestFist.Value ? "ON — forcing curl 1.0 on all fingers of both hands" : "OFF — controller input drives the fingers again")}.");
    }
}
