using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using HarmonyLib;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using Object = UnityEngine.Object;

public static class InteractionProgram
{
    [Serializable] private sealed class NativeComponents { public NativeComponent[] components = Array.Empty<NativeComponent>(); }
    [Serializable] private sealed class NativeComponent { public string assembly = "", type = "", path = ""; public NativeField[] fields = Array.Empty<NativeField>(); public bool enabled = true; }
    [Serializable] private sealed class NativeField { public string name = "", value = ""; }
    public static int Checks;
    public static string Metrics = "";
    private static LODGroup[]? _cloneGroups;
    private static LOD[][]? _cloneTables;
    private static SkinnedMeshRenderer[]? _cloneSkins;
    private static void ObserveRestoredCloneSource()
    {
        if (_cloneGroups == null) return;
        Check(_cloneGroups.Select((group,index) => SameLodTable(group.GetLODs(),_cloneTables![index])).All(value => value)
            && _cloneSkins!.All(skin => !skin.forceRenderingOff),
            "real visual clone restores original source topology before its body executes");
    }
    private static string Arg(string key)
    { string[] args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, key) + 1]; }
    private static Exception? _pendingAssertion;
    private static void Check(bool condition, string label)
    {
        Checks++;
        if (!condition) throw _pendingAssertion = new Exception(label);
    }
    private static void Render(Camera camera)
    {
        // Unity catches managed camera-callback exceptions at its native boundary.
        // Preserve assertions from callbacks and rethrow on the runner side, otherwise
        // an invalid clone/table can print an Exception while the case reports PASS.
        camera.Render();
        if (_pendingAssertion != null) throw _pendingAssertion;
    }
    private static void InjectOptionalFault() => throw new InvalidOperationException("Injected optional-owner lifetime fault");
    private static ScenarioVisibleIdleSnapshot CaptureCurrentPoseForPixelCalibration(ActorBarPose pose, Transform host)
    {
        // Compare CPU BakeMesh and original GPU skinning at ONE actual native bone pose.
        // The production crowd/warm/clock tests below separately drive native scheduling.
        // A retained interval sample is intentionally older than the later native pose;
        // pretending those were same-pose hid the original weak full-frame pixel check.
        var snapshot = new ScenarioVisibleIdleSnapshot(pose, host);
        snapshot.Tick(true, .5f);
        typeof(ScenarioVisibleIdleSnapshot).GetField("_sampleFrame", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(snapshot, Time.frameCount - 1);
        snapshot.AfterNativePose();
        Check(snapshot.HasPose, "same-pose calibration prepares actual native renderer geometry");
        return snapshot;
    }
    private static bool SameBodyPixels(Color32[] actual, Color32[] expected)
    {
        int body = expected.Count(pixel => pixel.g > 128 && pixel.r < 64);
        int changed = actual.Zip(expected, (a,b) => Math.Abs(a.g-b.g)>16).Count(value => value);
        bool same = changed <= Math.Max(3, body / 20);
        if (!same)
        {
            string prefix = Path.Combine(Arg("-evidenceRoot"), typeof(InteractionProgram).Assembly.GetName().Name! + "-pixel-mismatch");
            var picture = new Texture2D((int)Math.Sqrt(actual.Length),(int)Math.Sqrt(actual.Length),TextureFormat.RGBA32,false);
            picture.SetPixels32(actual); picture.Apply(); File.WriteAllBytes(prefix + "-actual.png", picture.EncodeToPNG());
            picture.SetPixels32(expected); picture.Apply(); File.WriteAllBytes(prefix + "-expected.png", picture.EncodeToPNG());
            Object.DestroyImmediate(picture);
            File.WriteAllText(prefix + ".txt", "actualGreen=" + actual.Count(pixel => pixel.g>128 && pixel.r<64)
                + "; expectedGreen=" + body + "; changed=" + changed);
        }
        return same;
    }
    private static bool SameLodTable(LOD[] left, LOD[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
            if (left[i].screenRelativeTransitionHeight != right[i].screenRelativeTransitionHeight
                || left[i].fadeTransitionWidth != right[i].fadeTransitionWidth
                || !left[i].renderers.SequenceEqual(right[i].renderers)) return false;
        return true;
    }
    private static Color32[] ReadPixels(Camera eye)
    {
        Render(eye);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = eye.targetTexture;
        var image = new Texture2D(eye.targetTexture.width,eye.targetTexture.height,TextureFormat.RGBA32,false);
        image.ReadPixels(new Rect(0,0,eye.targetTexture.width,eye.targetTexture.height),0,0); image.Apply();
        Color32[] pixels = image.GetPixels32();
        RenderTexture.active = previous; Object.DestroyImmediate(image);
        return pixels;
    }
    public static IEnumerator Run()
    {
        string evidence = Arg("-evidenceRoot"), name = typeof(InteractionProgram).Assembly.GetName().Name!;
        AssetBundle bundle = AssetBundle.LoadFromFile(Arg("-nativeDrakeBundle"));
        GameObject prefab = bundle.LoadAsset<GameObject>("Assets/Content/Characters/Monsters/MO_SpittingDrake/MO_SpittingDrake_PR.prefab");
        Check(prefab != null, "original publisher prefab loads");
        Animator original = prefab!.GetComponentsInChildren<Animator>(true).First(a => a.runtimeAnimatorController != null);
        File.WriteAllText(Path.Combine(evidence, name + "-original-animator.txt"), "cullingMode=" + original.cullingMode
            + "; clips=" + original.runtimeAnimatorController.animationClips.Length + "; behaviours="
            + string.Join(",", original.GetBehaviours<StateMachineBehaviour>().Select(b => b.GetType().FullName)));
        GameObject root = FigureVisualMirror.CloneVisual(prefab, Vector3.zero, Quaternion.identity, Vector3.one, out FigureVisualMirror mirror);
        Object.DestroyImmediate(mirror); root.SetActive(false);
        string path = AnimationUtility.CalculateTransformPath(original.transform, prefab.transform);
        Animator animator = root.transform.Find(path).gameObject.AddComponent<Animator>(); animator.avatar = original.avatar;
        // The original graph's callbacks need a running SaveData/Choreographer. Use its
        // original avatar/clip bodies with the same actual idle-state vocabulary here;
        // component identity/conditional fields still come from the original prefab.
        AnimatorController graph = AnimatorController.CreateAnimatorControllerAtPath("Assets/" + name + "OriginalClips.controller");
        graph.layers[0].stateMachine.AddState("SleepIdle").motion = original.runtimeAnimatorController.animationClips.First(c => c.name.Contains("Sleeping_Idle"));
        graph.layers[0].stateMachine.AddState("Idle-Run").motion = original.runtimeAnimatorController.animationClips.First(c => c.name == "Spitting_Flying_Idle_v001");
        graph.layers[0].stateMachine.AddState("WakeUp").motion = original.runtimeAnimatorController.animationClips.First(c => c.name.Contains("WakeUp"));
        // Native alternative-idle callback with its original inert default runs without
        // any fixture substitute. Its exact type is checked by the production audit.
        graph.layers[0].stateMachine.states.First(x => x.state.name == "SleepIdle").state.AddStateMachineBehaviour<ToggleAlternativeIdleFxSMB>();
        graph.layers[0].stateMachine.states.First(x => x.state.name == "SleepIdle").state.AddStateMachineBehaviour<ProgressChoreographerSMB>();
        animator.runtimeAnimatorController = graph;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = original.applyRootMotion;
        var componentReceipts = new List<string>();
        var needed = new HashSet<Transform> { root.transform };
        foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (Transform bone in skin.bones)
                for (Transform? t = bone; t != null && t != root.transform; t = t.parent) needed.Add(t);
        for (Transform? t = animator.transform; t != null && t != root.transform; t = t.parent) needed.Add(t);
        // Windows serialized MonoScripts do not register as Editor scripts through
        // Assembly.Load. Read their original identity/conditional fields independently
        // with UnityPy, then instantiate those EXACT publisher types on the copied rig.
        NativeComponents metadata = Newtonsoft.Json.JsonConvert.DeserializeObject<NativeComponents>(File.ReadAllText(Arg("-nativeComponentMetadata")))!;
        foreach (NativeComponent source in metadata.components)
        {
            Transform target = source.path.Length == 0 ? root.transform : root.transform.Find(source.path);
            if (target == null) throw new Exception("Original script transform absent: " + source.path);
            if (!needed.Contains(target)) continue;
            Type type = Assembly.Load(source.assembly).GetType(source.type, true)!;
            Component copy = target.gameObject.GetComponent(type) ?? target.gameObject.AddComponent(type);
            foreach (NativeField field in source.fields)
            {
                FieldInfo? originalField = type.GetField(field.name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (originalField == null) continue;
                Type targetType = originalField.FieldType;
                if (targetType.IsEnum) originalField.SetValue(copy, Enum.ToObject(targetType, int.Parse(field.value)));
                else if (targetType == typeof(bool)) originalField.SetValue(copy, field.value == "1" || field.value == "True");
                else if (targetType == typeof(string)) originalField.SetValue(copy, field.value);
                else if (targetType.IsPrimitive) originalField.SetValue(copy, Convert.ChangeType(field.value, targetType, System.Globalization.CultureInfo.InvariantCulture));
            }
            ((Behaviour)copy).enabled = false; // native gameplay boot is intentionally not executed
            componentReceipts.Add(type.Assembly.GetName().Name + "/" + type.FullName + "@" + source.path
                + " admitted=" + NativeActorPoseAudit.Allows(copy, root.transform));
        }
        Check(componentReceipts.Count >= 10 && componentReceipts.Any(x => x.Contains("DeathDissolve"))
            && componentReceipts.Any(x => x.Contains("AutomaticLOD")) && componentReceipts.Any(x => x.Contains("UnityGameEditorObject")),
            "real serialized native body AND gameplay wrapper scripts retained in audit rig");
        var character = root.GetComponent<CharacterManager>(); character.enabled = false;
        // Choreographer injects this real component on the Animator at actor construction
        // (original native lines891/1074); the wrapper/body prefabs alone omit it.
        var actorEvents = animator.gameObject.AddComponent<ActorEvents>(); actorEvents.enabled = false;
        var actor = root.AddComponent<ActorBehaviour>(); actor.enabled = false;
        Check(character.GetType().Assembly.GetName().Name == "GH.Runtime", "CharacterManager is actual publisher class");
        File.WriteAllLines(Path.Combine(evidence, name + "-native-components.txt"), componentReceipts);
        root.SetActive(true); animator.Rebind(); animator.Play("SleepIdle", 0, 0.2f); animator.Update(0.001f);
        Transform head = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "C_headSkel01_JNT");
        File.WriteAllLines(Path.Combine(evidence, name + "-native-state-behaviours.txt"), animator.GetBehaviours<StateMachineBehaviour>().Select(b => b.GetType().FullName));
        ActorBarPose? pose = ActorBarPose.Capture(root, head);
        Check(pose != null && pose.LoopCount >= 2, "original serialized controller loops prepare");
        Check(pose!.SparseEligible, "actual mandatory CharacterManager and original prefab components admit sparse checks; " + pose.AuditRefusal);
        Check(pose.IsEventFreeNativeIdle(), "original native SleepIdle is safe event-free idle");
        pose.TryTop(out float stableTop);
        int before = pose.VerificationCount;
        for (int i = 0; i < 100; i++)
        {
            animator.Play("SleepIdle", 0, i / 100f); animator.Update(0.0001f);
            pose.TryTop(out float top);
            Check(Mathf.Abs(top - stableTop) < 0.003f, "native admitted sleeping bar remains fixed across idle phases");
        }
        Check(pose.SkippedVerificationCount >= 98 && pose.VerificationCount - before <= 1,
            "native admitted rig actually skips repeated bone walks");
        var unknown = head.gameObject.AddComponent<UnknownBoneWriter>(); unknown.enabled = false;
        Check(!ActorBarPose.Capture(root, head)!.SparseEligible, "unknown writer remains unsafe even disabled");
        Object.DestroyImmediate(unknown);
        var derived = root.AddComponent<CharacterManagerSubclass>(); derived.enabled = false;
        Check(!ActorBarPose.Capture(root, head)!.SparseEligible, "unknown native subclass cannot bypass exact audit");
        Object.DestroyImmediate(derived);
        var provider = root.AddComponent<UnknownDetailProvider>(); provider.enabled = false;
        var details = root.GetComponent<DetailsDisabler>() ?? root.AddComponent<DetailsDisabler>(); details.enabled = false;
        Check(!NativeActorPoseAudit.Allows(details, root.transform), "unknown detail provider prevents sparse admission");
        Check(!ActorBarPose.Capture(root, head)!.SparseEligible, "unknown provider script also retains conservative bone checks");
        Object.DestroyImmediate(provider);
        var unknownGraph = AnimatorController.CreateAnimatorControllerAtPath("Assets/" + name + "UnknownState.controller");
        var unknownIdle = unknownGraph.layers[0].stateMachine.AddState("SleepIdle");
        unknownIdle.motion = original.runtimeAnimatorController.animationClips.First(c => c.name.Contains("Sleeping_Idle"));
        unknownIdle.AddStateMachineBehaviour<UnknownStateBoneWriter>();
        animator.runtimeAnimatorController = unknownGraph;
        animator.Rebind(); animator.Play("SleepIdle", 0, 0.2f); animator.Update(0.001f);
        Check(!ActorBarPose.Capture(root, head)!.IsEventFreeNativeIdle(),
            "unknown current native state callback cancels transform shortcut");
        animator.runtimeAnimatorController = graph;
        animator.Rebind(); animator.Play("SleepIdle", 0, 0.2f); animator.Update(0.001f);
        DeathDissolve dissolve = root.GetComponentInChildren<DeathDissolve>(true);
        DeathDissolve.s_DeathDissolvesInProgress.Add(dissolve);
        Check(!pose.SparseEligible, "actual active native dissolve immediately cancels sparse admission");
        DeathDissolve.s_DeathDissolvesInProgress.Remove(dissolve);
        dissolve.addVertexAnim = true;
        Check(!pose.SparseEligible, "native shader vertex animation immediately cancels sparse admission");
        dissolve.addVertexAnim = false;
        var record = new ScenarioIdleAnimationBudget.Record(actor, pose, animator);
        record.Tick(true);
        Check(record.Applied && animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms,
            "only transform culling is applied; original state clock stays native");
        record.Restore();
        HeldFigures.Held = actor; record.Tick(true);
        Check(!record.Applied && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "local held native actor restores exact original animator mode");
        HeldFigures.Held = null;
        NetHeldFigures.Held = actor; record.Tick(true);
        Check(!record.Applied, "peer held native actor stays fully evaluated");
        NetHeldFigures.Held = null;
        record.Tick(true); animator.cullingMode = AnimatorCullingMode.CullCompletely;
        record.Restore();
        Check(animator.cullingMode == AnimatorCullingMode.CullCompletely,
            "foreign animator culling edit survives owner restoration");
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Run the real Harmony prefix on the real publisher MF.AnimatorPlay method. No
        // lookalike MF, native Animator or synthetic return callback is used here.
        GameObject host = new("Native idle budget host");
        bool enabled = true;
        ScenarioIdleAnimationBudget.Install(host, () => enabled);
        ScenarioIdleAnimationBudget.Register(actor, pose);
        yield return null;
        Check(animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms, "registered native rig enters idle transform budget");
        actorEvents.ClearActorEventState();
        AnimationClip wake = original.runtimeAnimatorController.animationClips.First(c => c.name.Contains("WakeUp"));
        Check(MF.AnimatorPlay(animator, "WakeUp"), "real publisher MF nonloop state available");
        Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "real MF.AnimatorPlay restores before native nonloop dispatch");
        animator.Update(0.001f);
        Check(!pose.IsEventFreeNativeIdle(), "native nonloop action never enters transform shortcut");
        Check(actorEvents.ReceivedEvent(ActorEvents.ActorEvent.ProgressChoreographer),
            "real native state-exit continuation callback remains delivered");
        yield return null;
        Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate, "native action remains fully evaluated after player-frame boundary");
        animator.Play("SleepIdle", 0, 0.2f); animator.Update(0.001f);
        yield return null; yield return null; yield return null;
        Check(animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms, "native original idle resumes after action");
        enabled = false; yield return null;
        Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate, "option off restores exact original mode");
        ScenarioIdleAnimationBudget.Shutdown();

        // Native Unity culling advances the original state even while no eye camera can
        // see it. Compare actual transform changes versus AlwaysAnimate across frames.
        Transform wing = root.GetComponentsInChildren<Transform>(true).First(t => t.name.Contains("wing") && t.name.EndsWith("JNT"));
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        animator.Play("Idle-Run", 0, 0); animator.Update(0.001f);
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        yield return null;
        float startTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        Quaternion startWing = wing.localRotation;
        for (int i = 0; i < 12; i++) yield return null;
        Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > startTime,
            "actual offscreen native state machine continues with transform writes culled");
        Check(Quaternion.Angle(wing.localRotation, startWing) < 0.01f,
            "actual offscreen native bone transform writes are suppressed");
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        bool moved = false;
        for (int i = 0; i < 12; i++) { yield return null; moved |= Quaternion.Angle(wing.localRotation, startWing) > 0.1f; }
        Check(moved, "original AlwaysAnimate control still writes invisible native wing poses");

        // Native LODGroup visibility is independent of renderer.enabled. Its hidden
        // ForceLOD state cannot be reproduced by unrelated private MeshRenderers.
        LODGroup[] nativeLods = root.GetComponentsInChildren<LODGroup>(true);
        Check(nativeLods.Any(group => group.enabled && group.GetLODs().Length >= 3),
            "publisher rig retains real active three-level LOD topology");
        foreach (LODGroup group in nativeLods) group.fadeMode = LODFadeMode.CrossFade;
        var lodSnapshot = new ScenarioVisibleIdleSnapshot(pose, host.transform);
        lodSnapshot.Tick(true, .5f);
        Check(lodSnapshot.HasActiveNativeLod && !lodSnapshot.AwaitingNativePose && !lodSnapshot.HasPose,
            "active original LOD ownership declines visible idle sampling");
        var lodRecord = new ScenarioIdleAnimationBudget.Record(actor, pose, animator) { Visible = lodSnapshot };
        lodRecord.Tick(false, .5f);
        Check(!lodRecord.Applied && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "unsupported visible idle does not silently activate the separate offscreen option");
        nativeLods[0].ForceLOD(2); lodRecord.Tick(false, .5f);
        Check(!lodRecord.Applied && !lodSnapshot.AwaitingNativePose,
            "hidden native forced LOD remains original without warm retries");
        nativeLods[0].ForceLOD(-1);
        lodRecord.Tick(true, .5f);
        Check(lodRecord.Applied && animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms,
            "native LOD refusal retains the independent offscreen idle budget");
        lodRecord.Restore(); lodSnapshot.Dispose();
        int previousLodReports = VRLog.VisibleIdleLodReports;
        ScenarioIdleAnimationBudget.Install(host, () => false, () => .5f);
        ScenarioIdleAnimationBudget.Register(actor, pose); yield return null;
        Check(VRLog.VisibleIdleLodReports == previousLodReports + 1,
            "native LOD refusal has one bounded initial Debug report");
        for (int recapture = 0; recapture < 3; recapture++)
        {
            ScenarioIdleAnimationBudget.Register(actor, ActorBarPose.Capture(root, head)!);
            yield return null;
        }
        Check(VRLog.VisibleIdleLodReports == previousLodReports + 1,
            "native LOD Debug refusal stays deduplicated across pose recapture");
        ScenarioIdleAnimationBudget.Shutdown();
        // Destroy is end-of-frame. Retire this diagnostic owner before the later
        // fixture resolves a fresh Driver through the real host's GetComponent.
        yield return null;
        var expiredActorObject = new GameObject("Expired native LOD refusal actor");
        ActorBehaviour expiredActor = expiredActorObject.AddComponent<ActorBehaviour>(); expiredActor.enabled = false;
        var expiredRecord = new ScenarioIdleAnimationBudget.Record(expiredActor, pose, animator)
            { Visible = new ScenarioVisibleIdleSnapshot(pose, host.transform) };
        expiredRecord.Tick(false, .5f);
        Check(expiredRecord.VisibleLodRefused, "expired actor fixture begins with an actual native LOD refusal");
        Object.DestroyImmediate(expiredActorObject);
        Check(!expiredRecord.Tick(false, .5f) && !expiredRecord.VisibleLodRefused && !expiredRecord.VisiblePhysicsRefused,
            "destroyed native actor clears diagnostic refusal before name or live property reads");
        expiredRecord.Visible.Dispose();
        // The following positive snapshot calibration deliberately disables native LOD
        // ownership. It proves supported skins; it cannot establish native LOD coverage.
        foreach (LODGroup group in nativeLods) { group.fadeMode = LODFadeMode.None; group.enabled = false; }

        // Run the real optional visible-idle owner against native publisher geometry,
        // clips and camera callbacks. No Animator timing or renderer visibility stub.
        var eyeObject = new GameObject("VisibleIdleEye");
        var eye = eyeObject.AddComponent<Camera>(); eye.enabled = false;
        eye.transform.position = new Vector3(0f, 2f, -8f);
        eye.transform.LookAt(root.transform.position + Vector3.up);
        eye.orthographic = true; eye.orthographicSize = 5f;
        eye.clearFlags = CameraClearFlags.SolidColor; eye.backgroundColor = Color.black;
        eye.targetTexture = new RenderTexture(512, 512, 24); eye.targetTexture.Create();
        SkinnedMeshRenderer[] visibleSkins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
        foreach (SkinnedMeshRenderer skin in visibleSkins)
        {
            Material[] materials = skin.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
                materials[index] = new Material(Shader.Find("Unlit/Color")) { color = Color.green };
            skin.sharedMaterials = materials;
        }
        File.WriteAllText(Path.Combine(evidence,name+"-original-skinning-policy.txt"),
            "global="+QualitySettings.skinWeights+"; skins="+string.Join(",",visibleSkins.Select(skin=>skin.quality)));
        QualitySettings.skinWeights = SkinWeights.FourBones;
        foreach (SkinnedMeshRenderer skin in visibleSkins) skin.quality = SkinQuality.Bone4;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Play("Idle-Run", 0, 0f); animator.Update(.001f);
        ActorBarPose visiblePose = ActorBarPose.Capture(root, head)!;
        Check(visiblePose.IsEventFreeNativeIdle(), "native flying idle admits visible pose reduction");
        var ownedIdleSkins = new List<SkinnedMeshRenderer>();
        Check(visiblePose.CopyIdleSkinSources(ownedIdleSkins) && ownedIdleSkins.Count > 0,
            "visible idle pixel fixture uses positively owned native body surfaces");
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            if (renderer is not SkinnedMeshRenderer skin || !ownedIdleSkins.Contains(skin)) renderer.enabled = false;
        visibleSkins = ownedIdleSkins.ToArray();
        Mesh[] originalMeshes = visibleSkins.Select(skin => skin.sharedMesh).ToArray();
        Material[][] originalSlots = visibleSkins.Select(skin => skin.sharedMaterials).ToArray();
        float visibleInterval = .5f;
        Camera? admittedEye = eye;
        ScenarioIdleAnimationBudget.Install(host, () => false, () => visibleInterval, () => admittedEye);
        ScenarioIdleAnimationBudget.Register(actor, visiblePose);
        var recordsField = typeof(ScenarioIdleAnimationBudget.Driver).GetField("_records", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var records = (Dictionary<ActorBehaviour, ScenarioIdleAnimationBudget.Record>)recordsField.GetValue(host.GetComponent<ScenarioIdleAnimationBudget.Driver>())!;
        var visibleRecord = records[actor];
        bool sawLease = false, invokeActionInsideLease = false;
        Camera.CameraCallback inspectLease = camera =>
        {
            if (camera != eye || !visibleSkins.Any(skin => skin.forceRenderingOff)) return;
            sawLease = true;
            for (int index = 0; index < visibleSkins.Length; index++)
            {
                Check(visibleSkins[index].enabled && visibleSkins[index].sharedMesh == originalMeshes[index],
                    "visible idle retains native enabled state and mesh identity during culling");
                Check(visibleSkins[index].sharedMaterials.SequenceEqual(originalSlots[index]),
                    "visible idle retains every original material slot during culling");
            }
            if (invokeActionInsideLease)
            {
                Check(MF.AnimatorPlay(animator, "WakeUp"), "visible-idle native action dispatch remains available");
                Check(visibleSkins.All(skin => !skin.forceRenderingOff),
                    "native action immediately releases visible idle masks before continuation");
                invokeActionInsideLease = false;
            }
        };
        Camera.onPreCull += inspectLease;
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null; Render(eye);
            Check(visibleSkins.All(skin => !skin.forceRenderingOff),
                "visible idle masks restore after each real camera");
            Check(visibleSkins.Select((skin,index) => skin.enabled && skin.sharedMesh==originalMeshes[index]).All(value => value),
                "visible idle retains native enabled state and mesh identity during culling");
        }
        File.WriteAllText(Path.Combine(evidence, name + "-visible-pose-state.txt"),
            "applied=" + visibleRecord.Applied + "; samples=" + visibleRecord.Visible!.Samples
            + "; pose=" + visibleRecord.Visible.HasPose + "; awaiting=" + visibleRecord.Visible.AwaitingNativePose
            + "; actual skins=" + string.Join(",", ownedIdleSkins.Select(skin => skin.name + ":" + skin.sharedMesh.vertexCount))
            + "; LOD=" + string.Join(",", nativeLods.Select(group => group.enabled + ":" + group.fadeMode + ":" + group.size)));
        Check(visibleRecord.Visible!.HasPose && sawLease, "real native camera admits an actual private visible idle pose");
        Check(visibleRecord.Applied && animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms,
            "visible pose option is independent of offscreen option");
        Color32[] retainedPixels = ReadPixels(eye);
        Check(retainedPixels.Count(pixel => pixel.g > 128 && pixel.r < 64) > 10,
            "visible idle proxy retains visible original skin pixels");
        admittedEye = null;
        Color32[] originalPixels = ReadPixels(eye); // native render resolves its current skin pose first
        using (var calibration = CaptureCurrentPoseForPixelCalibration(visiblePose,host.transform))
        {
            calibration.BeforeCamera(true);
            Color32[] proxyPixels = ReadPixels(eye);
            calibration.Release();
            Check(originalPixels.Count(pixel => pixel.g > 128 && pixel.r < 64)>10 && SameBodyPixels(proxyPixels, originalPixels),
                "private visible idle silhouette matches native same-pose drawing");
        }
        foreach (SkinQuality policy in new[] { SkinQuality.Bone1,SkinQuality.Bone2,SkinQuality.Bone4 })
        {
            foreach (SkinnedMeshRenderer skin in visibleSkins) skin.quality = policy;
            yield return null; // allow native quality/skin caches to settle before the GPU control
            Color32[] sourcePolicy = ReadPixels(eye);
            using var policyCalibration = CaptureCurrentPoseForPixelCalibration(visiblePose,host.transform);
            policyCalibration.BeforeCamera(true);
            Color32[] bakedPolicy = ReadPixels(eye); policyCalibration.Release();
            Check(sourcePolicy.Count(pixel=>pixel.g>128&&pixel.r<64)>10 && SameBodyPixels(bakedPolicy,sourcePolicy),
                "same-pose bake preserves the actual native skinning influence policy " + policy);
        }
        admittedEye = eye;
        // Warm visibility after the original control; then demonstrate that transforms
        // stop between samples while the real native state clock continues.
        for (int frame=0; frame<4; frame++) { yield return null; Render(eye); }
        Quaternion frozenWing = wing.localRotation;
        float frozenClock = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        for (int frame=0; frame<8; frame++) { yield return null; Render(eye); }
        Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > frozenClock,
            "visible idle keeps the real native state clock advancing");
        Check(Quaternion.Angle(wing.localRotation, frozenWing) < .01f,
            "visible idle camera lease suppresses actual native bone writes between samples");
        // A native consumer of exact Renderer identities must get originals.
        var nativeBuffer = new UnityEngine.Rendering.CommandBuffer { name = "native-renderer-consumer" };
        nativeBuffer.DrawRenderer(visibleSkins[0], visibleSkins[0].sharedMaterial);
        eye.AddCommandBuffer(UnityEngine.Rendering.CameraEvent.BeforeForwardOpaque, nativeBuffer);
        Render(eye);
        Check(!visibleRecord.Visible.IsMasked && visibleSkins.All(skin => !skin.forceRenderingOff),
            "native command-buffer consumers retain original figure renderers");
        eye.RemoveCommandBuffer(UnityEngine.Rendering.CameraEvent.BeforeForwardOpaque, nativeBuffer); nativeBuffer.Dispose();
        // An action begins inside an already acquired camera lease: restoration must
        // be synchronous, not postponed until the next Update or PostRender.
        invokeActionInsideLease = true; Render(eye);
        Check(!invokeActionInsideLease,
            "native action immediately releases visible idle masks before continuation");
        Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "visible idle native action restores before the actual camera finishes");
        visibleInterval = 0f;
        yield return null;
        Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate
            && visibleSkins.All(skin => !skin.forceRenderingOff), "zero visible interval restores original pose evaluation");
        Camera.onPreCull -= inspectLease;
        // A crowd must retain its existing pictures while only bounded native sample
        // slots warm. These are actual cloned publisher rigs, clips and native skins.
        visibleInterval = .5f;
        animator.Play("Idle-Run", 0, 0f); animator.Update(.001f);
        var crowd = new List<GameObject>();
        for (int index = 0; index < 4; index++)
        {
            GameObject clone = Object.Instantiate(root);
            clone.transform.position = new Vector3(index - 2f, 0f, 0f);
            Animator cloneAnimator = clone.GetComponentInChildren<Animator>();
            cloneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            cloneAnimator.Rebind(); cloneAnimator.Play("Idle-Run", 0, 0f); cloneAnimator.Update(.001f);
            Transform cloneHead = clone.GetComponentsInChildren<Transform>(true).First(t => t.name == "C_headSkel01_JNT");
            ActorBarPose clonePose = ActorBarPose.Capture(clone, cloneHead)!;
            Check(clonePose.IsEventFreeNativeIdle(), "crowd uses audited actual publisher idle rigs");
            ScenarioIdleAnimationBudget.Register(clone.GetComponent<ActorBehaviour>(), clonePose);
            crowd.Add(clone);
        }
        int priorSamples = records.Values.Sum(item => item.Visible?.Samples ?? 0);
        for (int frame = 0; frame < 30; frame++)
        {
            yield return null; Render(eye);
            Check(records.Values.Count(item => item.Visible?.AwaitingNativePose == true) <= 2,
                "visible idle crowd warms at most two actual native poses concurrently");
            int samples = records.Values.Sum(item => item.Visible?.Samples ?? 0);
            Check(samples - priorSamples <= 2, "visible idle crowd bakes at most two actors per frame");
            priorSamples = samples;
            Check(crowd.SelectMany(item => item.GetComponentsInChildren<SkinnedMeshRenderer>(true)).All(skin => !skin.forceRenderingOff),
                "all original crowd skins restore after each actual camera");
        }
        Check(records.Values.All(item => item.Visible?.HasPose == true),
            "rotating visible idle sample lane eventually admits every actual crowd rig");
        host.SetActive(false); Render(eye);
        Check(records.Values.All(item => !item.Applied && item.Visible?.IsMasked == false)
            && crowd.SelectMany(item => item.GetComponentsInChildren<SkinnedMeshRenderer>(true)).All(skin => !skin.forceRenderingOff),
            "inactive idle host retains original native modes and visible skins across actual camera callbacks");
        host.SetActive(true);
        ScenarioIdleAnimationBudget.Shutdown();
        foreach (GameObject clone in crowd) Object.DestroyImmediate(clone);
        yield return null; // retire the destroyed owner before GetComponent resolves its replacement

        // Actual publisher groups stay enabled here. Leases use the SAME native group,
        // not an inferred screen-height value or a cloned group's unknown ForceLOD state.
        foreach (LODGroup group in nativeLods) { group.enabled = true; group.fadeMode = LODFadeMode.None; group.ForceLOD(-1); }
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        animator.Play("Idle-Run", 0, .2f); animator.Update(.001f);
        ActorBarPose authoredPose = ActorBarPose.Capture(root, head)!;
        float authoredInterval = .5f;
        ScenarioIdleAnimationBudget.Install(host, () => false, () => authoredInterval, () => eye);
        ScenarioIdleAnimationBudget.Register(actor, authoredPose);
        var nativeDriver = host.GetComponent<ScenarioIdleAnimationBudget.Driver>();
        var nativeRecords = (Dictionary<ActorBehaviour, ScenarioIdleAnimationBudget.Record>)recordsField.GetValue(nativeDriver)!;
        var authoredRecord = nativeRecords[actor];
        for (int frame = 0; frame < 8; frame++) { yield return null; Render(eye); }
        Check(authoredRecord.Applied && authoredRecord.Original == AnimatorCullingMode.CullUpdateTransforms
            && authoredRecord.Visible!.HasPose,
            "authored CullUpdateTransforms rigs prepare real supported visible idle poses");
        var nativeTables = nativeLods.Select(group => group.GetLODs()).ToArray();
        var nativeSizes = nativeLods.Select(group => group.size).ToArray();
        var nativeReferences = nativeLods.Select(group => group.localReferencePoint).ToArray();
        // Causal lease mutations activate only AFTER native warmup succeeds. A broken
        // first lease can otherwise fail a generic admission prerequisite and never
        // reach the specific topology/restoration oracle it is meant to exercise.
        nativeLods[0].gameObject.name = "NativeLodLeaseProbe";
        bool sawNativeLodLease = false;
        Camera.CameraCallback inspectNativeLod = camera =>
        {
            if (camera != eye || !authoredRecord.Visible!.IsMasked) return;
            sawNativeLodLease = true;
            Check(nativeLods.All(group => group.enabled), "idle LOD lease never disables original selection groups");
            Check(nativeLods.Select((group,index) => group.size == nativeSizes[index]
                && group.localReferencePoint == nativeReferences[index]).All(value => value),
                "idle LOD lease retains exact native bounds and reference points");
            Check(nativeLods.SelectMany(group => group.GetLODs()).SelectMany(level => level.renderers)
                .Any(renderer => renderer != null && renderer.name == "GloomhavenVR.VisibleIdlePose"),
                "idle LOD lease uses real private body renderers on the original group");
        };
        Camera.onPreCull += inspectNativeLod;
        for (int level = 0; level < nativeTables[0].Length; level++)
        {
            nativeLods[0].ForceLOD(level);
            Render(eye);
            Check(sawNativeLodLease && authoredRecord.Visible!.HasPose,
                "native forced LOD uses an actual complete pose lease");
            Check(nativeLods.Select((group,index) => SameLodTable(group.GetLODs(), nativeTables[index])).All(value => value),
                "native original LOD tables restore after every real camera");
            // Temporarily reject ONLY this camera, leaving the same actual pose/ForceLOD.
            nativeDriver.HeadCamera = () => null;
            Color32[] exact = ReadPixels(eye);
            using var calibration = CaptureCurrentPoseForPixelCalibration(authoredPose,host.transform);
            calibration.BeforeCamera(true);
            Color32[] baked = ReadPixels(eye);
            calibration.Release();
            nativeDriver.HeadCamera = () => eye;
            Check(exact.Count(pixel => pixel.g > 128 && pixel.r < 64) > 10,
                "native forced LOD positive control contains actual body pixels");
            Check(SameBodyPixels(baked, exact),
                "same native forced LOD pose pixels match original skin rendering");
        }
        nativeLods[0].ForceLOD(-1);
        eye.orthographic = false;
        Vector3 eyePosition = eye.transform.position;
        foreach (float distance in new[] { 3f, 15f, 500f })
        {
            eye.transform.position = new Vector3(0f, 2f, -distance); eye.transform.LookAt(root.transform.position + Vector3.up);
            nativeDriver.HeadCamera = () => null;
            Color32[] exact = ReadPixels(eye);
            using var calibration = CaptureCurrentPoseForPixelCalibration(authoredPose,host.transform);
            calibration.BeforeCamera(true);
            Color32[] baked = ReadPixels(eye);
            calibration.Release();
            nativeDriver.HeadCamera = () => eye;
            Check(SameBodyPixels(baked, exact),
                "native distance LOD and far culling match original camera pixels");
        }
        eye.orthographic = true; eye.transform.position = eyePosition; eye.transform.LookAt(root.transform.position + Vector3.up);
        for (int frame = 0; frame < 3; frame++) { yield return null; Render(eye); }
        Quaternion authoredWing = wing.localRotation;
        float authoredClock = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        for (int frame = 0; frame < 6; frame++) { yield return null; Render(eye); }
        Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > authoredClock,
            "native LOD pose lease retains authored original animation clock");
        Check(Quaternion.Angle(wing.localRotation, authoredWing) < .01f,
            "native LOD pose lease suppresses actual authored native bone writes");
        // A different forced level chosen AFTER this owner's PreCull uses the native
        // group's hidden state directly, without resetting or guessing it.
        nativeDriver.HeadCamera = () => null;
        nativeLods[0].ForceLOD(1);
        Color32[] lateForcedExact = ReadPixels(eye);
        using (var forcedCalibration = CaptureCurrentPoseForPixelCalibration(authoredPose,host.transform))
        {
            nativeLods[0].ForceLOD(0);
            forcedCalibration.BeforeCamera(true);
            Camera.CameraCallback lateForced = camera => { if (camera == eye) nativeLods[0].ForceLOD(1); };
            Camera.onPreCull += lateForced;
            Color32[] lateForcedBaked = ReadPixels(eye);
            Camera.onPreCull -= lateForced; forcedCalibration.Release();
            Check(lateForcedExact.Count(pixel=>pixel.g>128&&pixel.r<64)>10 && SameBodyPixels(lateForcedBaked,lateForcedExact),
                "late native ForceLOD keeps the exact forced original body");
        }
        nativeDriver.HeadCamera = () => eye;
        // Foreign table changes revoke the lease BEFORE rendering and survive cleanup.
        LOD[] foreignTable = nativeLods[0].GetLODs();
        foreignTable[0].screenRelativeTransitionHeight *= .9f;
        Camera.CameraCallback lateTable = camera => { if (camera == eye) nativeLods[0].SetLODs(foreignTable); };
        Camera.onPreCull += lateTable;
        Color32[] lateTableActual = ReadPixels(eye);
        Camera.onPreCull -= lateTable;
        nativeDriver.HeadCamera = () => null;
        Color32[] lateTableExpected = ReadPixels(eye);
        nativeDriver.HeadCamera = () => eye;
        File.WriteAllText(Path.Combine(evidence, name + "-late-lod-pixels.txt"),
            "actualGreen=" + lateTableActual.Count(pixel => pixel.g > 128 && pixel.r < 64)
            + "; originalGreen=" + lateTableExpected.Count(pixel => pixel.g > 128 && pixel.r < 64)
            + "; changedPixels=" + lateTableActual.Zip(lateTableExpected, (a,b) => Math.Abs(a.g-b.g)>16).Count(value => value));
        Check(SameBodyPixels(lateTableActual, lateTableExpected),
            "late native LOD table fallback retains actual current-camera original pixels");
        Check(!authoredRecord.Visible!.HasPose && visibleSkins.All(skin => !skin.forceRenderingOff)
            && SameLodTable(nativeLods[0].GetLODs(), foreignTable),
            "late foreign LOD table revokes idle lease and survives owner restoration");
        nativeLods[0].SetLODs(nativeTables[0]); nativeLods[0].ForceLOD(-1);
        float recoverBefore = Time.unscaledTime + 1.5f;
        while (!authoredRecord.Visible!.HasPose && Time.unscaledTime < recoverBefore) { yield return null; Render(eye); }
        Check(authoredRecord.Visible!.HasPose, "native LOD path recovers after foreign table fallback");
        // Current Fastest/Frame figure detail rewrites early levels to the original
        // coarse body and masks omitted fine skins. Owner identity is a separately tested
        // service boundary here; the geometry/table/camera/Animator below are real Unity.
        ScenarioIdleAnimationBudget.BeforeNativeContentChange(); authoredRecord.Visible!.Reset();
        LOD[] cappedTable = nativeLods[0].GetLODs();
        Renderer[] retainedBody = cappedTable[2].renderers;
        var cappedMasks = nativeTables[0].SelectMany(level => level.renderers).Distinct()
            .Where(renderer => renderer != null && !retainedBody.Contains(renderer)).ToArray();
        for (int level = 0; level < 2; level++) cappedTable[level].renderers = retainedBody;
        nativeLods[0].SetLODs(cappedTable);
        foreach (Renderer renderer in cappedMasks)
        { renderer.forceRenderingOff = true; ScenarioFigureDetailBudget.OwnedMasks.Add(renderer); }
        float cappedBefore = Time.unscaledTime + 1.5f;
        while (!authoredRecord.Visible.HasPose && Time.unscaledTime < cappedBefore) { yield return null; Render(eye); }
        Check(authoredRecord.Visible.HasPose && authoredPose.CopyIdleSkinSources(ownedIdleSkins)
            && ownedIdleSkins.Count == retainedBody.OfType<SkinnedMeshRenderer>().Count()
            && cappedMasks.All(renderer => renderer.forceRenderingOff),
            "capped native LOD rig snapshots exactly its complete retained original body");
        nativeDriver.HeadCamera = () => null;
        Color32[] cappedNative = ReadPixels(eye);
        using var cappedCalibration = CaptureCurrentPoseForPixelCalibration(authoredPose,host.transform);
        cappedCalibration.BeforeCamera(true);
        Color32[] cappedBaked = ReadPixels(eye);
        cappedCalibration.Release();
        nativeDriver.HeadCamera = () => eye;
        Check(cappedNative.Count(pixel => pixel.g > 128 && pixel.r < 64) > 10 && SameBodyPixels(cappedBaked, cappedNative),
            "capped native LOD pose keeps exact original coarse body pixels");
        for (int frame = 0; frame < 3; frame++) { yield return null; Render(eye); }
        Quaternion cappedWing = wing.localRotation;
        float cappedClock = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        for (int frame = 0; frame < 6; frame++) { yield return null; Render(eye); }
        Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > cappedClock
            && Quaternion.Angle(wing.localRotation,cappedWing) < .01f,
            "capped native LOD pose really suppresses bone writes while native clock continues");
        ScenarioIdleAnimationBudget.BeforeNativeContentChange(); authoredRecord.Visible.Reset();
        foreach (Renderer renderer in cappedMasks) renderer.forceRenderingOff = false;
        ScenarioFigureDetailBudget.OwnedMasks.Clear(); nativeLods[0].SetLODs(nativeTables[0]);
        float originalBefore = Time.unscaledTime + 1.5f;
        while (!authoredRecord.Visible.HasPose && Time.unscaledTime < originalBefore) { yield return null; Render(eye); }
        Check(authoredRecord.Visible.HasPose, "original native LOD body recovers after the separate detail cap restores");

        // Ownership/settings/native renderer consumers may change in a subscriber
        // AFTER our own PreCull. These pixels belong to the actual current camera.
        foreach (string lateGate in new[] { "local hold", "remote hold", "prop hold", "interval zero", "native DrawRenderer" })
        {
            float gateBefore=Time.unscaledTime+1.5f;
            while ((!authoredRecord.Applied || !authoredRecord.Visible.HasPose || authoredRecord.Visible.AwaitingNativePose) && Time.unscaledTime<gateBefore)
            { yield return null; Render(eye); }
            bool ownedBeforeGate=false;
            var lateBuffer=new UnityEngine.Rendering.CommandBuffer { name="late-native-renderer-consumer" };
            foreach (SkinnedMeshRenderer source in visibleSkins) lateBuffer.DrawRenderer(source,source.sharedMaterial);
            Camera.CameraCallback lateActorGate=camera=>
            {
                if(camera!=eye)return;
                ownedBeforeGate=authoredRecord.Visible.IsMasked;
                if(lateGate=="local hold") HeldFigures.Held=actor;
                if(lateGate=="remote hold") NetHeldFigures.Held=actor;
                if(lateGate=="prop hold") ActorPropBody.Held=actor;
                if(lateGate=="interval zero") authoredInterval=0f;
                if(lateGate=="native DrawRenderer") eye.AddCommandBuffer(UnityEngine.Rendering.CameraEvent.BeforeForwardOpaque,lateBuffer);
            };
            Camera.onPreCull+=lateActorGate;
            Color32[] lateGateActual=ReadPixels(eye);
            Camera.onPreCull-=lateActorGate;
            bool resumedAfterActualCamera=!authoredRecord.Applied;
            nativeDriver.HeadCamera=()=>null;
            Color32[] lateGateExpected=ReadPixels(eye);
            nativeDriver.HeadCamera=()=>eye;
            Check(ownedBeforeGate && lateGateExpected.Count(pixel=>pixel.g>128&&pixel.r<64)>10
                && SameBodyPixels(lateGateActual,lateGateExpected) && resumedAfterActualCamera
                && animator.cullingMode==AnimatorCullingMode.CullUpdateTransforms
                && visibleSkins.All(skin=>!skin.forceRenderingOff),
                "late "+lateGate+" restores actual current-camera native body and authored mode");
            HeldFigures.Held=null; NetHeldFigures.Held=null; ActorPropBody.Held=null; authoredInterval=.5f;
            eye.RemoveCommandBuffer(UnityEngine.Rendering.CameraEvent.BeforeForwardOpaque,lateBuffer); lateBuffer.Dispose();
        }
        float cloneBefore=Time.unscaledTime+1.5f;
        while ((!authoredRecord.Applied || !authoredRecord.Visible.HasPose || authoredRecord.Visible.AwaitingNativePose) && Time.unscaledTime<cloneBefore)
        { yield return null; Render(eye); }

        // Clone seam restores ALL native renderer/table state before Instantiate.
        bool clonedInsideLease = false;
        var cloneOracle = new Harmony("ghvr.nativeActorAudit.cloneOracle");
        cloneOracle.Patch(typeof(FigureVisualMirror).GetMethod("CloneVisual",BindingFlags.Static|BindingFlags.NonPublic),
            prefix:new HarmonyMethod(typeof(InteractionProgram),nameof(ObserveRestoredCloneSource)) { priority=Priority.Last });
        Camera.CameraCallback cloneDuringLease = camera =>
        {
            if (camera != eye || clonedInsideLease || !authoredRecord.Visible!.IsMasked) return;
            _cloneGroups = nativeLods; _cloneTables = nativeTables; _cloneSkins = visibleSkins;
            GameObject clone = FigureVisualMirror.CloneVisual(root, Vector3.zero, Quaternion.identity,
                Vector3.one, out FigureVisualMirror cloneMirror); clonedInsideLease = true;
            _cloneGroups = null; _cloneTables = null; _cloneSkins = null;
            Check(clone.GetComponentsInChildren<LODGroup>(true).SelectMany(group => group.GetLODs())
                .Select(level => level.renderers.Length).SequenceEqual(nativeTables.SelectMany(table => table)
                    .Select(level => level.renderers.Length)),
                "real visual clone captures every original LOD body surface before copying");
            Check(clone.GetComponentsInChildren<LODGroup>(true).SelectMany(group => group.GetLODs())
                .SelectMany(level => level.renderers).All(renderer => renderer == null || renderer.transform.IsChildOf(clone.transform)),
                "native clone never captures private idle LOD renderer references");
            Check(clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(skin => !skin.forceRenderingOff),
                "native clone never captures idle source masks");
            Object.DestroyImmediate(clone);
        };
        Camera.onPreCull += cloneDuringLease; Render(eye); Camera.onPreCull -= cloneDuringLease;
        cloneOracle.UnpatchSelf();
        Check(clonedInsideLease, "native clone fixture runs inside an actual LOD pose lease");
        var nestedObject = new GameObject("Nested native idle observer");
        var nestedEye = nestedObject.AddComponent<Camera>(); nestedEye.enabled = false; nestedEye.CopyFrom(eye);
        bool nestedRendered = false;
        Camera.CameraCallback nested = camera =>
        {
            if (camera != eye || nestedRendered || !authoredRecord.Visible!.IsMasked) return;
            nestedRendered = true; Render(nestedEye);
            Check(authoredRecord.Visible.IsMasked,
                "nested native camera restores the suspended outer LOD lease");
        };
        Camera.onPreCull += nested; Render(eye); Camera.onPreCull -= nested;
        Check(nestedRendered && nativeLods.Select((group,index) => SameLodTable(group.GetLODs(),nativeTables[index])).All(value => value),
            "nested native cameras restore exact original LOD tables at outer completion");
        Object.DestroyImmediate(nestedObject);
        Check(MF.AnimatorPlay(animator, "WakeUp"), "authored cull native action remains available");
        Check(!authoredRecord.Applied && animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms
            && nativeLods.Select((group,index) => SameLodTable(group.GetLODs(),nativeTables[index])).All(value => value),
            "native action restores authored culling mode and exact LOD tables synchronously");
        Camera.onPreCull -= inspectNativeLod;
        ScenarioIdleAnimationBudget.Shutdown(); yield return null;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Play("Idle-Run", 0, .2f); animator.Update(.001f);
        foreach (LODGroup group in nativeLods) group.enabled = false;

        // Observe late native renderer writes through the real camera callback boundary.
        // Each case bakes actual publisher skin once, then changes one original flag
        // before the next camera. A stale proxy must decline before the source is masked.
        foreach (string property in new[] { "sorting", "motion", "occlusion", "quality", "lod" })
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Play("Idle-Run", 0, 0f); animator.Update(.001f);
            var lateSnapshot = new ScenarioVisibleIdleSnapshot(visiblePose, host.transform);
            lateSnapshot.Tick(true, .5f); yield return null; lateSnapshot.AfterNativePose();
            Check(lateSnapshot.HasPose, "late renderer fixture prepares actual native skin");
            bool maskedAtCull = false;
            Camera.CameraCallback acquire = camera =>
            {
                if (camera != eye) return;
                lateSnapshot.BeforeCamera(true); maskedAtCull = lateSnapshot.IsMasked;
            };
            Camera.CameraCallback release = camera => { if (camera == eye) lateSnapshot.Release(); };
            Camera.onPreCull += acquire; Camera.onPostRender += release;
            Render(eye);
            Check(maskedAtCull && visibleSkins.All(skin => !skin.forceRenderingOff),
                "late renderer fixture starts from a restored native camera lease");
            SkinnedMeshRenderer source = visibleSkins[0];
            int originalOrder = source.sortingOrder;
            var originalMotion = source.motionVectorGenerationMode;
            bool originalOcclusion = source.allowOcclusionWhenDynamic;
            SkinQuality originalQuality = source.quality;
            if (property == "sorting") source.sortingOrder = originalOrder + 7;
            if (property == "motion") source.motionVectorGenerationMode = originalMotion == UnityEngine.MotionVectorGenerationMode.ForceNoMotion
                ? UnityEngine.MotionVectorGenerationMode.Camera : UnityEngine.MotionVectorGenerationMode.ForceNoMotion;
            if (property == "occlusion") source.allowOcclusionWhenDynamic = !originalOcclusion;
            if (property == "quality") source.quality = originalQuality == SkinQuality.Bone1 ? SkinQuality.Bone4 : SkinQuality.Bone1;
            if (property == "lod") { nativeLods[0].enabled = true; nativeLods[0].fadeMode = LODFadeMode.CrossFade; }
            Render(eye);
            Check(!maskedAtCull && !lateSnapshot.HasPose && visibleSkins.All(skin => !skin.forceRenderingOff),
                "late native " + property + " edit declines the stale idle proxy before rendering");
            source.sortingOrder = originalOrder; source.motionVectorGenerationMode = originalMotion; source.quality = originalQuality;
            source.allowOcclusionWhenDynamic = originalOcclusion; nativeLods[0].enabled = false; nativeLods[0].fadeMode = LODFadeMode.None;
            Camera.onPreCull -= acquire; Camera.onPostRender -= release; lateSnapshot.Dispose();
        }
        PerfConfig.VisibleIdleClothApproximation = true;
        var nativeCloth = visibleSkins[0].gameObject.AddComponent<Cloth>();
        nativeCloth.enabled = false;
        ActorBarPose clothPose = ActorBarPose.Capture(root, head)!;
        Check(clothPose != null && clothPose.HasIdleCloth && clothPose.IsEventFreeNativeIdle(),
            "disabled original cloth permits complete idle pose sampling");
        var clothSnapshot = new ScenarioVisibleIdleSnapshot(clothPose!, host.transform);
        clothSnapshot.Tick(true, .5f); yield return null; clothSnapshot.AfterNativePose();
        Check(clothSnapshot.HasPose && clothPose!.CopyIdleSkinSources(ownedIdleSkins)
            && ownedIdleSkins.Contains(visibleSkins[0]),
            "disabled cloth body is included in the complete visible idle replacement");
        Color32[] originalClothPixels = ReadPixels(eye);
        clothSnapshot.BeforeCamera(true);
        Color32[] bakedClothPixels = ReadPixels(eye);
        Check(SameBodyPixels(bakedClothPixels, originalClothPixels),
            "disabled cloth complete pose pixels match the original body");
        clothSnapshot.Release();
        var clothRecord = new ScenarioIdleAnimationBudget.Record(actor, clothPose!, animator) { Visible = clothSnapshot };
        clothRecord.Tick(true, .5f);
        Check(clothRecord.Applied, "disabled original cloth retains the optional offscreen budget");
        nativeCloth.enabled = true; clothRecord.Tick(true, .5f);
        Check(!clothPose!.IsEventFreeNativeIdle() && !clothRecord.Applied
            && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "live native cloth activation restores original bone evaluation immediately");
        clothSnapshot.Dispose();

        // A live graphics switch disables an ALREADY simulated solver. Freeze only
        // this fixture's animation clock so displacement is actual cloth deformation,
        // then verify both the native frozen garment and the explicitly different
        // skeletal approximation. Frozen solver topology is welded (fewer particles
        // than source vertices), so assigning Cloth.vertices to the mesh is unsafe.
        // Production never resets physics vertices, coefficients or actor clocks.
        float originalSpeed = animator.speed;
        animator.speed = 0f;
        nativeCloth.useGravity = false;
        nativeCloth.externalAcceleration = new Vector3(0f, -15f, 0f);
        nativeCloth.damping = .1f;
        ClothSkinningCoefficient[] constraints = nativeCloth.coefficients;
        Vector3[] solverRest = nativeCloth.vertices;
        Check(constraints.Length > 10 && solverRest.Length == constraints.Length,
            "simulated garment has actual native solver vertices");
        float pinnedHeight = solverRest.Max(vertex => vertex.y) - .05f;
        for (int vertex = 0; vertex < constraints.Length; vertex++)
        {
            constraints[vertex].maxDistance = solverRest[vertex].y >= pinnedHeight ? 0f : .5f;
            constraints[vertex].collisionSphereDistance = 0f;
        }
        nativeCloth.coefficients = constraints;
        nativeCloth.enabled = true;
        for (int frame = 0; frame < 3; frame++) { yield return null; Render(eye); }
        Vector3[] solverBefore = nativeCloth.vertices;
        for (int frame = 0; frame < 30; frame++) { yield return null; Render(eye); }
        Vector3[] solverAfter = nativeCloth.vertices;
        File.WriteAllText(Path.Combine(evidence,name+"-simulated-cloth-topology.txt"),
            "meshVertices="+visibleSkins[0].sharedMesh.vertexCount+"; solverVertices="+solverAfter.Length
            +"; maxDisplacement="+solverAfter.Zip(solverBefore,(a,b)=>Vector3.Distance(a,b)).Max());
        Check(solverAfter.Zip(solverBefore, (after,before) => Vector3.Distance(after,before)).Max() > .005f,
            "actual native cloth simulates deformation before graphics disable");
        nativeCloth.enabled = false;
        Color32[] simulatedClothOriginal = ReadPixels(eye);
        Color32[] simulatedClothBaked;
        using (var frozenCloth = CaptureCurrentPoseForPixelCalibration(clothPose,host.transform))
        {
            frozenCloth.BeforeCamera(true);
            simulatedClothBaked = ReadPixels(eye);
            frozenCloth.Release();
        }
        int frozenBody = simulatedClothOriginal.Count(pixel=>pixel.g>128&&pixel.r<64);
        Check(frozenBody>10 && simulatedClothBaked.Count(pixel=>pixel.g>128&&pixel.r<64)>10
            && simulatedClothBaked.Zip(simulatedClothOriginal,(a,b)=>Math.Abs(a.g-b.g)>16).Count(value=>value)>frozenBody/20,
            "explicit disabled cloth approximation changes the frozen native solver shape");
        GameObject skeletalControl = FigureVisualMirror.CloneVisual(root,Vector3.zero,Quaternion.identity,
            Vector3.one,out FigureVisualMirror skeletalMirror);
        foreach (SkinnedMeshRenderer source in visibleSkins) source.forceRenderingOff=true;
        Color32[] skeletalOriginal = ReadPixels(eye);
        foreach (SkinnedMeshRenderer source in visibleSkins) source.forceRenderingOff=false;
        Object.DestroyImmediate(skeletalControl);
        Check(SameBodyPixels(simulatedClothBaked,skeletalOriginal),
            "explicit disabled cloth approximation keeps the complete original skeletal body");
        PerfConfig.VisibleIdleClothApproximation = false;
        using (var refusedCloth = new ScenarioVisibleIdleSnapshot(clothPose,host.transform))
        {
            refusedCloth.Tick(true,.5f);
            typeof(ScenarioVisibleIdleSnapshot).GetField("_sampleFrame",BindingFlags.NonPublic|BindingFlags.Instance)!
                .SetValue(refusedCloth,Time.frameCount-1);
            refusedCloth.AfterNativePose();
            refusedCloth.BeforeCamera(true);
            Color32[] preservedCloth = ReadPixels(eye);
            Check(!clothPose.IsEventFreeNativeIdle() && !refusedCloth.HasPose && !refusedCloth.IsMasked
                && SameBodyPixels(preservedCloth,simulatedClothOriginal),
                "disabled cloth option off preserves the full frozen native solver body");
        }
        PerfConfig.VisibleIdleClothApproximation = true;
        animator.speed=originalSpeed;
        nativeCloth.enabled = false;
        ScenarioIdleAnimationBudget.Install(host, () => false, () => .5f, () => eye);
        ScenarioIdleAnimationBudget.Register(actor,clothPose);
        var physicsDriver = host.GetComponent<ScenarioIdleAnimationBudget.Driver>();
        var physicsRecords = (Dictionary<ActorBehaviour,ScenarioIdleAnimationBudget.Record>)recordsField.GetValue(physicsDriver)!;
        float physicsBefore = Time.unscaledTime + 1.5f;
        while (!physicsRecords[actor].Visible!.HasPose && Time.unscaledTime < physicsBefore) { yield return null; Render(eye); }
        Check(physicsRecords[actor].Visible!.HasPose,"disabled cloth native driver prepares its complete real body");
        for(int frame=0;frame<3;frame++) { yield return null; Render(eye); }
        Quaternion approximateWing=wing.localRotation;
        float approximateClock=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        for(int frame=0;frame<6;frame++) { yield return null; Render(eye); }
        Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime>approximateClock
            && Quaternion.Angle(wing.localRotation,approximateWing)<.01f,
            "explicit disabled cloth native warm path skips bone writes while its clock advances");
        Render(eye);
        Check(PerfMonitor.Counts["Figure.VisibleIdleClothApproximation"]==1,
            "cloth approximation telemetry counts a real masked body");
        Camera.CameraCallback disableLateApproximation = camera => { if (camera == eye) PerfConfig.VisibleIdleClothApproximation=false; };
        Camera.onPreCull += disableLateApproximation;
        Color32[] lateApproximationOff = ReadPixels(eye);
        Camera.onPreCull -= disableLateApproximation;
        physicsDriver.HeadCamera = () => null;
        Color32[] exactApproximationOff = ReadPixels(eye);
        Check(SameBodyPixels(lateApproximationOff,exactApproximationOff)
            && animator.cullingMode==AnimatorCullingMode.AlwaysAnimate && visibleSkins.All(skin=>!skin.forceRenderingOff)
            && !physicsRecords[actor].Applied && PerfMonitor.Counts["Figure.VisibleIdleClothApproximation"]==0,
            "live cloth approximation off restores exact current-camera native body and original mode");
        PerfConfig.VisibleIdleClothApproximation=true; physicsDriver.HeadCamera=()=>eye;
        physicsBefore=Time.unscaledTime+1.5f;
        while (!physicsRecords[actor].Visible!.HasPose && Time.unscaledTime<physicsBefore) { yield return null; Render(eye); }
        Check(physicsRecords[actor].Visible!.HasPose,"explicit cloth approximation recovers after a live off toggle");
        Camera.CameraCallback enableLateCloth = camera => { if (camera == eye) nativeCloth.enabled = true; };
        Camera.onPreCull += enableLateCloth;
        Color32[] latePhysicsActual = ReadPixels(eye);
        Camera.onPreCull -= enableLateCloth;
        physicsDriver.HeadCamera = () => null;
        Color32[] latePhysicsExpected = ReadPixels(eye);
        Check(latePhysicsExpected.Count(pixel=>pixel.g>128&&pixel.r<64)>10 && SameBodyPixels(latePhysicsActual,latePhysicsExpected)
            && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate && visibleSkins.All(skin=>!skin.forceRenderingOff),
            "late native cloth activation retains actual original body pixels before camera culling");
        Check(PerfMonitor.Counts["Figure.VisibleIdleClothApproximation"]==0,
            "cloth approximation telemetry excludes actual native physics fallback");
        ScenarioIdleAnimationBudget.Shutdown(); yield return null;
        animator.speed=originalSpeed; PerfConfig.VisibleIdleClothApproximation=false;
        Object.DestroyImmediate(nativeCloth);
        Object.DestroyImmediate(eye.targetTexture); Object.DestroyImmediate(eyeObject);

        // An eventful idle is deliberately NOT admitted, even though Unity continues
        // the state machine. Its callback may depend on the precise live bone position.
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/" + name + "Events.controller");
        AnimationClip eventClip = Object.Instantiate(original.runtimeAnimatorController.animationClips.First(c => c.isLooping));
        eventClip.events = new[] { new AnimationEvent { time = eventClip.length * 0.2f, functionName = "OnNativeReceipt" } };
        controller.layers[0].stateMachine.AddState("SleepIdle").motion = eventClip;
        animator.runtimeAnimatorController = controller; animator.Rebind(); animator.Play("SleepIdle", 0, 0); animator.Update(0.001f);
        ActorBarPose eventPose = ActorBarPose.Capture(root, head)!;
        Check(eventPose.SparseEligible, "event-bearing fixture remains a structurally safe native rig");
        Check(!eventPose.IsEventFreeNativeIdle(), "native eventful idle remains fully evaluated");
        record = new ScenarioIdleAnimationBudget.Record(actor, eventPose, animator); record.Tick(true);
        Check(!record.Applied, "event-bearing state is never transformed on a reduced budget");
        var eventReceipt = animator.gameObject.AddComponent<NativeEventReceipt>();
        Check(MonoScript.FromMonoBehaviour(eventReceipt) != null, "event receipt is an imported Unity MonoScript");
        var nativeEventFrames = new List<string>();
        for (int sample = 0; sample < 5; sample++)
        {
            animator.Update(animator.GetCurrentAnimatorStateInfo(0).length * 0.25f); yield return null;
            nativeEventFrames.Add("phase=" + animator.GetCurrentAnimatorStateInfo(0).normalizedTime
                + "; stateLength=" + animator.GetCurrentAnimatorStateInfo(0).length
                + "; clip=" + eventClip.name + "; length=" + eventClip.length + "; speed=" + animator.speed
                + "; events=" + eventClip.events.Length + "; enabled=" + animator.enabled + "; fireEvents=" + animator.fireEvents
                + "; receipt=" + eventReceipt.Events + "; mode=" + animator.cullingMode);
        }
        File.WriteAllLines(Path.Combine(evidence, name + "-native-event-frames.txt"), nativeEventFrames);
        Check(eventReceipt.Events > 0, "actual native animation event callback remains delivered");
        Object.DestroyImmediate(eventReceipt);

        // Fault the actual production owner through a temporary Harmony prefix. The real
        // native MF method must still run and optional mode ownership must be restored.
        animator.runtimeAnimatorController = graph; animator.Rebind(); animator.Play("SleepIdle", 0, 0.2f); animator.Update(0.001f);
        ActorBarPose faultPose = ActorBarPose.Capture(root, head)!;
        ScenarioIdleAnimationBudget.Install(host, () => true);
        ScenarioIdleAnimationBudget.Register(actor, faultPose);
        yield return null;
        Check(animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms, "fault fixture begins with an actual owned reduction");
        var fault = new Harmony("ghvr.nativeActorAudit.fault");
        fault.Patch(typeof(ScenarioIdleAnimationBudget.Driver).GetMethod("NativeAction", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(Animator) }, null), prefix: new HarmonyMethod(typeof(InteractionProgram), nameof(InjectOptionalFault)));
        actorEvents.ClearActorEventState();
        Check(MF.AnimatorPlay(animator, "WakeUp"), "optional owner fault never escapes or blocks native action dispatch");
        Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate && VRLog.BudgetFailures == 1,
            "optional owner fault restores original mode and reports once");
        animator.Update(0.001f);
        Check(actorEvents.ReceivedEvent(ActorEvents.ActorEvent.ProgressChoreographer), "continuation callback survives optional owner fault");
        Check(MF.AnimatorPlay(animator, "SleepIdle"), "subsequent native dispatch remains available after optional owner fault");
        animator.Update(0.001f); yield return null;
        Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate && VRLog.BudgetFailures == 1,
            "failed optional owner remains disabled without repeated diagnostic stream");
        fault.UnpatchSelf(); ScenarioIdleAnimationBudget.Shutdown();
        Metrics = "publisherScripts=" + componentReceipts.Count + "; bones=" + pose.BoneCount
            + "; sparseSkips=" + pose.SkippedVerificationCount + "; originalClockAndBoneCullVerified=true";
        ActorBarPose.Reset(); VRSession.Harmony.UnpatchSelf();
        Object.DestroyImmediate(root); Object.DestroyImmediate(host); bundle.Unload(true);
    }
}
