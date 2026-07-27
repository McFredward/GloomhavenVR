using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

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
    ///
    /// TASK #7 (dropdown menus unusable in VR) — two carve-outs, both verified against
    /// the decompiled <c>TMP_Dropdown</c> (Unity.TextMeshPro.dll; <c>ExtendedDropdown</c>
    /// derives from it):
    /// - The "Dropdown List" the Dropdown spawns INSIDE the subtree on open
    ///   (<c>Show()</c>: canvas with <c>overrideSorting=true, sortingOrder=30000</c>)
    ///   and the fullscreen "Blocker" it parents under the ROOT canvas — the HOST
    ///   (<c>CreateBlocker(rootCanvas)</c>: order 29999, clear Image, Button→Hide) are
    ///   adopted KEEPING overrideSorting: an open dropdown must render ON TOP of the
    ///   whole menu, and the Blocker must catch outside-clicks to close it. Generic
    ///   adoption cleared the override → the list dropped to host order at its
    ///   hierarchy position (BEHIND siblings drawn later, "vanished"), and since
    ///   <c>Show()</c> is a no-op while <c>m_Dropdown != null</c>, the next click on
    ///   the dropdown could not reopen it either. Their orders are re-based from the
    ///   game's 30000/29999 to <see cref="DropdownListSortingOrder"/>/<see
    ///   cref="DropdownBlockerSortingOrder"/> — still above every host canvas (1000)
    ///   and the ModalCloseButton X (1100), but BELOW the laser beam/dot visuals
    ///   (RayInteractor.RayVisualSortingOrder=5000) so the pointer dot stays visible
    ///   over the open list. Registered as nested raycast surfaces like everything
    ///   else, so the laser clicks the item Toggles and the Blocker
    ///   (UguiPointer.Beats: 4000/3999 beat host content; the list beats the Blocker).
    /// - The game DESTROYS both on close (<c>DelayedDestroyDropdownList</c>/
    ///   <c>DestroyBlocker</c>) — dead records are pruned at sweep time so the
    ///   adoption list cannot grow per open/close cycle and Release has nothing
    ///   stale to restore.
    /// Returns true when a NEW canvas was adopted this pass (callers re-run the
    /// mod-layer sweep immediately so a freshly spawned list/blocker never renders
    /// on the game UI layer).
    /// </summary>
    private static bool AdoptNestedCanvases(ConvertedPanel panel)
    {
        // Task #7: modal hosts now run this scan EVERY frame (see Tick) — advance the
        // periodic schedule only when it was actually due, otherwise the per-frame runs
        // would push the deadline forever forward and the schedule-driven consumers
        // (the ApplyModLayer re-sweep for pooled children) would never fire again.
        if (Time.frameCount >= panel.CanvasSweepNextFrame)
            panel.CanvasSweepNextFrame = Time.frameCount + CanvasSweepIntervalFrames;
        if (panel.Target == null)
            return false;

        // Task #7: prune records whose canvas the game destroyed (a closed dropdown
        // list/blocker). Nothing to restore — the GameObject is gone.
        for (int i = panel.AdoptedCanvases.Count - 1; i >= 0; i--)
        {
            if (panel.AdoptedCanvases[i].Canvas == null)
                panel.AdoptedCanvases.RemoveAt(i);
        }

        bool anyNew = false;
        CanvasScratch.Clear();
        panel.Target.GetComponentsInChildren(includeInactive: true, CanvasScratch);
        for (int i = 0; i < CanvasScratch.Count; i++)
            anyNew |= AdoptCanvas(panel, CanvasScratch[i]);
        CanvasScratch.Clear();

        // Task #7: the Dropdown's fullscreen "Blocker" is parented under the ROOT
        // canvas — the HOST — i.e. OUTSIDE the converted target subtree the loop above
        // scans. Adopt it from the host's DIRECT children, name-gated so mod-owned
        // sibling canvases (ModalCloseButton's X) are never touched.
        if (panel.HostRect != null)
        {
            for (int i = 0; i < panel.HostRect.childCount; i++)
            {
                Transform child = panel.HostRect.GetChild(i);
                if (child == null || child.name != DropdownBlockerName)
                    continue;
                Canvas? blocker = child.GetComponent<Canvas>();
                if (blocker != null)
                    anyNew |= AdoptCanvas(panel, blocker);
            }
        }
        return anyNew;
    }

    /// <summary>Name of the transient list GameObject a uGUI/TMP Dropdown spawns on open.</summary>
    private const string DropdownListName = "Dropdown List";

    /// <summary>Name of the fullscreen close-on-outside-click catcher a Dropdown parents under the root canvas.</summary>
    private const string DropdownBlockerName = "Blocker";

    /// <summary>
    /// Task #7: adopted sorting order of an open "Dropdown List" — above every host canvas
    /// (1000) and the modal X (1100), below the laser beam/dot visuals (5000).
    /// </summary>
    private const int DropdownListSortingOrder = 4000;

    /// <summary>Task #7: adopted order of the Dropdown "Blocker" — one under the list, same rationale.</summary>
    private const int DropdownBlockerSortingOrder = 3999;

    private static bool IsDropdownOverlay(Canvas nested) =>
        nested.name == DropdownListName || nested.name == DropdownBlockerName;

    /// <summary>
    /// Adopt (or re-assert) ONE nested canvas for <paramref name="panel"/> — see
    /// <see cref="AdoptNestedCanvases"/> for the contract, incl. the task-#7 dropdown
    /// overlay carve-out. Returns true only when the canvas was NEWLY adopted.
    /// </summary>
    private static bool AdoptCanvas(ConvertedPanel panel, Canvas nested)
    {
        if (nested == null || ReferenceEquals(nested, panel.HostCanvas))
            return false;

        // Already adopted → re-assert per sweep (change-only writes; no log — the
        // adoption line below already documented this canvas once).
        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            NestedCanvasRecord existing = panel.AdoptedCanvases[i];
            if (!ReferenceEquals(existing.Canvas, nested))
                continue;
            if (existing.KeepOverrideSorting)
            {
                // Task #7: a dropdown overlay stays TOP-sorted while it lives.
                if (!nested.overrideSorting)
                    nested.overrideSorting = true;
                if (nested.sortingOrder != existing.OverlaySortingOrder)
                    nested.sortingOrder = existing.OverlaySortingOrder;
            }
            else if (nested.overrideSorting)
            {
                nested.overrideSorting = false;
            }
            if (nested.worldCamera != panel.HostCanvas.worldCamera)
                nested.worldCamera = panel.HostCanvas.worldCamera;
            return false;
        }

        bool overlay = IsDropdownOverlay(nested);
        var record = new NestedCanvasRecord
        {
            Canvas = nested,
            OriginalOverrideSorting = nested.overrideSorting,
            OriginalWorldCamera = nested.worldCamera,
            KeepOverrideSorting = overlay,
            OverlaySortingOrder = nested.name == DropdownBlockerName
                ? DropdownBlockerSortingOrder
                : DropdownListSortingOrder,
        };
        if (overlay)
        {
            nested.overrideSorting = true;                    // keep the on-top contract
            nested.sortingOrder = record.OverlaySortingOrder; // re-based below the ray visuals
        }
        else
        {
            nested.overrideSorting = false;
        }
        nested.worldCamera = panel.HostCanvas.worldCamera;
        if (nested.GetComponent<GraphicRaycaster>() == null)
            record.AddedRaycaster = nested.gameObject.AddComponent<GraphicRaycaster>();

        UguiPokeSurfaces.RegisterNested(panel.HostCanvas, nested);
        panel.AdoptedCanvases.Add(record);
        VRLog.Info("WorldUI", overlay
            ? $"Adopted DROPDOWN overlay canvas '{nested.name}' in '{panel.HostGo.name}' " +
              $"(overrideSorting KEPT, sortingOrder re-based →{record.OverlaySortingOrder}, raycaster " +
              (record.AddedRaycaster != null ? "added" : "existing") +
              ") — renders on top of the menu, raycasts merged with the host (task #7)."
            : $"Adopted nested canvas '{nested.name}' in '{panel.HostGo.name}' " +
              $"(overrideSorting {record.OriginalOverrideSorting}→false, " +
              $"sortingOrder={nested.sortingOrder}, raycaster " +
              (record.AddedRaycaster != null ? "added" : "existing") +
              ") — inherits host sorting/depth, raycasts merged with the host.");
        return true;
    }

    // ---- task #4: world-space scroll clipping ----------------------------------------------

    // Scratch buffer (scroll-clip sweep only; reused, no per-call allocations).
    private static readonly List<ScrollRect> ScrollScratch = new(4);

    /// <summary>
    /// Task #4 (scrolling extends the menu upward — root cause): the game's full-screen
    /// options submenus scroll a settings list whose viewport needs NO mask in 2D — the
    /// window fills the screen, so anything scrolled past the viewport is off-screen and the
    /// SCREEN clips it. On a world-space host there is no screen edge: rows scrolled out of
    /// the viewport rendered above/below the floated window, which read as the menu
    /// "elongating vertically" on every scroll instead of clipping at a fixed frame.
    ///
    /// Fix: guarantee a WORKING clipper on every ScrollRect viewport inside the converted
    /// subtree — re-enable a disabled game-owned <see cref="RectMask2D"/>, or ADD one when the
    /// viewport has neither an enabled RectMask2D nor a functioning stencil <see cref="Mask"/>
    /// (the WorldTooltips frame-mask pattern). Fully reversible: added masks are destroyed and
    /// enabled ones re-disabled on <see cref="Release"/>. Runs at Convert and on the periodic
    /// sweep (pooled/late ScrollRects); writes are add-once, so steady state is a cheap
    /// component walk. Dropdown-list overlays ship uGUI's template viewport mask already and
    /// are left untouched by the HasClipper check.
    /// </summary>
    private static void EnsureScrollClipping(ConvertedPanel panel)
    {
        if (panel.Target == null)
            return;

        // Prune records whose component/GameObject the game destroyed.
        for (int i = panel.AddedScrollMasks.Count - 1; i >= 0; i--)
        {
            if (panel.AddedScrollMasks[i] == null)
                panel.AddedScrollMasks.RemoveAt(i);
        }
        for (int i = panel.EnabledScrollMasks.Count - 1; i >= 0; i--)
        {
            if (panel.EnabledScrollMasks[i] == null)
                panel.EnabledScrollMasks.RemoveAt(i);
        }

        ScrollScratch.Clear();
        panel.Target.GetComponentsInChildren(includeInactive: true, ScrollScratch);
        for (int i = 0; i < ScrollScratch.Count; i++)
        {
            ScrollRect sr = ScrollScratch[i];
            if (sr == null)
                continue;
            // The clip lives on the viewport (the content's direct parent when the optional
            // serialized viewport reference is empty — that parent may be the ScrollRect itself).
            RectTransform? viewport = sr.viewport != null
                ? sr.viewport
                : sr.content != null ? sr.content.parent as RectTransform : null;
            if (viewport == null)
                continue;

            RectMask2D existing = viewport.GetComponent<RectMask2D>();
            if (existing != null)
            {
                if (!existing.enabled)
                {
                    existing.enabled = true; // game-owned but off → turn it on while converted
                    panel.EnabledScrollMasks.Add(existing);
                    VRLog.Info("WorldUI", $"SCROLL CLIP: enabled the disabled RectMask2D on viewport " +
                                          $"'{viewport.name}' in '{panel.HostGo.name}' — scrolled-out content " +
                                          "now clips at the viewport (task #4; re-disabled on release).");
                }
                continue; // an enabled RectMask2D is a working clipper
            }
            Mask stencil = viewport.GetComponent<Mask>();
            if (stencil != null && stencil.enabled && stencil.graphic != null && stencil.graphic.enabled)
                continue; // a functioning stencil mask clips already

            RectMask2D added = viewport.gameObject.AddComponent<RectMask2D>();
            if (added == null)
                continue; // AddComponent refused (unexpected) — leave the viewport as-is
            panel.AddedScrollMasks.Add(added);
            VRLog.Info("WorldUI", $"SCROLL CLIP: viewport '{viewport.name}' of ScrollRect '{sr.name}' in " +
                                  $"'{panel.HostGo.name}' had NO clipper (screen-edge clipped in 2D) — " +
                                  "RectMask2D added so scrolling clips at a fixed window instead of " +
                                  "elongating it (task #4; removed on release).");
        }
        ScrollScratch.Clear();
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
            // Task #4 guard: an Image driving a stencil Mask is a CLIPPER, not a backing —
            // disabling it would stop the stencil write and break the mask's whole subtree
            // (scroll viewports use exactly this setup). Never treat it as a background.
            if (g.GetComponent<Mask>() != null)
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
    /// Effective-alpha floor for the content FIT measure: anything fainter than this is
    /// treated as invisible and neither sizes nor centers the panel (historic 0.05 value —
    /// unchanged, so fit geometry is identical to the shipped builds).
    /// </summary>
    private const float FitMinAlpha = 0.05f;

    /// <summary>
    /// Task #6 (pause window hard-cut behind the options menu): effective-alpha floor for
    /// DEPTH-MASK quad emission — deliberately HIGHER than <see cref="FitMinAlpha"/>. The
    /// hardware screenshot showed the parent pause window cut along one clean straight edge
    /// behind the options menu although the options window has NO visible content in that
    /// region: a barely-visible full-width element (a faint layout-container Image / the
    /// gradient title-banner strip, effective alpha just over 0.05) passed the shared 0.05
    /// test and emitted a WIDE depth quad, whose stamp made every later-drawn transparent —
    /// including the pause window's own canvas, which lies BEHIND the options plane below
    /// their intersection line — fail ZTest across the whole "empty" region (the WORLD still
    /// showed there because it draws before the mask, colour already in the buffer — exactly
    /// the observed sky-through-the-cut). A ≤15 %-opaque graphic reads as "nothing there",
    /// so it must not stamp depth either; genuinely visible content (rows, buttons, dialogs)
    /// is far above this floor and masks exactly as before, keeping the original purpose
    /// (HUD/initiative must not bleed through actual content) intact.
    /// </summary>
    internal const float MaskMinAlpha = 0.15f;

    /// <summary>
    /// Task #4/#5 shared per-graphic measure: the visibility test both unions use (enabled,
    /// not culled, effective alpha ≥ <paramref name="minAlpha"/>, non-degenerate draw rect)
    /// plus the graphic's host-local bounds, CLAMPED to its enclosing clipper's rect
    /// (<see cref="RectMask2D"/> / stencil <see cref="Mask"/> — i.e. a ScrollRect viewport):
    /// a settings row scrolled out of its viewport is CLIPPED at render time, so it must
    /// neither grow the content FIT nor stamp depth-mask coverage. False = the graphic
    /// contributes nothing (invisible, empty, or fully scrolled out). The alpha floor is
    /// caller-specific (task #6): <see cref="FitMinAlpha"/> for the content fit,
    /// <see cref="MaskMinAlpha"/> for depth-mask emission.
    /// </summary>
    private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g,
        out Vector2 gMin, out Vector2 gMax, float minAlpha = FitMinAlpha)
    {
        gMin = default;
        gMax = default;
        if (g == null || !g.enabled || g.canvasRenderer == null || g.canvasRenderer.cull)
            return false;
        // Effective alpha: own color × hierarchy (CanvasGroup) alpha.
        if (g.color.a * g.canvasRenderer.GetInheritedAlpha() < minAlpha)
            return false;

        var rect = (RectTransform)g.transform;
        // Zero draw size = nothing on screen (collapsed layout cells, empty
        // stretch containers with a Graphic) — must not anchor the union at
        // their corner points (test #16 measurement tightening).
        Rect drawRect = rect.rect;
        if (drawRect.width < 0.5f || drawRect.height < 0.5f)
            return false;

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
                return false; // fully scrolled out of its viewport
        }

        gMin = min;
        gMax = max;
        return true;
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
        bool any = false;

        ClipperMemo.Clear();
        GraphicScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            if (!TryGetVisibleHostRect(panel, GraphicScratch[i], out Vector2 gMin, out Vector2 gMax))
                continue;
            min = Vector2.Min(min, gMin);
            max = Vector2.Max(max, gMax);
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

        Vector2 sz = max - min;
        if (sz.x < 32f || sz.y < 32f)
            return false; // degenerate (mid scale-in animation) — caller retries

        const float Padding = 12f;
        sz += Vector2.one * (2f * Padding);
        sz.x = Mathf.Min(sz.x, frameMax.x - frameMin.x);
        sz.y = Mathf.Min(sz.y, frameMax.y - frameMin.y);

        size = sz;
        center = (min + max) * 0.5f;
        return true;
    }

    /// <summary>
    /// Task #5 (transparent gaps must stay transparent for OTHER MENUS too): collect ONE
    /// host-local rect PER visible graphic — <c>Vector4(minX, minY, maxX, maxY)</c> in
    /// host-local pixels — with the exact same visibility test the content fit uses
    /// (<see cref="TryGetVisibleHostRect"/>: enabled, not culled, effective alpha ≥ 0.05,
    /// non-degenerate draw rect, viewport-clamped per task #4). Replaces the old single
    /// union rect (<c>TryMeasureVisibleUnion</c>): the union stamped menu-plane depth
    /// across the GAPS between settings rows, which the world showed through (drawn
    /// earlier, colour already in the buffer) but other transparent menus did NOT (drawn
    /// later, depth-tested against the stamp). <see cref="GrabbableModal.SyncDepthMask"/>
    /// builds a per-graphic quad mesh from these rects, so depth is stamped only where
    /// content (approximately — its rect) actually renders and the gaps stay open for
    /// everything behind, menus included. Unclamped to the target frame on purpose: an
    /// open dropdown list may extend past it and the mask should back it wherever it draws.
    /// Rects beyond <paramref name="maxCount"/> are merged into the last slot (coverage is
    /// never lost, only gap fidelity in the overflow). Returns the rect count (0 = nothing
    /// visible; caller disables the mask). Reuses the fit scratch buffers (single-threaded,
    /// never re-entered).
    ///
    /// Task #6 (pause window hard-cut): emission uses the STRICTER <see cref="MaskMinAlpha"/>
    /// floor (0.15) instead of the fit's 0.05 — a barely-visible full-width container must not
    /// stamp a depth quad that hard-cuts other floated menus behind the plane (see the const's
    /// doc). <paramref name="sources"/> (optional) receives the emitting <see cref="Graphic"/>
    /// per rect, 1:1 with <paramref name="rects"/> (null entry = the overflow union slot) — the
    /// depth-mask rebuild diagnostic uses it to NAME wide/suspect quads in the hardware log.
    ///
    /// Task #6b (options window hard-cuts the pause menu — CULPRIT: 'Main Area/Viewport'):
    /// graphics that RENDER NO PIXELS must not stamp depth either. The hardware diag showed the
    /// options window's ScrollRect VIEWPORT image ('Main Area/Viewport', Image, sprite=null,
    /// a=1.00) emitting an 867x833 px quad covering the whole right pane — the viewport is an
    /// invisible clipper/raycast target (its Image drives a stencil <see cref="Mask"/> with
    /// <c>showMaskGraphic=false</c>, i.e. it draws ONLY to the stencil buffer, ColorMask 0 —
    /// zero visible pixels), yet it passed the alpha test and its depth stamp hard-cut the pause
    /// menu floating behind along one clean edge. <see cref="IsNonRenderingMaskEmitter"/>
    /// excludes that whole class from EMISSION ONLY (the content fit is untouched); excluded
    /// names + the matched rule land in <see cref="LastMaskExclusions"/> for the rebuild diag.
    /// </summary>
    internal static int CollectVisibleMaskRects(ConvertedPanel panel, List<Vector4> rects, int maxCount,
        List<Graphic?>? sources = null)
    {
        rects.Clear();
        sources?.Clear();
        LastMaskExclusions.Clear();
        if (panel == null || panel.Target == null || panel.HostRect == null)
            return 0;

        ClipperMemo.Clear();
        GraphicScratch.Clear();
        panel.Target.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (!TryGetVisibleHostRect(panel, g, out Vector2 gMin, out Vector2 gMax, MaskMinAlpha))
                continue;
            // Task #6b: a graphic that renders no pixels (invisible clipper / viewport /
            // raycast catcher) must not stamp depth. Checked only AFTER the (cheap) visibility
            // test passed, so the component lookups run for the ~dozens of emitting graphics,
            // not the whole subtree.
            if (IsNonRenderingMaskEmitter(g, out string rule))
            {
                if (LastMaskExclusions.Count < MaskExclusionLogCap)
                {
                    string parent = g.transform.parent != null ? g.transform.parent.name : "<root>";
                    LastMaskExclusions.Add(
                        $"'{parent}/{g.name}' {gMax.x - gMin.x:F0}x{gMax.y - gMin.y:F0}px [{rule}]");
                }
                continue;
            }
            if (rects.Count < maxCount)
            {
                rects.Add(new Vector4(gMin.x, gMin.y, gMax.x, gMax.y));
                sources?.Add(g);
            }
            else
            {
                // Cap reached: widen the last slot to the union of the overflow — coverage
                // stays correct, only the per-rect gap fidelity degrades past the cap.
                Vector4 last = rects[rects.Count - 1];
                rects[rects.Count - 1] = new Vector4(
                    Mathf.Min(last.x, gMin.x), Mathf.Min(last.y, gMin.y),
                    Mathf.Max(last.z, gMax.x), Mathf.Max(last.w, gMax.y));
                if (sources != null)
                    sources[sources.Count - 1] = null; // slot is now an anonymous overflow union
            }
        }
        GraphicScratch.Clear();
        return rects.Count;
    }

    /// <summary>Task #6b diag: cap on excluded-emitter entries kept per collection pass (log hygiene).</summary>
    private const int MaskExclusionLogCap = 8;

    /// <summary>
    /// Task #6b diag: graphics EXCLUDED from depth-mask emission by
    /// <see cref="IsNonRenderingMaskEmitter"/> during the LAST
    /// <see cref="CollectVisibleMaskRects"/> pass — "'parent/name' WxHpx [rule]" per entry,
    /// capped at <see cref="MaskExclusionLogCap"/>. Read by the depth-mask rebuild diagnostic
    /// (<c>GrabbableModal.LogDepthMaskRebuild</c>, same tick, same collection) so the hardware
    /// log states WHICH rule caught each invisible emitter ('Main Area/Viewport' &amp; friends).
    /// </summary>
    internal static readonly List<string> LastMaskExclusions = new(MaskExclusionLogCap);

    /// <summary>
    /// Task #6b: true when <paramref name="g"/> renders NO pixels despite passing the
    /// alpha/enabled visibility test — such a graphic must never stamp a depth-mask quad
    /// (it visually reads as "nothing there", so cutting another floated menu behind the
    /// plane along its rect is exactly the observed hard-cut bug). Three classes, checked
    /// in order; <paramref name="rule"/> names the one that matched:
    ///
    /// (a) STENCIL-CLIPPER IMAGE: the graphic drives an enabled stencil <see cref="Mask"/>
    ///     with <c>showMaskGraphic == false</c> — uGUI then renders it with ColorMask 0
    ///     (stencil write only), i.e. literally zero visible pixels. This is what the
    ///     options window's 'Main Area/Viewport' (867x833 px, sprite=null, a=1.00) and the
    ///     ESC menu's 'Scroll View/Viewport' (388x1003) are: ScrollRect viewport clippers.
    ///     A Mask WITH <c>showMaskGraphic == true</c> draws its graphic normally and is
    ///     deliberately NOT excluded.
    /// (b) SCROLLRECT VIEWPORT: the rect IS some ScrollRect's viewport (the serialized
    ///     <c>.viewport</c>, or the content's parent when that reference is empty) — the
    ///     clipping window itself, an invisible frame/raycast surface, never visible
    ///     content. Belt-and-braces for viewports clipped via <see cref="RectMask2D"/>
    ///     (including the ones task #4 adds ours to), whose Image is a raycast catcher.
    /// (c) INVISIBLE RAYCAST CATCHER: sprite-less Image in the default (~white) colour —
    ///     the classic full-area click-catcher pattern. Real visible backings in this UI
    ///     all carry sprites ('Panel', 'Panel_Divider', 'Mod_Frame', …) and tinted colours,
    ///     so a sprite-null near-white Image is a hit surface, not content.
    ///
    /// TMP text / RawImage / sprited Images fall through — they are real content and keep
    /// masking exactly as before ('UI Menu Panel' sprite='Panel', the row 'Background'
    /// Panel_Divider strips, buttons, dialogs).
    /// </summary>
    private static bool IsNonRenderingMaskEmitter(Graphic g, out string rule)
    {
        rule = string.Empty;
        if (g is not Image img)
            return false;

        // (a) stencil clipper: Mask with the graphic hidden → stencil-only draw (ColorMask 0).
        Mask stencil = img.GetComponent<Mask>();
        if (stencil != null && stencil.enabled && !stencil.showMaskGraphic)
        {
            rule = "Mask, showMaskGraphic=false: stencil-only, draws no pixels";
            return true;
        }

        // (b) ScrollRect viewport: the clipping window rect itself.
        var rect = (RectTransform)img.transform;
        ScrollRect? owner = img.GetComponentInParent<ScrollRect>();
        if (owner != null)
        {
            RectTransform? viewport = owner.viewport != null
                ? owner.viewport
                : owner.content != null ? owner.content.parent as RectTransform : null;
            if (ReferenceEquals(viewport, rect))
            {
                rule = "ScrollRect viewport: invisible clipper/raycast frame";
                return true;
            }
        }

        // (c) classic invisible raycast catcher: sprite-less, default-white Image.
        if (img.sprite == null && img.overrideSprite == null
            && img.color.r >= 0.95f && img.color.g >= 0.95f && img.color.b >= 0.95f)
        {
            rule = "sprite=null near-white: raycast catcher";
            return true;
        }
        return false;
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
    /// </summary>
    internal static bool FitHostToContent(ConvertedPanel panel, RectTransform? contentRoot = null)
    {
        if (panel == null || panel.Target == null || panel.HostRect == null)
            return true; // nothing to do, do not retry

        RectTransform root = contentRoot != null && contentRoot.gameObject.activeInHierarchy
                             && contentRoot.IsChildOf(panel.Target)
            ? contentRoot
            : panel.Target;

        if (!TryMeasureContent(panel, root, out Vector2 size, out Vector2 center))
            return false; // nothing visible / degenerate yet (fade-in) — caller retries

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
        panel.FitOneShotApplied = true; // item 1: a real resize happened — owner may re-derive its scale
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

        // Item 5 (pause-menu size consistency): a one-shot full-screen menu must land the SAME
        // compact size on EVERY open. Defer its single fit until the window's layout has SETTLED
        // (deterministic rebuild + a stable measured rect across N checks) instead of committing on
        // the first measurable frame, which locked a different rect each open.
        if (panel.FitOneShot && !panel.FitOneShotApplied)
        {
            SettleOneShotFit(panel);
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
    /// </summary>
    private static void SettleOneShotFit(ConvertedPanel panel)
    {
        RectTransform? contentRoot = panel.FitContentRoot;
        RectTransform root = contentRoot != null && contentRoot.gameObject.activeInHierarchy
                             && contentRoot.IsChildOf(panel.Target)
            ? contentRoot
            : panel.Target;

        // Deterministic layout: rebuild pending layout NOW so every open measures the same settled
        // tree regardless of how warm the layout was (a cold first open is laid out identically to a
        // warm re-open before we measure).
        Canvas.ForceUpdateCanvases();
        if (panel.Target != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Target);

        bool measurable = TryMeasureContent(panel, root, out Vector2 size, out _);
        bool deadline = Time.unscaledTime >= panel.FitFirstDeadline;

        if (!measurable)
        {
            // Nothing visible yet — keep retrying until the deadline, then give up to the full rect
            // (the reveal's deadline floor still pops the menu in, never an invisible one).
            if (deadline && !panel.FitGaveUpLogged)
            {
                panel.FitGaveUpLogged = true;
                panel.FitEnabled = false;
                panel.FitMeasuredOnce = true;
                VRLog.Warn("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' one-shot fit found nothing " +
                                      $"measurable after {FitFirstWarnSeconds:F1}s — keeping the full rect.");
            }
            return;
        }

        // Stability gate: the measured size must hold steady across consecutive checks.
        float tolX = Mathf.Max(panel.FitOneShotStableSize.x, size.x) * FitChangeFraction;
        float tolY = Mathf.Max(panel.FitOneShotStableSize.y, size.y) * FitChangeFraction;
        if (panel.FitOneShotStableCount > 0
            && Mathf.Abs(size.x - panel.FitOneShotStableSize.x) <= tolX
            && Mathf.Abs(size.y - panel.FitOneShotStableSize.y) <= tolY)
        {
            panel.FitOneShotStableCount++;
        }
        else
        {
            panel.FitOneShotStableSize = size;
            panel.FitOneShotStableCount = 1;
        }

        if (panel.FitOneShotStableCount < OneShotSettleChecks && !deadline)
            return; // still settling — keep measuring

        // Settled (or deadline forced): commit the single fit and LOCK. FitMeasuredOnce is still
        // false here, so FitHostToContent applies the now-stable size immediately (no shrink damping).
        FitHostToContent(panel, contentRoot);
        panel.FitMeasuredOnce = true;
        panel.FitNextCheckFrame = Time.frameCount + FitCheckIntervalFrames;
        panel.FitEnabled = false; // one-shot: freeze the rect (no per-frame re-fit flicker)
        VRLog.Info("WorldUI", $"MODAL WINDOW: '{panel.HostGo.name}' full-screen menu fitted ONCE after its " +
                              $"layout settled ({panel.FitOneShotStableCount} stable check(s)) — host rect locked " +
                              "(same compact size every open, no re-fit flicker).");
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

        // User #8 part 2: re-enable the full-window backing/blur we disabled while floated —
        // EXCEPT for the full-screen-menu family (Issue 5). On an X-close the float is released
        // before the game window is guaranteed hidden, and a momentary flat re-show of the ESC
        // menu carrying a re-enabled full-window blur read as a translucent veil over the whole
        // view (+ a left-edge stereo-split flicker). The blur is a flat-screen effect the mod
        // removes anyway, so for those menus it is left disabled; the game re-enables it on its
        // next genuine flat show. Non-menu modals restore normally.
        if (panel.KeepBackgroundHidden)
        {
            if (panel.HiddenBackgrounds.Count > 0)
                VRLog.Info("WorldUI", $"MODAL BACKGROUND: kept {panel.HiddenBackgrounds.Count} full-window " +
                                      "backing/blur image(s) DISABLED on release (full-screen menu) — no " +
                                      "veil on X-close if the game momentarily re-shows the menu flat.");
        }
        else
        {
            for (int i = 0; i < panel.HiddenBackgrounds.Count; i++)
            {
                Graphic g = panel.HiddenBackgrounds[i];
                if (g != null)
                    g.enabled = true;
            }
        }
        panel.HiddenBackgrounds.Clear();

        // Task #4: undo the world-space scroll clipping — masks WE added are destroyed, the
        // game-owned disabled ones we enabled go back to disabled (exact 2D restore).
        for (int i = 0; i < panel.AddedScrollMasks.Count; i++)
        {
            if (panel.AddedScrollMasks[i] != null)
                Object.Destroy(panel.AddedScrollMasks[i]);
        }
        panel.AddedScrollMasks.Clear();
        for (int i = 0; i < panel.EnabledScrollMasks.Count; i++)
        {
            if (panel.EnabledScrollMasks[i] != null)
                panel.EnabledScrollMasks[i].enabled = false;
        }
        panel.EnabledScrollMasks.Clear();

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

            // FIX B (gray band): if the released window reports CLOSED at the game level, force
            // the restored 2D window into the game's own hidden state. While a STICKY menu
            // floated, ModalFallback.ReassertStickyVisible kept re-enabling the window's Canvas +
            // CanvasGroup every tick after the game hid it; releasing it in that forced-visible
            // state parked an ENABLED screen-space window at its 2D home — the ESC menu's
            // width-hugged content column (~412/1920 px, docked LEFT) rendered as a gray band on
            // the left edge of the view until the next float hid it again. Runs on EVERY release
            // path (corner-X, both its branches; the controller-X CloseAll route; escape chord;
            // scenario exit). Scope guards: only targets that ARE a UIWindow root (decision-dock
            // rows / story content / mod-own panels carry no UIWindow → no-op), and only when the
            // game says CLOSED — a window released while genuinely open (manual screen chord,
            // style=screen, module shutdown) must stay visible in the 2D composite. The Canvas is
            // disabled ONLY for a `_disableCanvas` window (mirroring UIWindow.OnTransitionCompleted;
            // its own Show() re-enables it via OnTransitionStarted) — disabling any other window's
            // Canvas would be PERMANENT, because the game never touches `_canvas` for those.
            UIWindow? releasedWindow = target.GetComponent<UIWindow>();
            if (releasedWindow != null && !releasedWindow.IsOpen)
            {
                bool canvasDisabled = false;
                if (releasedWindow._disableCanvas)
                {
                    Canvas? windowCanvas = target.GetComponent<Canvas>();
                    if (windowCanvas != null && windowCanvas.enabled)
                    {
                        windowCanvas.enabled = false;
                        canvasDisabled = true;
                    }
                }
                CanvasGroup? windowGroup = target.GetComponent<CanvasGroup>();
                if (windowGroup != null)
                {
                    windowGroup.alpha = 0f;
                    windowGroup.blocksRaycasts = false;
                    windowGroup.interactable = false;
                }
                VRLog.Info("WorldUI", "release: restored 2D window forced hidden (game reports closed) — " +
                                      $"'{target.name}' (ID {releasedWindow.ID}): canvas " +
                                      $"{(canvasDisabled ? "disabled" : "left as-is")}, CanvasGroup " +
                                      "alpha=0, raycasts off.");
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
            // Task #7: MODAL hosts (Diagnostic) sweep EVERY frame — a uGUI Dropdown
            // spawns its "Dropdown List"/"Blocker" canvases mid-life on a click, and on
            // the 30-frame schedule the open list stayed laser-unclickable (not yet
            // raycast-merged) for up to ~0.4 s. The scan is a cheap component walk of
            // the (few) floated modal subtrees; writes stay change-gated. The heavier
            // mod-layer re-sweep still runs only on schedule OR when this pass actually
            // adopted a NEW canvas (the fresh list/blocker must leave the game UI layer
            // before the game's mono UI Camera double-draws it).
            bool sweepDue = Time.frameCount >= panel.CanvasSweepNextFrame;
            if (earlySettle || sweepDue || panel.Diagnostic)
            {
                bool adoptedNew = AdoptNestedCanvases(panel);
                // User #8: a freshly adopted/pooled child spawns on the game's UI layer —
                // re-assert the mod-layer move so the UI Camera never picks it up.
                if (panel.ModLayerEnabled && (earlySettle || sweepDue || adoptedNew))
                    ApplyModLayer(panel, initial: false);
            }

            // Task #4: pooled/late children can bring ScrollRects after Convert — re-sweep on
            // the periodic schedule so their viewports get a clipper too (change-gated inside).
            if (sweepDue)
                EnsureScrollClipping(panel);

            // User #8 part 2: re-assert the background hide (menu fade-ins can enable the
            // backing image a few frames after the window shows).
            if (panel.HideBackground && (earlySettle || Time.frameCount >= panel.BackgroundSweepNextFrame))
                HideFullScreenBackground(panel, initial: false);

            // Item 3a: reveal the render-hidden modal host once its settle delay has passed. The
            // treatments for THIS frame already ran above (canvas still disabled → harmless), and
            // LateTick re-treats once more (canvas now enabled) before the frame renders — so the
            // first visible frame is fully treated: a clean pop-in with zero flicker.
            // Item 1: a one-shot full-screen menu waits to pop in until its single content
            // fit has actually applied (so it appears already compact, never flashing at the
            // full 1920x2040 rect first), with the fit deadline as a safety floor so an
            // unmeasurable menu still reveals (never an invisible, un-dismissable menu).
            bool oneShotReady = !panel.FitOneShot || panel.FitOneShotApplied
                                || Time.unscaledTime >= panel.FitFirstDeadline;
            if (panel.RevealPending && panel.HostCanvas != null
                && Time.unscaledTime >= panel.RevealNotBefore && oneShotReady)
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
            // Task #7: dropdown overlays (list/blocker) KEEP overrideSorting by design —
            // AdoptCanvas re-asserts their top order; clearing it here would re-create
            // the vanishing-dropdown bug this guard must not fight.
            if (panel.AdoptedCanvases[i].KeepOverrideSorting)
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
