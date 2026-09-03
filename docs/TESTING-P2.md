# Phase 2 — Windows/HMD validation checklist (hands & interaction primitives)

> Prereq: Phase-1 validation passed (stereo diorama, head tracking — docs/TESTING-P1.md).
> Deploy as for P1 (`scripts/install.ps1`), Quest 3 over Link / Virtual Desktop / Steam
> Link, or any OpenXR HMD with Touch-style controllers.

## 0. Desktop smoke test first (no HMD needed)

Set in `BepInEx/config/dev.gloomhavenvr.cfg`:

```ini
[Dev]
Enabled = true
SimulateHands = true
```

1. Start the game flat, load any scenario.
2. **Overlay** (F10): shows `Mode TableIdle` in-scenario, `Menu2D` in the menu.
3. **Simulated hands** visible bobbing at the bottom of the view (procedural
   capsule hands — expect pink/magenta materials if the game stripped the Standard
   shader; that is cosmetic only).
4. Hold **T** / **G**: fingers curl (T = index, G = middle/ring/pinky). Overlay pose
   shows `Point` with G only, `Fist` with both, `OpenPalm` with none.
5. **Event bus**: end a turn, start a round — overlay lists `Msg StartTurn`,
   `Msg PlayerToSelectAbilityCardsOrLongRest` etc., and `Mode` transitions
   (`TableIdle -> CardSelection -> …`). BepInEx log shows the same via `[Dev]`.
6. **F9 (poke Ready via ExecuteEvents)**: with the Ready button visible, F9 must
   click it (turn ends / selection confirms — identical to a mouse click). With a
   modal dialog open (UI locked), F9 must log
   `raycast ... produced no hit (... modality respected)` and change nothing.
7. **Virtual mouse**: in the MAIN MENU run dev mode; the WorldUI driver is up.
   Warp/click currently has no UI trigger — validate via ScriptEngine/UnityExplorer
   REPL: `GloomhavenVR.WorldUI.VirtualMouse.Click(new Vector2(Screen.width/2f, Screen.height/2f))`
   should move the cursor to screen center and click whatever is there.

## 1. HMD checklist

Config: `[Dev] Enabled = true` recommended for the overlay on the desktop mirror.

### Hands present & articulated

- [ ] Both hands appear at controller positions **in the main menu already** and stay
      through scenario load. They hang off `VRRigDriver.RigRoot`, and the menu rig is
      built unconditionally, so menu hands are load-bearing now — they are what clicks
      the floating 2D screen. Log: `Hands built under '<parent>'`. No hands in the
      menu is a FAILURE, not an expected P2 gap.
- [ ] Hand size reads correctly against the diorama (they inherit the world scale).
- [ ] Orientation: fingers point along the controller "forward", palms face each
      other in a natural rest pose. If systematically twisted/offset, tune it live —
      see "Tuning the hand seat" below.

#### Tuning the hand seat (per hand STYLE, `dev.gloomhavenvr.hands.cfg`)

The hand VISUAL is posed from the OpenXR **grip** pose, which points up along the
controller handle — not where a relaxed hand points (hardware test #4: hands did not
match the controller pitch). The correction is a set of config values, live-tunable
while the game runs (the hands re-seat on the next frame, no restart).

The old shared `[Hands] GripPitchOffsetDegrees` + trim are **retired**. The seat is
now **per hand style** — one absolute set for each of `Glove`, `Plate`, `Arcane` —
in `BepInEx/config/dev.gloomhavenvr.hands.cfg`, under `[Hands]`:

| Key (`<Style>` = Glove / Plate / Arcane) | What it moves |
|---|---|
| `<Style>GripPitchDegrees` | pitch; **negative tilts the fingertips DOWN** |
| `<Style>GripRollDegrees` | twist around the controller's forward axis (MIRRORED between the hands) |
| `<Style>GripYawDegrees` | which way the fingers point (MIRRORED) |
| `<Style>LateralOffset` | device-space X, metres — moves BOTH hands the same way |
| `<Style>VerticalOffset` | device-space Y, metres, positive = up |
| `<Style>ForwardOffset` | device-space Z, metres, positive = toward the fingertips |
| `<Style>SpreadOffset` | how far APART the two hands sit (MIRRORED) |

Shipped values are the hardware-measured `DefaultSeat*` tables in
`src/GloomhavenVR/Hands/HandsConfig.cs` — read them there rather than from this doc.

1. Wear the style you want to tune, hold the controller like a relaxed pointing hand.
2. Adjust `<Style>GripPitchDegrees` in 10° steps until the virtual fingers extend
   where your real index finger points, then refine in 2–5° steps; then roll/yaw,
   then the three offsets, then spread. Report the final set + runtime
   (VD/Link/Steam Link) per controller type and per style.
3. Everything here is VISIBLE TO OTHER PLAYERS — the pose on the wire is the seated
   hand root — and held figures/cards hang off it, so tune it as the real pose.
4. The LASER is independent of all of it: it uses the OpenXR **aim ("pointer") pose**
   when the runtime delivers `PointerPosition`/`PointerRotation` (log:
   `<side> controller delivers the OpenXR aim pose (PointerPosition/PointerRotation)
   — laser uses it.`), and falls back to the grip-pose hand frame otherwise
   (`<side> controller lost the aim pose — laser falls back …`). If the laser
   direction feels wrong but the hands look right, report which of the two appeared.
- [ ] Trigger curls the index; grip curls middle/ring/pinky; resting the thumb on a
      button/stick curls the thumb.
- [ ] Poses: grip only → index stays straight (Point); everything released → flat
      hand (OpenPalm); grip+trigger → Fist. No finger jitter at rest.
- [ ] Tracking loss (controller behind back / covered): hand freezes, overlay shows
      NOT TRACKED, no exceptions in the log; recovers cleanly.

### Poke → real game button (ExecuteEvents path)

P2 has no world-space game canvases yet, so validate the pipeline in two halves:

- [ ] F9 on the desktop while in the HMD session: Ready button clicks (same synthetic
      pointer path the fingertip uses).
- [ ] Spawn-test (ScriptEngine/UnityExplorer): register any world-space canvas via
      `UguiPokeSurfaces.Register(canvas)` with a Button — fingertip press-through
      fires hover (haptic tick) then click (pulse) exactly once per press; retract
      and press again re-fires.
- [ ] With `UIManager.Instance.ToggleLockUI(true)` the same poke does nothing;
      unlock restores it.

### Ray

- [ ] In BoardTargeting (start a move/attack) the laser appears from the controller
      along the OpenXR aim pose (fallback: index knuckle along the hand); reticle
      dot sits on the board where it hits.
- [ ] The laser is present in EVERY mode, unconditionally (user ruling 2026-08:
      "der Laser ist ausnahmslos da"). There is no switch — `[Hands] RayAlwaysOn` and
      the ModalUI cone gate are both deleted. A mode in which the beam vanishes is a bug.
- [ ] Laser width/reticle size look sane at diorama scale (constants are
      world-scaled; report if not).
- [ ] NOTE: the ray does NOT yet drive game hover/picking — that is Phase-3a. Only
      the visual + `PickPose` (overlay `rayHit True`) are testable now.

### Grab

- [ ] Spawn-test a `GrabbableBehaviour` cube: approaching with the palm highlights
      (haptic tick), grip snaps it to the palm, releasing while moving throws it with
      believable velocity.

### Palm gate

- [ ] Turn a palm toward your face: overlay `palmGate OPEN` (dot > 0.6); turn away:
      closes below 0.35. No flicker when holding the hand near the threshold.
- [ ] Haptics: hover tick subtle, click pulse crisp, grab pulse strong — on the
      correct hand.

### Mode machine (HMD)

- [ ] Scenario load: `Menu2D -> TableIdle`. Card selection round: `-> CardSelection`.
      Turn start: `-> HalfSelection`. Choosing a move/attack: `-> BoardTargeting`.
      Modal dialog: `-> ModalUI` and back.
- [ ] The laser survives every one of those transitions — it does not blink out on
      mode changes (see the Ray section: visuals are unconditional).

### Stability / hygiene

- [ ] Scenario exit → menu → new scenario: hands tear down and rebuild; no
      duplicated hands, no null-ref spam.
- [ ] ScriptEngine F6 hot reload mid-scenario: hands/laser/overlay disappear and
      return; log shows clean module Shutdown/Init; no leaked GameObjects
      (check with UnityExplorer).
- [ ] 30 min play: no GC-spike stutter attributable to the mod (interactor loops are
      allocation-free; PokeInteractor only touches uGUI raycasts near registered
      canvases).

## 2. Questions this pass has since ANSWERED

Kept as a record so nobody re-opens them. All five were open runtime questions in the
original P2 pass; each is settled in the shipped code now.

1. **Grip-pose vs hand-frame offset** — settled and superseded. There is no shared
   offset any more: the seat is the per-style `[Hands] <Style>Grip*Degrees` /
   `<Style>*Offset` set above, shipped from hardware-measured tables. The aim pose IS
   delivered on the Quest 3 profiles the mod has been tested on; the fallback path
   still exists and names itself in the log.
2. **Thumb touch** — delivered; resting the thumb on a button/stick curls it (the
   pose checkbox in §1 is the live test).
3. **Haptic amplitudes** — the three presets are distinguishable on Touch
   controllers; hover tick / click pulse / grab pulse are the shipped set.
4. **Virtual mouse** — `VirtualMouse.Click()` registers. It is a dev/diagnostic path,
   not the shipped click path: real clicks go through uGUI `ExecuteEvents`.
5. **Mode mapping** — the card phases are covered by the card-mode messages; the mode
   machine has been through P3a/P3b/P5 since. Report a mode that feels wrong with the
   `Mode` transitions from the overlay/log attached.
