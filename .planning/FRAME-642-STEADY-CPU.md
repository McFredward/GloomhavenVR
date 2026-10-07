# Steam Frame steady-render CPU follow-up (integration slot642)

## Evidence and scope

Build638/source9870bd802 Frame, PC and remote banners were verified together.
The hardware inputs and parser receipt remain in the main checkout's ignored
`.planning/debug/frame638-followup/`; this follow-up adds no headset result.
Only fully loaded rendering is targeted.

The selected three-room windows average74.139ms/frame and39.968ms measured mod
work. The larger five-room windows average70.969ms/frame and40.650ms mod work.
In the latter, terrain9.069ms, world materials10.578ms and walls6.423ms total
26.070ms/frame. Nested timers must not be added again. Generic XR GPU timing
does not identify the remaining engine/wait time as GPU-busy work.

## Implementation

- Terrain stops examining the prepared remainder after its existing per-camera
  admission cap. Previous leases are recovered first; unexamined sources keep
  native output. Canonical material and factory results are reused only within
  the synchronous pass and invalidated at native writes and camera boundaries.
- World materials enumerate current registered, locally held and remotely held
  prop roots once per pass. Inactive renderers avoid redundant mesh queries;
  shader routes, keywords and ambient settings share current-pass reads.
  Renderer-wide and per-slot property blocks remain fresh. Nested native camera
  entries invalidate prepared material metadata, including shader/tint changes.
- Refusal revocation uses actual terrain leases and active or queued environment
  consumers. Prepared terrain membership remains a separate ownership decision.
  A previously refused source can revoke a newly adopted substitute again;
  an unchanged prepared-only source avoids needless revocation/re-adoption.
- Walls rebuild unused home maps only when faded candidates exist, check current
  owned membership before bounds, replace quadratic census membership with exact
  reference indexing and check the existing diagnostic deadline before scanning.
  Native election, fades and report cadence remain unchanged.

The existing `Optimize/SharedEnvironmentMaterialReads` control retains independent
reads for comparison when off. Other material, terrain and wall controls remain.
No new profile-specific binary, quality limit, room cap, resolution default,
network payload, mesh bank, asset bundle or visible animation policy is added.
`ScenarioTerrain.BudgetDeferred` measures the unexamined prepared remainder,
including inactive sources; it is not a visible draw or fallback count.
New detailed world counters remain guarded Debug diagnostics.

## Validation

The source group passed15/15. The complete local154-suite attempt recorded
147passes and7NPC test-binding failures. Those records remain red; bounded
repairs are recorded separately rather than called a fresh complete-gate pass.
The NPC main agent owns the upstream source/fixture integration. Unchanged
controls retain their earlier passing evidence.

The broad attempt passed all64terrain and46world runtime variants. The final
lease/refusal follow-up passed actual Unity production352terrain assertions plus
one affected control, and623world assertions plus four affected controls.
Integrated bridge checks cover20source contracts with13controls. Wall maintenance
checks use explicit Unity boundary surrogates; they do not establish native pixels.
All61final terrain/world source hashes remained identical after rebasing onto639.

Strict Debug and Release builds passed with zero warnings/errors. Current639
wire golden vectors passed299713assertions. Bundle format and surface retention
passed. A private compiled comparison confines CPU changes to PropGrab,
CoreModule, ScenarioTerrainBudget, WorldMaterialBudget and WallSegmentFade;
upstream639 changes are classified separately. This is an intentional behavior
change, not a zero-diff refactor.

Durable main-checkout evidence, manifests, failure logs and the precise inherited
scope are in `.planning/debug/frame638-steady-followup/`. Software assertions
and omitted-query counts do not establish headset FPS or visual acceptance.
Multiplayer Frame rendering still needs a paired hardware run.

## Integration and next hardware comparison

The NPC main agent owns final stamp/merge/push:639NPC,640menu/viewport,
641crossplay, then642CPU. The CPU worker does not change NetProtocol.ModBuild
or shared integration documents. Keep the existing radical graphics settings;
compare the shared-read control on/off in the same fully loaded large scene
with similar view/head motion. Inspect complete frame/mod timing and the new
terrain-deferred and world-root counters without adding nested timings together.

Retain full scenery, native material appearance, hand interaction and smooth
animations during both runs. Verify both eyes, native material changes, multiple
rooms and a multiplayer visitor. Target headset outcomes remain unverified.
