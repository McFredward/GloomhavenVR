# Phase 1 Windows validation — stereo + head tracking (R1)

Code-complete checklist for validating the XR bootstrap on a real Windows machine
with a headset. Target result (ROADMAP Phase 1 "done when"): *scenario visible as a
stereo diorama on Quest 3 (Link + SteamVR tested), head-tracked, stable frametime,
game still fully mouse-playable in parallel.*

## 0. Provenance of the shipped binaries

| Piece | Produced by | Notes |
|---|---|---|
| `GloomhavenVR.dll`, `GloomhavenVR.Preload.dll` | `dotnet build GloomhavenVR.sln -c Release` | needs `Directory.Build.props.user` → game `Managed/` |
| `libs/Natives/{UnityOpenXR,openxr_loader}.dll` | `scripts/fetch-natives.sh` | prebuilt binaries from `com.unity.xr.openxr@1.10.0` (needle-mirror), SHA256-pinned in `libs/Natives/README.md` |
| `libs/RuntimeDeps/Unity.XR.{Management,CoreUtils,OpenXR}.dll` | `scripts/build-runtimedeps.sh` | **provisional**: compiled from needle-mirror source outside Unity (see `tools/RuntimeDepsBuild/README.md`). If anything XR-side misbehaves inexplicably, produce the editor-harvested set (`unity/HARVESTING.md`) and drop it into `libs/RuntimeDeps/` — same file names — before digging deeper. |

## 1. Install

1. Gloomhaven v1.1.8307.0 + **BepInEx 5.4.23.5 x64** installed, game boots with
   `BepInEx/LogOutput.log` created. Recommended `BepInEx.cfg`: `[Logging.Console] Enabled = true`.
2. On the dev machine: `scripts/fetch-natives.sh && scripts/build-runtimedeps.sh && dotnet build GloomhavenVR.sln -c Release`.
3. `scripts/deploy.ps1 -GamePath "C:\...\Gloomhaven"` → resulting game-dir layout:

   ```
   BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll
   BepInEx/plugins/GloomhavenVR/RuntimeDeps/Unity.XR.Management.dll
   BepInEx/plugins/GloomhavenVR/RuntimeDeps/Unity.XR.CoreUtils.dll
   BepInEx/plugins/GloomhavenVR/RuntimeDeps/Unity.XR.OpenXR.dll
   BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll
   BepInEx/patchers/GloomhavenVR/Natives/UnityOpenXR.dll
   BepInEx/patchers/GloomhavenVR/Natives/openxr_loader.dll
   ```

   The preloader then self-installs at every boot (idempotent, hash-compared):

   ```
   Gloomhaven_Data/Plugins/x86_64/UnityOpenXR.dll
   Gloomhaven_Data/Plugins/x86_64/openxr_loader.dll
   Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json   (version 1.10.0)
   BepInEx/patchers/GloomhavenVR/install-state.json                          (diagnostic marker)
   ```

4. Headset ready: Quest 3 via **Quest Link** (Meta OpenXR runtime) for the first run;
   repeat via **Steam Link** (SteamVR runtime) for the second.

## 2. Expected log lines per stage (`BepInEx/LogOutput.log`)

| Stage | Expected line (prefix `[Info :GloomhavenVR.Preload]` / `[Info :GloomhavenVR]`) |
|---|---|
| Preloader ran | `Installed native: ...\Gloomhaven_Data\Plugins\x86_64\UnityOpenXR.dll` (first boot) or `Native up to date` (debug level); `OpenXR runtime assets ready (package 1.10.0).` |
| RuntimeDeps loaded | `[Core] Loaded 3 runtime dependencies: Unity.XR.CoreUtils, Unity.XR.Management, Unity.XR.OpenXR` |
| Pre-flight | `[Core] Pre-flight OK: OpenXR Display/Input subsystem descriptors are registered.` |
| Runtime enumeration | `[Core] OpenXR runtime candidates (N): Oculus (C:\...\oculus_openxr_64.json) \| ...` |
| Init success | `[Core] OpenXR session up — runtime: <name> <version>, render mode: MultiPass.` |
| Plugin summary | `v0.1.0 loaded — 7 modules initialized, VR RUNNING on '<runtime>'.` |
| Rig armed | `[Rig] Rig driver installed — waiting for a scenario camera.` |
| Compat | `[Compat] Kill-switches armed for: PostProcessLayer, PostProcessVolume, VolumetricFog.` |
| In scenario | `[Rig] VR rig built at focus (...), world scale <s> ...` then `[Rig] Recentered — ...` |

With `[General] Enabled = false`: only two lines — preloader skip notice + plugin
"Disabled via config" — and zero behavioral change.

## 3. Validation checklist

- [ ] Headset shows the game rendering stereo (both eyes, correct separation) once XR is up.
- [ ] Load any scenario: board appears as a **diorama/table** (world scale sane; if not, set `[Rig] WorldScale` in `dev.gloomhavenvr.cfg`, e.g. 10–20).
- [ ] Head tracking: 6-DoF, no drift, no double-image judder (reprojection ok).
- [ ] Desktop mirror still shows the game; **mouse play still works** (click hexes, cards, end turn).
- [ ] 2D UI (screen-space canvases) still visible & functional on the desktop mirror (VR UI is P3c — do NOT expect UI in the headset).
- [ ] No per-frame error spam in the log (occasional FOV-write warnings from scripted camera moves are a known P1 leftover).
- [ ] Frametime stable in headset (Quest 3 / Link: 72–90 fps against a mostly static board).
- [ ] Quit + relaunch: preloader logs `up to date` paths (idempotence).
- [ ] `Enabled = false` run: 100% vanilla behavior.
- [ ] Second runtime: repeat headset checks via Steam Link/SteamVR (runtime failover may kick in — check which runtime the log reports).
- [ ] (Optional) `RuntimeOverride` pointed at `steamxr_win64.json` forces SteamVR.

## 4. Failure triage

| Symptom (log evidence) | Diagnosis | Fix |
|---|---|---|
| Pre-flight FAILED: **no descriptors** (`display: False, input: False`) | Engine didn't pick up manifest/natives at boot | Check preloader lines earlier in the log; verify the three files under `Gloomhaven_Data/` (§1.3); verify `BepInEx/patchers/GloomhavenVR/Natives/*.dll` exist (fetch-natives + deploy); check `install-state.json` hashes vs `libs/Natives/README.md` |
| Descriptors OK but **no display subsystem** after all candidates (`All OpenXR runtime candidates failed`) | OpenXR loader can't reach a runtime | Headset connected & runtime running? Check `[Core] OpenXR runtime candidates` list — empty registry means no runtime installed; try `RuntimeOverride` with an explicit runtime JSON; check the OpenXR diagnostics report (debug log level) for `xrCreateInstance`/`xrGetSystem` errors |
| Display subsystem up but **black screen in headset** | Graphics API mismatch — desktop OpenXR needs **D3D11** | Add `-force-d3d11` to Steam launch options; verify the game didn't launch under `-force-glcore`/D3D12 |
| Stereo up but **world not table-scaled** / camera inside geometry | WorldScale heuristic off (s_TileSize not initialized at rig build) | Set `[Rig] WorldScale` explicitly; re-enter scenario |
| Stereo up but **camera fights/jumps** with game camera moves | A camera writer not covered by the LateUpdate skip (SmartFocus/timeline) | Expected P1 edge; note the trigger (cutscene? door reveal?) for the Phase-4 comfort pass |
| Broken/one-eye post effects | A PPv2/fog effect slipped through | Ensure `[Compat] DisablePostProcessing`/`DisableVolumetricFog` are true; add offender type name to `DisableComponents` |
| `DllNotFoundException: UnityOpenXR` in diagnostics | Natives missing from `Gloomhaven_Data/Plugins/x86_64` | Same as first row — preloader install failed |
| Crash/weird XR behavior with provisional RuntimeDeps | Provisional compile drift | Swap in the editor-harvested RuntimeDeps set (`unity/HARVESTING.md`) and retest |

## 5. Known P1 limitations (by design)

- No controllers/hands yet (Phase 2); no VR UI (Phase 3c); no world grab (Phase 4).
- Zoom (FOV-based) is meaningless in VR; scripted camera moves are frozen while VR runs.
- MultiPass rendering only (SPI is a later opt-in experiment).
- InputSystem stays at the game's 1.3.0; RuntimeDeps compiled accordingly
  (`USE_INPUT_SYSTEM_POSE_CONTROL` off). The InputSystem 1.7 upgrade decision lands in Phase 2.
