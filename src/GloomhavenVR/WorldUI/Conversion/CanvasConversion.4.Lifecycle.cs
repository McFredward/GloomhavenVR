using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        // ModBuild 199: the per-frame maintenance enrolment ends with the conversion. Cleared here
        // (and nowhere else — nothing may THROTTLE it, only end it) so a panel that is restored to
        // 2D and re-converted later re-enrols through ConvertedPanel.Diagnostic's latch.
        panel.PerFrameGuards = false;
        panel.GuardHostMoving = false;
        panel.GuardHostHeld = false;

        // ROUND 10 (supersample): stand the panel's capture path down FIRST, while its transforms
        // still carry the capture layer — this method's own layer restore (below) then hands them to
        // the game's layer. Both restores are guarded on the layer they expect to find, so the order
        // is not load-bearing; running ours first simply means no transform is ever written twice.
        PanelSupersample.NoticeRelease(panel);

        // User ruling 2026-08-02 round 2: undo the COMPLETE render hide FIRST. A window closed
        // while still behind the reveal gate (fast X, escape chord, scene teardown) would otherwise
        // be restored into 2D with the GAME's own nested canvases still disabled by us — an
        // invisible window the game never re-enables. The restore touches exactly what the hide
        // disabled, so on an already-revealed panel this is a no-op.
        SetPanelRenderVisible(panel, visible: true);

        if (panel.HostCanvas != null)
            UguiPokeSurfaces.Unregister(panel.HostCanvas); // drops nested registrations too

        // Drop out of the far-to-near draw ladder (CanvasConversion.8.Order.cs). The order pass
        // prunes on this flag rather than searching Active, so it must be cleared here and in the
        // dead-panel prune below — the two places a panel ever leaves Active.
        panel.OrderListed = false;
        panel.OrderSwapPeer = null;
        panel.OrderSwapStreak = 0;
        panel.OrderFollowers.Clear();

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
                // A conceded canvas is the one whose sortingOrder we took over — hand that back
                // too, or the game's 2D home would keep a draw order borrowed from a world-space
                // host that no longer exists.
                if (record.ConcededOverrideSorting)
                    record.Canvas.sortingOrder = record.OriginalSortingOrder;
            }
            if (record.AddedRaycaster != null)
                Object.Destroy(record.AddedRaycaster);
        }
        panel.AdoptedCanvases.Clear();
        // ModBuild 203: the sibling-rebase census describes a set that no longer exists. Zero it and
        // arm the rebuild, so a panel object that is ever re-converted cannot report — or write —
        // offsets derived from a previous life.
        panel.AdoptedOrderRebaseDirty = true;
        panel.RebaseEligible = panel.RebaseDistinctOriginals = panel.RebaseLifted = 0;
        panel.RebaseClamped = panel.AdoptedOverrideAtAdoption = panel.AdoptedOverrideNow = 0;
        panel.RebaseMinOriginal = int.MaxValue;
        panel.RebaseMaxOriginal = int.MinValue;

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

            // ==============================================================================
            // SetParent RETURNS void AND UNITY IS ALLOWED TO REFUSE IT. If the current parent
            // is mid-activation Unity logs
            //   "Cannot set the parent of the GameObject 'X' while activating or deactivating
            //    the parent GameObject 'Y'."
            // and DOES NOTHING. Release is reachable from exactly such a stack: the materialise
            // carrier's OnDisable (WindowMaterialiseRunner.cs:590 / Visibility.cs:548) finishes
            // the vanish and invokes this as its completion callback, which is what happened on
            // 2026-09-03 (.planning/debug/second_logs/Player.log:10601). The unconditional
            // detach three lines up was therefore unconditional in CODE and not in EFFECT, and
            // the Destroy(HostGo) at the end of this method took the game's persistent
            // confirmation box with it. VERIFY the move; never trust it.
            // ==============================================================================
            Transform? hostTransform = panel.HostGo != null ? panel.HostGo.transform : null;
            bool detached = hostTransform == null || !target.IsChildOf(hostTransform);
            if (!detached)
            {
                target.SetParent(null, worldPositionStays: false); // second try: bare scene root
                detached = !target.IsChildOf(hostTransform!);
                restoreParent = detached ? null : restoreParent;
                // HW-VERIFY: this names the BLOCKER of a refused detach. If it appears, the
                // window below was one Destroy away from being deleted out of the game.
                VRLog.Alert("WorldUI", "RELEASE DETACH REFUSED: Unity would not reparent the game "
                    + $"window '{target.name}' out of the float host "
                    + $"'{(panel.HostGo != null ? panel.HostGo.name : "<gone>")}' — the host is "
                    + "mid-activation, which means this release is running inside a SetActive/"
                    + "destroy callback (the materialise carrier's OnDisable is the known route). "
                    + $"The retry to the bare scene root {(detached ? "SUCCEEDED" : "ALSO FAILED")}. "
                    + "The host will NOT be destroyed while it still holds this window — the "
                    + "destroy is deferred to a normal frame instead. This exact refusal deleted "
                    + "the game's persistent 'Confirmation Box_MainMenu_pc' on 2026-09-03 and left "
                    + "the player unable to leave a scenario.");
            }

            // SCENE MEMBERSHIP goes home too. A reparent to the bare scene root (OriginalParent
            // died with its own scene) would otherwise leave a PERSISTENT game object sitting in
            // the active scene, where the NEXT scene load deletes it — the same defect one load
            // later. See CanvasConversion.KeepHostInTargetScene for the full reasoning.
            if (detached && restoreParent == null)
                RestoreTargetScene(panel, target);

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

        DestroyHostSafely(panel, "release");

        if (Active.Count == 0)
            RestoreCameraMask();
    }

    /// <summary>Restore every conversion (module shutdown / VR off).</summary>
    internal static void ReleaseAll()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
            Release(Active[i]);
        SoftLocks.Clear();
        // The draw ladder is a STATIC list (CanvasConversion.8.Order.cs); Release only clears each
        // panel's OrderListed flag, and the pruning pass that acts on it does not run again after a
        // shutdown. Drop the whole sequence here so a VR-off / hot reload leaves no references to
        // dead panels behind.
        OrderedPanels.Clear();
        RestoreCameraMask();
        // ROUND 10: the supersample path's own teardown — camera hooks removed, every borrowed
        // culling mask handed back, every render target released. Release() above already stood each
        // individual panel down; this drops the process-wide state it installed.
        PanelSupersample.Shutdown();
    }

    // =====================================================================================
    // A HOST MAY NEVER BE DESTROYED WHILE IT STILL HOLDS GAME CONTENT (2026-09-03).
    //
    // Object.Destroy CASCADES into every child. The float host is ours; what is parked under
    // it usually is not — it is a live game UIWindow that only rents the host for the length
    // of a float. Release detaches the target first, but Unity is allowed to REFUSE that
    // detach (see the block in Release), and the prune path never detached at all. Either way
    // the Destroy below used to delete a game object the game will never rebuild:
    //   * ModBuild 199-era: UIScenarioEscMenu — the pause menu could not be reopened at all.
    //   * 2026-09-03: 'Confirmation Box_MainMenu_pc' under DontDestroyOnLoad — every later
    //     confirmation threw and the player could not leave the scenario.
    //
    // So the destroy is now CONDITIONAL on the host being empty of game content, and when it
    // is not, the host is parked and retried from the per-frame service, where Unity is not
    // in the middle of an activation and the detach succeeds. A host that can never be
    // emptied is LEAKED on purpose: one orphaned empty GameObject costs nothing, a deleted
    // game window costs the player his session.
    // =====================================================================================

    /// <summary>Hosts whose destroy is waiting for their game content to come loose.</summary>
    private static readonly List<GameObject> DeferredHosts = new(2);

    /// <summary>How many service passes a deferred host is retried before it is abandoned alive.</summary>
    private const int DeferredHostMaxTries = 120;
    private static readonly List<int> DeferredHostTries = new(2);

    // ==========================================================================================
    // THE PARKED HOST'S SCENE MEMBERSHIP, CARRIED ALONGSIDE IT (ModBuild 373).
    //
    // Release restores scene membership when it detaches a target to the bare scene root
    // (RestoreTargetScene, called from Release). ServiceDeferredHosts frees a target the SAME WAY
    // — `stuck.SetParent(null, …)` — and did NOT, so a DontDestroyOnLoad window that came loose on
    // the deferred path was left sitting in the ACTIVE scene and died at the next scene load.
    // That is the ModBuild 361 / 'Confirmation Box_MainMenu_pc' failure exactly, reached through
    // the other door: the guard that was added to STOP that cascade could itself re-create it.
    //
    // The panel object is not carried past DestroyHostSafely, so the two facts RestoreTargetScene
    // needs — the home scene and whether it was persistent — are carried in parallel lists keyed
    // by index, the same way the retry counter already is. Applying the TARGET's home scene to
    // whatever came loose is right by construction: the only thing a float host holds that is not
    // mod-owned is that target (or content parked under a mod-owned intermediate, which belongs to
    // the same window).
    // ==========================================================================================
    private static readonly List<Scene> DeferredHostScenes = new(2);
    private static readonly List<bool> DeferredHostPersistent = new(2);

    /// <summary>Everything the mod parks under a float host carries this prefix — the materialise
    /// carrier and its debris (<c>GloomhavenVR.WindowMaterialiseDebris*</c>), the close-X plate
    /// (<c>GloomhavenVR.ModalCloseX</c>), the supersample display (<c>GloomhavenVR.PanelSS_*</c>),
    /// the transient-dismiss catcher and the story dock (<c>GloomhavenVR.StoryDock</c>). A game
    /// object never carries it, so the prefix IS the ownership test.</summary>
    private const string ModOwnedPrefix = "GloomhavenVR.";

    /// <summary>
    /// The first game-owned transform anywhere under <paramref name="node"/>, or null.
    ///
    /// <para>The walk descends ONLY through mod-owned nodes, and that is the point: the game's own
    /// content hangs below mod-owned intermediates in at least one shipped case — StoryComposite
    /// parks a <c>GloomhavenVR.StoryDock</c> under the host and then moves the GAME's story-window
    /// children into it — so a direct-children-only test would have called that host empty and
    /// destroyed the story window with it. Stopping at the first non-mod node keeps the walk
    /// bounded by the mod's own (tiny) subtrees; it never enters game hierarchy.</para>
    /// </summary>
    private static Transform? FindGameContent(Transform node, int depth)
    {
        if (depth <= 0)
            return null;
        for (int i = 0; i < node.childCount; i++)
        {
            Transform child = node.GetChild(i);
            if (!child.name.StartsWith(ModOwnedPrefix, System.StringComparison.Ordinal))
                return child;
            Transform? deeper = FindGameContent(child, depth - 1);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    /// <summary>True when something under <paramref name="host"/> belongs to the game.</summary>
    private static bool HostHoldsGameContent(GameObject host, out string first)
    {
        Transform? found = FindGameContent(host.transform, depth: 4);
        first = found != null ? found.name : string.Empty;
        return found != null;
    }

    /// <summary>
    /// Destroy a float host, but ONLY once it holds nothing of the game's. Otherwise park it
    /// for <see cref="ServiceDeferredHosts"/>.
    /// </summary>
    private static void DestroyHostSafely(ConvertedPanel panel, string why)
    {
        GameObject? host = panel.HostGo;
        if (host == null)
            return;
        if (!HostHoldsGameContent(host, out string blocker))
        {
            Object.Destroy(host);
            return;
        }
        if (DeferredHosts.Contains(host))
            return;
        DeferredHosts.Add(host);
        DeferredHostTries.Add(0);
        DeferredHostScenes.Add(panel.TargetHomeScene);
        DeferredHostPersistent.Add(panel.TargetWasPersistent);
        // HW-VERIFY: the guard that stops a mod host from deleting a game window. Every
        // appearance is a cascade that WOULD have happened before ModBuild 361.
        VRLog.Alert("WorldUI", $"HOST DESTROY DEFERRED ({why}): the float host '{host.name}' still "
            + $"holds the GAME object '{blocker}', so destroying it now would DELETE THAT OBJECT "
            + "with it. Unity refuses a reparent while the parent is mid-activation, which is how "
            + "a release running inside an OnDisable leaves the window attached. The host is kept "
            + "alive and retried once per frame from CanvasConversion.Tick instead; if it can "
            + "never be emptied it is leaked on purpose. THIS IS THE 2026-09-03 DEADLOCK GUARD: "
            + "the same cascade deleted 'Confirmation Box_MainMenu_pc' and left the player unable "
            + "to leave a scenario.");
    }

    /// <summary>Retry the parked detaches from a normal frame, then destroy what came loose.</summary>
    private static void ServiceDeferredHosts()
    {
        for (int i = DeferredHosts.Count - 1; i >= 0; i--)
        {
            GameObject host = DeferredHosts[i];
            if (host == null)
            {
                DropDeferredHost(i);
                continue;
            }
            // Retry the detach — this frame is a normal Update, not a Unity activation callback,
            // so SetParent is allowed now. Bounded: at most one game node comes loose per pass and
            // the loop below re-tests, so a pathological tree costs one frame per node.
            int freed = 0;
            for (int guard = 0; guard < 8; guard++)
            {
                Transform? stuck = FindGameContent(host.transform, depth: 4);
                if (stuck == null)
                    break;
                stuck.SetParent(null, worldPositionStays: false);
                if (stuck.IsChildOf(host.transform))
                    break; // still refused — try again next frame rather than spinning
                // SCENE MEMBERSHIP GOES HOME HERE TOO. `SetParent(null)` leaves the freed object in
                // whatever scene it currently belongs to — which, for anything that was parented
                // under a host, is the host's. Release already fixes that for its own detach
                // (RestoreTargetScene); this path did not, so a DontDestroyOnLoad window freed on
                // the deferred path lost its persistence and died at the next scene load. Same
                // defect as ModBuild 361, different door.
                RestoreFreedContentScene(stuck, DeferredHostScenes[i], DeferredHostPersistent[i]);
                freed++;
            }
            if (!HostHoldsGameContent(host, out string blocker))
            {
                VRLog.Note("WorldUI", $"HOST DESTROY DEFERRED — RESOLVED: '{host.name}' came loose "
                    + "on a normal frame and is destroyed now. The game object it was holding is "
                    + $"alive and back at the scene root ({freed} object(s) freed, scene membership "
                    + $"restored, persistent={DeferredHostPersistent[i]}).");
                Object.Destroy(host);
                DropDeferredHost(i);
                continue;
            }
            DeferredHostTries[i]++;
            if (DeferredHostTries[i] < DeferredHostMaxTries)
                continue;
            VRLog.Alert("WorldUI", $"HOST DESTROY ABANDONED: '{host.name}' still holds the game "
                + $"object '{blocker}' after {DeferredHostMaxTries} frame(s) of retries. The host "
                + "is LEAKED ALIVE rather than destroyed — an orphan empty canvas costs a few "
                + "bytes, deleting a game window costs the session.");
            DropDeferredHost(i);
        }
    }

    /// <summary>Drop one parked host and everything carried alongside it. One method so the four
    /// parallel lists can never fall out of step — a mismatch would apply one window's scene to
    /// another window's content, which is the exact class of bug this file keeps paying for.</summary>
    private static void DropDeferredHost(int i)
    {
        DeferredHosts.RemoveAt(i);
        DeferredHostTries.RemoveAt(i);
        DeferredHostScenes.RemoveAt(i);
        DeferredHostPersistent.RemoveAt(i);
    }

    /// <summary>
    /// Put game content freed from a PARKED host back in the scene it came from. Same contract as
    /// <see cref="RestoreTargetScene"/>, but keyed on the two facts carried alongside the host
    /// rather than on a <see cref="ConvertedPanel"/> that is long out of scope by then. Failure is
    /// never fatal — the worst case is the pre-fix behaviour — and it is logged.
    /// </summary>
    private static void RestoreFreedContentScene(Transform freed, Scene home, bool wasPersistent)
    {
        try
        {
            if (wasPersistent)
            {
                Object.DontDestroyOnLoad(freed.gameObject);
                // HW-VERIFY: the ModBuild 361 guard closing its OTHER door. A persistent game
                // window that came loose on the deferred path used to be left in the active scene,
                // where the next load deletes it while the game's Singleton keeps pointing at it.
                VRLog.Note("WorldUI", $"DEFERRED DETACH SCENE RESTORE: '{freed.name}' is a "
                    + "PERSISTENT game object that came loose from a parked float host, so it was "
                    + "made DontDestroyOnLoad again instead of being left in the active scene, "
                    + "where the next scene load would have deleted it — the ModBuild 361 defect "
                    + "reached through ServiceDeferredHosts instead of through Release.");
                return;
            }
            if (home.IsValid() && home.isLoaded && home != freed.gameObject.scene)
                SceneManager.MoveGameObjectToScene(freed.gameObject, home);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("WorldUI", $"DEFERRED DETACH SCENE RESTORE FAILED for '{freed.name}': "
                + $"{e.GetType().Name}: {e.Message}.");
        }
    }

    /// <summary>
    /// Put a released target back in the SCENE it came from when its original parent no longer
    /// exists. Without this a persistent (DontDestroyOnLoad) window detached to the bare scene
    /// root joins the active scene and dies on the next load — the same defect, one load later.
    /// </summary>
    private static void RestoreTargetScene(ConvertedPanel panel, RectTransform target)
    {
        try
        {
            if (panel.TargetWasPersistent)
            {
                Object.DontDestroyOnLoad(target.gameObject);
                VRLog.Note("WorldUI", $"RELEASE SCENE RESTORE: '{target.name}' is a PERSISTENT game "
                    + "window whose original parent is gone, so it was made DontDestroyOnLoad again "
                    + "instead of being left in the active scene, where the next scene load would "
                    + "have deleted it.");
                return;
            }
            Scene home = panel.TargetHomeScene;
            if (home.IsValid() && home.isLoaded && home != target.gameObject.scene)
                SceneManager.MoveGameObjectToScene(target.gameObject, home);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("WorldUI", $"RELEASE SCENE RESTORE FAILED for '{target.name}': "
                + $"{e.GetType().Name}: {e.Message}.");
        }
    }

    // ---- per-frame service (called by the WorldUI driver) --------------------------------

    /// <summary>
    /// Cheap housekeeping: prune panels whose target died with a scene unload,
    /// keep worldCamera bound to the live head camera, apply pending lock state.
    /// </summary>
    internal static void Tick()
    {
        // Parked hosts first: a host that is only waiting for Unity to allow the detach must be
        // retried on a frame that is NOT inside a SetActive callback, and this is that frame.
        ServiceDeferredHosts();

        // Part 9d, the LIFT half of the pre-Start flash veil, and it belongs at the TOP of the
        // Update phase for a reason the part's header states in full: Unity has already run
        // Start() for everything activated last frame by the time any Update runs, so this is the
        // earliest moment a veil can be released — which is what keeps the veil costing exactly
        // the frame it prevented and no second frame. Costs one pass over a list that is empty on
        // essentially every frame; there is no window scan and no component walk on this path.
        TickFlashVeilLift();

        Camera? cam = WorldCamera;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            ConvertedPanel panel = Active[i];
            if (!panel.IsAlive)
            {
                // The game destroyed the UI (scene unload) — drop our host too.
                Active.RemoveAt(i);
                // ROUND 10: the second (and last) exit from Active must stand the supersample down
                // as well, or a dead panel's capture camera and render target would outlive it.
                PanelSupersample.NoticeRelease(panel);
                // Same reason as in Release: never leave a surviving game-owned nested canvas
                // disabled by our reveal hide (IsAlive can be false for reasons other than the
                // whole subtree being gone).
                SetPanelRenderVisible(panel, visible: true);
                if (panel.HostCanvas != null)
                    UguiPokeSurfaces.Unregister(panel.HostCanvas);
                // Second (and last) exit from Active — leave the draw ladder here too.
                panel.OrderListed = false;
                panel.OrderSwapPeer = null;
                panel.OrderSwapStreak = 0;
                panel.OrderFollowers.Clear();
                // The prune path never had the detach Release has, so anything else parked under
                // the host (StoryComposite's dock still holding the game's story children, a
                // second adopted widget) used to be destroyed with it. DestroyHostSafely refuses
                // to destroy a host that still holds game content and defers instead.
                DestroyHostSafely(panel, "prune (target already destroyed)");
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
            // Task #7: MODAL hosts (PerFrameGuards) sweep EVERY frame — a uGUI Dropdown
            // spawns its "Dropdown List"/"Blocker" canvases mid-life on a click, and on
            // the 30-frame schedule the open list stayed laser-unclickable (not yet
            // raycast-merged) for up to ~0.4 s. The scan is a cheap component walk of
            // the (few) floated modal subtrees; writes stay change-gated. The heavier
            // mod-layer re-sweep still runs only on schedule OR when this pass actually
            // adopted a NEW canvas (the fresh list/blocker must leave the game UI layer
            // before the game's mono UI Camera double-draws it).
            bool sweepDue = Time.frameCount >= panel.CanvasSweepNextFrame;
            long sweepTicksThisFrame = 0;
            if (earlySettle || sweepDue || panel.PerFrameGuards)
            {
                // ModBuild 199: this walk is the THIRD piece of work the log-verbosity flag used to
                // gate, and the only one whose cost is not trivially bounded (a component walk of the
                // whole modal subtree — 131 visible graphics on the party window). It is timed into
                // the same still/moving budget as the two guards below, so "what a drag now costs"
                // is one number and not an estimate.
                long ta = System.Diagnostics.Stopwatch.GetTimestamp();
                bool adoptedNew = AdoptNestedCanvases(panel);
                long tb = System.Diagnostics.Stopwatch.GetTimestamp();
                if (panel.PerFrameGuards)
                {
                    sweepTicksThisFrame = tb - ta;
                    if (panel.GuardHostMoving) panel.AdoptSweepTicksMoving += sweepTicksThisFrame;
                    else panel.AdoptSweepTicksStill += sweepTicksThisFrame;
                    panel.AdoptSweepRuns++;
                    if (adoptedNew) panel.AdoptSweepAdoptions++;
                }
                // User #8: a freshly adopted/pooled child spawns on the game's UI layer —
                // re-assert the mod-layer move so the UI Camera never picks it up.
                // ROUND 10: ...unless the supersample path currently OWNS this panel's layers. Two
                // writers moving the same transforms to two different layers is a write war, and the
                // recorded "original layer" of whichever wrote second would be the other's layer. The
                // supersample path runs the identical sweep, on the same cadence, with the same
                // foreign-Renderer skip rule, onto its own capture layer — which the game's UI
                // Camera cannot see either, so the guarantee this call protects still holds.
                // USER REPORT 2026-09-03 (b), contributor 2: a repopulating window creates its
                // new children on the GAME's UI layer, and until this sweep moves them the
                // game's UI Camera draws exactly those children and nothing else of the
                // window — a partial, differently-projected copy, for up to the 30 frames the
                // periodic cadence can take. The 385 log names the size of that hole for the
                // character screen: 1358 late transforms swept on the PERIODIC cadence, with
                // nothing keyed to the content change that produced them. A settle burst is
                // exactly the event 'this window just repopulated', so it sweeps too. The
                // single-writer rule below is UNCHANGED: a supersampled panel's layers still
                // belong to PanelSupersample alone, burst or no burst.
                if (panel.ModLayerEnabled
                    && (earlySettle || sweepDue || adoptedNew || SubViewBurstRunning(panel))
                    && !PanelSupersample.OwnsPanelLayers(panel))
                    ApplyModLayer(panel, initial: false);
            }

            // Task #4: pooled/late children can bring ScrollRects after Convert — re-sweep on
            // the periodic schedule so their viewports get a clipper too (change-gated inside).
            //
            // AND ON A SETTLE BURST, for the same reason the mod-layer sweep above does. A
            // repopulating sub-view brings its OWN ScrollRects, and until this runs their
            // viewports have no clipper at all — the content is drawn at full length instead of
            // clipping at the viewport, which on a world-space host has no screen edge to save it.
            // The ModBuild 385 log measures exactly that on the character screen the frame a
            // battle goal is picked: DRAWN CONTENT 1976x1453 px against a 1080 px frame, with
            // 'Information/Rewards' — a battle-goal slot part — named as the graphic reaching
            // 373 px outside it. A clipper that arrives up to 30 frames after the list it must
            // clip is a clipper that is absent for exactly the frames anybody looks at.
            // Change-gated inside (add-once), so a burst on a window whose viewports are already
            // clipped is a component walk and no writes.
            if (sweepDue || SubViewBurstRunning(panel))
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
            //
            // ModBuild 199: the gate is PerFrameGuards, NOT Diagnostic. Diagnostic is the LOG
            // verbosity flag, and GrabbableModal drops it to ~1 Hz while a window is held — which
            // silently ran this every-frame fix at 1 Hz for exactly the interval the user reports
            // the flicker under. See ConvertedPanel.Diagnostic for the full history. The budget the
            // split is judged against is measured here and printed by TickGuardBudget below.
            if (panel.PerFrameGuards)
            {
                bool moving = panel.GuardHostMoving;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                ReassertAdoptedSorting(panel, moving);
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                // ROUND 7 steady-state guard: the same reason the sorting is re-asserted every
                // frame for modal hosts — a game writer that re-drives the conversion frame AFTER
                // the reveal would otherwise leave the window rendering at a fraction of its size,
                // or off its own plane, with the fit already locked and nothing left to notice.
                // Height cap excluded here on purpose (see the parameter's doc): a rect the game
                // drives from a layout component must not be fought every frame.
                bool frameCorrected = ReassertConversionFrame(panel, out _, includeHeightCap: false);
                long t2 = System.Diagnostics.Stopwatch.GetTimestamp();

                if (moving)
                {
                    panel.SortGuardTicksMoving += t1 - t0;
                    panel.FrameGuardTicksMoving += t2 - t1;
                    panel.FrameGuardRunsMoving++;
                    if (frameCorrected) panel.FrameGuardWritesMoving++;
                }
                else
                {
                    panel.SortGuardTicksStill += t1 - t0;
                    panel.FrameGuardTicksStill += t2 - t1;
                    panel.FrameGuardRunsStill++;
                    if (frameCorrected) panel.FrameGuardWritesStill++;
                }
                // The worst-frame figure covers ALL THREE pieces of per-frame work the split put back
                // on every drag frame — the adoption sweep included, or the number would flatter the
                // change by leaving out its most expensive part.
                long frameTotal = sweepTicksThisFrame + (t2 - t0);
                if (frameTotal > panel.GuardWorstFrameTicks)
                    panel.GuardWorstFrameTicks = frameTotal;
            }

            TickGuardBudget(panel);

            TickFit(panel); // test #14 item 1: content fit + growth re-fit (throttled)

            // (The per-host depth-compose stamp that used to run here is gone. Converted panels
            // occlude each other by DRAW ORDER now — TickPanelOrder, run last in the WorldUI
            // LateUpdate chain, once the frame's pose writers have all finished. See
            // CanvasConversion.8.Order.cs for why a per-quad depth stamp could never deliver the
            // per-pixel transparency the user asked for.)

            if (panel.Diagnostic)
                DiagnoseModal(panel, force: false); // change-gated per-frame flicker snapshot
        }

        // ROUND 10 (supersample): decide which floated windows render through their own
        // supersampled render target this frame. Runs AFTER the loop above, so every panel's fit,
        // reveal and hide state is this frame's settled value. Fully self-guarded and a no-op
        // while [WorldUI] PanelSupersample is false.
        PanelSupersample.Tick();

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
                // ModBuild 291: a float the liveness rule has made DORMANT is render-hidden because
                // it draws nothing, and its raycaster was switched off with it. A lock EDGE must not
                // hand input back to an invisible window — it is the same reasoning as
                // ModalFallback.Tick's raycast step, at the other writer of this flag.
                if (ModalFallback.IsDormantPanel(Active[i]))
                    continue;
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

        // ROUND 7: maintain the conversion frame BEFORE anything measures this frame. Convert pins
        // the target's scale, rotation, depth, anchors and (for the menu family) its capped height;
        // the game re-drives them, and the ModBuild 23 log caught the target rendering at
        // localScale 0.14 with a 2040 px rect where 1080 was pinned. Everything the fit measures is
        // expressed in that frame, so it is restored while the window is still render-hidden — the
        // one window in which correcting it is guaranteed to be invisible. Six compares and no
        // writes on a healthy panel.
        ReassertConversionFrame(panel, out _);

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
        // Round 3 (first-open size bug): a COMMITTED one-shot fit is not automatically a TRUSTED
        // one. While its verify hold is running (an unproven first open of this window in the
        // session — see CanvasConversion.ArmOneShotVerify) the window stays render-hidden and the
        // fit keeps re-verifying, because on hardware a cold ESC-menu fit looked perfectly settled
        // at commit time and its content then jumped to a disjoint place. The hold is clamped
        // inside RevealDeadline, so the 0.6 s "never stay invisible" bound below is untouched.
        bool fitTrusted = panel.FitVerifyHoldRevealUntil <= 0f || now >= panel.FitVerifyHoldRevealUntil;
        bool fitDone = !panel.FitEnabled || (panel.FitMeasuredOnce && fitTrusted);
        // ModBuild 184: a host whose pose belongs to someone else's per-frame writer can never
        // satisfy a stillness test — see ConvertedPanel.PoseOwnedExternally for the measurement
        // that proved it (every hover card stayed canvas.enabled=False until the deadline).
        bool poseStable = panel.PoseOwnedExternally || panel.RevealPoseStableFrames >= RevealStableFrames;
        bool settled = treated && fitDone && poseStable;
        bool deadline = now >= panel.RevealDeadline;
        if (!settled && !deadline)
        {
            // Remember the FIRST unmet criterion in gate order — when the gate opens next
            // frame(s), this is "what it waited for last" in the reveal log.
            panel.RevealLastBlocker = !treated ? "treatment (mod layer/backing)"
                : !fitDone ? (panel.FitMeasuredOnce
                    ? "one-shot fit VERIFY (unproven first open — re-measuring while hidden)"
                    : "first content fit (layout settle)")
                : "host pose/scale stillness";
            return;
        }

        // ROUND 6 (LEFT-EYE FLICKER): the gate DECIDES here, in Update — the flip itself happens in
        // LateUpdate (CompleteReveal, called from LateTick). See ConvertedPanel.RevealArmed for why
        // a MultiPass rig makes the frame phase of a visibility write load-bearing.
        panel.RevealArmed = true;
        panel.RevealArmedSettled = settled;
    }

    /// <summary>
    /// ROUND 6 — THE ATOMIC REVEAL, IN THE ONE FRAME PHASE BOTH EYES SHARE.
    ///
    /// MultiPass renders the head camera ONCE PER EYE, and both passes run after every Update and
    /// every LateUpdate of the frame. Anything that changes a window's visibility, pose or size
    /// AFTER the gate opened but BEFORE the render loop therefore lands in the first visible frame
    /// unevenly, and anything that changes it DURING the render loop (a per-camera callback) lands
    /// in ONE EYE ONLY — the reported left-eye flicker at the side of the view.
    ///
    /// The gate used to flip visibility from Update (CanvasConversion.Tick), and several mod
    /// systems run after that in the same frame — MrBacking builds and re-fits the opaque per-host
    /// backing plate one step later, the game's own scripts update in their own Update, and the
    /// LateUpdate steps (flatten re-assert, tooltip/hex re-facing) run later still. The window was
    /// therefore drawable for a whole phase during which its own backing plate could still be
    /// created, moved or resized.
    ///
    /// Deferring the flip to LateUpdate — the last main-thread phase before rendering, identical
    /// for both eye passes — removes that window entirely: everything that writes this frame has
    /// written by then, and nothing runs between the flip and the two eye renders. The reveal stays
    /// atomic (one pass over the recorded set) and the timing is unchanged (same frame), so the
    /// 0.6 s bound and the no-jump guarantee are untouched.
    /// </summary>
    private static void CompleteReveal(ConvertedPanel panel)
    {
        if (panel.HostGo == null || panel.HostCanvas == null)
        {
            panel.RevealPending = false;
            panel.RevealArmed = false;
            return;
        }
        // ModBuild 226 — AN EMPTY WINDOW MUST NEVER STAND. User ruling, verbatim: "Als mein
        // Mitspieler gejoint ist, kam ein leeres Fenster auf - sowas soll per se niemals passieren."
        // (.planning/debug/leeres_fenster.jpg: six-plus grab bars in mid-air with no window on them.)
        //
        // THE REVEAL EDGE IS THE RIGHT PLACE AND THE ONLY CHEAP ONE. It runs ONCE per float, it is
        // the last moment before anything of this panel is drawn, and by here the content fit has
        // measured (the gate waits for it), so a window that is going to have content has it. A
        // per-frame check would cost a component walk on every panel forever and would fight the
        // ordinary case of a window whose content pops in late.
        //
        // THE REFUSAL IS A RELEASE, NOT A HIDE. Hiding the bar and leaving the panel alive would
        // leave an invisible thing holding a map-room window slot and a draw-order rung — a worse
        // bug than the visible one, and explicitly ruled out. ModalFallback.RefuseEmptyFloat drops
        // the whole float (grab holder destroyed, host released, 2D home restored) and enrols the
        // window in the retry set, so it is reconsidered only after it closes and re-opens.
        //
        // SCOPED TO MODAL FLOATS: RefuseEmptyFloat returns false for any panel ModalFallback does not
        // own, so the surfaces (actor bars, dialog, stat panels, tray docks) reveal exactly as they
        // always did. That matters — several of them are legitimately empty for stretches.
        if (ModalFallback.RefuseEmptyFloat(panel))
        {
            panel.RevealPending = false;
            panel.RevealArmed = false;
            return;
        }
        float now = Time.unscaledTime;
        bool settled = panel.RevealArmedSettled;
        bool treated = now >= panel.RevealNotBefore;
        bool fitDone = !panel.FitEnabled || panel.FitMeasuredOnce;
        Transform t = panel.HostGo.transform;

        // Decisive phase evidence for the next hardware run. Round 7: read from the mod's own frame
        // phase markers, NOT from Camera.current — Unity keeps that property pointing at the last
        // camera that rendered, which made the round-6 line cry wolf on hardware.
        string phase = CurrentFramePhase;

        SetPanelRenderVisible(panel, visible: true, out int shownCanvases, out int shownRenderers);
        // WINDOW MATERIALISE (user request 2026-08-25: "ich möchte nicht mehr, dass die Fenster
        // einfach aufploppen"). Here and not at float creation, because THIS is the frame the eye
        // first sees the panel — and this is a LateUpdate, so the alphas PlayIn writes land before
        // MultiPass renders either eye. Called AFTER the reveal on purpose: the panel is already
        // visible, already raycastable and already a laser target when this runs, so the animation
        // is decoration over a window that works rather than a gate in front of one. It cannot fail
        // in a way this caller has to handle — with the effect off, the shader unresolved, or
        // anything thrown, the panel simply stays as the line above left it: fully shown.
        //
        // SCOPED TO FLOATED WINDOWS, and that is the integrator's call rather than the author's.
        // CompleteReveal fires for EVERY ConvertedPanel, and most of them are not "Fenster" in the
        // sense of the request: the actor health bars, the initiative track, the element board, the
        // objectives strip and every hover tooltip are converted panels too, and they appear and
        // disappear constantly. Blowing a health bar in on a cloud of particles every time an enemy
        // is revealed is noise the user did not ask for, and it would put the effect's per-element
        // cost on the busiest surfaces in the scene. A window with a grab bar is what he means.
        //
        // AND THE ANNOUNCEMENT WAITS FOR THE WINDOW IT ANNOUNCES (user report 2026-09-03: "kam …
        // die Animation das ein neues Fenster spawnt aber das 'Fenster' ist sofort wieder
        // verschwunden"). PlayAppearOrDefer plays the appear immediately for a float that has
        // something drawable under it — which is every ordinary window — and OWES it, to be spent
        // on first paint, for one that has not. It is asked here rather than inside PlayIn because
        // the deferral is per-FLOAT state and ModalFallback owns the float; the whole argument,
        // including why refusing the float or holding the reveal would both have been worse, is on
        // WindowPanel.AppearOwed and on PlayAppearOrDefer itself. THE REVEAL ABOVE IS UNTOUCHED:
        // this line decides a decoration and nothing else.
        if (ModalFallback.IsFloated(panel))
            ModalFallback.PlayAppearOrDefer(panel);
        panel.RevealPending = false;
        panel.RevealArmed = false;
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
                                  $"canvas(es) + {shownRenderers} renderer(s) (grab bar, X, " +
                                  $"MR plate) in this ONE frame at frame phase {phase}, frame " +
                                  $"{Time.frameCount}.");
        }
        else
        {
            // Deadline reveal: visibility beats perfection — the window may show one visible
            // correction, but it can never stay an invisible blocker.
            // ModBuild 250 — PRINT THE BUDGET THIS PANEL ACTUALLY HAD, not the class constant. A
            // pose-critical shared window gets a ONE-SHOT extension (ArcSeats
            // .GrantPoseCriticalRevealGrace), and a line that keeps saying "deadline 600 ms" while
            // the window waited 1500 would read as a broken clock — [[a-default-value-names-an-
            // unbuilt-thing]] in miniature.
            float budgetMs = (panel.RevealDeadline - panel.RevealRequestedAt) * 1000f;
            string budget = panel.RevealPoseCriticalGraceGiven
                ? $"deadline {budgetMs:F0} ms, EXTENDED from {RevealMaxWaitSeconds * 1000f:F0} ms " +
                  "because this window's spawn pose came from the shared table anchor and depends " +
                  "on a rect it had not measured — grep SHARED WINDOW REVEAL GRACE"
                : $"deadline {budgetMs:F0} ms";
            VRLog.Warn("WorldUI", $"MODAL REVEAL: '{panel.HostGo.name}' FORCED after {waitedMs:F0} ms " +
                                  $"({budget}; still waiting on " +
                                  $"{(!treated ? "treatment" : !fitDone ? "first content fit" : "pose stillness")}; " +
                                  $"fit={fitState}) — revealing anyway, a window must never stay invisible; " +
                                  $"{poseState}, final scale {finalScale}; unhid {shownCanvases} canvas(es) " +
                                  $"+ {shownRenderers} renderer(s) at frame phase {phase}, frame " +
                                  $"{Time.frameCount}. NOTE: any pose re-place still pending is " +
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
            // ROUND 6: this runs BEFORE the reveal flip below, so the frame a window becomes
            // visible in is already treated — the ordering the old Update-phase flip could not give.
            bool earlySettle = panel.EarlySettleUntil > 0f && Time.unscaledTime < panel.EarlySettleUntil;
            if (earlySettle)
            {
                if (panel.ModLayerEnabled)
                {
                    AdoptNestedCanvases(panel);
                    // ROUND 10: same single-writer rule as the Update-phase call — see the comment
                    // there. A supersampled panel's layers belong to PanelSupersample alone.
                    if (!PanelSupersample.OwnsPanelLayers(panel))
                        ApplyModLayer(panel, initial: false);
                }
                if (panel.HideBackground)
                    HideFullScreenBackground(panel, initial: false);
            }

            if (!panel.RevealPending)
                continue;

            // ROUND 6 (left-eye flicker): the ONE place a panel's visibility is ever switched ON.
            // TickRevealGate (Update) only ARMS the reveal; the flip happens here, after every
            // Update-phase writer and immediately before the render loop — the last frame phase
            // that is identical for both MultiPass eye passes. See CompleteReveal.
            if (panel.RevealArmed)
            {
                CompleteReveal(panel);
                continue;
            }

            // User ruling 2026-08-02 round 2 (THE hard guarantee): re-apply the complete render
            // hide in LateUpdate — after EVERY Update ran and immediately before the frame renders.
            // Update-time passes cannot cover children built by a tick step that runs AFTER
            // CanvasConversion.Tick (MrBacking creates its opaque per-host backing plate there), nor
            // anything a game script instantiates in its own Update. Whatever appeared this frame is
            // switched off before it is ever drawn. Idempotent + change-gated: a steady pending
            // panel costs one component walk and no writes, and only for the ≤0.6 s gate window.
            SetPanelRenderVisible(panel, visible: false);
        }

        // Part 9d, the SCAN half of the pre-Start flash veil (user ruling 2026-09-03: "Ich will
        // das dieser Blitz erst gar nicht vorkommt"). It has to be in LateUpdate and it has to be
        // HERE: the EventSystem dispatches the Button.onClick that repopulates a pooled UIWindow
        // subtree from its own Update, Unity runs every Update before any LateUpdate, and every
        // LateUpdate before the render loop — so this is the last phase in which the frame that
        // would flash can still be withheld. Above PanelSupersample.LateTick on purpose, so a
        // veiled subtree is already culled when the capture sizes itself around the panel's
        // drawn content. Idle cost is a bounded loop of field reads over the game's own
        // enabled-window registry; see CanvasConversion.9d.FlashVeil.cs for the full argument.
        TickFlashVeilScan();

        // ROUND 10 (supersample): keep each capture frustum, display quad and allocation on its
        // panel's live geometry. The CAPTURE itself is not driven from here — the per-panel capture
        // camera is an ordinary enabled camera at depth -200, so Unity renders it in its own camera
        // loop after EVERY LateUpdate and before the head camera's two eye passes, which keeps the
        // "both eyes read one finished, identical RenderTexture" invariant CameraOrderProbe measured.
        // The display quad's final pose is copied in the capture path's own onPreCull, i.e. after
        // every remaining LateUpdate pose writer (grab, board docks, the order ladder) has run.
        PanelSupersample.LateTick();
    }

    // ---- floated-modal flicker instrumentation + per-frame sorting guard ------------------

    /// <summary>
    /// Modal-host flicker guard: re-assert <c>overrideSorting=false</c> (and the host's
    /// worldCamera) on every adopted nested canvas EVERY frame, so a game writer that
    /// flips overrideSorting on between the 30-frame adoption sweeps cannot pull a subtree
    /// out of the host's dominant order for up to half a second (visible as flicker).
    /// Change-gated writes; only the (few) adopted entries of modal hosts are touched.
    /// </summary>
    /// <summary>
    /// How many frames a game writer may win the <c>overrideSorting</c> flag before the adoption
    /// stops clearing it and takes the sortingOrder instead (see
    /// <see cref="NestedCanvasRecord.ConcededOverrideSorting"/>). Small: a genuine one-off write
    /// (a canvas re-enabled, a dropdown opened) is a single frame, while a per-frame writer is
    /// unmistakable by the third.
    /// </summary>
    internal const int ConcedeAfterReclears = 3;

    /// <summary>
    /// How far ABOVE its host a conceded canvas's <c>sortingOrder</c> is pinned. Until ModBuild 196
    /// this was 0 — pinned EQUAL — and that was the bug behind "die Itemkarten-Overlays im
    /// Ausrüstungsmenu sind nur extrem transparent zu sehen": the card was not faint, it was BEHIND
    /// the window's own rows, and the photograph's ~0.27x attenuation is what an opaque row bleeding
    /// a little light looks like, not what an alpha of 0.27 looks like.
    /// <para>WHY EQUAL LOSES. A canvas with <c>overrideSorting</c> is sorted as its OWN entry —
    /// sortingLayer, then sortingOrder, then distance — and never by hierarchy. So the mod's
    /// <c>RaiseToWindowTop</c>, which writes hierarchy order, is INERT on exactly these canvases.
    /// At an equal order the tie falls through to distance, and <c>Flatten</c> makes an adopted box
    /// exactly coplanar with its host, which exhausts that tiebreaker too. Equal is not a tie here,
    /// it is a loss. The shop's item overlay renders correctly only because its canvas never had the
    /// flag forced back on, so it never entered this branch and hierarchy order still decided it.</para>
    /// <para>WHY +1 AND NOT MORE. It must clear the window's own content and nothing else:
    /// <see cref="ModalCloseButton.XOrderOffset"/> is +2, so the mod's close X still draws above the
    /// overlay, and <c>CanvasConversion.8.Order.PanelOrderStep</c> is 16, so a nearer window's whole
    /// band still wins. This stays a single writer of a single number — the ModBuild 190 ruling
    /// (concede the flag, own the number) is untouched; only the number is corrected.</para>
    /// </summary>
    internal const int ConcededOrderLift = 1;

    // ---- ModBuild 203: the CANVAS-vs-SIBLING half of "equal is a loss" ----------------------
    //
    // USER REPORT (2026-08-22, the permanent character window in the 3D map room, verbatim in
    // substance): inside "New Party display" exactly two of six sub-views come up broken and
    // flicker — the CHARACTER SHEET ('Campaign Adventure Party Assembly Variant') and the PERKS
    // view ('New UIPerksWindow Variant'). Glyphs drop out of strings that are otherwise correct;
    // perks can come up as a near-empty dark plate. It flickers while the window is dragged,
    // freezes broken on release, and is broken on open with no drag at all.
    //
    // THE HALF-LESSON THIS FILE ALREADY CARRIED. ConcededOrderLift above fixed the canvas-vs-HOST
    // relation: a conceded canvas pinned EQUAL to its host loses, because an overriding canvas is
    // sorted as its own entry (sortingLayer → sortingOrder → distance) and Flatten makes it exactly
    // coplanar with the host, so both tiebreakers are exhausted. NetProtocol.cs:1324 records it as
    // "EQUAL IS NOT A TIE, IT IS A LOSS". What it did NOT fix is the canvas-vs-SIBLING relation:
    // every conceded canvas of one panel was written the SAME number, `host + 1`. Two overriding
    // canvases at one sortingOrder do not consult hierarchy either — that is the entire meaning of
    // overrideSorting — so they are a tie, and Unity breaks such a tie by canvas registration
    // order, which is undefined and is re-rolled whenever a canvas is enabled, disabled, or has its
    // sortingOrder rewritten. Whatever layering the GAME expressed by authoring those canvases with
    // DIFFERENT orders was thrown away and replaced with an unstable coin flip. The host's order is
    // re-sorted by eye distance every frame (part 8), so every re-sort re-flips the coin — that is
    // the flicker, and the freeze-on-release is the coin landing.
    //
    // THE FIX. Rebase the game's own spread onto the host's live order instead of collapsing it:
    // each eligible canvas keeps its RANK among its siblings and the mod still owns the absolute
    // number. See RebuildConcededOrderOffsets for the derivation.
    //
    // REJECTED ALTERNATIVES.
    //   * "Write the raw difference (original − min) as the offset, as first specified." The real
    //     authored orders measured on this window are {1, 100, 1000} (ModBuild 202 hardware log,
    //     the adoption lines). A raw difference would put a canvas 999 orders above its host —
    //     sixty-two PanelOrderStep bands up, i.e. straight through every other window in the room.
    //     Only the ORDER of the authored values is information; their SPACING is not. Dense ranks
    //     preserve the former and discard the latter, which is exactly the requirement.
    //   * "Give up overrideSorting and re-clear the flag so hierarchy decides." That is the
    //     ModBuild 179 write war: the game re-asserts the flag every frame, the two sides alternate,
    //     and in MultiPass the eyes land on different sides of it ("flackert stark", 18,994 of one
    //     session's 20,173 lines). Concede the flag, own the number. Untouched here.
    //   * "Raise the whole nested band far above the host so nothing can be painted over." It would
    //     climb into the NEXT panel's slot (PanelOrderStep is 16) and a farther window's innards
    //     would paint over a nearer window. Bounded band, clamped, warned about once.
    //   * "Sort the eligible set by hierarchy only and ignore the authored orders." That discards
    //     real information: a canvas the game deliberately authored ABOVE a later sibling would be
    //     demoted. Hierarchy is the FALLBACK, used only where the authored orders tie — which is
    //     what uGUI itself would have done for a canvas that is not its own sorting root.
    //
    // WHAT THE EVIDENCE ACTUALLY SAYS, AND WHERE IT STOPS. Read the census line
    // (LogAdoptedOrderCensus) before believing any of the above on this window. In the ModBuild 202
    // log the party window carries ~21 adopted canvases but only ONE of them is CONCEDED ('UI Party
    // Inventory Item Tooltip', order host+1, overrideSorting TRUE); the other twenty read
    // `override=False` in every one of the 1,040 MODAL DIAG snapshots, and a canvas without
    // overrideSorting does not sort as its own entry at all — its sortingOrder is inert and it draws
    // inside its host's batch by hierarchy. One canvas cannot tie with itself. So on the evidence in
    // hand this rebase is a NO-OP on the reported window, and the census line exists to say that out
    // loud in one line on the next capture instead of leaving it to be re-derived from 5 MB of log.

    /// <summary>
    /// How wide the conceded-canvas band may be, in offsets above <see cref="ConcededOrderLift"/>.
    ///
    /// <para>THE BOUND, DERIVED. <c>CanvasConversion.8.Order.PanelOrderStep</c> (16) is the order gap
    /// between two adjacent panels on the distance ladder, and it is the MINIMUM gap: the ladder
    /// assigns <c>PanelOrderBase + rank * PanelOrderStep</c>, so two adjacent windows are exactly 16
    /// apart and never less. The whole of one window — host at +0, its conceded canvases, its
    /// followers — must therefore stay inside <c>[host, host+15]</c>, or a FARTHER window's innards
    /// climb into a NEARER window's slot, which is the defect part 8 exists to remove. With the lift
    /// at +1 that left offsets 0..14, i.e. fifteen distinct ranks — and since
    /// <see cref="RaisedOverlayOrderOffset"/> reserved the TOP of the slot for a raised hover overlay
    /// it leaves offsets 0..13, fourteen ranks, band <c>host+1 … host+14</c>. That reservation is
    /// what makes "raised to the window's top" mean something against a canvas that does not consult
    /// hierarchy; the rank it costs comes out of a set whose largest measured size is 1.</para>
    ///
    /// <para>CAN N EXCEED IT? YES, IN PRINCIPLE. The party window has ~21 adopted canvases, so if the
    /// game ever conceded all of them the eligible set would be 21 &gt; 14 and the top eight would have
    /// to share a rank. Measured, the eligible set on that window is 1, and the largest anywhere in
    /// the ModBuild 202 log is 1 (three concessions in the whole session, on three different
    /// windows). The clamp is therefore dead code in the shipped scene and is written to STAY dead
    /// code: it clamps to the top of the band and prints ONE line naming the window, so the next
    /// capture names the case instead of silently mis-drawing it.</para>
    ///
    /// <para>THE FOLLOWERS SHARE THIS BAND, AND THAT IS DELIBERATE.
    /// <see cref="ModalCloseButton.XOrderOffset"/> is +2, the grab bar is +4 and the game's hover
    /// tooltip rides at <c>WorldTooltips.MenuPanelSortingLift</c> (+10). A conceded canvas at offset
    /// ≥1 therefore TIES the close X, and at ≥3 outranks it. That is only acceptable because the
    /// measured eligible set is 1 — offset 0 for the single member, i.e. bit-identical to ModBuild
    /// 202's <c>host + 1</c>. The census line reports the lifted count so the first window that ever
    /// produces a non-zero offset is visible in the log the same session, and the follower offsets
    /// can be re-cut then, in the file that owns them, against a real case instead of a hypothetical
    /// one.</para>
    /// </summary>
    private const int ConcededOrderMaxOffset = PanelOrderStep - ConcededOrderLift - 2;

    /// <summary>
    /// THE ONE ORDER IN A WINDOW'S SLOT THAT NOTHING INSIDE THAT WINDOW MAY TAKE — reserved for a
    /// hover overlay this mod has RAISED to the window's content root
    /// (<c>TooltipOnWindow.RaiseToWindowTop</c>): the ability-card full preview and the local-tooltip
    /// families.
    ///
    /// <para>WHY A RESERVED NUMBER AND NOT A HIERARCHY WRITE. The raise makes the box the LAST CHILD
    /// of the content root, which decides the question for every canvas that draws inside the host's
    /// own batch — and for no other. A canvas with <c>overrideSorting</c> is sorted as its OWN entry
    /// (sortingLayer → sortingOrder → distance) and NEVER by hierarchy, so a conceded sibling pinned
    /// anywhere in <c>host+<see cref="ConcededOrderLift"/> … host+{ConcededOrderLift +
    /// ConcededOrderMaxOffset}</c> paints straight over a box drawing at the host's own order, and the
    /// raise is inert against it. The same applies with the flag left set: the game gives its preview
    /// canvas the ABSOLUTE order 10 <c>(AbilityCardUI.cs:1049-1061)</c>, which is below
    /// <c>PanelOrderBase</c> (100) and therefore below every converted window in the room — correct on
    /// the flat game's order-0 canvas, a guaranteed loss here. Both states have the same photograph.
    /// Owning the NUMBER answers both.</para>
    ///
    /// <para>WHY <c>PanelOrderStep - 1</c> AND WHAT NOW SITS BETWEEN IT AND THE NEXT WINDOW. It is the
    /// TOP of this window's own slot: <c>CanvasConversion.8.Order.PanelOrderStep</c> is 16, so the
    /// next panel on the ladder starts at <c>host+16</c> and NOTHING at all sits between the two. No
    /// order was stolen from another window. Inside this window it is strictly above the whole
    /// conceded band, which is why <see cref="ConcededOrderMaxOffset"/> above was narrowed by one
    /// (the band is now host+1..host+14, fifteen ranks down to fourteen) — the clamp it feeds has
    /// never been reached on hardware, the largest conceded set ever measured being 1, so the cost of
    /// the reservation is a rank in a set that has never had two members.</para>
    ///
    /// <para>WHAT IT OUTRANKS, STATED RATHER THAN DISCOVERED. Every follower of this window:
    /// <see cref="ModalCloseButton.XOrderOffset"/> at +2, the grab bar at +4, and the game's hover
    /// tooltip laid on a menu plane at <c>WorldTooltips.MenuPanelSortingLift</c> (+10). A raised hover
    /// overlay draws over its window's close X and grab bar while it is up. That is the intended
    /// trade — the overlay is transient, it exists for as long as a pointer rests on a row, and the
    /// alternative is the reported defect — but it IS a change, so it is written here rather than
    /// left to be found in a screenshot.</para>
    /// </summary>
    internal const int RaisedOverlayOrderOffset = PanelOrderStep - 1;

    /// <summary>
    /// KEEP ONE RAISED OVERLAY'S OWN CANVAS SORTING AS ITS OWN ENTRY, at a number this mod owns —
    /// the FLAG half of the reservation above. Idempotent, change-gated, and deliberately NOT a
    /// second writer of the sortingOrder: that number is written by
    /// <c>ApplyPanelOrder</c> through <c>RegisterOrderFollower</c>, which runs last in the frame.
    ///
    /// <para>NO WRITE WAR, BY CONSTRUCTION AND NOT BY HOPE. The game sets
    /// <c>overrideSorting = true</c> exactly ONCE per hover, at <c>AddComponent</c>
    /// (AbilityCardUI.cs:1049-1061); the writer that keeps clearing it is this mod's own
    /// <c>AdoptCanvas</c>. Marking the adoption record <see cref="NestedCanvasRecord.KeepOverrideSorting"/>
    /// makes that sweep RE-ASSERT the flag instead of clearing it, and re-assert
    /// <see cref="NestedCanvasRecord.OverlaySortingOrder"/> — which is kept equal to the follower's
    /// number here, so the two agree and neither writes. The flag is therefore flipped at most once
    /// per hover, on the frame after the adoption first sees the canvas, and never again.</para>
    ///
    /// <para>NOTHING LEAKS WHEN THE PREVIEW DIES. The game destroys this Canvas on the hide edge
    /// (and <c>TooltipOnWindow.CompleteCanvasTeardown</c> finishes the job when Unity refuses), which
    /// drops the adoption record on the next prune and the order follower inside
    /// <c>ApplyPanelOrder</c>'s own null sweep. There is no state on the panel that outlives the
    /// canvas.</para>
    ///
    /// <para>Returns TRUE when the canvas is a known adopted record of this panel, i.e. when the
    /// mark could be placed — FALSE on the first frame of a hover, before the adoption sweep has
    /// seen the freshly added Canvas. The caller writes the flag either way; the mark only decides
    /// whether the adoption will argue about it afterwards.</para>
    /// </summary>
    internal static bool KeepRaisedOverlayOnTop(ConvertedPanel panel, Canvas canvas, int wantOrder,
        out bool flagWritten)
    {
        flagWritten = false;
        if (panel == null || canvas == null)
            return false;

        // The FLAG. One write per hover: the adoption cleared it when it first adopted the canvas.
        if (!canvas.overrideSorting)
        {
            canvas.overrideSorting = true;
            flagWritten = true;
        }

        bool marked = false;
        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            NestedCanvasRecord rec = panel.AdoptedCanvases[i];
            if (!ReferenceEquals(rec.Canvas, canvas))
                continue;
            marked = true;
            bool dirty = false;
            if (!rec.KeepOverrideSorting)
            {
                rec.KeepOverrideSorting = true;
                dirty = true;
            }
            if (rec.OverlaySortingOrder != wantOrder)
            {
                rec.OverlaySortingOrder = wantOrder;
                dirty = true;
            }
            // A canvas the mod pins on top must never also be ranked inside the conceded band; the
            // rebase excludes KeepOverrideSorting-without-concession by design, and the reset here
            // keeps the cached offsets dense if this record had ever been ranked.
            if (rec.RebaseOffset != 0)
            {
                rec.RebaseOffset = 0;
                dirty = true;
                panel.AdoptedOrderRebaseDirty = true;
            }
            if (dirty)
                panel.AdoptedCanvases[i] = rec;
            break;
        }
        return marked;
    }

    /// <summary>
    /// THE TERM THE OLD DRAW-ORDER VERDICT COULD NOT SEE. Every canvas of <paramref name="panel"/>
    /// that is sorted as its OWN entry right now — conceded to a game writer, or kept on top by the
    /// adoption — with its live <c>sortingOrder</c>, EXCLUDING <paramref name="except"/>.
    ///
    /// <para>WHY IT HAD TO BE ADDED. <c>TooltipOnWindow.CountLaterPainters</c> is a pure hierarchy
    /// walk, so a box raised to last sibling scores 0 by construction, and
    /// <c>TooltipOnWindow.SortingVerdict</c> only ever compared the box's OWN nearest canvas against
    /// the host. Neither can see a DIFFERENT overriding canvas in the same window, which is exactly
    /// what outranks a raised box: hierarchy is not consulted for such a canvas at all. In the
    /// photographed state the evidence line therefore read "0 later painters, 0 clippers, SORTING
    /// VERDICT: OK" while the card was painted out — eight more clean measurements would have agreed
    /// with a broken build.</para>
    ///
    /// <para>Cost: one pass over a list the panel already holds — measured at 1 conceded entry and
    /// ~21 adopted entries on the party window — with no Unity call beyond the null test and the
    /// order read. No allocation: the names are appended into the caller's builder.</para>
    /// </summary>
    /// <param name="highest">Highest live sortingOrder among the reported canvases, or
    /// <see cref="int.MinValue"/> when there are none.</param>
    /// <returns>How many overriding canvases were found.</returns>
    internal static int DescribeOverridingCanvases(ConvertedPanel panel, Canvas? except,
        System.Text.StringBuilder into, int maxNamed, out int highest)
    {
        highest = int.MinValue;
        int found = 0;
        if (panel == null)
            return 0;
        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            NestedCanvasRecord rec = panel.AdoptedCanvases[i];
            Canvas c = rec.Canvas;
            if (c == null || ReferenceEquals(c, except) || !c.overrideSorting)
                continue;
            found++;
            if (c.sortingOrder > highest)
                highest = c.sortingOrder;
            if (found > maxNamed)
                continue;
            into.Append(found > 1 ? ", " : string.Empty)
                .Append('\'').Append(c.name).Append("' at ").Append(c.sortingOrder)
                .Append(rec.ConcededOverrideSorting ? " CONCEDED" : " kept");
        }
        if (found > maxNamed)
            into.Append(", +").Append(found - maxNamed).Append(" more");
        return found;
    }

    /// <summary>
    /// WHO OWNS THIS CANVAS'S SORTING, in one clause, read off the adoption record rather than
    /// inferred. The three answers are genuinely different states and the remedy for each is
    /// different, so the verdict line must not blur them.
    /// </summary>
    internal static string AdoptionStateOf(ConvertedPanel panel, Canvas? canvas)
    {
        if (panel == null || canvas == null)
            return "no canvas";
        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            NestedCanvasRecord rec = panel.AdoptedCanvases[i];
            if (!ReferenceEquals(rec.Canvas, canvas))
                continue;
            if (rec.ConcededOverrideSorting)
                return "adopted, CONCEDED to a game writer — the mod owns the number, the game owns the flag";
            if (rec.KeepOverrideSorting)
                return $"adopted, KEPT ON TOP by the mod — the mod owns both the flag and the number, "
                       + $"and the adoption re-asserts {rec.OverlaySortingOrder}";
            return "adopted, flag CLEARED by the adoption — this canvas's sortingOrder is INERT and "
                   + "hierarchy order decides it";
        }
        return "not adopted yet — the adoption sweep has not seen this canvas; it still carries "
               + "whatever the game gave it";
    }

    /// <summary>
    /// Re-derive the cached <see cref="NestedCanvasRecord.RebaseOffset"/> of every rebase-eligible
    /// record on <paramref name="panel"/>, and refresh the census fields the report line prints.
    ///
    /// <para>ELIGIBLE = CONCEDED, AND NOTHING ELSE. Only a canvas with <c>overrideSorting</c> TRUE is
    /// sorted as its own entry, and the conceded ones are exactly the canvases that have the flag AND
    /// whose number the mod owns. A canvas the adoption cleared the flag on draws inside its host's
    /// batch by hierarchy and its <c>sortingOrder</c> is inert — writing it would be theatre. Task #7
    /// dropdown overlays (<see cref="NestedCanvasRecord.KeepOverrideSorting"/> without concession) are
    /// excluded on purpose: their whole contract is to sit ABOVE the entire window at the absolute
    /// orders 4000/3999, and folding them into a host-relative band would re-create the vanishing
    /// dropdown of task #7. They keep their existing top-order behaviour untouched.</para>
    ///
    /// <para>THE RANK. Eligible records are ordered by (authored <c>OriginalSortingOrder</c>, then
    /// <see cref="NestedCanvasRecord.DfsIndex"/>, then instance id) and given DENSE offsets 0..K−1.
    /// The first key preserves what the game expressed; the second is the hierarchy fallback for a
    /// genuine authored tie — what uGUI itself would have done — computed once at adoption, never
    /// per frame; the third only guarantees the comparator is a strict TOTAL order, so
    /// "count of records that rank strictly before me" really is a dense permutation and two
    /// canvases can never be handed the same offset.</para>
    ///
    /// <para>COST. O(K²) with no allocation, where K is the eligible count — 1 in every window
    /// measured. It runs only when <see cref="ConvertedPanel.AdoptedOrderRebaseDirty"/> is set (a
    /// canvas adopted, pruned or conceded), never on a steady frame.</para>
    /// </summary>
    private static void RebuildConcededOrderOffsets(ConvertedPanel panel)
    {
        panel.AdoptedOrderRebaseDirty = false;
        int eligible = 0, lifted = 0, clamped = 0, distinct = 0;
        int min = int.MaxValue, max = int.MinValue;
        int overrideAtAdoption = 0;

        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            NestedCanvasRecord rec = panel.AdoptedCanvases[i];
            if (rec.Canvas == null)
                continue;
            if (rec.OriginalOverrideSorting)
                overrideAtAdoption++;
            if (!rec.ConcededOverrideSorting)
            {
                // Not eligible: keep the record's offset at a defined value so a canvas that is
                // conceded LATER cannot inherit a stale rank from a set it was never ranked in.
                if (rec.RebaseOffset != 0)
                {
                    rec.RebaseOffset = 0;
                    panel.AdoptedCanvases[i] = rec;
                }
                continue;
            }

            eligible++;
            if (rec.OriginalSortingOrder < min) min = rec.OriginalSortingOrder;
            if (rec.OriginalSortingOrder > max) max = rec.OriginalSortingOrder;

            // Dense rank = how many eligible siblings sort strictly before me. The comparator is a
            // strict total order, so the ranks are a permutation of 0..K-1 with no collisions.
            int rank = 0;
            bool firstWithThisOrder = true;
            for (int j = 0; j < panel.AdoptedCanvases.Count; j++)
            {
                if (j == i)
                    continue;
                NestedCanvasRecord other = panel.AdoptedCanvases[j];
                if (other.Canvas == null || !other.ConcededOverrideSorting)
                    continue;
                if (other.OriginalSortingOrder != rec.OriginalSortingOrder)
                {
                    if (other.OriginalSortingOrder < rec.OriginalSortingOrder)
                        rank++;
                    continue;
                }
                // Same authored order → the DISTINCT count must not double-count it, and the
                // hierarchy fallback decides the rank.
                if (j < i)
                    firstWithThisOrder = false;
                if (other.DfsIndex != rec.DfsIndex)
                {
                    if (other.DfsIndex < rec.DfsIndex)
                        rank++;
                    continue;
                }
                if (other.Canvas.GetInstanceID() < rec.Canvas.GetInstanceID())
                    rank++;
            }
            if (firstWithThisOrder)
                distinct++;

            int offset = rank;
            if (offset > ConcededOrderMaxOffset)
            {
                offset = ConcededOrderMaxOffset;
                clamped++;
            }
            if (offset != 0)
                lifted++;
            if (rec.RebaseOffset != offset)
            {
                rec.RebaseOffset = offset;
                panel.AdoptedCanvases[i] = rec;
            }
        }

        panel.RebaseEligible = eligible;
        panel.RebaseDistinctOriginals = distinct;
        panel.RebaseMinOriginal = min;
        panel.RebaseMaxOriginal = max;
        panel.RebaseLifted = lifted;
        panel.RebaseClamped = clamped;
        panel.AdoptedOverrideAtAdoption = overrideAtAdoption;

        if (clamped > 0 && !panel.RebaseClampLogged)
        {
            panel.RebaseClampLogged = true;
            VRLog.Info("WorldUI",
                $"ADOPTED ORDER BAND OVERFLOW '{panel.HostGo.name}': {eligible} conceded nested "
                + $"canvas(es) need {eligible} distinct order(s) above the host, but the band is only "
                + $"{ConcededOrderMaxOffset + 1} wide (CanvasConversion.8.Order.PanelOrderStep is "
                + $"{PanelOrderStep}, the lift is {ConcededOrderLift} and host+"
                + $"{RaisedOverlayOrderOffset} is reserved for a raised hover overlay, so orders "
                + $"host+{ConcededOrderLift}..host+{ConcededOrderLift + ConcededOrderMaxOffset} are "
                + $"all this window may use before it reaches that reservation and then the NEXT "
                + $"window's slot at host+{PanelOrderStep}). {clamped} canvas(es) were CLAMPED to the top of the band and "
                + "therefore still tie with each other. This is the one case the sibling rebase "
                + "cannot fully express; it has never been reached on hardware (the largest conceded "
                + "set ever measured is 1). Printed ONCE per window.");
        }
    }

    /// <summary>
    /// Re-assert the adoption's sorting contract on every nested canvas this host adopted.
    ///
    /// <para>ModBuild 199 INSTRUMENT: <paramref name="hostMoving"/> splits the run and correction
    /// counters into a STILL and a MOVING population, because the whole question of the round is
    /// whether running this at ~1 Hz instead of every frame WHILE A WINDOW IS HELD could produce a
    /// visible defect. It cannot if it never corrects anything: the counters below are what settle
    /// that, and <see cref="TickGuardBudget"/> prints them with the walk's comparison count on the
    /// same line. Counting is three int increments on a path that already walks the list.</para>
    /// </summary>
    private static void ReassertAdoptedSorting(ConvertedPanel panel, bool hostMoving)
    {
        int wrote = 0;
        panel.SortGuardLastCanvases = panel.AdoptedCanvases.Count;
        if (hostMoving) panel.SortGuardRunsMoving++;
        else panel.SortGuardRunsStill++;

        // ModBuild 203: re-derive the cached sibling offsets ONLY when the eligible set changed.
        // A steady frame does no work here at all, and the number written for a given canvas is
        // identical on every run — which is the property the whole concession depends on.
        if (panel.AdoptedOrderRebaseDirty)
            RebuildConcededOrderOffsets(panel);

        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            NestedCanvasRecord rec = panel.AdoptedCanvases[i];
            Canvas nested = rec.Canvas;
            if (nested == null)
                continue;

            // CONCEDED: the game owns the flag, we own the number. sortingOrder follows the HOST's
            // live order (CanvasConversion.8.Order re-sorts it per frame by eye distance), so the
            // subtree keeps drawing with its window while the game's writer is left alone.
            if (rec.ConcededOverrideSorting)
            {
                // ModBuild 203: + the canvas's own SIBLING RANK. Until 202 every conceded canvas of
                // one panel got this identical number, which is a tie between overriding canvases and
                // therefore an undefined, re-rollable draw order (see the block comment above
                // ConcededOrderMaxOffset). RebaseOffset is cached, dense and clamped into the band.
                int wantOrder = panel.HostCanvas != null
                    ? panel.HostCanvas.sortingOrder + ConcededOrderLift + rec.RebaseOffset
                    : nested.sortingOrder;
                if (panel.HostCanvas != null && nested.sortingOrder != wantOrder)
                {
                    nested.sortingOrder = wantOrder;
                    panel.SortGuardOrderWrites++;
                    wrote++;
                }
                if (panel.HostCanvas != null && nested.worldCamera != panel.HostCanvas.worldCamera)
                {
                    nested.worldCamera = panel.HostCanvas.worldCamera;
                    panel.SortGuardCameraWrites++;
                    wrote++;
                }
                continue;
            }

            // Task #7: dropdown overlays (list/blocker) KEEP overrideSorting by design —
            // AdoptCanvas re-asserts their top order; clearing it here would re-create
            // the vanishing-dropdown bug this guard must not fight.
            if (rec.KeepOverrideSorting)
                continue;

            if (nested.overrideSorting)
            {
                if (++rec.ReclearCount >= ConcedeAfterReclears)
                {
                    // STOP FIGHTING. Neither side wins a per-frame write war; the canvas's sorting
                    // state just alternates, and in MultiPass the two eye passes can land on
                    // different sides of it. Take the order instead and leave the flag.
                    rec.ConcededOverrideSorting = true;
                    rec.KeepOverrideSorting = true;
                    panel.AdoptedCanvases[i] = rec;
                    // ModBuild 203: the eligible set just GREW, so every cached sibling offset on
                    // this panel (including this record's own, which is still 0 from adoption) has
                    // to be re-derived before the number is written. Rebuild first, then re-read the
                    // record — the rebuild writes back into the list.
                    panel.AdoptedOrderRebaseDirty = true;
                    RebuildConcededOrderOffsets(panel);
                    rec = panel.AdoptedCanvases[i];
                    if (panel.HostCanvas != null)
                    {
                        nested.sortingOrder =
                            panel.HostCanvas.sortingOrder + ConcededOrderLift + rec.RebaseOffset;
                        panel.SortGuardOrderWrites++;
                        wrote++;
                    }
                    VRLog.Info("WorldUI", $"MODAL SORTING CONCEDED: adopted canvas '{nested.name}' in " +
                                          $"'{panel.HostGo.name}' had overrideSorting flipped back ON by a " +
                                          $"game writer {rec.ReclearCount} frames running. The mod now owns " +
                                          "its sortingOrder (which follows the host's live draw order) and " +
                                          "leaves the FLAG to the game. A per-frame write war has no winner " +
                                          "— the canvas re-sorts every frame and the two MultiPass eyes can " +
                                          "disagree, which is what 'flackert stark' looks like. This line is " +
                                          "printed ONCE per canvas.");
                    continue;
                }
                nested.overrideSorting = false;
                panel.SortGuardFlagWrites++;
                wrote++;
                panel.AdoptedCanvases[i] = rec;
                VRLog.Debug("WorldUI", $"MODAL DIAG: adopted canvas '{nested.name}' in " +
                                       $"'{panel.HostGo.name}' had overrideSorting flipped back ON by the game — " +
                                       $"re-cleared ({rec.ReclearCount}/{ConcedeAfterReclears} before conceding).");
            }
            else if (rec.ReclearCount != 0)
            {
                rec.ReclearCount = 0;   // it settled — a one-off write, not a writer
                panel.AdoptedCanvases[i] = rec;
            }

            if (panel.HostCanvas != null && nested.worldCamera != panel.HostCanvas.worldCamera)
            {
                nested.worldCamera = panel.HostCanvas.worldCamera;
                panel.SortGuardCameraWrites++;
                wrote++;
            }
        }

        if (wrote > 0)
        {
            if (hostMoving) panel.SortGuardWritesMoving++;
            else panel.SortGuardWritesStill++;
        }
    }

    /// <summary>How long a guard-budget report window is (unscaled seconds).</summary>
    private const float GuardBudgetWindowSeconds = 10f;

    /// <summary>
    /// The per-frame budget one 90 Hz frame allows, in microseconds — the threshold every guard
    /// cost below is printed against, on the same line as the value and the comparison count.
    /// </summary>
    private const double GuardFrameBudgetUs = 1000000.0 / 90.0;

    /// <summary>
    /// ModBuild 199 INSTRUMENT — "what does the per-frame modal maintenance actually correct, and
    /// what does it cost?", answered separately for a STILL and a MOVING window.
    ///
    /// <para>WHY IT EXISTS. ModBuild 198 named <c>ReassertAdoptedSorting</c> as the next suspect for
    /// "das Flackern beim Greifen und Verschieben": its own comment promises an every-frame
    /// re-assert, and the log throttle dropped it to ~1 Hz for exactly the interval the user
    /// complains about. That is a hypothesis about a fix that is not running — and it is only worth
    /// anything if the fix, when it DOES run, finds something to fix. This line counts the
    /// corrections. A window whose CORRECTED count stays 0 across a whole session of drags proves
    /// the throttle was a red herring on that window, no matter how bad the coupling reads.</para>
    ///
    /// <para>The line carries the walk's comparison count (adopted canvases), the corrections split
    /// by kind, the still/moving split with its per-second rate, and the cost as a mean AND a worst
    /// single frame against the 90 Hz frame budget — value, count and threshold on one line, so it
    /// can be read without the source. It is NOT gated on <see cref="ConvertedPanel.Diagnostic"/>:
    /// the remedy it measures must never be gated behind the diagnostic shipped to test it.</para>
    /// </summary>
    private static void TickGuardBudget(ConvertedPanel panel)
    {
        if (!panel.PerFrameGuards)
            return;
        float now = Time.unscaledTime;
        if (panel.GuardReportNextAt <= 0f)
        {
            panel.GuardReportNextAt = now + GuardBudgetWindowSeconds;
            panel.GuardReportSince = now;
            return;
        }
        if (now < panel.GuardReportNextAt)
            return;

        float span = Mathf.Max(now - panel.GuardReportSince, 0.001f);
        int runsStill = panel.SortGuardRunsStill, runsMoving = panel.SortGuardRunsMoving;
        int hitsStill = panel.SortGuardWritesStill, hitsMoving = panel.SortGuardWritesMoving;
        int frameRunsStill = panel.FrameGuardRunsStill, frameRunsMoving = panel.FrameGuardRunsMoving;
        int frameHitsStill = panel.FrameGuardWritesStill, frameHitsMoving = panel.FrameGuardWritesMoving;
        int totalRuns = runsStill + runsMoving;
        int totalHits = hitsStill + hitsMoving;

        panel.GuardReportNextAt = now + GuardBudgetWindowSeconds;
        panel.GuardReportSince = now;
        panel.SortGuardRunsStill = panel.SortGuardRunsMoving = 0;
        panel.SortGuardWritesStill = panel.SortGuardWritesMoving = 0;
        panel.FrameGuardRunsStill = panel.FrameGuardRunsMoving = 0;
        panel.FrameGuardWritesStill = panel.FrameGuardWritesMoving = 0;
        int flagWrites = panel.SortGuardFlagWrites, orderWrites = panel.SortGuardOrderWrites;
        int camWrites = panel.SortGuardCameraWrites;
        panel.SortGuardFlagWrites = panel.SortGuardOrderWrites = panel.SortGuardCameraWrites = 0;
        long sortTicks = panel.SortGuardTicksStill + panel.SortGuardTicksMoving;
        long frameTicks = panel.FrameGuardTicksStill + panel.FrameGuardTicksMoving;
        long sweepTicks = panel.AdoptSweepTicksStill + panel.AdoptSweepTicksMoving;
        long movingTicks = panel.SortGuardTicksMoving + panel.FrameGuardTicksMoving
                           + panel.AdoptSweepTicksMoving;
        long worstTicks = panel.GuardWorstFrameTicks;
        int sweepRuns = panel.AdoptSweepRuns, sweepAdoptions = panel.AdoptSweepAdoptions;
        panel.SortGuardTicksStill = panel.SortGuardTicksMoving = 0;
        panel.FrameGuardTicksStill = panel.FrameGuardTicksMoving = 0;
        panel.AdoptSweepTicksStill = panel.AdoptSweepTicksMoving = 0;
        panel.AdoptSweepRuns = panel.AdoptSweepAdoptions = 0;
        panel.GuardWorstFrameTicks = 0;
        int gapSamples = panel.PoseGapSamples, gapOver = panel.PoseGapOverOnePx;
        float gapWorst = panel.PoseGapWorstPx;
        double gapMean = gapSamples > 0 ? panel.PoseGapSumPx / gapSamples : 0.0;
        panel.PoseGapSamples = panel.PoseGapOverOnePx = 0;
        panel.PoseGapWorstPx = 0f;
        panel.PoseGapSumPx = 0.0;

        // A window nobody opened, moved or looked at this period says nothing worth a line.
        if (totalRuns == 0)
            return;

        double usPerTick = 1000000.0 / System.Diagnostics.Stopwatch.Frequency;
        double sortMeanUs = totalRuns > 0 ? sortTicks * usPerTick / totalRuns : 0.0;
        int frameRunsTotal = frameRunsStill + frameRunsMoving;
        double frameMeanUs = frameRunsTotal > 0 ? frameTicks * usPerTick / frameRunsTotal : 0.0;
        double sweepMeanUs = sweepRuns > 0 ? sweepTicks * usPerTick / sweepRuns : 0.0;
        double worstUs = worstTicks * usPerTick;
        double movingMeanUs = runsMoving > 0 ? movingTicks * usPerTick / runsMoving : 0.0;
        float stillSeconds = totalRuns > 0 ? span * runsStill / totalRuns : 0f;
        float movingSeconds = totalRuns > 0 ? span * runsMoving / totalRuns : 0f;
        double stillRate = stillSeconds > 0.01f ? hitsStill / (double)stillSeconds : 0.0;
        double movingRate = movingSeconds > 0.01f ? hitsMoving / (double)movingSeconds : 0.0;

        VRLog.Info("WorldUI",
            $"MODAL GUARD BUDGET '{panel.HostGo.name}' (this {span:F1} s window): the every-frame "
            + $"adopted-sorting re-assert RAN {totalRuns} time(s) over {panel.SortGuardLastCanvases} "
            + $"adopted canvas(es) each and CORRECTED something on {totalHits} of them "
            + $"({flagWrites} overrideSorting re-clear(s), {orderWrites} conceded sortingOrder "
            + $"write(s), {camWrites} worldCamera re-bind(s)) — STILL {hitsStill} correction(s) in "
            + $"{runsStill} run(s) over ~{stillSeconds:F1} s = {stillRate:F2}/s, MOVING {hitsMoving} "
            + $"correction(s) in {runsMoving} run(s) over ~{movingSeconds:F1} s = {movingRate:F2}/s "
            + $"(held by a hand on the last sample: {panel.GuardHostHeld}). The conversion-frame "
            + $"guard RAN {frameRunsTotal} time(s) and corrected {frameHitsStill + frameHitsMoving} "
            + $"({frameHitsMoving} of them while moving). The nested-canvas adoption sweep RAN "
            + $"{sweepRuns} time(s) and adopted {sweepAdoptions} new canvas(es). COST: sorting mean "
            + $"{sortMeanUs:F1} µs/frame, conversion frame mean {frameMeanUs:F1} µs/frame, adoption "
            + $"sweep mean {sweepMeanUs:F1} µs/frame, MOVING frames mean {movingMeanUs:F1} µs, "
            + $"WORST single frame {worstUs:F1} µs against a threshold of {GuardFrameBudgetUs:F0} µs "
            + $"(one 90 Hz frame) = {worstUs / GuardFrameBudgetUs * 100.0:F2} % of a frame. "
            + $"UPDATE->LATEUPDATE POSE GAP: {gapSamples} sample(s), mean {gapMean:F2} authored px, "
            + $"WORST {gapWorst:F2} px against a threshold of {GrabbableModal.PoseGapThresholdPx:F2} "
            + $"px (one authored pixel), {gapOver} sample(s) over it — this is how stale the host pose "
            + "is for every consumer that reads it during Update (PanelSupersample.Tick does), and it "
            + "is 0 for a window nobody is moving. HOW TO READ THIS: the "
            + "MOVING correction rate is the whole question. ModBuild 198 argued that dropping this "
            + "guard to ~1 Hz while the player holds a window is what the reported drag flicker is; "
            + "that can only be true if the guard corrects something while the window MOVES. A "
            + "MOVING figure of 0 over a session with real drags falsifies it outright — the guard "
            + "was never doing anything to lose. Read the cost against the threshold before "
            + "proposing to run anything else per frame.");

        LogAdoptedOrderCensus(panel);
    }

    /// <summary>
    /// MODBUILD 203 INSTRUMENT — "did the mod collapse the game's own nested-canvas layering into one
    /// unstable tie?", answerable from ONE line.
    ///
    /// <para>WHY IT EXISTS. The sibling rebase above is a fix for a defect whose entire premise is a
    /// number nobody had ever printed: how many DISTINCT sortingOrders the game authored across the
    /// canvases one window adopts. If that number is 1, the flat game had them all tied already, the
    /// mod destroyed no layering, and this lane cannot be the cause of anything — the fix would be a
    /// no-op dressed as a remedy, and a no-op that ships unmeasured is how four rounds get spent on
    /// the wrong term. The line also carries the overrideSorting census, because a sortingOrder on a
    /// canvas WITHOUT that flag is inert: such a canvas is not its own sorting root, it draws inside
    /// its host's batch in hierarchy order, and it can neither win nor lose a tie. Adopted count,
    /// eligible count, distinct authored orders, the authored min/max, the written min/max and the
    /// lifted count are all on one line so the next capture settles it without the source.</para>
    ///
    /// <para>Printed on the guard-budget cadence (<see cref="GuardBudgetWindowSeconds"/>, gated on
    /// <see cref="ConvertedPanel.PerFrameGuards"/>) — the same throttle as the line above it, and NOT
    /// on <see cref="ConvertedPanel.Diagnostic"/>: the measurement that judges a remedy must never be
    /// gated behind a flag the drag throttle clears.</para>
    /// </summary>
    private static void LogAdoptedOrderCensus(ConvertedPanel panel)
    {
        int adopted = 0, overrideNow = 0, conceded = 0;
        int writtenMin = int.MaxValue, writtenMax = int.MinValue;
        for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
        {
            NestedCanvasRecord rec = panel.AdoptedCanvases[i];
            if (rec.Canvas == null)
                continue;
            adopted++;
            if (rec.Canvas.overrideSorting)
                overrideNow++;
            if (!rec.ConcededOverrideSorting)
                continue;
            conceded++;
            int order = rec.Canvas.sortingOrder;
            if (order < writtenMin) writtenMin = order;
            if (order > writtenMax) writtenMax = order;
        }
        panel.AdoptedOverrideNow = overrideNow;

        int eligible = panel.RebaseEligible;
        int distinct = panel.RebaseDistinctOriginals;
        string authored = eligible > 0
            ? $"{panel.RebaseMinOriginal}..{panel.RebaseMaxOriginal}"
            : "n/a (no conceded canvas)";
        string written = conceded > 0 ? $"{writtenMin}..{writtenMax}" : "n/a (nothing written)";
        int hostOrder = panel.HostCanvas != null ? panel.HostCanvas.sortingOrder : 0;

        VRLog.Info("WorldUI",
            $"ADOPTED CANVAS ORDER CENSUS '{panel.HostGo.name}': {adopted} adopted canvas(es), "
            + $"{panel.AdoptedOverrideAtAdoption} of them had overrideSorting TRUE when the mod found "
            + $"them and {overrideNow} carry it NOW; {eligible} are CONCEDED, i.e. the mod owns their "
            + $"sortingOrder. Those conceded canvases had {distinct} DISTINCT authored "
            + $"sortingOrder(s) spanning {authored}; the mod writes them at {written} against a host "
            + $"at {hostOrder} (band host+{ConcededOrderLift}..host+"
            + $"{ConcededOrderLift + ConcededOrderMaxOffset}, raised-overlay reservation at host+"
            + $"{RaisedOverlayOrderOffset}, next window's slot at host+"
            + $"{PanelOrderStep}). {panel.RebaseLifted} of them sit at a NON-ZERO sibling offset, "
            + $"i.e. {panel.RebaseLifted} canvas(es) are drawn in a different order relative to their "
            + "siblings than ModBuild 202 drew them (202 pinned every conceded canvas of a window to "
            + $"the single value host+{ConcededOrderLift}); {panel.RebaseClamped} had to be CLAMPED "
            + "into the band and therefore still tie. HOW TO READ IT: the DISTINCT count is the whole "
            + "question. N distinct original orders collapsed to 1 is the defect — the game expressed "
            + "a layering with those N values and ModBuild 202 replaced it with one number, and a tie "
            + "between canvases that carry overrideSorting is broken by Unity's canvas registration "
            + "order, which is undefined and is re-rolled on every enable/disable and every "
            + "sortingOrder rewrite (the host's order is re-sorted by eye distance every frame, so "
            + "that is once per frame). 1 distinct original order means every adopted canvas was "
            + "already tied in the flat game and THIS LANE CANNOT BE THE CAUSE. Read the CONCEDED "
            + "count first, though: it is the population the whole argument is about. A window with "
            + "0 or 1 conceded canvas has no sibling relation to destroy no matter how many canvases "
            + "it adopted, because a canvas whose overrideSorting the adoption CLEARED does not sort "
            + "as its own entry at all — its sortingOrder is inert and it draws inside the host's "
            + "batch by hierarchy, exactly as the flat game drew it. If CONCEDED is 1 and the window "
            + "still renders broken, the cause is somewhere else entirely and this line is the proof "
            + "of it, not a symptom.");
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
