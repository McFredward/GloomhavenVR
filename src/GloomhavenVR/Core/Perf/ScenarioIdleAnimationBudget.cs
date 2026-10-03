using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Optional offscreen transform-work reduction on prepared, audited native idle rigs.
/// Unity's CullUpdateTransforms advances the original state machine and root-motion clock;
/// unlike CullCompletely or disabling Animator it does not stop native continuation. Only an
/// original event-free idle loop is admitted. Native action dispatch restores before Play,
/// locomotion restores before its command, and holding/state/controller changes are immediate.
/// No distance-dependent NPC clocks, manual animation stepping, gameplay writes or new replicas.
/// </summary>
internal static class ScenarioIdleAnimationBudget
{
    private static Driver? _driver;
    internal static void Install(GameObject host, Func<bool> enabled)
    {
        if (_driver != null) return;
        _driver = host.AddComponent<Driver>(); _driver.Enabled = enabled;
        PerfMonitor.Register("Figure.IdleTransformCull"); PerfMonitor.Register("Figure.IdleTracked");
        PerfMonitor.Register("Figure.IdleOriginalAlways"); PerfMonitor.Register("Figure.IdleAuthoredCull");
        try
        {
            VRSession.Harmony?.PatchAll(typeof(ScenarioIdleAnimationPlayPatch));
            VRSession.Harmony?.PatchAll(typeof(ScenarioIdleAnimationLocomotionPatch));
        }
        catch (Exception error)
        {
            // Without the synchronous native action seam, fail open to original AlwaysAnimate.
            _driver.NativeActionsReady = false;
            VRLog.Note("Perf", "Idle animation native action hook unavailable (" + error.GetType().Name
                + "); original transform evaluation retained.");
        }
    }

    internal static void Register(ActorBehaviour? actor, ActorBarPose? pose)
    { if (actor != null && pose != null) _driver?.Register(actor, pose); }
    internal static void Release(ActorBehaviour? actor)
    { if (actor != null) _driver?.Release(actor); }
    internal static void NativeAction(Animator? animator)
    { if (animator != null) _driver?.NativeAction(animator); }
    internal static void NativeAction(ActorBehaviour? actor)
    { if (actor != null) _driver?.NativeAction(actor); }
    internal static void Shutdown()
    {
        if (_driver == null) return;
        _driver.RestoreAll(); UnityEngine.Object.Destroy(_driver); _driver = null;
    }

    internal sealed class Record
    {
        internal readonly ActorBehaviour Actor;
        internal readonly ActorBarPose Pose;
        internal readonly Animator Animator;
        internal readonly AnimatorCullingMode Original;
        internal bool Applied, Foreign;
        internal int ResumeAfterFrame;
        internal Record(ActorBehaviour actor, ActorBarPose pose, Animator animator)
        { Actor = actor; Pose = pose; Animator = animator; Original = animator.cullingMode; }

        internal bool Tick(bool enabled)
        {
            if (Animator == null || Actor == null) { Restore(); return false; }
            if (Applied && Animator.cullingMode != AnimatorCullingMode.CullUpdateTransforms)
            { Applied = false; Foreign = true; }
            bool eligible = enabled && !Foreign && Original == AnimatorCullingMode.AlwaysAnimate
                && Actor.gameObject.activeInHierarchy && !Actor.IsMoving
                && !HeldFigures.Owns(Actor) && !NetHeldFigures.Owns(Actor) && !ActorPropBody.IsHeld(Actor)
                && Time.frameCount >= ResumeAfterFrame && Pose.IsEventFreeNativeIdle();
            if (!eligible) { Restore(); return true; }
            if (!Applied)
            {
                // Retain changes made by another owner before our first write too.
                if (Animator.cullingMode != Original) { Foreign = true; return true; }
                Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; Applied = true;
            }
            return true;
        }
        internal void Resume()
        { Restore(); ResumeAfterFrame = Time.frameCount + 2; }
        internal void Restore()
        {
            if (Applied && Animator != null && Animator.cullingMode == AnimatorCullingMode.CullUpdateTransforms)
                Animator.cullingMode = Original;
            Applied = false;
        }
    }

    // Run after ordinary native Update callbacks, before Unity evaluates Animator. Explicit
    // Play/locomotion hooks are synchronous; a late procedural/global clock is never delayed.
    [DefaultExecutionOrder(30000)]
    internal sealed class Driver : MonoBehaviour
    {
        internal Func<bool> Enabled = () => false;
        internal bool NativeActionsReady = VRSession.Harmony != null;
        private readonly Dictionary<ActorBehaviour, Record> _records = new();
        private readonly List<ActorBehaviour> _dead = new(8);
        internal void Register(ActorBehaviour actor, ActorBarPose pose)
        {
            if (_records.TryGetValue(actor, out Record current) && current.Pose == pose) return;
            Release(actor);
            Animator? animator = pose.NativeAnimator;
            if (animator != null) _records.Add(actor, new Record(actor, pose, animator));
        }
        internal void Release(ActorBehaviour actor)
        { if (_records.TryGetValue(actor, out Record current)) { current.Restore(); _records.Remove(actor); } }
        internal void NativeAction(Animator animator)
        { foreach (Record record in _records.Values) if (record.Animator == animator) record.Resume(); }
        internal void NativeAction(ActorBehaviour actor)
        { if (_records.TryGetValue(actor, out Record record)) record.Resume(); }
        internal void RestoreAll()
        { foreach (Record record in _records.Values) record.Restore(); _records.Clear(); }
        private void OnDestroy() => RestoreAll();
        private void Update()
        {
            bool enabled = VRSession.IsRunning && NativeActionsReady && Enabled();
            int applied = 0, originalAlways = 0, authoredCull = 0;
            using (PerfMonitor.Scope("ScenarioIdleAnimation"))
            {
                foreach (KeyValuePair<ActorBehaviour, Record> item in _records)
                {
                    if (!item.Value.Tick(enabled)) _dead.Add(item.Key);
                    if (item.Value.Applied) applied++;
                    if (item.Value.Original == AnimatorCullingMode.AlwaysAnimate) originalAlways++;
                    if (item.Value.Original == AnimatorCullingMode.CullUpdateTransforms) authoredCull++;
                }
                foreach (ActorBehaviour actor in _dead) _records.Remove(actor);
                _dead.Clear();
            }
            PerfMonitor.Count("Figure.IdleTransformCull", applied);
            PerfMonitor.Count("Figure.IdleTracked", _records.Count);
            PerfMonitor.Count("Figure.IdleOriginalAlways", originalAlways);
            PerfMonitor.Count("Figure.IdleAuthoredCull", authoredCull);
        }
    }
}

[HarmonyPatch(typeof(MF), nameof(MF.AnimatorPlay))]
internal static class ScenarioIdleAnimationPlayPatch
{
    [HarmonyPrefix]
    private static void Prefix(Animator animator) => ScenarioIdleAnimationBudget.NativeAction(animator);
}

[HarmonyPatch]
internal static class ScenarioIdleAnimationLocomotionPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string method in new[] { nameof(ActorBehaviour.SetLocoTarget), nameof(ActorBehaviour.PushPullToLocation),
            nameof(ActorBehaviour.TeleportToLocation), nameof(ActorBehaviour.ForceSetLocoIntermediateTarget) })
        {
            MethodInfo? target = AccessTools.Method(typeof(ActorBehaviour), method);
            if (target == null) throw new MissingMethodException(typeof(ActorBehaviour).FullName, method);
            yield return target;
        }
    }
    [HarmonyPrefix]
    private static void Prefix(ActorBehaviour __instance) => ScenarioIdleAnimationBudget.NativeAction(__instance);
}
