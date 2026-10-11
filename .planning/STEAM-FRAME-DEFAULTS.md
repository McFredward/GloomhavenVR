# PC and Steam Frame standalone defaults

Current policy: Build664, maintainer approval on 2026-10-10. Fresh PC mod settings
match **High-End PC**; fresh Steam Frame mod settings match **Standalone**.
The intermediate graphics profiles keep their own compromises. Every live
quality choice remains editable on either platform.

The Frame setup creates
`BepInEx/patchers/GloomhavenVR/frame-launch-opt-in.marker`. Only the
`GloomhavenVR` launch argument enables the mod in that installation; the original
Steam entry remains flat. An unset key uses `Core/Startup/FrameDefaults.cs` when
that marker exists, or `Defaults.*` on ordinary PC installations. BepInEx keeps
saved keys: updating or rerunning setup never applies a profile or overwrites
player choices. This document describes the shared Standalone preset; the
separate Quest implementation owns Quest platform activation.

## Rendering and presentation

| Configuration key | Fresh PC / High-End PC | Fresh Frame / Standalone |
|---|---:|---:|
| `[RenderQuality] MsaaLevel` | 8 | 0 |
| `EyeResolutionScale` | 1.00 | 0.80 |
| `ForceAnisotropic` | true | true |
| `ForceFullTextureResolution` | true | true |
| `ForceTextureStreamingOff` | true | false |
| `TextureStreamingBudgetMB` | 4096 | 900 |
| `PixelLightCount` | 0 | 0 |
| `[Sky] Style` | SwampNight | OffBlack |
| `[WorldUI] DesktopMirrorLeftEye` | true | false |
| `ImmersiveTownServices` | true | false |
| `WindowMaterialise` | true | false |
| `BarsOccluded` | true | true |

PC keeps the established readable-texture policy, including forced streaming
Off. Standalone allows native texture-memory management. Profile actions must
use the same policy instead of writing false for High-End PC. The budget is a
minimum only when native streaming is active, not reserved memory; a saved
false does not establish whether streaming is actually active.

OffBlack removes the surrounding scenario environment; it is not passthrough.
DesktopMirrorLeftEye=false produces a black desktop. BarsOccluded=true hides
the complete health bar behind walls; false shows the complete bar through them.

The setup's `vrpreferences.json` suggests a 3408-pixel per-eye SteamVR target,
separate from the mod's 0.80 scale. Existing SteamVR overrides remain editable
and are not replaced. See [STEAM-FRAME-VRPREFERENCES.md](STEAM-FRAME-VRPREFERENCES.md)
for setup, AppID limitations and headset verification.

## Scenario detail and work cadence

Zero density removes eligible optional detail, not every object or gameplay
surface. Original/simplified shading and the other quality compromises remain
reversible. Figure choices also apply to held figures.

| Configuration key | Fresh PC / High-End PC | Fresh Frame / Standalone |
|---|---:|---:|
| `[Optimize] ScenarioSceneryDensityPercent` | 100 | 0 |
| `ScenarioVegetationDensityPercent` | 100 | 0 |
| `ScenarioDecorationDensityPercent` | 100 | 0 |
| `ScenarioPlayerFigureDetailPercent` | 100 | 0 |
| `ScenarioEnemyFigureDetailPercent` | 100 | 0 |
| `ScenarioFigureEffectsDensityPercent` | 100 | 0 |
| `ScenarioEnvironmentEffectsDensityPercent` | 100 | 0 |
| `ScenarioFigureClothSimulation` | true | false |
| `SkinningBoneLimit` | 0 (native) | 2 |
| `FigureDistanceLod` | false | true |
| `OffscreenIdleAnimation` | false | true |
| `ReduceScenarioGenerationDetail` | false | true |
| `ScenarioSimpleEnvironmentShading` | false | true |
| `ScenarioCheapWallShading` | false | true |
| `WorldMaterialQualityModeCount` | 0 (original) | 2 (simple textured color) |
| `ScenarioStaticBatching` | false | true |
| `ScenarioStructuralBatching` | false | true |
| `ScenarioStructuralInstancing` | false | false |
| `ScenarioExplicitEnvironmentInstancing` | false | true |
| `ScenarioTerrainSubstitution` | true | true |
| `ScenarioTerrainPillarDistanceLod` | true | true |
| `ScenarioTerrainDetailPercent` | 100 | 0 |
| `ScenarioDistantTerrainDetailPercent` | 100 | 0 |
| `ScenarioTerrainCameraSourceLimitCount` | 0 (unlimited) | 64 |
| `ScenarioTerrainDistanceMeters` | 0.75 | 0.75 |
| `UiMaintenanceIntervalSeconds` | 0 | 0.15 |
| `ActorBarPoseCheckIntervalSeconds` | 0 | 0.10 |
| `InitiativeDepthEvalInterval` | 0 | 0.10 |
| `WallAutoHideBelowFpsCount` | 10 | 10 |
| `WallFadeEvalInterval` | 0 | 0 |
| `[WallFade] RescanIntervalSeconds` | 2.0 | 4.0 |
| `EvalIntervalSeconds` | 0 (effective 0.05) | 0.25 |

Wall rescan cadence controls inventory refresh frequency, not the duration of a
single rescan. Evaluation cadence controls wall visibility decisions; 0.25 s
trades some reaction speed for less work. The legacy Optimize interval stays
zero so an earlier manual override cannot take precedence over a profile's
intended cadence. The shared auto-hide threshold is now 10 FPS, replacing 15;
do not copy the submitted Frame snapshot's 7 FPS setting.

## Shared interaction defaults and calibration

Right-stick vertical movement, Follow and board transparency while occluding
the view stay On. Wrist attachment and combat log stay Off. Hand style, board
style, mask and ordinary interaction defaults stay shared. Logs use Info and
cheats are hidden/Off; the maintainer's Debug/cheat settings are test preferences.

The approved shared wrist-local position is
`[Cards] WristBoardOffsetMeters = (-0.166, 0.010, -0.036)` metres. Rotation stays
`(0, 0, 90)` degrees and wrist attachment stays Off. Graphics buttons do not
change attachment, saved wrist calibration, board pose/size, player scale or
combat-log placement. Other submitted positions remain personal calibration.

## Native quality and explicit profile actions

New `GlobalData` starts with `QualityLevel = "Fastest"` (`GH.Runtime.dll`,
`GlobalData()`). `GraphicSettings.SetupQualityLevel()` loads saved native
quality, including Steam Cloud choices. Startup does not overwrite that quality.
PC/Standalone alignment here describes mod controls; native quality changes
when a player explicitly chooses a profile through the original game callback.

| Graphics button | Native quality selected |
|---|---|
| Standalone | Fastest |
| Performance PC | Simple |
| Balanced PC | Good |
| High-End PC | Fantastic |

Each action writes its settings once, saves them, and leaves later manual
changes authoritative. It also resets environment style, wall evaluation/rescan
cadence, shared auto-hide threshold, pillar distance LOD and the
legacy wall interval. The corresponding fresh values and explicit profile
values must agree without changing personal control calibration. The shared
bar-occlusion value is a startup default; profiles preserve an explicit player
choice for health-bar visibility.

## Always-on optimizations and historical entries

Unused-camera suspension and audited pure work caches run everywhere. Old
SuspendUnusedCameras, SharedWallReadCache, LightStabiliserWorkCache, cache/read
flags and ScenarioEnvironmentMeshBank values cannot restore removed work paths.
Retired VisibleIdle keys have no consumer. Do not infer camera work from these
persisted entries.

The room-architecture/room-floor family, remote-content cadence, QualityPreset
and immersive-NPC low-poly controls are inert compatibility state. Broken NPC
low-poly meshes are removed; Standalone defaults to original windows instead.
Migration flags and saved apparent board widths are also state, not quality
choices to copy into fresh defaults.

Historical compromise evidence lives in
[STEAM-FRAME-SCENERY-600.md](STEAM-FRAME-SCENERY-600.md),
[STEAM-FRAME-SCENERY-601.md](STEAM-FRAME-SCENERY-601.md),
[STEAM-FRAME-SCENERY-603.md](STEAM-FRAME-SCENERY-603.md) and
[STEAM-FRAME-SCENERY-604.md](STEAM-FRAME-SCENERY-604.md). Their old coupled grass,
mirror and generation descriptions do not supersede current independent
controls or this policy. Config snapshots establish saved preferences; this
change claims no new measured FPS gain or headset validation.
