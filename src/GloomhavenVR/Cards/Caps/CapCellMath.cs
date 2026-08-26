using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// WHICH control a keycap is. The cap's SYMBOL and its per-board MATERIAL are both resolved
/// from this one value, so a cap has to say what it is exactly once.
///
/// <para>The numbering is the CELL INDEX in the per-style keycap atlas
/// (<c>unity/board-prep/buttons/cap_atlas.py</c>, <c>CELLS</c>) and is therefore load-bearing on
/// both sides of the wire: the owner's board and every peer's mirror of it resolve the cell
/// through the same <see cref="PlayTray.NewKeycapMaterial"/>, so a renumber that is not made in
/// the Python puts the wrong symbol on every cap everywhere at once. It is not itself a wire
/// field — nothing transmits a role — because both sides derive it from the same board layout.</para>
/// </summary>
internal enum CapRole : byte
{
    /// <summary>No symbol: plain style material. The bevel ring and the side walls of EVERY cap
    /// take this cell, and so does any cap whose role has no symbol yet.</summary>
    Plain = 0,
    Confirm = 1,
    Undo = 2,
    Skip = 3,
    ItemUse = 4,
    ShortRest = 5,
    LongRest = 6,

    /// <summary>The follow/pin toggle while the board is PINNED ("FIXIERT") — an anchor.</summary>
    FixedPinned = 7,

    /// <summary>The follow/pin toggle while the board FOLLOWS the player ("FOLGEN") — two
    /// footprints. The toggle swaps between this and <see cref="FixedPinned"/> live
    /// (<c>BoardButton.SetCapRole</c>), which is two floats on a material instance, not a
    /// rebuild.</summary>
    FixedFollow = 8,

    /// <summary>
    /// THE ROUND SIBLING OF <see cref="Plain"/> — the bezel/wall cell for a cap whose footprint is
    /// a DISC. It is not a control: nothing is ever "the PlainRound button", no press resolves to
    /// it, and no cap FACE ever samples it. It exists because <see cref="Plain"/> is
    /// <b>square-registered</b> art.
    ///
    /// <para><b>THE DEFECT IT FIXES.</b> Cell 0 is drawn by
    /// <c>unity/board-prep/buttons/cap_atlas.py</c> with <c>cellkind="bezel"</c>, which registers
    /// the gold band against a SQUARE (<c>cap_object.register_square</c>) — four straight runs
    /// meeting at mitred corners. Submeshes [1] (bezel) and [2] (wall) of every cap took that cell,
    /// round caps included, so a round rest disc painted a square gold frame with visible mitres
    /// inside a circular cap — reported by the user as square textures on the round buttons. Cell 9
    /// is the same band registered against the CIRCLE, so a disc's bezel ring closes on itself
    /// instead of turning four corners.</para>
    ///
    /// <para>A round cap and a square one must never be able to disagree about which of the two
    /// they take, and the owner's board and every peer's mirror of it least of all — so neither
    /// picks the cell inline: both go through <see cref="CapCellMath.PlainCell"/>.</para>
    /// </summary>
    PlainRound = 9,
}

/// <summary>
/// WHERE ONE ROLE'S CELL IS IN A KEYCAP ATLAS — the arithmetic, and nothing else.
///
/// <para><b>WHY IT IS ITS OWN FILE.</b> This is the one part of <see cref="CapSymbols"/> that can be
/// wrong quietly. Everything else there either works or throws: the atlas loads or it does not, the
/// material takes the texture or it does not. This function can hand back a perfectly well-formed
/// rectangle that points at the wrong cell, and the result is a board where Undo wears the anchor
/// and the rest pad wears a check mark — on the owner's board AND on every peer's mirror of it,
/// because both sides resolve the cell through this same code.</para>
///
/// <para><b>AND THE BUG IS NOT HYPOTHETICAL IN THIS REPOSITORY.</b> The board's own motif reader
/// had exactly it: <c>unity/board-prep/tex_symbols.cell_box</c> read the cell LETTER as a column and
/// the NUMBER as a row — the transpose of the grid the prompts had asked for — and six of nine cells
/// came back as the wrong motif, with plausible coverage numbers throughout. What made it findable
/// was writing the convention down and checking it; that is what this file and its vectors are.</para>
///
/// <para><b>IT IS FREE OF UNITY OBJECTS ON PURPOSE</b> — ints in, two <c>Vector2</c>s out, no
/// <c>Texture2D</c>, no <c>AssetBundle</c>, no logging. That is what lets
/// <c>tests/GloomhavenVR.WireTests</c> LINK it and drive the real function rather than a copy of it,
/// the same reason <c>Core/WallStandingProp.cs</c> is written the way it is.</para>
/// </summary>
internal static class CapCellMath
{
    /// <summary>Atlas grid, mirrored from <c>unity/board-prep/buttons/cap_atlas.py:GRID</c>. Power
    /// of two on both axes so every compression format and every mip level stays available with no
    /// special case, and seven spare cells cost almost nothing (they are plain material).</summary>
    internal const int GridCols = 4;
    internal const int GridRows = 4;

    /// <summary>Total cells. A role index outside it is clamped to the plain cell rather than
    /// throwing: a wrong symbol is a defect, a thrown exception in a material builder is a board
    /// that does not draw.</summary>
    internal const int CellCount = GridCols * GridRows;

    /// <summary>
    /// How far INSIDE its cell a cap's UV range is placed, in atlas texels.
    ///
    /// <para>A cap's UVs run exactly 0..1 over its footprint (<c>CardMesh.BuildBeveledKeycap</c> and
    /// <c>BuildRoundCap</c> both map planar XY that way), so mapping 0..1 onto the raw cell
    /// rectangle puts the outermost sample ON the cell boundary, where bilinear filtering takes
    /// half its weight from the NEIGHBOURING cell. The side WALLS make that concrete rather than
    /// theoretical: every wall vertex carries its own edge's XY, so a wall samples a thin strip at
    /// exactly u = 0 or u = 1. Two texels of inset is 0.8 % of the cell at the shipped 256 and
    /// removes the crossing entirely.</para>
    /// </summary>
    internal const int InsetTexels = 2;

    /// <summary>
    /// WHICH NON-SYMBOL CELL THIS CAP'S BEZEL RING AND SIDE WALLS TAKE — the one decision, made in
    /// one place.
    ///
    /// <para>Submeshes [1] and [2] of every cap carry no symbol, but they are not
    /// shape-independent: <see cref="CapRole.Plain"/> is registered against a SQUARE and
    /// <see cref="CapRole.PlainRound"/> against a CIRCLE, and a disc wearing the square band draws
    /// a mitred rectangle inside a round cap.</para>
    ///
    /// <para><b>IT IS A FUNCTION RATHER THAN TWO LITERALS BECAUSE THE OWNER AND THE MIRROR MINT
    /// THESE MATERIALS IN DIFFERENT FILES.</b> <c>Cards.PlayTray.BoardButton.Create</c> builds the
    /// player's own rest discs and <c>Net.RemoteBoardFurniture.InertCap.Round</c> builds every
    /// peer's copy of them, through the same <c>PlayTray.NewKeycapMaterial</c> but from two call
    /// sites that no compiler or gate ties together. Two inline ternaries can drift; one call
    /// cannot, and the drift would show as a peer's disc wearing a different bezel from the one its
    /// owner is looking at — the exact class of asymmetry <see cref="CapRole"/>'s own remark about
    /// both sides deriving the cell from the same code is there to prevent.</para>
    /// </summary>
    internal static CapRole PlainCell(bool round) => round ? CapRole.PlainRound : CapRole.Plain;

    /// <summary>
    /// The atlas sub-rectangle for cell <paramref name="cellIndex"/>, as the
    /// <c>(scale, offset)</c> pair a material's texture transform wants.
    ///
    /// <para>Taken from the atlas's OWN pixel size rather than from a constant, so re-authoring the
    /// atlas at a different cell size needs no code change here and cannot silently disagree with
    /// the file that wrote it.</para>
    ///
    /// <para><b>THE ROW IS COUNTED FROM THE BOTTOM AND THAT IS THE WHOLE TRAP.</b> The Python lays
    /// the grid out row-major with row 0 at the TOP of the image, because that is how an image is
    /// read. Unity's V runs from the BOTTOM. The cap's own V also runs from the bottom
    /// (<c>Uv = y/height + 0.5</c>, so v grows with +Y), which is why the atlas is authored with
    /// image-top = cap-top and needs no flip INSIDE a cell — but the row INDEX has to be counted
    /// from the other end, and forgetting that mirrors the whole grid vertically: role 4 would draw
    /// role 0's cell and nothing would look broken enough to notice from one screenshot.</para>
    /// </summary>
    internal static void Cell(int atlasWidth, int atlasHeight, int cellIndex,
                             out Vector2 scale, out Vector2 offset)
    {
        if (cellIndex < 0 || cellIndex >= CellCount)
            cellIndex = 0;
        if (atlasWidth <= 0 || atlasHeight <= 0)
        {
            // A degenerate atlas would divide by zero and hand back NaN, which paints a cap black
            // and reads as "the texture never loaded". Identity is the honest answer: the whole
            // texture, i.e. exactly what a material with no transform does.
            scale = Vector2.one;
            offset = Vector2.zero;
            return;
        }
        int col = cellIndex % GridCols;
        int row = cellIndex / GridCols;
        int rowFromBottom = GridRows - 1 - row;
        float cw = atlasWidth / (float)GridCols;
        float ch = atlasHeight / (float)GridRows;
        float insetU = InsetTexels / (float)atlasWidth;
        float insetV = InsetTexels / (float)atlasHeight;
        scale = new Vector2(cw / atlasWidth - 2f * insetU, ch / atlasHeight - 2f * insetV);
        offset = new Vector2(col * cw / atlasWidth + insetU,
                             rowFromBottom * ch / atlasHeight + insetV);
    }
}
