using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

// One mod binary for PC and Frame. The maintainer requested independent live controls
// for every new compromise (2026-10-05); profile detection only seeds unsaved values.
internal static partial class PerfConfig
{
    internal static ConfigEntry<bool> ScenarioEnvironmentMeshBank = null!;
    internal static ConfigEntry<bool> ScenarioExplicitEnvironmentInstancing = null!;
    internal static ConfigEntry<bool> ScenarioCheapWallShading = null!;
    internal static ConfigEntry<int> WorldMaterialQualityModeCount = null!;
    internal static ConfigEntry<int> ScenarioTerrainDetailPercent = null!;
    internal static ConfigEntry<int> ScenarioDistantTerrainDetailPercent = null!;
    internal static ConfigEntry<float> ScenarioTerrainDistanceMeters = null!;
    internal static ConfigEntry<int> ScenarioTerrainCameraSourceLimitCount = null!;
    internal static ConfigEntry<bool> SharedEnvironmentMaterialReads = null!;
    internal static ConfigEntry<bool> SharedUiWindowReads = null!;

    internal static bool EnvironmentMeshBankOn => ScenarioEnvironmentMeshBank?.Value ?? Defaults.ScenarioEnvironmentMeshBank;
    internal static bool EnvironmentDrawInstancingOn => ScenarioExplicitEnvironmentInstancing?.Value ?? Defaults.ScenarioExplicitEnvironmentInstancing;
    internal static bool CheapWallShadingOn => ScenarioCheapWallShading?.Value ?? Defaults.ScenarioCheapWallShading;
    internal static int WorldMaterialQualityMode => Mathf.Clamp(WorldMaterialQualityModeCount?.Value ?? Defaults.WorldMaterialQualityModeCount, 0, 2);
    internal static int TerrainDetailPercent => Mathf.Clamp(ScenarioTerrainDetailPercent?.Value ?? Defaults.ScenarioTerrainDetailPercent, 0, 100);
    internal static int DistantTerrainDetailPercent => Mathf.Clamp(ScenarioDistantTerrainDetailPercent?.Value ?? Defaults.ScenarioDistantTerrainDetailPercent, 0, 100);
    internal static float TerrainDistanceMeters => Mathf.Clamp(ScenarioTerrainDistanceMeters?.Value ?? Defaults.ScenarioTerrainDistanceMeters, .1f, 10f);
    internal static int TerrainCameraSourceLimit => Mathf.Clamp(ScenarioTerrainCameraSourceLimitCount?.Value ?? Defaults.ScenarioTerrainCameraSourceLimitCount, 0, 2048);
    internal static bool SharedEnvironmentMaterialReadsOn => SharedEnvironmentMaterialReads?.Value ?? Defaults.SharedEnvironmentMaterialReads;
    internal static bool SharedUiWindowReadsOn => SharedUiWindowReads?.Value ?? Defaults.SharedUiWindowReads;

    private static void BindFrameRendering(ConfigFile file)
    {
        ScenarioEnvironmentMeshBank = file.Bind("Optimize", "ScenarioEnvironmentMeshBank",
            FrameDefaults.Active ? FrameDefaults.ScenarioEnvironmentMeshBank : Defaults.ScenarioEnvironmentMeshBank,
            "Use private verified original environment meshes for render chunks when native meshes are unreadable. Source meshes, colliders and native objects remain unchanged. Works live on PC and Frame.");
        ScenarioExplicitEnvironmentInstancing = file.Bind("Optimize", "ScenarioExplicitEnvironmentInstancing",
            FrameDefaults.Active ? FrameDefaults.ScenarioExplicitEnvironmentInstancing : Defaults.ScenarioExplicitEnvironmentInstancing,
            "Submit eligible repeated static environment geometry in small camera-bound groups. Restores original rendering on unsupported or changed sources. Works live on PC and Frame.");
        ScenarioCheapWallShading = file.Bind("Optimize", "ScenarioCheapWallShading",
            FrameDefaults.Active ? FrameDefaults.ScenarioCheapWallShading : Defaults.ScenarioCheapWallShading,
            "Use cheaper original-textured wall materials while retaining continuous native wall dissolution. Floors keep original shading. Omits fine lighting and surface detail. Used when WorldMaterialQualityModeCount is 0. Works live; Off restores original materials.");
        WorldMaterialQualityModeCount = file.Bind("Optimize", "WorldMaterialQualityModeCount",
            FrameDefaults.Active ? FrameDefaults.WorldMaterialQualityModeCount : Defaults.WorldMaterialQualityModeCount,
            new ConfigDescription("Material shading for supported native static scenery throughout the game and DLCs: 0 = original, 1 = simple lighting, 2 = simple textured color. Lower-quality modes omit fine normal, gloss and surface lighting while retaining original 3D geometry, color textures and native animated visibility. Unsupported materials retain original rendering. At 0 the existing floor/wall shading switches apply; 1/2 take precedence over them. Works live on PC and Frame; fresh Frame/Standalone profiles use 2.", new AcceptableValueRange<int>(0, 2)));
        ScenarioTerrainDetailPercent = file.Bind("Optimize", "ScenarioTerrainDetailPercent",
            FrameDefaults.Active ? FrameDefaults.ScenarioTerrainDetailPercent : Defaults.ScenarioTerrainDetailPercent,
            new ConfigDescription("Eligible static wall and pillar mesh detail: 100 preserves original geometry, 0 uses the strongest available prepared 3D simplification. Floors, doors, actors, targeting and gameplay collision remain available. Works live on PC and Frame.", new AcceptableValueRange<int>(0, 100)));
        ScenarioDistantTerrainDetailPercent = file.Bind("Optimize", "ScenarioDistantTerrainDetailPercent",
            FrameDefaults.Active ? FrameDefaults.ScenarioDistantTerrainDetailPercent : Defaults.ScenarioDistantTerrainDetailPercent,
            new ConfigDescription("Additional eligible wall and pillar mesh detail cap beyond ScenarioTerrainDistanceMeters. 100 keeps selected near detail; lower values use coarser prepared 3D geometry. Revealed rooms and tactical contents remain represented.", new AcceptableValueRange<int>(0, 100)));
        ScenarioTerrainDistanceMeters = file.Bind("Optimize", "ScenarioTerrainDistanceMeters",
            FrameDefaults.Active ? FrameDefaults.ScenarioTerrainDistanceMeters : Defaults.ScenarioTerrainDistanceMeters,
            new ConfigDescription("Viewing distance in VR metres beyond which the distant wall and pillar detail cap applies. Native game and room visibility remain unchanged.", new AcceptableValueRange<float>(.1f, 10f)));
        SharedEnvironmentMaterialReads = file.Bind("Optimize", "SharedEnvironmentMaterialReads",
            FrameDefaults.Active ? FrameDefaults.SharedEnvironmentMaterialReads : Defaults.SharedEnvironmentMaterialReads,
            "Reuse exact unchanged original-material reads within one camera invocation. Off repeats the original per-surface validation for A/B comparison. Every renderer and later camera retains live native state.");
        ScenarioTerrainCameraSourceLimitCount = file.Bind("Optimize", "ScenarioTerrainCameraSourceLimitCount",
            FrameDefaults.Active ? FrameDefaults.ScenarioTerrainCameraSourceLimitCount : Defaults.ScenarioTerrainCameraSourceLimitCount,
            new ConfigDescription("Maximum eligible 3D wall/pillar substitutes per eye: 0 is unlimited; lower positive limits reduce substitute preparation CPU work. Remaining surfaces keep their original 3D geometry and shading, which can increase rendering cost. No room is hidden. Works live; fresh Frame/Standalone profiles use 64.", new AcceptableValueRange<int>(0, 2048)));
        SharedUiWindowReads = file.Bind("Optimize", "SharedUiWindowReads",
            FrameDefaults.Active ? FrameDefaults.SharedUiWindowReads : Defaults.SharedUiWindowReads,
            "Share exact current native window-registry reads across converted panels and reuse immediate original-property reads in multiplayer mirrors. Off retains independent reads for A/B comparison. Content, visibility and intermediate animation remain immediate.");

    }
}
