using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

// One mod binary for PC and Frame. The maintainer requested independent live controls
// for every quality compromise; pure work removal is universal (2026-10-08).
// Profile detection only seeds unsaved quality choices.
internal static partial class PerfConfig
{
    internal static ConfigEntry<bool> ScenarioExplicitEnvironmentInstancing = null!;
    internal static ConfigEntry<bool> ScenarioCheapWallShading = null!;
    internal static ConfigEntry<bool> ScenarioRoomArchitecture = null!;
    internal static ConfigEntry<int> ScenarioRoomFloorDetailPercent = null!;
    internal static ConfigEntry<int> ScenarioRoomArchitectureDensityPercent = null!;
    internal static ConfigEntry<bool> ScenarioRoomFloorBatching = null!;
    internal static ConfigEntry<int> ScenarioRoomFloorCameraSourceLimitCount = null!;
    internal static ConfigEntry<bool> ScenarioTerrainSubstitution = null!;
    internal static ConfigEntry<int> WorldMaterialQualityModeCount = null!;
    internal static ConfigEntry<int> WorldMaterialAmbientPercent = null!;
    internal static ConfigEntry<int> ScenarioTerrainDetailPercent = null!;
    internal static ConfigEntry<int> ScenarioDistantTerrainDetailPercent = null!;
    internal static ConfigEntry<float> ScenarioTerrainDistanceMeters = null!;
    internal static ConfigEntry<int> ScenarioTerrainCameraSourceLimitCount = null!;

    internal static bool EnvironmentMeshBankOn => true;
    internal static bool EnvironmentDrawInstancingOn => ScenarioExplicitEnvironmentInstancing?.Value ?? Defaults.ScenarioExplicitEnvironmentInstancing;
    internal static bool CheapWallShadingOn => ScenarioCheapWallShading?.Value ?? Defaults.ScenarioCheapWallShading;
    internal static bool RoomArchitectureEnabled => ScenarioRoomArchitecture?.Value ?? Defaults.ScenarioRoomArchitecture;
    internal static int RoomFloorDetailPercent => Mathf.Clamp(ScenarioRoomFloorDetailPercent?.Value ?? Defaults.ScenarioRoomFloorDetailPercent, 0, 100);
    internal static int RoomArchitectureDensityPercent => RoomArchitectureEnabled
        ? Mathf.Clamp(ScenarioRoomArchitectureDensityPercent?.Value ?? Defaults.ScenarioRoomArchitectureDensityPercent, 0, 100) : 100;
    internal static int RoomFloorCameraSourceLimit => Mathf.Clamp(ScenarioRoomFloorCameraSourceLimitCount?.Value
        ?? Defaults.ScenarioRoomFloorCameraSourceLimitCount, 0, 8192);
    internal static bool RoomFloorBatchingOn => RoomArchitectureEnabled
        && (ScenarioRoomFloorBatching?.Value ?? Defaults.ScenarioRoomFloorBatching);
    internal static bool TerrainSubstitutionOn => ScenarioTerrainSubstitution?.Value ?? Defaults.ScenarioTerrainSubstitution;
    internal static int WorldMaterialQualityMode => Mathf.Clamp(WorldMaterialQualityModeCount?.Value ?? Defaults.WorldMaterialQualityModeCount, 0, 2);
    internal static float WorldMaterialAmbientWeight => Mathf.Clamp(WorldMaterialAmbientPercent?.Value ?? Defaults.WorldMaterialAmbientPercent, 0, 100) * .01f;
    internal static int TerrainDetailPercent => Mathf.Clamp(ScenarioTerrainDetailPercent?.Value ?? Defaults.ScenarioTerrainDetailPercent, 0, 100);
    internal static int DistantTerrainDetailPercent => Mathf.Clamp(ScenarioDistantTerrainDetailPercent?.Value ?? Defaults.ScenarioDistantTerrainDetailPercent, 0, 100);
    internal static float TerrainDistanceMeters => Mathf.Clamp(ScenarioTerrainDistanceMeters?.Value ?? Defaults.ScenarioTerrainDistanceMeters, .1f, 10f);
    internal static int TerrainCameraSourceLimit => Mathf.Clamp(ScenarioTerrainCameraSourceLimitCount?.Value ?? Defaults.ScenarioTerrainCameraSourceLimitCount, 0, 2048);
    // Work removal is universal, not a player quality choice. The getter preserves
    // existing internal/test-reference read paths without binding a config entry.
    internal static bool SharedEnvironmentMaterialReadsOn => true;
    internal static bool SharedUiWindowReadsOn => true;

    private static void BindFrameRendering(ConfigFile file)
    {
        ScenarioRoomArchitecture = file.Bind("Optimize", "ScenarioRoomArchitecture",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomArchitecture : Defaults.ScenarioRoomArchitecture,
            "Use broader source-verified 3D floor, wall and pillar simplification throughout the game and DLCs. Original room boundaries, elevation, doors, figures, colliders and native wall fading remain. Enables the independent floor-detail, optional architectural-detail and floor-group controls. Off restores this room mode while retaining their values and the older wall-detail choice. Works live on every platform.");
        ScenarioRoomFloorDetailPercent = file.Bind("Optimize", "ScenarioRoomFloorDetailPercent",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomFloorDetailPercent : Defaults.ScenarioRoomFloorDetailPercent,
            new ConfigDescription("Room floor geometry: 100 keeps original detail; lower values use coarser prepared 3D surfaces. Fine cracks, bumps and relief are reduced; native height, open boundaries, room footprint, color textures and gameplay collision remain. Requires ScenarioRoomArchitecture. Works live with continuous geometry transitions.", new AcceptableValueRange<int>(0, 100)));
        ScenarioRoomArchitectureDensityPercent = file.Bind("Optimize", "ScenarioRoomArchitectureDensityPercent",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomArchitectureDensityPercent : Defaults.ScenarioRoomArchitectureDensityPercent,
            new ConfigDescription("Optional noninteractive architectural dressing: 100 retains all; 0 omits admitted complete decorative units. Fewer ornaments and mounted decorations remain visible. Required room boundaries, floors, doorways, lights, figures and interactive or held props remain. Requires ScenarioRoomArchitecture; independent of grass, vegetation and loose decoration. Works live.", new AcceptableValueRange<int>(0, 100)));
        ScenarioRoomFloorBatching = file.Bind("Optimize", "ScenarioRoomFloorBatching",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomFloorBatching : Defaults.ScenarioRoomFloorBatching,
            "Combine verified settled room floors into private local render groups. Uses the selected 3D floor detail and never changes native meshes or Unity batch metadata. Changed visibility, materials, property blocks or unsupported native consumers restore original rendering. Group bounds can change rendering and per-object lighting selection. Requires ScenarioRoomArchitecture. Works live; Off retains individual floor rendering.");
        ScenarioRoomFloorCameraSourceLimitCount = file.Bind("Optimize", "ScenarioRoomFloorCameraSourceLimitCount",
            FrameDefaults.Active ? FrameDefaults.ScenarioRoomFloorCameraSourceLimitCount : Defaults.ScenarioRoomFloorCameraSourceLimitCount,
            new ConfigDescription("Maximum individual simplified room-floor surfaces per eye, outside prepared floor groups: 0 is unlimited. Lower limits reduce proxy maintenance CPU work but leave overflow floors at native geometric detail. Independent of the wall/pillar source limit; floors never consume its budget. Requires ScenarioRoomArchitecture. Works live.", new AcceptableValueRange<int>(0, 8192)));
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
