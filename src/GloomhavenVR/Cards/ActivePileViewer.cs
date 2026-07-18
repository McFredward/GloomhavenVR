using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// The ACTIVE CARDS display area (feature 6, [Cards] ActivePile): a permanently-visible
/// compact column of the character's currently-active ability cards (round-long or
/// persistent — <c>CardPileType.Active</c>), docked off the board's RIGHT edge just past
/// the discard/burnt pile stacks (<see cref="PlayTray.ActiveMount"/>). Unlike the pile
/// <see cref="PileBrowser"/> it is NOT opened/closed on demand — it simply mirrors the
/// active pile whenever there is one, and shows nothing when the pile is empty. Each card
/// is rendered slightly SMALLER than the hand/browse cards (<see cref="CardScale"/>) and
/// stays grabbable so the player can pluck one out to read it, then it returns to the
/// column on release (the driver routes both, exactly like the browse arc). Purely
/// informational: adopting/plucking an active card never commits or selects it. The
/// active HALF/halves of each card carry a translucent highlight (VRCard.SetActiveHighlight,
/// driven from CardsGameApi.GetActiveHalves) so what is active is visible at a glance.
/// Layout only; content, highlight and lifecycle are driven by <see cref="CardsDriver"/>.
/// </summary>
internal sealed class ActivePileViewer
{
    /// <summary>Active cards read slightly smaller than the hand/browse fan (PileBrowser.CardScale = 1.3).</summary>
    internal const float CardScale = 0.82f;

    // Vertical column geometry: cards stack downward with a slight overlap (the lower a
    // card, the nearer the viewer, so its top covers the card above's bottom cleanly).
    private const float RowSpacingFactor = 0.7f; // fraction of the scaled card height between rows
    private const float ZStagger = 0.004f;       // render-order stagger, same as CardFan/PileBrowser

    private readonly List<VRCard> _cards = new(8);
    private Transform? _root;
    private TextMeshPro? _title;

    internal bool IsShown => _root != null && _root.gameObject.activeSelf;

    internal bool Contains(VRCard card) => _cards.Contains(card);

    // ------------------------------------------------------------------ lifecycle --

    /// <summary>Localized caption for the area (safe English fallback).</summary>
    internal static string Caption() => CardsGameApi.Localize("GUI_ACTIVE", "Active");

    internal void EnsureBuilt(PlayTray tray)
    {
        Transform? mount = tray.ActiveMount;
        if (mount == null)
            return;
        // A tray teardown destroys the column with the mount — the Unity fake-null makes
        // this == check true and the column rebuilds from scratch under the fresh mount.
        if (_root != null)
            return;

        // Rebuilding under a fresh mount: any prior card refs are stale (parked by the
        // board-switch / released with the old tray). The driver repopulates via SetCards.
        _cards.Clear();

        _root = new GameObject("GloomhavenVR.ActivePile").transform;
        _root.SetParent(mount, worldPositionStays: false);
        Core.VRLayers.Apply(_root.gameObject); // cards apply themselves in VRCard.Build

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(_root, worldPositionStays: false);
        titleGo.transform.localPosition = new Vector3(0f, 0.075f, -0.0025f); // above the column, viewer side (-Z)
        _title = titleGo.AddComponent<TextMeshPro>();
        _title.text = Caption().ToUpperInvariant();
        _title.alignment = TextAlignmentOptions.Center;
        _title.color = new Color(1f, 0.9f, 0.6f);
        WorldUI.NativeButtonSkin.ApplyFont(_title); // native HUD font, like the pile captions
        Core.TmpFit.Fit(_title, 0.09f, 0.024f, maxFontSize: 0.22f, wrap: false);
    }

    internal void SetVisible(bool visible)
    {
        if (_root != null && _root.gameObject.activeSelf != visible)
            _root.gameObject.SetActive(visible);
    }

    internal void Destroy()
    {
        _cards.Clear();
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
            _title = null; // child of _root, destroyed with it
        }
    }

    // ------------------------------------------------------------------ content --

    /// <summary>
    /// Replace the shown card set (driver rebuild path; cards fly to their column slots).
    /// The driver has already adopted the cards and set their per-card active highlight.
    /// </summary>
    internal void SetCards(List<VRCard> cards)
    {
        _cards.Clear();
        for (int i = 0; i < cards.Count; i++)
            _cards.Add(cards[i]);
        Relayout(instant: false);
    }

    /// <summary>Drop one card (widget recycled under us); the column closes the gap.</summary>
    internal void Remove(VRCard card)
    {
        if (_cards.Remove(card))
            Relayout(instant: false);
    }

    /// <summary>
    /// Return a card plucked out for a close read back into the column — the active-area
    /// counterpart of <see cref="PileBrowser.Add"/>. Purely informational: the release
    /// routes here, never to a select/slot seam.
    /// </summary>
    internal void Add(VRCard card)
    {
        if (!_cards.Contains(card))
            _cards.Add(card);
        Relayout(instant: false);
    }

    // ------------------------------------------------------------------ layout --

    private void Relayout(bool instant)
    {
        if (_root == null)
            return;
        int n = _cards.Count;
        if (n == 0)
            return;

        float step = CardsConfig.CardHeight * CardScale * RowSpacingFactor;
        float start = step * (n - 1) * 0.5f; // centered column, first card highest

        for (int i = 0; i < n; i++)
        {
            VRCard card = _cards[i];
            if (card == null || card.IsHeld)
                continue;
            if (!card.gameObject.activeSelf)
                card.gameObject.SetActive(true);

            // Lower cards sit nearer the viewer (-Z) so their tops overlap the card above.
            var pos = new Vector3(0f, start - step * i, -ZStagger * i);
            card.SetHome(_root, pos, Quaternion.identity, CardScale, instant);
            card.ResetColliderRegion(); // active cards are not fan-stripped
        }
    }
}
