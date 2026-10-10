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
        [Serializable] sealed class InputManifest { public string inputKey, proceduralBackend; public ModInput mod; }
        [Serializable] sealed class BuildStamp { public int schema = 1; public int modBuild; public string inputKey; }
        public const string CampaignShaderModeReceiptPath = "QuestCampaignShaderEvidence/build-mode.json";
        [Serializable] public sealed class CampaignShaderModeReceipt
        {
            public int schema = 1, shaderCount, nativeAliasCount, materialCount, nativeCompilerQueriesThisInvocation;
            public string mode, unityVersion, sourceManifestSha256, exhaustiveReceiptSha256;
            public bool requiredRetentionVerified, importedIdentitiesVerified, exhaustiveValidationRequested;
            public bool exhaustiveCompilerValidationCompleted, exhaustiveResultReused;
            public bool originalPixelParityVerified, headsetPictureVerified;
        }
        static CampaignShaderModeReceipt campaignShaderMode;
        [Serializable] sealed class Receipt
        {
            public int schema = 1;
            public string target, inputKey, package, profileSha256, unityVersion, buildResult;
            public string il2CppCompilerConfiguration, additionalIl2CppArgs, stereoRenderingPath, openXrRenderMode, graphicsApi;
            public bool incrementalGc;
            public string campaignShaderValidationMode;
            public bool campaignShaderExhaustiveCompleted, campaignShaderExhaustiveReused;
            public int campaignShaderNativeCompilerQueries;
            public string[] scenes;
            public string proceduralBackend;
            public QuestNativePluginContract.NativeFile[] stagedProceduralNativeFiles;
        }
        static string Required(string key)
        {
            string value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing build input " + key);
            return value;
        }
#if GHVR_QUEST_GAME
        [Serializable] public sealed class CampaignMemorySample
        {
            public string stage;
            public long managedBytes, monoUsedBytes, unityAllocatedBytes, unityReservedBytes, workingSetBytes;
            public bool processMemoryObserved;
        }
        [Serializable] public sealed class CampaignMemoryReceipt
        {
            public int schema = 1;
            public string inputKey, unityVersion;
            public CampaignMemorySample[] samples;
            public bool unreachableManagedMemoryCollected, unusedImportedAssetsUnloadRequested;
            public bool playerBuildCompleted, headsetPictureVerified;
        }
        public const string CampaignMemoryReceiptPath = "QuestCampaignEvidence/player-build-memory.json";

        public static void ReleaseCampaignBuildMemory(string inputKey)
        {
            // f392 completed the entire native bank, then the host OOM killer
            // terminated Unity during Player scene assembly (13GB anonymous
            // RSS). SBP's completed contexts and typed-validation temporaries
            // are no longer needed at this boundary. Release only unreachable
            // managed objects and unused imported assets; keep the saved startup
            // scene, live references, settings and all actual content intact.
            // This does not guarantee an RSS reduction or cure Player peak RAM.
            QuestWizardProgress.Publish("unity-player-memory", "Release unreachable imported objects before Player compilation; the saved original scene stays intact.", operation: "player");
            var before = SampleCampaignMemory("before-player-memory-release");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
            var after = SampleCampaignMemory("after-player-memory-release");
            Directory.CreateDirectory(Path.GetDirectoryName(CampaignMemoryReceiptPath));
            File.WriteAllText(CampaignMemoryReceiptPath, JsonUtility.ToJson(new CampaignMemoryReceipt {
                inputKey = inputKey, unityVersion = Application.unityVersion,
                samples = new[] { before, after }, unreachableManagedMemoryCollected = true,
                unusedImportedAssetsUnloadRequested = true
            }, true) + "\n");
            Debug.Log("[Quest Campaign build] memory handoff managed=" + before.managedBytes + "->" + after.managedBytes +
                " nativeAllocated=" + before.unityAllocatedBytes + "->" + after.unityAllocatedBytes +
                " workingSet=" + before.workingSetBytes + "->" + after.workingSetBytes +
                "; actual Player peak remains measured by the host.");
        }
        static CampaignMemorySample SampleCampaignMemory(string stage)
        {
            var sample = new CampaignMemorySample {
                stage = stage, managedBytes = GC.GetTotalMemory(false),
                monoUsedBytes = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(),
                unityAllocatedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                unityReservedBytes = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong()
            };
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                { sample.workingSetBytes = process.WorkingSet64; sample.processMemoryObserved = true; }
            }
            catch (Exception) { sample.workingSetBytes = -1; }
            return sample;
        }
#endif
        public static void Build()
        {
            QuestWizardProgress.Publish("unity-work-stage:method", "Unity build method entered.", 0, 1, "steps", "unity-validation", "start");
            using (new AndroidToolsOverride()) BuildPlayer();
            QuestWizardProgress.Publish("unity-work-stage:method", "Unity build method returned successfully.", 1, 1, "steps", "player", "complete");
        }
        static void BuildPlayer()
        {
            QuestWizardProgress.Operation("unity-import", true, "Unity project imported and Editor build entry point reached.");
            QuestWizardProgress.Operation("unity-validation", false, "Checking imported original scenes, package APIs and Android build settings.");
            campaignShaderMode = null;
            string target = Required("GHVR_QUEST_TARGET");
            if (target != "probe" && target != "startup" && target != "game")
                throw new InvalidOperationException("Unknown Quest build target.");
            string apk = Required("GHVR_QUEST_OUTPUT_APK");
            string package = Required("GHVR_QUEST_PACKAGE");
            var manifest = JsonUtility.FromJson<InputManifest>(File.ReadAllText(Required("GHVR_QUEST_MANIFEST_PATH")));
            if (manifest == null || manifest.mod == null || manifest.mod.modBuild <= 0
                || !System.Text.RegularExpressions.Regex.IsMatch(manifest.inputKey ?? "", "^[0-9a-f]{64}$"))
                throw new InvalidDataException("Android build identity is missing.");
            var configuration = new QuestWizardProgress.TaskSequence("unity-configuration", "unity-validation", 6);
            configuration.Run("Configure original mobile Android settings", () => {
                ConfigureAndroid(package, target != "probe");
                PlayerSettings.bundleVersion = "0.1.0.B" + manifest.mod.modBuild + "." + manifest.inputKey.Substring(0, 12);
                PlayerSettings.Android.bundleVersionCode = manifest.mod.modBuild;
            });
            QuestNativePluginContract.Contract nativeContract = null;
            configuration.Run("Configure the selected procedural runtime", () => nativeContract = ConfigureNativePlugin(manifest.proceduralBackend));
            configuration.Run("Configure Quest XR", () => ConfigureXr(target != "probe"));
            configuration.Run("Prepare required diagnostic materials", PrepareDiagnosticMaterials);
            configuration.Run("Prepare required diagnostic resources", PrepareDiagnosticResources);
            configuration.Run("Record the current build identity", () => {
                if (target == "probe") ValidateOwnedModel();
                File.WriteAllText("Assets/Quest/Resources/quest-build.json", JsonUtility.ToJson(new BuildStamp
                {
                    modBuild = manifest.mod.modBuild, inputKey = manifest.inputKey
                }));
            });
            string[] scenes;
            if (target != "probe")
            {
#if GHVR_QUEST_STARTUP
                scenes = PrepareOriginalStartup(target == "game");
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
            QuestWizardProgress.Operation("player", false, "Preparing original content delivery and the Android Player build.");
            BuildReport report;
#if GHVR_QUEST_GAME
            using (target == "game" ? new QuestCampaignContentBuild(apk, manifest.inputKey) : null)
#endif
            {
#if GHVR_QUEST_GAME
                if (target == "game") ReleaseCampaignBuildMemory(manifest.inputKey);
#endif
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes, locationPathName = apk,
                    target = BuildTarget.Android, options = BuildOptions.Development
                });
            }
#if GHVR_QUEST_GAME
            if (target == "game" && report.summary.result == BuildResult.Succeeded)
                QuestCampaignComputeValidation.Validate();
#endif
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
                incrementalGc = PlayerSettings.gcIncremental,
                stereoRenderingPath = PlayerSettings.stereoRenderingPath.ToString(),
                openXrRenderMode = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android).renderMode.ToString(),
                graphicsApi = string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Select(api => api.ToString())),
                campaignShaderValidationMode = campaignShaderMode == null ? "not-applicable" : campaignShaderMode.mode,
                campaignShaderExhaustiveCompleted = campaignShaderMode != null && campaignShaderMode.exhaustiveCompilerValidationCompleted,
                campaignShaderExhaustiveReused = campaignShaderMode != null && campaignShaderMode.exhaustiveResultReused,
                campaignShaderNativeCompilerQueries = campaignShaderMode == null ? 0 : campaignShaderMode.nativeCompilerQueriesThisInvocation,
                proceduralBackend = nativeContract == null ? null : nativeContract.backend,
                stagedProceduralNativeFiles = nativeContract == null ? null : nativeContract.files
            }, true));
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Android build failed: " + report.summary.result + ", errors=" + report.summary.totalErrors);
            QuestWizardProgress.Operation("player", true, "Unity Android Player and required final evidence completed; host delivery verification follows.");
            Debug.Log("[GloomhavenVR Quest] signed ARM64 IL2CPP " + target
                + (target == "game" ? " player built: " : " diagnostic built: ") + apk);
        }
        static void ConfigureFixedEyeMsaa()
        {
            // Before XR starts, keep every native quality-level switch at the
            // same sample count. The shared mod never resizes live Vulkan eyes.
#if GHVR_QUEST_GAME
            int startupSamples = GloomhavenVR.Core.QuestStandalonePlatform.StandaloneMsaaDefault;
#else
            throw new InvalidOperationException("Full Quest Campaign requires the current mod's standalone defaults.");
#endif
#if GHVR_QUEST_GAME
            var quality = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("QualitySettings"));
            var levels = quality.FindProperty("m_QualitySettings");
            if (levels == null || !levels.isArray || levels.arraySize == 0)
                throw new InvalidOperationException("Quest fixed MSAA requires original quality levels.");
            for (int i = 0; i < levels.arraySize; i++)
            {
                var samples = levels.GetArrayElementAtIndex(i).FindPropertyRelative("antiAliasing");
                if (samples == null)
                    throw new InvalidOperationException("Quest quality level has no MSAA field.");
                samples.intValue = startupSamples;
            }
            quality.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestBuild] Vulkan eye allocation fixed: all native quality levels use standalone MSAA="
                + startupSamples + " before XR startup.");
#endif
        }

        static void ConfigureAndroid(string package, bool originalStartup, bool configureSigning = true)
        {
            bool fullCampaign = Environment.GetEnvironmentVariable("GHVR_QUEST_TARGET") == "game";
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
                // Keep Development diagnostics while optimising native Campaign
                // execution. Unoptimised recovered rule parsers made startup
                // unnecessarily expensive on the mobile CPU. Build hosts must
                // bound native compiler concurrency to fit their available RAM.
                PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, fullCampaign
                    ? Il2CppCompilerConfiguration.Release : Il2CppCompilerConfiguration.Debug);
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
            if (fullCampaign) ConfigureFixedEyeMsaa();
            // Original Windows DXBC embeds reversed-depth and top-origin UV math.
            // Unity's Vulkan branch has those same conventions; GLES does not.
            // Keep the existing diagnostic backends and desktop settings intact.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] {
                fullCampaign ? GraphicsDeviceType.Vulkan : GraphicsDeviceType.OpenGLES3 });
#if GHVR_QUEST_GAME
            if (fullCampaign) QuestStartupAddressablesBuild.ConfigureCampaignShaderRetention();
#endif
            // The current mod's authored shaders and per-eye callbacks require
            // MultiPass. Query its public contract so future mod changes remain
            // authoritative; the independent hardware probe can still use SPI.
            PlayerSettings.stereoRenderingPath = OriginalModUsesMultiPass(originalStartup)
                ? StereoRenderingPath.MultiPass : StereoRenderingPath.SinglePass;
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
            QuestWizardProgress.Publish("unity-work-stage:method", "Android SDK compilation method entered.", 0, 1, "steps", "unity-import", "start");
            using (new AndroidToolsOverride())
            {
                var tasks = new QuestWizardProgress.TaskSequence("unity-sdk-tasks", "unity-import", 3);
                tasks.Run("Configure the actual Android Player SDK compilation", () => ConfigureAndroid(Required("GHVR_QUEST_PACKAGE"), true, false));
                const string output = "QuestStartupEvidence/PlayerSdk";
                if (Directory.Exists(output)) Directory.Delete(output, true);
                Directory.CreateDirectory(output);
                ScriptCompilationResult result = default(ScriptCompilationResult);
                tasks.Run("Compile the actual Android Player SDK assemblies", () => result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
                {
                    group = BuildTargetGroup.Android, target = BuildTarget.Android,
                    options = ScriptCompilationOptions.DevelopmentBuild | ScriptCompilationOptions.Assertions
                }, output));
                if (result.assemblies == null || result.assemblies.Count == 0)
                    throw new InvalidOperationException("Actual Android player script compilation produced no assemblies.");
                tasks.Run("Publish the compiled Android Player SDK evidence", () => File.WriteAllText(output + "/compilation.json", JsonUtility.ToJson(new PlayerSdkEvidence { unityVersion = Application.unityVersion }, true)));
                Debug.Log("[Quest startup] actual Android IL2CPP Development player SDK compiled; assemblies=" + result.assemblies.Count);
            }
            QuestWizardProgress.Publish("unity-work-stage:method", "Android SDK compilation method returned successfully.", 1, 1, "steps", "unity-import", "complete");
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
                // Match the builder audit: StreamingAssets DLLs are copied runtime
                // payloads, while nested Plugins/StreamingAssets remains a plugin path.
                .Where(path => !path.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase))
                .Where(path => !packages.Contains(Path.GetFileNameWithoutExtension(path))).OrderBy(path => path).ToArray();
            if (!actual.SequenceEqual(contract.plugins.Select(file => file.path).OrderBy(path => path)))
                throw new InvalidOperationException("Imported package API contract does not cover all active plugins.");
        }
        static string[] PrepareOriginalStartup(bool campaign = false)
        {
            // Capture200517 had several minutes of actual validation with no
            // visible work before its Sprite identity failure. Count each real
            // task and publish its label BEFORE entering a synchronous native
            // call; exceptions leave that task open and name it in the log.
            var tasks = new QuestWizardProgress.TaskSequence("unity-validation-tasks", "unity-validation", campaign ? 17 : 11);
            tasks.Run("Validate imported package API inputs", ValidatePackageApiContract);
            const string evidencePath = "Assets/Quest/Resources/quest-startup-report.json";
            const string adapterPath = "Assets/Quest/Resources/quest-standalone-report.json";
            StartupEvidence evidence = null;
            tasks.Run("Read original scene and standalone startup evidence", () => {
                evidence = JsonUtility.FromJson<StartupEvidence>(File.ReadAllText(evidencePath));
                var adapter = JsonUtility.FromJson<StandaloneEvidence>(File.ReadAllText(adapterPath));
                if (evidence == null || evidence.schema != 1 || evidence.target != (campaign ? "campaign" : "startup") || evidence.fullGameReady ||
                    adapter == null || !adapter.startupAdapterComplete || adapter.fullGameReady)
                    throw new InvalidOperationException("Original startup evidence is absent or claims an unsupported full game.");
                string[] names = { "Bootstrap", "Intro", "Gloomhaven_unified", "MainMenu" };
                if (evidence.selectedScenes == null || (!campaign && !evidence.selectedScenes.Select(Path.GetFileNameWithoutExtension).SequenceEqual(names))
                    || campaign && (evidence.selectedScenes.Length != 13 || names.Any(name => !evidence.selectedScenes.Any(path => Path.GetFileNameWithoutExtension(path) == name))))
                    throw new InvalidOperationException("Original Bootstrap/Intro/menu scene names or order were lost.");
            });
            if (campaign) tasks.Run("Validate the complete Campaign build inputs", ValidateCampaignBuildContract);
            tasks.Run("Load the required original scene assets", () => {
                var scenes = new QuestWizardProgress.Counter("unity-original-scenes", "unity-validation", evidence.selectedScenes.Length, "scenes", "Loading original scene assets");
                int done = 0;
                foreach (string path in evidence.selectedScenes)
                {
                    scenes.Report(done, path);
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                        throw new InvalidOperationException("Required original scene is unavailable: " + path);
                    scenes.Report(++done, path);
                }
                scenes.Complete("Required original scene assets loaded");
            });
            // Preserve original package-script identities before original scenes run.
            tasks.Run("Remap and validate original script bindings", QuestOriginalScriptBindings.RemapAndValidate);
            tasks.Run("Restore original script execution orders", QuestOriginalScriptOrders.RestoreAndVerify);
            tasks.Run("Validate original audio", QuestAudioValidation.Validate);
            tasks.Run("Validate startup Sprite geometry", QuestSpriteGeometryValidation.ValidateStartupAssets);
            tasks.Run("Validate original UI assets", () => QuestUiAssetValidation.Validate(false));
            tasks.Run("Validate original post effects", () => QuestPostEffectValidation.Validate(false));
            tasks.Run("Validate original video assets", () => QuestVideoValidation.Validate(false));
            tasks.Run("Validate original world screens", () => QuestWorldScreenValidation.Validate(false));
            if (campaign)
            {
                tasks.Run("Validate original Campaign assets", QuestCampaignAssetValidation.Validate);
                tasks.Run("Validate original Campaign textures", () => QuestCampaignTextureValidation.Validate());
                tasks.Run("Validate original Campaign Sprite geometry", () => QuestCampaignSpriteValidation.Validate());
                tasks.Run("Validate original Campaign compute sources", QuestCampaignComputeValidation.ValidateSources);
                tasks.Run("Retain original Campaign Shader identities", () => campaignShaderMode = PrepareCampaignShaders());
            }
            QuestWizardProgress.Operation("unity-validation", true, "Required original scene and graphics contracts validated.");
            QuestWizardProgress.Operation("content-bank", false, "Building original Android Addressables and complete game content bank.");
            QuestStartupAddressablesBuild.Build();
            var post = new QuestWizardProgress.TaskSequence("unity-content-post-tasks", "content-bank", campaign ? 6 : 5);
            if (campaign) post.Run("Validate built Android Campaign assets", QuestCampaignAssetValidation.ValidateAfterAndroidBuild);
            post.Run("Validate built original post effects", () => QuestPostEffectValidation.Validate(true));
            post.Run("Validate built original UI assets", () => QuestUiAssetValidation.Validate(true));
            post.Run("Validate built original videos", () => QuestVideoValidation.Validate(true));
            post.Run("Validate built original world screens", () => QuestWorldScreenValidation.Validate(true));
            const string startupScene = "Assets/Quest/Scenes/QuestOriginalStartup.unity";
            post.Run("Prepare and save the original VR startup scene", () => {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject bootstrap = new GameObject(campaign ? "Gloomhaven Quest Campaign" : "Original Gloomhaven startup diagnostic");
                bootstrap.AddComponent<QuestGameBootstrap>();
                PrepareLoadingLogo();
                bootstrap.AddComponent<QuestGameModLifecycle>().PrepareStartupView();
                Directory.CreateDirectory("Assets/Quest/Scenes");
                EditorSceneManager.SaveScene(scene, startupScene);
            });
            QuestWizardProgress.Operation("content-bank", true, "Original Android content bank and startup scene built and verified.");
            return new[] { startupScene }.Concat(evidence.selectedScenes).ToArray();
        }

        public static string CampaignShaderValidationMode(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "0") return "minimum";
            if (value == "1") return "exhaustive";
            throw new InvalidOperationException("GHVR_QUEST_VALIDATE_CAMPAIGN_SHADERS accepts only 0 or 1.");
        }

        public static CampaignShaderModeReceipt PrepareCampaignShaders()
        {
            // The maintainer requires normal local builds to perform only the
            // necessary work, including on a cold first build. Retain every
            // original alias and verify physical source/imported/pass/material
            // identities. Unity's native bundle/player builds remain mandatory.
            // The all-alias compiler/decode/reflection gate is an explicit
            // development opt-in and can reuse its completed graphics receipt.
            string mode = CampaignShaderValidationMode(Environment.GetEnvironmentVariable("GHVR_QUEST_VALIDATE_CAMPAIGN_SHADERS"));
            string manifestPath = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_MANIFEST") ?? QuestCampaignShaderValidation.DefaultManifest;
            string outputPath = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_OUTPUT") ?? "QuestCampaignShaderEvidence";
            string before = FileHash(manifestPath);
            var input = JsonUtility.FromJson<QuestCampaignShaderValidation.Manifest>(File.ReadAllText(manifestPath));
            if (input == null || input.schema != 1 || input.scope != "campaign-compiler" || input.graphicsApi != "Vulkan" || input.compilerPlatform != "Vulkan" ||
                input.shaders == null || input.materials == null || input.requiredShaderCount != input.shaders.Length || input.requiredMaterialCount != input.materials.Length)
                throw new InvalidDataException("Complete Campaign graphics provenance is missing before native retention.");
            if (File.Exists(CampaignShaderModeReceiptPath)) File.Delete(CampaignShaderModeReceiptPath);
            QuestCampaignShaderValidation.PrepareVariantCollection(manifestPath);
            QuestCampaignShaderValidation.VerifyImportedIdentities(input);
            bool exhaustive = mode == "exhaustive";
            if (exhaustive) QuestCampaignShaderValidation.Validate(manifestPath, outputPath);
            if (before != FileHash(manifestPath)) throw new InvalidOperationException("Campaign shader manifest changed during build-mode validation.");
            var receipt = new CampaignShaderModeReceipt {
                mode = mode, unityVersion = Application.unityVersion, sourceManifestSha256 = before,
                shaderCount = input.shaders.Length, nativeAliasCount = input.shaders.Sum(row => row.variants.Length), materialCount = input.materials.Length,
                requiredRetentionVerified = true, importedIdentitiesVerified = true, exhaustiveValidationRequested = exhaustive,
                exhaustiveCompilerValidationCompleted = exhaustive,
                exhaustiveResultReused = exhaustive && QuestCampaignShaderValidation.LastValidationCacheReused,
                nativeCompilerQueriesThisInvocation = exhaustive ? QuestCampaignShaderValidation.LastNativeCompileCount : 0,
                exhaustiveReceiptSha256 = exhaustive ? FileHash(Path.Combine(outputPath, "android-compiler.json")) : null
            };
            Directory.CreateDirectory(Path.GetDirectoryName(CampaignShaderModeReceiptPath));
            File.WriteAllText(CampaignShaderModeReceiptPath, JsonUtility.ToJson(receipt, true) + "\n");
            Debug.Log("Campaign shader build mode: " + mode + "; retained original aliases=" + receipt.nativeAliasCount + ", imported shaders=" + receipt.shaderCount +
                ", material identities=" + receipt.materialCount + ", exhaustive compiler result=" + receipt.exhaustiveCompilerValidationCompleted +
                ", reused=" + receipt.exhaustiveResultReused + ", native queries this invocation=" + receipt.nativeCompilerQueriesThisInvocation + ". Original pixels and headset remain separate gates.");
            return receipt;
        }

        [Serializable] sealed class CampaignBuildContract
        {
            public int schema;
            public string scope, inputKey;
            public bool completeOriginalContent, completeCurrentModAot, originalDynamicProceduralAbi, localNativeSaves, originalSessionCodeTransport;
            public ApiFile[] files;
        }

        static void ValidateCampaignBuildContract()
        {
            const string path = "Assets/Quest/Resources/quest-campaign-build-contract.json";
            if (!File.Exists(path)) throw new InvalidOperationException("Full Campaign native/content build contract is missing.");
            var contract = JsonUtility.FromJson<CampaignBuildContract>(File.ReadAllText(path));
            var inputs = JsonUtility.FromJson<InputManifest>(File.ReadAllText(Required("GHVR_QUEST_MANIFEST_PATH")));
            if (contract == null || contract.schema != 1 || contract.scope != "complete-campaign-package"
                || contract.inputKey != inputs.inputKey || !contract.completeOriginalContent || !contract.completeCurrentModAot
                || !contract.originalDynamicProceduralAbi || !contract.localNativeSaves || !contract.originalSessionCodeTransport
                || contract.files == null || contract.files.Length == 0)
                throw new InvalidOperationException("Full Campaign evidence lacks a required original game capability.");
            foreach (var file in contract.files)
                if (file.path.Contains("..") || Path.IsPathRooted(file.path) || !file.path.StartsWith("Assets/", StringComparison.Ordinal)
                    || !File.Exists(file.path) || new FileInfo(file.path).Length != file.size || FileHash(file.path) != file.sha256)
                    throw new InvalidOperationException("Campaign build evidence changed: " + file.path);
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
        static bool OriginalModUsesMultiPass(bool originalMod)
        {
#if GHVR_QUEST_STARTUP
            return originalMod && GloomhavenVR.Core.QuestStandalonePlatform.RequiresMultiPassStereo;
#else
            return false;
#endif
        }
        static void ConfigureXr(bool originalMod)
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
            xr.renderMode = OriginalModUsesMultiPass(originalMod)
                ? OpenXRSettings.RenderMode.MultiPass : OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(xr);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perTarget);
        }
        static QuestNativePluginContract.Contract ConfigureNativePlugin(string selectedBackend)
        {
            var paths = new System.Collections.Generic.List<string> { "Assets/Quest/Plugins/Android/arm64/libghvr_quest_passthrough.so" };
            QuestNativePluginContract.Contract nativeContract = null;
#if GHVR_QUEST_GAME
            nativeContract = QuestNativePluginContract.Configure(selectedBackend);
#endif
            foreach (string path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as PluginImporter;
                if (importer == null) throw new InvalidOperationException("Required ARM64 native plugin is missing: " + path);
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                importer.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
                importer.isPreloaded = false;
                importer.SaveAndReimport();
            }
            return nativeContract;
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
