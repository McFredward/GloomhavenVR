// GloomhavenVR — VoiceSpatialProbe: builds the headless Linux player that does the measuring.
//
// Batch:
//   Unity -batchmode -nographics -projectPath <this> -buildTarget Linux64 \
//         -executeMethod GloomhavenVR.VoiceProbe.VoiceSpatialProbeBuilder.BuildPlayer \
//         -logFile <log>
//   with VOICE_PROBE_PLAYER=<absolute output path for the executable>.
//
// WHY A PLAYER AND NOT EDITOR PLAY MODE. AudioRenderer needs play mode, and driving editor play
// mode from -executeMethod means an EditorApplication.update pump, a domain reload in the middle,
// and an exit path that batchmode does not always take. A standalone player has one entry point,
// one exit code and no domain reload; the module is installed
// (Editor/Data/PlaybackEngines/LinuxStandaloneSupport), so it is also the cheaper route.
//
// The probe scene is GENERATED here and DELETED again after the build, so nothing untracked is
// left in Assets/. The runtime script it references lives at Assets/VoiceSpatialProbe/ and stays.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloomhavenVR.VoiceProbe
{
    public static class VoiceSpatialProbeBuilder
    {
        private const string SceneDir = "Assets/VoiceSpatialProbe";
        private const string ScenePath = SceneDir + "/VoiceSpatialProbeScene.unity";

        public static void BuildPlayer()
        {
            int code = 1;
            try { code = Run(); }
            catch (Exception e) { Debug.LogError("[voice-probe] build threw: " + e); code = 1; }
            finally { EditorApplication.Exit(code); }
        }

        private static int Run()
        {
            string outPath = Environment.GetEnvironmentVariable("VOICE_PROBE_PLAYER");
            if (string.IsNullOrEmpty(outPath))
            {
                Debug.LogError("[voice-probe] VOICE_PROBE_PLAYER is not set (absolute path to the executable to write).");
                return 2;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            // ---- generate the scene ---------------------------------------------------------
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("VoiceSpatialProbeRunner");
            go.AddComponent<VoiceSpatialProbeRunner>();
            if (!Directory.Exists(SceneDir)) Directory.CreateDirectory(SceneDir);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("[voice-probe] could not save " + ScenePath);
                return 3;
            }
            AssetDatabase.Refresh();

            // ---- build ----------------------------------------------------------------------
            // Player, NOT the Server/headless subtarget: a dedicated-server build strips the audio
            // subsystem, which would turn this instrument into a silence generator with a CSV.
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;

            PreloadOpenXrSettings();

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outPath,
                target = BuildTarget.StandaloneLinux64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,   // keeps the managed stack traces readable
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[voice-probe] build {s.result}: {s.totalSize} bytes, {s.totalErrors} errors, " +
                      $"{s.totalWarnings} warnings -> {outPath}");

            // ---- clean up the generated scene ------------------------------------------------
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.Refresh();

            return s.result == BuildResult.Succeeded ? 0 : 4;
        }

        /// <summary>
        /// Makes com.unity.xr.openxr's package settings exist and be REGISTERED before the build
        /// starts, because otherwise it fails the build outright, twice over.
        ///
        /// OpenXRPackageSettings.Instance (Editor/OpenXRPackageSettings.cs:26-50) does
        /// EditorBuildSettings.AddConfigObject and then — outside the `ret != null` branch, so it
        /// fires whether or not the asset exists — throws
        ///     BuildFailedException("OpenXR Settings found in project but not yet loaded. Please build again.")
        /// if BuildPipeline.isBuildingPlayer. Its own comment says the registration cannot be done
        /// mid-build. "Build again" does NOT self-heal from batchmode: each invocation is a fresh
        /// editor process that finds no config object and throws in exactly the same place. So the
        /// registration is done HERE, before BuildPipeline.BuildPlayer, which is the one moment at
        /// which that code path is willing to do it.
        ///
        /// Reflection because the type is internal to Unity.XR.OpenXR.Editor. Failure is logged and
        /// not fatal: if the package is ever removed, the build simply proceeds without it.
        /// </summary>
        private static void PreloadOpenXrSettings()
        {
            try
            {
                var t = Type.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings, Unity.XR.OpenXR.Editor");
                if (t == null) { Debug.Log("[voice-probe] OpenXR editor assembly not present — nothing to preload."); return; }
                var m = t.GetMethod("GetOrCreateInstance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);
                if (m == null) { Debug.LogWarning("[voice-probe] OpenXRPackageSettings.GetOrCreateInstance not found."); return; }
                object o = m.Invoke(null, null);
                AssetDatabase.SaveAssets();
                Debug.Log($"[voice-probe] OpenXR package settings preloaded: {(o == null ? "null" : o.ToString())}. " +
                          "NOTE this writes Assets/XR/Settings/'OpenXR Package Settings.asset' and adds a " +
                          "config object to ProjectSettings/EditorBuildSettings.asset — the XR packages' own " +
                          "bootstrap, not something this probe invented.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[voice-probe] OpenXR preload failed (continuing): " + e.Message);
            }
        }
    }
}
