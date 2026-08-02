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
/// <see cref="CardsDriver"/>.
///
/// ALLOCATION RULE (this used to read "no allocations in Tick", which is false and is the
/// kind of blanket claim a perf pass trusts): the STEADY per-frame path allocates nothing,
/// but <see cref="Tick"/> does reach interpolated-string diagnostics (the live-tuning
/// signature line, <c>UpdateGazeBias</c>, <c>Relayout</c>) and <c>ComposeDepths</c> regrows
/// <c>_depths</c> when the hand outgrows its capacity. Every one of those sits BEHIND a
/// change or throttle gate, never in front of one — that ordering is the invariant, not the
/// absence of allocation. Keep new diagnostics on the same side of the gate.
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

    // Fan-out reveal animation (Demeo fan-in: every card seeds at the MIDDLE slot's pose and
    // lerps out to its own slot — decompiled CardHandView.cs:577-580, lerp at :430-431/444-462).
    // Elapsed seconds since Open (-1 = not animating). Runs on UNSCALED time: the game pauses
    // simulation time during selection, and the reveal must stay frame-rate independent.
    private float _openElapsed = -1f;

    // Quick-collapse on hide (reverse fan-in): elapsed seconds since Close (-1 = not
    // collapsing). While active the root stays visible and Tick blends the cards back into
    // the center stack, then deactivates the root.
    private float _closeElapsed = -1f;

    internal bool IsOpen { get; private set; }

    /// <summary>
    /// The currently OPEN local hand fan (null when closed / disposed). Read by the net presence
    /// sender to broadcast the local hand-card COUNT to other VR players (they render backs only —
    /// never card identities). Cosmetic; no game state.
    /// </summary>
    internal static CardFan? Current { get; private set; }

    /// <summary>
    /// The hand this fan is currently open ON (null before the first <see cref="Open"/>). Read by
    /// <see cref="Hands.HandGhosts"/> to fade exactly that hand while the fan is open — the fan
    /// already owns this fact, so the ghost feature keeps no duplicate state that could drift.
    /// </summary>
    internal Hands.VRHand? Hand => _hand;

    /// <summary>
    /// The fan's ROOT transform — the frame every card's arc pose, tilt, split, toe-in and depth bow
    /// is expressed in (null before the first <see cref="Open"/>). Read-only seam for
    /// <see cref="WorldUI.AvatarMirror"/>: the mirror needs the fan-LOCAL pose of each card (root
    /// pose ⁻¹ ∘ card pose) so it can re-emit the whole arc under a MIRRORED root, which is the only
    /// way the mirrored fan keeps reading the same way round as the real one relative to the
    /// mirrored hand. Exposed rather than duplicated because the root is also what the eased palm
    /// follow moves (its parent flips between PalmCenter and the rig root, see <see cref="Tick"/>),
    /// so no outside recomputation could reproduce it.
    /// </summary>
    internal Transform? Root => _root;

    /// <summary>How many cards the fan currently holds (broadcast as the remote hand-card count).</summary>
    internal int Count => _cards.Count;

    /// <summary>Cards currently owned by the fan (read-only view).</summary>
    internal IReadOnlyList<VRCard> Cards => _cards;

    internal bool Contains(VRCard card) => _cards.Contains(card);

    /// <summary>
    /// Index of the card the player is currently SINGLING OUT in this fan (lifted + enlarged +
    /// neighbours split apart), or -1 when none is. Read off <see cref="VRCard.IsHighlighted"/> —
    /// the very predicate the pop animation obeys — rather than off <c>_hoveredIndex</c>, so a
    /// grabber/fingertip lift that never went through <see cref="SetHovered"/> counts too and the
    /// answer can never disagree with what is on screen.
    ///
    /// Exists for the MULTIPLAYER mirror (<c>Net.NetAvatarDriver</c> broadcasts it as a bare index
    /// — a position, never a card identity — so a peer's copy of this fan lifts the same card).
    /// Only ONE card can be lifted at a time (the local hand arbitration and the laser both
    /// guarantee it), so the first match is the answer.
    /// </summary>
    internal int HighlightedIndex
    {
        get
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                VRCard c = _cards[i];
                if (c != null && !c.IsHeld && c.IsHighlighted)
                    return i;
            }
            return -1;
        }
    }

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
        _gazeBiasYaw = 0f;  // edge-read fix: start facing the head squarely; ease into any bias
        // Card presentation: open with the bow apex at the fan centre (the symmetric, neutral shape)
        // and let the gaze ease it outward — a fan that popped open already leaning would read as a
        // glitch, and the first frames are exactly when the head is still moving toward the hand.
        _gazeX = 0f;
        _layoutGazeX = float.NaN; // force the first steady frame to re-lay out (no stale gate)
        IsOpen = true;
        Current = this; // expose the open fan to the net presence sender (hand-card count)
        _closeElapsed = -1f; // reopen mid-collapse: the open animation takes over from here
        // Demeo fan-in: start the fan-out reveal (cards seed collapsed at the center pose
        // this frame via the Relayout blend at t=0, then Tick flies them out). 0 = instant.
        _openElapsed = CardsConfig.FanOpenDuration.Value > 0f ? 0f : -1f;
        Relayout(instant: true);
        // Seed the live-preview baseline so the first Tick doesn't fire a spurious relayout; any
        // config edited WHILE the fan was closed is already reflected by this Open's Relayout.
        _lastFanParamSig = FanParamSignature();
    }

    internal void Close()
    {
        IsOpen = false;
        if (ReferenceEquals(Current, this))
            Current = null;
        ClearFingertipHover(); // test #9: drop any fingertip pop/split
        _insertGap = -1;       // hand reorder: drop any open gap so a reopen starts closed
        _openElapsed = -1f;
        if (_overlay != null)
            _overlay.SetActive(false);
        // Quick collapse (reverse of the Demeo fan-in): keep the root visible and let Tick
        // blend the cards back into the center stack before hiding — a cheap mirrored
        // hide animation. Disabled (or nothing to animate) = the pre-animation instant off.
        if (_root != null && _root.gameObject.activeSelf && _cards.Count > 0
            && CardsConfig.FanCloseDuration.Value > 0f)
        {
            _closeElapsed = 0f;
            return;
        }
        _closeElapsed = -1f;
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
        _openElapsed = -1f;
        _closeElapsed = -1f;
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
        // Collapse-in-flight: the card set changed under a hide animation — finish the hide
        // instantly so a stale stack never lingers under the incoming set.
        if (!IsOpen && _closeElapsed >= 0f)
        {
            _closeElapsed = -1f;
            if (_root != null)
                _root.gameObject.SetActive(false);
        }
        if (_overlay != null)
            _overlay.SetActive(false);
        _cards.Clear();
        for (int i = 0; i < cards.Count; i++)
            _cards.Add(cards[i]);
        if (IsOpen)
        {
            Relayout(instant: false);
        }
        else if (_root != null)
        {
            // Task #4b (collision safety): a card handed BACK to a CLOSED fan used to
            // keep its old parent/home forever — Relayout only runs while open — so a
            // slotted card the game deselected (short rest's DeselectAllCards, an undo,
            // a rejected select) kept LYING in the tray recess while e.g. the short-rest
            // sacrifice display docked into the very same recess (the reported overlap
            // glitch: build 8b0553034 log, 'Short rest: presenting sacrificed card ...
            // in the left slot' with the played card still homed there). A closed fan
            // owns its cards HIDDEN in the hand: adopt any card not already parented
            // under the (inactive) fan root now; the next Open() re-lays them out.
            for (int i = 0; i < _cards.Count; i++)
            {
                VRCard c = _cards[i];
                if (c != null && !c.IsHeld && c.transform.parent != _root)
                    c.SetHome(_root, Vector3.zero, Quaternion.identity, 1f, instant: true);
            }
        }
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

        // Same layout geometry as Relayout (base arc, no split/gap offsets), including the SAME
        // composed depth (gaze apex + stacking clamp) so the gap the drop maps to keeps matching the
        // cards as they are actually drawn.
        float radius = Mathf.Max(0.02f, CardsConfig.FanEffectiveRadius.Value);
        float maxArc = Mathf.Clamp(CardsConfig.FanArcSweepDegrees.Value, 5f, 180f);
        float stepCap = Mathf.Clamp(CardsConfig.FanPerCardStepDegrees.Value, 1f, 60f);
        float archFactor = CardsConfig.FanFlatCurvatureFactor.Value;
        if (CardsConfig.FanCurveByFill.Value)
            archFactor *= Mathf.Clamp01((float)n / Mathf.Max(1, CardsConfig.FanMaxHandForCurve.Value));
        float step = n > 1 ? Mathf.Min(stepCap, maxArc / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;
        ComposeDepths(n);

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
            float cz = _depths[i]; // match the bowed layout so the gap maps in depth too
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
        if (_root == null)
            return;

        // Hide animation: after Close() the root stays visible while the cards blend back
        // into the center stack; finish by deactivating. Runs on unscaled time like the
        // reveal (the fan stays frozen in place — it no longer follows the palm).
        if (!IsOpen)
        {
            if (_closeElapsed >= 0f)
                TickCollapse();
            return;
        }

        if (_hand == null)
            return;

        Transform? palm = _hand.Rig.PalmCenter;
        if (palm == null)
            return;

        // Fan-out reveal: advance the open animation and re-blend the homes every frame
        // (Relayout applies the collapsed→slot lerp while _openElapsed >= 0). dt is capped
        // so a hitch cannot teleport the cards; unscaled so a paused game still animates.
        bool wasOpening = _openElapsed >= 0f;
        if (_openElapsed >= 0f)
        {
            _openElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Relayout(instant: true);
            if (_openElapsed >= OpenAnimTotal(_cards.Count))
            {
                _openElapsed = -1f;
                Relayout(instant: false); // hand the (identical) homes back to the normal per-card lerp
            }
        }

        // Real-time fan-tuning preview: while the fan is STEADILY open (not mid-reveal — the reveal
        // relayouts itself every frame with the live config), re-lay it out the instant any
        // steady-layout Fan param changes, so debug-menu/cfg edits show without a close+reopen. The
        // relayout uses instant:false (VRCard lerps each card to its new home) and Relayout skips
        // any card being plucked (IsHeld) + preserves the hover split — so this never stomps a grab
        // or fights the reveal/collapse animation. Allocation-free: one float compare per frame.
        if (wasOpening)
        {
            _lastFanParamSig = FanParamSignature(); // keep the baseline fresh through the reveal
        }
        else
        {
            float sig = FanParamSignature();
            if (sig != _lastFanParamSig) // NaN seed compares unequal → first steady frame just baselines
            {
                bool firstSeed = float.IsNaN(_lastFanParamSig);
                _lastFanParamSig = sig;
                if (!firstSeed)
                {
                    Relayout(instant: false);
                    float now = Time.unscaledTime;
                    if (now - _paramLogTime > 0.5f) // throttled: confirm a live re-layout fired
                    {
                        _paramLogTime = now;
                        Core.VRLog.Info("Cards",
                            $"Fan live re-layout: params changed (n={_cards.Count}) " +
                            $"edgeDepth={BowDepth(0, _cards.Count, GazeApexIndex(_cards.Count)) * 1000f:F1}mm " +
                            $"curve={CardsConfig.FanSideDepthCurve.Value:F3}m");
                    }
                }
            }
        }

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
        Vector3 headPos = head.transform.position;
        Vector3 away = _root.position - headPos;
        if (away.sqrMagnitude < 1e-6f)
            return;
        // Cards' +Z points away from the viewer (uGUI reads from -Z). Base billboard: the whole
        // fan faces the head POSITION, pivoted on the fan center (the palm).
        Quaternion baseFacing = Quaternion.LookRotation(away.normalized, Vector3.up);

        // Gaze-responsive facing (edge-read): OPT-IN and OFF by default — the user prefers the
        // steady billboard plus the depth curvature (see RestBowDepth / Relayout, driven by
        // [Cards] FanSideDepthCurve — there is no "SideDepth" member) as the primary
        // shape response. When [Cards] FanGazeBias is ON, UpdateGazeBias returns an eased extra
        // YAW (deg, about world up) that turns the fan partway toward the head's GAZE so the
        // looked-at end tips TOWARD the viewer; a WIDE deadzone + side-hysteresis stop it
        // dithering as the head shakes across the fan center (the reported indecisiveness).
        float biasYaw = 0f;
        if (CardsConfig.FanGazeBias.Value)
            biasYaw = UpdateGazeBias(away, head.transform.forward);
        else if (_gazeBiasYaw != 0f || _gazeSide != 0)
        {
            _gazeBiasYaw = 0f; // disabled mid-bias: drop any residual so re-enabling eases from center
            _gazeSide = 0;
        }
        _root.rotation = biasYaw != 0f
            ? Quaternion.AngleAxis(biasYaw, Vector3.up) * baseFacing
            : baseFacing;

        // Card presentation (edge-read fix): track where the gaze crosses the fan plane and re-lay the
        // fan out when it — or the head's distance to the hand — has moved enough to matter. LAST in
        // Tick, so it measures the gaze in the fan frame we just aimed this frame (no lag between the
        // billboard and the presentation) and so any relayout above (reveal / live tuning) has already
        // run and cannot stomp it.
        UpdateCardPresentation(head);
    }

    // ------------------------------------------------------------------ gaze-facing bias --

    // Gaze-responsive fan facing (edge-read fix). LOCAL tunables with sane defaults until the
    // orchestrator promotes them to live-tunable CardsConfig "Fan" entries — mirrors the
    // FanInsertGapFactor precedent above. All are pure yaw about world up (matching the fan's
    // horizontal arc); head PITCH is projected out so looking down at the fan adds no bias.

    // ROUND-2 AUDIT NOTE (the settings panel labels the FanGazeBias toggle "Blick-Neigung", so it is the
    // first suspect for any "felt threshold" report): this block is NOT a source of a discontinuity. The
    // _gazeSide latch is provably output-NEUTRAL — the weight t is exactly 0 whenever |gazeOffset| is
    // inside GazeBiasDeadzoneDeg, and outside it the latch has always just re-committed to
    // sign(gazeOffset) — so the emitted target reduces to a smooth ODD function of gazeOffset, C¹ at the
    // dead-zone edge (smoothstep' = 0 there). What IS worth knowing: GazeBiasDeadzoneDeg (20°) is WIDER
    // than the angle the whole fan subtends at a normal hand distance (~±18° for a 10-card hand at
    // 0.4 m), so with this toggle ON the yaw bias contributes nothing at all while the player scans
    // their own hand and only starts once they look PAST it. If a future report says the lean "only
    // kicks in when I look away from the cards", that mismatch is the reason — not a latch.

    /// <summary>Gaze offset (deg off "looking straight at the fan center") the head must CLEAR before the
    /// bias commits to a side. WIDE (was 12°) — the center zone where the fan stays squarely billboarded,
    /// so ordinary head motion (and a left-right shake crossing center) never nudges the lean. Paired with
    /// <see cref="GazeBiasReleaseDeg"/> for hysteresis: once committed to a side the bias only relaxes
    /// back to center when the gaze returns inside the smaller release band — it cannot dither at center.</summary>
    private const float GazeBiasDeadzoneDeg = 20f;

    /// <summary>Gaze offset (deg) at which a committed side RELEASES back to center (hysteresis floor,
    /// below GazeBiasDeadzoneDeg). The gate holds its current side between this and the deadzone, so a
    /// head shake sweeping through center cannot rapid-flip the lean's sign.</summary>
    private const float GazeBiasReleaseDeg = 10f;

    /// <summary>Gaze offset (deg) at which the bias reaches full weight (smoothstep-ramped between
    /// the dead zone and here). ~the half-arc a full hand subtends, so an edge card hits full bias.</summary>
    private const float GazeBiasFullDeg = 42f;

    /// <summary>Fraction of the gaze offset the fan turns toward the gaze at full weight: 1 = the
    /// fan faces squarely along the gaze, 0.6 opens the gazed edge forward without full gaze-lock
    /// swim (keeps the fan feeling attached to the palm, not head-locked).</summary>
    private const float GazeBiasGain = 0.6f;

    /// <summary>Hard clamp on the applied extra yaw (deg): caps the swing so a glance far past the
    /// fan can never spin it around — nausea / "still on my hand" guard.</summary>
    private const float GazeBiasMaxYawDeg = 32f;

    /// <summary>Exponential ease rate (1/s) of the applied yaw toward its target — smooth, no jitter,
    /// no snap on a quick head flick. UNSCALED time so it stays alive while the game pauses for
    /// card selection (like the fan-out reveal).</summary>
    private const float GazeBiasSmoothing = 9f;

    /// <summary>Eased state: extra yaw (deg, about world up) currently applied to the fan facing.</summary>
    private float _gazeBiasYaw;

    /// <summary>Hysteresis state: which side the gaze bias is currently COMMITTED to (0 = center/none,
    /// -1 = leaning toward the viewer's left / card i=0, +1 = right). The target yaw sign is driven by
    /// THIS, not the instantaneous SignedAngle, so a head shake across center cannot flip the lean.</summary>
    private int _gazeSide;

    /// <summary>Throttle clock for the gaze-bias diagnostic (unscaled seconds of the last line).</summary>
    private float _gazeLogTime;

    /// <summary>
    /// Compute + ease the gaze-facing bias. <paramref name="away"/> is head->fan (the base
    /// billboard forward); <paramref name="headForward"/> is the gaze. Returns the eased extra yaw
    /// in degrees about world up (0 = no bias / within the center dead zone). Allocation-free.
    /// </summary>
    private float UpdateGazeBias(Vector3 away, Vector3 headForward)
    {
        Vector3 up = Vector3.up;
        Vector3 awayH = Vector3.ProjectOnPlane(away, up);
        Vector3 gazeH = Vector3.ProjectOnPlane(headForward, up);

        float target = 0f;
        float gazeOffset = 0f;
        if (awayH.sqrMagnitude > 1e-6f && gazeH.sqrMagnitude > 1e-6f)
        {
            // Signed horizontal angle of the gaze off "looking straight at the fan center".
            // AngleAxis(gazeOffset, up) rotates awayH exactly onto gazeH (same Unity sign
            // convention as SignedAngle), so turning the fan toward the gaze is sign-consistent.
            gazeOffset = Vector3.SignedAngle(awayH, gazeH, up);
            float mag = Mathf.Abs(gazeOffset);
            int side = gazeOffset < 0f ? -1 : 1;

            // DITHER FIX (hysteresis): the old code fed gazeOffset's raw sign straight into the
            // target, so as the head shook across the fan center SignedAngle flipped sign every
            // few degrees and the fan swung indecisively. Now the COMMITTED side is a latch:
            //   center (0) -> a side only once the gaze clears the WIDE deadzone;
            //   a side -> center only once the gaze falls back inside the smaller release band.
            // Between release and deadzone the side is held, so a sweep through center parks the
            // lean at center (target 0) and re-commits decisively past the deadzone — never a
            // rapid sign flip. The target's sign comes from _gazeSide, not the live gazeOffset.
            if (_gazeSide == 0)
            {
                if (mag > GazeBiasDeadzoneDeg)
                    _gazeSide = side;
            }
            else if (mag < GazeBiasReleaseDeg)
            {
                _gazeSide = 0;
            }
            else if (side != _gazeSide && mag > GazeBiasDeadzoneDeg)
            {
                _gazeSide = side; // firm, past-deadzone crossing to the opposite side
            }

            if (_gazeSide != 0)
            {
                float t = Mathf.Clamp01((mag - GazeBiasDeadzoneDeg)
                                        / Mathf.Max(0.01f, GazeBiasFullDeg - GazeBiasDeadzoneDeg));
                t = t * t * (3f - 2f * t); // smoothstep ease-in/out of the weight
                target = Mathf.Clamp(_gazeSide * mag * GazeBiasGain * t,
                                     -GazeBiasMaxYawDeg, GazeBiasMaxYawDeg);
            }
        }
        else
        {
            _gazeSide = 0;
        }

        // Ease toward the target on unscaled, frame-rate-independent time (alive while paused).
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        _gazeBiasYaw = Mathf.Lerp(_gazeBiasYaw, target, 1f - Mathf.Exp(-GazeBiasSmoothing * dt));
        if (Mathf.Abs(_gazeBiasYaw) < 0.05f)
            _gazeBiasYaw = 0f;

        // Throttled diagnostic (>=1 s apart, only while off-center or biased): fan facing vs head
        // yaw + which END of the arc is under gaze. gazeOffset < 0 = gaze to the viewer's LEFT
        // (toward card i=0, fan-local -X); the bias then tips that left end toward the viewer.
        float now = Time.unscaledTime;
        if ((Mathf.Abs(gazeOffset) > GazeBiasDeadzoneDeg || Mathf.Abs(_gazeBiasYaw) > 0.5f)
            && now - _gazeLogTime > 1f)
        {
            _gazeLogTime = now;
            string end = _gazeSide == 0 ? "CENTER" : _gazeSide < 0 ? "LEFT" : "RIGHT";
            Core.VRLog.Info("Cards",
                $"Fan gaze-bias: gazeOff={gazeOffset:F1}deg committed={end} biasYaw={_gazeBiasYaw:F1}deg (n={_cards.Count})");
        }

        return _gazeBiasYaw;
    }

    // ------------------------------------------------------------------ card presentation --
    //
    // "Der Kartenfächer sollte immer hin zum Spieler blicken": with a big hand, turning to look at
    // the OUTERMOST card made that card HARDER to read — the exact opposite of what looking at
    // something should do. Two independent causes, both fixed here, both continuous (no latch, so
    // nothing can flicker as the gaze crosses a card boundary):
    //
    //   (1) ONE billboard for TEN cards. Tick aims the fan ROOT at the head, so every card shares a
    //       single normal — the normal that points at the head from the fan CENTRE. The outermost
    //       card sits ~13 cm along the arc from that centre, i.e. ~15-20° off to the side at hand
    //       distance, so it is seen obliquely: turned away from the eye precisely when the player
    //       turns to read it. FIX: per-card TOE-IN (FanFaceViewer) — each card is additionally
    //       rotated by the minimal arc that puts ITS OWN normal on the head. That is what a real
    //       hand of cards does when you cup it, so it still reads as a held hand; and because it is
    //       driven by the head POSITION (not the gaze direction) it is rock stable — turning the
    //       head on the neck does not move it at all.
    //
    //   (2) The depth bow's apex was welded to the MIDDLE card. FanSideDepthCurve recedes cards away
    //       from the viewer with distance from the apex, so the card at the end of the arc — the one
    //       you turn your head toward — was by construction the one pushed FURTHEST back (the
    //       reported "sie geht weiter nach hinten"). FIX: the gaze RELIEVES the recession
    //       (FanGazeApexFollow). Looking at a card lifts that card (and, tapering off, its
    //       neighbours) out of the resting bow, so it sits at zero recession while every other card
    //       keeps at most the recession it had at rest.
    //
    // ------------------------------------------------------------------------------------------
    // ROUND 2 — "erst in die falsche Richtung, dann ab einem Punkt abrupt die Richtung gewechselt".
    // The first cut of (2) SLID the apex and RE-NORMALISED the bow around it
    // (frac = |i - apex| / max(apex, n-1-apex), i.e. "stretch over the long side"). Three separate
    // defects fell out of that one line, and together they are exactly the reported hectic
    // back-and-forth with a felt threshold:
    //
    //   * ROOT CAUSE A — ONE shared corner at the fan centre. max(apex, n-1-apex) switches branch at
    //     apex == (n-1)/2 for EVERY card at once, so all ten cards kink at the SAME gaze angle: the
    //     fan centre, which is the angle the gaze crosses most often. With the shipped defaults
    //     (10 cards, 35 mm bow, hand ~0.4 m from the head) card 0 travelled ~5.7 mm per degree of
    //     head yaw on one side of centre and EXACTLY 0 mm/deg on the other — it is pinned at
    //     frac == 1 for the whole half-range in which it sits on the long side. That is the "point
    //     where the gaze tilt starts to grip" the user can feel: a hard velocity step from full
    //     speed to dead stop, superposed across the whole hand.
    //
    //   * ROOT CAUSE B — the promise "no card ever ends up further back than with the old symmetric
    //     bow" was simply FALSE, and that is the "wrong direction" half of the report. Because the
    //     bow was re-normalised by the LONG side, a card near the middle of the hand gets a LARGER
    //     frac when the apex sits at an end than it had at rest: e.g. card 3 of 10 has frac 0.33 at
    //     rest (3.9 mm) but frac 0.67 with the apex on card 9 (15.6 mm). Measured on the shipped
    //     defaults, turning the head toward the right-hand end pushed cards 1..6 up to 12 mm
    //     FURTHER AWAY than the neutral shape — i.e. the fan first curls away from you, "wie
    //     vorher", and only the far end ever comes forward.
    //
    //   * ROOT CAUSE C — the stacking clamp makes the whole thing one-sided, so the two halves of a
    //     head sweep never felt alike. The clamp (see ComposeDepths) requires each card to sit one
    //     stagger in FRONT of its predecessor; since the stagger is itself a monotone ramp, that is
    //     algebraically the requirement that the bow be NON-INCREASING in card index. So the bow can
    //     only ever be visible on the LOW-index side of its apex; the ascending flank is flattened
    //     onto the bare stagger ramp no matter what the bow says (the bow's slope, ~15 mm/card, is
    //     four times the 4 mm stagger). This is not a bug to remove — it is the draw order, and card
    //     n-1 must stay frontmost or the full-width collider Relayout hands it would be half-covered
    //     — but it means the response is inherently "un-curl the back of the hand", never "raise the
    //     front of it", and the maths must be shaped for that instead of fighting it.
    //
    // ROUND 2 FIX — the gaze is a multiplicative RELIEF of a FIXED resting bow, not a moving apex:
    //
    //     rest(i)  = FanSideDepthCurve · (|i - (n-1)/2| / ((n-1)/2))^FanCurvePower · fill
    //     bow(i)   = rest(i) · (1 - FanGazeApexFollow · exp(-((i - apex) / w)²))
    //
    //   rest(i) is the historic symmetric bow VERBATIM and no longer depends on the gaze at all, so:
    //     - B is fixed by construction: 0 ≤ bow(i) ≤ rest(i) for every card at every gaze angle, so
    //       no card can EVER be pushed further back than the pre-feature shape. The promise now
    //       holds as an identity rather than as a hope.
    //     - A is gone: there is no max(), no branch and no clamp corner anywhere in the apex, and
    //       because the relief is a Gaussian in (i - apex) the derivative with respect to the apex is
    //       continuous everywhere — including at apex == i, where exp' = 0. The response to a head
    //       turn is C¹ over the entire range and saturates smoothly at the two ends of the arc
    //       instead of stepping to zero at the centre.
    //     - Monotonicity is now structural: rest(i) is a constant, and exp(-((i-apex)/w)²) increases
    //       strictly as the apex approaches card i. Turning the head further toward a card therefore
    //       strictly reduces that card's recession and nothing else's — one extremum per card, at
    //       apex == i, so there is no reversal to feel.
    //     - C is respected rather than fought: the relief only ever LOWERS the bow, so the clamp
    //       binds exactly where it bound before (or less). The visible behaviour is "looking toward
    //       the back of the hand un-curls it, looking away lets it settle back to the neutral cup",
    //       which is monotone on both sides of the sweep.
    //   w (GazeReliefWidth*) is the relief's half-width in CARDS: wide enough that the neighbours of
    //   the gazed card come with it (so the hand opens toward the gaze rather than a single card
    //   popping), narrow enough that the far end keeps its cup.
    //
    //   FanGazeApexFollow now scales the relief AMPLITUDE (it used to lerp the apex position toward
    //   the centre). 0 is still the byte-exact revert — the relief term vanishes and bow(i) == the
    //   historic rest(i) — and intermediate values now mean "lift the gazed card partway out of the
    //   bow", which is what the name promises, instead of "put the apex under the wrong card".
    //
    // WHY NOT the alternatives we were asked to weigh:
    //   * Rotating the WHOLE fan toward the gaze (the previous attempt, [Cards] FanGazeBias) is a
    //     see-saw: the gazed end swings toward the viewer only by swinging the other end away, the
    //     whole hand visibly swims off the palm as the head turns, and a sign flip near the centre
    //     needs deadzones + hysteresis latches that then make the fan feel indecisive. Kept, but
    //     still opt-in and OFF; this region does not depend on it.
    //   * Killing the per-card ROLL for the gazed card would flatten the fan's signature shape for
    //     no readability gain (a rolled card is still face-on to the eye — roll does not foreshorten).
    //   * A forward "pop" of the gazed card would fight the hover pop (VRCard's 35 mm laser/fingertip
    //     raise) and, worse, could invert the hand's stacking order mid-fan and z-fight. Presentation
    //     is done purely by REMOVING recession, never by adding a raise.
    //
    // The apex is tracked as a fan-LOCAL X (metres) where the gaze ray pierces the fan plane — a
    // continuous, card-count-independent quantity — and eased exponentially (FanGazeSmoothing,
    // unscaled time so it stays alive while the game pauses for card selection). It is converted to a
    // fractional card index only at layout time. No index quantisation anywhere = no boundary flicker.
    //
    // The gaze POINT itself is likewise free of hard gates now (round 2). It used to be dropped to
    // the fan centre the moment the gaze got within ~78° of parallel to the fan plane
    // (gazeLocal.z > 0.2f), which is a jump straight from a saturated end apex to the middle of the
    // hand — a second felt threshold, just at a rarer angle. It is now weighted by a smoothstep over
    // gazeLocal.z (GazeGrazeMinZ..GazeGrazeFullZ) and the intersection is saturated at the ARC's own
    // half-width (the outermost card's x) rather than at the arc RADIUS, so _gazeX is directly
    // comparable to a card's x in the log and reaches the end card exactly when the gaze does.
    //
    // PICK SAFETY (the recurring, expensive bug in this project): everything here goes into the pose
    // handed to VRCard.SetHome — and CardFan.TryRaycast builds its rect from VRCard's HOME pose
    // (TryGetRestingLaserRect reads _homePos/_homeRot). So the toe-in rotation and the apex-shifted
    // depth are, by construction, part of the pick geometry: the laser keeps hitting exactly what is
    // drawn. Nothing here is applied to the live transform behind the home's back.

    /// <summary>Eased fan-local X (metres) where the head's gaze pierces the fan plane — the point the
    /// depth-bow apex is drawn toward. 0 = looking at the fan centre.</summary>
    private float _gazeX;

    /// <summary>Diagnostic only: the raw fan-local gaze YAW in degrees (atan2 of the gaze direction),
    /// i.e. how far the head is turned off "looking straight at the fan centre". Logged next to the
    /// apex so a head sweep can be read off the log as a (yaw -> apex) curve and checked for
    /// monotonicity. Never feeds the layout.</summary>
    private float _gazeYawDeg;

    /// <summary>The arc's own half-width in fan-local metres (the outermost card's |x|): the saturation
    /// bound for <see cref="_gazeX"/> and the log's reference for "the gaze has reached the end card".</summary>
    private float _gazeEdgeX = 0.1f;

    /// <summary>Gate state: the <see cref="_gazeX"/> the last <see cref="Relayout"/> used (NaN = none yet).</summary>
    private float _layoutGazeX = float.NaN;

    /// <summary>Gate state: the fan-local head position the last <see cref="Relayout"/> toed in toward.</summary>
    private Vector3 _layoutHeadLocal;

    /// <summary>Apex drift (metres of fan-local X) that triggers a re-layout. ~2 mm at the fan plane is
    /// well under a tenth of a card, so a still head never re-lays out and a turning head does so
    /// smoothly — this is a work gate only, it can never make the motion steppy (the cards' own
    /// exponential home-lerp smooths whatever the gate lets through).</summary>
    private const float GazeRelayoutEpsilon = 0.002f;

    /// <summary>Head-motion (fan-local metres) that triggers a re-layout for the TOE-IN. The fan
    /// billboards at the head, so this local position is essentially (0, 0, -distance): it only moves
    /// when the player moves the hand toward/away from their face, not when they turn their head.</summary>
    private const float ToeInRelayoutEpsilon = 0.003f;

    /// <summary>
    /// Per-frame gaze tracking + gated re-layout. Called at the END of <see cref="Tick"/>, AFTER the
    /// root has been positioned and aimed, so the fan-local frame we measure the gaze in is this
    /// frame's frame (no one-frame lag between the billboard and the presentation).
    /// </summary>
    private void UpdateCardPresentation(Camera head)
    {
        if (_root == null)
            return;

        _gazeEdgeX = ArcHalfWidth(_cards.Count);

        // Where does the gaze cross the fan plane? In fan-local space the head sits at ≈(0,0,-d)
        // (the root billboards at it) and the cards lie in the z≈0 plane, so the crossing X is
        // d * tan(gaze yaw off the fan) — exactly "which card am I looking at", continuously.
        Transform ht = head.transform;
        Vector3 headLocal = _root.InverseTransformPoint(ht.position);
        Vector3 gazeLocal = _root.InverseTransformDirection(ht.forward);
        _gazeYawDeg = Mathf.Atan2(gazeLocal.x, gazeLocal.z) * Mathf.Rad2Deg; // diagnostic only

        // CONTINUITY FIX (round 2): the old gate was a hard `gazeLocal.z > 0.2f`, which SNAPPED the
        // apex from wherever it was straight back to the fan centre the instant the gaze got near
        // parallel to the fan plane — a felt jump of up to the whole half-hand. The grazing case is
        // real (the intersection blows up as gazeLocal.z -> 0) but the cure must be continuous, so it
        // is now a smoothstep weight: full effect while the gaze is properly pointed at the hand,
        // fading to "centred" as it swings toward parallel, with the divisor floored so the
        // intersection can never explode inside the fade. In ordinary play (hand anywhere within
        // ~70° of the gaze) front == 1 and this is bit-identical to the plain intersection.
        float targetX = 0f;
        if (headLocal.z < 0f)
        {
            float front = Mathf.Clamp01((gazeLocal.z - GazeGrazeMinZ)
                                        / Mathf.Max(0.01f, GazeGrazeFullZ - GazeGrazeMinZ));
            front = front * front * (3f - 2f * front); // smoothstep: C¹ at both ends of the fade
            if (front > 0f)
            {
                float cross = headLocal.x
                              + gazeLocal.x * (-headLocal.z / Mathf.Max(gazeLocal.z, GazeGrazeMinZ));
                // Saturate at the ARC's half-width, not the arc RADIUS: the outermost card sits at
                // sin(halfSweep)·radius, well inside the radius, so clamping at the radius let _gazeX
                // keep growing after the apex had already reached the end card (a dead band that made
                // the effect feel like it "stopped gripping"). Clamping here makes _gazeX and a card's
                // x directly comparable — and the log's gazeX/edge pair immediately readable.
                targetX = Mathf.Clamp(cross, -_gazeEdgeX, _gazeEdgeX) * front;
            }
        }

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float rate = Mathf.Clamp(CardsConfig.FanGazeSmoothing.Value, 1f, 30f);
        _gazeX = Mathf.Lerp(_gazeX, targetX, 1f - Mathf.Exp(-rate * dt));

        // The reveal owns the layout while it runs (it relayouts every frame with the live apex).
        if (_openElapsed >= 0f)
            return;

        bool moved = float.IsNaN(_layoutGazeX)
                     || Mathf.Abs(_gazeX - _layoutGazeX) > GazeRelayoutEpsilon
                     || (CardsConfig.FanFaceViewer.Value > 0f
                         && (headLocal - _layoutHeadLocal).sqrMagnitude
                            > ToeInRelayoutEpsilon * ToeInRelayoutEpsilon);
        if (!moved)
            return;

        // [Optimize] FanRelayoutMinInterval (2026-07 perf pass). The gate above trips on ~2 mm of
        // apex drift, which during a fast head turn is EVERY SINGLE FRAME — and a relayout touches
        // every card's home pose, rotation and collider region. That is the mod's most obviously
        // head-motion-CORRELATED cost, i.e. the first suspect for "the world judders when I turn my
        // head fast", so it gets an explicit rate limit.
        //
        // DEFAULT 0 = off = today's behaviour, deliberately: the relayout is not KNOWN to be
        // expensive (the collider write is already idempotence-guarded in VRCard.SetColliderRegion,
        // and the [Perf] STEPS line now measures it), so this ships as a lever to test with, not as
        // a silent change. When it IS switched on nothing can look steppy: each card's own
        // exponential home-lerp keeps running every frame and smooths whatever the gate lets
        // through — the rate limit changes how often the TARGET moves, not how the cards travel.
        // Only the GAZE path is limited; card-set changes, hovers, plucks, insert gaps and the
        // fan-out reveal all call Relayout directly and are never delayed.
        float minInterval = Core.PerfConfig.FanRelayoutInterval;
        if (minInterval > 0f)
        {
            float now = Time.unscaledTime;
            if (now - _lastGazeRelayoutTime < minInterval)
                return;
            _lastGazeRelayoutTime = now;
        }
        Relayout(instant: false);
    }

    /// <summary>Unscaled time of the last GAZE-driven relayout ([Optimize] FanRelayoutMinInterval).</summary>
    private float _lastGazeRelayoutTime = float.NegativeInfinity;

    /// <summary>Gaze/fan-plane angle below which the crossing point is faded out (fan-local gaze +Z
    /// component). Under this the gaze is effectively parallel to the fan and the intersection is
    /// meaningless; the divisor is floored here too so it can never blow up.</summary>
    private const float GazeGrazeMinZ = 0.05f;

    /// <summary>Gaze/fan-plane angle at which the crossing point counts at FULL weight (fan-local gaze
    /// +Z). ~70° off the fan — comfortably outside anything the player does while reading their hand,
    /// so the fade is invisible in normal play and only tames the degenerate grazing case.</summary>
    private const float GazeGrazeFullZ = 0.35f;

    /// <summary>
    /// The arc's own half-width in fan-local metres for a hand of <paramref name="n"/>: the |x| of the
    /// outermost card, x = sin(step·(n-1)/2)·radius. Shares the exact step/sweep/radius derivation used
    /// by <see cref="Relayout"/> so the gaze saturation and the cards can never disagree.
    /// </summary>
    private static float ArcHalfWidth(int n)
    {
        float radius = Mathf.Max(0.02f, CardsConfig.FanEffectiveRadius.Value);
        if (n < 2)
            return radius;
        float maxArc = Mathf.Clamp(CardsConfig.FanArcSweepDegrees.Value, 5f, 180f);
        float stepCap = Mathf.Clamp(CardsConfig.FanPerCardStepDegrees.Value, 1f, 60f);
        float step = Mathf.Min(stepCap, maxArc / (n - 1));
        float half = step * (n - 1) * 0.5f;
        return Mathf.Max(0.001f, Mathf.Sin(half * Mathf.Deg2Rad) * radius);
    }

    /// <summary>
    /// The gaze apex as a FRACTIONAL card index for a hand of <paramref name="n"/>: the card the eased
    /// gaze point <see cref="_gazeX"/> lands on. Inverts the arc's x = sin(angle)·radius placement, so
    /// it stays correct at any radius/step/sweep, and is clamped to the real card range so looking past
    /// the end of the hand SATURATES on the end card (monotone: it can never wrap or reverse).
    ///
    /// ROUND 2: this no longer blends toward the geometric centre by FanGazeApexFollow. That blend was
    /// "put the apex under the WRONG card at partial strength"; the strength now scales the RELIEF
    /// amplitude in <see cref="BowDepth"/> instead, which is what the setting's name actually promises.
    /// FanGazeApexFollow = 0 still returns exactly (n-1)/2 (and the relief term vanishes), so the
    /// feature remains a bit-identical revert to the pre-change symmetric bow from the debug menu.
    /// </summary>
    private float GazeApexIndex(int n)
    {
        float center = (n - 1) * 0.5f;
        float follow = Mathf.Clamp01(CardsConfig.FanGazeApexFollow.Value);
        if (n < 2 || follow <= 0f)
            return center;

        float radius = Mathf.Max(0.02f, CardsConfig.FanEffectiveRadius.Value);
        float maxArc = Mathf.Clamp(CardsConfig.FanArcSweepDegrees.Value, 5f, 180f);
        float stepCap = Mathf.Clamp(CardsConfig.FanPerCardStepDegrees.Value, 1f, 60f);
        float step = Mathf.Min(stepCap, maxArc / (n - 1));
        if (step <= 0.0001f)
            return center;
        float start = -step * (n - 1) * 0.5f;

        float angle = Mathf.Asin(Mathf.Clamp(_gazeX / radius, -1f, 1f)) * Mathf.Rad2Deg;
        return Mathf.Clamp((angle - start) / step, 0f, n - 1f);
    }

    /// <summary>
    /// Scratch buffer of composed per-card local Z (stagger + bow + stacking clamp), filled by
    /// <see cref="ComposeDepths"/>. A field so the per-frame layout stays allocation-free.
    /// </summary>
    private float[] _depths = new float[16];

    /// <summary>
    /// Compose every card's fan-local Z for a hand of <paramref name="n"/> and return the apex index
    /// used. Single source of truth for the fan's depth so <see cref="Relayout"/>,
    /// <see cref="TickCollapse"/> and <see cref="NearestGap"/> can never drift apart.
    ///
    /// z = -ZStagger·i (the hand's stacking: each card draws in front of its left neighbour)
    ///     + bow(i)    (the apex-relative recession, <see cref="BowDepth"/>)
    ///
    /// STACKING CLAMP (positive curve only): the bow is a translation, so a strong bow can out-run
    /// the 4 mm stagger and INVERT the stack — with the shipped defaults (10 cards, 35 mm bow) cards
    /// 7-9 already ended up BEHIND card 6, so the right half of the fan overlapped backwards and the
    /// last card (the one Relayout hands a FULL-width collider, on the assumption that it is fully
    /// exposed) was actually half-covered by its neighbour. That inversion is a big part of why an
    /// outer card read as "sunk". We therefore clamp each card to sit at least one stagger in FRONT
    /// of its predecessor: the bow can curl the hand away, but it can never re-order it. The clamp
    /// only ever pulls cards TOWARD the viewer, is continuous in the apex (at the crossover the
    /// constraint is exactly met, so nothing pops as the apex slides), and is skipped for a NEGATIVE
    /// FanSideDepthCurve — a viewer-bulging bow is a deliberate opt-in whose whole point is the
    /// other stacking direction.
    ///
    /// WHAT THE CLAMP IMPLIES FOR THE GAZE RESPONSE (round-2 root cause C — worth stating, because it
    /// is the reason the fix is shaped the way it is): "each card one stagger in front of its
    /// predecessor" is, after the -ZStagger·i ramp is substituted in, algebraically the requirement
    /// that bow(i) be NON-INCREASING in i. A bow can therefore only ever be VISIBLE on the low-index
    /// side of its deepest point; the ascending flank is flattened onto the bare stagger ramp whatever
    /// the bow says (the bow's slope, ~15 mm/card on the defaults, is four times the 4 mm stagger).
    /// So the gaze response is physically "un-curl the back of the hand toward where I am looking",
    /// never "raise the front of it" — and <see cref="BowDepth"/>'s relief is built to do exactly that
    /// monotonically, instead of the old moving apex which tried to re-centre a bow the clamp then
    /// half-erased at a threshold. The clamp is left alone: it is the draw order, and card n-1 must
    /// stay frontmost or the full-width collider <see cref="Relayout"/> hands it would be half-covered.
    /// </summary>
    private float ComposeDepths(int n)
    {
        if (_depths.Length < n)
            _depths = new float[Mathf.NextPowerOfTwo(Mathf.Max(n, 16))];

        float apex = GazeApexIndex(n);
        for (int i = 0; i < n; i++)
            _depths[i] = -ZStagger * i + BowDepth(i, n, apex);

        if (CardsConfig.FanSideDepthCurve.Value > 0f)
        {
            for (int i = 1; i < n; i++)
            {
                float ceiling = _depths[i - 1] - ZStagger;
                if (_depths[i] > ceiling)
                    _depths[i] = ceiling;
            }
        }
        return apex;
    }

    // ------------------------------------------------------------------ layout --

    // Z distance between neighboring cards (meters, scale 1). Several times the
    // backing thickness so fanned cards can never interpenetrate visually — the
    // overlap is pure render order, like a real hand of cards (test #8 fix).
    private const float ZStagger = 0.004f;

    /// <summary>Throttle clock for the depth-curvature diagnostic (unscaled seconds of the last line).</summary>
    private float _curveLogTime;

    /// <summary>The apex the last "Fan depth-curve:" line reported. A change bigger than a tenth of a
    /// card means the player is mid-head-turn, which switches that line to the fast sweep cadence so a
    /// whole turn is recorded instead of two samples 2 s apart.</summary>
    private float _loggedApex = float.NaN;

    // ---- Real-time fan-tuning preview (self-contained, allocation-free) ----
    // The settings panel edits the GLOBAL Fan config while the fan is OPEN, but Relayout only ran
    // on open / card-set change, so a stepper edit only showed after closing + reopening the fan.
    // Tick now watches a cheap SIGNATURE of every STEADY-layout Fan param each frame and re-lays
    // the open fan out the instant any of them changes — so edits (step / arc / radius / split /
    // curvature / power / min-cards / arch / tilt / fill / card width) preview LIVE. The signature
    // is a weighted float sum (FanParamSignature); distinct multipliers make a same-sum collision
    // between realistic slider values vanishingly unlikely, and the relayout is idempotent, so a
    // rare miss/extra costs nothing. NaN seed = "recompute baseline, don't relayout" (set on Open
    // and refreshed every frame of the fan-out reveal, which relayouts itself already).

    /// <summary>Last-seen fan-param signature (NaN = uninitialised — reseeded without a relayout).</summary>
    private float _lastFanParamSig = float.NaN;

    /// <summary>Throttle clock for the live re-layout diagnostic (unscaled seconds of the last line).</summary>
    private float _paramLogTime;

    /// <summary>
    /// Allocation-free weighted signature of every Fan config value that changes the STEADY layout
    /// (<see cref="Relayout"/> + <see cref="RestBowDepth"/> + <see cref="SplitOffset"/>). Any edit to one
    /// term moves the sum, so Tick can detect a live tuning change with a single float compare. Animation
    /// durations (open/close/stagger), the gaze bias, the pop distance and the palm/follow params are
    /// EXCLUDED on purpose — they are already read live every frame (facing / follow / pop) or only matter
    /// mid-animation, so they need no relayout.
    /// </summary>
    private float FanParamSignature()
    {
        float s = 0f;
        s += CardsConfig.FanEffectiveRadius.Value * 7.1f;
        s += CardsConfig.FanArcSweepDegrees.Value * 3.3f;
        s += CardsConfig.FanPerCardStepDegrees.Value * 13.7f;
        s += CardsConfig.CardWidth.Value * 101.3f;
        s += CardsConfig.FanFlatCurvatureFactor.Value * 5.9f;
        s += CardsConfig.FanTiltFactor.Value * 17.3f;
        s += CardsConfig.FanMaxHandForCurve.Value * 2.7f;
        s += (CardsConfig.FanCurveByFill.Value ? 23.1f : 0f);
        s += CardsConfig.FanSplitMultiplier.Value * 211.7f;
        s += CardsConfig.FanSplitFalloff.Value * 19.9f;
        s += CardsConfig.FanHoverSplitScale.Value * 29.3f;
        s += CardsConfig.FanSideDepthCurve.Value * 307.1f;
        s += CardsConfig.FanCurvePower.Value * 11.1f;
        s += CardsConfig.FanCurveMinCards.Value * 6.1f;
        s += CardsConfig.FanFaceViewer.Value * 401.3f;      // per-card toe-in gain (steady shape)
        s += CardsConfig.FanGazeApexFollow.Value * 503.9f;  // bow-apex gaze follow (steady shape)
        return s;
    }

    /// <summary>
    /// The head (HMD) position in FAN-LOCAL space — the toe-in target and the frame the gaze is
    /// measured in. False when there is no camera yet (early frames / flat-screen), in which case the
    /// layout simply keeps the plain billboard orientation, exactly as before this feature existed.
    /// </summary>
    private bool TryGetHeadLocal(out Vector3 headLocal)
    {
        headLocal = default;
        if (_root == null)
            return false;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return false;
        headLocal = _root.InverseTransformPoint(head.transform.position);
        return true;
    }

    /// <summary>
    /// Depth curvature: the SIGNED bow of card <paramref name="i"/> of a hand of <paramref name="n"/>
    /// along the fan's local forward axis (fan-local +Z is AWAY from the viewer, since the fan faces the
    /// head with -Z), so a full hand bows into depth like a real held fan — with the card the player is
    /// looking at (<paramref name="apex"/>, the fractional index from <see cref="GazeApexIndex"/>) lifted
    /// out of that cup. Live-read each layout.
    ///
    /// The RESTING bow (<see cref="RestBowDepth"/>) is the symmetric, gaze-INDEPENDENT cup: the historic
    /// formula verbatim, normalised by the fixed half-span (n-1)/2. The gaze then RELIEVES it: card
    /// <paramref name="i"/> keeps rest(i) · (1 - FanGazeApexFollow · exp(-((i-apex)/w)²)) of its
    /// recession, so the card under <paramref name="apex"/> comes fully out of the bow and every other
    /// card keeps AT MOST what it had at rest.
    ///
    /// WHY MULTIPLICATIVE RELIEF AND NOT A MOVING APEX (the round-2 root cause — see the region header):
    /// the previous version re-normalised the bow by the LONGER side, max(apex, n-1-apex). That put a
    /// corner in EVERY card's response at the SAME gaze angle (the fan centre, where the max switches
    /// branch) — the felt threshold — and, because a mid-hand card's fraction GROWS when the apex slides
    /// to an end, it pushed the middle of the hand up to ~12 mm FURTHER back than the neutral shape: the
    /// reported "goes the wrong way first". With the relief form both defects are structurally
    /// impossible: 0 ≤ bow(i) ≤ rest(i) always (nothing can ever get worse than the pre-feature shape),
    /// and the only apex-dependent factor is a Gaussian, whose derivative in the apex is continuous
    /// everywhere — including at apex == i, where it is exactly 0. Each card therefore has one extremum,
    /// at "the gaze is on me", so turning the head further toward a card can only ever improve it.
    ///
    /// NOTE (raycast/collider safety, either sign): this shifts ONLY each card's local Z, never its
    /// rotation, and it goes into the card's HOME pose — the pluck raycast (<see cref="TryRaycast"/>)
    /// builds its plane from that same home pose (<see cref="VRCard.TryGetRestingLaserRect"/>), and the
    /// X/Y rect bounds are Z-independent, so the ray follows the moved card automatically — toward or
    /// away — and the hit rect is unchanged; the shrunken grab colliders
    /// (<see cref="VRCard.SetColliderRegion"/>) ride the transform likewise.
    /// </summary>
    private static float BowDepth(int i, int n, float apex)
    {
        float rest = RestBowDepth(i, n);
        if (rest == 0f)
            return 0f;
        float follow = Mathf.Clamp01(CardsConfig.FanGazeApexFollow.Value);
        if (follow <= 0f)
            return rest; // byte-exact revert: the historic symmetric bow, untouched by the gaze
        float w = Mathf.Max(GazeReliefMinWidth, (n - 1) * 0.5f * GazeReliefWidthFactor);
        float d = (i - apex) / w;
        return rest * (1f - follow * Mathf.Exp(-d * d));
    }

    /// <summary>Half-width of the gaze relief, as a fraction of the hand's half-span (in CARDS). Wide
    /// enough that the gazed card's neighbours come forward with it — the hand OPENS toward the gaze
    /// rather than one card popping out of the cup — and narrow enough that the far end keeps its cup.
    /// A LOCAL const (the FanInsertGapFactor / GazeBias* precedent): it changes the feel of an already
    /// smooth, monotone response, not whether the response is correct, so it does not earn a stepper.</summary>
    private const float GazeReliefWidthFactor = 0.55f;

    /// <summary>Floor on the relief half-width in cards, so a 4-5 card hand still relieves its
    /// neighbours instead of degenerating into a single-card notch.</summary>
    private const float GazeReliefMinWidth = 1.2f;

    /// <summary>
    /// The RESTING (gaze-independent) depth bow of card <paramref name="i"/> of a hand of
    /// <paramref name="n"/> — the historic symmetric cup, unchanged since before the gaze feature: rises
    /// with <see cref="CardsConfig.FanCurvePower"/> in the card's fraction-from-CENTRE (0 at the middle
    /// card, 1 at either end), scaled to <see cref="CardsConfig.FanSideDepthCurve"/> metres at the ends
    /// and ramped by hand size (flat at or below <see cref="CardsConfig.FanCurveMinCards"/>, full at
    /// <see cref="CardsConfig.FanMaxHandForCurve"/>) so a small hand stays nearly flat. A POSITIVE curve
    /// recedes the edge cards AWAY from the viewer; a NEGATIVE value bows them TOWARD the viewer. This
    /// is the CEILING the gaze relief works down from, which is what guarantees no card is ever pushed
    /// further back than the pre-feature shape. Returns 0 when disabled / a tiny hand.
    /// </summary>
    private static float RestBowDepth(int i, int n)
    {
        if (n < 2)
            return 0f;
        float curve = CardsConfig.FanSideDepthCurve.Value;
        if (curve == 0f)
            return 0f; // exactly flat; negative curve bows the edges TOWARD the viewer (handled below)
        int lo = Mathf.Clamp(CardsConfig.FanCurveMinCards.Value, 1, 64);
        if (n <= lo)
            return 0f; // small hand: stay flat
        float half = (n - 1) * 0.5f;                                   // FIXED half-span (no gaze term)
        float frac = Mathf.Clamp01(Mathf.Abs(i - half) / half);        // 0 centre .. 1 either end
        float power = Mathf.Clamp(CardsConfig.FanCurvePower.Value, 0.5f, 4f);
        int hi = Mathf.Max(lo + 1, CardsConfig.FanMaxHandForCurve.Value);
        float fill = Mathf.Clamp01((float)(n - lo) / (hi - lo)); // 0 at lo .. 1 at full hand
        return curve * Mathf.Pow(frac, power) * fill;
    }

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

        // Fan-out reveal (Demeo fan-in, CardHandView.cs:577-580): the collapsed pose is the
        // MIDDLE slot's base arc pose — every card seeds there and flies out to its own slot.
        int mid = n / 2;
        float midAngle = start + step * mid;
        float midRad = midAngle * Mathf.Deg2Rad;
        var collapsedRot = Quaternion.Euler(0f, 0f, -midAngle * tiltFactor);
        var collapsedXY = new Vector2(Mathf.Sin(midRad) * radius,
                                      (Mathf.Cos(midRad) - 1f) * radius * archFactor);
        bool opening = _openElapsed >= 0f;

        // Card presentation (see the region above): compose the per-card depth around the
        // gaze-following bow apex, and resolve the head in fan-local space for the per-card toe-in.
        // Both are recorded as the gate baseline so UpdateCardPresentation only re-lays out when the
        // player has actually moved enough to matter.
        float apex = ComposeDepths(n);
        float face = Mathf.Clamp01(CardsConfig.FanFaceViewer.Value);
        bool haveHead = TryGetHeadLocal(out Vector3 headLocal);
        bool toeIn = face > 0f && haveHead;
        _layoutGazeX = _gazeX;
        if (haveHead)
            _layoutHeadLocal = headLocal; // gate baseline, kept fresh even with the toe-in dialled out
        float maxToeDeg = 0f; // diagnostic: the largest per-card toe-in actually applied

        for (int i = 0; i < n; i++)
        {
            VRCard card = _cards[i];
            if (card == null || card.IsHeld)
                continue;
            if (!card.gameObject.activeSelf)
                card.gameObject.SetActive(true);

            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            // Arc bends around a pivot below the fan root; z-stagger keeps the draw order stable
            // (later cards nearer the viewer = -Z) and the composed depth bows the hand back into
            // depth (+Z, away from the viewer) around the gaze apex, so a full hand curves like a
            // real held fan AND the card being looked at is the one at the front of that curve.
            var rot = Quaternion.Euler(0f, 0f, -angle * tiltFactor);
            var pos = new Vector3(Mathf.Sin(rad) * radius,
                                  (Mathf.Cos(rad) - 1f) * radius * archFactor,
                                  _depths[i]);

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

            // Per-card TOE-IN: aim THIS card's normal at the head instead of inheriting the fan
            // root's single billboard normal. Applied AFTER the split/gap offsets so the aim uses
            // the card's final centre, and PRE-multiplied onto the roll so the roll (the fan's
            // signature shape) is preserved exactly — FromToRotation is the minimal arc from the
            // card's forward to the head, so it adds no twist of its own and cannot drift.
            if (toeIn)
            {
                Vector3 toCard = pos - headLocal;
                if (toCard.sqrMagnitude > 1e-6f)
                {
                    Vector3 dir = toCard.normalized;
                    var aim = Quaternion.FromToRotation(Vector3.forward, dir);
                    if (face < 1f)
                        aim = Quaternion.Slerp(Quaternion.identity, aim, face);
                    rot = aim * rot;
                    float deg = Vector3.Angle(Vector3.forward, dir) * face;
                    if (deg > maxToeDeg)
                        maxToeDeg = deg;
                }
            }

            // Fan-out reveal blend: fly each card from the collapsed center pose to its slot
            // with an ease-out and a tiny outward stagger. instant is forced so VRCard tracks
            // the blend exactly (its own home-lerp would double-smooth the motion). Each card
            // keeps its OWN z-stagger while collapsed so the draw order never flickers.
            if (opening)
            {
                float e = OpenProgress(i, mid);
                var collapsed = new Vector3(collapsedXY.x, collapsedXY.y, -ZStagger * i);
                pos = Vector3.Lerp(collapsed, pos, e);
                rot = Quaternion.Slerp(collapsedRot, rot, e);
            }

            card.SetHome(_root, pos, rot, 1f, instant || opening);

            if (i == n - 1)
                card.SetColliderRegion(w, 0f); // fully exposed — full width, exact-fit depth (no viewer-side apron)
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

        // Throttled fan-presentation diagnostic. Keeps the historic "Fan depth-curve:" prefix (existing
        // greps/log tooling). Round 2 makes it a SWEEP RECORDER: the throttle drops from 2 s to 0.25 s
        // while the apex is actually moving, so one head turn leaves a readable series of lines and the
        // response can be checked for continuity/monotonicity straight off the log — an idle fan still
        // costs one line every 2 s. Read a sweep as (gazeYaw -> apex -> bow0):
        //   gazeYaw  head yaw off "straight at the fan centre", fan-local degrees (the INPUT).
        //   gazeX    where that gaze crosses the fan plane / the arc's own half-width (the saturation
        //            bound) — |gazeX| == edge means the gaze has reached the end card.
        //   apex     the fractional card index the relief is centred on (the eased state).
        //   bow0     card 0's ACTUAL recession, and rest0 the resting bow it is relieved from: bow0
        //            must stay in [0, rest0] at every sample (the "can never get worse" invariant) and
        //            must move monotonically with gazeYaw — any reversal or step between consecutive
        //            lines is the bug this round fixed coming back.
        //   z=[..]   near/far composed depth spread AFTER the stacking clamp.
        // Grep: "Fan depth-curve:".
        // [Optimize] QuietDiagnostics: this is the one mod diagnostic whose cadence is TIED TO
        // HEAD MOTION — it deliberately speeds up to 4 Hz while the gaze apex sweeps, i.e. it emits
        // most during exactly the manoeuvre the player reports as juddery. 4 lines/s is still far
        // too little to cost frames (measured: the whole mod averaged ~3 lines/s on hardware), so it
        // stays on by default; this switch exists so a performance capture can rule it out entirely.
        float logNow = Time.unscaledTime;
        bool sweeping = Mathf.Abs(apex - _loggedApex) > 0.1f;
        if (!Core.PerfConfig.Quiet && logNow - _curveLogTime > (sweeping ? 0.25f : 2f))
        {
            _curveLogTime = logNow;
            _loggedApex = apex;
            float nearMm = float.PositiveInfinity, farMm = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                float mm = _depths[i] * 1000f;
                if (mm < nearMm) nearMm = mm;
                if (mm > farMm) farMm = mm;
            }
            float mid2 = (n - 1) * 0.5f;
            Core.VRLog.Info("Cards",
                $"Fan depth-curve: n={n} gazeYaw={_gazeYawDeg:F1}deg " +
                $"gazeX={_gazeX * 1000f:F0}/{_gazeEdgeX * 1000f:F0}mm " +
                $"apex={apex:F2}/{n - 1}{(Mathf.Abs(apex - mid2) < 0.05f ? " (mid)" : "")} " +
                $"bow0={BowDepth(0, n, apex) * 1000f:F1}/{RestBowDepth(0, n) * 1000f:F1}mm " +
                $"follow={CardsConfig.FanGazeApexFollow.Value:F2} " +
                $"toeIn={maxToeDeg:F1}deg face={face:F2} " +
                $"z=[{farMm:F1}..{nearMm:F1}]mm gazeBias={(CardsConfig.FanGazeBias.Value ? "ON" : "off")}");
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
    /// (Body extracted to <see cref="FanSweep.SplitOffset"/> unchanged — the pile browse arc and the
    /// item fan now open the same gap around their own highlight, so the shape, the falloff and the
    /// live FanSplit* tuning are shared instead of copied.)
    private static float SplitOffset(int signed) => FanSweep.SplitOffset(signed);

    // ------------------------------------------------------------------ reveal / hide animation --

    /// <summary>
    /// Ease-out cubic progress (0..1) of card <paramref name="i"/> in the fan-out reveal:
    /// starts after a per-card delay proportional to its slot distance from the center
    /// (FanOpenStagger — the fan ripples outward), then runs FanOpenDuration seconds.
    /// Demeo lerps all cards simultaneously (CardHandView.cs:430-431); the stagger is our
    /// small flourish and 0 restores Demeo-exact timing.
    /// </summary>
    private float OpenProgress(int i, int mid)
    {
        float dur = Mathf.Max(0.01f, CardsConfig.FanOpenDuration.Value);
        float delay = Mathf.Abs(i - mid) * Mathf.Max(0f, CardsConfig.FanOpenStagger.Value);
        float p = Mathf.Clamp01((_openElapsed - delay) / dur);
        float inv = 1f - p;
        return 1f - inv * inv * inv;
    }

    /// <summary>Total reveal-animation length for <paramref name="n"/> cards (base duration + the last card's stagger delay).</summary>
    private static float OpenAnimTotal(int n)
    {
        int mid = n / 2;
        int far = Mathf.Max(mid, n - 1 - mid);
        return Mathf.Max(0.01f, CardsConfig.FanOpenDuration.Value)
               + far * Mathf.Max(0f, CardsConfig.FanOpenStagger.Value);
    }

    /// <summary>
    /// Hide animation (reverse fan-in): blend every card from its base arc slot back into
    /// the collapsed center pose with an ease-in (accelerating shut reads snappy), then
    /// deactivate the root. Unscaled time, dt capped against hitches; allocation-free.
    /// Split/gap offsets are ignored — the collapse starts from the base fan shape.
    /// </summary>
    private void TickCollapse()
    {
        float dur = Mathf.Max(0.01f, CardsConfig.FanCloseDuration.Value);
        _closeElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float p = Mathf.Clamp01(_closeElapsed / dur);
        float e = p * p; // ease-in

        int n = _cards.Count;
        if (n > 0 && _root != null)
        {
            // Same base geometry as Relayout (no split/gap — the hover sources are inactive
            // once closed) — see Relayout for the term-by-term derivation.
            float radius = Mathf.Max(0.02f, CardsConfig.FanEffectiveRadius.Value);
            float maxArc = Mathf.Clamp(CardsConfig.FanArcSweepDegrees.Value, 5f, 180f);
            float stepCap = Mathf.Clamp(CardsConfig.FanPerCardStepDegrees.Value, 1f, 60f);
            float archFactor = CardsConfig.FanFlatCurvatureFactor.Value;
            float tiltFactor = CardsConfig.FanTiltFactor.Value;
            if (CardsConfig.FanCurveByFill.Value)
            {
                float fill = Mathf.Clamp01((float)n / Mathf.Max(1, CardsConfig.FanMaxHandForCurve.Value));
                archFactor *= fill;
                tiltFactor *= fill;
            }
            float step = n > 1 ? Mathf.Min(stepCap, maxArc / (n - 1)) : 0f;
            float start = -step * (n - 1) * 0.5f;

            int mid = n / 2;
            float midAngle = start + step * mid;
            float midRad = midAngle * Mathf.Deg2Rad;
            var collapsedRot = Quaternion.Euler(0f, 0f, -midAngle * tiltFactor);
            var collapsedXY = new Vector2(Mathf.Sin(midRad) * radius,
                                          (Mathf.Cos(midRad) - 1f) * radius * archFactor);
            // Same composed depth as the steady layout (the apex is frozen wherever the gaze left it —
            // the collapse is 120 ms and the fan no longer tracks the head, so re-aiming it would only
            // add motion to a shape that is on its way out). Toe-in is deliberately skipped: the cards
            // are folding back into the centre stack, which is a single pose by definition.
            ComposeDepths(n);

            for (int i = 0; i < n; i++)
            {
                VRCard card = _cards[i];
                if (card == null || card.IsHeld)
                    continue;
                float angle = start + step * i;
                float rad = angle * Mathf.Deg2Rad;
                var rot = Quaternion.Euler(0f, 0f, -angle * tiltFactor);
                var pos = new Vector3(Mathf.Sin(rad) * radius,
                                      (Mathf.Cos(rad) - 1f) * radius * archFactor,
                                      _depths[i]);
                var collapsed = new Vector3(collapsedXY.x, collapsedXY.y, -ZStagger * i);
                card.SetHome(_root, Vector3.Lerp(pos, collapsed, e),
                    Quaternion.Slerp(rot, collapsedRot, e), 1f, instant: true);
            }
        }

        if (p >= 1f)
        {
            _closeElapsed = -1f;
            if (_root != null)
                _root.gameObject.SetActive(false);
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

        // T2 (fan grab misses): a MODEST accept margin around each card's rect so a ray
        // that lands a few mm off the edge (trigger-pull jerk, overlapping strips) still
        // hits the card the player is visibly aiming at. Nearest-hit + the sticky rule
        // still arbitrate overlaps, so widening every card cannot flip the winner.
        const float acceptMargin = 1.10f;

        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard c = _cards[i];
            if (c == null || c.IsHeld || !c.gameObject.activeInHierarchy)
                continue;

            // ROOT-CAUSE FIX (laser sticks ABOVE a raised card): intersect the card's
            // RESTING rect, NOT its live transform. A laser-hovered card immediately pops
            // toward the viewer + up, so a plane built from the live transform moved INTO
            // the beam and latched the hover above the resting card — the board button
            // beside it went unreachable and the trigger grabbed the card by mistake. The
            // resting rect (VRCard.TryGetRestingLaserRect, world meters, pop excluded) makes
            // the pop purely visual: leaving the rect drops the hover at once.
            if (!c.TryGetRestingLaserRect(out Vector3 center, out Vector3 normal,
                    out Vector3 rectRight, out Vector3 rectUp, out float halfW, out float halfH))
                continue;
            // Cards face the viewer with -Z; a ray from the viewer travels along +Z.
            float denom = Vector3.Dot(direction, normal);
            if (denom < 1e-5f)
                continue;
            float dist = Vector3.Dot(center - origin, normal) / denom;
            if (dist <= 0f)
                continue;

            Vector3 hit = origin + direction * dist;
            Vector3 rel = hit - center; // in-plane offset from the resting card center (world meters)
            float lx = Vector3.Dot(rel, rectRight);
            float ly = Vector3.Dot(rel, rectUp);
            if (Mathf.Abs(lx) > halfW * acceptMargin || Mathf.Abs(ly) > halfH * acceptMargin)
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
