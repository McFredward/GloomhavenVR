using UnityEngine;
using GloomhavenVR.Rig;

namespace GloomhavenVR.WorldUI;

/// <summary>The owner's floating palm display. Peers receive its actual sampled geometry;
/// they never aim it at their own camera or run a second animation clock.</summary>
internal static class TownServiceOfferingPose
{
    internal static void Place(Transform seat, Transform palm, Transform station, float age)
    {
        float scale = Mathf.Max(.0001f, Mathf.Abs(station.lossyScale.x));
        Vector3 forward = station.forward;
        if (VRRigDriver.HeadCamera != null) forward = palm.position - VRRigDriver.HeadCamera.transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
        Quaternion facing = Quaternion.LookRotation(forward.normalized, Vector3.up);
        // Keep the full portrait above the palm, irrespective of wrist roll/pitch. Small
        // continuous motion conveys suspension without making the drop target hard to hit.
        seat.SetPositionAndRotation(palm.position + Vector3.up * ((.17f + .006f * Mathf.Sin(age * 1.8f)) * scale),
            facing * Quaternion.Euler(0f, 1.5f * Mathf.Sin(age * .9f), 0f));
        seat.localScale = Vector3.one;
    }

    internal static bool Contains(Transform seat, Vector3 world)
    {
        Vector3 point = seat.InverseTransformPoint(world);
        return Mathf.Abs(point.x) <= .18f && Mathf.Abs(point.y) <= .20f && Mathf.Abs(point.z) <= .16f;
    }
}

/// <summary>One finite settle of the actual card into the animated display frame.</summary>
internal sealed class TownServiceOfferingCard
{
    private readonly Transform _card;
    private readonly Vector3 _fromPosition, _fromScale;
    private readonly Quaternion _fromRotation;
    private readonly float _started, _scale;
    internal TownServiceOfferingCard(Transform card, Transform seat, float scale)
    {
        _card = card; _scale = scale; _started = Time.unscaledTime;
        card.SetParent(seat, true);
        _fromPosition = card.localPosition; _fromRotation = card.localRotation; _fromScale = card.localScale;
    }
    internal void Tick()
    {
        if (_card == null) return;
        float t = Mathf.Clamp01((Time.unscaledTime - _started) / .25f);
        t = t * t * (3f - 2f * t);
        _card.localPosition = Vector3.Lerp(_fromPosition, Vector3.zero, t);
        _card.localRotation = Quaternion.Slerp(_fromRotation, Quaternion.identity, t);
        _card.localScale = Vector3.Lerp(_fromScale, Vector3.one * _scale, t);
    }
}
