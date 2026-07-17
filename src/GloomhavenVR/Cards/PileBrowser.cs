using System.Collections.Generic;
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
}

/// <summary>
/// The pile browse fan (hardware test #21): a readable arc of one pile's cards,
/// raised by poking or pinch-grabbing a <see cref="PileViewer"/> stack. Simplified
/// <see cref="CardFan"/>-style arc at a fixed head-relative READING pose (placed once
/// at open — no per-frame following, calmer to read than a palm fan), cards slightly
/// enlarged. Purely informational: the cards are adopted read-only (never grabbable,
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

    private readonly List<VRCard> _cards = new(16);
    private Transform? _root;
    private TextMeshPro? _title;

    internal bool IsOpen { get; private set; }

    /// <summary>The pile currently browsed (null while closed).</summary>
    internal PileKind? Kind { get; private set; }

    internal bool Contains(VRCard card) => _cards.Contains(card);

    // ------------------------------------------------------------------ lifecycle --

    /// <summary>Open (or switch) the browser for one pile, placed at the current head pose.</summary>
    internal void Open(PileKind kind, Transform anchorParent)
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
        if (_root.parent != anchorParent)
            _root.SetParent(anchorParent, worldPositionStays: false);
        _root.gameObject.SetActive(true);
        Kind = kind;
        IsOpen = true;
        PlaceAtHead();
        Relayout(instant: false);
    }

    /// <summary>Close the browser. Cards are NOT touched — the driver's rebuild parks them.</summary>
    internal void Close()
    {
        IsOpen = false;
        Kind = null;
        _cards.Clear();
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    internal void Destroy()
    {
        _cards.Clear();
        IsOpen = false;
        Kind = null;
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

    // ------------------------------------------------------------------ placement --

    /// <summary>
    /// Reading pose: in front of the head at ~tray distance, raised toward eye
    /// height, tilted slightly back — the proven HalfSelection floating pose
    /// (HalfSelection.PlaceAtHead) shifted up for a card WALL instead of a pair.
    /// Placed once per open; deliberately no per-frame follow.
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
}
