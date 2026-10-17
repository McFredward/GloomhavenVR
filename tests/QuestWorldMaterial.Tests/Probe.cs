using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One retained original floor/material closure in the real Unity renderer.
/// The asset shader is already reconstructed from its native DXBC; this test
/// does not compare it with the Windows executable or observe final Quest eyes.
/// </summary>
public static class QuestWorldMaterialProbe
{
    private const string MeshPath = "Assets/QuestRecoveredBundles/220b503c4884acf498f380a37435cd4c/EN_CR_FloorHex_Edge_Even2.asset";
    private const string Material0 = "Assets/QuestRecoveredBundles/35c54e421df936d4d9a2494084dab2ea/EN_Crypt_Blocks 3.mat";
    private const string Material1 = "Assets/QuestRecoveredBundles/5381b44227d733f439b9b5313b5ed80a/EN_Crypt_Floor_Blocks.mat";
    private const string CapturedTexture = "Assets/QuestRecoveredBundles/21dd9d35059f61f47aaf11a14a67a6c4/GH_Crypt_Blocks_5.png";
    private static int _assertions;
    private static readonly List<object> Pixels = new List<object>();
    private static void Check(bool value, string message)
    { _assertions++; if (!value) throw new InvalidOperationException(message); }

    public static void Run()
    {
        int exit = 0;
        try
        {
            Check(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan, "Native floor uses the real Vulkan graphics backend");
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            Material source0 = AssetDatabase.LoadAssetAtPath<Material>(Material0), source1 = AssetDatabase.LoadAssetAtPath<Material>(Material1);
            Check(mesh != null && mesh.vertexCount == 3113 && mesh.subMeshCount == 2, "Retain the exact native two-submesh floor topology");
            Check(mesh.GetIndexCount(0) == 7872 && mesh.GetIndexCount(1) == 528, "Both actual native floor material parts are present");
            Check(mesh.HasVertexAttribute(VertexAttribute.Position) && mesh.HasVertexAttribute(VertexAttribute.Normal)
                && mesh.HasVertexAttribute(VertexAttribute.Tangent) && mesh.HasVertexAttribute(VertexAttribute.TexCoord0)
                && !mesh.HasVertexAttribute(VertexAttribute.Color), "Native Amp inputs remain complete without inventing a COLOR stream");
            Check(source0 != null && source1 != null && source0.shader == source1.shader && source0.shader.name == "Amp_Basic"
                && source0.shader.isSupported, "Use the exact retained translated native Amp shader");
            Check(source0.GetColor("_Tint").a == 0 && source1.GetColor("_Tint").a == 0, "Preserve the original native tint alpha zero");
            Check(source0.enableInstancing && source1.enableInstancing, "Original material instancing permission stays present");
            Check(source0.mainTexture != null && source1.mainTexture != null, "Both original materials retain their real native texture references");
            var floor = new GameObject("Retained native floor", typeof(MeshFilter), typeof(MeshRenderer));
            floor.layer = 29;
            floor.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = floor.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { source0, source1 };
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            string control = File.ReadAllText("ProbeControl.txt");
            if (control == "hide-world") renderer.enabled = false;
            var camera = new GameObject("Ordinary native-material camera").AddComponent<Camera>(); camera.enabled = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None; camera.cullingMask = 1 << 29;
            camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
            camera.renderingPath = RenderingPath.Forward;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.orthographic = true; camera.orthographicSize = 1.65f; camera.nearClipPlane = .01f; camera.farClipPlane = 20;
            camera.transform.position = new Vector3(0, 4, -2); camera.transform.LookAt(mesh.bounds.center);
            var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Check(target.Create(), "Native floor owns a real GPU color/depth target"); camera.targetTexture = target;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientProbe = new SphericalHarmonicsL2(); RenderSettings.fog = false;
            QualitySettings.pixelLightCount = 4; QualitySettings.shadows = ShadowQuality.Disable;
            var light = new GameObject("Actual point light").AddComponent<Light>();
            light.type = LightType.Point; light.color = Color.white; light.intensity = .35f; light.range = 12;
            light.shadows = LightShadows.None; light.renderMode = LightRenderMode.ForcePixel;
            light.transform.position = new Vector3(.25f, 2.5f, -.4f); light.cullingMask = 1 << 29;
            light.enabled = control != "omit-lighting";

            Color32[] original = Draw(camera, target, "native-point-lit");
            Check(Colored(original) > 100, "The original native floor receives actual per-object lighting and draws colored pixels");
            Check(Original(renderer, mesh, source0, source1), "Native draw retains both renderer material slots and exact mesh identity");
            Check(!mesh.HasVertexAttribute(VertexAttribute.Color), "The real GPU draw neither adds nor requests missing vertex colors");

            // Diagnose the two actual material parts separately. Sampling only
            // the first part sees the thin stone rim, not the central floor.
            var isolated = UnityEngine.Object.Instantiate(mesh);
            isolated.SetIndices(Array.Empty<int>(), MeshTopology.Triangles, 0, false);
            floor.GetComponent<MeshFilter>().sharedMesh = isolated;
            Color32[] centre = Draw(camera, target, "native-central-submesh1");
            Check(Colored(centre) > 100, "The actual central floor material and original submesh1 render with point lighting");
            floor.GetComponent<MeshFilter>().sharedMesh = mesh;
            UnityEngine.Object.DestroyImmediate(isolated);

            // The hardware census names this texture on its first material. The
            // exact runtime material object was not captured; this is a separate
            // clearly labelled source texture binding, not claimed exact state.
            Texture captured = AssetDatabase.LoadAssetAtPath<Texture2D>(CapturedTexture);
            Check(captured != null && captured.width == 2048 && captured.height == 2048, "The captured source texture identity has a retained native image");
            var observed = new Material(source0); observed.mainTexture = captured;
            renderer.sharedMaterials = new[] { observed, source1 };
            Check(Colored(Draw(camera, target, "captured-maintex-binding")) > 100, "The hardware-named main texture still draws native floor pixels");
            renderer.sharedMaterials = new[] { source0, source1 };

            light.enabled = false;
            Color32[] unlit = Draw(camera, target, "native-no-lighting");
            Check(Colored(unlit) < Colored(original) / 10, "Actual scene lighting is a causal GPU input, not inferred from a shader support flag");
            light.enabled = true;
            Check(Draw(camera, target, "native-light-restored").SequenceEqual(original), "Restoring the actual light restores the complete native RGBA image");

            var alternate0 = new Material(source0) { enableInstancing = false };
            var alternate1 = new Material(source1) { enableInstancing = false };
            renderer.sharedMaterials = new[] { alternate0, alternate1 };
            Check(Draw(camera, target, "native-instancing-permission-off").SequenceEqual(original), "A single native floor preserves pixels with either original instancing permission");
            renderer.sharedMaterials = new[] { source0, source1 };
            Check(Original(renderer, mesh, source0, source1) && source0.GetColor("_Tint").a == 0,
                "Qualification leaves actual native floor slots, topology and material alpha untouched");
            Check(!ShaderUtil.ShaderHasError(source0.shader), "The actually exercised original native shader has no compiler error");
            Directory.CreateDirectory("ProbeOutput");
            File.WriteAllText("ProbeOutput/results.json", "{\"assertions\":" + _assertions + ",\"hardwareVerified\":false,\"actualPerObjectLighting\":true,\"originalNativeEngineExecuted\":false,\"graphics\":\"" + SystemInfo.graphicsDeviceType + "\"}\n");
            File.WriteAllText("ProbeOutput/pixels.json", "[\n" + string.Join(",\n", Pixels) + "\n]\n");
            target.Release(); UnityEngine.Object.DestroyImmediate(observed);
            UnityEngine.Object.DestroyImmediate(alternate0); UnityEngine.Object.DestroyImmediate(alternate1);
        }
        catch (Exception error) { UnityEngine.Debug.LogException(error); exit = 1; }
        finally { EditorApplication.Exit(exit); }
    }

    private static bool Original(MeshRenderer renderer, Mesh mesh, Material first, Material second)
        => renderer.GetComponent<MeshFilter>().sharedMesh == mesh && renderer.sharedMaterials[0] == first && renderer.sharedMaterials[1] == second;
    private static int Colored(Color32[] pixels) => pixels.Count(p => p.r > 2 || p.g > 2 || p.b > 2);
    private static Color32[] Draw(Camera camera, RenderTexture target, string label)
    {
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            Color32[] pixels = texture.GetPixels32();
            Directory.CreateDirectory("ProbeOutput"); File.WriteAllBytes("ProbeOutput/" + label + ".png", texture.EncodeToPNG());
            long energy = pixels.Sum(p => (long)p.r + p.g + p.b);
            Pixels.Add("{\"label\":\"" + label + "\",\"colored\":" + Colored(pixels) + ",\"energy\":" + energy + "}");
            return pixels;
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
    }
}
