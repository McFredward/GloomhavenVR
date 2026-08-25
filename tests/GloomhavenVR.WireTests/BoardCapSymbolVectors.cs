using GloomhavenVR.Cards;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE TWO NEW PURE FUNCTIONS OF THE BOARD-BUTTON OVERHAUL — which cell of a keycap atlas a role
/// samples, and how far into its travel a pressed cap is.
///
/// <para>Neither is a wire format, and they are on this harness for the reason the seat clamps
/// beside them are: <b>every other gate in this repository agrees with them when they are
/// wrong.</b> The build compiles, the mirrors agree, the bundle loads, the wire coverage is
/// unchanged — and Undo wears an anchor, or the "spring-back past rest" in the comment never
/// crosses rest at all.</para>
///
/// <para><b>THE CELL MAP HAS ALREADY FAILED ONCE IN THIS REPOSITORY, one directory over.</b>
/// <c>unity/board-prep/tex_symbols.cell_box</c> read a cell letter as a COLUMN and its number as a
/// ROW — the transpose of the grid the generation prompts had asked for — and six of nine motifs
/// came back as the wrong device, with entirely plausible coverage numbers throughout. That was
/// caught by a person looking at a contact sheet. The same mistake here is worse and quieter: the
/// atlas rows are only distinguishable by what is carved into them, and the failure is symmetric
/// across the owner's board and every peer's mirror of it, because both sides resolve the cell
/// through the same call. So the properties are checked, not the pictures.</para>
///
/// <para><b>AND THE STROKE'S OVERSHOOT WAS WRONG WHEN IT WAS FIRST WRITTEN.</b> The release leg
/// started life as a decaying sine added to an ease-out. The arithmetic says such a sum never goes
/// negative — the ease dominates while the oscillation is largest and the oscillation has died by
/// the time the ease has not — so the cap would have ridden smoothly back to rest while a comment
/// three lines up described it springing past. It was caught on paper before it shipped, and these
/// vectors are what stop the next revision losing it again.</para>
/// </summary>
internal static class BoardCapSymbolVectors
{
    /// <summary>Float equality within a stated tolerance — the same helper and the same argument as
    /// <see cref="BoardSeatVectors"/>: these are computed fractions, not bytes.</summary>
    private static void Near(Harness t, string what, float expected, float actual, float tol) =>
        t.True(Mathf.Abs(expected - actual) <= tol,
               $"{what}: expected {expected:F6}, got {actual:F6} (tolerance {tol:G})");

    /// <summary>The shipped atlas: 1024 x 1024, 4 x 4 cells of 256 texels
    /// (<c>unity/board-prep/buttons/cap_atlas.py --cell 256</c>). Both axes are driven, and a
    /// NON-SQUARE atlas is driven too, because the U and V halves of the arithmetic are separate
    /// and a square test cannot tell them apart.</summary>
    private const int AtlasPx = 1024;

    internal static void Run(Harness t)
    {
        EveryCellIsInsideTheAtlas(t);
        NoTwoCellsOverlap(t);
        RowZeroIsTheTOPOfTheImage(t);
        ColumnsRunLeftToRight(t);
        NonSquareAtlasSplitsUAndVSeparately(t);
        OutOfRangeFallsBackToPlain(t);
        DegenerateAtlasIsIdentityNotNaN(t);
        TheRoundBezelIsItsOwnCell(t);

        StrokeStartsAndEndsAtRest(t);
        StrokeReachesTheBottomAndHoldsIt(t);
        StrokeAttackIsMonotone(t);
        StrokeCrossesRestExactlyOnce(t);
        StrokeReallyOvershoots(t);
    }

    // ---------------------------------------------------------------- the cell map -------------

    private static void Cell(int role, out Vector2 scale, out Vector2 offset) =>
        CapCellMath.Cell(AtlasPx, AtlasPx, role, out scale, out offset);

    /// <summary>Every cell's rectangle lies wholly inside 0..1. A rectangle that runs past the edge
    /// samples the clamped border row for part of a cap face, which reads as a smeared streak
    /// across the key rather than as a wrong symbol — the harder failure to attribute.</summary>
    private static void EveryCellIsInsideTheAtlas(Harness t)
    {
        for (int i = 0; i < CapCellMath.CellCount; i++)
        {
            Cell(i, out Vector2 s, out Vector2 o);
            t.True(s.x > 0f && s.y > 0f, $"cell {i}: scale must be positive, got {s}");
            t.True(o.x >= 0f && o.y >= 0f, $"cell {i}: offset must be >= 0, got {o}");
            t.True(o.x + s.x <= 1f + 1e-6f && o.y + s.y <= 1f + 1e-6f,
                   $"cell {i}: {o} + {s} runs past the atlas edge");
        }
    }

    /// <summary>No two cells share a texel. This is the property that makes a role a role: if two
    /// rectangles overlapped, one cap would show a slice of another's symbol along an edge and the
    /// defect would look like a texture-filtering artefact rather than a layout error.</summary>
    private static void NoTwoCellsOverlap(Harness t)
    {
        for (int a = 0; a < CapCellMath.CellCount; a++)
        {
            Cell(a, out Vector2 sa, out Vector2 oa);
            for (int b = a + 1; b < CapCellMath.CellCount; b++)
            {
                Cell(b, out Vector2 sb, out Vector2 ob);
                bool apart = oa.x + sa.x <= ob.x + 1e-6f || ob.x + sb.x <= oa.x + 1e-6f
                          || oa.y + sa.y <= ob.y + 1e-6f || ob.y + sb.y <= oa.y + 1e-6f;
                t.True(apart, $"cells {a} and {b} overlap: {oa}+{sa} against {ob}+{sb}");
            }
        }
    }

    /// <summary>
    /// THE ROW FLIP, stated as the thing it actually means. Cell 0 is drawn at the TOP-LEFT of the
    /// image by the Python, and Unity's V runs from the BOTTOM — so cell 0's V offset must be the
    /// HIGHEST of any row, not the lowest. Getting this backwards mirrors the whole grid vertically
    /// and swaps every role with the one two rows away, which is precisely the transpose bug the
    /// board's own motif reader shipped.
    /// </summary>
    private static void RowZeroIsTheTOPOfTheImage(Harness t)
    {
        Cell(0, out _, out Vector2 topLeft);                    // row 0, col 0
        Cell(CapCellMath.GridCols, out _, out Vector2 nextRow); // row 1, col 0
        t.True(topLeft.y > nextRow.y,
               $"row 0 must sit HIGHER in V than row 1 (Unity V runs from the bottom): "
               + $"row0 v={topLeft.y:F4}, row1 v={nextRow.y:F4}");
        // …and the top row must be the topmost band of the texture.
        float band = 1f / CapCellMath.GridRows;
        Near(t, "cell 0 v offset", (CapCellMath.GridRows - 1) * band + CapCellMath.InsetTexels / (float)AtlasPx,
             topLeft.y, 1e-6f);
    }

    /// <summary>Columns run left to right, U ascending, and the four cells of the top row are the
    /// four roles the design table puts on one row (Plain, Confirm, Undo, Skip).</summary>
    private static void ColumnsRunLeftToRight(Harness t)
    {
        float prev = -1f;
        for (int c = 0; c < CapCellMath.GridCols; c++)
        {
            Cell(c, out _, out Vector2 o);
            t.True(o.x > prev, $"cell {c}: U offset {o.x:F4} must exceed the previous column's {prev:F4}");
            prev = o.x;
        }
        // The roles that share the top row, so a renumbering of CapRole cannot silently move a
        // symbol to a different row without this failing.
        t.True((int)CapRole.Plain == 0 && (int)CapRole.Confirm == 1
               && (int)CapRole.Undo == 2 && (int)CapRole.Skip == 3,
               "CapRole 0..3 are the top row of the atlas (Plain, Confirm, Undo, Skip) — renumbering "
               + "them without re-authoring unity/board-prep/buttons/cap_atlas.py puts the wrong "
               + "symbol on every cap on every board");
        t.True((int)CapRole.ItemUse == 4 && (int)CapRole.ShortRest == 5 && (int)CapRole.LongRest == 6
               && (int)CapRole.FixedPinned == 7,
               "CapRole 4..7 are the second row of the atlas (ItemUse, ShortRest, LongRest, FixedPinned)");
        t.True((int)CapRole.FixedFollow == 8, "CapRole.FixedFollow is the first cell of the third row");
        t.True((int)CapRole.PlainRound == 9,
               "CapRole.PlainRound is the SECOND cell of the third row — the round-registered bezel "
               + "band. It is not a control and never appears on a cap FACE; moving it re-aims every "
               + "round cap's bezel ring and side walls at whatever cell 9 has become");
    }

    /// <summary>
    /// U AND V ARE SEPARATE HALVES OF THE ARITHMETIC and a square atlas cannot tell them apart. On a
    /// 2048 x 512 atlas the cell is 512 wide and 128 tall, and the two insets are different
    /// fractions — so a copy-paste that used the width in the V term (or vice versa) is invisible at
    /// 1024 x 1024 and shows here.
    /// </summary>
    private static void NonSquareAtlasSplitsUAndVSeparately(Harness t)
    {
        CapCellMath.Cell(2048, 512, 0, out Vector2 s, out Vector2 o);
        Near(t, "wide atlas cell width", 1f / CapCellMath.GridCols - 2f * (CapCellMath.InsetTexels / 2048f), s.x, 1e-7f);
        Near(t, "wide atlas cell height", 1f / CapCellMath.GridRows - 2f * (CapCellMath.InsetTexels / 512f), s.y, 1e-7f);
        Near(t, "wide atlas cell u", CapCellMath.InsetTexels / 2048f, o.x, 1e-7f);
        Near(t, "wide atlas cell v",
             (CapCellMath.GridRows - 1) / (float)CapCellMath.GridRows + CapCellMath.InsetTexels / 512f,
             o.y, 1e-7f);
    }

    /// <summary>A role outside the grid resolves to the PLAIN cell rather than throwing. A wrong
    /// symbol is a defect somebody reports; an exception inside a material builder is a board that
    /// does not draw at all, and the caller here is the one place both boards' caps are minted.</summary>
    private static void OutOfRangeFallsBackToPlain(Harness t)
    {
        Cell(0, out Vector2 s0, out Vector2 o0);
        foreach (int bad in new[] { -1, CapCellMath.CellCount, 9999 })
        {
            Cell(bad, out Vector2 s, out Vector2 o);
            Near(t, $"role {bad} -> plain scale.x", s0.x, s.x, 1e-7f);
            Near(t, $"role {bad} -> plain offset.y", o0.y, o.y, 1e-7f);
        }
    }

    /// <summary>
    /// THE ROUND BEZEL IS A DIFFERENT CELL FROM THE SQUARE ONE, AND THE SHAPE PICKS IT — the whole
    /// content of the fix, stated as the two properties that can regress.
    ///
    /// <para><b>WHY IT CAN GO WRONG SILENTLY.</b> Cells 0 and 9 are the SAME gold band drawn at the
    /// same size in the same palette; the only difference is what it is registered against — a
    /// square with mitred corners, or a circle. Every other gate in this repository agrees with a
    /// round cap wearing cell 0: the material builds, the atlas loads, the mirrors agree, the wire
    /// coverage is unchanged, and the cap renders in exactly the right colour. It shipped that way
    /// and the user had to report it from a screenshot ("viereckige Texturen" on the round rest
    /// buttons). So the two things a future edit could break are pinned here: that the two cells are
    /// genuinely DIFFERENT rectangles, and that <see cref="CapCellMath.PlainCell"/> — the one
    /// resolver the owner's <c>BoardButton.Create</c> and the peer mirror's
    /// <c>InertCap.Round</c>/<c>Square</c> all call — maps the shape to the right one.</para>
    /// </summary>
    private static void TheRoundBezelIsItsOwnCell(Harness t)
    {
        t.True(CapCellMath.PlainCell(round: false) == CapRole.Plain,
               "a SQUARE cap's bezel and walls take the square-registered band, cell 0");
        t.True(CapCellMath.PlainCell(round: true) == CapRole.PlainRound,
               "a ROUND cap's bezel and walls take the circle-registered band, cell 9 — taking cell 0 "
               + "paints a mitred rectangle inside a circular cap, which is the defect this exists for");
        t.True(CapCellMath.PlainCell(round: true) != CapCellMath.PlainCell(round: false),
               "the round and the square bezel cells must not be the same cell");

        // The cell itself: column 1 of the image's THIRD row, i.e. the second row counted from the
        // bottom in V. Written out rather than derived from the function under test, because a
        // vector that recomputes its own expectation cannot catch the row flip.
        Cell((int)CapRole.PlainRound, out Vector2 s, out Vector2 o);
        float band = 1f / CapCellMath.GridRows;
        float inset = CapCellMath.InsetTexels / (float)AtlasPx;
        Near(t, "cell 9 scale.x", 1f / CapCellMath.GridCols - 2f * inset, s.x, 1e-6f);
        Near(t, "cell 9 scale.y", band - 2f * inset, s.y, 1e-6f);
        Near(t, "cell 9 offset.x", 1f / CapCellMath.GridCols + inset, o.x, 1e-6f);
        Near(t, "cell 9 offset.y", band + inset, o.y, 1e-6f);

        // It sits immediately RIGHT of FixedFollow (cell 8) on the same row — the property that
        // fails first if either the enum or the Python's CELLS list is renumbered on one side only.
        Cell((int)CapRole.FixedFollow, out Vector2 s8, out Vector2 o8);
        Near(t, "cell 9 shares cell 8's row", o8.y, o.y, 1e-6f);
        t.True(o.x > o8.x, $"cell 9 must sit right of cell 8: u {o.x:F4} vs {o8.x:F4}");
        Near(t, "cell 9 is one column further", o8.x + 1f / CapCellMath.GridCols, o.x, 1e-6f);
        t.True(s == s8, $"every cell is the same size: cell 9 {s} vs cell 8 {s8}");
    }

    /// <summary>A zero-sized atlas must not divide by zero. NaN in a texture transform paints the
    /// cap black, which reads as "the texture never loaded" and sends the next round looking at the
    /// bundle instead of at this arithmetic.</summary>
    private static void DegenerateAtlasIsIdentityNotNaN(Harness t)
    {
        CapCellMath.Cell(0, 0, 3, out Vector2 s, out Vector2 o);
        t.True(s == Vector2.one && o == Vector2.zero,
               $"a 0x0 atlas must give the identity transform, got scale {s} offset {o}");
    }

    // ---------------------------------------------------------------- the press stroke ---------

    /// <summary>At rest before the press, and at rest after the stroke — exactly, and for ever.
    /// The cap's seat is written unconditionally from this value, so a stroke that ends at
    /// 0.003 instead of 0 leaves every key on the board a fraction of a millimetre depressed.</summary>
    private static void StrokeStartsAndEndsAtRest(Harness t)
    {
        Near(t, "stroke at t=0", 0f, ButtonStroke.Depth01(0f), 0f);
        Near(t, "stroke before t=0", 0f, ButtonStroke.Depth01(-1f), 0f);
        Near(t, "stroke at the end", 0f, ButtonStroke.Depth01(ButtonStroke.StrokeSeconds), 0f);
        Near(t, "stroke past the end", 0f, ButtonStroke.Depth01(ButtonStroke.StrokeSeconds + 10f), 0f);
    }

    /// <summary>It reaches the bottom of the travel and STAYS there for the detent. That hold is
    /// what makes the press read as a key bottoming out; without it the cap turns around at its
    /// lowest point and the whole stroke reads as a slide.</summary>
    private static void StrokeReachesTheBottomAndHoldsIt(Harness t)
    {
        Near(t, "stroke at the end of the attack", 1f,
             ButtonStroke.Depth01(ButtonStroke.AttackSeconds), 1e-6f);
        Near(t, "stroke mid-detent", 1f,
             ButtonStroke.Depth01(ButtonStroke.AttackSeconds + ButtonStroke.HoldSeconds * 0.5f), 1e-6f);
        Near(t, "stroke at the end of the detent", 1f,
             ButtonStroke.Depth01(ButtonStroke.AttackSeconds + ButtonStroke.HoldSeconds * 0.999f), 1e-6f);
    }

    /// <summary>The attack only ever goes DOWN — a cap that stutters on the way in reads as the
    /// button resisting a press the player has already committed to.</summary>
    private static void StrokeAttackIsMonotone(Harness t)
    {
        float prev = -1f;
        for (int i = 0; i <= 40; i++)
        {
            float d = ButtonStroke.Depth01(ButtonStroke.AttackSeconds * i / 40f);
            t.True(d >= prev - 1e-6f, $"attack step {i}: depth went back up ({prev:F4} -> {d:F4})");
            prev = d;
        }
    }

    /// <summary>
    /// ONE crossing of rest, not zero and not three. Zero means the "overshoot" in the comment is
    /// fiction (the failure the first draft of this curve actually had); three means the cap
    /// oscillates, which on a 4 mm travel reads as a wobble rather than as a spring.
    /// </summary>
    private static void StrokeCrossesRestExactlyOnce(Harness t)
    {
        int crossings = 0;
        float prev = ButtonStroke.Depth01(0f);
        const int steps = 2000;
        for (int i = 1; i <= steps; i++)
        {
            float d = ButtonStroke.Depth01(ButtonStroke.StrokeSeconds * i / steps);
            if (prev > 0f && d <= 0f)
                crossings++;
            prev = d;
        }
        t.True(crossings == 1, $"the stroke must cross rest exactly once on its way back, counted {crossings}");
    }

    /// <summary>
    /// THE OVERSHOOT IS REAL AND IT IS THE AUTHORED SIZE. This is the assertion the whole file
    /// exists for: the first version of the release leg was a decaying sine added to an ease-out,
    /// which cannot go negative at all, and nothing else in the build would have said so.
    /// </summary>
    private static void StrokeReallyOvershoots(Harness t)
    {
        float min = float.MaxValue;
        float atMin = 0f;
        const int steps = 4000;
        for (int i = 0; i <= steps; i++)
        {
            float s = ButtonStroke.StrokeSeconds * i / steps;
            float d = ButtonStroke.Depth01(s);
            if (d < min) { min = d; atMin = s; }
        }
        Near(t, "stroke overshoot depth", -ButtonStroke.ReboundFraction, min, 1e-3f);
        // …and it happens in the release, at the authored split — not right at the end, where it
        // would be indistinguishable from the settle, and not right after the detent, where it
        // would look like the cap being yanked out.
        float expectedAt = ButtonStroke.AttackSeconds + ButtonStroke.HoldSeconds
                           + ButtonStroke.ReleaseSeconds * ButtonStroke.ReboundSplit;
        Near(t, "stroke overshoot timing", expectedAt, atMin, 2e-3f);
    }
}
