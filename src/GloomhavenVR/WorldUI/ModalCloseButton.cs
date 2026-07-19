using System;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Item 3c: a small mod-drawn X (close) button pinned to the TOP-RIGHT corner of a floated
/// menu window's world-space host, so the user can dismiss the window directly (in addition to
/// the modal escape chord, <see cref="ModalFallback.TickEscapeChord"/>). It closes the window
/// through the game's OWN escape/hide path (<see cref="ModalFallback.CloseFloatedWindow"/> →
/// <c>UIWindow.Escape()</c>/<c>Hide()</c>), so game state observes the close normally.
///
/// MECHANISM (no new input path): the button is a plain uGUI <see cref="Button"/> parented to
/// the HOST canvas (a sibling of the game window subtree, NOT inside it). The host canvas is
/// already registered with <see cref="UguiPokeSurfaces"/> and hit by the dominant-hand laser
/// (RayUguiDriver), so the fingertip poke AND the laser drive this button's onClick through the
/// same <c>ExecuteEvents</c> path as every other converted widget — nothing extra to register.
/// Anchored to the host's top-right (anchor 1,1), so it tracks the host rect as the content fit
/// resizes it. Moved onto the dedicated mod layer with the rest of the host (so only the HMD head
/// camera draws it — no UI-Camera double-draw). Because it lives on the host — not the game tree —
/// it is destroyed with the host on <see cref="CanvasConversion.Release"/> and needs no teardown
/// wiring; the exact 2D restore is untouched.
///
/// The "X" glyph is drawn as two crossed <see cref="Image"/> bars (font-free) so it always
/// renders regardless of which TMP font resolved. EXCLUSION: only attached to grabbable modals
/// (<see cref="ModalFallback.IsGrabbableModal"/>) — never the Sieg/Niederlage results panels,
/// the same exclusion as the grab affordance.
///
/// Item 2 (style): the plate is now SUBTLE and on-theme with the VR settings panel the user likes —
/// a small, muted DARK plate (the settings panel's dark canvas bg, <c>Color(0.07,0.07,0.10)</c>)
/// carrying a BRASS "X" (the settings panel's brass grab-bar colour, <c>Color(0.62,0.5,0.28)</c>)
/// that brightens on hover — instead of the old loud bright-red box. The glyph is mathematically
/// centred on the plate (both bars anchored + pivoted at the plate centre, zero offset).
/// </summary>
internal static class ModalCloseButton
{
    // Small + tasteful (was 46 px). Sits inset from the host's top-right corner.
    private const float ButtonSizePx = 34f;
    private const float InsetPx = 7f;
    // The "X" occupies the middle ~44 % of the plate — a compact glyph with clear margins.
    private const float BarLengthFraction = 0.44f;
    private const float BarThicknessPx = 3.5f;

    /// <summary>
    /// Issue #8 (X unclickable) ROOT CAUSE + FIX. The X plate lived on the HOST canvas at
    /// <c>ModalFallback.ModalHostSortingOrder = 1000</c> — the SAME sortingOrder the adopted
    /// game-window content reports (verified in the runtime log: the Options window's adopted
    /// canvas raycasts at <c>sortingOrder=1000</c>). <see cref="UguiPointer.Beats"/> awards a
    /// coplanar raycast TIE to the nested game content (<c>challenger.sortingOrder &gt;=
    /// incumbent.sortingOrder</c>), so wherever any game raycast target sat behind the X — a
    /// full-window frame image covers the whole menu — the game graphic won the hit and the X's
    /// own <see cref="Button"/> never fired (the log shows game widgets clicking fine but NOT a
    /// single "MODAL CLOSE (X button)" line all session). Fix: give the X its OWN nested canvas
    /// ABOVE the content at this order (the grab bar's "above the menu" order, <see
    /// cref="GrabbableModal"/> BarSortingOrder) and register it as a nested surface of the host,
    /// so the poke/laser (<see cref="UguiPointer.TryRaycast"/> merges <see
    /// cref="UguiPokeSurfaces.NestedOf"/>) hit the X and it WINS the tie (1100 &gt; 1000).
    /// </summary>
    private const int CloseButtonSortingOrder = 1100;

    // On-theme palette (mirrors SettingsPanel): muted dark plate + brass glyph.
    private static readonly Color PlateColor = new(0.09f, 0.09f, 0.12f, 0.82f);  // settings-panel dark, semi-transparent
    private static readonly Color GlyphColor = new(0.62f, 0.5f, 0.28f, 0.95f);   // settings-panel brass grab-bar tone

    /// <summary>Build the X button on <paramref name="panel"/>'s host, closing <paramref name="window"/>.</summary>
    internal static void Attach(ConvertedPanel panel, UIWindow window)
    {
        if (panel == null || panel.HostRect == null || panel.HostCanvas == null || window == null)
            return;
        // Match the host's layer (the mod layer after Convert's ApplyModLayer) so the head
        // camera draws it and the game UI Camera does not double-draw it.
        int layer = panel.HostGo != null ? panel.HostGo.layer : 5;
        UIWindow target = window;
        try
        {
            Build(panel.HostRect, panel.HostCanvas, layer, () =>
            {
                // Diagnostic (issue #8): prove the click reached the X and WHICH window it targets —
                // distinguishes "click never hit the X" (no line) from "close failed" (this line, then
                // CloseFloatedWindow's own result line). The two together are the full X-close trace.
                VRLog.Info("WorldUI", $"MODAL CLOSE (X button): PRESSED for '{target.name}' (ID {target.ID}) " +
                                      "— closing exactly this window (Escape/Hide), other open windows untouched.");
                ModalFallback.CloseFloatedWindow(target);
            });
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"MODAL CLOSE (X button): could not build the X for '{window.name}' " +
                                  $"({ex.GetType().Name}: {ex.Message}) — the escape chord still closes it.");
        }
    }

    private static void Build(RectTransform host, Canvas hostCanvas, int layer, Action onClose)
    {
        var go = new GameObject("GloomhavenVR.ModalCloseX") { layer = layer };
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(host, worldPositionStays: false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f); // top-right corner of the host rect
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(ButtonSizePx, ButtonSizePx);
        rect.anchoredPosition = new Vector2(-InsetPx, -InsetPx);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);

        var img = go.AddComponent<Image>();
        img.color = PlateColor; // muted dark board-styled plate; the raycast target
        img.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        // Multiplies the plate colour: dim at rest, a touch brighter on hover, dimmer while pressed
        // — a subtle affordance, no loud colour flash.
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.highlightedColor = new Color(1.05f, 1.05f, 1.05f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(() =>
        {
            try { onClose(); }
            catch (Exception ex) { VRLog.Error("WorldUI", $"Modal X button action threw: {ex}"); }
        });

        // Issue #8 fix (see CloseButtonSortingOrder): lift the X onto its OWN nested canvas ABOVE
        // the adopted game content so it WINS the coplanar raycast tie the shared order-1000 lost.
        // The plate's Image now registers with THIS canvas (not the host), so it must be merged
        // into the host's hit-testing via UguiPokeSurfaces.RegisterNested — exactly how adopted
        // game canvases are queried by UguiPointer.TryRaycast. Cleaned up automatically: on
        // CanvasConversion.Release the host is UguiPokeSurfaces.Unregister'd (drops nested lists)
        // and this GameObject is destroyed with the host.
        var xCanvas = go.AddComponent<Canvas>();
        xCanvas.overrideSorting = true;
        xCanvas.sortingOrder = CloseButtonSortingOrder;
        if (hostCanvas != null)
            xCanvas.worldCamera = hostCanvas.worldCamera; // match the host's event camera (adoption pattern)
        go.AddComponent<GraphicRaycaster>();
        if (hostCanvas != null)
            UguiPokeSurfaces.RegisterNested(hostCanvas, xCanvas);

        // Two crossed bars form the "X" (font-free → always visible). Children of the plate → they
        // draw on the X's own canvas, on top of the menu, with the plate.
        CrossBar(rect, layer, 45f);
        CrossBar(rect, layer, -45f);
    }

    private static void CrossBar(RectTransform parent, int layer, float angleDeg)
    {
        var go = new GameObject("XBar") { layer = layer };
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(ButtonSizePx * BarLengthFraction, BarThicknessPx);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
        // Item 4 (centering fix): anchor (0.5,0.5) + anchoredPosition zero already puts the bar's
        // pivot at the PLATE CENTER. The old `localPosition = (0,0,0)` here OVERRODE that and snapped
        // the bar to the parent's LOCAL ORIGIN — which is the plate's PIVOT corner (top-right, the
        // plate pivots at (1,1)), NOT its center — so the whole X sat offset up-and-right (the
        // reported "still not centered"). Only normalize z; keep the centered x/y from the anchor.
        rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);

        var img = go.AddComponent<Image>();
        img.color = GlyphColor; // brass "X" (matches the settings-panel grab bar)
        img.raycastTarget = false; // pokes/laser hit the plate, not the glyph
    }
}
