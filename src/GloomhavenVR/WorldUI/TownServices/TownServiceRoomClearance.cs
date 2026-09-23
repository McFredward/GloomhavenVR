using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>The embodied town has walk-around stalls beyond the old map-only clearing.
/// Widen the mod's decorative map room horizontally, retaining floor/ceiling height and
/// restoring its exact authored scale on shutdown. Native map/scenario geometry is untouched.</summary>
internal static class TownServiceRoomClearance
{
    internal const float HorizontalExpansion = 3.5f;
    private static Transform? _room;
    private static TownServiceRoomGeometry? _geometry;
    private static Vector3 _base, _last;
    private static float _factor = 1f, _lastTime;

    internal static void Tick(bool enabled)
    {
        Transform? room = SkyAlternative.PlacedRoomRoot;
        if (room != _room)
        {
            Reset();
            if (room == null) return;
            _room = room; _base = room.localScale;
            _geometry = new TownServiceRoomGeometry(room);
            // A freshly placed room has not yet appeared. Avoid an opening-frame resize.
            _factor = enabled ? HorizontalExpansion : 1f;
            _last = _base; _lastTime = Time.unscaledTime;
        }
        if (_room == null) return;
        CaptureExternalScale();
        float dt = Mathf.Max(0f, Time.unscaledTime - _lastTime); _lastTime = Time.unscaledTime;
        _factor = Mathf.MoveTowards(_factor, enabled ? HorizontalExpansion : 1f, dt * 3f);
        _last = Vector3.Scale(_base, new Vector3(_factor, 1f, _factor));
        _room.localScale = _last;
        _geometry?.Apply(_factor);
    }

    // Room placement writes an absolute uniform authored scale. A caller can also scale
    // the currently widened room uniformly; retain that change without widening it twice.
    private static void CaptureExternalScale()
    {
        if (_room == null || _room.localScale == _last) return;
        Vector3 observed = _room.localScale;
        if (Mathf.Abs(_last.x) > .000001f && Mathf.Abs(_last.y) > .000001f && Mathf.Abs(_last.z) > .000001f)
        {
            Vector3 ratio = new Vector3(observed.x / _last.x, observed.y / _last.y, observed.z / _last.z);
            if (Mathf.Approximately(ratio.x, ratio.y) && Mathf.Approximately(ratio.y, ratio.z))
            {
                _base *= ratio.y;
                return;
            }
        }
        _base = observed;
    }

    internal static void Reset()
    {
        CaptureExternalScale();
        if (_room != null) _room.localScale = _base;
        _geometry?.Dispose(); _geometry = null;
        _room = null; _factor = 1f;
    }
}
