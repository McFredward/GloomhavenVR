# Original procedural runtime bridge

The full Quest target preserves the owner's original `ApparanceEngine.dll`. A small Windows x64 worker loads its twelve C ABI exports. The default backend uses pinned native Bionic ARM64EC Proton Wine and the official FEX CPU translator. The previous pinned Box64/Wine9 backend remains an explicit diagnostic option. Unity, original managed placement decoding, rendering, gameplay and networking remain native Quest code.

The runtime copies each input parameter buffer, resource descriptor, 15-float resource frame and output task buffer across a local socketpair. Guest pointers and Windows callback addresses never cross processes. Original synthesis threads, procedure evaluation, operator implementations, seeds, task completion and engine entity handles remain owned by the original engine. The runtime forwards original task bytes unchanged.

`runtime.stage(output_cache, ndk, android_plugin_directory, streaming_assets_directory, owner_engine_dll, *, backend=None)` preserves the original five-path call boundary and stages interpreted data under `StreamingAssets/ProceduralRuntime`. `backend="proton-arm64ec-fex"` is the default; `backend="box64-wine9"` must be explicit. Direct calls may select `GHVRQ_PROCEDURAL_BACKEND`. An unknown choice or failed Proton qualification stops delivery; there is no automatic fallback. The normal owned-content archive installs the payload with its existing receipt/progress flow. Builder requirements are in `requirements.txt`; the wireless installer needs none of them.

The native contract is:

- `quest_apparance_configure(nativeLibraryDir, payloadDirectory, writableDirectory)` returns **0 for success**, -1 for invalid or active configuration. Call it before the original engine starts. Configuration accepts paths up to 4,095 bytes.
- `writableDirectory` must be under Android `Context.getFilesDir()` (internal app storage). Wine's prefix needs real Linux symlinks and Unix sockets; external `Application.persistentDataPath` is an emulated-storage filesystem and cannot provide that contract. The installed payload and original procedure files may remain in the existing external owned-content directory because Wine reads them through absolute `Z:` paths.
- `quest_apparance_last_error()` returns bounded, deduplicated failure context. Transport errors also enter the original log callback after the current reply is consumed and the ABI mutex is unlocked.
- All twelve original `Apparance*` exports retain their signatures and results. `ApparanceInitialise` preserves original success value 1. `ApparanceUpdate` preserves the by-value three-float view struct. Asset request strings are ARM heap allocations compatible with the existing Android P/Invoke string marshaler. Native task buffers remain leased through their next corresponding pop call.
- The framing protocol is little endian, version 1, with a 20-byte header and a 128 MiB payload limit. Invalid frame sizes, sequence mismatches, malformed resource frames and disconnections stop the worker with useful failure context.
- `ApparanceShutdown` invokes the original shutdown and bounds child termination. Original engine `log.txt` stays in the private Wine prefix's `drive_c`; stderr goes to `writableDirectory/procedural-worker.log`.

For a diagnostic APK, collect internal `quest-procedural-state/procedural-worker.log` and the optional original engine log: `quest-procedural-state/proton-prefix/drive_c/log.txt` for Proton, or `quest-procedural-state/wine-prefix/drive_c/log.txt` for Box64. Use `adb shell run-as PACKAGE` alongside ordinary Unity capture. These logs are not in external `Android/data` storage.

## Default native Bionic Proton / ARM64EC FEX backend

`proton.lock.json` pins GameNative's public September28 Wine11 ARM64EC WCP
(`fffa4672…`,98,079,159 bytes) and official FEX2609.1 Ubuntu PPA package
(`c62210a5…`,1,693,160 bytes). Only the latter's ARM64EC Windows DLL is selected;
its Linux UnixLib companions and WOW64 translator are excluded. The Wine package
and official FEX DLL pass actual native/ARM64EC import checks together. Wine's
ARM64X native and EC export tables differ; the audit applies actual dynamic
fixups and uses the correct caller view, rather than assuming a COFF machine
field alone qualifies a hybrid library. Original worker and owner DLL stay plain
x64 PE. There is no x64 Unix/glibc Wine tree in this backend.

The package keeps the consistent complete 64-bit Wine PE/data tree, the 24 native
Unix modules without external desktop link-time dependencies, native server and two own-source helpers
(27 ARM64 ELF files). Optional X11/audio/media/Steam driver modules and 32-bit Wine
are excluded and their inventory is explicit. The minimum import proof also
includes native Wineboot/services/winedevice initialization, not just the owner's
direct DLL list. Runtime `dlopen` features beyond this closure remain unproven.

`proton_launcher.c` is an independently implemented narrow loader, with no
proprietary GameNative `libredirect` or other launcher/GPU helper. It exports the
Wine loader's reservation markers, loads APK-native `ntdll` and invokes its
original `__wine_main`. Only fixed Wine logical paths are adapted through
`dladdr`, `dlopen`, `execv` and `posix_spawn`. An inert installed-data marker
preserves Wine's canonical dirname for PE/data discovery. Actual executable
loads/re-execution always target `nativeLibraryDir`. ELF relocation changes only
bounded dynamic SONAME/DT_NEEDED/RUNPATH strings, preserving file offsets and
instruction sections. The independent audit checks actual relocated bindings,
not the source filename map alone.

The private `proton-prefix` is separate from the Wine9 prefix. Its Z: drive maps
to `/`, retaining original absolute-input conversion. A private real `system32`
directory contains individual links to installed builtin PE files. New Wineboot
outputs/subdirectories stay private; it is never a directory link into immutable
content. The pinned `setupapi/fakedll.c` recognizes existing reparse-point files
as installed Proton builtins and registers them without copying. Only the private
prefix receives those links, so Windows builders need no symlink privilege.
Native Wine's original initialization still runs; no fake ready prefix is used.
The registry selects `libarm64ecfex.dll`, the headless null graphics driver and no
Wine audio driver. Server synchronization is explicit; ntsync/fsync/esync and
desktop driver paths are disabled. No CPU feature or relaxed ordering/precision
gaming preset is forced. Sandbox, JIT and first-prefix latency need headset tests.

Native source/cache keys cover this backend's code, mapping, public runtime pins
and selected NDK, rather than the entire evolving mod. Cached artifacts are
SHA/size/source/inventory checked and the actual stage is audited before delivery.
Both success and failure retain `proton-stage-audit.json` plus bounded
`proton-stage-summary.json` with input identity/counts/error context for support.
Preparation logs identify package validation, layout, small compilation and
binding audit. These checks run on the builder, not at headset startup. Transient
owner/duplicate audit files are removed; verified public runtime cache remains
resumable. Returned `androidNativeFiles` is the exact flat-name/SHA/size inventory
for Unity importer and signed-APK validation; the Proton manifest uses schema2.

Actual source-stage evidence: 27 ARM64 ELF files, 1,156 native import occurrences,
126 native/EC PE contexts and 19,904 PE import occurrences qualify against the
selected NDK API29 and the owner's unchanged DLL. A separate actual host dynamic
loader/process fixture exercises the own launcher through native production
symbols, nested loader/server spawning, strict stdout framing and private prefix
links. These are static/host/native-compile witnesses. No Android app-context
Wine/FEX execution, campaign/Guildmaster synthesis equivalence, headset loading
time, JIT permission, performance or usable gameplay is established by them.

Wine LGPL2.1, FEX MIT and the Wine artifact's static ntsync LGPL3 notices accompany
the payload. Provenance distinguishes downloaded artifact hashes, upstream release
references, PPA source-package hashes and build recipes; it does not invent an
exact Git-to-binary attestation. Corresponding public source/build dependencies
remain linked in the lock and notice manifest. No proprietary game payload is
distributed by the builder source release.

Primary sources: [pinned Proton Wine](https://github.com/GameNative/proton-wine/tree/555aa70febb7d36e82d96697b554ff8f4fe0bb1a),
[official FEX ARM64EC acquisition](https://wiki.fex-emu.com/index.php/Development:ARM64EC),
[pinned FEX release source](https://github.com/FEX-Emu/FEX/tree/9fbdc00bd6401aff3b32d79e78ff98b8a13e4dcf),
and [Wine builtin installation](https://github.com/GameNative/proton-wine/blob/555aa70febb7d36e82d96697b554ff8f4fe0bb1a/dlls/setupapi/fakedll.c).

## Explicit Box64 / Wine9 fallback and historical proofs

Package `libQuestApparance.so` as an Android ARM64 plugin. Package `libquest_box64.so` and `libquest_wineserver.so` as Android-only **executables**, with native preload disabled and `extractNativeLibs=true`. Their interpreter is `/system/bin/linker64`. Every OS executable launch starts in `nativeLibraryDir`. Wine, the Windows worker and the owner's DLL are readable interpreted data; none is executed from writable app storage. A narrowly audited Android-only Box64 change accepts readable x64 ELF inputs without an executable file bit. Native ARM files retain the ordinary executable requirement.

Linux Wine 9 hardcodes `/tmp/.wine-UID` for server sockets and does not use `TMPDIR` for that path. The two pinned CPU ELF files are checksum/count guarded and only that string is relocated to `./.wine-UID`. Both original client and server first change to the absolute private `WINEPREFIX`, so this directory stays inside internal app storage. NUL padding preserves every ELF offset and both original format arguments. Original ownership, socket and lock checks remain active; the manifest records each original and modified SHA-256.

`upstream.lock.json` pins Box64 **v0.4.4 / 2f130fab1d6e1a4ee8a71dc60cfdfcc839ad192a**, portable vanilla Wine 9.0, licenses and the Ubuntu x64 Unix library closure. The payload includes provenance, per-file SHA-256 records, MIT/LGPL notices, GCC's runtime exception and each packaged dependency's copyright notice. All proprietary data comes from the owner-provided game; binary outputs are kept outside Git. The optional Wine GUI/media/device services are disabled for this CPU worker. Unity continues to supply all actual game media, input and rendering.

Focused verification:

```sh
python3 -m unittest discover -s tools/quest-procedural-runtime/tests -v
```

`replay.py` verifies genuine native journals captured from the original Windows game. Every raw build input and recorded task file is checked against its original SHA-256. Scheduling can assign different opaque object-set keys and resource handles, so the strict original task decoder canonicalizes only those keys and resource IDs resolved by the exact descriptor. Every tier, group name, nested parameter byte, frame, vertex, normal, colour, UV, triangle, bound and material descriptor remains exact. This normalization is used only for proof comparison.

`bridge_proof.py` exercises that same journal through the actual C ABI/socketpair/posix_spawn shim, including reentrant original callbacks, string ownership, task leases, original Save on a disposable procedure copy and engine stop/restart. The reference game directory remains read-only.

Evidence from the original Scenario 001 oracle: **18 native build requests, 87 resource requests, 18 canonical task streams and 56,133 task bytes** match through the direct Windows worker, through the C shim and through our source-built ARM64 Box64 v0.4.4 under QEMU. The C shim delivered 192 reentrant original log callbacks. Source-built Android Box64, shim and launcher pass ELF/export/dependency audits with the exact Unity 2021.3.5 Android NDK.

The ARM QEMU proof uses a private Linux/glibc environment and records `androidExecutionVerified=false`. Auxiliary Wine GUI/device services can emit errors under that proof environment; the actual engine and full recorded task comparison complete. The original generation oracle also retains its reported native UI `IsLoading` state; this proof establishes native generation bytes, not a playable headset scene.

Android/Bionic loader behavior, SELinux process execution, first Wine-prefix setup latency, headset memory/performance and sleep/resume remain hardware acceptance checks. Their result must be taken from a real full-target capture. The general runtime path supports changed parameters and arbitrary original procedures; further captured dynamic-prop, obstacle and seed fixtures strengthen the existing byte comparison without replacing native execution.

The B624 headset capture proves that the Android worker reached Box64 and then
failed Wine's `__libc_start_main` relocation before Wine main. The pinned upstream
`src/emu/entrypoint.c` conditionally compiled only the Bionic guest entry on an
Android host; the earlier Linux/glibc-host proof compiled the glibc branch. The
Quest adaptation preserves the complete upstream entry bodies and includes both
64-bit guest entries on Android. Its source checksum is pinned separately, and
the actual final ARM64 executable must export `my___libc_init`,
`my___libc_start_main` and `my___libc_init_first` before staging. This proves the
missing entry is present; successful Wine and original-engine execution on the
headset still require a new hardware capture. The small test fixture is the
unchanged Box64 revision's MIT-licensed entry source.

Primary sources: [Box64 Wine support](https://github.com/ptitSeb/box64/blob/v0.4.4/docs/WINE.md), [Box64 Android build](https://github.com/ptitSeb/box64/blob/v0.4.4/CMakeLists.txt), [portable Wine build and source instructions](https://github.com/Kron4ek/Wine-Builds), [official Wine 9.0 source](https://gitlab.winehq.org/wine/wine/-/tree/wine-9.0), and [Android executable storage restrictions](https://developer.android.com/about/versions/10/behavior-changes-10#execute-permission).

The Windows x64 source build uses the pinned CMake/Ninja wheels and the selected
Unity Android NDK. Both modern `Python3_EXECUTABLE` and the upstream compatibility
variable point to the running private builder interpreter. The two original
`git_head.h` dependency graphs retain their outputs and dependencies, but generate
the pinned archive revision through `cmake -P`; a source archive build needs no
POSIX `sh` or Git subprocess. This affects build provenance only, with the original
`NOGIT=ON` native option retained. The bridge, launcher and x64 worker use the real
NDK LLVM executables with explicit targets, not Windows batch wrappers.

The exact socket setup is visible in Wine's original [client setup_config_dir/init_server_dir](https://github.com/wine-mirror/wine/blob/wine-9.0/dlls/ntdll/unix/server.c) and [server create_server_dir](https://github.com/wine-mirror/wine/blob/wine-9.0/server/request.c).

The B625 capture reaches Wine's `ntdll.so` but fails GNU `__errno_location`
and `__assert_fail` binding on Bionic. `box64_bionic_abi.py` verifies the pinned
complete `wrappedlibc.c` and private map before adding Android-only bindings.
GNU errno maps to Bionic `__errno`; the assertion adapter preserves the GNU
arguments and terminates through Bionic `__assert2`. GNU ctype accessors return
thread-local pointers to 384-entry tables with GNU classification bit order,
32-bit case values, signed-byte indexing and separate EOF. Linux source branches
and maps remain unchanged. Tests include the complete pinned MIT source.

The worker subprocess explicitly uses `C.UTF-8`. It ships no MO catalogs, so
the font library's `dcgettext` adapter preserves the original diagnostic string.
The GNU `initstate_r`/`random_r` adapters preserve caller-owned 48-byte structures,
all state-size thresholds, 32-bit signed seed boundaries, state metadata and
GNU output sequences. They do not use Bionic's process-global random generator.
The host GNU oracle verifies every ctype entry and 245,760 generated values;
actual NDK compilation verifies the Android helper's exports. Neither establishes
successful Wine/original-engine execution on Quest.

`abi_audit.py` reads actual ELF dynamic/version tables with no Python package
dependency. Native builds retain SHA-bound copies of their compiled wrapper
headers; staging checks those exact GO/GOM/GO2 targets against the actual Box64
exports and public API29 Bionic dependencies. The fourteen required guest ELF
files cover bootstrap, `win32u`/`ws2_32` reached by the original DLL's static PE
imports, libgcc and the eight explicitly emulated font libraries. Guest-export
bindings retain version/default-version semantics. Unknown required dependencies
and strong imports fail before content delivery; unresolved weak imports remain
legal. A real old B625 payload fails with 22 unresolved occurrences of the seven
fixed names. `guest-abi-audit.json` records the final static check and keeps
`androidExecutionVerified=false`.

Other Unix Wine modules remain available as original data, but this gate does
not establish their Android support. In particular the original `dnsapi.so`
retains its unresolved GNU resolver-state dependencies; it is absent from the
current worker's nineteen-DLL static import closure. No fake resolver state is
provided. Dynamic DLL loading, a changed engine dependency graph, fresh prefix
initialization, callbacks, sleep/resume and hardware performance still require
runtime evidence; they must not be inferred from a passed symbol gate.

Every launch starts `procedural-worker.log` with UTC, parent/child PID, spawn
result and the native build input key. Only the previous attempt's last 256 KiB
is retained in `procedural-worker.previous.log`. This avoids attributing an old
failure to a new APK while keeping the existing collector's current-log path.

ABI references: [GNU ctype layout](https://github.com/bminor/glibc/blob/glibc-2.39/ctype/ctype.h),
[GNU reentrant random state](https://github.com/bminor/glibc/blob/glibc-2.39/stdlib/random_r.c),
[Bionic errno](https://github.com/aosp-mirror/platform_bionic/blob/android-14.0.0_r1/libc/include/errno.h),
and [Bionic assertion arguments](https://github.com/aosp-mirror/platform_bionic/blob/android-14.0.0_r1/libc/include/assert.h).
