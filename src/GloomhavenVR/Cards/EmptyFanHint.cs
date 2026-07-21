using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Task #9 (empty-fan feedback): when the palm-roll gate opens but the hand has ZERO
/// cards, nothing used to appear — which read as "the fan is broken". This shows a
/// small ghost placard at the exact spot the fan would open (palm + FanPalmOffset,
/// facing the head like the fan does): a parchment-toned card-sized plate with a
/// localized "Keine Handkarten / No hand cards" line, fading out over ~1.5 s. Purely
/// visual — no colliders, no interaction, mod layer only. The driver edge-triggers
/// <see cref="Show"/> (once per gate opening, never mid-modal) and pumps
/// <see cref="Tick"/> every frame; unscaled time so a paused selection still fades.
/// </summary>
internal sealed class EmptyFanHint
{
    private const float FadeSeconds = 1.5f;
    private const float PlateAlpha = 0.55f;
    private const float TextAlpha = 0.95f;

    private static readonly Color Parchment = new(0.85f, 0.78f, 0.62f);
    private static readonly Color InkBrown = new(0.24f, 0.17f, 0.10f);

    private GameObject? _root;
    private Material? _plateMaterial;
    private TextMeshPro? _label;
    private float _elapsed = -1f; // -1 = idle

    /// <summary>Place the placard at the would-be fan position and start the fade.</summary>
    internal void Show(VRHand hand)
    {
        Transform? palm = hand != null ? hand.Rig.PalmCenter : null;
        if (palm == null)
            return;
        EnsureBuilt();
        if (_root == null)
            return;

        // Same spawn math as CardFan.Tick's palm target: FanPalmOffset up the palm
        // normal, scaled by the true diorama scale (NOT palm.lossyScale — the glove
        // armature carries a 100× that would fling the placard out of view).
        float scale = Mathf.Max(hand!.WorldScale, 1e-4f);
        Vector3 pos = palm.position + palm.up * (CardsConfig.FanPalmOffset.Value * scale);
        _root.transform.position = pos;
        _root.transform.localScale = Vector3.one * scale;

        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            Vector3 away = pos - head.transform.position;
            if (away.sqrMagnitude > 1e-6f)
                _root.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        if (_label != null)
        {
            // Re-read per show: cheap, and it follows a live language switch.
            _label.text = Loc.CurrentLanguage == "German" ? "Keine Handkarten" : "No hand cards";
        }
        _elapsed = 0f;
        ApplyAlpha(1f);
        _root.SetActive(true);
    }

    /// <summary>Advance the fade (no-op while idle); hides itself when done.</summary>
    internal void Tick()
    {
        if (_elapsed < 0f || _root == null)
            return;
        _elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float p = Mathf.Clamp01(_elapsed / FadeSeconds);
        if (p >= 1f)
        {
            Hide();
            return;
        }
        ApplyAlpha(1f - p * p); // ease-in fade: holds readable, then ghosts away
    }

    /// <summary>Hide immediately (fade finished / a modal opened mid-fade).</summary>
    internal void Hide()
    {
        _elapsed = -1f;
        if (_root != null)
            _root.SetActive(false);
    }

    internal void Destroy()
    {
        _elapsed = -1f;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        _plateMaterial = null;
        _label = null;
    }

    // ------------------------------------------------------------------ internals --

    private void EnsureBuilt()
    {
        if (_root != null)
            return;

        _root = new GameObject("GloomhavenVR.EmptyFanHint");
        Object.DontDestroyOnLoad(_root);
        VRLayers.Apply(_root);

        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        // Parchment plate: a collider-free alpha-blended quad (deliberately NOT the
        // additive slot-glow material — parchment must read as a soft solid, and the
        // dark ink line must sit legibly on it).
        var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = "Plate";
        Object.Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(_root.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(w * 1.1f, h * 0.42f, 1f); // low, placard-like strip
        VRLayers.Apply(plate);
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            _plateMaterial = new Material(shader) { color = new Color(Parchment.r, Parchment.g, Parchment.b, PlateAlpha) };
            plate.GetComponent<MeshRenderer>().sharedMaterial = _plateMaterial;
        }

        // Ink line, slightly proud of the plate (toward the viewer = -Z, like the cards).
        var textGo = new GameObject("Label");
        textGo.transform.SetParent(_root.transform, worldPositionStays: false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        VRLayers.Apply(textGo);
        _label = textGo.AddComponent<TextMeshPro>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.color = new Color(InkBrown.r, InkBrown.g, InkBrown.b, TextAlpha);
        TmpFit.Fit(_label, w * 1.0f, h * 0.34f, maxFontSize: 0f, wrap: false); // cap from box height

        _root.SetActive(false);
    }

    private void ApplyAlpha(float k)
    {
        if (_plateMaterial != null)
        {
            Color c = _plateMaterial.color;
            c.a = PlateAlpha * k;
            _plateMaterial.color = c;
        }
        if (_label != null)
        {
            Color c = _label.color;
            c.a = TextAlpha * k;
            _label.color = c;
        }
    }
}
