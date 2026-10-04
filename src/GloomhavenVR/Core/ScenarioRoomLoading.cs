using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Observe only real newly revealed native rooms. Their own procedural jobs and
/// material requests determine readiness; camera movement and periodic budget maintenance do
/// not start loading. No original visibility, addressable handle, continuation or input is written.</summary>
internal static class ScenarioRoomLoading
{
    private static readonly HashSet<ProceduralMapTile> Rooms = new();
    private static readonly List<ApparanceEntity> Entities = new();
    private static readonly List<MaterialLoaderData> Materials = new();
    private static readonly FieldInfo? LoadedMaterials = typeof(MaterialLoaderData).GetField("_loadedMaterials",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static bool _installed;
    private static int _revealedFrame;
    private static float _nextScan, _quietSince;
    internal static bool HasPendingReveal => Rooms.Count > 0;
    internal static string PendingDescription => $"rooms={Rooms.Count}, generationOwners={Entities.Count}, materialRequests={Materials.Count}";

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        RoomVisibilityTracker.ProceduralMapTileVisibilityStateChanged += Revealed;
    }

    private static void Revealed(ProceduralMapTile tile, bool shown)
    {
        if (!shown || !ScenarioInteractionPreparation.AcceptsRoomReveal(tile) || !Rooms.Add(tile)) return;
        // Visibility is assigned before ProceduralMapTile.Update activates its children.
        // Keep two actual frame boundaries so the first idle observation cannot finish early.
        _revealedFrame = Time.frameCount;
        _nextScan = _quietSince = 0f;
        ScenarioInteractionPreparation.NotifyRoomReveal();
    }

    internal static bool Tick()
    {
        if (!HasPendingReveal) return true;
        if (Time.frameCount <= _revealedFrame + 1) return false;
        if (Time.realtimeSinceStartup >= _nextScan)
        {
            _nextScan = Time.realtimeSinceStartup + .25f;
            Entities.Clear(); Materials.Clear();
            foreach (ProceduralMapTile room in Rooms)
            {
                if (room == null || !room.gameObject.activeInHierarchy) continue;
                foreach (ApparanceEntity entity in room.GetComponentsInChildren<ApparanceEntity>(true))
                    if (entity != null && entity.gameObject.activeInHierarchy) Entities.Add(entity);
                foreach (MaterialLoader loader in room.GetComponentsInChildren<MaterialLoader>(true))
                    if (loader != null && loader.gameObject.activeInHierarchy && loader.LoadersData != null)
                        Materials.AddRange(loader.LoadersData);
            }
        }
        foreach (ApparanceEntity entity in Entities)
            if (entity != null && entity.gameObject.activeInHierarchy && entity.IsBusy)
            { _quietSince = 0f; return false; }
        foreach (MaterialLoaderData request in Materials)
        {
            // Renderer.enabled belongs to several visual budgets and cannot prove loading.
            // Read the exact original async completion array without accessing/mutating handles.
            if (request?.Renderer == null || !request.Renderer.gameObject.activeInHierarchy) continue;
            if (LoadedMaterials?.GetValue(request) is Material[] loaded)
                foreach (Material material in loaded)
                    if (material == null) { _quietSince = 0f; return false; }
        }
        if (_quietSince == 0f) _quietSince = Time.realtimeSinceStartup;
        // A final discovery observes children/loaders born at the preceding placement completion.
        return Time.realtimeSinceStartup - _quietSince >= .25f;
    }

    internal static void Complete() => Reset();
    internal static void Reset()
    {
        Rooms.Clear(); Entities.Clear(); Materials.Clear();
        _revealedFrame = 0; _nextScan = _quietSince = 0f;
    }

    internal static void Shutdown()
    {
        if (_installed) RoomVisibilityTracker.ProceduralMapTileVisibilityStateChanged -= Revealed;
        _installed = false;
        Reset();
    }
}
