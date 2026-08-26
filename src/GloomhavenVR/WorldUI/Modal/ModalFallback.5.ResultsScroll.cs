using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    // ---- results-window thumbstick scroll (user #12) ------------------------------------

    /// <summary>Stick-Y deadzone for the results-window scroll (mirrors RayUguiDriver.ScrollDeadzone).</summary>
    private const float ResultsScrollDeadzone = 0.3f;

    /// <summary>Wheel notches/second at full deflection (mirrors RayUguiDriver.ScrollNotchesPerSecond,
    /// so the results list scrolls at exactly the felt speed of every other menu list).</summary>
    private const float ResultsScrollNotchesPerSecond = 40f;

    /// <summary>Bare-Scrollbar fallback speed: normalized value units/second at full deflection.</summary>
    private const float ResultsScrollbarUnitsPerSecond = 0.6f;

    /// <summary>Reused event payload for the synthesized wheel events (UguiPointer.Scroll pattern).</summary>
    private static PointerEventData? _resultsScrollData;

    /// <summary>
    /// USER #12: "the Sieg/Niederlage window has a scrollbar — I want joystick scrolling
    /// here too, like every other scrollbar in menus." The generic thumbstick scroll
    /// (RayUguiDriver.TickStickScroll) only engages when the hover raycast target sits
    /// INSIDE a ScrollRect subtree; on the results window the laser mostly lands on the
    /// window frame/backdrop instead (the achievement rows are display-only), so the
    /// generic path no-ops. This drives the window's scroll area DIRECTLY while either
    /// hand's laser/poke hovers anywhere on the floated results host: a synthesized
    /// mouse-wheel notch stream onto the ScrollRect (same units/speed as the generic
    /// path — the ScrollRect applies its own scrollSensitivity), or, if the window
    /// carries only a bare Scrollbar, its <c>value</c> is driven directly. When the
    /// hover DOES land inside a ScrollRect subtree the generic path owns it and this
    /// stands down — never both, so no double-speed scrolling.
    /// </summary>
    private static void TickResultsStickScroll()
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!wp.IsResults || !wp.Panel.IsAlive || wp.Panel.HostGo == null)
                continue;
            if (wp.ResultsScroll == null && wp.ResultsScrollbar == null
                && !TryFindResultsScrollTarget(wp))
                continue;
            DriveResultsScroll(wp, VRHands.Left);
            DriveResultsScroll(wp, VRHands.Right);
        }
    }

    /// <summary>
    /// Locate the results window's scroll area (throttled to 1/s while missing — the
    /// achievement list pools in frames after the window opens). Prefers an ACTIVE
    /// <see cref="ScrollRect"/> (the game's ExtendedScrollRect derives from it), falls
    /// back to a bare vertical <see cref="Scrollbar"/>. Logs the found target once per
    /// window open (diagnostic contract).
    /// </summary>
    private static bool TryFindResultsScrollTarget(WindowPanel wp)
    {
        if (Time.unscaledTime < wp.NextResultsScrollSearch)
            return false;
        wp.NextResultsScrollSearch = Time.unscaledTime + 1f;

        Transform? root = wp.Window != null ? wp.Window.transform : wp.Panel.Target;
        if (root == null)
            return false;

        ScrollRect[] scrolls = root.GetComponentsInChildren<ScrollRect>(true);
        ScrollRect? scroll = null;
        for (int i = 0; i < scrolls.Length; i++)
        {
            if (scrolls[i] == null)
                continue;
            if (scroll == null)
                scroll = scrolls[i];
            if (scrolls[i].isActiveAndEnabled)
            {
                scroll = scrolls[i]; // an active one beats an inactive first hit
                break;
            }
        }
        if (scroll != null)
        {
            wp.ResultsScroll = scroll;
            if (!wp.ResultsScrollLogged)
            {
                wp.ResultsScrollLogged = true;
                VRLog.Info("WorldUI", "RESULTS SCROLL: scroll target of " +
                                      $"'{(wp.Window != null ? wp.Window.name : "<results>")}' = ScrollRect " +
                                      $"'{scroll.gameObject.name}' ({scroll.GetType().Name}, " +
                                      $"vBar={(scroll.verticalScrollbar != null ? "wired" : "none")}, " +
                                      $"active={scroll.isActiveAndEnabled}) — thumbstick-Y scrolls it while " +
                                      "the laser/poke hovers the floated window (wheel-notch stream; generic " +
                                      "hover path owns it when the ray lands inside the ScrollRect itself).");
            }
            return true;
        }

        // No ScrollRect anywhere: a bare Scrollbar (vertical preferred) is driven directly.
        Scrollbar[] bars = root.GetComponentsInChildren<Scrollbar>(true);
        Scrollbar? bar = null;
        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] == null)
                continue;
            bool vertical = bars[i].direction == Scrollbar.Direction.BottomToTop
                         || bars[i].direction == Scrollbar.Direction.TopToBottom;
            if (bar == null || (vertical && bars[i].isActiveAndEnabled))
                bar = bars[i];
        }
        if (bar != null)
        {
            wp.ResultsScrollbar = bar;
            if (!wp.ResultsScrollLogged)
            {
                wp.ResultsScrollLogged = true;
                VRLog.Info("WorldUI", "RESULTS SCROLL: scroll target of " +
                                      $"'{(wp.Window != null ? wp.Window.name : "<results>")}' = bare Scrollbar " +
                                      $"'{bar.gameObject.name}' (direction {bar.direction}, NO ScrollRect in the " +
                                      "window) — thumbstick-Y drives Scrollbar.value directly while the " +
                                      "laser/poke hovers the floated window.");
            }
            return true;
        }

        if (!wp.ResultsScrollSearchWarned)
        {
            wp.ResultsScrollSearchWarned = true;
            VRLog.Info("WorldUI", "RESULTS SCROLL: no ScrollRect/Scrollbar under " +
                                  $"'{(wp.Window != null ? wp.Window.name : "<results>")}' yet — " +
                                  "retrying every 1 s while the window floats (list content pools in late).");
        }
        return false;
    }

    /// <summary>
    /// One hand's contribution to the results scroll: only while its laser/poke hovers
    /// somewhere on THIS window's floated host, and only when the generic stick-scroll
    /// would no-op (hover outside any ScrollRect subtree — otherwise it owns the stick).
    /// Speed curve mirrors RayUguiDriver.TickStickScroll exactly (deadzone-normalized,
    /// unscaled time — the results screen halts the action processor).
    /// </summary>
    private static void DriveResultsScroll(WindowPanel wp, VRHand? hand)
    {
        if (hand == null)
            return;
        GameObject? hovered = hand.RayUgui.Hovered ?? hand.Poke.HoveredUi;
        if (hovered == null || !hovered.transform.IsChildOf(wp.Panel.HostTransform))
            return;
        if (hovered.GetComponentInParent<ScrollRect>() != null)
            return; // generic RayUguiDriver stick-scroll owns this hover — never double-drive

        // SCROLL BEATS FLIGHT (user 2026-08-03), reported from the same place the scroll is
        // decided so the two can never disagree — see UiScrollFocus. This branch
        // is a second, equally authoritative "the pointer is on a live scrollable": the hover is
        // on THIS results window's host and the window has a resolved scroll target. The
        // ScrollRect case still has to prove it can actually move (a results list short enough
        // to fit must not ground the player); a bare Scrollbar proves it with size < 1, which is
        // uGUI's own "the handle does not fill the track" i.e. there is travel to give.
        bool live = wp.ResultsScroll != null && wp.ResultsScroll.isActiveAndEnabled
            ? UiScrollFocus.CanScroll(wp.ResultsScroll)
            : wp.ResultsScrollbar != null && wp.ResultsScrollbar.isActiveAndEnabled
              && wp.ResultsScrollbar.size < 0.999f;
        if (live)
            UiScrollFocus.NoteScrollHover(hand,
                wp.ResultsScroll != null ? (UnityEngine.Object)wp.ResultsScroll : wp.ResultsScrollbar,
                "ModalFallback.ResultsScroll");

        float y = hand.Thumbstick.y;
        if (Mathf.Abs(y) < ResultsScrollDeadzone)
            return;
        float response = (Mathf.Abs(y) - ResultsScrollDeadzone) / (1f - ResultsScrollDeadzone);

        ScrollRect? scroll = wp.ResultsScroll;
        if (scroll != null && scroll.isActiveAndEnabled)
        {
            float notches = Mathf.Sign(y) * response * ResultsScrollNotchesPerSecond * Time.unscaledDeltaTime;
            _resultsScrollData ??= new PointerEventData(EventSystem.current);
            _resultsScrollData.scrollDelta = new Vector2(0f, notches);
            ExecuteEvents.ExecuteHierarchy(scroll.gameObject, _resultsScrollData, ExecuteEvents.scrollHandler);
            _resultsScrollData.scrollDelta = Vector2.zero;
            if (live)
                UiScrollFocus.NoteScrollDelivered(hand, scroll, "ModalFallback.ResultsScroll");
            return;
        }

        Scrollbar? bar = wp.ResultsScrollbar;
        if (bar != null && bar.isActiveAndEnabled)
        {
            // Stick up = toward the list top: value grows for BottomToTop/LeftToRight
            // scrollbars (the uGUI vertical-list convention), shrinks for the other two.
            float dir = bar.direction == Scrollbar.Direction.BottomToTop
                     || bar.direction == Scrollbar.Direction.LeftToRight ? 1f : -1f;
            bar.value = Mathf.Clamp01(bar.value
                + dir * Mathf.Sign(y) * response * ResultsScrollbarUnitsPerSecond * Time.unscaledDeltaTime);
            if (live)
                UiScrollFocus.NoteScrollDelivered(hand, bar, "ModalFallback.ResultsScroll");
        }
    }
}
