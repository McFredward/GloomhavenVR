# Quest startup storage reconciliation and retained APK updates

Capture: `quest-capture-20261016T074040Z-533e5a28.zip`.
Branch: `feature/quest3-standalone`; runtime unchanged at B672 / 1.1.1.

## Observed boundary

Both the installed package and startup banner identify B672, input
`d4e76da00ec2`, APK SHA256
`191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
The local completed-install receipt matches that APK. Startup begins07:37:25UTC
and fails07:37:27UTC at `Directory.CreateDirectory` for the generated mod bank's
`StreamingAssets` directory, with `UnauthorizedAccessException`. The main thread
continues producing frames; this is an explicit failed startup, not a long
conversion still making progress. The native game and mod banks never start.

The real app successfully writes startup logs/state and Vulkan cache, so the
whole application files root is not inaccessible. Its mod-bank receipt is not
accepted and startup stages the embedded245,954,518-byte mod ZIP before failing.
The capture does not prove the receipt rejection's exact cause or distinguish
bad UID, modes and default ACLs. Denied file existence/read checks are compatible
with that fallback. The existing code does update its failure view; an absent
error view cannot be claimed from this capture alone.

The earlier host-only change removed a mandatory debugger probe. It did not
normalize Android directory metadata. This new capture independently proves a
real app-access failure, rather than only a run-as namespace discrepancy.

## Android-owned repair without an APK change

Android14 can bind an app's external data around MediaProvider FUSE, while the
ADB shell has a different view. FUSE `chmod`/`chown` are intentionally not
implemented; their apparent success does not prove correction of the underlying
app directory. `ContextImpl` itself requests `fixupAppDir` for an existing
external directory it cannot write. A writable files root does not cause that
code to inspect each generated bank's descendants.

Use Android's `IStorageManager.fixupAppDir` through a small authored shell DEX,
resolved by its runtime interface and method name. No guessed Binder ordinal,
root, storage-isolation switch, app injection or debugger is used. The service
resolves the installed package UID and asks vold to normalize actual ownership,
permissions, default ACLs and quota metadata. The source validates app-specific
paths; the helper further accepts only user0 and our two generated banks.

The protocol validates every UTF-8 path and ordinary non-symlink directory before
its first mutation. The host verifies hardware, installed UID and the helper's
8,988-byte DEX transfer, then submits only manifested leaf parent directories.
vold already covers ancestors and their direct children, so explicit repeated
ancestor requests are unnecessary. It also normalizes immediate package/files
root siblings; this is platform app-directory metadata repair, not an exclusively
per-file operation. No unknown save contents are recursively enumerated or read.

Use an exact shell-owned temporary directory under `/data/local/tmp`, a read-only
DEX, bounded stdin and complete response framing. Remove the tiny helper after
use. Retain a bank repair stamp qualified by installed UID, helper hash and
manifest metadata so an unchanged subsequent update avoids repeating the work.
UID/source/helper changes receive a new scope. Actual app access stays explicitly
`runtime-pending` / `appAccessVerified=false`: a returned platform method can log
a vold failure without propagating an exception. The next hardware capture is
still the proof of runtime access; no headset success is claimed here.

Primary references:

- [StorageManagerService path validation and fixup](https://android.googlesource.com/platform/frameworks/base/+/android-14.0.0_r1/services/core/java/com/android/server/StorageManagerService.java#3305)
- [ContextImpl external-directory repair](https://android.googlesource.com/platform/frameworks/base/+/android-14.0.0_r1/core/java/android/app/ContextImpl.java#3582)
- [vold lower-filesystem resolution](https://android.googlesource.com/platform/system/vold/+/android-14.0.0_r1/VolumeManager.cpp#1017)
- [vold app ownership, ACL and immediate-child repair](https://android.googlesource.com/platform/system/vold/+/android-14.0.0_r1/Utils.cpp#269)
- [MediaProvider FUSE metadata operations](https://android.googlesource.com/platform/packages/providers/MediaProvider/+/android-14.0.0_r1/jni/FuseDaemon.cpp)

The tracked Java source and `StorageAccess.build.json` document reproducible JDK8
and D8 compilation. Two clean compilations produced identical DEX bytes. JVM
tests exercise real protocol/reflection and filesystem symlink controls with
explicit Android Binder stand-ins, not a real Android service.

## No-op mod update and installation reuse

An update still qualifies the selected local PC installation and signing source.
When runtime, authored art, profile, build and signer are unchanged, retain the
current workspace-owned APK instead of repacking/resigning it. Keep original
`latest-build.json`, build receipt, adjacent full-build report and game-bank
locator byte-identical. Current host inventory and PC qualification belong in
the separate `unchanged-apk.json` audit, not invented signed runtime metadata.
Borrowed or non-current sources retain the normal owned-output route. Real code,
profile, art or certificate changes do not inherit this shortcut.

Only the exact reviewed new update-driver row receives the existing host-only
producer alias. Unknown driver changes still invalidate relevant producer
scopes. The entire previous delivered inventory is checked against current
preparation, recovery and compact-code identities, including newly added helper
files; the runtime/game/native cooking sources are unchanged.

Installation also reuses an exact previously completed APK on the same headset.
New receipts retain its installed path/inode/size/mtime/ctime witness. An older
receipt qualifies that one installed APK's SHA256 once, without pulling or
installing its bytes. An unchanged later witness avoids that read. Wrong source,
headset, bytes or physical identity cannot inherit the old proof. Existing
content receipts retain game banks without a new full scan or transfer.

## Wireless access after USB work

The previous cable path intentionally avoided restarting adbd during transfer,
but never enabled or remembered wireless access. After the completed installation,
recovery, launch and cleanup, use a separate twelve-second bounded helper. Prefer
an already authorized matching endpoint; otherwise derive the current WLAN IPv4,
enable TCP mode only if needed and verify the actual stable headset identity.
Persist only a proved endpoint. The active installation transport remains USB.
An unsuccessful convenience setup retains installation and any previous valid
hint; it does not turn a finished installation into a failure.

The independent Connect action can remember an already authorized endpoint
without restarting adbd. Later captures reuse it. A headset reboot or network/IP
change can still require setup again; a stored address is not a promise that
Android's listener persists indefinitely. This follows [Meta's ADB workflow](https://developers.meta.com/vr/documentation/native/android/ts-adb/)
and [Android's wireless ADB instructions](https://developer.android.com/tools/adb#wireless).

## Handoff and limits

Replace the Builder while preserving `.ghvrq`. Choose **Update mod**, leave the
existing workspace APK selected, qualify the local PC game and install. Keep the
same profile and debugger option off. No new Unity Player or changed original
data bank is needed for this repair. Older physical APK proofs may require the
one-time device hash described above; only the tiny metadata helper is uploaded.
Normal successful startup removes the previously failed mod ZIP staging file
through existing cleanup. Do not reset app data or uninstall for this fix.

Focused host, retained-ZIP, protocol/JVM, wireless, signing/recovery and browser
evidence is separate from unverified Android service execution and gameplay.
Historical USB fixture expectations were updated to require optional setup only
after the actual cable installation/launch, preserving the transport boundary.
No fresh Unity/APK build, Windows full export or complete runtime/wire gate was
run. Own temporary checkouts and regenerable fixtures are cleaned after review.

Evidence: `/home/claw/quest3-local/build/evidence/B672-startup-wireless-20261016/`.
