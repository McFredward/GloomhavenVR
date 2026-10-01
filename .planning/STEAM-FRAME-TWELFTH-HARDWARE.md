# Steam Frame: Build 599 large-scenario evidence

## Scope and provenance

The newest supplied `LogOutput.log` and `Player.log` both identify ModBuild 599,
commit `0035d7916`. Their frame numbers and summary values match. The different
filesystem timestamps do not make the Player log stale. The run enters `ProcGen`
directly after the menu; it does not test the 3D map. No screenshots were supplied
in this Frame folder. Local raw copies and the parsed report are retained under
`.planning/debug/steam-frame-evidence/build599-20261001/` before the next upload
overwrites the usual evidence location.

Loading and startup windows are excluded below, as requested by the maintainer.
The eye target stays at 3408×3408, scale 1.00, MultiPass. Native graphics quality
is Fastest, shadows disabled, pixel lights zero, MSAA zero, head depth texture
None, and graphics jobs enabled. No eye-scale or wall-toggle trial is established
by this run. The effective wall evaluation remains 0.25 seconds and the table
rebuild setting 4 seconds; these are different cadences.

## Complete post-load scenario windows

| Metric | Window at LogOutput line 2387 | Window at line 2727 |
| --- | ---: | ---: |
| Duration / frames | 20.0 s / 242 | 20.2 s / 171 |
| Frame mean / p50 / p95 | 82.85 / 80.84 / 146.61 ms | 117.64 / 109.14 / 160.15 ms |
| Main-thread Update→LateUpdate mean | 35.54 ms | 57.34 ms |
| Main-thread render cull+submit mean | 10.31 ms | 23.29 ms |
| Remaining frame span | 36.99 ms | 37.01 ms |
| Measured mod scopes, exclusive total | 20.01 ms | 22.64 ms |
| Head camera cull+submit | 7.95 ms | 20.79 ms |
| Visible renderer estimate, p50 | 1,493 | 4,341 |
| Head distance from board, p50 | 14.1 world units | 53.0 world units |

Both windows have tracked HMD evidence, but head/view changes are substantial.
They are different scene views, not an optimization A/B. Compared with Build
598, different view populations also prevent claiming a measured improvement.
The runtime's 24/12 Hz report is not the physical panel's nominal capability.
The frame means correspond to approximately 12.1 and 8.5 completed game frames
per second; late ten-second heartbeats fall to around seven frames per second.

The remaining span includes unbracketed engine work and waits. Neither it nor
the runtime's `xr gpu` value is a GPU busy-time measurement. ZOOM's causal
language about view-scaled simulation is a hypothesis: a higher renderer count
and larger logic span alone cannot distinguish managed scripts, Unity native
animation/particles, procedural engine work, rendering preparation or waits.

## What the new instruments establish

`LogOutput.log` has no BepInEx Debug entries, while the matching `Player.log`
contains seven `NATIVE` summaries. `VRLog.Debug` uses `LogDebug`; the other perf
summaries use the mod's Debug-gated Info sink. This is a sink/filter difference,
not evidence that Debug was off or that the native probes failed. The exact
BepInEx disk-listener configuration was not supplied.

In the final 120-frame native capture, the nine selected inclusive callbacks
sum to about **1.13 ms/frame**, with possible nested overlap. Largest selected
values are ExtendedButton.Update 0.366 ms, ProceduralTileObserver.Update 0.305 ms,
and ActorBehaviour.Update/LateUpdate 0.178/0.140 ms. The associated phase capture
reports Update 38.33 ms, LateUpdate 13.99 ms and interphase 2.43 ms on 118 aligned
frames. The nine targets do not explain the CPU wall. Probe-body calibration
estimates 0.124 ms/frame at the observed call count; Harmony dispatch is excluded.
Do not subtract a sampled inclusive sum from a whole-window exclusive total.

Draw Calls, Batches, SetPass and Triangles counters produce only zeros and are
correctly reported as **n/a**. No actual draw count or GPU busy time is available.
The only expensive `SCENE`/`SIM` census sampled the early menu, with five
renderers and no animators/particles. Its 77.9 ms cold sample triggered eight
skipped windows; that cooldown carried into the scenario. Consequently the run
has no valid scenario callback-type, animator or particle population census.
The menu's empty populations must not be applied to the later scene.

## Other findings

Wall-table commits remain a separate, measured source of gameplay hitches.
After loading, representative commits cost 254–292 ms; WallCache accounts for
about 116–121 ms in those commits. The late partial interval also records a
310 ms WallFade.Late frame. The previous wall-off hardware observation still
rules out promising that fixing these hitches alone will solve sustained FPS.

No mod Error/Fatal entries or unhandled exception stack is present in the
supplied logs. The native Hydra backend repeatedly cannot resolve its host;
there is no timed evidence tying that failure to the large-scene slowdown.
The logged ActorBar zoom NullReferenceException is caught and deferred until
the next health update. Wall diagnostics flag latch/release/leftover ownership
anomalies; these are separate presentation warnings, not proof of the primary
performance cause. Absence of an exception does not establish correct headset
appearance or exclude a native crash outside the captured tail.

## Next implementation and measurement targets

1. Capture the **actual loaded scenario** once after preparation, keeping the
   census bounded and its sample explicitly excluded from gameplay timing.
   Do not carry a menu's diagnostic cooldown across scene boundaries. Extend
   selected CPU attribution to source-backed singleton callbacks, especially
   Choreographer.Update, PlatformLayer.Update, and available Apparance updates.
   Their source makes them candidates, not measured culprits. Native Unity
   animation, particles and render preparation still require wider attribution.
2. Audit scene decor by shared mesh/material, LOD, property block, procedural
   ownership, room reveal and head-specific visibility during loading. An
   inventory is the prerequisite for lossless submission reduction; it is not
   permission to batch arbitrary Apparance sources. Build 15's invisible-room
   failure and the maintainer's rejection of that batching remain binding.
3. Remove or slice repeated wall-cache preparation only where lifecycle and
   atomic publication can be preserved. Measure those hitches independently
   from the steady frame rate. Keep room reveals, native effects and remote
   presentation unchanged.
4. Obtain supported GPU/graphics-capture evidence if engine/render attribution
   remains inconclusive. Existing resolution, shadow and camera-mask readings
   do not support another blind quality-toggle pass. Single-Pass Instanced is
   blocked by shipped game shader variants and per-eye UI rendering, so it is
   not a safe setting for this player.

Supporting source reviews:
[native callbacks](research/FRAME599-NATIVE-REVIEW.md) and
[render feasibility](research/FRAME599-RENDER-REVIEW.md).
This round changes developer documentation only. It neither ships a runtime
optimization nor changes ModBuild, graphics defaults or multiplayer behavior;
re-running the existing runtime test gate would provide no new evidence.
