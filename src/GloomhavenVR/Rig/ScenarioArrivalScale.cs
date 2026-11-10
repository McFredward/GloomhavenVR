namespace GloomhavenVR.Rig;

/// <summary>Arrival is a readable table, while deliberate live zoom remains unrestricted.</summary>
internal static class ScenarioArrivalScale
{
    // A saved 11.5x multiplier produced a 131.7-unit rig in the paired753 report,
    // versus the peer's11-unit rig. Do not import extreme sightseeing zoom into a
    // fresh scenario. The caller remembers the corrected arrival so later camera
    // rebuilds cannot restore the outlier. Live gesture limits remain unchanged.
    internal static float Resolve(float saved, float fallback, bool arrival) =>
        arrival && (float.IsNaN(saved) || float.IsInfinity(saved) || saved < 0.25f || saved > 4f)
            ? fallback : saved;
}
