using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class CanvasConversion
{
    // ---- content fit (tests #13/#14) ------------------------------------------------------

    /// <summary>First fit attempt waits this long after Convert (window show animations run ~0.3 s).</summary>
    private const float FitDelaySeconds = 0.4f;

    /// <summary>If nothing visible was measurable by then, warn once and demote to periodic checks.</summary>
    private const float FitFirstWarnSeconds = 2.5f;

    /// <summary>Periodic growth re-check throttle (~0.4 s at 72 Hz) — the cheap dirty check.</summary>
    private const int FitCheckIntervalFrames = 30;

    /// <summary>Relative size/center change that triggers a re-fit (2 %).</summary>
    private const float FitChangeFraction = 0.02f;

    /// <summary>Minimum seconds between APPLIED shrink/re-center re-fits per host (test #17).</summary>
    private const float FitRefitMinIntervalSeconds = 1.5f;

    /// <summary>A shrink/re-center candidate must hold steady this long before it applies.</summary>
    private const float FitStableSeconds = 0.5f;

    // Scratch buffers for FitHostToContent (fit/periodic-check time only; reused, no
    // per-call allocations beyond one-time list growth).
    private static readonly List<Graphic> GraphicScratch = new(64);
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    /// <summary>
    /// Item 5 (pause-menu size consistency): consecutive fit checks the measured visible-content
    /// size must hold steady before a one-shot menu commits its single fit + lock. The window lays
    /// out over several frames after Show (children/spacers activate late, the fade/scale animation
    /// runs), so a fit taken on the FIRST measurable frame locked a different rect each open — the
    /// reported "taller on the 2nd+ open". Requiring the size to settle first makes every open land
    /// on the same fully laid-out visible-button bounds. At 72 Hz these run consecutively (the
    /// one-shot path re-checks every frame), so this is a fraction of a second.
    /// </summary>
    private const int OneShotSettleChecks = 6;

    /// <summary>
    /// FIRST-OPEN SIZE BUG (user report 2026-08-02: the pause menu is tiny/far away on the VERY
    /// FIRST open of a session, correct on every later open): how far BEFORE the reveal deadline
    /// (<see cref="RevealMaxWaitSeconds"/>) a still-unsettled first fit is committed anyway with
    /// the best measurement it has. WHY a lead and not the deadline itself: <c>Tick</c> runs
    /// <c>TickRevealGate</c> BEFORE <see cref="TickFit"/>, so a commit exactly AT the deadline
    /// would land one frame after the window was already shown — the visible re-fit jump the
    /// reveal gate exists to prevent. Committing slightly earlier keeps the 0.6 s reveal bound
    /// intact (a window can still never stay invisible) while guaranteeing the revealed rect is a
    /// FITTED one instead of the full 1920x1080 window frame.
    /// </summary>
    private const float FitForceCommitLeadSeconds = 0.05f;

    /// <summary>
    /// Consecutive checks of a STEADY measured bound after which the settle gate commits even
    /// though the forced layout flush still reports pending work (or the contributor count keeps
    /// flipping). WHY a second, longer bar instead of only the strict criterion: genuinely
    /// animated content — a story box typing its text, a pulsing prompt crossing the alpha floor —
    /// dirties layout or toggles a graphic every frame forever, and a strict-only gate would hold
    /// EVERY such window hidden until the reveal deadline on every open. ~0.25 s at 72 Hz: long
    /// enough that a half-built menu (which converges within a frame or two of the first flush)
    /// never reaches it, short enough to stay far inside the 0.6 s reveal bound.
    /// </summary>
    private const int SettleAnimatedFallbackChecks = 18;

    /// <summary>
    /// Effective-alpha floor for the content FIT measure: anything fainter than this is
    /// treated as invisible and neither sizes nor centers the panel (historic 0.05 value —
    /// unchanged, so fit geometry is identical to the shipped builds).
    /// </summary>
    private const float FitMinAlpha = 0.05f;

    /// <summary>
    /// Task #6 (pause window hard-cut behind the options menu): effective-alpha floor for
    /// DEPTH-MASK quad emission — deliberately HIGHER than <see cref="FitMinAlpha"/>. The
    /// hardware screenshot showed the parent pause window cut along one clean straight edge
    /// behind the options menu although the options window has NO visible content in that
    /// region: a barely-visible full-width element (a faint layout-container Image / the
    /// gradient title-banner strip, effective alpha just over 0.05) passed the shared 0.05
    /// test and emitted a WIDE depth quad, whose stamp made every later-drawn transparent —
    /// including the pause window's own canvas, which lies BEHIND the options plane below
    /// their intersection line — fail ZTest across the whole "empty" region (the WORLD still
    /// showed there because it draws before the mask, colour already in the buffer — exactly
    /// the observed sky-through-the-cut). A ≤15 %-opaque graphic reads as "nothing there",
    /// so it must not stamp depth either; genuinely visible content (rows, buttons, dialogs)
    /// is far above this floor and masks exactly as before, keeping the original purpose
    /// (HUD/initiative must not bleed through actual content) intact.
    /// </summary>
    internal const float MaskMinAlpha = 0.15f;

    /// <summary>
    /// Task #4/#5 shared per-graphic measure: the visibility test both unions use (enabled,
    /// not culled, effective alpha ≥ <paramref name="minAlpha"/>, non-degenerate draw rect)
    /// plus the graphic's host-local bounds, CLAMPED to its enclosing clipper's rect
    /// (<see cref="RectMask2D"/> / stencil <see cref="Mask"/> — i.e. a ScrollRect viewport):
    /// a settings row scrolled out of its viewport is CLIPPED at render time, so it must
    /// neither grow the content FIT nor stamp depth-mask coverage. False = the graphic
    /// contributes nothing (invisible, empty, or fully scrolled out). The alpha floor is
    /// caller-specific (task #6): <see cref="FitMinAlpha"/> for the content fit,
    /// <see cref="MaskMinAlpha"/> for depth-mask emission.
    /// </summary>
    private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g,
        out Vector2 gMin, out Vector2 gMax, float minAlpha = FitMinAlpha)
    {
        gMin = default;
        gMax = default;
        if (g == null || !g.enabled || g.canvasRenderer == null || g.canvasRenderer.cull)
            return false;
        // Effective alpha: own color × hierarchy (CanvasGroup) alpha.
        if (g.color.a * g.canvasRenderer.GetInheritedAlpha() < minAlpha)
            return false;

        var rect = (RectTransform)g.transform;
        // Zero draw size = nothing on screen (collapsed layout cells, empty
        // stretch containers with a Graphic) — must not anchor the union at
        // their corner points (test #16 measurement tightening).
        Rect drawRect = rect.rect;
        if (drawRect.width < 0.5f || drawRect.height < 0.5f)
            return false;

        rect.GetWorldCorners(CornerScratch);
        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);
        float maxZ = float.MinValue;
        for (int c = 0; c < 4; c++)
        {
            Vector3 local = panel.HostRect.InverseTransformPoint(CornerScratch[c]);
            if (local.x < min.x) min.x = local.x;
            if (local.y < min.y) min.y = local.y;
            if (local.x > max.x) max.x = local.x;
            if (local.y > max.y) max.y = local.y;
            if (local.z > maxZ) maxZ = local.z;
        }
        // Host-local +Z of the deepest corner (px; +Z = away from the viewer). Consumed by
        // CollectVisibleMaskRects → LastMaskMaxZ so the per-host depth mask can seat itself
        // BEHIND genuinely z-displaced content (the initiative row's authored recession) —
        // a mask in front of any content pixel would make that content fail its own ZTest.
        s_lastVisibleRectMaxZ = maxZ;

        // Task #4: clamp to the enclosing clipper (scroll viewport) — content the mask clips
        // away at render time must not count as visible.
        RectTransform? clipper = FindEnclosingClipper(panel, rect);
        if (clipper != null)
        {
            clipper.GetWorldCorners(CornerScratch);
            Vector3 ca = panel.HostRect.InverseTransformPoint(CornerScratch[0]);
            Vector3 cc = panel.HostRect.InverseTransformPoint(CornerScratch[2]);
            Vector2 clipMin = Vector2.Min(ca, cc);
            Vector2 clipMax = Vector2.Max(ca, cc);
            min = Vector2.Max(min, clipMin);
            max = Vector2.Min(max, clipMax);
            if (max.x - min.x < 0.5f || max.y - min.y < 0.5f)
                return false; // fully scrolled out of its viewport
        }

        gMin = min;
        gMax = max;
        return true;
    }

    /// <summary>
    /// How many graphics actually CONTRIBUTED to the last <see cref="TryMeasureContent"/> pass
    /// (passed <see cref="TryGetVisibleHostRect"/>). First-open size bug: the measured bounding
    /// box alone is a lossy settle signal — a partially laid-out menu can hold the same small box
    /// for several frames while its remaining rows are still zero-sized/culled. The contributor
    /// COUNT changes the moment one more element becomes real, so the settle gate compares it
    /// alongside size/center, and the fit log reports it (cold and warm opens must measure the
    /// same number of graphics).
    /// </summary>
    private static int s_lastMeasureGraphics;

    /// <summary>Per-pass memo (keyed by a graphic's immediate parent — siblings share one walk)
    /// for <see cref="FindEnclosingClipper"/>; cleared at the start of every measure pass.</summary>
    private static readonly Dictionary<Transform, RectTransform?> ClipperMemo = new(32);

    /// <summary>
    /// Task #4: nearest enclosing clipper of a graphic — an enabled <see cref="RectMask2D"/> or
    /// functioning stencil <see cref="Mask"/> on any ancestor up to (and including) the converted
    /// target. Null when nothing clips the graphic. Memoized per measure pass via
    /// <see cref="ClipperMemo"/>: the options list has dozens of row graphics under a handful of
    /// distinct parents, so the ancestor walk runs once per parent, not once per graphic.
    /// </summary>
    private static RectTransform? FindEnclosingClipper(ConvertedPanel panel, RectTransform rect)
    {
        Transform? parent = rect.parent;
        if (parent == null)
            return null;
        if (ClipperMemo.TryGetValue(parent, out RectTransform? memo))
            return memo;

        RectTransform? found = null;
        for (Transform? p = parent; p != null; p = p.parent)
        {
            var rm = p.GetComponent<RectMask2D>();
            if (rm != null && rm.enabled)
            {
                found = p as RectTransform;
                break;
            }
            var stencil = p.GetComponent<Mask>();
            if (stencil != null && stencil.enabled && stencil.graphic != null && stencil.graphic.enabled)
            {
                found = p as RectTransform;
                break;
            }
            if (ReferenceEquals(p, panel.Target) || ReferenceEquals(p, panel.HostRect))
                break; // never walk past the conversion root into the host/scene
        }
        ClipperMemo[parent] = found;
        return found;
    }

    /// <summary>
    /// Measure the visible-content size (padded, clamped to the target frame) and its center in
    /// host-local space, WITHOUT applying anything. False when nothing visible is measurable yet
    /// (still fading in) or the measured content is degenerate (mid scale-in). Shared by the
    /// per-frame fit (<see cref="FitHostToContent"/>) and the one-shot layout-settle gate
    /// (<see cref="SettleOneShotFit"/>) so both measure content the exact same way. Task #4:
    /// graphics are viewport-clamped (<see cref="TryGetVisibleHostRect"/>), so scrolling a list
    /// can never grow the union beyond the ScrollRect viewport — a scroll position change is
    /// size-neutral and can never trigger a re-fit.
    /// </summary>
    private static bool TryMeasureContent(ConvertedPanel panel, RectTransform root,
        out Vector2 size, out Vector2 center)
    {
        size = Vector2.zero;
        center = Vector2.zero;

        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);
        int contributing = 0;

        ClipperMemo.Clear();
        GraphicScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            if (!TryGetVisibleHostRect(panel, GraphicScratch[i], out Vector2 gMin, out Vector2 gMax))
                continue;
            min = Vector2.Min(min, gMin);
            max = Vector2.Max(max, gMax);
            contributing++;
        }
        GraphicScratch.Clear();
        s_lastMeasureGraphics = contributing;

        if (contributing == 0)
            return false; // nothing visible yet (fade-in) — caller retries

        // Clamp into the TARGET's own frame (in host-local space, via its world
        // corners so live show-animation scale is honored): off-screen/overflow
        // elements must not grow the panel beyond the window's own rect, but a
        // previously shrunk host must not cap a legitimate content growth.
        // EXCEPT for degenerate targets (test #16): a zero-size layout container
        // has no real frame — the 100 px Convert placeholder would crop the union
        // to a corner of the visibly overflowing content (objectives text). There
        // the union of visible graphics IS the frame.
        // An unreal frame clamps to nothing: infinite extents make both clamps
        // below natural no-ops without a second code path.
        Vector2 frameMin = new(float.MinValue, float.MinValue);
        Vector2 frameMax = new(float.MaxValue, float.MaxValue);
        if (!panel.FitFrameDegenerate)
        {
            panel.Target.GetWorldCorners(CornerScratch);
            Vector3 frameA = panel.HostRect.InverseTransformPoint(CornerScratch[0]);
            Vector3 frameB = panel.HostRect.InverseTransformPoint(CornerScratch[2]);
            // Min/max-normalized: a mid-animation rotation/negative scale must not
            // invert the frame and turn the clamp into garbage.
            frameMin = Vector2.Min(frameA, frameB);
            frameMax = Vector2.Max(frameA, frameB);
            min = Vector2.Max(min, frameMin);
            max = Vector2.Min(max, frameMax);
        }

        Vector2 sz = max - min;
        if (sz.x < 32f || sz.y < 32f)
            return false; // degenerate (mid scale-in animation) — caller retries

        const float Padding = 12f;
        sz += Vector2.one * (2f * Padding);
        sz.x = Mathf.Min(sz.x, frameMax.x - frameMin.x);
        sz.y = Mathf.Min(sz.y, frameMax.y - frameMin.y);

        size = sz;
        center = (min + max) * 0.5f;
        return true;
    }

    /// <summary>
    /// Task #5 (transparent gaps must stay transparent for OTHER MENUS too): collect ONE
    /// host-local rect PER visible graphic — <c>Vector4(minX, minY, maxX, maxY)</c> in
    /// host-local pixels — with the exact same visibility test the content fit uses
    /// (<see cref="TryGetVisibleHostRect"/>: enabled, not culled, effective alpha ≥ 0.05,
    /// non-degenerate draw rect, viewport-clamped per task #4). Replaces the old single
    /// union rect (<c>TryMeasureVisibleUnion</c>): the union stamped menu-plane depth
    /// across the GAPS between settings rows, which the world showed through (drawn
    /// earlier, colour already in the buffer) but other transparent menus did NOT (drawn
    /// later, depth-tested against the stamp). <see cref="GrabbableModal.SyncDepthMask"/>
    /// builds a per-graphic quad mesh from these rects, so depth is stamped only where
    /// content (approximately — its rect) actually renders and the gaps stay open for
    /// everything behind, menus included. Unclamped to the target frame on purpose: an
    /// open dropdown list may extend past it and the mask should back it wherever it draws.
    /// Rects beyond <paramref name="maxCount"/> are merged into the last slot (coverage is
    /// never lost, only gap fidelity in the overflow). Returns the rect count (0 = nothing
    /// visible; caller disables the mask). Reuses the fit scratch buffers (single-threaded,
    /// never re-entered).
    ///
    /// Task #6 (pause window hard-cut): emission uses the STRICTER <see cref="MaskMinAlpha"/>
    /// floor (0.15) instead of the fit's 0.05 — a barely-visible full-width container must not
    /// stamp a depth quad that hard-cuts other floated menus behind the plane (see the const's
    /// doc). <paramref name="sources"/> (optional) receives the emitting <see cref="Graphic"/>
    /// per rect, 1:1 with <paramref name="rects"/> (null entry = the overflow union slot) — the
    /// depth-mask rebuild diagnostic uses it to NAME wide/suspect quads in the hardware log.
    ///
    /// Task #6b (options window hard-cuts the pause menu — CULPRIT: 'Main Area/Viewport'):
    /// graphics that RENDER NO PIXELS must not stamp depth either. The hardware diag showed the
    /// options window's ScrollRect VIEWPORT image ('Main Area/Viewport', Image, sprite=null,
    /// a=1.00) emitting an 867x833 px quad covering the whole right pane — the viewport is an
    /// invisible clipper/raycast target (its Image drives a stencil <see cref="Mask"/> with
    /// <c>showMaskGraphic=false</c>, i.e. it draws ONLY to the stencil buffer, ColorMask 0 —
    /// zero visible pixels), yet it passed the alpha test and its depth stamp hard-cut the pause
    /// menu floating behind along one clean edge. <see cref="IsNonRenderingMaskEmitter"/>
    /// excludes that whole class from EMISSION ONLY (the content fit is untouched); excluded
    /// names + the matched rule land in <see cref="LastMaskExclusions"/> for the rebuild diag.
    /// </summary>
    internal static int CollectVisibleMaskRects(ConvertedPanel panel, List<Vector4> rects, int maxCount,
        List<Graphic?>? sources = null)
    {
        rects.Clear();
        sources?.Clear();
        LastMaskExclusions.Clear();
        LastMaskMaxZ = 0f;
        if (panel == null || panel.Target == null || panel.HostRect == null)
            return 0;

        ClipperMemo.Clear();
        GraphicScratch.Clear();
        panel.Target.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (!TryGetVisibleHostRect(panel, g, out Vector2 gMin, out Vector2 gMax, MaskMinAlpha))
                continue;
            // Task #6b: a graphic that renders no pixels (invisible clipper / viewport /
            // raycast catcher) must not stamp depth. Checked only AFTER the (cheap) visibility
            // test passed, so the component lookups run for the ~dozens of emitting graphics,
            // not the whole subtree.
            if (IsNonRenderingMaskEmitter(g, out string rule))
            {
                if (LastMaskExclusions.Count < MaskExclusionLogCap)
                {
                    string parent = g.transform.parent != null ? g.transform.parent.name : "<root>";
                    LastMaskExclusions.Add(
                        $"'{parent}/{g.name}' {gMax.x - gMin.x:F0}x{gMax.y - gMin.y:F0}px [{rule}]");
                }
                continue;
            }
            if (s_lastVisibleRectMaxZ > LastMaskMaxZ)
                LastMaskMaxZ = s_lastVisibleRectMaxZ; // deepest EMITTED graphic (see the field doc)
            if (rects.Count < maxCount)
            {
                rects.Add(new Vector4(gMin.x, gMin.y, gMax.x, gMax.y));
                sources?.Add(g);
            }
            else
            {
                // Cap reached: widen the last slot to the union of the overflow — coverage
                // stays correct, only the per-rect gap fidelity degrades past the cap.
                Vector4 last = rects[rects.Count - 1];
                rects[rects.Count - 1] = new Vector4(
                    Mathf.Min(last.x, gMin.x), Mathf.Min(last.y, gMin.y),
                    Mathf.Max(last.z, gMax.x), Mathf.Max(last.w, gMax.y));
                if (sources != null)
                    sources[sources.Count - 1] = null; // slot is now an anonymous overflow union
            }
        }
        GraphicScratch.Clear();
        return rects.Count;
    }

    /// <summary>Task #6b diag: cap on excluded-emitter entries kept per collection pass (log hygiene).</summary>
    private const int MaskExclusionLogCap = 8;

    /// <summary>Scratch: host-local max +Z (px) of the corners measured by the LAST
    /// <see cref="TryGetVisibleHostRect"/> call (set on success only).</summary>
    private static float s_lastVisibleRectMaxZ;

    /// <summary>
    /// Host-local +Z (px, ≥0) of the DEEPEST graphic emitted by the last
    /// <see cref="CollectVisibleMaskRects"/> pass. The per-host depth-compose mask
    /// (<see cref="TickHostDepthMask"/>) seats itself this far behind the host plane plus a
    /// fixed pad, so content the game (or the initiative depth normalization) genuinely
    /// z-displaces — portraits recede up to <c>[WorldUI] InitiativeDepthMaxSpreadPx</c> px —
    /// can never end up BEHIND its own panel's mask and fail its own ZTest. Flat panels
    /// measure ~0 and get the tight minimum offset.
    /// </summary>
    internal static float LastMaskMaxZ;

    /// <summary>
    /// Task #6b diag: graphics EXCLUDED from depth-mask emission by
    /// <see cref="IsNonRenderingMaskEmitter"/> during the LAST
    /// <see cref="CollectVisibleMaskRects"/> pass — "'parent/name' WxHpx [rule]" per entry,
    /// capped at <see cref="MaskExclusionLogCap"/>. Read by the depth-mask rebuild diagnostic
    /// (<c>GrabbableModal.LogDepthMaskRebuild</c>, same tick, same collection) so the hardware
    /// log states WHICH rule caught each invisible emitter ('Main Area/Viewport' &amp; friends).
    /// </summary>
    internal static readonly List<string> LastMaskExclusions = new(MaskExclusionLogCap);

    /// <summary>
    /// Task #6b: true when <paramref name="g"/> renders NO pixels despite passing the
    /// alpha/enabled visibility test — such a graphic must never stamp a depth-mask quad
    /// (it visually reads as "nothing there", so cutting another floated menu behind the
    /// plane along its rect is exactly the observed hard-cut bug). Three classes, checked
    /// in order; <paramref name="rule"/> names the one that matched:
    ///
    /// (a) STENCIL-CLIPPER IMAGE: the graphic drives an enabled stencil <see cref="Mask"/>
    ///     with <c>showMaskGraphic == false</c> — uGUI then renders it with ColorMask 0
    ///     (stencil write only), i.e. literally zero visible pixels. This is what the
    ///     options window's 'Main Area/Viewport' (867x833 px, sprite=null, a=1.00) and the
    ///     ESC menu's 'Scroll View/Viewport' (388x1003) are: ScrollRect viewport clippers.
    ///     A Mask WITH <c>showMaskGraphic == true</c> draws its graphic normally and is
    ///     deliberately NOT excluded.
    /// (b) SCROLLRECT VIEWPORT: the rect IS some ScrollRect's viewport (the serialized
    ///     <c>.viewport</c>, or the content's parent when that reference is empty) — the
    ///     clipping window itself, an invisible frame/raycast surface, never visible
    ///     content. Belt-and-braces for viewports clipped via <see cref="RectMask2D"/>
    ///     (including the ones task #4 adds ours to), whose Image is a raycast catcher.
    /// (c) INVISIBLE RAYCAST CATCHER: sprite-less Image in the default (~white) colour —
    ///     the classic full-area click-catcher pattern. Real visible backings in this UI
    ///     all carry sprites ('Panel', 'Panel_Divider', 'Mod_Frame', …) and tinted colours,
    ///     so a sprite-null near-white Image is a hit surface, not content.
    ///
    /// TMP text / RawImage / sprited Images fall through — they are real content and keep
    /// masking exactly as before ('UI Menu Panel' sprite='Panel', the row 'Background'
    /// Panel_Divider strips, buttons, dialogs).
    /// </summary>
    private static bool IsNonRenderingMaskEmitter(Graphic g, out string rule)
    {
        rule = string.Empty;
        if (g is not Image img)
            return false;

        // (a) stencil clipper: Mask with the graphic hidden → stencil-only draw (ColorMask 0).
        Mask stencil = img.GetComponent<Mask>();
        if (stencil != null && stencil.enabled && !stencil.showMaskGraphic)
        {
            rule = "Mask, showMaskGraphic=false: stencil-only, draws no pixels";
            return true;
        }

        // (b) ScrollRect viewport: the clipping window rect itself.
        var rect = (RectTransform)img.transform;
        ScrollRect? owner = img.GetComponentInParent<ScrollRect>();
        if (owner != null)
        {
            RectTransform? viewport = owner.viewport != null
                ? owner.viewport
                : owner.content != null ? owner.content.parent as RectTransform : null;
            if (ReferenceEquals(viewport, rect))
            {
                rule = "ScrollRect viewport: invisible clipper/raycast frame";
                return true;
            }
        }

        // (c) classic invisible raycast catcher: sprite-less, default-white Image.
        if (img.sprite == null && img.overrideSprite == null
            && img.color.r >= 0.95f && img.color.g >= 0.95f && img.color.b >= 0.95f)
        {
            rule = "sprite=null near-white: raycast catcher";
            return true;
        }
        return false;
    }

    /// <summary>
    /// Tests #13/#14: size a converted panel's HOST rect to the target's actual
    /// VISIBLE content bounds. Game windows often convert with a full-screen stretch
    /// root (story window 1920x1080 around a small strip; initiative track likewise)
    /// — the host rect is what the laser (RayUguiDriver) and poke plane intersect,
    /// so a full-window host registers a huge invisible plane that shadows the scene.
    ///
    /// Bounds = union of all enabled child <see cref="Graphic"/>s under
    /// <paramref name="contentRoot"/> (or the whole target) whose effective alpha is
    /// visible — invisible click-catchers (the story box's full-area alpha-0 skip
    /// button) stay clickable (GraphicRaycaster raycasts per-graphic, not per-host-
    /// rect) but no longer size the panel. Zero-draw-size graphics (collapsed
    /// layout cells) are skipped too (test #16). The union is clamped to the
    /// TARGET's own frame (not the current — possibly already shrunk — host rect),
    /// so a later content GROWTH (multi-page story, log lines) re-expands the host
    /// up to the window's original rect (test #14: one-shot fits under-covered
    /// later pages) — unless the target converted with a degenerate rect
    /// (<see cref="ConvertedPanel.FitFrameDegenerate"/>): overflowing content
    /// bounds then stand on their own (test #16: the zero-size objectives
    /// container must measure its full text, not the 100 px placeholder).
    ///
    /// The target is shifted so the content bound is centered on the host pose;
    /// Release() still restores the exact 2D home (originals captured at Convert).
    ///
    /// No-op within <see cref="FitChangeFraction"/> (2 %) of the current host rect —
    /// this doubles as the cheap dirty check for the periodic re-fit.
    ///
    /// Returns false when no visible content was measurable yet (e.g. the window is
    /// still fading in) — the caller retries later. Returns true once the host
    /// matches the visible content (fitted now or already within tolerance).
    /// </summary>
    internal static bool FitHostToContent(ConvertedPanel panel, RectTransform? contentRoot = null)
    {
        if (panel == null || panel.Target == null || panel.HostRect == null)
            return true; // nothing to do, do not retry

        RectTransform root = ResolveFitRoot(panel, contentRoot);

        if (!TryMeasureContent(panel, root, out Vector2 size, out Vector2 center))
            return false; // nothing visible / degenerate yet (fade-in) — caller retries

        // Dirty check (test #14): within 2 % of the current host rect (size AND
        // centering) — nothing to do. Host pivot is centered, so local origin ==
        // rect center and |center| is the content's off-center error directly.
        Rect host = panel.HostRect.rect;
        float tolX = Mathf.Max(host.width, size.x) * FitChangeFraction;
        float tolY = Mathf.Max(host.height, size.y) * FitChangeFraction;
        if (Mathf.Abs(size.x - host.width) <= tolX && Mathf.Abs(size.y - host.height) <= tolY
            && Mathf.Abs(center.x) <= tolX && Mathf.Abs(center.y) <= tolY)
            return true;

        // Re-fit churn damping (test #17): 'Panel_CombatLog' oscillated 569x138 ↔
        // 569x291 twice a second for minutes (log entries fade in and out) —
        // hundreds of re-fits and log lines. GROWTH beyond the current host bounds
        // still fast-paths (content must never sit clipped behind the damping), but
        // a pure shrink/re-center applies only when the measured candidate held
        // steady for FitStableSeconds AND the last applied fit is at least
        // FitRefitMinIntervalSeconds old. Oscillating content keeps resetting the
        // stability clock and the host simply stays at its largest recent extent.
        // The very first fit (FitMeasuredOnce false) is never damped.
        bool growth = size.x > host.width + tolX || size.y > host.height + tolY;
        if (panel.FitMeasuredOnce && !growth)
        {
            float now = Time.unscaledTime;
            bool sameCandidate = Mathf.Abs(size.x - panel.FitPendingSize.x) <= tolX
                                 && Mathf.Abs(size.y - panel.FitPendingSize.y) <= tolY;
            if (!sameCandidate)
            {
                panel.FitPendingSize = size;
                panel.FitPendingSince = now;
                return true; // measured fine — just deferred
            }
            if (now - panel.FitPendingSince < FitStableSeconds
                || now - panel.FitLastApplied < FitRefitMinIntervalSeconds)
                return true;
        }
        panel.FitPendingSize = Vector2.zero;
        panel.FitLastApplied = Time.unscaledTime;

        // Shifting the target by -center puts the content bound in the middle of
        // the resized host; the registered laser/poke plane now equals what the
        // user SEES (verify via the RayUguiDriver world-rect re-log lines).
        panel.Target.anchoredPosition -= center;
        panel.HostRect.sizeDelta = size;
        panel.FitOneShotApplied = true; // item 1: a real resize happened — owner may re-derive its scale
        VRLog.Info("WorldUI", $"Host rect fit '{panel.HostGo.name}': " +
                              $"{host.width:F0}x{host.height:F0} → {size.x:F0}x{size.y:F0} px " +
                              $"(content offset {center.x:F0},{center.y:F0}).");
        return true;
    }

    /// <summary>
    /// Central fit driver (test #14 item 1), called from <see cref="Tick"/>: first
    /// fit after <see cref="FitDelaySeconds"/> (retried per frame through the show
    /// animation, warn once at <see cref="FitFirstDeadline"/>), then a THROTTLED
    /// periodic re-check every ~<see cref="FitCheckIntervalFrames"/> frames per
    /// panel so content GROWTH (story pages, log lines) re-fits the host. Steady
    /// state cost: one Graphic-union scan per panel per 30 frames; the 2 % no-op
    /// threshold inside <see cref="FitHostToContent"/> is the dirty check, and
    /// shrink/re-center re-fits are additionally damped there (test #17 churn).
    /// </summary>
    private static void TickFit(ConvertedPanel panel)
    {
        if (!panel.FitEnabled)
            return;
        // User ruling 2026-08-02 (post-reveal jump): while the host is still render-hidden
        // behind the reveal gate, the first fit must NOT sit out the FitDelaySeconds grace.
        // That delay is what made every non-one-shot window reveal at its full pre-fit rect
        // (reveal at ~0.15 s, first fit not before 0.4 s — hardware log ModBuild 15: MODAL
        // REVEAL consistently BEFORE 'Host rect fit 1920x1080 → …'): the fit then visibly
        // shrank the frame and re-centered the content ~0.25–0.85 s AFTER the window was
        // already on screen — the reported "larger and lower, then snaps". The reveal gate
        // waits for this fit now, so it must run immediately; VISIBLE hosts (HUD conversions,
        // post-reveal re-checks) keep the historic delay untouched.
        if (Time.unscaledTime < panel.FitNotBefore && !panel.RevealPending)
            return;

        // Item 5 (pause-menu size consistency): a one-shot full-screen menu must land the SAME
        // compact size on EVERY open. Defer its single fit until the window's layout has SETTLED
        // (deterministic rebuild + a stable measured rect across N checks) instead of committing on
        // the first measurable frame, which locked a different rect each open.
        if (panel.FitOneShot && !panel.FitOneShotApplied)
        {
            SettleOneShotFit(panel);
            return;
        }

        // User ruling 2026-08-02: a render-hidden host's FIRST fit runs through the same
        // layout-settle machinery as the one-shot menus (forced synchronous rebuild + N stable
        // measures) instead of committing on the first measurable frame — a fit taken mid
        // show-animation would land a wrong rect that the periodic growth re-check then
        // visibly corrects AFTER reveal, re-creating the jump the reveal gate now prevents.
        if (panel.RevealPending && !panel.FitMeasuredOnce)
        {
            SettlePreRevealFirstFit(panel);
            return;
        }

        if (panel.FitMeasuredOnce && Time.frameCount < panel.FitNextCheckFrame)
            return;

        if (FitHostToContent(panel, panel.FitContentRoot))
        {
            panel.FitMeasuredOnce = true;
            panel.FitNextCheckFrame = Time.frameCount + FitCheckIntervalFrames;
        }
        else if (!panel.FitMeasuredOnce && Time.unscaledTime >= panel.FitFirstDeadline)
        {
            if (!panel.FitGaveUpLogged)
            {
                panel.FitGaveUpLogged = true;
                VRLog.Warn("WorldUI", $"Content fit: nothing visible in '{panel.HostGo.name}' after " +
                                      $"{FitFirstWarnSeconds:F1}s — keeping the full root rect and " +
                                      "re-checking periodically.");
            }
            panel.FitMeasuredOnce = true; // demote to the periodic check
            panel.FitNextCheckFrame = Time.frameCount + FitCheckIntervalFrames;
        }
        // else: not yet measurable and before the deadline — retry next frame.
    }

    /// <summary>
    /// Item 5 (pause-menu size consistency — "it must ALWAYS look like the first time"): drive a
    /// one-shot menu's single content fit only AFTER its layout has settled. Root cause of the
    /// "taller on 2nd+ open": the game populates/animates the window over several frames after Show
    /// (children activate late, fade/scale runs), so the OLD one-shot committed on the first
    /// measurable frame — a warm re-open measured MORE laid-out content than a cold first open and
    /// locked a taller rect. Fix: every frame force a deterministic layout rebuild, measure the
    /// visible content, and only commit the single fit + lock once that measurement has held steady
    /// for <see cref="OneShotSettleChecks"/> consecutive checks (or the first-warn deadline forces
    /// it, so an animated/unmeasurable menu still opens). The measure matches the final laid-out
    /// tree, identical every open. The reveal stays gated on <see cref="ConvertedPanel.FitOneShotApplied"/>,
    /// so the menu pops in already at its stable compact size — never flashing the full rect.
    ///
    /// FIRST-OPEN SIZE BUG (user report 2026-08-02, "tiny/very far away on the VERY FIRST open"):
    /// stability alone was not enough. A half-built menu is perfectly STABLE — its unbuilt rows
    /// are zero-sized or culled, so they contribute nothing and nothing moves for as many frames
    /// as you care to count — and the cold open latched exactly that (259x294 px versus 396x1080
    /// warm). The gate is content-aware now (<see cref="TickSettleGate"/>): the commit also
    /// requires a forced layout flush to produce NO further change, and the flush itself was
    /// re-ordered (<see cref="FlushPendingLayout"/>) so the measure no longer reads culling and
    /// clipping computed from the PRE-rebuild geometry.
    /// </summary>
    private static void SettleOneShotFit(ConvertedPanel panel)
    {
        RectTransform? contentRoot = panel.FitContentRoot;
        RectTransform root = ResolveFitRoot(panel, contentRoot);

        SettleResult state = TickSettleGate(panel, root, out string report);
        bool giveUp = Time.unscaledTime >= panel.FitFirstDeadline;
        bool revealDue = ForceCommitDue(panel);

        if (state == SettleResult.NotMeasurable)
        {
            // Nothing visible yet — keep retrying until the deadline, then give up to the full rect
            // (the reveal's deadline floor still pops the menu in, never an invisible one).
            if (giveUp && !panel.FitGaveUpLogged)
            {
                panel.FitGaveUpLogged = true;
                panel.FitEnabled = false;
                panel.FitMeasuredOnce = true;
                VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' one-shot fit found nothing " +
                                      $"measurable after {FitFirstWarnSeconds:F1}s — keeping the full rect " +
                                      $"({panel.FitSettleChecks} settle check(s)).");
            }
            return;
        }

        if (state != SettleResult.Settled && !giveUp && !revealDue)
            return; // still settling — keep measuring (render-hidden, so nothing visibly moves)

        // Settled (or forced): commit the single fit and LOCK. FitMeasuredOnce is still false here,
        // so FitHostToContent applies the now-stable size immediately (no shrink damping).
        FitHostToContent(panel, contentRoot);
        panel.FitMeasuredOnce = true;
        panel.FitNextCheckFrame = Time.frameCount + FitCheckIntervalFrames;
        panel.FitEnabled = false; // one-shot: freeze the rect (no per-frame re-fit flicker)
        if (state == SettleResult.Settled)
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' full-screen menu fitted ONCE after its " +
                                  $"layout settled — {report} — host rect locked (same compact size every " +
                                  "open, cold or warm, no re-fit flicker).");
        else
            VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' full-screen menu fit FORCED before its " +
                                  $"layout settled ({(revealDue ? "reveal deadline" : "first-fit deadline")}) — " +
                                  $"{report} — host rect locked at the best measurement available; the first " +
                                  "open may differ from later ones (report this line).");
    }

    /// <summary>
    /// User ruling 2026-08-02 ("the window must appear directly at the right spot"): settled
    /// FIRST fit for a render-hidden (reveal-pending) NON-one-shot host — story box, level
    /// messages (both chain rules), results, catch-all floats. WHY: these hosts convert at the
    /// full captured window rect (typically 1920x1080) with the visible content OFF-CENTER
    /// inside it; the reveal gate now waits for the first fit, and that fit must (a) run
    /// immediately instead of after <see cref="FitDelaySeconds"/>, and (b) commit a rect the
    /// periodic re-check will not immediately correct — so it uses the exact machinery item 5
    /// proved out for the one-shot menus: flush pending layout synchronously (the TMP/uGUI
    /// reflow that used to complete frames after reveal is forced NOW, making the fit inputs
    /// final), measure, and commit only once the measured size held steady for
    /// <see cref="OneShotSettleChecks"/> consecutive frames. Unlike the one-shot path the fit
    /// is NOT locked afterwards: story/message content genuinely grows later (multi-page text,
    /// log lines) and must keep re-fitting through the normal periodic growth check. Unmeasurable
    /// content (still fading in) simply retries next frame — the reveal DEADLINE caps the total
    /// hidden time, after which <see cref="TickFit"/> falls back to the historic first-fit path.
    ///
    /// FIRST-OPEN SIZE BUG (2026-08-02): this path shares the defect the ESC menu exposed — it ran
    /// the same "canvas update, then layout rebuild" flush and the same size-only stability gate —
    /// so it is fixed the same way, through the shared <see cref="TickSettleGate"/>. A story box or
    /// level message opened for the FIRST time in a session now waits for the same proof that its
    /// layout has nothing left to apply before its rect is committed.
    /// </summary>
    private static void SettlePreRevealFirstFit(ConvertedPanel panel)
    {
        RectTransform? contentRoot = panel.FitContentRoot;
        RectTransform root = ResolveFitRoot(panel, contentRoot);

        SettleResult state = TickSettleGate(panel, root, out string report);
        if (state == SettleResult.NotMeasurable)
            return; // nothing visible yet (fade-in) — retry next frame, bounded by the reveal deadline

        bool revealDue = ForceCommitDue(panel);
        if (state != SettleResult.Settled && !revealDue)
            return; // still settling — keep measuring (render-hidden, so nothing visibly moves)

        // Stable (or forced just before the reveal deadline): commit the FIRST fit (undamped —
        // FitMeasuredOnce is still false) and hand the host to the normal periodic growth re-check.
        // The reveal gate sees FitMeasuredOnce and — once the host pose also held still — pops the
        // window in already at this final rect.
        FitHostToContent(panel, contentRoot);
        panel.FitMeasuredOnce = true;
        panel.FitNextCheckFrame = Time.frameCount + FitCheckIntervalFrames;
        if (state == SettleResult.Settled)
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' pre-reveal first fit committed after its " +
                                  $"layout settled — {report} — the window reveals at this rect (later growth " +
                                  "still re-fits through the periodic check).");
        else
            VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' pre-reveal first fit FORCED at the reveal " +
                                  $"deadline before its layout settled — {report} — revealing at the best " +
                                  "measurement available (report this line).");
    }

    /// <summary>
    /// Shared measure root resolution: the caller's narrower content root when it is usable
    /// (active and genuinely inside the converted subtree), else the conversion target itself.
    /// Extracted so the per-frame fit and BOTH settle paths can never drift apart in what they
    /// measure — the whole point of the settle gate is that the committing measure is the same
    /// measure that was proven stable.
    /// </summary>
    private static RectTransform ResolveFitRoot(ConvertedPanel panel, RectTransform? contentRoot) =>
        contentRoot != null && contentRoot.gameObject.activeInHierarchy
        && contentRoot.IsChildOf(panel.Target)
            ? contentRoot
            : panel.Target;

    /// <summary>
    /// Flush EVERY pending uGUI rebuild of a converting panel — LAYOUT FIRST, canvas update
    /// SECOND. THE ORDER IS THE FIRST-OPEN BUG (user report 2026-08-02: pause menu tiny on the
    /// very first open of a session, correct on every later open):
    ///
    /// <see cref="Canvas.ForceUpdateCanvases"/> is what drives <c>CanvasUpdateRegistry</c> — the
    /// graphic rebuilds AND the clipping pass (<see cref="RectMask2D"/>/<see cref="Mask"/> →
    /// <c>CanvasRenderer.cull</c> + the clip rects). The content measure reads exactly those
    /// results: <see cref="TryGetVisibleHostRect"/> SKIPS any graphic whose canvasRenderer is
    /// culled and CLAMPS the rest to their clipper's rect. Running the canvas update BEFORE the
    /// layout rebuild (the old order) therefore evaluated culling/clipping against the PRE-rebuild
    /// geometry, and every rect the rebuild then moved or resized carried a stale cull flag and a
    /// stale clip rect into the measure.
    ///
    /// On a WARM re-open that is harmless: the subtree still carries the settled rects from the
    /// previous open, the rebuild changes nothing, and the stale state is already the correct
    /// state. On a COLD first open the game's layout for this window has NEVER run (proof in the
    /// hardware log: the ESC menu's own root rect converts at 1080 px on the first open and at
    /// 2040 px — "height capped 2040->1080" — on every later one), so the rebuild moves and
    /// resizes nearly everything, the whole measure ran against stale culling, and the fit locked
    /// a small mis-centered box (259x294 px at offset -486,468 versus 396x1080 at -774,0 warm).
    /// Rebuilding first and updating the canvases afterwards makes the measure read culling and
    /// clipping that belong to the geometry it is measuring.
    /// </summary>
    private static void FlushPendingLayout(ConvertedPanel panel)
    {
        if (panel.Target == null)
            return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Target);
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>Outcome of one <see cref="TickSettleGate"/> check.</summary>
    private enum SettleResult
    {
        /// <summary>Nothing visible/measurable yet (still fading in, still zero-sized).</summary>
        NotMeasurable,

        /// <summary>Measurable, but the content is still changing — do not commit yet.</summary>
        Settling,

        /// <summary>A forced rebuild changes nothing and the measurement held steady — commit.</summary>
        Settled,
    }

    /// <summary>
    /// Measure the content the way the fit will, and REPORT whether a forced layout flush changes
    /// that measurement. This is the content-aware settle criterion that replaced the old
    /// time/stability-only gate: "the numbers did not move for N frames" is satisfiable by a
    /// half-built menu (its remaining rows are zero-sized or culled, so they contribute nothing
    /// and nothing moves), whereas "a forced rebuild + canvas update produces the SAME
    /// measurement" can only be true once the layout genuinely has nothing left to do.
    /// <paramref name="layoutWasDirty"/> is that answer; it also covers the contributor COUNT, so
    /// one more element becoming real is caught even when the bounding box happens not to grow.
    /// </summary>
    private static bool TryMeasureSettled(ConvertedPanel panel, RectTransform root,
        out Vector2 size, out Vector2 center, out int graphics, out bool layoutWasDirty)
    {
        bool preOk = TryMeasureContent(panel, root, out Vector2 preSize, out Vector2 preCenter);
        int preGraphics = s_lastMeasureGraphics;

        FlushPendingLayout(panel);

        bool postOk = TryMeasureContent(panel, root, out size, out center);
        graphics = s_lastMeasureGraphics;

        layoutWasDirty = preOk != postOk || preGraphics != graphics
                         || (postOk && !MeasureMatches(preSize, preCenter, size, center));
        return postOk;
    }

    /// <summary>
    /// Two content measurements are the same within the fit's own <see cref="FitChangeFraction"/>
    /// tolerance (never below 1 px, so float noise on a large rect cannot masquerade as a change).
    /// Compares the CENTER as well as the size: a partially laid-out menu can keep its box size
    /// while the column slides sideways (measured on hardware: the cold first open sat 288 px to
    /// the right of the warm one), and a fit committed there is mis-centered even when its size
    /// looks right.
    /// </summary>
    private static bool MeasureMatches(Vector2 aSize, Vector2 aCenter, Vector2 bSize, Vector2 bCenter)
    {
        float tolX = Mathf.Max(Mathf.Max(aSize.x, bSize.x) * FitChangeFraction, 1f);
        float tolY = Mathf.Max(Mathf.Max(aSize.y, bSize.y) * FitChangeFraction, 1f);
        return Mathf.Abs(aSize.x - bSize.x) <= tolX && Mathf.Abs(aSize.y - bSize.y) <= tolY
               && Mathf.Abs(aCenter.x - bCenter.x) <= tolX && Mathf.Abs(aCenter.y - bCenter.y) <= tolY;
    }

    /// <summary>
    /// One settle check, shared by the one-shot menu fit and the pre-reveal first fit (they are
    /// mutually exclusive per panel, so they share the settle scratch fields). Commits nothing —
    /// it only advances the gate and hands the caller a log-ready <paramref name="report"/>.
    ///
    /// A check is SETTLED when both hold:
    ///  1. the forced layout flush (<see cref="FlushPendingLayout"/>) did not change the
    ///     measurement — deterministic proof that the layout has nothing left to apply, which is
    ///     what a cold first open lacks and a warm re-open has from the start; and
    ///  2. the measurement (size, center AND contributor count) has held steady for
    ///     <see cref="OneShotSettleChecks"/> consecutive checks — the historic bound against
    ///     committing mid show-animation, unchanged.
    /// </summary>
    private static SettleResult TickSettleGate(ConvertedPanel panel, RectTransform root, out string report)
    {
        report = string.Empty;
        panel.FitSettleChecks++;

        if (!TryMeasureSettled(panel, root, out Vector2 size, out Vector2 center,
                out int graphics, out bool layoutWasDirty))
        {
            if (layoutWasDirty)
                panel.FitSettleRebuildChanges++;
            panel.FitOneShotStableCount = 0;
            panel.FitOneShotStableGraphics = 0;
            return SettleResult.NotMeasurable;
        }
        if (layoutWasDirty)
            panel.FitSettleRebuildChanges++;

        // Geometry streak — the HISTORIC criterion, semantics unchanged (size, plus the center the
        // old gate ignored). Deliberately NOT reset by the contributor count: a pulsing/blinking
        // element crossing the alpha floor toggles the count every other frame without moving the
        // bounds, and resetting on it would starve the streak forever.
        bool held = panel.FitOneShotStableCount > 0
                    && MeasureMatches(panel.FitOneShotStableSize, panel.FitOneShotStableCenter, size, center);
        // Evaluated per check (not streak-resetting): content ARRIVING changes the contributor
        // count between two consecutive checks, which is the signal a stable bounding box hides.
        bool sameContent = panel.FitOneShotStableCount > 0 && graphics == panel.FitOneShotStableGraphics;
        if (held)
        {
            panel.FitOneShotStableCount++;
        }
        else
        {
            panel.FitOneShotStableSize = size;
            panel.FitOneShotStableCenter = center;
            panel.FitOneShotStableCount = 1;
        }
        panel.FitOneShotStableGraphics = graphics;

        // Tier A (the real gate): nothing left for the layout to apply, no content arrived between
        // the last two checks, and the measurement held for the historic number of checks.
        bool settled = !layoutWasDirty && sameContent
                       && panel.FitOneShotStableCount >= OneShotSettleChecks;
        // Tier B (bound): genuinely ANIMATED content (a typewriter story text, a pulsing prompt)
        // can keep dirtying the layout or flipping a graphic in and out indefinitely — never
        // committing would hold the window hidden until the reveal deadline on EVERY open. Once
        // the measured bounds themselves have held steady this much longer, commit anyway: the
        // bounds are what the fit uses, and they stopped moving.
        bool boundReached = panel.FitOneShotStableCount >= SettleAnimatedFallbackChecks;

        // Reported on the committing line so a hardware log can PROVE cold/warm equality: the
        // first open and every later one must show the same measured size, the same contributor
        // count and the same committed rect — and the rebuild-change counter says whether the
        // cold open needed the flush at all.
        report = $"measured {size.x:F0}x{size.y:F0} px at ({center.x:F0},{center.y:F0}) from " +
                 $"{graphics} visible graphic(s); {panel.FitOneShotStableCount} stable check(s) of " +
                 $"{panel.FitSettleChecks}; forced rebuild changed the measurement " +
                 $"{panel.FitSettleRebuildChanges}x (this check: {(layoutWasDirty ? "YES" : "no")}" +
                 (settled ? ")" : boundReached ? "; committed on the animated-content bound)" : ")");

        return settled || boundReached ? SettleResult.Settled : SettleResult.Settling;
    }

    /// <summary>
    /// True when a render-hidden panel's first fit must be committed NOW with whatever it last
    /// measured, because the reveal deadline is about to open the gate (see
    /// <see cref="FitForceCommitLeadSeconds"/> for why it fires slightly early). This is the
    /// BOUND on the content-aware gate: a window whose layout never settles still reveals within
    /// <see cref="RevealMaxWaitSeconds"/>, and it reveals at a fitted rect rather than at the raw
    /// full-screen window frame.
    /// </summary>
    private static bool ForceCommitDue(ConvertedPanel panel) =>
        panel.RevealPending && panel.RevealDeadline > 0f
        && Time.unscaledTime >= panel.RevealDeadline - FitForceCommitLeadSeconds;

}
