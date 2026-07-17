# GSD State

- **Milestone:** v0.1 (first playable VR release)
- **Position:** **Hardware iteration loop.** Test #19 done: menu both-eyes, two-character play, slots, door-hover all confirmed. Round 19 merged (5 branches): (1) laser guaranteed in EVERY mode — root cause: HalfSelection/TableIdle interactor policy had no Ray; single-target attacks stay in WaitingForCardSelection (not a TargetingState) → silent off; now level-derived suppression only while holding, reason-logged flips. (2) Initiative track: nested game Canvas sortingOrder=40 caused BOTH always-on-top rendering AND hollow GraphicRaycaster (Graphics registered with nested canvas) → CanvasConversion now neutralizes nested canvases (+30-frame sweep for pooled rows); docked-rect Info log. (3) VideoDepth 0.8→2.2 m (~2.4× screen distance), glass gap 3→6 cm; combat log head-facing yaw billboard (arc-slot pose faced table center = skew) + tray-style grab/resize/pin via shared PanelGrab (TrayGrab generalized). (4) Deliberate confirm: 0.35 s poke dwell on CONFIRM/UNDO/SET with charge visual + haptics, 0.7 s post-drop guard (accidental poke-confirms in #19 log), gold ✓ ready mirror, revoke via UIReadyToggle.ReadyUp(false) — offline ReadyButton is a one-shot party commit (no game-side revoke state). (5) Action selection on tray: x-offset root cause = game's ToggleFullCard re-anchors face rect (CardFace.Maintain now re-asserts anchors/pivot/rotation); round cards dock into tray slots; half-affordance hybrid (poke→PlayHalf, laser→Top/Bottom uGUI, per-half tint); Undo|Ready|Skip cluster docked bottom-center. Known duplicate: tray CONFIRM/UNDO + docked cluster Ready/Undo coexist. Awaiting hardware test #20. (1) screen LAYER SPLIT — screen-space-camera canvases can only render through their assigned camera (mirrors can never show them; right eye had no menu UI once video-depth replaced suspension) → UI stack retargeted to a transparent glass RT on its own quad (both eyes identical, floats in front), 3D background + mirrors behind, camera-plane video via APIOnly decode + per-tick texture copy with verified readiness ([WorldUI] ScreenLayerSplit, suspension fallback, black-video root cause: mid-play renderMode switch never re-opens the render path — now re-kicked). (2) UITextInfoPanel ("Geschlossene Tür") is a HOVER prop-info panel, not a dialog — ModalFallback treating it modal froze board picking → game's hover-leave Hide never ran → self-sustaining ModalUI lock (tests #17+#18); now passive PropInfoSurface (with UIPropInfoPanel), all 26 remaining modal IDs audited (DurabilityPanel unmapped, kept modal); HMD-worn virtual-mouse absolute priority (VD injects host mouse events → flip-war). (3) Floating "Runde 1" PhaseBanner box removed — its stuck soft lock ALSO disabled every converted panel's raycaster all scenario; round readout now a tray TMP label; initiative portrait clicks → CardsHandManager.SwitchHand (chain verified, provenance-logged); card slots 1.3×. Awaiting hardware test #19. (1) window frame/recess REMOVED (user dislike); camera-plane videos re-routed to RenderTexture and composited into BOTH eye RTs with behind-screen disparity ([WorldUI] VideoDepth 0.8 m, kill switch VideoDepthLayer) — menu UI floats in front of receded video background; intro force-suspends stereo in pre-menu scenes (both eyes guaranteed; IntroPlayer's VideoPlayer binding is scene-serialized, invisible to code). (2) Doff/don hard-lock fixed: virtual-mouse currency keep-alive was edge-triggered from FlatScreen-only warp calls — now level-triggered per-tick incl. device re-add (VirtualMouseBridge); VRPresenceWatch → SessionResumed sweep re-floats open modals in front of the head; non-dominant A/X chord force-closes the top modal via UIWindow.Escape/Hide; CanvasConversion re-fit hysteresis (CombatLog churned 2×/s). (3) Objectives density 0.6× (initiative untouched); tray always spawns head-relative then re-pins (PINNED world pose was never persisted — spawned at map bottom); scale-aware near/far clip planes (hands clipped at max zoom). Awaiting hardware test #18.
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
