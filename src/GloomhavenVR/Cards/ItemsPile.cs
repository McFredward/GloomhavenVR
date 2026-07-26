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

    /// <summary>
    /// Requirement #2: the per-board ITEM-fan offset — nudges the item fan root (board-anchored,
    /// held-follow, and above-board reading poses) INDEPENDENTLY of the ability-card fan, since item
    /// cards are a different, near-square shape. Read LIVE every <see cref="Tick"/> (a cheap Vector3
    /// config read) so the debug-menu 'Item card X/Y/Z' steppers move an OPEN fan immediately, exactly
    /// like <see cref="CardsConfig.BrowseFanOffset"/> does for the discard/burnt fans.
    /// </summary>
    private static Vector3 ItemFanOffset => CardsConfig.ItemCardOffset(CardsConfig.CurrentBoard).Value;

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

    // Requirement 6 (clip-in decision): the chip currently CLIPPED into the use slot awaiting a
    // Confirm/cancel decision (null = none). While set, the fan never live-rebuilds (so the decision
    // chip is never yanked), the chip is driven to the slot pose each tick, and a Confirm button shows.
    private ItemChip? _pendingUseChip;

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
        EmergeAll(); // req #5: fly the chips OUT of the pile stack (after _root is placed)
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
        _pendingUseChip = null; // #6: drop any pending decision on close
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        PlayTray.Current?.SetItemUseSlotVisible(false); // never leave the use slot up once the fan is gone
        _useSlotShownLogged = false;
        CollapseChips(); // req #5: fly the chips BACK INTO the pile stack, then self-destroy
        _signature = string.Empty;
        if (_root != null)
            _root.gameObject.SetActive(false);
        VRLog.Info("Cards", "Items pile browse CLOSE.");
    }

    internal void Destroy()
    {
        ClearHandSweep();
        ClearChips();
        _pendingUseChip = null; // #6
        IsOpen = false;
        _followHand = null;
        _boardAnchored = false;
        _hand = null;
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
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

        // Live refresh: rebuild only when nothing is held AND no decision is pending (so a chip clipped
        // into the use slot is never yanked by a rebuild) and the inventory state moved.
        if (!AnyHeld() && _pendingUseChip == null)
        {
            string sig = Signature(hand);
            if (sig != _signature)
                Populate(hand);
        }

        // Physical fingertip sweep: lift the chip nearest the dominant index tip (single-winner).
        UpdateHandSweep();

        if (_pendingUseChip != null)
        {
            // Requirement 6: a chip is CLIPPED into the use slot awaiting a decision — keep the slot +
            // Confirm button up and glue the chip to the slot pose, or resolve the cancel/invalidation.
            TickPendingUse(hand);
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
            if (showUseSlot != _useSlotShownLogged)
            {
                _useSlotShownLogged = showUseSlot;
                Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
                Vector3 pos = slot != null ? slot.position : Vector3.zero;
                VRLog.Info("Cards", $"ITEM USE SLOT: {(showUseSlot ? "shown" : "hidden")} " +
                                    $"(turn={turn} heldUsable={(heldUsable != null)}) at ({pos.x:F2},{pos.y:F2},{pos.z:F2}).");
            }
        }

        // Fan facing/position: HELD → float above the palm; BOARD-ANCHORED → re-read the shared
        // board anchor (+ live BrowseFanOffset). Either way billboard toward the head (ISSUE #7).
        if (_root == null)
            return;
        if (_followHand != null)
        {
            _root.localPosition = new Vector3(0f, HandPalmOffset, 0f) + ItemFanOffset; // req #2 item nudge
            FaceHead(_root);
        }
        else if (_boardAnchored)
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

    /// <summary>World anchor the fan emerges from / collapses into (req #5): the item PILE stack
    /// region (the pile mount PileViewer built the stacks under). Falls back to the fan root.</summary>
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
                if (c == null || c.Holder != null || c.PendingUse)
                    continue; // a chip clipped into the use slot is not a sweep candidate (#6)
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
    /// Requirement 6 (clip-in + decide): a just-released usable chip dropped onto/near the board's
    /// item-use slot does NOT use immediately — it CLIPS INTO the slot and waits. A poke/laser on the
    /// Confirm button uses it (<see cref="ConfirmPendingUse"/>); grabbing the card BACK OUT and
    /// releasing it returns it to the deck, no use. Dropped anywhere else (or a non-activatable chip)
    /// is a no-op — the base glide-home already runs. Inventory is never mutated here.
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
            return; // dropped away from the slot — the base glide-home returns it to the fan

        // Only one pending decision at a time (a fresh drop replaces an older pending clip cleanly).
        if (_pendingUseChip != null && !ReferenceEquals(_pendingUseChip, chip))
            _pendingUseChip.ReturnToFan();

        _pendingUseChip = chip;
        chip.PendingUse = true;
        chip.CancelReleaseGlide(); // do NOT glide home — we clip into the slot instead
        PlayTray.Current?.SetItemUseSlotVisible(true);
        PlayTray.Current?.SetItemUseConfirmVisible(true, ConfirmPendingUse);
        vrHand.SendHaptic(HapticPreset.HoverTick);
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
            chip.ReturnToFan(); // glide back to the fan (not held)
            CancelPendingUse("no longer usable");
            return;
        }

        PlayTray.Current?.SetItemUseSlotVisible(true);
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        if (slot != null && _root != null)
        {
            Vector3 lp = _root.InverseTransformPoint(slot.position);
            Quaternion lr = Quaternion.Inverse(_root.rotation) * slot.rotation;
            chip.SetClipTarget(lp, lr, ChipScale);
        }
    }

    /// <summary>Requirement 6 — drop the pending state + hide the Confirm button. Clears the chip's own
    /// PendingUse flag so a chip GRABBED back out glides home on release (instead of re-clipping); the
    /// invalidation path already called <see cref="ItemChip.ReturnToFan"/> to start that glide.</summary>
    private void CancelPendingUse(string why)
    {
        ItemChip? chip = _pendingUseChip;
        _pendingUseChip = null;
        if (chip != null)
            chip.PendingUse = false;
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
            // UseItemService owns the online GameAction send + local execution + re-validation.
            new UseItemService(actor).UseItem(item);
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
        bool consumed = item.SlotState == CItem.EItemSlotState.Consumed;
        bool spent = item.SlotState == CItem.EItemSlotState.Spent;
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
        // Actual rendered card size (item aspect) — set by TryHostRealCard, drives the backing + collider.
        private float _faceWidth;
        private float _faceHeight;

        // ITEM #1 (de-shimmer): the hosted ItemCardUI's cardBackground art loads ASYNC, so — exactly
        // like the ability cards' CardFace — its mipless-atlas sprites must be swapped for mip-baked
        // equivalents on host AND re-scanned on a slow cadence until the art has arrived (CardFace.cs
        // uses MipRescanInterval=1s and re-runs forever, because async arrivals / state changes keep
        // putting mipless originals back). RestoreSprites runs before the widget is recycled to the pool
        // so the pooled card is left clean. The bake caches are static + content-keyed, so the item
        // sprites share the ability cards' baked atlases for free (see CardFaceMipBake).
        private const float MipRescanInterval = 1f;
        private float _nextMipRescan;

        // ITEM #7 (live playable-gating legibility): non-usable chips (passive items, spent/consumed,
        // or anything whose live IsActivatable is false right now — e.g. off-turn) are dimmed so the
        // currently-usable ones read as the ones you can actually play. Applied via a REVERSIBLE
        // CanvasGroup alpha on the hosted card — never mutates the game card's own materials, and it is
        // reset to full alpha before the widget is recycled to the pool. Re-evaluated LIVE every Update
        // from IsActivatable, because usability changes with turn/phase. -1 = "not yet applied".
        private const float UsableAlpha = 1f;
        private const float DimAlpha = 0.4f;
        private CanvasGroup? _faceGroup;   // on the hosted ItemCardUI (reversible face dim)
        private int _usabilityShown = -1;  // last applied state: -1 none, 0 dimmed, 1 bright

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
        // awaiting a Confirm/cancel decision, PendingUse is set and the owner drives it to this clip
        // target (root-local, so it rides the board billboard) each tick. The chip stays grabbable so
        // the player can grab it BACK OUT to cancel (the #6 refinement); on release it glides to the fan.
        internal bool PendingUse { get; set; }
        private Vector3 _clipPos;
        private Quaternion _clipRot = Quaternion.identity;
        private float _clipScale = 1f;
        private bool _hasClip;

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

            // Now size the grab collider to the real card (a small margin for easy laser/finger targeting).
            box.size = new Vector3(cw + 0.006f, ch + 0.006f, 0.02f);
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
                cardUI.Show(highlightElement: false); // no UIManager lock; activates + loads art async
                cardUI.UpdateState(item.SlotState, force: true); // spent/consumed FX to match the fan look

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

                // Neutralize the card's own raycasters — we drive interaction through the mod's
                // collider (grab/laser), never the game's uGUI input module (which would raycast
                // this world canvas at the parked mouse pixel).
                foreach (GraphicRaycaster gr in cardGo.GetComponentsInChildren<GraphicRaycaster>(true))
                    gr.enabled = false;

                // ITEM #7 — a reversible CanvasGroup on the hosted card drives the live playable-gate
                // dim (alpha only; no game material is touched). Reuse any existing group so we never
                // stack components, and leave it at full alpha until the first Update classifies it.
                _faceGroup = cardGo.GetComponent<CanvasGroup>() ?? cardGo.AddComponent<CanvasGroup>();
                _faceGroup.alpha = 1f;

                // ITEM #1 — swap the mipless-atlas sprites for mip-baked equivalents right after host,
                // then re-scan on a slow cadence (Update) until the async background art has arrived.
                CardFaceMipBake.Rescan(cardUI);
                _nextMipRescan = Time.unscaledTime + MipRescanInterval;

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

        /// <summary>Requirement 6 — set the root-local pose the pending chip clips to (the use slot pose,
        /// re-read each tick by the owner so it stays glued to the slot as the board billboards).</summary>
        internal void SetClipTarget(Vector3 localPos, Quaternion localRot, float scale)
        {
            _clipPos = localPos;
            _clipRot = localRot;
            _clipScale = scale;
            _hasClip = true;
        }

        /// <summary>Requirement 6 — cancel the post-release glide-home (used when a drop CLIPS into the
        /// use slot instead of returning to the fan).</summary>
        internal void CancelReleaseGlide() => _releaseGlide = 0f;

        /// <summary>Requirement 6 — return the chip to its fan home (the "return to deck" path on cancel):
        /// clear the clip state and start the same glide the post-release home uses.</summary>
        internal void ReturnToFan()
        {
            PendingUse = false;
            _hasClip = false;
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
            _hasClip = false;
            _useFxActive = true;
            _useFxTime = UseFxSeconds;
            _useFxSpent = spent && !consumed;
            _useFxCollapseWorld = collapseWorld;
            _useFxBaseRot = transform.localRotation;
            _fingerPopped = false;
            _laserPopped = false;
            if (_box != null)
                _box.enabled = false; // no grabbing during the flourish
            if (consumed && _plume == null)
            {
                _plume = BurnCardFx.SpawnConsumedPlume(transform);
                if (_plume != null)
                    Core.VRLayers.Apply(_plume);
            }
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
            // Consumed: the plume (spawned above) billows over the flourish window; no extra motion.
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
            // Drop the pop so the grabbed chip starts from a clean pose.
            _fingerPopped = false;
            _laserPopped = false;
            _pop = 0f;
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
        }

        // ---- pop (readability) -----------------------------------------------------

        /// <summary>
        /// ITEM #1 + #7 — per-frame face upkeep, run in every state (held, popped, settled, glide):
        /// (1) re-run the mip-bake sprite swap on a slow cadence until the async background art has
        /// baked (mirror of <c>CardFace.Maintain</c>'s <see cref="MipRescanInterval"/> cadence — async
        /// arrivals / state changes keep putting the mipless originals back), and (2) LIVE re-evaluate
        /// the playable-gate dim from <see cref="IsActivatable"/> so the usable chips read bright and
        /// the non-usable ones (passive / spent / consumed / off-turn) dim. Usability changes with
        /// turn/phase, so it is polled every frame, never cached. Change-gated (both are no-ops unless
        /// due), so the per-frame cost is a clock compare + a bool compare.
        /// </summary>
        private void TickFaceMaintenance()
        {
            // ITEM #3 — keep the WorldSpace face canvas bound to the head camera (mirror of
            // VRCard.UpdateCanvasCamera) so the item face renders in the desktop mirror like ability cards.
            if (_faceCanvas != null)
            {
                Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
                if (head != null && _faceCanvas.worldCamera != head)
                {
                    _faceCanvas.worldCamera = head;
                    if (!s_loggedMirror)
                    {
                        s_loggedMirror = true;
                        VRLog.Info("Cards", "ITEM CARD mirror fix (#3): item FaceCanvas.worldCamera bound to the " +
                                            $"head camera '{head.name}' (layer {_cardGo?.layer ?? -1}) — the item face " +
                                            "now renders in the desktop mirror like the ability cards' face.");
                    }
                }
            }

            if (_cardUI != null && Time.unscaledTime >= _nextMipRescan)
            {
                _nextMipRescan = Time.unscaledTime + MipRescanInterval;
                CardFaceMipBake.Rescan(_cardUI);
            }

            int want = IsActivatable ? 1 : 0;
            if (want != _usabilityShown)
            {
                _usabilityShown = want;
                if (_faceGroup != null)
                    _faceGroup.alpha = want == 1 ? UsableAlpha : DimAlpha;
            }
        }

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

            TickFaceMaintenance(); // ITEM #1 (de-shimmer) + #7 (live playable-gate dim) — held or not

            if (Holder != null)
            {
                TickHeldPose(); // FIX 1 — track the wrist + billboard the face every frame while held
                return;
            }

            if (PendingUse) // Req #6 — clipped into the use slot, waiting for the decision
            {
                if (_hasClip)
                {
                    float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * Time.unscaledDeltaTime);
                    transform.localPosition = Vector3.Lerp(transform.localPosition, _clipPos, t);
                    transform.localRotation = Quaternion.Slerp(transform.localRotation, _clipRot, t);
                    transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _clipScale, t);
                }
                return;
            }

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
                    // ITEM #1/#7 — leave the pooled widget CLEAN: restore the mip-swapped sprites and
                    // reset the reversible playable-gate dim before the game reuses this card elsewhere.
                    CardFaceMipBake.RestoreSprites(_cardUI);
                    if (_faceGroup != null)
                        _faceGroup.alpha = 1f;
                    ObjectPool.RecycleCard(_cardUI.CardID, ObjectPool.ECardType.Item, _cardGo);
                }
                catch (System.Exception e)
                {
                    VRLog.Warn("Cards", $"ITEM CARD recycle failed ({e.Message}).");
                }
            }
            _cardGo = null;
            _cardUI = null;
            _faceGroup = null;
            _faceCanvas = null;
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
