using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// THE ONE DEFINITION OF "THE OTHER HAND" for a held object's pose — the sign flips, the two
/// rotations and the upright-at-grab base that turn one authored pose into a pose for either hand.
/// Both grab paths (<c>FigureGrabbable</c> for miniatures and health props, <c>GrabbableProp</c>
/// for map items) read every one of them from here; neither spells any of it a second time.
///
/// <para><b>WHY IT IS ITS OWN FILE (2026-09-05, the handedness round).</b> The user reported,
/// verbatim: <i>"Die Rotation ist anders, wenn ich Props in die linke oder rechte Hand nehme. Bei den Figuren passt
/// es. Bei den Props habe ich die Rotation nun so eingestellt, dass es für die linke Hand passt;
/// wenn ich es dann mit der rechten Hand nehme, ist es verdreht — warum klappt das bei den
/// Figuren, aber Props nicht?"</i> The premise of that question is that the two paths differ. They
/// did not: <c>FigureGrabConfig.HeldOffsetFor/HeldFaceYawFor/HeldRollFor/HeldUprightRotation</c>
/// and <c>PropHeldPose.HeldOffsetFor/YawFor/RollFor/HeldUprightRotation</c> were the SAME five
/// expressions written twice — and <c>CaptureUprightBase</c> was a SIXTH, twelve lines long and
/// character-identical in both files. All six live here now.</para>
///
/// <para>Two copies of one rule is how the two paths would eventually diverge for real, and this
/// project's own lint file (<c>scripts/check-mirrors.sh</c>) exists because "someone tunes one
/// copy" is the failure that keeps happening. So the rule lives here once and both callers read
/// it, rather than each carrying its own spelling of the same three ternaries.</para>
///
/// <para><b>THE MIRROR, GEOMETRICALLY, AND THE MEASUREMENT THAT SETTLES IT.</b> The mod's
/// <c>Anchor_Grab</c> frames on the two hands really are mirror-conjugate, and that is no longer a
/// claim: the shipped prefab pairs were read out and the left anchor's local rotation is
/// <c>(−x, y, z, −w)</c> of the right's to six decimals on every one of the three hand sets
/// (<c>VRHand_L/R</c>, <c>VRHandPlate_L/R</c>, <c>VRHandArcane_L/R</c>). That quaternion relation
/// IS conjugation by the reflection <c>M = diag(−1, 1, 1)</c> — for a rotation of angle θ about
/// axis n, <c>M R M</c> is the rotation of −θ about <c>Mn</c>, whose quaternion is exactly
/// <c>(x, −y, −z, w) ≡ (−x, y, z, −w)</c>. See <see cref="MirrorResidualDegrees"/>, which measures
/// that residual at runtime for whichever hand set is worn, and
/// <c>HeldPoseMirrorVectors.AnchorPairsAreMirrorConjugate</c>, which asserts it against the
/// prefabs themselves. So under the reflection <c>M</c> in ANCHOR space the lateral offset flips
/// sign, a YAW (about the frame's Y) and a ROLL (about its Z) flip sign, and a PITCH (about the
/// mirror axis itself) does not.</para>
///
/// <para><b>THE RAW LEFT↔RIGHT ANGLE IS NOT AN ASYMMETRY, AND MUST NOT BE READ AS ONE.</b>
/// <c>Quaternion.Angle(leftAnchor, rightAnchor)</c> reads 0.021° on the Glove set, 1.998° on the
/// Plate set and 11.401° on the Arcane set. Those three numbers are the SAME construction, not an
/// authoring error on one of them: they are the size of each anchor's tilt OUT OF the mirror plane,
/// doubled. The Glove anchor is a 180° turn about an axis lying IN the plane (<c>w = 0</c>,
/// <c>x = 0</c>), so it is its own mirror image and the angle collapses to float noise; the Arcane
/// anchor is cocked further out of the plane, so its mirror sits 11.4° away. The residual that
/// would actually indicate a broken pair is <see cref="MirrorResidualDegrees"/>, and it is ~0 on
/// all three.</para>
///
/// <para><b>WHERE THE MIRROR IS APPLIED, WHICH IS THE PART THE REPORT IS ABOUT.</b> The mirror
/// above is correct <i>in anchor space</i>. The shipped pose is not applied in anchor space. With
/// <c>HeldUprightAtGrab</c> on — which is the shipped default on BOTH paths — the held rotation is
/// composed against <see cref="UprightBase"/>, and that base is <c>Inverse(anchor.rotation) * W</c>,
/// so the anchor cancels out of the product exactly:
/// <c>world = anchor.rotation * (Inverse(anchor.rotation) * W) * held = W * held</c>. <c>W</c> is a
/// pure world-Y yaw taken from the direction the hand was pointing at the grab. There is no
/// handedness left in it. The mirror therefore has nothing to cancel, and what survives is the
/// mirror itself: the two hands present the object <c>wrap(2·yaw)</c> apart in the world, about an
/// axis tilted off world up by the pitch.</para>
///
/// <para>That residue reproduces the hold ONLY if the two hands' grab-time headings are themselves
/// mirror-symmetric (<c>h_left = −h_right</c>, in which case <c>W_L·held_L = M·(W_R·held_R)·M</c>
/// and the two hands do show a true mirror pair). Board objects are not picked up that way: you
/// reach for the chest at ITS hex, so both hands point at the same place and <c>h_left ≈
/// h_right</c>. The <c>[Props]/[Figure] HANDEDNESS</c> line prints both headings so a log can say
/// which of the two it was on real hardware, instead of this comment asserting it.</para>
///
/// <para><b>WHY THE FIGURES SURVIVE THE SAME DEFECT.</b> Not because their yaw sits at a fixed
/// point — it does not, and the ModBuild 434 log killed that theory by printing a 94° figure swing
/// beside a happy user. <c>wrap(2·−133°) = +94°</c> and <c>wrap(2·−89°) = +178°</c>: same defect,
/// twice the size. A miniature spun 94° about its own standing axis is still a miniature standing
/// up in your palm, facing a bit differently. A chest, a token or a quest item spun 178° about that
/// axis shows you its BACK. The object decides whether the artefact reads as "turned" or as
/// "verdreht"; the code was never different.</para>
///
/// <para><b>THE OFFSET IS NOT SUBJECT TO ANY OF THAT.</b> <c>t.localPosition</c> is written in
/// ANCHOR space and never multiplied by <see cref="UprightBase"/>, so the anchor does NOT cancel
/// out of it and the reflection is exactly right there — unconditionally, on every setting. Hence
/// <see cref="Offset"/> takes no <c>bothHandsAlike</c> argument and <see cref="OffsetSign"/> takes
/// no mode at all. It used to: until 2026-09-05 the offset followed the same switch as the
/// rotation, which meant turning that switch ON moved the RIGHT hand's item from +0.051 m to
/// −0.051 m in anchor X — a 10.2 cm jump to the pinky side of the palm. The switch, had the user
/// found it, would have fixed his rotation and broken his position in the same click.</para>
///
/// <para><b>THE ROTATION SWITCH.</b> <c>bothHandsAlike</c> stays a caller's decision because
/// "which of the two forms both hands take" is a question about the player's tuning history and not
/// about geometry. Passing <c>true</c> gives BOTH hands the mirrored form — see
/// <see cref="RotationSign"/> for why that one and not the authored one — so the item sits
/// identically in the two hands and no dial has to be re-entered to get there. Map items ship it
/// ON (<c>Defaults.PropHeldSameInBothHands</c>); the figures pass a literal <c>false</c>, which is
/// the ONE number that differs between the paths and is a look, not code.</para>
///
/// <para><b>NO UNITY COMPONENTS, NO <c>HandSide</c>.</b> Every entry point takes a plain
/// <c>bool left</c> rather than the enum and plain vectors rather than a <c>Transform</c>, so this
/// file depends on nothing but <c>Vector3</c>/<c>Quaternion</c> and can be source-linked into
/// <c>tests/GloomhavenVR.WireTests</c> — where <c>HeldPoseMirrorVectors</c> drives the left/right
/// pair against the reflection it is supposed to be. <see cref="UprightBase"/> deliberately takes
/// the anchor's forward, up and rotation as three values rather than the transform they came from,
/// for exactly that reason. Allocation-free: every return is a struct and nothing here touches a
/// transform, a config entry or a log.</para>
/// </summary>
internal static class HeldPoseMirror
{
    /// <summary>
    /// WHICH OF THE TWO FORMS THIS HAND'S ROTATION TAKES: +1 is the pose as authored (the dials
    /// are canonical for the RIGHT hand), −1 is its mirror image.
    ///
    /// <para>Mirroring, each hand takes its own form. With <paramref name="bothHandsAlike"/> on,
    /// BOTH hands take the MIRRORED form — deliberately that one and not the authored one. The
    /// player who needs this setting is the player who tuned the pose while watching the hand the
    /// dials are NOT authored for and found the other hand twisted (that is the whole report), so
    /// the pose he has already looked at and accepted IS the mirrored form. Handing both hands the
    /// authored form instead would give him the picture he rejected in both hands and ask him to
    /// negate three dials to get back. This way his numbers keep their values and their meaning in
    /// the hand he tuned them in — at the cost, stated in the key's own description, that it is the
    /// RIGHT hand that moves.</para>
    ///
    /// <para>ROTATION ONLY. The offset has its own sign (<see cref="OffsetSign"/>) with no mode
    /// term, because the offset is applied in a frame the mirror is unconditionally right in — see
    /// the class remarks.</para>
    /// </summary>
    internal static float RotationSign(bool left, bool bothHandsAlike)
        => left || bothHandsAlike ? -1f : 1f;

    /// <summary>
    /// The lateral sign for the held OFFSET. Unconditional: <c>t.localPosition</c> is anchor-local
    /// and the anchor's ±X is the anatomically opposite direction on the two hands (measured — see
    /// the class remarks), so the flip is right on every setting and follows no switch.
    /// </summary>
    internal static float OffsetSign(bool left) => left ? -1f : 1f;

    /// <summary>The GrabAnchor-local held offset. Only the LATERAL component is a mirror term:
    /// out-of-palm (Y) and toward-the-fingertips (Z) lie in the mirror plane and are the same
    /// number on both hands.</summary>
    internal static Vector3 Offset(bool left, float side, float up, float forward)
        => new(OffsetSign(left) * side, up, forward);

    /// <summary>One mirrored ANGLE — the yaw and the roll, which are rotations about axes the
    /// reflection reverses. Never call it for the pitch: that is a rotation about the mirror axis
    /// and is mirror-INVARIANT, so negating it would tip the two hands opposite ways.</summary>
    internal static float Angle(bool left, bool bothHandsAlike, float degrees)
        => RotationSign(left, bothHandsAlike) * degrees;

    /// <summary>
    /// The upright pinch pose as a FIXED anchor-local rotation.
    ///
    /// <para>ORDER MATTERS and a single <c>Quaternion.Euler(pitch, yaw, roll)</c> gets it wrong:
    /// Unity composes that as Ry * Rx * Rz, so the yaw would land LAST, about the HAND's up axis,
    /// and would TIP an already-pitched object instead of spinning it. The yaw goes FIRST, in the
    /// object's own frame, and the pitch/roll ride on top.</para>
    /// </summary>
    internal static Quaternion Upright(bool left, bool bothHandsAlike, float pitch, float yaw, float roll)
        => Quaternion.Euler(pitch, 0f, Angle(left, bothHandsAlike, roll))
           * Quaternion.Euler(0f, Angle(left, bothHandsAlike, yaw), 0f);

    /// <summary>The flat palm pose: pitch only, and therefore the same rotation on both hands
    /// whatever <c>bothHandsAlike</c> says — a pitch is mirror-invariant, so in the flat pose the
    /// switch moves nothing at all (and the offset, which never followed it, is unchanged too). It
    /// deliberately does NOT take the yaw and the roll; giving it all three makes the two poses
    /// identical and turns the upright switch into a switch that does nothing, which the figure
    /// path did once.</summary>
    internal static Quaternion Palm(float pitch) => Quaternion.Euler(pitch, 0f, 0f);

    /// <summary>
    /// THE HELD ROTATION, either pose — the two-line ternary that
    /// <c>FigureGrabbable.ApplyHeldPose</c> and <c>GrabbableProp.ApplyHeldPose</c> each used to
    /// spell for themselves, and that the two diagnostics then spelled twice more. One expression
    /// now, so "upright picks the pinch pose, otherwise the flat palm pose" cannot come to mean two
    /// different things on the two paths.
    /// </summary>
    internal static Quaternion Rotation(
        bool left, bool bothHandsAlike, bool upright, float pitch, float yaw, float roll)
        => upright ? Upright(left, bothHandsAlike, pitch, yaw, roll) : Palm(pitch);

    /// <summary>
    /// THE UPRIGHT-AT-GRAB BASE — the pre-multiplier that stands the object the right way up in the
    /// WORLD at the instant of the grab, whatever angle the hand reached from, and identity when
    /// the option is off.
    ///
    /// <para>Twelve lines that were character-identical in <c>FigureGrabbable</c> and
    /// <c>GrabbableProp</c> until 2026-09-05, differing only in which config bool they read — the
    /// sixth copied expression the handedness round found, and the load-bearing one: it is this
    /// term that cancels the anchor out of the held rotation and moves the mirror into a frame with
    /// no handedness in it (see the class remarks). It takes the anchor's forward, up and rotation
    /// as three plain values rather than the <c>Transform</c> they were read from, so the whole
    /// file stays free of Unity components and stays testable outside a Unity process.</para>
    ///
    /// <para>Captured ONCE, at the grab, and then left alone. That is the whole point: the object
    /// starts upright however you reached for it — palm down, from the side, upside down — and from
    /// then on it is an ordinary fixed rotation relative to the hand, so turning your wrist still
    /// turns it through every angle. It is not a constraint that keeps re-righting the object, which
    /// would fight you the moment you tried to look at its underside.</para>
    ///
    /// <para>The facing comes from the hand's own forward flattened onto the horizontal, not from
    /// the head: it keeps the object's front pointing the way you were reaching, and it does not
    /// make the result depend on where anyone is standing. Grabbing with the hand pointing
    /// near-vertical leaves that forward undefined, so the hand's UP is used instead — some
    /// horizontal direction is always available and any of them is better than a NaN.</para>
    ///
    /// <para>MULTIPLAYER: nothing extra is needed. A held figure's WORLD rotation is what goes on
    /// the wire (<c>Net.NetFigures</c>), so a peer sees whatever this produces, exactly; held map
    /// items are local-only in this build and put nothing on the wire at all.</para>
    /// </summary>
    internal static Quaternion UprightBase(
        bool uprightAtGrab, Vector3 anchorForward, Vector3 anchorUp, Quaternion anchorRotation)
    {
        if (!uprightAtGrab)
            return Quaternion.identity;

        Vector3 flat = Vector3.ProjectOnPlane(anchorForward, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f)
            flat = Vector3.ProjectOnPlane(anchorUp, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f)
            flat = Vector3.forward;

        return Quaternion.Inverse(anchorRotation) * Quaternion.LookRotation(flat.normalized, Vector3.up);
    }

    /// <summary>
    /// THE GRAB-TIME HEADING, in degrees about world up — the <c>W</c> of the class remarks,
    /// reduced to the one number that decides whether the mirror was ever the right operation.
    ///
    /// <para>Two hands whose headings are OPPOSITE (<c>h_left ≈ −h_right</c>, measured about the
    /// player's own forward) reached mirror-symmetrically, and the mirror then produces a true
    /// mirror pair in the world. Two hands whose headings AGREE reached for the same hex, and the
    /// mirror produces a <c>wrap(2·yaw)</c> spin instead. Printing it is how a log settles that
    /// without anyone being asked.</para>
    /// </summary>
    internal static float HeadingDegrees(Vector3 anchorForward, Vector3 anchorUp, Vector3 reference)
    {
        Vector3 flat = Vector3.ProjectOnPlane(anchorForward, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f)
            flat = Vector3.ProjectOnPlane(anchorUp, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f)
            flat = Vector3.forward;

        Vector3 baseline = Vector3.ProjectOnPlane(reference, Vector3.up);
        if (baseline.sqrMagnitude < 1e-6f)
            baseline = Vector3.forward;

        return Vector3.SignedAngle(baseline.normalized, flat.normalized, Vector3.up);
    }

    /// <summary>
    /// HOW FAR THE TWO HAND ANCHORS ARE FROM BEING A MIRROR PAIR, in degrees — 0 when the left
    /// anchor is exactly <c>M · right · M</c>, which is what every shipped hand set measures.
    ///
    /// <para>This is the number that answers "is the anchor a second cause", and the raw
    /// <c>Quaternion.Angle(left, right)</c> is NOT: that reads 0.021° / 1.998° / 11.401° on the
    /// Glove / Plate / Arcane sets purely because each anchor is cocked a different amount out of
    /// the mirror plane, and 11.4° there has been mistaken for an authoring fault. Conjugating by
    /// <c>M = diag(−1, 1, 1)</c> negates the quaternion's y and z (equivalently its x and w), so the
    /// comparison below is the whole test.</para>
    /// </summary>
    internal static float MirrorResidualDegrees(Quaternion leftAnchor, Quaternion rightAnchor)
        => Quaternion.Angle(leftAnchor, Mirrored(rightAnchor));

    /// <summary>A rotation reflected through the anchor frame's left-right plane:
    /// <c>M R M</c> with <c>M = diag(−1, 1, 1)</c>, which on the quaternion is y and z negated.
    /// Exposed so the wire tests can assert the shipped prefab pairs against it.</summary>
    internal static Quaternion Mirrored(Quaternion q) => new(q.x, -q.y, -q.z, q.w);
}
