using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using Object = UnityEngine.Object;

public static class InteractionProgram
{
    public static int Checks;
    public static string Metrics = "";
    private static string Arg(string key)
    { string[] args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, key) + 1]; }
    private static void Check(bool condition, string label)
    { Checks++; if (!condition) throw new Exception(label); }
    private static void Near(float actual, float expected, float tolerance, string label)
    { Check(Mathf.Abs(actual - expected) <= tolerance, label + " actual=" + actual + " expected=" + expected); }
    private sealed class NativeFigure
    {
        internal GameObject Root = null!;
        internal Animator Animator = null!;
        internal SkinnedMeshRenderer[] Skins = null!;
        internal WorldspacePanelUIController Controller = null!;
    }
    private static NativeFigure Spawn(GameObject prefab, Animator authored, AnimatorController controller)
    {
        GameObject root = FigureVisualMirror.CloneVisual(prefab, Vector3.zero, Quaternion.identity, Vector3.one, out FigureVisualMirror mirror);
        Object.DestroyImmediate(mirror); // independent actual native animation, never mirror-sync a prefab pose
        Transform animated = root.transform.Find(AnimationUtility.CalculateTransformPath(authored.transform, prefab.transform));
        Animator animator = animated.gameObject.AddComponent<Animator>(); animator.avatar = authored.avatar;
        animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
        var input = new GameObject("NativeBarInputs").AddComponent<WorldspacePanelUIController>();
        input.m_ObjectToTrack = root; input.m_BasePoint = root.transform;
        input.m_HeadBonePoint = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "C_headSkel01_JNT")
            ?? root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s => s.bones).First(t => t != null && t.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0);
        input.m_HeadBaseOffset = input.m_HeadBonePoint.position - root.transform.position;
        root.AddComponent<ActorBehaviour>();
        return new NativeFigure { Root = root, Animator = animator, Controller = input,
            Skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh.vertexCount > 0).ToArray() };
    }
    private static float BodyTop(NativeFigure figure, Mesh scratch, List<Vector3> vertices)
    {
        float top = float.NegativeInfinity;
        foreach (SkinnedMeshRenderer skin in figure.Skins)
        {
            skin.BakeMesh(scratch); scratch.GetVertices(vertices);
            foreach (Vector3 vertex in vertices) top = Mathf.Max(top, skin.transform.TransformPoint(vertex).y);
        }
        return top;
    }
    // Independent reference uses original readable Editor bind vertices, NOT the production
    // default BakeMesh/inverse recovery. Native meshes themselves have imported read/write OFF.
    private static float BindEnvelopeTop(NativeFigure figure)
    {
        float top = float.NegativeInfinity;
        foreach (SkinnedMeshRenderer skin in figure.Skins)
        {
            Mesh mesh = skin.sharedMesh; Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights; Matrix4x4[] bind = mesh.bindposes;
            var boxes = new Bounds[bind.Length]; var used = new bool[bind.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                BoneWeight w = weights[i];
                foreach (int index in new[] { w.weight0 > 0 ? w.boneIndex0 : -1, w.weight1 > 0 ? w.boneIndex1 : -1,
                    w.weight2 > 0 ? w.boneIndex2 : -1, w.weight3 > 0 ? w.boneIndex3 : -1 })
                {
                    if (index < 0) continue; Vector3 local = bind[index].MultiplyPoint3x4(vertices[i]);
                    if (used[index]) boxes[index].Encapsulate(local);
                    else { boxes[index] = new Bounds(local, Vector3.zero); used[index] = true; }
                }
            }
            for (int i = 0; i < boxes.Length; i++)
            {
                if (!used[i]) continue; Bounds box = boxes[i];
                foreach (float x in new[] { box.min.x, box.max.x }) foreach (float y in new[] { box.min.y, box.max.y })
                    foreach (float z in new[] { box.min.z, box.max.z })
                        top = Mathf.Max(top, skin.bones[i].TransformPoint(new Vector3(x, y, z)).y);
            }
        }
        return top;
    }
    private static int Count(string field)
    {
        object cache = typeof(ActorBarPose).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return (int)cache.GetType().GetProperty("Count")!.GetValue(cache)!;
    }
    private static void Pixels(Camera camera, RenderTexture target, Texture2D texture, string path, out int gap)
    {
        camera.Render(); RenderTexture.active = target;
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); RenderTexture.active = null;
        int bodyTop = -1, barBottom = target.height;
        Color32[] pixels = texture.GetPixels32();
        for (int y = 0; y < target.height; y++) for (int x = 0; x < target.width; x++)
        {
            Color32 color = pixels[y * target.width + x];
            if (color.r > 160 && color.g > 160 && color.b > 160) bodyTop = Math.Max(bodyTop, y);
            if (color.r > 160 && color.g < 70 && color.b < 70) barBottom = Math.Min(barBottom, y);
        }
        Check(bodyTop >= 0 && barBottom < target.height, "native Drake and lower bar edge produce visible pixels");
        gap = barBottom - bodyTop;
        Check(gap >= 2, "rendered health band lowest edge clears native Drake skin; gap=" + gap);
        if (path.Length != 0) File.WriteAllBytes(path, texture.EncodeToPNG());
    }
    public static IEnumerator Run()
    {
        string evidence = Arg("-evidenceRoot"); string name = typeof(InteractionProgram).Assembly.GetName().Name!;
        ActorBarPose.Reset(); ScenarioFigureDetailBudget.Instance.Reset();
        AssetBundle bundle = AssetBundle.LoadFromFile(Arg("-nativeDrakeBundle"));
        GameObject prefab = bundle.LoadAsset<GameObject>("Assets/Content/Characters/Monsters/MO_SpittingDrake/MO_SpittingDrake_PR.prefab");
        Animator authored = prefab.GetComponentsInChildren<Animator>(true).First(a => a.runtimeAnimatorController != null && a.runtimeAnimatorController.name == "SpittingDrake_Controller");
        File.WriteAllLines(Path.Combine(evidence, name + "-native-clips.txt"), authored.runtimeAnimatorController.animationClips.Select(c => c.name + " loop=" + c.isLooping + " length=" + c.length));
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/" + name + ".controller");
        foreach (string pose in new[] { "Sleeping", "Flying", "Wake" })
        {
            AnimatorState state = controller.layers[0].stateMachine.AddState(pose);
            state.motion = authored.runtimeAnimatorController.animationClips.Single(c => c.name == (pose == "Sleeping" ? "Spitting_Drake_Sleeping_Idle_v001" : pose == "Wake" ? "Spitting_Drake_Sleeping_WakeUp_v001" : "Spitting_Flying_Idle_v001"));
        }
        NativeFigure figure = Spawn(prefab, authored, controller);
        figure.Animator.Play("Base Layer.Sleeping", 0, 0.37f); figure.Animator.Update(0.0001f); yield return null;
        Check(figure.Skins.All(s => !s.sharedMesh.isReadable), "actual native skin imported read/write OFF");
        var scratch = new Mesh(); var vertices = new List<Vector3>();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        ActorBars.Adopted bar = ActorBars.Start(figure.Controller); timer.Stop();
        double prepareMs = timer.Elapsed.TotalMilliseconds;
        Check(bar.Pose != null && bar.Pose.BoneCount > 100, "actual native Drake envelope captured");
        float expected = BindEnvelopeTop(figure); bar.Pose!.TryCurrentTop(out float measured);
        Near(measured, expected, 0.003f, "default BakeMesh recovered original native bind vertices");
        timer.Restart(); for (int i = 0; i < 2000; i++) bar.Pose.TryCurrentTop(out measured); timer.Stop();
        double steadyMicroseconds = timer.Elapsed.TotalMilliseconds * 1000 / 2000;
        int prepared = PerfMonitor.Preparations, profiles = Count("Profiles");
        int minimumGap = int.MaxValue; float maxSurplus = 0, sleepAnchor = 0, flightAnchor = 0;

        // Native skin geometry is rendered by real Unity. A deliberately plain red health band
        // marks the production bar pose, with a conservative 0.46wu thickness. The original
        // WorldspaceUIPrefab health/effect geometry reaches about0.226wu below its root at the
        // user's609 figure-follow scale (~0.00523wu/px); the0.23wu lower edge covers it.
        // This isolates
        // body/bar overlap from native font/sprite availability; native gameplay UI is untouched.
        var white = new Material(Shader.Find("Unlit/Color")); white.color = Color.white;
        foreach (SkinnedMeshRenderer skin in figure.Skins) skin.sharedMaterials = Enumerable.Repeat(white, skin.sharedMaterials.Length).ToArray();
        GameObject band = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(band.GetComponent<Collider>());
        var red = new Material(Shader.Find("Unlit/Color")); red.color = Color.red; band.GetComponent<Renderer>().sharedMaterial = red;
        band.transform.localScale = new Vector3(1.3f, 0.46f, 1f);
        var camera = new GameObject("NativeDrakeProofCamera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black; camera.orthographic = true; camera.orthographicSize = 2.15f;
        camera.transform.position = new Vector3(0, 1.85f, -7); camera.transform.rotation = Quaternion.identity;
        var target = new RenderTexture(512, 512, 24); target.Create(); camera.targetTexture = target;
        var texture = new Texture2D(512, 512, TextureFormat.RGB24, false);
        using var csv = new StreamWriter(Path.Combine(evidence, name + "-poses.csv"));
        csv.WriteLine("pose,phase,skinTop,envelopeTop,anchorY,lowestBandY,pixelGap");
        foreach (string pose in new[] { "Sleeping", "Flying" }) for (int phase = 0; phase < 20; phase++)
        {
            figure.Animator.Play("Base Layer." + pose, 0, phase / 20f); figure.Animator.Update(0.0001f); yield return null;
            ActorBars.Tick(bar, figure.Controller, 100 + phase);
            Check(bar.Pose != null, "live native skin envelope remains available");
            Check(bar.Pose!.TryCurrentTop(out measured), "live native skin envelope remains available");
            float bodyTop = BodyTop(figure, scratch, vertices), anchor = ActorBars.Position(bar, figure.Controller).y;
            Check(measured + 0.002f >= bodyTop, "live cached envelope encloses evaluated skin");
            float surplus = measured - bodyTop; maxSurplus = Mathf.Max(maxSurplus, surplus);
            Check(surplus <= (pose == "Sleeping" ? 0.15f : 0.22f), "native skin bound tightness avoids sleeping-too-high floor");
            Check(anchor - 0.23f > bodyTop, "health band lowest edge clears actual evaluated pose on every frame");
            if (pose == "Sleeping") { sleepAnchor = anchor; Check(anchor < 1.50f, "sleeping bar lowers below authored flight floor"); }
            else flightAnchor = Mathf.Max(flightAnchor, anchor);
            band.transform.position = new Vector3(0, anchor, -1.5f);
            string path = phase == 10 && name.EndsWith("production", StringComparison.Ordinal) ? Path.Combine(evidence, pose.ToLowerInvariant() + ".png") : "";
            Pixels(camera, target, texture, path, out int gap); minimumGap = Math.Min(minimumGap, gap);
            csv.WriteLine(pose + "," + phase / 20f + "," + bodyTop + "," + measured + "," + anchor + "," + (anchor - 0.23f) + "," + gap);
        }
        Check(PerfMonitor.Preparations == prepared && Count("Profiles") == profiles && ActorBars.LegacyMeasures == 0,
            "steady native posing performs no preparation or legacy geometry census");
        Check(flightAnchor > 3f, "flying wings raise bar above obsolete head-only policy");

        // The real native flap is a cyclic body deformation, not a sequence of taller/lower
        // creatures. Choose its LOW pose first, then independently visit the entire authored
        // animation on the live original to prove that Capture already knows the cycle peak.
        // This cannot pass by a timer or a smooth filter that still oscillates every loop.
        float minimumPhase = 0, minimumNative = float.PositiveInfinity, maximumNative = float.NegativeInfinity;
        for (int phase = 0; phase < 96; phase++)
        {
            figure.Animator.Play("Base Layer.Flying", 0, phase / 96f); figure.Animator.Update(0.0001f);
            float height = BindEnvelopeTop(figure);
            if (height < minimumNative) { minimumNative = height; minimumPhase = phase / 96f; }
            maximumNative = Mathf.Max(maximumNative, height);
        }
        figure.Animator.Play("Base Layer.Flying", 0, minimumPhase); figure.Animator.Update(0.0001f);
        Transform[] nativeTransforms = figure.Root.GetComponentsInChildren<Transform>(true);
        Quaternion[] originalRotations = nativeTransforms.Select(t => t.localRotation).ToArray();
        ActorBars.Adopted loopBar = ActorBars.Start(figure.Controller);
        Check(loopBar.Pose != null && loopBar.Pose.LoopCount >= 2, "original flying and sleeping cycles prepared");
        Check(nativeTransforms.Select((t, i) => Quaternion.Angle(t.localRotation, originalRotations[i])).All(a => a < 0.0001f),
            "cycle sampling never changes the original native skeleton");
        loopBar.Pose!.TryTop(out float firstCycleTop);
        Check(firstCycleTop + 0.003f >= maximumNative, "complete native loop peak known before first flap; actual=" + firstCycleTop + " expected=" + maximumNative + " low=" + minimumNative + " stateLoop=" + figure.Animator.GetCurrentAnimatorStateInfo(0).loop);
        float stableY = ActorBars.Position(loopBar, figure.Controller).y;
        for (int repeat = 0; repeat < 3; repeat++) for (int phase = 0; phase < 96; phase++)
        {
            figure.Animator.Play("Base Layer.Flying", 0, phase / 96f); figure.Animator.Update(0.0001f);
            ActorBars.Tick(loopBar, figure.Controller, 160 + repeat * 2f + phase / 96f);
            Near(ActorBars.Position(loopBar, figure.Controller).y, stableY, 0.003f,
                "animated head cannot bob world-space cycle anchor");
            loopBar.Pose.TryTop(out float cycleTop);
            Check(cycleTop + 0.003f >= BindEnvelopeTop(figure), "fixed loop ceiling encloses every authored wing phase");
        }
        // A Frame cadence verifies the conservative full-loop ceiling, not health state. Exact
        // native callbacks/animations never wait for this optional body-matrix read. The clock is
        // real Unity Time; this same-frame phase sweep deliberately keeps the verification gate
        // closed while independently evaluating the original native skin at every phase.
        PerfConfig.ActorBarPoseCheckInterval = 0.1f;
        ActorBars.Adopted sparseBar = ActorBars.Start(figure.Controller);
        float sparseY = ActorBars.Position(sparseBar, figure.Controller).y;
        int sparseBefore = sparseBar.Pose!.VerificationCount;
        for (int phase = 0; phase < 96; phase++)
        {
            figure.Animator.Play("Base Layer.Flying", 0, phase / 96f); figure.Animator.Update(0.0001f);
            ActorBars.Tick(sparseBar, figure.Controller, 165 + phase / 96f);
            Near(ActorBars.Position(sparseBar, figure.Controller).y, sparseY, 0.003f,
                "optional cadence preserves every native wing-phase rendered anchor");
            sparseBar.Pose.TryTop(out float sparseTop);
            Check(sparseTop + 0.003f >= BindEnvelopeTop(figure), "optional cadence retains full-cycle conservative ceiling");
        }
        Check(sparseBar.Pose.SkippedVerificationCount >= 96 && sparseBar.Pose.VerificationCount - sparseBefore <= 1,
            "Frame loop cadence actually suppresses repeated original bone matrix walks");
        timer.Restart(); for (int i = 0; i < 2000; i++) sparseBar.Pose.TryTop(out _); timer.Stop();
        double sparseMicroseconds = timer.Elapsed.TotalMilliseconds * 1000 / 2000;
        // Native actions must invalidate the gate in the same frame, even with a long remaining
        // cadence window. Use the original standup clip and then a genuinely different loop.
        figure.Animator.Play("Base Layer.Wake", 0, 0.1f); figure.Animator.Update(0.0001f);
        sparseBar.Pose.TryTop(out float sparseWakeTop);
        Near(sparseWakeTop, BindEnvelopeTop(figure), 0.003f, "non-loop native action immediately bypasses optional cadence");
        figure.Animator.Play("Base Layer.Sleeping", 0, 0.4f); figure.Animator.Update(0.0001f);
        sparseBar.Pose.TryTop(out float sparseSleepTop);
        Check(sparseSleepTop < 1.25f, "new native loop immediately releases earlier optional cadence ceiling");
        NativeFigure written = Spawn(prefab, authored, controller);
        written.Animator.Play("Base Layer.Flying", 0, 0.3f); written.Animator.Update(0.0001f);
        written.Controller.m_HeadBonePoint.gameObject.AddComponent<UnknownPoseWriter>();
        ActorBars.Adopted proceduralBar = ActorBars.Start(written.Controller);
        int proceduralBefore = proceduralBar.Pose!.VerificationCount;
        for (int phase = 0; phase < 20; phase++)
        {
            written.Animator.Play("Base Layer.Flying", 0, phase / 20f); written.Animator.Update(0.0001f);
            written.Controller.m_HeadBonePoint.localPosition += Vector3.up * 10f;
            proceduralBar.Pose.TryTop(out float proceduralTop);
            Check(proceduralTop + 0.003f >= BindEnvelopeTop(written),
                "unknown procedural bone deformation always gets an immediate safe ceiling");
        }
        Check(proceduralBar.Pose.VerificationCount - proceduralBefore == 20 && proceduralBar.Pose.SkippedVerificationCount == 0,
            "procedural bone writer must stay perframe despite optional cadence");
        Object.DestroyImmediate(written.Root); Object.DestroyImmediate(written.Controller.gameObject);
        PerfConfig.ActorBarPoseCheckInterval = 0f;
        figure.Animator.Play("Base Layer.Flying", 0, 0.4f); figure.Animator.Update(0.0001f);
        // Board/world translation follows the creature, never the earlier world's peak.
        figure.Root.transform.position += Vector3.up * 1.4f;
        ActorBars.Tick(loopBar, figure.Controller, 170);
        Near(ActorBars.Position(loopBar, figure.Controller).y, stableY + 1.4f, 0.003f,
            "loop ceiling follows a relocated root immediately");
        figure.Root.transform.position -= Vector3.up * 1.4f;
        figure.Animator.Play("Base Layer.Sleeping", 0, 0.4f); figure.Animator.Update(0.0001f);
        loopBar.Pose.TryTop(out float changedLoopTop);
        Check(changedLoopTop < 1.25f, "native sleep state releases prior flight cycle peak immediately");
        figure.Animator.Play("Base Layer.Flying", 0, 0.4f); figure.Animator.Update(0.0001f);
        // The original bar used by transition tests stays at its final flying state.

        // Real native transition blend, then abrupt waking: downward changes are smooth, upward
        // clearance applies in the current evaluated frame rather than waiting for a timer.
        figure.Animator.CrossFade("Base Layer.Sleeping", 0.3f, 0, 0);
        float previous = ActorBars.Position(bar, figure.Controller).y;
        for (int step = 0; step < 45; step++)
        {
            figure.Animator.Update(1f / 60f); yield return null; ActorBars.Tick(bar, figure.Controller, 200 + step);
            float anchor = ActorBars.Position(bar, figure.Controller).y, bodyTop = BodyTop(figure, scratch, vertices);
            Check(anchor - 0.23f > bodyTop, "native flying-to-sleep transition keeps rendered lower band clear");
            band.transform.position = new Vector3(0, anchor, -1.5f);
            Pixels(camera, target, texture, "", out int transitionGap); minimumGap = Math.Min(minimumGap, transitionGap);
            if (step > 20) Check(anchor < previous + 0.015f, "lowering follows sleeping native pose in both directions");
            previous = anchor;
        }
        Check(previous < 1.50f, "sleep transition releases lifetime high-water latch");

        // The actual native waking clip must keep changing the bar's height, unlike a cyclic
        // flap. This exercises the semantic-state distinction rather than synthetic movement.
        figure.Animator.Play("Base Layer.Sleeping", 0, 0); figure.Animator.Update(0.0001f);
        ActorBars.Adopted wakingBar = ActorBars.Start(figure.Controller);
        float wakeLo = float.PositiveInfinity, wakeHi = float.NegativeInfinity;
        for (int phase = 0; phase <= 90; phase++)
        {
            figure.Animator.Play("Base Layer.Wake", 0, phase / 91f); figure.Animator.Update(0.0001f);
            ActorBars.Tick(wakingBar, figure.Controller, 260 + phase / 90f);
            float y = ActorBars.Position(wakingBar, figure.Controller).y;
            wakeLo = Mathf.Min(wakeLo, y); wakeHi = Mathf.Max(wakeHi, y);
            Check(y - 0.23f > BodyTop(figure, scratch, vertices), "actual native waking clip clears each evaluated body phase");
        }
        Check(wakeHi - wakeLo > 1f, "non-loop native standup retains meaningful dynamic height");
        Near(ActorBarPose.Follow(3f, 1f, 0.1f), 1f + 2f * Mathf.Exp(-0.8f), 0.00001f, "lowering uses bounded smoothing");
        figure.Animator.Play("Base Layer.Flying", 0, 0.3f); figure.Animator.Update(0.0001f); yield return null;
        ActorBars.Tick(bar, figure.Controller, 300);
        Check(ActorBars.Position(bar, figure.Controller).y - 0.23f > BodyTop(figure, scratch, vertices), "abrupt waking clears wings immediately");

        // Reset and capture at a different evaluated pose under rotated, translated and nonuniform
        // scaled ancestry. Recovered profile must agree with original native bind data, then remain
        // reusable for another pooled actor whose matrices are different.
        foreach (SkinQuality quality in new[] { SkinQuality.Bone1, SkinQuality.Bone2, SkinQuality.Bone4 })
        {
            ActorBarPose.Reset();
            Check(Count("Profiles") == 0, "reset releases profile meshes before another native hierarchy");
            var parent = new GameObject("RotatedScaledNativeParent").transform;
            parent.SetPositionAndRotation(new Vector3(1.2f, -0.7f, 0.4f), Quaternion.Euler(13, 51, -9));
            parent.localScale = new Vector3(1.4f, 0.8f, 1.1f);
            figure.Root.transform.SetParent(parent, false); figure.Root.transform.localRotation = Quaternion.Euler(-7, 28, 11);
            figure.Root.transform.localScale = new Vector3(0.9f, 1.2f, 1.05f);
            foreach (SkinnedMeshRenderer skin in figure.Skins) skin.quality = quality;
            figure.Animator.Play("Base Layer." + (quality == SkinQuality.Bone2 ? "Flying" : "Sleeping"), 0, 0.62f);
            figure.Animator.Update(0.0001f); yield return null;
            ActorBarPose transformed = ActorBarPose.Capture(figure.Root, figure.Controller.m_HeadBonePoint)!;
            Check(transformed != null && transformed.TryCurrentTop(out measured), "scaled native body envelope prepared");
            Near(measured, BindEnvelopeTop(figure), 0.004f, "default BakeMesh preserves rotated scaled child ancestry and native SkinQuality");
            Check(measured + 0.004f >= BodyTop(figure, scratch, vertices), "all native skin quality settings remain enclosed");
            NativeFigure remote = Spawn(prefab, authored, controller); remote.Root.transform.position = new Vector3(-1, 2, 0);
            remote.Root.transform.rotation = Quaternion.Euler(0, 103, -14); remote.Root.transform.localScale = new Vector3(0.7f, 1.7f, 1.1f);
            remote.Animator.Play("Base Layer.Flying", 0, 0.84f); remote.Animator.Update(0.0001f); yield return null;
            int reusable = Count("Profiles"); ActorBarPose reused = ActorBarPose.Capture(remote.Root, remote.Controller.m_HeadBonePoint)!;
            Check(reused.TryCurrentTop(out measured) && Count("Profiles") == reusable, "native profile reused across pooled actors without pose cache");
            Near(measured, BindEnvelopeTop(remote), 0.004f, "reused profile uses remote actor live bone transforms");
            Object.DestroyImmediate(remote.Root); Object.DestroyImmediate(remote.Controller.gameObject);
            figure.Root.transform.SetParent(null, false); figure.Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); figure.Root.transform.localScale = Vector3.one;
            Object.DestroyImmediate(parent.gameObject);
        }
        foreach (SkinnedMeshRenderer skin in figure.Skins) skin.quality = SkinQuality.Bone4;

        // Runtime private copies explicitly discard CPU vertex access. Editor imported assets
        // can remain inspectable despite isReadable=false; this control does not rely on that.
        ActorBarPose.Reset(); float uploadedReference = BindEnvelopeTop(figure);
        var originals = figure.Skins.Select(skin => skin.sharedMesh).ToArray();
        var uploadedCopies = originals.Select(mesh => Object.Instantiate(mesh)).ToArray();
        for (int i = 0; i < figure.Skins.Length; i++)
        {
            uploadedCopies[i].UploadMeshData(true); figure.Skins[i].sharedMesh = uploadedCopies[i];
            Check(!uploadedCopies[i].isReadable && uploadedCopies[i].boneWeights.Length == uploadedCopies[i].vertexCount
                && uploadedCopies[i].bindposes.Length > 0, "nonreadable runtime native copy retains skin metadata");
        }
        ActorBarPose uploadedPose = ActorBarPose.Capture(figure.Root, figure.Controller.m_HeadBonePoint)!;
        Check(uploadedPose != null && uploadedPose.TryCurrentTop(out measured), "nonreadable runtime native copy supports production private bake recovery");
        Near(measured, uploadedReference, 0.003f, "nonreadable runtime copy agrees with original native bind reference");
        for (int i = 0; i < figure.Skins.Length; i++) { figure.Skins[i].sharedMesh = originals[i]; Object.DestroyImmediate(uploadedCopies[i]); }

        // Actual production mesh records and actual offline derivative bank at detail0 vs100.
        // A defect returning Current must expose the coarser profile and fail this equivalence.
        ActorBarPose.Reset();
        var records = new List<ScenarioFigureMeshBank.Record>();
        foreach (SkinnedMeshRenderer skin in figure.Skins)
        {
            var record = new ScenarioFigureMeshBank.Record { Renderer = skin, Original = skin.sharedMesh };
            records.Add(record); ScenarioFigureDetailBudget.Instance.Own(record);
            ScenarioFigureMeshBank.Prepare(record.Original, 0);
        }
        ActorBarPose full = ActorBarPose.Capture(figure.Root, figure.Controller.m_HeadBonePoint)!; full.TryCurrentTop(out float fullTop);
        foreach (var record in records) record.Apply(0);
        Check(records.Any(r => r.UsesDerivative && r.Current!.vertexCount < r.Original.vertexCount), "actual native derivative bank reduces detail0 geometry");
        ActorBarPose.Reset(); ActorBarPose reduced = ActorBarPose.Capture(figure.Root, figure.Controller.m_HeadBonePoint)!; reduced.TryCurrentTop(out float reducedTop);
        Near(reducedTop, fullTop, 0.0001f, "detail0 vs100 native envelope uses exact original source");
        foreach (var record in records) record.Restore();
        var replacement = Object.Instantiate(records[0].Original); ((SkinnedMeshRenderer)records[0].Renderer).sharedMesh = replacement;
        Check(ScenarioFigureDetailBudget.OriginalMeshFor(records[0].Renderer) == null, "foreign native replacement is not an owned original source");
        ((SkinnedMeshRenderer)records[0].Renderer).sharedMesh = records[0].Original; Object.DestroyImmediate(replacement);

        // Native-named ghost child is excluded by exact mirror ownership, not a prefix. Its
        // source body lives elsewhere, and its huge translated pose must never raise this bar.
        ActorBarPose.Reset(); ActorBarPose clean = ActorBarPose.Capture(figure.Root, figure.Controller.m_HeadBonePoint)!; clean.TryCurrentTop(out float cleanTop);
        GameObject ghost = FigureVisualMirror.CloneVisual(figure.Root, Vector3.up * 100, Quaternion.identity, Vector3.one, out FigureVisualMirror ghostOwner);
        ghost.transform.SetParent(figure.Root.transform, true); ghost.name = "NativeActorName";
        // Alias its root reference to the tracked native head to make skeleton admission alone
        // insufficient. Exact mirror ownership must reject the entire visual, even then.
        foreach (SkinnedMeshRenderer skin in ghost.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            skin.rootBone = figure.Controller.m_HeadBonePoint;
        ActorBarPose withGhost = ActorBarPose.Capture(figure.Root, figure.Controller.m_HeadBonePoint)!; withGhost.TryCurrentTop(out measured);
        Near(measured, cleanTop, 0.0001f, "native-named visual ghost excluded by exact mirror owner");
        Object.DestroyImmediate(ghost);

        var rigidEffect = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(rigidEffect.GetComponent<Collider>()); rigidEffect.name = "TemporaryNativeAbilityMesh";
        rigidEffect.transform.SetParent(figure.Root.transform, false); rigidEffect.transform.localPosition = Vector3.up * 50;
        rigidEffect.transform.localScale = Vector3.one * 20;
        ActorBarPose noEffect = ActorBarPose.Capture(figure.Root, figure.Controller.m_HeadBonePoint)!;
        Check(noEffect != null && noEffect.TryCurrentTop(out measured), "temporary rigid native action effect does not poison body");
        Near(measured, cleanTop, 0.0001f, "temporary large rigid native action effect never raises body envelope");
        Object.DestroyImmediate(rigidEffect);

        // A supplemental particle/cloth skin may be unsupported without poisoning valid body.
        var fx = new GameObject("NativeIdleFX"); fx.transform.SetParent(figure.Root.transform, false); fx.AddComponent<ParticleSystem>();
        var shell = new GameObject("UnknownAnimatedShell").AddComponent<SkinnedMeshRenderer>(); shell.transform.SetParent(fx.transform, false);
        var unsupported = new Mesh(); unsupported.vertices = new[] { Vector3.zero }; shell.sharedMesh = unsupported;
        shell.bones = figure.Skins[0].bones;
        Check(ActorBarPose.Capture(figure.Root, figure.Controller.m_HeadBonePoint) != null, "unsupported particle skin cannot poison valid native body");
        Object.DestroyImmediate(fx); Object.DestroyImmediate(unsupported);

        var foreignSkeleton = new GameObject("SeparateNativeEffectSkeleton").transform;
        foreignSkeleton.SetParent(figure.Root.transform, false);
        var foreignShell = foreignSkeleton.gameObject.AddComponent<SkinnedMeshRenderer>();
        var shellMesh = new Mesh(); shellMesh.vertices = new[] { Vector3.up * 100 };
        foreignShell.sharedMesh = shellMesh; foreignShell.rootBone = foreignSkeleton; foreignShell.bones = new[] { foreignSkeleton };
        Check(ActorBarPose.Capture(figure.Root, figure.Controller.m_HeadBonePoint) != null,
            "unsupported separate native FX skeleton cannot poison tracked head body");
        Object.DestroyImmediate(foreignSkeleton.gameObject); Object.DestroyImmediate(shellMesh);

        // Async body and refused/singular preparation have bounded scheduled retries. A huge
        // unsupported real skin cannot turn 1000 gameplay ticks into per-frame preparation.
        var pending = new GameObject("NativeActorAwaitingBody"); var head = new GameObject("NativeHead").transform;
        head.SetParent(pending.transform, false); var waiting = new GameObject("WaitingBar").AddComponent<WorldspacePanelUIController>();
        waiting.m_ObjectToTrack = pending; waiting.m_HeadBonePoint = head; waiting.m_BasePoint = pending.transform;
        ActorBars.Adopted waitBar = ActorBars.Start(waiting); int before = PerfMonitor.Preparations;
        for (int i = 0; i < 1000; i++) ActorBars.Tick(waitBar, waiting, i / 90f);
        Check(PerfMonitor.Preparations - before == 8 && waitBar.AnchorSamplesLeft == 0, "missing body retries bounded and never per-frame");
        NativeFigure arriving = Spawn(prefab, authored, controller); arriving.Animator.Play("Base Layer.Sleeping", 0, 0); arriving.Animator.Update(0.0001f); yield return null;
        arriving.Root.transform.SetParent(pending.transform, false);
        waiting.m_HeadBonePoint = arriving.Controller.m_HeadBonePoint;
        waitBar = ActorBars.Start(waiting);
        Check(waitBar.Pose != null, "asynchronous native body can be adopted when it arrives");

        // A prepared native skeleton leaving this pooled wrapper invalidates only that bar's
        // preparation. It schedules a bounded retry, never a scan/bake on the invalidating frame.
        ActorBars.Adopted live = ActorBars.Start(arriving.Controller); before = PerfMonitor.Preparations;
        Transform detached = arriving.Controller.m_HeadBonePoint;
        Transform originalParent = detached.parent; detached.SetParent(null, true);
        ActorBars.Tick(live, arriving.Controller, 500);
        Check(live.Pose == null && live.AnchorSamplesLeft == 8 && PerfMonitor.Preparations == before,
            "reparented native skeleton triggers bounded deferred preparation");
        for (int i = 0; i < 1000; i++) ActorBars.Tick(live, arriving.Controller, 500 + i / 90f);
        Check(PerfMonitor.Preparations - before == 8 && live.AnchorSamplesLeft == 0,
            "invalid native skeleton retries cannot become per-frame census");
        detached.SetParent(originalParent, true);

        // Preparation with a transient singular hierarchy must not poison the mesh profile for
        // later healthy native actors. Actual native vertices/bones exercise the inverse refusal.
        ActorBarPose.Reset(); var singularParent = new GameObject("TransientSingularNativeParent").transform;
        singularParent.localScale = new Vector3(1, 0, 1); arriving.Root.transform.SetParent(singularParent, false);
        Check(ActorBarPose.Capture(arriving.Root, arriving.Controller.m_HeadBonePoint) == null && Count("Refused") == 0,
            "transient singular native skin refuses without poisoning shared original mesh");
        arriving.Root.transform.SetParent(pending.transform, false); Object.DestroyImmediate(singularParent.gameObject);
        Check(ActorBarPose.Capture(arriving.Root, arriving.Controller.m_HeadBonePoint) != null,
            "restored non-singular native skin can prepare shared original mesh");

        ActorBarPose.Reset(); Transform[] intact = arriving.Skins[0].bones;
        Transform[] partial = (Transform[])intact.Clone();
        int missingIndex = Array.FindIndex(partial, bone => bone != arriving.Controller.m_HeadBonePoint);
        partial[missingIndex] = null!; arriving.Skins[0].bones = partial;
        Check(ActorBarPose.Capture(arriving.Root, arriving.Controller.m_HeadBonePoint) == null && Count("Refused") == 0,
            "partial native skeleton refuses without poisoning shared original mesh");
        arriving.Skins[0].bones = intact;
        Check(ActorBarPose.Capture(arriving.Root, arriving.Controller.m_HeadBonePoint) != null,
            "completed native skeleton prepares after partial arrival");
        Mesh arrivingSource = arriving.Skins[0].sharedMesh; arriving.Skins[0].sharedMesh = null!;
        Check(ActorBarPose.Capture(arriving.Root, arriving.Controller.m_HeadBonePoint) == null,
            "partial native body source refuses misleading lower envelope");
        arriving.Skins[0].sharedMesh = arrivingSource;

        // A pooled controller can retarget while the first native actor remains completely
        // alive. Cached root/head identity must not mistake that body for the new actor.
        NativeFigure retarget = Spawn(prefab, authored, controller);
        retarget.Animator.Play("Base Layer.Flying", 0, 0.72f); retarget.Animator.Update(0.0001f); yield return null;
        live = ActorBars.Start(arriving.Controller); before = PerfMonitor.Preparations;
        arriving.Controller.m_ObjectToTrack = retarget.Root;
        arriving.Controller.m_HeadBonePoint = retarget.Controller.m_HeadBonePoint;
        arriving.Controller.m_BasePoint = retarget.Controller.m_BasePoint;
        ActorBars.Tick(live, arriving.Controller, 600);
        Check(live.Pose == null && PerfMonitor.Preparations == before && arriving.Root.activeInHierarchy,
            "pooled controller retarget invalidates old body while it stays alive");
        ActorBars.Tick(live, arriving.Controller, 600.3f);
        Check(live.Pose != null && live.Pose.TryCurrentTop(out measured), "retargeted native controller prepares new live body");
        Near(measured, BindEnvelopeTop(retarget), 0.003f, "retargeted envelope uses new native actor bones");
        arriving.Controller.m_ObjectToTrack = arriving.Root;
        arriving.Controller.m_HeadBonePoint = arriving.Root.GetComponentsInChildren<Transform>(true).First(t => t.name == "C_headSkel01_JNT");
        arriving.Controller.m_BasePoint = arriving.Root.transform;
        Object.DestroyImmediate(retarget.Root); Object.DestroyImmediate(retarget.Controller.gameObject);

        // Native source and palette may change within the same still-live pooled root. Foreign
        // artwork is its own source; an owned quality derivative remains the original's source.
        live = ActorBars.Start(arriving.Controller); before = PerfMonitor.Preparations;
        SkinnedMeshRenderer replacedSkin = arriving.Skins[0]; Mesh nativeOriginal = replacedSkin.sharedMesh;
        var taller = new Mesh { name = "NativeBodyGeometryReplacement" };
        taller.vertices = nativeOriginal.vertices.Select(vertex => vertex * 1.8f).ToArray();
        taller.boneWeights = nativeOriginal.boneWeights; taller.bindposes = nativeOriginal.bindposes;
        taller.subMeshCount = nativeOriginal.subMeshCount;
        for (int sub = 0; sub < taller.subMeshCount; sub++) taller.SetTriangles(nativeOriginal.GetTriangles(sub), sub);
        taller.RecalculateBounds(); replacedSkin.sharedMesh = taller;
        ActorBars.Tick(live, arriving.Controller, 700);
        Check(live.Pose == null && PerfMonitor.Preparations == before,
            "same-root native mesh replacement invalidates cached body source");
        ActorBars.Tick(live, arriving.Controller, 700.3f);
        Check(live.Pose != null && live.Pose.TryCurrentTop(out measured), "same-root replacement source prepares exact new geometry");
        Near(measured, BindEnvelopeTop(arriving), 0.004f, "replacement native geometry uses its own bind envelope");
        replacedSkin.sharedMesh = nativeOriginal; Object.DestroyImmediate(taller);

        live = ActorBars.Start(arriving.Controller); before = PerfMonitor.Preparations;
        Transform[] oldPalette = replacedSkin.bones, newPalette = (Transform[])oldPalette.Clone();
        int replacedIndex = nativeOriginal.boneWeights.GroupBy(weight => weight.boneIndex0)
            .OrderByDescending(group => group.Count()).First(group => oldPalette[group.Key] != arriving.Controller.m_HeadBonePoint).Key;
        Transform replacementBone = new GameObject("NativeBonePaletteReplacement").transform;
        replacementBone.SetParent(oldPalette[replacedIndex].parent, false);
        replacementBone.SetPositionAndRotation(oldPalette[replacedIndex].position + Vector3.up * 0.5f, oldPalette[replacedIndex].rotation);
        replacementBone.localScale = oldPalette[replacedIndex].localScale; newPalette[replacedIndex] = replacementBone;
        replacedSkin.bones = newPalette; live.NextAnchorSample = 801;
        ActorBars.Tick(live, arriving.Controller, 800);
        Check(live.Pose != null && PerfMonitor.Preparations == before, "palette copies use slow cadence, never every gameplay frame");
        ActorBars.Tick(live, arriving.Controller, 801);
        Check(live.Pose == null && PerfMonitor.Preparations == before,
            "same-root native bone palette replacement invalidates old cached bones");
        ActorBars.Tick(live, arriving.Controller, 801.3f);
        Check(live.Pose != null && live.Pose.TryCurrentTop(out measured), "replacement palette prepares with existing original profile");
        Near(measured, BindEnvelopeTop(arriving), 0.004f, "replacement palette uses new native bone transforms");
        replacedSkin.bones = oldPalette; Object.DestroyImmediate(replacementBone.gameObject);

        Transform savedBase = arriving.Controller.m_BasePoint;
        arriving.Controller.m_BasePoint = null!; arriving.Controller.m_PointToTrackOnActor = WorldspaceDisplayPanelBase.PoinToTrack.HeadBoneStatic;
        live = ActorBars.Start(arriving.Controller); before = PerfMonitor.Preparations;
        for (int i = 0; i < 1000; i++) ActorBars.Tick(live, arriving.Controller, i / 90f);
        Check(live.Pose == null && live.AnchorSamplesLeft == 0 && PerfMonitor.Preparations - before == 8,
            "partial native tracking controller cannot repeatedly re-arm preparation");
        arriving.Controller.m_BasePoint = savedBase;

        // Native track modes, explicit user offset and huge legitimate figure scaling remain live.
        figure.Controller.m_PointToTrackOnActor = WorldspaceDisplayPanelBase.PoinToTrack.Base;
        bar = ActorBars.Start(figure.Controller); float baseline = ActorBars.Position(bar, figure.Controller).y;
        ActorBars.BarHeightOffsetWU = 0.7f; ActorBars.Tick(bar, figure.Controller, 400);
        Near(ActorBars.Position(bar, figure.Controller).y, baseline + 0.7f, 0.001f, "player height offset applies after native envelope");
        ActorBars.BarHeightOffsetWU = 0; figure.Controller.m_PointToTrackOnActor = WorldspaceDisplayPanelBase.PoinToTrack.HeadBoneStatic;
        bar = ActorBars.Start(figure.Controller); Near(ActorBars.Position(bar, figure.Controller).y, baseline, 0.001f, "native base and static head tracking preserve world anchor");
        figure.Animator.Play("Base Layer.Flying", 0, 0.45f); figure.Animator.Update(0.0001f); yield return null;
        figure.Controller.m_HeadBaseOffset = figure.Controller.m_HeadBonePoint.position - figure.Controller.m_BasePoint.position;
        figure.Animator.Play("Base Layer.Sleeping", 0, 0.45f); figure.Animator.Update(0.0001f); yield return null;
        bar = ActorBars.Start(figure.Controller);
        Check(ActorBars.Position(bar, figure.Controller).y < 1.50f && bar.AnchorOffsetWU < 0,
            "flight-captured static head point never floors sleeping bar");
        band.transform.position = new Vector3(0, ActorBars.Position(bar, figure.Controller).y, -1.5f);
        Pixels(camera, target, texture, "", out int staticHeadGap); minimumGap = Math.Min(minimumGap, staticHeadGap);
        float signedAnchor = ActorBars.Position(bar, figure.Controller).y;
        foreach (var mode in new[] { WorldspaceDisplayPanelBase.PoinToTrack.Base, WorldspaceDisplayPanelBase.PoinToTrack.HeadBone, WorldspaceDisplayPanelBase.PoinToTrack.HeadBoneStatic })
        {
            figure.Controller.m_PointToTrackOnActor = mode; bar = ActorBars.Start(figure.Controller);
            Near(ActorBars.Position(bar, figure.Controller).y, signedAnchor, 0.001f, "all actual native track modes preserve evaluated sleeping placement");
        }
        Check(ActorBarPose.Offset(9, 0, 0, 0) > 9, "large legitimate body cannot be clipped by legacy ceiling");
        Near(ActorBarPose.Offset(9, 0, 0, 0.7f) - ActorBarPose.Offset(9, 0, 0, 0), 0.7f, 0.0001f,
            "positive player offset survives legitimate large native body");
        Check(ActorBarPose.Offset(1, 0, 0, -2) == 0, "explicit negative player offset retains floor override");
        ActorPropBody.Attached = figure.Root.GetComponent<ActorBehaviour>();
        Check(ActorBars.Start(figure.Controller).Pose == null, "attached prop keeps original authored body arch policy"); ActorPropBody.Attached = null;
        figure.Controller.m_HeadBonePoint = null!; Check(ActorBars.Start(figure.Controller).Pose == null, "headless actor retains legacy policy");
        Check(typeof(ActorBarPose).GetField("Baker", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) is SkinnedMeshRenderer baker
            && baker.sharedMesh == null && baker.bones.Length == 0 && baker.rootBone == null,
            "private baker releases original mesh and native bone references after preparation");
        ActorBarPose.Reset();
        Check(Count("Profiles") == 0 && Count("Refused") == 0 && typeof(ActorBarPose).GetField("Baker", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) == null,
            "reset releases profile meshes and diagnostic native references");
        AssetBundle otherBundle = AssetBundle.LoadFromFile(Arg("-nativeOtherBundle"));
        GameObject otherPrefab = otherBundle.LoadAllAssets<GameObject>().First(g => g.GetComponentsInChildren<Animator>(true).Any(a => a.runtimeAnimatorController != null));
        Animator otherAuthored = otherPrefab.GetComponentsInChildren<Animator>(true).First(a => a.runtimeAnimatorController != null);
        AnimationClip otherIdle = otherAuthored.runtimeAnimatorController.animationClips.First(c => c.isLooping && c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0);
        AnimatorController otherController = AnimatorController.CreateAnimatorControllerAtPath("Assets/" + name + "Other.controller");
        AnimatorState otherState = otherController.layers[0].stateMachine.AddState("Idle"); otherState.motion = otherIdle;
        NativeFigure other = Spawn(otherPrefab, otherAuthored, otherController);
        other.Animator.Play("Base Layer.Idle", 0, 0); other.Animator.Update(0.0001f);
        ActorBars.Adopted otherBar = ActorBars.Start(other.Controller);
        Check(otherBar.Pose != null && otherBar.Pose.LoopCount > 0, "independent original CaveBear idle envelope prepared");
        float otherY = ActorBars.Position(otherBar, other.Controller).y;
        for (int phase = 0; phase < 96; phase++)
        {
            other.Animator.Play("Base Layer.Idle", 0, phase / 96f); other.Animator.Update(0.0001f);
            ActorBars.Tick(otherBar, other.Controller, 1000 + phase / 96f);
            Near(ActorBars.Position(otherBar, other.Controller).y, otherY, 0.003f, "independent native creature idle cycle keeps fixed world height");
            otherBar.Pose!.TryTop(out float otherTop);
            Check(otherTop + 0.003f >= BindEnvelopeTop(other), "independent native creature loop ceiling encloses authored body");
        }
        Object.DestroyImmediate(other.Root); Object.DestroyImmediate(other.Controller.gameObject);
        Metrics = "real native sleeping/flying skin; imported isReadable=false; uploaded runtime copies retain boneWeights/bindposes and private bake recovery; lower-edge pixel gap >=" + minimumGap + "; max envelope surplus=" + maxSurplus.ToString("F4")
            + "wu; sleeping anchor=" + sleepAnchor.ToString("F3") + "; flying peak=" + flightAnchor.ToString("F3")
            + "; first native preparation=" + prepareMs.ToString("F2") + "ms; steady=" + steadyMicroseconds.ToString("F2") + "us/bar on Editor CPU"
            + "; optional cadence steady=" + sparseMicroseconds.ToString("F2") + "us/bar (Editor only); complete 3-cycle flap proof + actual native standup + independent original CaveBear idle; transformed ancestry, native SkinQuality, pooled remote reuse, actual detail0 bank and bounded retries";
        Object.DestroyImmediate(scratch); Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(white); Object.DestroyImmediate(red);
    }
}
