# Phase 1 Windows validation — stereo + head tracking (R1)

> **Audited 2026-09-08 at ModBuild 483** (base `49ceab21`). Every `[Section] Key`, log
> marker, type, method and file path named below was grepped against the tree. Config
> keys: no doc here names a live key that has been removed, and every key described as
> DELETED really is gone. What an audit of names cannot establish is that each step's
> expected BEHAVIOUR is still current — where a step was found asserting something the
> code now forbids, it says so in place.

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
3. `scripts/install.ps1 -GamePath "C:\...\Gloomhaven"` → resulting game-dir layout:

   ```
   BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll
   BepInEx/plugins/GloomhavenVR/RuntimeDeps/Unity.XR.Management.dll
   BepInEx/plugins/GloomhavenVR/RuntimeDeps/Unity.XR.CoreUtils.dll
   BepInEx/plugins/GloomhavenVR/RuntimeDeps/Unity.XR.OpenXR.dll
   BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll
   BepInEx/patchers/GloomhavenVR/Natives/UnityOpenXR.dll
   BepInEx/patchers/GloomhavenVR/Natives/openxr_loader.dll
   ```

   The preloader then self-installs at every boot (idempotent, hash-compared).
   NOTE: the game's data folder is **`GH_Data`** (the executable is `GH.exe`), not
   `Gloomhaven_Data` — the preloader derives it from BepInEx's ManagedPath:

   ```
   GH_Data/Plugins/x86_64/UnityOpenXR.dll
   GH_Data/Plugins/x86_64/openxr_loader.dll
   GH_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json   (version 1.10.0)
   BepInEx/patchers/GloomhavenVR/install-state.json                   (diagnostic marker)
   ```

4. Headset ready — connect **before** launching the game. Quest 3 paths and the OpenXR
   runtime each uses: **Virtual Desktop → VDXR** (primary test rig), **Quest Link /
   Air Link → Meta runtime**, **Steam Link → SteamVR**. The mod tries the machine's
   default runtime first, prefers VDXR while the Virtual Desktop Streamer is running,
   and only falls back to SteamVR last ([Core] RuntimePriority = auto).

## 2. Expected log lines per stage (`BepInEx/LogOutput.log`)

| Stage | Expected line (prefix `[Info :GloomhavenVR.Preload]` / `[Info :GloomhavenVR]`) |
|---|---|
| Preloader ran | `Installed native: ...\GH_Data\Plugins\x86_64\UnityOpenXR.dll` (first boot) or `Native up to date` (debug level); `OpenXR runtime assets ready (package 1.10.0).` |
| RuntimeDeps loaded | `[Core] Loaded 3 runtime dependencies: Unity.XR.CoreUtils, Unity.XR.Management, Unity.XR.OpenXR` then `[Core] RuntimeDeps declare 2 [RuntimeInitializeOnLoadMethod] hook(s) ...` + one `RuntimeInitializeOnLoad invoked: ... — OK` line each |
| Environment | `[Core] VR init environment: Unity 2021.3.5f1, graphics API Direct3D11 (...), -force-d3d11 ...` — **graphics API must be Direct3D11** |
| Pre-flight | `[Core] Pre-flight OK: OpenXR Display/Input subsystem descriptors are registered.` |
| Default runtime | `[Core] Registry ActiveRuntime: <name> (C:\...\*.json)` |
| Runtime enumeration | `[Core] OpenXR runtime candidates (N): system default → <name> \| ...` |
| Per attempt | `[Core] Attempting OpenXR init on: ...` then `phase 2/4: InitXRSDK done — activeLoader: OpenXRLoader` and `phase 4/4: display subsystems: 1 [running=False]` (running=False here is normal) |
| Init success | `[Core] OpenXR session up — runtime: <name> <version> (OpenXR plugin 1.10.0), render mode: MultiPass, activeLoader: OpenXRLoader.` |
| HMD rendering | `[Core] XR display subsystem is RUNNING (HMD rendering) after N frame(s).` — this is the line that means the headset actually displays the game |
| Plugin summary | `v<version> build <hash> [<branch>] (built <utc>) loaded — N modules initialized, VR RUNNING on '<runtime>'.` — the line leads with the build identity (check it IS the commit you deployed); `N` is however many modules `Plugin.RegisterModules` holds, twelve today. The substring to grep for is `VR RUNNING on '` |
| Rig armed | `[Rig] Rig driver + comfort stack installed — waiting for a scenario camera.` (without a headset: `Comfort stack installed in dev mode …`) |
| Compat | `[Compat] Kill-switches armed for: PostProcessLayer, PostProcessVolume, VolumetricFog.` |
| Intro (pre-menu) | `[WorldUI] Starting indicator shown (pre-menu scene, FlatScreen gated).` — intro plays vanilla on the desktop, HMD shows a grey void + "starting…" label |
| Menu scene up | `[Rig] Menu rig built at vantage of camera '<name>' (…)`, `[WorldUI] FlatScreen shown …`, `[WorldUI] FlatScreen quad placed: …`, `[WorldUI] Desktop mirror active — FlatScreen RT …`, `[Core] Stereo policy: '<name>' forced to StereoTargetEyeMask.None (…)` |
| Per menu scene load | `[WorldUI] Camera inventory after scene '<name>' (N active):` + one line per camera (tag/depth/clear/mask/stereo/target) — **quote these in every menu-rendering report** |
| In scenario | `[Rig] VR rig built at focus (...), world scale <s> ...` then `[Rig] Recentered — ...` |

With `[General] Enabled = false`: only two lines — preloader skip notice + plugin
"Disabled via config" — and zero behavioral change.

## 3. Validation checklist

- [ ] Headset shows the game rendering stereo (both eyes, correct separation) once XR is up.
- [ ] Load any scenario: board appears as a **diorama/table** (world scale sane).
      There is no `[Rig] WorldScale` bind any more — the base scale is derived at rig
      build and the player owns the rest: two-grip pinch-zoom, persisted as
      `[Comfort] SavedScaleMultiplier` and clamped by `[Comfort] ScaleMin`/`ScaleMax`
      in `dev.gloomhavenvr.comfort.cfg`. Report the `world scale <s>` figure from the
      `VR rig built at focus` line if the diorama is wrong at first spawn.
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

### 4.0 What to collect for EVERY XR failure report

Attach **all three** logs into `.planning/debug/` (create a dated subfolder).
**Reminder: hardware report #2 (menu blackscreen) was missing the Player.log — it is
still wanted for every report, including successful ones after a failure.**

1. `<game>/BepInEx/LogOutput.log` — the mod's own log (candidate attempts, phase logs).
2. `%USERPROFILE%\AppData\LocalLow\FlamingFowlStudios\Gloomhaven\Player.log` — the Unity
   player log. **Unity XR native errors (lines starting with `[XR]`, `xrCreateInstance`
   failures, graphics-requirement errors) land ONLY here**, never in the BepInEx log.
   (`Player-prev.log` next to it holds the previous run.)
3. `<game>/BepInEx/openxr-diagnostics.log` — the mod appends the native OpenXR
   diagnostics report here per failed candidate attempt (timestamped + labeled), plus a
   success report. This contains the per-attempt OpenXR error codes.

The BepInEx log's `VR init environment:` line states Unity version, graphics API
(**must be Direct3D11**), and whether `-force-d3d11` was passed. The
`Registry ActiveRuntime:` line states what the machine's default OpenXR runtime is.

### 4.1 Symptom table

| Symptom (log evidence) | Diagnosis | Fix |
|---|---|---|
| Pre-flight FAILED: **no descriptors** (`display: False, input: False`) | Engine didn't pick up manifest/natives at boot | Check preloader lines earlier in the log; verify the three files under `GH_Data/` (§1.3); verify `BepInEx/patchers/GloomhavenVR/Natives/*.dll` exist (fetch-natives + deploy); check `install-state.json` hashes vs `libs/Natives/README.md` |
| Descriptors OK but **no display subsystem** after all candidates (`All OpenXR runtime candidates failed`) | OpenXR loader can't reach a runtime | Headset connected & runtime running? Check `[Core] OpenXR runtime candidates` list and the per-candidate `phase 2/4: InitXRSDK done — activeLoader: null` lines; read `openxr-diagnostics.log` + Player.log `[XR]` lines for the native error; try `RuntimeOverride` with an explicit runtime JSON |
| `VR init environment:` reports a graphics API **other than Direct3D11** | Desktop OpenXR needs **D3D11** — session creation fails natively (errors in Player.log only) | Add `-force-d3d11` to Steam launch options; verify the game didn't launch under `-force-glcore`/D3D12 |
| `OpenXR session up` logged but **HMD never lights up** and `display subsystem did NOT start rendering` follows | Runtime never reached READY (headset asleep, streamer disconnected, or graphics requirements unmet) | Wake the headset / (re)connect Virtual Desktop or Link **before** launching; check Player.log `[XR]` lines; try `[Core] InitDelayFrames = 120` |
| **SteamVR boots although you play via Virtual Desktop/Link** | A runtime candidate attempt reached SteamVR before the right runtime | Should not happen anymore ("auto" tries the system default first and SteamVR last). If it does: set `[Core] RuntimePriority = vdxr` (or `oculus`), or `[Core] SkipRuntimeCandidates = true`, and report the candidate list line |
| **Desktop black in the menus** (intro audible, nothing visible) | Pre-`fix/menu-blackscreen` builds: FlatScreen quad died with a Single scene load while the UICamera stayed redirected into its RenderTexture | Update the mod. On current builds the desktop is fed by the end-of-frame RT blit (`Desktop mirror active` line); if still black, grep the `Camera inventory` lines and check the UICamera's `target=` column (see `docs/TESTING-P3C.md` §10) |
| **HMD black in the menus** (XR RUNNING logged) | Menu rig camera not rendering, or the flat-screen quad invisible (shader/layer/placement) | HMD grey = rig camera fine, content missing → check `FlatScreen quad placed:` (shader `NULL`? layer moved?). HMD pitch black = rig camera not reaching the HMD → quote `Menu rig built …` + `Camera inventory` lines |
| Stereo up but **world not table-scaled** / camera inside geometry | Base-scale heuristic off (s_TileSize not initialized at rig build) | Quote the `world scale <s>` figure from the `VR rig built at focus …` line; pinch-zoom to a sane size and check whether `[Comfort] SavedScaleMultiplier` in `dev.gloomhavenvr.comfort.cfg` holds it across a re-enter |
| Stereo up but **camera fights/jumps** with game camera moves | A camera writer not covered by the LateUpdate skip (SmartFocus/timeline) | Expected P1 edge; note the trigger (cutscene? door reveal?) for the Phase-4 comfort pass |
| Broken/one-eye post effects | A PPv2/fog effect slipped through | Ensure `[Compat] DisablePostProcessing`/`DisableVolumetricFog` are true; add offender type name to `DisableComponents` |
| `DllNotFoundException: UnityOpenXR` in diagnostics | Natives missing from `GH_Data/Plugins/x86_64` | Same as first row — preloader install failed |
| Crash/weird XR behavior with provisional RuntimeDeps | Provisional compile drift | Swap in the editor-harvested RuntimeDeps set (`unity/HARVESTING.md`) and retest |

## 5. Known P1 limitations (by design)

- No controllers/hands yet (Phase 2); no VR UI (Phase 3c); no world grab (Phase 4).
- Zoom (FOV-based) is meaningless in VR; scripted camera moves are frozen while VR runs.
- MultiPass rendering only (SPI is a later opt-in experiment).
- InputSystem stays at the game's 1.3.0; RuntimeDeps compiled accordingly
  (`USE_INPUT_SYSTEM_POSE_CONTROL` off). The InputSystem 1.7 upgrade decision lands in Phase 2.
