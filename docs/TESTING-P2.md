# Phase 2 — Windows/HMD validation checklist (hands & interaction primitives)

> Prereq: Phase-1 validation passed (stereo diorama, head tracking — docs/TESTING-P1.md).
> Deploy as for P1 (`scripts/deploy.ps1`), Quest 3 over Link / Virtual Desktop / Steam
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

- [ ] Both hands appear at controller positions once a scenario loads (they attach to
      the Phase-1 rig; there are no hands in the main menu — expected in P2).
- [ ] Hand size reads correctly against the diorama (they inherit the world scale).
- [ ] Orientation: fingers point along the controller "forward", palms face each
      other in a natural rest pose. If systematically twisted/offset, tune it live —
      see "Tuning the hand pitch" below.

#### Tuning the hand pitch (`[Hands] GripPitchOffsetDegrees`)

The hand VISUAL is posed from the OpenXR **grip** pose, which points up along the
controller handle — not where a relaxed hand points (hardware test #4: hands did not
match the controller pitch). The correction is a config value, hot-reloadable while
the game runs:

1. Open `BepInEx/config/GloomhavenVR.cfg` → `[Hands] GripPitchOffsetDegrees`
   (default **-60**; LCVR uses an 80° down-pitch for its controller-relative ray
   origins, so -40…-80 is the expected band).
2. **Negative tilts the fingertips DOWN** relative to the grip forward; positive
   tilts them up. Save the file — the hands re-pose on the next frame (no restart).
3. Hold the controller like a relaxed pointing hand; adjust in 10° steps until the
   virtual fingers extend where your real index finger points, then refine in 2–5°
   steps. Report the final value + runtime (VD/Link/Steam Link) per controller type.
4. The LASER is independent of this: it uses the OpenXR **aim ("pointer") pose**
   when the runtime delivers `PointerPosition`/`PointerRotation` (check the log for
   `controller delivers the OpenXR aim pose`) and only falls back to the hand frame
   without it. If the laser direction feels wrong but the hands look right, report
   whether that log line appeared.
   `VRHand.VisualOffsetPosition` stays a code constant (positional, rarely wrong).
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
- [ ] `[Hands] RayAlwaysOn = true` keeps the laser in every mode.
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
      Turn start: `-> HalfSelection`. Choosing a move/attack: `-> BoardTargeting`
      (laser turns on). Modal dialog: `-> ModalUI` and back.
- [ ] Laser only in BoardTargeting/Menu2D/ModalUI unless RayAlwaysOn.

### Stability / hygiene

- [ ] Scenario exit → menu → new scenario: hands tear down and rebuild; no
      duplicated hands, no null-ref spam.
- [ ] ScriptEngine F6 hot reload mid-scenario: hands/laser/overlay disappear and
      return; log shows clean module Shutdown/Init; no leaked GameObjects
      (check with UnityExplorer).
- [ ] 30 min play: no GC-spike stutter attributable to the mod (interactor loops are
      allocation-free; PokeInteractor only touches uGUI raycasts near registered
      canvases).

## 2. Open runtime questions for this pass (report back)

1. Controller grip-pose vs hand-frame offset: is `VisualOffsetPosition = (0, -0.02, -0.06)`
   plus `[Hands] GripPitchOffsetDegrees = -60` right on Quest 3 Touch Plus (tuning
   guide in §1 above)? Note per-runtime deltas (Link vs VD vs Steam Link), and
   whether the aim pose (`PointerPosition`/`PointerRotation`) is delivered.
2. Does `CommonUsages.primaryTouch/secondaryTouch/primary2DAxisTouch` deliver on the
   Quest 3 via the generic Touch profile, or does the thumb never curl?
3. Haptic amplitudes: are the three presets distinguishable on Touch controllers?
4. Virtual mouse: does `Click()` register in the main menu (button visibly presses)?
   Does moving the physical mouse afterwards recover normal desktop control?
5. `WaitingForCardSelection` is currently NOT mapped to a mode (card-mode messages
   cover it) — watch for scenarios where BoardTargeting/CardSelection feel wrong and
   note the message/state log around them (overlay shows both).
