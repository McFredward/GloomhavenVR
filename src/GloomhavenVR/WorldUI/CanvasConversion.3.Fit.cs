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
    /// BREATHING ROOM the fit adds around the measured visible-content union, in uGUI px of the
    /// host's own space, on EVERY side (see <see cref="TryMeasureContent"/>: the union is centered
    /// in a host that is <c>union + 2 ×</c> this per axis). Historic 12 px value — unchanged.
    ///
    /// <para>INTERNAL BECAUSE A SEAT SOLVED FROM THE HOST RECT'S EDGE IS OFF BY EXACTLY THIS
    /// (user, ModBuild 102: "Weiterhin rutschen die Elemente immer direkt so beginn tiefer als es
    /// sein müsste. … sie sollten sich immer am oberen Rand orientieren"). The decision area is
    /// laid out DOWNWARD from one ceiling
    /// (<c>WorldUI.Surfaces.DecisionDockSurface.AreaCeilingUp</c>): the widget row measures its own
    /// visible widget graphics and lands flush, but the prompt LINE and the use-bar drawer solved
    /// their seats from <c>HostRect.rect.yMax</c> / <c>rect.height</c> — the PADDED box — so each
    /// of them started this much below the ceiling AND handed the same error down to whatever hung
    /// off its published bottom edge. Any surface that pins a host by a rect EDGE must subtract
    /// this; that is what makes "the top edge is the offset" true to the pixel.</para>
    /// </summary>
    internal const float FitContentPaddingPx = 12f;

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

    /// <summary>How often each one-shot window has been fitted in this session — the "open #N" of
    /// the fit summary line, so a hardware log can be read as "open 1 said X, open 2 said Y"
    /// without counting lines by hand.</summary>
    private static readonly Dictionary<string, int> OneShotOpens = new();

    /// <summary>
    /// Hard cap (seconds from the fit commit) on the extended verify watch of a window whose show
    /// animation is still in flight. The fit already stands at the AUTHORED geometry, so this is
    /// pure insurance: it keeps re-checking until the animation lands and the rendered content can
    /// confirm the rect (or correct it once). Generous because the check is invisible and cheap —
    /// a hidden panel measures for free, a visible one only ~12x per second — and bounded because a
    /// permanently animating window must not keep a fit "open" forever.
    /// </summary>
    private const float FitVerifyAnimatedWatchSeconds = 4f;

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

    // ---- ROUND 4: the SHOW-ANIMATION blind spot -------------------------------------------------
    //
    // What the ModBuild 20 hardware log (peer machine, .planning/debug/remote/LogOutput.log) proves
    // about the cold ESC-menu open, quoted verbatim in the commit message:
    //
    //   * at convert time (age 0.0 s) the depth-mask diag measured 'Scroll View/Viewport' at
    //     388x1003 px — the SAME rect the warm open fits;
    //   * ~0.3 s later the fit measured the whole window at 76x307 px ('UI Menu Panel' 77x287
    //     where warm reads 368x1080, 'Scroll View/Viewport' 79x277 where warm reads 388x1003);
    //   * the VERIFY ran, REJECTED the commit once and re-fitted — to the SAME 76x307 px — and
    //     then locked it as "self-consistent for 13 check(s) (content offset 0,0 px)".
    //
    // Every contributor had shrunk by the same per-axis factor (~0.21 in x, ~0.27 in y) while its
    // AUTHORED RectTransform rect stayed exactly what the warm open measures. That is not a
    // half-built layout: it is the game's own LeanTween show animation
    // (ESCMenu._leanTweenGUIAnimator → LeanTweenGuiAnimationSettingScale, decompiled/GH.Runtime)
    // caught mid-flight, and on a cold open it is still in flight after the whole reveal budget.
    //
    // WHY NO SELF-CONSISTENCY CHECK CAN CATCH THAT: the content genuinely IS that small right now,
    // it is perfectly still (six ABSOLUTELY still checks), a forced layout rebuild changes nothing,
    // and after the fit it sits perfectly centered in the host it was fitted to. The verify asked
    // "does the content fit its host?" — and the honest answer was yes. The question it could not
    // ask is "is this the geometry the window will RENDER AT when the animation ends?".
    //
    // THE FIX MEASURES THE ANSWER INSTEAD OF GUESSING IT: alongside the live (rendered) union the
    // measure now builds the AUTHORED union — every graphic's own RectTransform.rect, positioned by
    // the accumulated localPosition chain with all intermediate localScales treated as 1. An
    // animation that scales a container changes the live union and leaves the authored one alone,
    // so the ratio between them IS the animation state, and the authored union IS the final
    // geometry. When the two disagree materially the fit commits the AUTHORED one — the rect the
    // window ends up at — instead of a freeze-frame of the animation.

    /// <summary>
    /// Deviation of the area-weighted live/authored size ratio from 1 above which the measure
    /// treats the window as MID SHOW-ANIMATION and fits the authored geometry instead. Deliberately
    /// coarse (30 %): the failure case is a factor of ~4.8, while legitimately scaled decorations (a
    /// pulsing initiative selection ring, an icon authored at 0.5) move an area-weighted whole-panel
    /// ratio by a few percent at most — so no settled window ever crosses it and every other window
    /// family measures byte-for-byte as before.
    /// </summary>
    private const float ShowAnimationScaleTol = 0.30f;

    /// <summary>
    /// A graphic scaled below this fraction of its authored size contributes nothing to the
    /// AUTHORED union either. WHY: scaling an element to (near) zero is the standard uGUI way to
    /// hide a collapsed dropdown/row, and the authored union must not resurrect it at full size.
    /// Far below the ~0.21 the frozen ESC-menu animation sat at, so the real case is unaffected.
    /// </summary>
    private const float AuthoredCollapsedRatio = 0.05f;

    /// <summary>
    /// Task #4 per-graphic measure: the visibility test both fit unions use (enabled, not culled,
    /// effective alpha ≥ <paramref name="minAlpha"/>, non-degenerate draw rect) plus the graphic's
    /// host-local bounds, CLAMPED to its enclosing clipper's rect (<see cref="RectMask2D"/> /
    /// stencil <see cref="Mask"/> — i.e. a ScrollRect viewport): a settings row scrolled out of its
    /// viewport is CLIPPED at render time, so it must not grow the content FIT. False = the graphic
    /// contributes nothing (invisible, empty, or fully scrolled out).
    ///
    /// <para>The second caller this method used to serve — depth-mask quad emission, with its own
    /// stricter alpha floor and its "ink" tightening — is gone with the depth stamps themselves
    /// (see CanvasConversion.8.Order.cs). What remains is the content fit alone, at exactly the
    /// geometry the shipped builds measured.</para>
    /// </summary>
    private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g,
        out Vector2 gMin, out Vector2 gMax, float minAlpha = FitMinAlpha) =>
        TryGetVisibleHostRect(panel, g, out gMin, out gMax, out _, out _, minAlpha);

    /// <summary>
    /// <see cref="TryGetVisibleHostRect(ConvertedPanel,Graphic,out Vector2,out Vector2,float)"/>
    /// plus the graphic's AUTHORED (scale-neutral) host-local rect in
    /// <paramref name="aMin"/>/<paramref name="aMax"/> — see the round-4 note above
    /// <see cref="ShowAnimationScaleTol"/>. The authored rect is the graphic's own
    /// <see cref="RectTransform.rect"/> placed by the accumulated <c>localPosition</c> chain up to
    /// the host with every intermediate <c>localScale</c> treated as 1, clamped to its clipper's
    /// authored rect the same way. It is empty (max &lt;= min) when the authored geometry is
    /// unusable; the caller then leaves that graphic out of the authored union.
    /// </summary>
    private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g,
        out Vector2 gMin, out Vector2 gMax, out Vector2 aMin, out Vector2 aMax,
        float minAlpha = FitMinAlpha)
    {
        gMin = default;
        gMax = default;
        aMin = default;
        aMax = default;
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
        for (int c = 0; c < 4; c++)
        {
            Vector3 local = panel.HostRect.InverseTransformPoint(CornerScratch[c]);
            if (local.x < min.x) min.x = local.x;
            if (local.y < min.y) min.y = local.y;
            if (local.x > max.x) max.x = local.x;
            if (local.y > max.y) max.y = local.y;
        }

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

        // Round 4: the same rect as the LAYOUT authored it — no show-animation scale anywhere in
        // the chain. Clipped by the clipper's authored rect for the same reason the live one is.
        AuthoredHostRect(panel, rect, out aMin, out aMax);
        if (clipper != null)
        {
            AuthoredHostRect(panel, clipper, out Vector2 acMin, out Vector2 acMax);
            aMin = Vector2.Max(aMin, acMin);
            aMax = Vector2.Min(aMax, acMax);
        }
        return true;
    }

    /// <summary>
    /// THE FIT'S OWN VISIBILITY VERDICT, EXPOSED — so the MR backing plate can ask the question
    /// instead of guessing at it (user hardware report 2026-08-08: "Der mixed Reality Hintergrund
    /// für die Initiativreihenfolge ist nach deiner letzten Änderung vertikal zu lang - davor war
    /// es besser, ich will nicht, dass große Hintergrund-Rechtecke existieren von denen der Platz
    /// garnicht genutzt wird").
    ///
    /// <para>ROOT CAUSE OF THAT REPORT, and why this is a shared method rather than a second copy
    /// of the test: <c>MrBacking.GlyphTrueRect</c> grows a panel's plate to cover TMP lines that
    /// RENDER outside the fitted host rect (ModBuild 90's genuinely overflowing damage prompt), and
    /// it judged "is this text on screen?" with its own, far weaker predicate — active + non-empty
    /// text + not under a mask + non-degenerate textBounds. THIS method rejects a graphic for four
    /// distinct reasons (<see cref="MeasureReject.Culled"/> / <see cref="MeasureReject.Faint"/> /
    /// <see cref="MeasureReject.Empty"/> / <see cref="MeasureReject.ClippedOut"/>) and additionally
    /// skips this mod's own cue art, and the plate then unioned EXACTLY the text the fit had judged
    /// invisible back in. The initiative track's own hardware line says what that costs:
    /// "1920x1080 → 1201x175 px … rejected 24 culled/disabled, 65 faint" for the fit, and one line
    /// later "16 text line(s) OUTSIDE its fitted host rect … 1293x294 px, centred at (46,-59)" for
    /// the plate — 119 px of extra height, almost all of it DOWNWARD, i.e. half a plate of empty
    /// passthrough room hanging under a portrait row that is only 172 px tall.</para>
    ///
    /// <para>The plate keeps its own, STRICTER extra rule on top of this one (clipped text is
    /// declined outright rather than clamped — see <c>MrBacking.IsClipped</c>); what it may never do
    /// again is accept something the FIT rejected. One deliberate ORDER difference against
    /// <see cref="TryMeasureContent"/>'s loop, which is behaviour-neutral because both are
    /// unconditional rejects: the cue-art NAME check runs AFTER the visibility test here.
    /// <c>Object.name</c> allocates a managed string on every read, the fit runs ~12x/second and
    /// only while a fit is armed, but the plate sweep runs per panel per FRAME — so the invisible
    /// rows must be rejected without ever touching it.</para>
    /// </summary>
    internal static bool CountsAsFitContent(ConvertedPanel? panel, Graphic? g)
    {
        if (panel == null || panel.HostRect == null || g == null)
            return false;
        if (!TryGetVisibleHostRect(panel, g, out _, out _))
            return false;
        // Mod-owned cue art (focus rings, frames, tints) is a PRESENTATION overlay on the game's
        // content, not content — the identical skip TryMeasureContent applies, and for the identical
        // reason (a breathing FocusRing must not size anything).
        return !g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Open an EXTERNAL run of <see cref="CountsAsFitContent"/> queries (the MR plate sweep asks
    /// once per panel per frame). <see cref="ClipperMemo"/> and <see cref="AuthoredOffsetMemo"/> are
    /// per-MEASURE-PASS caches keyed by <see cref="Transform"/>, and a fit pass clears them at its
    /// own start because transforms move between passes; an outside caller must do the same or it
    /// would read answers cached against a layout that has since moved — and, with the initiative
    /// fit deliberately DISARMED after its one applied re-fit, no fit pass may run for minutes to
    /// clear them for it. Two Dictionary.Clear() on caches that hold at most one panel's transforms.
    /// </summary>
    internal static void BeginContentQuery()
    {
        ClipperMemo.Clear();
        AuthoredOffsetMemo.Clear();
    }

    /// <summary>
    /// Per-pass memo of <see cref="AuthoredOffset"/> — the host-local position of a transform's
    /// local origin with every <c>localScale</c> in the chain treated as 1. Siblings share their
    /// whole ancestor chain, so the walk runs once per transform per measure pass. Cleared with
    /// <see cref="ClipperMemo"/> at the start of every pass (transforms move between passes).
    /// </summary>
    private static readonly Dictionary<Transform, Vector2> AuthoredOffsetMemo = new(64);

    /// <summary>
    /// Host-local position of <paramref name="t"/>'s local origin as the LAYOUT placed it: the sum
    /// of the <c>localPosition</c> of <paramref name="t"/> and all its ancestors up to (excluding)
    /// the host, with every intermediate scale treated as 1. WHY that is the right formula: with an
    /// identity rotation (uGUI, and the conversion forces the target's) a point maps into its
    /// parent as <c>localPosition + scale × point</c>, and a RectTransform's <c>localPosition</c>
    /// is computed from anchors/pivot — which no ancestor's SCALE influences. Dropping the scale
    /// factor therefore reconstructs exactly the geometry the layout produced, i.e. the geometry
    /// the window renders at once a scale-animating ancestor reaches 1.
    /// </summary>
    private static Vector2 AuthoredOffset(ConvertedPanel panel, Transform? t)
    {
        if (t == null || ReferenceEquals(t, panel.HostRect))
            return Vector2.zero;
        if (AuthoredOffsetMemo.TryGetValue(t, out Vector2 memo))
            return memo;
        Vector2 sum = (Vector2)t.localPosition + AuthoredOffset(panel, t.parent);
        AuthoredOffsetMemo[t] = sum;
        return sum;
    }

    // ROUND 5's ClampAuthoredToCanvasHeight is RETIRED with the authored-union fit source it served.
    // The design-height cap now lands on the COMMITTED size (below) and on the target's own rect
    // (ReassertConversionFrame) — the two places that actually decide what the host becomes.

    /// <summary>
    /// ROUND 7: cap the COMMITTED fit height of the full-screen-menu family to the same canvas
    /// design height <see cref="Convert"/> capped the captured size to. See the call site for why
    /// the Convert-time cap alone stopped holding (the game grows the window's own rect afterwards,
    /// so the target-frame clamp no longer bounds the fit). The cap is the height the game itself
    /// never draws past, so this can only ever remove empty space. No-op for every other family and
    /// for a fit already inside the cap, so nothing else moves by a pixel.
    /// </summary>
    private static void ClampFittedHeightToCanvas(ConvertedPanel panel, ref Vector2 size)
    {
        if (!panel.FitHeightCapped || panel.Target == null)
            return;
        float cap = ResolveStableHeightCap(panel.Target, panel.FitHeightCapName, out string source);
        if (cap <= 1f || size.y <= cap + 0.5f)
            return;
        s_lastFittedHeightCap = $"fitted height {size.y:F0} -> {cap:F0} px via {source}";
        size.y = cap;
    }

    /// <summary>Diagnostic for the fit log: what <see cref="ClampFittedHeightToCanvas"/> did on this
    /// pass (empty when it did nothing). Cleared by the measure that reports it.</summary>
    private static string s_lastFittedHeightCap = string.Empty;

    /// <summary>Authored (scale-neutral) host-local rect of <paramref name="rt"/> — its own
    /// <see cref="RectTransform.rect"/> placed at <see cref="AuthoredOffset"/>.</summary>
    private static void AuthoredHostRect(ConvertedPanel panel, RectTransform rt,
        out Vector2 min, out Vector2 max)
    {
        Rect r = rt.rect;
        Vector2 offset = AuthoredOffset(panel, rt);
        min = new Vector2(r.xMin, r.yMin) + offset;
        max = new Vector2(r.xMax, r.yMax) + offset;
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

    /// <summary>Largest contributor of the last measure pass — the probe
    /// <see cref="LogContentChain"/> walks up from.</summary>
    private static Graphic? s_lastTopGraphic;

    private static readonly string[] MeasureTopNames = new string[MeasureTopCount];
    private static readonly Vector4[] MeasureTopRects = new Vector4[MeasureTopCount];
    private static readonly float[] MeasureTopAreas = new float[MeasureTopCount];
    private static int s_lastRejectCulled, s_lastRejectFaint, s_lastRejectEmpty, s_lastRejectClipped;
    private static Vector2 s_lastUnionMin, s_lastUnionMax;
    private static bool s_lastFrameClamped;

    // Round 4 measure diagnostics: the LIVE (rendered) union, the AUTHORED (scale-neutral) union,
    // the area-weighted live/authored ratio, and whether the ratio declared a show animation in
    // flight — i.e. which of the two unions the fit actually used. These three numbers are what a
    // hardware log needs to answer "cold open vs warm open" outright.
    private static Vector2 s_lastLiveUnionMin, s_lastLiveUnionMax;
    private static Vector2 s_lastAuthoredUnionMin, s_lastAuthoredUnionMax;
    private static Vector2 s_lastMeasureRatio = Vector2.one;
    private static bool s_lastMeasureAnimating;

    /// <summary>Size/center the LAST successful <see cref="TryMeasureContent"/> returned (padded,
    /// frame-clamped) — reported verbatim by the fit summary line.</summary>
    private static Vector2 s_lastMeasureSize, s_lastMeasureCenter;

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
          .Append("; top (rendered rects): ");
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
        // Round 4: live vs authored. On a settled window these two are the same box and the ratio
        // is 1.00 — anything else names a show animation and says which union the fit used.
        sb.Append("; live ")
          .Append((s_lastLiveUnionMax.x - s_lastLiveUnionMin.x).ToString("F0")).Append('x')
          .Append((s_lastLiveUnionMax.y - s_lastLiveUnionMin.y).ToString("F0"))
          .Append("px vs authored ")
          .Append((s_lastAuthoredUnionMax.x - s_lastAuthoredUnionMin.x).ToString("F0")).Append('x')
          .Append((s_lastAuthoredUnionMax.y - s_lastAuthoredUnionMin.y).ToString("F0"))
          .Append("px at (").Append(s_lastAuthoredUnionMin.x.ToString("F0")).Append(',')
          .Append(s_lastAuthoredUnionMin.y.ToString("F0")).Append("), rendered/authored ratio ")
          .Append(s_lastMeasureRatio.x.ToString("F2")).Append('/')
          .Append(s_lastMeasureRatio.y.ToString("F2"))
          // Round 7: the fit ALWAYS uses the live union now. A ratio away from 1 no longer switches
          // the basis — it only says "this window is not at its final geometry yet", which is what
          // the settle gate reads.
          .Append(s_lastMeasureAnimating
              ? " → NOT AT ITS AUTHORED GEOMETRY (settle gate holds; the fit still uses the LIVE union — what the player sees)"
              : " → settled, live == authored");
        // Round 5/7: state whether the canvas design-height cap had to bound this measurement.
        if (s_lastFittedHeightCap.Length > 0)
        {
            sb.Append(" [").Append(s_lastFittedHeightCap).Append(']');
            s_lastFittedHeightCap = string.Empty;
        }
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
        // Round 4 (show-animation blind spot): the authored union runs alongside the live one.
        Vector2 aMinAll = new(float.MaxValue, float.MaxValue);
        Vector2 aMaxAll = new(float.MinValue, float.MinValue);
        int authoredContributors = 0;
        float ratioWeight = 0f, ratioSumX = 0f, ratioSumY = 0f;

        s_lastRejectCulled = s_lastRejectFaint = s_lastRejectEmpty = s_lastRejectClipped = 0;
        for (int i = 0; i < MeasureTopCount; i++)
        {
            MeasureTopAreas[i] = 0f;
            MeasureTopNames[i] = string.Empty;
        }
        s_lastFrameClamped = false;

        ClipperMemo.Clear();
        AuthoredOffsetMemo.Clear();
        GraphicScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            // Mod-owned cue art (focus rings, frames, tints) is a PRESENTATION overlay on the
            // game's content, not content. It also BREATHES — Board.FocusCue pulses a ring's
            // scale — so measuring it makes the union oscillate and re-place the whole panel
            // every few frames. Hardware log 2026-08-08: 26 applied re-fits of
            // Panel_InitiativeTrack whose union bottom edge was 'GloomhavenVR.FocusRing' /
            // 'GloomhavenVR.SelectionRing' caught at different points of their swell, host
            // height oscillating 182/186/188/190 px — which the user felt as the portraits
            // stepping up and down. The panel must be sized by what the GAME draws.
            if (g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                continue;
            if (!TryGetVisibleHostRect(panel, g, out Vector2 gMin, out Vector2 gMax,
                    out Vector2 aMin, out Vector2 aMax))
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

            // Authored union + the per-graphic live/authored size ratio (area-weighted, so the
            // panel-wide answer is dominated by the containers that actually span the window and
            // not by a dozen tiny icons). Graphics whose authored rect is unusable, or which are
            // scaled to nothing on purpose, sit this out — see AuthoredCollapsedRatio.
            float aW = aMax.x - aMin.x, aH = aMax.y - aMin.y;
            if (aW < 0.5f || aH < 0.5f)
                continue;
            float rx = (gMax.x - gMin.x) / aW, ry = (gMax.y - gMin.y) / aH;
            if (rx < AuthoredCollapsedRatio || ry < AuthoredCollapsedRatio)
                continue;
            float weight = aW * aH;
            ratioWeight += weight;
            ratioSumX += rx * weight;
            ratioSumY += ry * weight;
            aMinAll = Vector2.Min(aMinAll, aMin);
            aMaxAll = Vector2.Max(aMaxAll, aMax);
            authoredContributors++;
        }
        GraphicScratch.Clear();
        s_lastMeasureGraphics = contributing;
        s_lastLiveUnionMin = min;
        s_lastLiveUnionMax = max;

        // Is a show animation in flight? The area-weighted ratio between what is RENDERED and what
        // the layout AUTHORED is the answer, and it is the whole round-4 fix: a cold ESC menu reads
        // ~0.21 x / ~0.27 y here for a full second, with a perfectly steady live measurement.
        s_lastMeasureRatio = Vector2.one;
        s_lastMeasureAnimating = false;
        // Log-safe: an empty authored union would otherwise print float sentinels.
        bool authoredUsable = authoredContributors > 0 && aMaxAll.x > aMinAll.x && aMaxAll.y > aMinAll.y;
        s_lastAuthoredUnionMin = authoredUsable ? aMinAll : Vector2.zero;
        s_lastAuthoredUnionMax = authoredUsable ? aMaxAll : Vector2.zero;
        if (authoredUsable && ratioWeight > 0f
            && aMaxAll.x - aMinAll.x >= 32f && aMaxAll.y - aMinAll.y >= 32f)
        {
            s_lastMeasureRatio = new Vector2(ratioSumX / ratioWeight, ratioSumY / ratioWeight);
            s_lastMeasureAnimating = Mathf.Abs(s_lastMeasureRatio.x - 1f) > ShowAnimationScaleTol
                                     || Mathf.Abs(s_lastMeasureRatio.y - 1f) > ShowAnimationScaleTol;
        }
        // ROUND 7 — THE AUTHORED UNION IS NO LONGER A FIT SOURCE, ONLY A DIAGNOSTIC AND A GATE.
        //
        // Rounds 4-6 switched the fit to the AUTHORED union whenever the two disagreed, on the
        // theory that it was "the geometry the window ends at". The ModBuild 23 hardware log
        // refutes that outright: the rendered rects sat at host-local x ≈ 621 while the authored
        // union was centred on 0, and a pure scale can never move content 621 px sideways. The
        // authored construction erases EVERY intermediate localScale, including ones that are real
        // — and the same log finally named the real one: the conversion TARGET's own
        // `localScale 0.14` (see ReassertConversionFrame). Fitting the authored phantom is what
        // produced a correctly-sized host around content rendering at 14 % somewhere else: the
        // reported "big frame, thin strip of content, sitting further back".
        //
        // So there is ONE basis now — the LIVE one, i.e. what the player actually sees. The
        // authored union is still measured, because the RATIO between them is the cheapest possible
        // "is this window at its final geometry yet?" signal and the settle gate uses it (a window
        // whose rendered geometry is not its authored geometry has not settled). It just never
        // decides the rect any more. The worst case is now a host that hugs whatever is visible —
        // wrong-sized, but never a frame around content that is somewhere else.
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
            // Round 7: ONE basis. The frame is the target's LIVE rect (world corners → host-local),
            // exactly like the content it bounds — the round-4 authored-frame branch is gone with
            // the authored union it existed to support.
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

        Vector2 unionSize = sz;
        sz += Vector2.one * (2f * FitContentPaddingPx);
        sz.x = Mathf.Min(sz.x, frameMax.x - frameMin.x);
        sz.y = Mathf.Min(sz.y, frameMax.y - frameMin.y);
        // ROUND 7 ("vertikal zu lang", user, ModBuild 23): the full-screen-menu family's host
        // height is capped to the canvas DESIGN height at Convert — but only there. The game's
        // layout then grows the window's own rect (the cold open measured a 1920x2040 target frame
        // where Convert had pinned 1920x1080), so the frame clamp above stopped bounding anything
        // and the committed height leaked past the cap (412x1104 cold versus 412x1080 warm — and
        // far worse whenever the union itself is tall). The cap belongs on the COMMITTED size, not
        // only on the captured one: it is the same number Convert used, applied to the same family,
        // at the one point that decides what the host actually becomes.
        ClampFittedHeightToCanvas(panel, ref sz);

        size = sz;
        center = (min + max) * 0.5f;
        // What the padding (and the clamps above) actually left around the union, PER SIDE — the
        // number a seat solved from the host rect's EDGE has to subtract to land on the content.
        // Derived, never assumed: the frame clamp and the canvas height cap can both eat into it.
        s_lastMeasurePadding = Vector2.Max(Vector2.zero, (sz - unionSize) * 0.5f);
        s_lastMeasureSize = size;
        s_lastMeasureCenter = center;
        return true;
    }

    /// <summary>Per-side slack the last <see cref="TryMeasureContent"/> pass left between the
    /// visible-content union and the host size it produced (uGUI px). See
    /// <see cref="FitContentPaddingPx"/> and <see cref="ConvertedPanel.FitContentPadding"/>.</summary>
    private static Vector2 s_lastMeasurePadding;

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
        // Round 7: the LARGEST contributor is the probe the ancestor/depth dump walks up from —
        // the element that actually spans the measured union, so its chain is the one that decides
        // where and how big the window renders.
        if (slot == 0)
            s_lastTopGraphic = g;
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
        // Published for the surfaces that pin this host by a rect EDGE (the decision area's
        // top-down layout) — before the tolerance no-op below, because a panel sitting inside the
        // 2 % dirty band still owes them the slack its rect carries.
        panel.FitContentPadding = s_lastMeasurePadding;

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
        // The very first fit (FitMeasuredOnce false) is never damped, and neither is a panel that
        // opted out (ConvertedPanel.FitShrinkImmediate — the decision drawer, whose collapse must
        // land in the frame it happens; that panel is held frozen on LAYOUT TRUTH instead, so no
        // oscillation can reach this path).
        bool growth = size.x > host.width + tolX || size.y > host.height + tolY;
        if (panel.FitMeasuredOnce && !growth && !force && !panel.FitShrinkImmediate)
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
        //
        // ROUND 6: the shift and the resize INTERACT, so they are applied as a converging
        // fixed point instead of a single open-loop step — see ApplyFitConverging.
        ApplyFitConverging(panel, root, ref size, ref center, out string applyTrace);
        panel.FitOneShotApplied = true; // item 1: a real resize happened — owner may re-derive its scale
        // Round 3: every APPLIED fit advances the generation. ModalFallback's one-shot followers
        // (5b board-scale re-derivation, 5b-pose re-place) latch on the generation instead of a
        // plain bool, so a VERIFY correction re-runs them against the corrected geometry — without
        // it the window would keep the scale and pose derived from the rect that was just proven
        // wrong.
        panel.FitAppliedGeneration++;
        VRLog.Info("WorldUI", $"Host rect fit '{panel.HostGo.name}': " +
                              $"{host.width:F0}x{host.height:F0} → {size.x:F0}x{size.y:F0} px " +
                              $"(content offset {center.x:F0},{center.y:F0}) — {DescribeLastMeasure()} — " +
                              $"{applyTrace} — {DescribeTargetFrame(panel)}.");
        return true;
    }

    /// <summary>
    /// Iterations the fit APPLY may take to converge inside one call. Three is generous: the
    /// coupling below is a single linear feedback term, so one correction pass already lands it;
    /// the third only exists so a pathological layout still terminates with a logged verdict.
    /// </summary>
    private const int FitApplyIterations = 3;

    /// <summary>
    /// ROUND 6 — WHY THE FIT NOW ITERATES (proof, ModBuild 22 hardware log, cold ESC menu).
    ///
    /// The old apply was open-loop: measure the content, then in one step shift the target by
    /// <c>-center</c> AND write the new host size. That is only correct if resizing the host leaves
    /// the content where it was. It does not:
    ///
    ///   pass 2 measured the content at offset (-770,-12) and resized the host 1920 → 405 px;
    ///   pass 3 re-measured the SAME content at (+758,0) — the union had moved +1528 px while the
    ///   shift we applied was +770. The residual is +757.5 px = HALF the 1515 px width change,
    ///   i.e. exactly what a target sitting on a non-centred anchor of the host does when the host
    ///   rect shrinks (its anchor reference point moves from -960 to -202.5).
    ///
    /// So the correction was computed in the geometry BEFORE the resize and applied to the geometry
    /// AFTER it, and each pass re-created an error of the same order it had just removed. Three
    /// passes on the cold open burned both verify corrections and still left the content off-centre.
    ///
    /// The fix is to close the loop inside the call: write the size, flush the layout, RE-measure in
    /// the state that now exists, and derive the shift from THAT measurement — repeating until the
    /// re-measured content is genuinely centred in the host it is being fitted to. Every iteration
    /// is derived from a fresh measurement, so the operation is idempotent by construction (a
    /// converged panel measures ~0 offset and exits on the first check) and cannot accumulate error
    /// across passes or across measurement bases. All of it happens inside one frame, before
    /// anything renders — a hidden window never shows an intermediate step, and a visible one
    /// (growth re-fit) still moves exactly once.
    ///
    /// <paramref name="size"/>/<paramref name="center"/> are updated to the last applied values so
    /// the caller logs what actually landed, and <paramref name="trace"/> reports every iteration.
    /// </summary>
    private static void ApplyFitConverging(ConvertedPanel panel, RectTransform root,
        ref Vector2 size, ref Vector2 center, out string trace)
    {
        var sb = new System.Text.StringBuilder(160);
        sb.Append("apply: ");
        // Kill the coupling at its source where we are allowed to: Convert pinned the target's
        // whole frame precisely so the host rect could be resized underneath it and so the measure
        // reads real geometry. The hardware log proves the game re-drives it.
        if (ReassertConversionFrame(panel, out string frameNote))
            sb.Append(frameNote).Append("; ");

        for (int pass = 1; ; pass++)
        {
            // The whole reason this loop exists is the RESIZE: shifting the target is a rigid move
            // that cannot change what the next measurement reports, while resizing the host can
            // (that is the coupling the ModBuild 22 log proved). When the size is already right,
            // the single shift is exact — skip the re-measure and its forced canvas flush, which is
            // the expensive part of an applied fit.
            bool resized = Mathf.Abs(panel.HostRect.rect.width - size.x) > 0.5f
                           || Mathf.Abs(panel.HostRect.rect.height - size.y) > 0.5f;
            panel.HostRect.sizeDelta = size;
            panel.Target.anchoredPosition -= center;
            sb.Append('#').Append(pass).Append(" host=").Append(size.x.ToString("F0")).Append('x')
              .Append(size.y.ToString("F0")).Append(" shift=").Append((-center.x).ToString("F0"))
              .Append(',').Append((-center.y).ToString("F0"));

            if (!resized)
            {
                sb.Append(" → rigid re-centre only (host size unchanged, nothing can have moved)");
                break;
            }
            if (pass >= FitApplyIterations)
            {
                sb.Append(" (iteration cap reached — see the verify watch)");
                break;
            }

            // Re-measure the state we just created. The flush is what makes it honest: a
            // render-hidden panel gets no uGUI service, so without it the re-measure would read
            // the pre-resize geometry and always report "converged".
            FlushPendingLayout(panel);
            if (!TryMeasureContent(panel, root, out Vector2 nextSize, out Vector2 nextCenter))
            {
                sb.Append(" → content unmeasurable after the resize; kept");
                break;
            }
            float tolX = Mathf.Max(Mathf.Max(size.x, nextSize.x) * FitChangeFraction, 2f);
            float tolY = Mathf.Max(Mathf.Max(size.y, nextSize.y) * FitChangeFraction, 2f);
            if (Mathf.Abs(nextCenter.x) <= tolX && Mathf.Abs(nextCenter.y) <= tolY
                && Mathf.Abs(nextSize.x - size.x) <= tolX && Mathf.Abs(nextSize.y - size.y) <= tolY)
            {
                sb.Append(" → CONVERGED (re-measured ").Append(nextSize.x.ToString("F0")).Append('x')
                  .Append(nextSize.y.ToString("F0")).Append(" at ").Append(nextCenter.x.ToString("F0"))
                  .Append(',').Append(nextCenter.y.ToString("F0")).Append(')');
                break;
            }
            sb.Append(" → moved to ").Append(nextCenter.x.ToString("F0")).Append(',')
              .Append(nextCenter.y.ToString("F0")).Append(" (").Append(nextSize.x.ToString("F0"))
              .Append('x').Append(nextSize.y.ToString("F0")).Append("); ");
            size = nextSize;
            center = nextCenter;
        }
        trace = sb.ToString();
    }

    /// <summary>
    /// ROUND 7 — THE CONVERSION FRAME IS AN INVARIANT, AND IT HAS TO BE MAINTAINED, NOT ASSUMED.
    ///
    /// <see cref="Convert"/> pins six properties on the target the moment it adopts it: centred
    /// anchors and pivot, <c>localScale = 1</c>, identity rotation, <c>localPosition.z = 0</c> and
    /// (for the full-screen-menu family) a height-capped <c>sizeDelta</c>. Everything downstream
    /// assumes they hold — the host can only be resized underneath a target whose position does not
    /// depend on the host rect, and the content measure only means anything if the subtree renders
    /// at the scale it was authored at. The game re-drives them, and the hardware proved it twice:
    ///
    ///   ModBuild 22: anchors found at (0,0)..(0,0) — the host resize then dragged the content by
    ///                half the width change on every applied fit.
    ///   ModBuild 23: `target frame 1920x2040 px … localScale 0.14` — the ENTIRE converted subtree
    ///                was rendering at 14 %, which is the whole "rendered/authored 0.21/0.15" the
    ///                last three rounds mistook for a show animation in flight, and the rect had
    ///                grown to 2040 px where Convert had pinned 1080 ("vertikal zu lang").
    ///
    /// A target at 14 % renders its content small and displaced (the scale is about the pivot, and
    /// the content sits off-centre inside the target), which reads exactly as the user described it:
    /// "als sei der Inhalt deutlich weiter hinten und verschoben". Restoring the frame is not a new
    /// visual policy — it is the policy Convert has always applied; these windows have never been
    /// meant to render at anything but scale 1 inside their host.
    ///
    /// Every correction is logged the first time it fires per panel, with the value found, so the
    /// log names the drift instead of implying it. Reversibility is untouched: <see cref="Release"/>
    /// restores the ORIGINALS captured at Convert. Returns true (with a log-ready note) only when
    /// something actually had to be corrected — a healthy panel costs six compares and no writes.
    /// </summary>
    /// <param name="includeHeightCap">Re-pin the target's own rect height to the canvas design
    /// height too. TRUE on the paths that own the window's geometry anyway (pre-reveal maintenance
    /// and every applied fit); FALSE for the cheap steady-state guard, because the game may drive
    /// that rect from a layout component and re-writing it every frame forever would be a fight,
    /// not a fix — the committed host height is capped independently by
    /// <see cref="ClampFittedHeightToCanvas"/>.</param>
    private static bool ReassertConversionFrame(ConvertedPanel panel, out string note,
        bool includeHeightCap = true)
    {
        note = string.Empty;
        RectTransform? t = panel.Target;
        if (t == null)
            return false;

        var sb = new System.Text.StringBuilder(96);
        var half = new Vector2(0.5f, 0.5f);

        // 1. SCALE — the round-7 headline. A drifted scale corrupts the measure itself (the union
        //    is read through world corners), so it is corrected before anything else looks at it.
        Vector3 scale = t.localScale;
        if (Mathf.Abs(scale.x - 1f) > 0.001f || Mathf.Abs(scale.y - 1f) > 0.001f
            || Mathf.Abs(scale.z - 1f) > 0.001f)
        {
            t.localScale = Vector3.one;
            sb.Append($"localScale was ({scale.x:F2},{scale.y:F2},{scale.z:F2}) → 1");
        }

        // 2. ROTATION + 3. DEPTH — a converted panel is a flat plane coplanar with its host. A
        //    z-displaced or tilted target renders at a different depth than the frame, X and grab
        //    bar that follow the host: the same "content sits further back" reading, and on a
        //    stereo rig it is the disparity that makes it obvious.
        if (Quaternion.Angle(t.localRotation, Quaternion.identity) > 0.05f)
        {
            sb.Append(sb.Length > 0 ? "; " : string.Empty)
              .Append($"localRotation was {t.localRotation.eulerAngles} → identity");
            t.localRotation = Quaternion.identity;
        }
        Vector3 lp = t.localPosition;
        if (Mathf.Abs(lp.z) > 0.01f)
        {
            sb.Append(sb.Length > 0 ? "; " : string.Empty).Append($"localPosition.z was {lp.z:F1} → 0");
            lp.z = 0f;
            t.localPosition = lp;
        }

        // 4. ANCHORS/PIVOT (ModBuild 22): with the anchors collapsed to the centre the target's
        //    position is independent of the host rect, which is what lets the fit resize the host
        //    without dragging the content. Position and size are preserved exactly, so this is a
        //    pure re-parametrisation: nothing on screen moves.
        if ((t.anchorMin - half).sqrMagnitude > 1e-6f || (t.anchorMax - half).sqrMagnitude > 1e-6f)
        {
            Vector2 aMin = t.anchorMin, aMax = t.anchorMax;
            Vector2 keepSize = t.rect.size;
            Vector3 keepPos = t.localPosition;
            t.anchorMin = half;
            t.anchorMax = half;
            t.sizeDelta = keepSize;
            t.localPosition = keepPos;
            sb.Append(sb.Length > 0 ? "; " : string.Empty)
              .Append($"anchors had drifted to ({aMin.x:F2},{aMin.y:F2})..({aMax.x:F2},{aMax.y:F2}) → " +
                      "re-centred (position/size preserved)");
        }

        // 5. HEIGHT CAP: Convert pinned this family's target rect to the canvas design height so it
        //    would also be the frame the fit clamps to. The game's layout grew it to 2040 px, which
        //    is how the over-height leaked back in. Re-pin it (width and position untouched).
        if (includeHeightCap && panel.FitHeightCapped)
        {
            float cap = ResolveStableHeightCap(t, panel.FitHeightCapName, out _);
            if (cap > 1f && t.rect.height > cap + 0.5f)
            {
                float had = t.rect.height;
                t.sizeDelta = new Vector2(t.sizeDelta.x, t.sizeDelta.y - (t.rect.height - cap));
                sb.Append(sb.Length > 0 ? "; " : string.Empty)
                  .Append($"target rect height was {had:F0} → capped to {cap:F0}");
            }
        }

        if (sb.Length == 0)
            return false;
        note = "conversion frame RE-ASSERTED: " + sb;
        if (!panel.FrameDriftLogged)
        {
            panel.FrameDriftLogged = true;
            VRLog.Warn("WorldUI", $"MODAL FIT: '{panel.HostGo.name}' {note}. The game re-drove the frame " +
                                  "Convert pinned; everything the content fit measures is expressed in that " +
                                  "frame, so it is restored before the measure is trusted. (Reported once " +
                                  "per panel; the fit lines carry the per-apply note.)");
        }
        return true;
    }

    /// <summary>Fit diagnostic: the conversion target's own frame — the basis every measurement is
    /// expressed in. A cold and a warm open MUST show the same anchors/pivot here; the rect and
    /// anchored position say where the measured content sits inside it.</summary>
    private static string DescribeTargetFrame(ConvertedPanel panel)
    {
        RectTransform? t = panel.Target;
        if (t == null)
            return "target gone";
        Rect r = t.rect;
        Vector2 ap = t.anchoredPosition;
        return $"target frame {r.width:F0}x{r.height:F0} px at anchored ({ap.x:F0},{ap.y:F0}), " +
               $"anchors ({t.anchorMin.x:F2},{t.anchorMin.y:F2})..({t.anchorMax.x:F2},{t.anchorMax.y:F2}), " +
               $"pivot ({t.pivot.x:F2},{t.pivot.y:F2}), localScale {t.localScale.x:F2}";
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

        panel.FitOpenIndex = OneShotOpens.TryGetValue(key, out int opens) ? opens + 1 : 1;
        OneShotOpens[key] = panel.FitOpenIndex;

        panel.FitVerifyPending = true;
        panel.FitVerifyProven = matchesMemory;
        panel.FitVerifyUntil = now + FitVerifyWatchSeconds;
        // Round 4: a window whose show animation is still running has not shown its final geometry
        // yet, so its rect must not lock on the normal watch — the watch is extended (invisibly,
        // cheaply) up to this hard cap while the measure keeps reporting an animation in flight.
        panel.FitVerifyHardUntil = now + FitVerifyAnimatedWatchSeconds;
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
        // BOTH hides count here, and for exactly the reason stated above — this flag does NOT gate
        // whether the verify runs (it must keep running: the row's geometry has to be correct the
        // moment it comes back, which is also why DecisionDockSurface deliberately keeps Place()
        // going while focus-hidden and re-places on the un-hide tick). It selects the MEASUREMENT
        // MODE: a surface-owned focus hide disables the very same canvases, so the uGUI pipeline
        // does not service them either and the measure would read stale geometry without the
        // explicit layout flush below. Treating OwnerRenderHidden as "visible" would silently
        // re-introduce the round-2 stale-measure bug for every focus-hidden row.
        bool hidden = panel.RenderHidden || panel.OwnerRenderHidden;
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

        // Round 4: the animation is not over — the rect stands at the AUTHORED geometry and must
        // keep its verify open until the rendered content can confirm it. Extending here (not at
        // arm time) means the extension lasts exactly as long as the animation does, and the hard
        // cap keeps a permanently animating window from holding a fit open forever. The REVEAL is
        // untouched: it is released by FitVerifyHoldRevealUntil / RevealDeadline, never by this.
        if (s_lastMeasureAnimating && !panel.FitSawShowAnimation)
        {
            panel.FitSawShowAnimation = true;
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' verify sees the game's SHOW ANIMATION " +
                                  $"still in flight (rendered/authored {s_lastMeasureRatio.x:F2}/" +
                                  $"{s_lastMeasureRatio.y:F2}) — the committed rect is the AUTHORED geometry, " +
                                  "and the watch stays open until the animation lands so the rendered content " +
                                  "can confirm (or correct) it once.");
        }
        if (s_lastMeasureAnimating && now < panel.FitVerifyHardUntil)
        {
            panel.FitVerifyUntil = Mathf.Min(now + FitVerifyWatchSeconds, panel.FitVerifyHardUntil);
            expired = false;
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
            // Round 4: even a PROVEN rect does not lock while the show animation is still running —
            // the rendered content has not corroborated anything yet. `expired` is the bound (the
            // hard cap above forces it eventually), so this can never wedge.
            if (panel.FitVerifyStableCount >= FitVerifyStableChecks
                && ((panel.FitVerifyProven && !s_lastMeasureAnimating) || expired))
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
                                  $"window {(panel.RenderHidden ? "still render-hidden behind the reveal gate" : panel.OwnerRenderHidden ? "render-hidden by its surface (character focus)" : "already visible")}).");
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
        string key = panel.HostGo.name;
        bool hadMemory = LastOneShotFits.TryGetValue(key, out Vector4 previous);
        LastOneShotFits[key] = new Vector4(host.width, host.height,
            panel.Target != null ? panel.Target.anchoredPosition.x : 0f,
            panel.Target != null ? panel.Target.anchoredPosition.y : 0f);
        string line = $"MODAL WINDOW: '{key}' one-shot host rect LOCKED at " +
                      $"{host.width:F0}x{host.height:F0} px — {outcome}.";
        if (warn)
            VRLog.Warn("WorldUI", line);
        else
            VRLog.Info("WorldUI", line);
        LogFitSummary(panel, outcome, hadMemory ? previous : (Vector4?)null);
    }

    /// <summary>
    /// THE ONE LINE THE NEXT HARDWARE LOG IS READ FROM (user requirement, round 4). Everything the
    /// cold-open question needs, per open, in one place and in one order: which open of this window
    /// this is, the final host rect and content offset, the measured LIVE and AUTHORED unions with
    /// the rendered/authored ratio that decides between them, the contributor count and the three
    /// largest contributors by name and rect, what the verify decided and why, and what the
    /// PREVIOUS open of the same window committed. Comparing open 1 against open 2 is then a diff
    /// of two lines instead of a reconstruction from a dozen.
    /// </summary>
    private static void LogFitSummary(ConvertedPanel panel, string verdict, Vector4? previous)
    {
        if (panel.HostRect == null || panel.HostGo == null)
            return;
        Rect host = panel.HostRect.rect;
        string prev = previous.HasValue
            ? $"previous open committed {previous.Value.x:F0}x{previous.Value.y:F0} px " +
              $"(target at {previous.Value.z:F0},{previous.Value.w:F0})"
            : "no previous open in this session (this IS the cold one)";
        // Round 7: one chain dump per open at the LOCK, so a hardware log always carries the
        // cold/warm comparison the coordinator asked for — the cold open's dump (emitted when the
        // drift detector tripped) next to a settled one from the same window.
        LogContentChain(panel, "at the one-shot lock (settled reference)");
        VRLog.Info("WorldUI", $"MODAL FIT SUMMARY '{panel.HostGo.name}' open #{panel.FitOpenIndex}: " +
                              $"final host {host.width:F0}x{host.height:F0} px, last measured content " +
                              $"{s_lastMeasureSize.x:F0}x{s_lastMeasureSize.y:F0} px at offset " +
                              $"{s_lastMeasureCenter.x:F0},{s_lastMeasureCenter.y:F0}; " +
                              $"{s_lastMeasureGraphics} contributor(s); {DescribeLastMeasure()}; " +
                              $"show animation seen during this open: {(panel.FitSawShowAnimation ? "YES" : "no")}; " +
                              $"verify: {verdict}; {prev}.");
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

        // THE BASIS GATE (round 6, re-justified in round 7). A measurement whose RENDERED geometry
        // is not its AUTHORED geometry must never certify a fit, however still it looks — the fit
        // centres what it measures, and a subtree rendering at a fraction of its authored size is a
        // window that has not arrived. Round 7 changed what happens NEXT, not the gate: the fit no
        // longer switches to the authored union (that phantom is what put a correct frame around
        // content sitting somewhere else); it holds, the conversion frame is re-asserted — which is
        // what the drift actually was — and the LIVE union is committed either way at the deadline.
        // The cold open is also the only state that passes the strict "absolutely still" tier (a
        // half-built, drifted layout does not move at all), so this is the discriminator that tier
        // lacked.
        bool atAuthoredGeometry = !s_lastMeasureAnimating;
        Vector2 gateRatio = s_lastMeasureRatio;
        TickGeometryDrift(panel, root, atAuthoredGeometry);

        // Tier A (the real gate): nothing left for the layout to apply, no content arrived between
        // the last two checks, and the measurement held — relatively for the historic number of
        // checks AND absolutely still (round 3) for the same number.
        bool settled = atAuthoredGeometry && !layoutWasDirty && sameContent
                       && panel.FitOneShotStableCount >= OneShotSettleChecks
                       && panel.FitSettleStillCount >= OneShotSettleChecks;
        // Tier B (bound): genuinely ANIMATED content (a typewriter story text, a pulsing prompt)
        // can keep dirtying the layout or flipping a graphic in and out indefinitely — never
        // committing would hold the window hidden until the reveal deadline on EVERY open. Once
        // the measured bounds themselves have held steady this much longer, commit anyway: the
        // bounds are what the fit uses, and they stopped moving. Round 6: subject to the same basis
        // gate — "the bounds stopped moving" is worthless while the bounds are a freeze-frame.
        bool boundReached = atAuthoredGeometry
                            && panel.FitOneShotStableCount >= SettleAnimatedFallbackChecks;

        // Reported on the committing line so a hardware log can PROVE cold/warm equality: the
        // first open and every later one must show the same measured size, the same contributor
        // count and the same committed rect — and the rebuild-change counter says whether the
        // cold open needed the flush at all.
        report = $"measured {size.x:F0}x{size.y:F0} px at ({center.x:F0},{center.y:F0}) from " +
                 $"{graphics} visible graphic(s); {panel.FitOneShotStableCount} stable check(s) " +
                 $"({panel.FitSettleStillCount} of them ABSOLUTELY still) of {panel.FitSettleChecks}; " +
                 $"forced rebuild changed the measurement " +
                 $"{panel.FitSettleRebuildChanges}x (this check: {(layoutWasDirty ? "YES" : "no")}" +
                 (settled ? ")" : boundReached ? "; committed on the animated-content bound)" : ")") +
                 $"; geometry basis: {(atAuthoredGeometry ? "RENDERED == AUTHORED (the window is at its real geometry)" : $"rendered/authored {gateRatio.x:F2}/{gateRatio.y:F2} — NOT there yet, gate held")}" +
                 $"; drift streak {panel.FitAnimStalledChecks}" +
                 $", chain dumped: {(panel.FrameChainDumps > 0 ? "yes" : "no")}";

        return settled || boundReached ? SettleResult.Settled : SettleResult.Settling;
    }

    /// <summary>
    /// Consecutive settle checks the rendered/authored ratio may fail to IMPROVE before the window
    /// is declared DRIFTED rather than animating. ~0.14 s at 72 Hz. A healthy show animation moves
    /// the ratio by ~0.05 EVERY frame, so it can never reach this; the hardware cold open sat at a
    /// constant 0.21/0.15 for the entire pre-reveal budget and reaches it in a tenth of a second.
    /// </summary>
    private const int GeometryDriftChecks = 10;

    /// <summary>Ratio improvement (per axis, per check) that counts as PROGRESS — below it nothing
    /// is moving in a way that will land inside the reveal budget.</summary>
    private const float GeometryDriftProgressEpsilon = 0.02f;

    /// <summary>Chain dumps emitted per panel (<see cref="LogContentChain"/>) — it is a multi-line
    /// dump answering a cold/warm comparison, so it is capped rather than throttled.</summary>
    private const int FrameChainDumpCap = 2;

    /// <summary>
    /// ROUND 7 — WHAT REPLACED THE ROUND-6 "LAND THE SHOW ANIMATION" ACTION.
    ///
    /// Round 6 read a constant sub-1 rendered/authored ratio as a stalled LeanTween and sent every
    /// GUIAnimator in the subtree to its finish state. The ModBuild 23 log killed that theory in one
    /// line: 13 animators were sent to their finish state, NONE reported <c>IsPlaying</c>, and the
    /// ratio stayed at 0.21/0.15. It was never a tween — and the same log's frame diagnostic named
    /// the real cause: the conversion target's own <c>localScale 0.14</c>.
    ///
    /// The blanket call is therefore GONE, and not only because it was useless: a menu carries a
    /// dozen animators for buttons, highlights and HIDE transitions, and forcing all of them to
    /// their end state can apply a hide animation's end value just as easily as a show animation's.
    /// It was a scattergun aimed at a symptom.
    ///
    /// What remains is diagnosis plus the correct repair: the drift streak (how long the window has
    /// been away from its authored geometry without improving), the ancestor/depth dump that NAMES
    /// what holds it there, and <see cref="ReassertConversionFrame"/> — which restores the frame
    /// Convert pinned and the game re-drove. All of it while the window is render-hidden, so nothing
    /// the player can see ever moves.
    /// </summary>
    private static void TickGeometryDrift(ConvertedPanel panel, RectTransform root, bool atAuthoredGeometry)
    {
        if (atAuthoredGeometry)
        {
            panel.FitAnimStalledChecks = 0;
            panel.FitAnimLastRatio = s_lastMeasureRatio;
            return;
        }
        Vector2 ratio = s_lastMeasureRatio;
        bool improving = Mathf.Abs(ratio.x - panel.FitAnimLastRatio.x) > GeometryDriftProgressEpsilon
                         || Mathf.Abs(ratio.y - panel.FitAnimLastRatio.y) > GeometryDriftProgressEpsilon;
        panel.FitAnimLastRatio = ratio;
        panel.FitAnimStalledChecks = improving ? 0 : panel.FitAnimStalledChecks + 1;
        // '==' so this fires exactly ONCE per drift episode instead of on every check after it.
        if (panel.FitAnimStalledChecks != GeometryDriftChecks)
            return;

        // 1. SAY WHAT IS HOLDING IT THERE — the evidence the last three rounds had to infer.
        LogContentChain(panel, $"drifted at rendered/authored {ratio.x:F2}/{ratio.y:F2} for " +
                               $"{GeometryDriftChecks} checks without improving");

        // 2. CORRECT IT. The conversion frame is ours to maintain and a drifted one is exactly what
        //    the dump keeps showing; re-asserting it is the repair, not a workaround.
        if (!ReassertConversionFrame(panel, out string note))
            return;
        FlushPendingLayout(panel);
        panel.FitOneShotStableCount = 0;
        panel.FitSettleStillCount = 0;
        panel.FitOneShotStableGraphics = 0;
        panel.FitAnimStalledChecks = 0;
        bool measured = TryMeasureContent(panel, root, out Vector2 size, out Vector2 center);
        VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' was rendering at " +
                              $"{ratio.x:F2}/{ratio.y:F2} of its authored geometry and not converging — " +
                              $"{note}. Re-measured " +
                              (measured
                                  ? $"{size.x:F0}x{size.y:F0} px at ({center.x:F0},{center.y:F0}), new ratio " +
                                    $"{s_lastMeasureRatio.x:F2}/{s_lastMeasureRatio.y:F2}."
                                  : "nothing measurable (the settle gate keeps retrying)."));
    }

    /// <summary>
    /// ROUND 7 INSTRUMENTATION (coordinator request): dump the ancestor chain from the largest
    /// visible content graphic up to the host, so the log NAMES what holds the content at a fraction
    /// of its authored size and away from its frame, instead of us modelling it from two bounding
    /// boxes. Per level: local scale, local position INCLUDING Z, anchors, pivot, sizeDelta and rect
    /// size, plus the running scale product (which IS the rendered/authored ratio). Then the DEPTH
    /// answer the user's description asks for — the content plane's host-local Z against the host
    /// plane, which is 0 by construction — and the clock state, because a paused scaled clock
    /// freezing a tween is the competing hypothesis and this settles it.
    ///
    /// Capped at <see cref="FrameChainDumpCap"/> per panel: it is a multi-line dump answering a
    /// cold/warm comparison, not a per-frame trend.
    /// </summary>
    private static void LogContentChain(ConvertedPanel panel, string when)
    {
        if (panel.FrameChainDumps >= FrameChainDumpCap || panel.HostRect == null || panel.HostGo == null)
            return;
        Graphic? probe = s_lastTopGraphic;
        if (probe == null)
            return;
        panel.FrameChainDumps++;

        var sb = new System.Text.StringBuilder(512);
        sb.Append("MODAL CHAIN DUMP '").Append(panel.HostGo.name).Append("' (").Append(when)
          .Append("), open #").Append(panel.FitOpenIndex).Append(", probe graphic '")
          .Append(probe.name).Append("':");

        // The depth question, answered directly: the host plane is z = 0 by construction, so a
        // non-zero host-local Z here IS the content sitting in front of or behind its own frame
        // (in host pixels — the X, grab bar and depth masks all live on the z = 0 plane).
        var probeRect = (RectTransform)probe.transform;
        probeRect.GetWorldCorners(CornerScratch);
        float zMin = float.MaxValue, zMax = float.MinValue;
        for (int c = 0; c < 4; c++)
        {
            float z = panel.HostRect.InverseTransformPoint(CornerScratch[c]).z;
            zMin = Mathf.Min(zMin, z);
            zMax = Mathf.Max(zMax, z);
        }
        sb.Append(" content plane host-local z ").Append(zMin.ToString("F1")).Append("..")
          .Append(zMax.ToString("F1"))
          .Append(" px (host plane = 0; non-zero = the content is NOT coplanar with its frame)");

        Vector3 product = Vector3.one;
        int level = 0;
        for (Transform? t = probe.transform; t != null && level < 24; t = t.parent, level++)
        {
            Vector3 ls = t.localScale;
            Vector3 lp = t.localPosition;
            sb.Append("\n    [").Append(level).Append("] '").Append(t.name).Append("' scale=(")
              .Append(ls.x.ToString("F3")).Append(',').Append(ls.y.ToString("F3")).Append(',')
              .Append(ls.z.ToString("F3")).Append(") pos=(").Append(lp.x.ToString("F1")).Append(',')
              .Append(lp.y.ToString("F1")).Append(',').Append(lp.z.ToString("F1")).Append(')');
            if (t is RectTransform rt)
            {
                Rect r = rt.rect;
                sb.Append(" rect=").Append(r.width.ToString("F0")).Append('x')
                  .Append(r.height.ToString("F0")).Append(" sizeDelta=(")
                  .Append(rt.sizeDelta.x.ToString("F0")).Append(',')
                  .Append(rt.sizeDelta.y.ToString("F0")).Append(") anchors=(")
                  .Append(rt.anchorMin.x.ToString("F2")).Append(',').Append(rt.anchorMin.y.ToString("F2"))
                  .Append(")..(").Append(rt.anchorMax.x.ToString("F2")).Append(',')
                  .Append(rt.anchorMax.y.ToString("F2")).Append(") pivot=(")
                  .Append(rt.pivot.x.ToString("F2")).Append(',').Append(rt.pivot.y.ToString("F2")).Append(')');
            }
            if (ReferenceEquals(t, panel.Target))
                sb.Append("  <== CONVERSION TARGET (Convert pins scale 1, identity rotation, z 0, centred anchors)");
            if (ReferenceEquals(t, panel.HostRect))
            {
                sb.Append("  <== HOST (walk ends here)");
                break;
            }
            product = new Vector3(product.x * ls.x, product.y * ls.y, product.z * ls.z);
        }
        sb.Append("\n    accumulated scale content→host = (").Append(product.x.ToString("F3")).Append(',')
          .Append(product.y.ToString("F3")).Append(',').Append(product.z.ToString("F3"))
          .Append(") — THIS is the rendered/authored ratio; (1,1,1) means the window renders at the ")
          .Append("size it was authored at.");
        sb.Append("\n    clock: timeScale=").Append(Time.timeScale.ToString("F2"))
          .Append(" deltaTime=").Append(Time.deltaTime.ToString("F4"))
          .Append(" unscaledDeltaTime=").Append(Time.unscaledDeltaTime.ToString("F4"))
          .Append(" (a frozen scaled clock beside a live unscaled one = the game is paused, which ")
          .Append("would freeze any tween that does not ignore time scale).");
        VRLog.Warn("WorldUI", sb.ToString());
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
