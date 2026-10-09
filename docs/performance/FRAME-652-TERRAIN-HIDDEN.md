# Build652: suspend exact hidden-wall terrain work

The wall compromise already removes eligible native wall renderers from camera
visibility. The private terrain owner must also stop maintaining geometry that
cannot contribute pixels. Previously its Update still validated every prepared
wall, calculated distance detail and advanced geometry despite the persistent
wall mask. PreCull rejected the native mask only after crossing native visibility
properties.

`ConfigurePerformanceWallVisibility` receives the wall owner's exact managed
membership predicate. Update skips those prepared sources before validation,
detail and geometry work; PreCull skips them before native visibility properties.
Substitute ownership is false for an exact hidden source. Shared head/hand detail
reads are deferred until an unhidden valid source actually needs them. This is a
mandatory consequence of the existing wall compromise, with no additional setting.

The predicate deliberately does not use `forceRenderingOff` as a broad Update
veto. Other native masks retain their previous maintenance behavior. Prepared
record iteration and one managed membership query remain; private terrain work,
not all original game or GPU work, is the bounded claim. The shared native room
occlusion pipeline, gameplay, collision, floor, door and actor rules are unchanged.

Discovery may still traverse native placement roots at their original lifecycle
boundary, but an already-known exact hidden renderer is rejected before its filter
read or private record creation. No deferred renderer polling is added. On final
wall-mask release, the existing CoreModule callback restores the scenery owner's
current policy and calls `ScenarioTerrainBudget.MaterialReady(renderer)` to queue
that exact source once. A genuine queued readiness/release event with a changed
eligible mesh replaces its old private record at that boundary. Otherwise later
validation would delete the old record after consuming its only queued replacement.
Current material, mesh, scope and ancestry guards run again in Regular.

The executed final-release fixture also exposed an existing discovery identity
mismatch: QueueRoot added GameObject IDs, but dequeue removed Transform IDs.
Previously traversed sources therefore remained in the deduplication set forever.
Dequeue now removes the same GameObject ID, allowing actual later callbacks to
queue that source again. A separate causal control restores the original mismatch.
Owned world-material changes only release terrain leases and do not call this
readiness queue, so that owner does not introduce an eye-by-eye discovery cycle.

The focused fixture executes the complete source-extracted terrain driver,
geometry/admission partials, actual Unity2021.3.5 callbacks, meshes and camera
pixels. Source-bound observers retain the original native operations. The exact
wall membership/acquisition boundary is an explicit external surrogate; the final
CoreModule release lambda is extracted and executed, with only the independent
scenery callback represented by a no-op. The existing full wall-owner proof covers
that owner separately. `--integration-root` permits the worker to bind the frozen
main integration callback without overlaying or committing CoreModule.

New cases cover interrupted lease revocation; no hidden per-source validation,
detail, geometry or native camera visibility reads; no preparation for already
hidden discovery; native-mask distinction; no shared pose reads with all prepared
sources hidden; exact final-release discovery; live changed mesh/material/reparent
recovery and blue output pixels; current actor ancestry; and unchanged floor and
collision ownership. Nine causal controls remove each relevant guard, overreach
native masks, lose changed-mesh replacement, retain eager pose reads or drop the
source release callback. All existing controls remain.

Hardware appearance, native gameplay/network behavior and headset FPS are not
established by these local fixtures. Build notes and primary review record the
integrated gate and exact final tested scope.

## Worker validation

Final production source checkpoint is `587d84095`, following the two earlier
bounded source checkpoints `dc1dc9db0` and `f68b36d34`. Strict Debug and Release
builds pass with zero errors and warnings. Frame order verifies 11 locked orderings;
partial order verifies 49 types/302 parts with zero cross-part dependencies.

The complete focused terrain suite passes 403 actual Unity runtime assertions and
all 87 negative controls (88 variants including production). All 79 previous
variants remain; the nine new controls fail at their named assertions. Independent
native coverage still verifies all 10 exact structural definitions and every
original channel/index byte and coarse digest. The final source-stability receipt
reports unchanged inputs.

Raw final evidence lives in the worker's
`.planning/debug/frame652-terrain-hidden/full/run-5aakpvtj` when initially generated;
the primary handoff archive retains the exact final directory and its full
source-hash metadata. Earlier raw attempts are also retained. These include
observer/control nullable compilation failures, an invalid source-drift attempt,
pre-fix source release failures which discovered the queue identity bug, and a
broad control that initially tripped older visibility/liveness boundaries before
being restricted to the intended Update guard. None is counted as a passing
control or removed to improve the verdict. Primary integration runs the affected
final checks and records which earlier complete-gate evidence is inherited.
