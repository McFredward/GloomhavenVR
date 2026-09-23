using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Author-only placement on the actual room floor, independent of the visitor's head.</summary>
internal static class TownServicePlacement
{
    internal static bool TryResolve(byte service, Vector3 center, float scale,
        out Vector3 position, out Quaternion rotation)
    {
        position = center;
        rotation = Quaternion.identity;
        if (!MapRoomDriver.Active || !MapRoomDriver.TrySolveSeat(out MapRoomSeat.Seat seat, out _)) return false;
        Transform? room = SkyAlternative.PlacedRoomRoot;
        TownServiceLayout.Resolve(TownServiceLayout.ForRoom(room), service, 0,
            out Vector3 offset, out float heading);
        Quaternion frame = TownServiceLayout.Frame(room, MapRoomDriver.ParchmentRenderer?.transform);
        position = center + frame * offset * scale;
        rotation = frame * Quaternion.Euler(0f, heading, 0f);
        // Build 541 used tracking floor, which is not the floor mesh beneath the floating map.
        // In MR/default there is no custom floor; the canonical map seat remains the reference.
        position.y = room != null ? room.position.y : seat.FloorPosition.y;
        if (room != null) position.y = GroundHeight(room, position);
        return true;
    }

    internal static float GroundHeight(Transform room, Vector3 position)
    {
        // The room floors deliberately have no gameplay colliders. Read only their actual mesh
        // triangles once per placement; never raycast water, furniture, scenery or native map tiles.
        Transform? ground = room.Find("RoomGeo/Ground") ?? room.Find("RoomGeo/Floor");
        MeshFilter? filter = ground != null ? ground.GetComponent<MeshFilter>() : null;
        Mesh? mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null || !mesh.isReadable) return room.position.y;
        Vector3 p = ground!.InverseTransformPoint(position);
        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.triangles;
        float best = float.NegativeInfinity;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
            if (TryHeight(p, a, b, c, out float y)) best = Mathf.Max(best, y);
        }
        return float.IsNegativeInfinity(best) ? room.position.y : ground.TransformPoint(new Vector3(p.x, best, p.z)).y;
    }

    internal static bool TryHeight(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float height)
    {
        float determinant = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
        height = 0f;
        if (Mathf.Abs(determinant) < 1e-8f) return false;
        float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / determinant;
        float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / determinant;
        float w = 1f - u - v;
        if (u < -1e-5f || v < -1e-5f || w < -1e-5f) return false;
        height = u * a.y + v * b.y + w * c.y;
        return true;
    }
}
