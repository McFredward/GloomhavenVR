# Quest normal storage delivery and optional Android debugging

Report: `quest-build-support-20261014T205156Z-e232010c.zip`.
Branch: `feature/quest3-standalone`; runtime unchanged at B672 / 1.1.1.

## Observed failure and maintainer decision

The report identifies Builder16fdb8365, the existing B672 APK with input
`d4e76da00ec2` and the retained `reset-installed` signing transaction. Its four
mod files have already transferred successfully. The subsequent, additional
app-origin storage diagnostic fails before full game-bank delivery:

```
sh: <stdin>[19]: can't create temporary file /data/local/shozs4gc.tmp: Permission denied
```

The failed operation uploads a shell script using a here-document under run-as.
Android mksh materializes the here-document in its default temporary directory,
`/data/local`, which is not this app's writable directory. This is not evidence
that the preceding ADB transfer failed, nor evidence of an actual runtime
AssetBundle open failure. This report has no successful gameplay observation.

The maintainer explicitly rejected the debugger as a normal installation
requirement on2026-10-14 and requested a separate optional Runner setting.
ADB USB authorization, wireless connection and log collection are independent
of attaching an Android/Java debugger.

## Storage contexts

| Data or operation | Context-owned destination | Contract |
| --- | --- | --- |
| Required game banks, mod banks, persistent saves and bounded runtime logs | Unity `Application.persistentDataPath`, resolved through `Context.getExternalFilesDir()` | Existing game bootstrap already opens these paths from the real app. No broad storage permission is needed for its own directory. Updates preserve data; uninstall removes app-specific data. |
| Private procedural prefix/state requiring ordinary filesystem semantics | `Context.getFilesDir()` | Existing engine bridge uses private persistent files. Do not put unique state in evictable cache. |
| Temporary advanced app-origin diagnostic jobs | Private `cache/.ghvrq-jdwp-<uuid>` under the app's internal data directory | The optional helper owns this exact scratch; quote script bytes through stdin and set child `TMPDIR` to it. Remove it when the diagnostic process ends. |
| Native executables/libraries | Installed `ApplicationInfo.nativeLibraryDir` | Existing bridge uses installed executable locations. A writable app-home executable is not a valid production substitute on target29+. Debug-domain execution is not production proof. |
| Explicit ADB shell transfer/rename helpers | Shell-owned `/data/local/tmp/.ghvr-rename-<transaction>` | This is separate from `/data/local` and from app-context scratch. Existing helpers retain exact cleanup ownership. Never infer app access from shell access. |
| Another app's private/external data, a global temporary directory, arbitrary `/sdcard` locations | No general app access guarantee | Do not repair by adding broad storage permission, global chmod or moving required data into Downloads. |

Android11+ requires the app to obtain its own external app directory through
the platform API. A hardcoded path alone does not establish that a run-as child
has the real zygote app's storage mount and SELinux context. Use the platform
API in runtime; do not advertise a successful shell read as an app read.

Primary references:

- [Android app-specific storage](https://developer.android.com/training/data-storage/app-specific)
- [Unity2021.3 persistentDataPath](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Application-persistentDataPath.html)
- [Android10 writable-home execution restriction](https://developer.android.com/about/versions/10/behavior-changes-10#execute-permission)
- [AOSP mksh default temporary directory](https://android.googlesource.com/platform/external/mksh/+/refs/heads/main/Android.bp)
- [mksh temporary-file requirements](https://android.googlesource.com/platform/external/mksh/+/refs/tags/android-14.0.0_r37/src/mksh.faq)
- [AOSP run-as UID and SELinux transition](https://android.googlesource.com/platform/system/core/+/refs/tags/android-14.0.0_r1/run-as/run-as.cpp)
- [Meta ADB setup](https://developers.meta.com/vr/documentation/native/android/ts-adb/)
- [Meta asset-file testing](https://developers.meta.com/vr/documentation/android-apps/ps-assets/)

## Normal installation and retained work

The installer now leaves genuine app-origin opens to normal Unity startup.
Normal delivery keeps the existing exact manifest, hardware identity, accepted
file journal, missing/corrupt transfer checks, atomic finalization and compact
installation receipt. It does not instantiate an app storage probe, launch a
paused app, attach JDWP, forward a port or enable a debugger wait. A normal
signing-restoration attempt also cannot silently fall back to debugger-based
storage access: retain original saves and present the existing explicit choices
if direct restoration is unavailable.

Delivery records explicitly say `appAccess=runtime-pending` and
`appAccessVerified=false`. This is delivery success, not a new claim that every
file was opened by the app. Existing runtime first reads its compact receipt,
then opens the real environment AssetBundle and later native assets. It already
reports missing installation content. There is no new full-bank startup scan,
second install or fresh APK requirement in this host repair.

The old mandatory post-transfer access repair is removed from the default
path. Advanced CLI `--diagnose-app-storage` and explicit access-only repair
retain strict diagnostics; these can use the guarded paused-process helper.
An interrupted diagnostic's existing exact source/headset/installed-file
ownership receipt may be reconciled and released before installation proceeds;
normal installation never creates or rearms one. Foreign debugger selections
remain protected. Reset-selected transactions must not replay old saves.

The optional helper's here-document is replaced by correctly quoted `printf`
through the existing short-argv ADB stdin transport. Its child `TMPDIR` is the
private UUID scratch. Existing bounds, event guards, exact UID/PID/starttime,
output collection and cleanup remain in force.

## Separate Runner action

The device page now offers **Start game**, independent of a build or install.
Its collapsed **Developer options** contains an unchecked **Wait for Android
debugger on the next start** checkbox. Normal start uses the existing canonical
MAIN/LAUNCHER/VR intent. Only an explicit checked request adds transient
`am start -D`; the Runner does not attach, forward ports or set global debugger
configuration. This is Android/Java startup debugging, not an automatic Unity
IL2CPP/C# or native gameplay debugger.

USB selection remains preferred. The Runner qualifies the owned package and
launcher and distinguishes an acknowledged intent from an observed process.
It does not report a visible headset picture. A running game is retained when
debug mode is requested, with save-and-close guidance rather than a force-stop.
The checkbox is not a sticky installation setting.

## Validation boundary and next hardware test

Focused delivery tests execute real extraction, local shell/file operations,
hashes, corruption/interruption controls, receipts and warm reuse, and prove
that neither fresh delivery nor warm resume constructs the diagnostic view.
Advanced tests retain genuine app-open checks. The diagnostic worker executes
real JVM JDWP tests, including literal Unicode/quote/CRLF script upload over
32KiB, output preservation and private TMPDIR. Runner tests exercise the
authenticated API, actual Chrome and normal/explicit-debug requests.

One initial fixture reuse check exposed nondeterministic ZIP timestamps when
identical content was recreated across a two-second boundary. The fixture now
uses a fixed timestamp; production reuse checks were not weakened. Its failed
receipt is retained alongside the passing rerun.

Use the updated game-free Builder with the existing `.ghvrq` directory and
resume the retained installation. Keep the existing APK and accepted mod files;
deliver only outstanding game files. Do not reset, uninstall or rebuild Unity
for this repair. Keep the debugger option off for the hardware test and collect
a fresh runtime capture if normal startup still fails. Source/JVM/browser
checks do not establish Quest firmware storage access or successful gameplay.

Evidence: `/home/claw/quest3-local/build/evidence/B672-storage-contract-205156-20261014/`.
