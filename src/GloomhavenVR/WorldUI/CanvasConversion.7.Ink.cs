using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 7 (per-graphic INK footprint for the depth stamp). NEW members only —
// appended after parts 1-6 in the filename sort, so the existing member/static-initializer order
// (which the refactor guard tracks and part 1's header explains) is untouched.

internal static partial class CanvasConversion
{
    // ---- the OPAQUE footprint of a Graphic, not its layout rect ------------------------------
    //
    // USER RULING (hardware round, two reports at once): "Perspektive soll im gesamten Mod
    // respektiert werden (mit Ausnahme der Skybox). D.h. auch die Healthbars sollen Perspektive
    // respektieren und nicht hinter etwas verschwinden obwohl es im Vordergrund ist. Verbesser
    // nur die Transparenz, dass nicht dieser Rahmen drumrum auftaucht."
    // Depth is to be honoured EVERYWHERE. Nothing may be taken OUT of the depth compose; what
    // must stop occluding is the INVISIBLE, TRANSPARENT MARGIN around a panel's visible pixels.
    //
    // ROOT CAUSE of both reports. CollectVisibleMaskRects (CanvasConversion.3.Fit.cs) emits ONE
    // depth quad per visible Graphic sized to that Graphic's RECTTRANSFORM RECT, inflated by
    // HostMaskQuadPaddingPx. A rect is not ink:
    //   * report #5 (health bars): an actor bar's Images and TMP labels sit in rects far taller
    //     and wider than the drawn segments, so a bar in front of the "Infotafel" stamped a wide
    //     BAND at its own plane and the panel's content failed ZTest across the whole band —
    //     the "Transparenz drumrum" that cut a rectangle out of the panel (healthbar_problem.jpg).
    //   * report #6 (initiative track): the two portraits floating in front of the pause menu are
    //     surrounded by a GREY BLOCK that erases the menu row "SPIEL VERLASSEN" behind them
    //     (.planning/debug/initiativereihenfolge_transparenz.png). The block is far bigger than
    //     the portraits: the avatar entry's name/initiative TMP labels (rects sized for a long
    //     name, drawing a short string or nothing at all) and the frame Images (sprites with a
    //     transparent border inside their rect) all stamped their FULL rects.
    // Everything drawn after queue 2999 is discarded inside those rects, which is exactly the
    // grey block and exactly the hole in the info panel.
    //
    // WHY MEASURED SUB-RECTS AND NOT A PER-PIXEL DEPTH PREPASS. The per-pixel-exact alternative
    // is to render the actual UI meshes into depth with an alpha-clipped depth-only material.
    // Unity does not hand out a CanvasRenderer's generated mesh (no public getter; the uGUI mesh
    // lives native-side), so such a prepass would have to REBUILD every graphic's mesh — a second
    // full uGUI vertex pipeline per frame per host, with its own text/sprite/atlas handling — and
    // it would still need a second camera pass per eye, i.e. two more passes under MultiPass
    // stereo. That is speculative and expensive for an artefact that is entirely explained by a
    // handful of measurable sub-rects, so this file measures instead of guessing:
    //
    //   Image + sprite (Simple/Filled) -> the sub-rect uGUI ITSELF draws the sprite in
    //                                     (importer trim padding, preserveAspect letterbox) and,
    //                                     on top of that, the tight-mesh vertex bound.
    //   TMP_Text                       -> textBounds (the generated glyph run), and NOTHING at
    //                                     all when the string is empty / has no visible glyph.
    //   UnityEngine.UI.Text            -> the cached TextGenerator's vertex bound, same rule.
    //   anything else (RawImage, sprite-less Image, custom Graphic)
    //                                  -> UNCHANGED, the full rect. A too-SMALL stamp loses
    //                                     occlusion it should win, which is the failure mode the
    //                                     user reported in the other direction ("Health bars
    //                                     werden jetzt komplett verdeckt") — so anything that
    //                                     cannot be measured keeps the conservative rect.
    //
    // Every measure below only ever SHRINKS a quad inside its own rect, and every one is clamped
    // back into that rect, so no stamp can ever move or grow. Depth participation itself is
    // untouched: every converted host, health bars included, keeps stamping its plane.

    /// <summary>What <see cref="MeasureInkRect"/> could say about a graphic's drawn footprint.</summary>
    private enum InkMeasure
    {
        /// <summary>No usable measure — the caller keeps the full layout rect (conservative).</summary>
        Unmeasured,

        /// <summary>A tighter, measured sub-rect of the layout rect is available.</summary>
        Tightened,

        /// <summary>The graphic draws NO ink at all (empty label) — emit no quad whatsoever.</summary>
        Empty,
    }

    /// <summary>An intersected ink rect below this (host px on the graphic's own rect scale) is
    /// treated as a measurement failure, not as content: falling back to the rect is always safe,
    /// stamping a sliver is not.</summary>
    private const float InkMinExtentPx = 1f;

    /// <summary>A measured shrink smaller than this on BOTH axes is reported as
    /// <see cref="InkMeasure.Unmeasured"/> — it changes nothing visually and keeping it out of the
    /// diagnostic counters makes the "how much did the stamp shrink" line readable.</summary>
    private const float InkShrinkMinPx = 0.5f;

    /// <summary>How much of a measured ink rect must land inside the graphic's own layout rect
    /// before it is believed (see <see cref="CommitInk"/>).</summary>
    private const float InkOverlapMinFraction = 0.5f;

    /// <summary>
    /// Ink sub-rect of <paramref name="g"/> inside its own <paramref name="layout"/> rect
    /// (<c>RectTransform.rect</c> — pivot at the origin, the exact space the caller's corner
    /// transform expects). <paramref name="rule"/> names the measure that fired, for the hardware
    /// diagnostic. See the file header for why each type is measured the way it is.
    /// </summary>
    private static InkMeasure MeasureInkRect(Graphic g, Rect layout, out Rect ink, out string rule)
    {
        ink = layout;
        rule = string.Empty;

        // TMP first: TMP_Text IS a Graphic and (unlike UnityEngine.UI.Text) is what this game's
        // panels are built from, so it must be tested before any other branch could claim it.
        if (g is TMP_Text tmp)
            return MeasureTmpInk(tmp, layout, out ink, out rule);
        if (g is Text txt)
            return MeasureTextInk(txt, layout, out ink, out rule);
        if (g is Image img)
            return MeasureImageInk(img, layout, out ink, out rule);
        return InkMeasure.Unmeasured;
    }

    /// <summary>
    /// TMP: <see cref="TMP_Text.textBounds"/> is the bound of the GENERATED glyph run in the text
    /// object's own local space — the same space as <c>RectTransform.rect</c> (TMP lays characters
    /// out around the rect's pivot origin), so it drops straight into the layout rect. Two places
    /// in this repo already depend on exactly that and were proven on hardware:
    /// <c>MrBacking</c> sizes its plate from <c>Vector2.Min(rect size, textBounds.size)</c> and
    /// places it at <c>textBounds.center</c> precisely because that centre is correct for
    /// LEFT-aligned labels in a wide rect, and <c>DecisionDockSurface.GlyphEdgeIn</c> takes its
    /// row gaps from the same bounds because "dialog labels are routinely authored in rects far
    /// taller than their glyphs". The bound is line-box tall (ascender..descender), i.e.
    /// deliberately not glyph-tight, which is the conservative side. Two special cases matter more
    /// than the shrink itself:
    /// an EMPTY string and a run with no visible glyph draw nothing, so they must emit NO quad —
    /// that is the initiative avatar's name label, whose rect is sized for a long name and which
    /// stamped a block over the menu behind it while drawing not a single pixel. A non-empty
    /// string whose text info has not been generated yet is UNMEASURED (keep the rect): a mask
    /// that vanishes for a frame while TMP catches up would flicker the panels behind it.
    /// </summary>
    private static InkMeasure MeasureTmpInk(TMP_Text tmp, Rect layout, out Rect ink, out string rule)
    {
        ink = layout;
        rule = string.Empty;
        if (string.IsNullOrEmpty(tmp.text))
        {
            rule = "TMP: empty string, draws nothing";
            return InkMeasure.Empty;
        }
        TMP_TextInfo info = tmp.textInfo;
        if (info == null || info.characterCount <= 0)
            return InkMeasure.Unmeasured; // not generated yet — keep the rect
        Bounds b = tmp.textBounds;
        Vector3 size = b.size;
        if (size.x <= 0.01f || size.y <= 0.01f)
        {
            rule = "TMP: no visible glyph in the generated run";
            return InkMeasure.Empty;
        }
        return CommitInk(layout, Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y),
            "TMP textBounds", out ink, out rule);
    }

    /// <summary>
    /// Legacy <see cref="Text"/>: the cached <see cref="TextGenerator"/> holds exactly the vertices
    /// uGUI turns into the mesh, in generator pixels around the rect pivot — <c>Text.OnPopulateMesh</c>
    /// multiplies them by <c>1 / pixelsPerUnit</c> and nothing else, so the same scaling maps their
    /// bound into the layout rect. Same empty-run rule as TMP. This class is rare in this game's UI
    /// but not absent (WorldspaceModifierDisplayUnit's value labels are world-space
    /// <see cref="Text"/>), and a rect-sized stamp from a two-character label is exactly the
    /// reported artefact.
    /// </summary>
    private static InkMeasure MeasureTextInk(Text txt, Rect layout, out Rect ink, out string rule)
    {
        ink = layout;
        rule = string.Empty;
        if (string.IsNullOrEmpty(txt.text))
        {
            rule = "Text: empty string, draws nothing";
            return InkMeasure.Empty;
        }
        TextGenerator gen = txt.cachedTextGenerator;
        if (gen == null || gen.vertexCount < 4)
            return InkMeasure.Unmeasured; // never generated (or generated empty) — keep the rect
        if (gen.characterCountVisible <= 0)
        {
            rule = "Text: no visible glyph in the generated run";
            return InkMeasure.Empty;
        }
        float ppu = txt.pixelsPerUnit;
        if (ppu <= 0.0001f)
            return InkMeasure.Unmeasured;
        float unitsPerPixel = 1f / ppu;

        IList<UIVertex> verts = gen.verts; // the generator's own list — no copy, no per-frame alloc
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 p = verts[i].position;
            if (p.x < x0) x0 = p.x;
            if (p.y < y0) y0 = p.y;
            if (p.x > x1) x1 = p.x;
            if (p.y > y1) y1 = p.y;
        }
        if (x1 <= x0 || y1 <= y0)
            return InkMeasure.Unmeasured;
        return CommitInk(layout,
            Rect.MinMaxRect(x0 * unitsPerPixel, y0 * unitsPerPixel,
                x1 * unitsPerPixel, y1 * unitsPerPixel),
            "Text generator verts", out ink, out rule);
    }

    /// <summary>
    /// <see cref="Image"/> with a sprite, drawn Simple or Filled: uGUI's own
    /// <c>Image.GetDrawingDimensions</c> already places the sprite in a SUB-RECT of the layout rect
    /// — first the preserveAspect letterbox, then the importer's trim padding
    /// (<c>UnityEngine.Sprites.DataUtility.GetPadding</c>, the transparent border the importer cut
    /// off the source texture). Reproducing that sub-rect is not an approximation, it is where the
    /// quad uGUI submits actually is. On top of it comes the sprite's own TIGHT MESH bound
    /// (<see cref="TrySpriteInkFractions"/>): a Tight-meshType sprite carries a mesh generated
    /// around its opaque pixels, so its vertex bound names the ink even when the source rect was
    /// never trimmed. Both only shrink, and their intersection is taken.
    ///
    /// <para>Tiled is deliberately NOT measured (it repeats the sprite across the whole rect), and
    /// neither is Sliced WITH a border — its 9-slice geometry stretches the border across the whole
    /// rect, so the rect IS the drawn area. Sliced WITHOUT a border is measured: uGUI's
    /// <c>GenerateSlicedSprite</c> falls straight through to <c>GenerateSimpleSprite(toFill, false)</c>
    /// when <c>sprite.border</c> is zero, which is a very common authoring state for frame/icon
    /// Images. A sprite-less Image is a solid colour quad over its whole rect — also unmeasured,
    /// also correct.</para>
    /// </summary>
    private static InkMeasure MeasureImageInk(Image img, Rect layout, out Rect ink, out string rule)
    {
        ink = layout;
        rule = string.Empty;
        Sprite? sprite = img.overrideSprite != null ? img.overrideSprite : img.sprite;
        if (sprite == null)
            return InkMeasure.Unmeasured;

        // Which uGUI geometry path this Image takes, and whether that path honours preserveAspect
        // (GenerateSlicedSprite's border-less fall-through passes shouldPreserveAspect: false).
        bool borderless = sprite.border.sqrMagnitude <= 0f;
        bool simpleGeometry = img.type == Image.Type.Simple || img.type == Image.Type.Filled
                              || (img.type == Image.Type.Sliced && borderless);
        if (!simpleGeometry)
            return InkMeasure.Unmeasured;
        bool aspectHonoured = img.preserveAspect && img.type != Image.Type.Sliced;

        Vector2 spriteSize = new(sprite.rect.width, sprite.rect.height);
        if (spriteSize.x < 1f || spriteSize.y < 1f)
            return InkMeasure.Unmeasured;

        Rect drawn = layout;
        bool aspect = false;
        if (aspectHonoured)
        {
            aspect = PreserveSpriteAspect(ref drawn, spriteSize, ((RectTransform)img.transform).pivot);
        }
        bool trimmed = TrySpriteInkFractions(sprite, out Vector4 f);
        if (!aspect && !trimmed)
            return InkMeasure.Unmeasured;
        if (trimmed)
        {
            drawn = Rect.MinMaxRect(
                drawn.xMin + drawn.width * f.x, drawn.yMin + drawn.height * f.y,
                drawn.xMin + drawn.width * f.z, drawn.yMin + drawn.height * f.w);
        }
        string why = trimmed
            ? (aspect ? "Image sprite trim/tight-mesh + preserveAspect" : "Image sprite trim/tight-mesh")
            : "Image preserveAspect letterbox";
        return CommitInk(layout, drawn, why, out ink, out rule);
    }

    /// <summary>
    /// uGUI's <c>Image.PreserveSpriteAspectRatio</c>, reproduced: the letterboxed sub-rect a
    /// preserveAspect Image actually draws in, anchored by the rect's pivot exactly like uGUI
    /// anchors it. Returns false when the rect already matches the sprite aspect (nothing to gain).
    /// </summary>
    private static bool PreserveSpriteAspect(ref Rect rect, Vector2 spriteSize, Vector2 pivot)
    {
        if (rect.width <= 0.0001f || rect.height <= 0.0001f || spriteSize.y <= 0.0001f)
            return false;
        float spriteRatio = spriteSize.x / spriteSize.y;
        float rectRatio = rect.width / rect.height;
        if (Mathf.Abs(spriteRatio - rectRatio) < 0.0001f)
            return false;
        if (spriteRatio > rectRatio)
        {
            float oldHeight = rect.height;
            rect.height = rect.width * (1f / spriteRatio);
            rect.y += (oldHeight - rect.height) * pivot.y;
        }
        else
        {
            float oldWidth = rect.width;
            rect.width = rect.height * spriteRatio;
            rect.x += (oldWidth - rect.width) * pivot.x;
        }
        return true;
    }

    /// <summary>
    /// Normalized ink bound of a sprite inside its own (untrimmed) <see cref="Sprite.rect"/>, as
    /// <c>(u0, v0, u1, v1)</c> in 0..1. Two independent shrinks, intersected:
    ///
    /// <para>(a) IMPORTER TRIM — <c>DataUtility.GetPadding</c> reports how many pixels of
    /// transparent border the importer cut off each side when it packed the sprite; uGUI adds
    /// exactly this padding back as empty space when it draws, so the sprite's pixels only ever
    /// exist inside the padded sub-rect. Skipped for a TIGHT-PACKED atlas sprite, whose
    /// <c>textureRect</c> (which the padding derives from) is documented to raise.</para>
    ///
    /// <para>(b) TIGHT MESH — a Tight-meshType sprite's <see cref="Sprite.vertices"/> are generated
    /// around the OPAQUE pixels, in units around the pivot; mapping them back through
    /// <c>pixelsPerUnit</c> and <see cref="Sprite.pivot"/> gives the opaque bound inside the rect
    /// even when nothing was trimmed. A FullRect sprite returns its four rect corners here, i.e.
    /// no shrink — which is the honest answer for it.</para>
    ///
    /// <para>Result cached per sprite: <see cref="Sprite.vertices"/> allocates a fresh array on
    /// every read and this runs per graphic per frame. Sprites are shared assets and the cache is
    /// keyed by instance id (Unity does not recycle ids within a session); it is dropped wholesale
    /// once it grows past <see cref="SpriteInkCacheCap"/>, which bounds it without needing to
    /// observe asset unloads.</para>
    ///
    /// Returns false when nothing usable was measured or the shrink is negligible.
    /// </summary>
    private static bool TrySpriteInkFractions(Sprite sprite, out Vector4 frac)
    {
        int id = sprite.GetInstanceID();
        if (SpriteInkCache.TryGetValue(id, out frac))
            return frac.z > frac.x;

        frac = SpriteInkNone;
        Rect sr = sprite.rect;
        if (sr.width >= 1f && sr.height >= 1f)
        {
            float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;

            // (a) importer trim padding (left, bottom, right, top in source pixels).
            if (!(sprite.packed && sprite.packingMode == SpritePackingMode.Tight))
            {
                Vector4 pad = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
                if (pad.x >= 0f && pad.y >= 0f && pad.z >= 0f && pad.w >= 0f)
                {
                    u0 = Mathf.Max(u0, pad.x / sr.width);
                    v0 = Mathf.Max(v0, pad.y / sr.height);
                    u1 = Mathf.Min(u1, (sr.width - pad.z) / sr.width);
                    v1 = Mathf.Min(v1, (sr.height - pad.w) / sr.height);
                }
            }

            // (b) tight mesh vertex bound.
            float ppu = sprite.pixelsPerUnit;
            Vector2[] verts = sprite.vertices;
            if (ppu > 0.0001f && verts != null && verts.Length >= 3)
            {
                Vector2 pivot = sprite.pivot;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector2 p = verts[i];
                    if (p.x < x0) x0 = p.x;
                    if (p.y < y0) y0 = p.y;
                    if (p.x > x1) x1 = p.x;
                    if (p.y > y1) y1 = p.y;
                }
                u0 = Mathf.Max(u0, (x0 * ppu + pivot.x) / sr.width);
                v0 = Mathf.Max(v0, (y0 * ppu + pivot.y) / sr.height);
                u1 = Mathf.Min(u1, (x1 * ppu + pivot.x) / sr.width);
                v1 = Mathf.Min(v1, (y1 * ppu + pivot.y) / sr.height);
            }

            u0 = Mathf.Clamp01(u0);
            v0 = Mathf.Clamp01(v0);
            u1 = Mathf.Clamp01(u1);
            v1 = Mathf.Clamp01(v1);
            bool usable = u1 - u0 > 0.02f && v1 - v0 > 0.02f;
            bool shrinks = u0 > 0.001f || v0 > 0.001f || u1 < 0.999f || v1 < 0.999f;
            if (usable && shrinks)
                frac = new Vector4(u0, v0, u1, v1);
        }

        if (SpriteInkCache.Count >= SpriteInkCacheCap)
            SpriteInkCache.Clear();
        SpriteInkCache[id] = frac;
        return frac.z > frac.x;
    }

    /// <summary>Sentinel for "this sprite gives no usable shrink" (max &lt;= min, never applied).</summary>
    private static readonly Vector4 SpriteInkNone = new(0f, 0f, -1f, -1f);

    /// <summary>Entries kept in <see cref="SpriteInkCache"/> before it is dropped wholesale.</summary>
    private const int SpriteInkCacheCap = 1024;

    /// <summary>Per-sprite normalized ink bound — see <see cref="TrySpriteInkFractions"/>.</summary>
    private static readonly Dictionary<int, Vector4> SpriteInkCache = new(256);

    /// <summary>
    /// Clamp a measured ink rect back INTO the layout rect and decide whether it is worth using.
    /// A candidate that lands (partly) outside its own rect means the measure and the rect are not
    /// in the space this code believes they are — the answer to that is the full rect, never a
    /// guess: a stamp that is too small loses occlusion the panel should win.
    /// </summary>
    private static InkMeasure CommitInk(Rect layout, Rect candidate, string why,
        out Rect ink, out string rule)
    {
        ink = layout;
        rule = string.Empty;
        float x0 = Mathf.Max(layout.xMin, candidate.xMin);
        float y0 = Mathf.Max(layout.yMin, candidate.yMin);
        float x1 = Mathf.Min(layout.xMax, candidate.xMax);
        float y1 = Mathf.Min(layout.yMax, candidate.yMax);
        if (x1 - x0 < InkMinExtentPx || y1 - y0 < InkMinExtentPx)
            return InkMeasure.Unmeasured;
        // Sanity net against a space mismatch: a correct ink measure lies essentially INSIDE the
        // graphic's own rect (text may overshoot it by an ascender, never by half its width). If
        // most of the candidate falls outside, this code and the measure do not agree about the
        // origin — and the only safe answer to that is the full rect, never a shifted guess.
        float candidateArea = candidate.width * candidate.height;
        if (candidateArea > 0.0001f && (x1 - x0) * (y1 - y0) < InkOverlapMinFraction * candidateArea)
            return InkMeasure.Unmeasured;
        if (layout.width - (x1 - x0) < InkShrinkMinPx && layout.height - (y1 - y0) < InkShrinkMinPx)
            return InkMeasure.Unmeasured; // measured, but nothing to gain
        ink = Rect.MinMaxRect(x0, y0, x1, y1);
        rule = why;
        return InkMeasure.Tightened;
    }

    // ---- diagnostic: how much did the tightened footprint shrink the stamp? -------------------
    //
    // The next hardware test is read against these numbers, so they are collected on the SAME pass
    // that emits the quads (CollectVisibleMaskRects) and FORMATTED ONLY WHEN A LINE IS ACTUALLY
    // LOGGED. That split is not tidiness: the collection runs every frame for every masked host,
    // so building a name string per graphic here would be per-frame garbage in the VR main loop.
    // Everything below stores numbers and object references; the only strings involved are the
    // compile-time rule literals.
    //
    // Areas are host px^2 of the EMITTED quads; the raw figure is reconstructed from each quad's
    // own ink fraction (the graphic->host map is affine, so an area RATIO survives it exactly) and
    // is therefore approximate only where a scroll clipper also cut the quad.

    /// <summary>Summed host area (px^2) of the quads the last collection pass emitted.</summary>
    internal static float LastMaskInkArea;

    /// <summary>Summed host area (px^2) those same quads would have had at full rect size.</summary>
    internal static float LastMaskRawArea;

    /// <summary>How many graphics the last pass measured to a tighter footprint.</summary>
    internal static int LastMaskTightenedCount;

    /// <summary>How many graphics the last pass dropped entirely for drawing no ink.</summary>
    internal static int LastMaskNoInkCount;

    /// <summary>Ink-less graphics NAMED by the diagnostic (the rest are only counted).</summary>
    private const int MaskNoInkLogCap = 4;

    private static readonly Graphic?[] MaskNoInkGraphics = new Graphic?[MaskNoInkLogCap];
    private static readonly string[] MaskNoInkRules = new string[MaskNoInkLogCap];

    /// <summary>Biggest single area reduction of the last pass — the graphic, the fraction of its
    /// rect that survived, and the host px^2 removed (the ranking key).</summary>
    private static Graphic? s_maskTopShrinkGraphic;
    private static string s_maskTopShrinkRule = string.Empty;
    private static float s_maskTopShrinkFraction;
    private static float s_maskTopShrinkArea;

    /// <summary>
    /// One line for the hardware log: what the ink measure removed from this host's depth stamp.
    /// Built on demand by the host-mask rebuild diagnostic and appended to the modal mask's own
    /// rebuild line, so both mask owners report the same numbers in the same words.
    /// </summary>
    internal static string DescribeLastMaskInk()
    {
        float raw = LastMaskRawArea;
        float ink = LastMaskInkArea;
        float cut = raw > 1f ? (1f - ink / raw) * 100f : 0f;

        string top = string.Empty;
        if (s_maskTopShrinkGraphic != null)
        {
            Graphic g = s_maskTopShrinkGraphic;
            string parent = g.transform.parent != null ? g.transform.parent.name : "<root>";
            top = $" Biggest: '{parent}/{g.name}' ({g.GetType().Name}) kept " +
                  $"{s_maskTopShrinkFraction * 100f:F0} % of its rect ({s_maskTopShrinkArea:F0} px^2 " +
                  $"removed) [{s_maskTopShrinkRule}].";
        }

        var sb = new System.Text.StringBuilder(64);
        for (int i = 0; i < MaskNoInkLogCap; i++)
        {
            Graphic? g = MaskNoInkGraphics[i];
            if (g == null)
                continue;
            string parent = g.transform.parent != null ? g.transform.parent.name : "<root>";
            sb.Append(sb.Length > 0 ? "; " : " Ink-less: ")
              .Append('\'').Append(parent).Append('/').Append(g.name).Append("' [")
              .Append(MaskNoInkRules[i]).Append(']');
        }
        if (sb.Length > 0)
            sb.Append('.');

        return $"INK: stamp {ink:F0} of {raw:F0} host px^2 ({cut:F0} % less than the layout rects); " +
               $"{LastMaskTightenedCount} graphic(s) tightened, {LastMaskNoInkCount} dropped for " +
               $"drawing no ink.{top}{sb}";
    }

    /// <summary>Reset the ink counters at the start of a collection pass.</summary>
    private static void ResetMaskInkStats()
    {
        LastMaskInkArea = 0f;
        LastMaskRawArea = 0f;
        LastMaskTightenedCount = 0;
        LastMaskNoInkCount = 0;
        s_maskTopShrinkGraphic = null;
        s_maskTopShrinkRule = string.Empty;
        s_maskTopShrinkFraction = 0f;
        s_maskTopShrinkArea = 0f;
        for (int i = 0; i < MaskNoInkLogCap; i++)
        {
            MaskNoInkGraphics[i] = null;
            MaskNoInkRules[i] = string.Empty;
        }
    }

    /// <summary>Fold one emitted quad into the ink counters (see <see cref="DescribeLastMaskInk"/>).</summary>
    private static void RecordMaskInk(Graphic g, Vector2 gMin, Vector2 gMax, float inkFraction,
        string inkRule)
    {
        float area = Mathf.Max(0f, gMax.x - gMin.x) * Mathf.Max(0f, gMax.y - gMin.y);
        LastMaskInkArea += area;
        if (inkFraction >= 0.999f || inkFraction <= 0.0001f)
        {
            LastMaskRawArea += area;
            return;
        }
        float raw = area / inkFraction;
        LastMaskRawArea += raw;
        LastMaskTightenedCount++;
        float removed = raw - area;
        if (removed <= s_maskTopShrinkArea)
            return;
        s_maskTopShrinkArea = removed;
        s_maskTopShrinkFraction = inkFraction;
        s_maskTopShrinkRule = inkRule;
        s_maskTopShrinkGraphic = g;
    }

    /// <summary>Fold one dropped (ink-less) graphic into the counters, naming the first few.</summary>
    private static void RecordMaskNoInk(Graphic g, string inkRule)
    {
        int slot = LastMaskNoInkCount;
        LastMaskNoInkCount++;
        if (slot >= MaskNoInkLogCap)
            return;
        MaskNoInkGraphics[slot] = g;
        MaskNoInkRules[slot] = inkRule;
    }
}
