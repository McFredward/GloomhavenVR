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

The game already creates a fresh `GlobalData` with `QualityLevel = "Fastest"`
(`GH.Runtime.dll`, `GlobalData()`), its lowest native graphics preset. A saved
quality level or custom graphics profile is loaded by
`GraphicSettings.SetupQualityLevel()`; the mod must not reset it on each launch.
That includes an existing Steam Cloud profile: this Frame default applies only
to a genuinely new game profile, not to an existing save's graphics choices.
`ForceFullTextureResolution` and forced anisotropy deliberately retain the
tested VR picture even when the native preset is Fastest. The 3408×3408 SteamVR
per-eye target is an external runtime choice and is not written to the game or
mod config. Neither this profile nor the native preset resolves the measured
CPU hitches around NPC visits and the hand fan.
