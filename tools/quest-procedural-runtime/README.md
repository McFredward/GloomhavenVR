# Original procedural runtime bridge

The full Quest target preserves the owner's original `ApparanceEngine.dll`. A small Windows x64 worker loads its twelve C ABI exports. Pinned ARM64 Android Box64 executes that worker through portable Wine 9.0. Unity, original managed placement decoding, rendering, gameplay and networking remain native Quest code.

The runtime copies each input parameter buffer, resource descriptor, 15-float resource frame and output task buffer across a local socketpair. Guest pointers and Windows callback addresses never cross processes. Original synthesis threads, procedure evaluation, operator implementations, seeds, task completion and engine entity handles remain owned by the original engine. The runtime forwards original task bytes unchanged.

`runtime.stage(output_cache, ndk, android_plugin_directory, streaming_assets_directory, owner_engine_dll)` builds the three Android artifacts and stages the interpreted payload under `StreamingAssets/ProceduralRuntime`. The normal full-target owned-content archive can therefore install this directory with the existing receipt and overall progress flow. Builder requirements are in `requirements.txt`; the wireless installer needs none of them.

The native contract is:

- `quest_apparance_configure(nativeLibraryDir, payloadDirectory, writableDirectory)` returns **0 for success**, -1 for invalid or active configuration. Call it before the original engine starts. Configuration accepts paths up to 4,095 bytes.
- `writableDirectory` must be under Android `Context.getFilesDir()` (internal app storage). Wine's prefix needs real Linux symlinks and Unix sockets; external `Application.persistentDataPath` is an emulated-storage filesystem and cannot provide that contract. The installed payload and original procedure files may remain in the existing external owned-content directory because Wine reads them through absolute `Z:` paths.
- `quest_apparance_last_error()` returns bounded, deduplicated failure context. Transport errors also enter the original log callback after the current reply is consumed and the ABI mutex is unlocked.
- All twelve original `Apparance*` exports retain their signatures and results. `ApparanceInitialise` preserves original success value 1. `ApparanceUpdate` preserves the by-value three-float view struct. Asset request strings are ARM heap allocations compatible with the existing Android P/Invoke string marshaler. Native task buffers remain leased through their next corresponding pop call.
- The framing protocol is little endian, version 1, with a 20-byte header and a 128 MiB payload limit. Invalid frame sizes, sequence mismatches, malformed resource frames and disconnections stop the worker with useful failure context.
- `ApparanceShutdown` invokes the original shutdown and bounds child termination. Original engine `log.txt` stays in the private Wine prefix's `drive_c`; stderr goes to `writableDirectory/procedural-worker.log`.

For a diagnostic APK, collect the internal `quest-procedural-state/procedural-worker.log` and `quest-procedural-state/wine-prefix/drive_c/log.txt` using `adb shell run-as PACKAGE` alongside the ordinary Unity log capture. They are not in external `Android/data` storage.

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
