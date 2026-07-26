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
    // The browse arc gets the SAME physical-hand behaviour as the palm fan: as the free
    // (dominant) hand's index fingertip moves THROUGH the arc, the card nearest the tip
    // lifts/enlarges and every other card drops immediately — exactly ONE highlight at a
    // time, sweeping smoothly left-right. These reach constants mirror
    // CardsDriver.UpdateHandContactArbitration / CardFan.FingertipHoverReach (scale-1 metres):
    // a browse card is a CANDIDATE when the index tip is within ContactTipReach OR the palm
    // within ContactPalmReach of its collider; the WINNER is the nearest by index-tip distance
    // ALONE (palm noise stays out of the winner choice so the between-two-cards midpoint
    // resolves deterministically), with a small incumbent hysteresis so the lift never
    // flutters at a card boundary.
    private const float ContactTipReach = 0.035f;   // ~3.5 cm index-tip candidacy
    private const float ContactPalmReach = 0.13f;   // ~13 cm palm candidacy
    private const float ContactStickyMargin = 0.02f; // incumbent hysteresis (m, scale 1)

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
    private readonly List<VRCard> _handSuppressed = new(16);
    private float _nextHandLogAt; // throttle clock (unscaled s) for the winner-change log

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
    /// Reads the free (dominant) hand's index fingertip + palm DIRECTLY from the rig every
    /// frame and elects a SINGLE winner among the browse cards: the card nearest the index
    /// tip (candidacy by tip ≤ <see cref="ContactTipReach"/> OR palm ≤ <see cref="ContactPalmReach"/>,
    /// ranked by tip distance alone, with a <see cref="ContactStickyMargin"/> incumbent bonus so
    /// the lift never flutters between two cards). The winner lifts via
    /// <see cref="VRCard.SetFingertipHover"/> (the same pop the laser gives) and EVERY other browse
    /// card is <see cref="VRCard.SetHandPopSuppressed">pop-suppressed</see> for the sweeping hand,
    /// so a hand moving through the arc can never raise more than one card — exactly like the fan.
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

        VRHand? dom = VRHands.Primary;
        VRCard? winner = null, runnerUp = null;
        float winnerTip = 0f, runnerTip = 0f;
        float bestScore = float.MaxValue, secondScore = float.MaxValue;

        // The sweeping hand is the dominant/free hand: it must be tracked and NOT busy holding
        // something. In HELD mode the pinch that opened the browse holds via _followHand, whose
        // Grabber.Held != null then naturally excludes it (a held hand is not a sweeping hand).
        if (dom != null && !ReferenceEquals(dom, _followHand) && dom.HasPose
            && dom.Grabber.Held == null)
        {
            Vector3 tip = dom.Rig.IndexTip.position;
            Vector3 palm = dom.Rig.PalmCenter.position;
            float scale = dom.WorldScale;
            float tipReach = ContactTipReach * scale;
            float palmReach = ContactPalmReach * scale;
            float sticky = ContactStickyMargin * scale;

            for (int i = 0; i < _cards.Count; i++)
            {
                VRCard c = _cards[i];
                // Skip held/rooted cards: a card that cannot pop must not win, or a dead card
                // would suppress the lift of a real candidate right next to it.
                if (c == null || c.IsHeld || !c.CanGrab)
                    continue;
                if (!c.TryFingertipDistance(tip, out float tipDist)
                    || !c.TryFingertipDistance(palm, out float palmDist))
                    continue;
                if (tipDist > tipReach && palmDist > palmReach)
                    continue; // out of BOTH reaches — not a candidate (palm still qualifies)
                float score = tipDist; // rank by the index tip alone; palm only qualified candidacy
                if (ReferenceEquals(c, _handWinner))
                    score -= sticky; // hysteresis: the current lift holds until a rival is decisively closer
                if (score < bestScore)
                {
                    runnerUp = winner; secondScore = bestScore; runnerTip = winnerTip;
                    winner = c; bestScore = score; winnerTip = tipDist;
                }
                else if (score < secondScore)
                {
                    runnerUp = c; secondScore = score; runnerTip = tipDist;
                }
            }
        }

        if (!ReferenceEquals(winner, _handWinner))
        {
            _handWinner?.SetFingertipHover(false);
            _handWinner = winner;
            _handWinner?.SetFingertipHover(true);

            // Throttled log: winner + its index-tip distance + runner-up, so the next hardware
            // log proves the single-winner tip-first resolution (rate-limited against a flood).
            float now = Time.unscaledTime;
            if (winner != null && now >= _nextHandLogAt)
            {
                _nextHandLogAt = now + 0.5f;
                string runner = runnerUp != null
                    ? $"'{runnerUp.name}' (index-tip {runnerTip * 100f:F1} cm)"
                    : "none";
                Core.VRLog.Info("Cards",
                    $"Pile-browse hand sweep: '{winner.name}' — index-tip {winnerTip * 100f:F1} cm; " +
                    $"runner-up {runner}. The card nearest the index finger lifts; sweeping keeps " +
                    "exactly ONE browse card highlighted.");
            }
        }

        if (winner == null)
            return;
        // Scope the suppression to the sweeping hand (the same static the fan arbitration uses;
        // dom == VRHands.Primary here, so this agrees with CardsDriver's own per-frame set).
        VRCard.HandArbitrationHand = dom;
        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard c = _cards[i];
            if (c == null || c.IsHeld || ReferenceEquals(c, winner))
                continue;
            c.SetHandPopSuppressed(true);
            _handSuppressed.Add(c);
        }
    }

    /// <summary>Drop any live hand-sweep lift + suppression (browse close / destroy) so a closed
    /// browse never leaves a stale pop or a suppressed card behind.</summary>
    private void ClearHandSweep()
    {
        if (_handWinner != null)
            _handWinner.SetFingertipHover(false);
        _handWinner = null;
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
