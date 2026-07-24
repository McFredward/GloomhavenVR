using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// [Hands] curl/fist tunables AND (since the per-style rework) the PER-STYLE seat
/// controls, in the module's OWN config file
/// (<c>BepInEx/config/dev.gloomhavenvr.hands.cfg</c>, canonical
/// <see cref="ModuleConfig.Create"/> pattern — the legacy shared seat/trim entries stay
/// bound-but-unread in the main plugin cfg; their values were folded into the per-style
/// seed, see the per-style section below).
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

    /// <summary>Glove-pinky counter-abduction at full curl (degrees, safe fallback).</summary>
    public static float GlovePinkyCounterAbductionSafe(float fallback)
    {
        try
        {
            return GlovePinkyCounterAbduction == null
                ? fallback
                : Mathf.Clamp(GlovePinkyCounterAbduction.Value, -30f, 30f);
        }
        catch
        {
            return fallback;
        }
    }

    public static ConfigEntry<float> GlovePinkyCounterAbduction = null!;

    // ---- per-STYLE seat controls (2026-07 per-style rework) --------------------------------
    // The user's request: EVERY hand-tuning value in the debug menu is per selected hand
    // style, not just the scale. These four arrays (indexed by (int)HandStyle: Glove/
    // Plate/Arcane) are the ABSOLUTE per-style replacements for the old shared [Hands]
    // seat controls in the main plugin cfg (GripPitchOffsetDegrees / HandLateralOffset /
    // HandVerticalOffset / HandForwardOffset) + the additive per-style trims
    // (HandStyle*Trim). On FIRST bind each style is SEEDED with the old effective value
    // (global + that style's trim), so a tuned seat carries over to all three styles
    // instead of resetting; afterwards the saved per-style value always wins. The old
    // global/trim entries stay bound in Plugin (harmless orphans — nothing reads them
    // once these exist). VRHand.SyncVisualOffset re-reads the ACTIVE style's entries
    // every frame, so edits AND style switches live-apply without a rebuild.

    /// <summary>Per-style pitch (degrees) between the OpenXR grip pose and the visual hand (negative = fingers down).</summary>
    public static ConfigEntry<float>[]? StyleSeatPitch;

    /// <summary>Per-style lateral offset (meters, device-space X; positive = thumb side).</summary>
    public static ConfigEntry<float>[]? StyleSeatLateral;

    /// <summary>Per-style vertical offset (meters, device-space Y; positive = up).</summary>
    public static ConfigEntry<float>[]? StyleSeatVertical;

    /// <summary>Per-style forward offset (meters, device-space Z; positive = toward the fingertips).</summary>
    public static ConfigEntry<float>[]? StyleSeatForward;

    // Shipped defaults of the old global seat controls (Plugin.cs) — the seed fallback
    // when a Plugin entry is unbound (never in practice: Plugin.Awake binds before
    // HandsModule.Init calls Bind()).
    private const float DefaultSeatPitch = -30f;
    private const float DefaultSeatLateral = 0f;
    private const float DefaultSeatVertical = 0f;
    private const float DefaultSeatForward = -0.06f;

    /// <summary>Active-style seat pitch (degrees; legacy global+trim before Bind).</summary>
    public static float SeatPitchSafe(int style) =>
        StyleValue(StyleSeatPitch, style, LegacySeat(Plugin.GripPitchOffsetDegrees,
            Plugin.HandStylePitchTrim, style, DefaultSeatPitch));

    /// <summary>Active-style lateral (X) seat offset (meters; legacy global+trim before Bind).</summary>
    public static float SeatLateralSafe(int style) =>
        StyleValue(StyleSeatLateral, style, LegacySeat(Plugin.HandLateralOffset,
            Plugin.HandStyleLateralTrim, style, DefaultSeatLateral));

    /// <summary>Active-style vertical (Y) seat offset (meters; legacy global+trim before Bind).</summary>
    public static float SeatVerticalSafe(int style) =>
        StyleValue(StyleSeatVertical, style, LegacySeat(Plugin.HandVerticalOffset,
            Plugin.HandStyleVerticalTrim, style, DefaultSeatVertical));

    /// <summary>Active-style forward (Z) seat offset (meters; legacy global+trim before Bind).</summary>
    public static float SeatForwardSafe(int style) =>
        StyleValue(StyleSeatForward, style, LegacySeat(Plugin.HandForwardOffset,
            Plugin.HandStyleForwardTrim, style, DefaultSeatForward));

    // ---- per-STYLE wrist-HUD pose (2026-07 request B) --------------------------------------
    // The watch-face wrist HUD (WorldUI.WristHud) rests on the back of the hand MESH, whose
    // thickness/shape differs per hand style — so its pose (tilt + offset from the wrist
    // anchor) is PER STYLE too, exactly like the seat controls above. Section [WristHud] of
    // this cfg file; each style is SEEDED on first bind from the legacy global [WorldUI]
    // WristHud* entries (bound idempotently right before the seed reads them, because
    // HandsModule inits BEFORE WorldUIModule), so a tuned wrist pose carries over to all
    // three styles instead of resetting. WristHud.ApplyPose re-reads the ACTIVE style every
    // Tick, so edits AND style switches live-apply. The HUD's on/off toggle stays GLOBAL
    // ([WorldUI] WristHud) — enabling it is not a geometry preference.

    /// <summary>Per-style wrist-HUD pitch (degrees) on top of the flat-on-hand base.</summary>
    public static ConfigEntry<float>[]? StyleWristPitch;

    /// <summary>Per-style wrist-HUD yaw (degrees).</summary>
    public static ConfigEntry<float>[]? StyleWristYaw;

    /// <summary>Per-style wrist-HUD roll (degrees).</summary>
    public static ConfigEntry<float>[]? StyleWristRoll;

    /// <summary>Per-style wrist-HUD offset along wrist X (meters).</summary>
    public static ConfigEntry<float>[]? StyleWristOffsetX;

    /// <summary>Per-style wrist-HUD offset out the back of the hand (wrist +Y, meters).</summary>
    public static ConfigEntry<float>[]? StyleWristOffsetY;

    /// <summary>Per-style wrist-HUD offset toward the fingers (wrist +Z, meters).</summary>
    public static ConfigEntry<float>[]? StyleWristOffsetZ;

    // Shipped defaults of the legacy global [WorldUI] WristHud* pose (WristHud.cs statics).
    private const float DefaultWristOffsetY = 0.015f;
    private const float DefaultWristOffsetZ = 0.01f;

    /// <summary>
    /// Index of the ACTIVE hand style ([Hands] HandStyle, clamped; Glove before Plugin
    /// bound). The per-style consumers that are not per-hand rig objects (wrist HUD,
    /// held-figure pose) key off the CONFIGURED style — the settings panel edits the same.
    /// </summary>
    public static int ActiveStyleIndex
    {
        get
        {
            try
            {
                return (int)HandStyles.Clamp(
                    Plugin.HandStyle != null ? (int)Plugin.HandStyle.Value : 0);
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>The style's element of a per-style array, or <paramref name="fallback"/> (null/throw-safe).</summary>
    private static float StyleValue(ConfigEntry<float>[]? entries, int style, float fallback)
    {
        try
        {
            return entries != null ? entries[(int)HandStyles.Clamp(style)].Value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// The OLD effective seat value for a style: the shared global control + that style's
    /// additive trim (both live in the main plugin cfg). Used as the one-time SEED of the
    /// per-style entries and as the pre-Bind fallback, so behavior is identical before and
    /// after the per-style rework for an untouched config.
    /// </summary>
    private static float LegacySeat(ConfigEntry<float>? global, ConfigEntry<float>[]? trims,
        int style, float shippedDefault)
    {
        float v = shippedDefault;
        try
        {
            if (global != null)
                v = global.Value;
            if (trims != null)
                v += trims[(int)HandStyles.Clamp(style)].Value;
        }
        catch
        {
            return shippedDefault;
        }
        return v;
    }

    /// <summary>
    /// The legacy GLOBAL wrist-HUD pose value ([WorldUI] WristHud*) used as the one-time
    /// per-style SEED (and only ever consulted while the per-style key is absent from this
    /// cfg), so behavior is identical before and after the per-style rework.
    /// </summary>
    private static float LegacyWrist(ConfigEntry<float>? global, float shippedDefault)
    {
        try
        {
            return global != null ? global.Value : shippedDefault;
        }
        catch
        {
            return shippedDefault;
        }
    }

    public static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("hands");

        GlovePinkyCounterAbduction = config.Bind(
            "Hands", "GlovePinkyCounterAbduction", FingerCurler.DefaultGlovePinkyCounterAbductionDeg,
            "GLOVE style only: degrees of counter-abduction (rotation about the pinky root's " +
            "local Z = palm normal) applied at full curl, scaled by the curl value. The glove " +
            "pinky MESH tube leans ~18° outward while its bone chain is straight, so a pure " +
            "local-X fist leaves the curled pinky visibly splayed outward; this pulls it back " +
            "toward the ring finger as it curls (sign auto-flips for the right hand). 0 disables. " +
            "Live-tunable.");

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

        // PER-STYLE seat controls (per-style rework): one absolute pitch/X/Y/Z quartet per
        // hand style. The BIND DEFAULT of each entry is the OLD effective value for that
        // style (shared global + per-style trim, read from the already-bound main plugin
        // cfg) — a first run therefore SEEDS every style with the user's tuned seat, while
        // any later run keeps the saved per-style value (BepInEx returns saved over
        // default). No marker entry needed: the seed is only ever consulted while the key
        // is absent from this cfg file.
        string[] styleNames = { "Glove", "Plate", "Arcane" }; // index == (int)HandStyle
        StyleSeatPitch = new ConfigEntry<float>[HandStyles.Count];
        StyleSeatLateral = new ConfigEntry<float>[HandStyles.Count];
        StyleSeatVertical = new ConfigEntry<float>[HandStyles.Count];
        StyleSeatForward = new ConfigEntry<float>[HandStyles.Count];
        for (int i = 0; i < HandStyles.Count; i++)
        {
            string s = styleNames[i];
            StyleSeatPitch[i] = config.Bind(
                "Hands", $"{s}GripPitchDegrees",
                LegacySeat(Plugin.GripPitchOffsetDegrees, Plugin.HandStylePitchTrim, i, DefaultSeatPitch),
                $"Pitch (degrees) between the tracked OpenXR grip pose and the visual hand while " +
                $"the {s} style is worn — NEGATIVE tilts the fingertips DOWN. PER-STYLE absolute " +
                "value (supersedes the old shared GripPitchOffsetDegrees + trim in the main cfg; " +
                "seeded from them on first run). Live-tunable — the hands re-seat next frame.");
            StyleSeatLateral[i] = config.Bind(
                "Hands", $"{s}LateralOffset",
                LegacySeat(Plugin.HandLateralOffset, Plugin.HandStyleLateralTrim, i, DefaultSeatLateral),
                $"Lateral offset (meters, device-space X; POSITIVE = toward the thumb side) of the " +
                $"visual hand from the grip pose while the {s} style is worn. PER-STYLE absolute " +
                "value (supersedes the old shared HandLateralOffset + trim; seeded on first run). " +
                "Live-tunable.");
            StyleSeatVertical[i] = config.Bind(
                "Hands", $"{s}VerticalOffset",
                LegacySeat(Plugin.HandVerticalOffset, Plugin.HandStyleVerticalTrim, i, DefaultSeatVertical),
                $"Vertical offset (meters, device-space Y; POSITIVE = up) of the visual hand from " +
                $"the grip pose while the {s} style is worn. PER-STYLE absolute value (supersedes " +
                "the old shared HandVerticalOffset + trim; seeded on first run). Live-tunable.");
            StyleSeatForward[i] = config.Bind(
                "Hands", $"{s}ForwardOffset",
                LegacySeat(Plugin.HandForwardOffset, Plugin.HandStyleForwardTrim, i, DefaultSeatForward),
                $"Forward/depth offset (meters, device-space Z; POSITIVE = toward the fingertips) " +
                $"of the visual hand from the grip pose while the {s} style is worn. PER-STYLE " +
                "absolute value (supersedes the old shared HandForwardOffset + trim; seeded on " +
                "first run). Live-tunable.");
        }
        // PER-STYLE wrist-HUD pose (request B): seed each style from the legacy GLOBAL
        // [WorldUI] WristHud* entries. WorldUIConfig.Bind is idempotent and is called here
        // FIRST because HandsModule inits before WorldUIModule — without it the seed would
        // read unbound entries and silently reset a tuned wrist pose to the shipped defaults.
        WorldUI.WorldUIConfig.Bind();
        StyleWristPitch = new ConfigEntry<float>[HandStyles.Count];
        StyleWristYaw = new ConfigEntry<float>[HandStyles.Count];
        StyleWristRoll = new ConfigEntry<float>[HandStyles.Count];
        StyleWristOffsetX = new ConfigEntry<float>[HandStyles.Count];
        StyleWristOffsetY = new ConfigEntry<float>[HandStyles.Count];
        StyleWristOffsetZ = new ConfigEntry<float>[HandStyles.Count];
        for (int i = 0; i < HandStyles.Count; i++)
        {
            string s = styleNames[i];
            string per = $"PER-STYLE absolute value while the {s} hand style is worn " +
                "(supersedes the shared [WorldUI] WristHud* entry it was seeded from on first " +
                "run). Live-tunable — WristHud re-applies every tick.";
            StyleWristPitch[i] = config.Bind(
                "WristHud", $"{s}Pitch", LegacyWrist(WorldUI.WorldUIConfig.WristHudPitch, 0f),
                $"Wrist overview HUD tilt (pitch, degrees) on top of the flat-on-hand base. {per}");
            StyleWristYaw[i] = config.Bind(
                "WristHud", $"{s}Yaw", LegacyWrist(WorldUI.WorldUIConfig.WristHudYaw, 0f),
                $"Wrist overview HUD yaw (degrees). {per}");
            StyleWristRoll[i] = config.Bind(
                "WristHud", $"{s}Roll", LegacyWrist(WorldUI.WorldUIConfig.WristHudRoll, 0f),
                $"Wrist overview HUD roll (degrees). {per}");
            StyleWristOffsetX[i] = config.Bind(
                "WristHud", $"{s}OffsetX", LegacyWrist(WorldUI.WorldUIConfig.WristHudOffsetX, 0f),
                $"Wrist overview HUD offset along wrist X, real meters. {per}");
            StyleWristOffsetY[i] = config.Bind(
                "WristHud", $"{s}OffsetY",
                LegacyWrist(WorldUI.WorldUIConfig.WristHudOffsetY, DefaultWristOffsetY),
                $"Wrist overview HUD offset out the back of the hand (wrist +Y), real meters. {per}");
            StyleWristOffsetZ[i] = config.Bind(
                "WristHud", $"{s}OffsetZ",
                LegacyWrist(WorldUI.WorldUIConfig.WristHudOffsetZ, DefaultWristOffsetZ),
                $"Wrist overview HUD offset toward the fingers (wrist +Z), real meters. {per}");
        }

        VRLog.Info("Hands", "[Hands] Per-style seat controls bound: " +
            string.Join("; ", System.Array.ConvertAll(styleNames, n =>
            {
                int i = System.Array.IndexOf(styleNames, n);
                return $"{n} pitch {StyleSeatPitch[i].Value:0.#}° X/Y/Z " +
                       $"{StyleSeatLateral[i].Value * 1000f:0}/{StyleSeatVertical[i].Value * 1000f:0}/" +
                       $"{StyleSeatForward[i].Value * 1000f:0} mm";
            })) + " (first run seeds from the old global seat + trims).");

        // The toggle is the user's A/B lever during hardware tests — make every flip
        // land in the log so the session record shows WHICH fist was being judged.
        TestFist.SettingChanged += (_, _) =>
            VRLog.Info("Hands", $"[Hands] TestFist is now {(TestFist.Value ? "ON — forcing curl 1.0 on all fingers of both hands" : "OFF — controller input drives the fingers again")}.");
    }
}
