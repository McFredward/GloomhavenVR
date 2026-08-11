using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// ROUND 9 — THE CARD'S TRUE OUTLINE, DERIVED GEOMETRICALLY, ONCE, AND SHARED BY EVERY CONSUMER.
///
/// <para>WHY LUMA CONNECTIVITY DIED (the ModBuild-112 evidence, read before touching thresholds).
/// Round 8 erased the printed frame with a boundary-seeded BFS over "opaque AND luma ≤ 48" —
/// and the 112 hardware log shows it ate 24 px deep and stopped while the punched background
/// still painted 102 of 264 near-black band probes: the frame's dark pixels are interrupted by
/// brighter features, so boundary CONNECTIVITY can never reach the rest. Worse, the margin is
/// structurally hopeless: profiling the user's screenshot (.planning/debug/karten.png) across a
/// card edge puts the card CONTENT at luma 55–65 (dark maroon), the printed frame at ≤ 40, and
/// the threshold sat at 48 — seven luma units below content. Three erosion caps in a row (11,
/// 18, learned-24) failed for the same reason. Any "is this pixel frame?" rule based on
/// darkness alone is on a knife edge between frame and content.</para>
///
/// <para>WHAT IS ROBUST INSTEAD: the card's VISIBLE edge is a bright gold trim line —
/// ~(236,212,164), luma ~213 in the screenshot profile — against a frame at ≤ 40 and content at
/// 55–65. That is a ~150-luma separation on the side that matters. So the definition flips from
/// per-pixel to GEOMETRIC: scan the full-face background sprite from each boundary inward for
/// the FIRST bright pixel; the outermost bright contour is the card's outline; everything
/// outside it is frame, no matter how dark or bright, connected or not. Validated against the
/// screenshot (scratchpad run, flat board card): the derived contour hugs the trim on all four
/// edges, follows the class-banner bump at the top and the initiative-chip bulge at the bottom
/// (designed protrusions — their bright edging IS part of the contour), and measures bands of
/// left 0.8 %, right 1.2–2.0 %, top 3.4–4.5 %, bottom ≥ 6 % of the card — against the
/// screenshot's directly measured top ≈ 4.8 %, bottom ≈ 6.6 %, left ≈ 0.8 %, right ≈ 2.4 %
/// (top/right read low in the screenshot because the board card is tilted away from the camera;
/// the derivation runs on unlit, unprojected sprite pixels where that error does not exist).</para>
///
/// <para>REPRESENTATION. Four edge functions on the source sprite's own pixel grid: per ROW the
/// first/last bright column, per COLUMN the first/last bright row. A point is inside the
/// outline iff it is inside its row's span AND its column's span — the orthogonal intersection,
/// which reproduces rounded/chamfered corners and designed protrusions exactly (both were
/// verified on the screenshot). Rows/columns with no bright hit inside the contour's extent are
/// LINEARLY INTERPOLATED between their nearest hit neighbours — the trim is a continuous
/// contour, so a missing hit is sampling noise (anti-aliasing), not a hole; a gap RUN longer
/// than <see cref="MaxGapRunFraction"/> of the axis is evidence the contour is genuinely open
/// there and refuses the whole derivation instead. Rows outside the extent are entirely
/// outside (the frame band above/below the contour). A 5-tap median filter removes single-row
/// outliers (a bright speck in the frame) without rounding off monotone corner ramps.</para>
///
/// <para>VALIDATION — every gate refuses the WHOLE outline, and a refusal means the ModBuild-112
/// behaviour is kept verbatim (the luma-BFS punch stays as the fallback; see
/// <c>CardFace.FramePunch</c>). Gates, each logged with its numbers:
/// (1) bright pixels must exist and the contour extent must span ≥ <see cref="MinExtentFraction"/>
///     of each sprite axis — a small bright region cannot define a card;
/// (2) interior gap runs ≤ <see cref="MaxGapRunFraction"/>, total gaps ≤ <see cref="MaxTotalGapFraction"/>;
/// (3) each edge's central-60 % interquartile range ≤ <see cref="MaxCentralIqrFraction"/> of its
///     axis — THE load-bearing gate: replaying the algorithm on the screenshot with a threshold
///     that misses the (lighting-dimmed) bottom trim produced a 44 px IQR against an 11.5 px
///     gate, i.e. exactly the "latched onto interior text" failure this must catch;
/// (4) every band ≤ <see cref="MaxBandFraction"/> of the face and the inside area within
///     [<see cref="MinAreaFraction"/>, <see cref="MaxAreaFraction"/>] of the face.
/// The bright threshold is a LADDER (<see cref="ThresholdLadder"/>): 140 sits mid-gap between
/// content (≤ 65) and trim (~213); if validation fails there, 110 and 80 are tried — the
/// screenshot replay needed exactly such a step because projection lighting dimmed one edge,
/// and native sprite pixels have no lighting, so 140 is expected to win on hardware. The
/// threshold that validated is logged.</para>
///
/// <para>CONSUMERS — one geometry, three of them: (a) the sprite PUNCH
/// (<c>CardFaceMipBake.OutlinePunchedReplacementFor</c>) erases every face layer's pixels
/// outside the outline; (b) the MESH footprint is intersected with it after capture
/// (<c>CardFace.TryCapture</c>), so mesh and face agree by construction; (c) the (still
/// disabled) <c>CardShapeMask</c> would clip to the same footprint if ever re-enabled.
/// <c>CardMesh.CacheVersion</c> 4 → 5 makes the persisted mask agree — that bump has been
/// load-bearing three times.</para>
///
/// <para>DERIVED PER SOURCE SPRITE, cached by content identity: every card of a class and every
/// peer clone share one derivation and one verdict. The per-kind slot below holds the outline
/// of the face most recently swept, which — because the sweep runs immediately before the
/// capture in <c>CardFace.Offer</c> — is always the same face the capture is stamping.</para>
/// </summary>
internal sealed class CardOutline
{
    /// <summary>Bright thresholds tried in order (Rec.601, 0..255). 140 sits mid-gap between the
    /// measured content ceiling (~65) and the measured trim (~213); the lower rungs exist for art
    /// whose trim is dimmer, and validation decides — a rung only wins by passing every gate.</summary>
    private static readonly int[] ThresholdLadder = { 140, 110, 80 };

    /// <summary>Longest interpolated gap RUN tolerated inside the contour extent, per axis.</summary>
    private const float MaxGapRunFraction = 0.03f;

    /// <summary>Total no-hit rows/columns tolerated inside the contour extent, per axis.</summary>
    private const float MaxTotalGapFraction = 0.10f;

    /// <summary>Central-60 % IQR ceiling per edge — catches an edge that latched onto interior
    /// content instead of the trim (screenshot replay: 44 px IQR vs an 11.5 px gate).</summary>
    private const float MaxCentralIqrFraction = 0.03f;

    /// <summary>The contour must span at least this much of each sprite axis.</summary>
    private const float MinExtentFraction = 0.8f;

    /// <summary>No band may exceed this fraction of the face (measured bands: ≤ 6.6 %).</summary>
    private const float MaxBandFraction = 0.12f;

    /// <summary>Inside-area floor, as a fraction of the FACE (a letterboxed source still passes:
    /// 0.905 × ~0.9 outline ≈ 0.81; an outline this small is not a card face).</summary>
    private const float MinAreaFraction = 0.55f;

    /// <summary>Inside-area ceiling — a full-rect "outline" found nothing to derive.</summary>
    private const float MaxAreaFraction = 0.995f;

    /// <summary>Derivation cache by source content identity (null = refused, latched — one
    /// verdict per art, shared by class-mates and every peer clone).</summary>
    private static readonly Dictionary<string, CardOutline?> s_bySourceKey = new(4);

    /// <summary>The outline of the face most recently swept, per <see cref="CardBodyKind"/> —
    /// what the capture intersects its footprint with (same face by construction, see class doc).</summary>
    private static readonly CardOutline?[] s_byKind = new CardOutline?[3];

    private readonly int _w;
    private readonly int _h;
    private readonly float[] _left;    // per row (row 0 = sprite bottom): first inside column
    private readonly float[] _right;   // per row: last inside column
    private readonly float[] _bottom;  // per column: first inside row
    private readonly float[] _top;     // per column: last inside row

    /// <summary>Face-normalized rect the source sprite is DRAWN in (round-7 drawn rect). The
    /// mapping between face space and the edge arrays' sprite space.</summary>
    private readonly Rect _drawnFaceRect;

    /// <summary>Content identity of the source art — the punch texture cache key component.</summary>
    internal string SourceKey { get; }

    internal string SourceName { get; }

    /// <summary>The ladder rung that validated.</summary>
    internal int Threshold { get; }

    /// <summary>Per-edge band widths in FACE fractions (distance from the face edge to the
    /// outline's median edge) — the numbers a hardware log validates against the visible band.</summary>
    internal float BandLeft { get; }
    internal float BandRight { get; }
    internal float BandTop { get; }
    internal float BandBottom { get; }

    /// <summary>One-line band summary, worded once so every log site prints identical numbers.</summary>
    internal string BandSummary =>
        $"top {BandTop:P1}, bottom {BandBottom:P1}, left {BandLeft:P1}, right {BandRight:P1} of the face";

    private CardOutline(int w, int h, float[] left, float[] right, float[] bottom, float[] top,
                        Rect drawnFaceRect, string sourceKey, string sourceName, int threshold,
                        float bandLeft, float bandRight, float bandTop, float bandBottom)
    {
        _w = w;
        _h = h;
        _left = left;
        _right = right;
        _bottom = bottom;
        _top = top;
        _drawnFaceRect = drawnFaceRect;
        SourceKey = sourceKey;
        SourceName = sourceName;
        Threshold = threshold;
        BandLeft = bandLeft;
        BandRight = bandRight;
        BandTop = bandTop;
        BandBottom = bandBottom;
    }

    /// <summary>The current outline for a card kind, or null when none has validated yet.</summary>
    internal static CardOutline? ForKind(CardBodyKind kind) => s_byKind[(int)kind];

    /// <summary>
    /// Get or derive the outline for <paramref name="source"/> (a GAME sprite, resolved through
    /// <c>CardFaceMipBake.OriginalOf</c> by the caller) drawn at <paramref name="drawnFaceRect"/>
    /// on a <paramref name="kind"/> face. Refusals are latched by content identity with one log
    /// line; a success updates the per-kind slot so the capture that follows the sweep sees the
    /// same geometry. Never throws.
    /// </summary>
    internal static CardOutline? ForSource(CardBodyKind kind, Sprite source, Rect drawnFaceRect)
    {
        try
        {
            // Cache FIRST, on geometry alone — the sweep resolves this every second and the
            // pixel slice is a multi-MB copy that must only ever be built once per content.
            string? contentKey = CardFaceMipBake.ContentKeyOf(source, out string? keyRefusal);
            if (contentKey == null)
            {
                // Unextractable art (rotated/tight packing) — same verdict the punch itself
                // would reach; nothing to latch because there is no content key. Rare enough
                // (and Debug-level) that the repeat is acceptable.
                VRLog.Debug("Cards", $"CARD OUTLINE ({kind}): cannot key '{source.name}' — " +
                                     $"{keyRefusal ?? "no region"}. Geometric punch unavailable; the " +
                                     "ModBuild-112 luma-BFS punch remains the fallback (today's look).");
                return null;
            }
            if (s_bySourceKey.TryGetValue(contentKey, out CardOutline? known))
            {
                if (known != null)
                    s_byKind[(int)kind] = known;
                return known;
            }

            Color32[]? slice = CardFaceMipBake.SlicePixelsFor(source, out int w, out int h,
                out _, out string? sliceRefusal);
            if (slice == null)
            {
                s_bySourceKey[contentKey] = null; // latched — no retry storms
                VRLog.Info("Cards", $"CARD OUTLINE ({kind}): cannot read '{source.name}' — " +
                                    $"{sliceRefusal ?? "no pixels"}. Geometric punch unavailable; the " +
                                    "ModBuild-112 luma-BFS punch remains the fallback (today's look).");
                return null;
            }

            CardOutline? made = Derive(slice, w, h, drawnFaceRect, contentKey, source.name,
                out string? refusal, out int lastThreshold);
            s_bySourceKey[contentKey] = made;
            if (made == null)
            {
                VRLog.Info("Cards", $"CARD OUTLINE refused ({kind}): '{source.name}' {w}x{h} — {refusal} " +
                                    $"(last threshold tried {lastThreshold}). Geometric punch unavailable " +
                                    "for faces carrying this art; the ModBuild-112 luma-BFS punch remains " +
                                    "the fallback and the look degrades to exactly today's, never to a " +
                                    "wrong shape.");
                return null;
            }
            s_byKind[(int)kind] = made;
            VRLog.Info("Cards", $"CARD OUTLINE ({kind}): derived from '{source.name}' {w}x{h} at bright " +
                                $"threshold {made.Threshold} — bands {made.BandSummary}. These four numbers " +
                                "ARE the geometry every consumer now shares (sprite punch, mesh footprint, " +
                                "disabled shape mask); if a visible band disagrees with them, the derivation " +
                                "is wrong and THIS is the line that says so.");
            return made;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"CARD OUTLINE ({kind}) derivation failed ({ex.GetType().Name}: " +
                                $"{ex.Message}) — geometric punch unavailable, today's look kept.");
            return null;
        }
    }

    /// <summary>
    /// Is a FACE-normalized point (u right, v up, 0..1 across the face rect) inside the card's
    /// outline? Points outside the source's drawn rect are outside by definition (the art draws
    /// nothing there — the round-7 letterbox band, if any, is frame).
    /// <para>Boundary convention: <c>_left</c>/<c>_bottom</c> are the NEAR edge of the first
    /// bright pixel and inclusive as-is, but <c>_right</c>/<c>_top</c> store the last bright
    /// pixel's INDEX, whose far edge is index + 1 — without the +1 a pixel-center sample
    /// (x + 0.5) would erase the outermost trim pixel itself on the right and top.</para>
    /// </summary>
    internal bool InsideFace(float u, float v)
    {
        float sx = (u - _drawnFaceRect.xMin) / _drawnFaceRect.width * _w;
        float sy = (v - _drawnFaceRect.yMin) / _drawnFaceRect.height * _h;
        if (sx < 0f || sy < 0f || sx >= _w || sy >= _h)
            return false;
        int row = (int)sy;
        int col = (int)sx;
        return sx >= _left[row] && sx <= _right[row] + 1f && sy >= _bottom[col] && sy <= _top[col] + 1f;
    }

    // ------------------------------------------------------------------ derivation --

    private static CardOutline? Derive(Color32[] px, int w, int h, Rect drawnFaceRect,
                                       string contentKey, string sourceName,
                                       out string? refusal, out int lastThreshold)
    {
        refusal = "no threshold on the ladder produced a valid contour";
        lastThreshold = 0;
        if (drawnFaceRect.width <= 0.01f || drawnFaceRect.height <= 0.01f)
        {
            refusal = $"degenerate drawn rect {drawnFaceRect.width:F3}x{drawnFaceRect.height:F3}";
            return null;
        }
        var luma = new byte[px.Length];
        var opaque = new bool[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            Color32 c = px[i];
            opaque[i] = c.a >= 128;
            luma[i] = (byte)((c.r * 299 + c.g * 587 + c.b * 114) / 1000);
        }
        foreach (int threshold in ThresholdLadder)
        {
            lastThreshold = threshold;
            CardOutline? made = TryDerive(luma, opaque, w, h, drawnFaceRect, contentKey, sourceName,
                threshold, out string? why);
            if (made != null)
                return made;
            refusal = why;
        }
        return null;
    }

    private static CardOutline? TryDerive(byte[] luma, bool[] opaque, int w, int h,
                                          Rect drawnFaceRect, string contentKey, string sourceName,
                                          int threshold, out string? refusal)
    {
        // Per-row first/last bright column; per-column first/last bright row.
        var rowFirst = new float[h];
        var rowLast = new float[h];
        var colFirst = new float[w];
        var colLast = new float[w];
        for (int i = 0; i < w; i++)
        {
            colFirst[i] = -1f;
            colLast[i] = -1f;
        }
        for (int y = 0; y < h; y++)
        {
            rowFirst[y] = -1f;
            rowLast[y] = -1f;
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                if (!opaque[i] || luma[i] < threshold)
                    continue;
                if (rowFirst[y] < 0f)
                    rowFirst[y] = x;
                rowLast[y] = x;
                if (colFirst[x] < 0f)
                    colFirst[x] = y;
                colLast[x] = y;
            }
        }

        if (!CloseEdges(rowFirst, rowLast, h, out int rowLo, out int rowHi, out string? rowWhy))
        {
            refusal = $"rows: {rowWhy}";
            return null;
        }
        if (!CloseEdges(colFirst, colLast, w, out int colLo, out int colHi, out string? colWhy))
        {
            refusal = $"columns: {colWhy}";
            return null;
        }
        if (rowHi - rowLo + 1 < h * MinExtentFraction || colHi - colLo + 1 < w * MinExtentFraction)
        {
            refusal = $"contour extent {colHi - colLo + 1}x{rowHi - rowLo + 1} px spans less than " +
                      $"{MinExtentFraction:P0} of the {w}x{h} sprite — bright content is not a card frame";
            return null;
        }

        MedianFilter5(rowFirst, rowLo, rowHi);
        MedianFilter5(rowLast, rowLo, rowHi);
        MedianFilter5(colFirst, colLo, colHi);
        MedianFilter5(colLast, colLo, colHi);

        // Central-60 % stability — THE gate that caught the screenshot replay's bad edge.
        float iqrL = CentralIqr(rowFirst, rowLo, rowHi);
        float iqrR = CentralIqr(rowLast, rowLo, rowHi);
        float iqrB = CentralIqr(colFirst, colLo, colHi);
        float iqrT = CentralIqr(colLast, colLo, colHi);
        float iqrGateX = w * MaxCentralIqrFraction;
        float iqrGateY = h * MaxCentralIqrFraction;
        if (iqrL > iqrGateX || iqrR > iqrGateX || iqrB > iqrGateY || iqrT > iqrGateY)
        {
            refusal = $"an edge is unstable (central-60 % IQR left/right {iqrL:F0}/{iqrR:F0} px vs gate " +
                      $"{iqrGateX:F0}, bottom/top {iqrB:F0}/{iqrT:F0} px vs gate {iqrGateY:F0}) — the scan " +
                      "latched onto interior content instead of the trim contour at this threshold";
            return null;
        }

        // Empty rows/columns outside the extent: entirely outside the outline.
        for (int y = 0; y < h; y++)
        {
            if (y < rowLo || y > rowHi)
            {
                rowFirst[y] = float.MaxValue;
                rowLast[y] = float.MinValue;
            }
        }
        for (int x = 0; x < w; x++)
        {
            if (x < colLo || x > colHi)
            {
                colFirst[x] = float.MaxValue;
                colLast[x] = float.MinValue;
            }
        }

        // Band widths in FACE fractions, from the central-60 % medians (corners excluded).
        float medL = CentralMedian(rowFirst, rowLo, rowHi);
        float medR = CentralMedian(rowLast, rowLo, rowHi);
        float medB = CentralMedian(colFirst, colLo, colHi);
        float medT = CentralMedian(colLast, colLo, colHi);
        float bandLeft = drawnFaceRect.xMin + medL / w * drawnFaceRect.width;
        float bandRight = 1f - (drawnFaceRect.xMin + (medR + 1f) / w * drawnFaceRect.width);
        float bandBottom = drawnFaceRect.yMin + medB / h * drawnFaceRect.height;
        float bandTop = 1f - (drawnFaceRect.yMin + (medT + 1f) / h * drawnFaceRect.height);
        if (bandLeft < 0f || bandRight < 0f || bandTop < 0f || bandBottom < 0f
            || bandLeft > MaxBandFraction || bandRight > MaxBandFraction
            || bandTop > MaxBandFraction || bandBottom > MaxBandFraction)
        {
            refusal = $"implausible band widths (top {bandTop:P1}, bottom {bandBottom:P1}, left " +
                      $"{bandLeft:P1}, right {bandRight:P1}; gate 0..{MaxBandFraction:P0} of the face)";
            return null;
        }

        var made = new CardOutline(w, h, rowFirst, rowLast, colFirst, colLast, drawnFaceRect,
            contentKey, sourceName, threshold, bandLeft, bandRight, bandTop, bandBottom);

        // Inside-area sanity, sampled on a 48×48 face grid.
        const int probes = 48;
        int inside = 0;
        for (int py = 0; py < probes; py++)
        {
            float v = (py + 0.5f) / probes;
            for (int qx = 0; qx < probes; qx++)
            {
                if (made.InsideFace((qx + 0.5f) / probes, v))
                    inside++;
            }
        }
        float area = (float)inside / (probes * probes);
        if (area < MinAreaFraction || area > MaxAreaFraction)
        {
            refusal = $"inside area {area:P1} of the face outside [{MinAreaFraction:P0}, {MaxAreaFraction:P0}]";
            return null;
        }
        refusal = null;
        return made;
    }

    /// <summary>Contour extent + interior gap interpolation for one first/last edge pair
    /// (-1 = no hit). False with a reason when the contour is open beyond the gates.</summary>
    private static bool CloseEdges(float[] first, float[] last, int n,
                                   out int lo, out int hi, out string? why)
    {
        lo = -1;
        hi = -1;
        for (int i = 0; i < n; i++)
        {
            if (first[i] >= 0f)
            {
                if (lo < 0)
                    lo = i;
                hi = i;
            }
        }
        if (lo < 0)
        {
            why = "no bright pixel at all";
            return false;
        }
        int gapRunGate = Mathf.Max(1, Mathf.RoundToInt(n * MaxGapRunFraction));
        int totalGate = Mathf.Max(1, Mathf.RoundToInt(n * MaxTotalGapFraction));
        int total = 0;
        int run = 0;
        int prevHit = lo;
        for (int i = lo; i <= hi; i++)
        {
            if (first[i] >= 0f)
            {
                if (run > 0)
                {
                    // Linear interpolation across the gap — the contour is continuous.
                    float f0 = first[prevHit], f1 = first[i];
                    float l0 = last[prevHit], l1 = last[i];
                    for (int k = 1; k <= run; k++)
                    {
                        float t = (float)k / (run + 1);
                        first[prevHit + k] = f0 + (f1 - f0) * t;
                        last[prevHit + k] = l0 + (l1 - l0) * t;
                    }
                    run = 0;
                }
                prevHit = i;
                continue;
            }
            run++;
            total++;
            if (run > gapRunGate)
            {
                why = $"the contour is open for {run}+ consecutive rows/columns (gate {gapRunGate})";
                return false;
            }
        }
        if (total > totalGate)
        {
            why = $"{total} rows/columns inside the contour have no bright pixel (gate {totalGate})";
            return false;
        }
        why = null;
        return true;
    }

    /// <summary>5-tap median filter inside [lo..hi] — kills single-row outliers (a bright speck
    /// in the frame) while preserving the monotone ramps of a chamfered corner.</summary>
    private static void MedianFilter5(float[] edge, int lo, int hi)
    {
        if (hi - lo < 4)
            return;
        var copy = (float[])edge.Clone();
        var window = new float[5];
        for (int i = lo + 2; i <= hi - 2; i++)
        {
            for (int k = 0; k < 5; k++)
                window[k] = copy[i - 2 + k];
            System.Array.Sort(window);
            edge[i] = window[2];
        }
    }

    private static float CentralIqr(float[] edge, int lo, int hi)
    {
        float[] c = CentralSlice(edge, lo, hi);
        if (c.Length < 4)
            return 0f;
        System.Array.Sort(c);
        return c[(int)(c.Length * 0.75f)] - c[(int)(c.Length * 0.25f)];
    }

    private static float CentralMedian(float[] edge, int lo, int hi)
    {
        float[] c = CentralSlice(edge, lo, hi);
        if (c.Length == 0)
            return 0f;
        System.Array.Sort(c);
        return c[c.Length / 2];
    }

    /// <summary>The central 60 % of an edge array's extent — corner ramps excluded, so the
    /// medians/IQR describe the straight part of each edge.</summary>
    private static float[] CentralSlice(float[] edge, int lo, int hi)
    {
        int n = hi - lo + 1;
        int from = lo + (int)(n * 0.2f);
        int to = lo + (int)(n * 0.8f);
        var c = new float[Mathf.Max(0, to - from)];
        for (int i = from; i < to; i++)
            c[i - from] = edge[i];
        return c;
    }
}
