using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// The original task-first Entity.UpdateAll gives incoming task decoding its
/// entire five-millisecond budget before it submits queued Entity.Update builds.
/// A process-bridge round trip or a large placement can consume that budget;
/// a sustained task stream can therefore starve a newly requested nested wall.
/// Preserve native scheduling/decoding and give already queued visible requests
/// a submission opportunity after that same original engine update. This never
/// asks for a rebuild or clears IsBusy/placement: only the native task callback
/// can complete the original request. B686 identifies a busy Wall1 but does not
/// capture its queue flag; this conditional repair is not proof of that cause.
/// </summary>
internal static class QuestProceduralRequests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? NativeEntity = typeof(ApparanceEntity).GetField("m_Entity", Flags);
    private static readonly PropertyInfo? Handle = NativeEntity?.FieldType.GetProperty("Handle", Flags);
    private static readonly FieldInfo? Queued = NativeEntity?.FieldType.GetField("m_bRebuildRequest", Flags);
    private static readonly MethodInfo? Submit = NativeEntity?.FieldType.GetMethod("Update", Flags, null, new[] { typeof(float) }, null);
    private static readonly object[] DeltaTime = { .1f }; // same original UpdateEngine constant; no per-frame box/array
    private static bool _failed;

    internal static void AfterNativeUpdate(ApparanceEngine engine)
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || _failed
            || engine == null || engine != ApparanceEngine.Instance || !engine.isActiveAndEnabled
            || !SceneManager.GetSceneByName("ProcGen").isLoaded) return;
        SceneController loading = SceneController.Instance;
        if (loading == null || (!loading.IsLoading && !loading.ScenarioIsLoading)) return;
        try
        {
            if (NativeEntity == null || NativeEntity.FieldType.FullName != "Apparance.Net.Entity"
                || Handle == null || Handle.PropertyType != typeof(int) || Queued == null || Queued.FieldType != typeof(bool) || Submit == null || Submit.ReturnType != typeof(void))
                throw new MissingMemberException("Original Apparance queued-request contract is unavailable.");
            var entities = engine.EntityComponents;
            if (entities == null) return;
            int count = entities.Count;
            for (int index = 0; index < count && index < entities.Count; index++)
            {
                ApparanceEntity entity = entities[index];
                if (entity == null || !entity.isActiveAndEnabled || !entity.IsPopulated || entity.Frozen
                    || entity.ProcedureID == 0 || entity.m_EntityHandle == 0 || entity.GetComponent<ProceduralBase>() == null) continue;
                ProceduralMapTile room = entity.GetComponentInParent<ProceduralMapTile>();
                if (room == null || !room.isActiveAndEnabled || room.visibility != ProceduralMapTile.Visibility.All) continue;
                object? native = NativeEntity.GetValue(entity);
                if (native != null && Handle.GetValue(native) is int handle && handle == entity.m_EntityHandle
                    && Queued.GetValue(native) is true)
                    Submit.Invoke(native, DeltaTime);
            }
        }
        catch (Exception error)
        {
            _failed = true;
            VRLog.Warn("QuestLifecycle", "Original procedural request fairness is unavailable; native engine continuation retained: "
                + (error is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : error));
        }
    }

    internal static string Describe(ApparanceEntity entity)
    {
        object? native = NativeEntity?.GetValue(entity);
        return ":handle=" + entity.m_EntityHandle + ":queued="
            + (native == null ? "no-entity" : Queued?.GetValue(native)?.ToString() ?? "unknown");
    }
}

[HarmonyPatch(typeof(ApparanceEngine), "UpdateEngine")]
internal static class ApparanceEngine_QuestPendingRequests
{
    private static void Postfix(ApparanceEngine __instance) => QuestProceduralRequests.AfterNativeUpdate(__instance);
}
