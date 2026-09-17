using System;
using System.Collections.Generic;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>Opening-time fit of the native slab around the map, never through native furniture.</summary>
internal static class GuildmasterTableFit
{
    internal readonly struct Rect
    {
        internal readonly float Left, Right, Near, Far;
        internal Rect(float left, float right, float near, float far)
        { Left = left; Right = right; Near = near; Far = far; }
        internal float Area => Math.Max(0f, Right - Left) * Math.Max(0f, Far - Near);
        internal bool Contains(Rect r) => Left <= r.Left && Right >= r.Right && Near <= r.Near && Far >= r.Far;
        internal bool Overlaps(Rect r) => Left < r.Right && Right > r.Left && Near < r.Far && Far > r.Near;
    }

    // Start with the familiar campaign rim, then reduce only those edges that approach a prop.
    // The map itself is a hard lower bound: failing is preferable to silently intersecting a barrel.
    internal static bool TryFit(Rect map, float rim, float clearance, IReadOnlyList<Rect> obstacles, out Rect fit)
    {
        fit = new Rect(map.Left - rim, map.Right + rim, map.Near - rim, map.Far + rim);
        for (int i = 0; i < obstacles.Count; i++)
        {
            Rect b = obstacles[i];
            Rect block = new(b.Left - clearance, b.Right + clearance, b.Near - clearance, b.Far + clearance);
            if (!fit.Overlaps(block)) continue;
            Rect best = default;
            Consider(new Rect(fit.Left, Math.Min(fit.Right, block.Left), fit.Near, fit.Far), map, ref best);
            Consider(new Rect(Math.Max(fit.Left, block.Right), fit.Right, fit.Near, fit.Far), map, ref best);
            Consider(new Rect(fit.Left, fit.Right, fit.Near, Math.Min(fit.Far, block.Near)), map, ref best);
            Consider(new Rect(fit.Left, fit.Right, Math.Max(fit.Near, block.Far), fit.Far), map, ref best);
            if (best.Area <= 0f) return false;
            fit = best;
        }
        return fit.Contains(map);
    }

    private static void Consider(Rect candidate, Rect map, ref Rect best)
    {
        if (candidate.Contains(map) && candidate.Area > best.Area) best = candidate;
    }
}
