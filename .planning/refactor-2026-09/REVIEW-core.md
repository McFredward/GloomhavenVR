# REVIEW — lane core (2026-09 refactor)

> Base: `b40f8564` (the BRIEF commit). Worktree `/home/claw/gloomhaven_vr/.claude/worktrees/agent-a0714624b903f74e4`,
> branch `worktree-agent-a0714624b903f74e4`. Own guard baseline taken at HEAD (`baseline.rev` == HEAD).
> File set: `Core/` (all), `Rig/`, `Voice/`, `Compat/`, `Defaults/` — ~120 k lines.
>
> **Written incrementally** (coordinator instruction 2026-09-08: a partial review on disk survives a
> session-limit kill). Sections marked PENDING have not been read yet. Findings carry the brief §3.1
> fields: id | file:line | class | tier | evidence | action | guard expectation | risk. Ranked by risk
> reduced inside each sub-area; a finding NOT acted on still appears, with the reason.

## 0. Instruments, run at b40f8564 on the lane's paths

- `hygiene2.py`: 1 467 methods; 59.9 % ≤ 20 code lines, 86.3 % ≤ 50; 53 over 100, 9 over 200.
  Per directory over 100 code lines: WallFade 32/553, Perf 8/141, MixedReality 6/52, Environment 4/154,
  Haunt 4/114, root 3/125, Water 2/64, Sound 1/105, Diagnostics 1/23, SelfUpdate 1/76. Largest:
  `WallSegmentFade.Mounted.CollectWallMountedProps` 610 (nest 6), `WallSegmentFade.Tick` 377,
  `WallSegmentFade.Bind` 337, `LogOccluderVerdicts` 325, `PerfConfig.Bind` 294, `EnvSound.5.Shelf.LogBuilt` 256.
- `dupes2.py` (≥ 12 lines): Core 15 groups (10 cross-file, all inside `Core/WallFade` except one in-file
  pair `MixedReality.cs:1375-1387` / `:1526-1539`); Rig, Voice, Compat, Defaults: **0 groups**.
  The tool takes ONE path argument; the brief's plural `<your paths>` reads the second path as the window.
- `loadbearing.py`: 133 fields written inside diagnostic-named methods, 93 read outside. The guard's own
  `check-instrument-writes.py` reports **65** load-bearing at HEAD (the baseline file has 65 lines);
  the brief says 66 — one entry was retired between the 2026-08 baseline and ModBuild 480.
- Guard no-op control (`check --summary` at HEAD, nothing changed): my run produced `.guard/current`
  (11:59) but its printed summary was OVERWRITTEN by a sibling lane's run — the scratchpad directory is
  shared by the lanes of one session and both lanes chose the same file name. **Process finding**:
  lane-prefix every scratch file. The control is re-run before the first src commit.

## 1. Rig/ — READ IN FULL (19 files, 10 464 lines)

The 2026-07 review's Rig items are checked against HEAD first, because the brief inherits them:

| REVIEW-Net-Rig item | status at b40f8564 |
|---|---|
| R1 `_tailSteps` comment omitting `Rig.RenderQuality` | **FIXED** — `VRRigDriver.cs:527-555` carries the `FRAME-ORDER VRRigDriver._tailSteps [...]` marker (7 steps incl. `Rig.DepthPrepass`), byte-identical to `FRAME-ORDER.lock` |
| R5 `OnDestroy` restore order unguarded | **FIXED** — `VRRigDriver.cs:792-806` states the order and cites the invariant |
| R3 six "grip" descriptions in `ComfortSettings` | **FIXED except one residue** → RV-2 |
| D4 `ComfortGizmos` "vignette", "P3c", "Phase 4", seated presets | **FIXED** — `ComfortGizmos.cs:14-16` states the negative invariant; `Comfort.cs:18`, `ComfortSettings.cs:124` say the seated preset is gone |
| C4 partial split of `VRRigDriver` | **DONE** (6 parts). Its stated condition holds: `VRRigDriver.cs:1002-1017` names `VRRigDriver.WorldTilt.cs` and resets every tilt field |
| R2 `Recenter()` internal with no external caller | still `internal`; `grep '\.Recenter()'` outside `Rig/VRRigDriver*` = 0. Not acted on (one keyword, no reader benefit; the doc calls it "the comfort entry point") |
| "RigModule.Init AddComponent order looks load-bearing" non-finding | **RECORDED at the code** — `RigModule.cs:56-75` |

### Findings

**RV-1** | `Rig/ComfortSettings.cs:474-507` | risk-gap (asymmetric teardown) | Tier 3 (one added call) |
`Bind()` creates 22 `ComfortSetting<T>` wrappers (22 `= Bind(` sites, the last being `KeepPlaceOnReorigin`
at :435); `Unbind()` calls `Detach()` on **21** of them — `KeepPlaceOnReorigin.Detach()` is missing.
Input: `RigModule.Shutdown()` (hot reload). Output: 21 wrappers unsubscribe from their `ConfigEntry` and
null their `Changed`; the 22nd keeps its `SettingChanged` handler and its `Changed` subscribers. The
registry rule (INVARIANTS-Net-Rig "`ComfortSetting<T>` stores its handler so `Detach` can unsubscribe
exactly … `Unbind` calls `Detach()` on all wrappers") is broken. Blast radius is bounded:
`ModuleConfig.Create` builds a NEW `ConfigFile` per `Bind()` (`Core/ModuleConfig.cs:57-64`) and
`Register` replaces the registry entry, so the orphaned entry becomes unreachable; no live event can
reach the stale wrapper. The asymmetry is the defect, not a leak. Action: add
`KeepPlaceOnReorigin.Detach();` after `DebugGizmos.Detach();`. Guard: `CHANGED` confined to
`GloomhavenVR.Rig/ComfortSettings.cs`. Risk: nil. Hardware-observable: nothing (hot-reload only).

**RV-2** | `Rig/ComfortSettings.cs:429` | doc-drift, USER-FACING (cfg description) | Tier 1 (string literal) |
`SavedScaleMultiplier`'s description: "Written automatically after each two-grip scale gesture". The
gesture is two STICK-CLICKS since the P8 rebind (`WorldGrab.cs:8-10`; the same file's `WorldGrabEnabled`
text at :304: "THE BUTTON IS THE STICK CLICK, not the grip"). Last residue of REVIEW-Net-Rig R3.
Action: "two-grip" → "two-stick-click". Guard: `CHANGED` confined to `ComfortSettings` (a `Bind`
description literal is in the snapshot). Not a log token. Risk: nil.

**RV-3** | `Rig/SnapTurn.cs:126-135, 144-145` vs INVARIANTS-Net-Rig "Every stick-gate early return re-arms first" | doc-drift (registry wording) | not acted |
Two early returns do NOT re-arm `_armed`: the no-pose return (:126) and the world-grab return (:144).
Both are right — a clicked stick is a deflected stick, and re-arming there would fire a snap on the
frame the click releases. The registry sentence is wider than the code; the code carries its own
reason. Registry file is outside this lane → NEEDED-OUTSIDE note only if the integrator wants the
sentence narrowed to "every MODE-gate early return".

**RV-4** | `Rig/VRRigDriver.WorldTilt.cs` (whole file) | dead-looking, KEEP | — |
`TargetTiltDegrees => 0f` parks the feature (user ruling 2026-08, :14-25). Every path reduces to the
pre-feature no-op; `[Rig] WorldTiltDegrees` stays bound (§5 item 4). `WorldTiltRotation`
(`VRRigDriver.cs:448`) has no consumer (grep: only a comment in `WorldUI/Grab/PanelGrab.cs:35`) and
says so. Deleting any of it is the revival decision in reverse. Keep.

**RV-5** | `Rig/Flight.cs:123,150-163,273` | INSTRUMENT-WRITES entry `Flight::_idleReason <- ReportIdle` | Phase-5 outcome **1 (self-contained, keep)** |
The only outside read (:273, `if (_idleReason != ReelIdleReason)`) decides whether to COMPOSE the reel
attribution string — log plumbing, not a mechanism decision. Nothing to separate. No change.

**RV-6** | `Rig/LightStabiliser.cs:322,1152-1154,1509-1514` | INSTRUMENT-WRITES entry `LightStabiliser::_costTicks <- ReportWatch` | Phase-5 outcome **3 (move the write into the mechanism)** | Tier 2 |
`ReportWatch()` ends with the window reset (`_costTicks = _costFrames = _warmUpSkips = _excludedSkips = 0`,
:1511-1514); `DampAll` (the mechanism) accumulates `_costTicks`/`_costFrames` (:1153-1154). Moving the
four resets to `DampAll` immediately after the `ReportWatch()` call (:1159) preserves statement order
exactly (they are the callee's last statements) and makes `ReportWatch` pure text. Guard: `CHANGED`
confined to `GloomhavenVR.Rig/LightStabiliser.cs`; the baseline shrinks by one line. Risk: nil.

**RV-7** | `Rig/LightStabiliser.cs` (1 625 lines) | structure | not acted — one class, one algorithm, one shared record list, banner-sectioned. Charter §2 default.

**RV-8** | `Rig/RenderQuality.cs:1458-1512` | dead-looking, KEEP — `MsaaLabel/CycleMsaa/EyeScaleLabel/StepEyeScale` have no caller; the file says so (:1458-1465, citing `.planning/menu-audit/03-core-rig-perf.md`). §5 item 6 + a product decision.

**RV-9** | `Rig/VRRigDriver.HeadCamera.cs:102-119` `ResolveScenarioCamera` | negative — `Camera.allCameras` allocates, but only while unresolved and on the 30-frame cadence. Correct; recorded so it is not re-raised as "FindObjectsOfType-shaped".

Comment assertions tried against the source (Rig) — all held: `TearDownRig` resets every tilt field
(checked against `VRRigDriver.cs:345-515`); `RigPoseVersion` is bumped only by build/recenter paths,
never by `SnapTurn`/`WorldGrab`/`TickOriginGuard` (the last says so at `OriginGuard.cs:26-29`); the
arrival azimuth has one writer (`Recenter.cs:476-482`); `_tailSteps` matches `FRAME-ORDER.lock`; the
`ControlsProgress.Notify` / `TutorialVR.NotifyLocomotion` / `NotifyPlayerLocomotion` triple sits on every
rig-writing path (drag, rotate/zoom, turn, fly, lift).

## 2. Voice/ — READ IN FULL (6 files, 1 944 lines)

**VC-1** | `Voice/VoiceBadge.cs:112-123, 166-173`; `Voice/VoiceChatBridge.cs:231-233` | doc-drift (comments falsified by source) | Tier 0 |
`VoiceBadge`'s class doc: "ONE ITEM IN THAT CENSUS IS A REAL DEFECT … `VoiceSpatial.cs:458` is
`IsSpeaking(b.Voice) && !IsMuted(b.Voice)` … cannot be undone from here", repeated in `Tick`. At HEAD
`VoiceSpatial.cs:470` is `b.Speaking = VoiceChatBridge.IsSpeaking(b.Voice);` — the mute moved onto the
LEVEL (:458-469 explains the correction; :473). The defect the badge doc reports as open is fixed.
`VoiceChatBridge.IsMuted`'s doc (:231-232, "a muted peer makes no sound and must show none") states the
OLD rule the correction reversed. Action: rewrite the three comments to the shipped rule ("speaking is
the game's flag; the mute holds the level at step 1"). Guard: empty. (`git log -S` names the build.)

**VC-2** | `Voice/VoiceSpatial.cs:276-278` | risk-gap, latent, not acted |
`TryGetVoices` returning false (`service.PlayerVoices == null`) returns from `Tick` WITHOUT the
`StandDown` the `service == null` and `Count == 0` branches take, so a running feature keeps its ear
claim and applied sources. Reachable only if the game nulls a field-initialised list; resumes on the
next non-null tick. Recorded; a stand-down on a one-frame null would churn the ear.

**VC-3** | `Voice/VoiceSpatial.cs:316` | negative — `LiveVoices.Contains` in a `foreach` is O(n²) with n ≤ 4.

Comment assertions checked: "never writes `volume`/`Mute`" — true; "exactly one reflected door" — true;
`VoiceIcon` "no Unity type at all" — true (own `Sqrt`); `VoiceCurve` "asserted by the wire-test gate" —
`tests/GloomhavenVR.WireTests/VoiceCurveVectors.cs` exists.

## 3. Compat/ — READ IN FULL except the four small root files still in a persisted read (LoadoutHostingGuard, CardParticlesOff, InitialInputSkip, WallFadeDisable — folded in on the next commit)

**CP-1** | `Compat/HostingChainWatch.cs:403-461, 466-513` | INSTRUMENT-WRITES entries `HostingChainWatch::_pendingStack <- ReportAmputation`, `::_startedCount/_endedCount <- Replay` | Phase-5 outcome **4 (rename so the name stops lying)** | Tier 1 |
`ReportAmputation` is not a report: it consumes the pending amputation (:408), locates the thrower and
calls `Replay`, which INVOKES the skipped game listeners (:493) and forces a roster re-read (:506) — the
`a-write-inside-a-logger` shape exactly: a "delete the spent `Report*` methods" pass deletes the repair.
`ScenarioEntryWatch` in the same folder already made the split explicit (:289-293). Action: rename
`ReportAmputation` → `HandleAmputation` (private; its doc at :42-48 already says "IT ALSO REPAIRS THE
EDGE"). `Replay` stops being promoted (its only caller is no longer a diagnostic), so all three baseline
entries retire — the baseline shrinks by 3. Guard: `CHANGED` confined to
`GloomhavenVR.Compat/HostingChainWatch.cs` (names only). Log tokens untouched. Risk: nil.

**CP-2** | `Compat/ScenarioEntryWatch.cs` | negative, model — writes live in `Tick`, the three `Report*` methods are pure text (stated :289-293, verified). The approved game-state write is bounded exactly as its header says (:687-738).

**CP-3** | `Compat/CompatModule.cs:283-306` | negative — `FindObjectsOfType` per SCENE LOAD only.

**CP-4** | `Compat/Tutorial/*` | parallel-construction, not acted |
Five copies of the same three-line "error latch" (`_disabledByError` + try/catch + `VRLog.Error` with
stack) in `TutorialVR`, `TutorialGrabStep`, `TutorialCameraSkip`, `LevelMessageHeal`, `TutorialChainHold`
(`Degrade`). Each is the whole of its file's safety argument and each names its own consequence in the
log line; a shared helper would buy ~12 lines and lose the per-site sentence. Charter §4 P14 reasoning.

**CP-5** | `Compat/Tutorial/TutorialHintPatches.cs:352-354` | dead-looking, KEEP — `PilesAvailable() => true` is a constant guard kept "so the mod can never teach an interaction that is not there" (doc at :295-296). One branch, documented.

Comment assertions checked (Compat): `TutorialChainHold` "never the scenario-won/lost message" — the
test at :176-180 matches `CompleteLevel`'s own (verified against the decompile line cited);
`TutorialCameraSkip` "press bounded by page count" — `budget = pages + 1` (:217); `ScenarioEntryWatch`
"never the local player" — `ReferenceEquals(entry, me)` at :711.

## 4. Defaults/ — READ IN FULL (9 files, 1 887 lines). No number touched; structure and comments only.

**DF-1** | `Defaults/Defaults.Core.cs:264-266` | doc-drift (comment falsified by the value beside it) | Tier 0 |
The comment on `WalkInExitDwellSeconds` reads "`ExitDwellMovedSeconds` above is the dial the latch
borrowed; 2.5 is its shipped value, and the two are kept equal on purpose so the promotion is invisible
at the defaults." At HEAD `ExitDwellMovedSeconds = 0.5f` (:202) and `WalkInExitDwellSeconds = 2.5f`
(:266): the first was re-based from the user's cfg after the promotion (WALL-FADE-CLOSEOUT §2.2 records
exactly this: "his live cfg holds 0.5 … the walk-in release stop[s] tracking it"). The sentence is false
and would lead a reader to "restore" equality — a tuning change. Action: rewrite the sentence to say the
two WERE equal at the ModBuild 272 promotion and have since diverged by cfg re-base (0.5 vs 2.5), and
that neither may be moved to re-equalise them. Guard: empty (comment). Risk: nil. Same for
`ExitDwellStationarySeconds = 3.6f` (:203) vs the closeout's "7.00" — the closeout is the stale one there.

**DF-2** | `Defaults/Defaults.WorldUI.cs:140-144` | doc-drift (comment falsified by the value beside it) | Tier 0 |
"DEFAULT OFF for this build, deliberately … OFF is byte-for-byte today's rendering" sits directly above
`PanelSupersample = true`. Action: replace the paragraph with the current fact (shipped ON; the A/B
argument is history). Guard: empty. Risk: nil.

**DF-3** | `Defaults/` | negative — every `*_ByBoard` array is assembled from named entries (no literal;
rule at `Defaults.Core.cs:20-22` holds, checked in `Defaults.Cards.cs:415-435`); the two annotation-less
constants (`StackPitchFallback` :530, `EvalCadenceWhenUnsetSeconds` `Defaults.Core.cs:320`) say why they
are not config entries. `Defaults -> WorldUI` references (`WindowFaceMode`, `ButtonShape`, …) are enum
TYPES a default must name; not fixable from inside `Defaults/` and not worth an interface — recorded as
the layering smell the 2026-08 census already listed, no action.

## 5. Core root (26 files, 9 372 lines) — READ IN FULL

Read: ModuleConfig, MaterialLoaderHeal, UnseenTileOrder, GrabBar (GrabBarMesh), VRLog (+VRLogThrottle),
SceneRegistry, OcclusionFade (+PerspectiveWatch, OcclusionGate), GrabBarVisual, TmpFit, BundleShaders,
FollowPinAnchor, HeadEar, VRLayers, LayoutContentHeight, VRCameraPolicy, VRHeartbeat, CoreModule,
RichTextTags, EmbeddedTexture, VRPresenceWatch, FigureRendererGuard, StereoModeConfig, BuildInfo,
VRSession, IVRModule, GrabBarTexture.

### Findings

| id | tier | file:line | finding | action |
|----|------|-----------|---------|--------|
| CR-1 | 0 | `Core/GrabBar.cs:268-281` | An orphaned `<summary>` block sits directly above `QuantiseTiles`' own summary and describes the OLD shaft: "A UNIT-LENGTH shaft … The band is mapped ONCE rather than tiled". `Shaft` (:288-301) and `GrabBarVisual.SetLength` (:317-323) tile the band `length / TileLength` times. The block is a doc-drift leftover; the compiler accepts two `<summary>` elements on one member silently. | Delete the orphan block. The file is symlinked into the Unity preview project — comment-only change, safe on both sides. |
| CR-2 | keep | `Core/UnseenTileOrder.cs` (`LogState`) | Hand-rolled change-gate + 20 s heartbeat + 1.5 s minimum interval beside the shared `VRLogThrottle`. Not a duplicate: `VRLogThrottle` has no minimum-interval term, so a merge would change when the line prints. Baseline entry `_diagLastHash <- LogState`: the only outside reader is `Release()` resetting it to 0 — Phase-5 outcome 1 (gate-and-keep, self-contained). | none |
| CR-3 | discard | `Core/MaterialLoaderHeal.cs` (`Describe`) | `loadbearing.py` lists `Describe` as writing `_` — it is a C# discard, not a field. Checker false positive. | none; noted for §9 |
| CR-4 | verified | `Core/HeadEar.cs:52-59` | The class doc asserts `BoltVoicePlayerController` is dead code ("IVoicePlayer has no definition anywhere in the decompiled tree"). Checked at HEAD: `grep -rl IVoicePlayer decompiled/` returns only `GH.Runtime/VoiceChat/BoltVoicePlayerController.cs` itself. The assertion holds; the falsifier it names (an "N audio listeners" warning on a second client) stays the right one. | none |
| CR-5 | verified | `Core/VRCameraPolicy.cs:95-104`, `Core/MixedReality/MixedReality.cs:2456-2461` | Two `PruneDead` methods, each documenting why it is not the other's duplicate (different maps, MR additionally prunes its sky list and three latches). Agreed: the shared six-line fake-null idiom is not worth a generic helper on a scene-load path. | none |

Everything else in the root is clean: `Bind` orders intact, fake-null discipline observed, no per-frame
allocation in the drivers (`VRHeartbeat`, `VRPresenceWatch` reuse static lists), `BundleShaders` caches
successes only, `EmbeddedTexture` caches nulls on purpose and says so.

## 6. Core/Perf, Loc, MixedReality, SelfUpdate, Startup, Events, Diagnostics — READ IN FULL

### 6.1 Perf/ (9 files, 7 704 lines) — clean

PerfConfig's `Bind` order matches the shipped cfg layout; the ModBuild 227 one-shot is a marker, not a
force. TickGuard (throw-storm shape, 64-key cap), PerfMonitor (frame boundary at execution order
−30000, depth counter reset on every roll), PerfFrameSplit (+Zoom: index-aligned samples under one
gate), AutoLod (restore list, self-disabling host), PerfSceneProfile (rationed by its own measured
cost), PerfTextureCensus (rides the SCENE walk), LodGroupCensus (one sweep, two callers keep their
sentences). No dead code, no motion worth a commit. `PerfSceneProfile.cs:1336-1339` explains a design
choice in lane-ownership terms ("that file is not this lane's to edit") — historically true, the
decision still stands; left as is.

### 6.2 Loc/ (4 files, 6 435 lines) — API read; the two tables checked by script, not by eye

Method: `Loc.cs` (API + fan-out) read in full; `Loc.ConfigNames.cs` and
`Loc.ConfigDescriptions.German.cs` (5 261 lines of table) were checked by a script against
`.planning/refactor/.guard/surface.json` (412 bound keys) with the `FamilyKey` rules of
`Loc.ConfigDescriptions.cs:92-113` applied. Results: 512 `Pair` entries in `Loc.cs`, 0 with an empty
side, 0 duplicate keys, 5 EN==DE (`board`, `board_bronze`, `vr_board_bronze`, `mixed_reality`, `gold`
— all legitimate loan words). Every `Loc.Mod("…")` literal in `src/` resolves; the two non-resolving
prefixes `vr_board_` / `vr_style_` are concatenated ids, not gaps.

| id | tier | file:line | finding | action |
|----|------|-----------|---------|--------|
| LC-0 | §9 | `surface.json` vs `Cards/CardsConfig.cs:916`, `Hands/HandsConfig.cs:527,583`, `Board/FigureGrab/FigureGrabConfig.cs:691` | The surface census records LITERAL bind keys only. Every per-board / per-style / per-pile family is bound by interpolation (`$"BoardTilt_{board}"`, `$"{s}GripRollDegrees"`, `$"{s}PalmYaw"`, `$"FanStepDegrees_{pileNames[p]}"`), so all 61 wildcard entries in the two Loc tables that the census reports as "covering no bound key" ARE live. FigureGrab binds both the bare key (`:620`) and the family (`:691`), so its bare entries and its wildcards are both live. | none in src; brief-was-wrong entry |
| LC-1 | deferred (content) | `Core/Loc/Loc.ConfigDescriptions.German.cs` | 22 bound, menu-visible keys have NO German description (the browser falls back to the English bind text — a bilingual tooltip, the thing the file exists to prevent): `Cards/PokePadPixels`, `Net/DesyncPatienceSeconds`, `Optimize/AutomaticLodIdleSkip`, `Optimize/AutomaticLodSweepSeconds`, `Optimize/LodBias`, `Perf/LodCensus`, `RenderQuality/QualityPreset`, `WallFade/MaxEnterCells`, `WallFade/MinExclusiveCells`, `WallFade/SplitRunAdoptGroundScenery`, `WallFade/SplitRunUnified`, `WorldUI/BarHeightOffset`, `WorldUI/GrabBarTweenMs`, `WorldUI/SharedWindowArcRadiusMeters`, `WorldUI/WindowMaterialise`, `WorldUI/WindowMaterialiseAppearSeconds`, `WorldUI/WindowMaterialiseIntensity`, `WorldUI/WindowMaterialiseVanishSeconds`, plus the four markers under LC-3. Keys ConfigCatalog withholds (`Cheats/Enabled`, `Hands/HandColor`, `MixedReality/{HideSkyMeshes,OpaquePreviewTiles,UnseenBackingDebugColors,UnseenRegionMembership}`, `Optimize/Head*`, `SquareCaps/*`, `TransientButtons/*`, `Rig/MapPresentationMigrated230`) are excluded. | Translation work, not structure: 18 German paragraphs against long English bind texts. Not a refactor-lane change; listed for the owner of the next Loc round. |
| LC-2 | deferred (content) | `Core/Loc/Loc.ConfigNames.cs` | 40 bound, menu-visible keys have no display name (the row shows the spaced English key): the LC-1 set plus `Cards/{BoardPitchMaxDegrees,BoardPitchMinDegrees,ConfirmUndoInsetX,FanArcDegrees,HeldTiltDegrees,RestButtonInsetX,RoundButtonDiameter,RoundButtonThickness,TrayTilt}`, `FigureGrab/{HeldFaceYawDegrees,HeldOffsetForward,HeldOffsetSide,HeldOffsetUp,HeldTiltDegrees}`, `Rig/{MaskedReaimDeadband,MaskedReaimGain,MaskedReaimHeadRate,WorldTiltDegrees}`. `VROptionsTab.4.Curated.cs:1186` already treats `BarHeightOffset`'s fall-through as known. | Same as LC-1. |
| LC-3 | NEEDED-OUTSIDE (worldui) | `WorldUI/Options/ConfigCatalog.cs:470-490` | The catalog withholds one-shot migration markers by an explicit per-key table; `PeerBoardFade/DwellsMigrated312`, `Perf/ProfileDefaultsMigrated227`, `WallFade/WallFadeBarsMigrated252`, `WallFade/WallFadeBarsMigrated256` are bound (per surface.json) and not in that table. If no other rule hides them, they sit in the menu with the exact harm the table's own comment describes for `Rig/MapPresentationMigrated230` ("false re-arms the migration"). | Verify + add to the withheld table — WorldUI lane. |
| LC-4 | keep | `Core/Loc/Loc.cs` (`ctl_cant`) | Retired button caption kept because `check-surface.py` reads SHOUTED literals as log tokens; the comment says exactly that and names the removal recipe (delete + fresh baseline). | none this round (a baseline refresh is not a lane action) |

### 6.3 MixedReality/ (3 files, 3 511 lines)

| id | tier | file:line | finding | action |
|----|------|-----------|---------|--------|
| MR-1 | 0 | `Core/MixedReality/MixedReality.cs:368-381` | Two orphaned `<summary>` blocks ("Default for '[MixedReality] UnseenRimInset' … LOCAL FALLBACK: this branch does not own Defaults/Loc … swap this for `Defaults.UnseenRimInset` then" and the `UnseenRimTopClearance` twin) with NO member beneath them — the constants moved to `Defaults.Core.cs` and the code already reads `Defaults.UnseenRimInset` / `Defaults.UnseenRimTopClearance` (`:1939-1940`, `:1242-1244`). The summaries now attach to nothing and state a debt that is paid. | Delete both blocks. |
| MR-2 | 0 | `Core/MixedReality/MixedReality.cs:2064-2066` | Comment in `SyncUnseenUnderlays`: "The base quad lives under the scene-root holder, NOT under the source — it never dies structurally with the piece and must go explicitly". The base quads were removed (user ruling, `:1001-1004`); the fill IS a child of the source (`:1884-1885`) and the very next comment (`:2067`) says so. The sentence is a leftover from the rectangular-quad round. | Replace with one sentence: the fill and rim are children like the plate, destroyed here only for the component-removal case. |
| MR-3 | verified | `MixedReality.cs:28-33, 645-646` | "`Tick` is the LAST entry of `VRRigDriver`'s tail-step array" — `Rig/VRRigDriver.cs:546-554`: seventh of seven, after `Rig.HeadClearColor` and `Rig.DepthPrepass`. Holds. | none |
| MR-4 | verified | `MixedReality.cs:1772-1773` | `IsFigureOrActorRenderer` forwards to `FigureRendererGuard` (survey R9 done); the held-prop clause is present via `HeldByPlayer`. | none |

`MixedReality.Diag.cs` and `MrRimCurtain.cs` are clean (one-shot instruments, gated on a settled
backing count; mesh cache released with the underlays).

### 6.4 SelfUpdate/ (8 files, 2 688 lines)

| id | tier | file:line | finding | action |
|----|------|-----------|---------|--------|
| SU-1 | 0 | `Core/SelfUpdate/SelfUpdateModule.cs:13-16` | "REGISTRATION … Until the single line `_modules.Add(new Core.SelfUpdateModule());` is added [to `Plugin.RegisterModules`], nothing in this feature runs". `Plugin.cs:1095` has that line. The paragraph describes a state that no longer exists. | Rewrite the paragraph to state where it is registered. |
| SU-2 | 0 | `Core/SelfUpdate/SelfUpdateZip.cs:110` | `Verify`'s doc bullet "every entry is under `BepInEx/` or is `INSTALL.txt`" — the same file's `AllowedRootFiles` (`:61-65`) has two entries since 2026-09-03 and its own comment records that the single-file assumption was a shipped regression. | Bullet: "or one of `AllowedRootFiles`". |
| SU-3 | 0 | `Core/SelfUpdate/SelfUpdateConfig.cs:88-101` | `IsCheckEnabled`'s summary ("True when the update check may run, with the reason either way…") is followed by a second `<summary>` and attaches to `IsDevBuild`; `IsCheckEnabled` (`:104`) has none. | Move `IsDevBuild` + its summary above the `IsCheckEnabled` summary. |
| SU-4 | 1 | `Core/SelfUpdate/SelfUpdateConfig.cs:26-38`, `Defaults/Defaults.Plugin.cs:135-138` | INTEGRATOR NOTE: the shipped default sits here because "that directory is not this lane's to touch"; house convention is `Defaults.*.cs` with a `// => [Section] Key` annotation for `scripts/rebase-defaults.py`. Defaults is this lane's now. | Add `internal const bool UpdateCheckOnDevBuilds = false; // => [Dev] UpdateCheckOnDevBuilds` beside the other `[Dev]` entries; make `UpdateCheckOnDevBuildsDefault` forward to it (public shape unchanged, value unchanged); rewrite the note. Consts inline, so the guard reads empty. |
| SU-5 | NEEDED-OUTSIDE (csproj) | `Core/SelfUpdate/SelfUpdateZip.cs:11-24` | The whole reflection layer over `System.IO.Compression` exists because two `<Reference>` lines are missing from `GloomhavenVR.csproj`, a file this lane does not own. The comment already states the exact two lines. | Record in NEEDED-OUTSIDE-core.md; the file collapses to ~30 lines once they land. |

`SelfUpdateApplyScript` (pure string work, ASCII-guarded), `SelfUpdateCheck` (one falsifier line,
capped attempts), `SelfUpdateInstaller` (every byte inside staging until handover), `SelfUpdatePaths`
(confirmation gate on `pending.txt`), `SelfUpdateRelease` (host allow-list, non-throwing JSON) are
clean.

### 6.5 Startup/ (4 files, 1 136 lines)

| id | tier | file:line | finding | action |
|----|------|-----------|---------|--------|
| ST-1 | 0 | `Core/Startup/OpenXRBootstrap.cs:104-110` | `LogGraphicsJobs` carries TWO `<summary>` blocks; the first ("One-time environment fingerprint. Desktop OpenXR only supports D3D11 …") is `LogEnvironment`'s (`:181`), which has none. | Move the block onto `LogEnvironment`. |
| ST-2 | 0 | `Core/Startup/RuntimeDepsLoader.cs:86-113` | `InvokeRuntimeInitializers`' long summary ("Replays Unity's `[RuntimeInitializeOnLoadMethod]` pass … Inventory of the shipped RuntimeDeps …") is attached to the `InitializersInvokedKey` const (which has its own summary right after); the method (`:116`) has none. | Move the const + its summary above the method's summary. |

`OpenXRDiagnostics` and `OpenXRRuntimeRegistry` are clean; the candidate ordering (system default first,
SteamVR last) matches its own rationale.

### 6.6 Events/ (5 files, 1 110 lines)

| id | tier | file:line | finding | action |
|----|------|-----------|---------|--------|
| EV-1 | 0 | `Core/Events/VREventsModule.cs:7-10` | The class doc lists three observation patches; `Init` applies four (`UIWindow_Transition_Patch`, `:37`). | Add the fourth to the list. |

`VRModeStateMachine` (D4 rest-message predicate present with its `MODE REST REFUSED` token),
`VREvents`, `GameEventBridge`, `GameEventPatches` are clean. No Harmony target is touched by this lane.

### 6.7 Diagnostics/ (5 files, 939 lines) — clean

`ViewConeProbe` (Alert on the watchdog branch, documented), `DevConsole`/`DevModule`,
`TeardownReport` (two-frame delay, reasoned), `BundleDiagnostics`, `ExceptionTraces` (re-asserted per
scene load, restored on shutdown). Nothing to act on.
## 7. Core/Sound, Haunt, Environment, Water

### 7.1 Environment/ (6 files, 8 362 lines) — READ IN FULL

| id | tier | file:line | finding | action |
|----|------|-----------|---------|--------|
| EM-1 | 0 | `Core/Environment/ElementMood.cs:1271-1292` | `LogEdge`'s summary ("THE line. EDGE ONLY — one per actual change of the six columns…") is followed by a second `<summary>` and attaches to `LogGrowth`; `LogEdge` (`:1427`) has none. | Move the block onto `LogEdge`. |
| EM-2 | 0 | `Core/Environment/ElementMood.cs:432-436`, `:1002` | "The part with a phase to get wrong — the waning BREATH — is an absolute function of the shared clock and is therefore already identical on every client" (and "the breath" in the downstream list at `:1002`). The breath was deleted on 2026-09-06 (`TargetFor`, `:808-826`; class doc NO ENVIRONMENT EFFECT MAY BLINK); the sentence describes a term that no longer exists and contradicts the paragraph at `:253-260` that says so. | Rewrite: nothing on the channel carries a phase any more; the six anchor their own ramps. |
| EM-3 | 0 | `Core/Environment/ElementMood.cs:278-279`, `Core/Environment/SkyAlternative.cs:793, 1024, 1031` | Four citations of `MixedReality.cs:684` (the MR-OFF call) and `:692` (the MR-ON `StandDown`). At HEAD `SkyAlternative.Tick()` is `MixedReality.cs:678` and `SkyAlternative.StandDown()` is `:686`; the citations at `SkyAlternative.cs:913, 950` already carry the right numbers. | Correct the four numbers. |
| SA-verified | — | `SkyAlternative.cs:725-779, 903-945, 2831-2894` | The ModBuild 229/230 chain is closed at HEAD: `MinFarWorldUnits` gates on the branch roots, `_active` is set before the four isolated optional steps, `DespawnEnvironment` stands `EnvSound` down on the ordinary path. `WireStyleCode` reads `_active && _skyGo != null`, consistent with `Deactivate`. | none |
| DW-1 | 0 (log prose) | `Core/Environment/DoorOpenWatch.cs:1321, 1336-1337` | Two `READING:` tails in `LogClipSample` — "The hide falls back to the wall-shader route." and "the picture must be made by the mod, which is what the wall-shader fallback then does." — describe the stand-in the class doc (`:89-96`) says was removed at ModBuild 429 ("nothing in this file writes `Renderer.enabled` or `SetActive` on door content, ever"). The SHOUTED tokens on the line (`DOOR OPEN CLIP SAMPLE`, `PLAYABLE GRAPH`, `CLIP AND ITS PATHS`) are untouched by the fix. | Replace the two tails with what is true now (the door's picture is the game's; the instrument only names the side). |

`DoorLightPlates` (renderer flag only, write-war counter), `ApparanceDetailFocus` (restores only what
is still ours), `SkyBackdrop` (mechanism self-selected; `RemoveEffects`/`FullReset` layered and
documented as non-duplicates) are clean. `DoorOpenWatch` is instrument-heavy and internally
consistent: the three-phase probe, the clip sample (writes and restores, never overlapping the
window), the belt with its per-instance ledger, `_probing` closed on every teardown path.

### 7.2 Water/ (4 files, 4 923 lines) — READ IN FULL, clean

`WaterReflectionCaps`, `WaterEdgeBand`, `WaterOwnSurface` are pure and wire-tested (never-raise
invariants, the C# mirror of the swell field). `WaterTerrainVR`: material instances owned and
destroyed on every release path, bounds pad from the amplitude CEILING, `OwnSurface` flips are a
release-and-re-adopt, the respawn seam is a postfix that only queues. Its log scope is `Compat` by
design (`:173-175`). No findings.

### 7.3 Sound/ (8 files, 10 224 lines) — READ IN FULL

Read line by line: `GameAudio`, `EnvSound.1.Core` … `EnvSound.5.Shelf`, `EnvSoundSchedule`, and the
bank's enum, properties, `Build`/`Release`/`MeasuredShape`/`Finish` and the five DSP helpers
(`EnvSound.Bank.cs:1-820`). The 26 generators (`:820-4145`) were checked structurally rather than
sample by sample: every one is a `for` over a fixed count and the file contains **zero** `while`/`do`
statements, so the ModBuild 145 freeze class (a shrink-factor accumulator that underflows) cannot
recur there. The mechanism is sound throughout: every scheduled cue is a pure function of the shared
clock through `EnvSoundSchedule.TrySlot`, the join re-arms on a clock discontinuity, teardown resets
every field including the gates, the voice cap is counted against the pool about to exist, and the
one loud cue is clamped against its own stated ceiling.

| id | tier | file:line | finding | action |
|----|------|-----------|---------|--------|
| SD-1 | 0 | `Core/Sound/EnvSound.Bank.cs:276-281` | `Bed`'s summary: "both of them are hard-zeroed by `EnvSound._windGate`, so this clip is INAUDIBLE whenever the Air element is down … `TickBeds` logs it as a defect rather than playing it". Stale since ModBuild 223: `WindBed` "NEVER RETURNS ZERO" (`EnvSound.2.Tick.cs:303-308`), the wind beds rest at `WindRestFloor` and are never paused, and the `WIND LEAK` line warns while the bed keeps playing. | Rewrite: led by Air above the resting floor; the check is a warning, not a mute. |
| SD-2 | 0 | `Core/Sound/EnvSound.Bank.cs:21-27` | `EnvSoundClip.Bed`: "every emitter that plays this clip is hard-gated on the Air element … there is no wind SOUND without wind". Same staleness; the ruling was narrowed at 223 (`EnvSound.1.Core.cs:540-567`). | Same rewrite. |
| SD-3 | 0 | `Core/Sound/EnvSound.5.Shelf.cs:125-133` | `ScheduleShelfContacts`' `start` doc: "the caller deliberately allows a cue to fire late (up to the whole run plus 1.5 s), so `now` can be twenty seconds past the start". That is the pre-ModBuild-150 behaviour; `TickHaunt` now skips the whole event past `DeferredStaleSeconds` (2 s) — its own MID-FLIGHT JOIN block (`EnvSound.4.NightCalls.cs:967-1005`) says so. The conclusion (pass `start`, never reconstruct it) still holds; the premise is wrong. | Restate the premise: the cue may be up to 2 s late, and the ABSOLUTE times are what let a late joiner drop cleanly. |
| SD-4 | 0 | `EnvSound.1.Core.cs:11`, `EnvSound.Bank.cs:4`, `EnvSound.5.Shelf.cs:1014` (log prose) | Three path citations predate the `Core/Sound/` move: `Core/EnvSound.Bank.cs`, `Core/EnvSoundSchedule.cs`, "in Core/EnvSound.cs" (`LogBuilt`'s AT REST clause). `Core/HeadEar.cs` cites (`:1256`, Shelf `:942`) are correct — the file is there. | Correct the three paths (`Core/Sound/EnvSound.Bank.cs`, `Core/Sound/EnvSoundSchedule.cs`, `Core/Sound/EnvSound.1.Core.cs`). |
| SD-5 | 0 | `Core/Sound/EnvSound.4.NightCalls.cs:136-140, 155-160` | `NightCallDeck`'s summary opens "TWENTY cards at ModBuild 242, up from sixteen" with exact-twentieth shares; the ModBuild 246 block beneath it (`:178-199`) withdrew four cards and the field holds SIXTEEN. The 246 block is correct and the summary's headline is not. | One sentence at the top of the summary: sixteen since 246 (the 242 table below is the before column). |
| SD-6 | 0 (log prose) | `Core/Sound/EnvSound.5.Shelf.cs:1101-1129` | `LogBuilt`'s SwampNight clause still prints "a KeWick, a Raven, a Fox, a RoeDeer and an OwletBeg", "a Howl (wolf, far), a BarnOwl" with their gains, and "(4/3/3/2/2/2/1/1/1/1 of 20 — the fox, the roe deer, the wolf and the barn owl are one card each…)" beside a dynamically printed `NightCallDeck.Length` of 16. Four of those voices are withdrawn (ModBuild 246) and never built. No SHOUTED token in the affected sentences. | Print the six dealt voices and their shares of `NightCallDeck.Length`; drop the four withdrawn names. |
| SD-7 | 0 — DEFERRED | `Core/Sound/EnvSound.Bank.cs:387-399, 3576-3948` | `Fly` documents its own deletion condition — "if a round goes by with the catalogue STABLE and nothing claiming this clip, delete `MakeFly`, this property and `EnvSoundClip.Fly` together" — and it has been met many times over (the catalogue has not moved since ModBuild 149; no consumer outside the bank). But the bank's standing rule is that a deleted generator is preserved sample-for-sample in `.planning/envsound-replica/room.py`, which has no `make_fly` (it has stone, night_air, chirr and the calls), and that file is outside this lane's set. | Not in this round. Hand-off: port `MakeFly` to `room.py`, then delete the ~370 lines, the property, the enum member, the `Bank` arm and the `Release` null. |

`EnvSoundSchedule` (Unity-free, wire-tested), `GameAudio` and the deck/perch/window arithmetic are
clean. Two doc paragraphs are dated history rather than contradictions and are left alone: the bank's
COST paragraph (`:219-248`, "31 clips / 9.3 MB" measured at 242, 27 built since 246) and the 241/242
level tables in `EnvSound.4.NightCalls.cs` (their rows for the withdrawn voices are the before
column, which the 246 block says explicitly).
### 7.4 Haunt/ (8 files, 8 840 lines) — READ IN FULL

Mechanism clean: one writer per shader global with NaN rejection and write-on-change; the latch
loops by re-anchoring `_forceSince` in whole loops; `Resolve` is the character-for-character C#
mirror of the cginc cascade (float, same association, `Frac` not `Repeat`); the roster excludes DLC
by construction; the albedo lever promotes a material only on READ-BACK; the clone is born inactive,
stripped twice, and released in the one order that does not leak (child handle through the public
helper, materials destroyed explicitly, `DeinitializeCharacter` deliberately not called). The
findings are doc drift and one unreachable branch.

| id | tier | file:line | finding | action |
|----|------|-----------|---------|--------|
| HT-1 | 0 | `Core/Haunt/Haunt.Schedule.cs:15-16`; `Core/Sound/EnvSound.1.Core.cs:32-35` | Both say `Haunt.cs:74-75` "still quotes 'ohne sound' as the reason the apparitions are silent" / "publishes no audio channel — that remains true of THAT file". `Haunt.cs:74-75` is now the LOCAL SETTINGS paragraph; the quotation is the original request at `:18`, and `:96-102` is headed "NO SOUND, ever — WITHDRAWN" and records the reversal itself. | Rewrite both sentences: the class doc records the reversal; the request is quoted at `:18` as history. |
| HT-2 | 0 | `Core/Haunt/HauntFigures.Roster.cs:429-438`; `Core/Haunt/HauntFigures.Clone.cs:1180-1183` | "only correct because `EnvSound` owns the `AudioListener` … `EnvSound.TakeListener` (EnvSound.cs:1056-1090) moves the listener onto the VR head". Since ModBuild 297 the ear is `Core/HeadEar.cs`; `EnvSound.TakeListener` (`EnvSound.5.Shelf.cs:550-553`) is `HeadEar.Claim("EnvSound")`, and spatial voice chat claims the same ear. The gating argument survives (EnvSound claims while its switch is on). | Name `HeadEar` and the claim; correct the citation. |
| HT-3 | 0 | `Core/Haunt/HauntFigures.cs:43-48`; `HauntFigures.Events.cs:256-258`; `HauntFigures.Clone.cs:1566-1567, 1896-1897` | Stated as current fact: "a fiftieth in the cellar and a thirteenth in the wood since the two ModBuild 148 photographs (HauntFigures.Clone.cs, THE DARKENING)"; "roughly a fiftieth of its albedo"; "multiplied down to a fiftieth of their albedo (see Shade)"; "(0.015 in the cellar, 0.049 in the wood)". The ModBuild 153 re-fit lives in `HauntFigures.Math.cs:330-405` — 0.120 cellar, 0.200 wood, floor 0.100 — and Clone.cs's own THE DARKENING block ends "EVERYTHING ABOVE THIS LINE IS HISTORY — not one of its numbers is live" (`:2934-2935`). | Correct the four sentences to the Math.cs figures and cite Math.cs. Leave the derivation history and the worked examples that name the build they describe. |
| HT-4 | 0 | `Core/Haunt/Haunt.Schedule.cs:233`; `HauntFigures.Events.cs:867` | "(EnvSound.cs, "Fire once per (start, card)")" is `EnvSound.4.NightCalls.cs:920`; "`EnvSound.HauntPosition` (EnvSound.cs:1001-1022)" is `EnvSound.5.Shelf.cs:373-395`. | Correct both paths. |
| HT-5 | 0 | `Core/Haunt/HauntFigures.cs:96-103` | Class doc: "the DOOR that opens at the top of the stair and closes again (2 — light where there was none, no body at all)" and "In the forest only the two eyeshines (0) are left to the shader" — both deleted at ModBuild 149; `Haunt.IsInert` (`Haunt.cs:384-415`) names them as placeholders in which nothing happens. | Say the two are inert placeholders. |
| HT-6 | 0 (user-facing, EN+DE) | `Core/Haunt/Haunt.cs:178-209`; `Core/Loc/Loc.ConfigDescriptions.German.cs` (`Haunt`/`EasterEggs`) | The `EasterEggs` description still promises "eyes that open in the undergrowth and blink once" and "a dim warm door opening at the top of the stair and closing again"; the comment above it says "A description that names an event the player will never see is the most convincing kind of wrong" and its catalogue line still lists "the lit door at the stair top" and "the eyes". Both events were deleted at ModBuild 149 (`Haunt.IsInert`). **The DE text (`Loc.ConfigDescriptions.German.cs:447-470`) is two rounds further behind**: it still promises the rejected window walk-past ("von dem du nur die Beine siehst"), the rejected stair walk ("etwas zu Großes, das oben durch die Treppentür geht"), the face behind a tree deleted at 147 and the eyes deleted at 149 — four events a German player is told to expect and will never see. | Drop the two events from the EN text and the catalogue comment; re-translate the DE catalogue sentence from the corrected EN. Key, default and wire untouched; only the .cfg comment text changes. |
| HT-7 | 0 | `Core/Haunt/HauntFigures.Clone.cs:2476-2482` | Two consecutive `<summary>` blocks before `Shutdown`; the first ("Release the resident prefab ASSET handle…") belongs to `ReleasePrefab` (`:2497`), which has none. | Move it onto `ReleasePrefab`. |
| HT-8 | 0 (dead branch) | `Core/Haunt/HauntFigures.cs:471-474` | `else if (_card >= 0 && want < 0) Retire("the event ended");` is unreachable: with `_card >= 0` and `want < 0`, `want != _card` holds and the first branch has already fired with the same reason string. | Delete the branch. CHANGED confined to `TickBody`; no behaviour change. |
| HT-9 | 0 (log prose, optional) | `Core/Haunt/Haunt.cs`, `Force`'s refusal string | `!ScenarioBoardExists ? "there is no scenario board"` while `ForceReady` tests `TableInFrontOfPlayer` (board OR map room since ModBuild 177); `Tick`'s stand-down says "no scenario board and no mod room". Incomplete rather than wrong. | Low priority; align the wording if touched. |

Verified exact: `Events.cs:539` → `Haunt.Schedule.cs:188-198`; `Events.cs:378` → `:73`;
`Events.cs:1133-1134` self-cites `:288`/`:326`; `Clone.cs:1180` → `Haunt.Schedule.cs:19`;
`HauntFigures.Math.cs` is the wire-tested copy (not a mirror) of the three pure functions.
## 8. Core/WallFade (33 files) — READ IN FULL

`WALL-FADE-CLOSEOUT.md` read first. The fade BEHAVIOUR is CLOSED on hardware (user, ModBuild 283),
so **this lane took Tier 0 only here**: every edit below that was applied is a comment, and the
guard confirms it — after the WallFade batch, `WallSegmentFade` appears NOWHERE in the compiled-form
diff. Everything that would touch a statement is recorded as a work list and NOT done.

### 8.1 Phase-5 table — all 37 WallFade entries in `INSTRUMENT-WRITES.baseline`

The baseline is keyed by the NESTED class name, so `grep WallFade` on it finds nothing: the entries
are `FadeDriver::<field> <- <Method>` (33) and `FadeWrite::<field> <- FadeWrite` (4), lines 21-57.

| group | entries | outcome | evidence |
|-------|---------|---------|----------|
| 13 × `LogRescanBudget` | 21-32, 53 | **1 — gate and keep** | Each is accumulated or running-maxed in the mechanism and reset in the logger's window block (`WSF:5516-5559`). The "non-diagnostic reader" the checker sees is the `if (x > _cycleWorst…)` compare itself, whose only dependent branch is the max-update. **N9 RESOLVED: no decision reads any of the thirteen.** |
| 7 × `LogMountedCensus` | 33-39 | **3 — move the write into the mechanism** | Writes are the first seven statements of the logger (`Mounted.cs:4013-4019`); the readers are the change trigger at `Mounted.cs:3027-3043` (plus `WSF:2087-2088`). Precedent in the same file: ModBuild 410 already stamps `_lastLoggedHangingPlants` in the caller at `Mounted.cs:3046`. Exact move: delete `:4013-4019`, insert after `LogMountedCensus();` at `:3045`. Order-safe. |
| 2 × `LogStackedCensus` | 40-41 | **3** | Writes `Stacked.cs:1933-1934`; readers `Stacked.cs:1191-1192` (mechanism), resets `WSF:2089-2090`. Exact replacement text in the helper's findings. |
| 1 × `LogWalkInsideEdge` | 52 | **3** | Write `Inside.cs:2106` (the logger's first statement); reader `WSF:2716` (`if (_walkEdgePending)` in Tick). Pure motion. |
| 5 × `AuditOneCacheWall` | 43-47 | **1** | The only consumer is the WALL-PATH AUDIT's own backoff signature (`WSF:8065-8089`), which is already Quiet- and Idle-gated. Self-contained. |
| `_showEdgeUnitReturn <- AuditShowEdge` | 49 | **1** | The one mechanism touch is a lifetime `.Clear()` at `Mounted.cs:1541` (teardown). No value is ever read out of it by the mechanism. |
| `_subtreeScratch <- LogGeneratedContentChildren` | 50 | **1** | The logger runs from the heartbeat, outside the commit; every mechanism user clears the list before filling it. |
| `_unionRaisedPropFrames <- LogMountedUnion` | 51 | **1** | The reset IS the printed "SINCE THE LAST LINE" window and is bound to the print; mechanism reads are deltas only. Moving it would change what the line means. |
| `_live <- LogInsideState`, `_roomTileFootprint <- LogSampleGridCensus` | 42, 48 | **FP** | No write exists. The checker's `split_assignments` pass reads a comma DECLARATION as an assignment chain (`Inside.cs:2709`, `WSF:9214`). Retire only together with the zero-risk edit that splits each declaration in two, or the checker re-adds them. |
| 4 × `FadeWrite` ctor | 54-57 | **FP (clearing edit = 4, rename)** | The checker treats a constructor as a diagnostic because it is named like its type, then resolves readers by BARE FIELD NAME (`Fade`, `Owner`, `Path`, `R`) across the tree. The `SolidOwner` struct at `FadeCensus.cs:371-395` documents this exact cause and the precedent fix is a rename. |

**Net if the work list is taken: 37 entries → 7** (→ 0 with the optional `LogRescanBudget` split into
`BudgetLineDue` / `LogRescanBudget` / `ResetBudgetWindow`). **None of it was done this round** — a
write move is a statement move, and the closeout permits Tier 0/1 only here.

### 8.2 N4 — settled, and the closeout's §8 open question with it

`_samplePos` / `_sampleRay` have no reader outside `BlockedFraction`. `_sampleVisible` has exactly
one: `PieceBlockedSamples` (`Inside.cs:1343`), which is diagnostic. Every MECHANISM read happens on
an evaluate tick AFTER that tick's rebuild, which is after the commit (`WSF:2313` commit, `WSF:2401`
`UpdateSampleVisibility` under `evaluate`, `WSF:2613-2615` the decide loop under `else if
(evaluate)`). So "index-aligned to AllSamples with no invalidation path" **cannot reach a decision**.
The one real staleness is diagnostic and now says so in source (C16).

### 8.3 Applied this round — Tier 0 comments only

| id | file:line | what was wrong |
|----|-----------|----------------|
| C1 | `WallSegmentFade.CommitGate.cs:23-27` | "CollectWallMountedProps DRAINS and CLEARS `_mountedTouched`/`_mountedAnchorLedger`/`_mountedMobile` at the top of its pass" — it does not. Those three are CROSS-COMMIT ledgers cleared only on teardown or at their caps; the pass clears `_mountedOwned`/`_attachmentOwned`/`_mountedReleased` and restores what it no longer owns. The refusal's CONCLUSION survives; the mechanism sentence did not. |
| C2 | `WallSegmentFade.cs:2209-2213` | The explicit `ReleaseSamplingSuspension` is guarded by `!_samplingSuspended` and the line above already released the latch (`ResetInsideBoardState` → `ReleaseWalkInside`), so its cause string can never print. Marked a backstop. |
| C3-C6 | `WallSegmentFade.PropUnit.cs:350, 362, 370, 906` | Four references to `BeginStandingPropScope`, a method PERF S4 split away. C4's parenthetical was doubly wrong: the node-fact window opens in commit phase FIVE, so `_nodeFactsActive` is FALSE for phases 1-4. (`STALE-DOC-REFS.md`'s own line numbers for these — 255/267/275/800 — are themselves stale.) |
| — | `WallSegmentFade.cs:2025` | The same dead symbol once more, on `_standingScopeOpen`. The name is now absent from the whole repository. |
| C7 | `WallSegmentFade.Standing.cs:169, 212` | **A mangled paste**: the sentence twice on one line with a literal `///` inside the doc text, two `<para>` opened where one is closed, and a stray `</para></summary>` at :212 accidentally balancing it. |
| C8 | `WallSegmentFade.Standing.cs:575-583` | Two consecutive `<summary>` blocks on one method — the first is the doc of an overload ModBuild 268 folded away. Merged. |
| C9 | `WallSegmentFade.FreeStanding.cs:43-45, 450-457` | Both sites claimed "every member kept has its top ≥ 2.5 wu over that floor". FALSE for the AIRBORNE arm, which is admitted on its FOOT alone; the 2.5 wu bar is asked of the UNION. The conclusion (no member in the ground band) survives for a different reason, now stated. |
| C10 | `WallStandingProp.cs:274-278` | "A constant so the census can COUNT that refusal" — since ModBuild 279 the census counts off `FloorVerdict.WallFeatureFragment`, never the string. |
| C11 | `Core/OcclusionFade.cs:34-37` | Cited a type that **exists nowhere in src/ or tests/** (`FadeSnapshot`; it is `WallCommitDiff.SegmentFacts`), and said both shadow copies are "carried across the sliced commit" — the sliced commit is not built. |
| C14 | `WallCommitDiff.cs:18`, `WallStandingProp.cs:416`, `WallSegmentFade.Standing.cs:1047` | Three copies of the pre-ModBuild-280 "94.8 ms" commit cost. All three DATED (72.84 ms after 281) rather than silently restated, because each sits inside a dated round record. |
| C15 | `WallCommitDiff.cs:493` | "~1,900 owned renderers" against the closeout's 2,540. |
| C16 | `WallSegmentFade.Inside.cs` | ADDED the N4 residual to `PieceBlockedSamples`' doc — the flags it reads are the last evaluate tick's, misaligned if a `RebuildSamples` renumbers the grid in the same commit. Diagnostic only, and the doc now says why. |
| C17 | `WallCommitDiff.cs:471-475` | `OwnKey` packs the class into the low THREE bits; the invariant `KindCount ≤ 8` was nowhere stated. |
| C18 | `WallSegmentFade.HoldQuery.cs:152-155` | Two arms that can never be true for an external caller (both containers are per-census-call scratch). Not a defect; now labelled. |
| C19 | `WallSegmentFade.Stacked.cs:1103-1105` | `RestoreSegmentStacked` "returns immediately on `StackedState == 0`" — it restores the unit dressing first. |
| C20 | `WallSegmentFade.PropUnit.cs:206-209` | Asserted a live scratch-sharing hazard that does not exist today. Softened to a standing separation. |
| M3 | `WallSegmentFade.Stacked.cs:840-848` | `CollectStackedShellPieces`' doc block sat above `BeatsIncumbent`'s own, so the compiler attached two summaries to `BeatsIncumbent` and the real method had none. Moved (comment only). |

### 8.4 NOT done — the work list for a round permitted to touch wall-fade mechanism

- **N2 — a throw inside `RescanCore` retries the whole ~73 ms commit EVERY FRAME.** Confirmed from
  source: `_rescanStage = Idle` is at `WSF:5027`, OUTSIDE the try/finally whose `finally`
  (`WSF:5018-5026`) closes only the churn gate; `StepRescanCycle` has no `Commit` stage block, so a
  `stage == Commit` tick falls through and re-runs the commit. One log line for the whole session
  (`WSF:2160-2166`, `_failureLogged`). The subsystem's own record corroborates it —
  `CommitPhases.cs:84-91`, ModBuild 441: "aborted RescanCore … NO WALL FADED AT ALL while the retry
  burned the frame" — and that round guarded the two instruments, not the shape. Minimal fix is one
  `catch { AbandonRescanCycle(); throw; }` at `WSF:5018`. **The trade-off is a design decision, not a
  refactor**: a transient throw would heal on the next CADENCE (≤ 2 s) instead of the next frame, in
  exchange for a persistent throw costing one commit per 2 s instead of one per frame, the path
  audit no longer being starved, and the half-torn table no longer being re-torn. Explicitly NOT
  merely moving `_rescanStage = Idle` into the `finally` — that leaves `_committedSigValid` TRUE and
  the next cycle could SKIP its commit against a signature banked for a half-built table.
- **T1 — a write-only field.** `MountedProp.ShownAtFade` (`Mounted.cs:250`): three writes, zero
  reads in src/ or tests/. One write is on a mechanism path, so deletion needs the integrator.
- **T2 — a dead overload with the evidence attached to it.** `IsDoorwayAssembly(Renderer, Bounds,
  string)` (`FreeStanding.cs:834-835`) has zero callers; every site takes the 4-arg `out string why`
  overload. Its large ModBuild-410 record ("DOORWAYS NEVER FADE", torbogen_faded.jpg) is attached to
  the DEAD one.
- **T8 — a diagnostic defect of this file's own "ratio with two populations" class.**
  `WallCommitDiff.cs:790` prints the CHANGED total as `OwnershipChanged + FieldsChanged` while the
  name-group tally counts each changed segment ONCE, so the header can exceed the sum of the groups
  it introduces. Behaviour-free (the gate ships off); minimal fix stated in the findings.
- **T3-T7, T10** — duplication candidates, with both copies' exact ranges. T7 is RECORDED AS
  DO-NOT-MERGE: the four refusal chains have different order and lane-specific census sentences.
- **M1, M2, M4** — the ten outcome-3 stamp moves, two field-declaration relocations in
  `FreeStanding.cs`, and the `Held.cs`/`Floor.cs` window-reset extraction of the same shape.

**Closeout §9.0 is falsified twice** (T1 and T2), by files that did not exist when that sweep ran —
it covered 22 files and the subsystem is 33 today. Recorded in `NEEDED-OUTSIDE-core.md`.

Full findings, with per-entry line numbers:
`scratchpad/core-lane-wallfade/FINDINGS.txt` (614 lines).

## 9. Anything in the brief that was wrong (running list)

- `surface.json` `configKeys` (412) records literal bind keys only; every interpolated family bind
  (`$"BoardTilt_{board}"`, `$"{s}GripRollDegrees"`, `$"{s}PalmYaw"`, `$"FanStepDegrees_{pile}"`) is
  invisible to it, so a "key not bound" verdict from that file needs a grep before it is believed
  (LC-0). Sixty-one live Loc wildcard entries would otherwise read as dead.
- `loadbearing.py` counts a C# discard (`_`) as a written field (`MaterialLoaderHeal.Describe`, CR-3).
- §3.1: `dupes2.py <your paths> 12` — the tool takes ONE root path; a second path is parsed as the window.
- §6: "66 load-bearing instrument writes" — checker and baseline both say **65** at b40f8564.
- The "up to 4 read-only sub-agents" allowance was withdrawn by the user mid-round (max one alive).
- Process: the scratchpad directory is shared by the lanes of one session; unprefixed scratch names collide.
- WALL-FADE-CLOSEOUT §2.2 quotes `ExitDwellMovedSeconds / ExitDwellStationarySeconds` defaults as 2.50 / 7.00; `Defaults.Core.cs` ships 0.5 / 3.6 (cfg re-base). The closeout table is stale on those two.
- `STALE-DOC-REFS.md`'s line numbers for the four `BeginStandingPropScope` sites (255/267/275/800) are themselves stale; the sites were 350/362/370/906.
- `INSTRUMENT-WRITES.baseline` is keyed by the NESTED class name, so `grep WallFade` on it returns nothing — the 37 wall-fade entries are `FadeDriver::` (33) and `FadeWrite::` (4). Worth knowing before a lane concludes it owns none of them.
- The checker behind that baseline has two false-positive shapes, both of which cost a real
  investigation this round: it reads a comma DECLARATION (`T a = X, b = Y;`) as an assignment chain,
  and it treats a CONSTRUCTOR as a diagnostic because it is named like its type and then resolves
  "readers" by bare field name across the tree. Six of the 37 wall-fade entries are one or the other.
- WALL-FADE-CLOSEOUT §9.0's "zero unused fields, zero uncalled private methods" is an audit snapshot
  over the 22 files that existed at ModBuild 284; the subsystem is 33 files today and the claim is
  falsified twice inside the new ones.
- A lane that deletes lines from a file OTHER lanes cite by line number invalidates those citations.
  MR-1 removed 14 dead doc lines from `MixedReality.cs` and moved eight citations inside this lane
  plus two in WorldUI (both of which were already wrong). Worth a line in the protocol: cite by
  SYMBOL where possible, and re-grep line citations after any deletion.
