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
    public bool FitOneShot;

    /// <summary>Set true the frame a one-shot fit actually RESIZED the host — the owning modal
    /// then re-derives its board-relative scale from the now-fitted width (item 1).</summary>
    public bool FitOneShotApplied;

    /// <summary>True once the give-up warning was logged (log hygiene).</summary>
    public bool FitGaveUpLogged;

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
    /// <summary>Last measured visible-content size while a one-shot fit settles (stability clock).</summary>
    public Vector2 FitOneShotStableSize;

    /// <summary>Consecutive fit checks the measured content size has held steady (see <c>SettleOneShotFit</c>).</summary>
    public int FitOneShotStableCount;

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

    // ---- per-host depth compose (initiative portraits blended with a floated menu) --------
    /// <summary>
    /// Set by <see cref="GrabbableModal.Build"/> when the modal already owns a coplanar depth
    /// mask of its own (the pause/options/confirmation/results family): the central per-host
    /// mask (<see cref="CanvasConversion.TickHostDepthMask"/>) then stays off — two coplanar
    /// depth writers on one plane would only double the per-frame graphic walk for zero visual
    /// difference. Every other host (converted HUD panels, un-masked floated windows) gets the
    /// central mask.
    /// </summary>
    public bool HostDepthMaskSuppressed;

    /// <summary>Depth-compose mask root under <see cref="HostRect"/> (see
    /// <see cref="CanvasConversion.TickHostDepthMask"/>); null until first built.</summary>
    public Transform? HostDepthMask;

    /// <summary>The mask's dynamic per-graphic quad mesh — an ASSET, freed explicitly when the
    /// host dies (Unity never garbage-collects Mesh objects with the GameObject).</summary>
    public Mesh? HostDepthMaskMesh;

    /// <summary>Quantized hash of the last emitted mask rect set (rebuild gate — sub-pixel
    /// jitter never rebuilds, any real content change does).</summary>
    public int HostDepthMaskHash;
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
