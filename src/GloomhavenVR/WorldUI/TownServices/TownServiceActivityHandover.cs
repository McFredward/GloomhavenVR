using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A returning authority can have continued a different work/attention phase during
/// a partition. Reconcile from what this station actually displayed, not that peer's stale
/// packet. Blend evaluated contact poses: interpolating distant work clocks would replay
/// entire occupation cycles at extreme speed. This is presentation only, never an election.</summary>
internal sealed class TownServiceActivityHandover
{
    internal const float Duration = .35f;
    private bool _shown;
    private int _source;
    private uint _epoch;
    private float _age = Duration;
    private TownActivityVisual _fromActivity, _lastActivity;
    private TownFacePose _fromFace, _lastFace;

    internal void Sample(int source, uint epoch, float dt, in TownActivityVisual activity,
        in TownFacePose face, out TownActivityVisual shownActivity, out TownFacePose shownFace)
    {
        if (_shown && (source != _source || (epoch != 0 && _epoch != 0 && epoch != _epoch)))
        {
            _age = 0f; _fromActivity = _lastActivity; _fromFace = _lastFace;
        }
        else _age = Mathf.Min(Duration, _age + Mathf.Max(0f, dt));
        _source = source;
        if (epoch != 0) _epoch = epoch;
        float t = Mathf.SmoothStep(0f, 1f, _age / Duration);
        shownActivity = _shown ? TownServiceActivityMotion.Lerp(in _fromActivity, in activity, t) : activity;
        shownFace = _shown ? TownServiceFaceMotion.Interpolate(in _fromFace, in face, t) : face;
        _lastActivity = shownActivity; _lastFace = shownFace; _shown = true;
    }
    // The local author solves its own bounded gaze after the activity has been applied.
    // Keep that actual final result as the anchor if another authority takes over next frame.
    internal void RecordFace(in TownFacePose pose) => _lastFace = pose;
}
