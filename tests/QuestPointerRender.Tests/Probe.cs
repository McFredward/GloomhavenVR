using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Real native GPU characterization of the laser hit marker. This is deliberately
// independent of OpenXR: it separates ordinary Sprite/MeshRenderer semantics from
// tracked eye projection, and does not establish a Quest headset outcome.
public static class QuestPointerRenderProbe
{
    [Serializable] sealed class Row
    {
        public string name, shader, mesh;
        public int vertices, vertexStride, pixels, minX, maxX, minY, maxY;
        public bool expectedBounded, bounded;
        public Vector3 bounds;
        public Vector4 flip, rendererColor;
    }
    [Serializable] sealed class Report
    {
        public string unity, graphics;
        public bool hardwareVerified = false;
        public List<Row> cases = new List<Row>();
    }

    public static void Run()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProbeOutput"));
        Directory.CreateDirectory(output);
        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create();
        var camera = new GameObject("pointer-native-gpu-camera").AddComponent<Camera>();
        camera.enabled = false; camera.targetTexture = target;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.fieldOfView = 60; camera.nearClipPlane = .01f; camera.farClipPlane = 10;
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(sphere.GetComponent<Collider>());
        var filter = sphere.GetComponent<MeshFilter>();
        var renderer = sphere.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        sphere.transform.position = new Vector3(.1f, .08f, 1.5f);
        sphere.transform.rotation = Quaternion.Euler(35, 55, 40);
        sphere.transform.localScale = Vector3.one * .02f;
        var native = filter.sharedMesh;
        var explicitSphere = Sphere();
        var sprite = new PointerProductionMaterialFactory().Build();
        var ui = new Material(Shader.Find("UI/Default"));
        ui.color = new Color(.45f, .8f, 1, .35f); ui.renderQueue = 4600;
        try
        {
            void Capture(string name, Mesh mesh, Material material, bool expectedBounded = true)
            {
                filter.sharedMesh = mesh; renderer.sharedMaterial = material;
                camera.Render();
                var previous = RenderTexture.active;
                var image = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
                try
                {
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply();
                    var row = new Row { name = name, shader = material.shader.name, mesh = mesh.name,
                        vertices = mesh.vertexCount, vertexStride = mesh.GetVertexBufferStride(0), bounds = renderer.bounds.size,
                        minX = 512, minY = 512, maxX = -1, maxY = -1,
                        expectedBounded = expectedBounded,
                        flip = material.HasProperty("_Flip") ? material.GetVector("_Flip") : Vector4.zero,
                        rendererColor = material.HasProperty("_RendererColor") ? material.GetVector("_RendererColor") : Vector4.zero };
                    var pixels = image.GetPixels32();
                    for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
                    {
                        Color32 pixel = pixels[y * 512 + x];
                        if (pixel.b <= 8 || pixel.g <= 5) continue;
                        row.pixels++; row.minX = Math.Min(row.minX, x); row.minY = Math.Min(row.minY, y);
                        row.maxX = Math.Max(row.maxX, x); row.maxY = Math.Max(row.maxY, y);
                    }
                    row.bounded = row.pixels > 0 && row.maxX - row.minX < 24 && row.maxY - row.minY < 24;
                    report.cases.Add(row);
                    File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
                    File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(report, true) + "\n");
                    if (row.bounded != expectedBounded)
                        throw new Exception(name + " unexpected native GPU footprint: " + JsonUtility.ToJson(row));
                }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
            }
            Capture("native-sphere-production-sprite", native, sprite);
            Capture("explicit-sphere-production-sprite", explicitSphere, sprite);
            Capture("native-sphere-native-ui", native, ui);
            sprite.SetVector("_Flip", new Vector4(1, 1, 1, 1));
            sprite.SetVector("_RendererColor", Vector4.one);
            Capture("native-sphere-explicit-sprite-properties", native, sprite);
            sprite.SetVector("_Flip", new Vector4(100, 100, 1, 1));
            Capture("sprite-uniform-inflation-control", native, sprite, false);
            Debug.Log("PASS native pointer GPU characterization: " + report.cases.Count);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            Object.DestroyImmediate(sprite); Object.DestroyImmediate(ui); Object.DestroyImmediate(explicitSphere);
            Object.DestroyImmediate(sphere); Object.DestroyImmediate(camera.gameObject);
            target.Release(); Object.DestroyImmediate(target);
        }
    }

    static Mesh Sphere()
    {
        const int latitude = 16, longitude = 24;
        var vertices = new Vector3[(latitude + 1) * (longitude + 1)];
        var colors = new Color[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new List<int>();
        for (int y = 0; y <= latitude; y++) for (int x = 0; x <= longitude; x++)
        {
            float phi = y * Mathf.PI / latitude, theta = x * Mathf.PI * 2 / longitude;
            int i = y * (longitude + 1) + x;
            vertices[i] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)) * .5f;
            colors[i] = Color.white; uv[i] = new Vector2((float)x / longitude, (float)y / latitude);
            if (y == latitude || x == longitude) continue;
            int next = i + longitude + 1;
            triangles.Add(i); triangles.Add(next); triangles.Add(i + 1);
            triangles.Add(i + 1); triangles.Add(next); triangles.Add(next + 1);
        }
        var mesh = new Mesh { name = "explicit ordinary UV sphere" };
        mesh.vertices = vertices; mesh.colors = colors; mesh.uv = uv;
        mesh.triangles = triangles.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
