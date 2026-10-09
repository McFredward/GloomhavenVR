# Native card-restore proof

Execute the publisher GH.Runtime card controller and the production Harmony prefix
inside Unity 2021.3.5f1. Run from a prepared worker checkout:

```sh
python3 scripts/native-bugfix-cards-runtime/run.py
```

Requirements: original read-only `ressources/GH_Data/Managed` assemblies, the
existing HarmonyX 2.7.0 NuGet dependencies, `.dotnet/dotnet`, `xvfb-run`, and Unity
at `/home/claw/unity-2021.3.5/Editor/Unity`. Outputs go under ignored
`.planning/debug/native-bugfix-cards-runtime/`. `--source-root`, `--output-dir` and
explicit `--reuse-project` may override their corresponding paths. The runner
never modifies publisher inputs or the production source. One-off Unity import
caches are removed even on failure; reuse preserves only its explicitly selected
project cache.

The production variant must reproduce unpatched native failure before demonstrating
the repair. Two mutations must fail their exact causal assertion, rather than
failing compilation or boot. Two owner-member variants must let original restore
continue. The fixture uses actual native card widgets, half-action buttons,
`Init`, `CachePhase`, `Reset`, `RestorePhase` and `SetPhase`. It also executes
original damage-redirection and nested-summoner counterexamples for the excluded
rules patches. Transport and actor-stack context are explicit external boundaries;
this does not simulate a complete live multiplayer match.

See `.planning/BUGFIX-RULES-AUDIT.md` for the evidence, adopted scope, original
native references and mixed-client limitations. Fixture variants are not shipped
with the mod and must not be registered as gameplay patches.
