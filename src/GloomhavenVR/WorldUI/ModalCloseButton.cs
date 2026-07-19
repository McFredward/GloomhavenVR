using System;
using GloomhavenVR.Core;
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
/// </summary>
internal static class ModalCloseButton
{
    private const float ButtonSizePx = 46f;
    private const float InsetPx = 8f;
    private const float BarLengthFraction = 0.52f;
    private const float BarThicknessPx = 5f;

    /// <summary>Build the X button on <paramref name="panel"/>'s host, closing <paramref name="window"/>.</summary>
    internal static void Attach(ConvertedPanel panel, UIWindow window)
    {
        if (panel == null || panel.HostRect == null || window == null)
            return;
        // Match the host's layer (the mod layer after Convert's ApplyModLayer) so the head
        // camera draws it and the game UI Camera does not double-draw it.
        int layer = panel.HostGo != null ? panel.HostGo.layer : 5;
        UIWindow target = window;
        try
        {
            Build(panel.HostRect, layer, () => ModalFallback.CloseFloatedWindow(target));
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"MODAL CLOSE (X button): could not build the X for '{window.name}' " +
                                  $"({ex.GetType().Name}: {ex.Message}) — the escape chord still closes it.");
        }
    }

    private static void Build(RectTransform host, int layer, Action onClose)
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
        img.color = new Color(0.55f, 0.12f, 0.12f, 0.96f); // maroon disc-plate; the raycast target
        img.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.55f, 0.55f, 1f);
        colors.pressedColor = new Color(1f, 0.75f, 0.75f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() =>
        {
            try { onClose(); }
            catch (Exception ex) { VRLog.Error("WorldUI", $"Modal X button action threw: {ex}"); }
        });

        // Two crossed bars form the "X" (font-free → always visible).
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
        rect.localPosition = new Vector3(0f, 0f, 0f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.96f, 0.94f, 0.9f, 1f); // bone-white cross
        img.raycastTarget = false; // pokes/laser hit the plate, not the glyph
    }
}
