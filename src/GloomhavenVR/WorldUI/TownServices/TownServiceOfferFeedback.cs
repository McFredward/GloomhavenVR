using GloomhavenVR.Hands;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Original owner's physical offer intent and local controller feedback.
/// Card pre-drop guides are local-only by the maintainer's explicit exception;
/// purse guide appearance and feedback still belong to shared presentation.</summary>
internal sealed class TownServiceOfferFeedback
{
    private readonly CanvasGroup? _gate;
    private readonly Transform? _zone;
    private readonly Image? _border;
    private bool _near, _snap;
    private float _strength;
    private float _nextNearPulse, _nextSnapPulse;
    private VRHand? _lastHand;

    internal TownServiceOfferFeedback(CanvasGroup? gate = null, Transform? zone = null)
    {
        _gate = gate; _zone = zone;
        _border = zone != null ? zone.Find("Border")?.GetComponent<Image>() : null;
    }

    // Distances are measured from the actual release point in the destination's local frame.
    // The inner edge must agree with the release volume, not merely look close in world space.
    internal float Tick(bool eligible, VRHand? hand, float distance, bool inside, float approach,
        bool snapPulse = true)
    {
        if (hand != null && _lastHand != null && !ReferenceEquals(hand, _lastHand))
        { _near = _snap = false; _nextNearPulse = _nextSnapPulse = 0f; }
        if (hand != null) _lastHand = hand;
        bool near = eligible && hand != null && hand.HasPose && distance <= approach;
        bool snap = near && inside;
        if (near && !_near && Time.unscaledTime >= _nextNearPulse)
        { hand!.SendHaptic(HapticPreset.HoverTick); _nextNearPulse = Time.unscaledTime + .25f; }
        if (snap && !_snap && snapPulse && Time.unscaledTime >= _nextSnapPulse)
        { hand!.SendHaptic(HapticPreset.ClickPulse); _nextSnapPulse = Time.unscaledTime + .30f; }
        _near = near; _snap = snap;
        float target = snap ? 1f : near ? Mathf.Clamp01(1f - distance / approach) * .65f + .20f : 0f;
        _strength = Mathf.MoveTowards(_strength, target, Time.unscaledDeltaTime * 7f);
        return _strength;
    }

    internal void Clear() { _near = _snap = false; _strength = 0f; }

    internal void Paint(bool actionable, bool preview = false)
    {
        if (_gate == null || _zone == null) return;
        _gate.alpha = actionable ? 1f : preview ? .38f : 0f;
        // The zone is a preview, not an input surface. Only its ink reacts; its actual
        // accept volume and native button remain untouched.
        PaintInk(_zone, _border, _strength, preview && !actionable);
    }

    internal static void PaintInk(Transform zone, Image? border, float strength, bool preview = false)
    {
        if (border != null) border.color = preview
            ? new Color(.69f, .65f, .45f, .40f)
            : Color.Lerp(new Color(.24f, .67f, .34f, .48f),
                new Color(.43f, 1f, .60f, .85f), strength);
        zone.localScale = Vector3.one * (.001f * (1f + .055f * strength));
    }
}
