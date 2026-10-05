# Steam Frame Build624 review and isolated handover

This round starts from `dev` commit `9cc7e589e847693facf77332e87e78e5509186b3`
on `work/steam-frame-radical-20261005`. The maintainer requested an isolated
worktree because the NPC multiplayer integrator owns the live development
checkout and another agent owns the Quest standalone branch. The maintainer
explicitly requires confirmation before this round is handed to the integrator.
Nothing in this round is merged into `dev`, pushed, or sent to that agent.

The supplied October 5 Frame capture identifies Build624 in both game log sinks.
It is the hardware baseline, not a measurement of the candidate change below.
The append-only OpenXR file includes older startup entries; it does not establish
a second current run. No screenshot was supplied with these three files.

## Evidence and existing work

The [log audit](FRAME-624-LOG-AUDIT.md) records immutable input hashes, exact
line references, loading boundaries, applied settings, errors and measurement
limits. The [source strategies](FRAME-624-RADICAL-STRATEGIES.md) connect the
remaining costs to concrete implementation paths.

Existing work already includes generated-decoration removal and creation
avoidance, native coarse and derivative figure meshes, optional ambient-effects
suppression, disabled figure cloth, scoped two-bone skinning, audited idle
transform culling, native camera suspension, original-resource preparation,
UI inventories/maintenance intervals, wall dependency and geometry-read work,
cheaper compatible environment materials, bounded static render substitutes,
and incremental Debug diagnostics. Builds623/624 also retain the NPC multiplayer
publication and original-content work owned by the integrator. Those are
implemented features; each capture must still prove their actual application.

In this run the room count goes directly from one to three through the original
debug reveal command. There is no clean two-room comparison. The three fully
loaded, worn three-room summary windows report 82.70, 61.04 and 103.46 ms mean
application frame times. They are ordinary moving views, not matched fixed-view
A/B measurements. The last window is approximately 9.7 application frames/s.
Its measured main-thread spans are 44.39 ms logic, 10.06 ms render callbacks and
49.01 ms unbracketed engine work/waits. Mod scope time is contained in those
spans and must not be added to them.

The original target remains 3408 by 3408 per eye in MultiPass. Scene decoration,
vegetation, figure detail and ambient-effect controls already use their minimum
settings. The environment readback reports 38 eligible surfaces, **zero render
chunks**, 38 unreadable originals and one simpler material. Enabling the chunk
setting therefore does not prove that this scenario receives draw reduction.

Changing reported XR refresh from72 to24/12 during poor pacing must not redefine
the desired smooth-play target. The XR GPU field and unbracketed residual do not
establish actual GPU busy time. In particular, the existing log's statement that
a below-interval GPU reading proves headroom is too strong; this runtime requires
independent evidence. Native inclusive probes cover selected callbacks rather
than all engine work, and their summary sample is capped. The capture also does
not establish the incremental cost of an active multiplayer party.

## Candidate source change

`ScenarioEnvironmentBudget.RetireChangedNativeMaterials` currently repeats
shader, keyword and property queries for the same original material on every
admitted renderer before every camera cull. The candidate shares the material
verdict only within one synchronous validation invocation using reusable storage.
Each renderer still gets its own live property-block check. Both MultiPass eyes,
nested cameras, native material edits, source visibility, draw leases and exact
owned restoration keep their existing validation boundaries.

No verdict survives a callback. This deliberately avoids a once-per-frame cache:
native effects can change between cameras or eyes. It changes neither shader
output nor gameplay, remote presentation, wire cadence or original content.
It introduces no setting, asset, patch registration or build number change.
The common integrator must assign the next actual shared ModBuild and write its
build note after integration, before giving any changed binary to players.

This is a source-proven work-removal candidate, not a solution for a100 ms scene.
The recorded environment validation span rises to3.653 ms/frame in the final
summary; it is not an independently measured saving or an exclusive CPU budget.
The useful next experiment must address the representation and submission of
the retained scene as well.

## Proposed next implementation sequence

1. Measure the candidate in the same three-room scenario with matching settings;
   verify native walls, room reveal and both eyes before expanding scope. Record
   application frame distribution, logic/render spans and actual draw/GPU data
   where available. Keep cold loading and preparation separate.
2. Prototype private, provenance-validated render copies for the actual floor and
   structural assets, then bounded tile/cell substitutes. This must solve the
   unreadable-mesh rejection rather than merely enable another batching flag.
   Leave native source objects, material slots, game references and colliders
   intact. Preserve original reveal, wall/effect changes and command-buffer
   fallback. Do not resurrect Unity internal static batching: its source cloning
   failure is documented in `.planning/static-batching-removed.md`.
3. Offer an explicit **minimal 3D board mode** if the exact render substitution
   cannot meet the budget: simpler low-detail room shells and inexpensive
   materials, followed by progressively coarser already-revealed rooms. Keep
   every revealed playable cell, door, obstacle, actor identity, condition and
   interaction represented. Define wall-transition and visible-geometry tradeoffs
   concretely with the maintainer before implementing a new presentation exception.
4. If that still fails, define a **tactical 2.5D board mode** with planar room
   artwork and lightweight pieces, while hands, held cards and original readable
   UI remain responsive in VR. This is a larger visible compromise, with dedicated
   choices for elevation, pickup and action presentation. It is proposed, not
   authorized as a silent default or implemented by this round.
5. Develop multiplayer in parallel with that budget. Benchmark2 and4 players,
   including Frame as host and client, with the three-room board open. Share exact
   immutable capture/decode products where ownership allows, cache static observer
   hierarchy state, and retain live intermediate motion and immediate content
   edges. Use the existing transport clocks and lossless formats. Do not hide
   remote cards/windows/NPCs, lower their fidelity or drop animation samples as
   an implicit optimization; their parity contracts remain in force.

The targets are explicit:72 application Hz implies13.89 ms/frame, while a planned
36 Hz application compromise implies27.78 ms/frame and needs headset acceptance.
These are engineering budgets, not predicted outcomes or an automatic choice of
reprojection policy. At103.46 ms a few more small caches cannot establish either
target. Reserve measured headroom for multiplayer rather than spending all savings
on singleplayer appearance.

An eye-scale experiment from1.0 to0.6 reduces the per-eye pixel target to about
2045 square, or36% of the original pixel count. It can test pixel-related pressure
with a visible sharpness trade; it does not remove scene draw calls or the measured
logic span. Do not promise that resolution alone solves this capture.

## Integration and verification

The candidate and reports remain on the isolated branch. Focused production Unity
checks must exercise shared originals, native edits between actual camera renders,
per-renderer property blocks, foreign material replacements, ownership restitution
and warmed storage/allocations, including causal mutations. The complete local
gate, strict Release and bilingual docs checks run on the final candidate tree;
the exact result and limits are recorded below after completion.

After the maintainer confirms handover, the NPC integrator can inspect and
cherry-pick the isolated commits into its latest `dev`, resolve only concrete
overlaps, assign the actual shared build, and run the complete required gate on
that final integrated tree before its already-authorized `origin/dev` push.
This candidate's standalone evidence cannot certify a different merged tree.

Validation receipt: pending final candidate integration and checks.
