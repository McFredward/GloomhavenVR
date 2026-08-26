using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A PEER'S PICK-STATUS PLACARD — the hovering parchment strip above their control board that
/// reads e.g. "Barbar: Wähle 1 Karte(n) zum Verlieren".
///
/// WHY IT RIDES THE WIRE AT ALL (user request 2026-08-03: "Dieser Text soll auch synchronisiert
/// werden an der jeweiligen richtigen Position im MP"): unlike everything else on the remote board
/// this line is not derivable from replicated game state. It is composed by the OWNER's
/// <c>CardsDriver.UpdatePickStatus</c> out of their hand's live pick mode, the requested count,
/// how many of the current batch they have already placed and whether the game's confirm dialog is
/// up — several of which are local UI state that exists nowhere else. So it is sent verbatim as
/// extension record <see cref="NetProtocol.ExtIdPickBanner"/>.
///
/// WHAT IT DOES NOT LEAK: an actor label and a count — "choose 1 card to lose" — never WHICH card.
/// That is strictly less than the game's own turn banner already tells everyone, and it is the
/// same standing rule the rest of this layer follows (no card identity on the wire; reveals only
/// through <see cref="RevealGate"/>). Nothing here consults a card.
///
/// LANGUAGE: the line arrives already composed, in the SENDER's language, and is shown verbatim.
/// Re-composing it locally would mean shipping the pick mode, the counts and the actor identity as
/// structured fields and rebuilding the sentence — more wire, more coupling, and the result would
/// still name the peer's character. It is their board; it reads in their language.
///
/// WHERE IT SITS: <see cref="RemoteBoardLayout.PickBannerMount"/>, i.e. the owner's own
/// <c>PlayTray.PickBannerBase</c> plus THEIR OWN per-board offset — from extension record 28 when
/// they have moved that dial, and from the shipped constant for their synced board style when they
/// have not (the same derivation every other dock on this board uses). The DELIBERATELY-NOT note
/// that used to stand here — "a peer's private re-tuning of that offset never rides the wire" — was
/// retired with record 28: under the 1:1 ruling a placard the owner has moved must sit where they
/// moved it on every screen.
///
/// LOOK: the owner's own placard is a parchment quad with ink-brown text
/// (<c>PlayTray.EnsurePickBanner</c>) — reproduced here with the same colours through the shared
/// remote-board helpers, so a peer's placard reads like theirs rather than like a mod overlay.
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 7) — sender-composed local UI state that is
/// neither GLOBAL nor PER-ACTOR MODEL, for the reason given above. Its sibling is the board
/// tooltip (<see cref="RemoteBoardTooltip"/>, record 9), which shares the shape but adds a
/// sender-side identity gate. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemotePickBanner
{
    /// <summary>Plate size, metres — the owner's own placard is 0.44 × 0.055 (localScale of a
    /// unit quad in <c>PlayTray.EnsurePickBanner</c>).</summary>
    private static readonly Vector2 PlateSize = new(0.44f, 0.055f);

    /// <summary>Text box inside the plate — the owner fits to 0.42 × 0.048 at font 0.30.</summary>
    private static readonly Vector2 TextBox = new(0.42f, 0.048f);
    private const float MaxFont = 0.30f;

    private readonly Transform _root;
    private readonly TextMeshPro _label;
    private string _shown = string.Empty;

    public RemotePickBanner(Transform boardRoot, in RemoteBoardLayout layout)
    {
        _root = new GameObject("PickStatusBanner").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = layout.PickBannerMount;

        // Parchment plate, seated slightly BEHIND the text toward the board — the same 4 mm the
        // owner's own placard uses, so the text never z-fights its backing.
        MeshRenderer plate = BoardVisual.Quad(_root, "Plate", PlateSize,
            BoardVisual.Unlit(new Color(0.85f, 0.78f, 0.62f, 0.85f)));
        plate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
        // MR readability (user: the text backing must appear on REMOTE boards exactly as on the
        // owner's): the owner's own placard parchment is Opacified (PlayTray.5.Status), so this
        // mirror's 0.85 parchment gets the identical treatment — alpha 1 while MR is on, restored
        // exactly on off. Mod-owned material; normal mode stays bit-identical.
        WorldUI.MrBacking.Opacify(plate.sharedMaterial);

        _label = RemoteBoardContent.Label(_root, "Label", Vector3.zero, TextBox, MaxFont,
            new Color(0.24f, 0.17f, 0.10f), TextAlignmentOptions.Center, wrap: true);

        // DRAW ORDER is NOT set here any more: the placard is seated by the owning board's cluster
        // sweep at the tier its own board-local depth earns (BoardVisual.AdoptBoardOrder). It still
        // lands UNDER the peer's initiative mirror at every viewing angle — the placard hovers at
        // PickBannerBase z = −0.02 and the track is docked at −0.048/−0.070 — but now because that
        // IS the geometry, not because a constant asserted it.
        _root.gameObject.SetActive(false);
    }

    /// <summary>
    /// Push the peer's line (null/empty = their placard is hidden, which is also what a sender
    /// predating record 7 produces). Change-gated on both the active flag and the text: a TMP
    /// rewrite re-triggers auto-size layout every time, which is the churn the local board's own
    /// banner is careful to avoid.
    /// </summary>
    public void Apply(string? line)
    {
        bool show = !string.IsNullOrEmpty(line);
        if (!show)
        {
            if (_shown.Length != 0)
            {
                _shown = string.Empty;
                if (_root != null && _root.gameObject.activeSelf)
                    _root.gameObject.SetActive(false);
                VRLog.Info("Net", "Remote pick banner: hidden (record absent — the owner's placard is down).");
            }
            return;
        }
        if (line == _shown)
            return;
        _shown = line!;
        RemoteBoardContent.SetText(_label, _shown);
        if (_root != null && !_root.gameObject.activeSelf)
            _root.gameObject.SetActive(true);
        VRLog.Info("Net", $"Remote pick banner: \"{_shown}\" at board-local {_root.localPosition:F3}.");
    }
}
