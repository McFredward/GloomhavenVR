# VR Prior Art Research — Gloomhaven Digital VR Mod

Research date: 2026-07-14. Target: Gloomhaven (digital, Flaming Fowl Studios), Unity **2020.3.33f1** Mono, built-in render pipeline, BepInEx + Harmony, Meta Quest 3 via PC Link/SteamVR.

---

## 1. Executive Summary

Two proven bootstrap families exist for adding VR to flat Unity Mono games:

1. **Unity OpenXR plugin injected at runtime** (DaXcess: LCVR, RepoXR, CWVR). A BepInEx *preload patcher* copies `UnityOpenXR.dll` + `openxr_loader.dll` into `<Game>_Data/Plugins` and writes a `UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json`; the plugin then constructs `XRGeneralSettings`/`XRManagerSettings`/`OpenXRLoader` ScriptableObjects in code and calls `InitializeLoaderSync()` + `StartSubsystems()`. Input flows through Unity's new Input System with OpenXR interaction profiles. Works on any OpenXR runtime (Oculus/Link, SteamVR, Virtual Desktop).
2. **OpenVR/SteamVR path** (SPT-VR for Tarkov, NomaiVR, TwoForksVR, and UUVR's OpenVR toggler). Ships Valve's OpenVR XR plugin (`XRSDKOpenVR.dll` + manifest) or patches `globalgamemanagers` `enabledVRDevices` on legacy Unity, and uses **SteamVR Input** (`actions.json`) for controllers — which bypasses Unity's input system entirely. Requires SteamVR running.

**Recommendation for Gloomhaven (Unity 2020.3.33f1):** the OpenXR path is viable — Unity OpenXR package 1.7.0 is verified for 2020.3 — and is what LCVR/RepoXR prove out end-to-end. The single biggest unknown is whether Gloomhaven was built with the new Input System backend enabled (`Active Input Handling`); if not, either patch `globalgamemanagers` (UUVR has working AssetsTools-based code for editing this file) or fall back to the SteamVR Input path (SPT-VR precedent, fully independent of Unity input settings). Gloomhaven's game code lives in `GH.Runtime.dll` and `ScenarioRuleLibrary.dll` (not `Assembly-CSharp.dll`), is not obfuscated (community edits it with dnSpyEx), and BepInEx 5 + Harmony is already proven working by existing mods.

For interaction design, Demeo is the gold standard: palm-up card fan, grab-and-place cards onto board squares, grip-grab the table to move/zoom the diorama, targeting beams from hands, physically tossed dice.

---

## 2. Per-Project Analysis

### 2.1 LCVR — Lethal Company VR (DaXcess)

- **Repo:** https://github.com/DaXcess/LCVR — **License:** GPL-3.0 (~340 stars)
- **Game:** Lethal Company, Unity 2022.3.9f1, **Mono**, HDRP, BepInEx 5.4.22. README: https://github.com/DaXcess/LCVR/blob/main/README.md

**(a) VR init at runtime** — `Source/OpenXR.cs` (https://github.com/DaXcess/LCVR/blob/main/Source/OpenXR.cs):
- `Loader.InitializeXR()` creates `XRGeneralSettings` + `XRManagerSettings` instances, creates an `OpenXRLoader`, and adds it via `((List<XRLoader>)xrManagerSettings.activeLoaders).Add(xrLoader)`.
- Registers **seven controller interaction profiles** (Oculus Touch, Index, Vive, etc.) into `OpenXRSettings.Instance.features` before init — without this OpenXR reports no input.
- Configures single-pass rendering, disables depth submission.
- Validates that the OpenXR subsystems actually registered: checks `subsystems.Any(s => s.id == "OpenXR Input")` and a display subsystem — i.e. the native manifest injection must have already happened at preload time.
- **Runtime discovery/selection:** enumerates OpenXR runtimes from registry `SOFTWARE\Khronos\OpenXR\1` via P/Invoke (`RegOpenKeyEx`/`RegEnumValue`), parses runtime JSON manifests, locates SteamVR / Virtual Desktop / Oculus installs (`LocateCommonRuntimes()`), priority: config override → system default → others. Diagnostics via P/Invoke into `UnityOpenXR.dll`: `Internal_GenerateReport()`, `Internal_GetRuntimeName()`, `Internal_GetRuntimeVersion()`.
- `Source/Plugin.cs` verifies game version by SHA256 of `Assembly-CSharp.dll` against a list refreshed from a GitHub gist; supports `--disable-vr` / `--lcvr-skip-checksum` flags; on `InitializeXR()` failure sets a `StartupFailed` flag and lets the game run flat.
- **Shipped files (`RuntimeDeps` folder, from README):** `UnityEngine.SpatialTracking.dll`, `Unity.XR.CoreUtils.dll`, `Unity.XR.Interaction.Toolkit.dll`, `Unity.XR.Management.dll`, `Unity.XR.OpenXR.dll`, `openxr_loader.dll`, `UnityOpenXR.dll` — plus two asset bundles (`lethalcompanyvr`, `lethalcompanyvr-levels`).
- `Preloader/Preload.cs` here is a resilience shim: hooks `Assembly.GetTypes()` (filters null types) and `RuntimeType.IsAssignableFrom` (catches `TypeLoadException`) so soft dependencies don't crash reflection scans.

**(b) Input** — Unity InputSystem action maps (`Source/Input/Actions.cs`, `RemappableControls.cs`), `InputSystem.settings.defaultButtonPressPoint` tuning, custom composites (`ButtonFallbackComposite`), snap/smooth turn providers.

**(c) Hands** — `Source/Input/FingerCurler.cs` is the canonical controller-driven finger animation:
- Three actions: `ThumbAction` (thumb touch), `IndexAction` (trigger), `OthersAction` (grip) → thumb / index / middle+ring+pinky.
- Two bones per finger; `var angle = Mathf.Lerp(0f, 80f, curl); bone01.localRotation = Quaternion.AngleAxis(-angle, Vector3.right) * bone01Rotation;` with per-frame smoothing `Mathf.Lerp(curl, target, 0.5f)`.
- Gesture detection: thumbs-up when `index > 0.8 && grip > 0.8` and thumb untouched. Full body/arm rig in `Source/Player/VRPlayer.cs` (37 KB), `Bones.cs`, `VRController.cs`.

**(d) uGUI** — `Source/Patches/UI/UIPatches.cs`: forces `XRUIInputModule.activeInputMode = InputSystemActions` (XR Interaction Toolkit's UI input module), then rather than converting the game's screen-space canvases it **loads dedicated VR UI scenes/prefabs from asset bundles** (`SceneManager.LoadSceneAsync("LCVR Init Scene", Additive)`) and positions panels in world space. Custom `VRHUD.cs` (26 KB) rebuilds the HUD.

**(e) Camera** — `Source/Patches/CameraPatches.cs` + `XRPatches.cs`; XR rig with tracked pose; game camera scripts disabled via Harmony.

**(f) Stereo/post** — HDRP game; LCVR registers custom post-process shaders (`VolumeManager.RegisterCustomPostProcessShaders()`) from bundles. Not directly transferable to built-in RP but shows the "ship fixed shaders in a bundle" pattern.

### 2.2 RepoXR — R.E.P.O. VR (DaXcess)

- **Repo:** https://github.com/DaXcess/RepoXR — **License:** GPL-3.0. Assets built in companion Unity project https://github.com/DaXcess/RepoXR-Unity.
- **The cleanest bootstrap template.** `Preload/Preload.cs` (https://github.com/DaXcess/RepoXR/blob/main/Preload/Preload.cs), a BepInEx preload patcher with a **no-op `Patch()`** (no assembly modification at all):
  - Creates `REPO_Data/UnitySubsystems/UnityOpenXR/` and writes `UnitySubsystemsManifest.json` with `"name": "OpenXR XR Plugin", "version": "1.10.0"`.
  - Copies `UnityOpenXR.dll` and `openxr_loader.dll` from the mod's `RuntimeDeps/` into `REPO_Data/Plugins/`.
  - Also scans `sharedassets0.assets` for the game version → env vars.
  - This proves the OpenXR native subsystem can be injected into a game **that shipped with zero XR support**, before Unity's subsystem scan runs.
- Input: `Source/Input/VRInputSystem.cs`, `Actions.cs`, `TrackingInput.cs`, runtime rebinding (`RebindManager.cs`).
- UI: `Source/UI/XRRayInteractorManager.cs` (XR Interaction Toolkit ray interactors driving uGUI), `CurvedTMP.cs` for curved text, ~20 UI patch files; camera stack under `Source/Player/Camera/` (aim, position, zoom, custom spectator camera).
- Grabbing: patches the game's own physics grabber (`Source/Patches/PhysGrabberPatches.cs`) to be driven by hand pose instead of camera ray — a good pattern for retrofitting an existing game interaction system to motion controls.

### 2.3 CWVR — Content Warning VR (DaXcess)

- **Repo:** https://github.com/DaXcess/CWVR — same architecture ("powered by Unity's OpenXR plugin", 6DOF motion hands). Third data point that the pattern generalizes across Unity versions/pipelines. Thunderstore: https://thunderstore.io/c/content-warning/p/DaXcess/CWVR/

### 2.4 UUVR — Universal Unity VR (Raicuparta)

- **Repo:** https://github.com/Raicuparta/uuvr — **License:** GPL-3.0. Supports Mono + IL2CPP, legacy (pre-XR-plugin) and modern Unity.
- **Installer/patcher** — `Uuvr.Patcher/UuvrPatcher.cs`:
  - Copies a `CopyToGame/Data` folder into the game's `_Data` dir, containing `UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json` and `UnitySubsystems/XRSDKOpenVR/UnitySubsystemsManifest.json`, plus `StreamingAssets/SteamVR/actions.json` and controller binding files.
  - Copies arch-specific native plugins (`x64`/`x86`): `openxr_loader.dll`, `UnityOpenXR.dll`, `XRSDKOpenVR.dll` (Valve OpenVR XR plugin), `openvr_api.dll`, `OVRPlugin.dll`.
  - **Deletes conflicting pre-existing VR DLLs** first: `PluginsToDeleteBeforePatch = { "openvr_api", "openxr_loader", "UnityOpenXR", ... }` — important, stale copies break loading.
  - Under `#if LEGACY` (old Unity): uses AssetsTools to load `globalgamemanagers`, back it up, and set `BuildSettings.enabledVRDevices = ["OpenVR", "Oculus"]` — the legacy `XRSettings` enable path. It reads the Unity version straight from the assets file: `am.LoadClassDatabaseFromPackage(ggmFile.typeTree.unityVersion)`.
- **Runtime toggling** — `Uuvr/VrTogglers/` (`XrPluginOpenXrToggler.cs`, `XrPluginOpenVrToggler.cs`, `LegacyOpenVrToggler.cs`, managed by `VrTogglerManager.cs`). `XrPluginToggler.cs`: `ScriptableObject.CreateInstance<XRGeneralSettings>()` + `<XRManagerSettings>()`, `_managerSettings.loaders.Add(CreateLoader())`, `InitializeLoaderSync()`, check `activeLoader != null`, `StartSubsystems()` — identical skeleton to LCVR, confirmed to work on 2020-era Unity.
- **Vendored packages:** UUVR ships recompiled copies of `Uuvr.XR.Management`, `Uuvr.XR.OpenXR`, `Uuvr.XR.OpenVR`, and the full SteamVR C# plugin (`Uuvr.SteamVR/SteamVR_Input.cs` etc.) — i.e., you don't need the game to contain any XR managed code; you can ship it all.
- **UI** — `Uuvr/VrUi/`: `VrUiManager`, `VrUiCursor`, and two patch modes: `CanvasRedirect.cs` (re-target existing canvases into VR) and `ScreenMirrorPatchMode.cs` (render the flat screen to a texture on a quad) — the universal fallbacks when a game's UI resists world-space conversion. Config exposes UI position/scale/shader/render-queue and "components to disable / objects to deactivate" lists (for killing Cinemachine-style camera controllers generically). Release notes: https://github.com/Raicuparta/uuvr/releases/tag/v0.4.0
- **Camera** — `Uuvr/VrCamera/VrCamera.cs`, `VrCameraManager.cs`, `VrCameraOffset.cs`. Input is the weakest part (basic `UuvrInput`/XInput); UUVR is head-tracking-first, not motion-controls-first.

### 2.5 SPT-VR / TarkovVR (cybensis)

- **Repo:** https://github.com/cybensis/SPT-VR (open source; mod page: https://hub.sp-tarkov.com/files/file/2419-spt-vr/) — Tarkov is a Unity 2019-era **built-in render pipeline, legacy-input** game; the most relevant precedent for a game without the new Input System.
- **VR init:** Uses Valve's **OpenVR XR plugin + SteamVR plugin**. Ships managed libs in `libs/Managed/`: `Unity.XR.OpenVR.dll`, `Unity.XR.Management.dll`, `Unity.XR.OpenXR.dll`, `SteamVR.dll`, `SteamVR_Actions.dll`. Requires SteamVR running. `Patches/Core/VR/InitVRPatches.cs` parents the main camera into a rig via Harmony patch: `mainCam.transform.parent = VRGlobals.vrOffsetter.transform; mainCam.stereoTargetEye = StereoTargetEyeMask.Both;` and `AddComponent<SteamVR_TrackedObject>()`.
- **Input:** SteamVR Input action sets (`SteamVR_Actions`), rebindable via SteamVR's binding UI — completely independent of Unity's input configuration. Quest/Index/Vive bindings work out of the box; others user-bindable (README).
- **Hands:** custom hand models in `Assets/handsbundle` AssetBundle; `HandsPositioner.cs`; arm IK via `Source/Player/Body/IKManager.cs` + `DynamicElbowPositioner.cs`.
- **uGUI:** `Patches/UI/UIPatches.cs` (~67 KB of canvas surgery), `Source/UI/VRUIInteracter.cs` (laser-pointer interaction), `VRKeyboard.cs`, `MenuMover.cs`, `MouseInputBlockPatches.cs` — shows the "convert existing screen-space UI + laser pointer + block mouse" approach at scale.

### 2.6 NomaiVR — Outer Wilds (Raicuparta)

- **Repo:** https://github.com/Raicuparta/nomai-vr — **License:** MIT. SteamVR/OpenVR based; requires Steam + SteamVR even for Epic/Game Pass copies; **SteamVR Input** bindings; separate `NomaiVRPatcher` copies files into `OuterWilds_Data/Plugins/x86_64/` (incl. `openvr_fsr`); a `Unity/` project builds asset bundles (hands, input prompt icons). Mature (121 releases). Good reference for: hand-attached input prompts, converting a story game's UI, performance tricks (openvr_fsr, fixed foveated rendering addon).

### 2.7 TwoForksVR — Firewatch (Raicuparta)

- **Repo:** https://github.com/Raicuparta/two-forks-vr — **License:** MIT. Same architecture as NomaiVR (patcher + SteamVR + asset sources in `AssetSources/Graphics`), for a much older Unity. Confirms the patcher-plus-SteamVR pattern scales down to legacy Unity where the XR plugin framework doesn't exist.

### 2.8 Tabletop/turn-based flat2VR mods

No direct "flatscreen tabletop game → VR mod" precedent was found (Tabletop Simulator's VR is native, Demeo is native VR). The closest analogs are the diorama/god-view aspects of UUVR-modded strategy games and Demeo itself (Section 6). This mod would be breaking somewhat new ground in genre, but not in technique.

---

## 3. Recommended VR Bootstrap for Unity 2020.3 Mono + Built-in RP (step-by-step)

**Primary path: Unity OpenXR plugin, injected LCVR/RepoXR-style.** Unity's OpenXR package **1.7.0 is verified for Unity 2020.3** (https://docs.unity3d.com/2020.3/Documentation/Manual/com.unity.xr.openxr.html); RepoXR injects manifest version 1.10.0 into its game — match the package version you compile against.

1. **Harvest binaries:** create a throwaway Unity **2020.3.33f1** project, install `com.unity.xr.openxr` (+ `com.unity.xr.management`, `com.unity.inputsystem`, `com.unity.xr.interaction.toolkit` 2.x, `com.unity.xr.legacyinputhelpers`/`UnityEngine.SpatialTracking`), build a dummy player, and collect: managed `Unity.XR.Openxr.dll`, `Unity.XR.Management.dll`, `Unity.InputSystem.dll`, `Unity.XR.CoreUtils.dll`, `Unity.XR.Interaction.Toolkit.dll`, `UnityEngine.SpatialTracking.dll`; native `UnityOpenXR.dll`, `openxr_loader.dll`; and the generated `UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json`. (This is exactly the LCVR `RuntimeDeps` list: https://github.com/DaXcess/LCVR/blob/main/README.md)
2. **BepInEx preload patcher** (RepoXR `Preload/Preload.cs` template): on startup, ensure `Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json` exists and copy `UnityOpenXR.dll` + `openxr_loader.dll` into `Gloomhaven_Data/Plugins/x86_64/` (2020.3 uses the `Plugins/x86_64` subfolder). Delete stale VR dlls first (UUVR's `PluginsToDeleteBeforePatch` lesson). `Patch()` can be a no-op — no assembly rewriting needed.
3. **Input-handling check (critical):** inspect `globalgamemanagers` for `Active Input Handling`. If Gloomhaven shipped legacy-only, the Input System native backend is off and OpenXR input will be silent. Fix by patching `globalgamemanagers` (PlayerSettings `activeInputHandler` → Both) with **AssetsTools.NET** in the patcher, with backup — UUVR's `UuvrPatcher.cs` contains working `globalgamemanagers` edit + backup code to copy (https://github.com/Raicuparta/uuvr). Docs on the setting: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.0/manual/Installation.html
4. **Runtime init in the BepInEx plugin** (port LCVR `OpenXR.cs` + UUVR `XrPluginToggler.cs`):
   - `ScriptableObject.CreateInstance<XRGeneralSettings>()`, `CreateInstance<XRManagerSettings>()`, link them; create `OpenXRLoader`; enable interaction-profile features (at minimum `OculusTouchControllerProfile`, plus Index/Vive for SteamVR users) on `OpenXRSettings.Instance.features` **before** `InitializeLoaderSync()`; then `StartSubsystems()`.
   - Verify `OpenXR Input` + display subsystems exist; log runtime name/version via the `UnityOpenXR` P/Invokes; fail soft to flatscreen like LCVR.
   - Runtime selection: default OS runtime is fine (Quest 3 Link = Oculus runtime; Virtual Desktop = VDXR; SteamVR). Optionally port LCVR's registry enumeration for a config override.
5. **Stereo mode:** start with **multi-pass** for correctness. Built-in RP does **not** support single-pass instanced with deferred rendering (https://docs.unity3d.com/Manual/SinglePassInstancing.html); Gloomhaven's shaders and its post stack (likely PostProcessing v2) are untested in stereo. Move to single-pass instanced only after auditing shaders (custom shaders need `UNITY_VERTEX_INPUT_INSTANCE_ID` etc.).
6. **Camera:** find the gameplay `Camera` (and any Cinemachine brain / custom orbit controller in `GH.Runtime.dll`), Harmony-disable its mouse-orbit update, parent it under an XR rig GameObject, add `TrackedPoseDriver` (from `UnityEngine.SpatialTracking`), set `stereoTargetEye = Both`, reset FOV (HMD-driven), set tracking origin to floor. For the Demeo diorama feel, scale the **rig**, not the world: rig scale ≈ 10–20× makes the board miniature; grip-drag/two-hand-pinch reposition/zoom = translating/scaling the rig transform.
7. **Fallback path (de-risk):** Valve OpenVR XR plugin (`XRSDKOpenVR.dll` + its `UnitySubsystemsManifest.json` + `openvr_api.dll`) + SteamVR plugin with `StreamingAssets/SteamVR/actions.json` — exactly what UUVR ships and SPT-VR/NomaiVR use. SteamVR Input reads controllers via `openvr_api` directly, so it works even if Unity's input backends are locked to legacy. Cost: requires SteamVR even over Link (extra layer/latency on Quest), and Valve's plugin is old (known issue: single-pass non-instanced unsupported — https://github.com/ValveSoftware/unity-xr-plugin/issues/76).

---

## 4. Input & Hands Recommendations

- **Input:** Unity InputSystem `InputActionAsset` loaded from an embedded asset (LCVR `Source/Input/Actions.cs` pattern), with bindings on OpenXR device layouts (`<XRController>{LeftHand}/...`). Unity recommends binding OpenXR layouts and notes all bindings must be attached at startup (https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.4/manual/input.html). Include runtime rebinding later (RepoXR `RebindManager.cs`).
- **Palm-up detection** (for the card fan): compute `Vector3.Dot(handTransform.up /* or -forward depending on model */, Vector3.up)` or angle between palm normal and head-to-hand direction each frame; hysteresis threshold (~0.6 show / ~0.4 hide) to avoid flicker. Demeo gates its card hand on exactly this wrist rotation (see Section 6).
- **Finger animation:** port LCVR `FingerCurler.cs` (GPL-3.0 — fine if the mod is GPL; else reimplement the trivial pattern): trigger value → index curl, grip value → middle/ring/pinky, thumb touch (Quest 3 capacitive `thumbstickTouched`/`primaryTouched`) → thumb; 2-bone lerp 0→80°, smoothing lerp 0.5/frame; special-case point (index extended when trigger untouched but grip held) and thumbs-up.
- **Hand models & licensing:**
  - **SteamVR Unity Plugin hands** — BSD-3-Clause, redistributable with attribution; includes rigged low/high-poly hands + skeleton (https://github.com/ValveSoftware/steamvr_unity_plugin, license: https://github.com/ValveSoftware/steamvr_unity_plugin/blob/master/LICENSE). Best licensed option.
  - **Meta/Oculus sample hands** (`OVRHandPrefab`, Interaction SDK hands) — Oculus SDK License, redistribution restricted; avoid embedding in a GPL/MIT mod.
  - Community open packs: RoboHands-UnityXR (https://github.com/InfernoDigital/RoboHands-UnityXR, free/open, stylized), OculusVRHands wrapper repo (https://github.com/pinglis/OculusVRHands — check its model provenance before shipping), CC0 static hand meshes exist but need rigging (https://www.meshy.ai/tags/hand).
  - Ship models in an **AssetBundle built from a companion Unity 2020.3 project** (RepoXR-Unity / NomaiVR `Unity/` folder pattern) so shaders compile for the game's exact Unity version and built-in RP.

---

## 5. uGUI-in-VR Patterns

Three tiers, all with prior art:

1. **Convert existing canvases** (SPT-VR, UUVR `CanvasRedirect`): Harmony-patch `Canvas` setup (or sweep on scene load): `renderMode = WorldSpace`, position/scale in front of player (~1.5 m, scale ~0.001/px), assign `worldCamera`. Replace `StandaloneInputModule` with XRI's `XRUIInputModule` + add `TrackedDeviceGraphicRaycaster` to each canvas (RepoXR `XRRayInteractorManager.cs` drives uGUI through XRI ray interactors). Block mouse input (SPT-VR `MouseInputBlockPatches.cs`).
2. **Screen-mirror fallback** (UUVR `ScreenMirrorPatchMode.cs`): render UI camera to a RenderTexture on a world quad with a synthetic pointer — use for stubborn full-screen menus (Gloomhaven's map/menu screens are candidates).
3. **Bespoke VR UI from bundles** (LCVR): rebuild critical HUD elements as world-space prefabs. For Gloomhaven, likely needed for the card hand itself and initiative track.

**Finger poke:** XRI 2.3's `XRPokeInteractor` exists (https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.3/manual/whats-new-2.3.0.html) but XRI 2.3's minimum Unity version must be verified against 2020.3 (2.3 targets 2021.3+; XRI 2.0–2.2 support 2020.3 — treat as a risk). Robust alternative used widely in mods: a small trigger collider on the index fingertip; on overlap with a `Selectable`'s collider (or via `GraphicRaycaster.Raycast` from the fingertip), synthesize `PointerEventData` and call `ExecuteEvents.Execute(button, data, ExecuteEvents.submitHandler/pointerClickHandler)` — ~100 lines, no XRI dependency.

**Board hex touch:** hexes in Gloomhaven already respond to mouse raycasts; find the hover/click entry points in `GH.Runtime.dll`/`ScenarioRuleLibrary.dll` and Harmony-redirect them to (a) a hand-forward ray for at-distance selection and (b) fingertip physics overlap for direct touch, calling the same selection methods the mouse path calls. RepoXR's `PhysGrabberPatches.cs` is the model: keep the game's interaction logic, swap the input source.

---

## 6. Demeo Interaction Design Notes (design reference)

- **Card hand:** "action cards are activated by turning one of your hands palm up and grabbing the card and placing it on the play square needed to activate it" (https://www.thevrgrid.com/demeo/; also https://thevrrealm.com/reviews/demeo-review/). The fan is hidden until the palm-up wrist rotation; grabbing a card detaches it; playing = physically placing it on the highlighted board square; releasing elsewhere returns it to the fan.
- **Targeting beams:** "the original control scheme was effective thanks to the targeting beam that extended from your avatar's hands, which allowed you to interact with character models, roll dice, and play cards" (https://www.meta.com/blog/finding-a-name-for-fun-demeo-now-available-on-the-oculus-quest-platform/) — Demeo mixes direct grab (near) with laser (far); plan both.
- **Table/diorama manipulation:** "grab the table to move it around or zoom in and out of the game board", sticks tilt/rotate the view (https://www.thevrgrid.com/demeo/, https://vrcricketguy.com/dive-into-demeo-the-ultimate-vr-tabletop-adventure/). Implementation: grip = grab world anchor; one-hand drag translates, two-hand separation scales, two-hand twist rotates — applied inversely to the XR rig.
- **Pieces & dice:** grab your miniature and move it within highlighted range; dice are physically grabbed and tossed onto the table (https://thevrrealm.com/reviews/demeo-review/).
- **Hand-tracking update (Meta Interaction SDK details, dev interview):** figures have colliders; `TouchHandGrabInteractor` "conform[s] the fingers around the collider"; **cards use proximity detection + predefined hand poses** — the card "snaps to your fingers via your hand placement" rather than exact collider grabbing; card hand also surfaced via a wrist display swipe (https://www.meta.com/blog/demeo-hand-tracking-mixed-reality-mr/). Lesson: snap-to-pose grabbing for cards feels better than physical colliders.
- Background/context: https://venturebeat.com/games/resolution-games-demeo-aims-to-re-create-a-tabletop-dungeon-crawl-in-vr/ and Voices of VR #1665 on Battlemarked/Demeo follow-up (https://voicesofvr.com/1665-resolution-games-battlemarked-blends-mixed-reality-social-features-with-demeo-and-dd-gameplay/).

---

## 7. Gloomhaven Modding Ecosystem

- **Engine:** Unity **2020.3.33f1** (crash report thread: https://steamcommunity.com/app/780290/discussions/0/3362523432293255532/). Windows release Oct 2021, dev Flaming Fowl Studios (https://en.wikipedia.org/wiki/Gloomhaven_(video_game)).
- **Assemblies:** game code is in **`GH.Runtime.dll`** (UI, card layouts, save data) and **`ScenarioRuleLibrary.dll`** (game mechanics/ability types/conditions) — *not* `Assembly-CSharp.dll` (GHEM README: https://github.com/Acenm5/GHEM).
- **No obfuscation:** GHEM contributors edit the assemblies directly with dnSpyEx 6.4.1 (https://github.com/Acenm5/GHEM, LGPL-2.1); decompilation is straightforward.
- **BepInEx works:** Leopard2ARC/GloomhavenMod (https://github.com/Leopard2ARC/GloomhavenMod, MIT) is a BepInEx 5 + Harmony plugin installed via standard `winhttp.dll` + `doorstop_config.ini` next to `GH.exe`. Known quirks it fixes: broken save-name parsing (modded saves vanish from load menu), Resume-button failure with custom rulesets, the in-game ruleset compiler deleting mod source files.
- Nexus has a small mod scene (e.g. "Bug Fixes": https://www.nexusmods.com/gloomhaven/mods/8). Nothing on Thunderstore; no existing VR or camera mods found.
- The game has a built-in "Custom Ruleset" modding system (Steam Workshop) — irrelevant for VR but signals moddable architecture.

---

## 8. Risks / Unknowns

1. **Active Input Handling** — if Gloomhaven shipped legacy-only input, OpenXR-plugin input is dead until `globalgamemanagers` is patched (Section 3.3). Mitigation proven (UUVR AssetsTools edit) but must be validated on this game; fallback = SteamVR Input path (SPT-VR). **Verify first — this decides the architecture.**
2. **Stereo rendering of Gloomhaven's shaders/post stack** — built-in RP + PostProcessing v2 in stereo commonly breaks (screen-space effects sample wrong eye; no SPI with deferred: https://docs.unity3d.com/Manual/SinglePassInstancing.html). Start multi-pass, expect to disable/replace some effects (LCVR precedent: ship replacement shaders in a bundle).
3. **XRI version compatibility** — XRI 2.3 (poke interactor) may not run on 2020.3; may need XRI 2.0–2.2 + custom poke (Section 5). Low risk: custom poke is small.
4. **Camera architecture unknown** — Gloomhaven may use Cinemachine or a bespoke orbit rig; culling distances/LOD and fog tuned for a fixed top-down camera may look wrong up close. UUVR's "components to disable" config pattern is the generic mitigation.
5. **UI volume** — Gloomhaven is UI-heavy (card selection, initiative, tooltips, map, shops). Expect the majority of effort in tier-1/2/3 UI conversion, not in the VR bootstrap.
6. **Performance** — 2020.3 built-in RP at 2× eye render + 90 Hz; Gloomhaven already has performance complaints on flat (https://steamcommunity.com/app/780290/discussions/1/2994297535741213069/). Diorama scale helps (small screen-space triangles), but budget for resolution scaling and openvr_fsr-style upscaling (NomaiVR precedent).
7. **Game updates** — mitigate with LCVR-style assembly checksum + remote-updatable hash list; Gloomhaven updates are infrequent post-1.0 (favorable).
8. **License hygiene** — LCVR/RepoXR/UUVR are GPL-3.0: porting their code makes the mod GPL-3.0. NomaiVR/TwoForksVR (MIT) and SteamVR plugin (BSD-3) are permissive. Decide the mod's license before copying code.

---

## Source Index

- LCVR: https://github.com/DaXcess/LCVR (OpenXR.cs, Plugin.cs, Preloader/Preload.cs, Source/Input/FingerCurler.cs, Source/Patches/UI/UIPatches.cs, README.md)
- RepoXR: https://github.com/DaXcess/RepoXR (Preload/Preload.cs) + https://github.com/DaXcess/RepoXR-Unity
- CWVR: https://github.com/DaXcess/CWVR
- UUVR: https://github.com/Raicuparta/uuvr (Uuvr.Patcher/UuvrPatcher.cs, Uuvr/VrTogglers/XrPluginToggler.cs, Uuvr/VrUi/, lib native plugins + UnitySubsystems manifests)
- SPT-VR: https://github.com/cybensis/SPT-VR (Patches/Core/VR/InitVRPatches.cs, libs/Managed/, Assets/handsbundle) + https://hub.sp-tarkov.com/files/file/2419-spt-vr/
- NomaiVR: https://github.com/Raicuparta/nomai-vr · TwoForksVR: https://github.com/Raicuparta/two-forks-vr
- Unity docs: OpenXR on 2020.3 https://docs.unity3d.com/2020.3/Documentation/Manual/com.unity.xr.openxr.html · SPI limits https://docs.unity3d.com/Manual/SinglePassInstancing.html · OpenXR input https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.4/manual/input.html · InputSystem install/backends https://docs.unity3d.com/Packages/com.unity.inputsystem@1.0/manual/Installation.html · XRI 2.3 https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.3/manual/whats-new-2.3.0.html
- Valve: https://github.com/ValveSoftware/steamvr_unity_plugin (BSD-3) · https://github.com/ValveSoftware/unity-xr-plugin (+issue #76, #16)
- Demeo: https://www.thevrgrid.com/demeo/ · https://thevrrealm.com/reviews/demeo-review/ · https://www.meta.com/blog/demeo-hand-tracking-mixed-reality-mr/ · https://www.meta.com/blog/finding-a-name-for-fun-demeo-now-available-on-the-oculus-quest-platform/ · https://venturebeat.com/games/resolution-games-demeo-aims-to-re-create-a-tabletop-dungeon-crawl-in-vr/
- Gloomhaven: https://steamcommunity.com/app/780290/discussions/0/3362523432293255532/ · https://github.com/Acenm5/GHEM · https://github.com/Leopard2ARC/GloomhavenMod · https://www.nexusmods.com/gloomhaven/mods/8
- Hands: https://github.com/InfernoDigital/RoboHands-UnityXR · https://github.com/pinglis/OculusVRHands · https://github.com/oxters168/VRPhysicsHands
