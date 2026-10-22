# Build717 integration validation (2026-10-22)

## Scope and evidence boundaries

The source baseline is `d4892a097` (Build716 behavior plus trailer documentation).
All four hardware clients supplied for this review ran release Build715. The
implementation and paired-log analysis are in `MP717-SHARED-WINDOWS.md` and
`MP717-LATEJOIN.md`. Existing synchronized manual window movement/resize remains;
initial placement uses the native VR host, or one compatible VR presenter when
the native host is flat. Native decisions, readiness and continuation retain
their original authority.

Automated results below prove specific source, byte and runtime contracts. They
do not establish the picture, native network timing or interaction in a headset.
The video was sampled locally; it does not supply simultaneous views from all
four participants.

## Integrated checks

* Strict Release: zero warnings/errors on the final implementation. Main receipts
  are `.planning/debug/mp717/readiness-final-strict-build.log` and `final-deliverable-build.log` beside it.
* Source group:16/16 passed, receipt
  `test-runs/20261022-224422-f3dbde69`. Only the two corrected diagnostic tails
  subsequently changed; the final surface comparison retains their existing grep
  tokens and has no removals.
* Complete, unfiltered golden vectors on final integrated production source:
  **301541 assertions passed**, compilation0warnings/0errors. Receipt
  `.planning/debug/mp717-final-golden/run-hsmyu747` includes every production
  source hash, test input hashes, exact commit, output and completion cleanup.
  The earlier worker's5901 focused assertions/six compiling controls separately
  validate121's independent literal bytes and failure directions.
* All four committed UnityFS banks and the figure-bank manifest pass the original
  bundle-format checks.
* Surface comparison against the private original baseline: config669→669,
  patches242→242, log markers4811→4816; **no removed key, patch or marker**.
* Compiled review against that same private baseline:21 intended implementation
  types, five new types and nine metadata/inlined-constant-only differences.
  There are no unexpected or removed types. The generated project differs only
  in relative reference-path depth; its assembly references/order are identical.
  This is an intentional behavior change, not a pure-refactor equivalence claim.
  Receipts: `.planning/debug/mp717/compiled-review-final.log` and
  `compiled/manual-review.json`.

The original `refactor-guard.sh check --summary` invocation runs source checks,
the scheduler tests and the complete local suite sweep in an isolated worker
snapshot (`bd92b20ce`). It does not continue past failed local fixtures. Final
wire, bundle, surface and compiled phases were therefore executed explicitly;
they are never represented as a successful exit from that original invocation.
The scheduler's19 tests pass with two expected platform skips.

The complete240-suite sweep records232 passing suites and eight failures, all
preserved in `worker-evidence/full-sweep.tar.zst`. Each failed suite now has a
passing focused receipt on its affected final source. The current manifest hash
matches the complete sweep. Consequently all240 suites have passing coverage
composed from that sweep and the bounded repairs; this is **not a new single green
complete invocation**. The machine-readable receipt map is
`.planning/debug/mp717/composed-local-coverage.json`.

The six fixture/compiler failures were obsolete reward/video/native-close ports,
map admission and item-clip source bindings, and a duplicate central wire-source
link. All were repaired without weakening their assertions. The two remaining
failures were the unchanged NPC658 desktop renderer cases, repeated serially
below. Latest shared geometry, pose/codec, native map readiness and hand-selection
changes additionally use the focused final implementation checks in the next
table. Unchanged passing areas retain their earlier complete-sweep evidence.

## Focused final implementation coverage

| Contract | Passing final evidence |
| --- | --- |
| Exact shared geometry, six kinds, both authored aspects, different map domains and later zoom | Actual Unity549 assertions +22 compiling causal controls |
|119/121 packet context, capture/adoption/republication, initial/live pose, host/manual election, video and complete reward handoff | Production kernels588 assertions +29 compiling causal controls |
| Native map initialization, complete active-surface census, unknown controller, readied replacement, reordered closes, proposal/retirement |1288 assertions twice +45 compiling causal controls |
| Original map input admission and dispatch |2012 assertions +9 causal controls |
| Late assigned owned hand, original queued switch and revalidation |47 assertions +6 causal controls |
| Reward presentation/continuation |3275 assertions +27 controls; pose110 +8; independent placement/election29 +6 |
| Video receipt/playback and original lifecycle |83 assertions +17 controls; unchanged native-video presentation also passes in the complete sweep |
| Native item clip parent and lifecycle |645 assertions +6 controls, plus source-connection controls |
| Native town-window close admission and callback scope |113 assertions +13 controls |
| Local card slots → actual packet codec → remote board overlays |836 runtime assertions +11 compiling causal controls |

The later diagnostic corrections were checked by an additional portable
production588-assertion run and a complete affected reward-placement rerun;
unchanged causal controls are inherited explicitly. Four existing affected
reward/video suites also pass after their fixture boundary repairs.

## Preserved failures and cleanup

The initial complete-sweep failures and the initial full-golden source-binding
failures remain retained. Repairs update stale fixture boundaries and assertions;
no failing production assertion or deadline was removed. In the unchanged NPC658
first-picture workload, the original1.060144s missed its1s desktop wall-clock
limit; a serial repeat measured0.7917255s with the same45 required/82 prepared
nodes,46 members,12 events and9511 bytes. Its historical-source negative control
also passes serially by reaching the intended failing deadline at1.411889s. The
original Unity process crash was not reproduced. These results support transient
desktop load/process sensitivity without identifying that crash's exact cause or
proving headset latency.

Completed worker test caches were removed using the canonical retention API.
Before removing our completed worktrees, their evidence was archived under
`.planning/debug/mp717/worker-evidence/`, byte-compared with its source and given
SHA256 sidecars. The current sources and deliverables, all hardware logs/video,
failed results, source/manifest/capture evidence and other agents' work remain.

## Required next hardware review

Use Build717 on every VR client. Review all shared kinds with independent zooms,
host gaze placement, late joins and a subsequent manual move/resize. Check quest
clear/reselect while somebody is readied, City/World transitions and committed
host proposals. The fourth player's newly assigned owned hand should become
playable through its original native SwitchHand without a manual figure click,
once genuine native phase/modal/loading gates allow it. Review mixed2D/3D movie
and reward placement as well. These headset outcomes remain unverified.
