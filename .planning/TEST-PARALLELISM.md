# Parallel test execution — 2026-09-21

## Scope

The maintainer requested faster local and hosted validation after the merchant/menu fix
was completed. Build 541 was first validated by the original sequential gate and pushed
at `0e84862c`. This follow-up changes test execution and CI orchestration only; gameplay,
assets, wire format and ModBuild remain unchanged. No release is created.

## Execution and coverage

A shared manifest preserves the existing groups: 14 read-only source gates, 46 local
standalone runtime suites, and 48 hosted runtime suites. The small inventory differences
pre-date this change and are pinned in scheduler tests. Each suite runs its existing
production checks and negative controls unchanged, sequentially within that suite.
Independent suites run concurrently, with default worker count limited by available CPU
and memory and capped at eight. Managed children receive two logical processors. Compiler reuse remains private to each
suite; MSBuild node reuse is disabled. Longer suites are scheduled first; complete logs replay
in original manifest order after execution.

Every suite has a separate log, temporary directory and recorded exit status. Locks prevent
overlapping invocations from writing the same checkout/project outputs. Complete local
wire runs additionally serialize their build/executable; worker/compiler children cannot
retain that lock. The wire entry point accepts only concurrency and output-directory options,
so list/shard/group flags cannot silently turn a full gate into a partial one. Report directory
reservation is atomic. Cancellation terminates process groups and records incomplete suites;
failed, absent or cancelled work cannot produce a successful result.

The local guard runs the source group before the complete wire gate, then retains the bundle,
public-surface and compiled-snapshot barriers. Strict build and bilingual documentation gates
remain required separately. No shared baseline is rewritten.

Hosted CI runs the build/source checks alongside four runtime shards, each with two workers.
The existing exact-tree `Full checks [TREE]` marker is emitted only after every required job
succeeds; the proof verifier independently verifies each shard's identity, attempt and result.
The stable branch-protection status `Build and gates` and main-only release flow remain.
Historical serial evidence is judged using the workflow at its tested commit. Routine reports
consume no Actions artifact storage, and runtime shards only restore the NuGet cache.

## Measurement

The original sequential local gate took **884.21 seconds** on this checkout (16 available CPUs,
approximately 32 GiB RAM), passing all functional checks and 255,887 golden wire assertions.
Its expected historical compiled difference produced exit 1. Timing and full log:
`.planning/debug/town-build-541/sequential-run.json` and `guard.log`.

The completed build-541 hosted run `35590853967` took **763 seconds (12m43s)** from
creation to completion. Its Full checks job took 717s, including 594s in the serial
presentation harness step. This has the same 48-suite inventory used below; separate hosted
runs still have different machines/cache conditions, so it is an observed comparison, not
a universal timing guarantee. The earlier build-540 CI run took 16m18s, illustrating that
variation. Structured baseline evidence: `.planning/debug/parallel-tests/ci-sequential-baseline.json`.

Integrated parallel timing and hosted results are recorded after validation below.

## Rejected initial trial

The first integrated parallel trial stopped after 334.23 seconds once a real fixture
compilation failure appeared. Placing a temporary project beneath the checkout caused it
to inherit repository `Directory.Build.props`; the partial MR animation fixture then
failed on XML documentation references that are outside that fixture. The original scripts
created those projects in the system temporary directory. This was an execution-isolation
regression, not a product-code failure, and no warning gate was weakened to bypass it.
The runner reported failure, then recorded cancellation of the remaining work when stopped;
the full guard did not reach golden-vector execution or compiled snapshot.

The same initial layout also prevented efficient private compiler IPC on Unix due to long
socket paths. Short, external temporary roots and suite-specific compiler identities correct
both concerns. The rejected run is diagnostic evidence, not a successful speed benchmark:
`.planning/debug/parallel-tests/guard-trial-cold.log`.
