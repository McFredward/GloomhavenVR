# The held prop's flash ("Aufleuchten") — four rounds, and the window that never contained it

**Status (2026-09-06, user ruling): the animation is now SUPPRESSED in the hand rather than
fixed.** Four rounds tried to make a chest's and a trap's attention-flash play correctly while the
player holds the prop. All four failed, and this document exists so a fifth round does not repeat
them.

> "Das Aufleuchten der Truhen und Fallen funktioniert immer noch nicht richtig in der Hand.
> Dokumentier was du gemacht hast, ich will erstmal, um es einfach zu halten, diese Animation gar
> nicht mehr in der Hand haben stattdessen."

That is a decision, not a bug report: simplicity over a correct animation, chosen explicitly. The
suppression shipped in `src/GloomhavenVR/Board/FigureGrab/PropAnimBelt.cs` and is described in
[§6](#6-what-shipped-instead-the-suppression).

---

## START AT §12 — ROUND SEVEN (2026-09-06) FOUND THE PAINTER IS **US**

§11's occlusion strand read its own WORKING shape on the ModBuild 450 log for the trap and the user
reported the defect unchanged, so it is not the painter either. Round seven stopped looking for a
foreign painter and read the symptom: *"werden manchmal **weiß**"* is a **state**, not a movement,
and every reading in §§3–11 measures whether something MOVED. **`Animator.enabled = false` does not
undo a clip; it stops the clip where it is** — so the ModBuild 445 hush latches whatever the ~5 s
attention loop happened to be showing on the frame of the grab, for the whole hold.
Read [§12](#12-round-seven--2026-09-06-against-the-modbuild-450-log-the-painter-is-our-own-freeze)
first.

§11 (below) is still the correct account of the occlusion strand and of the four candidates a later
round must not re-open (§11.6); only its "the painter is a camera" verdict is superseded.

---

## READ THIS BEFORE THE HEADLINE BELOW - ROUND FIVE RETIRED IT (2026-09-05, ModBuild 447)

**The headline in the next section is a reading of ModBuild 435/436 and it is no longer true.** It
says the prop's animator was idle in BOTH windows. On ModBuild 447 the same instrument, same
anchor, says the opposite for the trap:

```
'Trap_BearTrap_PR'  clipRate hand=n/a/s home=0.202/s
                    anyLayerRate home=0.201/s
                    advancing hand=0/655  home=359/360
                    layerCount hand=0     home=1
```

Read it in order. The prop runs a **continuous, looping ~5 s clip while it stands on its hex** -
359 of 360 home frames advancing - and in the hand it is **stopped dead**, which is the ModBuild
445 hush doing exactly what it was written to do. (`hand=n/a` is not a missing measurement: a
disabled `Animator` reports `layerCount 0`, so there is no layer to read a rate off.)

So the record now reads: **the animator strand WORKS, and the shimmer is not the animator.** The
rest of sections 3-5 is still the correct account of how four rounds got there; only the "nothing
was animating in either window" verdict is superseded. Section 10 is round five.

---

## THE HEADLINE, AND IT IS THE WHOLE POINT OF THIS FILE

**FOUR ROUNDS HAVE MEASURED A WINDOW THAT NEVER CONTAINED THE FLASH.**

The instrument that four rounds built and refined — `PropAnimWatch` — samples the chest's
`PR_Chest_01_PR` animator, its layers, and its materials' property table, in the hand and again on
the hex. Its most recent hardware verdict, from both machines in a two-player session, is
unambiguous: **nothing was animating in EITHER window.**

```
clipRate      hand=0.000/s   home=0.000/s
anyLayerRate  hand=0.000/s   home=0.000/s      (layerCount 1, all layers sampled)
advancing     hand=0/665     home=0/360
stateChanges  hand=0         home=0
MATERIAL PROPERTIES: NOT ONE of the 40 tracked property slot(s) changed value in EITHER window
MATERIAL FLASH TERM (SpawnObjectAnimateMaterial_SMB): NONE on this animator's controller
```

A window in which the animation is not running **at home either** cannot answer a question about
how the animation runs in the hand. The measurement is flawless and it is about nothing.

**Therefore: the driver of the flash the user sees is NOT the chest's `PR_Chest_01_PR` animator,
and it is not any property of the two materials that animator's renderers use.** It is still
unidentified. Section 5 lists what is left and what distinguishes each candidate. Do not re-measure
this animator.

---

## 1. What the user actually reported, across the rounds

| date | verbatim | what it named |
|---|---|---|
| ModBuild 339-340 | *"Das Item flackert in der Hand."* / *"nur ganz kurz für einen Frame sichtbar"* | a **flicker** — the prop going dark, a different defect |
| 2026-09-03 | *"Die Truhe spielt so eine 'aufblitz-animation' ab, damit der Spieler sie besser sieht. […] Aktuell ist die Animation dort etwas kaputt - deutlich langsamer und kommt mir auch nicht so flüssig vor."* | the **flash**, too slow |
| 2026-09-05 | *"Wenn man das Prop in der Hand hat, ist diese Animation viel langsamer und ploppt auch manchmal mitten drin einfach weg"* | the flash: a **rate** that fell, and a play that **stops part way** |
| 2026-09-06 | *"funktioniert immer noch nicht richtig […] diese Animation gar nicht mehr in der Hand haben stattdessen"* | give up on it; remove it from the hand |

**The flicker and the flash are two different defects and were repeatedly folded together.** The
flicker (ModBuilds 335-349) is fixed. The flash (2026-09-03 onward) is not, and the flicker work is
listed below only so nobody mistakes it for flash work.

---

## 2. Round 0 — ModBuilds 335-349: the FLICKER, and it is solved

This is not the flash. It is here because the round numbering otherwise looks like it starts in the
middle, and because a previous reconstruction of this history got it wrong in a way worth naming.

**What a prior recon claimed, and what is actually true:** the recon said ModBuilds 335-341 "blamed
ghost clones / particles". They did not. Checked against `git log`:

* **ModBuild 340** (`7f48eda1`) blamed **this mod's own `WallSegmentFade`** — a held prop cannot
  pass `IsFigureOrActorRenderer` (no `ActorBehaviour`, a plain `MeshRenderer`, and the
  `GetComponentInParent` walk is discarded by the reparent), so the wall system hid the thing in
  the player's hand. The falsified lead that round was `RoomVisibilityManager`
  (`RoomVisibilityTracker.AddProp` has an empty body).
* **ModBuild 341** (`e673b48d`) blamed **`MaterialLoaderData.LoadMaterials`**, which sets
  `Renderer.enabled = false` on instantiation (decompiled `MaterialLoaderData.cs:35`), against
  `MaterialLoaderHeal.TryFinishDirect` running on a **one-second** watchdog — a ~1 Hz dark strobe.
  The fix was a per-frame fast lane. Particles are never mentioned in that round.
* The measurement `present=4..4 renderer(s), rebuilt=0` over 560 frames is real and is emitted by
  `GrabbableProp.cs:1632-1633` (`[Props] HOLD WATCH`). It comes from the **ModBuild 356** log, not
  from 335-341, and it killed the ghost-clone and particle candidates at **ModBuild 362**. The HOLD
  WATCH instrument did not exist before 341, so 341 could not have contained the reading.

**ModBuild 349** (`19884b09`, 2026-09-02) is the one that fixed the flicker, and it is the round
whose lesson everything after it rests on: **we were the churn.** `GrabbableProp.ApplyHeldPose`
writes the held prop's `localPosition`/`localRotation`/`localScale` every frame; that sets Unity's
`Transform.hasChanged`; Apparance's `ApparanceEntity.MonitorMovement` watches that and triggers a
`MonitorBounds` refresh; a refresh **destroys and re-instantiates** every placed object under the
prop, and each new one is born with `Renderer.enabled = false`. So the mod's own per-frame pose
write was rebuilding the prop's meshes ninety times a second. The fix sets
`ApparanceEntity.MonitorMovement = false` for the length of the hold and hands it back three frames
after landing:

* `GrabbableProp.FreezeApparance()` — `GrabbableProp.cs:1852`, called from `OnGrab` at `:475`
* `GrabbableProp.ScheduleThaw()` — `:1898`, called from both landing paths at `:798` and `:844`
* `ThawDelayFrames = 3` — `:1839`

**Result: the flicker is FIXED and this freeze is intact and untouched by everything below.** It did
not touch the flash.

---

## 3. Round 1 — ModBuild 362 (`8faaca2b`, 2026-09-03): an instrument, deliberately no remedy

New file: `src/GloomhavenVR/Board/FigureGrab/PropAnimWatch.cs` (866 lines).

The commit's own words: *"I DID NOT PIN THE CAUSE, SO I SHIPPED NOTHING THAT CLAIMS TO FIX IT."*
This follows the project's standing lesson that a fix gated behind the instrument shipped to test
it never executes, so "no improvement" carries no information.

**Two windows:** HAND is the hold. HOME opens `HomeSettleFrames` frames after the release glide
lands — the same prop, on its own hex, seconds later. A pre-grab hover window was considered and
dropped: it is not guaranteed to exist, not guaranteed to contain a flash, and while it lasts the
mod's own `FigureHighlight` has parented glow clones under the very subtree the watch walks.

**What it sampled:** `normalizedTime` rate, `Animator.speed`, `cullingMode`, `updateMode`,
evaluating/stalled frame counts, state changes, restarts, the SMB's own curve `t`, a whole-scene
Chronos census, `globalClock.timeScale`, `Time.timeScale`, `lossyScale`, particle systems, and
pose stomps.

**What the ModBuild 435 verdict then killed, permanently:**

| candidate | the reading that killed it |
|---|---|
| **Chronos `AreaClock`** — the framework's only position-dependent term, and moving a chest from a hex into a palm is the largest position change it could get | `scene holds 0 AreaClock(s) and 0 Timeline(s)`. The mechanism does not exist in this game. |
| **The SMB `animator.speed` latch** — `SpawnObjectAnimateMaterial_SMB.cs:24` and `DelayedDeactivatePropAnimSMB.cs:58` both latch `animator.speed = m_GlobalClock.timeScale` once in `OnStateEnter` and never refresh; the clock is `0.25` in slow-mo | `speed hand=1 home=1`, `globalClock.timeScale hand=1 home=1`. Nothing was stranded. |
| **The mod's own pose write erasing an animated transform channel** | `poseStomps=0 of 511`. `ApplyHeldPose` is not fighting the animator. |
| **Layer 0 of the chest's animator** | its `normalizedTime` did not move on 509 of 511 hand frames **and** 359 of 360 home frames — equally still on its own hex. |

That last row is the finding, and the round did not act on it: **its own step (7) then sent the next
round at `SpawnObjectAnimateMaterial_SMB.animProperty` — a property of an SMB the same line had
just reported as `NONE on this animator's controller`.**

---

## 4. Round 2 — `595585a0` (2026-09-05, shipped as ModBuild 436): four blind spots, plus a remedy

*(The commit ships no `ModBuild` bump of its own. It reads the ModBuild 435 log and lands between
the 435 and 436 bumps, so it reached hardware as **436**. A prior recon called it "ModBuild 362",
which is round 1's number.)*

Files: `PropAnimWatch.cs` (+1034), new `PropAnimBelt.cs` (536), plus 4 lines in `GrabbableProp.cs`
and 7 in `PropGrab.cs`.

**The four blind spots it closed in the instrument:**

1. **ONE LAYER.** It read `GetCurrentAnimatorStateInfo(0)` and nothing else. An attention flash
   layered over an idle base is the ordinary way to author exactly this. Now every layer is
   sampled and `layerCount` is printed so a truncation is visible.
2. **NO PICTURE.** It measured the DRIVERS and never the RESULT. Now up to 20 float/range/colour
   properties per material are read back off the shader's own property table, per frame, in both
   windows, as the rate each one moved at.
3. **NO VISIBILITY.** It read `cullingMode` and never `Renderer.isVisible` — the other half of what
   `cullingMode` means. Now visibility, enabled-ness, object activity and the drift between
   `renderer.bounds.center` and the renderer's own transform are all sampled.
4. **A GATE ONLY A CHEST COULD OPEN.** `NotifyGrab` refused any prop with no `Animator`, so a TRAP
   with none could never arm — and the user named traps. The budget was also global, so a session
   that grabbed obstacles first spent it on props that answer nothing. Arming now needs only a
   `Renderer`, and the budget is per prop KIND.

**The remedy it shipped — `PropAnimBelt`, "the belt":** for the length of the hold, set
`Animator.cullingMode = AlwaysAnimate`, set `SkinnedMeshRenderer.updateWhenOffscreen = true`, and
re-run the game's own world-anchor feeders (`ZephyrAnim.OnEnable`, `ObjectPosToMaterial.OnEnable`)
by toggling their `enabled` flag while the prop is off its hex — all with per-object restore
ledgers, re-walked every 45 frames in case Apparance rebuilt the subtree.

Its argument was that Unity answers "should I evaluate this animator?" from CULLING, culling is
answered from renderer BOUNDS, and **a player can hold a chest and look somewhere else** — a state
the flat game could not produce, because there a prop sits on a hex under a camera that sees the
whole room.

### What the latest hardware log says about the belt — and it is the reason for this document

Anchor: `] [Props] HELD-PROP ANIMATION A/B for 'Chest' Chest`. Both machines in a two-player
session agree.

**The belt APPLIED. Its preconditions genuinely existed.**

```
cullingMode         hand=AlwaysAnimate   home=CullUpdateTransforms
updateWhenOffscreen hand=3/3             home=0/3
AT THE GRAB, BEFORE THE BELT WROTE ANYTHING: 3 skinned renderer(s) had updateWhenOffscreen=false,
1 of 3 animator(s) were NOT already AlwaysAnimate
```

**And it changed nothing, because there was nothing to change.**

```
clipRate      hand=0.000/s  home=0.000/s
anyLayerRate  hand=0.000/s  home=0.000/s
advancing     hand=0/665    home=0/360
longest run with NO layer advancing  hand=665  home=360
NOT ONE of the 40 tracked property slot(s) changed value in EITHER window
```

Two further readings from that log deserve to be recorded, because they undercut the belt's own
stated mechanism:

* `worst gap between a renderer's culling bounds centre and its own transform hand=0.237 wu
  home=0.237 wu` — **identical**. The reparent did NOT leave the bounds behind, so the
  `updateWhenOffscreen` half of the belt's argument was never load-bearing.
* `lossyScale.x hand=1 home=1` — this hold was not resized at all, unlike the 3.648x the round-1
  header assumed.

**RATE VERDICT, in the instrument's own words:** *"NO ANIMATION WAS RUNNING IN EITHER WINDOW […]
this window did not contain the thing the report is about."*

---

## 5. What is still unknown, and what would distinguish the candidates

Everything below is **unproven**. The distinguishing fact in each row is what a future round should
look for; it is what tells you which candidate you are looking at.

### 5a. Why the instrument cannot see the answer — three structural blind spots

These are properties of `PropAnimWatch`, not of the defect, and they are why "nothing moved" is not
the same as "nothing happened":

1. **A screen-space post-effect writes no material property and no animator state.** `EPOOutline`
   draws by walking a static list (`Outlinable.GetAllActiveOutlinables`) in a post pass. A verdict
   built from animator layers and a material property table is blind to it **by construction**.
2. **`MaterialPropertyBlock` writes are invisible to material read-back.** A value set through
   `Renderer.SetPropertyBlock` shows up in neither `material` nor `sharedMaterial`. `BFX_ShaderProperies`
   (decompiled `BFX_ShaderProperies.cs:8,28,36`) drives a shader property from an `AnimationCurve`
   through exactly that path. A `GetPropertyBlock` probe would be needed.
3. **`.material` instantiates a per-renderer clone.** `SpawnObjectAnimateMaterial_SMB` and
   `PosToMat` both write through `.material`, which creates a clone the instrument's
   `sharedMaterial` read-back never looks at.

### 5b. The named candidates

| # | candidate | file:line | what distinguishes it |
|---|---|---|---|
| **1** | **`EPOOutline.Outlinable` raised by the game's hex hover** — `WorldspaceStarHexDisplay.cs:3587-3595` walks `propObject.GetComponentsInChildren<Outlinable>()` and calls `WorldspaceUITools.Instance.EnableHoveredOutline(...)` (decompiled `WorldspaceUITools.cs:156-163`), which writes `OutlineParameters.Enabled = true` | `WorldspaceUITools.cs:156-163` | **THE LEADING CANDIDATE.** It is a boolean toggle, so it is an instantaneous "Aufblitzen" and not a curve — which fits a symptom that "ploppt mitten drin einfach weg" far better than a clip does. It is invisible to blind spot (1). And it has a mechanism specific to a HELD prop: the game hovers the **hex** and raises the outline on the prop **object** logically occupying it — an object this mod has meanwhile moved into the player's palm. Look for the `Hovering` layer and `OutlineParameters.Enabled`, never for a clip. |
| **2** | **`WorldspaceUITools.ActivateAllOutlines`, driven by the HIGHLIGHT key** — `WorldspaceUITools.cs:90-101` (`Update`) and `:140-153` | `WorldspaceUITools.cs:140` | Global and **input-latched**: it flips *every* registered `Outlinable` in the scene at once while the key is held. Distinguished from #1 by affecting all props simultaneously rather than the held one. A VR binding whose "released" edge never fires would leave it latched on. |
| **3** | **`OutlineWrapper.OnEnable`** — re-derives the outline colour from `UIInfoTools.GetPropOutlineColor` and re-registers into `m_AllOutlinables` | decompiled `OutlineWrapper.cs:24-47` | Fires on **every** `OnDisable`→`OnEnable` cycle, which is precisely what an Apparance rebuild or a reparent-with-deactivate produces. Distinguished by a **colour change** accompanying the flash, and by duplicate entries piling up in `m_AllOutlinables` (`AddOutlinableToList`, `WorldspaceUITools.cs:68`, does not de-duplicate). |
| **4** | **The belt fighting the outline system** — `Outlinable.UpdateVisibility` is driven by `TargetStateListener.OnVisibilityChanged`, i.e. by `Renderer.isVisible`, the exact signal the belt was manipulating through `updateWhenOffscreen` and `cullingMode` | `Outlinable.cs:231-236, 264-270` | **A remedy that could cause the symptom it was treating.** Nothing in the mod accounted for this. Distinguished by the flash getting WORSE, not better, between ModBuild 435 and 436. |
| **5** | **`IdleSMB.ChangeSpeed`** — writes `m_Animator.speed` to `1f / SceneController.Instance.GameSpeedIncreaseAmount` when `SpeedUpToggle && CanSpeedUp` | decompiled `IdleSMB.cs:23-50` | The only speed writer that is a **game setting** rather than a clock — the one candidate that can make an animation "deutlich langsamer" while `globalClock.timeScale == 1`, which is exactly the reading the A/B produced. Its `GetComponentInChildren<ActorBehaviour>()` branch always fails for a prop, so a prop always takes the reciprocal branch. **However:** the A/B measured `speed hand=1 home=1`, so it is killed for this chest unless `SpeedUpToggle` was off during that session. |
| **6** | **A `MaterialPropertyBlock` writer** (class: `BFX_ShaderProperies`, `BFX_ManualAnimationUpdate`) | `BFX_ShaderProperies.cs:8,28,36` | Invisible to the instrument (blind spot 2). Low prior on a chest — these are blood-FX components — but the *mechanism class* matters more than these particular types. |
| **7** | **`RFX4_EffectSettingVisible` / `RFX4_ColorHelper`** — the only `_EmissionColor` writers in the whole decompiled game; both tint the list `{_TintColor, _Color, _EmissionColor, _BorderColor, _ReflectColor, _RimColor, _MainColor, _CoreColor}` | `RFX4_EffectSettingVisible.cs:13`, `RFX4_ColorHelper.cs:27` | Writes **eight properties at once**, so if this is the flash the material read-back would show a *cluster* of slots moving together, never one. |
| **8** | **`TileAnimation`** — a per-frame flipbook writing `materials[0].mainTextureOffset` from `Time.time * speed`, with an optional `LookAt(Camera.main)` billboard | decompiled `ThirdParty/TileAnimation.cs:35-47` | Two hand-hostile terms: it uses **`Time.time`, not the Chronos clock**, so it is immune to every clock candidate; and its billboard targets **`Camera.main`**, which in VR is a different camera — a billboarded flash sprite would face the wrong way or vanish edge-on once the prop is in a palm. |
| **9** | **`ProjectorModifier`** — a decal/projector whose `OnEnable`/`OnDisable` flip `_meshRenderer.enabled` | decompiled `ProjectorModifier.cs:17-28` | If the "flash" is a ground **decal under** the chest rather than the chest body, this is the writer — and it does **not** move with the prop into the hand, which is its own tell. |

### 5c. What is NOT the thing being seen

* **The mod's own additive hover glow is not it.** `FigureHighlight` clones the prop's renderers and
  draws them with the bundled `GloomhavenVR/Overlay` shader, and `FigureOverlay.OverlayPulse`
  (`FigureOverlay.cs:1274-1310`) genuinely pulses at 0.7 Hz. But `GrabbableProp.OnGrab` calls
  `ClearHighlight()` at `GrabbableProp.cs:496`, one line before it parks the layers, and the
  re-application path (`TryRelightHighlight`, `:449-457`) is guarded on `_holder == null` so it
  cannot relight a held prop. *Distinguishing fact if it ever IS this: `OverlayPulse` is driven by
  `Time.unscaledTime`, so it is immune to every clock candidate, and it pulses at a steady 0.7 Hz.*
* **The mod writes no colour on a prop.** There is no `SetColor`, no `_EmissionColor` writer and no
  `MaterialPropertyBlock` writer anywhere under `src/GloomhavenVR/Board/` — the two MPB mentions in
  `GrabbableProp.cs` (`:1700`, `:1729`) are read-only diagnostics. The mod also never enables,
  disables, colours or patches a game `Outlinable` on a prop; the only `EPOOutline` code in the mod
  strips them from haunt clones.

### 5d. One precondition worth checking before anything else

`UnityGameEditorObject.Start` (decompiled `:56-65`) puts every non-tile prop on the
`"Hovering"` layer. `GrabbableProp.ParkLayers()` (`GrabbableProp.cs:2187`, called from `OnGrab` at
`:498`) then moves the held `_visual` **and every object under it that carries a Collider** to
`IgnoreRaycastLayer` for the hold, precisely to keep the game's own pick ray off a prop held
centimetres from the eye.

**So a direct hover on the held prop cannot fire.** That does *not* kill candidate #1, because the
game's hex hover reaches the prop through `ObjectCacheService.GetPropObject`, not through a raycast
at the prop's current position — and it does not touch candidate #2 at all, which walks a static
list. But it is the first thing to verify, and it is cheap.

---

## 6. What shipped instead: the suppression

`PropAnimBelt` was **inverted**. It used to exist to make a held prop keep animating; it now exists
to stop it. Its header carries the full argument; the summary is:

| | field written | restored |
|---|---|---|
| **suppressed** | `Animator.enabled = false` on every animator under the visual | yes, per object, to its exact prior value |
| **suppressed** | `EPOOutline.Outlinable.enabled = false` on every one under the visual | yes, per object |
| **NOT suppressed** | `SkinnedMeshRenderer.updateWhenOffscreen = true` (kept) | yes, per object |
| **NOT touched** | any `Animator` whose controller carries a `DelayedDeactivatePropAnimSMB` | n/a — never written |

**Why `Outlinable.enabled` and not `OutlineParameters.Enabled`.** The obvious move is to write
`false` into the same property `EnableHoveredOutline` writes `true` into. It is the wrong field
twice over: (a) `OutlineParameters.Enabled` is assigned in **nine** places in `WorldspaceUITools`
alone (`:52, :71, :76, :147, :161, :172, :187, :198, :207`), so a single write at the grab edge
would be re-stomped by the next hover and holding it down would be a per-frame write war over a
flag the game believes it owns; and (b) an `Outlinable` carries **three** independent parameter
blocks — `OutlineParameters`, `FrontParameters`, `BackParameters` (`Outlinable.cs:160-184`) — so
clearing one leaves two free to draw. The component's own `enabled` flag has neither problem: the
game **never writes it** (grep the decompiled tree — nothing), and `Outlinable.OnDisable` removes
the component from the static list the outline pass walks, whatever the three parameter blocks say.
Handing the bool back re-runs the component's own `OnEnable`, which re-registers it at whatever
`OutlineParameters.Enabled` the game has meanwhile decided on. Exact restore, no write war.

**Why one animator is deliberately left running.** `DelayedDeactivatePropAnimSMB` is not a look. Its
`OnStateUpdate` counts a delay down and then calls `DeactivateProp`, which sends
`CDeactivatePropAnim_MessageData` into `ScenarioRuleClient.MessageHandler`
(`DelayedDeactivatePropAnimSMB.cs:102-141`) — a **rules message** for a sprung trap. It also holds
itself in a static list behind `DelayedDeactivationsAreInProgress()` (`:144-151`), which the game
polls to decide whether it may proceed. Freezing that animator mid-countdown would stall a rules
message and leave a global "still in progress" true for as long as the player holds the prop — a
phase deadline waiting to be missed, produced by a rendering lane writing game state by accident.
The user asked for the blink to stop, not for the trap to stop springing.

**What was removed.** The world-anchor strand. It re-ran `ZephyrAnim.OnEnable` and
`ObjectPosToMaterial.OnEnable` on a moving held prop so a shader sweep anchored on the object's
world position would follow it into the palm. There is no longer a sweep to make look right, and it
was the riskiest write in the file. The three feeder types are still counted for the census.

**This may not be enough, and that is stated in the code.** The A/B proved the flash is not on that
animator's layers or on its materials' tracked properties, so disabling the animator may change
nothing visible. That is why the outline is suppressed too, and why the grab-edge log line
enumerates rather than asserts.

---

## 7. The next hardware round: what to grep and what it answers

```
grep -n '] \[Props\] HELD-PROP ANIMATION HUSH' Player.log
```

That line is a **census, not a verdict**. It reports, for the first time in this whole effort, what
a held prop actually carries:

* how many `Animator`s were switched off, how many were already off, and how many were left running
  for the rules exception;
* **how many `EPOOutline.Outlinable` components are under the prop** — no round has ever counted
  this, and the number alone decides candidate #1. Zero means the outline is not the flash and this
  strand made no writes. Non-zero means the leading hypothesis has a body.
* how many renderers, how many are visible, how many particle systems and how many are playing, how
  many lights;
* the three world-anchor feeders separately, `PosToMat` especially — it pushes `_ObjPosY` into a
  material **every frame** on its own, so a non-zero count there is a live per-frame material
  writer on a held prop that no round has ruled in or out;
* **and the distinct MonoBehaviour type names under the prop, with counts, and the total distinct
  count beside the list so a truncation is visible.** That histogram is where the driver's name
  will be, if it is a component at all.

Read the histogram. Do not re-measure the animator.

`] [Props] HELD-PROP ANIMATION A/B` (`PropAnimWatch`) still runs and is still useful — but only for
a hold that **visibly flashes**. Its own RATE VERDICT says so. A verdict from a hold in which
nothing flashed is not evidence about the hand.

---

## 8. Multiplayer

Nothing in any of this goes on the wire and no wire field is needed. A peer does not render a held
prop at all: the prop is in this client's hand, on this client's copy, and a peer's own game is
animating its own copy on its own hex. Every field written by the suppression is a per-client
rendering switch — whether Unity evaluates an animator, whether an outline pass walks a component,
where it thinks a skinned mesh is. None of them changes the prop's transform, the rules state, or
anything a peer could observe. The state that justifies the suppression (a prop in a hand) is itself
local, which is exactly why the correction is local too. The whole-board opt-out remains
`[Net] RemoteBoards`; there is no per-sub-feature sync setting and this needs none.

---

## 9. Corrections to the record

A prior reconstruction of this history got four things wrong. They are corrected in place above and
listed here so the errors do not propagate:

1. **ModBuilds 335-341 did not blame ghost clones or particles.** 340 blamed the mod's own
   `WallSegmentFade`; 341 blamed `MaterialLoaderData`'s one-second dark gap. See §2.
2. **The `present=4..4 / rebuilt=0` measurement is from the ModBuild 356 log**, and it killed the
   ghost-clone and particle candidates at **ModBuild 362**. The instrument that produced it did not
   exist before 341.
3. **`595585a0` is not ModBuild 362** (that is round 1, `8faaca2b`). It ships no bump of its own and
   reached hardware as **ModBuild 436**.
4. **`EnableHoveredOutline` is at `WorldspaceUITools.cs:156-163`**, not 142-171; lines 142-147 are
   `ActivateAllOutlines`'s loop body — which is a *separate* and independently interesting
   candidate (§5b #2).

One further correction, to this round's own plan: the smallest suppression was proposed as writing
`Outlinable.OutlineParameters.Enabled = false`. That field has nine game-side writers and covers
only one of three parameter blocks; `Outlinable.enabled` has zero game-side writers and covers all
three. See §6.

---

## 10. Round 5 — 2026-09-05 (against ModBuild 447): the hush fired, the shimmer stayed, and the sweep was typed too narrowly

> **(12)** "Die Spiel-Highlighting Animation von Fallen und Truhen (dieser weisse Schimmer) ist
> immer noch **auch auf dem Asset sichtbar wenn es in der Hand ist**. Das soll nicht der Fall sein."

### 10.1 What the log actually says, and it is three separate findings

Anchors: `] [Props] HELD-PROP ANIMATION HUSH` and `] [Props] HELD-PROP ANIMATION A/B`, both logs
of the ModBuild 447 two-player session (`bffe5e884`).

**(a) The hush ran, on both machines.** Host and co-player both print, for `'BearTrap' Trap`:
`1 animator switched off`, `1 EPOOutline.Outlinable under the visual, 1 switched off`. So the
suppression is not gated out, not inert on its own terms, and not asymmetric between the two
clients. It silenced what it aimed at and the defect stood — the shape this project already has
written down twice ("a fix passed its own green readings and changed nothing").

**(b) The animator strand is effective and is NOT the shimmer.** See the banner at the top of this
file: `advancing home=359/360` vs `hand=0/655`. The clip really does run on the hex and really is
stopped in the hand.

**(c) `Outlinable.enabled = false` is genuinely effective too, so the outline is not the shimmer
either.** Verified against the decompiled component rather than assumed:
`Outlinable.OnDisable` does `outlinables.Remove(this)` (the static `HashSet` the outline pass
walks, `Outlinable.cs:82, 269-272`), and `UpdateVisibility`'s first test is
`if (!enabled) { outlinables.Remove(this); return; }` (`:236-245`) — so a disabled `Outlinable`
can never be re-added by a visibility event either. Candidate #1 of §5b is dead.

### 10.2 Both instruments were truncated, in different ways, and both truncations are old news here

**`PropAnimWatch`'s material read-back tracks 20 of 57 properties.** Its own population clause:
`material(s) 1/1 on shader 'Amp_Char_Shader'; shader propert(y/ies) 57 declared, 20
float/range/colour tracked` (`MatPropCap = 20`). **ModBuild 151 already lost a build to this, on
this exact shader**: *"the census that was supposed to catch all this had a cap of 24 properties,
while `Amp_Char_Shader` declares exactly 24 interesting ones — so every dump was truncated
precisely where `_MOD_TINT` would have appeared."* Therefore **"not one of the tracked slots moved"
was never evidence that no material property moved**, in either window, in any of the four rounds.

*(A correction to §5a while we are here: blind spot (3), "a write through `.material` is invisible
to a `sharedMaterial` read-back", is **false**. `Renderer.material` stores the clone it creates
back into the renderer, so `sharedMaterial(s)` afterwards returns the clone. The real blind spot
was the property cap sitting next to it.)*

**The hush census's own histogram is `GetComponentsInChildren<MonoBehaviour>()`, and `Light`
derives from `Behaviour`, not from `MonoBehaviour`.** So the histogram is structurally incapable of
naming a light; a light appears only as the bare `N light(s)` count at the end of the line. That
count is not zero:

| prop (host, 447) | lights | particle systems | in the histogram |
|---|---|---|---|
| `'BearTrap' Trap` | **2** | 8 (0 playing at the grab) | `RFX4_LightCurves x2`, `LevelUseParticles x2`, `SFXOnEnable x2`, `ParticleSystem_OnEnable_Default x2` |
| `'GoldPile' MoneyToken` | **3** | 7 | `RFX4_LightCurves x3`, `SFXOnEnable x2` |

**This project has paid for that exact hole once already,** ModBuild 151: *"A LIVE POINT LIGHT
INSIDE THE CREATURE. 'LivingSpirit_Light (1)', Point, intensity 20.00, range 1.0 m, parented in the
prefab — a lamp centimetres from its own face. `Strip`'s sweep is
`GetComponentsInChildren<MonoBehaviour>()` and **Light derives from Behaviour, not MonoBehaviour**,
so the sweep walked past it structurally."* The sentence describes `PropAnimBelt` before this
build, word for word.

### 10.3 The identification

**The shimmer is not painted by a material property and it has no renderer of its own. It is a
`Light`, and the thing animating it is `RFX4_LightCurves`.**

`RFX4_LightCurves.Update` (decompiled `:30-49`) writes
`lightSource.intensity = LightCurve.Evaluate((Time.time - startTime) / GraphTimeMultiplier) *
GraphIntensityMultiplier` **every frame**, and when its serialized `IsLoop` is set it re-seeds
`startTime` and runs for ever. There is one such component per light on both censused props. That
mechanism is:

* invisible to an **animator** verdict — it is not an animator, and it needs none;
* invisible to a **material property** read-back — it writes no material property;
* invisible to a **renderer/visibility** census — a `Light` is not a `Renderer`;
* invisible to the **MonoBehaviour histogram** — a `Light` is not a `MonoBehaviour`;
* driven by **`Time.time`**, so immune to every Chronos/clock candidate §3 killed;
* and untouched by every suppression shipped up to and including ModBuild 445.

It also explains why the report is about the **hand** and never about the board: a lamp that is
unremarkable on a prop lying on a hex a metre and a half away is a lamp twenty centimetres from the
eye once the player picks the prop up. Nothing about the light changes; the solid angle it
occupies in the player's view changes by a factor of fifty.

### 10.4 What shipped

`src/GloomhavenVR/Board/FigureGrab/PropAnimBelt.cs`, three additions:

| | written | restored |
|---|---|---|
| **NEW — every `Behaviour`-derived emitter**: `Light`, `Projector`, `LensFlare` | `enabled = false` | yes, per object, to its exact prior value |
| **NEW — particle systems that were PLAYING** | `Stop(withChildren, StopEmittingAndClear)` | `Play` on those same ones, and only if still a child of the visual |
| unchanged | `Animator.enabled`, `Outlinable.enabled`, `SkinnedMeshRenderer.updateWhenOffscreen` | unchanged |

The emitter class is taken **whole** rather than by naming `Light` alone: the type boundary IS the
defect, and naming one more type by hand is how the next one gets missed.

`RFX4_LightCurves` is deliberately **not** the component switched off. Disabling the *writer*
freezes the intensity at whatever the curve last wrote — a pinned shimmer rather than an absent
one. Disabling the *light* removes the picture whoever writes the number, and on release the light
returns at whatever value the curve has meanwhile reached, which is exactly the value the game
would have had. Own the final value; do not win a write war.

Particles are stopped-and-**cleared**, never paused: a paused system leaves its live particles
hanging in the air, which is a frozen shimmer. Systems that were already stopped are never touched
and never restarted, and each one is re-checked at the landing for still being a child of the
visual — these objects are pooled through `ObjectPool.Recycle` (`SpawnPFXOnEnable`), so a reference
taken at the grab can by the landing name an object recycled into a different effect.

**Two suppressions ship in one round and they are still attributable**, because the census now
prints the PRE-STATE per class: `N Light(s) of which M were ENABLED`, `K particle system(s) of
which J were PLAYING`. A class whose pre-count is zero made no writes and cannot be why anything
changed in either direction.

### 10.5 The new instruments, and the falsifier that matters

**`] [Props] HELD-PROP PAINT AFTER HUSH`** — a 360-frame window opened *after* the hush has written
everything it writes, at most one per prop kind, three per session. It reports, per class, what is
still enabled / still playing / still moving, plus:

* the material property table **read whole** (cap 64, declared count printed beside it) off
  `sharedMaterials` — every slot of every renderer, not just the first;
* every light's intensity **sampled whether or not the lamp is on**, so a curve still writing a
  rising number into a disabled light is visible: that is the reading that says the writer
  survived the suppression;
* the max simultaneous playing particle systems and live particle count.

**The falsifier is stated in the line itself.** The fix is *working* if the enabled/playing counts
are 0 **with non-zero pre-counts**. The fix is **inert** — and this is the reading that must not be
mistaken for success — if **every pre-count is 0**: nothing was on, nothing was written, and a
shimmer the user still sees is then painted by something that is not under the prop's subtree at
all, which is the one place none of these instruments can look.

**`] [Props] HELD-PROP HUSH RESTORE`** — what came back, and its own falsifier. `Restore` compares
what is there *now* against what this class *left* there before writing the remembered value, and
prints three counts that are all expected to read 0: emitters found re-enabled by somebody else
during the hold, particle systems no longer children of the prop, objects destroyed under the
ledger. Any of them above 0 says the prop did not come back exactly as the game left it. This is
the guard against the recorded incident where a hide saved a foreign mid-animation value and
restored garbage over another system's restore.

### 10.6 Multiplayer — and a correction to §8

§8 and the class header both said *"a peer does not render a held prop at all"*. **That is wrong.**
`NetProps` mirrors another player's held prop into their hand (`] [Props] HELD-PROP MIRROR`, 108
lines on the host and 135 on the co-player in the 447 session) and it calls `PropAnimBelt.Engage`
at `NetProps.cs:288` and `PropAnimBelt.Release` at `NetProps.cs:550` — the same ledger, the same
`Tick`. That is what makes this 1:1 **by construction**: every suppression added to `Apply` reaches
the mirrored copy through the same call, so a shimmer removed from the prop in my hand is removed
from the prop the peer sees in my hand. No wire field, no second code path, no per-sub-feature sync
setting.

### 10.7 What round five did NOT do, and why

* **It did not re-measure the animator or its 20 material slots.** §7 said not to and it was right.
* **It did not touch `Renderer.enabled` or `GameObject.activeSelf`.** A held prop is never
  scenery and must never be hidden; nothing here can hide one.
* **It did not add `Animator.WriteDefaultValues()` before the disable.** Disabling an `Animator`
  *pins* every property it was animating at its grab-instant value rather than clearing it, which
  is a live alternative explanation for "still visible". `WriteDefaultValues` would return them to
  the authored defaults — but it also snaps the rig to its bind pose, which is a visible change the
  user did not ask for, and the property table read whole by the new verdict will say whether any
  such value is stuck before anyone pays that price. **If the PAINT AFTER HUSH line reports a
  material property with a non-zero range that stopped moving at the grab, this is the next fix.**
* **It did not suppress `RFX4_LightCurves` itself** — see §10.4.

---

## 11. Round six — 2026-09-06, against the ModBuild 448 log: the falsifier fired, and the painter is a camera

**User, verbatim:** *"Dieser highlighting/Licht-Effekt von Props ist immer noch in der Hand
bemerkbar. Wiederholt! Gehe da nochmal tiefer rein, scheint ein hartnäckiges Problem zu sein."*
He has stopped calling it "der weiße Schimmer" and now calls it a **highlighting / Licht-Effekt**.

### 11.1 The reading that decided the round, and it is §10's own falsifier

`] [Props] HELD-PROP PAINT AFTER HUSH` fired 6 times on the host and 4 on the peer. For
`'GoldPile' MoneyToken`, over **358 sampled frames with the suppression already in place**:

| class | pre-count | after |
|---|---|---|
| animators | 2 of 2 found | **0 enabled on any frame**, advancing on 0 |
| outlines | 1 of 1 found | **0 enabled on any frame** |
| lights | 3 of 3 found, **3 ENABLED before this build wrote anything** | **0 lit**, brightest intensity **0** |
| particles | 7 of 7 found | **0 playing**, 0 live particles |
| material properties | 4 materials, 57 declared, 48 tracked | **0 of 192 slots moved** |
| renderers | 10 of 10 | at most **1** drawing |

Read it in the order §10 wrote it. The **pre-counts are non-zero**, so the strands genuinely ran
and the ModBuild 448 light finding was real. The **after-counts are all zero**, so the subtree is
dark. **And the user still sees the effect.** That is §10.5's own written falsifier, word for word:
*"the shimmer is then painted by something that is not under the prop's subtree at all, which is
the one place none of these instruments can look."*

**So the sixth round did not measure the prop. It looked outward.** Everything in §§3-10 is a
census rooted at the prop, which is why five rounds produced flawless measurements of nothing.

### 11.2 THE PAINTER, NAMED

| | |
|---|---|
| **object** | the prop's own `MeshRenderer`, but drawn by somebody else |
| **the component on the prop** | `ObjectOcclusionVolume` (decompiled `GH.Runtime/ObjectOcclusionVolume.cs`) — a nine-line MonoBehaviour whose whole body is a register/unregister pair |
| **its parent** | `TilesOcclusionGenerator.s_Instance` — a component **on a camera**, a scene singleton, outside every subtree any instrument in this file has ever walked |
| **the property** | the GLOBAL shader texture **`_ObjectOcclusion`**, beside `_TilesOcclusionMap` and `_EnableOcclusionMap` |

`ObjectOcclusionVolume.OnEnable` is a single statement:
`TilesOcclusionGenerator.s_Instance.AddObjectRenderer(GetComponent<MeshRenderer>())`.
`TilesOcclusionGenerator` (decompiled `GH.Runtime/TilesOcclusionGenerator.cs:150-193`) holds a
`CommandBuffer` at `CameraEvent.BeforeGBuffer` that draws **every registered renderer** with
`m_OcclusionObjectMaterial` into a **quarter-resolution** target, blurs it twice, and publishes the
result with `SetGlobalTexture("_ObjectOcclusion", …)`.

**Why every instrument in this file was structurally blind to it.** It is not an `Animator`, a
`Light`, a `Projector`, a `LensFlare`, a `ParticleSystem`, an `Outlinable` or a material property —
it is disjoint from every class four rounds of instruments sampled. The list lives on a camera, the
draw is issued by a command buffer, and the result reaches the shader as **global** state, which
`material.GetFloat/GetColor` cannot see by construction. This is the same shape as the ModBuild 151
`Light`-derives-from-`Behaviour` hole, one level further out.

**Why it is a HAND defect and not a board one** — the term that makes it fit the report rather than
merely fit the code. On its hex the prop's footprint in that map is small and **still**, so the
value sampled back is effectively constant and nobody has ever complained about it. In a palm the
prop **fills a large part of the eye and moves every frame**, so its own blurred quarter-resolution
silhouette sweeps across it: a soft moving wash with no animator, no lamp, no particle and no
material of its own behind it. Twenty centimetres from the eye that is a "Licht-Effekt".

**It was already named as the next suspect and could not be tested.** ModBuild 448's
`] [Props] HELD? two frames after the grab` line ends: *"all drawn, no block, and still nothing
visible means neither, and the next suspect is **the prop shader's own screen-space occlusion
term**."*

### 11.3 The fix — strand 5, one ledgered bool

`PropAnimBelt.Apply` gains one line beside the other four strands:

```csharp
b.OcclusionFound = TakeEmitters<ObjectOcclusionVolume>(b, go, ref b.OcclusionOn0);
```

`ObjectOcclusionVolume` derives from `MonoBehaviour` and therefore from `Behaviour`, so the
existing generic emitter primitive already gives it the per-object ledger, the pre-count, the
restore and the foreign-write falsifier at no extra cost.

**Taken through the game's own lifecycle, never by editing its list.** `OnDisable` *is*
`RemoveObjectRenderer` and `OnEnable` *is* `AddObjectRenderer`, and both set `m_RenderersUpdated`
so the generator rebuilds its command buffer. One ledgered bool is the whole change and the whole
undo. No game state is written. A volume already disabled is left alone.

**Restore.** It shares `Belt.Emitters` with strand 3 deliberately — "switch a `Behaviour` off,
remember what it was, write it back" is ONE restore, and a second copy is a second place for the
next fix to land on only one of. That also means it inherits §10.5's restore falsifier unchanged:
`Restore` compares what is there *now* against what this class *left* there before writing the
remembered value, and `] [Props] HELD-PROP HUSH RESTORE` prints the mismatch count. It reads 0 when
the restore is exact. This is the guard against the recorded incident where a hide saved a foreign
mid-animation value and restored garbage over another system's restore.

**Multiplayer.** Nothing new was needed. `NetProps.cs:288` calls `PropAnimBelt.Engage` and
`NetProps.cs:550` calls `PropAnimBelt.Release` for a REMOTE hold, so the mirrored copy goes through
the same `Apply`, the same ledger and the same `Restore`. Strand 5 reaches the peer's mirrored prop
**by construction** — no wire field, no second code path, no per-sub-feature sync setting.

**A held prop is never scenery.** Nothing here touches `Renderer.enabled`, `GameObject.activeSelf`
or any layer; unregistering from an occlusion map cannot hide anything.

### 11.4 The instrument — and this time it looks OUTWARD

A fix aimed outside the subtree cannot be verified by a census rooted inside it, so the post-hush
verdict gained an `OUTWARD` section (`PropAnimBelt.AppendOutward`). Two arms:

* **The occlusion arm** — is this prop still IN `TilesOcclusionGenerator.m_ObjectRenderers`
  (must be 0); is the generator present at all; how big is its list scene-wide; and the GLOBAL
  slots read with `Shader.GetGlobal*`: `_EnableOcclusionMap`'s range, how many of the two map
  textures are bound, and **how many frames `_ObjectOcclusion` re-bound to a different texture** —
  a non-zero count there means the command buffer is live and the term is switched on while the
  prop is in the hand; a zero means the buffer is not running and this whole strand is inert.
  It reads a list the game already maintains and three global slots: no scene sweep at all.
* **The foreign-emitter arm** — every `Light` in the SCENE that is **not** under the prop and
  stands within 1.5 wu of its drawn box, named by **hierarchy path** rather than by object name.
  This is the arm that can see a lamp on the HAND or the RIG, or a pooled effect object parented to
  the scene and merely positioned to follow the prop — none of which a `GetComponentsInChildren`
  census can reach, because a containment test answers "related to an X", never "IS an X".
  Swept **twice per verdict** (open and close), never per frame: `FindObjectsOfType` on a per-frame
  path has already cost this project two rounds and one 12.6 ms frame.

**Grep token:** `] [Props] HELD-PROP PAINT AFTER HUSH`, section `OUTWARD —`.

**The fix is WORKING** if occlusion-volumes-found is non-zero, of which a non-zero number were ON
before this build wrote anything, **and** still-registered-on-any-frame is **0**.

**The fix is INERT** if occlusion-volumes-found is **0** — this prop kind never registered, so
strand 5 wrote nothing and cannot be why anything changed either way. (Of the three props in the
448 log only `GoldPile` carries an `ObjectOcclusionVolume`; `QuestDoll` and `OneHexObstacle` do
not. So a report that names only the gold pile as fixed is the expected shape of a partial win,
not a contradiction — and every prop still SAMPLES the global map whether or not it contributes
to it.)

**THE READING THAT WOULD MEAN THE PAINTER IS SOMEWHERE EVEN THIS INSTRUMENT CANNOT SEE**, because
it is the one that has ended five of the last five rounds: occlusion volumes found and taken (a
real pre-count), still-registered 0, the map still **bound and rebinding every frame** so the term
is live, foreign lights near the prop **0**, every subtree class 0 — **and the effect still
reported**. That combination excludes the prop's subtree, excludes its contribution to the
occlusion map and excludes every lamp within reach of it. What is left is a painter with no
`Light`, no `Renderer` under or beside the prop and no global slot named here: a replacement-shader
or post pass drawing the whole frame, or a term inside the prop's own shader fed by global state
this line does not read. **The seventh round must then measure the PICTURE** — a per-eye frame
difference with the prop held still versus moving — because at that point every state probe in this
repository has been exhausted, and state probes cannot see sampling.

### 11.5 A second blind spot, proven and NOT fixed

The verdict's material read-back tracks **float/range/colour only**. Two things fall outside it and
both are now named in the line rather than left to be rediscovered:

* **VECTOR and TEXTURE properties are untracked.**
  `CustomObjectPositionToChildMaterials.Update` (decompiled `GH.Runtime/…:70-100`) writes the
  **vector** `_FadeSourcePos` into every child material **every frame** from a moving actor's world
  position. That is a live per-frame material writer on a held prop that would report as "nothing
  moved" in every reading taken so far. It is on `OneHexObstacle` (x2 in the 448 histogram).
* **The grab-edge census's feeder count is narrower than the family it is counting.** It names
  `ZephyrAnim`, `ObjectPosToMaterial` and `PosToMat` and prints `x0` for all three — on a line
  whose own MonoBehaviour histogram, twenty words later, names
  `CustomObjectPositionToChildMaterials x2`. A gate narrower than its choke point.

Neither was fixed this round, deliberately: the occlusion strand is a single suppression with a
clean pre-count, and shipping two remedies at once is how a round loses the ability to attribute.

### 11.6 Candidates FALSIFIED this round — do not re-open them

* **The mod's own pre-grab glow (`FigureHighlight` / `OverlayPulse`).** The 448 hush histogram
  names `OverlayPulse x1` under both `GoldPile` and `QuestDoll`, which looks damning: it is this
  mod's own component and it writes `_mat.color` from a 0.7 Hz sine on `Time.unscaledTime` plus a
  `_MainTex` scroll — a literal "highlighting/Licht-Effekt". **It is not the painter.**
  `GrabbableProp.OnGrab` calls `ClearHighlight()` at `:559`, *before* `PropAnimBelt.Engage` at
  `:593`, and `Object.Destroy` is deferred to end of frame — so the grab-edge census enumerates a
  component that is already condemned. During the hold neither hand can re-raise it: the holding
  hand early-outs of `ProximityGrabber.UpdateHighlight` on `Held != null`, the other hand is
  refused by `GrabbableProp.AllowsHand` (`if (_holder != null) return ReferenceEquals(_holder,
  hand)`), `WalkInHighlightEdges.TryRelightHighlight` guards `_holder != null` explicitly, and
  `RayGrabDriver` only ever highlights a `PanelGrabHandle`. **A latent asymmetry worth recording
  anyway:** `TryRelightHighlight` has the `_holder != null` guard and `OnGrabHighlight` does not.
  It is currently unreachable; it is one election change away from not being.
* **`RFX4_ParticleLight`'s pooled lights.** They looked like the archetypal "pooled effect object
  parented to the scene". They are not: `lights[i].transform.parent = base.transform`
  (`GH.Runtime/RFX4_ParticleLight.cs:33`) parents them **under** the effect, so they are inside the
  subtree and strand 3 already counts and switches them off.
* **The game's outline registry.** `OutlineWrapper.OnEnable` adds the prop's `Outlinable` to
  `WorldspaceUITools.m_AllOutlinables` — a genuine second, game-owned list outside the subtree that
  the hush's reasoning never mentioned. It does not matter: `Outlinable.OnDisable` removes the
  component from the pass's own static list, and the verdict measured **0 outlines enabled on any
  of 358 frames**.
* **`TargetStateListener`.** Present on every prop and named in every histogram. It is EPOOutline's
  internal `OnBecameVisible/OnBecameInvisible` hook and paints nothing.
* **The "Hovering" layer.** Layer 15, not the layer 8 the `HELD?` line reports; and `ParkLayers`
  already moves the visual and every collider host to Ignore Raycast for the hold.

---

## 12. Round seven — 2026-09-06, against the ModBuild 450 log: the painter is OUR OWN FREEZE

**User, verbatim:** *"Fallen und Truhen werden immer noch manchmal weiß wegen dieser
Aufblitzen-Animation wenn sie in der Hand sind. Problem ist also nicht behoben."*

Two words in that sentence are the whole round. **"manchmal"** — intermittent, so a window that
sampled a normal hold says nothing. **"weiß"** — the asset *is* white, an absolute state, where six
rounds of reporting used the words *Schimmer*, *highlighting* and *Licht-Effekt*, i.e. movement. The
word changed after the hush shipped.

### 12.1 What the ModBuild 450 log actually says, and what it excludes

`] [Props] HELD-PROP PAINT AFTER HUSH for 'Trap' Trap`, 360 sampled frames with the suppression in
place:

| class | pre-count | after |
|---|---|---|
| animators | 1 of 1 | **0 enabled**, advancing on 0 frames |
| outlines | 1 of 1 | **0 enabled** |
| lights | **0 of 0 found** | 0 lit |
| particles | 0 of 0 | 0 playing |
| renderers | 3 of 3 | at most **1** drawing |
| material properties | 2 materials, `Amp_Char_Shader`, 57 declared, 48 tracked | **0 of 96 slots moved** |
| screen-space occlusion | 1 volume, **2 ENABLED before this build wrote anything** | **0 still registered**, map re-bound on **359 of 360** frames |
| foreign lamps | — | 1 lit Light 1.14 wu away (a Forest Imp elite's idle effect) |

So strand 5 read its **WORKING** shape — real pre-count, zero still registered — and the defect
stood. The occlusion map is not the painter.

**And the A/B line beside it is the one that matters.**
`] [Props] HELD-PROP ANIMATION A/B for 'Trap' Trap`:

```
'Trap_BearTrap_PR'  advancing hand=0/1028  home=359/360
                    anyLayerRate hand=n/a  home=0.201/s
MATERIAL PROPERTIES … NOT ONE of the 20 tracked property slot(s) changed value in EITHER window
```

Read those two together. On its hex the prop runs **one continuous looping clip, ~5 s per cycle,
advancing on 359 of 360 frames**. In the hand it is **stopped dead** — the hush doing exactly what
it was written to do. And **no tracked material property moves in either window**, so whatever that
clip drives is not one of the 20 (or, per the post-hush line, the 48) float/range/colour properties:
it is something else — a texture, a vector, a renderer flag, a bone.

### 12.2 THE PAINTER, NAMED

| | |
|---|---|
| **object** | the prop's own visual root — `Trap_BearTrap_PR` and its 3 renderers |
| **the component** | its own `Animator`, running one looping ~5 s attention clip |
| **the property** | *whichever channel that clip drives* — and the point is that nobody needs to know which |
| **the painter** | **`PropAnimBelt`'s own `a.enabled = false`** (ModBuild 445, strand 1) |

`Animator.enabled = false` does not undo a clip. It stops the clip **where it is**. Every channel
the clip drives keeps the value it happened to hold on the frame of the grab, for as long as the
prop is held. On a ~5 s loop whose bright part is a fraction of the cycle, that is a coin toss:

* grab during the bright part → the prop stays **white for the whole hold**;
* grab anywhere else → the prop looks right.

That is **"manchmal"** exactly. It is **"weiß"** rather than a shimmer because it is a *held value*
rather than an animation. And it is **invisible to every reading in this file**, because all of them
ask "did anything MOVE?" and a latched value does not move. **`0 of 96 slots moved on any frame` is
precisely what a prop frozen white prints.** Six rounds of flawless zeroes were all consistent with
it.

The instrument said so about itself and nobody read it as a lead — `PropAnimWatch`'s own header:
*"THIS CLASS IS STILL USEFUL, BUT ONLY FOR A HOLD THAT VISIBLY FLASHES. A verdict from a hold in
which nothing flashed is not evidence about the hand."* The 450 A/B line says it outright: *"this
window did not contain the thing the report is about."*

### 12.3 The fix — strand 6, one call, and it is deliberately not `Play(state, layer, 0f)`

`PropAnimBelt.RewindAndStop`: every animator this class is about to switch off is first taken back
to its **bound default values** with `Animator.WriteDefaultValues()`, and only then disabled — so
the frozen frame is the resting one.

* **Not `Play(hash, layer, 0f)`.** Replaying a state RE-ENTERS it, and re-entering fires every
  `StateMachineBehaviour` on it. On these props that is how `DelayedDeactivatePropAnimSMB` sends a
  rules message and raises a global "deactivations in progress" flag. This lane does not write game
  state. `WriteDefaultValues` touches no state machine, fires no behaviour and no animation event.
* **It does not need to know which channel the flash lives in**, which is the whole reason it is the
  right shape: the 450 read-back proves it is none of the 48 float/range/colour properties while the
  clip advances on 359 of 360 home frames. One call returns **all** the controller's channels at
  once.
* **Refused, not forced**, on an animator that cannot have defaults — an inactive object or a null
  controller. Those are counted in `RewindSkipped` and stopped the way ModBuild 445 stopped them.
* **The rules exception is untouched.** An animator carrying `DelayedDeactivatePropAnimSMB` is not
  in the ledger at all, so it is neither rewound nor stopped.

**The restore, and how it was verified.** `Restore` hands the `enabled` flag back. The animator
resumes from the state it retained and **drives every one of those channels itself on the first
frame it evaluates**, over our defaults — so the highlight returns exactly as before *by
construction*, not by a remembered copy. That matters here specifically: this project has a recorded
incident (["a hide saved a foreign value"]) where a snapshot was restored over another system's
restore, and strand 6 creates **no snapshot to restore**. Its falsifier is counted rather than
asserted: `Belt.RestoreForeignAnimators` is how many animators were found ENABLED at the landing
although this class had switched them off, printed on `] [Props] HELD-PROP HUSH RESTORE` and
expected to read 0.

**Multiplayer.** Nothing new was needed and no wire field was taken. `NetProps` calls
`PropAnimBelt.Engage`/`Release` for a REMOTE hold too, so the peer's mirrored copy goes through the
same `Apply`, the same ledger and the same restore — strand 6 reaches it by construction.
**Verified from evidence on the LOCAL side only: the ModBuild 450 log is a single-player session.**
The mirrored half is reasoned from the call sites (`NetProps.cs` `Engage`/`Release`), not measured.

**A held prop is never scenery.** Nothing here touches `Renderer.enabled`, `GameObject.activeSelf`
or any layer.

### 12.4 The instrument — values, not movement; and vectors and textures

Six rounds reported **movement counts and never once a value**, which is why a term latched at a
wrong constant read exactly like a term that was correct. Four changes:

1. **Strand 6's pre-count is a VALUE difference.** The whole property table under the prop is read
   immediately before and immediately after the rewind, and the line reports **how many
   (material, property) slots the rewind actually CHANGED**, naming the first six with
   `before→after` values.
2. **VECTOR and TEXTURE properties are tracked** (plus `Int`), closing the blind spot the 450 line
   named about itself. A per-frame vector writer such as `CustomObjectPositionToChildMaterials`
   (`_FadeSourcePos`) would now show as a mover instead of as "nothing moved".
3. **Each material is read through the property table of ITS OWN shader.** The old code built one
   table from `VMats[0]`'s shader and read every other material through it, skipping in silence
   every id that material's shader does not declare — while printing "N material(s) on shader 'X'"
   as though all of them were X. The line now prints `mat0 'shader' D declared / T tracked` per
   material, plus **sampled-of-found** for the material count itself. Caps raised 4→16 materials and
   64→96 properties.
4. **The feeder census can name its own family.** `PropAnimWatch`'s world-anchor counter listed
   `ZephyrAnim`, `ObjectPosToMaterial` and `PosToMat` while the MonoBehaviour histogram twenty words
   later in the same line could name `CustomObjectPositionToChildMaterials` — a gate narrower than
   its choke point. That type is counted and named now.

**Grep token:** `] [Props] HELD-PROP PAINT AFTER HUSH`, section `THE FREEZE'S OWN LATCH`. It is also
printed at the grab edge on `] [Props] HELD-PROP ANIMATION HUSH`, section `REWOUND FIRST`.

**THE FIX IS WORKING** if `animator(s) were taken back to their BOUND DEFAULT VALUES` is non-zero
**and** the `slot(s) CHANGED VALUE` count beside it is non-zero: that pair says the grab really did
catch the clip away from rest and that the old build would have latched that picture.

**THE FIX IS INERT** if the rewound count is 0 (this prop carries no animator this class stops) or
if the changed count is 0 (the clip was already at rest at every sampled grab) — in which case it
can be neither the cause of an improvement nor of a regression, and the round says nothing either
way. Note that a single hold's changed count of 0 is *expected* some of the time: the defect is
phase-dependent, so the number to read across the session is whether it is EVER non-zero.

**AND THERE IS A THIRD READING OF A ZERO, which names a different fix rather than a dead lead.** A
changed count of 0 on a prop whose clip was *demonstrably* advancing on its hex — read
`anyLayerRate home` on the `ANIMATION A/B` line for the same prop kind; for the trap it is
`0.201/s` — does **not** say the clip was at rest. It says `WriteDefaultValues` did not land as an
*immediate* write: this class disables the animator in the same call, so a write deferred to the
next evaluation would never happen. The lever for that reading is `Animator.Rebind()` before
`WriteDefaultValues`, and the two readings are told apart by exactly that home rate.

**THE READING THAT WOULD MEAN THE PAINTER IS STILL BEYOND THE INSTRUMENT** — stated plainly, because
it has ended six of the last six rounds: **animators rewound with a NON-ZERO slots-changed count**
(so the latch existed and has been removed), occlusion volumes taken and still-registered 0, **no
material property of ANY class — float, colour, VECTOR or TEXTURE — moving**, no foreign lamp near
the prop, every subtree class 0, **and the prop still reported white**. That combination removes the
last state this file can reach. What would be left is what no state probe can see:

* a **`MaterialPropertyBlock`** written per frame — the `HELD?` probe samples blocks **once**, two
  frames after the grab, and read `0 carry a MaterialPropertyBlock` for the trap, so this is a
  snapshot and not a per-frame reading;
* a **shader keyword**, which switches a whole branch with no property moving;
* the **per-renderer light-probe SH and reflection probe** Unity re-picks when a renderer MOVES,
  which change for a prop carried across the room and for no other reason;
* a **replacement-shader or post pass** drawing the whole frame.

At that point the next round must measure the **picture**: a per-eye frame difference with the prop
held still versus moving, **and with the prop grabbed at two different phases of its idle loop** —
that second axis is new and is the one that distinguishes a latch from a painter.

### 12.5 Where round six's brief was wrong, and it is worth recording

The seventh round was briefed to chase `CustomObjectPositionToChildMaterials` writing the vector
`_FadeSourcePos`. **The trap does not carry one.** The ModBuild 450 grab-edge census enumerates
*all* 15 distinct MonoBehaviour types under it — `ApparanceEntity`, `ProceduralStyle`,
`ProceduralProp`, `UnityGameEditorObject`, `UnityGameEditorTrapProp`, `PropParent`,
`TimelineAssets`, `SpawnProp`, `Outlinable`, `OutlineWrapper`, `MaterialLoader`,
`ObjectPosToMaterial`, `TargetStateListener` x2, `ObjectOcclusionVolume`, `OverlayPulse` — and that
type is not among them. The one feeder it *does* carry, `ObjectPosToMaterial`, writes **only in
`OnEnable`** and through `GetComponent<Projector>()`, and the census counts **0 Projectors** under
this prop. So there was no live vector writer on the trap at all. The vector/texture read-back
shipped anyway, because it closes a named blind spot cheaply — but it is instrumentation, not the
fix.

### 12.6 Candidates checked again this round and still dead

* **`ObjectOcclusionVolume` / `TilesOcclusionGenerator`** (§11) — strand 5 read its WORKING shape on
  the trap and the defect stood.
* **`OverlayPulse` / `FigureHighlight`**, the mod's own additive pre-grab glow. Still not the
  painter *during a hold*: the post-hush verdict found **3 renderers and 2 materials, both on
  `Amp_Char_Shader`**, so no `GloomhavenVR/Overlay` material was drawing. (The third renderer in the
  belt's count against `PropAnimWatch`'s 2 is the overlay clone caught in its deferred-`Destroy`
  frame, which is consistent with §11.6's account rather than against it.)
* **`MaterialPropertyBlock`** — `0 carry a MaterialPropertyBlock, 0 of those with
  `_Toggle_Dissolve` ON` for the trap, so `WallSegmentFade` is not writing this prop. Snapshot only;
  listed above as still-open for that reason.
* **The foreign lamp.** The one lit Light within 1.5 wu is a Forest Imp elite's idle-effect point
  light, intensity 0.5, range 3, on the board — not on the hand or the rig. Recorded, not promoted.

### 12.7 What round seven did NOT do, and why

* It did **not** ship the per-eye picture difference. §11.4's prescription makes it conditional on
  every state probe being exhausted, and this round found an unexhausted one — the difference
  between a value and its movement — inside the existing instrument. Shipping a frame-capture rig
  next to an untested one-call fix would also make the next round unable to attribute.
* It did **not** touch the animators carrying `DelayedDeactivatePropAnimSMB`, or add any second
  suppression. One remedy per round, with a pre-count.
