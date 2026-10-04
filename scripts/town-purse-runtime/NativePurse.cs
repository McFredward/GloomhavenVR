using System;
using System.IO;
using UnityEngine;

/// <summary>Exact original PCG renderer data, in the production normalized holder hierarchy.
/// This fixture never loads the native prefab or runs its gameplay components.</summary>
internal static class NativePurse
{
    [Serializable] private sealed class Data
    {
        public Vector3 position, scale;
        public Quaternion rotation;
        public Vector3[] vertices = Array.Empty<Vector3>();
        public int[] triangles = Array.Empty<int>();
    }
    internal static Transform Create(Transform parent)
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-nativePurseData");
        if (at < 0) throw new Exception("original purse geometry requires the immutable PCG export");
        Data data = JsonUtility.FromJson<Data>(File.ReadAllText(args[at + 1]));
        var root = new GameObject("Town.OriginalMoneyBagTemplate").transform;
        root.SetParent(parent, false);
        var holder = new GameObject("Original.Treasure.Bay.Variant#2").transform;
        holder.SetParent(root, false);
        var visual = new GameObject("CR_ST_Shelf_KitchenItems_Bag_01 (3)").transform;
        visual.SetParent(holder, false);
        visual.localPosition = data.position; visual.localRotation = data.rotation; visual.localScale = data.scale;
        Mesh mesh = new Mesh { name = "CR_ST_Shelf_KitchenItems_Bag_01" };
        mesh.vertices = data.vertices; mesh.triangles = data.triangles; mesh.RecalculateBounds();
        visual.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        visual.gameObject.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard"));
        // Match TownServiceDecor.Build's complete native bounds, normalization and
        // bottom-centre translation before Piece adds its -65 mm local body seat.
        Bounds native = mesh.bounds, bounds = default;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = native.center + Vector3.Scale(native.extents, new Vector3(
                (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                (corner & 4) == 0 ? -1f : 1f));
            point = holder.InverseTransformPoint(visual.TransformPoint(point));
            if (corner == 0) bounds = new Bounds(point, Vector3.zero); else bounds.Encapsulate(point);
        }
        float factor = .125f / Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        holder.localScale = Vector3.one * factor;
        holder.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * factor;
        root.localPosition = new Vector3(0f, -.065f, 0f);
        return root;
    }
}
