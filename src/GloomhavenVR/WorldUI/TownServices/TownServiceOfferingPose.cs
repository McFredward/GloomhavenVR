using UnityEngine;
using System.Collections.Generic;
using GloomhavenVR.Rig;

namespace GloomhavenVR.WorldUI;

/// <summary>The owner's floating palm display. Facing remains owner-authored. The
/// user permits a local phase for its intrinsic sine hover on each observer.</summary>
internal static class TownServiceOfferingPose
{
    internal const float HoverAmplitude = .006f, HoverAngularSpeed = 1.8f;
    private sealed class NativeHover
    {
        internal float PlacedAt;
        internal Vector3 Amplitude, Offset;
        internal Transform? Original;
        internal uint Epoch;
    }
    private static readonly Dictionary<Transform, NativeHover> NativeHovers = new();
    private static readonly List<Transform> DeadHovers = new();
    private static uint _nextHoverEpoch;

    // Read the actual Place result, never derive a session age from a card's
    // creation or a network header. All detached prints/backings identify the
    // same physical child of this seat and therefore share one effect epoch.
    internal static bool TryHover(Transform source, out uint epoch,
        out Vector3 amplitude, out Vector3 offset)
    {
        epoch = 0; amplitude = offset = Vector3.zero;
        for (Transform? original = source; original != null && original.parent != null; original = original.parent)
            if (NativeHovers.TryGetValue(original.parent, out NativeHover? hover)
                && Time.unscaledTime - hover.PlacedAt <= .25f)
            {
                if (hover.Original != original)
                {
                    if (_nextHoverEpoch == uint.MaxValue) return false;
                    hover.Original = original; hover.Epoch = ++_nextHoverEpoch;
                }
                epoch = hover.Epoch; amplitude = hover.Amplitude; offset = hover.Offset;
                return epoch != 0;
            }
        return false;
    }
    internal static void BeginHover(Transform original, Transform seat)
    {
        // Native acceptance can follow a regrab without any intervening Place
        // call. Start this real offered lifetime once, before its finite settle;
        // repeated print/backing/canvas captures never create a second epoch.
        if (!NativeHovers.TryGetValue(seat, out NativeHover? hover)) return;
        hover.Original = original;
        hover.Epoch = _nextHoverEpoch == uint.MaxValue ? 0 : ++_nextHoverEpoch;
    }
    internal static bool VisitorWithin(Transform station, float reach)
    {
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null) return false;
        Vector3 local = station.InverseTransformPoint(head.transform.position);
        local.y = 0f;
        return local.sqrMagnitude <= reach * reach;
    }

    internal static void Place(Transform seat, Transform palm, Transform station, float age, float halfHeightWorld = 0f)
    {
        float scale = Mathf.Max(.0001f, Mathf.Abs(station.lossyScale.x));
        Vector3 forward = station.forward;
        if (VRRigDriver.HeadCamera != null) forward = palm.position - VRRigDriver.HeadCamera.transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
        Quaternion facing = Quaternion.LookRotation(forward.normalized, Vector3.up);
        if (!NativeHovers.TryGetValue(seat, out NativeHover? hover))
        {
            DeadHovers.Clear();
            foreach (Transform old in NativeHovers.Keys) if (old == null) DeadHovers.Add(old!);
            foreach (Transform old in DeadHovers) NativeHovers.Remove(old);
            if (NativeHovers.Count < 128) NativeHovers.Add(seat, hover = new NativeHover());
        }
        Vector3 amplitude = Vector3.up * (HoverAmplitude * scale);
        Vector3 offset = Vector3.up * (HoverAmplitude * Mathf.Sin(age * HoverAngularSpeed) * scale);
        if (hover != null)
        {
            if (hover.Original != null && hover.Original.parent != seat) hover.Original = null;
            hover.PlacedAt = Time.unscaledTime; hover.Amplitude = amplitude; hover.Offset = offset;
        }
        // Keep the full portrait above the palm, irrespective of wrist roll/pitch. Small
        // continuous motion conveys suspension without making the drop target hard to hit.
        seat.SetPositionAndRotation(palm.position + Vector3.up * Mathf.Max(.17f * scale, halfHeightWorld + .045f * scale) + offset,
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
        TownServiceOfferingPose.BeginHover(card, seat);
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
