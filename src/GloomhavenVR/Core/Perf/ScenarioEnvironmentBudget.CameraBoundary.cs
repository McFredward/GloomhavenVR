using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Camera.onPreCull listeners run in registration order. Returning leases from
/// onPreRender is too late: native culling already omitted masked originals.
/// Unity2021's managed FireOnPreCull invokes that whole listener list before
/// returning to native culling. Desktop uses that postfix; the Quest adapter
/// selects the scoped public-tail owner below because its Player owns CoreModule.
/// Each subscriber owns its own fail-open guard and removes itself on shutdown.
/// </summary>
internal static class ScenarioCameraCullBoundary
{
    private static Harmony? _installedOwner;
    private static Action<Camera>? _listeners;
    internal static void Subscribe(Action<Camera> listener) => _listeners += listener;
    internal static void Unsubscribe(Action<Camera> listener)
    {
        _listeners -= listener;
        if (_questCallbacks && _listeners == null)
        {
            Camera.onPreCull -= PrepareQuestCamera;
            Camera.onPreCull -= FinishQuestCamera;
            _questPublicList = null;
            QuestArmed.Clear();
        }
    }
    private static bool _questCallbacks;
    private static Harmony? _questOwner;
    private static Camera.CameraCallback? _questPublicList;
    private static readonly Dictionary<int, Camera.CameraCallback> QuestArmed = new();
    private static Action<Camera>? _unavailable;
    internal static void SubscribeUnavailable(Action<Camera> listener) => _unavailable += listener;
    internal static void UnsubscribeUnavailable(Action<Camera> listener) => _unavailable -= listener;
    internal static bool CanFinalize(Camera camera) => !_questCallbacks || camera != null
        && QuestArmed.TryGetValue(camera.GetInstanceID(), out Camera.CameraCallback snapshot)
        && ReferenceEquals(snapshot, Camera.onPreCull);

    // B688 requested the shared Frame budgets, but their Quest installation threw
    // before either could run. Its Player owns CoreModule and cannot accept the
    // desktop engine detour. Restore the existing owners, not a second renderer.
    // Real Unity2021 Camera.Render runs component OnPreCull before its public
    // delegate snapshot. A camera-owned component puts this one shared final
    // handler last at that earlier boundary. Keep an exact delegate identity:
    // any later writer revokes optional leases instead of claiming finality.
    // An unseen camera gets one original invocation while its component attaches.
    // No source geometry, light, visibility or setting is forced by this seam.
    internal static void InstallQuest()
    {
        Harmony owner = VRSession.Harmony ?? throw new InvalidOperationException("Native camera final pre-cull boundary requires the session Harmony owner.");
        if (!ReferenceEquals(_questOwner, owner))
        {
            QuestArmed.Clear();
            _questOwner = owner;
            foreach (Camera camera in UnityEngine.Object.FindObjectsOfType<Camera>(true)) AttachQuestCamera(camera);
        }
        _questCallbacks = true;
        Camera.onPreCull -= PrepareQuestCamera;
        Camera.onPreCull += PrepareQuestCamera;
    }
    private static void PrepareQuestCamera(Camera camera)
    {
        if (camera == null || _listeners == null) return;
        if (CanFinalize(camera)) return;
        AttachQuestCamera(camera);
        QuestArmed.Remove(camera.GetInstanceID());
        _unavailable?.Invoke(camera);
    }
    private static void AttachQuestCamera(Camera camera)
    {
        if (camera != null && camera.GetComponent<QuestFinalCull>() == null)
        {
            QuestFinalCull final = camera.gameObject.AddComponent<QuestFinalCull>();
            final.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
        }
    }
    private static void ArmQuestCamera(Camera camera)
    {
        if (!_questCallbacks || _listeners == null || camera == null) return;
        if (!ReferenceEquals(Camera.onPreCull, _questPublicList))
        {
            Camera.onPreCull -= FinishQuestCamera;
            Camera.onPreCull += FinishQuestCamera;
            _questPublicList = Camera.onPreCull;
        }
        QuestArmed[camera.GetInstanceID()] = _questPublicList!;
    }
    private static void FinishQuestCamera(Camera camera)
    {
        if (camera == null) return;
        bool valid = CanFinalize(camera);
        if (!QuestArmed.Remove(camera.GetInstanceID())) return;
        if (valid) AfterNativePreCull(camera);
        else _unavailable?.Invoke(camera);
    }
    [DefaultExecutionOrder(32000)]
    private sealed class QuestFinalCull : MonoBehaviour
    {
        private Camera? _camera;
        private void Awake() => _camera = GetComponent<Camera>();
        private void OnPreCull() { if (_camera != null) ArmQuestCamera(_camera); }
        private void OnDisable() { if (_camera != null) QuestArmed.Remove(_camera.GetInstanceID()); }
        private void OnDestroy() { if (_camera != null) QuestArmed.Remove(_camera.GetInstanceID()); }
    }
    internal static void Install()
    {
        Harmony owner = VRSession.Harmony ?? throw new InvalidOperationException("Native camera final pre-cull boundary requires the session Harmony owner.");
        if (ReferenceEquals(_installedOwner, owner)) return;
        owner.PatchAll(typeof(Camera_FinalPreCull_BudgetPatch));
        // Plugin.OnDestroy removes that owner's patches. A later plugin instance
        // may inhabit the same managed domain, so a process-wide bool is stale.
        _installedOwner = owner;
    }
    internal static void AfterNativePreCull(Camera camera) => _listeners?.Invoke(camera);
}

[HarmonyPatch(typeof(Camera), "FireOnPreCull")]
internal static class Camera_FinalPreCull_BudgetPatch
{
    private static void Postfix(Camera cam) => ScenarioCameraCullBoundary.AfterNativePreCull(cam);
}
