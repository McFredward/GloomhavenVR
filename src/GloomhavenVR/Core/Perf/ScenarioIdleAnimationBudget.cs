using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private static bool _faultReported;
    internal static void Install(GameObject host, Func<bool> enabled,
        Func<float>? visibleInterval = null, Func<Camera?>? headCamera = null,
        Func<Camera, bool>? nativeCameraConsumers = null)
    {
        if (_driver != null) return;
        _faultReported = false;
        _driver = host.AddComponent<Driver>(); _driver.Enabled = enabled;
        _driver.VisibleInterval = visibleInterval ?? (() => 0f);
        _driver.HeadCamera = headCamera ?? (() => null);
        _driver.NativeCameraConsumers = nativeCameraConsumers ?? (camera => camera.commandBufferCount > 0);
        PerfMonitor.Register("Figure.IdleTransformCull"); PerfMonitor.Register("Figure.IdleTracked");
        PerfMonitor.Register("Figure.IdleOriginalAlways"); PerfMonitor.Register("Figure.IdleAuthoredCull");
        PerfMonitor.Register("Figure.VisibleIdleBakes");
        PerfMonitor.Register("Figure.VisibleIdleSampling");
        PerfMonitor.Register("Figure.VisibleIdleSources");
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
    internal static void BeforeNativeContentChange()
    {
        try { _driver?.ReleaseCameraMasks(); }
        catch (Exception error) { FailOpen(error); }
    }
    internal static void NativeAction(Animator? animator)
    {
        try { if (animator != null && _driver != null && _driver.NativeActionsReady) _driver.NativeAction(animator); }
        catch (Exception error) { FailOpen(error); }
    }
    internal static void NativeAction(ActorBehaviour? actor)
    {
        try { if (actor != null && _driver != null && _driver.NativeActionsReady) _driver.NativeAction(actor); }
        catch (Exception error) { FailOpen(error); }
    }

    private static void FailOpen(Exception error)
    {
        // This helper is called inside a native Harmony prefix as well as Update. Even an
        // invalid Unity lifetime or foreign callback during restoration must never escape
        // into the game's action dispatch; disable this optional owner until reinstall.
        try
        {
            if (_driver != null) { _driver.NativeActionsReady = false; _driver.RestoreAllSafely(); }
        }
        catch { /* Native continuation takes precedence over optional cleanup. */ }
        if (_faultReported) return;
        _faultReported = true;
        try
        {
            VRLog.Note("Perf", "Idle animation budget failed (" + error.GetType().Name
                + "); optional transform reduction disabled and original modes restored where live.");
        }
        catch { /* A disposed logger cannot gate native continuation either. */ }
    }
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
        internal ScenarioVisibleIdleSnapshot? Visible;
        internal float VisibleInterval;
        internal Record(ActorBehaviour actor, ActorBarPose pose, Animator animator)
        { Actor = actor; Pose = pose; Animator = animator; Original = animator.cullingMode; }

        internal bool Tick(bool enabled, float visibleInterval = 0f, bool scheduleVisible = true)
        {
            if (Animator == null || Actor == null) { Restore(); return false; }
            if (Applied && Animator.cullingMode != AnimatorCullingMode.CullUpdateTransforms)
            { Applied = false; Foreign = true; }
            bool eligible = (enabled || visibleInterval > 0f) && !Foreign && Original == AnimatorCullingMode.AlwaysAnimate
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
            VisibleInterval = visibleInterval;
            Visible?.Tick(true, visibleInterval, scheduleVisible);
            return true;
        }
        internal void Resume()
        { Restore(); ResumeAfterFrame = Time.frameCount + 2; }
        internal void Restore()
        {
            VisibleInterval = 0f;
            Visible?.Tick(false, 0f);
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
        internal Func<float> VisibleInterval = () => 0f;
        internal Func<Camera?> HeadCamera = () => null;
        internal Func<Camera, bool> NativeCameraConsumers = camera => camera.commandBufferCount > 0;
        internal bool NativeActionsReady = VRSession.Harmony != null;
        private readonly Dictionary<ActorBehaviour, Record> _records = new();
        private readonly List<Record> _bakeOrder = new(32);
        private int _nextBakeIndex;
        private int _nextRequestIndex;
        private readonly List<ActorBehaviour> _dead = new(8);
        private readonly Stack<bool> _cameraPolicies = new();
        private void Awake()
        { Camera.onPreCull += BeforeCamera; Camera.onPostRender += AfterCamera; }
        internal void Register(ActorBehaviour actor, ActorBarPose pose)
        {
            if (_records.TryGetValue(actor, out Record current) && current.Pose == pose) return;
            Release(actor);
            Animator? animator = pose.NativeAnimator;
            if (animator != null)
            {
                var record = new Record(actor, pose, animator)
                    { Visible = new ScenarioVisibleIdleSnapshot(pose, transform) };
                _records.Add(actor, record); _bakeOrder.Add(record);
            }
        }
        internal void Release(ActorBehaviour actor)
        { if (_records.TryGetValue(actor, out Record current)) { current.Restore(); current.Visible?.Dispose(); _records.Remove(actor); _bakeOrder.Remove(current); } }
        internal void ReleaseCameraMasks()
        {
            foreach (Record record in _records.Values) record.Visible?.Release();
            _cameraPolicies.Clear();
        }
        internal void NativeAction(Animator animator)
        { foreach (Record record in _records.Values) if (record.Animator == animator) record.Resume(); }
        internal void NativeAction(ActorBehaviour actor)
        { if (_records.TryGetValue(actor, out Record record)) record.Resume(); }
        internal void RestoreAll()
        { foreach (Record record in _records.Values) { record.Restore(); record.Visible?.Dispose(); } _records.Clear(); _bakeOrder.Clear(); _cameraPolicies.Clear(); }
        internal void RestoreAllSafely()
        {
            foreach (Record record in _records.Values)
                try { record.Restore(); record.Visible?.Dispose(); } catch { /* Continue restoring other live owned actors. */ }
            _records.Clear();
            _bakeOrder.Clear();
            _cameraPolicies.Clear();
        }
        private void OnDestroy()
        { Camera.onPreCull -= BeforeCamera; Camera.onPostRender -= AfterCamera; RestoreAll(); }
        private void OnDisable()
        {
            try { ReleaseCameraMasks(); foreach (Record record in _records.Values) record.Restore(); }
            catch (Exception error) { FailOpen(error); }
        }
        private void BeforeCamera(Camera camera)
        {
            try
            {
                bool admitted = isActiveAndEnabled && VRSession.IsRunning && NativeActionsReady && camera == HeadCamera()
                    && camera != null && !NativeCameraConsumers(camera);
                _cameraPolicies.Push(admitted);
                foreach (Record record in _records.Values)
                {
                    // A late local/remote grab can occur after this owner's Update. The
                    // camera admission must read those live native ownership gates too.
                    if (record.Actor != null && (record.Actor.IsMoving || HeldFigures.Owns(record.Actor)
                        || NetHeldFigures.Owns(record.Actor) || ActorPropBody.IsHeld(record.Actor))) record.Resume();
                    record.Visible?.BeforeCamera(admitted && record.Applied && record.VisibleInterval > 0f);
                }
            }
            catch (Exception error) { FailOpen(error); }
        }
        private void AfterCamera(Camera camera)
        {
            try
            {
                // Attribute only masks still owned after the actual camera; a native
                // action/content callback may have revoked a planned substitute earlier.
                int masked = 0;
                foreach (Record record in _records.Values) masked += record.Visible?.MaskedSurfaceCount ?? 0;
                PerfMonitor.Count("Figure.VisibleIdleSources", masked);
                if (_cameraPolicies.Count > 0) _cameraPolicies.Pop();
                bool restoreOuter = _cameraPolicies.Count > 0 && _cameraPolicies.Peek();
                foreach (Record record in _records.Values)
                    record.Visible?.BeforeCamera(restoreOuter && record.Applied && record.VisibleInterval > 0f);
            }
            catch (Exception error) { FailOpen(error); }
        }
        private void LateUpdate()
        {
            try
            {
                // Every due actor remains on its original skin until sampled. A rotating
                // bounded lane prevents a crowd of idle rigs causing one synchronized bake
                // spike, while native animation clocks and actions continue independently.
                long start = Stopwatch.GetTimestamp();
                int baked = 0, count = _bakeOrder.Count;
                for (int scanned = 0; scanned < count && baked < 2; scanned++)
                {
                    if (_nextBakeIndex >= _bakeOrder.Count) _nextBakeIndex = 0;
                    ScenarioVisibleIdleSnapshot? snapshot = _bakeOrder[_nextBakeIndex++].Visible;
                    int previous = snapshot?.Samples ?? 0;
                    snapshot?.AfterNativePose();
                    if ((snapshot?.Samples ?? 0) != previous) baked++;
                    if ((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency >= 2d) break;
                }
                PerfMonitor.Count("Figure.VisibleIdleBakes", baked);
            }
            catch (Exception error) { FailOpen(error); }
        }
        private void Update()
        {
            try { Tick(); }
            catch (Exception error) { FailOpen(error); }
        }
        private void Tick()
        {
            bool enabled = VRSession.IsRunning && NativeActionsReady && Enabled();
            float visibleInterval = VRSession.IsRunning && NativeActionsReady
                ? Mathf.Clamp(VisibleInterval(), 0f, .5f) : 0f;
            // A camera callback interrupted before PostRender cannot leave native sources masked.
            if (_cameraPolicies.Count > 0)
            {
                ReleaseCameraMasks();
            }
            int applied = 0, originalAlways = 0, authoredCull = 0;
            using (PerfMonitor.Scope("ScenarioIdleAnimation"))
            {
                foreach (KeyValuePair<ActorBehaviour, Record> item in _records)
                {
                    if (!item.Value.Tick(enabled, visibleInterval, false)) _dead.Add(item.Key);
                    if (item.Value.Applied) applied++;
                    if (item.Value.Original == AnimatorCullingMode.AlwaysAnimate) originalAlways++;
                    if (item.Value.Original == AnimatorCullingMode.CullUpdateTransforms) authoredCull++;
                }
                foreach (ActorBehaviour actor in _dead) Release(actor);
                _dead.Clear();
                int warming = 0;
                foreach (Record record in _bakeOrder)
                    if (record.Visible?.AwaitingNativePose == true) warming++;
                // Keep existing private poses visible while waiting for a sample slot.
                // Warming every due skin at once would undo the transform saving for a
                // large crowd even though the later BakeMesh lane itself was bounded.
                for (int scanned = 0; scanned < _bakeOrder.Count && warming < 2; scanned++)
                {
                    if (_nextRequestIndex >= _bakeOrder.Count) _nextRequestIndex = 0;
                    Record record = _bakeOrder[_nextRequestIndex++];
                    bool awaiting = record.Visible?.AwaitingNativePose == true;
                    record.Visible?.RequestSample(record.VisibleInterval);
                    if (!awaiting && record.Visible?.AwaitingNativePose == true) warming++;
                }
                PerfMonitor.Count("Figure.VisibleIdleSampling", warming);
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
