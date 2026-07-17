using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
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
    /// </summary>
    internal static ConvertedPanel? Convert(RectTransform? target, string name, bool pokeable = true,
        PokeSurfaceTuning? pokeTuning = null, bool? fitContent = null)
    {
        if (target == null)
        {
            VRLog.Warn("WorldUI", $"CanvasConversion.Convert({name}): target is null/destroyed.");
            return null;
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

        Active.Add(panel);
        EnsureCameraMask();
        VRLog.Info("WorldUI", $"Converted '{name}' to world space ({size.x:F0}x{size.y:F0} px).");
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

        if (panel.Target != null)
        {
            RectTransform target = panel.Target;
            if (panel.OriginalParent != null)
                target.SetParent(panel.OriginalParent, worldPositionStays: false);
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

            // Tests #19/#20: pooled/late children may bring nested canvases after
            // Convert, and the game can flip overrideSorting back on live.
            if (Time.frameCount >= panel.CanvasSweepNextFrame)
                AdoptNestedCanvases(panel);

            TickFit(panel); // test #14 item 1: content fit + growth re-fit (throttled)
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
