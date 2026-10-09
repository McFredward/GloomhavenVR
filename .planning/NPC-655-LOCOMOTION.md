# NPC655: local map locomotion audit

## Evidence and limits

Both newest multiplayer logs identify Build653 (host and participant). This
therefore precedes Build654's XR-focus cache correction. The participant's
`LogOutput.log` records one map-rig build at line 1039 and one entry recenter at
line 1352. The rig is not rebuilt/recentered again until map teardown at line 6350.
The map-time comfort verdict stays `TableIdle`; all 103 flight verdict lines are
the normal map/menu lifecycle or the deliberate left thumbstick-click world-grab
and its release. No mid-map Menu2D gate, tracking-loss/reorigin compensation,
scroll suppression or locomotion exception appears.

`remote/Player.log` confirms tracked HMD, left and right controller at every
map-time heartbeat, with head movement between successive samples. The maintainer
additionally reports smooth observed head/hand motion during the participant's
local translation freeze. That rules against describing the report as delayed
remote presence, complete application freezing or a destroyed map rig. Map-entry
CPU spikes exist but are separate; they do not prove the reported input block.

The supplied logs contain neither raw stick/click samples nor Unity timeScale,
nor requested-input versus sampled rig displacement. They **do not prove a unique
cause for this intermittent hardware report**. The game's `TimeManager` uses
Chronos; its pause must not be conflated with `UnityEngine.Time.timeScale`.

## Source-proven repair

WorldGrab used scaled Unity deltaTime for both one-hand translation smoothing
and two-hand rotation/scale smoothing, while Flight and the recenter chord already
use the tracking clock. At Unity timeScale zero a real clicked world-grab still
owns the flight stick (correct arbitration), but its displacement is exactly
zero. This reproduces blocked translation/drag with tracked moving hands. Snap
turns still have their fixed angle; smooth turns also used the stopped clock.

All three comfort expressions now use native unscaledDeltaTime. No gameplay
clock, native state, input owner, scroll latch, mode gate, recenter/spawn seat,
map origin, rig clamp or tracked pose is changed. No timeout blindly unlocks
genuine input ownership and no speculative catch swallows a scroll exception.

`Comfort.MotionDiagnostics.cs` adds Debug-only observation in the component's
explicitly labelled `LateUpdate` phase. It is **not labelled render-final**:
Unity does not guarantee ordering against other LateUpdate writers. It records
raw axes/clicks/pose, actual grab/reel/scroll ownership, turn latch, flight/grab
settings, XR input focus, Unity scaled/tracking clocks, and sampled rig motion.
Net window displacement/rotation and accumulated sampled travel are separate
so a reversal cannot appear as net locomotion. Samples are bounded to one per 5s
with current input and one per 15s idle. The ordinary user log level exits before
rig lookup, sampling or diagnostic string construction.

## Validation

- Strict Debug and Release plugin/preloader builds: 0 warnings, 0 errors.
- `check-frame-order.py`: all 11 existing locked orders unchanged.
- Native Unity2021.3.5f1 Play Mode, real paused clock `0/0.01237947`: **24 runtime
  assertions** pass on the final production behavior. Covers card/window-held
  world grab, paused one/two-hand movement, ownership release, tracking loss and
  restoration, head-fixed turn pivot, modal-UI locomotion, actual scroll latch
  and deliberate escape, per-hand claim separation, explicit disabled-state
  cleanup, diagnostic throttle/net-vs-travel distinction, and normal-level
  diagnostic bypass.
- Three old-source controls fail exactly `paused one-hand drag remains movable`,
  `paused two-hand rotate and scale remain movable`, and `paused smooth turn
  remains movable`. The clock-repaired motion source hashes match the final
  production source; the follow-up changes only diagnostic counters. The final
  positive rerun reused these passing causal controls rather than repeating
  unrelated suites. The fixture's scroll-rest expectation was corrected to
  respect the unchanged production Override-to-Blocked-to-Open transition while
  the same-frame real hover remains stamped; no production scroll change was
  needed.
- New Python runner syntax and whitespace checks pass. This is focused evidence,
  not a new complete test-catalog, wire gate or connected-headset result.

Receipts in this worker, also archived under the main checkout's
`.planning/debug/npc655-locomotion/`:
`.planning/debug/locomotion-clock-runtime/run-h_12o175` (final positive) and
`run-1k4cnltk` (the three behavioral causal controls). The initial fixture runs
are not claimed as passing production evidence.

## Bounded follow-up: transient clock evidence

A five-second report's current clock values could miss a pause that has already
ended. The existing Debug window now also accumulates requested-motion frames
with zero scaled delta and the minimum Unity timeScale across requested-motion
frames. These counters reset with the existing report window. No additional line,
scan, normal-level sampling, input gate or motion behavior was added.

The focused final positive rerun `run-2_0i_v2l` passes the original **24** native
runtime assertions and **3** additional assertions. It captures a real paused
request, resumes Unity's clock across real frames, and verifies the bounded line
still records `zeroScaledRequestFrames=1` and `minRequestedTimeScale=0.000` while
its current `unityTimeScale=1.000`; the next window correctly resets to zero/one.
The resumed native delta was `0.000452669`. The unchanged three causal motion
controls from `run-1k4cnltk` are reused; no complete build/gate was repeated for
this observational refinement. The new receipt is also archived in the main
checkout's `.planning/debug/npc655-locomotion/`.

## Next hardware evidence

Retest independent local translation and thumbstick-click world dragging during
NPC interactions. If the intermittent symptom recurs, `LOCOMOTION SAMPLE`
separates delivered zero input, a legitimate local ownership gate, a stopped
Unity clock and actual sampled movement. A recurrence cannot honestly be ruled
out from Build653's existing logs or these automated results.
