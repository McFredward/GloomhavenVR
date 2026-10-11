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
unreadable file, and ignored tar errors never qualify a backup. Preserve the
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
