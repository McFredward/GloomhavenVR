using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using TMPro;
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

    /// <summary>Next frame the background-hide re-assert sweep runs (pooled/late fades).</summary>
    public int BackgroundSweepNextFrame;

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

    // ---- render-on-top (floated-MODAL sky/diorama occlusion fix) --------------------------
    /// <summary>
    /// Opt-in (<see cref="CanvasConversion.Convert"/> <c>renderOnTop</c>, floated MODAL windows
    /// only): every uGUI graphic in the converted subtree is switched to ZTest Always so the modal
    /// renders OVER all opaque geometry and can NEVER be occluded. The enclosing scenario backdrop
    /// ('GH_SkySphere', shader 'AMP_SkyShader') writes depth and exposes no <c>_ZWrite</c> to
    /// toggle, so a world-space menu (uGUI ZTests LEqual) dragged to the shell edge otherwise clips
    /// behind it — this keeps the sky rendered and lifts the menu above it instead of disabling the
    /// sky. Reversible: the game reparents these SAME graphics back to their 2D home on Release, so
    /// each graphic's original material / ZTest state is recorded in <see cref="RenderOnTopGraphics"/>
    /// and restored — the 2D UI must NOT be left stuck at ZTest Always.
    /// </summary>
    public bool RenderOnTopEnabled;

    /// <summary>Graphics switched to ZTest Always while floated, with the state to restore on Release.</summary>
    public readonly List<GraphicOverlayRecord> RenderOnTopGraphics = new(64);

    /// <summary>Next frame the render-on-top sweep re-runs (pooled/late graphics need ZTest Always too).</summary>
    public int RenderOnTopSweepNextFrame;
}

/// <summary>
/// A uGUI graphic switched to ZTest Always for a floated render-on-top modal (see
/// <see cref="ConvertedPanel.RenderOnTopEnabled"/>). Two mutually-exclusive restore modes:
/// an INSTANCE material swapped onto <see cref="Graphic.material"/> (regular Image/Text —
/// restore the original ref, destroy the instance), or a TMP FONT material whose
/// <c>_ZTestMode</c> value was overridden (restore the value — <see cref="TMP_Text.fontMaterial"/>
/// is already a per-object instance, so no shared asset is mutated).
/// </summary>
internal struct GraphicOverlayRecord
{
    public Graphic Graphic;
    public Material? OriginalMaterial; // instance-swap path: the ref to put back on Release
    public Material? Instance;         // instance-swap path: the instance we created (destroy on Release)
    public Material? TmpMaterial;      // TMP path: the font material whose _ZTestMode we changed
    public int TmpOriginalZTest;       // TMP path: the _ZTestMode value to restore
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

/// <summary>
/// Generic, fully reversible canvas conversion framework (Phase 3c).
///
/// Model: instead of flipping the game's big Screen-Space-Camera canvases to world
/// space (they host dozens of unrelated windows), individual panels
/// (<see cref="RectTransform"/> subtrees, usually a <c>UIWindow</c> root) are
/// re-parented onto small WorldUI-owned world-space host canvases
/// (<see cref="RenderMode.WorldSpace"/>, ~1 mm per uGUI pixel, configurable via
/// <see cref="WorldUIConfig.CanvasScaleMm"/>). Each host carries its own
/// <see cref="GraphicRaycaster"/> and is registered with
/// <see cref="UguiPokeSurfaces"/> so the Phase-2 fingertip poke synthesizes real
/// pointer events on it.
///
/// MODALITY: the game locks its 2D UI by disabling the raycasters it serializes
/// (<c>UIManager.ToggleLockUI</c>, verified: <c>public void ToggleLockUI(bool active)
/// { graphicRaycaster.enabled = !active; for (...) graphicRaycasters[i].enabled =
/// !active; }</c>). Our host raycasters are NOT in those lists, so the lock is
/// mirrored here: <see cref="VREvents.UiLockChanged"/> plus module-side soft locks
/// (phase banner) disable every host raycaster.
///
/// REVERSIBILITY: <see cref="Release"/>/<see cref="ReleaseAll"/> restore parent,
/// sibling index, anchors, pivot, anchored position, size, scale and local pose,
/// unregister the poke surface and destroy the host. The head camera's culling
/// mask (UI layer bit, needed to see world-space UI) is restored too.
/// </summary>
internal static class CanvasConversion
{
    // Built-in "UI". Converted panels host GAME-owned uGUI trees, which are never
    // re-layered (reversibility) — so the hosts stay on the game's UI layer and this
    // class owns the head camera's UI culling bit. Mod-owned visuals use the separate
    // dedicated mod layer instead (Core.VRLayers; docs/CAMERA-POLICY.md §2).
    private const int UiLayer = 5;

    private static readonly List<ConvertedPanel> Active = new(16);
    private static readonly HashSet<object> SoftLocks = new();

    private static bool _uiLocked;
    private static bool _lockDirty;
    private static Camera? _maskedCamera;
    private static int _originalCullingMask;
    private static int _maskRequests;

    /// <summary>Currently converted panels (read-only iteration).</summary>
    internal static IReadOnlyList<ConvertedPanel> ActivePanels => Active;

    /// <summary>World camera used for host canvases and poke projection.</summary>
    internal static Camera? WorldCamera =>
        Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;

    // ---- conversion ---------------------------------------------------------------------

    /// <summary>
    /// Move <paramref name="target"/> onto a new world-space host canvas.
    /// The host is unpositioned (identity pose) — the caller places
    /// <see cref="ConvertedPanel.HostTransform"/> in the world and owns its lifetime
    /// via <see cref="Release"/>. Returns null when the target is gone.
    /// <paramref name="fitContent"/> opts the host in/out of the central content fit
    /// (<see cref="TickFit"/>); default null = fit pokeable hosts only (their laser/
    /// poke plane must match visible content — test #14 item 1). Display-only panels
    /// whose target is a large stretch container (the enemy round-reveal holder) pass
    /// true: they need the host sized/centered on the visible content too, without
    /// ever registering as a poke surface.
    /// <paramref name="flatten2D"/> (test #21, opt-in per surface): neutralize the
    /// game's real 3D styling inside the subtree — see <see cref="FlattenSubtree"/>.
    /// <paramref name="sortingOrder"/> sets the host <see cref="Canvas.sortingOrder"/>
    /// (default 0). ALL host canvases share sortingOrder 0 by default, and Unity depth-sorts
    /// equal-order WORLD-space canvases by camera distance — which jitters with head
    /// micro-motion, so two overlapping equal-order hosts swap render order frame-to-frame
    /// (the floated-modal FLICKER, see ModalFallback). A dominant order lifts a host out of
    /// that ambiguity; adopted nested canvases keep <c>overrideSorting</c> cleared, so they
    /// inherit this order and stay ordered with the host.
    /// <paramref name="renderOnTop"/> (floated MODAL windows only, default false so other
    /// surfaces — initiative/actor-bars/combat-log — stay depth-tested and occluded by the
    /// diorama) switches every uGUI graphic in the subtree to ZTest Always so the modal renders
    /// OVER all opaque geometry (the sky dome, the diorama) and can never be occluded — the sky
    /// stays rendered. Reversible on <see cref="Release"/> (see <see cref="ApplyRenderOnTop"/>).
    /// </summary>
    internal static ConvertedPanel? Convert(RectTransform? target, string name, bool pokeable = true,
        PokeSurfaceTuning? pokeTuning = null, bool? fitContent = null, bool flatten2D = false,
        int sortingOrder = 0, bool diagnostic = false, bool useModLayer = false,
        bool transparentBackground = false, bool renderOnTop = false)
    {
        if (target == null)
        {
            VRLog.Warn("WorldUI", $"CanvasConversion.Convert({name}): target is null/destroyed.");
            return null;
        }

        // FLICKER HUNT: a re-conversion of a still-live target (or a still-live host of
        // the same name) is the convert↔release oscillation signature (like the ActorBars
        // re-adoption bug) — flag it loudly so the log attributes any per-open churn.
        if (diagnostic)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                ConvertedPanel existing = Active[i];
                bool sameTarget = ReferenceEquals(existing.Target, target);
                bool sameName = existing.HostGo != null && existing.HostGo.name == $"GloomhavenVR.Panel_{name}";
                if (sameTarget || sameName)
                {
                    VRLog.Warn("WorldUI", $"MODAL DIAG: Convert('{name}') while a live conversion of the same " +
                                          $"{(sameTarget ? "TARGET" : "host name")} is still Active — convert/release " +
                                          "OSCILLATION (this is per-open flicker churn, not a stable float).");
                    break;
                }
            }
        }

        var panel = new ConvertedPanel
        {
            Target = target,
            OriginalParent = target.parent,
            OriginalSiblingIndex = target.GetSiblingIndex(),
            OriginalAnchorMin = target.anchorMin,
            OriginalAnchorMax = target.anchorMax,
            OriginalPivot = target.pivot,
            OriginalAnchoredPosition = target.anchoredPosition,
            OriginalSizeDelta = target.sizeDelta,
            OriginalLocalScale = target.localScale,
            OriginalLocalPosition = target.localPosition,
            OriginalLocalRotation = target.localRotation,
        };

        // Rect size while still under the original (possibly stretch) anchors.
        Vector2 size = target.rect.size;
        bool degenerate = size.x < 1f || size.y < 1f;
        if (degenerate)
            size = new Vector2(Mathf.Max(size.x, 100f), Mathf.Max(size.y, 100f));

        var hostGo = new GameObject($"GloomhavenVR.Panel_{name}");
        hostGo.layer = UiLayer;
        var hostRect = hostGo.AddComponent<RectTransform>();
        var hostCanvas = hostGo.AddComponent<Canvas>();
        hostCanvas.renderMode = RenderMode.WorldSpace;
        hostCanvas.worldCamera = WorldCamera;
        // Equal-order world-space canvases depth-sort by camera distance (jitters with head
        // motion → overlapping hosts flicker); a dominant order lifts a host clear of the tie.
        hostCanvas.sortingOrder = sortingOrder;
        var raycaster = hostGo.AddComponent<GraphicRaycaster>();
        raycaster.enabled = !EffectiveLock;

        hostRect.sizeDelta = size;
        hostRect.pivot = new Vector2(0.5f, 0.5f);
        // Meters per pixel at diorama scale 1; the caller multiplies world scale
        // into the host transform's localScale via PlaceHost.
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        hostRect.localScale = Vector3.one * metersPerPixel;

        // Re-parent and center: anchors collapse to the middle so the captured
        // size becomes absolute.
        target.SetParent(hostRect, worldPositionStays: false);
        target.anchorMin = new Vector2(0.5f, 0.5f);
        target.anchorMax = new Vector2(0.5f, 0.5f);
        target.pivot = new Vector2(0.5f, 0.5f);
        target.anchoredPosition = Vector2.zero;
        target.sizeDelta = size;
        target.localScale = Vector3.one;
        target.localRotation = Quaternion.identity;
        target.localPosition = new Vector3(target.localPosition.x, target.localPosition.y, 0f);

        panel.HostGo = hostGo;
        panel.HostCanvas = hostCanvas;
        panel.HostRaycaster = raycaster;
        panel.HostRect = hostRect;

        AdoptNestedCanvases(panel); // tests #19/#20: sorting-override + raycast hijack

        if (flatten2D)
        {
            // Test #21: initial pass now (the subtree converts already tilted);
            // per-frame re-assert runs from LateTick — the game rewrites these.
            // The sweep's growth log stays muted for the initial pass (the line
            // below reports the starting count).
            panel.FlattenEnabled = true;
            panel.FlattenLogNextFrame = int.MaxValue;
            FlattenSubtree(panel);
            panel.FlattenLoggedCount = panel.Flattened.Count;
            panel.FlattenLogNextFrame = Time.frameCount + CanvasSweepIntervalFrames;
            VRLog.Info("WorldUI", $"Flattened {panel.Flattened.Count} transform(s) in '{name}' " +
                                  "(local rotation → identity, local z → 0; x/y animations untouched).");
        }

        if (pokeable)
            UguiPokeSurfaces.Register(hostCanvas, pokeTuning); // P5: per-canvas press feel (A.10)

        // Test #14 item 1: EVERY pokeable host is content-fitted, not only floated
        // modals — the initiative track converted at its full 1920x1080 window rect
        // (~45x25 m world plane!) and that invisible plane caught the laser before
        // everything behind it (dot off the dialog, tray clicks eaten). Fitting is
        // driven centrally from Tick(), incl. periodic re-fit on content growth.
        // Display-only callers opt in explicitly via fitContent (enemy reveal).
        if (fitContent ?? pokeable)
        {
            panel.FitEnabled = true;
            panel.FitFrameDegenerate = degenerate;
            panel.FitNotBefore = Time.unscaledTime + FitDelaySeconds;
            panel.FitFirstDeadline = Time.unscaledTime + FitFirstWarnSeconds;
        }

        panel.Diagnostic = diagnostic;
        panel.DiagConvertedAt = Time.unscaledTime;

        // User #8 part 1: move the whole floated-modal subtree onto the dedicated mod
        // layer so ONLY the HMD head camera renders it — the game's mono UI Camera
        // (cullingMask = UI layer only) can no longer double-draw the world-space modal
        // (the confirmed flicker). Runs AFTER adoption/flatten so nested-canvas GameObjects
        // are known; reversible via the recorded original layers.
        if (useModLayer)
        {
            panel.ModLayerEnabled = true;
            ApplyModLayer(panel, initial: true);
        }

        // User #8 part 2: hide the full-screen opaque backing/blur so the modal reads as
        // floating foreground content, not a flat rectangle. Reversible on Release.
        if (transparentBackground)
        {
            panel.HideBackground = true;
            HideFullScreenBackground(panel, initial: true);
        }

        // Sky/diorama occlusion fix: switch every uGUI graphic in the subtree to ZTest Always so a
        // floated MODAL renders OVER all opaque geometry — the enclosing sky dome ('AMP_SkyShader',
        // depth-writing, no _ZWrite) would otherwise clip a menu dragged to the shell edge. Only
        // floated modals opt in; reversible on Release (the game reparents these graphics back to 2D).
        if (renderOnTop)
        {
            panel.RenderOnTopEnabled = true;
            ApplyRenderOnTop(panel, initial: true);
        }

        // Sub-item A: for a floated modal that gets the mod-layer move and/or the
        // background hide, re-run BOTH every frame for a short settle window so a backing
        // the game instantiates / fades in a few frames after Convert is treated before its
        // first visible frame — killing the reported ~1 s initial flicker.
        if (useModLayer || transparentBackground || renderOnTop)
        {
            panel.EarlySettleUntil = Time.unscaledTime + EarlySettleSeconds;
            // Item 3a (DECISIVE initial-flicker fix): create the host RENDER-HIDDEN and pop it in
            // only once it is treated + stable. The initial ApplyModLayer / background-hide above
            // already ran with the canvas enabled (so the main backing is disabled), and the
            // early-settle sweep keeps re-treating every frame; disabling the canvas now guarantees
            // NO untreated frame is ever drawn while a late/faded-in backing is caught.
            hostCanvas.enabled = false;
            panel.RevealPending = true;
            panel.RevealNotBefore = Time.unscaledTime + RevealDelaySeconds;
        }

        Active.Add(panel);
        EnsureCameraMask();
        VRLog.Info("WorldUI", $"Converted '{name}' to world space ({size.x:F0}x{size.y:F0} px, " +
                              $"sortingOrder={sortingOrder}).");
        if (diagnostic)
            DiagnoseModal(panel, force: true); // one-shot baseline (host state + camera scan) at float time
        return panel;
    }

    /// <summary>
    /// Place a host in the world: <paramref name="pose"/> in world units,
    /// <paramref name="worldScale"/> = diorama scale (game units per real meter).
    /// The panel's physical size becomes pixels × CanvasScaleMm, read at call time.
    /// </summary>
    internal static void PlaceHost(ConvertedPanel panel, Vector3 position, Quaternion rotation, float worldScale)
    {
        if (panel.HostGo == null)
            return;
        Transform t = panel.HostGo.transform;
        t.SetPositionAndRotation(position, rotation);
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        t.localScale = Vector3.one * (metersPerPixel * worldScale);
    }

    // ---- nested-canvas adoption (tests #19/#20) --------------------------------------------

    /// <summary>Periodic sweep throttle (~0.4 s at 72 Hz) for late-appearing nested canvases.</summary>
    private const int CanvasSweepIntervalFrames = 30;

    /// <summary>
    /// Sub-item A: how long after Convert a floated modal re-treats (mod layer + background
    /// hide + adoption) EVERY frame instead of on the periodic sweep, so a backing the game
    /// fades in / instantiates within the first ~second is hidden before it ever renders.
    /// Comfortably covers the window show/fade animations (~0.3 s) plus any lazy populate.
    /// </summary>
    private const float EarlySettleSeconds = 1.5f;

    /// <summary>
    /// Item 3a: hold a freshly converted modal host render-hidden (canvas disabled) this long
    /// after Convert before the zero-flicker pop-in — long enough for the mod-layer move +
    /// background hide to have been applied over several frames (incl. a late/faded-in backing),
    /// short enough to read as an instant open. Well inside <see cref="EarlySettleSeconds"/>.
    /// </summary>
    private const float RevealDelaySeconds = 0.15f;

    // Scratch buffer (sweep time only; reused, no per-call allocations).
    private static readonly List<Canvas> CanvasScratch = new(8);

    /// <summary>
    /// Tests #19/#20: ADOPT every game-owned nested <see cref="Canvas"/> inside the
    /// converted subtree — keep it ENABLED, clear <c>overrideSorting</c>, merge its
    /// raycaster into the host's hit-testing. A nested canvas riding into the host
    /// breaks the conversion contract twice — the initiative track carries one
    /// (verified decompiled InitiativeTrack.cs: <c>[SerializeField] private Canvas
    /// canvas</c>, <c>sortingOrder = 40</c>, rewritten live by
    /// <c>ToggleSortingOrder</c> while popups show):
    ///
    /// - RENDERING: an override-sorting nested canvas beats every sortingOrder-0
    ///   transparent renderer in the view REGARDLESS OF DEPTH — the docked track drew
    ///   over the hands even with a hand held in front of it (test #19). With
    ///   <c>overrideSorting</c> cleared the canvas inherits the host's sorting and
    ///   depth/distance-sorts with the scene like plain host content. Test #19
    ///   DISABLED the component instead, believing the children would merge into the
    ///   host — in reality a disabled nested Canvas stops rendering its ENTIRE
    ///   subtree (the standard <c>canvas.enabled = false</c> hide-UI optimization;
    ///   children only merge up when the component is DESTROYED), so the docked
    ///   track was invisible in test #20 while the geometric ray∩rect clamp still
    ///   collided with its host: collisions without pixels.
    /// - HIT-TESTING: uGUI Graphics register with their NEAREST enabled parent canvas
    ///   (GraphicRegistry), so every Graphic under the nested canvas belongs to IT and
    ///   the HOST GraphicRaycaster raycasts a hollow set there. Each adopted canvas
    ///   therefore gets a GraphicRaycaster (added when missing) and is registered via
    ///   <see cref="UguiPokeSurfaces.RegisterNested"/>; UguiPointer.TryRaycast merges
    ///   its hits with the host's. The beam clamp and poke plane keep using the HOST
    ///   rect only. <c>worldCamera</c> is aligned with the host so the raycaster's
    ///   eventCamera matches the camera the drivers project screen points with.
    ///
    /// Everything is recorded on the panel and restored by <see cref="Release"/>
    /// (overrideSorting, worldCamera; raycasters WE added are destroyed). Swept
    /// periodically from <see cref="Tick"/>: pooled children (initiative rows) may
    /// bring canvases after conversion, and the game can flip overrideSorting back
    /// on live (AbilityCardUI/CardHighlight set it; ToggleSortingOrder's sortingOrder
    /// writes are harmless with override off) — re-asserted here, silently.
    /// </summary>
    private static void AdoptNestedCanvases(ConvertedPanel panel)
    {
        panel.CanvasSweepNextFrame = Time.frameCount + CanvasSweepIntervalFrames;
        if (panel.Target == null)
            return;

        CanvasScratch.Clear();
        panel.Target.GetComponentsInChildren(includeInactive: true, CanvasScratch);
        for (int i = 0; i < CanvasScratch.Count; i++)
        {
            Canvas nested = CanvasScratch[i];
            if (nested == null)
                continue;

            if (IsAdopted(panel, nested))
            {
                // Re-assert per sweep (change-only writes; no log — the adoption
                // line below already documented this canvas once).
                if (nested.overrideSorting)
                    nested.overrideSorting = false;
                if (nested.worldCamera != panel.HostCanvas.worldCamera)
                    nested.worldCamera = panel.HostCanvas.worldCamera;
                continue;
            }

            var record = new NestedCanvasRecord
            {
                Canvas = nested,
                OriginalOverrideSorting = nested.overrideSorting,
                OriginalWorldCamera = nested.worldCamera,
            };
            nested.overrideSorting = false;
            nested.worldCamera = panel.HostCanvas.worldCamera;
            if (nested.GetComponent<GraphicRaycaster>() == null)
                record.AddedRaycaster = nested.gameObject.AddComponent<GraphicRaycaster>();

            UguiPokeSurfaces.RegisterNested(panel.HostCanvas, nested);
            panel.AdoptedCanvases.Add(record);
            VRLog.Info("WorldUI", $"Adopted nested canvas '{nested.name}' in '{panel.HostGo.name}' " +
                                  $"(overrideSorting {record.OriginalOverrideSorting}→false, " +
                                  $"sortingOrder={nested.sortingOrder}, raycaster " +
                                  (record.AddedRaycaster != null ? "added" : "existing") +
                                  ") — inherits host sorting/depth, raycasts merged with the host.");
        }
        CanvasScratch.Clear();
    }

    private static bool IsAdopted(ConvertedPanel panel, Canvas nested)
    {
        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            if (ReferenceEquals(panel.AdoptedCanvases[i].Canvas, nested))
                return true;
        }
        return false;
    }

    // ---- dedicated mod layer for floated modals (user #8: UI-Camera double-draw) ----------

    // Scratch buffer (mod-layer sweep only; reused, no per-call allocations).
    private static readonly List<Transform> TransformScratch = new(128);

    /// <summary>
    /// Move the host + its ENTIRE converted subtree onto <see cref="Core.VRLayers.ModLayer"/>
    /// so ONLY the HMD head camera renders it (the game UI Camera's cullingMask is the UI
    /// layer only — bit 27 is never in it, so the mono double-draw stops). Sub-canvases are
    /// culled by their OWN GameObject layer, so the whole subtree — not just the host — must
    /// move. Every transform's original layer is recorded ONCE for a faithful restore;
    /// writes are change-gated, so the periodic re-sweep only pays for genuinely new/pooled
    /// children. No-op if the mod layer could not be resolved to a dedicated slot (it fell
    /// back to the UI layer — moving there would change nothing and lose the game's layers).
    /// </summary>
    private static void ApplyModLayer(ConvertedPanel panel, bool initial)
    {
        if (panel.HostGo == null)
            return;
        int modLayer = Core.VRLayers.ModLayer;
        if (modLayer == UiLayer)
            return; // no dedicated layer available — the move would not separate us from the UI Camera

        TransformScratch.Clear();
        panel.HostGo.GetComponentsInChildren(includeInactive: true, TransformScratch);
        int moved = 0;
        for (int i = 0; i < TransformScratch.Count; i++)
        {
            Transform t = TransformScratch[i];
            if (t == null || t.gameObject.layer == modLayer)
                continue;
            if (!IsRelayered(panel, t))
                panel.Relayered.Add(new LayerRecord { Transform = t, OriginalLayer = t.gameObject.layer });
            t.gameObject.layer = modLayer;
            moved++;
        }
        TransformScratch.Clear();
        if (moved > 0)
            VRLog.Info("WorldUI", $"MODAL LAYER: moved {moved} transform(s) of '{panel.HostGo.name}' onto the " +
                                  $"dedicated mod layer {modLayer} — only the HMD head camera renders it now, " +
                                  "the game UI Camera can no longer double-draw the world-space modal" +
                                  (initial ? "." : " (pooled/late children)."));
    }

    private static bool IsRelayered(ConvertedPanel panel, Transform t)
    {
        for (int i = 0; i < panel.Relayered.Count; i++)
        {
            if (ReferenceEquals(panel.Relayered[i].Transform, t))
                return true;
        }
        return false;
    }

    // ---- transparent modal background (user #8 part 2) ------------------------------------

    /// <summary>A background image must cover at least this fraction of the window rect (each axis).</summary>
    private const float BackgroundCoverFraction = 0.85f;

    /// <summary>Only opaque backings count as "the background" — invisible click-catchers are left alone.</summary>
    private const float BackgroundOpaqueAlpha = 0.3f;

    // Scratch buffers (background sweep only; reused).
    private static readonly List<Graphic> BgGraphicScratch = new(64);
    private static readonly Vector3[] BgCornerScratch = new Vector3[4];

    /// <summary>
    /// Disable the full-window opaque backing/blur image(s) of a floated full-screen menu
    /// so only its foreground content shows (user #8 part 2). A background is an
    /// <see cref="Image"/>/<see cref="RawImage"/> whose rect covers ~the whole window frame
    /// and is opaque; text, buttons and art are smaller and untouched. Disabling the
    /// <see cref="Graphic"/> hides it AND drops its raycast blocker (clicks pass through the
    /// now-empty area harmlessly — there is nothing behind it in world space). Reversible:
    /// each disabled graphic is recorded and re-enabled on <see cref="Release"/>.
    /// </summary>
    private static void HideFullScreenBackground(ConvertedPanel panel, bool initial)
    {
        if (panel.Target == null || panel.HostRect == null)
            return;

        panel.BackgroundSweepNextFrame = Time.frameCount + CanvasSweepIntervalFrames;

        // Window frame size in host-local space (target world corners → host-local).
        panel.Target.GetWorldCorners(BgCornerScratch);
        Vector3 fa = panel.HostRect.InverseTransformPoint(BgCornerScratch[0]);
        Vector3 fc = panel.HostRect.InverseTransformPoint(BgCornerScratch[2]);
        float frameW = Mathf.Abs(fc.x - fa.x);
        float frameH = Mathf.Abs(fc.y - fa.y);
        if (frameW < 1f || frameH < 1f)
            return;

        BgGraphicScratch.Clear();
        panel.Target.GetComponentsInChildren(includeInactive: false, BgGraphicScratch);
        int hidden = 0;
        for (int i = 0; i < BgGraphicScratch.Count; i++)
        {
            Graphic g = BgGraphicScratch[i];
            if (g == null || !g.enabled || !(g is Image || g is RawImage))
                continue;
            if (g.canvasRenderer == null || g.canvasRenderer.cull)
                continue;
            // Sub-item A: gate on the graphic's INTRINSIC alpha (its own serialized colour),
            // NOT the inherited CanvasGroup fade. The window's backing fades in from inherited-
            // alpha 0 over the show animation; multiplying by the fade made it read as
            // "transparent" for the whole fade-in, so it was only hidden once a later sweep
            // saw it near-opaque — the visible+flickering first second. An intrinsically opaque
            // full-cover image IS the backing even at inherited-alpha 0, so hide it on its first
            // frame; a true invisible click-catcher is intrinsically transparent (colour.a ~ 0)
            // and still excluded here.
            if (g.color.a < BackgroundOpaqueAlpha)
                continue;

            var grect = (RectTransform)g.transform;
            grect.GetWorldCorners(BgCornerScratch);
            Vector3 ga = panel.HostRect.InverseTransformPoint(BgCornerScratch[0]);
            Vector3 gc = panel.HostRect.InverseTransformPoint(BgCornerScratch[2]);
            float gw = Mathf.Abs(gc.x - ga.x);
            float gh = Mathf.Abs(gc.y - ga.y);
            if (gw < frameW * BackgroundCoverFraction || gh < frameH * BackgroundCoverFraction)
                continue; // smaller than the frame → foreground content, keep it

            g.enabled = false; // hide the backing AND its raycast blocker
            panel.HiddenBackgrounds.Add(g);
            hidden++;
        }
        BgGraphicScratch.Clear();
        if (hidden > 0)
            VRLog.Info("WorldUI", $"MODAL BACKGROUND: disabled {hidden} full-window backing/blur image(s) in " +
                                  $"'{panel.HostGo.name}' — the modal now shows only its foreground content " +
                                  (initial ? "(transparent background)." : "(late fade-in)."));
    }

    // ---- render-on-top (floated-MODAL sky/diorama occlusion fix) --------------------------

    private const string ZTestOverlayProp = "_ZTest";        // GloomhavenVR/Overlay etc.
    private const string ZTestTmpProp = "_ZTestMode";        // TMP distance-field shader
    private const string ZTestGuiProp = "unity_GUIZTestMode"; // built-in UI/Default shader

    private static readonly int AlwaysZTest = (int)UnityEngine.Rendering.CompareFunction.Always;

    // Scratch buffer (render-on-top sweep only; reused, no per-call allocations).
    private static readonly List<Graphic> RenderOnTopScratch = new(64);

    /// <summary>
    /// Switch every uGUI graphic in the converted subtree to ZTest Always so a floated MODAL
    /// renders OVER all opaque geometry (the enclosing sky dome writes depth with no <c>_ZWrite</c>
    /// toggle, and would otherwise clip a world-space menu dragged to the shell edge). The sky is
    /// left rendered — only the menu is lifted above it.
    ///
    /// Per graphic, two reversible modes so NO shared game material is mutated:
    /// - TMP text renders through its FONT material (<c>_ZTestMode</c>); <see cref="TMP_Text.fontMaterial"/>
    ///   is already a per-object instance, so the value is overridden there and the original value
    ///   recorded for restore.
    /// - Image/RawImage/legacy Text: a per-graphic INSTANCE of <see cref="Graphic.material"/> is
    ///   created, ZTest set on it (<c>unity_GUIZTestMode</c> for UI/Default, <c>_ZTest</c> for Overlay),
    ///   and swapped in; the original ref is put back and the instance destroyed on <see cref="Release"/>.
    ///
    /// Change-gated (a graphic already recorded is skipped) and swept from <see cref="Tick"/>/
    /// <see cref="LateTick"/> so pooled/late graphics (menu list items, fade-ins) are caught too;
    /// existing swapped instances hold on their own (their assigned material persists). Raycasting is
    /// geometric (unchanged), so poke/laser clicks are unaffected.
    /// </summary>
    private static void ApplyRenderOnTop(ConvertedPanel panel, bool initial)
    {
        if (panel.Target == null)
            return;
        panel.RenderOnTopSweepNextFrame = Time.frameCount + CanvasSweepIntervalFrames;

        RenderOnTopScratch.Clear();
        panel.Target.GetComponentsInChildren(includeInactive: false, RenderOnTopScratch);
        int switched = 0;
        for (int i = 0; i < RenderOnTopScratch.Count; i++)
        {
            Graphic g = RenderOnTopScratch[i];
            if (g == null || !g.enabled || IsRenderOnTopRecorded(panel, g))
                continue;

            if (g is TMP_Text tmp)
            {
                // TMP renders via its font material (_ZTestMode). fontMaterial is a per-object
                // instance, so this mutates only this text; record the value to restore it.
                Material? fm = tmp.fontMaterial;
                if (fm == null || !fm.HasProperty(ZTestTmpProp))
                    continue;
                int orig = fm.GetInt(ZTestTmpProp);
                fm.SetInt(ZTestTmpProp, AlwaysZTest);
                panel.RenderOnTopGraphics.Add(new GraphicOverlayRecord
                {
                    Graphic = g, TmpMaterial = fm, TmpOriginalZTest = orig,
                });
                switched++;
            }
            else
            {
                Material orig = g.material;
                if (orig == null)
                    continue;
                var inst = new Material(orig); // per-graphic instance, never the shared game asset
                ApplyZTestAlways(inst);
                g.material = inst;
                panel.RenderOnTopGraphics.Add(new GraphicOverlayRecord
                {
                    Graphic = g, OriginalMaterial = orig, Instance = inst,
                });
                switched++;
            }
        }
        RenderOnTopScratch.Clear();

        if (switched > 0)
            VRLog.Info("WorldUI", $"MODAL ON-TOP: switched {switched} graphic material(s) in " +
                                  $"'{panel.HostGo.name}' to ZTest Always (total {panel.RenderOnTopGraphics.Count}) — " +
                                  "the floated modal renders over the sky/diorama and can no longer be occluded" +
                                  (initial ? "." : " (pooled/late graphics)."));
    }

    /// <summary>
    /// Set ZTest Always on an INSTANCE material via whichever property its shader exposes:
    /// Overlay's <c>_ZTest</c>, TMP's <c>_ZTestMode</c>, and the built-in UI shader's
    /// <c>unity_GUIZTestMode</c> (the game's menu Images use <c>UI/Default</c>). A plain UI
    /// material that does not report any of these still honours <c>unity_GUIZTestMode</c> in its
    /// <c>ZTest [unity_GUIZTestMode]</c> pass, so it is set unconditionally as the fallback.
    /// </summary>
    private static void ApplyZTestAlways(Material m)
    {
        bool applied = false;
        if (m.HasProperty(ZTestOverlayProp)) { m.SetInt(ZTestOverlayProp, AlwaysZTest); applied = true; }
        if (m.HasProperty(ZTestTmpProp)) { m.SetInt(ZTestTmpProp, AlwaysZTest); applied = true; }
        if (m.HasProperty(ZTestGuiProp)) { m.SetInt(ZTestGuiProp, AlwaysZTest); applied = true; }
        if (!applied)
            m.SetInt(ZTestGuiProp, AlwaysZTest); // UI/Default honours it even when HasProperty is false
    }

    private static bool IsRenderOnTopRecorded(ConvertedPanel panel, Graphic g)
    {
        for (int i = 0; i < panel.RenderOnTopGraphics.Count; i++)
        {
            if (ReferenceEquals(panel.RenderOnTopGraphics[i].Graphic, g))
                return true;
        }
        return false;
    }

    // ---- 2D flatten (test #21) ------------------------------------------------------------

    /// <summary>Local rotation counts as 3D beyond this angle (degrees) off identity.</summary>
    private const float FlattenAngleEpsilon = 0.05f;

    /// <summary>Local z counts as 3D beyond this many uGUI pixels.</summary>
    private const float FlattenZEpsilon = 0.01f;

    // Scratch buffer (flatten sweep only; reused, no per-call allocations).
    private static readonly List<RectTransform> RectScratch = new(64);

    /// <summary>
    /// Test #21: neutralize REAL 3D inside a converted subtree. The combat log's
    /// content is styled with local rotations and z offsets (entries recede into
    /// depth, the round banner angles backward) — BAKED into the serialized
    /// prefab/scene RectTransforms, not written by any game code, and rendered
    /// through the perspective UI camera (verified decompiled CanvasManager.cs:
    /// <c>allCameras[i].tag.Equals("UICamera")</c> → <c>canvas.worldCamera</c>)
    /// where it reads as subtle 2D styling. On a world-space host it becomes
    /// literal geometry: content visibly tilted behind the panel plane,
    /// parallax-shifting with head motion (Convert only flattens the target ROOT's
    /// own pose, the subtree rode in untouched). No patchable runtime writer
    /// exists — the fix is clamping the transforms themselves.
    ///
    /// Every RectTransform under the target carrying a non-identity local rotation
    /// or a non-zero local z is recorded once (original rotation + z, restored by
    /// <see cref="Release"/>) and clamped: rotation → identity, z → 0. X/Y are
    /// NEVER touched — positional animations (entry slide/fade-ins) keep playing
    /// flat. A one-shot flatten is NOT enough, the values come back live:
    /// - pooled entry spawns write WORLD-identity rotation (verified ObjectPool.cs
    ///   Spawn: <c>gameObject.transform.rotation = rotation</c> with
    ///   <c>Quaternion.identity</c>) — under a world-ROTATED host that lands as a
    ///   tilted LOCAL rotation on every new log line;
    /// - the GUIAnimator tween system's MOVE_LOCAL channel writes a full Vector3
    ///   localPosition incl. z per frame (verified LeanTweenGuiAnimationSettingMove:
    ///   <c>Target.localPosition = value</c>; the banner intro plays it) — though
    ///   NO rotation channel exists (no ...SettingRotate subclass).
    /// So the sweep re-runs every frame from <see cref="LateTick"/> (LateUpdate —
    /// after the game's Update-time tween writers): one GetComponentsInChildren
    /// scan; writes are change-gated, and already-flat transforms cost only the
    /// two reads. The record lookup runs only for transforms actually tilted this
    /// frame.
    /// </summary>
    private static void FlattenSubtree(ConvertedPanel panel)
    {
        if (panel.Target == null)
            return;

        RectScratch.Clear();
        panel.Target.GetComponentsInChildren(includeInactive: true, RectScratch);
        for (int i = 0; i < RectScratch.Count; i++)
        {
            RectTransform rect = RectScratch[i];
            // The root's own pose is Convert's business (flattened there, restored
            // whole by Release) — the sweep owns strictly the subtree below it.
            if (rect == null || ReferenceEquals(rect, panel.Target))
                continue;

            Vector3 pos = rect.localPosition;
            Quaternion rot = rect.localRotation;
            bool tiltedRot = Quaternion.Angle(rot, Quaternion.identity) > FlattenAngleEpsilon;
            bool tiltedZ = Mathf.Abs(pos.z) > FlattenZEpsilon;
            if (!tiltedRot && !tiltedZ)
                continue;

            if (!IsFlattenRecorded(panel, rect))
            {
                panel.Flattened.Add(new FlattenRecord
                {
                    Transform = rect,
                    OriginalLocalZ = pos.z,
                    OriginalLocalRotation = rot,
                });
            }
            if (tiltedRot)
                rect.localRotation = Quaternion.identity;
            if (tiltedZ)
                rect.localPosition = new Vector3(pos.x, pos.y, 0f);
        }
        RectScratch.Clear();

        // Log once per conversion (from Convert), re-log when pooling grows the set
        // — throttled so a burst of new entries makes one line, not one per entry.
        if (panel.Flattened.Count > panel.FlattenLoggedCount
            && Time.frameCount >= panel.FlattenLogNextFrame)
        {
            VRLog.Info("WorldUI", $"Flatten grew to {panel.Flattened.Count} transform(s) in " +
                                  $"'{panel.HostGo.name}' (pooled children).");
            panel.FlattenLoggedCount = panel.Flattened.Count;
            panel.FlattenLogNextFrame = Time.frameCount + CanvasSweepIntervalFrames;
        }
    }

    private static bool IsFlattenRecorded(ConvertedPanel panel, RectTransform rect)
    {
        for (int i = 0; i < panel.Flattened.Count; i++)
        {
            if (ReferenceEquals(panel.Flattened[i].Transform, rect))
                return true;
        }
        return false;
    }

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

        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);
        bool any = false;

        GraphicScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (!g.enabled || g.canvasRenderer == null || g.canvasRenderer.cull)
                continue;
            // Effective alpha: own color × hierarchy (CanvasGroup) alpha.
            if (g.color.a * g.canvasRenderer.GetInheritedAlpha() < 0.05f)
                continue;

            var rect = (RectTransform)g.transform;
            // Zero draw size = nothing on screen (collapsed layout cells, empty
            // stretch containers with a Graphic) — must not anchor the union at
            // their corner points (test #16 measurement tightening).
            Rect drawRect = rect.rect;
            if (drawRect.width < 0.5f || drawRect.height < 0.5f)
                continue;
            rect.GetWorldCorners(CornerScratch);
            for (int c = 0; c < 4; c++)
            {
                Vector3 local = panel.HostRect.InverseTransformPoint(CornerScratch[c]);
                if (local.x < min.x) min.x = local.x;
                if (local.y < min.y) min.y = local.y;
                if (local.x > max.x) max.x = local.x;
                if (local.y > max.y) max.y = local.y;
            }
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

        Vector2 size = max - min;
        if (size.x < 32f || size.y < 32f)
            return false; // degenerate (mid scale-in animation) — caller retries

        const float Padding = 12f;
        size += Vector2.one * (2f * Padding);
        size.x = Mathf.Min(size.x, frameMax.x - frameMin.x);
        size.y = Mathf.Min(size.y, frameMax.y - frameMin.y);

        // Dirty check (test #14): within 2 % of the current host rect (size AND
        // centering) — nothing to do. Host pivot is centered, so local origin ==
        // rect center and |center| is the content's off-center error directly.
        Rect host = panel.HostRect.rect;
        Vector2 center = (min + max) * 0.5f;
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

    /// <summary>Restore the panel into its original 2D home and destroy the host.</summary>
    internal static void Release(ConvertedPanel? panel)
    {
        if (panel == null)
            return;
        Active.Remove(panel);

        if (panel.HostCanvas != null)
            UguiPokeSurfaces.Unregister(panel.HostCanvas); // drops nested registrations too

        // Restore the game's own nested canvases (tests #19/#20): overrideSorting and
        // worldCamera back to their captured values; raycasters WE added are removed
        // (ones the game serialized stay).
        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            NestedCanvasRecord record = panel.AdoptedCanvases[i];
            if (record.Canvas != null)
            {
                record.Canvas.overrideSorting = record.OriginalOverrideSorting;
                record.Canvas.worldCamera = record.OriginalWorldCamera;
            }
            if (record.AddedRaycaster != null)
                Object.Destroy(record.AddedRaycaster);
        }
        panel.AdoptedCanvases.Clear();

        // User #8 part 1: restore every transform we moved onto the mod layer back to its
        // original layer (game content re-joins the UI layer for the 2D restore).
        for (int i = 0; i < panel.Relayered.Count; i++)
        {
            LayerRecord record = panel.Relayered[i];
            if (record.Transform != null)
                record.Transform.gameObject.layer = record.OriginalLayer;
        }
        panel.Relayered.Clear();

        // User #8 part 2: re-enable the full-window backing/blur we disabled while floated.
        for (int i = 0; i < panel.HiddenBackgrounds.Count; i++)
        {
            Graphic g = panel.HiddenBackgrounds[i];
            if (g != null)
                g.enabled = true;
        }
        panel.HiddenBackgrounds.Clear();

        // Render-on-top: restore each graphic's original material / ZTest state — the game reparents
        // these SAME graphics back to their 2D home, which must NOT be left stuck at ZTest Always.
        for (int i = 0; i < panel.RenderOnTopGraphics.Count; i++)
        {
            GraphicOverlayRecord rec = panel.RenderOnTopGraphics[i];
            if (rec.Instance != null)
            {
                if (rec.Graphic != null) // Unity fake-null: destroyed by a scene unload
                    rec.Graphic.material = rec.OriginalMaterial; // put the original ref back
                Object.Destroy(rec.Instance);                    // drop the instance we created
            }
            else if (rec.TmpMaterial != null)
            {
                rec.TmpMaterial.SetInt(ZTestTmpProp, rec.TmpOriginalZTest); // TMP: restore the value
            }
        }
        panel.RenderOnTopGraphics.Clear();

        // Un-flatten (test #21) BEFORE the root restore below: original local
        // rotation and z go back per recorded transform (x/y stayed game-owned
        // throughout), and the root's full-pose restore then wins as ever.
        for (int i = 0; i < panel.Flattened.Count; i++)
        {
            FlattenRecord record = panel.Flattened[i];
            if (record.Transform == null)
                continue;
            Vector3 pos = record.Transform.localPosition;
            record.Transform.localPosition = new Vector3(pos.x, pos.y, record.OriginalLocalZ);
            record.Transform.localRotation = record.OriginalLocalRotation;
        }
        panel.Flattened.Clear();

        if (panel.Target != null)
        {
            RectTransform target = panel.Target;
            // Detach the target from the float host UNCONDITIONALLY before the host is destroyed
            // below. Previously the reparent ran ONLY when OriginalParent was still alive; if it had
            // been destroyed/replaced (Unity-null), the target stayed a CHILD of the host and the
            // Object.Destroy(HostGo) below CASCADED into it. That is exactly what destroyed the
            // scenario pause menu (UIScenarioEscMenu) on a mod X-close — a Singleton the game never
            // re-creates mid-scenario, so reopening it was impossible until a reload (confirmed via
            // the ESCMenu.OnDestroy probe). Detaching to the scene root (null) keeps the object alive
            // so OptionsToggle can reopen it; the game's flat placement doesn't matter because the
            // mod re-converts/re-floats it on the next open anyway.
            Transform? restoreParent = panel.OriginalParent != null ? panel.OriginalParent : null;
            target.SetParent(restoreParent, worldPositionStays: false);
            if (restoreParent != null)
            {
                target.SetSiblingIndex(panel.OriginalSiblingIndex);
                target.anchorMin = panel.OriginalAnchorMin;
                target.anchorMax = panel.OriginalAnchorMax;
                target.pivot = panel.OriginalPivot;
                target.anchoredPosition = panel.OriginalAnchoredPosition;
                target.sizeDelta = panel.OriginalSizeDelta;
                target.localScale = panel.OriginalLocalScale;
                target.localPosition = panel.OriginalLocalPosition;
                target.localRotation = panel.OriginalLocalRotation;
            }
        }

        if (panel.HostGo != null)
            Object.Destroy(panel.HostGo);

        if (Active.Count == 0)
            RestoreCameraMask();
    }

    /// <summary>Restore every conversion (module shutdown / VR off).</summary>
    internal static void ReleaseAll()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
            Release(Active[i]);
        SoftLocks.Clear();
        RestoreCameraMask();
    }

    // ---- per-frame service (called by the WorldUI driver) --------------------------------

    /// <summary>
    /// Cheap housekeeping: prune panels whose target died with a scene unload,
    /// keep worldCamera bound to the live head camera, apply pending lock state.
    /// </summary>
    internal static void Tick()
    {
        Camera? cam = WorldCamera;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            ConvertedPanel panel = Active[i];
            if (!panel.IsAlive)
            {
                // The game destroyed the UI (scene unload) — drop our host too.
                Active.RemoveAt(i);
                if (panel.HostCanvas != null)
                    UguiPokeSurfaces.Unregister(panel.HostCanvas);
                if (panel.HostGo != null)
                    Object.Destroy(panel.HostGo);
                continue;
            }
            if (panel.HostCanvas.worldCamera != cam)
                panel.HostCanvas.worldCamera = cam;

            // Sub-item A: for the first EarlySettleSeconds after Convert, re-treat every
            // frame (not on the ~0.4 s periodic schedule) so a backing the game fades in /
            // instantiates late is caught before its first visible frame — the initial
            // flicker fix. After the settle window the cheap periodic sweep takes over.
            bool earlySettle = panel.EarlySettleUntil > 0f && Time.unscaledTime < panel.EarlySettleUntil;

            // Tests #19/#20: pooled/late children may bring nested canvases after
            // Convert, and the game can flip overrideSorting back on live.
            if (earlySettle || Time.frameCount >= panel.CanvasSweepNextFrame)
            {
                AdoptNestedCanvases(panel);
                // User #8: a freshly adopted/pooled child spawns on the game's UI layer —
                // re-assert the mod-layer move so the UI Camera never picks it up.
                if (panel.ModLayerEnabled)
                    ApplyModLayer(panel, initial: false);
            }

            // User #8 part 2: re-assert the background hide (menu fade-ins can enable the
            // backing image a few frames after the window shows).
            if (panel.HideBackground && (earlySettle || Time.frameCount >= panel.BackgroundSweepNextFrame))
                HideFullScreenBackground(panel, initial: false);

            // Render-on-top: re-sweep so pooled/late graphics (menu list items, fade-ins) also get
            // ZTest Always; graphics already swapped hold their assigned instance material on their own.
            if (panel.RenderOnTopEnabled && (earlySettle || Time.frameCount >= panel.RenderOnTopSweepNextFrame))
                ApplyRenderOnTop(panel, initial: false);

            // Item 3a: reveal the render-hidden modal host once its settle delay has passed. The
            // treatments for THIS frame already ran above (canvas still disabled → harmless), and
            // LateTick re-treats once more (canvas now enabled) before the frame renders — so the
            // first visible frame is fully treated: a clean pop-in with zero flicker.
            if (panel.RevealPending && panel.HostCanvas != null
                && Time.unscaledTime >= panel.RevealNotBefore)
            {
                panel.HostCanvas.enabled = true;
                panel.RevealPending = false;
                VRLog.Info("WorldUI", $"MODAL REVEAL: '{panel.HostGo.name}' shown after settle " +
                                      "(mod layer + background hidden, stable frame) — zero-flicker pop-in.");
            }

            // FLICKER FIX (modal hosts only): the 30-frame adoption sweep re-asserts
            // overrideSorting=false, but a WORLD-space modal that shares its canvas order
            // with a nested canvas the game flips to overrideSorting=true even briefly
            // renders that subtree at the nested order → it swaps in/out of the host's
            // dominant order between sweeps = flicker. Re-assert every frame for the
            // (few) modal hosts: cheap (a handful of adopted entries), change-gated writes.
            if (panel.Diagnostic)
                ReassertAdoptedSorting(panel);

            TickFit(panel); // test #14 item 1: content fit + growth re-fit (throttled)

            if (panel.Diagnostic)
                DiagnoseModal(panel, force: false); // change-gated per-frame flicker snapshot
        }

        if (Active.Count > 0 || _maskRequests > 0)
            EnsureCameraMask();
        else
            RestoreCameraMask();

        if (_lockDirty)
        {
            _lockDirty = false;
            bool enabled = !EffectiveLock;
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active[i].HostRaycaster != null)
                    Active[i].HostRaycaster.enabled = enabled;
            }
        }
    }

    /// <summary>
    /// LateUpdate service (test #21): re-assert flatness AFTER the game's Update-time
    /// tween writers ran — an Update-time sweep would lose to any tween ticking after
    /// it and the tilt would render anyway. Cost: one change-gated subtree scan per
    /// FLATTENED panel per frame (only surfaces that opted in; combat log today).
    /// </summary>
    internal static void LateTick()
    {
        for (int i = 0; i < Active.Count; i++)
        {
            ConvertedPanel panel = Active[i];
            if (!panel.IsAlive)
                continue;
            if (panel.FlattenEnabled)
                FlattenSubtree(panel);

            // Sub-item A (INITIAL flicker — the residual): the mod-layer move + background hide
            // run from Tick() in Update, but the game instantiates / fades in / enables the
            // full-window opaque backing from its OWN Update, which may run AFTER ours. That frame
            // then RENDERS (rendering happens after all Updates) with the untreated backing still
            // on the game UI layer and visible — the reported 1–2 frame flash right as the menu
            // opens, before the next Update's sweep catches it. Re-running the treatment here in
            // LateUpdate (after every Update, immediately before the frame renders) closes that
            // one-frame gap: whatever the game did to the backing this frame is corrected before
            // it is ever drawn. Bounded to the early-settle window (the menu show/fade animation);
            // writes are change-gated inside each helper, so a steady modal costs a cheap scan.
            bool earlySettle = panel.EarlySettleUntil > 0f && Time.unscaledTime < panel.EarlySettleUntil;
            if (!earlySettle)
                continue;
            if (panel.ModLayerEnabled)
            {
                AdoptNestedCanvases(panel);
                ApplyModLayer(panel, initial: false);
            }
            if (panel.HideBackground)
                HideFullScreenBackground(panel, initial: false);
            if (panel.RenderOnTopEnabled)
                ApplyRenderOnTop(panel, initial: false);
        }
    }

    // ---- floated-modal flicker instrumentation + per-frame sorting guard ------------------

    /// <summary>
    /// Modal-host flicker guard: re-assert <c>overrideSorting=false</c> (and the host's
    /// worldCamera) on every adopted nested canvas EVERY frame, so a game writer that
    /// flips overrideSorting on between the 30-frame adoption sweeps cannot pull a subtree
    /// out of the host's dominant order for up to half a second (visible as flicker).
    /// Change-gated writes; only the (few) adopted entries of modal hosts are touched.
    /// </summary>
    private static void ReassertAdoptedSorting(ConvertedPanel panel)
    {
        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            Canvas nested = panel.AdoptedCanvases[i].Canvas;
            if (nested == null)
                continue;
            if (nested.overrideSorting)
            {
                nested.overrideSorting = false;
                VRLog.Info("WorldUI", $"MODAL DIAG: adopted canvas '{nested.name}' in " +
                                      $"'{panel.HostGo.name}' had overrideSorting flipped back ON by the game — " +
                                      "re-cleared (it was rendering at its own order, out of the host's).");
            }
            if (panel.HostCanvas != null && nested.worldCamera != panel.HostCanvas.worldCamera)
                nested.worldCamera = panel.HostCanvas.worldCamera;
        }
    }

    // Scratch for the modal camera scan (double-draw detection); reused, no per-frame alloc.
    private static readonly System.Text.StringBuilder DiagSb = new(256);

    /// <summary>
    /// FLOATED-MODAL FLICKER INSTRUMENTATION. Change-gated per-frame snapshot of a modal
    /// host + its adopted child canvases, plus a scan of every OTHER enabled camera that
    /// renders the host's layer (a second camera double-drawing the world-space modal is a
    /// prime flicker suspect — the scenario head camera runs mask 0xFFFFFFFF and the game's
    /// UICamera also renders the UI layer). Logs ONLY when a value actually changes, so a
    /// genuinely stable float produces exactly one baseline line and then silence; any
    /// per-frame churn (re-place, re-fit, enabled/sorting toggling, a child dropping out of
    /// order, a camera appearing) prints a diff line the next hardware log can reason from.
    /// </summary>
    private static void DiagnoseModal(ConvertedPanel panel, bool force)
    {
        if (panel.HostGo == null || panel.HostCanvas == null || panel.HostRect == null)
            return;

        Transform t = panel.HostGo.transform;
        Canvas c = panel.HostCanvas;
        Vector3 p = t.position;
        Vector3 s = t.lossyScale;
        Rect r = panel.HostRect.rect;
        string cam = c.worldCamera != null ? c.worldCamera.name : "<null>";
        float age = Time.unscaledTime - panel.DiagConvertedAt;

        // Host snapshot (rounded so sub-mm head jitter does not spam; a real re-place moves cm).
        DiagSb.Clear();
        DiagSb.Append("host pos=").Append(p.x.ToString("F2")).Append(',').Append(p.y.ToString("F2"))
            .Append(',').Append(p.z.ToString("F2"))
            .Append(" scale=").Append(s.x.ToString("F3"))
            .Append(" rect=").Append(r.width.ToString("F0")).Append('x').Append(r.height.ToString("F0"))
            .Append(" canvas.enabled=").Append(c.enabled)
            .Append(" active=").Append(panel.HostGo.activeInHierarchy)
            .Append(" order=").Append(c.sortingOrder)
            .Append(" override=").Append(c.overrideSorting)
            .Append(" mode=").Append(c.renderMode)
            .Append(" cam=").Append(cam)
            .Append(" targetAlive=").Append(panel.Target != null);
        // Adopted children: the render state that actually decides draw order per subtree.
        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            Canvas nc = panel.AdoptedCanvases[i].Canvas;
            if (nc == null)
            {
                DiagSb.Append(" | child#").Append(i).Append("=<dead>");
                continue;
            }
            DiagSb.Append(" | child '").Append(nc.name).Append("' enabled=").Append(nc.enabled)
                .Append(" order=").Append(nc.sortingOrder).Append(" override=").Append(nc.overrideSorting);
        }
        string snapshot = DiagSb.ToString();
        if (force || snapshot != panel.DiagLastSnapshot)
        {
            panel.DiagLastSnapshot = snapshot;
            VRLog.Info("WorldUI", $"MODAL DIAG '{panel.HostGo.name}' (age {age:F1}s): {snapshot}");
        }

        // Camera scan (double-draw): every OTHER enabled camera whose cullingMask includes
        // the host's layer bit will ALSO render this world-space host. Report each with its
        // render target so a backbuffer/display double-draw is obvious vs. a harmless RT.
        // Throttled (~every 20 frames) since Camera.allCameras allocates and cameras rarely
        // change; the change-gate still collapses steady state to a single line.
        if (!force && Time.frameCount < panel.DiagNextCameraScanFrame)
            return;
        panel.DiagNextCameraScanFrame = Time.frameCount + 20;
        int layerBit = 1 << panel.HostGo.layer;
        DiagSb.Clear();
        Camera[] all = Camera.allCameras;
        for (int i = 0; i < all.Length; i++)
        {
            Camera other = all[i];
            if (other == null || !other.enabled || ReferenceEquals(other, WorldCamera))
                continue;
            if ((other.cullingMask & layerBit) == 0)
                continue;
            string tgt = other.targetTexture != null ? other.targetTexture.name : "BACKBUFFER";
            if (string.IsNullOrEmpty(tgt))
                tgt = "BACKBUFFER";
            DiagSb.Append(" [").Append(other.name).Append(" depth=").Append(other.depth.ToString("F0"))
                .Append(" stereo=").Append(other.stereoTargetEye).Append(" →").Append(tgt).Append(']');
        }
        string cams = DiagSb.Length == 0 ? "(none — head camera only)" : DiagSb.ToString();
        if (force || cams != panel.DiagLastCameras)
        {
            panel.DiagLastCameras = cams;
            VRLog.Info("WorldUI", $"MODAL DIAG '{panel.HostGo.name}' second cameras rendering layer " +
                                  $"{panel.HostGo.layer}: {cams}");
        }
    }

    // ---- modality -------------------------------------------------------------------------

    private static bool EffectiveLock => _uiLocked || SoftLocks.Count > 0;

    /// <summary>
    /// Current mirrored modality (game UI lock OR module soft lock). Consumers that
    /// bypass raycasters (physical buttons via ExecuteEvents) must check this.
    /// </summary>
    internal static bool IsLockedNow => EffectiveLock;

    /// <summary>Mirror of the game's UI lock (subscribe VREvents.UiLockChanged → here).</summary>
    internal static void SetUiLocked(bool locked)
    {
        if (_uiLocked == locked)
            return;
        _uiLocked = locked;
        _lockDirty = true;
    }

    /// <summary>
    /// Module-side soft lock (e.g. while the phase banner blocks input full-screen
    /// in 2D — our converted surfaces must not accept pokes either, UI-ARCH §9.7).
    /// </summary>
    internal static void SetSoftLock(object requester, bool locked)
    {
        bool changed = locked ? SoftLocks.Add(requester) : SoftLocks.Remove(requester);
        if (changed)
            _lockDirty = true;
    }

    // ---- camera culling mask ---------------------------------------------------------------

    /// <summary>Non-conversion users of world-space UI (tooltips) keep the mask alive.</summary>
    internal static void AddMaskRequest() => _maskRequests++;

    internal static void RemoveMaskRequest() => _maskRequests = Mathf.Max(0, _maskRequests - 1);

    /// <summary>
    /// The scenario/head camera does not render the UI layer (the UICamera does, but
    /// only for its screen-space canvases). World-space UI needs the head camera to
    /// include the UI bit; recorded and restored when the last conversion goes away.
    /// </summary>
    private static void EnsureCameraMask()
    {
        Camera? cam = WorldCamera;
        if (cam == null)
            return;

        if (_maskedCamera != cam)
        {
            RestoreCameraMask();
            if ((cam.cullingMask & (1 << UiLayer)) == 0)
            {
                _maskedCamera = cam;
                _originalCullingMask = cam.cullingMask;
                cam.cullingMask |= 1 << UiLayer;
            }
        }
    }

    private static void RestoreCameraMask()
    {
        if (_maskedCamera != null)
        {
            _maskedCamera.cullingMask = _originalCullingMask;
            _maskedCamera = null;
        }
    }
}
