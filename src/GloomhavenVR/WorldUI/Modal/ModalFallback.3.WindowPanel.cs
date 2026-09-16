using System.Collections.Generic;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    /// <summary>Fallback windows currently open (tracked instances; pruned per tick).</summary>
    private static readonly HashSet<UIWindow> Open = new();
    private static readonly List<UIWindow> Scratch = new(8);

    /// <summary>All open modal windows this tick (ID-tracked + poll sources), rebuilt per tick.</summary>
    private static readonly List<UIWindow> OpenWindows = new(8);

    /// <summary>
    /// One floated window: the game window + its world-space host. Content fitting
    /// (test #13/#14) is centralized in <see cref="CanvasConversion"/> — every
    /// pokeable host is fitted and growth-re-fitted there; the story window's
    /// narrower content root is passed via <see cref="ConvertedPanel.FitContentRoot"/>.
    /// </summary>
    private sealed class WindowPanel
    {
        public UIWindow Window = null!;
        public ConvertedPanel Panel = null!;

        /// <summary>True when this floated window is a full-screen menu (ESC / options
        /// family) — the ones whose gamepad-nav selection highlight flickers and that the
        /// selection guard applies to (P6 flicker fix, <see cref="ApplyMenuSelectionGuard"/>).</summary>
        public bool FullScreenMenu;

        /// <summary>
        /// Issue #1: this window got a ONE-SHOT content fit (fitOneShot) at Convert — the ESC
        /// menu (width-hug) OR a pause/options confirmation dialog. Its board-relative scale was
        /// derived at Convert from the PRE-fit rect (the full window), so once the single fit
        /// shrinks the host to the visible dialog/button bounds the scale must be re-derived from
        /// the fitted width (the 5b re-scale block). Gates that re-derive so non-one-shot modals
        /// (story/message/rewards, whose default per-frame fit also flips FitOneShotApplied) never
        /// re-scale and keep their existing sizing.
        /// </summary>
        public bool OneShotFitted;

        /// <summary>
        /// Grabbable/scalable world affordance for this floated modal (sub-item B): the
        /// menu can be repositioned + two-hand-resized like the control board via the shared
        /// <see cref="PanelGrabHandle"/>. Since the Sieg/Niederlage rework (user request A)
        /// EVERY floated modal gets one — the end-of-scenario results windows included; those
        /// just carry no X close button (<see cref="IsResultsPanel"/>).
        /// </summary>
        public GrabbableModal? Grab;

        /// <summary>
        /// Item 1 (size): the board-relative host shrink derived for THIS window (a cap on
        /// <see cref="WindowScaleFactor"/>). Stored so a presence-regain refloat re-places the
        /// non-grabbable panels at the same size (grabbable ones carry it in their frame).
        /// </summary>
        public float ExtraScale = WindowScaleFactor;

        /// <summary>
        /// Item 1 (pause-menu size): the <see cref="ConvertedPanel.FitAppliedGeneration"/> the
        /// board-relative scale was last re-derived from, after the one-shot content fit shrank
        /// the host rect. WHY a generation and not the original bool latch (round 3, first-open
        /// size bug): the fit's new VERIFY phase may CORRECT a committed rect that turned out not
        /// to contain its own content, and a bool latch would leave the window carrying the scale
        /// derived from the rect that was just proven wrong. 0 = never derived (the first applied
        /// fit is generation 1).
        /// </summary>
        public int ScaleReDerivedAtFit;

        /// <summary>
        /// First-open pose fix (2026-08-02): the placement inputs the rule-1 spawn consumed, so the
        /// SAME placement can be replayed once against the FINAL (fitted, re-scaled) geometry while
        /// the window is still render-hidden — see <see cref="TickPoseRePlace"/>. Invalid
        /// (<c>Valid == false</c>) for a window that was NOT gaze-placed, above all the
        /// level-message rule-2 chain pose, which is authoritative and never re-placed.
        /// </summary>
        public SpawnAnchor SpawnAnchor;

        /// <summary>One-shot latch for the re-place above: set the moment the window has been
        /// re-placed OR permanently excluded, so the evaluation runs exactly once per open and can
        /// never oscillate with the reveal gate's pose-stability counter.</summary>
        public bool PoseRePlaceDone;

        /// <summary>
        /// The <see cref="ConvertedPanel.FitAppliedGeneration"/> <see cref="PoseRePlaceDone"/> was
        /// latched at. A one-shot VERIFY correction (round 3) advances the generation, which
        /// re-arms the latch so the placement is replayed against the CORRECTED geometry — while
        /// the window is still render-hidden. <c>TickPoseRePlaceOne</c> refuses to move an already
        /// revealed window on its own, so a late correction can never yank a visible window around.
        /// </summary>
        public int PoseRePlacedAtFit;

        /// <summary>
        /// Item 6 (parallel windows): a player-reachable menu
        /// (<see cref="MenuWindowFamily.IsGameOwnedMenu"/>) is
        /// STICKY — once floated it stays floated + visible in VR even when the GAME hides it. The
        /// ESC menu drives a single-toggle <c>ToggleGroup</c>: selecting Multiplayer turns the
        /// Options toggle off, whose deselect handler calls <c>UIOptionsWindow.Hide()</c> (a
        /// CanvasGroup alpha tween), so the game only ever keeps ONE submenu shown. The mod defeats
        /// that single-window policy for these menus by keeping the float alive and re-asserting the
        /// window's CanvasGroup (<see cref="ReassertStickyVisible"/>), so Options AND Multiplayer can
        /// float side by side. Blocking windows (story/results/confirms) are never sticky.
        /// </summary>
        public bool Sticky;

        /// <summary>Consecutive frames <c>ReassertStickyVisible</c> had to undo a hide. Zero while
        /// the window stays where we put it; a rising count is a write war (see that method).</summary>
        public int StickyFightFrames;

        /// <summary>ModBuild 181: this float is a HOVER CARD — it flies over the symbol the pointer
        /// is on, billboarded, with no grab bar and no X, and leaves with the hover. Its pose is
        /// driven every frame by <c>TickHoverCards</c>. See <c>IsMapRoomHoverCard</c>.</summary>
        public bool HoverCard;

        /// <summary>
        /// Item 6: set when the user closes THIS window via its own X / the escape chord. The per-tick
        /// release loop then drops the float even though it is <see cref="Sticky"/> — the ONLY way a
        /// sticky menu leaves VR short of scenario exit, so each floated window closes independently.
        /// </summary>
        public bool UserClosing;

        /// <summary>The window root's CanvasGroup (cached), re-asserted to keep a sticky menu visible
        /// after the game hides it. The window is <c>[RequireComponent(CanvasGroup)]</c>.</summary>
        public CanvasGroup? WindowCanvasGroup;

        /// <summary>
        /// Item 6 (empty-shell fix): the window root's own <see cref="Canvas"/> (cached), or null when
        /// the window carries none. A <c>UIWindow</c> whose serialized <c>_disableCanvas</c> is set
        /// DISABLES this Canvas when its hide fade completes (UIWindow.OnTransitionCompleted →
        /// <c>_canvas.enabled = false</c>). A disabled Canvas stops rendering its ENTIRE subtree, so a
        /// sticky menu the game single-window-toggled off (e.g. Options when Spielanleitung/Compendium
        /// opens) kept its float + CanvasGroup alpha but rendered as an EMPTY shell — only the mod-drawn
        /// grab bar / X remained. <see cref="ReassertStickyVisible"/> re-enables it so the parallel menu
        /// keeps its full live content. This is the window's own adopted canvas (same object
        /// <c>UIWindow._canvas = GetComponent&lt;Canvas&gt;()</c> targets), so re-enabling it never
        /// touches sub-canvases the game legitimately keeps hidden (closed option tabs).
        /// </summary>
        public Canvas? WindowCanvas;

        // OutOfViewSince — the recall's dwell stamp — is DELETED with the recall itself
        // (ModBuild 149, user ruling; the block is at the top of ModalFallback.6.MenuGuard.cs).
        // Nothing writes it any more, so leaving it would be a field that always reads 0 and an
        // invitation to restart the timer. Do not re-add it.

        /// <summary>
        /// LEVEL-MESSAGE CHAIN CONTINUITY (user ruling 2026-08-02): the last scripted message
        /// key (<see cref="CurrentLevelMessageKey"/>) seen displayed in this level-message
        /// group window. The tutorial chains messages through ONE kept-alive float, so a key
        /// CHANGE means a new hint just re-showed inside the existing panel at its previous
        /// pose — <see cref="TickLevelMessageChain"/> then CAPTURES that live pose into the shared
        /// chain store (<see cref="_chainPose"/>; it never re-places the panel — position
        /// continuity superseded the brief re-show recall). Seeded at convert so the first
        /// tick never mis-reads the just-placed pose as a message change. Null for
        /// non-level-message windows / before the first message.
        /// </summary>
        public string? LastLevelMessageKey;

        // ---- user #12: results-window thumbstick scroll ----------------------------------

        /// <summary>This float is a Sieg/Niederlage results window (<see cref="IsResultsPanel"/>)
        /// — its scroll area gets the thumbstick treatment (<see cref="TickResultsStickScroll"/>).</summary>
        public bool IsResults;

        /// <summary>The results window's scroll area (found lazily — the achievement list may
        /// populate frames after the window converts). Null until found.</summary>
        public ScrollRect? ResultsScroll;

        /// <summary>Fallback when the window carries a bare Scrollbar with NO ScrollRect —
        /// then the stick drives <see cref="Scrollbar.value"/> directly.</summary>
        public Scrollbar? ResultsScrollbar;

        /// <summary>Diagnostic latches: the found scroll target is logged ONCE per window open;
        /// a fruitless first search warns once (then silent 1 s retries).</summary>
        public bool ResultsScrollLogged;
        public bool ResultsScrollSearchWarned;

        /// <summary>Next unscaled time a fruitless scroll-target search may retry (1 s throttle).</summary>
        public float NextResultsScrollSearch;

        // ---- ModBuild 230: THE LIVENESS RULE (user: "Es darf niemals leere Fenster geben") ------
        //
        // Every field below is written ONLY by TickWindowLiveness / the convert that created this
        // entry (ModalFallback.9.Spawn.cs carries the whole rule and its evidence). They exist here
        // because the rule is per-FLOAT state and WindowPanel is the float; a parallel dictionary
        // keyed by UIWindow would go stale exactly when a window is destroyed under us, which is
        // one of the two failure shapes the rule has to catch.

        /// <summary>Unscaled time this float was created (the convert that built it). The
        /// "how long had it been standing" term of the release line, and the base the bounded
        /// liveness grace is measured from.</summary>
        public float FloatedAt;

        /// <summary>
        /// Has the emptiness half of the liveness rule been ARMED for this float? Until it is, a
        /// window that draws nothing is left alone — that is the spawn-to-first-paint window this
        /// project has twice shipped a false positive into. Arms on the FIRST frame the window is
        /// measured drawing something, or when <c>LivenessGraceSeconds</c> have passed since the
        /// float was created, whichever comes first — so the grace is bounded by construction and a
        /// permanently-un-armed population is impossible rather than merely unlikely.
        /// </summary>
        public bool LivenessArmed;

        /// <summary>Unscaled time <see cref="LivenessArmed"/> flipped, and which of the two arming
        /// conditions did it — both printed, because "armed by grace expiry" and "armed by first
        /// paint" mean very different things about a window that is later released.</summary>
        public float LivenessArmedAt;
        public string LivenessArmReason = string.Empty;

        /// <summary>Unscaled time this float was first measured drawing NOTHING in an unbroken run,
        /// or 0 while it is drawing something. ModBuild 291: the run now ends in a HIDE after
        /// <c>EmptyHideDwellSeconds</c> (0.35 s), not in a release after 2 s — the dwell got SHORTER
        /// because what it triggers became reversible, and a window whose content is merely swapped
        /// is protected by the wake being instant instead of by the bar being long.</summary>
        public float EmptySince;

        /// <summary>Last unscaled time this float was measured drawing something. Reported in the
        /// release line so the log states when the content actually went, not just when we looked.</summary>
        public float LastDrawnAt;

        /// <summary>Frame number the next liveness measurement may run on (the walk is strided —
        /// see <c>LivenessCheckStride</c>). Zero = due now.</summary>
        public int LivenessNextCheckFrame;

        /// <summary>
        /// Set by <c>TickWindowLiveness</c> when this float has been judged dead. The release loop
        /// in part 4 reads it as an unconditional "release this", which is what makes the liveness
        /// rule own the WHOLE window — panel, grab holder, X, collider and arc slot all leave
        /// through the one existing teardown rather than through a second, partial one.
        /// </summary>
        public bool EmptyReleasePending;

        /// <summary>Which shape fired (GONE / DARK) and the sub-reason, phrased for the log.
        /// Only meaningful while <see cref="EmptyReleasePending"/> is set.</summary>
        public string EmptyReleaseShape = string.Empty;

        // ---- ModBuild 291: DORMANCY — THE DARK VERDICT HIDES, IT NO LONGER TEARS DOWN ----------
        //
        // USER REPORT (2026-08-24, map room), verbatim: "Ich bin in die Karte gespawned dann ist das
        // Fenster mit der Character-UI plötzlich einfach verschwunden, und war mehrere Sekunden lang
        // verschwunden, bis es wieder aufgetaucht ist. Das soll nicht sein. Es darf erst gar nicht
        // verschwinden."
        //   — "I spawned into the map and then the window with the character UI suddenly just
        //     disappeared, and was gone for several seconds until it came back. That should not
        //     happen. It must not disappear in the first place."
        //
        // The ModBuild 230 ruling ("verschwindet das Objekt das in dem Fenster dargestellt wird, soll
        // auch das Fenster verschwinden" — "if the object shown in the window disappears, the window
        // should disappear too") says the WINDOW must go when its CONTENT goes. It never
        // said the float has to be DESTROYED to achieve that, and destroying it is what cost him the
        // seconds: host, collider, grab bar, arc seat and 150 MB of supersample target all had to be
        // rebuilt, and the rebuild re-ran the spawn placement, so the window came back at a different
        // yaw as well (Player.log:4309 yawed 54.8°, :4545 yawed 136.9° — the SAME window).
        //
        // A render-hidden float is exactly as invisible and exactly as un-clickable as a released
        // one: CanvasConversion part 6 disables every Canvas and every Renderer of the float, the
        // grab bar's GameObject goes with it (it is a registered extra render root) and both VR
        // input paths skip a Canvas that is not isActiveAndEnabled — that argument is already
        // written out in full on <c>GrabbableModal.IPanelGrabOwner.GrabVisible</c>. And it comes back
        // in ONE frame, at the same pose, in the same arc seat.
        //
        // Written ONLY by TickWindowLiveness (ModalFallback.9.Spawn.cs), which carries the rule.

        /// <summary>
        /// This float is DORMANT: alive, seated, posed and still measured, but render-hidden because
        /// its content is drawing nothing. NOT a release — <see cref="EmptyReleasePending"/> is the
        /// release, and a dormant float only ever reaches it through the long
        /// <c>DormantReleaseSeconds</c> backstop or through the ordinary "the game closed it" path.
        /// </summary>
        public bool Dormant;

        /// <summary>Unscaled time <see cref="Dormant"/> was set (0 while awake). The backstop is
        /// measured from here and the wake line reports how long the window was away.</summary>
        public float DormantSince;

        /// <summary>What was measured when it went dormant, phrased for the log.</summary>
        public string DormantReason = string.Empty;

        /// <summary>How many times this float has gone dormant and come back. A window that FLAPS
        /// says so in the census instead of silently costing a hide/show every couple of seconds —
        /// this counter is the falsifier for the short hide dwell.</summary>
        public int DormantCycles;

        // ---- THE APPEAR THIS FLOAT IS STILL OWED ------------------------------------------------
        //
        // USER REPORT (2026-09-03, map room, right after a reward popup), verbatim: "Nachdem das
        // Fenster kam, kam nach ca. 1-2 Sekunden dahinter die Animation das ein neues Fenster
        // spawnt aber das 'Fenster' ist sofort wieder verschwunden. Sowas sollte nicht passieren."
        //   — "after the window came up, the animation of a new window spawning played behind it
        //     about 1-2 seconds later, but the 'window' was gone again immediately."
        //
        // WHAT HE SAW, from Player.log (ModBuild 373): the map room re-floated 'New Party display'
        // (ID PartyPanel) at :5592, the pre-reveal content fit reported it had NOTHING MEASURABLE
        // (:5619), the reveal fired anyway on its 600 ms deadline (:5622) and the materialise dust
        // played its full 0.35 s over it (:5633) — 2071 CanvasRenderers driven, 900 shards in the
        // air — for a window that then failed the liveness rule's own verdict and was hidden again
        // (:5640, "not one of 861 Graphic(s) … passes"). The dust ANNOUNCED a window that was not
        // there.
        //
        // WHY THE REVEAL ITSELF IS NOT THE DEFECT, and this is the measurement that decides it.
        // The SAME window is born dark on its ORDINARY opens too: :3554 shows it opening, :3849
        // says "MODAL LIVENESS ARMED … after the bounded 1.5 s grace — it has NEVER been measured
        // drawing anything since it floated", and only at :3866 does the game show the inner
        // 'Party Display UI ' window that carries the content. That open was completely normal and
        // the window is still standing 2000 lines later. So "dark at the reveal edge" does NOT
        // distinguish the good open from the bad one — refusing the FLOAT on it would have thrown
        // away the map room's character screen, which is exactly the worse bug. The only thing that
        // distinguishes them is WHETHER THE CONTENT EVER ARRIVES, and that is knowable only later.
        //
        // SO THE ANNOUNCEMENT WAITS FOR THE THING IT ANNOUNCES. The reveal is untouched (a window
        // must never stay invisible, and a window with nothing drawable is invisible either way);
        // what moves is the DECORATION: when the reveal edge finds nothing drawable under the
        // float, the appear is OWED rather than played, and it is spent on the first frame the
        // liveness rule measures the window drawing — the first paint, or the wake from dormancy.
        // A float that never draws never spends it, which is the whole point.

        /// <summary>
        /// This float reached its reveal edge with nothing drawable under it, so its materialise
        /// APPEAR was not played and is still owed. Spent exactly once, by
        /// <c>ReleaseOwedAppear</c>, on the first frame the liveness rule measures this window
        /// drawing something; dropped unspent when the float is released.
        /// </summary>
        public bool AppearOwed;

        /// <summary>Unscaled time <see cref="AppearOwed"/> was set (0 when nothing is owed) — the
        /// "how long did the announcement wait" term of the release line, which is the number that
        /// says whether the wait was a hundred milliseconds or a whole second.</summary>
        public float AppearOwedSince;

        // ---- ModBuild 230: TRANSIENT ANNOUNCEMENTS (user: no X, click to dismiss) ---------------

        /// <summary>
        /// This float is a TRANSIENT ANNOUNCEMENT (<see cref="IsTransientAnnouncement"/>): a
        /// one-shot popup the game puts up to tell the player something and takes away again on the
        /// next click. It carries NO close button, it is never <see cref="Sticky"/>, and a click
        /// anywhere on it is routed to the game's own dismiss button.
        /// </summary>
        public bool Transient;
    }

    // ---- STORY WINDOW SYNC (wire record 19) seam ---------------------------------------------

    /// <summary>
    /// The floated STORY window's grab frame, if the scenario's narrative box
    /// (<c>StoryController.window</c>) is converted right now.
    ///
    /// <para>WHY THIS LIVES HERE. <see cref="Net.RemoteStorySync"/> has to read and write the pose
    /// and size the user gave that ONE window, and the only handle on those is the mod-owned
    /// <see cref="GrabbableModal"/> the conversion built for it — which is reachable only through
    /// the private <c>Converted</c> list. This is a pure LOOKUP: it never converts, never places
    /// and never releases anything, so the sync cannot change which windows float or when.</para>
    ///
    /// <para>Instance compare against the live singleton, exactly as
    /// <c>IsPollWindow</c> / the convert-time story-box sanity check do — the story window's
    /// <c>UIWindowID</c> is scene-serialized and the enum has no Story member, so the singleton IS
    /// the identity (decompiled StoryController.cs:65-66).</para>
    /// </summary>
    /// <returns>false when no story window is open, not converted (it fell back to the flat
    /// screen), or still behind the reveal gate — every one of which means "this client has no
    /// grabbable story window", NOT "this client cannot advance the story". The advance path never
    /// consults this.</returns>
    /// <remarks>Since ModBuild 222 this is a one-line forwarder onto the generalised
    /// <see cref="SharedWindows.TryGetGrab"/>, so that <see cref="Net.RemoteStorySync"/>'s three
    /// call sites and <c>ModalFallback.9.Spawn</c>'s one are untouched by the shared-window
    /// contract. The lookup itself moved because the map room has TWO more windows of exactly this
    /// shape and three copies of the same walk is how they drift apart.</remarks>
    internal static bool TryGetStoryGrab(out GrabbableModal? grab) =>
        SharedWindows.TryGetGrab(SharedWindowKind.ScenarioStory, out grab);

    /// <summary>A failed or deliberately screen-bound source cannot supply a shared float pose.
    /// Reuse the actual conversion policy so manual screen mode never strands waiting peers.</summary>
    internal static bool RewardPlacementFailed(UIWindow? window) =>
        window != null && (!ConvertBaseActive || Failed.Contains(window));

    internal static void PreserveRewardInitialPose(UIWindow window)
    {
        WindowPanel? wp = FindPanel(window);
        if (wp == null) return;
        wp.SpawnAnchor = default;
        wp.PoseRePlaceDone = true;
    }

    /// <summary>
    /// The mod-owned <see cref="GrabbableModal"/> built for a specific game <see cref="UIWindow"/>,
    /// or false when that window has no converted grab. Pending grabs are returned too;
    /// transport readers decide whether that window kind may publish before reveal.
    ///
    /// <para>A pure LOOKUP through the private <c>Converted</c> list: it never converts, never
    /// places and never releases anything, so nothing that calls it can change which windows float
    /// or when. That property is what makes it safe to call from the net module.</para>
    /// </summary>
    internal static bool TryGetGrabFor(UIWindow? window, out GrabbableModal? grab)
    {
        grab = null;
        if (window == null)
            return false;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Grab == null || !ReferenceEquals(wp.Window, window))
                continue;
            grab = wp.Grab;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The same lookup, keyed by <see cref="UIWindowID"/> — for the windows whose identity IS their
    /// id because no singleton exposes them (the quest popup). First match wins; the id is unique
    /// among floated windows in practice, and a second one would be a different window with the
    /// same authored id, which nothing in this project can tell apart anyway.
    /// </summary>
    internal static bool TryGetGrabById(UIWindowID id, out GrabbableModal? grab)
    {
        grab = null;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Grab == null || wp.Window == null || wp.Window.ID != id)
                continue;
            grab = wp.Grab;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The log name the mod already uses for the story float —
    /// <c>'GloomhavenVR.Panel_Modal_Story Window'</c> in every MODAL DIAG / MODAL REVEAL line — so
    /// the sync's own diagnostics name the window the same way the rest of the log does and the two
    /// can be grepped together.
    /// </summary>
    internal const string StoryWindowLogName = "GloomhavenVR.Panel_Modal_Story Window";
}
