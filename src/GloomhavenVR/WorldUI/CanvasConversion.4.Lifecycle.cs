using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class CanvasConversion
{
    /// <summary>Restore the panel into its original 2D home and destroy the host.</summary>
    internal static void Release(ConvertedPanel? panel)
    {
        if (panel == null)
            return;
        Active.Remove(panel);

        // User ruling 2026-08-02 round 2: undo the COMPLETE render hide FIRST. A window closed
        // while still behind the reveal gate (fast X, escape chord, scene teardown) would otherwise
        // be restored into 2D with the GAME's own nested canvases still disabled by us — an
        // invisible window the game never re-enables. The restore touches exactly what the hide
        // disabled, so on an already-revealed panel this is a no-op.
        SetPanelRenderVisible(panel, visible: true);

        if (panel.HostCanvas != null)
            UguiPokeSurfaces.Unregister(panel.HostCanvas); // drops nested registrations too

        DestroyHostDepthMask(panel); // frees the mask MESH asset (the GO cascades with HostGo below)

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
                // Same reason as in Release: never leave a surviving game-owned nested canvas
                // disabled by our reveal hide (IsAlive can be false for reasons other than the
                // whole subtree being gone).
                SetPanelRenderVisible(panel, visible: true);
                if (panel.HostCanvas != null)
                    UguiPokeSurfaces.Unregister(panel.HostCanvas);
                DestroyHostDepthMask(panel); // mesh asset — never leaked on a scene unload either
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

            // Item 3a + user ruling 2026-08-02 ("das Fenster soll direkt an der richtigen Stelle
            // erscheinen"): reveal the render-hidden modal host only at its FINAL pose and
            // scale. The old gate waited for the treatment window (mod layer + background) and
            // — one-shot menus only — the single content fit; every OTHER floated window then
            // revealed at its full pre-fit rect and was visibly re-fitted/re-centered ~0.25 to
            // 0.85 s later (ModBuild 15 hardware log: MODAL REVEAL consistently before 'Host
            // rect fit 1920x1080 → …' — the reported "larger and lower, then snaps"). The
            // settle criteria, the deadline and the reveal log live in TickRevealGate below.
            // The treatments for THIS frame already ran above (canvas still disabled →
            // harmless), and LateTick re-treats once more (canvas now enabled) before the frame
            // renders — so the first visible frame is fully treated: zero flicker, unchanged.
            if (panel.RevealPending && panel.HostCanvas != null)
                TickRevealGate(panel);

            // FLICKER FIX (modal hosts only): the 30-frame adoption sweep re-asserts
            // overrideSorting=false, but a WORLD-space modal that shares its canvas order
            // with a nested canvas the game flips to overrideSorting=true even briefly
            // renders that subtree at the nested order → it swaps in/out of the host's
            // dominant order between sweeps = flicker. Re-assert every frame for the
            // (few) modal hosts: cheap (a handful of adopted entries), change-gated writes.
            if (panel.Diagnostic)
                ReassertAdoptedSorting(panel);

            TickFit(panel); // test #14 item 1: content fit + growth re-fit (throttled)

            // Per-host depth compose (part 5): keep the color-invisible depth stamp matching
            // this host's visible content so converted panels occlude EACH OTHER per pixel —
            // runs after the fit so a just-resized host stamps its settled content, and every
            // frame (not throttled) because a stale stamp during the initiative reorder slide
            // would punch visible holes into a menu behind the moving portraits.
            TickHostDepthMask(panel);

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
    /// User ruling 2026-08-02: the reveal gate for a render-hidden floated modal host. The window
    /// becomes visible ONLY once it stands at its final pose and scale, so the player never sees
    /// the pre-fit / pre-scale intermediate state snap into place. Settle criteria (all three):
    ///
    ///  1. TREATMENT — the historic <see cref="ConvertedPanel.RevealNotBefore"/> window passed
    ///     (mod-layer move + background hide applied over several frames; unchanged).
    ///  2. FIT — the FIRST content fit committed (<see cref="ConvertedPanel.FitMeasuredOnce"/>),
    ///     or the host has no fit at all (<c>fitContent:false</c>, e.g. the Options family).
    ///     Pre-reveal the fit runs the settled synchronous-layout path
    ///     (<see cref="SettlePreRevealFirstFit"/> / <see cref="SettleOneShotFit"/>), so "fit
    ///     committed" means the layout reflow is flushed and the rect is final.
    ///  3. POSE — the host's world position, rotation, lossy scale AND host-rect size held still
    ///     for <see cref="RevealStableFrames"/> consecutive checks. WHY value-based stillness:
    ///     the pose finalizers span modules and frames (fit commit → ModalFallback 5b re-derives
    ///     the board scale → GrabbableModal.Tick writes it to the host transform NEXT tick);
    ///     watching the values catches every such hand-off without cross-module coupling.
    ///
    /// BOUNDED: at <see cref="ConvertedPanel.RevealDeadline"/> the host is revealed regardless
    /// (Warn names what was still pending) — a window must never stay invisible. The final pose
    /// itself is untouched: spawn clamps, chain-pose rule 1/2 and the fits run exactly as before;
    /// only the visible intermediate state is gone. One Info line per reveal reports the settle
    /// duration and what the gate waited for last, so the next hardware log proves the fix.
    /// </summary>
    private static void TickRevealGate(ConvertedPanel panel)
    {
        float now = Time.unscaledTime;

        // User ruling 2026-08-02 round 2: re-apply the COMPLETE hide before evaluating the gate.
        // ModalFallback.Tick runs EARLIER in this same Update than CanvasConversion.Tick, and it is
        // where the grab bar (GrabbableModal.Build) and the mod X (ModalCloseButton.Attach) are
        // built — so on the convert frame those children exist by the time we get here and would
        // otherwise draw at the pre-fit pose. The pass is idempotent: anything already disabled is
        // skipped, so a steady pending panel costs one component walk and zero writes.
        SetPanelRenderVisible(panel, visible: false);

        // Pose-stability tracking (criterion 3): any real change resets the stillness counter.
        Transform t = panel.HostGo.transform;
        Vector3 pos = t.position;
        Quaternion rot = t.rotation;
        Vector3 scl = t.lossyScale;
        Vector2 rect = panel.HostRect != null ? panel.HostRect.rect.size : Vector2.zero;
        bool held = panel.RevealHasSnapshot
                    && (pos - panel.RevealLastPos).sqrMagnitude <= RevealPosEpsilon * RevealPosEpsilon
                    && Quaternion.Angle(rot, panel.RevealLastRot) <= RevealRotEpsilonDeg
                    && (scl - panel.RevealLastScale).magnitude
                       <= panel.RevealLastScale.magnitude * RevealScaleEpsilonRel + 1e-6f
                    && Mathf.Abs(rect.x - panel.RevealLastRectSize.x) <= RevealRectEpsilonPx
                    && Mathf.Abs(rect.y - panel.RevealLastRectSize.y) <= RevealRectEpsilonPx;
        panel.RevealPoseStableFrames = held ? panel.RevealPoseStableFrames + 1 : 0;
        panel.RevealHasSnapshot = true;
        panel.RevealLastPos = pos;
        panel.RevealLastRot = rot;
        panel.RevealLastScale = scl;
        panel.RevealLastRectSize = rect;

        bool treated = now >= panel.RevealNotBefore;
        bool fitDone = !panel.FitEnabled || panel.FitMeasuredOnce;
        bool poseStable = panel.RevealPoseStableFrames >= RevealStableFrames;
        bool settled = treated && fitDone && poseStable;
        bool deadline = now >= panel.RevealDeadline;
        if (!settled && !deadline)
        {
            // Remember the FIRST unmet criterion in gate order — when the gate opens next
            // frame(s), this is "what it waited for last" in the reveal log.
            panel.RevealLastBlocker = !treated ? "treatment (mod layer/backing)"
                : !fitDone ? "first content fit (layout settle)"
                : "host pose/scale stillness";
            return;
        }

        // ATOMIC REVEAL (user ruling 2026-08-02 round 2): host canvas, every nested canvas and
        // every mod-drawn renderer — content, grab bar, X + its depth stamp, depth masks, MR
        // backing plate — become visible in ONE pass, in THIS frame, at the final pose. Nothing
        // of the window was drawable anywhere before this line.
        SetPanelRenderVisible(panel, visible: true, out int shownCanvases, out int shownRenderers);
        panel.RevealPending = false;
        float waitedMs = (now - panel.RevealRequestedAt) * 1000f;
        string fitState = panel.FitMeasuredOnce ? "applied" : panel.FitEnabled ? "pending" : "n/a";
        // First-open pose fix (2026-08-02): state WHERE the revealed pose came from. The placement
        // runs at convert time, i.e. from the PRE-fit rect/scale, so ModalFallback replays it once
        // against the final geometry while the panel is still hidden — this line is the proof that
        // it happened (or the reason it deliberately did not) for the next hardware log.
        string poseState = panel.PoseRePlaced
            ? $"pose RE-PLACED after the fit: " +
              $"({panel.PoseRePlacedFrom.x:F2},{panel.PoseRePlacedFrom.y:F2},{panel.PoseRePlacedFrom.z:F2})" +
              $" → ({panel.PoseRePlacedTo.x:F2},{panel.PoseRePlacedTo.y:F2},{panel.PoseRePlacedTo.z:F2})"
            : $"pose from spawn ({panel.PoseRePlaceReason})";
        // `t` is this host's transform, read at the top of the gate — the same value the pose
        // stability check tracked, i.e. the scale the first visible frame renders at.
        string finalScale = t.lossyScale.x.ToString("F3");
        if (settled)
        {
            string lastWait = panel.RevealLastBlocker.Length > 0
                ? panel.RevealLastBlocker
                : "nothing (settled immediately)";
            VRLog.Info("WorldUI", $"MODAL REVEAL: '{panel.HostGo.name}' shown after settle " +
                                  $"({waitedMs:F0} ms; last waited on {lastWait}; fit={fitState}, " +
                                  $"pose still for {panel.RevealPoseStableFrames} frame(s)) — revealed at " +
                                  "its FINAL pose/scale (mod layer + background hidden) — zero-flicker " +
                                  $"pop-in; {poseState}, final scale {finalScale}; unhid {shownCanvases} " +
                                  $"canvas(es) + {shownRenderers} renderer(s) (grab bar, X, depth masks, " +
                                  "MR plate) in this ONE frame.");
        }
        else
        {
            // Deadline reveal: visibility beats perfection — the window may show one visible
            // correction, but it can never stay an invisible blocker.
            VRLog.Warn("WorldUI", $"MODAL REVEAL: '{panel.HostGo.name}' FORCED after {waitedMs:F0} ms " +
                                  $"(deadline {RevealMaxWaitSeconds * 1000f:F0} ms; still waiting on " +
                                  $"{(!treated ? "treatment" : !fitDone ? "first content fit" : "pose stillness")}; " +
                                  $"fit={fitState}) — revealing anyway, a window must never stay invisible; " +
                                  $"{poseState}, final scale {finalScale}; unhid {shownCanvases} canvas(es) " +
                                  $"+ {shownRenderers} renderer(s). NOTE: any pose re-place still pending is " +
                                  "now permanently SKIPPED — moving a visible window is the jump this gate " +
                                  "exists to prevent.");
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

            // User ruling 2026-08-02 round 2 (THE hard guarantee): re-apply the complete render
            // hide in LateUpdate — after EVERY Update ran and immediately before the frame renders.
            // Update-time passes cannot cover children built by a tick step that runs AFTER
            // CanvasConversion.Tick (MrBacking creates its opaque per-host backing plate there), nor
            // anything a game script instantiates in its own Update. Whatever appeared this frame is
            // switched off before it is ever drawn. Idempotent + change-gated: a steady pending
            // panel costs one component walk and no writes, and only for the ≤0.6 s gate window.
            if (panel.RevealPending)
                SetPanelRenderVisible(panel, visible: false);

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
