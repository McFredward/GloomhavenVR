# Build650: cheaper existing world-material validation

Integration base: `f006ecf881246ce57ef535d7dfab91c860b09eae` (Build649).
Runtime candidate: `a68f268319a8abd5686214491698ef9551c54402`, developed
from current dev's Build648 before the independent649 wrist UI integration.
The three renderer partials are byte-identical in the worker and integration.

## Authorized scope and behavior

Following the647 regression and648 rollback, the maintainer selected strategy1:
reduce the existing material path's recurring CPU work and verify an expected
net improvement before delivery. No room renderer, wider geometry admission,
asset bank, shader, profile, graphics setting or wire-layout change is included.
These universal work reductions have no player option or visible quality trade.
The existing material-quality choices retain their independent behavior.

- Each admitted surface retains its Renderer's exact owning GameObject and
  Transform references. Those identities are fixed for that component lifetime.
  Current layer, activation, parent, scene, names and components are still read.
  MeshFilter identity/sharedMesh and registered/local/remote held roots stay live.
- Each current ancestry node's name is read once in its callback-free iteration.
  This removes a duplicate native getter; no name or scope verdict is persisted
  across eyes, frames, nested cameras or native writes.
- Freshly empty property blocks skip probes for absent video/effect/blend values.
  Both emptiness values are sampled inside the slot effect reader after current
  source resolution and indexed-block retrieval. Neither value crosses an
  earlier-slot preparation or another callback-capable boundary. Nonempty
  blocks retain the original typed presence checks, including zero overrides.

The cache adds two managed references per admitted surface, approximately7KiB
for440 surfaces on a64-bit runtime. Surface retirement and RestoreAll release
them. It adds no per-frame collections, persistent material verdicts or new
native ownership. Geometry, material values, effects, visibility, continuous
wall fading, native cloning/restoration and final camera boundaries remain
authoritative. The larger647 floor experiment remains withdrawn.

## Performance evidence and its limits

The independent harness compiles the complete old and candidate PreCull paths
into separate assemblies and executes both in the same actual Unity2021.3.5
Mono process. Timed calls use real Unity objects/APIs and a bound delegate;
no native-call observer is inserted. Warmed old/new batches alternate order,
with collection outside timing and matched workload receipts/source hashes.
The native controllers, configuration, producer/consumer bridges and PerfMonitor
are explicit fixture boundaries. This is neither the original game scene nor
a measurement of GPU execution, multiplayer load or Steam Frame FPS.

The final corrected representative shape contains440 candidates/eye,
376 active sources,402 material slots,23 shared originals and581 current
ancestry-node reads. Both eyes execute for each measured application frame.
The true both-block case verifies376 populated renderer-wide blocks and402
populated indexed blocks. Its21 interleaved rounds confirm a net CPU gain.

| Complete material path, both eyes | Old median ms | Candidate median ms | Paired median saving |
| --- | ---: | ---: | ---: |
| Representative, no MPBs |3.481|2.900|16.52%|
| Renderer-wide MPBs |4.186|3.411|18.54%|
| Indexed MPBs |4.217|3.412|18.99%|
| Both MPB levels populated |4.222|3.694|12.46%|

Paired bootstrap95% intervals are15.95–17.21%,18.14–19.23%,
18.62–19.64% and11.64–12.65%, respectively. Ratios of the separate lane
medians and medians of paired savings are different aggregations.
The representative P95s also decrease. Native-write, options/debug, excluded,
unique-original, unsupported and deep/nested-read cases are measured separately.
Early video/animated MPB vetoes and four-slot Standard overrides receive their
own repeated comparisons to price the added emptiness checks.

One artificial all440-source two-slot/both-block stress case initially showed
a higher P95 despite a lower median. Its original samples remain retained.
Two fresh isolated21-round repeats show9.64% and9.54% paired CPU savings
with95% intervals7.91–10.67% and9.01–11.06%; both P95s decrease. The earlier
tail rise was not reproduced. Coordinated test lanes were idle during these
repeats; global OS activity was not measured. This supports an expected net
gain without guaranteeing every individual sample or tail. Preliminary
fixtures also had inaccurate topology/block labels; their unchanged receipts
remain historical evidence rather than the final representative result.

Unity Mono's thread-allocation API returns0 even for a retained64KiB allocation
positive control. It cannot establish allocation-free operation or a measured
byte reduction. Noisy native heap deltas are not an allocation counter either.
Only the bounded added owner-reference storage is reported as a size estimate.

## Validation ledger

Integration source16/16 and strict Debug/Release builds pass with0 warnings or
errors. Direct golden vectors pass299,714 assertions. Private compiled snapshots
retain1238 types: WorldMaterialBudget changes; eight other differences are
exactly the numeric649-to650 build constant. No unrelated compiled behavior
changes. The shared integration baseline is never overwritten.

Actual Camera.Render production checks pass765 assertions, retaining
all736 previous assertions and29 new cases. The added cases cover empty/nonempty
wide/indexed blocks, tint pixels, live indexed animation, video/effect refusal,
Standard zero blend presence, current Preview/Generated Content renames and
destroyed/recreated Renderer identity. All64 previous runtime variants also pass.
Two affected-suite attempts retain a harness expectation-order failure: the
deliberate broken Standard/wide-MPB controls are caught by a different valid
assertion before the named expected assertion. No production assertion is
removed or weakened. The final affected run executes production and all67
negative variants successfully (68 total), with exit0 and unchanged bound
sources. This is one complete focused material-runtime run, not a repository
complete-gate pass. Its receipt is `runtime-complete/run-abs34ya_` in the
independent proof payload.

All39 runtime-bound inputs are hash-identical in the final worker run and the
Build650 integration, including unchanged native-boundary sources and external
dependencies. The integrator reuses that focused runtime evidence without
claiming another execution of the68 variants.

This bounded repair uses focused validation and inherits the unchanged648/649
evidence for NPC, terrain, environment, assets, graphics controls and UI.
It is not a new complete176-suite pass. A full new wrapper/gate is not claimed
merely because source16 or direct goldens passed.

Local receipts reside in `.planning/debug/frame650/`; the independent proof
and implementation payloads are preserved under its `workers/` directory as
verified archives with per-file hashes before removing the two worker worktrees.
Failed attempts, raw samples and generated assemblies remain available.
The independent proof
and implementation reports document the exact run IDs, failures and coverage:
[CPU implementation](../docs/performance/FRAME-650-WORLD-CPU.md),
[independent proof](../docs/performance/FRAME-650-WORLD-PROOF.md).

A fully loaded same-view Frame comparison with options closed remains necessary
to determine the actual whole-game frame-time gain. Both VR peers use650.
