using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ValidateTownLighting
{
    static IEnumerator routine;
    static int assertions, negatives;
    static string output;
    static Camera camera;
    static Texture2D readback;
    static readonly List<string> results = new List<string>();
    public static void Run()
    {
        output = Directory.GetParent(Application.dataPath).FullName;
        Debug.Log("Town lighting fixture starting");
        routine = Execute(); EditorApplication.update += Step;
    }
    static void Step()
    {
        try { if (!routine.MoveNext()) { EditorApplication.update -= Step; EditorApplication.Exit(0); } }
        catch (Exception error) { Debug.LogException(error); EditorApplication.update -= Step; EditorApplication.Exit(1); }
    }
    static void Check(bool value, string reason)
    { assertions++; if (!value) throw new Exception("Town lighting assertion: " + reason); }
    static Color32[] Picture(string name = null)
    {
        camera.Render(); RenderTexture.active = camera.targetTexture;
        readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); readback.Apply();
        if (name != null) File.WriteAllBytes(Path.Combine(output, name + ".png"), readback.EncodeToPNG());
        return readback.GetPixels32();
    }
    static float Difference(Color32[] first, Color32[] second)
    {
        double difference = 0; int count = 0;
        for (int i = 0; i < first.Length; i++)
        {
            if (first[i].r <= 10 || second[i].r <= 10) continue;
            difference += Math.Abs(first[i].r - second[i].r) + Math.Abs(first[i].g - second[i].g) + Math.Abs(first[i].b - second[i].b);
            count++;
        }
        return (float)(difference / Math.Max(1, count) / 3);
    }
    static float Brightness(Color32[] image) => image.Sum(p => (long)p.r + p.g + p.b) / (image.Length * 3f);
    static void Save(string name, Color32[] image)
    { readback.SetPixels32(image); readback.Apply(); File.WriteAllBytes(Path.Combine(output, name + ".png"), readback.EncodeToPNG()); }
    static Light Lamp(Vector3 position)
    {
        Light light = new GameObject("Actual practical").AddComponent<Light>();
        light.type = LightType.Point; light.renderMode = LightRenderMode.ForceVertex;
        light.range = 2.65f; light.intensity = 2.6f; light.color = new Color(1, .75f, .5f);
        light.cullingMask = 1 << VRLayers.ModLayer; light.transform.position = position;
        TownServiceLighting.ClaimPractical(light); return light;
    }
    static void Release(Light light)
    { TownServiceLighting.ForgetPractical(light); UnityEngine.Object.DestroyImmediate(light.gameObject); }
    static void CameraPose(float x, float scale, float eye = 0)
    {
        camera.nearClipPlane = .01f * scale; camera.farClipPlane = 10 * scale;
        camera.transform.position = new Vector3(x + eye, 1.35f, -2.7f) * scale;
        camera.transform.LookAt(new Vector3(x + eye, 1.05f, .1f) * scale);
    }
    static IEnumerator Execute()
    {
        Debug.Log("Town lighting fixture entering render routine");
        QualitySettings.pixelLightCount = 0;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0; RenderSettings.reflectionIntensity = 0;
        camera = new GameObject("Head camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << VRLayers.ModLayer; camera.fieldOfView = 45;
        camera.targetTexture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
        readback = new Texture2D(512, 512, TextureFormat.RGB24, false);
        var current = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TownNpc.shader");
        var old = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TownNpc548.shader");
        Check(current != null && current.isSupported && old != null && old.isSupported, "production and regression shaders compile");
        var bundle = AssetBundle.LoadFromFile(File.ReadAllText(Path.Combine(output, "bundle-path.txt")).Trim());
        Debug.Log("Town lighting fixture loaded actual bundle");
        foreach (string npc in new[] { "merchant", "priestess", "enchantress" })
        {
            var root = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>("assets/bundle/townservices/prefabs/town" + npc + ".prefab"));
            foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = VRLayers.ModLayer;
            var actor = root.transform.Find("Actor");
            var skins = new List<Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.transform.IsChildOf(actor)) renderer.enabled = false;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = new Material(materials[i]);
                    string shaderName = materials[i].shader.name;
                    if (shaderName == "GloomhavenVR/TownNpc") { materials[i].shader = current; skins.Add(materials[i]); }
                    else if (shaderName == "GloomhavenVR/TownEye" || shaderName == "GloomhavenVR/TownCornea")
                        materials[i].shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/" + shaderName.Substring(shaderName.IndexOf('/') + 1) + ".shader");
                }
                renderer.sharedMaterials = materials;
            }
            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
            var block = new MaterialPropertyBlock(); block.SetFloat("_TownVisibility", 1); block.SetFloat("_LightingUnrelated", .73f);
            skin.SetPropertyBlock(block);
            Vector3 centre = skin.bounds.center;
            var lamps = new List<Light>();
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3;
                lamps.Add(Lamp(centre + new Vector3(Mathf.Cos(angle) * .9f, .15f, Mathf.Sin(angle) * .9f)));
            }
            // These real actor bounds cross a lamp-ranking tie with only 0.1 mm of
            // movement. The camera follows identically, eliminating silhouette drift.
            foreach (bool legacy in new[] { true, false })
            {
                Debug.Log("Town lighting sequence: " + npc + " legacy=" + legacy);
                foreach (var material in skins) material.shader = legacy ? old : current;
                Color32[] previous = null, before = null, after = null; float peak = 0;
                for (int frame = 0; frame <= 40; frame++)
                {
                    float x = (frame - 20) * .0001f;
                    root.transform.position = new Vector3(x, 0, 0); CameraPose(x, 1);
                    yield return null; var pixels = Picture();
                    if (previous != null)
                    {
                        float difference = Difference(previous, pixels);
                        if (difference > peak) { peak = difference; before = previous; after = pixels; }
                    }
                    previous = pixels;
                }
                results.Add(npc + (legacy ? " legacy" : " stable") + " maximum adjacent mean RGB delta=" + peak.ToString("F5") + "/255, 0.1 mm movement");
                Debug.Log(results.Last());
                Save(npc + (legacy ? "-legacy-before" : "-stable-before"), before);
                Save(npc + (legacy ? "-legacy-after" : "-stable-after"), after);
                if (legacy) { Check(peak > 5, npc + " historical shader reproduces visible lamp-list discontinuity"); negatives++; }
                else Check(peak < .5f, npc + " physical lighting varies continuously across the same tie");
            }
            root.transform.position = Vector3.zero; CameraPose(0, 1);
            yield return null; var unit = Picture(npc + "-fixed");
            Check(Brightness(unit) > 2, npc + " actual practicals light the real body");
            var values = new List<Vector3>(); foreach (var light in lamps) { values.Add(light.transform.position); light.transform.position *= 198; light.range *= 198; }
            root.transform.localScale = Vector3.one * 198; CameraPose(0, 198);
            yield return null; var scaled = Picture(npc + "-scale198");
            Check(Difference(unit, scaled) < 1, npc + " actual game world scale preserves lighting");
            root.transform.localScale = Vector3.one;
            for (int i = 0; i < lamps.Count; i++) { lamps[i].transform.position = values[i]; lamps[i].range /= 198; }
            CameraPose(0, 1, -.032f); yield return null; var left = Picture(npc + "-left-eye");
            CameraPose(0, 1, .032f); yield return null; var right = Picture(npc + "-right-eye");
            Check(Mathf.Abs(Brightness(left) - Brightness(right)) < 1, npc + " translated stereo views retain lighting energy");
            CameraPose(0, 1);
            // A peer can create the same actual lights in a different arrival order.
            foreach (var lamp in lamps) TownServiceLighting.ForgetPractical(lamp);
            for (int i = lamps.Count - 1; i >= 0; i--) TownServiceLighting.ClaimPractical(lamps[i]);
            yield return null; var reordered = Picture();
            Check(Difference(unit, reordered) < .1f, npc + " peer arrival order does not select another light");
            skin.GetPropertyBlock(block);
            Check(Mathf.Abs(block.GetFloat("_LightingUnrelated") - .73f) < .0001f && block.GetFloat("_TownVisibility") == 1, npc + " unrelated and dissolve property blocks survive binding");
            int originalCap = QualitySettings.pixelLightCount;
            QualitySettings.pixelLightCount = 4; yield return null; var higherCap = Picture();
            Check(Difference(unit, higherCap) < .1f, npc + " local pixel-light budget does not change practical presentation");
            QualitySettings.pixelLightCount = originalCap;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var enabled = renderers.Select(renderer => renderer.enabled).ToArray();
            // Render both anatomical optical layers without the skin occluding them.
            foreach (string optical in new[] { "GloomhavenVR/TownEye", "GloomhavenVR/TownCornea" })
            {
                Bounds eyeBounds = default; bool any = false;
                foreach (var renderer in renderers)
                {
                    renderer.enabled = renderer.sharedMaterials.Any(material => material.shader.name == optical);
                    if (!renderer.enabled) continue;
                    if (!any) { eyeBounds = renderer.bounds; any = true; } else eyeBounds.Encapsulate(renderer.bounds);
                }
                Check(any, npc + " original optical layer exists: " + optical);
                camera.transform.position = eyeBounds.center + new Vector3(0,0,-.23f); camera.transform.LookAt(eyeBounds.center);
                var glint = Lamp(camera.transform.position + new Vector3(.03f,.03f,0));
                yield return null; var opticalPixels = Picture(npc + "-" + optical.Substring(optical.IndexOf('/') + 1));
                Check(opticalPixels.Count(pixel => pixel.r > 20 || pixel.g > 20 || pixel.b > 20) > 20,
                    npc + " actual globe/cornea receives physical lighting: " + optical);
                Release(glint);
            }
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = enabled[i];
            CameraPose(0, 1);
            foreach (var light in lamps) light.enabled = false;
            yield return null; var dark = Picture(npc + "-no-practicals");
            Check(Brightness(dark) < .1f, npc + " no phantom lighting or duplicate SH with lamps disabled");
            foreach (var light in lamps) Release(light);
            Check(Shader.GetGlobalInt("_TownPracticalCount") == 0, npc + " teardown clears shader count immediately");
            UnityEngine.Object.DestroyImmediate(root);
        }
        // Actual owner/visitor registration and teardown, not a copied test binder.
        var host = new GameObject("Station"); var lighting = new TownServiceLighting(host.transform, 1);
        lighting.SetFlame(new Vector3(-.6f,1.3f,0), 0); lighting.SetFlame(new Vector3(.6f,1.3f,0), 1); lighting.SetVisibility(.5f);
        TownServiceLightList.Bind(); var positions = Shader.GetGlobalVectorArray("_TownPracticalPositions");
        Check(Shader.GetGlobalInt("_TownPracticalCount") == 2 && positions[0].x == -.6f && positions[1].x == .6f, "real SetFlame publishes both actual lamp anchors");
        var colours = Shader.GetGlobalVectorArray("_TownPracticalColours");
        Check(Mathf.Abs(colours[0].x - 1.3f) < .001f, "owned visibility scales actual intensity once");
        lighting.Dispose(); UnityEngine.Object.DestroyImmediate(host); TownServiceLightList.Bind();
        Check(Shader.GetGlobalInt("_TownPracticalCount") == 0, "last station disposal clears all owned practicals");
        TownLightingEditorLifetime.Flush();
        var native = new GameObject("Native light").AddComponent<Light>(); native.intensity = .93f; native.renderMode = LightRenderMode.Auto; native.cullingMask = -1;
        var full = new List<Light>(); for (int i = 0; i < TownServiceLightList.Capacity; i++) full.Add(Lamp(new Vector3(i,1,0)));
        Light overflow = Lamp(Vector3.zero); TownServiceLighting.ClaimPractical(overflow);
        Check(VRLog.Warnings == 1, "over-capacity anomaly is bounded and deduplicated");
        TownServiceLightList.Bind(); Check(Shader.GetGlobalInt("_TownPracticalCount") == TownServiceLightList.Capacity, "maximum real lamp capacity is bounded");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) TownServiceLightList.Bind();
        timer.Stop(); results.Add("32 live lamp CPU binding average=" + (timer.Elapsed.TotalMilliseconds / 1000).ToString("F5") + " ms (Editor/GL; not headset GPU timing)");
        var allocationMethod = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread");
        if (allocationMethod != null)
        {
            var allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), allocationMethod);
            long start = allocated();
            for (int i = 0; i < 1000; i++) TownServiceLightList.Bind();
            long delta = allocated() - start;
            Check(delta == 0, "steady-state shader binding allocates no managed bytes: " + delta);
            results.Add("Managed allocation: " + delta + " bytes / 1000 full-capacity binds");
        }
        full[2].enabled = false; full[3].gameObject.SetActive(false); full[4].cullingMask = 1; full[5].intensity = 0;
        TownServiceLightList.Bind(); colours = Shader.GetGlobalVectorArray("_TownPracticalColours");
        for (int i = 2; i <= 5; i++) Check(colours[i] == Vector4.zero, "inactive/hidden/out-of-mask lights contribute zero");
        UnityEngine.Object.DestroyImmediate(full[6].gameObject); TownServiceLightList.Bind();
        Check(Shader.GetGlobalVectorArray("_TownPracticalColours")[6] == Vector4.zero, "destroyed Unity lights clear stale shader slots");
        Check(native.intensity == .93f && native.renderMode == LightRenderMode.Auto && native.cullingMask == -1, "native lights remain untouched");
        foreach (var light in full) if (light != null) Release(light); Release(overflow);
        Check(Shader.GetGlobalInt("_TownPracticalCount") == 0, "all capacity slots clear after lifecycle churn");
        File.WriteAllText(Path.Combine(output, "result.txt"), "Town lighting: " + assertions + " assertions; " + negatives + " visual regression controls\n" + string.Join("\n", results));
    }
}
