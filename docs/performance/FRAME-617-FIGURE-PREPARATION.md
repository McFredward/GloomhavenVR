# Figure interaction preparation — build 617

The build-616 Frame capture recorded a 109.10 ms figure pickup containing 55.89 ms
of ghost construction, and separate 65.93–98.54 ms stat-panel steps. These are
individual measured steps, not a prediction of the saving on another device.
The implementation moves reusable original ghost construction and already-present
stat artwork cache misses into the scenario loading interval on every platform.

## Loading and lifetime

`FigureInteractionPreparation.Begin()` takes one snapshot of actual native
`ActorBehaviour` objects after the game's loading has established actors and UI.
`Tick()` prepares at most one complete ghost, or one shared stat-art resource.
`CompletedCount`, `TotalCount` and `IsReady` let the loading coordinator retain the
spinner while this finite work drains. A single original ghost clone remains an
atomic operation; this is a work-count bound, not a millisecond frame budget.
Skipped or failed entries count as completed. Failures retain immediate pickup
fallback, with at most three figure and two stat-art warnings per preparation.
Actors already held are skipped without replacing their live ghost. A loading
timeout calls `CancelPreparation()` to stop pending work while preserving active
holds and reusable visuals; it does not invoke full scene teardown.

Prepared ghosts stay inactive under an inactive persistent parking parent. They
contain the original render hierarchy and mesh references, with the existing
ghost tint, native cutout masks and masked depth twins; they contain no native
gameplay scripts, Animator, cloth, collider, particle system or camera. No native
`Show`, animation evaluation or synthetic actor is used. Local and remote holds
both acquire through the existing `FigureGhosts.NotifyHeld` path, including either
hand and ordinary immediate release before native movement or another action.

At acquisition, the cache validates the source root, hierarchy, renderer, mesh
layout, sprite, material/shader/queue, skin-bone and native LOD-table identities. Changed identities use
the existing immediate construction path. Actors introduced after the loading
snapshot also use that fallback, and their successful ghost is cached for later
holds. This does not run a recurring scene discovery.

The inactive twin binds the **current** evaluated original bones, native animation
state/layers/phase, blend weights, home selection ring, native material-mask
properties and skin bounds before its first pickup frame. A sleeping dragon does
not retain a flying pose captured during loading. Release parks the twin immediately;
scene/load/VR teardown releases its references and owned materials. Materials are
explicitly released even if a prepared twin never activated: Unity otherwise skips
`OnDestroy` for components whose `Awake` never ran.

## Stat-preview scope

Stat preparation reads the existing original `ActorStatPanel` and
`EnemyCurrentTurnStatPanel` images, including inactive descendants, deduplicates
them and populates the same `CardFaceMipBake` sprite/texture cache used by the live
preview. It never assigns replacement art into the original UI, activates a window,
calls native `Show`, changes the actor shown or constructs the second-hand panel.
The existing `PanelMipBake` setting still gates this work; disabling it during
preparation drains the queue without further bakes.

This warms only resources already authored/loaded in those original panels.
An asynchronously loaded actor portrait, condition icon or native layout that does
not exist yet keeps the existing live preview path. First conversion, native async
content and the singleton-safe second-hand snapshot are **not** eliminated by this
change. The fixture's cache boundary proves that the original shared resource API
is invoked; it does not measure the actual GPU readback or claim removal of the
whole measured 65–99 ms stat-panel step.

## Verification

The real Unity 2021.3.5 play-mode overlay fixture loads the shipped SpittingDrake
rig and original sleeping/flying clips. Final focused production passed **718
assertions**, including genuine rendered pose/cutout/depth checks, local/remote
cache reuse, current sleep/fly phase and material mask refresh, changed mesh/shader
fallback, immediate action release, and cleanup of a never-activated twin.

Causal negative controls detect stale prepared pose, missing current mask refresh,
changed mesh/shader reuse, destroyed pool lifetime, missing original stat-art cache
work, missing inactive material release, replaced immediate holds, destructive
timeout cancellation and changed LOD-table reuse. Existing native-controller restart,
frozen animation and force-rendering-off controls remain active. These are runtime
correctness checks; improved headset frame time remains a hardware measurement.

Focused receipts and exact production/native-bundle hashes are retained under
`.planning/debug/frame617-implementation/figures/`. `focused/run-4ubzqnhx` contains
the first six control results; `focused/run-9oq82pqh` contains final production and
the three additional current-mask/shader/material-lifetime controls. Additional
held-input, cancellation and LOD receipts are listed in `focused-summary.json`.
Production-only checks of the shared mirror boundaries also passed native actor
audit (140 assertions) and original actor bar pose (1,615 assertions). The integrated
final gate is the authority for complete suite coverage; a focused run is partial.
