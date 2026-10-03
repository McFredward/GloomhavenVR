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
    [Serializable] private sealed class NativeComponents { public NativeComponent[] components = Array.Empty<NativeComponent>(); }
    [Serializable] private sealed class NativeComponent { public string assembly = "", type = "", path = ""; public NativeField[] fields = Array.Empty<NativeField>(); public bool enabled = true; }
    [Serializable] private sealed class NativeField { public string name = "", value = ""; }
    public static int Checks;
    public static string Metrics = "";
    private static string Arg(string key)
    { string[] args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, key) + 1]; }
    private static void Check(bool condition, string label)
    { Checks++; if (!condition) throw new Exception(label); }
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
        var unknownState = graph.layers[0].stateMachine.states.First(x => x.state.name == "SleepIdle").state
            .AddStateMachineBehaviour<UnknownStateBoneWriter>();
        animator.Rebind(); animator.Play("SleepIdle", 0, 0.2f); animator.Update(0.001f);
        Check(!ActorBarPose.Capture(root, head)!.IsEventFreeNativeIdle(),
            "unknown current native state callback cancels transform shortcut");
        Object.DestroyImmediate(unknownState);
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
        Metrics = "publisherScripts=" + componentReceipts.Count + "; bones=" + pose.BoneCount
            + "; sparseSkips=" + pose.SkippedVerificationCount + "; originalClockAndBoneCullVerified=true";
        ActorBarPose.Reset(); VRSession.Harmony.UnpatchSelf();
        Object.DestroyImmediate(root); Object.DestroyImmediate(host); bundle.Unload(true);
    }
}
