using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class QuestProbeDiagnosticsRunner
{
    [Serializable] sealed class Case { public string name, ns, expected; }
    [Serializable] sealed class Manifest { public string output; public Case[] cases; }
    static int assertions;
    static Manifest activeManifest;
    static StreamWriter activeOutput;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    static object Call(object instance, string name, params object[] args)
    {
        try { return instance.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    static object Get(object value, string name) { return value.GetType().GetField(name).GetValue(value); }
    static T Get<T>(object value, string name) { return (T)Get(value, name); }
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-questDiagnosticsManifest") + 1]));
        var output = new StreamWriter(Path.Combine(manifest.output, "results.txt"));
        activeManifest = manifest; activeOutput = output;
        try
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/QuestDiagnosticAlbedo.shader");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "diagnostic albedo shader compiles");
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.CreateAsset(new Material(shader), "Assets/Resources/quest-albedo-material.mat");
            File.WriteAllText("Assets/Resources/quest-probe-materials.json", "{\"schema\":1,\"originalShaderFidelity\":false,\"materials\":[{\"materialName\":\"fixture source\",\"sourceShader\":\"Original/Unverified\",\"sourceSavedColor\":{\"r\":1,\"g\":0.3,\"b\":0,\"a\":1}}]}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            EditorApplication.update += PlaymodeStep;
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { output.WriteLine("FAIL setup: " + error); output.Dispose(); EditorApplication.Exit(1); }
    }
    static void PlaymodeStep()
    {
        if (!EditorApplication.isPlaying) return;
        EditorApplication.update -= PlaymodeStep;
        var manifest = activeManifest; var output = activeOutput;
        bool passed = true;
        try
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/QuestDiagnosticAlbedo.shader");
            output.WriteLine("Unity " + Application.unityVersion + "; graphics=" + SystemInfo.graphicsDeviceName);
            RenderShaderComparison(shader);
            foreach (Case entry in manifest.cases)
            {
                try
                {
                    Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(entry.ns + ".QuestProbeDiagnostics")).FirstOrDefault(t => t != null);
                    Check(type != null, "actual runtime type loads: " + entry.name);
                    Validate(type, shader, Path.Combine(manifest.output, entry.name));
                    if (!string.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: " + entry.name);
                    output.WriteLine("PASS " + entry.name);
                }
                catch (Exception error)
                {
                    if (!string.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                        output.WriteLine("PASS negative control " + entry.name + ": " + error.Message);
                    else { passed = false; output.WriteLine("FAIL " + entry.name + ": " + error); }
                }
                output.Flush();
            }
            output.WriteLine(assertions + " assertions");
        }
        catch (Exception error) { passed = false; output.WriteLine("FAIL setup: " + error); }
        finally { output.Dispose(); }
        EditorApplication.Exit(passed ? 0 : 1);
    }
    static void RenderShaderComparison(Shader shader)
    {
        var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, Color.green); texture.Apply();
        var material = new Material(shader); material.mainTexture = texture; material.color = new Color(.3f, .5f, .7f, 1);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.layer = 31; quad.GetComponent<Renderer>().sharedMaterial = material;
        var camera = new GameObject("unlit shader pixel comparison").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -2); camera.orthographic = true; camera.orthographicSize = .6f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.magenta; camera.cullingMask = 1 << 31;
        var target = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGB32);
        var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixel.ReadPixels(new Rect(32, 32, 1, 1), 0, 0); pixel.Apply(); Color color = pixel.GetPixel(0, 0);
            Check(color.r < .04f && color.b < .04f && Mathf.Abs(color.g - .5f) < .06f,
                "actual albedo shader multiplies texture and tint without scene lighting: " + color);
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.DestroyImmediate(quad); Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(material); Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(pixel);
        }
    }
    static void Validate(Type type, Shader shader, string output)
    {
        Directory.CreateDirectory(output);
        Type windowType = type.GetNestedType("FrameWindow");
        object window = Activator.CreateInstance(windowType, new object[] { 100 });
        for (int i = 1; i <= 100; i++) Call(window, "RecordMilliseconds", (float)i);
        object stats = Call(window, "Read", 30f, 123456L);
        Check(Get<int>(stats, "samples") == 100 && Get<float>(stats, "minMs") == 1 && Get<float>(stats, "maxMs") == 100, "rolling frame min/max reflect actual samples");
        Check(Get<float>(stats, "averageMs") == 50.5f, "frame average uses all samples");
        Check(Get<float>(stats, "p95Ms") == 95f, "frame p95 uses nearest-rank ninety-fifth percentile");
        Check(Get<int>(stats, "longFrames") == 50 && Get<float>(stats, "longFrameBudgetMs") == 50, "long frames use actual XR refresh period");
        Check(!Get<bool>(stats, "gpuTimingAvailable") && Get<long>(stats, "managedBytes") == 123456L, "managed memory is separate from unavailable GPU timing");
        Call(window, "RecordMilliseconds", float.NaN); Call(window, "RecordMilliseconds", float.PositiveInfinity); Call(window, "RecordMilliseconds", 0f);
        Check(Get<int>(Call(window, "Read", 0f, 0L), "samples") == 100, "invalid timing samples never enter rolling storage");
        Check(Get<int>(Call(window, "Read", 0f, 0L), "longFrames") == -1, "unknown XR refresh never invents a long-frame budget");
        window = Activator.CreateInstance(windowType, new object[] { 4 });
        for (int i = 1; i <= 10; i++) Call(window, "RecordMilliseconds", (float)i);
        stats = Call(window, "Read", 72f, 0L);
        Check(Get<int>(stats, "samples") == 4 && Get<float>(stats, "minMs") == 7 && Get<float>(stats, "averageMs") == 8.5f, "rolling window retires oldest samples");

        var stage = new GameObject("fixture stage");
        var camera = new GameObject("fixture view").AddComponent<Camera>();
        var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var texture = new Texture2D(4, 2) { name = "actual fixture atlas" };
        var source = new Material(shader) { name = "fixture source", color = new Color(.3f, .5f, .7f, 1) };
        Component diagnostics = null;
        try
        {
            model.transform.SetParent(stage.transform, false);
            model.transform.localPosition = new Vector3(.25f, .83f, 1.3f); model.transform.localScale = Vector3.one * .25f;
            Renderer renderer = model.GetComponent<Renderer>();
            source.mainTexture = texture; source.mainTextureScale = new Vector2(.5f, .75f); source.mainTextureOffset = new Vector2(.125f, .25f);
            renderer.sharedMaterial = source;
            var animator = model.AddComponent<Animator>(); animator.speed = 1.75f; animator.fireEvents = false;
            var secondary = new GameObject("independent animator").AddComponent<Animator>(); secondary.transform.SetParent(model.transform, false);
            secondary.speed = .6f; secondary.enabled = false;
            Vector3 originalPosition = model.transform.localPosition, originalScale = model.transform.localScale;
            diagnostics = stage.AddComponent(type);
            Call(diagnostics, "Initialize", stage.transform, camera, model, 611, "fixture-exact-input", false, output);
            Check(renderer.sharedMaterial != source && source.mainTexture == texture && source.color == new Color(.3f, .5f, .7f, 1), "source material remains immutable while owned lit clone runs");
            Transform panel = (Transform)type.GetProperty("Panel").GetValue(diagnostics);
            RawImage atlas = panel.GetComponentInChildren<RawImage>(true);
            Check(atlas.texture == texture && atlas.color == source.color, "atlas preview shows actual source texture and tint");
            Check(panel.GetComponentsInChildren<Image>(true).Count(i => i.name.StartsWith("UI colour swatch ")) == 6, "stereo reference stays within UI as six authored swatches");
            Color[] expectedColors = { Color.white, new Color(.5f, .5f, .5f), Color.red, Color.green, Color.blue, Color.black };
            for (int i = 0; i < expectedColors.Length; i++)
                Check(panel.Find("UI stereo colour reference/UI colour swatch " + i).GetComponent<Image>().color == expectedColors[i], "UI colour reference retains numeric swatch " + i);
            Call(diagnostics, "ToggleRenderMode");
            Check(renderer.sharedMaterial.mainTexture == texture, "albedo mode retains original texture object");
            Check(renderer.sharedMaterial.mainTextureScale == source.mainTextureScale && renderer.sharedMaterial.mainTextureOffset == source.mainTextureOffset, "albedo mode retains original UV scale and offset");
            Check(renderer.sharedMaterial.color == source.color, "albedo mode retains original tint");
            Check(renderer.sharedMaterial.shader == shader, "albedo comparison uses retained tinted unlit shader");
            object snapshot = Call(diagnostics, "CaptureSnapshot");
            Array materials = (Array)Get(snapshot, "materials"); object info = materials.GetValue(0);
            Check(Get<bool>(info, "texturePresent") && Get<int>(info, "width") == 4 && Get<int>(info, "height") == 2, "snapshot describes actual texture dimensions");
            Check(Get<string>(info, "sourceShader") == "Original/Unverified" && !Get<bool>(info, "originalShaderFidelity"), "source metadata cannot certify original shader fidelity");
            Call(diagnostics, "ToggleRenderMode");
            Check(renderer.sharedMaterial.mainTexture == texture && source.color == new Color(.3f, .5f, .7f, 1), "lit round trip retains texture and immutable source tint");
            Call(diagnostics, "ToggleAnimation"); Check(animator.speed == 0f && secondary.speed == 0f, "animation pause keeps original animators and controllers");
            Call(diagnostics, "ToggleAnimation"); Check(animator.speed == 1.75f && secondary.speed == .6f && !secondary.enabled && !animator.fireEvents, "animation resume restores each original speed without enabling events");
            float floor = renderer.bounds.min.y;
            Call(diagnostics, "ToggleModelScale");
            Check(Vector3.Distance(model.transform.localScale, originalScale * 3) < .0001f && Mathf.Abs(renderer.bounds.min.y - floor) < .0001f, "larger inspection model remains grounded on existing tabletop");
            Call(diagnostics, "ToggleModelScale");
            Check(model.transform.localScale == originalScale && model.transform.localPosition == originalPosition, "inspection round trip restores exact authored pose and scale");

            Type trackingType = type.Assembly.GetType(type.Namespace + ".QuestProbeTrackingSnapshot");
            object tracking = Activator.CreateInstance(trackingType);
            foreach (string name in new[] { "headTracked", "leftTracked", "rightTracked", "leftAimTracked", "rightAimTracked" }) trackingType.GetField(name).SetValue(tracking, true);
            trackingType.GetField("leftAimDirection").SetValue(tracking, Vector3.right);
            trackingType.GetField("rigPosition").SetValue(tracking, new Vector3(2, 0, 3)); trackingType.GetField("rigYaw").SetValue(tracking, 37f);
            Call(diagnostics, "UpdateTracking", tracking);
            foreach (string name in new[] { "headTracked", "leftTracked", "rightTracked", "leftAimTracked", "rightAimTracked" }) trackingType.GetField(name).SetValue(tracking, false);
            Call(diagnostics, "UpdateTracking", tracking); Call(diagnostics, "UpdateTracking", tracking);
            snapshot = Call(diagnostics, "CaptureSnapshot");
            Check(Get<int>(snapshot, "headTrackingLosses") == 1 && Get<int>(snapshot, "leftAimTrackingLosses") == 1 && Get<int>(snapshot, "rightTrackingLosses") == 1, "tracking losses count transitions, not every invalid frame");
            Check(Get<Vector3>(Get(snapshot, "tracking"), "leftAimDirection") == Vector3.right && Get<float>(Get(snapshot, "tracking"), "rigYaw") == 37f, "snapshot retains actual aim direction and world rig pose");

            BoxCollider next = panel.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name == "probeNextPage");
            Check((bool)Call(diagnostics, "TryActivate", next), "trigger hit routes the active diagnostic button");
            Check(Get<int>(Call(diagnostics, "CaptureSnapshot"), "page") == 1, "page action changes only selected diagnostic page");
            Call(diagnostics, "SetLanguage", true);
            Check(panel.GetComponentsInChildren<Text>(true).Any(t => t.text == GloomhavenVR.Core.QuestText.Get("probeToggleAnimation", true)), "actual bilingual localization reaches action buttons");
            Call(diagnostics, "NextPage"); Call(diagnostics, "NextPage");
            Check(Get<int>(Call(diagnostics, "CaptureSnapshot"), "page") == 0, "page selection wraps bounded battery");
            for (int i = 0; i < 20; i++) Call(diagnostics, "OnApplicationPause", i % 2 == 0);
            Call(diagnostics, "SaveSnapshot");
            string json = File.ReadAllText(Path.Combine(output, "quest-hardware-state.json"));
            snapshot = JsonUtility.FromJson(json, type.GetNestedType("HardwareSnapshot"));
            Check(Get<int>(snapshot, "modBuild") == 611 && Get<string>(snapshot, "inputKey") == "fixture-exact-input", "persisted snapshot retains actual build provenance");
            Check(((Array)Get(snapshot, "lifecycle")).Length == 16 && Get<int>(snapshot, "pauseEvents") == 20 && json.Length < 131072, "lifecycle and JSON remain bounded while retaining event count");
            Check(!Get<bool>(Get(snapshot, "timing"), "gpuTimingAvailable"), "persisted snapshot never claims unavailable GPU measurements");
            Call(diagnostics, "ToggleAnimation"); Call(diagnostics, "ToggleModelScale"); Call(diagnostics, "ToggleRenderMode");
            Object.DestroyImmediate(diagnostics); diagnostics = null;
            Check(renderer.sharedMaterial == source && source.mainTexture == texture, "diagnostic destruction restores original material references");
            Check(animator.speed == 1.75f && secondary.speed == .6f && model.transform.localScale == originalScale && model.transform.localPosition == originalPosition, "diagnostic destruction restores animation and model pose");
            int writeWarnings = 0;
            Application.LogCallback capture = (message, trace, logType) => { if (message.StartsWith("[QuestProbe] bounded snapshot write failed:")) writeWarnings++; };
            Application.logMessageReceived += capture;
            string blocked = Path.Combine(output, "blocked-directory"); File.WriteAllText(blocked, "fixture");
            try
            {
                diagnostics = stage.AddComponent(type);
                Call(diagnostics, "Initialize", stage.transform, camera, model, 611, "fixture-exact-input", false, blocked);
                Call(diagnostics, "SaveSnapshot"); Call(diagnostics, "SaveSnapshot");
                Check(writeWarnings == 1, "continuous snapshot failure reports once and keeps the probe alive");
                Check(((Transform)type.GetProperty("Panel").GetValue(diagnostics)).GetComponentsInChildren<Text>(true).Any(t => t.text.Contains(GloomhavenVR.Core.QuestText.Get("probeStateFailed", false))), "failed snapshot is visible without a false success label");
            }
            finally { Application.logMessageReceived -= capture; }
        }
        finally
        {
            if (diagnostics != null) Object.DestroyImmediate(diagnostics);
            Object.DestroyImmediate(stage); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(source); Object.DestroyImmediate(texture);
        }
    }
}
