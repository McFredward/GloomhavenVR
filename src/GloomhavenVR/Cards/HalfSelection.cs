using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// In-turn action selection: the round's played cards sit docked in the control
/// board's card slots (test #19; head-floating layout only as the no-tray fallback);
/// poking the top or bottom half commits that half via
/// <c>FullAbilityCard.OnAbilityClick(ActionType, isProxyAction: false, checkValid: true)</c>
/// — the identical call the 2D buttons make, so all validity/phase guards apply
/// (see CardsGameApi.PlayHalf). The CARD HALVES are the whole affordance (test #19):
/// no mod-side "ATK 2"-style buttons — the game's card art already shows the values,
/// and the game's own face widgets (default-action buttons, consume/infusion
/// buttons) stay reachable because each card's world canvas is registered with the
/// P2 <c>UguiPokeSurfaces</c> while in this layout (finger poke AND dominant laser
/// via <c>RayUguiDriver</c>). Laser commits therefore run through the REAL
/// 'Top button'/'Bottom button' uGUI click path; the mod zones mirror that hover as
/// a per-half tint (gold = top, teal = bottom) and additionally handle fingertip
/// pokes. Invalid halves are dimmed using the same query the UI renders from
/// (<c>FullAbilityCard.IsInteractable + isValid</c>). The
/// <c>CardsActionControlller</c> phase machine (Select1st → Pick1st → Select2nd →
/// Pick2nd) drives which halves report playable — we only mirror it.
/// </summary>
internal sealed class HalfSelection
{
    private sealed class ZoneSet
    {
        public GameObject Root = null!;
        public HalfZone Top = null!;
        public HalfZone Bottom = null!;
        public Canvas? RegisteredCanvas;
    }

    private readonly List<VRCard> _cards = new(4);
    private readonly Dictionary<VRCard, ZoneSet> _zones = new(4);
    private Transform? _root;
    private PlayTray? _tray;
    private bool _placed;

    /// <summary>Poke commit request: (card, half). CardsDriver executes it.</summary>
    internal Action<VRCard, CBaseCard.ActionType>? PlayRequested;

    internal bool IsVisible => _root != null && _root.gameObject.activeSelf;

    internal bool Contains(VRCard card) => _cards.Contains(card);

    /// <summary>
    /// Test #19: the action-selection display lives ON the control board — the round
    /// cards dock straight into the tray's two card slots (same position, frame and
    /// SlotScale density as during selection; stable under tray follow/pin/move/
    /// resize because the cards parent under the slot transforms, exactly like
    /// <see cref="PlayTray.PlaceCard"/>). The head-floating layout survives only as
    /// the no-tray fallback.
    /// </summary>
    internal void DockTo(PlayTray? tray) => _tray = tray;

    /// <summary>Dock target for card <paramref name="index"/>; null → floating fallback.</summary>
    private Transform? DockSlot(int index) =>
        _tray != null && _tray.Root != null ? _tray.SlotTransform(index) : null;

    // ------------------------------------------------------------------ lifecycle --

    internal void EnsureBuilt(Transform anchorParent)
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
        _root = new GameObject("GloomhavenVR.HalfSelection").transform;
        _root.SetParent(anchorParent, worldPositionStays: false);
        _placed = false;
    }

    internal void SetVisible(bool visible)
    {
        if (_root == null)
            return;
        if (_root.gameObject.activeSelf != visible)
            _root.gameObject.SetActive(visible);
        if (visible && !_placed && DockSlot(0) == null)
            PlaceAtHead(); // floating fallback only — docked cards live on the tray
        if (!visible)
            ClearCards();
    }

    internal void InvalidatePlacement() => _placed = false;

    internal void Destroy()
    {
        ClearCards();
        if (_root != null)
        {
            UnityEngine.Object.DestroyImmediate(_root.gameObject);
            _root = null;
        }
    }

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
        // Slightly above/behind the tray pose: comfortable reading + poke distance.
        Vector3 pos = headT.position
                      + flatForward * (CardsConfig.TrayForward.Value * 0.95f * scale)
                      + Vector3.up * (-(CardsConfig.TrayDown.Value - 0.14f) * scale);
        _root.position = pos;
        _root.rotation = Quaternion.LookRotation(flatForward, Vector3.up)
                         * Quaternion.Euler(-18f, 0f, 0f);
        _placed = true;
    }

    // ------------------------------------------------------------------ content --

    /// <summary>Lay out the played cards (1 or 2) and arm their poke zones.</summary>
    internal void SetCards(List<VRCard> cards)
    {
        // Disarm zones of cards leaving the layout.
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (!cards.Contains(_cards[i]))
                DisarmCard(_cards[i]);
        }

        _cards.Clear();
        _cards.AddRange(cards);
        if (_root == null)
            return;

        float w = CardsConfig.CardWidth.Value;
        const float layoutScale = 1.45f;
        int n = _cards.Count;
        for (int i = 0; i < n; i++)
        {
            VRCard card = _cards[i];
            if (card == null)
                continue;
            card.gameObject.SetActive(true);
            card.Grabbable = false; // pokes only in this layout
            Transform? slot = DockSlot(i);
            if (slot != null)
            {
                // Docked (test #19): the round cards stay in the SAME tray slots
                // they were played into — home scale 1 under the slot root, so the
                // slot's own SlotScale is the card density (test #18 pattern).
                card.SetHome(slot, Vector3.zero, Quaternion.identity, 1f);
            }
            else
            {
                float x = n > 1 ? (i == 0 ? -0.75f : 0.75f) * w * layoutScale : 0f;
                card.SetHome(_root, new Vector3(x, 0f, 0f), Quaternion.identity, layoutScale);
            }
            ArmCard(card);
        }
    }

    internal void ClearCards()
    {
        for (int i = _cards.Count - 1; i >= 0; i--)
            DisarmCard(_cards[i]);
        _cards.Clear();
        if (_laserZone != null)
            _laserZone.SetLaserHover(false);
        _laserZone = null;
    }

    // ------------------------------------------------------------------ zones --

    private void ArmCard(VRCard card)
    {
        if (_zones.TryGetValue(card, out ZoneSet existing))
        {
            existing.Root.SetActive(true);
            RegisterCanvas(card, existing);
            return;
        }

        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        var set = new ZoneSet { Root = new GameObject("HalfZones") };
        set.Root.transform.SetParent(card.transform, worldPositionStays: false);

        // No default-action chips (test #19): the game's own face widgets cover the
        // default "Attack 2"/"Move 2" options via the registered canvas below.
        set.Top = HalfZone.Create(set.Root.transform, card, CBaseCard.ActionType.TopAction,
            new Vector3(0f, h * 0.27f, 0f), new Vector2(w * 0.96f, h * 0.42f), this);
        set.Bottom = HalfZone.Create(set.Root.transform, card, CBaseCard.ActionType.BottomAction,
            new Vector3(0f, -h * 0.27f, 0f), new Vector2(w * 0.96f, h * 0.42f), this);
        Core.VRLayers.Apply(set.Root); // mod layer (render-only; zones poke via registries)

        _zones[card] = set;
        RegisterCanvas(card, set);
    }

    private void RegisterCanvas(VRCard card, ZoneSet set)
    {
        if (set.RegisteredCanvas != null)
            return;
        Canvas? canvas = card.GetComponentInChildren<Canvas>(true);
        if (canvas != null)
        {
            UguiPokeSurfaces.Register(canvas);
            set.RegisteredCanvas = canvas;
        }
    }

    private void DisarmCard(VRCard card)
    {
        if (card == null || !_zones.TryGetValue(card, out ZoneSet set))
            return;
        if (set.RegisteredCanvas != null)
        {
            UguiPokeSurfaces.Unregister(set.RegisteredCanvas);
            set.RegisteredCanvas = null;
        }
        if (set.Root != null)
            set.Root.SetActive(false);
    }

    internal void DestroyZonesFor(VRCard card)
    {
        if (!_zones.TryGetValue(card, out ZoneSet set))
            return;
        if (set.RegisteredCanvas != null)
            UguiPokeSurfaces.Unregister(set.RegisteredCanvas);
        if (set.Root != null)
            UnityEngine.Object.Destroy(set.Root);
        _zones.Remove(card);
    }

    // ------------------------------------------------------------------ per frame --

    /// <summary>
    /// Refresh valid/invalid dimming from the game's own interactability query and
    /// mirror the dominant laser's uGUI hover as the per-half tint.
    /// </summary>
    internal void Tick(VRHand? dominant)
    {
        if (!IsVisible)
            return;
        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard card = _cards[i];
            if (card == null || !_zones.TryGetValue(card, out ZoneSet set))
                continue;
            FullAbilityCard? full = card.FullCard;
            set.Top.SetPlayable(full != null && CardsGameApi.IsHalfPlayable(full, CBaseCard.ActionType.TopAction));
            set.Bottom.SetPlayable(full != null && CardsGameApi.IsHalfPlayable(full, CBaseCard.ActionType.BottomAction));
        }
        UpdateLaserHover(dominant);
    }

    private HalfZone? _laserZone;

    /// <summary>
    /// Laser hover feedback (test #19): the card faces are registered with
    /// <c>UguiPokeSurfaces</c> while in this layout, so the dominant hand's
    /// <c>RayUguiDriver</c> already hovers AND trigger-clicks the REAL half widgets
    /// ('Top button'/'Bottom button' — the exact uGUI targets the 2D click path
    /// uses; the commit stays there, so there is no second laser commit path to
    /// double-fire). This only MIRRORS that hover as the per-half tint: map the
    /// hovered widget to its card, split top/bottom on the ray∩card-plane hit
    /// (same math as <see cref="PlayTray.TryRaycastCards"/>). No allocations.
    /// </summary>
    private void UpdateLaserHover(VRHand? dominant)
    {
        HalfZone? hit = null;
        GameObject? hovered = dominant != null && dominant.RayUgui.HasHit
            ? dominant.RayUgui.Hovered
            : null;
        if (dominant != null && hovered != null)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                VRCard card = _cards[i];
                if (card == null || !_zones.TryGetValue(card, out ZoneSet set)
                    || !hovered.transform.IsChildOf(card.transform))
                    continue;
                PickPose pick = dominant.Ray.Current;
                Transform t = card.transform;
                float denom = Vector3.Dot(pick.Direction, t.forward);
                if (denom < 1e-5f)
                    break;
                float dist = Vector3.Dot(t.position - pick.Origin, t.forward) / denom;
                if (dist <= 0f)
                    break;
                Vector3 local = t.InverseTransformPoint(pick.Origin + pick.Direction * dist);
                hit = local.y >= 0f ? set.Top : set.Bottom;
                break;
            }
        }

        if (ReferenceEquals(hit, _laserZone))
            return;
        if (_laserZone != null)
            _laserZone.SetLaserHover(false);
        _laserZone = hit;
        if (_laserZone != null)
            _laserZone.SetLaserHover(true);
    }

    internal void RequestPlay(VRCard card, CBaseCard.ActionType type)
    {
        try
        {
            PlayRequested?.Invoke(card, type);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Cards", $"PlayRequested subscriber threw: {ex}");
        }
    }

    /// <summary>One pokeable half zone with a state overlay quad.</summary>
    private sealed class HalfZone : PokeableBehaviour
    {
        private VRCard _card = null!;
        private CBaseCard.ActionType _type;
        private HalfSelection _owner = null!;
        private Material? _overlay;
        private bool _playable;
        private bool _hovered;
        private bool _laserHovered;

        private static readonly Color InvalidColor = new(0f, 0f, 0f, 0.55f);
        // Distinct per-half hover tints (test #19): the glow itself says WHICH
        // action a commit would play — warm gold for top, cool teal for bottom.
        private static readonly Color TopHoverColor = new(1f, 0.82f, 0.35f, 0.26f);
        private static readonly Color BottomHoverColor = new(0.35f, 0.75f, 1f, 0.26f);
        private static readonly Color ClearColor = new(0f, 0f, 0f, 0f);

        internal static HalfZone Create(Transform parent, VRCard card, CBaseCard.ActionType type,
            Vector3 localPos, Vector2 size, HalfSelection owner)
        {
            var go = new GameObject($"Zone_{type}");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, 0.012f);
            box.isTrigger = true;

            // Overlay quad on the viewer side (-Z): dim when invalid, glow on hover.
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Overlay";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(go.transform, worldPositionStays: false);
            quad.transform.localPosition = new Vector3(0f, 0f, -0.0018f); // viewer side (-Z)
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            Material? overlay = null;
            Shader? shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                overlay = new Material(shader) { color = ClearColor };
                quad.GetComponent<MeshRenderer>().sharedMaterial = overlay;
            }

            var zone = go.AddComponent<HalfZone>();
            zone._card = card;
            zone._type = type;
            zone._owner = owner;
            zone._overlay = overlay;
            return zone;
        }

        internal void SetPlayable(bool playable)
        {
            if (_playable == playable && _overlay == null)
                return;
            _playable = playable;
            UpdateOverlay();
        }

        /// <summary>
        /// Laser hover tint (set by <see cref="UpdateLaserHover"/>). Purely visual —
        /// the laser COMMIT runs through RayUguiDriver's real uGUI click.
        /// </summary>
        internal void SetLaserHover(bool hovered)
        {
            if (_laserHovered == hovered)
                return;
            _laserHovered = hovered;
            UpdateOverlay();
        }

        private void UpdateOverlay()
        {
            if (_overlay == null)
                return;
            Color color;
            if (!_playable)
                color = InvalidColor;
            else if (_hovered || _laserHovered)
                color = _type == CBaseCard.ActionType.TopAction ? TopHoverColor : BottomHoverColor;
            else
                color = ClearColor;
            if (_overlay.color != color)
                _overlay.color = color;
        }

        public override void OnPokeEnter(VRHand hand)
        {
            _hovered = true;
            UpdateOverlay();
            if (_playable)
                hand.SendHaptic(HapticPreset.HoverTick);
        }

        public override void OnPokeExit(VRHand hand)
        {
            _hovered = false;
            UpdateOverlay();
        }

        public override void OnPoke(VRHand hand)
        {
            if (!_playable)
                return;
            hand.SendHaptic(HapticPreset.ClickPulse);
            _owner.RequestPlay(_card, _type);
        }
    }
}
