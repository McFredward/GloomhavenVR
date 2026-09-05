using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// THE ONE DEFINITION OF "THE OTHER HAND" for a held object's pose — the three sign flips and the
/// two rotations that turn one authored pose into a pose for either hand.
///
/// <para><b>WHY IT IS ITS OWN FILE (2026-09-05, the handedness round).</b> The user reported,
/// verbatim: <i>"Die Rotation ist anders, wenn ich Props in die linke oder rechte Hand nehme. Bei den Figuren passt
/// es. Bei den Props habe ich die Rotation nun so eingestellt, dass es für die linke Hand passt;
/// wenn ich es dann mit der rechten Hand nehme, ist es verdreht — warum klappt das bei den
/// Figuren, aber Props nicht?"</i> The premise of that question is that the two paths differ. They
/// did not: <c>FigureGrabConfig.HeldOffsetFor/HeldFaceYawFor/HeldRollFor/HeldUprightRotation</c>
/// and <c>PropHeldPose.HeldOffsetFor/YawFor/RollFor/HeldUprightRotation</c> were the SAME five
/// expressions written twice, and the mirror they compute is exact — a numeric check of the
/// shipped prop dials (pitch 18°, yaw −133°, roll 0°) reproduces the reflected right-hand axes to
/// the last digit on the left. So the answer to "why does it work for figures" is that it does not
/// work differently at all; see <see cref="Upright"/> for what the mirror actually costs and why
/// the same dial value is invisible on one object and glaring on another.</para>
///
/// <para>Two copies of one rule is how the two paths would eventually diverge for real, and this
/// project's own lint file (<c>scripts/check-mirrors.sh</c>) exists because "someone tunes one
/// copy" is the failure that keeps happening. So the rule lives here once and both callers read
/// it, rather than each carrying its own spelling of the same three ternaries.</para>
///
/// <para><b>WHAT THE MIRROR IS, GEOMETRICALLY.</b> The mod's <c>GrabAnchor</c> frame is built the
/// same way on both hands (+Y out of the palm, +Z along the fingers), and the rig root that
/// carries it is mirror-conjugated per side by <c>VRHand.SyncVisualOffset</c> (its seat roll and
/// yaw are negated for the left hand, its pitch is not). Under that construction the anchor's ±X
/// is the ANATOMICALLY opposite direction on the two hands, so reproducing a pose in the other
/// hand is the reflection <c>M = diag(-1, 1, 1)</c> in anchor space: the lateral offset flips
/// sign, a YAW (about the frame's Y) and a ROLL (about its Z) flip sign, and a PITCH (about the
/// mirror axis itself) does not.</para>
///
/// <para><b>WHAT IT COSTS, WHICH IS THE PART THE REPORT IS ABOUT.</b> The mirror leaves the held
/// object's UP axis in the palm untouched and reflects only its FACING. The size of that
/// reflection is set by the yaw (and the roll) and by nothing else: at yaw 0° and at yaw ±180° the
/// mirror is a fixed point and the two hands are bit-identical, while at the shipped −133° the two
/// hands present the object's front <b>94° apart</b> in the shared anchor frame. That is exactly
/// as true of a miniature as of a chest — which is why a figure pose tuned near a fixed point
/// looks the same in both hands while a map-item pose tuned at −133° does not, with not one line
/// of code differing between them.</para>
///
/// <para><b>THE SWITCH.</b> Whether the mirror is the RIGHT operation is a question about the
/// player, not about the geometry: it reproduces the hold when the two hands are posed
/// symmetrically (both palms toward the face, the way a mini is brought up to be read) and it
/// swings the object when they are not (both hands reaching the same way across the board, the way
/// a chest is picked up). That cannot be derived, so <c>mirrored</c> is a caller's
/// decision and every entry point takes it. Passing <c>false</c> makes both hands use the authored
/// numbers verbatim, which is "the other hand looks like the one I tuned".</para>
///
/// <para><b>NO UNITY COMPONENTS, NO <c>HandSide</c>.</b> Every entry point takes a plain
/// <c>bool left</c> rather than the enum, so this file depends on nothing but
/// <c>Vector3</c>/<c>Quaternion</c> and can be source-linked into
/// <c>tests/GloomhavenVR.WireTests</c> — where <c>HeldPoseMirrorVectors</c> drives the left/right
/// pair against the reflection it is supposed to be. Allocation-free: both returns are structs and
/// nothing here touches a transform, a config entry or a log.</para>
/// </summary>
internal static class HeldPoseMirror
{
    /// <summary>+1 for the hand the dials are authored for (the RIGHT one), −1 for the other hand
    /// while <paramref name="mirrored"/> is on, and +1 for both hands while it is off.</summary>
    internal static float Sign(bool left, bool mirrored) => left && mirrored ? -1f : 1f;

    /// <summary>The GrabAnchor-local held offset. Only the LATERAL component is a mirror term:
    /// out-of-palm (Y) and toward-the-fingertips (Z) lie in the mirror plane and are the same
    /// number on both hands.</summary>
    internal static Vector3 Offset(bool left, bool mirrored, float side, float up, float forward)
        => new(Sign(left, mirrored) * side, up, forward);

    /// <summary>One mirrored ANGLE — the yaw and the roll, which are rotations about axes the
    /// reflection reverses. Never call it for the pitch: that is a rotation about the mirror axis
    /// and is mirror-INVARIANT, so negating it would tip the two hands opposite ways.</summary>
    internal static float Angle(bool left, bool mirrored, float degrees)
        => Sign(left, mirrored) * degrees;

    /// <summary>
    /// The upright pinch pose as a FIXED anchor-local rotation.
    ///
    /// <para>ORDER MATTERS and a single <c>Quaternion.Euler(pitch, yaw, roll)</c> gets it wrong:
    /// Unity composes that as Ry * Rx * Rz, so the yaw would land LAST, about the HAND's up axis,
    /// and would TIP an already-pitched object instead of spinning it. The yaw goes FIRST, in the
    /// object's own frame, and the pitch/roll ride on top.</para>
    /// </summary>
    internal static Quaternion Upright(bool left, bool mirrored, float pitch, float yaw, float roll)
        => Quaternion.Euler(pitch, 0f, Angle(left, mirrored, roll))
           * Quaternion.Euler(0f, Angle(left, mirrored, yaw), 0f);

    /// <summary>The flat palm pose: pitch only, and therefore the same rotation on both hands
    /// whatever <c>mirrored</c> says — a pitch is mirror-invariant. It deliberately does NOT take
    /// the yaw and the roll; giving it all three makes the two poses identical and turns the
    /// upright switch into a switch that does nothing, which the figure path did once.</summary>
    internal static Quaternion Palm(float pitch) => Quaternion.Euler(pitch, 0f, 0f);
}
