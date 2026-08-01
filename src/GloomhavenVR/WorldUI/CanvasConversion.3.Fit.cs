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
        bool any = false;

        ClipperMemo.Clear();
        GraphicScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            if (!TryGetVisibleHostRect(panel, GraphicScratch[i], out Vector2 gMin, out Vector2 gMax))
                continue;
            min = Vector2.Min(min, gMin);
            max = Vector2.Max(max, gMax);
            any = true;
        }
        GraphicScratch.Clear();

        if (!any)
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

        RectTransform root = contentRoot != null && contentRoot.gameObject.activeInHierarchy
                             && contentRoot.IsChildOf(panel.Target)
            ? contentRoot
            : panel.Target;

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
        if (!panel.FitEnabled || Time.unscaledTime < panel.FitNotBefore)
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
    /// </summary>
    private static void SettleOneShotFit(ConvertedPanel panel)
    {
        RectTransform? contentRoot = panel.FitContentRoot;
        RectTransform root = contentRoot != null && contentRoot.gameObject.activeInHierarchy
                             && contentRoot.IsChildOf(panel.Target)
            ? contentRoot
            : panel.Target;

        // Deterministic layout: rebuild pending layout NOW so every open measures the same settled
        // tree regardless of how warm the layout was (a cold first open is laid out identically to a
        // warm re-open before we measure).
        Canvas.ForceUpdateCanvases();
        if (panel.Target != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Target);

        bool measurable = TryMeasureContent(panel, root, out Vector2 size, out _);
        bool deadline = Time.unscaledTime >= panel.FitFirstDeadline;

        if (!measurable)
        {
            // Nothing visible yet — keep retrying until the deadline, then give up to the full rect
            // (the reveal's deadline floor still pops the menu in, never an invisible one).
            if (deadline && !panel.FitGaveUpLogged)
            {
                panel.FitGaveUpLogged = true;
                panel.FitEnabled = false;
                panel.FitMeasuredOnce = true;
                VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' one-shot fit found nothing " +
                                      $"measurable after {FitFirstWarnSeconds:F1}s — keeping the full rect.");
            }
            return;
        }

        // Stability gate: the measured size must hold steady across consecutive checks.
        float tolX = Mathf.Max(panel.FitOneShotStableSize.x, size.x) * FitChangeFraction;
        float tolY = Mathf.Max(panel.FitOneShotStableSize.y, size.y) * FitChangeFraction;
        if (panel.FitOneShotStableCount > 0
            && Mathf.Abs(size.x - panel.FitOneShotStableSize.x) <= tolX
            && Mathf.Abs(size.y - panel.FitOneShotStableSize.y) <= tolY)
        {
            panel.FitOneShotStableCount++;
        }
        else
        {
            panel.FitOneShotStableSize = size;
            panel.FitOneShotStableCount = 1;
        }

        if (panel.FitOneShotStableCount < OneShotSettleChecks && !deadline)
            return; // still settling — keep measuring

        // Settled (or deadline forced): commit the single fit and LOCK. FitMeasuredOnce is still
        // false here, so FitHostToContent applies the now-stable size immediately (no shrink damping).
        FitHostToContent(panel, contentRoot);
        panel.FitMeasuredOnce = true;
        panel.FitNextCheckFrame = Time.frameCount + FitCheckIntervalFrames;
        panel.FitEnabled = false; // one-shot: freeze the rect (no per-frame re-fit flicker)
        VRLog.Info("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' full-screen menu fitted ONCE after its " +
                              $"layout settled ({panel.FitOneShotStableCount} stable check(s)) — host rect locked " +
                              "(same compact size every open, no re-fit flicker).");
    }

}
