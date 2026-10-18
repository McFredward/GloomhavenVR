using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string name)
    { _checks++; if (!value) throw new Exception(name); }
    private static Dictionary<string, object?> ProfileValues()
    {
        var values = new Dictionary<string, object?>();
        foreach (var module in ModuleConfig.Snapshot())
            foreach (var entry in module.Value.Entries)
                values.Add(module.Key + "/" + entry.Key.Section + "/" + entry.Key.Key,
                    entry.Value.GetType().GetProperty("Value")!.GetValue(entry.Value));
        return values;
    }
    private static void Main()
    {
        Check(!GloomhavenVR.FrameDefaults.Active, "ordinary PC without a marker retains its own fresh-default selector");
        QuestStandalonePlatform.Enabled = true;
        Check(GloomhavenVR.FrameDefaults.Active, "fresh Quest selects shared standalone defaults without a Frame marker");
        QuestStandalonePlatform.Enabled = false;
        GloomhavenVR.FrameLaunchOptIn.MarkerPresent = true;
        Check(GloomhavenVR.FrameDefaults.Active, "fresh Steam Frame selects the same default family through its launch marker");
        QuestStandalonePlatform.Enabled = true;
        Check(GloomhavenVR.FrameDefaults.Active, "both platform signals still select one shared standalone family");
        QuestStandalonePlatform.Enabled = false;
        GloomhavenVR.FrameLaunchOptIn.MarkerPresent = false;
        Check(!GloomhavenVR.FrameDefaults.Active && ModuleConfig.Files.Count == 0,
            "platform detection neither binds profiles nor persists settings");
        Check(!GraphicsProfiles.Apply(-1) && !GraphicsProfiles.Apply(4), "invalid indices cannot change native/config state");
        Check(ModuleConfig.Files.Count == 0, "invalid profile does not bind or persist settings");
        Check(!GraphicsProfiles.Apply(0), "absent native UI fails closed before control changes");
        Check(ModuleConfig.Files.Count == 0, "unavailable native UI does not write VR controls");
        var invalid = new Gloomhaven.GraphicSettings(); invalid.gameObject.scene.Valid=false;
        var cold = new Gloomhaven.GraphicSettings(false);
        var native = new Gloomhaven.GraphicSettings();
        UnityEngine.Object.Settings = new[] {invalid, cold, native};
        RenderQuality.Bind(); PerfConfig.Bind(); WorldUIConfig.Bind(); WallFadeTuning.Bind();
        Check(RenderQuality.MsaaLevel.Value == 8 && RenderQuality.ForceTextureStreamingOff.Value,
            "fresh PC bindings seed High-End MSAA and readable textures before any profile action");
        Check(PerfConfig.WallVisibilityModeCount.Value == 2 && PerfConfig.WallAutoHideBelowFpsCount.Value == 10,
            "fresh PC bindings seed the approved automatic wall policy");
        for (int i=0; i<4; i++)
        {
            Check(GraphicsProfiles.Apply(i), "all profiles apply on original initialized scene UI");
            string[] levels={"Fastest","Simple","Good","Fantastic"};
            Check(QualitySettings.Selected==levels[i] && native.Calls==i+1 && native.Saves==i+1, "native callback and persistence selected once");
            Check(invalid.Calls==0 && cold.Calls==0, "prefabs/uninitialized graphics views are untouched");
            Check(RenderQuality.MsaaLevel!.Value==(i==0?0:i==1?2:i==2?4:8), "MSAA quality progresses across profiles");
            Check(RenderQuality.ForceTextureStreamingOff.Value == (i != 0),
                "PC profiles preserve the readable-texture policy; Standalone retains native streaming");
            Check(RenderQuality.EyeResolutionScale!.Value==(i==0?.8f:1f) && RenderQuality.PixelLightCount!.Value==0,
                "standalone selects 0.8 while all PC quality profiles retain 1.0 and the safe light cap");
            Check(PerfConfig.ScenarioFigureEffectsDensityPercent.Value==(i==0?0:i==1?25:i==2?60:100), "figure FX budget spans disabled through original");
            Check(PerfConfig.ScenarioEnvironmentEffectsDensityPercent.Value==PerfConfig.ScenarioFigureEffectsDensityPercent.Value, "environment FX profile matches visible detail choice");
            Check(WorldUIConfig.ImmersiveTownServices.Value==(i!=0) && WindowMaterialise.Entry.Value==(i!=0), "standalone disables costly NPC/dust; PC restores original features");
            Check(PerfConfig.ScenarioFigureClothSimulation.Value==(i>=2), "cloth simulation trade is explicit");
            Check(PerfConfig.ReduceScenarioGenerationDetail.Value==(i<=1), "next-load generation trade is explicit");
            Check(PerfConfig.EnvironmentMeshBankOn, "exact full-detail private mesh preparation applies to every profile");
            Check(PerfConfig.ScenarioExplicitEnvironmentInstancing.Value==(i<3), "explicit draw option is independent and disabled in original quality");
            Check(PerfConfig.ScenarioCheapWallShading.Value==(i<2), "wall shading compromise is explicit for low profiles");
            Check(PerfConfig.ScenarioTerrainSubstitution.Value,
                "explicit profiles restore the prepared-terrain path before later individual comparisons");
            Check(PerfConfig.WorldMaterialQualityModeCount.Value==(i==0?2:0),
                "standalone requests radical audited world shading while ordinary PC profiles keep native materials");
            Check(PerfConfig.WorldMaterialAmbientPercent.Value==100,
                "all explicit profiles preserve scene atmosphere when mode 2 is selected");
            Check(PerfConfig.ScenarioTerrainDetailPercent.Value==(i==0?0:i==1?50:100), "3D terrain detail restores original at balanced/high quality");
            Check(PerfConfig.ScenarioDistantTerrainDetailPercent.Value==(i<2?0:i==2?50:100), "distant 3D geometry has an independent cap");
            Check(PerfConfig.ScenarioTerrainCameraSourceLimitCount.Value==(i==0?64:0),
                "standalone bounds terrain substitution work while PC quality profiles retain unlimited originals-compatible substitution");
            Check(PerfConfig.ScenarioTerrainDistanceMeters.Value==GloomhavenVR.FrameDefaults.ScenarioTerrainDistanceMeters, "VR distance threshold is platform-independent");
            Check(PerfConfig.ScenarioTerrainPillarDistanceLod.Value,
                "every explicit profile restores independent pillar distance LOD");
            Check(PerfConfig.WallVisibilityModeCount.Value == 2 && PerfConfig.WallAutoHideBelowFpsCount.Value == 10,
                "explicit profiles restore the approved automatic wall policy at10FPS");
            Check(PerfConfig.WallFadeEvalInterval.Value == 0f && WallFadeTuning.EvalIntervalSecondsEntry.Value == (i == 0 ? .25f : 0f),
                "profiles reset both wall cadence doors to their respective fresh defaults");
            Check(ModuleConfig.Get("rig").TryGetEntry(new BepInEx.Configuration.ConfigDefinition("Sky", "Style"),
                    out BepInEx.Configuration.ConfigEntry<SkyStyle> sky) && sky.Value == (i == 0 ? SkyStyle.OffBlack : SkyStyle.SwampNight),
                "the original bound sky entry receives the Standalone or PC environment");
            Check(PerfConfig.SharedEnvironmentMaterialReadsOn && PerfConfig.SharedUiWindowReadsOn, "environment and UI work removal apply universally without a quality switch");
            foreach (var file in ModuleConfig.Snapshot())
                foreach (var key in file.Value.Entries.Keys)
                    Check(!key.Key.StartsWith("VisibleIdle",StringComparison.Ordinal)
                        && key.Key != "SharedEnvironmentMaterialReads" && key.Key != "SkipHiddenWallAttachmentWrites"
                        && key.Key != "ScenarioEnvironmentMeshBank" && key.Key != "SharedUiWindowReads",
                        "profiles never bind retired idle or pure environment work-removal controls");
            foreach(var file in ModuleConfig.Snapshot()) Check(file.Value.SaveOnConfigSet, "autosave flags restored for every file");
        }
        PerfConfig.ScenarioTerrainPillarDistanceLod.Value = false;
        PerfConfig.WallVisibilityModeCount.Value = 1;
        PerfConfig.WallAutoHideBelowFpsCount.Value = 7;
        PerfConfig.WallFadeEvalInterval.Value = .1f;
        WallFadeTuning.EvalIntervalSecondsEntry.Value = .2f;
        Check(GraphicsProfiles.Apply(0), "can return to standalone after high-end and independent edits");
        Check(PerfConfig.ScenarioTerrainPillarDistanceLod.Value && PerfConfig.WallVisibilityModeCount.Value == 2
            && PerfConfig.WallAutoHideBelowFpsCount.Value == 10 && PerfConfig.WallFadeEvalInterval.Value == 0
            && WallFadeTuning.EvalIntervalSecondsEntry.Value == .25f,
            "Standalone restores a known wall and pillar state after arbitrary saved overrides");
        GloomhavenVR.FrameLaunchOptIn.MarkerPresent = true;
        Check(GraphicsProfiles.Apply(0), "can return to shared standalone on Steam Frame after high-end");
        var frameValues = ProfileValues();
        int frameCalls = native.Calls;
        GloomhavenVR.FrameLaunchOptIn.MarkerPresent = false;
        QuestStandalonePlatform.Enabled = true;
        Check(GraphicsProfiles.Apply(0) && native.Calls == frameCalls + 1 && native.Saves == native.Calls,
            "Quest shared standalone profile invokes and persists the original native callback once");
        var questValues = ProfileValues();
        Check(questValues.Count == frameValues.Count, "Quest and Steam Frame expose the same complete profile controls");
        foreach (var value in frameValues)
            Check(questValues.TryGetValue(value.Key, out var actual) && Equals(actual, value.Value),
                "Quest and Steam Frame share the actual production standalone choice: " + value.Key);
        QuestStandalonePlatform.Enabled = false;
        Check(!GloomhavenVR.FrameDefaults.Active, "returning to ordinary PC clears the platform signal without replacing chosen controls");
        Check(RenderQuality.TextureStreamingBudgetMB!.Value==GloomhavenVR.FrameDefaults.TextureStreamingBudgetMB, "standalone streaming memory matches Frame constant");
        Check(PerfConfig.UiMaintenanceIntervalSeconds.Value==GloomhavenVR.FrameDefaults.UiMaintenanceIntervalSeconds && WallFadeTuning.RescanIntervalSecondsEntry!.Value==GloomhavenVR.FrameDefaults.WallRescanIntervalSeconds, "standalone maintenance/wall cadence matches Frame constants");
        Check(PerfConfig.ScenarioSceneryDensityPercent.Value==0 && PerfConfig.ScenarioDecorationDensityPercent.Value==0 && PerfConfig.ScenarioVegetationDensityPercent.Value==0, "standalone removes eligible scenery classes");
        Check(PerfConfig.ScenarioPlayerFigureDetailPercent.Value==0 && PerfConfig.ScenarioEnemyFigureDetailPercent.Value==0 && !WorldUIConfig.DesktopMirrorLeftEye.Value, "standalone figure and desktop defaults apply on PC too");
        RenderQuality.MsaaLevel!.Value=4;
        Check(RenderQuality.MsaaLevel.Value==4, "later individual edit is not overwritten by stored profile");
        RenderQuality.EyeResolutionScale!.Value=.95f;
        RenderQuality.Bind();
        Check(RenderQuality.EyeResolutionScale.Value==.95f,
            "individual resolution remains independent after Standalone until another profile is explicitly chosen");
        PerfConfig.ScenarioTerrainCameraSourceLimitCount.Value=32;
        PerfConfig.WorldMaterialQualityModeCount.Value=1;
        PerfConfig.WorldMaterialAmbientPercent.Value=40;
        PerfConfig.Bind();
        Check(PerfConfig.ScenarioTerrainCameraSourceLimitCount.Value==32,
            "terrain substitution source limit remains independently adjustable after Standalone");
        Check(PerfConfig.WorldMaterialQualityModeCount.Value==1,
            "material shading stage remains independently adjustable after Standalone");
        Check(PerfConfig.WorldMaterialAmbientPercent.Value==40,
            "ambient weight remains independently adjustable after an explicit profile");
        PerfConfig.ScenarioTerrainSubstitution.Value=false;
        PerfConfig.Bind();
        Check(!PerfConfig.ScenarioTerrainSubstitution.Value,
            "the terrain geometry comparison survives rebinding without selecting another preset");
        Check(PerfConfig.ScenarioTerrainDetailPercent.Value==0 && PerfConfig.WorldMaterialQualityModeCount.Value==1,
            "turning off terrain substitutes retains saved detail and independent world-material choices");
        PerfConfig.ScenarioCheapWallShading.Value=false;
        PerfConfig.ScenarioExplicitEnvironmentInstancing.Value=false;
        PerfConfig.ScenarioTerrainDetailPercent.Value=75;
        PerfConfig.ScenarioDistantTerrainDetailPercent.Value=25;
        Check(!PerfConfig.ScenarioCheapWallShading.Value && !PerfConfig.ScenarioExplicitEnvironmentInstancing.Value,
            "individual rendering toggles remain independently editable after preset");
        Check(PerfConfig.ScenarioTerrainDetailPercent.Value==75 && PerfConfig.ScenarioDistantTerrainDetailPercent.Value==25,
            "near and distant detail choices remain independent after preset");
        Check(PerfConfig.SharedEnvironmentMaterialReadsOn && PerfConfig.SharedUiWindowReadsOn,
            "environment and UI work removal stay universal across individual quality edits");
        int calls=native.Calls; var names=QualitySettings.names; QualitySettings.names=new[]{"Good"};
        Check(!GraphicsProfiles.Apply(0) && native.Calls==calls && RenderQuality.MsaaLevel.Value==4, "missing native level leaves tuned VR controls untouched");
        QualitySettings.names=names; native.ThrowOnCallback=true;
        Check(!GraphicsProfiles.Apply(0) && RenderQuality.MsaaLevel.Value==4, "native callback failure leaves tuned controls untouched");
        native.ThrowOnCallback=false;
        var authoritative = new Gloomhaven.GraphicSettings();
        SceneController.Instance = new(authoritative);
        Check(GraphicsProfiles.Apply(2) && authoritative.Calls==1 && native.Calls==calls, "serialized boot owner takes priority over incidental initialized settings");
        SceneController.Instance = null;
        ModuleConfig.Get("rig").SaveOnConfigSet=false;
        ModuleConfig.Get("perf").ThrowOnSave=true;
        Check(GraphicsProfiles.Apply(0), "save failure does not undo live choices or poison unrelated files");
        Check(!ModuleConfig.Get("rig").SaveOnConfigSet && ModuleConfig.Get("worldui").SaveOnConfigSet, "all saved autosave modes restored even if one file fails");
        PerfConfig.ScenarioDecorationDensityPercent.ThrowOnWrite=true;
        try { GraphicsProfiles.Apply(1); throw new Exception("fault not reached"); } catch(InvalidOperationException) { }
        Check(!ModuleConfig.Get("rig").SaveOnConfigSet && ModuleConfig.Get("perf").SaveOnConfigSet && ModuleConfig.Get("worldui").SaveOnConfigSet, "config exception restores every file's save flags");
        Console.WriteLine("PASS: "+_checks+" production profile boundary assertions (native rendering/headset appearance require hardware)");
    }
}
