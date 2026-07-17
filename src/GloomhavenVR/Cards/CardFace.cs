using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Adoption of a live game card face onto a VR card, with full restore.
///
/// Mechanism (ARCHITECTURE §5 "golden seam" #4, CARDS.md §3/§5): every
/// <c>AbilityCardUI</c> owns a self-contained <c>FullAbilityCard</c> child with its
/// own Canvas (<c>public Canvas Canvas</c>, FullAbilityCard.cs:96). The game itself
/// re-parents these at runtime — <c>CardsHandUI.ToggleFullCardsPreview</c> moves whole
/// cards into <c>FullCardHandViewer.CardContainer</c> (CardsHandUI.cs:345) and the
/// dialog flows re-parent <c>fullAbilityCard.gameObject</c> into popups
/// (CardsHandUI.cs:826/2069) — so re-hosting the face is a game-sanctioned operation.
/// We move ONLY the FullAbilityCard rect under a world-space canvas on the VR card;
/// the AbilityCardUI root (mini card, buttons, layout) stays in the suppressed 2D
/// hand so all game bookkeeping keeps working.
///
/// While adopted:
/// - <c>AbilityCardUI.LockFullCard = true</c> (verified property, AbilityCardUI.cs:163)
///   short-circuits <c>ToggleFullCardPreview</c> (early-out at AbilityCardUI.cs:1033),
///   preventing the game's hover preview from adding Canvases / moving the face.
/// - <see cref="Maintain"/> re-asserts parent/active/rect once per frame because
///   <c>UpdateView/ToggleFullCard</c> (CardsHandUI.cs:993) rewrite anchors and
///   deactivate the face whenever the game refreshes the (invisible) 2D hand.
///
/// <see cref="Restore"/> puts every captured value back — REQUIRED before the widget
/// returns to the game's object pool (see Patches/CardLifecyclePatches.cs).
/// </summary>
internal sealed class CardFace
{
    /// <summary>Centered anchor frame the host pose uses (anchors AND pivot).</summary>
    private static readonly Vector2 CenterAnchor = new(0.5f, 0.5f);

    private AbilityCardUI? _owner;
    private RectTransform? _face;
    private RectTransform? _host;

    // Captured original state.
    private Transform? _origParent;
    private int _origSibling;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot;
    private Vector3 _origLocalScale;
    private Vector2 _origAnchorMin;
    private Vector2 _origAnchorMax;
    private Vector2 _origPivot;
    private Vector2 _origAnchoredPos;
    private bool _origActive;
    private bool _origLock;

    private float _fitScale = 1f;

    // Test #22: change-dedup for the burn/lose-confirm re-claim log (Maintain).
    private bool _reclaimedFromDialog;

    internal bool IsAdopted => _face != null;

    /// <summary>
    /// True once the face was re-claimed from a burn/lose/discard confirm popup back
    /// onto our dock (see <see cref="Maintain"/>). Purely informational.
    /// </summary>
    internal bool ReclaimedFromDialog => _reclaimedFromDialog;

    internal AbilityCardUI? Owner => _owner;

    /// <summary>Pixel size of the face rect (for host canvas sizing).</summary>
    internal Vector2 FaceSize { get; private set; } = new(270f, 400f);

    /// <summary>
    /// Re-parent the live face under <paramref name="host"/> (a world-space canvas
    /// rect). Returns false if the widget has no usable face.
    /// </summary>
    internal bool Adopt(AbilityCardUI owner, RectTransform host)
    {
        if (owner == null || owner.fullAbilityCard == null)
            return false;

        RectTransform? face = owner.fullAbilityCard.RectTransform;
        if (face == null)
            face = owner.fullAbilityCard.transform as RectTransform;
        if (face == null)
            return false;

        _owner = owner;
        _face = face;
        _host = host;
        _reclaimedFromDialog = false;

        _origParent = face.parent;
        _origSibling = face.GetSiblingIndex();
        _origLocalPos = face.localPosition;
        _origLocalRot = face.localRotation;
        _origLocalScale = face.localScale;
        _origAnchorMin = face.anchorMin;
        _origAnchorMax = face.anchorMax;
        _origPivot = face.pivot;
        _origAnchoredPos = face.anchoredPosition;
        _origActive = face.gameObject.activeSelf;
        _origLock = owner.LockFullCard;

        owner.LockFullCard = true;

        Vector2 size = face.rect.size;
        if (size.x > 1f && size.y > 1f)
            FaceSize = size;
        _fitScale = ComputeFitScale(host, FaceSize);

        ApplyHostPose();
        return true;
    }

    private static float ComputeFitScale(RectTransform host, Vector2 faceSize)
    {
        Vector2 hostSize = host.rect.size;
        if (hostSize.x <= 0f || hostSize.y <= 0f)
            return 1f;
        return Mathf.Min(hostSize.x / faceSize.x, hostSize.y / faceSize.y);
    }

    private void ApplyHostPose()
    {
        if (_face == null || _host == null)
            return;
        _face.SetParent(_host, worldPositionStays: false);
        _face.anchorMin = CenterAnchor;
        _face.anchorMax = CenterAnchor;
        _face.pivot = CenterAnchor;
        _face.anchoredPosition3D = Vector3.zero;
        _face.localRotation = Quaternion.identity;
        _face.localScale = new Vector3(_fitScale, _fitScale, _fitScale);
        if (!_face.gameObject.activeSelf)
            _face.gameObject.SetActive(true);
    }

    /// <summary>
    /// Per-frame guard (no allocations): the game rewrites face anchors/active state
    /// whenever it refreshes the suppressed 2D hand — put our pose back.
    /// </summary>
    internal void Maintain()
    {
        if (_face == null || _host == null)
            return;
        if (_owner == null || _owner.fullAbilityCard == null)
        {
            // Widget died under us (scene teardown) — drop references.
            _face = null;
            _owner = null;
            return;
        }

        if (!ReferenceEquals(_face.parent, _host))
        {
            // Who moved it? A hand-layout refresh re-parents the face back under its
            // own AbilityCardUI (reclaim it). A game DIALOG (burn/redraw popups take
            // fullAbilityCard.gameObject, CardsHandUI.cs:826/2069) re-parents it
            // somewhere foreign.
            if (_face.parent == _origParent || (_owner != null && _face.IsChildOf(_owner.transform)))
            {
                ApplyHostPose();
            }
            // BURN/LOSE/DISCARD CONFIRM (test #22, symptom 4b): the game passes the
            // LIVE face straight to <c>DialogPopup.Show(fullAbilityCard.gameObject)</c>
            // (CardsHandUI.cs:826/850/909/2069), which re-parents it under the popup's
            // <c>contentHolder</c> WITHOUT the healthy full-card preview prep
            // (ToggleFullCard / ToggleFullCardCanvasSorting / a CardEffects _PosAndBounds
            // refresh — CardsHandUI.cs:340-345). On our world-space modal float the
            // card's custom screen-space card shader then resolves to DEEP BLACK, its
            // hover FX Image quads blow up to the popup canvas scale, and the burn flame
            // overlay reads as a fullscreen sheet (symptoms 4b/4c). We do NOT float that
            // popup content: RE-CLAIM the face onto our own known-good FaceCanvas (the
            // identical pipeline that renders every other hand/dock card correctly) so
            // the card stays readable ON the action-selection dock and the burn effect
            // plays on the card mesh. The popup's own yes/no buttons still float and
            // commit the burn; its now-empty contentHolder is harmless, and DialogPopup
            // restores the face to this same parent on Hide (PreviousState, verified
            // DialogPopup.cs:38-46). Keyed purely off widget state (a DialogPopup in the
            // parent chain), never off the modal-dock code.
            else if (IsDialogContent(_face.parent))
            {
                if (!_reclaimedFromDialog)
                {
                    _reclaimedFromDialog = true;
                    VRLog.Info("Cards", "CardFace re-claimed the burn/lose confirm card onto the " +
                                        "action-selection dock (kept off the modal float where the " +
                                        "screen-space card shader renders black).");
                }
                ApplyHostPose();
            }
            else
            {
                // Any other foreign parent — YIELD, never fight it; the next HandShown
                // rebuild re-adopts the face after the flow resolves.
                VRLog.Debug("Cards", $"CardFace yielded to game dialog ({_face.parent?.name ?? "null"}).");
                Yield();
            }
            return;
        }
        if (!_face.gameObject.activeSelf)
            _face.gameObject.SetActive(true);
        // Re-assert the FULL anchor frame, not just the anchored position (test #19
        // x-offset): on ActionSelection entry the game re-anchors the face rect —
        // <c>AbilityCardUI.ToggleFullCard(active: true)</c> sets
        // <c>anchorMin = anchorMax = (0, 0.5)</c>, LEFT-middle (AbilityCardUI.cs:
        // 1000-1002, verified ilspycmd). With only anchoredPosition3D restored, the
        // face's pivot then sat on the host's LEFT EDGE — the card art (and the
        // backing fitted to it) rendered half a card left of the slot frame.
        if (_face.anchorMin != CenterAnchor)
            _face.anchorMin = CenterAnchor;
        if (_face.anchorMax != CenterAnchor)
            _face.anchorMax = CenterAnchor;
        if (_face.pivot != CenterAnchor)
            _face.pivot = CenterAnchor;
        if (_face.anchoredPosition3D != Vector3.zero)
            _face.anchoredPosition3D = Vector3.zero;
        if (_face.localRotation != Quaternion.identity)
            _face.localRotation = Quaternion.identity;
        float scale = _face.localScale.x;
        if (!Mathf.Approximately(scale, _fitScale))
            _face.localScale = new Vector3(_fitScale, _fitScale, _fitScale);
    }

    /// <summary>
    /// True when <paramref name="parent"/> lives inside a <c>DialogPopup</c> — i.e. the
    /// game handed our face to a burn/lose/discard confirm popup (the only flow that
    /// re-parents a live <c>fullAbilityCard.gameObject</c> into a DialogPopup, verified
    /// CardsHandUI.cs:826/850/909/2069). Full-card PREVIEW re-parents into
    /// <c>FullCardHandViewer.CardContainer</c> instead — and is short-circuited here
    /// anyway by <c>LockFullCard</c> — so this never mis-fires on a preview.
    /// </summary>
    private static bool IsDialogContent(Transform? parent) =>
        parent != null && parent.GetComponentInParent<DialogPopup>() != null;

    /// <summary>
    /// Let go of the face WITHOUT touching its transform (a game dialog owns it now).
    /// Only the LockFullCard flag is returned.
    /// </summary>
    internal void Yield()
    {
        _reclaimedFromDialog = false;
        if (_owner != null)
            _owner.LockFullCard = _origLock;
        _face = null;
        _host = null;
        _owner = null;
    }

    /// <summary>Give the face back to the game exactly as captured.</summary>
    internal void Restore()
    {
        RectTransform? face = _face;
        AbilityCardUI? owner = _owner;
        _face = null;
        _host = null;
        _owner = null;
        _reclaimedFromDialog = false;

        if (face == null)
            return;

        // Owner/parent may already be destroyed during scene teardown.
        if (owner != null)
            owner.LockFullCard = _origLock;

        if (_origParent == null)
            return;

        try
        {
            face.SetParent(_origParent, worldPositionStays: false);
            face.SetSiblingIndex(_origSibling);
            face.anchorMin = _origAnchorMin;
            face.anchorMax = _origAnchorMax;
            face.pivot = _origPivot;
            face.anchoredPosition = _origAnchoredPos;
            face.localPosition = _origLocalPos;
            face.localRotation = _origLocalRot;
            face.localScale = _origLocalScale;
            face.gameObject.SetActive(_origActive);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"CardFace.Restore partial failure (teardown race is benign): {ex.Message}");
        }
    }
}
