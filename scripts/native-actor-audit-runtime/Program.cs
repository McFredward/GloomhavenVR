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
        ActorPropBody.Held = actor; record.Tick(true);
        Check(!record.Applied, "prop-held native actor stays fully evaluated");
        ActorPropBody.Held = null;
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

        // Keep a native body visible for successive real Unity frames while the separate
        // offscreen setting is enabled. Original LOD renderers and every evaluated pose
        // remain live: no bake, proxy, body mask or substituted LOD table exists.
        LODGroup[] nativeLods = root.GetComponentsInChildren<LODGroup>(true);
        Check(nativeLods.Any(group => group.enabled && group.GetLODs().Length >= 3),
            "publisher rig retains real active three-level LOD topology");
        LOD[][] nativeTables = nativeLods.Select(group => group.GetLODs()).ToArray();
        foreach (LODGroup group in nativeLods) group.ForceLOD(0);
        var eyeObject = new GameObject("ContinuousNativeIdleEye");
        var eye = eyeObject.AddComponent<Camera>(); eye.enabled = true;
        eye.transform.position = new Vector3(0f, 2f, -8f);
        eye.transform.LookAt(root.transform.position + Vector3.up);
        eye.orthographic = true; eye.orthographicSize = 5f;
        eye.clearFlags = CameraClearFlags.SolidColor; eye.backgroundColor = Color.black;
        eye.targetTexture = new RenderTexture(256, 256, 24); eye.targetTexture.Create();
        SkinnedMeshRenderer[] nativeSkins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
        foreach (SkinnedMeshRenderer skin in nativeSkins)
        {
            Material[] materials = skin.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
                materials[index] = new Material(Shader.Find("Unlit/Color")) { color = Color.green };
            skin.sharedMaterials = materials;
        }
        Mesh[] originalMeshes = nativeSkins.Select(skin => skin.sharedMesh).ToArray();
        Material[][] originalSlots = nativeSkins.Select(skin => skin.sharedMaterials).ToArray();
        animator.Play("Idle-Run", 0, .1f); animator.Update(.001f);
        ActorBarPose visiblePose = ActorBarPose.Capture(root, head)!;
        ScenarioIdleAnimationBudget.Install(host, () => enabled);
        ScenarioIdleAnimationBudget.Register(actor, visiblePose); enabled = true;
        Render(eye); yield return null; Render(eye); yield return null;
        var nativeFramePoses = new List<string>();
        Transform[] visibleBones = nativeSkins.SelectMany(skin => skin.bones).Distinct().ToArray();
        for (int frame = 0; frame < 8; frame++)
        {
            Color32[] budgetPixels = ReadPixels(eye);
            Check(budgetPixels.Count(pixel => pixel.g > 128 && pixel.r < 64) > 10,
                "visible original idle has actual native body pixels");
            AnimatorCullingMode budgetMode = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Color32[] originalPixels = ReadPixels(eye);
            animator.cullingMode = budgetMode;
            Check(SameBodyPixels(budgetPixels, originalPixels),
                "offscreen option preserves actual same-pose native visible pixels");
            Check(nativeSkins.Select((skin,index) => skin.sharedMesh == originalMeshes[index]
                && skin.sharedMaterials.SequenceEqual(originalSlots[index]) && !skin.forceRenderingOff).All(value => value)
                && nativeLods.Select((group,index) => SameLodTable(group.GetLODs(),nativeTables[index])).All(value => value),
                "visible idle keeps original meshes materials masks and native LOD tables");
            float previousPhase = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            Quaternion previousWing = wing.localRotation;
            Quaternion[] previousBones = visibleBones.Select(bone => bone.localRotation).ToArray();
            Vector3[] previousPositions = visibleBones.Select(bone => bone.localPosition).ToArray();
            Render(eye); yield return null;
            Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > previousPhase,
                "visible idle advances its original clock every real Unity frame");
            File.AppendAllText(Path.Combine(evidence,name + "-continuous-native-diagnostic.txt"),
                "frame=" + Time.frameCount + "; previousPhase=" + previousPhase + "; phase=" + animator.GetCurrentAnimatorStateInfo(0).normalizedTime
                + "; dt=" + Time.deltaTime + "; mode=" + animator.cullingMode + "; angle=" + Quaternion.Angle(wing.localRotation,previousWing)
                + "; visible=" + string.Join(",",nativeSkins.Select(skin => skin.name + ":" + skin.isVisible)) + "\n");
            // A wing can reach an authored turning point and Quaternion.Angle rounds
            // tiny changes to zero. Observe the complete original evaluated skeleton.
            Check(visibleBones.Select((bone,index) => bone.localRotation.x != previousBones[index].x
                || bone.localRotation.y != previousBones[index].y || bone.localRotation.z != previousBones[index].z
                || bone.localRotation.w != previousBones[index].w || bone.localPosition != previousPositions[index]).Any(value => value),
                "visible idle evaluates original native bones every real Unity frame");
            nativeFramePoses.Add("frame=" + Time.frameCount + "; phase=" + animator.GetCurrentAnimatorStateInfo(0).normalizedTime
                + "; wing=" + wing.localRotation + "; culling=" + animator.cullingMode);
        }
        File.WriteAllLines(Path.Combine(evidence,name + "-continuous-native-idle.txt"),nativeFramePoses);
        Check(!host.GetComponentsInChildren<MeshRenderer>(true).Any(),
            "offscreen animation owner creates no sampled private renderers");
        host.SetActive(false);
        Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "inactive idle host restores exact original mode synchronously");
        host.SetActive(true); yield return null;
        ScenarioIdleAnimationBudget.Shutdown(); yield return null;
        Object.DestroyImmediate(eye.targetTexture); Object.DestroyImmediate(eyeObject);

        // Native cloth is never replaced with a skeletal sampled shape. An active solver
        // still prevents offscreen transform suppression, and a late enable restores it.
        Cloth nativeCloth = nativeSkins.First(skin => skin.sharedMesh != null && skin.sharedMesh.vertexCount > 0)
            .gameObject.AddComponent<Cloth>();
        nativeCloth.enabled = false;
        ActorBarPose clothPose = ActorBarPose.Capture(root,head)!;
        Check(clothPose.IsEventFreeNativeIdle(), "disabled native cloth retains its original unsampled surface");
        var clothRecord = new ScenarioIdleAnimationBudget.Record(actor,clothPose,animator);
        clothRecord.Tick(true);
        Check(clothRecord.Applied,"disabled native cloth permits independent offscreen culling without geometry replacement");
        nativeCloth.enabled = true; clothRecord.Tick(true);
        Check(!clothRecord.Applied && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "live native cloth activation restores original bone evaluation immediately");
        Object.DestroyImmediate(nativeCloth);

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
