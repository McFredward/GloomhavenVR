using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 6 (COMPLETE render hide for the reveal gate). NEW members only —
// appended after parts 1-5 in the filename sort, so the existing member/static-initializer
// order (which the refactor guard tracks and part 1's header explains) is untouched.

internal static partial class CanvasConversion
{
    // ---- the complete, atomic render hide (user ruling 2026-08-02, round 2) -----------------
    //
    // WHAT WAS STILL RENDERING. The reveal gate (part 4, TickRevealGate) held a freshly floated
    // window back with `HostCanvas.enabled = false`. That switches off ONE canvas's own batch —
    // and a floated window is drawn by a lot more than that:
    //
    //  * THE GRAB BAR (the user named it: "ein Aufploppen der Greifbar"). GrabbableModal.EnsureFrame
    //    builds `GloomhavenVR.ModalGrab_<name>/Frame/Bar` — a Cube MeshRenderer at BarSortingOrder —
    //    in a SCENE-ROOT GameObject that is not under the host at all (the host follows the frame,
    //    not the other way round). No canvas state on earth can hide it. It is built AFTER Convert,
    //    from the host's PRE-FIT rect (SyncBar seats it under a 1920x1080-ish frame at the pre-fit
    //    extraScale), so it popped in far too wide and far too low, then jumped to its real place
    //    when the fit and the 5b scale re-derivation landed ~150 ms later.
    //  * THE MODAL DEPTH MASK (same holder tree, GrabbableModal.BuildDepthMask): a depth-WRITING
    //    per-graphic quad mesh. Its colour blend is Zero/One so it paints nothing — but it stamps
    //    the pre-fit window footprint into the depth buffer at queue 2999, so for those ~150 ms
    //    every transparent thing behind that oversized rectangle failed ZTest: a window-shaped
    //    hole in the wrong place, which is exactly "das Fenster woanders".
    //  * THE MR BACKING PLATE (MrBacking.TickPanels): an OPAQUE plate MeshRenderer parented under
    //    HostRect for every live converted panel while MR backings are on, gated only on
    //    `HostGo.activeInHierarchy`. A solid dark rectangle at the pre-fit rect.
    //  * THE MOD X's DEPTH STAMP (ModalCloseButton.BuildDepthStamp): a MeshRenderer quad under the
    //    host — again outside the canvas path entirely.
    //  * NESTED CANVASES: the adopted game canvases and the X's own draw/hit canvases
    //    (ModalCloseButton, `overrideSorting = true`) are independent render roots. Whether Unity
    //    propagates a disabled parent Canvas down to them is version/override dependent and not
    //    worth betting the fix on — so they are disabled explicitly and restored exactly.
    //
    // THE MECHANISM. One helper walks the host subtree AND every registered extra render root once,
    // switching off every enabled Canvas and every enabled Renderer it finds and RECORDING each one.
    // The reveal switches exactly the recorded set back on, in one pass, in one frame — so the first
    // visible frame shows the whole window (content, bar, X, masks, plate) at its final pose, and
    // nothing is ever visible anywhere else.
    //
    // WHY RECORD INSTEAD OF RE-ENABLING EVERYTHING: a converted subtree is full of components that
    // are off ON PURPOSE — a game-disabled sub-canvas (a closed option tab), a renderer some other
    // mod system parked. Recording only components whose `enabled` was TRUE at the moment we cleared
    // it makes the restore exact by construction: the restore set is, element for element, the set
    // the hide changed. Graphics are NOT touched at all (a uGUI Graphic is a CanvasRenderer user, not
    // a Renderer), so the deliberate background hide (ConvertedPanel.HiddenBackgrounds) and every
    // other Graphic-level decision keeps its own authority.
    //
    // WHY IT IS RE-APPLIED EVERY FRAME WHILE PENDING: the grab bar, the X and the MR plate are all
    // built AFTER Convert, some of them in a later tick step than CanvasConversion.Tick. The hide is
    // idempotent (an already-disabled component is skipped, never double-recorded), so it is simply
    // re-run from TickRevealGate (Update — catches everything ModalFallback.Tick built earlier in the
    // same frame) and from LateTick (LateUpdate, after EVERY Update and immediately before the frame
    // renders — the hard guarantee that covers MrBacking, which ticks after us).

    // Sweep scratch (single-threaded ticks; reused, no per-frame allocation).
    private static readonly List<Canvas> HideCanvasScratch = new(16);
    private static readonly List<Renderer> HideRendererScratch = new(16);

    /// <summary>Latch for <see cref="AssertNotInRenderPhase"/> — the hazard is structural, so one
    /// line per session is enough and the check can never spam a frame loop.</summary>
    private static bool s_renderPhaseViolationLogged;

    /// <summary>
    /// Nesting depth of the mod's own frame-phase ticks (Update / LateUpdate). Set by
    /// <see cref="BeginFramePhase"/>/<see cref="EndFramePhase"/> around every WorldUI tick step.
    ///
    /// WHY IT EXISTS — the ModBuild 23 log is a FALSE POSITIVE and this is the fix for the DETECTOR,
    /// not for the code it watches: <c>Camera.current</c> is documented as "the camera we are
    /// currently rendering with", but Unity does NOT clear it when the render loop ends — it keeps
    /// returning the last camera that rendered, and <c>stereoActiveEye</c> on an idle stereo camera
    /// reads Left. The round-6 detector therefore reported "INSIDE the render of
    /// 'GloomhavenVR.HeadCamera' (stereo eye Left)" for a call that provably came from
    /// <c>WorldUIModule.LateUpdate → CanvasConversion.LateTick → CompleteReveal</c> — a MonoBehaviour
    /// LateUpdate, which Unity never runs inside a camera render.
    ///
    /// Knowing we are inside our OWN Update/LateUpdate tick is positive proof that we are not inside
    /// a render, so the check becomes: a stale-or-live <c>Camera.current</c> only counts as a
    /// violation when the call did NOT come from a frame-phase tick. That is exactly the class the
    /// detector was built for — visibility flipped from a camera callback, which on MultiPass lands
    /// between the two eye passes and shows in one eye.
    /// </summary>
    private static int s_framePhaseDepth;

    /// <summary>Name of the frame phase currently running (log only).</summary>
    private static string s_framePhase = "none";

    /// <summary>Mark the start of a main-thread frame phase (Update/LateUpdate) — see
    /// <see cref="s_framePhaseDepth"/>. Paired with <see cref="EndFramePhase"/> in a finally.</summary>
    internal static void BeginFramePhase(string phase)
    {
        s_framePhaseDepth++;
        s_framePhase = phase;
    }

    /// <summary>End of a frame phase (see <see cref="BeginFramePhase"/>).</summary>
    internal static void EndFramePhase()
    {
        s_framePhaseDepth = Mathf.Max(0, s_framePhaseDepth - 1);
        if (s_framePhaseDepth == 0)
            s_framePhase = "none";
    }

    /// <summary>The frame phase a visibility flip happened in, for the reveal log — proof, per
    /// reveal, that it landed where both eyes see the same thing.</summary>
    internal static string CurrentFramePhase =>
        s_framePhaseDepth > 0
            ? $"{s_framePhase} (main thread, before both eye passes — one consistent frame)"
            : Camera.current != null
                ? $"UNKNOWN phase with Camera.current = '{Camera.current.name}' — possible ONE-EYE HAZARD"
                : "outside any mod tick (no camera rendering)";

    /// <summary>
    /// ROUND 6 (LEFT-EYE FLICKER) — THE INVARIANT, AND ITS DETECTOR.
    ///
    /// This is a MultiPass stereo rig: the head camera is rendered ONCE PER EYE, left first. A
    /// visibility write is only ever correct when it happens in the main-thread frame phase — every
    /// Update and LateUpdate runs before BOTH eye passes, so a window switched on there is on in
    /// both. A write made from inside a camera callback (<c>Camera.onPreCull</c>,
    /// <c>onPreRender</c>, <c>onPostRender</c>, <c>OnWillRenderObject</c>) lands BETWEEN the two eye
    /// renders and is therefore visible in ONE EYE for one frame — exactly the reported "brief
    /// flicker at the side of the LEFT eye".
    ///
    /// ROUND 7 — the round-6 version tested <c>Camera.current != null</c> alone and cried wolf on
    /// hardware, because Unity leaves that property pointing at the last camera that rendered (see
    /// <see cref="s_framePhaseDepth"/> for the full refutation). The sound test is "a camera is
    /// current AND we did not get here from one of the mod's own frame-phase ticks" — being inside
    /// our Update/LateUpdate is positive proof that no camera is rendering us.
    /// </summary>
    private static void AssertNotInRenderPhase(string what)
    {
        if (s_renderPhaseViolationLogged || s_framePhaseDepth > 0 || Camera.current == null)
            return;
        s_renderPhaseViolationLogged = true;
        VRLog.Warn("WorldUI", $"MODAL RENDER PHASE VIOLATION: a panel {what} ran OUTSIDE every mod frame " +
                              $"phase while camera '{Camera.current.name}' is current (stereo eye " +
                              $"{Camera.current.stereoActiveEye}). If that is a live render, it lands between " +
                              "the two MultiPass eye passes and shows in ONE EYE for a frame. Visibility must " +
                              "only ever change in Update/LateUpdate. Reported once, with the call site:\n" +
                              System.Environment.StackTrace);
    }

    /// <summary>
    /// Register a mod-drawn tree that belongs to <paramref name="panel"/> but lives OUTSIDE the
    /// host subtree (the <see cref="GrabbableModal"/> holder: the grab bar). It is
    /// hidden and revealed with the window from then on. If the panel is ALREADY render-hidden the
    /// new root is hidden immediately, in the same frame it was built — a late-built child must
    /// never get one visible frame of its own.
    /// </summary>
    internal static void AddRenderRoot(ConvertedPanel? panel, Transform? root)
    {
        if (panel == null || root == null || panel.ExtraRenderRoots.Contains(root))
            return;
        panel.ExtraRenderRoots.Add(root);
        if (panel.RenderHidden)
            HideTree(panel, root, out _, out _);
        // DELIBERATELY NOT the same immediate hide for OwnerRenderHidden (the surface-owned hide):
        // the restore set for that hide lives on the SURFACE, not on the panel, and is unreachable
        // from here — hiding the new root into panel.HiddenCanvases/HiddenRenderers would hand it
        // to the reveal gate's restore instead of the surface's, i.e. the gate would switch it back
        // on while the surface still wants it hidden. The surface re-asserts its hide every tick and
        // walks ExtraRenderRoots itself (CanvasConversion.ApplyOwnerRenderHide), so a late root is
        // caught on the next tick. That one frame is unreachable in practice today: only
        // GrabbableModal registers a render root, and the only OwnerRenderHidden panels are the
        // board-docked decision row and use bars, which are never grabbable windows.
    }

    /// <summary>
    /// Make everything that belongs to <paramref name="panel"/> invisible (false) or visible
    /// (true): the host canvas, every nested Canvas in the host subtree, every Renderer in it
    /// (the MR backing plate) AND every registered extra render root (the grab bar). See the file
    /// header for what each of those is and why the
    /// host canvas alone was not enough.
    ///
    /// HIDE is idempotent and meant to be re-applied while the reveal gate is pending — components
    /// disabled by an earlier pass are skipped, so re-running it only ever catches newly built
    /// children. REVEAL re-enables exactly the recorded set and clears it, so calling it twice is
    /// a no-op.
    /// </summary>
    /// <param name="canvasesChanged">How many Canvas components this call actually switched.</param>
    /// <param name="renderersChanged">How many Renderer components this call actually switched.</param>
    internal static void SetPanelRenderVisible(ConvertedPanel? panel, bool visible,
        out int canvasesChanged, out int renderersChanged)
    {
        canvasesChanged = 0;
        renderersChanged = 0;
        if (panel == null)
            return;
        AssertNotInRenderPhase(visible ? "reveal" : "hide");

        if (!visible)
        {
            panel.RenderHidden = true;
            if (panel.HostGo != null)
                HideTree(panel, panel.HostGo.transform, out canvasesChanged, out renderersChanged);
            for (int i = panel.ExtraRenderRoots.Count - 1; i >= 0; i--)
            {
                Transform root = panel.ExtraRenderRoots[i];
                if (root == null)
                {
                    panel.ExtraRenderRoots.RemoveAt(i); // holder destroyed with the grab
                    continue;
                }
                HideTree(panel, root, out int c, out int r);
                canvasesChanged += c;
                renderersChanged += r;
            }
            return;
        }

        // Reveal: exactly the recorded set, in one pass — everything becomes visible in the SAME
        // frame. Destroyed components (Unity fake-null) are simply skipped.
        //
        // ModBuild 395 — WITH ONE EXCEPTION, AND IT IS THE USER'S FLASH. An entry recorded off a
        // window that had not yet run Start() carries the PREFAB's default, not a decision; the
        // game makes the real decision moments later, into a canvas our hide already holds off,
        // and replaying the prefab default over it is what put the whole character screen on the
        // wall at once. For those entries only, the game's CURRENT verdict is asked instead.
        // Everything else is restored bit-for-bit as before. See RevealRestoreWithheld.
        s_revealWithheldCanvases = 0;
        s_revealPreStartNotOpen = 0;
        s_revealWithheldName = "none";
        for (int i = 0; i < panel.HiddenCanvases.Count; i++)
        {
            Canvas c = panel.HiddenCanvases[i];
            if (c == null || c.enabled)
                continue;
            bool preStart = i < panel.HiddenCanvasWasPreStart.Count
                            && panel.HiddenCanvasWasPreStart[i];
            if (preStart && RevealRestoreWithheld(panel, c))
            {
                s_revealWithheldCanvases++;
                s_revealWithheldName = c.gameObject.name;
                continue;
            }
            c.enabled = true;
            canvasesChanged++;
        }
        // FAIL TOWARD DRAWING, and this is the net that makes the exception safe to ship. If the
        // pass above ended with NOTHING switched on and something withheld, the panel would be
        // revealed empty — the one outcome "Es darf niemals leere Fenster geben" forbids outright.
        // Then the withhold is abandoned wholesale and the old exact restore runs instead.
        if (canvasesChanged == 0 && s_revealWithheldCanvases > 0)
        {
            for (int i = 0; i < panel.HiddenCanvases.Count; i++)
            {
                Canvas c = panel.HiddenCanvases[i];
                if (c == null || c.enabled)
                    continue;
                c.enabled = true;
                canvasesChanged++;
            }
            VRLog.Alert("WorldUI", "REVEAL RESTORE WITHHELD: the pre-Start withhold would have "
                + $"revealed '{panel.HostGo?.name ?? "?"}' with ZERO canvases switched on, so it "
                + $"was ABANDONED and all {canvasesChanged} recorded canvas(es) were restored the "
                + "old way. The window draws; the flash this rule prevents may be back on this one "
                + "open. A repeat of this line names a panel whose every recorded canvas belongs "
                + "to a not-open UIWindow, which means the rule's premise does not hold there.");
            s_revealWithheldCanvases = 0;
            s_revealWithheldName = "none";
        }
        // AND SAY SO AT A TIER THE SHIPPED DEFAULT PRINTS. The clause this rule appends to the two
        // MODAL REVEAL lines rides VRLog.Info/Warn, which ModBuild 331 moved to the DEBUG tier — so
        // on the user's hardware that clause is invisible, and reading it here would repeat exactly
        // the mistake that made the 392 round unreadable. This line is printed, and it is emitted
        // on the first three reveals of a session WHATEVER it found (so a zero is legible as a
        // zero) and afterwards only on a reveal that actually had something to decide (so it can
        // never become per-frame chatter).
        if (s_revealPreStartNotOpen > 0 || s_revealClauseLines < 3)
        {
            s_revealClauseLines++;
            // HW-VERIFY: the number that says whether the pre-Start restore exception reaches the
            // screen the user photographed. See RevealWithholdClause for how to read the pair.
            VRLog.Note("WorldUI", $"REVEAL RESTORE WITHHELD on '{panel.HostGo?.name ?? "?"}' at "
                + $"frame {Time.frameCount}: {RevealWithholdClause()} Restored "
                + $"{canvasesChanged} canvas(es) this reveal.");
        }
        panel.HiddenCanvases.Clear();
        panel.HiddenCanvasWasPreStart.Clear();
        for (int i = 0; i < panel.HiddenRenderers.Count; i++)
        {
            Renderer r = panel.HiddenRenderers[i];
            if (r == null || r.enabled)
                continue;
            r.enabled = true;
            renderersChanged++;
        }
        panel.HiddenRenderers.Clear();
        panel.RenderHidden = false;
    }

    /// <summary>Convenience overload for callers that do not report counts.</summary>
    internal static void SetPanelRenderVisible(ConvertedPanel? panel, bool visible) =>
        SetPanelRenderVisible(panel, visible, out _, out _);

    /// <summary>
    /// One render root's hide pass: every ENABLED Canvas and every ENABLED Renderer under
    /// <paramref name="root"/> (inclusive) is switched off and recorded on the panel. Inactive
    /// GameObjects are skipped deliberately — they render nothing, and if the game activates one
    /// while the gate is still pending the next re-apply pass catches it before that frame draws.
    /// </summary>
    private static void HideTree(ConvertedPanel panel, Transform root,
        out int canvasesChanged, out int renderersChanged)
    {
        canvasesChanged = 0;
        renderersChanged = 0;

        root.GetComponentsInChildren(false, HideCanvasScratch);
        for (int i = 0; i < HideCanvasScratch.Count; i++)
        {
            Canvas c = HideCanvasScratch[i];
            if (c == null || !c.enabled)
                continue; // already off (by us on an earlier pass, or by the game on purpose)
            c.enabled = false;
            panel.HiddenCanvases.Add(c);
            // ModBuild 395: record WHETHER THE `true` WE JUST READ WAS A DECISION. See
            // ConvertedPanel.HiddenCanvasWasPreStart for the full argument; the read itself is one
            // GetComponent on a GameObject we are already touching, paid once per canvas per hide
            // (a canvas already recorded is skipped by the `!c.enabled` line above), and it writes
            // nothing to the game.
            panel.HiddenCanvasWasPreStart.Add(IsOnPreStartWindow(c));
            canvasesChanged++;
        }

        root.GetComponentsInChildren(false, HideRendererScratch);
        for (int i = 0; i < HideRendererScratch.Count; i++)
        {
            Renderer r = HideRendererScratch[i];
            if (r == null || !r.enabled)
                continue;
            r.enabled = false;
            panel.HiddenRenderers.Add(r);
            renderersChanged++;
        }
    }

    // ---- the SURFACE-owned render hide (character focus, user ruling 2026-08-08) -------------
    //
    // Same mechanism as the reveal gate above, different OWNER — see
    // ConvertedPanel.OwnerRenderHidden for why the two may not share a flag or a restore set. A
    // surface calls Apply every tick it wants the panel invisible and Lift when it wants it back;
    // the recorded sets live on the SURFACE (passed in), so the gate's reveal can never clear them
    // and the surface's restore can never clear the gate's.
    //
    // WHAT THIS ADDS OVER THE `Canvas.enabled = false` THE SURFACES DID BEFORE (the bug, hardware
    // report ModBuild 84): "Der mixed-reality Hintergrund für die decision ist auch bei den anderen
    // Characteren noch zu sehen aber leer." The MR backing plate is NOT a Canvas — it is an opaque
    // plate MeshRenderer MrBacking parents under the host rect — so a canvas-only hide left an
    // empty dark rectangle floating where the row had been. Renderers are now switched off here,
    // and MrBacking additionally refuses to BUILD a plate for an OwnerRenderHidden panel.

    // Sweep scratch for the surface-owned hide. Deliberately its own pair rather than the reveal
    // gate's: the two hides are independent passes and must never be able to alias each other's
    // in-flight buffer if one ever ends up running inside the other's call stack.
    private static readonly List<Canvas> OwnerHideCanvasScratch = new(16);
    private static readonly List<Renderer> OwnerHideRendererScratch = new(16);

    /// <summary>
    /// Render-hide everything that belongs to <paramref name="panel"/> ON BEHALF OF ITS SURFACE:
    /// every enabled <see cref="Canvas"/> and every enabled <see cref="Renderer"/> in the host
    /// subtree AND in every registered <see cref="ConvertedPanel.ExtraRenderRoots"/> (the
    /// <see cref="GrabbableModal"/> grab bar lives outside the host, so a canvas-only hide would
    /// leave it floating). Nothing is deactivated and no game method is called — disabling a Canvas
    /// or Renderer COMPONENT runs no game code, which is the property the focus feature may never
    /// lose (the game's <c>ExtendedButton.OnDisable</c> raises <c>ActiveChanged(false)</c> and can
    /// clear the EventSystem selection; see the surfaces' own ApplyFocusHide docs).
    ///
    /// <para>IDEMPOTENT, and meant to be re-asserted every tick: a component already disabled is
    /// skipped and never double-recorded, so a plate or a pooled button's canvas built a frame
    /// later is caught on the next tick. EXACT RESTORE: only components whose <c>enabled</c> was
    /// TRUE at hide time are recorded, so <see cref="LiftOwnerRenderHide"/> can never switch on
    /// something that was deliberately off.</para>
    ///
    /// <para>Walks INACTIVE children too (unlike the reveal gate, which re-runs every frame while
    /// pending and can afford to skip them): a focus hide can last for minutes, and a GameObject
    /// the game activates during that time must not get one visible frame before the next tick's
    /// re-assert.</para>
    /// </summary>
    /// <param name="canvases">The surface's record of canvases IT disabled (appended to).</param>
    /// <param name="renderers">The surface's record of renderers IT disabled (appended to).</param>
    /// <param name="canvasesChanged">How many Canvas components this call actually switched off.</param>
    /// <param name="renderersChanged">How many Renderer components this call actually switched off.</param>
    internal static void ApplyOwnerRenderHide(ConvertedPanel? panel, List<Canvas> canvases,
        List<Renderer> renderers, out int canvasesChanged, out int renderersChanged)
    {
        canvasesChanged = 0;
        renderersChanged = 0;
        if (panel == null)
            return;
        AssertNotInRenderPhase("surface-owned hide");

        // Set FIRST, before any component is touched: MrBacking.TickPanels reads this flag to
        // refuse building a plate at all, and it ticks LATER in the same Update than the surfaces
        // (WorldUIModule.BuildTickSteps: DecisionDockSurface/UseBarsSurface -> CanvasConversion ->
        // MrBacking), so a hide applied here is always in force before the plate sweep runs. That
        // ordering is what makes the fix flash-free: on the very first hidden tick there is no
        // plate to switch off because none is ever created.
        panel.OwnerRenderHidden = true;

        if (panel.HostGo != null)
            HideOwnerTree(panel.HostGo.transform, canvases, renderers,
                ref canvasesChanged, ref renderersChanged);
        for (int i = panel.ExtraRenderRoots.Count - 1; i >= 0; i--)
        {
            Transform root = panel.ExtraRenderRoots[i];
            if (root == null)
            {
                panel.ExtraRenderRoots.RemoveAt(i); // holder destroyed with the grab
                continue;
            }
            HideOwnerTree(root, canvases, renderers, ref canvasesChanged, ref renderersChanged);
        }
    }

    /// <summary>
    /// Undo <see cref="ApplyOwnerRenderHide"/>: re-enable EXACTLY the recorded set and clear it.
    /// Safe (and required) even after the conversion was released — the components are held by
    /// reference and belong enabled wherever they now live, so a row restored into its 2D home is
    /// never stranded invisible. <paramref name="panel"/> may be null for that reason; the flag is
    /// cleared when it is not.
    /// </summary>
    internal static void LiftOwnerRenderHide(ConvertedPanel? panel, List<Canvas> canvases,
        List<Renderer> renderers)
    {
        AssertNotInRenderPhase("surface-owned reveal");
        for (int i = 0; i < canvases.Count; i++)
        {
            Canvas c = canvases[i];
            if (c == null || c.enabled)
                continue;
            c.enabled = true;
        }
        canvases.Clear();
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer r = renderers[i];
            if (r == null || r.enabled)
                continue;
            r.enabled = true;
        }
        renderers.Clear();
        if (panel != null)
            panel.OwnerRenderHidden = false;
    }

    /// <summary>One render root's surface-owned hide pass (see <see cref="ApplyOwnerRenderHide"/>).</summary>
    private static void HideOwnerTree(Transform root, List<Canvas> canvases, List<Renderer> renderers,
        ref int canvasesChanged, ref int renderersChanged)
    {
        root.GetComponentsInChildren(true, OwnerHideCanvasScratch);
        for (int i = 0; i < OwnerHideCanvasScratch.Count; i++)
        {
            Canvas c = OwnerHideCanvasScratch[i];
            if (c == null || !c.enabled)
                continue; // already off (by us on an earlier pass, or by the game on purpose)
            c.enabled = false;
            canvases.Add(c);
            canvasesChanged++;
        }
        OwnerHideCanvasScratch.Clear();

        root.GetComponentsInChildren(true, OwnerHideRendererScratch);
        for (int i = 0; i < OwnerHideRendererScratch.Count; i++)
        {
            Renderer r = OwnerHideRendererScratch[i];
            if (r == null || !r.enabled)
                continue;
            r.enabled = false;
            renderers.Add(r);
            renderersChanged++;
        }
        OwnerHideRendererScratch.Clear();
    }

    // ==========================================================================================
    // ModBuild 395 — THE PRE-START RESTORE EXCEPTION (user report 2026-09-03: "Leider ist es noch
    // da. … Weiterhin tritt es jetzt auch auf nachdem ich das erste mal auf 'Reisen' drücke. Auch
    // nachdem ich eine persönliche Quest gewählt habe blitzt es wieder auf.")
    // ==========================================================================================
    //
    // WHAT THE 392 LOG SAYS, and it is the reason this rule lives HERE and not in part 9d. The
    // flash the user photographed is measured by the mod's own instrument: SUB-VIEW SETTLE BURST
    // on 'GloomhavenVR.Panel_Modal_New Party display' reports "694 visible graphic(s) … PEAK 694
    // over a union of 1920x1080 px" where "the settled state of this window is 85-128". Its
    // visibility test is the fit's own TryGetVisibleHostRect, which rejects on cull AND on
    // colour times CanvasGroup-inherited alpha (CanvasConversion.3.Fit.cs:324-331) — so those 694
    // are graphics that really put pixels on the wall, not a census that would agree with a
    // closed window.
    //
    // AND THE SAME LOG SAYS WHEN. Every one of the six opens of that panel reads
    // "MODAL REVEAL: 'GloomhavenVR.Panel_Modal_New Party display' FORCED after ~605 ms
    // (deadline 600 ms; still waiting on first content fit) … unhid 16 canvas(es)". Sixteen, every
    // time, on a screen the ModBuild 388 adoption sweep found no nested canvas worth adopting in.
    // Those sixteen are the sub-views, and the reveal switches all sixteen on in one frame.
    //
    // WHY THE OLD EXACT RESTORE IS NOT EXACT HERE. The contract on HiddenCanvases is "only
    // components that were enabled at hide time are recorded, so the restore can never switch on
    // something that was deliberately off". That is true whenever the recorded `true` was a
    // DECISION. A panel is RenderHidden from Convert itself (part 1), i.e. from the frame the
    // screen is instantiated, and at that frame no sub-view UIWindow has run Start() yet — Start
    // is where UIWindow first drives itself to its starting visual state, and for a window that
    // hides by its canvas that write is `_canvas.enabled = false` in OnTransitionStarted
    // (decompiled UIWindow.cs:358-372, 566-580). So the hide records the PREFAB default for all
    // sixteen, the game decides "hidden" a few frames later into a canvas we are already holding
    // off (its write is a no-op it never repeats), and ~605 ms later the reveal replays the prefab
    // default over the game's verdict. That is the delete-character confirmation, the
    // mercenary-create screen and the un-populated "New Text" labels arriving together.
    //
    // WHY PART 9d's VEIL COULD NEVER HAVE CAUGHT IT, which is the same fact from the other side:
    // its candidates must be pre-Start AND inside a panel that is NOT RenderHidden
    // (CanvasConversion.9d.FlashVeil.cs:516/550). On this screen those two conditions are disjoint
    // in time — the panel is RenderHidden for the whole ~605 ms in which its sub-views are
    // pre-Start, and by the time it is revealed every one of them has started. The veil is not
    // wrong; the frames it was aimed at are not the frames the user sees.
    //
    // WHAT THIS RULE MAY AND MAY NOT DO. It never calls SetActive, Show or Hide, never writes the
    // game's alpha, state or event wiring, and never writes anything at all on the game side: it
    // DECLINES ONE WRITE OF OUR OWN, and only for an entry whose recorded value we can prove
    // carried no information. Everything else restores bit-for-bit, so a window that was mid
    // fade-out when the hide ran — HasGoneToStartingState already true — is untouched to the bit.

    /// <summary>Counter for the clause the two MODAL REVEAL lines append. Set by the reveal pass
    /// immediately before those lines are built, and read by nothing else.</summary>
    private static int s_revealWithheldCanvases;

    /// <summary>Name of the last canvas the reveal pass left off, for the same clause.</summary>
    private static string s_revealWithheldName = "none";

    /// <summary>Recorded canvases in THIS reveal that were the defect population — recorded while
    /// their window was pre-Start, and that window now reports started and not open. The
    /// difference between this and <see cref="s_revealWithheldCanvases"/> is exactly the set the
    /// `_disableCanvas` term refused, and it is the number the next hardware round reads.</summary>
    private static int s_revealPreStartNotOpen;

    /// <summary>How many printed REVEAL RESTORE WITHHELD lines this session has produced.</summary>
    private static int s_revealClauseLines;

    /// <summary>How many recorded canvases the last reveal left OFF because the game had decided
    /// against them while the hide held them (see the block above).</summary>
    internal static int RevealWithheldCanvases => s_revealWithheldCanvases;

    /// <summary>The last such canvas's GameObject name, or "none".</summary>
    internal static string RevealWithheldName => s_revealWithheldName;

    /// <summary>
    /// The clause both MODAL REVEAL lines append. UNCONDITIONAL — it states the zero as plainly as
    /// it states a count, because a clause that only appears when it acted cannot tell a reader
    /// whether the rule ran; that is the exact hole part 9d's 392 line fell into.
    /// </summary>
    private static string RevealWithholdClause() =>
        s_revealWithheldCanvases == 0
            ? "PRE-START RESTORE WITHHELD 0 canvas(es) of "
              + $"{s_revealPreStartNotOpen} that were recorded off a pre-Start UIWindow the game "
              + "has since decided against. A zero on BOTH numbers means this panel's hide never "
              + "recorded a prefab default and the rule has nothing to do here; a zero on the "
              + "first with the second non-zero means every one of them was refused by the "
              + "`_disableCanvas` term, i.e. those windows hide by their CanvasGroup alpha and "
              + "this rule is not the lever that reaches them."
            : $"PRE-START RESTORE WITHHELD {s_revealWithheldCanvases} of {s_revealPreStartNotOpen} "
              + "candidate canvas(es) (last "
              + $"'{s_revealWithheldName}'): each was recorded as 'enabled' while its own UIWindow "
              + "had not run Start() yet — a prefab default, not a decision — and that window now "
              + "reports itself started and NOT open. Switching them on is what painted the whole "
              + "character screen at once. If a sub-view is missing after a reveal, this count is "
              + "the first suspect and this is the line that names it.";

    /// <summary>
    /// Is <paramref name="c"/>'s OWN GameObject a <c>UIWindow</c> that has not run <c>Start()</c>
    /// yet? GetComponent and not GetComponentInParent: a canvas nested under a window is not that
    /// window's visibility lever, and [[containment-is-not-identity]] is the record of the two
    /// questions being different ones.
    /// </summary>
    private static bool IsOnPreStartWindow(Canvas c)
    {
        if (c == null)
            return false;
        var w = c.GetComponent<UIWindow>();
        return w != null && !w.HasGoneToStartingState;
    }

    /// <summary>
    /// Should the reveal leave this recorded canvas OFF? Only ever asked for an entry recorded
    /// while its window was pre-Start, i.e. one whose recorded <c>enabled == true</c> is a prefab
    /// default rather than a decision. Answers YES only when the GAME'S OWN current verdict is
    /// "this window is not shown", and every uncertainty answers NO — which is the direction that
    /// draws.
    /// </summary>
    private static bool RevealRestoreWithheld(ConvertedPanel panel, Canvas c)
    {
        if (c == null)
            return false;
        // NEVER the panel's own root. The host canvas and the conversion target carry the window
        // the player opened; withholding either is the empty window the 2026-08-02 ruling forbids
        // outright, and the reveal gate (part 4) is the only owner of that one's visibility.
        if (ReferenceEquals(c, panel.HostCanvas))
            return false;
        if (panel.Target != null && ReferenceEquals(c.transform, panel.Target))
            return false;
        if (panel.HostGo != null && ReferenceEquals(c.gameObject, panel.HostGo))
            return false;

        var w = c.GetComponent<UIWindow>();
        if (w == null)
            return false; // not a game window's lever — we have no verdict to defer to
        if (!w.HasGoneToStartingState)
            return false; // the game STILL has not decided; replay what we found and let it decide
        if (w.IsOpen)
            return false; // the game wants it shown — restore, and this is the common case

        // From here the entry IS the defect population: recorded at prefab state, and the game has
        // since decided the window is not shown. Count it BEFORE the last term, so the reveal line
        // can state how many the `_disableCanvas` term then refused — that number, and only that
        // number, says whether this rule reaches the screen the user photographed.
        s_revealPreStartNotOpen++;

        // THE LAST TERM, AND IT IS THE ONE THAT KEEPS THE RULE FROM LATCHING A WINDOW OFF FOREVER.
        // Withholding is only safe where the GAME will switch this canvas back on when it next
        // shows the window — i.e. where the canvas is the window's OWN visibility lever. That is
        // exactly UIWindow's serialized `_disableCanvas` (decompiled UIWindow.cs:119): with it set,
        // OnTransitionStarted writes `_canvas.enabled = true` on every Show and false on every
        // instant Hide (:566-580). With it CLEAR the window hides by its CanvasGroup alpha and
        // NEVER touches Canvas.enabled — so a canvas withheld there would stay dark through every
        // later Show, which is the "leeres Fenster" the standing ruling forbids and a far worse
        // defect than the flash. There is no public accessor, so the field is read once through a
        // cached FieldInfo; a read that fails for any reason answers "restore".
        return ReadsDisableCanvas(w);
    }

    /// <summary>Cached <c>UIWindow._disableCanvas</c> accessor — resolved once, never written.</summary>
    private static System.Reflection.FieldInfo? s_disableCanvasField;

    private static bool s_disableCanvasFieldResolved;

    /// <summary>Does this window hide itself by switching its own Canvas off? See
    /// <see cref="RevealRestoreWithheld"/> for why the answer bounds the whole rule.</summary>
    private static bool ReadsDisableCanvas(UIWindow w)
    {
        if (!s_disableCanvasFieldResolved)
        {
            s_disableCanvasFieldResolved = true;
            try
            {
                s_disableCanvasField = AccessTools.Field(typeof(UIWindow), "_disableCanvas");
            }
            catch (System.Exception)
            {
                s_disableCanvasField = null;
            }
            if (s_disableCanvasField == null)
                VRLog.Alert("WorldUI", "REVEAL RESTORE WITHHELD: UIWindow._disableCanvas could not "
                    + "be resolved, so the pre-Start restore exception is STOOD DOWN for the whole "
                    + "session and every recorded canvas is restored the old way. The window always "
                    + "draws; the flash it prevents is back. A rename of that field in a game patch "
                    + "is the expected cause.");
        }
        if (s_disableCanvasField == null)
            return false;
        try
        {
            return s_disableCanvasField.GetValue(w) is true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }
}
