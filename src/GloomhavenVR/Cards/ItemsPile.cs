using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// The THIRD control-board pile (item 4, "Gegenstände"): the acting character's
/// equipped ITEM cards, mounted below the burnt pile and browsed exactly like the
/// discard / burnt stacks (poke to toggle a fixed head-relative reading wall,
/// pinch-grab to raise it as a hand-held fan). Unlike the ability-card piles the
/// content is NOT ability-card widgets — items are <see cref="CItem"/> (a
/// <c>CBaseCard</c>) read live from <c>PlayerActor.Inventory.AllItems</c>
/// (CInventory.cs:35), so this pile renders its own physical item chips instead of
/// adopting <c>AbilityCardUI</c>. Purely informational: the inventory is read, never
/// written (using an item still happens through the game's own use-items flow).
///
/// State → look (verified against <c>CItem.EItemSlotState</c>, CItem.cs:18, and the
/// use path <c>CInventory.UseItem</c>, CInventory.cs:926-948):
/// <list type="bullet">
/// <item>FULLY CONSUMED (<c>SlotState == Consumed</c>; single-use / permanently spent
/// for the scenario, never refreshed on a long rest — see
/// <c>CAbilityRefreshItemCards</c> which only refreshes <c>PermanentlyConsumed != true</c>,
/// CAbilityRefreshItemCards.cs:48) → the BURN display: ashen chip + the game's own
/// CardSmoke plume via <see cref="BurnCardFx.SpawnConsumedPlume"/>.</item>
/// <item>SPENT-but-refreshable (<c>SlotState == Spent</c>; reactivated by a long rest)
/// → the chip lies VERTICAL / rolled 90° ("tapped") in the pile.</item>
/// <item>Otherwise (Equipped / Useable / Active / …) → upright and readable.</item>
/// </list>
/// A chip taken INTO the hand (pinch-grab) snaps upright + enlarged at a reading pose
/// regardless of its pile state, so the item card is always readable in hand
/// (requirement 4). Layout mirrors <see cref="PileBrowser"/>; open/close is driven by
/// <see cref="PileViewer"/> which routes the item stack's poke/grab here.
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
    // PileBrowser.BoardFloatHeight / BoardFloatProudZ / BoardAnchorBase, kept in lockstep so
    // all three pile fans open in one place). The root parents under the board root
    // (PlayTray.Current.Root) so it inherits the board's live pose + scale.
    private const float BoardFloatHeight = 0.26f;
    private const float BoardFloatProudZ = -0.05f;
    private static Vector3 BoardAnchorBase =>
        new(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);

    // Dynamic USE slot (requirement 3): a highlighted drop target floating just above the
    // board top, BELOW the hanging fan, board-local so it rides the board's pose/scale. It
    // only appears while an ACTIVATABLE item chip is in hand; releasing that chip onto/near
    // this pad uses the item. Kept clear of the fan body (the fan pivot sits at
    // BoardTopLocalY + BoardFloatHeight; the pad sits well below it, near the board edge).
    private const float UseSlotFloatHeight = 0.08f;
    private const float UseSlotProudZ = -0.06f;
    private static Vector3 UseSlotBase =>
        new(0f, PlayTray.BoardTopLocalY + UseSlotFloatHeight, UseSlotProudZ);
    /// <summary>Drop-to-use capture radius (world metres at board scale 1; scaled by the board's live scale).</summary>
    private const float UseSlotRadius = 0.13f;

    private readonly List<ItemChip> _chips = new(12);
    private Transform? _root;
    private TextMeshPro? _title;
    private Transform? _anchor;   // the pile mount (rig-space, diorama-scaled) — placement scale ref
    private VRHand? _followHand;
    private CardsHandUI? _hand;
    private string _signature = string.Empty; // last-built inventory state, for cheap live refresh
    private bool _boardAnchored; // poke-toggle fan parented under the board root (mirrors PileBrowser)

    // The dynamic use slot + its highlight material (created lazily under the board root,
    // shown only while an activatable chip is held — see SetUseSlotVisible / OnChipReleased).
    private Transform? _useSlot;
    private bool _useSlotVisible;

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

    /// <summary>Poke-toggle: open at a fixed head-relative reading wall, or close if already open.</summary>
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
        // board-top spot (same as the discard/burnt PileBrowser), so the three pile fans
        // open in one place and this fan inherits the board's live pose + scale. A held
        // (grabbed) fan still follows the grabbing hand; no board → head-relative fallback.
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
        SetUseSlotVisible(false); // never leave a use pad floating once the fan is gone
        ClearChips();
        _signature = string.Empty;
        if (_root != null)
            _root.gameObject.SetActive(false);
        VRLog.Info("Cards", "Items pile browse CLOSE.");
    }

    internal void Destroy()
    {
        ClearChips();
        IsOpen = false;
        _followHand = null;
        _boardAnchored = false;
        _hand = null;
        if (_useSlot != null)
        {
            Object.DestroyImmediate(_useSlot.gameObject);
            _useSlot = null;
        }
        _useSlotVisible = false;
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
    /// Self-closes on context loss (no acting hand / hand switch), follows the holding hand
    /// when held, and cheaply rebuilds the chips when an item's state changed (spent → tapped,
    /// consumed → burn) — but never while a chip is in hand, so a grab is never yanked away.
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

        // Requirement 3: the dynamic USE slot appears ONLY while an activatable item chip is
        // in hand, and rides the board-local anchor (re-read each frame so a live BrowseFanOffset
        // tune moves it with the fan) while billboarding toward the head like the fan.
        SetUseSlotVisible(HeldActivatableChip() != null);
        if (_useSlot != null && _useSlotVisible)
        {
            _useSlot.localPosition = UseSlotBase + CardsConfig.BrowseFanOffset.Value;
            FaceHead(_useSlot);
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
    /// float the fan at the SAME spot above the control board the discard/burnt fans use —
    /// <see cref="BoardAnchorBase"/> plus the debug-menu-tunable <see cref="CardsConfig.BrowseFanOffset"/>.
    /// The root is a child of the board root (set in <see cref="Open"/>), so this board-LOCAL
    /// offset inherits the board's live scale + pose; <see cref="Tick"/> re-reads it and
    /// billboards the facing toward the head each frame.
    /// </summary>
    private void PlaceAboveBoard()
    {
        if (_root == null)
            return;
        _root.localPosition = BoardAnchorBase + CardsConfig.BrowseFanOffset.Value;
        _root.localRotation = Quaternion.identity; // Tick billboards the WORLD rotation each frame
        FaceHead(_root); // face the head immediately (no first-frame flash of the un-billboarded arc)
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

    // ------------------------------------------------------------------ use slot --

    /// <summary>
    /// The single held chip that can actually be USED (requirement 3): non-passive AND in a
    /// Useable/Selected slot state — the EXACT predicate <c>UseItemService.UseItem</c> enforces
    /// before it will act, so the pad never appears for an item the service would reject
    /// (spent/consumed/passive/equipped items just return to their fan home on release).
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
    /// Show/hide the dynamic use pad. Lazily built under the board root the first time it is
    /// needed (no board → no pad). Idempotent and allocation-free once built.
    /// </summary>
    private void SetUseSlotVisible(bool visible)
    {
        if (visible)
        {
            EnsureUseSlot();
            if (_useSlot == null)
                return;
            if (!_useSlot.gameObject.activeSelf)
                _useSlot.gameObject.SetActive(true);
            _useSlotVisible = true;
        }
        else
        {
            _useSlotVisible = false;
            if (_useSlot != null && _useSlot.gameObject.activeSelf)
                _useSlot.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Build the use pad once, parented under the board root so it rides the board's pose/scale
    /// (the same anchor family the fan uses). A card-sized glowing quad + a localized "USE"
    /// caption, styled like the other board markers; starts hidden (Tick toggles it).
    /// </summary>
    private void EnsureUseSlot()
    {
        if (_useSlot != null)
            return;
        Transform? boardRoot = PlayTray.Current?.Root;
        if (boardRoot == null)
            return; // no board — the drop-to-use pad only exists in the board layout

        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        var go = new GameObject("GloomhavenVR.ItemUseSlot");
        go.transform.SetParent(boardRoot, worldPositionStays: false);
        go.transform.localPosition = UseSlotBase + CardsConfig.BrowseFanOffset.Value;

        var pad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pad.name = "Pad";
        Object.Destroy(pad.GetComponent<Collider>());
        pad.transform.SetParent(go.transform, worldPositionStays: false);
        pad.transform.localScale = new Vector3(w * 1.15f, h * 1.15f, 1f);
        pad.transform.localPosition = new Vector3(0f, 0f, 0.001f);
        var padRenderer = pad.GetComponent<MeshRenderer>();
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
        if (shader != null)
            // Warm gold, translucent — reads as an inviting "drop here to use" highlight without
            // masking the board behind it (Sprites/Default is unlit + alpha-blended, VR-cheap).
            padRenderer.sharedMaterial = new Material(shader) { color = new Color(1f, 0.82f, 0.35f, 0.5f) };

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, worldPositionStays: false);
        labelGo.transform.localPosition = new Vector3(0f, 0f, -0.002f); // viewer side (-Z)
        var label = labelGo.AddComponent<TextMeshPro>();
        // "USE" — localized from the game's own use-item bar key with a safe English fallback.
        label.text = Core.Loc.Game("GUI_USE", "USE").ToUpperInvariant();
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.15f, 0.10f, 0.03f);
        WorldUI.NativeButtonSkin.ApplyFont(label);
        Core.TmpFit.Fit(label, w * 0.9f, h * 0.5f, maxFontSize: 0.14f, wrap: false);

        Core.VRLayers.Apply(go);
        go.SetActive(false);
        _useSlot = go.transform;
    }

    /// <summary>
    /// Requirement 3: a just-released chip is offered to the use pad. If the chip is activatable
    /// and was dropped onto/near the pad, use it via the game's own <c>UseItemService</c> (which
    /// owns ALL multiplayer sync — ItemToken + GameActionType.UseItem — and re-validates the
    /// item itself). Otherwise this is a no-op and the chip simply returns to its fan home (the
    /// base <c>GrabbableBehaviour</c> restore already ran). Never mutates inventory directly.
    /// </summary>
    internal void OnChipReleased(ItemChip chip, Vector3 dropWorldPos, VRHand vrHand)
    {
        if (chip == null || !chip.IsActivatable || !_useSlotVisible || _useSlot == null)
            return;

        // Proximity test in world space (parenting-independent): capture radius scales with the
        // board so the pad stays the same on-screen size at any board scale.
        float scale = _useSlot.lossyScale.x;
        float radius = UseSlotRadius * (scale > 1e-4f ? scale : 1f);
        if ((dropWorldPos - _useSlot.position).sqrMagnitude > radius * radius)
            return; // dropped away from the pad — nothing to do

        CPlayerActor? actor = _hand != null ? _hand.PlayerActor : null;
        if (actor == null || chip.Item == null)
            return;

        try
        {
            // UseItemService handles the online GameAction send + local execution; passive/
            // non-usable items are rejected inside it (belt-and-braces with IsActivatable).
            new UseItemService(actor).UseItem(chip.Item);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Cards", $"Item use failed for '{chip.name}': {e.Message}");
            return;
        }

        vrHand.SendHaptic(HapticPreset.ClickPulse);
        VRLog.Info("Cards", $"Item '{chip.name}' USED via drop-to-use pad ({vrHand.Side}). " +
                            "Fan will rebuild when the item re-classifies (Spent/Consumed).");
        // Hide the pad now; the fan re-classifies the chip on the next state-change refresh.
        SetUseSlotVisible(false);
    }

    // ================================================================== item chip ==

    /// <summary>
    /// One physical item card in the pile: a slab carrying the item's name (readable),
    /// tinted by its pile state, pinch-grabbable so it can be taken into the hand to read.
    /// Grabbing snaps it upright + enlarged (<see cref="GetHeldPose"/>) — a tapped/consumed
    /// item still reads normally in hand (requirement 4). Consumed items also carry the
    /// game's burn plume (<see cref="BurnCardFx.SpawnConsumedPlume"/>), torn down with the chip.
    /// </summary>
    internal sealed class ItemChip : GrabbableBehaviour
    {
        internal enum Visual { Ready, Spent, Consumed }

        internal Visual State { get; private set; }

        /// <summary>The live inventory item this chip represents (read-only — the ONLY write is the
        /// single <c>UseItemService.UseItem</c> call the owner makes on a drop-to-use).</summary>
        internal CItem? Item { get; private set; }

        /// <summary>True when this item can actually be USED right now (requirement 3): non-passive
        /// AND Useable/Selected — the exact gate <c>UseItemService.UseItem</c> enforces. Drives the
        /// dynamic use pad's visibility and the drop-to-use.</summary>
        internal bool IsActivatable { get; private set; }

        private ItemsPile? _owner; // for the drop-to-use callback on release
        private Vector3 _homePos;
        private Quaternion _homeRot;
        private Material? _faceMaterial;
        private GameObject? _plume; // consumed-item smoke, destroyed with the chip

        internal static ItemChip Create(ItemsPile owner, Transform parent, CItem item)
        {
            Visual state = Classify(item);
            float w = CardsConfig.CardWidth.Value;
            float h = CardsConfig.CardHeight;

            var go = new GameObject($"ItemChip_{Name(item)}");
            go.transform.SetParent(parent, worldPositionStays: false);

            // Face slab (a thin card). +Z points away from the viewer (uGUI/sprites read from -Z).
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Face";
            Object.Destroy(slab.GetComponent<Collider>());
            slab.transform.SetParent(go.transform, worldPositionStays: false);
            slab.transform.localScale = new Vector3(w, h, 0.0022f);
            Color color = FaceColor(state);
            Material? faceMaterial = null;
            var renderer = slab.GetComponent<MeshRenderer>();
            Shader? shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                faceMaterial = new Material(shader) { color = color };
                renderer.sharedMaterial = faceMaterial;
            }

            // Requirement 1: render the item's REAL art on the face. GetItemConfig(...).miniIcon is
            // a SYNCHRONOUS Sprite (no addressable async), drawn via a SpriteRenderer so the sprite's
            // own atlas UV rect is honoured (a raw material.mainTexture would show the whole atlas).
            // The parchment/spent/consumed FaceColor stays as the background tint BEHIND the icon.
            Sprite? icon = TryGetIcon(item);
            bool hasIcon = icon != null;
            if (hasIcon)
            {
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(go.transform, worldPositionStays: false);
                // Upper region of the face; name drops to a bottom strip (below) so they never overlap.
                iconGo.transform.localPosition = new Vector3(0f, h * 0.14f, -0.0018f);
                var sr = iconGo.AddComponent<SpriteRenderer>();
                sr.sprite = icon;
                // Consumed items are ashen: desaturate the art so it reads as spent/burnt.
                if (state == Visual.Consumed)
                    sr.color = new Color(0.62f, 0.60f, 0.58f);
                // Fit the sprite (aspect-preserving) into the icon region.
                Vector2 size = icon!.bounds.size;
                if (size.x > 1e-4f && size.y > 1e-4f)
                {
                    float s = Mathf.Min(w * 0.82f / size.x, h * 0.56f / size.y);
                    iconGo.transform.localScale = Vector3.one * s;
                }
            }

            // Item name on the viewer face (readable in hand — requirement 4). With an icon the name
            // sits in a bottom strip under the art; without one it keeps the centered full-face fit.
            var nameGo = new GameObject("Name");
            nameGo.transform.SetParent(go.transform, worldPositionStays: false);
            nameGo.transform.localPosition = new Vector3(0f, hasIcon ? -h * 0.36f : 0f, -0.0025f);
            var nameTmp = nameGo.AddComponent<TextMeshPro>();
            nameTmp.text = Name(item);
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color = state == Visual.Consumed ? new Color(0.75f, 0.72f, 0.7f)
                                                     : new Color(0.12f, 0.10f, 0.07f);
            WorldUI.NativeButtonSkin.ApplyFont(nameTmp);
            Core.TmpFit.Fit(nameTmp, w * (hasIcon ? 0.9f : 0.88f), h * (hasIcon ? 0.24f : 0.82f),
                            maxFontSize: 0.16f, wrap: true);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(w + 0.006f, h + 0.006f, 0.02f);
            box.isTrigger = true;

            var chip = go.AddComponent<ItemChip>();
            chip.snapToHand = true; // taken INTO the hand to read
            chip.State = state;
            chip.Item = item;
            chip._owner = owner;
            // Mirror UseItemService.UseItem's own accept gate EXACTLY so the pad never lies.
            chip.IsActivatable = item.YMLData != null
                && item.YMLData.Trigger != CItem.EItemTrigger.PassiveEffect
                && (item.SlotState == CItem.EItemSlotState.Useable
                    || item.SlotState == CItem.EItemSlotState.Selected);
            chip._faceMaterial = faceMaterial;
            Core.VRLayers.Apply(go);

            // FULLY CONSUMED → the same burn display as burnt cards (reuse BurnCardFx).
            if (state == Visual.Consumed)
                chip._plume = BurnCardFx.SpawnConsumedPlume(go.transform);

            return chip;
        }

        /// <summary>
        /// Resolve the item's face art SYNCHRONOUSLY (requirement 1): <c>UIInfoTools.GetItemConfig</c>
        /// keyed on <c>YMLData.Art</c> returns an <c>ItemConfigUI</c> whose <c>miniIcon</c> is a plain
        /// <see cref="Sprite"/> — no addressable/async load (unlike <c>BackgroundImage</c>). Fully
        /// null-guarded: a missing tools singleton / config / icon falls back to the colored face.
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
                return null; // GetItemConfig touches SceneController YML — guard a mid-load call
            }
        }

        /// <summary>Seat the chip in its arc slot (its "home" the base restores it to on release).</summary>
        internal void SetHome(Vector3 pos, Quaternion rot, float scale)
        {
            _homePos = pos;
            _homeRot = rot;
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
            base.OnGrab(hand); // snaps to the reading pose
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Cards", $"Item chip '{name}' taken into hand ({hand.Side}) — readable (state {State}).");
        }

        /// <summary>
        /// Requirement 3 (drop-to-use): capture the drop location BEFORE the base restores the chip
        /// to its fan home, then offer it to the owner's use pad. The base restore always runs, so a
        /// chip dropped anywhere else (or a non-activatable chip) simply returns to the fan as before.
        /// </summary>
        public override void OnRelease(VRHand hand, Vector3 velocity)
        {
            // While held (snapToHand) the chip sits at the hand's grab anchor, so its world position
            // IS the drop point. Capture it before base.OnRelease re-parents it back to the fan.
            Vector3 dropWorldPos = transform.position;
            base.OnRelease(hand, velocity); // detach + restore fan home
            _owner?.OnChipReleased(this, dropWorldPos, hand);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_plume != null)
            {
                Object.Destroy(_plume);
                _plume = null;
            }
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
            // item.YMLData.Name is a LOCALIZATION KEY, not display text — showing it raw is the "ItemName"
            // bug. Resolve it exactly like the flat game (ItemCardUI.cs:231 LocalizationManager.GetTranslation)
            // via the mod's Loc helper; fall back to the key only if there is no translation.
            string? key = item != null && item.YMLData != null ? item.YMLData.Name : null;
            if (string.IsNullOrEmpty(key))
                return "?";
            return Core.Loc.Game(key!, key!);
        }
    }
}
