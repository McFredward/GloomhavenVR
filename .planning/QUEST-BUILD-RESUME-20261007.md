# Quest Windows build continuation — 2026-10-07

This tooling repair stays on `feature/quest3-standalone`, as explicitly
reconfirmed by the maintainer on 2026-10-07. It changes neither the shared `dev`
runtime nor ModBuild 627. Qualification below distinguishes fixture evidence
from an actual Windows build and headset result.

## Captured failure

`quest-build-support-20261007T200625Z-6442cd0c.zip` identifies Builder source
`440b9905728caa80b2f201fa0ec67138eb755b10`. Its failed build attempt lasted
1,158.2 seconds. All 16 raw packages were retained. The closed 204,339,293-byte
checkpoint exists, with no pending merge journal. The first derived copy failed
at `Assets/AnimationClip/ErrorPulse.anim`, before any of 189,701 planned files
was accepted. This capture does not prove corrupt asset bytes.

Combining the rotated `progress.previous.log` and `progress.log` gives
1,012.469 seconds for the redundant 189,737-file raw resume scan. Considering
only the final log fragment would undercount this phase at 441 seconds.
Removing this replay is a source-path improvement; it does not establish a
whole-build duration or an ETA for previously unobserved Unity work.

The copy compared full path-stat and descriptor-stat tuples. Windows CPython
3.14.8 uses creation time for the former ctime and change time for the latter.
The source and output comparisons therefore rejected legitimate unchanged
files. The source bridge compares identity, size and modification time across
APIs, retaining full before/after guards within each API. The destination
compares file identity and size and publishes its final path stamp after the
writer closes; Windows can legitimately finalize its write timestamp at close,
as documented by [Microsoft](https://learn.microsoft.com/en-us/windows/win32/sysinfo/file-times).
Streamed SHA-256 and complete-write qualification remain required. See
[CPython issue 157671](https://github.com/python/cpython/issues/157671)
and the pinned [posixmodule](https://github.com/python/cpython/blob/v3.14.8/Modules/posixmodule.c)
and [fileutils](https://github.com/python/cpython/blob/v3.14.8/Python/fileutils.c)
sources. Capture timestamps are unavailable, so the precise Windows stat
values are source evidence rather than an executed Windows witness.

## Continuation boundaries

- A closed raw export adopts the actual source, core, package plan, managed
  metadata, CAB ownership and final checkpoint receipts. A pending merge uses
  the existing raw recovery path. Inconsistent closed receipts fail visibly;
  they never trigger a silent re-export. Raw bytes are qualified at their
  actual staging consumers instead of another complete raw preflight sweep.
- Derived staging has fourteen ordered, durable phases. Per-file accepted
  copies survive interruption. The journal qualifies retained latest-writer
  bytes once per cold invocation and retains GUID/layout/native/TMP context,
  preventing a later retry from replacing transformed assets with raw copies.
- A write-ahead journal backs up only existing files actually touched by an
  open staging transformation. Python writes, renames and removals restore
  that phase's preimages after failure or process termination; earlier phases
  remain committed. External writers require an explicit owned seam. The
  native temporary workspace has a separate declared ownership boundary.
  Backup storage is the sum of first-touched existing files in the open phase
  and can reach several GB for large transformations; it is removed on publish
  or rollback. SQLite normal synchronization covers process termination, not
  arbitrary loss of power.
- Preparation checkpoints actual project overlay, startup content, native
  runtime, audio, texture, graphics, mod-bank and compiler/settings functions.
  Later writers supersede earlier output contracts. Bounded undo sets cover
  destructive functions. Unity `Library` and immutable original inputs are
  outside those undo sets. Changed inputs/recipes can legitimately invalidate
  affected results; arbitrary old transformed trees are never assumed valid.
  This repair retains the declared-source metadata guard before each
  checkpoint. Narrowing it to independently qualified consumer scopes remains
  a measured optimization opportunity; it adds no repeated content hashing.

Retained files with corrupt bytes, unknown owners or incompatible recipes
produce a specific error. They are preserved for diagnosis instead of
repeating a completed stage without explanation. This is continuation after
an interrupted owned build, not a promise to reuse incompatible output.

## Progress presentation

The long build is foregrounded in six groups: inputs/copies, original
conversion, project/presentation, mod/code, Unity import and Android export.
Its 23 actual operations show completed, currently running/checking, retained,
reused, failed and pending states. Conversion additionally exposes the actual
package schedule, eight sections and fourteen staging phases.

Compatible retry retains the global high-water percentage while naming an
earlier live check separately. Real substage/file/byte counters contribute to
the weighted total. A replayed earlier counter cannot grant progress to an
unfinished later Player operation. No clock animation invents build work or
an unmeasured whole-build estimate. The failure alert, retry and diagnostic
export remain independent of optional live-log requests.

Audio, cubemap, ordinary/float texture and sprite functions additionally report
their existing object schedules. Their observed item fraction belongs only to
the actually opened matching preparation checkpoint. For example, two finished
texture checkpoints plus 250/1,000 items in the third yield 75% of that operation.
A nested platform-image pass is visible without granting the cubemap operation
additional global credit. Complete items do not publish a function or operation
success before its output contracts qualify.

## Qualification and delivery

The integrated source checkpoint `e55fbe2ff54f869036e51cdb978b0650d6d4ed9f`
passes all nineteen affected tooling suites: 617 passing cases and two existing
optional recovery skips (619 total). All 28 UI cases run, including eleven
actual Chrome workflows. Fixtures exercise real copy/ZIP/metadata consumers,
process termination and phase rollback, preparation continuation, observed
item fractions, isolated loaders and visible failure/retry. External SDK,
codec and Unity actions use declared fixtures; this is not Windows execution.
The final source checkpoint changes only this note and `STATE.md` after that
gate. Publication separately qualifies the exact extracted game-free ZIP,
authentic HTTP/CLI startup, all 21 bundled slideshow images and retained session
state across two Wizard servers; evidence remains outside worker worktrees.

No host APK, original game export, Unity import or exhaustive shader compilation
is part of this tooling repair. A complete Windows APK and headset outcome still
require the maintainer's next build and hardware capture.

Keep the existing Wizard work/state folder when replacing the Builder ZIP.
Continuation selects the same owned session and compatible intermediate
results; clearing that folder would intentionally remove its resume evidence.
