using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A PEER'S SYNCED BOARD TOOLTIP -- the text panel at the remote control board's TOOLTIP AREA
/// (top-left corner) that shows what that player is currently reading on their own board
/// (user request 2026-08-04: "Auch Tooltipps sollen im Multiplayer synchronisiert werden und
/// vollstaendig am Remote-Board angezeigt werden").
///
/// WHY IT RIDES THE WIRE AT ALL: like the pick banner, a tooltip's text is composed by the
/// OWNER's client out of what THEY hover -- local UI state that exists nowhere else, not
/// derivable from replicated game state. So it is sent verbatim as extension record
/// <see cref="NetProtocol.ExtIdBoardTooltip"/>.
///
/// WHAT IT DOES NOT LEAK -- THE IDENTITY GATE, which distinguishes this record from the pick
/// banner: a tooltip CAN name a card (hovering an ability card surfaces its title and effect
/// text), and the standing rule is absolute -- no card identity on the wire, ever; reveals only
/// through <see cref="RevealGate"/>. The gate therefore lives on the SENDER
/// (<c>WorldUI.WorldTooltips</c>, the only place that knows what the tooltip is anchored to):
/// only content already public to peers is ever transmitted, and ambiguity suppresses. By the
/// time bytes reach this class they are public by construction; this class is pure display.
///
/// LANGUAGE: the text arrives already composed, in the SENDER's language, and is shown verbatim
/// -- the same argument as the pick banner (it is their board; it reads in their language).
///
/// ─── 1:1 GEOMETRY (hardware MP test 2026-08-04, "die Overlay-Tooltips am Remote-Board sind
/// kaputt -- 1:1 wie beim Spieler selbst") ─────────────────────────────────────────────────────
/// The first shipped revision drew a FIXED 0.34 × 0.17 m parchment field with the text auto-sized
/// under a 0.032 max font -- TMP world metrics are fontSize/10 ≈ line height in metres
/// (<see cref="TmpFit"/>), so that cap allowed at most 3 mm lines: microscopic text in the
/// top-left corner of a huge, mostly EMPTY beige plate that grew straight up over the initiative
/// mirror and washed its portraits out. Nothing about that mirrored the owner's tooltip, which is
/// the game's own frame CONTENT-FITTED around the text.
///
/// The mirror now reproduces the owner's presentation by construction:
///   • SAME METRE-PER-PIXEL: the owner's tooltip canvas renders at CanvasScaleMm(1) × 0.001 ×
///     board scale × 0.5 = <see cref="MetersPerUiPixel"/> board-local m/px
///     (<c>WorldUI.WorldTooltips.LateTick</c>'s scale derivation; the board scale factors out
///     because this whole board root is scaled by the synced BoardScale). The peer's private
///     HoverInfoScale dial is DELIBERATELY-NOT applied, like every local tuning on this board.
///   • SAME TEXT SCALE: the game's tooltip type is ~16 px (<c>UITooltip.m_PCTitleFontSize</c>)
///     ⇒ fixed TMP font <see cref="FontSize"/> (16 px × m/px × 10), never auto-shrunk.
///   • SAME FOOTPRINT RULE: the plate is fitted to the TEXT (measured via
///     <c>TMP_Text.GetPreferredValues</c> under the game's default frame width,
///     <c>UITooltip.m_DefaultWidth</c> = 257 px, widened only as far as the wrapped text needs)
///     plus a frame pad -- a compact box that hugs its content, exactly like the owner's frame.
///   • SAME INK: the game tooltip is light type on a DARK frame (m_TitleFontColor = white on the
///     dark frame sprite); the old beige-parchment/ink-brown scheme was the pick banner's, not
///     the tooltip's, and it is what read as "washed out" over the portraits.
///   • SAME AREA CONTRACT: the plate's bottom-left corner seats <see cref="MarginY"/> above the
///     area origin and the box grows UP/RIGHT into open air -- the local
///     <c>TryGetBoardAreaPose</c> "starts top-left" contract -- so a compact tooltip no longer
///     reaches over the initiative mirror at all; when a tall one legitimately does, the
///     compositing tier below resolves it.
///
/// WHERE IT SITS: <see cref="RemoteBoardLayout.TooltipMount"/> -- the owner's own
/// <c>PlayTray.TooltipAreaBase</c> (the board's authored top-left corner, already proud of the
/// board face) plus the SHIPPED per-board offset. The same DELIBERATELY-NOT rule as every dock:
/// a peer's private debug-menu re-tuning of the offset never rides the wire.
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 9) -- sender-composed local UI state, like the
/// pick banner, but IDENTITY-GATED at the source. See INVARIANTS-Net-Rig.md "Net -- content
/// classification".</remarks>
internal sealed class RemoteBoardTooltip
{
    /// <summary>Board-local metres per uGUI pixel of the owner's tooltip canvas: CanvasScaleMm
    /// default (1) × 0.001 × 0.5 (the WorldTooltips halving) -- the board-scale factor is carried
    /// by the board root's own synced scale.</summary>
    private const float MetersPerUiPixel = 0.0005f;

    /// <summary>Fixed TMP font size: the game's ~16 px tooltip type at the canvas metric above
    /// (TMP world line height ≈ fontSize / 10 ⇒ 16 px × 0.0005 m/px × 10 = 0.08 → 8 mm lines at
    /// board scale 1, exactly the owner's). Never auto-shrunk -- the PLATE fits the text, not the
    /// text the plate.</summary>
    private const float FontSize = 0.08f;

    /// <summary>Wrap width, board-local metres: the game's authored default frame width
    /// (<c>UITooltip.m_DefaultWidth</c> = 257 px) at the canvas metric. Narrow content yields a
    /// narrower plate (the box hugs the text); longer content wraps at this width and grows UP,
    /// like the owner's frame.</summary>
    private const float MaxTextWidth = 257f * MetersPerUiPixel;

    /// <summary>Hard ceiling on the fitted text height (the 192-byte wire cap bounds the content
    /// anyway; this only guards a pathological all-newline text). Overflow truncates.</summary>
    private const float MaxTextHeight = 0.22f;

    /// <summary>Frame pad between the text and the plate edge, metres (~20 px of frame art on the
    /// game's own tooltip at this metric).</summary>
    private const float FramePad = 0.010f;

    /// <summary>Clearance between the board's top edge and the plate's bottom edge -- the remote
    /// mirror of <c>WorldUI.WorldTooltips.BoardAnchorMarginY</c> (board-local metres).</summary>
    private const float MarginY = 0.03f;

    /// <summary>The game tooltip's frame ink: near-black warm dark, opaque enough to read over
    /// anything, dark enough never to wash out what it overlaps.</summary>
    private static readonly Color PlateInk = new(0.09f, 0.075f, 0.06f, 0.94f);

    /// <summary>The game tooltip's type colour (m_TitleFontColor is white; a hair warm here so it
    /// sits in the board's palette without dropping legibility).</summary>
    private static readonly Color TextInk = new(0.93f, 0.90f, 0.83f);

    private readonly Transform _root;
    private readonly Transform _plate;
    private readonly TextMeshPro _label;
    private string _shown = string.Empty;

    public RemoteBoardTooltip(Transform boardRoot, in RemoteBoardLayout layout)
    {
        // The root IS the area origin (the authored top-left corner, proud of the board face);
        // Apply() lays the fitted box out in root-local space, growing up/right from it.
        _root = new GameObject("BoardTooltip").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = layout.TooltipMount;

        // Dark frame plate, seated slightly BEHIND the text toward the board -- the same 4 mm the
        // pick banner uses, so the text never z-fights its backing. Sized per Apply().
        MeshRenderer plate = BoardVisual.Quad(_root, "Plate", Vector2.one, BoardVisual.Unlit(PlateInk));
        _plate = plate.transform;
        // MR readability (user: the text backing must cover REMOTE boards too): the owner reads
        // their tooltip on a CONVERTED panel, which MrBacking's panel sweep plates opaquely in MR;
        // this mod-drawn mirror is no panel, so its 0.94 frame ink is Opacified instead — alpha 1
        // while MR is on, restored exactly on off. Normal mode stays bit-identical.
        WorldUI.MrBacking.Opacify(plate.sharedMaterial);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(_root, worldPositionStays: false);
        _label = labelGo.AddComponent<TextMeshPro>();
        _label.alignment = TextAlignmentOptions.TopLeft;
        _label.color = TextInk;
        // FIXED font at the owner's own text metric -- the whole point of the rewrite (see the
        // class doc): the box will be fitted around the text, so auto-sizing must stay off or a
        // long tooltip would silently shrink below the owner's presentation again.
        _label.enableAutoSizing = false;
        _label.fontSize = FontSize;
        _label.enableWordWrapping = true;
        _label.overflowMode = TextOverflowModes.Truncate;
        _label.richText = true; // the sender transmits the game's own line markup verbatim

        // TOOLTIP tier of the remote board's fixed sub-ladder: proud of the board, annotating
        // its content, so it must beat both the furniture and the docked widgets it overlaps
        // (the initiative mirror spans the whole top edge; a TALL tooltip can still grow over
        // it) at every viewing angle -- see BoardVisual's sub-ladder header. Text over plate
        // keeps resolving via the 4 mm z offset, as before.
        plate.sortingOrder = BoardVisual.OrderTooltip;
        _label.sortingOrder = BoardVisual.OrderTooltip;

        _root.gameObject.SetActive(false);
    }

    /// <summary>
    /// Push the peer's tooltip text (null/empty = hidden, which is also what a sender predating
    /// record 9 -- or one whose identity gate is suppressing -- produces). Change-gated on both
    /// the active flag and the text: a rewrite re-runs the measure + layout pass below.
    /// </summary>
    public void Apply(string? text)
    {
        bool show = !string.IsNullOrEmpty(text);
        if (!show)
        {
            if (_shown.Length != 0)
            {
                _shown = string.Empty;
                if (_root != null && _root.gameObject.activeSelf)
                    _root.gameObject.SetActive(false);
                VRLog.Info("Net", "Remote board tooltip: hidden (record absent -- the owner's tooltip is down).");
            }
            return;
        }
        if (text == _shown)
            return;
        _shown = text!;
        Layout(_shown);
        if (_root != null && !_root.gameObject.activeSelf)
            _root.gameObject.SetActive(true);
    }

    /// <summary>
    /// CONTENT-FITTED layout (the giant-plate fix): measure the text at the owner's fixed type
    /// metric under the game's default frame width, then size the plate to text + frame pad and
    /// seat the box so its BOTTOM-LEFT corner sits <see cref="MarginY"/> above the area origin --
    /// a panel that STARTS top-left and grows up/right into open air, the exact contract the
    /// owner's local area follows (<c>WorldTooltips.TryGetBoardAreaPose</c>).
    /// </summary>
    private void Layout(string text)
    {
        _label.text = text;
        // Preferred size under the wrap-width constraint: narrow content reports its own width
        // (the plate then hugs it), long content wraps at the owner's default frame width.
        Vector2 pref = _label.GetPreferredValues(text, MaxTextWidth, 0f);
        // Floors are ONE line height (TMP world line ≈ fontSize / 10): a degenerate measure must
        // never collapse the plate to a sliver, and no real text is narrower than a glyph line.
        float oneLine = FontSize * 0.1f;
        float textW = Mathf.Clamp(pref.x, oneLine, MaxTextWidth);
        float textH = Mathf.Clamp(pref.y, oneLine, MaxTextHeight);
        _label.rectTransform.sizeDelta = new Vector2(textW, textH);

        float plateW = textW + 2f * FramePad;
        float plateH = textH + 2f * FramePad;
        // Root-local frame: origin = area origin; +X right, +Y up; -Z toward the viewer.
        var center = new Vector3(plateW * 0.5f, MarginY + plateH * 0.5f, 0f);
        _plate.localScale = new Vector3(plateW, plateH, 1f);
        _plate.localPosition = center + new Vector3(0f, 0f, 0.004f);
        _label.transform.localPosition = center;

        VRLog.Info("Net", $"Remote board tooltip: {text.Length} chars -> plate " +
                          $"{plateW:F3}x{plateH:F3} m (content-fitted at the owner's text metric, " +
                          $"font {FontSize:F2} = {FontSize * 100f:F0} mm lines, wrap {MaxTextWidth:F3} m), " +
                          $"bottom-left {MarginY:F3} m above the area origin at board-local " +
                          $"{_root.localPosition:F3} -- grows up/right like the owner's own frame.");
    }
}
