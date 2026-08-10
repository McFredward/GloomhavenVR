using GloomhavenVR.Rig;

namespace GloomhavenVR.WireTests;

/// <summary>
/// SCROLL-VS-TURN ARBITRATION (<see cref="ScrollTurnGate"/>), driven frame by frame.
///
/// <para>WHY IT IS ON THIS HARNESS. Same reason as <c>ConfigSteps</c> and
/// <c>RelaunchCommand</c>: it decides something that is only ever observed by FEEL, from inside
/// a headset, one thumb push at a time. A latch that never releases does not throw, does not log
/// and does not look wrong — it produces a player who reports "ich kann mich nicht mehr drehen",
/// which is a whole hardware round to find. The two cases that matter are the RELEASE EDGE (the
/// stick is still deflected when the scroll ends — a naive "gate on is-scrolling" fix moves the
/// unwanted turn 200 ms later instead of removing it) and the two EXITS from the block, and
/// neither is reachable from any other test in this repo.</para>
///
/// <para>Rearm thresholds are the ones <c>SnapTurn</c> hands in: 0.2 for Smooth (its deadzone,
/// and the shipped default mode), 0.3 for Snap (its re-arm hysteresis).</para>
/// </summary>
internal static class ScrollTurnGateVectors
{
    private const float SmoothRearm = 0.2f;
    private const float SnapRearm = 0.3f;

    internal static void Run(Harness t)
    {
        NeverScrolling(t);
        HoverAloneCostsNothing(t);
        TheReportedDefect(t);
        ReleaseEdge(t);
        DeliberateFlickAlwaysTurns(t);
        OverrideIsHeld(t);
        DiagonalIsNotAFlick(t);
        ResetFailsOpen(t);
        ModeThresholds(t);
    }

    /// <summary>A player who never points at a list must never notice this class exists.</summary>
    private static void NeverScrolling(Harness t)
    {
        t.Case("scroll-turn: no scroll, no gate");
        var g = new ScrollTurnGate();
        t.True(g.Evaluate(false, 0f, 0f, SmoothRearm), "idle stick turns");
        t.True(g.Evaluate(false, 0.25f, 0f, SmoothRearm), "slow smooth push turns");
        t.True(g.Evaluate(false, 0.95f, 0f, SmoothRearm), "full flick turns");
        t.True(g.Evaluate(false, 0.4f, 0.9f, SmoothRearm), "even a diagonal turns — nothing owns the stick");
        t.True(!g.IsBlocking, "gate never armed");
    }

    /// <summary>
    /// Pointing at a scrollable with an untouched stick must not cost a single frame of turning.
    /// This is the case the block ARMS on, so it is also the one that proves the arm can clear in
    /// the same frame it was set (see the sequential-transition note in Evaluate).
    /// </summary>
    private static void HoverAloneCostsNothing(Harness t)
    {
        t.Case("scroll-turn: hover with a resting stick");
        var g = new ScrollTurnGate();
        for (int frame = 0; frame < 5; frame++)
            t.True(g.Evaluate(true, 0f, 0f, SmoothRearm), $"resting stick over a list turns (frame {frame})");
        t.True(g.Evaluate(true, 0.05f, 0.05f, SmoothRearm), "stick slop over a list still turns");
    }

    /// <summary>
    /// The user report itself (2026-08-11): pushing the stick up a menu list carries an
    /// incidental sideways component, and Smooth turn's deadzone is 0.2, so the world yawed.
    /// </summary>
    private static void TheReportedDefect(Harness t)
    {
        t.Case("scroll-turn: the reported defect");
        var g = new ScrollTurnGate();
        t.True(!g.Evaluate(true, 0.35f, 0.9f, SmoothRearm), "scroll push with sideways drift does NOT turn");
        t.True(!g.Evaluate(true, 0.28f, 0.95f, SmoothRearm), "…nor on the next frame");
        t.True(!g.Evaluate(true, 0.45f, -0.85f, SmoothRearm), "…nor scrolling the other way");
        t.True(g.IsBlocking, "the gate reports itself as blocking");
    }

    /// <summary>
    /// THE RELEASE EDGE — the whole reason the gate is a latch. The scroll ends (beam off the
    /// list, grace expired) while the thumb is still over; lifting the block right there fires
    /// exactly the accidental turn the user reported, only later.
    /// </summary>
    private static void ReleaseEdge(Harness t)
    {
        t.Case("scroll-turn: release edge");
        var g = new ScrollTurnGate();
        t.True(!g.Evaluate(true, 0.35f, 0.9f, SmoothRearm), "blocked while scrolling");
        t.True(!g.Evaluate(false, 0.35f, 0.9f, SmoothRearm), "scroll ENDED, stick still over — still blocked");
        t.True(!g.Evaluate(false, 0.30f, 0.6f, SmoothRearm), "…and while the thumb relaxes through the band");
        t.True(!g.Evaluate(false, 0.21f, 0.3f, SmoothRearm), "…right down to the rearm threshold");
        t.True(g.Evaluate(false, 0.2f, 0.1f, SmoothRearm), "axis at rest ⇒ turning is back");
        t.True(g.Evaluate(false, 0.9f, 0f, SmoothRearm), "…and a fresh flick turns immediately");
    }

    /// <summary>
    /// The anti-stranding exit: a genuine sideways flick turns even while the beam sits on a
    /// list, so "I parked the pointer on the options list" can never mean "I cannot turn".
    /// </summary>
    private static void DeliberateFlickAlwaysTurns(Harness t)
    {
        t.Case("scroll-turn: deliberate flick escapes the block");
        var g = new ScrollTurnGate();
        t.True(!g.Evaluate(true, 0.4f, 0.9f, SmoothRearm), "armed by a scroll push");
        t.True(g.Evaluate(true, 0.85f, 0.2f, SmoothRearm), "hard sideways, sideways-dominant ⇒ turns");
        t.True(g.Evaluate(true, 0.7f, 0.69f, SmoothRearm), "exactly at the threshold, still x-dominant ⇒ turns");
    }

    /// <summary>
    /// The override is HELD to the axis coming back to rest, not re-tested per frame: a player
    /// easing off mid-rotation must not drop back into the block and see the yaw stutter.
    /// </summary>
    private static void OverrideIsHeld(Harness t)
    {
        t.Case("scroll-turn: override hysteresis");
        var g = new ScrollTurnGate();
        t.True(!g.Evaluate(true, 0.4f, 0.9f, SmoothRearm), "armed");
        t.True(g.Evaluate(true, 0.9f, 0.1f, SmoothRearm), "flick takes the axis");
        t.True(g.Evaluate(true, 0.5f, 0.1f, SmoothRearm), "easing off keeps turning");
        t.True(g.Evaluate(true, 0.35f, 0.4f, SmoothRearm), "…even back below the flick threshold");
        t.True(!g.Evaluate(true, 0.05f, 0.9f, SmoothRearm), "released onto a scroll push ⇒ blocked again");
        t.True(g.IsBlocking, "and it says so");
        t.True(g.Evaluate(false, 0.1f, 0f, SmoothRearm), "one frame later, at rest and not scrolling ⇒ open");
    }

    /// <summary>
    /// A full diagonal is the ACCIDENT, not a turn: the sideways component only reaches the flick
    /// threshold because the player is pushing hard, and y is just as large. It must stay blocked.
    /// </summary>
    private static void DiagonalIsNotAFlick(Harness t)
    {
        t.Case("scroll-turn: a diagonal is not a flick");
        var g = new ScrollTurnGate();
        t.True(!g.Evaluate(true, 0.71f, 0.71f, SmoothRearm), "45° push does not turn");
        t.True(!g.Evaluate(true, 0.75f, 0.8f, SmoothRearm), "y-dominant hard push does not turn");
        t.True(!g.Evaluate(true, 0.6f, 0.1f, SmoothRearm), "sideways but below the flick threshold: still blocked");
    }

    /// <summary>Structural fail-open: turn hand switched, turning disabled, a mode took the stick.</summary>
    private static void ResetFailsOpen(Harness t)
    {
        t.Case("scroll-turn: reset fails open");
        var g = new ScrollTurnGate();
        t.True(!g.Evaluate(true, 0.5f, 0.9f, SmoothRearm), "blocked");
        g.Reset();
        t.True(!g.IsBlocking, "reset clears the latch");
        t.True(g.Evaluate(false, 0.5f, 0.9f, SmoothRearm), "…and the very same stick pose turns again");
    }

    /// <summary>
    /// "Back at rest" is the ACTIVE MODE's own no-input threshold, so the gate never holds the
    /// axis below a deflection that mode would have acted on — and never releases above one.
    /// </summary>
    private static void ModeThresholds(Harness t)
    {
        t.Case("scroll-turn: mode-specific rearm");
        var snap = new ScrollTurnGate();
        t.True(!snap.Evaluate(true, 0.5f, 0.9f, SnapRearm), "snap: blocked by a scroll push");
        t.True(snap.Evaluate(false, 0.29f, 0.2f, SnapRearm), "snap: 0.29 is below its 0.3 re-arm ⇒ open");

        var smooth = new ScrollTurnGate();
        t.True(!smooth.Evaluate(true, 0.5f, 0.9f, SmoothRearm), "smooth: blocked by a scroll push");
        t.True(!smooth.Evaluate(false, 0.29f, 0.2f, SmoothRearm), "smooth: 0.29 is above its 0.2 deadzone ⇒ held");
        t.True(smooth.Evaluate(false, 0.19f, 0.2f, SmoothRearm), "smooth: 0.19 ⇒ open");
    }
}
