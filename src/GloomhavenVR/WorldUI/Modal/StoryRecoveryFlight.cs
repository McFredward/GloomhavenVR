using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>One finite recovery of one existing story presentation. Endpoints never follow the
/// headset; the coordinator retains native lifetime and shared/manual pose authority.</summary>
internal sealed class StoryRecoveryFlight
{
    internal const float Seconds = .62f;
    internal readonly UIWindow Window;
    internal readonly ConvertedPanel Panel;
    internal readonly GrabbableModal Grab;
    internal readonly SharedWindowKind Kind;
    internal readonly bool Online, SharedClaim;
    internal readonly Vector3 From, Target;
    private readonly Quaternion _fromRotation, _targetRotation;
    private readonly float _startedAt, _arc;

    internal StoryRecoveryFlight(UIWindow window, ConvertedPanel panel, GrabbableModal grab,
        SharedWindowKind kind, bool online, bool sharedClaim, Vector3 from, Quaternion fromRotation,
        Vector3 target, Quaternion targetRotation, float reference, float now)
    {
        Window = window; Panel = panel; Grab = grab; Kind = kind;
        Online = online; SharedClaim = sharedClaim;
        From = from; Target = target; _fromRotation = fromRotation; _targetRotation = targetRotation;
        _startedAt = now;
        _arc = Mathf.Min(.08f * reference, Vector3.Distance(from, target) * .05f);
    }

    internal bool Sample(float now, out Vector3 position, out Quaternion rotation)
    {
        float t = Mathf.Clamp01((now - _startedAt) / Seconds);
        float eased = t * t * (3f - 2f * t);
        position = Vector3.Lerp(From, Target, eased)
            + Vector3.up * (_arc * 4f * eased * (1f - eased));
        rotation = Quaternion.Slerp(_fromRotation, _targetRotation, eased);
        return t >= 1f;
    }
}
