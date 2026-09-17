using System;

namespace GloomhavenVR.WorldUI;

/// <summary>Opening-only horizontal packing, in one reader's yaw plane. Geometry is sampled once;
/// neither the layout nor its animation follows a moving headset. Shared callers elect ONE author.</summary>
internal static class WindowReflowLayout
{
    internal struct Window
    {
        internal float Left, Right, Bottom, Top; // Projected tangent-space bounds.
        internal float X, Depth, Width;
        internal bool Movable;
    }

    // Keep existing order. A newly opened window can join an overlapping component, but a parked
    // window outside it is not permission to rearrange the room. Bounds are the actual four corners.
    internal static bool TryArrange(Window[] windows, int count, int incoming, float halfView,
        float gap, float maxDepth, bool[] included, float[] targetX, out float depth)
    {
        depth = 0f;
        if (count < 2 || incoming < 0 || incoming >= count || halfView <= 0f)
            return false;
        for (int i = 0; i < count; i++) included[i] = false;
        Window fresh = windows[incoming];
        if (!Valid(fresh) || !InView(fresh, halfView))
            return false;
        included[incoming] = true;
        int members = 1;
        bool collision = false;
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < count; i++)
            {
                Window candidate = windows[i];
                if (included[i] || !Valid(candidate)
                    || !InView(candidate, halfView))
                    continue;
                for (int j = 0; j < count; j++)
                {
                    if (!included[j] || !Overlap(candidate, windows[j])) continue;
                    collision = true;
                    if (!candidate.Movable) continue; // Keep protected windows as fixed obstacles.
                    included[i] = true;
                    members++;
                    changed = true;
                    break;
                }
            }
        } while (changed);
        if (!fresh.Movable)
        {
            included[incoming] = false;
            members--;
        }
        if (!collision || members == 0) return false;
        float width = gap * (members - 1), centre = 0f;
        for (int i = 0; i < count; i++)
        {
            if (!included[i]) continue;
            width += windows[i].Width;
            centre += windows[i].X;
            depth = Math.Max(depth, windows[i].Depth);
        }
        depth = Math.Max(depth, width * 0.5f / halfView);
        if (depth > maxDepth || depth <= 0f) return false;
        float minimumDepth = depth;
        centre /= members;
        // Fixed corner/private windows remain obstacles, not a veto on the whole encounter. Search
        // from the nearest readable depth; within each row prefer the original group's centre.
        for (int step = 0; step <= 20; step++)
        {
            depth = minimumDepth + (maxDepth - minimumDepth) * step / 20f;
            float room = Math.Max(0f, depth * halfView - width * 0.5f);
            float desiredLeft = Math.Max(-room, Math.Min(room, centre)) - width * 0.5f;
            if (TryPack(windows, count, members, included, targetX, depth, halfView, gap, desiredLeft)
                || TryPack(windows, count, members, included, targetX, depth, halfView, gap,
                    -depth * halfView)) return true;
        }
        return false;
    }

    private static bool TryPack(Window[] windows, int count, int members, bool[] included,
        float[] targetX, float depth, float halfView, float gap, float cursor)
    {
        // At most three moving map windows today; stable rank avoids a sorting allocation.
        for (int rank = 0; rank < members; rank++)
        {
            for (int i = 0; i < count; i++)
            {
                if (!included[i]) continue;
                int before = 0;
                for (int j = 0; j < count; j++)
                    if (included[j] && (windows[j].X < windows[i].X
                        || (windows[j].X == windows[i].X && j < i))) before++;
                if (before != rank) continue;
                Window target = windows[i];
                target.Bottom *= windows[i].Depth / depth;
                target.Top *= windows[i].Depth / depth;
                // Skip fixed footprints until this complete rectangle has a free interval.
                for (int pass = 0; pass <= count; pass++)
                {
                    target.Left = cursor / depth;
                    target.Right = (cursor + target.Width) / depth;
                    bool blocked = false;
                    for (int j = 0; j < count; j++)
                    {
                        if (included[j] || !Valid(windows[j]) || !Overlap(target, windows[j])) continue;
                        cursor = Math.Max(cursor, windows[j].Right * depth + gap);
                        blocked = true;
                    }
                    if (!blocked) break;
                    if (pass == count) return false;
                }
                if (cursor + target.Width > depth * halfView + 0.00001f) return false;
                targetX[i] = cursor + target.Width * 0.5f;
                cursor += target.Width + gap;
                break;
            }
        }
        return true;
    }

    internal static bool HasVisibleOverlap(Window[] windows, int count, int incoming, float halfView)
    {
        if (incoming < 0 || incoming >= count || !Valid(windows[incoming])
            || !InView(windows[incoming], halfView)) return false;
        for (int i = 0; i < count; i++)
            if (i != incoming && Valid(windows[i]) && InView(windows[i], halfView)
                && Overlap(windows[incoming], windows[i])) return true;
        return false;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool Valid(Window w) => w.Depth > 0f && w.Width > 0f
        && Finite(w.Depth) && Finite(w.Width) && Finite(w.X) && Finite(w.Left)
        && Finite(w.Right) && Finite(w.Top) && Finite(w.Bottom)
        && w.Left < w.Right && w.Bottom < w.Top;

    private static bool InView(Window w, float halfView) => w.Right > -halfView
        && w.Left < halfView && w.Top > -halfView && w.Bottom < halfView;

    private static bool Overlap(Window a, Window b) => a.Left < b.Right && b.Left < a.Right
        && a.Bottom < b.Top && b.Bottom < a.Top;
}
