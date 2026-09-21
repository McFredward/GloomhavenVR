using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Actual scene lighting must drive NPCs, including a zero-pixel-light VR camera.
public static class ValidateTownAssets
{
    static int assertions;
    static string output;
    static Camera camera;
    static readonly Color Background = new Color(.1f, .1f, .1f);
    static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidDataException(message); }
    static string Arg(string name)
    { var args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, name) + 1]; }
    public static void BuildAndRun()
    {
        try
        {
            output = Arg("-townEvidence"); Directory.CreateDirectory(output);
            var assets = Directory.GetFiles("Assets/Bundle/TownServices/Prefabs", "*.prefab").OrderBy(x => x).ToArray();
            // Linux pixel evidence needs Linux shader bytecode; the shipping Windows bundle
            // is built separately from these identical prefabs, materials and shader sources.
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] { new AssetBundleBuild {
                assetBundleName = "town-review.bundle", assetNames = assets } }, BuildAssetBundleOptions.None, BuildTarget.StandaloneLinux64);
            Check(manifest != null, "Review bundle built");
            routine = Run(); EditorApplication.update += Step;
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static Color32[] Picture(string name)
    {
        camera.Render(); RenderTexture.active = camera.targetTexture;
        var texture = new Texture2D(800, 800, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 800, 800), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG());
        var pixels = texture.GetPixels32(); UnityEngine.Object.DestroyImmediate(texture); return pixels;
    }
    static int Bright(Color32[] pixels, bool actor)
    {
        int count = 0;
        // Above the furniture: only the actor can contribute pixels in this camera.
        for (int y = actor ? 440 : 110; y < (actor ? 660 : 370); y++)
            for (int x = actor ? 270 : 150; x < (actor ? 530 : 650); x++)
            { var p = pixels[y * 800 + x]; if (p.r + p.g + p.b > 120) count++; }
        return count;
    }
    static int RetainedBright(Color32[] reference, Color32[] current, bool actor)
    {
        int count = 0;
        for (int y = actor ? 440 : 110; y < (actor ? 660 : 370); y++)
            for (int x = actor ? 270 : 150; x < (actor ? 530 : 650); x++)
            {
                var a = reference[y * 800 + x]; var b = current[y * 800 + x];
                if (a.r + a.g + a.b > 120 && b.r + b.g + b.b > 120) count++;
            }
        return count;
    }
    static int Different(Color32[] a, Color32[] b)
    {
        int count = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 12) count++;
        return count;
    }
    static void Visibility(GameObject root, float visibility)
    {
        var block = new MaterialPropertyBlock(); block.SetFloat("_TownVisibility", visibility);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>()) renderer.SetPropertyBlock(block);
    }
    static void Sample(GameObject root, string clip, float time)
    {
        var animation = root.GetComponentInChildren<Animation>();
        animation.Stop(); var state = animation[clip]; state.enabled = true;
        state.weight = 1; state.time = time; animation.Sample(); state.enabled = false;
    }
        static Vector3[] PosedVertices(SkinnedMeshRenderer renderer, Transform reference)
        {
            var mesh = renderer.sharedMesh; var vertices = mesh.vertices; var weights = mesh.boneWeights;
            var bind = mesh.bindposes; var bones = renderer.bones;
            var matrices = bones.Select((bone, i) => reference.worldToLocalMatrix * bone.localToWorldMatrix * bind[i]).ToArray();
            var result = new Vector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var w = weights[i]; var v = vertices[i];
                result[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 +
                    matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                    matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 +
                    matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            }
            return result;
        }

    static IEnumerator routine;
    static void Step()
    {
        try { if (!routine.MoveNext()) { EditorApplication.update -= Step; EditorApplication.Exit(0); } }
        catch (Exception error) { Debug.LogException(error); EditorApplication.update -= Step; EditorApplication.Exit(1); }
    }
    static IEnumerator Run()
    {
        QualitySettings.pixelLightCount = 0;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0; RenderSettings.reflectionIntensity = 0;
        Check(UnityEngine.Object.FindObjectsOfType<Light>().Length == 0, "No scene lights mask the defect");
        camera = new GameObject("Map head camera").AddComponent<Camera>();
        camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Background; camera.fieldOfView = 60;
        camera.targetTexture = new RenderTexture(800, 800, 24);
        var bundle = AssetBundle.LoadFromFile(Path.Combine(output, "town-review.bundle"));
        var oldShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/OldTownShader.shader");
        Check(oldShader != null && oldShader.isSupported, "Historical self-lighting control compiles on GL");
        foreach (var npc in new[] { "merchant", "priestess", "enchantress" })
        {
            var root = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(
                "assets/bundle/townservices/prefabs/town" + npc + ".prefab"));
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            camera.transform.position = new Vector3(0, 1.6f, -2.3f);
            camera.transform.LookAt(new Vector3(0, .9f, .1f));
            if (npc == "merchant")
            {
                var counter = root.transform.Find("Counter");
                var planks = Enumerable.Range(0, 4).Select(i => counter.Find("SurfacePlank" + i).GetComponent<Renderer>().bounds).ToArray();
                var top = planks[0]; foreach (var plank in planks) top.Encapsulate(plank);
                Check(top.size.x >= 1.64f && top.size.z >= .79f, "Merchant top contains catalogue and tray footprint");
                Check(counter.Find("Coin0") == null && counter.Find("LedgerCover") == null && counter.Find("LeatherMat") == null,
                    "Native decoration and physical cards replace primitive tabletop props");
            }
            var lod = root.GetComponentInChildren<LODGroup>();
            Check(lod.size > 1.7f && lod.size < 2.2f, npc + " human-sized LOD envelope");
            Check(lod.GetLODs().Length == 3, npc + " retains automatic three-level LOD");
            Check(root.transform.Find("GroundAnchor") != null && root.transform.Find("LightAnchor") != null,
                npc + " explicit floor and physical light anchors");
            Sample(root, "Idle", 0);
            yield return null;
            var unlit = Picture(npc + "-unlit");
            Check(Bright(unlit, true) < 100, npc + " no self-lit skin in a black environment");
            var light = new GameObject("Physical stand light").AddComponent<Light>();
            light.type = LightType.Point; light.renderMode = LightRenderMode.ForceVertex;
            light.cullingMask = 1 << 31; light.range = 5; light.intensity = 3;
            light.color = new Color(1, .85f, .65f); light.transform.position = new Vector3(-.6f, 1.65f, -.5f);
            RenderSettings.ambientLight = new Color(.16f, .18f, .23f);
            RenderSettings.ambientIntensity = 1;
            yield return null;
            var full = Picture(npc + "-full");
            int body = Bright(full, true), furniture = Bright(full, false);
            Check(body > 10000, npc + " auto-LOD actor visible and textured with stand lighting");
            Check(furniture > 1000, npc + " furniture visible and textured with stand lighting");
            Check(!full.Any(p => p.r > 240 && p.b > 240 && p.g < 10), npc + " no unsupported shader magenta");
            Visibility(root, .5f); yield return null; var half = Picture(npc + "-half");
            Visibility(root, 0); yield return null; var hidden = Picture(npc + "-hidden");
            Check(Bright(hidden, true) == 0 && Bright(hidden, false) == 0, npc + " fully hidden at zero visibility");
            Check(RetainedBright(full, half, true) > 0 && RetainedBright(full, half, true) < body, npc + " intermediate actor dissolve rendered");
            Check(RetainedBright(full, half, false) > 0 && RetainedBright(full, half, false) < furniture, npc + " intermediate furniture dissolve rendered");
            Visibility(root, 1); Sample(root, "Greeting", 1.2f);
            yield return null;
            var greeting = Picture(npc + "-greeting");
            Check(Different(full, greeting) > 500, npc + " greeting changes actual rendered pixels");
            Sample(root, "Idle", 0);
            var cameraPosition = camera.transform.position;
            var cameraRotation = camera.transform.rotation;
            float cameraFov = camera.fieldOfView;
            camera.nearClipPlane = .01f; camera.fieldOfView = 38;
            foreach (var view in new[] { "front", "three-quarter", "profile", "back" })
            {
                var centre = new Vector3(0, 1.59f, .65f);
                var offset = view == "front" ? new Vector3(0, 0, -.61f) :
                    view == "three-quarter" ? new Vector3(.36f, 0, -.49f) :
                    view == "profile" ? new Vector3(.61f, 0, 0) : new Vector3(0, 0, .61f);
                camera.transform.position = centre + offset; camera.transform.LookAt(centre);
                yield return null; Picture(npc + "-head-" + view);
            }
            camera.transform.position = cameraPosition; camera.transform.rotation = cameraRotation;
            camera.fieldOfView = cameraFov;
            var actorRoot = root.transform.Find("Actor");
            var actorBase = actorRoot.localPosition;
            actorRoot.localPosition += Vector3.up * .05f;
            foreach (AnimationState state in root.GetComponentInChildren<Animation>())
            {
                Sample(root, state.name, state.length * .5f);
                Check((actorRoot.localPosition - actorBase - Vector3.up * .05f).sqrMagnitude < 1e-10f,
                    npc + " " + state.name + " preserves runtime terrain correction");
            }
            actorRoot.localPosition = actorBase;
            var envelope = new Bounds(); bool hasEnvelope = false;
            var skin = lod.GetLODs()[0].renderers.OfType<SkinnedMeshRenderer>().Single();
            Check(skin.sharedMaterials.Length == 2 && skin.sharedMaterials[1].mainTexture.width == 4096,
                npc + " separate head atlas retains 4K without additional skinned renderers");
            Sample(root, "Idle", 0);
            var bakedControl = new Mesh(); skin.BakeMesh(bakedControl, true);
            var cpuControl = PosedVertices(skin, root.transform);
            var bakedControlVertices = bakedControl.vertices;
            float poseError = 0;
            for (int i = 0; i < cpuControl.Length; i += 97)
                poseError = Mathf.Max(poseError, (cpuControl[i] - root.transform.InverseTransformPoint(skin.transform.TransformPoint(bakedControlVertices[i]))).magnitude);
            Check(poseError < .0001f, npc + " CPU skinning agrees with explicitly scaled Unity bake");
            UnityEngine.Object.DestroyImmediate(bakedControl);
            foreach (AnimationState state in root.GetComponentInChildren<Animation>())
                for (float t = 0; t <= state.length + .05f; t += .1f)
                {
                    Sample(root, state.name, Mathf.Min(t, state.length));
                    var posed = PosedVertices(skin, root.transform);
                    if (t < .05f || Mathf.Abs(t-state.length*.5f) < .051f || t >= state.length-.05f)
                        using (var points = new BinaryWriter(File.Create(Path.Combine(output, npc + "-" + state.name + "-" + t.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ".xyz32"))))
                            foreach (var point in posed) { points.Write(point.x); points.Write(point.y); points.Write(point.z); }
                    foreach (var local in posed)
                    {
                        if (!hasEnvelope) { envelope = new Bounds(local, Vector3.zero); hasEnvelope = true; }
                        else envelope.Encapsulate(local);
                    }
                }
            Check(envelope.size.y > 1.6f && envelope.size.y < 2.1f, npc + " CPU skinning uses real metre units");
            Check(envelope.min.x >= -.9f && envelope.max.x <= .9f && envelope.min.z >= -.5f && envelope.max.z <= 1.15f, npc + " all animation phases remain in placement envelope");
            Debug.Log("TOWN_ACTOR_ENVELOPE " + npc + " min=" + envelope.min.ToString("F4") + " max=" + envelope.max.ToString("F4"));
            Sample(root, "Idle", 0);
            // Native handoff runs around 198 world units per metre in the supplied map log.
            root.transform.localScale = Vector3.one * 198;
            light.transform.position *= 198; light.range *= 198;
            camera.transform.position *= 198; camera.farClipPlane = 2000;
            yield return null;
            var scaled = Picture(npc + "-scale198");
            Check(Bright(scaled, true) > body * .9f, npc + " actor remains visible at map scale");
            root.transform.localScale = Vector3.one; camera.transform.position /= 198;
            light.transform.position /= 198; light.range /= 198;
            float size = lod.size; lod.size *= .01f;
            yield return null;
            var culled = Picture(npc + "-negative-lod");
            Check(Bright(culled, true) < body / 20, npc + " negative control: old tiny LOD removes actor");
            lod.size = size;
            UnityEngine.Object.DestroyImmediate(light.gameObject);
            RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                foreach (var material in renderer.materials) material.shader = oldShader;
            yield return null;
            var dark = Picture(npc + "-negative-lighting");
            Check(Bright(dark, true) > 10000, npc + " negative control: old studio light makes skin glow in darkness");
            Check(Bright(dark, false) > 10000, npc + " negative control: old studio light makes furniture glow in darkness");
            Debug.Log("TOWN_ASSET_PIXELS " + npc + " body=" + body + " furniture=" + furniture + " lod=" + size);
            UnityEngine.Object.DestroyImmediate(root);
        }
        var flameShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Bundle/TownServices/Shaders/TownFlame.shader");
        Check(flameShader != null && flameShader.isSupported, "Native flame presentation shader compiles");
        var flame = GameObject.CreatePrimitive(PrimitiveType.Quad); flame.layer = 31;
        flame.transform.position = new Vector3(0, 1, 0); flame.transform.localScale = new Vector3(.4f, .6f, 1);
        var flameMaterial = new Material(flameShader); flame.GetComponent<Renderer>().sharedMaterial = flameMaterial;
        flameMaterial.SetFloat("_Billboard", 1); flameMaterial.SetFloat("_Toggle_Flipbook", 1);
        flameMaterial.SetFloat("_FlipbookTileX", 2); flameMaterial.SetFloat("_FlipbookTileY", 2);
        var frames = new Texture2D(2, 2); frames.filterMode = FilterMode.Point;
        frames.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.yellow }); frames.Apply();
        flameMaterial.mainTexture = frames;
        camera.transform.position = new Vector3(0, 1, -2); camera.transform.LookAt(flame.transform);
        yield return null; var frame0 = Picture("flame-front-frame0");
        flameMaterial.SetFloat("_TownAnimationTime", .26f);
        yield return null; var frame1 = Picture("flame-front-frame1");
        Check(Different(frame0, frame1) > 1000, "Shared clock advances original flame atlas frames");
        camera.transform.position = new Vector3(2, 1, 0); camera.transform.LookAt(flame.transform);
        yield return null; var side = Picture("flame-side");
        Check(Different(frame1, side) < 1000, "Verified XY flame quad remains visible edge-on with shared-eye billboard");
        flameMaterial.SetFloat("_TownVisibility", 0);
        yield return null; var absent = Picture("flame-hidden");
        Check(Different(side, absent) > 1000, "Flame obeys station dissolve visibility");
        UnityEngine.Object.DestroyImmediate(flame);
        UnityEngine.Object.DestroyImmediate(frames); UnityEngine.Object.DestroyImmediate(flameMaterial);
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS " + assertions + " assertions; 6 visual negative controls\n");
        Debug.Log("TOWN_ASSET_VALIDATION_PASS assertions=" + assertions + " negativeControls=6");
    }
}
