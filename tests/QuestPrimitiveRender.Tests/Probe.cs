using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Ordinary native GPU evidence, not an OpenXR/Quest headset result. Reconstruct
// actual Unity primitives, measure CPU invariants and compare rendered RGBA.
public static class QuestPrimitiveRenderProbe
{
    [Serializable] sealed class GpuRow { public string name; public int pixels, differences; }
    [Serializable] sealed class Report
    {
        public string unity, graphics;
        public int assertions;
        public bool hardwareVerified = false;
        public List<string> meshes = new List<string>();
        public List<GpuRow> gpu = new List<GpuRow>();
    }
    static Report report;
    static string output;

    static void Require(bool value, string text)
    {
        report.assertions++;
        if (!value) throw new Exception(text);
    }

    public static void Run()
    {
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProbeOutput"));
        Directory.CreateDirectory(output);
        report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        var objects = new List<Object>();
        int constructionDiagnostics = 0;
        Application.LogCallback observeLog = (message, trace, type) =>
        {
            if (message.StartsWith("[Quest startup] presentation primitive-upload ", StringComparison.Ordinal))
                constructionDiagnostics++;
        };
        Application.logMessageReceived += observeLog;
        try
        {
            var target = new RenderTexture(256, 192, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create(); objects.Add(target);
            var camera = new GameObject("primitive-camera").AddComponent<Camera>(); objects.Add(camera.gameObject);
            camera.enabled = false; camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear; camera.fieldOfView = 60; camera.nearClipPlane = .05f;
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, true); objects.Add(texture);
            texture.filterMode = FilterMode.Point;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                texture.SetPixel(x, y, new Color((x + 1) / 8f, (y + 1) / 8f, ((x + y) % 3 + 1) / 3f, (x % 3 + 1) / 3f));
            texture.Apply();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/PrimitiveOverlay.shader");
            var material = new Material(shader) { color = new Color(.6f, .7f, .8f, .65f), mainTexture = texture };
            objects.Add(material); material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZTest", (int)CompareFunction.LessEqual); material.SetInt("_ZWrite", 0);
            material.SetFloat("_VertexColor", 1);
            foreach (PrimitiveType kind in new[] { PrimitiveType.Quad, PrimitiveType.Sphere })
            {
                var go = GameObject.CreatePrimitive(kind); objects.Add(go);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                var filter = go.GetComponent<MeshFilter>(); var renderer = go.GetComponent<Renderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.sharedMaterial = material;
                go.transform.position = new Vector3(.11f, -.04f, 2);
                go.transform.rotation = Quaternion.Euler(17, -21, 9);
                go.transform.localScale = new Vector3(.7f, .65f, .6f);
                var source = filter.sharedMesh;
                Require(!source.HasVertexAttribute(VertexAttribute.Color), kind + " actual builtin has no COLOR");
                var sourcePositions = source.vertices; var sourceUV = source.uv; var sourceIndices = source.triangles;
                QuestStandalonePlatform.Enabled = false;
                Require(QuestPrimitiveMesh.Rebuild(source, "desktop") == null, "Desktop retains native mesh by identity");
                QuestStandalonePlatform.Enabled = true;
                var owned = QuestPrimitiveMesh.Rebuild(source, kind.ToString()); objects.Add(owned);
                Require(owned != null && owned != source, "Quest returns an owned mesh");
                Require(owned.vertexCount == source.vertexCount && owned.subMeshCount == source.subMeshCount
                    && owned.bounds == source.bounds && owned.indexFormat == source.indexFormat, "Geometry metadata retained");
                Require(owned.HasVertexAttribute(VertexAttribute.Color) && !owned.HasVertexAttribute(VertexAttribute.Normal)
                    && !owned.HasVertexAttribute(VertexAttribute.Tangent) && !owned.HasVertexAttribute(VertexAttribute.TexCoord1),
                    "Unlit mesh owns explicit COLOR and only consumed vertex streams");
                var positions = owned.vertices; var uv = owned.uv; var colors = owned.colors; var indices = owned.triangles;
                for (int i = 0; i < positions.Length; i++)
                    Require(positions[i] == sourcePositions[i] && uv[i] == sourceUV[i] && colors[i] == Color.white,
                        "Primitive positions/UV/default white preserved at " + i);
                for (int i = 0; i < indices.Length; i++) Require(indices[i] == sourceIndices[i], "Index winding retained");
                report.meshes.Add(QuestPrimitiveMesh.Describe(source)); report.meshes.Add(QuestPrimitiveMesh.Describe(owned));
                Color32[] Capture(string name, Mesh mesh)
                {
                    filter.sharedMesh = mesh; camera.Render();
                    var prior = RenderTexture.active; var image = new Texture2D(256, 192, TextureFormat.RGBA32, false, true);
                    try
                    {
                        RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 256, 192), 0, 0); image.Apply();
                        File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG()); return image.GetPixels32();
                    }
                    finally { RenderTexture.active = prior; Object.DestroyImmediate(image); }
                }
                var nativePixels = Capture(kind + "-native", source);
                void Compare(string name, Color32[] actual, bool equal)
                {
                    int difference = 0, pixels = 0;
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Color32 a = actual[i], b = nativePixels[i]; if (a.a != 0) pixels++;
                        if (Math.Abs(a.r - b.r) > 1 || Math.Abs(a.g - b.g) > 1
                            || Math.Abs(a.b - b.b) > 1 || Math.Abs(a.a - b.a) > 1) difference++;
                    }
                    report.gpu.Add(new GpuRow { name = name, differences = difference, pixels = pixels });
                    Require(pixels > 100, "GPU must actually draw measured geometry");
                    Require(equal ? difference == 0 : difference > 100, "Native GPU RGBA/silhouette expectation: " + name);
                }
                Compare(kind + "-reconstructed-exact", Capture(kind + "-owned", owned), true);
                var changed = Object.Instantiate(owned); objects.Add(changed);
                var changedUV = changed.uv; for (int i = 0; i < changedUV.Length; i++) changedUV[i] += new Vector2(.125f, .25f);
                changed.uv = changedUV;
                Compare(kind + "-UV-mutation-control", Capture(kind + "-uv-control", changed), false);
                var shifted = Object.Instantiate(owned); objects.Add(shifted);
                var shiftedPositions = shifted.vertices; for (int i = 0; i < shiftedPositions.Length; i++) shiftedPositions[i] += new Vector3(.2f, .1f, 0);
                shifted.vertices = shiftedPositions; shifted.RecalculateBounds();
                Compare(kind + "-position-mutation-control", Capture(kind + "-position-control", shifted), false);
                Require(source.vertices.Length == sourcePositions.Length && !source.HasVertexAttribute(VertexAttribute.Color), "Builtin attributes unchanged");
                for (int i = 0; i < sourcePositions.Length; i++) Require(source.vertices[i] == sourcePositions[i], "Builtin positions unchanged");
                go.SetActive(false);
            }
            var multi = new Mesh { name = "colored submesh base-index contract" }; objects.Add(multi);
            multi.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, Vector3.one };
            multi.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one, Vector2.zero };
            multi.colors = new[] { Color.red, Color.green, Color.blue, Color.clear, new Color(.3f, .6f, .7f, .4f) };
            multi.subMeshCount = 2;
            multi.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
            multi.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 1, false, 2); multi.RecalculateBounds();
            var multiOwned = QuestPrimitiveMesh.Rebuild(multi, "colored-submeshes"); objects.Add(multiOwned);
            Require(multiOwned != null && multiOwned.subMeshCount == 2, "Preserve multiple submeshes");
            for (int i = 0; i < multi.vertexCount; i++) Require(multiOwned.colors[i] == multi.colors[i], "Preserve authored color including alpha");
            for (int sub = 0; sub < 2; sub++)
            {
                var expected = multi.GetIndices(sub, true); var actual = multiOwned.GetIndices(sub, true);
                for (int i = 0; i < expected.Length; i++) Require(actual[i] == expected[i], "Preserve actual base-index geometry");
            }
            Require(QuestPrimitiveMesh.Rebuild(null, "null") == null, "Null input falls back");
            var invalid = new Mesh(); objects.Add(invalid);
            Require(QuestPrimitiveMesh.Rebuild(invalid, "empty") == null, "Empty input falls back");
            invalid.vertices = new[] { Vector3.zero, Vector3.one, Vector3.up };
            invalid.triangles = new[] { 0, 1, 2 };
            Require(QuestPrimitiveMesh.Rebuild(invalid, "missing-uv") == null, "Missing UV cannot be silently invented");
            invalid.uv = new[] { Vector2.zero, Vector2.one, Vector2.up };
            invalid.vertices = new[] { new Vector3(float.NaN, 0, 0), Vector3.one, Vector3.up };
            Require(QuestPrimitiveMesh.Rebuild(invalid, "nonfinite") == null, "Nonfinite source refuses upload");
            invalid.vertices = new[] { Vector3.zero, Vector3.one, Vector3.up };
            invalid.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
            Require(QuestPrimitiveMesh.Rebuild(invalid, "line-topology") == null, "Unsupported topology falls back");
            invalid.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0); invalid.UploadMeshData(true);
            Require(QuestPrimitiveMesh.Rebuild(invalid, "not-readable") == null, "Unreadable source falls back");
            Require(constructionDiagnostics == 8, "Construction diagnostics have a bounded success/refusal budget");
            var afterBudget = QuestPrimitiveMesh.Rebuild(multi, "after-diagnostic-budget"); objects.Add(afterBudget);
            Require(afterBudget != null && constructionDiagnostics == 8, "Diagnostic cap never gates actual geometry repair");
            File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("PASS Quest primitive CPU/GPU reconstruction: " + report.assertions);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            Application.logMessageReceived -= observeLog;
            foreach (Object item in objects) if (item != null) Object.DestroyImmediate(item);
        }
    }
}
