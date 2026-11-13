# Automatic installation-data access repair

## Observed failure

`quest-build-support-20261011T112552Z-0cf57185.zip` identifies Builder
`c06f0a070`, Runtime669 source and the same completed Runtime665 APK as the
preceding report. All six setup/build stages remain complete. The fourth
installation attempt lasts619.986 seconds and fails while an external-data tar
has transferred11,425,341,374 bytes. Shell access cannot read
`files/tombstone_00`, `01` and `02`. The old app has not been uninstalled.
Credential-encrypted and device-protected backups already succeed through
Android `run-as`.

The device now reports the original installed APK's actual SHA256 as
`3d32aeb6232fabcb0a0c278f38410426e8e41ee0b8293c6b5f49a98b78e5839a`,
matching the retained B625 hardware APK, with2,871,119,500 bytes. The replacement
Runtime665 APK retains SHA256
`0a585c616387ae5b3eca83a490d3a990c39fc7eccc122d7273a9dceeaa1a094f`.
No screenshot accompanies this support archive.

The device census contains all6,367 files owned by the B625 game-bank receipt
plus14 additional files left by older installations. Rejecting any unknown
descendant classified the entire large bank as mutable and sent it through the
host backup. These extra paths are seven old built-in Shader bundles and seven
old startup bundles. Their names are evidence for this report, not a new
exclusion allowlist. Unknown future files must remain preserved.

## Follow-up capture120937: external namespace access

`quest-build-support-20261011T120937Z-db4a52eb.zip` confirms the delivered
`db6e26f7a`/Runtime670 Builder and the unchanged completed Runtime665 APK.
All six setup/build stages remain closed. Installation attempt5 fails after
43.659 seconds, before binary backup transfers or uninstall. Shell census
identifies119 mutable external entries after separating receipt-owned content;
the three inaccessible regular0600 files have430,807,424,746 and0 bytes.
Private and device-protected access still work. The external `run-as` attempt
now fails while traversing `/sdcard/Android/data/dev.gloomhavenvr.quest` itself.

The old B625 Player's actual `libunity.so` includes `/tombstone_%02d` and its
native tombstone writer. Its59,272,344-byte member has SHA256
`8cdbbb0a444d449692d93cf2b2436c4c692b73587f5fff9a04a64c405a928e11`.
This corroborates the crash-diagnostic naming, but does not justify discarding
unknown nonempty files or claiming their actual contents have been read.

Android `run-as` switches UID/domain without entering the app's mount namespace.
The `/sdcard` alias can traverse a shell-owned parent that the changed UID cannot
search. A direct emulated-storage view must first prove that it refers to the
same primary storage. If an actual app namespace is required, use only that
installed app's owned, identity-checked process view and keep original game
startup suspended. Never follow an unbound PID, silently omit unreadable saves,
change permanent developer settings or rely on opaque old-UID file renames for
new-UID restoration. Archive extraction still belongs to the replacement UID.

The access order is shell, original app UID, proved direct emulated-storage
alias, then the app's own process namespace. The final fallback resolves the
installed launcher, waits for a current debugger-wait marker before Application
creation, stops all of that process's threads and uses the canonical storage
path below its `/proc/PID/root`. Guard the headset, package command line, app
UID and process start time at each command/transfer boundary. No PID is persisted
as a reusable authority. Batch archive-path validation instead of repeating
process probes for every filename. Close the owned process before committing
restoration success; a verified retry stops only the proved replacement package
before normal launch. Preserve prior debugger selection and avoid applying a
second transient debug request while the app's first request is still pending.

The exported support package contains no `active.json`, so the missing optional
recovery summary cannot establish a collector-path defect. A fixture written
through the actual `Recovery.commit()` qualifies the existing selection. New
support metadata records a fixed bounded reason when the explicitly named
current transaction cannot be summarized; source/headset/transaction checks
remain intact. No raw journal, app bytes or unrelated history is exported.

This follow-up's exact source contracts, independent controls and delivery
checks live in `B670-install-recovery-120937-20261011`. Actual Quest namespace
access and successful migration remain hardware outcomes, not host-test claims.

Fresh integrated validation covers262 installer host cases (24 optional
environment cases skipped),54 release/support cases and27 installation/progress
Wizard cases:343 passes. Seven independent real Linux namespace, process-stop,
positive-list tar and old/new UID controls qualify the underlying filesystem
strategy. Android package operations, ADB responses and Meta permissions remain
simulated in the transaction fixtures. Prior146 affected Wizard cases and66
actual Chrome/backend UI cases are inherited only where their source is unchanged.

The follow-up also integrates newly published dev671
`7fe3cabf86cee55d4b8ae96c768de53dc9fc3d89`. Its production difference from670
is the existing handshake's build constant. The composed Quest source compiles
strict Release with0 warnings and0 errors. Installation retry still uses the
finished665 APK; it does not export a new Player or repeat its six closed stages.
The delivered ZIP is checked against18 actual earlier releases with360 changed
input controls, then launched twice through its extracted Linux launcher with a
retained session. These are host delivery checks, not a full Linux game build.

## Follow-up capture124423: save-priority recovery

`quest-build-support-20261011T124423Z-31171fcc.zip` identifies the delivered
`1217f6d76`/Runtime671 Builder and the unchanged completed Runtime665 APK. All
six earlier stages remain complete. Installation attempt6 lasts41.989 seconds
and fails before binary backup transfer or uninstall. Shell census again finds
three inaccessible regular files outside the native save folders. The process
fallback starts the installed launcher for debugger waiting, but its first
`pidof` observation has exit1 with empty stdout/stderr: the process is not yet
present. The production ADB wrapper wrongly treats that ordinary pending state
as a fatal error, bypassing the intended bounded poll. Only this explicitly
approved blank exit1 observation now means no PID yet; nonempty device errors,
other statuses and unapproved commands still fail with command/exit context.

The maintainer's2026-10-11 clarification says that current and stored savegames
are the required data. This explicitly narrows the historical all-file promise
below. The Wizard requests the installation-only `saves` policy. It preserves
every readable file and may omit inaccessible non-save members only after a
complete typed census. All six native `GloomSaves` release-channel trees,
`QuestSaveBackups`, pending save-transfer journals and save-import staging stay
protected in full, including global state, checkpoints, older/corrupted saves
and every filename inside those trees. A crash-like filename is never grounds
to omit a member inside a save root. An unreadable parent or incomplete traversal
cannot prove that the required saves were captured and cannot authorize removal.

The standalone installer's compatibility default remains `all`;
`--recovery-data-policy saves` selects the same explicit narrower promise.
Policy transitions before replacement retain the transaction, original APK,
healthy completed root archives and bank witnesses. Only changed positive member
lists are recaptured. Once replacement begins, the durable original policy owns
restoration, verification and rollback. Current omissions are counted separately
and preserved in the recovery journal; they never enter the required archive
inventory or become an ignored tar failure. The six completed build stages and
the665 APK remain untouched during installation-only retry.

An actual unreadable protected save pauses with specific bilingual instructions
and retained-app/APK context. There is no destructive save reset. A stricter
`all` attempt with inaccessible non-save files can offer an explicit choice to
keep saves and continue, retry all files, or finish without installation. Approval
is bound to the current session, completed APK/input/build key, installation
attempt and one durable nonce. Stale, foreign and busy actions are refused;
ordinary retry preserves the decision and legitimate choice changes clear it.

The report's `signingRecoveryUnavailable: path-binding-mismatch` also proves a
Windows collector regression. `normcase` converts slash separators, but the
collector appends its directory-boundary slash after normalizing the prefix.
Normalize the full boundary instead. A production `Recovery.commit()` fixture
now exercises actual `ntpath.normcase`; a neighboring foreign prefix remains
rejected. Support reports include only policy, omitted-member count/bytes and
the existing bounded recovery frontier, never raw journals, filenames or saves.

Exact source contracts, affected checks, independent filesystem/process controls
and delivery qualification live in
`B671-install-recovery-124423-20261011`. Actual Quest migration remains a hardware
outcome; this repair does not import Unity, compile a Shader matrix or export a
new APK. The source delivery includes published dev671 and its1.1.1 project
version after the1.1.0 release; the finished665 installation still stays retained.

Fresh affected validation covers276 installer host passes (24 optional Windows
cases skipped),109 Wizard cases,57 release/support cases and11 installation UI
cases, including real Chrome and HTTP/Engine/Supervisor fixtures. All453 pass.
Eleven independent real Linux filesystem/permission/new-UID and ADB-wrapper
subprocess controls also pass against the integrated installer. Their Android
package responses remain simulated. Strict composed Release compiles with
0 warnings/errors after integrating published dev
`238dcc5896990df71b6fba03a6d19c71ec2735d3`; no whole-game gate is repeated.

## Follow-up capture141941: Windows binary archive transport

`quest-build-support-20261011T141941Z-e3c44392.zip` identifies source
`3c8bd0859`/Runtime671 and the same finished665 APK. All six preceding stages
remain complete; installation attempt7 stops after 43.678 seconds. Save-priority
access now succeeds: three unreadable non-save entries are omitted, with 855,553
bytes. The old app has not been removed. The next refusal compares a fresh
private archive against its complete positive census.

The private root has five required members, but its retained archive inventory
has only two. The recorded archives have 6,679 and3,076 bytes: respectively
23 and4 bytes beyond a 512-byte tar boundary. This is evidence of damaged
framing, not permission to trust the shorter historical inventory.

Pinned Android12 ADB source separates these transport routes. `shell -T`
disables the remote PTY but does not enable the Windows CRT binary stream mode.
`exec-out` sets binary stdout; `exec-in` sets binary stdin. The independent
control builds actual Android12 Toybox and a native 64-bit Windows CRT probe:
a 9,216-byte tar with 19 LF bytes becomes 9,235 bytes in text stdout mode, and
Python's tar reader silently returns only seven of eleven required members.
Text stdin also truncates that archive to 4,656 bytes at Ctrl-Z. Binary streams
retain every byte. The official pinned 32-bit Windows ADB executable cannot run
in this environment's 64-bit-only Wine. Therefore the ADB route is source-proven
and the Windows CRT effect is physically reproduced, while the actual Windows
ADB client and Quest firmware remain separate unverified outcomes.

Capture uses binary `exec-out` with bounded positive-list tar chunks, then
combines verified members into a host PAX archive. Require complete 512-byte
framing, valid headers/trailer and the entire typed census with exact file sizes.
If native tar capture fails, automatically stream the affected root's individual
files into host PAX; compare source hashes before and after transfer, local
streamed hashes and exact byte counts. Do not buffer whole save files. Preserve
all save/history/import trees and terminal links, and reject incomplete access.
Restoration uses binary `exec-in`, the replacement app UID and independent
post-extraction inventory/hash verification. Exec transport has no shell-v2
remote exit status, so a local zero exit is never proof of successful restoration.

Only the affected snapshot is repaired. Keep successful original APK/root/bank
results and unique earlier archives until a replacement has been verified.
If both safe transfer strategies remain unavailable, the Wizard retains the
finished build and pauses installation with the actual bounded repair reason,
an installation retry and an option to finish without installing. No save reset
or repeated Unity work is proposed. The support summary exposes only known
transport/root/reason enums, planned/observed counts, bytes and framing remainder;
it never opens tar bodies or exports member names, app bytes or raw journals.

Exact fresh checks, primary-source hashes, independent Windows CRT/Toybox and
integrated recovery controls are retained in
`B671-install-recovery-141941-20261011`. Installation-only retry keeps the
finished665 Player. The latest published dev change integrated this round only
corrects release-highlight menu paths; game sources remain unchanged from the
preceding strict 0-warning/0-error Release. No new Unity import, Shader matrix,
APK export or complete local game gate is claimed.

Fresh validation passes 284 installer host cases (24 existing optional Windows
checks skipped), 114 Wizard cases, 60 release/support cases and 13 installation
UI cases: 471 passing cases. The primary integrator also reruns all 93 affected
signing-recovery cases against the composed source; these are a subset of the
installer suite, not additional distinct tests. The final root UI run includes
actual Chrome/backend witnesses and labels attempts accurately when one of two
safe transport strategies is retried. Eighteen independent controls cover five
actual Windows CRT/Toybox cases, four actual Linux ADB protocol cases and nine
physical filesystem/production recovery cases. They preserve 184 opaque files,
all six native save channels/history/import roots, unusual positive-list names,
healthy snapshots and exact new-UID restoration. A source-changing audit replay
is retained as superseded; only the stable final source-bound replay qualifies
the delivery. The actual official Windows ADB client and successful Quest
installation still require the maintainer's hardware test.

## Repair boundaries

This is still an installation-only signing migration. Healthy updates signed
with the same local key use the ordinary data-preserving Android update path.
Only an actual incompatible-signature response initiates backup and replacement.
The original APK supports rollback; app-data backups protect saves/settings from
the required full package removal. No hardcoded signing key, source conversion,
Unity import, Shader compilation or fresh APK export is needed.

Determine readable storage identities before transferring payloads. External
storage classification and access identity are independent: an external archive
can use the installed app's `run-as` identity without inheriting internal-cache
exclusions. Enumerate and preserve the actual mutable subset, including unknown
files within otherwise receipt-owned banks. Receipt-owned bank contents remain
on the headset through the existing proved move/copy path. Restore those banks
before restoring their separately backed-up additional files.

Use explicit bounded archive member lists instead of broad recursive exclusion
patterns. Native crash-diagnostic filenames alone never justify dropping an
unreadable file. The explicit save-priority exception above can omit only proved
non-save members; ignored tar errors never qualify a backup. Preserve the
original installation until required data have verified recovery material.
Retain completed root archives and the original APK across an interrupted
`created` transaction. Prune only unreferenced generated partial archives in
that transaction after its identity and protected material are established.

## Diagnostics and validation

The Wizard retains the complete current multi-line installation failure and
its separate backup location, while reporting automatic access/capture work.
Support export includes only the explicitly named current source/headset-bound
transaction's state, archive existence/sizes, root counts and known retained-bank
names. It never exports original APK bytes, tar archives, private member names,
inventory contents or an unfiltered recovery journal.

Root validation passes243 installer tests (24 optional environment cases skipped),
48 source-delivery/support tests,146 affected Wizard tests and all66 UI tests,
including actual Chrome/backend fixtures:503 passing cases. The independent
audit adds five real-filesystem transaction controls and two selected-APK change
controls during atomic/copy parking. Package operations and access identities
are simulated; actual tar, durable JSON, rename/copy and archive operations run
on Linux. Unknown nested files, directories, terminal links and changed saves
survive restoration and interrupted retries. A source replacement before
package removal leaves the original app and its banks intact.

The source delivery integrates published dev670
`829d3468f7d089b3d63340f14c4cc76f3bf2e80b`; the composed Quest source passes strict
Release with0 errors and0 warnings. Published NPC670 focused evidence and the
unchanged game/native contracts remain inherited, rather than a repeated full
gate. An installation-only retry preserves the completed Runtime665 Player.
No Unity original import, Shader matrix or APK export is performed this round.

Exact delivered-source continuation controls, actual extracted Linux launcher
evidence and cleanup measurements are recorded beside this round's delivery.
The bounded evidence directory retains its initiating Runtime669 report name:
`B669-install-recovery-112552-20261011`. Actual Quest access permissions and
successful migration still require a hardware test.

## Platform contracts

- [Pinned Android12 ADB stream routing](https://android.googlesource.com/platform/packages/modules/adb/+/refs/tags/android-12.0.0_r1/client/commandline.cpp)
  separates RemoteShell from binary `exec-in`/`exec-out` and their Windows
  `_setmode` prologue. Current ADB source is also retained by hash in the audit;
  source routing and physical Windows CRT controls do not claim a Quest daemon
  or actual official Windows ADB end-to-end test.
- [Android app signing](https://developer.android.com/studio/publish/app-signing)
  explains the signing identity required by ordinary APK updates.
- [Android app-specific files](https://developer.android.com/training/data-storage/app-specific)
  documents uninstall removal of app-specific internal/external data.
- [AOSP run-as](https://android.googlesource.com/platform/system/core/+/master/run-as/run-as.cpp)
  establishes the installed debuggable app's actual access identity.
- [Android Toybox tar](https://android.googlesource.com/platform/external/toybox/+/refs/heads/android12-release/toys/posix/tar.c)
  supplies `--no-recursion` and newline-delimited `-T` lists; host shell expansion
  must not turn member names into options or recursive patterns.
- [Pinned Android12 run-as](https://android.googlesource.com/platform/system/core/+/refs/tags/android-12.0.0_r1/run-as/run-as.cpp)
  and [run-as SELinux policy](https://android.googlesource.com/platform/system/sepolicy/+/refs/tags/android-12.0.0_r1/private/runas_app.te)
  establish UID/domain changes and same-app process access without automatic
  adoption of its mount namespace.
- [Pinned Android12 Zygote storage setup](https://android.googlesource.com/platform/frameworks/base/+/refs/tags/android-12.0.0_r1/core/jni/com_android_internal_os_Zygote.cpp)
  establishes package-specific mounts and the shell-owned alias boundary.
- [Pinned Android12 ActivityThread](https://android.googlesource.com/platform/frameworks/base/+/refs/tags/android-12.0.0_r1/core/java/android/app/ActivityThread.java)
  places debugger waiting before Application/provider creation;
  [ActivityManagerService](https://android.googlesource.com/platform/frameworks/base/+/refs/tags/android-12.0.0_r1/services/core/java/com/android/server/am/ActivityManagerService.java)
  restores prior transient debug selection at app binding.
- [Linux process-root semantics](https://www.man7.org/linux/man-pages/man5/proc_pid_root.5.html)
  describe access through the target process's filesystem view. This source and
  AOSP permissions qualify the implementation; they do not establish that Meta's
  current firmware permits the fallback on this headset.
