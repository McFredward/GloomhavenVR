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
    private static string Arg(string key)
    { string[] args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, key) + 1]; }
    private static void Check(bool condition, string label)
    { Checks++; if (!condition) throw new Exception(label); }
    private static void InjectOptionalFault() => throw new InvalidOperationException("Injected optional-owner lifetime fault");
    private static Color32[] ReadPixels(Camera eye)
    {
        eye.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = eye.targetTexture;
        var image = new Texture2D(128,128,TextureFormat.RGBA32,false);
        image.ReadPixels(new Rect(0,0,128,128),0,0); image.Apply();
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
        foreach (LODGroup group in nativeLods) group.enabled = false;

        // Run the real optional visible-idle owner against native publisher geometry,
        // clips and camera callbacks. No Animator timing or renderer visibility stub.
        var eyeObject = new GameObject("VisibleIdleEye");
        var eye = eyeObject.AddComponent<Camera>(); eye.enabled = false;
        eye.transform.position = new Vector3(0f, 2f, -8f);
        eye.transform.LookAt(root.transform.position + Vector3.up);
        eye.orthographic = true; eye.orthographicSize = 5f;
        eye.clearFlags = CameraClearFlags.SolidColor; eye.backgroundColor = Color.black;
        eye.targetTexture = new RenderTexture(128, 128, 24); eye.targetTexture.Create();
        SkinnedMeshRenderer[] visibleSkins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
        foreach (SkinnedMeshRenderer skin in visibleSkins)
        {
            Material[] materials = skin.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
                materials[index] = new Material(Shader.Find("Unlit/Color")) { color = Color.green };
            skin.sharedMaterials = materials;
        }
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
            yield return null; eye.Render();
            Check(visibleSkins.All(skin => !skin.forceRenderingOff),
                "visible idle masks restore after each real camera");
            Check(visibleSkins.Select((skin,index) => skin.enabled && skin.sharedMesh==originalMeshes[index]).All(value => value),
                "visible idle retains native enabled state and mesh identity during culling");
        }
        Check(visibleRecord.Visible!.HasPose && sawLease, "real native camera admits an actual private visible idle pose");
        Check(visibleRecord.Applied && animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms,
            "visible pose option is independent of offscreen option");
        Color32[] proxyPixels = ReadPixels(eye);
        Check(proxyPixels.Count(pixel => pixel.g > 128 && pixel.r < 64) > 10,
            "visible idle proxy retains visible original skin pixels");
        // Render the original immediately without advancing Animator: same bone pose,
        // same native material references, separate GPU-skinned versus baked routes.
        admittedEye = null;
        Color32[] originalPixels = ReadPixels(eye);
        int differentPixels = proxyPixels.Zip(originalPixels, (a,b) => Math.Abs(a.g-b.g)>16).Count(value => value);
        Check(differentPixels < 128*128/50, "private visible idle silhouette matches native same-pose drawing");
        admittedEye = eye;
        // Warm visibility after the original control; then demonstrate that transforms
        // stop between samples while the real native state clock continues.
        for (int frame=0; frame<4; frame++) { yield return null; eye.Render(); }
        Quaternion frozenWing = wing.localRotation;
        float frozenClock = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        for (int frame=0; frame<8; frame++) { yield return null; eye.Render(); }
        Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > frozenClock,
            "visible idle keeps the real native state clock advancing");
        Check(Quaternion.Angle(wing.localRotation, frozenWing) < .01f,
            "visible idle camera lease suppresses actual native bone writes between samples");
        // A native consumer of exact Renderer identities must get originals.
        var nativeBuffer = new UnityEngine.Rendering.CommandBuffer { name = "native-renderer-consumer" };
        nativeBuffer.DrawRenderer(visibleSkins[0], visibleSkins[0].sharedMaterial);
        eye.AddCommandBuffer(UnityEngine.Rendering.CameraEvent.BeforeForwardOpaque, nativeBuffer);
        eye.Render();
        Check(!visibleRecord.Visible.IsMasked && visibleSkins.All(skin => !skin.forceRenderingOff),
            "native command-buffer consumers retain original figure renderers");
        eye.RemoveCommandBuffer(UnityEngine.Rendering.CameraEvent.BeforeForwardOpaque, nativeBuffer); nativeBuffer.Dispose();
        // An action begins inside an already acquired camera lease: restoration must
        // be synchronous, not postponed until the next Update or PostRender.
        invokeActionInsideLease = true; eye.Render();
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
            yield return null; eye.Render();
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
        host.SetActive(false); eye.Render();
        Check(records.Values.All(item => !item.Applied && item.Visible?.IsMasked == false)
            && crowd.SelectMany(item => item.GetComponentsInChildren<SkinnedMeshRenderer>(true)).All(skin => !skin.forceRenderingOff),
            "inactive idle host retains original native modes and visible skins across actual camera callbacks");
        host.SetActive(true);
        ScenarioIdleAnimationBudget.Shutdown();
        foreach (GameObject clone in crowd) Object.DestroyImmediate(clone);

        // Observe late native renderer writes through the real camera callback boundary.
        // Each case bakes actual publisher skin once, then changes one original flag
        // before the next camera. A stale proxy must decline before the source is masked.
        foreach (string property in new[] { "sorting", "motion", "occlusion", "lod" })
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
            eye.Render();
            Check(maskedAtCull && visibleSkins.All(skin => !skin.forceRenderingOff),
                "late renderer fixture starts from a restored native camera lease");
            SkinnedMeshRenderer source = visibleSkins[0];
            int originalOrder = source.sortingOrder;
            var originalMotion = source.motionVectorGenerationMode;
            bool originalOcclusion = source.allowOcclusionWhenDynamic;
            if (property == "sorting") source.sortingOrder = originalOrder + 7;
            if (property == "motion") source.motionVectorGenerationMode = originalMotion == UnityEngine.MotionVectorGenerationMode.ForceNoMotion
                ? UnityEngine.MotionVectorGenerationMode.Camera : UnityEngine.MotionVectorGenerationMode.ForceNoMotion;
            if (property == "occlusion") source.allowOcclusionWhenDynamic = !originalOcclusion;
            if (property == "lod") nativeLods[0].enabled = true;
            eye.Render();
            Check(!maskedAtCull && !lateSnapshot.HasPose && visibleSkins.All(skin => !skin.forceRenderingOff),
                "late native " + property + " edit declines the stale idle proxy before rendering");
            source.sortingOrder = originalOrder; source.motionVectorGenerationMode = originalMotion;
            source.allowOcclusionWhenDynamic = originalOcclusion; nativeLods[0].enabled = false;
            Camera.onPreCull -= acquire; Camera.onPostRender -= release; lateSnapshot.Dispose();
        }
        var nativeCloth = visibleSkins[0].gameObject.AddComponent<Cloth>();
        nativeCloth.enabled = false;
        ActorBarPose clothPose = ActorBarPose.Capture(root, head)!;
        Check(clothPose != null && clothPose.HasIdleCloth && clothPose.IsEventFreeNativeIdle(),
            "disabled original cloth permits only the independent offscreen idle path");
        var clothSnapshot = new ScenarioVisibleIdleSnapshot(clothPose!, host.transform);
        clothSnapshot.Tick(true, .5f);
        Check(!clothSnapshot.AwaitingNativePose && !clothSnapshot.HasPose,
            "cloth body never enters a partial visible idle replacement");
        var clothRecord = new ScenarioIdleAnimationBudget.Record(actor, clothPose!, animator) { Visible = clothSnapshot };
        clothRecord.Tick(true);
        Check(clothRecord.Applied, "disabled original cloth retains the optional offscreen budget");
        nativeCloth.enabled = true; clothRecord.Tick(true);
        Check(!clothPose!.IsEventFreeNativeIdle() && !clothRecord.Applied
            && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "live native cloth activation restores original bone evaluation immediately");
        clothSnapshot.Dispose(); Object.DestroyImmediate(nativeCloth);
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
