using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Gloomhaven;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// Four explicit graphics actions requested in the Build619 audit. A profile is never a
/// startup latch: it writes the existing controls once, and subsequent individual edits win.
/// Standalone uses the same constants as fresh Frame profiles, on every supported headset.
/// Native quality uses the game's own settings callback, including its save and UI refresh.
/// </summary>
internal static class GraphicsProfiles
{
    private static readonly string[] NativeLevels = { "Fastest", "Simple", "Good", "Fantastic" };

    internal static bool Apply(int index)
    {
        if (index < 0 || index >= NativeLevels.Length) return false;
        // Resolve the original callback before changing any VR controls. Never instantiate the
        // native settings prefab, initialize its screen, or write gameplay/save data ourselves.
        if (!ApplyNativeQuality(NativeLevels[index])) return false;
        RenderQuality.Bind();
        PerfConfig.Bind();
        WorldUIConfig.Bind();
        WallFadeTuning.Bind();
        _ = WindowMaterialise.Enabled; // Bind the existing dust control, including at main menu.
        KeyValuePair<string, ConfigFile>[] files = ModuleConfig.Snapshot();
        var saveFlags = new bool[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            saveFlags[i] = files[i].Value.SaveOnConfigSet;
            files[i].Value.SaveOnConfigSet = false;
        }
        try
        {
            bool standalone = index == 0, low = index <= 1;
            int density = index == 0 ? 0 : index == 1 ? 25 : index == 2 ? 60 : 100;
            int body = index == 0 ? 0 : index == 1 ? 33 : index == 2 ? 66 : 100;
            Set(RenderQuality.MsaaLevel, standalone ? FrameDefaults.MsaaLevel : index == 1 ? 2 : index == 2 ? 4 : 8);
            Set(RenderQuality.EyeResolutionScale, FrameDefaults.EyeResolutionScale);
            Set(RenderQuality.ForceAnisotropic, FrameDefaults.ForceAnisotropic);
            Set(RenderQuality.ForceFullTextureResolution, FrameDefaults.ForceFullTextureResolution);
            Set(RenderQuality.ForceTextureStreamingOff, FrameDefaults.ForceTextureStreamingOff);
            Set(RenderQuality.TextureStreamingBudgetMB, standalone ? FrameDefaults.TextureStreamingBudgetMB : Defaults.TextureStreamingBudgetMB);
            // The maintainer's earlier pixel-light ruling still applies to every profile.
            Set(RenderQuality.PixelLightCount, FrameDefaults.PixelLightCount);
            Set(PerfConfig.ScenarioSceneryDensityPercent, standalone ? FrameDefaults.ScenarioSceneryDensityPercent : density);
            Set(PerfConfig.ScenarioDecorationDensityPercent, standalone ? FrameDefaults.ScenarioDecorationDensityPercent : density);
            Set(PerfConfig.ScenarioVegetationDensityPercent, standalone ? FrameDefaults.ScenarioVegetationDensityPercent : density);
            Set(PerfConfig.ScenarioPlayerFigureDetailPercent, standalone ? FrameDefaults.ScenarioPlayerFigureDetailPercent : body);
            Set(PerfConfig.ScenarioEnemyFigureDetailPercent, standalone ? FrameDefaults.ScenarioEnemyFigureDetailPercent : body);
            Set(PerfConfig.ScenarioFigureEffectsDensityPercent, standalone ? FrameDefaults.ScenarioFigureEffectsDensityPercent : density);
            Set(PerfConfig.ScenarioEnvironmentEffectsDensityPercent, standalone ? FrameDefaults.ScenarioEnvironmentEffectsDensityPercent : density);
            Set(PerfConfig.ScenarioFigureClothSimulation, standalone ? FrameDefaults.ScenarioFigureClothSimulation : !low);
            Set(PerfConfig.ReduceScenarioGenerationDetail, standalone ? FrameDefaults.ReduceScenarioGenerationDetail : low);
            Set(PerfConfig.ScenarioSimpleEnvironmentShading, standalone ? FrameDefaults.ScenarioSimpleEnvironmentShading : low);
            Set(PerfConfig.ScenarioStaticBatching, standalone ? FrameDefaults.ScenarioStaticBatching : index < 3);
            Set(PerfConfig.ScenarioStructuralBatching, standalone ? FrameDefaults.ScenarioStructuralBatching : index < 3);
            Set(PerfConfig.ScenarioStructuralInstancing, FrameDefaults.ScenarioStructuralInstancing);
            // One-time presets only: every new control remains independently editable.
            Set(PerfConfig.ScenarioEnvironmentMeshBank, standalone ? FrameDefaults.ScenarioEnvironmentMeshBank : index < 3);
            Set(PerfConfig.ScenarioExplicitEnvironmentInstancing, standalone ? FrameDefaults.ScenarioExplicitEnvironmentInstancing : index < 3);
            Set(PerfConfig.ScenarioCheapWallShading, standalone ? FrameDefaults.ScenarioCheapWallShading : low);
            Set(PerfConfig.ScenarioTerrainDetailPercent, standalone ? FrameDefaults.ScenarioTerrainDetailPercent : index == 1 ? 50 : 100);
            Set(PerfConfig.ScenarioDistantTerrainDetailPercent, standalone ? FrameDefaults.ScenarioDistantTerrainDetailPercent : index == 1 ? 0 : index == 2 ? 50 : 100);
            Set(PerfConfig.ScenarioTerrainDistanceMeters, FrameDefaults.ScenarioTerrainDistanceMeters);
            Set(PerfConfig.SharedEnvironmentMaterialReads, true);
            Set(PerfConfig.SharedUiWindowReads, true);
            Set(PerfConfig.VisibleIdleAnimationIntervalSeconds, standalone ? FrameDefaults.VisibleIdleAnimationIntervalSeconds : 0f);
            Set(PerfConfig.FigureDistanceLod, standalone ? FrameDefaults.FigureDistanceLod : index < 3);
            Set(PerfConfig.SkinningBoneLimit, standalone ? FrameDefaults.SkinningBoneLimit : low ? 2 : index == 2 ? 4 : 0);
            Set(PerfConfig.OffscreenIdleAnimation, standalone ? FrameDefaults.OffscreenIdleAnimation : index < 3);
            Set(PerfConfig.UiMaintenanceIntervalSeconds, standalone ? FrameDefaults.UiMaintenanceIntervalSeconds : low ? .1f : 0f);
            Set(PerfConfig.ActorBarPoseCheckIntervalSeconds, standalone ? FrameDefaults.ActorBarPoseCheckIntervalSeconds : low ? .1f : 0f);
            Set(PerfConfig.InitiativeDepthEvalInterval, standalone ? FrameDefaults.InitiativeDepthEvalInterval : low ? .1f : 0f);
            Set(WallFadeTuning.RescanIntervalSecondsEntry, standalone ? FrameDefaults.WallRescanIntervalSeconds : Defaults.RescanIntervalSeconds);
            Set(WorldUIConfig.DesktopMirrorLeftEye, standalone ? FrameDefaults.DesktopMirrorLeftEye : Defaults.DesktopMirrorLeftEye);
            Set(WorldUIConfig.ImmersiveTownServices, standalone ? FrameDefaults.ImmersiveTownServices : Defaults.ImmersiveTownServices);
            SetBound(files, "WorldUI", "WindowMaterialise", standalone ? FrameDefaults.WindowMaterialise : Defaults.WindowMaterialise);
            VRLog.Info("Rig", $"Graphics profile applied: {NativeLevels[index]} / VR profile {index}; individual settings remain adjustable.");
            return true;
        }
        finally
        {
            for (int i = 0; i < files.Length; i++)
                files[i].Value.SaveOnConfigSet = saveFlags[i];
            for (int i = 0; i < files.Length; i++)
            {
                if (!saveFlags[i]) continue;
                try { files[i].Value.Save(); }
                catch (Exception error)
                {
                    VRLog.Warn("Rig", "Graphics profile persistence failed for " + files[i].Key
                        + ": " + error.GetType().Name + "; current session values remain active.");
                }
            }
        }
    }

    private static void Set<T>(ConfigEntry<T>? entry, T value)
    {
        if (entry != null) entry.Value = value;
    }

    private static void SetBound<T>(KeyValuePair<string, ConfigFile>[] files, string section, string key, T value)
    {
        var definition = new ConfigDefinition(section, key);
        foreach (KeyValuePair<string, ConfigFile> file in files)
            if (file.Value.TryGetEntry(definition, out ConfigEntry<T> entry))
            { entry.Value = value; return; }
    }

    private static bool ApplyNativeQuality(string name)
    {
        if (Array.IndexOf(QualitySettings.names, name) < 0)
        {
            VRLog.Warn("Rig", $"Graphics profile not applied: native quality '{name}' is unavailable.");
            return false;
        }
        try
        {
            foreach (GraphicSettings settings in NativeSettingsOwners())
            {
                if (settings == null || !settings.gameObject.scene.IsValid()) continue;
                // Awake initializes both fields before this original private input callback is safe.
                if (AccessTools.Field(typeof(GraphicSettings), "levelOpts")?.GetValue(settings) == null
                    || AccessTools.Field(typeof(GraphicSettings), "unityProfiles")?.GetValue(settings) == null) continue;
                var callback = AccessTools.Method(typeof(GraphicSettings), "SetQualityLevel", new[] { typeof(string) });
                if (callback == null) break;
                callback.Invoke(settings, new object[] { name });
                return true;
            }
        }
        catch (Exception error)
        {
            VRLog.Warn("Rig", "Graphics profile not applied: native settings callback failed: " + error.GetType().Name);
            return false;
        }
        VRLog.Warn("Rig", "Graphics profile not applied: original game graphics settings are not ready.");
        return false;
    }

    private static IEnumerable<GraphicSettings> NativeSettingsOwners()
    {
        // SceneController boot explicitly initializes this exact serialized owner, including
        // when the native graphics page has never been opened. Prefer it over incidental views.
        SettingsHolder? holder = SceneController.Instance != null ? SceneController.Instance.SettingsHolder : null;
        GraphicSettings? primary = holder != null
            ? AccessTools.Field(typeof(SettingsHolder), "_graphicSettings")?.GetValue(holder) as GraphicSettings : null;
        if (primary != null) yield return primary;
        foreach (GraphicSettings candidate in UnityEngine.Object.FindObjectsOfType<GraphicSettings>(true))
            if (candidate != primary) yield return candidate;
    }
}
