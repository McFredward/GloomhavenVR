using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class QuestLitPrimitiveProbe
{
    private static int checks;
    private static readonly List<Object> Owned = new List<Object>();
    private static T Keep<T>(T item) where T : Object { Owned.Add(item); return item; }
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }

    public static void Run()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProbeOutput"));
        Directory.CreateDirectory(output);
        try
        {
            var cube = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            MeshFilter filter = cube.GetComponent<MeshFilter>(); Mesh source = filter.sharedMesh;
            Check(source.vertexCount == 24 && source.normals.Length == 24, "Actual builtin lit Cube has authored flat normals");
            QuestStandalonePlatform.Enabled = false;
            Check(QuestLitPrimitiveMesh.Rebuild(source, "desktop") == null, "Desktop retains native Cube identity");
            QuestStandalonePlatform.Enabled = true;
            Mesh copy = Keep(QuestLitPrimitiveMesh.Rebuild(source, "pile-test"));
            Check(copy != null && copy != source && copy.vertexCount == source.vertexCount
                && copy.bounds == source.bounds && copy.subMeshCount == source.subMeshCount, "Owned primitive retains actual geometry metadata");
            for (int i = 0; i < source.vertexCount; i++)
                Check(copy.vertices[i] == source.vertices[i] && copy.normals[i] == source.normals[i] && copy.colors[i] == Color.white,
                    "Position, flat normal and default white COLOR are exact");
            var sourceTangents = source.tangents; var copyTangents = copy.tangents;
            Check(sourceTangents.Length == copyTangents.Length, "Native tangent presence retained");
            for (int i = 0; i < sourceTangents.Length; i++) Check(sourceTangents[i] == copyTangents[i], "Native tangent including handedness retained");
            foreach (var descriptor in source.GetVertexAttributes())
                Check(copy.HasVertexAttribute(descriptor.attribute) && copy.GetVertexAttributeDimension(descriptor.attribute) == descriptor.dimension,
                    "Every native vertex attribute and component count retained");
            for (int channel = 0; channel < 8; channel++)
            {
                var expected = new List<Vector4>(); var actual = new List<Vector4>();
                source.GetUVs(channel, expected); copy.GetUVs(channel, actual);
                Check(expected.Count == actual.Count, "UV channel presence retained");
                for (int i = 0; i < expected.Count; i++) Check(expected[i] == actual[i], "All native UV channels exact");
            }
            var expectedIndices = source.triangles; var actualIndices = copy.triangles;
            Check(expectedIndices.Length == actualIndices.Length, "Index count retained");
            for (int i = 0; i < expectedIndices.Length; i++) Check(expectedIndices[i] == actualIndices[i], "Triangle winding exact");

            var camera = Keep(new GameObject("ordinary-camera")).AddComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.fieldOfView = 60; camera.nearClipPlane = .05f;
            var target = Keep(new RenderTexture(192, 144, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)); target.Create(); camera.targetTexture = target;
            var lamp = Keep(new GameObject("native-light")).AddComponent<Light>(); lamp.type = LightType.Directional;
            lamp.transform.rotation = Quaternion.Euler(20, -30, 0); lamp.intensity = 1.1f;
            var texture = Keep(new Texture2D(8, 8, TextureFormat.RGBA32, false, true)); texture.filterMode = FilterMode.Point;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) texture.SetPixel(x, y, new Color((x + 1) / 8f, (y + 1) / 8f, .3f)); texture.Apply();
            var material = Keep(new Material(Shader.Find("Standard"))); material.mainTexture = texture; material.color = new Color(.6f, .8f, .4f);
            material.SetFloat("_Glossiness", .25f); material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", new Color(.1f, .15f, .08f));
            cube.GetComponent<Renderer>().sharedMaterial = material; cube.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            cube.transform.position = new Vector3(.07f, -.04f, 2.5f); cube.transform.rotation = Quaternion.Euler(17, 23, 11);
            cube.transform.localScale = new Vector3(.8f, .9f, .25f);
            Color32[] Capture(Mesh mesh, string name)
            {
                filter.sharedMesh = mesh; camera.Render(); RenderTexture prior = RenderTexture.active;
                var image = new Texture2D(192, 144, TextureFormat.RGBA32, false, true);
                try
                {
                    RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 192, 144), 0, 0); image.Apply();
                    File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG()); return image.GetPixels32();
                }
                finally { RenderTexture.active = prior; Object.DestroyImmediate(image); }
            }
            Color32[] original = Capture(source, "native-cube");
            int Difference(Color32[] pixels)
            {
                int diff = 0, visible = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 a = pixels[i], b = original[i]; if (a.a != 0) visible++;
                    if (Math.Abs(a.r - b.r) > 1 || Math.Abs(a.g - b.g) > 1 || Math.Abs(a.b - b.b) > 1 || Math.Abs(a.a - b.a) > 1) diff++;
                }
                Check(visible > 100, "Actual Standard Cube draws measured pixels"); return diff;
            }
            Check(Difference(Capture(copy, "owned-cube")) == 0, "Actual Standard/emission GPU output is unchanged by explicit primitive upload");
            var normalMutation = Keep(Object.Instantiate(copy)); var wrong = normalMutation.normals;
            for (int i = 0; i < wrong.Length; i++) wrong[i] = Vector3.up; normalMutation.normals = wrong;
            Check(Difference(Capture(normalMutation, "normal-control")) > 100, "Actual lit output gate rejects lost native normals");
            var uvMutation = Keep(Object.Instantiate(copy)); var wrongUv = uvMutation.uv;
            for (int i = 0; i < wrongUv.Length; i++) wrongUv[i] += new Vector2(.25f, .125f); uvMutation.uv = wrongUv;
            Check(Difference(Capture(uvMutation, "uv-control")) > 100, "Actual output gate rejects wrong native UVs");
            Check(Difference(Capture(source, "following-native-cube")) == 0, "Following ordinary camera render retains its original output");
            Check(!source.HasVertexAttribute(VertexAttribute.Color) && source.normals.Length == 24, "Builtin source data never modified");
            Check(QuestLitPrimitiveMesh.Rebuild(null, "null") == null, "Null input retains native fallback");
            var invalid = Keep(new Mesh()); Check(QuestLitPrimitiveMesh.Rebuild(invalid, "empty") == null, "Empty input refuses reconstruction");
            invalid.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; invalid.triangles = new[] { 0, 1, 2 };
            Check(QuestLitPrimitiveMesh.Rebuild(invalid, "missing-normals") == null, "Missing lighting normals cannot be silently invented");
            File.WriteAllText(Path.Combine(output, "results.json"), "{\"assertions\":" + checks + ",\"graphics\":\"" + SystemInfo.graphicsDeviceType + "\",\"hardwareVerified\":false}\n");
            Debug.Log("PASS Quest lit primitive native CPU/GPU parity: " + checks); EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally { foreach (var item in Owned) if (item != null) Object.DestroyImmediate(item); }
    }
}
