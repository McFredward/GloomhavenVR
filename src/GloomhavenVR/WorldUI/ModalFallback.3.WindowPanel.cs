using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.EventSystems;
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
        /// Item 1 (pause-menu size): true once a full-screen menu's board-relative scale has been
        /// re-derived from its FITTED host width (after the one-shot content fit shrank the rect)
        /// and pushed to <see cref="Grab"/>. One-shot latch — the re-derive runs once per open.
        /// </summary>
        public bool ScaleReDerived;

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

        /// <summary>
        /// LOST-MENU RECALL: unscaled time-stamp since when this panel's host has been
        /// CONTINUOUSLY out of the head view (or beyond the recall distance). 0 = currently
        /// visible / grabbed / not tracked. When the elapsed span exceeds
        /// <see cref="RecallOutOfViewSeconds"/> the panel is re-placed in front of the HMD
        /// (<see cref="TickMenuRecall"/>) so an OPEN menu can never be invisibly lost while
        /// it blocks card/board input.
        /// </summary>
        public float OutOfViewSince;

        /// <summary>
        /// LEVEL-MESSAGE CHAIN CONTINUITY (user ruling 2026-08-02): the last scripted message
        /// key (<see cref="CurrentLevelMessageKey"/>) seen displayed in this level-message
        /// group window. The tutorial chains messages through ONE kept-alive float, so a key
        /// CHANGE means a new hint just re-showed inside the existing panel at its previous
        /// pose — <see cref="TickMenuRecall"/> then CAPTURES that live pose into the per-group
        /// chain store (<see cref="ChainPoses"/>; it no longer re-places the panel — position
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
}
