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

    // Test #9: the card a FINGERTIP is hovering (-1 = none), resolved every frame by
    // UpdateFingertipHover. Used as the split source ONLY when the driver isn't already
    // driving one (_hoveredIndex < 0) — the driver's laser/grabber hover always wins. This
    // lets a light fingertip touch split the fan exactly like the laser without this path
    // and the driver (which owns _hoveredIndex and re-computes it every frame) fighting.
    private int _pokeHoveredIndex = -1;

    // G4: when the eased follow starts (open, or the follow mode flips) the fan snaps to the
    // palm once instead of easing in from a stale position.
    private bool _followInit;

    // Hand reorder: the active insertion GAP (0..n, -1 = none) while a fan-originating card is
    // held over the fan. Cards on either side slide apart to open room and a board-slot-style
    // glow overlay marks the drop point. Driven by the driver via SetInsertionGap each frame.
    private int _insertGap = -1;
    private GameObject? _overlay; // the gold gap glow (shared CardGlow recipe); child of _root

    internal bool IsOpen { get; private set; }

    /// <summary>
    /// The currently OPEN local hand fan (null when closed / disposed). Read by the net presence
    /// sender to broadcast the local hand-card COUNT to other VR players (they render backs only —
    /// never card identities). Cosmetic; no game state.
    /// </summary>
    internal static CardFan? Current { get; private set; }

    /// <summary>How many cards the fan currently holds (broadcast as the remote hand-card count).</summary>
    internal int Count => _cards.Count;

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
        Current = this; // expose the open fan to the net presence sender (hand-card count)
        Relayout(instant: true);
    }

    internal void Close()
    {
        IsOpen = false;
        if (ReferenceEquals(Current, this))
            Current = null;
        ClearFingertipHover(); // test #9: drop any fingertip pop/split
        _insertGap = -1;       // hand reorder: drop any open gap so a reopen starts closed
        if (_overlay != null)
            _overlay.SetActive(false);
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    internal void Destroy()
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        ClearFingertipHover();
        _cards.Clear();
        _insertGap = -1;
        _overlay = null; // destroyed with _root (its parent) below
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
        ClearFingertipHover(); // card set/indices change — re-resolve on the next Tick scan
        _insertGap = -1;       // reorder: card set changed; the driver re-pushes the gap next frame
        if (_overlay != null)
            _overlay.SetActive(false);
        _cards.Clear();
        for (int i = 0; i < cards.Count; i++)
            _cards.Add(cards[i]);
        if (IsOpen)
            Relayout(instant: false);
    }

    /// <summary>Remove a card (grabbed away); remaining cards close the gap.</summary>
    internal void Remove(VRCard card)
    {
        if (_cards.Remove(card))
        {
            if (ReferenceEquals(card, _pokeHoverCard))
                ClearFingertipHover();
            else
                _pokeHoveredIndex = -1; // indices shifted; the Tick scan re-stamps
            if (IsOpen)
                Relayout(instant: false);
        }
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

    // ------------------------------------------------------------------ insertion gap (reorder) --

    /// <summary>How wide the opened insertion gap is, as a fraction of a card width (each side
    /// slides half of this). ~one card so a full card visibly fits. Local until the orchestrator
    /// wires a live-tunable <c>FanInsertGapWidth</c> config (see the worker report).</summary>
    private const float FanInsertGapFactor = 0.9f;

    /// <summary>The gap glow overlay sits this far proud (toward the viewer, negative local Z) of
    /// the neighbouring card so it reads over them without z-fighting. Mirrors the board slot
    /// overlay's <c>SlotGlowBaseZ</c> (-0.006).</summary>
    private const float OverlayProudZ = -0.006f;

    /// <summary>
    /// Hand reorder: open (or move) the insertion GAP at <paramref name="gap"/> (0..n; -1 = none).
    /// Mirrors <see cref="SetHovered"/> — records the gap and relayouts so the fan slides apart
    /// around it and the board-slot-style glow appears there. Pushed by the driver each frame
    /// while a fan-originating card is held over the fan (<c>CardsDriver.UpdateFanInsertion</c>).
    /// </summary>
    public void SetInsertionGap(int gap)
    {
        int n = _cards.Count;
        if (gap < 0 || gap > n)
            gap = -1;
        if (gap == _insertGap)
            return;
        _insertGap = gap;
        if (IsOpen)
            Relayout(instant: false);
    }

    /// <summary>
    /// Hand reorder: map a world point (the held card's position) to the nearest inter-card gap
    /// index (0..n). Projects the point into fan-local space and counts the cards whose base
    /// centre sits left of it. Returns -1 when the point is NOT near the fan (outside the
    /// reorder zone) — the driver treats that as "no gap", so a release there cancels
    /// (return-to-origin) rather than committing. Allocation-free.
    /// </summary>
    internal int NearestGap(Vector3 worldPoint)
    {
        if (!IsOpen || _root == null)
            return -1;
        int n = _cards.Count;
        if (n == 0)
            return -1;

        // Same layout geometry as Relayout (base arc, no split/gap offsets).
        float radius = Mathf.Max(0.02f, CardsConfig.FanEffectiveRadius.Value);
        float maxArc = Mathf.Clamp(CardsConfig.FanArcSweepDegrees.Value, 5f, 180f);
        float stepCap = Mathf.Clamp(CardsConfig.FanPerCardStepDegrees.Value, 1f, 60f);
        float archFactor = CardsConfig.FanFlatCurvatureFactor.Value;
        if (CardsConfig.FanCurveByFill.Value)
            archFactor *= Mathf.Clamp01((float)n / Mathf.Max(1, CardsConfig.FanMaxHandForCurve.Value));
        float step = n > 1 ? Mathf.Min(stepCap, maxArc / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

        Vector3 local = _root.InverseTransformPoint(worldPoint);

        // Reorder zone: reject points farther than one card from the nearest card centre (in the
        // fan plane) so a release well away from the fan cancels. Generous — tune on hardware.
        float reach = Mathf.Max(CardsConfig.CardWidth.Value, CardsConfig.CardHeight) * 1.25f;
        int gap = 0;
        float best = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            float rad = (start + step * i) * Mathf.Deg2Rad;
            float cx = Mathf.Sin(rad) * radius;
            float cy = (Mathf.Cos(rad) - 1f) * radius * archFactor;
            float cz = -ZStagger * i;
            if (cx < local.x)
                gap++;
            float dx = cx - local.x, dy = cy - local.y, dz = cz - local.z;
            float d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < best)
                best = d2;
        }
        return best <= reach * reach ? gap : -1;
    }

    /// <summary>
    /// Reorder gap-open push (real meters, fan-local X) for card <paramref name="i"/> given the
    /// active <paramref name="gap"/>: cards left of the gap slide left, right of it slide right,
    /// most for the immediate neighbours and decaying outward (gaussian in slot-distance, reusing
    /// <see cref="CardsConfig.FanSplitFalloff"/>), opening a card-width slot for the incoming card.
    /// </summary>
    private static float GapOffset(int i, int gap)
    {
        int d;
        float side;
        if (i < gap) { d = gap - i; side = -1f; }
        else { d = i - gap + 1; side = 1f; }
        float falloff = Mathf.Max(0.0001f, CardsConfig.FanSplitFalloff.Value);
        float x = (d - 1) / falloff;
        float half = CardsConfig.CardWidth.Value * FanInsertGapFactor * 0.5f;
        return side * half * Mathf.Exp(-x * x);
    }

    // ------------------------------------------------------------------ fingertip hover --

    /// <summary>Fingertip hover reach (meters, scale 1): the index tip pops a card when this
    /// close to its front face — "touching / just reaching", not from afar (test #9).</summary>
    private const float FingertipHoverReach = 0.035f;

    /// <summary>The card the free hand's index tip is currently hovering (null = none).</summary>
    private VRCard? _pokeHoverCard;

    /// <summary>
    /// Test #9 (fingertip highlight): pop the fan card the FREE (dominant) hand's index tip
    /// is touching / just reaching — the same visual the laser gives — and split the fan
    /// around it. Deliberately NOT the global poke registry (that has no per-hand filter and
    /// would buzz the fan-OWNING hand whose fingers sit right by the cards); we scan the
    /// single dominant hand here, pick the ONE nearest card within reach (no multi-pop
    /// flip-flop), and drive the card's own pop + the fan split. The driver's laser/grabber
    /// hover still wins the split (<see cref="_hoveredIndex"/> &gt;= 0 in <see cref="Relayout"/>);
    /// this fills in when neither is active. Allocation-free.
    /// </summary>
    private void UpdateFingertipHover()
    {
        VRCard? target = null;
        VRHand? dom = VRHands.Primary;
        // Only the free hand highlights by fingertip: the fan-owning hand is excluded (its
        // palm/fingers sit inside the fan — the exact flip-flop source of test #10), and a
        // hand already holding a card is mid-placement, not browsing.
        if (dom != null && !ReferenceEquals(dom, _hand) && dom.HasPose && dom.Grabber.Held == null)
        {
            Vector3 tip = dom.Rig.IndexTip.position;
            float best = FingertipHoverReach * dom.WorldScale;
            for (int i = 0; i < _cards.Count; i++)
            {
                VRCard c = _cards[i];
                if (c == null || c.IsHeld)
                    continue;
                if (c.TryFingertipDistance(tip, out float d) && d <= best)
                {
                    best = d;
                    target = c;
                }
            }
        }

        if (ReferenceEquals(target, _pokeHoverCard))
            return;

        _pokeHoverCard?.SetFingertipHover(false);
        _pokeHoverCard = target;
        int index = -1;
        if (target != null)
        {
            target.SetFingertipHover(true);
            index = _cards.IndexOf(target);
            if (!s_loggedFingertipHover)
            {
                s_loggedFingertipHover = true;
                Core.VRLog.Info("Cards", "Fingertip card hover ACTIVE (test #9): the free hand's " +
                                         "index tip now pops a fan card on contact (no palm-deep reach needed).");
            }
            dom!.SendHaptic(HapticPreset.HoverTick); // debounced: only on card change
        }
        // Split the fan around the poke-hovered card when the driver isn't driving one.
        if (index != _pokeHoveredIndex)
        {
            _pokeHoveredIndex = index;
            if (IsOpen && _hoveredIndex < 0)
                Relayout(instant: false);
        }
    }

    /// <summary>Drop any live fingertip hover pop + split (fan close / card set change).</summary>
    private void ClearFingertipHover()
    {
        _pokeHoverCard?.SetFingertipHover(false);
        _pokeHoverCard = null;
        _pokeHoveredIndex = -1;
    }

    /// <summary>One-shot session log guard for the fingertip-hover confirmation line.</summary>
    private static bool s_loggedFingertipHover;

    // ------------------------------------------------------------------ per frame --

    /// <summary>Follow the palm (rigidly or eased) and face the head every frame while open.</summary>
    internal void Tick()
    {
        if (!IsOpen || _root == null || _hand == null)
            return;

        Transform? palm = _hand.Rig.PalmCenter;
        if (palm == null)
            return;

        // Test #9: pop the fan card the free hand's index tip is touching (before the
        // follow/face math so a hover-driven split relayouts this same frame).
        UpdateFingertipHover();

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

    // ---------------------------------------------------------------- item 8: wider, rounder fan --
    // The fan's width/roundness/spacing (per-card step cap, total arc sweep, arc radius, hover-split
    // scale) is now driven by GLOBAL CardsConfig entries — live-tunable from the in-VR "Fan" debug
    // category — instead of the four LOCAL consts that used to live here. They are seeded to the old
    // effective values so nothing changes until tuned. Everything downstream (collider strip, split
    // gap, laser/fingertip hit-testing) still derives from the SAME radius/step, so it stays aligned
    // with the width the player dials in. See CardsConfig.Fan* + CardsDriver's live-apply.

    /// <summary>
    /// Live re-apply hook (in-VR "Fan" debug category): CardsDriver calls this when a global fan
    /// ConfigEntry changes so the open fan re-lays out immediately at the new geometry. No-op while
    /// the fan is closed (the next Open() lays out fresh).
    /// </summary>
    internal void ApplyLayout()
    {
        if (IsOpen)
            Relayout(instant: false);
    }

    private void Relayout(bool instant)
    {
        if (_root == null)
            return;

        int n = _cards.Count;
        if (n == 0)
            return;

        // Item 8: widen + round geometry, now from the GLOBAL Fan config (live-tunable). Clamp
        // defensively even though the ConfigEntries carry AcceptableValueRange (a hand-edited cfg
        // could still hold an out-of-range value).
        float radius = Mathf.Max(0.02f, CardsConfig.FanEffectiveRadius.Value);
        float maxArc = Mathf.Clamp(CardsConfig.FanArcSweepDegrees.Value, 5f, 180f);
        float stepCap = Mathf.Clamp(CardsConfig.FanPerCardStepDegrees.Value, 1f, 60f);
        float w = CardsConfig.CardWidth.Value;
        // Separated but still overlapping: per-card step holds at the step cap for small hands
        // (easy per-card targeting) and shrinks only once the hand is full enough that the whole
        // sweep would exceed the arc cap.
        float step = n > 1 ? Mathf.Min(stepCap, maxArc / (n - 1)) : 0f;
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
        // The driver's laser/grabber hover (_hoveredIndex) wins; a fingertip poke-hover
        // (_pokeHoveredIndex, test #9) fills in when the driver isn't driving one.
        int source = _hoveredIndex >= 0 ? _hoveredIndex : _pokeHoveredIndex;
        int hovered = source >= 0 && source < n ? source : -1;

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

            // Hand reorder: when an insertion gap is open, shift the cards on each side apart to
            // make room for the incoming card + its overlay (independent of the hover split;
            // while a card is HELD the hover source is inactive so these never fight).
            if (_insertGap >= 0)
                pos += rot * new Vector3(GapOffset(i, _insertGap), 0f, 0f);

            card.SetHome(_root, pos, rot, 1f, instant);

            if (i == n - 1)
                card.ResetColliderRegion(); // fully exposed
            else
                card.SetColliderRegion(strip, -(w - strip) * 0.5f);
        }

        // Hand reorder: place + show the board-slot-style glow at the open gap, or hide it. The
        // gap slot sits at the arc angle midway between cards gap-1 and gap (extending past the
        // ends for gap==0/gap==n), proud of the neighbour so it reads over the fan.
        if (_insertGap >= 0)
        {
            EnsureOverlay();
            float gapAngle = start + step * (_insertGap - 0.5f);
            float gapRad = gapAngle * Mathf.Deg2Rad;
            _overlay!.transform.localRotation = Quaternion.Euler(0f, 0f, -gapAngle * tiltFactor);
            _overlay.transform.localPosition = new Vector3(
                Mathf.Sin(gapRad) * radius,
                (Mathf.Cos(gapRad) - 1f) * radius * archFactor,
                -ZStagger * _insertGap + OverlayProudZ);
            if (!_overlay.activeSelf)
                _overlay.SetActive(true);
        }
        else if (_overlay != null && _overlay.activeSelf)
        {
            _overlay.SetActive(false);
        }
    }

    /// <summary>Lazily build the gap glow overlay (gold, card-shaped) via the SHARED
    /// <see cref="CardGlow"/> recipe so it looks IDENTICAL to the board slot overlay. Created
    /// inactive as a child of the fan root; <see cref="Relayout"/> poses + toggles it.</summary>
    private void EnsureOverlay()
    {
        if (_overlay != null || _root == null)
            return;
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;
        _overlay = CardGlow.CreateGlowQuad("FanInsertHighlight", _root,
            new Vector3(w * 1.24f, h * 1.24f, 1f), Vector3.zero,
            new Color(1f, 0.85f, 0.3f, 0.95f)); // same gold as the board slot glow
        Core.VRLayers.Apply(_overlay);
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
        // FanHoverSplitScale (global, live-tunable) keeps the gap proportional to the wider card spacing.
        float splitScale = Mathf.Max(0f, CardsConfig.FanHoverSplitScale.Value);
        return Mathf.Sign(signed) * Mathf.Exp(-x * x) * CardsConfig.FanSplitMultiplier.Value * splitScale;
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
