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
    private static bool _failureReported;
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(ObjectPool), nameof(ObjectPool.Spawn),
        new[] { typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Quaternion), typeof(bool) });

    private static void Postfix(GameObject prefab, GameObject __result)
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || prefab == null || __result == null) return;
        try
        {
            GlobalSettings settings = GlobalSettings.Instance;
            if (settings != null && settings.m_ActiveBonusBuffTargetEffects != null
                && ReferenceEquals(prefab, settings.m_ActiveBonusBuffTargetEffects.ShieldActiveBonusTargetEffect))
                QuestPingStreams.AttachShield(__result);
        }
        catch (Exception error)
        {
            if (_failureReported) return;
            _failureReported = true;
            VRLog.Warn("Board", "Quest shield upload unavailable; original effect retained (" + error.GetType().Name + ").");
        }
    }
}
