// GloomhavenVR companion project — XR RuntimeDeps harvester.
//
// Builds a throwaway Windows Mono player, then collects the managed XR
// assemblies + native OpenXR plugins + UnitySubsystems manifest into the
// repository root (../../ from this project):
//
//   libs/RuntimeDeps/                       managed DLLs (Assembly.LoadFile'd
//                                           by the BepInEx plugin at runtime)
//   libs/Natives/Plugins/x86_64/            UnityOpenXR.dll, openxr_loader.dll
//   libs/Natives/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json
//   libs/RuntimeDeps/harvest-manifest.json  version bookkeeping (packages,
//                                           editor version, build date)
//
// Menu:  GloomhavenVR > Harvest RuntimeDeps (dummy build + collect)
//        GloomhavenVR > Harvest RuntimeDeps (collect only)
// Batch: Unity.exe -batchmode -nographics -projectPath <this project>
//        -buildTarget Win64
//        -executeMethod GloomhavenVR.RuntimeDepsHarvester.BuildAndHarvest
//        -logFile harvest.log
//        (do NOT pass -quit; the method calls EditorApplication.Exit itself)
//
// Why a real player build: the managed package assemblies
// (Unity.XR.Management.dll, Unity.XR.OpenXR.dll, ...) are compiled from
// package source BY THIS EDITOR, so a 2021.3 editor produces the
// 2021.3-compatible binaries the mod must ship (TOOLCHAIN.md §5.2, risk R5).
// The natives are prebuilt inside the com.unity.xr.openxr package and are
// taken from Library/PackageCache (exact same files the build would copy).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloomhavenVR
{
    public static class RuntimeDepsHarvester
    {
        private const string DummyScenePath = "Assets/DummyHarvestScene.unity";
        private const string DummyBuildDir = "Build/DummyPlayer";
        private const string DummyExeName = "GHVRDummy.exe";

        // Managed assemblies to ship (LCVR/RepoXR RuntimeDeps list, adapted —
        // see .planning/research/TOOLCHAIN.md §5.2).
        private static readonly string[] RequiredManaged =
        {
            "Unity.XR.Management.dll",
            "Unity.XR.OpenXR.dll",
            "Unity.XR.CoreUtils.dll",
            "Unity.XR.Interaction.Toolkit.dll",
            // Newer than the game's 1.3.0 — the resolved package (1.7.0 via
            // XRIT 2.6.5) is harvested so the mod can upgrade the game's copy
            // if Phase 1/2 decides it is needed.
            "Unity.InputSystem.dll",
        };

        // Nice-to-have; the game already ships some of these (version parity
        // is checked by the mod at install time, not here).
        private static readonly string[] OptionalManaged =
        {
            "UnityEngine.SpatialTracking.dll",
            "Unity.XR.OpenXR.Features.MockRuntime.dll",
            "Unity.XR.OpenXR.Features.RuntimeDebugger.dll",
            "Unity.XR.OpenXR.Features.ConformanceAutomation.dll",
        };

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        private static string RepoRoot => Path.GetFullPath(Path.Combine(ProjectRoot, "..", ".."));
        private static string RuntimeDepsDir => Path.Combine(RepoRoot, "libs", "RuntimeDeps");
        private static string NativesDir => Path.Combine(RepoRoot, "libs", "Natives");

        [MenuItem("GloomhavenVR/Harvest RuntimeDeps (dummy build + collect)")]
        public static void BuildAndHarvestFromMenu()
        {
            BuildDummyPlayer();
            Harvest();
        }

        [MenuItem("GloomhavenVR/Harvest RuntimeDeps (collect only)")]
        public static void HarvestFromMenu() => Harvest();

        /// <summary>Batch-mode entry point: dummy build + harvest, then exit 0/1.</summary>
        public static void BuildAndHarvest()
        {
            try
            {
                BuildDummyPlayer();
                Harvest();
                Debug.Log("[GloomhavenVR] BuildAndHarvest OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR] BuildAndHarvest FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        /// <summary>Batch-mode entry point: harvest from an existing dummy build.</summary>
        public static void HarvestOnly()
        {
            try
            {
                Harvest();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR] HarvestOnly FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void BuildDummyPlayer()
        {
            // Keep every package assembly in the player — nothing may be stripped.
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone,
                ManagedStrippingLevel.Disabled);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,
                ScriptingImplementation.Mono2x); // the game is Mono

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, DummyScenePath);

            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { DummyScenePath },
                    locationPathName = Path.Combine(DummyBuildDir, DummyExeName),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                };

                Debug.Log("[GloomhavenVR] Building dummy Windows Mono player...");
                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new Exception($"Dummy player build failed: {report.summary.result} "
                        + $"({report.summary.totalErrors} error(s)). "
                        + "Is 'Windows Build Support (Mono)' installed for this editor?");
            }
            finally
            {
                AssetDatabase.DeleteAsset(DummyScenePath);
            }
        }

        private static void Harvest()
        {
            var managedDir = Path.Combine(ProjectRoot, DummyBuildDir,
                Path.GetFileNameWithoutExtension(DummyExeName) + "_Data", "Managed");
            if (!Directory.Exists(managedDir))
                throw new Exception($"Dummy build Managed folder not found: {managedDir}. "
                    + "Run the dummy build first (BuildAndHarvest).");

            Directory.CreateDirectory(RuntimeDepsDir);
            var harvested = new List<string>();

            foreach (var dll in RequiredManaged)
            {
                var src = Path.Combine(managedDir, dll);
                if (!File.Exists(src))
                    throw new Exception($"Required managed assembly missing from dummy build: {dll}");
                File.Copy(src, Path.Combine(RuntimeDepsDir, dll), overwrite: true);
                harvested.Add(dll);
            }

            foreach (var dll in OptionalManaged)
            {
                var src = Path.Combine(managedDir, dll);
                if (!File.Exists(src)) continue;
                File.Copy(src, Path.Combine(RuntimeDepsDir, dll), overwrite: true);
                harvested.Add(dll);
            }

            var openXrVersion = HarvestNatives();
            WriteSubsystemsManifest(openXrVersion);
            WriteHarvestManifest(harvested, openXrVersion);

            Debug.Log($"[GloomhavenVR] Harvest complete:\n"
                      + $"  managed  -> {RuntimeDepsDir}\n"
                      + $"  natives  -> {NativesDir}\n"
                      + $"  packages -> harvest-manifest.json");
        }

        /// <summary>
        /// Copies UnityOpenXR.dll + openxr_loader.dll out of the resolved
        /// com.unity.xr.openxr package (Library/PackageCache). These natives are
        /// prebuilt in the package — the player build merely copies them, so the
        /// package cache is the authoritative, version-exact source.
        /// Returns the resolved package version (for the subsystems manifest).
        /// </summary>
        private static string HarvestNatives()
        {
            var openXr = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == "com.unity.xr.openxr");
            if (openXr == null)
                throw new Exception("com.unity.xr.openxr is not resolved in this project.");

            var pkgRoot = openXr.resolvedPath;
            var candidates = new Dictionary<string, string>
            {
                // package-relative source -> destination file name
                { Path.Combine("Runtime", "windows", "x64", "UnityOpenXR.dll"), "UnityOpenXR.dll" },
                { Path.Combine("RuntimeLoaders", "windows", "x64", "openxr_loader.dll"), "openxr_loader.dll" },
            };

            var destDir = Path.Combine(NativesDir, "Plugins", "x86_64");
            Directory.CreateDirectory(destDir);

            foreach (var kv in candidates)
            {
                var src = Path.Combine(pkgRoot, kv.Key);
                if (!File.Exists(src))
                    throw new Exception($"Native plugin not found in package: {src}");
                File.Copy(src, Path.Combine(destDir, kv.Value), overwrite: true);
            }

            return openXr.version;
        }

        /// <summary>
        /// Writes the UnitySubsystems manifest the preloader installs to
        /// Gloomhaven_Data/UnitySubsystems/UnityOpenXR/ (TOOLCHAIN.md §5.2).
        /// The version field MUST match the OpenXR package the DLLs came from.
        /// </summary>
        private static void WriteSubsystemsManifest(string openXrVersion)
        {
            var dir = Path.Combine(NativesDir, "UnitySubsystems", "UnityOpenXR");
            Directory.CreateDirectory(dir);
            var json =
"{\n" +
"  \"name\": \"OpenXR XR Plugin\",\n" +
$"  \"version\": \"{openXrVersion}\",\n" +
"  \"libraryName\": \"UnityOpenXR\",\n" +
"  \"displays\": [ { \"id\": \"OpenXR Display\" } ],\n" +
"  \"inputs\":   [ { \"id\": \"OpenXR Input\" } ]\n" +
"}\n";
            File.WriteAllText(Path.Combine(dir, "UnitySubsystemsManifest.json"), json);
        }

        private static void WriteHarvestManifest(List<string> harvestedFiles, string openXrVersion)
        {
            var trackedPackages = new[]
            {
                "com.unity.xr.management",
                "com.unity.xr.openxr",
                "com.unity.xr.core-utils",
                "com.unity.xr.interaction.toolkit",
                "com.unity.inputsystem",
                "com.unity.xr.legacyinputhelpers",
            };
            var all = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Where(p => trackedPackages.Contains(p.name))
                .OrderBy(p => p.name)
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"unityVersion\": \"{Application.unityVersion}\",");
            sb.AppendLine($"  \"buildDateUtc\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\",");
            sb.AppendLine($"  \"gameUnityVersion\": \"2021.3.5f1\",");
            sb.AppendLine($"  \"gameInputSystemVersion\": \"1.3.0\",");
            sb.AppendLine($"  \"subsystemsManifestVersion\": \"{openXrVersion}\",");
            sb.AppendLine("  \"packages\": {");
            for (int i = 0; i < all.Count; i++)
                sb.AppendLine($"    \"{all[i].name}\": \"{all[i].version}\"{(i < all.Count - 1 ? "," : "")}");
            sb.AppendLine("  },");
            sb.AppendLine("  \"managedFiles\": [");
            for (int i = 0; i < harvestedFiles.Count; i++)
                sb.AppendLine($"    \"{harvestedFiles[i]}\"{(i < harvestedFiles.Count - 1 ? "," : "")}");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"nativeFiles\": [");
            sb.AppendLine("    \"Plugins/x86_64/UnityOpenXR.dll\",");
            sb.AppendLine("    \"Plugins/x86_64/openxr_loader.dll\",");
            sb.AppendLine("    \"UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json\"");
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            Directory.CreateDirectory(RuntimeDepsDir);
            File.WriteAllText(Path.Combine(RuntimeDepsDir, "harvest-manifest.json"), sb.ToString());
        }
    }
}
