using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using NativeVector = Apparance.Net.Vector3;

// These game boundaries only provide ownership for the verbatim ProceduralBase.
[RequireComponent(typeof(ApparanceEntity))] public class ProceduralMapTile : ProceduralBase { }
public class ProceduralStyle : MonoBehaviour { public int Seed; }
public class ProceduralScenario : MonoBehaviour { }
public static class GloomUtility
{
    public static T FindAncestorComponent<T>(Component component) where T : Component { return component.GetComponentInParent<T>(); }
    public static T EnsureComponent<T>(GameObject owner) where T : Component { return owner.GetComponent<T>() ?? owner.AddComponent<T>(); }
}

public static class QuestNativeReentryProbe
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("ApparanceEngine")] static extern int quest_apparance_configure(string executables, string payload, string state);
    [DllImport("ApparanceEngine")] static extern void ApparanceUpdateAsset(int context, string name, int id, int bounds, float[] frame, int variants);
    [Serializable] public class Row { public int entity, result, boundsAvailable, variantCount; public uint procedure; public bool dynamicDetail; public string operation, descriptor, buffer; public float[] frame; }
    static readonly FieldInfo Native = typeof(ApparanceEntity).GetField("m_Entity", Flags);
    static readonly FieldInfo Queue = typeof(Apparance.Net.Entity).GetField("m_bRebuildRequest", Flags);
    static readonly MethodInfo Tick = typeof(ApparanceEngine).GetMethod("UpdateEngine", Flags);
    static readonly MethodInfo Start = typeof(ApparanceEngine).GetMethod("Start", Flags);
    static readonly Dictionary<string, Row> Replies = new Dictionary<string, Row>();
    static readonly List<ApparanceEntity> Entities = new List<ApparanceEntity>();
    static readonly List<ProceduralBase> Placements = new List<ProceduralBase>();
    static readonly List<PlacementObserver> Observers = new List<PlacementObserver>();
    static string _state;
    static int _checks, _assetReplies;
    static ApparanceEngine _engine;
    static GameObject _prefab;
    static void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); _checks++; UnityEngine.Debug.Log("PASS " + text); }
    static Apparance.Net.Entity Entity(ApparanceEntity owner) { return (Apparance.Net.Entity)Native.GetValue(owner); }

    // Exact recorded native asset bounds/variants, with deliberately tiny fixture
    // prefabs for actual Unity placement. No original model/artwork is certified.
    public static bool ResolveRecordedAssets()
    {
        int context, id; string descriptor;
        while ((descriptor = Apparance.Net.Engine.AssetRequestPoll(out context, out id)) != null)
        {
            Row row;
            if (!Replies.TryGetValue(descriptor, out row)) throw new InvalidOperationException("Missing exact native asset reply: " + descriptor);
            ApparanceUpdateAsset(row.entity, descriptor, id, row.boundsAvailable, row.frame, row.variantCount);
            var map = (IDictionary)typeof(ApparanceResources).GetField("m_IDToAssetMap", Flags).GetValue(_engine.Resources);
            map[id] = new AssetInfo { ID = id, Name = descriptor, Object = _prefab,
                MinBounds = Vector3.zero, MaxBounds = Vector3.one, VariantCount = row.variantCount, HasBounds = true };
            _assetReplies++;
        }
        return false;
    }

    sealed class PlacementObserver : Apparance.Net.IObjectPlacement
    {
        readonly Apparance.Net.IObjectPlacement _original;
        public int Begins, Ends, Objects, Groups, Meshes, Vertices;
        public PlacementObserver(ApparanceEntity owner) { _original = (Apparance.Net.IObjectPlacement)owner; }
        public bool IsValid { get { return _original.IsValid; } }
        public void BeginContentUpdate() { Begins++; _original.BeginContentUpdate(); }
        public void EndContentUpdate() { _original.EndContentUpdate(); Ends++; }
        public void CreateObject(int handle, int tier, NativeVector offset, int resource, Apparance.Net.Frame frame, Apparance.Net.ParameterCollection parameters, string name, int children)
        { Objects++; _original.CreateObject(handle, tier, offset, resource, frame, parameters, name, children); }
        public void CreateGroup(int handle, int tier, string name, int children) { Groups++; _original.CreateGroup(handle, tier, name, children); }
        public void DestroyObject(int handle) { _original.DestroyObject(handle); }
        public void CreateMesh(int handle, int tier, NativeVector offset, int vertices, NativeVector[] positions, NativeVector[] normals, IList<uint[]> colors, IList<Apparance.Net.Vector2[]> uvs, Apparance.Net.MeshPart[] parts)
        { Meshes++; Vertices += vertices; _original.CreateMesh(handle, tier, offset, vertices, positions, normals, colors, uvs, parts); }
    }

    static string WorkerIdentity()
    {
        string file = File.ReadAllText(Path.Combine(_state, "procedural-worker.log"));
        int offset = file.IndexOf("childPID=", StringComparison.Ordinal);
        return offset < 0 ? "absent" : file.Substring(offset).Split('\n')[0].Trim();
    }
    static void Receipt(string path, string before, string after)
    {
        int complete = 0, busy = 0, queued = 0, begins = 0, ends = 0, objects = 0, groups = 0, meshes = 0, vertices = 0;
        for (int i = 0; i < Entities.Count; i++)
        {
            if (Placements[i].IsPlacementCompleted) complete++;
            if (Entities[i].IsBusy) busy++;
            if ((bool)Queue.GetValue(Entity(Entities[i]))) queued++;
            var observer = Observers[i]; begins += observer.Begins; ends += observer.Ends; objects += observer.Objects;
            groups += observer.Groups; meshes += observer.Meshes; vertices += observer.Vertices;
        }
        File.WriteAllText(path, "{\"assertions\":" + _checks + ",\"entities\":" + Entities.Count + ",\"complete\":" + complete
            + ",\"busy\":" + busy + ",\"queued\":" + queued + ",\"begins\":" + begins + ",\"ends\":" + ends
            + ",\"objects\":" + objects + ",\"groups\":" + groups + ",\"meshes\":" + meshes + ",\"vertices\":" + vertices
            + ",\"exactRecordedAssetReplies\":" + _assetReplies + ",\"sameWorker\":" + (before == after ? "true" : "false")
            + ",\"actualOriginalAssembliesAndEngine\":true,\"fixtureAssetPrefabs\":true,\"headsetVerified\":false}\n");
    }
    public static void Run()
    {
        int exit = 0;
        try
        {
            Directory.CreateDirectory("ProbeOutput");
            string fixture = Path.GetFullPath("NativeInputs"); _state = Path.Combine(fixture, "state");
            Check(quest_apparance_configure("/usr/lib/wine", Path.Combine(fixture, "payload"), _state) == 0, "Actual copied-buffer native bridge configured before original Awake");
            var rows = new List<Row>();
            foreach (string line in File.ReadAllLines(Path.Combine(fixture, "calls.jsonl")))
            {
                Row row = JsonUtility.FromJson<Row>(line); rows.Add(row);
                if (row.operation == "asset-response" && row.descriptor.Length != 0) Replies[row.descriptor] = row;
            }
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single); scene.name = "ProcGen";
            _prefab = new GameObject("Recorded-resource fixture prefab"); _prefab.hideFlags = HideFlags.HideAndDontSave;
            var engineObject = new GameObject("Original engine"); engineObject.SetActive(false); _engine = engineObject.AddComponent<ApparanceEngine>();
            _engine.EditorLiveEditing = false; _engine.EditorSynthesisBufferSize = 35; engineObject.SetActive(true);
            _engine.EnableDetailFocus = true; _engine.DetailFocus = engineObject; _engine.EnableInstancedRendering = false; _engine.PrefabInstancing = false;
            Check(ApparanceEngine.Instance == _engine && Apparance.Net.Engine.IsRunning, "Unchanged original Awake starts actual native engine through bridge");
            var byHandle = new Dictionary<int, ApparanceEntity>();
            foreach (Row row in rows)
            {
                if (row.operation != "create") continue;
                var owner = new GameObject("Recorded entity " + row.result); var entity = owner.AddComponent<ApparanceEntity>();
                var placement = owner.AddComponent<ProceduralBase>(); entity.IsPopulated = true;
                typeof(ApparanceEntity).GetMethod("CheckStructure", Flags).Invoke(entity, null);
                typeof(ApparanceEntity).GetMethod("CreateEntity", Flags).Invoke(entity, null);
                var observer = new PlacementObserver(entity); Entity(entity).ObjectPlacementHandler = observer;
                Entities.Add(entity); Placements.Add(placement); Observers.Add(observer); byHandle[row.result] = entity;
            }
            foreach (Row row in rows)
            {
                if (row.operation != "build") continue;
                var entity = byHandle[row.entity]; entity.ProcedureID = row.procedure; Entity(entity).Procedure = row.procedure;
                var parameters = Entity(entity).Parameters; parameters.SetData(File.ReadAllBytes(Path.Combine(fixture, row.buffer)));
                Entity(entity).SetParameters(parameters); Entity(entity).DynamicDetail = row.dynamicDetail;
                byte[] effective; parameters.GetData(out effective); UnityEngine.Debug.Log("INPUT original=" + row.entity + " native=" + entity.m_EntityHandle + " procedure=" + Entity(entity).Procedure + " bytes=" + effective.Length);
            }
            Check(Entities.Count == 18 && Entities[0].m_EntityHandle < 0, "Eighteen actual native handles and effective game parameters exist before code reconnect");
            string workerCache = Path.Combine(_state, "apparance-worker.exe");
            byte[] workerExpected = File.ReadAllBytes(Path.Combine(fixture, "expected-worker.exe"));
            Check(File.ReadAllBytes(workerCache).SequenceEqual(workerExpected), "Embedded bridge publishes the exact owned PE worker in private runtime storage");
            long cachedWrite = File.GetLastWriteTimeUtc(workerCache).Ticks;
            string before = WorkerIdentity();
            // Actual original Start calls CheckCodeStart -> ReconnectEngine ->
            // StartEngine/Initialise again, after those original entity creates.
            Start.Invoke(_engine, null); string after = WorkerIdentity();
            Check(Apparance.Net.Engine.IsRunning, "Actual original delayed Start successfully reconnects its native code lifecycle");
            var harmony = new Harmony("quest-native-reentry-assets");
            harmony.Patch(typeof(ApparanceEngine).GetMethod("UpdateAssets", Flags), new HarmonyMethod(typeof(QuestNativeReentryProbe).GetMethod("ResolveRecordedAssets")));
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 6)
            {
                Tick.Invoke(_engine, null);
                bool done = true;
                for (int i = 0; i < Entities.Count; i++) if (Entities[i].IsBusy || !Placements[i].IsPlacementCompleted) { done = false; break; }
                if (done) break;
                Thread.Sleep(2);
            }
            Receipt("ProbeOutput/native-reentry.json", before, after);
            bool completed = true; int objectCount = 0, groupCount = 0;
            for (int i = 0; i < Entities.Count; i++)
            {
                if (Entities[i].IsBusy || !Placements[i].IsPlacementCompleted || Observers[i].Begins != 1 || Observers[i].Ends != 1) completed = false;
                objectCount += Observers[i].Objects; groupCount += Observers[i].Groups;
            }
            Check(completed, "All previously created native handles finish actual Unity placement after original code reconnect");
            Check(before == after, "Original Awake and code reconnect retain one worker and socket owner");
            Check(File.GetLastWriteTimeUtc(workerCache).Ticks == cachedWrite, "Healthy code reconnect never rewrites the private worker cache");
            File.Copy(Path.Combine(_state, "procedural-worker.log"), "ProbeOutput/first-worker.log", true);
            Check(objectCount > 100 && groupCount > 0 && _assetReplies > 50, "Actual original nonempty procedural task streams route object and group callbacks with exact recorded asset replies");
            Tick.Invoke(_engine, null);
            for (int i = 0; i < Entities.Count; i++) Check(Observers[i].Begins == 1 && Observers[i].Ends == 1, "Original completed entity " + i + " has exactly one Begin/End continuation");
            Receipt("ProbeOutput/native-reentry.json", before, after);
            harmony.UnpatchSelf();
            foreach (var entity in Entities) Entity(entity).Dispose();
            Apparance.Net.Engine.Stop();
            Check(!Apparance.Net.Engine.IsRunning, "Original Shutdown closes its worker and native running state");
            Apparance.Net.Engine.Start(Application.streamingAssetsPath + "/Procedures", null, 1, 35, false);
            Check(Apparance.Net.Engine.IsRunning && WorkerIdentity() != after, "Explicit original shutdown permits a new native worker session");
            Check(File.GetLastWriteTimeUtc(workerCache).Ticks == cachedWrite, "Healthy explicit restart reuses exact cached worker bytes without rewriting");
            var restarted = Apparance.Net.Engine.CreateEntity(); Check(restarted.Handle != 0, "Fresh original native session creates a valid handle after shutdown"); restarted.Dispose();
            Apparance.Net.Engine.Stop();
            File.WriteAllBytes(workerCache, new byte[] { 77, 90, 0 });
            Apparance.Net.Engine.Start(Application.streamingAssetsPath + "/Procedures", null, 1, 35, false);
            Check(Apparance.Net.Engine.IsRunning && File.ReadAllBytes(workerCache).SequenceEqual(workerExpected), "Next launch atomically repairs only a truncated owned worker cache from embedded bytes");
            Apparance.Net.Engine.Stop();
            Receipt("ProbeOutput/native-reentry.json", before, after);
        }
        catch (Exception error) { UnityEngine.Debug.LogException(error); exit = 1; }
        finally { try { Apparance.Net.Engine.Stop(); } catch { } UnityEditor.EditorApplication.Exit(exit); }
    }
}
