using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

// External boundaries used by the verbatim original WaitForLoad/WaitForProcGen
// methods copied read-only into this fixture by the runner. Engine execution,
// asset IO and multiplayer transport are controlled, not hardware-qualified.
public enum EKeyActionTag { All }
public enum GameActionType { NotifyLoadingFinished, NotifyFullLoadingFinished }
public static class InputManager
{
    public static int Blocks, Releases;
    public static void RequestDisableInput(object owner, EKeyActionTag tag) { Blocks++; }
    public static void RequestEnableInput(object owner, EKeyActionTag tag) { Releases++; }
}
public static class FFSNetwork { public static bool IsOnline; }
public class LoadingPlayer { public bool IsParticipant; }
public static class PlayerRegistry
{
    public static bool WaitForOtherPlayers, WaitForOtherPlayersFullLoaded;
    public static LoadingPlayer MyPlayer;
    public static void NotifyLoadingFinished(LoadingPlayer player) { }
    public static void NotifyFullLoadingFinished(LoadingPlayer player) { }
    public static IEnumerator WaitForAllPlayersFullLoaded() { yield break; }
}
public static class Synchronizer { public static void SendSideAction(GameActionType type) { } }
public class ProceduralContentPlatformData
{
    public bool _isWaitForGeneration;
    public int _maxFramesToWaitContentCompleteness;
}
public class LoadingPlatformSetting
{
    public ProceduralContentPlatformData ProceduralContentPlatformData = new ProceduralContentPlatformData();
}
public static class PlatformLayer { public static LoadingPlatformSetting Setting = new LoadingPlatformSetting(); }
public class RoomVisibilityManager : MonoBehaviour
{
    public static RoomVisibilityManager s_Instance;
    public GameObject Maps;
}
public class NativeLoadingFixtureDriver : MonoBehaviour
{
    public void Begin() { StartCoroutine(UnityGameEditorRuntime.WaitForLoad()); }
}

/// <summary>Execute the real original loading coroutines under Unity PlayerLoop
/// and the actual Harmony patch. Original placements/native worker are explicit
/// callbacks; no test claim about generated Android geometry is made.</summary>
public static class QuestProceduralLoadingContractProbe
{
    private static int _assertions, _phase, _due, _exit;
    private static float _deadline;
    private static ApparanceEngine _engine;
    private static ProceduralMapTile _room;
    private static ProceduralBase _wall;
    private static ApparanceEntity _wallEntity;
    private static MaterialLoaderData _materialData;
    private static Material _material;
    private static NativeLoadingFixtureDriver _runner;
    private static bool _oldOptionsEnabled;
    private static UnityEditor.EnterPlayModeOptions _oldOptions;
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        _assertions++; UnityEngine.Debug.Log("PASS " + text);
    }
    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/BeforeWorld.unity");
            Scene("Gloomhaven_unified"); Scene("Game"); Scene("ProcGen");
            _oldOptions = UnityEditor.EditorSettings.enterPlayModeOptions;
            _oldOptionsEnabled = UnityEditor.EditorSettings.enterPlayModeOptionsEnabled;
            UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = true;
            UnityEditor.EditorSettings.enterPlayModeOptions = UnityEditor.EnterPlayModeOptions.DisableDomainReload
                | UnityEditor.EnterPlayModeOptions.DisableSceneReload;
            _deadline = Time.realtimeSinceStartup + 30f;
            UnityEditor.EditorApplication.update += Observe;
            UnityEditor.EditorApplication.isPlaying = true;
        }
        catch (Exception error) { Fail(error); }
    }
    private static void Observe()
    {
        try
        {
            if (Time.realtimeSinceStartup > _deadline) throw new TimeoutException("Native loading contract did not finish: " + _phase);
            if (_phase == 0 && UnityEditor.EditorApplication.isPlaying)
            {
                var engineOwner = new GameObject("Apparance Engine");
                SceneManager.MoveGameObjectToScene(engineOwner, SceneManager.GetSceneByName("Gloomhaven_unified"));
                _engine = engineOwner.AddComponent<ApparanceEngine>(); _engine.enabled = false;
                _engine.Resources = engineOwner.AddComponent<ApparanceResources>(); ApparanceEngine.Instance = _engine;
                var maps = new GameObject("Maps");
                SceneManager.MoveGameObjectToScene(maps, SceneManager.GetSceneByName("ProcGen"));
                var visibility = maps.AddComponent<RoomVisibilityManager>(); visibility.Maps = maps; RoomVisibilityManager.s_Instance = visibility;
                maps.AddComponent<ProceduralPlacementNotifierHandler>();
                _room = new GameObject("Visible requested room").AddComponent<ProceduralMapTile>();
                _room.transform.parent = maps.transform; _room.visibility = ProceduralMapTile.Visibility.All;
                var entity = _room.gameObject.AddComponent<ApparanceEntity>(); entity.m_EntityHandle = 0;
                _engine.EntityComponents.Add(entity);
                new GameObject("Generated Content").transform.parent = _room.transform;
                _wall = new GameObject("Requested original wall").AddComponent<ProceduralBase>(); _wall.transform.parent = _room.transform;
                _wallEntity = _wall.gameObject.AddComponent<ApparanceEntity>();
                _engine.EntityComponents.Add(_wallEntity);
                var hidden = new GameObject("Pending hidden preview").AddComponent<ProceduralMapTile>();
                hidden.transform.parent = maps.transform; hidden.visibility = ProceduralMapTile.Visibility.Preview;
                hidden.gameObject.AddComponent<ApparanceEntity>();
                SceneController.Instance = maps.AddComponent<SceneController>();
                QuestStandalonePlatform.Enabled = VRSession.IsRunning = true;
                VRSession.Harmony = new HarmonyLib.Harmony("quest-loading-contract");
                QuestProceduralLifecycle.Install(false);
                var original = HarmonyLib.AccessTools.Method(typeof(UnityGameEditorRuntime), "WaitForProcGen");
                Check(HarmonyLib.Harmony.GetPatchInfo(original).Postfixes.Count == 1,
                    "Actual Harmony patch wraps the original private generation coroutine");
                _runner = maps.AddComponent<NativeLoadingFixtureDriver>();
                Begin(false, 0); _due = Time.frameCount + 12; _phase = 1;
            }
            else if (_phase == 1 && Time.frameCount >= _due)
            {
                Check(_engine.enabled && ApparanceEngine.NativeStarts == 1 && ApparanceEngine.NativeUpdates > 0,
                    "Original engine callbacks run while the native loading continuation waits");
                Check(SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 0,
                    "Original load continuation cannot close before requested placement completion");
                Check(InputManager.Blocks == 1 && InputManager.Releases == 0,
                    "Native input ownership remains blocked during actual generation");
                _room.IsPlacementCompleted = true; _room.GetComponent<ApparanceEntity>().m_EntityHandle = 1;
                _due = Time.frameCount + 4; _phase = 2;
            }
            else if (_phase == 2 && Time.frameCount >= _due)
            {
                Check(SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 0,
                    "A complete room cannot bypass its requested unfinished original wall");
                _wall.IsPlacementCompleted = true; _wallEntity.IsBusy = true;
                _due = Time.frameCount + 4; _phase = 3;
            }
            else if (_phase == 3 && Time.frameCount >= _due)
            {
                Check(SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 0,
                    "Original busy state keeps the loading boundary open after an early placement callback");
                _wallEntity.IsBusy = false;
                var source = GameObject.CreatePrimitive(PrimitiveType.Cube); source.name = "Placed pending material";
                source.transform.parent = _room.transform;
                var loader = source.AddComponent<MaterialLoader>();
                _materialData = new MaterialLoaderData { Renderer = source.GetComponent<Renderer>() };
                _materialData.MaterialReferences.Add(new object()); loader.LoadersData.Add(_materialData);
                _material = _materialData.Renderer.sharedMaterial; _materialData.Renderer.enabled = false;
                _due = Time.frameCount + 4; _phase = 30;
            }
            else if (_phase == 30 && Time.frameCount >= _due)
            {
                Check(SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 0,
                    "Placed native geometry with a deferred material Start retains loading");
                _materialData._handles = new[] { new FixtureMaterialHandle { Valid = true, IsDone = false } };
                _due = Time.frameCount + 4; _phase = 31;
            }
            else if (_phase == 31 && Time.frameCount >= _due)
            {
                Check(SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 0,
                    "Actual in-flight material handles retain loading without renderer writes");
                _materialData._handles = new[] { new FixtureMaterialHandle { Valid = true, IsDone = true, Result = _material } };
                _materialData.Renderer.sharedMaterial = null;
                _due = Time.frameCount + 4; _phase = 32;
            }
            else if (_phase == 32 && Time.frameCount >= _due)
            {
                Check(SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 0,
                    "Completed material handles cannot close before original renderer assignment");
                _materialData.Renderer.sharedMaterial = _material;
                _due = Time.frameCount + 4; _phase = 4;
            }
            else if (_phase == 4 && Time.frameCount >= _due)
            {
                Check(!QuestProceduralLoading.HasPendingContent(RoomVisibilityManager.s_Instance.Maps),
                    "Hidden and preview requests do not keep the native loader open");
                Check(!SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 1
                    && InputManager.Releases == 1,
                    "Actual original continuation closes once and releases input after all requested placements finish");
                Check(!_materialData.Renderer.enabled,
                    "Assigned materials do not reverse another owner's renderer disable");
                var owned = new Material(_material);
                ScenarioEnvironmentBudget.Originals[owned] = _material;
                _materialData.Renderer.sharedMaterial = owned;
                Check(!QuestProceduralLoading.HasPendingContent(RoomVisibilityManager.s_Instance.Maps),
                    "Known optimized material ownership resolves to the original loaded source without blocking");
                _materialData._loadedMaterials = new[] { _material };
                ScenarioEnvironmentBudget.Originals.Clear();
                Check(!QuestProceduralLoading.HasPendingContent(RoomVisibilityManager.s_Instance.Maps),
                    "A complete original callback remains ready after a later material owner's replacement");
                _room.IsPlacementCompleted = false;
                // Batch Editor does not advance WaitForEndOfFrame. Execute the
                // original zero-frame enumerator explicitly, then use its
                // exhausted-budget branch in the real PlayerLoop continuation.
                var notifier = RoomVisibilityManager.s_Instance.Maps.GetComponent<ProceduralPlacementNotifierHandler>();
                notifier.UpdateProceduralCache(RoomVisibilityManager.s_Instance.Maps);
                IEnumerator capped = notifier.WaitForPlacementCompletedAll(0);
                Check(capped.MoveNext() && capped.Current is WaitForEndOfFrame && !capped.MoveNext(),
                    "Actual original zero-frame notifier finishes after its one allowed frame despite pending placements");
                Begin(true, -1); _due = Time.frameCount + 12; _phase = 5;
            }
            else if (_phase == 5 && Time.frameCount >= _due)
            {
                Check(SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 1,
                    "Original exhausted placement frame budget cannot bypass actual Quest readiness");
                _room.IsPlacementCompleted = true; _due = Time.frameCount + 4; _phase = 6;
            }
            else if (_phase == 6 && Time.frameCount >= _due)
            {
                Check(!SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 2,
                    "Original capped notifier still completes normally after the Quest readiness boundary");
                ScopeChecks();
                QuestStandalonePlatform.Enabled = false;
                _room.IsPlacementCompleted = false;
                Begin(false, 0); _due = Time.frameCount + 8; _phase = 7;
            }
            else if (_phase == 7 && Time.frameCount >= _due)
            {
                Check(!SceneController.Instance.IsLoading && SceneController.Instance.LoadingClosures == 3
                    && !_room.IsPlacementCompleted,
                    "Desktop's original optional generation wait is unchanged");
                Check(_room.GetComponent<ApparanceEntity>().m_EntityHandle == 1 && _wall.gameObject.activeSelf
                    && _wall.enabled && _wallEntity.IsPopulated && ApparanceEngine.NativeStarts == 1,
                    "Loading adaptation never writes original handles, placement, hierarchy or repeated engine lifecycle");
                Finish();
            }
        }
        catch (Exception error) { Fail(error); }
    }
    private static void ScopeChecks()
    {
        var maps = RoomVisibilityManager.s_Instance.Maps;
        var occlusion = GameObject.CreatePrimitive(PrimitiveType.Cube);
        occlusion.name = "Native object occlusion input";
        occlusion.transform.parent = _room.transform;
        occlusion.AddComponent<ObjectOcclusionVolume>();
        Renderer occlusionRenderer = occlusion.GetComponent<Renderer>(); occlusionRenderer.enabled = false;
        var occlusionLoader = occlusion.AddComponent<MaterialLoader>();
        var occlusionData = new MaterialLoaderData { Renderer = occlusionRenderer };
        occlusionData.MaterialReferences.Add(new object()); occlusionLoader.LoadersData.Add(occlusionData);
        Check(!QuestProceduralLoading.HasPendingContent(maps),
            "Native object occlusion inputs do not wait for their unused Addressables material");
        var volumes = new GameObject("Native tile occlusion owner"); volumes.transform.parent = _room.transform;
        volumes.AddComponent<TilesOcclusionVolume>();
        occlusion.transform.parent = volumes.transform;
        UnityEngine.Object.DestroyImmediate(occlusion.GetComponent<ObjectOcclusionVolume>());
        Check(!QuestProceduralLoading.HasPendingContent(maps),
            "Native tile occlusion ownership keeps auxiliary materials outside world readiness");
        occlusion.transform.parent = _room.transform;
        Check(QuestProceduralLoading.HasPendingContent(maps),
            "An equally disabled visible renderer still waits for actual material assignment");
        var preview = maps.GetComponentsInChildren<ProceduralMapTile>()[1];
        occlusion.transform.parent = preview.transform;
        // A loader in the visible room may retain a renderer reference whose
        // native presentation owner is a different, unrevealed room.
        _room.gameObject.AddComponent<MaterialLoader>().LoadersData.Add(occlusionData);
        Check(!QuestProceduralLoading.HasPendingContent(maps),
            "Cross-room material references cannot make an unrevealed preview block visible readiness");
        UnityEngine.Object.DestroyImmediate(occlusion);
        UnityEngine.Object.DestroyImmediate(volumes);
        _wall.IsPlacementCompleted = false; _wallEntity.Frozen = true;
        Check(!QuestProceduralLoading.HasPendingContent(maps), "Frozen original requests remain outside loading admission");
        _wallEntity.Frozen = false; _wallEntity.IsPopulated = false;
        Check(!QuestProceduralLoading.HasPendingContent(maps), "Unpopulated original entities never create synthetic work");
        _wallEntity.IsPopulated = true; _wallEntity.ProcedureID = 0;
        Check(!QuestProceduralLoading.HasPendingContent(maps), "Missing native procedures do not create synthetic work");
        _wallEntity.ProcedureID = 123; _wall.gameObject.SetActive(false);
        Check(!QuestProceduralLoading.HasPendingContent(maps), "Inactive original children are never activated for loading");
        _wall.gameObject.SetActive(true); _wall.enabled = false;
        Check(!QuestProceduralLoading.HasPendingContent(maps), "Disabled native observer ownership remains unchanged");
        _wall.enabled = true; _wall.IsPlacementCompleted = true;
    }
    private static void Begin(bool originalWait, int frameLimit)
    {
        PlatformLayer.Setting.ProceduralContentPlatformData._isWaitForGeneration = originalWait;
        PlatformLayer.Setting.ProceduralContentPlatformData._maxFramesToWaitContentCompleteness = frameLimit;
        SceneController.Instance.IsLoading = SceneController.Instance.ScenarioIsLoading = true;
        _runner.Begin();
    }
    private static void Scene(string name)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Additive);
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/" + name + ".unity"))
            throw new InvalidOperationException("Could not create loading fixture scene " + name);
    }
    private static void Fail(Exception error) { UnityEngine.Debug.LogException(error); _exit = 1; Finish(); }
    private static void Finish()
    {
        UnityEditor.EditorApplication.update -= Observe;
        QuestProceduralLifecycle.Uninstall(); VRSession.Harmony?.UnpatchSelf(); VRSession.Harmony = null;
        UnityEditor.EditorApplication.isPlaying = false;
        UnityEditor.EditorSettings.enterPlayModeOptions = _oldOptions;
        UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = _oldOptionsEnabled;
        Directory.CreateDirectory("ProbeOutput");
        File.WriteAllText("ProbeOutput/result.json", "{\"assertions\":" + _assertions
            + ",\"hardwareVerified\":false,\"actualPlayerLoop\":true,\"originalLoadCoroutines\":true,\"actualHarmonyPatch\":true,\"engineBoundary\":\"controlled Unity callbacks, no original engine execution\"}\n");
        UnityEditor.EditorApplication.Exit(_exit);
    }
}
