# DEMEO-HANDS-CARDS — file-level blueprint for reworking GloomhavenVR hands + card interaction to match Demeo

Research + spec, 2026-07-17. North-star = Demeo (Resolution Games, "Bowser" framework).
Sources decompiled under `decompiled-demeo/`. Our code under `src/GloomhavenVR/`.

This doc is the implementation spec for the next round. Every Demeo claim is cited to a
decompiled file + line. Every "change" is mapped to a concrete GloomhavenVR file/method.

> TL;DR of the biggest gaps: (1) Demeo **scales fan curvature by how full the hand is**
> (flat with few cards, curved when many) — we hard-code curvature. (2) Demeo **spreads
> the whole fan apart around the hovered card** (a "split" animation) and pops the
> hovered card toward the viewer — we only pop one card. (3) Demeo **grabs with the
> TRIGGER/index**, not grip — we grab with grip. (4) The fan **eases/follows the hand
> through a smoothed ViewHelper with a dead-zone**, it is not rigidly parented — ours is
> rigidly parented to `PalmCenter`. (5) Demeo's good hand *feel* is **not** position
> smoothing on controllers (there is none) — it comes from the grab/attach model; the
> predictive `HandTrackingSmoothingLayer` is a **hand-tracking-only** nicety.

---

## 0. Two interaction stacks in Demeo — which one to model

Demeo ships two stacks. Model the **classic "Prototyping" stack**, not Bowser:

- **Classic "Prototyping" stack (MODEL THIS):** `CardHandView` / `CardHandController`
  (the fan), `Grabbable` (`Prototyping/Grabbable.cs`), `VRHandInstance`,
  `VRHandTrackingModule` (`Prototyping.Internals/`), `PollTouchingObjects`,
  `XRInteractionToolkitCard`, `CardTargetPointer`. This is what actually drives the
  card-hand feel on Quest controllers. It has no hard XRI dependency for the core path
  (XRI is only the *grab-event source* on some platforms; the grab state itself lives in
  plain `Grabbable` + `VRHandTrackingModule`).
- **Bowser stack (DO NOT re-implement):** `decompiled-demeo/Bowser*Runtime/` —
  `Selectable3D`, `PointerInputHandler`, `HandAttachmentVisibilityHandler`, etc. This is
  the newer pointer/UI + VisionOS layer (clicking, wrist menus). Not the card-grab feel.
  Only relevant nugget: `BowserHandAttachmentsRuntime/Bowser/HandAttachmentVisibilityHandler.cs`
  gates wrist attachments on visibility — analogous to our reveal gate, nothing to copy.

**Dependency flag:** Demeo relies on `UnityEngine.XR.Interaction.Toolkit` (XRGrabInteractable,
IXRSelectInteractor — see `XRInteractionToolkitCard.cs:4-6`). **We do NOT bundle XRI and
should not.** Everywhere Demeo uses an XRI interactable we already have a native equivalent
(`RayInteractor`, `ProximityGrabber`, `VRCard : GrabbableBehaviour`). The blueprint below
re-implements the *behavior*, never the dependency.

---

## 1. How Demeo does HANDS

### 1.1 Representation
- Per-hand object is **`VRHandInstance : MonoBehaviour, IHandInstance`**
  (`VRHandInstance.cs`). Fields: `handedness` (HandType), a `handPivot` transform
  (`:38`), a `laserBeamAnchor` found as child `"Pointer"` (`:113`), a `LineRenderer`
  laser (`:130`), a `laserPointerHit` reticle (`:132-136`), and a `PollTouchingObjects`
  child (`:156`). The visible hand is a **skinned avatar model** attached under
  `/Avatars` (`:107-111`); the mesh itself is authored art, not procedural.
- Hand-tracking (articulated fingers) uses **`HandTrackingObject`**
  (`HandTrackingObject.cs`): `oculusHandL/R`, `trackingSpace`, per-hand position/rotation
  offsets, and explicit `thumbLeft/Right`, `indexLeft/Right` transforms plus a
  `cardHoverDistance` (`:29`) used for finger-tip proximity to cards.

### 1.2 Tracking source
- **Controllers:** device pose drives the hand transform directly (the classic path).
  Grab input is polled in `VRHandTrackingModule.Update` (`Prototyping.Internals/VRHandTrackingModule.cs:434-445`).
- **Hand-tracking:** Oculus hand bones → `HandTrackingObject` → smoothed by the layer below.
- `MotherbrainGlobalVars.IsUsingHandTracking()` switches between them throughout.

### 1.3 The smoothing layer — `HandTrackingSmoothingLayer.cs` (hand-tracking ONLY)
This is likely why Demeo *hand-tracking* feels good. **It does not run for controllers.**
Params (serialized): `smoothingFactor = 20` (`:15`), `predictionFactor = 1.5f` (`:18`),
`angleBetweenHandsLimit = 25` (`:36`).

- **Root position:** `Lerp(smoothed, raw + predictedMotion, dt * 20)` (`:53`,`:58`).
- **Root rotation:** `Lerp(smoothed, raw, dt * 20 * 2)` — rotation smoothed at **2×** the
  position rate (`:59`). (Rotation should feel snappier than position.)
- **Predictive motion (`PredictTrackedPosition` `:85-114`):** keeps the last 5 raw
  positions, averages the frame-to-frame deltas, then scales by
  `predictionFactor * (pow(1 + lastStepDistance, 4) - 1)` and **clamps magnitude to 1**
  (`:112-113`). Fast motion is extrapolated forward (hides latency); slow motion is not.
- **Per-joint (`:61-82`):** `smoothingOption 0` = snap (`localRotation = raw`),
  `option 1` = `Lerp(..., dt*20)` for position and `dt*40` for rotation.
- **Overlap special-case (`:48-51`,`:69`,`:76`):** when both hands are within **25°** of
  each other (measured from the camera) the far hand's `Proximal`/`Metacarpal` joints
  stop taking position updates — kills jitter when hands cross. Niche; only matters for
  articulated hand-tracking.

**Takeaway for us:** on controllers Demeo applies **no positional filter** — raw tracked
pose. Our `VRHand.ReadDevice` writing the device pose straight to the transform is already
correct for controllers. Do **not** add lag to controllers. Port this layer only if/when
we add Quest hand-tracking.

### 1.4 Pose / finger animation
Controller finger curl in the classic stack is driven off trigger/grip analog; articulated
fingers come straight from the tracked bones (posed by the smoothing layer). There is no
elaborate IK — the "feel" is in the grab attach, not the finger rig.

### 1.5 Grab detection — TRIGGER, over a proximity touch set (KEY)
Two-stage, in `VRHandTrackingModule`:

1. **Touch = proximity.** `PollTouchingObjects.UpdateGrabbables` does
   `Physics.OverlapSphereNonAlloc(pos, radius * lossyScale.x, ..., interactableLayers)`
   (`Prototyping.Internals/PollTouchingObjects.cs:59`). Touching set feeds
   `Grabbable.OnTouched` → `OnLocalTouchStatusChanged` (this is what the fan uses for
   hover, §2.4).
2. **Grab = TRIGGER press while touching.** Controllers:
   `Button btn = (i==0) ? Button.LeftIndex : Button.RightIndex;` and grab begins on
   `ButtonPhase.Began` (`VRHandTrackingModule.cs:434-438`); release on
   `ButtonPhase.Released` / not-Holding (`:440-444`). **`Button.LeftIndex/RightIndex` is
   the TRIGGER.** Grip is *not* the card-grab button in Demeo.
3. **Hand-tracking:** grab is `IsGrabbingLeft/Right()` (grip pose) or pinch, with a grab
   grace period (`:412-431`, `AdjustHandTrackedObjectAnchor` `:601-604`).

The grabbed object is moved every frame by an **anchor GameObject parented to the hand**
(`GrabObject` `:515-575`; `UpdateGrabbedObjectPositions` `:577-590`; `Grabbable.GrabUpdate`
`Prototyping/Grabbable.cs:353-378`). On grab the anchor is set to the grabbable's pose at
grab time (`VRHandTrackingModule.cs:555`) — a **rigid snap-to-current**, no lerp for
controllers.

### 1.6 Haptics
- Hover-change on a card → audio `MotherbrainAudio.OnCardHover(transform, hapticHand)`
  (`CardHandView.cs:426`) with a `HapticHand` derived from which hand is touching.
- PS5 adaptive triggers on card touch/pickup (`CardHandView.cs:362-373`).
- Card grabbed → `MotherbrainAudio.OnCardGrabbed` (`GrabbableActionableCard.cs:101`),
  invalid drop → `OnCardDropInvalid`, put-back → `OnCardPutBackInHand`
  (`CardHandController.cs:786-793`).

---

## 2. How Demeo does the CARD HAND

The fan is a self-contained object, **`CardHandView`** (`CardHandView.cs`), driven by
**`CardHandController`** (`CardHandController.cs`). One `CardHolder` per slot holds the
`Card`, a `cardSlot` GameObject (the target transform), and `isGrabbed/isSelected/show`
flags (`CardHandView.cs:22-33`).

### 2.1 Reveal / hide — palm-up of the OFF hand, generous, no contortion
`CardHandController.GetDisplayToShow()` (`:459-509`):
- Left hand: `Vector3.Dot(leftHand.right, avatarUpVector) > 0.6f` → show `Display.LeftHand`
  (`:474`,`:487-490`).
- Right hand: `Vector3.Dot(-rightHand.right, avatarUpVector) > 0.6f` → `Display.RightHand`
  (`:477`,`:491-494`).
- **Gated off while that hand is busy:** must NOT be holding grip/index (`flag2/flag3`
  `:475-476`), so you can't reveal with the same hand you're grabbing with.
- Hand-tracking adds grab/pinch/rock-pose gating (`:480-486`).
- **Hide:** `ShouldClose()` closes when the dot drops `< 0.6f` (`:452-456`) — same
  threshold both ways, i.e. **no hysteresis gap** in Demeo (they rely on the 0.6 cone
  being comfortable). `0.6` ≈ a **53° half-cone** off palm-up.

The fan **follows** the chosen hand: `followTransform = GetLeftHand/GetRightHand(...)`
(`CardHandView.cs:618`,`:634`), positioned via **`ViewHelper`** with `positionOffset`,
`rotationOffset`, `scale`, **`minDistanceToMove`** and **`duration`** (`:677`). `ViewHelper`
is a **smoothed, dead-zoned follow** — the fan eases toward the hand and ignores sub-
`minDistanceToMove` jitter. This is a big part of why the fan feels planted, not jittery.

### 2.2 Fan LAYOUT math — `CalculateMovableElements` (`CardHandView.cs:786-848`) (KEY)
Serialized tunables (`:69-97`): `minCardSpace`, `maxCardSpace`, `minEmptySpace`,
`maxEmptySpace`, `cardFanOffsetY`, `cardAngleZ`, `cardAngleY`, `cardSplitCurve`
(AnimationCurve), `fanSplitMultiplier`, `cardMoveSpeed`.

Let `n = inventoryData.Count`, `maxCards = piece.inventory.MaxNumberOfCards` (default 10).

```
fill      = n / maxCards                                   // :789   0..1
cardSpace = Lerp(maxCardSpace, minCardSpace, fill)         // :790   TIGHTER as hand fills
gapSpace  = Lerp(minEmptySpace, maxEmptySpace, fill)       // :791   category gaps
```
Cards are laid on a **horizontal line** (X), accumulating `cardSpace` per card plus
`gapSpace` at category boundaries (`:796-804`); total width `num4`, half-width `num5`
centers the fan (`:793-794`).

Per card `j` (`:806-814`):
```
x     = list[j]                                            // running X
t     = x / totalWidth                                     // 0..1 across the fan   :809
angZ  = Lerp(cardAngleZ, -cardAngleZ, t)                  // tilt: +z left → -z right :810
yArc  = (Sin(PI * t) - 0.5) * cardFanOffsetY              // parabolic arch, peak mid :811
slot.localPosition = (x - halfWidth,  yArc * fill,  0)     // :814
slot.localRotation = Euler(0, cardAngleY, angZ * fill)    // :814
```

**THE distinctive trait:** both `yArc` and `angZ` are multiplied by **`fill`** (`:814`).
A near-empty hand is **flat and untilted**; the arch and per-card tilt **grow as the hand
fills**. `cardAngleY` is a constant yaw that cants the whole fan toward the viewer.

### 2.3 Reveal animation — `Show` / `ChangeCards` / `TransitionOut`
- `Show(...)` picks the animation (`:473-498`): `Fan` (first open), `Flip` (switching hero),
  or `None`. `CardHandController.Show` chooses: `Fan` from closed, `Flip` when viewing a
  different piece (`CardHandController.cs:549-558`).
- **Fan-in:** with `doFanAnimation`, every card starts at the **middle slot's** pose
  (`cardHolders[n/2].cardSlot`) and lerps out to its own slot (`CardHandView.cs:577-580`).
  The lerp runs in `Tick` at `lerpTime += dt * cardMoveSpeed` (`:430-431`),
  `Slerp`/`Lerp` per card (`:444`,`:447`,`:462`).
- **Flip-out:** `TransitionOut` stacks the cards, rotates 180°, waits
  `1/cardMoveSpeed + 0.1s`, then re-fans (`:524-547`).

### 2.4 Hover highlight + the SPLIT — `Tick` (`CardHandView.cs:416-471`) (KEY)
Selection is **proximity-touch of the OTHER hand**, not gaze/laser:
`Card.Grabbable.OnLocalTouchStatusChanged` sets `selectedCardIndex` when a card is locally
touched and nothing is grabbed (`:349-360`, via §1.5 touch set).

While `anyCardSelected` (`:445-464`):
```
time  = (i - selectedCardIndex) / numCards                // :451  signed distance
split = cardSplitCurve.Evaluate(time) * fanSplitMultiplier// :452
b     = slot.localPos + right * split                     // :453  neighbors slide apart
selected card:  b2 = slot.localPos - forward * 0.25       // :454  pops toward viewer
```
- **Non-selected cards spread away from the hovered one** along their local right, amount
  shaped by `cardSplitCurve` (`:462`). This is the "fan opens up around your finger" feel.
- **Hovered card** moves `-forward * 0.25` (out toward the player) and
  `ShowAsSelected(true)` (enlarge/highlight) (`:457-458`).
- Hover-change fires a **haptic + audio tick** (`:423-428`).

When nothing is selected, cards just lerp back to their slot poses (`:445-449`).

### 2.5 Selecting / plucking a card
Touching a card with the free hand highlights it (§2.4); **grabbing** (trigger, §1.5)
plucks it. `CardHandView` does not itself carry the plucked card — see §3.

---

## 3. How Demeo GRABS & PLAYS cards

### 3.1 Grab handoff — a duplicate "action card"
When a fan card is grabbed, `CardHandController.OnGrabCard(card)` (`CardHandController.cs:611-698`):
- `cardHandView.SetGrabbedCardIndex(card.indexInHand)` hides the in-hand card
  (`CardHandView.cs:393-409`; `Tick` sets it inactive/`isGrabbed`).
- Swaps in a persistent **`actionCard` (`GrabbableActionableCard`)** that carries all the
  play logic and range checks (`:620-648`), and returns *its* `Grabbable` as the thing the
  hand actually holds (`:689`). So the fan card never leaves the fan — a proxy is flown.

### 3.2 Grab pose — where the card sits, orientation, readability
`XRInteractionToolkitCard.Grab` (`XRInteractionToolkitCard.cs:80-108`):
- **Position:** `InverseTransformPoint(interactor attach)` — grabbed **at the touch point**
  (`:83`,`:89`,`:101`).
- **Rotation (fixed):** left = `Euler(-120, 60, -160)`, right = `Euler(-120, -60, 160)`
  (`:90`,`:102`). A hard-coded readable angle — card canted up toward the face.
- Applied per frame by `AdjustHandTrackedObjectAnchor` (hand-tracking, `VRHandTrackingModule.cs:592-628`)
  or, for controllers, the anchor is snapped to the card's pose at grab and follows rigidly
  (`:555`, §1.5).
- `Grabbable.SnapToHandCenterOnGrab()` (`Prototyping/Grabbable.cs:511-514`) + hand offset
  logic (`VRHandInstance.AdjustGrabbableWithOffset` `:255-273`) re-center the card in the
  hand for non-card grabbables; cards use the `ProjectPointOnPlane` branch (`:260-263`).

### 3.3 Aim / drop onto a board tile — `CardTargetPointer` + `ActionSelect`
While held, `GrabbableActionableCard.OnGrabMove` (`GrabbableActionableCard.cs:146-198`)
each frame:
- Ranges by proximity: trash (`:161`), put-back (`:177`) — checked against transforms via
  `IsInRange` sqr-distance, ranges **putback/sell/trade = 2 m, trash = 1 m**
  (`CardHandController.cs:162-168`,`805-836`).
- Otherwise raycasts the card onto the board: `actionSelect.TryUpdateHoverCoords(targetPointer.transform.position)`
  → if a valid tile, `targetPointer.Activate()` and `SetTarget(tileWorldPos + (0,0.05,0))`
  (`:190-197`).
- **`CardTargetPointer`** (`CardTargetPointer.cs`) draws a **2-point VERTICAL line** from
  the card straight down to the target tile's Y (`:63-76`) — the drop telegraph.

Release — `OnGrabStatusChanged(grabbing=false)` (`GrabbableActionableCard.cs:104-137`):
- Valid = `CurrentHoverTileIsValid && CanUseCard() && !(inPutBack||inTrash)` (`:106-109`).
- Builds `DropCardInfo{ validDrop, targetTile, toInteractFrom }` and calls back
  (`:121-135`). `CardHandController` then routes: trash / **UseAbility** (emit
  `SerializableEventUseAbility` with `targetTile` `:718-723`) / sell / trade / put-back /
  invalid-return (`:647-687`).
- **Snap/return:** invalid or out-of-range drop → `PutBackCardInHandAfterInvalidDrop`
  (audio `OnCardDropInvalid`), fan re-shows (`:790-794`, `SetGrabbedCardIndex(-1)` `:686`).

### 3.4 Held-card fly-in
There is no soft fly-in for controllers — the anchor snaps (`:555`) and follows rigidly.
Hand-tracking applies the fixed attach pose each frame (`:601-628`).

---

## 4. Gap analysis vs GloomhavenVR

Legend: files are under `src/GloomhavenVR/`. "≈" = already close.

| # | Demeo behavior (cite) | Our current behavior (cite) | Change |
|---|---|---|---|
| G1 | Fan curvature (arch + per-card tilt) **scales by hand fill** — flat when few, curved when many (`CardHandView.cs:814`) | `CardFan.Relayout` uses fixed `radius/arc`; curvature factor `0.55` and tilt `0.85` are constants, **no fill scaling** (`Cards/CardFan.cs:~116-163`) | Multiply arch offset and per-card tilt by a `fill = n/maxHand` term (or by `n`-normalized). Add `FanFillCurve`/`FanMaxHand` to config. **HIGH impact, cheap.** |
| G2 | Hover **splits the whole fan apart** around the hovered card via `cardSplitCurve*fanSplitMultiplier`; hovered card pops `-forward*0.25` (`CardHandView.cs:451-464`) | Only the hovered card pops (`VRCard.Update` pop `0.012/0.035`, scale `+0.18`, `Cards/VRCard.cs:~515-548`); neighbors don't move (`CardFan` has no split) | Add neighbor-split to `CardFan`: offset each card along local right by `SplitCurve(signedDist)*SplitMul`. Keep our per-card pop. **HIGH impact.** |
| G3 | Grab = **TRIGGER/index** over a proximity touch set (`VRHandTrackingModule.cs:434-438`) | Grab = **GRIP** (`ProximityGrabber` uses `_hand.GripDown`, `Cards/../Interact/ProximityGrabber.cs:~80-87`) | Make the grab button configurable and default the **card** grab to trigger (laser-pluck already uses trigger via `ForceGrab(releaseOnTriggerUp:true)`, `CardsDriver.cs:347`). Reconcile so proximity-grab and laser-pluck share the trigger. **HIGH impact (muscle memory).** |
| G4 | Fan **eases to the hand** through `ViewHelper` (`minDistanceToMove` dead-zone + `duration`) (`CardHandView.cs:677`) | Fan is **rigidly parented** to `PalmCenter`, re-oriented each frame `LookRotation(away)` (`Cards/CardFan.cs:~93-109`) | Add optional smoothed follow: lerp `_root` pos/rot toward the palm target with a dead-zone, instead of hard parent, when `TrayFollow`-style config is on. **MED impact, reduces jitter.** |
| G5 | Reveal = `Dot(hand.right, avatarUp) > 0.6` (53° cone), no hysteresis, gated off the busy hand (`CardHandController.cs:474-494`) | `PalmGate` supination roll dot, enter `SupinationThreshold=0.2` / exit `-0.15` (very generous, deliberate), device-pose normal (`Interact/PalmGate.cs`, `CardsConfig.cs`, wired `CardsDriver.UpdatePalmGate:261-300`) | ≈ Keep our generous default (P6 decision), but **expose a "demeo" preset** (enter 0.6, tight cone) and add the "busy-hand gate" (don't reveal on the hand currently grabbing). **LOW-MED.** |
| G6 | Hover selection = **proximity touch of the other hand** (`CardHandView.cs:349-360`) | Hover = **laser raycast** `CardFan.TryRaycast` (geometric, sticky) (`Cards/CardFan.cs:~180-230`) | Keep laser as the controller path (it's our click model), but also honor `ProximityGrabber` highlight as a hover source so finger-near-card selects like Demeo. **LOW-MED.** |
| G7 | Held card = fixed readable Euler at the touch point, rigid follow (`XRInteractionToolkitCard.cs:90-102`) | Held card = **pinch between thumb+index tips**, `HeldFaceBias=65°`, `InspectScale=1.6`, soft fly-in (`Cards/VRCard.cs:~385-454`) | ≈ Ours is arguably nicer. Only align if it reads worse in-headset; make `HeldFaceBias`/offset match Demeo's up-cant if needed. **LOW.** |
| G8 | Board-drop telegraph = **vertical target line** to the tile (`CardTargetPointer.cs:63-76`); drop on tile plays | We select into `PlayTray` slots (Gloomhaven rules differ: pick-2), `CardsDriver.OnCardReleased:807-926`; highlight-then-drop, radius fallback | ≈ Tray is the correct GH equivalent (documented in `DEMEO-CARDS.md`). Optionally add a drop telegraph (line/ghost) to the tray slot for parity feel. **LOW.** |
| G9 | Smoothing layer for articulated hands (`HandTrackingSmoothingLayer.cs`, factor 20, prediction 1.5) | No hand-tracking path; controllers use raw pose (correct) (`Hands/VRHand.cs:340-343`) | Only relevant **if** we add Quest hand-tracking. Port the layer then. Do **not** smooth controllers. **DEFERRED.** |
| G10 | Fan-in animates all cards **out from the middle slot** (`CardHandView.cs:577-580`) | `CardFan.Relayout(instant:true)` on open; animated only on add/remove | Add a "deal from center" fan-in: on `Open`, seed each card at the middle home then lerp to slot at `CardLerpSpeed`. **LOW, polish.** |

Cross-cutting: our controller pose has **no positional smoothing** — this matches Demeo and
should stay. Do not "fix" it.

---

## 5. Concrete implementation blueprint (parallelizable, file-ownership stated)

Ordered by impact. **Phase 0 is a blocking prerequisite** (shared config file); after it,
Groups A–D own disjoint files and can run in parallel.

### Phase 0 — Config surface (BLOCKING, one worker) — owns `Cards/CardsConfig.cs`
Add tunables so A–D never touch this file again:
- `FanCurveByFill` (bool, default true), `FanMaxHandForCurve` (int, default 10),
  `FanFlatCurvatureFactor` (float, default 0.55 — current), `FanTiltFactor` (0.85 — current).
- `FanSplitMultiplier` (float, e.g. 0.02 m) and a split falloff constant / `AnimationCurve`
  substitute (`FanSplitFalloff`, since we have no serialized curve — use a coded curve).
- `FanSelectedPopForward` (float, default match current `0.035`).
- `GrabButton` (enum `Grip|Trigger`, default `Trigger` for cards).
- `FanFollowSmoothing` (float, 0 = rigid parent (current), >0 = eased follow rate),
  `FanFollowDeadzone` (m, default ~0.003).
- `RevealPreset` (`generous|demeo`) mapping to enter/exit dots.
- Keep all existing fields. Update the `CardsConfig` defaults table in code comments.

### Group A — Fan layout + hover split — owns `Cards/CardFan.cs`
1. **G1 curvature-by-fill:** in `Relayout`, compute `fill = Clamp01(n / FanMaxHandForCurve)`
   and multiply the vertical arch term and the per-card Z tilt by `fill` (mirrors
   `CardHandView.cs:814`). Gate behind `FanCurveByFill`.
2. **G2 neighbor split:** in `Relayout` (and a lightweight per-frame `Tick` pass when a card
   is hovered), offset each non-hovered card along its local right by
   `FanSplitFalloff(signedIndexDistance) * FanSplitMultiplier` (port
   `CardHandView.cs:451-462`). The hovered index comes from the existing `TryRaycast`
   sticky-hover / `_laserHover` state that `CardsDriver` already tracks — expose a
   `SetHovered(int index)` on `CardFan`.
3. **G10 deal-from-center fan-in (optional):** on `Open`, seed card homes at the middle slot,
   then `Relayout(instant:false)`.
- Depends on: Phase 0. No other group edits this file.

### Group B — Grab input + held pose — owns `Cards/../Interact/ProximityGrabber.cs` + `Cards/VRCard.cs`
1. **G3 trigger-grab:** in `ProximityGrabber`, replace the hard `_hand.GripDown`/`GripUp`
   grab edges with `CardsConfig.GrabButton` (Trigger default). Ensure the proximity path
   and the laser `ForceGrab(releaseOnTriggerUp:true)` (`CardsDriver.cs:347,432`) agree on
   the release button so a card grabbed either way releases on trigger-up.
   - Risk: trigger is also our uGUI/laser "click". Guard: only trigger-grab when a
     grabbable is `Highlighted` **and** no UI pointer is consuming the trigger this frame
     (RayInteractor already arbitrates — check `SetInteractorMask`/mode in `VRHand`).
2. **G7 held-pose tune (optional):** if in-headset review wants Demeo's cant, adjust
   `HeldFaceBias`/`HeldPinchOffset` usage in `GetHeldPose` (`VRCard.cs:~385-413`). No
   structural change.
- Depends on: Phase 0. Does not touch `CardFan` or `CardsDriver`.

### Group C — Reveal gate + fan follow — owns `Cards/../Interact/PalmGate.cs`
1. **G5 reveal preset + busy-hand gate:** apply `RevealPreset` to `EnterThreshold`/
   `ExitThreshold` (the wiring point is `CardsDriver.UpdatePalmGate:284-285`, but the
   preset→dot mapping and the "ignore if this hand is mid-grab" check belong in `PalmGate`
   so `CardsDriver` stays a thin wire). Add an `IgnoreWhenHandBusy` flag that suppresses
   `Changed` while the gate hand reports an active grab (mirrors `CardHandController.cs:475-476`).
- Depends on: Phase 0. **Note:** the *follow smoothing* (G4) is implemented in `CardFan`
  (Group A owns `CardFan.cs`), so to avoid a file conflict, **assign G4 to Group A** (it is
  a `CardFan.Tick` change) and keep Group C to `PalmGate.cs` only. Restated in the ownership
  summary below.

### Group D — Drop telegraph + hover source (optional polish) — owns the reveal/hover section of `Cards/CardsDriver.cs`
1. **G6 proximity hover source:** feed `ProximityGrabber.Highlighted` (when it is a `VRCard`
   in the fan) into `CardFan.SetHovered(...)` alongside the laser hover, so finger-near-card
   selects like Demeo. Lives in `CardsDriver.UpdatePalmGate`/tick region (not the
   `OnCardReleased` drop routing — leave that untouched).
2. **G8 tray drop telegraph (optional):** draw a short line/ghost from the held card to the
   target `PlayTray` slot while hovering (analogue of `CardTargetPointer`). Would add a small
   helper; keep it in `CardsDriver` + `PlayTray` (coordinate if Group D and a PlayTray change
   overlap — otherwise defer G8).
- Depends on: A (needs `CardFan.SetHovered`). Run D after A merges.

### File-ownership summary (no conflicts)
- **Phase 0:** `CardsConfig.cs` (blocking, first).
- **A:** `CardFan.cs` — G1, G2, G4 (follow smoothing in `Tick`), G10.
- **B:** `ProximityGrabber.cs`, `VRCard.cs` — G3, G7.
- **C:** `PalmGate.cs` — G5.
- **D (after A):** `CardsDriver.cs` (reveal/hover region only), optionally `PlayTray.cs` — G6, G8.

### Skinned-hands vs procedural
- Nothing above needs the real skinned-hands bundle. G1–G8 are card-space math + input and
  work with today's procedural hands.
- The skinned-hands path (`docs/ANLEITUNG-HAENDE.md`, SteamVR glove AssetBundle mapped by
  `HandVisuals.MapPrefabRig`) matters only for **finger realism during a pinch grab** — a
  visual nicety, not required for the feel changes. G9 (hand-tracking smoothing) is the only
  item that would additionally need articulated bones, and it is deferred.

### Dependency / risk flags
- **XRI is a Demeo-only dependency** (`XRInteractionToolkitCard.cs`). We re-implement its
  grab-event behavior natively in `ProximityGrabber`/`VRCard` (already the case). **Never
  add XRI.** Where Demeo cites `XRGrabInteractable`/`IXRSelectInteractor`, our
  `GrabbableBehaviour`/`ProximityGrabber` is the substitute.
- **Trigger overload risk (G3):** trigger drives both card-grab and uGUI click. Must
  arbitrate via existing `VRHand` interactor mask so a card-grab doesn't also fire a UI
  click. Test with `PlayTray`/`HalfSelection` poke surfaces.
- **Follow smoothing risk (G4):** over-smoothing makes the fan feel laggy — Demeo's
  `minDistanceToMove` dead-zone matters more than the lerp rate. Default `FanFollowSmoothing`
  conservative; keep rigid-parent as an option.
- **No controller position smoothing** — do not regress this. Only articulated hand-tracking
  should ever get the predictive layer.

---

## 6. Quick wins vs larger rework

### Quick wins (cheap, high impact — do first)
- **G1 curvature-by-fill** — a few lines in `CardFan.Relayout` (multiply arch + tilt by
  `fill`). Single biggest "that's the Demeo look" change.
- **G3 trigger-grab default** — swap the grab button in `ProximityGrabber`; muscle-memory
  parity with Demeo.
- **G5 reveal preset** — map a `demeo` preset to enter/exit dots; config-only + tiny
  `PalmGate` change.
- **G10 deal-from-center fan-in** — small `Open` tweak, nice reveal polish.
- **Haptic/audio hover tick on selection change** — we already have `HapticPreset.HoverTick`;
  ensure it fires on fan hover-change (mirrors `CardHandView.cs:423-428`).

### Larger rework (structural — schedule after quick wins land)
- **G2 fan split animation** — needs a hovered-index channel into `CardFan` and a per-frame
  relayout pass; the highest-fidelity feel item.
- **G4 smoothed dead-zoned follow** — replaces rigid parenting with an eased follow; touches
  the fan's core transform update, needs careful tuning to avoid lag.
- **G6 proximity hover as a first-class selection source** — unifies laser + finger hover.
- **G9 hand-tracking predictive smoothing** — only with a Quest hand-tracking path; port
  `HandTrackingSmoothingLayer` wholesale (factor 20, prediction 1.5, per-joint options).
- **G8 board/tray drop telegraph** — optional `CardTargetPointer` analogue for the tray.

---

### Appendix — key Demeo tunables to seed our config from
Demeo's numeric defaults live in prefab/inspector (not in decompiled code), so match by feel:
- Fan: `minCardSpace/maxCardSpace` (spacing tightens with fill), `cardFanOffsetY` (arch),
  `cardAngleZ` (per-card tilt extent), `cardAngleY` (whole-fan yaw toward viewer),
  `fanSplitMultiplier` + `cardSplitCurve` (hover spread), `cardMoveSpeed` (lerp) —
  `CardHandView.cs:69-97`.
- Grab attach Euler: L `(-120,60,-160)`, R `(-120,-60,160)` — `XRInteractionToolkitCard.cs:90,102`.
- Reveal dot: `0.6` — `CardHandController.cs:474`. Ranges: putback/sell/trade `2 m`,
  trash `1 m` — `CardHandController.cs:162-168`.
- Hand-tracking smoothing: `smoothingFactor 20`, `predictionFactor 1.5`, rotation `2×`,
  overlap limit `25°` — `HandTrackingSmoothingLayer.cs:15,18,59,36`.
