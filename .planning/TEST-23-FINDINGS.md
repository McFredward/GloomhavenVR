# Test #23 findings (pre-Demeo build) — fix plan

User tested the round-22 build. Six findings. User is WAITING for all fixes before next test.
Round 23 (Demeo card parity) merges first; these fixes branch from the post-Demeo HEAD.

## 1. Combat log gone, cannot be re-spawned via VR settings
- Log: `Combat log shown (settings)` then `Combat log hidden (settings) — released to its 2D home, will not auto-reappear`. The settings toggle worked once but the panel then vanished and the toggle won't bring it back.
- Suspects: the X-close `CombatLogUserClosed` persisted flag vs the settings toggle state get out of sync; or the combat log only exists mid-scenario / its master `CombatLog` config got cleared; or re-convert fails silently.
- Files: `WorldUI/Surfaces/CombatLogSurface.cs`, `WorldUI/SettingsPanel.cs`, `WorldUI/WorldUIConfig.cs`.

## 2. Mixed Reality does NOT remove the level skybox → no green background
- Log: `Mixed reality ON — skybox disabled ... 1 camera clear(s)` — only ONE camera keyed (the HeadCamera). The scenario sky/background is NOT coming through `RenderSettings.skybox` or a `clearFlags==Skybox` camera — it is some other source (a skybox DOME/background mesh, an environment plane, or a camera clearing SolidColor to a sky color). Needs deeper investigation of what actually renders the scenario background.
- Files: `Core/MixedReality.cs`, `Core/VRCameraPolicy.cs`, investigate decompiled scenario environment/camera setup.

## 3. Floating enemy-reveal overview — three problems
- (a) Text sticks OUT in 3D, not flat on the surface — SAME baked-tilt issue as the combat log. `EnemyRevealSurface` does not enable `Flatten2D` (the opt-in added in round 21 `CanvasConversion.Convert(flatten2D:)`).
- (b) Does not scale with zoom in/out — it floats at fixed size over the board; should scale WITH the diorama (world grab).
- (c) Should behave like the opening flat board: a "lazy" follow that auto-brings it into the field of view.
- Log: host-rect fit thrashing (627→649 px repeatedly) — pin the host rect too.
- Files: `WorldUI/Surfaces/EnemyRevealSurface.cs` (enable Flatten2D, diorama-scale, lazy follow into view, pin host rect).

## 4. Replace remaining MOD-made buttons with the in-game buttons (consistent style)
- "Lange Rast", "Kurze Rast", "Fortfahren", "Rückgängig machen" — the tray's CONFIRM/UNDO/rest `BoardButton`s are mod-drawn. Replace with the game's native widgets docked like the DecisionDock does, so everything has the familiar style.
- Files: `WorldUI/ButtonCluster.cs`, `Cards/PlayTray.cs`, reuse `WorldUI/Surfaces/DecisionDockSurface.cs` docking. (WAVE 2 — after the decision-dock rework in item 6 settles.)

## 5. Button hover tooltip — 3D-tilted + appears too high
- Hovering e.g. "Schaden erhalten" shows the game's damage tooltip. SAME 3D baked-tilt as the combat log (needs Flatten2D). AND it appears far too high — should appear NEAR the hovered button.
- This is `TakeDamagePanel.ShowDamageTooltip()` (part of the take-damage panel — tied to item 6).
- Files: `WorldUI/Surfaces/DecisionDockSurface.cs` (+ flatten + place tooltip near the docked button).

## 6. Burn-two-discarded — DEADLOCK + game exception (CRITICAL)
- Choosing "zwei abgeworfene Karten verbrennen" should show TWO card slots to place the cards; currently no confirm and no undo button appear (they seem present but invisible) → deadlock. Flat screen shows "Leider ist ein Fehler aufgetreten".
- ROOT CAUSE (Player.log): `An exception occurred within the TakeDamagePanel.BurnDiscardedCards()` → NRE:
  ```
  at TakeDamagePanel.get_IsLethalDamage()      // dereferences actorBeingAttacked.Health
  at TakeDamagePanel.ShowDamageTooltip()
  at TakeDamagePanel.ClearSelectedToggle()
  at TakeDamagePanel.BurnDiscardedCards(bool)
  ```
  `TakeDamagePanel.IsLethalDamage` (decompiled TakeDamagePanel.cs:167) reads `actorBeingAttacked.Health` — `actorBeingAttacked` (field :97) is NULL when our docked "Burn Discarded" toggle fires the game's `BurnDiscardedCards`. Our DecisionDock suppression is resetting/hiding the panel (Hide clears actorBeingAttacked) while its docked toggle still drives it → NRE → the game aborts the flow and shows its error dialog.
- FIX DIRECTION: dock the take-damage widgets WITHOUT letting the panel's own state (`actorBeingAttacked`, selection) get reset — keep it logically "open" while only its VISUALS are suppressed; provide the two discard slots + a native confirm + native undo. Verify the whole burn-two path runs the game's own commit (`GameState.Lose2DiscardCardsToAvoidAttack`) without NRE.
- Files: `WorldUI/Surfaces/DecisionDockSurface.cs`, `WorldUI/ModalFallback.cs`, the burn-two pick field (`Cards/CardsDriver.cs` drop-field region + `Cards/HalfSelection.cs`), decompiled `GH.Runtime/TakeDamagePanel.cs`.

## Dispatch plan
- Wave 1 (parallel, disjoint): A = items 1+2 (settings/MR); B = item 3 (enemy reveal); C = items 5+6 (take-damage tooltip + burn-two NRE/slots/confirm-undo — the deadlock).
- Wave 2 (after wave 1): D = item 4 (native rest/confirm/undo/continue tray buttons) — builds on C's settled decision dock.
