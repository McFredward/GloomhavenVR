// WHERE THE FIXIERT BOARD IS — the other half of the board's size, driven zoom by zoom.
//
// THE REPORT (ModBuild 159 hardware, 2026-08-18, verbatim):
//
//   "1) Das Board zoomed immer noch im Fixiert modus mit - das soll nicht sein. Fixiert heißt in
//    jeglicher hinsicht fixiert und fix, EGAL wie man zoomed oder sich bewegt auch wenn man es in
//    einer Hand festhält."
//
// "immer noch" again, and this time the mod's own instrument had signed off on the build: the
// BOARD SIZE diagnostic measured the board's apparent WIDTH, found it constant across a zoom
// sweep, and printed so — in the same session, in the same log, in which the tester was watching
// the board grow. Both were right. What the eye judges is ANGULAR size, and
//
//     angular size = size ÷ distance
//
// so a build that freezes the numerator and lets the denominator sweep is indistinguishable, from
// inside a headset, from one that freezes nothing. ModBuild 159 pinned the board at fixed GAME
// WORLD coordinates, and a zoom rescales the PLAYER — so the board's distance in player metres
// moved with every pinch while its apparent width sat perfectly still.
//
// HIS OWN LOG MEASURES IT, and this file is written in those numbers:
//   * 146 "Board pose [pinned board size held against the table zoom]" lines report the SAME world
//     position, (24.50, 7.83, 16.39), and the same own scale 0.74, from L2677 to L3664;
//   * across exactly that span the BOARD SIZE lines report rig scales from ×19.01 to ×83.53.
//     A board at a fixed world spot, seen by a player who grew 4.39×, is 4.39× closer in the only
//     units the player has. That is the whole defect, and the size diagnostic could not see it;
//   * the SECOND defect is one identity apart: L3467 prints rig ×64.79 and L3474 prints "parent
//     chain ×64.79 ÷ rig ×68.50" — the pinned holder's scale at frame N is the rig's scale at
//     frame N−1, because it was re-asserted by hand in CardsDriver.Update, one phase BEFORE
//     WorldGrab.Update writes the new rig scale. Over 223 BOARD SIZE lines the anchor ratio ran
//     0.89…1.11 (only 79 read 1.00): the board breathed ±10 % through every pinch.
//
// WHY THIS IS A TEST AND NOT A HARDWARE ROUND. Neither term is observable from outside a headset,
// and the instrument that was supposed to catch them agreed with the broken build for two rounds.
// The arithmetic for both — the player-frame conversion, the distance, the angle — was put into
// src/GloomhavenVR/Cards/BoardSizeFrame.cs, free of everything but Mathf and the plain Vector3/
// Quaternion value types, precisely so it could be linked in here and driven at his own recorded
// rig scales. The PHASE the anchor is sampled in cannot be expressed as arithmetic at all, so it
// is pinned as a source property at the bottom of this file.

using System;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class BoardPinFrameVectors
{
    // ---- his session, as the log recorded it --------------------------------------------------

    /// <summary>The rig scales the BOARD SIZE lines sampled while the pinned board's world
    /// position never moved (L2677–L3664: world pos (24.50, 7.83, 16.39), own scale 0.74).</summary>
    private static readonly float[] PinnedSpanRigScales =
        { 19.01f, 24.30f, 31.44f, 34.83f, 42.71f, 55.02f, 64.79f, 68.50f, 83.53f };

    /// <summary>The board he was looking at during that span: own localScale 0.739 at 42.9 cm of
    /// apparent width (L3467).</summary>
    private const float ApparentWidth = 0.429f;

    /// <summary>The identity that proves the one-frame lag: L3467's rig scale is L3474's parent
    /// chain.</summary>
    private const float LaggedParent = 64.79f;
    private const float LiveRig = 68.50f;

    private static float Near(float a, float b) => Math.Abs(a - b);

    /// <summary>A yaw rotation, built from the managed constructor rather than
    /// <c>Quaternion.Euler</c>: Euler is an engine ECall and these vectors run OUTSIDE Unity, where
    /// every native entry point throws. (This is also why <c>BoardSizeFrame.ToPlayerFrame</c> takes
    /// the conjugate instead of calling <c>Quaternion.Inverse</c>.)</summary>
    private static Quaternion Yaw(float degrees)
    {
        float half = degrees * 0.5f * Mathf.Deg2Rad;
        return new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
    }

    public static void Run(Harness t, string repoRoot)
    {
        TheOneFrameLag(t);
        TheDistanceTermWasNeverFrozen(t);
        TheRigLocalPinHoldsBothTerms(t);
        WalkingStillChangesWhatYouSee(t);
        AngularSizeIsAPureRatio(t);
        DegenerateInputsRefuse(t);
        SourceLint(t, repoRoot);
    }

    // ------------------------------------------------------------------------------------------
    //  1. DEFECT (A): THE ANCHOR WAS ONE FRAME OLD, and the log proves it by identity.
    // ------------------------------------------------------------------------------------------
    private static void TheOneFrameLag(Harness t)
    {
        t.Case("PF1. the pinned anchor lagged the rig by exactly one frame (L3467/L3474)");

        // What the log printed, recomputed from the two numbers it printed beside it.
        float lagged = BoardSizeFrame.AnchorRatio(LaggedParent, LiveRig);
        t.True(Near(lagged, 0.945839f) < 1e-5f,
            $"L3474's anchor is 64.79/68.50 = 0.95 (got {lagged:F6})");

        // And what that costs the player: the board is drawn at the anchor ratio times the size it
        // is supposed to be. 5.4 % small on that one frame — and the session's extremes were 0.89
        // and 1.11, i.e. a board breathing ±10 % through every zoom gesture. Nothing about this is
        // subtle from inside a headset; it is exactly the "zoomed mit" of the report.
        float drawn = BoardSizeFrame.ApparentWidth(0.739f, LaggedParent, LiveRig);
        float wanted = BoardSizeFrame.ApparentWidth(0.739f, LiveRig, LiveRig);
        t.True(drawn < wanted * 0.95f,
            $"a lagging anchor draws the board SMALL ({drawn * 100f:F1} cm vs {wanted * 100f:F1} cm)");
        t.True(Near(BoardSizeFrame.ApparentWidth(0.739f, 0.89f * LiveRig, LiveRig) / wanted, 0.89f) < 1e-4f,
            "the session's low extreme (anchor 0.89) is an 11 % undersized board");
        t.True(Near(BoardSizeFrame.ApparentWidth(0.739f, 1.11f * LiveRig, LiveRig) / wanted, 1.11f) < 1e-4f,
            "the session's high extreme (anchor 1.11) is an 11 % oversized board");

        // THE CURE, stated as arithmetic: the anchor and the rig read in the SAME frame are the
        // same float, so the ratio is exactly one — at every scale his session reached. This is
        // what the LateUpdate seat buys, and it is why no epsilon appears in this assertion.
        foreach (float rig in PinnedSpanRigScales)
            t.True(BoardSizeFrame.AnchorRatio(rig, rig) == 1f,
                $"rig ×{rig}: an anchor sampled in the same frame is EXACTLY 1.00");
    }

    // ------------------------------------------------------------------------------------------
    //  2. DEFECT (B): THE DISTANCE TERM WAS NEVER FROZEN — the report, reproduced.
    // ------------------------------------------------------------------------------------------
    private static void TheDistanceTermWasNeverFrozen(Harness t)
    {
        t.Case("PF2. a WORLD-pinned board changes angular size with the zoom (the 2026-08-18 report)");

        // The ModBuild 159 arrangement: the board stands at a fixed world spot, the player stands
        // where they stand, and the zoom rescales the player. Put the board an ordinary 0.60 player
        // metres from the head at the low end of his sweep — 0.60 × 19.01 = 11.406 world units —
        // and hold BOTH world quantities still, which is exactly what that build did.
        const float worldDistance = 11.406f;
        BoardSizeFrame.TryPlayerDistance(Vector3.zero, new Vector3(worldDistance, 0f, 0f),
                                         PinnedSpanRigScales[0], out float dLo);
        BoardSizeFrame.TryPlayerDistance(Vector3.zero, new Vector3(worldDistance, 0f, 0f),
                                         83.53f, out float dHi);
        t.True(Near(dLo, 0.60f) < 1e-3f, $"at rig ×19.01 the board is 0.60 m away (got {dLo:F3})");
        t.True(Near(dHi, 0.1365f) < 1e-3f, $"at rig ×83.53 the SAME board is 0.14 m away (got {dHi:F3})");
        t.True(Near(dLo / dHi, 83.53f / 19.01f) < 1e-3f,
            "the distance in player metres moved by the full zoom factor (4.39x) — the term the "
            + "size diagnostic never measured");

        // With the apparent WIDTH held constant (which ModBuild 159 did do, and logged), the angle
        // the board subtends therefore sweeps with the zoom. This assertion is the user's sentence
        // in numbers: the board zooms along.
        float aLo = BoardSizeFrame.AngularWidthDegrees(ApparentWidth, dLo);
        float aHi = BoardSizeFrame.AngularWidthDegrees(ApparentWidth, dHi);
        t.True(Near(aLo, 39.5f) < 0.5f, $"at 0.60 m a 42.9 cm board subtends ~39.5° (got {aLo:F1}°)");
        t.True(Near(aHi, 115.0f) < 1.0f, $"at 0.14 m the SAME board subtends ~115° (got {aHi:F1}°)");
        t.True(aHi > 2f * aLo,
            $"the board more than DOUBLED in the eye across his sweep ({aLo:F1}° → {aHi:F1}°) while "
            + "the mod's own diagnostic reported its size as unchanged");
    }

    // ------------------------------------------------------------------------------------------
    //  3. THE FIX: A PIN STORED IN THE PLAYER'S FRAME HOLDS BOTH TERMS AT ONCE.
    // ------------------------------------------------------------------------------------------
    private static void TheRigLocalPinHoldsBothTerms(Harness t)
    {
        t.Case("PF3. a rig-local pin: the board's distance and angular size survive the whole sweep");

        // The pin, as the transform hierarchy now stores it: a constant offset in the player's own
        // frame. 35 cm to the right, 25 cm down, 55 cm forward — a board on the desk in front of
        // you. The head sits at the rig origin (the player is standing still; PF4 covers walking).
        var pinInPlayerFrame = new Vector3(0.35f, -0.25f, 0.55f);
        var headInPlayerFrame = Vector3.zero;
        float expectedDistance = pinInPlayerFrame.magnitude;
        float expectedAngle = BoardSizeFrame.AngularWidthDegrees(ApparentWidth, expectedDistance);

        // Sweep everything the rig does to a player who never physically moves: the table zoom
        // (his own recorded scales), a snap turn (yaw), a world grab / teleport / recentre (rig
        // position). Under the world pin ANY of these moved the board in the eye. Under the rig
        // pin none of them may.
        var seen = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < PinnedSpanRigScales.Length; i++)
        {
            float scale = PinnedSpanRigScales[i];
            Quaternion rot = Yaw(i * 45f);                             // snap turns
            var pos = new Vector3(i * 7.5f, i * -1.25f, i * 3.5f);     // grab / teleport / recentre

            Vector3 boardWorld = BoardSizeFrame.ToWorld(pos, rot, scale, pinInPlayerFrame);
            Vector3 headWorld = BoardSizeFrame.ToWorld(pos, rot, scale, headInPlayerFrame);
            seen.Add(boardWorld);

            t.True(BoardSizeFrame.TryPlayerDistance(headWorld, boardWorld, scale, out float d),
                $"rig ×{scale}: the distance resolves");
            t.True(Near(d, expectedDistance) < 1e-3f,
                $"rig ×{scale}: the board stays {expectedDistance:F3} m away in PLAYER metres (got {d:F3})");
            t.True(Near(BoardSizeFrame.AngularWidthDegrees(ApparentWidth, d), expectedAngle) < 0.05f,
                $"rig ×{scale}: the board subtends the same {expectedAngle:F1}° it always did");

            // The round trip, which is the invariant stated the other way: whatever the rig does,
            // reading the board's world position back in the player's frame returns the pin.
            Vector3 back = BoardSizeFrame.ToPlayerFrame(pos, rot, scale, boardWorld);
            t.True((back - pinInPlayerFrame).magnitude < 1e-3f,
                $"rig ×{scale}: the pin reads back as itself in the player's frame");
        }

        // AND THE TEST IS NOT VACUOUS: the board's WORLD pose moved a long way while all of the
        // above held. That motion is not a defect to be fixed — it is what keeps the board still in
        // the player's eye, and it is the reason the freeze sentinel had to stop diffing world
        // poses (PlayTray.2.Watchdog.cs) and the reason the MP wire now sends a moving board.
        t.True((seen[seen.Count - 1] - seen[0]).magnitude > 10f,
            "the pinned board's WORLD position swept by more than 10 units across the same test");
    }

    // ------------------------------------------------------------------------------------------
    //  4. THE CONSTRAINT ON THE FIX: WALKING MUST STILL WORK.
    // ------------------------------------------------------------------------------------------
    private static void WalkingStillChangesWhatYouSee(Harness t)
    {
        t.Case("PF4. physically walking DOES change the board's distance — it must");

        // A rig-local pin nails the board to the play space, not to the head. Stepping 40 cm
        // towards it therefore brings it 40 cm closer and makes it bigger, exactly as a real object
        // on a real desk does. A fix that also froze THIS would have produced a board glued to the
        // face, which is neither mode and is what "fixiert" must never mean.
        var pin = new Vector3(0f, 0f, 1.00f);
        const float scale = 31.44f;
        Vector3 boardWorld = BoardSizeFrame.ToWorld(Vector3.zero, Quaternion.identity, scale, pin);

        Vector3 standing = BoardSizeFrame.ToWorld(Vector3.zero, Quaternion.identity, scale, Vector3.zero);
        Vector3 stepped = BoardSizeFrame.ToWorld(Vector3.zero, Quaternion.identity, scale,
                                                 new Vector3(0f, 0f, 0.40f));
        BoardSizeFrame.TryPlayerDistance(standing, boardWorld, scale, out float before);
        BoardSizeFrame.TryPlayerDistance(stepped, boardWorld, scale, out float after);
        t.True(Near(before, 1.00f) < 1e-3f, $"standing still: 1.00 m away (got {before:F3})");
        t.True(Near(after, 0.60f) < 1e-3f, $"after a 40 cm step: 0.60 m away (got {after:F3})");
        t.True(BoardSizeFrame.AngularWidthDegrees(ApparentWidth, after)
               > BoardSizeFrame.AngularWidthDegrees(ApparentWidth, before) * 1.5f,
            "walking towards the board makes it bigger in the eye, as a real object would");
    }

    // ------------------------------------------------------------------------------------------
    //  5. THE ANGLE ITSELF.
    // ------------------------------------------------------------------------------------------
    private static void AngularSizeIsAPureRatio(Harness t)
    {
        t.Case("PF5. angular width: the one measure that is free of the player's scale");

        // A 64 cm board one metre away, worked out by hand: 2·atan(0.32/1.00) = 35.489°.
        t.True(Near(BoardSizeFrame.AngularWidthDegrees(0.64f, 1.00f), 35.4894f) < 1e-3f,
            "a 64 cm board at 1 m subtends 35.49°");
        // Half the board at half the distance is the same picture. This is the property that makes
        // the measure worth logging: it cannot be moved by anything that rescales the player, so a
        // line of it from either side of a zoom is a decisive comparison.
        t.True(Near(BoardSizeFrame.AngularWidthDegrees(0.32f, 0.50f),
                    BoardSizeFrame.AngularWidthDegrees(0.64f, 1.00f)) < 1e-4f,
            "halving both terms leaves the angle untouched");
        t.True(Near(BoardSizeFrame.AngularWidthDegrees(2f, 1f), 90f) < 1e-3f,
            "a board twice as wide as its distance fills a right angle");
        t.True(BoardSizeFrame.AngularWidthDegrees(0.64f, 0.5f)
               > BoardSizeFrame.AngularWidthDegrees(0.64f, 1.0f),
            "closer is bigger");
    }

    // ------------------------------------------------------------------------------------------
    //  6. DEGENERATE INPUTS REFUSE RATHER THAN LIE.
    // ------------------------------------------------------------------------------------------
    private static void DegenerateInputsRefuse(Harness t)
    {
        t.Case("PF6. no rig, a zero scale or an infinity yields a refusal, never a number");

        t.True(!BoardSizeFrame.TryPlayerDistance(Vector3.zero, Vector3.one, 0f, out _),
            "a zero rig scale refuses (there is no player frame to measure in)");
        t.True(!BoardSizeFrame.TryPlayerDistance(Vector3.zero, Vector3.one, float.PositiveInfinity, out _),
            "an infinite rig scale refuses");
        t.True(BoardSizeFrame.TryPlayerDistance(Vector3.zero, Vector3.zero, 12f, out float zero)
               && zero == 0f,
            "a board AT the head is zero metres away, and that is a legal answer");
        t.True(BoardSizeFrame.AngularWidthDegrees(0.64f, 0f) == 0f,
            "a zero distance has no meaningful angular width — 0, not an infinity");
        t.True(BoardSizeFrame.AngularWidthDegrees(0f, 1f) == 0f, "a zero-width board subtends nothing");
    }

    // ------------------------------------------------------------------------------------------
    //  7. SOURCE LINT — the two properties no arithmetic can express.
    // ------------------------------------------------------------------------------------------
    private static void SourceLint(Harness t, string repoRoot)
    {
        t.Case("PF7. the pin is stored rig-locally and sampled in the LAST phase before rendering");

        string frame = File.ReadAllText(Path.Combine(
            repoRoot, "src", "GloomhavenVR", "Cards", "TrayPinFrame.cs"));

        // THE PHASE IS THE FIX for defect (A), and it is invisible to every vector above: the same
        // arithmetic run in Update reproduces the one-frame lag exactly, with no test failing and
        // nothing logged. So the phase is pinned as text — LateUpdate, at an execution order that
        // puts it after the rig's own writers (WorldGrab.Update, and VRRigDriver.LateUpdate's
        // world-tilt heal at the default order 0).
        t.True(Regex.IsMatch(frame, @"\[DefaultExecutionOrder\(\s*20000\s*\)\]"),
            "TrayPinFrame still pins its execution order (20000: after every default-order writer, "
            + "before PerfFrameSplit's 30000 end-of-logic marker)");
        t.True(Regex.IsMatch(frame, @"private\s+void\s+LateUpdate\(\)"),
            "TrayPinFrame still seats the anchor in LateUpdate — an Update-phase seat IS the "
            + "one-frame lag of L3467/L3474 and would pass every vector in this file");
        t.True(Regex.IsMatch(frame, @"SetPositionAndRotation\(pos,\s*rig\.rotation\)")
               && Regex.IsMatch(frame, @"localScale\s*=\s*Vector3\.one\s*\*\s*scale"),
            "TrayPinFrame still copies the rig's POSITION, ROTATION and SCALE — freezing a subset "
            + "is how the distance term was left out in the first place");

        string watchdog = File.ReadAllText(Path.Combine(
            repoRoot, "src", "GloomhavenVR", "Cards", "PlayTray.2.Watchdog.cs"));

        // The hand arithmetic must stay gone. Both of these lines were the ModBuild 159 mechanism;
        // if either comes back the anchor is being re-derived in the Update phase again.
        t.True(!watchdog.Contains("_pinRoot.localScale = Vector3.one * live"),
            "the Update-phase holder rescale is still gone (it was the one-frame lag)");
        t.True(!watchdog.Contains("_rigLocalPinPos"),
            "the hand-cached rig-relative pin is still gone (the hierarchy stores it now)");
        t.True(watchdog.Contains("_pinFrame.Seat()"),
            "the watchdog still seats the pin frame before it reads the anchor");

        // The freeze sentinel must measure the frame the ruling freezes. Diffing the WORLD pose of
        // a rig-local board reports every zoom and every step as a violation, which is how a
        // sentinel gets switched off.
        t.True(watchdog.Contains("TryGetPlayerFramePose"),
            "the PINNED freeze sentinel diffs the pose in the PLAYER's frame, not the world's");

        // And the diagnostic must print the term it was blind to. This is the instrument that
        // agreed with two broken builds; a size-only line here is the defect, not the report of it.
        t.True(watchdog.Contains("DISTANCE") && watchdog.Contains("subtends"),
            "the BOARD SIZE line reports the board's distance in player metres and the angle it "
            + "subtends — the terms whose absence let it sign off on ModBuild 159");
    }
}
