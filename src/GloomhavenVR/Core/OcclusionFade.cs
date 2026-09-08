using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE SEE-THROUGH DECISION, WRITTEN ONCE (user request 2026-08-27: <i>"Wir hatten einiges an
/// Umbaumaßnahmen beim Wand-Fading das den Verdeckungsgrad prüft inklusive Optimierungen. ich
/// will dass die Logik für das Board die selbe ist, am besten derselbe code."</i>).
///
/// <para><b>WHAT IS SHARED, AND WHY EXACTLY THIS MUCH.</b> Two subsystems ask the same question
/// of two completely different occluders: <c>WallSegmentFade</c> asks it of a wall run against
/// its room's floor grid, <c>PeerBoardFade</c> asks it of a peer's control board against the
/// play field. Everything about WHAT is measured differs — the wall has room attribution, split
/// runs, prop units, a sliced commit and a masonry dissolve shader; the board has an oriented
/// board-local box, a material-swap delivery and a CanvasGroup. None of that belongs here.
/// What is genuinely the same is the DECISION between a stream of raw coverage fractions and a
/// stable boolean: the EMA that kills the jitter, the Schmitt band, the two-sided dwell, the
/// exponential ramp, and the "did the perspective actually change" signal that picks which exit
/// dwell applies. That is this file, and nothing else is.</para>
///
/// <para><b>THE DRIFT THIS ENDS.</b> <c>PeerBoardFade</c> was hand-copied from the wall's design
/// and its own class doc said so ("modelled one for one on <c>WallFadeTuning</c>"). A hand-copy
/// is a snapshot: by 2026-08-27 the board still carried the collapsing <c>Min(Off, On)</c> low
/// bar the wall replaced in ModBuild 252, still carried the eye-inside-the-box short circuit the
/// wall deleted in ModBuild 255 (with a post-mortem naming it as the source of 44 bogus
/// <c>raw 1.00</c> readings), and had never gained the rig-root clause that is the only way a
/// world-grab zoom registers as a perspective change at all. Every one of those is now
/// structurally impossible to have on one side and not the other, because there is one body of
/// code.</para>
///
/// <para><b>WHY THE PRIMITIVES TAKE <c>ref</c> STATE INSTEAD OF OWNING IT.</b> The obvious shape
/// is one struct holding {smooth, init, pendingRaw, pendingSince, latched} with a Step method —
/// and that is exactly what <see cref="OcclusionGate"/> is, and what the board uses. The wall
/// cannot: those five fields live on <c>Segment</c> and on <c>WallRun</c>, and TWO further
/// shadow copies of the same four of them exist elsewhere — <c>GateMemory</c>
/// (WallSegmentFade.Gate.cs), which carries the debounce across a gate prop's death and rebirth,
/// and <c>WallCommitDiff.SegmentFacts</c>, which the churn gate snapshots before and after one
/// atomic commit — the latter diffed field-by-field BY NAME in the commit-diff census. Wrapping them in a struct would
/// mean unpacking it again at every one of those sites and re-deriving the diff's field ids,
/// for no behavioural gain and with the carry-forward — the thing that decides whether a wall
/// survives a commit still faded — as the blast radius. So the RULE lives here once and the
/// STORAGE stays where each caller already keeps it. The wall's four dwell sites and the
/// board's one now run the same statements either way.</para>
///
/// <para>PURELY LOCAL, ON BOTH SIDES. Nothing here is ever computed from anything but this
/// viewer's own head, and no result of it goes on the wire.</para>
/// </summary>
internal static class OcclusionFade
{
    /// <summary>Short fade-IN prompt dwell (~0.2 s per spec). The one dwell that is not a config
    /// dial on either side: it exists to reject a single frame's coverage spike, not to be
    /// tuned.</summary>
    internal const float EnterDwellSeconds = 0.20f;

    /// <summary>How long one perspective change keeps the SHORT exit dwell armed.</summary>
    internal const float ReevalArmSeconds = 3f;

    /// <summary>REAL tracking-space metres of head translation that count as "the perspective
    /// changed". Scale-independent by construction: the head camera is a direct child of the rig
    /// root, so its localPosition is already tracking space and the diorama scale (11–20 world
    /// units per real metre) has divided out before the comparison.</summary>
    internal const float HeadMoveReevalMetres = 0.18f;

    /// <summary>Exponential fade time constant (~0.35 s to 95%).</summary>
    internal const float FadeTauSeconds = 0.12f;

    /// <summary>EMA over the raw coverage fraction — the jitter killer.</summary>
    internal const float FractionTauSeconds = 0.15f;

    /// <summary>Below this distance from the target the ramp snaps, so a fade actually finishes
    /// instead of asymptoting and leaving a property-block write running forever.</summary>
    internal const float FadeSnapEpsilon = 0.005f;

    /// <summary>Ceiling on the EMA's own time step. After a long stall the un-capped delta feeds
    /// the whole stall duration into the filter and snaps the smoothed coverage almost onto the
    /// raw sample; the cap is the safe direction and cannot fire in steady state.</summary>
    internal const float EvalDeltaCeilingSeconds = 0.5f;

    /// <summary>World units above the floor plane a sample sits at, so it is never swallowed by
    /// the mesh it stands on.</summary>
    internal const float FloorSampleEpsilon = 0.05f;

    /// <summary>Viewport slack on the frustum test. Also covers the mono-vs-per-eye skew: under
    /// MultiPass the two eye frusta differ by half the IPD and a little horizontal FOV, and 0.20
    /// viewport-relative covers that generously — which is what lets ONE mono answer feed a
    /// decision that must be identical in both eyes.</summary>
    internal const float FrustumMargin = 0.20f;

    /// <summary>Total floor/play-field sample budget. The cost that matters is one
    /// <c>WorldToViewportPoint</c> each per evaluation.</summary>
    internal const int MaxFloorSamples = 96;

    /// <summary>Fraction of the HIGH bar the low bar falls back to when the configured pair
    /// cannot form a Schmitt band at all — see <see cref="SchmittLowBar"/>. 0.6 keeps a band
    /// wide enough to survive the EMA's own jitter (tau 0.15 s) without making an occluder that
    /// genuinely stopped occluding wait for a near-zero reading before it comes back.</summary>
    internal const float DegenerateBandFallback = 0.6f;

    /// <summary>One step of an exponential filter with time constant <paramref name="tau"/>.</summary>
    internal static float StepFactor(float dt, float tau) => 1f - Mathf.Exp(-dt / tau);

    /// <summary>
    /// The EMA's own time step: the interval since the last EVALUATION, never since the last
    /// frame. A cadence that gated the decision but fed the filter a frame delta would silently
    /// stretch its time constant and change WHICH occluders yield, which is exactly what a
    /// performance dial must not do. Falls back to the frame delta on the very first evaluation,
    /// where there is no previous one to measure from.
    /// </summary>
    internal static float EvalDelta(float now, float lastEval, float frameDelta) =>
        lastEval > 0f ? Mathf.Min(now - lastEval, EvalDeltaCeilingSeconds) : frameDelta;

    /// <summary>
    /// Advance the smoothed coverage. THE FIRST EVALUATION SEEDS RATHER THAN FILTERS: an
    /// occluder that has just become decidable must start from its own live reading, not ramp up
    /// to it from zero — dropping <paramref name="init"/> is how both callers force a re-seed
    /// after a period in which they were not measuring.
    /// </summary>
    internal static void AdvanceCoverage(float raw, float step, ref float smooth, ref bool init)
    {
        if (!init)
        {
            init = true;
            smooth = raw;
        }
        else
        {
            smooth += (raw - smooth) * step;
        }
    }

    /// <summary>The Schmitt trigger itself: while latched the LOW bar holds it, while released
    /// the HIGH bar is what it takes to latch.</summary>
    internal static bool Above(float smooth, bool latched, float onFraction, float offFraction) =>
        smooth >= (latched ? offFraction : onFraction);

    /// <summary>
    /// Two-sided dwell. Returns true on the frame the latch actually flips, so a caller can log
    /// the edge and only the edge.
    ///
    /// <para>The state is deliberately three-valued in effect — <paramref name="latched"/> is
    /// what the world sees, <paramref name="pendingRaw"/> is what the measurement currently says,
    /// and <paramref name="pendingSince"/> is how long it has said it. A raw reading that
    /// oscillates never accumulates dwell, because any disagreement restarts the clock.</para>
    /// </summary>
    internal static bool StepDwell(bool raw, float now, float enterDwell, float exitDwell,
                                   ref bool pendingRaw, ref float pendingSince, ref bool latched)
    {
        if (raw != pendingRaw)
        {
            pendingRaw = raw;
            pendingSince = now;
        }
        if (pendingRaw == latched)
            return false;
        float dwell = pendingRaw ? enterDwell : exitDwell;
        if (now - pendingSince < dwell)
            return false;
        latched = pendingRaw;
        return true;
    }

    /// <summary>Critically-damped-style exponential ramp toward the debounced state, with the
    /// snap. This is the reason nothing ever pops even when the decision does.</summary>
    internal static float Ramp(float fade, float target, float step)
    {
        fade += (target - fade) * step;
        return Mathf.Abs(target - fade) < FadeSnapEpsilon ? target : fade;
    }

    /// <summary>
    /// The Schmitt LOW bar, with the degenerate-band guard.
    ///
    /// <para>A Schmitt trigger needs <c>off &lt; on</c>. A pair where the configured low bar is
    /// at or above the high bar has no band at all, and the older remedy — <c>Min(off, on)</c> —
    /// collapses BOTH bars onto one threshold that a coverage sitting near it crosses back and
    /// forth on EMA noise. That is the mechanism behind the group churn in the ModBuild 250 wall
    /// log, where four walls flip ON together and OFF together at lines 9471/9477, 10856/10870,
    /// 11746/11751, 12660/12667 and 12706/12709: four coverages sharing one bar with nothing
    /// between them. A configured low bar BELOW the high bar is honoured exactly as written —
    /// this only repairs the impossible order, which on both subsystems' shipped defaults is
    /// unreachable and exists as a guard against a hand-edited cfg.</para>
    /// </summary>
    internal static float SchmittLowBar(float configured, float onFraction) =>
        configured < onFraction ? configured : onFraction * DegenerateBandFallback;

    /// <summary>
    /// Is this world point in the head's view? Mono view/projection with
    /// <see cref="FrustumMargin"/> of slack — ONE answer used by both eyes, which is what keeps
    /// every decision downstream of it per-eye identical.
    /// </summary>
    internal static bool InFrustum(Camera head, Vector3 world)
    {
        Vector3 vp = head.WorldToViewportPoint(world);
        return vp.z > 0f
            && vp.x > -FrustumMargin && vp.x < 1f + FrustumMargin
            && vp.y > -FrustumMargin && vp.y < 1f + FrustumMargin;
    }
}

/// <summary>
/// "DID THE VIEWPOINT REALLY CHANGE?" — the signal that picks the SHORT exit dwell over the long
/// one, and the one place where "the head moved" and "the head only turned" are told apart.
///
/// <para>THREE CAUSES, AND THE SECOND IS THE ONE A HAND-COPY LOSES. (1) The rig pose version
/// bumps on a recenter or a rig rebuild. (2) The RIG ROOT itself moved, turned or was rescaled —
/// that is <c>WorldGrab</c> writing <c>rig.position</c>/<c>rig.rotation</c>/<c>rig.localScale</c>
/// for a two-handed drag or zoom, and a snap turn. It has to be watched in WORLD space precisely
/// because the head is a child of the rig: a world-grab moves them together, so the head's own
/// tracking-space position does not move a millimetre and clause (3) sees nothing at all. Zooming
/// into the board is the single largest perspective change this mod offers, and without this
/// clause it registers as no change whatsoever. (3) Real head TRANSLATION: the head camera's
/// localPosition is tracking space (metres, independent of the diorama scale), so a move past
/// <see cref="OcclusionFade.HeadMoveReevalMetres"/> re-arms and re-anchors while micro-sway and
/// pure rotation never do.</para>
///
/// <para>A fourth cause is caller-specific and arrives through <see cref="Note"/>: the wall's
/// room bounds shifting (the board was moved or tilted), the peer board's own root being dragged
/// by its owner. Both mean "the geometry under the question moved", which is the same thing to
/// the viewer as moving himself.</para>
///
/// <para>A CLASS AND NOT A STRUCT, on purpose: <c>_poseVersion = -1</c> and
/// <c>_lastChange = -inf</c> are load-bearing initial values (the first tick of a session must
/// arm, and nothing before it may read as armed), and a struct field on a MonoBehaviour gets
/// <c>default</c> instead of them.</para>
/// </summary>
internal sealed class PerspectiveWatch
{
    private int _poseVersion = -1;
    private Vector3 _headAnchor;
    private bool _headAnchorInit;
    private Vector3 _rigPos;
    private Quaternion _rigRot;
    private float _rigScale;
    private bool _rigSnapInit;
    private float _lastChange = float.NegativeInfinity;

    /// <summary>Was the perspective disturbed recently enough to use the SHORT exit dwell?</summary>
    internal bool Armed(float now) => now - _lastChange <= OcclusionFade.ReevalArmSeconds;

    /// <summary>Arm from a cause this watch cannot see for itself — see the class note.</summary>
    internal void Note(float now) => _lastChange = now;

    /// <summary>The three self-observable causes. Cheap enough to run every frame, and it must:
    /// a perspective change whose edge waits out a skipped evaluation is a change that arrives
    /// after the dwell it was supposed to shorten.</summary>
    internal void Tick(Transform headT, float now)
    {
        int pv = Rig.VRRigDriver.RigPoseVersion;
        if (pv != _poseVersion)
        {
            _poseVersion = pv;
            _lastChange = now;
        }

        Transform? rig = Rig.VRRigDriver.RigRoot;
        if (rig != null)
        {
            Vector3 p = rig.position;
            Quaternion q = rig.rotation;
            float s = rig.lossyScale.x;
            if (!_rigSnapInit)
            {
                _rigSnapInit = true;
                _rigPos = p;
                _rigRot = q;
                _rigScale = s;
            }
            else if ((p - _rigPos).sqrMagnitude > 0.0004f * s * s // 2cm real, scale-aware
                     || Quaternion.Angle(q, _rigRot) > 0.5f
                     || Mathf.Abs(s - _rigScale) > 0.005f * Mathf.Max(_rigScale, 0.001f))
            {
                _rigPos = p;
                _rigRot = q;
                _rigScale = s;
                _lastChange = now;
            }
        }

        Vector3 headLocal = headT.localPosition;
        if (!_headAnchorInit)
        {
            _headAnchorInit = true;
            _headAnchor = headLocal;
        }
        else if ((headLocal - _headAnchor).sqrMagnitude
                 > OcclusionFade.HeadMoveReevalMetres * OcclusionFade.HeadMoveReevalMetres)
        {
            _headAnchor = headLocal;
            _lastChange = now;
        }
    }
}

/// <summary>
/// The whole decision as one piece of state: raw coverage in, a stable <see cref="Latched"/> out.
/// Used by <c>PeerBoardFade</c>, which keeps no other copy of these fields. The wall drives the
/// same primitives against its own <c>Segment</c>/<c>WallRun</c> fields for the reason set out on
/// <see cref="OcclusionFade"/> — this type is the convenient packaging, not the authority; the
/// authority is the static methods it calls.
/// </summary>
internal struct OcclusionGate
{
    /// <summary>EMA-smoothed coverage fraction — the number both bars are compared against.</summary>
    public float Smooth;
    /// <summary>False until the first evaluation seeds <see cref="Smooth"/>. Clearing it forces a
    /// re-seed from the next live reading instead of a ramp up from a stale one.</summary>
    public bool SmoothInit;
    public bool PendingRaw;
    public float PendingSince;
    /// <summary>What the world sees. The only output.</summary>
    public bool Latched;

    /// <summary>One EVALUATION tick: EMA, Schmitt, dwell. Returns true on the flip frame only.
    /// The ramp is deliberately NOT here — it runs every frame, on a different clock, and folding
    /// it in would tie the two cadences together.</summary>
    public bool Evaluate(float raw, float now, float fracStep, float onFraction,
                         float offFraction, float enterDwell, float exitDwell)
    {
        OcclusionFade.AdvanceCoverage(raw, fracStep, ref Smooth, ref SmoothInit);
        bool above = OcclusionFade.Above(Smooth, Latched, onFraction, offFraction);
        return OcclusionFade.StepDwell(above, now, enterDwell, exitDwell,
                                       ref PendingRaw, ref PendingSince, ref Latched);
    }

    /// <summary>Forget everything. A caller that comes back is judged fresh against the view it
    /// comes back into, never against the one it left.</summary>
    public void Reset()
    {
        Smooth = 0f;
        SmoothInit = false;
        PendingRaw = false;
        PendingSince = 0f;
        Latched = false;
    }
}
