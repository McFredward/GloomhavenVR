# Frame650 world-material CPU proof

## Decision and scope

Candidate production commit `a68f268319a8abd5686214491698ef9551c54402`
removes repeated exact-owner lookups, samples the current scope-node name once,
and omits absent-property calls on actually empty native property-block levels.
It does not cache native visibility, layers, parents, names, mesh/component
membership, material values, effects or ownership across camera invocations.
No shader, geometry, quality setting, default, asset or wire change is part of
this proof lane. These are unconditional removals of redundant work.

The measured complete CPU path is consistently cheaper for the representative
loaded-room workloads. This supports expecting a net CPU improvement before
delivery; it does not establish a Steam Frame FPS gain. The previous Frame run
was not an identical-view benchmark, and no 650 headset capture exists.

## Reproduction

```sh
python3 scripts/check-world-material-cpu.py --source-root /path/to/candidate
python3 scripts/check-world-material-runtime.py --source-root /path/to/candidate
```

The CPU lane compiles complete actual `WorldMaterialBudget*.cs`, the actual final
camera boundary, and production's exact pure local-prop readers. Baseline is pinned
to 648 commit `d47fa87414c61736076540a4a27db5963f3bcd93`; candidate source hashes
and both compiled assemblies are retained with each run. Both execute in the
same Unity 2021.3.5f1 Mono process. `Driver.PreCull` is bound once to a delegate;
timed batches include two complete eye invocations and, where selected, native
writes, nested factory passes or two additional unreachable options captures.
Actual Unity MeshRenderers, transforms, components, materials and MPBs execute.

Twenty-one 24-frame batches alternate baseline/candidate and candidate/baseline.
Each fixture has 24 warm frames plus 16 warm batch frames. Reflection, object
creation, shader import, JSON/reporting, shape inspection and forced symmetric
collection occur outside timed regions. Native API calls are not replaced or
Harmony-instrumented. Outer per-frame timestamps provide CPU frame distributions.
Native scene/controllers/config, shader resolution and PerfMonitor remain explicit
fixture boundaries; the timing boundary uses the same quiet scope in both lanes.
This fixture is not an original scenario, GPU benchmark or multiplayer acceptance.

## Representative measurements

The final fixtures have 440 registered sources, 376 active renderers, 402 active
material slots, 374 supported slots, 28 unsupported slots, 23 supported shared originals
plus one unsupported original, and 581 current scope nodes per world-eye invocation.
These approximate the supplied 645 counts (~440 sources,~64 inactive,~374 variants,
~28 native slots,~23 material refreshes and ~580 scope nodes per eye). MPB density in
the supplied capture is unknown, so the proof covers four distinct populations.
Both eyes are already included in every figure below.

| Actual workload |648 median ms/frame | Candidate | Paired median saving,95% interval |
|---|---:|---:|---:|
| No native MPBs |3.4810 |2.8996 |16.52% [15.95,17.21] |
| Renderer-level MPBs, indexed levels empty |4.1858 |3.4114 |18.54% [18.14,19.23] |
| Indexed MPBs, renderer levels empty |4.2169 |3.4122 |18.99% [18.62,19.64] |
| Both levels populated, every indexed slot populated |4.2224 |3.6942 |12.46% [11.64,12.65] |
| Two extra excluded options captures, Debug |3.7470 |3.1123 |16.96% [16.80,17.46] |
| Native material/keyword/block/slot writes |4.2126 |3.3838 |19.17% [18.20,19.71] |

Representative CPU-frame P95 also decreases in all six workloads. These are
CPU-path timestamps, not whole-game frame times. The interval uses 5000 bootstrap
resamples of paired batch ratios with deterministic seed 650; it is not a bound
on future headset behavior or a promise that every individual frame is faster.

Worst-route 21-batch tests retain a complete populated four-slot workload while
native MPBs veto simplification at the earliest video/scalar branch:

| Early refusal workload |648 median ms/frame | Candidate | Paired saving |
|---|---:|---:|---:|
| Renderer-level RenderTexture |3.4427 |3.0681 |10.93% |
| Renderer-level animated scalar |3.5531 |3.1876 |10.21% |
| Indexed RenderTexture |3.5648 |3.0978 |13.20% |
| Indexed animated scalar |3.6683 |3.0989 |15.21% |
| Four Standard slots, both levels populated |6.9053 |6.4746 |6.12% |

All five early-refusal P95s decrease. An independent both-level representative
repeat measures 4.1797→3.6278ms, paired saving 13.12% [12.90,13.73]. A 440-unique-
original stress case only saves 2.59% [1.36,3.16], consistent with the unchanged
native material refresh work dominating that deliberately extreme population.
The settled Off route remains effectively identical at a fraction of a microsecond;
percentages at that scale are measurement noise, not an optimization claim.

One fully populated two-slot stress run has an increased P95 despite an 8.12%
paired median saving. That observation is retained. A subsequent coordinated
quiet repeat runs that exact population twice with fresh fixtures: paired savings
9.64% [7.91,10.67] and 9.54% [9.01,11.06], with P95 decreasing 9.5358→8.0744ms
and 7.1644→7.0210ms. Both four-slot and representative controls also improve.
The earlier tail rise does not recur, but absolute host tails vary substantially.
There is no proven event cause and no promise that every individual frame is
faster. The quiet repeat did not overlap this lane's Unity correctness runs;
the primary/implementation lanes completed heavy checks and agreed to remain
idle during it. A global host process census was not recorded.

## Measurement corrections and memory limits

The first broad 21-workload run used 140 intermediate transforms, 64 of which belonged
to inactive sources, so its representative scope count was below the hardware
target. A substring selector also classified two/four-slot workload names as
indexed-only. Both issues were identified by independent review. Raw receipts
remain intact; only the corrected 581-node final run above supports the stated
representative numbers. Explicit native `isEmpty` shape reports prove which block
levels and indexed slots were actually populated.

Unity's `GC.GetAllocatedBytesForCurrentThread` returns zero even for a rooted
65,536-byte positive-control array in this runtime. It is invalid here: zero
reported bytes never establishes allocation-free execution. Mono heap deltas are
retained as raw diagnostics but vary with collection/free-list state and do not
establish allocated bytes. No measured byte reduction or GC-speed gain is claimed.
The source adds two exact owner references per Surface (~7 KiB for 440 sources on a
64-bit runtime) and removes one current name getter per examined scope node.
No cross-frame component or material-value cache is introduced.

## Correctness and evidence ledger

The existing 736-assertion actual-source Unity suite remains intact. Twenty-nine
new assertions bring production to 765: empty/nonempty renderer/indexed MPB
transitions, actual tint pixels and native block precedence, slot-local animation,
zero-valued Standard blend overrides, current Preview/Generated Content renaming
and destroyed/recreated Renderer identity. Existing immediate material/shader/
keyword/pass/mesh/scope/held-root/native-writer, camera-mask, command-buffer,
late-native-boundary, cloning, Off, additive-scene, native wall-fade and factory-
consumer tests continue to execute.

Original 64 runtime variants are preserved. Four additional causal controls force
nonempty renderer/indexed blocks to be skipped, incorrectly accept either empty
level as sufficient, and cache mutable node names across eyes. The final focused
run passes production 765 and all 68 runtime variants with exit 0 and unchanged
input hashes. A source-anchor adaptation
retains exact layer mutation controls for both the old direct owner getter and
the current stored exact owner, and retains the video refusal mutation for both
equivalent guard forms. Assertions or acceptance thresholds were not weakened.
The first 68 attempt records 67 passes: the original Standard-mode mutation is
rejected by a new earlier zero-blend assertion rather than its expected original
assertion. Moving the 29 new cases after the existing family checks restores that
original control's first-rejection predicate. The second 68 attempt also records
67 passes: the new renderer-wide-skip control is now correctly rejected earlier
by that same existing native Standard MPB assertion. Only the new control's exact
expected message is corrected to that causal rejection. Both failures remain
archived; no production change or relaxed runtime predicate is involved.

Private receipts under `.planning/debug/frame650-proof/`:

- `cpu-final/run-rva0cr3b`: corrected topology, 21 paired batches ×24 frames ×12
  workloads; original inputs, assemblies, shape reports, raw per-frame samples,
  positive allocation controls, source-stability report and paired statistics.
- `cpu-corners/run-fdbbzolc`:21×24×6 early-refusal/independent-repeat workloads.
- `cpu-tail-repeat/run-y621k0p9`:21×24×4 coordinated quiet workloads, including
  two fresh fully populated two-slot fixtures and per-frame tail distributions.
- `cpu/run-o5z1lzx4`: original broad 15×24×21 coverage, with corrected interpretation
  of its smaller topology and indexed-only labels; not the final representative run.
- `cpu/run-i9gc7me4`: narrow 51 candidate 11×24×5 first experiment; not final delivery.
- `cpu/run-u23xv2oj`: initial fixture compilation failure, retained.
- `runtime-initial/run-zrmul_pg`: production 765 passing receipt.
- `runtime-final/run-ga1oqw7r`: first 68-variant attempt, 67 passes and one
  deliberate-mutant expected-message mismatch.
- `runtime-reordered-final/run-i285_n7b`: second 68-variant attempt, 67 passes,
  all original 64 variants accepted, one new control's expected-message mismatch.
- `runtime-complete/run-abs34ya_`: final complete focused 68-variant PASS,
  production 765, exit 0, unchanged source hashes.

The primary integration lane owns final strict builds, source checks, compiled
scope review and inherited evidence for unchanged subsystems. These focused
measurements do not constitute a new complete local-gate or headset pass.

Existing nested-read-pass and actual late-camera mutation cases remain covered.
This lane does not claim a new proof of arbitrary reentrant callbacks into all
shared production lists; no native block emptiness value is carried across such
a callback in the delivered code. Counts of eliminated calls are source-derived,
not profiler samples: the representative current path removes 2384 exact component
owner/transform getters and 1162 duplicate current-name getters per two-eye frame.
The source continues to sample all mutable ownership and effect guards.
