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
