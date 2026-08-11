# Menu-Audit 03 — Plugin / Core / Rig / Perf / Stereo-Compositor

Audited at HEAD (291d06d). Trace method: every bound entry grepped for `.Value` readers across
`src/GloomhavenVR` + `src/GloomhavenVR.Preload` (decompiled/ and tests/ excluded). "Curated" =
row in `WorldUI/VROptionsTab.4.Curated.cs` (the shipping everyday menu). Entries whose
description starts with `LEGACY — no effect` / `RESERVED —` / `DEPRECATED —` are auto-hidden
from the whole catalog (`ConfigCatalog.cs:350,394-431`); the `NotOffered` table
(`ConfigCatalog.cs:364`) hides working-but-unreachable entries.

Counting note vs. the briefing: **PerfMonitor.cs binds 0 entries** (it only reads PerfConfig;
all 26 perf entries live in PerfConfig.cs) and **ModuleConfig.cs binds 0** (it is the config-file
factory/registry only). ComfortSettings.cs binds 20, not ~2 — all audited below.
No static-batching dial survives in any of these files (consistent with the removal ruling).

## src/GloomhavenVR/Plugin.cs (44 rows; per-style families grouped ×3)

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [General] Enabled | ACTIVE | POWER | Read once in Awake (Plugin.cs:562); restart-only master switch — a live menu row can't apply it |
| [General] LogLevel | ACTIVE | POWER | Live (Plugin.cs:257,265 → VRLog.Level); diagnostics |
| [General] RuntimeOverride | ACTIVE | POWER | CoreModule.cs:~88 → OpenXRBootstrap; startup only |
| [Core] RuntimePriority | ACTIVE | POWER | CoreModule.cs:91; startup only (ConfigCatalog.cs:852 marks Core restart-only) |
| [Core] SkipRuntimeCandidates | ACTIVE | POWER | CoreModule.cs:91 → OpenXRBootstrap.cs:75 |
| [Core] EnableGraphicsJobs | ACTIVE | NORMAL (curated) | Read by the PRELOADER (GloomhavenVR.Preload/Patcher.cs); curated under Grafik▸Performance |
| [Core] AutoRestartForGraphicsJobs | ACTIVE | NORMAL (curated) | Preloader-only read (Patcher.cs); curated beside it |
| [Core] InitDelayFrames | ACTIVE | POWER | Plugin.cs:580; startup escape hatch |
| [Rig] WorldScale | DEAD | — | Marked "LEGACY — no effect"; zero readers; superseded by pinch gesture + SavedScaleMultiplier |
| [Rig] MenuRig | ACTIVE | NORMAL (curated) | VRRigDriver.cs:510 picks Menu rig kind; curated under Grafik |
| [Rig] SpawnInCircle | ACTIVE | NORMAL (curated) | VRRigDriver.Recenter.cs:166; curated under Multiplayer |
| [Rig] Experimental3DMap | DEAD | — | "RESERVED —" placeholder, zero readers (deliberately kept, doc in Plugin.cs:82-92) |
| [Rig] WorldTiltDegrees | DEAD (parked) | — | Runtime clamps tilt to 0 (VRRigDriver.WorldTilt.cs:30 `TargetTiltDegrees => 0f`); revival documented there |
| [Rig] MaskedReaimHeadRate/Gain/Deadband | DEAD (dormant) | — | Read at VRRigDriver.cs:369-377 but sole consumer TickWorldTilt is forced off by the tilt-0 clamp |
| [Rig] VoidColor | ACTIVE | UNCERTAIN | VRRigDriver.HeadCamera.cs:212,310; desc frames it as a debug aid, name ("Leerraum-Farbe") reads aesthetic |
| [Rig] ForwardRendering | ACTIVE | NORMAL (curated) | VRRigDriver.HeadCamera.cs:327; curated under Grafik (arguably POWER, but already a curated row) |
| [Compat] DisablePostProcessing | ACTIVE | NORMAL (curated) | CompatModule.cs:151 |
| [Compat] DisableVolumetricFog | ACTIVE | NORMAL (curated) | CompatModule.cs:153 |
| [Compat] DisableComponents | ACTIVE | POWER | CompatModule.cs:155 (free-text type list) |
| [Compat] WallFade | ACTIVE | NORMAL (curated ×2) | WallSegmentFade.cs:288 gate, live; curated under Komfort▸Sichtbarkeit AND Grafik |
| [Compat] TutorialVRAdapt | ACTIVE | UNCERTAIN | CompatModule.cs:132, TutorialGrabStep.cs:180,216; compat bridge, default on, not curated |
| [Hands] PrimaryHand | ACTIVE | NORMAL (curated) | 5 readers (HandsDriver, ComfortSettings TurnHand resolution…) |
| [Hands] HandStyle | ACTIVE | NORMAL (curated) | 9 readers; live rebuild + MP-synced (Net.AvatarState) |
| [Hands] GripPitchOffsetDegrees | DEAD | — | Bind text says "read once as seed" — stale: the runtime seeding was REMOVED (HandsConfig.cs:205-211 ships measured per-style defaults); zero readers |
| [Hands] HandLateralOffset | DEAD | — | Same as GripPitchOffsetDegrees — zero readers, seed path removed |
| [Hands] HandVerticalOffset | DEAD | — | Same — zero readers |
| [Hands] HandForwardOffset | DEAD | — | Same — zero readers |
| [Hands] {Glove,Plate,Arcane}Scale | ACTIVE | POWER | HandVisuals.cs:164, live per frame; deliberately moved out of Avatar to Debug ("calibration, not a choice" — Curated.cs:298-300) |
| [Hands] {Style}PitchTrimDegrees ×3 | DEAD | — | "LEGACY — no effect"; zero readers (seed path removed) |
| [Hands] {Style}LateralTrim ×3 | DEAD | — | Same |
| [Hands] {Style}VerticalTrim ×3 | DEAD | — | Same |
| [Hands] {Style}ForwardTrim ×3 | DEAD | — | Same |
| [Hands] LaserFingerOrigin | ACTIVE | NORMAL (curated) | RayInteractor.cs:738 |
| [Hands] ScrollWithStickOnly | **DEAD but CURATED** | — | ZERO readers in all of src (only bind/Defaults/Loc/Curated row). Curated.cs:161 still shows it as a working toggle |
| [Hands] LaserFingerOffsetMeters | ACTIVE | POWER | RayInteractor.cs:747; fine-tune of LaserFingerOrigin |
| [Hands] HandColor | SUSPECT | — | Read (HandVisuals.cs:711) but only tints the PROCEDURAL fallback hand a bundle-install never shows; correctly NotOffered (ConfigCatalog.cs:370) |
| [Hands] RayAlwaysOn | **DEAD but CURATED** | — | Read at VRModeStateMachine.cs:270 but provably no-op: line 268 already ORs Ray for dominant unconditionally, line 277 strips non-dominant "under any config"; RayInteractor.cs:628 doc says "inert now". Curated.cs:155 still shows it |
| [Hands] ModalRayConeDegrees | **DEAD but CURATED** | — | Cone gate retired: `VisualsAllowed => true` (RayInteractor.cs:634); zero value readers. Curated.cs:163 still shows it |
| [Dev] Enabled | ACTIVE | POWER | 18 readers (DevModule gate etc.) |
| [Dev] Overlay | ACTIVE | POWER | DevConsole.cs:77 |
| [Dev] SimulateHands | ACTIVE | POWER | HandsDriver.cs:129, RigTarget.cs:47, DevConsole F8 |
| [Dev] InputDeviceDumpInterval | ACTIVE | POWER | DevConsole.cs:110 |

## src/GloomhavenVR/Core/PerfConfig.cs (26) — PerfMonitor.cs binds nothing

All 26 are ACTIVE (readers verified: PerfMonitor.cs throughout for [Perf]; each [Optimize]
accessor has call sites — CacheDelegates 7, MapIconCacheOn 1, FigureScanCacheOn 2, LeanStrings 2,
TooltipGateOn 2, FanRelayoutInterval 1, WallFadeInterval 1, InitiativeDepthInterval 1, Quiet 3,
RemoteContentSeconds 1, SceneProfileOn 1, CullSubmitSplitOn 3, DepthPrepassOn 2,
HeadMaskFromScenarioCam 1, HeadMaskDropMask 3). PerfConfig's own class doc records the user
ruling: measurement + pure work-removal entries are CONFIG-FILE/Debug-ONLY ("super verwirrend
für den User").

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [Perf] Enabled | ACTIVE | POWER | PerfMonitor.cs:372,417; measurement master, logs only |
| [Perf] SummaryIntervalSeconds | ACTIVE | POWER | PerfMonitor.cs:520 |
| [Perf] Attribution | ACTIVE | POWER | PerfMonitor.cs:420 etc. |
| [Perf] TopSteps | ACTIVE | POWER | PerfMonitor.cs:844 |
| [Perf] SpikeLines | ACTIVE | POWER | PerfMonitor.cs:477 |
| [Perf] SpikeBudgetFactor | ACTIVE | POWER | PerfMonitor.cs:472 |
| [Perf] SpikeMaxPerSecond | ACTIVE | POWER | PerfMonitor.cs:482 |
| [Perf] Allocations | ACTIVE | POWER | PerfMonitor.cs:547 |
| [Perf] XrStats | ACTIVE | POWER | PerfMonitor.cs:535,1193 |
| [Perf] FrameSplit | ACTIVE | POWER | PerfMonitor.cs:449,777 |
| [Perf] SceneCensus | ACTIVE | POWER | PerfMonitor.cs:782 |
| [Perf] SceneProfile | ACTIVE | POWER | PerfMonitor.cs:805 (default off, heavyweight walk) |
| [Perf] CullSubmitSplit | ACTIVE | POWER | PerfFrameSplit via CullSubmitSplitOn |
| [Optimize] CacheTickDelegates | ACTIVE | POWER | A/B work-removal, default on, invisible |
| [Optimize] MapIconCache | ACTIVE | POWER | A/B work-removal |
| [Optimize] FigureScanCache | ACTIVE | POWER | A/B work-removal |
| [Optimize] LeanLogStrings | ACTIVE | POWER | A/B work-removal |
| [Optimize] TooltipScanGate | ACTIVE | POWER | A/B work-removal |
| [Optimize] FanRelayoutMinInterval | ACTIVE | POWER | freshness-vs-work interval; Debug pane by design |
| [Optimize] WallFadeEvalInterval | ACTIVE | POWER | dito |
| [Optimize] InitiativeDepthEvalInterval | ACTIVE | POWER | dito |
| [Optimize] QuietDiagnostics | ACTIVE | POWER | log-suppression for clean captures |
| [Optimize] RemoteContentInterval | ACTIVE | POWER | MP board-scan cadence override |
| [Optimize] HeadDepthPrepass | ACTIVE | POWER | real visual trade (VFX soft-fade), but a diagnostics lever |
| [Optimize] HeadCullingMaskDrop | ACTIVE | POWER | measurement instrument (layer names from [Perf] SCENE line) |
| [Optimize] HeadMaskFromScenarioCamera | ACTIVE | POWER | experimental mask seed, default off |

## src/GloomhavenVR/WorldUI/FlatScreenStereo.2.Compositor.cs (25)

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [WorldUI] StereoScreen | ACTIVE | NORMAL candidate | WantActive (Compositor.cs:215); visible feature toggle, localized name, not curated |
| [WorldUI] ScreenDepthStrength | ACTIVE | POWER | DepthStrength (Compositor.cs:175); comfort fine-tune |
| [WorldUI] VideoDepthLayer | ACTIVE | POWER | EndStackSync (Compositor.cs:588) |
| [WorldUI] VideoDepth | ACTIVE | POWER | ComputeVideoShiftUv (Compositor.cs:295) |
| [WorldUI] ScreenParallaxScale | ACTIVE | POWER | ParallaxScale (Compositor.cs:177) |
| [WorldUI] ScreenLeftMirrorFallback | **SUSPECT** | — | Description LIES: claims "no reader anywhere in the mod" — it IS read as a gate in FlatScreenStereo.3.Map.cs:26 (TickBlackProbe) and :161 (TickFastMapEngage); a persisted `false` silently disables the whole map-fix engagement. Hidden from menu by DEPRECATED marker. Fix the desc or drop the reads |
| [WorldUI] MapAlbedoRender | ACTIVE | POWER | The live map fix (Compositor.cs:174 + Map.cs:161); default on, leave-alone switch |
| [WorldUI] MapAlbedoOriginalMaterial | DEAD | — | DEPRECATED, only declaration in 1.State.cs — no reader |
| [WorldUI] MapAlbedoAmbient | DEAD | — | dito |
| [WorldUI] MapAlbedoLight | DEAD | — | dito |
| [WorldUI] MapCaptureMode | DEAD | — | dito (strategy disproven on hardware, code removed) |
| [WorldUI] MapStripBeautify | DEAD | — | dito |
| [WorldUI] MapStripVolumetricFog | DEAD | — | dito |
| [WorldUI] MapStripSSAO | DEAD | — | dito |
| [WorldUI] MapStripPostProcess | DEAD | — | dito |
| [WorldUI] MapTexFlipX | DEAD | — | dito (mode 2 never implemented) |
| [WorldUI] MapTexFlipY | DEAD | — | dito |
| [WorldUI] MapTexSwapDiag | DEAD | — | dito |
| [WorldUI] MapStripAllImageEffects | DEAD | — | dito |
| [WorldUI] MapUvSource | DEAD | — | dito (CPU uv0-rebuild path removed, 53af144) |
| [WorldUI] MapUvSwapUV | DEAD | — | dito |
| [WorldUI] MapUvFlipU | DEAD | — | dito |
| [WorldUI] MapUvFlipV | DEAD | — | dito |
| [WorldUI] MapUvChannel | DEAD | — | dito (shader hard-codes UV0) |
| [WorldUI] MapUvComponent | DEAD | — | dito |

## src/GloomhavenVR/Core/MixedReality.cs (10)

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [MixedReality] Enabled | ACTIVE | NORMAL (curated) | Curated Grafik▸Mixed Reality |
| [MixedReality] KeyColor | ACTIVE | NORMAL (curated) | Curated, preset dropdown (special row) |
| [MixedReality] HideSkyMeshes | ACTIVE | POWER | Safety valve, deliberately NotOffered ("part of MR itself", ConfigCatalog.cs:374) |
| [MixedReality] OpaquePreviewTiles | ACTIVE | POWER | Safety valve, desc says "not offered in the VR menu" |
| [MixedReality] UnseenSkirtScale | ACTIVE | POWER | Numeric MR-backing tuning, live rebuild |
| [MixedReality] UnseenWaferDrop | ACTIVE | POWER | dito (fresh key after UnseenFillDrop semantics change) |
| [MixedReality] UnseenRimInset | ACTIVE | POWER | dito |
| [MixedReality] UnseenRimTopClearance | ACTIVE | POWER | dito |
| [MixedReality] UnseenBackingDebugColors | ACTIVE | POWER | Screenshot diagnostic, default off |
| [MixedReality] UnseenRegionMembership | ACTIVE | POWER | Safety valve, desc says not offered in menu |

## src/GloomhavenVR/Core/StereoModeConfig.cs (1)

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [Stereo] RenderMode | ACTIVE | POWER | Read once at session creation (OpenXRBootstrap.cs:282); SPI provably breaks rendering here — test harness, never a normal option. NOTE: the Label()/Cycle() panel accessors have no caller since the old SettingsPanel died; entry now reachable only via generic catalog |

## src/GloomhavenVR/Core/WallSegmentFade.cs — WallFadeTuning (6)

All drive the SHIPPED whole-wall fade (not the parked one-eye stereo experiment — that work
never landed as config; analysis lives in .planning/wall-fade-stereo-rivalry.md).

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [WallFade] OnFraction | ACTIVE | POWER | WallSegmentFade.cs:754 (live per evaluation tick) |
| [WallFade] OffFraction | ACTIVE | POWER | WallSegmentFade.cs:755, Gate.cs:481 |
| [WallFade] ExitDwellMovedSeconds | ACTIVE | POWER | WallSegmentFade.cs:756 |
| [WallFade] ExitDwellStationarySeconds | ACTIVE | POWER | WallSegmentFade.cs:757 |
| [WallFade] StackedShellFade | ACTIVE | UNCERTAIN | Stacked.cs:416,683; visible behavior toggle (fort superstructures), not curated |
| [WallFade] SyncPeerFades | ACTIVE | NORMAL (curated) | Net.cs:184; curated under Komfort▸Sichtbarkeit |

## src/GloomhavenVR/Core/ModuleConfig.cs (0)

Binds no entries — it is the config-file factory + registry the in-VR browser enumerates.

## src/GloomhavenVR/Rig/RenderQuality.cs (6)

All re-asserted per frame from Tick (RenderQuality.cs:265-276). NOTE: the preset cycle row
(CyclePreset/PresetLabel, "Quality/Balanced/Performance/Minimum") has NO caller any more — the
old SettingsPanel is gone and VROptionsTab curates no RenderQuality row at all. The user-facing
GPU-quality trade PerfConfig's doc points at ("SettingsPanel.BuildPerformanceCategory") no
longer exists in the menu.

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [RenderQuality] MsaaLevel | ACTIVE | NORMAL candidate | ApplyMsaa per frame; classic quality setting, localized, NOT curated |
| [RenderQuality] ForceAnisotropic | ACTIVE | NORMAL candidate | ApplyAniso; pure quality raise |
| [RenderQuality] EyeResolutionScale | ACTIVE | NORMAL candidate | ApplyEyeScale; THE primary GPU lever per its own doc |
| [RenderQuality] ViewportScaleFallback | ACTIVE | POWER | RenderQuality.cs:482; A/B lever-choice diag |
| [RenderQuality] RebuildRigOnMsaaChange | ACTIVE | POWER | RenderQuality.cs:389; answered diagnostic, "leave off in normal play" |
| [RenderQuality] PixelLightCount | ACTIVE | UNCERTAIN | ApplyPixelLights; real visible perf-vs-look trade the game doesn't expose |

## src/GloomhavenVR/Rig/ComfortSettings.cs (20)

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [Comfort] WorldGrabEnabled | ACTIVE | NORMAL (curated) | WorldGrab driver |
| [Comfort] FreeMovement | ACTIVE | NORMAL (curated) | EffectiveScaleMin/Max, clamp gates |
| [Comfort] VerticalDrag | ACTIVE | NORMAL (curated) | EffectiveVerticalDrag |
| [Comfort] RotateEnabled | ACTIVE | NORMAL (curated) | WorldGrab |
| [Comfort] ScaleEnabled | ACTIVE | NORMAL (curated) | WorldGrab |
| [Comfort] ScaleMin | ACTIVE | POWER | Clamp numeric; FreeMovement (default on) mostly overrides it; not curated |
| [Comfort] ScaleMax | ACTIVE | POWER | dito |
| [Comfort] TurnMode | ACTIVE | NORMAL (curated) | Turn driver |
| [Comfort] SnapTurnDegrees | ACTIVE | NORMAL (curated) | |
| [Comfort] SmoothTurnSpeed | ACTIVE | NORMAL (curated) | |
| [Comfort] TurnHand | ACTIVE | NORMAL (curated) | |
| [Comfort] FlightEnabled | ACTIVE | NORMAL (curated) | Rig/Flight |
| [Comfort] FlightDirection | ACTIVE | NORMAL (curated) | |
| [Comfort] FlightMaxSpeed | ACTIVE | NORMAL (curated) | |
| [Comfort] FlightHand | ACTIVE | NORMAL (curated) | |
| [Comfort] RecenterHoldSeconds | ACTIVE | NORMAL (curated) | |
| [Comfort] SavedScaleMultiplier | ACTIVE | POWER | Persisted gesture STATE, not a choice (auto-written after each pinch); keep out of curated |
| [Comfort] DebugGizmos | ACTIVE | POWER | Comfort debug overlay |
| [Comfort] KeepPlaceOnReorigin | ACTIVE | UNCERTAIN | VRRigDriver.TickOriginGuard; genuine comfort toggle, localized, not curated |
| [Comfort] TableScaleDefault25Applied | ACTIVE (internal) | POWER | One-time migration marker (ComfortSettings.cs:374-388); must never get a row (named "Interne Marke") |

---

## DEAD (one-line evidence each)

**Curated menu currently shows three dead knobs — highest-value finding:**
- `[Hands] ScrollWithStickOnly` — zero readers in all of src (bind/Defaults/Loc/curated row only); Curated.cs:161.
- `[Hands] RayAlwaysOn` — read at VRModeStateMachine.cs:270 but provably no-op (dominant ray unconditionally ORed at :268, non-dominant stripped at :277 "under any config"; RayInteractor.cs:628: "inert now"); Curated.cs:155.
- `[Hands] ModalRayConeDegrees` — cone gate retired, `VisualsAllowed => true` (RayInteractor.cs:634), zero value readers; Curated.cs:163.
None of the three carries a retired-marker description, so the catalog cannot hide them and their rows silently lie. Either mark them `LEGACY — no effect` + drop the curated rows, or reinstate readers.

Already-hidden dead entries (retired markers keep them out of every menu — no action needed for the overhaul, listed for completeness):
- `[Rig] WorldScale` — zero readers; superseded by pinch gesture (doc at bind site).
- `[Rig] Experimental3DMap` — reserved placeholder, zero readers.
- `[Rig] WorldTiltDegrees` — consumer forces tilt 0 (VRRigDriver.WorldTilt.cs:30); parked.
- `[Rig] MaskedReaimHeadRate/Gain/Deadband` — read (VRRigDriver.cs:369-377) but only by the parked tilt path.
- `[Hands] GripPitchOffsetDegrees / HandLateralOffset / HandVerticalOffset / HandForwardOffset` — zero readers. NOTE: their descriptions still claim "read once as the seed"; the seeding was removed (HandsConfig.cs:205-211 ships measured defaults), so they are plain DEAD, not LEGACY-SEED — descriptions are one generation stale.
- `[Hands] {Glove,Plate,Arcane}{PitchTrimDegrees,LateralTrim,VerticalTrim,ForwardTrim}` (12) — zero readers, same stale-seed wording.
- 18 `[WorldUI] Map*` compositor knobs (MapAlbedoOriginalMaterial, MapAlbedoAmbient, MapAlbedoLight, MapCaptureMode, MapStripBeautify/VolumetricFog/SSAO/PostProcess/AllImageEffects, MapTexFlipX/FlipY/SwapDiag, MapUvSource/SwapUV/FlipU/FlipV/Channel/Component) — only declarations in FlatScreenStereo.1.State.cs, no readers.

SUSPECT (not dead, not clean):
- `[WorldUI] ScreenLeftMirrorFallback` — description says "no reader anywhere"; actually read as a gate at FlatScreenStereo.3.Map.cs:26 and :161. A `false` in a user's cfg silently disables the campaign-map fix. Should be made truly dead (drop the reads) or re-documented.
- `[Hands] HandColor` — read (HandVisuals.cs:711) but only affects the procedural fallback hand; correctly NotOffered.

## NORMAL candidates

Already curated (no work needed, listed to confirm): Compat/WallFade (×2), WallFade/SyncPeerFades,
Hands/PrimaryHand, Hands/HandStyle, Hands/LaserFingerOrigin, Compat/DisablePostProcessing,
Compat/DisableVolumetricFog, Rig/ForwardRendering, Rig/MenuRig, Rig/SpawnInCircle,
MixedReality/Enabled, MixedReality/KeyColor, Core/EnableGraphicsJobs, Core/AutoRestartForGraphicsJobs,
Comfort: TurnMode, SnapTurnDegrees, SmoothTurnSpeed, TurnHand, FlightEnabled, FlightDirection,
FlightMaxSpeed, FlightHand, FreeMovement, WorldGrabEnabled, VerticalDrag, RotateEnabled,
ScaleEnabled, RecenterHoldSeconds.

NOT yet curated but everyday-player material:
- `[RenderQuality] MsaaLevel`, `EyeResolutionScale`, `ForceAnisotropic` — the GPU quality trade; the Grafik tab currently has NO render-quality row at all since the old panel's preset cycle row lost its caller (RenderQuality.cs:683-696 accessors are orphaned). Restoring a preset row (Qualität/Ausgewogen/Performance/Minimum) would fit the existing curated style.
- `[WorldUI] StereoScreen` — visible 3D-depth toggle for the floating screen, localized, costs one extra menu-scene render.

## UNCERTAIN (one question each)

- `[Rig] VoidColor` — Soll die Leerraum-Farbe um die Menüs eine normale Spieler-Option werden, oder bleibt sie ein reines Debug-Werkzeug (Grau = Diagnose)?
- `[Compat] TutorialVRAdapt` — Soll die Tutorial-VR-Anpassung als normale Option sichtbar sein, oder immer an bleiben und nur unter Erweitert erreichbar?
- `[WallFade] StackedShellFade` — Soll "Festungs-Aufbauten mit ausblenden" als normale Option direkt neben "Wände durchsichtig" stehen, oder reicht Erweitert?
- `[RenderQuality] PixelLightCount` — Pixellichter (max) ist ein sichtbarer Qualitäts-/Performance-Regler: normale Grafik-Option oder Debug?
- `[Comfort] KeepPlaceOnReorigin` — "Platz nach Headset-Absetzen behalten": als normale Komfort-Option kuratieren oder unter Erweitert lassen?
