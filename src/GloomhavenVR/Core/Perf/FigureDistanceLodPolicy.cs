using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Projected-size quality selection; world scaling never changes the thresholds.
/// The configured near detail is a ceiling, not an instruction to show more expensive meshes.</summary>
internal sealed class FigureDistanceLodPolicy
{
    private const float MiddleEntry = 8f, MiddleExit = 7f;
    private const float FarEntry = 20f, FarExit = 18f;
    private int _band;

    /// <summary>The first visible reduction for the supplied geometric radius.
    /// Percentages below 67 already select the prepared 45/20 tier, so the middle
    /// band adds no visible reduction. Figures and independent pillar policies use
    /// these same boundaries with their own original geometry; no actor registry,
    /// camera, scene or saved setting is consulted by this pure scalar helper.</summary>
    internal static Vector2 FirstReductionDistances(int cap, float radius)
    {
        radius = Mathf.Max(radius, .001f);
        return cap >= 67 ? new Vector2(radius * MiddleEntry, radius * MiddleExit)
            : new Vector2(radius * FarEntry, radius * FarExit);
    }

    internal int Select(int cap, Bounds bounds, Vector3 eye, bool protectedNear)
    {
        if (!PerfConfig.FigureDistanceLodEnabled || protectedNear)
        { _band = 0; return cap; }
        float radius = Mathf.Max(bounds.extents.magnitude, .001f);
        float ratio = Vector3.Distance(bounds.center, eye) / radius;
        // Separate entry/exit edges prevent mesh chatter at a stationary viewing boundary.
        _band = _band switch
        {
            0 => ratio > FarEntry ? 2 : ratio > MiddleEntry ? 1 : 0,
            1 => ratio > FarEntry ? 2 : ratio < MiddleExit ? 0 : 1,
            _ => ratio < MiddleExit ? 0 : ratio < FarExit ? 1 : 2
        };
        return _band == 2 ? -1 : _band == 1 ? Mathf.Min(cap, 45) : cap;
    }
}
