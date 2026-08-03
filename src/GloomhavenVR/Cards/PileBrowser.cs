using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// The pile browse fan (hardware test #21): a readable arc of one pile's cards,
/// raised by poking or pinch-grabbing a <see cref="PileViewer"/> stack. Simplified
/// <see cref="CardFan"/>-style arc, cards slightly enlarged. The POKE-TOGGLE fan now
/// floats at a fixed spot ABOVE the control board (<see cref="PlaceAboveBoard"/>): its
/// root is parented under the board root (<see cref="PlayTray.Current"/>.<see cref="PlayTray.Root"/>)
/// so it INHERITS the board's live scale and pose — the fan tracks a two-hand board
/// resize and a board switch. The pinch-GRAB fan stays a reading fan pinned to the
/// grabbing hand (item 5). Either way the arc BILLBOARDS to face the head every frame in
/// <see cref="Tick"/> so the cards always face the player (ISSUE #7) — only the ANCHOR
/// position + scale come from the board, not the head. Purely informational: the cards
/// are adopted read-only (never grabbable, never poke-selectable — CardsDriver clears
/// both flags), and closing simply lets the next rebuild park them again. Layout only —
/// open/close policy and content live in <see cref="CardsDriver"/>. No allocations after open.
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

    /// <summary>
    /// Poke-toggle float height: how far the fan's ROOT pivot sits ABOVE the board's top
    /// edge, in board-LOCAL meters (the fan hangs DOWNWARD from this pivot, so the body
    /// floats a comfortable read above the board face and clears the initiative track,
    /// which tops out ≈ 0.08 m past the edge). Tunable — raise to lift the whole fan. The
    /// pivot lives in board-local space, so it scales with the board automatically.
    /// </summary>
    private const float BoardFloatHeight = 0.26f;

    /// <summary>Poke-toggle proud offset toward the viewer (board-local −Z is out of the board face), meters.</summary>
    private const float BoardFloatProudZ = -0.05f;

    // ---- physical hand sweep (single-winner highlight) ---------------------------
    // The browse arc runs the SAME election as the palm fan, literally: FanSweep.Score. Its
    // reach constants used to be MIRRORED here as scale-1 metres, which was the bug — a browse
    // card sits under the CONTROL BOARD and therefore carries the board's scale on top of the
    // rig's, so a real-metre reach meant something different on every player's board. The reach
    // now comes from FanSweep.ResolveReach, derived from the card's own live world width. See
    // FanSweep for the full root-cause note.

    /// <summary>
    /// The poke-toggle fan's FIXED board-local base anchor (above the board top edge, slightly
    /// proud toward the viewer). The live anchor is this base plus the debug-menu-tunable
    /// <see cref="CardsConfig.BrowseFanOffset"/>, re-read every <see cref="Tick"/> while
    /// board-anchored so the Piles 'Browse X/Y/Z' steppers move an OPEN fan immediately.
    /// </summary>
    private static Vector3 BoardAnchorBase =>
        new(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);

    private readonly List<VRCard> _cards = new(16);
    private Transform? _root;
    private TextMeshPro? _title;
    private VRHand? _followHand;
    private bool _boardAnchored; // poke-toggle fan parented under the board root (not held, not head-fallback)

    // Hand-sweep state: the single browse card the physical hand is currently lifting (null =
    // none) and the set of cards this tick pop-suppressed so nothing else can lift with it.
    private VRCard? _handWinner;

    /// <summary>Which hand elected <see cref="_handWinner"/> (null = none). Read by
    /// <see cref="HandOwnedCard"/> so the trigger goes to the hand that is actually holding the
    /// card up — with BOTH hands sweeping, "the sweeping hand" is no longer a constant.</summary>
    private VRHand? _handWinnerHand;

    private readonly List<VRCard> _handSuppressed = new(16);
    private float _nextHandLogAt; // throttle clock (unscaled s) for the winner-change log
    private float _nextHandMissLogAt; // throttle clock for the "nothing won, here is why" line

    /// <summary>
    /// Arc index of the current hand-sweep winner, or -1 — the browse fan's counterpart of
    /// <c>CardFan._pokeHoveredIndex</c>. Drives the SPLIT (see <see cref="Relayout"/>): the
    /// highlighted card is the pivot and its neighbours step aside, which is the visible half of
    /// "exactly one card at a time" the user asked the pile fans to copy from the hand cards.
    /// </summary>
    private int _handWinnerIndex = -1;

    // Requirement 5 (emerge from the pile): the world position of the pile STACK this browse
    // opened over; the first content layout pre-seats every card AT this point so the existing
    // per-card home-lerp flies them OUT of the stack into the arc (a subtle emerge, no pop-in).
    // Consumed on the first non-empty Relayout, then cleared.
    private Vector3 _emergeWorld;
    private bool _emergePending;

    internal bool IsOpen { get; private set; }

    /// <summary>The pile currently browsed (null while closed).</summary>
    internal PileKind? Kind { get; private set; }

    /// <summary>Held-fan mode (grabbed a pile): the arc follows the grabbing hand.</summary>
    internal bool IsHandHeld => _followHand != null;

    /// <summary>Which hand a HELD reading fan is pinned to — the receiver cannot guess it (either
    /// hand may pinch-grab a stack) so it rides the wire. Mirrors <see cref="ItemsPile.IsHeldByLeftHand"/>.</summary>
    internal bool IsHeldByLeftHand => _followHand != null && _followHand.Side == HandSide.Left;

    /// <summary>
    /// The open BOARD-ANCHORED browse fan's board-local anchor position (the root is a child of
    /// the board root, so <c>localPosition</c> IS the board frame), or null while closed /
    /// hand-held / head-fallback. Multiplayer read seam for the fan-anchor wire record — carries
    /// the owner's live <c>[Cards] BrowseFanOffset</c> tuning that a receiver cannot derive.
    /// Mirrors <see cref="ItemsPile.BoardLocalAnchor"/>.
    /// </summary>
    internal Vector3? BoardLocalAnchor =>
        IsOpen && _boardAnchored && _root != null ? _root.localPosition : null;

    /// <summary>
    /// The OPEN pile browser, or null while every pile fan is closed. Exists for exactly one
    /// reason: the multiplayer extras sender (<c>Net.NetAvatarDriver.TickExtrasSend</c>) has to
    /// answer "does this player have a pile fan up, which pile, how many cards" once per extras
    /// packet, and the browser instance itself is a private field of <c>CardsDriver</c>. Publishing
    /// a static here is the same seam <see cref="ItemsPile.Current"/> and <see cref="CardFan.Current"/>
    /// already use, and it keeps the Net layer from reaching into the Cards driver's privates.
    /// Purely a read seam — nothing mutates the browser through it.
    /// </summary>
    internal static PileBrowser? Current { get; private set; }

    internal bool Contains(VRCard card) => _cards.Contains(card);

    /// <summary>Requirement 2 (collapse-on-close): the cards currently in the arc, so CardsDriver can
    /// fly each one back down into its pile stack before the driver reparks it. Read-only snapshot use
    /// only — the driver never mutates this list (Close/SetCards own it).</summary>
    internal IReadOnlyList<VRCard> Cards => _cards;

    /// <summary>
    /// Index of the browse card the player is currently SINGLING OUT (lifted + enlarged by the
    /// hand sweep's elected winner or by the laser hover), or -1 when none is. Read off
    /// <see cref="VRCard.IsHighlighted"/> — the predicate the pop animation itself obeys — so it
    /// covers both sources with one test and can never disagree with what is on screen.
    ///
    /// Exists for the MULTIPLAYER mirror, exactly like <c>CardFan.HighlightedIndex</c>: the Net
    /// layer broadcasts a bare fan INDEX (a position, never a card identity) so a peer's copy of
    /// this arc lifts the same card. Exactly one card can be lifted at a time — the sweep elects a
    /// single winner and pop-suppresses the rest — so the first match is the answer.
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

    /// <summary>
    /// Open (or switch) the browser for one pile. With <paramref name="followHand"/>
    /// the arc is a HELD reading fan pinned to that hand (test #22 item 5, grabbed a
    /// pile); without it the arc floats at a fixed spot ABOVE the control board
    /// (<see cref="PlaceAboveBoard"/>), inheriting the board's live scale/pose, and falls
    /// back to a head-relative pose only if no board exists. Either way the cards are
    /// readable, individually grabbable and laser-hoverable — the driver owns those flags
    /// and the open/close policy. <paramref name="anchorParent"/> is the head-relative
    /// fallback parent (the hands root) when no control board is present.
    /// </summary>
    internal void Open(PileKind kind, Transform anchorParent, VRHand? followHand = null,
        Vector3? emergeFromWorld = null)
    {
        // Requirement 5: remember the pile-stack world anchor so the first content layout emerges
        // the cards out of the stack (cleared once consumed). A held fan (followHand) still emerges
        // from the pile the same way — the cards fly from the stack up to the reading fan.
        _emergePending = emergeFromWorld.HasValue;
        _emergeWorld = emergeFromWorld ?? default;
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
            WorldUI.MrBacking.Label(_title); // browser title floats over the room in MR
        }
        _followHand = followHand;
        // Poke-toggle: anchor under the board root so the fan inherits the board's live
        // scale + pose (tracks a resize + a board switch). No board → head-relative fallback.
        Transform? boardRoot = followHand == null ? PlayTray.Current?.Root : null;
        _boardAnchored = boardRoot != null;
        Transform parent = followHand != null ? followHand.Rig.PalmCenter
                         : boardRoot != null ? boardRoot
                         : anchorParent;
        if (_root.parent != parent)
            _root.SetParent(parent, worldPositionStays: false);
        _root.gameObject.SetActive(true);
        Kind = kind;
        IsOpen = true;
        Current = this; // publish to the multiplayer extras sender (see Current's doc comment)
        if (followHand != null)
            Tick(); // place immediately near the holding hand
        else if (boardRoot != null)
            PlaceAboveBoard(); // float above the board, inherit its scale (tracks resize/switch)
        else
            PlaceAtHead(); // no board — head-relative fallback
        Relayout(instant: false);
    }

    /// <summary>Close the browser. Cards are NOT touched — the driver's rebuild parks them.</summary>
    internal void Close()
    {
        IsOpen = false;
        Kind = null;
        if (ReferenceEquals(Current, this))
            Current = null; // unpublish: the extras sender stops advertising the fan this frame
        _followHand = null;
        _boardAnchored = false;
        ClearHandSweep(); // drop any hand-sweep lift + suppression before the cards are released
        _cards.Clear();
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    internal void Destroy()
    {
        ClearHandSweep();
        _cards.Clear();
        IsOpen = false;
        Kind = null;
        if (ReferenceEquals(Current, this))
            Current = null;
        _followHand = null;
        _boardAnchored = false;
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
    /// Poke-toggle reading pose (primary): a spot a comfortable reading height ABOVE the
    /// control board, centered on its long axis — <see cref="BoardAnchorBase"/> plus the
    /// debug-menu-tunable <see cref="CardsConfig.BrowseFanOffset"/>. The root is a child of
    /// the board root (set up in <see cref="Open"/>), so this board-LOCAL offset INHERITS the
    /// board's live scale + pose — the fan tracks a two-hand board resize and a board switch.
    /// POSITION is set here and re-read every <see cref="Tick"/> (live tuning); the FACING is
    /// re-billboarded toward the head every frame in <see cref="Tick"/> (ISSUE #7). The
    /// downward-hanging arc clears the board top edge and the initiative track (see
    /// <see cref="BoardFloatHeight"/>).
    /// </summary>
    private void PlaceAboveBoard()
    {
        if (_root == null)
            return;
        _root.localPosition = BoardAnchorBase + CardsConfig.BrowseFanOffset.Value;
        _root.localRotation = Quaternion.identity; // Tick billboards the WORLD rotation each frame
        Tick(); // face the head immediately (no first-frame flash of the un-billboarded arc)
    }

    /// <summary>
    /// Head-relative reading pose (FALLBACK — used only when no control board exists): in front
    /// of the head at ~tray distance, raised toward eye height, tilted slightly back — the proven
    /// HalfSelection floating pose (HalfSelection.PlaceAtHead) shifted up for a card WALL instead
    /// of a pair. POSITION is placed once per open (deliberately no per-frame follow); the FACING
    /// is re-billboarded toward the head every frame in <see cref="Tick"/> (ISSUE #7).
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
    /// poke-toggled fan that is board-anchored (<see cref="PlaceAboveBoard"/>): its local
    /// position stays put under the board (so it rides the board's pose + scale), only its
    /// facing tracks the head, so the cards face the player even BEFORE one is plucked into
    /// the hand. No-op while closed.
    /// </summary>
    internal void Tick()
    {
        if (!IsOpen || _root == null)
            return;
        // Physical hand sweep (user issue): move the free hand THROUGH the arc to highlight the
        // card nearest the fingertip, exactly one at a time — same feel as the palm fan.
        UpdateHandSweep();
        // Held mode: the pivot floats above the palm along the palm normal (+Y of PalmCenter).
        // Board-anchored mode: re-read the base + [Cards] BrowseFanOffset every frame — a cheap
        // Vector3 config read — so the debug menu's Piles 'Browse X/Y/Z' steppers move an OPEN
        // fan live; the board-LOCAL anchor still rides its parent's pose/scale for free.
        if (_followHand != null)
            _root.localPosition = new Vector3(0f, HandPalmOffset, 0f);
        else if (_boardAnchored)
            _root.localPosition = BoardAnchorBase + CardsConfig.BrowseFanOffset.Value;
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

        // PER-PILE SPREAD, read LIVE so a debug stepper widens the OPEN fan (user request
        // 2026-08-03). The shipped defaults are the old constants, so nothing moves until dialled.
        PileKind kind = Kind ?? PileKind.Discard;
        float radius = CardsConfig.FanRadius.Value * CardsConfig.FanRadiusFactor(kind).Value;
        float step = n > 1
            ? Mathf.Min(CardsConfig.FanStepDegrees(kind).Value, MaxArcDegrees / (n - 1))
            : 0f;
        float start = -step * (n - 1) * 0.5f;

        // HAND-FAN PARITY, part 1 (the collider strip). Each card's grab collider shrinks to the
        // chord between neighbouring card centres, anchored at its exposed LEFT edge, exactly as
        // CardFan.Relayout does — so the per-card regions TILE the arc instead of overlapping by
        // half a card. Without this the fingertip measured 0.0 cm to two or three browse cards at
        // once, ties never displaced the incumbent, and the sweep skipped every 2nd/3rd card
        // (the user's report). The chord is fan-local; the cards are enlarged by CardScale, so it
        // is divided back into card-local metres.
        float fullWidth = CardsConfig.CardWidth.Value;
        float strip = FanSweep.StripWidth(n, radius, step, fullWidth, CardScale);
        // HAND-FAN PARITY, part 2 (the split): the highlighted card is the pivot and stays put;
        // its neighbours slide aside so the winner is unmistakable. Offsets are authored for the
        // hand fan's card scale, so they ride CardScale into this arc's larger cards.
        int hovered = _handWinnerIndex >= 0 && _handWinnerIndex < n ? _handWinnerIndex : -1;

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
            if (hovered >= 0 && i != hovered)
                pos += rot * new Vector3(FanSweep.SplitOffset(i - hovered) * CardScale, 0f, 0f);
            card.SetHome(_root, pos, rot, CardScale, instant);
            // The last card in the arc is fully exposed (nothing draws over it) and so is the
            // hovered pivot, whose neighbours have just stepped aside — both keep the full width.
            if (i == n - 1 || i == hovered)
                card.SetColliderRegion(fullWidth, 0f);
            else
                card.SetColliderRegion(strip, FanSweep.StripOffset(fullWidth, strip));
            // Requirement 5 (emerge): on the first content layout after open, drop the card ONTO the
            // pile stack world point; SetHome left _instantNext=false, so the per-card home-lerp then
            // flies it up into the arc — the cards visibly emerge FROM the stack.
            if (_emergePending && !instant)
                card.transform.position = _emergeWorld;
        }
        _emergePending = false; // one-shot: only the first layout after open emerges
    }

    // ------------------------------------------------------------------ hand sweep --

    /// <summary>
    /// Physical HAND sweep over the browse arc — the browse counterpart of
    /// <see cref="CardFan.UpdateFingertipHover"/> + <c>CardsDriver.UpdateHandContactArbitration</c>.
    /// Reads EITHER hand's index fingertip + palm DIRECTLY from the rig every
    /// frame and elects a SINGLE winner among the browse cards through the SHARED election
    /// <see cref="FanSweep.Score{T}"/> — the same code the ability hand fan runs, with the reach
    /// resolved from each card's own live world size (<see cref="FanSweep.ResolveReach"/>) so the
    /// board's scale can no longer change the feel. The winner lifts via
    /// <see cref="VRCard.SetFingertipHover"/> (the same pop the laser gives) and EVERY other browse
    /// card is <see cref="VRCard.SetHandPopSuppressed">pop-suppressed</see> for the WINNING hand,
    /// so a hand moving through the arc can never raise more than one card — exactly like the fan.
    ///
    /// BOTH HANDS (user report 2026-08-03: "Mit der linken Hand highlighted die Karte nicht mal.
    /// Ich will, dass die Karten … mit BEIDEN Händen aufgenommen werden können und bei beiden
    /// Händen auch reagieren"). This loop used to read <c>VRHands.Primary</c> and nothing else — a
    /// hard dominant-hand filter, so the off hand swept the arc in complete silence however close
    /// it got, and the 2026-08-03 hardware log accordingly carries no single <c>(Left)</c> browse
    /// line. It is the very same defect the ITEM fan had and fixed (see
    /// <see cref="ItemsPile.UpdateHandSweep"/>); the browse arc is now the same shape: each hand
    /// runs its own election, the better of the two wins, and <see cref="_handWinnerHand"/> records
    /// WHICH hand so the trigger can be handed to it (<see cref="HandOwnedCard"/>) instead of being
    /// guessed. Exactly one card is lifted across both hands, so the multiplayer highlight index
    /// (<see cref="HighlightedIndex"/>) still has exactly one answer.
    ///
    /// LASER vs HAND: purely spatial, so the two never fight. When the hand is physically IN the
    /// arc a fingertip/palm candidate exists → the hand drives (and, being deep in the arc, the
    /// aim ray no longer lands a clean browse hit, so <c>CardsDriver.UpdateBrowseLaser</c> clears its
    /// own hover that same frame). When the hand is OUT at pointing distance nothing is within
    /// reach → no winner, this method suppresses/lifts nothing, and the existing laser hover
    /// (resolved in CardsDriver before this Tick) is the sole highlight. Read-only throughout:
    /// this only raises/enlarges for readability — it never grabs or plays a browse card.
    /// Allocation-free. No-op while closed (Tick guards <see cref="IsOpen"/>).
    /// </summary>
    private void UpdateHandSweep()
    {
        // Re-derive the suppression set from scratch every tick (stale-flag proof: a card that
        // left the arc mid-frame is cleared here or by its own OnDisable).
        for (int i = 0; i < _handSuppressed.Count; i++)
        {
            if (_handSuppressed[i] != null)
                _handSuppressed[i].SetHandPopSuppressed(false);
        }
        _handSuppressed.Clear();

        FanSweepPick<VRCard> pick = FanSweepPick<VRCard>.Empty;
        VRHand? winnerHand = null;
        VRHand? missHand = null;
        float winnerScale = 1f, missScale = 1f;

        // A hand qualifies while it is tracked and NOT busy holding something. In HELD mode the
        // pinch that opened the browse holds via _followHand, whose Grabber.Held != null then
        // naturally excludes it (a held hand is not a sweeping hand); the explicit identity test
        // keeps it out even in the frame before the hold registers.
        for (int h = 0; h < 2; h++)
        {
            VRHand? hand = h == 0 ? VRHands.Left : VRHands.Right;
            if (hand == null || ReferenceEquals(hand, _followHand) || !hand.HasPose
                || hand.Grabber.Held != null)
                continue;

            Vector3 tip = hand.Rig.IndexTip.position;
            Vector3 palm = hand.Rig.PalmCenter.position;
            float scale = Mathf.Max(hand.WorldScale, 1e-4f);

            FanSweepPick<VRCard> handPick = FanSweepPick<VRCard>.Empty;
            for (int i = 0; i < _cards.Count; i++)
            {
                VRCard c = _cards[i];
                if (c == null)
                    continue;
                // Reach per card from its OWN live world width: a browse card carries the board's
                // scale, so a real-metre constant would mean something different on every player's
                // board (the 0,32× vs 0,80× split in the 2026-08-02 hardware logs). Held/rooted
                // cards are rejected inside Score via IFanSweepTarget.SweepEligible.
                FanReach reach = FanSweep.ResolveReach(scale, ((IFanSweepTarget)c).SweepFaceWidthWorld);
                FanSweep.Score(c, tip, palm, reach, _handWinner, tipFirst: true, ref handPick);
            }

            if (handPick.Winner != null && handPick.BestScore < pick.BestScore)
            {
                // Keep whichever near-miss record is the closer one across the two hands — the
                // winner branch overwrites the whole pick, and the miss diagnostic must survive.
                VRCard? keptMiss = pick.Miss;
                float keptContact = pick.MissContact, keptTip = pick.MissTip, keptPalm = pick.MissPalm;
                pick = handPick;
                winnerHand = hand;
                winnerScale = scale;
                if (keptMiss != null && keptContact < pick.MissContact)
                {
                    pick.Miss = keptMiss;
                    pick.MissContact = keptContact;
                    pick.MissTip = keptTip;
                    pick.MissPalm = keptPalm;
                }
            }
            else if (handPick.Miss != null && handPick.MissContact < pick.MissContact)
            {
                pick.Miss = handPick.Miss;
                pick.MissContact = handPick.MissContact;
                pick.MissTip = handPick.MissTip;
                pick.MissPalm = handPick.MissPalm;
                missHand = hand;
                missScale = scale;
            }
        }

        VRCard? winner = pick.Winner;
        _handWinnerHand = winner != null ? winnerHand : null;
        if (!ReferenceEquals(winner, _handWinner))
        {
            _handWinner?.SetFingertipHover(false);
            _handWinner = winner;
            _handWinner?.SetFingertipHover(true);
            // Re-split the arc around the new pivot (hand-fan parity): the winner holds still and
            // its neighbours step aside. Only on a CHANGE, so the layout is not touched per frame.
            int index = winner != null ? _cards.IndexOf(winner) : -1;
            if (index != _handWinnerIndex)
            {
                _handWinnerIndex = index;
                Relayout(instant: false);
            }

            float now = Time.unscaledTime;
            if (winner != null && winnerHand != null && now >= _nextHandLogAt)
            {
                _nextHandLogAt = now + 0.5f;
                FanSweep.LogWinner("Pile-browse", winnerHand.Side.ToString(), pick,
                    FanSweep.ResolveReach(winnerScale, ((IFanSweepTarget)winner).SweepFaceWidthWorld));
            }
        }

        if (winner == null)
        {
            // Nothing won: say by how much the nearest card was missed, against the reach that was
            // actually in force. Throttled hard — a hand resting near the board would flood it.
            if (pick.Miss != null && missHand != null && Time.unscaledTime >= _nextHandMissLogAt)
            {
                FanReach missReach = FanSweep.ResolveReach(missScale,
                    ((IFanSweepTarget)pick.Miss).SweepFaceWidthWorld);
                if (pick.MissContact <= missReach.Palm * 2f)
                {
                    _nextHandMissLogAt = Time.unscaledTime + 2f;
                    FanSweep.LogNearMiss("Pile-browse", missHand.Side.ToString(), pick, missReach);
                }
            }
            return;
        }
        // Scope the suppression to the WINNING hand (per card, not through the global
        // VRCard.HandArbitrationHand the ability fan uses — with both hands sweeping two fans at
        // once, a single global "the arbitration hand" cannot describe the state any more, and
        // whichever fan wrote it last would silently retune the other's grab gate).
        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard c = _cards[i];
            if (c == null || c.IsHeld || ReferenceEquals(c, winner))
                continue;
            c.SetHandPopSuppressed(true, winnerHand);
            _handSuppressed.Add(c);
        }
    }

    /// <summary>
    /// The ONE browse card <paramref name="hand"/> is physically in contact with, or null — the
    /// browse counterpart of <see cref="ItemsPile.HandOwnedChip"/> and
    /// <c>CardsDriver.HandOwnedFanCard</c>. It is the hand-sweep winner when THIS hand elected it,
    /// else the hand's own proximity-grab candidate (which is what its trigger would otherwise
    /// take) as long as that candidate is in this arc.
    ///
    /// WHY the driver consults this (user report 2026-08-03: the right hand highlights a browse
    /// card but the trigger does not pick it up). While a hand reaches INTO the arc its beam is
    /// somewhere else entirely — the arc floats ABOVE the control board, so the ray behind the hand
    /// lands on the board — and the board laser then clamps the beam
    /// (<c>Ray.UiHitOverride</c>), which makes <c>ProximityGrabber</c> DEFER the trigger to the
    /// laser (<c>Ray.HasFreshUiHit</c>) and consumes the press as a board click instead. The
    /// 2026-08-03 hardware log has that exact sequence: three <c>Board: laser click → Board</c>
    /// lines interleaved with <c>Pile-browse hand sweep (Right): WINNER …</c> and not one pluck.
    /// Handing the trigger to the card the hand is holding up is the same single-owner contract the
    /// ability fan (<c>UpdateFanLaser</c>'s hand branch) and the item fan already enforce.
    /// </summary>
    internal VRCard? HandOwnedCard(VRHand? hand)
    {
        if (hand == null || !IsOpen)
            return null;
        // Unity's lifetime-aware != (a destroyed card is "null" without being a null reference).
        if (_handWinner != null && ReferenceEquals(_handWinnerHand, hand)
            && !_handWinner.IsHeld && _handWinner.CanGrab)
            return _handWinner;
        if (hand.Grabber.Highlighted is VRCard prox && prox != null && !prox.IsHeld && prox.CanGrab
            && _cards.Contains(prox))
            return prox;
        return null;
    }

    /// <summary>Drop any live hand-sweep lift + suppression (browse close / destroy) so a closed
    /// browse never leaves a stale pop or a suppressed card behind.</summary>
    private void ClearHandSweep()
    {
        if (_handWinner != null)
            _handWinner.SetFingertipHover(false);
        _handWinner = null;
        _handWinnerHand = null;
        _handWinnerIndex = -1; // the split closes with the lift
        for (int i = 0; i < _handSuppressed.Count; i++)
        {
            if (_handSuppressed[i] != null)
                _handSuppressed[i].SetHandPopSuppressed(false);
        }
        _handSuppressed.Clear();
    }

    // ------------------------------------------------------------------ laser pick --

    /// <summary>
    /// Geometric ray hit-test over the browse arc (item 5, laser-hover to read /
    /// pluck close) — the browse counterpart of <see cref="CardFan.TryRaycast"/>, sharing
    /// its shape (per-card plane + rect, nearest hit, sticky-hover hysteresis so overlap
    /// doesn't flip the highlight) but NOT its geometry. No allocations.
    ///
    /// ROOT CAUSE of "der Laser geht einfach durch die Karten" (user report 2026-08-03), which
    /// this method used to have THREE independent halves of. The comment here used to claim the
    /// browse arc "has no pop feedback loop", and that claim was simply false —
    /// <c>CardsDriver.UpdateBrowseLaser</c> calls <c>SetLaserHover(true)</c> on the hovered browse
    /// card exactly like the ability fan does, and <c>VRCard.TickHomePose</c> then moves the card
    /// up to <c>[Cards] FanSelectedPopForward</c> (3,5 cm card-local, ×1.3 arc scale) TOWARD the
    /// viewer and grows it ×1.18. So the target moved the instant the beam landed on it:
    ///  1. FRAME. The test intersected the card's LIVE transform, i.e. the popped pose, so the
    ///     plane the pick used was never the plane the pick had accepted. The fix is the one the
    ///     fan already ships: accept EITHER the RESTING rect (<see cref="VRCard.TryGetRestingLaserRect"/>
    ///     — immune to the pop, so hovering cannot move the target out from under the beam) OR the
    ///     LIVE rect (<see cref="VRCard.TryGetLiveLaserRect"/> — the card WHERE IT VISIBLY IS while
    ///     the HAND sweep has lifted it off its resting slot, and during the emerge flight). Both
    ///     rects come back in world metres with world half-extents, so the frame mixing that fed
    ///     card-LOCAL half-extents to an <c>InverseTransformPoint</c> result while scaling the
    ///     overshoot by <c>lossyScale</c> is gone too. Union of two poses = strictly monotone: the
    ///     pop can only ever ADD acceptance to the card the beam is already on, never remove it,
    ///     which is what breaks the loop.
    ///  2. MARGIN. The arc used EXACT half-extents while the angular rescue evaluates to exactly
    ///     zero at reading distance (see <see cref="FanSweep.LaserAcceptMargin"/>) — no tolerance
    ///     at all. The log's alternating HIT / "0.1 cm outside its face" is that in numbers.
    ///  3. GRAZING RAYS. <c>denom &lt; 1e-5</c> only rejected an exactly parallel beam, so a ray
    ///     pointed somewhere else entirely still "crossed" each card's infinite plane metres away
    ///     and was reported as a near miss with a metre-scale pad (see
    ///     <see cref="FanSweep.LaserMinFaceDot"/>).
    /// </summary>
    internal bool TryRaycast(Vector3 origin, Vector3 direction, VRCard? sticky,
        out VRCard? card, out Vector3 point, out float distance,
        bool allowNearMiss = false)
    {
        card = null;
        point = default;
        distance = float.PositiveInfinity;
        LastLaserPick = FanSweep.FanLaserPick.None;

        if (!IsOpen || _root == null)
            return false;

        // PASS 2 bookkeeping (the angular rescue): the least-overshooting card that missed the
        // exact rect, and by how much relative to the pad it was allowed at its own distance.
        VRCard? rescue = null;
        Vector3 rescuePoint = default;
        float rescueDist = 0f, rescueOvershoot = 0f, rescuePad = 0f, rescueRatio = float.MaxValue;
        float rescueWidth = 0f;
        // Nearest card the beam missed ENTIRELY (recorded even when the pad could not save it) —
        // "the ray was 4 cm off 'Wunde' with 1 cm of pad" is the line that decides aim vs. logic.
        var miss = FanSweep.FanLaserPick.None;
        float missOvershoot = float.MaxValue;

        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard c = _cards[i];
            if (c == null || c.IsHeld || !c.gameObject.activeInHierarchy)
                continue;

            // Best of this card's TWO poses (resting first, then live — see the doc above).
            bool hit = false;
            Vector3 hitPoint = default;
            float hitDist = 0f, hitWidth = 0f;
            float bestOver = float.MaxValue;
            Vector3 overPoint = default;
            float overDist = 0f, overPad = 0f, overWidth = 0f;

            for (int pose = 0; pose < 2; pose++)
            {
                Vector3 center, normal, right, up;
                float halfW, halfH;
                bool valid = pose == 0
                    ? c.TryGetRestingLaserRect(out center, out normal, out right, out up, out halfW, out halfH)
                    : c.TryGetLiveLaserRect(out center, out normal, out right, out up, out halfW, out halfH);
                if (!valid || halfW <= 1e-5f || halfH <= 1e-5f)
                    continue;

                // Cards face the viewer with −Z, so the beam meets the face along +normal.
                float denom = Vector3.Dot(direction, normal);
                // Numerical stability ONLY — see FanSweep.LaserMinFaceDenominator. A crossing that
                // lands inside the finite rect is a hit at ANY incidence: the beam is going through
                // the visible card. The angular cone that used to sit here rejected legitimate hits
                // because the card faces the HEAD while the beam leaves the HAND.
                if (denom < FanSweep.LaserMinFaceDenominator)
                    continue;
                float dist = Vector3.Dot(center - origin, normal) / denom;
                if (dist <= 0f)
                    continue;

                Vector3 world = origin + direction * dist;
                Vector3 rel = world - center;
                float overX = Mathf.Abs(Vector3.Dot(rel, right)) - halfW * FanSweep.LaserAcceptMargin;
                float overY = Mathf.Abs(Vector3.Dot(rel, up)) - halfH * FanSweep.LaserAcceptMargin;
                if (overX <= 0f && overY <= 0f)
                {
                    if (!hit || dist < hitDist)
                    {
                        hit = true;
                        hitPoint = world;
                        hitDist = dist;
                        hitWidth = halfW * 2f;
                    }
                    continue;
                }
                if (!allowNearMiss)
                    continue;
                float overshoot = Mathf.Max(overX, 0f) + Mathf.Max(overY, 0f);
                // The grazing-plane bound now lives HERE, where the defect actually was: a crossing
                // this far outside the face was never aimed at this card (see
                // FanSweep.LaserMaxMissOvershootFactor). Bounding it in card widths — instead of
                // refusing oblique beams outright — is what lets a steeply-met card still be HIT.
                if (overshoot > Mathf.Max(halfW, halfH) * FanSweep.LaserMaxMissOvershootFactor)
                    continue;
                if (overshoot >= bestOver)
                    continue;
                bestOver = overshoot;
                overPoint = world;
                overDist = dist;
                overPad = FanSweep.LaserPad(dist, Mathf.Max(halfW, halfH));
                overWidth = halfW * 2f;
            }

            if (!hit)
            {
                // Outside both faces. ANGULAR RESCUE (see FanSweep.LaserMinHalfAngleDegrees):
                // a browse card under a shrunken board can subtend less than the controller's own
                // aim jitter, so it is granted a minimum angular half-size — but only as this
                // second-chance pass, and never for the occluder (allowNearMiss defaults to false),
                // so a rescued near-miss can never start hiding game UI behind the arc.
                if (!allowNearMiss || bestOver >= float.MaxValue)
                    continue;
                if (bestOver < missOvershoot)
                {
                    missOvershoot = bestOver;
                    miss = new FanSweep.FanLaserPick
                    {
                        Hit = false, Rescued = false, Name = c.name, Distance = overDist,
                        Overshoot = bestOver, Pad = overPad, FaceWidthWorld = overWidth,
                    };
                }
                if (overPad <= 0f || bestOver > overPad)
                    continue;
                float ratio = bestOver / overPad;
                if (ReferenceEquals(c, sticky))
                    ratio *= 0.5f; // the incumbent keeps the beam through a graze (same hysteresis as below)
                if (ratio >= rescueRatio)
                    continue;
                rescue = c;
                rescuePoint = overPoint;
                rescueDist = overDist;
                rescueOvershoot = bestOver;
                rescuePad = overPad;
                rescueRatio = ratio;
                rescueWidth = overWidth;
                continue;
            }

            if (ReferenceEquals(c, sticky))
            {
                card = c;
                point = hitPoint;
                distance = hitDist;
                LastLaserPick = new FanSweep.FanLaserPick
                {
                    Hit = true, Rescued = false, Name = c.name, Distance = hitDist,
                    Overshoot = 0f, Pad = 0f, FaceWidthWorld = hitWidth,
                };
                return true;
            }

            if (hitDist >= distance)
                continue;
            card = c;
            point = hitPoint;
            distance = hitDist;
            LastLaserPick = new FanSweep.FanLaserPick
            {
                Hit = true, Rescued = false, Name = c.name, Distance = hitDist,
                Overshoot = 0f, Pad = 0f, FaceWidthWorld = hitWidth,
            };
        }

        if (card == null && rescue != null)
        {
            card = rescue;
            point = rescuePoint;
            distance = rescueDist;
            LastLaserPick = new FanSweep.FanLaserPick
            {
                Hit = true, Rescued = true, Name = rescue.name, Distance = rescueDist,
                Overshoot = rescueOvershoot, Pad = rescuePad, FaceWidthWorld = rescueWidth,
            };
        }
        else if (card == null)
        {
            LastLaserPick = miss;
        }
        return card != null;
    }

    /// <summary>
    /// What the last <see cref="TryRaycast"/> decided — hit or miss, which card, dead-on or
    /// rescued by the angular pad. Read by <c>CardsDriver.UpdateBrowseLaser</c> for the throttled
    /// laser diagnostic; the pick itself runs per frame per hand and must stay silent.
    /// </summary>
    internal FanSweep.FanLaserPick LastLaserPick { get; private set; } = FanSweep.FanLaserPick.None;
}
