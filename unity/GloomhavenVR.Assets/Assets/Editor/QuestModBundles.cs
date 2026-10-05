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
        const string TownRoot = ContentRoot + "/TownServices";
        const string TownBundleName = "ghvr-town.bundle";
        const string VoiceBundleName = "ghvr-town-voices.bundle";
        static readonly string[] ExcludedExtensions = { ".md", ".txt", ".gitkeep", ".meta", ".cginc" };
        // These are the authored assets required by the real menu's hand/controller,
        // board and bundled-shader paths. The original main bank also contains the
        // environment shells. Full Campaign also includes the separate town banks.
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

        static readonly string[] RequiredTownAssets =
        {
            TownRoot + "/Prefabs/TownMerchant.prefab", TownRoot + "/Prefabs/TownPriestess.prefab",
            TownRoot + "/Prefabs/TownEnchantress.prefab", TownRoot + "/Prefabs/TownWorkTray.prefab",
            TownRoot + "/Shaders/TownNpc.shader", TownRoot + "/Shaders/TownEye.shader",
            TownRoot + "/Shaders/TownCornea.shader", TownRoot + "/Shaders/TownFlame.shader",
            TownRoot + "/town-facial-rig-contract.json"
        };

        [Serializable] public sealed class FileReceipt { public string path, sha256; public long size; }
        [Serializable] public sealed class BankReceipt
        {
            public string bundleName;
            public string[] assetNames, requiredAssetNames, dependencies;
            public FileReceipt bundle;
        }
        [Serializable] public sealed class Receipt
        {
            public int schema = 1;
            public string target = "Android", unityVersion, bundleName = BundleName;
            public string graphicsApi = "OpenGLES3", colorSpace = "Linear", stereoRenderingPath = "SinglePass";
            public bool typeTreesEnabled = true, chunkBasedCompression = true, townBanksIncluded = false;
            public string[] assetNames, requiredAssetNames, builtinDependencies;
            public FileReceipt bundle;
            public FileReceipt[] bundles;
            public BankReceipt[] banks;
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
            string fullGameValue = Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_FULL_GAME");
            if (!string.IsNullOrEmpty(fullGameValue) && fullGameValue != "0" && fullGameValue != "1")
                throw new InvalidOperationException("GHVR_QUEST_MOD_FULL_GAME must be 0 or 1.");
            bool fullGame = fullGameValue == "1";
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
            string[] mainDependencies = AssetDatabase.GetDependencies(assets.ToArray(), true);
            if (mainDependencies.Any(p => p.StartsWith(TownRoot + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Main Android mod bank has an unexpected town-art dependency.");
            var builds = new List<AssetBundleBuild>
            {
                new AssetBundleBuild { assetBundleName = BundleName, assetNames = assets.ToArray() }
            };
            if (fullGame)
            {
                // Match BuildTownServices.BuildBundle's authored selection exactly.
                // Authoring/rebuilding furniture or actors is ordinary mod development;
                // the Quest builder compiles their current immutable snapshot only.
                string[] art = Directory.GetFiles(TownRoot + "/Prefabs", "*.prefab", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(TownRoot + "/Shaders", "*.shader"))
                    .Concat(new[] { TownRoot + "/town-facial-rig-contract.json" })
                    .Select(path => path.Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                string[] voices = Directory.GetFiles(TownRoot + "/Audio", "*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .Select(path => path.Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                if (art.Length == 0 || !voices.Any(path => path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    || !voices.Any(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Full Quest Campaign requires authored town art, voice clips and metadata.");
                foreach (string required in RequiredTownAssets)
                    if (!art.Contains(required) || !File.Exists(required) || AssetDatabase.AssetPathToGUID(required) == string.Empty)
                        throw new InvalidOperationException("Required authored town asset is unavailable: " + required);
                foreach (string path in voices)
                {
                    if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && AssetDatabase.LoadAssetAtPath<AudioClip>(path) == null)
                        throw new InvalidOperationException("Town voice clip failed Android import: " + path);
                    if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && AssetDatabase.LoadAssetAtPath<TextAsset>(path) == null)
                        throw new InvalidOperationException("Town voice metadata failed Android import: " + path);
                }
                foreach (string path in art.Where(path => path.EndsWith(".prefab", StringComparison.Ordinal)))
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) throw new InvalidOperationException("Town prefab failed Android import: " + path);
                    foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh == null) throw new InvalidOperationException("Town prefab has a missing mesh: " + path + "/" + filter.name);
                    string furnitureName = path.EndsWith("TownPriestess.prefab", StringComparison.Ordinal) ? "Shrine"
                        : path.EndsWith("TownEnchantress.prefab", StringComparison.Ordinal) ? "Workbench" : "";
                    if (furnitureName.Length == 0) continue;
                    Transform furniture = prefab.transform.Find(furnitureName);
                    if (furniture == null || furniture.GetComponentsInChildren<Transform>(true).Length > 128)
                        throw new InvalidOperationException("Town cloth furniture exceeds its original mirror module contract: " + path);
                    int runners = furniture.GetComponentsInChildren<MeshFilter>(true)
                        .Count(filter => filter.name.StartsWith("ClothRunner_", StringComparison.Ordinal));
                    if (runners != (furnitureName == "Shrine" ? 2 : 1))
                        throw new InvalidOperationException("Town cloth runner count changed: " + path);
                }
                builds.Add(new AssetBundleBuild { assetBundleName = TownBundleName, assetNames = art });
                builds.Add(new AssetBundleBuild { assetBundleName = VoiceBundleName, assetNames = voices });
            }
            string[] dependencies = AssetDatabase.GetDependencies(builds.SelectMany(build => build.assetNames).ToArray(), true);
            var sourcePaths = new SortedSet<string>(dependencies.Where(File.Exists), StringComparer.Ordinal);
            // Shader include text is a real compiler input even when Unity omits
            // it from its serialized object dependency graph. Hash it without
            // packing it as a dead TextAsset in the bank.
            foreach (string include in Directory.GetFiles(ContentRoot, "*.cginc", SearchOption.AllDirectories))
                if (fullGame || !include.Replace('\\', '/').StartsWith(TownRoot + "/", StringComparison.Ordinal))
                    sourcePaths.Add(include.Replace('\\', '/'));
            if (fullGame)
            {
                foreach (string script in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
                    sourcePaths.Add(script.Replace('\\', '/'));
                foreach (string assembly in Directory.GetFiles("Assets", "*.asmdef", SearchOption.AllDirectories))
                    sourcePaths.Add(assembly.Replace('\\', '/'));
                // These immutable inputs are separate from Unity's generated
                // package lock and the private PlayerSettings overrides above.
                foreach (string setting in new[] { "Packages/manifest.json", "ProjectSettings/ProjectVersion.txt" })
                    if (File.Exists(setting)) sourcePaths.Add(setting);
            }
            foreach (string path in sourcePaths.ToArray())
                if (File.Exists(path + ".meta")) sourcePaths.Add(path + ".meta");
            FileReceipt[] sources = sourcePaths.Select(Describe).ToArray();
            Directory.CreateDirectory(output);
            // Keep original desktop builders and their outputs untouched. The same
            // authored paths compile their actual Android shaders/textures; type trees
            // remain enabled, and LZ4 permits LoadFromFile's normal on-demand reads.
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(output,
                builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode,
                BuildTarget.Android);
            if (manifest == null || !manifest.GetAllAssetBundles().OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(builds.Select(build => build.assetBundleName).OrderBy(name => name, StringComparer.Ordinal)))
                throw new InvalidOperationException("Android authored mod bundle manifest is absent or unexpected.");
            foreach (FileReceipt source in sources)
                if (Describe(source.path).sha256 != source.sha256)
                    throw new InvalidOperationException("Authored mod source changed during bundle build: " + source.path);
            BankReceipt[] banks = builds.Select(build => new BankReceipt
            {
                bundleName = build.assetBundleName, assetNames = build.assetNames,
                requiredAssetNames = build.assetBundleName == BundleName ? RequiredAssets
                    : build.assetBundleName == TownBundleName ? RequiredTownAssets : build.assetNames,
                dependencies = manifest.GetAllDependencies(build.assetBundleName),
                bundle = Describe(Path.Combine(output, build.assetBundleName))
            }).ToArray();
            foreach (BankReceipt bank in banks)
            {
                bank.bundle.path = bank.bundleName;
                if (bank.dependencies.Any(dependency => !builds.Any(build => build.assetBundleName == dependency)))
                    throw new InvalidOperationException("Authored Android bank requires an unshipped dependency: " + bank.bundleName);
            }
            var receipt = new Receipt
            {
                unityVersion = Application.unityVersion, assetNames = assets.ToArray(), requiredAssetNames = RequiredAssets,
                sourceFiles = sources, builtinDependencies = dependencies.Where(p => !File.Exists(p)).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                bundle = banks[0].bundle, bundles = banks.Select(bank => bank.bundle).ToArray(), banks = banks,
                townBanksIncluded = fullGame
            };
            receipt.bundle.path = BundleName;
            File.WriteAllText(Path.Combine(output, "quest-mod-bundles.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("[GloomhavenVR Quest] authored Android mod banks built: banks=" + banks.Length
                + " assets=" + banks.Sum(bank => bank.assetNames.Length) + " sourceFiles=" + sources.Length
                + " bytes=" + banks.Sum(bank => bank.bundle.size) + " fullCampaign=" + fullGame);
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
