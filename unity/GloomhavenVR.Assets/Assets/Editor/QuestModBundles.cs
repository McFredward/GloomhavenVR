using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR
{
    /// <summary>Build existing authored mod art for Android in private builder output.</summary>
    public static class QuestModBundles
    {
        const string BundleName = "gloomhavenvr.bundle";
        const string ContentRoot = "Assets/Bundle";
        static readonly string[] ExcludedExtensions = { ".md", ".txt", ".gitkeep", ".meta", ".cginc" };
        // These are the authored assets required by the real menu's hand/controller,
        // board and bundled-shader paths. The original main bank also contains the
        // environment shells. Town art/voices are distinct later campaign banks.
        static readonly string[] RequiredAssets =
        {
            "Assets/Bundle/Hands/VRHand_L.prefab", "Assets/Bundle/Hands/VRHand_R.prefab",
            "Assets/Bundle/Hands/VRHandPlate_L.prefab", "Assets/Bundle/Hands/VRHandPlate_R.prefab",
            "Assets/Bundle/Hands/VRHandArcane_L.prefab", "Assets/Bundle/Hands/VRHandArcane_R.prefab",
            "Assets/Bundle/Controllers/quest3/Controller_quest3_left.prefab",
            "Assets/Bundle/Controllers/quest3/Controller_quest3_right.prefab",
            "Assets/Bundle/Controllers/generic/Controller_generic_left.prefab",
            "Assets/Bundle/Controllers/generic/Controller_generic_right.prefab",
            "Assets/Bundle/Head/Mask_0.prefab", "Assets/Bundle/Head/Mask_1.prefab", "Assets/Bundle/Head/Mask_2.prefab",
            "Assets/Bundle/Table/MapTable.prefab", "Assets/Bundle/Table/PlayTray.prefab",
            "Assets/Bundle/Table/PlayTray_9capjqp6.prefab", "Assets/Bundle/Table/PlayTray_16vm268h.prefab",
            "Assets/Bundle/Table/Overlay.shader", "Assets/Bundle/Table/BoardLit.shader",
            "Assets/Bundle/Table/MapUnlit.shader", "Assets/Bundle/Table/WindowMaterialise.shader",
            "Assets/Bundle/Environments/Env_Swamp.prefab", "Assets/Bundle/Environments/Env_Cellar.prefab"
        };

        [Serializable] public sealed class FileReceipt { public string path, sha256; public long size; }
        [Serializable] public sealed class Receipt
        {
            public int schema = 1;
            public string target = "Android", unityVersion, bundleName = BundleName;
            public string graphicsApi = "OpenGLES3", colorSpace = "Linear", stereoRenderingPath = "SinglePass";
            public bool typeTreesEnabled = true, chunkBasedCompression = true, townBanksIncluded = false;
            public string[] assetNames, requiredAssetNames, builtinDependencies;
            public FileReceipt bundle;
            public FileReceipt[] sourceFiles;
        }

        /// <summary>Batch entry; must run in a copied project using exact Unity2021.3.5.</summary>
        public static void BuildAll()
        {
            try
            {
                Build();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[GloomhavenVR Quest] authored mod bundle build failed: " + error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        static void Build()
        {
            if (Application.unityVersion != "2021.3.5f1") throw new InvalidOperationException("Android mod bundles require exact Unity2021.3.5f1.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Launch the private mod-art project with -buildTarget Android.");
            // Match the generated Quest player's actual rendering contract rather
            // than the copied desktop project's graphics and stereo defaults.
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            string configured = Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT");
            if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathRooted(configured))
                throw new InvalidOperationException("GHVR_QUEST_MOD_BUNDLE_OUTPUT must select fresh absolute private output.");
            string output = Path.GetFullPath(configured);
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (output == project || output.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || project.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new InvalidOperationException("Android mod bundle output must be empty and outside the source project and its desktop outputs.");
            var assets = new List<string>();
            foreach (string file in Directory.GetFiles(ContentRoot, "*", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (path.StartsWith(ContentRoot + "/TownServices/", StringComparison.Ordinal)
                    || ExcludedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) || Path.GetFileName(path).StartsWith(".")
                    || AssetDatabase.AssetPathToGUID(path) == string.Empty) continue;
                assets.Add(path);
            }
            assets.Sort(StringComparer.Ordinal);
            foreach (string required in RequiredAssets)
                if (!assets.Contains(required)) throw new InvalidOperationException("Required authored mod asset is unavailable: " + required);
            string[] dependencies = AssetDatabase.GetDependencies(assets.ToArray(), true);
            if (dependencies.Any(p => p.StartsWith(ContentRoot + "/TownServices/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Main Android mod bank has an unexpected town-art dependency.");
            var sourcePaths = new SortedSet<string>(dependencies.Where(File.Exists), StringComparer.Ordinal);
            // Shader include text is a real compiler input even when Unity omits
            // it from its serialized object dependency graph. Hash it without
            // packing it as a dead TextAsset in the bank.
            foreach (string include in Directory.GetFiles(ContentRoot, "*.cginc", SearchOption.AllDirectories))
                if (!include.Replace('\\', '/').StartsWith(ContentRoot + "/TownServices/", StringComparison.Ordinal))
                    sourcePaths.Add(include.Replace('\\', '/'));
            foreach (string path in sourcePaths.ToArray())
                if (File.Exists(path + ".meta")) sourcePaths.Add(path + ".meta");
            FileReceipt[] sources = sourcePaths.Select(Describe).ToArray();
            Directory.CreateDirectory(output);
            // Keep original desktop builders and their outputs untouched. The same
            // authored paths compile their actual Android shaders/textures; type trees
            // remain enabled, and LZ4 permits LoadFromFile's normal on-demand reads.
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = BundleName, assetNames = assets.ToArray() } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode,
                BuildTarget.Android);
            if (manifest == null || !manifest.GetAllAssetBundles().SequenceEqual(new[] { BundleName }))
                throw new InvalidOperationException("Android authored mod bundle manifest is absent or unexpected.");
            foreach (FileReceipt source in sources)
                if (Describe(source.path).sha256 != source.sha256)
                    throw new InvalidOperationException("Authored mod source changed during bundle build: " + source.path);
            var receipt = new Receipt
            {
                unityVersion = Application.unityVersion, assetNames = assets.ToArray(), requiredAssetNames = RequiredAssets,
                sourceFiles = sources, builtinDependencies = dependencies.Where(p => !File.Exists(p)).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                bundle = Describe(Path.Combine(output, BundleName))
            };
            receipt.bundle.path = BundleName;
            File.WriteAllText(Path.Combine(output, "quest-mod-bundles.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("[GloomhavenVR Quest] authored Android mod bank built: assets=" + assets.Count
                + " sourceFiles=" + sources.Length + " bytes=" + receipt.bundle.size + " sha256=" + receipt.bundle.sha256);
        }

        static FileReceipt Describe(string path)
        {
            string hash;
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            return new FileReceipt { path = path.Replace('\\', '/'), size = new FileInfo(path).Length, sha256 = hash };
        }
    }
}
