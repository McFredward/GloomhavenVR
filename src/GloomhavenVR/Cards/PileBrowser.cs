using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>Which control-board pile stack a browse request refers to (test #21).</summary>
internal enum PileKind
{
    /// <summary>The discard pile (<c>CCharacterClass.DiscardedAbilityCards</c>).</summary>
    Discard,

    /// <summary>The burnt pile (lost + permanently lost, the 2D "burnt" header union).</summary>
    Burnt,

    /// <summary>The acting character's ITEM cards (<c>PlayerActor.Inventory.AllItems</c>).</summary>
    Items,
}

/// <summary>
/// The pile browse fan (hardware test #21): a readable arc of one pile's cards,
/// raised by poking or pinch-grabbing a <see cref="PileViewer"/> stack. Simplified
/// <see cref="CardFan"/>-style arc at a fixed head-relative READING pose (its POSITION is
/// placed once at open — calmer to read than a chasing palm fan — but it BILLBOARDS to
/// face the head every frame in <see cref="Tick"/> so the cards always face the player,
/// ISSUE #7), cards slightly enlarged. Purely informational: the cards are adopted read-only (never grabbable,
/// never poke-selectable — CardsDriver clears both flags), and closing simply lets
/// the next rebuild park them again. Layout only — open/close policy and content
/// live in <see cref="CardsDriver"/>. No allocations after open.
/// </summary>
internal sealed class PileBrowser
{
    // Arc geometry relative to the palm fan: larger radius + per-card step cap so
    // more of every card stays exposed at reading distance (browse is for READING,
    // not for picking a grab target).
    private const float RadiusFactor = 1.7f;
    private const float MaxArcDegrees = 110f;
    private const float MaxStepDegrees = 10f;
    private const float CardScale = 1.3f;
    private const float ZStagger = 0.004f; // render-order stagger, same as CardFan

    // Hand-held reading pose (test #22, item 5): float the arc above the holding
    // palm and tilt it back toward the head — the "take the pile INTO my hand"
    // placement, so each card is at reading distance and pinch/laser-reachable.
    private const float HandPalmOffset = 0.16f;

    private readonly List<VRCard> _cards = new(16);
    private Transform? _root;
    private TextMeshPro? _title;
    private VRHand? _followHand;

    internal bool IsOpen { get; private set; }

    /// <summary>The pile currently browsed (null while closed).</summary>
    internal PileKind? Kind { get; private set; }

    /// <summary>Held-fan mode (grabbed a pile): the arc follows the grabbing hand.</summary>
    internal bool IsHandHeld => _followHand != null;

    internal bool Contains(VRCard card) => _cards.Contains(card);

    // ------------------------------------------------------------------ lifecycle --

    /// <summary>
    /// Open (or switch) the browser for one pile. With <paramref name="followHand"/>
    /// the arc is a HELD reading fan pinned to that hand (test #22 item 5, grabbed a
    /// pile); without it the arc is placed once at a fixed head-relative reading pose
    /// (poke-toggle). Either way the cards are readable, individually grabbable and
    /// laser-hoverable — the driver owns those flags and the open/close policy.
    /// </summary>
    internal void Open(PileKind kind, Transform anchorParent, VRHand? followHand = null)
    {
        if (_root == null)
        {
            _root = new GameObject("GloomhavenVR.PileBrowser").transform;
            Core.VRLayers.Apply(_root.gameObject); // cards apply themselves in VRCard.Build

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(_root, worldPositionStays: false);
            titleGo.transform.localPosition = new Vector3(0f, 0.16f, -0.004f);
            _title = titleGo.AddComponent<TextMeshPro>();
            _title.text = string.Empty;
            _title.alignment = TextAlignmentOptions.Center;
            _title.color = new Color(1f, 0.9f, 0.6f);
            // Single line: localized pile names + count shrink into the box (TmpFit, test #12).
            Core.TmpFit.Fit(_title, 0.30f, 0.032f, maxFontSize: 0.34f, wrap: false);
        }
        _followHand = followHand;
        Transform parent = followHand != null ? followHand.Rig.PalmCenter : anchorParent;
        if (_root.parent != parent)
            _root.SetParent(parent, worldPositionStays: false);
        _root.gameObject.SetActive(true);
        Kind = kind;
        IsOpen = true;
        if (followHand != null)
            Tick(); // place immediately near the holding hand
        else
            PlaceAtHead();
        Relayout(instant: false);
    }

    /// <summary>Close the browser. Cards are NOT touched — the driver's rebuild parks them.</summary>
    internal void Close()
    {
        IsOpen = false;
        Kind = null;
        _followHand = null;
        _cards.Clear();
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    internal void Destroy()
    {
        _cards.Clear();
        IsOpen = false;
        Kind = null;
        _followHand = null;
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
            _title = null; // child of _root, destroyed with it
        }
    }

    // ------------------------------------------------------------------ content --

    /// <summary>Replace the browsed card set + title (rebuild path; cards fly to their arc slots).</summary>
    internal void SetCards(List<VRCard> cards, string title)
    {
        _cards.Clear();
        for (int i = 0; i < cards.Count; i++)
            _cards.Add(cards[i]);
        if (_title != null && _title.text != title)
            _title.text = title;
        if (IsOpen)
            Relayout(instant: false);
    }

    /// <summary>Drop one card (widget recycled mid-browse); remaining cards close the gap.</summary>
    internal void Remove(VRCard card)
    {
        if (_cards.Remove(card) && IsOpen)
            Relayout(instant: false);
    }

    /// <summary>
    /// Return a card plucked out for a close read (item 5) back into the arc — the
    /// browse counterpart of <see cref="CardFan.Add"/>. No-op once the browse closed
    /// (the driver parks the card instead), so a closed browse never re-homes cards.
    /// </summary>
    internal void Add(VRCard card)
    {
        if (!IsOpen)
            return;
        if (!_cards.Contains(card))
            _cards.Add(card);
        Relayout(instant: false);
    }

    // ------------------------------------------------------------------ placement --

    /// <summary>
    /// Reading pose: in front of the head at ~tray distance, raised toward eye
    /// height, tilted slightly back — the proven HalfSelection floating pose
    /// (HalfSelection.PlaceAtHead) shifted up for a card WALL instead of a pair.
    /// POSITION is placed once per open (deliberately no per-frame follow); the FACING is
    /// re-billboarded toward the head every frame in <see cref="Tick"/> (ISSUE #7).
    /// </summary>
    private void PlaceAtHead()
    {
        if (_root == null)
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Transform headT = head.transform;
        Vector3 flatForward = headT.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        Vector3 pos = headT.position
                      + flatForward * (CardsConfig.TrayForward.Value * 0.9f * scale)
                      + Vector3.up * (-(CardsConfig.TrayDown.Value - 0.22f) * scale);
        _root.position = pos;
        _root.rotation = Quaternion.LookRotation(flatForward, Vector3.up)
                         * Quaternion.Euler(-12f, 0f, 0f);
    }

    /// <summary>
    /// Per-frame facing update while the browse is open (ISSUE #7). In HELD mode (item 5)
    /// the arc also floats above the grabbing palm and moves with the controller. In BOTH
    /// modes the arc BILLBOARDS to face the head every frame — exactly like
    /// <see cref="CardFan.Tick"/> — so the browsed cards always face the player, even the
    /// poke-toggled wall that is placed once (<see cref="PlaceAtHead"/>) and never
    /// repositions: its position stays put (calm to read), only its facing tracks the head,
    /// so the cards face the player even BEFORE one is plucked into the hand. No-op while closed.
    /// </summary>
    internal void Tick()
    {
        if (!IsOpen || _root == null)
            return;
        // Held mode only: the pivot floats above the palm along the palm normal (+Y of
        // PalmCenter). The poke-toggle wall keeps the fixed position PlaceAtHead gave it.
        if (_followHand != null)
            _root.localPosition = new Vector3(0f, HandPalmOffset, 0f);
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Vector3 away = _root.position - head.transform.position;
        if (away.sqrMagnitude < 1e-6f)
            return;
        // Cards' +Z points away from the viewer (uGUI reads from -Z); tilt back a touch.
        _root.rotation = Quaternion.LookRotation(away.normalized, Vector3.up)
                         * Quaternion.Euler(-12f, 0f, 0f);
    }

    // ------------------------------------------------------------------ layout --

    private void Relayout(bool instant)
    {
        if (_root == null)
            return;
        int n = _cards.Count;
        if (n == 0)
            return;

        float radius = CardsConfig.FanRadius.Value * RadiusFactor;
        float step = n > 1 ? Mathf.Min(MaxStepDegrees, MaxArcDegrees / (n - 1)) : 0f;
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
            // Same arc math as CardFan.Relayout: bend around a pivot below the
            // root, z-stagger for stable draw order (later = nearer the viewer).
            var pos = new Vector3(Mathf.Sin(rad) * radius,
                                  (Mathf.Cos(rad) - 1f) * radius * 0.55f,
                                  -ZStagger * i);
            var rot = Quaternion.Euler(0f, 0f, -angle * 0.85f);
            card.SetHome(_root, pos, rot, CardScale, instant);
            card.ResetColliderRegion(); // browse cards are not fan-stripped
        }
    }

    // ------------------------------------------------------------------ laser pick --

    /// <summary>
    /// Geometric ray hit-test over the browse arc (item 5, laser-hover to read /
    /// pluck close) — the browse counterpart of <see cref="CardFan.TryRaycast"/>. Same
    /// per-card plane+rect test, scale-aware (the arc's cards are enlarged), same
    /// sticky-hover hysteresis so overlap doesn't flip the highlight. No allocations.
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
            float denom = Vector3.Dot(direction, t.forward);
            if (denom < 1e-5f)
                continue;
            float dist = Vector3.Dot(t.position - origin, t.forward) / denom;
            if (dist <= 0f)
                continue;

            Vector3 hit = origin + direction * dist;
            Vector3 local = t.InverseTransformPoint(hit); // scale-aware (enlarged cards)
            if (Mathf.Abs(local.x) > halfW || Mathf.Abs(local.y) > halfH)
                continue;

            if (ReferenceEquals(c, sticky))
            {
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
