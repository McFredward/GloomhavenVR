using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        [Serializable] sealed class ScriptType { public string assembly, @namespace, @class, propertiesHash; public int executionOrder; }
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
            var scripts = new Dictionary<string, MonoScript>(StringComparer.Ordinal);
            foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            {
                // The original native roster also contains partial/static
                // source-file scripts for which GetClass returns null. Their
                // exact MonoScript metadata, not reflection alone, is the key.
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
                    if (!path.EndsWith(".cs", StringComparison.Ordinal)) continue;
                    assemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(path);
                    namespaceName = ""; className = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrEmpty(assemblyName)) continue;
                }
                if (!assemblyName.EndsWith(".dll", StringComparison.Ordinal)) assemblyName += ".dll";
                string key = assemblyName + "|" + namespaceName + "|" + className;
                if (!scripts.ContainsKey(key)) scripts.Add(key, script);
            }
            var roots = new List<UnityEngine.Object>();
            foreach (var row in input.scriptTypes)
            {
                string key = row.assembly + "|" + row.@namespace + "|" + row.@class;
                if (!scripts.TryGetValue(key, out MonoScript script))
                {
                    // Engine default scripts are already emitted by the same
                    // exact editor's default resource bank, even when the public
                    // MonoImporter catalog excludes those native assets. The
                    // closed output roster still must prove every one matches.
                    if (row.assembly.StartsWith("UnityEngine.", StringComparison.Ordinal) && row.assembly.EndsWith("Module.dll", StringComparison.Ordinal)) continue;
                    throw new InvalidDataException("Retained original serialized script is missing: " + key + "; a full build is required.");
                }
                roots.Add(script);
            }
            // MonoScript assets carry only class identity/property metadata.
            // They make the original roster available to the linker and the
            // output gate without loading original scenes, prefabs or textures.
            PlayerSettings.SetPreloadedAssets(roots.ToArray());
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory("Assets/QuestCodeUpdate");
            const string scenePath = "Assets/QuestCodeUpdate/CodeOnly.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            string output = Required("GHVR_QUEST_CODE_OUTPUT");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
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
