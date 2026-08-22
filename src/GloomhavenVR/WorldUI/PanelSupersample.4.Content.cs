// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). The whole design argument, and every reason this class exists at all,
// lives in the class doc at the top of PanelSupersample.1.Core.cs; it is deliberately not
// restated here. Part 4 answers exactly one question: WHAT RECTANGLE MUST THE CAPTURE FRAME so
// that switching the dial on can never take visible content away from the player.

using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    // ---- the capture frame ---------------------------------------------------------------------

    /// <summary>Corner scratch for the content walk. Deliberately NOT the report's
    /// <c>Corners</c> array: both run inside the same LateUpdate and sharing one buffer between two
    /// unrelated measurements is the kind of coupling that produces a wrong number once and then
    /// never again reproducibly.</summary>
    private static readonly Vector3[] ContentCorners = new Vector3[4];

    /// <summary>Work stack for <see cref="MeasureFrame"/>: a transform and the clip rectangle that
    /// is in force for it, in HOST-LOCAL uGUI pixels. Carrying the clip DOWN the walk is what makes
    /// the measurement O(subtree) instead of O(subtree x depth).</summary>
    private static readonly List<ClipFrame> ContentStack = new(256);

    private readonly struct ClipFrame
    {
        internal readonly Transform Transform;
        internal readonly Rect Clip;

        internal ClipFrame(Transform transform, Rect clip)
        {
            Transform = transform;
            Clip = clip;
        }
    }

    /// <summary>An unbounded rectangle to start the clip chain from (host-local uGUI px). Large
    /// enough that no authored uGUI rect can reach it, small enough that intersections stay exact in
    /// single precision.</summary>
    private static readonly Rect Unbounded = Rect.MinMaxRect(-1e6f, -1e6f, 1e6f, 1e6f);

    /// <summary>
    /// MEASURE THE CAPTURE FRAME — the union of the host rect and everything the window actually
    /// draws, in HOST-LOCAL uGUI pixels, clamped by <see cref="MaxContentExpansion"/>.
    ///
    /// <para><b>WHY THIS EXISTS.</b> ModBuild 192 framed the host rect exactly, on the argument that
    /// the dial's OFF and ON geometry should then be identical to the pixel. The hardware log
    /// falsified the premise that argument rested on: <i>"'New Party display' draws content larger
    /// than its host frame (1920x1080 vs 328x1080 uGUI px)"</i>. A world-space uGUI canvas does not
    /// clip at its own root rect, so in the OFF path that overspill IS drawn and IS visible; an
    /// orthographic camera framing the root rect cuts it off. Losing visible content is a worse
    /// defect than the shimmer this whole path removes, so the frame follows the content.</para>
    ///
    /// <para><b>WHAT IT COSTS.</b> The "OFF and ON are identical" promise becomes: identical
    /// whenever the content fits inside the host rect — which is the normal case, because the
    /// content fit centres content inside the host by construction
    /// (<c>CanvasConversion.FitContentPadding</c>) — and otherwise ON shows MORE than the host rect,
    /// never less. The quad grows with the frame and the image scale is unchanged (one authored
    /// pixel still maps to <c>Factor</c> render-target texels), so the growth is invisible except as
    /// transparent margin plus render-target cost. The state line reports the overspill in uGUI
    /// pixels every report, so a window that starts doing this is never a mystery.</para>
    ///
    /// <para><b>BIASED TOWARDS OVER-MEASURING, ON PURPOSE.</b> Over-measuring costs VRAM and adds
    /// transparent margin; under-measuring CROPS. So the drawing test is deliberately permissive
    /// (an enabled, un-culled Graphic with a non-zero own alpha counts, regardless of any
    /// CanvasGroup fade above it) and the only hard limit is
    /// <see cref="MaxContentExpansion"/> — which is reported as CLAMPED and warned about once,
    /// because it is the one remaining way this path can still cost the player content.</para>
    ///
    /// <para><b>MASKS ARE HONOURED, and that is not an optimisation.</b> A ScrollRect's content is
    /// routinely many times taller than its viewport; without clipping each graphic against its
    /// nearest enabled <see cref="RectMask2D"/> / <see cref="Mask"/> ancestor, one scroll list would
    /// expand the frame (and the render target) by an order of magnitude for content that is not
    /// drawn. The clip is carried down the walk and intersected, in host-local space, using
    /// axis-aligned bounds throughout — a rotated child inside a mask therefore measures as its
    /// bounding box, which errs towards over-measuring, which is the safe direction.</para>
    ///
    /// <para>Never throws. If anything is missing the frame falls back to the host rect, i.e.
    /// exactly ModBuild 192's behaviour.</para>
    ///
    /// <para><b>ModBuild 201 ALSO MEASURES THE CONTENT'S OWN SCALE HERE</b>, in the same walk, because
    /// this is the only place that already knows both a child's authored rect and where that rect
    /// LANDS in host space. See <see cref="Entry.MinContentScale"/> for what that number is for and
    /// what the ModBuild 200 hardware log said about it. It also QUANTISES the frame it produces —
    /// see <see cref="FrameQuantumPx"/>, which is the flicker half of this round.</para>
    /// </summary>
    private static void MeasureFrame(Entry e)
    {
        ConvertedPanel panel = e.Panel;
        RectTransform? host = panel.HostRect;
        if (host == null || panel.HostGo == null)
            return;

        float started = Time.realtimeSinceStartup;
        Rect hostRect = host.rect;
        Rect union = hostRect;
        // THE CONTENT-SCALE CENSUS (ModBuild 201). Accumulated across the walk, committed at the end.
        float minScale = 1f;
        float minScaleArea = 0f;
        string minScaleName = string.Empty;
        int scaleSamples = 0;
        int subCritical = 0;
        float subCriticalArea = 0f;
        // A graphic must cover at least this much of the host rect before it is allowed to lower the
        // number — see MinScaledAreaFraction for why a bare minimum over every graphic is the wrong
        // statistic and would let one decorative pip quadruple every window's render target.
        float areaFloor = Mathf.Abs(hostRect.width * hostRect.height) * MinScaledAreaFraction;

        ContentStack.Clear();
        ContentStack.Add(new ClipFrame(panel.HostGo.transform, Unbounded));
        while (ContentStack.Count > 0)
        {
            int last = ContentStack.Count - 1;
            ClipFrame node = ContentStack[last];
            ContentStack.RemoveAt(last);
            Transform t = node.Transform;
            if (t == null || !t.gameObject.activeSelf)
                continue;
            if (ReferenceEquals(t, e.CamGo != null ? e.CamGo.transform : null))
                continue;

            bool isRoot = ReferenceEquals(t, panel.HostGo.transform);
            // FOREIGN RENDER SUBTREES ARE EXCLUDED FROM THE FRAME, and that does NOT lose them.
            // The same rule the layer sweep uses, for the same reason: a real Renderer or a Camera
            // in here belongs to somebody else (the live 3D character rig and its preview camera;
            // the shop window pools up to 44 of them as items arrive), it is NOT moved onto the
            // capture layer, and it is therefore NOT in the captured image at all. Framing it would
            // allocate render-target area for pixels that are guaranteed empty AND pull the frame's
            // centre off the content that IS captured. What happens to it instead is exactly what
            // happened before this path existed and before ModBuild 193 changed anything: it stays
            // on the game's own layer and the HEAD camera draws it directly, at its true world pose.
            // So it remains visible — unfiltered, and depth-sorted against the quad by its own world
            // z, which is ModBuild 192's behaviour unchanged. Nothing about the capture frame can
            // take it away; the frame only ever decides what the CAPTURE covers.
            if (!isRoot && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
                continue;

            Rect clip = node.Clip;
            var rt = t as RectTransform;
            if (rt != null && !isRoot)
            {
                if (TryHostLocalBounds(host, rt, out Rect bounds))
                {
                    if (ClipsChildren(t))
                    {
                        if (!Intersect(clip, bounds, out clip))
                            continue; // fully clipped away: neither this nor anything under it draws
                    }
                    var graphic = t.GetComponent<Graphic>();
                    if (Draws(graphic) && Intersect(clip, bounds, out Rect visible))
                    {
                        union = Union(union, visible);
                        // THE SCALE OF THIS GRAPHIC'S OWN AUTHORED PIXELS, in host-local pixels. The
                        // two quantities are already in hand: `local` is the rect the artist authored
                        // and `bounds` is where it landed. Their ratio IS the accumulated scale chain
                        // between the two, without touching a single extra transform.
                        Rect local = rt.rect;
                        if (local.width > 1f && local.height > 1f)
                        {
                            float sx = bounds.width / local.width;
                            float sy = bounds.height / local.height;
                            // A ROTATED child measures as its bounding box, so its two ratios diverge
                            // and neither is a scale. Skip it rather than report a fiction; the axis-
                            // aligned siblings in the same subtree carry the same scale anyway.
                            if (sx > 1e-4f && sy > 1e-4f
                                && Mathf.Abs(sx - sy) <= 0.05f * Mathf.Max(sx, sy))
                            {
                                float s = Mathf.Min(sx, sy);
                                float area = Mathf.Abs(visible.width * visible.height);
                                scaleSamples++;
                                if (s < ScaledContentThreshold)
                                {
                                    subCritical++;
                                    if (area > subCriticalArea)
                                        subCriticalArea = area;
                                }
                                if (area >= areaFloor && s < minScale)
                                {
                                    minScale = s;
                                    minScaleArea = area;
                                    minScaleName = t.name;
                                }
                            }
                        }
                    }
                }
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                ContentStack.Add(new ClipFrame(t.GetChild(i), clip));
        }
        ContentStack.Clear();

        // THE CLAMP — the one place content can still be lost, so it keeps the host rect centred and
        // gives away as much of the overspill as the budget allows, edge by edge.
        //
        // ModBuild 201: WHAT IS QUANTISED IS THE OVERSPILL, NOT THE ABSOLUTE EDGE — see
        // FrameQuantumPx for why the frame must sit on a grid at all, and read this paragraph for why
        // the grid is anchored on the HOST RECT'S OWN EDGES rather than on its centre. A host rect is
        // 390x880 or 1143x1080 uGUI px; its half-extents are not multiples of anything. Snapping
        // absolute edges to a grid would therefore inflate EVERY window (390x880 -> 448x896) and turn
        // this path's "the capture frame IS the host rect, so ON and OFF geometry are identical"
        // promise into a permanent GROWN reading on windows that overspill by nothing at all.
        // Quantising the overspill instead keeps a non-overspilling frame EXACTLY the host rect, and
        // still delivers the property the grid is for: between two measurements the host rect does
        // not move, so every frame edge differs from the last only by a whole number of grid cells,
        // and with the rate quantised to RateQuantum that is a whole number of texels.
        // The expansion limit is rounded OUTWARD too, never inward: this grid must not be able to
        // crop a single pixel more than ModBuild 200 already did. It costs at most one grid cell of
        // extra transparent margin on a window that was already at the limit.
        float padX = QuantiseUp(hostRect.width * (MaxContentExpansion - 1f) * 0.5f);
        float padY = QuantiseUp(hostRect.height * (MaxContentExpansion - 1f) * 0.5f);
        float left = Mathf.Clamp(QuantiseUp(hostRect.xMin - union.xMin), 0f, padX);
        float right = Mathf.Clamp(QuantiseUp(union.xMax - hostRect.xMax), 0f, padX);
        float down = Mathf.Clamp(QuantiseUp(hostRect.yMin - union.yMin), 0f, padY);
        float up = Mathf.Clamp(QuantiseUp(union.yMax - hostRect.yMax), 0f, padY);
        float xMin = hostRect.xMin - left;
        float xMax = hostRect.xMax + right;
        float yMin = hostRect.yMin - down;
        float yMax = hostRect.yMax + up;
        bool clamped = xMin > union.xMin + 0.5f || xMax < union.xMax - 0.5f
                       || yMin > union.yMin + 0.5f || yMax < union.yMax - 0.5f;
        Rect frame = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        if (frame.width < 1f || frame.height < 1f)
            frame = hostRect;

        e.Frame = frame;
        e.HostRectAtMeasure = hostRect;
        e.ExpandX = Mathf.Max(0f, frame.width - hostRect.width);
        e.ExpandY = Mathf.Max(0f, frame.height - hostRect.height);
        e.ExpandClamped = clamped;
        e.ContentMeasures++;
        e.LastMeasureFrame = Time.frameCount;
        RecordContentScale(e, minScale, minScaleArea, minScaleName, scaleSamples,
                           subCritical, subCriticalArea);
        e.ContentMs += (Time.realtimeSinceStartup - started) * 1000.0;

        if (clamped && !e.ExpandClampWarned)
        {
            e.ExpandClampWarned = true;
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: '{e.Window}' draws content that reaches "
                              + $"{union.width:F0}x{union.height:F0} uGUI px around a "
                              + $"{hostRect.width:F0}x{hostRect.height:F0} host rect — more than the "
                              + $"{MaxContentExpansion:F0}x expansion this path will frame. THE "
                              + $"CONSEQUENCE: the capture frames {frame.width:F0}x{frame.height:F0} "
                              + "and whatever lies outside THAT is cropped from the supersampled "
                              + "image; it was visible before this build and is not now. The clamp "
                              + "exists because the render target scales with the frame, so an "
                              + "unbounded frame is an unbounded allocation. If this window's content "
                              + "genuinely lives that far outside its own frame, raise "
                              + "MaxContentExpansion (and expect the VRAM cost to follow), or switch "
                              + "[WorldUI] PanelSupersample off for this session.");
        }
    }

    /// <summary>Snap an overspill (always &gt;= 0) UP to the capture-frame grid — see
    /// <see cref="FrameQuantumPx"/>. Every use rounds OUTWARD, so quantising the capture frame can
    /// only ever grow it and can never crop content that ModBuild 200 kept.</summary>
    private static float QuantiseUp(float v) => Mathf.Ceil(v / FrameQuantumPx) * FrameQuantumPx;

    /// <summary>
    /// Commit the content-scale census this measurement produced, and keep the whole distribution of
    /// it — because the summary field of the previous round was quoted as an operating point and was
    /// not one. See <see cref="Entry.MinContentScale"/>.
    /// </summary>
    private static void RecordContentScale(Entry e, float minScale, float area, string name,
                                           int samples, int subCritical, float subCriticalArea)
    {
        // A window whose walk found no measurable rect at all keeps the previous reading rather than
        // silently resetting to 1.0 — an unmeasured window and an unscaled one must not print alike.
        if (samples <= 0)
        {
            e.ContentScaleUnmeasured++;
            return;
        }
        minScale = Mathf.Clamp(minScale, MinContentScaleFloor, 1f);
        e.MinContentScale = minScale;
        e.MinContentScaleArea = area;
        e.MinContentScaleName = name;
        e.ContentScaleSamples = samples;
        e.ScaledGraphics = subCritical;
        e.ScaledGraphicsArea = subCriticalArea;

        e.ContentScaleReadings++;
        e.ContentScaleSum += minScale;
        if (e.ContentScaleReadings == 1)
        {
            e.MinContentScaleLowest = minScale;
            e.MinContentScaleHighest = minScale;
        }
        else
        {
            if (minScale < e.MinContentScaleLowest)
                e.MinContentScaleLowest = minScale;
            if (minScale > e.MinContentScaleHighest)
                e.MinContentScaleHighest = minScale;
        }
    }

    /// <summary>
    /// Does this transform CLIP its children? Both uGUI clipping mechanisms count:
    /// <see cref="RectMask2D"/> (a rectangle in the shader) and <see cref="Mask"/> (a stencil
    /// effect, which needs a <see cref="Graphic"/> to write the stencil and is inert without one).
    /// Only ENABLED ones — a disabled mask clips nothing, and the conversion enables/adds masks of
    /// its own (<c>CanvasConversion.EnsureScrollClipping</c>), so the live component state is the
    /// only trustworthy answer here.
    /// </summary>
    private static bool ClipsChildren(Transform t)
    {
        var rect2d = t.GetComponent<RectMask2D>();
        if (rect2d != null && rect2d.enabled && rect2d.gameObject.activeInHierarchy)
            return true;
        var mask = t.GetComponent<Mask>();
        if (mask != null && mask.enabled && mask.gameObject.activeInHierarchy)
        {
            var graphic = t.GetComponent<Graphic>();
            if (graphic != null && graphic.enabled)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does this graphic put pixels on the screen? Permissive by design (see
    /// <see cref="MeasureFrame"/>): enabled, active, not culled by uGUI's own rect culling, and not
    /// authored fully transparent. A CanvasGroup fade is deliberately NOT consulted — a window
    /// measured mid-fade would otherwise report a frame that is too small and crop itself for a
    /// cadence once the fade completed.
    /// </summary>
    private static bool Draws(Graphic? g)
    {
        if (g == null || !g.enabled || !g.gameObject.activeInHierarchy)
            return false;
        if (g.color.a <= 0.004f)
            return false;
        CanvasRenderer cr = g.canvasRenderer;
        return cr != null && !cr.cull;
    }

    /// <summary>Axis-aligned bounds of <paramref name="rt"/> in <paramref name="host"/>'s local
    /// space, in uGUI pixels. World corners are used rather than the raw rect so a child under any
    /// chain of scales/rotations is measured where it actually lands.</summary>
    private static bool TryHostLocalBounds(RectTransform host, RectTransform rt, out Rect bounds)
    {
        bounds = default;
        Rect local = rt.rect;
        if (local.width <= 0f && local.height <= 0f)
            return false;
        rt.GetWorldCorners(ContentCorners);
        Vector3 first = host.InverseTransformPoint(ContentCorners[0]);
        float minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
        for (int i = 1; i < 4; i++)
        {
            Vector3 p = host.InverseTransformPoint(ContentCorners[i]);
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private static bool Intersect(Rect a, Rect b, out Rect result)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float yMax = Mathf.Min(a.yMax, b.yMax);
        result = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return xMax > xMin && yMax > yMin;
    }

    private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(
        Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
        Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    // ---- CONTENT INTEGRITY: what the window's TEXT is actually able to draw ----------------------

    /// <summary>Work stack for <see cref="MeasureContent"/>. Deliberately NOT
    /// <see cref="ContentStack"/> or <see cref="Scratch"/>: the frame measurement, the layer sweep and
    /// this scan all run inside the same LateUpdate and can call each other (the release repair runs
    /// all three), and sharing one buffer between re-entrant walks is the kind of coupling that
    /// produces a wrong number once and then never again reproducibly.
    /// <para>From ModBuild 203 it carries the RESOLVED DRAW ORDER and the CLIP down the walk (see
    /// <see cref="OrderFrame"/>) so the over-paint census is O(subtree) and rides this one traversal
    /// instead of adding a second.</para></summary>
    private static readonly List<OrderFrame> TextWalk = new(256);

    /// <summary>
    /// A node of <see cref="MeasureContentCore"/>'s walk, plus everything that is decided by its
    /// ANCESTORS and would otherwise have to be re-derived by climbing back up per graphic:
    /// <list type="bullet">
    /// <item><see cref="Order"/> — the <c>sortingOrder</c> of the nearest enabled ancestor
    /// <see cref="Canvas"/> that has <c>overrideSorting</c> set, falling back to the host (root)
    /// canvas's own order. This is the OUTER half of uGUI's painter key.</item>
    /// <item><see cref="Canvas"/> — the nearest enabled ancestor canvas whether or not it overrides,
    /// so a plate can name the canvas it belongs to and print that canvas's sorting state.</item>
    /// <item><see cref="Clip"/> / <see cref="ClipEmpty"/> — the mask rectangle in force, in host-local
    /// uGUI px, exactly as <see cref="MeasureFrame"/> carries it. A scroll list's off-screen rows must
    /// not be counted as painted over: they are not drawn at all.</item>
    /// </list>
    /// <para><b>THE CLIP IS ADVISORY FOR THE CENSUS AND INERT FOR THE TEXT SCAN.</b> A fully clipped
    /// subtree still gets walked and its text still gets scanned and repaired, because those counters
    /// are eight rounds old and this round must not silently change what they measure — it only stops
    /// the census from recording a rect nothing draws.</para>
    /// </summary>
    private readonly struct OrderFrame
    {
        internal readonly Transform Transform;
        internal readonly int Order;
        internal readonly Canvas? Canvas;
        internal readonly Rect Clip;
        internal readonly bool ClipEmpty;

        internal OrderFrame(Transform transform, int order, Canvas? canvas, Rect clip, bool clipEmpty)
        {
            Transform = transform;
            Order = order;
            Canvas = canvas;
            Clip = clip;
            ClipEmpty = clipEmpty;
        }
    }

    /// <summary>
    /// One drawing graphic as the over-paint census sees it: WHERE it lands (host-local uGUI px,
    /// already clipped by its masks), WHEN it is painted (the resolved order key), and whether it is
    /// itself a full-frame plate. Everything the census answers is a comparison between two of these.
    /// </summary>
    private readonly struct DrawRecord
    {
        internal readonly Graphic Graphic;
        internal readonly Transform Transform;
        internal readonly Canvas? Canvas;
        internal readonly Rect Rect;
        internal readonly float Area;
        /// <summary>Fraction of the OPEN SUB-VIEW's rect this graphic covers (0..1).</summary>
        internal readonly float Cover;
        internal readonly int Order;
        internal readonly int Index;
        internal readonly bool Plate;

        internal DrawRecord(Graphic graphic, Transform transform, Canvas? canvas, Rect rect,
                            float area, float cover, int order, int index, bool plate)
        {
            Graphic = graphic;
            Transform = transform;
            Canvas = canvas;
            Rect = rect;
            Area = area;
            Cover = cover;
            Order = order;
            Index = index;
            Plate = plate;
        }
    }

    /// <summary>Every drawing graphic of the last census, in walk order. Bounded by
    /// <see cref="MaxOverPaintGraphics"/>; cleared the moment the census is committed, so no
    /// <see cref="Graphic"/> reference is held across frames.</summary>
    private static readonly List<DrawRecord> DrawRecords = new(512);

    /// <summary>Indices into <see cref="DrawRecords"/> of the records that are full-frame plates.</summary>
    private static readonly List<int> PlateRecords = new(8);

    /// <summary>Scratch for the per-plate sentences and for the foreign-subtree list.</summary>
    private static readonly StringBuilder OverPaintSb = new(1024);
    private static readonly StringBuilder ForeignSb = new(256);

    /// <summary>The three largest graphics one plate covers, as (area, name) — filled per plate.</summary>
    private static readonly List<(float Area, string Name)> CoveredTop = new(4);

    /// <summary>Property ids the plate census probes for, cached once: a string lookup per property
    /// per plate per scan would be the most expensive thing on this line. <c>_GrabTexture</c> and
    /// <c>_BackgroundTexture</c> are how a UI blur shader reads what is behind it;
    /// <c>_CameraOpaqueTexture</c> is the built-in-pipeline equivalent; a bound-but-null
    /// <c>_MainTex</c> is a plate drawing a flat colour over whatever it covers.</summary>
    private static readonly int GrabTexId = Shader.PropertyToID("_GrabTexture");
    private static readonly int BackgroundTexId = Shader.PropertyToID("_BackgroundTexture");
    private static readonly int CameraOpaqueTexId = Shader.PropertyToID("_CameraOpaqueTexture");

    /// <summary>Scratch for the atlas census sentence.</summary>
    private static readonly StringBuilder AtlasSb = new(256);

    /// <summary>Cap on regenerations one scan may force, so a repair can never become the spike.</summary>
    private const int MaxRegeneratePerScan = 256;

    /// <summary>
    /// <b>THE INSTRUMENT FOR "DIE DARGESTELLTE ANZEIGE IST KAPUTT", AND THE REPAIR IN ONE WALK.</b>
    ///
    /// <para><b>WHAT THE PHOTOGRAPH ACTUALLY SHOWS</b> (.planning/debug/kaputte_anzeige.jpg, measured
    /// off the pixels rather than described): the mercenary window's six stat labels render as
    /// <i>"Ge n i"</i>, <i>"Go"</i>, <i>"F rt g te"</i>, <i>"Gebun e g st e"</i>, <i>"ers k :"</i>,
    /// <i>"erb s e :"</i> — individual GLYPHS absent from strings whose LAYOUT is intact. Three
    /// measurements pin that down and each one kills a candidate cause:
    /// <list type="number">
    /// <item>THE ADVANCES ARE FULL WIDTH. The trailing colon of <i>Verstärkungen:</i> (14 characters)
    /// and of <i>Verbesserungen:</i> (15) sit 12 px apart — one character's advance — so NOTHING was
    /// substituted, shortened or removed. A text engine that replaced a missing glyph would have
    /// shifted everything after it. <b>The characters are all still in the layout; their quads put no
    /// pixels down.</b></item>
    /// <item>THE GAPS ARE EMPTY, NOT DIM. Peak luminance inside the gap where <i>d h e</i> of
    /// "Gesundheit" belongs is 24, against a 20 background and a 147 ink. This is not a
    /// contrast/alpha artifact with a faint residue; the pixels were never written.</item>
    /// <item>IT IS NOT UNDERSAMPLING, WHICH IS THIS PATH'S OWN PRIOR DIAGNOSIS. The same window's
    /// SMALLER text — <i>"Schließe sechs Basisspiel-Nebenszenarien ab."</i> — renders every character,
    /// and the surviving glyphs are at full brightness and crisp. Minification below Nyquist dims and
    /// blurs uniformly; it does not delete some glyphs of one label and leave a smaller label
    /// perfect. The ink runs also do NOT line up into vertical stripes across the six rows, which is
    /// what a sampling-phase artifact would look like.</item>
    /// </list></para>
    ///
    /// <para><b>SO THIS SCAN SEPARATES EXACTLY THE THREE STATES THE ROUND WAS ASKED FOR</b>, per text
    /// component, with the comparison count on the same line:
    /// <list type="bullet">
    /// <item><see cref="Entry.GlyphsNotInAtlas"/> — the font asset does NOT have the character, its
    /// fallbacks included. Non-zero proves the atlas cannot serve the string, which is the dynamic
    /// font atlas hypothesis, and it also proves that re-taking the capture is useless.</item>
    /// <item><see cref="Entry.GlyphsNotVisible"/> — the text engine parsed the character and marked it
    /// NOT VISIBLE (overflow truncation, missing-glyph replacement, a maxVisibleCharacters clamp).
    /// The content is genuinely absent and the capture is faithful.</item>
    /// <item><see cref="Entry.GlyphsBlankQuad"/> — the character is visible and its generated quad has
    /// zero area: it holds its advance and draws nothing. <b>That is the exact shape of the
    /// photograph</b>, so a non-zero value here IS the finding and a permanent zero retires the whole
    /// family.</item>
    /// </list>
    /// A fourth state — the capture ran mid-repack, or ahead of the canvas rebuild — is counted at the
    /// capture instant instead (<see cref="Entry.CapturesDuringFontRebuild"/>,
    /// <see cref="Entry.CapturesBeforeCanvasUpdate"/>), because that is the only place the answer
    /// exists.</para>
    ///
    /// <para><b>AND IT REPAIRS AS IT GOES.</b> A component that fails any of the three tests (or every
    /// component, when <paramref name="repairAll"/> — a font atlas repack invalidates meshes that
    /// still pass every test) re-requests its characters into the atlas and re-generates its mesh on
    /// the spot. Forcing the regeneration is preferred over re-taking the capture blindly, because a
    /// stale mesh re-captured is still a stale mesh. The counts reported are the PRE-repair state, so
    /// the log says what was wrong and not merely that something ran.</para>
    ///
    /// <para>Never throws. Bounded by <see cref="MaxGlyphChecksPerScan"/> and
    /// <see cref="MaxRegeneratePerScan"/>, and a scan that hit either bound says so
    /// (<see cref="Entry.ContentScanTruncated"/>) rather than letting a truncated scan read clean.</para>
    /// </summary>
    private static void MeasureContent(Entry e, bool repairAll = false)
    {
        // FAIL SOFT, AND SAY SO. This scan calls into TextMeshPro and into uGUI's text generator on
        // objects the mod does not own. An exception here must cost this scan and nothing else — it
        // must NOT reach LateTick's catch, which stands the entire supersample path down and sends
        // every floated window back to the shimmering direct rendering. A scan that threw is counted
        // separately so it can never be read as a scan that came back clean.
        try
        {
            MeasureContentCore(e, repairAll);
        }
        catch (System.Exception ex)
        {
            e.ContentScanFailures++;
            TextWalk.Clear();
            AtlasSb.Length = 0;
            DrawRecords.Clear();
            PlateRecords.Clear();
            CoveredTop.Clear();
            OverPaintSb.Length = 0;
            ForeignSb.Length = 0;
            if (!e.ContentScanFailWarned)
            {
                e.ContentScanFailWarned = true;
                VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: the content-integrity scan on '{e.Window}' "
                                  + $"threw ({ex.GetType().Name}: {ex.Message}). THE CONSEQUENCE: this "
                                  + "window's CONTENT INTEGRITY field is stale from here on and its "
                                  + "release repair does not regenerate text — the capture, the mip "
                                  + "chain, the layer isolation and the still-window sharpness are "
                                  + "all unaffected, and the failure count on the state line keeps a "
                                  + "failed scan from reading as a clean one.");
            }
        }
    }

    private static void MeasureContentCore(Entry e, bool repairAll)
    {
        ConvertedPanel panel = e.Panel;
        if (panel.HostGo == null)
            return;
        float started = Time.realtimeSinceStartup;

        e.ContentScans++;
        e.TextComponents = 0;
        e.GlyphsChecked = 0;
        e.GlyphsNotInAtlas = 0;
        e.GlyphsNotVisible = 0;
        e.GlyphsBlankQuad = 0;
        e.WorstText = string.Empty;
        e.WorstTextBad = 0;
        e.WorstTextChecked = 0;
        e.TextCulled = 0;
        e.TextClean = 0;
        e.MeshQuadsChecked = 0;
        e.MeshMissingQuads = 0;
        e.MeshDegenerateQuads = 0;
        e.MeshDegenerateUv = 0;
        e.MeshUvOutOfRange = 0;
        e.MeshNonFinite = 0;
        e.MeshWorst = string.Empty;
        e.MeshWorstBad = 0;
        e.SubMeshesSeen = 0;
        e.SubMeshesEmpty = 0;
        e.SubMeshesInUse = 0;
        e.SubMeshesInactive = 0;
        e.SubMeshesCulled = 0;
        e.SubMeshesWrongLayer = 0;
        e.SubMeshesNoTexture = 0;
        e.SubMeshWorst = string.Empty;
        e.ContentScanTruncated = false;
        e.RegeneratedComponents = 0;
        e.RegeneratedChars = 0;
        AtlasSb.Length = 0;
        int atlasesNamed = 0;
        bool anyRepaired = false;

        // ---- THE OVER-PAINT CENSUS RIDES THIS WALK (ModBuild 203) --------------------------------
        // Nothing below adds a traversal: the census reads the same nodes, the same one
        // GetComponent<Graphic> per node, and carries what it needs down the stack.
        ResetOverPaint(e);
        RectTransform? host = panel.HostRect;
        bool census = host != null;
        // THE CENSUS'S OWN BUDGET. Only the two phases that are NOT shared with the text scan can be
        // attributed honestly — resolving the open sub-view and committing the plate comparison. The
        // per-node share (one GetComponent<Canvas>, two mask GetComponents on interior nodes only,
        // and one host-local bounds per DRAWING graphic) rides inside the CONTENT INTEGRITY scan's own
        // ms figure, which is printed next to this one for exactly that reason. Both are bounded:
        // MaxOverPaintGraphics records, MaxPlatesReported sentences.
        float censusStarted = Time.realtimeSinceStartup;
        if (census)
            ResolveOpenSubView(e, panel, host!);
        e.OverPaintMs += (Time.realtimeSinceStartup - censusStarted) * 1000.0;
        float refArea = Mathf.Abs(e.OpenViewRect.width * e.OpenViewRect.height);
        int rootOrder = panel.HostCanvas != null ? panel.HostCanvas.sortingOrder : 0;
        int visitIndex = 0;

        TextWalk.Clear();
        TextWalk.Add(new OrderFrame(panel.HostGo.transform, rootOrder, panel.HostCanvas,
                                    Unbounded, clipEmpty: false));
        while (TextWalk.Count > 0)
        {
            int last = TextWalk.Count - 1;
            OrderFrame node = TextWalk[last];
            TextWalk.RemoveAt(last);
            Transform t = node.Transform;
            if (t == null || !t.gameObject.activeInHierarchy)
                continue;
            if (ReferenceEquals(t, e.CamGo != null ? e.CamGo.transform : null))
                continue;
            bool isRoot = ReferenceEquals(t, panel.HostGo.transform);
            e.OverPaintVisited++;
            // THE DEPTH-FIRST HIERARCHY INDEX — the INNER half of uGUI's painter key. Children are
            // pushed in reverse and popped LIFO, so this counter increments in exact pre-order, which
            // is the order uGUI walks a canvas's graphics in when it builds its batches.
            int index = ++visitIndex;
            // The same foreign-subtree rule the frame measurement and the layer sweep use, for the
            // same reason: a real Renderer or a Camera in here belongs to somebody else. ModBuild 203
            // NAMES them on the way past instead of only counting them — see NoteForeignSubtree.
            if (!isRoot)
            {
                var renderer = t.GetComponent<Renderer>();
                var foreignCam = renderer == null ? t.GetComponent<Camera>() : null;
                if (renderer != null || foreignCam != null)
                {
                    if (census)
                        NoteForeignSubtree(e, host!, t, renderer, foreignCam);
                    continue;
                }
            }

            // ONE GetComponent, not two: a transform carries at most one Graphic, and both text
            // families derive from it. On the party window this walk visits ~2700 transforms, so the
            // difference is a whole millisecond of a release frame.
            var graphic = t.GetComponent<Graphic>();
            if (graphic is TMP_Text tmp)
            {
                if (ScanTmpText(e, tmp, repairAll, ref atlasesNamed))
                    anyRepaired = true;
            }
            else if (graphic is Text legacy && ScanLegacyText(e, legacy, repairAll))
            {
                anyRepaired = true;
            }

            // ---- resolve this node's draw order and clip, for itself and for its children --------
            int order = node.Order;
            Canvas? canvas = node.Canvas;
            Rect clip = node.Clip;
            bool clipEmpty = node.ClipEmpty;
            if (census && !isRoot)
            {
                // A nested Canvas only starts a new sorting band when it is ENABLED and actually
                // OVERRIDES. That distinction is not decorative: 19 of this window's 20 adopted
                // nested canvases carry overrideSorting = false, so they are NOT sorting roots and
                // everything under them sorts by hierarchy inside the host batch — exactly as the
                // flat game drew it. Reading the flag rather than assuming it is what keeps this
                // census correct now that the sibling-canvas tie hypothesis is dead, and what would
                // make it show a real tie if the adoption's sorting state ever changed.
                var own = t.GetComponent<Canvas>();
                if (own != null && own.enabled)
                {
                    canvas = own;
                    if (own.overrideSorting)
                        order = own.sortingOrder;
                }

                var rt = t as RectTransform;
                if (rt != null)
                {
                    // A mask on a LEAF clips nothing, so the two mask GetComponents are only paid on
                    // interior nodes — which is where every mask in a uGUI hierarchy actually is.
                    if (!clipEmpty && t.childCount > 0 && ClipsChildren(t)
                        && TryHostLocalBounds(host!, rt, out Rect clipBounds))
                    {
                        if (!Intersect(clip, clipBounds, out clip))
                            clipEmpty = true;
                    }
                    if (!clipEmpty && Draws(graphic) && !IsModOwned(t.name))
                        RecordDrawn(e, host!, rt, graphic!, canvas, clip, order, index, refArea);
                }
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                TextWalk.Add(new OrderFrame(t.GetChild(i), order, canvas, clip, clipEmpty));
        }
        TextWalk.Clear();
        if (census)
        {
            float commitStarted = Time.realtimeSinceStartup;
            CommitOverPaint(e);
            e.OverPaintMs += (Time.realtimeSinceStartup - commitStarted) * 1000.0;
        }

        if (atlasesNamed == 0)
            AtlasSb.Append("no font asset reached");
        e.AtlasNote = AtlasSb.ToString();
        AtlasSb.Length = 0;

        // ONE canvas update for the whole batch, and only if anything was actually re-generated: a
        // ForceUpdateCanvases per component would be N layout passes for one release.
        if (anyRepaired)
            Canvas.ForceUpdateCanvases();

        e.ContentScanMs += (Time.realtimeSinceStartup - started) * 1000.0;
    }

    /// <summary>Scan (and if needed repair) one TextMeshPro component. Returns true if it regenerated.
    /// <para>The ATLAS test reads the component's requested string, because that is what it ASKS for
    /// and it is answerable whether or not a mesh was ever generated; the VISIBILITY and QUAD tests
    /// read <c>textInfo</c>, because that is what it PRODUCED. The two together are what separates
    /// "the font cannot serve this string" from "the font can and the mesh still draws nothing".</para></summary>
    private static bool ScanTmpText(Entry e, TMP_Text t, bool repairAll, ref int atlasesNamed)
    {
        if (!t.isActiveAndEnabled)
            return false;
        string s = t.text;
        if (string.IsNullOrEmpty(s))
            return false;
        e.TextComponents++;

        int bad = 0;
        int checkedHere = 0;
        TMP_FontAsset? font = t.font;
        if (font != null)
        {
            NoteTmpAtlas(font, ref atlasesNamed);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                // Crude but sufficient rich-text skip: a '<...>' run is markup, not glyphs, and
                // counting it would inflate the comparison count with characters nothing draws.
                if (c == '<') { inTag = true; continue; }
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                    continue;
                if (e.GlyphsChecked >= MaxGlyphChecksPerScan)
                {
                    e.ContentScanTruncated = true;
                    break;
                }
                e.GlyphsChecked++;
                checkedHere++;
                if (!font.HasCharacter(c, true, false))
                {
                    e.GlyphsNotInAtlas++;
                    bad++;
                }
            }
        }

        TMP_TextInfo info = t.textInfo;
        if (info != null && info.characterInfo != null)
        {
            int n = Mathf.Min(info.characterCount, info.characterInfo.Length);
            for (int i = 0; i < n; i++)
            {
                TMP_CharacterInfo ci = info.characterInfo[i];
                char c = ci.character;
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                    continue;
                if (!ci.isVisible)
                {
                    e.GlyphsNotVisible++;
                    bad++;
                    continue;
                }
                Vector3 bl = ci.vertex_BL.position;
                Vector3 tr = ci.vertex_TR.position;
                float area = Mathf.Abs((tr.x - bl.x) * (tr.y - bl.y));
                if (area <= DegenerateQuadArea)
                {
                    e.GlyphsBlankQuad++;
                    bad++;
                }
            }
        }

        // THE SUBMITTED MESH — the measurement ModBuild 196 did not make, and the reason 236 clean
        // readings did not settle anything. Everything above reads the LAYOUT; this reads what was
        // actually written into the vertex and UV arrays that get uploaded.
        int meshBad = ScanTmpMesh(e, t, info);

        NoteRendererState(e, t.canvasRenderer);
        // AND THE SUB-MESHES THIS COMPONENT HANDS ITS OTHER MATERIALS TO — see Entry.SubMeshesSeen.
        // Done from here rather than from the outer walk on purpose: that walk skips anything
        // !activeInHierarchy, which is precisely one of the states this needs to be able to report.
        ScanTmpSubMeshes(e, t);

        if (bad > e.WorstTextBad)
        {
            e.WorstTextBad = bad;
            e.WorstTextChecked = checkedHere;
            e.WorstText = Describe(t.gameObject.name, s);
        }
        if (meshBad > e.MeshWorstBad)
        {
            e.MeshWorstBad = meshBad;
            e.MeshWorst = Describe(t.gameObject.name, s);
        }
        if (bad == 0 && meshBad == 0)
            e.TextClean++;
        bad += meshBad;

        if ((!repairAll && bad == 0) || e.RegeneratedComponents >= MaxRegeneratePerScan)
        {
            if (e.RegeneratedComponents >= MaxRegeneratePerScan)
                e.ContentScanTruncated = true;
            return false;
        }

        // THE REPAIR, in the order that makes it a repair rather than a retry: put the characters
        // back in the atlas FIRST (a mesh regenerated against an atlas that still lacks them would
        // come out exactly as broken), then re-parse and re-generate the mesh.
        if (font != null && font.atlasPopulationMode == AtlasPopulationMode.Dynamic)
            font.TryAddCharacters(s, out string _);
        t.ForceMeshUpdate(true, true);
        e.RegeneratedComponents++;
        e.RegeneratedChars += s.Length;
        return true;
    }

    /// <summary>
    /// <b>THE SUBMITTED MESH — THE ONE PLACE THE PHOTOGRAPH'S FAULT CAN LIVE AND ModBuild 196 COULD
    /// NOT LOOK.</b>
    ///
    /// <para><b>WHY THIS EXISTS AND WHY THE PREVIOUS INSTRUMENT WAS NOT WRONG, ONLY SHORT.</b>
    /// ModBuild 196 shipped a three-way scan and the full ModBuild 196 hardware log was then counted
    /// rather than sampled: <b>236 readings, of which 152 read <c>0 / 0 / 0</c> and 84 read exactly
    /// one character parsed-but-not-visible. Zero characters missing from the atlas, zero zero-area
    /// quads, zero atlas repacks, in a session with real drags and up to 140 release repairs on one
    /// window.</b> The photograph shows dozens of missing glyphs across six rows. A working
    /// instrument that never sees the defect is measuring the wrong quantity, and this is the
    /// quantity it was measuring: every counter in <see cref="ScanTmpText"/> reads
    /// <see cref="TMP_CharacterInfo"/>, which is the LAYOUT RECORD the text engine writes while it
    /// lays the string out. Its <c>isVisible</c> flag and its <c>vertex_BL/TR</c> positions are
    /// decided BEFORE anything is written into a mesh.</para>
    ///
    /// <para><b>WHAT IS DOWNSTREAM OF IT, AND MATCHES THE PHOTOGRAPH EXACTLY.</b> The glyph the eye
    /// sees is four vertices and four UVs in <c>textInfo.meshInfo[m]</c>, uploaded to the
    /// <see cref="CanvasRenderer"/>. Three things can go wrong there while every layout counter stays
    /// clean, and all three produce full advances with no ink — which is precisely what was measured
    /// off the image (the trailing colons of <i>Verstärkungen:</i> and <i>Verbesserungen:</i> exactly
    /// one advance apart, gaps at background luminance, no vertical alignment across rows):
    /// <list type="number">
    /// <item>THE QUAD WAS NEVER WRITTEN. The character's <c>vertexIndex</c> points past the end of
    /// the mesh that was actually generated — the layout ran further than the mesh did.</item>
    /// <item>THE ATLAS UV RECTANGLE COLLAPSED. Four identical UVs sample ONE atlas texel, so an SDF
    /// shader draws a flat distance value over the whole quad: full geometry, full advance, no ink.
    /// <b>Nothing in ModBuild 196 read a single UV.</b></item>
    /// <item>THE UVs POINT OUTSIDE THE ATLAS, i.e. they are stale against a texture that moved.</item>
    /// </list>
    /// A non-finite vertex is counted as a fourth: the GPU discards such a triangle silently.</para>
    ///
    /// <para><b>HOW TO READ IT.</b> <see cref="Entry.MeshQuadsChecked"/> is the denominator and is
    /// printed on the same line as every count, so "the mesh instrument never ran" can never again
    /// look like "the mesh is clean" — the mistake this whole round exists to stop repeating. If
    /// these counters stay at zero through a session in which the user sees the defect, then the
    /// fault is not in the text at ANY level, source or mesh, and the next round must stop looking at
    /// text: what remains is the capture path (measured by the CAPTURE PATH field) and frame pacing
    /// (measured by MOTION BUDGET and by the RELEASE line's dropped-frame count).</para>
    ///
    /// <para>Cost: four vector reads per visible character, inside a walk that already visits every
    /// character. Bounded by the same <see cref="MaxGlyphChecksPerScan"/> budget as the rest.</para>
    /// </summary>
    private static int ScanTmpMesh(Entry e, TMP_Text t, TMP_TextInfo? info)
    {
        if (info == null || info.characterInfo == null || info.meshInfo == null)
            return 0;
        int bad = 0;
        int n = Mathf.Min(info.characterCount, info.characterInfo.Length);
        for (int i = 0; i < n; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible || char.IsWhiteSpace(ci.character) || char.IsControl(ci.character))
                continue;
            int m = ci.materialReferenceIndex;
            if (m < 0 || m >= info.meshInfo.Length)
            {
                e.MeshQuadsChecked++;
                e.MeshMissingQuads++;
                bad++;
                continue;
            }
            TMP_MeshInfo mi = info.meshInfo[m];
            Vector3[] verts = mi.vertices;
            Vector2[] uvs = mi.uvs0;
            int v = ci.vertexIndex;
            e.MeshQuadsChecked++;
            // "Written into the mesh" is decided by vertexCount, NOT by the array length: TMP keeps
            // its vertex arrays allocated at the high-water mark of every string this component has
            // ever held, so an array long enough to index proves nothing about this generation.
            if (verts == null || uvs == null || v < 0 || v + 3 >= mi.vertexCount
                || v + 3 >= verts.Length || v + 3 >= uvs.Length)
            {
                e.MeshMissingQuads++;
                bad++;
                continue;
            }

            Vector3 p0 = verts[v], p1 = verts[v + 1], p2 = verts[v + 2], p3 = verts[v + 3];
            Vector2 u0 = uvs[v], u1 = uvs[v + 1], u2 = uvs[v + 2], u3 = uvs[v + 3];
            if (!Finite(p0) || !Finite(p1) || !Finite(p2) || !Finite(p3)
                || !Finite(u0) || !Finite(u1) || !Finite(u2) || !Finite(u3))
            {
                e.MeshNonFinite++;
                bad++;
                continue;
            }

            float minX = Mathf.Min(Mathf.Min(p0.x, p1.x), Mathf.Min(p2.x, p3.x));
            float maxX = Mathf.Max(Mathf.Max(p0.x, p1.x), Mathf.Max(p2.x, p3.x));
            float minY = Mathf.Min(Mathf.Min(p0.y, p1.y), Mathf.Min(p2.y, p3.y));
            float maxY = Mathf.Max(Mathf.Max(p0.y, p1.y), Mathf.Max(p2.y, p3.y));
            if ((maxX - minX) * (maxY - minY) <= DegenerateQuadArea)
            {
                e.MeshDegenerateQuads++;
                bad++;
                continue;
            }

            float uMinX = Mathf.Min(Mathf.Min(u0.x, u1.x), Mathf.Min(u2.x, u3.x));
            float uMaxX = Mathf.Max(Mathf.Max(u0.x, u1.x), Mathf.Max(u2.x, u3.x));
            float uMinY = Mathf.Min(Mathf.Min(u0.y, u1.y), Mathf.Min(u2.y, u3.y));
            float uMaxY = Mathf.Max(Mathf.Max(u0.y, u1.y), Mathf.Max(u2.y, u3.y));
            if ((uMaxX - uMinX) * (uMaxY - uMinY) <= DegenerateUvArea)
            {
                e.MeshDegenerateUv++;
                bad++;
                continue;
            }
            // Half a texel of slack on each side: TMP writes glyph UVs with a padding inset and a
            // legitimate edge glyph can sit fractionally outside [0,1] without sampling anything it
            // should not, since both targets clamp.
            const float slack = 0.001f;
            if (uMinX < -slack || uMinY < -slack || uMaxX > 1f + slack || uMaxY > 1f + slack)
            {
                e.MeshUvOutOfRange++;
                bad++;
            }
        }
        return bad;
    }

    /// <summary>
    /// <b>THE SUB-MESH SCAN — the object four builds of "0 defects out of 3,471 quads" never looked
    /// at.</b> The full argument is on <see cref="Entry.SubMeshesSeen"/>; the short version is that a
    /// <c>TextMeshProUGUI</c> draws only the glyphs served by its FIRST material and hands every
    /// other one — second atlas page, fallback font, inline sprite — to a <see cref="TMP_SubMeshUI"/>
    /// on a CHILD GameObject with its own CanvasRenderer, material, layer and active state.
    /// <c>textInfo.meshInfo[1..]</c> carries their vertex data, which <see cref="ScanTmpMesh"/> reads
    /// and finds clean, and <see cref="NoteRendererState"/> then asks the PARENT whether it drew.
    ///
    /// <para><b>THE ONE THING HERE THAT IS A FIX AND NOT A MEASUREMENT.</b> A sub-mesh born between
    /// two capture-layer sweeps sits on the game's UI layer, which this panel's capture camera does
    /// not cull in — so those glyphs are missing from the TEXTURE while every quad, UV and glyph
    /// record describing them is perfect. That is repaired on sight, unconditionally, and NOT gated
    /// on the defect count: a remedy that only runs when its own diagnostic already fired is a
    /// remedy that never runs, which is the ModBuild 196 mistake this lane has already paid for. The
    /// per-frame layer sweep still owns the general case; this closes the window between its
    /// cadences for the one family of objects that is born mid-string.</para>
    ///
    /// <para>Cost: TMP parents its sub-meshes as DIRECT children of the text GameObject, so this is
    /// one <c>childCount</c> loop and one <c>GetComponent</c> per child of a text component — no
    /// recursive search and no allocation. Windows whose text needs a single material report
    /// <c>0 sub-mesh(es)</c> and pay a single integer compare.</para>
    /// </summary>
    private static void ScanTmpSubMeshes(Entry e, TMP_Text t)
    {
        Transform parent = t.transform;
        int layer = e.Layer;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform c = parent.GetChild(i);
            if (c == null)
                continue;
            var sub = c.GetComponent<TMP_SubMeshUI>();
            if (sub == null)
                continue;
            e.SubMeshesSeen++;

            // THE REPAIR, FIRST AND UNCONDITIONAL — before the emptiness test below can skip
            // anything. Only when this panel actually holds a private layer; a refused panel has
            // Layer < 0 and its subtree must stay exactly where the game put it. Deliberately NOT
            // behind the in-use test: an empty pooled sub-mesh that the next string fills would
            // otherwise be filled ON THE WRONG LAYER and stay there until the next sweep cadence,
            // which is the exact window this repair was added to close.
            int bad = 0;
            if (layer >= 0 && c.gameObject.layer != layer)
            {
                e.SubMeshesWrongLayer++;
                bad++;
                if (!IsRecorded(e, c))
                    e.Relayered.Add(new LayerRecord { Transform = c, OriginalLayer = c.gameObject.layer });
                c.gameObject.layer = layer;
            }

            // THE DENOMINATOR ModBuild 200 DID NOT HAVE, and without which its own numbers cannot be
            // read at all. TMP POOLS these objects: it creates one per material reference the string
            // has EVER needed and leaves the surplus in place with an emptied mesh. A pooled sub-mesh
            // is legitimately culled, legitimately transparent and legitimately without a texture —
            // its normal life looks exactly like the abuse. The ModBuild 200 log read
            // "19 TMP SUB-MESH(ES) ... of which 17 culled/transparent" on the character window and
            // 0 of 0 or 0 of 1 on every other window, and that difference is NOT evidence of a defect
            // until the empty ones are subtracted — the character window is simply the only window
            // with enough text to pool any. A sub-mesh whose MESH carries vertices is one the text
            // engine actually handed glyphs to; only those can be missing from the picture, and only
            // those are counted below.
            Mesh? mesh = sub.mesh;
            if (mesh == null || mesh.vertexCount == 0)
            {
                e.SubMeshesEmpty++;
                if (bad > 0 && e.SubMeshWorst.Length == 0)
                    e.SubMeshWorst = Describe(c.gameObject.name, t.text);
                continue;
            }
            e.SubMeshesInUse++;

            if (!c.gameObject.activeInHierarchy)
            {
                e.SubMeshesInactive++;
                bad++;
            }

            CanvasRenderer cr = sub.canvasRenderer;
            if (cr != null && (cr.cull || cr.GetAlpha() <= 0.004f || cr.GetInheritedAlpha() <= 0.004f))
            {
                e.SubMeshesCulled++;
                bad++;
            }

            Material mat = sub.materialForRendering;
            if (mat == null || (mat.HasProperty(MainTexId) && mat.GetTexture(MainTexId) == null))
            {
                e.SubMeshesNoTexture++;
                bad++;
            }

            if (bad > 0 && e.SubMeshWorst.Length == 0)
                e.SubMeshWorst = Describe(c.gameObject.name, t.text);
        }
    }

    /// <summary>Cached <c>_MainTex</c> id for <see cref="ScanTmpSubMeshes"/> — a string lookup per
    /// sub-mesh per scan would be the most expensive line in the walk.</summary>
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    private static bool Finite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
          || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    private static bool Finite(Vector2 v) =>
        !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.x) || float.IsInfinity(v.y));

    /// <summary>Scan (and if needed repair) one legacy uGUI <see cref="Text"/>. The game's own UI is
    /// TextMeshPro throughout (the hierarchy census in the hardware log reads
    /// <c>[RectTransform, CanvasRenderer, TextMeshProUGUI, TextLocalizedListener]</c> on every label),
    /// so this path exists for the mod's own labels and for completeness; it tests glyph availability
    /// only, because legacy <c>TextGenerator</c> emits four vertices for whitespace as well and a
    /// zero-area quad there is normal rather than a defect.</summary>
    private static bool ScanLegacyText(Entry e, Text t, bool repairAll)
    {
        if (!t.isActiveAndEnabled)
            return false;
        Font? font = t.font;
        string s = t.text;
        if (font == null || string.IsNullOrEmpty(s))
            return false;
        e.TextComponents++;

        int bad = 0;
        int checkedHere = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || char.IsControl(c))
                continue;
            if (e.GlyphsChecked >= MaxGlyphChecksPerScan)
            {
                e.ContentScanTruncated = true;
                break;
            }
            e.GlyphsChecked++;
            checkedHere++;
            if (!font.GetCharacterInfo(c, out CharacterInfo _, t.fontSize, t.fontStyle))
            {
                e.GlyphsNotInAtlas++;
                bad++;
            }
        }

        NoteRendererState(e, t.canvasRenderer);

        if (bad > e.WorstTextBad)
        {
            e.WorstTextBad = bad;
            e.WorstTextChecked = checkedHere;
            e.WorstText = Describe(t.gameObject.name, s);
        }
        if (bad == 0)
            e.TextClean++;

        if ((!repairAll && bad == 0) || e.RegeneratedComponents >= MaxRegeneratePerScan)
            return false;
        if (font.dynamic)
            font.RequestCharactersInTexture(s, t.fontSize, t.fontStyle);
        t.SetAllDirty();
        e.RegeneratedComponents++;
        e.RegeneratedChars += s.Length;
        return true;
    }

    /// <summary>
    /// Fingerprint a TextMeshPro font asset's atlas and detect a REPACK.
    /// <para>TextMeshPro does NOT raise <see cref="Font.textureRebuilt"/> — that event belongs to the
    /// legacy dynamic <see cref="Font"/> — so the hook in <see cref="InstallHooks"/> alone would be
    /// blind to exactly the font family this game uses. Watching the atlas texture COUNT and the atlas
    /// texture's instance id is the cheapest observation that changes when TMP grows or replaces an
    /// atlas, and it is recorded into the same counters as the legacy event so one number answers
    /// "did an atlas move under us".</para>
    /// </summary>
    private static void NoteTmpAtlas(TMP_FontAsset font, ref int atlasesNamed)
    {
        int id = font.GetInstanceID();
        Texture atlas = font.atlasTexture;
        int textureId = atlas != null ? atlas.GetInstanceID() : 0;
        int count = font.atlasTextureCount;
        if (TmpAtlasSeen.TryGetValue(id, out (int Count, int TextureId) seen))
        {
            if (seen.Count != count || seen.TextureId != textureId)
            {
                _fontRebuilds++;
                _fontRebuildFrame = Time.frameCount;
                _fontRebuildName = font.name;
                ArmRebuildRepair();
            }
        }
        TmpAtlasSeen[id] = (count, textureId);

        if (atlasesNamed >= 3)
            return;
        if (atlasesNamed > 0)
            AtlasSb.Append(", ");
        atlasesNamed++;
        AtlasSb.Append('\'').Append(font.name).Append("' ").Append(font.atlasPopulationMode)
               .Append(' ').Append(font.atlasWidth).Append('x').Append(font.atlasHeight)
               .Append(" x").Append(count).Append(" texture(s)");
    }

    /// <summary>
    /// Count a text component that puts NO pixels into the capture for a reason that is not about
    /// glyphs: uGUI/TMP has culled its <see cref="CanvasRenderer"/>, or its effective alpha is zero.
    /// <para>Kept separate from every glyph counter on purpose — see <see cref="Entry.TextCulled"/>
    /// for why "missing from the capture" and "captured and painted over" must not be allowed to
    /// collapse into one number, and for the plain statement that NOTHING in this scan can see the
    /// second of those.</para>
    /// </summary>
    private static void NoteRendererState(Entry e, CanvasRenderer? cr)
    {
        if (cr == null)
            return;
        if (cr.cull || cr.GetAlpha() <= 0.004f || cr.GetInheritedAlpha() <= 0.004f)
            e.TextCulled++;
    }

    /// <summary>A component name plus the first few characters of its string, for the report. Bounded
    /// so one pathological label cannot make the state line unreadable.</summary>
    private static string Describe(string name, string text)
    {
        string trimmed = text.Length <= 28 ? text : text.Substring(0, 28) + "...";
        return name + " (\"" + trimmed.Replace('\n', ' ') + "\")";
    }

    // ---- THE SUB-VIEW SWEEP BURST (ModBuild 203) -------------------------------------------------

    /// <summary>
    /// <b>DID THE OPEN SUB-VIEW CHANGE SINCE THE LAST FRAME?</b> Called at the TOP of the per-frame
    /// service, before <c>SyncGeometry</c>, so that arming a burst can also force this same frame's
    /// capture-frame re-measure (by pulling <see cref="Entry.NextContentFrame"/> to now — the one
    /// existing seam that means "re-measure", used rather than a second MeasureFrame call).
    ///
    /// <para>Everything about WHY is on <see cref="Entry.SubViewChanges"/>. Never throws; a throw
    /// leaves the signature unchanged, which costs one missed burst and nothing else.</para>
    /// </summary>
    private static void NoticeSubViewChange(Entry e)
    {
        int sig;
        try
        {
            sig = ActiveSetSignature(e.Panel);
        }
        catch (System.Exception)
        {
            return;
        }
        if (!e.SubViewSigValid)
        {
            e.SubViewSigValid = true;
            e.SubViewSignature = sig;
            return;
        }
        if (sig == e.SubViewSignature)
            return;

        e.SubViewSignature = sig;
        e.SubViewChanges++;
        // FORCE THE CAPTURE-FRAME RE-MEASURE ON THIS FRAME, whether or not a burst is armed below. A
        // newly opened sub-view is exactly the case in which the window's drawn content changes
        // without its host rect moving, so nothing else in this class would notice: SyncGeometry's own
        // trigger reads the host transform and the host RectTransform, and a tab press moves neither.
        e.NextContentFrame = Time.frameCount;

        // THE COST FUSE — see SweepBurstCooldownFrames. A change inside the cooldown is the SAME
        // repopulation still settling; it extends a burst that is still running and is counted, but
        // it may not arm a second one. Without this a flapping signature would re-arm every frame and
        // this remedy would quietly become the per-frame sweep that was measured and removed twice.
        if (Time.frameCount - e.SubViewChangeFrame < SweepBurstCooldownFrames)
        {
            e.SubViewChangesCoalesced++;
            if (e.SweepBurstFramesLeft > 0)
                e.SweepBurstFramesLeft = Mathf.Max(e.SweepBurstFramesLeft, 1);
            return;
        }

        e.SweepBursts++;
        e.SubViewChangeFrame = Time.frameCount;
        e.SweepBurstFramesLeft = MinSweepBurstFrames;
        e.SweepBurstFramesRun = 0;
        e.SweepBurstMisses = 0;
        e.SweepBurstMoved = 0;
        e.SweepBurstLastHitFrame = -1;
    }

    /// <summary>
    /// A CHEAP, STABLE SIGNATURE OVER WHAT IS OPEN. Two independent parts, deliberately combined so
    /// that neither has to be right on its own:
    /// <list type="number">
    /// <item>the ACTIVE DIRECT CHILDREN of the conversion target, by instance id in sibling order —
    /// which works on any converted window, including ones that have no game sub-views at all;</item>
    /// <item>the game's own <c>NewPartyDisplayUI</c> answer: the <c>ActiveDisplay</c> enum plus which
    /// of the six sub-view roots are active. This catches a switch whose roots are NOT direct children
    /// of the target, which part (1) alone would miss.</item>
    /// </list>
    /// <para>Cost per frame: one <c>childCount</c> loop over a window's direct children (order ten)
    /// plus seven property reads on a singleton. That is why this runs every frame while the SWEEP it
    /// arms — at a measured 1.71 ms — does not.</para>
    /// <para>It is a LOCAL DERIVATION and does not read <c>CanvasConversion</c>'s <c>fx</c> state:
    /// that is another lane's file and exposes no accessor for it.</para>
    /// </summary>
    private static int ActiveSetSignature(ConvertedPanel panel)
    {
        int sig = 17;
        Transform? target = panel.Target;
        if (target != null)
        {
            int n = target.childCount;
            sig = sig * 31 + n;
            for (int i = 0; i < n; i++)
            {
                Transform c = target.GetChild(i);
                if (c != null && c.gameObject.activeSelf)
                    sig = sig * 31 + c.GetInstanceID();
            }
        }

        NewPartyDisplayUI? display;
        try
        {
            display = NewPartyDisplayUI.PartyDisplay;
        }
        catch (System.Exception)
        {
            return sig;
        }
        if (display == null || target == null)
            return sig;
        try
        {
            sig = sig * 31 + (int)display.ActiveDisplay;
            sig = MixSubView(sig, display.CharacterSelector, target);
            sig = MixSubView(sig, display.PerkManager, target);
            sig = MixSubView(sig, display.AbilityCardsDisplay, target);
            sig = MixSubView(sig, display.EnhancementCardsDisplay, target);
            sig = MixSubView(sig, display.ItemInventoryDisplay, target);
            sig = MixSubView(sig, display.BattleGoalWindow, target);
        }
        catch (System.Exception)
        {
            // A partial mix is still STABLE (it throws in the same place every frame), so it stays a
            // usable signature rather than a source of phantom changes.
        }
        return sig;
    }

    private static int MixSubView(int sig, Component? view, Transform target)
    {
        if (view == null)
            return sig * 31;
        Transform t = view.transform;
        bool open = t.gameObject.activeInHierarchy && IsUnder(t, target);
        return sig * 31 + (open ? t.GetInstanceID() : 0);
    }

    /// <summary>
    /// <b>THE BURST — sweep the capture layer on every frame while the newly opened view is still
    /// arriving, then get out of the way.</b>
    ///
    /// <para>Runs AFTER the visibility sync and BEFORE the ordinary cadence check, and resets that
    /// cadence as it goes so a burst frame is never followed by a redundant periodic sweep on the
    /// same frame. It backs off in two ways: it always runs <see cref="MinSweepBurstFrames"/> frames
    /// (the content does not exist yet on frame 0), then extends only while sweeps keep finding late
    /// joiners, and stops after <see cref="SweepBurstMissTolerance"/> consecutive empty sweeps or at
    /// <see cref="MaxSweepBurstFrames"/> whichever comes first.</para>
    ///
    /// <para>At the end it runs ONE content scan, so the CONTENT INTEGRITY, OVER-PAINT and attribution
    /// fields describe the view that was just opened rather than the one that was closed — the whole
    /// reason eight rounds of clean readings could not be assigned to anything.</para>
    ///
    /// <para>Never throws: the caller's LateTick catch would stand the entire supersample path down.</para>
    /// </summary>
    private static void ServiceSweepBurst(Entry e)
    {
        if (e.SweepBurstFramesLeft <= 0)
            return;
        // THE HARD WALL-CLOCK GATE, belt and braces beside the per-burst frame cap: a burst may only
        // ever sweep inside the MaxSweepBurstFrames frames that follow its arming, whatever a coalesced
        // change did to its counters. This is what makes the worst case arithmetic on
        // SweepBurstCooldownFrames a bound and not an intention.
        if (Time.frameCount - e.SubViewChangeFrame >= MaxSweepBurstFrames)
        {
            e.SweepBurstFramesLeft = 0;
            FinishSweepBurst(e);
            return;
        }
        float started = Time.realtimeSinceStartup;
        e.SweepBurstFramesLeft--;
        e.SweepBurstFramesRun++;
        int before = e.LateJoiners;
        ApplyCaptureLayer(e, initial: false);
        // The periodic cadence is re-armed from HERE, so the burst replaces it rather than doubling it.
        e.NextSweepFrame = Time.frameCount + SweepIntervalFrames;
        int moved = e.LateJoiners - before;
        e.SweepBurstMoved += moved;
        if (moved > 0)
        {
            e.SweepBurstLastHitFrame = Time.frameCount;
            e.SweepBurstMisses = 0;
            // STILL ARRIVING: extend, up to the hard cap. This is the only thing that can make a burst
            // longer than MinSweepBurstFrames, so a burst that reaches the cap is a window that was
            // still repopulating for a quarter of a second — which is a finding about the GAME's
            // cadence and not about ours, and the report says so.
            if (e.SweepBurstFramesRun + e.SweepBurstFramesLeft < MaxSweepBurstFrames)
                e.SweepBurstFramesLeft++;
        }
        else
        {
            e.SweepBurstMisses++;
            if (e.SweepBurstFramesRun >= MinSweepBurstFrames
                && e.SweepBurstMisses >= SweepBurstMissTolerance)
            {
                e.SweepBurstFramesLeft = 0;
            }
        }
        e.SweepBurstMs += (Time.realtimeSinceStartup - started) * 1000.0;
        if (e.SweepBurstFramesLeft <= 0)
            FinishSweepBurst(e);
    }

    /// <summary>Close a burst: commit its distribution, re-scan the content so every field on the
    /// state line describes the view that was just opened, and log the one line that prices it.</summary>
    private static void FinishSweepBurst(Entry e)
    {
        int hole = e.SweepBurstLastHitFrame >= 0
            ? e.SweepBurstLastHitFrame - e.SubViewChangeFrame
            : 0;
        e.SweepBurstHoleFramesLast = hole;
        if (hole > e.SweepBurstHoleFramesMax)
            e.SweepBurstHoleFramesMax = hole;
        e.SweepBurstHoleSum += hole;
        e.SweepBurstHoleReadings++;
        e.SweepBurstFramesLast = e.SweepBurstFramesRun;
        e.SweepBurstMovedLast = e.SweepBurstMoved;
        if (e.SweepBurstMoved == 0)
            e.SweepBurstsConverged++;

        // ONE content scan per burst, not per frame: this is what re-attributes every field on the
        // state line to the view that was just opened.
        MeasureContent(e);

        VRLog.Info(Scope, $"PANEL SUPERSAMPLE SUB-VIEW BURST '{e.Window}': TRIGGER = the set of "
            + "ACTIVE sub-view roots inside this window changed (a tab press; no drag, no resize, no "
            + $"host-rect change — which is why nothing before ModBuild 203 forced anything). NOW "
            + $"OPEN: {(e.OpenViewName.Length > 0 ? "'" + e.OpenViewName + "'" : "none")}, "
            + $"ActiveDisplay={(e.OpenViewActive.Length > 0 ? e.OpenViewActive : "unavailable")}, "
            + $"{e.OpenViewCount} root(s). THE BURST ran {e.SweepBurstFramesRun} frame(s) (floor "
            + $"{MinSweepBurstFrames}, cap {MaxSweepBurstFrames}, extended only while sweeps kept "
            + $"finding arrivals, ended after {e.SweepBurstMisses} consecutive empty sweep(s) against "
            + $"a tolerance of {SweepBurstMissTolerance}) and moved {e.SweepBurstMoved} transform(s) "
            + $"onto capture layer {e.Layer} in total ({e.SubViewChanges} change(s) noticed since "
            + $"engage, {e.SubViewChangesCoalesced} of them COALESCED into a running burst by the "
            + $"{SweepBurstCooldownFrames}-frame cost fuse rather than arming a second one), at "
            + $"{(e.SweepBursts > 0 ? e.SweepBurstMs / e.SweepBursts : 0.0):F2} ms per burst so far "
            + $"across {e.SweepBursts} burst(s) (a single sweep measured 1.71 ms on this window "
            + $"against an {FrameBudgetMs:F2} ms frame budget, so a burst is ~15 % of one frame for "
            + "the frames it runs and NOTHING for every other frame — it is armed by a content "
            + "change and disarms itself, which is what makes it different from the ModBuild 193 "
            + "per-frame-while-moving sweep that was measured, shipped and falsified). THE NUMBER "
            + "NOBODY HAS MEASURED BEFORE: "
            + $"{hole} frame(s) elapsed between the sub-view change and the LAST sweep that still "
            + "found a late joiner — that is the length of the window in which this view's content "
            + "was MISSING FROM THE CAPTURE and drawn straight into the eye at its own sorting order. "
            + $"WORST {e.SweepBurstHoleFramesMax}, MEAN "
            + $"{(e.SweepBurstHoleReadings > 0 ? (double)e.SweepBurstHoleSum / e.SweepBurstHoleReadings : 0.0):F1}, "
            + $"over {e.SweepBurstHoleReadings} burst(s), of which {e.SweepBurstsConverged} found "
            + "NOTHING AT ALL. HOW TO READ IT: 0 frames (or a burst that moved 0) means there was no "
            + "hole to close on this switch and the 'initial kaputt beim Öffnen' report is NOT a "
            + "late-joiner problem for this view — look at the OVER-PAINT CENSUS on the state line "
            + "instead. A SMALL number (1-3 frames) means the hole existed and this burst closed it, "
            + "and the user should see the difference on the very next tab press. A number that keeps "
            + "reaching the cap means the game is STILL repopulating after "
            + $"{MaxSweepBurstFrames} frames, i.e. the hole is the GAME's cadence and not ours, and "
            + "the fix would have to be an arrival HOOK rather than any poll — which is the step "
            + "ModBuild 193's own log line has been asking for since it was written. NOTE ON THE "
            + "NEIGHBOURING LINE: each sweep of this burst that moves something also prints a "
            + "'pooled/late transform(s) ... joined capture layer' line, and that line will say it "
            + "ran because the window 'reached its periodic cadence' — it is in another file and "
            + "cannot see this trigger. THIS line is the authority on why those sweeps ran.");
    }

    // ---- THE OVER-PAINT CENSUS (ModBuild 203) ----------------------------------------------------

    /// <summary>Clear the census counters for a fresh scan. The DISTRIBUTION fields
    /// (<see cref="Entry.OverPaintReadings"/> and friends) are deliberately NOT cleared: they are the
    /// whole point of the "never a bare extreme" rule and they accumulate across the session.</summary>
    private static void ResetOverPaint(Entry e)
    {
        DrawRecords.Clear();
        PlateRecords.Clear();
        CoveredTop.Clear();
        OverPaintSb.Length = 0;
        ForeignSb.Length = 0;
        e.OverPaintGraphics = 0;
        e.OverPaintVisited = 0;
        e.OverPaintTruncated = false;
        e.OverPaintPlates = 0;
        e.OverPaintOpaquePlates = 0;
        e.OverPaintCovered = 0;
        e.OverPaintWorstCovered = 0;
        e.OverPaintCoveredArea = 0f;
        e.OverPaintTied = 0;
        e.OverPaintNote = string.Empty;
        e.ForeignRenderers = 0;
        e.ForeignCoplanar = 0;
        e.ForeignNote = string.Empty;
    }

    /// <summary>
    /// <b>WHICH SUB-VIEW IS OPEN — the attribution eight rounds of clean measurements did not carry.</b>
    ///
    /// <para>Two of the party window's six sub-views render broken and four do not, and every field
    /// on this class's state line so far averaged over whichever happened to be open when the report
    /// cadence fired. A reading of "0 defects" is unreadable without knowing which view it was taken
    /// of, which is exactly how twenty-two rounds of correct measurements produced no decision.</para>
    ///
    /// <para><b>THIS IS A LOCAL DERIVATION, ON PURPOSE.</b> <c>CanvasConversion.3.Fit.cs</c> already
    /// computes an open-set signature (<c>fx.OpenSignature</c>) over the same six sub-views, but that
    /// state is private to another lane's file and NO read-only accessor for it exists — so nothing
    /// here reads it and nothing here edits that file. Instead this asks the game's own singleton the
    /// same question the fit asks it: <c>NewPartyDisplayUI.PartyDisplay</c> for the six sub-view roots
    /// and <c>ActiveDisplay</c> for the tab the game itself considers open. The two derivations can in
    /// principle disagree; if they ever do, the log prints the roots this one found and the count, so
    /// the disagreement is visible rather than silent.</para>
    ///
    /// <para>Windows that are not the party display have no such sub-views: the reference rect is then
    /// the HOST RECT and the source string says so, so "no sub-view" and "sub-view not measured" can
    /// never print alike. Never throws — every game-side access is guarded, and a throw falls back to
    /// the host rect.</para>
    /// </summary>
    private static void ResolveOpenSubView(Entry e, ConvertedPanel panel, RectTransform host)
    {
        e.OpenViewRect = host.rect;
        e.OpenViewName = string.Empty;
        e.OpenViewActive = string.Empty;
        e.OpenViewCount = 0;
        e.OpenViewSource = "the HOST RECT (this window has no NewPartyDisplayUI sub-views)";

        NewPartyDisplayUI? display;
        try
        {
            display = NewPartyDisplayUI.PartyDisplay;
        }
        catch (System.Exception)
        {
            e.OpenViewSource = "the HOST RECT (NewPartyDisplayUI.PartyDisplay threw)";
            return;
        }
        if (display == null || panel.Target == null)
            return;

        string active;
        try
        {
            active = display.ActiveDisplay.ToString();
        }
        catch (System.Exception)
        {
            active = "?";
        }
        e.OpenViewActive = active;

        float bestArea = 0f;
        int found = 0;
        try
        {
            found += ConsiderSubView(e, panel, host, display.CharacterSelector, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.PerkManager, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.AbilityCardsDisplay, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.EnhancementCardsDisplay, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.ItemInventoryDisplay, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.BattleGoalWindow, ref bestArea);
        }
        catch (System.Exception)
        {
            // A partial sweep is still attributable — keep whatever was resolved and say the ask threw.
            e.OpenViewCount = found;
            e.OpenViewSource = $"NewPartyDisplayUI.ActiveDisplay={active}, but reading the sub-view "
                               + "roots THREW part way, so the rect may be the host rect";
            return;
        }

        e.OpenViewCount = found;
        e.OpenViewSource = found > 0
            ? $"NewPartyDisplayUI.ActiveDisplay={active}, {found} sub-view root(s) open inside this "
              + "window, the LARGEST of them measured as the reference rect"
            : $"NewPartyDisplayUI.ActiveDisplay={active} but NO sub-view root is active inside this "
              + "window, so the reference is the HOST RECT";
    }

    /// <summary>One candidate sub-view root: it counts when it is active, lives inside the window we
    /// converted, and measures. The LARGEST by host-local area becomes the reference rect — a plate
    /// is judged against the view it would hide, not against the whole host.</summary>
    private static int ConsiderSubView(Entry e, ConvertedPanel panel, RectTransform host,
                                       Component? view, ref float bestArea)
    {
        if (view == null)
            return 0;
        Transform t = view.transform;
        if (!t.gameObject.activeInHierarchy || !IsUnder(t, panel.Target))
            return 0;
        if (t is not RectTransform rt || !TryHostLocalBounds(host, rt, out Rect bounds))
            return 1; // open, but unmeasurable: still counted, so the count and the rect can disagree
        float area = Mathf.Abs(bounds.width * bounds.height);
        if (area > bestArea)
        {
            bestArea = area;
            e.OpenViewRect = bounds;
            e.OpenViewName = t.name;
        }
        return 1;
    }

    /// <summary>Is <paramref name="t"/> at or below <paramref name="root"/>? Bounded climb; no
    /// allocation, and it answers IDENTITY-or-descendant rather than "has a component of that type
    /// somewhere above", which is a slip this project has already shipped twice.</summary>
    private static bool IsUnder(Transform t, Transform? root)
    {
        if (root == null)
            return false;
        for (Transform? p = t; p != null; p = p.parent)
        {
            if (ReferenceEquals(p, root))
                return true;
        }
        return false;
    }

    /// <summary>The mod's own art inside a converted window (cue rings, grab handles) is not the
    /// game's layering and must not appear in a census about the game's layering.</summary>
    private static bool IsModOwned(string name) =>
        name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal);

    /// <summary>
    /// NAME a foreign render subtree instead of only counting it — <see cref="Entry.ForeignRenderers"/>
    /// for the argument. The HOST-LOCAL z is the load-bearing number: the display quad sits at
    /// host-local z = 0, so a foreign renderer at z ~ 0 is COPLANAR with it and its depth tie can
    /// resolve differently in the two MultiPass eyes.
    /// </summary>
    private static void NoteForeignSubtree(Entry e, RectTransform host, Transform t,
                                           Renderer? renderer, Camera? camera)
    {
        e.ForeignRenderers++;
        float z = host.InverseTransformPoint(t.position).z;
        if (Mathf.Abs(z) <= ForeignCoplanarEpsPx)
            e.ForeignCoplanar++;
        if (e.ForeignRenderers > MaxForeignNamed)
            return;
        bool enabled = renderer != null ? renderer.enabled : camera != null && camera.enabled;
        string type = renderer != null ? renderer.GetType().Name
                                       : camera != null ? camera.GetType().Name : "?";
        if (ForeignSb.Length > 0)
            ForeignSb.Append(", ");
        ForeignSb.Append('[').Append(HostPath(t, host.transform)).Append(", ").Append(type)
                 .Append(enabled ? ", ENABLED" : ", disabled")
                 .Append(", host-local z ").Append(z.ToString("F2")).Append(" px")
                 .Append(Mathf.Abs(z) <= ForeignCoplanarEpsPx
                     ? " — COPLANAR with the display quad" : string.Empty)
                 .Append(']');
    }

    /// <summary>Full hierarchy path from the panel host down to <paramref name="t"/>, bounded so one
    /// deep subtree cannot make the state line unreadable.</summary>
    private static string HostPath(Transform? t, Transform root)
    {
        if (t == null)
            return "(destroyed)";
        PathSb.Length = 0;
        int guard = 0;
        for (Transform? p = t; p != null && !ReferenceEquals(p, root) && guard < 24; p = p.parent, guard++)
        {
            if (PathSb.Length > 0)
                PathSb.Insert(0, '/');
            PathSb.Insert(0, p.name);
        }
        if (PathSb.Length == 0)
            PathSb.Append(t.name);
        return PathSb.ToString();
    }

    private static readonly StringBuilder PathSb = new(160);

    /// <summary>Record one drawing graphic for the census. The rect is the graphic's host-local
    /// axis-aligned bounds INTERSECTED with the mask clip in force, i.e. what it can actually put
    /// pixels on — the same rule <see cref="MeasureFrame"/> uses, for the same reason.</summary>
    private static void RecordDrawn(Entry e, RectTransform host, RectTransform rt, Graphic graphic,
                                    Canvas? canvas, Rect clip, int order, int index, float refArea)
    {
        if (DrawRecords.Count >= MaxOverPaintGraphics)
        {
            e.OverPaintTruncated = true;
            return;
        }
        if (!TryHostLocalBounds(host, rt, out Rect bounds))
            return;
        if (!Intersect(clip, bounds, out Rect visible))
            return;
        float area = Mathf.Abs(visible.width * visible.height);
        if (area <= 0f)
            return;
        // COVER IS MEASURED AGAINST THE OPEN SUB-VIEW, NOT THE HOST. A plate that fills a sub-view is
        // what hides a sub-view's content; the host rect of this window is 1920x1080 and a sub-view
        // backdrop at 1620x1080 would read as 84 % of it either way — but on a window where the host
        // is much larger than the open view, judging against the host would miss the plate entirely.
        float cover = 0f;
        if (refArea > 1f && Intersect(e.OpenViewRect, visible, out Rect onView))
            cover = Mathf.Abs(onView.width * onView.height) / refArea;
        bool plate = cover >= OverPaintPlateFraction;
        if (plate)
            PlateRecords.Add(DrawRecords.Count);
        DrawRecords.Add(new DrawRecord(graphic, rt, canvas, visible, area, cover, order, index, plate));
        e.OverPaintGraphics++;
    }

    /// <summary>
    /// <b>THE ANSWER FIELD: for every full-frame plate, HOW MANY DRAWING GRAPHICS IT IS PAINTED OVER.</b>
    ///
    /// <para>A graphic is painted over by a plate when it INTERSECTS the plate's rect, draws BEFORE it
    /// in resolved painter's order, and is not itself a plate (two stacked backdrops are a backdrop,
    /// not an occlusion). "Before" is the pair (resolved <c>sortingOrder</c>, depth-first hierarchy
    /// index) that <see cref="OrderFrame"/> carried down the walk.</para>
    ///
    /// <para><b>AND IT REPORTS THE TIE SEPARATELY.</b> Two canvases that both set
    /// <c>overrideSorting</c> to the SAME <c>sortingOrder</c> have NO defined order between them —
    /// Unity resolves them by canvas registration, not by hierarchy — so for those pairs the
    /// hierarchy index below is a plausible resolution and not a prediction. Every such pair is
    /// counted into <see cref="Entry.OverPaintTied"/> and named as an UNSTABLE TIE, which is how this
    /// instrument proves or refutes the parallel lane's draw-order defect without depending on it.</para>
    ///
    /// <para>Cost: <c>plates x records</c> rectangle compares, with at most
    /// <see cref="MaxOverPaintGraphics"/> records and typically fewer than a handful of plates. No
    /// traversal, no allocation beyond the report string.</para>
    /// </summary>
    private static void CommitOverPaint(Entry e)
    {
        e.OverPaintPlates = PlateRecords.Count;
        for (int p = 0; p < PlateRecords.Count; p++)
        {
            int pi = PlateRecords[p];
            DrawRecord plate = DrawRecords[pi];
            float ownAlpha = plate.Graphic != null ? plate.Graphic.color.a : 0f;
            CanvasRenderer? cr = plate.Graphic != null ? plate.Graphic.canvasRenderer : null;
            float crAlpha = cr != null ? cr.GetAlpha() : 1f;
            float inherited = cr != null ? cr.GetInheritedAlpha() : 1f;
            bool opaque = ownAlpha * crAlpha * inherited >= 0.99f;
            if (opaque)
                e.OverPaintOpaquePlates++;

            int covered = 0;
            int tied = 0;
            float coveredArea = 0f;
            CoveredTop.Clear();
            for (int i = 0; i < DrawRecords.Count; i++)
            {
                if (i == pi)
                    continue;
                DrawRecord r = DrawRecords[i];
                if (r.Plate)
                    continue;
                if (r.Order > plate.Order || (r.Order == plate.Order && r.Index > plate.Index))
                    continue; // drawn AFTER the plate: the plate cannot hide it
                if (!Intersect(plate.Rect, r.Rect, out Rect hit))
                    continue;
                float a = Mathf.Abs(hit.width * hit.height);
                covered++;
                coveredArea += a;
                if (r.Order == plate.Order && !ReferenceEquals(r.Canvas, plate.Canvas))
                    tied++;
                InsertCovered(a, r.Transform != null ? r.Transform.name : "?");
            }
            e.OverPaintCovered += covered;
            e.OverPaintCoveredArea += coveredArea;
            e.OverPaintTied += tied;
            if (covered > e.OverPaintWorstCovered)
                e.OverPaintWorstCovered = covered;

            if (p >= MaxPlatesReported)
                continue;
            if (OverPaintSb.Length > 0)
                OverPaintSb.Append(' ');
            OverPaintSb.Append('[').Append('#').Append(p + 1).Append(" '")
                .Append(plate.Transform != null ? plate.Transform.name : "?").Append("' at ")
                .Append(HostPath(plate.Transform, e.Panel.HostGo.transform)).Append(": rect ")
                .Append(plate.Rect.width.ToString("F0")).Append('x')
                .Append(plate.Rect.height.ToString("F0")).Append(" px = ")
                .Append((plate.Cover * 100f).ToString("F0"))
                .Append(" % of the open sub-view's area; COLOUR RGBA ")
                .Append(plate.Graphic != null ? plate.Graphic.color.r.ToString("F3") : "?").Append('/')
                .Append(plate.Graphic != null ? plate.Graphic.color.g.ToString("F3") : "?").Append('/')
                .Append(plate.Graphic != null ? plate.Graphic.color.b.ToString("F3") : "?").Append('/')
                .Append(ownAlpha.ToString("F3")).Append(" x crAlpha ").Append(crAlpha.ToString("F3"))
                .Append(" x inherited ").Append(inherited.ToString("F3"))
                .Append(opaque ? " = OPAQUE (it DELETES what it covers)"
                               : " = translucent (it TINTS what it covers)")
                .Append("; ").Append(PlateMaterialNote(plate.Graphic))
                .Append("; canvas '")
                .Append(plate.Canvas != null ? plate.Canvas.name : "none").Append("' sortingOrder ")
                .Append(plate.Canvas != null ? plate.Canvas.sortingOrder : 0)
                .Append(" overrideSorting ")
                .Append(plate.Canvas != null && plate.Canvas.overrideSorting ? "TRUE" : "false")
                .Append(", RESOLVED ORDER KEY (").Append(plate.Order).Append(", hierarchy index ")
                .Append(plate.Index).Append("); PAINTS OVER ").Append(covered).Append(" of ")
                .Append(e.OverPaintGraphics).Append(" drawing graphic(s)");
            if (covered == 0)
            {
                OverPaintSb.Append(" — NOTHING is drawn under it inside its own rect, i.e. it is a "
                                   + "LEGITIMATE BACKDROP and over-paint cannot be this view's fault");
            }
            else
            {
                OverPaintSb.Append(", ").Append(coveredArea.ToString("F0"))
                    .Append(" px² of intersecting area IN TOTAL (a SUM over the ").Append(covered)
                    .Append(", not a union — overlapping victims are counted once each), of which ")
                    .Append(tied)
                    .Append(" sit at the SAME resolved sortingOrder under a DIFFERENT canvas = an "
                            + "UNSTABLE TIE whose real GPU order this census cannot predict. THE ")
                    .Append(CoveredTop.Count).Append(" LARGEST (contributors, NOT the extent): ");
                for (int k = 0; k < CoveredTop.Count; k++)
                {
                    if (k > 0)
                        OverPaintSb.Append(", ");
                    OverPaintSb.Append('\'').Append(CoveredTop[k].Name).Append("' ")
                        .Append(CoveredTop[k].Area.ToString("F0")).Append(" px²");
                }
            }
            OverPaintSb.Append(']');
        }

        e.OverPaintNote = OverPaintSb.ToString();
        e.ForeignNote = ForeignSb.ToString();
        OverPaintSb.Length = 0;
        ForeignSb.Length = 0;
        DrawRecords.Clear();
        PlateRecords.Clear();
        CoveredTop.Clear();

        e.OverPaintScans++;
        e.OverPaintReadings++;
        e.OverPaintCoveredSum += e.OverPaintCovered;
        if (e.OverPaintReadings == 1)
        {
            e.OverPaintCoveredLowest = e.OverPaintCovered;
            e.OverPaintCoveredHighest = e.OverPaintCovered;
        }
        else
        {
            if (e.OverPaintCovered < e.OverPaintCoveredLowest)
                e.OverPaintCoveredLowest = e.OverPaintCovered;
            if (e.OverPaintCovered > e.OverPaintCoveredHighest)
                e.OverPaintCoveredHighest = e.OverPaintCovered;
        }
    }

    /// <summary>Keep the <see cref="MaxCoveredNamed"/> largest victims of one plate, by intersecting
    /// area. Insertion into a fixed tiny list — no sort, no allocation.</summary>
    private static void InsertCovered(float area, string name)
    {
        for (int i = 0; i < CoveredTop.Count; i++)
        {
            if (area > CoveredTop[i].Area)
            {
                CoveredTop.Insert(i, (area, name));
                if (CoveredTop.Count > MaxCoveredNamed)
                    CoveredTop.RemoveAt(CoveredTop.Count - 1);
                return;
            }
        }
        if (CoveredTop.Count < MaxCoveredNamed)
            CoveredTop.Add((area, name));
    }

    /// <summary>
    /// <b>WHAT THE PLATE'S MATERIAL IS — a specifically requested field, and nobody has ever read this
    /// shader name at runtime.</b>
    ///
    /// <para>The game ships <c>UIBlurDisabler</c> (<c>decompiled/GH.Runtime/UIBlurDisabler.cs:19</c>),
    /// whose entire remedy for these plates is <c>_image.material = null</c> — which PROVES the
    /// plate's appearance IS its material, not its colour and not its sprite. Its other branch is
    /// worth reading with this line in hand: <c>_color = new Color(17f, 17f, 17f, 85f)</c>, i.e. an
    /// UNCLAMPED colour whose alpha of 85 saturates to a fully opaque near-white plate, applied when
    /// <c>SimplifiedUI</c> and <c>DisableUIBlur</c> are both on. So the colour figures on this line
    /// are printed RAW and not clamped: a component above 1 is itself the finding.</para>
    ///
    /// <para><c>_image.material = null</c> makes <see cref="Graphic.material"/> return
    /// <c>defaultGraphicMaterial</c>, so "IS the default UI material" on this line is the same
    /// statement as "the blur disabler already ran (or was never needed) here". Anything else, with a
    /// <c>_GrabTexture</c>, <c>_BackgroundTexture</c> or <c>_CameraOpaqueTexture</c> property, is a
    /// shader that reads WHAT IS BEHIND IT — and a grab-pass source inside a render-to-texture capture
    /// is not the eye's framebuffer, which is a way for a plate to come out flat dark that no glyph,
    /// mesh or sampling instrument can see.</para>
    /// </summary>
    private static string PlateMaterialNote(Graphic? g)
    {
        if (g == null)
            return "material UNREADABLE (the graphic went away between the walk and the report)";
        Material mat;
        try
        {
            mat = g.material;
        }
        catch (System.Exception ex)
        {
            return $"material UNREADABLE ({ex.GetType().Name})";
        }
        if (mat == null)
            return "material NULL (nothing to draw with — uGUI falls back to the default UI material)";
        bool isDefault = ReferenceEquals(mat, Graphic.defaultGraphicMaterial);
        Shader? shader = mat.shader;
        string probes = isDefault
            ? string.Empty
            : ", probes ["
              + (mat.HasProperty(GrabTexId) ? "_GrabTexture YES" : "_GrabTexture no") + ", "
              + (mat.HasProperty(BackgroundTexId) ? "_BackgroundTexture YES" : "_BackgroundTexture no")
              + ", "
              + (mat.HasProperty(CameraOpaqueTexId) ? "_CameraOpaqueTexture YES"
                                                    : "_CameraOpaqueTexture no")
              + ", "
              + (!mat.HasProperty(MainTexId) ? "_MainTex ABSENT"
                  : mat.GetTexture(MainTexId) == null ? "_MainTex NULL" : "_MainTex bound")
              + "]";
        return (isDefault
                   ? "material IS the default UI material (i.e. no blur/grab shader here — the same "
                     + "state UIBlurDisabler produces with _image.material = null)"
                   : "material is NOT the default UI material")
               + ": shader '" + (shader != null ? shader.name : "?") + "', renderQueue "
               + mat.renderQueue + probes;
    }
}
