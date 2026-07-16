# GSD State

- **Milestone:** v0.1 (first playable VR release)
- **Position:** **Hardware iteration loop.** 10 on-device test rounds done; menu + campaign map + scenario entry + clicking + card fan + board targeting all working in VR. Current round: map visuals, story-lock fallback, free movement, roll-only card reveal, 3D cards in hand, control-board redesign (all merged, awaiting test #11).
- **Last update:** 2026-07-17

## Done
- Research (5 reports + PATCH-TARGETS real-DLL audit) → ARCHITECTURE → ROADMAP
- P0 skeleton, P0b asset pipeline, P1 XR bootstrap (M1), P2 hands & primitives (M2),
  P3a board touch, P3b Demeo card hand (M3), P3c world-space UI (M4), P4 comfort,
  P5 integration + in-VR settings panel + packaging
- All builds green at every merge; Harmony inventory: 17 patched methods, zero cross-module
  duplicates (`docs/PATCH-INVENTORY.md`)
- `dist/GloomhavenVR-0.1.0.zip` (17 entries, layout-verified against runtime path constants)

## Human hardware steps (in order)
1. Windows-PC: Gloomhaven + BepInEx 5.4.23.5 + release zip per `INSTALL.md`; chainload smoke test (log lines per `docs/TESTING-P1.md`)
2. Quest 3 session: full-loop script `docs/TESTING-FULL-LOOP.md` (covers P1–P4 checklists)
3. Unity 2021.3 editor harvest (`unity/HARVESTING.md`) — replaces provisional RuntimeDeps 1:1; only needed if XR misbehaves or before public release
4. SteamVR glove import + bundle build (`unity/HANDS.md`) — replaces procedural hands/props
5. Report tuning values flagged in TESTING docs (world scale, fan arc, panel poses, poke tuning, laser cone)

## Known gaps (accepted for v0.1 pre-alpha)
- Runtime behavior entirely unvalidated on hardware (all code paths compile-verified + desktop-dev-harness only)
- Asset bundle absent → procedural visuals ship
- Item/ability/augment bars + party HUD not physicalized (flat screen fallback)
- Menu rig assumes a usable Camera.main in menu scenes (config off-switch exists)
- Multiplayer untested by design (v1 single-player scope)

## Standing decisions
- BepInEx 5.4.23.5, HarmonyX, net472, publicized refs; OpenXR 1.10.0 + XR Management 4.5.0; MultiPass default
- Never patch ScenarioRuleLibrary/Bolt; commit through UI seams only
- License GPL-3.0 (LCVR/RepoXR pattern reuse, credited); SteamVR hands BSD-3
- Module config: `dev.gloomhavenvr.<module>.cfg` via `ModuleConfig.Create`
