using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

// Deliberately abort at the first preprocess boundary, before native code,
// player content or APK packaging. The real package callback runs INSIDE this
// boundary so its actual isBuildingPlayer admission guard is exercised.
public sealed class CompactOpenXRBoundaryProbe : IPreprocessBuildWithReport
{
    public int callbackOrder => int.MinValue;
    public static bool armed;
    public static Action<BuildReport> observe;
    public void OnPreprocessBuild(BuildReport report)
    {
        if (!armed) throw new BuildFailedException("Fixture safety stop: no player compilation is permitted.");
        try { observe(report); }
        finally { throw new BuildFailedException("Fixture safety stop after OpenXR preprocess observation; no player compilation is permitted."); }
    }
}

public static class CompactOpenXRPreparationFixture
{
    [Serializable] public sealed class Probe
    {
        public string mode, error, buildResult;
        public bool callbackReached, isBuildingPlayer, packageCallbackSucceeded, noPlayerOutput;
    }
    [Serializable] sealed class Result
    {
        public bool success, publicAccessorAloneInsufficient, exactStaleSettingsBoundary,
            registeredBeforeBuild, retryUnchanged, rootsUnchanged, loaderConfigurationUnchanged,
            enabledFeaturesUnchanged, packageAssetPersisted, noPlayerOutput;
        public int featureCount;
        public double preparationSeconds, retrySeconds;
        public string error;
        public List<Probe> probes = new List<Probe>();
    }
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static Exception Unwrap(Exception error)
    { while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException; return error; }
    static string SettingsBytes()
    {
        return string.Join("\n", Directory.GetFiles("Assets/XR", "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => path + ":" + File.GetLastWriteTimeUtc(path).Ticks + ":" + Convert.ToBase64String(File.ReadAllBytes(path))));
    }
    static string LoaderState()
    {
        EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>(XRGeneralSettings.k_SettingsKey, out var config);
        return config == null ? "absent" : AssetDatabase.GetAssetPath(config) + ":" + EditorJsonUtility.ToJson(config, true);
    }
    static string Features()
    {
        return string.Join("\n", OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android).GetFeatures<OpenXRFeature>()
            .Select(feature => feature.GetType().FullName + ":" + feature.enabled).OrderBy(value => value, StringComparer.Ordinal));
    }
    static Probe ProbeBoundary(string mode, string scenePath)
    {
        var probe = new Probe { mode = mode };
        string output = "ProbeOutput/" + mode + ".apk";
        Directory.CreateDirectory("ProbeOutput");
        CompactOpenXRBoundaryProbe.armed = true;
        CompactOpenXRBoundaryProbe.observe = report => {
            probe.callbackReached = true;
            probe.isBuildingPlayer = BuildPipeline.isBuildingPlayer;
            try
            {
                Type callbackType = typeof(FeatureHelpers).Assembly.GetType("UnityEditor.XR.OpenXR.Features.OpenXRChooseRuntimeLibraries", true);
                ((IPreprocessBuildWithReport)Activator.CreateInstance(callbackType)).OnPreprocessBuild(report);
                probe.packageCallbackSucceeded = true;
            }
            catch (Exception error) { probe.error = Unwrap(error).ToString(); }
        };
        try
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { target = BuildTarget.Android,
                scenes = new[] { scenePath }, locationPathName = output, options = BuildOptions.Development });
            probe.buildResult = report.summary.result.ToString();
        }
        finally { CompactOpenXRBoundaryProbe.armed = false; CompactOpenXRBoundaryProbe.observe = null; }
        probe.noPlayerOutput = !File.Exists(output) && !Directory.GetFiles("ProbeOutput", "libil2cpp.so", SearchOption.AllDirectories).Any()
            && !Directory.GetFiles("ProbeOutput", "global-metadata.dat", SearchOption.AllDirectories).Any();
        Require(probe.callbackReached && probe.isBuildingPlayer && probe.noPlayerOutput && probe.buildResult == "Failed",
            "The probe did not stop safely at the real preprocess boundary: " + mode);
        return probe;
    }

    public static void Run()
    {
        var result = new Result();
        try
        {
            Require(EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android), "Android target unavailable.");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "dev.gloomhavenvr.fixture.openxr");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel30;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            const string scenePath = "Assets/Probe.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            // Persist exactly the package asset whose missing registration
            // caused the supplied Windows capture, without assigning a loader.
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            AssetDatabase.SaveAssets();
            result.packageAssetPersisted = File.Exists("Assets/XR/Settings/OpenXR Package Settings.asset");
            Require(result.packageAssetPersisted, "Pinned package did not persist its settings asset.");
            string enabledBefore = Features();
            EditorBuildSettings.RemoveConfigObject(Constants.k_SettingsKey);
            result.publicAccessorAloneInsufficient = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android) == null;
            Require(result.publicAccessorAloneInsufficient, "Negative control did not recreate unregistered persisted settings.");
            var negative = ProbeBoundary("unprepared", scenePath);
            result.probes.Add(negative);
            result.exactStaleSettingsBoundary = !negative.packageCallbackSucceeded && negative.error != null
                && negative.error.Contains("OpenXR Settings found in project but not yet loaded.  Please build again.");
            Require(result.exactStaleSettingsBoundary, "The actual package did not reproduce the reported settings boundary.");

            // Its failed callback has registered the container too late. Clear
            // that incidental change, then invoke the production early path.
            EditorBuildSettings.RemoveConfigObject(Constants.k_SettingsKey);
            var roots = PlayerSettings.GetPreloadedAssets();
            string loaderBefore = LoaderState();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            QuestCodeUpdateBuild.PrepareOpenXRForBuild();
            result.preparationSeconds = watch.Elapsed.TotalSeconds;
            result.registeredBeforeBuild = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android) != null;
            AssetDatabase.SaveAssets();
            string firstBytes = SettingsBytes();
            watch.Restart();
            QuestCodeUpdateBuild.PrepareOpenXRForBuild();
            result.retrySeconds = watch.Elapsed.TotalSeconds;
            AssetDatabase.SaveAssets();
            result.retryUnchanged = firstBytes == SettingsBytes();
            result.enabledFeaturesUnchanged = enabledBefore == Features();
            result.featureCount = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android).GetFeatures<OpenXRFeature>().Length;
            result.rootsUnchanged = roots.SequenceEqual(PlayerSettings.GetPreloadedAssets());
            result.loaderConfigurationUnchanged = loaderBefore == LoaderState();
            Require(result.registeredBeforeBuild && result.retryUnchanged && result.enabledFeaturesUnchanged
                && result.rootsUnchanged && result.loaderConfigurationUnchanged, "Production preparation changed retained configuration unexpectedly.");
            var positive = ProbeBoundary("prepared", scenePath);
            result.probes.Add(positive);
            Require(positive.packageCallbackSucceeded && positive.error == null, "Prepared package callback failed: " + positive.error);
            result.noPlayerOutput = result.probes.All(probe => probe.noPlayerOutput);
            result.success = result.noPlayerOutput;
        }
        catch (Exception error) { result.error = Unwrap(error).ToString(); }
        finally
        {
            File.WriteAllText(Environment.GetEnvironmentVariable("GHVR_QUEST_OPENXR_FIXTURE_RESULT"), JsonUtility.ToJson(result, true) + "\n");
            if (!result.success) { Debug.LogError(result.error); EditorApplication.Exit(1); }
        }
    }
}
