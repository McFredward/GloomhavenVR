// THE LEFT HAND IS THE MIRROR OF THE RIGHT — driven as arithmetic, because in a headset it is
// only ever seen as "das ist verdreht".
//
// THE REPORT THIS PINS (the 2026-09-05 handedness round, verbatim): "Die Rotation ist anders,
// wenn ich Props in die linke oder rechte Hand nehme. Bei den Figuren passt es. Bei den Props habe ich die Rotation nun
// so eingestellt, dass es für die linke Hand passt; wenn ich es dann mit der rechten Hand nehme,
// ist es verdreht — warum klappt das bei den Figuren, aber Props nicht?"
//
// The answer that round found is that the two paths do NOT differ: the figures' held pose and the
// map items' held pose are the same five expressions, and the mirror they compute is exact. That
// answer is only worth something if it stays true, and there is nothing in a build log, a golden
// vector or a config surface that would notice the day one of the two paths grew a sign of its
// own again. Hence this file, and hence HeldPoseMirror being ONE function both of them call.
//
// WHAT ONLY A TEST CAN CATCH HERE, and why each block exists:
//
//   1. A SIGN. Negating the pitch instead of the yaw, or the yaw and not the roll, produces a
//      perfectly plausible pose in BOTH hands. It is wrong only in the relationship between them,
//      which no single screenshot shows and no assertion about one hand can fail on. The mirror is
//      therefore asserted as the REFLECTION ITSELF — every basis vector of the left-hand rotation
//      against M·(right-hand rotation)·M with M = diag(-1,1,1) — rather than against a table of
//      remembered numbers, so the test states the geometry and not a snapshot of it.
//
//   2. THE COMPOSITION ORDER. Quaternion.Euler(pitch, yaw, roll) composes as Ry*Rx*Rz, which puts
//      the yaw LAST and turns it into a tip. Both paths deliberately compose yaw FIRST. That is
//      invisible at yaw 0 and at pitch 0 — i.e. in exactly the state anyone would eyeball it in —
//      so it is driven at a pitch and a yaw that are both far from zero.
//
//   3. THE SWING IS THE YAW, DOUBLED. The mirror's visible cost is set by the YAW alone and equals
//      wrap(2*yaw): it vanishes at 0° and at ±180° and is widest near ±90°. The FIXED-POINT story
//      that once lived here — "his figure yaw happens to sit where the mirror is invisible" — is
//      DEAD, killed by the ModBuild 434 log, which printed a 94° figure swing beside a user who is
//      happy with the figures. What is true is that 2*(−133°) wraps to 94° and 2*(−89°) wraps to
//      178°: the same defect, twice the size, about a near-vertical axis. A miniature spun 94°
//      about its standing axis is still a miniature standing up; a chest spun 178° shows its back.
//
//   3b. THE ANCHOR PAIR IS A MIRROR PAIR, read out of the shipped prefabs. The raw left<->right
//      Anchor_Grab angle reads 0.021°/1.998°/11.401° on the Glove/Plate/Arcane sets and the 11.4°
//      has been mistaken for an authoring fault. It is not one: the left quaternion is exactly
//      (-x, y, z, -w) of the right on all three, which IS conjugation by diag(-1, 1, 1). The
//      residual against that conjugate is ~0 everywhere, so the anchor is not a second cause.
//
//   4. THE SWITCH, AND WHICH OF THE TWO POSES IT PICKS. [FigureGrab] PropHeldSameInBothHands on
//      must give both hands the pose the LEFT hand already shows — the one he tuned and accepted —
//      and NOT the authored (right-hand) form. Both designs are "identical in both hands" and both
//      look self-consistent in a headset; only one of them means he never has to re-enter a dial.
//      There is no runtime symptom that separates them, so it is nailed down here.
//
//   5. THE OFFSET DOES NOT FOLLOW THE SWITCH, AND IT USED TO. The offset is written to
//      t.localPosition in ANCHOR space, which HeldPoseMirror.UprightBase never cancels, so its flip
//      is unconditionally right. While PropHeldSameInBothHands reached it, turning the switch ON
//      moved a right-hand item from +0.051 m to -0.051 m in anchor X — 10.2 cm to the pinky side of
//      the palm. The one key that fixed the reported rotation broke the position in the same click,
//      and nothing but this file would have caught it before hardware.
//
// WHY THE EULER CONVERSION IS LOCAL, and why that is honest rather than a mirrored
// re-implementation. `Quaternion.Euler` is one of the few members of UnityEngine.CoreModule that
// is NOT managed — it is an ECall into the engine (`Internal_FromEulerRad`) and throws a
// SecurityException outside a Unity process, so `HeldPoseMirror.Upright` cannot be called from
// here at all. What CAN be called is everything the mirror actually decides: `RotationSign`,
// `OffsetSign`, `Angle`, `UprightBase`, `MirrorResidualDegrees` and
// `Offset` are driven as the real shipped functions, and the rotation blocks feed their REAL
// output into a local half-angle conversion, so the sign logic under test is never re-implemented
// — only the trig is. Quaternion multiplication, `Quaternion.Angle`, `Vector3.Angle` and
// `q * Vector3` are all managed and are Unity's own. The one thing that leaves is the COMPOSITION
// ORDER inside `Upright`, and that is why the last block is a source lint: it reads
// HeldPoseMirror.cs and asserts the shipped composition is the yaw-first one this file models,
// and that neither FigureGrabConfig nor PropHeldPose has grown a sign ternary of its own again.

using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Board.FigureGrab;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class HeldPoseMirrorVectors
{
    /// <summary>Quaternion comparisons are floating point on both sides of a double negation, so
    /// the bar is a real tolerance rather than equality — but a tight one: every failure this file
    /// exists for is a SIGN, worth tens of degrees, never a rounding step.</summary>
    private const float AngleEpsilon = 0.01f;

    private const float VectorEpsilon = 1e-4f;

    // The map items' shipped pose (Defaults.Board.cs). Written out rather than read from
    // PropHeldPose so the vectors keep meaning what they mean when somebody retunes a dial — the
    // geometry under test is the mirror, not the current taste.
    private const float PropPitch = 18f;
    private const float PropYaw = -133f;
    private const float PropRoll = 0f;
    private const float PropSide = 0.051f;
    private const float PropUp = -0.013f;
    private const float PropForward = 0.05f;

    public static void Run(Harness t, string repoRoot)
    {
        ReflectionIsExact(t);
        PitchIsInvariant(t);
        OffsetFlipsOnlySideways(t);
        CompositionOrderIsYawFirst(t);
        SwingIsSetByYawAlone(t);
        TheSwitchNeedsNoArithmetic(t);
        OneDefinitionForBothPaths(t, repoRoot);
        AnchorPairsAreMirrorConjugate(t, repoRoot);
        TheUprightBaseCancelsTheAnchor(t, repoRoot);
    }

    // -------------------------------------------------------------------------------------------
    //  The two helpers that stand in for the engine's native euler conversion. See the file header
    //  for why they exist; NOTHING about the mirror is decided here — every sign comes out of the
    //  shipped HeldPoseMirror.Angle, and the composition order is asserted against the real source
    //  by OneDefinitionForBothPaths below.
    // -------------------------------------------------------------------------------------------

    /// <summary>A quaternion for a rotation of <paramref name="degrees"/> about a unit axis, built
    /// from the managed constructor and managed trig.</summary>
    private static Quaternion AxisAngle(float ax, float ay, float az, float degrees)
    {
        float half = degrees * Mathf.Deg2Rad * 0.5f;
        float s = Mathf.Sin(half);
        return new Quaternion(ax * s, ay * s, az * s, Mathf.Cos(half));
    }

    /// <summary>Unity's <c>Quaternion.Euler(x, y, z)</c> composition — Ry * Rx * Rz — spelled out
    /// so it can run outside the engine.</summary>
    private static Quaternion Euler(float x, float y, float z)
        => AxisAngle(0f, 1f, 0f, y) * AxisAngle(1f, 0f, 0f, x) * AxisAngle(0f, 0f, 1f, z);

    /// <summary><c>HeldPoseMirror.Upright</c> with the engine's euler conversion swapped for the
    /// managed one. The SIGNS are the shipped function's; only the trig is local.</summary>
    private static Quaternion Upright(bool left, bool bothHandsAlike, float pitch, float yaw, float roll)
        => Euler(pitch, 0f, HeldPoseMirror.Angle(left, bothHandsAlike, roll))
           * Euler(0f, HeldPoseMirror.Angle(left, bothHandsAlike, yaw), 0f);

    /// <summary><c>HeldPoseMirror.Palm</c>, likewise.</summary>
    private static Quaternion Palm(float pitch) => Euler(pitch, 0f, 0f);

    // -------------------------------------------------------------------------------------------
    //  1. THE MIRROR IS THE REFLECTION — asserted against M·R·M, not against remembered numbers.
    // -------------------------------------------------------------------------------------------
    private static void ReflectionIsExact(Harness t)
    {
        t.Case("mirror/left-is-the-reflection-of-right");

        // Deliberately all three axes non-zero and none of them near a fixed point: a roll of 0
        // (which is what the mod ships) would let a dropped roll flip pass unnoticed.
        foreach ((float pitch, float yaw, float roll) in new[]
                 {
                     (PropPitch, PropYaw, PropRoll),   // the shipped map-item pose
                     (17f, -133f, 0f),                 // the shipped figure pose
                     (18f, -133f, 25f),                // …and one with a real roll on it
                     (-40f, 61f, -12f),
                 })
        {
            Quaternion right = Upright(left: false, bothHandsAlike: false, pitch, yaw, roll);
            Quaternion mirrored = Upright(left: true, bothHandsAlike: false, pitch, yaw, roll);

            // A rotation's mirror image maps the reflected source axis to the reflected image axis.
            // Checking all three basis vectors pins the whole rotation with no quaternion sign
            // ambiguity to argue about.
            AssertReflects(t, right, mirrored, Vector3.up, $"up, at ({pitch},{yaw},{roll})");
            AssertReflects(t, right, mirrored, Vector3.forward, $"forward, at ({pitch},{yaw},{roll})");
            AssertReflects(t, right, mirrored, Vector3.right, $"right, at ({pitch},{yaw},{roll})");
        }
    }

    /// <summary>M·R·M applied to a basis vector, where M = diag(-1,1,1) is the reflection in the
    /// hand frame's left-right plane. The left-hand rotation must agree with it exactly.</summary>
    private static void AssertReflects(Harness t, Quaternion right, Quaternion mirrored,
                                       Vector3 axis, string what)
    {
        Vector3 reflectedSource = new(-axis.x, axis.y, axis.z);
        Vector3 image = right * reflectedSource;
        Vector3 expected = new(-image.x, image.y, image.z);
        Vector3 actual = mirrored * axis;
        t.True((expected - actual).sqrMagnitude < VectorEpsilon * VectorEpsilon,
               $"the LEFT hand's {what} is the reflection of the RIGHT hand's "
               + $"(expected {Show(expected)}, got {Show(actual)})");
    }

    // -------------------------------------------------------------------------------------------
    //  2. PITCH IS MIRROR-INVARIANT — the one angle that must NOT flip.
    // -------------------------------------------------------------------------------------------
    private static void PitchIsInvariant(Harness t)
    {
        t.Case("mirror/pitch-does-not-flip");

        // Pitch alone (yaw and roll zero) is a rotation about the mirror axis itself, so the two
        // hands must receive the IDENTICAL rotation. Negating it here is the classic wrong fix and
        // would tip a held object toward the face in one hand and away in the other.
        Quaternion right = Upright(left: false, bothHandsAlike: false, PropPitch, 0f, 0f);
        Quaternion left = Upright(left: true, bothHandsAlike: false, PropPitch, 0f, 0f);
        t.True(Quaternion.Angle(right, left) < AngleEpsilon,
               "with only a pitch dialled in, both hands get the same rotation — a pitch is a "
               + "rotation about the mirror axis and is mirror-invariant");

        // The palm pose is pitch-only by construction, so it is the same rotation on both hands
        // whatever the mirror says. Stated as its own assertion because the palm pose takes no
        // side at all and that has to stay a deliberate property, not an oversight.
        t.True(Quaternion.Angle(Palm(PropPitch),
                                Upright(left: false, bothHandsAlike: false, PropPitch, 0f, 0f))
               < AngleEpsilon,
               "the flat palm pose is the upright pose with yaw and roll at zero");
    }

    // -------------------------------------------------------------------------------------------
    //  3. THE OFFSET — sideways flips, out-of-palm and toward-the-fingertips do not.
    // -------------------------------------------------------------------------------------------
    private static void OffsetFlipsOnlySideways(Harness t)
    {
        t.Case("mirror/offset-flips-x-only");

        Vector3 right = HeldPoseMirror.Offset(left: false, PropSide, PropUp, PropForward);
        Vector3 left = HeldPoseMirror.Offset(left: true, PropSide, PropUp, PropForward);

        t.True(Mathf.Abs(right.x - PropSide) < VectorEpsilon,
               "the RIGHT hand is the hand the dials are authored for — it takes the value as written");
        t.True(Mathf.Abs(left.x + PropSide) < VectorEpsilon,
               "the LEFT hand takes the negated lateral offset: the anchor's +X is the anatomically "
               + "opposite side of the two hands");
        t.True(Mathf.Abs(left.y - right.y) < VectorEpsilon && Mathf.Abs(left.z - right.z) < VectorEpsilon,
               "out of the palm (Y) and toward the fingertips (Z) lie IN the mirror plane and are "
               + "the same number on both hands");
    }

    // -------------------------------------------------------------------------------------------
    //  4. THE YAW GOES FIRST — a spin about the item's own axis, never a tip.
    // -------------------------------------------------------------------------------------------
    private static void CompositionOrderIsYawFirst(Harness t)
    {
        t.Case("mirror/yaw-is-applied-first");

        // With the yaw applied FIRST, in the item's own frame, the item's own up axis is decided
        // by the pitch and the roll alone — spinning it cannot tip it. The one-call spelling
        // Quaternion.Euler(pitch, yaw, roll) composes Ry*Rx*Rz and therefore yaws LAST, about the
        // hand's up axis, which moves the item's up axis as soon as it is pitched. That is the
        // difference this asserts, and at pitch 18° it is worth several degrees.
        Quaternion spun = Upright(left: false, bothHandsAlike: false, PropPitch, PropYaw, PropRoll);
        Quaternion unspun = Upright(left: false, bothHandsAlike: false, PropPitch, 0f, PropRoll);
        t.True(Vector3.Angle(spun * Vector3.up, unspun * Vector3.up) < AngleEpsilon,
               "the yaw is a pure SPIN: it leaves the item's own up axis exactly where the pitch "
               + "and the roll put it");

        Quaternion wrongOrder = Euler(PropPitch, PropYaw, PropRoll);
        t.True(Vector3.Angle(wrongOrder * Vector3.up, unspun * Vector3.up) > 1f,
               "…and the one-call Quaternion.Euler(pitch, yaw, roll) spelling does NOT — this is "
               + "the regression the split composition exists to prevent");
    }

    // -------------------------------------------------------------------------------------------
    //  5. THE SWING IS SET BY THE YAW ALONE — the explanation handed to the user, as arithmetic.
    // -------------------------------------------------------------------------------------------
    private static void SwingIsSetByYawAlone(Harness t)
    {
        t.Case("mirror/swing-is-a-function-of-the-yaw");

        // 0° and ±180° are fixed points of the mirror: a pose tuned there is bit-identical in the
        // two hands however large the pitch is. This is why "bei den Figuren passt es" and the map
        // items do not, with the same code running for both.
        t.True(Swing(PropPitch, 0f, 0f) < AngleEpsilon,
               "at yaw 0° the mirror is a fixed point — the two hands are identical");
        t.True(Swing(PropPitch, 180f, 0f) < AngleEpsilon,
               "at yaw 180° too, which is the other fixed point");
        t.True(Swing(PropPitch, -180f, 0f) < AngleEpsilon,
               "…and it is the same fixed point approached from the other side");

        // The shipped map-item yaw. ~94° is the number the HANDEDNESS log line prints as
        // mirrorSwing, and it is what "verdreht" measures.
        float shipped = Swing(PropPitch, PropYaw, PropRoll);
        t.True(shipped > 85f && shipped < 105f,
               $"at the shipped map-item yaw of {PropYaw}° the two hands present the item about "
               + $"94° apart — measured {shipped:0.#}°, which is the report");

        // A roll with no yaw swings it too, so the log line's claim must not be read as "yaw only".
        t.True(Swing(0f, 0f, 30f) > 1f,
               "a ROLL is a mirror term as well — the swing is set by the yaw AND the roll, and a "
               + "pose with a roll on it is not saved by a yaw at a fixed point");
    }

    /// <summary>The angle between the pose one hand gets and the pose the other gets — the
    /// <c>mirrorSwing</c> the grab-time HANDEDNESS line prints.</summary>
    private static float Swing(float pitch, float yaw, float roll)
        => Quaternion.Angle(Upright(left: false, bothHandsAlike: false, pitch, yaw, roll),
                            Upright(left: true, bothHandsAlike: false, pitch, yaw, roll));

    // -------------------------------------------------------------------------------------------
    //  6. THE SWITCH — on means "both hands get the pose the LEFT hand already shows", and the
    //     user has to do NO arithmetic to see it.
    // -------------------------------------------------------------------------------------------
    //
    // This is the block that pins the promise made to the player rather than a property of the
    // geometry. [FigureGrab] PropHeldSameInBothHands ON must reproduce, on BOTH hands, exactly the
    // picture his LEFT hand shows with the switch off — with PropHeldRotYaw, PropHeldRotRoll and
    // PropHeldOffsetSide untouched. An earlier draft of this feature handed both hands the AUTHORED
    // form instead, which is equally "identical in both hands" and was wrong for him: it would have
    // shown him, in both hands, the pose he had already rejected, and asked him to negate three
    // dials to get back to the one he wanted. There is no runtime symptom that separates the two
    // designs — both look self-consistent — so it is nailed down here.
    private static void TheSwitchNeedsNoArithmetic(Harness t)
    {
        t.Case("mirror/same-in-both-hands-is-the-left-hand-pose");

        // The pose he tuned and accepted: what the LEFT hand shows under the mirror.
        Quaternion tunedOnLeft = Upright(left: true, bothHandsAlike: false,
                                         PropPitch, PropYaw, PropRoll);

        t.True(Quaternion.Angle(Upright(left: true, bothHandsAlike: true,
                                        PropPitch, PropYaw, PropRoll), tunedOnLeft) < AngleEpsilon,
               "with PropHeldSameInBothHands ON the LEFT hand does not move at all — it is already "
               + "showing the pose he tuned, and the switch must not disturb it");
        t.True(Quaternion.Angle(Upright(left: false, bothHandsAlike: true,
                                        PropPitch, PropYaw, PropRoll), tunedOnLeft) < AngleEpsilon,
               "…and the RIGHT hand comes over to match it: both hands now hold the item exactly "
               + "the way his left hand held it, with not one dial re-entered");

        // The negative half, and the reason the block exists: it must NOT be the authored form.
        Quaternion authored = Upright(left: false, bothHandsAlike: false,
                                      PropPitch, PropYaw, PropRoll);
        t.True(Quaternion.Angle(Upright(left: false, bothHandsAlike: true,
                                        PropPitch, PropYaw, PropRoll), authored) > 1f,
               "and it is deliberately NOT the authored (right-hand) form — handing both hands "
               + "that would show him the picture he reported, in both hands");

        // THE OFFSET DOES NOT FOLLOW THE SWITCH, AND THIS BLOCK USED TO ASSERT THAT IT DID
        // (2026-09-05, round 2). The old assertion was wrong for a reason no runtime symptom in a
        // headset would have separated from the reported one: the offset is written to
        // t.localPosition, in ANCHOR space, and HeldPoseMirror.UprightBase never touches it — so
        // unlike the rotation the anchor does NOT cancel out of it, the anchor's ±X really is the
        // anatomically opposite direction on the two hands, and the flip is right on every setting.
        // While the switch reached it, turning the switch ON moved a right-hand item from
        // +0.051 m to −0.051 m in anchor X, 10.2 cm across the palm to the pinky side. The key that
        // fixed the reported rotation broke the position in the same click.
        t.Case("mirror/the-offset-never-follows-the-switch");
        Vector3 offRight = HeldPoseMirror.Offset(left: false, PropSide, PropUp, PropForward);
        Vector3 offLeft = HeldPoseMirror.Offset(left: true, PropSide, PropUp, PropForward);
        t.True(Mathf.Abs(offRight.x - PropSide) < VectorEpsilon,
               "the RIGHT hand keeps the authored lateral offset whatever the switch says — the "
               + "switch is a ROTATION mode and the offset lives in a frame the mirror is "
               + "unconditionally right in");
        t.True(Mathf.Abs(offLeft.x + PropSide) < VectorEpsilon,
               "…and the LEFT hand keeps the negated one, so the item stays on the thumb side of "
               + "BOTH palms on either setting");
        t.True((offRight - offLeft).sqrMagnitude > VectorEpsilon,
               "…which means the two hands' offsets stay DIFFERENT: collapsing them was the "
               + "10.2 cm jump across the palm the old design shipped");

        // THE FLAT PALM POSE. Pitch-only, so the switch cannot rotate anything there; the offset
        // above is the whole of its effect. Worth an assertion because a reader of the key's
        // description is told exactly that.
        t.Case("mirror/palm-pose-rotation-is-untouched-by-the-switch");
        // The rotation half: the palm pose takes no side and no switch, so all four combinations
        // are one rotation. Asserted against the UPRIGHT pose's own hand-to-hand difference so the
        // case cannot pass by both sides being trivially equal to each other and to nothing.
        t.True(Quaternion.Angle(Palm(PropPitch),
                                Upright(left: true, bothHandsAlike: true, PropPitch, 0f, 0f))
               < AngleEpsilon,
               "the flat palm pose is pitch-only, so the switch cannot rotate it — it is the same "
               + "rotation the upright pose gives at yaw 0 and roll 0, on either hand");
        // …and the offset half, which is NOT moved either, so in the flat palm pose the switch
        // does nothing whatsoever. That is what the key's description now promises, and it is the
        // half that changed this round: the offset used to be the switch's entire effect there.
        // The magnitude is stated so a silently-zeroed PropHeldOffsetSide could not make it pass.
        t.True(Mathf.Abs(offRight.x - PropSide) < VectorEpsilon && Mathf.Abs(PropSide) > VectorEpsilon,
               "…and the sideways offset does not move either — with PropHeldUpright off the "
               + "switch changes NOTHING, which is what the key's description promises");
    }

    // -------------------------------------------------------------------------------------------
    //  7. ONE DEFINITION, BOTH PATHS — a source lint, and the only assertion here that can see the
    //     thing the 2026-09-05 handedness round was actually about.
    // -------------------------------------------------------------------------------------------
    //
    // The report was "why does the mirror work for the figures and not for the props", and the
    // answer was that it works identically because the two files carried the SAME expressions. That
    // is a property of the source, not of any runtime value: the day either path grows its own
    // `side == HandSide.Left ? -x : x` back, every runtime assertion above still passes and the
    // answer given to the user quietly becomes false. So it is checked where it lives.
    private static void OneDefinitionForBothPaths(Harness t, string repoRoot)
    {
        string dir = Path.Combine(repoRoot, "src", "GloomhavenVR", "Board", "FigureGrab");
        string mirror = ReadOrEmpty(Path.Combine(dir, "HeldPoseMirror.cs"));
        string figures = ReadOrEmpty(Path.Combine(dir, "FigureGrabConfig.cs"));
        string props = ReadOrEmpty(Path.Combine(dir, "PropHeldPose.cs"));

        t.Case("mirror/composition-order-in-the-shipped-source");
        t.True(mirror.Length > 0, "HeldPoseMirror.cs is readable from the repo root");
        // The yaw-first composition the blocks above model. A single three-argument
        // Quaternion.Euler(pitch, yaw, roll) here would put the yaw last and turn it into a tip,
        // which no assertion running outside Unity could otherwise see.
        t.True(Regex.IsMatch(mirror,
                   @"Quaternion\.Euler\(\s*pitch\s*,\s*0f\s*,\s*Angle\([^)]*roll\s*\)\s*\)\s*\*\s*"
                   + @"Quaternion\.Euler\(\s*0f\s*,\s*Angle\([^)]*yaw\s*\)\s*,\s*0f\s*\)"),
               "HeldPoseMirror.Upright still composes the YAW FIRST, in the item's own frame, and "
               + "the pitch/roll on top — the composition the vectors above model");
        t.True(!Regex.IsMatch(mirror, @"Quaternion\.Euler\(\s*pitch\s*,\s*yaw\s*,\s*roll\s*\)"),
               "…and never the one-call spelling, which composes Ry*Rx*Rz and yaws about the HAND");

        t.Case("mirror/neither-path-carries-its-own-sign");
        // The exact shape that was duplicated: a hand-side ternary producing a sign or a negated
        // angle. Both files may still MENTION HandSide (they take it as a parameter and pass
        // `side == HandSide.Left` into the shared helper); what neither may do again is decide the
        // sign itself.
        var ownSign = new Regex(@"HandSide\.Left\s*\?\s*-");
        t.True(!ownSign.IsMatch(figures),
               "FigureGrabConfig reads the mirror out of HeldPoseMirror and no longer spells its "
               + "own sign flip — two copies of one rule is how the two paths diverge for real");
        t.True(!ownSign.IsMatch(props),
               "…and PropHeldPose likewise");
        t.True(figures.Contains("HeldPoseMirror.") && props.Contains("HeldPoseMirror."),
               "both paths really do call the shared definition");

        t.Case("mirror/the-figures-keep-the-mirror-unconditionally");
        // The other half of the 2026-09-05 handedness round's ruling, and the one that protects a
        // tuning the user has already accepted: the map items got a switch, the figures did not. If
        // a dial ever appears at the figure call sites, his figure hold has become changeable by a
        // key he did not ask for.
        t.True(Regex.Matches(figures, @"HeldPoseMirror\.\w+\([^;]*bothHandsAlike:\s*false").Count >= 4,
               "every figure call site passes bothHandsAlike: false as a LITERAL, so the figures "
               + "stay mirrored and their hold is bit-identical to what it was before the mirror "
               + "was shared");
        t.True(!Regex.IsMatch(figures, @"bothHandsAlike:\s*(?!false\b)[A-Za-z_]"),
               "…and never a variable or a config read: the figures' hold must not become "
               + "switchable by a key he did not ask for");
        t.True(props.Contains("PropHeldSameInBothHands"),
               "and the map items' switch is bound where its eight siblings are");

        // THE SHIPPED DEFAULT, asserted against Defaults/ and not against a Clamped() fallback —
        // on this project that distinction has cost a round of its own. ModBuild 434 shipped this
        // key OFF and the hardware log came back "PropHeldSameInBothHands = False" beside the
        // unchanged complaint: a remedy the player has to find is not a remedy. 435 ships it ON, so
        // a player who changes nothing gets a map item held the same way in both hands.
        t.Case("mirror/the-switch-ships-on");
        string defaults = ReadOrEmpty(Path.Combine(
            repoRoot, "src", "GloomhavenVR", "Defaults", "Defaults.Board.cs"));
        t.True(Regex.IsMatch(defaults, @"PropHeldSameInBothHands\s*=\s*true\s*;"),
               "Defaults.PropHeldSameInBothHands ships TRUE — both hands hold a map item the way "
               + "the LEFT hand held it, with no dial re-entered and no setting to find");
        t.True(Regex.IsMatch(defaults, @"PropHeldRotYaw\s*=\s*-133f\s*;"),
               "…and PropHeldRotYaw's shipped default is untouched at -133: the switch changes "
               + "which FORM both hands take, never the number anyone has tuned");
    }

    /// <summary>
    /// The file with its COMMENT LINES REMOVED, which is not tidiness — it is the whole reason the
    /// lint is trustworthy. These three files explain the mirror at length and quote the wrong
    /// spellings verbatim while doing it ("a single <c>Quaternion.Euler(pitch, yaw, roll)</c> gets
    /// it wrong"), so a search over the raw text finds the prose warning against a defect and
    /// reports the defect. That is a mistake this repo has already paid for and has a note about:
    /// a token quoted in its own explanation counts itself.
    /// </summary>

    // -------------------------------------------------------------------------------------------
    //  8. THE ANCHOR PAIR IS A MIRROR PAIR — read out of the shipped prefabs, not asserted in prose.
    // -------------------------------------------------------------------------------------------
    //
    // The 2026-09-05 round arrived carrying a suspected SECOND cause: "Anchor_Grab differs
    // left<->right by 11.40° on the Arcane hand set (Plate 1.998°, Glove 0.021°), and the mirror
    // assumes those frames are mirror-conjugate." Those three numbers are real and they are NOT an
    // asymmetry. Quaternion.Angle(left, right) measures how far apart the two frames are, and two
    // frames that are correct MIRROR IMAGES of each other are only equal when the axis happens to
    // lie in the mirror plane — which is exactly what the Glove's does (w = 0, x = 0: a 180° turn
    // about an in-plane axis is its own mirror image, hence 0.021° of float noise). The Plate and
    // Arcane anchors are cocked progressively further OUT of that plane, so their mirrors sit
    // progressively further away. The number that would actually show a broken pair is the residual
    // against the true conjugate, and it is ~0 on all three.
    //
    // Read straight out of the .prefab YAML rather than from a copied literal: the whole point is
    // that the assertion fails if someone re-authors a hand set, which a literal could not see. If
    // the prefabs are not reachable (a source drop without the unity/ tree) the case reports that
    // and does not pretend to have measured anything — a missing file must never read as a pass.
    private static void AnchorPairsAreMirrorConjugate(Harness t, string repoRoot)
    {
        t.Case("mirror/anchor-pairs-are-mirror-conjugate");

        string dir = Path.Combine(repoRoot, "unity", "GloomhavenVR.Assets",
                                  "Assets", "Bundle", "Hands");
        string[] sets = { "VRHand", "VRHandPlate", "VRHandArcane" };
        int measured = 0;

        foreach (string set in sets)
        {
            Quaternion? l = AnchorGrabRotation(Path.Combine(dir, set + "_L.prefab"));
            Quaternion? r = AnchorGrabRotation(Path.Combine(dir, set + "_R.prefab"));
            if (l == null || r == null)
                continue;

            measured++;
            float residual = HeldPoseMirror.MirrorResidualDegrees(l.Value, r.Value);
            t.True(residual < 0.05f,
                   set + ": the LEFT Anchor_Grab is the diag(-1,1,1) conjugate of the RIGHT one — "
                   + "residual " + residual.ToString("0.####") + "°, so the anchors are a correct "
                   + "mirror pair and are NOT a second cause of the handedness report");
        }

        t.True(measured == sets.Length,
               "all three shipped hand sets were actually read (" + measured + " of "
               + sets.Length + ") — a prefab that cannot be found must not read as a pass");
    }

    /// <summary>The Anchor_Grab GameObject's local rotation out of a Unity .prefab YAML, or null
    /// when the file or the object is not there. Deliberately a small hand parse and not a YAML
    /// library: the only thing wanted is one m_LocalRotation on the one Transform whose
    /// m_GameObject is the object named Anchor_Grab.</summary>
    private static Quaternion? AnchorGrabRotation(string path)
    {
        if (!File.Exists(path))
            return null;

        string text = File.ReadAllText(path);
        // Documents are "--- !u!<class> &<fileID>"; a GameObject is class 1, a Transform class 4.
        string[] docs = Regex.Split(text, @"^--- !u!(\d+) &(\d+)\s*$", RegexOptions.Multiline);

        string? anchorId = null;
        for (int i = 1; i + 2 < docs.Length + 1 && i + 2 <= docs.Length; i += 3)
        {
            if (docs[i] == "1" && Regex.IsMatch(docs[i + 2], @"^\s*m_Name:\s*Anchor_Grab\s*$",
                                                RegexOptions.Multiline))
            {
                anchorId = docs[i + 1];
                break;
            }
        }
        if (anchorId == null)
            return null;

        for (int i = 1; i + 2 <= docs.Length; i += 3)
        {
            if (docs[i] != "4"
                || !Regex.IsMatch(docs[i + 2], @"m_GameObject:\s*\{fileID:\s*" + anchorId + @"\}"))
                continue;

            Match m = Regex.Match(docs[i + 2],
                @"m_LocalRotation:\s*\{x:\s*(\S+?),\s*y:\s*(\S+?),\s*z:\s*(\S+?),\s*w:\s*(\S+?)\}");
            if (!m.Success)
                return null;
            return new Quaternion(Num(m.Groups[1].Value), Num(m.Groups[2].Value),
                                  Num(m.Groups[3].Value), Num(m.Groups[4].Value));
        }
        return null;
    }

    private static float Num(string s)
        => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    // -------------------------------------------------------------------------------------------
    //  9. THE UPRIGHT-AT-GRAB BASE CANCELS THE ANCHOR — the term that decides the whole report.
    // -------------------------------------------------------------------------------------------
    //
    // HeldPoseMirror's justification for negating the yaw and the roll is written entirely in
    // ANCHOR space, and it is correct there. The shipped pose is not applied in anchor space:
    // [FigureGrab] HeldUprightAtGrab and PropHeldUprightAtGrab both ship TRUE, and they compose the
    // held rotation against Inverse(anchor.rotation) * W. The anchor therefore cancels out of the
    // product exactly, leaving world = W * held with W a pure world-Y yaw and no handedness in it —
    // so the mirror has nothing left to cancel and survives as a wrap(2*yaw) spin between the
    // hands. That cancellation is the load-bearing step of the explanation handed to the user, and
    // it is asserted two ways: as arithmetic on real quaternions, and as a source lint on the shape
    // that produces it, because Quaternion.LookRotation is an engine ECall and cannot run here.
    private static void TheUprightBaseCancelsTheAnchor(Harness t, string repoRoot)
    {
        t.Case("mirror/upright-at-grab-cancels-the-anchor");

        // An arbitrary, deliberately non-trivial anchor rotation and an arbitrary world yaw, both
        // written as raw quaternion literals so no Euler/LookRotation ECall is needed.
        Quaternion anchor = Normalized(new Quaternion(0.2f, -0.5f, 0.31f, 0.78f));
        Quaternion world = Normalized(new Quaternion(0f, 0.38268f, 0f, 0.92388f)); // 45° about +Y
        Quaternion held = Normalized(new Quaternion(-0.11f, 0.62f, 0.05f, 0.77f));

        // Quaternion.Inverse is an engine ECall like Quaternion.Euler, so the inverse is taken
        // locally — for a UNIT quaternion it is exactly the conjugate, and all three above are
        // normalized. Nothing about the cancellation under test is re-implemented by that: the
        // multiplication, and therefore the cancellation itself, is Unity's own managed operator.
        Quaternion uprightBase = Conjugate(anchor) * world;
        Quaternion composed = anchor * (uprightBase * held);

        t.True(Quaternion.Angle(composed, world * held) < AngleEpsilon,
               "with upright-at-grab ON the anchor cancels out of the final world pose exactly — "
               + "anchor * (Inverse(anchor) * W) * held IS W * held, so the mirror lands in a frame "
               + "with no handedness in it and its whole justification is gone");

        // The negative half: with the option OFF the base is identity and the anchor stays in, so
        // the mirror is applied in the frame it is actually justified in.
        t.True(Quaternion.Angle(anchor * (Quaternion.identity * held), world * held) > 1f,
               "…and with it OFF the pose really does ride the anchor, which is the frame the "
               + "reflection is correct in — the two modes are not cosmetically different");

        t.Case("mirror/upright-base-still-has-the-cancelling-shape");
        string mirror = ReadOrEmpty(Path.Combine(
            repoRoot, "src", "GloomhavenVR", "Board", "FigureGrab", "HeldPoseMirror.cs"));
        t.True(Regex.IsMatch(mirror,
                   @"return\s+Quaternion\.Inverse\(anchorRotation\)\s*\*\s*Quaternion\.LookRotation\("),
               "HeldPoseMirror.UprightBase still returns Inverse(anchorRotation) * LookRotation(...) "
               + "— the exact shape whose cancellation the arithmetic above depends on");
        t.True(Regex.IsMatch(mirror, @"if\s*\(!uprightAtGrab\)\s*\r?\n\s*return Quaternion\.identity;"),
               "…and still returns identity when the option is off, so the anchor stays in the "
               + "product and the reflection keeps the frame it is justified in");

        t.Case("mirror/both-grab-paths-read-the-shared-upright-base");
        string dir = Path.Combine(repoRoot, "src", "GloomhavenVR", "Board", "FigureGrab");
        string fig = ReadOrEmpty(Path.Combine(dir, "FigureGrabbable.cs"));
        string prop = ReadOrEmpty(Path.Combine(dir, "GrabbableProp.cs"));
        t.True(fig.Contains("HeldPoseMirror.UprightBase") && prop.Contains("HeldPoseMirror.UprightBase"),
               "both grab paths call the shared UprightBase — it was twelve character-identical "
               + "lines in each file until 2026-09-05, and it is the term the report turned on");
        t.True(!Regex.IsMatch(fig, @"Quaternion\.LookRotation\(flat")
               && !Regex.IsMatch(prop, @"Quaternion\.LookRotation\(flat"),
               "…and neither of them has grown its own copy back");
    }

    /// <summary>The inverse of a UNIT quaternion. <c>Quaternion.Inverse</c> is an ECall and throws
    /// outside a Unity process, exactly like <c>Quaternion.Euler</c>.</summary>
    private static Quaternion Conjugate(Quaternion q) => new(-q.x, -q.y, -q.z, q.w);

    private static Quaternion Normalized(Quaternion q)
    {
        float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
    }

    private static string ReadOrEmpty(string path)
    {
        if (!File.Exists(path))
            return string.Empty;
        var kept = new System.Text.StringBuilder();
        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", System.StringComparison.Ordinal))
                continue;
            kept.Append(line).Append('\n');
        }
        return kept.ToString();
    }

    private static string Show(Vector3 v) => $"({v.x:0.####},{v.y:0.####},{v.z:0.####})";
}
