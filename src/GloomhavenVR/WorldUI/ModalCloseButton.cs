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

    /// <summary>How far (host px ≈ mm) the X floats toward the viewer, so its equal-tier canvas
    /// deterministically draws over the window's coplanar content. Imperceptible in the headset.
    /// NOTE (MP round 2, "immer noch kein X"): the nudge alone does NOT decide the equal-tier
    /// draw — see <see cref="DepthStampBehindPx"/> for why, and for the stamp that does.</summary>
    private const float ViewerNudgePx = 4f;

    /// <summary>
    /// MP round 2 root cause ("Kontrolle übergeben" STILL had no X): the blacklist flip DID
    /// attach the X to the transfer-control player picker ('UI Multiplayer Select Player
    /// Submenu_unified', ID MutiplayerPlayerPicker) — no attach failure in the log — but the X
    /// never became VISIBLE. The equal-order tie (X visuals at ModalHostSortingOrder == the
    /// adopted window content's 1000) is broken by Unity's transparent-sort DISTANCE, and that
    /// distance is measured camera → CANVAS (bounds/transform), not per-pixel: the X canvas sits
    /// at the host's top-right CORNER, ~half a panel diagonal off-axis, so its euclidean camera
    /// distance measures FARTHER than the window canvas' center even though the plate is nudged
    /// 4 px toward the viewer — the X draws FIRST and the window content paints over it. Every
    /// other X-carrying float ships with its full-window backing stripped (the ESC/Options/
    /// submenu family, WantsTransparentBackground — nothing of theirs draws at the corner, so
    /// the mis-sort was invisible and their X "worked"); the player picker is the one window
    /// that floats with its opaque backing INTACT, which is exactly where the overdraw shows.
    ///
    /// FIX (general, not per-window): a color-invisible DEPTH-WRITING quad — the established
    /// GrabbableModal.BuildDepthMask material state (ZWrite on, ZTest LEqual, Blend Zero One →
    /// framebuffer color untouched), renderQueue 2999 so it stamps BEFORE all ~3000 UI — sits a
    /// hair behind the plate, between the X and the window plane. Whatever window pixels would
    /// paint over the plate now FAIL ZTest against the stamp (they lie ≥ the 4 px nudge behind
    /// it) regardless of how the canvas tie resolves, while the X's own plate/glyph pass
    /// (nearer) and a genuinely NEARER info panel still wins (its depth passes LEqual) — the
    /// issue-#8 "X must not pierce nearer panels" ruling is preserved. Order-independent, so it
    /// is safe for every window geometry, opaque backing or not.
    /// </summary>
    private const float DepthStampBehindPx = 1.5f;
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
    /// <para>Since the perspective fix this order carries ONLY the invisible HitPlane. The
    /// VISIBLE X sits on the modal tier (<see cref="ModalFallback"/>.ModalHostSortingOrder) like
    /// the window and the at-hand info panels, where equal order lets camera distance decide —
    /// the X at 1100 was the last element still drawing THROUGH a nearer info panel. Raycast
    /// priority and draw priority genuinely need different orders here, hence two canvases.</para>
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
            // Attach evidence (MP round 2 lesson): the X built silently, so "no X visible" could
            // not be told apart from "no X attached" in the hardware log. One line per attach.
            VRLog.Info("WorldUI", $"MODAL CLOSE (X button): attached to '{window.name}' (ID {window.ID}) " +
                                  "— top-right plate + depth stamp (draws over the window's own backing, " +
                                  "still occluded by nearer panels/hands).");
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
        // The VISUALS sit on the modal tier (ModalHostSortingOrder), like the window and the
        // at-hand info panels: at an equal order Unity breaks the transparent-UI tie by camera
        // distance, so an info panel held between the player and the menu now occludes the X
        // exactly as it occludes the window. The old single canvas at 1100 was the last thing
        // still piercing the panels — order beat distance, and depth never got a vote.
        //
        // Equal order against the window's own coplanar content would leave the X-vs-window draw
        // undefined, so the whole button is NUDGED a few px toward the viewer. The side the
        // viewer is on is MEASURED from the head camera, not assumed from the canvas axes.
        var xCanvas = go.AddComponent<Canvas>();
        xCanvas.overrideSorting = true;
        xCanvas.sortingOrder = ModalFallback.ModalHostSortingOrder;
        if (hostCanvas != null)
            xCanvas.worldCamera = hostCanvas.worldCamera; // match the host's event camera (adoption pattern)

        Camera? cam = hostCanvas != null ? hostCanvas.worldCamera : null;
        float toViewer = cam != null
            ? Mathf.Sign(Vector3.Dot(host.forward, cam.transform.position - host.position))
            : -1f; // fallback: converted panels face the player from their -Z side
        rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y,
                                         toViewer * ViewerNudgePx);

        // MP round 2 fix (see DepthStampBehindPx): guarantee the X draws over the window's own
        // coplanar content INDEPENDENT of the equal-tier canvas sort — the corner-mounted X
        // canvas measures FARTHER than the window canvas center, so on windows that keep their
        // opaque full-window backing (the transfer-control player picker) the backing painted
        // over the plate and the X was invisible. The stamp depth-occludes exactly the plate
        // footprint against everything at/behind the window plane; nearer panels/hands still
        // draw over the X (their depth passes), so nothing pierces.
        BuildDepthStamp(rect, layer, toViewer);

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

    /// <summary>
    /// The plate-sized depth stamp behind the X (see <see cref="DepthStampBehindPx"/>): a quad
    /// in the exact GrabbableModal.BuildDepthMask material state — the bundled Overlay shader
    /// forced to ZWrite 1 (stamp depth) / ZTest 4 = LEqual (nearer hands/board/panels still
    /// win) / Cull Off / Blend Zero One (color = dst, framebuffer untouched), renderQueue 2999
    /// so it lands after every opaque draw and BEFORE the ~3000 transparent UI. MeshRenderer
    /// keeps the default sortingOrder 0, below the canvases' 1000 — on either transparent-sort
    /// axis it composites before the UI, which is the whole point: by the time ANY canvas
    /// paints the plate area, the stamp depth is already there and content behind the plate
    /// fails ZTest. Parented to the plate rect (host px units), so it tracks the fit/scale like
    /// the plate itself and dies with the host on Release — no teardown wiring.
    /// </summary>
    private static void BuildDepthStamp(RectTransform plate, int layer, float toViewer)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "DepthStamp";
        go.layer = layer;
        UnityEngine.Object.Destroy(go.GetComponent<Collider>()); // never a poke/laser/physics target
        Transform t = go.transform;
        t.SetParent(plate, worldPositionStays: false);
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(ButtonSizePx, ButtonSizePx, 1f); // unit quad → plate footprint (px)
        // Between the plate face and the window plane: the plate sits ViewerNudgePx toward the
        // viewer, so a small setback the OPPOSITE way keeps the stamp in front of the window
        // content (≥ the nudge behind) but safely off the plate's own pixels (no z-fight).
        // Plate pivot is its top-right corner (1,1) — recenter the quad on the plate rect.
        t.localPosition = new Vector3(-ButtonSizePx * 0.5f, -ButtonSizePx * 0.5f,
                                      -toViewer * DepthStampBehindPx);

        var mr = go.GetComponent<MeshRenderer>();
        Material mat = WorldUIAssets.CreateFlatMaterial(Color.clear, overlay: true);
        bool depthCapable = mat.HasProperty("_ZWrite") && mat.HasProperty("_ZTest");
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 1);     // WRITE depth (stamp the plate plane)
        if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", 4);       // LEqual — closer things still occlude the X
        if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);         // two-sided (host may be viewed from behind)
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", 0); // Zero ┐ color = 0*src + 1*dst
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", 1); // One  ┘   = dst (UNCHANGED)
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 1; // 2999: pre-UI
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        if (!depthCapable)
            // Same degradation note as the modal depth mask: without the bundle shader the X
            // falls back to the (usually sufficient) canvas nudge — visible everywhere except
            // over an opaque full-window backing.
            VRLog.Warn("WorldUI", "MODAL CLOSE (X button): Overlay shader unavailable — the X depth " +
                                  "stamp cannot write depth; on windows with an opaque backing the X " +
                                  "may stay hidden behind the window content.");
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
