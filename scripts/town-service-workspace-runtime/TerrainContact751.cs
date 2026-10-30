using System;
using System.Linq;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static class TerrainContact751
{
    private static int _checks;
    private static void Check(bool value, string why) { _checks++; if (!value) throw new Exception(why); }
    private static float Surface(MeshFilter filter, Vector3 point)
    {
        Vector3 p = filter.transform.InverseTransformPoint(point);
        Vector3[] vertices = filter.sharedMesh.vertices; int[] triangles = filter.sharedMesh.triangles;
        float highest = float.NegativeInfinity;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-9f) continue;
            float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
            float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d, w = 1f - u - v;
            if (Mathf.Min(u, Mathf.Min(v, w)) >= -1e-5f) highest = Mathf.Max(highest, u * a.y + v * b.y + w * c.y);
        }
        Check(!float.IsNegativeInfinity(highest), "shipping terrain contains each native sole");
        p.y = highest; return filter.transform.TransformPoint(p).y;
    }
    public static int Run()
    {
        _checks = 0;
        string[] args = Environment.GetCommandLineArgs();
        AssetBundle bundle = AssetBundle.LoadFromFile(args[Array.IndexOf(args, "-workspaceBundle") + 1]);
        Check(bundle != null, "shipping environment bundle loads");
        try
        {
            foreach (float metric in new[] { 1f, 198f })
            foreach (bool forest in new[] { false, true })
            {
                GameObject prefab = bundle!.LoadAsset<GameObject>("Assets/Bundle/Environments/Env_" + (forest ? "Swamp" : "Cellar") + ".prefab");
                GameObject room = UnityEngine.Object.Instantiate(prefab);
                float floor = -.78f * metric; Vector3 center = new Vector3(7f * metric, 0, -3f * metric);
                Quaternion yaw = Quaternion.Euler(0, 37, 0);
                room.transform.SetPositionAndRotation(new Vector3(center.x, floor, center.z), yaw);
                room.transform.localScale = Vector3.one * metric * 5.4f / (forest ? 9f : 6.5f);
                MeshFilter filter = room.transform.Find("RoomGeo/" + (forest ? "Ground" : "Floor")).GetComponent<MeshFilter>();
                Mesh original = filter.sharedMesh; Vector3[] before = original.vertices, normals = original.normals;
                Vector3 position = room.transform.position, scale = room.transform.localScale;
                var points = new Vector3[6]; int count = 0;
                for (byte service = 1; service <= 3; service++)
                {
                    TownServiceLayout.Resolve(TownServiceLayout.Environment.Open, service, 0, out Vector3 offset, out float heading);
                    foreach (float x in new[] { -.12f, .12f }) points[count++] = center + yaw * offset * metric + yaw * Quaternion.Euler(0, heading, 0) * new Vector3(x, 0, .65f) * metric;
                }
                float[] oldSurface = points.Select(p => Surface(filter, p)).ToArray();
                if (forest) Check(oldSurface.Min() < floor - .05f * metric, "original woodland reproduces the visible sole gap");
                Check(TownServiceTerrainContact.Create(room, false, center, yaw, metric, floor) == null && filter.sharedMesh == original,
                    "menu/scenario and Cellar admission leave original meshes untouched");
                using (var contact = TownServiceTerrainContact.Create(room, forest, center, yaw, metric, floor))
                {
                    Check(room.transform.position == position && room.transform.localScale == scale, "local contacts preserve room placement and scale");
                    for (int i = 0; i < points.Length; i++)
                        Check(Mathf.Abs(Surface(filter, points[i]) - (forest ? floor : oldSurface[i])) < .0005f * metric,
                            "local woodland ground meets all fixed shared soles while Cellar stays exact" + $": forest={forest}, metric={metric}, foot={i}, actual={Surface(filter, points[i])}, floor={floor}");
                    Check(original.vertices.SequenceEqual(before) && original.normals.SequenceEqual(normals), "borrowed prefab mesh remains immutable");
                    if (forest)
                    {
                        Check(contact != null && filter.sharedMesh != original, "each woodland room owns its contact mesh");
                        Vector3[] after = filter.sharedMesh.vertices, afterNormals = filter.sharedMesh.normals; int outside = 0;
                        for (int i = 0; i < before.Length; i++)
                        {
                            Vector3 world = filter.transform.TransformPoint(before[i]);
                            if (points.Min(p => new Vector2(world.x - p.x, world.z - p.z).magnitude) <= .45f * metric) continue;
                            Check(after[i].Equals(before[i]) && afterNormals[i].Equals(normals[i]), "terrain outside contact pads retains exact geometry and normals"); outside++;
                        }
                        Check(outside > 1000, "original woodland outside local contacts is covered");
                    }
                }
                Check(filter.sharedMesh == original, "disposing contacts restores the exact original mesh");
                UnityEngine.Object.DestroyImmediate(room);
            }
        }
        finally { bundle!.Unload(true); }
        return _checks;
    }
}

namespace GloomhavenVR.Rig
{
    // This test executes terrain ownership/math and shipping meshes, with no rig.
    internal static class VRRigDriver { internal static Quaternion YawOnly(Quaternion q) => Quaternion.Euler(0, q.eulerAngles.y, 0); }
}
