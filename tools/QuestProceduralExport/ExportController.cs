using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using ScenarioRuleLibrary.CustomLevels;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Procedural
{
    /// <summary>Runs only in the isolated conversion host, using the original scene/load controllers.</summary>
    public sealed class ExportController : MonoBehaviour
    {
        [Serializable] public sealed class LevelInput { public string path, archive, entry; }
        [Serializable] public sealed class Configuration { public int schema; public string runId, outputRoot; public LevelInput[] levels; }
        [Serializable] sealed class StageReceipt
        {
            public int schema = 1;
            public string scope = "original-windows-campaign-generation", runId, unityVersion, scene, state, error;
            public bool nativeEngineStarted, originalSceneInitialization, originalScenarioLoaded;
            public bool geometryExported = false;
            public int sceneCount, nativeEntityCount, completedOwnerCount, pendingOwnerCount, generatedRendererCount;
            public int inputManagerObjectCount;
            public bool inputManagerInitialized;
            public string editorState, originalGameState;
            public bool sceneLoading, scenarioLoading, roomVisibilityAvailable;
        }
        static Configuration configuration;
        StageReceipt receipt;
        float start;
        float nextProgress;
        string firstError;

        public static void Initialize()
        {
            if (configuration != null) throw new InvalidOperationException("The native exporter was initialized twice.");
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "--quest-export-config");
            if (index < 0 || index + 1 >= arguments.Length) throw new ArgumentException("Native export configuration is missing.");
            configuration = JsonUtility.FromJson<Configuration>(File.ReadAllText(arguments[index + 1]));
            if (configuration == null || configuration.schema != 1 || string.IsNullOrEmpty(configuration.outputRoot)
                || configuration.levels == null || configuration.levels.Length == 0)
                throw new InvalidDataException("Native export configuration is invalid.");
            string output = Path.GetFullPath(configuration.outputRoot);
            if (!Path.IsPathRooted(configuration.outputRoot) || output.StartsWith(Application.dataPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The native exporter needs an isolated writable output root.");
            Directory.CreateDirectory(output);
            NativeCapture.Configure(Path.Combine(output, "native-requests"));
            string stateRoot = Path.Combine(output, "isolated-save-state");
            Directory.CreateDirectory(stateRoot);
            // The generated adapter is initialized before any original save or
            // worker touches a path. Never use the player's normal PC save root.
            Type paths = Type.GetType("QuestGame.Compatibility.Paths, QuestGame.Compatibility", true);
            paths.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { stateRoot });
            GameObject host = new GameObject("Build-time original campaign exporter");
            DontDestroyOnLoad(host);
            host.AddComponent<ExportController>();
        }

        public static IEnumerator LoadNativeStartup()
        {
            // Intro contains the original persistent InputManager. Even a
            // build-time movie bypass must execute that authored scene first.
            AsyncOperation intro = SceneManager.LoadSceneAsync("Intro", LoadSceneMode.Single);
            if (intro == null) throw new InvalidOperationException("The original persistent-input scene is missing.");
            yield return intro;
            AsyncOperation load = SceneManager.LoadSceneAsync("Gloomhaven_unified", LoadSceneMode.Single);
            if (load == null) throw new InvalidOperationException("The original content-initialization scene is missing.");
            yield return load;
        }

        void Awake()
        {
            start = Time.realtimeSinceStartup;
            receipt = new StageReceipt { runId = configuration.runId, unityVersion = Application.unityVersion, state = "waiting-original-startup" };
            Application.logMessageReceived += Observe;
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
            Persist();
        }

        void Update()
        {
            if (Time.realtimeSinceStartup < nextProgress) return;
            nextProgress = Time.realtimeSinceStartup + 10;
            receipt.inputManagerObjectCount = Resources.FindObjectsOfTypeAll<InputManager>().Length;
            receipt.inputManagerInitialized = Singleton<InputManager>.Instance != null;
            receipt.editorState = LevelEditorController.s_Instance != null ? LevelEditorController.s_Instance.CurrentState.ToString() : "absent";
            receipt.originalGameState = SaveData.Instance != null && SaveData.Instance.Global != null ? SaveData.Instance.Global.CurrentGameState.ToString() : "absent";
            receipt.sceneLoading = SceneController.Instance != null && SceneController.Instance.IsLoading;
            receipt.scenarioLoading = SceneController.Instance != null && SceneController.Instance.ScenarioIsLoading;
            receipt.roomVisibilityAvailable = RoomVisibilityManager.s_Instance != null;
            receipt.nativeEntityCount = ApparanceEngine.Instance != null ? ApparanceEngine.Instance.EntityComponents.Count : 0;
            receipt.completedOwnerCount = receipt.pendingOwnerCount = 0;
            foreach (ProceduralBase owner in Resources.FindObjectsOfTypeAll<ProceduralBase>())
            {
                if (!owner.gameObject.scene.IsValid() || !owner.gameObject.scene.isLoaded || !owner.gameObject.activeInHierarchy) continue;
                if (owner.IsPlacementCompleted) receipt.completedOwnerCount++; else receipt.pendingOwnerCount++;
            }
            receipt.generatedRendererCount = Resources.FindObjectsOfTypeAll<Renderer>().Count(renderer => renderer != null
                && renderer.gameObject.scene.IsValid() && renderer.gameObject.scene.isLoaded && HasGeneratedAncestor(renderer.transform));
            Persist();
        }

        IEnumerator Start()
        {
            while (SceneController.Instance == null || SaveData.Instance == null || SaveData.Instance.Global == null
                || !SceneManager.GetSceneByName("MainMenu").isLoaded || SceneController.Instance.IsLoading)
            {
                if (Expired(180)) yield break;
                yield return null;
            }
            receipt.originalSceneInitialization = true;
            receipt.nativeEngineStarted = Apparance.Net.Engine.IsRunning;
            if (!receipt.nativeEngineStarted) { Fail("Original Windows procedural engine did not initialize."); yield break; }
            receipt.state = "loading-original-campaign-level";
            Persist();
            CCustomLevelData level = null;
            try
            {
                using (FileStream input = File.OpenRead(configuration.levels[0].path))
                    level = new BinaryFormatter().Deserialize(input) as CCustomLevelData;
                if (level == null || level.ScenarioState == null) throw new InvalidDataException("Selected original Campaign level is invalid.");
                SaveData.Instance.LoadCustomLevelFromData(level, LevelEditorController.ELevelEditorState.Editing);
                SceneController.Instance.LevelEditorStart();
            }
            catch (Exception error) { Fail(error.ToString()); }
            if (receipt.error != null) yield break;
            while (RoomVisibilityManager.s_Instance == null || LevelEditorController.s_Instance == null
                || LevelEditorController.s_Instance.CurrentState != LevelEditorController.ELevelEditorState.Editing
                || SceneController.Instance.ScenarioIsLoading)
            {
                if (Expired(360)) yield break;
                yield return null;
            }
            receipt.originalScenarioLoaded = true;
            receipt.state = "waiting-original-procedural-geometry";
            Persist();
            int stable = 0;
            while (stable < 15)
            {
                int pending = 0, complete = 0;
                foreach (ProceduralBase owner in Resources.FindObjectsOfTypeAll<ProceduralBase>())
                {
                    if (!owner.gameObject.scene.IsValid() || !owner.gameObject.scene.isLoaded || !owner.gameObject.activeInHierarchy) continue;
                    if (owner.IsPlacementCompleted) complete++; else pending++;
                }
                receipt.pendingOwnerCount = pending;
                receipt.completedOwnerCount = complete;
                receipt.nativeEntityCount = ApparanceEngine.Instance != null ? ApparanceEngine.Instance.EntityComponents.Count : 0;
                stable = pending == 0 && complete > 0 ? stable + 1 : 0;
                if (Expired(480)) yield break;
                yield return null;
            }
            receipt.generatedRendererCount = Resources.FindObjectsOfTypeAll<Renderer>().Count(renderer => renderer != null
                && renderer.gameObject.scene.IsValid() && renderer.gameObject.scene.isLoaded
                && HasGeneratedAncestor(renderer.transform));
            NativeCapture.Flush();
            receipt.state = "original-geometry-ready";
            Persist();
            UnityEngine.Debug.Log("[Quest export] original native geometry ready; owners=" + receipt.completedOwnerCount
                + " renderers=" + receipt.generatedRendererCount);
            Application.Quit(receipt.generatedRendererCount > 0 ? 0 : 1);
        }

        static bool HasGeneratedAncestor(Transform child)
        {
            for (Transform current = child; current != null; current = current.parent)
                if (current.name == "Generated Content") return true;
            return false;
        }

        bool Expired(float seconds)
        {
            if (Time.realtimeSinceStartup - start <= seconds) return false;
            Fail("Original conversion stage timed out: " + receipt.state + (firstError == null ? "" : "\nFirst error: " + firstError));
            return true;
        }

        void Observe(string message, string trace, LogType type)
        {
            if (firstError == null && type is LogType.Error or LogType.Exception or LogType.Assert) firstError = message + "\n" + trace;
            if (type is LogType.Error or LogType.Exception or LogType.Assert)
                File.AppendAllText(Path.Combine(configuration.outputRoot, "original-exceptions.log"), message + "\n" + trace + "\n");
        }

        void Fail(string error)
        {
            receipt.error = error;
            receipt.state = "failed";
            Persist();
            Application.Quit(1);
        }

        void Persist()
        {
            receipt.scene = SceneManager.GetActiveScene().name;
            receipt.sceneCount = SceneManager.sceneCount;
            File.WriteAllText(Path.Combine(configuration.outputRoot, "original-campaign-stage.json"), JsonUtility.ToJson(receipt, true));
        }

        void OnDestroy() { Application.logMessageReceived -= Observe; }
    }
}
