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
> generated inventory (`.planning/refactor/CHARTER.md` §5).
>
> **Scope warning for a newcomer:** the prose below covers the *Phase-5* patch set only. The
> patch surface has since grown by well over an order of magnitude. **Never quote a count
> from this page** — `docs/PATCH-INVENTORY.md` is the count of record and
> `scripts/patch-inventory.sh check` prints it (`patch surface: N classes, M methods, all
> registered exactly once`). At ModBuild 483 (2026-09-08) it printed **107 classes / 165
> methods** against the table of 17 below — the pass before this one read 85 / 141, which is
> both the drift rate and the reason for the rule. The newer patches are documented where they live — in a block comment above the patch class
> itself, which by project convention states the user report verbatim, the root cause and the
> rejected alternatives. `docs/PATCH-INVENTORY.md` lists all of them; `.planning/STATE.md` says
> where the project stands.

## Effect / gate prose for the Phase-5 patch set

Audited 2026-07 on `feat/integration`, and re-verified 17/17 accurate since. This
covered the 17 patches that existed then; the repository now declares many times that,
so this table is a **subset** — the rows below are still accurate, the ones missing
were simply never written. (This paragraph used to carry its own patch count, which
disagreed with the one in the scope warning above. Neither is repeated now: see
`docs/PATCH-INVENTORY.md`.)

Two GAME types are shared between modules, and both are safe for the same reason —
the modules touch **different members**:

- `InputManager` — Board patches `get_CursorPosition`, WorldUI patches the two
  gamepad-mode setters.
- `UIManager` — Core (Events) postfixes `ToggleLockUI`, Board prefixes
  `get_IsPointerOverUI`.

A shared TYPE is not a contention; a shared METHOD is. For the one method pair that
IS shared across modules, see the contention table below.

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
| **Thumbstick AXIS** | `SnapTurn` (Rig) on `[Comfort] TurnHand`; `AoeControl` (Board) on the OTHER hand; `Flight` (Rig) on `[Comfort] FlightHand` | **Turning is never suppressed by mode** (user ruling, ModBuild 138: *"Die drehung soll nie blockiert sein!"*). The `BoardTargeting` hard-disable is gone: the mode is derived from the SHARED Choreographer wait-state (a peer's pending move froze everyone's turning) and is also true where AoE rotation declines to run, so `SnapTurn` asks the consumer (`LocalTurnControl.TargetingOwnsStick`) instead. AoE rotation moved to the hand NOT bound to turning (`AoeControl.ResolveRotationHand`), so the two no longer share an axis at all. Turning is ACTIVE in `ModalUI` (test #13). Surviving suppressions: `Menu2D` (non-dev-proxy) and the menu-scroll gate (`ScrollTurnGate`). Snap turn still re-arms on release so no stale flick fires. |
| **Thumbstick CLICK** | `WorldGrab` (Rig), alone | A distinct button from the axis, so it never fights SnapTurn/AoE/Flight. Since **P8** the world grab lives here rather than on the grip, and it engages **regardless of what the hand holds** — precisely so locomotion stays available while a mini or a card is in hand (the held object is parented to the hand and rides the rig). Runs in every scenario mode incl. `ModalUI` (test #13); only `Menu2D` (non-dev-proxy) is excluded, and since ModBuild 178 that is exact — the 3D map room resolves to `TableIdle`. |
| **Grip** | `ProximityGrabber` (per hand) → figure/card grabs | Since P8 the grip is the FIGURE grab (`Board/FigureGrab/*`) and card/object grabs; it no longer moves the world, so the old "object grabs win over WorldGrab at grip-down" arbitration is gone with the binding it arbitrated. |
| **Trigger** | `BoardClickDriver` (far click) vs `FlatScreen` pointer vs card grabs | Disjoint by mode/state: BoardPick is inactive in `Menu2D`/`ModalUI` (flat-screen modes); the far click is suppressed while the hand holds a grabbable or hovers poke UI (`Grabber.Held`/`Poke.HoveredUi` guards). |
| **Face buttons** | Recenter chord (Rig): **B+Y held on BOTH hands**; the non-dominant **A/X** (WorldUI, `WorldUI/Grab/NonDominantHold.cs`) | Different buttons — no overlap. The A/X side no longer opens a settings panel (that panel is deleted): one tracker, one press, at most one action — a HOLD for `[WorldUI] ManualScreenChordSeconds` closes the top floating modal, else toggles the flat screen (the self-rescue); an unconsumed release inside 0.35 s is a TAP that toggles the game OPTIONS window. Single B/Y presses remain free. |
| **Laser/ray visuals** | One laser: **dominant hand only, in EVERY mode** (`VRModeStateMachine.InteractorsFor`) | Enforced by two unconditional rules applied last: the dominant hand always gets Ray (test #19 — desktop parity: the mouse can always point and the game gates by state), and the non-dominant hand never does, in any mode under any config (user ruling 2026-08-03: *"Nur die aktive Hand … soll einen Laser haben."*). The far pick (`BoardPick`) exclusively consumes `VRHands.PrimaryPick`, so the non-dominant laser was pure noise. The ModalUI **cone gate is deleted** with its `[Hands] ModalRayConeDegrees` key — `RayInteractor.VisualsAllowed` is unconditionally true, because the gate WAS the tutorial's "no laser at all on the playfield while the instruction box is open". |
| **`Camera.main` per-frame lookups** | evaluated, left in place | Unity 2021.3 caches `Camera.main` internally (no tag scan since 2020.2). All WorldUI sites already funnel through `CanvasConversion.WorldCamera` (HeadCamera → Camera.main fallback); the remaining per-frame uses (PalmGate, CardFan, VRCard, HandsDriver sim path) are single cached-property reads — centralizing further buys nothing measurable. |
| **`FindObjectOfType<Plugin>` (the config lookup)** | retired, and it stays retired | `BoardModule.ResolvePluginConfig` was the offender; the P5 config migration (`ModuleConfig.Create`) replaced it and both names are now zero-hit in `src/`. **Scope: this row is about that ONE call.** It is not a blanket audit of scene sweeps — `src/` currently contains **269** `FindObject(s)OfType` sites (counted 2026-09-08 at ModBuild 483; ~238 when this row was written) and nothing enforces a policy over them. `FindObjectsOfType` is a full scene sweep and is not "near-free"; measure before adding one on a per-frame path. |
| **`TakeDamagePanel` burn-hover methods** | **Cards AND WorldUI both prefix them** | The one genuinely shared PATCH TARGET across modules. `OnMouseEnterBurnOne/Two` and `OnMouseExitBurnOne/Two` each carry two prefixes: Cards' `TakeDamagePanel_BurnHover_Skip` (`Cards/Patches/DamageFlowPatches.cs`) returns **`false` unconditionally** — hovering a burn option must not open the full hand preview, and the exits are skipped symmetrically because `ResetPreviewing` on exit would still re-run `BurnAvailableCard`/`BurnDiscardedCards` as a hidden side effect — while WorldUI's `TakeDamagePanelSafety` returns its `AllowHover(...)` verdict. **Either can return false**, so neither module may assume its own prefix decides the outcome, and a change to one must be read against the other. Harmony orders unprioritised prefixes by registration; do not depend on that order. |

**Intra-module double prefixes** (same module, one game method, two patch classes) —
worth knowing for the same reason, even though no cross-module boundary is involved:

- `Choreographer.TileHandler` — `Placement_Click_Diagnostics` (prefix, Board) and
  `Choreographer_TileHandler_OwnershipGuard` (prefix, Board). The guard can return
  false; the diagnostic must never throw, and wraps its whole body in
  `Net.Desync.DispatchGuard` because `ActionProcessor` turns any exception under a
  Choreographer dispatch into "Desynchronization occurred" plus a forced session
  shutdown.
- `WorldspaceStarHexDisplay.Update` — `Placement_UpdateGate_Diagnostics` (prefix,
  Board) and `HexHoverClear` (postfix, Board), which deliberately runs AFTER the
  method's own tail.

## Also worth knowing (breadth, not depth)

Patches whose cost lands outside their own subsystem. Full list and targets:
`docs/PATCH-INVENTORY.md`.

- **`MouseWorldSurfaceCut`** (WorldUI) — postfix on `EventSystem.RaycastAll`, i.e.
  **every uGUI raycast in the process** passes through it. The broadest hook the mod
  owns.
- **`ActorBehaviour_HeldTransform_Patch`** (Board/FigureGrab) — prefixes
  `ActorBehaviour.Update` **and** `LateUpdate` for **every actor**, plus
  `SetHilighted`. It contends directly with the rest of `Board/FigureGrab/` for who
  owns a held figure's transform; read them together.
- **`TooltipWindowPatches`** (19 methods) and **`TakeDamagePanelSafety`** (17) — both
  WorldUI, and still the two largest patch classes in the repo by method count; third
  place is `MapPartyTravel.TravelDrivePatches` at 5. A change to either is a wide change.
  (Counted from `docs/PATCH-INVENTORY.md` on 2026-09-08; that file is the count of record.)
- **`MapPartyTravel.TravelDrivePatches`** (`WorldUI/MapRoom/MapTravelConfirm.cs`) —
  prefixes `PartyToken.PartyMoveTo`, i.e. the map's travel drive itself.
- **The ESC-menu set** — four suppressors/finalizers in
  `WorldUI/Patches/EscMenuInputBlock.cs` + `EscMenuShowSafety.cs`
  (`ShowUIWindowSuppressor`, `EscMenuEscapeSuppressor`, `EscMenuTransitionFinalizer`,
  `EscMenuMultiplayerCheckFinalizer`), plus `ESCMenu_OnShow_LatchGuard_Patch`. All
  resolve their targets at runtime and degrade by design.
- **`WorldUI/Patches/ConfirmationBoxRescue.cs`** — three **prefix** patches over the
  `ConfirmationBox.Show…` overloads, likewise runtime-resolved. There is no patch class
  called `ConfirmationBoxRescue`: the three are
  `ConfirmationBox_ShowGenericConfirmation_Pair_Rescue_Patch`,
  `…_Single_Rescue_Patch` and `ConfirmationBox_ShowGenericSpendConfirmation_Rescue_Patch`;
  `ConfirmationBoxRescueTargets` beside them resolves the targets and patches nothing.

## Per-mode / per-hand interactor matrix (final)

See `docs/INTERFACES-P2.md` §4 — the state machine is the single source of truth;
modules must not toggle interactors directly.

## `CharacterClickSelectsOnly` — a character click selects, and nothing more

Added 2026-08-22 on the user's request: *"Ich möchte das ein Klick auf den Character nur den
aktuell ausgewählten Character für die Handkarten ändert nicht direct das Characterinfo-Sub-Menu
öffnet, das soll wirklich nur dann passieren, wenn man auf das entsprechende Symbol (das 1. mit der
abgebildeten 'Person') in der Leiste klickt."*

| Patched method | Module | Type | Effect / gate |
|---|---|---|---|
| `NewPartyCharacterUI.OnClick()` | WorldUI | prefix | lower `autoOpenDefaultPanel` for this one call, so `OnClick` takes the game's own `classToggle.group.SetAllTogglesOff()` branch (`NewPartyCharacterUI.cs:834`) instead of `classToggle.isOn = true` (`:821`). Gated on `WorldUIConfig.ConversionActive` **and** `slot.Data != null` **and** `!MapFTUEManager.IsPlaying` **and** `!InputManager.GamePadInUse` **and** the flag being `true` already. |
| `NewPartyCharacterUI.OnClick()` | WorldUI | postfix | raise the flag again — the mod never holds it. |

**Why a scoped swap and not a write on the flag.** `autoOpenDefaultPanel` has three writers: the
field initialiser (`true`, `:252`), `NewPartyDisplayUI.EnableSelectionMode` / `DisableSelectionMode`
(`:1417` / `:1495`), and — decisively — **our own**
`WorldUI/MapRoom/GuildmasterDestinations.ReArmCharacterScreen`, which is level-triggered once per
tick for as long as the map room stands and puts the flag back to `true` on every slot that has it
off. A permanent `false` would be re-raised ~72×/s by mod code and would trip that class's own
`CHARACTER SCREEN RE-ARM STUCK` warning. So the flag is conceded and only the *read* at `:817` is
owned: the value is lowered inside the same synchronous call that reads it, after every possible
external write and before any next one. When the game (or our re-arm) calls the setter with `true`,
nothing happens — that is the steady state between clicks.

**Cross-subsystem cost — an instrument's premise was falsified, and has since been re-keyed.**
`GuildmasterDestinations.TickSheetOutcome` used to arm on `NewPartyDisplayUI.SelectedUISlot`
changing to a slot with a character and warn `CHARACTER SHEET OUTCOME … DID NOT OPEN` when the
party-assembly window was still closed some ticks later. That premise — "a slot click should open
the sheet" — is exactly what this change retires, so for a while the watcher warned on **every**
character click in the map room. **Since ModBuild 220 it arms on the rising edge of the person
icon (`classToggle`) instead** (`WorldUI/MapRoom/GuildmasterDestinations.cs`): a selection change
now arms nothing and only re-baselines the toggle latch to the toggle's CURRENT value, so a slot
that arrives with the icon already lit is not mistaken for a fresh press; and the arming tick
itself never judges, because a slot click runs `SetAllTogglesOff` → `TryHideCurrentDisplay` and the
PREVIOUS character's window can still be closing in that same frame. It measures whether the sheet
actually opened, which is the outcome rather than a precondition.

*Caveat for a log reader:* `CharacterClickSelectsOnly`'s own two suppression lines still
announce the old state — "EXPECT A KNOWN FALSE ALARM … the watcher needs re-keying onto
the person icon" and "A CHARACTER SHEET OUTCOME 'DID NOT OPEN' warning following this
line is expected". Those strings pre-date ModBuild 220 and are stale; a `DID NOT OPEN`
warning today is a real finding, not the expected false alarm they promise.

**What is deliberately unchanged.** The selection itself
(`InvokeOnCharacterSelected`, `OnClick:812`) runs first and untouched, so
`NewPartyDisplayUI.SelectedUISlot` still moves — which is what `MapRoomHand` resolves the card fan
from (never `cardsToggle` / `CardWindowSelected`), and what the merchant/temple/enhancement
`onCharacterSelectedCallback` (`NewPartyDisplayUI.cs:886`) still fires on. The empty-slot recruit
path (`OnClick:837-856`), the multiplayer assign-role button (`OnClickAssignRole`), the FTUE and the
gamepad flow are all outside the gate. Every panel the auto-open could have opened — the class/info
sheet and the battle goal — stays one click away on its own icon in the same row.

## Held figure action handover (ModBuild 501)

`Choreographer_HeldFigureAction_Patch` releases affected cosmetic holds before native
message processing reads movement origins or attack-facing positions. It follows the native
main-thread and phase guards and always allows the original message body to run. Its own
exception boundary prevents a presentation failure from entering network desync handling.
`MF_HeldFigureAnimation_Patch` covers actual available non-idle clips, including aura actors;
missing clips and idle loops leave inspection holds intact. Both are explicitly registered
in `BoardModule`. Five additional `ActorBehaviour_HeldTransform_Patch` prefixes restore
the board pose before native movement setters capture their origin. No gameplay callback
is skipped and no authoritative position or movement path is written.
