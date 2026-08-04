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
/// WHERE IT SITS: <see cref="RemoteBoardLayout.TooltipMount"/> -- the owner's own
/// <c>PlayTray.TooltipAreaBase</c> (the board's authored top-left corner) plus the SHIPPED
/// per-board offset, with the plate growing up/right from that corner exactly like the owner's
/// own tooltip area does. The same DELIBERATELY-NOT rule as every dock: a peer's private
/// debug-menu re-tuning of the offset never rides the wire.
///
/// LOOK: the same parchment-and-ink family as <see cref="RemotePickBanner"/>, through the same
/// shared remote-board helpers, so a peer's tooltip reads like board furniture rather than a
/// mod overlay. Rich-text markup in the text is rendered by TMP as-is (the sender transmits the
/// game's own line markup verbatim).
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 9) -- sender-composed local UI state, like the
/// pick banner, but IDENTITY-GATED at the source. See INVARIANTS-Net-Rig.md "Net -- content
/// classification".</remarks>
internal sealed class RemoteBoardTooltip
{
    /// <summary>Plate size, metres -- sized for the record's 192-byte cap (~8 wrapped lines at
    /// the text box's fitted font) while staying inside the board's own footprint family.</summary>
    private static readonly Vector2 PlateSize = new(0.34f, 0.17f);

    /// <summary>Text box inset inside the plate.</summary>
    private static readonly Vector2 TextBox = new(0.32f, 0.155f);
    private const float MaxFont = 0.032f;

    /// <summary>Clearance between the board's top edge and the plate's bottom edge -- the remote
    /// mirror of <c>WorldUI.WorldTooltips.BoardAnchorMarginY</c> (board-local metres).</summary>
    private const float MarginY = 0.03f;

    private readonly Transform _root;
    private readonly TextMeshPro _label;
    private string _shown = string.Empty;

    public RemoteBoardTooltip(Transform boardRoot, in RemoteBoardLayout layout)
    {
        _root = new GameObject("BoardTooltip").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        // The mount is the AREA ORIGIN (top-left corner); the plate is seated so its bottom-left
        // corner sits (MarginY) above it -- a panel that STARTS top-left and grows up/right into
        // open air, the same contract the owner's local area follows.
        _root.localPosition = layout.TooltipMount
                              + new Vector3(PlateSize.x * 0.5f, MarginY + PlateSize.y * 0.5f, 0f);

        // Parchment plate, seated slightly BEHIND the text toward the board -- the same 4 mm the
        // pick banner uses, so the text never z-fights its backing.
        MeshRenderer plate = BoardVisual.Quad(_root, "Plate", PlateSize,
            BoardVisual.Unlit(new Color(0.85f, 0.78f, 0.62f, 0.90f)));
        plate.transform.localPosition = new Vector3(0f, 0f, 0.004f);

        _label = RemoteBoardContent.Label(_root, "Label", Vector3.zero, TextBox, MaxFont,
            new Color(0.24f, 0.17f, 0.10f), TextAlignmentOptions.TopLeft, wrap: true);

        // TOOLTIP tier of the remote board's fixed sub-ladder: proud of the board, annotating
        // its content, so it must beat both the furniture and the docked widgets it overlaps
        // (the initiative mirror spans the whole top edge, this plate grows up at top-left) at
        // every viewing angle -- see BoardVisual's sub-ladder header. Text over plate keeps
        // resolving via the 4 mm z offset, as before.
        plate.sortingOrder = BoardVisual.OrderTooltip;
        _label.sortingOrder = BoardVisual.OrderTooltip;

        _root.gameObject.SetActive(false);
    }

    /// <summary>
    /// Push the peer's tooltip text (null/empty = hidden, which is also what a sender predating
    /// record 9 -- or one whose identity gate is suppressing -- produces). Change-gated on both
    /// the active flag and the text: a TMP rewrite re-triggers auto-size layout every time.
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
        RemoteBoardContent.SetText(_label, _shown);
        if (_root != null && !_root.gameObject.activeSelf)
            _root.gameObject.SetActive(true);
        VRLog.Info("Net", $"Remote board tooltip: {_shown.Length} chars at board-local {_root.localPosition:F3} " +
                          "(the remote board's tooltip area, top-left).");
    }
}
