using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Short-rest / long-rest tokens on the play tray (pokeable).
///
/// Short rest: poke → <c>ShortRest.MouseClick()</c> (via CardsGameApi.ToggleShortRest),
/// i.e. exactly the 2D widget path — the game's own yes/no confirmation dialog, then
/// <c>CardsHandUI.PerformShortRest</c> with its burn/redraw dialogs. Long rest: poke →
/// toggles the long-rest pseudo-card (CardID −1); the burn-a-discarded-card step on
/// the long-rested turn arrives later as <c>CardHandMode.LoseCard</c> and is served by
/// the fan's poke-to-select path. Confirmations stay the game's 2D dialogs until P3c
/// physicalizes them.
/// Tokens dim when the action is unavailable (same gating the 2D widgets use — see
/// CardsGameApi.CanShortRest/CanLongRest).
/// </summary>
internal sealed class RestControls
{
    private RestToken? _shortToken;
    private RestToken? _longToken;

    /// <summary>Raised on poke; CardsDriver queues the actual game call.</summary>
    internal System.Action? ShortRestRequested;
    internal System.Action? LongRestRequested;

    internal void EnsureBuilt(PlayTray tray)
    {
        if (_shortToken == null && tray.ShortRestAnchor != null)
            _shortToken = RestToken.Create(tray.ShortRestAnchor, "ZZZ", new Color(0.85f, 0.75f, 0.35f),
                () => ShortRestRequested?.Invoke());
        if (_longToken == null && tray.LongRestAnchor != null)
            _longToken = RestToken.Create(tray.LongRestAnchor, "99", new Color(0.5f, 0.65f, 0.9f),
                () => LongRestRequested?.Invoke());
    }

    internal void Destroy()
    {
        if (_shortToken != null)
            Object.DestroyImmediate(_shortToken.gameObject);
        if (_longToken != null)
            Object.DestroyImmediate(_longToken.gameObject);
        _shortToken = null;
        _longToken = null;
    }

    /// <summary>Refresh availability/selected tinting (call per frame while the tray shows).</summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        bool canShort = false, canLong = false, shortSelected = false, longSelected = false;
        if (hand != null)
        {
            canShort = CardsGameApi.CanShortRest(hand);
            canLong = CardsGameApi.CanLongRest(hand);
            shortSelected = CardsGameApi.IsShortRestSelected(hand);
            longSelected = CardsGameApi.IsLongRestSelected(hand);
        }
        _shortToken?.SetState(canShort, shortSelected);
        _longToken?.SetState(canLong, longSelected);
    }

    /// <summary>One pokeable token: disc + label, dimmed when unavailable.</summary>
    private sealed class RestToken : PokeableBehaviour
    {
        private System.Action? _onPoke;
        private Material? _material;
        private Color _baseColor;
        private bool _enabled0 = true;

        internal static RestToken Create(Transform anchor, string label, Color color, System.Action onPoke)
        {
            var go = new GameObject("RestToken");
            go.transform.SetParent(anchor, worldPositionStays: false);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(go.transform, worldPositionStays: false);
            disc.transform.localScale = new Vector3(0.035f, 0.004f, 0.035f);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Material? material = null;
            Shader? shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                material = new Material(shader) { color = color };
                disc.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, worldPositionStays: false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.004f); // viewer side (-Z)
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.fontSize = 0.55f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;
            ((RectTransform)textGo.transform).sizeDelta = new Vector2(0.05f, 0.03f);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.04f, 0.04f, 0.015f);
            box.isTrigger = true;

            var token = go.AddComponent<RestToken>();
            token._onPoke = onPoke;
            token._material = material;
            token._baseColor = color;
            return token;
        }

        internal void SetState(bool enabled, bool selected)
        {
            _enabled0 = enabled;
            if (_material == null)
                return;
            Color color = enabled ? _baseColor : Color.Lerp(_baseColor, Color.gray, 0.75f);
            if (selected)
                color = Color.Lerp(color, Color.white, 0.45f);
            if (_material.color != color)
                _material.color = color;
        }

        public override void OnPoke(VRHand hand)
        {
            if (!_enabled0)
                return;
            hand.SendHaptic(HapticPreset.ClickPulse);
            _onPoke?.Invoke();
        }

        public override void OnPokeEnter(VRHand hand)
        {
            if (_enabled0)
                hand.SendHaptic(HapticPreset.HoverTick);
        }
    }
}
