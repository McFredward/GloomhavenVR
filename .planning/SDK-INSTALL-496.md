# ModBuild 496 — SDK selection regression

## Cause

The 494 release preparation added global.json 8.0.400/latestPatch to constrain hosted
compiler selection. That unintentionally rejected the user's installed SDKs 10.0.102
and 10.0.301. The failure at commit 9cc13012 happens before MSBuild can load; it is
not caused by the subsequent shared-window documentation commit.

## Repair

- global.json uses major roll-forward: prefer 8.0.4xx when present, otherwise allow a
  later installed stable SDK. CI/release explicitly assert the SDK8 feature band after
  setup, so a local fallback does not silently change the hosted compiler.
- install.ps1 probes --version from the repository before downloads, config changes
  or game writes. All dotnet calls resolve SDKs from that directory and restore the
  caller's location. Failure reports the policy and installed SDK inventory.
- The conditional legacy ShaderOcclusionPatcher restore tool targets net8.0. SDK10
  alone does not install runtime8; only this tool invocation enables runtime major
  roll-forward. The Unity plugin remains net472 and no game behavior changes.

## Evidence

- Isolated SDK10-only host reproduces the old global.json failure; the new policy
  resolves successfully. With SDK10.0.102 and 10.0.401 together it selects 10.0.102.
- Full strict solution builds: SDK8.0.423, SDK10.0.102 and SDK10.0.401, zero errors/warnings.
- Full local guard passes all 17 checkers. Seven compiled types differ only by ModBuild.
  Config625, patch surface152 and log tokens4716 unchanged; bundle74943763bytes unchanged.
- Wire assertions:253055 under runtime8 and253063 under runtime10. Per-vector tracing
  of the SAME binary explains every extra assertion: compressed group packets4→6
  (+2 budget checks) and a compressed fixture48→54bytes (+6 truncation checks).
  All decoded payload/golden checks pass; no vector was skipped or weakened.
- Capture18206, playback466, board refresh1216 and all12 runtime negative controls pass.
- PowerShell parser and isolated native-command tests cover missing/incompatible SDK,
  success, scope restoration, exit forwarding and absence of early installation writes.
- Maintenance tool under runtime10-only: old launch exits150 (missing runtime8);
  --roll-forward Major reaches its CLI and an empty-directory restore exits0, copies0files.

The real Windows installation and headset launch remain user verification. No actual
game files were modified during the script tests. Developer SDK selection follows
[Microsoft's documented roll-forward rules](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).
