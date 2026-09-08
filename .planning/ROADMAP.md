# GloomhavenVR — Roadmap

> **HISTORICAL.** Phases P0–P5 are complete and were completed long before ModBuild 92. This
> file records how the project was built and is kept for provenance; it does **not** describe
> current work. For where the project stands, what is open and how a round is run, read
> [`STATE.md`](STATE.md) — and, for what actually happened per build, the newest-first build
> notes at the top of `src/GloomhavenVR/Net/NetProtocol.cs`. `.planning/INDEX.md` says which of
> the other files here are still live. *(Pointer added 2026-09-08, ModBuild 483.)*

> Phases derive from `.planning/ARCHITECTURE.md`. Requirements R1–R5 from `.planning/PROJECT.md`.
> Parallelization model: independent `feat/*` branches per workstream, orchestrator merges to
> `main` after review; `main` must always build.

## Dependency graph

```
P0 Skeleton ─▶ P1 VR Bootstrap (R1) ─▶ P2 Hands & Primitives (R2) ─┬─▶ P3a Board Touch (R5)
        │                                                          ├─▶ P3b Card Hand (R3)
        └────▶ P0b Asset Pipeline ──(bundles)──────────────────────┼─▶ P3c World UI (R4)
                                                                   └─▶ P4 Comfort & Settings
P3a+P3b+P3c ─▶ P5 Full-loop QA & Performance
```

P0/P0b run in parallel. P3a/P3b/P3c are **mutually independent** (separate modules, separate
patch targets) — one worker each on `feat/board`, `feat/cards`, `feat/world-ui`. They consume
the stable interfaces from P2 (`IPokeable`, `IGrabbable`, hand ray API, VR event bus).

## Phase 0 — Project skeleton `feat/skeleton`
Solution + csproj (net472, publicized game refs, GamePath override), Preload patcher stub,
plugin stub with config gate, ScriptEngine dev-loop docs, install script (BepInEx 5.4.23.5),
CI-style local build script.
**Done when:** `dotnet build` produces loadable plugin; game boots vanilla with mod installed & disabled; log line proves chainload.

## Phase 0b — Asset pipeline `feat/assets` (parallel to P0–P2)
Companion Unity 2021.3.5f1 project; harvest RuntimeDeps (XR Management 4.5.0, OpenXR 1.10.0,
CoreUtils, XRIT 2.x) + natives + UnitySubsystems manifest as one coherent set; hand models
(license-checked), card backing mesh, tray, physical button, laser; bundle build script.
**Done when:** `gloomhavenvr.bundle` loads in-game via `AssetBundle.LoadFromFile`; test cube spawnable; RuntimeDeps set documented with exact versions.

## Phase 1 — VR bootstrap: stereo + head tracking (R1) `feat/xr-bootstrap`
Preloader native/manifest install; OpenXR init with pre-flight + runtime failover; MultiPass;
`CameraController.LateUpdate` prefix-skip; VROrigin rig + TrackedPoseDriver; diorama scale;
D3D11 guard; PPv2 kill-switches.
**Done when:** scenario visible as stereo diorama on Quest 3 (Link+SteamVR tested), head-tracked, stable frametime, game still fully mouse-playable in parallel.

## Phase 2 — Hands & interaction primitives (R2) `feat/hands`
Hand rendering from bundle; FingerCurler; controller action maps (generic Touch profile);
primitives: poke (ExecuteEvents + raycaster-state respect), index ray, proximity grab,
palm gate; haptics; **VR event bus** (Choreographer.ProcessMessage postfix +
UINavigation.EventStateChanged) and the VR mode state machine skeleton; virtual-mouse bridge.
**Done when:** hands visible with articulated fingers; poke clicks a real game button (e.g. Ready); ray hovers a hex with game highlight appearing; event bus logs phase transitions.

## Phase 3a — Board touch targeting (R5) `feat/board`
Patch `MF.FindInteractableAtMousePosition` + `InputManager.CursorPosition` for touch/ray pick;
commit via existing `s_Callback` click path; AoE via `RotateAOEClockwise` on thumbstick;
door/chest/loot/actor picks; enemy stat popup trigger.
**Done when:** full move+attack turn (incl. AoE placement & rotation) playable purely by touching/pointing at the board, with undo working.

## Phase 3b — Physical card hand (R3) `feat/cards`
Suppress 2D hand in VR; palm-up fan of live-canvas 3D cards; grab/inspect; play tray (2 slots,
initiative = slot order, swap support); short/long-rest tokens; physical Ready button; half
selection by poking card halves (`OnAbilityClick`/Proxy APIs); async wrapper around spin-wait.
**Done when:** complete card-selection round + in-turn top/bottom choice + rest, done entirely with hands; initiative order provably correct in game state.

## Phase 3c — World-space UI (R4) `feat/world-ui`
Physical Ready/Undo/Skip cluster; initiative track + element board as world panels; true
world-space actor bars (replace `TrackCharacter`); confirmation dialogs world-modal; wrist HUD;
tooltips; floating 2D screen + virtual mouse for menus/merchant/level-up; force non-gamepad mode.
**Done when:** a scenario is playable start-to-finish without ever seeing screen-space UI; out-of-scenario screens usable on floating screen.

## Phase 4 — Comfort & settings `feat/comfort` (after P2, parallel to P3x)
World grab/rotate/two-hand scale; snap turn; height calibration; in-VR settings panel
(BepInEx cfg backed: bindings profile, SPI toggle, effect kill-switches, scale).
**Done when:** table repositionable Demeo-style; settings persist.

## Phase 5 — Full-loop QA & performance
Merge integration; campaign loop test (guildmaster→scenario→level-up); SPI experiment + PPv2
patches; EPOOutline/fog stereo fixes; save-compat check (known modded-save name bug);
README + install guide + demo video; v0.1 release zip (plugin + patcher + bundle + RuntimeDeps).
**Done when:** one full scenario chain played in VR without flat-screen fallback; release artifact installs clean on a fresh Gloomhaven.

## Milestones

- **M1 "In the room"** = P1 done — you stand at the Gloomhaven table in 3D.
- **M2 "Hands on"** = P2 done — you press a real in-game button with your finger.
- **M3 "Demeo moment"** = P3b done — you play your round from a fanned card hand.
- **M4 "No screens"** = P3a+P3c done — full scenario without 2D UI.
- **v0.1 release** = P5 done.

## Worker/branch assignment (execution stage)

| Branch | Scope | Depends on |
|---|---|---|
| `feat/skeleton` | P0 | — |
| `feat/assets` | P0b | — |
| `feat/xr-bootstrap` | P1 | P0 (+natives from P0b) |
| `feat/hands` | P2 | P1, P0b |
| `feat/board` | P3a | P2 interfaces (mergeable stubs OK) |
| `feat/cards` | P3b | P2 interfaces |
| `feat/world-ui` | P3c | P2 interfaces |
| `feat/comfort` | P4 | P2 |

Conflict containment: each P3x workstream owns its module folder + its own Harmony patch
classes; shared surface (`Core`, `Hands` interfaces, event bus) is frozen after P2 and only
changed via orchestrator.
