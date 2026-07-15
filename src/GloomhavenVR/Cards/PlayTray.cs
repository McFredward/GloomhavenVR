using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// The floating play tray: two card slots (slot 0 = initiative) plus rest-token
/// anchors. Anchored in rig space (moves with the table), placed relative to the HMD
/// when card selection starts; config offsets tune the pose ([Cards] Tray*).
/// Slot order == initiative order: <see cref="SyncFromGameState"/> mirrors
/// <c>CCharacterClass.RoundAbilityCards/InitiativeAbilityCard</c> into the slots, and
/// physically swapping the two cards (or poking the initiative badge) drives the
/// game's own <c>AbilityCardUI.SwapInitiative()</c> (via <see cref="CardsGameApi"/>).
/// Bundle asset <c>PlayTray.prefab</c> (children <c>Slot1/Slot2/ShortRestToken/LongRestToken</c>,
/// see unity/.../Table/README.md) with a full procedural fallback.
/// </summary>
internal sealed class PlayTray
{
    private const float SlotCaptureRadius = 0.11f; // meters, scaled by tray lossyScale

    private Transform? _root;
    private Transform?[] _slots = new Transform?[2];
    private Transform? _shortRestAnchor;
    private Transform? _longRestAnchor;
    private readonly VRCard?[] _occupants = new VRCard?[2];

    private TextMeshPro? _badge;
    private InitiativeBadgeZone? _badgeZone;
    private MeshRenderer? _readyLamp;
    private Material? _readyMaterial;
    private bool _placed;

    internal bool IsVisible => _root != null && _root.gameObject.activeSelf;

    internal Transform? ShortRestAnchor => _shortRestAnchor;
    internal Transform? LongRestAnchor => _longRestAnchor;
    internal Transform? Root => _root;

    /// <summary>Raised when the initiative badge is poked (CardsDriver queues the swap).</summary>
    internal System.Action? SwapRequested;

    // ------------------------------------------------------------------ lifecycle --

    internal void EnsureBuilt(VRCardFactory factory, Transform anchorParent)
    {
        if (_root != null)
        {
            if (_root.parent != anchorParent)
            {
                _root.SetParent(anchorParent, worldPositionStays: false);
                _placed = false;
            }
            return;
        }

        _root = new GameObject("GloomhavenVR.PlayTray").transform;
        _root.SetParent(anchorParent, worldPositionStays: false);

        GameObject? prefab = factory.GetTrayPrefab();
        if (prefab != null)
        {
            GameObject visual = Object.Instantiate(prefab, _root, false);
            visual.name = "TrayVisual";
            _slots[0] = FindDeep(visual.transform, "Slot1");
            _slots[1] = FindDeep(visual.transform, "Slot2");
            _shortRestAnchor = FindDeep(visual.transform, "ShortRestToken");
            _longRestAnchor = FindDeep(visual.transform, "LongRestToken");
        }

        if (_slots[0] == null || _slots[1] == null)
            BuildProceduralTray();

        BuildBadge();
        BuildReadyLamp();
        _placed = false;
    }

    internal void Destroy()
    {
        _occupants[0] = _occupants[1] = null;
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
        }
        _slots = new Transform?[2];
        _badge = null;
        _badgeZone = null;
        _readyLamp = null;
        _placed = false;
    }

    internal void SetVisible(bool visible)
    {
        if (_root == null)
            return;
        if (_root.gameObject.activeSelf != visible)
            _root.gameObject.SetActive(visible);
        if (visible && !_placed)
            PlaceAtHead();
    }

    /// <summary>Position the tray in rig space from the current head pose + config offsets.</summary>
    internal void PlaceAtHead()
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
        Vector3 right = Vector3.Cross(Vector3.up, flatForward);

        float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        Vector3 offset = CardsConfig.TrayOffset; // (right, -down, forward), real meters
        Vector3 pos = headT.position
                      + flatForward * (offset.z * scale)
                      + right * (offset.x * scale)
                      + Vector3.up * (offset.y * scale);

        _root.position = pos;
        _root.rotation = Quaternion.LookRotation(flatForward, Vector3.up)
                         * Quaternion.Euler(-CardsConfig.TrayTilt.Value, 0f, 0f);
        _placed = true;
        VRLog.Debug("Cards", "Play tray placed at head-relative pose.");
    }

    /// <summary>Force re-placement next time the tray shows (mode re-entry).</summary>
    internal void InvalidatePlacement() => _placed = false;

    // ------------------------------------------------------------------ slots --

    internal VRCard? Occupant(int slot) => _occupants[slot];

    internal int SlotOf(VRCard card)
    {
        if (_occupants[0] == card) return 0;
        if (_occupants[1] == card) return 1;
        return -1;
    }

    internal bool ContainsCard(VRCard card) => SlotOf(card) >= 0;

    /// <summary>
    /// Which slot would capture a card released at <paramref name="worldPos"/>?
    /// Returns -1 when outside both capture radii.
    /// </summary>
    internal int SlotAt(Vector3 worldPos)
    {
        if (_root == null || !IsVisible)
            return -1;
        float scale = _root.lossyScale.x;
        float radius = SlotCaptureRadius * scale;
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot == null)
                continue;
            float dist = Vector3.Distance(worldPos, slot.position);
            if (dist <= radius && dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Visually park a card in a slot (game-state sync happens separately).</summary>
    internal void PlaceCard(VRCard card, int slot, bool instant = false)
    {
        if (_slots[slot] == null)
            return;
        // A card can only occupy one slot.
        if (_occupants[0] == card) _occupants[0] = null;
        if (_occupants[1] == card) _occupants[1] = null;
        _occupants[slot] = card;
        card.gameObject.SetActive(true);
        card.SetHome(_slots[slot]!, Vector3.zero, Quaternion.identity, 1f, instant);
    }

    internal void RemoveCard(VRCard card)
    {
        int slot = SlotOf(card);
        if (slot >= 0)
            _occupants[slot] = null;
    }

    internal void ClearSlots()
    {
        _occupants[0] = null;
        _occupants[1] = null;
    }

    /// <summary>
    /// Mirror the authoritative game state into the slots: slot 0 always shows the
    /// initiative card (<c>CCharacterClass.InitiativeAbilityCard</c>), slot 1 the
    /// other round card. Returns true when anything changed.
    /// </summary>
    internal bool SyncFromGameState(CardsHandUI hand, VRCardFactory factory)
    {
        if (hand.PlayerActor == null)
            return false;
        var round = hand.PlayerActor.CharacterClass.RoundAbilityCards;
        ScenarioRuleLibrary.CAbilityCard? initiative = hand.PlayerActor.CharacterClass.InitiativeAbilityCard;

        VRCard? want0 = null, want1 = null;
        for (int i = 0; i < round.Count && i < 2; i++)
        {
            AbilityCardUI? widget = FindWidget(hand, round[i]);
            if (widget == null)
                continue;
            VRCard card = factory.GetOrCreate(widget);
            bool isInitiative = initiative != null ? round[i] == initiative : i == 0;
            if (isInitiative && want0 == null)
                want0 = card;
            else if (want1 == null)
                want1 = card;
            else
                want0 ??= card;
        }

        bool changed = _occupants[0] != want0 || _occupants[1] != want1;
        _occupants[0] = null;
        _occupants[1] = null;
        if (want0 != null)
            PlaceCard(want0, 0);
        if (want1 != null)
            PlaceCard(want1, 1);
        return changed;
    }

    private static AbilityCardUI? FindWidget(CardsHandUI hand, ScenarioRuleLibrary.CAbilityCard card)
    {
        var cards = hand.cardsUI; // publicized private list
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null && cards[i].AbilityCard == card)
                return cards[i];
        }
        return null;
    }

    // ------------------------------------------------------------------ badge/lamp --

    /// <summary>Update the initiative badge + ready lamp (call each frame while visible; cheap).</summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        if (_badge == null)
            return;

        string text = "-";
        bool canSwap = false;
        bool ready = false;
        if (hand != null && hand.PlayerActor != null)
        {
            if (CardsGameApi.IsLongRestSelected(hand))
            {
                text = "99"; // long rest initiative by game rule
            }
            else
            {
                ScenarioRuleLibrary.CAbilityCard? initiative = CardsGameApi.InitiativeCard(hand);
                if (initiative != null)
                    text = CardsGameApi.InitiativeValue(initiative).ToString();
                canSwap = hand.PlayerActor.CharacterClass.RoundAbilityCards.Count == 2;
            }
            ready = CardsGameApi.IsSelectionReady(hand);
        }

        if (_badge.text != text)
            _badge.text = text;
        if (_badgeZone != null)
            _badgeZone.SwapEnabled = canSwap;
        if (_readyMaterial != null)
        {
            Color color = ready ? new Color(0.25f, 0.85f, 0.3f) : new Color(0.35f, 0.33f, 0.3f);
            if (_readyMaterial.color != color)
                _readyMaterial.color = color;
        }
    }

    // ------------------------------------------------------------------ build --

    private void BuildProceduralTray()
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "TrayBoard";
        Object.Destroy(board.GetComponent<Collider>());
        board.transform.SetParent(_root, worldPositionStays: false);
        board.transform.localScale = new Vector3(w * 3.6f, h * 1.35f, 0.008f);
        board.transform.localPosition = new Vector3(0f, 0f, 0.006f);
        Tint(board, new Color(0.16f, 0.13f, 0.10f));

        for (int i = 0; i < 2; i++)
        {
            var slot = new GameObject($"Slot{i + 1}").transform;
            slot.SetParent(_root, worldPositionStays: false);
            slot.localPosition = new Vector3((i == 0 ? -0.62f : 0.62f) * w, 0f, 0f);
            _slots[i] = slot;

            var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frame.name = "Frame";
            Object.Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(slot, worldPositionStays: false);
            frame.transform.localScale = new Vector3(w * 1.06f, h * 1.06f, 1f);
            frame.transform.localPosition = new Vector3(0f, 0f, 0.004f); // behind the card, in front of the board
            Tint(frame, i == 0 ? new Color(0.45f, 0.38f, 0.2f) : new Color(0.28f, 0.27f, 0.25f));
        }

        _shortRestAnchor = new GameObject("ShortRestToken").transform;
        _shortRestAnchor.SetParent(_root, worldPositionStays: false);
        _shortRestAnchor.localPosition = new Vector3(w * 1.55f, h * 0.28f, -0.01f);

        _longRestAnchor = new GameObject("LongRestToken").transform;
        _longRestAnchor.SetParent(_root, worldPositionStays: false);
        _longRestAnchor.localPosition = new Vector3(w * 1.55f, -h * 0.28f, -0.01f);
    }

    private void BuildBadge()
    {
        if (_root == null || _slots[0] == null)
            return;
        float h = CardsConfig.CardHeight;

        var badgeGo = new GameObject("InitiativeBadge");
        badgeGo.transform.SetParent(_slots[0], worldPositionStays: false);
        badgeGo.transform.localPosition = new Vector3(0f, h * 0.62f, 0f); // TMP reads from -Z (viewer side)

        _badge = badgeGo.AddComponent<TextMeshPro>();
        _badge.text = "-";
        _badge.fontSize = 1.1f;
        _badge.alignment = TextAlignmentOptions.Center;
        _badge.color = new Color(1f, 0.9f, 0.6f);
        var rect = (RectTransform)badgeGo.transform;
        rect.sizeDelta = new Vector2(0.09f, 0.035f);

        // Poke the badge to swap initiative (same as the 2D badge click).
        var zoneGo = new GameObject("SwapZone");
        zoneGo.transform.SetParent(badgeGo.transform, worldPositionStays: false);
        var box = zoneGo.AddComponent<BoxCollider>();
        box.size = new Vector3(0.08f, 0.035f, 0.02f);
        box.isTrigger = true;
        _badgeZone = zoneGo.AddComponent<InitiativeBadgeZone>();
        _badgeZone.Owner = this;
    }

    private void BuildReadyLamp()
    {
        if (_root == null)
            return;
        var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        lamp.name = "ReadyLamp";
        Object.Destroy(lamp.GetComponent<Collider>());
        lamp.transform.SetParent(_root, worldPositionStays: false);
        lamp.transform.localScale = Vector3.one * 0.018f;
        lamp.transform.localPosition = new Vector3(-CardsConfig.CardWidth.Value * 1.55f, 0f, -0.01f);
        _readyLamp = lamp.GetComponent<MeshRenderer>();
        Shader? shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            _readyMaterial = new Material(shader) { color = new Color(0.35f, 0.33f, 0.3f) };
            _readyLamp.sharedMaterial = _readyMaterial;
        }
    }

    private static void Tint(GameObject go, Color color)
    {
        var renderer = go.GetComponent<MeshRenderer>();
        Shader? shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Sprites/Default");
        if (shader != null)
            renderer.sharedMaterial = new Material(shader) { color = color };
    }

    private static Transform? FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform? found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>Poke target on the initiative badge → initiative swap.</summary>
    private sealed class InitiativeBadgeZone : PokeableBehaviour
    {
        internal PlayTray? Owner;
        internal bool SwapEnabled;

        public override void OnPoke(VRHand hand)
        {
            if (!SwapEnabled || Owner == null)
                return;
            hand.SendHaptic(HapticPreset.ClickPulse);
            Owner.SwapRequested?.Invoke();
        }

        public override void OnPokeEnter(VRHand hand)
        {
            if (SwapEnabled)
                hand.SendHaptic(HapticPreset.HoverTick);
        }
    }
}
