# 1.1.0 compatibility and installation review

Review date: 2026-10-11. Compared published `v1.0.8`
(`4640fff2f0dedf5e4c8d3ded7d21684ef4b5ed60`) with current `dev`
(`ce637a1dc3fb609204701c701498a20cd7bdc0ae`, Build 665).
This is the review phase: no production code, build number, asset, or wire change
is made here. The parallel Build 666 NPC ring/hover/summon work is excluded.
Quest standalone implementation remains owned by the separate agent.

## Confirmed repair required: Frame self-update loses VR launch opt-in

**Trigger:** a correctly configured Steam Frame user starts `GloomhavenVR`, then
accepts an in-game update and automatic restart.

**Result:** the update applier relaunches original `Gloomhaven` without the
required VR opt-in. Files can update successfully while the restart opens flat.

Source chain:

- `scripts/install-steam-frame.sh:293` starts
  `steam -applaunch 780290 --gloomhavenvr`. Keeping the original AppID is
  deliberate: saves, Proton prefix and Steamworks stay attached to the base game.
- `scripts/steam-frame-config.py:154` removes `--gloomhavenvr` from the base
  game's persisted launch options, so that the original library entry stays flat.
- `Core/SelfUpdate/SelfUpdateInstaller.cs:374-383` reads `SteamGameId` and process
  arguments and passes both to the script builder.
- `Core/SelfUpdate/SelfUpdateApplyScript.cs:207-208` takes the Steam branch and
  emits only `steam://rungameid/{steamGameId}`, discarding the process arguments.
- `GloomhavenVR.Preload/FrameLaunchOptIn.cs:22-34` rejects a marked installation
  unless an exact `--gloomhavenvr` argument is present.
- `GloomhavenVR.Preload/Patcher.cs:505-513` already avoids its own equivalent
  graphics-job relaunch on Frame for this same reason. The updater was not
  connected to that existing launch distinction.

A small isolated managed proof compiles the **unchanged** production script
builder and Frame gate. It verifies that the original launch is allowed, the
generated update relaunch is `start "" "steam://rungameid/780290"`, and a
marked base launch without the opt-in is rejected. Evidence:
`.planning/debug/release110-compat/frame-update-opt-in-proof.log` and
`source-hashes.json`. This proves the lost argument; it is not a test of Wine's
detached batch process or actual Steam Frame restart.

### Repair direction

Use the already installed, verified VR shortcut as the restart target. Do not
change the base game's launch options, create a new Proton prefix, or leave a
temporary global opt-in token that an unrelated original-entry launch could use.

`steam-frame-config.py:350-374` already reads the actual shortcut AppID and keeps
existing Steam-assigned IDs. Its tests explicitly cover a retained custom ID and
collision avoidance. A guessed CRC derived from the launch path is therefore
insufficient. Persist bounded, validated restart metadata after exact shortcut
readback, and preserve it when replacing the mod tree. The host-side launch
wrapper can refresh this metadata before its existing `steam -applaunch` call,
covering previously configured installations without C# traversing SteamOS
paths through Wine. A missing or invalid restart target must not silently choose
the original flat launch.

The shortcut's 32-bit artwork AppID is distinct from its 64-bit launch GameID.
The reported conversion is `(stored AppID << 32) | 0x02000000`; it must use the
actual retained stored ID. [Valve's Linux tracker discussion #9463](https://github.com/ValveSoftware/steam-for-linux/issues/9463)
describes this distinction and why predicting the ID from an executable path
stopped working. This is a report in Valve's tracker, not an official API warranty.
The installed wrapper and final GameID still need a real Frame restart check.

Passing encoded process arguments through `steam://run` alone is not an adequate
proof of delivery: [Valve's Linux tracker #12264](https://github.com/ValveSoftware/steam-for-linux/issues/12264)
remains open and reports that this form ignores launch arguments. The Valve
Developer Community protocol and command-line pages returned HTTP 403 during
this review. Preserve the known working wrapper instead of claiming those URLs
establish reliable Frame argument delivery.

## Checked paths without a new confirmed defect

| Area | Source review and outcome |
| --- | --- |
| Updating directly from 1.0.8 | The released verifier accepts arbitrary files beneath `BepInEx/` and the same two root guides. New Frame helpers, notices, environment assets and all 66 indexed figure parts stay under that subtree. No bridge release or old updater grammar change is necessary. |
| Larger package | Downloads use `DownloadHandlerFile`; published/downloaded/extracted sizes use `long`. Progress divides in `double`; stalled transfers, rather than an overall large-download timeout, abort. Current prebuilt assets total approximately 535 MiB, below the existing 4 GiB parser boundary and classic ZIP offsets. This is a source/layout assessment, not a measured real large download. |
| Split figure banks | Current index has 1,594 unique derivatives in 66 parts. Runtime reads only indexed parts beside the plugin; a missing/invalid bank retains original meshes. Historical leftovers from a non-mirroring update are not glob-loaded as active banks. Both packagers stage the complete validated index and parts. |
| Frame versus original flat launch | The marker does not ship in the release archive. Its existence and exact opt-in check are shared between the preloader and plugin. Setup keeps the original Steam entry flat and prepares graphics jobs idempotently before VR startup. The confirmed updater gap above is the exception. |
| Fresh defaults and saved configuration | `FrameDefaults.Active` selects initial values from the Frame marker; ordinary BepInEx bindings preserve saved keys. High-End PC and Standalone are explicit one-time profile actions. `GraphicsProfiles.cs:22-39,100-120` checks the native quality callback first and restores persistence flags in `finally`; later individual edits remain authoritative. Personal wrist calibration is not rewritten by these profiles. |
| 2D/3D and immersive off | `TownServicePresentation.cs:145-160` cancels immersive samples before restoring the existing native controller when immersive mode is disabled or coordination is unavailable. It does not gratuitously close/reopen the controller. Existing dedicated native-setting and map-switch receipts remain the prior evidence; no new hardware claim is made here. |
| Unmodded host | `TownServiceGrantSync.cs:45-55,91-100` requires a known compatible coordinator and forbids commits in flat network mode. With an unmodded host, the original native window is the deliberate fallback; no invented local exclusive grant or altered native replay is introduced. This preserves playability but cannot give an unmodded renderer NPC presentation. |
| Scene generation compromise | `ScenarioGenerationDetail.cs:71-87,101-117` freezes the choice per scenario, replaces only scoped map/wall parameter reads with a transient native-settings copy, and retains native exception continuation and unrelated settings. Live changes do not rebuild gameplay geometry mid-turn. |
| Scenery, figure and animation reductions | Owners retain original renderer/mesh/LOD/cloth references and restore only owned visual changes. `ScenarioSceneryBudget`, `ScenarioFigureDetailBudget` and `ScenarioIdleAnimationBudget` do not replace authoritative actors or native game actions. Figure mesh derivatives leave collider/bone/material identity intact; action prefixes restore offscreen animation culling before native dispatch. |
| Camera work removal | `NativeCameraRenderBudget.cs:78-105` excludes the headset and native video render cameras, preserves native projection readers, and restores a camera when it stops being a discarded sink. `FlatScreen.3.Desktop.cs:197-228,398-432` retains independently consumed render textures and restores the sink before native capture. The mirror choice changes only spectator pixels. |
| Release publication | `release-upload.py` uploads a verified single ZIP to an unpublished draft and confirms its immutable main/tag provenance before publication. Split bundles are files inside that one asset. No release, tag rewrite or main push is part of this review. |

## Focused receipts and limits

- `python3 scripts/check-figure-mesh-bank.py prebuilt`: **1,594 derivatives,
  66 valid game-compatible parts**.
- `bash scripts/test-install-steam-frame.sh`: **passed** desktop launch, exact
  Steam entry, graphics-job boot setup and repeat installation on a synthetic
  account. Its scratch directory is removed by the existing test.
- Current managed archive regression fixture, extended privately with all 66
  actual part filenames: **21 assertions passed**.
- The same archive fixture against **unmodified released 1.0.8 verifier source**:
  **21 assertions passed**. The isolated net8 wrapper suppresses the two historical
  reflection-nullability warnings, CS8600/CS8603; no old runtime source is repaired
  for this proof. Archive payloads are small stubs, so this proves layout acceptance
  and safe extraction, not full asset content or a 535 MiB download.
- `bash scripts/test-frame-archive.sh`: **skipped**, explicitly reporting that
  `pwsh` is unavailable. No new Windows PowerShell archive pass is claimed.
- Source-backed Frame relaunch defect proof: **reproduced** as described above.

No complete 209-suite gate, Unity regeneration, actual release ZIP build,
headset validation, or multiplayer hardware run was repeated for this read-only
lane. Historical focused and full-gate receipts remain evidence for unchanged
production; they are not newly executed tests. This lane confirms one repair
required before release and does not claim that all untested game scenarios or
platform process-launch behavior have been exhaustively proven.
