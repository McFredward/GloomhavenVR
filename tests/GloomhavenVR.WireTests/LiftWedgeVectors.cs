using GloomhavenVR.Rig;

namespace GloomhavenVR.WireTests;

/// <summary>
/// TURN-VS-LIFT AXIS SEPARATION (<see cref="LiftWedge"/>), driven push by push.
///
/// <para>WHY IT IS ON THIS HARNESS. Same reason as <c>ScrollTurnGate</c> and <c>ConfigSteps</c>:
/// it decides something that is only ever observed by FEEL, from inside a headset, one thumb push
/// at a time. A wedge one comparison too loose does not throw, does not log and does not look
/// wrong — it produces a player whose world yaws every time they try to rise. A wedge one
/// comparison too tight produces a control that silently refuses to work for anyone whose thumb is
/// not perfectly vertical, which is indistinguishable from a broken button and is the failure mode
/// this project has paid the most hardware rounds for.</para>
///
/// <para>THE FOUR CLAIMS THE FEATURE MAKES ABOUT ITSELF are all asserted here, because each of
/// them is written into a user-facing German sentence in the options menu and into the log's
/// diagnostic line, and a claim in a caption that the code does not honour is worse than no
/// caption:</para>
/// <list type="number">
/// <item>45° is a PURE TURN, in both directions of travel (engaging and sustaining).</item>
/// <item>A snap-turn flick can never lift, on a circular stick gate — this one is arithmetic, and
/// it is the whole of the "Snap mode is handled" answer.</item>
/// <item>The lift deadzone is stricter than turning's, not equal to it.</item>
/// <item>The hysteresis widens the wedge once engaged, and never the other way round.</item>
/// </list>
/// </summary>
internal static class LiftWedgeVectors
{
    /// <summary>SnapTurn.SnapEngageThreshold — mirrored here, and pinned by <see cref="SnapFlickCanNeverLift"/>.</summary>
    private const float SnapEngage = 0.7f;

    /// <summary>SnapTurn.SmoothDeadzone — the deflection Smooth turn starts yawing at.</summary>
    private const float SmoothDeadzone = 0.2f;

    internal static void Run(Harness t)
    {
        ForwardPushLifts(t);
        FortyFiveIsAlwaysPureTurn(t);
        RestingStickNeverLifts(t);
        SnapFlickCanNeverLift(t);
        HysteresisWidensNeverNarrows(t);
        DeadzoneIsStricterThanTurning(t);
        SignIsIrrelevantToTheVerdict(t);
    }

    /// <summary>The plain case the user asked for: push the stick forward, rise.</summary>
    private static void ForwardPushLifts(Harness t)
    {
        t.Case("lift-wedge: a straight forward push lifts");
        t.True(LiftWedge.Evaluate(0f, 1f, false), "full forward push engages");
        t.True(LiftWedge.Evaluate(0f, -1f, false), "full backward push engages (it sinks)");
        t.True(LiftWedge.Evaluate(0f, 0.5f, false), "exactly at the engage deflection");
        t.True(!LiftWedge.Evaluate(0f, 0.49f, false), "just under the engage deflection does not");
        // Slop tolerance is the whole reason the rule is a ratio: 0.25 of sideways on a push meant
        // to be straight up is ordinary thumb behaviour and must still lift.
        t.True(LiftWedge.Evaluate(0.25f, 0.9f, false), "a real thumb's 0.25 of sideways slop still lifts");
    }

    /// <summary>
    /// CLAIM 1, and the one the German caption states outright ("a diagonal push at 45 degrees is a
    /// pure turn"): at |y| == |x| nothing lifts, whether or not a lift was already running. Both
    /// directions matter — an engage rule alone would let a lift that started steep survive being
    /// dragged down to the diagonal.
    /// </summary>
    private static void FortyFiveIsAlwaysPureTurn(Harness t)
    {
        t.Case("lift-wedge: 45 degrees is a pure turn, entering and leaving");
        t.True(!LiftWedge.Evaluate(0.707f, 0.707f, false), "full 45 push does not engage");
        t.True(!LiftWedge.Evaluate(0.707f, 0.707f, true), "…and releases a running lift");
        t.True(!LiftWedge.Evaluate(0.5f, 0.5f, false), "half-deflection 45 does not engage");
        t.True(!LiftWedge.Evaluate(0.5f, 0.5f, true), "…and releases");
        t.True(!LiftWedge.Evaluate(0.4f, 0.4f, true), "a shallow 45 releases too");
        // And just past it, on the vertical side, it is allowed to sustain — the wedge boundary is
        // at 45 exactly, not somewhere vaguely above it.
        t.True(LiftWedge.Evaluate(0.5f, 0.61f, true), "50.6 degrees sustains (just inside 1.20)");
        t.True(!LiftWedge.Evaluate(0.5f, 0.59f, true), "49.7 degrees does not (just outside 1.20)");
    }

    private static void RestingStickNeverLifts(Harness t)
    {
        t.Case("lift-wedge: a resting or sideways stick never lifts");
        t.True(!LiftWedge.Evaluate(0f, 0f, false), "centred stick");
        t.True(!LiftWedge.Evaluate(0f, 0f, true), "centred stick releases a running lift");
        t.True(!LiftWedge.Evaluate(1f, 0f, false), "a full sideways flick is a turn, not a lift");
        t.True(!LiftWedge.Evaluate(1f, 0f, true), "…and ends a running lift");
        t.True(!LiftWedge.Evaluate(0f, 0.34f, true), "under the sustain deflection, a lift ends");
    }

    /// <summary>
    /// CLAIM 2 — THE SNAP-MODE ANSWER, AND IT IS ARITHMETIC. A snap fires at |x| >= 0.70. For the
    /// lift to engage beside it the stick would have to reach |y| >= 1.50 * 0.70 = 1.05, and even
    /// the looser sustain wedge would need |y| >= 0.84, i.e. a vector of magnitude 1.09. A
    /// thumbstick's gate is a circle, so neither exists. This sweep walks the whole circle at snap
    /// deflection and asserts there is no point on it where both are true — which is what lets the
    /// feature claim "with snap turn, the vertical axis can never trigger a snap" without adding a
    /// single line of code to SnapTurn.
    /// </summary>
    private static void SnapFlickCanNeverLift(Harness t)
    {
        t.Case("lift-wedge: a snap-turn flick can never also lift");
        int checkedPoints = 0;
        for (int deg = 0; deg <= 360; deg++)
        {
            double rad = deg * System.Math.PI / 180.0;
            var x = (float)System.Math.Cos(rad);
            var y = (float)System.Math.Sin(rad);   // on the unit circle: the stick's outer gate
            bool snapWouldFire = System.Math.Abs(x) >= SnapEngage;
            if (!snapWouldFire)
                continue;
            checkedPoints++;
            t.True(!LiftWedge.Evaluate(x, y, false), $"no engage at {deg} deg on the gate");
            t.True(!LiftWedge.Evaluate(x, y, true), $"no sustain at {deg} deg on the gate");
        }
        t.True(checkedPoints > 0, "the sweep found snap-firing points at all");
    }

    /// <summary>
    /// CLAIM 4: hysteresis must only ever make it EASIER to keep lifting, never easier to start.
    /// The inverted version of this rule is a real and silent defect — it produces a lift that
    /// engages on a diagonal and then drops out, once per frame, which reads as a broken stick.
    /// </summary>
    private static void HysteresisWidensNeverNarrows(Harness t)
    {
        t.Case("lift-wedge: hysteresis only ever widens the wedge");
        for (int xi = 0; xi <= 100; xi++)
        {
            for (int yi = 0; yi <= 100; yi++)
            {
                float x = xi / 100f;
                float y = yi / 100f;
                if (x * x + y * y > 1f)
                    continue;   // outside a circular stick gate
                if (LiftWedge.Evaluate(x, y, false))
                    t.True(LiftWedge.Evaluate(x, y, true), $"engages at ({x:F2},{y:F2}) so it must sustain");
            }
        }
        // …and it really is wider, not merely not-narrower: a point that sustains but cannot engage.
        t.True(!LiftWedge.Evaluate(0.3f, 0.4f, false), "shallow-and-soft does not engage");
        t.True(LiftWedge.Evaluate(0.3f, 0.4f, true), "…but a running lift survives there");
    }

    /// <summary>
    /// CLAIM 3, the comfort one: rising must take a more deliberate push than turning does.
    /// Uncommanded vertical motion is the nausea risk in this feature, so a deflection that Smooth
    /// turn already acts on must be nowhere near enough to lift.
    /// </summary>
    private static void DeadzoneIsStricterThanTurning(Harness t)
    {
        t.Case("lift-wedge: lifting needs a more deliberate push than turning");
        t.True(LiftWedge.EngageDeflection > SmoothDeadzone,
               "the lift engage deflection is above Smooth turn's deadzone");
        t.True(LiftWedge.SustainDeflection > SmoothDeadzone,
               "even the sustain deflection is above it");
        t.True(!LiftWedge.Evaluate(0f, SmoothDeadzone, false),
               "a deflection Smooth turn would already yaw on does not lift");
        t.True(LiftWedge.SustainRatio > 1f, "the sustain ratio is above 1, so 45 degrees can never lift");
        t.True(LiftWedge.EngageRatio > LiftWedge.SustainRatio, "engaging is the stricter of the two");
        t.True(LiftWedge.EngageDeflection > LiftWedge.SustainDeflection, "…on the deflection too");
    }

    /// <summary>
    /// The verdict is about MAGNITUDES only — which way the stick leans decides which way the rig
    /// travels, and that sign lives in Flight, not here. All four quadrants must answer alike, or a
    /// player would find that sinking works and rising does not (or that it only works while
    /// leaning one way).
    /// </summary>
    private static void SignIsIrrelevantToTheVerdict(Harness t)
    {
        t.Case("lift-wedge: all four quadrants answer alike");
        for (int xi = 0; xi <= 20; xi++)
        {
            for (int yi = 0; yi <= 20; yi++)
            {
                float x = xi / 20f;
                float y = yi / 20f;
                foreach (bool running in new[] { false, true })
                {
                    bool expected = LiftWedge.Evaluate(x, y, running);
                    t.True(LiftWedge.Evaluate(-x, y, running) == expected, $"(-x,+y) at ({x:F2},{y:F2})");
                    t.True(LiftWedge.Evaluate(x, -y, running) == expected, $"(+x,-y) at ({x:F2},{y:F2})");
                    t.True(LiftWedge.Evaluate(-x, -y, running) == expected, $"(-x,-y) at ({x:F2},{y:F2})");
                }
            }
        }
    }
}
