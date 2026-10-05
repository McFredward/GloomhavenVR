#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Local, completed native validation evidence; never a partial-coverage shortcut.</summary>
    public static class QuestCampaignShaderCache
    {
        public const string MarkerName = "completed-validation.json";
        public const string LegacyConsumer = "d1e5de9d9c1743e25bbc891c91cefef53d527bd8a1b95918abb49e787bd9a3d3";
        public const string VulkanVerifier = "3e85b950523bd4a4ef9ef58140836bc047d9d27263f1fd0966347841dfbce7a7";
        public const string SmolvVerifier = "bdfff94920bb7ffdfed224cad4126f9c987cbf8d68ef8b7ae904f5a38fdbe0fc";
        const string MigrationConsumer = "c43e6ee16a80512f1411a8e91bd0a0e6d72fcb25205e65a8fda956bbf8ecfc95";
        const string EditorRoot = "Assets/Quest/Editor/";
        [Serializable] public sealed class FileRecord { public string path, sha256; public long size; }
        [Serializable] public sealed class Closure
        {
            public int schema = 1;
            public string unityVersion, manifestSha256, settingsSha256, graphicsApi;
            public FileRecord[] files;
        }
        [Serializable] public sealed class Marker
        {
            public int schema = 1, shaderCount, aliasCount, materialCount;
            public bool completed;
            public string closureSha256, receiptSha256, legacySnapshotSha256, legacyLogSha256, legacyProvenanceSha256;
            public FileRecord[] outputs;
        }
        [Serializable] public sealed class LegacySnapshot
        {
            public int schema, shaderCount, aliasCount;
            public string scope, originalConsumerSha256;
            public bool unchangedBetweenCaptures, receiptCompletionObserved;
            public FileRecord[] files;
        }
        [Serializable] sealed class LegacyProvenance
        {
            public int schema;
            public FileRecord[] stagedEditorSources;
            public FileRecord campaignShaderManifest;
            public LegacyToolchain toolchain;
        }
        [Serializable] sealed class LegacyToolchain { public string unityVersion, editorSha256; }
        // Unknown PlayerSettings fields remain in the fingerprint. Only these
        // packaging/CIL/runtime fields are excluded; they cannot change the
        // explicitly queried native shader programs. The graphics settings,
        // every tier and every shader dependency remain separately bound.
        static readonly HashSet<string> NonGraphicsPlayerFields = new HashSet<string>(StringComparer.Ordinal) {
            "companyName", "productName", "bundleVersion", "applicationIdentifier", "overrideDefaultApplicationIdentifier",
            "AndroidBundleVersionCode", "AndroidKeystoreName", "AndroidKeyaliasName", "androidUseCustomKeystore",
            "scriptingDefineSymbols", "scriptingBackend", "il2cppCompilerConfiguration", "additionalIl2CppArgs",
            "apiCompatibilityLevelPerPlatform", "scriptingRuntimeVersion", "gcIncremental", "gcWBarrierValidation",
            "preloadedAssets", "m_BuildTargetIcons", "m_BuildTargetPlatformIcons", "metroPackageName", "metroPackageVersion"
        };

        public static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes)); }
        static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public static string FileHash(string path) { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(stream)); }
        static bool Digest(string value) { return value != null && Regex.IsMatch(value, "^[0-9a-f]{64}$"); }
        public static string ClosureHash(Closure closure) { return Hash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(closure))); }
        static FileRecord Record(string path, string label)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Native graphics dependency is missing: " + label);
            return new FileRecord { path = label, sha256 = FileHash(path), size = new FileInfo(path).Length };
        }
        public static string GraphicsPlayerSettings(string text)
        {
            var fields = Regex.Matches(text, @"(?m)^  ([A-Za-z_]\w*):");
            if (fields.Count == 0) throw new InvalidOperationException("Unrecognized Unity PlayerSettings representation.");
            var result = new StringBuilder(text.Substring(0, fields[0].Index));
            for (int index = 0; index < fields.Count; ++index)
                if (!NonGraphicsPlayerFields.Contains(fields[index].Groups[1].Value))
                    result.Append(text.Substring(fields[index].Index, (index + 1 < fields.Count ? fields[index + 1].Index : text.Length) - fields[index].Index));
            return result.ToString();
        }

        public static Closure Capture(QuestCampaignShaderValidation.Manifest input, string manifestPath)
        {
            if (Application.unityVersion != "2021.3.5f1" || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android || input.graphicsApi != "Vulkan")
                throw new InvalidOperationException("Completed native cache requires original-version Android/Vulkan.");
            var paths = new SortedDictionary<string, string>(StringComparer.Ordinal);
            Action<string, string> add = (path, label) => { if (!File.Exists(path)) throw new InvalidOperationException("Native graphics closure is incomplete: " + label); paths[label] = path; };
            Action<string> asset = path => {
                SafeAsset(path); add(path, path);
                if (File.Exists(path + ".meta")) add(path + ".meta", path + ".meta");
            };
            foreach (var row in input.shaders) asset(row.assetPath);
            foreach (var row in input.programs ?? new QuestCampaignShaderValidation.SourceProgram[0]) asset(row.assetPath);
            foreach (string dependency in AssetDatabase.GetDependencies(input.shaders.Select(row => row.assetPath).ToArray(), true))
                if (dependency.StartsWith("Assets/", StringComparison.Ordinal) || dependency.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    if (dependency.StartsWith("Assets/", StringComparison.Ordinal)) asset(dependency);
                    else {
                        string packagePath = dependency;
                        if (!File.Exists(packagePath))
                        {
                            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(dependency);
                            if (package == null) throw new InvalidOperationException("Native shader package dependency cannot be resolved.");
                            packagePath = Path.Combine(package.resolvedPath, dependency.Substring(("Packages/" + package.name + "/").Length));
                        }
                        add(packagePath, dependency); if (File.Exists(packagePath + ".meta")) add(packagePath + ".meta", dependency + ".meta");
                    }
                }
                else if (File.Exists(dependency)) throw new InvalidOperationException("Native graphics closure has an unrecognized engine dependency.");
            foreach (string folder in new[] {"Assets/Quest/Settings", "Assets/XR/Settings"})
                if (Directory.Exists(folder)) foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) add(path, path.Replace('\\', '/'));
            foreach (string path in new[] {"ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/EditorSettings.asset", "ProjectSettings/ProjectVersion.txt", "Packages/manifest.json", "Packages/packages-lock.json"}) add(path, path);
            foreach (string name in new[] {"QuestCampaignShaderValidation.cs", "QuestVulkanShaderValidation.cs", "QuestSmolvDecoder.cs", "QuestCampaignShaderCache.cs"}) add(EditorRoot + name, EditorRoot + name);
            string contents = EditorApplication.applicationContentsPath;
            add(EditorApplication.applicationPath, "editor/Unity");
            foreach (string path in Directory.GetFiles(Path.Combine(contents, "CGIncludes"), "*", SearchOption.AllDirectories))
                add(path, "editor/CGIncludes/" + path.Substring(Path.Combine(contents, "CGIncludes").Length + 1).Replace('\\', '/'));
            string tools = Path.Combine(contents, "Tools");
            if (!File.Exists(Path.Combine(tools, "UnityShaderCompiler")) && !File.Exists(Path.Combine(tools, "UnityShaderCompiler.exe")))
                throw new InvalidOperationException("Actual UnityShaderCompiler is missing.");
            foreach (string path in Directory.GetFiles(tools))
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path) == "UnityShaderCompiler" || Path.GetFileName(path) == "UnityShaderCompiler.exe")
                    add(path, "editor/Tools/" + Path.GetFileName(path));
            foreach (string name in new[] {"UnityEditor.CoreModule.dll", "UnityEngine.CoreModule.dll"})
            {
                var found = Directory.GetFiles(Path.Combine(contents, "Managed"), name, SearchOption.AllDirectories);
                if (found.Length != 1) throw new InvalidOperationException("Native graphics API module identity is ambiguous.");
                add(found[0], "editor/Managed/" + name);
            }
            var android = Directory.GetFiles(Path.Combine(contents, "PlaybackEngines", "AndroidPlayer"), "UnityEditor.Android.Extensions.dll", SearchOption.AllDirectories);
            if (android.Length != 1) throw new InvalidOperationException("Actual Android Editor module is missing or ambiguous.");
            add(android[0], "editor/Android/UnityEditor.Android.Extensions.dll");
            // A persisted file and the currently effective graphics APIs must
            // agree. A caller changing settings without saving cannot reuse.
            string settings = GraphicsPlayerSettings(File.ReadAllText("ProjectSettings/ProjectSettings.asset"));
            string effective = string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)) + "|" + PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) + "|" + PlayerSettings.colorSpace + "|" + PlayerSettings.stereoRenderingPath;
            var records = paths.Select(row => Record(row.Value, row.Key)).ToList();
            // Catch concurrent input replacement during this capture.
            foreach (var row in records) if (FileHash(paths[row.path]) != row.sha256) throw new InvalidOperationException("Native graphics input changed during capture.");
            foreach (var row in input.shaders)
            {
                var importer = AssetImporter.GetAtPath(row.assetPath);
                if (!(importer is ShaderImporter)) throw new InvalidOperationException("Native shader importer type changed.");
                records.Add(new FileRecord { path = "importer/" + row.guid,
                    sha256 = Hash(Encoding.UTF8.GetBytes(importer.GetType().AssemblyQualifiedName + "\n" + AssetDatabase.GetAssetDependencyHash(row.assetPath))), size = 0 });
            }
            return new Closure { unityVersion = Application.unityVersion, graphicsApi = input.graphicsApi, manifestSha256 = FileHash(manifestPath),
                settingsSha256 = Hash(Encoding.UTF8.GetBytes(settings + "\n" + effective)), files = records.OrderBy(row => row.path, StringComparer.Ordinal).ToArray() };
        }

        static void SafeAsset(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("\\") || path.Split('/').Any(part => part == "." || part == ".." || part.Length == 0))
                throw new InvalidOperationException("Native graphics asset path escapes Assets.");
        }
        public static string AliasKey(string guid, int subshader, int pass, int tier, string[] keywords)
        {
            if (keywords == null || keywords.Distinct().Count() != keywords.Length) throw new InvalidOperationException("Native alias keywords are absent or duplicated.");
            return guid + ":" + subshader + ":" + pass + ":" + tier + ":" + string.Join(" ", keywords.OrderBy(value => value, StringComparer.Ordinal));
        }
        public static FileRecord[] VerifyReceipt(QuestCampaignShaderValidation.Manifest input, string manifestHash, string outputPath)
        {
            var receipt = JsonUtility.FromJson<QuestCampaignShaderValidation.Receipt>(File.ReadAllText(Path.Combine(outputPath, "android-compiler.json")));
            if (receipt == null || receipt.schema != 1 || receipt.unityVersion != "2021.3.5f1" || receipt.graphicsApi != "Vulkan" || receipt.compilerPlatform != "Vulkan" ||
                receipt.sourceManifestSha256 != manifestHash || receipt.materialCount != input.materials.Length || receipt.originalPixelParityVerified || receipt.headsetPictureVerified || receipt.programs == null)
                throw new InvalidOperationException("Completed native shader receipt has stale or missing provenance.");
            var expected = new Dictionary<string, QuestCampaignShaderValidation.Variant>(StringComparer.Ordinal);
            foreach (var shader in input.shaders) foreach (var bank in shader.variants)
                expected.Add(AliasKey(shader.guid, bank.subshader, bank.pass, bank.hardwareTier, bank.keywords), bank);
            if (receipt.programs.Length != expected.Count) throw new InvalidOperationException("Completed native shader receipt omits original aliases.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var program in receipt.programs)
            {
                if (program == null) throw new InvalidOperationException("Completed native shader receipt has a null alias.");
                string key = AliasKey(program.guid, program.subshader, program.pass, program.hardwareTier, program.keywords);
                QuestCampaignShaderValidation.Variant original;
                if (!seen.Add(key) || !expected.TryGetValue(key, out original) || original.stereo != program.stereo || !program.vertexCompiled || !program.fragmentCompiled ||
                    original.stereo == "multiview" && ((!program.vertexEyeRoutingObserved && !original.viewInvariant) || original.requiresFragmentEyeRouting && !program.fragmentEyeRoutingObserved))
                    throw new InvalidOperationException("Completed native shader receipt changes original alias coverage.");
                AddOutput(files, program.file, program.bankSha256, ".vulkan");
                AddOutput(files, program.vertexFile, program.vertexSha256, ".vertex.spv");
                AddOutput(files, program.fragmentFile, program.fragmentSha256, ".fragment.spv");
            }
            return files.Select(row => {
                var actual = Record(Path.Combine(outputPath, row.Key), row.Key);
                if (actual.sha256 != row.Value) throw new InvalidOperationException("Completed native evidence output changed: " + row.Key);
                return actual;
            }).ToArray();
        }
        static void AddOutput(IDictionary<string, string> files, string name, string checksum, string suffix)
        {
            if (!Digest(checksum) || name != checksum + suffix) throw new InvalidOperationException("Completed native evidence has an unsafe or unhashed bank address.");
            string prior;
            if (files.TryGetValue(name, out prior) && prior != checksum) throw new InvalidOperationException("Completed native output name collision.");
            files[name] = checksum;
        }
        public static bool TryReuse(QuestCampaignShaderValidation.Manifest input, Closure closure, string outputPath)
        {
            string path = Path.Combine(outputPath, MarkerName);
            if (!File.Exists(path)) return false;
            try
            {
                var marker = JsonUtility.FromJson<Marker>(File.ReadAllText(path));
                if (marker == null || marker.schema != 1 || !marker.completed || marker.closureSha256 != ClosureHash(closure) || marker.shaderCount != input.shaders.Length || marker.materialCount != input.materials.Length ||
                    marker.aliasCount != input.shaders.Sum(row => row.variants.Length) || marker.receiptSha256 != FileHash(Path.Combine(outputPath, "android-compiler.json"))) return false;
                var outputs = VerifyReceipt(input, closure.manifestSha256, outputPath);
                return JsonUtility.ToJson(new OutputSet { files = outputs }) == JsonUtility.ToJson(new OutputSet { files = marker.outputs });
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is InvalidOperationException)
            { Debug.Log("Completed native shader cache rejected: " + error.Message); return false; }
        }
        [Serializable] sealed class OutputSet { public FileRecord[] files; }
        public static void Invalidate(string outputPath)
        {
            string path = Path.Combine(outputPath, MarkerName);
            if (File.Exists(path)) File.Delete(path);
        }
        public static void Complete(QuestCampaignShaderValidation.Manifest input, Closure before, Closure after, string outputPath, string legacySnapshot = null, string legacyLog = null, string legacyProvenance = null)
        {
            if (ClosureHash(before) != ClosureHash(after)) throw new InvalidOperationException("Native graphics inputs changed during complete validation.");
            var outputs = VerifyReceipt(input, before.manifestSha256, outputPath);
            var marker = new Marker { completed = true, shaderCount = input.shaders.Length, aliasCount = input.shaders.Sum(row => row.variants.Length), materialCount = input.materials.Length,
                closureSha256 = ClosureHash(before), receiptSha256 = FileHash(Path.Combine(outputPath, "android-compiler.json")), outputs = outputs,
                legacySnapshotSha256 = legacySnapshot, legacyLogSha256 = legacyLog, legacyProvenanceSha256 = legacyProvenance };
            string path = Path.Combine(outputPath, MarkerName), temp = path + ".tmp";
            Invalidate(outputPath);
            File.WriteAllText(temp, JsonUtility.ToJson(marker, true) + "\n", new UTF8Encoding(false));
            // Same-directory rename publishes the marker only after complete
            // receipt/output/input validation. Leftover .tmp is never accepted.
            File.Move(temp, path);
        }

        /// <summary>Explicit migration of the observed f905 full PASS; never automatic old-receipt trust.</summary>
        public static void SeedLegacyCompleted()
        {
            string manifestPath = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_MANIFEST") ?? QuestCampaignShaderValidation.DefaultManifest;
            string outputPath = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_OUTPUT") ?? "QuestCampaignShaderEvidence";
            string snapshotPath = Required("GHVR_QUEST_SHADER_LEGACY_SNAPSHOT"), logPath = Required("GHVR_QUEST_SHADER_LEGACY_LOG"), provenancePath = Required("GHVR_QUEST_SHADER_LEGACY_PROVENANCE");
            var input = JsonUtility.FromJson<QuestCampaignShaderValidation.Manifest>(File.ReadAllText(manifestPath));
            var snapshot = JsonUtility.FromJson<LegacySnapshot>(File.ReadAllText(snapshotPath));
            var provenance = JsonUtility.FromJson<LegacyProvenance>(File.ReadAllText(provenancePath));
            if (input.scope != "campaign-compiler" || input.shaders.Length != 688 || input.shaders.Sum(row => row.variants.Length) != 51564 ||
                snapshot == null || snapshot.schema != 1 || snapshot.scope != "completed-native-validation-legacy-input-snapshot" || !snapshot.unchangedBetweenCaptures ||
                snapshot.shaderCount != 688 || snapshot.aliasCount != 51564 || snapshot.originalConsumerSha256 != LegacyConsumer || snapshot.files == null ||
                provenance == null || provenance.schema != 1 || provenance.stagedEditorSources == null || provenance.toolchain == null || provenance.toolchain.unityVersion != "2021.3.5f1")
                throw new InvalidOperationException("Legacy migration lacks the captured original completed-validator provenance.");
            string log = File.ReadAllText(logPath);
            if (!log.Contains("PASS Campaign Android shader gate: materials=" + input.materials.Length + ", actual banks=51564."))
                throw new InvalidOperationException("Legacy full native validation did not reach actual PASS.");
            var original = snapshot.files.ToDictionary(row => row.path, StringComparer.Ordinal);
            foreach (var expected in new[] {new FileRecord {path=EditorRoot+"QuestCampaignShaderValidation.cs",sha256=LegacyConsumer},new FileRecord {path=EditorRoot+"QuestVulkanShaderValidation.cs",sha256=VulkanVerifier},new FileRecord {path=EditorRoot+"QuestSmolvDecoder.cs",sha256=SmolvVerifier}})
                if (!original.ContainsKey(expected.path) || original[expected.path].sha256 != expected.sha256 || provenance.stagedEditorSources.Count(row => row.path == expected.path && row.sha256 == expected.sha256) != 1)
                    throw new InvalidOperationException("Legacy native validator helpers differ from the exact source-proven version.");
            if (FileHash(EditorRoot + "QuestCampaignShaderValidation.cs") != MigrationConsumer || FileHash(EditorRoot + "QuestVulkanShaderValidation.cs") != VulkanVerifier || FileHash(EditorRoot + "QuestSmolvDecoder.cs") != SmolvVerifier)
                throw new InvalidOperationException("Legacy migration cannot bless unknown native verifier drift.");
            var before = Capture(input, manifestPath);
            foreach (var row in before.files)
            {
                if (row.path == EditorRoot + "QuestCampaignShaderValidation.cs" || row.path == EditorRoot + "QuestCampaignShaderCache.cs" || row.path.StartsWith("importer/", StringComparison.Ordinal)) continue;
                FileRecord old;
                if (!original.TryGetValue(row.path, out old) || old.sha256 != row.sha256 || old.size != row.size)
                    throw new InvalidOperationException("Legacy graphics dependency drift requires real native validation: " + row.path);
            }
            // PlayerSettings is normalized for subsequent ordinary retries,
            // but the one-time legacy migration requires its original bytes.
            string player = "ProjectSettings/ProjectSettings.asset";
            if (!original.ContainsKey(player) || original[player].sha256 != FileHash(player) || !original.ContainsKey(manifestPath) || original[manifestPath].sha256 != before.manifestSha256 ||
                provenance.campaignShaderManifest == null || provenance.campaignShaderManifest.sha256 != before.manifestSha256 || provenance.toolchain.editorSha256 != original["editor/Unity"].sha256)
                throw new InvalidOperationException("Legacy native validation inputs/settings changed after the live snapshot.");
            QuestCampaignShaderValidation.VerifyImportedIdentities(input);
            Complete(input, before, Capture(input, manifestPath), outputPath, FileHash(snapshotPath), FileHash(logPath), FileHash(provenancePath));
            Debug.Log("Sealed observed legacy complete Campaign shader PASS: all688 shaders/all51564 aliases; outputs and unchanged source/settings/tool closure verified. No native sweep repeated.");
        }
        static string Required(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value) || !File.Exists(value)) throw new InvalidOperationException("Legacy validation migration requires " + name);
            return value;
        }
    }
}
#endif
