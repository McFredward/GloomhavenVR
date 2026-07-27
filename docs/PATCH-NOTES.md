# Harmony patch notes (hand-maintained)

> Companion to `docs/PATCH-INVENTORY.md`, which is **generated** from source by
> `scripts/patch-inventory.sh` and therefore can only state facts a parser can
> derive: which classes declare patches, what they target, and which module
> registers them.
>
> This file holds the two things the generator cannot derive and which were
> written by hand during Phase 5. They were carried over verbatim when the
> inventory became generated, because losing them would have been a real loss —
> the generator answers *what is patched*, this file answers *why, and what it
> costs another subsystem*.
>
> **It is not checked against the source.** Treat it as prose, not as an
> authority for "is this a patch target?" — that question is answered by the
> generated inventory (`CHARTER.md` §5).

## Effect / gate prose for the Phase-5 patch set

Audited 2026-07 on `feat/integration`. This covered the 17 patches that existed
then; the repository now declares 56 patch methods across 34 classes, so this
table is a **subset** — the rows below are still accurate, the ones missing were
simply never written. `InputManager` is touched by two modules, but on
**different methods** (Board: `get_CursorPosition`; WorldUI: gamepad-mode
setters) — no interaction.

| # | Patched method | Module | Type | Effect / gate |
|---|---|---|---|---|
| 1 | `Choreographer.ProcessMessage(CMessageData)` | Core (Events) | postfix | observe → `VREvents.ChoreographerMessage`; never alters game state |
| 2 | `Choreographer.SetChoreographerState(...)` | Core (Events) | postfix | observe → `VREvents.ChoreographerStateChanged` |
| 3 | `UIManager.ToggleLockUI(bool)` | Core (Events) | postfix | observe → `VREvents.UiLockChanged` |
| 4 | `CameraController.LateUpdate()` | Rig | prefix-skip | skip while `VRSession.IsRunning` (rig owns the camera); vanilla otherwise |
| 5 | `CameraController.RefreshFocusPosition(float?)` | Rig | prefix-skip | same gate as #4 (scripted camera moves bypass LateUpdate) |
| 6 | `CardsHandManager.Show(CPlayerActor, CardHandMode, …13 args)` | Cards | postfix | arm hand suppression + raise `VREvents.HandShown(player, mode)` |
| 7 | `CardsHandManager.Show(CardHandMode, …7 args)` | Cards | postfix | as #6 with `player = null` (all-hands overload) |
| 8 | `CardsHandManager.ShowHands()` (private) | Cards | postfix | as #6 — the single visual choke point (SwitchHand/coroutine/Update paths) |
| 9 | `CardsHandUI.OnDestroy()` | Cards | prefix (observe) | restore adopted card faces before pool recycle |
| 10 | `CardsHandUI.DestroyCardUI(CAbilityCard)` | Cards | prefix (observe) | single-card face restore before recycle |
| 11 | `MF.FindInteractableAtMousePosition(bool, LayerMask)` | Board | prefix (conditional replace) | substitute VR pick ray; **returns true (vanilla) whenever `BoardPick` is inactive** |
| 12 | `InputManager.get_CursorPosition` | Board | prefix (conditional replace) | project VR pick to screen cursor; vanilla when `BoardPick` inactive |
| 13 | `Controller.CommonLoop(bool)` (private) | Board | postfix | OR the VR click into the game's click flags (verbatim double-click bookkeeping); no-op without a pending VR click |
| 14 | `WorldspaceDisplayPanelBase.TrackCharacter()` | WorldUI | prefix-skip | skip only for bars ADOPTED by `ActorBars`; vanilla for all others |
| 15 | `WorldspaceDisplayPanelBase.LateUpdate()` (private) | WorldUI | prefix-skip | same ownership gate as #14 |
| 16 | `InputManager.SetGamepadInputDevice(bool)` | WorldUI | prefix (conditional block) | swallow switches TO gamepad while `InputModeGuard.Active`; mouse switches always pass |
| 17 | `InputManager.AssignGamepadBindingsToPlayerActions(bool)` (private) | WorldUI | prefix (conditional block) | belt-and-braces on the only `isUseGamepadInPc = true` writer |

## Cross-module contention rules (final, audited)

| Resource | Owner(s) | Rule |
|---|---|---|
| **Thumbstick** | `AoeControl` (Board) in `BoardTargeting`; `SnapTurn` (Rig) elsewhere | SnapTurn is hard-disabled in `BoardTargeting` (AoE rotation), `ModalUI` (reserved for future scroll) and `Menu2D`, and re-arms on exit so no stale flick fires. AoE reads only the **primary** hand's stick, only in `BoardTargeting`. Flat screen uses no stick. No frame has two stick consumers. |
| **Grip** | `ProximityGrabber` (per hand) > `WorldGrab` (Rig) | Object grabs win: WorldGrab only engages a hand whose grabber neither `Held` nor `Highlighted` at grip-down, and ignores that grip until physical release. WorldGrab additionally off in `ModalUI` and (P5, because the menu rig gives Menu2D a rig root) in `Menu2D`. |
| **Trigger** | `BoardClickDriver` (far click) vs `FlatScreen` pointer vs card grabs | Disjoint by mode/state: BoardPick is inactive in `Menu2D`/`ModalUI` (flat-screen modes); the far click is suppressed while the hand holds a grabbable or hovers poke UI (`Grabber.Held`/`Poke.HoveredUi` guards). |
| **Face buttons** | Recenter chord (Rig): **B+Y held on BOTH hands**; Settings panel chord (WorldUI): **A/X held on the NON-dominant hand** | Different buttons — no overlap. Single A/X and B/Y presses remain free. |
| **Laser/ray visuals** | One laser: **dominant hand only** in `CardSelection`/`BoardTargeting` (P5 per-hand matrix); ModalUI laser cone-gated to UI surfaces | The far pick (`BoardPick`) exclusively consumes `VRHands.PrimaryPick`, so the non-dominant laser was pure noise — now off. |
| **`Camera.main` per-frame lookups** | evaluated, left in place | Unity 2021.3 caches `Camera.main` internally (no tag scan since 2020.2). All WorldUI sites already funnel through `CanvasConversion.WorldCamera` (HeadCamera → Camera.main fallback); the remaining per-frame uses (PalmGate, CardFan, VRCard, HandsDriver sim path) are single cached-property reads — centralizing further buys nothing measurable. |
| **`FindObjectOfType` calls** | none per-frame | The one offender (`BoardModule.ResolvePluginConfig`, init-time only) was retired by the P5 config migration (`ModuleConfig.Create`). |

## Per-mode / per-hand interactor matrix (final)

See `docs/INTERFACES-P2.md` §4 — the state machine is the single source of truth;
modules must not toggle interactors directly.
