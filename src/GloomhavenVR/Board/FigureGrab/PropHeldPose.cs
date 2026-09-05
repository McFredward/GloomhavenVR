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
/// <para><b>NOTHING MOVES ON FIRST RUN — TRUE OF THE EIGHT, NO LONGER TRUE OF THE NINTH.</b>
/// Every one of the eight defaults below is the figure value the mod SHIPS — read out of
/// <c>Defaults/Defaults.Board.cs</c>, never off a <c>Clamped(...)</c> call, which on this project
/// is a pre-bind fallback and not a shipped default. So the first build with those keys holds a
/// chest exactly where ModBuild 349 held it, and only a deliberate edit moves them.
/// <see cref="SameInBothHands"/> is the exception and says so in its own remarks: it was flipped
/// to TRUE and it DOES move the picture, deliberately, because shipping the remedy switched off
/// meant the user never found it.</para>
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
/// a mirror image between them. It is not part of the one-for-one mirror above and deliberately has
/// no figure twin — the figures' hold was tuned round after round with the reflection in the
/// picture and the user has accepted it ("bei den Figuren passt es"), so the figure call sites pass
/// a literal <c>false</c> (mirror kept) while the map items ship it ON. That literal is the ONE
/// number that differs between the two paths; the code behind it is one expression in
/// <see cref="HeldPoseMirror"/> that both of them call.</para>
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
    /// <para><b>THE ANSWER (2026-09-05, second round — the first one was wrong about WHERE).</b>
    /// It does not work differently for the figures, and the mirror itself is exact: the shipped
    /// left and right <c>Anchor_Grab</c> frames really are mirror-conjugate, measured off all three
    /// prefab pairs (see <see cref="HeldPoseMirror"/>). What was wrong is the FRAME the mirror
    /// lands in. <c>[FigureGrab] PropHeldUprightAtGrab</c> ships ON, and it composes the held
    /// rotation against <c>Inverse(anchor.rotation) * W</c> — so the anchor cancels out of the final
    /// world pose exactly, leaving <c>world = W * held</c> with <c>W</c> a pure world-Y yaw taken
    /// from where the hand was pointing. There is no handedness left in that frame for the mirror to
    /// cancel, so the mirror survives as itself: the two hands present the item <c>wrap(2·yaw)</c>
    /// apart in the world.</para>
    ///
    /// <para><b>AND THAT IS WHY THE FIGURES SURVIVE IT.</b> Not because their yaw sits at a fixed
    /// point — the ModBuild 434 log killed that theory by printing a 94° figure swing beside a happy
    /// user. <c>wrap(2·−133°) = 94°</c> for the figures and <c>wrap(2·−89°) = 178°</c> for the map
    /// items at the user's own re-tuned value: the same defect, twice the size, about a near-vertical
    /// axis. A miniature spun 94° about its standing axis is still a miniature standing up in your
    /// palm; a chest or a token spun 178° shows you its BACK. The object decides whether the artefact
    /// reads as "turned" or as "verdreht"; the code never differed.</para>
    ///
    /// <para><b>SHIPPED TRUE, AND WHAT THAT DOES TO A TUNED CFG.</b> The user's
    /// <c>PropHeldRotYaw = −89</c> is untouched and still reads −89 in his file. With this key ON
    /// both hands apply <c>+89</c> — which is exactly what his LEFT hand applies today, i.e. the
    /// picture he tuned and accepted ("so eingestellt, dass es für die linke Hand passt"). His RIGHT
    /// hand comes over to match it and <c>mirrorSwing</c> goes to 0°. He re-enters nothing. Turning
    /// the key OFF restores the mirror exactly as ModBuild 349-434 held it. See
    /// <see cref="HeldPoseMirror.RotationSign"/> for why "identical" is the mirrored form and not the
    /// authored one.</para>
    ///
    /// <para><b>IT ASSUMES <see cref="UprightAtGrab"/> IS ON, WHICH IS ALSO WHAT SHIPS.</b> The
    /// derivation above is a statement about the frame the pose is composed in, so it is only as
    /// true as that key. With <c>PropHeldUprightAtGrab</c> turned OFF the pose rides the anchor
    /// instead, the anchor's own mirror-conjugacy stays in the product, and the mirror is much
    /// closer to the right operation — a player who has turned that key off probably wants this one
    /// off too. Nothing is derived from it here on purpose: a dial that silently ignores you is
    /// worse than one that needs a second click, and the HANDEDNESS log line prints both keys and
    /// the resulting <c>poseFrame=</c> so the pair can be read rather than guessed at.</para>
    ///
    /// <para><b>IT NO LONGER TOUCHES THE OFFSET, AND IT USED TO.</b> Until this round the same
    /// switch reached <c>HeldPoseMirror.Offset</c>, so turning it ON also moved a right-hand item
    /// from +0.051 m to −0.051 m in anchor X — 10.2 cm to the pinky side of the palm. The offset is
    /// written in ANCHOR space, which <c>UprightBase</c> never cancels, so its flip is right on every
    /// setting and now follows nothing. See <see cref="HeldOffsetFor"/>.</para>
    ///
    /// <para><b>THE NAME IS THE PICTURE, NOT THE TRANSFORM.</b> It was called PropHeldMirrorHands
    /// for exactly one build and renamed the same day, because "mirror off" became a lie the moment
    /// off meant "apply the mirrored form to both hands" — a mirror IS still applied, to both. The
    /// key says what the player sees instead: ON, the item sits the same way in both hands; OFF,
    /// each hand holds it as the mirror image of the other. No cfg anyone has tuned is orphaned by
    /// the rename — the key had never been in a build a player ran.</para>
    ///
    /// <para><b>IN THE FLAT PALM POSE</b> (<see cref="Upright"/> off) this switch does NOTHING at
    /// all: that pose is pitch-only and a pitch is mirror-invariant, and the offset stopped
    /// following the switch this round. It used to move the offset, which is the paragraph
    /// above.</para>
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

        // The ninth key is not one of the eight and must not claim to be: it has no figure twin to
        // ship at the value of, and unlike them it DOES change the picture on first run.
        const string ninthTail =
            " Applies to MAP ITEMS only (chests, gold piles, quest items, resources, traps and "
            + "destructible obstacles) and never to the figures, which keep the mirror. LIVE: a "
            + "prop already in your hand re-poses on the next frame.";

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
            "Hold a MAP ITEM THE SAME WAY IN BOTH HANDS. ON (the default): "
            + "both hands hold it identically, the way your LEFT hand held it before. Nothing has "
            + "to be re-entered — PropHeldRotYaw, PropHeldRotRoll and PropHeldOffsetSide keep "
            + "their numbers and their meaning on either setting; it is the RIGHT hand that comes "
            + "over to match the left. OFF (how ModBuild 349-434 held it): each hand holds the "
            + "item as the MIRROR IMAGE of the other, which reproduces the hold only when you "
            + "bring both hands up as mirror images of each other — reach for the same hex with "
            + "either hand, as you actually do, and it is a visible turn of twice the yaw "
            + "instead. (In the flat palm pose, with PropHeldUpright off, this does nothing: that "
            + "pose is pitch-only and a pitch is the same in both hands.) This setting assumes "
            + "PropHeldUprightAtGrab is ON, which is also what ships — with that key off the item "
            + "rides the hand frame instead, where the mirror is much closer to right, so turn "
            + "this one off as well." + ninthTail);
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
    /// answers and <see cref="HeldPoseMirror.RotationSign"/> for why "identical" is the mirrored form and
    /// not the authored one.</summary>
    internal static bool Alike => Val(SameInBothHands, Defaults.PropHeldSameInBothHands);

    /// <summary>
    /// The GrabAnchor-local held offset for one hand. The tuned values are canonical for the RIGHT
    /// hand; the LEFT is the MIRROR IMAGE across the hand frame's left-right (X) axis, so only the
    /// lateral component flips sign. Identical in shape and in reason to
    /// <c>FigureGrabConfig.HeldOffsetFor</c>.
    ///
    /// <para><b>IT DOES NOT FOLLOW <see cref="SameInBothHands"/>, AND IT USED TO</b> (2026-09-05).
    /// The offset is written to <c>t.localPosition</c>, in ANCHOR space, which
    /// <c>HeldPoseMirror.UprightBase</c> never touches — so unlike the rotation the anchor does not
    /// cancel out of it and the anchor's ±X really is the anatomically opposite direction on the
    /// two hands (measured from the shipped prefabs; see <c>HeldPoseMirror</c>). The flip is
    /// therefore right on every setting. While the switch reached it, turning the switch ON moved a
    /// right-hand item from +0.051 m to −0.051 m in anchor X — 10.2 cm to the pinky side of the
    /// palm — so the one key that fixed the reported rotation broke the position in the same
    /// click.</para>
    /// </summary>
    internal static Vector3 HeldOffsetFor(HandSide side)
        => HeldPoseMirror.Offset(
            side == HandSide.Left,
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

    /// <summary>The held rotation for one hand in EITHER pose — <see cref="HeldPoseMirror.Rotation"/>
    /// with the map items' dials substituted. The figures' twin is
    /// <c>FigureGrabConfig.HeldRotationFor</c>; both are one line onto the same shared
    /// expression.</summary>
    internal static Quaternion HeldRotationFor(HandSide side, bool upright)
        => HeldPoseMirror.Rotation(side == HandSide.Left, Alike, upright, Pitch, Yaw, Roll);
}
