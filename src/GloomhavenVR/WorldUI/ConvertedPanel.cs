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

    // ---- re-fit churn damping (test #17; see FitHostToContent) -------------------------
    /// <summary>Time of the last APPLIED fit (shrink/re-center rate limit).</summary>
    public float FitLastApplied;

    /// <summary>Pending shrink/re-center candidate size; zero when none.</summary>
    public Vector2 FitPendingSize;

    /// <summary>Time the pending candidate was first measured (stability clock).</summary>
    public float FitPendingSince;

    /// <summary>Host transform for placement by the owning surface.</summary>
    public Transform HostTransform => HostGo.transform;

    /// <summary>True while the moved rect still exists (scene not unloaded).</summary>
    public bool IsAlive => Target != null;

    // ---- floated-modal FLICKER instrumentation (targeted; only modal hosts opt in) -------
    /// <summary>
    /// Opt-in per-frame diagnostics for the floated-modal flicker hunt (set by
    /// <see cref="CanvasConversion.Convert"/> <c>diagnostic</c>, true for ModalFallback
    /// hosts only — the small content-fit panels never flicker and would only spam).
    /// Drives <see cref="CanvasConversion.DiagnoseModal"/>: a change-gated snapshot of the
    /// host + adopted-child render state, plus a camera scan that reveals a second camera
    /// double-drawing the modal's UI layer.
    /// </summary>
    public bool Diagnostic;

    /// <summary>Last-logged per-frame host/child snapshot (change-gated — logs only on churn).</summary>
    public string? DiagLastSnapshot;

    /// <summary>Last-logged camera scan (change-gated — cameras rarely change).</summary>
    public string? DiagLastCameras;

    /// <summary>Convert time (unscaled) — the snapshot reports host age so a re-place is obvious.</summary>
    public float DiagConvertedAt;

    /// <summary>Next frame the (allocating) camera scan runs — throttled; cameras change rarely.</summary>
    public int DiagNextCameraScanFrame;

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
    /// (grab affordances, MR plates) read THIS instead of guessing from the canvas.
    /// </summary>
    public bool RenderHidden;

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

    /// <summary>Renderers this panel's render hide turned off — same exact-restore contract as
    /// <see cref="HiddenCanvases"/> (grab bar, MR backing plate).</summary>
    public readonly List<Renderer> HiddenRenderers = new(8);

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
