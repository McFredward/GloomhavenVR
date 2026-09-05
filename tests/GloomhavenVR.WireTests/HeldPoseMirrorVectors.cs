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
//   3. THE FIXED POINTS, which are the whole explanation for the report. The mirror's visible cost
//      is set by the YAW alone, and it vanishes at 0° and at ±180°. A figure pose tuned near a
//      fixed point looks identical in both hands while a map-item pose tuned at the shipped −133°
//      swings ~94°, with not one line of code differing between them. If that ever stopped being
//      true, the explanation handed to the user would be wrong.
//
//   4. THE SWITCH. [FigureGrab] PropHeldMirrorHands off must be EXACTLY "both hands take the
//      authored numbers", including that the RIGHT hand does not move at all — the promise the
//      key's own description makes to a user who has already tuned one hand.
//
// WHY THE EULER CONVERSION IS LOCAL, and why that is honest rather than a mirrored
// re-implementation. `Quaternion.Euler` is one of the few members of UnityEngine.CoreModule that
// is NOT managed — it is an ECall into the engine (`Internal_FromEulerRad`) and throws a
// SecurityException outside a Unity process, so `HeldPoseMirror.Upright` cannot be called from
// here at all. What CAN be called is everything the mirror actually decides: `Sign`, `Angle` and
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
        SwitchedOffMeansAuthored(t);
        OneDefinitionForBothPaths(t, repoRoot);
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
    private static Quaternion Upright(bool left, bool mirrored, float pitch, float yaw, float roll)
        => Euler(pitch, 0f, HeldPoseMirror.Angle(left, mirrored, roll))
           * Euler(0f, HeldPoseMirror.Angle(left, mirrored, yaw), 0f);

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
            Quaternion right = Upright(left: false, mirrored: true, pitch, yaw, roll);
            Quaternion mirrored = Upright(left: true, mirrored: true, pitch, yaw, roll);

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
        Quaternion right = Upright(left: false, mirrored: true, PropPitch, 0f, 0f);
        Quaternion left = Upright(left: true, mirrored: true, PropPitch, 0f, 0f);
        t.True(Quaternion.Angle(right, left) < AngleEpsilon,
               "with only a pitch dialled in, both hands get the same rotation — a pitch is a "
               + "rotation about the mirror axis and is mirror-invariant");

        // The palm pose is pitch-only by construction, so it is the same rotation on both hands
        // whatever the mirror says. Stated as its own assertion because the palm pose takes no
        // side at all and that has to stay a deliberate property, not an oversight.
        t.True(Quaternion.Angle(Palm(PropPitch),
                                Upright(left: false, mirrored: true, PropPitch, 0f, 0f))
               < AngleEpsilon,
               "the flat palm pose is the upright pose with yaw and roll at zero");
    }

    // -------------------------------------------------------------------------------------------
    //  3. THE OFFSET — sideways flips, out-of-palm and toward-the-fingertips do not.
    // -------------------------------------------------------------------------------------------
    private static void OffsetFlipsOnlySideways(Harness t)
    {
        t.Case("mirror/offset-flips-x-only");

        Vector3 right = HeldPoseMirror.Offset(left: false, mirrored: true, PropSide, PropUp, PropForward);
        Vector3 left = HeldPoseMirror.Offset(left: true, mirrored: true, PropSide, PropUp, PropForward);

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
        Quaternion spun = Upright(left: false, mirrored: true, PropPitch, PropYaw, PropRoll);
        Quaternion unspun = Upright(left: false, mirrored: true, PropPitch, 0f, PropRoll);
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
        => Quaternion.Angle(Upright(left: false, mirrored: true, pitch, yaw, roll),
                            Upright(left: true, mirrored: true, pitch, yaw, roll));

    // -------------------------------------------------------------------------------------------
    //  6. THE SWITCH — off means "both hands take the authored numbers", right hand unmoved.
    // -------------------------------------------------------------------------------------------
    private static void SwitchedOffMeansAuthored(Harness t)
    {
        t.Case("mirror/switched-off-is-the-authored-pose-on-both-hands");

        Quaternion authored = Upright(left: false, mirrored: true,
                                                     PropPitch, PropYaw, PropRoll);

        // THE PROMISE THE KEY'S DESCRIPTION MAKES, both halves of it.
        t.True(Quaternion.Angle(Upright(left: false, mirrored: false,
                                                       PropPitch, PropYaw, PropRoll), authored)
               < AngleEpsilon,
               "with PropHeldMirrorHands OFF the RIGHT hand does not move at all — it is the hand "
               + "the dials are authored for either way");
        t.True(Quaternion.Angle(Upright(left: true, mirrored: false,
                                                       PropPitch, PropYaw, PropRoll), authored)
               < AngleEpsilon,
               "…and the LEFT hand becomes identical to it");

        Vector3 offRight = HeldPoseMirror.Offset(left: false, mirrored: false, PropSide, PropUp, PropForward);
        Vector3 offLeft = HeldPoseMirror.Offset(left: true, mirrored: false, PropSide, PropUp, PropForward);
        t.True((offRight - offLeft).sqrMagnitude < VectorEpsilon * VectorEpsilon
               && Mathf.Abs(offRight.x - PropSide) < VectorEpsilon,
               "the offset follows the switch with the rotation — one key, not a half-mirror");

        // AND THE ARITHMETIC THE DESCRIPTION HANDS A USER WHO TUNED THE *LEFT* HAND: negate the
        // three mirror terms once, and both hands land where his left hand was.
        t.Case("mirror/negating-the-three-terms-reproduces-a-left-hand-tuning");
        Quaternion tunedOnLeft = Upright(left: true, mirrored: true,
                                                        PropPitch, PropYaw, PropRoll);
        Quaternion bothAfterNegating = Upright(left: false, mirrored: false,
                                                              PropPitch, -PropYaw, -PropRoll);
        t.True(Quaternion.Angle(tunedOnLeft, bothAfterNegating) < AngleEpsilon,
               "negating PropHeldRotYaw and PropHeldRotRoll with the mirror off gives both hands "
               + "exactly the pose the LEFT hand had with the mirror on");
        Vector3 sideAfterNegating = HeldPoseMirror.Offset(left: false, mirrored: false,
                                                          -PropSide, PropUp, PropForward);
        Vector3 leftUnderMirror = HeldPoseMirror.Offset(left: true, mirrored: true,
                                                        PropSide, PropUp, PropForward);
        t.True((sideAfterNegating - leftUnderMirror).sqrMagnitude < VectorEpsilon * VectorEpsilon,
               "…and negating PropHeldOffsetSide does the same for the position");
    }

    // -------------------------------------------------------------------------------------------
    //  7. ONE DEFINITION, BOTH PATHS — a source lint, and the only assertion here that can see the
    //     thing the the 2026-09-05 handedness round round was actually about.
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
        // The other half of the the 2026-09-05 handedness round ruling, and the one that protects a tuning the user
        // has already accepted: the map items got a switch, the figures did not. If a dial ever
        // appears at the figure call sites, his figure hold has become changeable by a key he did
        // not ask for.
        t.True(Regex.Matches(figures, @"HeldPoseMirror\.\w+\([^;]*mirrored:\s*true").Count >= 4,
               "every figure call site passes mirrored: true as a LITERAL, so the figures' hold is "
               + "bit-identical to what it was before the mirror was shared");
        t.True(props.Contains("PropHeldMirrorHands"),
               "and the map items' switch is bound where its eight siblings are");
    }

    /// <summary>
    /// The file with its COMMENT LINES REMOVED, which is not tidiness — it is the whole reason the
    /// lint is trustworthy. These three files explain the mirror at length and quote the wrong
    /// spellings verbatim while doing it ("a single <c>Quaternion.Euler(pitch, yaw, roll)</c> gets
    /// it wrong"), so a search over the raw text finds the prose warning against a defect and
    /// reports the defect. That is a mistake this repo has already paid for and has a note about:
    /// a token quoted in its own explanation counts itself.
    /// </summary>
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
