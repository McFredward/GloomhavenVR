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
            Core.VRLayers.Apply(_root.gameObject); // cards Apply themselves in VRCard.Build
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

    // Z distance between neighboring cards (meters, scale 1). Several times the
    // backing thickness so fanned cards can never interpenetrate visually — the
    // overlap is pure render order, like a real hand of cards (test #8 fix).
    private const float ZStagger = 0.004f;

    private void Relayout(bool instant)
    {
        if (_root == null)
            return;

        int n = _cards.Count;
        if (n == 0)
            return;

        float radius = CardsConfig.FanRadius.Value;
        float maxArc = CardsConfig.FanArcDegrees.Value;
        float w = CardsConfig.CardWidth.Value;
        // Slight overlap: per-card step shrinks as the hand grows, capped by maxArc.
        float step = n > 1 ? Mathf.Min(11f, maxArc / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

        // G1 curvature-by-fill (Demeo CardHandView.cs:814): both the vertical arch and the
        // per-card Z-tilt are multiplied by how full the hand is, so a few cards read nearly
        // flat/untilted and a full hand arches and tilts. FanFlatCurvatureFactor/FanTiltFactor
        // are the fill=1 targets (== the pre-Demeo constants 0.55/0.85). With FanCurveByFill
        // OFF we drop the fill term entirely and use those constants at every hand size, so the
        // legacy look is preserved exactly.
        float archFactor = CardsConfig.FanFlatCurvatureFactor.Value;
        float tiltFactor = CardsConfig.FanTiltFactor.Value;
        if (CardsConfig.FanCurveByFill.Value)
        {
            float fill = Mathf.Clamp01((float)n / Mathf.Max(1, CardsConfig.FanMaxHandForCurve.Value));
            archFactor *= fill;
            tiltFactor *= fill;
        }

        // Exposed strip of each card = chord between neighboring card centers. The
        // right neighbor draws IN FRONT (more negative z), covering this card's right
        // side — so each card's grab collider shrinks to its visible LEFT strip and
        // neighboring colliders no longer overlap (constant-haptic-buzz fix).
        float chord = n > 1 ? 2f * radius * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad) : w;
        float strip = Mathf.Clamp(chord, w * 0.25f, w);

        for (int i = 0; i < n; i++)
        {
            VRCard card = _cards[i];
            if (card == null || card.IsHeld)
                continue;
            if (!card.gameObject.activeSelf)
                card.gameObject.SetActive(true);

            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            // Arc bends around a pivot below the fan root; z-stagger keeps the
            // draw order stable (later cards nearer the viewer = -Z).
            var rot = Quaternion.Euler(0f, 0f, -angle * tiltFactor);
            var pos = new Vector3(Mathf.Sin(rad) * radius,
                                  (Mathf.Cos(rad) - 1f) * radius * archFactor,
                                  -ZStagger * i);
            card.SetHome(_root, pos, rot, 1f, instant);

            if (i == n - 1)
                card.ResetColliderRegion(); // fully exposed
            else
                card.SetColliderRegion(strip, -(w - strip) * 0.5f);
        }
    }

    // ------------------------------------------------------------------ laser pick --

    /// <summary>
    /// Demeo pluck (P6): intersect the dominant hand's aim ray with the fanned cards
    /// GEOMETRICALLY (per-card plane + rect — no physics, works regardless of the
    /// shrunken grab colliders). The nearest hit along the ray is the topmost card by
    /// construction (z-stagger/pop move upper cards toward the viewer). No allocations.
    ///
    /// P7 (hardware test #10, highlight hysteresis): <paramref name="sticky"/> — the
    /// currently hovered card — WINS whenever the ray still touches its rect at all,
    /// even if a neighbor is nearer along the ray. Overlapping fan cards + the pop
    /// animation can otherwise trade the "nearest" title mid-animation; with the
    /// sticky rule the hover only moves once the ray actually leaves the card.
    /// </summary>
    internal bool TryRaycast(Vector3 origin, Vector3 direction, VRCard? sticky,
        out VRCard? card, out Vector3 point, out float distance)
    {
        card = null;
        point = default;
        distance = float.PositiveInfinity;

        if (!IsOpen || _root == null)
            return false;

        float halfW = CardsConfig.CardWidth.Value * 0.5f;
        float halfH = CardsConfig.CardHeight * 0.5f;

        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard c = _cards[i];
            if (c == null || c.IsHeld || !c.gameObject.activeInHierarchy)
                continue;

            Transform t = c.transform;
            // Cards face the viewer with -Z; a ray from the viewer travels along +Z.
            float denom = Vector3.Dot(direction, t.forward);
            if (denom < 1e-5f)
                continue;
            float dist = Vector3.Dot(t.position - origin, t.forward) / denom;
            if (dist <= 0f)
                continue;

            Vector3 hit = origin + direction * dist;
            Vector3 local = t.InverseTransformPoint(hit); // scale-aware (pop growth included)
            if (Mathf.Abs(local.x) > halfW || Mathf.Abs(local.y) > halfH)
                continue;

            if (ReferenceEquals(c, sticky))
            {
                // Current hover still under the ray — it wins outright.
                card = c;
                point = hit;
                distance = dist;
                return true;
            }

            if (dist >= distance)
                continue;
            card = c;
            point = hit;
            distance = dist;
        }

        return card != null;
    }
}
