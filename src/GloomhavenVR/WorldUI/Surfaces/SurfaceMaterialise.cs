using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// THE APPEAR AND THE VANISH FOR A <see cref="FloatingDecisionSurface"/>'S PANEL — the same dust,
/// the same shards and the same field a floated <see cref="ModalFallback"/> window gets, on a panel
/// <see cref="ModalFallback"/> can never see.
///
/// <para><b>THE REPORT (user, 2026-09-03, verbatim):</b> <i>"Weiterhin hat das Entscheidungsfenster
/// bei dem zB einem Character ein Item gegeben werden muss keine Animation. Auch dieses Fenster soll
/// die Animation zum auftauchen und Verschwinden haben."</i> The window is the map-side reward
/// popup, floated by <see cref="DistributeRewardSurface"/>.</para>
///
/// <para><b>IT ANIMATED ON NEITHER EDGE, AND THAT WAS STRUCTURAL RATHER THAN A REGRESSION.</b>
/// <c>WindowMaterialise.PlayIn</c> had exactly one call site — <c>CanvasConversion.CompleteReveal</c>
/// (CanvasConversion.4.Lifecycle.cs:955), behind <c>if (ModalFallback.IsFloated(panel))</c> — and
/// <c>PlayOut</c> exactly one, the modal release loop. A surface panel satisfies neither: these
/// popups carry no <c>UIWindow</c>, so they never enter <c>ModalFallback.Converted</c>, and
/// <c>PlayAppearOrDefer</c>'s own doc says so in as many words ("surfaces … never reach here").
/// The ModBuild 392 log agrees by census: every one of the 32 <c>WINDOW MATERIALISE PLAYOUT</c>
/// lines names a <c>Panel_Modal_*</c> host and not one names <c>Panel_DistributeReward</c>. The
/// 30 fps frame walk of the user's video agrees on both edges: the popup is absent at t=20.87 s and
/// fully drawn at t=20.90 s, and fully drawn at t=23.87 s and gone at t=23.90 s — one frame each
/// way. (The dust visible beside it in a sparse frame is the NEIGHBOURING event window's vanish,
/// which is playing over the same seconds.)</para>
///
/// <para><b>WHAT IS REUSED, UNCHANGED.</b> <c>WindowMaterialise.PlayIn</c>,
/// <c>WindowMaterialise.PlayOut</c>, <c>WindowMaterialise.Cancel</c>, the runner, the field and the
/// debris — every line of the effect itself. Both entry points already take a
/// <c>ConvertedPanel</c>, which is precisely what a surface owns, so nothing about the effect had
/// to learn what a surface is. This class is an OWNER, in the same sense
/// <see cref="SurfaceGrabBar"/> is the surface family's owner of <c>PanelGrabHandle</c>.</para>
///
/// <para><b>THE THREE THINGS THAT WERE STRUCTURALLY UNAVAILABLE, each named rather than
/// half-taken.</b>
/// <list type="number">
/// <item><b>The reveal-edge call site.</b> <c>CanvasConversion.4.Lifecycle.cs:955-956</c> is the
/// frame the eye first sees a panel, and it is another lane's file this round. Rather than edit it,
/// the appear is driven from <see cref="Pump"/> — a <c>[DefaultExecutionOrder(20000)]</c>
/// MonoBehaviour whose <c>LateUpdate</c> Unity therefore runs AFTER <c>WorldUIModule</c>'s (default
/// order 0), i.e. after <c>CanvasConversion.LateTick → CompleteReveal</c> has flipped
/// <c>ConvertedPanel.RenderHidden</c> in the same frame phase. So the appear still starts in the
/// LateUpdate of the reveal, before either eye pass renders, which is the whole reason the modal
/// call site sits where it does. WHAT THE INTEGRATOR COULD DO INSTEAD, and it is strictly better:
/// widen that line to <c>if (ModalFallback.IsFloated(panel)) … else SurfaceMaterialise.OnRevealed
/// (panel)</c> and delete the pump.</item>
/// <item><b><c>ModalFallback.HasNothingToDissolve</c> and <c>AppearStillOwed</c></b>
/// (ModalFallback.9.Spawn.cs:3142 / :3218). Both walk <c>ModalFallback.Converted</c> and return
/// "no" for any panel that list does not carry — which is every surface panel. They are not merely
/// inaccessible, they are keyed on a record these panels never get. The two TERMS behind them are
/// re-derived here, on this family's own stored verdict.</item>
/// <item><b><c>ModalFallback.DrawsAnythingScriptSide</c></b> (ModalFallback.9.Spawn.cs:2925) is
/// <c>private static</c>. <see cref="DrawsAnythingScriptSide"/> below is a faithful copy, and that
/// is the one duplication in this lane. REQUESTED CHANGE: make that method and
/// <c>GroupChainAlpha</c> beside it <c>internal</c>, and this copy is deleted — two copies of one
/// rule is exactly how two verdicts drift, which <c>BarSizeSettle</c>'s own doc says.</item>
/// </list></para>
///
/// <para><b>THE OWED-APPEAR RULE (ModBuild 374) HOLDS HERE FROM THE FIRST BUILD.</b> User, same
/// day: <i>"kam … die Animation das ein neues Fenster spawnt aber das 'Fenster' ist sofort wieder
/// verschwunden"</i> — dust for a window that was not there. A surface panel that reaches its
/// reveal edge with nothing drawable under it does NOT play its appear: the appear is OWED and
/// spent on the first frame the panel is measured drawing something. Same rule, same instrument
/// shape, same wording in the log, and the verdict is stored on ONE field
/// (<see cref="Entry.Owed"/>) so nothing can disagree with it.</para>
///
/// <para><b>AND ModBuild 378's HALF GOES WITH IT: NO CONTENT, NO DUST AND NO ROD.</b> User:
/// <i>"Wenn kein Fenster inhalt hat soll neben der Animation auch kein Greifbalken erscheinen."</i>
/// The withhold is taken from the SAME stored verdict, in the same statement
/// (<see cref="SurfaceGrabBar.SetWithheld"/>), and released in the same statement — so for this
/// family, as for the modal one, the dust and the rod cannot disagree about whether a window has
/// content. Before this build the surface rod arrived at the reveal edge unconditionally, because
/// the surface family had no content verdict at all.</para>
///
/// <para><b>THE VANISH NEEDED A CLOSE EDGE, AND THE ONE THE MODALS USE DOES NOT EXIST HERE.</b>
/// <c>WindowMaterialise</c> hears a modal close through <c>VREvents.WindowVisibility</c>, published
/// from the Harmony postfix on <c>UIWindow.EvaluateAndTransitionToVisualState</c>. These popups have
/// no <c>UIWindow</c>. Their close is <c>UIDistributePointsPopup.Hide()</c> and
/// <c>UIAbilityCardPicker.Hide()</c>, and BOTH end in <c>window.SetActive(false)</c> — a hard cut,
/// no tween — which is also exactly why the surface's own level trigger (<c>ShownPanel()</c>, whose
/// terms are <c>window.activeSelf</c>) can only ever fire AFTER the content is already gone. Starting
/// a dissolve there would be the ModBuild 387 trap in a new place: every counter true, nothing on the
/// screen, and a 0.9 s delay bought with it. <see cref="SurfaceCloseEdge"/> is therefore the missing
/// publisher — the same shape as the UIWindow postfix, one method earlier — and this class starts the
/// vanish from it, while the content is still active and still drawing.</para>
///
/// <para><b>WHAT THE PLAYER WILL ACTUALLY SEE ON THE VANISH, MEASURED AND NOT ASSUMED.</b> The
/// shards are torn from the elements at <c>PlayOut</c> entry — i.e. inside the prefix, while every
/// element is still <c>activeInHierarchy</c>, which is the term <c>BuildEmissionTable</c>
/// (WindowMaterialiseDebris.cs:641) skips on — and the cloud is parented to the MOD-OWNED HOST
/// (WindowMaterialiseDebris.cs:480), not to the game's window. So the cloud is built and flies for
/// the full <c>VanishSeconds</c>. The ELEMENT dissolve is a different matter: one frame later the
/// game's own <c>SetActive(false)</c> takes the whole subtree, and an inactive
/// <c>CanvasRenderer</c> draws nothing whatever alpha is written on it. The honest description is
/// therefore: <b>the window's own pixels leave on the game's own frame, exactly as fast as they do
/// today, and a few hundred shards tear out of where it stood and blow away over ~0.9 s.</b> That is
/// the visible half of the effect and it is the half the request describes ("in Partikel von Wind
/// verweht"); it is not the full element-by-element wipe a modal gets.</para>
///
/// <para><b>WHY THE MODAL TRICK FOR THAT IS REFUSED HERE RATHER THAN MISSED.</b>
/// <c>WindowVisibilityHold.Assert</c> (WindowMaterialiseVisibility.cs:234) really does
/// <c>SetActive(true)</c> on a window the game deactivated, and hands it back through the game's own
/// rule at the end — so the machinery exists. It is not used here, for two independent reasons and
/// either alone is enough. (1) It captures <c>UIWindow</c> components only
/// (WindowMaterialiseVisibility.cs:156), so on these panels it captures zero records and is inert.
/// (2) Holding THIS window active would be actively dangerous: <c>UIDistributePointsPopup.Hide</c>
/// runs <c>controllerArea.Destroy()</c> beside the deactivate, the ModBuild 392 log records this
/// window as carrying the <c>ControllerInputAreaLocal</c> itself, and this is the popup whose
/// absence produced four deadlock reports while
/// <c>MapChoreographer.WaitDistributionEnds</c> waits and the map is input-locked. A decoration does
/// not get to hold the game's focus-area teardown open for 0.9 s. The house rule says the same thing
/// shorter: presentation code writes no game state.</para>
///
/// <para><b>NEITHER EDGE CAN DELAY THE DECISION OR STRAND THE FLOW.</b>
/// <list type="bullet">
/// <item>The APPEAR is called AFTER the reveal, on a panel that is already visible, already
/// raycastable and already a laser target — <c>PlayIn</c> is documented as unable to fail in a way
/// the caller handles, it writes only <c>CanvasRenderer</c> alphas, and it gates nothing. No term of
/// any <c>WantConverted</c>, no <c>Show</c>, no <c>Hide</c>, no <c>SetActive</c>, no synthesised
/// click.</item>
/// <item>The VANISH defers only <c>CanvasConversion.Release</c> — the call that re-parents the
/// GAME's window back to its 2D home — and only for a window the game has ALREADY closed. The
/// promise the campaign waits on (<c>UIDistributeReward.Distribute → OnConfirmClick →
/// promise.Resolve</c>) is resolved by the popup's own confirm button, which has already been
/// pressed by the time <c>Hide()</c> runs; nothing downstream reads the mod's parenting.</item>
/// <item>The deferral is bounded by <c>WindowMaterialise.HardCeilingSeconds</c> = 2.0 s in code, and
/// it is not even a deferral of the surface: <see cref="HandOffRelease"/> hands the release over
/// while the surface sets <c>Panel = null</c> in the same tick, so the surface is free to convert
/// again immediately. If the same target comes back inside the window,
/// <see cref="FinishNow"/> ends the dissolve SYNCHRONOUSLY (<c>WindowMaterialise.Cancel</c>, whose
/// contract is that a cancelled vanish still runs its callback) before the re-convert.</item>
/// <item>Every pre-existing release path still releases with no animation at all: a destroyed
/// target, a scene unload, the A/X flat-screen chord, <c>ReleaseCurrentPanel</c>'s popup switch,
/// <c>Shutdown</c> and <c>WindowMaterialise.CancelAll</c>. The vanish is ADDITIVE — it exists only
/// on the one edge that publishes it, and <see cref="HandOffRelease"/> returns false everywhere
/// else, which makes the old code path the default rather than the fallback.</item>
/// </list></para>
///
/// <para><b>THE ModBuild 395 PRE-START POPULATION, ON BOTH EDGES, CHECKED RATHER THAN INHERITED.</b>
/// <c>WindowMaterialiseRunner.CollectElements</c> snapshots every element's
/// <c>CanvasRenderer.GetAlpha()</c> ONCE at construction and restores exactly those numbers at the
/// end, so a runner built over a not-yet-populated window would hold and then re-assert prefab
/// values. Neither edge here can be built at that moment, and both for a stated reason rather than
/// by luck. The APPEAR is gated on <see cref="DrawsAnythingScriptSide"/> being TRUE — active
/// subtree, enabled Graphics above the fit's alpha floor, a live CanvasGroup chain and, since this
/// build, no disabled Canvas over them — which is a strictly later instant than the reveal edge the
/// other lane's flash came from; a panel that has not been populated is exactly the panel this class
/// OWES its appear to and does not animate. The VANISH is built inside the game's own
/// <c>Hide()</c>, which is by construction long after <c>Start()</c>. And the alpha channel itself
/// is the quiet one: <c>CanvasRenderer.SetAlpha</c> is a float uGUI never writes
/// (<c>WindowMaterialise</c>'s own class doc says so, and it is why that channel was chosen), so
/// the snapshot's content is 1.0 for anything the mod has not driven.</para>
///
/// <para><b>MULTIPLAYER: nothing.</b> Confirmed rather than assumed — every write below is a
/// <c>CanvasRenderer.SetAlpha</c> on the local client, a mod-owned mesh, and one
/// <c>MeshRenderer.enabled</c> on the local rod. No <c>NetProtocol</c> field, no record, no wire
/// coverage, no <c>ModBuild</c> implication; a peer sees their own animation on their own client at
/// their own configured duration, exactly as <c>WindowMaterialise</c>'s own class doc states for the
/// modal windows. There is also no per-sub-feature dial: the effect rides the ONE existing
/// <c>WindowMaterialise.Enabled</c> / duration configuration, which is what "it syncs fully or not at
/// all" means for a purely local decoration.</para>
/// </summary>
internal static class SurfaceMaterialise
{
    private const string Scope = "WorldUI";

    /// <summary>
    /// One live conversion of one <see cref="FloatingDecisionSurface"/>. At most three exist at
    /// once (one per subclass), which is why a list and a linear walk are the right shape.
    /// </summary>
    private sealed class Entry
    {
        internal ConvertedPanel Panel = null!;

        internal string Name = string.Empty;

        internal SurfaceGrabBar? Bar;

        /// <summary>The reveal edge has been seen and the appear decision taken.</summary>
        internal bool Decided;

        /// <summary>The appear has run. Nothing walks this panel's subtree after this is true.</summary>
        internal bool Played;

        /// <summary>ModBuild 374: the reveal edge found nothing drawable, so the appear is owed.</summary>
        internal bool Owed;

        internal float OwedSince;
    }

    private static readonly List<Entry> Armed = new(3);

    /// <summary>
    /// A vanish in flight. Holds the release the surface hands over — see
    /// <see cref="HandOffRelease"/> for why the two arrive in either order.
    /// </summary>
    private sealed class Vanish
    {
        internal ConvertedPanel Panel = null!;

        /// <summary>The surface's own release, once it has handed it over. Null until then.</summary>
        internal Action? Release;

        /// <summary>The effect has finished; only the release is still outstanding.</summary>
        internal bool EffectDone;
    }

    private static readonly List<Vanish> Vanishing = new(2);

    private static Pump? _pump;

    // ---- arming ---------------------------------------------------------------------------------

    /// <summary>
    /// A surface has floated and PLACED a panel: watch it for its reveal edge.
    ///
    /// <para>Called from <see cref="FloatingDecisionSurface.Place"/>, in the same statement the grab
    /// bar is built, so the two are armed off one event and the ModBuild 378 pairing is a property
    /// of the call site rather than of two schedules.</para>
    /// </summary>
    internal static void Arm(ConvertedPanel? panel, string name, SurfaceGrabBar? bar)
    {
        if (panel == null || !panel.IsAlive)
            return;
        Disarm(panel);
        Armed.Add(new Entry { Panel = panel, Name = name, Bar = bar });
        EnsurePump();
    }

    /// <summary>Stop watching a panel. Idempotent, and safe on a panel that was never armed.</summary>
    internal static void Disarm(ConvertedPanel? panel)
    {
        if (panel == null)
            return;
        for (int i = Armed.Count - 1; i >= 0; i--)
            if (ReferenceEquals(Armed[i].Panel, panel))
                Armed.RemoveAt(i);
    }

    // ---- the appear -----------------------------------------------------------------------------

    /// <summary>
    /// THE REVEAL-EDGE PASS. Runs in <c>LateUpdate</c> at execution order 20000, i.e. after
    /// <c>WorldUIModule.LateUpdate → CanvasConversion.LateTick → CompleteReveal</c> has flipped
    /// <see cref="ConvertedPanel.RenderHidden"/> for any panel that revealed this frame — so the
    /// appear starts in the same frame phase the modal one does, before either eye pass renders.
    ///
    /// <para>ALLOCATION-FREE AND CHEAP AFTER THE FIRST FRAME: an entry whose appear has played is
    /// one bool test, and the subtree walk only ever runs for a panel that is on the screen and has
    /// not yet been measured drawing.</para>
    /// </summary>
    private static void LatePass()
    {
        float now = Time.unscaledTime;
        for (int i = Armed.Count - 1; i >= 0; i--)
        {
            Entry e = Armed[i];
            if (e.Panel == null || !e.Panel.IsAlive || e.Panel.HostGo == null)
            {
                Armed.RemoveAt(i);
                continue;
            }
            if (e.Played)
                continue;
            // NOT YET REVEALED. The reveal gate holds a fresh conversion render-hidden until its fit
            // has measured and its pose is still (up to CanvasConversion's own 0.6 s bound), and an
            // appear played over a hidden panel is an appear the player never sees.
            if (e.Panel.RenderHidden || e.Panel.OwnerRenderHidden)
                continue;

            bool draws = DrawsAnythingScriptSide(e.Panel.Target);
            if (!e.Decided)
            {
                e.Decided = true;
                if (draws)
                {
                    e.Bar?.SetWithheld(false);
                    WindowMaterialise.PlayIn(e.Panel);
                    e.Played = true;
                    NoteAppear(e, "PLAYED", now);
                }
                else
                {
                    e.Owed = true;
                    e.OwedSince = now;
                    // ModBuild 378 — THE ROD GOES WITH THE DUST, ON THIS FRAME AND FROM THIS VERDICT.
                    // This is a LateUpdate and the rod's own follow tick is the next frame's Update,
                    // so asking here is what keeps the handle from being drawn for the frame between.
                    e.Bar?.SetWithheld(true);
                    NoteAppear(e, "HELD", now);
                }
                continue;
            }
            if (e.Owed && draws)
            {
                e.Owed = false;
                e.Played = true;
                e.Bar?.SetWithheld(false);
                WindowMaterialise.PlayIn(e.Panel);
                NoteAppear(e, "RELEASED", now);
            }
        }
    }

    // ---- the vanish -----------------------------------------------------------------------------

    /// <summary>
    /// THE GAME IS CLOSING ONE OF THESE POPUPS RIGHT NOW — published by
    /// <see cref="SurfaceCloseEdge"/> from a prefix on the game's own <c>Hide()</c>, i.e. BEFORE the
    /// <c>window.SetActive(false)</c> in the same method.
    ///
    /// <para>That instant is the only one at which the effect has anything to work with, and the
    /// reason is mechanical: <c>BuildEmissionTable</c> skips every element that is not
    /// <c>activeInHierarchy</c>, so a shard cloud built one statement later is empty. This method
    /// returns void and can refuse: the game's close is never gated on it.</para>
    /// </summary>
    /// <param name="window">The popup's own <c>window</c> GameObject, as the game holds it.</param>
    internal static void OnGameClosing(GameObject? window)
    {
        if (window == null)
            return;
        Transform wt = window.transform;
        for (int i = 0; i < Armed.Count; i++)
        {
            Entry e = Armed[i];
            RectTransform? target = e.Panel?.Target;
            if (target == null)
                continue;
            // Containment, not identity: DistributeRewardSurface.FloatRoot converts through the
            // NEAREST COMMON ANCESTOR of the popup window and its confirm button, which is not
            // required to be the window's own transform.
            if (!ReferenceEquals(wt, target) && !wt.IsChildOf(target))
                continue;
            BeginVanish(e, window);
            return;
        }
    }

    private static void BeginVanish(Entry e, GameObject window)
    {
        ConvertedPanel panel = e.Panel;
        // The rod leaves with the window's pixels, on this frame. The surface's own DestroyGrab is
        // one Update tick later, and by then the whole subtree is inactive — a rod standing in the
        // room with no window on it is the artefact of .planning/debug/leeres_fenster2.jpg.
        e.Bar?.SetWithheld(true);

        // THE ENTRY STAYS ARMED AND ITS APPEAR VERDICT IS RESET, which does two jobs with one line.
        // (a) DistributeRewardSurface's distribution HOLD keeps a float alive across a popup that
        // the game hid (WantConverted's `|| _holdArmed` term), so the very same conversion can be
        // shown again — and a panel that comes back deserves its appear exactly as much as one that
        // arrives. (b) It makes a SECOND close edge on an already-closed window fall into the
        // "never measured drawing" term below instead of starting a dissolve over nothing, which is
        // the ModBuild 387 shape in miniature.
        bool decided = e.Decided;
        bool owed = e.Owed;
        e.Decided = false;
        e.Played = false;
        e.Owed = false;

        // ModBuild 374's rule, on the OTHER edge and from the same stored verdict: a float that was
        // never measured drawing anything has nothing for a dissolve to take away, and a float that
        // is not on the screen has nothing either. These are the two terms
        // ModalFallback.HasNothingToDissolve carries, re-derived for a family that list cannot see —
        // and they are LIFETIME PROPERTIES rather than a measurement taken here, for the reason
        // stated over there: a drawability test at the close edge is confounded by the closing
        // itself and would delete the vanish outright.
        string? nothing = null;
        if (owed || !decided)
            nothing = "it was never once measured drawing anything in its whole life as a float — "
                      + "its materialise APPEAR was still OWED, so there is nothing on the screen "
                      + "for a dissolve to take away";
        else if (!window.activeInHierarchy)
            // THE ONE MEASUREMENT, AND IT IS NOT THE CONFOUNDED ONE. ModalFallback.HasNothingToDissolve
            // refuses to measure drawability at a close edge because the mod's OWN close-edge hold
            // disables the window's CanvasGroup, so the reading would call every ordinary closing
            // window dark. No such hold exists here — WindowVisibilityHold captures UIWindow records
            // only and finds none on these panels — so this bit is the GAME's alone, read one
            // statement before the game itself flips it. It is also exactly the term
            // WindowMaterialiseDebris.BuildEmissionTable skips on, i.e. the question "will there be
            // a single shard" asked directly rather than by proxy.
            nothing = "the window GameObject was ALREADY inactive when its own Hide() ran, so every "
                      + "element under it is out of the emission table and the dissolve would tear "
                      + "no shard at all — only delay the release";
        else if (panel.RenderHidden || panel.OwnerRenderHidden)
            nothing = "the panel is render-hidden — every Canvas and Renderer of this float is "
                      + "already off, so a dissolve would animate alphas nobody renders and only "
                      + "delay the release";
        if (nothing != null)
        {
            NoteVanish(e.Name, "SKIPPED", nothing);
            return;
        }

        var v = new Vanish { Panel = panel };
        Vanishing.Add(v);
        NoteVanish(e.Name, "STARTED",
                   "the shards are torn from the elements NOW, while the game's own Hide() has not "
                   + "yet reached its window.SetActive(false), and they fly from a cloud parented to "
                   + "the mod-owned host — so they outlive the subtree going inactive one statement "
                   + "later. The window's own pixels still leave on the game's frame");
        WindowMaterialise.PlayOut(panel, () => OnEffectDone(v));
    }

    private static void OnEffectDone(Vanish v)
    {
        v.EffectDone = true;
        Action? release = v.Release;
        v.Release = null;
        Vanishing.Remove(v);
        // The surface may not have reached its own release tick yet, in which case there is nothing
        // to run and HandOffRelease will find no record and release inline. Either order is total.
        release?.Invoke();
    }

    /// <summary>
    /// THE SURFACE HAS REACHED ITS RELEASE. If a vanish for this panel is in flight, hand the
    /// release over to it and return true; otherwise return false and the caller releases exactly as
    /// it always did.
    ///
    /// <para><b>THE TWO EVENTS ARRIVE IN EITHER ORDER, AND BOTH ORDERS ARE TOTAL.</b> The close edge
    /// is a Harmony prefix inside the game's call stack; the surface's release is its next Update
    /// tick, which may be the same frame or a later one, and the effect may itself have finished
    /// first (a zero-length dial, the effect switched off, a dead panel — <c>PlayOut</c> runs its
    /// callback synchronously on all of those). So: no record ⇒ release now; a record whose effect
    /// has already finished ⇒ release now; a live record ⇒ it runs the release at the end of the
    /// dissolve. There is no path on which the release is skipped, and none on which it runs
    /// twice.</para>
    /// </summary>
    internal static bool HandOffRelease(ConvertedPanel? panel, Action release)
    {
        if (release == null)
            throw new ArgumentNullException(nameof(release),
                "the release must run; a null one would strand the game's window under a mod host.");
        if (panel == null)
            return false;
        for (int i = 0; i < Vanishing.Count; i++)
        {
            Vanish v = Vanishing[i];
            if (!ReferenceEquals(v.Panel, panel))
                continue;
            if (v.EffectDone)
                return false;
            v.Release = release;
            return true;
        }
        return false;
    }

    /// <summary>Is a dissolve still holding this panel's release?</summary>
    internal static bool IsVanishing(ConvertedPanel? panel)
    {
        if (panel == null)
            return false;
        for (int i = 0; i < Vanishing.Count; i++)
            if (ReferenceEquals(Vanishing[i].Panel, panel))
                return true;
        return false;
    }

    /// <summary>
    /// END EVERY DISSOLVE OF THIS FAMILY NOW, RELEASE INCLUDED. Called before a surface converts
    /// again and from a shutdown: a decoration may never be the reason a decision window waits for
    /// its slot, and a re-convert of a target still parented under a dying host is the hazard
    /// <c>WindowMaterialise.IsVanishing</c> exists for on the modal side.
    ///
    /// <para><c>WindowMaterialise.Cancel</c> is the right instrument for this and not a workaround:
    /// its documented contract is that a cancelled VANISH still runs its callback, precisely so that
    /// cancelling the animation can never cancel the release.</para>
    /// </summary>
    internal static void FinishNow(ConvertedPanel? panel, string reason)
    {
        if (panel == null)
            return;
        for (int i = Vanishing.Count - 1; i >= 0; i--)
        {
            Vanish v = Vanishing[i];
            if (!ReferenceEquals(v.Panel, panel))
                continue;
            Vanishing.RemoveAt(i);
            // Cancel ends in Finish → the runner's onDone → OnEffectDone, which runs the handed-over
            // release. Removing the record first keeps that path from walking a list being mutated.
            v.EffectDone = true;
            Action? release = v.Release;
            v.Release = null;
            WindowMaterialise.Cancel(v.Panel, reason);
            release?.Invoke();
        }
    }

    // ---- the drawability verdict ------------------------------------------------------------------

    private static readonly List<Graphic> WalkGraphics = new(64);

    private static readonly List<Renderer> WalkRenderers = new(16);

    /// <summary>
    /// DOES THIS SUBTREE DRAW ANYTHING, JUDGED SCRIPT-SIDE? A faithful copy of
    /// <c>ModalFallback.DrawsAnythingScriptSide</c> (ModalFallback.9.Spawn.cs:2925), which is
    /// <c>private static</c> and therefore unreachable from here.
    ///
    /// <para>THE SCRIPT-SIDE TEST IS THE CORRECT ONE AT THIS INSTANT, not merely the cheap one, and
    /// the argument is that method's: the strict verdict reads
    /// <c>CanvasRenderer.GetInheritedAlpha</c> and <c>cull</c>, which uGUI maintains by SERVICING a
    /// canvas — and this runs in the very LateUpdate that just enabled those canvases, so the strict
    /// test would read stale zeros for a panel that is about to draw perfectly well. Every term below
    /// is written by the game from its own Update and is readable whether or not anything is
    /// rendering.</para>
    ///
    /// <para>The asymmetry of the two errors is also the right way round here: a wrong "it draws"
    /// costs one appear over a panel that then turns out empty, and a wrong "it is dark" costs the
    /// appear being deferred a few frames and then played on first paint — which is what was asked
    /// for anyway. Neither can strand a window, and neither touches the float.</para>
    /// </summary>
    private static bool DrawsAnythingScriptSide(Transform? root)
    {
        if (root == null || !root.gameObject.activeInHierarchy)
            return false;
        WalkGraphics.Clear();
        root.GetComponentsInChildren(includeInactive: false, WalkGraphics);
        for (int i = 0; i < WalkGraphics.Count; i++)
        {
            Graphic g = WalkGraphics[i];
            if (g == null || !g.enabled)
                continue;
            if (g.color.a < CanvasConversion.FitMinAlpha)
                continue;
            RectTransform? gr = g.rectTransform;
            if (gr == null)
                continue;
            Rect r = gr.rect;
            if (r.width < 0.5f || r.height < 0.5f)
                continue;
            if (g.gameObject.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal))
                continue;
            if (GroupChainAlpha(gr, root) < CanvasConversion.FitMinAlpha)
                continue;
            // ModBuild 395's WITHHOLD IS A TERM HERE, and this is the one place this copy
            // deliberately goes FURTHER than the method it copies. That build stopped the reveal
            // from re-enabling a Canvas it had recorded as `enabled` off a pre-Start window the game
            // has since decided against, so a canvas really can stay switched off underneath a
            // revealed panel — and every other term above is script-side and cannot see it. An
            // appear played over content no canvas is drawing is exactly "dust for a window that was
            // not there", i.e. the ModBuild 374 complaint arriving by a new route. The walk stops at
            // the conversion target and only runs for a graphic that has already passed everything
            // else, so a drawing panel pays for one short chain.
            if (CanvasChainDisabled(gr, root))
                continue;
            return true;
        }
        // A 3D preview (the item's own model, a portrait render) carries no Graphic at all, and
        // `enabled` on a Renderer is game state rather than canvas state.
        WalkRenderers.Clear();
        root.GetComponentsInChildren(includeInactive: false, WalkRenderers);
        for (int i = 0; i < WalkRenderers.Count; i++)
        {
            Renderer rend = WalkRenderers[i];
            if (rend != null && rend.enabled
                && !rend.gameObject.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Is any <c>Canvas</c> between this graphic and the conversion target switched OFF? A disabled
    /// Canvas stops its whole subtree rendering, and uGUI leaves every script-side term this class
    /// reads exactly as it was — so without this a withheld canvas reads as a drawing panel.
    ///
    /// <para>Read as a plain <c>Canvas.enabled</c> and not through <c>Graphic.canvas</c>: that
    /// property is maintained by canvas servicing, which is precisely what has not happened yet in
    /// the LateUpdate this runs in.</para>
    /// </summary>
    private static bool CanvasChainDisabled(Transform from, Transform stop)
    {
        Transform? t = from;
        while (t != null)
        {
            var c = t.GetComponent<Canvas>();
            if (c != null && !c.enabled)
                return true;
            if (ReferenceEquals(t, stop))
                break;
            t = t.parent;
        }
        return false;
    }

    /// <summary>Product of every <c>CanvasGroup.alpha</c> from <paramref name="from"/> up to and
    /// including <paramref name="stop"/>, honouring <c>ignoreParentGroups</c> exactly as uGUI does.
    /// The script-side equivalent of the inherited alpha a serviced CanvasRenderer would report.
    /// </summary>
    private static float GroupChainAlpha(Transform from, Transform stop)
    {
        float alpha = 1f;
        Transform? t = from;
        while (t != null)
        {
            var group = t.GetComponent<CanvasGroup>();
            if (group != null)
            {
                alpha *= group.alpha;
                if (alpha < CanvasConversion.FitMinAlpha)
                    return alpha;
                if (group.ignoreParentGroups)
                    return alpha;
            }
            if (ReferenceEquals(t, stop))
                break;
            t = t.parent;
        }
        return alpha;
    }

    // ---- WHAT ACTUALLY HAPPENED, PER SURFACE AND PER EDGE ------------------------------------------
    //
    // ModBuild 394 was spent on an instrument whose every number was true while the feature was
    // invisible, so these two lines report the OUTCOME and nothing else: not "the effect was wired
    // up", not "the call was made", but which of the named outcomes the panel got, and — on the
    // vanish — the reason when it got none. Read them like this:
    //
    //   * a surface float with NO appear line at all      => the reveal edge was never reached for
    //     it (read the MODAL REVEAL / conversion lines for that host, not this feature);
    //   * PLAYED                                          => the ordinary case; dust on arrival;
    //   * HELD then RELEASED                              => the ModBuild 374 rule fired and then
    //     spent the appear on first paint; the RELEASED line carries how long it waited;
    //   * HELD with no RELEASED                           => that panel never drew anything at all,
    //     and its rod was withheld for the same reason and for the same length of time;
    //   * a vanish SKIPPED                                => the close edge was reached and refused
    //     by the same two terms, which the line names;
    //   * a close with no vanish line at all              => SurfaceCloseEdge never published (its
    //     own installation line is the next thing to read), and the release then happened with no
    //     animation, exactly as in every build before this one.
    //
    // CHANGE-GATED ON (surface, outcome) AND NOT ON THE COUNTS, because the counts change on every
    // call and a gate that reads them is no gate at all. The counters are bumped before the gate, so
    // a repeat is still in the tally the next DIFFERENT line prints.
    private static int _appearPlayed;

    private static int _appearHeld;

    private static int _appearReleased;

    private static int _vanishStarted;

    private static int _vanishSkipped;

    private static string _lastAppearKey = string.Empty;

    private static string _lastVanishKey = string.Empty;

    private static void NoteAppear(Entry e, string outcome, float now)
    {
        if (outcome == "PLAYED")
            _appearPlayed++;
        else if (outcome == "HELD")
            _appearHeld++;
        else
            _appearReleased++;
        string key = e.Name + "\0" + outcome;
        if (key == _lastAppearKey)
            return;
        _lastAppearKey = key;
        string waited = outcome == "RELEASED"
            ? $" It waited {(now - e.OwedSince) * 1000f:F0} ms for its first paint."
            : string.Empty;
        string what = outcome switch
        {
            "PLAYED" => "the panel was drawing at its reveal edge, so the appear ran THERE — the "
                        + "first frame the eye sees it is the first frame of the animation",
            "HELD" => "the panel reached its reveal edge with NOTHING DRAWABLE under it, so the "
                      + "appear has NOT been played: it is OWED and will run on the first frame this "
                      + "panel is measured drawing something. Its grab bar was withheld in the same "
                      + "statement and from the same verdict (ModBuild 378), so the dust and the rod "
                      + "cannot disagree. The panel itself is revealed exactly as before — the float, "
                      + "the pose, the fit and the input path are untouched and only the decoration "
                      + "waits",
            _ => "the panel is drawing, so the appear it was owed ran NOW, and its grab bar came back "
                 + "in the same statement. This is the one and only time this float can spend it",
        };
        // HW-VERIFY
        VRLog.Note(Scope, $"SURFACE MATERIALISE APPEAR {outcome} on '{e.Name}': {what}.{waited} "
                          + $"TALLY this session: {_appearPlayed} played at the reveal edge, "
                          + $"{_appearHeld} held for an empty panel, {_appearReleased} of those "
                          + "later spent on first paint. BEFORE THIS BUILD THIS FAMILY HAD NO APPEAR "
                          + "AT ALL: WindowMaterialise.PlayIn's only call site is behind "
                          + "ModalFallback.IsFloated, which is false for every panel with no "
                          + "UIWindow. IF THIS LINE IS ABSENT for a decision window the user says "
                          + "still pops in, the reveal edge was never reached and the conversion "
                          + "lines for that host are the place to look, not this feature.");
    }

    private static void NoteVanish(string name, string outcome, string why)
    {
        if (outcome == "STARTED")
            _vanishStarted++;
        else
            _vanishSkipped++;
        string key = name + "\0" + outcome;
        if (key == _lastVanishKey)
            return;
        _lastVanishKey = key;
        // HW-VERIFY
        VRLog.Note(Scope, $"SURFACE MATERIALISE VANISH {outcome} on '{name}': {why}. TALLY this "
                          + $"session: {_vanishStarted} started, {_vanishSkipped} skipped for having "
                          + "nothing to dissolve. WHAT THE PLAYER SEES WHEN THIS SAYS STARTED: the "
                          + "window's own pixels leave on the GAME's frame — its Hide() ends in "
                          + "window.SetActive(false) and an inactive CanvasRenderer draws nothing — "
                          + "and a shard cloud, torn while the elements were still active and "
                          + "parented to the mod-owned host, blows away over the configured vanish "
                          + "length. The element-by-element wipe a modal window gets is NOT visible "
                          + "here, and holding the window active to buy it was refused: this popup "
                          + "carries the ControllerInputAreaLocal that its Hide() destroys, and it is "
                          + "the one four deadlock reports were about. THIS LINE IS THE FALSIFIER: it "
                          + "is written on every close edge this family publishes, so a decision "
                          + "window that left the screen and is named by no line here never reached "
                          + "the effect — read SurfaceCloseEdge's installation line next.");
    }

    // ---- the late pump ----------------------------------------------------------------------------

    private static void EnsurePump()
    {
        if (_pump != null)
            return;
        // DontDestroyOnLoad and NOT HideFlags.HideAndDontSave: the two overlap, and Unity's own
        // teardown treats a HideAndDontSave object as the caller's to destroy — a pump that survives
        // by a flag rather than by the scene rule is a pump nothing can ever clean up. It carries no
        // Renderer and no Collider, so there is nothing for it to be seen or hit as.
        var go = new GameObject("GloomhavenVR.SurfaceMaterialisePump");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _pump = go.AddComponent<Pump>();
    }

    /// <summary>
    /// THE ONE PLACE IN THIS LANE THAT PINS AN EXECUTION ORDER, AND IT IS RELIED ON RATHER THAN
    /// HARDENED WITH.
    ///
    /// <para>The appear must start in the LateUpdate that revealed the panel: Unity's MultiPass rig
    /// renders the head camera once per eye and both passes run after every LateUpdate, so a write
    /// made here lands in both eyes, and a write made one frame later means the player saw one full
    /// frame of an un-animated panel — the pop the report is about. <c>WorldUIModule</c>'s driver is
    /// an ordinary MonoBehaviour at the default execution order, and its LateUpdate is what runs
    /// <c>CanvasConversion.LateTick → CompleteReveal</c>. A positive order runs strictly after it,
    /// by Unity's own rule rather than by luck — the same reason <c>PerfFrameSplit</c> pins 30000.
    /// The project's standing objection to <c>[DefaultExecutionOrder]</c> (BoardDriver.cs:66,
    /// RigModule.cs:59) is about freezing an order the code does NOT rely on, which would hide the
    /// bug; here the order IS the requirement and is stated as one.</para>
    ///
    /// <para>UNGUARDED Update BODIES STARVE INPUT IN THIS PROJECT — a single NRE out of a per-frame
    /// method has taken the whole WorldUI driver down before. A decoration is never allowed to do
    /// that, so the body is wrapped and a throw costs the animation and nothing else.</para>
    /// </summary>
    [DefaultExecutionOrder(20000)]
    private sealed class Pump : MonoBehaviour
    {
        private bool _reported;

        private void LateUpdate()
        {
            try
            {
                LatePass();
            }
            catch (Exception ex)
            {
                if (_reported)
                    return;
                _reported = true;
                VRLog.Error(Scope, "SURFACE MATERIALISE: the reveal-edge pass threw "
                                   + $"({ex.GetType().Name}: {ex.Message}). Decision panels appear "
                                   + "exactly as they did before this feature existed — fully drawn, "
                                   + "on the frame their reveal gate opens. Nothing about the float, "
                                   + "the pose, the fit or the input path depends on this pass. This "
                                   + "line prints once per process.");
            }
        }
    }
}
