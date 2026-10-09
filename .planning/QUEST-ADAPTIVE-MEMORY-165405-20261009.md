# Quest adaptive compiler memory — capture165405

Runtime remains ModBuild657. Work stays on `feature/quest3-standalone`, including
current published dev657 (`a9af3c330`); this is a Builder repair, not a new local
APK or a change to desktop mod behavior.

## Proven trigger

`quest-build-support-20261009T165405Z-b5b3dec1.zip` identifies source `a1fec28b3`.
Its Windows host has16 logical CPUs,63.7GiB physical RAM,36.1GiB available physical
RAM and27.5GiB actual additional commit. All27 preparation owners are recorded
closed, with no pending preparation transaction. The previous40GiB native
admission estimate rejects this run **before launching the compiler**. This
capture therefore does not establish a new Windows compiler OOM. Successful
earlier32GiB Linux APK builds were valid counterevidence to that fixed threshold.

## Actual native action qualification

The historical largest dispatcher action compiles the original recovered
`GH.Runtime7.cpp`, including the large original `Choreographer.ProcessMessage`
body. With the pinned Unity2021.3.5 NDK compiler, unchanged source and `-Os`, the
old full `-g` metadata action used24,172,988KiB RSS plus3,093,152KiB swap and took
145.10s. Appending `-gline-tables-only` now takes26.07s,456,724KiB maximum RSS
(484,200KiB sampled process-group RSS), with no swap. It succeeds under real4GiB
and8GiB native-process virtual-address limits. Four concurrent copies succeed in
32.41s at1.91GiB combined peak RSS. The26.5MB `Il2CppInvokerTable.cpp` action
succeeds under4GiB at1.59GiB peak; `ScenarioRuleLibrary27.cpp` needs356MiB.

The profile retains the existing optimization, bracket-depth flag, ABI and LLD
selection. Generated executable sections match an independent `-g0 -Os` build;
the removed historical full-`-g` object is unavailable for direct comparison.
Line tables remain, and the pinned Unity offline `usymtool` converts a bounded
linked ELF with the original giant method and source names present. This local
symbol fixture is not a whole-game link or a headset execution test.

The pinned Editor's actual `IL2CPPUtils.GetAdditionalArguments` reflection and
IL2CPP options-parser invocation establish the process-local environment seam:
`IL2CPP_ADDITIONAL_ARGS` follows PlayerSettings; duplicate scalar compiler flags
replace the earlier value. The environment therefore repeats the existing depth
flag and adds line tables; template assets and their preparation keys stay intact.
This follows the [Unity2021.3 implementation](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/2021.3/Editor/Mono/BuildPipeline/Il2Cpp/IL2CPPUtils.cs),
[Unity additional-arguments contract](https://docs.unity3d.com/2021.3/Documentation/Manual/handling-IL2CPP-additional-args.html)
and [Clang debug-format controls](https://clang.llvm.org/docs/UsersManual.html).

## Scheduling and recovery

- Budget native workers at2GiB each, plus6GiB future Unity growth and2GiB
  transient reserve, bounded by actual available physical RAM and commit/swap.
  Capture165405 now selects9 workers. Unknown or tighter capacity selects one;
  estimates never refuse to launch the supervised build. Historical32GiB worker
  evidence remains explicitly historical, not the current workload estimate.
- Genuine compiler allocation failures retain Bee objects, halve concurrency
  and retry the same project and flags. A single worker uses a visible,
  cancellable5–30s backoff, an attainable live headroom threshold and a single
  Unity background worker. Ordinary compilation errors remain failures.
- Fatal children cannot run C# disposal. Before retry, restore the exact parked
  Player payload journal and owned partial delivery, keeping the outer content
  transaction, its backups and Unity Library. Never restart preparation here.
- External codec allocation failures retain successful disk-backed items, drain
  siblings once, then retry the failed item alone. An oversized estimate also
  runs alone; parent publication is never replayed after side effects. Actual
  owned float-texture evidence peaks at21.33MiB decoded output; this is not a
  promise about arbitrary unbounded user images.
- Memory observation/retry events carry bounded measured capacity, jobs and
  attempt fields. The Wizard shows automatic recovery, preserves its active
  operation and progress frontier, and suppresses unsupported ETA during waits.
  It does not ask the user to increase RAM or alter system paging settings.
- Preserve bounded `.memory-attempt-N.log` diagnostic archives. Live tailing
  excludes them, including tails registered before a rename, so old completed
  counters cannot replay. Support exports retain the actual allocation diagnostic
  and resource fields with the existing redaction boundaries.

## Resume ownership and delivery

Exact reviewed source-byte aliases cover the changed host policy, codec observer,
native invocation helper and progress transport. The complete Builder AST aliases
only the reviewed late compiler environment/invocation. Original producers,
templates, game/profile inputs and unknown helper edits remain qualified. The
current input still owns managed weaving, native compilation and final delivery.
The fixed original conversion recipe is unchanged.

The final release selector receipt compares actual previous/current delivered
inventories and Builder bytes against all27 closed owners recorded in the capture.
Its local game/profile/journal and XR reference records are explicit fixtures;
the user's Windows workspace is not present here. Changed original inputs,
templates, graphics/artwork, unknown metadata and orchestration must still reject
reuse. Production completion fixtures separately retain real local test archives,
Library and transaction backups without rerunning closed producers.

Replace the source archive files, restart the Wizard, select the existing owned
workspace/session and continue. Retain its signing key and caches. The handoff is
the source-only Windows/Linux Builder; it includes no original game payload or
another locally built APK.

## Validation scope

Integrated focused checks cover native admission/retry12, preparation identity
and completed-owner migration plus Builder scheduling31, host/codec/progress/
release-support80 (plus one new support archive/metric regression), Wizard
memory/stage/support79, actual compact ARM64 symbols2, and browser/UI25. Worker
float-texture10 and additional unaffected observer suites retain their separately
recorded passing evidence. The real codec child also survives an intentionally
failed32MiB address-space limit by retaining and retrying its isolated output.
The archive-tail fixture fails against the former observer and passes the repair.

Final release inventory/resume and actual extracted Linux launcher/API checks are
recorded beside the handoff receipt under the private `B657-memory-165405` evidence
directory. No unrelated complete wire gate, fresh Unity asset import, shader
matrix, whole Windows APK build or new headset success is claimed. Native peak
measurements are actual Linux pinned-compiler actions, not a whole-Builder4GiB
RAM limit. The next Windows run is the end-to-end acceptance check.
