# GSD State

- **Milestone:** v0.1 (first playable VR release)
- **Position:** P0, P0b, P1, P2 merged to main (all builds green). P3a/P3b/P3c/P4 running in parallel on feat/board, feat/cards, feat/world-ui, feat/comfort.
- **Last update:** 2026-07-15

## Done
- 5 research reports + PATCH-TARGETS.md (real-DLL audit: 60 ✅ / 8 corrections / 0 missing; mangled-names risk retired)
- `PROJECT.md`, `ARCHITECTURE.md`, `ROADMAP.md`
- P0 skeleton (sln, net472, publicized refs, preloader+plugin stubs) — merged
- P0b asset pipeline (unity companion project, HARVESTING.md, HANDS.md: SteamVR gloves BSD-3 OK, no 2021.3 donor set exists, natives prebuilt in needle-mirror package) — merged
- P1 XR bootstrap (fetch-natives.sh SHA256-pinned, provisional RuntimeDeps compiled from needle-mirror source with zero exclusions, preloader install, OpenXR init + failover, camera takeover via CameraController.LateUpdate+RefreshFocusPosition prefix-skips, diorama rig) — merged, M1 code-complete
- P2 hands & primitives (VRHands/FingerCurler via InputDevices, IPokeable/IGrabbable/PokeInteractor/RayInteractor/ProximityGrabber/PalmGate, VREvents bus, VRModeStateMachine, VirtualMouse bridge, dev harness F8/F9/F10) — merged, API frozen in docs/INTERFACES-P2.md

## In flight (parallel workers)
- `feat/board` (P3a): picking patch, click commit, AoE stick rotation
- `feat/cards` (P3b): palm fan, play tray, rests, half selection
- `feat/world-ui` (P3c): button cluster, canvas conversion, actor bars, flat screen
- `feat/comfort` (P4): world grab/rotate/scale, snap turn, recenter (settings panel deferred to post-P3c)

## Next
1. Merge P3a/P3b/P3c/P4 (watch: Cards/Board/WorldUI stubs untouched rule; settings panel follow-up)
2. P5 integration + human hardware steps: Unity editor harvest (unity/HARVESTING.md), SteamVR gloves import (unity/HANDS.md), Windows+Quest3 runtime validation (docs/TESTING-P1/P2/P3A/P3B/P3C/P4.md)

## Standing decisions
- BepInEx 5.4.23.5, HarmonyX, net472, publicized refs
- OpenXR 1.10.0 + XR Management 4.5.0 (harvested from dummy 2021.3.5f1 build), MultiPass first
- Never patch ScenarioRuleLibrary/Bolt; commit through UI seams only (s_Callback, Proxy* card APIs, OnClickInternal)
- Mod license GPL-3.0 (LCVR/RepoXR pattern reuse)
