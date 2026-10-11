# Build630 loading indicator review

Developer notes; hardware acceptance is pending. This lane starts at `dev`
`e6ecf3d4532bbb5c947fcefb8111200bee49c768` (Build629).

## Evidence and cause

Both supplied immutable Frame logs identify Build628 / `176beb741`; the runtime
reports Steam Frame defaults. In `Player.log`, native `Disabling loading screen.`
is followed by `Scenario interaction preparation started; original input remains
available.`. The ordinary priority-restoration line follows that edge. The
preparation reports progress at 10, 20, 30, 40, 50 and 60 seconds, then completes
at **61.81 seconds** before the VR spinner fades out. The original
`CharacterPlacement` input already reports `paused=False` during that interval.

`LoadingIndicator.Tick` included the initial optional cache preparation in its
loading gate. `ScenarioInteractionPreparation.ShowsLoadingIndicator` retained
that gate for card sprites, native references, mip/backing jobs and figure ghost
preparation, including presentation-budget waits. This source path explains the
measured spinner overhang independently of whether those caches improve later
interactions. No comparable new PC capture is supplied; the same source path
applies on PC.

The same run also explicitly records the display's `BelowNormal -> Low` native
async-priority override. That code deliberately reduced Unity integration work
per frame at the cost of longer loading. The user accepts load-time hitches and
wants the ready scenario to remain playable. The logs establish that the override
ran; they do not isolate how many seconds it added.

## Changed behavior

The initial scenario spinner follows the original native loading flags and the
existing one-way boot coverage. Optional initial card/figure preparation still
runs with its existing bounded work and fallback behavior, but cannot extend the
spinner or flat-screen suppression after native loading completes. The normal
0.30-second visual fade remains.

A subsequent native room reveal still publishes actual loading while its real
procedural generation or live material handles remain pending. The original
room watcher, two-frame activation guard, completed/failed/released-handle rules
and native input remain unchanged. Cosmetic preparation after a completed reveal
continues in the background without extending that room's spinner.

The display no longer writes `Application.backgroundLoadingPriority` at any
edge, including shutdown. It retains the actual native setting instead of
raising it to a different priority. Ordinary start/completion reports remain
bounded by edges and include the native setting or elapsed observation time.
The existing `[WorldUI] LoadingIndicator` key/default remain; its description
now matches the behavior.

Native `IsLoading`/`ScenarioIsLoading`, gameplay continuations, peer readiness,
scene state and input are not written. Native multiplayer loading/state
comparison still owns the indicator when the game's flags say it is loading;
optional remote presentation preparation adds no new dependency.

## Focused verification

- Strict `ci-build.sh Release`: zero warnings and errors.
- `check-perf-census-runtime.py`: **1,485** production fixture assertions,
  **31** causal negative controls, and **13** separate real-engine-frame
  assertions, all pass in Unity 2021.3.5f1.
- New causal controls restore the erroneous initial cache gate, extend a completed
  room's spinner with cosmetic work, and reintroduce native-priority throttling.
  Each fails its corresponding behavioral assertion.
- The production loading-edge observer runs against Unity's real process
  priority at Low, BelowNormal, Normal and High across repeated edges. It
  preserves the setting and emits four lifecycle lines for four edges rather
  than a line per frame.
- Existing native-generation/material-handle, room activation, completion,
  fault/reset and original-callback controls remain in the same focused suite.
- Instrument-write source check passes; eleven locked frame orders pass.

The fixture binds the complete production preparation/room watcher and the
actual loading lifecycle method. The game scene flags, model and preparation
jobs are explicit external seams; it is not a complete game load or a headset
picture test. Boot art, fades, head attachment and town-mode readiness are
unchanged source paths. These tests establish the removed spinner dependency and
absence of native-priority writes, not an exact next-hardware load duration or
any steady-state FPS improvement. The primary integrator runs the complete gate
on the final merged tree.

Raw worker evidence is `.planning/debug/perf-census-runtime/run-ml_yipk2/`, with
source hashes, compiled variants, exact results and Unity logs. The main checkout
retains the hardware inputs under
`.planning/debug/frame630-review-20261006/inputs/`.

## Next hardware checks

On PC and Frame, enter a scenario and check that the spinner fades with native
loading completion while original scenario input is available. Confirm newly
revealed rooms still show their real load and finish when generation/material
work finishes. Repeated scenario/map/menu transitions, a canceled transition,
VR disable/restart and an immersive-town toggle must retain their normal display
and fallback paths. Load hitches are acceptable; a spinner surviving completed
initial optional preparation is a regression.
