# Build620: loading indication follows outstanding room assets

## Hardware evidence and its limits

The supplied PC `LogOutput.log` and `Player.log` both identify Build619,
commit `6e36062e3`. Immutable copies and SHA-256 values live in the main checkout's
`.planning/debug/frame620/inputs`. The initial preparation finishes after 8.77 s.
A subsequent room report remains active at 20/30/40/50/60/70/80 s, with card queues
empty, 74/74 figure jobs completed, and no pending presentation masks. Its last
inventory contains 25 generation owners and 2,405 material-loader data entries;
the indicator clears only at the 90 s safety limit.

Those old counts are inventory totals, not counts of unfinished operations. They
cannot establish exactly which native request or generation owner held the gate.
The source review and independent async replay establish a concrete false-loading
path consistent with the report, rather than a measured hardware performance gain.

## Cause and implementation

Original `MaterialLoaderData.LoadMaterials` allocates `_loadedMaterials` with
extra slots when `IsSaveExistedMaterials` is true. Those slots can remain null even
when all material handles are complete. Failed native requests also have null
results. Build619 treated any such null as continuing asset loading.

The room observer now reads the original `_handles` and `_released` fields without
changing either. Only a valid, unfinished native material operation or an active
busy `ApparanceEntity` keeps the room pending. Completed failures, released or
invalid handles and dormant renderer data do not masquerade as continued loading.
A final discovery includes children created during procedural placement. The two
initial activation frame boundaries remain; the extra completion quiet timer is
removed. No full-scene recurring census is added.

The loading indicator now distinguishes initial cold preparation from new-room
asset loading. Initial card/figure preparation stays bounded and visible. Once a
new room's native work completes, the spinner disappears on that update; remaining
cosmetic queues or ghost-cache preparation can finish without extending it. The
native game's own loading indication is preserved. Opening settings or ordinary
maintenance never creates a room-loading event.

The existing ten-second Debug progress report now includes actual unfinished
material handles and busy generation owners, separately from inventory counts and
completed failures. Counting and formatting occur only inside existing guarded,
bounded Debug paths. Ordinary player log volume is unchanged.

## Verification

The focused native Unity 2021.3.5 fixture executes the complete production room
observer and coordinator with real `ResourceManager` async operation handles.
It reproduces null result slots independently of operation completion; exercises
live pending, completed, failed, released and invalid handles; and checks that
cosmetic preparation cannot extend a completed room's spinner. Four causal
controls fail at their intended readiness assertions. Production: 1,449 runtime
assertions plus 13 real-engine frame checks. The complete integrated gate is
recorded separately in the Build620 validation ledger.

This is source and native-runtime evidence. A headset test must still confirm the
indicator duration when opening a room, together with first interactions and late
arriving room assets. It is not a proof that all remaining Frame hitches are fixed.
