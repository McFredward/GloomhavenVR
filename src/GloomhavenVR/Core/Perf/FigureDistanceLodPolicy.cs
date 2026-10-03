using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Projected-size quality selection; world scaling never changes the thresholds.
/// The configured near detail is a ceiling, not an instruction to show more expensive meshes.</summary>
internal sealed class FigureDistanceLodPolicy
{
    private int _band;
    internal int Select(int cap, Bounds bounds, Vector3 eye, bool protectedNear)
    {
        if (!PerfConfig.FigureDistanceLodEnabled || protectedNear)
        { _band = 0; return cap; }
        float radius = Mathf.Max(bounds.extents.magnitude, .001f);
        float ratio = Vector3.Distance(bounds.center, eye) / radius;
        // Separate entry/exit edges prevent mesh chatter at a stationary viewing boundary.
        _band = _band switch
        {
            0 => ratio > 20f ? 2 : ratio > 8f ? 1 : 0,
            1 => ratio > 20f ? 2 : ratio < 7f ? 0 : 1,
            _ => ratio < 7f ? 0 : ratio < 18f ? 1 : 2
        };
        return _band == 2 ? -1 : _band == 1 ? Mathf.Min(cap, 45) : cap;
    }
}
