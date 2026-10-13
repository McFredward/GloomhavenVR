# Quest Builder: closed-bank repair and measured parallel import

The maintainer's Windows report `quest-build-support-20261013T165327Z-371e4049.zip`
identifies release `45416ec2f`, ModBuild672, Python3.14.8, sixteen logical CPUs and
approximately64GiB RAM. The failed build stage lasted24697seconds. No new headset
picture or complete Windows export is established by this repair.

## Failure and retained work

Android Addressables finished successfully in `1:09:50.641`. Its downstream
isolated content packer then failed with `No module named 'storage'`. Unity calls
that helper with `-I`; sibling imports are unavailable in that mode. The packer
now contains its own small standard-library Windows FileID/ChangeTime observer
and has no dependency on the Builder's `storage` module. Preserve the selected
venv executable instead of resolving its symlink to a different Python base.

Commit a native bank receipt immediately after successful Addressables cooking,
before packing. The exact compiler identity binds completed preparation,
original controls, runtime/native source inventory and the pinned toolchain.
Qualified unchanged native file stamps retain actual byte proofs; unknown stamps
fall back to real reads. Check catalog/bundle closure before accepting a bank.
This is independent of the final APK and does not certify headset behavior.

Pre-receipt builds can adopt an existing bank only with their exact retained
input manifest, closed preparation controls, real compiler provenance and the
specific captured success-followed-by-packer-import-failure log. Actual staged
Editor/driver records must match that invocation's immutable source inventory.
The report omitted those provenance arrays; the Windows workspace decides
eligibility from its local records. First legacy adoption must qualify native
bytes once. It then publishes native metadata witnesses; modern matching
receipts bypass the large legacy preparation-journal read completely.

The actual3337-file previous release inventory retains its complete preparation
scope and raw/derived recovery recipes under the reviewed repair. Existing
twenty-seven preparation transactions, sixteen raw export packages, original
imports and compatible native programs stay eligible. Refresh only the late
Addressables Editor script under its existing GUID. Unknown helper/producer
changes lose the finite compatibility proof. An incomplete preparation prefix
cannot use an overlay intended for the complete closed ownership schedule.
Do not delete the maintainer's workspace or start a new run for this repair.

## Where the time went

The report supplies these observed command durations, which overlap neither
their nested work nor each other only when explicitly shown by the logs:

| Observed work | Duration | Change |
| --- | ---: | --- |
| Original recovery command |3332.9s|Existing memory-bounded raw exporter pool retained; new file staging batches below. |
| Initial Unity import/SDK command |5603.0s|Actual separate import workers enabled. |
| TextureImporter samples within that import |3516s /14986samples|About63% of the import; a real parallel-import target. |
| Authored mod bank command |405.3s|Same assets and compiler contract; original cache keys retained. |
| Final Unity command |4791.6s|Includes4190.6s successful Addressables cooking; close its bank before downstream packing. |

The old `-job-worker-count` value6/7 controlled general JobQueue threads. It did
not start separate asset import processes. Recovery already used isolated raw
export workers and media conversion already had an independent codec pool; the
pipeline was not entirely serial. A single Unity Library cannot safely be
modified by several arbitrary Editor builds at once. Original reference/type
conversion, import, cooking and final APK export still have real dependencies.

## Parallel work and memory admission

- Fresh ordinary file copies execute in32-file batches on bounded I/O threads,
  up to sixteen. Each worker streams data and SHA rather than retaining a whole
  asset in memory. The parent alone qualifies existing outputs, owns SQLite and
  commits deterministic journal/progress records. Partial published files remain
  eligible for precise continuation. Worker publication uses native Windows
  FileID/ChangeTime; unsupported stamps require actual output qualification.
- Real Unity2021.3 import processes use `OutOfProcessPerQueue` and
  `desiredWorkerCount` before the initial refresh. Reserve2GiB per import process
  and6GiB for the future parent, plus existing system headroom. Re-observe physical
  RAM, Windows commit, cgroups and user job limits. Lightweight JobQueue threads
  receive an independent limit instead of the former2GiB-per-thread coupling.
- A Linux worker needs an actual X display even when the parent is headless.
  Use the available desktop/Xvfb launcher; otherwise retain in-process import.
  Only witnessed initial parallel-import allocation failures receive one serial
  retry, with Library and source files retained. Read current planned worker
  logs too; stale diagnostics and ordinary compile errors cannot trigger it.
- Current worker logs share a finite unique-path asset census with their owner.
  The UI counts each completed asset once across workers and keeps the correct
  import operation. Resource choices and both memory attempts remain logged.

File I/O does not benefit from GPU scheduling. No graphics quality, texture
format, original Shader variant or native gameplay content was reduced. Further
speed work should use the next full Windows metrics to identify the remaining
dominant phases, rather than adding redundant whole-file verification or
concurrent writers to Unity's shared Library.

Unity references: [parallel import](https://docs.unity3d.com/2021.3/Documentation/Manual/ParallelImport.html)
and [DesiredWorkerCount](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AssetDatabase.DesiredWorkerCount.html).

## Verification and practical limits

The integrated focused groups run62native/packer tests,339source/recovery/update
tests (one optional actual Editor test skipped, with its passing native fixture
inherited),120progress tests,19coverage tests and7real-child memory-retry tests.
They include actual `-I` helper execution and production C# orchestration with
small compiler spies: force packing failure after bank creation, resume, and
prove that native cooking was not called again. Unknown source drift, changed
native bytes, incomplete receipts and stale worker logs are negative controls.

A separate pinned2021.3.5f1 Editor fixture requests three import workers and
observes two active workers for its tiny texture queue. Serial/parallel import
produces matching dimensions, format, mips and `imageContentsHash`. The tiny
parallel fixture is slower because of process startup:18.7s versus4.38s. A warm
Linux128MiB file-copy benchmark also shows no speed gain:0.597s parallel versus
0.584s serial. Neither proves a Windows speedup. The capture's hour-scale import
is the reason to enable measured parallelism, not a claimed multiplicative gain.

Release validation checks the actual extracted Linux launcher, local venv,
authenticated discovery, global support export and complete source inventory.
It creates no new game project or APK locally. Unchanged B672 mod and hardware
evidence is inherited, not a new complete wire/runtime gate. Exact source hashes,
logs, native fixture results and cleanup receipts are retained outside the public
handoff folder under `quest3-local/build/evidence/B672-build-failure-speed-20261013`.
Own children are reaped and regenerable fixtures cleaned through the central API.
Remove only clean integrated worker worktrees after the new archive is checked.
