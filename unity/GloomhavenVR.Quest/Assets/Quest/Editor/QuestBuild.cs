using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
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
        [Serializable] sealed class InputManifest { public string inputKey; }
        [Serializable] sealed class Receipt
        {
            public int schema = 1;
            public string target, inputKey, package, profileSha256, unityVersion, buildResult;
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
            string target = Required("GHVR_QUEST_TARGET");
            if (target != "probe") throw new InvalidOperationException("Game target is gated until recovered assets, platform adapter and complete AOT conversion pass.");
            string apk = Required("GHVR_QUEST_OUTPUT_APK");
            string package = Required("GHVR_QUEST_PACKAGE");
            ConfigureAndroid(package);
            ConfigureNativePlugin();
            ConfigureXr();
            PrepareDiagnosticMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Quest hardware diagnostic").AddComponent<QuestHardwareProbe>();
            Directory.CreateDirectory("Assets/Quest/Scenes");
            const string scenePath = "Assets/Quest/Scenes/QuestHardwareProbe.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Directory.CreateDirectory(Path.GetDirectoryName(apk));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath }, locationPathName = apk,
                target = BuildTarget.Android, options = BuildOptions.Development
            });
            byte[] profile = File.ReadAllBytes(Required("GHVR_QUEST_PROFILE_PATH"));
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(profile)).Replace("-", "").ToLowerInvariant();
            var manifest = JsonUtility.FromJson<InputManifest>(File.ReadAllText(Required("GHVR_QUEST_MANIFEST_PATH")));
            File.WriteAllText(apk + ".build.json", JsonUtility.ToJson(new Receipt
            {
                target = target, inputKey = manifest.inputKey, package = package,
                profileSha256 = hash, unityVersion = Application.unityVersion,
                buildResult = report.summary.result.ToString(), scenes = new[] { scenePath }
            }, true));
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Android build failed: " + report.summary.result + ", errors=" + report.summary.totalErrors);
            Debug.Log("[GloomhavenVR Quest] signed ARM64 IL2CPP hardware diagnostic built: " + apk);
        }
        static void ConfigureAndroid(string package)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Android build target switch failed");
            PlayerSettings.companyName = "GloomhavenVR";
            PlayerSettings.productName = "GloomhavenVR Quest Test";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, package);
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel30;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
            PlayerSettings.runInBackground = true;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = Required("GHVR_QUEST_KEYSTORE_PATH");
            PlayerSettings.Android.keyaliasName = Required("GHVR_QUEST_KEYSTORE_ALIAS");
            PlayerSettings.Android.keystorePass = Required("GHVR_QUEST_KEYSTORE_PASSWORD");
            PlayerSettings.Android.keyaliasPass = Required("GHVR_QUEST_KEYALIAS_PASSWORD");
            // Passwords are never logged or saved in the source template.
            var settings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var input = settings.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 1; settings.ApplyModifiedPropertiesWithoutUndo(); }
            foreach (string item in new[] { "SDK", "NDK", "JDK" })
            {
                string path = Environment.GetEnvironmentVariable(item == "JDK" ? "GHVR_QUEST_JDK" : "GHVR_QUEST_ANDROID_" + item);
                if (string.IsNullOrWhiteSpace(path)) continue;
                EditorPrefs.SetBool("Android" + item + "UseEmbedded", false);
                EditorPrefs.SetString(item == "JDK" ? "JdkPath" : "Android" + item + "Root", path);
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
        static void PrepareDiagnosticMaterials()
        {
            // Only the isolated, explicitly labelled probe uses this approximation.
            // Never accept this conversion as recovered game shader parity.
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Quest/Recovered" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || !material.HasProperty("_Diffuse")) continue;
                var diffuse = material.GetTexture("_Diffuse");
                var normal = material.HasProperty("_NormalMap") ? material.GetTexture("_NormalMap") : null;
                material.shader = Shader.Find("Standard");
                material.SetTexture("_MainTex", diffuse);
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
            }
        }
    }
}
