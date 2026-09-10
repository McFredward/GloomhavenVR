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
        // An exchange cannot survive the fan being raised again — the reveal below re-seeds every
        // card at the centre stack, which is a different start pose for the same blend. Land it.
        FinishSwap();
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
        // THE FALSIFIER FOR "die Karten sind erst grau" (user 2026-08-24). The SetActive above is
        // the exact frame every adopted face becomes active in the hierarchy, i.e. the frame the
        // game's OnEnable → ShowCard would have started the card-art loads if CardArtPrewarm had
        // not already started them while the cards were parked. Measured HERE, after the reveal is
        // armed, so the number it reports is what the player is about to see: how many of these
        // faces have no art on this very frame, and how long the last one then takes. Zero is the
        // pass condition. See Cards/CardArtPrewarm.cs.
        CardArtPrewarm.NoteFanOpened(_cards);
    }

    internal void Close()
    {
        IsOpen = false;
        if (ReferenceEquals(Current, this))
            Current = null;
        ClearFingertipHover(); // test #9: drop any fingertip pop/split
        _insertGap = -1;       // hand reorder: drop any open gap so a reopen starts closed
        _openElapsed = -1f;
        // A closed fan stops ticking, so an exchange in the air would be frozen — and its cards
        // would never be handed back. Land it here; the driver's next drain collects them.
        FinishSwap();
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
        _authoredOrder.Clear(); // the order described objects that are about to be destroyed
        // The leaving cards are children of the root this is about to destroy, so there is nothing
        // left to fly or to hand back — drop the bookkeeping rather than report dead objects to the
        // driver. Its pending face restore is gated on HasLeavingCards, which now reads false.
        _swapElapsed = -1f;
        _leaving.Clear();
        _leavePos.Clear();
        _leaveRot.Clear();
        _leaveScale.Clear();
        _leaveGrab.Clear();
        _leaveIndex.Clear();
        _rescued.Clear();
        _rescuePos.Clear();
        _rescueRot.Clear();
        _rescueScale.Clear();
        _insertGap = -1;
        _openElapsed = -1f;
        _closeElapsed = -1f;
        _overlay = null; // destroyed with _root (its parent) below
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
        }
        _arriving.Clear();
        if (_arrivalSeat != null)
        {
            // Detach first: a card still riding the seat must not be destroyed WITH it. It keeps its
            // world pose at the scene root and the driver's own park sweep / factory teardown
            // disposes of it, exactly as it would have done for any other loose card.
            _arrivalSeat.DetachChildren();
            Object.DestroyImmediate(_arrivalSeat.gameObject);
            _arrivalSeat = null;
        }
        IsOpen = false;
    }

    // ------------------------------------------------- inbound flight seat (defect B) --

    /// <summary>
    /// USER REPORT 2026-08-24 (verbatim): "Die Animation, dass die Karten zurück auf die Hand bzw
    /// in den Fächer gehen spielt nur ab wenn der Fächer aktuell auch auf ist. Das soll nicht sein,
    /// auch wenn der Fächer aktuell nicht auf ist soll die Animation abspielen als wäre er auf."
    ///
    /// <para>THE MECHANISM IT FIXES. A CLOSED fan parents its cards under <see cref="_root"/> with
    /// the root <c>SetActive(false)</c> (<see cref="Close"/>, and the adopt branch of
    /// <see cref="SetCards"/>), so a card handed back to a closed hand is not
    /// <c>activeInHierarchy</c>: <c>VRCard.Update</c> never ticks and NO flight could run. That is
    /// why <c>CardsDriver.DrainPickReturnFlight</c> printed "RETURN REFUSED … it is not live in the
    /// hierarchy" for every card whenever the fan happened to be down.</para>
    ///
    /// <para>THE FIX IS THE OUTBOUND FLIGHT'S OWN SHAPE, MIRRORED. <c>FlyLockedPicksToPile</c> never
    /// re-parents anything: the card flies while it still hangs off the LIVE tray recess and only
    /// changes owner when it lands (<c>_factory.Park</c>). The inbound flight now does the same in
    /// reverse — for the duration of the flight the card hangs off THIS seat, a mod-owned transform
    /// that is always active and sits exactly where an OPEN fan's root would sit (the palm seat the
    /// eased follow in <see cref="Tick"/> drives the root to), and only when it LANDS is it re-homed
    /// into the fan. No second animation system, no fan state is faked: the fan stays closed, its
    /// root stays disabled, and the card simply is not under it while it is in the air.</para>
    ///
    /// <para>Purely local VR presentation. Nothing here writes game state and nothing rides the
    /// wire — the flight's peer announcement is the existing 2-byte semantic anchor pair
    /// (<c>Discard → HandFan</c>) the driver already reports, which carries no card identity.</para>
    /// </summary>
    private Transform? _arrivalSeat;

    /// <summary>Cards riding <see cref="_arrivalSeat"/> right now — every layout loop in this class
    /// skips them for exactly the reason it skips a HELD card: an animation owns the transform, and
    /// re-homing it under the (possibly disabled) fan root would kill the flight mid-air.</summary>
    private readonly List<VRCard> _arriving = new(4);

    /// <summary>Is this card currently flying INTO the hand on <see cref="_arrivalSeat"/>?</summary>
    internal bool IsArriving(VRCard card) =>
        _arriving.Count > 0 && card != null && _arriving.Contains(card);

    /// <summary>
    /// Seat <paramref name="card"/> for a flight into the hand and give it the home pose to land
    /// on, whatever the fan's open state is. An OPEN fan needs no seat — the card is already under
    /// a live root with a real arc seat — so this is a no-op that answers true for it; a CLOSED fan
    /// hands the card to <see cref="_arrivalSeat"/> instead. The caller launches
    /// <c>VRCard.FlyFromPile</c> immediately afterwards, which seeds the start pose at the pile, so
    /// the re-parent is never visible.
    /// </summary>
    /// <returns>False (with <paramref name="refusal"/> set) only when there is no hand seat to fly
    /// to at all — then the caller must NOT fly the card.</returns>
    internal bool TrySeatArrival(VRCard card, out string? refusal)
    {
        refusal = null;
        if (card == null)
        {
            refusal = "the VR card is gone";
            return false;
        }
        if (card.IsHeld)
        {
            refusal = "the player is holding it — the hand owns the pose";
            return false;
        }
        if (card.gameObject.activeInHierarchy)
            return true; // already live where it is (an OPEN fan IS the seat: its arc pose is home)
        if (!TryResolveArrivalPose(out Vector3 pos, out Quaternion rot, out Transform? frame)
            || frame == null)
        {
            refusal = "the hand fan has no seat to fly to (it has never been opened on a live hand " +
                      "rig this session), so there is no destination — never a wrong-spot teleport";
            return false;
        }
        if (_arrivalSeat == null)
        {
            _arrivalSeat = new GameObject("GloomhavenVR.CardFanArrival").transform;
            Core.VRLayers.Apply(_arrivalSeat.gameObject); // cards Apply themselves in VRCard.Build
        }
        if (_arrivalSeat.parent != frame)
            _arrivalSeat.SetParent(frame, worldPositionStays: false);
        if (!_arrivalSeat.gameObject.activeSelf)
            _arrivalSeat.gameObject.SetActive(true);
        _arrivalSeat.SetPositionAndRotation(pos, rot);
        if (!card.gameObject.activeSelf)
            card.gameObject.SetActive(true); // the card's own object, never the fan root
        card.SetHome(_arrivalSeat, Vector3.zero, Quaternion.identity, 1f, instant: true);
        if (!_arriving.Contains(card))
            _arriving.Add(card);
        return true;
    }

    /// <summary>
    /// Where an OPEN fan's root would be RIGHT NOW: <c>FanPalmOffset</c> real-metres up the palm
    /// normal, under the rig root — the exact seat and the exact frame the shipped (eased) follow
    /// branch of <see cref="Tick"/> drives <see cref="_root"/> to, including the reason it must not
    /// read <c>palm.lossyScale</c> (the bundle glove's 100× armature). Rotation is the fan's own
    /// base billboard (facing the head). Falls back to the last known root pose, so a fan that is
    /// merely mid-collapse still resolves.
    /// </summary>
    private bool TryResolveArrivalPose(out Vector3 pos, out Quaternion rot, out Transform? frame)
    {
        pos = default;
        rot = Quaternion.identity;
        frame = null;
        Transform? rig = VRRigDriver.RigRoot;
        Transform? palm = _hand != null ? _hand.Rig.PalmCenter : null;
        if (palm != null && rig != null && _hand != null)
        {
            pos = palm.position + palm.up * (CardsConfig.FanPalmOffset.Value * _hand.WorldScale);
            frame = rig;
        }
        else if (_root != null && _root.parent != null)
        {
            pos = _root.position;
            frame = _root.parent;
        }
        else
        {
            return false;
        }
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        Vector3 away = head != null ? pos - head.transform.position : Vector3.zero;
        rot = away.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(away.normalized, Vector3.up) // the fan's own baseFacing
            : _root != null ? _root.rotation
            : palm != null ? palm.rotation
            : Quaternion.identity;
        return true;
    }

    /// <summary>
    /// Keep the seat on the hand while cards are in the air, and hand each one back to the fan the
    /// moment its flight ends. Runs from <see cref="Tick"/> BEFORE the closed-fan early return —
    /// the whole point is that this works with the fan down.
    ///
    /// <para>MID-FLIGHT FAN STATE, both directions, stated: the player may OPEN the fan while a card
    /// is arriving (the layout loops skip it, so it keeps flying and joins the arc on the relayout
    /// that follows its landing) or CLOSE it (the seat is a separate object from the fan root, so
    /// disabling the root cannot freeze the flight; the card lands at the hand and is then re-homed
    /// collapsed under the closed root, i.e. hidden in the hand exactly like every other hand card).
    /// Neither can strand, duplicate or lose a card.</para>
    ///
    /// <para>A GRAB mid-flight also ends the arrival — <c>VRCard.Update</c> drops <c>_flying</c> the
    /// moment the card is held ("a re-grab mid-flight wins") — and the prune drops it from the list
    /// WITHOUT re-homing it: the hand owns a held card's pose and this class writes nothing a hold
    /// depends on (the standing <c>StampMembership</c> rule). The next <see cref="SetCards"/> after
    /// the release adopts it back into the fan like any other returning card. In practice a flying
    /// card is not grabbable at all (<c>FlyFromPile</c> clears <c>Grabbable</c>) and a closed fan
    /// offers no grab, so this is a belt-and-braces path.</para>
    /// </summary>
    private void TickArrivals()
    {
        bool landedWhileOpen = false;
        for (int i = _arriving.Count - 1; i >= 0; i--)
        {
            VRCard c = _arriving[i];
            if (c != null && c.IsFlying && !c.IsHeld && c.transform.parent == _arrivalSeat)
                continue;
            _arriving.RemoveAt(i);
            if (c == null || c.IsHeld || _root == null)
                continue;
            if (IsOpen)
            {
                // The next relayout gives it its arc seat with the ordinary home-lerp (no teleport).
                landedWhileOpen = true;
                continue;
            }
            if (c.transform.parent == _arrivalSeat && _cards.Contains(c))
            {
                // Closed: the card belongs collapsed under the (inactive) root — the same pose the
                // adopt branch of SetCards gives every other card of a closed hand. A card the hand
                // no longer names (the game took it back mid-flight) is deliberately LEFT on the
                // seat instead: it is not ours to re-home, and the driver's park sweep — which
                // skips a FLYING card and therefore could not have taken it earlier — collects it
                // on the next rebuild.
                c.SetHome(_root, Vector3.zero, Quaternion.identity, 1f, instant: true);
                Core.VRLog.Info("Cards", $"Hand fan ARRIVAL landed with the fan CLOSED: '{c.name}' is now " +
                                    "parked collapsed under the closed fan root — the return animation " +
                                    "ran in full and the card is in the hand, invisible until the fan " +
                                    "is raised, exactly like every other card of a closed hand.");
            }
        }
        // The seat's world pose is DELIBERATELY frozen at launch and not re-aimed at the hand each
        // frame. VRCard.FlyFromPile captures its target world point once, and its fly-IN completion
        // snaps the card to localPosition == home: a seat that chased the palm would make that snap
        // exactly as large as the hand moved during the flight — a visible pop at the very end. Held
        // still, the card lands precisely where it was aimed, and the re-home into the fan (below /
        // the next Relayout) carries whatever the hand did in the meantime, smoothly or invisibly.
        // Only the FRAME is re-asserted, world-pose-preserving, so a rig re-parent cannot drag it.
        if (_arriving.Count > 0 && _arrivalSeat != null)
        {
            Transform? rig = VRRigDriver.RigRoot;
            if (rig != null && _arrivalSeat.parent != rig)
                _arrivalSeat.SetParent(rig, worldPositionStays: true);
        }
        if (landedWhileOpen && _root != null)
            Relayout(instant: false);
    }

    // ------------------------------------------------------------------ content --

    /// <summary>
    /// What the player may DO with this fan — the whole interaction vocabulary of a hand fan, as
    /// three states rather than the one bool ("read-only") this used to be.
    /// </summary>
    internal enum FanMode
    {
        /// <summary>Full play: grab, laser-pluck, reorder, and DROP into a board slot (the real
        /// card-selection window, and the modal pick flows).</summary>
        Interactive,

        /// <summary>
        /// INSPECTION ONLY (user ruling 2026-08-08: "Ich möchte das man jederzeit auch eine Karte
        /// aus der Hand nehmen kann um sie sich genau anzuschauen, auch wenn man die Karte nirgendwo
        /// ablegen kann. Das soll also niemals blockiert sein"). Everything that only MOVES the card
        /// through the player's own hands works — laser hover/pluck, proximity grab, hand-to-hand
        /// transfer, holding it up to read it — and NOTHING that writes game state does: the
        /// release always returns the card HOME (<c>CardsDriver.OnCardReleased</c> routes on
        /// <see cref="VRCard.InspectOnly"/> before any game seam is reachable).
        /// </summary>
        Inspect,

        /// <summary>
        /// A PICTURE: not even grabbable. Reserved for a hand this client is not entitled to
        /// handle at all — after the 2026-09-07 ruling that is a hand inside the game's own secret
        /// <c>SelectAbilityCardsOrLongRest</c> window belonging to a character this client does not
        /// control (a FOCUSED foreign hand is <see cref="Inspect"/>, not this: see
        /// <c>Board.CharacterFocus.HandInspectable</c>). This is the old read-only behaviour,
        /// unchanged, and it is now the exception rather than the rule.
        ///
        /// <para>THIS MODE HIDES NOTHING, AND THAT MATTERS BECAUSE THE SOURCE HAS SAID OTHERWISE.
        /// <see cref="StampMode"/> sets <c>Grabbable=false; InspectOnly=false</c> and nothing else;
        /// <c>CardsDriver.FillHandFan</c> adopts every hand widget's live <c>FullAbilityCard</c>
        /// with no reveal-gate term at all, and the local pipeline has no back mesh to fall back on
        /// (<c>Cards.Art.CardFace.Adopt</c> re-hosts the game's own rect, so a local card is FRONT
        /// or NOT DRAWN, never BACK). A comment elsewhere in the driver calls this mode "inert end
        /// to end" on the premise that vanilla would not draw a remote actor's fronts anyway. That
        /// premise is false, read at source (2026-09-07 review R1, correction C1): vanilla's four
        /// selection-phase guards — <c>AbilityCardUI.cs:980, 1024, 1100, 1188</c> — only force
        /// <c>fullAbilityCard.DisplaySelected(false)</c> and swap a <c>CardPileType.Round</c> mini
        /// card to <c>unselectedCardType</c>. Vanilla's secret is WHICH TWO CARDS ARE SELECTED, not
        /// the hand; <c>CardsHandManager.ShowTabs</c> (<c>CardsHandManager.cs:718-726</c>) activates
        /// the character tabs precisely WHEN <c>PhaseType == SelectAbilityCardsOrLongRest</c>, and
        /// <c>CardsHandTabs.UpdateTabsInteraction</c> (<c>CardsHandTabs.cs:132-139</c>) makes every
        /// non-dead tab clickable with NO ownership test, so in the flat game you can tab to a
        /// teammate's hand during the selection phase and read it.</para>
        ///
        /// <para>WHAT ACTUALLY KEEPS A FOREIGN HAND OFF THIS BOARD IS THREE INPUT GUARDS, AND THEY
        /// ARE LOAD-BEARING — deleting any one of them puts a peer's unrevealed hand on this
        /// client's own board, in the one window the game exists to keep secret. Named here so a
        /// reader who trusts "inert end to end" cannot remove one by accident:
        /// <list type="number">
        ///   <item><c>Board.CharacterFocus.Refusal</c> (<c>Board/CharacterFocus.cs:263-279</c>) —
        ///         refuses a focus while <c>RevealGate.IsSecretSelectionPhase</c>, so
        ///         <c>PresentedHandCore</c> falls back to the game's own hand.</item>
        ///   <item><c>InitiativeTrackPlayerAvatar_OnClick_Guard</c>'s foreign-portrait branch
        ///         (<c>Board/Patches/SelectionGuardPatches.cs:145-150</c>) — refuses the whole
        ///         portrait click via <c>CardsGameApi.IsForeignControlledSelect</c>.</item>
        ///   <item><c>Choreographer_TileHandler_OwnershipGuard</c>
        ///         (<c>Board/Patches/SelectionGuardPatches.cs:365-395</c>) — blocks the tile-click
        ///         <c>SwitchHand</c> in <c>WaitingForCardSelection</c>.</item>
        /// </list>
        /// Plus <c>Cards.Patches.HandSuppression</c>, which holds the vanilla 2D hand window at
        /// <c>blocksRaycasts = false</c>, so the character tabs above are unreachable in VR at all.
        /// The picture is therefore SAFE TODAY and no reachable scenario was found — but it is safe
        /// because those four inputs are closed, not because this mode hides a face.</para>
        /// </summary>
        Picture,
    }

    /// <summary>
    /// The fan's current interaction mode. Default <see cref="FanMode.Interactive"/> — a fan that
    /// has not been told anything behaves exactly as it did before this concept existed.
    ///
    /// <para>WHY THE FAN ITSELF CARRIES THE FLAG rather than trusting the driver to hand it inert
    /// cards: the fan owns the ONE laser path into a hand card (<see cref="TryRaycast"/> — the
    /// laser driver asks the fan, it does not raycast colliders), and it owns
    /// <see cref="Remove"/>, the seam a grab uses to pull a card out. Deciding both HERE means the
    /// verdict survives every route a card can take into this list, including the between-rebuild
    /// seams (<see cref="Add"/> on a released card) where the driver's per-card stamp has not run
    /// yet.</para>
    /// </summary>
    internal FanMode Mode { get; private set; } = FanMode.Interactive;

    /// <summary>Set the interaction mode. Called by the driver BEFORE <see cref="SetCards"/> so the
    /// first frame of a focus / locked view is already correct.</summary>
    internal void SetMode(FanMode mode)
    {
        if (Mode == mode)
            return;
        Mode = mode;
        // A fan that just became a PICTURE must not keep a live hover/highlight from the mode it
        // replaced — the laser can no longer clear it, because it can no longer see it.
        if (mode == FanMode.Picture)
            ClearFingertipHover();
        // Re-stamp the cards already in the list so a mode learned one frame late is never one
        // frame of the wrong affordance.
        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard c = _cards[i];
            if (c == null)
                continue;
            StampMode(c);
            if (mode == FanMode.Picture)
                c.SetLaserHover(false);
        }
    }

    /// <summary>Apply <see cref="Mode"/> to one card. The single place fan membership turns into an
    /// interaction verdict, used by <see cref="SetMode"/>, <see cref="SetCards"/> and
    /// <see cref="Add"/> alike so the three cannot drift. <c>PokeSelectEnabled</c> is forced OFF in
    /// every mode — touching a hand card must never commit it (item 10), inspection included.</summary>
    private void StampMode(VRCard card)
    {
        card.PokeSelectEnabled = false;
        switch (Mode)
        {
            case FanMode.Picture:
                card.Grabbable = false;
                card.InspectOnly = false;
                break;
            case FanMode.Inspect:
                card.Grabbable = true;
                card.InspectOnly = true;
                break;
            default: // Interactive — the driver's zone stamp owns Grabbable here (a fan card may
                     // still be non-grabbable for a reason that has nothing to do with the fan).
                card.InspectOnly = false;
                break;
        }
    }

    /// <summary>
    /// Turn fan MEMBERSHIP into interaction verdicts for one card — the gate-hand veto plus
    /// <see cref="StampMode"/>. The single place <see cref="SetCards"/> and <see cref="Add"/>
    /// share, so the two membership seams cannot drift.
    ///
    /// <para>A HELD CARD IS NOT A FAN CARD (hand-to-hand transfer bug, user report 2026-08-08:
    /// "Auch das Wechseln der Hand in dem die Karte gehalten wird soll immer möglich sein —
    /// aktuell ist es nicht möglich wenn man sich eine Karte eines Characters anguckt der nicht
    /// am Zug ist"). ROOT CAUSE: the game's widget list still names a card the player is holding,
    /// so every Rebuild put it back into <see cref="SetCards"/>'s incoming list, and this stamp
    /// then wrote <c>AllowsGateHand = false</c> onto a card sitting in the player's hand. The
    /// hand-to-hand transfer adopts the card into the OTHER hand, and that hand is normally the
    /// GATE hand (the fan hangs off it) — so <c>VRCard.AllowsHand</c> refused the adoption and
    /// <c>CardsDriver.TransferHeldCard</c> aborted, every time, for as long as rebuilds kept
    /// coming. That is exactly a watched, non-acting character: its turn churn (actor change /
    /// action signature, <c>CardsDriver.PollModeChange</c>) marks the driver dirty over and over,
    /// so in a focus view the re-stamp was effectively per-frame and the transfer NEVER worked,
    /// while in a quiet locked phase (no rebuild during the hold) the very same gesture DID work —
    /// the hardware log shows both outcomes minutes apart. The same stale FALSE also makes
    /// <c>ProximityGrabber.HealDeadHeld</c> force-drop a card held IN the gate hand mid-hold.</para>
    ///
    /// <para>THE RULE: while a card is HELD, this seam writes nothing that a hold depends on.
    /// <see cref="VRCard.AllowsGateHand"/> stays TRUE (stamped at the one point where any card
    /// becomes held, <c>CardsDriver.OnCardGrabbed</c>) and <see cref="VRCard.Grabbable"/> is left
    /// alone — mirroring the driver's Rebuild zone loop, which skips held cards outright, and this
    /// class' own layout/raycast/fingertip scans, which all skip <c>IsHeld</c> already.</para>
    ///
    /// <para>THE ONE EXCEPTION IS THE FAN'S OWN VERDICT, AND IT NOW RUNS IN BOTH DIRECTIONS
    /// (2026-09-06 item 6a). The paragraph that stood here said the exception was "a TIGHTENING,
    /// never a widening... Clearing it is deliberately NOT done: per <see cref="VRCard.InspectOnly"/>
    /// the verdict the player saw when they grabbed the card is the verdict that decides their
    /// release." The user's report falsifies the second half:</para>
    /// <para><em>"Wenn ich eine Karte in der Hand hab bevor ich drücke, dass ich den Schaden mit
    /// Verbrennen negieren möchte (Entscheidungsbutton), dann zappt die Karte nicht an die Stelle
    /// und ich kann sie im Overlay nicht ablegen. Ich muss sie erst wieder ablegen und dann nochmal
    /// dorthin zappen, dann funktioniert alles."</em></para>
    /// <para>THE MECHANISM, and it is this one flag twice. Outside a pick the fan is
    /// <see cref="FanMode.Inspect"/>, so a card grabbed to be read carries
    /// <c>InspectOnly = true</c>. Pressing the burn decision opens the pick and
    /// <c>CardsDriver.Rebuild</c> sets the fan <see cref="FanMode.Interactive"/> — but
    /// <see cref="SetMode"/>'s re-stamp loop only walks <c>_cards</c>, and a grab has already
    /// REMOVED the held card from that list, while the <see cref="SetCards"/> that puts it back
    /// lands HERE and used to tighten only. So the flag survived the whole flow, and it costs the
    /// player the placement TWICE over: <c>CardsDriver.IsReadOnlyViewerCard</c> tests it, so the
    /// slot never even GLOWS for the card ("zappt die Karte nicht an die Stelle"), and
    /// <c>CardsDriver.OnCardReleased</c>'s InspectOnly arm then returns it home without a
    /// SelectCard ("kann sie im Overlay nicht ablegen"). Dropping and re-grabbing works because the
    /// re-grab re-stamps the flag from the fan's CURRENT mode — which is exactly what this seam
    /// should have been doing all along: the flow arms its target only while nothing is held, an
    /// arming edge the already-held case never crosses.</para>
    /// <para>WIDENING IT IS SAFE, and the release path is where that is proved rather than
    /// asserted: <c>OnCardReleased</c> still refuses a browse-loaned card (the PileOrigin arm), a
    /// card the presented hand does not own (the <c>HandOwnsWidget</c> belt), a placement the board
    /// is not offering (<c>PlacementIsOffered</c>), a pile the pick may not draw from
    /// (<c>PickPileIsLegalFor</c>) and a pick that is not live at all
    /// (<c>CardsGameApi.PickFlowLive</c>). What this restores is only the verdict a re-grab would
    /// have given the same card in the same frame. The tightening half is unchanged: a fan that
    /// stops being Interactive still stamps the flag onto a held card.</para>
    /// </summary>
    private void StampMembership(VRCard card)
    {
        if (card.IsHeld)
        {
            card.InspectOnly = Mode != FanMode.Interactive;
            return;
        }
        // Gate-hand veto seam (general rule 2026-08-04, see VRCard.AllowsGateHand): a card
        // becomes a FAN card the moment it enters this list, and only fan cards refuse the
        // fan-owning hand. Stamped here — not only in the driver's Rebuild loop — so a card
        // handed to the fan between rebuilds can never spend frames grabbable by the very
        // hand the fan hangs off. The ONE interaction fact this layout class writes, because
        // fan membership is decided exactly here.
        card.AllowsGateHand = false;
        // MODE: re-assert the fan's verdict at the same seam that decides fan membership,
        // so a card that entered the fan between rebuilds never spends a frame with the
        // wrong affordance (inert in Picture, inspect-only in Inspect).
        StampMode(card);
    }

    /// <summary>Replace the fan's card set (called on rebuilds; cards fly to their arc slots).</summary>
    internal void SetCards(List<VRCard> cards) => SetCards(cards, swap: false);

    /// <summary>
    /// Replace the fan's card set. <paramref name="swap"/> = "this is not a card being added or
    /// removed, the WHOLE HAND was exchanged for another character's" — see the exchange region
    /// below for what that turns into and why it is a separate animation from the open reveal.
    /// The caller (<c>CardsDriver.Rebuild</c>) owns that verdict because only it knows which
    /// character the board is presenting; the fan only knows its own list changed, and a list
    /// change alone can never tell a swap from a draw.
    /// </summary>
    internal void SetCards(List<VRCard> cards, bool swap)
    {
        // The outgoing wave was captured EARLIER, by the driver's BeginSwapOut at the top of the
        // rebuild — it has to be, because the rebuild's park sweep runs before this call and would
        // otherwise have teleported the whole outgoing hand into the pool (see BeginSwapOut). All
        // that is left for a SWAP here is the half that needs the INCOMING list: which of the cards
        // on their way out are named again and must turn around instead.
        //
        // A NON-SWAP SET DOES NOT CANCEL A RUNNING EXCHANGE, and that is not a nicety: while the
        // player is WATCHING a character (which is exactly when they switch between them) the
        // driver is marked dirty by that character's own turn churn over and over, so Rebuild — and
        // therefore this method — runs at very nearly every frame. Landing the exchange on the first
        // such call would have made the whole animation one frame long in the only situation it
        // exists for. The incoming list on those frames is the SAME character's hand, so the running
        // blend is still aimed at the right target and simply carries on.
        bool exchange = _swapElapsed >= 0f && _root != null && IsOpen;
        if (exchange && swap)
            ArmSwapArrival(cards);
        else if (_swapElapsed >= 0f && !IsOpen)
            FinishSwap(); // a closed fan stops ticking — nothing may be left mid-flight

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
        {
            // A card cannot be a fan card and an outgoing card at once. The swap path has already
            // done this through ArmSwapArrival; repeating it here is a no-op then and closes the
            // case where a card of an exchange still in the air is named by a NON-swap set.
            if (cards[i] != null)
                RescueFromLeaving(cards[i]);
            // MB497: a grab removes one card from the physical arc. A model rebuild must not
            // put it back while it is still held: that made the count flap N/N-1 and reshuffled
            // the covered remote fan. The authored order below still remembers its return seat.
            if (cards[i] != null && !cards[i].IsHeld)
                _cards.Add(cards[i]);
            // Membership → interaction verdicts (gate-hand veto + mode), and the held-card rule
            // that keeps a card the player is HOLDING transferable between the hands: see
            // StampMembership. NOTE the incoming list legitimately still names a held card — the
            // game's widget list does not know about our grabs — which is exactly why that rule
            // lives there and not in the caller.
            if (cards[i] != null)
                StampMembership(cards[i]);
        }
        // Remember the order we were TOLD, so a card that later comes back through Add lands where
        // its publisher put it instead of on the right-hand end (see _authoredOrder). Snapshotted
        // rather than aliased: the caller reuses its buffer every rebuild.
        _authoredOrder.Clear();
        _authoredOrder.AddRange(cards);
        if (IsOpen)
        {
            // instant during an exchange: the swap-in blend below drives every incoming card's
            // pose itself, exactly like the open reveal does, so VRCard's own home-lerp must not
            // double-smooth it (Relayout forces `instant || opening || swapping` per card anyway;
            // this only keeps the first frame consistent with the ones the Tick drives).
            Relayout(instant: exchange);
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
                // …EXCEPT a card that is flying INTO the hand right now (defect B): adopting it
                // under the DISABLED root is precisely what killed the return animation, and
                // TickArrivals re-homes it here itself the moment it lands.
                if (c != null && !c.IsHeld && !IsArriving(c) && c.transform.parent != _root)
                    c.SetHome(_root, Vector3.zero, Quaternion.identity, 1f, instant: true);
            }
        }
    }

    /// <summary>Remove a card (grabbed away); remaining cards close the gap. A
    /// <see cref="FanMode.Picture"/> fan refuses: nothing may be pulled out of a character's hand
    /// the player is not entitled to handle. <see cref="FanMode.Inspect"/> ALLOWS it — pulling a
    /// card out to read it is the whole point of that mode, and the gap it leaves is what makes the
    /// remaining fan legible while the card is up at the player's face.</summary>
    internal void Remove(VRCard card)
    {
        if (Mode == FanMode.Picture)
            return;
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

    /// <summary>
    /// The order the fan was last TOLD to be in — a snapshot of the incoming list of the most
    /// recent <see cref="SetCards"/>, and the fan's only memory of where a card belongs.
    ///
    /// <para>WHY IT EXISTS (user report 2026-08-22, the second face of "added cards appear at the
    /// right edge"): <see cref="Add"/> is the RETURN-HOME seam — a card the player lifted out to
    /// read and let go of again, an inspect release, a refused drop — and it used to APPEND. In a
    /// scenario that was invisible, because <c>CardsDriver.Rebuild</c> runs at very nearly every
    /// frame and re-publishes the whole list a moment later. In the MAP ROOM there is no such
    /// heartbeat: its source publishes only when the loadout signature moves
    /// (<c>MapRoomHand.Reconcile</c>), so a card taken out for a look and put back stayed at the
    /// right edge until the player next ticked a card in the party screen. Same visible defect,
    /// different route into it.</para>
    ///
    /// <para>THIS IS NOT A SORT, and deliberately so: the fan is a layout, it holds no card
    /// identity and it must never decide an order of its own. It restores the order it was GIVEN —
    /// by the game's own initiative-sorted widget list in a scenario (through
    /// <c>CardsDriver.FillHandFan</c>, plus the player's own drag-reorder in
    /// <c>ReorderFanBuffer</c>, which this must not undo either) and by the initiative-sorted
    /// loadout in the map room. Whatever the publisher meant, a returning card goes back into it.</para>
    /// </summary>
    private readonly List<VRCard> _authoredOrder = new(16);

    /// <summary>
    /// Where <paramref name="card"/> belongs in the CURRENT list, according to
    /// <see cref="_authoredOrder"/>: the number of cards presently in the fan that the published
    /// order puts BEFORE it. A card the last publish never named (or a fan that has never been
    /// published to) answers <c>_cards.Count</c> — the append this method replaces, which stays the
    /// honest answer when there is no order to restore. Allocation-free; n is a hand.
    /// </summary>
    private int HomeIndexFor(VRCard card)
    {
        int slot = _authoredOrder.IndexOf(card);
        if (slot < 0)
            return _cards.Count;
        int before = 0;
        for (int i = 0; i < slot; i++)
        {
            VRCard c = _authoredOrder[i];
            if (c != null && _cards.Contains(c))
                before++;
        }
        return before < _cards.Count ? before : _cards.Count;
    }

    /// <summary>Return a card to the fan (release outside a drop zone) — animated, and back into
    /// its OWN place rather than onto the right-hand end (see <see cref="_authoredOrder"/>). The
    /// cards on either side glide apart to open the gap, because <see cref="Relayout"/> below runs
    /// with <c>instant: false</c> exactly as it always has — only the index changed.</summary>
    internal void Add(VRCard card)
    {
        // A card cannot be a fan card and an outgoing card at once — if this one is mid-exit, it
        // turns around from where it is (RescueFromLeaving states the invariant).
        RescueFromLeaving(card);
        if (!_cards.Contains(card))
            _cards.Insert(HomeIndexFor(card), card);
        // Fan entry = gate-hand veto (general rule 2026-08-04, see SetCards / VRCard.AllowsGateHand):
        // a void-released card re-enters the fan HERE, often frames before the next Rebuild
        // re-stamps zones — without this the fan-owning hand could hover/grab its own fan card.
        // MODE: the RETURN-HOME seam of an inspection release lands here, frames before the next
        // Rebuild re-stamps zones — re-assert the fan's verdict so the card is immediately
        // re-grabbable for another look (Inspect) or immediately inert (Picture).
        // Shared with SetCards through StampMembership, which also carries the held-card rule (a
        // released card is never held, so this call is the plain stamp it always was).
        StampMembership(card);
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
    /// slides half of this). ~one card so a full card visibly fits. Deliberately a local constant
    /// and not a config dial — no [Cards] key has ever been bound for it.</summary>
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
    internal int InsertionGap => IsOpen ? _insertGap : -1;

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
    private static float GapOffset(int i, int gap) =>
        InsertionOffset(i, gap, CardsConfig.CardWidth.Value, CardsConfig.FanSplitFalloff.Value);

    internal static float InsertionOffset(int i, int gap, float cardWidth, float splitFalloff)
    {
        int d;
        float side;
        if (i < gap) { d = gap - i; side = -1f; }
        else { d = i - gap + 1; side = 1f; }
        float falloff = Mathf.Max(0.0001f, splitFalloff);
        float x = (d - 1) / falloff;
        float half = cardWidth * FanInsertGapFactor * 0.5f;
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
        // Defect B: cards flying INTO the hand are ticked FIRST and unconditionally — the whole
        // point of the inbound-flight seat is that it works while this fan is closed (and therefore
        // while everything below this line is skipped). Cheap no-op when nothing is arriving.
        if (_arriving.Count > 0)
            TickArrivals();

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

        // CHARACTER EXCHANGE (see the exchange region): advance the wipe and drive the OUTGOING
        // half; the incoming half rides the Relayout below, which therefore has to run every frame
        // for as long as the exchange does. Mutually exclusive with the reveal by construction —
        // BeginSwapOut drops _openElapsed — so the two blends can never both write a card's home.
        bool swapping = _swapElapsed >= 0f;
        if (swapping)
        {
            TickSwap();
            if (_cards.Count > 0)
                Relayout(instant: true);
        }

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
        if (wasOpening || swapping)
        {
            // …and through the exchange, for the same reason: it relayouts every frame already.
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

        // The reveal — and the character exchange — own the layout while they run (both relayout
        // every frame with the live apex, so a second gated relayout here would only duplicate it).
        if (_openElapsed >= 0f || _swapElapsed >= 0f)
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

        // Character EXCHANGE, arriving half (see the exchange region). The TARGET of every incoming
        // card is its real arc home, which is what this method computes — so the blend lives here
        // rather than in a second layout path that could drift from it. Mutually exclusive with the
        // reveal (BeginSwapOut drops _openElapsed), so at most one of the two blends ever applies.
        bool swapping = _swapElapsed >= 0f;
        float swapArc = swapping ? Mathf.Max(0f, CardsConfig.FanSwapArc.Value) : 0f;
        float swapBack = swapping ? SwapOvershoot : 0f;

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
            // IsArriving: a card flying INTO the hand owns its own transform for the flight (the
            // held-card rule, applied to an animation) — see the inbound-flight-seat region. It
            // joins the arc on the relayout that follows its landing.
            if (card == null || card.IsHeld || IsArriving(card))
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

            // Exchange, arriving half: fly in from the deal point off the arc's LOW end (or, for a
            // card that turned around mid-exit, from wherever it actually is), bowing TOWARD the
            // viewer at mid-flight — the opposite of the leaving half's duck, so the two hands
            // cross in depth — and settling with the back-ease overshoot.
            float scale = 1f;
            if (swapping)
            {
                float t = InProgress(i);
                float e = EaseOutBack(t, swapBack);
                SwapEnterSeed(card, out Vector3 seedPos, out Quaternion seedRot, out float seedScale);
                pos = Vector3.LerpUnclamped(seedPos, pos, e);
                pos.z -= swapArc * Mathf.Sin(t * Mathf.PI);
                rot = Quaternion.Slerp(seedRot, rot, Mathf.Clamp01(e));
                scale = Mathf.LerpUnclamped(seedScale, 1f, e);
            }

            card.SetHome(_root, pos, rot, scale, instant || opening || swapping);

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
            PositionInsertionOverlay(_overlay!.transform, _insertGap, start, step, radius, archFactor, tiltFactor);
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
        _overlay = CreateInsertionOverlay(_root, CardsConfig.CardWidth.Value, CardsConfig.CardHeight);
    }

    internal static GameObject CreateInsertionOverlay(Transform parent, float width, float height)
    {
        GameObject overlay = CardGlow.CreateGlowQuad("FanInsertHighlight", parent,
            new Vector3(width * 1.24f, height * 1.24f, 1f), Vector3.zero,
            new Color(1f, 0.85f, 0.3f, 0.95f));
        Core.VRLayers.Apply(overlay);
        CardGlow.RankWithPanels(overlay);
        return overlay;
    }

    internal static void PositionInsertionOverlay(Transform overlay, int gap, float start,
        float step, float radius, float arch, float tilt)
    {
        float angle = start + step * (gap - 0.5f);
        float rad = angle * Mathf.Deg2Rad;
        overlay.localRotation = Quaternion.Euler(0f, 0f, -angle * tilt);
        overlay.localPosition = new Vector3(Mathf.Sin(rad) * radius,
            (Mathf.Cos(rad) - 1f) * radius * arch, -ZStagger * gap + OverlayProudZ);
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
                // …and an ARRIVING card is not part of the collapse either: closing the fan while a
                // card is flying home must not yank it under the root that is about to be disabled.
                if (card == null || card.IsHeld || IsArriving(card))
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

    // ------------------------------------------------------- character-swap EXCHANGE --
    //
    // USER REPORT 2026-08-09: "Wenn man die Handkarten anschaut während man den Character wechselt
    // gefällt mir die jetzige Animation nicht - mach auch hier eine neue coolere Tauschanimation
    // rein die den Fächer austauscht."
    //
    // WHAT IT USED TO DO. Nothing at all. A focus switch is a plain Rebuild: the driver filled its
    // buffer with the OTHER character's cards and called SetCards, which cleared _cards, re-laid
    // the new set out, and left the old cards to the driver's park sweep — i.e. teleported into the
    // pool in the same frame. The player, who is by definition staring straight at the fan when
    // they do this, saw a CONTENT EDIT: n cards blinked away, m cards blinked in, and any card that
    // happened to survive in both frames merely slid to a new slot. That is the standing ruling
    // ("everything that moves must move WITH an animation; popping is unacceptable") broken at
    // exactly the moment it is most visible.
    //
    // ────────────────────────────────────────────────────────────────────────────────────────────
    // THE SHAPE, AND WHY THIS SHAPE. The vocabulary is the one ModBuild 92 established for the ITEM
    // fan (Cards/ItemsPile.cs — centre-out stagger, arc toward the viewer, seed scale, signed spin,
    // settle overshoot), because a second idiom for the same job would read as a different mod. But
    // it is deliberately NOT that animation replayed:
    //
    //  • NOT CENTRE-OUT. The open/close ripple runs outward from the fan's middle, because a hand of
    //    cards being RAISED unfolds from its middle. An exchange is not a raise. Sequencing it the
    //    same way would make a swap look like "the fan closed and opened again", which is precisely
    //    the two-unrelated-animations failure the brief names. This one is sequenced ALONG THE ARC,
    //    by card index, so it is a WIPE and never a fold.
    //
    //  • THE TWO HALVES TRAVEL THE SAME WAY, AND SO DO THEIR FRONTS. The outgoing hand converges on
    //    a gather point one FanSwapTravel past the arc's HIGH-index end; the incoming hand fans out
    //    of the mirror-image point past the LOW-index end. Both waves are delayed by
    //    index × FanSwapStagger, so both moving fronts run low-index → high-index. What the eye
    //    tracks is therefore ONE front crossing the palm, with the old hand ahead of it and the new
    //    hand behind it. The alternative — gathering right-to-left and then dealing left-to-right —
    //    is two fronts in opposite directions, which is exactly how "glued together" looks.
    //
    //  • THE OVERLAP IS THE EXCHANGE. FanSwapOverlap starts each slot's ARRIVAL while that slot's
    //    DEPARTURE is still in the air (the delay between them is (1 − overlap) × FanSwapDuration,
    //    and the stagger term is shared, so the two waves stay in lockstep at every slot rather
    //    than drifting apart along the hand). At the shipped 0.66 the hands visibly cross. At 0 the
    //    same wipe still runs, one card at a time — that is the "refill an emptied hand" reading,
    //    and it is the honest bottom of the range, not the default.
    //
    //  • THEY SEPARATE IN DEPTH INSTEAD OF COLLIDING. A leaver ducks AWAY from the viewer by
    //    FanSwapArc at mid-flight; an arriver bows TOWARD them by the same amount. Two crossing
    //    hands at the same depth would interpenetrate and z-fight; at opposite depths the new hand
    //    unambiguously passes IN FRONT of the old one, which is what an exchange looks like — and
    //    depth is the one cue a passthrough background cannot mask, because it is stereo.
    //
    //  • THE ROLLS ARE OPPOSITELY SIGNED. The gather winds one way, the deal unwinds the other
    //    (FanSwapSpinDegrees, signed by which end of the arc the card is heading for). A rotation
    //    changes a card's OUTLINE, and a cluttered room never supplies a coherent outline rotation
    //    by accident; the opposite signs are what stop the whole thing reading as one shove.
    //
    //  • THE ARRIVAL OVERSHOOTS, THE DEPARTURE WINDS UP. EaseOutBack / EaseInBack on
    //    FanSwapSettleOvershoot, the same pair and the same argument as ItemsPile: a reversal of
    //    direction is the loudest event motion has and costs no extra travel.
    //
    // ────────────────────────────────────────────────────────────────────────────────────────────
    // THE THREE THINGS THAT MUST NOT BREAK, and how each is closed:
    //
    //  1. THE FAN STAYS USABLE. Nothing here gates input for a single frame. The incoming cards are
    //     in _cards from the first frame of the exchange, so TryRaycast, SetHovered and the
    //     fingertip scan see them exactly as they always do — and because those all read the card's
    //     HOME pose (which the blend writes every frame), the laser tracks a card THROUGH its
    //     flight instead of pointing at where it will end up. A grab mid-exchange simply wins: every
    //     loop in this class already skips IsHeld, so the grabbed card drops out of the wave and the
    //     rest carries on. That is also why a swap can never "block" — worst case the player pulls a
    //     card out of a hand that is still arriving, which is the same gesture as any other pluck.
    //
    //  2. SWITCHING AGAIN MID-SWAP. This WILL happen (the initiative portraits are a row and people
    //     scrub along it), so it is the case the state machine is built around rather than guarded
    //     against. A second exchange does not restart anything: every card still on its way out
    //     keeps its place in the wave and is RE-SEEDED AT ITS CURRENT POSE (never at where it
    //     started), so the clock restarting cannot teleport it; the cards the fan holds now join the
    //     same outgoing list behind them. And a card named again by the INCOMING list — the
    //     A → B → A scrub, the one way a card can be in both halves at once — is pulled out of the
    //     outgoing list into _rescued and turns around from where it is, mid-air. It is therefore
    //     impossible for one card to be animated by both halves, and impossible for one to be left
    //     behind: the ONLY exits from _leaving are "landed, handed to the driver" and "rescued".
    //
    //  3. POOLED CARDS ARE NOT DESTROYED UNDER SOMEBODY. The fan does not own card lifetime and does
    //     not try to: it hands each leaver back through TryTakeLandedOutgoing THE MOMENT that card's
    //     own flight ends, and the driver parks it / restores its adopted face then (CardsDriver's
    //     deferred focus release). A HELD card never enters the outgoing list at all — the capture
    //     loop skips IsHeld, exactly like every other loop here, so a card the player is holding
    //     when they switch character stays in their hand and cannot vanish. Close(), Open() and
    //     Destroy() all force the exchange to land, so a card can never be stranded mid-flight by a
    //     fan that stopped ticking.

    /// <summary>Cards on their way OUT (the hand being replaced), parallel to the four seed lists
    /// below. A card is in exactly one of <c>_cards</c> and this list, never both.</summary>
    private readonly List<VRCard> _leaving = new(12);
    private readonly List<Vector3> _leavePos = new(12);
    private readonly List<Quaternion> _leaveRot = new(12);
    private readonly List<float> _leaveScale = new(12);

    /// <summary>Each leaver's place IN THE WAVE, which is NOT its place in the list: cards are
    /// handed back as they land, and the earliest ones land first, so using the list position for
    /// the stagger delay would shorten every remaining card's delay by one place each time one was
    /// removed — and the tail of the wipe would snap to the gather point in a single frame. The
    /// index is assigned once and travels with the card.</summary>
    private readonly List<int> _leaveIndex = new(12);

    /// <summary>The <see cref="VRCard.Grabbable"/> a leaver had when it was captured. A leaving card
    /// makes no promises (same rule as <c>VRCard.Vanish</c>), so the capture drops the flag — and a
    /// card that turns around mid-exit gets its exact previous verdict back rather than waiting for
    /// the next rebuild's zone stamp to notice.</summary>
    private readonly List<bool> _leaveGrab = new(12);

    /// <summary>Cards that were leaving and are named again by the incoming hand (the A→B→A scrub):
    /// they fly IN from where they actually are instead of from the deal point. Parallel lists,
    /// normally empty — the lookup below only runs when it is not.</summary>
    private readonly List<VRCard> _rescued = new(2);
    private readonly List<Vector3> _rescuePos = new(2);
    private readonly List<Quaternion> _rescueRot = new(2);
    private readonly List<float> _rescueScale = new(2);

    /// <summary>Seconds since the exchange began (-1 = none). UNSCALED, like every other animation
    /// here: the game pauses simulation time during card phases and the swap must still run.</summary>
    private float _swapElapsed = -1f;

    /// <summary>How many cards the OUTGOING wave started with — it fixes the wipe's rhythm and the
    /// gather point's place on the arc even as cards land and leave <see cref="_leaving"/>.</summary>
    private int _swapOutCount;

    /// <summary>True while at least one card is still flying OUT — the driver's gate for handing a
    /// focused character's adopted card faces back to the game.</summary>
    internal bool HasLeavingCards => _leaving.Count > 0;

    /// <summary>Is this card currently flying out of the fan? The driver's park sweep must skip it
    /// for the same reason it skips <c>IsFlying</c>/<c>IsVanishing</c> cards: the animation owns the
    /// transform until it lands.</summary>
    internal bool IsLeaving(VRCard card) => _leaving.Count > 0 && _leaving.Contains(card);

    /// <summary>
    /// Hand back every outgoing card whose flight has ENDED (appended to <paramref name="into"/>,
    /// which is NOT cleared). Called once per frame by the driver, which then parks each one and —
    /// once the whole exchange has drained — restores the outgoing character's adopted faces.
    /// Per-card rather than per-exchange so a card is disposed of the instant it is invisible,
    /// instead of sitting shrunk at the gather point until the last one arrives.
    /// </summary>
    internal void TryTakeLandedOutgoing(List<VRCard> into)
    {
        for (int i = _leaving.Count - 1; i >= 0; i--)
        {
            VRCard c = _leaving[i];
            // A destroyed card (an external release we did not drive) drops out silently — it is
            // already gone, and reporting it would only make the driver park a dead object.
            if (c != null)
            {
                if (_swapElapsed >= 0f && OutProgress(_leaveIndex[i]) < 1f)
                    continue;
                into.Add(c);
            }
            RemoveLeavingAt(i);
        }
    }

    /// <summary>Drop entry <paramref name="i"/> from the six parallel outgoing lists at once — the
    /// single place they are mutated together, so they cannot drift.</summary>
    private void RemoveLeavingAt(int i)
    {
        _leaving.RemoveAt(i);
        _leavePos.RemoveAt(i);
        _leaveRot.RemoveAt(i);
        _leaveScale.RemoveAt(i);
        _leaveGrab.RemoveAt(i);
        _leaveIndex.RemoveAt(i);
    }

    /// <summary>
    /// Arm the exchange's OUTGOING half: everything the fan currently shows becomes the wave that
    /// gathers off the arc.
    ///
    /// <para>WHY THIS IS A SEPARATE, PUBLIC STEP RATHER THAN PART OF <see cref="SetCards"/>. The
    /// driver's rebuild does its per-card PARK SWEEP (every VR card that ends this rebuild in no
    /// zone is teleported into the pool) BEFORE it hands the fan its new list. On a swap frame the
    /// outgoing hand is in no zone by definition, so by the time SetCards ran the whole wave would
    /// already have been parked — the exact pop the exchange exists to remove, and invisible in a
    /// code read because the two steps live 200 lines apart. Capturing here, at the top of the
    /// rebuild, is what makes <see cref="IsLeaving"/> answer TRUE while that sweep runs.</para>
    /// </summary>
    internal void BeginSwapOut()
    {
        if (_root == null || !IsOpen)
            return;

        // A reveal that is still running is superseded: the two blends write the same home pose, and
        // "the fan is still unfolding" is no longer true the moment its content is being exchanged.
        _openElapsed = -1f;
        _rescued.Clear();
        _rescuePos.Clear();
        _rescueRot.Clear();
        _rescueScale.Clear();

        // (1) cards ALREADY leaving (a second switch landed mid-wipe) keep their place in the wave,
        //     re-seeded where they ARE — never where they started, or the restarting clock would
        //     teleport them back. Their Grabbable was dropped when they were first captured, so the
        //     recorded value is kept: re-reading it here would only record the false we wrote.
        for (int i = 0; i < _leaving.Count; i++)
        {
            VRCard c = _leaving[i];
            if (c == null)
                continue;
            CaptureLocal(c, out Vector3 p, out Quaternion r, out float s);
            _leavePos[i] = p;
            _leaveRot[i] = r;
            _leaveScale[i] = s;
        }

        // (2) the hand the fan is showing joins them, in arc order, behind the stragglers.
        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard c = _cards[i];
            // A HELD card is not exchanged. The player is holding it; it stays in their hand and is
            // not ours to fly away or hand back (the standing rule at StampMembership: while a card
            // IsHeld this class writes nothing a hold depends on).
            // An ARRIVING card is not exchanged either, for the same reason: its inbound flight owns
            // the transform, and joining the outgoing wave would re-home it mid-air.
            if (c == null || c.IsHeld || IsArriving(c) || _leaving.Contains(c))
                continue;
            CaptureLocal(c, out Vector3 p, out Quaternion r, out float s);
            _leaving.Add(c);
            _leavePos.Add(p);
            _leaveRot.Add(r);
            _leaveScale.Add(s);
            _leaveGrab.Add(c.Grabbable);
            _leaveIndex.Add(0); // re-assigned in wave order below
            c.Grabbable = false; // a card on its way out makes no promises (VRCard.Vanish's rule)
        }

        ReindexLeaving();
        // AND THE FAN NO LONGER HOLDS THEM. Without this the outgoing hand would sit in BOTH lists
        // for the ~570 lines of rebuild between here and SetCards, which is not merely untidy — the
        // driver calls SetMode in that window, and StampMode writes Grabbable back to TRUE on a
        // Picture→Inspect transition (scrubbing from a teammate to one of your own mercs), undoing
        // the "makes no promises" flag two lines above and letting the player grab a card that is
        // already flying away. Clearing here makes the one-list-only invariant hold at every
        // instant instead of at most instants; SetCards replaces the list unconditionally a moment
        // later, and a HELD card is dropped from it exactly as it was before this feature existed.
        _cards.Clear();
        _swapElapsed = 0f;
    }

    /// <summary>How many cards are still on their way out — the driver's own count for the
    /// exchange, read AFTER <see cref="BeginSwapOut"/> has emptied <see cref="Count"/>.</summary>
    internal int LeavingCount => _leaving.Count;

    /// <summary>
    /// The card count the exchange's two end points are derived from: the LARGER of the two hands.
    /// Both the gather and the deal point must be built from the SAME span or they are not mirror
    /// images — a 4-card hand leaving and a 9-card hand arriving would otherwise put them at
    /// different |x| and different y, which is exactly what the region header and the config
    /// description promise they are not. Taking the larger span also guarantees both points sit a
    /// full <c>FanSwapTravel</c> clear of BOTH arcs rather than inside the wider one.
    /// </summary>
    private int SwapArcSpan() => Mathf.Max(1, Mathf.Max(_swapOutCount, _cards.Count));

    /// <summary>
    /// THE ONE WAY A CARD LEAVES THE OUTGOING WAVE OTHER THAN BY LANDING: it turns around. If
    /// <paramref name="card"/> is flying out, pull it out of that wave and record where it ACTUALLY
    /// IS (not where the wave started it), so the arriving half seeds from there and the turnaround
    /// is a change of direction rather than a teleport back to the start line. Its pre-exit
    /// <see cref="VRCard.Grabbable"/> comes back with it, so it is immediately as usable as it was.
    ///
    /// <para>This is what makes "a card is in exactly one of <c>_cards</c> and <c>_leaving</c>"
    /// STRUCTURAL rather than a timing argument: every route by which a card can (re-)enter the
    /// fan's own list — <see cref="ArmSwapArrival"/> on a swap, <see cref="Add"/> on a release —
    /// goes through here first, so it is impossible for one card to end up driven by both halves of
    /// an exchange at once, or handed to the driver for disposal while it is also on screen.</para>
    /// </summary>
    private void RescueFromLeaving(VRCard card)
    {
        if (_leaving.Count == 0 || _root == null)
            return;
        int i = _leaving.IndexOf(card);
        if (i < 0)
            return;
        CaptureLocal(card, out Vector3 p, out Quaternion r, out float s);
        _rescued.Add(card);
        _rescuePos.Add(p);
        _rescueRot.Add(r);
        _rescueScale.Add(s);
        card.Grabbable = _leaveGrab[i]; // exactly the verdict it had before it started leaving
        RemoveLeavingAt(i);
    }

    /// <summary>Re-number the wave and record its size. Run whenever the outgoing list is (re)built,
    /// so the stagger runs 0, 1, 2 … along the list however many generations it was assembled from.</summary>
    private void ReindexLeaving()
    {
        for (int i = 0; i < _leaving.Count; i++)
            _leaveIndex[i] = i;
        _swapOutCount = _leaving.Count;
    }

    /// <summary>
    /// The half of the arming that needs the INCOMING list: any card on its way out that the new
    /// hand names again turns around from where it is, mid-air, instead of leaving. That is the
    /// A → B → A scrub — the one way a single card can belong to both halves of an exchange — and
    /// resolving it HERE, by moving the card out of the outgoing list, is what makes "no card is
    /// ever driven by both halves" true by construction rather than by timing.
    /// </summary>
    private void ArmSwapArrival(List<VRCard> incoming)
    {
        for (int i = 0; i < incoming.Count; i++)
        {
            VRCard c = incoming[i];
            if (c != null)
                RescueFromLeaving(c);
        }
        ReindexLeaving();
        Core.VRLog.Info("Cards",
            $"Fan EXCHANGE: {_swapOutCount} card(s) gather off the arc's high end while " +
            $"{incoming.Count} deal out of the low end ({_rescued.Count} turned around mid-exit), " +
            $"one wipe of {SwapTotalSeconds(_swapOutCount, incoming.Count):F2}s at " +
            $"{CardsConfig.FanSwapDuration.Value:F2}s/card + {CardsConfig.FanSwapStagger.Value:F3}s " +
            $"stagger, overlap {CardsConfig.FanSwapOverlap.Value:F2}. The two halves pass in depth " +
            "(±FanSwapArc) and counter-roll; held cards are untouched.");
    }

    /// <summary>This card's pose IN THE FAN ROOT's frame, read off its live transform rather than
    /// its recorded home — the capture must be where the card actually IS, or a re-seed mid-flight
    /// (or a card mid-pop) would jump.</summary>
    private void CaptureLocal(VRCard card, out Vector3 pos, out Quaternion rot, out float scale)
    {
        Transform root = _root!;
        Transform t = card.transform;
        pos = root.InverseTransformPoint(t.position);
        rot = Quaternion.Inverse(root.rotation) * t.rotation;
        float rootScale = root.lossyScale.x;
        scale = rootScale > 1e-5f ? t.lossyScale.x / rootScale : 1f;
    }

    // ---- timing. One duration and one stagger drive BOTH halves; the only asymmetry is the
    // arrival's head start, which is what the overlap dial means.

    private static float SwapDuration => Mathf.Max(0.02f, CardsConfig.FanSwapDuration.Value);
    private static float SwapStagger => Mathf.Max(0f, CardsConfig.FanSwapStagger.Value);
    private static float SwapOvershoot => Mathf.Clamp(CardsConfig.FanSwapSettleOvershoot.Value, 0f, 3f);

    /// <summary>How long after a slot's card starts LEAVING its replacement starts ARRIVING.
    /// Expressed against the per-card duration (not against the whole wave) on purpose: the stagger
    /// term is shared by both halves, so this keeps the two waves in lockstep at EVERY slot instead
    /// of letting them drift apart along the hand.</summary>
    private static float SwapArriveDelay =>
        (1f - Mathf.Clamp01(CardsConfig.FanSwapOverlap.Value)) * SwapDuration;

    /// <summary>Progress (0..1) of outgoing card <paramref name="i"/>.</summary>
    private float OutProgress(int i) =>
        Mathf.Clamp01((_swapElapsed - i * SwapStagger) / SwapDuration);

    /// <summary>Progress (0..1) of incoming card <paramref name="j"/>.</summary>
    private float InProgress(int j) =>
        Mathf.Clamp01((_swapElapsed - SwapArriveDelay - j * SwapStagger) / SwapDuration);

    /// <summary>Total length of the whole exchange for the two hand sizes (the driver uses it as
    /// the hard deadline on its deferred face restore, so it must be an upper bound).</summary>
    internal static float SwapTotalSeconds(int outCount, int inCount) =>
        SwapArriveDelay + SwapDuration
        + Mathf.Max(0, Mathf.Max(outCount, inCount) - 1) * SwapStagger;

    /// <summary>Ease-out BACK — overshoots 1 near the end and settles onto it. Verbatim the curve
    /// <c>ItemsPile.ItemChip</c> uses, so the two fans settle with the same character; s = 0
    /// degenerates to a plain ease-out, which is what FanSwapSettleOvershoot = 0 restores.</summary>
    private static float EaseOutBack(float t, float s)
    {
        float u = t - 1f;
        return 1f + u * u * ((s + 1f) * u + s);
    }

    /// <summary>Ease-in BACK — dips slightly the WRONG way first (the leaver winds up into the fan
    /// before it goes) and then accelerates out. The mirror of <see cref="EaseOutBack"/>.</summary>
    private static float EaseInBack(float t, float s) => t * t * ((s + 1f) * t - s);

    /// <summary>
    /// The point a hand is GATHERED INTO (<paramref name="side"/> = +1, off the arc's high-index
    /// end) or DEALT OUT OF (<paramref name="side"/> = −1, off its low-index end), in fan-local
    /// metres, plus the pose a card holds there.
    ///
    /// <para>It is the arc's own outermost slot pushed one <c>FanSwapTravel</c> further along, so
    /// the two points are mirror images and sit on the line the fan already describes — a hand
    /// swept off the end of itself, not a card thrown at an arbitrary offset. The roll is that end
    /// slot's roll plus the SIGNED <c>FanSwapSpinDegrees</c>, which is what makes the gather and the
    /// deal counter-rotate.</para>
    /// </summary>
    private static void SwapGatherPoint(int n, float side, out Vector3 pos, out Quaternion rot)
    {
        float radius = Mathf.Max(0.02f, CardsConfig.FanEffectiveRadius.Value);
        float maxArc = Mathf.Clamp(CardsConfig.FanArcSweepDegrees.Value, 5f, 180f);
        float stepCap = Mathf.Clamp(CardsConfig.FanPerCardStepDegrees.Value, 1f, 60f);
        float tiltFactor = CardsConfig.FanTiltFactor.Value;
        float archFactor = CardsConfig.FanFlatCurvatureFactor.Value;
        if (CardsConfig.FanCurveByFill.Value)
        {
            float fill = Mathf.Clamp01((float)Mathf.Max(n, 1) / Mathf.Max(1, CardsConfig.FanMaxHandForCurve.Value));
            archFactor *= fill;
            tiltFactor *= fill;
        }
        float step = n > 1 ? Mathf.Min(stepCap, maxArc / (n - 1)) : 0f;
        float endAngle = step * (n - 1) * 0.5f;              // the +X end's arc angle
        float rad = endAngle * Mathf.Deg2Rad;
        float travel = Mathf.Max(0f, CardsConfig.FanSwapTravel.Value);
        pos = new Vector3(side * (Mathf.Sin(rad) * radius + travel),
                          (Mathf.Cos(rad) - 1f) * radius * archFactor,
                          0f);
        rot = Quaternion.Euler(0f, 0f,
            -side * endAngle * tiltFactor + side * CardsConfig.FanSwapSpinDegrees.Value);
    }

    /// <summary>
    /// Advance the exchange and drive the OUTGOING half. The incoming half is not here: it rides
    /// <see cref="Relayout"/>, because its target is each card's real arc home and re-deriving that
    /// in a second place is exactly how two layout paths drift apart.
    /// Allocation-free; unscaled time with the same hitch cap as the reveal.
    /// </summary>
    private void TickSwap()
    {
        if (_swapElapsed < 0f || _root == null)
            return;
        _swapElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        if (_leaving.Count > 0)
        {
            SwapGatherPoint(SwapArcSpan(), 1f, out Vector3 gather, out Quaternion gatherRot);
            float seed = Mathf.Clamp(CardsConfig.FanSwapSeedScale.Value, 0.02f, 1f);
            float arc = Mathf.Max(0f, CardsConfig.FanSwapArc.Value);
            float s = SwapOvershoot;
            for (int i = 0; i < _leaving.Count; i++)
            {
                VRCard c = _leaving[i];
                // A grab mid-exit wins outright: the hand owns the pose from here, and the card
                // stays in the list so the driver still hands it back when the wave drains.
                if (c == null || c.IsHeld)
                    continue;
                float t = OutProgress(_leaveIndex[i]); // wave place, not list place — see _leaveIndex
                float e = EaseInBack(t, s);
                Vector3 p = Vector3.LerpUnclamped(_leavePos[i], gather, e);
                // Duck AWAY from the viewer (fan-local +Z) at mid-flight, peaking at t = 0.5 and
                // exactly 0 at both ends — the arriving half bows the other way, so the two hands
                // cross in depth instead of through each other.
                p.z += arc * Mathf.Sin(t * Mathf.PI);
                // The ROLL eases on the CLAMPED progress: a card that overshoots its roll reads as
                // a wobble, and it is the one axis where the reversal does not help (ItemsPile).
                Quaternion r = Quaternion.Slerp(_leaveRot[i], gatherRot, Mathf.Clamp01(e));
                float sc = Mathf.LerpUnclamped(_leaveScale[i], seed, e);
                c.SetHome(_root, p, r, sc, instant: true);
            }
        }

        if (_swapElapsed >= SwapTotalSeconds(_swapOutCount, _cards.Count))
            _swapElapsed = -1f; // the wave is spent; TryTakeLandedOutgoing has already drained it
    }

    /// <summary>
    /// End the exchange NOW, landing everything at its finished pose. Called on every path by which
    /// the per-frame tick could stop running: the fan CLOSES, it re-OPENS (the reveal owns the
    /// layout from there), or it is handed a card set while closed. Deliberately NOT called by an
    /// ordinary open-fan <see cref="SetCards"/> — a watched character's own turn churn re-runs the
    /// driver's rebuild at nearly every frame, so that would have cut the wipe to one frame in the
    /// only situation it exists for. Nothing is left mid-air and nothing is left in
    /// <see cref="_leaving"/> unreported: the cards stay there for the driver's next drain, which
    /// now sees them at progress 1 (<see cref="_swapElapsed"/> is negative).
    /// </summary>
    private void FinishSwap()
    {
        if (_swapElapsed < 0f && _leaving.Count == 0)
            return;
        if (_root != null && _leaving.Count > 0)
        {
            SwapGatherPoint(SwapArcSpan(), 1f, out Vector3 gather, out Quaternion gatherRot);
            float seed = Mathf.Clamp(CardsConfig.FanSwapSeedScale.Value, 0.02f, 1f);
            for (int i = 0; i < _leaving.Count; i++)
            {
                VRCard c = _leaving[i];
                if (c != null && !c.IsHeld)
                    c.SetHome(_root, gather, gatherRot, seed, instant: true);
            }
        }
        _swapElapsed = -1f;
        _rescued.Clear();
        _rescuePos.Clear();
        _rescueRot.Clear();
        _rescueScale.Clear();
    }

    /// <summary>
    /// Where incoming <paramref name="card"/> flies IN FROM: the deal point off the arc's
    /// low-index end, or — for a card that was on its way OUT and got named again — exactly where
    /// it is right now, so a mid-air turnaround is continuous rather than a teleport back to the
    /// start line. The rescued lookup is a linear scan of a list that is empty in every ordinary
    /// swap, and is skipped entirely when it is.
    /// </summary>
    private void SwapEnterSeed(VRCard card, out Vector3 pos, out Quaternion rot, out float scale)
    {
        if (_rescued.Count > 0)
        {
            int r = _rescued.IndexOf(card);
            if (r >= 0)
            {
                pos = _rescuePos[r];
                rot = _rescueRot[r];
                scale = _rescueScale[r];
                return;
            }
        }
        SwapGatherPoint(SwapArcSpan(), -1f, out pos, out rot);
        scale = Mathf.Clamp(CardsConfig.FanSwapSeedScale.Value, 0.02f, 1f);
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

        // PICTURE: the fan is the ONLY laser path into a hand card (the laser driver asks the
        // fan rather than raycasting colliders), so refusing here removes the whole route — the
        // laser reports no hit and falls through to whatever is behind the fan, exactly as it does
        // when the fan is closed. FanMode.Inspect does NOT refuse: the laser pluck is one of the
        // two ways a player picks a card up to read it (the other being the proximity grab), and
        // inspection must never be blocked. What the pluck may not do — commit the card — is
        // refused at the RELEASE instead (VRCard.InspectOnly), not by hiding the card from the ray.
        if (!IsOpen || Mode == FanMode.Picture || _root == null)
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
