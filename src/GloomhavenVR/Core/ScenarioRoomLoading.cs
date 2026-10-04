using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GloomhavenVR.Core;

/// <summary>Observe only real newly revealed native rooms. Their own procedural jobs and
/// material requests determine readiness; camera movement and periodic budget maintenance do
/// not start loading. No original visibility, addressable handle, continuation or input is written.</summary>
internal static class ScenarioRoomLoading
{
    private static readonly HashSet<ProceduralMapTile> Rooms = new();
    private static readonly List<ApparanceEntity> Entities = new();
    private static readonly List<MaterialLoaderData> Materials = new();
    private static readonly FieldInfo? Handles = typeof(MaterialLoaderData).GetField("_handles",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? Released = typeof(MaterialLoaderData).GetField("_released",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static bool _installed;
    private static int _revealedFrame;
    private static float _nextScan;
    internal static bool HasPendingReveal => Rooms.Count > 0;
    internal static string PendingDescription
    {
        get
        {
            int busy = 0, pending = 0, failed = 0;
            foreach (ApparanceEntity entity in Entities)
                if (entity != null && entity.gameObject.activeInHierarchy && entity.IsBusy) busy++;
            foreach (MaterialLoaderData request in Materials)
            {
                if (request?.Renderer == null || !request.Renderer.gameObject.activeInHierarchy
                    || Released?.GetValue(request) is true) continue;
                if (Handles?.GetValue(request) is AsyncOperationHandle<Material>[] handles)
                    foreach (AsyncOperationHandle<Material> handle in handles)
                    {
                        if (!handle.IsValid()) continue;
                        if (!handle.IsDone) pending++;
                        else if (handle.Status == AsyncOperationStatus.Failed) failed++;
                    }
            }
            return $"rooms={Rooms.Count}, generationOwners={Entities.Count}, materialRequests={Materials.Count}, pendingGeneration={busy}, pendingMaterialHandles={pending}, completedFailedHandles={failed}";
        }
    }

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
        _nextScan = 0f;
        ScenarioInteractionPreparation.NotifyRoomReveal();
    }

    internal static bool Tick()
    {
        if (!HasPendingReveal) return true;
        if (Time.frameCount <= _revealedFrame + 1) return false;
        bool discovered = Time.realtimeSinceStartup >= _nextScan;
        if (discovered) Discover();
        if (HasNativeWork()) return false;
        // Placement may have created children after the previous discovery. Observe them
        // once before closing, without adding a quiet timer after completed native work.
        if (!discovered) Discover();
        return !HasNativeWork();
    }

    private static void Discover()
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

    private static bool HasNativeWork()
    {
        foreach (ApparanceEntity entity in Entities)
            if (entity != null && entity.gameObject.activeInHierarchy && entity.IsBusy)
                return true;
        foreach (MaterialLoaderData request in Materials)
        {
            // Renderer.enabled is also owned by visual budgets. The result array reserves
            // slots for existing materials and can retain nulls after every request has
            // completed (or failed). Neither is evidence of outstanding asset loading.
            if (request?.Renderer == null || !request.Renderer.gameObject.activeInHierarchy
                || Released?.GetValue(request) is true) continue;
            if (Handles?.GetValue(request) is AsyncOperationHandle<Material>[] handles)
                foreach (AsyncOperationHandle<Material> handle in handles)
                    if (handle.IsValid() && !handle.IsDone) return true;
        }
        return false;
    }

    internal static void Complete() => Reset();
    internal static void Reset()
    {
        Rooms.Clear(); Entities.Clear(); Materials.Clear();
        _revealedFrame = 0; _nextScan = 0f;
    }

    internal static void Shutdown()
    {
        if (_installed) RoomVisibilityTracker.ProceduralMapTileVisibilityStateChanged -= Revealed;
        _installed = false;
        Reset();
    }
}
