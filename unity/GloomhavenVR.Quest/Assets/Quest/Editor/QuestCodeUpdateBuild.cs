using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Compiles complete player code while importing no original game assets.</summary>
    public static class QuestCodeUpdateBuild
    {
        [Serializable] internal sealed class ScriptType { public string assembly, @namespace, @class, propertiesHash; public int executionOrder; }
        [Serializable] sealed class PreparationProgress
        {
            public int schema = 1, done, total;
            public string operation = "update-code", unit = "tasks", phase, status, detail;
        }
        sealed class ScriptOrder
        {
            public MonoScript script;
            public string key;
            public int previous, expected;
        }
        [Serializable] sealed class Input
        {
            public int schema, versionCode;
            public string unityVersion, packageAbi, versionName;
            public ScriptType[] scriptTypes;
        }
        [Serializable] sealed class FileReceipt { public string path, sha256; public long size; }
        [Serializable] sealed class Receipt
        {
            public int schema = 1;
            public string scope = "quest-code-update-v1", unityVersion, packageAbi, abi = "arm64-v8a", buildResult;
            public bool originalAssetsImported;
            public FileReceipt[] files;
        }
        [Serializable] sealed class SdkReceipt
        {
            public int schema = 1;
            public string target = "Android", backend = "IL2CPP", compilation = "Player",
                options = "DevelopmentBuild|Assertions", unityVersion;
        }
        static string Required(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing code update input: " + name);
            return value;
        }
        static Input Configure()
        {
            var input = JsonUtility.FromJson<Input>(File.ReadAllText(Required("GHVR_QUEST_CODE_INPUT")));
            if (input == null || input.schema != 1 || input.unityVersion != Application.unityVersion
                || input.scriptTypes == null || input.scriptTypes.Length == 0 || input.versionCode <= 0)
                throw new InvalidDataException("Code update requires the exact original Unity version and script roster.");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Code update Android build target switch failed.");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, Required("GHVR_QUEST_PACKAGE"));
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_4_6);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Minimal);
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.gcIncremental = true;
            PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetAdditionalIl2CppArgs("--compiler-flags=-fbracket-depth=1024 --linker-flags=-fuse-ld=lld");
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel30;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.bundleVersion = input.versionName;
            PlayerSettings.Android.bundleVersionCode = input.versionCode;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, "GHVR_QUEST_STARTUP;GHVR_QUEST_GAME");
            // Match the original player's legacy and Input System APIs before
            // either SDK compilation or native AOT compilation strips code.
            var settings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var inputHandler = settings.FindProperty("activeInputHandler");
            if (inputHandler == null) throw new InvalidDataException("Code update could not configure the original input backends.");
            inputHandler.intValue = 2;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return input;
        }

        // Package ABI discovery must not enter the full Campaign shader/content
        // build helper. The caller subsequently binds the staged original DLLs
        // against these actual Android player assemblies.
        public static void CompileSdk()
        {
            using (new AndroidToolsOverride()) CompileSdkPlayer();
        }
        static void CompileSdkPlayer()
        {
            Configure();
            const string output = "QuestStartupEvidence/PlayerSdk";
            Directory.CreateDirectory(output);
            var result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings {
                group = BuildTargetGroup.Android, target = BuildTarget.Android,
                options = ScriptCompilationOptions.DevelopmentBuild | ScriptCompilationOptions.Assertions
            }, output);
            if (result.assemblies == null || result.assemblies.Count == 0)
                throw new InvalidOperationException("Code-only actual Android SDK compilation produced no assemblies.");
            File.WriteAllText(output + "/compilation.json", JsonUtility.ToJson(new SdkReceipt { unityVersion = Application.unityVersion }, true));
            Debug.Log("[Quest code update] player SDK compiled; assemblies=" + result.assemblies.Count);
        }

        public static void Build()
        {
            using (new AndroidToolsOverride()) BuildPlayer();
        }
        static void BuildPlayer()
        {
            var input = Configure();
            var roots = PrepareOriginalScripts(input.scriptTypes);
            // MonoScript assets carry only class identity/property metadata.
            // They make the original roster available to the linker and the
            // output gate without loading original scenes, prefabs or textures.
            Progress("update-code-preload", 0, 3, "Preserving original serialized script roots");
            var preloaded = roots.ToArray();
            if (!PlayerSettings.GetPreloadedAssets().SequenceEqual(preloaded)) PlayerSettings.SetPreloadedAssets(preloaded);
            Progress("update-code-preload", 1, 3, "Original serialized script roots retained");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory("Assets/QuestCodeUpdate");
            const string scenePath = "Assets/QuestCodeUpdate/CodeOnly.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            Progress("update-code-preload", 2, 3, "Minimal native-code compilation scene saved");
            AssetDatabase.SaveAssets();
            Progress("update-code-preload", 3, 3, "Minimal native-code project preparation complete");
            string output = Required("GHVR_QUEST_CODE_OUTPUT");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Progress("update-code-handover", 0, 1, "Handing the prepared code-only project to the Android compiler");
            Progress("update-code-handover", 1, 1, "Android native-code compilation starting");
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                target = BuildTarget.Android, scenes = new[] { scenePath },
                locationPathName = output, options = BuildOptions.Development
            });
            var files = new List<FileReceipt>();
            if (result.summary.result == BuildResult.Succeeded)
                using (var zip = System.IO.Compression.ZipFile.OpenRead(output))
                    foreach (string name in new[] { "lib/arm64-v8a/libil2cpp.so", "assets/bin/Data/Managed/Metadata/global-metadata.dat" })
                    {
                        var entry = zip.GetEntry(name);
                        if (entry == null) throw new InvalidDataException("Native code pair is incomplete: " + name);
                        using (var source = entry.Open()) using (var sha = SHA256.Create())
                            files.Add(new FileReceipt { path = name, size = entry.Length,
                                sha256 = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant() });
                    }
            File.WriteAllText(output + ".code-build.json", JsonUtility.ToJson(new Receipt {
                unityVersion = Application.unityVersion, packageAbi = input.packageAbi,
                buildResult = result.summary.result.ToString(), files = files.ToArray(), originalAssetsImported = false
            }, true) + "\n");
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Code-only Android build failed: " + result.summary.result + ", errors=" + result.summary.totalErrors);
            Debug.Log("[Quest code update] matching ARM64 IL2CPP native pair built; original asset conversion/import was not run.");
        }

        // Support163304 spent 2265 seconds repeatedly importing the same game
        // plugin before it ever reached the native compiler. SetExecutionOrder
        // imports even an unchanged value. Validate the whole original roster
        // first, retain exact roots, and batch only actual metadata repairs.
        // Kept callable so the real Editor fixture exercises this production
        // boundary without converting game assets or building an Android APK.
        internal static List<UnityEngine.Object> PrepareOriginalScripts(ScriptType[] rows)
        {
            if (rows == null || rows.Length == 0) throw new InvalidDataException("The original serialized script roster is empty.");
            var scripts = new Dictionary<string, MonoScript>(StringComparer.Ordinal);
            var catalog = MonoImporter.GetAllRuntimeMonoScripts();
            Progress("update-code-catalog", 0, catalog.Length, "Reading the imported runtime script catalog");
            int catalogDone = 0;
            foreach (var script in catalog)
            {
                string key = ScriptIdentity(script);
                if (key != null && !scripts.ContainsKey(key)) scripts.Add(key, script);
                CountedProgress("update-code-catalog", ++catalogDone, catalog.Length, key ?? "Non-runtime script inspected");
            }
            var roots = new List<UnityEngine.Object>();
            var orders = new Dictionary<int, ScriptOrder>();
            Progress("update-code-roster", 0, rows.Length, "Matching the retained original serialized script roster");
            for (int i = 0; i < rows.Length; ++i)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrEmpty(row.assembly) || string.IsNullOrEmpty(row.@class))
                    throw new InvalidDataException("The original serialized script roster contains an invalid row.");
                string key = row.assembly + "|" + row.@namespace + "|" + row.@class;
                if (!scripts.TryGetValue(key, out MonoScript script))
                {
                    // Engine default scripts are already emitted by the same
                    // exact editor's default resource bank, even when the public
                    // MonoImporter catalog excludes those native assets. The
                    // closed output roster still must prove every one matches.
                    if (row.assembly.StartsWith("UnityEngine.", StringComparison.Ordinal) && row.assembly.EndsWith("Module.dll", StringComparison.Ordinal))
                    { CountedProgress("update-code-roster", i + 1, rows.Length, key); continue; }
                    throw new InvalidDataException("Retained original serialized script is missing: " + key + "; a full build is required.");
                }
                // Source/package metadata can differ from the original player
                // (for example the private UGUI EventSystem). Preserve its
                // actual imported order before writing the replacement roster.
                int id = script.GetInstanceID();
                if (orders.TryGetValue(id, out ScriptOrder previous))
                {
                    if (previous.expected != row.executionOrder)
                        throw new InvalidDataException("The original serialized script roster has conflicting execution orders: " + key);
                }
                else orders.Add(id, new ScriptOrder { script = script, key = key,
                    previous = MonoImporter.GetExecutionOrder(script), expected = row.executionOrder });
                roots.Add(script);
                CountedProgress("update-code-roster", i + 1, rows.Length, key);
            }
            var repairs = orders.Values.Where(order => order.previous != order.expected).ToArray();
            int done = 0, total = repairs.Length + orders.Count + (repairs.Length == 0 ? 0 : 1);
            Progress("update-code-orders", 0, total, "Preserving original script execution orders; repairs=" + repairs.Length);
            var applied = new List<ScriptOrder>();
            try
            {
                if (repairs.Length != 0)
                {
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (var repair in repairs)
                        {
                            applied.Add(repair);
                            MonoImporter.SetExecutionOrder(repair.script, repair.expected);
                            CountedProgress("update-code-orders", ++done, total, repair.key);
                        }
                    }
                    finally { AssetDatabase.StopAssetEditing(); }
                    Progress("update-code-orders", ++done, total, "Changed script metadata imported as one batch");
                }
                foreach (var order in orders.Values)
                {
                    if (MonoImporter.GetExecutionOrder(order.script) != order.expected)
                        throw new InvalidDataException("Retained original script execution order could not be restored: " + order.key);
                    CountedProgress("update-code-orders", ++done, total, order.key);
                }
            }
            catch
            {
                if (applied.Count != 0)
                {
                    AssetDatabase.StartAssetEditing();
                    try { foreach (var order in applied) MonoImporter.SetExecutionOrder(order.script, order.previous); }
                    finally { AssetDatabase.StopAssetEditing(); }
                }
                throw;
            }
            Debug.Log("[Quest code update] original script roster prepared; rows=" + rows.Length +
                ", uniqueScripts=" + orders.Count + ", executionOrderRepairs=" + repairs.Length);
            return roots;
        }

        static readonly MethodInfo SourceAssemblyName = typeof(MonoScript).GetMethod("GetAssemblyName", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo SourceNamespace = typeof(MonoScript).GetMethod("GetNamespace", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static string ScriptIdentity(MonoScript script)
        {
            // Partial/static source-file scripts can have a null GetClass.
            // Keep their exact MonoScript identity instead of reflection alone.
            var serialized = new SerializedObject(script);
            var assembly = serialized.FindProperty("m_AssemblyName");
            var ns = serialized.FindProperty("m_Namespace");
            var name = serialized.FindProperty("m_ClassName");
            Type type = script.GetClass();
            string assemblyName, namespaceName, className;
            if (assembly != null && ns != null && name != null && !string.IsNullOrEmpty(assembly.stringValue) && !string.IsNullOrEmpty(name.stringValue))
            { assemblyName = assembly.stringValue; namespaceName = ns.stringValue; className = name.stringValue; }
            else if (type != null)
            { assemblyName = type.Assembly.GetName().Name + ".dll"; namespaceName = type.Namespace ?? ""; className = type.Name; }
            else
            {
                string path = AssetDatabase.GetAssetPath(script);
                if (!path.EndsWith(".cs", StringComparison.Ordinal)) return null;
                // Support170547 reaches a real package MonoScript whose whole
                // source is disabled by UNITY_2023_2_OR_NEWER. GetClass is null
                // on our pinned 2021 Editor, but the full player's serialized
                // roster still retains its namespace. Read the same native
                // MonoScript identity that Unity serializes; a filename with an
                // invented empty namespace cannot qualify that original row.
                if (SourceAssemblyName == null || SourceNamespace == null)
                    throw new InvalidDataException("The pinned Editor cannot read the native source script identity: " + path);
                assemblyName = SourceAssemblyName.Invoke(script, null) as string;
                namespaceName = SourceNamespace.Invoke(script, null) as string ?? "";
                className = script.name;
                if (string.IsNullOrEmpty(assemblyName)) assemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(path);
                if (string.IsNullOrEmpty(assemblyName)) return null;
            }
            if (!assemblyName.EndsWith(".dll", StringComparison.Ordinal)) assemblyName += ".dll";
            return assemblyName + "|" + namespaceName + "|" + className;
        }

        static void CountedProgress(string phase, int done, int total, string detail)
        {
            if (done == total || done % 32 == 0) Progress(phase, done, total, detail);
        }
        static void Progress(string phase, int done, int total, string detail)
        {
            Debug.Log("GHVRQ_PROGRESS " + JsonUtility.ToJson(new PreparationProgress { phase = phase,
                done = done, total = total, status = done == total ? "complete" : "progress", detail = detail }));
        }

        sealed class AndroidToolsOverride : IDisposable
        {
            readonly List<Action> restore = new List<Action>();
            public AndroidToolsOverride()
            {
                try
                {
                    Apply("GHVR_QUEST_ANDROID_SDK", "SdkUseEmbedded", "AndroidSdkRoot", v => AndroidExternalToolsSettings.sdkRootPath = v, () => AndroidExternalToolsSettings.sdkRootPath);
                    Apply("GHVR_QUEST_ANDROID_NDK", "NdkUseEmbedded", "AndroidNdkRootR21D", v => AndroidExternalToolsSettings.ndkRootPath = v, () => AndroidExternalToolsSettings.ndkRootPath);
                    Apply("GHVR_QUEST_JDK", "JdkUseEmbedded", "JdkPath", v => AndroidExternalToolsSettings.jdkRootPath = v, () => AndroidExternalToolsSettings.jdkRootPath);
                }
                catch { Dispose(); throw; }
            }
            void Apply(string variable, string embeddedKey, string rootKey, Action<string> setter, Func<string> getter)
            {
                string value = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(value)) return;
                string expected = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(expected)) throw new InvalidDataException("Android code-build tool override is missing: " + variable);
                bool hasEmbedded = EditorPrefs.HasKey(embeddedKey), embedded = EditorPrefs.GetBool(embeddedKey, true), hasRoot = EditorPrefs.HasKey(rootKey);
                string previous = EditorPrefs.GetString(rootKey);
                restore.Add(() => {
                    if (hasRoot) EditorPrefs.SetString(rootKey, previous); else EditorPrefs.DeleteKey(rootKey);
                    if (hasEmbedded) EditorPrefs.SetBool(embeddedKey, embedded); else EditorPrefs.DeleteKey(embeddedKey);
                });
                EditorPrefs.SetBool(embeddedKey, false); setter(expected);
                string actual = Path.GetFullPath(getter()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!string.Equals(expected, actual, comparison) || EditorPrefs.GetBool(embeddedKey, true))
                    throw new InvalidOperationException("Unity did not select the explicit code-build tool: " + variable);
            }
            public void Dispose() { for (int i = restore.Count - 1; i >= 0; --i) restore[i](); restore.Clear(); }
        }
    }
}
