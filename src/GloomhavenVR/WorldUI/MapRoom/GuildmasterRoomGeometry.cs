using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Read native furniture once when placing the room/rail. Native scene transforms, map icon
/// coordinates and gameplay controllers remain untouched; all peers derive the same layout.
/// </summary>
internal static class GuildmasterRoomGeometry
{
    internal static bool Active => MapRoomDriver.Active
        && MapRuleLibrary.Adventure.AdventureState.MapState != null
        && !MapRuleLibrary.Adventure.AdventureState.MapState.IsCampaign;

    internal static bool TryFloor(out float floorY)
    {
        floorY = 0f;
        MeshRenderer? parchment = MapRoomDriver.ParchmentRenderer;
        if (!Active || parchment == null || !MapRoomDriver.TrySolveSeat(out var seat, out _)) return false;
        Bounds map = parchment.bounds;
        float bottom = float.PositiveInfinity;
        foreach (MeshRenderer renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!NativeProp(renderer, parchment) || !Named(renderer.transform, "bench", "barrel", "table")) continue;
            Bounds b = renderer.bounds;
            // A slab or a tabletop decoration is not a floor-standing prop. Exclude remote/native
            // furniture outside this map's immediate scene footprint as well as impossible bounds.
            float depth = (map.max.y - b.min.y) / seat.Scale;
            if (depth < .4f || depth > 1.5f
                || Mathf.Abs(b.center.x - map.center.x) > map.extents.x + 1.5f * seat.Scale
                || Mathf.Abs(b.center.z - map.center.z) > map.extents.z + 1.5f * seat.Scale) continue;
            bottom = Mathf.Min(bottom, b.min.y);
        }
        if (float.IsInfinity(bottom)) return false;
        // Match the leg system's small downward contact allowance for the rooms' floor relief.
        // This is applied only at initial environment placement, never a later room teleport.
        floorY = GuildmasterRoomLayout.GroundFloor(bottom, seat.Scale);
        if (VRLog.WantsDebug)
            VRLog.Info("MapRoom", $"GUILDMASTER FURNITURE FLOOR: base={bottom:F2}, floor={floorY:F2}, "
                + $"scale={seat.Scale:F2}; native furniture/map transforms unchanged.");
        return true;
    }

    internal static bool TryRail(MeshRenderer parchment, MapRoomSeat.Seat seat, int actions, int maps,
        float capSize, float gapRatio, float groupGapRatio, float outerRatio, out GuildmasterRoomLayout.Rail layout)
    {
        Vector3 right = seat.Rotation * Vector3.right, forward = seat.Rotation * Vector3.forward;
        Bounds map = Project(parchment, right, forward);
        float margin = .008f * seat.Scale;
        float left = map.max.x + margin;
        MeshRenderer? table = null;
        bool measured = Active && MapTableLegs.TryFindTable(parchment, parchment.bounds, seat.Scale,
            out table, out _);
        // The leg finder deliberately requires a thin slab. Native Guildmaster furniture can also
        // be one mesh containing its legs: its painted top still supports the controls. Do not make
        // access to the game's merchant/trainer depend on the stricter optional-leg heuristic.
        table = measured ? table : FindTableSupport(parchment, seat.Scale);
        Bounds slab = table != null ? Project(table, right, forward) : map;
        float edge = slab.max.x - margin;
        float near = slab.min.z + margin, far = slab.max.z - margin;
        float knifeFar = float.NegativeInfinity;
        foreach (MeshRenderer renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!NativeProp(renderer, parchment) || !Named(renderer.transform, "knife", "dagger", "sword")) continue;
            Bounds prop = Project(renderer, right, forward);
            // A weapon elsewhere in this scene (or an inactive renderer left after a character
            // preview) must not consume the tabletop merely because its X/Z projection intersects.
            if (!renderer.enabled || prop.max.y < map.min.y - .03f * seat.Scale
                || prop.min.y > map.max.y + .2f * seat.Scale
                || prop.max.x < left || prop.min.x > Mathf.Max(edge, left + capSize * 3f)
                || prop.max.z < map.min.z || prop.min.z > map.max.z) continue;
            knifeFar = Mathf.Max(knifeFar, prop.max.z + .02f * seat.Scale);
        }
        near = Mathf.Max(near, knifeFar);
        if (table != null && GuildmasterRoomLayout.TryGroupedRail(left, edge, near, far, actions, maps, capSize,
            gapRatio, groupGapRatio, outerRatio, out layout) && GuildmasterRoomLayout.HasUsableCap(layout, capSize)) return true;

        // Build 530 returned without any caps whenever the optional support measurement failed.
        // These are necessary native actions, not decoration. Keep a deterministic, reachable
        // right-side rail, above the measured knife, while reporting the unsupported furniture fit.
        layout = GuildmasterRoomLayout.FallbackGroupedRail(left, map.max.z, knifeFar, actions, maps,
            capSize, gapRatio, groupGapRatio, outerRatio);
        if (VRLog.WantsDebug)
            VRLog.Info("MapRoom", $"GUILDMASTER RAIL FIT: support='{(table != null ? table.name : "<none>")}', "
                + $"side=({left:F2}..{edge:F2}), depth=({near:F2}..{far:F2}), "
                + $"scale={seat.Scale:F2}; using right-side controls without moving native furniture.");
        return false;
    }

    internal static MeshRenderer? FindTableSupport(MeshRenderer parchment, float scale)
    {
        Bounds map = parchment.bounds;
        MeshRenderer? best = null;
        float bestArea = float.PositiveInfinity;
        foreach (MeshRenderer renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (renderer == parchment || !NativeProp(renderer, parchment)
                || !Named(renderer.transform, "table", "tabletop", "slab")) continue;
            Bounds b = renderer.bounds;
            if (b.min.x > map.min.x + .02f * scale || b.max.x < map.max.x - .02f * scale
                || b.min.z > map.min.z + .02f * scale || b.max.z < map.max.z - .02f * scale
                || Mathf.Abs(b.max.y - map.max.y) > .12f * scale
                || b.size.y < .03f * scale) continue;
            float area = b.size.x * b.size.z;
            if (area >= bestArea) continue;
            best = renderer;
            bestArea = area;
        }
        return best;
    }

    private static Bounds Project(MeshRenderer renderer, Vector3 right, Vector3 forward)
    {
        MeshFilter? filter = renderer.GetComponent<MeshFilter>();
        if (renderer.isPartOfStaticBatch || filter == null || filter.sharedMesh == null)
        {
            Bounds b = renderer.bounds;
            return new Bounds(new Vector3(Vector3.Dot(b.center, right), b.center.y,
                Vector3.Dot(b.center, forward)), new Vector3(
                2f * MapRoomSeat.HalfExtentAlong(b.size, right), b.size.y,
                2f * MapRoomSeat.HalfExtentAlong(b.size, forward)));
        }
        // Project the authored box directly. Projecting an already world-aligned AABB a second
        // time inflates rotated parchment/knife footprints and can erase a real free side strip.
        Bounds mesh = filter.sharedMesh.bounds;
        Bounds projected = default;
        for (int i = 0; i < 8; i++)
        {
            Vector3 point = renderer.transform.TransformPoint(new Vector3(
                (i & 1) == 0 ? mesh.min.x : mesh.max.x,
                (i & 2) == 0 ? mesh.min.y : mesh.max.y,
                (i & 4) == 0 ? mesh.min.z : mesh.max.z));
            Vector3 p = new(Vector3.Dot(point, right), point.y, Vector3.Dot(point, forward));
            if (i == 0) projected = new Bounds(p, Vector3.zero);
            else projected.Encapsulate(p);
        }
        return projected;
    }

    private static bool NativeProp(MeshRenderer renderer, MeshRenderer parchment)
    {
        if (!renderer.gameObject.activeInHierarchy || renderer.gameObject.scene != parchment.gameObject.scene) return false;
        for (Transform? t = renderer.transform; t != null; t = t.parent)
            if (t.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool Named(Transform transform, string a, string b, string c)
    {
        for (Transform? t = transform; t != null; t = t.parent)
            if (t.name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0
                || t.name.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0
                || t.name.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}
