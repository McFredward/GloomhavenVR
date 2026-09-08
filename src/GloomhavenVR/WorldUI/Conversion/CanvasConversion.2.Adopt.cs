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

    /// <summary>
    /// User ruling 2026-08-02 (post-reveal jump): hard ceiling on how long a floated modal may
    /// stay render-hidden while the reveal gate waits for its FINAL pose (first content fit
    /// committed + host pose/scale/rect still for <see cref="RevealStableFrames"/> checks). Past
    /// this the host is revealed anyway with a Warn — a window must never stay invisible. WHY
    /// 0.6 s: the worst HONEST settle is the ESC menu — its show/scale animation (~0.3 s) must
    /// finish before the measured content holds the 6 stable fit checks (~0.08 s at 72 Hz), then
    /// the ModalFallback 5b scale re-derivation lands one frame later and the pose-stability
    /// counter needs <see cref="RevealStableFrames"/> more frames (~0.45 s total). 0.5 s would
    /// leave only ~50 ms margin and risk deadline-revealing the ESC menu mid-settle — exactly
    /// the visible correction this gate exists to prevent.
    /// </summary>
    private const float RevealMaxWaitSeconds = 0.6f;

    /// <summary>
    /// Consecutive reveal-gate checks the host world pose, lossy scale AND host-rect size must
    /// hold still before reveal. WHY frames and not a flag: the writers that finalize the pose
    /// live in different modules and land one frame apart (CanvasConversion fit commit →
    /// ModalFallback 5b re-derives the board scale → GrabbableModal.Tick applies it to the host
    /// transform the NEXT tick) — a value-based stillness window covers every such hand-off
    /// without cross-module coupling, including writers added later.
    /// </summary>
    private const int RevealStableFrames = 3;

    /// <summary>Reveal pose-stability epsilon: world-position change below this is jitter, not a
    /// re-place (0.01 world units ≈ 0.3 mm real at the typical diorama scale ~33).</summary>
    private const float RevealPosEpsilon = 0.01f;

    /// <summary>Reveal pose-stability epsilon: rotation change below this (degrees) is jitter.</summary>
    private const float RevealRotEpsilonDeg = 0.25f;

    /// <summary>Reveal pose-stability epsilon: relative lossy-scale change below this is jitter.</summary>
    private const float RevealScaleEpsilonRel = 0.005f;

    /// <summary>Reveal pose-stability epsilon: host-rect size change below this (px) is jitter.</summary>
    private const float RevealRectEpsilonPx = 0.5f;

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
            {
                panel.AdoptedCanvases.RemoveAt(i);
                // ModBuild 203: the rebase-eligible SET shrank, so every cached offset above the
                // removed one may need to close up. Marking dirty here is what keeps the offsets
                // DENSE without ever re-deriving them per frame.
                panel.AdoptedOrderRebaseDirty = true;
            }
        }

        bool anyNew = false;
        CanvasScratch.Clear();
        panel.Target.GetComponentsInChildren(includeInactive: true, CanvasScratch);
        // ModBuild 203: GetComponentsInChildren returns PRE-ORDER DEPTH-FIRST, so `i` is this
        // canvas's hierarchy index inside the converted subtree. It is captured once, at adoption,
        // as the fallback tiebreaker for the sibling rebase (see NestedCanvasRecord.DfsIndex).
        for (int i = 0; i < CanvasScratch.Count; i++)
            anyNew |= AdoptCanvas(panel, CanvasScratch[i], i);
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
                    // The Blocker lives OUTSIDE the scanned subtree (under the host), so it has no
                    // index in the sweep above. It is a dropdown overlay and therefore excluded from
                    // the rebase by KeepOverrideSorting anyway; the sentinel just keeps the fallback
                    // ordering total and deterministic if that ever changes.
                    anyNew |= AdoptCanvas(panel, blocker, int.MaxValue);
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
    /// <param name="dfsIndex">Pre-order depth-first position of <paramref name="nested"/> in the
    /// converted subtree, recorded once (ModBuild 203, see
    /// <see cref="NestedCanvasRecord.DfsIndex"/>).</param>
    private static bool AdoptCanvas(ConvertedPanel panel, Canvas nested, int dfsIndex)
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
            if (existing.ConcededOverrideSorting)
            {
                // ModBuild 179: the game owns the flag on this one. Do NOT touch it here either —
                // this sweep runs every 30 frames and would restart the write war the concession
                // exists to end. The per-frame guard (ReassertAdoptedSorting) owns the order.
            }
            else if (existing.KeepOverrideSorting)
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
            OriginalSortingOrder = nested.sortingOrder,
            OriginalWorldCamera = SafeOriginalWorldCamera(panel, nested),
            KeepOverrideSorting = overlay,
            OverlaySortingOrder = nested.name == DropdownBlockerName
                ? DropdownBlockerSortingOrder
                : DropdownListSortingOrder,
            DfsIndex = dfsIndex,
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
        // From this write on, a GAME canvas carries a MOD camera. That is correct while the mod owns
        // it and wrong the instant the mod does not, so the canvas joins the repair pass's tracked
        // population here — with the value it is owed — and leaves it only when it is destroyed.
        // See CanvasConversion.4b.CameraOwnership.cs.
        WatchAdoptedCamera(nested, record.OriginalWorldCamera);
        if (nested.GetComponent<GraphicRaycaster>() == null)
            record.AddedRaycaster = nested.gameObject.AddComponent<GraphicRaycaster>();

        UguiPokeSurfaces.RegisterNested(panel.HostCanvas, nested);
        panel.AdoptedCanvases.Add(record);
        // ModBuild 203: a new record can change the min authored order (and therefore every cached
        // offset) of this panel's rebase-eligible set. Re-derive once, before the next write.
        panel.AdoptedOrderRebaseDirty = true;
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


    /// <summary>Running count of adoptions that found our OWN head camera on a game canvas.</summary>
    private static int _adoptCameraLeaks;

    /// <summary>
    /// The value to record as a game canvas's ORIGINAL <see cref="Canvas.worldCamera"/> — never one
    /// the MOD wrote. This is the fix for the left-edge rim (ModBuild 424); it is stated as an
    /// invariant rather than as a repair of one code path, because it is correct under every
    /// mechanism that could produce the state.
    ///
    /// <para>THE DEFECT. Adoption points a game canvas at the host's camera — in VR that is
    /// <c>GloomhavenVR.HeadCamera</c> — and <see cref="Release"/> hands
    /// <see cref="NestedCanvasRecord.OriginalWorldCamera"/> back. If the value CAPTURED here was
    /// itself written by the mod (two panels holding the same canvas across a fast re-open, an
    /// adoption that outlived its release, a panel that died with its scene), the hand-back writes
    /// our head camera onto a canvas the mod no longer owns. That canvas is now a ROOT
    /// <see cref="RenderMode.ScreenSpaceCamera"/> canvas parked at its <c>planeDistance</c> IN FRONT
    /// OF THE HMD, on layer 5, which the head camera's mask contains and must keep containing
    /// (<see cref="Core.VRLayers.GameUiLayer"/>). The moment the game fades the window in flat — a
    /// second options press interrupting the materialise, exactly the user's reproducer — its
    /// full-height left-docked panel is drawn into the eye at mid-fade alpha, head-locked. ModBuild
    /// 423's log carries both halves: the same canvas reads <c>cam=UI Camera</c> on the session's
    /// first open (frame 2467) and <c>cam=GloomhavenVR.HeadCamera</c> from frame 4140 on, and the
    /// eye census then measured the panel at rect <c>(0.041,0.000)-(0.274,1.193)</c>, alpha 0.895.</para>
    ///
    /// <para>THE RULE. Our head camera is never a value the GAME wrote — nothing game-side can find
    /// it (it is untagged, so it is not <c>Camera.main</c>, and <c>VRCameraPolicy</c> keeps it out of
    /// every game camera path). So observing it here PROVES the record would be self-referential
    /// ([[a-claim-must-not-measure-itself]], [[a-hide-saved-a-foreign-value]]). Prefer the exact
    /// truth — a live record for the same canvas on another active panel, which is the two-panel
    /// case — and otherwise the GAME'S OWN UI CAMERA, which is what the 423 log shows this very
    /// canvas carrying on the session's FIRST open (<c>cam=UI Camera</c>, rendering into
    /// <c>GloomhavenVR.DesktopScrubSink</c>, i.e. not in the eye). <c>null</c> is only the last
    /// resort, and deliberately not the first choice: a null camera turns a ScreenSpaceCamera canvas
    /// into a Screen-Space-OVERLAY one, and the two shipped subsystems that reason about overlays in
    /// VR DISAGREE about whether the HMD sees them (<c>ModalFallback</c>'s screen-bind says an
    /// overlay "reaches the desktop mirror and nothing else"; <c>EyeReachCensus</c> says Unity
    /// composites it after the camera loop and it "lands in ONE eye texture"). Handing a game canvas
    /// to a state whose visibility the mod cannot state is not a fix — so restore the camera the
    /// game had, and let null stand only where no game UI camera exists at all.</para>
    /// </summary>
    private static Camera? SafeOriginalWorldCamera(ConvertedPanel panel, Canvas nested)
    {
        Camera? observed = nested.worldCamera;
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null || !IsModOwnedCamera(observed))
            return observed;

        _adoptCameraLeaks++;

        // ModBuild 426, AND THIS IS WHY 424 FIRED 23 TIMES AND CHANGED NOTHING. `observed` above is
        // read AFTER Convert re-parented this window under the mod's world-space host, and Unity
        // reports a nested canvas's worldCamera from its ROOT — so for essentially every adopted
        // canvas it reports OURS whatever the canvas itself holds. The proof is in the 425 log: all
        // fourteen 'Content' canvases and 'UI Map Esc Menu' report sortingOrder=1000, the host's
        // SEED order and not their own (the census reads the same ESC-menu canvas at order=1200 once
        // it is a root again), and the ONE adoption that did not leak is the one canvas whose
        // overrideSorting the game holds TRUE — i.e. the one that IS its own sorting root and
        // therefore reports its own values. So prefer the value captured BEFORE the re-parent; that
        // is an observation of the game's canvas, and everything below it is a fallback.
        Camera? preCaptured = PreCapturedCameraOf(panel, nested, out bool preSeen);
        if (preSeen && !IsModOwnedCamera(preCaptured))
        {
            // HW-VERIFY: the ordinary, healthy path once ModBuild 426 ships. If the two branches
            // below still dominate this log, the pre-capture is not reaching the canvases that leak.
            VRLog.Note("WorldUI", $"MODAL ADOPT CAMERA LEAK: game canvas '{nested.name}' reads OUR "
                + $"camera '{CamName(observed)}' from the float host it is now nested under — an "
                + "INHERITED read, not the canvas's own value. Recording the camera captured before "
                + $"the re-parent, '{CamName(preCaptured)}', which is what the GAME had. LIVE VALUE "
                + $"AFTER THIS GUARD: '{CamName(nested.worldCamera)}' — adoption sets it to the "
                + "host's camera on the next line, which is correct while the mod owns this canvas; "
                + "the release and the repair pass own it from the moment the mod does not. "
                + $"LEAK #{_adoptCameraLeaks} this session."); // HW-VERIFY
            return preCaptured;
        }
        for (int p = 0; p < Active.Count; p++)
        {
            ConvertedPanel other = Active[p];
            for (int i = 0; i < other.AdoptedCanvases.Count; i++)
            {
                NestedCanvasRecord rec = other.AdoptedCanvases[i];
                if (!ReferenceEquals(rec.Canvas, nested) || ReferenceEquals(rec.OriginalWorldCamera, head))
                    continue;
                VRLog.Note("WorldUI", $"MODAL ADOPT CAMERA LEAK: game canvas '{nested.name}' already "
                    + $"carried OUR head camera '{head.name}' when panel "
                    + $"'{(other.HostGo != null ? other.HostGo.name : "<dead host>")}' still holds it. "
                    + "Recording THAT panel's captured original "
                    + $"'{(rec.OriginalWorldCamera != null ? rec.OriginalWorldCamera.name : "<none>")}' "
                    + "instead of the value the mod itself wrote, so the release hands the game back "
                    + $"what the game had. LEAK #{_adoptCameraLeaks} this session."
                    // APPENDED ModBuild 426 — never reword the sentence above it. 424 shipped a line
                    // that stated what it CAUGHT and never what the canvas was left holding, so a
                    // catch that corrected nothing read exactly like a fix for a whole build.
                    + $" LIVE VALUE AFTER THIS GUARD: '{CamName(nested.worldCamera)}'"
                    + (IsModOwnedCamera(nested.worldCamera)
                        ? " — STILL OURS. That is BY DESIGN here (adoption binds it to the host on "
                          + "the next line); it stops being by design at Release, which is where "
                          + "'] CANVAS CAMERA RESTORE' states the live value on the far side of the "
                          + "re-parent, and '] CANVAS CAMERA REPAIR' corrects anything that survives."
                        : " — not a mod camera.")); // HW-VERIFY
                return rec.OriginalWorldCamera;
            }
        }

        Camera? gameUi = FindGameUiCamera(head);
        VRLog.Note("WorldUI", $"MODAL ADOPT CAMERA LEAK: game canvas '{nested.name}' "
            + $"(mode={(nested.rootCanvas != null ? nested.rootCanvas.renderMode : nested.renderMode)}, "
            + $"layer {nested.gameObject.layer}) was found pointing at OUR head camera '{head.name}' "
            + "with no live panel holding it — a value only the mod ever writes, left behind by an "
            + "earlier conversion, which parks the canvas in front of the HMD at its planeDistance "
            + "and makes it follow the head. THIS IS THE LEFT-EDGE RIM; the head camera's mask "
            + "contains the game UI layer by design and cannot drop it (Core/VRLayers.GameUiLayer). "
            + $"Recording '{(gameUi != null ? gameUi.name : "<null>")}' as its original instead"
            + (gameUi != null
                ? " — the game's own UI camera, which renders into "
                  + $"'{(gameUi.targetTexture != null ? gameUi.targetTexture.name : "the BACKBUFFER")}'."
                : " — no game UI camera exists here, so the canvas degrades to Screen-Space-OVERLAY, "
                  + "which ModalFallback's screen-bind owns.")
            + $" LEAK #{_adoptCameraLeaks} this session."
            // APPENDED ModBuild 426 — see the note on the other branch. The sentence above is 424's
            // and stays word for word.
            + $" LIVE VALUE AFTER THIS GUARD: '{CamName(nested.worldCamera)}'"
            + (IsModOwnedCamera(nested.worldCamera)
                ? " — STILL OURS, which is by design at ADOPTION and never at release. "
                  + "PRE-CAPTURE: " + (preSeen
                      ? "this canvas WAS captured before the re-parent and the captured value was a "
                        + "mod camera too, so the game's own value was already gone before this "
                        + "conversion started — the repair pass is what closes that."
                      : "this canvas was NOT captured before the re-parent (it arrived late — a "
                        + "pooled row, a dropdown, a repopulated sub-view), so this branch is the "
                        + "only answer available and it is a guess by construction.")
                : " — not a mod camera.")); // HW-VERIFY
        return gameUi;
    }

    /// <summary>
    /// The GAME's own UI camera — the one whose culling mask is the game UI layer and which the
    /// game's <c>CanvasManager</c> hands to its screen-space canvases (observed in the 423 log as
    /// <c>'UI Camera' … mask=0x00000020 → GloomhavenVR.DesktopScrubSink</c>). Identified by the
    /// game's own "UICamera" TAG first — the same classification <c>FlatScreen.IsUiCamera</c> and
    /// <c>ModalFallback.FindCompositedUiCamera</c> use — and by that exact mask as the fallback, so
    /// a retagged build still resolves. Never <paramref name="head"/>, and never a mod-created
    /// camera (all of ours are named <c>GloomhavenVR.*</c>). Called only on a leak, which is a
    /// once-per-window-open event at worst, so the camera sweep is not on any hot path.
    /// </summary>
    private static Camera? FindGameUiCamera(Camera head)
    {
        int count = Core.VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        Camera? byMask = null;
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || ReferenceEquals(cam, head) || cam.name.StartsWith("GloomhavenVR."))
                continue;
            if (cam.CompareTag("UICamera"))
                return cam;
            if (byMask == null && cam.cullingMask == Core.VRLayers.GameUiLayerMask)
                byMask = cam;
        }
        return byMask;
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

        // WALKED, NOT FLATTENED (ModBuild 180), because a foreign 3D subtree must be skipped
        // WHOLE — see IsForeignRenderSubtree. GetComponentsInChildren hands back a flat list with
        // no way to stop at a branch, so the sweep descends explicitly.
        TransformScratch.Clear();
        TransformScratch.Add(panel.HostGo.transform);
        int moved = 0;
        int skipped = 0;
        while (TransformScratch.Count > 0)
        {
            int last = TransformScratch.Count - 1;
            Transform t = TransformScratch[last];
            TransformScratch.RemoveAt(last);
            if (t == null)
                continue;
            if (!ReferenceEquals(t, panel.HostGo.transform) && IsForeignRenderSubtree(t))
            {
                skipped++;
                continue; // and NOT its children either — that is the whole point
            }
            if (t.gameObject.layer != modLayer)
            {
                if (!IsRelayered(panel, t))
                    panel.Relayered.Add(new LayerRecord { Transform = t, OriginalLayer = t.gameObject.layer });
                t.gameObject.layer = modLayer;
                moved++;
            }
            for (int i = t.childCount - 1; i >= 0; i--)
                TransformScratch.Add(t.GetChild(i));
        }
        TransformScratch.Clear();
        if (moved > 0 || skipped > 0)
            VRLog.Info("WorldUI", $"MODAL LAYER: moved {moved} transform(s) of '{panel.HostGo.name}' onto the " +
                                  $"dedicated mod layer {modLayer} — only the HMD head camera renders it now, " +
                                  "the game UI Camera can no longer double-draw the world-space modal" +
                                  (initial ? "." : " (pooled/late children).") +
                                  (skipped > 0
                                      ? $" {skipped} subtree(s) LEFT ALONE because they carry a real Renderer "
                                        + "or Camera — a live 3D model inside a window belongs to the game's own "
                                        + "preview camera, which culls it BY LAYER."
                                      : string.Empty));
    }

    /// <summary>
    /// Is this transform the root of a subtree the mod-layer sweep must NOT touch?
    ///
    /// <para>THE FLICKER THIS ENDS (user, 3D map room: <i>"Wenn ich einen Character in dem Fenster
    /// öffne flackert der Inhalt des Fensters stark"</i>, with a screenshot showing the character
    /// model rendered LARGE in front of its own window). uGUI draws through
    /// <c>CanvasRenderer</c>, which is NOT a <c>Renderer</c> — so any real <c>Renderer</c> inside a
    /// converted window is by definition NOT part of the UI. In the party/character windows it is
    /// the live 3D character rig, which the game renders with its OWN preview camera into a
    /// RenderTexture the panel then displays. That camera culls BY LAYER. Moving the rig onto the
    /// mod layer therefore did two things at once: the preview camera stopped seeing its subject,
    /// and our head camera — whose mask is broad in the map room — started drawing the raw model
    /// directly in the world at the panel's position. Two pictures of the same character fighting
    /// over the same pixels is exactly what "flackert stark" looks like.</para>
    ///
    /// <para>The mod layer exists to hide the floated modal from the game's UI CAMERA. A 3D
    /// renderer was never visible to that camera in the first place, so leaving it alone costs
    /// nothing and is the only correct answer. Same for a nested <c>Camera</c>: relayering the
    /// object a camera sits on is meaningless, and its subtree is its own business.</para>
    /// </summary>
    private static bool IsForeignRenderSubtree(Transform t)
    {
        GameObject go = t.gameObject;
        return go.GetComponent<Renderer>() != null || go.GetComponent<Camera>() != null;
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
        {
            // ROUND 6: OUR OWN content change must reset the fit's settle streak. The ModBuild 22
            // cold open committed its first fit one frame BEFORE this sweep caught two late-fading
            // full-window images, so that fit measured a 1934 px wide union (the backings) and the
            // very next measurement — of the 388 px menu column that actually remains — rejected it
            // and burned a verify correction. A window whose measured content we just changed has
            // not settled, by definition.
            panel.FitOneShotStableCount = 0;
            panel.FitSettleStillCount = 0;
            panel.FitOneShotStableGraphics = 0;
            VRLog.Info("WorldUI", $"MODAL BACKGROUND: disabled {hidden} full-window backing/blur image(s) in " +
                                  $"'{panel.HostGo.name}' — the modal now shows only its foreground content " +
                                  (initial ? "(transparent background)" : "(late fade-in)") +
                                  "; the content-fit settle streak was reset (the measured content just changed).");
        }
    }

    // ---- 2D flatten (test #21) + THE WINDOW FLATNESS GUARANTEE (ModBuild 193) --------------

    /// <summary>Local rotation counts as 3D beyond this angle (degrees) off identity.</summary>
    private const float FlattenAngleEpsilon = 0.05f;

    /// <summary>Local z counts as 3D beyond this many uGUI pixels.</summary>
    private const float FlattenZEpsilon = 0.01f;

    /// <summary>
    /// THE COST BOUND, AND IT IS THE ONLY ONE — transforms the DISCOVERY walk may pop per frame per
    /// panel. What is bounded is a per-frame budget, not a period, and that choice is the whole
    /// design:
    ///
    /// <para>A full <c>GetComponentsInChildren</c> rescan every frame is what the old sweep did, and
    /// on the surfaces it ran on (tens of transforms) it was free. The floated windows are three
    /// orders of magnitude away — the ModBuild 192 log measured 'New Party display' at 2700
    /// transforms and 'UI Shop Item Window' at 2114 — so that shape had to go. The obvious
    /// replacement, "full rescan every N frames", trades the average for a SPIKE: nine cheap frames
    /// and one that walks 2700 transforms, which on a 13.9 ms budget is a periodic hitch and exactly
    /// the kind of thing that reads as stutter in a headset.</para>
    ///
    /// <para>So the walk is RESUMABLE instead (<see cref="ConvertedPanel.FlattenWalk"/>): every
    /// frame pops at most this many transforms and keeps the rest for the next frame. The cost is
    /// FLAT and identical for a 50-transform card and a 2700-transform window; only the time to come
    /// all the way round differs, and that number is measured and printed per window
    /// (<c>FlattenLastCycleFrames</c>). At 400 a 2700-transform window is fully re-examined every 7
    /// frames (~95 ms at 72 Hz) — and note this is the DISCOVERY latency for something new only.
    /// Everything already known is re-asserted EVERY frame, so nothing that has been made flat can
    /// go 3D and stay that way for even one tick.</para>
    ///
    /// <para>THIS IS THE DIAL. If the printed cost is too high, lower it (slower discovery, cheaper
    /// frames); if the printed discovery latency shows a pooled card visibly tilted before it is
    /// caught, raise it. Both readings come out of the census line below.</para>
    /// </summary>
    private const int FlattenWalkBudgetPerFrame = 400;

    /// <summary>Minimum frames between two census lines for one window (~0.8 s at 72 Hz). The
    /// census is CHANGE-GATED on its counts, so a settled window prints nothing; this only stops a
    /// window whose pooled content is churning from printing every frame.</summary>
    private const int FlattenCensusIntervalFrames = 60;

    // Scratch buffer (census key assembly only; reused, no per-call allocations).
    private static readonly System.Text.StringBuilder FlattenCensusScratch = new(320);

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
    /// - and (ModBuild 193, the mechanism behind the WINDOW report) <c>ObjectPool</c>'s
    ///   card path reparents with <c>SetParent(parent)</c> — <c>worldPositionStays:
    ///   true</c> — at ObjectPool.cs:468, and resets local rotation only when the caller
    ///   asks (<c>resetLocalRotation</c> defaults false, :415 / :481-483; local z IS
    ///   always zeroed at :484-486, rotation is not). No item-card caller asks. Under a
    ///   flat screen-space canvas the preserved WORLD rotation is a zero LOCAL rotation
    ///   and nobody ever saw it; under a world-space host YAWED to face the player it
    ///   lands as a local rotation the size of that yaw.
    ///
    /// THE SWEEP THIS FEEDS (rewritten in ModBuild 193, see
    /// <see cref="RunFlattenPass"/>): a per-frame unbudgeted RE-ASSERT of everything
    /// already known — which is what makes "the values come back live" harmless — plus a
    /// budgeted, resumable DISCOVERY walk for transforms nobody has seen yet. The old
    /// shape was a full <c>GetComponentsInChildren</c> every frame, which was free on the
    /// surfaces it ran on (tens of transforms) and is not free on a floated window (2700).
    /// The walk is now explicit rather than component-list based for the same reason
    /// <see cref="ApplyModLayer"/>'s is: a foreign render subtree must be skipped WHOLE.
    ///
    /// TWO OWNERS, ONE CLAMP. This method is the entry point for the test-#21 opt-in
    /// family (<c>flatten2D</c>), driven centrally from <see cref="LateTick"/>. The
    /// floated-window family (<c>flattenWindow</c>) runs the identical pass from its own
    /// <see cref="PanelFlattenDriver"/> — see <see cref="DriveWindowFlatten"/> for why the
    /// two are separate. No panel is ever in both.
    /// </summary>
    private static void FlattenSubtree(ConvertedPanel panel) => RunFlattenPass(panel);

    /// <summary>
    /// THE WINDOW FLATNESS GUARANTEE, per-frame entry point — called from
    /// <see cref="PanelFlattenDriver"/>'s LateUpdate, which rides on the panel's own host
    /// GameObject. Every gate here is a safety property, not a preference:
    /// <list type="bullet">
    /// <item>NOT <see cref="ConvertedPanel.FlattenEnabled"/> — that family is already swept by
    /// <c>CanvasConversion.LateTick</c> and must not be swept twice in one frame.</item>
    /// <item>STILL IN <see cref="Active"/> — <c>Release</c> removes the panel from the registry
    /// (CanvasConversion.4.Lifecycle.cs:15) BEFORE it restores every recorded rotation/z
    /// (:115-124) and only then destroys the host. Unity would still run this LateUpdate in the
    /// SAME frame, on a host whose <c>Destroy</c> is only queued — without this gate the sweep
    /// would re-flatten and re-record everything the release had just handed back, and the host
    /// would then be destroyed with those records inside it. That is a permanent, silent loss of
    /// the game's own styling for the rest of the session. "Only while it is still ours" is
    /// exactly this line.</item>
    /// </list>
    /// </summary>
    internal static void DriveWindowFlatten(ConvertedPanel? panel)
    {
        if (panel == null || !panel.FlattenWindowGuarantee || panel.FlattenEnabled || !panel.IsAlive)
            return;
        bool ours = false;
        for (int i = 0; i < Active.Count; i++)
        {
            if (ReferenceEquals(Active[i], panel))
            {
                ours = true;
                break;
            }
        }
        if (!ours)
            return;
        RunFlattenPass(panel);
        LogFlattenCensus(panel, force: false);
    }

    /// <summary>
    /// One flatten pass. Two halves with two different costs, and the split IS the cost bound:
    ///
    /// <para>(A) THE RE-ASSERT, EVERY CALL, UNBUDGETED. Walk the RECORDED set only — the transforms
    /// this panel has already caught carrying 3D — and write rotation → identity / z → 0 wherever
    /// the value has come back. This is the "the values come back live" half. It is O(records),
    /// which is a handful, never the subtree size, and it is why nothing that has been made flat can
    /// be seen tilted for even one frame.</para>
    ///
    /// <para>(B) THE DISCOVERY WALK, BUDGETED AND RESUMABLE. Look for transforms nobody has seen yet
    /// (pooled children, lazily populated rows), at most
    /// <see cref="FlattenWalkBudgetPerFrame"/> transforms per frame, resuming where the last frame
    /// stopped. See that constant for why a per-frame budget and not a period. A completed cycle
    /// also PRUNES: a record whose transform died is dropped, and a record whose transform the game
    /// has moved OUT of this window is RESTORED first and then dropped.</para>
    ///
    /// <para><paramref name="completeCycle"/> ignores the budget and runs the walk all the way
    /// round in this call. Used once, from <see cref="Convert"/>: a subtree converts ALREADY tilted,
    /// and it has to be flat on the first frame anybody could see it, not seven frames later.</para>
    /// </summary>
    private static void RunFlattenPass(ConvertedPanel panel, bool completeCycle = false)
    {
        if (panel.Target == null)
            return;

        // ---- (A) re-assert the known set ---------------------------------------------------
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        int reasserts = 0;
        for (int i = 0; i < panel.Flattened.Count; i++)
        {
            Transform tf = panel.Flattened[i].Transform;
            if (tf == null)
                continue; // pruned at cycle end; never allocate a removal on the per-frame path
            Vector3 pos = tf.localPosition;
            Quaternion rot = tf.localRotation;
            if (Quaternion.Angle(rot, Quaternion.identity) > FlattenAngleEpsilon)
            {
                tf.localRotation = Quaternion.identity;
                reasserts++;
            }
            if (Mathf.Abs(pos.z) > FlattenZEpsilon)
            {
                tf.localPosition = new Vector3(pos.x, pos.y, 0f);
                reasserts++;
            }
        }
        panel.FlattenReasserts = reasserts;
        panel.FlattenReassertTotal += reasserts;
        panel.FlattenReassertTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        panel.FlattenReassertRuns++;

        // ---- (B) budgeted, resumable discovery walk ------------------------------------------
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (panel.FlattenWalk.Count == 0)
        {
            // Start of a cycle: seed at the root and zero the cycle accumulators.
            panel.FlattenWalk.Add(panel.Target);
            panel.FlattenCycleVisited = 0;
            panel.FlattenCycleForeign = 0;
            panel.FlattenCyclePlain3D = 0;
            panel.FlattenCycleFrames = 0;
            panel.FlattenCycleForeignSample = null;
            panel.FlattenCyclePlain3DSample = null;
        }
        panel.FlattenCycleFrames++;

        int budget = completeCycle ? int.MaxValue : FlattenWalkBudgetPerFrame;
        while (budget-- > 0 && panel.FlattenWalk.Count > 0)
        {
            int last = panel.FlattenWalk.Count - 1;
            Transform t = panel.FlattenWalk[last];
            panel.FlattenWalk.RemoveAt(last);
            if (t == null)
                continue; // destroyed between two frames of this cycle — nothing to do
            panel.FlattenCycleVisited++;

            // CASE (2) — A FOREIGN RENDER SUBTREE. Skipped WHOLE and left EXACTLY as the game has
            // it: not relayered (that shipped once and drew the character twice — see
            // IsForeignRenderSubtree), and not flattened either. Flattening it would be the SAME
            // mistake in a different coordinate: a real Renderer inside a window is a live 3D model
            // the game aims its OWN preview camera at, so zeroing its local rotation/z moves the
            // subject relative to that camera and the window's picture of it changes or empties.
            // The honest answer for a genuinely 3D object is that it IS 3D; what would make it lie
            // flat on the window is being CAPTURED into the window's image, which is
            // PanelSupersample's path, not this one. Counted and NAMED here so the log says which
            // object it was rather than only how many there were.
            if (!ReferenceEquals(t, panel.Target) && IsForeignRenderSubtree(t))
            {
                panel.FlattenCycleForeign++;
                panel.FlattenCycleForeignSample ??= t.name;
                continue; // and NOT its children either — that is the whole point
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                panel.FlattenWalk.Add(t.GetChild(i));

            // The root's own pose is Convert's business (flattened there, restored whole by
            // Release) — the sweep owns strictly the subtree below it.
            if (ReferenceEquals(t, panel.Target))
                continue;

            Vector3 pos = t.localPosition;
            Quaternion rot = t.localRotation;
            bool tiltedRot = Quaternion.Angle(rot, Quaternion.identity) > FlattenAngleEpsilon;
            bool tiltedZ = Mathf.Abs(pos.z) > FlattenZEpsilon;
            if (!tiltedRot && !tiltedZ)
                continue;

            if (!(t is RectTransform))
            {
                // A plain Transform holder inside a uGUI tree, carrying 3D but not a render root.
                // COUNTED AND NAMED, NEVER WRITTEN — see ConvertedPanel.FlattenLastPlain3D. This is
                // the census bucket to read if every other count comes back zero.
                panel.FlattenCyclePlain3D++;
                panel.FlattenCyclePlain3DSample ??= t.name;
                continue;
            }

            // CASES (1) and (3) — a RectTransform carrying a baked/inherited local rotation and/or
            // a local z, whether or not it also carries a nested Canvas. A nested canvas needs no
            // separate rule: its own transform IS a RectTransform and the same clamp puts its whole
            // subtree back in the window plane. It is counted separately only so the census can say
            // whether a case-(3) canvas was involved.
            if (!IsFlattenRecorded(panel, t))
            {
                panel.Flattened.Add(new FlattenRecord
                {
                    Transform = t,
                    OriginalLocalZ = pos.z,
                    OriginalLocalRotation = rot,
                });
            }
            if (tiltedRot)
                t.localRotation = Quaternion.identity;
            if (tiltedZ)
                t.localPosition = new Vector3(pos.x, pos.y, 0f);
        }

        if (panel.FlattenWalk.Count == 0)
        {
            // Cycle complete: prune, classify and PUBLISH. The census only ever reads published
            // numbers, so it can never report a window half-walked.
            PruneFlattenRecords(panel);
            ClassifyFlattenRecords(panel);
            panel.FlattenLastVisited = panel.FlattenCycleVisited;
            panel.FlattenLastForeign = panel.FlattenCycleForeign;
            panel.FlattenForeignSample = panel.FlattenCycleForeignSample;
            panel.FlattenLastPlain3D = panel.FlattenCyclePlain3D;
            panel.FlattenPlain3DSample = panel.FlattenCyclePlain3DSample;
            panel.FlattenLastCycleFrames = panel.FlattenCycleFrames;
        }
        panel.FlattenScanTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        panel.FlattenScanRuns++;

        // Legacy growth line for the test-#21 opt-in family, wording unchanged so anything that
        // greps for it still finds it. The floated-window family prints the full census instead.
        if (!panel.FlattenWindowGuarantee
            && panel.Flattened.Count > panel.FlattenLoggedCount
            && Time.frameCount >= panel.FlattenLogNextFrame)
        {
            VRLog.Info("WorldUI", $"Flatten grew to {panel.Flattened.Count} transform(s) in " +
                                  $"'{panel.HostGo.name}' (pooled children).");
            panel.FlattenLoggedCount = panel.Flattened.Count;
            panel.FlattenLogNextFrame = Time.frameCount + CanvasSweepIntervalFrames;
        }
    }

    /// <summary>
    /// RESTORE DISCIPLINE, the half <c>Release</c> cannot do. Drop records whose transform the game
    /// destroyed (pooled children are destroyed, and without this the list — and the O(n) lookup
    /// that walks it — grows for the whole life of a long-lived window), and HAND BACK anything the
    /// game has re-parented out of this window before dropping it: it leaves carrying exactly the
    /// local rotation and z we took from it. Runs on the rescan cadence only; the per-frame
    /// re-assert never pays for the ancestor walk.
    /// </summary>
    private static void PruneFlattenRecords(ConvertedPanel panel)
    {
        for (int i = panel.Flattened.Count - 1; i >= 0; i--)
        {
            FlattenRecord record = panel.Flattened[i];
            if (record.Transform == null)
            {
                panel.Flattened.RemoveAt(i);
                continue;
            }
            if (panel.Target != null && record.Transform.IsChildOf(panel.Target))
                continue;
            Vector3 pos = record.Transform.localPosition;
            record.Transform.localPosition = new Vector3(pos.x, pos.y, record.OriginalLocalZ);
            record.Transform.localRotation = record.OriginalLocalRotation;
            panel.Flattened.RemoveAt(i);
        }
        if (panel.FlattenLoggedCount > panel.Flattened.Count)
            panel.FlattenLoggedCount = panel.Flattened.Count;
    }

    /// <summary>
    /// Census breakdown of the recorded set: how many transforms we hold because of a ROTATION, how
    /// many because of a local Z, how many for both, and how many of them are nested canvases (case
    /// 3). Recomputed on the rescan cadence from the ORIGINALS, so the numbers describe what the
    /// game authored, not what the subtree looks like after we clamped it.
    /// </summary>
    private static void ClassifyFlattenRecords(ConvertedPanel panel)
    {
        int rotOnly = 0, zOnly = 0, both = 0, canvases = 0;
        for (int i = 0; i < panel.Flattened.Count; i++)
        {
            FlattenRecord record = panel.Flattened[i];
            if (record.Transform == null)
                continue;
            bool r = Quaternion.Angle(record.OriginalLocalRotation, Quaternion.identity) > FlattenAngleEpsilon;
            bool z = Mathf.Abs(record.OriginalLocalZ) > FlattenZEpsilon;
            if (r && z)
                both++;
            else if (r)
                rotOnly++;
            else if (z)
                zOnly++;
            if (record.Transform.GetComponent<Canvas>() != null)
                canvases++;
        }
        panel.FlattenRotationCount = rotOnly;
        panel.FlattenZCount = zOnly;
        panel.FlattenBothCount = both;
        panel.FlattenLastNestedCanvas = canvases;
    }

    /// <summary>
    /// THE PER-WINDOW CENSUS. Printed unconditionally at conversion — <b>including for a window
    /// with nothing to report</b>, because a silent log must never be readable as "nothing looked" —
    /// and afterwards whenever the COUNTS change (rate-limited to
    /// <see cref="FlattenCensusIntervalFrames"/>). The measured cost is deliberately kept OUT of
    /// the change key: it fluctuates every frame and would turn a change-gate into a spam loop.
    /// </summary>
    private static void LogFlattenCensus(ConvertedPanel panel, bool force)
    {
        if (!panel.FlattenWindowGuarantee || panel.HostGo == null)
            return;
        if (!force && Time.frameCount < panel.FlattenCensusNextFrame)
            return;

        FlattenCensusScratch.Length = 0;
        FlattenCensusScratch.Append(panel.Flattened.Count).Append('/')
            .Append(panel.FlattenRotationCount).Append('/').Append(panel.FlattenZCount).Append('/')
            .Append(panel.FlattenBothCount).Append('/').Append(panel.FlattenLastNestedCanvas).Append('/')
            .Append(panel.FlattenLastForeign).Append('/').Append(panel.FlattenLastPlain3D).Append('/')
            .Append(panel.FlattenLastVisited).Append('/').Append(panel.FlattenReasserts > 0 ? 1 : 0);
        string key = FlattenCensusScratch.ToString();
        if (!force && string.Equals(key, panel.FlattenCensusLast, System.StringComparison.Ordinal))
            return;
        panel.FlattenCensusLast = key;
        panel.FlattenCensusNextFrame = Time.frameCount + FlattenCensusIntervalFrames;

        double scanUs = panel.FlattenScanRuns > 0
            ? panel.FlattenScanTicks * 1000000.0 / System.Diagnostics.Stopwatch.Frequency / panel.FlattenScanRuns
            : 0.0;
        double reassertUs = panel.FlattenReassertRuns > 0
            ? panel.FlattenReassertTicks * 1000000.0 / System.Diagnostics.Stopwatch.Frequency / panel.FlattenReassertRuns
            : 0.0;

        VRLog.Info("WorldUI",
            $"WINDOW FLATNESS '{panel.HostGo.name}': Flattened {panel.Flattened.Count} transform(s) " +
            $"({panel.FlattenRotationCount} rotation-only, {panel.FlattenZCount} z-only, " +
            $"{panel.FlattenBothCount} both, {panel.FlattenLastNestedCanvas} of them nested canvases) " +
            $"out of {panel.FlattenLastVisited} walked; {panel.FlattenLastForeign} foreign render " +
            $"subtree(s) LEFT ALONE" +
            (panel.FlattenForeignSample != null ? $" (first: '{panel.FlattenForeignSample}')" : "") +
            $"; {panel.FlattenLastPlain3D} plain non-Rect transform(s) carry 3D and are also left alone" +
            (panel.FlattenPlain3DSample != null ? $" (first: '{panel.FlattenPlain3DSample}')" : "") +
            $". Re-asserts this frame {panel.FlattenReasserts}, {panel.FlattenReassertTotal} since " +
            $"conversion. COST: {scanUs:F0} us/frame for the budgeted discovery walk " +
            $"({FlattenWalkBudgetPerFrame} transform(s) max per frame, one full pass every " +
            $"{panel.FlattenLastCycleFrames} frame(s)) plus {reassertUs:F0} us/frame for the " +
            $"unbudgeted re-assert of the {panel.Flattened.Count} known transform(s). " +
            "HOW TO READ THIS LINE. " +
            "(1) The user's complaint is an element standing OUT of the window plane. If 'Flattened' " +
            "is non-zero the sweep found and clamped exactly that, and the element should now lie in " +
            "the plane — case (1)/(3). (2) If 'Flattened' is 0 but 'foreign render subtree(s)' is not, " +
            "the thing sticking out is a REAL 3D MODEL the game renders with its own preview camera; " +
            "it is deliberately untouched (moving it once drew the character twice, and flattening it " +
            "would move it out of that camera's frame), and the only correct way to make it lie on the " +
            "window is to capture it into the window's image — PanelSupersample, not this sweep. " +
            "(3) If both are 0 but 'plain non-Rect transform(s)' is not, the flatten contract's " +
            "RectTransform-only rule is what is holding it back and the named object is the next " +
            "thing to look at. (4) If EVERY count is 0 the window is already flat and whatever the " +
            "user sees is not a local pose at all — look at the host's own orientation next, not at " +
            "its contents. (5) 'Re-asserts since conversion' climbing steadily means a GAME writer is " +
            "putting the rotation/z back every frame; this sweep is change-gated and runs once per " +
            "frame in LateUpdate, so it cannot alternate a value with itself, but a second writer " +
            "would show up here first. (6) COST is what this sweep costs EVERY frame, not a peak: " +
            "the discovery walk is budgeted per frame, so a 2700-transform window and a 50-transform " +
            "card cost the same per frame and differ only in 'one full pass every N frame(s)' — " +
            "which is also the worst-case delay before a newly pooled tilted child is discovered. If " +
            "the us/frame is too high, lower CanvasConversion.FlattenWalkBudgetPerFrame; if a card " +
            "is visibly tilted for a moment before it snaps flat, raise it.");
    }

    private static bool IsFlattenRecorded(ConvertedPanel panel, Transform t)
    {
        for (int i = 0; i < panel.Flattened.Count; i++)
        {
            if (ReferenceEquals(panel.Flattened[i].Transform, t))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Attach the per-window flatten driver to <paramref name="panel"/>'s host. Same pattern as
    /// <c>SurfaceGrabBar.HostLateSync</c> and <c>AvatarMirror.LatePin</c>: a tiny MonoBehaviour on
    /// the object it serves, so its lifetime is the host's lifetime and no registry can leak it.
    /// </summary>
    private static void AttachFlattenDriver(ConvertedPanel panel)
    {
        if (panel.HostGo == null)
            return;
        panel.HostGo.AddComponent<PanelFlattenDriver>().Panel = panel;
    }

}

/// <summary>
/// Per-window LateUpdate driver for the flatness guarantee (ModBuild 193).
///
/// <para>WHY LateUpdate: the sweep has to run AFTER the game's Update-time tween writers, or a tilt
/// written this frame renders this frame — the reason <c>CanvasConversion.LateTick</c> is a
/// LateUpdate service too. Any LateUpdate satisfies that, since Unity runs every Update before every
/// LateUpdate.</para>
///
/// <para>WHY ITS OWN COMPONENT INSTEAD OF THE CENTRAL LateTick: the central one is gated on
/// <c>ConvertedPanel.FlattenEnabled</c>, and <c>PanelSupersample.Eligible</c> refuses every panel
/// carrying that flag (PanelSupersample.1.Core.cs). Setting it for the floated-window family would
/// have turned supersampling off for the family it is actually running on — measured live on
/// 'New Party display' and 'Quest Log Manager' in the ModBuild 192 log. A separate opt-in and a
/// separate driver keep both features on, and cost one component per floated window.
/// RESOLVED AT INTEGRATION (ModBuild 193): that refusal was verified false and REMOVED. This
/// driver survives on its own merits — the budgeted resumable walk and the per-window census —
/// not because supersampling refuses anything.</para>
///
/// <para>NO ORDERING HAZARD AGAINST THE OTHER FLATTENERS. <c>TooltipOnWindow.LateTick</c> clamps the
/// same two components on tooltip subtrees inside these same windows, and both writers write the
/// SAME value (identity / z 0) under the same epsilons. Two writers of one number are only dangerous
/// when they disagree — the MultiPass eye-disagreement bug this project has shipped came from two
/// owners ALTERNATING a value between frames. Here neither writer can produce a value the other
/// would change, so the order between them is irrelevant and no frame can end tilted.</para>
///
/// <para>NEVER THROWS. An unguarded exception in a MonoBehaviour Update starves VR input for the
/// rest of the session; this one catches, reports once, and switches ITSELF off rather than the
/// window.</para>
/// </summary>
internal sealed class PanelFlattenDriver : MonoBehaviour
{
    internal ConvertedPanel? Panel;
    private bool _failed;

    private void LateUpdate()
    {
        if (_failed)
            return;
        try
        {
            CanvasConversion.DriveWindowFlatten(Panel);
        }
        catch (System.Exception ex)
        {
            _failed = true;
            enabled = false;
            VRLog.Error("WorldUI", $"WINDOW FLATNESS: the per-frame sweep on '{name}' threw " +
                                   $"({ex.GetType().Name}: {ex.Message}) — this driver is now OFF for this " +
                                   "window and the window keeps whatever pose the game gives its content. " +
                                   "Everything already flattened stays flattened and is still restored on " +
                                   "release; no other window is affected.");
        }
    }
}
