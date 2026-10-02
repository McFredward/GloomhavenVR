# Quest assembly integration evidence

Date: 2026-10-03. Worker baseline: dev
`5344a5504adc4158ba466e7a9fbf460dedd5808f`, ModBuild 607.
This evidence concerns generated managed hook integration. It does not establish
that the reconstructed game, multiplayer or existing mod runs under Android IL2CPP.

## Current source-derived integration

`tools/QuestWeaver` reads the current compiled mod and locally owned original game
assemblies through Cecil. The reviewed ModBuild 607 input contains:

| Surface | Observed count |
|---|---:|
| Attributed patch classes | 169 |
| Attributed hook methods | 254 |
| Harmony registration/lifecycle call sites | 176 |
| Direct Patch call sites included above | 6 |
| Reference-returning FieldRef factories | 7 |
| Generated hook/target candidate bindings | 311 |
| Original methods wrapped in staged copies | 233 |
| Protected NetworkManager-family types verified invariant | 7 |

The candidate count includes bounded supersets for metadata selectors. Original
`Prepare`, `TargetMethod` and `TargetMethods` still execute their ordinary reflection
on the target, including initialization side effects. Only the exact chosen targets
activate. A target outside the compiled closure fails explicitly; it is never silently
left without its hook. Direct registrations use the actual `Harmony.Patch` argument
positions, including arbitrarily named hooks, rather than naming conventions.

Generated wrappers call patch bodies directly. They preserve typed per-class state,
by-reference arguments/results, private field injection, original skip, priority and
registration ordering, exception suppression and propagation. Exact installed HarmonyX
2.7 behavior was inspected: every prefix runs after a skip and boolean returns AND;
finalizers have a guarded recovery pass after an original, patch or finalizer fails.
The fixtures pin both details. Secondary finalizer errors receive bounded diagnostics
while subsequent cleanup still runs.

The original HarmonyX class initializer invokes a runtime stack-trace detour. The
converter therefore emits a minimal same-identity `0Harmony.dll` with attribute,
ordinary-reflection and activation APIs. Its complete exact API surface is compared
with the chosen mod's member references; unknown APIs block conversion. It has no
MonoMod, Cecil, Reflection.Emit or runtime-detour dependency. The generated seven
field accessors return genuine references to the original fields; they retain mutation
semantics and reuse identical accessors.

Protected type signatures and semantic method IL are fingerprinted before integration
and compared after serialized output is reread. Metadata token movement is not treated
as a gameplay change. ScenarioRuleLibrary and Photon Bolt are not emitted or patched.
All original inputs remain read-only; generated outputs are local staged copies.

## Executable validation

Run `dotnet run --project tests/QuestWeaver.Tests/QuestWeaver.Tests.csproj --configuration Release`.
The fixture compiles against the real HarmonyX 2.7 API. Separate game/mod assemblies
are integrated, loaded and exercised, with **33 assertions** covering:

- Inactive vanilla behavior, activation and owner-specific UnpatchSelf.
- Typed state, references, results, private fields and generated FieldRef mutation.
- Every prefix after skip; return true cannot undo an earlier false.
- Original errors, exception remapping/suppression, guarded finalizer recovery,
  secondary errors and later cleanup.
- Metadata selectors and Prepare side effects; direct custom-named hooks.
- Missing fields, missing direct hooks, unknown injection/API, protected IL mutation
  and already-integrated input as real negative controls.
- A separately compiled N -> N+1 fixture changing ordinary behavior and adding an
  ordinary patch, integrated with unchanged builder/weaver source and executed in
  a fresh child process.

Both Debug and Release discover their own fixture outputs. No proprietary game
fixtures or runtime assets are tracked. Actual current game output was also serialized
and reread successfully; executing those real Unity game methods is still a native
runtime/hardware gate.

## Remaining full-game AOT and startup gates

The report's `complete` describes hook integration only. A separate `aotRisks` list
currently records 15 source-proven concerns:

1. `Plugin` derives from the original BepInEx `BaseUnityPlugin`. Its constructor
   accesses Chainloader's initialized metadata, Paths, logger/config and assembly
   location before Awake. A GameObject cannot safely instantiate it unchanged.
   Provide a standalone lifecycle adapter retaining the existing configuration,
   logger and module behavior; exclude original chainloader/preloader/detour paths.
2. `Core.Init` depends on `RuntimeDepsLoader.LoadAll`. Desktop assembly-directory
   discovery and `Assembly.LoadFile` cannot supply code to ordinary IL2CPP. Compile
   Android XR packages into the player, replace only the platform dependency-readiness
   seam and let Unity invoke original package initialization exactly once.
3. Content loaders still use `Assembly.Location` for hands, cards, world UI, town
   service and figure-bank content. Resolve them against a Quest content root, keeping
   current resource discovery and embedded-resource names. The figure cache's lifetime
   remains an independent memory gate.
4. Four OpenXR diagnostic P/Invoke exports must be checked against the actual Android
   package native library. A name matching the Windows DLL is insufficient evidence.

The emitted link.xml conservatively preserves the integrated assemblies, but complete
original save/network/UnityEvent reflection metadata and generic native code closure
remain unproven. Android SDK/native dependencies, asset/script GUID binding, Unity
package DLL collisions, platform authentication, rule data, save round trips and native
Unity lifecycle still require independent validation. The Harmony facade validates
the mod's surface; additional framework/plugin consumers need their own compatibility
checks rather than assuming arbitrary desktop Harmony APIs exist.

The concrete next step is a recovered original scene with preserved DLL/script binding,
the standalone BepInEx/config/content-root adapter and compiled XR dependency readiness,
then actual IL2CPP compilation and a bounded original-method/mod-bootstrap hardware
probe. A separately labeled native XR/content diagnostic APK does not close this gate.
