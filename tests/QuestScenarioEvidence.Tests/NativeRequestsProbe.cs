using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

// Controlled game ownership boundaries. ApparanceEngine/ApparanceEntity and
// Entity.UpdateAll/Update/HandleTask come from actual original assemblies.
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled = true; }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog { internal static int Warnings; internal static void Warn(string category, string text) { Warnings++; UnityEngine.Debug.LogWarning(text); } }
}
public class SceneController : MonoBehaviour { public static SceneController Instance; public bool IsLoading = true, ScenarioIsLoading = true; }
[RequireComponent(typeof(ApparanceEntity))] public class ProceduralMapTile : ProceduralBase { public enum Visibility { Hidden, Preview, All } public Visibility visibility; }
public class ProceduralStyle : MonoBehaviour { public int Seed; }
public class ProceduralScenario : MonoBehaviour { }
public static class GloomUtility
{
    public static T FindAncestorComponent<T>(Component component) where T : Component { return component.GetComponentInParent<T>(); }
    public static T EnsureComponent<T>(GameObject owner) where T : Component { return owner.GetComponent<T>() ?? owner.AddComponent<T>(); }
}

public static class QuestNativeRequestsProbe
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int _assertions;
    static readonly FieldInfo NativeField = typeof(ApparanceEntity).GetField("m_Entity", Flags);
    static readonly FieldInfo QueuedField = NativeField.FieldType.GetField("m_bRebuildRequest", Flags);
    static readonly MethodInfo Tick = typeof(ApparanceEngine).GetMethod("UpdateEngine", Flags);
    static void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); _assertions++; UnityEngine.Debug.Log("PASS " + text); }
    static bool Queued(ApparanceEntity entity) { return (bool)QueuedField.GetValue(NativeField.GetValue(entity)); }
    static void Invoke(ApparanceEntity entity, string method) { typeof(ApparanceEntity).GetMethod(method, Flags).Invoke(entity, null); }
    static ProceduralBase Wall(ApparanceEngine engine, Transform room, string name, out ApparanceEntity entity)
    {
        var go = new GameObject(name); go.transform.parent = room;
        entity = go.AddComponent<ApparanceEntity>(); var placement = go.AddComponent<ProceduralBase>();
        entity.ProcedureID = 123; entity.IsPopulated = true;
        Invoke(entity, "CheckStructure"); Invoke(entity, "CreateEntity");
        if (!engine.EntityComponents.Contains(entity)) engine.EntityComponents.Add(entity); return placement;
    }
    public static void Run()
    {
        int exit = 0;
        try
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            scene.name = "ProcGen";
            var owner = new GameObject("Original native engine"); SceneManager.MoveGameObjectToScene(owner, scene);
            var engine = owner.AddComponent<ApparanceEngine>();
            engine.EnableDetailFocus = true; engine.DetailFocus = owner;
            Check(engine == ApparanceEngine.Instance && engine.Resources != null,
                "Actual original engine Awake establishes original owner and resource component");
            SceneController.Instance = owner.AddComponent<SceneController>();
            var room = new GameObject("Room B").AddComponent<ProceduralMapTile>(); room.visibility = ProceduralMapTile.Visibility.All;
            var wall = Wall(engine, room.transform, "Wall 1", out ApparanceEntity wallEntity);
            int began = 0, completed = 0;
            wall.ContentPlacementStarted += unused => began++;
            wall.ContentPlacementCompleted += unused => completed++;
            var preview = new GameObject("Unrevealed preview").AddComponent<ProceduralMapTile>(); preview.visibility = ProceduralMapTile.Visibility.Preview;
            Wall(engine, preview.transform, "Preview wall", out ApparanceEntity previewEntity);
            Wall(engine, room.transform, "Frozen wall", out ApparanceEntity frozen); frozen.Frozen = true;
            Wall(engine, room.transform, "Inactive wall", out ApparanceEntity inactive); inactive.gameObject.SetActive(false);
            Wall(engine, room.transform, "Unpopulated wall", out ApparanceEntity unpopulated); unpopulated.IsPopulated = false;
            Wall(engine, room.transform, "Zero-procedure wall", out ApparanceEntity zero); zero.ProcedureID = 0;
            Wall(engine, room.transform, "Unregistered wall", out ApparanceEntity unregistered); while (engine.EntityComponents.Contains(unregistered)) engine.EntityComponents.Remove(unregistered);
            Wall(engine, room.transform, "Stale wall", out ApparanceEntity stale); stale.m_EntityHandle += 1000000;
            var noise = Apparance.Net.Engine.CreateEntity();
            NativeRequestsBoundary.Reset(); NativeRequestsBoundary.NoiseHandle = noise.Handle; NativeRequestsBoundary.Noise = true;
            Check(Queued(wallEntity) && wallEntity.IsBusy && !wall.IsPlacementCompleted,
                "Actual original requested wall starts busy with an existing queued build");
            for (int i = 0; i < 4; i++) Tick.Invoke(engine, null);
            Check(NativeRequestsBoundary.Builds == 0 && Queued(wallEntity) && began == 0 && completed == 0,
                "Actual original task-first five-ms scheduler starves pending nested wall submission");
            Check(QuestProceduralRequests.Describe(wallEntity).Contains(":queued=True") && wallEntity.m_EntityHandle != 0,
                "Bounded loading evidence distinguishes original queue ownership and native handle");
            QuestStandalonePlatform.Enabled = false; QuestProceduralRequests.AfterNativeUpdate(engine);
            Check(NativeRequestsBoundary.Builds == 0 && Queued(wallEntity), "Desktop never changes original native request scheduling");
            QuestStandalonePlatform.Enabled = true; VRSession.IsRunning = false; QuestProceduralRequests.AfterNativeUpdate(engine);
            Check(NativeRequestsBoundary.Builds == 0, "Stopped VR retains original native request ownership");
            VRSession.IsRunning = true; SceneController.Instance.IsLoading = SceneController.Instance.ScenarioIsLoading = false;
            QuestProceduralRequests.AfterNativeUpdate(engine); Check(NativeRequestsBoundary.Builds == 0, "Settled ordinary play does not run loading fairness");
            SceneController.Instance.IsLoading = true;
            var harmony = new HarmonyLib.Harmony("quest-native-request-proof"); harmony.PatchAll(typeof(ApparanceEngine_QuestPendingRequests));
            Tick.Invoke(engine, null);
            Check(NativeRequestsBoundary.Builds == 1 && !Queued(wallEntity) && wallEntity.IsBusy && began == 1 && completed == 0,
                "Real engine postfix submits only the already queued wall through original Update and BeginContentUpdate");
            Check(Queued(previewEntity) && Queued(frozen) && Queued(inactive) && Queued(unpopulated) && Queued(zero) && Queued(unregistered) && Queued(stale),
                "Preview frozen inactive unpopulated invalid unregistered and stale native requests remain untouched");
            Check(!wall.IsPlacementCompleted, "Fairness cannot fabricate original placement completion before a native task");
            Tick.Invoke(engine, null);
            Check(!wallEntity.IsBusy && wall.IsPlacementCompleted && completed == 1,
                "Original task decoding and actual Unity EndContentUpdate complete the nested wall");
            Check(NativeRequestsBoundary.Builds == 1 && !Queued(wallEntity), "Completed native request is never submitted twice");
            int before = NativeRequestsBoundary.Builds;
            for (int i = 0; i < 3; i++) Tick.Invoke(engine, null);
            Check(NativeRequestsBoundary.Builds == before && began == 1 && completed == 1,
                "Healthy original continuation remains a strict scheduling no-op after completion");
            Check(QuestProceduralRequests.Describe(wallEntity).Contains(":queued=False") && VRLog.Warnings == 0,
                "Completed evidence reports the actual native queue without hiding an invocation failure");
            Check(NativeRequestsBoundary.Pops >= 9 && NativeRequestsBoundary.TickCalls >= 9,
                "Actual original UpdateEngine continues native updates and task polling throughout the repaired lifecycle");
            harmony.UnpatchSelf(); NativeRequestsBoundary.Noise = false;
            Directory.CreateDirectory("ProbeOutput");
            File.WriteAllText("ProbeOutput/result.json", "{\"assertions\":" + _assertions + ",\"actualOriginalManagedAssemblies\":true,\"nativeInteropControlled\":true,\"originalProceduralBaseSourceExecuted\":true,\"headsetVerified\":false}\n");
        }
        catch (Exception error) { UnityEngine.Debug.LogException(error); exit = 1; }
        finally { UnityEditor.EditorApplication.Exit(exit); }
    }
}
