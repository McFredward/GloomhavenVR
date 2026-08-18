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
/// renders regardless of which TMP font resolved. EXCLUSION: never attached to the
/// Sieg/Niederlage results windows — they float as grabbable modals like everything else now,
/// but the only way out of the end-of-scenario window must remain its native continue/retry
/// buttons (an X would strand the scenario-end flow).
///
/// Item 2 (style): the plate is SUBTLE and on-theme — a small, muted DARK plate
/// (<c>Color(0.07,0.07,0.10)</c>) carrying a BRASS "X" (<c>Color(0.62,0.5,0.28)</c>) that
/// brightens on hover, instead of the old loud bright-red box. Both tones were sampled from
/// the mod's own (since retired) free-floating settings panel, which the user liked. The glyph
/// is mathematically centred on the plate (both bars anchored + pivoted at the plate centre,
/// zero offset).
/// </summary>
internal static class ModalCloseButton
{
    // Small + tasteful (was 46 px). Sits inset from the host's top-right corner.
    private const float ButtonSizePx = 34f;

    /// <summary>How far (host px ~ mm) the X floats toward the viewer, so it never z-fights the
    /// window's coplanar content. Imperceptible in the headset. NOTE (MP round 2, "immer noch kein
    /// X"): the nudge alone does NOT decide the draw - see <see cref="XOrderOffset"/> for what
    /// does now, and for why the depth stamp that used to is gone.</summary>
    private const float ViewerNudgePx = 4f;

    /// <summary>
    /// MP round 2 root cause ("Kontrolle uebergeben" STILL had no X) and the TRANSPARENCY ROUND
    /// answer to it. The blacklist flip DID attach the X to the transfer-control player picker - no
    /// attach failure in the log - but the X never became VISIBLE: its canvas sat at the SAME order
    /// as the window content, and Unity breaks an equal-order tie by camera distance measured to the
    /// CANVAS, not per pixel. The X canvas sits at the host's top-right CORNER, ~half a panel
    /// diagonal off-axis, so it measured FARTHER than the window canvas centre and the window's
    /// opaque backing painted over it. Every other X-carrying float ships with its backing stripped,
    /// which is why only the player picker showed the defect.
    ///
    /// <para>Round 2 fixed that with a colour-invisible DEPTH-WRITING quad behind the plate. That
    /// stamp is now GONE: it is a 34x34 px box of "everything behind this is deleted", so it cut a
    /// hard-edged hole into whatever panel happened to lie behind the corner of a window - the same
    /// defect, one element smaller, as the per-host and per-modal stamps this round removes. The X
    /// no longer needs it: its visible canvas is an ORDER FOLLOWER of its own panel
    /// (<see cref="XOrderOffset"/>), so it beats its own window's content by ORDER instead of by a
    /// distance tie, deterministically and with no depth written anywhere. And because the offset
    /// stays under <c>CanvasConversion.PanelOrderStep</c>, a genuinely NEARER panel still outranks
    /// it - the issue-#8 "the X must not pierce nearer panels" ruling is preserved by construction
    /// rather than by a ZTest.</para>
    /// </summary>
    private const int XOrderOffset = 2;
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
    /// single "MODAL CLOSE (X button)" line all session). Fix: an elevated nested canvas at this
    /// order, registered as a nested surface of the host, so the poke/laser (<see
    /// cref="UguiPointer.TryRaycast"/> merges <see cref="UguiPokeSurfaces.NestedOf"/>) hit it and
    /// it WINS the tie (1100 &gt; 1000).
    ///
    /// <para>This order carries ONLY the invisible HitPlane, and it is deliberately a FIXED value,
    /// not a ladder follower. It is compared against the host's CONVERSION tier, which
    /// <c>CanvasConversion.BaseSortingOrderOf</c> keeps reporting to <c>UguiPointer.Beats</c>
    /// unchanged while the DRAW order moves with distance - so 1100 &gt; 1000 stays exactly the
    /// comparison the shipped builds made, and no raycast decision anywhere changed with the
    /// transparency round. The VISIBLE X is a separate canvas riding the ladder
    /// (<see cref="XOrderOffset"/>): raycast priority and draw priority genuinely need different
    /// orders here, hence two canvases.</para>
    /// </summary>
    private const int CloseButtonSortingOrder = 1100;

    // On-theme palette, sampled from the mod's own retired free-floating settings panel: muted
    // dark plate + brass glyph. Kept as literals because that panel is gone (the VR settings are
    // an options-window tab now, VROptionsTab) — there is no shared palette left to point at.
    private static readonly Color PlateColor = new(0.09f, 0.09f, 0.12f, 0.82f);  // dark, semi-transparent
    private static readonly Color GlyphColor = new(0.62f, 0.5f, 0.28f, 0.95f);   // brass grab-bar tone

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
            Build(panel, panel.HostRect, panel.HostCanvas, layer, () =>
            {
                // Diagnostic (issue #8): prove the click reached the X and WHICH window it targets —
                // distinguishes "click never hit the X" (no line) from "close failed" (this line, then
                // CloseFloatedWindow's own result line). The two together are the full X-close trace.
                VRLog.Info("WorldUI", $"MODAL CLOSE (X button): PRESSED for '{target.name}' (ID {target.ID}) " +
                                      "— closing exactly this window (Escape/Hide), other open windows untouched.");
                ModalFallback.CloseFloatedWindow(target);
            });
            // Attach evidence (MP round 2 lesson): the X built silently, so "no X visible" could
            // not be told apart from "no X attached" in the hardware log. One line per attach.
            VRLog.Info("WorldUI", $"MODAL CLOSE (X button): attached to '{window.name}' (ID {window.ID}) " +
                                  "- top-right plate riding the panel's draw order +2 (over the window's " +
                                  "own backing, still under any panel that is genuinely nearer).");
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"MODAL CLOSE (X button): could not build the X for '{window.name}' " +
                                  $"({ex.GetType().Name}: {ex.Message}) — the escape chord still closes it.");
        }
    }

    private static void Build(ConvertedPanel panel, RectTransform host, Canvas hostCanvas, int layer,
        Action onClose)
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
        img.color = PlateColor; // muted dark board-styled plate
        img.raycastTarget = false; // clicks land on the invisible HitPlane below, not the visuals

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

        // DRAW and RAYCAST split onto two canvases, because they need DIFFERENT orders.
        //
        // The VISUALS ride the owning panel's distance-derived draw order at a small offset, so the
        // X is unambiguously over its own window and unambiguously under any panel that is
        // genuinely nearer than that window (see XOrderOffset). The plate is still NUDGED a few px
        // toward the viewer so it cannot z-fight the window's coplanar content; the side the viewer
        // is on is MEASURED from the head camera, not assumed from the canvas axes.
        var xCanvas = go.AddComponent<Canvas>();
        xCanvas.overrideSorting = true;
        // Seed only; RegisterOrderFollower below puts it on the panel's live ladder order + offset.
        xCanvas.sortingOrder = panel.DrawSortingOrder + XOrderOffset;
        if (hostCanvas != null)
            xCanvas.worldCamera = hostCanvas.worldCamera; // match the host's event camera (adoption pattern)

        Camera? cam = hostCanvas != null ? hostCanvas.worldCamera : null;
        float toViewer = cam != null
            ? Mathf.Sign(Vector3.Dot(host.forward, cam.transform.position - host.position))
            : -1f; // fallback: converted panels face the player from their -Z side
        rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y,
                                         toViewer * ViewerNudgePx);

        // Beat the window's own coplanar content by ORDER, not by a depth stamp and not by a
        // distance tie: the X canvas follows its panel's ladder order at XOrderOffset (see that
        // constant). Registered here so the very first frame is already ordered.
        if (panel != null)
            CanvasConversion.RegisterOrderFollower(panel, xCanvas, XOrderOffset);

        // The RAYCAST keeps the elevated order (issue #8), on an INVISIBLE plate: Beats()
        // compares sortingLayer then sortingOrder and never distance, so this is what lets the X
        // win the cross-raycaster tie against the adopted game content behind it — WITHOUT also
        // winning the draw against a nearer info panel. Registered as a nested surface exactly
        // like before; released with the host, so no teardown of its own.
        var hitGo = new GameObject("HitPlane") { layer = layer };
        var hitRect = hitGo.AddComponent<RectTransform>();
        hitRect.SetParent(rect, worldPositionStays: false);
        hitRect.anchorMin = Vector2.zero;
        hitRect.anchorMax = Vector2.one;
        hitRect.offsetMin = Vector2.zero;
        hitRect.offsetMax = Vector2.zero;
        hitRect.localScale = Vector3.one;
        hitRect.localRotation = Quaternion.identity;
        hitRect.localPosition = new Vector3(hitRect.localPosition.x, hitRect.localPosition.y, 0f);
        var hitImg = hitGo.AddComponent<Image>();
        hitImg.color = new Color(0f, 0f, 0f, 0f); // invisible; Graphic raycasting ignores alpha
        hitImg.raycastTarget = true;
        var hitCanvas = hitGo.AddComponent<Canvas>();
        hitCanvas.overrideSorting = true;
        hitCanvas.sortingOrder = CloseButtonSortingOrder;
        if (hostCanvas != null)
            hitCanvas.worldCamera = hostCanvas.worldCamera;
        hitGo.AddComponent<GraphicRaycaster>();
        if (hostCanvas != null)
            UguiPokeSurfaces.RegisterNested(hostCanvas, hitCanvas);

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
        img.color = GlyphColor; // brass "X"
        img.raycastTarget = false; // pokes/laser hit the plate, not the glyph
    }
}
