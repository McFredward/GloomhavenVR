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
        _gazeBiasYaw = 0f;  // edge-read fix: start facing the head squarely; ease into any bias
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
            float cz = -ZStagger * i + SideDepth(i, n); // match the bowed layout so the gap maps in depth too
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
                            $"edgeDepth={SideDepth(0, _cards.Count) * 1000f:F1}mm " +
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
        // steady billboard plus the depth curvature (see SideDepth / Relayout) as the primary
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
    }

    // ------------------------------------------------------------------ gaze-facing bias --

    // Gaze-responsive fan facing (edge-read fix). LOCAL tunables with sane defaults until the
    // orchestrator promotes them to live-tunable CardsConfig "Fan" entries — mirrors the
    // FanInsertGapFactor precedent above. All are pure yaw about world up (matching the fan's
    // horizontal arc); head PITCH is projected out so looking down at the fan adds no bias.

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

    // ------------------------------------------------------------------ layout --

    // Z distance between neighboring cards (meters, scale 1). Several times the
    // backing thickness so fanned cards can never interpenetrate visually — the
    // overlap is pure render order, like a real hand of cards (test #8 fix).
    private const float ZStagger = 0.004f;

    /// <summary>Throttle clock for the depth-curvature diagnostic (unscaled seconds of the last line).</summary>
    private float _curveLogTime;

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
    /// (<see cref="Relayout"/> + <see cref="SideDepth"/> + <see cref="SplitOffset"/>). Any edit to one
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
        return s;
    }

    /// <summary>
    /// Depth curvature: the SIGNED bow of card <paramref name="i"/> of a hand of <paramref name="n"/>
    /// along the fan's local forward axis (fan-local +Z is AWAY from the viewer, since the fan faces the
    /// head with -Z), so a full hand bows into depth like a real held fan. A POSITIVE
    /// <see cref="CardsConfig.FanSideDepthCurve"/> recedes the edge cards AWAY from the viewer (center
    /// nearest); a NEGATIVE value bows them the OTHER way, TOWARD the viewer (center furthest). Quadratic
    /// (<see cref="CardsConfig.FanCurvePower"/>) in the card's fraction-from-center (0 at the middle, 1 at
    /// the outermost — always ≥ 0, so the SIGN comes purely from the curve value), scaled to
    /// FanSideDepthCurve metres at the edge and ramped by hand size (flat at or below
    /// <see cref="CardsConfig.FanCurveMinCards"/>, full at <see cref="CardsConfig.FanMaxHandForCurve"/>)
    /// so a small hand stays nearly flat. Returns 0 when disabled / a tiny hand. Live-read each layout.
    ///
    /// NOTE (raycast/collider safety, either sign): this shifts ONLY each card's local Z, never its
    /// rotation. The pluck raycast (<see cref="TryRaycast"/>) builds its per-card plane from the LIVE card
    /// transform (<c>t.position</c>/<c>t.forward</c>) and its rect from <c>InverseTransformPoint</c>
    /// (Z-independent for the X/Y bounds), so the ray follows the moved card automatically — toward or
    /// away — and the hit rect is unchanged; the shrunken grab colliders
    /// (<see cref="VRCard.SetColliderRegion"/>) ride the transform likewise.
    /// </summary>
    private static float SideDepth(int i, int n)
    {
        if (n < 2)
            return 0f;
        float curve = CardsConfig.FanSideDepthCurve.Value;
        if (curve == 0f)
            return 0f; // exactly flat; negative curve bows the edges TOWARD the viewer (handled below)
        int lo = Mathf.Clamp(CardsConfig.FanCurveMinCards.Value, 1, 64);
        if (n <= lo)
            return 0f; // small hand: stay flat
        float center = (n - 1) * 0.5f;
        float frac = center > 0f ? Mathf.Abs(i - center) / center : 0f; // 0 center .. 1 outermost
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
            // (later cards nearer the viewer = -Z) and SideDepth bows the SIDES back into depth
            // (+Z, away from the viewer) so a full hand curves like a real held fan.
            var rot = Quaternion.Euler(0f, 0f, -angle * tiltFactor);
            var pos = new Vector3(Mathf.Sin(rad) * radius,
                                  (Mathf.Cos(rad) - 1f) * radius * archFactor,
                                  -ZStagger * i + SideDepth(i, n));

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

        // Throttled depth-curvature diagnostic (>=2 s apart): applied edge recession in mm, hand
        // size, and the gaze-bias enable state — so the debug menu tuning is observable in the log.
        float logNow = Time.unscaledTime;
        if (logNow - _curveLogTime > 2f)
        {
            _curveLogTime = logNow;
            float edgeMm = SideDepth(0, n) * 1000f;
            Core.VRLog.Info("Cards",
                $"Fan depth-curve: edgeDepth={edgeMm:F1}mm n={n} " +
                $"gazeBias={(CardsConfig.FanGazeBias.Value ? "ON" : "off")}");
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
                                      -ZStagger * i + SideDepth(i, n));
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
