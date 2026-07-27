using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them to something
// prettier. Compile order is the filename sort of the SDK's default `**/*.cs`
// glob, and a partial class's members land in metadata in compile order, so the
// numbering is what makes the four parts concatenate back into the original
// member order. It matters more here than elsewhere: the seven shared static
// scratch buffers are declared beside the sweeps that use them, i.e. spread
// across parts 1-4, and reordering their initializers is exactly the kind of
// change the guard reports as MOVED rather than CHANGED.

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
internal static partial class CanvasConversion
{
    // Built-in "UI". Converted panels host GAME-owned uGUI trees, which are never
    // re-layered (reversibility) — so the hosts stay on the game's UI layer and this
    // class owns the head camera's UI culling bit. Mod-owned visuals use the separate
    // dedicated mod layer instead (Core.VRLayers; docs/CAMERA-POLICY.md §2).
    private const int UiLayer = 5;

    private static readonly List<ConvertedPanel> Active = new(16);
    private static readonly HashSet<object> SoftLocks = new();

    /// <summary>
    /// Issue 2 fallback reference: the FIRST (cold) captured height per converted window name,
    /// used as the stable height cap when the root canvas has no <see cref="CanvasScaler"/> to read
    /// a design-space reference resolution from. A cold first open reads the game window before its
    /// layout grows, so this is the compact height every warm reopen clamps back down to.
    /// </summary>
    private static readonly Dictionary<string, float> FirstCapHeights = new();

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
    /// <paramref name="capHeightToCanvas"/> (bug #7, ESC/Options full-screen-menu family ONLY):
    /// cap the captured height to the root canvas REFERENCE height (~1080 — the compact
    /// first-open height) so the panel height stops depending on the game's post-layout root
    /// growth. The scenario ESC menu has a tall content column (~2040 px, only the top ~1080
    /// populated); a COLD first open reads the root before layout expands it (~1080), every WARM
    /// reopen reads the settled ~2040 → a ~1.9x taller panel with an empty bottom. Clamping the
    /// height here (before it becomes the host size AND the pinned target frame the content fit
    /// clamps to) makes every open land the same compact height. Width and scale are untouched.
    /// Guarded to full-screen menus by the caller (<c>ModalFallback.IsFullScreenMenu</c>);
    /// normal floated modals/tooltips/cards keep their exact captured size.
    /// </summary>
    internal static ConvertedPanel? Convert(RectTransform? target, string name, bool pokeable = true,
        PokeSurfaceTuning? pokeTuning = null, bool? fitContent = null, bool flatten2D = false,
        int sortingOrder = 0, bool diagnostic = false, bool useModLayer = false,
        bool transparentBackground = false, bool fitOneShot = false, bool capHeightToCanvas = false,
        bool keepBackgroundHidden = false)
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

        // Bug #7 (pause-menu height): a full-screen menu (ESC / Options family) captures its
        // HEIGHT verbatim from the game window rect here. The scenario ESC menu has a TALL
        // content column (~2040 px, only the top ~1080 populated) — a COLD first open reads the
        // root before the game layout expands it (~1080), every WARM reopen reads the settled
        // ~2040, so the panel opened ~1.9x taller with an empty bottom. Cap the captured height
        // to the root canvas REFERENCE height (its RectTransform rect height — the compact
        // first-open height, ~1080) BEFORE it becomes the host size (:sizeDelta below) AND the
        // pinned target frame (:target.sizeDelta) the one-shot content fit clamps to. Cold is
        // already <= the cap (unchanged); warm is clamped down to the same compact height →
        // identical every open. Width and scale are untouched. Read from the SAME root canvas the
        // conversion resolves (target still under its original parent here); if it cannot be
        // resolved, the cap is skipped rather than guessing a magic number. Guarded to the
        // ESC/Options full-screen-menu family via capHeightToCanvas — normal modals never enter.
        float heightCapFrom = 0f, heightCapTo = 0f;
        bool heightCapped = false;
        string heightCapSource = "none";
        if (capHeightToCanvas)
        {
            // Issue 2 (pause menu taller on warm opens): the old cap read the LIVE post-layout
            // root-canvas rect (rootCanvas.rect.size.y), which GROWS with content (cold ~1080,
            // warm ~2040) — so warm size.y(2040) > refHeight(2040) was false and the cap never
            // fired. Read a STABLE reference instead: the root canvas's CanvasScaler
            // referenceResolution.y (~1080, constant regardless of layout growth); fall back to
            // the FIRST successful (cold) capture height recorded per window if there is no scaler.
            float refHeight = ResolveStableHeightCap(target, name, out heightCapSource);
            if (refHeight > 1f && size.y > refHeight)
            {
                heightCapFrom = size.y;
                heightCapTo = refHeight;
                size.y = refHeight;
                heightCapped = true;
            }
            // Record the FIRST (cold) capture height for this window as the fallback reference:
            // a cold first open reads the root before layout expands it, so size.y here is the
            // compact reference every subsequent (warm) open should clamp back down to.
            if (!FirstCapHeights.ContainsKey(name))
                FirstCapHeights[name] = size.y;
        }

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

        // Task #4: a world-space host has no screen edge — every ScrollRect viewport in the
        // subtree must carry a WORKING clipper or scrolled-out content renders past the window.
        EnsureScrollClipping(panel);

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
            panel.FitOneShot = fitOneShot; // item 1: full-screen menus fit once then lock (no re-fit flicker)
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
            // Issue 5: for the full-screen-menu family, leave the backing disabled on Release so a
            // momentary flat re-show on X-close cannot flash the full-window blur veil.
            panel.KeepBackgroundHidden = keepBackgroundHidden;
            HideFullScreenBackground(panel, initial: true);
        }

        // Sub-item A: for a floated modal that gets the mod-layer move and/or the
        // background hide, re-run BOTH every frame for a short settle window so a backing
        // the game instantiates / fades in a few frames after Convert is treated before its
        // first visible frame — killing the reported ~1 s initial flicker.
        if (useModLayer || transparentBackground)
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
                              $"sortingOrder={sortingOrder})" +
                              (heightCapped
                                  ? $" (height capped {heightCapFrom:F0}->{heightCapTo:F0} via {heightCapSource})."
                                  : "."));
        if (diagnostic)
            DiagnoseModal(panel, force: true); // one-shot baseline (host state + camera scan) at float time
        return panel;
    }

    /// <summary>
    /// Issue 2: resolve the STABLE height cap for a full-screen-menu conversion. Prefer the root
    /// canvas's <see cref="CanvasScaler"/> <c>referenceResolution.y</c> — a fixed design value
    /// (~1080) that does NOT grow with post-layout content, unlike the live root-canvas rect the
    /// old cap read. Falls back to the first (cold) capture height recorded for this window in
    /// <see cref="FirstCapHeights"/>; returns 0 (no cap) when neither is available yet.
    /// </summary>
    private static float ResolveStableHeightCap(RectTransform target, string name, out string source)
    {
        Canvas? rootCanvas = target.GetComponentInParent<Canvas>();
        Canvas? root = rootCanvas != null ? rootCanvas.rootCanvas : null;
        if (root != null)
        {
            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null
                && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                && scaler.referenceResolution.y > 1f)
            {
                source = "CanvasScaler.referenceResolution";
                return scaler.referenceResolution.y;
            }
        }
        if (FirstCapHeights.TryGetValue(name, out float first) && first > 1f)
        {
            source = "first-capture fallback";
            return first;
        }
        source = "none";
        return 0f;
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

}
