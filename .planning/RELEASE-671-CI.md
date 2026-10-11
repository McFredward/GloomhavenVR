# Release 1.1.0 CI audit — 2026-10-11

## Current result and historical failures

The release audit starts from dev
`829d3468f7d089b3d63340f14c4cc76f3bf2e80b` (ModBuild670), tree
`36de52a76b9b3bb4664c90fcab994f6c2a2ac06c`. Its latest full hosted
[CI run38136094637](https://github.com/McFredward/GloomhavenVR/actions/runs/38136094637/attempts/1)
passed source/build checks, all four runtime shards, the full-check completion
marker and the stable `Build and gates` job. Running the production
`ci-proof-reuse.py` verifier against live API metadata accepts this exact tree;
the checkout had no tracked modifications during verification.

The latest failed CI,
[run37954092465](https://github.com/McFredward/GloomhavenVR/actions/runs/37954092465),
is historical: runtime shard1 failed `player-settings-help` on four overlong
wall-setting help strings (English/German lengths316/379 and369/395).
Source/build checks and the other23 suites in that shard passed. The completion
aggregator correctly blocked the run; it did not issue false release evidence.
Commit `68fc96d97` already shortened those strings, and subsequent complete dev
runs passed. Earlier home-only SDK lookup failures were already repaired;
[CI-SDK-RESOLUTION-REVIEW.md](CI-SDK-RESOLUTION-REVIEW.md) records their cause and
the executable-selection controls.

No remaining workflow defect or release blocker was demonstrated. This audit
does not change a healthy workflow, weaken a gate or skip a failing suite. The
latest previous published release,
[v1.0.8 run36052544814](https://github.com/McFredward/GloomhavenVR/actions/runs/36052544814),
also completed successfully. The new Build671 source tree still requires its
own successful full hosted CI before publication; Build670 evidence cannot
authorize changed release sources.

## Focused validation

- Trusted CI proof:39 tests pass, including failed/pending/superseded evidence,
  exact-tree identity, merge parents, docs-only inheritance and recovery.
- Draft release upload:21 tests pass, including lost responses, partial uploads,
  immutable source/tag checks and refusal to modify an existing published release.
- Workflow topology:5 tests pass; all four required shards, completion gates,
  optional storage boundaries and release proof remain enforced.
- Release provenance/bookkeeping simulation:36 assertions pass, including normal
  PR merges, concurrent dev changes, retry, unchanged release content and preservation
  of an independently advanced development version.
- Hosted SDK selection:9 tests pass; explicit override, PATH installation, empty
  override and local fallback remain covered.
- One full focused `player-settings-help` suite passes:2305 production lookup
  assertions, all three runtime controls and all five content controls;670 bound
  keys,548 exact/family entries,143 heading/action entries and138 original hints.
  This is explicitly a partial1/1 runner result, not a new complete local gate.

Evidence is retained in the CI worker's gitignored `.planning/debug/` under
`release671-*` and `player-settings-help/run-wt3tdlq0`. The initially omitted
runner `--group` argument is retained as an argument-validation failure; it did
not start a suite. The corrected invocation uses the central runner's normal
completion cleanup. Its original generated project sources and receipts remain;
their regenerable bin/obj caches are gone. Unit fixtures and Git simulation
scratch directories are removed by their normal temporary-directory cleanup.
Post-reap calls to `cleanup_completed_run` retain bounded cleanup receipts and
find no additional owned project caches; no shared game references are swept.

## Publication rules inspected without changes

The active `main` repository ruleset requires a pull request with zero required
approving reviews. Merge commits are allowed, there is no required-status-check
rule, and there are no bypass actors. The active all-branch ruleset forbids
deletion and non-fast-forward updates. Normal dev-to-main PR merging is allowed;
there is no need to change rules or use an administrative bypass. The project
still requires successful exact-tree CI before merging/publishing even though
the repository ruleset does not independently require that status.

The release workflow builds the main commit with release stamping, verifies its
archive/bundles and creates an immutable tag. It publishes only after a draft's
uploaded archive is verified, then records release ancestry and the next version
on dev through normal fast-forward retry. Actual v1.1.0 publication and its new
CI run are integration evidence to be recorded by the primary agent separately.
