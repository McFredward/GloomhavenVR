# Build 644: measured Frame bottlenecks and independent render paths

The supplied 643 run confirms the wall repair according to the maintainer. Its
actual LogOutput/Player banners are 643/29d4b6dd8. Three fully native-loaded,
figure-steady five-room windows (LogOutput lines 4382/4518/5027) contain 707 frames over
55.7 printed seconds: **78.634 ms/frame, 43.240 ms measured mod work**. Terrain
pre-cull is 13.144 ms, WallFade.Late 10.477 ms and world materials 6.214 ms per frame.
These parents combine both eye callbacks; wall child phases must not be added
again. The 43 ms measurement identifies substantial mod work, rather than assigning
all remaining cost to the original game. Unmeasured engine work, waits and
GPU/compositor costs are not separately priced by this capture.

Both native eye targets now remain 2728x2728 at saved allocation 0.80 and viewport
1.00, approximately 36% less pixel capacity than 642. This establishes the startup
resolution repair, not a controlled FPS improvement. Overall five-room timings
are similar to 642 under different viewpoints/fade populations. The short one-room
window at 48.91 ms still contains optional cache/scenery work. No new 643 PC or remote
peer run, completed scene renderer census or screenshots accompany this capture.
The reproducible audit and frozen input receipts are retained under
`.planning/debug/frame643-followup/` in the integration checkout.

## Delivered changes

Terrain's original admission walked the grabbable registry for every source
ancestor, independently in both eyes. Read the exact current registered/local-held/remote-held roots once within the
synchronous invocation and fold membership into existing ancestry facts. Native
writes, new cameras, nesting, scene changes and recovery invalidate those reads.
This pure work removal is universal; the former player comparison switch is
retired. No mutable verdict persists across frames. Native room visibility, material/MPB changes and held objects keep
priority.

`Optimize/ScenarioTerrainSubstitution` independently selects the prepared terrain
path. **Off retains the original three-dimensional room geometry** and ends
terrain substitute preparation; `WorldMaterialQualityModeCount` still simplifies
eligible native material slots independently. Saved detail percentages and cheap
wall choices return with On. This is a CPU/GPU trade, not a lower-detail preset:
restoring originals can add approximately 155,799 triangles per frame among the
counted paired leases. That count is not a visible GPU triangle measurement.
Existing PC/Frame/Standalone substitution defaults remain On until hardware A/B
prices the final optimized path.

Automatic dormant-attachment delivery avoids repeated material/particle-color
writes only at exact fade 1, on an actually disabled attachment whose enable bit
this wall owner disabled. Floor and local/remote-held guards still run first;
intermediate fades, native re-enablement and returning pieces resume full delivery
immediately. It never skips a main wall or foundation. All platforms receive this
work removal automatically, without a player switch or profile difference.

Repeated Hide calls skip the release bridge only when there is neither an enabled
write nor a current/queued geometry consumer. A late native disable still revokes
an existing substitute before the same draw. The dormant attachment gate also
refuses current/queued consumers. The enable ledger records only a real owned
true-to-false setter: a renderer already disabled by the game is not adopted and
later re-enabled by this optimization. The 643 canonical-material fade repair is
retained.

An actual nested-camera test also exposed a pre-existing terrain recovery defect:
a camera render inside material preparation cleared the outer lease owner, then
the outer pass resumed masking originals without its post-render recovery. Lease
acquisition now records its camera after callback-capable preparation. Native
sources must be unmasked at the end of both nested and resumed outer draws.

The shared binary exposes the terrain trade in Graphics/Details and Advanced, with
English/German names and player help. Optimize changes retain the existing
Debug-only measurement boundary; QUALITY-CONTROLS includes the terrain switch. No new
normal-tier per-frame trace, shader bank, gameplay command or wire layout.

Open VR Options invoked world-material scans nearly four times/frame instead of
two, at roughly 11.3-11.8 ms instead of 5.7-6.7 ms. Current camera-mask exclusions now
avoid material/scope work for sources that capture cannot see. This is universal work removal, not a quality option; technical comparison paths
live only in the source-bound runtime fixtures.
Fresh source layers and camera masks are read each invocation; no cross-frame
union is cached. Current/queued geometry consumers, null cameras and cameras with
external command buffers retain complete validation. Nested cameras still
invalidate mutable material and ownership reads even when every source is
excluded. UI artwork, window capture resolution and updates stay unchanged.

## Existing controls: universal work versus actual tradeoffs

The 126-key source audit accounts for every frozen PerfConfig binding and adjacent
render/UI/animation/diagnostic choices. **14 existing keys are retired**, including
nine legacy flags whose getters were already true: CacheTickDelegates, MapIconCache,
FigureScanCache, LeanLogStrings, TooltipScanGate, AutomaticLodIdleSkip,
SharedWallReadCache, LightStabiliserWorkCache and SuspendUnusedCameras. Their
removal cleans the misleading surface; it does not create another FPS gain.
SharedEnvironmentMaterialReads, SharedUiWindowReads and the exact tier-100
ScenarioEnvironmentMeshBank strategy now always apply. AutomaticLodSweepSeconds
becomes an internal 15-second discovery cadence with the existing 8-second initial
delay; WalkInSuspendSampling always omits decisions overridden by solid-wall
walk-in state. Fade ramps, native writes, release detection, room reveals and
in-flight rebuild completion remain live. Saved orphan keys are ignored; actual
quality/timing choices are not reset or overwritten.

Four grouping controls **remain configurable** after an independent deeper review:
ScenarioStaticBatching, ScenarioStructuralBatching,
ScenarioExplicitEnvironmentInstancing and ScenarioStructuralInstancing.
Private combined renderer bounds can change native per-renderer Forward light
selection; current guards establish probe/lightmap contracts, not dynamic-light
equivalence. The custom instanced command explicitly draws pass 0 while original
high/low Amp N_MRAO bundles retain a ForwardAdd pass 1. Native automatic instancing
keeps native passes but lacks supplementary vertex-stream admission and operates
on globally shared material flags. The actual prior Unity stream fixture documents
that automatic instancing can ignore supplementary positions. These are reasons
not to force those paths universally, not confirmed headset artifacts in this run.
The full-detail mesh bank does not change those choices or force simpler shading.

Visible shaders, mesh tiers, scenery/effect densities, cloth, offscreen animation,
texture/resolution sampling, lighting and real update/discovery latency settings
remain independent choices. Diagnostics remain controls for measurement work.
FigureExemptSkip remains off by default: its narrowed signature can omit a real
prop dependency, so it cannot be labeled harmless pure work removal. Mature map
poses and tooltip guards are already automatic; new map membership/canvas
discovery still has historical polling latency. AutoLOD's unchanged implementation
does not restore a disabled component after arbitrary foreign effective-mode/root
changes; the fixed cadence does not broaden admission or claim to fix that gap.
Both independent audits and native shader-pass provenance are frozen in
`.planning/debug/frame643-followup/policy-audit/` and `policy-wall-audit/`.

## Next hardware comparison

Use the same fully loaded all-room view and retain 0.8, world material mode 2 and
all other settings. Close VR Options, wait for preparation to settle, then record
roughly 30-45 seconds each with terrain substitution **On, Off, On**. Inspect both
eyes, all rooms, near walls and pillar/shelf fading in both paths. Compare parent
costs and frame medians, rather than a single spike or nominal runtime refresh.
Existing grass, vegetation, loose decoration, optional FX
and near/far terrain detail are already at their minimum; lowering the 64-source
cap targets nothing in this run because neither budget fallback nor deferral occurs.

## Further compromises, in order of evidence

1. Omit independently budgeted *noninteractive architectural details* at distance
   or by density, while retaining each room's 3D floor, required outline, actors,
   doors and interactive/held props. Work must withdraw from terrain/material/fade
   preparation as well as drawing. A renderer-only mask does not guarantee CPU
   savings. This requires proven whole-detail units and restoration before it can
   become another profile default; full-room hiding or a 2.5D board is excluded.
2. Build native-fade-aware small submission groups. Current chunks cover only
   about 18 sources/frame into 6 groups, not the whole board. Extend only with exact
   current per-source fade/MPB/material ownership and revocable camera submissions.
   Never reintroduce native StaticBatchingUtility/SetStaticBatchInfo: historical
   Apparance clones lost all material slots and entire revealed rooms disappeared.
3. Treat single-pass stereo as a separate rendering project. The game/mod currently
   require MultiPass; the historical SPI setting produced a black right eye and
   was removed. A new path needs actual original/DLC shader delivery, UI/video,
   particles, eye/FOV and interaction validation. The combined two-eye terrain+
   world cost of 19.357 ms is not an estimate of removable stereo overhead or a promise
   to double FPS.

The open-options windows stay outside the primary steady comparison. Hardware
must price the new masked-source work exclusion separately with the options open
and closed in the same loaded view. The final 643 scene census never completed,
so older renderer totals are not current evidence.

Validation and compiled integration receipts are recorded after the focused
runtime checks; automated render/ownership tests do not establish headset FPS or
multiplayer acceptance. A paired multiplayer run is still required.
