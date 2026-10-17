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
/// later. Append actual original placement/material readiness inside that same wait.
/// Hidden/preview rooms and frozen or unrequested entities never extend loading.
/// No renderer, hierarchy, generation flag or rules state is written here.
/// </summary>
internal static class QuestProceduralLoading
{
    internal static IEnumerator Wait(IEnumerator native)
    {
        yield return native;
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning) yield break;
        SceneController controller = SceneController.Instance;
        RoomVisibilityManager visibility = RoomVisibilityManager.s_Instance;
        if (controller == null || visibility == null || visibility.Maps == null) yield break;
        GameObject maps = visibility.Maps;
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

    internal static bool HasPendingContent(GameObject maps)
    {
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
                if (Requested(entity) && (!placement.IsPlacementCompleted || entity.IsBusy)) return true;
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
                    if (data == null || data.Renderer == null || !data.Renderer.gameObject.activeInHierarchy
                        || data.MaterialReferences == null || data.MaterialReferences.Count == 0) continue;
                    var handles = data._handles;
                    if (handles == null || handles.Length != data.MaterialReferences.Count) return true;
                    bool nativeCallbackCompleted = data._loadedMaterials != null
                        && data._loadedMaterials.Length >= handles.Length;
                    if (nativeCallbackCompleted)
                        foreach (Material material in data._loadedMaterials!)
                            if (material == null) { nativeCallbackCompleted = false; break; }
                    Material[] assigned = data.Renderer.sharedMaterials;
                    foreach (var handle in handles)
                    {
                        if (!handle.IsValid() || !handle.IsDone || handle.Result == null) return true;
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
                        if (!found) return true;
                    }
                }
            }
        }
        return false;
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
