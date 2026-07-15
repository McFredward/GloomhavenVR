using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// The palm fan (Demeo hand): an animated arc of <see cref="VRCard"/>s hovering above
/// the non-dominant palm, shown/hidden by the P2 <c>PalmGate</c>. Cards face the HMD,
/// overlap slightly, and the grab-candidate card pops forward (VRCard handles the pop
/// animation via <c>IGrabHighlight</c>). Pure layout — interaction routing lives in
/// <see cref="CardsDriver"/>. No allocations in <see cref="Tick"/>.
/// </summary>
internal sealed class CardFan
{
    private readonly List<VRCard> _cards = new(16);
    private Transform? _root;
    private VRHand? _hand;

    internal bool IsOpen { get; private set; }

    /// <summary>Cards currently owned by the fan (read-only view).</summary>
    internal IReadOnlyList<VRCard> Cards => _cards;

    internal bool Contains(VRCard card) => _cards.Contains(card);

    // ------------------------------------------------------------------ lifecycle --

    internal void Open(VRHand hand)
    {
        _hand = hand;
        if (_root == null)
        {
            _root = new GameObject("GloomhavenVR.CardFan").transform;
        }
        _root.SetParent(hand.Rig.PalmCenter, worldPositionStays: false);
        _root.gameObject.SetActive(true);
        IsOpen = true;
        Relayout(instant: true);
    }

    internal void Close()
    {
        IsOpen = false;
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    internal void Destroy()
    {
        _cards.Clear();
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
        }
        IsOpen = false;
    }

    // ------------------------------------------------------------------ content --

    /// <summary>Replace the fan's card set (called on rebuilds; cards fly to their arc slots).</summary>
    internal void SetCards(List<VRCard> cards)
    {
        _cards.Clear();
        for (int i = 0; i < cards.Count; i++)
            _cards.Add(cards[i]);
        if (IsOpen)
            Relayout(instant: false);
    }

    /// <summary>Remove a card (grabbed away); remaining cards close the gap.</summary>
    internal void Remove(VRCard card)
    {
        if (_cards.Remove(card) && IsOpen)
            Relayout(instant: false);
    }

    /// <summary>Return a card to the fan (release outside a drop zone) — animated.</summary>
    internal void Add(VRCard card)
    {
        if (!_cards.Contains(card))
            _cards.Add(card);
        if (IsOpen)
            Relayout(instant: false);
    }

    // ------------------------------------------------------------------ per frame --

    /// <summary>Orient the fan toward the head every frame while open.</summary>
    internal void Tick()
    {
        if (!IsOpen || _root == null || _hand == null)
            return;

        // Pivot floats above the palm along the palm normal (+Y of PalmCenter).
        _root.localPosition = new Vector3(0f, CardsConfig.FanPalmOffset.Value, 0f);

        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Vector3 away = _root.position - head.transform.position;
        if (away.sqrMagnitude < 1e-6f)
            return;
        // Cards' +Z points away from the viewer (uGUI reads from -Z).
        _root.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    // ------------------------------------------------------------------ layout --

    private void Relayout(bool instant)
    {
        if (_root == null)
            return;

        int n = _cards.Count;
        if (n == 0)
            return;

        float radius = CardsConfig.FanRadius.Value;
        float maxArc = CardsConfig.FanArcDegrees.Value;
        // Slight overlap: per-card step shrinks as the hand grows, capped by maxArc.
        float step = n > 1 ? Mathf.Min(11f, maxArc / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

        for (int i = 0; i < n; i++)
        {
            VRCard card = _cards[i];
            if (card == null || card.IsHeld)
                continue;
            if (!card.gameObject.activeSelf)
                card.gameObject.SetActive(true);

            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            // Arc bends around a pivot below the fan root; small z-stagger keeps the
            // draw order stable (later cards nearer the viewer = -Z).
            var pos = new Vector3(Mathf.Sin(rad) * radius,
                                  (Mathf.Cos(rad) - 1f) * radius * 0.55f,
                                  -0.0018f * i);
            var rot = Quaternion.Euler(0f, 0f, -angle * 0.85f);
            card.SetHome(_root, pos, rot, 1f, instant);
        }
    }
}
