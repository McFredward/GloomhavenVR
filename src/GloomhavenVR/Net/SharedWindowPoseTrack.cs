using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Interpolate received shared-frame poses without extrapolation or duplicate restarts.
/// Keeping the track in wire coordinates lets a local zoom/seat change reproject it immediately.</summary>
internal sealed class SharedWindowPoseTrack
{
    private RigPose _from, _to;
    private float _fromSize, _toSize, _at, _duration;
    private bool _ready;

    internal void Reset() => _ready = false;

    internal void Seed(RigPose pose, float size, float now)
    {
        _from = _to = pose;
        _fromSize = _toSize = size;
        _at = now;
        _duration = 0;
        _ready = true;
    }

    internal RigPose Sample(RigPose target, float size, float now, out float sampledSize)
    {
        if (!_ready) Seed(target, size, now);
        if (!_to.Position.Equals(target.Position) || !_to.Rotation.Equals(target.Rotation) || _toSize != size)
        {
            _from = Current(now, out _fromSize);
            _duration = Mathf.Clamp(now - _at, 1f / NetProtocol.SendRateHz, 0.2f);
            _at = now;
            _to = target;
            _toSize = size;
        }
        return Current(now, out sampledSize);
    }

    private RigPose Current(float now, out float size)
    {
        float t = _duration > 0 ? Mathf.Clamp01((now - _at) / _duration) : 1f;
        size = Mathf.LerpUnclamped(_fromSize, _toSize, t);
        return new RigPose { Position = Vector3.LerpUnclamped(_from.Position, _to.Position, t),
            Rotation = _from.Rotation.Equals(_to.Rotation) ? _to.Rotation
                : Quaternion.SlerpUnclamped(_from.Rotation, _to.Rotation, t) };
    }
}
