using System.Collections.Generic;
using GloomhavenVR.Core;
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
    ///
    /// <para>ModBuild 291 — INTERNAL rather than private, and shared rather than copied. The
    /// liveness rule's dormant WAKE test (<c>ModalFallback.DrawsAnythingScriptSide</c>) asks the same
    /// "is this faint enough to count as invisible?" question of script-side state, and the two must
    /// use ONE floor or the rule that hides a window and the rule that brings it back could disagree
    /// about the same graphic. The VALUE is untouched.</para>
    /// </summary>
    internal const float FitMinAlpha = 0.05f;

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

    /// <summary>ModBuild 449 — how many graphics the EFFECT-QUAD refusal took out of the last
    /// <see cref="TryMeasureContent"/> pass, the family mask it took them under, and the tallest one,
    /// for <see cref="DescribeLastMeasure"/>. Kept beside the other reject counters because it IS one:
    /// the difference is only that this one is the mod's judgement rather than uGUI's, so the line has
    /// to be able to say it out loud. A count of 0 on a panel that still churns is a reading and not a
    /// silence — see the ledger's own prose.</summary>
    private static int s_lastRejectEffect, s_lastEffectMask;
    private static string s_lastEffectTallest = string.Empty;
    private static float s_lastEffectTallestPx;
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
        // ---- ModBuild 449 - THE EFFECT-QUAD LEDGER, AND IT PRINTS THE ZERO. --------------------
        //
        // This path had NO transient test at all before 449: ModBuild 201's exclusion lives in
        // MeasureFixedFitParts, which only fixed-fit panels reach, so `Host rect fit` measured the
        // game's animated decoration on every window that is not one. Two panels paid for that on
        // the ModBuild 448 pair and they are the same defect one panel apart:
        //
        //   'UI Quest Popup'   512x846 px on the tick before the quest confirm was parked, 512x1021
        //                      (union bottom -756) on the tick after it, because the parked toggle
        //                      brought 'Button_FX/UIFX_Wave (1)' with it.
        //   'Panel_ElementBoard'  60x120 -> 60x84 with union (-30,-30)..(30,30) while the FX are
        //                      idle, and 60x120 -> 60x120 with union (-242,-53)..(52,48) while
        //                      'FX/UIFX_Wave (1)' and 'FX/UIFX_Sparks (1)' (106x101 px each) play.
        //                      The 'wird erstellt' caption is NOT the cause of that swing and the
        //                      log says so: 'Creating icon/CreatingText' is 199x50 px at y -25..25
        //                      and 'CreatedText/Text' 284x55 px at y -27..28, both INSIDE the
        //                      element's own 60 px band; they widen the union to x=-242/-340 and the
        //                      width is frame-clamped to 60 in every single sample.
        //
        // THE ZERO IS PRINTED because "no effect quad moved anything" and "the rule never reached
        // one" are the two states a next round has to tell apart, and 448 lost a whole build to
        // exactly that ambiguity.
        sb.Append("; EFFECT-QUAD LEDGER: ").Append(s_lastRejectEffect)
          .Append(" animated effect quad(s) refused from THIS measure");
        if (s_lastEffectMask != 0)
        {
            sb.Append(", from ").Append(TransientFamilies.Describe(s_lastEffectMask));
            if (s_lastEffectTallest.Length > 0)
            {
                sb.Append("; the tallest was '").Append(s_lastEffectTallest).Append("' at ")
                  .Append(s_lastEffectTallestPx.ToString("F0")).Append(" px tall");
            }
        }
        else
        {
            sb.Append(" (none seen in this window this pass). A ZERO HERE ON A PANEL WHOSE HEIGHT "
                      + "STILL SWINGS MEANS THE QUAD IS NOT DECLARED BY ITS UIFX CONTROLLER AND NOT "
                      + "UNDER A PURE FX CONTAINER EITHER — name what animates it, do not widen a "
                      + "family");
        }
        sb.Append(". Refused content is still DRAWN and the hit rect and capture frame still cover "
                  + "it — it is only barred from deciding the window's WORLD SIZE, which is the 1:1 "
                  + "term: two clients sampling one animation at their own phase cannot agree, and "
                  + "no settle gate can close that because both of them settle");
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
        s_lastRejectEffect = 0;
        s_lastEffectMask = 0;
        s_lastEffectTallest = string.Empty;
        s_lastEffectTallestPx = 0f;
        for (int i = 0; i < MeasureTopCount; i++)
        {
            MeasureTopAreas[i] = 0f;
            MeasureTopNames[i] = string.Empty;
        }
        s_lastFrameClamped = false;

        ClipperMemo.Clear();
        AuthoredOffsetMemo.Clear();
        // ModBuild 449: this memo now has a second reader on this path, and its documented lifetime
        // is "cleared at the top of every measure" for the reason its own comment gives — subtrees
        // are re-parented between passes, so an answer computed against a previous hierarchy is not
        // an answer.
        TransientMemo.Clear();
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
            // ---- ModBuild 449 - AND THE GAME'S OWN BREATHING ART IS THE SAME ARGUMENT. ----------
            //
            // The FocusRing paragraph directly above is this rule with a different owner: a graphic
            // whose extent moves on its own clock cannot size a window, because the size is written
            // from whichever frame the measure happened to land on. Everything it says about
            // Panel_InitiativeTrack "stepping up and down" is true of the GAME's UIFX quads too, and
            // the multiplayer cost is worse than a step: two clients sample the same animation at
            // their own phase, so the WORLD SIZE, the shared seat and the grab bar all diverge and
            // no settle gate can close it — both clients settle, on different content.
            //
            // SCOPED TO FAMILY 7 ON PURPOSE, AND THAT IS THE WHOLE RISK CONTROL. TransientFamilies
            // also carries the six hover/tooltip families, and on THIS path they have never been
            // refused — the ModBuild 201 exclusion lives in MeasureFixedFitParts, which only
            // fixed-fit panels reach. Refusing them here as well would change the fitted size of
            // every ordinary window in the mod, including the merchant and the temple whose current
            // behaviour the user has ACCEPTED. So this asks for one family and takes one family.
            //
            // THE IDENTITY IS THE GAME'S OWN and it is asked twice: is this graphic inside a pure
            // UIFX effect container (ModBuild 448's subtree rule), or is it an Image that a
            // UIFX_MaterialFX_Control DECLARES as one of its effect quads (449's per-graphic rule,
            // for the authoring where the controller sits on the widget)? Neither can reach a label,
            // an icon or anything the game did not itself mark as decoration.
            int effect = TransientFamilies.OfGraphic(g.transform, root, TransientMemo);
            if (effect == TransientFamilies.EffectQuadFamily)
            {
                s_lastRejectEffect++;
                s_lastEffectMask |= 1 << effect;
                RectTransform ert = g.rectTransform;
                float eh = ert != null ? Mathf.Abs(ert.rect.height) : 0f;
                if (eh > s_lastEffectTallestPx || s_lastEffectTallest.Length == 0)
                {
                    s_lastEffectTallestPx = eh;
                    s_lastEffectTallest = g.gameObject.name;
                }
                continue;
            }
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

        FitLoopState loop = GetFitLoop(panel);
        loop.Comparisons++;

        // THE FIXED-SIZE WINDOW (user report 2026-08-22 — see the region below for the measurement
        // and the trade). Deliberately ABOVE the dirty check, the damping and the converged guard:
        // for the one window this matches, the whole growth path is replaced rather than tuned, so
        // it can neither reach the guard nor be affected by it. Every other window falls through
        // and behaves byte-for-byte as it did before.
        if (IsFixedSizeWindow(panel))
            return ApplyFixedFit(panel, root, size, center);
        ReleaseFixedFit(panel);

        // Dirty check (test #14): within 2 % of the current host rect (size AND
        // centering) — nothing to do. Host pivot is centered, so local origin ==
        // rect center and |center| is the content's off-center error directly.
        Rect host = panel.HostRect.rect;
        float tolX = Mathf.Max(host.width, size.x) * FitChangeFraction;
        float tolY = Mathf.Max(host.height, size.y) * FitChangeFraction;
        if (Mathf.Abs(size.x - host.width) <= tolX && Mathf.Abs(size.y - host.height) <= tolY
            && Mathf.Abs(center.x) <= tolX && Mathf.Abs(center.y) <= tolY)
            return true;
        loop.Deviations++;

        // THE CONVERGED GUARD (see FitLoopState). This exact measurement, against this exact host
        // size, has already been through ApplyFitConverging once and that apply wrote NOTHING —
        // neither the host size nor the target position ended anywhere other than where it started.
        // The apply is a pure function of those two inputs, so a repeat cannot write anything
        // either; the only thing it would still produce is the forced layout rebuild inside it, and
        // that rebuild is the one part of an applied fit the GAME's layout can see.
        //
        // `force` is exempt for the same reason it is exempt from the damping: it is the one-shot
        // VERIFY's corrective re-fit — a single, proven, material inconsistency on a window whose
        // rect is otherwise locked, capped at FitVerifyMaxCorrections per open, so it can neither
        // loop nor be the churn this guard exists to stop.
        if (!force && loop.ConvergedValid && HostSizeMatches(host, loop.ConvergedHost)
            && MeasureMatches(loop.ConvergedSize, loop.ConvergedCenter, size, center))
        {
            loop.Suppressed++;
            if (!loop.ConvergedLogged)
            {
                loop.ConvergedLogged = true;
                VRLog.Info("WorldUI", $"FIT CONVERGED '{panel.HostGo.name}': the content measures " +
                                      $"{size.x:F0}x{size.y:F0} px at ({center.x:F0},{center.y:F0}) against a " +
                                      $"{host.width:F0}x{host.height:F0} px host — outside the 2 % dirty band, so " +
                                      "the fit WANTS to re-apply, but the apply this measurement produces has " +
                                      "already run and it changed neither the host size nor the target position. " +
                                      "It is skipped from here on, and with it its " +
                                      "LayoutRebuilder.ForceRebuildLayoutImmediate: the game's own LayoutGroups " +
                                      "re-drive their children's anchors and anchoredPosition during that " +
                                      "rebuild, which is what made elements inside a CONVERGED window twitch once " +
                                      "per damping interval. The guard is level-triggered — it releases the " +
                                      "moment the measurement or the host size genuinely changes, so a window " +
                                      $"whose content grows still re-fits. {DescribeFitLoop(loop)}");
            }
            return true;
        }
        if (loop.ConvergedValid)
        {
            // RELEASE — the growth/shrink path. Logged with BOTH measurements, because this line is
            // the proof that the guard is level-triggered and not a mute button.
            VRLog.Info("WorldUI", $"FIT CONVERGED GUARD RELEASED '{panel.HostGo.name}': the measurement moved " +
                                  $"from {loop.ConvergedSize.x:F0}x{loop.ConvergedSize.y:F0} px at " +
                                  $"({loop.ConvergedCenter.x:F0},{loop.ConvergedCenter.y:F0}) on a " +
                                  $"{loop.ConvergedHost.x:F0}x{loop.ConvergedHost.y:F0} px host to " +
                                  $"{size.x:F0}x{size.y:F0} px at ({center.x:F0},{center.y:F0}) on a " +
                                  $"{host.width:F0}x{host.height:F0} px host — a real content change, so the fit " +
                                  $"runs. {DescribeFitLoop(loop)}");
            loop.ConvergedValid = false;
            loop.ConvergedLogged = false;
        }

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
        //
        // WHAT WAS WRITTEN, MEASURED RATHER THAN ASSUMED (see FitLoopState): the apply's two
        // outputs are the host SIZE and the target's anchoredPosition, and both are read back
        // afterwards. The measurement that produced them is kept too, because the guard above needs
        // the INPUT that provably yielded "no write", not just the fact that there was one.
        Vector2 wroteHostBefore = new(host.width, host.height);
        Vector2 wrotePosBefore = panel.Target.anchoredPosition;
        Vector2 measuredSize = size, measuredCenter = center;
        ApplyFitConverging(panel, root, ref size, ref center, out string applyTrace,
            out bool frameReasserted);
        loop.Applies++;
        Vector2 wroteHostAfter = new(panel.HostRect.rect.width, panel.HostRect.rect.height);
        Vector2 wrotePosAfter = panel.Target.anchoredPosition;
        // A frame re-assertion IS a write (scale/anchors/rotation/depth), and one that the game is
        // actively fighting — never record that pass as a no-op, or the guard would suppress the
        // maintenance along with the churn.
        bool wroteNothing = !frameReasserted
                            && Mathf.Abs(wroteHostAfter.x - wroteHostBefore.x) <= FitNoWriteEpsilonPx
                            && Mathf.Abs(wroteHostAfter.y - wroteHostBefore.y) <= FitNoWriteEpsilonPx
                            && Mathf.Abs(wrotePosAfter.x - wrotePosBefore.x) <= FitNoWriteEpsilonPx
                            && Mathf.Abs(wrotePosAfter.y - wrotePosBefore.y) <= FitNoWriteEpsilonPx;
        if (wroteNothing)
        {
            loop.NoOpApplies++;
            loop.ConvergedValid = true;
            loop.ConvergedLogged = false;
            loop.ConvergedSize = measuredSize;
            loop.ConvergedCenter = measuredCenter;
            loop.ConvergedHost = wroteHostAfter;
        }
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
                              $"{applyTrace} — {DescribeTargetFrame(panel)} — " +
                              (wroteNothing
                                  ? "WROTE NOTHING (host size and target position ended exactly where they " +
                                    "started) — this measurement is now guarded and will not be applied again "
                                  : "WROTE " +
                                    $"{wroteHostBefore.x:F0}x{wroteHostBefore.y:F0}→{wroteHostAfter.x:F0}x{wroteHostAfter.y:F0} px " +
                                    $"host, target {wrotePosBefore.x:F0},{wrotePosBefore.y:F0}→" +
                                    $"{wrotePosAfter.x:F0},{wrotePosAfter.y:F0} ")
                              + $"— {DescribeFitLoop(loop)}.");
        return true;
    }

    /// <summary>
    /// Tolerance (px) below which an applied fit is judged to have written NOTHING. Half a pixel:
    /// the apply writes <c>sizeDelta</c> and <c>anchoredPosition</c> from float measurements, so an
    /// exact-equality test would be defeated by the last bit, while anything the player could see is
    /// orders of magnitude above this.
    /// </summary>
    private const float FitNoWriteEpsilonPx = 0.5f;

    /// <summary>Host rect size equals the size recorded with a no-op apply, to the pixel.</summary>
    private static bool HostSizeMatches(Rect host, Vector2 recorded) =>
        Mathf.Abs(host.width - recorded.x) <= FitNoWriteEpsilonPx
        && Mathf.Abs(host.height - recorded.y) <= FitNoWriteEpsilonPx;

    // =============================================================================================
    // THE FIXED-SIZE WINDOW — ONE SIZE, AND THE SUB-VIEWS FITTED INTO IT
    // =============================================================================================
    //
    // USER REPORT (2026-08-22, translated): "The character UI constantly changes its SIZE depending
    // on which submenu is open — that should not happen. Instead the SUBMENUS should adapt to a
    // FIXED size."
    //
    // WE CAUSED IT. ModBuild 196 stopped tearing the equipment view out into a window of its own, so
    // ALL FIVE of NewPartyDisplayUI's side-by-side sub-views now open inside the map room's
    // permanent character screen — which is what he asked for, and which is why the resizes became
    // constant and obvious.
    //
    // WHAT THE FIVE SUB-VIEWS ACTUALLY NEED, measured off the ModBuild 196 hardware log
    // (.planning/debug/LogOutput.log, every 'Host rect fit …New Party display' line with its top
    // contributors). The HEIGHT is 1080 px in every single state — the window's own authored frame
    // bounds it — so the report is entirely about WIDTH:
    //
    //   no sub-view (the permanent character column)   328 px   'Party Display UI' 300x1080
    //   enhancement cards                              716 px   'Enhance Ability Cards …' 368 px
    //   equipment                                      860 px   'Character Items Equipment …' 512 px
    //   equipment + party inventory column            1136 px   two 512 px columns side by side
    //   ability cards                                 1066 px   scroll viewport 722 px
    //   perks                                         1920 px   'New UIPerksWindow Variant' 1620x1080
    //   character selector                            1920 px   'Campaign … Assembly Variant' 1620 px
    //                                                           with a 1477 px 'Character3D/RawImage'
    //
    // ** THE 196-ERA GLOSS ON THE PERKS ROW IS FALSIFIED — SEE THE ModBuild 201 BLOCK BELOW. ** Until
    // this build that row ended with "(mostly a full-screen 'Blur' plate; its real content column is
    // 512 px)". ModBuild 200's own BACKDROP CENSUS measured it on hardware and it is not true: with
    // EVERY full-frame plate excluded the perks view still needs 1613x1080 px and the character
    // selector still needs 1648x1080 px. The 512 px was a reading of ONE contributor, never of the
    // union, and it has now been the premise of three separate proposals.
    //   battle goals                                   NOT MEASURED — it was never opened in the
    //                                                  logged session (it is a scenario-start view).
    //                                                  It is a full-screen UIBattleGoalPickerWindow
    //                                                  like the perks one, so it is assumed to want
    //                                                  the same 1920 px and is handled by the same
    //                                                  scale-to-fit branch. If a later log shows it
    //                                                  fitting differently, the FIXED FIT line names
    //                                                  the width it asked for.
    //
    // Transients seen in the same log and deliberately NOT designed for: the equipment view ramps
    // 860→896→…→1148 and 1132→…→1485 while its inventory column slides in, and once spiked to
    // 1899 px for a moment. Those are animation frames, not sizes the window needs.
    //
    // ** THE WIDTH ARGUMENT BELOW IS SUPERSEDED BY THE ModBuild 202 BLOCK AT THE END OF THIS NOTE. **
    // Its mechanics are still exactly right and are the reason the 202 width costs what it costs —
    // read it as the derivation of what a wider host BUYS AND SPENDS, not as the choice of 1143 px,
    // which the user's "match the character images" ruling has now overridden.
    //
    // WHY 1143 px AND NOT 1920, WHICH IS THE OBVIOUS CHOICE. The host width does not only decide how
    // wide the frame is — it decides how LARGE EVERYTHING IN THE WINDOW IS DRAWN, because
    // ModalFallback.DeriveWindowScale caps a floated window's physical width at
    // `ModalTargetWidthMeters × WindowLegibility` and shrinks the panel to get there:
    //
    //     extraScale = min(WindowScaleFactor × legibility,  targetWidthMeters × legibility / (px × mpp))
    //
    // The logged panel scales prove the breakpoint exactly: 0.173 at 328/716/860/1066/1136 px, then
    // 0.169 at 1173, 0.163 at 1213, … 0.133 at 1485, 0.103 at 1920 — i.e. the world width is pinned
    // at ~1.00 m from ~1143 px upward and every pixel of host beyond that is paid for by shrinking
    // the CONTENT. The legibility dial cancels out of the breakpoint (it multiplies both terms), so
    // 1143 px = targetWidthMeters / (WindowScaleFactor × CanvasScaleMm) = 0.8 / (0.7 × 0.001) is the
    // widest fixed size that costs no apparent size at all. Pinning at 1920 instead would draw the
    // permanently-visible character column — the state the window is in most of the time — at 60 %
    // of today's size and put this panel back at roughly 1.9 authored pixels per rendered pixel,
    // which is the sampling band ModBuild 189's legibility dial was raised to escape.
    //
    // CORRECTION (ModBuild 198): the 197 draft of this note ended that sentence with "and which this
    // panel cannot buy back (PanelSupersample is deliberately OFF for it)". THE FRESH HARDWARE LOG
    // SAYS THE OPPOSITE — "PANEL SUPERSAMPLE engaged on 'New Party display': … a 1971x1458 capture
    // target … resolved … into a 1971x1458 MIPPED display target". The claim is struck rather than
    // quietly deleted, because the number it was used to justify does not depend on it: 1143 px is
    // the widest host that still renders at the FULL legibility scale (0.875 mm per uGUI px,
    // measured off the same log's HIT RECT lines), and that is a statement about how large the
    // content is DRAWN — which supersampling does not change, it only resamples the same apparent
    // size more cleanly. Supersampling softens the ALIASING half of the old argument against 1920;
    // it does not make 1920 draw the character column any bigger. If the fixed width is ever
    // revisited, revisit it on the apparent-size argument, not on this one.
    //
    // THE COST, STATED RATHER THAN HIDDEN. 1143 px is bigger than five of the seven measured states,
    // so the window is MOSTLY EMPTY when a narrow sub-view (or none) is up: the character column is
    // 328 px of a 1143 px frame. That empty space is unavoidable in ANY fixed size — the states span
    // 328…1920 px, a factor of 5.9 — and it is what "the window keeps one size" buys. It is placed
    // to the RIGHT of the content, never around it: see the left-alignment note on
    // <see cref="ApplyFixedFit"/>.
    //
    // AND THE PACKING SIDE OF IT, WHICH IS A REAL COST TOO. The map room reserves an angular slot per
    // window. This one claimed 45° at spawn (its pre-fit 1920 px rect) and then fitted down to 328 px
    // ≈ 13°, so the packer has been reserving a slot for a window that GROWS back to 45° the moment a
    // tab opens — the ModBuild 195 "the merchant spawned on top of the permanent character screen"
    // overlap. A permanently 1.00 m wide window makes the claim honest, and it also makes it
    // permanent: inside the measured ±32° usable cone, a 45° window centred at 0° leaves ~9.5° on
    // each side, so a second window will overlap it at this reading distance. That was already true
    // whenever a tab was open; it is now true always.
    //
    // WHAT "THE SUB-VIEWS ADAPT" MEANS MECHANICALLY — AND THE ONE-LEVEL-DOWN MISTAKE ModBuild 198
    // SHIPPED. 198 scaled the whole converted subtree (a uniform localScale on the CONVERSION TARGET)
    // whenever the measured union did not fit the pinned host. That branch worked exactly as
    // designed: the host really did stay at 1143x1080 for the whole session (its own log: 5 APPLIED,
    // 22 STABLE, one host-size write). And the user reported the same complaint a third time, because
    // the conversion target's subtree CONTAINS THE CHARACTER COLUMN. Opening perks wrote
    // `content scale 1.000 → 0.595`; closing it wrote `0.595 → 1.000`. The column the user was
    // looking at shrank to 59.5 % and jumped back — from where he sits, the window scaled into
    // another size. Pinning the frame while rescaling everything inside it does not satisfy "die
    // Größe ändert sich nicht"; it moves the change one level down. It also moved the column
    // SIDEWAYS: the union's left-edge pin re-derived a ±400 px target shift on every tab change
    // (log: +400, -400, +401, -401), because a scaled union has a different left edge.
    //
    // ModBuild 199 SPLITS THE SUBTREE INSTEAD, which is what the user asked for in his own words
    // ("stattdessen sollen sich die SUBMENUS an eine fixe Größe anpassen" — the frame stays put and
    // what was already on screen stays the size it was; only the newly-opened view adapts):
    //
    //   THE BASE — everything under the target that is NOT an open sub-view, i.e. the character
    //   column, the party name and the gold row — is NEVER SCALED and its bottom-left corner is
    //   PINNED ONCE and only ever re-asserted onto that same corner. Not re-derived, not re-centred,
    //   not recomputed from a union that a sub-view can change. That is a guarantee, not a tendency:
    //   there is no expression anywhere in the fixed-fit branch that can make the column a different
    //   size, and the FIXED FIT line prints the column's own rendered size and corner next to the
    //   ones it was pinned at so a hardware log states it rather than implying it.
    //
    //   THE OPEN SUB-VIEW is scaled and seated on its OWN root (the GameObject NewPartyDisplayUI
    //   serialises), against THE COLUMN'S RIGHT EDGE — see the next block for why that is not the
    //   frame's right edge, which is what ModBuild 199 shipped and what both of the 200 reports are.
    //
    // =============================================================================================
    // ModBuild 200 — THE ONE LINE OF THE 199 DESIGN THAT WAS WRONG, AND THE TWO FAULTS IT MADE
    // =============================================================================================
    //
    // THE SIZE IS FIXED AND HE CONFIRMED IT ("Das Character-Fenster ist nun stabil in der Größe").
    // Nothing below reopens the base/sub-view split, the one-shot host size, the column pin or the
    // ModBuild 196 converged guard. Exactly one quantity changes: WHAT THE SUB-VIEW IS SEATED ON.
    //
    // 199 seated the scaled sub-view flush against the FRAME'S RIGHT EDGE. That single choice
    // produces both of his reports, and its own hardware log states each of them in numbers:
    //
    //   (c) THE GAP. 'Character Items Equipment Content' needs 543 px, is drawn at 1.000, and its
    //       right edge lands on the frame's right edge minus a 12 px margin, i.e. at +560. The
    //       column's right edge is at -560+328 = -232. Between them: 248 px of empty frame
    //       (photograph 'character_ui_lücke.jpg'; measured off it against the 328 px column,
    //       ~272 px — the same number through a lens at an angle). 'Character Ability Cards
    //       Display Variant' needs 749 px and leaves 42 px, which is the narrow dark strip in
    //       'character_ui_auflösung.jpg'. His words: "ich will GAR KEINE Lücken."
    //
    //   (a) THE OVERLAP. 'Campaign Adventure Party Assembly Variant' needs 1648 px, is scaled to
    //       1143/1648 = 0.693, and 1143 px seated on the right edge of a 1143 px frame reaches all
    //       the way back to the left edge: the 199 line's own words, "covering 340 px of the
    //       column" (photograph 'character_ui_überlagerung.jpg', the selector over the character
    //       list). His words: "es ÜBERLAGERT sie."
    //
    // A right-edge seat makes the distance from the column a FUNCTION OF THE SUB-VIEW'S WIDTH. That
    // is also the "man sieht wie es dahin springt": every tab lands somewhere else.
    //
    // SEATING ON THE COLUMN'S RIGHT EDGE removes both by construction, not by tuning. The seated
    // left edge IS the seam, so the gap is 0 px and the overlap is 0 px for every sub-view, at every
    // scale, and the seat is the same x for all six — nothing moves between tabs but the content.
    //
    // WHAT THE SEAM IS DERIVED FROM, AND WHY NOT FROM THE LIVE MEASURE. The seam is
    // BasePin.x + (base width at the pin) = -560 + 328 = -232 px, captured ONCE on a pass with no
    // sub-view open, exactly like the pin itself. It is NOT the live base union's right edge, and
    // the 199 hardware log is the reason: while the ability-card view is open the base union reads
    // 886x1095 and then 939x1095 and then 328x1080 again, alternating within seconds (174 and 165
    // graphics against 139). Something transient — a hover preview, in the two frames it is up — is
    // drawn under the target and owned by none of the six serialized sub-view roots, so it lands in
    // the base. A seam taken from that would sit ~600 px too far right, would leave the ability-card
    // view 245 px of slot instead of 803, would scale it to 0.33, and would move again on the next
    // pass. That is precisely the jump this round removes, so the seam is a capture, never a reading.
    //
    // THE SLOT, AND THE NUMBER THAT COMES OUT OF IT. Seam -232 to the frame's right edge +571.5 is
    // 803 px of usable width, against 1143 px for a right-edge seat. Every narrow view still fits
    // untouched (543 and 749 measured; 388 and 808 from the 196 census), so four of the six are
    // unaffected in size and merely move ~250 px left onto the column. The two full-screen views
    // pay for it: 1648 px scales to 803/1648 = 0.487 instead of 0.693.
    //
    // THE COST, STATED IN THE UNITS THE COMPLAINT IS IN — and this is report (b), "die Auflösung …
    // so niedrig, dass man den Text kaum lesen kann, besonders in der KARTENAUSWAHL". At the pinned
    // 1143x1080 px = 1000x945 mm the window draws 0.875 mm per authored px (HIT RECT, same log), so
    // at the 1.2 m reading distance one authored px is 2.51 arc-minutes and the game's body caps —
    // 13.5 authored px, measured off 'character_ui_auflösung.jpg' against the 328 px column (see
    // FixedFitBodyCapPx) — are ~34 arc-minutes. The slot is seam -232 to frame right +572 = 803 px.
    // Per sub-view, at this seam:
    //
    //   enhancement cards       388 px → 1.000 → 0.875 mm/px → 33.8' cap → 1.27 rendered px/authored
    //   equipment               543 px → 1.000 → 0.875 mm/px → 33.8' cap → 1.27
    //   ability cards           749 px → 1.000 → 0.875 mm/px → 33.8' cap → 1.27
    //   equipment + inventory   808 px → 0.994 → 0.870 mm/px → 33.6' cap → 1.26
    //   perks                  1620 px → 0.496 → 0.434 mm/px → 16.8' cap → 0.63
    //   character selector     1648 px → 0.487 → 0.426 mm/px → 16.5' cap → 0.62
    //
    // (Rendered px per authored px from this repo's own calibration: the legibility dial's doc
    // states a 1920 px window reaches 1:1 at legibility 1.65, i.e. at 0.8x1.65 m / 1920 px =
    // 0.6875 mm per authored px. Anything above that line is supersampled; anything below is the
    // undersampling band ModBuild 189 was raised to escape.)
    //
    // SO: THE FOUR NARROW VIEWS ARE COMFORTABLE AND THE TWO FULL-SCREEN ONES ARE NOT — and a
    // right-edge seat did not make them comfortable either (0.693 → 0.606 mm/px → 23.5' → 0.88
    // rendered px per authored px, still under 1:1). AND THE VIEW HE NAMED IS ALREADY AT SCALE
    // 1.000: 'character_ui_auflösung.jpg' is the ability-card selection, which the log shows drawn
    // at 1.000 with nothing scaled at all. Report (b) is therefore NOT caused by sub-view scaling
    // and cannot be fixed by any seating rule: it is the window's own density. The lever for it is
    // solid angle and nothing else, and it is already shipped and in his hands — see below.
    //
    // THE FOUR WAYS OUT OF (b), WITH THE PRICE OF EACH. At a 1.00 m window, 1.20 m away, beside a
    // 328 px column, a 1648 px view can be drawn exactly four ways and there is no fifth:
    //
    //   1. 0.487, no gap, no overlap                        — THIS BUILD.
    //   2. 0.693, 340 px of the column covered              — ModBuild 199. He rejected it, (a).
    //   3. 1.000, 845 px (0.74 m) hanging outside the frame — the hit rect and the supersample
    //      capture both already grow to cover content drawn outside the host frame, so it would
    //      WORK; it makes the window 1.74 m / 72° across and reads as "the window got bigger",
    //      which is the complaint the last four builds were about.
    //   4. 0.679, with the column HIDDEN while the view is open — a mode, and the column
    //      disappearing under him is the same class of surprise as (a).
    //
    //   NONE of the four changes the density of the ability-card view he actually named. Two things
    //   do, and neither is a seating rule:
    //
    //   * [WorldUI] WindowLegibility (shipped, user-facing, live at the next re-fit). It is the
    //     window's physical width: 1.25 → 1.00 m → 0.875 mm/px → 45° of view; 1.45 → 1.16 m →
    //     1.015 mm/px → 52°; 1.75 → 1.40 m → 1.225 mm/px → 61°. It lifts EVERY view including the
    //     four already at scale 1.000, and it costs angular footprint against a standing 45° ruling
    //     and a measured ±32° usable cone. Its default is NOT changed here: 61° is past a size he
    //     has already called too big once, and that is his ruling to move, not ours.
    //   * The two-hand resize, which already rides on top of the dial and already wins, per window
    //     and temporarily. That is the right tool for "the perks list is too small RIGHT NOW".
    //
    //   AND ONE THAT LOOKS LIKE A WAY OUT AND IS NOT: the standing 1.20 m → 1.85 m reading-distance
    //   offer. Rendered pixels are bought with SOLID ANGLE; moving the same window further away is
    //   35 % LESS apparent size and therefore 35 % worse for (b). It is a window-PACKING remedy and
    //   should be judged as one — it must not be taken in the belief that it helps legibility.
    //
    // WHY THE ModBuild 196 CONVERGED GUARD IS NOT REOPENED, AND STILL IS NOT BY THE 199 SPLIT. This
    // branch never calls <see cref="ApplyFitConverging"/> and never calls
    // <see cref="FlushPendingLayout"/> — the two things the perpetual re-fit loop was made of, because
    // the loop was made of a forced LayoutRebuilder pass that the game's own LayoutGroups then answered
    // with a fresh layout, which the next fit measured, which forced another rebuild. Nothing in the
    // 199 split adds one: the extra work it does is a second PASS OVER GRAPHICS (world-corner reads,
    // no writes at all — see MeasureFixedFitParts, which deliberately does not go through
    // TryMeasureContent), and its three writes are a host sizeDelta, a target anchoredPosition and a
    // sub-view localScale/anchoredPosition. None of those is a layout rebuild. Its settled state is a
    // pure comparison: the host already IS the fixed size, the column is already on its pinned corner,
    // and the open sub-view is already at the scale and seat the measurement asks for — so the pass
    // returns having written nothing and disturbed nothing. The guard's own code and every other
    // window's path are untouched.
    //
    // The ONE way the loop could come back is a sub-view root that sits inside a LayoutGroup, which
    // would re-drive the anchoredPosition we write and make us write it again ~2.5 times a second.
    // That is why the fit counts foreign overwrites of its own pose exactly (not statistically) and
    // CONCEDES after FixedFitMaxReAsserts of them rather than fight — see the concede branch. Losing
    // the sub-view placement costs a sub-view that spills; winning a write war costs stereo rivalry.
    //
    // =============================================================================================
    // ModBuild 201 — NOTHING MAY MOVE BECAUSE OF A MOUSEOVER, AND THE 512 px PREMISE IS DEAD
    // =============================================================================================
    //
    // USER REPORTS THIS ROUND (translated):
    //   (3) "The menu with the character is now suddenly scaled very small and no longer matches the
    //       size of the character images next to it."                    → the 0.487 selector.
    //   (4) "The flicker on moving only occurs with the CHARACTER (first button) and the PERKS (last
    //       button). By now it also comes up broken INITIALLY … it is quite random, it depends on
    //       when you release."                                            → those same two views.
    //   (5) "In the card-selection sub-menu, hovering the lower options shows the cards at the bottom
    //       — and that immediately triggers a shift of the whole window. … Nothing may shift because
    //       of mouseovers."
    //
    // (3) AND (4) NAME EXACTLY THE TWO SUB-VIEWS THAT ARE SCALED AT ALL, which is the whole of the
    // correlation and is why this block exists. The 200 hardware log's own distribution, counted
    // rather than sampled (79 FIXED FIT lines, .planning/debug/LogOutput.log):
    //
    //   'Character Ability Cards Display Variant'   61 lines  need 749x1080  scale 1.000
    //   'Character Items Equipment Content'          8 lines  need 543x1080  scale 1.000 (one 0.981)
    //   'Campaign Adventure Party Assembly Variant'  7 lines  need 1648      scale 0.487
    //   'New UIPerksWindow Variant'                  1 line   need 1627      scale 0.494
    //
    // ---------------------------------------------------------------------------------------------
    // WHY THE TWO WIDE VIEWS STILL CANNOT REACH 1.000, MEASURED RATHER THAN ARGUED
    // ** THE HEADING IS FALSIFIED BY THE ModBuild 202 RULING: THEY CAN, AND THEY NOW DO. **
    // Everything measured below stands — the plate route really is worth 14 px and the selector's
    // width really is content — and it is exactly why 202 does the only remaining thing: it stops
    // trying to fit 1648 px of content into a 803 px slot and makes the slot 1648 px wide instead.
    // What that costs is in the 202 block at the end of this note, and it is not free.
    // ---------------------------------------------------------------------------------------------
    //
    // Two openings were proposed for getting them to 1.000. THE 200 LOG FALSIFIES BOTH, and both are
    // falsified by the same instrument the 199 round shipped precisely so that the next round would
    // not have to infer again — the BACKDROP CENSUS. Its two lines, verbatim:
    //
    //   perks:     "2 full-frame plate(s) inside it, the largest 'New UIPerksWindow Variant' at
    //               1620x1080 px; WITHOUT them the view would need 1613x1080 px"
    //   selector:  "2 full-frame plate(s) inside it, the largest 'Display' at 1620x1080 px;
    //               WITHOUT them the view would need 1648x1080 px"
    //
    //   * "PERKS is mostly a full-screen Blur backdrop around a ~512 px content column, so DISABLING
    //     the plate (not merely un-measuring it — CanvasConversion.4.Lifecycle.cs already hides
    //     backing plates via panel.HiddenBackgrounds) fits it at 1.000." FALSE. Removing every
    //     full-frame plate takes perks from 1627 to 1613 px — 14 px, 0.9 %. The 512 px was one
    //     contributor's width in a ModBuild 196 top-3 line, never the union's. It is NOT a route to
    //     scale 1.000 and must not be sold as one a fourth time.
    //
    //     AND WHILE WE ARE HERE — WHY THE PERKS PLATE IS NOT IN panel.HiddenBackgrounds, since that
    //     was the other half of the question and the answer is two exact lines, neither of them in
    //     this file (so neither is changed here):
    //       1. ModalFallback.WantsTransparentBackground lists ESCMenu, Options, OptionsSubmenu,
    //          ViceOptionsSubmenu, ResultsPanel, RewardsPanel, AdventureCompletionPanel and the four
    //          MenuConfirmations. UIWindowID.PartyPanel is not among them, so ConvertedPanel
    //          .HideBackground is never even set for the character screen and the sweep never looks
    //          at it. It is NOT, as one might assume, that the perks view was inactive at convert
    //          time: CanvasConversion.4.Lifecycle re-runs HideFullScreenBackground on the
    //          BackgroundSweepNextFrame cadence, so a plate that appears with a tab WOULD be caught.
    //       2. Even opted in it would miss by a hair. That sweep requires a graphic to cover
    //          BackgroundCoverFraction = 0.85 of the window frame on BOTH axes; the plate is
    //          1620 px of an authored 1920 px frame = 0.844. The fit's own plate test uses 0.80/0.95
    //          and does catch it, which is why the census can see a plate the hide sweep cannot.
    //     Worth proposing on its own merits — a full-window blur veil means nothing on a floated
    //     window with a transparent frame, and 'perks.jpg' is a DARK RECTANGLE with a few glyph
    //     fragments in it, which is what a surviving 1620x1080 plate over missing content looks like
    //     — but it is a change to two files this lane does not own and it buys 14 px of width.
    //   * "The SELECTOR's width is a 1477 px Character3D/RawImage, i.e. pictorial content that could
    //     be scaled while the text beside it stays at 1.000." FALSE as a width argument: the census
    //     removes every full-frame plate and the union does not move at all (1648 → 1648), so the
    //     union's extremes are NOT the big pictorial elements. Splitting a sub-view into
    //     independently-scaled children also breaks the game's own layout contract — the portrait and
    //     the stat rows are placed by the prefab relative to one another, and scaling one of them
    //     leaves a hole exactly where the other one expects a neighbour. It is not done.
    //
    // So the four ways out listed in the 200 block above remain the only four, and this build still
    // takes (1): 0.487, no gap, no overlap, column untouched. (3) THEREFORE REMAINS OPEN AND IT IS A
    // RULING, NOT A BUG: at a 1.00 m window, 1.20 m away, with the 328 px column visible, 1648 px of
    // content cannot be drawn at 1.000 — option (3) of that list draws 1.74 m / 72° across, past the
    // ~1.3 m slab he has already called too big (see ModalFallback's ModalTargetWidthMeters doc), and
    // option (4) makes the column vanish under him. A comment is not consent and neither is a doc
    // comment's headroom: growing the drawn window toward a size he rejected is his call to make.
    //
    // WHAT IS SHIPPED INSTEAD, so the next round decides that with data: the census now names the
    // graphics that DEFINE the content union's left and right extremes, with their rects. "1613 px
    // wide" is a number nobody can act on; "'X' at 1620x412 px is the left extreme and 'Y' the right"
    // is. If those extremes turn out to be a second backdrop that merely failed the 95 %-height plate
    // test, the plate route comes back alive with evidence behind it.
    //
    // ---------------------------------------------------------------------------------------------
    // (5) NOTHING MAY MOVE BECAUSE OF A MOUSEOVER — THREE THINGS COULD, AND ALL THREE ARE CLOSED
    // ---------------------------------------------------------------------------------------------
    //
    // THE CONTAMINATION IS OURS, AND THE LOG NAMES IT. TooltipOnWindow RAISES a hover preview out of
    // wherever the game put it and re-parents it directly under the conversion target's child so it
    // draws on top — its own line, 18 times in the 200 log: "LOCAL TOOLTIP 'FullAbilityCard
    // (ability-card hover preview)' laid FLAT ON its OWNING floated window … it hangs under 'New
    // Party display'", with "the box hangs at sibling 10 of 10" 19 times. That is why the preview
    // belongs to NONE of the six serialized sub-view roots and lands in the BASE union: we moved it
    // there. The distribution of the base reading over the same 79 lines:
    //
    //   328x1080 px, 135-139 graphics   27 lines   the real character column
    //   896x1080…1230 px, 157-172       50 lines   column + a RAISED FullAbilityCard hover preview
    //   1155/1159x1080 px, 146-149       2 lines   column + a RAISED UIPartyItemInventoryTooltip
    //
    // Every one of the 52 contaminated readings is accounted for by the two families the mod's own
    // LOCAL TOOLTIP lines name. Nothing is left over, so the rule below is an identity and not a
    // residue. What that contamination MOVED, all of it visible in the same lines:
    //
    //   * THE COLUMN. The base pin is the base union's bottom-LEFT corner and the correction was
    //     re-derived and written EVERY pass with no settle gate at all: 47 "the character column
    //     re-aligned by 0,N px" writes in one session, counted — 32 of them exactly +15 px, the rest
    //     spread over -11, -13, -14, -15, -30, -42, -60, -64, -135 and -150. x never moved (the
    //     preview opens to the RIGHT of the column, so it only extends the union's right edge); y
    //     moved because the preview hangs BELOW it. THAT IS HIS "the whole window shifts", and note
    //     that the repeating +15 would have passed a settle gate on its own — it is the EXCLUSION
    //     that kills that one, and the gate that kills the ramp. Both are needed.
    //   * THE OPEN SUB-VIEW'S SCALE. The equipment view measures 543 px, and once measured 819 px
    //     with the item hint inside it — 819 > the 803 px slot, so the fit wrote scale 0.981 and
    //     re-seated the view. A mouseover changed the size of the thing being hovered.
    //   * THE SEAM was already immune (captured once, ModBuild 200) and stays exactly as it is.
    //
    // THE RULE. A graphic is TRANSIENT if any ancestor up to the fit root carries one of six game
    // component types (see TransientFamilyOf). Matched BY COMPONENT TYPE — the same table this repo
    // already argues for at TooltipOnWindow.cs:510-531, extended with UIItemModifiersTooltip — never
    // by name, never by "it is not one of the six", and never by the nested Canvas the game adds to a
    // full card (ModBuild 194 proved that Canvas outlives its own hide). Transient graphics count
    // toward NEITHER the base union NOR any sub-view union, and the count is printed every line, so
    // "nothing shifted" and "the rule never fired" cannot look alike.
    //
    // AND THE SAFETY NET, because an exclusion that empties a bucket is worse than the contamination:
    // both unions are built twice, clean and raw, and a bucket whose CLEAN reading has no graphics
    // falls back to the raw one and says so in the line. The one family that could plausibly empty a
    // bucket is FullAbilityCard — if the character screen showed full cards permanently, excluding
    // them would delete the ability-card view. It does not:
    // UIPartyCharacterAbilityCardsDisplay.cs:355/363 calls ToggleFullCard(false) and
    // ToggleFullCardPreview(false) on every spawned row, so a full card in this window is ALWAYS a
    // hover, and the log agrees (the ability-card view's need is 749x1080 on all 61 of its lines
    // while the base swings by 568 px underneath it).
    //
    // TWO MORE THINGS THAT COULD MOVE, AND NOW CANNOT:
    //
    //   * THE COLUMN RE-ASSERT IS BEHIND THE SETTLE GATE. Even with every tooltip excluded the base
    //     still slides while the window's OWN show animation runs — the same 79 lines have the clean
    //     328x1080 column reading its bottom edge at y = -540, -525, -480 and -405, and the HIT RECT
    //     line names the mover ("FURTHEST OUTSIDE the frame: 'New Party display/Party Display UI ' by
    //     30 px", ramping 30 → 90 → 120 → 150 → 30). A ramp never repeats a value, which is exactly
    //     what FixedFitSettleChecks was written to reject, and the base was the one write exempt from
    //     it. It is not exempt any more (the pre-reveal first fit still is, as before).
    //   * THE SUB-VIEW POSE IS SOLVED ONCE PER OPEN SET AND THEN FROZEN, like the host size, the pin
    //     and the seam. While the same tabs stay open the scale and the seat are constants, not
    //     re-derived quantities — so no measurement of any kind can re-scale or re-seat a view that
    //     is already up. This is the half of (4) that is ours: the 200 log's perks open shows "3
    //     sub-view scale write(s), 7 sub-view re-seat(s), 19 column re-assert(s)" DURING the open,
    //     i.e. the window was still being written 29 times while he was dragging it, and the
    //     supersample capture re-resolves on drag. "It is quite random, it depends on when you
    //     release" is what a capture that races a pose write looks like. The freeze releases the
    //     moment the set of open sub-views changes, which is the only event that may legitimately
    //     ask for a different scale.
    //
    // =============================================================================================
    // ModBuild 202 — ONE WINDOW, ONE SCALE. THE SUB-VIEW SCALE IS NO LONGER SOLVED; IT IS 1.000.
    // =============================================================================================
    //
    // THE RULING (2026-08-22, translated). The four trade-offs the 201 block ends on were put to the
    // user as a question, and he refused the question: "I don't quite understand your question. I
    // want the sub-menu to be exactly the size of the whole window and to match the size of the
    // character images on the left, so that it is perceived as ONE window. That is already
    // successfully the case for the other sub-menus (except perks). GUARANTEE that."
    //
    // So this is a requirement and not a preference, and it names its own REFERENCE: the character
    // images. They are the fixed quantity — the sub-menu is to be brought up to them, and "one
    // window" is the acceptance test. Four of the six sub-views already satisfied it (all at scale
    // 1.000); perks (1627 px → 0.494) and the character selector (1648 px → 0.487) did not, and those
    // are also the only two he reports flickering, because a scaled subtree is captured below the
    // sampling band limit. One cause, two reports, one fix.
    //
    // WHAT CHANGED, AND IT IS ONE LINE OF ARITHMETIC. The scale-to-fit solve is GONE. Where
    // SolveSubViewPlacement wrote `min(1, slot/need)` it now writes the constant 1. Nothing measures
    // its way to a different number any more, which is what "guarantee" has to mean: there is no
    // expression left in this file that can draw a sub-view at anything but the scale the character
    // column is drawn at. A view too wide for the frame SPILLS to the right (the seat is still the
    // column seam, so it can never reach back over the column) and the FIXED FIT line prints the
    // spill in pixels — visible and stated, instead of silently shrunk.
    //
    // AND THE HOST IS SIZED TO MAKE THE SPILL ZERO FOR EVERY VIEW WE HAVE EVER MEASURED:
    //
    //     FixedFitWidthPx = FitContentPaddingPx + column + widest sub-view = 12 + 328 + 1648 = 1988 px
    //
    // Each term is a measurement off the fresh hardware log (.planning/debug/LogOutput.log, 83
    // FIXED FIT lines), and each is a UNION and never one contributor — the misread that has now cost
    // three proposals. The column: 27 of the 83 lines read the base with no sub-view open and all 27
    // read 328x1080 px. The widest sub-view: the character selector at 1648 px on all 7 of its lines
    // (perks 1627 on its 1, ability cards 749 on 61, equipment 543 on 8 and 819 on the one line where
    // an item hint was still being measured — that one is what ModBuild 201's transient rule
    // removed). 12 px is the left padding the column's own pin already carries, so the seam lands at
    // -994+12+328 = -654 and the slot is exactly 994-(-654) = 1648 px. The width is still captured
    // ONCE per window life and still never re-derived: it is a constant, so a tab change cannot even
    // in principle ask for a different frame.
    //
    // COULD THE CENSUS HAVE BOUGHT SOMETHING NARROWER THAN 1648? NO, AND HERE IS THE STATE OF THAT
    // EVIDENCE. The BACKDROP CENSUS answers it for the plates and its verdict is unchanged: 2
    // full-frame plates in each wide view, and removing every one of them takes perks 1627 → 1613 px
    // (0.9 %) and the selector 1648 → 1648 px (nothing at all). The CONTENT EXTREMES census that
    // ModBuild 201 shipped to answer the rest of it HAS NOT RUN ON HARDWARE YET — the freshest log is
    // a ModBuild 200 log (83 FIXED FIT lines, 2 BACKDROP CENSUS lines, ZERO 'CONTENT EXTREMES'
    // lines), so nobody has yet seen which two graphics define the union. That is a gap and it is
    // stated rather than filled with an inference: 1648 px is the measured union and the host is
    // sized to it. If a 202 log's CONTENT EXTREMES turns out to name a second backdrop that merely
    // failed the 95 %-height plate test, the width can come DOWN by that much later, and the only
    // thing that changes is one constant.
    //
    // ---------------------------------------------------------------------------------------------
    // WHAT THIS COSTS, AND THE PART OF IT THAT IS NOT IN THIS FILE
    // ---------------------------------------------------------------------------------------------
    //
    // A WIDER HOST DOES NOT MAKE THE WINDOW BIGGER. IT MAKES EVERYTHING IN IT SMALLER. That is the
    // mechanism the 197-era block above derives, and it is decisive here, so it is restated in the
    // numbers of this round rather than cross-referenced: ModalFallback.DeriveWindowScale caps a
    // floated window's PHYSICAL width at `ModalTargetWidthMeters × WindowLegibility` = 0.80 × 1.25 =
    // 1.00 m and buys the cap by shrinking the panel. The 200 log states it for this very window:
    // "MODAL WINDOW SIZE: 'New Party display' re-scaled to its FITTED rect 1143x1080 px (extraScale
    // 0.521 → 0.875)", and 0.875 = 1.00 m / 1.143 m is the cap arriving exactly at 1143 px. At
    // 1988 px the same rule returns 1.00 m / 1.988 m = 0.503:
    //
    //     host 1988x1080 px × 0.503 = 1.00 x 0.54 m — the SAME 1.00 m / 45° footprint as today,
    //     and 0.503 mm per authored px instead of 0.875, i.e. every glyph, every portrait and the
    //     character column itself drawn at 57.5 % of the size he has already approved.
    //
    // THAT SATISFIES THE LETTER OF THE RULING AND BREAKS ITS REFERENCE. All six views would read
    // 1.000 and the window would be one coherent surface — and the character images, which are the
    // thing he told us to match, would have shrunk by 42 % to meet the sub-menu instead of the other
    // way round. So the width alone is not the deliverable; the width plus a physical size that keeps
    // 0.875 mm per authored px is:
    //
    //     1988 px × 0.875 mm/px = 1.74 m across, 2·atan(0.87/1.2) = 72° at the 1.2 m reading
    //     distance, with every sub-view AND the column at today's density (33.8' for a 13.5 px body
    //     cap, up from 16.5' for the two wide views).
    //
    // AND 72° IS A REAL PRICE, STATED AND NOT MITIGATED. This window is non-closable by user ruling
    // and permanent in the map room, whose measured usable cone is about ±32°; a 72° window centred
    // at 0° fills it and then some, so a second window WILL overlap it at this reading distance
    // (the ModBuild 195 merchant-on-the-character-screen class of report). It is also past the ~1.3 m
    // slab that once read "too big". Nothing here quietly compensates for that: no automatic distance
    // change, no automatic legibility change, no default touched. The two dials that exist stay in
    // his hands, and what each does to this window is:
    //
    //   * [WorldUI] WindowLegibility multiplies the cap, so it scales the whole window — including
    //     the match, which is preserved at every setting because the column and the sub-view are the
    //     same scale by construction now. It cannot, on its own, undo the shrink described above:
    //     its 1.75 ceiling gives 0.503 × 1.75/1.25 = 0.704 mm/px, still short of 0.875.
    //   * THE READING DISTANCE (the standing 1.20 → 1.85 m offer). A window of FIXED physical width
    //     subtends less angle further away — 1.74 m at 1.85 m is 51° instead of 72° — and the
    //     column/sub-view match survives untouched, because both scale together with the window. It
    //     buys back the packing cost and it costs apparent size (i.e. it is the wrong lever for the
    //     old report (b) and the right one for the overlap), and that trade is his to make.
    //
    // THE ONE LINE THIS LANE MAY NOT WRITE. Getting from the first block of numbers to the second is
    // a change to ModalFallback.DeriveWindowScale — this file does not own it, so it is NOT made
    // here. The exact patch is: after `boardRelative` is computed, return `cap` for the fixed-size
    // character screen (`CanvasConversion.IsFixedSizeWindow(panel)`, which is why that predicate is
    // `internal` and not `private`), on the argument that this window's width is CONTENT-derived —
    // it is the width its own sub-views need at 1:1 — and not a taste size the board-relative rule
    // should be re-negotiating. Until that patch lands, this build ships the first block of numbers:
    // the match is exact and the whole window is 57.5 % of its previous size.
    //
    // TWO THINGS THAT ARE NOW ALLOWED TO SPILL RATHER THAN SHRINK, BOTH DELIBERATE AND BOTH PRINTED:
    //
    //   * BATTLE GOALS, the one sub-view never seen in any logged session. If it really is a
    //     full-screen 1920 px view like the perks one, it will reach ~272 px past the frame's right
    //     edge at 1.000 instead of being scaled to 0.86. The frame is transparent, the hit rect and
    //     the supersample capture frame both already grow to cover content drawn outside the host
    //     rect, and the FIXED FIT line names the width it asked for and the spill in pixels — so the
    //     next log turns this from an assumption into the one constant that needs raising.
    //   * HEIGHT. The scale is no longer solved against the frame height either, and the 200 log has
    //     the selector's union reading 1648x1347, x1480, x1547 and x1580 px on 5 of its 7 lines (the
    //     other 2 read 1080). Those readings predate ModBuild 201's transient rule and are most
    //     likely the hover content it now refuses to measure — but "most likely" is not a guarantee,
    //     so the vertical spill is measured and printed on every line as well. A view that really is
    //     1580 px tall now hangs ~250 px above and below the frame at full size rather than being
    //     shrunk to 68 % — which is the same ruling applied to the other axis.

    /// <summary>
    /// THE CHARACTER COLUMN'S WIDTH in authored uGUI px — the permanent strip of character portraits
    /// down the left of the window, and the REFERENCE the user's ruling names ("match the size of the
    /// character images on the left"). MEASURED, never chosen: of the 83 <c>FIXED FIT</c> lines in the
    /// fresh hardware log, 27 read the base union with no sub-view open and every one of them reads
    /// 328x1080 px. (The other 56 read the same column plus a raised hover preview, which ModBuild
    /// 201's transient rule stopped measuring.)
    ///
    /// <para>Used for ONE thing: deriving <see cref="FixedFitWidthPx"/> below, which has to be a
    /// compile-time constant because the host size is captured before anything is measured. The seam
    /// every sub-view is seated on is still taken from the column that is actually on screen — see
    /// <see cref="EnsureColumnSeam"/> — so a column that measured differently would move the seam and
    /// show up as spill in the log, never as a silently rescaled view.</para>
    /// </summary>
    private const float FixedFitColumnWidthPx = 328f;

    /// <summary>
    /// THE WIDEST SUB-VIEW ROOT, authored uGUI px, at scale 1. The character selector: 1648 px on all
    /// 7 of its lines in the same log, against perks 1627 (1 line), ability cards 749 (61) and
    /// equipment 543 (8, plus one 819 that was an item hint being measured). A UNION and not a top
    /// contributor — the "perks is really a 512 px column behind a blur plate" reading was one
    /// contributor's width and was the premise of three separate proposals before ModBuild 200
    /// measured it (removing every full-frame plate: perks 1627 → 1613 px, selector 1648 → 1648).
    /// </summary>
    private const float FixedFitWidestSubViewPx = 1648f;

    /// <summary>
    /// THE FIXED HOST WIDTH (uGUI px): left padding + the character column + the widest sub-view, so
    /// that EVERY sub-view fits beside the column at scale 1.000 and the window reads as one surface.
    /// 12 + 328 + 1648 = 1988. Captured once per window life and never re-derived, so a tab change
    /// cannot ask for a different frame; narrow views leave transparent frame to the right, exactly
    /// as they always have.
    ///
    /// <para><b>WHAT IT COSTS IS NOT IN THIS FILE.</b> <c>ModalFallback.DeriveWindowScale</c> caps a
    /// floated window's PHYSICAL width at <c>ModalTargetWidthMeters × WindowLegibility</c> = 1.00 m
    /// and shrinks the panel to get there, so at 1988 px the window keeps its 1.00 m / 45° footprint
    /// and draws 0.503 mm per authored px instead of 0.875 — the match is exact and everything in the
    /// window, the character images included, is 57.5 % of its previous size. Keeping 0.875 mm/px
    /// (1.74 m across, 72° at 1.2 m) needs the one-line exemption described at the end of the region
    /// note. Read that block before changing this number.</para>
    /// </summary>
    private const float FixedFitWidthPx =
        FitContentPaddingPx + FixedFitColumnWidthPx + FixedFitWidestSubViewPx;

    /// <summary>Sanity bounds so a frame caught mid-layout can never pin the window at a nonsense
    /// rect. Below either minimum the capture is simply postponed. From ModBuild 202 the WIDTH is a
    /// constant (<see cref="FixedFitWidthPx"/>) and is not clamped by these at all —
    /// <see cref="FixedFitMinWidthPx"/> is now purely a "has this window been laid out yet?" test on
    /// the authored frame, while the height bounds still clamp the captured height.</summary>
    private const float FixedFitMinWidthPx = 512f;
    private const float FixedFitMinHeightPx = 256f;
    private const float FixedFitMaxHeightPx = 2160f;

    /// <summary>Content-scale change below which nothing is written (1 %). The solved scale is the
    /// constant 1.000 from ModBuild 202 on, so this now only ever answers "has somebody ELSE scaled
    /// the root we place" — which is exactly what the re-assert and concede counters are for.</summary>
    private const float FixedFitScaleEpsilon = 0.01f;

    /// <summary>Content shift (px) below which nothing is written.</summary>
    private const float FixedFitShiftEpsilonPx = 1f;

    /// <summary>
    /// FULL-FRAME BACKDROP PLATE — a single graphic that spans essentially the whole authored window
    /// frame. The perks sub-view's 1620x1080 <c>Blur</c> is one; a content column never is.
    ///
    /// <para><b>THIS DECIDES NOTHING. IT IS PRINTED, NOT APPLIED — and that is the answer to the
    /// question ModBuild 198 raised and deferred.</b> The proposal was to leave such plates OUT of
    /// the measured need, on the observation that the perks view's 1620 px is mostly plate around a
    /// ~512 px content column, so the two "1920 px" sub-views would drop to something that fits. The
    /// measurement is right and the conclusion does not follow: excluding the plate from the MEASURE
    /// does not stop the plate being RENDERED. Fit the 512 px column at scale 1 and the 1620 px plate
    /// is still drawn at 1620 px inside a 1143 px window — and this panel's supersample capture
    /// deliberately GROWS its frame to cover content drawn outside the host rect (the ModBuild 198
    /// log: "CAPTURE FRAME 1727x1453, GROWN by 584x373 px to cover content drawn outside the host
    /// frame"), so the excess is not quietly clipped: a dim slab would hang ~240 px past each side of
    /// the window. That trades a legibility complaint for a "there is a grey rectangle around my
    /// window" complaint, which is not an improvement, and it would have shipped as one.</para>
    ///
    /// <para>Making it work needs the plate SCALED to the frame while its content column is scaled
    /// separately — i.e. knowing, per sub-view, which child is backdrop and which is content. That is
    /// prefab data, it differs for all six views, and the one measurement anybody has of it is a
    /// single top-contributor line. So the plate census is MEASURED AND LOGGED this round instead of
    /// acted on: the next round gets the real backdrop-versus-content split for every sub-view the
    /// user opens, from hardware, rather than another inference. The second full-screen view settles
    /// it on its own either way — the character selector's 1477 px is a <c>Character3D/RawImage</c>,
    /// which is content, so plate exclusion could never have fixed that one.</para>
    /// </summary>
    private const float FixedFitPlateWidthFraction = 0.80f;
    private const float FixedFitPlateHeightFraction = 0.95f;

    /// <summary>
    /// Consecutive fit checks a NEW layout candidate (content scale + alignment shift) must repeat
    /// before it is written. The fit samples every <see cref="FitCheckIntervalFrames"/> frames
    /// (~0.4 s), so this is ~0.8 s — and it is what keeps the equipment view's slide-in animation
    /// (which measured 1132→1173→1213→…→1485 px, a DIFFERENT value on every sample) and its
    /// one-frame 1899 px spike from ever rescaling the window: a ramp never repeats a value, and a
    /// spike is usually not even sampled. A real tab change parks at one width and passes on the
    /// second sample. The FIRST fit of a window's life is exempt (it must land before the reveal).
    /// </summary>
    private const int FixedFitSettleChecks = 2;

    /// <summary>
    /// How many times the fit may find its own sub-view pose overwritten before it stops writing it.
    /// The count only rises when the SOLUTION was unchanged and the pose was not, which a tab change
    /// cannot produce — so this is a fight counter, not a churn counter, and 8 of them is several
    /// seconds of one. See the concede branch in <see cref="ApplyFixedFitCore"/>.
    /// </summary>
    private const int FixedFitMaxReAsserts = 8;

    /// <summary>How often a settled fixed-size window restates its counters, so a hardware log can
    /// tell "never ran" (no line at all) from "stable" from "still resizing".</summary>
    private const float FixedFitStableLogSeconds = 20f;

    /// <summary>
    /// Reading distance the FIXED FIT line's arc-minute figures are quoted at, real metres. A MIRROR
    /// of <c>ModalFallback.WindowDistanceMeters</c> (private there, and this lane does not own that
    /// file) and used for NOTHING but the log text — no placement, no scale and no seat reads it, so
    /// a drift between the two costs a wrong number in a diagnostic and can never move a window.
    /// If the two ever disagree, the HIT RECT line's own millimetre figure is the authority.
    /// WAS 1.2 until ModBuild 241, when the user asked for windows to spawn "ein bisschen (nicht
    /// viel) weiter weg" and the real constant moved to 1.40; scripts/check-mirrors.sh is what
    /// caught this copy, which is the whole reason that lint names this pair.
    /// </summary>
    private const float FixedFitReadingDistanceMeters = 1.40f;

    /// <summary>
    /// Arc-minutes subtended by one millimetre at <see cref="FixedFitReadingDistanceMeters"/>:
    /// <c>atan(0.001 / 1.40) in arc-minutes</c> = 2.456 (it was 2.865 at the pre-ModBuild-241
    /// reading distance of 1.2 m, so arc-minute figures from an older log read ~17 % high against
    /// figures from a newer one). Written out rather than computed so the
    /// constant carries its derivation; the small-angle error at this size is under 0.001 %.
    /// </summary>
    private const float MmToArcMinAtReadingDistance = 2.456f;

    /// <summary>
    /// The game's body-text CAP HEIGHT in authored uGUI px, for the legibility figure the FIXED FIT
    /// line prints. MEASURED, not assumed: off '.planning/debug/character_ui_auflösung.jpg', whose
    /// character column is a known 328 authored px wide, the ability-card names and the equipment
    /// row labels both read 13-14 authored px of cap. Diagnostic only — nothing is placed or scaled
    /// from it. At the pinned 0.875 mm per authored px that is ~34 arc-minutes, comfortable; the two
    /// full-screen sub-views land at ~17, which is the honest cost of the 803 px slot.
    /// </summary>
    private const float FixedFitBodyCapPx = 13.5f;

    /// <summary>
    /// IS THIS THE MAP ROOM'S PERMANENT CHARACTER SCREEN — the one window this whole region applies
    /// to. Nothing else in the game is touched: every other converted window keeps the growth path
    /// (a host that hugs its visible content and re-fits when that content changes), which is what
    /// the story box, the quest popup, the merchant, the pause menu and every HUD conversion need.
    ///
    /// <para><b>WHY THIS PREDICATE WAS REWRITTEN — ModBuild 197 SHIPPED IT AND IT NEVER RETURNED
    /// TRUE ONCE.</b> The whole fixed-size feature was inert on hardware: the branch's own
    /// instrument line (<c>FIXED FIT</c>) appears ZERO times in 9 MB of fresh 197 log while
    /// <c>Host rect fit '…New Party display'</c> shows the growth path running as before
    /// (860→1920, 1920→328, 1066→1102→1138→1174→860). Exactly one of the three conjuncts was
    /// false, and the same log names it. The 197 gate asked
    /// <c>panel.Target.GetComponent&lt;NewPartyDisplayUI&gt;()</c>; the arc packer's
    /// <c>ModalFallback.IsPermanentPanel</c> asks the OTHER two conjuncts of the same panel —
    /// <c>panel.Target.GetComponent&lt;UIWindow&gt;()</c> and
    /// <see cref="ModalFallback.IsMapRoomPermanent"/> — and its verdict is printed in the 197 log:
    /// <c>[0°±23° 'GloomhavenVR.Panel_Modal_New Party display' PERMANENT/no-X]</c>. Both of those
    /// were TRUE. Only the <c>NewPartyDisplayUI</c> conjunct can have been false, i.e.
    /// <b><c>NewPartyDisplayUI</c> is not on the GameObject that carries the character screen's
    /// <c>UIWindow</c>.</b></para>
    ///
    /// <para><b>AND THE ARGUMENT THAT SAID IT MUST BE WAS READING THE WRONG LINE.</b> 197 justified
    /// the <c>GetComponent</c> with "NewPartyDisplayUI caches <c>window = GetComponent&lt;UIWindow&gt;()</c>
    /// in its own Awake (decompiled :277)". That statement is real but it is CONDITIONAL, and the
    /// condition is the half that was skipped: the line is
    /// <c>if ((object)window == null) { window = GetComponent&lt;UIWindow&gt;(); }</c>, and
    /// <c>window</c> is a <c>[SerializeField]</c> (NewPartyDisplayUI.cs:155-156) — a reference the
    /// PREFAB assigns, to any GameObject the prefab likes. The <c>GetComponent</c> is a fallback for
    /// an unassigned field, not a contract. This is nothing like <c>QuestLogManager</c>, where
    /// <c>[RequireComponent(typeof(UIWindow))]</c> makes Unity itself guarantee the pairing — and
    /// even there the guarantee is about the two components, never about which GameObject the
    /// CONVERSION happened to target.</para>
    ///
    /// <para><b>THE NEW TEST, AND WHY EACH HALF IS AN IDENTITY TEST.</b> Two independent exact
    /// identities are asked, and the window is the character screen if EITHER answers yes. Neither
    /// is a name match (localisation, <c>(Clone)</c> and prefab renames all defeat those) and
    /// neither is a containment test (<c>GetComponentIn{Parent,Children}</c> answers "related to an
    /// X", the slip this repo shipped twice):</para>
    /// <list type="number">
    /// <item><b>The window the LIVE party display drives.</b> <c>NewPartyDisplayUI.PartyDisplay</c>
    /// is the game's own handle on the single live party display
    /// (<c>Singleton&lt;APartyDisplayUI&gt;.Instance as NewPartyDisplayUI</c>, NewPartyDisplayUI.cs:246;
    /// <c>Singleton&lt;T&gt;</c> is a plain static set in Awake — reading it creates nothing). Its
    /// <c>OnShown</c> property IS its own window's event object (<c>public UnityEvent OnShown =&gt;
    /// window.onShown;</c>, :240) and <c>UIWindow.onShown</c> is a field initialised per instance
    /// (<c>public VisibilityEvent onShown = new VisibilityEvent();</c>, UIWindow.cs:135), so a
    /// REFERENCE comparison against this window's <c>onShown</c> is an instance-identity test on the
    /// <c>UIWindow</c> itself. It answers "the party display's window IS this window", with no
    /// assumption whatsoever about which GameObject either component sits on — which is precisely
    /// the assumption that failed. See <see cref="IsWindowOfPartyDisplay"/>.</item>
    /// <item><b>The window's own serialized identifier.</b> <c>window.ID == UIWindowID.PartyPanel</c>.
    /// <c>UIWindowID</c> is the game's own enum, serialized on the <c>UIWindow</c> and used by the
    /// game's own dispatch (<c>UIWindow.GetWindow(UIWindowID)</c>); it is what
    /// <c>ModalFallback.MapRoomPermanentIds</c> already keys the permanent set on, and it is what the
    /// 197 log prints for this very window (<c>'New Party display' (ID PartyPanel)</c>). It is an
    /// identifier, not a name: it survives localisation, cloning and renames.</item>
    /// </list>
    ///
    /// <para><b>WHY EITHER-OR IS NOT "LOOSENING THE GATE".</b> The two surviving conjuncts are
    /// unchanged and still do the containing work: the target must carry a <c>UIWindow</c>, and that
    /// window must satisfy <see cref="ModalFallback.IsMapRoomPermanent"/> — which is true for at
    /// most three windows in the game (<c>PartyPanel</c>, <c>PartyAssemblyWindow</c>, the quest log)
    /// and only while the map room stands. Test 2 alone already picks exactly ONE of those three.
    /// Test 1 exists so a future ID change cannot silence the feature the way this round's component
    /// assumption did, and the gate line below prints both answers side by side and says so out loud
    /// when they DISAGREE — so the next hardware log settles which of them is the durable one instead
    /// of another round of inference.</para>
    ///
    /// <para><b>AND IT CAN NEVER AGAIN BE SILENT.</b> Every converted panel gets one
    /// <c>FIXED FIT GATE</c> line, including — especially — the ones that do not match, and a fresh
    /// line whenever the verdict changes. "Not armed" and "armed and stable" can no longer look
    /// alike, because "not armed" now says so, names the target GameObject, and names which conjunct
    /// refused. See <see cref="LogFixedFitGate"/>.</para>
    ///
    /// <para><b>WHY IT IS <c>internal</c> AND NOT <c>private</c> (ModBuild 202).</b> The one question
    /// outside this file that has to be answered by exactly this predicate is whether
    /// <c>ModalFallback.DeriveWindowScale</c> may re-negotiate this window's physical width: the
    /// fixed fit sizes the host from its CONTENT (column + widest sub-view, see
    /// <see cref="FixedFitWidthPx"/>), and the board-relative cap answers a wider host by shrinking
    /// everything in it. A second copy of the identity test over there is exactly the drift this
    /// round's gate rewrite was paid for, so the test stays here and is merely reachable. Nothing in
    /// the mod calls it across files yet — see the end of the region note for the patch that would.
    /// </para>
    /// </summary>
    internal static bool IsFixedSizeWindow(ConvertedPanel panel)
    {
        if (panel.Target == null || panel.HostRect == null)
            return false;

        // CONJUNCT 1 + 2 (unchanged, and both PROVEN true for this window by the 197 log — see the
        // doc above): the converted root is a window root, and that window is one of the map room's
        // permanent, un-closable windows.
        var window = panel.Target.GetComponent<UIWindow>();
        bool permanent = window != null && ModalFallback.IsMapRoomPermanent(window);

        // CONJUNCT 3, REPLACED: which of the permanent windows is the CHARACTER SCREEN. Asked twice,
        // independently, as an identity — never as containment and never by name.
        NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
        // Reset before the short-circuit, or a panel that never asks would log the note left behind
        // by the previous panel that did — a stale instrument reading, which is the class of bug
        // this whole round is about.
        s_fixedFitOwnerNote = "not asked — this window is not one of the map room's permanent windows";
        bool ownedByPartyDisplay = permanent && IsWindowOfPartyDisplay(display, window);
        bool idIsCharacterScreen = permanent && window!.ID == UIWindowID.PartyPanel;
        bool armed = permanent && (ownedByPartyDisplay || idIsCharacterScreen);

        LogFixedFitGate(panel, window, display, permanent, ownedByPartyDisplay, idIsCharacterScreen,
            armed);
        return armed;
    }

    /// <summary>
    /// IS <paramref name="window"/> THE VERY <c>UIWindow</c> INSTANCE THE LIVE PARTY DISPLAY DRIVES —
    /// identity test 1 of <see cref="IsFixedSizeWindow"/>, argued in full there.
    ///
    /// <para>The comparison is <c>ReferenceEquals(display.OnShown, window.onShown)</c>: the property
    /// returns the display's OWN window's event object and every <c>UIWindow</c> constructs its own,
    /// so equal references mean one <c>UIWindow</c> instance. It reads no GameObject and walks no
    /// hierarchy, which is the whole point — the 197 gate failed on exactly the hierarchy assumption
    /// this avoids.</para>
    ///
    /// <para>THE PROPERTY CAN THROW, and that is handled rather than assumed away: <c>OnShown</c>
    /// dereferences the display's serialized <c>window</c> field, which the game leaves null if the
    /// prefab never assigned it AND the Awake fallback found no <c>UIWindow</c> on its own
    /// GameObject. A throw here means "the party display cannot name its own window", which is a
    /// NO for this test — it is never allowed to disarm the fit on its own, because identity test 2
    /// answers the same question from the other side. The reason string is carried into the gate log
    /// so the throw is visible instead of merely absorbed.</para>
    /// </summary>
    private static bool IsWindowOfPartyDisplay(NewPartyDisplayUI? display, UIWindow? window)
    {
        if (display == null)
        {
            s_fixedFitOwnerNote = "the game reports no live party display "
                                  + "(Singleton<APartyDisplayUI>.Instance is null)";
            return false;
        }
        if (window == null)
        {
            s_fixedFitOwnerNote = "this panel's target carries no UIWindow to compare against";
            return false;
        }
        object? shown;
        try
        {
            shown = display.OnShown;
        }
        catch (System.Exception ex)
        {
            s_fixedFitOwnerNote = $"the party display could not name its own window ({ex.GetType().Name}) "
                                  + "— its serialized 'window' reference is null and its Awake fallback "
                                  + "found nothing";
            return false;
        }
        bool same = shown != null && ReferenceEquals(shown, window.onShown);
        s_fixedFitOwnerNote = same
            ? "the live party display drives THIS UIWindow instance (its OnShown IS this window's "
              + "own onShown event object)"
            : "the live party display drives a DIFFERENT UIWindow instance";
        return same;
    }

    /// <summary>Why <see cref="IsWindowOfPartyDisplay"/> answered what it answered, for the gate log.
    /// Written and read in the same call chain, on the main thread, before any await-free return —
    /// so it is always the note that belongs to the verdict being logged.</summary>
    private static string s_fixedFitOwnerNote = string.Empty;

    /// <summary>
    /// THE GATE'S INSTRUMENT — the line that makes ModBuild 197's failure mode impossible to repeat.
    ///
    /// <para>197's entire cost was that a shipped feature produced NO LOG LINE AT ALL, so "the branch
    /// never ran" was indistinguishable from "the branch ran and the window was stable". This prints
    /// once for EVERY converted panel — armed or not, window or not, map room or not — and again
    /// whenever the verdict changes. A hardware log therefore always answers, for every panel: was
    /// the fixed fit even offered this window, and if not, which conjunct said no.</para>
    ///
    /// <para>It also carries the census 197 needed and did not have: where <c>NewPartyDisplayUI</c>
    /// actually SITS relative to the conversion target. That answer is diagnostic only — nothing in
    /// <see cref="IsFixedSizeWindow"/> decides anything from it — which is why it may safely be
    /// obtained by walking the hierarchy, the very thing a decision must not do.</para>
    /// </summary>
    private static void LogFixedFitGate(ConvertedPanel panel, UIWindow? window,
        NewPartyDisplayUI? display, bool permanent, bool ownedByPartyDisplay,
        bool idIsCharacterScreen, bool armed)
    {
        if (panel.HostGo == null || panel.Target == null)
            return;

        // The verdict as a bit pattern rather than a string: this runs on every fit check of every
        // converted panel, and a formatted key would allocate once per check forever just to be
        // thrown away. 0 is not a valid verdict (bit 0 is always set), so a fresh entry always logs.
        bool roomStanding = MapRoom.MapRoomDriver.Active;
        int key = 1
                  | (armed ? 2 : 0)
                  | (permanent ? 4 : 0)
                  | (ownedByPartyDisplay ? 8 : 0)
                  | (idIsCharacterScreen ? 16 : 0)
                  | (window != null ? 32 : 0)
                  | (roomStanding ? 64 : 0);
        int id = panel.HostGo.GetInstanceID();
        if (FixedFitGateLogs.TryGetValue(id, out FixedFitGateLog? seen) && seen != null
            && seen.Key == key && seen.Owner == panel.HostGo)
            return;
        FixedFitGateLogs[id] = new FixedFitGateLog { Owner = panel.HostGo, Key = key };
        PruneFixedFitGateLogs();

        string host = panel.HostGo.name;
        string target = $"'{panel.Target.name}' (path {FixedFitGatePath(panel.Target)})";

        if (window == null)
        {
            // Short form: this is an ordinary HUD/panel conversion, not a window root at all. It
            // still prints, because silence is the failure mode this line exists to remove.
            VRLog.Info("WorldUI", $"FIXED FIT GATE '{host}': NOT ARMED — the conversion target {target} "
                                  + "carries no UIWindow, so it is not a game window root and the "
                                  + "fixed-size branch does not apply to it. It keeps the normal growth "
                                  + "fit, byte-for-byte as in every build before this one.");
            return;
        }

        VRLog.Info("WorldUI",
            $"FIXED FIT GATE '{host}': {(armed ? "ARMED" : "NOT ARMED")} — conversion target {target}; "
            + $"target carries NewPartyDisplayUI: {(panel.Target.GetComponent<NewPartyDisplayUI>() != null ? "YES" : "NO")}; "
            + $"{DescribePartyDisplaySite(panel, display)}; "
            + $"target carries UIWindow: YES (ID {window.ID}); map room standing: {roomStanding}; "
            + $"IsMapRoomPermanent: {permanent}; identity 1 — the live party display drives this "
            + $"window: {(ownedByPartyDisplay ? "YES" : "NO")} ({s_fixedFitOwnerNote}); identity 2 — "
            + $"window ID is PartyPanel: {(idIsCharacterScreen ? "YES" : "NO")}. "
            + (armed
                ? "The host rect is pinned and the sub-views are fitted into it (the FIXED FIT lines "
                  + "carry the sizes)."
                : permanent
                    ? "This IS one of the map room's permanent windows, but neither identity test "
                      + "calls it the character screen — so it keeps the growth fit, which is correct "
                      + "for the quest log and the assembly window."
                    : "The growth fit keeps this window (it is not one of the map room's permanent "
                      + "windows while the room stands).")
            + (permanent && ownedByPartyDisplay != idIsCharacterScreen
                ? " ** THE TWO IDENTITY TESTS DISAGREE ABOUT THIS WINDOW.** They are independent by "
                  + "design and either one arms the fit, so nothing is broken right now — but one of "
                  + "them is wrong about this window and this line is the only place that says so. "
                  + "Report it: the fix is to drop the loser, not to keep both."
                : string.Empty)
            + " HOW TO READ THIS LINE: it prints once per converted panel and again on every verdict "
            + "change, so a hardware log that contains NO 'FIXED FIT GATE' line for a window means the "
            + "content fit never ran for it at all — not that the gate refused it. ModBuild 197 shipped "
            + "a fixed-size branch that produced total silence and that silence was read as 'armed and "
            + "stable'; an instrument that cannot distinguish its own failure modes agrees with every "
            + "broken build.");
    }

    /// <summary>
    /// WHERE <c>NewPartyDisplayUI</c> ACTUALLY SITS relative to the conversion target — the census
    /// ModBuild 197 needed. DIAGNOSTIC ONLY: this walks the hierarchy in both directions, which is
    /// exactly what an identity DECISION must never do (see <see cref="IsFixedSizeWindow"/>), and
    /// nothing here feeds the decision.
    /// </summary>
    private static string DescribePartyDisplaySite(ConvertedPanel panel, NewPartyDisplayUI? display)
    {
        RectTransform target = panel.Target;
        if (display == null)
            return "the game reports no live party display singleton, so there is nothing to locate";
        Transform site = display.transform;
        if (ReferenceEquals(site, target))
            return "the live NewPartyDisplayUI is ON the conversion target itself (0 levels away)";
        int up = FixedFitLevelsUp(target, site);
        if (up > 0)
            return $"the live NewPartyDisplayUI sits on an ANCESTOR of the target, {up} level(s) UP "
                   + $"('{site.name}')";
        int down = FixedFitLevelsUp(site, target);
        if (down > 0)
            return $"the live NewPartyDisplayUI sits on a DESCENDANT of the target, {down} level(s) "
                   + $"DOWN ('{site.name}')";
        return $"the live NewPartyDisplayUI ('{site.name}', path {FixedFitGatePath(site)}) is in an "
               + "UNRELATED subtree — neither an ancestor nor a descendant of the target";
    }

    /// <summary>Levels from <paramref name="node"/> up to <paramref name="ancestor"/>; 0 when
    /// <paramref name="ancestor"/> is not an ancestor of <paramref name="node"/>.</summary>
    private static int FixedFitLevelsUp(Transform node, Transform ancestor)
    {
        int levels = 0;
        for (Transform? t = node.parent; t != null; t = t.parent)
        {
            levels++;
            if (ReferenceEquals(t, ancestor))
                return levels;
        }
        return 0;
    }

    /// <summary>Hierarchy path of a transform, for the gate line. Bounded so a pathological tree
    /// cannot produce an unbounded log string.</summary>
    private static string FixedFitGatePath(Transform node)
    {
        var sb = new System.Text.StringBuilder(96);
        int guard = 0;
        for (Transform? t = node; t != null && guard < 16; t = t.parent, guard++)
        {
            if (sb.Length > 0)
                sb.Insert(0, '/');
            sb.Insert(0, t.name);
        }
        return sb.ToString();
    }

    /// <summary>One gate verdict already reported for one host, so the line repeats only on a real
    /// change of verdict.</summary>
    private sealed class FixedFitGateLog
    {
        internal GameObject? Owner;
        internal int Key;
    }

    private static readonly Dictionary<int, FixedFitGateLog> FixedFitGateLogs = new(4);

    /// <summary>Drop entries whose host GameObject is gone (same pattern as the fit-loop table).</summary>
    private static void PruneFixedFitGateLogs()
    {
        if (FixedFitGateLogs.Count < 8)
            return;
        List<int>? dead = null;
        foreach (KeyValuePair<int, FixedFitGateLog> pair in FixedFitGateLogs)
        {
            if (pair.Value == null || pair.Value.Owner == null)
                (dead ??= new List<int>(4)).Add(pair.Key);
        }
        if (dead == null)
            return;
        for (int i = 0; i < dead.Count; i++)
            FixedFitGateLogs.Remove(dead[i]);
    }

    /// <summary>
    /// ONE MEMBER OF THE ACTIVE SUB-VIEW GROUP — a game sub-view root we place, plus the pose it had
    /// before we ever touched it. Everything we write is written ABSOLUTELY (home + our offset), never
    /// incrementally, so a pass that finds the member somewhere else corrects it exactly instead of
    /// accumulating; and <see cref="ReleaseFixedFit"/> can put it back exactly.
    /// </summary>
    private sealed class SubViewFit
    {
        /// <summary>The sub-view's own root (the GameObject its <c>UIWindow</c>/display component
        /// sits on). Unity-null once the game destroys it → dropped.</summary>
        internal Transform? View;

        /// <summary>The pose the sub-view has when we are not placing it. RE-READ every pass until
        /// the first write: a show animation (<c>LeanTweenGuiAnimationSettingScale/Move</c> can drive
        /// a serialized <c>RectTransform</c> target's <c>localScale</c>/<c>anchoredPosition</c>) is
        /// still in flight when we FIRST see the sub-view, and the settle gate holds our first write
        /// off for ~0.8 s — so by the time it freezes, the home is the settled one.</summary>
        internal Vector3 HomeScale = Vector3.one;
        internal Vector2 HomeAnchored;
        internal bool Written;

        // ---- live, refilled by every measure pass -------------------------------------------
        internal bool Visible;
        internal Vector2 Min, Max;
        internal int Graphics;

        /// <summary>The same union built WITHOUT the transient-content rule (ModBuild 201) — the
        /// fallback used when the clean reading has no graphics at all, so an exclusion can never
        /// delete a sub-view. <see cref="UsedRawUnion"/> says which one the pass used.</summary>
        internal Vector2 RawMin, RawMax;
        internal int RawGraphics;
        internal bool UsedRawUnion;

        /// <summary>Transient graphics dropped from THIS member this pass, cumulative over its
        /// life — so "no mouseover was ignored" and "no mouseover happened" are different logs.</summary>
        internal int TransientDropped;

        /// <summary>Host-local position of the member's own origin (its pivot).</summary>
        internal Vector2 Pivot;

        /// <summary>Scale factor and host-local offset currently ON the member relative to its home —
        /// read back off the transform, not remembered, so somebody else's write is visible.</summary>
        internal float Applied = 1f;
        internal Vector2 AppliedShift;

        /// <summary>Host-local units per parent-local unit, for converting a wanted offset into an
        /// <c>anchoredPosition</c>.</summary>
        internal float HostPerParent = 1f;

        // ---- solved by SolveSubViewPlacement, in host-local px --------------------------------

        /// <summary>The member's geometry with OUR scale and offset undone — what it would measure
        /// if this fit had never touched it.</summary>
        internal Vector2 NaturalMin, NaturalMax, NaturalPivot;

        /// <summary>The host-local offset from the member's natural pose that the group transform
        /// asks for. Written as <c>HomeAnchored + WantShift / HostPerParent</c>.</summary>
        internal Vector2 WantShift;

        /// <summary>The pose we last WROTE for this member. Finding the member somewhere else while
        /// this is unchanged is a foreign write and nothing else — see the concede branch.</summary>
        internal float WrittenScale = 1f;
        internal Vector2 WrittenShift;

        // ---- the ModBuild 200 seating report, host-local px, filled by SolveSubViewPlacement ----

        /// <summary>Where this member ENDS UP once the solved scale and shift are on it — the rect
        /// the log prints, so "seated flush" is a stated number and not an intention.</summary>
        internal Vector2 SeatedMin, SeatedMax;

        /// <summary>Empty px between the column seam and this member's left edge, and px of this
        /// member drawn left of the seam. The group's LEFTMOST member has 0 of each by
        /// construction; a second member open at the same time keeps its own offset from it, which
        /// is its own arrangement and not a gap this fit introduced.</summary>
        internal float GapPx, OverlapPx;

        /// <summary>ModBuild 201 — THE FROZEN SEAT. Once a solution has been written for the open
        /// set this member belongs to, its host-local offset is a CONSTANT for as long as that set
        /// stays open: no later measurement may re-seat a view that is already on screen. Cleared by
        /// <see cref="FixedFitState.ReleaseSolution"/> when the open set changes.</summary>
        internal Vector2 FrozenWantShift;
        internal bool WantFrozen;
    }

    /// <summary>Per-panel state of the fixed-size fit — the captured size, the pinned column corner,
    /// the sub-view group we place, the settle gate, and the counters every log line carries.</summary>
    private sealed class FixedFitState
    {
        /// <summary>Host GameObject this entry belongs to (Unity-null once destroyed → pruned).</summary>
        internal GameObject? Owner;

        /// <summary>The fixed host size, captured ONCE and never re-derived — that is what makes
        /// "one size" a guarantee rather than a tendency.</summary>
        internal Vector2 Size;

        internal bool SizeCaptured;

        /// <summary>THE COLUMN'S PIN: the host-local BOTTOM-LEFT corner the base union (everything
        /// that is not an open sub-view — in practice the character column, the party name and the
        /// gold row) is held at, captured once and then only ever re-asserted. The bottom-left corner
        /// rather than the centre on purpose: a base that grows a row at the top must not slide the
        /// whole column downward to keep a centre.</summary>
        internal Vector2 BasePin;
        internal bool BasePinned;
        internal Vector2 BaseSizeAtPin;

        /// <summary>THE COLUMN SEAM (ModBuild 200): the host-local x every open sub-view's LEFT edge
        /// is seated on, so the gap and the overlap are both 0 px by construction and identical for
        /// all six views. Captured ONCE, on a pass with no sub-view open, from the base union's own
        /// right edge — never from the live union, which the 199 hardware log shows reading 886 and
        /// 939 px instead of 328 whenever transient hover content lands in it. See the ModBuild 200
        /// block in the region note.</summary>
        internal float ColumnSeamX;
        internal bool SeamCaptured;

        /// <summary>The base width the seam was taken from, and whether the safety clamp had to
        /// pull the seam back off a provisional (sub-view-contaminated) reading — both printed, so
        /// a seam that is not the column cannot look like one that is.</summary>
        internal float SeamBaseWidth;
        internal bool SeamClamped;

        /// <summary>Set once if the captured seam was RE-DERIVED because the column later measured
        /// materially NARROWER than the width the seam was taken from — see
        /// <see cref="EnsureColumnSeam"/>. Carries the two widths so the log states the correction
        /// rather than only its effect.</summary>
        internal bool SeamReDerived;
        internal float SeamWidthBeforeReDerive;

        /// <summary>GRAPHICS THIS MOD PARKED INTO THE GAME'S WINDOW and the base measurement
        /// therefore refused: this pass, and the running total over this window's life. Zero and
        /// never-checked must not look alike, so both are printed on every line — the whole of the
        /// ModBuild 445 fix is that these can no longer move the seam, and a rule that never fires
        /// proves nothing [[gated-remedy-never-ran]].</summary>
        internal int ParkedGuestsThisPass;
        internal int ParkedGuestsRefused;

        /// <summary>The furthest-right edge any refused parked graphic reached this pass, and the
        /// name of the graphic that reached it — i.e. exactly how far the seam WOULD have been
        /// pushed. Meaningless while <see cref="ParkedGuestsThisPass"/> is 0.</summary>
        internal float ParkedGuestRightX;
        internal string ParkedGuestWidest = string.Empty;

        // ---- live base reading, refilled by every measure pass (the proof line) ---------------
        internal bool BaseVisible;
        internal Vector2 BaseMin, BaseMax;
        internal int BaseGraphics;

        /// <summary>The base union built WITHOUT the transient-content rule, and whether the pass had
        /// to fall back to it because the clean reading was empty (ModBuild 201 safety net).</summary>
        internal Vector2 BaseRawMin, BaseRawMax;
        internal int BaseRawGraphics;
        internal bool BaseUsedRawUnion;

        // ---- the ModBuild 201 mouseover ledger -------------------------------------------------

        /// <summary>Transient graphics (hover previews, item hints, modifier flyouts) this pass
        /// refused to measure, and the running total over this window's life. Printed on every line
        /// BECAUSE zero and never-checked must not look alike — the whole of report (5) is that a
        /// mouseover can no longer move anything, and a rule that never fires proves nothing.</summary>
        internal int TransientThisPass;
        internal int TransientIgnored;

        /// <summary>Bit per family index of <see cref="TransientFamilyOf"/> seen so far, and the
        /// widest transient graphic's owning family + rect this pass — so the line NAMES what was
        /// ignored instead of only counting it.</summary>
        internal int TransientFamilyMask;
        internal string TransientWidest = string.Empty;
        internal Vector2 TransientWidestSize;

        // ---- the ModBuild 201 content-extreme census (diagnostic, decides nothing) -------------

        /// <summary>The non-plate, non-transient graphics that DEFINE the open sub-view group's
        /// content union in x — the measurement the "can perks reach 1.000?" question needs and has
        /// never had. A width alone cannot be acted on; the two graphics that produce it can.</summary>
        internal string ContentLeftName = string.Empty, ContentRightName = string.Empty;
        internal Vector2 ContentLeftSize, ContentRightSize;
        internal float ContentLeftX, ContentRightX;

        // ---- the ModBuild 201 solution freeze ---------------------------------------------------

        /// <summary>Signature of the set of sub-views the frozen solution was solved for. A change of
        /// signature — and NOTHING else — releases the freeze.</summary>
        internal int SolutionSignature;

        /// <summary>Signature of the set of sub-views open on the LAST measured pass. Compared with
        /// <see cref="SolutionSignature"/> to decide whether the freeze still applies.</summary>
        internal int OpenSignature;
        internal bool SolutionFrozen;

        /// <summary>The group seat the frozen solution was solved with. There is no frozen SCALE
        /// beside it: from ModBuild 202 the scale is the constant 1.000 for every sub-view, so the
        /// seat is the only thing a solution still consists of.</summary>
        internal Vector2 FrozenGroupShift;

        /// <summary>Drop the frozen sub-view solution (the open set changed). The members keep their
        /// homes and their written poses; only the SOLUTION is re-opened for one more solve.</summary>
        internal void ReleaseSolution()
        {
            SolutionFrozen = false;
            for (int i = 0; i < Views.Count; i++)
                Views[i].WantFrozen = false;
        }

        /// <summary>The sub-views the game currently has open inside this window.</summary>
        internal readonly List<SubViewFit> Views = new(4);

        /// <summary>Group scale and group alignment we last wrote (host-local px).</summary>
        internal float ViewScale = 1f;
        internal Vector2 ViewShift;

        /// <summary>Candidate of the previous check, and how many consecutive checks it has held —
        /// the settle gate (see <see cref="FixedFitSettleChecks"/>).</summary>
        internal float PendingScale = 1f;
        internal Vector2 PendingShift;

        /// <summary>The COLUMN correction of the previous check (ModBuild 201). The base re-assert is
        /// behind the same gate as the sub-view pose now, and this is the quantity that has to
        /// repeat: an animation ramp produces a different correction every sample and is therefore
        /// never written, while a real dislocation produces the same one twice and is.</summary>
        internal Vector2 PendingBaseShift;
        internal int PendingChecks;

        // ---- last sub-view report, for the log ------------------------------------------------
        internal string ViewName = "none";
        internal Vector2 ViewNeed;
        internal Vector2 ViewContentNeed;
        internal int ViewPlates;
        internal string PlateName = string.Empty;
        internal Vector2 PlateSize;

        /// <summary>The GROUP's seating report (worst case over the open members): px of empty
        /// frame between the seam and the leftmost seated edge, px drawn left of the seam, px drawn
        /// right of the frame, and the slot width the scale was solved against. Both of the first
        /// two must read 0 — that is the whole of reports (a) and (c).</summary>
        internal float ViewGapPx;
        internal float ViewOverlapPx;

        /// <summary>Px of the open group drawn past the frame's right edge, and past its top or
        /// bottom edge. From ModBuild 202 a view that does not fit is drawn at full size and spills
        /// rather than being scaled down, so these two are the whole of "does the fixed width still
        /// hold?" — the host is sized so both read 0 for every sub-view ever measured.</summary>
        internal float ViewSpillPx;
        internal float ViewSpillYPx;
        internal float ViewSlotPx;

        internal int Comparisons;
        internal int Deviations;
        internal int Deferred;
        internal int HostWrites;
        internal int BaseWrites;
        internal int ScaleWrites;
        internal int Shifts;

        /// <summary>How often a pass found OUR sub-view pose overwritten by somebody else — the
        /// write-war counter. A number that climbs every pass means the game is animating the root we
        /// place and the next round must place something else instead.</summary>
        internal int ReAsserts;

        /// <summary>The write war has been conceded, and said so once.</summary>
        internal bool ConcededLogged;

        internal float LastLogTime = float.NegativeInfinity;

        // ---- the sub-view settle burst (user report 2026-09-03) — see part 9c -----------
        //
        // Appended at the END of this class on purpose: the refactor guard tracks member
        // order, and nothing that already existed moves. None of these has an initializer
        // except BurstLastReported, which needs a value no real outcome can take (an outcome
        // is (+/-1)*(frames+1), so it is never 0).

        /// <summary>Last open-set signature seen by <c>TickSubViewBurst</c>, and whether one
        /// has ever been taken. NOT comparable with <see cref="OpenSignature"/> — the two are
        /// different signatures of different granularity; part 9c's doc says why.</summary>
        internal int BurstSignature;
        internal bool BurstSigValid;

        /// <summary>Checks the burst in flight may still force, the frame the next one is due
        /// on, and the frame the burst was armed on.</summary>
        internal int BurstChecksLeft;
        internal int BurstNextCheckFrame;
        internal int BurstStartFrame;

        /// <summary>Checks this burst has actually run, and whether it ended because the fit
        /// settled (rather than by spending its cap).</summary>
        internal int BurstChecksRun;
        internal bool BurstSettled;

        /// <summary>A burst is armed and has not yet reported its outcome. SEPARATE from
        /// <see cref="BurstChecksLeft"/> on purpose: the last forced check DECREMENTS that counter
        /// to zero and only then runs, so "no checks left" and "this burst is over" are two
        /// different facts and reading one as the other reports an expiry for a burst that goes on
        /// to settle in the very same frame.</summary>
        internal bool BurstActive;

        /// <summary>Session totals for the report: bursts armed, bursts that spent every
        /// check without settling, and the worst frames from an open-set change to the seat
        /// landing — the number that bounds how long a wrong seat was on screen.</summary>
        internal int Bursts;
        internal int BurstsExpired;
        internal int BurstWorstFrames;

        /// <summary>The last outcome printed, so an identical one stays quiet.</summary>
        internal int BurstLastReported;

        /// <summary>The PEAK visible-graphic count and drawn union this burst has measured, and
        /// the count on its FIRST pass. The flash the user reported (2026-09-03 (b)) IS this
        /// number: 85-128 graphics settled against 597-696 while the whole character-management
        /// tree paints. Before the burst the fit sampled every 30 frames and caught that state
        /// four times in a session BY LUCK; the burst measures every open-set change, so the peak
        /// is now a census rather than an anecdote.</summary>
        internal int BurstPeakGraphics;
        internal int BurstFirstGraphics;
        internal Vector2 BurstPeakUnion;

        // ---- ModBuild 396: WHICH GRAPHIC MADE THE COLUMN WIDE ------------------------------
        //
        // The 395 log settles two questions and opens this one. The column is measured every fit
        // pass, and it caught the flash TWICE in that session — "the CHARACTER COLUMN renders
        // 1964x1080 px … [99 graphic(s)] … GREW by 1636,0 px since the pin", against a settled
        // 328x1080 at 85-86 graphics. So the flash is not a pre-Start paint (24 pre-Start windows
        // all session, 0 accepted) and not a stale canvas restore (PRE-START RESTORE WITHHELD
        // 0 of 0): it is ~13 extra graphics that paint across the FULL width of the window and
        // 384 px below it, and no instrument in the tree names one of them.
        //
        // These three hold the transforms at the extremes of the column's own union on the
        // current pass. They are live only within the pass that wrote them and are resolved to
        // text immediately, by the report below, on the passes where the column overspilled.
        internal Transform? BaseEdgeLeft;
        internal Transform? BaseEdgeRight;
        internal Transform? BaseEdgeBottom;
        internal float BaseEdgeLeftX;
        internal float BaseEdgeRightX;
        internal float BaseEdgeBottomY;

        /// <summary>Printed COLUMN OVERSPILL lines so far — capped, so a window that overspills
        /// every pass cannot flood the log the way ModBuild 331 was asked to stop.</summary>
        internal int OverspillLines;

        // ---- the sub-view SEAT VEIL (user report 2026-09-05) — see part 9g ------------------
        //
        // Appended at the END of this class for the reason the burst block above gives: the
        // refactor guard tracks member order and nothing that already existed may move. Only the
        // two lists carry an initializer, and they are INSTANCE fields — check-partial-order.py
        // is about STATIC initialiser order across parts, which this adds nothing to.

        /// <summary>The open-set signature whose sub-view seat the fixed fit has actually WRITTEN,
        /// and whether one has ever been written. This is the veil's release condition, stated as
        /// a fact about the seat rather than as an elapsed time.</summary>
        internal int SeatedSignature;
        internal bool SeatedValid;

        /// <summary>The open-set signature the veil is currently holding for, and whether it
        /// stands. A veil is keyed on the SET, not on a member: a set that changes mid-hold hands
        /// back what it holds and re-raises, so the two can never be confused.</summary>
        internal int SeatVeilSignature;
        internal bool SeatVeilActive;

        /// <summary>The frame and unscaled time the standing veil was raised on — the two halves
        /// of the gap the report states, in frames AND in milliseconds.</summary>
        internal int SeatVeilStartFrame;
        internal float SeatVeilStartTime;

        /// <summary>An open set the deadline already released once. It is never veiled again, so a
        /// window whose seat can never be solved cannot strobe.</summary>
        internal int SeatVeilGaveUpSignature;
        internal bool SeatVeilGaveUpValid;

        /// <summary>Session totals: veils raised, veils lifted BY THE SEAT LANDING, veils released
        /// by the deadline, and the worst gap in frames. The middle two are the yes/no the report
        /// turns into a sentence — a veil that was lifted by the seat means the first visible frame
        /// of that sub-view carried the final seat.</summary>
        internal int SeatVeilRaised;
        internal int SeatVeilLiftedOnSeat;
        internal int SeatVeilOverdue;
        internal int SeatVeilWorstFrames;
        internal int SeatVeilWorstMillis;
        internal int SeatVeilRenderers;

        /// <summary>The last outcome printed, so an identical one stays quiet.</summary>
        internal int SeatVeilLastReported;

        /// <summary>Foreign alpha writes LEARNED while this veil stood — the same accounting the
        /// hidden-window veil keeps, and for the same reason: the materialise runner writes this
        /// channel every LateUpdate of an appear, and the value it left is the one a lift must hand
        /// back rather than overwrite. [[a-hide-saved-a-foreign-value]]</summary>
        internal int SeatVeilLearned;

        // ---- ModBuild 435: the SEAT-STAMP REFUSAL, and the SEAT WATCH — see part 9g ----------
        //
        // Appended at the END for the reason the two blocks above give: the refactor guard tracks
        // member order and nothing that already existed may move. One instance-field initializer
        // (WatchName), matching ViewName's; check-partial-order.py is about STATIC initialiser
        // order across parts and this adds nothing to it.

        /// <summary>How many times the SETTLED branch DECLINED to stamp a seat because a sub-view
        /// was open by the game's own reckoning and the measurement could not see it yet (the
        /// show fade). Printed on the FIXED FIT line so "the branch was never reached" and "it was
        /// reached and it declined" can never read alike. [[a-held-instrument-reads-as-dead]]</summary>
        internal int SeatUnseen;

        /// <summary>The open set the watch is following, and whether it is following one. One
        /// EPISODE = one open set, from the frame the game shows it to the frame its seat exists
        /// or the set changes.</summary>
        internal int WatchSignature;
        internal bool WatchActive;

        /// <summary>The element being watched: the last member of the open set (the display's own
        /// serialized order puts the battle-goal picker last), its name, and one CanvasRenderer
        /// under it used ONLY to read <c>GetInheritedAlpha()</c> — the ancestors' CanvasGroup
        /// product, which the seat veil's own alpha writes provably do not touch.</summary>
        internal Transform? WatchRoot;
        internal string WatchName = "none";
        internal CanvasRenderer? WatchAlphaProbe;
        internal int WatchMembers;

        /// <summary>The frame and unscaled time the episode opened.</summary>
        internal int WatchStartFrame;
        internal float WatchStartTime;

        /// <summary>THE FIRST FRAME THE PLAYER COULD SEE IT — not veiled by us AND above the fit's
        /// own visibility floor — with the element's centre in its PARENT'S local frame at that
        /// moment. <c>WatchDrawn</c> false at the end of an episode is the GOOD reading: nothing
        /// of it ever reached the eye before its seat existed.</summary>
        internal bool WatchDrawn;
        internal int WatchFirstDrawFrame;
        internal float WatchFirstDrawTime;
        internal Vector2 WatchFirstDrawCentre;

        /// <summary>THE FRAME THE SEAT EXISTED, and the same centre re-read there. The difference
        /// of the two centres IS the pop, measured on the picture rather than asserted from this
        /// veil's own bookkeeping. [[instruments-measured-the-bookkeeping]]</summary>
        internal bool WatchSeated;
        internal int WatchSeatFrame;
        internal float WatchSeatTime;
        internal Vector2 WatchSeatCentre;

        /// <summary>Session totals: episodes opened, episodes in which no frame of the sub-view was
        /// ever drawn before its seat existed, and the worst measured drop in parent-local px.
        /// <c>WatchEpisodes</c> is the UNCONDITIONAL liveness field: zero of them in a session that
        /// opened the character screen means this watch never ran, which is a different fact from
        /// "it ran and found nothing to correct".</summary>
        internal int WatchEpisodes;
        internal int WatchEpisodesClean;
        internal int WatchWorstDropPx;

        /// <summary>The last outcome printed, so an identical one stays quiet.</summary>
        internal int WatchLastReported;
    }

    /// <summary>Fixed-fit state by host GameObject instance ID.</summary>
    private static readonly Dictionary<int, FixedFitState> FixedFits = new(2);

    /// <summary>Fallback entry for a panel whose host is already gone — nothing reads it.</summary>
    private static readonly FixedFitState OrphanFixedFit = new();

    private static FixedFitState GetFixedFit(ConvertedPanel panel)
    {
        if (panel.HostGo == null)
            return OrphanFixedFit;
        int id = panel.HostGo.GetInstanceID();
        if (FixedFits.TryGetValue(id, out FixedFitState? entry) && entry != null)
            return entry;
        FixedFits[id] = entry = new FixedFitState { Owner = panel.HostGo };
        PruneFixedFits();
        return entry;
    }

    /// <summary>Drop entries whose host GameObject is gone (same pattern as the fit-loop table).</summary>
    private static void PruneFixedFits()
    {
        if (FixedFits.Count < 2)
            return;
        List<int>? dead = null;
        foreach (KeyValuePair<int, FixedFitState> pair in FixedFits)
        {
            if (pair.Value == null || pair.Value.Owner == null)
                (dead ??= new List<int>(2)).Add(pair.Key);
        }
        if (dead == null)
            return;
        for (int i = 0; i < dead.Count; i++)
            FixedFits.Remove(dead[i]);
    }

    /// <summary>
    /// HAND A PANEL BACK TO THE GROWTH PATH. Called on every fit of a panel that is NOT (or is no
    /// longer) a fixed-size window — in practice the moment the map room is torn down under a
    /// character screen that is somehow still converted. Every sub-view pose we wrote is put back
    /// exactly (we kept each one's home), because nothing else in the mod knows we moved them and the
    /// growth path would otherwise measure a window whose sub-views are permanently shrunk. Costs one
    /// dictionary lookup on a table that is empty for every other window in the game.
    /// </summary>
    private static void ReleaseFixedFit(ConvertedPanel panel)
    {
        if (FixedFits.Count == 0 || panel.HostGo == null)
            return;
        int id = panel.HostGo.GetInstanceID();
        if (!FixedFits.TryGetValue(id, out FixedFitState? fx) || fx == null)
            return;
        // Part 9g: hand back anything the seat veil is holding BEFORE the entry leaves the table —
        // after the Remove nothing would ever look at it again and the held renderers would be
        // stranded invisible. Unconditional, exactly like MapTravelConfirm's hold-down release.
        LiftSubViewSeatVeil(panel, fx, overdue: false, silent: true);
        FixedFits.Remove(id);

        int restored = 0;
        for (int i = 0; i < fx.Views.Count; i++)
        {
            SubViewFit v = fx.Views[i];
            if (!v.Written || v.View == null)
                continue;
            v.View.localScale = v.HomeScale;
            if (v.View is RectTransform vr)
                vr.anchoredPosition = v.HomeAnchored;
            restored++;
        }
        if (restored == 0)
            return;
        VRLog.Info("WorldUI", $"FIXED FIT RELEASED '{panel.HostGo.name}': this window is no longer the " +
                              "map room's permanent character screen, so the " + restored +
                              " sub-view(s) this fit had placed are restored to the pose the game gave " +
                              "them and the normal growth fit takes over. fixed fit: " +
                              $"{fx.Comparisons} comparison(s) made, {fx.Deviations} deviation(s) found, " +
                              $"{fx.HostWrites} host-size write(s), {fx.BaseWrites} column re-assert(s), " +
                              $"{fx.ScaleWrites} sub-view scale write(s), {fx.ReAsserts} foreign overwrite(s).");
    }

    /// <summary>
    /// THE FIXED-SIZE FIT. Replaces the growth path for the one window
    /// <see cref="IsFixedSizeWindow"/> matches: the host rect is pinned to a size captured once, and
    /// the content is fitted INTO it instead of the other way round. See the region note above for
    /// the measurement, the choice of size and the trade that was accepted.
    ///
    /// <para>THREE THINGS ARE WRITTEN, EACH ONLY WHEN IT IS WRONG, AND EACH ON A DIFFERENT OBJECT —
    /// which is the whole correction ModBuild 199 makes:</para>
    /// <list type="number">
    /// <item>the HOST SIZE, once, at the first fit (and never again — nothing else writes it);</item>
    /// <item>the conversion TARGET's anchoredPosition, to seat the BASE — the character column and
    /// the furniture around it — on a corner captured once. Its SCALE is never written: the target's
    /// subtree contains the column, and scaling it is exactly what made the user report the same
    /// complaint three builds running;</item>
    /// <item>an anchoredPosition on each OPEN SUB-VIEW's own root, seating it on the column seam, and
    /// a uniform localScale that is now always the identity (ModBuild 202: the sub-view scale is the
    /// constant 1.000, so this write only ever puts back a scale somebody ELSE wrote). The host is
    /// sized to hold the widest sub-view at 1.000 instead — see <see cref="FixedFitWidthPx"/>.</item>
    /// </list>
    ///
    /// <para>WHY THE BASE IS PINNED BY A CORNER AND NOT BY A UNION. ModBuild 198 pinned the LEFT EDGE
    /// OF THE WHOLE UNION, on the reasoning that the column is the union's leftmost element in every
    /// measured state and would therefore stay put. It is the leftmost element — and it still moved,
    /// because the union it was pinned by is measured AFTER the content scale, so a tab change
    /// re-derived a ±400 px target shift (its own log: +400, −400, +401, −401). A quantity that is
    /// re-derived from something a sub-view can change is not a pin. This one is captured once, from
    /// the base alone, and afterwards only ever re-asserted onto the same corner — bottom-left rather
    /// than centre, so a base that grows a row at the top does not slide the column down to keep a
    /// centre. The empty space is always to the RIGHT of the column, which is where every sub-view
    /// opens: the window becomes a board that things appear on.</para>
    ///
    /// <para>THE HOST IS NEVER RE-POSED BY THIS (ModBuild 193 ruling, "windows must not move once
    /// spawned"). Nothing here touches the host transform; every position write is an
    /// anchoredPosition INSIDE the host. The applied-fit generation — which is what re-arms
    /// ModalFallback's pose re-place — is advanced only when the HOST SIZE itself changed, i.e. once
    /// per window life, before the reveal. A tab change re-scales and re-seats a sub-view inside a
    /// host that keeps its rect, so it cannot re-arm a placement at all.</para>
    /// </summary>
    private static bool ApplyFixedFit(ConvertedPanel panel, RectTransform root,
        Vector2 size, Vector2 center)
    {
        FixedFitState fx = GetFixedFit(panel);
        fx.Comparisons++;
        return ApplyFixedFitCore(panel, root, fx, size, center);
    }

    private static bool ApplyFixedFitCore(ConvertedPanel panel, RectTransform root,
        FixedFitState fx, Vector2 size, Vector2 center)
    {

        // The measure and both writes below are expressed in the conversion frame, so it is repaired
        // BEFORE anything reads the measurement — exactly as ApplyFitConverging does, and one step
        // earlier, because a corrected frame invalidates the size/center we were handed. The height
        // cap is excluded: this window is not in the capped family and the fixed height bounds it.
        // On a healthy panel (the steady-state guard re-asserts every frame for modal hosts) this is
        // six compares and no writes, and the re-measure never runs.
        //
        // ORDERING (ModBuild 198): this runs BEFORE the size capture below, not after it. The
        // captured size is the one number in this whole region that is never re-derived — "one size"
        // is a guarantee precisely because nothing writes it twice — so it must not be read out of a
        // frame the guard is about to correct. ModBuild 23 found this very target at
        // `localScale 0.14` with its rect grown from 1080 to 2040 px; capturing in that state would
        // have pinned the window at a nonsense rect for its entire life, and the sanity bounds below
        // (2160 px) would not have caught it.
        if (ReassertConversionFrame(panel, out _, includeHeightCap: false)
            && TryMeasureContent(panel, root, out Vector2 freshSize, out Vector2 freshCenter))
        {
            size = freshSize;
            center = freshCenter;
        }

        if (!fx.SizeCaptured)
        {
            // THE WIDTH IS A CONSTANT, NOT A READING (ModBuild 202): it is the column plus the widest
            // sub-view, i.e. the width at which every sub-view fits beside the character images at
            // scale 1.000 — see FixedFitWidthPx. It is deliberately WIDER than this window's own
            // authored 1920 px frame, which is why it is not expressed as a clamp on it any more.
            // The HEIGHT still comes from the authored frame (every sub-view is a full-height view and
            // the frame bounds them), clamped for sanity.
            //
            // The frame is still READ, for one thing only: a rect below the sanity floor means the
            // window has not been laid out yet, and capturing anything in that state would pin the
            // window for its whole life (ModBuild 23 found this very target at localScale 0.14 with
            // its rect grown to 2040 px).
            Rect frame = panel.Target.rect;
            if (frame.width < FixedFitMinWidthPx || frame.height < FixedFitMinHeightPx)
                return true; // frame not laid out yet; measured fine, retry on the next check
            fx.Size = new Vector2(
                FixedFitWidthPx,
                Mathf.Clamp(frame.height, FixedFitMinHeightPx, FixedFitMaxHeightPx));
            fx.SizeCaptured = true;
        }

        Rect host = panel.HostRect.rect;

        // SPLIT THE SUBTREE. Everything below this line distinguishes the BASE (the character column
        // and the window furniture around it — what is on screen no matter which tab is open) from
        // the SUB-VIEW GROUP (what the game just opened). ModBuild 198 did not make this distinction
        // and scaled their union; that is why the column shrank to 59.5 % whenever perks opened.
        CollectActiveSubViews(panel, fx);
        MeasureFixedFitParts(panel, root, fx);
        // Part 9c: while a burst is running this pass lands ON the frames the whole sub-view tree
        // is painting, so it is the one place that can count it. Reads what the walk above already
        // produced and writes nothing.
        NoticeBurstCensus(fx, size);

        // ---- 1. THE HOST SIZE, once -----------------------------------------------------------
        bool hostWrong = Mathf.Abs(host.width - fx.Size.x) > FitNoWriteEpsilonPx
                         || Mathf.Abs(host.height - fx.Size.y) > FitNoWriteEpsilonPx;

        // ---- 2. THE COLUMN'S PIN, captured once and only ever re-asserted ----------------------
        Vector2 baseShift = Vector2.zero;
        bool baseWrong = false;
        if (fx.BaseVisible)
        {
            Vector2 baseSize = fx.BaseMax - fx.BaseMin;
            if (!fx.BasePinned && baseSize.x >= 32f && baseSize.y >= 32f)
            {
                // Left edge inside the fixed frame, vertically centred AT THE MOMENT OF THE PIN. The
                // fixed size is the reference and not the live host rect: on the very first fit the
                // host is still the pre-fit 1920 px frame.
                fx.BasePin = new Vector2(-fx.Size.x * 0.5f + FitContentPaddingPx, -baseSize.y * 0.5f);
                fx.BaseSizeAtPin = baseSize;
                fx.BasePinned = true;
            }
            if (fx.BasePinned)
            {
                baseShift = fx.BasePin - fx.BaseMin;
                baseWrong = Mathf.Abs(baseShift.x) > FixedFitShiftEpsilonPx
                            || Mathf.Abs(baseShift.y) > FixedFitShiftEpsilonPx;
            }
        }

        // ---- 2b. THE COLUMN SEAM, captured once, on a pass with the column ALONE ----------------
        EnsureColumnSeam(fx);

        // ---- 3. THE SUB-VIEW GROUP -------------------------------------------------------------
        bool havePlacement = SolveSubViewPlacement(fx, out float wantScale, out Vector2 groupShift);
        bool viewWrong = false;
        if (havePlacement)
        {
            for (int i = 0; i < fx.Views.Count; i++)
            {
                SubViewFit v = fx.Views[i];
                if (!v.Visible)
                    continue;
                if (Mathf.Abs(v.Applied - wantScale) > FixedFitScaleEpsilon
                    || Mathf.Abs(v.WantShift.x - v.AppliedShift.x) > FixedFitShiftEpsilonPx
                    || Mathf.Abs(v.WantShift.y - v.AppliedShift.y) > FixedFitShiftEpsilonPx)
                    viewWrong = true;
                // Somebody else moved what we placed — counted before the settle gate can hide it.
                // The test is exact rather than statistical: this member was written by us, and it is
                // no longer at the pose WE wrote. A tab change cannot produce that (a different tab
                // is a different member, and a member we have never written has nothing to compare).
                if (v.Written
                    && (Mathf.Abs(v.Applied - v.WrittenScale) > FixedFitScaleEpsilon
                        || Mathf.Abs(v.AppliedShift.x - v.WrittenShift.x) > FixedFitShiftEpsilonPx
                        || Mathf.Abs(v.AppliedShift.y - v.WrittenShift.y) > FixedFitShiftEpsilonPx))
                    fx.ReAsserts++;
            }

            // THE CIRCUIT BREAKER, and it is here because of a rule this repo has paid for twice:
            // never win a write war. If something else re-drives these roots every frame (a
            // LayoutGroup on their parent, a show animation that never ends), re-writing our pose
            // ~2.5 times a second forever makes the sub-view flicker between two placements and, on a
            // MultiPass rig, makes the two eyes disagree. Past the threshold we CONCEDE: the sub-view
            // is left exactly where the game puts it (it will spill, which is visible and honest) and
            // the line below says so, so the next round replaces the thing we place instead of
            // guessing that a fight is happening.
            if (fx.ReAsserts >= FixedFitMaxReAsserts)
            {
                if (!fx.ConcededLogged)
                {
                    fx.ConcededLogged = true;
                    VRLog.Warn("WorldUI",
                        $"FIXED FIT CONCEDED '{(panel.HostGo != null ? panel.HostGo.name : "?")}': the " +
                        $"sub-view pose this fit writes has been overwritten by the game {fx.ReAsserts} " +
                        "time(s) while the solution itself did not change, so something else owns " +
                        $"'{fx.ViewName}'s localScale/anchoredPosition. The fit stops placing sub-views " +
                        "rather than fight for them — the character column is UNAFFECTED (it is pinned " +
                        "by the conversion target, which nothing else writes), so the fixed size still " +
                        "holds; the open sub-view will simply be drawn at the size the game gives it " +
                        "and may reach outside the frame. The next round must place a root the game " +
                        "does not drive.");
                }
                viewWrong = false;
            }
        }

        if (!hostWrong && !baseWrong && !viewWrong)
        {
            // SETTLED — and settled here means "nothing was written and nothing was disturbed":
            // no ApplyFitConverging, no ForceRebuildLayoutImmediate, so the ModBuild 196 re-fit loop
            // has nothing to run on.
            fx.PendingChecks = 0;
            fx.PendingScale = wantScale;
            fx.PendingShift = groupShift;
            // Part 9g: "THERE IS NOTHING LEFT TO WRITE FOR THIS OPEN SET" IS ALSO A SEAT, and the
            // seat veil must be released by it or it would hold a sub-view that is ALREADY in the
            // right place until its backstop fired. Three real cases reach here without a write —
            // a sub-view whose wanted seat happens to BE its authored home, a set the fit has
            // already placed and re-measured, and the ModBuild 201 write-war concession (which
            // clears viewWrong on purpose) — and in every one of them the correct answer to "may
            // this be drawn now?" is yes.
            //
            // ModBuild 435: AND THERE IS A FOURTH WAY TO REACH HERE THAT IS NOT A SEAT AT ALL, and
            // it is the whole 2026-09-05 defect. `viewWrong` is also false when `havePlacement` is
            // false, i.e. when SolveSubViewPlacement found ZERO measurable members — which is what
            // an open sub-view looks like for the length of the game's own show fade, because
            // MeasureFixedFitParts drops every graphic below FitMinAlpha and the fit then reports
            // "no sub-view open, nothing to place". Stamping SeatedValid there answers "is it
            // seated?" with "I cannot see it yet", the veil lifts on that answer and never rises
            // again for this set, and the real seat lands ~60 frames later ON SCREEN. That is
            // exactly the shape of [[a-claim-must-not-measure-itself]]: the release condition was
            // being satisfied by the ABSENCE of the thing it waits for. So the stamp now requires
            // either a set that is genuinely empty (nothing to hold) or a measurement that actually
            // had members. `SeatUnseen` counts the refusals and the FIXED FIT line prints it, so
            // "the fit never reached this branch" and "it reached it and declined" cannot look alike.
            int openNow = SubViewOpenSetSignature(panel);
            if (openNow == 0 || havePlacement)
            {
                fx.SeatedSignature = openNow;
                fx.SeatedValid = true;
            }
            else
            {
                fx.SeatUnseen++;
            }
            panel.FitContentPadding = Vector2.Max(Vector2.zero, (fx.Size - size) * 0.5f);
            // Part 9c: nothing left to write, so a settle burst in flight has done its job
            // and stops here rather than spending its remaining checks on a window that has
            // stopped moving. This is the branch that keeps the ordinary tab press at one or
            // two extra checks instead of the cap.
            NoticeFixedFitSettled(panel, fx);
            LogFixedFit(panel, fx, wantScale, "STABLE", string.Empty, throttled: true);
            return true;
        }

        fx.Deviations++;

        // The settle gate — one candidate must repeat before it is written. Exempt on the very first
        // fit of this window's life: that one runs pre-reveal and must land before the window pops
        // in, exactly like the undamped first fit of the growth path. The HOST SIZE stays exempt:
        // it is not derived from any content measurement at all (it is the window's own authored
        // frame, clamped), so it cannot be chasing anything.
        //
        // ModBuild 201: THE COLUMN PIN IS NO LONGER EXEMPT. 199/200 exempted it on the reasoning that
        // it is not derived from a sub-view measurement and therefore cannot chase an animation. The
        // first half is true and the conclusion does not follow: the correction is derived from the
        // BASE union, and the base slides under the window's own show/hide animation — the 200 log
        // has the clean 328x1080 column reading its bottom edge at y = -540, -525, -480 and -405 and
        // the HIT RECT line names 'Party Display UI ' ramping 30 → 150 px past the frame. 47 column
        // re-alignments were written in one session, two of them 135 and 150 px, and that is what the
        // user sees as the window shifting. A ramp never repeats a value; a real dislocation does.
        bool first = !panel.FitMeasuredOnce;
        if ((viewWrong || baseWrong) && !hostWrong && !first)
        {
            bool same = Mathf.Abs(wantScale - fx.PendingScale) <= FixedFitScaleEpsilon
                        && Mathf.Abs(groupShift.x - fx.PendingShift.x) <= FixedFitShiftEpsilonPx
                        && Mathf.Abs(groupShift.y - fx.PendingShift.y) <= FixedFitShiftEpsilonPx
                        && Mathf.Abs(baseShift.x - fx.PendingBaseShift.x) <= FixedFitShiftEpsilonPx
                        && Mathf.Abs(baseShift.y - fx.PendingBaseShift.y) <= FixedFitShiftEpsilonPx;
            fx.PendingChecks = same ? fx.PendingChecks + 1 : 1;
            fx.PendingScale = wantScale;
            fx.PendingShift = groupShift;
            fx.PendingBaseShift = baseShift;
            if (fx.PendingChecks < FixedFitSettleChecks)
            {
                fx.Deferred++;
                // Throttled, so a window that defers FOREVER (a sub-view whose measurement never
                // repeats) still says so instead of going silent — the third state the counters
                // exist to separate.
                LogFixedFit(panel, fx, wantScale, "DEFERRED", string.Empty, throttled: true);
                return true; // measured fine — the candidate has simply not repeated yet
            }
        }
        fx.PendingChecks = 0;

        var wrote = new System.Text.StringBuilder(128);
        if (hostWrong)
        {
            wrote.Append($"host {host.width:F0}x{host.height:F0} → {fx.Size.x:F0}x{fx.Size.y:F0} px");
            panel.HostRect.sizeDelta = fx.Size;
            fx.HostWrites++;
        }
        if (baseWrong)
        {
            // Target-local, inside the host. The host transform is never touched — see the ruling
            // quoted in this method's doc. This moves the sub-views with it, which is why their own
            // placement is deferred to the next check rather than computed against a stale frame.
            panel.Target.anchoredPosition += baseShift / Mathf.Max(HostPerParentUnits(panel, panel.Target), 1e-4f);
            fx.BaseWrites++;
            wrote.Append(wrote.Length > 0 ? "; " : string.Empty)
                 .Append($"the character column re-aligned by {baseShift.x:F0},{baseShift.y:F0} px onto " +
                         $"its pinned corner ({fx.BasePin.x:F0},{fx.BasePin.y:F0})");
        }
        else if (viewWrong && havePlacement)
        {
            int placed = 0;
            for (int i = 0; i < fx.Views.Count; i++)
            {
                SubViewFit v = fx.Views[i];
                if (!v.Visible || v.View == null)
                    continue;
                // ABSOLUTE, never incremental: home + our own offset. A pass that finds the member
                // somewhere else therefore corrects it exactly instead of drifting, and the home we
                // kept is what ReleaseFixedFit puts back.
                v.View.localScale = new Vector3(v.HomeScale.x * wantScale, v.HomeScale.y * wantScale,
                    v.HomeScale.z * wantScale);
                if (v.View is RectTransform vr)
                    vr.anchoredPosition = v.HomeAnchored
                                          + v.WantShift / Mathf.Max(v.HostPerParent, 1e-4f);
                v.Written = true;
                v.WrittenScale = wantScale;
                v.WrittenShift = v.WantShift;
                // ModBuild 201: freeze what we just wrote. Not on the pre-reveal FIRST fit — that one
                // is exempt from the settle gate and may well be looking at a show animation, and a
                // pose frozen out of an animation frame would stay wrong for the window's whole life.
                // The first gated write, ~0.8 s later, is the one that becomes the constant.
                if (!first)
                {
                    v.FrozenWantShift = v.WantShift;
                    v.WantFrozen = true;
                }
                placed++;
            }
            if (placed > 0)
            {
                // Part 9g: THE SEAT FOR THIS OPEN SET NOW EXISTS. Recorded against the CHEAP
                // open-set signature (the one the per-frame veil reads) and not against
                // fx.OpenSignature, because the two are different signatures of different
                // granularity and reading one as the other is [[two-fans-one-name]]. This is the
                // veil's release condition, and it is a fact about the write rather than a timer.
                fx.SeatedSignature = SubViewOpenSetSignature(panel);
                fx.SeatedValid = true;
                if (Mathf.Abs(wantScale - fx.ViewScale) > FixedFitScaleEpsilon)
                    fx.ScaleWrites++;
                else
                    fx.Shifts++;
                wrote.Append(wrote.Length > 0 ? "; " : string.Empty)
                     .Append($"sub-view '{fx.ViewName}' scale {fx.ViewScale:F3} → {wantScale:F3} and " +
                             $"shifted by {groupShift.x:F0},{groupShift.y:F0} px onto the column seam " +
                             $"x={fx.ColumnSeamX:F0} ({placed} root(s) placed; the column was not touched)");
                fx.ViewScale = wantScale;
                fx.ViewShift = groupShift;
                if (!first)
                {
                    fx.FrozenGroupShift = groupShift;
                    fx.SolutionSignature = fx.OpenSignature;
                    fx.SolutionFrozen = true;
                    wrote.Append(". This placement is now FROZEN for as long as this set of sub-views "
                                 + "stays open — no later measurement may re-scale or re-seat a view "
                                 + "that is already on screen");
                }
            }
        }

        // What the surfaces that pin this host by a rect EDGE must subtract — derived, never
        // assumed: with a fixed host the slack is whatever the fixed size leaves around the content.
        panel.FitContentPadding = Vector2.Max(Vector2.zero, (fx.Size - size) * 0.5f);
        panel.FitOneShotApplied = true;
        if (hostWrong)
        {
            // ONLY a host-size change re-derives the window's world scale and re-arms the pose
            // re-place. A sub-view re-scale or re-alignment leaves the host rect exactly as it was,
            // so it must not advance the generation — that is what keeps a tab change from being
            // able to ask for a placement at all.
            panel.FitAppliedGeneration++;
        }
        LogFixedFit(panel, fx, wantScale, "APPLIED", wrote.ToString(), throttled: false);
        // THE MAP ROOM'S CORNER SEATS ARE DEFINED ON WHAT THE WINDOW DRAWS, and this is the moment
        // that changes (ModBuild 412: the character screen narrowed to its column 0.3 s after its
        // corner claim, after the reveal, after the one pre-reveal re-place had been refused). The
        // hook re-measures the drawn rect and re-seats the window's anchored edge onto its corner
        // when — and only when — the anchor moved; see ModalFallback.OnFixedFitApplied.
        ModalFallback.OnFixedFitApplied(panel);
        return true;
    }

    /// <summary>
    /// WHICH SUB-VIEWS THE GAME CURRENTLY HAS OPEN INSIDE THIS WINDOW — asked of the game's own
    /// serialized references, never of the hierarchy and never by name.
    ///
    /// <para><c>NewPartyDisplayUI</c> holds all six of them as <c>[SerializeField]</c>s with public
    /// accessors (<c>AbilityCardsDisplay</c>, <c>EnhancementCardsDisplay</c>, <c>PerkManager</c>,
    /// <c>CharacterSelector</c>, <c>ItemInventoryDisplay</c>, <c>BattleGoalWindow</c>), so "which
    /// object is the sub-view's root" is a fact the prefab already states. <c>ActiveDisplay</c> —
    /// the game's own <c>DisplayType</c> — is deliberately NOT the test: it has no value for the
    /// enhancement-cards view and its <c>LEVELUP</c> value names a window that lives outside this
    /// panel, so it answers a question one notch away from the one that matters. The question that
    /// matters is "is this root being DRAWN inside the window we are fitting", and that is answered
    /// exactly by <c>activeInHierarchy</c> (a <c>UIWindow</c> deactivates its own GameObject once its
    /// alpha tween reaches zero — UIWindow.cs:746) plus a descendant test against the conversion
    /// target.</para>
    ///
    /// <para>A candidate nested inside another candidate is dropped: the group transform must be
    /// applied once per independent root or the inner one would be scaled twice.</para>
    /// </summary>
    private static void CollectActiveSubViews(ConvertedPanel panel, FixedFitState fx)
    {
        for (int i = 0; i < fx.Views.Count; i++)
            fx.Views[i].Visible = false;

        NewPartyDisplayUI? display;
        try
        {
            display = NewPartyDisplayUI.PartyDisplay;
        }
        catch (System.Exception)
        {
            return;
        }
        if (display == null)
            return;

        SubViewCandidates.Clear();
        try
        {
            AddSubViewCandidate(panel, display.AbilityCardsDisplay);
            AddSubViewCandidate(panel, display.EnhancementCardsDisplay);
            AddSubViewCandidate(panel, display.PerkManager);
            AddSubViewCandidate(panel, display.CharacterSelector);
            AddSubViewCandidate(panel, display.ItemInventoryDisplay);
            AddSubViewCandidate(panel, display.BattleGoalWindow);
        }
        catch (System.Exception)
        {
            SubViewCandidates.Clear();
            return;
        }

        // Drop a candidate that lives inside another candidate.
        for (int i = SubViewCandidates.Count - 1; i >= 0; i--)
        {
            for (int j = 0; j < SubViewCandidates.Count; j++)
            {
                if (i == j)
                    continue;
                if (FixedFitLevelsUp(SubViewCandidates[i], SubViewCandidates[j]) > 0)
                {
                    SubViewCandidates.RemoveAt(i);
                    break;
                }
            }
        }

        for (int i = 0; i < SubViewCandidates.Count; i++)
        {
            Transform t = SubViewCandidates[i];
            SubViewFit? entry = null;
            for (int j = 0; j < fx.Views.Count; j++)
            {
                if (ReferenceEquals(fx.Views[j].View, t))
                {
                    entry = fx.Views[j];
                    break;
                }
            }
            if (entry == null)
            {
                entry = new SubViewFit { View = t };
                fx.Views.Add(entry);
            }
            entry.Visible = true;
        }
        SubViewCandidates.Clear();

        // Forget members whose GameObject the game destroyed, so the table cannot grow without bound
        // across a session of opening and closing tabs.
        for (int i = fx.Views.Count - 1; i >= 0; i--)
        {
            if (fx.Views[i].View == null)
                fx.Views.RemoveAt(i);
        }
    }

    private static readonly List<Transform> SubViewCandidates = new(8);

    private static void AddSubViewCandidate(ConvertedPanel panel, Component? c)
    {
        if (c == null || panel.Target == null)
            return;
        Transform t = c.transform;
        if (ReferenceEquals(t, panel.Target) || !c.gameObject.activeInHierarchy)
            return;
        // ModBuild 435: the SHOWN term, and it must be the same one the signature uses or this
        // collector and the veil that waits on it are two populations again (part 9c,
        // SubViewIsShown, carries the whole argument). It removes nothing this fit could measure:
        // a root the game has hidden draws no graphic that passes the visibility verdict below, so
        // MeasureFixedFitParts already dropped it — this only makes "the set is empty" and "the
        // signature is 0" the same statement instead of two that disagree for a second.
        if (!SubViewIsShown(c))
            return;
        if (FixedFitLevelsUp(t, panel.Target) <= 0)
            return; // not inside the window we are fitting
        for (int i = 0; i < SubViewCandidates.Count; i++)
        {
            if (ReferenceEquals(SubViewCandidates[i], t))
                return;
        }
        SubViewCandidates.Add(t);
    }

    /// <summary>
    /// <b>THE GUESTS: GAME OBJECTS THIS MOD HAS MOVED INTO A GAME WINDOW, WHICH THE FIT MUST NOT
    /// MEASURE AS THAT WINDOW'S OWN CONTENT.</b>
    ///
    /// <para><b>WHY A LIST AND NOT A TEST.</b> Everything the mod BUILDS carries the
    /// <c>GloomhavenVR.</c> name prefix (or, since ModBuild 420, a <see cref="ModOwnedContent"/>
    /// marker), and the sweep in <see cref="MeasureFixedFitParts"/> already skips it. Neither answers
    /// for a GAME widget the mod merely re-parented: the confirm button is the game's own
    /// <c>UIReadyToggle</c> and the icon row is the game's own <c>UIReadyTrackerBar</c>. They are not
    /// renamed — deliberately, because renaming a game object is a mutation of somebody else's scene
    /// — so no ownership test that reads the object can see them, and the only honest answer is the
    /// parker's own [[a-typed-collector-cannot-see-a-shape]].</para>
    ///
    /// <para><b>WHAT GOES WRONG WHEN THEY ARE COUNTED, from the 2026-09-05 hardware log.</b> The
    /// COLUMN SEAM is the host-local x every sub-view the game opens is seated on, and it is
    /// <see cref="EnsureColumnSeam"/>'s pin-plus-base-width. On the first life of 'New Party display'
    /// in that log the base reads <c>328x1080 px</c> and the seam is <c>x=-654</c> on all 45 lines:
    /// the ready row was hosted by the quest card, not by this window. On the second life the row is
    /// adopted INTO this window (<c>MAP QUEST READY CARD … card='New Party display'</c>) 64 lines
    /// BEFORE the first fit pass, and every number moves together:</para>
    /// <list type="bullet">
    /// <item>first pass: base <c>1065x1080</c>, seam <c>x=83</c> (provisional), and the battle-goal
    /// picker is seated at <c>rect 83..648</c> — <b>737 px right of the character column</b>, which
    /// is the user's <i>"Abstand Quests … wieder kaputt"</i> and his own attribution of it to the
    /// ready bar, which is correct;</item>
    /// <item>after <c>LOADOUT CONFIRM PARKED</c> the row moves onto the control and the base reads
    /// <c>802x1080</c> = the real 328 px column + the 24 px gap + the 450 px confirm. With no
    /// sub-view open that pass CAPTURES the seam at <c>x=-180</c> — <b>474 px wrong, once, for the
    /// rest of the window's life</b>.</item>
    /// </list>
    ///
    /// <para><b>THE LOOP IS THE POINT, NOT THE PIXELS.</b> Both guests are placed one gap right of
    /// what this window paints. Measuring them as what this window paints makes the seam a function
    /// of its own consequences [[a-claim-must-not-measure-itself]], and because the seam is captured
    /// ONCE the error cannot heal. <c>LoadoutConfirmPark</c> reached the same conclusion for its own
    /// solve and excluded the row from it; this is the other half — the fit had no such exclusion at
    /// all, and the recon that sent this round looked at the seat solve rather than at the seam.</para>
    ///
    /// <para><b>NOTHING ELSE CHANGES.</b> The guests are still DRAWN, still interactive, and still
    /// measured by everything whose job is to cover what is drawn: the hit rect, the grab bar, the
    /// close-X seat and the supersample capture frame all union them exactly as before. The refusal
    /// is scoped to the one measurement that decides where the GAME's content is placed.</para>
    ///
    /// <para><b>A PARKER ADDED LATER MUST BE ADDED HERE.</b> That is a convention, and this file's
    /// own history says conventions fail silently — so the failure is made loud instead: every fit
    /// line prints how many guest graphics were refused this pass and over the window's life, and
    /// names the one that reached furthest right. A window whose seam is wrong with a refusal count
    /// of 0 has a third parker nobody has listed, and the line says so on the spot.</para>
    /// </summary>
    private static Transform? ParkedGuestControl() => LoadoutConfirmPark.HeldControl;

    /// <summary>The adopted ready-icon row. <see cref="ParkedGuestControl"/>'s twin; the doc there
    /// carries the argument for both.</summary>
    private static Transform? ParkedGuestReadyRow() => MapRoom.MapQuestReadyRoster.HeldRow;

    /// <summary>Is <paramref name="t"/> inside either parked guest subtree? Two reference compares
    /// and, at most, two parent walks per graphic, and only on the fit's own cadence.</summary>
    private static bool IsParkedGuest(Transform t, Transform? guestA, Transform? guestB) =>
        (guestA != null && (ReferenceEquals(t, guestA) || t.IsChildOf(guestA)))
        || (guestB != null && (ReferenceEquals(t, guestB) || t.IsChildOf(guestB)));

    /// <summary>
    /// THE SPLIT MEASURE — one walk over the window's visible graphics that fills THREE unions
    /// instead of one: the BASE (nothing that belongs to an open sub-view), each open SUB-VIEW, and
    /// each sub-view's content WITHOUT its full-frame backdrop plates.
    ///
    /// <para>It deliberately does not go through <see cref="TryMeasureContent"/>: that method fills a
    /// dozen <c>s_last*</c> statics the fit log reads, and calling it three more times per pass would
    /// leave the diagnostics describing whichever sub-call ran last. It shares the per-graphic
    /// visibility verdict (<see cref="TryGetVisibleHostRect"/>) and the mod-owned-cue-art skip, so
    /// what it counts as visible is byte-for-byte what the main measure counts.</para>
    ///
    /// <para>THE PLATE TEST IS A DIAGNOSTIC AND NOTHING ELSE — no write anywhere in this file reads
    /// <c>ContentNeed</c>. See <see cref="FixedFitPlateWidthFraction"/> for the argument.</para>
    /// </summary>
    private static void MeasureFixedFitParts(ConvertedPanel panel, RectTransform root, FixedFitState fx)
    {
        fx.BaseVisible = false;
        fx.BaseGraphics = 0;
        fx.BaseRawGraphics = 0;
        fx.BaseUsedRawUnion = false;
        fx.ViewPlates = 0;
        fx.PlateName = string.Empty;
        fx.PlateSize = Vector2.zero;
        fx.ViewContentNeed = Vector2.zero;
        fx.TransientThisPass = 0;
        fx.TransientWidest = string.Empty;
        fx.TransientWidestSize = Vector2.zero;
        fx.ContentLeftName = string.Empty;
        fx.ContentRightName = string.Empty;
        fx.ParkedGuestsThisPass = 0;
        fx.ParkedGuestRightX = 0f;
        fx.ParkedGuestWidest = string.Empty;
        Vector2 baseMin = new(float.MaxValue, float.MaxValue);
        Vector2 baseMax = new(float.MinValue, float.MinValue);
        Vector2 baseRawMin = new(float.MaxValue, float.MaxValue);
        Vector2 baseRawMax = new(float.MinValue, float.MinValue);
        Vector2 contentMin = new(float.MaxValue, float.MaxValue);
        Vector2 contentMax = new(float.MinValue, float.MinValue);
        int contentGraphics = 0;
        float plateArea = 0f;
        float widestTransient = 0f;

        for (int i = 0; i < fx.Views.Count; i++)
        {
            SubViewFit v = fx.Views[i];
            v.Graphics = 0;
            v.RawGraphics = 0;
            v.UsedRawUnion = false;
            v.Min = new Vector2(float.MaxValue, float.MaxValue);
            v.Max = new Vector2(float.MinValue, float.MinValue);
            v.RawMin = new Vector2(float.MaxValue, float.MaxValue);
            v.RawMax = new Vector2(float.MinValue, float.MinValue);
            if (!v.Visible || v.View == null)
                continue;
            // The home is RE-READ until the first write: a show animation can still be driving this
            // root's scale/position when we first see it, and the settle gate holds our first write
            // off for ~0.8 s — long enough for a LeanTween show to have landed.
            if (!v.Written)
            {
                v.HomeScale = v.View.localScale;
                if (v.View is RectTransform hr)
                    v.HomeAnchored = hr.anchoredPosition;
            }
            v.HostPerParent = HostPerParentUnits(panel, v.View);
            float home = Mathf.Abs(v.HomeScale.x) > 1e-4f ? v.HomeScale.x : 1f;
            v.Applied = v.View.localScale.x / home;
            v.AppliedShift = v.View is RectTransform ar
                ? (ar.anchoredPosition - v.HomeAnchored) * v.HostPerParent
                : Vector2.zero;
            v.Pivot = panel.HostRect.InverseTransformPoint(v.View.position);
        }

        // A full-frame plate is judged against the window's OWN authored frame, which is the same
        // rect the growth path clamps its union into.
        Vector2 frame = panel.Target.rect.size;

        ClipperMemo.Clear();
        AuthoredOffsetMemo.Clear();
        TransientMemo.Clear();
        FixedFitGraphics.Clear();
        root.GetComponentsInChildren(includeInactive: false, FixedFitGraphics);
        // The two subtrees this mod has moved INTO the game's window on this tick, read once per
        // pass rather than once per graphic. See ParkedGuestControl for the whole argument.
        Transform? guestA = ParkedGuestControl();
        Transform? guestB = ParkedGuestReadyRow();
        for (int i = 0; i < FixedFitGraphics.Count; i++)
        {
            Graphic g = FixedFitGraphics[i];
            if (g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                continue;
            if (!TryGetVisibleHostRect(panel, g, out Vector2 gMin, out Vector2 gMax))
                continue;

            // THE MOD'S OWN GUESTS ARE NOT THE GAME'S COLUMN. Both of them are GAME objects that
            // this mod re-parented into this window, so the name test above is blind to them, and
            // both are SEATED AGAINST what this measurement produces. Refused from every union this
            // method builds — clean, raw and sub-view — because the raw union is the fallback and a
            // fallback that re-admits the contamination is not one. See ParkedGuestControl.
            if (IsParkedGuest(g.transform, guestA, guestB))
            {
                fx.ParkedGuestsThisPass++;
                if (gMax.x > fx.ParkedGuestRightX || fx.ParkedGuestWidest.Length == 0)
                {
                    fx.ParkedGuestRightX = gMax.x;
                    fx.ParkedGuestWidest = g.gameObject.name;
                }
                continue;
            }

            // ModBuild 201, report (5): a hover preview, an item hint or a modifier flyout is DRAWN
            // and is deliberately allowed to reach outside the frame — the hit rect and the capture
            // frame both follow it — but it may not be MEASURED, because every quantity this fit
            // writes is derived from a measurement and a measurement that a mouseover can change is
            // a mouseover that can move the window.
            int transient = TransientFamilyOf(g.transform, root);
            if (transient != 0)
            {
                fx.TransientThisPass++;
                fx.TransientFamilyMask |= 1 << transient;
                float tw = gMax.x - gMin.x;
                if (tw > widestTransient)
                {
                    widestTransient = tw;
                    fx.TransientWidest = TransientFamilyNames[transient];
                    fx.TransientWidestSize = gMax - gMin;
                }
            }

            SubViewFit? owner = OwningSubView(fx, g.transform, root);
            if (owner == null)
            {
                baseRawMin = Vector2.Min(baseRawMin, gMin);
                baseRawMax = Vector2.Max(baseRawMax, gMax);
                fx.BaseRawGraphics++;
                if (transient != 0)
                    continue;
                // ModBuild 396: remember WHICH graphic sits at each extreme of the column union.
                // Three compares and, at most, three reference writes per accepted graphic; the
                // strings are built only on a pass that actually overspilled. See the report.
                if (fx.BaseGraphics == 0 || gMin.x < fx.BaseEdgeLeftX)
                {
                    fx.BaseEdgeLeftX = gMin.x;
                    fx.BaseEdgeLeft = g.transform;
                }
                if (fx.BaseGraphics == 0 || gMax.x > fx.BaseEdgeRightX)
                {
                    fx.BaseEdgeRightX = gMax.x;
                    fx.BaseEdgeRight = g.transform;
                }
                if (fx.BaseGraphics == 0 || gMin.y < fx.BaseEdgeBottomY)
                {
                    fx.BaseEdgeBottomY = gMin.y;
                    fx.BaseEdgeBottom = g.transform;
                }
                baseMin = Vector2.Min(baseMin, gMin);
                baseMax = Vector2.Max(baseMax, gMax);
                fx.BaseGraphics++;
                continue;
            }
            owner.RawMin = Vector2.Min(owner.RawMin, gMin);
            owner.RawMax = Vector2.Max(owner.RawMax, gMax);
            owner.RawGraphics++;
            if (transient != 0)
            {
                owner.TransientDropped++;
                continue;
            }
            owner.Min = Vector2.Min(owner.Min, gMin);
            owner.Max = Vector2.Max(owner.Max, gMax);
            owner.Graphics++;

            float w = gMax.x - gMin.x, h = gMax.y - gMin.y;
            bool plate = frame.x > 1f && frame.y > 1f
                         && w >= frame.x * FixedFitPlateWidthFraction
                         && h >= frame.y * FixedFitPlateHeightFraction;
            if (plate)
            {
                fx.ViewPlates++;
                if (w * h > plateArea)
                {
                    plateArea = w * h;
                    fx.PlateSize = new Vector2(w, h);
                    fx.PlateName = g.gameObject.name;
                }
                continue;
            }
            // In the sub-view's NATURAL space (our own scale and offset undone), so the census is
            // comparable with the needs quoted in the region note whatever we have written.
            float a = Mathf.Max(owner.Applied, 0.01f);
            Vector2 p0 = owner.Pivot - owner.AppliedShift;
            Vector2 cMin = p0 + (gMin - owner.Pivot) / a;
            Vector2 cMax = p0 + (gMax - owner.Pivot) / a;
            // ModBuild 201: NAME the two graphics that define the content union in x. "1613 px wide
            // with the backdrop removed" is a number nobody can act on; "this graphic is its left
            // edge and that one its right" is what decides whether the wide views can ever reach
            // scale 1.000, and no round has ever had it.
            if (contentGraphics == 0 || cMin.x < contentMin.x)
            {
                fx.ContentLeftName = g.gameObject.name;
                fx.ContentLeftSize = new Vector2(w, h) / a;
                fx.ContentLeftX = cMin.x;
            }
            if (contentGraphics == 0 || cMax.x > contentMax.x)
            {
                fx.ContentRightName = g.gameObject.name;
                fx.ContentRightSize = new Vector2(w, h) / a;
                fx.ContentRightX = cMax.x;
            }
            contentMin = Vector2.Min(contentMin, cMin);
            contentMax = Vector2.Max(contentMax, cMax);
            contentGraphics++;
        }
        FixedFitGraphics.Clear();
        fx.TransientIgnored += fx.TransientThisPass;
        fx.ParkedGuestsRefused += fx.ParkedGuestsThisPass;

        // THE SAFETY NET. An exclusion that empties a bucket is worse than the contamination it
        // removes, so both readings are kept and the raw one is used — loudly — when the clean one
        // measured nothing at all. See the ModBuild 201 block: the only family that could plausibly
        // do this is FullAbilityCard, and the character screen never shows a full card except on
        // hover, so this is expected to stay at zero forever.
        if (fx.BaseGraphics > 0 && baseMax.x > baseMin.x && baseMax.y > baseMin.y)
        {
            fx.BaseVisible = true;
            fx.BaseMin = baseMin;
            fx.BaseMax = baseMax;
        }
        else if (fx.BaseRawGraphics > 0 && baseRawMax.x > baseRawMin.x && baseRawMax.y > baseRawMin.y)
        {
            fx.BaseVisible = true;
            fx.BaseUsedRawUnion = true;
            fx.BaseMin = baseRawMin;
            fx.BaseMax = baseRawMax;
            fx.BaseGraphics = fx.BaseRawGraphics;
        }
        fx.BaseRawMin = baseRawMin;
        fx.BaseRawMax = baseRawMax;
        for (int i = 0; i < fx.Views.Count; i++)
        {
            SubViewFit v = fx.Views[i];
            if (!v.Visible || v.Graphics > 0 || v.RawGraphics == 0 || v.RawMax.x <= v.RawMin.x)
                continue;
            v.UsedRawUnion = true;
            v.Min = v.RawMin;
            v.Max = v.RawMax;
            v.Graphics = v.RawGraphics;
        }
        if (contentGraphics > 0 && contentMax.x > contentMin.x)
            fx.ViewContentNeed = contentMax - contentMin;

        // Name the widest open sub-view, for the log. Every seating figure is cleared here too, so a
        // pass on which the placement does not solve prints zeroes rather than the last pass's
        // numbers — a stale "gap 0 px" would be exactly the "no gap" / "never checked" confusion the
        // line exists to prevent.
        fx.ViewName = "none";
        fx.ViewNeed = Vector2.zero;
        fx.ViewGapPx = 0f;
        fx.ViewOverlapPx = 0f;
        fx.ViewSpillPx = 0f;
        fx.ViewSpillYPx = 0f;
        fx.ViewSlotPx = 0f;
        float widest = 0f;
        for (int i = 0; i < fx.Views.Count; i++)
        {
            SubViewFit v = fx.Views[i];
            if (!v.Visible || v.Graphics == 0 || v.View == null || v.Max.x <= v.Min.x)
            {
                v.Visible = false;
                continue;
            }
            float w = v.Max.x - v.Min.x;
            if (w <= widest)
                continue;
            widest = w;
            fx.ViewName = v.View.name;
        }
    }

    /// <summary>Which open sub-view a graphic belongs to, or null for the base. Walks up to the fit
    /// root; ≤ 6 candidates and a shallow uGUI tree, ~2.5 passes per second.</summary>
    private static SubViewFit? OwningSubView(FixedFitState fx, Transform node, Transform root)
    {
        int guard = 0;
        for (Transform? t = node; t != null && guard < 64; t = t.parent, guard++)
        {
            for (int i = 0; i < fx.Views.Count; i++)
            {
                SubViewFit v = fx.Views[i];
                if (v.Visible && ReferenceEquals(v.View, t))
                    return v;
            }
            if (ReferenceEquals(t, root))
                return null;
        }
        return null;
    }

    /// <summary>Scratch for the split measure. Separate from <see cref="GraphicScratch"/> because
    /// <see cref="TryMeasureContent"/> owns that one and both run inside the same fit pass.</summary>
    private static readonly List<Graphic> FixedFitGraphics = new(128);

    // =============================================================================================
    // ModBuild 201 — TRANSIENT CONTENT, IDENTIFIED BY COMPONENT TYPE
    // =============================================================================================

    /// <summary>Human name per family index of <see cref="TransientFamilyOf"/>; index 0 is
    /// "not transient" and is never printed.
    ///
    /// <para>ModBuild 241: the table itself now lives in <see cref="TransientFamilies"/> and this is
    /// the SAME array object, not a copy. <see cref="PanelInkBounds"/> needs the identical answer for
    /// the grab bar, and a second hand-maintained copy of a six-entry type list is the drift this
    /// file's own borrowings are already flagged for. Every call site below is unchanged.</para></summary>
    private static readonly string[] TransientFamilyNames = TransientFamilies.Names;

    /// <summary>
    /// WHICH TRANSIENT FAMILY THIS GRAPHIC BELONGS TO, or 0 for real window content — the whole of
    /// report (5), "nothing may shift because of mouseovers".
    ///
    /// <para><b>IT IS AN IDENTITY, NOT A RESIDUE.</b> ModBuild 200 could only say "these ~35 graphics
    /// belong to none of the six serialized sub-view roots", which is a statement about what they are
    /// NOT and would silently swallow any future window furniture that happens to sit outside a
    /// sub-view. This asks the game's own component types instead, exactly the table
    /// <c>TooltipOnWindow</c> already argues for at its family-table comment — with
    /// <c>UILocalTooltip</c> listed as the BASE so its three subclasses are one entry, and with
    /// <c>UIItemModifiersTooltip</c> added, which is the one family in the game that table is
    /// missing.</para>
    ///
    /// <para>THE TEST IS CONTAINMENT AND THAT IS CORRECT HERE, which is worth stating because this
    /// repo has twice shipped the opposite mistake. "Containment is not identity" is a rule about
    /// DECIDING WHAT AN OBJECT IS: <c>GetComponentInParent&lt;UIWindow&gt;</c> must never be used to
    /// answer "is this a window root". Here the question is genuinely about the ancestor — the marker
    /// component sits on the tooltip's own root and the graphic being judged is a descendant of it,
    /// so "does this graphic belong to a tooltip subtree" IS an ancestor question. The walk stops at
    /// the fit root, so nothing outside the window can ever answer it.</para>
    ///
    /// <para>WHY NOT THE NESTED CANVAS. <c>AbilityCardUI.ToggleFullCardPreview</c> adds a
    /// <c>Canvas</c> with <c>overrideSorting</c> and <c>sortingOrder 10</c> on show and destroys it on
    /// hide, which looks like a perfect state flag. It is not: ModBuild 194 established that the
    /// mod's own <c>GraphicRaycaster</c> adoption keeps that <c>Canvas</c> alive past the hide, so it
    /// would report "hovering" forever. The component that MEANS "this is a full card" never lies.</para>
    ///
    /// <para>Walked once per transform per pass through <see cref="TransientMemo"/> — siblings share
    /// their whole ancestor chain, exactly like <see cref="AuthoredOffset"/>, so a ~250-transform
    /// window costs one six-way <c>TryGetComponent</c> probe per transform at the ~2.5 Hz fit
    /// cadence and nothing at all on the 99 % of frames that are not fit passes.</para>
    ///
    /// <para>ModBuild 241: the six probes and the table moved to <see cref="TransientFamilies"/> so
    /// <see cref="PanelInkBounds"/> could ask the SAME question about the grab bar without a second
    /// copy of the list. Only the memo stayed here, because its lifetime is this file's (it is cleared
    /// with the clipper and authored-offset memos at the top of every split measure) and the ink walk
    /// has a different cadence entirely. Everything above still describes the answer exactly.</para>
    ///
    /// <para>ModBuild 449: <c>OfGraphic</c> rather than <c>Of</c> — the same memoised ancestor
    /// identity first, and then the game's own per-quad declaration for the authoring where the UIFX
    /// controller sits on the widget and 448's subtree guard therefore refuses. Strictly additive:
    /// it is asked only where <c>Of</c> already answered 0. See
    /// <see cref="TransientFamilies.IsDeclaredEffectQuad"/>.</para>
    /// </summary>
    private static int TransientFamilyOf(Transform? node, Transform root) =>
        TransientFamilies.OfGraphic(node, root, TransientMemo);

    /// <summary>Per-pass memo of <see cref="TransientFamilyOf"/>, cleared with the clipper and
    /// authored-offset memos at the start of every split measure (subtrees are re-parented between
    /// passes — TooltipOnWindow raises a hover preview out of its own view and back).</summary>
    private static readonly Dictionary<Transform, int> TransientMemo = new(128);

    /// <summary>The families seen so far, spelled out for the log — a bitmask in a hardware log is a
    /// number somebody has to decode against a source file that may have moved on by then.</summary>
    private static string DescribeTransientFamilies(int mask) => TransientFamilies.Describe(mask);

    /// <summary>
    /// SOLVE THE GROUP TRANSFORM for whatever sub-views are open: one uniform scale and one
    /// translation, applied to every member so their arrangement RELATIVE TO EACH OTHER is preserved
    /// exactly (member <c>i</c> is scaled about its own pivot and its pivot is then moved as if the
    /// whole group had scaled about the group centre — the composition of the two IS a group scale).
    /// False when nothing is open, which is the window's most common state and needs no writes.
    ///
    /// <para>THE SEAT IS THE COLUMN'S RIGHT EDGE (ModBuild 200), i.e. <see cref="FixedFitState.ColumnSeamX"/>,
    /// a quantity captured ONCE from the column alone and never re-derived. Every open sub-view's
    /// LEFT edge lands exactly on it, so the empty gap and the covered-column overlap are both 0 px
    /// for all six views at every scale, and the seat does not move between tabs — which is what
    /// ModBuild 199's frame-right-edge seat could not do, because it made the distance from the
    /// column a function of the sub-view's own width (248 px of empty frame for the 543 px equipment
    /// view, 340 px of column covered by the 1648 px selector; both photographed).</para>
    ///
    /// <para><b>THE SCALE IS NOT SOLVED AT ALL FROM ModBuild 202 — IT IS THE CONSTANT 1.000.</b>
    /// Through 201 it was <c>min(1, slot/need, frameHeight/need)</c> against the slot the seam
    /// leaves (803 px of a 1143 px frame), which drew the two full-screen sub-views at 0.487/0.494
    /// and produced both the "shrunken foreign panel" report and the flicker on exactly those two
    /// views. The host is sized to hold the widest sub-view beside the column instead
    /// (<see cref="FixedFitWidthPx"/>), the slot is still measured, and a view that does not fit it
    /// SPILLS past the transparent frame at full size and says so in the log. See the ModBuild 202
    /// block in the region note for what the wider host costs and for the one line of it that is not
    /// in this file.</para>
    ///
    /// <para>WHAT IT IS SOLVED AGAINST: the sub-view's NATURAL geometry, reconstructed by undoing our
    /// own scale and offset — <c>p0 = P0 + (p - P) / applied</c>. The measure reads world corners, so
    /// everything it reports already carries whatever we last wrote; solving against the raw reading
    /// would compound our own scale every pass.</para>
    /// </summary>
    private static bool SolveSubViewPlacement(FixedFitState fx,
        out float wantScale, out Vector2 groupShift)
    {
        wantScale = 1f;
        groupShift = Vector2.zero;

        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);
        int members = 0;
        int signature = 0;
        for (int i = 0; i < fx.Views.Count; i++)
        {
            SubViewFit v = fx.Views[i];
            if (!v.Visible)
                continue;
            float applied = Mathf.Max(v.Applied, 0.01f);
            Vector2 p0 = v.Pivot - v.AppliedShift;
            v.NaturalMin = p0 + (v.Min - v.Pivot) / applied;
            v.NaturalMax = p0 + (v.Max - v.Pivot) / applied;
            v.NaturalPivot = p0;
            min = Vector2.Min(min, v.NaturalMin);
            max = Vector2.Max(max, v.NaturalMax);
            members++;
            // Order-independent (the collector's order follows the display's serialized field order,
            // which is stable, but nothing here should depend on that) and cheap: XOR of the members'
            // instance IDs plus the count, which distinguishes "equipment alone" from "equipment plus
            // the inventory column" — the one open-set change this window actually makes.
            signature ^= v.View != null ? v.View.GetInstanceID() : 0;
        }
        if (members == 0)
        {
            // Every tab closed. The freeze is released HERE rather than only on a signature change,
            // so the grain is "once per OPEN" and not "once per session": closing perks and opening
            // it again re-solves it (its content may genuinely differ — a different character), while
            // hovering inside a view that stays open never can. Without this the signature of a
            // re-opened view would match the one it had before and the stale solution would stand.
            fx.OpenSignature = 0;
            if (fx.SolutionFrozen)
                fx.ReleaseSolution();
            return false;
        }
        signature = signature * 31 + members;
        fx.OpenSignature = signature;

        // ModBuild 201: THE ONLY EVENT THAT MAY RE-OPEN A SOLVED PLACEMENT is the set of open
        // sub-views changing. Anything else — a hover, a show animation, a list that grew a row, a
        // re-measure that landed one pixel differently — finds the scale and the seat already
        // decided and leaves them alone. See the (5) block in the region note for what used to move.
        if (fx.SolutionFrozen && fx.SolutionSignature != signature)
            fx.ReleaseSolution();

        Vector2 natural = max - min;
        fx.ViewNeed = natural;
        if (natural.x < 1f || natural.y < 1f)
            return false;

        // THE SLOT: from the column seam to the frame's right edge, not the whole frame — the width a
        // sub-view has beside the character images. From ModBuild 202 it no longer decides a scale;
        // it is the yardstick the SPILL below is measured against, and the fixed width is chosen so
        // that with the measured 328 px column it comes to exactly the widest measured sub-view
        // (1988 - 12 - 328 = 1648 px).
        float frameRight = fx.Size.x * 0.5f;
        float seam = fx.ColumnSeamX;
        float slot = Mathf.Max(frameRight - seam, fx.Size.x * FixedFitMinSlotFraction);
        fx.ViewSlotPx = slot;

        Vector2 c = (min + max) * 0.5f;

        // ==========================================================================================
        // THE SCALE IS 1.000 AND IT IS NOT SOLVED (ModBuild 202 ruling: "I want the sub-menu to be
        // exactly the size of the whole window and to match the size of the character images on the
        // left, so that it is perceived as ONE window. Guarantee that.").
        //
        // This assignment IS the guarantee. There is no expression left in this file that can draw a
        // sub-view at a scale the character column is not drawn at, so "all six at 1.000" cannot
        // depend on a measurement landing well. What used to stand here was
        // min(1, slot/need, frameHeight/need) — correct arithmetic that produced 0.494 for perks and
        // 0.487 for the selector, i.e. the shrunken foreign panel he reported twice, and the scaled
        // subtree whose capture falls below the sampling band limit (the flicker he reported on those
        // same two views and no others).
        //
        // The slot and the frame height are still MEASURED and still printed — as SPILL. A view too
        // wide or too tall for the frame now hangs past its (transparent) edge at full size instead
        // of being shrunk, which is visible, honest and something the next log names in pixels. It
        // can never reach back over the column, because the seat below is the column's seam.
        // ==========================================================================================
        wantScale = 1f;

        // THE SEAT: the group's LEFT edge lands exactly on the seam, and the group is centred on the
        // frame's centre line vertically (host pivot is centred, so that line is y = 0). No margin is
        // charged on either side — a margin here is the gap he asked to be rid of, and on the right it
        // would only push a full-bleed view for a stripe of frame nobody can see. FROZEN once written:
        // the seat is a constant for as long as this set of sub-views stays open, and the report below
        // is still computed against the LIVE geometry on purpose, so a view that changed under a
        // frozen seat shows up as a non-zero gap in the log instead of being silently corrected.
        groupShift = fx.SolutionFrozen
            ? fx.FrozenGroupShift
            : new Vector2(seam - min.x, -(min.y + max.y) * 0.5f);

        float leftEdge = float.MaxValue, rightEdge = float.MinValue;
        float topEdge = float.MinValue, bottomEdge = float.MaxValue;
        for (int i = 0; i < fx.Views.Count; i++)
        {
            SubViewFit v = fx.Views[i];
            if (!v.Visible)
                continue;
            // Expressed about the member's OWN pivot, which is what the write actually does (scale
            // about the pivot, then move the pivot). Algebraically identical to the group form
            // c + (NaturalMin - c)·s + groupShift when WantShift is the solved one, and still exact
            // when WantShift is a frozen constant — which the group form would not be.
            v.WantShift = fx.SolutionFrozen && v.WantFrozen
                ? v.FrozenWantShift
                : c + (v.NaturalPivot - c) * wantScale + groupShift - v.NaturalPivot;
            Vector2 origin = v.NaturalPivot + v.WantShift;
            v.SeatedMin = origin + (v.NaturalMin - v.NaturalPivot) * wantScale;
            v.SeatedMax = origin + (v.NaturalMax - v.NaturalPivot) * wantScale;
            v.GapPx = Mathf.Max(0f, v.SeatedMin.x - seam);
            v.OverlapPx = Mathf.Max(0f, seam - v.SeatedMin.x);
            leftEdge = Mathf.Min(leftEdge, v.SeatedMin.x);
            rightEdge = Mathf.Max(rightEdge, v.SeatedMax.x);
            bottomEdge = Mathf.Min(bottomEdge, v.SeatedMin.y);
            topEdge = Mathf.Max(topEdge, v.SeatedMax.y);
        }

        // THE GROUP'S REPORT, worst case over the open members. Both of the first two are 0 by
        // construction and are printed anyway: "0 px" and "never measured" must not look alike, and
        // the whole of reports (a) and (c) is these two numbers.
        fx.ViewGapPx = Mathf.Max(0f, leftEdge - seam);
        fx.ViewOverlapPx = Mathf.Max(0f, seam - leftEdge);
        // SPILL — what the scale-to-fit solve used to absorb (ModBuild 202). The host is sized so
        // that both read 0 for every sub-view ever measured; anything else is a view we have never
        // seen, and the log names its width so the next round can raise one constant instead of
        // re-introducing a scale.
        fx.ViewSpillPx = Mathf.Max(0f, rightEdge - frameRight);
        fx.ViewSpillYPx = Mathf.Max(0f,
            Mathf.Max(topEdge - fx.Size.y * 0.5f, -fx.Size.y * 0.5f - bottomEdge));
        return true;
    }

    /// <summary>
    /// Fraction of the fixed frame the sub-view slot may never fall below. A pure safety clamp on a
    /// seam that was taken from a base union somebody else contaminated (see
    /// <see cref="EnsureColumnSeam"/>): with the real 328 px column the slot is 1648 of 1988 px =
    /// 0.83, so this never fires in the measured case, and if it ever does the log says so.
    /// </summary>
    private const float FixedFitMinSlotFraction = 0.40f;

    /// <summary>
    /// CAPTURE THE COLUMN SEAM — the host-local x every open sub-view's left edge is seated on.
    ///
    /// <para>Taken as THE COLUMN'S PIN PLUS THE BASE UNION'S WIDTH — not the base union's live right
    /// edge, which is not the same thing on the pass that captures it (see the comment on the
    /// arithmetic below) — and only on a pass where NO sub-view is open,
    /// because that is the only pass on which the base union IS the column: the ModBuild 199
    /// hardware log has it reading 328x1080 px with 139 graphics while nothing is open and
    /// 886x1095 / 939x1095 px with 174 / 165 graphics moments later with the ability-card view up,
    /// alternating back and forth. Something transient is drawn under the conversion target that
    /// none of the six serialized sub-view roots owns, so it is attributed to the base. A seam taken
    /// from that reading would sit ~600 px too far right and would move again on the next pass,
    /// which is the jump this round exists to remove.</para>
    ///
    /// <para>Until a clean pass arrives the seam is PROVISIONAL: it tracks the live reading (clamped,
    /// so it can never eat the slot) and is not marked captured, so the first clean pass replaces it
    /// and no pass after that ever changes it. In practice the window opens on the column with no tab
    /// selected, so the first pass of its life is the clean one — the 199 log's first FIXED FIT line
    /// is exactly that.</para>
    /// </summary>
    private static void EnsureColumnSeam(FixedFitState fx)
    {
        if (fx.SeamCaptured)
        {
            MaybeReDeriveSeamOnNarrowing(fx);
            return;
        }

        float frameRight = fx.Size.x * 0.5f;
        float limit = frameRight - fx.Size.x * FixedFitMinSlotFraction;

        if (!fx.BaseVisible || !fx.BasePinned)
        {
            // Nothing of the base measured (or pinned) yet — seat on the frame's own left edge, so a
            // sub-view that somehow arrives before the column is still placed deterministically.
            fx.ColumnSeamX = -frameRight + FitContentPaddingPx;
            return;
        }

        // PIN + WIDTH, never the live right edge. On the very first fit the base has been PINNED but
        // its shift has not been written yet, so fx.BaseMax.x is still wherever the game left the
        // column — the 199 log's own first line reads "renders 328x1080 px from (-984,-540) … MOVED
        // by -425,0 px". Taking the right edge there would seat every sub-view 425 px too far left,
        // for the whole life of the window, and it would be captured once so nothing would ever
        // correct it. The left edge is the pin by definition; only the width is read.
        float width = fx.BaseMax.x - fx.BaseMin.x;
        float raw = fx.BasePin.x + width;
        fx.SeamClamped = raw > limit;
        fx.ColumnSeamX = Mathf.Min(raw, limit);
        fx.SeamBaseWidth = width;

        bool anyViewOpen = false;
        for (int i = 0; i < fx.Views.Count; i++)
        {
            if (fx.Views[i].Visible)
            {
                anyViewOpen = true;
                break;
            }
        }
        if (!anyViewOpen)
            fx.SeamCaptured = true; // the column alone was measured: final, never re-derived
    }

    /// <summary>How much narrower the column has to measure than the width the seam was taken from
    /// before the capture is treated as PROVEN CONTAMINATED. Well above the 1 px the shift epsilons
    /// work in and far below the 474 px the 2026-09-05 log shows, so it can neither chase noise nor
    /// miss the failure it exists for.</summary>
    private const float FixedFitSeamShrinkPx = 32f;

    /// <summary>
    /// <b>A SEAM CAPTURED FROM A CONTAMINATED COLUMN IS RECOVERABLE, EXACTLY ONCE.</b>
    ///
    /// <para>"Captured once and never re-derived" is the standing ruling and it is kept: a seam that
    /// tracks the live union is the jump ModBuild 200 removed, and a sub-view seated on a moving seam
    /// is the "runter ploppen" ModBuild 434/435 spent two rounds on. This is not that. It fires on
    /// ONE piece of positive evidence — the column later measuring MATERIALLY NARROWER than the width
    /// the seam was taken from — which is a statement no correct capture can make, because the base
    /// is pinned and never rescaled and the fit's own line asserts that on every pass. If the column
    /// is 328 px now and the seam was taken from 802 px, the 802 was not the column.</para>
    ///
    /// <para><b>THE GUARDS ARE WHAT MAKE IT SAFE.</b> Once per window life (so it can never
    /// oscillate); only while NO sub-view is open (so nothing is seated on the seam at the instant it
    /// moves, and the next open re-seats from the corrected value rather than jumping under the
    /// player's hand); and only ever NARROWER, through the same clamp the capture uses.</para>
    ///
    /// <para><b>IT IS ALSO THE FALSIFIER FOR THE ROUND ABOVE.</b> With <see cref="IsParkedGuest"/> in
    /// place the two known contaminants can no longer reach the base, so this is expected to fire
    /// ZERO times — and every fit line prints that it has not. A hardware log in which it DOES fire
    /// names a third contaminant nobody has listed, and repairs the layout in the same breath instead
    /// of leaving the window wrong until someone reads the log [[gated-remedy-never-ran]].</para>
    /// </summary>
    private static void MaybeReDeriveSeamOnNarrowing(FixedFitState fx)
    {
        if (fx.SeamReDerived || !fx.BaseVisible || !fx.BasePinned)
            return;
        for (int i = 0; i < fx.Views.Count; i++)
        {
            if (fx.Views[i].Visible)
                return; // something is seated on the seam right now — never move it under a hand
        }

        float width = fx.BaseMax.x - fx.BaseMin.x;
        if (fx.SeamBaseWidth - width < FixedFitSeamShrinkPx)
            return;

        float frameRight = fx.Size.x * 0.5f;
        float limit = frameRight - fx.Size.x * FixedFitMinSlotFraction;
        float before = fx.ColumnSeamX;
        float raw = fx.BasePin.x + width;
        fx.SeamClamped = raw > limit;
        fx.ColumnSeamX = Mathf.Min(raw, limit);
        fx.SeamWidthBeforeReDerive = fx.SeamBaseWidth;
        fx.SeamBaseWidth = width;
        fx.SeamReDerived = true;

        // HW-VERIFY: this must never print. If it does, the fit's parked-guest list is short by one
        // parker and this line names how far that cost the seam — which is the only way a hardware
        // log can distinguish "the exclusion works" from "the exclusion was never asked".
        VRLog.Alert("WorldUI",
            "COLUMN SEAM RE-DERIVED: the character column measured "
            + $"{width:F0} px this pass but the seam was captured from a {fx.SeamWidthBeforeReDerive:F0} px "
            + $"base, so the capture counted {fx.SeamWidthBeforeReDerive - width:F0} px that are not the "
            + $"column. The seam moves x={before:F0} → x={fx.ColumnSeamX:F0} px, ONCE for this window's "
            + "life and only because no sub-view is open this pass, so nothing is seated on it as it "
            + "moves. WHAT THIS MEANS: every sub-view the game opens is seated on this x, so a seam "
            + "that is too far right draws the battle-goal cards that far right of the character "
            + "column — the user's 'Abstand Quests' report. WHAT TO DO: the base is only supposed to "
            + "contain the game's own column, and the two things this mod parks into this window (the "
            + "confirm control and the ready-icon row) are already refused by CanvasConversion's "
            + "parked-guest list. A THIRD PARKER EXISTS and is not on that list — the fit line's "
            + "'PARKED GUESTS' clause says how many graphics the list did catch, and the difference "
            + "is the one to find.");
    }

    /// <summary>Host-local units per parent-local unit for <paramref name="node"/> — what a wanted
    /// host-local offset must be divided by to become an <c>anchoredPosition</c> delta. 1 for every
    /// transform in an unscaled chain, which is what <see cref="ReassertConversionFrame"/> keeps the
    /// conversion target at; derived rather than assumed because a wrong factor here would move a
    /// window's content by a wrong amount silently.</summary>
    private static float HostPerParentUnits(ConvertedPanel panel, Transform node)
    {
        if (panel.HostRect == null)
            return 1f;
        float hostScale = panel.HostRect.lossyScale.x;
        Transform? parent = node.parent;
        float parentScale = parent != null ? parent.lossyScale.x : hostScale;
        if (Mathf.Abs(hostScale) < 1e-6f || Mathf.Abs(parentScale) < 1e-6f)
            return 1f;
        return parentScale / hostScale;
    }

    /// <summary>
    /// THE FIXED-SIZE FIT'S INSTRUMENT. One line per applied change, plus a restatement every
    /// <see cref="FixedFitStableLogSeconds"/> while nothing changes — so a hardware log tells three
    /// states apart that look identical from the outside: NEVER RAN (no such line at all for this
    /// window), STABLE (comparisons climbing, host writes stuck at 1, deviations flat), and STILL
    /// RESIZING (host writes climbing). The comparison count is always printed next to the deviation
    /// count for exactly that reason.
    ///
    /// <para><b>WHAT ModBuild 199 ADDED, AND WHY.</b> The 198 line printed the HOST size and the
    /// UNION's need, and both were correct while the user's actual complaint went unrecorded: the
    /// character column was being rescaled inside a host that never moved. This line now prints THE
    /// COLUMN'S OWN RENDERED SIZE AND CORNER next to the corner it was pinned at, so "the column is a
    /// constant size" and "the column is being rescaled" are two visibly different logs and no round
    /// after this one has to infer which happened. It also prints what the open sub-view needs WITH
    /// and WITHOUT full-frame backdrop plates, which is the measurement the blur-plate question was
    /// argued from and never had.</para>
    /// </summary>
    private static void LogFixedFit(ConvertedPanel panel, FixedFitState fx, float scale,
        string verdict, string wrote, bool throttled)
    {
        float now = Time.unscaledTime;
        if (throttled && now - fx.LastLogTime < FixedFitStableLogSeconds)
            return;
        fx.LastLogTime = now;

        // The window's REAL width, measured off the host transform rather than re-derived from the
        // placement constants this file does not own — so what DeriveWindowScale actually did with
        // FixedFitWidthPx is STATED here rather than assumed. It is the one number that says whether
        // the window kept its footprint and lost apparent size, or grew and kept it: at 1988 px the
        // board-relative cap returns 1.00 m (45°) unless the character screen is exempted from it,
        // and 1.74 m (72°) if it is. See the end of the region note.
        float rig = RigUnitsPerMetre();
        float unit = panel.HostGo != null ? panel.HostGo.transform.lossyScale.x : 0f;
        float widthMeters = rig > 0f && unit > 0f ? fx.Size.x * unit / rig : 0f;
        // ModBuild 202: the width in ALL THREE units the arguments about this window are made in —
        // authored px (what the sub-views are measured in), millimetres (what the mm-per-px figure
        // divides into) and DEGREES OF VIEW (what "too big" and the map room's ±32° usable cone are
        // stated in). A width quoted in one of them alone is what let a 42 % apparent-size change and
        // a 27° footprint change look like the same decision.
        string physical = widthMeters > 0f
            ? $"{widthMeters:F2} x {fx.Size.y * unit / rig:F2} m = {widthMeters * 1000f:F0} mm wide = "
              + $"{2f * Mathf.Atan2(widthMeters * 0.5f, FixedFitReadingDistanceMeters) * Mathf.Rad2Deg:F0}"
              + $"° of view at {FixedFitReadingDistanceMeters:0.0} m"
            : "physical size unknown (no rig scale yet)";

        // THE PROOF. Not the host, not the union: the column itself, in the size it is DRAWN, against
        // the size it was drawn at when it was pinned. Equal numbers = the user's complaint is gone.
        string column;
        if (!fx.BaseVisible)
        {
            column = "the character column measured NOTHING this pass (no visible non-sub-view "
                     + "graphic under the target) — the pin cannot be checked";
        }
        else
        {
            Vector2 bs = fx.BaseMax - fx.BaseMin;
            column = $"the CHARACTER COLUMN renders {bs.x:F0}x{bs.y:F0} px from ({fx.BaseMin.x:F0},"
                     + $"{fx.BaseMin.y:F0}) [{fx.BaseGraphics} graphic(s)]"
                     + (fx.BaseUsedRawUnion
                         ? " ** MEASURED WITH TRANSIENT CONTENT INCLUDED: every non-sub-view graphic "
                           + "under the target this pass belonged to a hover/tooltip family, so the "
                           + "clean reading was empty and the raw one was used rather than losing the "
                           + "column entirely. This is not expected to happen — report it **"
                         : fx.BaseRawGraphics > fx.BaseGraphics
                             ? $" — {fx.BaseRawGraphics - fx.BaseGraphics} further graphic(s) were "
                               + $"drawn under the target and IGNORED as transient (raw union "
                               + $"{fx.BaseRawMax.x - fx.BaseRawMin.x:F0}x"
                               + $"{fx.BaseRawMax.y - fx.BaseRawMin.y:F0} px); the column is measured "
                               + "from the clean one, so a mouseover cannot move it"
                             : string.Empty);
            if (fx.BasePinned)
            {
                Vector2 drift = fx.BaseMin - fx.BasePin;
                Vector2 grew = bs - fx.BaseSizeAtPin;
                column += $", pinned at ({fx.BasePin.x:F0},{fx.BasePin.y:F0}) at "
                          + $"{fx.BaseSizeAtPin.x:F0}x{fx.BaseSizeAtPin.y:F0} px → "
                          + (Mathf.Abs(drift.x) <= 1f && Mathf.Abs(drift.y) <= 1f
                             && Mathf.Abs(grew.x) <= 1f && Mathf.Abs(grew.y) <= 1f
                              ? "SAME SIZE, SAME PLACE as when it was pinned (this is what the user "
                                + "asked for: the column is never scaled and never re-derived)"
                              : $"MOVED by {drift.x:F0},{drift.y:F0} px and GREW by {grew.x:F0},"
                                + $"{grew.y:F0} px since the pin — if this line ever reports a size "
                                + "change, the column is being rescaled again and that IS the bug");
            }
            else
            {
                column += " (not pinned yet)";
            }
            // PARKED GUESTS — printed unconditionally, because "the exclusion held" and "the
            // exclusion was never asked" are the two readings that must not look alike, and only the
            // count separates them.
            column += $"; PARKED GUESTS refused from the column: {fx.ParkedGuestsThisPass} this pass, "
                      + $"{fx.ParkedGuestsRefused} over this window's life"
                      + (fx.ParkedGuestsThisPass > 0
                          ? $" — the furthest right was '{fx.ParkedGuestWidest}' at "
                            + $"x={fx.ParkedGuestRightX:F0} px, i.e. counting it would have put the "
                            + $"seam {Mathf.Max(0f, fx.ParkedGuestRightX - fx.BaseMax.x):F0} px further "
                            + "right and seated every sub-view the game opens there. These are GAME "
                            + "widgets this mod re-parented into this window (the confirm control and "
                            + "the ready-icon row); they are still drawn, still interactive and still "
                            + "inside the hit rect, the grab bar's ink and the capture frame — they "
                            + "are barred from the COLUMN alone, because they are seated against it"
                          : " (nothing this mod parked into this window was drawn this pass, so the "
                            + "column is the game's own content by construction)");
            ReportColumnOverspill(panel, fx, bs);
        }

        // THE SEAM, and where it came from — so "the seat is the column's edge" is a stated number.
        float mmPerPx = rig > 0f && unit > 0f ? unit / rig * 1000f : 0f;
        string seam = fx.SeamCaptured || fx.BaseVisible
            ? $"the COLUMN SEAM is x={fx.ColumnSeamX:F0} px"
              + (fx.SeamCaptured
                  ? $", CAPTURED ONCE from a {fx.SeamBaseWidth:F0} px base measured with no sub-view "
                    + "open and never re-derived since"
                  : $", still PROVISIONAL (taken from a {fx.SeamBaseWidth:F0} px base measured while a "
                    + "sub-view was open — the first pass with the column alone replaces it)")
              + (fx.SeamClamped
                  ? " and CLAMPED off a base reading that would have left less than "
                    + $"{FixedFitMinSlotFraction:P0} of the frame as slot"
                  : string.Empty)
              + (fx.SeamReDerived
                  ? $" and RE-DERIVED ONCE off a {fx.SeamWidthBeforeReDerive:F0} px capture that the "
                    + "column later contradicted by measuring narrower — see the COLUMN SEAM "
                    + "RE-DERIVED line for what that says about the parked-guest list"
                  : string.Empty)
              + (fx.BaseVisible && fx.BaseMax.x - fx.ColumnSeamX > 1f
                  ? $"; the LIVE base union ends at x={fx.BaseMax.x:F0} px this pass, "
                    + $"{fx.BaseMax.x - fx.ColumnSeamX:F0} px past the seam — that is transient "
                    + "content none of the six serialized sub-view roots owns, and the seat "
                    + "deliberately ignores it"
                  : string.Empty)
            : "the COLUMN SEAM has no base to be captured from yet";

        string view;
        if (fx.ViewName == "none")
        {
            view = "no sub-view is open, so nothing is scaled, no seat is written, and the spare "
                   + $"width is {fx.Size.x * 0.5f - fx.ColumnSeamX:F0} px of empty (transparent) "
                   + "frame right of the seam";
        }
        else
        {
            view = $"the open sub-view group needs {fx.ViewNeed.x:F0}x{fx.ViewNeed.y:F0} px at scale 1 "
                   + $"and is DRAWN AT SCALE {scale:F3} in a {fx.ViewSlotPx:F0} px slot (seam "
                   + $"x={fx.ColumnSeamX:F0} to the frame's right edge x={fx.Size.x * 0.5f:F0})"
                   + (scale >= 0.999f
                       ? " — the same scale as the character column, which is the whole of the ruling"
                       : " — ** NOT 1.000. Nothing in this build solves a scale any more, so a number "
                         + "here that is not 1.000 was written by something else and IS the bug **")
                   + $". GAP {fx.ViewGapPx:F0} px, OVERLAP {fx.ViewOverlapPx:F0} px"
                   + (fx.ViewGapPx <= 1f && fx.ViewOverlapPx <= 1f
                       ? " (both zero — seated flush on the column, which is the whole of the "
                         + "'Lücke' and 'Überlagerung' reports)"
                       : " (NON-ZERO — the seat did not land on the seam and that IS the bug)")
                   + $", SPILL {fx.ViewSpillPx:F0} px past the frame's right edge and "
                   + $"{fx.ViewSpillYPx:F0} px past its top/bottom"
                   + (fx.ViewSpillPx > 1f || fx.ViewSpillYPx > 1f
                       ? " — this view is WIDER OR TALLER than the fixed frame and is deliberately "
                         + "drawn at full size anyway (the frame is transparent and both the hit rect "
                         + "and the supersample capture grow to cover it). If this is not a transient, "
                         + "the fixed width constant is the thing to raise — never the scale"
                       : " (both zero — the fixed width holds for this view)")
                   + ". PER SUB-VIEW:";
            int listed = 0;
            for (int i = 0; i < fx.Views.Count; i++)
            {
                SubViewFit v = fx.Views[i];
                if (!v.Visible || v.View == null)
                    continue;
                listed++;
                float need = v.NaturalMax.x - v.NaturalMin.x;
                view += $" [{v.View.name}: rect {v.SeatedMin.x:F0}..{v.SeatedMax.x:F0} x "
                        + $"{v.SeatedMin.y:F0}..{v.SeatedMax.y:F0} px, SCALE {scale:F3} — "
                        // Reports (3) and (4) were both about WHICH views were scaled, so every member
                        // still states its own case rather than leaving it to be inferred from the
                        // group's numbers — it is just that from ModBuild 202 there is only one case.
                        + (scale >= 0.999f
                            ? $"1.000 by construction; its {need:F0} px against a {fx.ViewSlotPx:F0} px "
                              + "slot decides nothing but the spill above"
                            : $"** foreign scale on this root: its {need:F0} px are being drawn at "
                              + $"{scale:F3}, which this fit no longer asks for **")
                        + $", gap {v.GapPx:F0} px, overlap {v.OverlapPx:F0} px"
                        + (v.TransientDropped > 0
                            ? $", {v.TransientDropped} transient graphic(s) ignored inside it"
                            : string.Empty)
                        + (v.UsedRawUnion
                            ? ", ** measured WITH transient content (the clean reading was empty) **"
                            : string.Empty)
                        + (mmPerPx > 0f
                            ? $", {mmPerPx * scale:F3} mm per authored px = "
                              + $"{mmPerPx * scale * FixedFitBodyCapPx * MmToArcMinAtReadingDistance:F1}' "
                              + $"for a {FixedFitBodyCapPx:F0} px body cap at "
                              + $"{FixedFitReadingDistanceMeters:0.0} m"
                            : string.Empty)
                        + "]";
            }
            if (listed == 0)
                view += " none (the group solved but no member survived the measure)";
            else
                view += $". THE MATCH: {listed} open sub-view(s), ALL AT SCALE {scale:F3}, against a "
                        + "character column that is drawn at 1.000 and is never scaled at all — equal "
                        + "numbers here mean the sub-menu and the character images are the same size "
                        + "and the window reads as ONE surface, which is the ruling this build "
                        + $"implements. Asserted on every one of the {fx.Comparisons} comparison(s) "
                        + "this window has made, so 'all at 1.000' and 'never checked' cannot look "
                        + "alike";
            view += fx.ViewPlates > 0
                ? $". BACKDROP CENSUS (diagnostic, it decides nothing): {fx.ViewPlates} full-frame "
                  + $"plate(s) inside it, the largest '{fx.PlateName}' at {fx.PlateSize.x:F0}x"
                  + $"{fx.PlateSize.y:F0} px; WITHOUT them the view would need "
                  + $"{fx.ViewContentNeed.x:F0}x{fx.ViewContentNeed.y:F0} px — and if that is not "
                  + "materially narrower than the need above, then DISABLING the backdrop cannot get "
                  + "this view to scale 1.000, whatever else it may be worth doing (ModBuild 200 "
                  + "measured 1627 → 1613 px for perks and 1648 → 1648 px for the selector, which is "
                  + "what killed that proposal)"
                : ". BACKDROP CENSUS: no full-frame plate inside it, so its width is all content";
            // ModBuild 201 — WHAT ACTUALLY MAKES THE VIEW THAT WIDE. The two graphics at the content
            // union's x extremes, by name and rect, in the sub-view's own natural space. This is the
            // measurement the "can the wide views reach 1.000?" question has been argued from
            // twice without ever having: if these two turn out to be a second backdrop that merely
            // failed the 95 %-height plate test, the plate route is alive again with evidence behind
            // it; if they are the list and the stat column, it is dead for good.
            view += fx.ContentLeftName.Length > 0
                ? $". CONTENT EXTREMES (diagnostic): the union's LEFT edge x={fx.ContentLeftX:F0} px "
                  + $"is '{fx.ContentLeftName}' ({fx.ContentLeftSize.x:F0}x{fx.ContentLeftSize.y:F0} "
                  + $"px) and its RIGHT edge x={fx.ContentRightX:F0} px is '{fx.ContentRightName}' "
                  + $"({fx.ContentRightSize.x:F0}x{fx.ContentRightSize.y:F0} px)"
                : ". CONTENT EXTREMES: none measured (every graphic in the view was a full-frame plate "
                  + "or transient)";
        }

        // THE MOUSEOVER LEDGER (report 5). Printed on EVERY line, including the zero, because "no
        // mouseover moved anything" and "the rule was never reached" are the two states this whole
        // round is about telling apart — the same reason the comparison count sits next to the
        // deviation count.
        string transientLedger =
            $"MOUSEOVER LEDGER: {fx.TransientThisPass} transient graphic(s) refused this pass, "
            + $"{fx.TransientIgnored} over this window's life"
            + (fx.TransientFamilyMask != 0
                ? $", from {DescribeTransientFamilies(fx.TransientFamilyMask)}"
                : ", no hover/tooltip family has been seen inside this window yet")
            + (fx.TransientWidest.Length > 0
                ? $"; the widest one this pass was {fx.TransientWidest} at "
                  + $"{fx.TransientWidestSize.x:F0}x{fx.TransientWidestSize.y:F0} px"
                : string.Empty)
            + ". Transient content is still DRAWN and the hit rect and the capture frame still grow "
            + "to cover it — it is only barred from the MEASURE, so it can move neither the host "
            + "size, nor the column's pin, nor the seam, nor a sub-view's scale or seat";

        VRLog.Info("WorldUI",
            $"FIXED FIT '{(panel.HostGo != null ? panel.HostGo.name : "?")}' {verdict}: host pinned at " +
            $"{fx.Size.x:F0}x{fx.Size.y:F0} px = {physical}" +
            (mmPerPx > 0f ? $" ({mmPerPx:F3} mm per authored px)" : string.Empty) +
            $"; {column}; {seam}; {view}; {transientLedger}" +
            (wrote.Length > 0 ? $"; wrote {wrote}" : "; wrote nothing") +
            $". PLACEMENT: {(fx.SolutionFrozen ? "FROZEN — the scale and the seat are constants until the set of open sub-views changes" : fx.ViewName == "none" ? "no sub-view open, nothing to place" : "solving (not frozen yet — the first gated write freezes it)")}." +
            $" fixed fit: {fx.Comparisons} comparison(s) made, {fx.Deviations} deviation(s) found, " +
            $"{fx.Deferred} deferred by the settle gate, {fx.HostWrites} host-size write(s), " +
            $"{fx.BaseWrites} column re-assert(s), {fx.ScaleWrites} sub-view scale write(s), " +
            $"{fx.Shifts} sub-view re-seat(s), {fx.ReAsserts} foreign overwrite(s) of a pose we had " +
            "written — the host size is written ONCE, the host is never re-posed, and the conversion " +
            "target itself is never scaled." +
            // ModBuild 396 — WHY THE SETTLE BURST READ ZERO, stated on the line the burst is read
            // beside. The 395 session contains no SUB-VIEW SETTLE BURST line at all, where the 392
            // session's burst was the only instrument that had quantified the flash. Nothing about
            // the burst changed: TickSubViewBurst arms only when the OPEN-SET SIGNATURE changes
            // after a baseline observation, and a session in which the player never switched
            // sub-view produces no event for it to count. That is a correct zero and an unreadable
            // one, so the state now rides here — a burst count of 0 beside a valid baseline and a
            // non-zero sub-view count means "no tab was switched", not "the instrument is gone".
            $" SUB-VIEW BURST STATE: {fx.Bursts} burst(s) armed and {fx.BurstsExpired} expired this "
            + $"session over {fx.Views.Count} known sub-view(s); the open-set baseline is "
            + (fx.BurstSigValid ? "OBSERVED" : "NOT YET OBSERVED (the burst cannot arm before it)")
            + ". The burst counts CHANGES of the open set, so zero bursts with a valid baseline "
            + "means the set never changed while this window was up — read the COLUMN OVERSPILL "
            + "line for the flash instead, which is measured every pass and needs no event. "
            // ModBuild 435 — THE TWO NUMBERS THAT SAY WHETHER THIS ROUND'S CORRECTION IS ALIVE, and
            // they ride here because this line is guaranteed to appear in any session that opens the
            // character screen. SEAT STAMP DECLINED counts the passes on which the SETTLED branch
            // refused to call an unmeasurable open set "seated" — the exact hole the 434 log fell
            // through. SEAT WATCH counts episodes, and a zero there in a session with a sub-view
            // open means the watch never ran rather than that it found nothing.
            + $"SEAT STAMP DECLINED: {fx.SeatUnseen} pass(es) — each one is a pass on which the game "
            + "had a sub-view open, the measurement could not see it yet (its show fade), and this "
            + "fit therefore did NOT tell the seat veil it was seated. Before ModBuild 435 every one "
            + "of these stamped a seat and lifted the veil onto an un-seated sub-view. "
            + $"SEAT WATCH: {fx.WatchEpisodes} episode(s), {fx.WatchEpisodesClean} of them with no "
            + $"frame of the sub-view drawn before its seat existed, worst measured drop "
            + $"{fx.WatchWorstDropPx} px. "
            + DescribeHiddenWindowVeil(panel) + ".");
    }

    // =============================================================================================
    // THE RE-FIT LOOP INSTRUMENT — and the guard it justifies
    // =============================================================================================
    //
    // THE MEASUREMENT (ModBuild 195 hardware log, .planning/debug/LogOutput.log). Every APPLIED fit
    // of the session — 159 lines, three windows — split by whether the host SIZE actually changed:
    //
    //   'Panel_Modal_New Party display'   11 applied,   0 size-neutral, 11 changed — the equipment /
    //       perks / ability-card views growing and collapsing the permanent character window:
    //       328 ↔ 716 ↔ 1066 ↔ 1920 px. This is the growth path, and it must keep working.
    //   'Panel_Modal_UI Map Esc Menu'      2 applied,   1 size-neutral,  1 changed. The size-neutral
    //       one is NOT a no-op: it moved the target by (-125,-107) ("rigid re-centre only").
    //   'Panel_Modal_UI Quest Popup'     146 applied, 142 size-neutral,  4 changed.
    //
    // 142 of 146 applied fits on ONE window wrote "512x1015 → 512x1015 px (content offset 0,-174)",
    // over and over, for as long as the window was open, at the FitRefitMinIntervalSeconds cadence.
    // That damping interval is the ~1 s period of the user's "the travel button jumps to a different
    // place for one frame, in a loop".
    //
    // WHY A CONVERGED FIT KEPT RE-APPLYING — the fit CHANGES WHAT IT MEASURES, so it can never
    // settle. The two measurements in that line are taken in two different layout states:
    //
    //   * the periodic check measures the window as the game leaves it:  512x667 px at (0,174)
    //     (the apply trace's own first pass states the input: "#1 host=512x667 shift=0,-174");
    //   * ApplyFitConverging then calls FlushPendingLayout — LayoutRebuilder.ForceRebuildLayoutImmediate
    //     on the whole subtree — and re-measures: 512x1015 px at (0,0), which is the size already
    //     written, so it reports "CONVERGED" and writes the host back to what it was.
    //
    // The forced rebuild inflates this window's content by 348 px downward; the game's own layout
    // relaxes it back before the next frame. Two independent instruments agree that the RELAXED
    // state is what renders: the hit-rect walk (TickHitRect, which never flushes) reports "DRAWN
    // CONTENT 590x738 px at (-38,222)" — bottom edge y = -147, exactly the bottom the unflushed fit
    // measure implies — while the flushed measure claims the content reaches y = -496. And the
    // settle gate said so from the first frame: on all THREE opens of this window its line reads
    // "forced rebuild changed the measurement 18x (this check: YES)", i.e. every single check. Every
    // other window in the session reads 0x or 1x. That counter is the discriminator, and it names
    // exactly the one window that then looped forever.
    //
    // WHAT IS FIXED HERE, AND WHAT IS DELIBERATELY NOT. Not fixed: WHICH of the two layout states
    // the window should be sized to. That is a sizing decision on a window whose size nobody has
    // complained about, and it would move the frame, the close-X, the grab bar and the supersample
    // capture frame of a floated window — the round that clamped a union into the frame it was
    // trying to see past is the standing warning against touching that lightly. Fixed: the fit no
    // longer RE-APPLIES a result it has already proven changes nothing. The state stays exactly
    // where it is today; only the perpetual re-application and its forced rebuild go away.
    //
    // WHY LEVEL-TRIGGERED AND NOT A LONGER INTERVAL. Raising FitRefitMinIntervalSeconds makes the
    // twitch rarer and leaves the cause; and the cause is not a rate at all — it is that the
    // decision "does this need a re-fit?" was made against a quantity (the freshly measured content)
    // that the apply itself does not have to move, instead of against what the apply LAST WROTE. So
    // the guard records the apply's OUTPUTS (host size, target anchoredPosition) around the call and
    // its INPUTS (measured size, measured center, host size) with them: when the same inputs recur
    // and the outputs did not move last time, the apply is skipped. Any real change in either input
    // releases it — which is why the equipment window can still go 328 → 1066 px and back.
    //
    // WHAT IT DOES TO THE SAME LOG, replayed decision by decision over all 159 recorded fits (the
    // guard's own predicate, fed the entry measurement, entry host size and recorded outcome of
    // every line, with the state reset at each window OPEN because a new panel is a new entry):
    //
    //   'Panel_Modal_New Party display'  11 fits → 11 applied,   0 suppressed  (growth untouched)
    //   'Panel_Modal_UI Map Esc Menu'     2 fits →  2 applied,   0 suppressed
    //   'Panel_Modal_UI Quest Popup'    146 fits →  9 applied, 137 suppressed, guard released 3x
    //
    // and, decisively, ZERO fits that changed the host size or moved the target were suppressed —
    // the guard cannot suppress one, because the recorded signature is only ever written by an apply
    // that did neither. ForceRebuildLayoutImmediate calls from this path on the quest window over
    // that session: 291 → 17.

    /// <summary>
    /// Per-panel re-fit loop accounting. The counters exist so a hardware log can tell three states
    /// apart that all look alike from the outside: a fit that CONVERGED (comparisons climbing,
    /// deviations flat or fully suppressed), one that NEVER RAN (comparisons 0), and one that is
    /// STILL THRASHING (applies climbing with no-ops among them). Every fit line carries them, so
    /// there is no separate cadence to read.
    /// </summary>
    private sealed class FitLoopState
    {
        /// <summary>The host GameObject this entry belongs to (Unity-null once destroyed → pruned).</summary>
        internal GameObject? Owner;

        /// <summary>Content measurements this panel's fit has evaluated (the denominator).</summary>
        internal int Comparisons;

        /// <summary>Of those, how many fell OUTSIDE the 2 % dirty band, i.e. asked for a re-fit.</summary>
        internal int Deviations;

        /// <summary>Deviations that reached <see cref="ApplyFitConverging"/>.</summary>
        internal int Applies;

        /// <summary>Applies that wrote neither the host size nor the target position.</summary>
        internal int NoOpApplies;

        /// <summary>Deviations the converged guard skipped (the re-applications that no longer happen).</summary>
        internal int Suppressed;

        /// <summary>Forced layout rebuilds (<see cref="FlushPendingLayout"/>) charged to this panel —
        /// the expensive, side-effecting operation this whole section exists to bound.</summary>
        internal int Flushes;

        /// <summary>True while <see cref="ConvergedSize"/>/<see cref="ConvergedCenter"/>/
        /// <see cref="ConvergedHost"/> hold an input triple that provably produced no write.</summary>
        internal bool ConvergedValid;

        /// <summary>Whether the guard's engage line has already been written for this episode.</summary>
        internal bool ConvergedLogged;

        /// <summary>Measured content size of the apply that wrote nothing.</summary>
        internal Vector2 ConvergedSize;

        /// <summary>Measured content center of the apply that wrote nothing.</summary>
        internal Vector2 ConvergedCenter;

        /// <summary>Host rect size the apply that wrote nothing started and ended at.</summary>
        internal Vector2 ConvergedHost;
    }

    /// <summary>Fit loop state by host GameObject instance ID (one entry per converted panel).</summary>
    private static readonly Dictionary<int, FitLoopState> FitLoops = new(8);

    /// <summary>Fallback entry for a panel whose host GameObject is already gone — keeps every call
    /// site total without a null check of its own; nothing reads it.</summary>
    private static readonly FitLoopState OrphanFitLoop = new();

    /// <summary>This panel's loop accounting, created on first use. Pruned of dead panels whenever a
    /// new entry appears (the only moment the dictionary can grow), exactly like
    /// <see cref="PruneHitRects"/>.</summary>
    private static FitLoopState GetFitLoop(ConvertedPanel panel)
    {
        if (panel.HostGo == null)
            return OrphanFitLoop;
        int id = panel.HostGo.GetInstanceID();
        if (FitLoops.TryGetValue(id, out FitLoopState? entry) && entry != null)
            return entry;
        FitLoops[id] = entry = new FitLoopState { Owner = panel.HostGo };
        PruneFitLoops();
        return entry;
    }

    /// <summary>Drop entries whose host GameObject is gone.</summary>
    private static void PruneFitLoops()
    {
        if (FitLoops.Count < 2)
            return;
        List<int>? dead = null;
        foreach (KeyValuePair<int, FitLoopState> pair in FitLoops)
        {
            if (pair.Value == null || pair.Value.Owner == null)
                (dead ??= new List<int>(4)).Add(pair.Key);
        }
        if (dead == null)
            return;
        for (int i = 0; i < dead.Count; i++)
            FitLoops.Remove(dead[i]);
    }

    /// <summary>The instrument's one sentence: comparisons made ALONGSIDE deviations found, so
    /// "converged", "never ran" and "still thrashing" read as three different lines.</summary>
    private static string DescribeFitLoop(FitLoopState loop) =>
        $"fit loop: {loop.Comparisons} comparison(s) made, {loop.Deviations} deviation(s) found, " +
        $"{loop.Applies} applied ({loop.NoOpApplies} of them wrote nothing), {loop.Suppressed} " +
        $"re-application(s) suppressed by the converged guard, {loop.Flushes} forced layout " +
        "rebuild(s) charged to this panel";

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
    /// <paramref name="frameReasserted"/> tells the caller whether this apply had to repair the
    /// conversion frame — a write in its own right, and one the converged guard must never mistake
    /// for a no-op (see FitLoopState).
    /// </summary>
    private static void ApplyFitConverging(ConvertedPanel panel, RectTransform root,
        ref Vector2 size, ref Vector2 center, out string trace, out bool frameReasserted)
    {
        var sb = new System.Text.StringBuilder(160);
        sb.Append("apply: ");
        // Kill the coupling at its source where we are allowed to: Convert pinned the target's
        // whole frame precisely so the host rect could be resized underneath it and so the measure
        // reads real geometry. The hardware log proves the game re-drives it.
        frameReasserted = ReassertConversionFrame(panel, out string frameNote);
        if (frameReasserted)
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
        //    THE REFERENCE IS 1 FOR EVERY PANEL, INCLUDING THE FIXED-SIZE ONE. ModBuild 198 made an
        //    exception here because its fixed-size branch scaled the conversion TARGET to fit a
        //    full-screen sub-view — and that exception is exactly what the user reported for the
        //    third time: the target's subtree contains the permanently-visible character column, so
        //    scaling it to fit a sub-view rescaled the column too. ModBuild 199 scales the SUB-VIEW's
        //    own root instead and never touches the target, so this guard is back to holding every
        //    converted target at 1 — and it now actively PROTECTS the column, because a target scale
        //    is once again unambiguously drift (ModBuild 23 found this very target at 0.14).
        const float want = 1f;
        Vector3 scale = t.localScale;
        if (Mathf.Abs(scale.x - want) > 0.001f || Mathf.Abs(scale.y - want) > 0.001f
            || Mathf.Abs(scale.z - want) > 0.001f)
        {
            t.localScale = Vector3.one * want;
            sb.Append($"localScale was ({scale.x:F2},{scale.y:F2},{scale.z:F2}) → {want:F3}");
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

        // 6. THE SHARED WINDOW'S DESIGN FRAME (ModBuild 450). Same shape as the height cap above and
        //    for a stricter reason: that one keeps ONE client's window the same size across opens,
        //    this one keeps EVERY client's copy of the same window the same size as every other's.
        //    The two machines in the ModBuild 448 logs run 1920x1080 and 2580x1080 canvases, so a
        //    canvas-anchored window is authored 660 px wider on one of them and every metre derived
        //    from it inherits that. Re-pinning the target to the canvas's own referenceResolution
        //    makes the GAME lay the window out at the design width, which is what puts the content
        //    fit, the ink bounds, the grab bar and the shared seat on identical inputs.
        //    It rides `includeHeightCap` for exactly the reason that parameter exists: the cheap
        //    per-frame guard must not fight a layout component, and the pre-reveal maintenance pass
        //    and every applied fit both carry it. Anchors are already centred by step 4 above, which
        //    is what makes sizeDelta a SIZE here rather than an inset.
        if (includeHeightCap && SharedWindowSize.IsArmed(panel)
            && SharedWindowSize.Repin(panel, t, out string sharedNote))
        {
            sb.Append(sb.Length > 0 ? "; " : string.Empty).Append(sharedNote);
        }

        if (sb.Length == 0)
            return false;
        note = "conversion frame RE-ASSERTED: " + sb;
        if (!panel.FrameDriftLogged)
        {
            panel.FrameDriftLogged = true;
            VRLog.Warn("WorldUI", $"MODAL FIT: '{panel.HostGo.name}' {note}. Something other than this " +
                                  "guard owned the frame Convert pinned — usually the game re-driving it, " +
                                  "and on a SHARED window also this build's own design-frame re-pin (that " +
                                  "clause names itself in the note above and is a WRITE WE INTEND, not " +
                                  "drift). Everything the content fit measures is expressed in that frame, " +
                                  "so it is restored before the measure is trusted. (Reported once per " +
                                  "panel; the fit lines carry the per-apply note.)");
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
    /// animation, warn once at <see cref="ConvertedPanel.FitFirstDeadline"/>), then a THROTTLED
    /// periodic re-check every ~<see cref="FitCheckIntervalFrames"/> frames per
    /// panel so content GROWTH (story pages, log lines) re-fits the host. Steady
    /// state cost: one Graphic-union scan per panel per 30 frames; the 2 % no-op
    /// threshold inside <see cref="FitHostToContent"/> is the dirty check, and
    /// shrink/re-center re-fits are additionally damped there (test #17 churn).
    /// </summary>
    private static void TickFit(ConvertedPanel panel)
    {
        // THE INTERACTIVE AREA, FIRST AND UNCONDITIONALLY (user report 2026-08-21, the equipment
        // window's swap panel). Deliberately ABOVE every early return in this method: the hit rect
        // must keep tracking a window whose SIZING is finished or disabled. `FitEnabled` goes false
        // the moment a one-shot menu locks its rect, and the equipment window's content grew long
        // after its pre-reveal first fit had committed — a hit-rect update hung off the fit's own
        // gates would be blind in exactly the cases that produced the report. Self-throttled
        // (FitCheckIntervalFrames, staggered per panel) and self-guarded; see TickHitRect.
        TickHitRect(panel);

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

        // USER REPORT 2026-09-03 (b)+(c): a fixed-size window whose OPEN SUB-VIEW SET just
        // changed is checked at close spacing for a few frames instead of waiting out the
        // cadence below — otherwise the sub-view's seat (the battle-goal picker's -381 px in
        // the ModBuild 385 log) lands up to 60 frames after it is on screen and the player
        // watches it pop. Deliberately BELOW every one-shot and pre-reveal return above, so
        // the reveal gate cannot be reached from here; the only field it writes is
        // FitNextCheckFrame. Part 9c carries the whole argument and the cost.
        TickSubViewBurst(panel);

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
        // ModBuild 291 — A DORMANT PANEL IS HIDDEN FOR THE MEASUREMENT MODE AND VISIBLE FOR THE
        // CADENCE, AND THE SPLIT IS THE WHOLE REASON THE TWO WERE EVER ONE FLAG.
        //
        // The per-frame arm exists because a panel behind the REVEAL GATE is racing a 0.6 s deadline:
        // its geometry has to be final before the window is shown, so the check is worth a forced
        // layout pass every frame for at most that long. A panel the liveness rule made dormant is
        // racing nothing — it is off the screen because it draws nothing, and its verify watch would
        // otherwise spend up to 1.5 s running LayoutRebuilder.ForceRebuildLayoutImmediate over a
        // 2115-transform party display EVERY frame for a window nobody can see. It still uses the
        // FLUSHED measurement mode below (the canvases really are disabled, so an unflushed read
        // really would be stale) — only the cadence drops back to the throttled one.
        bool dormant = ModalFallback.IsDormantPanel(panel);
        if ((!hidden || dormant) && Time.frameCount < panel.FitVerifyNextCheckFrame && !expired)
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
        {
            // ModBuild 250 — THIS RETURN IS THE ONE PATH THAT BYPASSES ForceCommitDue, AND IT WAS
            // SILENT. A window whose content never becomes measurable never commits a fit at all:
            // it reveals at the raw captured rect on the deadline, with fit=pending, and everything
            // downstream that waits on FitMeasuredOnce — above all the ONE pre-reveal pose re-place
            // — is then refused forever (.planning/debug/LogOutput.log, 'UI Event Window' FORCED at
            // 601 ms). There is nothing to commit here, so the behaviour is unchanged and correct;
            // what was missing is that a hardware log could not tell this case ("the content never
            // appeared") apart from "the content appeared and its layout never settled". One line,
            // once, at the moment it stops mattering. [[sentinel-overflow-and-silent-scans]]
            if (ForceCommitDue(panel) && !panel.FitNeverMeasurableLogged)
            {
                panel.FitNeverMeasurableLogged = true;
                VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' pre-reveal first fit found "
                                      + $"NOTHING MEASURABLE by its reveal deadline — {report}. The "
                                      + "window reveals at the RAW captured rect, fit=pending, and "
                                      + "every consumer that waits on the first fit is skipped from "
                                      + "here on: the one pre-reveal pose re-place above all, which "
                                      + "means a spawn pose derived from this rect is the pose the "
                                      + "window keeps. If this window is a SHARED one its half-size "
                                      + "is a term of the shared anchor, so this line is the reason "
                                      + "its home looks wrong — and the suspect is the CONTENT, not "
                                      + "the placement.");
            }
            return; // nothing visible yet (fade-in) — retry next frame, bounded by the reveal deadline
        }

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
        // COUNTED, because this call is not free and not side-effect-free: it is a full layout pass
        // over the window's subtree, and uGUI's LayoutGroups re-drive their children's anchors and
        // anchoredPosition while it runs — the mechanism behind the travel button's one-frame jump.
        // Every fit line reports the running total per panel (see DescribeFitLoop).
        GetFitLoop(panel).Flushes++;
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

    // =============================================================================================
    // THE HIT RECT — the interactive area follows the content the window actually DRAWS
    // =============================================================================================
    //
    // THE REPORT (user, 2026-08-21, screenshot .planning/debug/ausrüstungsmenu.jpg):
    //
    //   "Im Ausrüstungsmenü kann man einen Gegenstand anklicken um ihn mit einem anderen zu
    //    tauschen, dafür wird das Fenster größer - allerdings wächst das Fenster in VR nicht bzw.
    //    der interaktive Bereich nicht. Der Laser geht durch den rechten Bereich hindurch der
    //    angewachsen ist, dort kann man entsprechend auch nichts bedienen. […] achte darauf wo das
    //    x ist, da endet der interaktive Bereich des Fensters."
    //
    // WHAT ACTUALLY GROWS, FROM THE HARDWARE LOG (ModBuild 194, .planning/debug/Player.log). The
    // window's OWN RectTransform does not grow at all. It is 532x1080 uGUI px at conversion
    // (line 5845: "Converted 'Modal_Character Items Equipment Content' to world space (532x1080
    // px)") and it is still 532x1080 in all THIRTY supersample state reports of that session
    // (line 5892 and 29 identical siblings):
    //
    //   PANEL SUPERSAMPLE 'Character Items Equipment Content': host rect 532x1080 uGUI px,
    //   CAPTURE FRAME 805x1080 (GROWN by 273x0 px to cover content drawn outside the host frame;
    //   CLAMPED — content IS being cropped)
    //
    // and the warning one line earlier (5868) gives the unclamped truth:
    //
    //   'Character Items Equipment Content' draws content that reaches 903x1080 uGUI px around a
    //   532x1080 host rect
    //
    // So the game enables a sibling subtree that draws 371 px to the RIGHT of the window's own
    // rect and never touches that rect. A world-space uGUI canvas does not clip at its own root
    // rect, so all 371 px are DRAWN and VISIBLE — they are the inventory column in the screenshot.
    //
    // WHAT BOUNDS A HIT TODAY: exactly that unchanged root rect.
    //   * RayUguiDriver.TryIntersect takes the canvas RectTransform's four world corners and
    //     accepts the ray only for u,v in [0,1] — i.e. inside the 532 px.
    //   * PokeInteractor.TickCanvases does `rect.rect.Contains(local)` — the same 532 px.
    // 532 / 903 = 59 %, so 41 % of the visible window is dead to both. The close-X is parented to
    // the HOST rect at anchor (1,1) (ModalCloseButton.cs:147-151), which is why the user could read
    // the boundary straight off the screenshot: the X marks the host rect's right edge.
    //
    // WHY THE CONTENT FIT COULD NEVER HAVE CAUGHT THIS. TickFit's periodic growth re-check DOES
    // run on this panel every 30 frames, and it measures 532x1080 every time — because
    // TryMeasureContent CLAMPS its union into the conversion target's own frame
    // ("off-screen/overflow elements must not grow the panel beyond the window's own rect", the
    // block above `Vector2 sz = max - min;`). That clamp is correct for SIZING — it is what stops
    // a stray off-screen graphic from inflating a window — but it means the fit is structurally
    // incapable of reporting content that lives outside the window frame. The same log proves it
    // for a second window: 'UI Quest Popup' logs "512x1015 → 512x1015 px … [frame-clamped]" over
    // and over while the supersample frame reports 154x83 px of overspill on the same window.
    //
    // WHAT THIS SECTION DOES — and what was REJECTED.
    //
    //   REJECTED: grow the WINDOW (relax the frame clamp so HostRect.sizeDelta covers the
    //   overspill). It is the tidier story — visible frame, X, grab bar and hit area would all
    //   agree by construction — and it would also un-clamp the supersample capture frame that is
    //   currently cropping this window. It was rejected because of what the host rect IS to
    //   everything else: ApplyFitConverging re-centres the target by -center on every applied fit,
    //   the close-X rides the host's top-right corner, GrabbableModal CAPS its grab bar at a
    //   fraction of `_panel.HostRect.rect` (since ModBuild 236 the bar is PLACED against the drawn
    //   ink, but the frame is still the ceiling on its width and the floor under its Y), and
    //   MapRoom slots the window by the angular width that rect subtends. The swap panel opens and
    //   closes on every item the player
    //   swaps, so growing the host would slide the window ~185 px sideways under his hands, move
    //   the X and re-size the grab bar, once per swap — on a window whose pose the log explicitly
    //   calls PLAYER-OWNED after a grab. The shrink damping (FitStableSeconds +
    //   FitRefitMinIntervalSeconds) would additionally hold the enlarged frame for over a second
    //   after the swap panel closed, i.e. a laser hitting empty space. That is a worse defect than
    //   the one being fixed, and it would land on every converted window at once.
    //
    //   CHOSEN: the host rect stays the window's FRAME (placement, X, grab bar, supersample — all
    //   untouched), and the RAY/POKE PLANE gets its own HIT RECT: the union of the host rect and
    //   the content the window measurably draws, re-measured on the fit's own cadence. Nothing is
    //   written to any game transform, so there is no write war to lose; the only consumer is the
    //   ray/poke intersection test.
    //
    // MEASURED, NEVER PADDED. The union is built from the SAME per-graphic visibility verdict the
    // content fit uses (TryGetVisibleHostRect: enabled, not culled, effective alpha >= FitMinAlpha,
    // non-degenerate draw rect, clamped to its enclosing RectMask2D/Mask viewport) over the SAME
    // root the fit measures (ResolveFitRoot). What it deliberately does NOT do is apply the target
    // frame clamp — that clamp is the thing this measurement exists to see past. So the hit rect
    // reaches exactly as far as pixels the player can see, and not one pixel further; when the
    // swap panel closes and its graphics go inactive/transparent, the union collapses back and the
    // hit rect follows it down on the next check.
    //
    // WHAT THE GROWTH COSTS, stated plainly: inside the grown region the laser now WINS the
    // nearest-canvas arbitration against anything behind it. That is the same rule that already
    // governs the host rect, applied over the area the window visibly covers — but it does mean a
    // window drawn behind the overspill becomes unclickable there. It is reported in the log line
    // below (the grown extent, and the graphic that reaches furthest outside) precisely so that
    // trade is auditable instead of assumed.

    /// <summary>
    /// Hard bound on the hit rect as a multiple of the host rect, per axis. This is a SAFETY
    /// BOUND, not a pad: it never adds area, it only refuses to follow a pathological measurement
    /// (a window that somehow measures a graphic parked far off in the scene would otherwise
    /// register an enormous invisible click plane that shadows everything behind it). 4x is
    /// deliberately looser than PanelSupersample's 2x expansion clamp — that one bounds a VRAM
    /// allocation, this one bounds nothing but a rectangle test — and the equipment window's
    /// measured 903/532 = 1.70x therefore passes it untouched. Every clamp is reported.
    /// </summary>
    private const float MaxHitExpansion = 4f;

    /// <summary>Per-panel hit-rect state. Keyed by host-canvas instance ID so the ray/poke drivers
    /// can look it up from the <see cref="Canvas"/> alone, which is all they hold.</summary>
    private sealed class HitRectEntry
    {
        /// <summary>The host canvas this entry belongs to (Unity-null once destroyed → pruned).</summary>
        internal Canvas? Canvas;

        /// <summary>THE ANSWER: what a ray/poke must be inside, in the host RectTransform's own
        /// local space (uGUI px). Always contains the host rect.</summary>
        internal Rect Hit;

        /// <summary>The host rect at the last committed measurement (uGUI px).</summary>
        internal Rect Host;

        /// <summary>The measured drawn-content union, BEFORE the host union and the clamp.</summary>
        internal Rect Content;

        /// <summary>True when <see cref="MaxHitExpansion"/> had to cut the union down.</summary>
        internal bool Clamped;

        /// <summary>Graphics that contributed to the last committed union.</summary>
        internal int Contributors;

        /// <summary>Path of the graphic reaching furthest OUTSIDE the host rect ("" when none does).</summary>
        internal string OutsideOwner = string.Empty;

        /// <summary>How far outside the host rect that graphic reaches (uGUI px).</summary>
        internal float OutsideBy;

        /// <summary>Frame the next measurement is due (throttle, staggered per panel).</summary>
        internal int NextCheckFrame;

        /// <summary>Committed changes so far — the log line's "this is not the first time" counter.</summary>
        internal int Commits;

        /// <summary>Unscaled time the next log line may be written (see <see cref="HitRectLogIntervalSeconds"/>).</summary>
        internal float NextLogAt;

        /// <summary>Commits swallowed by that throttle since the last line — reported ON the next
        /// line, so a throttled burst is visible as a burst instead of vanishing.</summary>
        internal int SuppressedCommits;

        /// <summary>Whether the LAST LOGGED line said the hit rect was larger than the host rect.
        /// A commit that flips this is never throttled: it is the transition the bug report is
        /// about ("the window grew, the interactive area did not"), and burying it behind a rate
        /// limit would mean the one line the next hardware log is read for could be the one
        /// swallowed. Sub-second churn WITHIN a state still throttles normally.</summary>
        internal bool LoggedGrown;

        /// <summary>ModBuild 242 — the SHRINK RUN. How many consecutive measurements have agreed
        /// that the drawn content is materially smaller than the frame. Shrinking the interactive
        /// area is the one direction that can KILL INPUT, so it is granted on the same terms
        /// <c>GrabbableModal</c> grants an ink release: a dead band, then a run of agreeing
        /// samples, and any sample that disagrees resets it to zero. Growth still commits on
        /// sight.</summary>
        internal int ShrinkRun;

        /// <summary>The narrowed rect the current run is arguing for — carried forward as the
        /// OUTERMOST member of the run, so a wobbling run commits its most generous reading.</summary>
        internal Rect ShrinkCandidate;

        internal bool ShrinkValid;

        /// <summary>Narrowings committed over this window's life — the falsifier's "did this ever
        /// fire" counter. ModBuild 243 made it count the TRANSITION into a narrowed state; before
        /// that it ticked once per walk while the narrowing merely persisted, which is how the
        /// ModBuild 242 log came to read 332 narrowings on a rect that had not moved.</summary>
        internal int Shrinks;

        /// <summary>ModBuild 243 — the last <c>PanelInkBounds.ActiveSetSignature</c> of this
        /// window's target, and how many times it has moved. A change forces the expensive walk on
        /// the same frame, so the interactive area follows a tab press as fast as the grab bar
        /// does. The count is on the log line because a signature that never moves and a window
        /// that never changes look identical.</summary>
        internal int Signature;

        internal bool SignatureValid;

        internal int SignatureEdges;

        /// <summary>ModBuild 440 — THE MOD'S OWN CHROME ON THIS HOST, in the host rect's own local
        /// px: the union of every raycast target under a mod-owned subtree parked on the host
        /// (today the close X's plate and its <c>HitPlane</c>, and a transient's full-window dismiss
        /// catcher). Measured by <see cref="TryMeasureChrome"/>, which is a geometric walk and NOT
        /// the drawn-content walk — the content walk skips mod art by name and starts at the GAME's
        /// fit root, so no chrome has ever been able to reach the hit rect through it.</summary>
        internal Rect Chrome;

        internal bool ChromeValid;

        /// <summary>Raycast targets that went into <see cref="Chrome"/>.</summary>
        internal int ChromeParts;

        /// <summary>Path of the first chrome graphic unioned — names WHICH piece is holding the
        /// rect open, so a reader never has to guess which of the two shapes it is.</summary>
        internal string ChromeOwner = string.Empty;

        /// <summary>The chrome floor actually pushed an edge of the committed rect back out, i.e.
        /// without it the beam would pass straight through that piece of chrome.</summary>
        internal bool ChromeHeld;

        /// <summary>Commits over this window's life on which the floor bit. A count that stays 0 on
        /// a window whose X is reachable means the narrowing never reached the plate in the first
        /// place, which is the normal reading for an INK-seated X.</summary>
        internal int ChromeHolds;

        /// <summary>Last reported chrome verdict, so the Note line prints on a TRANSITION rather
        /// than on every commit: 0 = not reported yet, 1 = no chrome, 2 = chrome inside the rect on
        /// its own, 3 = the floor held it open, 4 = chrome still OUTSIDE the committed rect.</summary>
        internal int ChromeVerdict;
    }

    /// <summary>ModBuild 242 — margin left around the drawn content when the interactive area is
    /// allowed to shrink below the frame, in the window's authored px. It has to absorb a widget
    /// that appears BETWEEN two 30-frame walks: the rect is the laser's whole verdict, so a margin
    /// that is too tight is dead input on a button that was drawn 200 ms ago.</summary>
    private const float HitRectContentPadPx = 64f;

    /// <summary>How far inside the frame the padded content must sit before shrinking is even
    /// considered. BY VALUE from <c>GrabbableModal.InkReleaseDeadBandPx</c> (32) — one 32 px
    /// quantum, the same dead band the capture frame's shrink hysteresis uses, so all three
    /// instruments agree about what "smaller" means.</summary>
    private const float HitRectShrinkDeadBandPx = 32f;

    /// <summary>Agreeing measurements a narrowing must survive before it commits. Three at
    /// <see cref="FitCheckIntervalFrames"/> is ~1 s.</summary>
    private const int HitRectShrinkRuns = 3;

    /// <summary>
    /// Minimum seconds between HIT RECT log lines for one window. THE RECT ITSELF IS NEVER
    /// THROTTLED — only the line is. WHY the throttle exists at all: the union follows real drawn
    /// content, and some of that content legitimately comes and goes (this very window carries a
    /// game-owned 'UI Party Inventory Item Tooltip' that can pop up outside the frame and follow the
    /// pointer). Each appearance is a real change to the interactive area and must land in the rect
    /// immediately; what it must not do is bury the log. Suppressed commits are counted and named on
    /// the next line, so a window that is churning reads AS churning.
    /// </summary>
    private const float HitRectLogIntervalSeconds = 1f;

    /// <summary>Hit rects by host-canvas instance ID. Small (one entry per floated window).</summary>
    private static readonly Dictionary<int, HitRectEntry> HitRects = new(8);

    /// <summary>Scratch for <see cref="TryMeasureDrawnUnion"/>. Deliberately NOT
    /// <see cref="GraphicScratch"/>: the two walks run in the same <see cref="TickFit"/> call and
    /// sharing one buffer between two independent measurements is the kind of coupling that
    /// produces a wrong number once and then never again reproducibly.</summary>
    private static readonly List<Graphic> HitGraphicScratch = new(64);

    /// <summary>Scratch for <see cref="TryMeasureChrome"/> — a separate buffer for the same reason
    /// <see cref="HitGraphicScratch"/> is separate from <c>GraphicScratch</c>: the chrome walk and
    /// the content walk run in the same <see cref="TickHitRect"/> call.</summary>
    private static readonly List<Graphic> ChromeGraphicScratch = new(8);

    /// <summary>Scratch for <c>RectTransform.GetWorldCorners</c> in the chrome walk.</summary>
    private static readonly Vector3[] ChromeCornerScratch = new Vector3[4];

    /// <summary>Once-per-session guard for the never-throw warning below.</summary>
    private static bool s_hitRectFaultLogged;

    /// <summary>
    /// THE PUBLIC ANSWER, and the one place the ray/poke plane may ask it: the rectangle a hit on
    /// <paramref name="canvas"/> must land inside, in that canvas RectTransform's own local space
    /// (uGUI px, the same space as <c>RectTransform.rect</c>).
    ///
    /// <para>Returns false for every canvas this module has not measured — mod-built surfaces, the
    /// board's docked canvases, anything registered by a path other than <c>Convert</c>. The
    /// callers then use <c>rect.rect</c> exactly as they did before, so a canvas without an entry
    /// behaves BYTE-IDENTICALLY to the shipped builds. It also returns false before the first
    /// measurement of a converted window has committed, for the same reason.</para>
    ///
    /// <para>Never throws, never allocates, never writes: a dictionary probe and a Unity-null
    /// check. Safe to call from an Update/LateUpdate hot path once per canvas per frame.</para>
    ///
    /// <para><b>THE CONTRACT, RESTATED, BECAUSE IT HAS MOVED TWICE SINCE THIS COMMENT WAS WRITTEN
    /// AND THE PARAGRAPHS ABOVE STILL DESCRIBE THE FIRST VERSION.</b> It was <c>Content ∪ Host</c>,
    /// i.e. never narrower than the frame. ModBuild 242 gave up the frame floor (the narrowing; see
    /// <see cref="TickHitRect"/>). ModBuild 440 puts a DIFFERENT floor under it: the rect always
    /// contains the mod-owned interactive chrome parked on the host (<see cref="TryMeasureChrome"/>),
    /// because the narrowing was measuring what the GAME draws and was therefore free to cut over
    /// the mod's own close button — which is exactly what it did on the combat log for three
    /// rounds. Today's contract in one line: <c>(Content ∪ Host, optionally narrowed to the padded
    /// content) ∪ ModChrome</c>, bounded by <see cref="MaxHitExpansion"/>.</para>
    /// </summary>
    internal static bool TryGetHitRect(Canvas? canvas, out Rect hit)
    {
        hit = default;
        if (canvas == null)
            return false;
        if (!HitRects.TryGetValue(canvas.GetInstanceID(), out HitRectEntry? entry) || entry == null)
            return false;
        if (entry.Canvas == null || !ReferenceEquals(entry.Canvas, canvas))
            return false;
        if (entry.Hit.width < 1f || entry.Hit.height < 1f)
            return false;
        hit = entry.Hit;
        return true;
    }

    /// <summary>
    /// WHAT THIS WINDOW ACTUALLY DRAWS, in its host RectTransform's own local space (uGUI px),
    /// beside the host rect it was measured against — for a caller that has to reason about the
    /// window's VISIBLE extent rather than about its frame.
    ///
    /// <para>THE ONE CALLER, AND WHY NEITHER EXISTING SURFACE COULD SERVE. The map room's arc
    /// allocator books every floated window an angular reservation, and it sized those from the
    /// host rect. For one window the two are not remotely the same: 'New Party display' has a
    /// 1988 px frame around a 328 px character column sitting at x −818 — 1648 px of empty
    /// transparent frame — so it reserved 88° of a 180° arc for something that occupies 14°.
    /// <see cref="TryGetHitRect"/> cannot express that: it returns <c>Content ∪ Host</c>, which by
    /// construction is never NARROWER than the frame. That is its contract and it is the correct
    /// contract for a ray test, which must never shrink below the window the player can see. And
    /// the cached <c>HitRectEntry.Content</c> cannot be read instead, because its measurement runs
    /// on a 30-frame staggered cadence and the moment the allocator needs it is earlier than that
    /// cadence reaches. From his ModBuild 233 log, in order: the pre-reveal re-place for that
    /// window logs at line 15939 and its FIRST hit-rect commit lands at 15943 — four lines later. A
    /// cached read would have returned nothing at exactly the moment it mattered.</para>
    ///
    /// <para>SO IT MEASURES, and it measures through <see cref="TryMeasureDrawnUnion"/> — the same
    /// walk, the same per-graphic verdict, the same mask and alpha rules as the content fit and the
    /// hit rect. A graphic this call counts is a graphic those two would have counted; there is no
    /// second opinion anywhere in this file about what "visible" means. What it does NOT do is
    /// write: no entry is created, no cadence is disturbed, no rect is committed, and the hit rect
    /// the laser tests against is not touched by this call in any way.</para>
    ///
    /// <para>COST: one subtree walk (~85 µs for the 251-transform equipment window, per the
    /// ModBuild 194 flatness line). The caller is event-gated — spawn and the one pre-reveal
    /// re-place, never per frame — so this is a handful of walks per session.</para>
    ///
    /// <para>False when nothing is measurable at all: the window is still hidden behind the reveal
    /// gate, or mid fade-in, and no graphic passes the visibility test. The caller then falls back
    /// to the host rect, which is the behaviour of every build before ModBuild 234. That is the
    /// NORMAL answer at spawn time, and it is the reason the allocator asks again at the re-place.</para>
    ///
    /// <para>Never throws: this is reached from the placement path, and an unguarded exception
    /// there starves VR input.</para>
    /// </summary>
    internal static bool TryMeasureDrawnContent(ConvertedPanel? panel, out Rect content,
        out Rect host, out int contributors)
    {
        content = default;
        host = default;
        contributors = 0;
        try
        {
            if (panel == null || panel.HostRect == null || panel.Target == null)
                return false;
            host = panel.HostRect.rect;
            if (host.width < 1f || host.height < 1f)
                return false; // degenerate host (not laid out yet) — nothing to measure against
            RectTransform root = ResolveFitRoot(panel, panel.FitContentRoot);
            if (!TryMeasureDrawnUnion(panel, root, host, out content, out contributors, out _, out _))
                return false;
            return content.width >= 1f && content.height >= 1f;
        }
        catch (System.Exception e)
        {
            if (!s_hitRectFaultLogged)
            {
                s_hitRectFaultLogged = true;
                VRLog.Warn("WorldUI", "DRAWN CONTENT: the visible-extent measurement threw "
                                      + $"{e.GetType().Name} ('{e.Message}') and was swallowed so "
                                      + "the placement path keeps running. WHAT THIS MEANS: the map "
                                      + "room's arc reservations fall back to the HOST RECT, which "
                                      + "is the behaviour of every build before ModBuild 234 — a "
                                      + "window with a wide transparent frame books more arc than "
                                      + "it draws and the room packs more tightly than it needs to. "
                                      + "Nothing is mis-placed and nothing becomes unclickable. "
                                      + "Reported once.");
            }
            return false;
        }
    }

    /// <summary>
    /// Re-measure this panel's hit rect if its check is due. Called FIRST from
    /// <see cref="TickFit"/> — before every one of that method's early returns — because the hit
    /// rect must keep tracking a window whose SIZING fit has finished forever (a one-shot menu
    /// locks <c>FitEnabled</c> false, and the equipment window's content grows long after its
    /// pre-reveal first fit committed).
    ///
    /// <para>CADENCE: <see cref="FitCheckIntervalFrames"/> (30 frames, ~0.33 s at 90 Hz), staggered
    /// per panel by instance ID so several open windows never measure on the same frame. That is
    /// the same rhythm the growth re-fit already runs at, and the same subtree walk (~85 us for the
    /// 251-transform equipment window per the ModBuild 194 flatness line), so the steady-state cost
    /// of this whole section is one extra content walk per open window per 30 frames.</para>
    ///
    /// <para>A SETTLED WINDOW WRITES NOTHING: the measurement is compared against the committed
    /// rect with the fit's own <see cref="FitChangeFraction"/> tolerance, and a sub-tolerance result
    /// is discarded — no field write, no log line, no allocation past the walk. Only a material
    /// change commits, and every commit logs exactly once.</para>
    ///
    /// <para>NEVER THROWS. This runs inside the WorldUI LateUpdate chain; an escaping exception
    /// there would take the rest of the chain — and with it the panel treatments the whole UI
    /// depends on — down with it. Any fault degrades to "no entry", i.e. the pre-existing host-rect
    /// behaviour, and warns once.</para>
    /// </summary>
    private static void TickHitRect(ConvertedPanel panel)
    {
        try
        {
            if (panel == null || panel.HostCanvas == null || panel.HostRect == null
                || panel.Target == null || panel.HostGo == null)
                return;

            int id = panel.HostCanvas.GetInstanceID();
            if (!HitRects.TryGetValue(id, out HitRectEntry? entry) || entry == null)
            {
                HitRects[id] = entry = new HitRectEntry
                {
                    Canvas = panel.HostCanvas,
                    // Stagger: the walks of N open windows spread across the interval instead of
                    // landing on one frame together.
                    NextCheckFrame = Time.frameCount + (id & 0x7FFFFFFF) % FitCheckIntervalFrames,
                };
                PruneHitRects();
            }
            // ModBuild 243 — THE CHEAP EDGE, so the laser rect moves on the same frame as the brass
            // bar and the close X rather than up to FitCheckIntervalFrames later. This walk is the
            // expensive one (every Graphic under the window); PanelInkBounds.ActiveSetSignature is
            // the cheap one (the active set to depth two, order sixty nodes), and a change in it is
            // exactly "a sub-view opened or closed". It cannot make the rect WRONG — it only decides
            // WHEN the same measurement is taken — and it is the reason the three pieces of chrome
            // now agree within a frame instead of within a third of a second.
            int sig = PanelInkBounds.ActiveSetSignature(panel, out _);
            if (!entry.SignatureValid || sig != entry.Signature)
            {
                entry.Signature = sig;
                entry.SignatureValid = true;
                entry.SignatureEdges++;
                entry.NextCheckFrame = Time.frameCount;
            }
            if (Time.frameCount < entry.NextCheckFrame)
                return;
            entry.NextCheckFrame = Time.frameCount + FitCheckIntervalFrames;

            Rect host = panel.HostRect.rect;
            if (host.width < 1f || host.height < 1f)
                return; // degenerate host (not laid out yet) — keep whatever we had

            RectTransform root = ResolveFitRoot(panel, panel.FitContentRoot);
            bool measured = TryMeasureDrawnUnion(panel, root, host, out Rect content,
                out int contributors, out string outsideOwner, out float outsideBy);

            // Nothing measurable (mid fade-in, window hidden behind the reveal gate): the hit rect
            // is the host rect, which is exactly the shipped behaviour. Never a stale grown rect —
            // that would be a laser hitting empty space.
            Rect union = measured ? Union(host, content) : host;

            // ---- ModBuild 242: THE FRAME IS NO LONGER A FLOOR --------------------------------------
            //
            // User report, verbatim (2026-08-24): "Aktuell haben wir die Situation, dass das 'x' weit
            // rechts, der Balken klein und zwischen dem linken Teil und dem X unsichtbare Collider für
            // den Laser ist. Wenn kleineres Fenster, dann voll mit verschobenem X und ohne unsichtbaren
            // Collider."
            //
            // THIS IS THE SURFACE HE IS DESCRIBING, and it is not a collider. RayUguiDriver.TryIntersect
            // wins its pick on the four world corners of THIS rectangle alone, before any graphic is
            // raycast — so a beam crossing empty transparent frame ends on the window, draws its reticle
            // there and shadows everything behind it. The ModBuild 241 log measures the cost on the
            // options window with the VR tab open: DRAWN CONTENT 1301x1827 px at (-132,374) inside a
            // 1552x1080 frame, i.e. a live rectangle reaching x=776 for a picture that stops at x=518.
            //
            // THE CONTRACT THIS CHANGES, deliberately and in one place. TryGetHitRect's own comment
            // says the rect "by construction is never NARROWER than the frame … the correct contract
            // for a ray test, which must never shrink below the window the player can see". The second
            // half of that sentence is the real rule and it is preserved exactly: `content` IS the
            // window the player can see, measured with this file's own visibility verdict. What is
            // given up is the FIRST half — the frame, which for these windows is transparent margin.
            //
            // AND IT IS GRANTED THE WAY AN INK RELEASE IS GRANTED, because shrinking an interactive
            // area is the one direction that can kill input on a button that is really there:
            //   * only when the padded content is more than HitRectShrinkDeadBandPx inside the frame;
            //   * only after HitRectShrinkRuns consecutive measurements agree, carrying the OUTERMOST
            //     rect of the run forward, so a wobbling run commits its most generous member;
            //   * never on a sample that could not measure — an unmeasurable window goes straight back
            //     to the host rect and the run resets, which is the pre-242 behaviour;
            //   * growth still commits on sight, and any growth cancels a run in progress.
            Rect live = union;
            if (measured)
            {
                Rect padded = Rect.MinMaxRect(content.xMin - HitRectContentPadPx,
                                              content.yMin - HitRectContentPadPx,
                                              content.xMax + HitRectContentPadPx,
                                              content.yMax + HitRectContentPadPx);
                Rect narrowed = Rect.MinMaxRect(Mathf.Max(union.xMin, padded.xMin),
                                                Mathf.Max(union.yMin, padded.yMin),
                                                Mathf.Min(union.xMax, padded.xMax),
                                                Mathf.Min(union.yMax, padded.yMax));
                bool worthIt = narrowed.width > 1f && narrowed.height > 1f
                               && (narrowed.xMin > union.xMin + HitRectShrinkDeadBandPx
                                   || narrowed.xMax < union.xMax - HitRectShrinkDeadBandPx
                                   || narrowed.yMin > union.yMin + HitRectShrinkDeadBandPx
                                   || narrowed.yMax < union.yMax - HitRectShrinkDeadBandPx);
                // ModBuild 243 — THE ONE DIRECTION THAT CAN KILL INPUT IS THE STALE NARROW RECT, NOT
                // THE FRESH ONE, and that is why only GROWTH is made faster here.
                //
                // User report (2026-08-24) is about the grab bar and the X, not about this rectangle;
                // it is touched at all because ModBuild 242 let this rect shrink BELOW the frame, and
                // that made the 30-frame cadence load-bearing for input. Press a tab in the options
                // window and the drawn content jumps from 399 px wide to ~1500 px — the ModBuild 242
                // log measures exactly that (HIT RECT commits #1 -> #3, DRAWN CONTENT 399x2158 ->
                // 1495x2464) — and until the next walk lands, up to FitCheckIntervalFrames later, the
                // laser's whole verdict is the OLD narrow rectangle. That is up to 336 ms at 90 Hz of
                // dead beam over buttons that are already painted, and the 64 px pad cannot cover a
                // 1000 px jump. Re-checking on the next frame after a walk that GREW the rect closes
                // that window to ~11 ms.
                //
                // THE SHRINK RUN IS DELIBERATELY LEFT AT THE 30-FRAME CADENCE. Its three agreeing
                // measurements are the only thing standing between a bad measurement and a button
                // that cannot be clicked, and a rect that stays too WIDE for an extra second costs
                // nothing but a beam landing on transparent frame. Speeding up the safe direction and
                // leaving the dangerous one is the whole of this change; a future round that wants
                // the shrink faster must first say what replaces the run.
                if (!worthIt)
                {
                    entry.ShrinkValid = false;
                    entry.ShrinkRun = 0;
                }
                else if (entry.ShrinkValid
                         && Mathf.Abs(entry.ShrinkCandidate.xMin - narrowed.xMin) <= HitRectShrinkDeadBandPx
                         && Mathf.Abs(entry.ShrinkCandidate.xMax - narrowed.xMax) <= HitRectShrinkDeadBandPx
                         && Mathf.Abs(entry.ShrinkCandidate.yMin - narrowed.yMin) <= HitRectShrinkDeadBandPx
                         && Mathf.Abs(entry.ShrinkCandidate.yMax - narrowed.yMax) <= HitRectShrinkDeadBandPx)
                {
                    entry.ShrinkCandidate = Rect.MinMaxRect(
                        Mathf.Min(entry.ShrinkCandidate.xMin, narrowed.xMin),
                        Mathf.Min(entry.ShrinkCandidate.yMin, narrowed.yMin),
                        Mathf.Max(entry.ShrinkCandidate.xMax, narrowed.xMax),
                        Mathf.Max(entry.ShrinkCandidate.yMax, narrowed.yMax));
                    entry.ShrinkRun++;
                    if (entry.ShrinkRun >= HitRectShrinkRuns)
                    {
                        live = entry.ShrinkCandidate;
                        // ModBuild 243: count the TRANSITION, not every sample taken while the rect
                        // is already narrowed. The ModBuild 242 log reads 'NARROWINGS COMMITTED over
                        // this window's life: 332' on an options window whose hit rect never changed
                        // between two consecutive lines — 328 of those were this counter ticking once
                        // per walk on a settled narrowing, and a falsifier that cannot tell "it
                        // narrowed 332 times" from "it has been narrow for 332 walks" is not one.
                        if (entry.ShrinkRun == HitRectShrinkRuns)
                            entry.Shrinks++;
                    }
                }
                else
                {
                    entry.ShrinkCandidate = narrowed;
                    entry.ShrinkValid = true;
                    entry.ShrinkRun = 1;
                }
            }
            else
            {
                entry.ShrinkValid = false;
                entry.ShrinkRun = 0;
            }
            union = live;

            // ---- ModBuild 440: THE NARROWING MAY NEVER CUT OVER THE WINDOW'S OWN CHROME ---------
            //
            // User report, verbatim (2026-09-05), and he had already found the cause himself: "Der
            // X-Button beim Kampflog hat immer noch Probleme und ich denke ich hab herausgefunden
            // wieso: Wenn man den Kampflog berührt oder drüberhovered wird er nach oben hinweg
            // größer — scheinbar auch der Bereich in dem der Laser collidet, dann kann ich das X gut
            // drücken — wenn der Kampflog wieder kleiner geworden ist geht der Laser durch das X
            // hindurch. Der ganze Fensterbereich vom Kampflog kann dauerhaft colliden um das Problem
            // zu lösen."
            //
            // THE ARITHMETIC, OUT OF HIS OWN ModBuild 439 LOG, and it is this block that produced it.
            // 'GloomhavenVR.Panel_CombatLog' has a 569x291 px host rect (y -146..146). The close X is
            // seated on the FRAME path (CombatLogSurface.SyncCloseX), so its plate's top-right corner
            // is (xMax-7, yMax-7) and, with pivot (1,1), the plate occupies y 105..139 — INSIDE the
            // frame by construction. The committed hit rect alternates between two states in that log:
            //   line 10414  DRAWN CONTENT 569x291 -> HIT RECT 569x291 [y -146..146]   (hovered)
            //   line 10472  DRAWN CONTENT 569x114 -> HIT RECT 569x178 [y -146..32]    (at rest)
            // The second one is this narrowing, and its top edge is 73 px BELOW the bottom of the
            // plate. TryIntersect tests that rectangle and nothing else, so at rest the beam does not
            // stop on this window at all — "geht der Laser durch das X hindurch", exactly.
            //
            // WHAT MAKES THE TWO STATES ALTERNATE IS THE GAME, NOT THE MOD: CombatLogHandler is an
            // IPointerEnterHandler whose OnPointerEnter calls ExpandLog() and whose OnPointerExit
            // calls MinimizeLog(), tweening combatLogWindow.anchorMax.y between minimizeToPercent
            // (0.5) and 1 over expandAnimationTime. The window really is half-height at rest and
            // full-height while pointed at, the drawn content really does halve, and the interactive
            // area faithfully followed it down over the mod's own button. His observation is the
            // mechanism.
            //
            // THE FIX IS A FLOOR, NOT A LARGER INSET. An inset big enough to swallow the plate would
            // be a magic number that breaks the moment the plate's size or a window's margin moves,
            // and it would say nothing about the next piece of chrome parked against a window edge.
            // The rule instead is the one his remedy generalises to: the rectangle a pointer is
            // tested against is the window's own extent PLUS the mod-owned chrome seated on it. The
            // content walk cannot express that (TryMeasureChrome's doc says why), so the chrome is
            // measured separately and unioned in AFTER the narrowing — which is the only place it can
            // matter, because every piece of chrome is already inside the host rect the union starts
            // from.
            //
            // WHAT THIS DOES NOT DO. It never reaches past the host rect (the chrome is seated inside
            // it, and MaxHitExpansion below still bounds anything that is not), so it cannot extend
            // over a neighbouring panel and cannot make this window shadow anything it did not
            // already shadow before ModBuild 242. It takes back only the strip between the narrowed
            // edge and the mod's own button. The narrowing itself is untouched and still bites on
            // every edge no chrome stands on — the options window, which is the window his ModBuild
            // 242 "unsichtbare Collider" report was about, has its X on the INK and is unaffected.
            if (TryMeasureChrome(panel, out Rect chrome, out int chromeParts, out string chromeOwner))
            {
                entry.Chrome = chrome;
                entry.ChromeValid = true;
                entry.ChromeParts = chromeParts;
                entry.ChromeOwner = chromeOwner;
                Rect withChrome = Union(union, chrome);
                entry.ChromeHeld = withChrome.xMin < union.xMin - 0.5f
                                   || withChrome.xMax > union.xMax + 0.5f
                                   || withChrome.yMin < union.yMin - 0.5f
                                   || withChrome.yMax > union.yMax + 0.5f;
                union = withChrome;
            }
            else
            {
                entry.ChromeValid = false;
                entry.ChromeParts = 0;
                entry.ChromeOwner = string.Empty;
                entry.ChromeHeld = false;
            }

            // THE SAFETY BOUND (see MaxHitExpansion). Host-centred, edge by edge, so a clamp gives
            // away as much of the overspill as the bound allows instead of dropping all of it.
            float padX = host.width * (MaxHitExpansion - 1f) * 0.5f;
            float padY = host.height * (MaxHitExpansion - 1f) * 0.5f;
            float xMin = Mathf.Max(union.xMin, host.xMin - padX);
            float xMax = Mathf.Min(union.xMax, host.xMax + padX);
            float yMin = Mathf.Max(union.yMin, host.yMin - padY);
            float yMax = Mathf.Min(union.yMax, host.yMax + padY);
            bool clamped = xMin > union.xMin + 0.5f || xMax < union.xMax - 0.5f
                           || yMin > union.yMin + 0.5f || yMax < union.yMax - 0.5f;
            Rect hit = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            if (hit.width < 1f || hit.height < 1f)
                hit = host;

            // ModBuild 440 — THE FALSIFIER FOR THE CHROME FLOOR, AND IT IS EVALUATED BEFORE THE
            // DIRTY CHECK ON PURPOSE. A rect that has SETTLED with the mod's own button outside it
            // is the exact failure this round exists to catch, and a check placed after the early
            // return below would be the one thing a settled defect can never print. It is gated on
            // the VERDICT and not on the rect, so a steady state costs one comparison and one line
            // for its whole life; a state that keeps flipping reads AS flipping.
            int chromeVerdict;
            if (!entry.ChromeValid)
                chromeVerdict = 1;
            else if (hit.xMin > entry.Chrome.xMin + 0.5f || hit.xMax < entry.Chrome.xMax - 0.5f
                     || hit.yMin > entry.Chrome.yMin + 0.5f || hit.yMax < entry.Chrome.yMax - 0.5f)
                chromeVerdict = 4;
            else
                chromeVerdict = entry.ChromeHeld ? 3 : 2;
            if (chromeVerdict != entry.ChromeVerdict)
            {
                entry.ChromeVerdict = chromeVerdict;
                if (chromeVerdict == 3)
                    entry.ChromeHolds++;
                if (chromeVerdict != 1)
                {
                    // HW-VERIFY
                    VRLog.Note("WorldUI",
                        $"HIT RECT CHROME FLOOR '{panel.HostGo.name}': {entry.ChromeParts} mod-owned "
                        + $"raycast target(s) on this host, first '{entry.ChromeOwner}', spanning "
                        + $"x {entry.Chrome.xMin:F0}..{entry.Chrome.xMax:F0} and "
                        + $"y {entry.Chrome.yMin:F0}..{entry.Chrome.yMax:F0} px; the committed hit "
                        + $"rect is x {hit.xMin:F0}..{hit.xMax:F0} and y {hit.yMin:F0}..{hit.yMax:F0} "
                        + $"px (host x {host.xMin:F0}..{host.xMax:F0}, y {host.yMin:F0}..{host.yMax:F0}); "
                        + $"floor held open {entry.ChromeHolds} time(s) this window's life. VERDICT: "
                        + (chromeVerdict == 4
                            ? "THE CHROME IS OUTSIDE THE COMMITTED RECT — RayUguiDriver.TryIntersect "
                              + "tests that rect and nothing else, so the beam passes straight "
                              + "through this window's own close X and does not even stop on the "
                              + "panel. THIS IS THE ModBuild 439 DEFECT STILL LIVE. Only two things "
                              + "can produce it now: the MaxHitExpansion clamp cut the union back "
                              + "(the HIT RECT line for this window says CLAMPED), or the chrome is "
                              + "seated outside the host rect, which PlaceAgainstInk clamps against "
                              + "and therefore should be impossible"
                            : chromeVerdict == 3
                                ? "the floor is DOING WORK — the drawn-content narrowing had pulled "
                                  + "the interactive area in over this chrome and the floor pushed "
                                  + "that edge back out. This is the reading the combat log is "
                                  + "expected to produce at rest, where the game's own "
                                  + "CombatLogHandler.MinimizeLog halves the drawn content while the "
                                  + "close X stays at the frame's corner"
                                : "the chrome is inside the committed rect on its own and the floor "
                                  + "changed nothing — the reading for every window whose X is "
                                  + "seated against the INK, and the reading the combat log gives "
                                  + "while the player is hovering it and the game has expanded it")
                        + ". A window that prints this line ONCE and never again has settled in that "
                        + "state; silence after it is the instrument agreeing with itself, not the "
                        + "instrument stopping.");
                }
            }

            // Dirty check — the same relative tolerance the content fit uses for its own no-op.
            float tolX = Mathf.Max(Mathf.Max(host.width, hit.width) * FitChangeFraction, 1f);
            float tolY = Mathf.Max(Mathf.Max(host.height, hit.height) * FitChangeFraction, 1f);
            if (entry.Commits > 0
                && Mathf.Abs(hit.xMin - entry.Hit.xMin) <= tolX
                && Mathf.Abs(hit.xMax - entry.Hit.xMax) <= tolX
                && Mathf.Abs(hit.yMin - entry.Hit.yMin) <= tolY
                && Mathf.Abs(hit.yMax - entry.Hit.yMax) <= tolY)
                return; // settled — nothing written, nothing logged

            // ModBuild 243 — A WALK THAT GREW THE RECT ASKS AGAIN ON THE NEXT FRAME. See the comment
            // on the shrink run above for why growth and only growth. Bounded by construction: the
            // dirty check five lines up is what got us here, so the fast re-check stops the moment
            // the rect settles, and a settled rect goes straight back to FitCheckIntervalFrames.
            bool grewThisWalk = entry.Commits > 0
                           && (hit.xMin < entry.Hit.xMin - 0.5f || hit.xMax > entry.Hit.xMax + 0.5f
                               || hit.yMin < entry.Hit.yMin - 0.5f || hit.yMax > entry.Hit.yMax + 0.5f);
            if (grewThisWalk)
                entry.NextCheckFrame = Time.frameCount + 1;

            entry.Hit = hit;
            entry.Host = host;
            entry.Content = measured ? content : host;
            entry.Clamped = clamped;
            entry.Contributors = contributors;
            entry.OutsideOwner = outsideOwner;
            entry.OutsideBy = outsideBy;
            entry.Commits++;
            // The rect above is already live for the ray/poke plane. Only the LINE is rate-limited,
            // and never across the grown/not-grown transition (see HitRectEntry.LoggedGrown).
            bool grown = hit.width > host.width + 0.5f || hit.height > host.height + 0.5f;
            if (entry.Commits > 1 && grown == entry.LoggedGrown
                && Time.unscaledTime < entry.NextLogAt)
            {
                entry.SuppressedCommits++;
                return;
            }
            entry.NextLogAt = Time.unscaledTime + HitRectLogIntervalSeconds;
            entry.LoggedGrown = grown;
            LogHitRect(panel, entry, measured, root);
            entry.SuppressedCommits = 0;
        }
        catch (System.Exception e)
        {
            if (!s_hitRectFaultLogged)
            {
                s_hitRectFaultLogged = true;
                VRLog.Warn("WorldUI", "HIT RECT: the interactive-area measurement threw " +
                                      $"{e.GetType().Name} ('{e.Message}') and was swallowed so the " +
                                      "WorldUI LateUpdate chain keeps running. WHAT THIS MEANS FOR " +
                                      "INPUT: nothing regressed — every canvas without a committed " +
                                      "hit rect falls back to its own RectTransform rect, which is " +
                                      "the behaviour of every build before this one. Windows that " +
                                      "draw outside their own frame simply stay unclickable there " +
                                      "until this is fixed. Logged once per session.");
            }
        }
    }

    /// <summary>Drop entries whose host canvas is gone. Runs only when a NEW entry is created (a
    /// window opened), which is the only moment the dictionary can grow — so a session that opens
    /// and closes windows all evening never accumulates.</summary>
    private static void PruneHitRects()
    {
        if (HitRects.Count < 2)
            return;
        List<int>? dead = null;
        foreach (KeyValuePair<int, HitRectEntry> pair in HitRects)
        {
            if (pair.Value == null || pair.Value.Canvas == null)
                (dead ??= new List<int>(4)).Add(pair.Key);
        }
        if (dead == null)
            return;
        for (int i = 0; i < dead.Count; i++)
            HitRects.Remove(dead[i]);
    }

    /// <summary>
    /// The union of everything <paramref name="root"/>'s subtree measurably DRAWS, in host-local
    /// uGUI px — the content fit's measurement with its one disqualifying difference: the target
    /// FRAME CLAMP is not applied.
    ///
    /// <para>WHY THAT DIFFERENCE IS THE WHOLE POINT. <see cref="TryMeasureContent"/> ends every
    /// pass with <c>min = Vector2.Max(min, frameMin); max = Vector2.Min(max, frameMax);</c> against
    /// the conversion target's own rect, because a window must not be SIZED by something that
    /// escaped its frame. This measurement answers the opposite question — "what can the player
    /// see, wherever it is?" — and the ModBuild 194 log shows two windows for which the two answers
    /// differ materially ('Character Items Equipment Content' 903 vs 532 px wide, 'UI Quest Popup'
    /// reporting "[frame-clamped]" on every single fit while its supersample frame grows by
    /// 154x83). Everything else is shared code on purpose: the per-graphic verdict is
    /// <see cref="TryGetVisibleHostRect"/> itself, so a graphic this walk counts is a graphic the
    /// fit would have counted, mask clipping and alpha floor included, and the two can never
    /// disagree about what "visible" means.</para>
    ///
    /// <para>Mod-owned cue art is skipped for the same reason the fit skips it (it breathes, and it
    /// is presentation, not content). <paramref name="outsideOwner"/>/<paramref name="outsideBy"/>
    /// name the single graphic that reaches furthest beyond <paramref name="host"/>, so the log can
    /// say WHICH element made the window grow rather than only that it did.</para>
    ///
    /// <para>False when nothing was measurable at all (window mid-fade, still hidden); the caller
    /// then falls back to the host rect rather than holding a stale grown one.</para>
    /// </summary>
    private static bool TryMeasureDrawnUnion(ConvertedPanel panel, RectTransform root, Rect host,
        out Rect union, out int contributors, out string outsideOwner, out float outsideBy)
    {
        union = default;
        contributors = 0;
        outsideOwner = string.Empty;
        outsideBy = 0f;

        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);

        // Same per-pass contract as TryMeasureContent: both memos are keyed by live Transforms and
        // must not survive into a walk taken at a different moment.
        ClipperMemo.Clear();
        AuthoredOffsetMemo.Clear();
        HitGraphicScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, HitGraphicScratch);
        for (int i = 0; i < HitGraphicScratch.Count; i++)
        {
            Graphic g = HitGraphicScratch[i];
            if (g == null)
                continue;
            if (g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                continue; // mod cue art is presentation, not content (see TryMeasureContent)
            if (!TryGetVisibleHostRect(panel, g, out Vector2 gMin, out Vector2 gMax))
                continue;

            min = Vector2.Min(min, gMin);
            max = Vector2.Max(max, gMax);
            contributors++;

            float outside = Mathf.Max(
                Mathf.Max(host.xMin - gMin.x, gMax.x - host.xMax),
                Mathf.Max(host.yMin - gMin.y, gMax.y - host.yMax));
            if (outside > outsideBy)
            {
                outsideBy = outside;
                Transform? parent = g.transform.parent;
                outsideOwner = parent != null ? parent.name + "/" + g.name : g.name;
            }
        }
        HitGraphicScratch.Clear();

        if (contributors == 0)
            return false;
        union = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return union.width >= 1f && union.height >= 1f;
    }

    /// <summary>Smallest rect containing both.</summary>
    private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(
        Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
        Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    /// <summary>
    /// <b>THE WINDOW'S OWN CHROME, WHICH THE CONTENT WALK CANNOT SEE BY CONSTRUCTION</b> — the
    /// union of every raycast target under a MOD-OWNED subtree parked directly on
    /// <paramref name="panel"/>'s host, in the host rect's own local px.
    ///
    /// <para><b>WHY A SECOND WALK EXISTS AT ALL.</b> <see cref="TryMeasureDrawnUnion"/> answers
    /// "what does the GAME draw": it starts at <c>ResolveFitRoot</c> — the conversion target, i.e.
    /// the game window — and it skips every graphic whose name carries the <c>GloomhavenVR.</c>
    /// prefix, because mod cue art breathes and must never size a window. Both of those rules are
    /// right and neither is being changed. Their consequence is that the mod's own close X is
    /// invisible to the one measurement that decides where the laser may land, and no amount of
    /// tuning the content walk can fix that: the plate is not content and it is not the game's.</para>
    ///
    /// <para><b>WHAT COUNTS AS CHROME, and it is the ownership test the host-destroy guard already
    /// uses</b> (<c>FindGameContent</c>): a direct child of the host carrying a SEALED
    /// <see cref="ModOwnedContent"/> marker, or — the documented fallback for subtrees parked from
    /// files that still name rather than mark — a name starting with <c>ModOwnedPrefix</c>. A
    /// TRANSPARENT dock (<c>MayHoldGameContent</c>) is deliberately NOT chrome: it holds the game's
    /// own window, which the content walk already measures, and treating it as chrome would union
    /// the game's tree twice under a name that promises the opposite.</para>
    ///
    /// <para><b>AND ONLY WHAT A POINTER CAN ACTUALLY HIT.</b> The union takes graphics with
    /// <c>raycastTarget</c> true and <c>isActiveAndEnabled</c> — never the decorative half. That is
    /// what keeps the supersample display quad (<c>GloomhavenVR.PanelSS_*</c>, a full-panel
    /// <c>RawImage</c> with <c>raycastTarget=false</c>) and every breathing focus/selection ring out
    /// of the rectangle the beam is judged against. The two shapes that DO qualify today are the
    /// close X's plate + <c>HitPlane</c> and a transient window's full-host dismiss catcher, and
    /// both of them are surfaces a press is SUPPOSED to land on.</para>
    ///
    /// <para>COST: <c>host.childCount</c> is a handful, only the mod-owned ones are descended, and
    /// those subtrees are 2-4 nodes. Runs on the same 30-frame staggered cadence as the content
    /// walk it sits beside.</para>
    ///
    /// <para>False when this host carries no interactive chrome at all, which is most windows — the
    /// caller then commits exactly the rect it would have committed before this method existed.</para>
    /// </summary>
    private static bool TryMeasureChrome(ConvertedPanel panel, out Rect chrome, out int parts,
        out string owner)
    {
        chrome = default;
        parts = 0;
        owner = string.Empty;
        RectTransform? host = panel.HostRect;
        if (host == null)
            return false;

        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);
        int children = host.childCount;
        for (int i = 0; i < children; i++)
        {
            Transform child = host.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;
            // The ownership question, asked exactly as FindGameContent asks it: marker first, name
            // second. A transparent dock is skipped — see the doc above.
            if (child.TryGetComponent(out ModOwnedContent owned))
            {
                if (owned.MayHoldGameContent)
                    continue;
            }
            else if (!child.name.StartsWith(ModOwnedPrefix, System.StringComparison.Ordinal))
            {
                continue; // the game's own window subtree; the content walk owns it
            }

            ChromeGraphicScratch.Clear();
            child.GetComponentsInChildren(includeInactive: false, ChromeGraphicScratch);
            for (int g = 0; g < ChromeGraphicScratch.Count; g++)
            {
                Graphic graphic = ChromeGraphicScratch[g];
                if (graphic == null || !graphic.raycastTarget || !graphic.isActiveAndEnabled)
                    continue;
                RectTransform r = graphic.rectTransform;
                if (r == null)
                    continue;
                // World corners -> host-local, which is the space every other rect in this file is
                // in. Going through world space rather than assuming the child sits at the host's
                // own scale and rotation is what makes this correct for a piece of chrome that
                // rides its own nested canvas, as the close X's HitPlane does.
                r.GetWorldCorners(ChromeCornerScratch);
                for (int c = 0; c < 4; c++)
                {
                    Vector3 local = host.InverseTransformPoint(ChromeCornerScratch[c]);
                    var corner = new Vector2(local.x, local.y);
                    min = Vector2.Min(min, corner);
                    max = Vector2.Max(max, corner);
                }
                parts++;
                if (owner.Length == 0)
                    owner = child.name + "/" + graphic.name;
            }
        }
        ChromeGraphicScratch.Clear();

        if (parts == 0)
            return false;
        chrome = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return chrome.width >= 1f && chrome.height >= 1f;
    }

    /// <summary>
    /// World units per REAL (tracking-space) metre — the diorama scale on the rig root. Returns 0
    /// when there is no rig yet, and the caller then prints "rig scale unknown" instead of a
    /// millimetre figure derived from a guess: mixing world units and metres without naming which
    /// is how this project once drew a laser 0.15 mm wide and called it correct.
    /// </summary>
    private static float RigUnitsPerMetre()
    {
        Transform? rigRoot = Rig.VRRigDriver.RigRoot;
        if (rigRoot != null)
        {
            float s = rigRoot.lossyScale.x;
            if (s > 1e-4f)
                return s;
        }
        float baseScale = Rig.VRRigDriver.BaseWorldScale;
        return baseScale > 1e-4f ? baseScale : 0f;
    }

    /// <summary>
    /// One line per COMMITTED hit rect (first measurement, and every material change afterwards).
    /// It carries the full derivation — host rect, measured drawn content, which of the two is
    /// larger, the resulting hit rect — in uGUI px AND in real millimetres at the live rig scale,
    /// plus the element that reaches furthest outside the frame.
    /// </summary>
    private static void LogHitRect(ConvertedPanel panel, HitRectEntry entry, bool measured,
        RectTransform root)
    {
        Rect host = entry.Host;
        Rect hit = entry.Hit;
        Rect content = entry.Content;
        float growX = hit.width - host.width;
        float growY = hit.height - host.height;
        bool grown = growX > 0.5f || growY > 0.5f;

        // uGUI px -> world units is the host's own lossy scale (this is what GetWorldCorners
        // applies); world units -> real metres is the rig scale. Named separately on purpose.
        float pxToWorld = panel.HostRect != null ? panel.HostRect.lossyScale.x : 0f;
        float unitsPerMetre = RigUnitsPerMetre();
        string realSize;
        if (pxToWorld > 1e-6f && unitsPerMetre > 1e-4f)
        {
            float mmPerPx = pxToWorld / unitsPerMetre * 1000f;
            realSize = $"REAL SIZE at the live rig scale ({unitsPerMetre:F1} world units per "
                       + $"tracking metre, host scale {pxToWorld:F4} world units per uGUI px = "
                       + $"{mmPerPx:F3} mm per px): the window's own frame is "
                       + $"{host.width * mmPerPx:F0}x{host.height * mmPerPx:F0} mm and the "
                       + $"interactive area is {hit.width * mmPerPx:F0}x{hit.height * mmPerPx:F0} mm"
                       + (grown ? $", i.e. {growX * mmPerPx:F0}x{growY * mmPerPx:F0} mm wider/taller "
                                  + "than the frame" : " — identical");
        }
        else
        {
            realSize = "REAL SIZE: not stated — rig scale unknown "
                       + $"(rig units/metre {unitsPerMetre:F1}, host px scale {pxToWorld:F5}); a "
                       + "millimetre figure derived from a guessed scale would be worse than none";
        }

        // ModBuild 242 — a THIRD state exists now and it must not read as the second. Before this
        // round the rect could only ever equal the host or exceed it, so "content fits inside the
        // frame" and "the hit rect IS the host rect" were the same sentence. They no longer are: the
        // shrink run can pull the rect INSIDE the frame, and if that case kept printing the old text
        // the next log could not tell "the narrowing never fired" from "the narrowing is off".
        // entry.Shrinks is the did-this-ever-fire counter and it is printed even when this particular
        // commit did not narrow, because a counter nobody prints is a counter nobody can falsify.
        float shrinkL = hit.xMin - host.xMin;
        float shrinkR = host.xMax - hit.xMax;
        float shrinkD = hit.yMin - host.yMin;
        float shrinkU = host.yMax - hit.yMax;
        bool narrowed = shrinkL > 0.5f || shrinkR > 0.5f || shrinkD > 0.5f || shrinkU > 0.5f;

        string verdict = !measured
            ? "NOTHING MEASURABLE (window still hidden or fading in) — the hit rect IS the host "
              + "rect, exactly as in every build before this one"
            : grown
                ? $"GROWN: the window draws {growX:F0}x{growY:F0} px outside its own frame and the "
                  + "laser/poke plane now reaches that far"
                : narrowed
                    ? $"NARROWED: the laser/poke plane has been pulled INSIDE the frame by "
                      + $"L {shrinkL:F0} / R {shrinkR:F0} / D {shrinkD:F0} / U {shrinkU:F0} px, "
                      + $"after {HitRectShrinkRuns} agreeing measurements and a {HitRectContentPadPx:F0} px "
                      + "pad around the drawn content. THAT STRIP OF EMPTY FRAME NO LONGER CATCHES THE "
                      + "BEAM — which is the whole of the user's \"unsichtbare Collider\" report, and it "
                      + "is also the one direction that can KILL INPUT on a button that is really "
                      + "there, so if something in this window stopped reacting, this line is the first "
                      + "suspect and the pad is the dial"
                    : "content fits inside the frame and the narrowing did not fire — the hit rect IS "
                      + "the host rect, so the ray/poke geometry is bit-identical to the shipped builds";

        VRLog.Info("WorldUI",
            $"HIT RECT '{panel.HostGo.name}' (commit #{entry.Commits}, root '{root.name}'"
            + (entry.SuppressedCommits > 0
                ? $", {entry.SuppressedCommits} further change(s) since the last line were applied "
                  + "to the rect but not logged — this window's drawn content is churning"
                : string.Empty)
            + "): "
            + $"host rect {host.width:F0}x{host.height:F0} px at "
            + $"({host.center.x:F0},{host.center.y:F0}); DRAWN CONTENT "
            + $"{content.width:F0}x{content.height:F0} px at "
            + $"({content.center.x:F0},{content.center.y:F0}) from {entry.Contributors} visible "
            + $"graphic(s); LARGER = {(grown ? "the content" : "the host rect")} → HIT RECT "
            + $"{hit.width:F0}x{hit.height:F0} px at ({hit.center.x:F0},{hit.center.y:F0}) "
            + $"[x {hit.xMin:F0}..{hit.xMax:F0}, y {hit.yMin:F0}..{hit.yMax:F0}] — {verdict}. "
            + $"NARROWINGS COMMITTED over this window's life: {entry.Shrinks}"
            + (entry.Shrinks == 0
                ? " — zero means this window has never had a strip of empty frame worth taking back, "
                  + "NOT that the rule is switched off. "
                : " (transitions INTO a narrowed state, not walks spent in one — ModBuild 243). ")
            + $"SUB-VIEW EDGES that forced this walk early: {entry.SignatureEdges} over this window's "
            + "life (ModBuild 243 — a tab press moves the interactive area on the same frame it moves "
            + "the brass bar and the close X, instead of up to "
            + $"{FitCheckIntervalFrames} frames later; a count stuck at 1 while the user opens "
            + "sub-menus means PanelInkBounds.ActiveSetSignature has gone blind for this window and "
            + "the laser is following the 30-frame poll again). "
            + (entry.OutsideBy > 0.5f
                ? $"FURTHEST OUTSIDE the frame: '{entry.OutsideOwner}' by {entry.OutsideBy:F0} px. "
                : "Nothing reaches outside the frame. ")
            + (entry.Clamped
                ? $"CLAMPED by the {MaxHitExpansion:F0}x safety bound — the measured content is "
                  + "larger than this path will follow, so the region beyond the hit rect stays "
                  + "unclickable; raise MaxHitExpansion if the window genuinely draws that far out. "
                : string.Empty)
            // APPENDED, ModBuild 440 — the chrome floor's own terms on the line that carries the
            // rect, so the two never have to be read from two places. The standalone
            // HIT RECT CHROME FLOOR line above is the default-tier verdict; this is the arithmetic.
            + (entry.ChromeValid
                ? $"MOD CHROME ON THIS HOST: {entry.ChromeParts} raycast target(s), first "
                  + $"'{entry.ChromeOwner}', spanning x {entry.Chrome.xMin:F0}..{entry.Chrome.xMax:F0} "
                  + $"and y {entry.Chrome.yMin:F0}..{entry.Chrome.yMax:F0} px — the hit rect is "
                  + "floored to contain it, so the narrowing can never cut over this window's own "
                  + "close X"
                  + (entry.ChromeHeld
                      ? " and ON THIS COMMIT IT DID: the floor pushed an edge back out. "
                      : ". ")
                : "MOD CHROME ON THIS HOST: none interactive — the floor had nothing to hold and "
                  + "this rect is what the content walk alone produced. ")
            + realSize + ". HOW TO READ THIS LINE: the HIT RECT is the rectangle "
            + "RayUguiDriver.TryIntersect tests the aim ray against — a laser lands on this window "
            + "if and only if it crosses this rectangle, and nowhere else. 'LARGER = the host rect' "
            + "means this window changed nothing and behaves exactly as before. 'LARGER = the "
            + "content' means the game drew outside the window's own RectTransform (it does NOT "
            + "resize that rect) and the interactive area followed it; the window's visible frame, "
            + "its close-X and the hit rect's own contract all still ride the HOST rect. The GRAB "
            + "BAR no longer does: since ModBuild 236 it is placed under the LOWEST DRAWN GRAPHIC "
            + "and centred on the drawn ink, so on a window with overspill it moves down and in — "
            + "see the GRAB BAR CLEARS THE INK line for the same window. If the "
            + "user reports dead input on a window, this line says whether the mod believed the "
            + "content was there at all: a hit rect equal to the host rect on a window that "
            + "visibly extends past it means the extension failed the visibility test (culled, "
            + "alpha below the fit floor, or clipped by a mask) — compare against the PANEL "
            + "SUPERSAMPLE capture-frame line for the same window, which measures the same "
            + "overspill with a deliberately more permissive test.");
    }

    // ==========================================================================================
    // ModBuild 396 — NAME THE GRAPHIC, NOT THE NUMBER (user report 2026-09-03, after 395: "Die
    // 'flashs' sind nach wie vor genau so da ohne dass ich eine Veränderung feststellen kann.")
    // ==========================================================================================
    //
    // TWO PREMISES DIED IN ONE LOG, and both were falsified by instruments built to falsify them.
    //
    //   * NOTHING IS PRE-START. 'FLASH VEIL SCAN #7' examined 591,324 registry entries across
    //     8,826 scans and found 24 with HasGoneToStartingState == false — 20 of those held at
    //     alpha 0 by a real CanvasGroup, the rest without a converted-panel ancestor. PREVENTED 0.
    //     So the flashing subtree has ALREADY RUN Start(); ModBuild 392's discriminator describes
    //     a population that is not the defect. (LET THROUGH 4 is the one thing that line proves
    //     positively: the veil is not eating close animations.)
    //   * THE REVEAL RECORDS NO PREFAB DEFAULT. 'REVEAL RESTORE WITHHELD' on New Party display
    //     reads "0 canvas(es) of 0" — the panel's hide never recorded a canvas off a pre-Start
    //     window, so the ModBuild 395 mechanism does not occur. It restored 16 canvases and not
    //     one of them was a candidate.
    //
    // WHAT SURVIVED IS THIS FILE'S OWN COLUMN MEASUREMENT, and it caught the flash twice in that
    // session without being asked to:
    //
    //     the CHARACTER COLUMN renders 1964x1080 px from (-1002,-540) [99 graphic(s)],
    //     pinned at (-982,-540) at 328x1080 px -> MOVED by -20,0 px and GREW by 1636,0 px
    //
    // against a settled 328x1080 at 85-86 graphics, and on the same frame PanelSupersample reports
    // "draws content that reaches 1996x1453 uGUI px around a 1988x1080 host rect" with "measured
    // overspill L32 R32 D384 U0". So the flash is about THIRTEEN extra graphics that paint across
    // the full width of the window and 384 px below it — not a whole tree at once, and not
    // anything either 392 or 395 was built for.
    //
    // WHAT NO LINE IN THE TREE SAYS IS **WHICH** GRAPHICS. Every reading so far has been a count
    // or a rectangle, and a count is compatible with every explanation ([[a-summary-stat-is-not-
    // the-field]], [[name-the-blocker-not-the-number]] — six rounds of tuning a fraction ended the
    // moment one field named WHICH renderer was responsible). This line names them: the three
    // graphics standing at the left, right and bottom extremes of the column's own union on a pass
    // where the column overspilled its pin, each with its full path and the verdict of the nearest
    // UIWindow above it. If the subtree is a sibling screen the game briefly enables, its window
    // is in that text; if it is one window drawn before its data binding runs, its window is in
    // that text and reports itself OPEN. Either way the next round starts from a name.
    //
    // IT IS NOT A FIX AND DOES NOT PRETEND TO BE. It writes nothing, reads only what the fit walk
    // has already visited, and builds strings on the passes that overspilled and no others.

    /// <summary>How far past its pinned width the column may drift before this counts as the
    /// flash. Four pixels, i.e. anything the existing SAME SIZE, SAME PLACE test would not have
    /// already called equal.</summary>
    private const float ColumnOverspillPx = 4f;

    /// <summary>Printed overspill lines per panel per session. Eight is far more than the two the
    /// 395 session produced and far fewer than a flood.</summary>
    private const int ColumnOverspillMaxLines = 8;

    /// <summary>
    /// SAY WHICH GRAPHIC MADE THE COLUMN WIDE, on the passes where it was wide. Change-triggered
    /// on a condition that is FALSE in the settled state — the column at its pin — so this is a
    /// quiet line that fires on the defect and nowhere else.
    /// </summary>
    private static void ReportColumnOverspill(ConvertedPanel panel, FixedFitState fx, Vector2 bs)
    {
        if (!fx.BasePinned || fx.OverspillLines >= ColumnOverspillMaxLines)
            return;
        Vector2 grew = bs - fx.BaseSizeAtPin;
        if (grew.x <= ColumnOverspillPx && grew.y <= ColumnOverspillPx)
            return;
        fx.OverspillLines++;

        // HW-VERIFY: the line that names the flashing subtree. A round asking "what is drawing"
        // reads this; the counts on the FIXED FIT line above it only ever said "something is".
        VRLog.Note("WorldUI", "COLUMN OVERSPILL on "
            + $"'{(panel.HostGo != null ? panel.HostGo.name : "?")}' at frame {Time.frameCount} "
            + $"(#{fx.OverspillLines} of at most {ColumnOverspillMaxLines}): the character column "
            + $"measured {bs.x:F0}x{bs.y:F0} px against the {fx.BaseSizeAtPin.x:F0}x"
            + $"{fx.BaseSizeAtPin.y:F0} px it was pinned at — GREW by {grew.x:F0},{grew.y:F0} px "
            + $"over {fx.BaseGraphics} graphic(s). THE THREE EXTREMES OF THAT UNION, each with the "
            + "nearest UIWindow above it and that window's own verdict on itself: LEFT edge "
            + $"x={fx.BaseEdgeLeftX:F0} {DescribeOverspillEdge(fx.BaseEdgeLeft, panel)}; RIGHT edge "
            + $"x={fx.BaseEdgeRightX:F0} {DescribeOverspillEdge(fx.BaseEdgeRight, panel)}; BOTTOM "
            + $"edge y={fx.BaseEdgeBottomY:F0} {DescribeOverspillEdge(fx.BaseEdgeBottom, panel)}. "
            + "HOW TO READ IT: a window reporting OPEN=True is one the game is deliberately "
            + "showing, so the defect is its CONTENT arriving late and the owner is whatever "
            + "populates it; OPEN=False with its canvas enabled is a subtree that is drawing "
            + "against the game's own verdict, and THAT is a visibility defect this lane can act "
            + "on. STARTED=False on any of them would mean the pre-Start population is back, which "
            + "the 395 session measured at zero.");
    }

    /// <summary>One extreme graphic, as a path plus the nearest ancestor UIWindow's verdict.
    /// Reads only; every failure degrades to a string rather than an exception.</summary>
    private static string DescribeOverspillEdge(Transform? t, ConvertedPanel panel)
    {
        if (t == null)
            return "'(none — no graphic at this extreme)'";
        string path = t.name;
        Transform? node = t.parent;
        for (int depth = 0; node != null && depth < 6; depth++, node = node.parent)
        {
            path = node.name + "/" + path;
            if (panel.Target != null && ReferenceEquals(node, panel.Target))
                break;
        }

        var w = t.GetComponentInParent<UIWindow>();
        if (w == null)
            return $"'{path}' (NO UIWindow above it inside 6 levels — it belongs to the column "
                   + "proper or to something the game parents outside a window)";
        var c = w.GetComponent<Canvas>();
        return $"'{path}' under UIWindow '{w.gameObject.name}' (ID {w.ID}): OPEN={w.IsOpen}, "
               + $"STARTED={w.HasGoneToStartingState}, VISIBLE={w.IsVisible}, "
               + $"activeSelf={w.gameObject.activeSelf}, canvas="
               + (c == null ? "none" : c.enabled ? "enabled" : "disabled");
    }
}
