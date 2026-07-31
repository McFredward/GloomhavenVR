using System;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// [FigureGrab] config section (P8). Bound against a module-owned config file
/// (<c>BepInEx/config/dev.gloomhavenvr.figuregrab.cfg</c>) via
/// <see cref="ModuleConfig.Create"/> — self-contained, so this worker never has to edit
/// the shared <see cref="BoardConfig"/>. Feature toggle plus the in-hand pose tunables
/// (held scale / offset / tilt) that need a hardware pass to feel right.
///
/// PER HAND STYLE (2026-07 request A): the held mini is docked between thumb and index of
/// the hand MESH, whose geometry differs per style (Glove/Plate/Arcane) — so every held-pose
/// tunable (offset X/Y/Z, tilt, face yaw, scale) is stored PER STYLE (the <c>Style*</c>
/// arrays, exactly the HandsConfig per-style seat pattern): each style is SEEDED on first
/// bind from the legacy global entry in this same file (BepInEx returns saved over default,
/// so a tuned pose carries over to all three styles instead of resetting; afterwards the
/// saved per-style value always wins). The <c>Active*</c> accessors read the ACTIVE style
/// ([Hands] HandStyle) live, and both a value edit AND a style switch re-pose the currently
/// held mini immediately (SettingChanged → <see cref="FigureGrabbable.ReapplyAll"/>). The
/// legacy global entries stay bound as harmless orphans (pre-Bind fallback + seed source).
/// <see cref="HeldUpright"/> is a MODE, not geometry — it stays global.
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
    /// used (<see cref="HeldPalmRotation"/> relative to the hand, lays it flat).
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

    // ---- per-STYLE held pose (request A; indexed by (int)HandStyle: Glove/Plate/Arcane) ----

    /// <summary>Per-style held lateral offset (GrabAnchor-local X, meters).</summary>
    public static ConfigEntry<float>[]? StyleHeldOffsetSide;

    /// <summary>Per-style held offset out of the palm (GrabAnchor-local Y, meters).</summary>
    public static ConfigEntry<float>[]? StyleHeldOffsetUp;

    /// <summary>Per-style held offset toward the fingertips (GrabAnchor-local Z, meters).</summary>
    public static ConfigEntry<float>[]? StyleHeldOffsetForward;

    /// <summary>Per-style held tilt (degrees).</summary>
    public static ConfigEntry<float>[]? StyleHeldTiltDegrees;

    /// <summary>Per-style upright-mode face yaw (degrees).</summary>
    public static ConfigEntry<float>[]? StyleHeldFaceYawDegrees;

    /// <summary>Per-style inspection zoom on top of the board world-scale.</summary>
    public static ConfigEntry<float>[]? StyleHeldScale;

    /// <summary>Per-style held ROLL (degrees) — superseded by <see cref="StyleHeldRotRoll"/>.</summary>
    public static ConfigEntry<float>[]? StyleHeldRollDegrees;

    // The rotation trio, renamed so the three sit TOGETHER in an alphabetically sorted menu
    // (HeldRotPitch / HeldRotRoll / HeldRotYaw) instead of scattered between the offsets, the
    // scale and the upright switch — "Tilt" and "FaceYaw" also never said which axis they were.
    // Each is seeded from the value its predecessor held, so nothing anyone tuned is lost.

    /// <summary>Per-style PITCH (degrees): tips the mini forward/back. Was HeldTiltDegrees.</summary>
    public static ConfigEntry<float>[]? StyleHeldRotPitch;

    /// <summary>Per-style YAW (degrees): spins the mini about its OWN up axis. Was HeldFaceYawDegrees.</summary>
    public static ConfigEntry<float>[]? StyleHeldRotYaw;

    /// <summary>Per-style ROLL (degrees): spins the mini about its OWN forward axis. Was HeldRollDegrees.</summary>
    public static ConfigEntry<float>[]? StyleHeldRotRoll;

    /// <summary>The ACTIVE style's per-style value, else the legacy global, else the shipped default.</summary>
    private static float StyleOr(ConfigEntry<float>[]? entries, ConfigEntry<float>? legacy, float shipped)
    {
        try
        {
            if (entries != null)
                return entries[Hands.HandsConfig.ActiveStyleIndex].Value;
            return legacy != null ? legacy.Value : shipped;
        }
        catch
        {
            return shipped;
        }
    }

    internal static float ActiveHeldSide => StyleOr(StyleHeldOffsetSide, HeldOffsetSide, 0f);
    internal static float ActiveHeldUp => StyleOr(StyleHeldOffsetUp, HeldOffsetUp, 0.03f);
    internal static float ActiveHeldForward => StyleOr(StyleHeldOffsetForward, HeldOffsetForward, 0.03f);
    internal static float ActiveHeldTilt => StyleOr(StyleHeldRotPitch, null, StyleOr(StyleHeldTiltDegrees, HeldTiltDegrees, 0f));
    internal static float ActiveHeldFaceYaw => StyleOr(StyleHeldRotYaw, null, StyleOr(StyleHeldFaceYawDegrees, HeldFaceYawDegrees, 0f));
    internal static float ActiveHeldRoll => StyleOr(StyleHeldRotRoll, null, StyleOr(StyleHeldRollDegrees, null, 0f));

    /// <summary>Active-style inspection zoom (what <see cref="FigureGrabbable"/> applies).</summary>
    internal static float ActiveHeldScale => StyleOr(StyleHeldScale, HeldScale, 1.5f);

    /// <summary>
    /// GrabAnchor-local held position for the RIGHT hand — the canonical pose the debug
    /// steppers tune (ACTIVE hand style). The LEFT hand derives from it by mirroring
    /// (see <see cref="HeldOffsetFor"/>).
    /// </summary>
    internal static Vector3 HeldOffset => new(ActiveHeldSide, ActiveHeldUp, ActiveHeldForward);

    /// <summary>
    /// The GrabAnchor-local held offset for a given hand. The tuned values are canonical for
    /// the RIGHT hand; the LEFT hand is the MIRROR IMAGE across the hand-frame's left-right (X)
    /// axis, so the mini sits in the left hand exactly as it does in the right. Only the lateral
    /// component (<see cref="HeldOffsetSide"/>, local X) flips sign; forward (Z) and up (Y) are
    /// unchanged. The hand rig frame is NOT mirrored between hands (mesh mirrored, frame shared —
    /// see HandRig), so without this flip the same local X put the mini on the same frame-side of
    /// both hands (anatomically opposite) — this negation restores the true mirror.
    /// </summary>
    internal static Vector3 HeldOffsetFor(HandSide side)
    {
        float sideSign = side == HandSide.Left ? -1f : 1f;
        return new Vector3(sideSign * ActiveHeldSide, ActiveHeldUp, ActiveHeldForward);
    }

    /// <summary>
    /// The inspection yaw for a given hand. Mirroring a rotation across the hand-frame's
    /// left-right (X) plane negates the YAW (and any ROLL) while leaving the TILT (pitch about X)
    /// untouched — so the LEFT hand yaws the mini's readable front the opposite way, the mirror of
    /// the tuned RIGHT-hand yaw. (Roll is always 0 here, so only the yaw needs the flip.)
    /// </summary>
    internal static float HeldFaceYawFor(HandSide side)
        => side == HandSide.Left ? -ActiveHeldFaceYaw : ActiveHeldFaceYaw;

    /// <summary>
    /// Held ROLL for one hand — the third rotation axis, so a mini has the same freedom in the
    /// hand as the hand itself: three offsets and three angles.
    ///
    /// <para>Mirrored exactly like the yaw, and for the same reason: roll is a rotation about the
    /// forward axis, so the mirror image of a right-hand roll is the opposite roll. Tune once on
    /// the right hand and the left is correct. (Tilt stays mirror-invariant — it is a rotation
    /// about the mirror axis itself.)</para>
    /// </summary>
    internal static float HeldRollFor(HandSide side)
        => side == HandSide.Left ? -ActiveHeldRoll : ActiveHeldRoll;

    /// <summary>
    /// Issue A — the upright held orientation as a FIXED CONSTANT rotation RELATIVE TO THE
    /// GRABANCHOR (NOT derived from world up, the player's head, the figure's board rotation,
    /// or the grab-moment anchor orientation), so the mini sits the SAME way in the palm no
    /// matter the approach angle and RIDES THE HAND (turn the hand → it turns with it).
    ///
    /// The mod's GrabAnchor is authored so its +Y points OUT OF THE PALM (see
    /// <c>HandVisuals</c>: the palm anchor's +Y is the palm normal, GrabAnchor inherits it), and
    /// the board mini's model local +Y is its up axis — so an IDENTITY base already stands the
    /// mini upright out of the palm, exactly the good "perfect horizontal grab" hold (palm up →
    /// mini up). Because it is anchor-LOCAL, grabbing from directly above (palm down) makes the
    /// mini extend OUT of the palm (harmlessly downward) instead of standing world-upright and
    /// clipping INTO the hand — the whole bug. <see cref="HeldTiltDegrees"/> then tips it toward
    /// the face and the MIRROR-CORRECT <see cref="HeldFaceYawFor"/> spins the readable front
    /// toward the player (negated for the left hand, tilt mirror-invariant), both live-tunable.
    /// </summary>
    /// <remarks>
    /// ORDER MATTERS, and a single Quaternion.Euler(tilt, yaw, roll) gets it wrong. Unity composes
    /// that as Ry * Rx * Rz, so the YAW is applied LAST and therefore about the HAND's up axis.
    /// Once the mini is tilted, spinning about the hand's up axis no longer spins the mini — it
    /// TIPS it, which is exactly the complaint: yaw on a figure you have just stood upright should
    /// turn it and nothing else. So the yaw goes FIRST, in the mini's own frame, and tilt/roll are
    /// applied on top: R = tiltRoll * yaw. Upright (tilt 0) that is an ordinary spin about the
    /// mini's axis; tilted, it is still a pure spin, just seen tipped.
    /// </remarks>
    internal static Quaternion HeldUprightRotation(HandSide side)
        => Quaternion.Euler(ActiveHeldTilt, 0f, HeldRollFor(side))
           * Quaternion.Euler(0f, HeldFaceYawFor(side), 0f);

    /// <summary>
    /// The palm pose: tilt only, as it always was.
    ///
    /// <para>It deliberately does NOT take the yaw and the roll. Giving it all three (which I did
    /// once) made the two poses byte-for-byte identical and quietly turned the "hold upright"
    /// switch into a setting that does nothing — the yaw was the only thing it ever selected.
    /// The switch keeps its meaning; the extra axes live in the upright pose, which is the one
    /// you inspect a mini in.</para>
    ///
    /// </summary>
    internal static Quaternion HeldPalmRotation() => Quaternion.Euler(ActiveHeldTilt, 0f, 0f);

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
        // The six entries below are LEGACY (see the per-STYLE block further down, which
        // superseded them): each is read exactly once, as the bind DEFAULT that seeds its
        // three per-style successors the first time this cfg file is written, and never
        // again — StyleOr() prefers the per-style array whenever it exists, which is always
        // after Bind() has run. They stay bound because unbinding drops the keys from every
        // existing dev.gloomhavenvr.figuregrab.cfg (CHARTER §5). HeldUpright is NOT legacy:
        // it is a MODE, not geometry, and stayed deliberately global.
        const string legacyTail =
            "This entry is read once, as the seed for those per-style keys the first time they " +
            "are created, and never again. Kept bound so existing config files keep loading. " +
            "Historical meaning: ";
        HeldScale = config.Bind(
            "FigureGrab", "HeldScale", 1.5f,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldScale. " +
            legacyTail +
            "inspection zoom applied on top of the figure's board world-scale while held " +
            "(1 = board size in your hand; higher enlarges it).");
        HeldOffsetForward = config.Bind(
            "FigureGrab", "HeldOffsetForward", 0.03f,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldOffsetForward. " +
            legacyTail +
            "held position offset toward the fingertips (grab-anchor local Z) — moves the mini " +
            "out to the thumb–index pinch point.");
        HeldOffsetUp = config.Bind(
            "FigureGrab", "HeldOffsetUp", 0.03f,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldOffsetUp. " +
            legacyTail +
            "held position offset out of the palm (grab-anchor local Y).");
        HeldOffsetSide = config.Bind(
            "FigureGrab", "HeldOffsetSide", 0f,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldOffsetSide. " +
            legacyTail +
            "held lateral position offset (grab-anchor local X) toward the thumb–index pinch.");
        HeldUpright = config.Bind(
            "FigureGrab", "HeldUpright", true,
            "Hold the mini UPRIGHT (standing, pointing up) pinched between thumb and index and " +
            "facing you, like inspecting a chess piece. False = legacy flat-on-palm pose. " +
            "LIVE (unlike the other Held* entries in this section): this is a mode, not " +
            "geometry, so it stayed global instead of going per hand style.");
        HeldTiltDegrees = config.Bind(
            "FigureGrab", "HeldTiltDegrees", 0f,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldTiltDegrees. " +
            legacyTail +
            "held tilt (degrees) — tip the mini toward your face for inspection.");
        HeldFaceYawDegrees = config.Bind(
            "FigureGrab", "HeldFaceYawDegrees", 0f,
            "LEGACY — no effect, superseded by [FigureGrab] Glove/Plate/ArcaneHeldFaceYawDegrees. " +
            legacyTail +
            "upright mode only: extra yaw (degrees) to spin the mini's front toward you. Set 180 " +
            "if it faces away.");

        // PER-STYLE held pose (request A): one absolute offset/tilt/yaw/scale set per hand
        // style. The BIND DEFAULT of each entry is the legacy global entry's CURRENT value
        // (bound just above in this same file — saved wins over shipped default), so a first
        // run SEEDS every style with the user's tuned pose; any later run keeps the saved
        // per-style value. No marker entry needed: the seed is only consulted while the key
        // is absent from the cfg. HeldUpright stays global (a mode, not geometry).
        string[] styleNames = { "Glove", "Plate", "Arcane" }; // index == (int)HandStyle
        StyleHeldOffsetSide = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldOffsetUp = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldOffsetForward = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldTiltDegrees = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldFaceYawDegrees = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldScale = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldRollDegrees = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldRotPitch = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldRotYaw = new ConfigEntry<float>[HandStyles.Count];
        StyleHeldRotRoll = new ConfigEntry<float>[HandStyles.Count];
        for (int i = 0; i < HandStyles.Count; i++)
        {
            string s = styleNames[i];
            string per = $"PER-STYLE absolute value while the {s} hand style is worn " +
                "(supersedes the shared legacy entry it was seeded from on first run). " +
                "Live-tunable — a held mini re-poses immediately.";
            StyleHeldOffsetSide[i] = config.Bind(
                "FigureGrab", $"{s}HeldOffsetSide", HeldOffsetSide.Value,
                $"Held lateral position offset (grab-anchor local X) toward the thumb-index pinch. {per}");
            StyleHeldOffsetUp[i] = config.Bind(
                "FigureGrab", $"{s}HeldOffsetUp", HeldOffsetUp.Value,
                $"Held position offset out of the palm (grab-anchor local Y). {per}");
            StyleHeldOffsetForward[i] = config.Bind(
                "FigureGrab", $"{s}HeldOffsetForward", HeldOffsetForward.Value,
                $"Held position offset toward the fingertips (grab-anchor local Z). {per}");
            StyleHeldTiltDegrees[i] = config.Bind(
                "FigureGrab", $"{s}HeldTiltDegrees", HeldTiltDegrees.Value,
                "LEGACY — no effect, superseded by [FigureGrab] " + s + "HeldRotPitch. Read once, " +
                "as the seed for its successor.");
            StyleHeldFaceYawDegrees[i] = config.Bind(
                "FigureGrab", $"{s}HeldFaceYawDegrees", HeldFaceYawDegrees.Value,
                "LEGACY — no effect, superseded by [FigureGrab] " + s + "HeldRotYaw. Read once, as " +
                "the seed for its successor.");
            StyleHeldScale[i] = config.Bind(
                "FigureGrab", $"{s}HeldScale", HeldScale.Value,
                $"Inspection zoom applied on top of the figure's board world-scale while held. {per}");
            StyleHeldRollDegrees[i] = config.Bind(
                "FigureGrab", $"{s}HeldRollDegrees", 0f,
                "LEGACY — no effect, superseded by [FigureGrab] " + s + "HeldRotRoll. Read once, as " +
                "the seed for its successor.");

            // The trio, named so it sorts together and says which axis it is. Seeded from the
            // predecessor's CURRENT value, so an existing tuning carries over untouched.
            StyleHeldRotPitch[i] = config.Bind(
                "FigureGrab", $"{s}HeldRotPitch", StyleHeldTiltDegrees[i].Value,
                "PITCH (degrees): tips the mini forward and back, about the axis running across " +
                $"your palm. This is the old HeldTiltDegrees under a name that says which axis it is. {per}");
            StyleHeldRotYaw[i] = config.Bind(
                "FigureGrab", $"{s}HeldRotYaw", StyleHeldFaceYawDegrees[i].Value,
                "YAW (degrees): turns the mini about ITS OWN up axis — a spin, never a tip, so it " +
                "brings the readable front toward you. Applied to the mini before pitch and roll, " +
                "which is what keeps it a spin no matter how the other two are set. MIRRORED " +
                $"between the hands, so tune the right hand and the left follows. {per}");
            StyleHeldRotRoll[i] = config.Bind(
                "FigureGrab", $"{s}HeldRotRoll", StyleHeldRollDegrees[i].Value,
                "ROLL (degrees): turns the mini about ITS OWN forward axis. MIRRORED between the " +
                $"hands like the yaw. {per}");
        }

        // Live-tune hook: any held-pose tunable change re-poses the currently-held mini in-hand
        // (the in-headset debug-menu steppers), so tuning is interactive. BepInEx still persists
        // every write to dev.gloomhavenvr.figuregrab.cfg. The legacy globals keep their hooks
        // (harmless — unread once the per-style entries exist).
        void Reapply(object sender, EventArgs e) => FigureGrabbable.ReapplyAll();
        HeldScale.SettingChanged += Reapply;
        HeldOffsetForward.SettingChanged += Reapply;
        HeldOffsetUp.SettingChanged += Reapply;
        HeldOffsetSide.SettingChanged += Reapply;
        HeldUpright.SettingChanged += Reapply;
        HeldTiltDegrees.SettingChanged += Reapply;
        HeldFaceYawDegrees.SettingChanged += Reapply;
        for (int i = 0; i < HandStyles.Count; i++)
        {
            StyleHeldOffsetSide[i].SettingChanged += Reapply;
            StyleHeldOffsetUp[i].SettingChanged += Reapply;
            StyleHeldOffsetForward[i].SettingChanged += Reapply;
            StyleHeldTiltDegrees[i].SettingChanged += Reapply;
            StyleHeldFaceYawDegrees[i].SettingChanged += Reapply;
            StyleHeldScale[i].SettingChanged += Reapply;
            StyleHeldRollDegrees[i].SettingChanged += Reapply;
            StyleHeldRotPitch[i].SettingChanged += Reapply;
            StyleHeldRotYaw[i].SettingChanged += Reapply;
            StyleHeldRotRoll[i].SettingChanged += Reapply;
        }
        // A hand-style SWITCH changes which per-style set is active — re-pose a held mini
        // right away (the Active* accessors read the new style on the next call).
        if (Plugin.HandStyle != null)
            Plugin.HandStyle.SettingChanged += Reapply;
    }
}
