using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class UseBarAnimationPlaybackVectors
{
    internal static void Run(Harness t)
    {
        t.Case("native animation clock: packet underflow preserves intermediate poses");
        var clock = new UseBarAnimationPlaybackClock();
        clock.Reset(10f, 100f);
        Near(t, clock.Advance(100.05f, 10f), 10f, "a missing packet freezes source time");
        Near(t, clock.Advance(100.10f, 10f), 10f, "repeated pictures cannot accumulate extrapolated time");
        Near(t, clock.Advance(100.11f, 10.05f), 10f, "new picture begins at the held pose");
        Near(t, clock.Progress(10f, 10.05f), 0f, "arrival does not jump directly to the target");
        clock.Advance(100.135f, 10.05f);
        Near(t, clock.Progress(10f, 10.05f), 0.5f, "source interval has a real midpoint");
        clock.Advance(100.16f, 10.05f);
        Near(t, clock.Progress(10f, 10.05f), 1f, "source interval reaches its endpoint");
        Near(t, clock.Advance(101f, 10.05f), 10.05f, "final picture holds without extrapolation");

        t.Case("native animation clock: delayed widget creation replays retained start/mid/final");
        clock.Reset(0f, 20f);
        Near(t, clock.Advance(20f, 0.2f), 0f, "new original clone starts at the retained first picture");
        Near(t, clock.Advance(20.05f, 0.2f), 0.05f, "retained first interval advances at source speed");
        Near(t, clock.Progress(0f, 0.1f), 0.5f, "retained midpoint is not replaced by latest");
        Near(t, clock.Advance(20.15f, 0.2f), 0.15f, "a clone remap reuses the running clock");
        Near(t, clock.Progress(0.1f, 0.2f), 0.5f, "second interval remains continuous after remap");
        Near(t, clock.Advance(20.3f, 0.2f), 0.2f, "retained final picture is held");

        t.Case("native animation clock: idle predecessors and replacement reset");
        clock.Advance(25f, 5f);
        Near(t, clock.Progress(0.2f, 5f), 1f, "long idle skips to the actual held predecessor without an invented blend");
        Near(t, clock.Cursor, 5f, "idle skip rebases source time");
        clock.Advance(25f, 5.1f);
        Near(t, clock.Progress(5f, 5.1f), 0f, "new motion starts from its real predecessor");
        clock.Advance(25.05f, 5.1f);
        Near(t, clock.Progress(5f, 5.1f), 0.5f, "animation after idle still has a midpoint");
        clock.Reset(8f, 30f);
        Near(t, clock.Advance(30f, 8.1f), 8f, "replacement segment starts independently");
        Near(t, clock.Advance(29f, 8.1f), 8f, "a negative local delta cannot reverse the animation");
    }

    private static void Near(Harness t, float actual, float expected, string detail) =>
        t.True(Math.Abs(actual - expected) < 0.001f, detail);
}
