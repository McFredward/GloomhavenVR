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
