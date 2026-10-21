using System;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// Bind the mobile upload correction to the original TargetShield pool spawn,
/// never to a renderer name or scene-wide particle census. The exact native
/// source uses two additive mesh particle draws; leave its fog and lights alone.
/// No damage value, event, spawn, placement, lifetime or pool decision is changed.
/// </summary>
[HarmonyPatch]
internal static class QuestNativeShieldSpawn
{
    private static bool _failureReported, _defaultHitReported;
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(ObjectPool), nameof(ObjectPool.Spawn),
        new[] { typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Quaternion), typeof(bool) });

    // The supplied shield-shaped video is not a native spawn receipt. The
    // separate zero-damage hit effect has unqualified fog/distortion consumers;
    // observe its exact authored fallback once, without adapting any of them.
    private static void ObserveDefaultHit(GameObject prefab, GameObject effect)
    {
        if (_defaultHitReported || !VRLog.Wants(VRLogLevel.Debug)) return;
        _defaultHitReported = true;
        ParticleSystemRenderer[] renderers = effect.GetComponentsInChildren<ParticleSystemRenderer>(true);
        var details = new System.Text.StringBuilder(256);
        for (int index = 0; index < renderers.Length && index < 6; index++)
        {
            ParticleSystemRenderer renderer = renderers[index];
            Material material = renderer.sharedMaterial;
            if (index != 0) details.Append(';');
            details.Append(renderer.name).Append(':').Append(renderer.renderMode).Append(':')
                .Append(material != null && material.shader != null ? material.shader.name : "none")
                .Append(":active=").Append(renderer.enabled && renderer.gameObject.activeInHierarchy);
        }
        Debug.Log("[Quest startup] shield-native-source role=default-hit prefab=" + prefab.name
            + " renderers=" + renderers.Length + " observed=" + details + " adapter=none");
    }

    private static void Postfix(GameObject prefab, GameObject __result)
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || prefab == null || __result == null) return;
        try
        {
            GlobalSettings settings = GlobalSettings.Instance;
            if (settings != null && settings.m_ActiveBonusBuffTargetEffects != null
                && ReferenceEquals(prefab, settings.m_ActiveBonusBuffTargetEffects.ShieldActiveBonusTargetEffect))
                QuestPingStreams.AttachShield(__result);
            else if (settings != null && settings.m_DefaultHitEffects != null
                && ReferenceEquals(prefab, settings.m_DefaultHitEffects.DefaultShieldHitEffect))
                ObserveDefaultHit(prefab, __result);
        }
        catch (Exception error)
        {
            if (_failureReported) return;
            _failureReported = true;
            VRLog.Warn("Board", "Quest shield upload unavailable; original effect retained (" + error.GetType().Name + ").");
        }
    }
}
