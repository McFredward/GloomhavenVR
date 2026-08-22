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
        /// Item 6 (parallel windows): a player-reachable menu (<see cref="NonBlockingMenus"/>) is
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

    /// <summary>
    /// The mod-owned <see cref="GrabbableModal"/> built for a specific game <see cref="UIWindow"/>,
    /// or false when that window is not floated (not open, not converted, or still behind the
    /// reveal gate).
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
