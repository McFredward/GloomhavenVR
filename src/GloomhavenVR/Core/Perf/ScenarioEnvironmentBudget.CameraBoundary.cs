using System;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Camera.onPreCull listeners run in registration order. Returning leases from
/// onPreRender is too late: native culling already omitted masked originals.
/// Unity2021's managed FireOnPreCull invokes that whole listener list before
/// returning to native culling. Its postfix is the safe shared validation seam.
/// Each subscriber owns its own fail-open guard and removes itself on shutdown.
/// </summary>
internal static class ScenarioCameraCullBoundary
{
    private static Harmony? _installedOwner;
    private static Action<Camera>? _listeners;
    internal static void Subscribe(Action<Camera> listener) => _listeners += listener;
    internal static void Unsubscribe(Action<Camera> listener) => _listeners -= listener;
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
