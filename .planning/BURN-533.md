# Unresolved short-rest flash — build 533

## Evidence

The new owner log is ModBuild 532; the supplied peer log remains historical build 500.
The user confirms this test accidentally omitted Debug and has enabled it for the next run.
Consequently the existing `BURN PRESENTATION` continuity records are absent.

`Player.log:3874` starts the native short-rest loss sequence. The FlameStrike redraw choice
moves Discarded -> Lost (`:3889`); the other discarded cards return to Hand. Before its
2.66-second burn hold ends (`:3931`), the existing bounded `BURN RAMP ABANDONED` report
finds raw GreyOut 0 and settles native artwork. Freezing Nova and Reviving Ether action
burns finish at raw 1 after two seconds. This does not identify the flashed rendered frame
or prove an animation replay. Build 532's terminal-floor repair did not solve the reported
hardware symptom, and this build must not be described as another confirmed burn fix.

## Audit and diagnostic change

The native short-rest path begins `BurnCard` in `FinalizeShortRest`, before asynchronous
model commitment. `AnimateCardsLost` later refreshes all widgets and drives separate
mini-card movement. Full-card effect changes pass through the existing Toggle/Restore
protection. Original-card normalization is excluded by `CardHalfTone`; the old held-card
material override has no active callers. No additional short-rest-specific playback defect
was established in this review. No speculative playback change was made.

Debug-only `BURN NATIVE TRACE` now correlates first/native terminal steps, reset decisions,
effect type and requested activation, retirement, native clock/delta, model memberships,
original effect identity and actual CanvasRenderer material bindings. It reads both plate
and flame channels, distinguishing a material replacement from a rewind. It never asks
`materialForRendering` to create/update a stencil copy and never modifies native materials.

Tracing is limited to originals whose animated timeline was observed, plus their recent
tail. Repeated ordinary transitions are deduplicated; each episode has eighteen output lines
with six reserved for terminal/render anomalies, and the process has a 128-line ceiling.
Normal logging returns before sampling or formatting. This is evidence collection for the
next reproducible short rest, not a claim about final headset pixels.

## Focused validation

The replay harness compiles the complete production trace beside the extracted playback
methods. Assertions cover normal-level silence, renderer/base separation, flame-only
rewinds, duplicate suppression, reserved completion capacity, per-episode/session bounds,
and read-only material observation. Six planted trace regressions must fail these assertions.
Existing playback/recovery regression checks remain unchanged in behavior.

Focused result: 644 runtime assertions, six source bindings and 30 negative controls
(24 existing, six diagnostic controls). Worker Release build: zero warnings/errors.
The integration result is recorded in STATE.md. Hardware reproduction is still required.
