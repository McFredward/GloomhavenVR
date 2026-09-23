using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Shader-only fixture. Final authored NPC/LOD geometry is checked separately by
// ValidateTownAssets; GL images alone cannot certify the Windows keyword stages.
public static class ValidateTownEyes
{
    static IEnumerator routine;
    static string output;
    static Camera camera;
    static Renderer eye;
    static int assertions;

    public static void Run()
    {
        output = Path.Combine(Directory.GetCurrentDirectory(), "evidence");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory("Build");
        var bundle = BuildPipeline.BuildAssetBundles("Build", new[] {
            new AssetBundleBuild { assetBundleName = "eyes.bundle", assetNames = new[] {
                "Assets/TownEye.shader", "Assets/TownCornea.shader" } }
        }, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        Check(bundle != null, "Windows shader bundle builds");
        routine = Render(); EditorApplication.update += Step;
    }
    static void Step()
    {
        try
        {
            if (routine.MoveNext()) return;
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS " + assertions + " eye shader render assertions\n");
            EditorApplication.update -= Step; EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
        Debug.Log("EYE_PASS " + label);
    }
    static int Picture(string label)
    {
        camera.Render(); RenderTexture.active = camera.targetTexture;
        var texture = new Texture2D(256, 256, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(output, label + ".png"), texture.EncodeToPNG());
        int lit = 0;
        foreach (var pixel in texture.GetPixels32()) if (pixel.r > 4 || pixel.g > 4 || pixel.b > 4) lit++;
        UnityEngine.Object.DestroyImmediate(texture);
        Debug.Log("EYE_PIXELS " + label + "=" + lit);
        return lit;
    }
    static void Visibility(float value)
    {
        var block = new MaterialPropertyBlock();
        block.SetFloat("_TownVisibility", value); eye.SetPropertyBlock(block);
    }
    static IEnumerator Render()
    {
        QualitySettings.pixelLightCount = 0;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0; RenderSettings.reflectionIntensity = 0;
        RenderSettings.fog = false;
        foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>()) UnityEngine.Object.DestroyImmediate(light.gameObject);
        camera = new GameObject("Forward eye probe").AddComponent<Camera>();
        camera.renderingPath = RenderingPath.Forward;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << 27; camera.nearClipPlane = .001f;
        camera.targetTexture = new RenderTexture(256, 256, 24);
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.layer = 27; sphere.transform.localScale = Vector3.one * .025f;
        eye = sphere.GetComponent<Renderer>(); eye.shadowCastingMode = ShadowCastingMode.Off;
        var lamp = new GameObject("Original-practical equivalent").AddComponent<Light>();
        lamp.type = LightType.Point; lamp.renderMode = LightRenderMode.ForceVertex;
        lamp.range = 1; lamp.intensity = 3; lamp.cullingMask = 1 << 27;
        lamp.transform.position = new Vector3(.025f, .04f, -.08f);
        foreach (string shaderName in new[] { "TownEye", "TownCornea" })
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/" + shaderName + ".shader");
            Check(shader != null && shader.isSupported, shaderName + " supported in GL probe");
            var material = new Material(shader); eye.sharedMaterial = material;
            foreach (string atlas in new[] { "brown_eye", "green_eye" })
            {
                if (shaderName == "TownEye")
                {
                    material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/" + atlas + ".png");
                    material.SetColor("_Color", new Color(.65f, .65f, .65f, 1));
                }
                foreach (float offset in new[] { -.003f, .003f })
                {
                    string name = shaderName + "-" + atlas + (offset < 0 ? "-left-view" : "-right-view");
                    camera.transform.position = new Vector3(offset, 0, -.08f); camera.transform.LookAt(Vector3.zero);
                    Visibility(1); lamp.enabled = true; yield return null;
                    int full = Picture(name);
                    Check(full > 20, name + " practical-only light reaches fragments at pixelLights=0");
                    Visibility(.5f); yield return null;
                    int partial = Picture(name + "-dissolving");
                    Check(partial > 0 && partial < full, name + " intermediate dissolve retains lit fragments");
                    Visibility(0); yield return null;
                    Check(Picture(name + "-hidden") == 0, name + " hidden has no floating eyes or glints");
                    Visibility(1); lamp.enabled = false; yield return null;
                    Check(Picture(name + "-dark") == 0, name + " no self-emission without light");
                }
            }
            UnityEngine.Object.DestroyImmediate(material);
        }
        // Reproduce the missing D3D point-light term under GL deliberately. This is
        // a controlled visual negative, separate from inspection of actual D3D blobs.
        eye.sharedMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/BrokenEye.shader"));
        lamp.enabled = true; Visibility(1); yield return null;
        Check(Picture("negative-missing-fragment-practicals") == 0, "missing practical term reproduces black globe despite live point light");
    }
}
