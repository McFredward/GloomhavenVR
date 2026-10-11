# Hosted SDK lookup repair — 2026-10-09

## Cause and scope

Full logs of Actions runs37854070367 (Build649) and37858258266 (Build651)
show the same three failures: `graphics-profiles`, `render-quality-runtime`
and `player-settings-help`. Each tries `~/.dotnet/dotnet` despite the SDK
installed by `setup-dotnet` being available on `PATH`. Source/build checks and
the other90 CI suites pass in651. The full-check aggregator correctly refuses
to issue successful evidence when either failed shard remains red.

Local testing missed this because this machine has that home-directory SDK.
No game bug, runtime or network change is needed to repair these failures.

All three scripts now preserve a nonempty explicit `DOTNET` override, otherwise
resolve `dotnet` on `PATH`, otherwise retain the original local SDK fallback.
An explicitly invalid override still fails instead of being silently replaced.
The CI-selected command census found no other direct instance of this defect.
Local-only native Unity tools were not broadly refactored in this bounded repair.

## Verification

- Original failure reproduced with a temporary empty `HOME`, `DOTNET` unset
  and the real installed SDK on `PATH`.
- Graphics profiles:281 unchanged production assertions pass under that setup.
- Render quality:114 production assertions,23 causal controls,2 startup controls
  and14 source bindings pass under that setup.
- Player help:2,296 production assertions,3 runtime controls and5 content controls
  pass under that setup.
- Nine fast executable-selection regression tests cover explicit override,
  missing home SDK, empty override and local fallback. The profile/render tests
  intercept each actual script's first compilation boundary; restoring either
  original home-only expression is rejected by the PATH case.
- Workflow topology5, trusted-proof39, scheduler18 (2 optional resource skips),
  release upload21 and release topology36 assertions/tests retain their original
  distinct counts. All16 registered source suites and5 bilingual doc pairs pass.

CI runs the fast lookup regressions before its strict build. Existing93 runtime
suites,4 shards,2 workers per shard, golden-vector compile, strict warning gate,
release proof and optional artifact policy remain intact. No test is skipped or
made nonblocking. This is focused local validation, not a new179-suite local gate.
The source-changing dev push still requires the entire hosted workflow to finish;
its Actions run is the authoritative final hosted result.

Worker evidence is archived in `.planning/debug/ci-repair/{help,toolchain}/`;
full original job logs and local source-gate receipts remain in that same audit
directory. Production source, assets and native-game fixtures are unchanged;
ModBuild remains651.
