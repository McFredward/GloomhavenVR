using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using GloomhavenVR.Core;

/// <summary>Exercise the actual installed MonoBehaviour in Unity's PlayerLoop,
/// rather than calling its Tick method and assuming installation/message delivery.
/// ApparanceEngine is a controlled Unity callback seam, not original managed/native
/// engine execution; the bridge and actual generated world remain hardware gates.</summary>
public static class QuestProceduralPlayerLoopProbe
{
    private static int _assertions, _phase, _exit, _failureReads;
    private static float _due, _deadline;
    private static ApparanceEngine _engine;
    private static GameObject _driver;
    private static UnityEditor.EnterPlayModeOptions _oldOptions;
    private static bool _oldOptionsEnabled;
    private static void Check(bool value, string text)
    {
        if (!value) throw new InvalidOperationException(text);
        _assertions++;
        Debug.Log("PASS " + text);
    }
    public static void Run()
    {
        try
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/BeforeWorld.unity"))
                throw new InvalidOperationException("Could not retain the initial empty fixture scene.");
            NativeScene("Gloomhaven_unified"); NativeScene("Game"); NativeScene("ProcGen");
            _oldOptions = UnityEditor.EditorSettings.enterPlayModeOptions;
            _oldOptionsEnabled = UnityEditor.EditorSettings.enterPlayModeOptionsEnabled;
            UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = true;
            UnityEditor.EditorSettings.enterPlayModeOptions = UnityEditor.EnterPlayModeOptions.DisableDomainReload
                | UnityEditor.EnterPlayModeOptions.DisableSceneReload;
            _deadline = Time.realtimeSinceStartup + 45f;
            UnityEditor.EditorApplication.update += Observe;
            UnityEditor.EditorApplication.isPlaying = true;
        }
        catch (Exception error) { Fail(error); }
    }
    private static void Observe()
    {
        try
        {
            float now = Time.realtimeSinceStartup;
            if (now > _deadline) throw new TimeoutException("Actual Unity lifecycle did not finish: phase=" + _phase);
            if (_phase == 0 && UnityEditor.EditorApplication.isPlaying)
            {
                var owner = new GameObject("Apparance Engine");
                SceneManager.MoveGameObjectToScene(owner, SceneManager.GetSceneByName("Gloomhaven_unified"));
                _engine = owner.AddComponent<ApparanceEngine>(); _engine.enabled = false;
                ApparanceEngine.Instance = _engine; _engine.Resources = owner.AddComponent<ApparanceResources>();
                var tile = new GameObject("Requested native tile").AddComponent<ProceduralMapTile>();
                tile.visibility = ProceduralMapTile.Visibility.All;
                var entity = tile.gameObject.AddComponent<ApparanceEntity>(); entity.m_EntityHandle = 0;
                _engine.EntityComponents.Add(entity);
                new GameObject("Generated Content").transform.parent = tile.transform;
                // Preserve an actual controlled pre-existing non-room handle,
                // as independently created by original Entity.Start before
                // Engine.Start. This was B681's false global veto on hardware.
                var pooled = new GameObject("Pooled pre-existing entity").AddComponent<ApparanceEntity>();
                pooled.m_EntityHandle = 71; pooled.gameObject.SetActive(false); _engine.EntityComponents.Add(pooled);
                var preview = new GameObject("Generated preview room").AddComponent<ProceduralMapTile>();
                preview.visibility = ProceduralMapTile.Visibility.Preview; preview.IsPlacementCompleted = true;
                var previewEntity = preview.gameObject.AddComponent<ApparanceEntity>();
                previewEntity.m_EntityHandle = 72; _engine.EntityComponents.Add(previewEntity);
                var generatedPreview = new GameObject("Generated Content"); generatedPreview.transform.parent = preview.transform;
                new GameObject("Original preview geometry").transform.parent = generatedPreview.transform;
                SceneController.Instance = new GameObject("SceneController").AddComponent<SceneController>();
                QuestStandalonePlatform.Enabled = true; VRSession.IsRunning = true;
                Check(!Application.isFocused, "Headless PlayerLoop exposes actual false desktop window focus");
                // Native generation is not an input action. An actual reported XR
                // focus loss under a system keyboard must not suppress its lifecycle.
                VRSession.InputFocus = false;
                ApparanceEngine.NativeStarts = ApparanceEngine.NativeUpdates = 0;
                QuestProceduralLifecycle.Install(false);
                Directory.CreateDirectory("ProbeOutput");
                _driver = GameObject.Find("GloomhavenVR.QuestProceduralLifecycle");
                Check(_driver != null && QuestProceduralLifecycle.Describe(_engine).Contains("driver=True"),
                    "Real install creates the actual lifecycle driver");
                QuestProceduralLifecycle.Install(false);
                Check(UnityEngine.Object.FindObjectsOfType<MonoBehaviour>().Count(component => component.gameObject.name == "GloomhavenVR.QuestProceduralLifecycle") == 1,
                    "Repeated installation retains one owned driver");
                _driver.SendMessage("OnApplicationPause", true);
                _due = now + 4.5f; _phase = 1;
            }
            else if (_phase == 1 && now >= _due)
            {
                string state = QuestProceduralLifecycle.Describe(_engine);
                Check(!_engine.enabled && ApparanceEngine.NativeStarts == 0 && ApparanceEngine.NativeUpdates == 0,
                    "Actual OS pause messages prevent native Start and Update");
                Check(state.Contains("admission=os-paused") && !state.Contains("ticks=0 "),
                    "Actual PlayerLoop reports its live driver and pause admission");
                _driver.SendMessage("OnApplicationPause", false);
                _due = now + 1f; _phase = 2;
            }
            else if (_phase == 2 && now >= _due)
            {
                Check(!_engine.enabled && ApparanceEngine.NativeStarts == 0,
                    "Actual PlayerLoop cannot bypass the initial native stability grace");
                _due = now + 3.5f; _phase = 3;
            }
            else if (_phase == 3 && now >= _due)
            {
                string state = QuestProceduralLifecycle.Describe(_engine);
                Check(_engine.enabled && ApparanceEngine.NativeStarts == 1 && ApparanceEngine.NativeUpdates > 0,
                    "Actual installed driver restores native Start and Update despite input focus loss");
                Check(_engine.EntityComponents[0].m_EntityHandle == 0
                    && _engine.EntityComponents[1].m_EntityHandle == 71 && !_engine.EntityComponents[1].gameObject.activeSelf
                    && _engine.EntityComponents[2].m_EntityHandle == 72,
                    "Actual PlayerLoop leaves requested, pooled and preview entity handles unchanged");
                Check(state.Contains("codeStarted=True") && state.Contains("xrFocus=False") && state.Contains("recovery=attempted"),
                    "Actual lifecycle evidence separates original continuation from input focus");
                File.WriteAllText("ProbeOutput/playerloop-state.txt", state + "\n");
                _engine.enabled = false;
                _due = now + 4.5f; _phase = 4;
            }
            else if (_phase == 4 && now >= _due)
            {
                Check(!_engine.enabled && ApparanceEngine.NativeStarts == 1,
                    "Actual installed driver never fights a second disable owner");
                QuestProceduralLifecycle.Uninstall();
                _due = now + .5f; _phase = 5;
            }
            else if (_phase == 5 && now >= _due)
            {
                Check(_driver == null, "Actual uninstall retires the owned driver");
                ApparanceEngine.FailCensus = true;
                QuestProceduralLifecycle.Install(false);
                _driver = GameObject.Find("GloomhavenVR.QuestProceduralLifecycle");
                _due = now + 1.5f; _phase = 6;
            }
            else if (_phase == 6 && now >= _due)
            {
                string state = QuestProceduralLifecycle.Describe(_engine);
                Check(!_engine.enabled && _driver.GetComponent<MonoBehaviour>().enabled == false && VRLog.Alerts == 1,
                    "Observer failure disables only its owned driver and reports one normal-level anomaly");
                Check(state.Contains("admission=observation-failed") && state.Contains("recovery=not-admitted"),
                    "Failure evidence preserves original native ownership without a repair attempt");
                _failureReads = ApparanceEngine.CensusReads;
                _due = now + 2f; _phase = 7;
            }
            else if (_phase == 7 && now >= _due)
            {
                Check(ApparanceEngine.CensusReads == _failureReads && VRLog.Alerts == 1 && !_engine.enabled,
                    "A failed observer never retries native census or logs another anomaly");
                File.AppendAllText("ProbeOutput/playerloop-state.txt", QuestProceduralLifecycle.Describe(_engine) + "\n");
                QuestProceduralLifecycle.Uninstall();
                ApparanceEngine.FailCensus = false;
                _phase = 8; UnityEditor.EditorApplication.isPlaying = false;
            }
            else if (_phase == 8 && !UnityEditor.EditorApplication.isPlaying) Finish();
        }
        catch (Exception error) { Fail(error); }
    }
    private static void NativeScene(string name)
    {
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Additive);
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/" + name + ".unity"))
            throw new InvalidOperationException("Could not create exact native scene fixture: " + name);
    }
    private static void Fail(Exception error)
    {
        Debug.LogException(error); _exit = 1;
        UnityEditor.EditorApplication.update -= Observe;
        UnityEditor.EditorApplication.isPlaying = false;
        Finish();
    }
    private static void Finish()
    {
        UnityEditor.EditorApplication.update -= Observe;
        UnityEditor.EditorSettings.enterPlayModeOptions = _oldOptions;
        UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = _oldOptionsEnabled;
        Directory.CreateDirectory("ProbeOutput");
        File.WriteAllText("ProbeOutput/result.json", "{\"assertions\":" + _assertions + ",\"hardwareVerified\":false,\"actualPlayerLoop\":true,\"engineBoundary\":\"controlled Unity callbacks, no original engine execution\"}\n");
        UnityEditor.EditorApplication.Exit(_exit);
    }
}
