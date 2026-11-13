# Shared editor fixture completion

Test class: B, bounded to the Results and environment fixture launch/cleanup
lifecycle. Base: `8cf9028c9ef4cf9e1131257d632d85fe7614324d` (`dev`).

The retained Quest771 runs show Unity continuing to write results after the
launcher timed out and project caches were deleted. Unity can escape xvfb-run's
process group, so reaping that launcher does not establish native editor exit.

Both runners now use the existing exact executable/project `cleanup_project_editors`
followed by the existing FD-aware `cleanup_run_compilers` before
`cleanup_completed_run`. A normally exited editor can leave Roslyn borrowing its
private Bee files without any live editor ancestor. A compiler also borrowing a
different private run must remain alive; the PWD-only compiler helper cannot
establish that condition. Uncertain editor/compiler ownership or exit pins the run
with `.keep` and refuses cache deletion, including a later outer-suite cleanup. The
environment runner supplies a small explicit completion log because its native
warning stream exceeded retention's bounded locator size. Results required-child
exit codes and earlier child receipts survive nonzero completion and timeout.
Existing timeouts, oracles, shader ABI and production sources are unchanged.

The existing `scripts/menu-depth-character/test_project_editors.py` now executes
the actual runner functions with real escaped editor/child processes. Coverage
includes successful no-op teardown, native exit 7, timeout, remaining-editor
refusal, causal omitted-editor and omitted-compiler controls, cache/evidence ordering and Results
required-followup failure/timeout receipts. Original exact-owner, foreign-process,
forced-kill, premature-reaping and idempotency controls remain.

Final cheap process proof: **139 assertions pass in 17.27 seconds**, with every
direct child reaped, completion cleanup receipts and no remaining generated caches
or `.keep` pins. The preceding 81-pass editor-only runs are also retained. The
additional controls launch real orphan compilers after normal editor exit, prove
exclusive-FD retirement, preserve a live compiler holding foreign input, and
reject an omitted compiler teardown before cache deletion. Evidence is copied to
`/home/claw/quest3-local/build/evidence/quest-v1.2.4-rebase-20261113/editor-cleanup/`.
`git diff --check` passes. No Unity, C# build or complete runtime suite ran in
the initial implementation worker. Subsequent runtime qualification is recorded
below; this initial record remains process unit proof, without headset acceptance.

The patch belongs on both `dev` and `feature/quest3-standalone`. When resolving the
environment manifest hunk on Quest, retain its existing Quest source/fixture
bindings and add the runner/helper/retention fingerprints. Do not replace the
Quest production inputs with the desktop roster. Cross-platform runtime evidence
reuse still follows the separately recorded input-closure audit.

## Final input-bound runtime qualification, 2026-11-13

Main remains clean at `8cf9028c9ef4cf9e1131257d632d85fe7614324d`. No Main build
output or config was written. All build flags, original oracles, GL backend and
timeout budgets are unchanged.

- Dev environment: all 96 cases, 11,478 production assertions and 95 controls
  pass with native exit zero. The 271.879-second invocation includes compilation;
  its Unity deadline remains 240 seconds. All 22 Main source pins, four runner
  pins and 15 byte-identical Main fixture pins match. Editor/compiler cleanup
  reports no remaining/refused process.
- Dev actual Results head: 55 coordinator/final-head pixel assertions and one
  causal control pass in 46.279 seconds. Strict Release passes with zero warnings
  and errors in 16.17 seconds. The private source/output root holds exactly 1,208
  byte-identical Main C#/project files (40,968,338 bytes), with read-only Asset
  and reference links. Supported `--source-root` relocation preserves the build
  parameters and instrumentation. Only the bound private Git build stamp may
  differ. The run pins 1,416 source/dependency inputs and the actual DLL.

Preserved plugin SHA256:
`6414cb39d1273f21a551ccb9d6ea22354adb7190d7d1b8e71a3b5624b25ae090`.
Plugin/preloader DLLs and build logs were hash-preserved before cleanup. All four
private build `bin`/`obj` caches are absent. The API initially retained copied
movie inputs exceeding its report bound; those exact duplicates were verified
against unchanged read-only Main Assets and removed, then the remaining bin
passed the unchanged cleanup API. No original input, capture or DLL proof was lost.

Preserved unsuccessful attempts are explicitly separate: sparse worker fixture
absence (zero tests); a collector failing to recognize the environment suffix
evidence path despite all 96 native passes; and an initially missing transitive
`catalogue.py` input in the private head layout (zero tests). Materializing the
unchanged fixtures/read-only helper resolved setup. Read-only reconciliation
verified the complete environment manifest, source/fixture pins and cleanup;
the collector defect caused no native rerun or test-boundary change.

Quest manifest coverage qualifies in aggregate: level-up 94 cases (2,218
assertions / 89 controls), desktop 118 (5,327 / 84), flat confirmation 35
(228 / 29). Only missing tails ran after input-bound successful prefixes.
The full ordinary uGUI consumer passes 56 assertions / 10 controls across all
11 cases. Already-qualified common-pointer coverage was not repeated. Case
names, mutation messages and entry methods match the original manifests.
Aggregate coverage is not a new successful complete local invocation.

Exact commands, source/fixture pins, original failures, counts and cleanup proof:
`/home/claw/quest3-local/build/evidence/quest-v1.2.4-rebase-20261113/editor-audit/`
contains `dev-qualification-proof.json`, `quest-continuation-union.json` and
`final-proof-file-sha256.json`. Successful serial wrappers report no remaining
owned processes or wrapper pins and remove private TMP. Native game bootstrap
and headset acceptance remain outside these fixtures.
