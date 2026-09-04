using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// A game UI panel (RectTransform) that has been moved onto a WorldUI-owned
/// world-space host canvas. Stores everything needed to put it back exactly
/// where it was (hot reload / VR-off must leave the 2D UI intact).
/// </summary>
internal sealed class ConvertedPanel
{
    // What was moved.
    public RectTransform Target = null!;

    // Original placement (restored on Release).
    public Transform OriginalParent = null!;
    public int OriginalSiblingIndex;
    public Vector2 OriginalAnchorMin, OriginalAnchorMax, OriginalPivot;
    public Vector2 OriginalAnchoredPosition, OriginalSizeDelta;
    public Vector3 OriginalLocalScale;
    public Vector3 OriginalLocalPosition;
    public Quaternion OriginalLocalRotation;

    /// <summary>
    /// THE SCENE THE TARGET LIVED IN BEFORE THE CONVERSION MOVED IT — recorded because a
    /// reparent SILENTLY CHANGES SCENE MEMBERSHIP, and that is what destroyed the game's
    /// persistent confirmation box on 2026-09-03 (see <c>CanvasConversion.KeepHostInTargetScene</c>).
    /// A GameObject belongs to the scene of its ROOT: the instant a <c>DontDestroyOnLoad</c>
    /// window becomes a child of a host created in the active scene, it stops being persistent
    /// and the next scene unload deletes it — while the game's persistent Singleton keeps its
    /// managed reference and calls into the dead native object.
    /// </summary>
    public UnityEngine.SceneManagement.Scene TargetHomeScene;

    /// <summary>True when <see cref="TargetHomeScene"/> was the <c>DontDestroyOnLoad</c> scene, i.e.
    /// the target is a PERSISTENT game object that must not be allowed to join a normal scene.</summary>
    public bool TargetWasPersistent;

    // WorldUI-owned host.
    public GameObject HostGo = null!;
    public Canvas HostCanvas = null!;
    public GraphicRaycaster HostRaycaster = null!;
    public RectTransform HostRect = null!;

    // ---- content-fit state (test #14 item 1; driven by CanvasConversion.Tick) ----------
    /// <summary>Optional narrower subtree to measure (e.g. the story window's UICharacterStoryBox).</summary>
    public RectTransform? FitContentRoot;

    /// <summary>
    /// True when the target converted with a degenerate (&lt;1 px) rect that Convert
    /// clamped to the 100 px placeholder (zero-size layout containers, e.g. the
    /// objectives list). The placeholder is NOT a real window frame — clamping the
    /// content fit into it would crop the measured bounds to 100 px while the text
    /// visibly overflows it (test #16: giant objectives text from a 100 px host).
    /// </summary>
    public bool FitFrameDegenerate;

    /// <summary>True for pokeable hosts: the registered laser/poke plane must match visible content.</summary>
    public bool FitEnabled;

    /// <summary>First fit attempt not before this time (window show animations run ~0.3 s).</summary>
    public float FitNotBefore;

    /// <summary>Warn once if nothing measurable by this time (then demote to periodic checks).</summary>
    public float FitFirstDeadline;

    /// <summary>True after the first successful measure (or after the deadline warn).</summary>
    public bool FitMeasuredOnce;

    /// <summary>
    /// Item 1 (pause-menu size): a full-screen menu (ESC / Options family) fits ONCE to its
    /// visible content and then LOCKS — no per-frame re-fit. The P6 flicker fix exempted these
    /// menus from the fit entirely (host stayed at the game window's own rect: 1920x2040, hugely
    /// tall with empty space, and different on the first open before the window had laid out). A
    /// one-shot fit trims the empty space and lands on the DETERMINISTIC visible-button bounds, so
    /// the panel is the same compact size every open; locking after the single apply keeps the
    /// per-frame re-fit flicker the exemption was avoiding from ever recurring (the opaque backing
    /// is hidden by <see cref="ConvertedPanel.HideBackground"/>, so the fit measures only the
    /// stable foreground content — the other arm of that flicker is already gone).
    /// </summary>
    /// <summary>
    /// ROUND 5 (cold ESC menu, hardware ModBuild 21): this panel was converted with the
    /// full-screen-menu HEIGHT CAP (<c>capHeightToCanvas</c>) — its host rect was clamped to the
    /// root <c>CanvasScaler.referenceResolution.y</c> at convert (log: "height capped 2040-&gt;1080").
    /// The AUTHORED union the round-4 fix measures is NOT subject to that clamp, so a cold open
    /// adopted the raw authored 2040 px height while every warm open fits 1080 — a host twice as
    /// tall as the game ever draws, with the visible menu sitting in one half and an empty frame
    /// in the other (exactly the reported "empty window in front, menu far behind").
    /// Remembering the flag lets the fit apply the SAME cap to an authored measurement.
    /// </summary>
    public bool FitHeightCapped;

    /// <summary>The window name the height cap was resolved under (cap fallback key).</summary>
    public string FitHeightCapName = string.Empty;

    public bool FitOneShot;

    /// <summary>Set true the frame a one-shot fit actually RESIZED the host — the owning modal
    /// then re-derives its board-relative scale from the now-fitted width (item 1).</summary>
    public bool FitOneShotApplied;

    /// <summary>True once the give-up warning was logged (log hygiene).</summary>
    public bool FitGaveUpLogged;

    // ---- one-shot fit: commit → VERIFY → lock (first-open size bug, round 3) ----------------
    /// <summary>
    /// True once the one-shot settle gate has COMMITTED its single fit. Distinct from
    /// <see cref="FitOneShotApplied"/> (which only says a resize happened, and stays false when
    /// the fit landed inside the 2 % no-op tolerance) and from the final lock: between commit and
    /// lock the rect must still earn itself through <see cref="FitVerifyPending"/>.
    /// </summary>
    public bool FitCommitted;

    /// <summary>
    /// True while a committed one-shot fit is being VERIFIED — i.e. re-measured to prove the
    /// content still sits centered in, and sized like, the host rect it was just fitted to. WHY
    /// this phase exists: on hardware (ModBuild 18) the ESC menu's cold first open measured a
    /// perfectly steady 271x282 px box, locked it, and the game's layout then moved the real
    /// content to a place that did not overlap that box on EITHER axis — the reported "almost
    /// empty window frame in front of the player, actual menu far off to the side". Nothing
    /// observable AT COMMIT TIME distinguished that measurement from a good one, so the rect is
    /// no longer trusted on commit: it is watched, and a materially self-inconsistent rect is
    /// re-fitted (see <c>CanvasConversion.VerifyOneShotFit</c>).
    /// </summary>
    public bool FitVerifyPending;

    /// <summary>Unscaled time the verify watch ends — after this the rect is locked at the best
    /// measurement available, self-consistent or not (bounded: a window is never watched forever).</summary>
    public float FitVerifyUntil;

    /// <summary>
    /// True when the committed rect reproduces this window's PREVIOUS open in this session (see
    /// <c>CanvasConversion.LastOneShotFits</c>). A proven rect locks the moment it verifies —
    /// warm re-opens keep their historic instant behaviour. An UNPROVEN one (the session's first
    /// open, i.e. the defective case) stays under watch for the whole bounded window even after
    /// the window has been revealed, because the hardware failure is a discrete LATE jump.
    /// </summary>
    public bool FitVerifyProven;

    /// <summary>Next frame the verify watch samples once the window is VISIBLE (throttled: a
    /// render-hidden panel checks every frame, a visible one only a few times a second).</summary>
    public int FitVerifyNextCheckFrame;

    /// <summary>
    /// Round 4 (show-animation blind spot): hard end of the EXTENDED verify watch. While the
    /// measure reports the game's show animation still in flight, <see cref="FitVerifyUntil"/> is
    /// pushed forward — the rect stands at the AUTHORED geometry and deserves confirmation from the
    /// rendered content — but never past this cap, so a permanently animating window cannot hold a
    /// fit open forever. The REVEAL is not affected by either: it is bounded by
    /// <see cref="RevealDeadline"/> alone.
    /// </summary>
    public float FitVerifyHardUntil;

    /// <summary>True once this open's measure has seen the game's show animation in flight (the
    /// rendered content materially smaller/larger than the layout authored it). Reported by the fit
    /// summary line — it is the difference between a cold and a warm open of the ESC menu.</summary>
    public bool FitSawShowAnimation;

    /// <summary>Which open of this window (by host name) this conversion is, 1-based — the "open #N"
    /// of the fit summary line, so a hardware log reads as "open 1 said X, open 2 said Y".</summary>
    public int FitOpenIndex;

    /// <summary>
    /// While &gt; 0 and not yet reached, the reveal gate keeps this window render-hidden even though
    /// its fit is committed: an UNPROVEN one-shot rect (no earlier open of this window in the
    /// session to compare against) uses the remaining, already-budgeted pre-reveal time to
    /// re-verify instead of popping in at a rect nothing has corroborated. Always clamped inside
    /// <see cref="RevealDeadline"/>, so the "a window may never stay invisible" bound is untouched.
    /// </summary>
    public float FitVerifyHoldRevealUntil;

    /// <summary>Consecutive verify checks the committed rect was self-consistent (streak → lock).</summary>
    public int FitVerifyStableCount;

    /// <summary>Consecutive verify checks a MATERIAL self-consistency error persisted (streak →
    /// one corrective re-fit; a single-frame animation artifact must never move the window).</summary>
    public int FitVerifyErrorCount;

    /// <summary>Corrective re-fits applied during this open's verify watch (hard-capped, so the
    /// one-shot lock's "no re-fit flicker" promise only ever yields to a genuinely broken rect).</summary>
    public int FitVerifyCorrections;

    /// <summary>Measured content size at the previous MATERIALLY inconsistent verify check — the
    /// correction requires the erroneous measurement to have stopped moving, so a window caught
    /// mid animation is never re-fitted to an intermediate rect.</summary>
    public Vector2 FitVerifyErrorSize;

    /// <summary>Measured content center at the previous materially inconsistent verify check (see
    /// <see cref="FitVerifyErrorSize"/>).</summary>
    public Vector2 FitVerifyErrorCenter;

    /// <summary>
    /// Incremented by every APPLIED fit. ModalFallback's one-shot followers — the board-relative
    /// scale re-derivation (5b) and the pose re-place at final geometry (5b-pose) — latch on this
    /// value instead of a plain bool, so a VERIFY correction makes them replay against the
    /// corrected geometry. Without it a corrected window would keep the scale and the spawn pose
    /// derived from the rect that was just proven wrong.
    /// </summary>
    public int FitAppliedGeneration;

    /// <summary>Next periodic re-check frame (growth dirty-check throttle).</summary>
    public int FitNextCheckFrame;

    // ---- nested-canvas adoption (tests #19/#20) ----------------------------------------
    /// <summary>
    /// Game-owned nested <see cref="Canvas"/> components inside the converted subtree,
    /// kept ENABLED with <c>overrideSorting</c> cleared and their raycaster merged into
    /// the host's hit-testing while converted; original state restored on Release (see
    /// <see cref="CanvasConversion.AdoptNestedCanvases"/> for why disabling them was
    /// wrong).
    /// </summary>
    public readonly List<NestedCanvasRecord> AdoptedCanvases = new(2);

    /// <summary>
    /// THE CANVASES OF THIS WINDOW AS THE GAME LEFT THEM, captured by
    /// <c>CanvasConversion.PreCaptureGameCameras</c> in the last moment before <c>Convert</c>
    /// re-parents the window under the float host. Parallel to <see cref="PreCapturedCameras"/>.
    ///
    /// <para>WHY IT IS TAKEN THERE AND NOWHERE ELSE. Unity reports a NESTED canvas's
    /// <c>worldCamera</c> and <c>sortingOrder</c> from its ROOT canvas, and after the re-parent
    /// that root is the mod's world-space host. Every value read from inside the float is therefore
    /// the MOD's, which is why ModBuild 424's leak guard fired on 23 of the 24 adoptions in the 425
    /// log and had to fall back to guessing the game's UI camera. Read before the re-parent, this
    /// is the game's actual value, and the release hands back an observation instead of a
    /// guess.</para>
    /// </summary>
    public readonly List<Canvas> PreCapturedCanvases = new(4);

    /// <summary>The <c>worldCamera</c> each entry of <see cref="PreCapturedCanvases"/> carried at
    /// its 2D home — <c>null</c> where the game itself had null, which is an observation and not a
    /// fallback.</summary>
    public readonly List<Camera?> PreCapturedCameras = new(4);

    /// <summary>Next frame for the periodic nested-canvas sweep (pooled children can bring canvases late).</summary>
    public int CanvasSweepNextFrame;

    // ---- 2D flatten (test #21) ---------------------------------------------------------
    /// <summary>
    /// Opt-in (<see cref="CanvasConversion.Convert"/> <c>flatten2D</c>): neutralize the
    /// game's REAL 3D styling inside the converted subtree — see
    /// <see cref="CanvasConversion.FlattenSubtree"/>.
    /// </summary>
    public bool FlattenEnabled;

    /// <summary>Transforms caught carrying 3D (rotation / local z), originals kept for Release.</summary>
    public readonly List<FlattenRecord> Flattened = new(16);

    /// <summary>Count at the last flatten log line (log once per conversion, re-log on pooled growth).</summary>
    public int FlattenLoggedCount;

    /// <summary>Earliest frame for the next growth re-log (pooling adds entries one by one).</summary>
    public int FlattenLogNextFrame;

    // ---- WINDOW FLATNESS GUARANTEE (ModBuild 193; user report 3) -----------------------
    /// <summary>
    /// THE FLOATED-WINDOW FAMILY'S OWN FLATTEN OPT-IN — set by
    /// <see cref="CanvasConversion.Convert"/> <c>flattenWindow</c>, which every window
    /// <c>ModalFallback</c> floats now passes. Deliberately a DIFFERENT field from
    /// <see cref="FlattenEnabled"/>, and the reason is not cosmetic:
    /// <c>PanelSupersample.Eligible</c> refuses any panel with <see cref="FlattenEnabled"/> set
    /// (PanelSupersample.1.Core.cs, the second bullet of its eligibility list), so re-using that
    /// flag for the floated family would have switched the whole supersampling feature off in the
    /// same build that turned flattening on — the ModBuild 192 log shows it live on
    /// 'New Party display' and 'Quest Log Manager'. Two opt-ins, two drivers, no collision; see
    /// <c>CanvasConversion.PanelFlattenDriver</c> for the cadence. RESOLVED AT INTEGRATION
    /// (ModBuild 193): the refusal was verified false and REMOVED, so this field is no longer
    /// load-bearing for that reason — it now only selects WHICH DRIVER runs the clamp.
    /// </summary>
    public bool FlattenWindowGuarantee;

    /// <summary>
    /// THE RESUMABLE WALK. The discovery scan does NOT restart from the root every frame and does
    /// NOT run to completion in one frame: it pops a fixed budget of transforms per frame and keeps
    /// the rest here for the next one (<c>CanvasConversion.FlattenWalkBudgetPerFrame</c>). A window
    /// is therefore fully re-examined every <c>ceil(size / budget)</c> frames at a FLAT cost, rather
    /// than costing nothing for nine frames and a 2700-transform spike on the tenth. Entries can go
    /// Unity-null between frames (pooled children are destroyed mid-walk); the pop null-checks.
    /// </summary>
    public readonly List<Transform> FlattenWalk = new(64);

    /// <summary>Running counters for the walk cycle IN PROGRESS; published to the
    /// <c>FlattenLast*</c> fields (which the census reads) only when a cycle completes, so the
    /// census never reports half a window.</summary>
    public int FlattenCycleVisited, FlattenCycleForeign, FlattenCyclePlain3D, FlattenCycleFrames;

    /// <summary>First foreign / plain-3D name seen during the cycle in progress.</summary>
    public string? FlattenCycleForeignSample, FlattenCyclePlain3DSample;

    /// <summary>Transforms walked by the last COMPLETE cycle.</summary>
    public int FlattenLastVisited;

    /// <summary>Frames the last COMPLETE cycle took — i.e. the worst-case latency, in frames,
    /// between a newly pooled child arriving tilted and this sweep finding it.</summary>
    public int FlattenLastCycleFrames;

    /// <summary>Foreign render subtrees (a real <c>Renderer</c> or <c>Camera</c>) the last rescan
    /// refused to descend into — case (2), left EXACTLY as the game has them.</summary>
    public int FlattenLastForeign;

    /// <summary>Of the transforms the last rescan FLATTENED, how many carried a nested
    /// <c>Canvas</c> — case (3). Counted on the flattened set only (a <c>GetComponent</c> per
    /// tilted transform is free; one per visited transform over a 2700-transform window is not).
    /// </summary>
    public int FlattenLastNestedCanvas;

    /// <summary>
    /// Transforms inside the window that carry real 3D (rotation / local z) but are NOT
    /// <c>RectTransform</c>s and are not foreign render roots — plain <c>Transform</c> holders
    /// inside a uGUI tree. They are COUNTED AND NAMED, never written: the flatten contract has
    /// always been RectTransform-only (test #21) and widening it silently would put this sweep in
    /// charge of objects whose pose is somebody else's meaning. If the user's report survives a
    /// build in which every other number here is zero, THIS number is where to look next.
    /// </summary>
    public int FlattenLastPlain3D;

    /// <summary>Name of the first such plain 3D-posed transform (see <see cref="FlattenLastPlain3D"/>).</summary>
    public string? FlattenPlain3DSample;

    /// <summary>Of the recorded set: how many were caught carrying a local ROTATION only.</summary>
    public int FlattenRotationCount;

    /// <summary>Of the recorded set: how many were caught carrying a local Z offset only.</summary>
    public int FlattenZCount;

    /// <summary>Of the recorded set: how many carried BOTH a rotation and a z offset.</summary>
    public int FlattenBothCount;

    /// <summary>Writes the last per-frame re-assert pass had to make (0 = the game is not fighting
    /// us on this window; a steady non-zero number names a live writer).</summary>
    public int FlattenReasserts;

    /// <summary>Total re-assert writes since conversion (the write-war evidence in the census).</summary>
    public int FlattenReassertTotal;

    /// <summary>Stopwatch ticks spent in the budgeted discovery walk since conversion (census
    /// reports the per-FRAME mean, which is the number that has to fit in the frame budget).</summary>
    public long FlattenScanTicks;

    /// <summary>Frames in which the discovery walk ran (divisor for <see cref="FlattenScanTicks"/>).</summary>
    public int FlattenScanRuns;

    /// <summary>Stopwatch ticks spent in per-frame re-assert passes since conversion.</summary>
    public long FlattenReassertTicks;

    /// <summary>Per-frame re-assert passes run since conversion.</summary>
    public int FlattenReassertRuns;

    /// <summary>Earliest frame the per-window census line may be re-printed.</summary>
    public int FlattenCensusNextFrame;

    /// <summary>Last census payload, so an unchanged window re-prints on the slow heartbeat only.</summary>
    public string? FlattenCensusLast;

    /// <summary>Name of the first foreign render subtree the last rescan refused — so the log names
    /// the thing the user is looking at instead of only counting it.</summary>
    public string? FlattenForeignSample;

    // ---- re-fit churn damping (test #17; see FitHostToContent) -------------------------
    /// <summary>Time of the last APPLIED fit (shrink/re-center rate limit).</summary>
    public float FitLastApplied;

    /// <summary>Pending shrink/re-center candidate size; zero when none.</summary>
    public Vector2 FitPendingSize;

    /// <summary>Time the pending candidate was first measured (stability clock).</summary>
    public float FitPendingSince;

    /// <summary>
    /// PER-SIDE SLACK between the visible content and this host's rect, in uGUI px of the host's
    /// own space, as of the last measure (<c>CanvasConversion.FitContentPaddingPx</c>, minus
    /// whatever the frame clamp / canvas height cap ate). The content is CENTERED in the host, so
    /// the visible top edge is <c>HostRect.rect.yMax - FitContentPadding.y</c>.
    ///
    /// <para>WHY IT IS PUBLISHED: the decision area is laid out downward from one ceiling and a
    /// surface that pins its host by <c>rect.yMax</c> therefore seats the CONTENT this far below
    /// the ceiling — and passes the error on to everything hanging off its published bottom edge.
    /// Zero until the first measure, which is safe: nothing places before it has fitted.</para>
    /// </summary>
    public Vector2 FitContentPadding;

    /// <summary>
    /// OPT OUT OF THE SHRINK DAMPING ABOVE — the panel gives its growth back in the frame the
    /// content shrinks (user ruling 2026-08-09, the decision area: "sobald es wieder eingeklappt
    /// wird soll es sofort reagieren"; the reported ~2 s lag IS
    /// <c>FitStableSeconds</c> 0.5 + <c>FitRefitMinIntervalSeconds</c> 1.5 plus the periodic check
    /// throttle). Growth was always immediate; this makes the panel symmetric.
    ///
    /// <para>ONLY FOR PANELS THAT HAVE A BETTER STABILITY MECHANISM THAN A CLOCK. The damping
    /// exists so OSCILLATING content (the combat log's fading lines) cannot re-fit twice a second,
    /// and a hover scale-up that decays would otherwise re-place the panel the instant it decays.
    /// <c>UseBarsSurface.BarDock</c> qualifies: it holds <see cref="FitEnabled"/> OFF unless the
    /// bar's LAYOUT TRUTH changed (slot set, open sub-picker, post-dock settle), so no hover or
    /// press transient ever reaches this path at all. A panel that fits every frame must NOT set
    /// this.</para>
    /// </summary>
    public bool FitShrinkImmediate;

    /// <summary>Host transform for placement by the owning surface.</summary>
    public Transform HostTransform => HostGo.transform;

    /// <summary>True while the moved rect still exists (scene not unloaded).</summary>
    public bool IsAlive => Target != null;

    // ---- floated-modal FLICKER instrumentation (targeted; only modal hosts opt in) -------
    /// <summary>
    /// VERBOSITY ONLY. Opt-in per-frame LOGGING for the floated-modal flicker hunt (set by
    /// <see cref="CanvasConversion.Convert"/> <c>diagnostic</c>, true for ModalFallback
    /// hosts only — the small content-fit panels never flicker and would only spam).
    /// Drives <see cref="CanvasConversion.DiagnoseModal"/>: a change-gated snapshot of the
    /// host + adopted-child render state, plus a camera scan that reveals a second camera
    /// double-drawing the modal's UI layer.
    ///
    /// <para><b>ModBuild 199 — THIS FIELD NO LONGER GATES ANY WORK, AND MUST NEVER DO SO AGAIN.</b>
    /// Until ModBuild 198 it gated three pieces of real per-frame maintenance in
    /// <c>CanvasConversion.Tick</c> (the per-frame nested-canvas sweep,
    /// <c>ReassertAdoptedSorting</c> — whose own comment reads "FLICKER FIX (modal hosts only) …
    /// re-assert every frame" — and <c>ReassertConversionFrame</c>). Because
    /// <see cref="GrabbableModal.ThrottleDiagWhileMoving"/> clears it to ~1 Hz for exactly as long as
    /// the player holds a window, a fix whose stated contract was "every frame" ran at 1 Hz precisely
    /// during the interval the user reports flicker under. The two concerns are split now:
    /// <see cref="PerFrameGuards"/> is the WORK gate and nothing throttles it; this flag is the LOG
    /// gate and the drag throttle still owns it. A future spam fix may turn this off freely.</para>
    /// </summary>
    public bool Diagnostic
    {
        get => _diagnostic;
        set
        {
            _diagnostic = value;
            // ENROLMENT LATCH, never a throttle. The only writer that ever passes TRUE for a panel
            // that was not already enrolled is CanvasConversion.Convert(diagnostic: true) — i.e. a
            // ModalFallback host. Every later write is GrabbableModal's log throttle, which alternates
            // this flag on a panel that is already enrolled, so the latch is a no-op for it. Setting
            // the work gate HERE keeps the enrolment in one place without a second writer in
            // CanvasConversion.1.Core.cs (another lane's file this round).
            if (value)
                PerFrameGuards = true;
        }
    }

    private bool _diagnostic;

    /// <summary>
    /// WORK, NOT VERBOSITY: this host runs the per-frame modal-host maintenance in
    /// <c>CanvasConversion.Tick</c> — the every-frame nested-canvas sweep, the adopted-sorting
    /// re-assert and the conversion-frame guard. Latched on by <see cref="Diagnostic"/>'s enrolment
    /// (see there) and cleared only by <c>CanvasConversion.Release</c>. <b>Nothing may throttle
    /// this.</b> Its whole reason to exist is that a per-frame fix stops being a fix at 1 Hz.
    /// </summary>
    public bool PerFrameGuards;

    // ---- guard budget instrument (ModBuild 199) ------------------------------------------
    /// <summary>Published every <see cref="GrabbableModal.Tick"/>: the host pose changed this frame
    /// (the same epsilon the log throttle uses). False for a window nobody is moving.</summary>
    public bool GuardHostMoving;

    /// <summary>Published every <see cref="GrabbableModal.Tick"/>: a hand actually grips this
    /// window's bar right now. A window can be MOVING without being HELD (recall, refloat).</summary>
    public bool GuardHostHeld;

    /// <summary>Guard-budget accumulators for the current report window: how many times the
    /// adopted-sorting re-assert RAN, split by whether the host was still or moving.</summary>
    public int SortGuardRunsStill, SortGuardRunsMoving;

    /// <summary>How many of those runs actually WROTE something (a correction). This is the number
    /// that decides whether throttling the guard could ever have mattered.</summary>
    public int SortGuardWritesStill, SortGuardWritesMoving;

    /// <summary>Correction census by kind: overrideSorting cleared, conceded sortingOrder followed,
    /// worldCamera re-bound. Summed over the report window.</summary>
    public int SortGuardFlagWrites, SortGuardOrderWrites, SortGuardCameraWrites;

    /// <summary>Stopwatch ticks spent inside the adopted-sorting re-assert, still vs moving.</summary>
    public long SortGuardTicksStill, SortGuardTicksMoving;

    /// <summary>Conversion-frame guard: runs and corrections, still vs moving.</summary>
    public int FrameGuardRunsStill, FrameGuardRunsMoving;

    /// <summary>Conversion-frame guard corrections (its return value), still vs moving.</summary>
    public int FrameGuardWritesStill, FrameGuardWritesMoving;

    /// <summary>Stopwatch ticks spent inside the conversion-frame guard, still vs moving.</summary>
    public long FrameGuardTicksStill, FrameGuardTicksMoving;

    /// <summary>Worst SINGLE frame's combined guard cost this window, in stopwatch ticks — the
    /// number a per-frame budget is actually judged against (a mean hides the spike).</summary>
    public long GuardWorstFrameTicks;

    /// <summary>Adopted canvases walked on the last run — the loop's comparison count.</summary>
    public int SortGuardLastCanvases;

    /// <summary>Nested-canvas adoption sweep: stopwatch ticks, still vs moving, plus how often it
    /// ran and how often it actually adopted a new canvas (the only outcome that costs writes).</summary>
    public long AdoptSweepTicksStill, AdoptSweepTicksMoving;

    /// <summary>Adoption sweep runs and real adoptions in the current report window.</summary>
    public int AdoptSweepRuns, AdoptSweepAdoptions;

    /// <summary>
    /// UPDATE→LATEUPDATE POSE GAP (ModBuild 199, GrabbableModal): how far the host moved between the
    /// Update-time frame→host copy and the LateUpdate re-sync, expressed in the window's own AUTHORED
    /// PIXELS. Anything that samples the host pose during Update — and <c>PanelSupersample.Tick</c>
    /// does, at the end of <c>CanvasConversion.Tick</c> — is reading a pose this many pixels stale.
    /// Worst value and sample count for the current report window.
    /// </summary>
    public float PoseGapWorstPx;

    /// <summary>Samples and over-threshold count for the Update→LateUpdate pose gap.</summary>
    public int PoseGapSamples, PoseGapOverOnePx;

    /// <summary>Sum of the pose gap in authored pixels (for the mean).</summary>
    public double PoseGapSumPx;

    /// <summary>Next unscaled time the guard-budget line prints (0 = not scheduled yet).</summary>
    public float GuardReportNextAt;

    /// <summary>Unscaled time the current report window opened (its real duration, not the nominal).</summary>
    public float GuardReportSince;

    /// <summary>Last-logged per-frame host/child snapshot (change-gated — logs only on churn).</summary>
    public string? DiagLastSnapshot;

    /// <summary>Last-logged camera scan (change-gated — cameras rarely change).</summary>
    public string? DiagLastCameras;

    /// <summary>Convert time (unscaled) — the snapshot reports host age so a re-place is obvious.</summary>
    public float DiagConvertedAt;

    /// <summary>Next frame the (allocating) camera scan runs — throttled; cameras change rarely.</summary>
    public int DiagNextCameraScanFrame;

    // ---- adopted sibling order rebase + census (ModBuild 203) -----------------------------
    /// <summary>
    /// The rebase-eligible SET changed (a canvas was adopted, pruned, or conceded), so
    /// <c>CanvasConversion.RebuildConcededOrderOffsets</c> must re-derive every
    /// <see cref="NestedCanvasRecord.RebaseOffset"/> before the next write. Set by the writers of
    /// <see cref="AdoptedCanvases"/>; cleared by the rebuild. This is the ONLY thing that may move a
    /// cached offset — nothing per-frame does.
    /// </summary>
    public bool AdoptedOrderRebaseDirty;

    /// <summary>Census, refreshed by each rebase: how many adopted records are rebase-eligible
    /// (conceded — i.e. the ones whose <c>sortingOrder</c> the mod actually writes AND whose
    /// <c>overrideSorting</c> is TRUE, which is the only combination in which a sortingOrder decides
    /// anything at all).</summary>
    public int RebaseEligible;

    /// <summary>Census: how many DISTINCT authored <c>sortingOrder</c>s those eligible canvases had at
    /// adoption. THE decisive number — see the HOW TO READ IT paragraph on the census log line.</summary>
    public int RebaseDistinctOriginals;

    /// <summary>Census: min/max authored order across the eligible set (int.MaxValue/MinValue when the
    /// set is empty).</summary>
    public int RebaseMinOriginal, RebaseMaxOriginal;

    /// <summary>Census: how many eligible canvases ended up at a NON-ZERO offset, i.e. how many are no
    /// longer pinned to the single ModBuild 202 value of <c>host + ConcededOrderLift</c>. Zero means
    /// this lane changed no pixel on this window.</summary>
    public int RebaseLifted;

    /// <summary>Census: how many eligible canvases had their offset CLAMPED into the band (0 in every
    /// scene measured so far — the band is 15 wide and the largest eligible set observed is 1).</summary>
    public int RebaseClamped;

    /// <summary>The band-overflow warning is printed once per panel, not once per rebuild.</summary>
    public bool RebaseClampLogged;

    /// <summary>Census: adopted canvases whose <c>overrideSorting</c> was TRUE at adoption, and how
    /// many carry it right now. The pair separates "the game authored a sorting root here" from "the
    /// mod left one standing"; a canvas with the flag FALSE sorts by hierarchy inside its host's batch
    /// and its <c>sortingOrder</c> is inert, so it can neither tie nor be tied with.</summary>
    public int AdoptedOverrideAtAdoption, AdoptedOverrideNow;

    // ---- dedicated mod layer for floated modals (user #8: UI-Camera double-draw) ----------
    /// <summary>
    /// Opt-in (<see cref="CanvasConversion.Convert"/> <c>useModLayer</c>): this host and its
    /// ENTIRE converted subtree are moved onto <see cref="Core.VRLayers.ModLayer"/> — the
    /// dedicated mod layer that ONLY the HMD head camera renders. The game's mono
    /// <c>UI Camera</c> (cullingMask = UI layer only) can no longer double-draw the
    /// world-space modal, which was the confirmed flicker root cause. Reversible: every
    /// touched transform's original layer is recorded in <see cref="Relayered"/> and
    /// restored on <see cref="CanvasConversion.Release"/>.
    /// </summary>
    public bool ModLayerEnabled;

    /// <summary>Every transform re-layered onto the mod layer, with its original layer (restored on Release).</summary>
    public readonly List<LayerRecord> Relayered = new(64);

    // ---- transparent modal background (user #8 part 2) ------------------------------------
    /// <summary>
    /// Opt-in (<see cref="CanvasConversion.Convert"/> <c>transparentBackground</c>): the
    /// full-window opaque backing/blur image(s) of a floated full-screen menu (ESC /
    /// Options / Results family) are disabled while floated so only the foreground
    /// content (buttons/text/art) shows — it no longer reads as a flat rectangle in space.
    /// Reversible: the disabled graphics are recorded here and re-enabled on Release.
    /// </summary>
    public bool HideBackground;

    /// <summary>Full-screen background graphics we disabled while floated (re-enabled on Release).</summary>
    public readonly List<Graphic> HiddenBackgrounds = new(4);

    /// <summary>
    /// Issue 5 (menu-close VEIL): for the full-screen-menu family (ESC / Options) the disabled
    /// full-window blur/backing must NOT be re-enabled on <see cref="CanvasConversion.Release"/>.
    /// On an X-close the float is released before the game window is guaranteed hidden, and the game
    /// can momentarily re-show the ESC menu in flat 2D — carrying a re-enabled full-window blur that
    /// reads as a translucent veil over the whole view (plus a left-edge stereo-split flicker). The
    /// blur is a flat-screen effect the mod deliberately removes anyway, so for these menus it is
    /// left disabled; the game re-creates/re-enables it on the next genuine flat show. Non-menu
    /// modals (confirmations, story, results) keep the normal restore-on-Release behavior.
    /// </summary>
    public bool KeepBackgroundHidden;

    /// <summary>Next frame the background-hide re-assert sweep runs (pooled/late fades).</summary>
    public int BackgroundSweepNextFrame;

    // ---- item 5 (pause-menu size consistency): one-shot fit layout-settle gate ------------
    /// <summary>Last measured visible-content size while a settled first fit is pending (stability
    /// clock). Shared by BOTH pre-commit settle paths — the one-shot menu fit
    /// (<c>SettleOneShotFit</c>) and the pre-reveal first fit of every other render-hidden host
    /// (<c>SettlePreRevealFirstFit</c>, user ruling 2026-08-02) — which never run on the same
    /// panel (a panel is either one-shot or not).</summary>
    public Vector2 FitOneShotStableSize;

    /// <summary>Consecutive fit checks the measured content size has held steady (see
    /// <c>SettleOneShotFit</c> / <c>SettlePreRevealFirstFit</c>).</summary>
    public int FitOneShotStableCount;

    /// <summary>
    /// First-open size bug (2026-08-02): measured content CENTER of the stability candidate, part
    /// of the settle signature alongside <see cref="FitOneShotStableSize"/>. A partially laid-out
    /// menu can keep its box size while the whole column slides sideways once the layout lands
    /// (hardware: the cold first open measured its content 288 px to the right of every warm
    /// open), and a fit committed there is mis-centered even though its size looked steady.
    /// </summary>
    public Vector2 FitOneShotStableCenter;

    /// <summary>
    /// First-open size bug: how many graphics contributed to the PREVIOUS settle check's measure.
    /// The strongest cheap signal that content is still arriving — an element that becomes real
    /// changes the count immediately, even when it sits inside the current bounding box and
    /// therefore moves neither the size nor the center. Compared per check (it must not reset the
    /// size/center streak: a pulsing element toggles it forever without moving the bounds).
    /// </summary>
    public int FitOneShotStableGraphics;

    /// <summary>Total settle checks run before the first fit committed — reported by the fit log
    /// so a hardware log can compare a cold first open against a warm re-open.</summary>
    public int FitSettleChecks;

    /// <summary>
    /// Round 3: consecutive settle checks the measurement held ABSOLUTELY still (within
    /// <c>CanvasConversion.SettleStillEpsilonPx</c>), as opposed to within the 2 % relative
    /// tolerance <see cref="FitOneShotStableCount"/> uses. The strict settle tier requires both:
    /// 2 % of a small cold measure is several pixels per check, which let genuinely drifting
    /// content pass as steady for a whole streak.
    /// </summary>
    public int FitSettleStillCount;

    /// <summary>
    /// How many settle checks saw the forced layout flush CHANGE the measurement (i.e. the layout
    /// still had work pending). Expected to be &gt;0 on a session's first open of a window and 0 on
    /// every later one — that difference IS the first-open bug, so the fit log reports it.
    /// </summary>
    public int FitSettleRebuildChanges;

    // ---- ROUND 6: the show animation that never lands --------------------------------------
    /// <summary>
    /// Consecutive settle checks whose measure found the window away from its authored geometry
    /// (rendered/authored ratio materially below 1) WITHOUT the ratio improving — i.e. DRIFTED, not
    /// animating. A healthy show animation moves the ratio every single frame; the cold ESC menu sat
    /// at a constant 0.21/0.15 across the whole pre-reveal budget. At
    /// <c>CanvasConversion.GeometryDriftChecks</c> the chain is dumped and the conversion frame
    /// re-asserted (see <c>CanvasConversion.TickGeometryDrift</c>).
    /// </summary>
    public int FitAnimStalledChecks;

    /// <summary>Ratio (rendered/authored) of the previous settle check — the drift detector's
    /// reference (see <see cref="FitAnimStalledChecks"/>).</summary>
    public Vector2 FitAnimLastRatio = Vector2.one;

    /// <summary>True once this panel has logged a conversion-frame drift correction (the loud line
    /// is worth exactly once per panel; every applied fit still carries the short per-apply note).
    /// See <c>CanvasConversion.ReassertConversionFrame</c>.</summary>
    public bool FrameDriftLogged;

    /// <summary>How many ancestor/depth chain dumps this panel has emitted — capped by
    /// <c>CanvasConversion.FrameChainDumpCap</c> (it is a multi-line dump).</summary>
    public int FrameChainDumps;

    // ---- task #4 (world-space scroll clipping) --------------------------------------------
    /// <summary>
    /// Task #4 (scrolling extended the menu upward): <see cref="RectMask2D"/> components WE
    /// added to mask-less ScrollRect viewports inside the converted subtree, destroyed on
    /// Release. In 2D a full-screen submenu's scroll list is clipped by the SCREEN edge, so
    /// the prefab can ship without a viewport mask; on a world-space host there is no screen
    /// edge and rows scrolled past the viewport rendered above/below the window — the menu
    /// visually "elongated" instead of clipping. Same pattern as <c>WorldTooltips</c>' frame
    /// mask.
    /// </summary>
    public readonly List<RectMask2D> AddedScrollMasks = new(2);

    /// <summary>Task #4: game-owned but DISABLED RectMask2D clippers we enabled while converted
    /// (re-disabled on Release).</summary>
    public readonly List<RectMask2D> EnabledScrollMasks = new(2);

    // ---- initial-flicker settle window (sub-item A) ---------------------------------------
    /// <summary>
    /// EVERY-FRAME settle deadline (unscaled time) after Convert for a floated modal host:
    /// until this passes, the mod-layer move, the nested-canvas adoption AND the
    /// background-hide re-run every frame instead of only on the ~0.4 s periodic sweep. Root
    /// cause of the reported ~1 s initial flicker (background visible, then gone): the game
    /// instantiates / fades in the full-window backing a few frames AFTER Convert, so on the
    /// periodic schedule the untreated opaque backing rendered on the game UI layer (double-
    /// drawn by the mono UI Camera → flicker) and unhidden for up to half a second before a
    /// sweep caught it. Re-treating every frame through the fade-in makes the very first
    /// visible frame already transparent + on the mod layer. Zero when the host opted out of
    /// both treatments.
    /// </summary>
    public float EarlySettleUntil;

    // ---- render-hidden-until-treated reveal (sub-item A, the DECISIVE initial-flicker fix) ----
    /// <summary>
    /// Item 3a: a floated modal host is created with its <see cref="Canvas"/> DISABLED so the
    /// untreated opaque backing NEVER draws a frame. The host stays render-hidden until
    /// <see cref="RevealNotBefore"/> passes — by then the mod-layer move + background hide have
    /// been applied and re-applied over several frames, so the very first VISIBLE frame is already
    /// on layer 27 with its backing gone. A ~0.15 s pop-in with ZERO flicker replaces the ~1 s
    /// backing flash. Cleared (and the canvas enabled) by <see cref="CanvasConversion.Tick"/>.
    /// </summary>
    public bool RevealPending;

    /// <summary>Earliest unscaled time the render-hidden modal host may be revealed (see <see cref="RevealPending"/>).</summary>
    public float RevealNotBefore;

    // ---- final-pose reveal settle (user ruling 2026-08-02: no post-reveal pose/scale jump) ----
    /// <summary>
    /// Unscaled time the reveal gate was armed (Convert). WHY: the user ruling demands the window
    /// become visible only at its FINAL pose and scale — the reveal log reports how long that
    /// settle took, measured from here, so a hardware log can prove the gate's timing.
    /// </summary>
    public float RevealRequestedAt;

    /// <summary>
    /// Hard reveal deadline (unscaled). Past this the host is shown even when the settle criteria
    /// (first fit + stable pose) were never met — a window must NEVER stay invisible (an
    /// unmeasurable, still-animating window would otherwise be an un-dismissable invisible
    /// blocker). A Warn line names what was still pending.
    /// </summary>
    public float RevealDeadline;

    /// <summary>
    /// THE POSE-CRITICAL GRACE HAS ALREADY BEEN GRANTED TO THIS PANEL (ModBuild 250) — a latch, so
    /// the extension below is a ONE-SHOT and never a per-frame push that would make
    /// <see cref="RevealDeadline"/> unreachable.
    ///
    /// <para><b>THE BUG IT ANSWERS</b>, from <c>.planning/debug/LogOutput.log</c>: <c>UI Event
    /// Window</c> was FORCED visible after 601 ms with <c>fit=pending</c>, so its pose had been
    /// computed from the PRE-fit rect; the one pre-reveal re-place then arrived and was permanently
    /// REFUSED by the pose lock, because a revealed window is never moved. The window therefore
    /// stood, for the rest of its life, at a pose derived from a rect it no longer had — on the
    /// sibling window that same correction was worth 0.260 m.</para>
    ///
    /// <para><b>WHY THE DEADLINE MOVES AND THE WINDOW DOES NOT.</b> Two rulings collide here:
    /// "a window must never stay invisible" (why the forced reveal exists) and "einmal gespawned
    /// sind sie fix" (why the pose lock refuses). Only the FIRST is a ruling about a bound — it is
    /// satisfied by any finite one, and 0.6 s is a chosen number, not the ruling. The second is a
    /// ruling about a POSE THE PLAYER CAN SEE, and it is absolute: a shared window that jumps after
    /// it is visible jumps in front of every player at once. So the pose-critical case buys time
    /// while it is still render-hidden, and never a millimetre after. If even the extended bound
    /// expires the old behaviour stands unchanged, and the reveal line says so.</para>
    ///
    /// <para>SCOPE: set only where a SHARED window's spawn pose was taken from the shared table
    /// anchor while its content fit had not yet measured — the one case in which a stale rect moves
    /// an identity-owned slot that every client is looking at. Every other window keeps the 0.6 s
    /// bound exactly as it was.</para>
    /// </summary>
    public bool RevealPoseCriticalGraceGiven;

    /// <summary>One-shot latch for the "nothing was ever measurable" warning on the PRE-REVEAL first
    /// fit — see <c>CanvasConversion.SettlePreRevealFirstFit</c>. Separate from
    /// <see cref="FitGaveUpLogged"/> so the two families' one-shots can never consume each
    /// other's.</summary>
    public bool FitNeverMeasurableLogged;

    /// <summary>
    /// THIS HOST'S POSE IS WRITTEN EVERY FRAME BY SOMEONE ELSE (ModBuild 184) — so the reveal
    /// gate's stillness criterion is meaningless for it and must be skipped.
    ///
    /// <para>Set for the map room's HOVER CARDS: <c>ModalFallback.TickHoverCards</c> writes the
    /// host's position and rotation on every tick so the card flies over the icon under the
    /// pointer. The gate (see <c>CanvasConversion.TickRevealGate</c>) waits for the host to hold
    /// still for <c>RevealStableFrames</c> consecutive checks, and a pose that is rewritten every
    /// frame — from a head-relative direction, no less — resets that counter every frame. The
    /// counter can therefore NEVER reach its target, and the card only ever became visible at the
    /// 0.6 s <see cref="RevealDeadline"/>, by which time most hovers are already over. The 183
    /// hardware log states it plainly: every MODAL DIAG line for 'UI Quest Preview Popup' reads
    /// <c>canvas.enabled=False</c>, and the draw-order line labels it "(hidden: reveal gate)".</para>
    ///
    /// <para>Only criterion 3 is dropped. Treatment (mod layer / backing) and the content fit
    /// still gate the reveal, so the card is still never seen in its untreated or unfitted state —
    /// which is the whole point of the gate. What is dropped is a test that asks a question this
    /// host cannot answer.</para>
    /// </summary>
    public bool PoseOwnedExternally;

    /// <summary>True once a pose snapshot exists — the first tracked frame has nothing to compare against.</summary>
    public bool RevealHasSnapshot;

    /// <summary>Host world position at the previous reveal-gate check (pose-stability tracking).</summary>
    public Vector3 RevealLastPos;

    /// <summary>Host world rotation at the previous reveal-gate check.</summary>
    public Quaternion RevealLastRot;

    /// <summary>Host lossy scale at the previous reveal-gate check (catches the one-shot scale re-derivation).</summary>
    public Vector3 RevealLastScale;

    /// <summary>Host rect size (px) at the previous reveal-gate check (catches the content fit resize).</summary>
    public Vector2 RevealLastRectSize;

    /// <summary>Consecutive reveal-gate checks the host pose/scale/rect held still (reset to 0 on any change).</summary>
    public int RevealPoseStableFrames;

    // ---- ROUND 6 (LEFT-EYE FLICKER): one frame phase for every visibility flip ---------------
    /// <summary>
    /// The Update-phase reveal gate DECIDED to show this window; the actual visibility flip is
    /// deferred to <c>CanvasConversion.LateTick</c> (LateUpdate). WHY the split: MultiPass renders
    /// the head camera once per eye AFTER every Update and LateUpdate, so LateUpdate is the last
    /// frame phase that is provably identical for both eye passes — while Update is followed by
    /// several mod systems (MrBacking's plate, the game's own scripts) that can still move, resize
    /// or re-activate parts of the window. Flipping in Update therefore exposed the first visible
    /// frame to writers that had not run yet; flipping in LateUpdate does not.
    /// </summary>
    public bool RevealArmed;

    /// <summary>Whether the armed reveal met all settle criteria (false = the deadline forced it) —
    /// carried from the Update-phase decision to the LateUpdate-phase flip so the reveal log is
    /// unchanged in content.</summary>
    public bool RevealArmedSettled;

    /// <summary>Last unmet settle criterion (treatment/fit/pose) — the reveal log names what the gate waited for last.</summary>
    public string RevealLastBlocker = "";

    // ---- one-shot pose re-place at final geometry (first-open pose fix, 2026-08-02) ----------
    /// <summary>
    /// True when <c>ModalFallback.TickPoseRePlace</c> actually MOVED this host while it was still
    /// render-hidden, because the spawn placement had been computed from the PRE-fit rect/scale.
    /// Reported by the MODAL REVEAL line so a hardware log proves which pose the first visible
    /// frame shows and where it came from.
    /// </summary>
    public bool PoseRePlaced;

    /// <summary>Host world position before the re-place (only meaningful with <see cref="PoseRePlaced"/>).</summary>
    public Vector3 PoseRePlacedFrom;

    /// <summary>Host world position after the re-place (only meaningful with <see cref="PoseRePlaced"/>).</summary>
    public Vector3 PoseRePlacedTo;

    /// <summary>Human-readable outcome of the ONE re-place evaluation — either that it moved, or
    /// WHY it did not (verbatim chain pose, grabbed, already revealed, pose unchanged). Written
    /// once per open by ModalFallback and read only by the reveal log.</summary>
    public string PoseRePlaceReason = "not evaluated (window not enrolled in the pose re-place)";

    // ---- COMPLETE render hide (user ruling 2026-08-02 round 2: nothing may pop in elsewhere) ----
    /// <summary>
    /// True while the panel is fully render-hidden by
    /// <see cref="CanvasConversion.SetPanelRenderVisible"/>. WHY a panel-level flag and not just
    /// <c>HostCanvas.enabled</c>: a floated window is drawn by far more than its host canvas —
    /// nested <see cref="Canvas"/> components (adopted game canvases, the mod X's own draw/hit
    /// canvases) are independent render roots, and the grab bar and the MR backing plate are
    /// <see cref="Renderer"/>s that the uGUI canvas path never touches at all. One of those (the
    /// grab bar) does not even live under the host — it hangs off the mod-owned
    /// <see cref="GrabbableModal"/> holder, see
    /// <see cref="ExtraRenderRoots"/>. Consumers that must not act on an invisible window
    /// (grab affordances, MR plates) read THIS instead of guessing from the canvas — and, since
    /// 2026-08-08, <see cref="OwnerRenderHidden"/> alongside it: the panel is invisible if EITHER
    /// hide is in force, and each has its own owner and its own restore set.
    /// </summary>
    public bool RenderHidden;

    /// <summary>
    /// True while the OWNING SURFACE has render-hidden this panel for a reason of its OWN — today
    /// only the character focus ("one character owns a decision", user ruling 2026-08-08:
    /// <c>DecisionDockSurface.ApplyFocusHide</c> and <c>UseBarsSurface.Dock.ApplyFocusHide</c> hide
    /// a decision row / use bar whose owner is not the character the player is looking at).
    ///
    /// <para>WHY IT IS A SECOND FLAG AND NOT <see cref="RenderHidden"/>. That flag is owned, start
    /// to finish, by the CONVERSION's reveal-gate lifecycle: it is set by
    /// <see cref="CanvasConversion.SetPanelRenderVisible"/> only, from
    /// <c>CanvasConversion.1.Core</c> (Convert arms the gate) and <c>CanvasConversion.4.Lifecycle</c>
    /// (the per-frame re-apply, the reveal, Release). Those calls are LEVEL writes, not a counter:
    /// the gate's own <c>SetPanelRenderVisible(panel, true)</c> would silently lift a surface's
    /// focus hide, and the gate's per-frame re-hide would fold the surface's components into
    /// <see cref="HiddenCanvases"/>/<see cref="HiddenRenderers"/> where the reveal — not the
    /// surface — would later switch them back on. Two owners, one flag, and the loser is whichever
    /// wrote first. A separate flag makes the two hides ORTHOGONAL: each keeps its own recorded
    /// restore set, and every consumer that must not act on an invisible panel simply reads BOTH
    /// (MR backing plate, grab affordance — see <see cref="MrBacking"/> and
    /// <see cref="GrabbableModal"/>).</para>
    ///
    /// <para>The two never actually overlap on today's surfaces: the reveal gate is armed only for
    /// the floated-modal family (<c>Convert</c> arms it when the mod layer or the transparent
    /// background is requested), and the docked decision row / use bars are converted by
    /// <c>WorldSurface</c> without either. The orthogonality above is what keeps that a
    /// coincidence rather than a load-bearing assumption.</para>
    /// </summary>
    public bool OwnerRenderHidden;

    /// <summary>
    /// Mod-drawn trees that belong to this window but are NOT children of the host: today the
    /// <see cref="GrabbableModal"/> holder (the grab bar), which is a scene-root
    /// GameObject the host merely follows. The render hide walks these exactly like the host
    /// subtree, so "everything belonging to the window" really means everything.
    /// </summary>
    public readonly List<Transform> ExtraRenderRoots = new(2);

    /// <summary>
    /// Canvases THIS panel's render hide turned off, in the order they were turned off. Only
    /// components that were <c>enabled == true</c> at hide time are recorded, so the restore
    /// re-enables exactly what the hide disabled and can never switch on something that was
    /// deliberately off (a game-disabled sub-canvas, a closed option tab).
    /// </summary>
    public readonly List<Canvas> HiddenCanvases = new(8);

    /// <summary>Frame on which this panel's render hide was last lifted (the reveal). Read by the
    /// hidden-window veil so a veil that lands on the reveal frame can say so — that coincidence
    /// is the mechanism the ModBuild 401 note describes.</summary>
    public int LastRevealFrame = -1;

    /// <summary>
    /// ONE FLAG PER ENTRY OF <see cref="HiddenCanvases"/>, in lockstep with it: was the
    /// <c>UIWindow</c> ON THAT CANVAS'S OWN GameObject still PRE-START when this panel's hide
    /// recorded it? (No <c>UIWindow</c> there at all, or one that had already run its
    /// <c>Start()</c>, both record <c>false</c>.)
    ///
    /// <para>WHY THE FLAG EXISTS, and it is the whole of ModBuild 395's fix. The hide's
    /// exact-restore contract — "only components that were enabled at hide time are recorded, so
    /// the restore can never switch on something that was deliberately off" — rests on the
    /// recorded <c>true</c> being a DECISION by the game. For a window whose <c>Start()</c> has
    /// not run yet it is not a decision, it is the PREFAB's authored default: <c>UIWindow.Start</c>
    /// is the method that first drives the window to its starting visual state (decompiled
    /// UIWindow.cs:358-372), and for a <c>_disableCanvas</c> window that state is written as
    /// <c>_canvas.enabled</c> in <c>OnTransitionStarted</c>. A panel converted at prefab state is
    /// therefore recorded with EVERY sub-view canvas "enabled", the game decides moments later
    /// (while our hide already holds them off, so its write is a no-op it cannot repeat), and the
    /// reveal then replays the prefab default over the game's verdict — which is the screen the
    /// user photographed: the delete-character confirmation, the mercenary-create screen and
    /// dozens of un-populated labels all painting at once.</para>
    ///
    /// <para>Entries flagged <c>false</c> are restored exactly as before — the contract still
    /// holds wherever the recorded value really was a decision, so a window that was fading out
    /// when the hide ran keeps today's behaviour to the bit.</para>
    /// </summary>
    public readonly List<bool> HiddenCanvasWasPreStart = new(8);

    /// <summary>Renderers this panel's render hide turned off — same exact-restore contract as
    /// <see cref="HiddenCanvases"/> (grab bar, MR backing plate).</summary>
    public readonly List<Renderer> HiddenRenderers = new(8);

    // ---- MR backing plate opt-out (user ruling 2026-08-04) ---------------------------------
    /// <summary>
    /// True for a converted panel that must NEVER receive the mixed-reality backing plate
    /// (<see cref="MrBacking"/> sweeps <see cref="CanvasConversion.ActivePanels"/> and plates
    /// EVERY live panel by default — over-coverage is harmless behind an opaque window, but two
    /// panel families proved the exception and the user ruled them out explicitly, 2026-08-04:
    /// <list type="bullet">
    /// <item>ACTOR HEALTH BARS (<see cref="ActorBars"/>): the adopted
    /// <c>WorldspacePanelUIController</c> host rect is far larger than the thin bar band actually
    /// drawn in it, so its host-rect plate rendered as a solid dark RECTANGLE floating over the
    /// miniature / through the middle of the health bar. The bar reads fine against the real room
    /// (it sits over the board anyway), so it gets no plate at all.</item>
    /// <item>THE FIGURE-GRAB INFO PANELS (<see cref="Surfaces.StatPanelSurface"/>): the
    /// actor/enemy stat card shown while a figure is held (and the same card on a miniature poke —
    /// it is one and the same window, so the exclusion follows the SURFACE, not the trigger)
    /// carries the game's own parchment card art as backing; a host-rect plate behind it only
    /// added a dark border proud of the card. User: "hier wird das nicht gebraucht".</item>
    /// </list>
    /// Set at Convert/Adopt time by the owning surface — per conversion, so the constant
    /// create/destroy churn of bars and stat-card hysteresis releases re-applies it on every new
    /// <see cref="ConvertedPanel"/> instance. No config knob on purpose: the comparable per-panel
    /// exclusions in this codebase (non-pokeable, flatten2D, transparentBackground) are
    /// hard-coded architectural decisions of the owning surface, and the user's ruling is the
    /// reason of record.
    /// </summary>
    public bool MrBackingSuppressed;

    // ---- per-frame distance draw order (CanvasConversion.8.Order.cs) -----------------------
    //
    // Replaces the per-host depth-compose mask this class used to carry
    // (HostDepthMaskSuppressed / HostDepthMask / HostDepthMaskMesh / HostDepthMaskHash /
    // HostMaskInkDiagNextAllowed). A depth stamp is a per-QUAD statement and a panel's
    // transparency is per PIXEL, so the stamp could only ever trade a hole for a smaller hole
    // (hardware: the ink measure removed 2 % of the initiative track's stamp and 10 % of an actor
    // bar's). Panels now write NO depth at all and are painted far to near instead - see the file
    // header of CanvasConversion.8.Order.cs for the full derivation.

    /// <summary>
    /// The <see cref="Canvas.sortingOrder"/> this panel was CONVERTED with (0 for HUD hosts, 1000
    /// for the modal/at-hand tier). Two consumers, neither of which may see the live ladder value:
    /// UguiPointer's cross-raycaster tie-break (<see cref="CanvasConversion.BaseSortingOrderOf"/>),
    /// and the ladder's own insertion tie-break for two panels the player cannot tell apart in
    /// depth - which is the last thing <c>ModalFallback.ModalHostSortingOrder</c> still decides.
    /// </summary>
    public int BaseSortingOrder;

    /// <summary>The ladder order currently written onto <see cref="HostCanvas"/> (and, offset, onto
    /// every <see cref="OrderFollowers"/> entry). Reported by the PANEL DRAW ORDER diagnostic.</summary>
    public int DrawSortingOrder;

    /// <summary>Metres from the eye to the CLOSEST POINT of this panel's rect at the last order
    /// pass (see <c>CanvasConversion.PanelEyeDistance</c> for why not the centre).</summary>
    public float OrderDistance;

    /// <summary>True while this panel sits in the persistent far-to-near sequence
    /// (<c>CanvasConversion.OrderedPanels</c>). Cleared by Release and the dead-panel prune, which
    /// is how the order pass drops it without a set lookup.</summary>
    public bool OrderListed;

    // ---- WHERE THE WINDOW WAS LAST DRAWN (the stray-dissolve rule, 2026-09-03) ----------------
    // USER REPORT: at scenario start in the map room the DISSOLVE dust appeared somewhere else
    // entirely, where no window was (any more). ModBuild 410 log: 'New Party display' went DORMANT
    // (EMPTY WINDOW HIDDEN, :4257) and stayed dark for the rest of its float; the story curtain
    // released it (:4423) and the vanish played 900 shards over its empty seat (:4424-:4459),
    // 35 degrees to the right of the story window the player was looking at. A VANISH may only
    // play where a window was DRAWN on the previous frame, and it must spawn from THAT pose — so
    // the last LateUpdate in which this panel was render-visible is stamped here, with the host's
    // world pose at that moment. Written by CanvasConversion.TickPanelOrder (the last WorldUI
    // LateUpdate step) and read by WindowMaterialise.PlayOut in the NEXT frame's Update: a stamp
    // equal to Time.frameCount - 1 there means "drawn on the previous frame".
    /// <summary><c>Time.frameCount</c> of the last order pass that found this panel render-visible
    /// (no reveal gate, no render hide, host active and its canvas on). 0 = never.</summary>
    public int LastShownFrame;

    /// <summary>Host world position at <see cref="LastShownFrame"/>.</summary>
    public Vector3 LastShownPosition;

    /// <summary>Host world rotation at <see cref="LastShownFrame"/>.</summary>
    public Quaternion LastShownRotation = Quaternion.identity;

    /// <summary>Host lossy scale at <see cref="LastShownFrame"/>, so a debris cloud spawned from
    /// the stamped pose can undo a scale change made since.</summary>
    public Vector3 LastShownLossyScale = Vector3.one;

    /// <summary>What released this float, stamped by the release loop BEFORE the float leaves
    /// <c>ModalFallback.Converted</c>, so the vanish can name its trigger. Empty until released.
    /// </summary>
    public string ReleaseTrigger = string.Empty;

    /// <summary>The release loop's own verdict on whether this float has anything on the screen to
    /// dissolve, taken while the float was still in <c>ModalFallback.Converted</c> (the two
    /// lifetime terms: APPEAR still owed, or DORMANT). Empty = there is something to dissolve.
    /// WHY IT IS STAMPED: <c>ModalFallback.HasNothingToDissolve</c> searches <c>Converted</c> for
    /// the float, and the release loop removes it from that list BEFORE calling
    /// <c>WindowMaterialise.PlayOut</c> — so from ModBuild 374 to 410 the rule found nothing and
    /// answered "dissolve it" for every dormant window it was written to refuse (0 VANISH SKIPPED
    /// lines in any log since). The stamp survives the removal.</summary>
    public string ReleaseNothingToDissolveWhy = string.Empty;

    /// <summary>The successor this panel currently wants to swap places with, and for how many
    /// consecutive frames it has wanted that. BOTH gates of the anti-flicker hysteresis: a swap
    /// needs a distance disagreement beyond the margin AND that same peer for a whole streak, so
    /// head micro-motion (sub-millimetre, sub-frame) can never reorder anything.</summary>
    public ConvertedPanel? OrderSwapPeer;

    /// <summary>See <see cref="OrderSwapPeer"/>.</summary>
    public int OrderSwapStreak;

    /// <summary>Mod-owned canvases/renderers that must ride this panel's ladder order at a fixed
    /// offset — the close X, the grab bar. Registered via
    /// <see cref="CanvasConversion.RegisterOrderFollower(ConvertedPanel, Canvas, int)"/>; entries
    /// are pruned when their object dies, so they need no teardown of their own.</summary>
    public readonly List<OrderFollower> OrderFollowers = new(4);

    /// <summary>
    /// Identity of the BOARD this panel is currently DOCKED on — the <c>PlayTray</c> instance,
    /// i.e. the very object that anchors that board's furniture group
    /// (<see cref="IFurnitureOrderAnchor"/>). Null while the panel floats. Written by the dock
    /// placement paths (TrayMountedPanelSurface, DecisionDockSurface, UseBarsSurface,
    /// DamageTooltipSurface) every Place, cleared on their floating fallbacks.
    ///
    /// WHY (user report 2026-08-04 #2: initiative portraits blending with the Statustafel text,
    /// popping with viewing angle): a board-docked panel and the board's own transparent furniture
    /// are effectively COPLANAR, so their measured eye distances sit within (or straddle) the
    /// ladder's swap margin and which one "wins" flips as the head orbits. Distance must not
    /// arbitrate inside the board's own plane — the furniture rank pass
    /// (<c>CanvasConversion.TickFurnitureOrder</c>) reads this tag to keep the board's furniture
    /// STRUCTURALLY below every panel docked on the same board, at every angle, while distance
    /// continues to rank the whole board cluster against everything else.
    /// </summary>
    public object? OrderCluster;
}

/// <summary>
/// A transform moved onto the mod layer for a floated modal (see
/// <see cref="ConvertedPanel.ModLayerEnabled"/>) — its original layer, restored on Release.
/// </summary>
internal struct LayerRecord
{
    public Transform Transform;
    public int OriginalLayer;
}

/// <summary>
/// A game-owned nested <see cref="Canvas"/> inside a converted subtree, adopted by
/// <see cref="CanvasConversion.AdoptNestedCanvases"/> — everything needed to restore
/// its exact pre-conversion state on Release.
/// </summary>
internal struct NestedCanvasRecord
{
    public Canvas Canvas;
    public bool OriginalOverrideSorting;
    public Camera? OriginalWorldCamera;

    /// <summary>Raycaster added by us (destroyed on Release); null when the canvas already had one.</summary>
    public GraphicRaycaster? AddedRaycaster;

    /// <summary>
    /// Task #7 (dropdowns): TRUE for a transient uGUI-Dropdown overlay canvas — the
    /// "Dropdown List" the Dropdown spawns inside the subtree and the fullscreen
    /// "Blocker" it parents under the ROOT (host) canvas. These MUST keep
    /// <c>overrideSorting</c> so they render ON TOP of the whole menu (that is their
    /// entire purpose); clearing it — the generic adoption behavior — dropped the open
    /// list to host order at its hierarchy position, i.e. BEHIND siblings drawn later,
    /// and the "vanished" list then blocked re-opening (TMP_Dropdown.Show is a no-op
    /// while <c>m_Dropdown != null</c>). See <see cref="CanvasConversion.AdoptCanvas"/>.
    /// </summary>
    public bool KeepOverrideSorting;

    /// <summary>Sorting order re-asserted while adopted (only when <see cref="KeepOverrideSorting"/>).</summary>
    public int OverlaySortingOrder;

    /// <summary>
    /// CONCEDED (ModBuild 179): the game re-asserts <c>overrideSorting = true</c> on this canvas
    /// every frame and will not be argued out of it. When set, the adoption stops writing the FLAG
    /// and owns the NUMBER instead — <c>sortingOrder</c> follows the host's live order, so the
    /// subtree still draws exactly where the host draws while the game's own writer is left
    /// satisfied. Implies <see cref="KeepOverrideSorting"/>.
    ///
    /// <para>WHY IT EXISTS. The 3D map room's party window carries a tooltip canvas whose game-side
    /// writer sets the flag in its own per-frame pass. The guard cleared it in LateUpdate, the game
    /// set it again next Update, and neither won: the canvas's sorting state alternated every
    /// frame, which in MultiPass means the two eye passes could disagree — the user's report was
    /// *"flackert stark"* and 18,994 of that session's 20,173 log lines were the guard's own
    /// re-clear notice. A write war with the game is never won by writing harder.</para>
    /// </summary>
    public bool ConcededOverrideSorting;

    /// <summary>The canvas's own sortingOrder at adoption — restored on release for any canvas
    /// whose order we took over (see <see cref="ConcededOverrideSorting"/>).</summary>
    public int OriginalSortingOrder;

    /// <summary>Frames this canvas has been caught with the flag flipped back on. Concedes at
    /// <see cref="CanvasConversion.ConcedeAfterReclears"/>.</summary>
    public int ReclearCount;

    /// <summary>
    /// ModBuild 203 (sibling rebase): this canvas's depth-first position among the canvases
    /// <see cref="CanvasConversion.AdoptNestedCanvases"/> found inside the converted subtree, captured
    /// ONCE at adoption. <c>GetComponentsInChildren</c> returns pre-order depth-first, so the sweep's
    /// own loop index IS the hierarchy index — no second walk, no per-frame query.
    ///
    /// <para>WHY IT EXISTS. It is the fallback tiebreaker for
    /// <see cref="RebaseOffset"/>: when two adopted canvases carry the SAME authored
    /// <see cref="OriginalSortingOrder"/>, the flat game resolved them by hierarchy (that is what uGUI
    /// does for a canvas that is not its own sorting root), so the rebase must resolve them the same
    /// way rather than leaving a tie for Unity's canvas registration order to break — that order is
    /// undefined and is re-rolled on every enable/disable. Canvases adopted LATE (pooled rows, a
    /// dropdown list) get an index past the initial sweep's range; they are appended, which is
    /// deterministic, which is the whole requirement.</para>
    /// </summary>
    public int DfsIndex;

    /// <summary>
    /// ModBuild 203: the cached, stable offset this canvas's <c>sortingOrder</c> is written at, ABOVE
    /// <c>host + CanvasConversion.ConcededOrderLift</c>. Dense (0..K−1 over the K rebase-eligible
    /// records of this panel) and clamped — see
    /// <c>CanvasConversion.RebuildConcededOrderOffsets</c> for the derivation, the bound and why the
    /// bound is what it is.
    ///
    /// <para>CACHED, NEVER DERIVED PER FRAME. The per-frame guard must write the same number for the
    /// same canvas on every single run: a value re-derived from a live query (a min over a list that
    /// changes as canvases are adopted and pruned) would make the written order oscillate, which is
    /// the exact failure mode — a canvas whose order changes between two MultiPass eye passes — that
    /// the concession machinery exists to end. Recomputed only when the eligible SET changes
    /// (<see cref="ConvertedPanel.AdoptedOrderRebaseDirty"/>).</para>
    /// </summary>
    public int RebaseOffset;
}

/// <summary>
/// A transform inside a flattened subtree (test #21) that carried real 3D — its
/// original local rotation and z, restored by <see cref="CanvasConversion.Release"/>
/// (x/y stay live: the game animates those and they were never touched).
/// </summary>
internal struct FlattenRecord
{
    public Transform Transform;
    public float OriginalLocalZ;
    public Quaternion OriginalLocalRotation;
}
