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
        public string expectedPixels;
        public Vector3 bounds;
        public Vector4 flip, rendererColor;
        public Color center;
        [NonSerialized] public Color32[] pixelData;
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
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
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
        var factory = new GloomhavenVR.Hands.Interact.PointerProductionMaterialFactory();
        var sprite = factory.Build(false);
        var owned = factory.Build(true);
        var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        occluder.transform.position = new Vector3(.1f, .08f, 1);
        occluder.transform.localScale = Vector3.one * .15f;
        var occluderMaterial = new Material(Shader.Find("Unlit/Color"));
        occluderMaterial.color = Color.black;
        occluder.GetComponent<Renderer>().sharedMaterial = occluderMaterial;
        occluder.SetActive(false);
        var line = new GameObject("native-line-color-contract").AddComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = 2; line.widthMultiplier = .004f;
        line.SetPositions(new[] { new Vector3(-.2f, 0, 1.5f), new Vector3(.2f, .1f, 1.5f) });
        line.startColor = new Color(.45f, .8f, 1, .35f);
        line.endColor = new Color(.45f, .8f, 1, .05f);
        line.gameObject.SetActive(false);
        try
        {
            void Require(bool condition, string message)
            { if (!condition) throw new Exception(message); }
            Require(sprite.shader == Shader.Find("Sprites/Default"), "Desktop keeps its original native sprite material");
            Require(owned.shader == sprite.shader, "Quest retains the same native sprite shader and target alpha contract");
            Require(owned.renderQueue == 4600 && sprite.renderQueue == 4600, "Both platform paths retain pointer render queue");
            Require(!owned.enableInstancing && owned.GetVector("_Flip") == Vector4.one && owned.GetColor("_RendererColor") == Color.white,
                "Quest pins neutral native sprite renderer inputs and disables material instancing");
            Row Capture(string name, Mesh mesh, Material material, string expectedPixels = "small", bool quest = false, bool corruptBlock = false)
            {
                filter.sharedMesh = mesh; renderer.sharedMaterial = material;
                if (line.gameObject.activeSelf) line.sharedMaterial = material;
                renderer.SetPropertyBlock(null); line.SetPropertyBlock(null);
                if (corruptBlock)
                {
                    var corrupt = new MaterialPropertyBlock();
                    corrupt.SetVector("_Flip", new Vector4(100, 100, 1, 1));
                    corrupt.SetColor("_RendererColor", Color.red);
                    corrupt.SetFloat("_PointerUnrelatedFixtureValue", 7);
                    renderer.SetPropertyBlock(corrupt); line.SetPropertyBlock(corrupt);
                }
                factory.Apply(renderer, quest); factory.Apply(line, quest);
                if (!quest) Require(renderer.HasPropertyBlock() == corruptBlock && line.HasPropertyBlock() == corruptBlock,
                    "Desktop does not add or replace a renderer property block");
                if (quest)
                {
                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                    Require(block.GetVector("_Flip") == Vector4.one && block.GetColor("_RendererColor") == Color.white,
                        "Quest final renderer block overrides sprite-only inputs");
                    if (corruptBlock) Require(block.GetFloat("_PointerUnrelatedFixtureValue") == 7,
                        "Quest sprite repair retains unrelated renderer block state");
                }
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
                        expectedPixels = expectedPixels, expectedBounded = expectedPixels == "small",
                        flip = material.HasProperty("_Flip") ? material.GetVector("_Flip") : Vector4.zero,
                        rendererColor = material.HasProperty("_RendererColor") ? material.GetVector("_RendererColor") : Vector4.zero };
                    var pixels = image.GetPixels32();
                    row.pixelData = pixels;
                    for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
                    {
                        Color32 pixel = pixels[y * 512 + x];
                        if (pixel.b <= 8 || pixel.g <= 5) continue;
                        row.pixels++; row.minX = Math.Min(row.minX, x); row.minY = Math.Min(row.minY, y);
                        row.maxX = Math.Max(row.maxX, x); row.maxY = Math.Max(row.maxY, y);
                    }
                    row.bounded = row.pixels > 0 && row.maxX - row.minX < 24 && row.maxY - row.minY < 24;
                    if (row.pixels > 0) row.center = image.GetPixel((row.minX + row.maxX) / 2, (row.minY + row.maxY) / 2);
                    report.cases.Add(row);
                    File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
                    File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(report, true) + "\n");
                    bool valid = expectedPixels == "small" ? row.bounded : expectedPixels == "none" ? row.pixels == 0
                        : expectedPixels == "line" ? row.pixels > 0 && row.pixels < 10000 : row.pixels > 100000;
                    if (!valid)
                        throw new Exception(name + " unexpected native GPU footprint: " + JsonUtility.ToJson(row));
                    return row;
                }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
            }
            void SameRgba(Row actual, Row expected, string message)
            {
                Require(actual.pixels == expected.pixels && actual.minX == expected.minX && actual.maxX == expected.maxX
                    && actual.minY == expected.minY && actual.maxY == expected.maxY
                    && Mathf.Abs(actual.center.r - expected.center.r) < .009f
                    && Mathf.Abs(actual.center.g - expected.center.g) < .009f
                    && Mathf.Abs(actual.center.b - expected.center.b) < .009f
                    && Mathf.Abs(actual.center.a - expected.center.a) < .009f, message);
                for (int i = 0; i < actual.pixelData.Length; i++)
                {
                    Color32 a = actual.pixelData[i], b = expected.pixelData[i];
                    Require(Math.Abs(a.r - b.r) <= 2 && Math.Abs(a.g - b.g) <= 2
                        && Math.Abs(a.b - b.b) <= 2 && Math.Abs(a.a - b.a) <= 2,
                        message + " pixel=" + i);
                }
            }
            var desktop = Capture("desktop-native-sphere-original-sprite", native, sprite);
            var quest = Capture("quest-native-sphere-explicit-sprite", native, owned, quest: true);
            SameRgba(quest, desktop, "Owned Quest pointer retains native Sprite RGBA blending and silhouette");
            Capture("quest-explicit-sphere-native-sprite", explicitSphere, owned, quest: true);
            owned.SetVector("_Flip", new Vector4(100, 100, 1, 1));
            owned.SetColor("_RendererColor", Color.red);
            var unaffected = Capture("quest-renderer-block-repairs-material-control", native, owned, quest: true);
            SameRgba(unaffected, quest, "Quest renderer block repairs inflated sprite material fields without changing RGBA");
            owned.SetVector("_Flip", Vector4.one); owned.SetColor("_RendererColor", Color.white);
            sprite.SetVector("_Flip", new Vector4(100, 100, 1, 1));
            Capture("sprite-uniform-inflation-control", native, sprite, "large");
            sprite.SetVector("_Flip", Vector4.one);
            occluder.SetActive(true);
            Capture("quest-depth-occluded", native, owned, "none", quest: true);
            Capture("desktop-depth-occluded", native, sprite, "none");
            occluder.SetActive(false);
            var colors = explicitSphere.colors;
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(.6f, .3f, .8f, .4f);
            explicitSphere.colors = colors;
            var desktopTint = Capture("desktop-mesh-vertex-tint", explicitSphere, sprite);
            SameRgba(Capture("quest-mesh-vertex-tint", explicitSphere, owned, quest: true), desktopTint, "Quest retains mesh vertex color and alpha");
            renderer.enabled = false; line.gameObject.SetActive(true);
            var desktopLine = Capture("desktop-line-vertex-alpha", native, sprite, "line");
            SameRgba(Capture("quest-line-vertex-alpha", native, owned, "line", quest: true), desktopLine, "Quest retains the actual LineRenderer color/alpha gradient");
            renderer.enabled = true; line.gameObject.SetActive(false);
            SameRgba(Capture("quest-final-block-repairs-foreign-block-control", native, owned, quest: true, corruptBlock: true), quest,
                "Quest repairs final sprite renderer block fields without changing RGBA or unrelated fields");
            Debug.Log("PASS native pointer GPU characterization: " + report.cases.Count);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            Object.DestroyImmediate(sprite); Object.DestroyImmediate(owned); Object.DestroyImmediate(explicitSphere);
            Object.DestroyImmediate(occluder); Object.DestroyImmediate(occluderMaterial); Object.DestroyImmediate(line.gameObject);
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
