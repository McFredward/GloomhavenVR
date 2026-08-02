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
    /// FIRST-OPEN SIZE BUG round 3 (hardware ModBuild 18): absolute per-axis epsilon (px) a
    /// measurement must hold to inside for the STRICT settle tier. WHY on top of the relative
    /// <see cref="FitChangeFraction"/> test in <see cref="MeasureMatches"/>: that tolerance is 2 %
    /// OF THE MEASURED SIZE, so on the cold ESC-menu measure (247x258 px raw) it accepted ~5 px of
    /// drift PER CHECK — i.e. content sliding at up to ~360 px/s still read as "steady" for the
    /// whole six-check streak. A tween in motion cannot pass a 1.5 px bar, and a genuinely settled
    /// layout reproduces its bounds bit-for-bit, so the strict tier costs settled windows nothing.
    /// Content that keeps jittering (typewriter text) simply falls through to the
    /// <see cref="SettleAnimatedFallbackChecks"/> tier, which still uses the relative tolerance.
    /// </summary>
    private const float SettleStillEpsilonPx = 1.5f;

    /// <summary>
    /// Consecutive SELF-CONSISTENT verify checks a committed one-shot fit must pass before the
    /// rect is locked for good (see <see cref="VerifyOneShotFit"/>). Four checks ≈ 55 ms at 72 Hz:
    /// long enough that a single-frame animation artifact cannot certify a fit, short enough that
    /// a warm re-open (whose content never moves) is certified well before the reveal delay.
    /// </summary>
    private const int FitVerifyStableChecks = 4;

    /// <summary>
    /// How long a one-shot fit that has NO cross-open precedent (the session's FIRST open of this
    /// window — the broken case) keeps the reveal gate closed while it re-verifies. WHY the hold
    /// exists at all: the hardware log proves the ESC menu's content is STILL for ~83 ms and then
    /// jumps to a completely disjoint place (cold union 247x258 px at (-486,-225) versus the warm
    /// 372x1080 px at (-774,0) — no overlap on EITHER axis), so no stillness criterion of any
    /// practical length can see the change coming; only continuing to watch can. The window is
    /// render-hidden throughout, so the wait is invisible, and it is clamped against
    /// <see cref="ConvertedPanel.RevealDeadline"/> (<see cref="RevealMaxWaitSeconds"/>) so the
    /// 0.6 s "a window may never stay invisible" bound is untouched. A WARM open skips the hold
    /// entirely — its fit matches the remembered one, which is the proof the cold open lacks.
    /// </summary>
    private const float FitVerifyUnprovenHoldSeconds = 0.45f;

    /// <summary>Clear the unproven hold this long BEFORE the reveal deadline, so the reveal still
    /// opens through the normal settled path (Info) instead of the deadline path (Warn).</summary>
    private const float FitVerifyRevealLeadSeconds = 0.1f;

    /// <summary>
    /// Total watch window (seconds from the fit commit) during which a committed one-shot fit is
    /// still re-verified and may be corrected. Extends PAST the reveal deadline on purpose: a
    /// permanently mis-fitted window (the reported "empty frame in front, content far away") is far
    /// worse than one visible correction, and the correction is capped at
    /// <see cref="FitVerifyMaxCorrections"/>.
    /// </summary>
    private const float FitVerifyWatchSeconds = 1.5f;

    /// <summary>Hard cap on corrective re-fits per open — the one-shot lock's no-flicker promise
    /// survives: at most this many corrections, each requiring a MATERIAL, persistent error.</summary>
    private const int FitVerifyMaxCorrections = 2;

    /// <summary>Consecutive checks a material self-consistency error must persist before a
    /// corrective re-fit runs (a one-frame animation artifact must never move the window).</summary>
    private const int FitVerifyErrorChecks = 2;

    /// <summary>Frames between verify checks once the window is VISIBLE (~12 Hz at 72 Hz). Hidden
    /// panels check every frame; a visible one is serviced by the normal uGUI pipeline, so the
    /// watch only has to sample often enough to catch a discrete late content jump.</summary>
    private const int FitVerifyVisibleCheckFrames = 6;

    /// <summary>
    /// A committed fit is SELF-CONSISTENT while the re-measured content still sits centered in
    /// (and sized like) the host rect it was fitted to, within this fraction of the host extent.
    /// The fit centers the content by construction, so any drift beyond this means the content
    /// MOVED after the fit was locked — exactly the screenshot case.
    /// </summary>
    private const float FitVerifyTolFraction = 0.06f;

    /// <summary>Floor for <see cref="FitVerifyTolFraction"/> (px) so a small host does not get an
    /// unusably tight tolerance.</summary>
    private const float FitVerifyMinTolPx = 12f;

    /// <summary>
    /// The error must exceed this fraction of the host extent before a corrective re-fit is
    /// allowed — deliberately far above <see cref="FitVerifyTolFraction"/> so only a genuinely
    /// broken fit (content substantially OUTSIDE its own host rect) is ever corrected on a
    /// possibly-visible window; smaller inconsistencies are logged and left alone.
    /// </summary>
    private const float FitVerifyMaterialFraction = 0.25f;

    /// <summary>Absolute floor (px) for the material-error test — see <see cref="FitVerifyMaterialFraction"/>.</summary>
    private const float FitVerifyMaterialMinPx = 40f;

    /// <summary>
    /// Cross-open memory (task item 3): the LAST committed one-shot fit per window, keyed by host
    /// name — <c>(size, center)</c> in host-local px. Its ONLY reliable use is as proof: a later
    /// open that fits materially differently from the remembered one proves that one of the two
    /// was wrong, which the commit log then states loudly. It cannot repair the session's FIRST
    /// open (there is nothing remembered yet — that IS the broken case), so it is deliberately not
    /// used as a fit SOURCE: seeding a rect from a previous open would silently paper over a real
    /// content change (a menu that legitimately gains a button between opens). It IS used as a
    /// CONFIDENCE signal: a fit that reproduces the remembered one needs no unproven-open reveal
    /// hold, so warm opens keep their historic ~0.15 s pop-in.
    /// </summary>
    private static readonly Dictionary<string, Vector4> LastOneShotFits = new();

    /// <summary>Why <see cref="TryGetVisibleHostRect"/> rejected the graphic it was last asked
    /// about — aggregated per measure pass into the fit log (see <see cref="DescribeLastMeasure"/>)
    /// so a hardware run states WHAT the cold open was and was not looking at.</summary>
    private enum MeasureReject
    {
        /// <summary>Contributed (not rejected).</summary>
        None,

        /// <summary>Destroyed, component-disabled, or <c>CanvasRenderer.cull</c> (clipped away).</summary>
        Culled,

        /// <summary>Effective alpha below the caller's floor (still fading in / invisible catcher).</summary>
        Faint,

        /// <summary>Zero-size draw rect (collapsed layout cell, un-built row).</summary>
        Empty,

        /// <summary>Fully outside its enclosing clipper (scrolled out of a viewport).</summary>
        ClippedOut,
    }

    /// <summary>Reject reason of the LAST <see cref="TryGetVisibleHostRect"/> call (scratch).</summary>
    private static MeasureReject s_lastReject;

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
        s_lastReject = MeasureReject.None;
        if (g == null || !g.enabled || g.canvasRenderer == null || g.canvasRenderer.cull)
        {
            s_lastReject = MeasureReject.Culled;
            return false;
        }
        // Effective alpha: own color × hierarchy (CanvasGroup) alpha.
        if (g.color.a * g.canvasRenderer.GetInheritedAlpha() < minAlpha)
        {
            s_lastReject = MeasureReject.Faint;
            return false;
        }

        var rect = (RectTransform)g.transform;
        // Zero draw size = nothing on screen (collapsed layout cells, empty
        // stretch containers with a Graphic) — must not anchor the union at
        // their corner points (test #16 measurement tightening).
        Rect drawRect = rect.rect;
        if (drawRect.width < 0.5f || drawRect.height < 0.5f)
        {
            s_lastReject = MeasureReject.Empty;
            return false;
        }

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
            {
                s_lastReject = MeasureReject.ClippedOut;
                return false; // fully scrolled out of its viewport
            }
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

    // ---- measure diagnostics (round 3: the next hardware log must be DECISIVE) ---------------
    //
    // The ModBuild 18 log could prove THAT the cold measure differs (271x282 px from 31 graphics
    // versus 396x1080 px from 29) but not WHICH graphics made the difference — the union and the
    // contributor count are both lossy. These scratch fields carry the three largest contributors
    // (name + host-local rect), the raw pre-clamp union and the per-reason reject tally out of the
    // last measure pass, formatted on demand by DescribeLastMeasure() at LOG time only (the settle
    // gate runs every frame; the detail string must not be built every frame).

    /// <summary>Number of contributor slots kept per measure pass for the fit log.</summary>
    private const int MeasureTopCount = 3;

    private static readonly string[] MeasureTopNames = new string[MeasureTopCount];
    private static readonly Vector4[] MeasureTopRects = new Vector4[MeasureTopCount];
    private static readonly float[] MeasureTopAreas = new float[MeasureTopCount];
    private static int s_lastRejectCulled, s_lastRejectFaint, s_lastRejectEmpty, s_lastRejectClipped;
    private static Vector2 s_lastUnionMin, s_lastUnionMax;
    private static bool s_lastFrameClamped;

    /// <summary>
    /// Human-readable detail of the LAST <see cref="TryMeasureContent"/> pass for the fit log:
    /// the raw (pre-frame-clamp) union, whether the target frame clamped it, the three largest
    /// contributing graphics with their host-local rects, and how many graphics were rejected for
    /// each reason. Built only when a line is actually logged.
    /// </summary>
    private static string DescribeLastMeasure()
    {
        var sb = new System.Text.StringBuilder(192);
        sb.Append("union (").Append(s_lastUnionMin.x.ToString("F0")).Append(',')
          .Append(s_lastUnionMin.y.ToString("F0")).Append(")..(")
          .Append(s_lastUnionMax.x.ToString("F0")).Append(',')
          .Append(s_lastUnionMax.y.ToString("F0")).Append(") px")
          .Append(s_lastFrameClamped ? " [frame-clamped]" : " [unclamped]")
          .Append("; top: ");
        bool any = false;
        for (int i = 0; i < MeasureTopCount; i++)
        {
            if (MeasureTopAreas[i] <= 0f)
                continue;
            if (any)
                sb.Append(", ");
            any = true;
            Vector4 r = MeasureTopRects[i];
            sb.Append('\'').Append(MeasureTopNames[i]).Append("' ")
              .Append((r.z - r.x).ToString("F0")).Append('x').Append((r.w - r.y).ToString("F0"))
              .Append("px at (").Append(r.x.ToString("F0")).Append(',').Append(r.y.ToString("F0"))
              .Append(')');
        }
        if (!any)
            sb.Append("none");
        sb.Append("; rejected ").Append(s_lastRejectCulled).Append(" culled/disabled, ")
          .Append(s_lastRejectFaint).Append(" faint, ").Append(s_lastRejectEmpty)
          .Append(" zero-size, ").Append(s_lastRejectClipped).Append(" clipped out");
        return sb.ToString();
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
        int contributing = 0;

        s_lastRejectCulled = s_lastRejectFaint = s_lastRejectEmpty = s_lastRejectClipped = 0;
        for (int i = 0; i < MeasureTopCount; i++)
        {
            MeasureTopAreas[i] = 0f;
            MeasureTopNames[i] = string.Empty;
        }
        s_lastFrameClamped = false;

        ClipperMemo.Clear();
        GraphicScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (!TryGetVisibleHostRect(panel, g, out Vector2 gMin, out Vector2 gMax))
            {
                switch (s_lastReject)
                {
                    case MeasureReject.Culled: s_lastRejectCulled++; break;
                    case MeasureReject.Faint: s_lastRejectFaint++; break;
                    case MeasureReject.Empty: s_lastRejectEmpty++; break;
                    default: s_lastRejectClipped++; break;
                }
                continue;
            }
            min = Vector2.Min(min, gMin);
            max = Vector2.Max(max, gMax);
            contributing++;
            RecordTopContributor(g, gMin, gMax);
        }
        GraphicScratch.Clear();
        s_lastMeasureGraphics = contributing;
        s_lastUnionMin = min;
        s_lastUnionMax = max;

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
            // Round 3 diagnostics: a union the FRAME cropped is a different animal from one the
            // content itself bounded — the fit log now says which of the two it committed.
            s_lastFrameClamped = min.x < frameMin.x || min.y < frameMin.y
                                 || max.x > frameMax.x || max.y > frameMax.y;
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
    /// Keep the <see cref="MeasureTopCount"/> largest contributors of the running measure pass
    /// (insertion sort over a 3-slot array — no allocation, no ordering cost worth measuring for
    /// the few dozen graphics of a menu). The union is a bounding box: naming the elements that
    /// actually SPAN it is the only way a hardware log can say why a cold open measured a
    /// different box than a warm one.
    /// </summary>
    private static void RecordTopContributor(Graphic g, Vector2 gMin, Vector2 gMax)
    {
        float area = (gMax.x - gMin.x) * (gMax.y - gMin.y);
        int slot = -1;
        for (int i = 0; i < MeasureTopCount; i++)
        {
            if (area > MeasureTopAreas[i])
            {
                slot = i;
                break;
            }
        }
        if (slot < 0)
            return;
        for (int i = MeasureTopCount - 1; i > slot; i--)
        {
            MeasureTopAreas[i] = MeasureTopAreas[i - 1];
            MeasureTopRects[i] = MeasureTopRects[i - 1];
            MeasureTopNames[i] = MeasureTopNames[i - 1];
        }
        MeasureTopAreas[slot] = area;
        MeasureTopRects[slot] = new Vector4(gMin.x, gMin.y, gMax.x, gMax.y);
        Transform? parent = g.transform.parent;
        MeasureTopNames[slot] = parent != null ? parent.name + "/" + g.name : g.name;
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
    ///
    /// <paramref name="force"/> (round 3, the one-shot VERIFY path): skip the shrink/re-center
    /// damping. The damping exists to stop OSCILLATING content (combat-log lines fading in and
    /// out) from re-fitting twice a second; a verify correction is the opposite case — a single,
    /// proven, material inconsistency on a window whose rect is otherwise LOCKED — and must land
    /// in the frame it was decided, not 1.5 s later.
    /// </summary>
    internal static bool FitHostToContent(ConvertedPanel panel, RectTransform? contentRoot = null,
        bool force = false)
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
        if (panel.FitMeasuredOnce && !growth && !force)
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
        // Round 3: every APPLIED fit advances the generation. ModalFallback's one-shot followers
        // (5b board-scale re-derivation, 5b-pose re-place) latch on the generation instead of a
        // plain bool, so a VERIFY correction re-runs them against the corrected geometry — without
        // it the window would keep the scale and pose derived from the rect that was just proven
        // wrong.
        panel.FitAppliedGeneration++;
        VRLog.Info("WorldUI", $"Host rect fit '{panel.HostGo.name}': " +
                              $"{host.width:F0}x{host.height:F0} → {size.x:F0}x{size.y:F0} px " +
                              $"(content offset {center.x:F0},{center.y:F0}) — {DescribeLastMeasure()}.");
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
        // Round 3: the one-shot VERIFY watch is exempt from the grace too. It starts while the
        // window is still hidden and must keep running across the reveal — pausing it for the
        // remainder of the 0.4 s grace would blind it during exactly the window in which the
        // hardware log shows the ESC menu's content jumping.
        if (Time.unscaledTime < panel.FitNotBefore && !panel.RevealPending && !panel.FitVerifyPending)
            return;

        // Item 5 (pause-menu size consistency): a one-shot full-screen menu must land the SAME
        // compact size on EVERY open. Defer its single fit until the window's layout has SETTLED
        // (deterministic rebuild + a stable measured rect across N checks) instead of committing on
        // the first measurable frame, which locked a different rect each open.
        //
        // Round 3 (first open STILL wrong on hardware ModBuild 18): the one-shot path is now two
        // phases. COMMIT (below) is unchanged in spirit — settle, then fit once. VERIFY
        // (<see cref="VerifyOneShotFit"/>) is new: the committed rect must PROVE itself
        // self-consistent (the content still centered in, and sized like, the host it was fitted
        // to) before it is locked, because the hardware log shows the ESC menu's content jumping to
        // a completely disjoint place AFTER a perfectly steady six-check settle streak — a change
        // no pre-commit criterion can anticipate, and one only a post-commit watch can catch.
        if (panel.FitOneShot && !panel.FitVerifyPending && !panel.FitCommitted)
        {
            SettleOneShotFit(panel);
            return;
        }
        if (panel.FitOneShot && panel.FitVerifyPending)
        {
            VerifyOneShotFit(panel);
            return;
        }
        if (panel.FitOneShot)
            return; // committed AND verified — the rect is locked (FitEnabled is false by now)

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
    ///
    /// ROUND 3 (hardware ModBuild 18 — still broken, and the log finally says why). The cold ESC
    /// menu measured 271x282 px at (-486,-225) from 31 graphics with six steady checks and a flush
    /// that changed nothing; the warm re-open measured 396x1080 px at (-774,0) from 29. Those two
    /// boxes do not merely differ in size — they do not OVERLAP ON EITHER AXIS. The cold fit was
    /// therefore not a partial measurement of the final content: it measured content that then
    /// MOVED somewhere else entirely, which is exactly the screenshot (an almost empty window frame
    /// in front of the player, the real menu far off to the side). The same log proves the game's
    /// layout for the window had never run at cold-convert time — the height cap fires on every
    /// WARM open ("height capped 2040-&gt;1080") and on no cold one, i.e. the captured root was still
    /// the un-laid-out 1080. No criterion evaluable AT COMMIT TIME can see that jump coming, so
    /// this method no longer locks: it commits, and hands the rect to <see cref="VerifyOneShotFit"/>
    /// to earn its lock (and, on the session's first open of a window, to earn the reveal).
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

        // Settled (or forced): commit the single fit. FitMeasuredOnce is still false here, so
        // FitHostToContent applies the now-stable size immediately (no shrink damping).
        FitHostToContent(panel, contentRoot);
        panel.FitMeasuredOnce = true;
        panel.FitCommitted = true;
        panel.FitNextCheckFrame = Time.frameCount + FitCheckIntervalFrames;
        ArmOneShotVerify(panel, out string proof);
        string detail = DescribeLastMeasure();
        if (state == SettleResult.Settled)
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' full-screen menu fitted ONCE after its " +
                                  $"layout settled — {report} — {detail} — {proof}");
        else
            VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' full-screen menu fit FORCED before its " +
                                  $"layout settled ({(revealDue ? "reveal deadline" : "first-fit deadline")}) — " +
                                  $"{report} — {detail} — {proof}");
    }

    /// <summary>
    /// Arm the post-commit VERIFY phase of a one-shot fit and report (for the commit log) whether
    /// this open's rect has cross-open PROOF behind it.
    ///
    /// WHY the reveal hold is keyed on cross-open memory (task item 3, decided here): the hardware
    /// log proves the difference between a good and a bad one-shot fit is not visible AT COMMIT
    /// TIME — the cold ESC-menu measure was steady for six consecutive checks, its forced layout
    /// flush changed nothing, and it was still measuring content that jumped to a DISJOINT place
    /// milliseconds later. The only signal that separates the two cases without more guessing is
    /// whether this window has already been fitted once in this session: a warm open reproduces
    /// the remembered rect (proof it is the settled one) and keeps the historic fast pop-in, while
    /// an UNPROVEN first open spends the remaining — already budgeted, entirely invisible —
    /// pre-reveal time re-verifying instead of revealing at a rect nothing has corroborated.
    /// The memory is never used as a fit SOURCE (see <see cref="LastOneShotFits"/>): a menu may
    /// legitimately change between opens, and silently restoring an old rect would hide that.
    /// </summary>
    private static void ArmOneShotVerify(ConvertedPanel panel, out string proof)
    {
        proof = "rect COMMITTED (host gone — nothing to verify).";
        if (panel.HostRect == null || panel.HostGo == null)
        {
            panel.FitEnabled = false;
            return;
        }
        float now = Time.unscaledTime;
        Rect host = panel.HostRect.rect;
        string key = panel.HostGo.name;
        bool matchesMemory = false;
        bool hasMemory = LastOneShotFits.TryGetValue(key, out Vector4 remembered);
        if (hasMemory)
        {
            float tolX = Mathf.Max(Mathf.Max(host.width, remembered.x) * FitChangeFraction, 2f);
            float tolY = Mathf.Max(Mathf.Max(host.height, remembered.y) * FitChangeFraction, 2f);
            matchesMemory = Mathf.Abs(host.width - remembered.x) <= tolX
                            && Mathf.Abs(host.height - remembered.y) <= tolY;
        }

        panel.FitVerifyPending = true;
        panel.FitVerifyProven = matchesMemory;
        panel.FitVerifyUntil = now + FitVerifyWatchSeconds;
        panel.FitVerifyNextCheckFrame = 0;
        panel.FitVerifyStableCount = 0;
        panel.FitVerifyErrorCount = 0;
        panel.FitVerifyCorrections = 0;
        // An UNPROVEN rect holds the reveal gate for the rest of the (already bounded) pre-reveal
        // budget; a rect that reproduces a remembered one is proven and holds nothing.
        panel.FitVerifyHoldRevealUntil = matchesMemory
            ? 0f
            : Mathf.Min(now + FitVerifyUnprovenHoldSeconds,
                panel.RevealDeadline > 0f ? panel.RevealDeadline - FitVerifyRevealLeadSeconds : 0f);

        if (!hasMemory)
        {
            proof = "rect COMMITTED but UNPROVEN (first open of this window in the session — nothing " +
                    "to compare against): the reveal is held while it is re-verified, still bounded by " +
                    $"the {RevealMaxWaitSeconds * 1000f:F0} ms reveal deadline.";
        }
        else if (matchesMemory)
        {
            proof = $"rect COMMITTED and matches the previous open ({remembered.x:F0}x{remembered.y:F0} px) " +
                    "— proven, no reveal hold, verify continues in the background.";
        }
        else
        {
            proof = $"rect COMMITTED but DIFFERS MATERIALLY from the previous open of this window " +
                    $"({remembered.x:F0}x{remembered.y:F0} px, target at " +
                    $"({remembered.z:F0},{remembered.w:F0}) → now {host.width:F0}x{host.height:F0} px). The " +
                    "reveal is held while this one is re-verified.";
            VRLog.Warn("WorldUI", $"MODAL WINDOW: '{key}' one-shot fit is INCONSISTENT ACROSS OPENS — " +
                                  $"previous {remembered.x:F0}x{remembered.y:F0} px, now " +
                                  $"{host.width:F0}x{host.height:F0} px. Either one of the two fits measured a " +
                                  "menu that had not finished laying out (the smaller rect is then the " +
                                  "defective one), or the menu genuinely gained/lost content between the two " +
                                  "opens. Report this line together with the two 'Host rect fit' lines above " +
                                  "it — their measured unions and top contributors say which of the two it is.");
        }
    }

    /// <summary>
    /// THE DEFENSIVE VALIDITY CHECK (round 3). A committed one-shot fit centers the measured
    /// content in the host rect BY CONSTRUCTION — so re-measuring it immediately afterwards must
    /// yield a content offset of ~0 and a size of ~the host rect. Anything else means the content
    /// MOVED (or grew, or shrank) after the fit was taken, i.e. the fit measured a window that was
    /// not finished. That is exactly the reported defect: the hardware screenshot shows an almost
    /// EMPTY window frame in front of the player with the real menu content far off to the side —
    /// a 271x282 px host whose content, once the game's layout finally landed, sat hundreds of
    /// pixels outside it, permanently, because the one-shot fit had already LOCKED.
    ///
    /// So the lock is now earned, not assumed:
    ///  * SELF-CONSISTENT for <see cref="FitVerifyStableChecks"/> consecutive checks → lock (Info).
    ///  * MATERIALLY inconsistent (<see cref="FitVerifyMaterialFraction"/> of the host extent) for
    ///    <see cref="FitVerifyErrorChecks"/> consecutive checks → ONE forced corrective re-fit
    ///    (Warn), up to <see cref="FitVerifyMaxCorrections"/> per open. Below that bar the
    ///    inconsistency is logged at the end but never moves the window: the no-flicker promise of
    ///    the one-shot lock only ever yields to a window that is genuinely broken.
    ///  * <see cref="FitVerifyWatchSeconds"/> elapsed → lock at the best rect available (Warn if it
    ///    never became self-consistent).
    ///
    /// The watch deliberately outlives the reveal: while the panel is render-hidden the re-measure
    /// costs nothing at all, and after the reveal one visible correction is still enormously better
    /// than a window that stays wrong until it is closed.
    /// </summary>
    private static void VerifyOneShotFit(ConvertedPanel panel)
    {
        if (panel.Target == null || panel.HostRect == null || panel.HostGo == null)
        {
            panel.FitVerifyPending = false;
            panel.FitVerifyHoldRevealUntil = 0f;
            panel.FitEnabled = false;
            return;
        }
        float now = Time.unscaledTime;
        bool expired = now >= panel.FitVerifyUntil;
        // COST CONTROL. While the panel is render-hidden the check runs every frame and forces the
        // layout/canvas flush — the uGUI pipeline does not service a disabled canvas, so without
        // the flush the measure would read stale geometry (that ordering IS the round-2 fix). Once
        // the window is VISIBLE the pipeline runs it every frame anyway: no flush is needed (a
        // global Canvas.ForceUpdateCanvases per frame for over a second would be a real cost), and
        // the watch throttles to a few checks per second — a late content jump is a discrete event,
        // not something that needs per-frame sampling.
        bool hidden = panel.RenderHidden;
        if (!hidden && Time.frameCount < panel.FitVerifyNextCheckFrame && !expired)
            return;
        panel.FitVerifyNextCheckFrame = Time.frameCount + FitVerifyVisibleCheckFrames;

        RectTransform? contentRoot = panel.FitContentRoot;
        RectTransform root = ResolveFitRoot(panel, contentRoot);
        if (hidden)
            FlushPendingLayout(panel);
        if (!TryMeasureContent(panel, root, out Vector2 size, out Vector2 center))
        {
            // Content became unmeasurable (a fade-out, a closing window). Nothing to verify
            // against — never re-fit on that; just run the watch out and lock.
            panel.FitVerifyStableCount = 0;
            panel.FitVerifyErrorCount = 0;
            if (expired)
                LockOneShotFit(panel, "content became unmeasurable during the verify watch", warn: false);
            return;
        }

        Rect host = panel.HostRect.rect;
        float tolX = Mathf.Max(host.width * FitVerifyTolFraction, FitVerifyMinTolPx);
        float tolY = Mathf.Max(host.height * FitVerifyTolFraction, FitVerifyMinTolPx);
        float errX = Mathf.Max(Mathf.Abs(center.x), Mathf.Abs(size.x - host.width));
        float errY = Mathf.Max(Mathf.Abs(center.y), Mathf.Abs(size.y - host.height));
        bool consistent = errX <= tolX && errY <= tolY;
        bool material = errX > Mathf.Max(host.width * FitVerifyMaterialFraction, FitVerifyMaterialMinPx)
                        || errY > Mathf.Max(host.height * FitVerifyMaterialFraction, FitVerifyMaterialMinPx);

        if (consistent)
        {
            panel.FitVerifyErrorCount = 0;
            panel.FitVerifyStableCount++;
            // A PROVEN rect (it reproduces this window's previous open) locks the moment the streak
            // is met — the warm path keeps its historic behaviour exactly. An UNPROVEN one keeps
            // being watched for the whole (bounded) window even after it reveals: the hardware
            // failure is a discrete late jump, and a rect nothing has corroborated has not earned
            // the right to stop looking. The REVEAL is released independently, at
            // FitVerifyHoldRevealUntil, so the watch never delays the window becoming visible.
            if (panel.FitVerifyStableCount >= FitVerifyStableChecks
                && (panel.FitVerifyProven || expired))
            {
                LockOneShotFit(panel,
                    $"self-consistent for {panel.FitVerifyStableCount} check(s) " +
                    $"(content offset {center.x:F0},{center.y:F0} px inside a {host.width:F0}x{host.height:F0} px " +
                    $"host; {panel.FitVerifyCorrections} correction(s) applied)", warn: false);
            }
            return;
        }

        panel.FitVerifyStableCount = 0;
        // A correction must never chase a MOVING target: the error has to be material AND the
        // erroneous measurement itself has to have stopped moving (absolute stillness against the
        // previous error check). Without this a dialog caught mid scale-in would be re-fitted to
        // an intermediate size — trading one wrong rect for another. A still, materially
        // inconsistent measurement is the real failure: the content has arrived somewhere the
        // committed host does not cover.
        bool errorHeld = material && panel.FitVerifyErrorCount > 0
                         && MeasureStill(panel.FitVerifyErrorSize, panel.FitVerifyErrorCenter, size, center);
        panel.FitVerifyErrorCount = material ? (errorHeld ? panel.FitVerifyErrorCount + 1 : 1) : 0;
        panel.FitVerifyErrorSize = size;
        panel.FitVerifyErrorCenter = center;
        if (panel.FitVerifyErrorCount >= FitVerifyErrorChecks
            && panel.FitVerifyCorrections < FitVerifyMaxCorrections)
        {
            panel.FitVerifyErrorCount = 0;
            panel.FitVerifyCorrections++;
            VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' one-shot fit REJECTED by the validity " +
                                  $"check — the committed {host.width:F0}x{host.height:F0} px host no longer " +
                                  $"contains its own content (re-measured {size.x:F0}x{size.y:F0} px at offset " +
                                  $"{center.x:F0},{center.y:F0}; that is the 'empty frame in front, content far " +
                                  $"away' failure). {DescribeLastMeasure()}. Re-fitting " +
                                  $"(correction {panel.FitVerifyCorrections} of {FitVerifyMaxCorrections}, " +
                                  $"window {(panel.RenderHidden ? "still render-hidden" : "already visible")}).");
            FitHostToContent(panel, contentRoot, force: true);
            return;
        }

        if (expired)
            LockOneShotFit(panel,
                $"NEVER became self-consistent — content offset {center.x:F0},{center.y:F0} px, measured " +
                $"{size.x:F0}x{size.y:F0} px against a {host.width:F0}x{host.height:F0} px host " +
                $"({panel.FitVerifyCorrections} correction(s) applied). {DescribeLastMeasure()}", warn: true);
    }

    /// <summary>
    /// End of the one-shot fit's life cycle: freeze the rect (no per-frame re-fit flicker — the
    /// historic promise), record it as this window's cross-open reference, and state in one line
    /// how the lock was earned so a hardware log can compare open 1 against open 2.
    /// </summary>
    private static void LockOneShotFit(ConvertedPanel panel, string outcome, bool warn)
    {
        panel.FitVerifyPending = false;
        panel.FitVerifyHoldRevealUntil = 0f;
        panel.FitEnabled = false; // one-shot: freeze the rect
        if (panel.HostRect == null || panel.HostGo == null)
            return;
        Rect host = panel.HostRect.rect;
        LastOneShotFits[panel.HostGo.name] = new Vector4(host.width, host.height,
            panel.Target != null ? panel.Target.anchoredPosition.x : 0f,
            panel.Target != null ? panel.Target.anchoredPosition.y : 0f);
        string line = $"MODAL WINDOW: '{panel.HostGo.name}' one-shot host rect LOCKED at " +
                      $"{host.width:F0}x{host.height:F0} px — {outcome}.";
        if (warn)
            VRLog.Warn("WorldUI", line);
        else
            VRLog.Info("WorldUI", line);
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
        string detail = DescribeLastMeasure();
        if (state == SettleResult.Settled)
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' pre-reveal first fit committed after its " +
                                  $"layout settled — {report} — {detail} — the window reveals at this rect " +
                                  "(later growth still re-fits through the periodic check).");
        else
            VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' pre-reveal first fit FORCED at the reveal " +
                                  $"deadline before its layout settled — {report} — {detail} — revealing at the " +
                                  "best measurement available (report this line).");
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
    /// STRICT stillness: the two measurements are identical to within
    /// <see cref="SettleStillEpsilonPx"/> px on every axis — no relative slack. Used by the
    /// settle gate's strict tier only (see the constant's doc for why the relative tolerance was
    /// too generous on a small cold measure); the animated-content fallback tier keeps
    /// <see cref="MeasureMatches"/>, so a jittering story text still commits on schedule.
    /// </summary>
    private static bool MeasureStill(Vector2 aSize, Vector2 aCenter, Vector2 bSize, Vector2 bCenter) =>
        Mathf.Abs(aSize.x - bSize.x) <= SettleStillEpsilonPx
        && Mathf.Abs(aSize.y - bSize.y) <= SettleStillEpsilonPx
        && Mathf.Abs(aCenter.x - bCenter.x) <= SettleStillEpsilonPx
        && Mathf.Abs(aCenter.y - bCenter.y) <= SettleStillEpsilonPx;

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
        // Round 3: the STRICT tier additionally demands absolute stillness against the streak's
        // anchor. 2 % of a 247 px cold measure is ~5 px per check — enough slack for content
        // drifting at ~360 px/s to look "steady" for the whole streak.
        bool still = panel.FitOneShotStableCount > 0
                     && MeasureStill(panel.FitOneShotStableSize, panel.FitOneShotStableCenter, size, center);
        // Evaluated per check (not streak-resetting): content ARRIVING changes the contributor
        // count between two consecutive checks, which is the signal a stable bounding box hides.
        bool sameContent = panel.FitOneShotStableCount > 0 && graphics == panel.FitOneShotStableGraphics;
        if (held)
        {
            panel.FitOneShotStableCount++;
            panel.FitSettleStillCount = still ? panel.FitSettleStillCount + 1 : 0;
        }
        else
        {
            panel.FitOneShotStableSize = size;
            panel.FitOneShotStableCenter = center;
            panel.FitOneShotStableCount = 1;
            panel.FitSettleStillCount = 1;
        }
        panel.FitOneShotStableGraphics = graphics;

        // Tier A (the real gate): nothing left for the layout to apply, no content arrived between
        // the last two checks, and the measurement held — relatively for the historic number of
        // checks AND absolutely still (round 3) for the same number.
        bool settled = !layoutWasDirty && sameContent
                       && panel.FitOneShotStableCount >= OneShotSettleChecks
                       && panel.FitSettleStillCount >= OneShotSettleChecks;
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
                 $"{graphics} visible graphic(s); {panel.FitOneShotStableCount} stable check(s) " +
                 $"({panel.FitSettleStillCount} of them ABSOLUTELY still) of {panel.FitSettleChecks}; " +
                 $"forced rebuild changed the measurement " +
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
