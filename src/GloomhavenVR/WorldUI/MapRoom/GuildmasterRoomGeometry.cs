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

    internal static bool TryRail(MeshRenderer parchment, MapRoomSeat.Seat seat, int count,
        float capSize, float gapRatio, float outerRatio, out GuildmasterRoomLayout.Rail layout)
    {
        layout = default;
        if (!Active || !MapTableLegs.TryFindTable(parchment, parchment.bounds, seat.Scale,
            out MeshRenderer? table, out _) || table == null) return false;
        Vector3 right = seat.Rotation * Vector3.right, forward = seat.Rotation * Vector3.forward;
        Bounds map = Project(parchment.bounds, right, forward);
        Bounds slab = Project(table.bounds, right, forward);
        float margin = .008f * seat.Scale;
        float left = map.max.x + margin, edge = slab.max.x - margin;
        float near = slab.min.z + margin, far = slab.max.z - margin;
        foreach (MeshRenderer renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!NativeProp(renderer, parchment) || !Named(renderer.transform, "knife", "dagger", "sword")) continue;
            Bounds prop = Project(renderer.bounds, right, forward);
            if (prop.max.x < left || prop.min.x > edge || prop.max.z < near || prop.min.z > far) continue;
            // "Above the knife" is farther along the table as read from the seat, not floating
            // above it in world Y. Use the blade's entire measured footprint plus a small gap.
            near = Mathf.Max(near, prop.max.z + .02f * seat.Scale);
        }
        return GuildmasterRoomLayout.TryRail(left, edge, near, far, count, capSize,
            gapRatio, outerRatio, out layout);
    }

    private static Bounds Project(Bounds b, Vector3 right, Vector3 forward)
    {
        Vector3 centre = new(Vector3.Dot(b.center, right), b.center.y, Vector3.Dot(b.center, forward));
        Vector3 size = new(2f * MapRoomSeat.HalfExtentAlong(b.size, right), b.size.y,
            2f * MapRoomSeat.HalfExtentAlong(b.size, forward));
        return new Bounds(centre, size);
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
