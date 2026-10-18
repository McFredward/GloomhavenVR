using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Preserve the original WaitForLoad continuation: network admission, input and
/// DisableLoadingScreen remain native. Desktop's optional, frame-limited
/// WaitForProcGen is insufficient for the translated worker: B683 dismissed the
/// loader with zero handles/content, then completed both rooms fifteen seconds
/// later. Use actual original placement/material readiness inside that same wait.
/// Hidden/preview rooms and frozen or unrequested entities never extend loading.
/// No renderer, hierarchy, generation flag or rules state is written here.
/// </summary>
internal static class QuestProceduralLoading
{
    internal static IEnumerator Wait(IEnumerator native)
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning)
        {
            yield return native;
            yield break;
        }
        SceneController controller = SceneController.Instance;
        RoomVisibilityManager visibility = RoomVisibilityManager.s_Instance;
        if (controller == null || visibility == null || visibility.Maps == null)
        {
            yield return native;
            yield break;
        }
        GameObject maps = visibility.Maps;
        // Do not first enumerate desktop's optional frame-budget wait here.
        // Its notifier enlists every ProceduralBase, including hidden previews
        // and frozen/unrequested children, before our requested-content gate
        // could run. Those callbacks may correctly never complete. The scoped
        // native readiness below supersedes that private wait on Quest; the
        // surrounding original WaitForLoad still owns networking, input and
        // DisableLoadingScreen, with no deadline or synthetic completion.
        (native as System.IDisposable)?.Dispose();
        // Generation delivers prefab Start and deferred material callbacks on
        // the following PlayerLoop. Observe completion across that boundary,
        // rather than closing the native loader in the placement callback.
        do
        {
            yield return null;
            if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || controller == null
                || SceneController.Instance != controller || visibility == null || visibility.Maps != maps
                || maps == null || !maps.activeInHierarchy || !SceneManager.GetSceneByName("ProcGen").isLoaded)
                yield break;
        }
        while (HasPendingContent(maps));
    }

    internal static bool HasPendingContent(GameObject maps) => HasPendingContent(maps, false, out _);

    // The room census can report complete rooms while a requested child or
    // material entry still holds the original continuation. Reuse the exact
    // admission below for its bounded first-blocker observation, instead of
    // inferring readiness from room totals or writing another per-frame log.
    internal static string DescribePendingContent(GameObject maps)
    {
        HasPendingContent(maps, true, out string detail);
        return detail;
    }

    private static bool HasPendingContent(GameObject maps, bool describe, out string detail)
    {
        detail = "none";
        foreach (ProceduralMapTile room in maps.GetComponentsInChildren<ProceduralMapTile>(true))
        {
            if (room == null || !room.isActiveAndEnabled || room.visibility != ProceduralMapTile.Visibility.All)
                continue;
            if (!Requested(room.GetComponent<ApparanceEntity>())) continue;
            foreach (ProceduralBase placement in room.GetComponentsInChildren<ProceduralBase>(true))
            {
                if (placement == null || !placement.isActiveAndEnabled
                    || placement.GetComponentInParent<ProceduralMapTile>() != room) continue;
                ApparanceEntity entity = placement.GetComponent<ApparanceEntity>();
                if (Requested(entity) && (!placement.IsPlacementCompleted || entity.IsBusy))
                {
                    if (describe) detail = "placement:" + Label(placement.name) + ":room=" + Label(room.name)
                        + ":completed=" + placement.IsPlacementCompleted + ":busy=" + entity.IsBusy
                        + QuestProceduralRequests.Describe(entity);
                    return true;
                }
            }
            // MaterialLoader.Start can defer one frame after placement. Its
            // original entry owns renderer disable/assignment and may also be
            // repaired by the existing per-entry MaterialLoaderHeal watchdog.
            // Wait for its actual result assignment, never enable its renderer
            // or retry healthy Addressables handles here. A completed loader
            // whose renderer another native owner hides is already ready.
            foreach (MaterialLoader loader in room.GetComponentsInChildren<MaterialLoader>())
            {
                if (loader == null || !loader.isActiveAndEnabled || loader.LoadersData == null
                    || loader.GetComponentInParent<ProceduralMapTile>() != room) continue;
                foreach (MaterialLoaderData data in loader.LoadersData)
                {
                    if (data == null || !NeedsMaterialAssignment(data.Renderer, room)
                        || data.MaterialReferences == null || data.MaterialReferences.Count == 0) continue;
                    var handles = data._handles;
                    if (handles == null || handles.Length != data.MaterialReferences.Count)
                    {
                        if (describe) detail = "material-handles:" + Label(data.Renderer.name) + ":room=" + Label(room.name)
                            + ":loader=" + Label(loader.name) + ":handles=" + (handles == null ? -1 : handles.Length)
                            + ":refs=" + data.MaterialReferences.Count + ":enabled=" + data.Renderer.enabled;
                        return true;
                    }
                    bool nativeCallbackCompleted = data._loadedMaterials != null
                        && data._loadedMaterials.Length >= handles.Length;
                    if (nativeCallbackCompleted)
                        foreach (Material material in data._loadedMaterials!)
                            if (material == null) { nativeCallbackCompleted = false; break; }
                    Material[] assigned = data.Renderer.sharedMaterials;
                    foreach (var handle in handles)
                    {
                        if (!handle.IsValid() || !handle.IsDone || handle.Result == null)
                        {
                            if (describe) detail = "material-pending:" + Label(data.Renderer.name) + ":room=" + Label(room.name)
                                + ":valid=" + handle.IsValid() + ":done=" + (handle.IsValid() && handle.IsDone)
                                + ":enabled=" + data.Renderer.enabled;
                            return true;
                        }
                        // Original callbacks fill _loadedMaterials then assign
                        // synchronously on this same main thread. A complete
                        // array proves that native edge even when a later local
                        // material owner has legitimately replaced the slots.
                        // The native save-existing sizing deadlock leaves null
                        // trailing entries, so it still needs actual assignment.
                        if (nativeCallbackCompleted) continue;
                        bool found = false;
                        foreach (Material material in assigned)
                            if (material != null && ReferenceEquals(ScenarioEnvironmentBudget.CanonicalMaterial(material), handle.Result))
                            { found = true; break; }
                        if (!found)
                        {
                            if (describe) detail = "material-assignment:" + Label(data.Renderer.name) + ":room=" + Label(room.name)
                                + ":loaded=" + Label(handle.Result.name) + ":slots=" + assigned.Length
                                + ":callback=" + nativeCallbackCompleted + ":enabled=" + data.Renderer.enabled;
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    private static string Label(string name) => name.Length <= 64 ? name : name.Substring(0, 64);

    private static bool NeedsMaterialAssignment(Renderer? renderer, ProceduralMapTile room)
    {
        if (renderer == null || !renderer.gameObject.activeInHierarchy
            || renderer.GetComponentInParent<ProceduralMapTile>() != room) return false;
        // These are native off-screen occlusion inputs, not missing world art.
        // TilesOcclusionGenerator.DrawRenderer supplies its own occlusion material;
        // their serialized built-in material references need not load through
        // Addressables. MaterialLoaderHeal excludes the same native owners. B684
        // could instead wait forever for their irrelevant material assignment
        // after the requested rooms and visible material loads had completed.
        // Test component ownership, never renderer.enabled: a real pending
        // visible loader deliberately disables its renderer until assignment.
        return renderer.GetComponent<ObjectOcclusionVolume>() == null
            && renderer.GetComponentInParent<TilesOcclusionVolume>() == null;
    }

    private static bool Requested(ApparanceEntity? entity) => entity != null && entity.isActiveAndEnabled
        && entity.IsPopulated && !entity.Frozen && entity.ProcedureID != 0;
}

[HarmonyPatch(typeof(UnityGameEditorRuntime), "WaitForProcGen")]
internal static class UnityGameEditorRuntime_QuestPlacementLoading
{
    private static void Postfix(ref IEnumerator __result)
    {
        if (QuestStandalonePlatform.Enabled && VRSession.IsRunning)
            __result = QuestProceduralLoading.Wait(__result);
    }
}
