# Phase 3a — Windows/HMD validation checklist (board touch targeting)

> Prereq: Phase-2 validation passed (hands articulated, poke clicks Ready, ray hovers
> a hex — docs/TESTING-P2.md). Deploy as for P1/P2 (`scripts/deploy.ps1`).
>
> What Phase 3a adds: the game's entire pick/hover/click pipeline follows the VR
> hands. **Far mode** = the primary hand's index ray (laser visible in BoardTargeting
> mode, or always with `[Hands] RayAlwaysOn`). **Near mode** = an index fingertip
> within `[Board] TouchRange` (default 10 cm real) above the board takes over from
> the ray; touching a hex commits a click. AoE patterns rotate with the thumbstick.

## 0. Desktop smoke test first (no HMD needed)

Set in `BepInEx/config/dev.gloomhavenvr.cfg`:

```ini
[Dev]
Enabled = true
SimulateHands = true
[Board]
ForceFarMode = true
```

(`ForceFarMode` matters here: the simulated hands hang in front of the camera and
never reach the board, so only the far ray is exercisable flat.)

1. Load a scenario, wait for your turn. Overlay (F10) shows `Mode BoardTargeting`
   once an action wants targets.
2. The simulated primary (right) hand's ray sweeps the board as the fake hands bob —
   the game's hex hover highlight (pooled hex borders) must follow the ray hit,
   NOT the mouse. Moving the physical mouse over the board must do nothing while
   the ray is live.
3. Hold **T** (simulated trigger) while the ray rests on a reachable hex during move
   selection: the hex is selected exactly as a mouse click would (path preview /
   waypoint appears). BepInEx log: `[Board] click requested (trigger, Right)`.
4. Undo the selection with the game's Undo button (mouse is fine) — state must roll
   back exactly as after a mouse click.
5. Toggle sim hands off (F8): mouse picking/hover instantly behaves 100% vanilla
   (the patches fall through when no VR pick exists).

Known dev-mode quirk: while sim hands are up in a scenario, the game cursor is the
ray projection, so mouse camera-drag/tooltips follow the ray, not the mouse. F8 off
restores the mouse. (In real VR the game camera is rig-driven, so this is moot.)

## 1. HMD checklist

### Hover follows the hands

- [ ] In BoardTargeting mode the laser appears on the primary hand; the game's hex
      highlight + cursor hover star track the reticle across the board.
- [ ] Move the index fingertip to within ~10 cm of the board: picking switches to the
      fingertip (highlight follows the finger, not the laser). Lifting the hand past
      the threshold hands picking back to the ray.
- [ ] Either hand can near-touch (closest fingertip wins); only the primary hand
      supplies the far ray.
- [ ] Haptics: a subtle tick when the pick moves onto a new hex/actor
      (`[Board] HoverHaptics = false` silences it).
- [ ] Optional: `[Board] SnapToHexCenter = true` — hover/tooltip anchors sit on hex
      centers instead of the raw hit point.
- [ ] Hovering an enemy miniature (ray or fingertip) opens the game's enemy stat
      panel, same as mouse hover; moving off closes it. (The panel is still the
      screen-space one — world-space restyling is Phase 3c.)

### Click commit — full move+attack turn

- [ ] **Touch-click**: during move selection, touching a reachable hex with the
      fingertip selects it (click pulse haptic). Retract and touch again for the
      second/confirm click if `EnableSecondClickHexToConfirm` is on.
- [ ] **Trigger-click**: pointing the laser at a hex and pulling the trigger does the
      same from a distance.
- [ ] Play a complete turn purely by touching/pointing: select move destination →
      confirm → select attack target → confirm. Damage/XP resolve normally.
- [ ] Double-click semantics: two rapid trigger pulls / pokes on the same target
      behave like a mouse double-click (e.g. instant confirm where the game supports it).
- [ ] Clicks while it is NOT your decision point do nothing (the game's own
      turn-control gate) — no desync, no error spam.
- [ ] Nuance (expected): a VR click on empty space does not clear the current target
      the way a mouse click on nothing does; use Undo/right-flow instead.

### AoE placement & rotation

- [ ] Select a ranged AoE ability: pattern preview follows the hover as with the
      mouse. Flick the primary thumbstick right = pattern rotates 60° clockwise,
      left = counter-clockwise; one haptic tick per step; holding the stick repeats
      (~3 steps/s).
- [ ] Melee/adjacent AoE (range ≤ 1): the pattern facing follows the hovered hex
      (the game's own mouse-facing logic riding our pick) — the stick intentionally
      does nothing.
- [ ] First click places/locks the AoE, second click confirms — identical to mouse.

### Doors / chests / loot / actors

- [ ] Door: move onto/adjacent and confirm via the game's flow — door opens, room
      reveals.
- [ ] Chest/loot tile: touch-click to move onto it, loot resolves at turn end as usual.
- [ ] Clicking an enemy/ally miniature (not its hex) targets its tile — identical to
      clicking the hex.
- [ ] Character placement at scenario start: near-touch a starting hex to place your
      hero (the ray is normally off in CardSelection mode — near touch or
      `[Hands] RayAlwaysOn` covers placement).

### Undo / consistency

- [ ] After any VR-made selection, the game's Undo button rolls back exactly as it
      would after mouse input (VR clicks travel the identical
      `TileBehaviour.s_Callback` path).
- [ ] Alt-tab to the flat mirror and finish an action with the mouse mid-turn:
      no conflicts (mouse works whenever the hands are not actively picking).
- [ ] Modal dialogs (UI locked): board picking suspends (mode = ModalUI), resumes
      after the dialog closes.

## 2. Triage

| Symptom | Check |
|---|---|
| Hover ignores the hands, follows the mouse | Overlay mode — ray only runs in BoardTargeting unless `RayAlwaysOn`; hands must be tracked (`VRHands.Ready`). |
| Highlight follows ray but clicks do nothing | Turn control: is it your decision point? Log should show `[Board] click requested…`; if present but no selection, capture `Choreographer` wait state from the overlay. |
| Near mode never engages | `[Board] ForceFarMode` still true from desktop testing? `TouchRange` too small for your play scale? |
| AoE won't rotate | Only RANGED AoE rotates via stick (range > 1); melee follows hover. Stick deadzone: raise/lower `AoeFlickThreshold`. |
| Clicks land on wrong hex | Try `SnapToHexCenter = true`; if still off, note whether near or far mode and the diorama scale — fingertip lift constant may need tuning. |
| Stat panel flickers while pointing at enemies | Report — hover projection may be oscillating between eyes; note HMD + scale. |
