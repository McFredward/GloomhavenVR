using System;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal sealed partial class SettingsPanel : IPanelGrabOwner
{
    // ==========================================================================================
    //  Hover explanations (user 2026-07: "Die Erklärtexte sind zu lang, sie da drin stehen zu
    //  lassen; mach ein Mouseover-Hinweis oder so etwas stattdessen.")
    // ==========================================================================================

    /// <summary>
    /// WHAT CHANGED AND WHY. Several rows carried a three-to-five line paragraph UNDER them,
    /// permanently. That is the wrong shape for this panel twice over: it triples the height of a
    /// pane whose whole 2026-07 redesign was about being a short scannable list, and it spends
    /// that height on text a player reads exactly once. The label alone already names the
    /// compromise; the paragraph is what you want only at the moment you are undecided — which is
    /// precisely the moment you are pointing at the row. So the paragraphs moved to hover.
    ///
    /// <para>ONE BUBBLE, NOT ONE PER ROW: a single panel-owned object is repositioned and re-texted
    /// on each hover. Sixty rows do not each carry a hidden TextMeshPro, and there is never more
    /// than one explanation on screen to read.</para>
    ///
    /// <para>LASER AND TOUCH BOTH, FOR FREE: the trigger is
    /// <see cref="SettingsTooltipTarget"/>, a stock uGUI
    /// <c>IPointerEnterHandler</c>/<c>IPointerExitHandler</c>. The mod's pointer dispatches those
    /// through <c>ExecuteEvents</c> from BOTH the far ray and the fingertip, so neither source is
    /// special-cased here and neither can regress independently of the other.</para>
    ///
    /// <para>THE BUBBLE IS INERT: its background and its text both have
    /// <c>raycastTarget = false</c>, so it can never become the top raycast hit, never steal the
    /// hover that is keeping it open (which would make it flicker itself in and out), and never
    /// swallow a click aimed at whatever is behind it.</para>
    ///
    /// <para>REVERSIBILITY / MULTIPLAYER: the bubble is a child of the panel's own canvas, created
    /// and destroyed with it. It reads config text and shows it. No game object, no game state, no
    /// wire traffic.</para>
    /// </summary>
    private GameObject? _tipRoot;

    private RectTransform? _tipRect;
    private TextMeshProUGUI? _tipText;

    /// <summary>The row currently showing the bubble — only ITS exit may close it.</summary>
    private SettingsTooltipTarget? _tipOwner;

    /// <summary>Bubble width in canvas pixels (the panel itself is <see cref="PanelWidthPx"/>).</summary>
    private const float TipWidthPx = 300f;

    /// <summary>Gap between the panel's right edge and the bubble's left edge, in canvas pixels.</summary>
    private const float TipGapPx = 14f;

    // ---- build --------------------------------------------------------------------------------

    /// <summary>
    /// Create the single hover bubble, inactive, as a direct child of the settings canvas.
    ///
    /// <para><c>ignoreLayout</c> IS LOAD-BEARING: the canvas root drives a
    /// <see cref="VerticalLayoutGroup"/> plus a <see cref="ContentSizeFitter"/>, so an ordinary
    /// child would become another stacked row and would grow the panel by the height of the
    /// longest explanation — reintroducing exactly the problem this replaces. Ignored by layout,
    /// it is free-positioned in canvas pixel space beside the hovered row instead.</para>
    ///
    /// <para>Built during <c>Build()</c> rather than lazily on first hover so it is inside the
    /// subtree <c>VRLayers.Apply</c> re-layers at the end of the build; a bubble created later
    /// would stay on the uGUI layer and simply not render for the VR head camera.</para>
    /// </summary>
    private void BuildTooltip()
    {
        if (_root == null)
            return;

        _tipRoot = new GameObject("Tooltip") { layer = 5 };
        _tipRect = _tipRoot.AddComponent<RectTransform>();
        _tipRoot.transform.SetParent(_root.transform, worldPositionStays: false);

        // Anchor at the canvas root's own pivot (bottom-center) so anchoredPosition and the local
        // coordinates we compute from a row's world position share one origin.
        _tipRect.anchorMin = _tipRect.anchorMax = new Vector2(0.5f, 0f);
        _tipRect.pivot = new Vector2(0f, 0.5f);     // grows rightward from the panel edge
        _tipRect.sizeDelta = new Vector2(TipWidthPx, 0f);

        var ignore = _tipRoot.AddComponent<LayoutElement>();
        ignore.ignoreLayout = true;

        var bg = _tipRoot.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.05f, 0.09f, 0.97f);
        bg.raycastTarget = false;                    // inert: never steals the hover that opened it

        var layout = _tipRoot.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        var fitter = _tipRoot.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // width stays TipWidthPx
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;   // height follows the text

        var textGo = new GameObject("Text") { layer = 5 };
        textGo.transform.SetParent(_tipRoot.transform, worldPositionStays: false);
        _tipText = textGo.AddComponent<TextMeshProUGUI>();
        _tipText.fontSize = 13f;
        _tipText.alignment = TextAlignmentOptions.TopLeft;
        _tipText.color = new Color(0.90f, 0.88f, 0.82f);
        _tipText.raycastTarget = false;
        _tipText.enableWordWrapping = true;
        WorldUIAssets.TryAssignGameFont(_tipText);

        _tipRoot.transform.SetAsLastSibling(); // drawn over the rows it sits beside
        _tipRoot.SetActive(false);
    }

    /// <summary>
    /// Give <paramref name="row"/> a hover explanation. Adds the transparent hit target the row
    /// needs (a Row is a layout group with no <see cref="Graphic"/>, so today it cannot be
    /// raycast at all) plus the handler.
    ///
    /// <para>The hit image is the row's BACKGROUND, so it never outranks the row's own buttons:
    /// the graphic raycaster sorts children above their parent and the pointer takes the top hit,
    /// which is still the button. Clicking is unchanged; only hovering gained a receiver.</para>
    ///
    /// <para>Opt-in per row rather than folded into <c>Row()</c>: only a handful of rows have an
    /// explanation worth the extra graphic, and the rest should stay free.</para>
    /// </summary>
    private void Tip(RectTransform row, string locId) => Tip(row, () => Loc.Mod(locId));

    /// <summary>
    /// Same as <see cref="Tip(RectTransform,string)"/>, but with the text resolved by a delegate
    /// instead of a Loc id. The config browser needs this: its rows are a REUSED POOL — the row that
    /// showed "Cull Submit Split" a moment ago describes a different entry after a page turn — so the
    /// explanation has to be produced at hover time from whatever the row is currently bound to. A
    /// fixed string (or a fixed Loc id) would be the previous entry's description.
    /// </summary>
    private void Tip(RectTransform row, Func<string> text)
    {
        if (row == null)
            return;
        GameObject go = row.gameObject;
        if (go.GetComponent<Graphic>() == null)
        {
            var hit = go.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f); // fully transparent still raycasts
            hit.raycastTarget = true;
        }
        var target = go.AddComponent<SettingsTooltipTarget>();
        target.Text = text;
        target.Hover = OnTooltipHover;
    }

    // ---- hover --------------------------------------------------------------------------------

    /// <summary>
    /// Show/hide the bubble. Only the CURRENT owner's exit closes it, so the enter of the next row
    /// followed by the exit of the previous one — a legal order, since the two walks are
    /// independent — cannot leave the panel with no bubble while the beam is resting on a row.
    /// </summary>
    private void OnTooltipHover(SettingsTooltipTarget target, bool entered)
    {
        if (!entered)
        {
            if (ReferenceEquals(_tipOwner, target))
                HideTooltip();
            return;
        }
        if (_tipRoot == null || _tipRect == null || _tipText == null || _root == null)
            return;

        string text = target.Text != null ? target.Text() : string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            HideTooltip();
            return;
        }

        _tipOwner = target;
        _tipText.text = text;
        _tipRoot.SetActive(true);

        // Park it beside the hovered row: the row's world pivot expressed in the canvas root's
        // local (pixel) space gives the vertical line-up, and the horizontal offset is a constant
        // step outside the panel's right edge.
        var rowRect = target.GetComponent<RectTransform>();
        float y = rowRect != null
            ? _root.transform.InverseTransformPoint(rowRect.position).y
            : _tipRect.anchoredPosition.y;

        // LONG, MULTI-LINE EXPLANATIONS (the config browser shows the config file's own paragraphs,
        // and several run to a dozen wrapped lines). The bubble is centred on the row, so a tall one
        // would hang below the panel's bottom edge and read as floating in mid-air. Rebuild the
        // layout NOW — the ContentSizeFitter would otherwise only publish the new height at the end
        // of the frame, and we would be clamping against the PREVIOUS row's bubble — then lift the
        // bubble so its bottom never crosses the canvas root's bottom edge (local y = 0, the root's
        // pivot). One synchronous rebuild of one small subtree, on a hover, is not a per-frame cost.
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRect);
        float halfHeight = _tipRect.rect.height * 0.5f;
        if (y < halfHeight)
            y = halfHeight;

        _tipRect.anchoredPosition = new Vector2(PanelWidthPx * 0.5f + TipGapPx, y);
    }

    private void HideTooltip()
    {
        _tipOwner = null;
        if (_tipRoot != null)
            _tipRoot.SetActive(false);
    }

    /// <summary>
    /// Drop every reference to a bubble whose GameObject is about to die (or already has), so a
    /// rebuild never writes into a destroyed canvas. Called from the same places that null
    /// <c>_root</c>: language rebuild, shutdown, and every close.
    /// </summary>
    private void ResetTooltip()
    {
        _tipOwner = null;
        _tipRoot = null;
        _tipRect = null;
        _tipText = null;
    }
}
