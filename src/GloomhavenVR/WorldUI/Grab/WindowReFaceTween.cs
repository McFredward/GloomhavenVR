using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// A release turns the actual grab frame over the same duration and cubic ease-out as its rod.
/// The target and world pivot are captured once: moving the head never steers an in-flight turn.
/// Owners advance this in LateUpdate, before copying the frame to their native host, and cancel
/// before any new carry or placement. The frame therefore always matches the visible grab pose.
/// </summary>
internal sealed class WindowReFaceTween
{
    private bool _active;
    private float _startedAt;
    private float _duration;
    private Vector3 _pivot;
    private Vector3 _fromPosition;
    private Quaternion _fromRotation;
    private Quaternion _toRotation;

    internal void Cancel() => _active = false;

    internal void Begin(Transform frame, Vector3 pivot, Quaternion facing)
    {
        _pivot = pivot;
        _fromPosition = frame.position;
        _fromRotation = frame.rotation;
        _toRotation = facing;
        _startedAt = Time.unscaledTime;
        _duration = GrabBarTween.DurationSeconds;
        _active = true;
    }

    internal bool Advance(Transform frame)
    {
        if (!_active)
            return false;
        float k = _duration <= 0f ? 1f
            : Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
        float inv = 1f - k;
        float eased = 1f - inv * inv * inv;
        Quaternion rotation = k >= 1f ? _toRotation
            : Quaternion.SlerpUnclamped(_fromRotation, _toRotation, eased);
        // Interpolating the endpoints' positions would cut a chord and move off-centre ink.
        // Rotate the original frame offset on every step so its drawn pivot remains fixed.
        Quaternion delta = rotation * Quaternion.Inverse(_fromRotation);
        frame.SetPositionAndRotation(_pivot + delta * (_fromPosition - _pivot), rotation);
        if (k >= 1f)
            _active = false;
        return true;
    }
}
