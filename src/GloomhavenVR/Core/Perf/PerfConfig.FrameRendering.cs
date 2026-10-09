using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

// One mod binary for PC and Frame. The maintainer requested independent live controls
// for every quality compromise; pure work removal is universal (2026-10-08).
// Profile detection only seeds unsaved quality choices.
internal static partial class PerfConfig
{
    // INERT compatibility storage after the647 renderer rollback; no render consumer reads these.
    internal static ConfigEntry<bool> ScenarioRoomArchitecture = null!;
    internal static ConfigEntry<int> ScenarioRoomFloorDetailPercent = null!;
    internal static ConfigEntry<int> ScenarioRoomArchitectureDensityPercent = null!;
    internal static ConfigEntry<bool> ScenarioRoomFloorBatching = null!;
    internal static ConfigEntry<int> ScenarioRoomFloorCameraSourceLimitCount = null!;
    internal static ConfigEntry<bool> ScenarioExplicitEnvironmentInstancing = null!;
    internal static ConfigEntry<bool> ScenarioCheapWallShading = null!;
    internal static ConfigEntry<bool> ScenarioTerrainSubstitution = null!;
    internal static ConfigEntry<int> WorldMaterialQualityModeCount = null!;
    internal static ConfigEntry<int> WorldMaterialAmbientPercent = null!;
    internal static ConfigEntry<int> ScenarioTerrainDetailPercent = null!;
    internal static ConfigEntry<int> ScenarioDistantTerrainDetailPercent = null!;
    internal static ConfigEntry<float> ScenarioTerrainDistanceMeters = null!;
    internal static ConfigEntry<int> ScenarioTerrainCameraSourceLimitCount = null!;
    internal static ConfigEntry<int> WallVisibilityModeCount = null!;
    internal static ConfigEntry<int> WallAutoHideBelowFpsCount = null!;

    internal static bool EnvironmentMeshBankOn => true;
    internal static bool EnvironmentDrawInstancingOn => ScenarioExplicitEnvironmentInstancing?.Value ?? Defaults.ScenarioExplicitEnvironmentInstancing;
    internal static bool CheapWallShadingOn => ScenarioCheapWallShading?.Value ?? Defaults.ScenarioCheapWallShading;
    internal static bool TerrainSubstitutionOn => ScenarioTerrainSubstitution?.Value ?? Defaults.ScenarioTerrainSubstitution;
    internal static int WorldMaterialQualityMode => Mathf.Clamp(WorldMaterialQualityModeCount?.Value ?? Defaults.WorldMaterialQualityModeCount, 0, 2);
    internal static float WorldMaterialAmbientWeight => Mathf.Clamp(WorldMaterialAmbientPercent?.Value ?? Defaults.WorldMaterialAmbientPercent, 0, 100) * .01f;
    internal static int TerrainDetailPercent => Mathf.Clamp(ScenarioTerrainDetailPercent?.Value ?? Defaults.ScenarioTerrainDetailPercent, 0, 100);
    internal static int DistantTerrainDetailPercent => Mathf.Clamp(ScenarioDistantTerrainDetailPercent?.Value ?? Defaults.ScenarioDistantTerrainDetailPercent, 0, 100);
    internal static float TerrainDistanceMeters => Mathf.Clamp(ScenarioTerrainDistanceMeters?.Value ?? Defaults.ScenarioTerrainDistanceMeters, .1f, 10f);
    internal static int TerrainCameraSourceLimit => Mathf.Clamp(ScenarioTerrainCameraSourceLimitCount?.Value ?? Defaults.ScenarioTerrainCameraSourceLimitCount, 0, 2048);
    internal static int WallVisibilityMode => Mathf.Clamp(WallVisibilityModeCount?.Value ?? Defaults.WallVisibilityModeCount, 0, 2);
    internal static int WallAutoHideBelowFps => Mathf.Clamp(WallAutoHideBelowFpsCount?.Value ?? Defaults.WallAutoHideBelowFpsCount, 5, 30);
    // Work removal is universal, not a player quality choice. The getter preserves
    // existing internal/test-reference read paths without binding a config entry.
    internal static bool SharedEnvironmentMaterialReadsOn => true;
    internal static bool SharedUiWindowReadsOn => true;

    private static void BindFrameRendering(ConfigFile file)
    {
        // A visible quality trade is always editable. The maintainer explicitly requested
        // Auto for every fresh profile (2026-10-09), with instant hiding rather than a fade.
        // Unlike graphics presets, selecting a wall policy is independent of material detail.
        WallVisibilityModeCount = file.Bind("Optimize", "WallVisibilityModeCount",
            Defaults.WallVisibilityModeCount,
            new ConfigDescription("Wall visibility: 0 = regular see-through settings, 1 = hide all walls instantly, 2 = automatic. Automatic hides walls after sustained low frame rate in loaded gameplay and keeps them hidden until the scenario ends. Select regular to restore them sooner. Changing the threshold while automatic is selected restores walls and starts a fresh measurement. Hidden walls override look-dependent fading and the inside-play-area setting. Door frames and arches remain visible. Works live; automatic is the default on all platforms.", new AcceptableValueList<int>(0, 1, 2)));
        WallAutoHideBelowFpsCount = file.Bind("Optimize", "WallAutoHideBelowFpsCount",
            Defaults.WallAutoHideBelowFpsCount,
            new ConfigDescription("Frame-rate threshold for automatic wall hiding, in frames per second. A sustained drop below this value during loaded gameplay hides walls for the rest of the scenario. Loading pauses do not trigger it. Higher values favor performance sooner. Changing this value in automatic mode restores walls and starts a fresh measurement; an unchanged effective value keeps the current state. Used only in automatic wall mode; door frames and arches stay visible.", new AcceptableValueRange<int>(5, 30)));

        ScenarioRoomArchitecture = file.Bind("Optimize", "ScenarioRoomArchitecture",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomArchitecture : Defaults.ScenarioRoomArchitecture,
            "INERT — retained only to preserve your saved configuration. The broader room-architecture renderer from647 has been withdrawn after a Steam Frame performance regression. This value currently has no effect.");
        ScenarioRoomFloorDetailPercent = file.Bind("Optimize", "ScenarioRoomFloorDetailPercent",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomFloorDetailPercent : Defaults.ScenarioRoomFloorDetailPercent,
            new ConfigDescription("INERT — retained only to preserve your saved configuration. The broader room-architecture renderer from647 has been withdrawn after a Steam Frame performance regression. This value currently has no effect.", new AcceptableValueRange<int>(0, 100)));
        ScenarioRoomArchitectureDensityPercent = file.Bind("Optimize", "ScenarioRoomArchitectureDensityPercent",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomArchitectureDensityPercent : Defaults.ScenarioRoomArchitectureDensityPercent,
            new ConfigDescription("INERT — retained only to preserve your saved configuration. The broader room-architecture renderer from647 has been withdrawn after a Steam Frame performance regression. This value currently has no effect.", new AcceptableValueRange<int>(0, 100)));
        ScenarioRoomFloorBatching = file.Bind("Optimize", "ScenarioRoomFloorBatching",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomFloorBatching : Defaults.ScenarioRoomFloorBatching,
            "INERT — retained only to preserve your saved configuration. The broader room-architecture renderer from647 has been withdrawn after a Steam Frame performance regression. This value currently has no effect.");
        ScenarioRoomFloorCameraSourceLimitCount = file.Bind("Optimize", "ScenarioRoomFloorCameraSourceLimitCount",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomFloorCameraSourceLimitCount : Defaults.ScenarioRoomFloorCameraSourceLimitCount,
            new ConfigDescription("INERT — retained only to preserve your saved configuration. The broader room-architecture renderer from647 has been withdrawn after a Steam Frame performance regression. This value currently has no effect.", new AcceptableValueRange<int>(0, 8192)));

        ScenarioTerrainSubstitution = file.Bind("Optimize", "ScenarioTerrainSubstitution",
            FrameDefaults.Active ? FrameDefaults.ScenarioTerrainSubstitution : Defaults.ScenarioTerrainSubstitution,
            "Use prepared 3D substitutes for eligible walls and pillars. Off restores original geometry and stops substitute preparation, while the independent world-material mode still applies. This trades lower CPU preparation for potentially more GPU geometry work; compare in the same loaded view. Detail percentages and simpler-wall choices are retained for On. Works live on every platform; no room is hidden.");
        ScenarioExplicitEnvironmentInstancing = file.Bind("Optimize", "ScenarioExplicitEnvironmentInstancing",
            FrameDefaults.Active ? FrameDefaults.ScenarioExplicitEnvironmentInstancing : Defaults.ScenarioExplicitEnvironmentInstancing,
            "Submit eligible repeated static environment geometry in small camera-bound groups. Restores original rendering on unsupported or changed sources. Works live on PC and Frame.");
        ScenarioCheapWallShading = file.Bind("Optimize", "ScenarioCheapWallShading",
            FrameDefaults.Active ? FrameDefaults.ScenarioCheapWallShading : Defaults.ScenarioCheapWallShading,
            "Use cheaper original-textured wall materials while retaining continuous native wall dissolution. Floors keep original shading. Omits fine lighting and surface detail. Used when WorldMaterialQualityModeCount is 0. Works live; Off restores original materials.");
        WorldMaterialQualityModeCount = file.Bind("Optimize", "WorldMaterialQualityModeCount",
            FrameDefaults.Active ? FrameDefaults.WorldMaterialQualityModeCount : Defaults.WorldMaterialQualityModeCount,
            new ConfigDescription("Material shading for supported native static scenery throughout the game and DLCs: 0 = original, 1 = simple lighting, 2 = simple textured color. Lower-quality modes omit fine normal, gloss and surface lighting while retaining original 3D geometry, color textures and native animated visibility. Unsupported materials retain original rendering. At 0 the existing floor/wall shading switches apply; 1/2 take precedence over them. Works live on PC and Frame; fresh Frame/Standalone profiles use 2.", new AcceptableValueRange<int>(0, 2)));
        WorldMaterialAmbientPercent = file.Bind("Optimize", "WorldMaterialAmbientPercent",
            Defaults.WorldMaterialAmbientPercent,
            new ConfigDescription("Ambient lighting for world material mode 2: 100 uses original scene ambient light to retain atmosphere; 0 shows raw textured color. Values between blend the two. Fine surface lighting remains omitted. Works live on every platform; modes 0/1 are unaffected.", new AcceptableValueRange<int>(0, 100)));
        ScenarioTerrainDetailPercent = file.Bind("Optimize", "ScenarioTerrainDetailPercent",
            FrameDefaults.Active ? FrameDefaults.ScenarioTerrainDetailPercent : Defaults.ScenarioTerrainDetailPercent,
            new ConfigDescription("Eligible static wall and pillar mesh detail: 100 preserves original geometry, 0 uses the strongest available prepared 3D simplification. Floors, doors, actors, targeting and gameplay collision remain available. Works live on PC and Frame.", new AcceptableValueRange<int>(0, 100)));
        ScenarioDistantTerrainDetailPercent = file.Bind("Optimize", "ScenarioDistantTerrainDetailPercent",
            FrameDefaults.Active ? FrameDefaults.ScenarioDistantTerrainDetailPercent : Defaults.ScenarioDistantTerrainDetailPercent,
            new ConfigDescription("Additional eligible wall and pillar mesh detail cap beyond ScenarioTerrainDistanceMeters. 100 keeps selected near detail; lower values use coarser prepared 3D geometry. Revealed rooms and tactical contents remain represented.", new AcceptableValueRange<int>(0, 100)));
        ScenarioTerrainDistanceMeters = file.Bind("Optimize", "ScenarioTerrainDistanceMeters",
            FrameDefaults.Active ? FrameDefaults.ScenarioTerrainDistanceMeters : Defaults.ScenarioTerrainDistanceMeters,
            new ConfigDescription("Viewing distance in VR metres beyond which the distant wall and pillar detail cap applies. Native game and room visibility remain unchanged.", new AcceptableValueRange<float>(.1f, 10f)));
        ScenarioTerrainCameraSourceLimitCount = file.Bind("Optimize", "ScenarioTerrainCameraSourceLimitCount",
            FrameDefaults.Active ? FrameDefaults.ScenarioTerrainCameraSourceLimitCount : Defaults.ScenarioTerrainCameraSourceLimitCount,
            new ConfigDescription("Maximum eligible 3D wall/pillar substitutes per eye: 0 is unlimited; lower positive limits reduce substitute preparation CPU work. Remaining surfaces keep their original 3D geometry and shading, which can increase rendering cost. No room is hidden. Works live; fresh Frame/Standalone profiles use 64.", new AcceptableValueRange<int>(0, 2048)));

    }
}
