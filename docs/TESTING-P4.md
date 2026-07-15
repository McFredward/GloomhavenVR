# Phase 4 — Comfort & table manipulation: hardware test checklist

> Prereqs: P1/P2 checklists pass (stereo rig + tracked hands in a scenario).
> Config: `BepInEx/config/dev.gloomhavenvr.comfort.cfg` (created on first run).
> Turn on `[Comfort] DebugGizmos = true` for all of this — it shows grab state,
> scale multiplier, clamp status and chord progress bottom-left on the desktop mirror.

## 0. Desktop smoke test (no HMD — do this first)

- [ ] `[Dev] Enabled = true`, start game, F8 (sim hands on): gizmos line shows
      `rig DEV-PROXY`, both hands `free`.
- [ ] Hold **G**: gizmos flips to `TWO-HAND (rotate/scale)`, both hands `WORLD`;
      the proxy yaw/scale numbers drift with the hand sway animation.
- [ ] Release **G**: back to `idle`; `savedScaleMult` updated and written to
      `dev.gloomhavenvr.comfort.cfg`.
- [ ] **F11** logs `Dev recenter requested` (no rig — safe no-op).
- [ ] F6 hot reload (ScriptEngine): no errors, comfort stack reinstalls, config
      rebinds (log: "Comfort settings bound").

## 1. One-grip drag (in scenario, HMD)

- [ ] Grip in empty air, move hand: table follows the hand horizontally, direction
      correct (pull left → board comes left), no vertical motion with
      `VerticalDrag = false`.
- [ ] Micro-jitter while holding grip still: board does NOT swim (1.5 cm deadzone
      before the gesture goes live, smoothing after).
- [ ] Grip **near a fanned card / grabbable object**: the card grab wins, the table
      does not move (gizmos hand shows `object`, not `WORLD`).
- [ ] `VerticalDrag = true`: vertical drag works; dragging the table up past your
      head stops — eyes always keep ~10 real cm above the table plane
      (gizmos `clamp ACTIVE` blinks while you push the limit).
- [ ] Release feels clean: no post-release drift.

## 2. Two-grip rotate + scale

- [ ] Both grips in empty air, spread hands: board grows; together: shrinks.
      Feels anchored between the hands (the point between your fists stays put).
- [ ] Haptic detent (`ClickPulse`, both hands) every 25% scale step.
- [ ] Clamps: cannot shrink below 0.5× / grow above 4× of base (gizmos multiplier
      pegs at the limit).
- [ ] Swing hands around each other: board yaws around the midpoint; no pitch/roll
      ever. `RotateEnabled = false` disables only rotation, scale still works.
- [ ] Release, then re-grip: no jump on re-engage (fresh anchors).
- [ ] Restart scenario / re-enter VR: last scale multiplier restored
      (`SavedScaleMultiplier` re-applied at rig build).

## 3. Snap turn & stick contention

- [ ] Stick flick right (dominant hand): world yaws 45° about your head — your head
      does not translate; `ClickPulse` haptic on the turn hand.
- [ ] Hold the stick: exactly ONE step; re-arm only after the stick re-centers.
- [ ] `SnapTurnDegrees = 30`, `TurnHand = Left`, `TurnMode = Smooth/Off`: each behaves.
- [ ] **AoE non-contention:** start an AoE-targeting action (mode gizmo shows
      `BoardTargeting`): stick rotates the AoE pattern (P3a), NO snap turn fires
      (gizmos: `SUPPRESSED: BoardTargeting owns the stick`). Leave targeting with the
      stick still deflected: no stale turn fires on exit.
- [ ] While the turn hand is world-grabbing: stick does nothing.

## 4. Recenter & height

- [ ] Hold **B+Y on both controllers** ~1 s (chord progress % in gizmos): rig recenters
      — eyes ~0.7 m above the table focus, ~0.7 m back, facing the board; `GrabPulse`
      on both hands. Holding longer does not re-fire.
- [ ] `SeatedMode = true` (edit config or ConfigurationManager): recenter re-runs
      immediately; table now sits correctly for a chair (eyes 0.50 m above, 0.55 m back).
- [ ] `TableHeightOffset = 0.2`: table sits 20 cm lower relative to your eyes after
      the automatic re-recenter.
- [ ] Recenter after dragging/scaling the table across the room: one chord brings the
      table back to a sane spot at the current scale.
- [ ] `RecenterHoldSeconds = 0`: chord disabled.

## 5. Vignette (config-gated, default off)

- [ ] `VignetteEnabled = true`: radial dark border fades in while dragging/rotating/
      scaling and on every snap turn, fades out ~0.5 s after motion stops. Center of
      view always stays clear.
- [ ] No vignette ever appears with `VignetteEnabled = false` (default).
- [ ] `VignetteStrength` visibly scales the effect.

## 6. Regression & stability

- [ ] Card/object grabs (P2/P3b) unaffected: grabbing, holding and releasing a
      grabbable never moves the table.
- [ ] Game camera untouched: exit VR / disable mod → flat-screen camera behaves
      vanilla (P1 restoration path still intact).
- [ ] Scenario exit mid-grab (or hands lose tracking mid-grab): no errors, grab state
      resets, rig teardown clean.
- [ ] F6 hot reload with VR running: comfort stack + config rebind cleanly; no
      duplicate vignette rings / gizmo overlays.
- [ ] Frametime: no hitching while grabbing/turning (all comfort code is per-frame
      math, no allocations; config file writes only at gesture end).

## Known tuning knobs (report values that felt right)

| Constant | Where | Default |
|---|---|---|
| Drag deadzone | `WorldGrab.DragDeadzoneMeters` | 0.015 m |
| Rotate deadzone | `WorldGrab.RotateDeadzoneDegrees` | 2.5° |
| Scale deadzone | `WorldGrab.ScaleDeadzoneFraction` | 4% of grip distance |
| Drag smoothing | `WorldGrab.PositionSmoothing` | 18 /s |
| Rotate/scale smoothing | `WorldGrab.RotateScaleSmoothing` | 14 /s |
| Eye clearance clamp | `RigClamp.MinEyeAboveTableMeters` | 0.10 m |
| Snap engage / re-arm | `SnapTurn` | 0.7 / 0.3 |
