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
/// burnt stacks (poke to toggle a fixed reading wall above the board, pinch-grab to
/// raise it as a hand-held fan). Content is <see cref="CItem"/> read live from
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
/// <see cref="PileBrowser"/>), dominant-hand LASER hover+pluck (registered as a
/// <see cref="PlayTray"/> laser target → the board laser pops on hover / plucks on
/// trigger), pinch-GRAB, and read-in-hand (snap upright + enlarged).</item>
/// <item>USE = a CLIP-IN slot (was: a floating drop pad computed once). The slot is board
/// furniture built by <see cref="PlayTray"/> UNDER the board next to Confirm/Undo
/// (per-board <c>ItemUseSlotOffset</c>, debug-menu tunable). Its visibility is
/// re-evaluated LIVE every tick: shown ONLY while it is the local character's own turn
/// (<see cref="CardsGameApi.IsActionTurn"/>) AND a held item card's live
/// <c>SlotState</c> is usable. Dropping that card into the slot calls
/// <c>new UseItemService(hand.PlayerActor).UseItem(cItem)</c> (which owns ALL multiplayer
/// sync + re-validates the item).</item>
/// </list>
/// State → look: CONSUMED → ashen + the game's burn plume (<see cref="BurnCardFx"/>) and
/// the card's own consumed FX; SPENT → rolled 90° ("tapped") in the fan + the card's spent
/// FX; otherwise upright. A chip taken INTO the hand snaps upright + enlarged so it always
/// reads. Layout mirrors <see cref="PileBrowser"/>; open/close is driven by
/// <see cref="PileViewer"/>.
/// </summary>
internal sealed class ItemsPile
{
    // Arc geometry — same family as PileBrowser (reading, not picking).
    private const float RadiusFactor = 1.7f;
    private const float MaxArcDegrees = 110f;
    private const float MaxStepDegrees = 10f;
    private const float ChipScale = 1.25f;
    private const float ZStagger = 0.004f;
    private const float HandPalmOffset = 0.16f;

    // Shared board-top anchor (requirement 2): the poke-toggle item fan opens at the SAME
    // spot above the control board as the discard/burnt PileBrowser (mirror of
    // PileBrowser.BoardFloatHeight / BoardFloatProudZ / BoardAnchorBase). The root parents
    // under the board root (PlayTray.Current.Root) so it inherits the board's live pose + scale.
    private const float BoardFloatHeight = 0.26f;
    private const float BoardFloatProudZ = -0.05f;
    private static Vector3 BoardAnchorBase =>
        new(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);

    // Physical hand-sweep reach (mirror of PileBrowser.ContactTipReach/PalmReach/StickyMargin,
    // scale-1 metres): a chip is a candidate when the dominant index tip is within TipReach OR
    // the palm within PalmReach; the winner is the nearest by index tip alone, with a small
    // incumbent hysteresis so the lift never flutters at a card boundary.
    private const float ContactTipReach = 0.035f;
    private const float ContactPalmReach = 0.13f;
    private const float ContactStickyMargin = 0.02f;

    /// <summary>Drop-into-use capture radius (world metres at board scale 1; scaled by the slot's live scale).</summary>
    private const float UseSlotRadius = 0.13f;

    private readonly List<ItemChip> _chips = new(12);
    private Transform? _root;
    private TextMeshPro? _title;
    private Transform? _anchor;   // the pile mount (rig-space, diorama-scaled) — placement scale ref
    private VRHand? _followHand;
    private CardsHandUI? _hand;
    private string _signature = string.Empty; // last-built inventory state, for cheap live refresh
    private bool _boardAnchored; // poke-toggle fan parented under the board root (mirrors PileBrowser)

    // Hand-sweep single-winner state: the chip the physical hand currently lifts (null = none).
    private ItemChip? _handWinner;
    private float _nextHandLogAt;

    // Diagnostics dedup for the live USE-slot gate log ("ITEM USE SLOT: shown/hidden …").
    private bool _useSlotShownLogged;

    internal bool IsOpen { get; private set; }
    internal bool IsHandHeld => _followHand != null;

    // ------------------------------------------------------------------ config --

    /// <summary>The pile mount PileViewer built the item stack under (placement reference).</summary>
    internal void SetAnchor(Transform anchor) => _anchor = anchor;

    /// <summary>Item count of the acting character's inventory (drives the board stack look).</summary>
    internal int Count(CardsHandUI? hand)
    {
        List<CItem>? items = ItemsOf(hand);
        return items != null ? items.Count : 0;
    }

    /// <summary>The acting character's equipped items (null-safe; never mutated).</summary>
    private static List<CItem>? ItemsOf(CardsHandUI? hand)
    {
        CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
        CInventory? inv = actor != null ? actor.Inventory : null;
        return inv != null ? inv.AllItems : null;
    }

    // ------------------------------------------------------------------ open/close --

    /// <summary>Poke-toggle: open at a fixed reading wall above the board, or close if already open.</summary>
    internal void TogglePoke(CardsHandUI hand, VRHand vrHand)
    {
        if (IsOpen && _followHand == null)
        {
            Close();
            return;
        }
        Open(hand, followHand: null);
    }

    /// <summary>Pinch-grab: open (or re-pin) as a hand-held reading fan following the grabbing hand.</summary>
    internal void OpenHeld(CardsHandUI hand, VRHand vrHand) => Open(hand, followHand: vrHand);

    /// <summary>Grip released: dismiss a held fan (a poke-toggled wall stays up).</summary>
    internal void ReleaseHeld(VRHand vrHand)
    {
        if (_followHand != null)
            Close();
    }

    private void Open(CardsHandUI hand, VRHand? followHand)
    {
        _hand = hand;
        EnsureRoot();
        _followHand = followHand;
        // Requirement 2: the poke-toggle fan anchors under the board root at the shared
        // board-top spot; a held (grabbed) fan follows the grabbing hand.
        Transform? boardRoot = followHand == null ? PlayTray.Current?.Root : null;
        _boardAnchored = boardRoot != null;
        Transform parent = followHand != null ? followHand.Rig.PalmCenter
                         : boardRoot != null ? boardRoot
                         : (_anchor != null ? _anchor : _root!.parent);
        if (parent != null && _root!.parent != parent)
            _root.SetParent(parent, worldPositionStays: false);
        _root!.gameObject.SetActive(true);
        IsOpen = true;
        _signature = string.Empty; // force a build
        Populate(hand);
        if (followHand != null)
            Tick(hand); // seat by the holding hand immediately
        else if (boardRoot != null)
            PlaceAboveBoard(); // float above the board, inherit its scale (mirror PileBrowser)
        else
            PlaceAtHead(); // no board — head-relative fallback
        VRLog.Info("Cards", $"Items pile browse OPEN ({(followHand != null ? "held in hand" : boardRoot != null ? "board-anchored" : "toggled")}, " +
                            $"{_chips.Count} item(s)).");
    }

    internal void Close()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        _followHand = null;
        _boardAnchored = false;
        ClearHandSweep();
        PlayTray.Current?.SetItemUseSlotVisible(false); // never leave the use slot up once the fan is gone
        _useSlotShownLogged = false;
        ClearChips();
        _signature = string.Empty;
        if (_root != null)
            _root.gameObject.SetActive(false);
        VRLog.Info("Cards", "Items pile browse CLOSE.");
    }

    internal void Destroy()
    {
        ClearHandSweep();
        ClearChips();
        IsOpen = false;
        _followHand = null;
        _boardAnchored = false;
        _hand = null;
        PlayTray.Current?.SetItemUseSlotVisible(false);
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

        // Live refresh: rebuild only when nothing is held and the inventory state moved.
        if (!AnyHeld())
        {
            string sig = Signature(hand);
            if (sig != _signature)
                Populate(hand);
        }

        // Physical fingertip sweep: lift the chip nearest the dominant index tip (single-winner).
        UpdateHandSweep();

        // Requirement 3 (LIVE gate, re-evaluated every tick): the USE clip-in slot shows ONLY
        // while (a) it is this hand's own action turn AND (b) a HELD item card's live SlotState
        // is usable. Poll the held chip's live SlotState here (never cache it at create time).
        bool turn = CardsGameApi.IsActionTurn(hand);
        ItemChip? heldUsable = HeldActivatableChip();
        bool showUseSlot = turn && heldUsable != null;
        PlayTray.Current?.SetItemUseSlotVisible(showUseSlot);
        if (showUseSlot != _useSlotShownLogged)
        {
            _useSlotShownLogged = showUseSlot;
            Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
            Vector3 pos = slot != null ? slot.position : Vector3.zero;
            VRLog.Info("Cards", $"ITEM USE SLOT: {(showUseSlot ? "shown" : "hidden")} " +
                                $"(turn={turn} heldUsable={(heldUsable != null)}) at ({pos.x:F2},{pos.y:F2},{pos.z:F2}).");
        }

        // Fan facing/position: HELD → float above the palm; BOARD-ANCHORED → re-read the shared
        // board anchor (+ live BrowseFanOffset). Either way billboard toward the head (ISSUE #7).
        if (_root == null)
            return;
        if (_followHand != null)
        {
            _root.localPosition = new Vector3(0f, HandPalmOffset, 0f);
            FaceHead(_root);
        }
        else if (_boardAnchored)
        {
            _root.localPosition = BoardAnchorBase + CardsConfig.BrowseFanOffset.Value;
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

        List<CItem>? items = ItemsOf(hand);
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

    /// <summary>Cheap change key: item count + each item's slot-state (drives live refresh).</summary>
    private static string Signature(CardsHandUI? hand)
    {
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

        float radius = CardsConfig.FanRadius.Value * RadiusFactor;
        float step = n > 1 ? Mathf.Min(MaxStepDegrees, MaxArcDegrees / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

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
            chip.SetHome(pos, rot, ChipScale);
        }
    }

    // ------------------------------------------------------------------ hand sweep --

    /// <summary>
    /// Physical HAND sweep over the item fan (the item counterpart of
    /// <see cref="PileBrowser.UpdateHandSweep"/>): the free (dominant) hand's index tip elects a
    /// SINGLE winner among the chips (nearest by tip distance, candidacy by tip ≤
    /// <see cref="ContactTipReach"/> OR palm ≤ <see cref="ContactPalmReach"/>, incumbent
    /// hysteresis). The winner POPS (lift/enlarge) and every other chip drops — so sweeping the
    /// hand through the fan keeps exactly ONE chip highlighted, just like the ability-card fan.
    /// Read-only: it only lifts for readability; the pinch/laser own the pull-into-hand.
    /// </summary>
    private void UpdateHandSweep()
    {
        VRHand? dom = VRHands.Primary;
        ItemChip? winner = null, runnerUp = null;
        float winnerTip = 0f, runnerTip = 0f;
        float bestScore = float.MaxValue, secondScore = float.MaxValue;

        // The sweeping hand is the dominant/free hand — tracked and NOT holding anything. In
        // HELD mode the pinch that opened the fan holds via _followHand, naturally excluded.
        if (dom != null && !ReferenceEquals(dom, _followHand) && dom.HasPose && dom.Grabber.Held == null)
        {
            Vector3 tip = dom.Rig.IndexTip.position;
            Vector3 palm = dom.Rig.PalmCenter.position;
            float scale = dom.WorldScale;
            float tipReach = ContactTipReach * scale;
            float palmReach = ContactPalmReach * scale;
            float sticky = ContactStickyMargin * scale;

            for (int i = 0; i < _chips.Count; i++)
            {
                ItemChip c = _chips[i];
                if (c == null || c.Holder != null)
                    continue;
                if (!c.TryFingertipDistance(tip, out float tipDist)
                    || !c.TryFingertipDistance(palm, out float palmDist))
                    continue;
                if (tipDist > tipReach && palmDist > palmReach)
                    continue; // out of BOTH reaches — not a candidate
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
            _handWinner?.SetFingertipPop(false);
            _handWinner = winner;
            _handWinner?.SetFingertipPop(true);

            float now = Time.unscaledTime;
            if (winner != null && now >= _nextHandLogAt)
            {
                _nextHandLogAt = now + 0.5f;
                string runner = runnerUp != null ? $"'{runnerUp.name}' ({runnerTip * 100f:F1} cm)" : "none";
                VRLog.Info("Cards", $"Item-fan hand sweep: '{winner.name}' — index-tip {winnerTip * 100f:F1} cm; " +
                                    $"runner-up {runner}. One chip lifts at a time (like the ability fan).");
            }
        }
    }

    private void ClearHandSweep()
    {
        _handWinner?.SetFingertipPop(false);
        _handWinner = null;
    }

    // ------------------------------------------------------------------ use slot --

    /// <summary>
    /// The single held chip that can actually be USED right now (requirement 3): non-passive AND
    /// in a Useable/Selected slot state — the EXACT predicate <c>UseItemService.UseItem</c>
    /// enforces (evaluated LIVE via <see cref="ItemChip.IsActivatable"/>), so the slot never
    /// appears for an item the service would reject.
    /// </summary>
    private ItemChip? HeldActivatableChip()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c != null && c.Holder != null && c.IsActivatable)
                return c;
        }
        return null;
    }

    /// <summary>
    /// Requirement 3 (clip-in to use): a just-released chip is offered to the board's item-use
    /// slot. If the chip is activatable and was dropped onto/near the slot, use it via the game's
    /// own <c>UseItemService</c> (which owns ALL multiplayer sync — ItemToken +
    /// GameActionType.UseItem — and re-validates the item itself). Otherwise it is a no-op and the
    /// chip returns to its fan home (the base restore already ran). Never mutates inventory directly.
    /// </summary>
    internal void OnChipReleased(ItemChip chip, Vector3 dropWorldPos, VRHand vrHand)
    {
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        if (chip == null || !chip.IsActivatable || slot == null || !slot.gameObject.activeSelf)
            return;

        // Proximity test in world space (parenting-independent): the capture radius scales with
        // the board so the slot stays the same on-screen size at any board scale.
        float scale = slot.lossyScale.x;
        float radius = UseSlotRadius * (scale > 1e-4f ? scale : 1f);
        if ((dropWorldPos - slot.position).sqrMagnitude > radius * radius)
            return; // dropped away from the slot — nothing to do

        CPlayerActor? actor = _hand != null ? _hand.PlayerActor : null;
        if (actor == null || chip.Item == null)
            return;

        string itemName = chip.name;
        try
        {
            // UseItemService handles the online GameAction send + local execution; passive/
            // non-usable items are rejected inside it (belt-and-braces with IsActivatable).
            new UseItemService(actor).UseItem(chip.Item);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Cards", $"ITEM USE failed for '{itemName}': {e.Message}");
            return;
        }

        vrHand.SendHaptic(HapticPreset.ClickPulse);
        VRLog.Info("Cards", $"ITEM USED {itemName} (clip-in slot, {vrHand.Side}). " +
                            "Fan re-classifies (Spent/Consumed) on the next state-change refresh.");
        PlayTray.Current?.SetItemUseSlotVisible(false); // hide now; the fan rebuilds when the item re-classifies
        _useSlotShownLogged = false;
    }

    // ================================================================== item chip ==

    /// <summary>
    /// One physical item card in the pile. Renders the REAL <c>ItemCardUI</c> face (game art +
    /// name; async background via <c>ImageAddressableLoader</c>) hosted on a world-space canvas,
    /// with a legacy colored slab + name as the fallback. Supports the full ability-card
    /// interaction set: fingertip hand-sweep POP (<see cref="SetFingertipPop"/>), dominant-hand
    /// LASER hover+pluck (via <see cref="IPokeable"/> + a <see cref="PlayTray"/> laser target),
    /// pinch-GRAB, and read-in-hand (<see cref="GetHeldPose"/>). Consumed items carry the burn
    /// plume; spent/consumed also show the card's own state FX (UpdateState).
    /// </summary>
    internal sealed class ItemChip : GrabbableBehaviour, IPokeable
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
        internal bool IsActivatable
        {
            get
            {
                CItem? item = Item;
                return item != null && item.YMLData != null
                    && item.YMLData.Trigger != CItem.EItemTrigger.PassiveEffect
                    && (item.SlotState == CItem.EItemSlotState.Useable
                        || item.SlotState == CItem.EItemSlotState.Selected);
            }
        }

        // Pop/enlarge for readability (fingertip sweep OR laser hover — spatially exclusive, so
        // one effective pop). Mirrors VRCard's pop: a small grow + a nudge toward the viewer.
        private const float PopScale = 1.18f;
        private const float PopLift = 0.02f;   // local -Z (toward the viewer) at full pop
        private const float PopLerpSpeed = 16f;

        private ItemsPile? _owner; // for the clip-in-to-use callback on release
        private Vector3 _homePos;
        private Quaternion _homeRot;
        private float _homeScale = 1f;
        private BoxCollider? _box;
        private GameObject? _plume;     // consumed-item smoke, destroyed with the chip
        private GameObject? _cardGo;    // hosted ItemCardUI GameObject (recycled to the pool on disable)
        private ItemCardUI? _cardUI;
        private bool _fingerPopped;
        private bool _laserPopped;
        private float _pop; // smoothed 0..1

        private static bool s_loggedRealCard;

        internal static ItemChip Create(ItemsPile owner, Transform parent, CItem item)
        {
            Visual state = Classify(item);
            float w = CardsConfig.CardWidth.Value;
            float h = CardsConfig.CardHeight;

            var go = new GameObject($"ItemChip_{Name(item)}");
            go.transform.SetParent(parent, worldPositionStays: false);

            // Thin dark card BODY behind the face (reads as a card even before async art loads).
            var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backing.name = "Backing";
            Object.Destroy(backing.GetComponent<Collider>());
            backing.transform.SetParent(go.transform, worldPositionStays: false);
            backing.transform.localScale = new Vector3(w, h, 0.0022f);
            backing.transform.localPosition = new Vector3(0f, 0f, 0.0012f); // behind the face (+Z away from viewer)
            var backRenderer = backing.GetComponent<MeshRenderer>();
            Shader? backShader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            Core.VRLayers.Apply(backing); // mod-owned backing on the mod layer (recursion-safe: no children)

            var chip = go.AddComponent<ItemChip>();
            chip.snapToHand = true; // taken INTO the hand to read
            chip.State = state;
            chip.Item = item;
            chip._owner = owner;

            // Requirement 1: host the REAL ItemCardUI face. Fall back to a colored slab + name if
            // the game's item-card pool is unavailable (out of scenario / missing bundle).
            bool realCard = chip.TryHostRealCard(item, go.transform, w, h, state);
            if (!realCard)
            {
                if (backShader != null)
                    backRenderer.sharedMaterial = new Material(backShader) { color = FaceColor(state) };
                BuildFallbackFace(go.transform, item, w, h, state);
            }
            else if (backShader != null)
            {
                backRenderer.sharedMaterial = new Material(backShader) { color = new Color(0.10f, 0.09f, 0.08f) };
            }

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(w + 0.006f, h + 0.006f, 0.02f);
            box.isTrigger = true;
            chip._box = box;
            // NOTE: do NOT VRLayers.Apply(go) — it recurses into the hosted ItemCardUI, which is a
            // GAME-owned canvas that must keep its authored UI layer (reversibility rule; it renders
            // via the VR camera's UI-layer bit owned by CanvasConversion). The mod-owned pieces
            // (backing above, fallback face + plume below) are layered individually instead.

            // FULLY CONSUMED → the game's burn plume, torn down with the chip (requirement).
            if (state == Visual.Consumed)
            {
                chip._plume = BurnCardFx.SpawnConsumedPlume(go.transform);
                if (chip._plume != null)
                    Core.VRLayers.Apply(chip._plume);
            }

            // Laser: register the chip's collider as a board laser target so the dominant hand's
            // beam pops it on hover (OnPokeEnter) and plucks it on trigger (OnPoke) — the same
            // laser-pull ability normal cards have. Unregistered in OnDisable.
            PlayTray.Current?.RegisterLaserTarget(box, chip);

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
                cardUI.Show(highlightElement: false); // no UIManager lock; activates + loads art async
                cardUI.UpdateState(item.SlotState, force: true); // spent/consumed FX to match the fan look

                // Fit the card's native rect onto the physical card size (mirror of CardFace).
                var cardRect = cardGo.transform as RectTransform;
                Vector2 native = cardRect != null ? cardRect.rect.size : Vector2.zero;
                if (native.x < 1f || native.y < 1f)
                    native = new Vector2(300f, 440f); // sane fallback if the rect isn't measurable yet
                canvasRect.sizeDelta = native;
                float fit = Mathf.Min(w / native.x, h / native.y);
                canvasRect.localScale = new Vector3(fit, fit, fit);
                if (cardRect != null)
                {
                    cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
                    cardRect.anchoredPosition3D = Vector3.zero;
                    cardRect.localRotation = Quaternion.identity;
                    cardRect.localScale = Vector3.one;
                }

                // Neutralize the card's own raycasters — we drive interaction through the mod's
                // collider (grab/laser), never the game's uGUI input module (which would raycast
                // this world canvas at the parked mouse pixel).
                foreach (GraphicRaycaster gr in cardGo.GetComponentsInChildren<GraphicRaycaster>(true))
                    gr.enabled = false;

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

        // Take-into-hand reading pose: upright, facing the palm, enlarged — the same frame
        // math VRCard uses so items read like ability cards do when inspected.
        protected override HeldPose GetHeldPose(VRHand hand)
        {
            float bias = CardsConfig.HeldFaceBias.Value * Mathf.Deg2Rad;
            var faceNormal = new Vector3(0f, Mathf.Cos(bias), -Mathf.Sin(bias));
            float thumbSide = hand.Side == HandSide.Right ? 1f : -1f;
            var rot = Quaternion.LookRotation(-faceNormal, new Vector3(thumbSide, 0f, 0f));
            return new HeldPose(new Vector3(0f, 0.02f, 0.04f), rot, CardsConfig.InspectScale.Value);
        }

        public override void OnGrab(VRHand hand)
        {
            // Drop the pop before the base snap captures the pre-grab pose (so the release restores
            // the un-popped home scale).
            _fingerPopped = false;
            _laserPopped = false;
            _pop = 0f;
            if (Holder == null)
            {
                transform.localScale = Vector3.one * _homeScale;
                transform.localPosition = _homePos;
            }
            base.OnGrab(hand); // snaps to the reading pose
            hand.SendHaptic(HapticPreset.ClickPulse);
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
            base.OnRelease(hand, velocity); // detach + restore fan home
            _owner?.OnChipReleased(this, dropWorldPos, hand);
        }

        // ---- pop (readability) -----------------------------------------------------

        /// <summary>Fingertip hand-sweep pop (set by the owner's single-winner sweep).</summary>
        internal void SetFingertipPop(bool on) => _fingerPopped = on;

        /// <summary>True while the dominant index tip / palm is within this chip's collider reach.</summary>
        internal bool TryFingertipDistance(Vector3 worldPoint, out float distance)
        {
            distance = float.MaxValue;
            if (_box == null || !_box.enabled || !_box.gameObject.activeInHierarchy)
                return false;
            distance = Vector3.Distance(worldPoint, _box.ClosestPoint(worldPoint));
            return true;
        }

        private void Update()
        {
            if (Holder != null)
                return; // held — the hand owns the pose
            bool popped = _fingerPopped || _laserPopped;
            float target = popped ? 1f : 0f;
            _pop = Mathf.MoveTowards(_pop, target, PopLerpSpeed * Time.unscaledDeltaTime);
            // Apply on top of the arc home: grow a touch + nudge toward the viewer (local -Z).
            transform.localScale = Vector3.one * (_homeScale * (1f + (PopScale - 1f) * _pop));
            transform.localPosition = _homePos + new Vector3(0f, 0f, -PopLift * _pop);
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
            hand.Grabber.ForceGrab(this, releaseOnTriggerUp: true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_box != null)
                PlayTray.Current?.UnregisterLaserTarget(_box);
            if (_plume != null)
            {
                Object.Destroy(_plume);
                _plume = null;
            }
            // Return the hosted ItemCardUI to the game's pool BEFORE this chip is destroyed (recycle
            // reparents it under the pool, so it survives the chip teardown and its art is unloaded).
            if (_cardGo != null && _cardUI != null)
            {
                try
                {
                    ObjectPool.RecycleCard(_cardUI.CardID, ObjectPool.ECardType.Item, _cardGo);
                }
                catch (System.Exception e)
                {
                    VRLog.Warn("Cards", $"ITEM CARD recycle failed ({e.Message}).");
                }
            }
            _cardGo = null;
            _cardUI = null;
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
