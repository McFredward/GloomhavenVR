# Phase 4 — Comfort & table manipulation: hardware test checklist

> **Historical hardware checklist.** Retained for source and diagnostic links; this is not
> the current release acceptance list. Use [STATE.md](../.planning/STATE.md) and the latest
> build notes for current behavior and outstanding headset checks.


> **Audited 2026-09-08 at ModBuild 483** (base `49ceab21`). Every `[Section] Key`, log
> marker, type, method and file path named below was grepped against the tree. Config
> keys: no doc here names a live key that has been removed, and every key described as
> DELETED really is gone. What an audit of names cannot establish is that each step's
> expected BEHAVIOUR is still current — where a step was found asserting something the
> code now forbids, it says so in place.

> Prereqs: P1/P2 checklists pass (stereo rig + tracked hands in a scenario).
> Config: `BepInEx/config/dev.gloomhavenvr.comfort.cfg` (created on first run).
> Turn on `[Comfort] DebugGizmos = true` for all of this — it shows grab state,
> scale multiplier, clamp status and chord progress bottom-left on the desktop mirror.

## 0. Desktop smoke test (no HMD — do this first)

- [ ] `[Dev] Enabled = true`, start game, F8 (sim hands on): gizmos line shows
      `rig DEV-PROXY`, both hands `free`.
- [ ] Hold **G**: gizmos flips to `TWO-HAND (rotate/scale)`, both hands `WORLD`;
      the proxy yaw/scale numbers drift with the hand sway animation.
- [ ] Release **G**: back to `idle`; the gizmo's scale reading
      `scale <s> (x<mult> of base <base>)` settles, and the multiplier is written to
      `dev.gloomhavenvr.comfort.cfg` as `[Comfort] SavedScaleMultiplier`.
      (This step used to name an on-screen token `savedScaleMult`; the overlay has never
      printed that string — `Rig/ComfortGizmos.cs:50-52` is the format above.)
- [ ] **F11** (desktop dev mode only — `[Dev] Enabled` and VR NOT running) logs
      `Dev recenter requested (F11).` and requests a recenter (no rig — safe no-op).
- [ ] F6 hot reload (ScriptEngine): no errors, comfort stack reinstalls, config
      rebinds (log: "Comfort settings bound").

## 1. One-grip drag (in scenario, HMD)

> Test #10: `[Comfort] FreeMovement` (default **true**) removes ALL positional
> clamps — the checks below marked *(legacy)* apply only with `FreeMovement = false`.

- [ ] Default (`FreeMovement = true`): grip in empty air, move hand — table follows
      in **every direction**, including straight up past your head and down below
      your feet; no clamp ever engages; recenter chord (B+Y, 1 s) returns you to the
      table edge from anywhere.
- [ ] *(legacy)* `FreeMovement = false`: table follows the hand horizontally,
      direction correct (pull left → board comes left), no vertical motion with
      `VerticalDrag = false`.
- [ ] Micro-jitter while holding grip still: board does NOT swim (1.5 cm deadzone
      before the gesture goes live, smoothing after).
- [ ] Grip **near a fanned card / grabbable object**: the card grab wins, the table
      does not move (gizmos hand shows `object`, not `WORLD`).
- [ ] *(legacy)* `FreeMovement = false`, `VerticalDrag = true`: vertical drag works;
      dragging the table up past your head stops — eyes always keep ~10 real cm
      above the table plane (gizmos `clamp ACTIVE` blinks while you push the limit).
- [ ] Release feels clean: no post-release drift.

## 2. Two-grip rotate + scale

- [ ] Both grips in empty air, spread hands: board grows; together: shrinks.
      Feels anchored between the hands (the point between your fists stays put).
- [ ] Haptic detent (`ClickPulse`, both hands) every 25% scale step.
- [ ] Scale limits: with `FreeMovement = true` (default) the effective range is at
      least 0.1×–12× of base; with `FreeMovement = false` the configured
      `ScaleMin`/`ScaleMax` apply (gizmos multiplier pegs at the limit).
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
- [ ] **AoE non-contention — INVERTED since ModBuild 138, and this step used to assert
      the opposite.** It read "stick rotates the AoE pattern (P3a), NO snap turn fires
      (gizmos: `SUPPRESSED: BoardTargeting owns the stick`)". Board state may no longer
      suppress turning at all (*"Die drehung soll nie blockiert sein!"*), and the two
      controls no longer share a stick: AoE rotation moved to the hand `[Comfort] TurnHand`
      does not use. That gizmo string does not exist; `Rig/ComfortGizmos.cs:66-70` names
      this doc's old expectation as "the next round's bug report".

      What to check now: start an AoE-targeting action (mode gizmo shows
      `BoardTargeting`). The **turn stick still turns** — that is the requirement — and the
      gizmo reads `(targeting up, nothing claims the turn stick — turning stays mine)`.
      The **other** hand's stick rotates the pattern. A reading of
      `(SUPPRESSED: a live AoE pattern claims the TURN stick — not expected, the hands
      should be split)` is a real defect: report it. Leave targeting with either stick
      still deflected: no stale turn fires on exit.
- [ ] While the turn hand is world-grabbing: stick does nothing.

## 4. Recenter & height

- [ ] Hold **B+Y on both controllers** ~1 s (chord progress % in gizmos): rig recenters
      to the ONE standing seat — eyes **0.30 m above** the table focus plane and
      **0.70 m back**, facing the board (`ComfortSettings.StandingEyeHeightMeters` /
      `StandingEyeBackMeters`); `GrabPulse` on both hands. Holding longer does not re-fire.
- There is NO seat setting to test. `[Comfort] SeatedMode` and `[Comfort]
  TableHeightOffset` are both gone (user ruling 2026-08: "durch das freie Bewegen
  braucht man das nicht mehr"). The 0.30 m is not a typo and not a regression: the
  tuned `TableHeightOffset` was -0.40 against a 0.70 preset, so folding the addend
  into the constant keeps the seat exactly where every build so far put it. How high
  you sit while PLAYING is locomotion now — flight and the world grab, below.
- [ ] Recenter after dragging/scaling the table across the room: one chord brings the
      table back to a sane spot at the current scale.
- [ ] `RecenterHoldSeconds = 0`: chord disabled.

## 5. Stick flight

`[Comfort] FlightEnabled` (default ON), `FlightHand` (default Left), `FlightDirection`
(`Head` = fly where you look, pitch included / `Hand` = along the dominant aim ray),
`FlightMaxSpeed` (apparent m/s at FULL deflection, range 0.2–3). Shipped values live in
`src/GloomhavenVR/Defaults/Defaults.Rig.cs`. Grep the log for `stick flight:`.

- [ ] Push the flight hand's thumbstick FORWARD: you fly through the scene; BACK flies
      backwards; SIDEWAYS strafes level. Partial deflection is squared — small pushes
      creep, full push is exactly `FlightMaxSpeed`.
- [ ] Speed is in APPARENT metres: zoom the diorama in and out and fly again — it must
      feel the same, not faster on a big table.
- [ ] `FlightDirection = Head` follows the HMD's forward INCLUDING pitch (look down,
      fly down); `Hand` follows the dominant aim ray, so you can fly one way and look
      another.
- [ ] With turn and flight on DIFFERENT hands (the shipped default: turn right, fly
      left) both work at once. Put them on the SAME hand: turning keeps the sideways
      axis, strafe stands down, forward/back flight still works — and the log says so
      once (`stick flight: sideways strafe OFF — …`).
- [ ] While a live AoE pattern is rotating on that hand, strafe stands down too and
      comes back after the aim (same log line, `allowed` flipping back).
- [ ] Point the beam at a scrollable menu list and push the stick: flight SUSPENDS
      (`stick flight: SUSPENDED on the <side> hand …`) and RESUMES when the beam leaves
      (`… RESUMED …`). The other hand is unaffected.
- [ ] `FlightEnabled = false`: that stick does nothing at all. Turning is untouched.
- [ ] Any time flight refuses to move you, ONE line says why:
      `stick flight: doing nothing because <reason>. (Mode …, FlightEnabled=…,
      FlightHand=…, TurnHand=…)`. Quote it in the report — do not guess.

## 5b. Vertical lift on the turn stick

`[Comfort] TurnStickVertical` (default **OFF** — its off state is the pre-feature
behaviour, and uncommanded vertical motion is the nausea risk in this feature).

- [ ] With it OFF: pushing the turn stick forward/back does nothing.
- [ ] Set it true: push the TURN stick forward to rise, back to sink, straight up and
      down at `FlightMaxSpeed`. Log: `stick vertical lift: ACTIVE on the <side> turn
      stick — …`.
- [ ] Turning still owns the sideways axis and is never blocked: a push has to be
      clearly more vertical than sideways (~56°) before it lifts, so a 45° diagonal is
      a pure turn. Verify a diagonal flick turns and does NOT lift.
- [ ] It needs turn hand ≠ flight hand. Put both on one controller: forward/back flight
      keeps that axis and the lift does nothing — log
      `stick vertical lift: STANDING DOWN on the <side> turn …`.

## 5c. Laser carry reel (pull a window toward you)

`[Comfort] LaserCarryReel` (default **ON**) and `LaserCarryReelSpeed` (apparent m/s at
full deflection, range 0.25–6).

- [ ] Grab a floating window at a distance with the laser (trigger on its grab bar):
      log `<name> grab: LASER-CARRY armed (<side>, …)`.
- [ ] While holding it, pull THAT hand's thumbstick BACK → the window comes toward you;
      push FORWARD → it goes away. (Back = toward you is the corrected mapping, ModBuild
      231 — the first hardware round reported the opposite.)
- [ ] It comes all the way to just in front of your hand — close enough to then simply
      grab it — and stops there rather than being driven into your face. It cannot be
      pushed past the reach of the laser holding it.
- [ ] Turning is never affected (turning reads sideways, the reel reads up/down).
- [ ] While the reel has the stick, that hand's flight AND its `TurnStickVertical` lift
      stand down, and the log NAMES the reel as the reason
      (`… a LASER CARRY owns it — …`). Release the window → both come back, logged
      (`<name> grab: LASER-CARRY reel closed (<side>, <why>) — …`).
- [ ] `LaserCarryReel = false`: the stick keeps doing whatever it did before, and a
      laser-held window stays at the distance you grabbed it.

## 5d. Keep your place across a tracking-origin change

`[Comfort] KeepPlaceOnReorigin` (default **ON**).

- [ ] Stand somewhere deliberate at the table, note exactly where and which way you
      face. Take the headset OFF and put it back ON (the usual cause of a runtime
      origin shift). You must end up at the SAME spot with the SAME facing.
- [ ] Nothing world-anchored moved: the board, panels and tray are where they were —
      it is the rig that was shifted back, not the world.
- [ ] A tracking BLIP must not trigger it: brief occlusion / a quick controller loss
      leaves you where you are (the detector waits a few frames to be sure).
- [ ] `KeepPlaceOnReorigin = false`: the runtime's origin wins again (old behaviour) —
      useful to confirm the feature is what you were seeing.

## 6. Regression & stability

- [ ] Card/object grabs (P2/P3b) unaffected: grabbing, holding and releasing a
      grabbable never moves the table.
- [ ] Game camera untouched: exit VR / disable mod → flat-screen camera behaves
      vanilla (P1 restoration path still intact).
- [ ] Scenario exit mid-grab (or hands lose tracking mid-grab): no errors, grab state
      resets, rig teardown clean.
- [ ] F6 hot reload with VR running: comfort stack + config rebind cleanly; no
      duplicate gizmo overlays, no stuck flight/reel state.
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
