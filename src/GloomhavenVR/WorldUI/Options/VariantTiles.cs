using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE THREE ASSET CHOICES ARE PICTURES, NOT A LIST OF WORDS.
///
/// <para>USER RULING (2026-09-02, verbatim): <i>"Hierbei will ich, dass das umstellen der Assets
/// etwas präsenter wird. Am Besten will ich dass die Umgebung (Wald, Keller, Default, Schwarz,
/// Mixed Reality) als Kacheln mit einem Bild darin angeboten werden. Genau für die Hände und
/// Masken."</i> Three pickers — environment, hands, masks — become a strip of picture tiles. Every
/// other setting in the pane keeps its row; this is not a new menu, it is a different control on
/// three rows that were dropdowns.</para>
///
/// <para><b>WHY A DROPDOWN WAS THE WRONG CONTROL HERE.</b> The other named-choice rows in this
/// window pick a BEHAVIOUR ("Laser only", "Always") and a word says it exactly. These three pick an
/// APPEARANCE, and "Runenschleier" tells a player nothing about what will be on their face. A
/// dropdown also hides every alternative until it is opened, which is the opposite of what the
/// ruling asks for ("präsenter").</para>
///
/// <para><b>THE FIVE ENVIRONMENT TILES ARE NOT ONE ENUM.</b> <see cref="SkyStyle"/> has four
/// members; "Mixed Reality" is a separate dial, <c>[MixedReality] Enabled</c>, and its own class
/// doc says so in as many words ("DELIBERATELY NOT MIXED REALITY: MR is its own dial with its own
/// precedence"). The player nevertheless experiences five alternatives, because MR OVERRIDES the
/// sky — with it on, no <see cref="SkyStyle"/> value is visible. So the strip presents the five
/// states the player can actually be in, and the two keys behind it are kept consistent:
/// <list type="bullet">
///   <item>picking a sky tile writes <c>[Sky] Style</c>, and clears <c>[MixedReality] Enabled</c>
///   ONLY IF it was set — otherwise the player would pick "Keller" and go on seeing passthrough;</item>
///   <item>picking the MR tile sets <c>[MixedReality] Enabled</c> and LEAVES <c>[Sky] Style</c>
///   ALONE, so turning MR off again returns the environment the player had chosen. MR is a mode
///   laid over the choice, not a replacement for it, and the config should survive it.</item>
/// </list>
/// No key changes its meaning, its range or its default, and no new key is introduced.</para>
///
/// <para><b>MULTIPLAYER.</b> Presentation only. The tiles write the same three entries the
/// dropdowns wrote, through the same <c>Apply</c> wrapper, so hand style and mask id keep riding
/// the wire exactly as before and the environment stays what it always was — each player's own
/// room. Nothing here reads a peer's copy of a key, and nothing here is mirrored.</para>
///
/// <para><b>ART SHIPS INSIDE THE PLUGIN DLL.</b> Eleven 320x240 PNGs, 425,715 bytes measured, as
/// <c>EmbeddedResource</c> — the route <see cref="EmbeddedTexture"/> exists for and states the case
/// for: the asset bundle is 74,558,728 bytes and putting eleven thumbnails in it would cost every
/// user a full re-install instead of a DLL drop. See <c>GloomhavenVR.csproj</c>.</para>
///
/// <para><b>THERE IS NEVER AN EMPTY STRIP.</b> A tile whose PNG is missing or undecodable keeps its
/// plate, its border and its label and stays clickable; the picker degrades to labelled tiles,
/// which is still a working control. <see cref="EmbeddedTexture.Get"/> caches the null, so a
/// missing resource costs one lookup for the process, not one per tile per page build.</para>
/// </summary>
internal static partial class VROptionsTab
{
    // ---- geometry -------------------------------------------------------------------------
    //
    // A FIXED TILE WIDTH, not a share of the row. Sharing the row would make the three-tile pickers
    // draw tiles half again as wide as the five-tile one, and the picture inside would then float in
    // the middle of its own plate (preserveAspect fits the 4:3 art to the SHORTER side). One width
    // means one apparent size across all three strips.
    private const float TileWidth = 232f;
    private const float TileHeight = 204f;
    private const float TileGap = 10f;

    /// <summary>
    /// THE PICTURE'S SIZE IS THE CONSTRAINT AND THE ROW COUNT IS THE FREE VARIABLE.
    ///
    /// <para>USER RULING (2026-09-05, verbatim): <i>"Statt die Kacheln bei den Umgebungen in den VR
    /// Optionen immer enger zu machen und das Bild darin immer kleiner, mach einfach zwei Reihen, so
    /// dass man das Bild aber noch gut erkennen kann."</i> The strip used to be ONE
    /// <c>HorizontalLayoutGroup</c> row whose tiles shrank toward a <c>TileMinWidth</c> of 116 — half
    /// the authored width — as environments were added, and the 4:3 art shrank with them. It now
    /// WRAPS instead.</para>
    ///
    /// <para>TWO ROWS IS NOT HARD-CODED, and deliberately so: two rows is what this floor happens to
    /// produce at five environments in today's pane. The rule is "a tile never goes below this width;
    /// add a row instead", so a sixth and seventh environment get a third row rather than a third
    /// round of shrinking. The number itself: a tile may lose at most a QUARTER of its authored width
    /// before the strip wraps. 116 px is the width that produced the complaint and 232 is the size
    /// the art was authored at, so the floor belongs near the authored end, not midway.</para>
    /// </summary>
    private const float TileMinLegibleWidth = TileWidth * 0.75f;

    /// <summary>Thickness of the border ring — the SELECTED marker. 5 px reads at arm's length;
    /// a 1 px outline is the kind of hairline that disappears in a headset.</summary>
    private const float TileBorder = 5f;

    /// <summary>Bottom strip of the tile that carries the name. The label is not optional: art
    /// alone cannot distinguish two dark rooms, and it is the whole control when art is missing.</summary>
    private const float TileLabelHeight = 27f;

    /// <summary>How small a tile label may get before <c>VROptionsTab.ProbeCaptionFit</c> reports
    /// it by name instead of shrinking further. Higher than the settings rows' floor on purpose: a
    /// tile is narrower than a row, and a 9-point word centred under a picture stops reading as the
    /// picture's name.</summary>
    private const float TileLabelFloor = 10f;

    private const float TileLabelSize = 16f;

    // ---- colours --------------------------------------------------------------------------
    //
    // Three redundant cues say "this one". A border alone is not enough in a headset (it is a thin
    // ring at the edge of vision), so the selected tile ALSO shows its picture at full brightness
    // while the others are dimmed, and its label goes bold and gold. Any one of the three read on
    // its own is enough to answer "which is on?".
    private static readonly Color TileBorderOn = new(0.85f, 0.69f, 0.30f, 1f);
    private static readonly Color TileBorderOnHot = new(1f, 0.85f, 0.45f, 1f);
    // OPAQUE, both of them (2026-09-05 transparency sweep). They shipped at 0.92 and 0.95, which is
    // an alpha nobody chose for a reason anybody wrote down — and an UNSELECTED tile's frame is the
    // one cue on this strip that has to read against whatever is behind the window. The difference
    // an 8% hole makes to the look is nil; the difference it makes to the rule "no mod-drawn
    // graphic in this menu is translucent" is the whole rule.
    private static readonly Color TileBorderOff = new(0.19f, 0.17f, 0.14f, 1f);
    private static readonly Color TileBorderOffHot = new(0.50f, 0.40f, 0.18f, 1f);

    /// <summary>A tile that cannot be chosen. Derived from <see cref="TileBorderOff"/> rather than
    /// authored, so the two can never be equal again by hand — which is the defect this replaced
    /// (B6): the disabled colour WAS the un-picked colour.</summary>
    private static readonly Color TileBorderDisabled =
        new(TileBorderOff.r * 0.5f, TileBorderOff.g * 0.5f, TileBorderOff.b * 0.5f, TileBorderOff.a);
    private static readonly Color TileBorderPressed = new(0.95f, 0.78f, 0.36f, 1f);
    private static readonly Color TilePlate = new(0.07f, 0.065f, 0.06f, 1f);
    private static readonly Color TilePictureOn = Color.white;
    private static readonly Color TilePictureOff = new(0.56f, 0.55f, 0.53f, 1f);
    private static readonly Color TileLabelOff = new(0.72f, 0.70f, 0.66f, 1f);

    /// <summary>Built sprites, by manifest name. The TEXTURE is already cached for the process by
    /// <see cref="EmbeddedTexture"/>; this caches the Sprite wrapper so re-opening the tab does not
    /// allocate eleven more of them. Misses are cached as null for the same reason.</summary>
    private static readonly Dictionary<string, Sprite?> TileSprites = new(11);

    /// <summary>One choice in a picture picker.</summary>
    private sealed class VariantTile
    {
        /// <summary>Manifest name of the embedded PNG, or empty for a tile that has no art.</summary>
        internal string Resource = string.Empty;

        internal Func<string> Label = () => string.Empty;

        internal Func<bool> Selected = () => false;

        /// <summary>
        /// Write the choice. Returns TRUE when the write changed which rows the pane should have,
        /// so the page needs a rebuild rather than a repaint.
        ///
        /// <para>It is a return value rather than a fixed flag on the tile because the only case is
        /// dynamic: a sky tile rebuilds the page ONLY IF it had to clear <c>[MixedReality]
        /// Enabled</c> on the way past, and whether it had to is not known until the click.
        /// <c>Apply</c> already covers the other two pickers on its own — hand style is a variant
        /// selector, and it rebuilds for those.</para>
        /// </summary>
        internal Func<bool> Choose = () => false;
    }

    /// <summary>
    /// The rows that are drawn as picture tiles instead of a control. A pure section/key table, the
    /// same shape as <c>HasSpecialRow</c> — the two never overlap in effect because this hook runs
    /// first and returns, so <c>[Sky] Style</c> and <c>[Net] MaskId</c> keep their dropdown
    /// definitions in the curated file as the code path nobody reaches while this one builds.
    /// </summary>
    private static bool HasVariantTiles(ConfigCatalog.ConfigItem item) =>
        (string.Equals(item.Section, "Sky", StringComparison.Ordinal)
         && string.Equals(item.Key, "Style", StringComparison.Ordinal))
        || (string.Equals(item.Section, "Hands", StringComparison.Ordinal)
            && string.Equals(item.Key, "HandStyle", StringComparison.Ordinal))
        || (string.Equals(item.Section, "Net", StringComparison.Ordinal)
            && string.Equals(item.Key, "MaskId", StringComparison.Ordinal));

    /// <summary>
    /// THE ONE ENTRY POINT. Returns false for every other setting, so a single call at the top of
    /// <c>BuildRow</c> is the whole integration.
    /// </summary>
    private static bool TryBuildVariantTiles(Transform parent, ConfigCatalog.ConfigItem item,
                                             string? caption, string? hintKey)
    {
        if (!HasVariantTiles(item))
            return false;

        VariantTile[]? tiles = VariantTilesFor(item);
        if (tiles == null || tiles.Length == 0)
            return false; // let the ordinary row ladder have it — never leave the setting unbuilt

        // The name of the setting, on its own line above the strip. A tile carries the name of the
        // CHOICE; the strip still has to say what is being chosen.
        BuildHeader(parent, Caption(item, caption), hintKey, sub: true);

        var strip = new GameObject("VariantTiles_" + item.Section + "_" + item.Key,
                                   typeof(RectTransform));
        var stripRect = (RectTransform)strip.transform;
        stripRect.SetParent(parent, worldPositionStays: false);
        Rows.Add(strip); // ClearRows owns it from here — a strip that outlived a rebuild would stack

        // A GRID, NOT A ROW — see TileMinLegibleWidth for the ruling. The grid's own
        // CalculateLayoutInputVertical reports the height of however many rows it ended up with, so
        // the strip is NOT given a LayoutElement height any more: a fixed one would have pinned the
        // strip to a single row's worth of space and clipped the second.
        var grid = strip.AddComponent<GridLayoutGroup>();
        grid.spacing = new Vector2(TileGap, TileGap);
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.padding = new RectOffset(0, 0, 2, 14);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;

        // THE STRIP'S WIDTH COMES FROM THE PANEL, NEVER FROM THE TILES (user report 2026-09-07
        // item 13, kachel_problem.jpg; the defect is measured in the ModBuild 472 log).
        //
        // A GridLayoutGroup at FixedColumnCount reports minWidth == preferredWidth == the width of
        // its own row, and ContentRoot's VerticalLayoutGroup runs childControlWidth WITH
        // childForceExpandWidth, whose cross-axis rule is Clamp(innerWidth, childMin, innerWidth).
        // A CHILD MINIMUM LARGER THAN THE CONTAINER THEREFORE WINS. Seeding the grid at
        // tiles.Length columns of TileWidth made the strip demand 5*232 + 4*10 = 1200 px, the panel
        // handed back 1200 instead of its own 618, and TileStripGrid then measured 1200, concluded
        // five columns fit, and RE-CONFIRMED THE SEED. A self-confirming fixed point: the strip
        // measured a width its own seed had caused.
        //
        // The old comment here read "TileStripGrid corrects both the moment the strip's width is
        // known" and that was the false sentence protecting this defect. It only corrected them
        // when its first LateUpdate BEAT the first layout pass — which is the case on a page built
        // by a press inside the open window (the strip is still at the RectTransform default of
        // 100 px, one column is chosen, minWidth drops to 174, and the panel's real 618 arrives
        // next pass) and NOT the case on a page built by the window's own OnShow, where a layout
        // pass has already run by the time LateUpdate arrives. Six page builds in the ModBuild 472
        // single-player log say exactly that: the four reached by a press read "in 100 px" then
        // "in 618 px" and settle at 2 rows x 3 columns; the two reached by UIWindow SHOWN read "in
        // 1200 px" once and stop at 1 row x 5 columns.
        //
        // Two independent guards, because this must not depend on winning a race:
        //   (1) a LayoutElement pinning minWidth to 0. LayoutElement.layoutPriority is 1 against
        //       LayoutGroup's 0, so LayoutUtility.GetMinSize returns 0 whatever the grid says and
        //       the container's width wins in EVERY ordering. preferredWidth/height stay -1, so the
        //       grid still owns the strip's height and the row-count contract above is untouched.
        //   (2) a seed that is already the answer whenever the parent's width can be read, and one
        //       single column when it cannot — never tiles.Length. Even with (1) in place this
        //       keeps the first drawn frame inside the panel instead of showing one clipped
        //       1200 px row until LateUpdate lands.
        var fence = strip.AddComponent<LayoutElement>();
        fence.minWidth = 0f;

        TileGridShape(ParentContentWidth(parent), grid.padding.horizontal, tiles.Length,
                      out _, out int seedColumns, out float seedCell);
        grid.constraintCount = seedColumns;
        grid.cellSize = new Vector2(seedCell, TileHeight);

        TileStripGrid balancer = strip.AddComponent<TileStripGrid>();
        balancer.Bind(grid, tiles.Length, PageBuildKind);

        var built = new List<BuiltTile>(tiles.Length);
        int withArt = 0;

        for (int i = 0; i < tiles.Length; i++)
        {
            if (BuildOneTile(stripRect, tiles[i], item, built))
                withArt++;
        }

        // Paint the initial selection through the same function the clicks use, so "what selected
        // looks like" is written down once.
        RepaintVariantTiles(built);

        // HW-VERIFY: the next hardware round has to answer whether the strips drew and whether the
        // embedded art decoded on the headset; both are one number each and neither is visible in
        // any other line. The EDGE and the PANEL WIDTH ride here rather than only on the strip
        // line below, because this one is unconditional and prints exactly once per page build: a
        // build whose strip never got a usable width would otherwise leave no record of which edge
        // produced it, which is the reading ModBuild 472 could not make.
        VRLog.Note("WorldUI",
            $"Variant tiles for {item.Section}/{item.Key}: {tiles.Length} tile(s), " +
            $"{withArt} with art, {tiles.Length - withArt} label-only; page build = " +
            $"{PageBuildKind}; panel offers {ParentContentWidth(parent):F0} px, seeded " +
            $"{seedColumns} column(s) at {seedCell:F0} px.");

        return true;
    }

    /// <summary>
    /// THE WIDTH A CHILD OF <c>ContentRoot</c> IS ACTUALLY OFFERED: the parent's rect less the
    /// layout group's own horizontal padding, which on <c>ContentRoot</c> is the gap reserved for
    /// the sub-tab column and is not the child's to use. Returns 0 when there is no parent rect or
    /// no layout has run yet — <see cref="TileGridShape"/> reads that as "not known".
    /// </summary>
    private static float ParentContentWidth(Transform? parent)
    {
        if (parent is not RectTransform rect)
            return 0f;
        float width = rect.rect.width;
        var group = rect.GetComponent<LayoutGroup>();
        if (group != null)
            width -= group.padding.horizontal;
        return width;
    }

    /// <summary>
    /// HOW MANY ROWS, HOW MANY COLUMNS AND HOW WIDE A CELL, for a strip of <paramref name="count"/>
    /// tiles offered <paramref name="stripWidth"/> px. THE ONE COPY of the wrap arithmetic — the
    /// seed in <c>TryBuildVariantTiles</c> and <see cref="TileStripGrid"/>'s live recompute must
    /// never be able to disagree, because a seed that answers differently from the recompute is
    /// exactly the defect this shape function was extracted to kill.
    ///
    /// <para>Returns FALSE when the width is not usable yet, and then hands back the safe seed —
    /// ONE column at the authored size. Never <paramref name="count"/> columns: the grid's minimum
    /// width is what the container obeys, so a fallback wider than the panel would size the panel
    /// to the fallback rather than the other way round.</para>
    ///
    /// <para>THE RESULT FITS WHENEVER ONE TILE FITS, and not unconditionally — the first draft of
    /// this sentence claimed the latter and a sweep of counts 1..24 over widths 115..4000 px
    /// falsified it. <c>columns</c> is at most <c>fit</c>, and <c>fit</c> counts tiles at the
    /// legible floor plus a gap, so <c>columns*cell + (columns-1)*gap ≤ usable - gap</c>: at
    /// <c>stripWidth ≥ TileMinLegibleWidth</c> the worst overrun over that whole sweep is exactly
    /// 0.0 px. BELOW that width <c>fit</c> is clamped up to 1 and the single column overruns by up
    /// to <c>TileMinLegibleWidth - stripWidth</c>. That is the wrap ruling working as written — a
    /// tile may never go below the floor, so a panel too narrow for one legible tile gets a tile
    /// that sticks out rather than one nobody can see. Unreachable in this pane, whose content
    /// column measures 618 px, and the instrument below names it separately so it can never be
    /// mistaken for the ModBuild 472 defect.</para>
    /// </summary>
    private static bool TileGridShape(float stripWidth, int horizontalPadding, int count,
                                      out int rows, out int columns, out float cell)
    {
        rows = 1;
        columns = 1;
        cell = TileWidth;

        if (count <= 0)
            return false;

        float usable = stripWidth - horizontalPadding + TileGap;
        if (usable <= TileGap + 1f)
            return false;

        // How many tiles fit at the floor, then how few ROWS that needs, then an EVEN spread over
        // those rows. Never more columns than tiles, never fewer than one.
        int fit = Mathf.Clamp(Mathf.FloorToInt(usable / (TileMinLegibleWidth + TileGap)), 1, count);
        rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)fit));
        columns = Mathf.Clamp(Mathf.CeilToInt(count / (float)rows), 1, count);

        // The leftover width is given back to the tiles, up to the authored size — a two-row strip
        // in a wide pane should not leave a hole on the right of each row.
        cell = Mathf.Clamp(usable / columns - TileGap, TileMinLegibleWidth, TileWidth);
        return true;
    }

    /// <summary>The width one built row of the strip occupies, which is what has to fit the panel.</summary>
    private static float TileRowWidth(int columns, float cell, int horizontalPadding) =>
        columns * cell + Mathf.Max(0, columns - 1) * TileGap + horizontalPadding;

    /// <summary>
    /// Chooses the strip's COLUMN COUNT and CELL WIDTH from the width the strip actually got.
    ///
    /// <para>WHY A COMPONENT AND NOT A NUMBER AT BUILD TIME. The strip's width is zero until the
    /// pane's own layout pass has run, so nothing at build time can divide by it — the same reason
    /// <c>VROptionsTab.ProbeCaptionFit</c> gives up and waits for the next rebuild. A
    /// <c>UIBehaviour</c> is told when that width changes, including when the player re-sizes or
    /// re-seats the window, so the strip re-flows instead of keeping a count decided once.</para>
    ///
    /// <para>WHY NOT <c>Constraint.Flexible</c>, which wraps on its own and needs none of this: it
    /// fills each row to capacity, so five tiles in a pane that fits four come out FOUR AND ONE. The
    /// rows are balanced here instead — how many rows do we need at the legible floor, then spread
    /// the tiles evenly over them — which is 3+2 for the same pane. Only then is the cell allowed to
    /// grow back toward <see cref="TileWidth"/> to use up the leftover width.</para>
    /// </summary>
    private sealed class TileStripGrid : UnityEngine.EventSystems.UIBehaviour
    {
        private GridLayoutGroup? _grid;
        private int _count;
        private int _lastColumns = -1;
        private bool _pending;

        /// <summary>Which edge built the page this strip belongs to, captured at BUILD time: the
        /// recompute runs a frame or more later, by which point the flag naming the edge is long
        /// cleared. This is the term the user's report turns on — the defect was only ever visible
        /// on a page restored by the window's own show.</summary>
        private string _buildKind = "?";

        internal void Bind(GridLayoutGroup grid, int count, string buildKind)
        {
            _grid = grid;
            _count = count;
            _buildKind = buildKind;
            _lastColumns = -1;
            _pending = true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _pending = true;
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            _pending = true;
        }

        /// <summary>
        /// The recompute is DEFERRED to the frame's end, not run inside
        /// <see cref="OnRectTransformDimensionsChange"/>. That callback fires from INSIDE Unity's
        /// layout rebuild, and marking a rect for rebuild from in there is the
        /// "Trying to add ... while we are already inside a layout rebuild loop" complaint — one
        /// warning per strip per frame in a log this project reads for other things.
        /// </summary>
        private void LateUpdate()
        {
            if (!_pending)
                return;
            _pending = false;
            Apply();
        }

        private void Apply()
        {
            if (_grid == null || _count <= 0)
                return;

            float width = ((RectTransform)transform).rect.width;
            if (!TileGridShape(width, _grid.padding.horizontal, _count,
                               out int rows, out int columns, out float cell))
            {
                // Layout has not run yet. LEAVE _pending SET: the only other thing that re-arms
                // this is OnRectTransformDimensionsChange, and a strip whose width never changes
                // again — because it was already at its final value when this component woke —
                // would otherwise stay on its seed for the life of the page.
                _pending = true;
                return;
            }

            _grid.cellSize = new Vector2(cell, TileHeight);
            if (columns == _lastColumns)
                return;

            _lastColumns = columns;
            _grid.constraintCount = columns;

            // THE PANEL IS THE THING THE ROW HAS TO FIT, so the line names it beside the row rather
            // than leaving a reader to infer it from the strip rect — the strip rect is the term
            // that LIED in ModBuild 472, reading 1200 px inside a 618 px panel because the strip's
            // own seed had inflated it.
            float row = TileRowWidth(columns, cell, _grid.padding.horizontal);
            float panel = ParentContentWidth(transform.parent);
            // THREE VERDICTS, NOT TWO. A panel too narrow for ONE tile at the legible floor is the
            // wrap ruling refusing to shrink further, which is deliberate and documented at
            // TileGridShape; calling that OVERRUNS would put the word on a working strip and cost a
            // future reader the count that matters. OVERRUNS therefore means the ModBuild 472
            // defect and nothing else.
            string fit = panel <= 0f
                ? "panel width not readable"
                : row - panel <= 1f
                    ? $"fits with {panel - row:F0} px to spare"
                    : panel < TileMinLegibleWidth
                        ? $"sticks out by {row - panel:F0} px because the panel is under the " +
                          $"{TileMinLegibleWidth:F0} px legibility floor — the wrap ruling, not a defect"
                        : $"OVERRUNS its panel by {row - panel:F0} px";

            // HW-VERIFY: the wrap ruling's only observable is "how many rows, at what tile width",
            // and no other line carries either number; the OVERRUNS clause is the whole of user
            // report 2026-09-07 item 13 in one word. Change-gated on the COLUMN count, so a settled
            // strip prints once per page build and a re-seated window prints again with the new
            // answer — Bind clears _lastColumns, so every build gets at least one line.
            VRLog.Note("WorldUI",
                $"Variant tile strip {name} [{_buildKind}]: {_count} tile(s) in {rows} row(s) x " +
                $"{columns} column(s), cell {cell:F0} px wide (floor {TileMinLegibleWidth:F0}, " +
                $"authored {TileWidth:F0}); row {row:F0} px against {panel:F0} px of panel — {fit} " +
                $"(strip rect {width:F0} px).");
            // The ROW COUNT changed, so the strip's own preferred height did too, and the pane above
            // it has to be told: a grid does not mark its ancestors dirty for its own reflow.
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }

    /// <summary>The pieces of one built tile that the repaint has to reach.</summary>
    private sealed class BuiltTile
    {
        internal Button Button = null!;
        internal Image? Picture;
        internal TMP_Text Label = null!;
        internal VariantTile Tile = null!;
    }

    /// <summary>One tile. Returns true when it got a picture (false = label-only, still usable).</summary>
    private static bool BuildOneTile(RectTransform strip, VariantTile tile,
                                     ConfigCatalog.ConfigItem item, List<BuiltTile> built)
    {
        // THE FRAME IS THE TILE: full rect, and the RAYCAST TARGET. Its visible part is the border
        // ring (the plate covers the middle), so one Graphic is both the hit area for laser and
        // poke AND the surface the Button tints. A Button whose target sits under an opaque child
        // gets no visible hover; a Button with no raycastable Graphic at all gets no clicks.
        var tileGo = new GameObject(tile.Label(), typeof(RectTransform));
        var tileRect = (RectTransform)tileGo.transform;
        tileRect.SetParent(strip, worldPositionStays: false);

        // NO LayoutElement here: GridLayoutGroup writes every cell's size itself and ignores one,
        // so a LayoutElement on a grid child is a set of numbers that look authoritative and are
        // inert. The size comes from grid.cellSize, which TileStripGrid owns.

        var frame = tileGo.AddComponent<Image>();
        frame.color = Color.white; // the ColorBlock below carries the real colour; see RepaintTile
        frame.raycastTarget = true;

        // THE TILE'S PLATE WEARS THE GAME'S PANEL ART. It was a sprite-less quad — a flat fill,
        // which is exactly the "langweilig einfarbig" the 2026-09-05 report objects to. The tint is
        // unchanged (it is the plate's judged colour); only the surface under it is now the game's
        // own, harvested and MEASURED in VROptionsTab.10.Skin.cs. A harvest that found nothing
        // leaves the plate sprite-less, i.e. exactly as it shipped.
        var plate = MakeTileChild<Image>(tileRect, "Plate", TileBorder, TileBorder);
        SkinAsPanel(plate);
        plate.color = TilePlate;
        plate.raycastTarget = false;

        // THE PICTURE IS LAID OUT AGAINST THE RECT, NOT AGAINST ITS DRAWN PIXELS. preserveAspect
        // fits the 4:3 texture inside this rect and centres it; the alternative — sizing the rect to
        // the art's visible content — is how a logo once shipped 2.35x too wide on this project.
        Image? picture = null;
        Sprite? sprite = TileSprite(tile.Resource);
        if (sprite != null)
        {
            picture = MakeTileChild<Image>(tileRect, "Picture", TileBorder, TileBorder);
            var pictureRect = (RectTransform)picture.transform;
            pictureRect.offsetMin = new Vector2(TileBorder, TileLabelHeight);
            pictureRect.offsetMax = new Vector2(-TileBorder, -TileBorder);
            picture.sprite = sprite;
            picture.preserveAspect = true;
            picture.raycastTarget = false;
        }

        var labelGo = new GameObject("Label", typeof(RectTransform));
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.SetParent(tileRect, worldPositionStays: false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.offsetMin = new Vector2(TileBorder, TileBorder);
        labelRect.offsetMax = new Vector2(-TileBorder, TileBorder + TileLabelHeight);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        NativeButtonSkin.ApplyFont(label);
        label.text = tile.Label();
        label.fontSize = TileLabelSize;
        label.alignment = TextAlignmentOptions.Center;
        // The name must never be replaced by an ellipsis (standing ruling for this pane): shrink
        // the glyphs instead and keep the word readable. ONE implementation of that ruling now —
        // VROptionsTab.FitCaption — with this pane's own floor of 10 (a tile is narrower than a
        // settings row and a 9-point word centred under a picture reads as a caption, not a name).
        // The probe comes with it: this path used to fail SILENTLY when a name did not fit, so an
        // over-long German environment name was invisible in the log.
        VROptionsTab.FitCaption(label, TileLabelSize, TileLabelFloor, wrap: false);
        VROptionsTab.ProbeCaptionFit(label, "variant-tile:" + tile.Resource);
        label.raycastTarget = false;

        var button = tileGo.AddComponent<Button>();
        button.targetGraphic = frame;
        button.transition = Selectable.Transition.ColorTint;
        VariantTile captured = tile;
        button.onClick.AddListener(() => OnVariantTileClicked(item, captured, built));

        built.Add(new BuiltTile { Button = button, Picture = picture, Label = label, Tile = tile });
        return picture != null;
    }

    private static T MakeTileChild<T>(RectTransform parent, string name, float inset, float insetY)
        where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, worldPositionStays: false);
        // ANCHORS OWN A STRETCH CHILD'S SIZE: these stay at (0,0)-(1,1) and the inset is expressed
        // as offsets. Collapsing them to a point and setting sizeDelta would size the child to ZERO.
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, insetY);
        rect.offsetMax = new Vector2(-inset, -insetY);
        return go.AddComponent<T>();
    }

    /// <summary>
    /// A tile was clicked. Wrapped whole: <c>UnityEvent.Invoke</c> has no per-listener catch, so an
    /// exception raised here would amputate every listener queued after it — on a canvas the player
    /// still has to click their way back out of.
    /// </summary>
    private static void OnVariantTileClicked(ConfigCatalog.ConfigItem item, VariantTile tile,
                                             List<BuiltTile> built)
    {
        try
        {
            // Apply() repaints the pane's value labels and rebuilds the page by itself when the
            // edited entry is a variant selector (hand style) or a dependency parent.
            bool rebuild = false;
            Apply(item, () => rebuild = tile.Choose());

            // Every object below may already have been destroyed by that rebuild; each access is
            // null-guarded (Unity's == null answers true for a destroyed object).
            RepaintVariantTiles(built);

            if (rebuild)
                TickGuard.Run("VROptionsTab.VariantTiles", Rebuild, "WorldUI");
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"variant tile click on {item.Section}/{item.Key} threw " +
                                  $"({e.GetType().Name}: {e.Message}).");
        }
    }

    /// <summary>Repaint every tile in a strip from its own <c>Selected</c> predicate.</summary>
    private static void RepaintVariantTiles(List<BuiltTile> built)
    {
        for (int i = 0; i < built.Count; i++)
        {
            BuiltTile made = built[i];
            bool on;
            try
            {
                on = made.Tile.Selected();
            }
            catch
            {
                on = false; // an unreadable entry must not stop the rest of the strip repainting
            }

            if (made.Button != null)
            {
                ColorBlock colors = made.Button.colors;
                colors.normalColor = on ? TileBorderOn : TileBorderOff;
                colors.highlightedColor = on ? TileBorderOnHot : TileBorderOffHot;
                colors.pressedColor = TileBorderPressed;
                colors.selectedColor = colors.normalColor;
                // NOT TileBorderOff, which is what an available UN-PICKED tile is painted: the two
                // were byte-identical, so a tile that could not be chosen would have looked exactly
                // like one that simply was not chosen yet (2026-09 redundancy audit, B6). Nothing
                // sets interactable = false on these buttons today, so this has never been on
                // screen — which is precisely why it would have shipped wrong the day something
                // did. Half the un-picked border's luminance, alpha kept: a disabled tile reads as
                // sunk into the pane rather than as a choice waiting to be made.
                colors.disabledColor = TileBorderDisabled;
                colors.colorMultiplier = 1f;
                colors.fadeDuration = VROptionsTab.HoverTintFadeSeconds;
                made.Button.colors = colors;
            }

            if (made.Picture != null)
                made.Picture.color = on ? TilePictureOn : TilePictureOff;

            if (made.Label != null)
            {
                made.Label.color = on ? TileBorderOn : TileLabelOff;
                made.Label.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            }
        }
    }

    /// <summary>The sprite for one embedded PNG, or null when it is absent or undecodable.</summary>
    private static Sprite? TileSprite(string resource)
    {
        if (string.IsNullOrEmpty(resource))
            return null;
        if (TileSprites.TryGetValue(resource, out Sprite? cached))
            return cached;

        Sprite? sprite = null;
        // Colour art, so linear:false. Clamp, not Repeat: at a tile's edge bilinear filtering would
        // otherwise fetch the opposite side of the picture.
        Texture2D? texture = EmbeddedTexture.Get(resource, linear: false, TextureWrapMode.Clamp);
        if (texture != null)
        {
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                                   new Vector2(0.5f, 0.5f), 100f);
            sprite.name = resource;
        }

        TileSprites[resource] = sprite;
        return sprite;
    }

    /// <summary>Drop the built sprites (module shutdown / hot reload).
    ///
    /// <para>NO CALLER AT HEAD, AND THIS IS NOT DEAD CODE — IT IS INERT (2026-09 refactor, F-85).
    /// The doc here used to say the drop "matches <c>WorldUIAssets.Reset</c>'s contract";
    /// <c>WorldUIAssets.Reset</c> does not call this and nothing else does, so the sentence asserted
    /// a participation that does not exist. There is no defect behind it: the sprites are built over
    /// <c>Core/EmbeddedTexture.cs</c> textures whose own cache is never cleared on a module reset, so
    /// they stay valid across a hot reload and a stale entry is a small leak rather than a blank
    /// tile. Kept rather than deleted, because deleting it removes the only mechanism; if it is ever
    /// wired, the call belongs in <c>WorldUIAssets.Reset</c> or <c>WorldUIModule.Shutdown</c>, both
    /// of which are lane worldui-front — a NEEDED-OUTSIDE entry, not an in-lane edit.</para></summary>
internal static void ResetVariantTiles() => TileSprites.Clear();
}
