using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// [Hands] curl/fist tunables AND (since the per-style rework) the PER-STYLE seat
/// controls, in the module's OWN config file
/// (<c>BepInEx/config/dev.gloomhavenvr.hands.cfg</c>, canonical
/// <see cref="ModuleConfig.Create"/> pattern — the legacy shared seat/trim entries are gone
/// from Plugin entirely; the per-style tables below replaced them, see that section).
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
///
/// WHOSE HAND THESE ANSWER FOR: this client's own, and no other. The curl a peer's finger is at
/// rides the wire as 0..1; the DEGREES it is applied at are a per-player choice and ride record 28
/// (ids 201..204) with it, so a remote hand resolves its limits from the OWNER's record and never
/// from the accessors below. That distinction is not left to the reader — every FingerCurler call
/// site states it through <see cref="FingerCurler.GripLimits"/>, which defaults to "ask my own
/// dials" so a path that forgets can only fall back to the local answer.
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

    private const float DefaultCurlInputFullAt = Defaults.CurlInputFullAt;

    // Current full-curl defaults (2026-07 fist fix: 65/80/50 read visibly open even at
    // curl 1.0 on hardware — see FingerCurler.DefaultFingerMaxAngles). The OLD trio is
    // kept for the one-time saved-config migration in Bind.
    private const float DefaultCurlProximal = Defaults.CurlProximal;
    private const float DefaultCurlMiddle = Defaults.CurlMiddle;
    private const float DefaultCurlTip = Defaults.CurlTip;
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

    /// <summary>THIS VIEWER's three configured per-joint max angles (fallback = the FingerCurler
    /// defaults, 75/95/65 degrees). Only ever an answer about the LOCAL player's hands — a peer's
    /// come off their own tuning record; see the class remarks and
    /// <see cref="FingerCurler.GripLimits"/>.</summary>
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

    /// <summary>THIS VIEWER's glove-pinky counter-abduction at full curl (degrees, safe fallback;
    /// 14 shipped). Local hands only, for the reason <see cref="FingerMaxAnglesSafe"/> gives.</summary>
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

    // ---- ghost hand while the card fan is open ---------------------------------------------
    // User request: with the fan open on the palm, the HAND itself covers card details. The
    // hand that carries the open fan fades to a "Geisterhand" — still visible (you must be able
    // to see where your fingers are to grab a card), just see-through. Both toggles SHIP ON
    // (Defaults.Hands.cs; they shipped off when the feature was new). Applied reversibly on
    // per-renderer material CLONES (see HandGhost) and mirrored onto the avatar mirror + the
    // multiplayer avatar, so all three renderings of this player agree.

    /// <summary>Fade the hand that carries the OPEN card fan ("ghost hand").</summary>
    public static ConfigEntry<bool> GhostHandOnFan = null!;

    /// <summary>Also ghost a hand while it HOLDS a card (independent of the fan toggle).</summary>
    public static ConfigEntry<bool> GhostHandOnHeldCard = null!;

    /// <summary>Do the hands push scenery cloth (curtains, hangings) out of the way?
    /// See <see cref="SceneClothHands"/>.</summary>
    public static ConfigEntry<bool> HandsDisturbScenery = null!;
    public static ConfigEntry<bool> HandsDisturbVfx = null!;

    /// <summary>Ghost-hand transparency STRENGTH 0.05..0.95 (higher = more see-through).</summary>
    public static ConfigEntry<float> GhostHandStrength = null!;

    // ---- per-STYLE seat controls (2026-07 per-style rework) --------------------------------
    // The user's request: EVERY hand-tuning value in the debug menu is per selected hand
    // style, not just the scale. These four arrays (indexed by (int)HandStyle: Glove/
    // Plate/Arcane) are the ABSOLUTE per-style replacements for the old shared [Hands]
    // seat controls in the main plugin cfg (GripPitchOffsetDegrees etc.) + the additive
    // per-style trims — both since deleted from Plugin entirely (2026-08 dead-settings
    // sweep; values in old .cfg files survive as harmless BepInEx orphans).
    // VRHand.SyncVisualOffset re-reads the ACTIVE style's entries every frame, so edits
    // AND style switches live-apply without a rebuild.

    /// <summary>Per-style pitch (degrees) between the OpenXR grip pose and the visual hand (negative = fingers down).</summary>
    public static ConfigEntry<float>[]? StyleSeatPitch;

    /// <summary>Per-style lateral offset (meters, device-space X; positive = thumb side).</summary>
    public static ConfigEntry<float>[]? StyleSeatLateral;

    /// <summary>Per-style vertical offset (meters, device-space Y; positive = up).</summary>
    public static ConfigEntry<float>[]? StyleSeatVertical;

    /// <summary>Per-style forward offset (meters, device-space Z; positive = toward the fingertips).</summary>
    public static ConfigEntry<float>[]? StyleSeatForward;

    /// <summary>
    /// Per-style ROLL (degrees) around the controller's forward axis — the twist that makes a hand
    /// look rotated on the controller rather than sitting on it. MIRRORED between the hands, which
    /// is why it is separate from pitch; the reason it must be is stated at the mirroring itself,
    /// in <see cref="VRHand.SyncVisualOffset"/>.
    /// </summary>
    public static ConfigEntry<float>[]? StyleSeatRoll;

    /// <summary>
    /// Per-style YAW (degrees) around the hand's up axis — which way the fingers point.
    /// MIRRORED like <see cref="StyleSeatRoll"/>: the left hand gets the negated value, so a
    /// positive number turns both hands the same way relative to their own side of the body.
    /// </summary>
    public static ConfigEntry<float>[]? StyleSeatYaw;

    /// <summary>
    /// Per-style SPREAD (meters): how far apart the two hands sit. MIRRORED — positive moves the
    /// left hand left and the right hand right. Separate from <see cref="StyleSeatLateral"/> and
    /// not a replacement for it (lateral is UNMIRRORED, so no value of it can widen the pair) —
    /// see <see cref="VRHand.SyncVisualOffset"/>, where both terms are applied.
    /// </summary>
    public static ConfigEntry<float>[]? StyleSeatSpread;

    // ---- shipped per-style seat, indexed by (int)HandStyle: Glove, Plate, Arcane ------------
    // THESE ARE MEASURED, NOT DERIVED. They used to be seeded at runtime from the old shared
    // [Hands] seat keys (GripPitchOffsetDegrees etc.) plus a per-style trim — entries that were
    // retired and have since been deleted from Plugin, so a fresh install's hands were seated by
    // keys nobody could see or edit. The values below are the ones actually dialled in on
    // hardware, one hand style at a time, and they are now the shipped defaults outright.
    // Existing configs are untouched: BepInEx returns a saved value over a changed default.
    private static readonly float[] DefaultSeatPitch = { Defaults.GloveGripPitchDegrees, Defaults.PlateGripPitchDegrees, Defaults.ArcaneGripPitchDegrees };
    private static readonly float[] DefaultSeatLateral = { Defaults.GloveLateralOffset, Defaults.PlateLateralOffset, Defaults.ArcaneLateralOffset };
    private static readonly float[] DefaultSeatVertical = { Defaults.GloveVerticalOffset, Defaults.PlateVerticalOffset, Defaults.ArcaneVerticalOffset };
    private static readonly float[] DefaultSeatForward = { Defaults.GloveForwardOffset, Defaults.PlateForwardOffset, Defaults.ArcaneForwardOffset };

    // Roll/yaw/spread are part of the same dialled-in seat: they were 0 across the board while
    // the three axes did not exist yet, and carry the measured hardware pass now that they do.
    private static readonly float[] DefaultSeatRoll = { Defaults.GloveGripRollDegrees, Defaults.PlateGripRollDegrees, Defaults.ArcaneGripRollDegrees };
    private static readonly float[] DefaultSeatYaw = { Defaults.GloveGripYawDegrees, Defaults.PlateGripYawDegrees, Defaults.ArcaneGripYawDegrees };
    private static readonly float[] DefaultSeatSpread = { Defaults.GloveSpreadOffset, Defaults.PlateSpreadOffset, Defaults.ArcaneSpreadOffset };

    private static float Seat(float[] table, int style) => table[(int)HandStyles.Clamp(style)];

    /// <summary>
    /// The seat rotation AS SHIPPED for a style — the tuned values deliberately ignored.
    ///
    /// <para>Gesture measures use this instead of the live hand frame. The card fan's reveal gate
    /// reads a hand frame to decide "is the palm turned up", and while that frame carried the
    /// user's own pitch/roll/yaw, cosmetically re-seating the hands silently re-tuned the GESTURE:
    /// turn the hands 20 degrees to make them sit right on the controller and the fan starts
    /// opening at a different wrist angle. Anchoring on the shipped seat keeps "how far do I turn
    /// my wrist" a property of the CONTROLLER, which is what the player actually learns, and makes
    /// hand tuning purely cosmetic again.</para>
    /// </summary>
    public static Quaternion ShippedSeatRotation(int style) =>
        Quaternion.Euler(-Seat(DefaultSeatPitch, style), 0f, 0f);

    /// <summary>Active-style seat pitch (degrees; the shipped per-style value before Bind).</summary>
    public static float SeatPitchSafe(int style) =>
        StyleValue(StyleSeatPitch, style, Seat(DefaultSeatPitch, style));

    /// <summary>Active-style lateral (X) seat offset (meters; shipped per-style value before Bind).</summary>
    public static float SeatLateralSafe(int style) =>
        StyleValue(StyleSeatLateral, style, Seat(DefaultSeatLateral, style));

    /// <summary>Active-style vertical (Y) seat offset (meters; shipped per-style value before Bind).</summary>
    public static float SeatVerticalSafe(int style) =>
        StyleValue(StyleSeatVertical, style, Seat(DefaultSeatVertical, style));

    /// <summary>Active-style forward (Z) seat offset (meters; shipped per-style value before Bind).</summary>
    public static float SeatForwardSafe(int style) =>
        StyleValue(StyleSeatForward, style, Seat(DefaultSeatForward, style));

    /// <summary>Active-style seat ROLL (degrees, unmirrored; VRHand negates it for the left hand).</summary>
    public static float SeatRollSafe(int style) =>
        StyleValue(StyleSeatRoll, style, Seat(DefaultSeatRoll, style));

    /// <summary>Active-style seat YAW (degrees, unmirrored; VRHand negates it for the left hand).</summary>
    public static float SeatYawSafe(int style) =>
        StyleValue(StyleSeatYaw, style, Seat(DefaultSeatYaw, style));

    /// <summary>Active-style hand SPREAD (meters, unmirrored; VRHand negates it for the left hand).</summary>
    public static float SeatSpreadSafe(int style) =>
        StyleValue(StyleSeatSpread, style, Seat(DefaultSeatSpread, style));

    // ---- per-STYLE wrist-HUD pose (2026-07 request B) --------------------------------------
    // The wrist HUD (WorldUI.WristHud) hovers over the hand MESH, whose thickness/shape differs
    // per hand style — so its pose (tilt + offset from the wrist anchor) is PER STYLE too,
    // exactly like the seat controls above. Section [WristHud] of this cfg file.
    // WristHud.ApplyPose re-reads the ACTIVE style every Tick, so edits AND style switches
    // live-apply. The HUD's on/off toggle stays GLOBAL ([WorldUI] WristHud) — enabling it is
    // not a geometry preference.
    //
    // KEYS RENAMED TO *Palm* (2026-08-09). The HUD was turned around onto the PALM side at the
    // user's request, which meant replacing the base rotation it is trimmed against — and every
    // saved trim was the correction for the OLD base (a ~-90° pitch that undid the wrist
    // anchor's own +90°, plus a -180° yaw). Keeping the key names would have let those saved
    // numbers ride silently into the new base and land the plate back on the knuckles: two
    // rotations cancelling, the exact failure the request warned about. A trim against a
    // different base is a different setting, so it gets a different name and the shipped
    // default (0°, WristHud's PalmFlat IS the wanted orientation) applies to everyone.

    /// <summary>Per-style wrist-HUD pitch (degrees) on top of the palm base.</summary>
    public static ConfigEntry<float>[]? StyleWristPitch;

    /// <summary>Per-style wrist-HUD yaw (degrees).</summary>
    public static ConfigEntry<float>[]? StyleWristYaw;

    /// <summary>Per-style wrist-HUD roll (degrees).</summary>
    public static ConfigEntry<float>[]? StyleWristRoll;

    /// <summary>Per-style wrist-HUD offset across the hand (wrist +X, meters).</summary>
    public static ConfigEntry<float>[]? StyleWristOffsetX;

    /// <summary>Per-style wrist-HUD offset toward the fingers (wrist +Y, meters).</summary>
    public static ConfigEntry<float>[]? StyleWristOffsetY;

    /// <summary>Per-style wrist-HUD offset out of the palm (wrist +Z, meters).</summary>
    public static ConfigEntry<float>[]? StyleWristOffsetZ;

    // WHY THE THREE OFFSET KEYS END IN THE WORD "Offset" AND NOT IN AN AXIS LETTER — this is the
    // fix for "Der X-Offset beim Arm-HUD hat keinen Einfluss, alle anderen Werte und Offsets
    // funktionieren" (2026-08-09), and the dial was never unwired: it was unMOVABLE.
    //
    // One ◀/▶ press is derived per entry (ConfigCatalog.ResolveStep) in falling order: a step
    // written down for it, else the unit named in its KEY, else a fiftieth of the declared range,
    // else a fiftieth of the shipped default's MAGNITUDE. ConfigSteps matches its unit words as a
    // SUFFIX ("Meters", "Offset", "Degrees", …), and the old keys were {Style}OffsetX/Y/Z — the
    // axis letter sat between the unit word and the end of the name, so the test never saw
    // "Offset" and all three fell through to the magnitude rule. That rule scales the step to the
    // DIAL, and X shipped the smallest default of the three (-0.003 for the glove against Y's
    // -0.053): 0.003/50 -> 0.00005, i.e. one press moved the HUD by a twentieth of a millimetre
    // while Y moved by one. ConfigSteps' own remarks record the same defect for the per-board
    // asset rotation, where a trailing "_Oak" hid the unit word exactly this way.
    //
    // Naming the DIRECTION rather than the axis fixes the step (all three now end in "Offset", so
    // the unit rule gives them a centimetre, bounded below by their own scale — 1 cm for the
    // centred side offset, 5 mm for the other two) and fixes the caption at the same time: "X"
    // meant the wrist ANCHOR's X, which is not a frame anyone can guess from a letter. STILL OPEN,
    // reported rather than fixed here: any OTHER key in the project ending in a bare axis letter
    // still misses its unit word, and TryUnit is shared with dials this round must not re-step.

    // ---- shipped per-style wrist-HUD pose, indexed by (int)HandStyle: Glove, Plate, Arcane ----
    // The per-style ARRAYS earn their keep: the three meshes have different bulk under the plate,
    // a player who trims one style must not move the other two — and the 2026-08-09 hardware pass
    // did dial them apart. The glove wears the plate close (5 cm down the arm, 2 cm clear, no
    // pitch); plate and arcane push it 13 cm down the forearm and 7.5 cm out and tilt it 32°. The
    // values themselves, and the warning not to re-express them, live in Defaults.Hands.cs.
    private static readonly float[] DefaultWristPitch = { Defaults.GlovePalmPitch, Defaults.PlatePalmPitch, Defaults.ArcanePalmPitch };
    private static readonly float[] DefaultWristYaw = { Defaults.GlovePalmYaw, Defaults.PlatePalmYaw, Defaults.ArcanePalmYaw };
    private static readonly float[] DefaultWristRoll = { Defaults.GlovePalmRoll, Defaults.PlatePalmRoll, Defaults.ArcanePalmRoll };
    private static readonly float[] DefaultWristOffsetX = { Defaults.GlovePalmSideOffset, Defaults.PlatePalmSideOffset, Defaults.ArcanePalmSideOffset };
    private static readonly float[] DefaultWristOffsetY = { Defaults.GlovePalmFingerOffset, Defaults.PlatePalmFingerOffset, Defaults.ArcanePalmFingerOffset };
    private static readonly float[] DefaultWristOffsetZ = { Defaults.GlovePalmLiftOffset, Defaults.PlatePalmLiftOffset, Defaults.ArcanePalmLiftOffset };

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


    public static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("hands");

        // THE RANGES IN THIS METHOD ARE THE CLAMPS THE CODE ALREADY APPLIES, declared (2026-08-22
        // settings audit, user: "Prüfe für jede Einstellung die Bedienmöglichkeit, nicht jedes
        // Feld macht sinn mit einer verschiebaren Bar besonders wenn man bis auf die Kommastellen
        // etwas anpassen will."). Each of these four entries was an UNBOUNDED stepper whose value
        // is clamped where it is read, a few lines up in this same file — so past the clamp the
        // arrows kept moving the number on screen and nothing on the hand, which is exactly the
        // "hat keinen Einfluss" report shape. Declaring the clamp changes no live behaviour (every
        // value went through it anyway) and it makes the row honest.
        GlovePinkyCounterAbduction = config.Bind(
            "Hands", "GlovePinkyCounterAbduction", Defaults.GlovePinkyCounterAbduction,
            new ConfigDescription(
                "GLOVE style only: degrees of counter-abduction (rotation about the pinky root's " +
                "local Z = palm normal) applied at full curl, scaled by the curl value. The glove " +
                "pinky MESH tube leans ~18° outward while its bone chain is straight, so a pure " +
                "local-X fist leaves the curled pinky visibly splayed outward; this pulls it back " +
                "toward the ring finger as it curls (sign auto-flips for the right hand). 0 disables. " +
                "Live-tunable. Range -30..30, the clamp GlovePinkyCounterAbductionSafe applies.",
                new AcceptableValueRange<float>(-30f, 30f)));

        // Ghost hand (see the field docs above). Bound BEFORE the seat block so a fresh cfg
        // file groups the three ghost keys next to the other [Hands] visual toggles.
        GhostHandOnFan = config.Bind(
            "Hands", "GhostHandOnFan", Defaults.GhostHandOnFan,
            "Make the hand that currently holds the OPEN card fan semi-transparent (\"ghost " +
            "hand\") so the hand mesh stops covering card details. The hand stays visible — " +
            "only its opacity drops (strength: GhostHandStrength). OFF by default; nothing " +
            "about the hands changes until you enable it. Live-tunable, fully reversible (the " +
            "fade runs on private per-renderer material copies, never on the shared hand " +
            "materials), and carried to the avatar mirror and to other players' view of you.");
        GhostHandOnHeldCard = config.Bind(
            "Hands", "GhostHandOnHeldCard", Defaults.GhostHandOnHeldCard,
            "ALSO make a hand semi-transparent while it HOLDS a card — the fingers wrap exactly " +
            "the art you lifted the card to read. Independent of GhostHandOnFan: either can be " +
            "on without the other, and both hands can ghost at once (a card in each). Shares " +
            "GhostHandStrength, is live-tunable, fully reversible, and synchronized — other " +
            "players see your hands exactly as you do.");
        HandsDisturbScenery = config.Bind(
            "Hands", "HandsDisturbScenery", Defaults.HandsDisturbScenery,
            "Reach through a curtain and it MOVES. Any simulated cloth the room already has - " +
            "curtains, hangings - is pushed aside by your hand while it is inside it, with either " +
            "hand, whether you are holding something or not. The cloth's own authored colliders " +
            "are kept and everything is restored exactly when your hand leaves. Purely local and " +
            "purely cosmetic: cloth has never been networked, so every player's own hands stir " +
            "their own copy of the room and nothing can desync. Off: the scenery hangs still, " +
            "exactly as before. (A held figure's cape is a separate switch, [FigureGrab] " +
            "ClothFollowsFreeHand.)");
        HandsDisturbVfx = config.Bind(
            "Hands", "HandsDisturbVfx", Defaults.HandsDisturbVfx,
            "Put a hand in the smoke and the smoke goes ROUND it. The scenario's own effects - " +
            "fire, smoke, embers, dust, the wash of a spell - deflect off your hands while they " +
            "are inside, and are restored to the authored settings the moment you leave. The " +
            "reaction is purely kinematic: particles bounce, slow a little and die slightly " +
            "early. Nothing is ever spawned - effects the game manages its own collision on are " +
            "skipped entirely, and collision MESSAGES stay off, so no impact effect can be " +
            "triggered by your hand. Purely local and purely cosmetic: particles have never been " +
            "networked, so every player stirs their own copy of the room and nothing can desync. " +
            "Costs a little frame time while a hand is actually inside an effect (particle " +
            "collision is tested per particle), and never anything when it is not. Off: effects " +
            "pass through your hands, exactly as before.");
        GhostHandStrength = config.Bind(
            "Hands", "GhostHandStrength", Defaults.GhostHandStrength,
            new ConfigDescription(
                "Ghost-hand transparency STRENGTH while the card fan is open: 0 = fully solid, " +
                "1 = fully invisible (material alpha = 1 - strength). Clamped so the hand never " +
                "disappears completely — you still need to see where your fingers are to grab a " +
                "card. Live-tunable; the same strength is sent to other players so your ghost " +
                "hand looks identical on their screens.",
                new AcceptableValueRange<float>(HandGhosts.MinStrength, HandGhosts.MaxStrength)));

        TestFist = config.Bind(
            "Hands", "TestFist", Defaults.TestFist,
            "DEBUG: force a FULL fist (curl 1.0 on all five fingers of both hands) regardless " +
            "of controller input. Toggle this on to see the maximum fist the current rig can " +
            "produce — if this fist looks right but squeezing the controller does not close the " +
            "hand, the loss is in the INPUT (grip value never reaching full range); if even " +
            "this fist stays open, the loss is in the rig/angles (raise CurlProximal/CurlMiddle/CurlTip).");
        // The three joint angles share ONE range, 0..130 — the clamp FingerMaxAnglesSafe applies
        // to all three (HandsConfig.cs, ~line 100). They must also share a STEP, and now do: with
        // the range declared all three derive 2° a press instead of 2/2/1, which is the
        // "two dials of one family must move together" rule ConfigSteps was written for.
        CurlProximal = config.Bind(
            "Hands", "CurlProximal", Defaults.CurlProximal,
            new ConfigDescription(
                "Full-curl rotation (degrees, local X) of each finger's PROXIMAL (root/knuckle) " +
                "joint at curl 1.0. The thumb's proximal angle scales proportionally " +
                "(default thumb 25 at finger 75). Live-tunable. Range 0-130.",
                new AcceptableValueRange<float>(0f, 130f)));
        CurlMiddle = config.Bind(
            "Hands", "CurlMiddle", Defaults.CurlMiddle,
            new ConfigDescription(
                "Full-curl rotation (degrees, local X) of each finger's MIDDLE joint at curl 1.0. " +
                "The thumb's middle angle scales proportionally (default thumb 45 at finger 95). " +
                "Live-tunable. Range 0-130.",
                new AcceptableValueRange<float>(0f, 130f)));
        CurlTip = config.Bind(
            "Hands", "CurlTip", Defaults.CurlTip,
            new ConfigDescription(
                "Full-curl rotation (degrees, local X) of each finger's TIP (distal) joint at " +
                "curl 1.0. The thumb's tip angle scales proportionally (default thumb 60 at " +
                "finger 65). Live-tunable. Range 0-130.",
                new AcceptableValueRange<float>(0f, 130f)));

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
        // The range is the clamp CurlInputFullAtSafe applies (0.3..1) — see the block comment at
        // GlovePinkyCounterAbduction. This one is a CURATED row ("Vollgriff-Hilfe") and an
        // ACCESSIBILITY control, so an endless stepper past the clamp was the worst version of
        // the fault: the player whose grip does not reach full range is exactly the player who
        // keeps pressing.
        CurlInputFullAt = config.Bind(
            "Hands", "CurlInputFullAt", Defaults.CurlInputFullAt,
            new ConfigDescription(
                "Raw analog grip/trigger value (0.3-1.0) that already counts as a FULL curl: " +
                "curl = raw / this, clamped to 1. Quest 3 controllers via Virtual Desktop often " +
                "plateau the analog grip below 1.0 at a comfortable full squeeze — lower this if " +
                "the '[Hands] squeeze released: peak grip=...' log line shows your peak never " +
                "reaches 1.0. Set 1.0 for the raw, unremapped input.",
                new AcceptableValueRange<float>(0.3f, 1f)));

        // PER-STYLE seat controls: one absolute pitch/roll/yaw/X/Y/Z/spread set per hand style,
        // defaulting to the hardware-measured DefaultSeat* tables above (why those are literal
        // tables rather than seeded from the retired shared globals: the block above them).
        string[] styleNames = { "Glove", "Plate", "Arcane" }; // index == (int)HandStyle
        StyleSeatPitch = new ConfigEntry<float>[HandStyles.Count];
        StyleSeatLateral = new ConfigEntry<float>[HandStyles.Count];
        StyleSeatVertical = new ConfigEntry<float>[HandStyles.Count];
        StyleSeatForward = new ConfigEntry<float>[HandStyles.Count];
        StyleSeatRoll = new ConfigEntry<float>[HandStyles.Count];
        StyleSeatYaw = new ConfigEntry<float>[HandStyles.Count];
        StyleSeatSpread = new ConfigEntry<float>[HandStyles.Count];
        for (int i = 0; i < HandStyles.Count; i++)
        {
            string s = styleNames[i];
            StyleSeatPitch[i] = config.Bind(
                "Hands", $"{s}GripPitchDegrees",
                Seat(DefaultSeatPitch, i),
                $"Pitch (degrees) between the tracked OpenXR grip pose and the visual hand while " +
                $"the {s} style is worn — NEGATIVE tilts the fingertips DOWN. PER-STYLE absolute " +
                "value (the old shared GripPitchOffsetDegrees + trim are retired). Live-tunable — the hands re-seat next frame.");
            StyleSeatLateral[i] = config.Bind(
                "Hands", $"{s}LateralOffset",
                Seat(DefaultSeatLateral, i),
                $"Lateral offset (meters, device-space X) of the visual hand from the grip pose " +
                $"while the {s} style is worn. NOT MIRRORED: both hands move the same way, which " +
                "is what you want when the pair sits off-centre on the controllers — to move them " +
                $"APART use {s}SpreadOffset. PER-STYLE absolute value. Other players see it, and " +
                "so do figures and cards held in the hand. Live-tunable.");
            StyleSeatVertical[i] = config.Bind(
                "Hands", $"{s}VerticalOffset",
                Seat(DefaultSeatVertical, i),
                $"Vertical offset (meters, device-space Y; POSITIVE = up) of the visual hand from " +
                $"the grip pose while the {s} style is worn. PER-STYLE absolute value (supersedes " +
                "the old shared HandVerticalOffset + trim; seeded on first run). Live-tunable.");
            StyleSeatForward[i] = config.Bind(
                "Hands", $"{s}ForwardOffset",
                Seat(DefaultSeatForward, i),
                $"Forward/depth offset (meters, device-space Z; POSITIVE = toward the fingertips) " +
                $"of the visual hand from the grip pose while the {s} style is worn. PER-STYLE " +
                "absolute value (supersedes the old shared HandForwardOffset + trim; seeded on " +
                "first run). Live-tunable.");
            StyleSeatRoll[i] = config.Bind(
                "Hands", $"{s}GripRollDegrees",
                Seat(DefaultSeatRoll, i),
                $"Roll (degrees) of the visual hand around the controller's forward axis while the " +
                $"{s} style is worn — the twist that makes a hand look rotated ON the controller " +
                "rather than seated on it. MIRRORED BETWEEN THE HANDS: the two controllers report " +
                "the same handedness of local axes, so one value applied to both would twist them " +
                "the same way in world terms and the pair would stop being symmetric. The left " +
                "hand therefore gets the negated value, and a positive number turns both palms the " +
                "same way relative to their own side of your body. OTHER PLAYERS SEE IT: the pose " +
                "on the wire is the VISIBLE hand (the sampler reads the seated hand root), and " +
                "figures and cards held in the hand hang off that same root, so they follow too. " +
                "Live-tunable — the hands re-seat next frame.");
            StyleSeatYaw[i] = config.Bind(
                "Hands", $"{s}GripYawDegrees",
                Seat(DefaultSeatYaw, i),
                $"Yaw (degrees) of the visual hand around its up axis while the {s} style is worn — " +
                "which way the fingers point. MIRRORED BETWEEN THE HANDS exactly like " +
                $"{s}GripRollDegrees: the left hand gets the negated value, so a positive number " +
                "turns both hands the same way relative to their own side of your body. Other " +
                "players see it, and so do figures and cards held in the hand. Live-tunable.");
            StyleSeatSpread[i] = config.Bind(
                "Hands", $"{s}SpreadOffset",
                Seat(DefaultSeatSpread, i),
                $"How far APART the two hands sit (meters) while the {s} style is worn: POSITIVE " +
                "moves the left hand left and the right hand right, negative brings them together. " +
                $"MIRRORED, which is what distinguishes it from {s}LateralOffset — that one shifts " +
                "both hands the same way in device space (the pair moves together), and no value " +
                "of it can widen the pair. Other players see it, and so do figures and cards held " +
                "in the hand. Live-tunable.");
        }
        // PER-STYLE wrist-HUD pose (request B): each style ships the pose measured for it
        // (DefaultWrist* above). WorldUIConfig.Bind stays called here — it is idempotent, and
        // HandsModule inits before WorldUIModule, so this keeps the [WorldUI] entries bound
        // from the same point in the sequence they always were.
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
            string per = $"PER-STYLE absolute value while the {s} hand style is worn. " +
                "Live-tunable — WristHud re-applies every tick.";
            // The three TRIMS sit on top of the palm base (WristHud.PalmFlat), which already IS
            // the shipped orientation — so 0 means "as shipped" and the numbers stay small and
            // readable. No declared range on any of these six: a bounded scalar is rendered as a
            // SLIDER (VROptionsTab.2.Rows.cs), and a bar you drag is the wrong instrument for a
            // millimetre trim on your own wrist. They stay steppers.
            StyleWristPitch[i] = config.Bind(
                "WristHud", $"{s}PalmPitch", Seat(DefaultWristPitch, i),
                $"Wrist HUD tilt (pitch, degrees) on top of the palm base — 0 is the shipped " +
                $"orientation, flat on the palm and read by turning your palm up. {per}");
            StyleWristYaw[i] = config.Bind(
                "WristHud", $"{s}PalmYaw", Seat(DefaultWristYaw, i),
                $"Wrist HUD yaw (degrees) on top of the palm base; 0 is the shipped orientation. {per}");
            StyleWristRoll[i] = config.Bind(
                "WristHud", $"{s}PalmRoll", Seat(DefaultWristRoll, i),
                $"Wrist HUD roll (degrees) on top of the palm base; 0 is the shipped orientation. {per}");
            // The three OFFSETS are stated in the WRIST ANCHOR's frame, measured off the shipped
            // prefabs (see WristHud.Build): across the hand, toward the fingers, out of the palm.
            // Their names end in the unit word on purpose — see the block above the arrays.
            StyleWristOffsetX[i] = config.Bind(
                "WristHud", $"{s}PalmSideOffset", Seat(DefaultWristOffsetX, i),
                $"Wrist HUD offset ACROSS the hand, real meters (wrist +X — toward the little " +
                $"finger on the right hand, the thumb on the left). {per}");
            StyleWristOffsetY[i] = config.Bind(
                "WristHud", $"{s}PalmFingerOffset", Seat(DefaultWristOffsetY, i),
                $"Wrist HUD offset toward the FINGERS, real meters (wrist +Y; negative slides it " +
                $"back down the forearm, which is where it ships). {per}");
            StyleWristOffsetZ[i] = config.Bind(
                "WristHud", $"{s}PalmLiftOffset", Seat(DefaultWristOffsetZ, i),
                $"Wrist HUD offset OUT OF THE PALM, real meters (wrist +Z — how far clear of the " +
                $"hand the plate floats). {per}");
        }

        VRLog.Info("Hands", "[Hands] Per-style seat controls bound: " +
            string.Join("; ", System.Array.ConvertAll(styleNames, n =>
            {
                int i = System.Array.IndexOf(styleNames, n);
                return $"{n} pitch {StyleSeatPitch[i].Value:0.#}° X/Y/Z " +
                       $"{StyleSeatLateral[i].Value * 1000f:0}/{StyleSeatVertical[i].Value * 1000f:0}/" +
                       $"{StyleSeatForward[i].Value * 1000f:0} mm";
            })) + " (first run seeds from the old global seat + trims).");

        // Ghost hand: the flip itself is worth one log line (the per-hand engage/release lines
        // with the renderer counts come from HandGhost, but only once a fan actually opens).
        GhostHandOnFan.SettingChanged += (_, _) =>
            VRLog.Info("Hands", $"[Hands] GhostHandOnFan is now {(GhostHandOnFan.Value ? "ON" : "OFF")} " +
                                $"(strength {GhostHandStrength.Value * 100f:0}% ⇒ alpha " +
                                $"{HandGhosts.AlphaFor(GhostHandStrength.Value):0.00}) — the hand carrying " +
                                "the open card fan " +
                                (GhostHandOnFan.Value ? "fades to a ghost hand." : "stays solid."));

        // The toggle is the user's A/B lever during hardware tests — make every flip
        // land in the log so the session record shows WHICH fist was being judged.
        TestFist.SettingChanged += (_, _) =>
            VRLog.Info("Hands", $"[Hands] TestFist is now {(TestFist.Value ? "ON — forcing curl 1.0 on all fingers of both hands" : "OFF — controller input drives the fingers again")}.");
    }
}
