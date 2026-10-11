# Quest binary content delivery — capture163658

## Evidence

`quest-build-support-20261011T163658Z-f1588b1f.zip` identifies Builder source
`c3f4115ae`, Runtime671, the completed B665 APK and six closed build stages.
The previous census repair succeeds on the headset. The three save/settings
archives total 573,440 bytes; restoration and verification finish. Android
reports the installed replacement B665, with the app stopped before launch.

The next operation fails while uploading a 112-byte installation receipt:
`remote fchown failed: Operation not permitted`. The affected destination is
inside `quest-mod-resources`, not a save archive. The client also prints
`1 file pushed`; that statement is insufficient evidence of accepted bytes.
Android12 AOSP `file_sync_service.cpp` calls `fchown` after creating/opening the
destination and before consuming DATA frames. A partially created empty file
can therefore accompany that client summary.

The user previously accepted normal content reinstallation after Android refused
the rename-only bank retention method. The required roughly 10.93 GB content
delivery remains a normal installation operation, not an app-data backup. This
repair does not change that choice or introduce another uninstall, APK backup,
asset-bank backup, Unity import or game build.

## Repair

Keep the ordinary sync push fast path. Only the observed fchown/EPERM error
chooses a binary `adb exec-in sh -c` upload; subsequent files on that connection
reuse the selected transport. Other offline/authorization/permission failures
retain their original failure context. No Android UID or signing key is fixed
in source.

Feed a binary file handle directly to the subprocess instead of buffering an
asset in Python or translating Windows line endings. Use a short, unique staging
name below the generated bank's StreamingAssets directory. AOSP escapes every
argument after the first executable, so the script operand must be supplied
exactly once, without prequoting it. Android's intentional `/sdcard` alias stays
accepted; linked banks, descendants and destination leaves remain rejected.

`exec-in` does not propagate the remote command's exit status. Require a unique
post-EOF completion marker, exact remote size and SHA-256 before an atomic rename
publishes the destination. Missing markers, wrong bytes, timeout and interrupted
connections never create a completion claim. Cleanup removes only the current
operation's generated staging. Successfully installed files and restored saves
remain retained. Normal sync receipt writes also require exact size/SHA proof.
Raw asset verification replaces the ordinary per-batch remote hash pass, avoiding
a second full asset hash. Warm completed-bank reuse still performs no tree scan
or asset hash.

The content progress denominator now spans both mod and game banks. Measured
binary-stream byte progress is reported once per second, with verified batch
milestones and a single final completion. Bank-local log counts cannot finish
the entire content stage. Client-read bytes remain below a file's final byte
until its remote verification and publication succeed.

## Validation and handoff

Focused validation passes 210 installer/content/signing tests, 52 Wizard progress/
completed-delivery/failure tests and 61 support/release tests. Twelve independent
process-backed controls bind the final installer source SHA. They include the
original failure, exact binary payload preservation, silent client success,
wrong same-sized bytes, timeout, offline, checksum failure, foreign/linked paths,
the expected storage alias, warm reuse and a 16 MiB transfer with less than 0.5 MiB
peak Python allocations. These are Linux subprocess/file controls using the
production ADB adapter; they do not establish Quest FUSE/UID permissions or a
complete official Windows ADB outcome.

The delivered game-free ZIP is qualified through its actual source inventory,
an extracted Linux launcher, retained session selection and the previous
delivered inventory at the captured completed preparation frontier. Changed
game/profile/producers must still invalidate that frontier. Unchanged runtime
and UI evidence remains inherited, not a new complete game gate. The next
hardware test must establish the Android delivery result.

Publish on `feature/quest3-standalone` with McFredward attribution. Replace only
the latest public Builder ZIP/audit/validation. After reaping test processes,
remove owned proof caches, the finished audit worktree and the private duplicate
ZIP; preserve canonical inputs, current native symbols, captures, branch refs
and other agents' work. Compact evidence lives under
`/home/claw/quest3-local/build/evidence/B671-install-recovery-163658-20261011/`.
