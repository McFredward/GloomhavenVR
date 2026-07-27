using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class CanvasConversion
{
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

}
