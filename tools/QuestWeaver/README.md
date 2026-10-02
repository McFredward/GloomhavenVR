# Quest AOT hook integration

This tool consumes locally owned original managed assemblies and the currently built
mod. It writes disposable staged copies; it never changes the original installation.
No game binaries or decompiled sources belong in this directory.

```bash
dotnet run --project tools/QuestWeaver/QuestWeaver.csproj -- audit \
  --mod /absolute/path/GloomhavenVR.dll --managed /absolute/path/Managed \
  --output /absolute/path/audit.json
dotnet run --project tools/QuestWeaver/QuestWeaver.csproj -- weave \
  --mod /absolute/path/GloomhavenVR.dll --managed /absolute/path/Managed \
  --output /absolute/path/empty-staged-directory --report /absolute/path/weave.json
dotnet run --project tests/QuestWeaver.Tests/QuestWeaver.Tests.csproj
```

The output contains the mod, assemblies whose methods/field helpers were integrated,
`QuestWeaver.Runtime.dll`, a validated same-identity `0Harmony.dll` reflection/metadata
facade, and a conservative `link.xml`. The original HarmonyX static initializer installs
a runtime detour; copying its whole DLL would defeat the AOT conversion. Merge these into the
recovered project's assembly staging. Original runtime dependencies remain necessary.
`audit` returns 2 for unresolved integration gates; `weave` fails before producing
output when such gates exist. Exit 1 indicates a tool/input error; 64 is usage.

Supported wrappers directly call generated Prefix/Postfix/Finalizer hooks. They
preserve activation, original skip, argument/result references, per-class typed
`__state`, injected fields, ordinary preparation/target-selector side effects, priority
and registration order, and finalizer exception suppression/propagation. `PatchAll`,
the actual direct `Patch` argument positions, and `UnpatchSelf` are redirected to an
activation facade. The facade never emits code or reflectively invokes patch bodies.
All HarmonyX prefixes run after a skip, with boolean returns combined by AND. Finalizers
retain the guarded recovery pass after original/patch/finalizer exceptions; secondary
finalizer failures are logged with a process-wide bound and cannot skip later cleanup.
Reference-returning private-field factories become precompiled delegates accessing
the original field. Method selectors are bounded metadata-derived candidate closures;
their original runtime reflection selects exact active targets. A selector escaping
that closure raises a precise error instead of dropping the patch.

Transpilers/IL manipulators, arbitrary runtime factories, constructors, generic
originals, value-type instance originals, pass-through postfixes, before/after owner
constraints and unknown injection semantics are explicit integration gates. Ordinary
new patches within supported semantics are discovered from each current mod assembly.
`--diagnostic-static-subset` is reserved for identifying blocked integration and writes
an `INCOMPLETE` marker. It is not a playable or complete conversion.

The audit's `complete` means the assembly integration has no currently reported gate.
It does not certify IL2CPP, serializer/generic closure, Android native dependencies,
recovered assets, multiplayer authentication, runtime behavior or headset performance.
Those gates belong to the builder and hardware evidence. `aotRisks` names remaining
desktop bootstrap, dynamic assembly-loading, content-path and native-entrypoint concerns;
an empty hook `issues` array does not resolve them. Output validation compares protected
game type signatures and semantic IL before/after serialization. ScenarioRuleLibrary and
Photon Bolt assemblies are not emitted. The executable fixture uses the real HarmonyX
2.7 API and covers N -> N+1 changed behavior/new patches without builder edits.
