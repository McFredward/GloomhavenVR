// THE CONTROL BOARD'S SIZE — the arithmetic behind three user reports, driven zoom by zoom.
//
// THE REPORT (ModBuild 158 hardware, verbatim):
//
//   "2) Das Minimum und Maximum des boards ist immer noch abhängig von der Größe meiner Maske.
//    Ich hatte in einer Hand das Controllboard und habe dann gezoomed - dann hat das controllboard
//    mitgezoomed das soll nicht passieren mitzoomen per se nur bei "Folgen". Weiterhin hat sich
//    damit auch das maximum und minimum wieder verschoben der Größe von board."
//
// "immer noch" is earned: the same arithmetic has been re-cut four times (ModBuild 35 introduced
// the apparent-width limits, 39 added the rig-scale divisor, 57 fed it into the two-hand gesture,
// 73 stopped it correcting a pinned board) and every cut left the bound a function of the LIVE rig
// scale while the board's parent chain was not. The three claims in the report are ONE quantity:
//
//     bound_localScale = MinWidth × rigScale / (BoardWidth × parentChain)
//
// linear in how big the player currently is. His own log measured both halves of that fraction —
// a pin holder frozen at ×2.4137 against live rig scales running 2.41 → 20.34 — and the
// consequences it predicts are in the same log, by number: a board released at localScale 2.25
// (ABOVE the shared handle's own maximum of 2, which only the owner's floor can produce) and
// BoardScale_Steel ratcheting 0.54 → 1.00 → 1.13 as PersistPoseToConfig absorbed the overflow,
// moving the settings' expressible window with every step.
//
// WHY THIS IS A TEST AND NOT A HARDWARE ROUND. Every number here is observed ONLY from inside a
// headset, by feel, one zoom sweep at a time — "the board got bigger when I zoomed" and "I can't
// make it as small as I used to" are the entire instrumentation, and the mod cannot notice either.
// Four builds reached the tester with a zoom-coupled bound. The arithmetic was pulled out of
// PlayTray (Transform, the config, the grab handle, the whole cards module) into
// src/GloomhavenVR/Cards/BoardSizeFrame.cs — free of everything but Mathf — precisely so it could
// be linked in here and driven at his own rig scales.

using System;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Cards;

namespace GloomhavenVR.WireTests;

internal static class BoardSizeVectors
{
    // ---- the shipped configuration under test -------------------------------------------------
    private const float MinWidth = 0.18f;   // [Cards] BoardMinWidthMeters (Defaults.Cards.cs)
    private const float MaxWidth = 1.4f;    // [Cards] BoardMaxWidthMeters

    // ---- his session, as the log recorded it --------------------------------------------------
    /// <summary>"World scale 2.41 (base 11.46, table zoom 0.21x)" … "20.34 (… 1.77x)" — the rig
    /// scales the BAR SIZE lines sampled during the zoom sweep he was reporting about.</summary>
    private static readonly float[] SessionRigScales =
        { 2.41f, 2.46f, 2.67f, 2.71f, 3.51f, 3.81f, 4.30f, 6.49f, 11.56f, 11.83f, 19.33f, 20.34f };

    /// <summary>The pin holder his FIXIERT board was hanging under: baked once at pin time from a
    /// rig scale of 2.41 and never re-asserted. Recovered from the log line "world scale 4.826 …
    /// own localScale 2.000" — 4.826 / 2.000.</summary>
    private const float FrozenPinHolder = 2.4137f;

    private static float Near(float a, float b) => Math.Abs(a - b);

    public static void Run(Harness t, string repoRoot)
    {
        BoundsAreGeometryOnly(t);
        ApparentSizeAcrossZoom(t);
        TheStaleAnchorRegression(t);
        GestureWindowNeverInflates(t);
        BothLimitsReachable(t);
        RatchetIsGone(t);
        ConstantsMatchTheirSources(t, repoRoot);
    }

    // ------------------------------------------------------------------------------------------
    //  1. THE BOUNDS ARE A FUNCTION OF THE BOARD, AND OF NOTHING ELSE.
    //     Report sentence one ("das Minimum und Maximum … abhängig von der Größe meiner Maske").
    // ------------------------------------------------------------------------------------------
    private static void BoundsAreGeometryOnly(Harness t)
    {
        t.Case("BS1. board size bounds: board geometry + the two config metres, nothing else");

        BoardSizeFrame.Bounds(MinWidth, MaxWidth, out float lo, out float hi);
        // 18 cm and 140 cm over the board's own 64 cm of width. Written out rather than recomputed
        // from the same expression: a vector that re-derives the formula it is testing tests
        // nothing.
        t.True(Near(lo, 0.28125f) < 1e-6f, $"minimum is 18/64 cm = 0.28125 units (got {lo})");
        t.True(Near(hi, 2.1875f) < 1e-6f, $"maximum is 140/64 cm = 2.1875 units (got {hi})");
        t.True(Near(BoardSizeFrame.WidthOf(lo), MinWidth) < 1e-6f, "the minimum reads back as 18 cm");
        t.True(Near(BoardSizeFrame.WidthOf(hi), MaxWidth) < 1e-6f, "the maximum reads back as 140 cm");

        // The signature of the defect: sweep everything cosmetic and everything zoom-related, and
        // the pair may not move by one float. Bounds() cannot even SEE a rig scale — that is the
        // fix, stated as a type — so what this drives is the gesture window over the same sweep,
        // which can.
        foreach (float rig in SessionRigScales)
        {
            BoardSizeFrame.GestureWindow(MinWidth, MaxWidth, rig, rig, out float glo, out float ghi);
            t.True(Near(glo, 0.28125f) < 1e-5f,
                $"rig ×{rig}: the gesture floor stays 0.28125 (got {glo})");
            t.True(Near(ghi, 2f) < 1e-5f,
                $"rig ×{rig}: the gesture ceiling stays 2.00 (got {ghi})");
        }

        // A crossed or absurd config pair may not invert the window — the board is not optional
        // content and there is no value of these two dials that may make it unreachable.
        BoardSizeFrame.Bounds(1.4f, 0.18f, out float clo, out float chi);
        t.True(chi > clo, "a crossed min/max pair still yields an ordered band");
        BoardSizeFrame.Bounds(0f, 0f, out float zlo, out float zhi);
        t.True(zlo > 0f && zhi > zlo, "a zeroed min/max pair still yields a positive, ordered band");
    }

    // ------------------------------------------------------------------------------------------
    //  2. APPARENT SIZE IS CONSTANT ACROSS A ZOOM SWEEP.
    //     Report sentence two ("dann hat das controllboard mitgezoomed das soll nicht passieren").
    // ------------------------------------------------------------------------------------------
    private static void ApparentSizeAcrossZoom(Harness t)
    {
        t.Case("BS2. a held board keeps its apparent size across his own zoom sweep");

        // The board he was holding: 1.0853 size units (the shipped TrayScale 2 × BoardScale_Oak
        // 0.54265) = 69.5 cm of apparent width.
        const float units = 1.0853f;
        float want = BoardSizeFrame.WidthOf(units);
        t.True(Near(want, 0.694592f) < 1e-5f, $"the shipped board is 69.5 cm wide (got {want * 100f:F1} cm)");

        // WITH THE ANCHOR INVARIANT (parent ≡ rig — FOLGEN because the tray hangs under the rig,
        // FIXIERT because SyncPinHolder keeps the holder on the live rig scale): the localScale the
        // board is rendered at is the size units themselves, at every zoom, and the apparent size
        // never moves. This is the whole fix, expressed as 12 rig scales.
        foreach (float rig in SessionRigScales)
        {
            float local = BoardSizeFrame.LocalScaleFor(units, rig, rig);
            float apparent = BoardSizeFrame.ApparentWidth(local, rig, rig);
            t.True(Near(local, units) < 1e-5f,
                $"rig ×{rig}: localScale is the size units ({local} vs {units})");
            t.True(Near(apparent, want) < 1e-5f,
                $"rig ×{rig}: apparent width holds at {want * 100f:F1} cm (got {apparent * 100f:F1} cm)");
            t.True(Near(BoardSizeFrame.SizeUnits(local, rig, rig), units) < 1e-5f,
                $"rig ×{rig}: the size round-trips back out of the transform");
            t.True(Near(BoardSizeFrame.AnchorRatio(rig, rig), 1f) < 1e-6f,
                $"rig ×{rig}: the anchor ratio the diagnostic prints reads 1.00");
        }
    }

    // ------------------------------------------------------------------------------------------
    //  3. THE REGRESSION ITSELF, at the numbers his log recorded.
    // ------------------------------------------------------------------------------------------
    private static void TheStaleAnchorRegression(Harness t)
    {
        t.Case("BS3. the stale FIXIERT anchor reproduces the reported numbers");

        // The board he pinned at rig ×2.41 and then zoomed away from. Its holder stayed at
        // ×2.4137 while he ran out to ×20.34, so the ratio the whole size arithmetic divides by
        // ran from 1.00 to 0.12 — and everything measured against it moved by the same factor.
        t.True(Near(BoardSizeFrame.AnchorRatio(FrozenPinHolder, 2.41f), 1.0016f) < 1e-3f,
            "at the pin, the frozen holder still agrees with the rig (ratio 1.00)");
        t.True(Near(BoardSizeFrame.AnchorRatio(FrozenPinHolder, 20.34f), 0.11866f) < 1e-4f,
            "eight zoom steps later the same holder is 0.12 of the rig — the bug, as a number");

        // What that does to a board of constant localScale: it "mitgezoomed", by a factor of 8.4.
        const float local = 2f;
        float atPin = BoardSizeFrame.ApparentWidth(local, FrozenPinHolder, 2.41f);
        float zoomed = BoardSizeFrame.ApparentWidth(local, FrozenPinHolder, 20.34f);
        t.True(Near(atPin, 1.2820f) < 1e-3f, $"pinned: 128 cm at the pin (got {atPin * 100f:F1} cm)");
        t.True(Near(zoomed, 0.15188f) < 1e-4f, $"zoomed out: 15 cm (got {zoomed * 100f:F1} cm)");
        t.True(atPin / zoomed > 8f, "the same board shrank more than eightfold without being touched");

        // And what it did to the gesture's FLOOR — the half of the defect that is not merely
        // cosmetic. The pre-fix window was MinWidth/perUnit with no intersection against the
        // handle's own range, so as the ratio fell the floor CLIMBED, and past rig ×17.2 it stood
        // above the handle's maximum of 2. His released board measured 2.253, which this arithmetic
        // places at rig ×19.33 — inside the sweep the same log recorded. The gesture was not
        // resizing his board any more; the floor was.
        float oldFloorAt1933 = MinWidth * 19.33f / (BoardSizeFrame.BoardWidthLocal * FrozenPinHolder);
        t.True(Near(oldFloorAt1933, 2.2524f) < 1e-3f,
            $"the old floor at rig ×19.33 is his logged 2.253 (got {oldFloorAt1933:F4})");
        t.True(oldFloorAt1933 > BoardSizeFrame.GestureFactorMax,
            "…and it stands ABOVE the handle's own maximum, which is how a board reaches 2.25");

        // The fix, at the identical rig scale: the floor is where it always is.
        BoardSizeFrame.GestureWindow(MinWidth, MaxWidth, 19.33f, 19.33f, out float lo, out float hi);
        t.True(Near(lo, 0.28125f) < 1e-5f, $"fixed: the floor at rig ×19.33 is 0.28125 (got {lo})");
        t.True(Near(hi, 2f) < 1e-5f, $"fixed: the ceiling at rig ×19.33 is 2.00 (got {hi})");
    }

    // ------------------------------------------------------------------------------------------
    //  4. THE WINDOW CAN NEVER INFLATE THE BOARD, whatever the anchor is doing.
    // ------------------------------------------------------------------------------------------
    private static void GestureWindowNeverInflates(Harness t)
    {
        t.Case("BS4. the gesture window is an intersection, never an override");

        // PanelGrabHandle clamps to its generic factor range FIRST and to the owner's window
        // SECOND, so an owner window outside that range does not restrict the gesture — it
        // rewrites the clamped target. The window is therefore intersected, and this drives it
        // against anchors far worse than the one that shipped.
        float[] ratios = { 0.01f, 0.1f, 0.5f, 1f, 2f, 10f, 100f };
        foreach (float ratio in ratios)
        {
            BoardSizeFrame.GestureWindow(MinWidth, MaxWidth, ratio, 1f, out float lo, out float hi);
            t.True(lo >= BoardSizeFrame.GestureFactorMin - 1e-6f,
                $"anchor ratio {ratio}: floor {lo} is inside the handle's range");
            t.True(hi <= BoardSizeFrame.GestureFactorMax + 1e-6f,
                $"anchor ratio {ratio}: ceiling {hi} is inside the handle's range");
            t.True(hi >= lo, $"anchor ratio {ratio}: the window is never inverted ({lo}..{hi})");
        }

        // Degenerate transforms must not produce a NaN window — the tray's own scale is written
        // from it every resize frame.
        BoardSizeFrame.GestureWindow(MinWidth, MaxWidth, 0f, 0f, out float dlo, out float dhi);
        t.True(!float.IsNaN(dlo) && !float.IsNaN(dhi) && dhi >= dlo,
            "a degenerate anchor still yields an ordered, finite window");
        t.True(!BoardSizeFrame.TryWidthPerScaleUnit(0f, 1f, out _), "a zero parent chain is refused");
        t.True(!BoardSizeFrame.TryWidthPerScaleUnit(1f, 0f, out _), "a zero rig scale is refused");
    }

    // ------------------------------------------------------------------------------------------
    //  5. BOTH LIMITS STAY REACHABLE BY THE TWO-HAND GRAB.
    // ------------------------------------------------------------------------------------------
    private static void BothLimitsReachable(Harness t)
    {
        t.Case("BS5. the two-hand resize can still reach both limits");

        // A window is only a window if the gesture can arrive at both ends of it: a floor at the
        // handle's own minimum would mean "you can never make it small", a ceiling at the maximum
        // that the handle clamps to anyway would mean the setting does nothing.
        BoardSizeFrame.GestureWindow(MinWidth, MaxWidth, 1f, 1f, out float lo, out float hi);
        t.True(lo > BoardSizeFrame.GestureFactorMin,
            $"the floor {lo} is strictly inside the handle's 0.15, so the board's own minimum binds");
        t.True(hi <= BoardSizeFrame.GestureFactorMax,
            $"the ceiling {hi} is reachable by the gesture");
        t.True(hi / lo > 7f, "the reachable range spans more than sevenfold — a usable resize");

        // What the player actually gets, in centimetres, and it is NOT the configured pair: the
        // handle's factor ceiling of 2 caps the 140 cm maximum at 128 cm. Asserted rather than
        // hidden, because a player who cannot reach the size the setting promises will report it.
        BoardSizeFrame.ReachableWidths(MinWidth, MaxWidth, 1f, 1f, out float loM, out float hiM);
        t.True(Near(loM, 0.18f) < 1e-5f, $"the smallest reachable board is 18 cm (got {loM * 100f:F1})");
        t.True(Near(hiM, 1.28f) < 1e-5f, $"the largest reachable board is 128 cm (got {hiM * 100f:F1})");

        // Clamping a target beyond either end lands exactly ON the end (the pinch "stops at the
        // limit" rather than snapping back or refusing).
        foreach (float rig in SessionRigScales)
        {
            BoardSizeFrame.GestureWindow(MinWidth, MaxWidth, rig, rig, out float glo, out float ghi);
            float tinyTarget = Math.Max(Math.Min(0.01f, ghi), glo);
            float hugeTarget = Math.Max(Math.Min(50f, ghi), glo);
            t.True(Near(tinyTarget, glo) < 1e-6f, $"rig ×{rig}: pinching in stops at the minimum");
            t.True(Near(hugeTarget, ghi) < 1e-6f, $"rig ×{rig}: spreading out stops at the maximum");
        }
    }

    // ------------------------------------------------------------------------------------------
    //  6. THE BoardScale RATCHET — report sentence three.
    // ------------------------------------------------------------------------------------------
    private static void RatchetIsGone(Harness t)
    {
        t.Case("BS6. a grab/release cycle no longer moves the board's expressible range");

        // THE OLD LOOP, from his log, twice in one session:
        //   released at 2.00 units, TrayScale clamped to a fixed 2 → 2 × BoardScale 0.54 = 1.09,
        //   which is not 2.00 → BoardScale_Steel re-seated to 1.00 → the settings window moves
        //   from 0.27–1.09 to 0.50–2.00. Then again at 2.25 → 1.13 → 0.57–2.25.
        // THE NEW RULE: TrayScale's read-clamp is the SAME band the size is clamped into
        // (CardsConfig.ClampedTrayScale), so the product always reproduces and the per-board seed
        // is never written. Driven here as the arithmetic CardsConfig performs.
        BoardSizeFrame.Bounds(MinWidth, MaxWidth, out float lo, out float hi);
        float[] seeds = { 0.05f, 0.54265f, 1f, 1.13f, 4f };
        float[] releases = { 0.28125f, 0.5f, 1.0853f, 1.5f, 2f, 2.1875f };
        foreach (float seed in seeds)
        {
            foreach (float released in releases)
            {
                // PersistPoseToConfig: the size is stored in units, TrayScale takes all of it.
                float units = BoardSizeFrame.ClampUnits(released, MinWidth, MaxWidth);
                float trayScale = units / seed;
                // ClampedTrayScale, verbatim: the band, carried onto the factor.
                float clamped = Math.Max(lo / seed, Math.Min(hi / seed, trayScale));
                float reproduced = BoardSizeFrame.ClampUnits(clamped * seed, MinWidth, MaxWidth);
                t.True(Near(reproduced, units) < 1e-4f,
                    $"seed {seed}, released {released}: the size reproduces exactly "
                    + $"({reproduced} vs {units}) — so nothing is absorbed into BoardScale");
            }
        }

        // And the band itself is untouched by the seed, which is what "the min/max stopped moving"
        // means: whatever BoardScale_<board> says, the board is between 18 and 140 cm.
        foreach (float seed in seeds)
        {
            float smallest = BoardSizeFrame.ClampUnits(lo / seed * seed, MinWidth, MaxWidth);
            float largest = BoardSizeFrame.ClampUnits(hi / seed * seed, MinWidth, MaxWidth);
            t.True(Near(BoardSizeFrame.WidthOf(smallest), MinWidth) < 1e-4f,
                $"seed {seed}: the smallest expressible board is still 18 cm");
            t.True(Near(BoardSizeFrame.WidthOf(largest), MaxWidth) < 1e-4f,
                $"seed {seed}: the largest expressible board is still 140 cm");
        }
    }

    // ------------------------------------------------------------------------------------------
    //  7. SOURCE LINT — the two constants this file's whole argument rests on.
    // ------------------------------------------------------------------------------------------
    private static void ConstantsMatchTheirSources(Harness t, string repoRoot)
    {
        t.Case("BS7. BoardSizeFrame's mirrored constants still match their declarations");

        // BoardWidthLocal must be the board's real width. If the mesh is re-authored and only
        // PlayTray.BoardW moves, every bound above is computed over the wrong geometry and NOTHING
        // fails — the board simply stops honouring its own centimetres.
        string build = File.ReadAllText(Path.Combine(
            repoRoot, "src", "GloomhavenVR", "Cards", "PlayTray.6.Build.cs"));
        Match w = Regex.Match(build, @"const\s+float\s+BoardW\s*=\s*([0-9.]+)f");
        t.True(w.Success, "PlayTray.6.Build.cs still declares BoardW");
        if (w.Success)
            t.True(Near(float.Parse(w.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        BoardSizeFrame.BoardWidthLocal) < 1e-6f,
                $"BoardSizeFrame.BoardWidthLocal ({BoardSizeFrame.BoardWidthLocal}) == PlayTray.BoardW ({w.Groups[1].Value})");

        // The handle's generic factor range. The window is INTERSECTED with this pair; if the pair
        // moves and the mirror does not, the intersection either strangles the gesture or stops
        // preventing the inflation it exists to prevent.
        string grab = File.ReadAllText(Path.Combine(
            repoRoot, "src", "GloomhavenVR", "WorldUI", "PanelGrab.cs"));
        Match mn = Regex.Match(grab, @"const\s+float\s+MinScale\s*=\s*([0-9.]+)f");
        Match mx = Regex.Match(grab, @"const\s+float\s+MaxScale\s*=\s*([0-9.]+)f");
        t.True(mn.Success && mx.Success, "PanelGrab.cs still declares MinScale/MaxScale");
        if (mn.Success)
            t.True(Near(float.Parse(mn.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        BoardSizeFrame.GestureFactorMin) < 1e-6f,
                $"GestureFactorMin ({BoardSizeFrame.GestureFactorMin}) == PanelGrabHandle.MinScale ({mn.Groups[1].Value})");
        if (mx.Success)
            t.True(Near(float.Parse(mx.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        BoardSizeFrame.GestureFactorMax) < 1e-6f,
                $"GestureFactorMax ({BoardSizeFrame.GestureFactorMax}) == PanelGrabHandle.MaxScale ({mx.Groups[1].Value})");

        // The shipped limits this file drives. They are configuration, so they may move — but the
        // vectors above are written in their values, and a silent re-default would leave every
        // centimetre in this file quietly wrong.
        string defaults = File.ReadAllText(Path.Combine(
            repoRoot, "src", "GloomhavenVR", "Defaults", "Defaults.Cards.cs"));
        t.True(Regex.IsMatch(defaults, @"BoardMinWidthMeters\s*=\s*0\.18f"),
            "Defaults.BoardMinWidthMeters is still 0.18 m (the 18 cm this file asserts)");
        t.True(Regex.IsMatch(defaults, @"BoardMaxWidthMeters\s*=\s*1\.4f"),
            "Defaults.BoardMaxWidthMeters is still 1.4 m (the 140 cm this file asserts)");

        // THE INVARIANT, AS A SOURCE PROPERTY. The whole fix is that the FIXIERT holder tracks the
        // live rig scale; the block that used to forbid exactly that stood in this file for
        // dozens of builds. If the write goes away again, every vector above still passes (the
        // arithmetic is fine — it is the anchor that would be stale), and only the tester would
        // find out. So the write itself is linted.
        string watchdog = File.ReadAllText(Path.Combine(
            repoRoot, "src", "GloomhavenVR", "Cards", "PlayTray.2.Watchdog.cs"));
        t.True(Regex.IsMatch(watchdog, @"_pinRoot\.localScale\s*=\s*Vector3\.one\s*\*\s*live"),
            "SyncPinHolder still re-asserts the pin holder's scale from the LIVE rig scale — "
            + "without it the anchor ratio drifts and every bound above becomes zoom-coupled again");
        t.True(watchdog.Contains("TickHeldSizeFreeze"),
            "the held-size freeze is still ticked — a board in the hand keeps its apparent size");
    }
}
