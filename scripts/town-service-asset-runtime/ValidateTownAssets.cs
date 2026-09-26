using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using GloomhavenVR.WorldUI;

// Actual scene lighting must drive NPCs, including a zero-pixel-light VR camera.
public static class ValidateTownAssets
{
    static int assertions;
    static string output;
    static Camera camera;
    static bool sourceReview;
    public static void ReviewSources()
    {
        output = Arg("-townEvidence"); Directory.CreateDirectory(output);
        sourceReview = true; routine = Run(); EditorApplication.update += Step;
    }
    static readonly Color Background = new Color(.1f, .1f, .1f);
    static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidDataException(message); }
    static string Arg(string name)
    { var args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, name) + 1]; }
    public static void RunBundle()
    {
        output = Arg("-townEvidence"); Directory.CreateDirectory(output);
        routine = Run(); EditorApplication.update += Step;
    }
    public static void BuildAndRun()
    {
        try
        {
            output = Arg("-townEvidence"); Directory.CreateDirectory(output);
            var assets = Directory.GetFiles("Assets/Bundle/TownServices/Prefabs", "*.prefab").Concat(Directory.GetFiles("Assets/Bundle/TownServices/Shaders", "*.shader")).Concat(new[] { "Assets/Bundle/TownServices/town-facial-rig-contract.json" }).OrderBy(x => x).ToArray();
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
        // The production lamp registry is separately rendered by check-town-lighting.py.
        // This isolated asset fixture has no runtime station lifecycle; register its actual
        // authored point lights in the same shader units, including the 198x-scale pass.
        var positions = new Vector4[32]; var colours = new Vector4[32]; int lampCount = 0;
        foreach (var lamp in UnityEngine.Object.FindObjectsOfType<Light>())
        {
            if (!lamp.isActiveAndEnabled || lamp.type != LightType.Point || lamp.range <= 0) continue;
            var p = lamp.transform.position;
            positions[lampCount] = new Vector4(p.x, p.y, p.z, 1f / (lamp.range * lamp.range));
            Color c = QualitySettings.activeColorSpace == ColorSpace.Linear ? lamp.color.linear : lamp.color;
            colours[lampCount++] = c * lamp.intensity;
        }
        Shader.SetGlobalVectorArray("_TownPracticalPositions", positions);
        Shader.SetGlobalVectorArray("_TownPracticalColours", colours);
        Shader.SetGlobalInt("_TownPracticalCount", lampCount);
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

    static readonly string[] FaceShapes = { "BlinkLeft", "BlinkRight", "JawOpen", "MouthWide", "MouthRound", "Smile", "BrowRaise",
        "LidUpLeft", "LidDownLeft", "LidUpRight", "LidDownRight" };
    static void FaceWeight(GameObject root, string name, float value)
    {
        foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            skin.SetBlendShapeWeight(skin.sharedMesh.GetBlendShapeIndex(name), value);
    }
    static void ResetFace(GameObject root)
    { foreach (var name in FaceShapes) FaceWeight(root, name, 0); }
    static bool EyeCentresVisible(GameObject root, Color32[] mask, Color32[] closed, out string evidence)
    {
        bool complete = true; evidence = "";
        foreach (string name in new[] { "EyeLeft", "EyeRight" })
        {
            Transform eye = root.GetComponentsInChildren<Transform>().Single(t => t.name == name);
            MeshFilter globe = eye.GetComponentsInChildren<MeshFilter>().Single(f => f.name.EndsWith("Globe"));
            float radius = globe.sharedMesh.bounds.extents.x * Mathf.Abs(globe.transform.lossyScale.x);
            Vector3 centre = eye.position + eye.forward * (radius * .9f);
            Vector3 pixel = camera.WorldToScreenPoint(centre);
            float screenRadius = Vector3.Distance(pixel, camera.WorldToScreenPoint(centre + camera.transform.right * (radius * .2f)));
            int r = Mathf.Clamp(Mathf.RoundToInt(screenRadius), 2, 12), visible = 0, closedVisible = 0, samples = 0;
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                samples++;
                int x = Mathf.RoundToInt(pixel.x) + dx, y = Mathf.RoundToInt(pixel.y) + dy;
                if (pixel.z <= 0 || x < 0 || y < 0 || x >= 800 || y >= 800) continue;
                Color32 colour = mask[y * 800 + x];
                if (colour.r > 220 && colour.b > 220 && colour.g < 20) visible++;
                colour = closed[y * 800 + x];
                if (colour.r > 220 && colour.b > 220 && colour.g < 20) closedVisible++;
            }
            // A few exposed scleral edge pixels passed the former global-area test
            // even when a stale prefab placed both actual irises behind the cheeks.
            // Require EACH physical iris to be exposed through its real lid aperture,
            // and hidden by that lid when closed. A globe protruding through the
            // cheek must fail even if its own centre happens to remain visible.
            complete &= visible >= samples * .7f && closedVisible <= samples * .1f;
            evidence += name + "=" + visible + "/" + samples + ";closed=" + closedVisible + ";";
        }
        return complete;
    }

    static IEnumerator EyePixels(GameObject root, string npc, int level, Color32[] lit)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>().Where(r => r.sharedMaterial.shader.name == "GloomhavenVR/TownEye" || r.sharedMaterial.shader.name == "GloomhavenVR/TownCornea").ToArray();
        var original = renderers.Select(r => r.sharedMaterial).ToArray();
        var maskMaterial = new Material(Shader.Find("Unlit/Color")); maskMaterial.color = Color.magenta;
        foreach (var renderer in renderers) renderer.sharedMaterial = maskMaterial;
        var mask = Picture(npc + "-face-lod" + level + "-eye-aperture-mask");
        FaceWeight(root, "BlinkLeft", 100); FaceWeight(root, "BlinkRight", 100);
        yield return null;
        var closed = Picture(npc + "-face-lod" + level + "-closed-eye-mask");
        ResetFace(root); yield return null;
        Check(EyeCentresVisible(root, mask, closed, out string centres),
            npc + " LOD" + level + " both physical iris centres are visible, not only displaced scleral slivers: " + centres);
        if (level == 0)
        {
            Transform[] eyes = new[] { "EyeLeft", "EyeRight" }.Select(name => root.GetComponentsInChildren<Transform>().Single(t => t.name == name)).ToArray();
            Vector3[] positions = eyes.Select(eye => eye.position).ToArray();
            for (int i = 0; i < eyes.Length; i++) eyes[i].position -= eyes[i].up * (.02f * Mathf.Abs(root.transform.lossyScale.x));
            yield return null;
            Color32[] displaced = Picture(npc + "-negative-displaced-eyes");
            FaceWeight(root, "BlinkLeft", 100); FaceWeight(root, "BlinkRight", 100);
            yield return null;
            var displacedClosed = Picture(npc + "-negative-displaced-eyes-closed");
            ResetFace(root); yield return null;
            bool accepted = EyeCentresVisible(root, displaced, displacedClosed, out string displacedCentres);
            for (int i = 0; i < eyes.Length; i++) eyes[i].position = positions[i];
            Check(!accepted, npc + " negative control: cheek-displaced globes must fail iris-centre visibility: " + displacedCentres);
        }
        for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = original[i];
        UnityEngine.Object.DestroyImmediate(maskMaterial);
        int visible = 0, illuminated = 0; double brightness = 0;
        for (int i = 0; i < mask.Length; i++)
            if (mask[i].r > 220 && mask[i].b > 220 && mask[i].g < 20)
            {
                visible++; int value = lit[i].r + lit[i].g + lit[i].b;
                brightness += value / 3.0; if (value > 120) illuminated++;
            }
        Check(visible > 30, npc + " LOD" + level + " actual globe aperture is visible through fitted lids");
        Check(illuminated > visible * .2 && brightness / visible > 18,
            npc + " LOD" + level + " visible eyes receive actual stand/environment light");
        File.AppendAllText(Path.Combine(output, npc + "-eye-pixels.txt"), "LOD" + level + " aperturePixels=" + visible + " litPixels=" + illuminated + " centres=" + centres + " mean=" + (brightness / visible).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "\n");
    }

    static IEnumerator FacialEvidence(GameObject root, string npc, SkinnedMeshRenderer fixedSkin)
    {
        var actor = root.transform.Find("Actor");
        var head = root.GetComponentsInChildren<Transform>().Single(t => t.name == "Head");
        var eyes = new[] { "EyeLeft", "EyeRight" }.Select(name => root.GetComponentsInChildren<Transform>().Single(t => t.name == name)).ToArray();
        foreach (var eye in eyes)
        {
            Check(eye.parent == head, npc + " eye is attached to actual animated Head");
            Check(Vector3.Dot(eye.forward, actor.forward) > .97f, npc + " neutral eye +Z optical frame follows face");
            Check(eye.GetComponentsInChildren<MeshRenderer>().Length == 2, npc + " dimensional globe and independent corneal shell");
            foreach (var renderer in eye.GetComponentsInChildren<MeshRenderer>())
                Check(renderer.sharedMaterial.shader.isSupported && renderer.sharedMaterial.HasProperty("_TownVisibility"), npc + " eye lighting and dissolve shader compiled");
        }
        Check(head.Find("MouthAudioAnchor") != null, npc + " anatomical mouth anchor retained without generated voice assets");
        foreach (var side in new[] { "L", "R" })
        {
            var hand = actor.GetComponentsInChildren<Transform>().Single(t => t.name == "Hand." + side);
            var palm = hand.Find("PalmContact." + side);
            Check(palm != null, npc + " anatomical palm marker attached to hand");
            Check(Vector3.Dot(palm.up, hand.up) > .999f && Vector3.Dot(palm.forward, hand.forward) > .999f,
                npc + " palm marker preserves finger-forward and palmar-normal frame");
            float offset = (actor.InverseTransformPoint(palm.position) - actor.InverseTransformPoint(hand.position)).magnitude;
            Check(offset > .025f && offset < .085f, npc + " palm attachment uses bind pose rather than animated FBX opening pose");
            foreach (var digit in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                var tip = actor.GetComponentsInChildren<Transform>().Single(t => t.name == digit + "Tip." + side);
                Check(tip.parent.name == digit + "3." + side, npc + " anatomical fingertip follows distal joint");
                var pad = actor.GetComponentsInChildren<Transform>().Single(t => t.name == digit + "Pad." + side);
                Check(pad.parent == tip.parent, npc + " actual palmar pad follows the same distal joint");
                float separation = (actor.InverseTransformPoint(pad.position) - actor.InverseTransformPoint(tip.position)).magnitude;
                Check(separation > .001f && separation < .035f, npc + " pad is on the distal skin rather than at a guessed wrist offset");
            }
        }
        foreach (AnimationState clip in root.GetComponentInChildren<Animation>())
            Check(!AnimationUtility.GetCurveBindings(clip.clip).Any(binding => binding.propertyName.StartsWith("blendShape.") ||
                binding.path.Split('/').Any(segment => segment.StartsWith("PalmContact.") || segment.StartsWith("PalmCentre.") ||
                    new[] { "Thumb", "Index", "Middle", "Ring", "Little" }.Any(digit => segment.StartsWith(digit + "Tip.") || segment.StartsWith(digit + "Pad.")))),
                npc + " body clips cannot overwrite facial state or reparented hand contact frames");
        var metrics = new System.Collections.Generic.List<string>();
        for (int level = 0; level < 1; level++)
        {
            var skin = fixedSkin;
            var mesh = skin.sharedMesh;
            if (level == 0) using (var writer = new BinaryWriter(File.Create(Path.Combine(output, npc + "-triangles.i32"))))
                foreach (var index in mesh.triangles) writer.Write(index);
            Check(mesh.blendShapeCount == FaceShapes.Length, npc + " LOD" + level + " all facial channels present");
            var bodyVertices = mesh.GetIndices(0).Concat(mesh.GetIndices(2)).Distinct().ToArray();
            var vertexDelta = new Vector3[mesh.vertexCount]; var normalDelta = new Vector3[mesh.vertexCount];
            var tangentDelta = new Vector3[mesh.vertexCount];
            for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
            {
                mesh.GetBlendShapeFrameVertices(shapeIndex, 0, vertexDelta, normalDelta, tangentDelta);
                Check(bodyVertices.All(index => vertexDelta[index].sqrMagnitude < 1e-14f), npc + " facial shapes never move costume vertices");
                int changedNormals = bodyVertices.Count(index => normalDelta[index].sqrMagnitude > 1e-8f);
                Check(changedNormals == 0, npc + " unchanged costume has no facial normal deltas");
            }

            foreach (var shape in FaceShapes) Check(mesh.GetBlendShapeIndex(shape) >= 0, npc + " LOD" + level + " " + shape);
            Check(actor.GetComponentsInChildren<Renderer>().Length == 5, npc + " one skinned actor plus four bounded eye passes");
            metrics.Add("LOD" + level + " vertices=" + mesh.vertexCount + " triangles=" + mesh.triangles.Length / 3 +
                " meshMemoryBytes=" + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh));
            ResetFace(root);
            camera.transform.position = new Vector3(0, 1.60f, .03f); camera.transform.LookAt(new Vector3(0, 1.60f, .65f));
            yield return null; var neutral = Picture(npc + "-face-lod" + level + "-neutral");
            var eyeReview = EyePixels(root, npc, level, neutral);
            while (eyeReview.MoveNext()) yield return eyeReview.Current;
            FaceWeight(root, "BlinkLeft", 100); FaceWeight(root, "BlinkRight", 100);
            yield return null; var blink = Picture(npc + "-face-lod" + level + "-blink");
            Check(Different(neutral, blink) > 100, npc + " LOD" + level + " actual eyelid deformation renders");
            ResetFace(root); FaceWeight(root, "JawOpen", 25); FaceWeight(root, "MouthRound", 35);
            yield return null; var speech = Picture(npc + "-face-lod" + level + "-speech");
            Check(Different(neutral, speech) > 100, npc + " LOD" + level + " small oral deformation renders");
        }
        File.WriteAllLines(Path.Combine(output, npc + "-facial-metrics.txt"), metrics.ToArray());
        ResetFace(root);
        var headRotation = head.rotation; var eyeRotations = eyes.Select(e => e.localRotation).ToArray();
        foreach (float yaw in new[] { -50f, 50f }) foreach (float pitch in new[] { -22f, 22f })
        {
            head.rotation = Quaternion.AngleAxis(yaw, actor.up) * Quaternion.AngleAxis(pitch, actor.right) * headRotation;
            for (int i = 0; i < eyes.Length; i++) eyes[i].localRotation = eyeRotations[i] * Quaternion.Euler(Mathf.Sign(pitch) * 15, Mathf.Sign(yaw) * 25, 0);
            FaceWeight(root, pitch > 0 ? "LidDownLeft" : "LidUpLeft", 100);
            FaceWeight(root, pitch > 0 ? "LidDownRight" : "LidUpRight", 100);
            camera.transform.position = new Vector3(.32f, 1.61f, .10f); camera.transform.LookAt(new Vector3(0, 1.59f, .65f));
            yield return null; Picture(npc + "-gaze-" + yaw + "-" + pitch);
            var skin = fixedSkin;
            var points = PosedVertices(skin, root.transform);
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, npc + "-gaze-" + yaw + "-" + pitch + ".xyz32"))))
                foreach (var point in points) { writer.Write(point.x); writer.Write(point.y); writer.Write(point.z); }
            Check(points.All(v => v.y <= 2.1f && v.x >= -.9f && v.x <= .9f && v.z >= -.5f && v.z <= 1.15f), npc + " gaze extremes remain inside reserved station bounds");
            ResetFace(root); FaceWeight(root, "BlinkLeft", 100); FaceWeight(root, "BlinkRight", 100);
            yield return null; Picture(npc + "-gaze-" + yaw + "-" + pitch + "-blink");
            ResetFace(root);
        }
        head.rotation = headRotation;
        for (int i = 0; i < eyes.Length; i++) eyes[i].localRotation = eyeRotations[i];
        camera.transform.position = new Vector3(.32f, 1.61f, .10f); camera.transform.LookAt(new Vector3(0, 1.59f, .65f));
        FaceWeight(root, "BlinkLeft", 100); FaceWeight(root, "BlinkRight", 100);
        yield return null; Picture(npc + "-face-blink-oblique");
        ResetFace(root); FaceWeight(root, "JawOpen", 35); FaceWeight(root, "MouthWide", 20);
        yield return null; Picture(npc + "-face-speech-oblique");
        ResetFace(root);
    }

    static IEnumerator routine;
    static List<MeshCollider> FurnitureColliders(Transform furniture)
    {
        var colliders = new List<MeshCollider>();
        foreach (var filter in furniture.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.gameObject.activeInHierarchy) continue;
            Check(filter.sharedMesh != null && filter.sharedMesh.isReadable, "Authored furniture geometry remains readable for grounding and support");
            Check(filter.sharedMesh.uv.Length == filter.sharedMesh.vertexCount, "Authored furniture retains UVs on every vertex");
            var collider = filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = filter.sharedMesh;
            colliders.Add(collider);
        }
        Physics.SyncTransforms();
        return colliders;
    }

    static float SupportHeight(List<MeshCollider> colliders, Transform frame, Vector3 point)
    {
        var origin = frame.TransformPoint(new Vector3(point.x, 2.4f, point.z));
        var ray = new Ray(origin, -frame.up); float highest = float.NegativeInfinity;
        foreach (var collider in colliders)
            if (collider.Raycast(ray, out RaycastHit hit, 3f))
                highest = Mathf.Max(highest, frame.InverseTransformPoint(hit.point).y);
        return highest;
    }

    static void ValidateSlots(GameObject root)
    {
        GloomhavenVR.WorldUI.TownServiceAssets.Current = root;
        var rack = GloomhavenVR.WorldUI.TownServiceMerchantDrawer.CreateHousingTemplate();
        var crank = GloomhavenVR.WorldUI.TownServiceMerchantDrawer.CreateTemplate(null);
        Check(crank.transform.Find("Handle") != null, "Actual cabinet materials support a physical crank factory");
        rack.transform.SetParent(root.transform, false);
        rack.transform.localPosition = new Vector3(-.95f, 1.22f, .035f);
        var colliders = FurnitureColliders(rack.transform.Find("Cassette"));
        var furniture = FurnitureColliders(root.transform.Find("Counter"));
        for (int i = 0; i < TownServiceMerchantLayout.StockCapacity; i++)
        {
            Vector3 centre = TownServiceMerchantLayout.StockPosition(i);
            foreach (float dx in new[] { -.5f, .5f }) foreach (float dy in new[] { -.5f, .5f })
            {
                Vector3 corner = centre + new Vector3(dx * TownServiceMerchantLayout.CardWidth,
                    dy * TownServiceMerchantLayout.CardHeight, 0);
                var ray = new Ray(rack.transform.TransformPoint(corner), rack.transform.forward);
                Check(colliders.Any(c => c.Raycast(ray, out RaycastHit hit, .06f)), "Every vertical card corner has an actual opaque rack behind it");
            }
        }
        // Both folded leaves and the card cassette use the production motion, evaluated
        // against the imported mesh. A state-only clock assertion cannot catch mirrored FBX.
        for (int frame = 0; frame <= 40; frame++)
        {
            float progress = frame / 40f;
            GloomhavenVR.Net.TownServices.TownCassetteMotion.Apply(rack.transform, progress);
            foreach (var mesh in rack.GetComponentsInChildren<MeshFilter>())
            foreach (var vertex in mesh.sharedMesh.vertices)
            {
                Vector3 point = root.transform.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                Check(point.x >= -1.39f && point.x <= -.51f && point.z >= -.06f && point.z <= .61f,
                    "Actual cassette and folding leaves stay within the authored cabinet envelope");
            }
        }
        GloomhavenVR.Net.TownServices.TownCassetteMotion.Apply(rack.transform, .5f);
        var shutter = FurnitureColliders(rack.transform.Find("Shutter"));
        Physics.SyncTransforms();
        for (int i = 0; i < TownServiceMerchantLayout.StockCapacity; i++)
        {
            var p = TownServiceMerchantLayout.StockPosition(i);
            foreach (float dx in new[] { -.5f, .5f }) foreach (float dy in new[] { -.5f, .5f })
            {
                var corner = p + new Vector3(dx * TownServiceMerchantLayout.CardWidth,
                    dy * TownServiceMerchantLayout.CardHeight, -.10f);
                Check(shutter.Any(c => c.Raycast(new Ray(rack.transform.TransformPoint(corner), rack.transform.forward),
                    out RaycastHit hit, .15f)), "Closed opaque shutter covers every card corner before page replacement");
            }
        }
        foreach (var collider in shutter) UnityEngine.Object.DestroyImmediate(collider);
        GloomhavenVR.Net.TownServices.TownCassetteMotion.Apply(rack.transform, 1f);
        // The static cabinet must be on the same side as its runtime cassette anchors.
        Check(SupportHeight(furniture, root.transform, new Vector3(-.95f, 0f, .30f)) > 1.5f,
            "Asymmetric cabinet FBX imports on the agreed left side of the merchant");
        // The native hanging lantern is not part of this bundle, but its measured visible
        // half-width and production seat are. Test that complete footprint against the
        // complete authored cabinet, including the tall side/rear panels. The regression
        // used only the ledger/worktop as its mental model and moved the lamp into the box.
        // The complete side cheek spans the full cabinet height/depth, rather than only
        // the ledger/worktop considered by the regression. Using its authored imported
        // envelope also avoids treating the intentionally touching hanging link as an
        // obstruction or collapsing the cabinet's non-convex gaps into one giant AABB.
        var completeSide = new Bounds(new Vector3(-1.355f, 1.22f, .45f), new Vector3(.04f, .84f, .88f));
        Check(TownServiceMerchantDrawer.MerchantLanternClears(
                completeSide, TownServiceMerchantDrawer.MerchantLanternSeat()),
            "Merchant hanging lantern clears the complete imported cabinet side and rear envelope");
        Check(!TownServiceMerchantDrawer.MerchantLanternClears(
                completeSide, new Vector3(-1.34f, 1.11f, .11f)),
            "Lantern-clearance instrument rejects the regressed build-565 seat inside the cabinet wall");
        foreach (var collider in colliders) UnityEngine.Object.DestroyImmediate(collider);
        foreach (var collider in furniture) UnityEngine.Object.DestroyImmediate(collider);
        UnityEngine.Object.DestroyImmediate(rack); UnityEngine.Object.DestroyImmediate(crank);
        GloomhavenVR.WorldUI.TownServiceAssets.Current = null;
    }

    static void ValidateFurniture(GameObject root, string npc)
    {
        var furniture = root.transform.Find(npc == "merchant" ? "Counter" : npc == "priestess" ? "Shrine" : "Workbench");
        Check(furniture != null, npc + " has its authored furniture root");
        var nodes = furniture.GetComponentsInChildren<Transform>(true);
        Check(!nodes.Any(n => n.name.IndexOf("drawer", StringComparison.OrdinalIgnoreCase) >= 0), npc + " has no obsolete drawer banks");
        var meshes = furniture.GetComponentsInChildren<MeshFilter>(true).Where(f => f.gameObject.activeInHierarchy).ToArray();
        var fixedMeshes = meshes.Where(mesh => !mesh.name.StartsWith("GroundSupport", StringComparison.Ordinal)).ToArray();
        Check(fixedMeshes.Length >= 2 && fixedMeshes.Length <= 6, npc + " fixed furniture is grouped by material rather than one renderer per ornament");
        Check(meshes.Length > fixedMeshes.Length && meshes.Length - fixedMeshes.Length <= 20,
            npc + " only bounded physical floor supports retain independent grounding transforms");
        Check(meshes.Sum(f => f.sharedMesh.triangles.Length / 3) > 1000, npc + " curved joinery and relief are actual geometry");
        Check(nodes.All(n => n.GetComponents<Component>().All(c => c == null || c.GetType().Name != "Canvas")), npc + " furniture contains no baked gameplay UI");
        foreach (var renderer in furniture.GetComponentsInChildren<MeshRenderer>(true))
        foreach (var material in renderer.sharedMaterials)
            Check(material != null && material.shader != null && material.HasProperty("_TownVisibility"), npc + " every authored detail uses the lit dissolving town material");
        if (npc != "merchant") return;
        ValidateSlots(root);
        var template = furniture.Find("CounterReturn");
        Check(template != null && !template.gameObject.activeSelf, "Open return template exists without drawing unused stock wings");
        Check(TownServiceMerchantLayout.StockCapacity == 12, "Visible stock is bounded per mechanical cassette");
        var surfaces = FurnitureColliders(furniture);
        foreach (float x in new[] { -.20f, 0f, .20f })
            Check(Mathf.Abs(SupportHeight(surfaces, furniture, new Vector3(x, 0, .18f)) - .958f) < .012f,
                "Merchant ledger and coin workspace remains supported behind stock terraces");
        foreach (var collider in surfaces) UnityEngine.Object.DestroyImmediate(collider);
    }
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
        var bundle = sourceReview ? null : AssetBundle.LoadFromFile(Path.Combine(output, "town-review.bundle"));
        Check(sourceReview ? AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Bundle/TownServices" }).Length == 0 : bundle.LoadAllAssets<AudioClip>().Length == 0 && !bundle.GetAllAssetNames().Any(n => n.Contains("/audio/")), "No generated voice assets ship in the town bundle");
        var flameShader = sourceReview ? AssetDatabase.LoadAssetAtPath<Shader>("Assets/Bundle/TownServices/Shaders/TownFlame.shader")
            : bundle.LoadAllAssets<Shader>().SingleOrDefault(shader => shader.name == "GloomhavenVR/TownFlame");
        Check(flameShader != null, "Actual town bundle includes the native-prop billboard shader");
        var flameContract = new Material(flameShader);
        Check(String.Equals(flameContract.GetTag("DisableBatching", false, ""), "True", StringComparison.OrdinalIgnoreCase), "Bundled billboard preserves per-object transforms under dynamic batching");
        UnityEngine.Object.DestroyImmediate(flameContract);
        var oldShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/OldTownShader.shader");
        Check(oldShader != null && oldShader.isSupported, "Historical self-lighting control compiles on GL");
        foreach (var npc in new[] { "merchant", "priestess", "enchantress" })
        {
            var root = UnityEngine.Object.Instantiate(sourceReview ? AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Bundle/TownServices/Prefabs/Town" + Char.ToUpperInvariant(npc[0]) + npc.Substring(1) + ".prefab") : bundle.LoadAsset<GameObject>(
                "assets/bundle/townservices/prefabs/town" + npc + ".prefab"));
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            camera.transform.position = new Vector3(0, 1.6f, -2.3f);
            camera.transform.LookAt(new Vector3(0, .9f, .1f));
            ValidateFurniture(root, npc);
            Check(root.GetComponentInChildren<LODGroup>() == null, npc + " has no view-dependent LOD switching");
            var fixedSkin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            Check(fixedSkin.name.StartsWith("LOD0_"), npc + " ships only the original high-detail actor surface");
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
            var overviewPosition = camera.transform.position; var overviewRotation = camera.transform.rotation;
            camera.transform.position = new Vector3(0f, 1.8f, -4.5f);
            camera.transform.LookAt(new Vector3(0f, .90f, -.30f));
            yield return null; Picture(npc + "-stand-wide");
            camera.transform.position = overviewPosition; camera.transform.rotation = overviewRotation;
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
            foreach (var view in new[] { "front", "three-quarter", "profile", "back", "above", "below", "back-three-quarter" })
            {
                var centre = new Vector3(0, 1.59f, .65f);
                var offset = view == "front" ? new Vector3(0, 0, -.61f) :
                    view == "three-quarter" ? new Vector3(.36f, 0, -.49f) :
                    view == "profile" ? new Vector3(.61f, 0, 0) :
                    view == "above" ? new Vector3(.12f, .55f, -.35f) :
                    view == "below" ? new Vector3(0, -.4f, -.6f) :
                    view == "back-three-quarter" ? new Vector3(.43f, .05f, .43f) : new Vector3(0, 0, .61f);
                camera.transform.position = centre + offset; camera.transform.LookAt(centre);
                yield return null; Picture(npc + "-head-" + view);
            }
            var faceReview = FacialEvidence(root, npc, fixedSkin);
            while (faceReview.MoveNext()) yield return faceReview.Current;
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
            var skin = fixedSkin;
            Check(skin.sharedMaterials.Length == 3 && skin.sharedMaterials[1].mainTexture.width == 4096 && skin.sharedMaterials[2].mainTexture.width == 2048,
                npc + " separate 4K head and shared2K anatomical hand atlases retain one skinned renderer");
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
            // Preserve the projection depth ratio with the scaled scene. Leaving the
            // preceding close-up near plane at 0.01 creates artificial lining z-fighting.
            float nearPlane = camera.nearClipPlane, farPlane = camera.farClipPlane;
            camera.transform.position *= 198; camera.nearClipPlane *= 198; camera.farClipPlane *= 198;
            yield return null;
            var scaled = Picture(npc + "-scale198");
            Check(Bright(scaled, true) > body * .9f, npc + " actor remains visible at equivalent map projection");
            // Production TickClipPlanes caps near at 0.5 and far/near at 50,000.
            // Exercise that worst allowed depth ratio as well, not only scaled projection.
            camera.nearClipPlane = Mathf.Clamp(.05f * 198, .01f, .5f);
            camera.farClipPlane = camera.nearClipPlane * 50000;
            yield return null;
            var nativeScaled = Picture(npc + "-scale198-native-clips");
            Check(Bright(nativeScaled, true) > body * .9f, npc + " actor remains visible with native capped map clip planes");
            root.transform.localScale = Vector3.one; camera.transform.position /= 198;
            camera.nearClipPlane = nearPlane; camera.farClipPlane = farPlane;
            light.transform.position /= 198; light.range /= 198;
            // Re-introduce the old bad culling mechanism as a visual negative control;
            // the shipped actor itself has no LODGroup or distance-dependent surface.
            var lod = root.transform.Find("Actor").gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.5f, root.transform.Find("Actor").GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds(); lod.size *= .01f;
            yield return null;
            var culled = Picture(npc + "-negative-lod");
            Check(Bright(culled, true) < body / 20, npc + " negative control: old tiny LOD removes actor");
            UnityEngine.Object.DestroyImmediate(lod);
            foreach (var renderer in root.transform.Find("Actor").GetComponentsInChildren<Renderer>()) renderer.enabled = true;
            UnityEngine.Object.DestroyImmediate(light.gameObject);
            RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                foreach (var material in renderer.materials) material.shader = oldShader;
            yield return null;
            var dark = Picture(npc + "-negative-lighting");
            Check(Bright(dark, true) > 10000, npc + " negative control: old studio light makes skin glow in darkness");
            // Open carved stands occupy fewer pixels than the obsolete solid box fronts.
            // Anchor the control to this mesh's measured lit footprint, retaining the same
            // minimum visible-surface floor used by the production lighting assertion.
            Check(Bright(dark, false) > 1000 && Bright(dark, false) >= furniture * .8f,
                npc + " negative control: old studio light makes furniture glow in darkness");
            Debug.Log("TOWN_ASSET_PIXELS " + npc + " body=" + body + " furniture=" + furniture + " fixedDetail=true");
            UnityEngine.Object.DestroyImmediate(root);
        }
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
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS " + assertions + " assertions; 9 visual negative controls\n");
        Debug.Log("TOWN_ASSET_VALIDATION_PASS assertions=" + assertions + " negativeControls=9");
    }
}
