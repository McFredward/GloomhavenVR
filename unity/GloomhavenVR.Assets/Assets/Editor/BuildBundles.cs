// GloomhavenVR companion project — AssetBundle builder.
//
// Menu:  GloomhavenVR > Build AssetBundles
// Batch: Unity.exe -batchmode -nographics -projectPath <this project>
//        -buildTarget Win64 -executeMethod GloomhavenVR.AssetsBuilder.BuildAll
//        -logFile build-bundles.log
//        (do NOT pass -quit; BuildAll calls EditorApplication.Exit itself)
//
// Rules (see .planning/research/TOOLCHAIN.md §4.1):
//  - Target StandaloneWindows64 (the game is a Windows x64 player).
//  - TypeTrees stay ON: never pass BuildAssetBundleOptions.DisableWriteTypeTree.
//    Without TypeTrees any serialization-layout drift between this editor and
//    the game's 2021.3.5f1 runtime crashes the load.
//  - Everything under Assets/Bundle/ goes into a single bundle
//    "gloomhavenvr.bundle" (docs/markdown/license files are excluded).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class AssetsBuilder
    {
        private const string BundleName = "gloomhavenvr.bundle";
        private const string ContentRoot = "Assets/Bundle";
        private const string OutputDir = "Build/Bundles";

        // Never bundled: documentation / license / bookkeeping files.
        private static readonly string[] ExcludedExtensions =
            { ".md", ".txt", ".gitkeep", ".meta" };

        [MenuItem("GloomhavenVR/Build AssetBundles")]
        public static void BuildFromMenu()
        {
            var manifest = Build();
            if (manifest != null)
                Debug.Log($"[GloomhavenVR] Bundles written to {Path.GetFullPath(OutputDir)}");
        }

        /// <summary>Batch-mode entry point. Exits the editor with 0/1.</summary>
        public static void BuildAll()
        {
            try
            {
                var manifest = Build();
                if (manifest == null)
                    throw new Exception("BuildAssetBundles returned null (see editor log above).");
                Debug.Log("[GloomhavenVR] BuildAll OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR] BuildAll FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static AssetBundleManifest Build()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                Debug.LogWarning("[GloomhavenVR] Active build target is "
                    + EditorUserBuildSettings.activeBuildTarget
                    + "; bundles are still built for StandaloneWindows64 explicitly.");

            var assets = CollectBundleAssets();
            if (assets.Count == 0)
                throw new Exception($"No bundleable assets found under {ContentRoot}/ — nothing to build.");

            Debug.Log($"[GloomhavenVR] Building '{BundleName}' with {assets.Count} asset(s):\n  "
                      + string.Join("\n  ", assets));

            Directory.CreateDirectory(OutputDir);

            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = assets.ToArray(),
                },
            };

            // BuildAssetBundleOptions.None keeps TypeTrees enabled (default) and
            // deterministic bundle content. Do not add DisableWriteTypeTree.
            return BuildPipeline.BuildAssetBundles(
                OutputDir,
                builds,
                BuildAssetBundleOptions.None,
                BuildTarget.StandaloneWindows64);
        }

        private static List<string> CollectBundleAssets()
        {
            var result = new List<string>();
            if (!Directory.Exists(ContentRoot))
                return result;

            foreach (var file in Directory.GetFiles(ContentRoot, "*", SearchOption.AllDirectories))
            {
                var path = file.Replace('\\', '/');
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var name = Path.GetFileName(path);
                if (ExcludedExtensions.Contains(ext) || name.StartsWith("."))
                    continue;
                // Only things Unity actually imported as assets.
                if (AssetDatabase.AssetPathToGUID(path) == string.Empty)
                    continue;
                result.Add(path);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
