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

    // G2: index of the card the ray/finger is currently over (-1 = none). Drives the
    // whole-fan split in Relayout. Fed by the driver via SetHovered (parallel-owned file).
    private int _hoveredIndex = -1;

    // G4: when the eased follow starts (open, or the follow mode flips) the fan snaps to the
    // palm once instead of easing in from a stale position.
    private bool _followInit;

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
        _followInit = true; // G4: snap to the palm on the first Tick, don't ease in
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

    // ------------------------------------------------------------------ hover split --

    /// <summary>
    /// G2 (Demeo CardHandView.cs:451-464): tell the fan which card the ray/finger is over
    /// so the WHOLE fan can split apart around it — the neighbours slide sideways to open a
    /// gap, most for the nearest, and the hovered card pops toward the viewer. Index -1 =
    /// nothing hovered, and the split relaxes back to zero via the existing per-card lerp.
    ///
    /// PUBLIC because the hover source (laser <c>_laserHover</c> / proximity highlight) lives
    /// in <see cref="CardsDriver"/> — a parallel-owned file (blueprint Group D) that pushes
    /// the index in here. The split offsets are fan-LOCAL constants (they depend only on the
    /// slot rotations, not the fan's head-facing world orientation), so we recompute on hover
    /// CHANGE only and let <see cref="VRCard"/> animate the slide — a per-frame pass would be
    /// redundant and this stays allocation-free.
    ///
    /// The hovered card's forward pop is intentionally NOT applied here: <see cref="VRCard"/>
    /// already pops any hovered/highlighted card toward the viewer (its <c>_laserPopped</c> /
    /// <c>_popped</c> path, magnitude == <see cref="CardsConfig.FanSelectedPopForward"/>'s
    /// 0.035 default), so baking a pop into the home too would double it.
    /// </summary>
    public void SetHovered(int index)
    {
        if (index == _hoveredIndex)
            return;
        _hoveredIndex = index;
        if (IsOpen)
            Relayout(instant: false);
    }

    // ------------------------------------------------------------------ per frame --

    /// <summary>Follow the palm (rigidly or eased) and face the head every frame while open.</summary>
    internal void Tick()
    {
        if (!IsOpen || _root == null || _hand == null)
            return;

        Transform? palm = _hand.Rig.PalmCenter;
        if (palm == null)
            return;

        // The palm target: FanPalmOffset up the palm normal, in world space. FanPalmOffset is
        // "real meters" and must be multiplied by the DIORAMA scale (WorldScale) to land in
        // world units.
        //
        // INVISIBLE-FAN FIX: we must NOT read palm.lossyScale here. For the procedural hand the
        // palm anchor is a direct child of the hand root, so palm.lossyScale == WorldScale and
        // this used to be equivalent — but the bundle GLOVE prefab hangs its anchors under an
        // armature transform authored at localScale 100 (fbx cm→m), so palm.lossyScale ==
        // WorldScale * 100. Scaling the standoff by that put the fan ~9 m (× WorldScale) up the
        // palm normal instead of 0.09 m — metres out of view, i.e. the fan "did not render where
        // the player looks". _hand.WorldScale is the true diorama scale (VRHand transform, ABOVE
        // the glove's 100× armature) and reproduces the procedural distance exactly for both hands.
        float scale = _hand.WorldScale;
        Vector3 target = palm.position + palm.up * (CardsConfig.FanPalmOffset.Value * scale);

        float smoothing = CardsConfig.FanFollowSmoothing.Value;
        Transform? rig = VRRigDriver.RigRoot;
        if (smoothing > 0f && rig != null)
        {
            // G4 eased dead-zoned follow (Demeo ViewHelper, CardHandView.cs:677). Parent to the
            // STABLE rig root (same diorama scale as the palm, so card sizes are unchanged) and
            // ease the fan's WORLD position toward the palm — decoupling it from the palm so the
            // dead zone can hold it perfectly still through sub-threshold hand jitter. Reparent
            // only when the mode actually flips (worldPositionStays: no visible jump).
            if (_root.parent != rig)
            {
                _root.SetParent(rig, worldPositionStays: true);
                _followInit = true;
            }

            if (_followInit)
            {
                _followInit = false;
                _root.position = target;
            }
            else
            {
                Vector3 delta = target - _root.position;
                // minDistanceToMove: below the dead zone the fan holds still; past it, ease in
                // frame-rate-independent exponential steps at the configured rate.
                if (delta.magnitude > CardsConfig.FanFollowDeadzone.Value * scale)
                    _root.position += delta * (1f - Mathf.Exp(-smoothing * Time.deltaTime));
            }
        }
        else
        {
            // Rigid (pre-Demeo, FanFollowSmoothing == 0): welded to the palm. Parent to
            // PalmCenter and sit at the offset — exactly the previous behaviour.
            // NOTE: this NON-default path parents the fan directly under PalmCenter, so with the
            // bundle glove (100× armature scale on the anchors) both this offset AND the card
            // sizes inherit that 100× — it is only correct for the procedural hand / an unscaled
            // glove rig. The shipped default is the eased branch above (FanFollowSmoothing = 16),
            // which parents to the rig root and uses _hand.WorldScale, so it is glove-correct.
            if (_root.parent != palm)
            {
                _root.SetParent(palm, worldPositionStays: true);
                _followInit = true;
            }
            _root.localPosition = new Vector3(0f, CardsConfig.FanPalmOffset.Value, 0f);
        }

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

        // G2 whole-fan split (Demeo CardHandView.cs:451-464): when a card is hovered the
        // others slide sideways to open a gap around it. Clamp defends against a stale index
        // left over after a card was plucked out of the fan before the driver clears it.
        int hovered = _hoveredIndex >= 0 && _hoveredIndex < n ? _hoveredIndex : -1;

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

            // Slide non-hovered cards along their OWN local right (rot * X, in fan space) to
            // open the split gap around the hovered card. The hovered card is the pivot and
            // does not move (its pop is VRCard's job — see SetHovered).
            if (hovered >= 0 && i != hovered)
                pos += rot * new Vector3(SplitOffset(i - hovered), 0f, 0f);

            card.SetHome(_root, pos, rot, 1f, instant);

            if (i == n - 1)
                card.ResetColliderRegion(); // fully exposed
            else
                card.SetColliderRegion(strip, -(w - strip) * 0.5f);
        }
    }

    /// <summary>
    /// G2 sideways split offset (real meters) for a card <paramref name="signed"/> = i -
    /// hovered slots from the hovered card. Coded substitute for Demeo's serialized
    /// <c>cardSplitCurve</c> (CardHandView.cs:452): a GAUSSIAN in slot-distance,
    /// <c>FanSplitMultiplier * exp(-(d / FanSplitFalloff)^2)</c> with d = |signed|, signed by
    /// which side of the hovered card we are on (left slides left, right slides right). The
    /// nearest neighbour moves most and the push decays smoothly outward; FanSplitFalloff is
    /// the Gaussian width — higher = only the immediate neighbours move, lower = the whole
    /// fan spreads. FanSplitMultiplier = 0 disables the split entirely.
    /// </summary>
    private static float SplitOffset(int signed)
    {
        float d = Mathf.Abs(signed);
        float falloff = Mathf.Max(0.0001f, CardsConfig.FanSplitFalloff.Value);
        float x = d / falloff;
        return Mathf.Sign(signed) * Mathf.Exp(-x * x) * CardsConfig.FanSplitMultiplier.Value;
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
