# Installed Quest APK as an update source

User ruling, 2026-10-21: the existing Quest installation is sufficient for an
update; a separately retained PC installation is no longer mandatory for this
path. This is an update-input rule, not a new proof of store ownership.

## Scope and API

`tools/quest-installer/installed_source.py` exports:

- `inspect_installed(adb, address, hardware=None)`: read stable Quest identity,
  exact user-0 Package Manager path, version and regular-file stat. No APK payload,
  external bank, save or private-storage read. Missing installation returns
  `available: false` and an action; ambiguous/split/unsafe inputs are rejected.
- `acquire_installed(adb, address, state_root, *, hardware=None, progress=None,
  cancel=None)`: acquire the exact base APK, then use the production
  `apk_updates.inspect_base` validator. Return its real input/build/profile facts,
  compact seed availability, local APK path and an import receipt.

Both operations use the supplied installer's `Adb` instance. The caller selects
USB/Wi-Fi with the existing `choose_device`, owns the ADB server lifetime and
provides its normal cancellable runner. There is no second server, socket
protocol, debugger, app start, force-stop, permission change or uninstall here.

The acquire result includes `source`, `apk`, `path`, `remotePath`, `deviceSerial`,
`versionCode`, `versionName`, `size`, `witness`, `witnessKey`, `identity`, `base`,
`seed`, `receipt`, `reused` and `resumedBytes`. `base` is the full bounded APK
inspection object for the update driver; the wizard must omit it from its small
persisted/display state. Discovery cannot truthfully report seed availability
without reading the APK; it reports only the version-stamped input-key prefix.

The progress callback receives a dictionary with `phase`, `done`, `total`,
`unit: bytes` and `detail`. Transfer reports stay below 100% until metadata and
closing device/package qualification succeed. A cancel event or predicate is
checked before observation, between bounded transfers and before publication.
The wizard's supplied runner additionally cancels/reaps a current ADB child.

## Transfer and retry behavior

Only Package Manager's exact `/data/app/.../dev.gloomhavenvr.quest-.../base.apk`
is read. Splits, traversal, shell syntax, wrong package roots, links and files
outside the finite 8-GiB APK bound are refused. `exec-out dd` reads fixed ranges
through `Adb.runner`, with at most 8 MiB captured per command and an exact byte
count. Binary transfer does not pass through Windows text conversion. Android's
raw exec service merges stderr and does not return the remote exit status, so
`dd`'s footer is suppressed inside the fixed device-side `sh -c` script. Exact
received length remains mandatory even when the ADB client itself returns zero.
This follows the [AOSP raw shell service](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/daemon/shell_service.cpp)
and [client exec argument handling](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/commandline.cpp).

Inputs live in `<state_root>/build/device-inputs/<witness-key>/`. The witness
includes stable headset serial, exact package generation path, Android version,
file size, inode, modification time and mode. Hardware and the whole package
witness are rechecked after transfer and before publication. A replaced headset,
new installed package generation or changed stat cannot publish a completed
input under the old receipt. Only signed APK metadata and native headers are
inspected; original content banks are not inflated or rehashed.

Each completely written/fsynced chunk updates the small atomic transfer journal.
An ordinary cancellation, connection error, short transfer or timeout can resume
from the previous committed byte offset. A process crash between writing a chunk
and journaling it can invalidate the partial local stat witness; that incomplete
base-APK transfer is restarted, never accepted as complete. No completed build,
game content bank or save result is invalidated by that retry.

A completed import is reused only while the remote package witness and the
ordinary local file's complete stat witness remain unchanged. It still receives
the production bounded metadata validation; it does not undergo a full bank or
APK rehash on each discovery/retry. This stat cache is a retry optimization, not
a tamper-proof local authenticity mechanism. Invalid/changed local caches are
reacquired from the selected headset.

Free disk space is checked before transfer for remaining APK bytes plus a 64-MiB
margin. Temporary compiler/update space is the update driver's separate budget.
Historical witness directories may contain old base APKs; the storage UI should
classify them as removable device inputs. Removing them permits future updates
but requires the next installed-base transfer again. External Campaign banks,
save data and the finished update package are separate and must be retained.

## Compatibility limit

An existing ARM64 IL2CPP APK does not contain the original Mono game assemblies
needed to compile a fresh mod. The sibling update-seed implementation must qualify
`assets/Quest/update-seed.zip` and `assets/Quest/update-seed.json` against that
APK. Older players lacking this seed need a one-time update using the matching
PC game files before Quest-only updates become available. The importer reports
missing seed capability; it does not infer original DLLs from native code or
silently start a full asset rebuild.

## Focused evidence

`tests/quest-installer/test_installed_source.py` passes 26 protocol tests using
the actual `installer.Adb`, APK native-header/contract inspection and real ZIP
fixtures. They cover USB/remembered transport aliases, read-only discovery,
actual 8-MiB binary chunks, exact byte progress, missing/split/unsafe sources,
authorization, space, local-link refusal, cancellation/resume, short/error/timeout
retries, closing package/device changes and unchanged closed-import reuse. A
real host `sh`/`dd` witness also merges raw stdout/stderr and verifies that the
fixed script suppresses `dd`'s footer before it can enter the binary channel.

Eight compiling causal controls remove headset identity, closing package witness,
binary byte-count qualification, native version binding, local cache witness or
cancellation, remove the closing local-file witness, or allow the raw channel's
`dd` footer into APK bytes. Each is
detected by its selected behavior test. Test subprocesses
are reaped and the common `cleanup_completed_run` API is called. Fixtures are
removed by their owning temporary-directory contexts; no Unity project is built.

Evidence, source hashes and individual control failures are retained under
`/home/claw/quest3-local/build/evidence/quest-device-update-20261021/device/`.
This is host/protocol evidence, not real-headset, Android signature, stock shell
`dd` compatibility, live Wi-Fi interruption, Windows or Unity build acceptance.
The integrated wizard must qualify those boundaries separately.
