# Risk-based testing strategy

Effective 2026-10-27, following the maintainer's request to shorten routine changes
while retaining useful regression coverage. This is the current policy for all
agents and contributors. It supersedes older instructions requiring the complete
local gate after every edit or every bounded repair. Technical contracts,
multiplayer parity, storage cleanup and hosted release checks still apply.

## Decision and evidence

Use focused checks for a bounded change, the complete gate for changes with broad
impact, and periodic complete integration checkpoints. Keep the full inventory;
choose when to execute it. Select tests by affected behavior and dependencies,
including callers and consumers, rather than by the number of edited lines.

The existing infrastructure already supports this policy. At local dev
`b1bf8eb748328caaa0806e3887cbfbe947f396cc`, the manifest lists 258 local,
114 hosted and 16 source suites. Counts change; discover the current inventory
with `scripts/run-test-suites.py --group local --list`.

Historical reports in the main checkout's ignored `.planning/debug/` show:

| Report | Recorded scope | Wall time | Outcome |
|---|---|---|---|
| `menu728/full-gate/suites/results.json` | 252 local suites, four jobs | 48.2 min | 250 passed, two failed |
| `menu730/full-gate/suites/results.json` | 258 local suites, four jobs | 56.7 min | 255 passed, three failed |
| Issues18–24 focused board run | Six affected suites | 2.2 min | Six passed |
| Issues18–24 focused map run | Eight affected suites | 2.3 min | Eight passed |
| Issues18–24 source run | 16 source suites | 25 sec | 16 passed |

These are existing measurements, not new passes or promises for future changes.
The focused report paths are
`issues18-24/integration-evidence/{board,map,source}/results.json`.
The long runs measure the local scheduler only, excluding other build, golden
vector and compiled-review work. The failed complete invocations remain failed;
later repairs can provide explicitly identified aggregate coverage.

Large projects separate fast feedback from expensive validation. Google's
[test-size guidance](https://testing.googleblog.com/2010/12/test-sizes.html)
classifies tests by resources and isolation, and its
[testing guidance](https://testing.googleblog.com/2015/04/just-say-no-to-more-end-to-end-tests.html)
recommends many small checks plus fewer integration and end-to-end checks.
Meta describes [selecting relevant tests for each change](https://engineering.fb.com/2018/11/21/developer-tools/predictive-test-selection/).
[Bazel's cache model](https://bazel.build/remote/caching) makes explicit inputs,
commands and environment part of reusable build actions. Our application of
these ideas is conservative manual selection and input-bound evidence reuse;
we do not have Meta's prediction model or Bazel's dependency graph.

## Required scope before a dev push

The integrator chooses the class before implementation and records one short
reason. When dependencies or authority effects cannot be bounded, choose C.
The times below are feedback targets, not timeouts or permission to omit tests.
They exclude implementation and waiting for hardware.

| Class | Scope and examples | Required validation | Target |
|---|---|---|---|
| A: documentation/content | Developer prose, player instructions, standalone media with no runtime/package change | Review final diff and local links; docs-i18n for player docs; relevant content validation. No runtime build or full gate solely for prose. | 1–3 min |
| B: bounded behavior | One known subsystem, its dependencies and consumers; isolated bug fix, localized UI behavior, limited fixture repair | Fresh strict build for C# changes; all source suites for production C# changes; affected runtime suites and controls; final diff/scope review; applicable surface, localization and visual checks. Identify reusable evidence for omitted areas. | First useful result 1–3 min; final checks usually 5–15 min |
| C: broad/critical integration | Shared lifecycle, frame order, capture/compositor infrastructure, protocol/serialization, privacy/reveal rules, native actor/network authority, startup/preloader, dependencies/build tooling, broad refactor, overlapping worker behavior changes, or uncertain impact | Complete local gate once on the integrated candidate; applicable tool/asset/platform checks; actual-runtime and visual coverage where affected. | Budget a separate 45–75+ min validation phase, measured per run |
| R: release | Publication of a release rather than an ordinary dev change | Complete local coverage with valid final-input bindings, existing trusted hosted proof and fresh release build/package checks; applicable hardware acceptance recorded separately. | Planned release work |

Class B is permitted only when affected tests are identifiable and there is
auditable passing evidence for the unchanged areas being inherited. Without
that basis, establish a complete checkpoint. A docs-only policy change can be
class A locally even when hosted CI deliberately runs its complete checks.

Paths are hints, not a selector: a small change in a general capture allocator
can affect every window. A local window rule can remain B if it changes no
shared coordinator behavior. A build-number/comment-only `NetProtocol.cs` edit
does not itself change the protocol, but inspect inlined build-number consumers.
Treat defaults, product localization, packaging templates and test fixtures as
their actual affected subsystem, not automatically as documentation.
Textual conflicts limited to build numbers, comments or documentation do not
themselves establish overlapping behavior changes; inspect the resolved inputs.

For production C# changes, build Release with zero warnings/errors. Also build
Debug when preprocessor/configuration behavior differs or a Debug-only defect
is involved; the maintainer uses Debug. Preserve separate real-runtime and
metadata-reference compilation checks when either reference contract changes.
Do not run both configurations repeatedly when nothing relevant changed.

For B, review compiled scope when reusing a prior compiled-scope claim, changing
patch registrations or when the source/repair scope is uncertain.
Use private compiled snapshots and the comparison procedure in
`scripts/refactor-guard.sh`; never replace a shared baseline. The guard currently
has no focused/compiled-only mode. Calling `check` invokes the complete gate.
If scope cannot otherwise be established, use C rather than claim an unperformed
compiled comparison.

## Choosing and running checks

At task start, state the intended behavior, affected production inputs, selected
suite IDs and reuse basis. Keep this to a few lines in the working notes; no new
planning document is needed for each small fix. Include a dependent consumer
when data crosses a subsystem boundary. Workers run their owned focused checks;
the integrator tests the union after integration, without re-running unchanged
worker evidence when final inputs can be verified.

Existing commands, run from the appropriate checkout:

```sh
# Discovery only; does not execute tests.
python3 scripts/run-test-suites.py --group local --list

# Example B selection for an options-close defect; extend for actual dependencies.
bash scripts/ci-build.sh Release
python3 scripts/run-test-suites.py --group source
python3 scripts/run-test-suites.py --group local --suite vr-options --suite vr-options-close
python3 scripts/check-docs-i18n.py

# C: the guard includes source suites, all local suites, golden vectors,
# bundle validation, public surface comparison and compiled comparison.
bash scripts/refactor-guard.sh check --summary
bash scripts/ci-build.sh Release
python3 scripts/check-docs-i18n.py
```

Inspect the guard's printed verdict: exit 1 can indicate an expected compiled
difference, while an earlier failed check is a failure. Explain every changed
type; do not convert any exit 1 indiscriminately into success.

`wire-tests.sh` already runs every local suite before the golden executable.
Do not run it again after a guard pass, or run `--group local` before the guard,
for the same inputs. `--suite` is always partial and does not run golden vectors.
There is currently no golden-only flag. A wire/serialization change requires C;
do not substitute a writer/reader round trip for expected-byte goldens.

Examples of affected-suite starting points, always extended by dependency review:

| Changed behavior | Starting selection |
|---|---|
| Options toggle/closure | `vr-options`, `vr-options-close`; `options-coverage` is in source checks |
| Quest readiness handoff | `map-quest-ready-runtime`, `map-ready-handoff`, `map-flow`; add relevant native/flat-host consumers |
| Opening placement or room making | `window-reflow`, `shared-window-reflow`, `shared-window-geometry-runtime`, `shared-window-pose-runtime` |
| Character window rendering or preview rotation | Affected `menu-depth-*` suites, including real coordinator/final text and figure pixels; shared compositor changes are C |
| Held figure permissions | `figure-hold`, `near-figure-grab-runtime`, plus native movement/remote consumers |
| Movie presentation | `native-video`, `video-playback`, `stereo-video-render`, plus native playback and shared transport consumers |
| Fixture-only repair | Its complete containing suite and controls; production evidence can be inherited only with unchanged production inputs |

Use runner concurrency once per integration, bounded for the shared machine.
Coordinate expensive Unity runs with other active agents. Increasing jobs is not
a substitute for isolation; contention can lengthen runs and distort performance
measurements. Preserve controlled hardware/load conditions for timing assertions.

After **every** test run, stop/reap owned processes and remove regenerable project
caches using the existing `cleanup_completed_run` lifecycle. Prefer the central
runner, which performs completion cleanup for passing, failed and interrupted
runs. Standalone fixture callers must use the same API. Retain logs, result
reports, source/input manifests, captures, hardware inputs, `.keep` pins and
current deliverables. Never retain generated caches to accelerate tests in
conflict with this storage rule or clean another agent's active inputs.

## Reusing evidence and handling failures

A previous green label alone is insufficient. Reuse requires all of the following:

1. Identify the successful run and its Git/source or immutable input manifest.
   Compare against the **final** candidate, including intervening commits and
   merges, not just the most recent patch. Without complete bindings, rerun.
2. Verify the suite's relevant production inputs and transitive dependencies,
   test/fixture sources, native references/assets, configuration and toolchain
   are unchanged. Byte-identical named files do not establish this when a shared
   helper, compile item, callback consumer or native dependency changed.
3. Retain original logs, results, assertion/control counts and input hashes.
   `results.json` binds the inventory and logs; by itself it does **not** bind
   production source, native assets or toolchain. Existing fixture source pins
   or an explicit reviewed manifest must supply those bindings.
4. Rerun every affected suite after a repair, including its negative controls.
   Merge conflicts or changed shared dependencies invalidate affected evidence.
   Repeat the whole gate only when impact is broad/unknown or evidence is missing.
5. Report new focused evidence, inherited evidence, failed attempts and hardware
   limits separately. Aggregate final coverage is not a new successful complete
   invocation. Counts need explanations when tests are removed or consolidated.

Recheck changed runtime/package inputs even if they are called a build stamp or
documentation. Runtime hashes may differ because of timestamps/build identity;
document exactly which normalized comparison and dependencies justify reuse.
Changing an expected screenshot, golden bytes or tolerance requires independent
behavior evidence; it cannot certify itself by matching the new expectation.

An affected failure blocks delivery of that change. Preserve the failing proof,
classify product, fixture or environment cause, repair and rerun the affected
scope. Retry an environmental failure once after establishing the cause;
repeated retries until green do not erase a flaky failure. A pre-existing,
demonstrably unrelated failure may be recorded for a bounded dev repair with an
owner and retained evidence, but never relabeled as a full pass or silently
waived for release. Missing/skipped/cancelled tests provide no passing coverage.

Hosted CI policy remains unchanged: source/policy changes run the hosted checks;
trusted proof reuse follows `scripts/ci-proof-reuse.py`, not a local agent claim.
Its docs allowlist currently excludes `AGENTS.md` and `CLAUDE.md`. A hosted green
run does not execute the real-Unity golden executable or establish headset pixels.
See [CI-CD.md](CI-CD.md) for release proof and publication requirements.

## Keeping coverage useful

Add a regression test when it protects a concrete failure or contract. Extend an
existing suite by default. A new expensive Unity project or suite needs a reason
the existing fixture cannot observe the behavior, plus an estimated runtime cost.
Do not add tests solely for a prose correction, trivial constant replacement or
the presence of the implementation's own text.

Use the cheapest layer that can actually falsify the defect:

| Layer | What it establishes | What it cannot establish |
|---|---|---|
| Source checks | Forbidden authority writes, registration/config/privacy contracts | Executed lifecycle or rendered appearance |
| Pure logic/contract tests | Geometry, permissions, state transitions, exact serialized bytes | Unity integration and final image |
| Real Unity integration | Actual production coordinators, native callback/lifecycle behavior | All headset/graphics backends or paired devices |
| Render/pixel tests | Text/figure detail, composition, masks, effects and motion in the rendered path | Subjective headset readability and unexecuted backends |
| Focused hardware acceptance | The exercised device/build/scene/player combination | Unobserved combinations or automated full coverage |

Keep broad edge-case combinations at the cheap layer and a small representative
set at the integration layer. Retain every integration test that proves behavior
the lower layer cannot see. For the Build729 character-window contract, exercise
the actual coordinator and final head pixels throughout motion, including text
and figure; sampling/target-state assertions cannot replace them. Include character
creation and right-side skin/personal-quest hints where affected. For multiplayer,
cover author/observer and flat-host authority, visibility and shared world geometry
where the change can affect them. Keep consented local-only guide exceptions.

Use a causal negative control for each new failure class/checker: reproduce the
old defect or mutate the relevant production behavior and observe a failure.
Reuse that control where valid instead of multiplying identical mutation projects.
Do not assert only a private helper copied from production or line-count/text
patterns as proof of runtime or visual behavior.

Consolidate redundant suites only with a mapping from old cases/controls to their
replacement and retained unique behavioral coverage. Assertions and line coverage
are diagnostics, not quality targets; do not chase a global percentage or delete
an expensive visual test merely because a cheap state test passes.

## Complete checkpoints and rollout

Apply classes A/B/C immediately using the existing runner. The integrator owns a
complete checkpoint after any C integration and, during active B-only runtime
development, by the end of the development day or after ten runtime-changing dev
commits since the last complete checkpoint, whichever comes first. Documentation
alone does not trigger a runtime checkpoint. Batch independent changes so workers
do not each execute the same complete inventory. Record the checkpoint SHA and
evidence; a bounded subsequent repair follows the reuse rules above. Releases
still require complete applicable coverage.

This cadence is an agent workflow obligation, not a newly installed scheduled
job. If the checkpoint cannot finish, hand off its exact pending scope and do not
claim completion. Stop and diagnose a known integration failure before layering
further dependent changes onto it.

For the next ten ordinary runtime changes, record classification, first-feedback
time, final validation time, actual suite IDs, reuse basis, and any regression
found by the next complete checkpoint. The working targets are median B validation
under ten minutes and 90th percentile under fifteen. If a required test costs
more, keep it and investigate its setup/duplication cost; the target is not a cap.
Review missed selection cases and widen dependencies before considering further
skips. Compare equivalent machine/load conditions; this policy is not a measured
speedup until those observations exist.

Infrastructure improvements are follow-up work, not features implemented here:

1. Add reviewed subsystem/dependency profiles with a conservative C fallback for
   unknown paths; compare proposed selections against complete checkpoints before
   allowing automated selection. Do not add an ML selector for this repository.
2. Use recorded durations/resource needs to order slow suites first and consolidate
   repeated fixture setup while retaining case/control isolation and cleanup.
3. Consider a separately verified golden-only entry point and compiled-only guard
   mode, preserving the existing complete commands and their failure behavior.
4. Automate immutable per-suite input fingerprints before automatic local reuse.
   Include toolchain, fixtures, native inputs and transitive dependencies; changes
   to shared harness/build inputs invalidate all dependent proofs. Keep evidence,
   not unbounded generated project caches.

Every handoff/commit should contain this compact validation record:

```text
Test class: B — options-close lifecycle, bounded to the existing options owner.
Final candidate: <SHA and dirty-diff/input hash if applicable>.
Executed: <suite IDs; counts and controls; strict build; source checks; result paths>.
Inherited: <run/SHA/input manifest; unchanged scope and comparison basis>.
Failed/pending: <retained failures, missing coverage, hardware/backend limits>.
Elapsed validation: <measured time; separate from implementation>.
Cleanup: <completion receipt; preserved evidence location>.
```

Keep ordinary records in commit/round notes and ignored evidence. Never describe
a partial selection as a complete gate or a source-proven fix as headset-verified.
