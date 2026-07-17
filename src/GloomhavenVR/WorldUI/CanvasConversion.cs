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

    /// <summary>Host transform for placement by the owning surface.</summary>
    public Transform HostTransform => HostGo.transform;

    /// <summary>True while the moved rect still exists (scene not unloaded).</summary>
    public bool IsAlive => Target != null;
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
    /// </summary>
    internal static ConvertedPanel? Convert(RectTransform? target, string name, bool pokeable = true,
        PokeSurfaceTuning? pokeTuning = null)
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
        if (size.x < 1f || size.y < 1f)
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

        if (pokeable)
        {
            UguiPokeSurfaces.Register(hostCanvas, pokeTuning); // P5: per-canvas press feel (A.10)

            // Test #14 item 1: EVERY pokeable host is content-fitted, not only floated
            // modals — the initiative track converted at its full 1920x1080 window rect
            // (~45x25 m world plane!) and that invisible plane caught the laser before
            // everything behind it (dot off the dialog, tray clicks eaten). Fitting is
            // driven centrally from Tick(), incl. periodic re-fit on content growth.
            panel.FitEnabled = true;
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

    // ---- content fit (tests #13/#14) ------------------------------------------------------

    /// <summary>First fit attempt waits this long after Convert (window show animations run ~0.3 s).</summary>
    private const float FitDelaySeconds = 0.4f;

    /// <summary>If nothing visible was measurable by then, warn once and demote to periodic checks.</summary>
    private const float FitFirstWarnSeconds = 2.5f;

    /// <summary>Periodic growth re-check throttle (~0.4 s at 72 Hz) — the cheap dirty check.</summary>
    private const int FitCheckIntervalFrames = 30;

    /// <summary>Relative size/center change that triggers a re-fit (2 %).</summary>
    private const float FitChangeFraction = 0.02f;

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
    /// rect) but no longer size the panel. The union is clamped to the TARGET's own
    /// frame (not the current — possibly already shrunk — host rect), so a later
    /// content GROWTH (multi-page story, log lines) re-expands the host up to the
    /// window's original rect (test #14: one-shot fits under-covered later pages).
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
        panel.Target.GetWorldCorners(CornerScratch);
        Vector3 frameBl = panel.HostRect.InverseTransformPoint(CornerScratch[0]);
        Vector3 frameTr = panel.HostRect.InverseTransformPoint(CornerScratch[2]);
        min = Vector2.Max(min, new Vector2(frameBl.x, frameBl.y));
        max = Vector2.Min(max, new Vector2(frameTr.x, frameTr.y));

        Vector2 size = max - min;
        if (size.x < 32f || size.y < 32f)
            return false; // degenerate (mid scale-in animation) — caller retries

        const float Padding = 12f;
        size += Vector2.one * (2f * Padding);
        size.x = Mathf.Min(size.x, Mathf.Abs(frameTr.x - frameBl.x));
        size.y = Mathf.Min(size.y, Mathf.Abs(frameTr.y - frameBl.y));

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
    /// threshold inside <see cref="FitHostToContent"/> is the dirty check.
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
            UguiPokeSurfaces.Unregister(panel.HostCanvas);

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
