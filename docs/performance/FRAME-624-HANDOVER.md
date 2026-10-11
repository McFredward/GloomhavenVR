# Steam Frame Build624 evidence and current isolated candidate

The implementation status below supersedes the initial cache-only handover. The
worker tree is now based on `dev` commit `01503d4c2e28ee50b0b3662debb48a9ad19f88f0`
(Build625). The supplied hardware capture remains Build624; it is not a candidate
measurement. The maintainer requested all other reversible measures, explicitly
rejected a 2.5D board, and will run the resolution comparison personally. No eye
resolution default changes. PC and Frame use one mod and asset set with different
fresh defaults; each compromise remains independently adjustable.

See [FRAME-625-IMPLEMENTATION.md](FRAME-625-IMPLEMENTATION.md) for the current
source, configuration, validation, historical clone regression and hardware checks.
The main `dev` checkout belongs to the external NPC integrator. This branch has
not been merged, pushed or handed to that agent. The maintainer explicitly requires
confirmation before handover. The integrator assigns the actual next common
ModBuild before distributing any changed binary.

The remainder is the retained first-stage analysis/checkpoint receipt, not current
implementation status. Its proposed 2.5D fallback is rejected and will not be built.

---

# Steam Frame Build 624 review and isolated handover

This round starts from `dev` commit `9cc7e589e847693facf77332e87e78e5509186b3`
on `work/steam-frame-radical-20261005`. The maintainer requested an isolated
worktree because the NPC multiplayer integrator owns the live development
checkout and another agent owns the Quest standalone branch. The maintainer
explicitly requires confirmation before this round is handed to the integrator.
Nothing in this round is merged into `dev`, pushed, or sent to that agent.

The supplied October 5 Frame capture identifies Build 624 in both game log sinks;
the binary banner reports `b123cefc9`, built 2026-10-05 16:01:31 UTC. Seven later
NPC/test commits lead to this worktree's `9cc7e589` source checkpoint. The
performance, defaults, wall-fade and converted-UI directories are unchanged
between those two commits; do not equate their complete runtime trees.
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
and incremental Debug diagnostics. Builds 623/624 also retain the NPC multiplayer
publication and original-content work owned by the integrator. Those are
implemented features; each capture must still prove their actual application.

In this run the room count goes directly from one to three through the original
debug reveal command. There is no clean two-room comparison. The three fully
loaded, worn three-room summary windows report 82.70, 61.04 and 103.46 ms mean
application frame times. They are ordinary moving views, not matched fixed-view
A/B measurements. Together they contain 810 frames over approximately 60.3 s,
with a frame-weighted mean of 74.41 ms (approximately 13.4 application frames/s).
Background interaction preparation continues after the native spinner closes;
these are not three identically warmed-resource windows. The last window is
approximately 9.7 application frames/s.
Its measured main-thread spans are 44.39 ms logic, 10.06 ms render callbacks and
49.01 ms unbracketed engine work/waits. Mod scope time is contained in those
spans and must not be added to them.

The original target remains 3408 by 3408 per eye in MultiPass. Scene decoration,
vegetation, figure detail and ambient-effect controls already use their minimum
settings. The environment readback reports 38 eligible surfaces, **zero render
chunks**, 38 unreadable originals and one simpler material. Enabling the chunk
setting therefore does not prove that this scenario receives draw reduction.
That complete environment readback precedes the all-room reveal; there is no
later complete environment readback proving its final three-room population.

Changing reported XR refresh from 72 to 24/12 during poor pacing must not redefine
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

This is a source-proven work-removal candidate, not a solution for a 100 ms scene.
The recorded environment validation span rises to 3.653 ms/frame in the final
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
5. Develop multiplayer in parallel with that budget. Benchmark 2 and 4 players,
   including Frame as host and client, with the three-room board open. Share exact
   immutable capture/decode products where ownership allows, cache static observer
   hierarchy state, and retain live intermediate motion and immediate content
   edges. Use the existing transport clocks and lossless formats. Do not hide
   remote cards/windows/NPCs, lower their fidelity or drop animation samples as
   an implicit optimization; their parity contracts remain in force.

The targets are explicit: 72 application Hz implies 13.89 ms/frame, while a planned
36 Hz application compromise implies 27.78 ms/frame and needs headset acceptance.
These are engineering budgets, not predicted outcomes or an automatic choice of
reprojection policy. At 103.46 ms a few more small caches cannot establish either
target. Reserve measured headroom for multiplayer rather than spending all savings
on singleplayer appearance.

An eye-scale experiment from 1.0 to 0.6 reduces the per-eye pixel target to about
2045 square, or 36% of the original pixel count. It can test pixel-related pressure
with a visible sharpness trade; it does not remove scene draw calls or the measured
logic span. Do not promise that resolution alone solves this capture.

## Integration and verification

The candidate and reports remain on the isolated branch. Focused production Unity
checks must exercise shared originals, native edits between actual camera renders,
per-renderer property blocks, foreign material replacements, ownership restitution
and warmed storage/allocations, including causal mutations. This isolated worker
delivery runs the complete affected render suites, all source gates, strict
Release, a separate compiled comparison and bilingual docs checks. The complete
local gate belongs on the final integration tree owned by the other main agent.

After the maintainer confirms handover, the NPC integrator can inspect and
cherry-pick the isolated commits into its latest `dev`, resolve only concrete
overlaps, assign the actual shared build, and run the complete required gate on
that final integrated tree before its already-authorized `origin/dev` push.
This candidate's standalone evidence cannot certify a different merged tree.

Validation receipt: all 14 source gates pass. The complete affected suites pass:
environment budget 733 production assertions across 44 production/negative variants;
structural instancing 23 assertions across 5 production/causal variants. The suite
scheduler correctly labels this selection as partial overall coverage. Strict
Release has zero errors/warnings. The private 9cc7e589 compiled baseline comparison
contains exactly one changed type, `ScenarioEnvironmentBudget`, with no additions,
removals or unexpected reference/resource changes. The separate already-built
golden-vector executable passes 308,248 assertions; this is the byte-vector stage,
not complete local suite coverage. Its first direct launch lacked `DOTNET_ROOT`;
the failed host receipt is retained separately and the corrected launch uses the
installed runtime. No Frame FPS improvement has been measured, and no new
shader/eye picture is claimed.

The optional 137-suite pre-integration attempt was stopped to avoid competing with
the integrator's seven-job full NPC gate and the active Quest build on a memory-
constrained machine. Its original receipt retains 5 passes and 132 cancellations;
it is not complete or successful coverage. The full-guard wrapper's generic
"wire format changed" message follows the cancellation; it is not a failing byte
vector. The source group had already completed successfully. All receipts remain
in this worktree's `.planning/debug/frame624-candidate/` and `.planning/debug/test-runs/`.
The final integrated `dev` still requires the full local gate, including golden
vectors, before push; this report explicitly does not waive that requirement.
The independent receipt/hash verification is in
`.planning/debug/frame624-candidate/validation-ledger.json`.
