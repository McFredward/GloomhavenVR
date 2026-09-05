using BepInEx.Configuration;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// HOW A MAP ITEM SITS IN YOUR HAND — the prop half of the held pose, tunable SEPARATELY from
/// the figures'.
///
/// <para><b>THE REPORT (ModBuild 350 hardware round), verbatim.</b> "Füge eigene offsets hinzu
/// an welche Position die Items zu sehen sein sollen in der Hand wenn man sie greift. Das selbe
/// gibt es schon für Figuren, ich will genau das selbe nun auch für Map-Items (zerstörbare
/// obstacles, Truhen, Goldhaufen,...) separat einstellen können."</para>
///
/// <para><b>WHY IT NEEDED ITS OWN SET AT ALL.</b> ModBuild 349 gave props the figure hold by
/// READING THE FIGURE'S KEYS — <c>FigureGrabConfig.HeldOffsetFor</c>,
/// <c>HeldUprightRotation</c>, <c>HeldPalmRotation</c> — which was right for "behave like a
/// figure" and wrong for "and be adjustable on its own". A chest is a box the size of a hex and
/// a mini is a 30 mm figure on a base: the pinch offset and the inspection pitch that read one
/// well cannot read the other well, and there was no dial that could say so. These eight keys
/// are the figures' eight EFFECTIVE dials, mirrored one for one, on their own axis.</para>
///
/// <para><b>NOTHING MOVES ON FIRST RUN.</b> Every default below is the figure value the mod
/// SHIPS — read out of <c>Defaults/Defaults.Board.cs</c>, never off a <c>Clamped(...)</c> call,
/// which on this project is a pre-bind fallback and not a shipped default. So the first build
/// with these keys holds a chest exactly where ModBuild 349 held it, and only a deliberate edit
/// moves it.</para>
///
/// <para><b>ONE MIRROR, NOT A SMALLER ONE.</b> The figures' pose is nine config keys per hand
/// style (<c>{Glove,Plate,Arcane}HeldOffset{Side,Up,Forward}</c> +
/// <c>{…}HeldRot{Pitch,Yaw,Roll}</c>) plus two global modes (<c>HeldUpright</c>,
/// <c>HeldUprightAtGrab</c>), but they collapse to EIGHT effective values at the point of use:
/// <c>FigureGrabConfig.ActiveHeld{Side,Up,Forward,Tilt,FaceYaw,Roll}</c> pick the worn style's
/// entry, and the two modes are global by design ("a mode, not geometry"). The per-style
/// multiplicity is a property of the GLOVE, not of the object in it — a chest does not change
/// shape when you change gauntlets — so the mirror is of the eight effective dials, and every
/// figure key is accounted for in the table in the round notes.</para>
///
/// <para><b>AND ONE KEY THE FIGURES DO NOT HAVE</b> (2026-09-05, the handedness round):
/// <see cref="SameInBothHands"/> decides whether the item is held the SAME WAY in both hands or as
/// a mirror image between them.
/// It is not part of the one-for-one mirror above and deliberately has no figure twin — the
/// figures' hold was tuned round after round with the reflection in the picture and the user has
/// accepted it ("bei den Figuren passt es"), so the figure call sites pass a literal
/// <c>true</c>.</para>
///
/// <para><b>LIVE-TUNABLE, with no hook of its own.</b> A held prop's pose is re-asserted every
/// frame by <c>GrabbableProp.TickHeld</c> → <c>ApplyHeldPose</c> (that re-assert is why a
/// foreign write to a held prop's transform lasts one frame instead of a whole hold), and
/// <see cref="HeldOffsetFor"/> is read live by <c>GrabbableProp.AllowsHand</c>. So dropping a
/// tuned cfg and re-reading it re-poses what is already in the hand, exactly like the figures'
/// <c>ReapplyAll</c> hook does — one dial, no restart. Only
/// <see cref="HeldUprightAtGrab"/> is a grab-time capture (it is a one-shot by definition), and
/// that is true of the figure key it mirrors too.</para>
///
/// <para><b>MULTIPLAYER.</b> Prop holds are local-only in this build BY DESIGN (see
/// <see cref="HeldProps"/>): nothing about a held prop goes on the wire, so a peer never renders
/// one and there is no mirrored visual for these keys to be read on the wrong side of. When the
/// prop hold does get a wire record, these keys follow the standing ruling the figures already
/// follow — the OWNER's values drive what every viewer sees, and a viewer's own copy of the same
/// key is never ANDed or blended into it. There is deliberately NO per-sub-feature sync switch
/// here: prop sync will be part of whatever whole-board dial already exists, or nothing.</para>
/// </summary>
internal static class PropHeldPose
{
    /// <summary>Held lateral offset toward the thumb–index pinch (GrabAnchor-local X, meters).
    /// Mirrors the figures' <c>{Style}HeldOffsetSide</c>.</summary>
    public static ConfigEntry<float>? OffsetSide;

    /// <summary>Held offset out of the palm (GrabAnchor-local Y, meters). Mirrors the figures'
    /// <c>{Style}HeldOffsetUp</c>.</summary>
    public static ConfigEntry<float>? OffsetUp;

    /// <summary>Held offset toward the fingertips (GrabAnchor-local Z, meters). Mirrors the
    /// figures' <c>{Style}HeldOffsetForward</c>.</summary>
    public static ConfigEntry<float>? OffsetForward;

    /// <summary>PITCH (degrees) — tips the item toward your face. Mirrors the figures'
    /// <c>{Style}HeldRotPitch</c>.</summary>
    public static ConfigEntry<float>? RotPitch;

    /// <summary>YAW (degrees) — spins the item about its OWN up axis. Mirrors the figures'
    /// <c>{Style}HeldRotYaw</c>.</summary>
    public static ConfigEntry<float>? RotYaw;

    /// <summary>ROLL (degrees) — spins the item about its OWN forward axis. Mirrors the figures'
    /// <c>{Style}HeldRotRoll</c>.</summary>
    public static ConfigEntry<float>? RotRoll;

    /// <summary>Upright pinch pose vs the flat palm pose. Mirrors the figures'
    /// <c>HeldUpright</c>.</summary>
    public static ConfigEntry<bool>? Upright;

    /// <summary>Stand the item the right way up in the world at the instant of the grab, whatever
    /// angle you reached from. Mirrors the figures' <c>HeldUprightAtGrab</c>.</summary>
    public static ConfigEntry<bool>? UprightAtGrab;

    /// <summary>
    /// MIRROR THE HOLD BETWEEN THE HANDS, or give both hands the authored pose verbatim
    /// (2026-09-05, the handedness round). The NINTH key, and the only one with no figure twin — it exists because of
    /// the report the figures' eight dials have never produced.
    ///
    /// <para><b>THE REPORT, verbatim.</b> <i>"Die Rotation ist anders, wenn ich Props in die linke
    /// oder rechte Hand nehme. Bei den Figuren passt es. Bei den Props habe ich die Rotation nun so
    /// eingestellt, dass es für die linke Hand passt; wenn ich es dann mit der rechten Hand nehme,
    /// ist es verdreht — warum klappt das bei den Figuren, aber Props nicht?"</i></para>
    ///
    /// <para><b>THE ANSWER, AND WHY IT IS A SWITCH AND NOT A FIX.</b> It does not work differently
    /// for the figures. The prop path and the figure path apply the SAME mirror, and it is exact —
    /// see <see cref="HeldPoseMirror"/>, which is now the one place either of them computes it.
    /// What the mirror costs is set by the YAW alone: it reflects the held object's FACING and
    /// leaves its up axis in the palm untouched, so at yaw 0° or ±180° the two hands are identical
    /// and at the shipped −133° they present the object's front 94° apart. The figures' hold sits
    /// where it does because it was tuned round after round WITH that reflection in the picture;
    /// the map items' has not been. So the honest question is not "which path is broken" but
    /// "should this object be mirrored at all", and that is about how the player holds their two
    /// hands, which no amount of source reading decides.</para>
    ///
    /// <para><b>SHIPPED FALSE = TODAY, BIT FOR BIT.</b> Nothing moves on first run: false is the
    /// mirror, which is what every build since ModBuild 349 has done. Turned ON, both hands take
    /// the MIRRORED form — so the hand the report was written about does not move at all and the
    /// other one comes to meet it. It is the RIGHT hand that changes, and that is deliberate: the
    /// player reaching for this switch is the player who tuned the pose while watching the hand the
    /// dials are NOT authored for, so the picture he has already accepted is the mirrored one. See
    /// <see cref="HeldPoseMirror.Sign"/>. He needs no arithmetic and his three dials keep both
    /// their numbers and their meaning on either setting.</para>
    ///
    /// <para><b>THE NAME IS THE PICTURE, NOT THE TRANSFORM.</b> It was called PropHeldMirrorHands
    /// for exactly one build and renamed the same day, because "mirror off" became a lie the moment
    /// off meant "apply the mirrored form to both hands" — a mirror IS still applied, to both. The
    /// key says what the player sees instead: ON, the item sits the same way in both hands; OFF,
    /// each hand holds it as the mirror image of the other. No cfg anyone has tuned is orphaned by
    /// the rename — the key had never been in a build a player ran.</para>
    ///
    /// <para><b>IN THE FLAT PALM POSE</b> (<see cref="Upright"/> off) this switch moves the OFFSET
    /// and nothing else: that pose is pitch-only, and a pitch is mirror-invariant.</para>
    ///
    /// <para>LIVE like the other eight (the per-frame re-assert in <c>GrabbableProp.TickHeld</c>
    /// re-poses whatever is already in the hand), and MULTIPLAYER-inert for the same reason they
    /// are: prop holds are local-only in this build, and when they do reach the wire this key
    /// follows the standing ruling — the OWNER's value decides what every viewer sees, never
    /// ANDed with a viewer's own copy.</para>
    /// </summary>
    public static ConfigEntry<bool>? SameInBothHands;

    /// <summary>
    /// Bound from <c>FigureGrabConfig.Bind</c>, into the SAME <c>dev.gloomhavenvr.figuregrab.cfg</c>
    /// and the SAME <c>[FigureGrab]</c> section as the keys they mirror — so a tuned cfg drop is
    /// still one file, and the prop rows sit directly under the figure rows they were copied from
    /// in every alphabetical listing the menu builds.
    /// </summary>
    internal static void Bind(ConfigFile config)
    {
        const string tail =
            " Applies to MAP ITEMS only (chests, gold piles, quest items, resources, traps and "
            + "destructible obstacles) and never to the figures — the matching [FigureGrab] "
            + "Glove/Plate/Arcane key still owns those. Ships at the figures' shipped value, so "
            + "nothing moves until you change it. LIVE: a prop already in your hand re-poses on "
            + "the next frame.";

        OffsetSide = config.Bind(
            "FigureGrab", "PropHeldOffsetSide", Defaults.PropHeldOffsetSide,
            "Held lateral position offset (grab-anchor local X) toward the thumb-index pinch, for "
            + "a held MAP ITEM. Mirrored between the hands, so tune the right hand and the left "
            + "follows." + tail);
        OffsetUp = config.Bind(
            "FigureGrab", "PropHeldOffsetUp", Defaults.PropHeldOffsetUp,
            "Held position offset out of the palm (grab-anchor local Y), for a held MAP ITEM."
            + tail);
        OffsetForward = config.Bind(
            "FigureGrab", "PropHeldOffsetForward", Defaults.PropHeldOffsetForward,
            "Held position offset toward the fingertips (grab-anchor local Z), for a held MAP "
            + "ITEM." + tail);
        RotPitch = config.Bind(
            "FigureGrab", "PropHeldRotPitch", Defaults.PropHeldRotPitch,
            "PITCH (degrees): tips a held MAP ITEM forward and back, about the axis running "
            + "across your palm." + tail);
        RotYaw = config.Bind(
            "FigureGrab", "PropHeldRotYaw", Defaults.PropHeldRotYaw,
            "YAW (degrees): turns a held MAP ITEM about ITS OWN up axis — a spin, never a tip, so "
            + "it brings the readable front toward you. Applied before pitch and roll, which is "
            + "what keeps it a spin however the other two are set. Mirrored between the hands."
            + tail);
        RotRoll = config.Bind(
            "FigureGrab", "PropHeldRotRoll", Defaults.PropHeldRotRoll,
            "ROLL (degrees): turns a held MAP ITEM about ITS OWN forward axis. Mirrored between "
            + "the hands like the yaw." + tail);
        Upright = config.Bind(
            "FigureGrab", "PropHeldUpright", Defaults.PropHeldUpright,
            "Hold a MAP ITEM UPRIGHT (standing the way it stands on its hex) pinched between "
            + "thumb and index and turned toward you. False = the flat-on-palm pose, which takes "
            + "the pitch and ignores the yaw and the roll." + tail);
        UprightAtGrab = config.Bind(
            "FigureGrab", "PropHeldUprightAtGrab", Defaults.PropHeldUprightAtGrab,
            "Stand a MAP ITEM the right way up IN THE WORLD at the moment you grab it, whatever "
            + "angle you reached from. Captured ONCE, at the grab: afterwards it rides the hand, "
            + "so turning your wrist still turns it through every angle. The angles above stay "
            + "offsets — with this on they are offsets from 'standing up' rather than from the "
            + "hand." + tail);
        SameInBothHands = config.Bind(
            "FigureGrab", "PropHeldSameInBothHands", Defaults.PropHeldSameInBothHands,
            "Hold a MAP ITEM THE SAME WAY IN BOTH HANDS. OFF (the default, and how every build so "
            + "far has held it): each hand holds the item as the MIRROR IMAGE of the other — "
            + "right when you bring both hands up the same way, and a visible turn when you do "
            + "not. ON: both hands hold it identically, the way your LEFT hand holds it now. "
            + "Nothing has to be re-entered — PropHeldRotYaw, PropHeldRotRoll and "
            + "PropHeldOffsetSide keep their numbers and their meaning on either setting; it is "
            + "the RIGHT hand that comes over to match the left. (In the flat palm pose, with "
            + "PropHeldUpright off, this moves the sideways offset and nothing else.)" + tail);
    }

    // ---- the accessors, shaped exactly like FigureGrabConfig's -----------------------------
    //
    // Each guards against a null entry and falls back to the SHIPPED default rather than to a
    // literal invented here: a fallback that disagrees with Defaults/ is a second shipped value
    // nobody can find. Reached only if something asks for a held pose before Bind() ran, which
    // the call order makes impossible today and which must still not throw inside a hold.

    private static float Val(ConfigEntry<float>? e, float shipped) => e != null ? e.Value : shipped;

    private static bool Val(ConfigEntry<bool>? e, bool shipped) => e != null ? e.Value : shipped;

    /// <summary>Pitch in degrees (both poses).</summary>
    internal static float Pitch => Val(RotPitch, Defaults.PropHeldRotPitch);

    /// <summary>Upright-pose yaw in degrees, canonical for the RIGHT hand.</summary>
    internal static float Yaw => Val(RotYaw, Defaults.PropHeldRotYaw);

    /// <summary>Upright-pose roll in degrees, canonical for the RIGHT hand.</summary>
    internal static float Roll => Val(RotRoll, Defaults.PropHeldRotRoll);

    /// <summary>Upright pinch pose (true) or flat palm pose (false).</summary>
    internal static bool HeldUpright => Val(Upright, Defaults.PropHeldUpright);

    /// <summary>Capture "the right way up in the world" once, at the grab.</summary>
    internal static bool HeldUprightAtGrab => Val(UprightAtGrab, Defaults.PropHeldUprightAtGrab);

    /// <summary>Hold the item identically in both hands (both take the MIRRORED form), or mirror
    /// it between them, which is what ships. See <see cref="SameInBothHands"/> for the report this
    /// answers and <see cref="HeldPoseMirror.Sign"/> for why "identical" is the mirrored form and
    /// not the authored one.</summary>
    internal static bool Alike => Val(SameInBothHands, Defaults.PropHeldSameInBothHands);

    /// <summary>
    /// The GrabAnchor-local held offset for one hand. The tuned values are canonical for the RIGHT
    /// hand; the LEFT is the MIRROR IMAGE across the hand frame's left-right (X) axis, so only the
    /// lateral component flips sign. The hand rig frame is NOT mirrored between hands (mesh
    /// mirrored, frame shared), so without this flip the same local X puts the item on the same
    /// frame-side of both hands, which is anatomically opposite. Identical in shape and in reason
    /// to <c>FigureGrabConfig.HeldOffsetFor</c>.
    /// </summary>
    internal static Vector3 HeldOffsetFor(HandSide side)
        => HeldPoseMirror.Offset(
            side == HandSide.Left, Alike,
            Val(OffsetSide, Defaults.PropHeldOffsetSide),
            Val(OffsetUp, Defaults.PropHeldOffsetUp),
            Val(OffsetForward, Defaults.PropHeldOffsetForward));

    /// <summary>Yaw for one hand — mirroring a rotation across the hand frame's left-right plane
    /// negates the yaw (and the roll) and leaves the pitch untouched. Returns the NEGATED value on
    /// both hands while <see cref="SameInBothHands"/> is on.</summary>
    internal static float YawFor(HandSide side)
        => HeldPoseMirror.Angle(side == HandSide.Left, Alike, Yaw);

    /// <summary>Roll for one hand (see <see cref="YawFor"/>).</summary>
    internal static float RollFor(HandSide side)
        => HeldPoseMirror.Angle(side == HandSide.Left, Alike, Roll);

    /// <summary>
    /// The upright held orientation as a FIXED CONSTANT rotation relative to the GrabAnchor.
    ///
    /// <para>ORDER MATTERS and a single <c>Quaternion.Euler(pitch, yaw, roll)</c> gets it wrong:
    /// Unity composes that as Ry * Rx * Rz, so the yaw would be applied LAST, about the HAND's up
    /// axis, and would TIP an already-pitched item instead of spinning it. The yaw goes FIRST, in
    /// the item's own frame. This is <c>FigureGrabConfig.HeldUprightRotation</c> with the prop
    /// dials substituted, and the composition order is the same for the same reason.</para>
    /// </summary>
    internal static Quaternion HeldUprightRotation(HandSide side)
        => HeldPoseMirror.Upright(side == HandSide.Left, Alike, Pitch, Yaw, Roll);

    /// <summary>The flat palm pose: pitch only. It deliberately does NOT take the yaw and the
    /// roll — giving it all three makes the two poses identical and turns
    /// <see cref="HeldUpright"/> into a switch that does nothing, which is a mistake the figure
    /// path already made once.</summary>
    internal static Quaternion HeldPalmRotation() => HeldPoseMirror.Palm(Pitch);
}
