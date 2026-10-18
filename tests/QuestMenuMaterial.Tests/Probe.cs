using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.WorldUI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Real ordinary-camera GPU pixels, not an XR provider substitute. The capture
// proves the opaque/premultiplied/foreground contract of the production factory;
// only the next headset run establishes whether its menu is visible.
public static class QuestMenuMaterialProbe
{
    [Serializable] sealed class Report
    {
        public string unity, graphics, colorSpace;
        public bool hardwareVerified;
        public int comparisons, defectControls;
        public List<string> cases = new List<string>();
    }
    public static void Run()
    {
        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(), colorSpace = QualitySettings.activeColorSpace.ToString() };
        var screenShader = Resources.Load<Shader>("QuestWorldScreen");
        var glassShader = Resources.Load<Shader>("QuestWorldScreenGlass");
        var target = new RenderTexture(192, 144, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create();
        var camera = new GameObject("menu-material-camera").AddComponent<Camera>();
        camera.enabled = false; camera.targetTexture = target; camera.aspect = 192f / 144f;
        camera.fieldOfView = 65; camera.nearClipPlane = .01f; camera.farClipPlane = 10;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .18f, .22f, .3f);
        camera.renderingPath = RenderingPath.Forward; camera.allowHDR = false; camera.allowMSAA = false;
        var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
        background.transform.position = new Vector3(0, 0, 2.1f);
        background.transform.localScale = new Vector3(2.4f, 1.5f, 1);
        var glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glass.transform.position = new Vector3(0, 0, 2);
        glass.transform.localScale = new Vector3(2.4f * 2f / 2.1f, 1.5f * 2f / 2.1f, 1);
        var foreground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        foreground.transform.position = new Vector3(0, 0, 1);
        foreground.transform.localScale = Vector3.one * .15f;
        var foregroundMaterial = new Material(Shader.Find("Unlit/Color"));
        foregroundMaterial.color = Color.magenta;
        foreground.GetComponent<Renderer>().sharedMaterial = foregroundMaterial;
        foreground.SetActive(false);
        var left = Pattern(false, false); var right = Pattern(true, false); var ui = Pattern(false, true);
        var originalScreen = new Material(screenShader) { mainTexture = left, renderQueue = 1000 };
        originalScreen.SetTexture("_RightTex", left); originalScreen.SetFloat("_StereoCapture", 0);
        var originalGlass = new Material(glassShader) { mainTexture = ui };
        var factory = new FlatScreen();
        var ownedScreen = FlatScreen.Build(screenShader, left, false, true, false);
        var ownedGlass = FlatScreen.Build(glassShader, ui, true, true, false);
        factory.Apply(background.GetComponent<Renderer>(), ownedScreen, false);
        factory.MenuQueue(ownedScreen, 1000); // Actual TickBackdropDepth's menu policy and queue synchronizer.
        void Require(bool condition, string detail) { if (!condition) throw new Exception(detail); }
        Color32[] Capture(Material opaque, Material transparent)
        {
            if (opaque == ownedScreen) factory.Apply(background.GetComponent<Renderer>(), opaque, false);
            else background.GetComponent<Renderer>().sharedMaterials = new[] { opaque };
            factory.Apply(glass.GetComponent<Renderer>(), transparent, true);
            camera.Render();
            var previous = RenderTexture.active;
            var cpu = new Texture2D(192, 144, TextureFormat.RGBA32, false, true);
            try { RenderTexture.active = target; cpu.ReadPixels(new Rect(0, 0, 192, 144), 0, 0); cpu.Apply(); return cpu.GetPixels32(); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(cpu); }
        }
        int Difference(Color32[] a, Color32[] b)
        {
            int max = 0;
            for (int index = 0; index < a.Length; index++)
            {
                max = Math.Max(max, Math.Abs(a[index].r - b[index].r));
                max = Math.Max(max, Math.Abs(a[index].g - b[index].g));
                max = Math.Max(max, Math.Abs(a[index].b - b[index].b));
                max = Math.Max(max, Math.Abs(a[index].a - b[index].a));
            }
            return max;
        }
        try
        {
            Require(ownedScreen.shader.name == "GloomhavenVR/Overlay" && ownedGlass.shader == ownedScreen.shader,
                "Quest MultiPass uses the owned ordinary-mesh shader for both layers");
            var backgroundRenderer = background.GetComponent<Renderer>();
            Require(backgroundRenderer.sharedMaterials.Length == 2 && backgroundRenderer.sharedMaterial == ownedScreen
                && backgroundRenderer.sharedMaterials[1].renderQueue == 999,
                "Opaque underlay follows the real menu queue without taking stereo texture ownership");
            factory.Apply(backgroundRenderer, ownedGlass, true);
            Require(backgroundRenderer.sharedMaterials.Length == 1 && backgroundRenderer.sharedMaterial == ownedGlass,
                "Split glass removes the opaque alpha underlay instead of leaving a black second pass");
            factory.Apply(backgroundRenderer, ownedScreen, false);
            Require(backgroundRenderer.sharedMaterials.Length == 2 && backgroundRenderer.sharedMaterial == ownedScreen,
                "Split teardown returns the same opaque presentation and consuming material");
            foreach (bool shared in new[] { false, true }) foreach (bool isGlass in new[] { false, true })
            {
                Shader prior = isGlass ? glassShader : screenShader;
                var material = FlatScreen.Build(prior, ui, isGlass, false, shared);
                Require(material.shader == prior, "Desktop retains the exact supplied shader");
                Object.DestroyImmediate(material);
            }
            foreach (bool isGlass in new[] { false, true })
            {
                Shader prior = isGlass ? glassShader : screenShader;
                var material = FlatScreen.Build(prior, ui, isGlass, true, true);
                Require(material.shader == prior, "Shared-eye Quest modes retain the existing stereo-aware shader");
                Object.DestroyImmediate(material);
            }
            foreach (bool rightEye in new[] { false, true })
            foreach (bool occlude in new[] { false, true })
            foreach (Vector3 angles in new[] { Vector3.zero, new Vector3(3, 8, 11), new Vector3(-5, -7, -13) })
            {
                // MultiPass's existing late material swap is still authoritative.
                var image = rightEye ? right : left;
                originalScreen.mainTexture = image; originalScreen.SetTexture("_RightTex", image);
                ownedScreen.mainTexture = image;
                camera.transform.rotation = Quaternion.Euler(angles); foreground.SetActive(occlude);
                var expected = Capture(originalScreen, originalGlass);
                var actual = Capture(ownedScreen, ownedGlass);
                int difference = Difference(expected, actual);
                Require(difference <= 1, "Owned screen/glass RGBA, depth and pose parity differs by " + difference
                    + " right=" + rightEye + " occlude=" + occlude + " angles=" + angles
                    + " expectedCenter=" + expected[72 * 192 + 96] + " actualCenter=" + actual[72 * 192 + 96]);
                report.comparisons++;
                report.cases.Add((rightEye ? "right" : "left") + ":foreground=" + occlude + ":pose=" + angles + ":maxRGBA=" + difference);
            }
            camera.transform.rotation = Quaternion.identity; foreground.SetActive(false);
            originalScreen.mainTexture = left; originalScreen.SetTexture("_RightTex", left); ownedScreen.mainTexture = left;
            var reference = Capture(originalScreen, originalGlass);
            ownedGlass.SetInt("_SrcBlend", 5);
            Require(Difference(reference, Capture(ownedScreen, ownedGlass)) > 8, "Control detects a second glass alpha multiplication");
            ownedGlass.SetInt("_SrcBlend", 1); report.defectControls++;
            ownedScreen.mainTexture = right;
            Require(Difference(reference, Capture(ownedScreen, ownedGlass)) > 20, "Control detects incorrect per-eye texture routing");
            ownedScreen.mainTexture = left; report.defectControls++;
            foreground.SetActive(true); reference = Capture(originalScreen, originalGlass);
            ownedScreen.renderQueue = 4000;
            Require(Difference(reference, Capture(ownedScreen, ownedGlass)) > 20, "Control detects an opaque screen drawn over foreground hands");
            ownedScreen.renderQueue = 1000; report.defectControls++;
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProbeOutput"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("PASS Quest menu material comparisons=" + report.comparisons + " defectControls=" + report.defectControls);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(background); Object.DestroyImmediate(glass);
            Object.DestroyImmediate(foreground); Object.DestroyImmediate(foregroundMaterial);
            Object.DestroyImmediate(originalScreen); Object.DestroyImmediate(originalGlass); Object.DestroyImmediate(ownedScreen); Object.DestroyImmediate(ownedGlass);
            Object.DestroyImmediate(left); Object.DestroyImmediate(right); Object.DestroyImmediate(ui); target.Release(); Object.DestroyImmediate(target);
        }
    }
    static Texture2D Pattern(bool right, bool ui)
    {
        var result = new Texture2D(32, 16, TextureFormat.RGBA32, false, true);
        var pixels = new Color32[512];
        for (int y = 0; y < 16; y++) for (int x = 0; x < 32; x++)
        {
            byte alpha = ui ? (byte)(x < 8 ? 0 : x < 20 ? 96 : 255) : (byte)255;
            pixels[y * 32 + x] = ui ? new Color32((byte)(alpha * .8f), (byte)(alpha * .6f), (byte)(alpha * .2f), alpha)
                : new Color32((byte)(right ? x * 6 : 30), (byte)(y * 12), (byte)(right ? 30 : x * 6), (byte)(x < 8 ? 0 : x < 20 ? 96 : 255));
        }
        result.SetPixels32(pixels); result.Apply(); return result;
    }
}
