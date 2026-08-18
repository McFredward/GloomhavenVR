using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// The arithmetic behind a FIXIERT control board that is invisible to the world zoom and immovable
/// under everything else. Free of everything but <c>Mathf</c>/<c>Vector3</c>/<c>Quaternion</c>
/// precisely so it can be linked into the wire tests and driven at the rig scales the user's own
/// hardware logs recorded (×19.01 … ×137.05) — see tests/GloomhavenVR.WireTests/BoardZoomCarryVectors.cs.
///
/// <para><b>WHY THE OBVIOUS ANSWERS ARE BOTH WRONG.</b> Four shipped builds each moved WHERE the
/// pinned board's pose was re-derived, and each landed on one of two pure anchors. The user has now
/// rejected both by name:</para>
/// <list type="bullet">
/// <item><b>World-static</b> (ModBuild 158): "Ich hatte in einer Hand das Controllboard und habe dann
/// gezoomed — dann hat das controllboard mitgezoomed, das soll nicht passieren", and again on 159:
/// "Das Board zoomed immer noch im Fixiert modus mit."</item>
/// <item><b>Rig-anchored</b> (ModBuild 160): "Fixiert funktioniert nun garnicht mehr — egal was man
/// dort einstellt, das board zoomed nun immer mit und <b>geht immer mit</b>." A rig-parented board is
/// indeed zoom-invariant, and it also travels with the player, which is the half he rejects.</item>
/// </list>
///
/// <para><b>THE GEOMETRY THAT MAKES THIS SOLVABLE.</b> The world pinch does not scale about the head.
/// <c>Rig/WorldGrab.cs</c> solves the rig pose so the GLUED HAND MIDPOINT stays put:</para>
/// <code>rig.position = _midAnchorWorld - rot * (mid * s);</code>
/// <para>so a scale change moves the head in world space as well, and the perceived pose of a world
/// point <c>P</c> is the RIG-LOCAL one, <c>rot⁻¹·(P − rigPos)/s</c>. Two consequences follow directly,
/// and they are the whole design:</para>
/// <list type="number">
/// <item>Holding the board's RIG-LOCAL pose constant makes it <b>invisible to the zoom</b> — perceived
/// size and perceived distance both unchanged. (What 160 did, on every frame, hence "geht immer mit".)</item>
/// <item>Holding the board's WORLD pose constant makes it <b>stay in the room</b> — which is what
/// walking away from it must do. (What 158 did, on every frame, hence "zoomt mit".)</item>
/// </list>
///
/// <para><b>THE RULE.</b> Neither anchor, but a gate between them: <i>on any frame in which the rig's
/// lossy SCALE changed, re-derive the pinned board's world pose from its rig-local pose cached at the
/// end of the previous frame; on every other frame, touch nothing at all.</i> A zoom is then the only
/// event that can move the board's world pose, and it moves it exactly enough to leave the eye
/// unchanged. Walking, stick locomotion, teleport, flight, snap turn and a world-grab DRAG all leave
/// the scale alone, so nothing is written and the board stays in the room — user ruling 2026-08-07,
/// "Fixiert heißt: völlig unabhängig vom Character, bewegt sich in KEINSTER Weise, außer es wird aktiv
/// verschoben oder skaliert".</para>
/// </summary>
internal static class BoardZoomCarry
{
    /// <summary>
    /// The zoom gate's threshold, RELATIVE. It has to be relative: the user's own logs sweep the rig
    /// from ×19.01 to ×137.05 in a single session, so any absolute epsilon is either deaf at the small
    /// end or trigger-happy at the large one — at ×137 an absolute 0.01 is 0.007 %, at ×19 it is 0.05 %.
    /// 1e-4 of the larger operand is far below one frame of a real pinch (WorldGrab's exponential
    /// smoothing moves the scale by whole percent per frame) and far above the float noise of a rig
    /// that nobody is touching, which is exactly zero: a static rig's lossyScale is recomputed from
    /// unchanged operands and compares bit-equal.
    /// </summary>
    internal const float RelativeScaleEpsilon = 1e-4f;

    /// <summary>
    /// THE GATE. True when <paramref name="current"/> differs from <paramref name="previous"/> by more
    /// than <see cref="RelativeScaleEpsilon"/> of the larger of the two.
    ///
    /// <para><b>Compare against the last CARRIED scale, never against the last FRAME's.</b> A creep of
    /// less than one epsilon per frame would otherwise never fire the gate at all while still summing
    /// to a real zoom over a second — the board would ride it, silently, and the failure would look
    /// exactly like report 2 again. Against the last carried scale a sub-threshold creep accumulates
    /// until it crosses, so the gate can be late by at most one epsilon but can never be missed.</para>
    ///
    /// <para>Non-finite or non-positive operands report NO change: a degenerate rig scale is not a zoom,
    /// and writing a pose derived from it would put the board at a NaN the freeze sentinel then convicts
    /// us for. The caller re-baselines instead.</para>
    /// </summary>
    internal static bool ScaleChanged(float previous, float current, out float relativeDelta)
    {
        relativeDelta = 0f;
        if (!IsFinite(previous) || !IsFinite(current) || previous <= 0f || current <= 0f)
            return false;
        float larger = Mathf.Max(previous, current);
        relativeDelta = Mathf.Abs(current - previous) / larger;
        return relativeDelta > RelativeScaleEpsilon;
    }

    /// <summary>
    /// The pin holder's new scale, so that the board's apparent size is unchanged by the zoom.
    ///
    /// <para>The holder ("GloomhavenVR.TrayPin") sits at the world origin with identity rotation and a
    /// uniform scale <c>H</c> baked ONCE at pin time from the rig (PlayTray.ApplyFollowMode); the tray
    /// root hangs under it with the player's dialled <c>localScale L</c>. World size is therefore
    /// <c>H·L</c> and PERCEIVED size is <c>H·L/s</c>. Holding that constant across a scale change means
    /// holding the RATIO <c>H/s</c>, i.e. scaling <c>H</c> by exactly the factor <c>s</c> moved by.</para>
    ///
    /// <para>Expressed as a ratio rather than as <c>H := s</c> on purpose. <c>ApplyFollowMode</c> bakes
    /// <c>H</c> from <c>_root.parent ?? _anchorParent</c>, and that anchor is only the rig scale while
    /// its own local scale is 1 — an assumption nothing in this file can check. Carrying the ratio is
    /// correct whatever <c>H</c> started at, and it is continuous at the pin moment by construction.</para>
    /// </summary>
    internal static float CarryHolderScale(float holderScale, float previousRigScale, float rigScale)
    {
        if (!IsFinite(holderScale) || !IsFinite(previousRigScale) || !IsFinite(rigScale))
            return holderScale;
        if (previousRigScale <= 0f || rigScale <= 0f || holderScale <= 0f)
            return holderScale;
        return holderScale * (rigScale / previousRigScale);
    }

    /// <summary>
    /// A world point in the player's own frame — <c>rot⁻¹·(P − rigPos)/s</c>, i.e. Unity's
    /// <c>Transform.InverseTransformPoint</c> for a uniformly scaled rig, written out so the wire tests
    /// can drive it without a live Transform. THIS IS WHAT THE PLAYER PERCEIVES: the rig is what scales
    /// the eyes, the hands and the interpupillary distance, so two world points that differ only by the
    /// zoom map to the same rig-local point and therefore look identical.
    /// </summary>
    internal static Vector3 ToRigLocal(Vector3 world, Vector3 rigPos, Quaternion rigRot, float rigScale)
    {
        if (rigScale <= 0f || !IsFinite(rigScale))
            return Vector3.zero;
        return (Conjugate(rigRot) * (world - rigPos)) / rigScale;
    }

    /// <summary>
    /// The inverse of a rotation. <c>Quaternion.Inverse</c> would be the obvious call and is NOT used,
    /// for a reason that matters to this file specifically: it is an engine ECall, so it throws
    /// outside Unity ("ECall methods must be packaged into a system module") and every property in
    /// BoardZoomCarryVectors would be untestable — which is precisely the situation that let four
    /// builds ship. For a unit quaternion the inverse IS the conjugate, rig rotations are unit by
    /// construction (they are written by WorldGrab as a product of AngleAxis and Euler results), and
    /// the conjugate is pure managed arithmetic. Same answer, and it can be driven on the build
    /// machine.
    /// </summary>
    internal static Quaternion Conjugate(Quaternion q) => new(-q.x, -q.y, -q.z, q.w);

    /// <summary>The exact inverse of <see cref="ToRigLocal"/> (<c>Transform.TransformPoint</c> for a
    /// uniform scale). Feeding the CACHED rig-local point back through this against the NEW rig is the
    /// carry: same perceived pose, new world pose.</summary>
    internal static Vector3 ToWorld(Vector3 rigLocal, Vector3 rigPos, Quaternion rigRot, float rigScale)
        => rigPos + rigRot * (rigLocal * rigScale);

    /// <summary>A world length in perceived metres. At rig scale 21 the player IS twenty-one times
    /// larger, so a world metre is 1/21 of a perceived one — the same distinction the apparent-size
    /// clamp and the stick-flight speed dial already make, for the same reason: a length is meaningless
    /// until you say whose metres it is in.</summary>
    internal static float PerceivedMeters(float worldMeters, float rigScale)
        => rigScale > 0f && IsFinite(rigScale) ? worldMeters / rigScale : worldMeters;

    /// <summary>
    /// The angle the board subtends, in degrees. <b>This is the measure that decides the reports, and
    /// its absence is why four broken builds were signed off.</b> The old diagnostic printed apparent
    /// size alone (world size ÷ rig scale) and that number agreed with every one of them, because what
    /// the eye judges is ANGULAR size — size ÷ distance. A world-static board under a zoom has BOTH its
    /// perceived size and its perceived distance scale by 1/s, so its apparent-size line looks wrong
    /// while its angle is unchanged; a board whose size is frozen but whose distance is not reads as
    /// "mitgezoomed" while the same line looks right. Only the pair, plus the angle, is decidable.
    /// </summary>
    internal static float SubtendedDegrees(float perceivedWidth, float perceivedDistance)
    {
        if (!IsFinite(perceivedWidth) || !IsFinite(perceivedDistance) || perceivedDistance <= 1e-6f)
            return 0f;
        return 2f * Mathf.Atan2(perceivedWidth * 0.5f, perceivedDistance) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// The tray's apparent width per unit of its own localScale — the shared measure of the release-time
    /// safety clamp (<c>PlayTray.ClampApparentSize</c>) and the live two-hand gesture window
    /// (<c>PlayTray.GrabScaleLimits</c>), restated here so the tests can prove the property that matters:
    /// <b>it stops moving with the zoom.</b>
    ///
    /// <para>The user's second sentence in report 1 — "Weiterhin hat sich damit auch das Maximum und
    /// Minimum wieder verschoben" — is this expression. <c>parent</c> is the pin holder's scale, baked
    /// once at pin time; <c>rigScale</c> is live. While the holder was frozen and the rig swept, the
    /// ratio <c>parent/rigScale</c> swept with it (his log: a CONSTANT parent chain ×40.10 against rig
    /// scales ×5.13–×137.19), so both the clamp and the gesture window moved under the player's hands.
    /// Once the holder tracks the rig across a scale change the ratio is a constant, and both
    /// enforcement points are zoom-invariant without either limit being loosened.</para>
    /// </summary>
    internal static float ApparentWidthPerScaleUnit(float halfWidthLocal, float parentScale, float rigScale)
    {
        if (rigScale <= 0f || !IsFinite(rigScale))
            rigScale = 1f;
        return halfWidthLocal * 2f * parentScale / rigScale;
    }

    private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
}
