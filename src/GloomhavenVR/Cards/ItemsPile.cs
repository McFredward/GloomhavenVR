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

    private readonly List<ItemChip> _chips = new(12);
    private Transform? _root;
    private TextMeshPro? _title;
    private Transform? _anchor;   // the pile mount (rig-space, diorama-scaled) — placement scale ref
    private VRHand? _followHand;
    private CardsHandUI? _hand;
    private string _signature = string.Empty; // last-built inventory state, for cheap live refresh

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
        Transform parent = followHand != null ? followHand.Rig.PalmCenter
                                              : (_anchor != null ? _anchor : _root!.parent);
        if (parent != null && _root!.parent != parent)
            _root.SetParent(parent, worldPositionStays: false);
        _root!.gameObject.SetActive(true);
        IsOpen = true;
        _signature = string.Empty; // force a build
        Populate(hand);
        if (followHand != null)
            Tick(hand); // seat by the holding hand immediately
        else
            PlaceAtHead();
        VRLog.Info("Cards", $"Items pile browse OPEN ({(followHand != null ? "held in hand" : "toggled")}, " +
                            $"{_chips.Count} item(s)).");
    }

    internal void Close()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        _followHand = null;
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
        _hand = null;
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

        if (_followHand == null || _root == null)
            return;
        _root.localPosition = new Vector3(0f, HandPalmOffset, 0f);
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Vector3 away = _root.position - head.transform.position;
        if (away.sqrMagnitude < 1e-6f)
            return;
        _root.rotation = Quaternion.LookRotation(away.normalized, Vector3.up)
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
            ItemChip chip = ItemChip.Create(_root, item);
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

    /// <summary>Fixed head-relative reading pose (mirrors PileBrowser.PlaceAtHead).</summary>
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

        private Vector3 _homePos;
        private Quaternion _homeRot;
        private Material? _faceMaterial;
        private GameObject? _plume; // consumed-item smoke, destroyed with the chip

        internal static ItemChip Create(Transform parent, CItem item)
        {
            Visual state = Classify(item);
            float w = CardsConfig.CardWidth.Value;
            float h = CardsConfig.CardHeight;

            var go = new GameObject($"ItemChip_{Name(item)}");
            go.transform.SetParent(parent, worldPositionStays: false);

            // Face slab (a thin card). +Z points away from the viewer (uGUI reads from -Z).
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

            // Item name on the viewer face (readable in hand — requirement 4).
            var nameGo = new GameObject("Name");
            nameGo.transform.SetParent(go.transform, worldPositionStays: false);
            nameGo.transform.localPosition = new Vector3(0f, 0f, -0.0025f);
            var nameTmp = nameGo.AddComponent<TextMeshPro>();
            nameTmp.text = Name(item);
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color = state == Visual.Consumed ? new Color(0.75f, 0.72f, 0.7f)
                                                     : new Color(0.12f, 0.10f, 0.07f);
            WorldUI.NativeButtonSkin.ApplyFont(nameTmp);
            Core.TmpFit.Fit(nameTmp, w * 0.88f, h * 0.82f, maxFontSize: 0.16f, wrap: true);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(w + 0.006f, h + 0.006f, 0.02f);
            box.isTrigger = true;

            var chip = go.AddComponent<ItemChip>();
            chip.snapToHand = true; // taken INTO the hand to read
            chip.State = state;
            chip._faceMaterial = faceMaterial;
            Core.VRLayers.Apply(go);

            // FULLY CONSUMED → the same burn display as burnt cards (reuse BurnCardFx).
            if (state == Visual.Consumed)
                chip._plume = BurnCardFx.SpawnConsumedPlume(go.transform);

            return chip;
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
            string? n = item != null && item.YMLData != null ? item.YMLData.Name : null;
            return string.IsNullOrEmpty(n) ? "?" : n!;
        }
    }
}
