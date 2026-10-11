# General signing-conflict recovery at installation

## Evidence and scope

`quest-build-support-20261011T102641Z-c3bf876b.zip` records Builder
`e1d71c3e0`, session `94dd446a91eb4dbea53e48e573f5e1fc`, with all six
setup/build stages complete. The installation-only retry retains the completed
Runtime665 APK, input
`9207a0cf49519b717a85c4d3e6f13f9dade1b1cbb2e3bb460e4484365899dbef`,
SHA-256 `0a585c616387ae5b3eca83a490d3a990c39fc7eccc122d7273a9dceeaa1a094f`,
and its 10,929,781,172-byte original content bank. Android rejects the APK
update with a signing mismatch. This is an installation repair: no original
conversion, Unity import, Shader compilation, APK compilation or key replacement
is required.

The Windows signing log identifies a different public certificate from the
retained sandbox test APK. The supplied report does not contain the installed
headset APK's certificate, so that particular old certificate is not established
by this capture. The previous support collector omitted the named current ADB
log because Windows CRLF did not match its end-of-line expression; that selection
is corrected without adding unrelated device history.

## Recovery contract

The maintainer explicitly requests general automatic recovery on different host
systems, rather than selecting a hardcoded sandbox key. A normal compatible
update retains the existing installation and local signing key. Only Android's
actual package-install signature conflict can begin replacement recovery.
Private keys are neither shipped nor downloaded; an installed APK cannot supply
its original private signing key.

Before removing a package, retain and verify the actual original installed APK,
native saves and indices, settings, unknown mutable app-specific files and
private procedural state. Native save filenames and account IDs stay byte-for-
byte unchanged. Detect actual Android user ownership and private-data access;
prove that the replacement's compiled manifest supports restoration. Preserve
both credential-encrypted and device-protected file roots, including `no_backup`.
Use a persistent, source/headset-bound recovery journal before each mutation.

Large original content banks require their own preservation proof. Android's
FUSE layer can deny cross-boundary rename even when filesystem IDs agree;
`mv` can then copy. A fast retention route must prove the actual move behavior.
A slower fallback must retain the old bank, not assume that the replacement
APK's bank also makes the old APK usable. Insufficient backup capacity or access
must be reported before an unprotected uninstall.

Restore private bytes under the new app UID. Wine-prefix symlinks are retained
as link text, never dereferenced; regular files and directories must not be
written through an archived link. Restoration and byte checks precede launch
and the final installation receipt. Interruptions retain recovery material and
resume installation alone. Android Keystore and provider authentication are
system state outside this file-backup contract; universal authentication-session
preservation is not claimed.

## Presentation and validation

The Wizard keeps the completed APK and its actual665 version visible. Optional
signing backup, replacement, restoration and verification contribute to the
existing APK installation operation. Child transfer completion does not close
the parent; installed-build confirmation, content readiness and launch retain
their own completion boundaries. A safety refusal exposes its actual reason and
retained recovery location with an installation retry, without another build.

Root validation and exact delivered-package evidence are recorded in the round's
compact validation receipt. The unchanged game/runtime, native-bank and final
signing checks inherit their previous evidence. Current published dev669 is
integrated into the feature without rewriting its published history; the merge
preserves both the18 Quest CI additions and the new native-bank lifecycle suite.
The source build passes Release with warnings treated as errors (0/0).

On the integrated tree, installer223 cases pass (24 optional environment checks
skip), including33 new real-filesystem/tar transaction and manifest controls;
release/support43 and installation Wizard22 cases pass. UI64 cases pass and the
two real-API cases run explicitly and pass, for66 successful UI cases. The merged
runner inventory passes16 cases (two optional SDK checks skip). The worker's612
Wizard checks and independent Android-source review are retained as scoped
evidence. No fresh complete game gate or Unity/Shader/APK build is claimed.
The existing2.7-GiB test APK's actual compiled manifest is accepted, while a
copy with the wrong Android debuggable resource ID is rejected; only the small
manifest is read and the original APK is unchanged.

Mock ADB, host tests and browser
checks do not establish successful Quest migration, filesystem permissions or
the headset picture. Those remain a hardware test.

## Primary platform contracts

- [Android app signing](https://developer.android.com/studio/publish/app-signing):
  updates require compatible signing identity.
- [Android app-specific storage](https://developer.android.com/training/data-storage/app-specific):
  uninstall removes internal and app-specific external storage.
- [AOSP run-as](https://android.googlesource.com/platform/system/core/+/master/run-as/run-as.cpp):
  actual debuggable, UID, data-path and SELinux checks govern access.
- [AOSP package removal](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/pm/RemovePackageHelper.java):
  retaining data also retains package signing state; full removal affects CE/DE
  data and Android Keystore state.
- [AOSP FUSE rename](https://android.googlesource.com/platform/packages/providers/MediaProvider/+/refs/heads/main/jni/FuseDaemon.cpp):
  cross-backing-boundary rename may return `EXDEV`.

## Storage

Keep canonical APK/content, matching symbols, active work and supplied captures.
Prune this round's completed private worker checkouts and duplicate delivery ZIP
after integration and validation; record measured reclaimed storage. User device
recovery backups are separate from rebuildable Unity caches and must remain
available for a pending restoration.

Host recovery files live below `<workspace>/device/signing-recovery/<headset>/`
in the Wizard's default configuration. A transaction keeps its original APK,
mutable tar archives and active journal there. A successful installation removes
only its own temporary copied headset banks; host backups remain available.
