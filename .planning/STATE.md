# GSD State

- **Milestone:** v0.1 (first playable VR release)
- **Position:** Planning complete (research + architecture + roadmap). Next: Phase 0 + Phase 0b in parallel.
- **Last update:** 2026-07-15

## Done
- 5 research reports in `.planning/research/` (code analysis of decompiled game + prior art + toolchain, all claims verified)
- `PROJECT.md` (vision/requirements R1–R5), `ARCHITECTURE.md` (concept), `ROADMAP.md` (phases P0–P5, branch plan)
- Local tooling: ilspycmd working (DOTNET_ROOT=$HOME/.dotnet), decompiled sources in `decompiled/` (gitignored)

## Next
1. Spawn workers: `feat/skeleton` (P0) + `feat/assets` (P0b, needs Unity 2021.3.5f1 editor — human/GUI step for editor install may be required)
2. Then `feat/xr-bootstrap` (P1) — first on-headset test is the earliest hardware checkpoint (needs Quest 3 + Windows machine; dev happens here, runtime testing is manual)

## Standing decisions
- BepInEx 5.4.23.5, HarmonyX, net472, publicized refs
- OpenXR 1.10.0 + XR Management 4.5.0 (harvested from dummy 2021.3.5f1 build), MultiPass first
- Never patch ScenarioRuleLibrary/Bolt; commit through UI seams only (s_Callback, Proxy* card APIs, OnClickInternal)
- Mod license GPL-3.0 (LCVR/RepoXR pattern reuse)
