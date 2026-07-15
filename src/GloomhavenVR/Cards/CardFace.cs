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

    internal bool IsAdopted => _face != null;

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
        _face.anchorMin = new Vector2(0.5f, 0.5f);
        _face.anchorMax = new Vector2(0.5f, 0.5f);
        _face.pivot = new Vector2(0.5f, 0.5f);
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
            // somewhere foreign — YIELD, never fight a dialog; the next HandShown
            // rebuild re-adopts the face after the dialog resolves.
            if (_face.parent == _origParent || (_owner != null && _face.IsChildOf(_owner.transform)))
            {
                ApplyHostPose();
            }
            else
            {
                VRLog.Debug("Cards", $"CardFace yielded to game dialog ({_face.parent?.name ?? "null"}).");
                Yield();
            }
            return;
        }
        if (!_face.gameObject.activeSelf)
            _face.gameObject.SetActive(true);
        if (_face.anchoredPosition3D != Vector3.zero)
            _face.anchoredPosition3D = Vector3.zero;
        float scale = _face.localScale.x;
        if (!Mathf.Approximately(scale, _fitScale))
            _face.localScale = new Vector3(_fitScale, _fitScale, _fitScale);
    }

    /// <summary>
    /// Let go of the face WITHOUT touching its transform (a game dialog owns it now).
    /// Only the LockFullCard flag is returned.
    /// </summary>
    internal void Yield()
    {
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
