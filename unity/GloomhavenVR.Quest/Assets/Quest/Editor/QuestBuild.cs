using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEditor.Build.Player;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace GloomhavenVR.Quest.Editor
{
    public static class QuestBuild
    {
        [Serializable] sealed class ModInput { public int modBuild; }
        [Serializable] sealed class InputManifest { public string inputKey; public ModInput mod; }
        [Serializable] sealed class BuildStamp { public int schema = 1; public int modBuild; public string inputKey; }
        [Serializable] sealed class Receipt
        {
            public int schema = 1;
            public string target, inputKey, package, profileSha256, unityVersion, buildResult;
            public string il2CppCompilerConfiguration, additionalIl2CppArgs;
            public bool incrementalGc;
            public string[] scenes;
        }
        static string Required(string key)
        {
            string value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing build input " + key);
            return value;
        }
        public static void Build()
        {
            using (new AndroidToolsOverride()) BuildPlayer();
        }
        static void BuildPlayer()
        {
            string target = Required("GHVR_QUEST_TARGET");
            if (target != "probe" && target != "startup")
                throw new InvalidOperationException("Game target is gated until recovered assets, platform adapter and complete AOT conversion pass.");
            string apk = Required("GHVR_QUEST_OUTPUT_APK");
            string package = Required("GHVR_QUEST_PACKAGE");
            var manifest = JsonUtility.FromJson<InputManifest>(File.ReadAllText(Required("GHVR_QUEST_MANIFEST_PATH")));
            if (manifest == null || manifest.mod == null || manifest.mod.modBuild <= 0
                || !System.Text.RegularExpressions.Regex.IsMatch(manifest.inputKey ?? "", "^[0-9a-f]{64}$"))
                throw new InvalidDataException("Android build identity is missing.");
            ConfigureAndroid(package, target == "startup");
            PlayerSettings.bundleVersion = "0.1.0.B" + manifest.mod.modBuild + "." + manifest.inputKey.Substring(0, 12);
            PlayerSettings.Android.bundleVersionCode = manifest.mod.modBuild;
            ConfigureNativePlugin();
            ConfigureXr();
            PrepareDiagnosticMaterials();
            PrepareDiagnosticResources();
            if (target == "probe") ValidateOwnedModel();
            File.WriteAllText("Assets/Quest/Resources/quest-build.json", JsonUtility.ToJson(new BuildStamp
            {
                modBuild = manifest.mod.modBuild, inputKey = manifest.inputKey
            }));
            string[] scenes;
            if (target == "startup")
            {
#if GHVR_QUEST_STARTUP
                scenes = PrepareOriginalStartup();
#else
                throw new InvalidOperationException("Original startup compile contract is missing; regenerate the startup project.");
#endif
            }
            else
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Quest hardware diagnostic").AddComponent<QuestHardwareProbe>();
                Directory.CreateDirectory("Assets/Quest/Scenes");
                const string scenePath = "Assets/Quest/Scenes/QuestHardwareProbe.unity";
                EditorSceneManager.SaveScene(scene, scenePath);
                scenes = new[] { scenePath };
            }
            EditorBuildSettings.scenes = scenes.Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Directory.CreateDirectory(Path.GetDirectoryName(apk));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = apk,
                target = BuildTarget.Android, options = BuildOptions.Development
            });
            byte[] profile = File.ReadAllBytes(Required("GHVR_QUEST_PROFILE_PATH"));
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(profile)).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(apk + ".build.json", JsonUtility.ToJson(new Receipt
            {
                target = target, inputKey = manifest.inputKey, package = package,
                profileSha256 = hash, unityVersion = Application.unityVersion,
                buildResult = report.summary.result.ToString(), scenes = scenes,
                il2CppCompilerConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(BuildTargetGroup.Android).ToString(),
                additionalIl2CppArgs = PlayerSettings.GetAdditionalIl2CppArgs(),
                incrementalGc = PlayerSettings.gcIncremental
            }, true));
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Android build failed: " + report.summary.result + ", errors=" + report.summary.totalErrors);
            Debug.Log("[GloomhavenVR Quest] signed ARM64 IL2CPP " + target + " diagnostic built: " + apk);
        }
        static void ConfigureAndroid(string package, bool originalStartup, bool configureSigning = true)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Android build target switch failed");
            PlayerSettings.companyName = "GloomhavenVR";
            PlayerSettings.productName = "GloomhavenVR Quest Test";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, package);
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            // The recovered B619 project disabled incremental GC. The maintainer
            // reports repeating menu hitches; that is not proof of their cause,
            // but full-heap pauses are avoidable in this Android player. Restore
            // Unity's incremental mode, without changing desktop mod settings or
            // forcing collections. Runtime evidence reports the actual mode.
            PlayerSettings.gcIncremental = true;
            if (originalStartup)
            {
                PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_4_6);
                // Large recovered assemblies exhaust the native optimizer's memory.
                // This menu diagnostic establishes execution, not release performance.
                PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Debug);
                // IL2CPP preserves a long left-associated tuning sum as nested calls.
                // Increase the parser limit without changing the managed expression.
                // The NDK's old BFD reports ARM64 CALL26 relocation overflows here.
                // LLD links the same objects without changing managed expressions.
                PlayerSettings.SetAdditionalIl2CppArgs("--compiler-flags=-fbracket-depth=1024 --linker-flags=-fuse-ld=lld");
            }
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel30;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
            PlayerSettings.runInBackground = true;
            if (configureSigning)
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Required("GHVR_QUEST_KEYSTORE_PATH");
                PlayerSettings.Android.keyaliasName = Required("GHVR_QUEST_KEYSTORE_ALIAS");
                PlayerSettings.Android.keystorePass = Required("GHVR_QUEST_KEYSTORE_PASSWORD");
                PlayerSettings.Android.keyaliasPass = Required("GHVR_QUEST_KEYALIAS_PASSWORD");
            }
            // Passwords are never logged or saved in the source template.
            var settings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var input = settings.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = originalStartup ? 2 : 1; settings.ApplyModifiedPropertiesWithoutUndo(); }
        }
#if GHVR_QUEST_STARTUP
        [Serializable] sealed class StartupEvidence
        {
            public int schema;
            public string target;
            public bool fullGameReady;
            public string[] selectedScenes;
        }
        [Serializable] sealed class StandaloneEvidence { public bool startupAdapterComplete, fullGameReady; }
        [Serializable] sealed class ApiFile { public string path, sha256; public long size; }
        [Serializable] sealed class ApiContract { public int schema; public bool complete; public string reportSha256, sdkRoot; public ApiFile[] plugins, sdk; }
        [Serializable] sealed class PlayerSdkEvidence
        {
            public int schema = 1;
            public string target = "Android", backend = "IL2CPP", compilation = "Player",
                options = "DevelopmentBuild|Assertions", unityVersion;
        }
        public static void CompileStartupSdk()
        {
            using (new AndroidToolsOverride())
            {
                ConfigureAndroid(Required("GHVR_QUEST_PACKAGE"), true, false);
                const string output = "QuestStartupEvidence/PlayerSdk";
                if (Directory.Exists(output)) Directory.Delete(output, true);
                Directory.CreateDirectory(output);
                var result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
                {
                    group = BuildTargetGroup.Android, target = BuildTarget.Android,
                    options = ScriptCompilationOptions.DevelopmentBuild | ScriptCompilationOptions.Assertions
                }, output);
                if (result.assemblies == null || result.assemblies.Count == 0)
                    throw new InvalidOperationException("Actual Android player script compilation produced no assemblies.");
                File.WriteAllText(output + "/compilation.json", JsonUtility.ToJson(new PlayerSdkEvidence { unityVersion = Application.unityVersion }, true));
                Debug.Log("[Quest startup] actual Android IL2CPP Development player SDK compiled; assemblies=" + result.assemblies.Count);
            }
        }
        static string FileHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static void ValidatePackageApiContract()
        {
            const string root = "Assets/Quest/Resources/quest-package-api-";
            var contract = JsonUtility.FromJson<ApiContract>(File.ReadAllText(root + "contract.json"));
            if (contract == null || contract.schema != 1 || !contract.complete ||
                contract.reportSha256 != FileHash(root + "report.json") || contract.plugins == null ||
                contract.plugins.Length == 0 || contract.sdk == null || contract.sdk.Length != 8 ||
                contract.sdkRoot != "QuestStartupEvidence/PlayerSdk")
                throw new InvalidOperationException("The imported Unity package API compatibility gate is missing or stale.");
            string[] packages = { "UnityEngine.UI", "Unity.InputSystem", "Unity.Addressables", "Unity.ResourceManager",
                "Unity.ScriptableBuildPipeline", "Unity.XR.Management", "Unity.XR.OpenXR", "Unity.XR.CoreUtils" };
            foreach (var file in contract.plugins.Concat(contract.sdk))
            {
                bool sdk = contract.sdk.Contains(file);
                string path = sdk ? contract.sdkRoot + "/" + file.path : file.path;
                if (file.path.Contains("..") || Path.IsPathRooted(file.path) ||
                    (!sdk && !file.path.StartsWith("Assets/", StringComparison.Ordinal)) ||
                    (sdk && !packages.Contains(Path.GetFileNameWithoutExtension(file.path))) ||
                    !File.Exists(path) || new FileInfo(path).Length != file.size || FileHash(path) != file.sha256)
                    throw new InvalidOperationException("Imported package API inputs changed: " + file.path);
            }
            string[] actual = Directory.GetFiles("Assets", "*.dll", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .Where(path => !packages.Contains(Path.GetFileNameWithoutExtension(path))).OrderBy(path => path).ToArray();
            if (!actual.SequenceEqual(contract.plugins.Select(file => file.path).OrderBy(path => path)))
                throw new InvalidOperationException("Imported package API contract does not cover all active plugins.");
        }
        static string[] PrepareOriginalStartup()
        {
            ValidatePackageApiContract();
            const string evidencePath = "Assets/Quest/Resources/quest-startup-report.json";
            const string adapterPath = "Assets/Quest/Resources/quest-standalone-report.json";
            var evidence = JsonUtility.FromJson<StartupEvidence>(File.ReadAllText(evidencePath));
            var adapter = JsonUtility.FromJson<StandaloneEvidence>(File.ReadAllText(adapterPath));
            if (evidence == null || evidence.schema != 1 || evidence.target != "startup" || evidence.fullGameReady ||
                adapter == null || !adapter.startupAdapterComplete || adapter.fullGameReady)
                throw new InvalidOperationException("Original startup evidence is absent or claims an unsupported full game.");
            string[] names = { "Bootstrap", "Intro", "Gloomhaven_unified", "MainMenu" };
            if (evidence.selectedScenes == null || !evidence.selectedScenes.Select(Path.GetFileNameWithoutExtension).SequenceEqual(names))
                throw new InvalidOperationException("Original Bootstrap/Intro/menu scene names or order were lost.");
            foreach (string path in evidence.selectedScenes)
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    throw new InvalidOperationException("Required original scene is unavailable: " + path);
            // The recovery helper remaps exact original package-script identities
            // before any original scene runs. Unsupported references remain a build error.
            QuestOriginalScriptBindings.RemapAndValidate();
            QuestOriginalScriptOrders.RestoreAndVerify();
            QuestAudioValidation.Validate();
            QuestSpriteGeometryValidation.ValidateStartupAssets();
            QuestUiAssetValidation.Validate(false);
            QuestPostEffectValidation.Validate(false);
            QuestVideoValidation.Validate(false);
            QuestWorldScreenValidation.Validate(false);
            QuestStartupAddressablesBuild.Build();
            QuestPostEffectValidation.Validate(true);
            QuestUiAssetValidation.Validate(true);
            QuestVideoValidation.Validate(true);
            QuestWorldScreenValidation.Validate(true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject bootstrap = new GameObject("Original Gloomhaven startup diagnostic");
            bootstrap.AddComponent<QuestGameBootstrap>();
            // Preserve the logo import and neutral startup camera. The reusable
            // artwork is created only when actual content installation needs it;
            // cached starts continue directly into the original native flow.
            PrepareLoadingLogo();
            bootstrap.AddComponent<QuestGameModLifecycle>().PrepareStartupView();
            Directory.CreateDirectory("Assets/Quest/Scenes");
            const string startupScene = "Assets/Quest/Scenes/QuestOriginalStartup.unity";
            EditorSceneManager.SaveScene(scene, startupScene);
            return new[] { startupScene }.Concat(evidence.selectedScenes).ToArray();
        }

        static void PrepareLoadingLogo()
        {
            const string path = "Assets/Quest/Resources/quest-loading-logo.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("The original loading logo importer is unavailable.");
            int sourceWidth, sourceHeight;
            importer.GetSourceTextureWidthAndHeight(out sourceWidth, out sourceHeight);
            // Unity's default NPOT scaling squashes this original 1024x179 wordmark to 1024x128.
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            Texture2D logo = Resources.Load<Texture2D>(QuestLoadingView.LogoResource);
            if (logo == null || sourceWidth <= 0 || sourceHeight <= 0 ||
                logo.width != sourceWidth || logo.height != sourceHeight)
                throw new InvalidOperationException("Loading logo import did not preserve its original PNG dimensions.");
        }
#endif
        sealed class AndroidToolsOverride : IDisposable
        {
            readonly System.Collections.Generic.List<Action> restore = new System.Collections.Generic.List<Action>();
            public AndroidToolsOverride()
            {
                try
                {
                    Apply("SDK", "GHVR_QUEST_ANDROID_SDK", "SdkUseEmbedded", "AndroidSdkRoot",
                        value => AndroidExternalToolsSettings.sdkRootPath = value,
                        () => AndroidExternalToolsSettings.sdkRootPath);
                    Apply("NDK", "GHVR_QUEST_ANDROID_NDK", "NdkUseEmbedded", "AndroidNdkRootR21D",
                        value => AndroidExternalToolsSettings.ndkRootPath = value,
                        () => AndroidExternalToolsSettings.ndkRootPath);
                    Apply("JDK", "GHVR_QUEST_JDK", "JdkUseEmbedded", "JdkPath",
                        value => AndroidExternalToolsSettings.jdkRootPath = value,
                        () => AndroidExternalToolsSettings.jdkRootPath);
                }
                catch { Dispose(); throw; }
            }
            void Apply(string label, string variable, string embeddedKey, string rootKey,
                Action<string> setRoot, Func<string> getRoot)
            {
                string value = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(value)) return;
                string expected = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(expected)) throw new InvalidOperationException("Android " + label + " override directory does not exist");
                bool hadEmbedded = EditorPrefs.HasKey(embeddedKey), embedded = EditorPrefs.GetBool(embeddedKey, true);
                bool hadRoot = EditorPrefs.HasKey(rootKey);
                string previous = EditorPrefs.GetString(rootKey);
                restore.Add(() =>
                {
                    if (hadRoot) EditorPrefs.SetString(rootKey, previous); else EditorPrefs.DeleteKey(rootKey);
                    if (hadEmbedded) EditorPrefs.SetBool(embeddedKey, embedded); else EditorPrefs.DeleteKey(embeddedKey);
                });
                EditorPrefs.SetBool(embeddedKey, false);
                setRoot(expected);
                string actual = Path.GetFullPath(getRoot()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!string.Equals(expected, actual, comparison) || EditorPrefs.GetBool(embeddedKey, true))
                    throw new InvalidOperationException("Unity did not select the explicit Android " + label + " override");
                Debug.Log("[GloomhavenVR Quest] selected Android " + label + " override: " + actual);
            }
            public void Dispose()
            {
                for (int index = restore.Count - 1; index >= 0; index--) restore[index]();
                restore.Clear();
            }
        }
        static void ConfigureXr()
        {
            XRGeneralSettingsPerBuildTarget perTarget;
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perTarget))
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                Directory.CreateDirectory("Assets/Quest/Settings");
                AssetDatabase.CreateAsset(perTarget, "Assets/Quest/Settings/XRGeneralSettings.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = true;
            var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            manager.automaticLoading = true;
            manager.automaticRunning = true;
            if (!XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android))
                throw new InvalidOperationException("OpenXR loader assignment failed");
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            EnableFeature(QuestPassthroughFeature.FeatureId);
            EnableFeature("com.unity.openxr.feature.metaquest");
            EnableFeature("com.unity.openxr.feature.input.oculustouch");
            EnableFeature("com.unity.openxr.feature.input.metaquestplus");
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(xr);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perTarget);
        }
        static void ConfigureNativePlugin()
        {
            const string path = "Assets/Quest/Plugins/Android/arm64/libghvr_quest_passthrough.so";
            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null) throw new InvalidOperationException("ARM64 passthrough library is missing");
            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(false);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
            importer.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
            importer.SaveAndReimport();
        }
        static void EnableFeature(string id)
        {
            OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id);
            if (feature == null) throw new InvalidOperationException("Required OpenXR feature missing: " + id);
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }
        static void PrepareDiagnosticResources()
        {
            // Shader.Find in a runtime-created scene does not establish a build dependency.
            // Serialized Resources materials retain exactly the diagnostic shaders we use.
            foreach (var item in new[]
            {
                new[] { "quest-ray-material", "Unlit/Color" },
                new[] { "quest-surface-material", "Standard" },
                new[] { "quest-albedo-material", "GloomhavenVR/Quest/DiagnosticAlbedo" }
            })
            {
                string path = "Assets/Quest/Resources/" + item[0] + ".mat";
                var shader = Shader.Find(item[1]);
                if (shader == null) throw new InvalidOperationException("Required diagnostic shader unavailable: " + item[1]);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = item[0] };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
            Debug.Log("[GloomhavenVR Quest] diagnostic shader Resources retained: Unlit/Color, Standard, DiagnosticAlbedo");
        }
        static void PrepareDiagnosticMaterials()
        {
            QuestProbeMaterialConversion.Prepare();
        }
        static void ValidateOwnedModel()
        {
            var prefab = Resources.Load<GameObject>("quest-original-model");
            if (prefab == null) return; // Optional only for the explicitly diagnostic target.
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                    throw new InvalidOperationException("Diagnostic model has unresolved gameplay scripts");
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0 || renderers.Any(renderer => renderer.sharedMesh == null || renderer.bones.Length == 0))
                throw new InvalidOperationException("Original model mesh/skinning closure is invalid");
            var animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null || animator.runtimeAnimatorController.animationClips.Length == 0)
                throw new InvalidOperationException("Original model animator/clips are missing");
            QuestProbeMaterialConversion.ValidateModel(prefab);
            Debug.Log("[GloomhavenVR Quest] original native model import verified skinnedRenderers=" + renderers.Length +
                " clips=" + animator.runtimeAnimatorController.animationClips.Length + " shaderParity=false");
        }
    }
}
