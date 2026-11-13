# Preserving dynamic Apparance output on Quest

Read-only feasibility audit, 2026-10-05. No native/game/procedure input was changed and no speculative runtime implementation is included in this checkpoint. This document supplements the original Campaign state audit; it does not reclassify missing dynamic presentation as optional.

## Result

The supplied Windows engine can plausibly be retained as an isolated, on-device CPU generation service under a Windows compatibility layer plus x86-64-to-ARM64 execution. The actual engine boundary is unusually suitable: twelve C entrypoints exchange typed byte buffers and asset descriptions; the engine does not import Unity, Direct3D, OpenGL or Vulkan. Unity rendering can remain native ARM64.

This is a researched execution strategy, not a working Quest runtime. A Windows DLL cannot be renamed into an Android plugin. A real Win32/CRT loader and CPU execution layer, copied-buffer adapter, Android packaging and measured complete-scenario generation are necessary. No inspected component supplies those twelve integrated Quest entrypoints today. Static pristine-scenario export still does not close dynamic prop/tile/seed behavior.

A managed procedure executor is a second possible route. Its authored graph data is available, but 159 native builtin operator types used by the supplied procedure library still require exact semantics. Neither readable XML nor broad operator descriptions establish bit-compatible PRNG, mesh topology, frame transforms, recursive selection or output ordering.

## Exact inspected engine

Original input: `ressources/GH_Data/Plugins/x86_64/ApparanceEngine.dll`.

- SHA-256: `03cd691469b2169e701982098e16729dbca7398c3c2637d6377ebabf52302260`.
- Size: 1,405,952 bytes; PE32+, machine `0x8664` (x86-64), Microsoft x64 binary, native CLR directory empty.
- Image size: `0x272000`; relocations, TLS, unwind/exception metadata and CRT initialization are present.
- 905 named exports, of which twelve begin `Apparance` and match the supplied `Apparance.Net.Interop` imports. Most remaining exports describe native C++ types/metadata; they are not an Android C API.

The sixteen imported modules are:

| Module | Imported entries | Relevant dependency |
| --- | ---: | --- |
| `KERNEL32.dll` | 80 | Threads, TLS, critical sections, events, timers, virtual memory, files, enumeration, process creation, exception unwinding |
| `USER32.dll` | 1 | `MessageBoxA` |
| `ole32.dll` | 1 | `CoTaskMemAlloc` |
| `MSVCP140.dll` | 30 | MSVC C++ locale, threads, mutexes, conditions |
| `WS2_32.dll` | 26 | Socket/event operations and ordinal imports |
| `VCRUNTIME140.dll` | 18 | C++ exceptions/RTTI, SEH and memory/string operations |
| `api-ms-win-crt-runtime-l1-1-0.dll` | 14 | CRT initialization, errors, termination |
| `api-ms-win-crt-string-l1-1-0.dll` | 17 | Windows CRT narrow/wide string functions |
| `api-ms-win-crt-stdio-l1-1-0.dll` | 19 | Streams, formatted I/O, files |
| `api-ms-win-crt-heap-l1-1-0.dll` | 4 | Allocation |
| `api-ms-win-crt-convert-l1-1-0.dll` | 7 | Character/numeric conversion |
| `api-ms-win-crt-math-l1-1-0.dll` | 21 | Floating-point functions |
| `api-ms-win-crt-utility-l1-1-0.dll` | 2 | `rand`, `srand` |
| `api-ms-win-crt-time-l1-1-0.dll` | 1 | `_time64` |
| `api-ms-win-crt-filesystem-l1-1-0.dll` | 1 | `_wstat32` |
| `ADVAPI32.dll` | 1 | `GetUserNameA` |

Import presence establishes a loader dependency, not that every call executes during Campaign generation. Conversely, suppressing `MessageBoxA` alone does not remove the required CRT/TLS/thread/exception machinery. No graphics backend is imported by this DLL.

Private audit receipt: `/home/claw/quest3-local/full-network/apparance-proof/original-apparance.json`. It contains exact import/export lists, procedure type references and native builtin-ID occurrences. It explicitly records `nativeRuntimeEvaluated=false` and `androidRuntimeValidated=false` for this spike. The already separate original Windows exporter is evidence of a different execution environment.

## Actual managed/native interface

`Apparance.Net.Interop` declares all imports with `CharSet.Ansi`; the original Windows ABI and marshalled values must be inspected explicitly rather than inherited as Android struct/callback assumptions.

| Entry | Boundary data |
| --- | --- |
| `ApparanceInitialise` | Procedure directory, log callback, synthesizer count, buffer MB, live-editing flag; integer result |
| `ApparanceIsRunning` | Integer running state |
| `ApparanceUpdate` | Delta time and an `Apparance.Net.Vector3` view position |
| `ApparanceSave` / `ApparanceShutdown` | Engine lifecycle |
| `ApparanceCreateEntity` / `ApparanceDestroyEntity` | Original integer entity handles and old-handle reuse |
| `ApparanceEntityBuild` | Handle, procedure ID, parameter byte length/pointer and dynamic-detail flag |
| `ApparancePopEntityTask` / `ApparancePopEngineTask` | Integer result plus task byte length/pointer |
| `ApparanceUpdateAsset` | Entity context, asset descriptor/ID, bounds availability, float frame and variant count |
| `ApparanceGetNextAssetRequest` | Asset description string plus context/ID |

`Entity.Update` serializes the unchanged `ParameterCollection`, sends its bytes and then calls `BeginContentUpdate`. `Entity.CheckTasks` immediately reads returned native bytes. `DecodeAddObject` already consumes groups and resource instances with hierarchy, placement frames and parameter collections, plus generated vertices, normals, colours, UVs, triangle parts, materials and bounds. Existing native managed placement completion/debug/removal flows can therefore consume the original engine's task streams; a replacement need not recreate Unity objects inside the guest CPU environment.

A service bridge must copy input/output bytes. Guest pointers, callback addresses, heap ownership and Windows string-allocation contracts cannot cross an ARM64 process boundary. Preserve integer handle reuse, operation ordering, task lifetime, asset-query/response correlation, parameters, variant counts and callback completion. Account explicitly for Windows string encoding and the Microsoft x64 struct ABI versus Android AAPCS64. The original log callback also needs a real IL2CPP-compatible reverse boundary or copied log events.

## Executing the original binary locally

Box64's official source provides ARM64 and Android build switches. Its documented x64 Wine route runs Windows x64 executables, and Winlator's own repository demonstrates an Android application built around Wine and Box64. This is primary-source evidence that the general platform combination exists, not that this plugin has been validated in our APK. [Box64 build configuration](https://github.com/ptitSeb/box64/blob/main/CMakeLists.txt), [Box64 Wine modes](https://github.com/ptitSeb/box64/blob/main/docs/WINE.md), [Winlator](https://github.com/brunodev85/winlator/blob/main/README.md).

The narrow fidelity spike should use a small Windows x64 worker which loads the owner's exact DLL, forwards the twelve operations and copies task bytes. Run only this CPU worker under a pinned Android-compatible Wine/Box64 stack; keep the game, XR, audio, assets and renderer native. Link it through a local private IPC/native ARM64 shim. This inference follows from the inspected import and managed boundary, not from a benchmark. An isolated worker also prevents guest signal/TLS/heap machinery from sharing Unity's managed/native execution state.

Stock FEX supplies another x86-on-ARM64 execution layer and documents Wine/Proton compatibility, but its normal Linux installation requires an x64 root filesystem. It is not a drop-in Bionic Unity library. Do not confuse a normal Linux FEX package with an Android-compatible packaged runtime. [FEX prerequisites and supported hosts](https://github.com/FEX-Emu/FEX/blob/main/Readme.md).

A potential in-process translator would additionally need robust native-to-guest calls, guest-to-native callbacks and Win32/CRT state isolation. ARM64 Wine modes need a CPU translation module for x64; current Box64 documentation describes ARM64EC support as future work. Avoid selecting that mode merely because the host is ARM64. Use an actually supported and tested x64 worker route first. [Box64 Wine mode definitions](https://github.com/ptitSeb/box64/blob/main/docs/WINE.md).

Android 10+ targets cannot execute helper files from the writable app home directory. Code must be packaged with the APK, with an install/package location accepted by the OS, and the guest's PE loading/dynarec mappings must work under the Quest app domain. The current Android target cannot adopt a Termux app directory or a desktop `exec` recipe unchanged. This is a packaging proof requirement, not evidence that a packaged helper is impossible. [Android execution restrictions](https://developer.android.com/about/versions/10/behavior-changes-10#execute-permission).

Required acceptance sequence:

1. Build/package the selected open-source ARM64 compatibility stack and worker entirely from pinned inputs; copy only the owner's original DLL/procedure library through the builder. Prove startup/shutdown and filesystem access within a standalone APK, without a runtime PC or vendor service.
2. Exercise the actual twelve-operation ABI through copied data, including native log/error propagation, asset requests and view/dynamic-detail updates. Validate one generated native mesh and resource placement against the original Windows oracle.
3. Execute a complete Campaign scene, then ability-created props with changed sibling contexts, moved/destroyed obstacles, changed tile avoidance, room/door visibility and valid imported save continuation. Compare original output and completion order for identical requests; do not normalize away meaningful differences.
4. Measure cold initialization, memory, total generation, incremental updates, process restart/suspend behavior and Unity frame stalls. The native engine can synthesize asynchronously, but that alone does not prove an acceptable Quest latency or memory footprint. Keep output caching as an optimization over a complete runtime fallback.

No step above has been passed on Quest by this read-only audit. The startup helper proof is the next decisive gate, before spending effort on integration or promising full parity.

## Binary lifting and CPU emulation

Remill can lift machine instructions to LLVM bitcode; McSema supports PE/ELF control-flow recovery and x86-64 lifting. Their own documentation still separates control-flow recovery from instruction translation. Neither reading PE bytes nor generating bitcode automatically substitutes the DLL's Win32/CRT imports, TLS initialization, indirect C++ calls, exceptions or callback/struct ABI. A static lifted ARM64 plugin would need complete control-flow/dependency recovery and differential output validation across the dynamic state domain. This is a separate native compatibility project, not a normal Unity rebuild. [Remill](https://github.com/lifting-bits/remill/blob/master/docs/README.md), [McSema](https://github.com/lifting-bits/mcsema/blob/master/README.md).

Unicorn supports x86-64 emulation on Android hosts and provides instrumentation, but its CPU execution API does not itself supply the required Windows loader and service semantics. Executing a trivial export before DLL initialization would prove instruction execution only. It would not prove the procedural engine, multithreading, exception handling or practical generation performance. It is useful for targeted native operator investigation/oracle probes, rather than a justified complete engine replacement by itself. [Unicorn framework](https://github.com/unicorn-engine/unicorn/blob/master/README.md).

## Managed graph executor evidence and missing semantics

The supplied `StreamingAssets/Procedures` library contains 601 parseable `.proc` definitions. Their XML includes stable procedure type IDs, typed inputs/constants, numbered operators, connections and outputs; `.procedit` carries editor names/comments/layout. Across those graphs there are 591 referenced operator type IDs. Of those, 432 resolve to supplied procedure definitions and 159 have no supplied `.proc` body. All 159 occur as little-endian identifier constants in the inspected native DLL. Those counts cover the whole supplied library, including obsolete/debug/generated-mode procedures; they are not an asserted Campaign reachability minimum.

Native strings include builtin names, descriptions and typed parameter terminology. C++ exports include `OperatorLibrary::GetEvaluator` and native library/inspection metadata. This proves recoverable metadata exists in the binary; it does not give a safe public twelve-call query for every builtin evaluator or reveal each complete algorithm. The current official operator catalogue documents operations and their input/output concepts, but its inspected HTML contains none of these 159 hexadecimal IDs and is newer than this DLL. Map the exact binary's IDs/signatures before implementing any semantics. [Official builtin operator catalogue](https://apparance.uk/docs/operators/operators.htm).

For a faithful portable executor, the concrete research path is:

1. Compute the real closure from every original runtime template/procedure root and effective Campaign/DLC style. Resolve nested procedure calls, recursion/repeaters, resource catalogue requests and conditional branches conservatively. Identify which of the 159 builtins are actually required.
2. Recover exact builtin metadata/table dispatch from the owned binary or an original-engine metadata/oracle worker. Store IDs, signatures/defaults and source hashes. Names or visually similar outputs are insufficient to equate implementations.
3. Implement typed value semantics and demand/branch/recursion evaluation matching the native engine. For each required builtin, derive and test signed integer overflow/conversion, float rounding, seed/PRNG consumption, frame conventions, list ordering, asset bounds/variants and geometry generation. Do not substitute `System.Random` or a convenient mesh primitive absent proof.
4. Use the original Windows engine as an offline differential oracle for individual operators and complete procedure graphs. Include boundary values, repeated calls, seed/context variations and dynamic tile lists. Preserve the same native placement stream or prove equivalent native placement/callback behavior.

Finite static resource placement and arithmetic operators may be straightforward after exact mapping; recursive space construction, noise and final mesh topology are material unknowns. This strategy solves arbitrary dynamic inputs only once the required builtin closure is complete and validated. A nearest cached recipe, guessed seed reduction, empty output or static fallback does not meet that requirement.

The official product information lists native runtimes for several custom desktop/console platforms, while Unity's supplied plugin is Windows x64. It does not publish a matching Android runtime for this owned 2022 binary. No vendor contact, provider registration or proprietary runtime download was attempted. [Apparance runtime products](https://apparance.uk/products/products.htm).

## Decision supported by current evidence

Prioritize the on-device original-engine worker feasibility gate if preserving dynamic output quickly is the immediate objective: it retains native operator behavior and avoids reconstructing 159 unknown builtin implementations. Continue exact authored exports/caches in parallel where they remain useful. Keep a managed graph executor as a longer independent route if packaging/performance defeats the original-engine worker, with the original binary retained as its offline oracle.

The evidence supports neither declaring the full procedural port finished nor declaring Android execution impossible. It identifies a concrete narrow ABI and platform-compatible execution family, and the remaining proofs needed to turn that family into an acceptable native Quest implementation.
