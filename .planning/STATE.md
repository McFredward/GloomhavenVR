# GSD State

- **Milestone:** v0.1 (first playable VR release)
- **Position:** **Hardware iteration loop.** Test #17 done: placement/re-placement + fan + tray sizes largely confirmed on device. Round 17 merged: (1) window frame/recess REMOVED (user dislike); camera-plane videos re-routed to RenderTexture and composited into BOTH eye RTs with behind-screen disparity ([WorldUI] VideoDepth 0.8 m, kill switch VideoDepthLayer) — menu UI floats in front of receded video background; intro force-suspends stereo in pre-menu scenes (both eyes guaranteed; IntroPlayer's VideoPlayer binding is scene-serialized, invisible to code). (2) Doff/don hard-lock fixed: virtual-mouse currency keep-alive was edge-triggered from FlatScreen-only warp calls — now level-triggered per-tick incl. device re-add (VirtualMouseBridge); VRPresenceWatch → SessionResumed sweep re-floats open modals in front of the head; non-dominant A/X chord force-closes the top modal via UIWindow.Escape/Hide; CanvasConversion re-fit hysteresis (CombatLog churned 2×/s). (3) Objectives density 0.6× (initiative untouched); tray always spawns head-relative then re-pins (PINNED world pose was never persisted — spawned at map bottom); scale-aware near/far clip planes (hands clipped at max zoom). Awaiting hardware test #18.
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
