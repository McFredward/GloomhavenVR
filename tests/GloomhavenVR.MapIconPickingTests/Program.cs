using System;
using GloomhavenVR.WorldUI.MapRoom;
using GloomhavenVR.Hands.Interact;

static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private readonly record struct Icon(float X, float Z, float Width, float Depth, int Key);

    private static int Pick(float x, float z, params Icon[] icons)
    {
        float best = float.PositiveInfinity;
        int key = int.MaxValue;
        foreach (Icon icon in icons)
        {
            float dx = x - icon.X, dz = z - icon.Z;
            if (!MapIconPickGeometry.Contains(dx, dz, icon.Width, icon.Depth)) continue;
            float score = MapIconPickGeometry.Score(dx, dz);
            if (!MapIconPickGeometry.Prefer(score, icon.Key, best, key)) continue;
            best = score;
            key = icon.Key;
        }
        return key;
    }

    public static void Main()
    {
        // Scaled icons can share nearly their entire footprint. Both centres must remain
        // reachable, independently of physics entry distance, target size or discovery order.
        var broad = new Icon(0, 0, 6, 6, 20);
        var small = new Icon(0.7f, 0.1f, 1.5f, 1.5f, 10);
        Check(Pick(small.X, small.Z, broad, small) == small.Key, "small overlapping centre wins");
        Check(Pick(broad.X, broad.Z, broad, small) == broad.Key, "large overlapping centre wins");
        for (int i = 0; i <= 100; i++)
        {
            float t = i / 100f;
            float x = small.X * t, z = small.Z * t;
            int actual = Pick(x, z, broad, small);
            Check(actual == Pick(x, z, small, broad), "candidate order is irrelevant");
            if (i != 50) Check(actual == (i < 50 ? broad.Key : small.Key), "seam follows centre midpoint");
        }
        // A nearest centre outside the footprint may not steal the painted part of another icon.
        Check(Pick(2.8f, 0, broad, small) == broad.Key, "outside footprint is excluded");
        Check(Pick(4, 0, broad, small) == int.MaxValue, "no invisible reach beyond footprints");
        Check(Pick(0, 0, new Icon(0, 0, 0, 1, 1)) == int.MaxValue, "zero size is not pickable");
        Check(Pick(0, 0, new Icon(0, 0, -1, 1, 1)) == int.MaxValue, "negative size is not pickable");
        Check(MapIconPickGeometry.Prefer(1f, 10, 1f, 20), "stable tie key wins");
        Check(!MapIconPickGeometry.Prefer(1f, 20, 1f, 10), "stable tie key rejects reverse");
        // Scaling the complete room keeps ownership; no hard-coded proximity radius.
        for (int scale = 1; scale <= 20; scale++)
        {
            var a = new Icon(0, 0, 6 * scale, 6 * scale, 20);
            var b = new Icon(.7f * scale, .1f * scale, 1.5f * scale, 1.5f * scale, 10);
            Check(Pick(.6f * scale, .1f * scale, a, b) == 10, "room scale preserves ownership");
        }
        // A dense cluster exceeds the old sixteen-hit physics buffer. Every centre remains
        // reachable because the registry traversal is not a capped raycast-hit list.
        var dense = new Icon[80];
        for (int i = 0; i < dense.Length; i++) dense[i] = new Icon(i * .01f, 0, 2, 2, i);
        for (int i = 0; i < dense.Length; i++)
            Check(Pick(dense[i].X, 0, dense) == i, "dense cluster centre remains reachable");
        Check(MapIconPickGeometry.PlaneDistance(5, -1, 1, out float distance) && distance == 4,
            "painted plane distance");
        Check(MapIconPickGeometry.PlaneDistance(5, -.5f, 1, out distance) && distance == 8,
            "oblique ray uses same painted plane");
        Check(!MapIconPickGeometry.PlaneDistance(5, 1, 1, out _), "behind ray rejected");
        Check(!MapIconPickGeometry.PlaneDistance(5, 0, 1, out _), "parallel ray rejected");
        Check(!MapIconPickGeometry.PlaneDistance(float.NaN, -1, 1, out _), "invalid ray rejected");
        Check(!MapIconPickGeometry.PlaneDistance(float.PositiveInfinity, -1, 1, out _), "infinite ray rejected");
        float inf = float.PositiveInfinity;
        foreach (float roomScale in new[] { .01f, 1f, 198f })
        {
            Check(MapIconPickGeometry.PlaneDistance(4f * roomScale, -1, 0, out distance),
                "scaled ray intersects painted plane");
            float front = 3f * roomScale;
            Check(!LaserPointerPolicy.TargetBeforeBlocker(distance,
                LaserPointerPolicy.PickLimit(20 * roomScale, inf, front, inf, false)), "bar blocks plane owner");
            Check(!LaserPointerPolicy.TargetBeforeBlocker(distance,
                LaserPointerPolicy.PickLimit(20 * roomScale, inf, inf, front, false)), "panel blocks plane owner");
            Check(!LaserPointerPolicy.TargetBeforeBlocker(distance,
                LaserPointerPolicy.PickLimit(20 * roomScale, front, inf, inf, false)), "fan blocks plane owner");
            Check(!LaserPointerPolicy.TargetBeforeBlocker(distance,
                LaserPointerPolicy.PickLimit(20 * roomScale, inf, inf, inf, true)), "carry frame blocks plane owner");
            Check(LaserPointerPolicy.TargetBeforeBlocker(distance,
                LaserPointerPolicy.PickLimit(20 * roomScale, inf, inf, inf, false)), "clear beam reaches plane owner");
        }
        Console.WriteLine($"Map icon picking: {_checks} production geometry assertions passed");
    }
}
