using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// The THIRD control-board pile (item 4, "Gegenstände"): the acting character's
/// equipped ITEM cards, mounted below the burnt pile and browsed like the discard /
/// burnt stacks (poke — or the board laser — toggles a fixed reading wall above the
/// board; there is NO hand-held variant, see <see cref="TogglePoke"/>). Content is
/// <see cref="CItem"/> read live from
/// <c>PlayerActor.Inventory.AllItems</c> (CInventory.cs:35); the inventory is read,
/// never written (using an item goes through the game's own <c>UseItemService</c>).
///
/// ITEMS REWORK (hardware round 2 — the prior bespoke chips FAILED on hardware):
/// <list type="bullet">
/// <item>REAL card face (was: grey slab; <c>miniIcon</c> is null for items). Each chip
/// hosts a LIVE <c>ItemCardUI</c> obtained from the game's own <c>ObjectPool</c>
/// (<c>SpawnCard(item.ID, ECardType.Item, …)</c>) reparented onto a world-space canvas —
/// the exact card the flat game shows, with its addressable background art
/// (<c>ItemConfigUI.BackgroundImage</c>) loading async onto <c>cardBackground</c>. A
/// legacy colored slab + name is kept only as a fallback when the pool is unavailable.</item>
/// <item>NORMAL-CARD interaction (was: pinch-only). Chips now support the SAME set as
/// ability cards: fingertip hand-sweep POP (readability, single-winner like
/// <see cref="PileBrowser"/>), dominant-hand LASER hover+pluck (a dedicated geometric
/// fan pick, <see cref="TryLaserRaycast"/>, driven by <c>CardsDriver.UpdateItemFanLaser</c>
/// exactly like the pile-browse fan — pops on hover / plucks on trigger), pinch-GRAB,
/// and read-in-hand (snap upright + enlarged).</item>
/// <item>USE = a CLIP-IN slot (was: a floating drop pad computed once). The slot is board
/// furniture built by <see cref="PlayTray"/> UNDER the board next to Confirm/Undo
/// (per-board <c>ItemUseSlotOffset</c>, debug-menu tunable). Its visibility is
/// re-evaluated LIVE every tick: shown ONLY while it is the local character's own turn
/// (<see cref="CardsGameApi.IsActionTurn"/>) AND a held item card's live
/// <c>SlotState</c> is usable. Dropping that card into the slot activates through the
/// game's own items-bar slot click when a live slot exists (the exact 2D/proxy seam),
/// falling back to <c>new UseItemService(hand.PlayerActor).UseItem(cItem)</c> (which owns
/// ALL multiplayer sync + re-validates the item).</item>
/// <item>ACTIVATION SPLIT (requirement C): PLAIN use/toggle items activate EXCLUSIVELY by
/// this place-into-slot flow — their symbols are filtered out of the docked
/// <c>UIUseItemsBar</c> (WorldUI <c>UseBarsSurface</c>); items whose activation opens an
/// element SUB-CHOICE at the slot (<see cref="CardsGameApi.ItemNeedsSubChoice"/>) keep
/// their bar symbol instead and never clip into the slot. The split also covers the
/// TAKE-DAMAGE decision: placing an OnAttacked shield/retaliate card toggles it through
/// the panel's own slot seam (<see cref="TickTakeDamagePick"/>), the panel's confirm
/// commits.</item>
/// </list>
/// State → look: CONSUMED → ashen + the hosted card's OWN consumed FX (the game's separate
/// CardSmoke plume is deliberately NOT spawned here — see the note in ItemChip.Create);
/// SPENT → rolled 90° ("tapped") in the fan + the card's spent FX; otherwise upright. A chip taken INTO the hand snaps upright + enlarged so it always
/// reads. Layout mirrors <see cref="PileBrowser"/>; open/close is driven by
/// <see cref="PileViewer"/>.
///
/// THE FAN ONLY OPENS BY USER ACTION (user report 2026-08-07: "der Gegenstands-Fächer ging beim
/// Schaden von selbst auf und liess sich nie wieder schliessen"). <see cref="Open"/> has exactly
/// ONE caller — <see cref="TogglePoke"/>, i.e. a deliberate poke/laser click on the items stack.
/// The two decision-flow pumps (<see cref="TickDemandPick"/>, <see cref="TickTakeDamagePick"/>)
/// used to raise the fan themselves AND re-raise it every 0.5 s while their flow was live, which
/// is precisely why no close could stick. They now only SERVE an already-open fan and log a
/// throttled cue naming the items stack. Any future "this flow needs the fan up" requirement must
/// raise a CUE on the stack, never call Open.
/// </summary>
internal sealed class ItemsPile
{
    // Arc geometry — same family as PileBrowser (reading, not picking).
    private const float RadiusFactor = 1.7f;
    private const float MaxArcDegrees = 110f;
    private const float MaxStepDegrees = 10f;
    private const float ChipScale = 1.25f;
    private const float ZStagger = 0.004f;

    // Shared board-top anchor (requirement 2): the poke-toggle item fan opens at the SAME
    // spot above the control board as the discard/burnt PileBrowser (mirror of
    // PileBrowser.BoardFloatHeight / BoardFloatProudZ / BoardAnchorBase). The root parents
    // under the board root (PlayTray.Current.Root) so it inherits the board's live pose + scale.
    private const float BoardFloatHeight = 0.26f;
    private const float BoardFloatProudZ = -0.05f;
    private static Vector3 BoardAnchorBase =>
        new(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);

    /// <summary>
    /// Requirement #2: the per-board ITEM-fan offset — nudges the item fan root (board-anchored,
    /// held-follow, and above-board reading poses) INDEPENDENTLY of the ability-card fan, since item
    /// cards are a different, near-square shape. Read LIVE every <see cref="Tick"/> (a cheap Vector3
    /// config read) so the debug-menu 'Item card X/Y/Z' steppers move an OPEN fan immediately, exactly
    /// like <see cref="CardsConfig.BrowseFanOffset"/> does for the discard/burnt fans.
    /// </summary>
    private static Vector3 ItemFanOffset => CardsConfig.ItemCardOffset(CardsConfig.CurrentBoard).Value;

    // The hand-sweep reach constants that used to be mirrored here (scale-1 metres) are gone: the
    // item fan now runs the SHARED election (FanSweep.Score) with the reach resolved from each
    // chip's own live world size (FanSweep.ResolveReach). That is the fix for the 2026-08-02
    // report where one player could barely touch a chip and the other could not reproduce it —
    // the chips hang under the CONTROL BOARD and carry its scale, and the two players' boards
    // were 0,32× and 0,80×. Full root cause on FanSweep.ResolveReach.

    /// <summary>Drop-into-use capture radius (world metres at board scale 1; scaled by the slot's live scale).</summary>
    private const float UseSlotRadius = 0.13f;

    private readonly List<ItemChip> _chips = new(12);
    private Transform? _root;
    private TextMeshPro? _title;
    // The ITEMS stack transform (PileViewer.EnsureBuilt passes _items.transform; the shared pile
    // MOUNT is only the null fallback). Used solely as the emerge/collapse converge point — it is
    // not a parent and not a placement scale reference.
    private Transform? _anchor;
    private CardsHandUI? _hand;
    private string _signature = string.Empty; // last-built inventory state, for cheap live refresh
    private bool _boardAnchored; // fan parented under the board root (mirrors PileBrowser)

    // Hand-sweep single-winner state: the chip the physical hand currently lifts (null = none)
    // and WHICH hand elected it (either hand may sweep — see UpdateHandSweep).
    private ItemChip? _handWinner;
    private VRHand? _handWinnerHand;
    private float _nextHandLogAt;
    private float _nextHandMissLogAt;

    /// <summary>Arc index of the hand-sweep winner, or -1 — drives the fan SPLIT in
    /// <see cref="Relayout"/> (hand-fan parity: the highlighted chip is the pivot, its neighbours
    /// step aside, so the single winner is unmistakable while a hand sweeps through).</summary>
    private int _handWinnerIndex = -1;

    /// <summary>Chips this tick's sweep pop-suppressed, so the set can be cleared from scratch
    /// next tick (stale-flag proof: a chip that left the fan mid-frame cannot stay suppressed).</summary>
    private readonly List<ItemChip> _handSuppressed = new(12);

    // Diagnostics dedup for the live USE-slot gate log ("ITEM USE SLOT: shown/hidden …").
    private bool _useSlotShownLogged;

    // Requirement 6 (clip-in decision): the chip currently CLIPPED into the use slot awaiting a
    // Confirm/cancel decision (null = none). While set, the fan never live-rebuilds (so the decision
    // chip is never yanked), the chip is driven to the slot pose each tick, and a Confirm button shows.
    private ItemChip? _pendingUseChip;

    // Requirement 8 (drop preview): a translucent GHOST duplicate shown at the use-slot pose while a held
    // usable item card comes NEAR the slot — a preview of where it will land if released. Parented under
    // the board's use slot (so it rides the slot pose + visibility); built lazily, toggled by proximity.
    private GameObject? _useGhost;

    internal bool IsOpen { get; private set; }

    /// <summary>
    /// ALWAYS FALSE since the whole-fan trigger grab was removed (user ruling 2026-08-02 — see
    /// <see cref="TogglePoke"/>): the item fan has exactly one anchoring, board-anchored. Kept as
    /// a property, not deleted, because it is a WIRE seam — <c>NetAvatarDriver</c> fills the
    /// <c>ItemFanHeld</c>/<c>ItemFanLeftHand</c> extras fields from it, and the packet layout must
    /// not shift (no ModBuild bump for a local-only interaction change). Peers therefore always
    /// draw the ghost item fan board-anchored, which is now the only state that exists.
    /// </summary>
    internal bool IsHandHeld => false;

    /// <inheritdoc cref="IsHandHeld"/>
    internal bool IsHeldByLeftHand => false;

    /// <summary>
    /// The open BOARD-ANCHORED fan's board-local anchor position (its root sits under the board
    /// root, so <c>localPosition</c> IS the board frame), or null while closed.
    /// Multiplayer read seam for the fan-anchor wire record: the base spot plus the owner's live
    /// per-board <c>[Cards] BrowseFanOffset + ItemCardOffset</c> tuning — the part a receiver
    /// could never derive, which is why their copy floated at the untuned default.
    /// </summary>
    internal Vector3? BoardLocalAnchor =>
        IsOpen && _boardAnchored && _root != null ? _root.localPosition : null;

    /// <summary>
    /// The currently OPEN item fan (null when closed / destroyed) — the exact counterpart of
    /// <see cref="CardFan.Current"/> for ability cards.
    ///
    /// ROOT CAUSE this exists (user report 5, "Itemkarten auf der Hand sind IMMER noch nicht im
    /// Spiegel zu sehen"): both consumers of "what cards is the local player holding" —
    /// <see cref="WorldUI.AvatarMirror"/> (the local self-preview) and
    /// <see cref="Net.NetAvatarDriver"/> (the multiplayer extras packet) — could only ever find the
    /// ABILITY fan, because <see cref="CardFan"/> published itself through a static Current and the
    /// items fan published NOTHING. The items fan is owned privately by <see cref="PileViewer"/>,
    /// so neither consumer had any way to reach it, and item cards were therefore invisible in the
    /// mirror AND on every peer. Publishing the open fan the same way closes both gaps at once.
    /// </summary>
    internal static ItemsPile? Current { get; private set; }

    /// <summary>The chips the fan currently holds (read-only view — mirrored / counted, never mutated).</summary>
    internal IReadOnlyList<ItemChip> Chips => _chips;

    // ------------------------------------------------------------------ config --

    /// <summary>
    /// The ITEMS stack transform (req #5 converge point), NOT the shared pile mount — the mount
    /// sits up by the DISCARD stack, and "simplifying" this to it makes the fan emerge from the
    /// wrong pile. PileViewer passes the mount only as a null fallback.
    /// </summary>
    internal void SetAnchor(Transform anchor) => _anchor = anchor;

    /// <summary>Item count of the acting character's inventory (drives the board stack look).</summary>
    internal int Count(CardsHandUI? hand)
    {
        List<CItem>? items = ItemsOf(hand);
        return items != null ? items.Count : 0;
    }

    /// <summary>
    /// The acting character's equipped items (null-safe; never mutated). Requirement A —
    /// verified UNFILTERED: <c>CInventory.AllItems</c> (CInventory.cs:35-55) concatenates EVERY
    /// equipped slot (Head, Body, Legs, TwoHand/OneHand, SmallItems, QuestItems) with no
    /// usability/slot-state filter, and neither <see cref="Populate"/> nor <see cref="Signature"/>
    /// drops any entry — passives and timed/triggered items (the initiative boots) are ALWAYS
    /// present; state only changes the LOOK (Spent = tapped, Consumed = ashen, usable = gold
    /// frame). The historical "boots missing" report was the presented-HAND mismatch during
    /// another actor's decision phase — fixed in CardsDriver.CurrentHand (deciding-actor chain).
    /// </summary>
    private static List<CItem>? ItemsOf(CardsHandUI? hand)
    {
        CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
        CInventory? inv = actor != null ? actor.Inventory : null;
        return inv != null ? inv.AllItems : null;
    }

    // ------------------------------------------------------------------ open/close --

    /// <summary>
    /// Toggle: open at a fixed reading wall above the board, or close if already open. THE only
    /// way the item fan opens (poke on the items stack, or the board laser's LaserToggle).
    ///
    /// REMOVED (user ruling 2026-08-02: "Das Greifen des GANZEN Fächers mit dem Trigger war
    /// möglich — das komplett entfernen, das war nie gewollt."): the items stack used to also be
    /// PINCH-GRABBABLE, which opened the fan as one object hanging off the grabbing palm
    /// (<c>OpenHeld</c> / <c>ReleaseHeld</c> / a <c>_followHand</c> the whole fan parented to).
    /// Besides being unwanted it broke both other item interactions for as long as it was held:
    /// the grabbing hand's <c>Grabber.Held</c> was non-null, which is an early-out in
    /// <c>CardsDriver.UpdateItemFanLaser</c> (no laser hover, no laser pluck — the reported
    /// "the laser goes straight through the fan"), and that same hand was excluded from the
    /// hand sweep (no highlights). Individual chip pluck is untouched.
    /// </summary>
    internal void TogglePoke(CardsHandUI hand, VRHand vrHand)
    {
        if (IsOpen)
        {
            Close($"user toggle (stack poke/laser, {vrHand.Side})");
            return;
        }
        Open(hand, vrHand);
    }

    /// <summary>
    /// USER-OPEN ONLY (user report 2026-08-07: "der Gegenstands-Fächer ging bei Schaden von selbst
    /// auf und liess sich nicht mehr schliessen"). This method has exactly ONE caller —
    /// <see cref="TogglePoke"/>, i.e. a deliberate poke/laser click on the items STACK. The two
    /// flow pumps below (<see cref="TickDemandPick"/>, <see cref="TickTakeDamagePick"/>) used to
    /// call it on their own AND re-call it every 0.5 s for as long as the flow was live, which is
    /// exactly the reported defect: the fan opened by itself on damage and every close (click-away,
    /// stack poke, foreign interaction) was undone within half a second, so it could never be
    /// closed. Both auto-open blocks are GONE; the flows now only SERVE an already-open fan and
    /// tell the player, through the board's item-use slot and the demand banner, that the items
    /// stack is where the candidates live. Keep it that way: any new "the flow needs the fan"
    /// requirement must raise a CUE, never call Open.
    /// </summary>
    private void Open(CardsHandUI hand, VRHand? by = null)
    {
        // AN EMPTY FAN MUST NOT OPEN (user ruling 2026-08-03: "Da 0 Gegenstände da waren soll es
        // auch gar nicht möglich sein den Fächer zu öffnen!"). With no items the fan used to open
        // anyway: no chips, no laser targets, and only its own "Gegenstände (0)" caption floating
        // over the board — which is also why it could not be clicked away, since the toggle lives
        // on the items STACK and the empty fan covers nothing to poke. Gated HERE rather than at
        // the toggle so every entry point is covered, including the flow-driven opens (surrender
        // pick, take-damage place), where an empty fan is equally useless.
        int count = ItemsOf(hand)?.Count ?? 0;
        if (count <= 0)
        {
            VRLog.Info("Cards", "Items pile browse REFUSED — the actor carries 0 items, so there is " +
                                "nothing to fan out. An empty fan has no chips and no way to close " +
                                "itself; the stack stays closed instead " +
                                $"(requested by {(by != null ? by.Side.ToString() : "auto/flow")}).");
            return;
        }
        _hand = hand;
        EnsureRoot();
        // Requirement 2: the fan anchors under the board root at the shared board-top spot.
        Transform? boardRoot = PlayTray.Current?.Root;
        _boardAnchored = boardRoot != null;
        Transform parent = boardRoot != null ? boardRoot
                         : (_anchor != null ? _anchor : _root!.parent);
        if (parent != null && _root!.parent != parent)
            _root.SetParent(parent, worldPositionStays: false);
        _root!.gameObject.SetActive(true);
        IsOpen = true;
        Current = this; // publish to the mirror + the net extras sender (see Current's doc comment)
        _signature = string.Empty; // force a build
        Populate(hand);
        if (boardRoot != null)
            PlaceAboveBoard(); // float above the board, inherit its scale (mirror PileBrowser)
        else
            PlaceAtHead(); // no board — head-relative fallback
        EmergeAll(); // req #5: fly the chips OUT of the pile stack (after _root is placed)
        VRLog.Info("Cards", $"ITEM FAN OPEN ({(boardRoot != null ? "board-anchored" : "head-relative fallback")}, " +
                            $"{_chips.Count} item(s)) — trigger: {(by != null ? $"USER stack poke/laser ({by.Side})" : "USER (unattributed)")}. " +
                            "There is NO automatic open path any more; if this line ever appears without a " +
                            "user trigger, a flow called Open() again.");
    }

    /// <summary>
    /// Close the fan. <paramref name="reason"/> is logged so every close (and, by absence, every
    /// failure to close) is traceable in the hardware log — the item-fan counterpart of
    /// <c>CardsDriver.CloseBrowser</c>'s reason string.
    /// </summary>
    internal void Close(string reason = "unspecified")
    {
        if (!IsOpen)
            return;
        _lastCloseReason = reason;
        IsOpen = false;
        if (ReferenceEquals(Current, this))
            Current = null; // unpublish (mirror + net extras stop showing the fan this frame)
        _boardAnchored = false;
        ClearHandSweep();
        _pendingUseChip = null; // #6: drop any pending decision on close
        _demandChip = null;     // surrender pick: the chip dies with the fan; the game selection
                                // survives in the picker and the pump re-opens the fan next tick
        _tdChip = null;         // take-damage place: same — the toggle survives in the panel
        if (_useGhost != null) // #8: never leave a drop-preview floating once the fan is gone
            _useGhost.SetActive(false);
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        PlayTray.Current?.SetItemUseSlotVisible(false); // never leave the use slot up once the fan is gone
        _useSlotShownLogged = false;
        CollapseChips(); // req #5: fly the chips BACK INTO the pile stack, then self-destroy
        _signature = string.Empty;
        if (_root != null)
            _root.gameObject.SetActive(false);
        VRLog.Info("Cards", $"ITEM FAN CLOSE — trigger: {reason}. It stays closed until the player pokes/laser-clicks " +
                            "the items stack again (no flow re-opens it).");
    }

    /// <summary>Last close reason (diagnostics only — surfaced by the demand/take-damage cue lines
    /// so a "why is the fan not up?" question is answerable from the log alone).</summary>
    private string _lastCloseReason = "never closed";

    internal void Destroy()
    {
        ClearHandSweep();
        ClearChips();
        _pendingUseChip = null; // #6
        _demandChip = null;     // surrender pick
        _demandActive = false;
        _demandLoseReward = false;
        _tdChip = null;         // take-damage place
        _tdActive = false;
        if (ReferenceEquals(Current, this))
            Current = null;
        IsOpen = false;
        _boardAnchored = false;
        _hand = null;
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        PlayTray.Current?.SetItemUseSlotVisible(false);
        if (_useGhost != null) // #8: the ghost is parented under the (foreign) use slot — destroy it explicitly
        {
            Object.DestroyImmediate(_useGhost);
            _useGhost = null;
        }
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
            _title = null;
        }
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject("GloomhavenVR.ItemsPile").transform;
        Core.VRLayers.Apply(_root.gameObject);

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(_root, worldPositionStays: false);
        titleGo.transform.localPosition = new Vector3(0f, 0.16f, -0.004f);
        _title = titleGo.AddComponent<TextMeshPro>();
        _title.text = string.Empty;
        _title.alignment = TextAlignmentOptions.Center;
        _title.color = new Color(1f, 0.9f, 0.6f);
        Core.TmpFit.Fit(_title, 0.30f, 0.032f, maxFontSize: 0.34f, wrap: false);
        WorldUI.MrBacking.Label(_title); // fan title floats over the room in MR
    }

    // ------------------------------------------------------------------ per-frame --

    /// <summary>
    /// Called every frame while the board piles show (from <see cref="PileViewer.TickStatus"/>).
    /// Self-closes on context loss, follows the holding hand when held, runs the physical
    /// hand-sweep pop, gates the live USE slot, and cheaply rebuilds the chips when an item's
    /// state changed — but never while a chip is in hand, so a grab is never yanked away.
    /// </summary>
    internal void Tick(CardsHandUI? hand)
    {
        if (!IsOpen)
            return;
        if (hand == null || hand != _hand)
        {
            Close();
            return;
        }

        // Live refresh: rebuild only when nothing is held AND no decision is pending (so a chip clipped
        // into the use slot is never yanked by a rebuild) and the inventory state moved. The take-damage
        // clip (_tdChip) must guard too: unlike the surrender pick, its select seam (ToggleShieldItem →
        // Inventory.SelectItem) CHANGES the item's SlotState, which changes the signature — without the
        // guard the very act of placing the shield card would rebuild the fan and destroy the clipped chip.
        if (!AnyHeld() && _pendingUseChip == null && _tdChip == null)
        {
            string sig = Signature(hand);
            if (sig != _signature)
                Populate(hand);
        }

        // Physical fingertip sweep: lift the chip nearest the dominant index tip (single-winner).
        UpdateHandSweep();

        // NOTE (usable-highlight): the per-chip gold rim glow is toggled inside each chip's own face
        // maintenance from CanUseNow — nothing to drive from here. The throttled tally diagnostic lives
        // in PileViewer.TickStatus instead, because it must also report while this fan is CLOSED (the
        // items STACK carries the same highlight then, and there are no chips to count).

        if (_demandActive)
        {
            // ITEM SURRENDER pick: TickDemandPick owns the slot visibility, the clipped chip
            // and the confirm — the action-turn use gate below must not fight it (it would
            // hide the slot every tick: IsActionTurn is false while the Choreographer waits
            // in WaitingForItemRefresh).
            TickUseGhost(false, null);
        }
        else if (_tdActive)
        {
            // TAKE-DAMAGE shield place (req C): TickTakeDamagePick owns the slot visibility,
            // the ghost and the clipped chip — the action-turn gate below must not fight it
            // (IsActionTurn is false while the enemy's attack waits on the damage decision).
        }
        else if (_pendingUseChip != null)
        {
            // Requirement 6: a chip is CLIPPED into the use slot awaiting a decision — keep the slot +
            // Confirm button up and glue the chip to the slot pose, or resolve the cancel/invalidation.
            TickPendingUse(hand);
            TickUseGhost(false, null); // clipped in — the ghost preview is not needed
        }
        else
        {
            // Requirement 3 (LIVE gate, re-evaluated every tick): the USE clip-in slot shows ONLY
            // while (a) it is this hand's own action turn AND (b) a HELD item card's live SlotState
            // is usable. Poll the held chip's live SlotState here (never cache it at create time).
            bool turn = CardsGameApi.IsActionTurn(hand);
            ItemChip? heldUsable = HeldActivatableChip();
            bool showUseSlot = turn && heldUsable != null;
            PlayTray.Current?.SetItemUseSlotVisible(showUseSlot);
            // Requirement 8: preview where the held card lands as it nears the slot.
            TickUseGhost(showUseSlot, heldUsable);
            if (showUseSlot != _useSlotShownLogged)
            {
                _useSlotShownLogged = showUseSlot;
                Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
                Vector3 pos = slot != null ? slot.position : Vector3.zero;
                VRLog.Info("Cards", $"ITEM USE SLOT: {(showUseSlot ? "shown" : "hidden")} " +
                                    $"(turn={turn} heldUsable={(heldUsable != null)}) at ({pos.x:F2},{pos.y:F2},{pos.z:F2}).");
            }
        }

        // Fan facing/position: BOARD-ANCHORED → re-read the shared board anchor (+ live
        // BrowseFanOffset) and billboard toward the head (ISSUE #7). The former hand-held branch
        // died with the whole-fan trigger grab (see TogglePoke).
        if (_root == null)
            return;
        if (_boardAnchored)
        {
            _root.localPosition = BoardAnchorBase + CardsConfig.BrowseFanOffset.Value + ItemFanOffset; // req #2 item nudge
            FaceHead(_root);
        }
    }

    /// <summary>Billboard a transform to face the head (mirror of PileBrowser.Tick's facing math).</summary>
    private static void FaceHead(Transform t)
    {
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Vector3 away = t.position - head.transform.position;
        if (away.sqrMagnitude < 1e-6f)
            return;
        // +Z points away from the viewer (uGUI/sprites read from -Z); tilt back a touch.
        t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up)
                     * Quaternion.Euler(-12f, 0f, 0f);
    }

    private bool AnyHeld()
    {
        for (int i = 0; i < _chips.Count; i++)
            if (_chips[i] != null && _chips[i].Holder != null)
                return true;
        return false;
    }

    // ------------------------------------------------------------------ content --

    private void Populate(CardsHandUI hand)
    {
        if (_root == null)
            return;
        ClearChips();

        // Flow 1 (goal-chest forfeit): the demanded candidates are the picker's REWARD items,
        // never the inventory — the same chips/fan machinery renders them (ItemChip.Create only
        // needs a CItem with an ID for the pooled ItemCardUI face).
        List<CItem>? items = DemandItemsOverride() ?? ItemsOf(hand);
        if (items == null || items.Count == 0)
        {
            _signature = Signature(hand);
            RefreshTitle(0);
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            CItem item = items[i];
            if (item == null)
                continue;
            ItemChip chip = ItemChip.Create(this, _root, item);
            _chips.Add(chip);
        }
        _signature = Signature(hand);
        RefreshTitle(_chips.Count);
        Relayout();
    }

    private void RefreshTitle(int count)
    {
        if (_title != null)
            _title.text = $"{PileViewer.Caption(PileKind.Items)} ({count})";
    }

    private void ClearChips()
    {
        for (int i = 0; i < _chips.Count; i++)
            if (_chips[i] != null)
                Object.DestroyImmediate(_chips[i].gameObject);
        _chips.Clear();
        _handWinner = null;
    }

    /// <summary>World anchor the fan emerges from / collapses into (req #5): the ITEMS stack
    /// transform handed to <see cref="SetAnchor"/>. Falls back to the fan root.</summary>
    private Vector3 PileConvergeWorld() =>
        _anchor != null ? _anchor.position : (_root != null ? _root.position : Vector3.zero);

    /// <summary>
    /// Requirement 5 (emerge): once the fan root is placed, drop every chip ONTO the pile stack point
    /// (in ROOT-LOCAL space, so it rides the board like the arc homes) and start each chip's home-glide
    /// — the chips visibly fly OUT of the pile into the arc. Reuses the same easing as the release glide.
    /// </summary>
    private void EmergeAll()
    {
        if (_root == null)
            return;
        Vector3 localConverge = _root.InverseTransformPoint(PileConvergeWorld());
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c != null && c.Holder == null)
                c.BeginEmerge(localConverge);
        }
    }

    /// <summary>
    /// Requirement 5 (collapse): on close, hand each chip off to a self-driven glide BACK INTO the pile
    /// stack, then it destroys itself (recycling its ItemCardUI in OnDisable). The chips are re-parented
    /// OUT of the fan root first so they keep updating after the root is deactivated. A held chip (rare
    /// close-mid-grab) is dropped immediately. Clears the live list so a re-open builds fresh chips.
    /// </summary>
    private void CollapseChips()
    {
        Vector3 converge = PileConvergeWorld();
        Transform? keep = PlayTray.Current?.Root != null ? PlayTray.Current!.Root
                        : (_anchor != null ? _anchor : null);
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c == null)
                continue;
            if (c.Holder != null)
            {
                Object.DestroyImmediate(c.gameObject); // held on close — just drop it
                continue;
            }
            if (keep != null)
                c.transform.SetParent(keep, worldPositionStays: true); // survive the root deactivation
            c.BeginCollapse(converge);
        }
        _chips.Clear();
        _handWinner = null;
    }

    /// <summary>Cheap change key: item count + each item's slot-state (drives live refresh).
    /// Instance (no longer static): while the goal-chest forfeit demand overrides the content
    /// source the key is prefixed + built from the REWARD items' IDs instead, so the flip
    /// inventory↔rewards (and any reward-list change) rebuilds, and inventory changes during
    /// the forfeit cannot yank the reward fan.</summary>
    private string Signature(CardsHandUI? hand)
    {
        List<CItem>? rewards = DemandItemsOverride();
        if (rewards != null)
        {
            var rb = new StringBuilder(8 + rewards.Count * 6);
            rb.Append("LR:").Append(rewards.Count).Append(':');
            for (int i = 0; i < rewards.Count; i++)
                rb.Append(rewards[i] != null ? rewards[i].ID : -1).Append(',');
            return rb.ToString();
        }
        List<CItem>? items = ItemsOf(hand);
        if (items == null)
            return "0";
        var sb = new StringBuilder(items.Count * 4);
        sb.Append(items.Count).Append(':');
        for (int i = 0; i < items.Count; i++)
        {
            CItem it = items[i];
            sb.Append(it != null ? (int)it.SlotState : -1).Append(',');
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ placement --

    /// <summary>
    /// Shared board-top reading pose (requirement 2, mirror of PileBrowser.PlaceAboveBoard):
    /// float the fan at the SAME spot above the control board the discard/burnt fans use.
    /// </summary>
    private void PlaceAboveBoard()
    {
        if (_root == null)
            return;
        _root.localPosition = BoardAnchorBase + CardsConfig.BrowseFanOffset.Value;
        _root.localRotation = Quaternion.identity; // Tick billboards the WORLD rotation each frame
        FaceHead(_root);
    }

    /// <summary>Fixed head-relative reading pose (fallback when no control board exists).</summary>
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

    private void Relayout()
    {
        if (_root == null)
            return;
        int n = _chips.Count;
        if (n == 0)
            return;

        // PER-PILE SPREAD, read LIVE (user request 2026-08-03: the item fan must be wider so a
        // single chip can be grabbed physically). Shipped default = the old constant.
        float radius = CardsConfig.FanRadius.Value * CardsConfig.FanRadiusFactor(PileKind.Items).Value;
        float step = n > 1
            ? Mathf.Min(CardsConfig.FanStepDegrees(PileKind.Items).Value, MaxArcDegrees / (n - 1))
            : 0f;
        float start = -step * (n - 1) * 0.5f;

        // HAND-FAN PARITY (the collider strip — see FanSweep.StripWidth). Item chips overlap each
        // other by roughly half a card in this arc, and until now each one carried a FULL-size
        // grab box: a fingertip inside the overlap measured 0.0 cm to two or three chips at once,
        // which is exactly what the 2026-08-02 hardware log shows ("contact 0,0 cm; runner-up
        // 0,0 cm"). Ties do not displace the incumbent, so the lift stuck and the sweep skipped
        // chips. Shrinking each chip's box to the chord between neighbouring centres makes the
        // per-chip regions TILE, which also fixes the ProximityGrabber (it picks the nearest
        // collider by the same ClosestPoint metric) — "what pops is what I grab", by geometry.
        // The chord is fan-local; chips are enlarged by ChipScale, hence the divide.
        float stripFanLocal = n > 1 ? FanSweep.ArcChord(radius, step) / ChipScale : float.MaxValue;
        int hovered = _handWinnerIndex >= 0 && _handWinnerIndex < n ? _handWinnerIndex : -1;

        for (int i = 0; i < n; i++)
        {
            ItemChip chip = _chips[i];
            if (chip == null || chip.Holder != null)
                continue;

            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * radius,
                                  (Mathf.Cos(rad) - 1f) * radius * 0.55f,
                                  -ZStagger * i);
            Quaternion rot = Quaternion.Euler(0f, 0f, -angle * 0.85f);
            // SPENT items lie "tapped": roll the chip 90° in its slot (requirement 3).
            if (chip.State == ItemChip.Visual.Spent)
                rot *= Quaternion.Euler(0f, 0f, 90f);
            // Split the arc around the highlighted chip (hand-fan parity): the pivot holds still,
            // its neighbours slide along their OWN local right so the winner reads unmistakably.
            if (hovered >= 0 && i != hovered)
                pos += rot * new Vector3(FanSweep.SplitOffset(i - hovered) * ChipScale, 0f, 0f);
            chip.SetHome(pos, rot, ChipScale);
            // The last chip in the arc is fully exposed and the split pivot has room on both
            // sides — both keep their full grab box; everything else wears its visible strip.
            // A chip CLIPPED into the use slot (#6) is not in the arc at all and keeps its full
            // box, or pulling it back out of the slot would get harder the moment the sweep
            // re-laid the fan out around some other chip.
            chip.SetGrabStrip(chip.PendingUse || i == n - 1 || i == hovered
                ? float.MaxValue
                : stripFanLocal);
        }
    }

    // ------------------------------------------------------------------ hand sweep --

    /// <summary>
    /// Physical HAND sweep over the item fan — now literally the SAME election the ability hand
    /// fan runs (<see cref="FanSweep.Score{T}"/>), which is what the user asked for ("gleiche die
    /// Fächer der Piles an das System der Handkarten an"): EITHER free hand's index tip elects a
    /// SINGLE winner among the chips, candidacy by tip OR palm against the reach resolved from the
    /// chip's own live world size (<see cref="FanSweep.ResolveReach"/>), ranked TIP-FIRST with
    /// incumbent hysteresis. The winner POPS and every other chip is suppressed for that hand
    /// (<see cref="ItemChip.SetHandSuppressed"/>) so the ProximityGrabber's own candidate — and
    /// therefore the trigger grab — lands on the same single chip. Read-only: it only lifts for
    /// readability; the pinch/laser own the pull-into-hand.
    ///
    /// ROOT CAUSE this fixes (user report 2026-08-02, MP host: "physically I could only grab
    /// chips with the LEFT hand, but the LEFT hand produced no highlights; the RIGHT hand
    /// highlighted but could not grab anything"). The two halves had DIFFERENT hand policies:
    ///   • HIGHLIGHT was <c>VRHands.Primary</c> only — a hard dominant-hand filter, so the
    ///     off-hand swept through the fan in complete silence no matter how close it got.
    ///   • GRAB is <c>ProximityGrabber</c>, which runs on BOTH hands — but on the DOMINANT hand
    ///     the trigger defers to the laser (<c>ProximityGrabber.Tick</c> yields whenever
    ///     <c>Ray.HasFreshUiHit</c> is set, and the item-fan laser path sets
    ///     <c>Ray.UiHitOverride</c> on every hovered chip), so on the dominant hand the pluck
    ///     belongs to <c>CardsDriver.UpdateItemFanLaser</c> — which that session refused every
    ///     pluck at its blocking-modal commit gate (the MP player picker, see
    ///     <c>ModalFallback.NonBlockingMenus</c>). Net effect, exactly as reported: dominant
    ///     hand = highlight, no grab; off hand = grab, no highlight.
    /// Sweeping BOTH hands makes the highlight policy match the grab policy — every hand that
    /// can take a chip also lights it up first.
    ///
    /// SCORING: ranked TIP-FIRST, like every other fan. This method used to rank by
    /// <c>min(tip, palm)</c> — the one place the three copies of the election had genuinely
    /// drifted apart — which let a chip the PALM brushed outrank the chip the index finger was
    /// pointing at. With the colliders now tiling the arc (see <see cref="Relayout"/>) the tip
    /// distance is a clean partition, so tip-first is both correct and the shared behaviour.
    /// </summary>
    private void UpdateHandSweep()
    {
        // Re-derive the per-hand suppression from scratch every tick (stale-flag proof).
        for (int i = 0; i < _handSuppressed.Count; i++)
        {
            if (_handSuppressed[i] != null)
                _handSuppressed[i].SetHandSuppressed(null);
        }
        _handSuppressed.Clear();

        FanSweepPick<ItemChip> pick = FanSweepPick<ItemChip>.Empty;
        VRHand? winnerHand = null;
        VRHand? missHand = null;
        float winnerScale = 1f, missScale = 1f;

        // BOTH hands sweep (see doc): a hand qualifies while it is tracked and NOT holding
        // anything. A hand holding a chip is excluded on purpose — the held chip already rides
        // that hand, and popping a second one under it reads as a phantom. Each hand runs its own
        // election and the better of the two wins, so "which hand" is never guessed.
        for (int h = 0; h < 2; h++)
        {
            VRHand? hand = h == 0 ? VRHands.Left : VRHands.Right;
            if (hand == null || !hand.HasPose || hand.Grabber.Held != null)
                continue;

            Vector3 tip = hand.Rig.IndexTip.position;
            Vector3 palm = hand.Rig.PalmCenter.position;
            float scale = Mathf.Max(hand.WorldScale, 1e-4f);

            FanSweepPick<ItemChip> handPick = FanSweepPick<ItemChip>.Empty;
            for (int i = 0; i < _chips.Count; i++)
            {
                ItemChip c = _chips[i];
                if (c == null)
                    continue;
                // Reach from the chip's OWN live world width — the fix for "same build, different
                // board scale, different behaviour". Held / clipped chips are rejected inside
                // Score via IFanSweepTarget.SweepEligible.
                FanReach reach = FanSweep.ResolveReach(scale, ((IFanSweepTarget)c).SweepFaceWidthWorld);
                FanSweep.Score(c, tip, palm, reach, _handWinner, tipFirst: true, ref handPick);
            }

            if (handPick.Winner != null && handPick.BestScore < pick.BestScore)
            {
                ItemChip? keptMiss = pick.Miss;
                float keptMissContact = pick.MissContact;
                float keptMissTip = pick.MissTip, keptMissPalm = pick.MissPalm;
                pick = handPick;
                winnerHand = hand;
                winnerScale = scale;
                if (keptMiss != null && keptMissContact < pick.MissContact)
                {
                    pick.Miss = keptMiss;
                    pick.MissContact = keptMissContact;
                    pick.MissTip = keptMissTip;
                    pick.MissPalm = keptMissPalm;
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

        ItemChip? winner = pick.Winner;
        _handWinnerHand = winner != null ? winnerHand : null;
        if (!ReferenceEquals(winner, _handWinner))
        {
            _handWinner?.SetFingertipPop(false);
            _handWinner = winner;
            _handWinner?.SetFingertipPop(true);
            // Re-split the arc around the new pivot (hand-fan parity). Only on a CHANGE.
            int index = winner != null ? _chips.IndexOf(winner) : -1;
            if (index != _handWinnerIndex)
            {
                _handWinnerIndex = index;
                Relayout();
            }

            float now = Time.unscaledTime;
            if (winner != null && winnerHand != null && now >= _nextHandLogAt)
            {
                _nextHandLogAt = now + 0.5f;
                FanSweep.LogWinner("Item-fan", winnerHand.Side.ToString(), pick,
                    FanSweep.ResolveReach(winnerScale, ((IFanSweepTarget)winner).SweepFaceWidthWorld));
            }
        }

        if (winner == null)
        {
            // NEAR-MISS diagnostic (throttled, only while a hand is genuinely reaching): name the
            // hand, the chip, both probe distances and the EFFECTIVE reaches they failed, all in
            // real centimetres. This is the line that decides "the sweep is broken" versus "the
            // hand was never close enough" on the next hardware log without any arithmetic.
            if (pick.Miss != null && missHand != null && Time.unscaledTime >= _nextHandMissLogAt)
            {
                FanReach missReach = FanSweep.ResolveReach(missScale,
                    ((IFanSweepTarget)pick.Miss).SweepFaceWidthWorld);
                if (pick.MissContact <= missReach.Palm * 2f)
                {
                    _nextHandMissLogAt = Time.unscaledTime + 2f;
                    FanSweep.LogNearMiss("Item-fan", missHand.Side.ToString(), pick, missReach);
                }
            }
            return;
        }

        // SINGLE-WINNER for the GRAB too (user report "what lights up is not what I get"): every
        // other chip refuses the winning hand in ItemChip.AllowsHand, so the ProximityGrabber —
        // which picks the nearest collider by the very same ClosestPoint metric — cannot land on
        // a chip the hand merely brushed while sweeping. Scoped to the WINNING hand only: the
        // other hand keeps its own, independent candidate.
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c == null || c.Holder != null || ReferenceEquals(c, winner))
                continue;
            c.SetHandSuppressed(winnerHand);
            _handSuppressed.Add(c);
        }
    }

    private void ClearHandSweep()
    {
        _handWinner?.SetFingertipPop(false);
        _handWinner = null;
        _handWinnerHand = null;
        _handWinnerIndex = -1; // the split closes with the lift
        for (int i = 0; i < _handSuppressed.Count; i++)
        {
            if (_handSuppressed[i] != null)
                _handSuppressed[i].SetHandSuppressed(null);
        }
        _handSuppressed.Clear();
    }

    /// <summary>
    /// The chip THIS hand is physically in contact with, or null — the item counterpart of
    /// <c>CardsDriver.HandOwnedFanCard</c>. It is the hand-sweep winner when this very hand
    /// elected it, else the hand's own proximity-grab candidate (which is what its trigger would
    /// actually take).
    ///
    /// WHY the laser consults this (user report 2026-08-02, "what lights up is not what I get"):
    /// while a hand reaches INTO the fan its laser is usually on the fan too, and the laser path
    /// sets <c>Ray.UiHitOverride</c> — which makes <c>ProximityGrabber</c> defer the trigger to
    /// the laser. The pop the player sees then comes from the hand sweep while the grab comes
    /// from the beam, and on an arc of overlapping chips those are routinely different cards.
    /// <c>CardsDriver.UpdateItemFanLaser</c> therefore yields hover AND trigger to the hand
    /// whenever this is non-null and disagrees with the ray chip — the same single-owner
    /// contract the ability fan enforces in <c>UpdateFanHoverSplit</c>.
    /// </summary>
    /// <summary>
    /// Index of the chip currently singled out in this fan, or -1 — the ITEM-fan counterpart of
    /// <c>PileBrowser.HighlightedIndex</c> and <c>CardFan.HighlightedIndex</c>.
    ///
    /// <para>MULTIPLAYER (user report 2026-08-03: "Die Highlights der Karten vom Fächer werden
    /// nicht synchronisiert bei den Piles z.B. Gegenstände/Item-Fächer"). The wire carries a bare
    /// fan POSITION, never a card identity (extension record 6), and the sender used to read that
    /// position from the pile BROWSER only — so a player sweeping their item fan lifted a chip
    /// that no peer ever saw move. This is the missing source; at most one board fan is open at a
    /// time, so it feeds the very same wire field.</para>
    ///
    /// <para>LASER PARITY (user report 2026-08-07: "Beim Hovern mit dem Laser über eine
    /// Pile/Item-Karte wird das Highlight nicht synchronisiert; mit der Hand schon"). This used to
    /// return <c>_handWinnerIndex</c> and nothing else, i.e. the HAND SWEEP only. The laser hover
    /// path (<c>CardsDriver.UpdateItemFanLaser</c> → <c>ItemChip.OnPokeEnter</c>) sets a SEPARATE
    /// flag that <c>ItemChip.Tick</c> pops on but that nothing here could read — so a beam hover
    /// lifted a chip locally and reached no peer. It now scans for
    /// <see cref="ItemChip.IsHighlighted"/>, which covers BOTH pop flags — the exact shape
    /// <c>PileBrowser.HighlightedIndex</c> / <c>CardFan.HighlightedIndex</c> already use through
    /// <c>VRCard.IsHighlighted</c>, which is why those two synced on the laser from the start.
    /// NO WIRE CHANGE: this is still the same bare fan POSITION on extension record 6, and
    /// <c>NetAvatarDriver</c> still dedups it against the last value it sent.</para>
    ///
    /// <para>DEDUP / SINGLE OWNER: the hand sweep wins whenever it has a winner. That is the same
    /// arbitration <c>CardsDriver.UpdateItemFanLaser</c> already applies locally (it yields hover
    /// AND trigger to the hand whenever <see cref="HandOwnedChip"/> disagrees with the ray chip),
    /// so the transmitted index is by construction the ONE chip the owner sees popped — hand and
    /// laser can never contribute two different indices, and a hover held across a hand→laser
    /// handover emits no packet at all while the chip is unchanged.</para>
    /// </summary>
    internal int HighlightedIndex
    {
        get
        {
            if (!IsOpen)
                return -1;
            if (_handWinnerIndex >= 0 && _handWinnerIndex < _chips.Count)
                return _handWinnerIndex;
            for (int i = 0; i < _chips.Count; i++)
            {
                ItemChip c = _chips[i];
                if (c != null && c.IsHighlighted)
                    return i;
            }
            return -1;
        }
    }

    internal ItemChip? HandOwnedChip(VRHand? hand)
    {
        if (hand == null || !IsOpen)
            return null;
        if (_handWinner != null && ReferenceEquals(_handWinnerHand, hand))
            return _handWinner;
        return hand.Grabber.Highlighted as ItemChip;
    }

    // ------------------------------------------------------------------ laser pick --

    /// <summary>
    /// Geometric ray hit-test over the item fan — the item counterpart of
    /// <see cref="PileBrowser.TryRaycast"/>, the same algorithm verbatim (per-chip plane + rect off
    /// the LIVE transform, nearest hit along the ray, sticky-hover hysteresis so overlap never
    /// flips the highlight), with the rect sized to each chip's ACTUAL near-square face
    /// (<see cref="ItemChip.FaceWidth"/>/<see cref="ItemChip.FaceHeight"/> — item cards are not
    /// the tall ability rect). Driven by <c>CardsDriver.UpdateItemFanLaser</c>. No allocations.
    ///
    /// ROOT CAUSE this replaces (user: sweeping the laser RIGHT→LEFT popped only every SECOND
    /// card, while left→right stepped through every card): the chips used to be the ONLY fan
    /// cards whose laser hover came from the generic board-element PHYSICS scan
    /// (PlayTray.LaserTargets → nearest <c>Collider.Raycast</c> over the LIVE BoxColliders,
    /// re-elected from scratch every frame, no hysteresis). But the hover POP is part of that
    /// collider: the popped chip lifts 2 cm toward the viewer and grows ×1.18, so the pick
    /// geometry moved the instant the beam landed — the exact feedback class
    /// <see cref="VRCard.TryGetRestingLaserRect"/> documents for the ability fan ("the raise
    /// moved the plane INTO the beam"). At this fan's spacing (63.5 mm near-square faces at
    /// ×1.25 chip scale on a 10°-step arc ≈ 47.5 mm centers, 86.8 mm collider spans) the
    /// un-popped corridor between a popped chip's ENLARGED collider edge (±51.2 mm) and the
    /// next-but-one chip's collider (51.6 mm out) is under a MILLIMETRE — so any hand-off that
    /// had to clear the popped collider landed two chips over, and the immediate neighbour's
    /// hover lived a frame at best. Why only ONE direction: a lifted collider shadows the beam
    /// only on the side facing AWAY from the beam origin — with the laser in the (dominant,
    /// right) hand the 2 cm lift parallax-extends the popped chip's pick footprint over its
    /// LEFT neighbour's corridor, while hand-offs to the RIGHT happen on the beam-origin side
    /// where there is no shadow; the z-stagger tiebreak (chip i+1 sits 4 mm nearer the viewer
    /// than chip i) then hands the shared exit region to the far chip. Hence: right→left skips
    /// every second card, left→right does not. A per-frame geometric pick with the sticky rule
    /// has none of these behaviours in either direction: the pop can only ever EXTEND the
    /// incumbent's own hover (harmless — the beam is on that card anyway), never occlude a
    /// neighbour, because each rect is tested independently and the hand-off is decided by the
    /// nearest rect actually under the ray.
    /// </summary>
    internal bool TryLaserRaycast(Vector3 origin, Vector3 direction, ItemChip? sticky,
        out ItemChip? chip, out Vector3 point, out float distance,
        bool allowNearMiss = false)
    {
        chip = null;
        point = default;
        distance = float.PositiveInfinity;
        LastLaserPick = FanSweep.FanLaserPick.None;

        if (!IsOpen || _root == null)
            return false;

        // ANGULAR RESCUE bookkeeping (the "I cannot reliably laser-hover the chips" report). The
        // exact-rect test below is correct at the default board scale and a coin flip on a small
        // one: at board 0,32× a chip is 2,5 × 3,5 REAL cm, so at a ~50 cm aim distance it subtends
        // 1,4° × 2,0° half-angle — inside a hand-held controller's own aim jitter, while the same
        // fan on the 0,80× default board subtends 5,7° × 8,0° and never misses. Each chip is
        // therefore granted a minimum angular half-size (FanSweep.LaserMinHalfAngleDegrees) as a
        // SECOND pass: while the beam is genuinely on a chip nothing changes, and the fan OCCLUDER
        // (RayInteractor.ComputeFanOccluder) never passes allowNearMiss, so a rescued near-miss can
        // never begin hiding game UI behind the fan.
        ItemChip? rescue = null;
        Vector3 rescuePoint = default;
        float rescueDist = 0f, rescueOvershoot = 0f, rescuePad = 0f, rescueRatio = float.MaxValue;
        float rescueWidth = 0f;
        var miss = FanSweep.FanLaserPick.None;
        float missOvershoot = float.MaxValue;

        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            // Held chips ride the hand (never laser targets). A clipped (PendingUse) chip stays
            // pickable on purpose: the old collider path let the laser pull it back out of the
            // use slot, and that must keep working (its live transform sits at the slot pose).
            if (c == null || c.Holder != null || !c.gameObject.activeInHierarchy)
                continue;

            Transform t = c.transform;
            float denom = Vector3.Dot(direction, t.forward); // chips face the viewer with −Z
            // Numerical stability ONLY (see FanSweep.LaserMinFaceDenominator). The angular cone that
            // used to sit here rejected legitimate hits: a chip billboards to the HEAD while the
            // beam leaves the HAND, so an off-to-the-side controller meets the face steeply even
            // with the reticle dead centre on it. A crossing inside the finite face is a hit at any
            // incidence; the grazing-plane runaway is bounded below, in card widths, where it belongs.
            if (denom < FanSweep.LaserMinFaceDenominator)
                continue;
            float dist = Vector3.Dot(t.position - origin, t.forward) / denom;
            if (dist <= 0f)
                continue;

            Vector3 hit = origin + direction * dist;
            Vector3 local = t.InverseTransformPoint(hit); // scale-aware (ChipScale + pop grow)
            // Accept margin (FanSweep.LaserAcceptMargin), the same ~10 % the ability fan has always
            // had: the exact rect plus a zero angular pad at reading distance left no tolerance at
            // all for controller jitter or the chip's own hover pop.
            float halfW = c.FaceWidth * 0.5f;
            float halfH = c.FaceHeight * 0.5f;
            float overX = Mathf.Abs(local.x) - halfW * FanSweep.LaserAcceptMargin;
            float overY = Mathf.Abs(local.y) - halfH * FanSweep.LaserAcceptMargin;
            float lossy = Mathf.Max(t.lossyScale.x, 1e-5f);
            if (overX > 0f || overY > 0f)
            {
                if (!allowNearMiss)
                    continue;
                float overshoot = (Mathf.Max(overX, 0f) + Mathf.Max(overY, 0f)) * lossy;
                // A crossing this far outside the face was never aimed at this chip — that is the
                // grazing-plane runaway, bounded where it actually is (FanSweep doc).
                if (overshoot > Mathf.Max(halfW, halfH) * lossy * FanSweep.LaserMaxMissOvershootFactor)
                    continue;
                float pad = FanSweep.LaserPad(dist, Mathf.Max(halfW, halfH) * lossy);
                if (overshoot < missOvershoot)
                {
                    missOvershoot = overshoot;
                    miss = new FanSweep.FanLaserPick
                    {
                        Hit = false, Rescued = false, Name = c.name, Distance = dist,
                        Overshoot = overshoot, Pad = pad, FaceWidthWorld = halfW * 2f * lossy,
                    };
                }
                if (pad <= 0f || overshoot > pad)
                    continue;
                float ratio = overshoot / pad;
                if (ReferenceEquals(c, sticky))
                    ratio *= 0.5f; // the incumbent keeps the beam through a graze (same hysteresis as below)
                if (ratio >= rescueRatio)
                    continue;
                rescue = c;
                rescuePoint = hit;
                rescueDist = dist;
                rescueOvershoot = overshoot;
                rescuePad = pad;
                rescueRatio = ratio;
                rescueWidth = halfW * 2f * lossy;
                continue;
            }

            if (ReferenceEquals(c, sticky))
            {
                // Current hover still under the ray — it wins outright (overlap hysteresis).
                chip = c;
                point = hit;
                distance = dist;
                LastLaserPick = new FanSweep.FanLaserPick
                {
                    Hit = true, Rescued = false, Name = c.name, Distance = dist,
                    Overshoot = 0f, Pad = 0f, FaceWidthWorld = halfW * 2f * lossy,
                };
                return true;
            }

            if (dist >= distance)
                continue;
            chip = c;
            point = hit;
            distance = dist;
            LastLaserPick = new FanSweep.FanLaserPick
            {
                Hit = true, Rescued = false, Name = c.name, Distance = dist,
                Overshoot = 0f, Pad = 0f, FaceWidthWorld = halfW * 2f * lossy,
            };
        }

        if (chip == null && rescue != null)
        {
            chip = rescue;
            point = rescuePoint;
            distance = rescueDist;
            LastLaserPick = new FanSweep.FanLaserPick
            {
                Hit = true, Rescued = true, Name = rescue.name, Distance = rescueDist,
                Overshoot = rescueOvershoot, Pad = rescuePad, FaceWidthWorld = rescueWidth,
            };
        }
        else if (chip == null)
        {
            LastLaserPick = miss;
        }
        return chip != null;
    }

    /// <summary>
    /// What the last <see cref="TryLaserRaycast"/> decided — hit or miss, which chip, dead-on or
    /// rescued by the angular pad, and how far outside the face the ray crossed. Read by
    /// <c>CardsDriver.UpdateItemFanLaser</c> for the throttled laser diagnostic; the pick itself
    /// runs per frame per hand and must stay silent.
    /// </summary>
    internal FanSweep.FanLaserPick LastLaserPick { get; private set; } = FanSweep.FanLaserPick.None;

    // ------------------------------------------------------------------ use slot --

    /// <summary>
    /// The single held chip that can actually be USED right now (requirement 3): non-passive AND
    /// in a Useable/Selected slot state — the EXACT predicate <c>UseItemService.UseItem</c>
    /// enforces (evaluated LIVE via <see cref="ItemChip.IsActivatable"/>), so the slot never
    /// appears for an item the service would reject. Requirement C (activation split): items
    /// whose activation needs a further SUB-CHOICE (element consume/infuse "Any" —
    /// <see cref="CardsGameApi.ItemNeedsSubChoice"/>) are excluded — their activation lives
    /// EXCLUSIVELY on their docked bar symbol (the choice UI is there); plain items conversely
    /// activate exclusively by the place-into-slot flow (their symbols never dock).
    /// </summary>
    private ItemChip? HeldActivatableChip()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c != null && c.Holder != null && c.IsActivatable
                && !CardsGameApi.ItemNeedsSubChoice(c.Item!, _hand))
                return c;
        }
        return null;
    }

    /// <summary>
    /// USABLE-HIGHLIGHT truth source — can this chip's item be USED right now: it is the local
    /// character's own action turn AND the item's live state is activatable (non-passive +
    /// Useable/Selected — the exact gate <c>UseItemService.UseItem</c> enforces). Owner-driven
    /// (turn-aware), so OFF-turn nothing highlights and ON-turn exactly the playable items light up.
    /// Polled live from each chip's per-frame face maintenance; never cached — usability moves with
    /// turn/phase and with every item the player spends.
    ///
    /// WHY the highlight (not a dim) hangs off THIS predicate: the game logic stays untouched; the
    /// visual is a pure read of it. Flipping the cue from "grey out the unusable" to "light up the
    /// usable" is a change of which side of this bool draws a quad, nothing else.
    /// </summary>
    internal bool CanUseNow(ItemChip chip) =>
        chip != null && _hand != null && CardsGameApi.IsActionTurn(_hand) && chip.IsActivatable;

    /// <summary>
    /// The SINGLE live activatability predicate, shared by the chips (<see cref="ItemChip.IsActivatable"/>)
    /// and by the fan-CLOSED stack highlight (<see cref="UsableCount"/>): non-passive AND in a
    /// Useable/Selected slot state — byte-for-byte the gate <c>UseItemService.UseItem</c> enforces, so a
    /// highlight can never promise a use the service would reject. Read-only on game data.
    /// </summary>
    private static bool IsItemActivatable(CItem? item) =>
        item != null && item.YMLData != null
        && item.YMLData.Trigger != CItem.EItemTrigger.PassiveEffect
        && (item.SlotState == CItem.EItemSlotState.Useable
            || item.SlotState == CItem.EItemSlotState.Selected);

    /// <summary>
    /// How many of the acting character's equipped items are usable RIGHT NOW (0 when it is not this
    /// hand's action turn). Read straight from the live inventory rather than from the chips, because
    /// the items STACK highlight must work while the fan is CLOSED and no chips exist at all. Cheap:
    /// a turn check plus one pass over a handful of items, per frame, allocation-free.
    /// </summary>
    internal int UsableCount(CardsHandUI? hand)
    {
        if (hand == null || !CardsGameApi.IsActionTurn(hand))
            return 0;
        List<CItem>? items = ItemsOf(hand);
        if (items == null)
            return 0;
        int n = 0;
        for (int i = 0; i < items.Count; i++)
            if (IsItemActivatable(items[i]))
                n++;
        return n;
    }

    /// <summary>
    /// Requirement 8 — show a translucent GHOST duplicate at the use-slot pose while a HELD usable item
    /// card comes NEAR the slot, previewing where it will land if released (mirror of the ability cards'
    /// drop telegraph, which glows the destination slot). Built lazily under the slot so it rides the
    /// slot pose + visibility; toggled purely by proximity. No-op / hidden when nothing is held near it.
    /// </summary>
    private void TickUseGhost(bool showUseSlot, ItemChip? heldUsable)
    {
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        bool show = false;
        if (showUseSlot && heldUsable != null && slot != null && slot.gameObject.activeSelf)
        {
            float scale = slot.lossyScale.x;
            float near = UseSlotRadius * 2.2f * (scale > 1e-4f ? scale : 1f); // generous "approaching" band
            if ((heldUsable.transform.position - slot.position).sqrMagnitude <= near * near)
                show = true;
        }
        if (show)
        {
            if (_useGhost == null && slot != null)
                _useGhost = BuildUseGhost(slot);
            if (_useGhost != null && !_useGhost.activeSelf)
                _useGhost.SetActive(true);
        }
        else if (_useGhost != null && _useGhost.activeSelf)
        {
            _useGhost.SetActive(false);
        }
    }

    /// <summary>Requirement 8 — the translucent gold card-shaped ghost, parented at the use-slot pose.</summary>
    private static GameObject BuildUseGhost(Transform slot)
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "GloomhavenVR.ItemUseGhost";
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(slot, worldPositionStays: false);
        quad.transform.localPosition = new Vector3(0f, 0f, -0.004f); // viewer side, proud of the slot face
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(w, h, 1f);
        var mr = quad.GetComponent<MeshRenderer>();
        Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (sh != null)
            mr.sharedMaterial = new Material(sh) { color = new Color(0.92f, 0.85f, 0.5f, 0.34f) };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Core.VRLayers.Apply(quad); // mod-owned ghost on the mod layer (double-sided Sprites/Default)
        return quad;
    }

    /// <summary>
    /// Requirement 6 (clip-in + decide): a just-released usable chip dropped onto/near the board's
    /// item-use slot does NOT use immediately — it CLIPS INTO the slot and waits. A poke/laser on the
    /// Confirm button uses it (<see cref="ConfirmPendingUse"/>); grabbing the card BACK OUT and
    /// releasing it returns it to the deck, no use. Dropped anywhere else (or a non-activatable chip)
    /// is a no-op — the base glide-home already runs. Inventory is never mutated here.
    /// </summary>
    internal void OnChipReleased(ItemChip chip, Vector3 dropWorldPos, VRHand vrHand)
    {
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        if (chip == null || slot == null || !slot.gameObject.activeSelf)
            return;

        // ITEM SURRENDER pick (event consume/refresh mali): while the game's ItemCardPicker is
        // open, a drop into the slot SELECTS the item through the picker's own slot seam —
        // never UseItemService (nothing is consumed until the demand confirm commits). The
        // IsActivatable gate below is the ACTION-TURN use gate and deliberately does not apply:
        // the picker's own candidate filter (cardSlots) is the authority here.
        if (_demandActive)
        {
            HandleDemandDrop(chip, dropWorldPos, slot, vrHand);
            return;
        }

        // TAKE-DAMAGE shield place (req C): while the damage decision presents OnAttacked
        // candidates on the (hidden) items bar, a drop TOGGLES the shield item through the
        // panel's own slot seam — never UseItemService (the panel's confirm commits + syncs).
        if (_tdActive)
        {
            HandleTakeDamageDrop(chip, dropWorldPos, slot, vrHand);
            return;
        }

        if (!chip.IsActivatable)
            return;
        // Requirement C (activation split): a CHOICE item (element sub-pick) never clips into
        // the use slot — its bar symbol carries the choice UI. Mirrors HeldActivatableChip, so
        // the slot was never shown for this chip anyway; this is the belt-and-braces on the
        // drop itself.
        if (CardsGameApi.ItemNeedsSubChoice(chip.Item!, _hand))
        {
            VRLog.Info("Cards", $"ITEM place: '{chip.name}' needs an element sub-choice — its docked bar " +
                                "symbol carries that choice; the card returns to the fan.");
            return;
        }

        // Proximity test in world space (parenting-independent): the capture radius scales with
        // the board so the slot stays the same on-screen size at any board scale.
        float scale = slot.lossyScale.x;
        float radius = UseSlotRadius * (scale > 1e-4f ? scale : 1f);
        if ((dropWorldPos - slot.position).sqrMagnitude > radius * radius)
            return; // dropped away from the slot — the base glide-home returns it to the fan

        // Only one pending decision at a time (a fresh drop replaces an older pending clip cleanly).
        if (_pendingUseChip != null && !ReferenceEquals(_pendingUseChip, chip))
            _pendingUseChip.ReturnToFan();

        _pendingUseChip = chip;
        chip.PendingUse = true;
        chip.CancelReleaseGlide(); // do NOT glide home — we clip into the slot instead
        // Bolt it to the slot (see ClipIntoSlot): rigid by hierarchy, not chased by a lerp — the
        // fan root billboards to the head, the slot does not, and chasing across that boundary is
        // what made the clipped card swim behind head movement.
        if (_root != null)
            chip.ClipIntoSlot(slot, _root, ChipScale);
        PlayTray.Current?.SetItemUseSlotVisible(true);
        PlayTray.Current?.SetItemUseConfirmVisible(true, ConfirmPendingUse);
        vrHand.SendHaptic(HapticPreset.HoverTick);
        // ITEM 4 (card sounds): the place "thunk" the ability cards play when a card drops into a slot.
        CardsDriver.PlayCardSound(CardsConfig.CardPlaceSound.Value, chip.transform);
        VRLog.Info("Cards", $"ITEM clip-in: '{chip.name}' held in the use slot ({vrHand.Side}) — " +
                            "poke USE to confirm, or grab it back out to cancel.");
    }

    /// <summary>
    /// Requirement 6 — per-tick pending-decision service: resolve a CANCEL (the card was grabbed back
    /// out of the slot), an invalidation (turn ended / item no longer usable), or else keep the card
    /// glued to the slot pose (root-local so it rides the board billboard) with the Confirm button up.
    /// </summary>
    private void TickPendingUse(CardsHandUI hand)
    {
        ItemChip? chip = _pendingUseChip;
        if (chip == null)
            return;

        // Cancel by grabbing it BACK OUT (#6 refinement): once held again, clear the pending state; the
        // chip's own OnRelease then returns it to the fan (or re-clips if dropped back on the slot).
        if (chip.Holder != null)
        {
            CancelPendingUse("grabbed back out of the slot");
            return;
        }
        // Invalidated: no longer this hand's action turn, or the item is no longer usable.
        if (!CardsGameApi.IsActionTurn(hand) || !chip.IsActivatable)
        {
            UnclipChip(chip);   // back into the fan's frame BEFORE the fan-local glide starts
            chip.ReturnToFan(); // glide back to the fan (not held)
            CancelPendingUse("no longer usable");
            return;
        }

        PlayTray.Current?.SetItemUseSlotVisible(true);
        // No per-frame pose work: the chip is a CHILD of the slot while clipped (ClipIntoSlot), so
        // the hierarchy holds it exactly, whatever the head and the board do. Re-assert the parent
        // only if something else stole it (a board rebuild re-creating the slot transform).
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        if (slot != null && _root != null && chip.transform.parent != slot)
            chip.ClipIntoSlot(slot, _root, ChipScale);
    }

    /// <summary>
    /// Take a clipped chip back out of the use-slot hierarchy and into the fan root, world pose
    /// preserved (see <see cref="ItemChip.ClipIntoSlot"/> for why it was parented to the slot at all).
    /// EVERY exit from the pending state routes through here — cancel, invalidation and the grab that
    /// pulls the card back out — so the chip's fan-local home pose and glide are always evaluated in
    /// the frame they were written for. No-op when the chip is not (or no longer) under the slot.
    /// </summary>
    internal void UnclipChip(ItemChip chip) => chip?.UnclipFromSlot(_root);

    /// <summary>Re-run the arc layout (poses, split, collider strips) without rebuilding content —
    /// used when a chip rejoins the arc after a grab, so its full-size grab box is stripped back
    /// down and the per-chip regions tile again.</summary>
    internal void RefreshFanLayout()
    {
        if (IsOpen)
            Relayout();
    }

    /// <summary>Requirement 6 — drop the pending state + hide the Confirm button. Clears the chip's own
    /// PendingUse flag so a chip GRABBED back out glides home on release (instead of re-clipping); the
    /// invalidation path already called <see cref="ItemChip.ReturnToFan"/> to start that glide.</summary>
    private void CancelPendingUse(string why)
    {
        ItemChip? chip = _pendingUseChip;
        _pendingUseChip = null;
        if (chip != null)
        {
            UnclipChip(chip); // no-op when a grab already took it out of the slot hierarchy
            chip.PendingUse = false;
        }
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        PlayTray.Current?.SetItemUseSlotVisible(false);
        _useSlotShownLogged = false;
        VRLog.Info("Cards", $"ITEM clip-in CANCEL ({why}) — card returns to the deck, NOT used.");
    }

    /// <summary>
    /// Requirement 6 — CONFIRM: use the pending item through the game's own <c>UseItemService</c>
    /// (which owns ALL multiplayer sync + re-validates), then reflect the result with an animation ON
    /// the clipped card (burn plume for Consumed, a "tap" roll for Spent) before it collapses back into
    /// the deck. Invoked by the Confirm button's poke/laser callback. Inventory is never mutated here.
    /// </summary>
    private void ConfirmPendingUse()
    {
        ItemChip? chip = _pendingUseChip;
        _pendingUseChip = null;
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        if (chip == null)
            return;

        CPlayerActor? actor = _hand != null ? _hand.PlayerActor : null;
        if (actor == null || chip.Item == null)
        {
            chip.ReturnToFan();
            return;
        }

        CItem item = chip.Item;
        string itemName = chip.name;
        try
        {
            // Requirement C: prefer the game's OWN items-bar slot click when a live slot exists
            // (ShowUsableItems keeps the hidden 2D bar populated during the turn) — byte-identical
            // to the 2D click AND to the game's own MP replay seam (ProxyUseItemBonus →
            // slot.OnPointerDown, UIUseItemsBar.cs:618). Unlike the direct service call it also
            // auto-resolves FIXED-element consumes (MultiElementPickController.Pick) before the
            // wired UseItemService runs. Only PLAIN slots reach here (the sub-choice gate is on
            // the drop), so the click can never open a picker. Fallback: the direct service call
            // (owns the online GameAction send + local execution + re-validation), as before.
            UIUseItemScenario? slot = CardsGameApi.LiveItemsBarSlot(item);
            bool viaSlot = slot != null && !CardsGameApi.SlotNeedsSubChoice(slot)
                           && CardsGameApi.ClickItemsBarSlot(slot);
            if (!viaSlot)
                new UseItemService(actor).UseItem(item);
            VRLog.Info("Cards", $"ITEM USE seam: {(viaSlot ? "items-bar slot click (game's own 2D/proxy seam)" : "UseItemService direct (no live bar slot)")} for '{itemName}'.");
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Cards", $"ITEM USE failed for '{itemName}': {e.Message}");
            chip.ReturnToFan();
            return;
        }

        // Reflect the RESULT with an FX on the clipped card, then it collapses back into the deck.
        // Detach it from the fan root + drop it from the live list so the flourish/collapse runs to
        // completion even as the fan live-rebuilds to show the item's new (Spent/Consumed) state.
        // Requirement 9b FIX: read the intended flourish from the item's STATIC usage TYPE
        // (YMLData.Usage), NOT the live SlotState — online, UseItemService.UseItem sends the state
        // change as a GameAction that resolves a frame or more LATER, so SlotState is still
        // Useable/Selected the instant we return here and the old "spent = SlotState==Spent" read was
        // false → no tap animation ever played. The usage type is authored config, stable pre/post use.
        CItem.EUsageType usage = item.YMLData != null ? item.YMLData.Usage : CItem.EUsageType.None;
        bool consumed = usage == CItem.EUsageType.Consumed
                        || item.SlotState == CItem.EItemSlotState.Consumed;
        bool spent = !consumed
                     && (usage == CItem.EUsageType.Spent
                         || item.SlotState == CItem.EItemSlotState.Spent);
        Vector3 converge = PileConvergeWorld();
        Transform? keep = PlayTray.Current?.Root != null ? PlayTray.Current!.Root : _anchor;
        if (keep != null)
            chip.transform.SetParent(keep, worldPositionStays: true);
        _chips.Remove(chip);
        if (ReferenceEquals(_handWinner, chip))
            _handWinner = null;
        chip.PlayUseThenCollapse(consumed, spent, converge);

        PlayTray.Current?.SetItemUseSlotVisible(false);
        _useSlotShownLogged = false;
        VRLog.Info("Cards", $"ITEM USED {itemName} (CONFIRM; state now {item.SlotState}) — playing " +
                            $"{(consumed ? "burn" : spent ? "tap" : "use")} FX, then it returns to the deck.");
    }

    // ------------------------------------------------- item-surrender pick (event mali) --

    // The game's open ItemCardPicker being served (event consume/refresh demand, OR the
    // goal-chest "lose 1 item reward" forfeit — flow 1: the SAME window, discriminated by the
    // Choreographer wait state, see CardsGameApi.OpenLoseRewardPicker). The chip clipped into
    // the use slot mirrors the picker's selection; the slot's confirm button ("ITEM ABGEBEN"/
    // "ITEM AUFFRISCHEN"/"BELOHNUNG ABGEBEN", never "USE") commits through the owning picker's
    // own confirm seam. All game selection state lives in the PICKER — the mod only mirrors it.
    private bool _demandActive;
    private bool _demandRefreshing;
    private bool _demandLoseReward;  // flow 1: the fan shows REWARD items, commit = ItemRewardLosePicker
    private ItemChip? _demandChip;   // chip clipped into the slot for the current selection
    private float _demandCueAt;      // throttle for the "fan is closed, open the stack" cue line

    /// <summary>
    /// The picker the ACTIVE demand mirrors — the refresh/consume picker normally, the
    /// goal-chest forfeit picker while flow 1 owns the shared window. Every selection seam
    /// (drop-select, grab-back deselect, ready check) resolves through here so the two flows
    /// can never cross-talk on the one scene-serialized ItemCardPicker.
    /// </summary>
    private ItemCardPicker? ActiveDemandPicker() =>
        _demandLoseReward
            ? CardsGameApi.OpenLoseRewardPicker(out _)
            : CardsGameApi.OpenItemPicker(out _, out _);

    /// <summary>
    /// Flow 1 content override: while the goal-chest forfeit demand is live the fan presents
    /// the picker's REWARD items (never in any inventory — <see cref="CardsGameApi.LoseRewardItems"/>);
    /// null otherwise, and <see cref="Populate"/>/<see cref="Signature"/> fall back to the
    /// inventory as always.
    /// </summary>
    private List<CItem>? DemandItemsOverride() =>
        _demandActive && _demandLoseReward ? CardsGameApi.LoseRewardItems() : null;

    /// <summary>True while an item consume/refresh demand is being served (gates the normal
    /// action-turn use-slot logic in <see cref="Tick"/>).</summary>
    internal bool DemandActive => _demandActive;

    /// <summary>
    /// EVENT ITEM-SURRENDER pump, one call per frame from the driver (independent of the
    /// action-turn item flow): while the game's <c>ItemCardRefreshPicker</c> demands items
    /// from a locally-controlled actor (see <see cref="CardsGameApi.OpenItemPicker"/> — the
    /// flat picker window is invisible on VR's hidden 2D stack, the item twin of the
    /// card-discard deadlock), raise the ITEM FAN board-anchored, keep the board's item-use
    /// slot visible as the drop target, glue the selected chip to the slot, and surface the
    /// demand confirm. Cleans up the moment the picker closes (confirmed / game moved on).
    /// </summary>
    internal void TickDemandPick(CardsHandUI? hand)
    {
        ItemCardPicker? picker = CardsGameApi.OpenItemPicker(out CPlayerActor? actor, out bool refreshing);
        bool loseReward = false;
        if (picker == null)
        {
            // Flow 1 (goal-chest "lose 1 item reward"): the SAME window, owned by
            // ItemRewardLosePicker while the Choreographer waits in
            // WaitingForLoseGoalChestRewardSelection. Only the DECIDING client (host/offline
            // — the game's own CanSelect gate) raises the fan; guests get the wait banner
            // from UpdateItemDemandStatus and the host's commit replays via the game's own
            // ProxyItemRewardLose. No owning actor: any presented local hand anchors the fan.
            picker = CardsGameApi.OpenLoseRewardPicker(out bool canSelect);
            loseReward = picker != null;
            if (!canSelect)
                picker = null;
        }
        bool active = picker != null && hand != null
                      && (loseReward // forfeit: party-level pick, no actor to match
                          || (actor != null && ReferenceEquals(hand.PlayerActor, actor)
                              && (!FFSNetwork.IsOnline || actor.IsUnderMyControl)));
        if (!active)
        {
            if (_demandActive)
                EndDemand("picker closed — selection committed or the game moved on");
            return;
        }

        if (!_demandActive || _demandLoseReward != loseReward)
        {
            if (_demandActive) // owner flipped refresh↔forfeit without a closed frame — restart clean
                EndDemand("demand owner changed (refresh/consume ↔ goal-chest forfeit)");
            _demandActive = true;
            _demandRefreshing = !loseReward && refreshing;
            _demandLoseReward = loseReward;
            _demandCueAt = 0f;
            _signature = string.Empty; // content source may have flipped (inventory ↔ reward items)
            if (loseReward)
                VRLog.Info("Cards", "GOAL-CHEST FORFEIT pick OPEN: the game demands " +
                                    $"{CardsGameApi.ItemPickWanted(picker!)} earned reward item(s) back " +
                                    "(ItemRewardLosePicker → the same flat ItemCardPicker window, dead in VR; " +
                                    "Choreographer in WaitingForLoseGoalChestRewardSelection) — item fan raised " +
                                    "with the REWARD items, drop one into the board's item slot; the slot button " +
                                    "commits through ItemRewardLosePicker.ConfirmSelectedRewardItems.");
            else
                VRLog.Info("Cards", $"ITEM SURRENDER pick OPEN ({(refreshing ? "refresh" : "consume")}): the game demands " +
                                    $"{CardsGameApi.ItemPickWanted(picker!)} item(s) from " +
                                    $"'{(actor != null ? CardsGameApi.ActorLabel(actor) : "?")}' (ItemCardRefreshPicker; flat window is dead in " +
                                    "VR) — item fan raised, drop the demanded item into the board's item slot; the slot " +
                                    "button commits through the game's own picker confirm.");
        }

        // NO AUTO-OPEN (user report 2026-08-07). The demand used to raise the fan itself and
        // re-raise it every 0.5 s while it was live, which made the fan unclosable. The demand is
        // now advertised, not forced: the use slot below is the ask, the demand banner names it,
        // and the items STACK (whose usable-highlight embers are already running) is the one place
        // that opens the fan. Throttled cue line so the next hardware log proves which state the
        // fan was in during a demand.
        if (!IsOpen && Time.unscaledTime >= _demandCueAt)
        {
            _demandCueAt = Time.unscaledTime + 5f;
            VRLog.Info("Cards", "ITEM DEMAND waiting with the item fan CLOSED — poke (or laser-click) the " +
                                $"items stack to fan the candidates out (last close: {_lastCloseReason}). " +
                                "The mod never opens the fan by itself.");
        }

        // The use slot IS the ask — visible for the whole demand (Tick's action-turn gate is
        // bypassed while _demandActive, see there).
        PlayTray.Current?.SetItemUseSlotVisible(true);

        // Clipped-chip service: a grab-back DESELECTS through the picker's own slot seam; the
        // chip's own release then glides it home (or re-clips on a re-drop).
        ItemChip? chip = _demandChip;
        if (chip != null && chip.Item != null && chip.Holder != null)
        {
            CardsGameApi.ItemPickDeselect(picker!, chip.Item);
            UnclipChip(chip);
            chip.PendingUse = false;
            _demandChip = null;
            VRLog.Info("Cards", "ITEM SURRENDER: card grabbed back out of the slot — deselected through the " +
                                "game's ItemCardPickerSlot seam; drop an item again to choose.");
            chip = null;
        }
        if (chip != null && _root != null)
        {
            Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
            if (slot != null && chip.transform.parent != slot)
                chip.ClipIntoSlot(slot, _root, ChipScale); // re-assert after a board rebuild
        }

        // Demand confirm: shown exactly while the PICKER reports the full selection (survives a
        // lost chip — e.g. the fan was closed and rebuilt — because the game selection is the
        // authority). Label says SURRENDER/FORFEIT, never "USE".
        bool ready = CardsGameApi.ItemPickReady(picker!);
        PlayTray.Current?.SetItemUseConfirmVisible(ready, ready ? ConfirmDemandPick : null,
            DemandConfirmLabel());
    }

    /// <summary>The demand confirm-cap wording per flow: refresh (positive pick), forfeit
    /// (flow 1 — the player GIVES UP an earned reward), or the consume surrender.</summary>
    private string DemandConfirmLabel() =>
        _demandLoseReward ? Core.Loc.Mod("item_lose_reward")
        : _demandRefreshing ? Core.Loc.Mod("item_refresh_confirm")
        : Core.Loc.Mod("item_surrender");

    /// <summary>Drop routing while a demand is active (see <see cref="OnChipReleased"/>).</summary>
    private void HandleDemandDrop(ItemChip chip, Vector3 dropWorldPos, Transform slot, VRHand vrHand)
    {
        float scale = slot.lossyScale.x;
        float radius = UseSlotRadius * (scale > 1e-4f ? scale : 1f);
        if ((dropWorldPos - slot.position).sqrMagnitude > radius * radius)
            return; // dropped away from the slot — the base glide-home returns it to the fan

        ItemCardPicker? picker = ActiveDemandPicker();
        if (picker == null || chip.Item == null)
            return;
        if (!CardsGameApi.IsItemPickCandidate(picker, chip.Item))
        {
            VRLog.Info("Cards", $"ITEM SURRENDER: '{chip.name}' is NOT among the demanded candidates " +
                                "(the picker's own filter) — it returns to the fan.");
            return; // base glide-home
        }

        // A previous clipped selection returns to the fan; the select seam below swaps the
        // game selection (ItemPickSelect deselects the oldest when full — the picker's own
        // overflow rule).
        if (_demandChip != null && !ReferenceEquals(_demandChip, chip))
        {
            ItemChip old = _demandChip;
            _demandChip = null;
            if (old.Item != null)
                CardsGameApi.ItemPickDeselect(picker, old.Item);
            UnclipChip(old);
            old.PendingUse = false;
            old.ReturnToFan();
        }

        if (!CardsGameApi.ItemPickSelect(picker, chip.Item))
        {
            VRLog.Warn("Cards", $"ITEM SURRENDER: game picker REJECTED selecting '{chip.name}' " +
                                "(slot not selectable) — card returns to the fan.");
            return; // base glide-home
        }

        _demandChip = chip;
        chip.PendingUse = true;
        chip.CancelReleaseGlide();
        if (_root != null)
            chip.ClipIntoSlot(slot, _root, ChipScale);
        vrHand.SendHaptic(HapticPreset.HoverTick);
        CardsDriver.PlayCardSound(CardsConfig.CardPlaceSound.Value, chip.transform);
        VRLog.Info("Cards", $"ITEM SURRENDER clip-in: '{chip.name}' SELECTED through the game's " +
                            "ItemCardPickerSlot seam — press the slot button " +
                            $"('{DemandConfirmLabel()}') " +
                            "to commit, or grab it back to swap.");
    }

    /// <summary>
    /// Demand CONFIRM — commit through the game's own picker confirm
    /// (<see cref="CardsGameApi.ConfirmItemPick"/>: Inventory.UseItem/ReactivateItem +
    /// GameActionType.ConsumeItem/RefreshItem with the game's ItemsToken, picker hidden,
    /// Choreographer released). On success the clipped chip plays the same result FX as a
    /// normal use (burn plume for a consumed item, tap for spent/refreshed) and collapses
    /// back into the deck — the surrender is VISIBLE, not a silent vanish.
    /// </summary>
    private void ConfirmDemandPick()
    {
        ItemChip? chip = _demandChip;
        _demandChip = null;
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        // Flow 1 commits through ITS owning picker (ItemRewardLosePicker.ConfirmSelectedRewardItems
        // — RewardGroup removal + GameActionType.LoseItemReward + StepComplete); the refresh/
        // consume demand through ItemCardRefreshPicker.ConfirmSelectedCards as before.
        bool fired = _demandLoseReward
            ? CardsGameApi.ConfirmLoseRewardPick()
            : CardsGameApi.ConfirmItemPick();
        VRLog.Info("Cards", (_demandLoseReward ? "GOAL-CHEST FORFEIT CONFIRM → " : "ITEM SURRENDER CONFIRM → ") +
                            "game picker confirm " +
                            (fired ? (_demandLoseReward
                                         ? "accepted (Reward removed from every RewardGroup + GameActionType." +
                                           "LoseItemReward IndexToken — outcome networked by the game, Choreographer released)."
                                         : "accepted (Inventory.UseItem/ReactivateItem + GameActionType.ConsumeItem/" +
                                           "RefreshItem via the game's ItemsToken — outcome networked by the game).")
                                   : "rejected (selection incomplete or picker already closed)."));
        if (chip == null)
            return;
        if (!fired || chip.Item == null)
        {
            UnclipChip(chip);
            chip.PendingUse = false;
            chip.ReturnToFan();
            return;
        }

        // Result FX, mirrored from ConfirmPendingUse: consumed → burn out of the slot;
        // spent/refreshed → tap. Usage type is authored config (stable pre/post the
        // networked state change — the same 9b lesson as the use flow). A FORFEITED reward
        // item always burns: it leaves the party for good, whatever its usage type says.
        CItem item = chip.Item;
        CItem.EUsageType usage = item.YMLData != null ? item.YMLData.Usage : CItem.EUsageType.None;
        bool consumed = _demandLoseReward
                        || (!_demandRefreshing
                            && (usage == CItem.EUsageType.Consumed
                                || item.SlotState == CItem.EItemSlotState.Consumed));
        bool spent = !consumed;
        Vector3 converge = PileConvergeWorld();
        Transform? keep = PlayTray.Current?.Root != null ? PlayTray.Current!.Root : _anchor;
        if (keep != null)
            chip.transform.SetParent(keep, worldPositionStays: true);
        _chips.Remove(chip);
        if (ReferenceEquals(_handWinner, chip))
            _handWinner = null;
        chip.PendingUse = false;
        chip.PlayUseThenCollapse(consumed, spent, converge);
        PlayTray.Current?.SetItemUseSlotVisible(false);
        _useSlotShownLogged = false;
    }

    /// <summary>Demand teardown (picker closed): unclip/return any leftover chip, drop the
    /// confirm + slot, restore the normal action-turn gates. The fan stays as-is — the live
    /// rebuild shows the item's new state; the player closes it like any browse.</summary>
    private void EndDemand(string why)
    {
        _demandActive = false;
        bool wasLoseReward = _demandLoseReward;
        _demandLoseReward = false;
        if (wasLoseReward)
            _signature = string.Empty; // fan content flips back from REWARD items to the inventory
        ItemChip? chip = _demandChip;
        _demandChip = null;
        if (chip != null)
        {
            UnclipChip(chip);
            chip.PendingUse = false;
            if (chip.Holder == null && chip.gameObject != null && chip.gameObject.activeInHierarchy)
                chip.ReturnToFan();
        }
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        PlayTray.Current?.SetItemUseSlotVisible(false);
        _useSlotShownLogged = false;
        VRLog.Info("Cards", $"ITEM SURRENDER pick END ({why}).");
    }

    // ------------------------------------------------- take-damage shield place (req C) --

    // TAKE-DAMAGE decision context: while TakeDamagePanel presents the attacked actor's
    // OnAttacked shield/retaliate items on the (hidden) UIUseItemsBar
    // (TakeDamagePanel.Show → ShowItems(actorBeingAttacked, OnAttacked-filter,
    // ToggleShieldItem, clear:true), TakeDamagePanel.cs:249-264), placing the item CARD into
    // the board's use slot toggles the item through the game's own slot seam — the exact 2D
    // click (slot.OnPointerDown → Toggle → onSelect/onUnselect → TakeDamagePanel.
    // ToggleShieldItem → Inventory.SelectItem/DeselectItem). Grabbing the card back OUT
    // toggles it off the same way. There is NO extra board confirm: the panel's own
    // TakeDamage button (docked by DecisionDockSurface) commits — that confirm carries the
    // whole selection online in one GameActionType.TakeDamage ItemsToken
    // (TakeDamagePanel.cs:770-775; per-click sync is deliberately absent in the 2D flow too,
    // the ShowItems wrapper skips the send during TakeDamageConfirmation). Zero wire changes.
    private bool _tdActive;
    private ItemChip? _tdChip;      // chip clipped into the slot = the toggled shield item
    private float _tdCueAt;         // throttle for the "fan is closed, open the stack" cue line

    /// <summary>
    /// TAKE-DAMAGE place pump, one call per frame from the driver (sibling of
    /// <see cref="TickDemandPick"/>): while an open, locally-decided take-damage decision
    /// presents OnAttacked items for the PRESENTED hand's actor
    /// (<see cref="CardsGameApi.TakeDamagePlaceContext"/> — the presented hand itself follows
    /// the attacked actor via CardsGameApi.TakeDamageHand), gate the use slot on a held
    /// candidate and service the clipped chip (grab-back = toggle off through the panel's own
    /// slot seam). Ends the moment the panel closes — the game's confirm already committed (or
    /// discarded) the selection.
    ///
    /// NO AUTO-OPEN (user report 2026-08-07: "als der Charakter Schaden bekam ging der
    /// Gegenstands-Fächer von selbst auf und liess sich nie wieder schliessen"). This pump used
    /// to <c>Open</c> the fan on the first frame of the decision and RE-open it every 0.5 s for
    /// as long as the panel stayed up — so the stack poke, the click-away and every foreign-
    /// interaction close were all undone within half a second, and the hardware log shows the
    /// exact churn (CLOSE → CLOSE → "OPEN … opened by auto/flow" triplets, e.g. LogOutput.log
    /// 15732-15734 / 15906-15908 / 20748-20750, plus ~15 cycles on the peer). Placing a shield
    /// item is OPTIONAL, so nothing is lost by requiring the deliberate stack poke; the item
    /// stack's usable-highlight embers already advertise that there is something to play.
    /// </summary>
    internal void TickTakeDamagePick(CardsHandUI? hand)
    {
        bool active = hand != null && !_demandActive && CardsGameApi.TakeDamagePlaceContext(hand);
        if (!active)
        {
            if (_tdActive)
                EndTakeDamagePick("panel closed / context lost — the panel confirm owns the committed selection");
            return;
        }

        if (!_tdActive)
        {
            _tdActive = true;
            _tdCueAt = 0f;
            VRLog.Info("Cards", "TAKE-DAMAGE item place ARMED: the damage decision presents OnAttacked " +
                                "shield/retaliate items (TakeDamagePanel → hidden UIUseItemsBar; plain " +
                                "symbols are filtered from the docked bar). The item fan is NOT raised " +
                                $"automatically (fan currently {(IsOpen ? "OPEN" : "closed")}) — poke the items " +
                                "stack, then place a shield card into the board's item slot to toggle it; grab " +
                                "it back out to untoggle; the panel's own TakeDamage confirm commits.");
        }

        // NO AUTO-OPEN — see the method doc. One throttled cue instead, so a hardware log still
        // proves whether the fan was available during the decision.
        if (!IsOpen && Time.unscaledTime >= _tdCueAt)
        {
            _tdCueAt = Time.unscaledTime + 5f;
            VRLog.Info("Cards", "TAKE-DAMAGE item place: shield candidates exist but the item fan is CLOSED " +
                                $"(last close: {_lastCloseReason}) — poke the items stack to fan them out. " +
                                "Placing a shield is optional; the mod never opens the fan by itself.");
        }

        // Use slot: visible while a candidate chip is HELD or one is clipped (the toggle).
        ItemChip? held = HeldTakeDamageCandidate();
        bool show = held != null || _tdChip != null;
        PlayTray.Current?.SetItemUseSlotVisible(show);
        TickUseGhost(show && _tdChip == null, held);

        // Clipped-chip service.
        ItemChip? chip = _tdChip;
        if (chip == null)
            return;
        if (chip.Item == null)
        {
            _tdChip = null;
            return;
        }
        if (chip.Holder != null)
        {
            // Grabbed back OUT = toggle the shield item OFF through the same slot seam.
            UIUseItemScenario? slot = CardsGameApi.LiveItemsBarSlot(chip.Item);
            if (slot != null && chip.Item.SlotState == CItem.EItemSlotState.Selected)
                CardsGameApi.ClickItemsBarSlot(slot);
            UnclipChip(chip);
            chip.PendingUse = false;
            _tdChip = null;
            VRLog.Info("Cards", "TAKE-DAMAGE item place: card grabbed back out of the slot — shield item " +
                                "untoggled through the panel's own slot seam.");
            return;
        }
        // The game moved the selection out from under the clip (e.g. DeselectAllShieldItems on
        // a lethal recalc): return the card to the fan so card and state never disagree.
        if (chip.Item.SlotState != CItem.EItemSlotState.Selected)
        {
            UnclipChip(chip);
            chip.PendingUse = false;
            chip.ReturnToFan();
            _tdChip = null;
            VRLog.Info("Cards", "TAKE-DAMAGE item place: the game deselected the shield item — card returns to the fan.");
            return;
        }
        // Re-assert the clip after a board rebuild recreated the slot transform.
        Transform? useSlot = PlayTray.Current?.ItemUseSlotTransform;
        if (useSlot != null && _root != null && chip.transform.parent != useSlot)
            chip.ClipIntoSlot(useSlot, _root, ChipScale);
    }

    /// <summary>The single HELD chip that is a live take-damage candidate — its item has a
    /// visible slot on the panel-populated items bar (the game's own OnAttacked filter +
    /// CanConsume gate decided candidacy; the mod adds nothing).</summary>
    private ItemChip? HeldTakeDamageCandidate()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c != null && c.Holder != null && c.Item != null
                && CardsGameApi.LiveItemsBarSlot(c.Item) != null)
                return c;
        }
        return null;
    }

    /// <summary>Drop routing while the take-damage context is active (see <see cref="OnChipReleased"/>):
    /// clip + TOGGLE ON through the panel's own slot seam; a second card swaps (the previous one is
    /// untoggled and returns to the fan — one clipped card mirrors one toggled item, so grab-back
    /// stays an exact inverse). A non-candidate drop glides home untouched.</summary>
    private void HandleTakeDamageDrop(ItemChip chip, Vector3 dropWorldPos, Transform slot, VRHand vrHand)
    {
        float scale = slot.lossyScale.x;
        float radius = UseSlotRadius * (scale > 1e-4f ? scale : 1f);
        if ((dropWorldPos - slot.position).sqrMagnitude > radius * radius)
            return; // dropped away from the slot — the base glide-home returns it to the fan

        if (chip.Item == null)
            return;
        UIUseItemScenario? barSlot = CardsGameApi.LiveItemsBarSlot(chip.Item);
        if (barSlot == null)
        {
            VRLog.Info("Cards", $"TAKE-DAMAGE item place: '{chip.name}' is NOT among the OnAttacked candidates " +
                                "(the panel's own filter) — it returns to the fan.");
            return; // base glide-home
        }

        // Swap: untoggle + return a previously clipped selection first (1:1 card ↔ toggle).
        if (_tdChip != null && !ReferenceEquals(_tdChip, chip))
        {
            ItemChip old = _tdChip;
            _tdChip = null;
            if (old.Item != null && old.Item.SlotState == CItem.EItemSlotState.Selected)
            {
                UIUseItemScenario? oldSlot = CardsGameApi.LiveItemsBarSlot(old.Item);
                if (oldSlot != null)
                    CardsGameApi.ClickItemsBarSlot(oldSlot);
            }
            UnclipChip(old);
            old.PendingUse = false;
            old.ReturnToFan();
        }

        // Toggle ON through the game's own click seam (idempotent on a re-drop of an already
        // selected item). Verify against the INVENTORY truth (SlotState), not the widget flag.
        if (chip.Item.SlotState != CItem.EItemSlotState.Selected)
        {
            if (!CardsGameApi.ClickItemsBarSlot(barSlot)
                || chip.Item.SlotState != CItem.EItemSlotState.Selected)
            {
                VRLog.Warn("Cards", $"TAKE-DAMAGE item place: the game rejected toggling '{chip.name}' " +
                                    "(slot state gate) — card returns to the fan.");
                return; // base glide-home
            }
        }

        _tdChip = chip;
        chip.PendingUse = true;
        chip.CancelReleaseGlide();
        if (_root != null)
            chip.ClipIntoSlot(slot, _root, ChipScale);
        vrHand.SendHaptic(HapticPreset.HoverTick);
        CardsDriver.PlayCardSound(CardsConfig.CardPlaceSound.Value, chip.transform);
        VRLog.Info("Cards", $"TAKE-DAMAGE item place: '{chip.name}' TOGGLED through the panel's own slot seam " +
                            "(ToggleShieldItem) — grab it back out to untoggle; the panel's TakeDamage " +
                            "confirm commits (and syncs) the selection.");
    }

    /// <summary>Take-damage context teardown (panel closed / context lost): the game's confirm
    /// already committed or discarded the selection, so the chip is only returned visually —
    /// nothing is toggled here. The fan stays up; its live rebuild shows the items' new state.</summary>
    private void EndTakeDamagePick(string why)
    {
        _tdActive = false;
        ItemChip? chip = _tdChip;
        _tdChip = null;
        if (chip != null)
        {
            UnclipChip(chip);
            chip.PendingUse = false;
            if (chip.Holder == null && chip.gameObject != null && chip.gameObject.activeInHierarchy)
                chip.ReturnToFan();
        }
        PlayTray.Current?.SetItemUseSlotVisible(false);
        _useSlotShownLogged = false;
        VRLog.Info("Cards", $"TAKE-DAMAGE item place END ({why}).");
    }

    // ================================================================== item chip ==

    /// <summary>
    /// One physical item card in the pile. Renders the REAL <c>ItemCardUI</c> face (game art +
    /// name; async background via <c>ImageAddressableLoader</c>) hosted on a world-space canvas,
    /// with a legacy colored slab + name as the fallback. Supports the full ability-card
    /// interaction set: fingertip hand-sweep POP (<see cref="SetFingertipPop"/>), dominant-hand
    /// LASER hover+pluck (the <see cref="IPokeable"/> seam, driven by the driver's geometric
    /// item-fan pick — see <see cref="ItemsPile.TryLaserRaycast"/>),
    /// pinch-GRAB, and read-in-hand (<see cref="GetHeldPose"/>). Spent/consumed chips show the
    /// hosted card's own state FX (UpdateState); consumed chips carry NO separate burn plume.
    /// </summary>
    internal sealed class ItemChip : GrabbableBehaviour, IPokeable, IGrabbableHandFilter,
        IFanSweepTarget
    {
        internal enum Visual { Ready, Spent, Consumed }

        internal Visual State { get; private set; }

        /// <summary>The live inventory item this chip represents (read-only — the ONLY write is the
        /// single <c>UseItemService.UseItem</c> the owner makes on a clip-in-to-use).</summary>
        internal CItem? Item { get; private set; }

        /// <summary>
        /// LIVE (never cached — hardware bug 3): can this item be USED right now? non-passive AND
        /// Useable/Selected — the exact gate <c>UseItemService.UseItem</c> enforces. Read every
        /// tick by the owner's use-slot gate + the clip-in-to-use path.
        /// </summary>
        internal bool IsActivatable => IsItemActivatable(Item);

        // Pop/enlarge for readability (fingertip sweep OR laser hover — spatially exclusive, so
        // one effective pop). Mirrors VRCard's pop: a small grow + a nudge toward the viewer.
        private const float PopScale = 1.18f;
        private const float PopLift = 0.02f;   // local -Z (toward the viewer) at full pop
        private const float PopLerpSpeed = 16f;

        /// <summary>Grab-box margin around the rendered face (card-local metres) — a little slack
        /// for easy laser/finger targeting. Named because <see cref="SetGrabStrip"/> has to rebuild
        /// the box from the face size every layout and must not drift from <see cref="Create"/>.</summary>
        private const float ColliderMargin = 0.006f;

        /// <summary>Grab-box depth (card-local metres): the chip is a thin plate.</summary>
        private const float ColliderDepth = 0.02f;

        private ItemsPile? _owner; // for the clip-in-to-use callback on release
        private Vector3 _homePos;
        private Quaternion _homeRot;
        private float _homeScale = 1f;
        private BoxCollider? _box;
        private GameObject? _cardGo;    // hosted ItemCardUI GameObject (recycled to the pool on disable)
        private ItemCardUI? _cardUI;
        private SmokeClamp[]? _smokeClamps; // ItemCardEffects emitters bounded card-local; restored before recycle
        private bool _fingerPopped;
        private bool _laserPopped;
        private float _pop; // smoothed 0..1
        // Actual rendered card size (item aspect) — set by TryHostRealCard, drives the backing + collider.
        private float _faceWidth;
        private float _faceHeight;

        /// <summary>Rendered face width in card-local metres (item aspect — NOT the ability-card
        /// aspect). Falls back to the ability-card width before the real card has been hosted.
        /// Read by <see cref="WorldUI.AvatarMirror"/> so a mirrored item slab keeps the item's own,
        /// near-square shape instead of being stretched to the ability-card ratio.</summary>
        internal float FaceWidth => _faceWidth > 0.001f ? _faceWidth : CardsConfig.CardWidth.Value;

        /// <summary>Rendered face height in card-local metres (see <see cref="FaceWidth"/>).</summary>
        internal float FaceHeight => _faceHeight > 0.001f ? _faceHeight : CardsConfig.CardHeight;

        // ITEM #1 (de-shimmer): the hosted ItemCardUI's cardBackground art loads ASYNC, so — exactly
        // like the ability cards' CardFace — its mipless-atlas sprites must be swapped for mip-baked
        // equivalents on host AND re-scanned on a slow cadence until the art has arrived (CardFace.cs
        // uses MipRescanInterval=1s and re-runs forever, because async arrivals / state changes keep
        // putting mipless originals back). RestoreSprites runs before the widget is recycled to the pool
        // so the pooled card is left clean. The bake caches are static + content-keyed, so the item
        // sprites share the ability cards' baked atlases for free (see CardFaceMipBake).
        private const float MipRescanInterval = 1f;
        private float _nextMipRescan;

        // ITEM #1 (de-shimmer immediately): the 1 s maintenance cadence above lands the FIRST successful
        // bake up to ~1.5 s after the fan opens (the art loads async, the periodic Rescan then catches
        // it), so the card visibly shimmers until then. Close that window: for the first ~2 s after host
        // poll the hosted card's background sprite every frame (a cheap null check) and Rescan the INSTANT
        // the async art appears — no visible shimmer window once the art is present. Then the 1 s cadence
        // takes over for ongoing state-change maintenance.
        private const float TightArtPollSeconds = 2f;
        private float _tightArtPollUntil;
        private bool _artBaked;

        // USABLE HIGHLIGHT (replaces the former de-emphasis dim — see ROOT CAUSE below).
        //
        // WHAT CHANGED AND WHY: the first two attempts at this cue worked the NEGATIVE way round —
        // de-emphasise everything you cannot play. Attempt 1 (a CanvasGroup alpha on the hosted card)
        // was invisible because the game's own ItemCardUI.OnReturnedToPool DISABLES the card's
        // CanvasGroup (component.enabled = false, ItemCardUI.cs:350), so a pooled card handed back to
        // us ignores every alpha we write. Attempt 2 (a mod-owned dark quad over the face) was visible
        // but wrong on its own terms: with most items passive or off-turn, the fan was mostly grey
        // sludge, the art stopped reading, and the player had to infer the playable cards from the
        // ABSENCE of a veil. The cue is now POSITIVE — light up exactly the cards you CAN play and
        // leave every other card at its natural, fully legible look.
        //
        // MECHANISM (round 2 — the gold glow QUAD is gone, see BuildUsableFrame): the cue is now the
        // SAME soft, breathing OUTLINE the initiative order wears for "this hero still has to choose"
        // (WorldUI SoftCueArt.FrameSprite + SoftFramePulse, extracted from InitiativeSelectionGlow) —
        // a hollow 9-sliced frame in the card's margin, with rounded corners so it hugs the card
        // instead of boxing it in. The user rejected the previous flat gold overlay outright and named
        // that initiative frame as the reference, so the two now share one sprite recipe and one breath.
        // Fully mod-owned: a child canvas of OUR chip GameObject, destroyed with the chip; the hosted
        // game ItemCardUI is never touched, so the widget goes back to the ObjectPool untouched.
        // Toggled LIVE (never cached) from the owner's turn-aware CanUseNow. -1 = "not yet applied".
        private GameObject? _usableFrame;  // mod-owned soft gold outline framing USABLE item cards
        private int _usabilityShown = -1;  // last applied state: -1 none, 0 normal, 1 highlighted

        // ITEM #3 (desktop mirror): the hosted card's world-space FaceCanvas. VRCard binds its face
        // canvas' worldCamera to the head camera every frame (VRCard.UpdateCanvasCamera); ItemsPile
        // never did, so a WorldSpace canvas with a null worldCamera culled/sorted differently per
        // camera and the item face was absent from the desktop mirror while ability cards showed.
        // Binding it to VRRigDriver.HeadCamera makes the item face render exactly like the ability
        // face (both faces are already on the same authored UI layer; the mod bodies on the mod layer).
        private Canvas? _faceCanvas;

        // FIX 1 (held pose) — the in-hand pinch target captured at grab time, in GrabAnchor-local
        // space; TickHeldPose lerps toward it each frame while also billboarding the face to the head
        // (mirror of VRCard._heldPos/_heldRot/_heldScale + VRCard.TickHeldPose).
        private Vector3 _heldPos;
        private Quaternion _heldRot = Quaternion.identity;
        private float _heldScale = 1f;

        /// <summary>FIX 1 — fraction of the card height between the bottom edge and the pinch anchor
        /// (mirror of VRCard.PinchGripFraction): the fingers grip ~12 % up from the card bottom.</summary>
        private const float PinchGripFraction = 0.12f;

        /// <summary>FIX 2 — unscaled seconds the post-release home glide runs (mirror of
        /// VRCard.ReleaseGlideSeconds): keeps the exponential home-lerp flying while the game may pause
        /// simulation time. <see cref="_releaseGlide"/> counts it down; a re-grab cancels it.</summary>
        private const float ReleaseGlideSeconds = 0.35f;
        private float _releaseGlide;

        // Requirement 5 (collapse-into-pile): a closing chip is detached from the fan root by the owner
        // and self-glides (WORLD space) into the pile stack point, then destroys itself — its OnDisable
        // recycles the hosted ItemCardUI back to the pool, so the collapse never leaks a card widget.
        private bool _collapsing;
        private Vector3 _collapseWorld;
        private float _collapseTime;
        private const float CollapseSeconds = 0.26f;

        // Requirement 6 (clip-in decision): while a released usable chip is CLIPPED into the use slot
        // awaiting a Confirm/cancel decision, PendingUse is set and the chip is RE-PARENTED onto the
        // slot (ClipIntoSlot) so the hierarchy holds it there rigidly — it used to be chased toward a
        // per-tick target instead, which is what made it swim behind head movement. The chip stays
        // grabbable so the player can grab it BACK OUT to cancel (the #6 refinement); the grab hands it
        // back to the fan root first, so on release it glides to the fan as always.
        internal bool PendingUse { get; set; }
        // (There is no _hasClip flag any more: the clip/unclip rework left behind a field that was
        // written false in three places and never read — CS0414. The hierarchy owns the clipped
        // pose now, so there is nothing for a flag to gate.)

        // Requirement 6 (use FX): after a CONFIRM the owner detaches the chip and plays a brief flourish
        // reflecting the result — a burn plume (Consumed) or a "tap" roll to 90° with a scale pulse
        // (Spent) — then the chip collapses into the deck. Independent of the collapse/pending states.
        private bool _useFxActive;
        private float _useFxTime;
        private bool _useFxSpent;
        private Vector3 _useFxCollapseWorld;
        private Quaternion _useFxBaseRot = Quaternion.identity;
        private const float UseFxSeconds = 0.55f;

        private static bool s_loggedRealCard;
        private static bool s_loggedBacking; // one-line confirm of the backing source reused (FIX 3)
        private static bool s_loggedMirror;  // one-line confirm of the desktop-mirror canvas-camera bind (#3)

        internal static ItemChip Create(ItemsPile owner, Transform parent, CItem item)
        {
            Visual state = Classify(item);
            float w = CardsConfig.CardWidth.Value;
            float h = CardsConfig.CardHeight;

            var go = new GameObject($"ItemChip_{Name(item)}");
            go.transform.SetParent(parent, worldPositionStays: false);

            // #2: the grab collider MUST exist on this GameObject BEFORE the GrabbableBehaviour (ItemChip)
            // is added — GrabbableBehaviour.Awake caches GetComponent<Collider>() and, finding none, warned
            // and never armed grab, so the laser/trigger pluck was dead (only the collider-free finger
            // sweep worked). Add it first with a placeholder size; it's resized to the real card below.
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;

            var chip = go.AddComponent<ItemChip>();
            chip.snapToHand = true; // taken INTO the hand to read
            chip.State = state;
            chip.Item = item;
            chip._owner = owner;
            chip._box = box;

            // Requirement 1: host the REAL ItemCardUI face. Fall back to a colored slab + name if the
            // game's item-card pool is unavailable. Hosting measures the item card's real (near-square)
            // size into chip._faceWidth/_faceHeight so the body + collider match it (no black bars).
            bool realCard = chip.TryHostRealCard(item, go.transform, w, h, state);
            float cw = chip._faceWidth > 0.001f ? chip._faceWidth : w;
            float ch = chip._faceHeight > 0.001f ? chip._faceHeight : h;

            // FIX 3 — the card BODY behind the face. For a REAL item card, reuse the SAME backing the
            // ability cards wear (bundle CardBacking.prefab handed out by PlayTray, or the procedural
            // CardMesh fallback) so the item card's back matches the others instead of a plain black
            // slab — cropped to the item card's near-square shape. The legacy colored cube slab is kept
            // ONLY for the fallback (pool-unavailable) face so its icon/name still read on a tinted body.
            GameObject? backing = realCard ? chip.BuildCardBacking(go.transform, cw, ch) : null;
            if (backing == null)
            {
                // Thin cube slab, sized to the ACTUAL card (item aspect): dark for a real card whose
                // shared backing was unavailable, tinted FaceColor for the colored-slab fallback face.
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Backing";
                Object.Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(go.transform, worldPositionStays: false);
                cube.transform.localScale = new Vector3(cw, ch, 0.0022f);
                cube.transform.localPosition = new Vector3(0f, 0f, 0.0012f); // behind the face (+Z away from viewer)
                Shader? backShader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                if (backShader != null)
                    cube.GetComponent<MeshRenderer>().sharedMaterial = new Material(backShader)
                        { color = realCard ? new Color(0.10f, 0.09f, 0.08f) : FaceColor(state) };
                Core.VRLayers.Apply(cube); // mod-owned backing on the mod layer (recursion-safe: no children)
            }
            if (!realCard)
                BuildFallbackFace(go.transform, item, cw, ch, state);

            // USABLE HIGHLIGHT — the mod-owned soft outline AROUND the card, hidden by default and
            // shown by TickFaceMaintenance for items that CAN be used right now. Built to the ACTUAL
            // card size so the frame traces the real (near-square) item silhouette; mod layer; a child
            // of the chip, so it dies with the chip and never touches the pooled game card.
            chip._usableFrame = chip.BuildUsableFrame(go.transform, cw, ch);

            // Now size the grab collider to the real card (a small margin for easy laser/finger targeting).
            box.size = new Vector3(cw + ColliderMargin, ch + ColliderMargin, ColliderDepth);
            // NOTE: do NOT VRLayers.Apply(go) — it recurses into the hosted ItemCardUI, which is a
            // GAME-owned canvas that must keep its authored UI layer (reversibility rule; it renders
            // via the VR camera's UI-layer bit owned by CanvasConversion). The mod-owned pieces
            // (backing above, fallback face below) are layered individually instead.

            // FULLY CONSUMED → the ashen tint here PLUS the game's own ItemCardEffects burn timeline,
            // which TryHostRealCard leaves live on the hosted card (bounded by ClampCardEffectSmoke).
            // The SEPARATE CardSmoke plume stays removed — BurnCardFx.SpawnConsumedPlume, which spawned
            // it, has itself been deleted for want of callers (its own file keeps the full reasoning).
            // That prefab is authored for the full-size screen card and, spawned onto the item chip, was
            // the second fog source.

            // Laser: NOT registered as a generic PlayTray laser target any more. The chips' laser
            // hover/pluck is the driver's geometric fan pick (CardsDriver.UpdateItemFanLaser →
            // ItemsPile.TryLaserRaycast, same OnPokeEnter/OnPoke seam) — the physics-collider scan
            // elected nearest LIVE colliders with no hysteresis, and since the hover pop moves and
            // enlarges this very collider, that made right→left laser sweeps skip every second
            // chip (full mechanism on TryLaserRaycast). The collider itself stays: it is the
            // pinch-grab volume and the fingertip-sweep distance source.

            if (!s_loggedRealCard)
            {
                s_loggedRealCard = true;
                VRLog.Info("Cards", $"ITEM CARD: art resolved={realCard} via " +
                                    $"{(realCard ? "ObjectPool ItemCardUI (real face + async background art)" : "fallback colored slab + name")} " +
                                    $"for '{go.name}'.");
            }
            return chip;
        }

        /// <summary>
        /// FIX 3 (back face — not black): build the chip's card BODY reusing the SAME backing the
        /// ability cards use so the item card's back matches them. Prefers the bundle
        /// <c>CardBacking.prefab</c> the <see cref="VRCardFactory"/> hands to <see cref="VRCard.Build"/>
        /// (exposed via <see cref="PlayTray.CardBackingPrefab"/>); when the bundle prefab is
        /// unavailable it falls back to the procedural <see cref="CardMesh"/> slab — the exact edge/back
        /// materials VRCard's own procedural backing uses. Either way the body is sized to the item
        /// card's near-square <paramref name="cw"/>×<paramref name="ch"/> shape (the same back, just
        /// cropped). Returns null only if neither path can build (caller draws the legacy cube slab).
        /// </summary>
        private GameObject? BuildCardBacking(Transform parent, float cw, float ch)
        {
            try
            {
                GameObject? prefab = PlayTray.Current?.CardBackingPrefab;
                GameObject backing;
                string source;
                if (prefab != null)
                {
                    backing = Object.Instantiate(prefab, parent, worldPositionStays: false);
                    // The prefab is authored at the ability card w×h — crop it to the item's near-square
                    // shape by scaling its authored (base) scale by cw/w and ch/h (same back, cropped).
                    float w = CardsConfig.CardWidth.Value;
                    float h = CardsConfig.CardHeight;
                    Vector3 baseScale = backing.transform.localScale;
                    if (w > 1e-5f && h > 1e-5f)
                        backing.transform.localScale = new Vector3(
                            baseScale.x * (cw / w), baseScale.y * (ch / h), baseScale.z);
                    source = "bundle CardBacking.prefab (shared with the ability cards, via PlayTray)";
                }
                else
                {
                    // Procedural fallback: the SAME rounded slab + edge/back materials VRCard builds when
                    // the bundle prefab is missing — sized directly to the item's near-square shape.
                    backing = new GameObject("Backing");
                    backing.transform.SetParent(parent, worldPositionStays: false);
                    backing.AddComponent<MeshFilter>().sharedMesh = CardMesh.Get(cw, ch);
                    var mr = backing.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = new[] { CardMesh.CreateEdgeMaterial(), CardMesh.CreateBackMaterial() };
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    source = "procedural CardMesh backing (same edge/back material as the ability cards)";
                }
                backing.name = "Backing";
                backing.transform.localPosition = Vector3.zero; // face canvas sits a hair in front (-Z)
                Core.VRLayers.Apply(backing); // mod-owned backing on the mod layer (no game children)
                if (!s_loggedBacking)
                {
                    s_loggedBacking = true;
                    VRLog.Info("Cards", $"ITEM CARD backing source: {source} — the item chip's back now " +
                                        "matches the ability cards (no more plain black slab).");
                }
                return backing;
            }
            catch (System.Exception e)
            {
                VRLog.Warn("Cards", $"ITEM CARD backing build failed ({e.Message}) — falling back to the cube slab.");
                return null;
            }
        }

        /// <summary>
        /// Build the mod-owned "you can play this NOW" cue: a soft, breathing gold FRAME around the
        /// card — the very treatment the user pointed at ("ein Rahmen, ähnlich wie du es mal bei der
        /// Initiativreihenfolge gemacht hast"), reusing the initiative ring's own art and motion via
        /// <see cref="WorldUI.SoftCueArt.FrameSprite"/> + <see cref="WorldUI.SoftFramePulse"/>.
        ///
        /// WHY A FRAME AND NOT THE OLD GLOW QUAD: the previous cue was an additive gold quad parked
        /// behind the card so its oversized border survived as a halo. It worked mechanically but the
        /// user rejected the LOOK outright — a hard-edged rectangle of light stuck behind an antique
        /// fantasy card. A hollow 9-sliced outline draws light ONLY in the card's margin band, with a
        /// falloff on both sides and ROUNDED corners, so nothing is ever a filled rectangle; the item
        /// art stays completely untouched and the cue reads as the same "waiting for you" language the
        /// initiative bar already speaks.
        ///
        /// WHY A uGUI CANVAS AND NOT A MESH: the frame's thickness must be CONSTANT (a fixed band in the
        /// card's margin), not proportional to the card — a stretched textured quad would scale its
        /// border with the card and turn a hairline into a slab on a wide card. That is exactly what a
        /// 9-sliced Image gives for free, and it is what makes this literally the same mechanism as the
        /// initiative ring rather than a look-alike. The chip already hosts one mod-owned world-space
        /// canvas for the card face, so this adds a second sibling canvas of the SAME kind — it is
        /// parked at <see cref="FrameZ"/> (viewer side of the face) with an explicit
        /// <c>sortingOrder</c> above the face canvas, so the draw order is decided, not distance-luck.
        ///
        /// Built INACTIVE; <see cref="TickFaceMaintenance"/> toggles it live from the owner's turn-aware
        /// <c>CanUseNow</c>. Never touches the hosted game card — a child of the chip, destroyed with it.
        /// </summary>
        private GameObject BuildUsableFrame(Transform parent, float cw, float ch)
        {
            // The mod's telegraph gold, warmed toward the initiative ring's amber so the two cues are
            // recognisably the same voice. Alpha is the CEILING the breath multiplies (SoftFramePulse).
            var color = new Color(1f, 0.80f, 0.32f, 1f);

            var canvasGo = new GameObject("UsableFrame", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)canvasGo.transform;
            rt.SetParent(parent, worldPositionStays: false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1; // above the hosted face canvas (0) — deterministic, not distance-sorted

            // px → metres. Working in a fixed pixel reference (rather than in metres) is what lets the
            // outset/border constants below be stated in the SAME uGUI px units the initiative ring uses,
            // so both frames end up proportionally identical.
            float scale = cw / FrameReferencePixels;
            rt.sizeDelta = new Vector2(FrameReferencePixels + 2f * FrameOutsetPixels,
                                       ch / scale + 2f * FrameOutsetPixels);
            rt.localScale = new Vector3(scale, scale, scale);
            rt.localPosition = new Vector3(0f, 0f, FrameZ);
            rt.localRotation = Quaternion.identity;

            // The outline itself lives on a CHILD stretched to the canvas rect, so SoftFramePulse can
            // breathe its localScale without rescaling the canvas' pixel reference under it.
            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.SetParent(rt, worldPositionStays: false);
            ringRt.anchorMin = Vector2.zero;
            ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero;
            ringRt.offsetMax = Vector2.zero;
            ringRt.localPosition = new Vector3(0f, 0f, 0f);
            ringRt.localRotation = Quaternion.identity;

            var img = ringGo.GetComponent<Image>();
            img.sprite = WorldUI.SoftCueArt.FrameSprite(FrameCornerRadiusPx);
            img.type = Image.Type.Sliced;
            img.fillCenter = false;   // hollow — a frame, never a wash over the art
            img.raycastTarget = false; // the chip is grabbed through its collider; nothing may raycast this
            img.color = color;
            ringGo.AddComponent<WorldUI.SoftFramePulse>().Init(img, color);

            Core.VRLayers.Apply(canvasGo); // mod-owned overlay on the mod layer (no game children below it)
            canvasGo.SetActive(false);     // shown only while the item is usable
            return canvasGo;
        }

        /// <summary>Pixel width the card is mapped to for the frame canvas. Matches the item card's own
        /// native rect (~300 px), so <see cref="FrameOutsetPixels"/> and the sprite's 16 px border land in
        /// the same proportions the initiative ring wears on a portrait.</summary>
        private const float FrameReferencePixels = 300f;

        /// <summary>How far (uGUI px, i.e. ~1/300 of the card width) the frame rect is grown past the card
        /// on every side — the initiative ring's outset. The sprite's bright core sits 2–6 px inside the
        /// frame's outer lip, so the visible gold line floats just OUTSIDE the card silhouette and only its
        /// soft inner falloff touches the art's outermost few percent.</summary>
        private const float FrameOutsetPixels = 8f;

        /// <summary>Corner rounding (px) of the outline. Non-zero on purpose: the user's one hard
        /// constraint on the new cue was "nicht viereckig", and a rounded, softly-falling outline hugging
        /// the card is the opposite of the hard rectangle that was rejected.</summary>
        private const int FrameCornerRadiusPx = 14;

        /// <summary>Local -Z (TOWARD the viewer) the frame sits at: 1 mm proud of the hosted face canvas
        /// (-0.0012) so no card body or face graphic can ever occlude or z-fight the outline, yet close
        /// enough that it never parallax-separates from the card when the fan is viewed at an angle.</summary>
        private const float FrameZ = -0.0022f;

        /// <summary>
        /// Host the game's real <c>ItemCardUI</c> on a world-space canvas (requirement 1): spawn it
        /// from the game's own pool exactly as the flat inventory tooltip does
        /// (<c>ObjectPool.SpawnCard(item.ID, ECardType.Item, …)</c> → set <c>item</c> →
        /// <c>Show(false)</c> → <c>UpdateState(SlotState, force)</c>), then reparent + fit it onto a
        /// world-space canvas. The background art loads ASYNC onto <c>cardBackground</c> once active.
        /// Any GraphicRaycaster on the card is disabled so the game's input module never raycasts our
        /// world canvas (the card is a grab target, not a click surface). Fully guarded — returns
        /// false on any failure so the caller draws the colored-slab fallback.
        /// </summary>
        private bool TryHostRealCard(CItem item, Transform parent, float w, float h, Visual state)
        {
            try
            {
                if (item.ID == 0)
                    return false;

                var canvasGo = new GameObject("FaceCanvas");
                canvasGo.transform.SetParent(parent, worldPositionStays: false);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                _faceCanvas = canvas; // #3: bind worldCamera to the head camera (kept live in TickFaceMaintenance)
                var canvasRect = (RectTransform)canvasGo.transform;
                canvasRect.localPosition = new Vector3(0f, 0f, -0.0012f); // viewer side of the backing
                // Layer the (still EMPTY) mod-owned canvas now — BEFORE the game card is parented
                // under it — so the recursive layer apply never touches the game-owned card (which
                // keeps its authored UI layer; SpawnCard's reparent does not change the card's layer).
                Core.VRLayers.Apply(canvasGo);

                GameObject cardGo = ObjectPool.SpawnCard(item.ID, ObjectPool.ECardType.Item,
                    canvasRect, resetLocalScale: true);
                if (cardGo == null)
                {
                    Object.Destroy(canvasGo);
                    return false;
                }
                var cardUI = cardGo.GetComponent<ItemCardUI>();
                if (cardUI == null)
                {
                    ObjectPool.RecycleCard(item.ID, ObjectPool.ECardType.Item, cardGo);
                    Object.Destroy(canvasGo);
                    return false;
                }
                cardUI.item = item;
                // The game's ItemCardEffects runs ON the card (Show()/UpdateState() →
                // cardEffects.ToggleEffect(Consumed/Spent)) and is KEPT LIVE: the "verbraucht"
                // look the user wants back — the burn tint + dissolve + grey-out material sweep
                // over the card's face images, the greyed card text and the `fgFx` flame/ghost
                // overlay quad — is all uGUI ON the card's own RectTransforms, so it renders at
                // exactly card size on our world canvas. (An earlier round nulled cardEffects
                // wholesale to kill the fog; that threw the on-card FX away with it.)
                cardUI.Show(highlightElement: false); // no UIManager lock; activates + loads art async
                cardUI.UpdateState(item.SlotState, force: true); // plays the state FX on the card

                // Fit the card's native rect onto the physical card size (mirror of CardFace).
                var cardRect = cardGo.transform as RectTransform;
                Vector2 native = cardRect != null ? cardRect.rect.size : Vector2.zero;
                if (native.x < 1f || native.y < 1f)
                    native = new Vector2(300f, 300f); // near-square fallback (item cards are ~square)
                canvasRect.sizeDelta = native;
                float fit = Mathf.Min(w / native.x, h / native.y);
                canvasRect.localScale = new Vector3(fit, fit, fit);
                // #1: item cards are NEAR-SQUARE, not the tall ability rect — fitting them into the w×h
                // ability box left black backing bars top/bottom. Record the ACTUAL rendered card size so
                // Create sizes the backing slab + grab collider to match it exactly (no bars; the dark
                // body is the same as the other cards, just cropped to the item card's shape).
                _faceWidth = native.x * fit;
                _faceHeight = native.y * fit;
                if (cardRect != null)
                {
                    cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
                    cardRect.anchoredPosition3D = Vector3.zero;
                    cardRect.localRotation = Quaternion.identity;
                    cardRect.localScale = Vector3.one;
                }

                // Bound the ONE part of the state FX that is NOT uGUI (see ClampCardEffectSmoke).
                // Runs AFTER the fit above so the clamp can measure the card's final world scale;
                // still the same frame the effect coroutine was started in, and particles do not
                // simulate until after this Update, so nothing oversized is ever emitted.
                ClampCardEffectSmoke(cardGo);

                // Neutralize the card's own raycasters — we drive interaction through the mod's
                // collider (grab/laser), never the game's uGUI input module (which would raycast
                // this world canvas at the parked mouse pixel).
                foreach (GraphicRaycaster gr in cardGo.GetComponentsInChildren<GraphicRaycaster>(true))
                    gr.enabled = false;

                // ITEM #1 — swap the mipless-atlas sprites for mip-baked equivalents right after host,
                // then (a) tight-poll for the async background art every frame for the first ~2 s and
                // Rescan the INSTANT it appears (no shimmer window), and (b) re-scan on the 1 s cadence
                // afterward for ongoing state-change maintenance.
                CardFaceMipBake.Rescan(cardUI);
                _nextMipRescan = Time.unscaledTime + MipRescanInterval;
                _tightArtPollUntil = Time.unscaledTime + TightArtPollSeconds;
                _artBaked = false;

                _cardGo = cardGo;
                _cardUI = cardUI;
                return true;
            }
            catch (System.Exception e)
            {
                VRLog.Warn("Cards", $"ITEM CARD host failed ({e.Message}) — falling back to the colored slab.");
                return false;
            }
        }

        /// <summary>One clamped game emitter + the module values it had before we touched it. The start
        /// size/speed are kept as the WHOLE <c>MinMaxCurve</c>, not just their multiplier: in
        /// TwoConstants/TwoCurves mode the multiplier only writes the MAX side, so scaling through it
        /// would leave the MIN side unshrunk (and could invert the range).</summary>
        private struct SmokeClamp
        {
            internal ParticleSystem Ps;
            internal ParticleSystemSimulationSpace Space;
            internal ParticleSystemScalingMode Scaling;
            internal ParticleSystem.MinMaxCurve StartSize;
            internal ParticleSystem.MinMaxCurve StartSpeed;
        }

        /// <summary>
        /// The consumed/spent plume may not read wider than this many CARD WIDTHS in world space —
        /// wisps ON the card, never a cloud AROUND it. The user explicitly wants the burn look back
        /// but not the fog, so this is a hard geometric bound, not a taste multiplier.
        /// </summary>
        private const float SmokeCardSpan = 0.9f;

        private static bool s_loggedSmokeClamp;

        /// <summary>
        /// ROOT CAUSE of the field-sized fog ring around a consumed item card, and the ONE thing that
        /// has to be neutralised (everything else in <c>ItemCardEffects</c> stays live).
        ///
        /// <c>ItemCardEffects.BurnCardTimeline</c> / <c>GhostOutOnTimeline</c> drive two kinds of
        /// visual. Nearly all of it is uGUI ON the card — the <c>_Burn</c>/<c>_Dissolve</c>/
        /// <c>_GreyOut</c>/<c>_Flow</c> material sweep over <c>imgComp</c>, the greyed <c>txtComp</c>
        /// text, and the <c>fgFx</c> flame/ghost overlay Image — all of which lives on the card's own
        /// RectTransforms and therefore renders at exactly card size on our world-space FaceCanvas.
        /// The single exception is the serialized <c>fx_Smoke</c> <see cref="ParticleSystem"/> the
        /// timeline switches on: a particle system renders through its OWN renderer, not the canvas,
        /// and Unity's default <c>ParticleSystemScalingMode.Local</c>/<c>Shape</c> makes it IGNORE the
        /// parent scale chain. Under the flat screen-space canvas that is invisible (scale 1); under
        /// our FaceCanvas — which is downscaled by ~<c>fit</c> (card metres ÷ ~300 canvas px, i.e.
        /// three orders of magnitude) to make the card hand-sized — the emitter keeps emitting at its
        /// authored CANVAS-PIXEL size in METRES. That is the green fog: puffs tens of metres across,
        /// centred on and following the card. Nothing about the effect's colour or timing was wrong,
        /// only its scale reference.
        ///
        /// Fix (mod-side, reversible, no re-layering, no game data touched): leave the effect running
        /// and put every emitter in the hosted card back into the card's frame of reference —
        /// <c>simulationSpace = Local</c> so the puffs ride the card instead of being emitted into
        /// world space at authored size, and <c>scalingMode = Hierarchy</c> so start size/velocity
        /// inherit the FaceCanvas downscale like the rest of the card does. Hierarchy scaling alone
        /// already restores the authored card-relative look (unlike <see cref="BurnCardFx"/>, whose
        /// plume is a separately spawned prefab authored for the full-size screen card); on top of it
        /// we still shrink start size/speed by whatever factor is needed to keep the plume inside
        /// <see cref="SmokeCardSpan"/> card widths, so a stray authored value can never grow into a
        /// cloud again. Only these four module values are written, and every one is recorded per
        /// emitter and restored by <see cref="RestoreCardEffectSmoke"/> before the widget goes back to
        /// <c>ObjectPool.RecycleCard</c>.
        /// </summary>
        private void ClampCardEffectSmoke(GameObject cardGo)
        {
            _smokeClamps = null;
            if (cardGo == null)
                return;
            // includeInactive: the timeline only SetActive(true)s fx_Smoke once it starts, and
            // RestoreCard switches it back off — so at host time it is normally inactive.
            ParticleSystem[] systems = cardGo.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            if (systems.Length == 0)
                return;

            // World size of one authored unit once scalingMode=Hierarchy applies the card's scale chain.
            float lossy = Mathf.Abs(cardGo.transform.lossyScale.x);
            float span = (_faceWidth > 0.001f ? _faceWidth : CardsConfig.CardWidth.Value) * SmokeCardSpan;

            var clamps = new List<SmokeClamp>(systems.Length);
            var report = new StringBuilder();
            foreach (ParticleSystem ps in systems)
            {
                if (ps == null)
                    continue;
                ParticleSystem.MainModule main = ps.main;
                var rec = new SmokeClamp
                {
                    Ps = ps,
                    Space = main.simulationSpace,
                    Scaling = main.scalingMode,
                    StartSize = main.startSize,
                    StartSpeed = main.startSpeed,
                };

                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;

                // Belt-and-braces bound: with Hierarchy scaling a particle's world size is its start
                // size × the card's lossy scale, and its world drift is start speed × lifetime × the
                // same. Shrink both by the single worst-case factor needed to keep them inside `span`.
                float shrink = 1f;
                if (lossy > 1e-6f && span > 1e-5f)
                {
                    float worldSize = UpperBound(main.startSize) * lossy;
                    float worldDrift = UpperBound(main.startSpeed) * UpperBound(main.startLifetime) * lossy;
                    float worst = Mathf.Max(worldSize, worldDrift);
                    if (worst > span)
                        shrink = span / worst;
                }
                if (shrink < 1f)
                {
                    main.startSize = ScaleCurve(rec.StartSize, shrink);
                    main.startSpeed = ScaleCurve(rec.StartSpeed, shrink);
                }

                clamps.Add(rec);
                if (!s_loggedSmokeClamp)
                    report.Append(report.Length > 0 ? ", " : "")
                          .Append(ps.name).Append(" [").Append(rec.Space).Append('/').Append(rec.Scaling)
                          .Append(" → Local/Hierarchy, size×").Append(shrink.ToString("F3")).Append(']');
            }
            _smokeClamps = clamps.ToArray();

            if (!s_loggedSmokeClamp)
            {
                s_loggedSmokeClamp = true;
                VRLog.Debug("Cards", $"ITEM CARD FX: game ItemCardEffects LIVE (burn/dissolve/greyout/overlay on the " +
                                     $"card); bounded {clamps.Count} emitter(s) to <= {span:F3} m " +
                                     $"(card lossy {lossy:F5}): {(report.Length > 0 ? report.ToString() : "none")}.");
            }
        }

        /// <summary>Worst-case value a start-size/speed/lifetime curve can produce (curve modes fold
        /// their multiplier, which is the curve's peak, so this is an upper bound in every mode).</summary>
        private static float UpperBound(ParticleSystem.MinMaxCurve curve) => curve.mode switch
        {
            ParticleSystemCurveMode.Constant => curve.constant,
            ParticleSystemCurveMode.TwoConstants => Mathf.Max(curve.constantMin, curve.constantMax),
            _ => Mathf.Abs(curve.curveMultiplier),
        };

        /// <summary>Scale a start-size/speed curve by <paramref name="f"/> in EVERY mode (constants and
        /// curve multipliers alike), so both ends of a random range shrink together.</summary>
        private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float f) => curve.mode switch
        {
            ParticleSystemCurveMode.Constant => new ParticleSystem.MinMaxCurve(curve.constant * f),
            ParticleSystemCurveMode.TwoConstants =>
                new ParticleSystem.MinMaxCurve(curve.constantMin * f, curve.constantMax * f),
            ParticleSystemCurveMode.TwoCurves =>
                new ParticleSystem.MinMaxCurve(curve.curveMultiplier * f, curve.curveMin, curve.curveMax),
            _ => new ParticleSystem.MinMaxCurve(curve.curveMultiplier * f, curve.curve),
        };

        /// <summary>
        /// Put every emitter clamped by <see cref="ClampCardEffectSmoke"/> back to its recorded module
        /// values. MUST run before <c>ObjectPool.RecycleCard</c>: the card widget is GAME-owned and
        /// pooled, so the flat UI would inherit our clamp on the next spawn. Writing module values on a
        /// system the pool has already disabled is harmless, so no active check is needed.
        /// </summary>
        private void RestoreCardEffectSmoke()
        {
            SmokeClamp[]? clamps = _smokeClamps;
            _smokeClamps = null;
            if (clamps == null)
                return;
            foreach (SmokeClamp rec in clamps)
            {
                if (rec.Ps == null)
                    continue;
                ParticleSystem.MainModule main = rec.Ps.main;
                main.simulationSpace = rec.Space;
                main.scalingMode = rec.Scaling;
                main.startSize = rec.StartSize;
                main.startSpeed = rec.StartSpeed;
            }
        }

        /// <summary>
        /// Legacy fallback face (pool unavailable): a colored slab tint is set on the backing by the
        /// caller; here we add the synchronous mini-icon (if any) + the localized item name so the
        /// chip still reads. Only reached when <see cref="TryHostRealCard"/> failed.
        /// </summary>
        private static void BuildFallbackFace(Transform parent, CItem item, float w, float h, Visual state)
        {
            Sprite? icon = TryGetIcon(item);
            bool hasIcon = icon != null;
            if (hasIcon)
            {
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(parent, worldPositionStays: false);
                iconGo.transform.localPosition = new Vector3(0f, h * 0.14f, -0.0018f);
                var sr = iconGo.AddComponent<SpriteRenderer>();
                sr.sprite = icon;
                if (state == Visual.Consumed)
                    sr.color = new Color(0.62f, 0.60f, 0.58f);
                Vector2 size = icon!.bounds.size;
                if (size.x > 1e-4f && size.y > 1e-4f)
                {
                    float s = Mathf.Min(w * 0.82f / size.x, h * 0.56f / size.y);
                    iconGo.transform.localScale = Vector3.one * s;
                }
                Core.VRLayers.Apply(iconGo); // mod-owned fallback icon on the mod layer
            }

            var nameGo = new GameObject("Name");
            nameGo.transform.SetParent(parent, worldPositionStays: false);
            nameGo.transform.localPosition = new Vector3(0f, hasIcon ? -h * 0.36f : 0f, -0.0025f);
            var nameTmp = nameGo.AddComponent<TextMeshPro>();
            nameTmp.text = Name(item);
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color = state == Visual.Consumed ? new Color(0.75f, 0.72f, 0.7f)
                                                     : new Color(0.12f, 0.10f, 0.07f);
            WorldUI.NativeButtonSkin.ApplyFont(nameTmp);
            Core.TmpFit.Fit(nameTmp, w * (hasIcon ? 0.9f : 0.88f), h * (hasIcon ? 0.24f : 0.82f),
                            maxFontSize: 0.16f, wrap: true);
            Core.VRLayers.Apply(nameGo); // mod-owned fallback name label on the mod layer
        }

        /// <summary>
        /// Fallback-only: resolve the item's SYNCHRONOUS mini-icon (<c>ItemConfigUI.miniIcon</c>).
        /// Often null for items (the real face lives in the addressable <c>BackgroundImage</c>, used
        /// by the hosted card above) — fully null-guarded.
        /// </summary>
        private static Sprite? TryGetIcon(CItem item)
        {
            if (item == null || item.YMLData == null)
                return null;
            string? art = item.YMLData.Art;
            if (string.IsNullOrEmpty(art))
                return null;
            if (!Singleton<UIInfoTools>.IsInitialized)
                return null;
            UIInfoTools tools = Singleton<UIInfoTools>.Instance;
            if (tools == null)
                return null;
            try
            {
                return tools.GetItemConfig(art)?.miniIcon;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Requirement 5 (emerge): start the chip AT the pile converge point (root-local) and shrunk,
        /// then reuse the release-glide easing to fly it out to its arc home. Called once at open, after
        /// <see cref="SetHome"/> has recorded the home pose.
        /// </summary>
        internal void BeginEmerge(Vector3 localConverge)
        {
            if (Holder != null)
                return;
            transform.localPosition = localConverge;
            transform.localScale = Vector3.one * (_homeScale * 0.35f);
            _releaseGlide = ReleaseGlideSeconds; // Update's home-glide flies it to _homePos/_homeRot/_homeScale
        }

        /// <summary>
        /// Requirement 5 (collapse): begin a self-driven WORLD-space glide into the pile stack point,
        /// then destroy this chip. The owner has already re-parented the chip out of the fan root so it
        /// keeps updating after the root deactivates. Disables the collider so it can't be grabbed mid-collapse.
        /// </summary>
        internal void BeginCollapse(Vector3 worldConverge)
        {
            _collapsing = true;
            _collapseWorld = worldConverge;
            _collapseTime = CollapseSeconds;
            _fingerPopped = false;
            _laserPopped = false;
            if (_box != null)
                _box.enabled = false;
        }

        /// <summary>
        /// Requirement 6 — BOLT the chip into the use slot: re-parent it onto the slot transform at an
        /// exact zero local pose, so it is as rigid there as a played ability card is in a board recess.
        ///
        /// ROOT CAUSE of "wenn man mit dem Kopf wackelt, wackelt die Karte auch etwas und zieht nach":
        /// the clipped chip stayed parented to the ITEMS-FAN root, and that root BILLBOARDS to the head
        /// every frame (ItemsPile.Tick rewrites _root.rotation from the head direction). The slot,
        /// meanwhile, is bolted to the board. So the owner re-derived the slot pose in fan-root local
        /// space each tick and the chip chased it with an exponential lerp — a target that jumped with
        /// every head movement, followed by something that only ever converges asymptotically. The card
        /// could not help but swim behind the head.
        ///
        /// Re-parenting removes the chase entirely instead of tuning it: once the chip IS a child of the
        /// slot, Unity's transform hierarchy holds it there for free, at zero cost and with no residual
        /// error, however the head or the board moves. The world SIZE is preserved across the re-parent
        /// by dividing the fan's world scale out of the chip's local scale, since the two parents sit at
        /// different points in the board's scale chain.
        /// </summary>
        internal void ClipIntoSlot(Transform slot, Transform fanRoot, float fanLocalScale)
        {
            if (slot == null)
                return;
            _releaseGlide = 0f; // and no glide may fight the parent
            transform.SetParent(slot, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            float fan = fanRoot != null ? fanRoot.lossyScale.x : 1f;
            float host = slot.lossyScale.x;
            float ratio = host > 1e-5f ? fan / host : 1f;
            transform.localScale = Vector3.one * (fanLocalScale * ratio);
        }

        /// <summary>
        /// Requirement 6 — take the chip back OUT of the slot hierarchy and hand it to
        /// <paramref name="fanRoot"/> again, KEEPING its current world pose so nothing jumps. Every exit
        /// from the pending state goes through this (cancel, invalidation, confirm), so the chip's
        /// fan-local home pose, glide-home and use-flourish all run in the frame they were written for.
        /// </summary>
        internal void UnclipFromSlot(Transform? fanRoot)
        {
            if (fanRoot == null || transform.parent == fanRoot)
                return;
            transform.SetParent(fanRoot, worldPositionStays: true);
        }

        /// <summary>Requirement 6 — cancel the post-release glide-home (used when a drop CLIPS into the
        /// use slot instead of returning to the fan).</summary>
        internal void CancelReleaseGlide() => _releaseGlide = 0f;

        /// <summary>Requirement 6 — return the chip to its fan home (the "return to deck" path on cancel):
        /// clear the clip state and start the same glide the post-release home uses.</summary>
        internal void ReturnToFan()
        {
            PendingUse = false;
            _releaseGlide = ReleaseGlideSeconds; // Update's home-glide flies it back to the arc slot
        }

        /// <summary>
        /// Requirement 6 — after a CONFIRM: play the result flourish (burn plume for Consumed, a "tap"
        /// roll to 90° with a scale pulse for Spent), then collapse into the deck. The owner has already
        /// detached the chip from the fan root and removed it from the live list, so this runs to
        /// completion even if the fan closes/rebuilds.
        /// </summary>
        internal void PlayUseThenCollapse(bool consumed, bool spent, Vector3 collapseWorld)
        {
            PendingUse = false;
            _useFxActive = true;
            _useFxTime = UseFxSeconds;
            _useFxSpent = spent && !consumed;
            _useFxCollapseWorld = collapseWorld;
            _useFxBaseRot = transform.localRotation;
            _fingerPopped = false;
            _laserPopped = false;
            if (_box != null)
                _box.enabled = false; // no grabbing during the flourish
            // Consumed flourish = a brief hold on the card's OWN state FX (the game's ItemCardEffects
            // burn sweep + its card-bounded smoke, kept live by TryHostRealCard) over the ashen tint,
            // before the card collapses back into the deck. No extra CardSmoke prefab is spawned here —
            // that separate, screen-card-authored plume was the field-covering fog.
        }

        /// <summary>Requirement 6 — advance the post-confirm flourish, then hand off to the collapse.</summary>
        private void TickUseFx()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _useFxTime -= dt;
            float k = 1f - Mathf.Exp(-14f * dt);
            if (_useFxSpent)
            {
                // "Tap": roll the card to 90° (the game's tapped look) with a brief scale pulse accent.
                Quaternion target = _useFxBaseRot * Quaternion.Euler(0f, 0f, 90f);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, target, k);
                float prog = 1f - Mathf.Clamp01(_useFxTime / UseFxSeconds);
                float pulse = 1f + 0.14f * Mathf.Sin(prog * Mathf.PI);
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * (_homeScale * pulse), k);
            }
            // Consumed: the card's own burn FX plays over the flourish window; no extra motion.
            if (_useFxTime <= 0f)
            {
                _useFxActive = false;
                BeginCollapse(_useFxCollapseWorld);
            }
        }

        /// <summary>Seat the chip in its arc slot (its "home" the base restores it to on release).</summary>
        internal void SetHome(Vector3 pos, Quaternion rot, float scale)
        {
            _homePos = pos;
            _homeRot = rot;
            _homeScale = scale;
            if (Holder != null)
                return; // held — the base restores this home on release
            transform.localPosition = pos;
            transform.localRotation = rot;
            transform.localScale = Vector3.one * scale;
        }

        // FIX 1 — take-into-hand reading pose: pinched between thumb and index, the card CENTER sitting
        // (0.5 − PinchGripFraction)·cardH above the pinch along the card up-axis, at InspectScale. This
        // is VRCard.GetHeldPose verbatim, except cardH is the ITEM card's own near-square held height
        // (_faceHeight at held scale) so the pinch grips the right spot on a near-square card. The base
        // snap seats this the instant the chip is grabbed; TickHeldPose then billboards the face.
        protected override HeldPose GetHeldPose(VRHand hand)
        {
            float scale = CardsConfig.InspectScale.Value;
            // Item cards are near-square — use the chip's OWN measured held height (not the tall ability
            // CardHeight) so the grip offset lifts the card the right amount out of the pinch.
            float cardH = (_faceHeight > 0.001f ? _faceHeight : CardsConfig.CardHeight) * scale;

            // GrabAnchor frame: +Y out of the palm, +Z along the fingers, ±X thumb side.
            float bias = CardsConfig.HeldFaceBias.Value * Mathf.Deg2Rad;
            var faceNormal = new Vector3(0f, Mathf.Cos(bias), -Mathf.Sin(bias));
            float thumbSide = hand.Side == HandSide.Right ? 1f : -1f;
            // Card +Z (away from the viewer) = −faceNormal; card top (+Y) = thumb side.
            var rot = Quaternion.LookRotation(-faceNormal, new Vector3(thumbSide, 0f, 0f));

            Vector3 pinchLocal;
            FingerJoints thumb = hand.Rig.GetFinger(Finger.Thumb);
            FingerJoints index = hand.Rig.GetFinger(Finger.Index);
            if (thumb.IsValid && index.IsValid)
            {
                Vector3 pinchWorld = (thumb.Tip.position + index.Tip.position) * 0.5f;
                pinchLocal = hand.Rig.GrabAnchor.InverseTransformPoint(pinchWorld);
            }
            else
            {
                pinchLocal = new Vector3(0f, CardsConfig.HeldOffPalm.Value, CardsConfig.HeldForward.Value);
            }
            pinchLocal += CardsConfig.HeldPinchOffset.Value;

            Vector3 pos = pinchLocal + rot * new Vector3(0f, cardH * (0.5f - PinchGripFraction), 0f);
            return new HeldPose(pos, rot, scale);
        }

        public override void OnGrab(VRHand hand)
        {
            // Leave the use slot BEFORE the base records the pre-grab parent. A clipped chip is a
            // CHILD of the slot (ClipIntoSlot), and base.OnRelease restores exactly the parent it saw
            // here — so grabbing a clipped card and dropping it elsewhere would have put it back under
            // the SLOT while its glide-home target (_homePos) is fan-root local. Handing it to the fan
            // root first (world pose preserved) keeps the whole grab/release path in one frame of
            // reference, so a cancel really does return the card to the deck.
            _owner?.UnclipChip(this);
            // Drop the pop so the grabbed chip starts from a clean pose.
            _fingerPopped = false;
            _laserPopped = false;
            _pop = 0f;
            // A chip in the hand (or clipped into the use slot) is no longer part of the arc: give
            // it its FULL grab box back so pulling it out of the slot again stays easy, and clear
            // any sweep suppression it was carrying. Relayout re-strips it when it rejoins the arc.
            SetGrabStrip(float.MaxValue);
            SetHandSuppressed(null);
            // FIX 1 — keep the world pose across the base re-parent so TickHeldPose flies the chip from
            // its fan slot INTO the hand instead of teleporting (mirror of VRCard.OnGrab). The base snap
            // seats GetHeldPose; we capture that local target, then restore the pre-grab world pose and
            // let TickHeldPose ease in.
            Vector3 worldPos = transform.position;
            Quaternion worldRot = transform.rotation;
            Vector3 worldScale = transform.localScale;
            base.OnGrab(hand); // snaps to the reading pose at GetHeldPose
            _heldPos = transform.localPosition;
            _heldRot = transform.localRotation;
            _heldScale = transform.localScale.x;
            transform.position = worldPos;
            transform.rotation = worldRot;
            transform.localScale = worldScale;
            _releaseGlide = 0f; // re-grab mid-glide: the held pose takes over cleanly (FIX 2)
            hand.SendHaptic(HapticPreset.ClickPulse);
            // ITEM 4 (card sounds): the SAME pluck/grab SFX ability cards play on grab (laser-pluck
            // routes through OnPoke → ForceGrab → OnGrab, so it fires there too — no double sound).
            CardsDriver.PlayCardSound(CardsConfig.CardGrabSound.Value, transform);
            VRLog.Info("Cards", $"Item chip '{name}' taken into hand ({hand.Side}) — readable (state {State}).");
        }

        /// <summary>
        /// Requirement 3 (clip-in to use): capture the drop location BEFORE the base restores the
        /// chip to its fan home, then offer it to the board's item-use slot. The base restore always
        /// runs, so a chip dropped anywhere else (or a non-activatable chip) simply returns to the fan.
        /// </summary>
        public override void OnRelease(VRHand hand, Vector3 velocity)
        {
            // While held the chip sits at the hand's grab anchor, so its world position IS the drop
            // point. Capture it before base.OnRelease re-parents it back to the fan.
            Vector3 dropWorldPos = transform.position;
            // FIX 2 — glide back, don't snap: base.OnRelease restores the pre-grab LOCAL pose (an
            // instant teleport to the fan slot). Mirror VRCard.OnRelease — keep the world pose across
            // the re-parent so the chip stays at the release point, then Update's home-lerp flies it
            // back to its fan home over ReleaseGlideSeconds (unscaled, so it plays even while paused).
            Quaternion worldRot = transform.rotation;
            Vector3 worldScale = transform.localScale;
            base.OnRelease(hand, velocity); // detach + restore fan home (pre-grab local pose)
            transform.position = dropWorldPos;
            transform.rotation = worldRot;
            transform.localScale = worldScale;
            _releaseGlide = ReleaseGlideSeconds;
            _owner?.OnChipReleased(this, dropWorldPos, hand);
            // Re-derive the arc's collider strips: this chip carried a FULL grab box while it was
            // in the hand (see OnGrab) and is about to glide back into the arc, where a full box
            // would overlap its neighbours again and re-break the sweep for exactly one card.
            _owner?.RefreshFanLayout();
            // Card sounds: a release that CLIPS into the use slot plays the place "thunk" (fired in
            // OnChipReleased, which sets PendingUse). A release that merely glides back to the fan
            // plays NOTHING — deliberately.
            //
            // It used to play the soft take-back click here, on the claim that this matched the
            // ability cards' SFX set. It did not: CardsDriver only plays that click when a card is
            // pulled back off the FIELD during a pick reopen, precisely because the game itself is
            // silent in that one case. An ability card released back into the fan makes no mod sound
            // at all. So the item chip was the noisier of the two, which is exactly what the user
            // objected to ("die Item-Karten sollten die selben und nicht mehr Geräusche machen als
            // die anderen Karten auch"). Grab and place remain, and now match one-for-one.
        }

        // ---- pop (readability) -----------------------------------------------------

        /// <summary>
        /// ITEM #1 + #7 — per-frame face upkeep, run in every state (held, popped, settled, glide):
        /// (1) re-run the mip-bake sprite swap on a slow cadence until the async background art has
        /// baked (mirror of <c>CardFace.Maintain</c>'s <see cref="MipRescanInterval"/> cadence — async
        /// arrivals / state changes keep putting the mipless originals back), and (2) LIVE re-evaluate
        /// the playable gate from <see cref="IsActivatable"/> so exactly the cards that CAN be used
        /// right now wear the soft gold frame and every other card stays untouched. Usability changes with
        /// turn/phase, so it is polled every frame, never cached. Change-gated (both are no-ops unless
        /// due), so the per-frame cost is a clock compare + a bool compare.
        /// </summary>
        private void TickFaceMaintenance()
        {
            // ITEM #5 — keep the WorldSpace face canvas bound to the head camera (mirror of
            // VRCard.UpdateCanvasCamera). A WorldSpace canvas renders on its LAYER regardless of
            // worldCamera, so this alone can't decide mirror visibility — the deep diagnostic below logs
            // exactly what the item face is (layer, canvas modes, which cameras would draw it) so the next
            // hardware log pinpoints why a held item face is/ isn't in the desktop mirror vs an ability face.
            if (_faceCanvas != null)
            {
                Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
                if (head != null && _faceCanvas.worldCamera != head)
                    _faceCanvas.worldCamera = head;
                if (!s_loggedMirror && head != null)
                    LogMirrorDiag(head);
            }

            // ITEM #1 (de-shimmer immediately) — tight per-frame poll for the async background art; the
            // instant the hosted card's cardBackground sprite is present, Rescan so the mip-baked face
            // lands with ZERO shimmer window (instead of up to ~1.5 s later on the slow cadence).
            if (!_artBaked && _cardUI != null && Time.unscaledTime <= _tightArtPollUntil)
            {
                Image? bg = _cardUI.cardBackground;
                if (bg != null && bg.sprite != null)
                {
                    _artBaked = true;
                    CardFaceMipBake.Rescan(_cardUI); // art just arrived — bake it NOW
                    _nextMipRescan = Time.unscaledTime + MipRescanInterval;
                    if (!s_loggedArtBaked)
                    {
                        s_loggedArtBaked = true;
                        VRLog.Info("Cards", "ITEM #1: background art detected on host — mip-baked immediately " +
                                            "(tight per-frame poll), so the item card no longer shimmers for ~1.5 s.");
                    }
                }
            }

            // Ongoing maintenance cadence (async re-arrivals / game sprite reassignments put mipless
            // originals back — swap them for the baked copies again), same 1 s cadence as CardFace.
            if (_cardUI != null && Time.unscaledTime >= _nextMipRescan)
            {
                _nextMipRescan = Time.unscaledTime + MipRescanInterval;
                CardFaceMipBake.Rescan(_cardUI);
            }

            // USABLE HIGHLIGHT (live) — light the soft gold FRAME when this card CAN be played RIGHT NOW.
            // Turn-aware: the owner gates on IsActionTurn AND IsActivatable, so off-turn nothing glows
            // and on-turn exactly the playable items do. Polled every frame (usability moves with
            // turn/phase and with every item spent) but change-gated, so the steady-state cost is one
            // bool compare. Non-usable cards get NO treatment at all — they simply keep their natural
            // look, which is the whole point of flipping the cue from "dim the rest" to "light these".
            int want = (_owner != null && _owner.CanUseNow(this)) ? 1 : 0;
            if (want != _usabilityShown)
            {
                _usabilityShown = want;
                if (_usableFrame != null && _usableFrame.activeSelf != (want == 1))
                    _usableFrame.SetActive(want == 1); // frame ON only while usable
            }
        }

        private static bool s_loggedArtBaked;

        /// <summary>
        /// ITEM #5 (desktop-mirror investigation) — one-shot deep diagnostic: dump exactly what the hosted
        /// item face is so the hardware log can compare it to a held ABILITY face. Logs the item card's
        /// render layer + our FaceCanvas mode/camera, every nested Canvas on the hosted ItemCardUI (mode /
        /// worldCamera / overrideSorting — a nested Screen-Space canvas would render only through its own
        /// camera and never reach the head mirror), and every enabled Camera whose cullingMask includes the
        /// item's layer (so the log reveals whether the head camera — and any separate avatar/desktop mirror
        /// camera — actually draws it). No behaviour change; pure evidence.
        /// </summary>
        private void LogMirrorDiag(Camera head)
        {
            s_loggedMirror = true;
            try
            {
                int layer = _cardGo != null ? _cardGo.layer : -1;
                var sb = new StringBuilder(256);
                sb.Append("ITEM #5 MIRROR DIAG: item face layer=").Append(layer)
                  .Append(" ('").Append(layer >= 0 ? LayerMask.LayerToName(layer) : "?").Append("'), FaceCanvas ")
                  .Append(_faceCanvas != null ? _faceCanvas.renderMode.ToString() : "null")
                  .Append(" worldCam=").Append(_faceCanvas != null && _faceCanvas.worldCamera != null ? _faceCanvas.worldCamera.name : "null")
                  .Append("; head '").Append(head.name).Append("' mask 0x").Append(head.cullingMask.ToString("X8"))
                  .Append(" rendersItemLayer=").Append(layer >= 0 && (head.cullingMask & (1 << layer)) != 0);

                if (_cardGo != null)
                {
                    Canvas[] nested = _cardGo.GetComponentsInChildren<Canvas>(true);
                    sb.Append("; nested canvases=").Append(nested.Length);
                    for (int i = 0; i < nested.Length && i < 4; i++)
                    {
                        Canvas c = nested[i];
                        if (c == null || ReferenceEquals(c, _faceCanvas))
                            continue;
                        sb.Append(" [").Append(c.name).Append(':').Append(c.renderMode)
                          .Append(c.overrideSorting ? " override" : "")
                          .Append(" cam=").Append(c.worldCamera != null ? c.worldCamera.name : "null").Append(']');
                    }
                }

                int drawers = 0;
                if (layer >= 0)
                {
                    Camera[] cams = Camera.allCameras;
                    for (int i = 0; i < cams.Length; i++)
                        if (cams[i] != null && (cams[i].cullingMask & (1 << layer)) != 0)
                        {
                            drawers++;
                            sb.Append("; draws:'").Append(cams[i].name).Append('\'');
                        }
                }
                sb.Append(" (").Append(drawers).Append(" camera(s) render the item layer). " +
                          "If only the head camera draws it, the desktop mirror IS the head mirror and the item " +
                          "SHOULD be visible; if a separate avatar/desktop-mirror camera exists but is absent here, " +
                          "its cullingMask excludes the item layer — that is the miss.");
                VRLog.Info("Cards", sb.ToString());
            }
            catch (System.Exception e)
            {
                VRLog.Warn("Cards", $"ITEM #5 mirror diag skipped ({e.Message}).");
            }
        }

        /// <summary>Fingertip hand-sweep pop (set by the owner's single-winner sweep).</summary>
        internal void SetFingertipPop(bool on) => _fingerPopped = on;

        /// <summary>
        /// True while this chip is the one singled out in the fan — the ITEM-fan counterpart of
        /// <c>VRCard.IsHighlighted</c>, and the predicate the MULTIPLAYER hover channel reads
        /// through <see cref="ItemsPile.HighlightedIndex"/>.
        ///
        /// <para>MP test 2026-08-07 ("Beim Hovern mit dem Laser über eine Pile/Item-Karte wird das
        /// Highlight nicht synchronisiert; mit der Hand schon"). Both pop paths write the same
        /// visual state — <c>Tick</c> tweens on <c>_fingerPopped || _laserPopped</c> — but only the
        /// FINGERTIP one had a public reader, so extension record 6 carried the hand sweep and
        /// nothing else. Covering both here is exactly what <c>VRCard.IsHighlighted</c> already
        /// does for the pile browser and the hand fan, which is why THOSE synced on the laser.</para>
        ///
        /// <para>A held chip and a chip clipped into the use slot are excluded: neither is at a fan
        /// position any more, so neither has an index a peer could mirror.</para>
        /// </summary>
        internal bool IsHighlighted => Holder == null && !PendingUse && (_fingerPopped || _laserPopped);

        /// <summary>True while the dominant index tip / palm is within this chip's collider reach.</summary>
        internal bool TryFingertipDistance(Vector3 worldPoint, out float distance)
        {
            distance = float.MaxValue;
            if (_box == null || !_box.enabled || !_box.gameObject.activeInHierarchy)
                return false;
            distance = Vector3.Distance(worldPoint, _box.ClosestPoint(worldPoint));
            return true;
        }

        // ---- single-winner grab gate (mirrors VRCard._handPopSuppressed / AllowsHand) ----

        /// <summary>The hand this chip refuses because the owner's sweep gave that hand a
        /// DIFFERENT winner (null = refuses nobody). Per-hand, not a static: the item fan sweeps
        /// both hands and each hand must keep its own independent candidate.</summary>
        private VRHand? _suppressedForHand;

        /// <summary>Set by <see cref="ItemsPile.UpdateHandSweep"/>: this chip lost the election for
        /// <paramref name="hand"/> and must stay out of that hand's proximity grab, so the chip
        /// that POPPED is the chip the trigger takes ("what lights up is what I get").</summary>
        internal void SetHandSuppressed(VRHand? hand) => _suppressedForHand = hand;

        /// <summary>Per-hand grab/hover gate (<see cref="IGrabbableHandFilter"/>): a chip that lost
        /// the hand sweep is invisible to that hand's <c>ProximityGrabber</c> — no highlight, no
        /// grab, no haptic. The laser path is untouched (it arbitrates itself).</summary>
        public bool AllowsHand(VRHand hand) => !ReferenceEquals(hand, _suppressedForHand);

        /// <summary>
        /// Shrink this chip's grab box to its VISIBLE strip in FAN-local metres (see
        /// <see cref="FanSweep.StripWidth"/>), or <see cref="float.MaxValue"/> for the full face.
        /// The strip is measured ALONG THE ARC, which for a SPENT chip — rolled 90° in its slot so
        /// it reads as "tapped" — is the box's local Y, not X. Getting that axis wrong would leave
        /// a tapped chip with a collider covering both its neighbours, i.e. exactly the overlap
        /// this is here to remove. Idempotent: an unchanged box is not rewritten (a per-frame
        /// physics-shape dirty for every chip is the cost this guard avoids).
        /// </summary>
        internal void SetGrabStrip(float stripFanLocal)
        {
            if (_box == null)
                return;
            float fullW = FaceWidth + ColliderMargin;
            float fullH = FaceHeight + ColliderMargin;
            bool tapped = State == Visual.Spent; // rolled 90°: the arc runs along local Y
            float along = tapped ? fullH : fullW;
            float strip = Mathf.Clamp(stripFanLocal, along * 0.25f, along);
            float offset = -(along - strip) * 0.5f;
            Vector3 size = tapped
                ? new Vector3(fullW, strip, ColliderDepth)
                : new Vector3(strip, fullH, ColliderDepth);
            Vector3 center = tapped ? new Vector3(0f, offset, 0f) : new Vector3(offset, 0f, 0f);
            if (_box.size == size && _box.center == center)
                return;
            _box.size = size;
            _box.center = center;
        }

        // ---- IFanSweepTarget (explicit: no widening of the chip's surface) ----

        /// <summary>A held chip rides a hand and a chip clipped into the use slot is awaiting a
        /// decision (#6) — neither may win the sweep, or a dead chip would suppress the lift of a
        /// live one beside it.</summary>
        bool IFanSweepTarget.SweepEligible => Holder == null && !PendingUse;

        /// <summary>The chip's rendered face width in WORLD units. This is the number that makes
        /// the reach board-scale-invariant: an item fan on a 0,32× board reports ~2,5 real cm here
        /// where the same fan on the 0,80× default reports ~6,3, and the reach follows.</summary>
        float IFanSweepTarget.SweepFaceWidthWorld => FaceWidth * transform.lossyScale.x;

        bool IFanSweepTarget.TrySweepDistance(Vector3 worldPoint, out float distance)
            => TryFingertipDistance(worldPoint, out distance);

        string IFanSweepTarget.SweepName => name;

        private void Update()
        {
            if (_collapsing)
            {
                // Req #5 — self-glide into the pile, then destroy (OnDisable recycles the card widget).
                float cdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                _collapseTime -= cdt;
                float ct = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * cdt);
                transform.position = Vector3.Lerp(transform.position, _collapseWorld, ct);
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * (_homeScale * 0.2f), ct);
                if (_collapseTime <= 0f)
                    Object.Destroy(gameObject);
                return;
            }

            if (_useFxActive) // Req #6 — post-confirm burn/tap flourish, then collapse into the deck
            {
                TickUseFx();
                return;
            }

            TickFaceMaintenance(); // ITEM #1 (de-shimmer) + live usable-highlight frame — held or not

            if (Holder != null)
            {
                TickHeldPose(); // FIX 1 — track the wrist + billboard the face every frame while held
                return;
            }

            // Req #6 — clipped into the use slot, waiting for the decision: NOTHING to do. The chip is
            // a child of the slot with an exact zero local pose (ClipIntoSlot), so it is held there by
            // the transform hierarchy — rigid, free, and with no residual error. Any per-frame pose
            // work here would be the swim-behind-the-head bug coming back.
            if (PendingUse)
                return;

            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f); // unscaled: pop/glide play while paused
            bool popped = _fingerPopped || _laserPopped;
            _pop = Mathf.MoveTowards(_pop, popped ? 1f : 0f, PopLerpSpeed * udt);
            // Pop applied on top of the arc home: grow a touch + nudge toward the viewer (local -Z).
            Vector3 posTarget = _homePos + new Vector3(0f, 0f, -PopLift * _pop);
            float scaleTarget = _homeScale * (1f + (PopScale - 1f) * _pop);

            if (_releaseGlide > 0f)
            {
                // FIX 2 — post-release glide: exponential ease toward the fan home (position + rotation
                // + scale) at CardLerpSpeed on unscaled time, so a released chip flies back to its slot
                // instead of snapping. The window converges ~99 % by ReleaseGlideSeconds; the tiny
                // residual settle below is imperceptible (same easing/feel as VRCard's home-lerp).
                _releaseGlide -= udt;
                float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * udt);
                transform.localPosition = Vector3.Lerp(transform.localPosition, posTarget, t);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, _homeRot, t);
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * scaleTarget, t);
            }
            else
            {
                // Settled: apply the pop directly on the arc home (unchanged steady-state behavior).
                transform.localScale = Vector3.one * scaleTarget;
                transform.localPosition = posTarget;
            }
        }

        /// <summary>
        /// FIX 1 — fly-in / read-in-hand pose: the chip sits at the pinch point (localPosition lerped in
        /// GrabAnchor-local space, so it tracks the wrist 1:1) but per-frame BILLBOARDS its face to the
        /// head, and lerps scale — VRCard.TickHeldPose verbatim, so a held item card is at the same
        /// place/orientation as a held ability card (at the pinch, always facing the player).
        /// </summary>
        private void TickHeldPose()
        {
            float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * 1.5f * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, _heldPos, t);
            Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
            if (head != null)
            {
                Vector3 away = transform.position - head.transform.position; // card +Z away from viewer
                if (away.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(away.normalized, head.transform.up), t);
            }
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _heldScale, t);
        }

        // ---- IPokeable (laser hover + pluck; the board laser drives these) ----------

        public void OnPokeEnter(VRHand hand)
        {
            if (Holder != null)
                return;
            _laserPopped = true;
            hand.SendHaptic(HapticPreset.HoverTick);
        }

        public void OnPokeExit(VRHand hand) => _laserPopped = false;

        public void OnPoke(VRHand hand)
        {
            // Laser trigger (or fingertip poke) on the chip → pluck it into the hand to read,
            // exactly like the ability-card browse laser (ForceGrab, released on trigger-up).
            if (Holder != null || !CanGrab)
                return;
            _laserPopped = false;
            // An EXPLICIT pluck overrides the hand sweep's arbitration for this chip. Without this
            // the pull-jerk grace path could hand ForceGrab a chip the sweep had meanwhile
            // suppressed for that very hand (AllowsHand=false → ForceGrab refuses), turning a
            // promised pluck into a silent refusal. The sweep re-derives its set next tick anyway.
            SetHandSuppressed(null);
            hand.Grabber.ForceGrab(this, releaseOnTriggerUp: true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            // (No laser-target unregister: chips are picked geometrically by the driver's item-fan
            // laser path now, never through PlayTray.LaserTargets — see Create's laser note.)
            // Undo the emitter clamp BEFORE anything else touches the hosted card, so the widget the
            // pool gets back is byte-for-byte the one it handed out even if the recycle below fails.
            RestoreCardEffectSmoke();
            // Return the hosted ItemCardUI to the game's pool BEFORE this chip is destroyed (recycle
            // reparents it under the pool, so it survives the chip teardown and its art is unloaded).
            if (_cardGo != null && _cardUI != null)
            {
                try
                {
                    // ITEM #1 — leave the pooled widget CLEAN: restore the mip-swapped sprites before the
                    // game reuses this card elsewhere. (The usable highlight is a mod-owned frame canvas,
                    // destroyed with the chip below — it never touches the game card, nothing to reset.)
                    CardFaceMipBake.RestoreSprites(_cardUI);
                    ObjectPool.RecycleCard(_cardUI.CardID, ObjectPool.ECardType.Item, _cardGo);
                }
                catch (System.Exception e)
                {
                    VRLog.Warn("Cards", $"ITEM CARD recycle failed ({e.Message}).");
                }
            }
            _cardGo = null;
            _cardUI = null;
            _faceCanvas = null;
            _usableFrame = null; // child of the chip GameObject — destroyed with it
            _usabilityShown = -1;
        }

        // ---- state → look ----------------------------------------------------------

        private static Visual Classify(CItem item)
        {
            switch (item.SlotState)
            {
                case CItem.EItemSlotState.Consumed:
                    return Visual.Consumed;
                case CItem.EItemSlotState.Spent:
                    return Visual.Spent;
                default:
                    return Visual.Ready;
            }
        }

        private static Color FaceColor(Visual state) => state switch
        {
            Visual.Consumed => new Color(0.22f, 0.19f, 0.17f), // ashen (burnt)
            Visual.Spent => new Color(0.34f, 0.40f, 0.52f),    // cool "tapped/spent" blue-grey
            _ => new Color(0.80f, 0.72f, 0.52f),               // parchment (ready)
        };

        private static string Name(CItem item)
        {
            // item.YMLData.Name is a LOCALIZATION KEY — resolve it like the flat game
            // (ItemCardUI.CreateCard: LocalizationManager.GetTranslation) via the mod's Loc helper.
            string? key = item != null && item.YMLData != null ? item.YMLData.Name : null;
            if (string.IsNullOrEmpty(key))
                return "?";
            return Core.Loc.Game(key!, key!);
        }
    }
}
