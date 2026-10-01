# Steam Frame standalone defaults

The Frame setup creates `BepInEx/patchers/GloomhavenVR/frame-launch-opt-in.marker`.
Only the `GloomhavenVR` launch argument enables the mod in this installation;
the original Steam entry remains flat. When the Frame VR plugin binds an unset
config key, it uses the separate profile in `Core/Startup/FrameDefaults.cs`.
BepInEx loads any persisted key instead of that fallback, so rerunning setup or
updating the mod does not overwrite the player's choices. Windows PC installs
have no marker and keep the existing `Defaults.*` values.

| Key | Fresh Frame | Fresh PC | Build 592 basis |
|---|---:|---:|---|
| `[RenderQuality] MsaaLevel` | 0 | 4 | 0× in the headset |
| `EyeResolutionScale` | 1.00 | 1.00 | 3408×3408 eye target came from SteamVR, with no additional mod scale |
| `ForceAnisotropic` | true | true | forced globally |
| `ForceFullTextureResolution` | true | true | texture mip-drop limit 0 |
| `ForceTextureStreamingOff` | false | true | native Fastest streaming state remained off without a mod force |
| `TextureStreamingBudgetMB` | 900 | 4096 | native Fantastic budget; inactive under the fresh Fastest preset |
| `PixelLightCount` | 0 | 0 | 0 live pixel lights |
| `[WallFade] RescanIntervalSeconds` | 4.0 | 2.0 | 4.0 s live cadence |

Build 600 adds a maintainer-authorized scenery compromise and makes Frame a
defaults profile throughout: no platform marker may override these live choices
or hide their controls. All entries below can be changed on PC and Frame; saved
values take precedence. The two work caches are lossless optimizations on both
platforms, with off switches for comparison and restoration of the old work path.

| Key | Fresh Frame | Fresh PC | Control and behavior |
|---|---:|---:|---|
| `[Optimize] ScenarioSceneryDensityPercent` | 25 | 100 | VR Options → Graphics → Scenario grass (%); 100 restores original decorative grass rendering |
| `[Optimize] SharedWallReadCache` | true | true | Advanced options/config; false restores repeated material and ancestor queries |
| `[Optimize] LightStabiliserWorkCache` | true | true | Advanced options/config; false restores original light lookup/write cadence |
| `[WorldUI] DesktopMirrorLeftEye` | true | true | Existing live desktop-mirror toggle; a saved false is respected on Frame too |

The Build 594 forced Frame mirror and hidden toggle are superseded by this
2026-10-01 instruction. See [STEAM-FRAME-SCENERY-600.md](STEAM-FRAME-SCENERY-600.md)
for the scenery exclusions, evidence limits and next headset comparison.

The game already creates a fresh `GlobalData` with `QualityLevel = "Fastest"`
(`GH.Runtime.dll`, `GlobalData()`), its lowest native graphics preset. A saved
quality level or custom graphics profile is loaded by
`GraphicSettings.SetupQualityLevel()`; the mod must not reset it on each launch.
That includes an existing Steam Cloud profile: this Frame default applies only
to a genuinely new game profile, not to an existing save's graphics choices.
`ForceFullTextureResolution` and forced anisotropy deliberately retain the
tested VR picture even when the native preset is Fastest. The 3408×3408 SteamVR
per-eye target was set manually in Build 592; the Frame setup now suggests the
same value through the documented game-root `vrpreferences.json` mechanism.
That is a SteamVR per-application default, separate from the mod's 1.00 scale.
An existing SteamVR override remains editable and is not replaced. See
[STEAM-FRAME-VRPREFERENCES.md](STEAM-FRAME-VRPREFERENCES.md) for the installer,
AppID limitation and headset verification. Neither this profile nor the native
preset resolves the measured CPU hitches around NPC visits and the hand fan.
