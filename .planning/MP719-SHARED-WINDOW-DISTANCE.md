# Fixed shared-window spawn distance — Build719

## Request and hardware evidence

The maintainer's 2026-10-23 small multiplayer review confirms matching shared
window size and host-oriented placement after717, but reports a window farther
away when its host is larger. Its initial host-to-window distance must also be
independent of host zoom, retaining host gaze as the opening's origin/facing.

Both supplied logs identify Build717: main `LogOutput.log:17` and remote
`LogOutput.log:18`. The host records24 quest-confirm spawn/final-fit entries,
each with the same198.117 common size reference and106.508 initial world width.
The guest records no host-spawn writes. These support the reported fixed size
and single author; the old spawn log omits the head and placement multiplier,
so it cannot establish an exact measured host-to-window distance. Hardware
inputs remain untouched; a bounded extract lives in
`.planning/debug/mp719/hardware-extract.json`. The718 WorldUI source is identical
to717, so the reported placement calculation remains present at the dev baseline.

## Source findings and repair

`ComputeSharedHostSpawn` still used `PanelLayout.WorldScale` for the initial
1.4-metre design distance and board/readability clamp. That multiplier contains
the author's current zoom. Its final-fit replay additionally reused the saved
raw pose and scale, which could retain a previous private placement. Native
movie creation separately used that same live multiplier for its existing
0.75-metre design distance. Equal window dimensions did not fix either producer.

Use the same captured common world reference as the opening's actual renderer
for every shared placement length. A conversion before grab construction uses
the common geometry reference; fitted/reward placement retains the grab's
captured reference across ambient map/scenario changes. Replay retains its
original head and forward vector, recomputing the raw offset with that common
metric. Its half-size likewise uses the captured reference before clamping.

Shared native video creation calls one production helper using the shared
reference; private movies retain their original viewer-scale placement. Existing
modal and movie family distances, host/flat-host-surrogate election, yaw-only
facing, native callbacks, table/readability constraints and synchronized manual
movement/resize remain. Already placed or grabbed windows are not periodically
repositioned. No wire layout, asset, configuration or product-string change.

The existing bounded `SHARED WINDOW HOST SPAWN` marker now additionally records
the original head and actual world distance; its fixedScale field names the
captured placement reference. This permits direct measurement in the next test.

## Validation and limits

The earlier geometry proof exercised render geometry with a substituted spawn
clamp and an incorrect1.3 literal. The expanded proof executes the production
1.4 constant, actual centre/board clamp kernels and dedicated movie spawn helper
with real Unity transforms. It varies host zoom independently of head pose,
common map/scenario geometry, looking direction, table context and fit replay;
distance comparisons use absolute world units, never normalization by host zoom.
Its explicit native/tracking boundaries remain documented in the fixture.

The full expanded Unity proof passes4463 assertions and29 compiling negative
controls; the production sampler/119+121 receive/election proof passes588
assertions and29 controls. Worker input hashes match the integrated source and
fixtures except the build719 protocol constant/note. Integrated719 production
reruns pass4463/588, retaining the workers' unchanged29+29 negative controls.

Eleven distinct affected suites have passing coverage: window/shared-window
reflow, quest window, reward pose, native video, video playback, reward showcase,
shared reward placement, shared UI window, Unity geometry and portable pose.
The first integrated five-suite batch had three passes and two fixture compiler
failures: native video's grab/size substitutes lacked the new captured-reference
properties; the reward extraction omitted the new scale helper and a vector
magnitude property. Both affected suites pass their complete repaired reruns.
The reward fixture now extracts the actual1.4 distance constant and independently
expects its correct36.8 endpoint rather than the previous1.3-based35.6 value.
Original callbacks/authority assertions remain. Failed receipts are retained,
never represented as passing complete invocations.

Strict Release passes with zero warnings/errors, all16 source gates pass, and
complete unfiltered golden vectors pass306431 assertions. Bundle and source
surface checks pass with all669 settings,242 patches and4816 log markers
retained. Private compiled comparison against718 finds two intended behavior
types (`ModalFallback`, `NativeVideoWindow`) and nine build-only differences,
with no added/removed type or unrelated behavior. Receipts, exact input checks,
compiled diff and deliverable hashes live under `.planning/debug/mp719/`.

This is a bounded placement repair with focused coverage and unchanged717/718
evidence, not a new complete local-suite invocation. Automated passes do not
establish headset pictures or native multiplayer timing. Completed-run caches
are removed with the canonical retention API. Both owned worker worktrees were
removed after their evidence was archived, SHA256-hashed and byte-compared;
source, logs, failures, captures and results remain preserved, as do other agents'
worktrees and shared game references.
