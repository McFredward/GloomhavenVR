# Burn-layout and fan-audio regression scope

The existing layout section executes the production burn-sequencing/claim methods
and rebuild admission with explicit game-model, native playback and downstream
renderer boundaries. Its original assertions and sixteen causal controls remain.

The audio section compiles the exact opening/closing branch from
`CardsDriver.UpdatePalmGate` and the complete production `CardFan.Open` and
`CardFan.Close` methods. The current palm intent and burn wait are supplied inputs;
the existing layout section tests production barrier discovery separately. The
hierarchy, card-layout helpers, prewarm, profiler and `PlayFanEdgeSound` sink are
named stand-ins. The sink counts requests before native clip throttling, so the
test detects repeated logical audio edges that a real audio controller can hide.

Both animated and instant close paths exercise 180 blocked frames, successful
release, stable frames, ordinary reopen/close and cancelled open/close intents.
Two additional runtime controls restore unconditional open or close audio and
must fail the corresponding blocked-frame assertion after successful compilation.
This proves request timing, not native audio playback, a complete palm-pose flow,
multiplayer transport or a headset recording.

Run `bash scripts/burn-layout-tests.sh` or select `burn-layout` with the central
suite runner. Sources, generated projects, compiler logs, runtime logs, hash and
result receipts stay in `.planning/debug/burn-layout-runtime/run-*`. Every stopped
run calls the shared `cleanup_completed_run` API after reaping its process groups;
only regenerable project caches are removed, including for failed controls.
