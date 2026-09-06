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

## START AT §19 — ROUND TWELVE (2026-09-06): THIS BUILD IS AN EXPERIMENT, NOT A PROBE

**STRAND 5 IS OFF.** For twelve rounds this mod unregistered a held prop from
`TilesOcclusionGenerator`, so the prop was no longer DRAWN INTO the global `_ObjectOcclusion`
map while it went on SAMPLING it — **and a prop absent from a darkening map is UNDARKENED,
i.e. brighter than the same prop on the board**, with no material, no component and no
lighting behind the difference. All three of those are now measured IDENTICAL (§17.2, §18.1),
which is what leaves this standing. It is the only asymmetry this mod created itself, it
shipped in ModBuild 449 (`30058ced` → `d9005f02`, verified from `git log`), and nothing it was
introduced for was ever confirmed.

**Confirm which build you are testing with one grep:**
`] [Props] HELD-PROP ANIMATION HUSH` containing `STRAND 5 OFF - NULL PERTURBATION`.

**§19.4 writes the three expected outcomes down BEFORE the test** — gone, unchanged, or worse
— and what each one means. Read it before reading the next log.

Also closed this round: the **write war** (0 ledger edges over 4 rescans), the **mirroring**
hypothesis (determinant 1 vs 1), and **why the standing board watch printed nothing** — my own
defect, a 3600-FRAME window on a rig running 38.5 fps, i.e. 93 s (§19.2). `updateWhenOffscreen`
is the only other asymmetry left and §19.5 says plainly that it cannot brighten anything.

Read [§19](#19-round-twelve--2026-09-06-against-the-modbuild-457-log-an-experiment-not-a-probe-strand-5-is-off)
first, then **§15, the obituary** of every strand and probe deleted on 2026-09-06.

---

## §18 — ROUND ELEVEN (2026-09-06): THE "LATCHED VALUE" MODEL IS WRONG

> *"Es ist wie der flash nur deutlich verlangsamt und nicht ganz flüssig wie beim flash auf
> dem Spielbrett."*

**A latched value cannot stutter.** §14's model — the white is a value frozen at the grab — is
retired by the user's own observation: the effect still happens in the hand, it is the SAME
flash, it is distinctly SLOWED, and it is NOT FLUID. Something drives it at a reduced and
irregular rate, and that is a signature with a NUMBER in it. Every "nothing moved" this file
has printed proved only that our sampling did not catch movement.

And the user made the methodological correction too: *"dieser Blitz ist ja ein voll spiel
gewolltes Highlighting … müsstest du es doch auch im originalspiel finden können."* Eleven
rounds chased instrument readings for a deliberate feature sitting in `decompiled/`.
**Read the game first.** §18.4 has the three leads that came out of doing so — Chronos
(with a correction: round one DID measure it, §3, but only downward from the visual, and a
clock governs from ABOVE), `IdleSMB` stranding `animator.speed` because a disabled Animator
never fires `OnStateExit`, and a shader phase seeded by a world position that was never
written. §18.5 names the five arms that measure them, including the first observer in this
whole investigation that **needs no grab**.

Read [§18](#18-round-eleven--2026-09-06-against-the-modbuild-456-log-the-latched-value-model-is-wrong-and-the-search-moves-into-the-games-own-source)
first, then **§15, the obituary** of every strand and probe deleted on 2026-09-06.

---

## §17 — ROUND TEN (2026-09-06): THE MATERIAL CLASS IS OUT

ModBuild 455, 194 frames. A trap standing on its own hex runs its ENTIRE animator loop
(advancing 193/194, fraction 0.001..0.997) and moves **0 of 57 material slots**, on a material
asset it **SHARES** with every other trap; the held prop differed on **0 slots, ever**. So:

1. **The whole material class is excluded.** No fix written on a material value can change the
   picture, and §§3-14's remaining material hypotheses are all dead.
2. **The ~5 s idle loop is NOT the flash** — carried unexamined for four rounds. It sweeps its
   full range while nothing about the material changes, so it drives BONES (a bear trap's
   jaws), not a brightness. `clipRate 0.202/s` is the jaws' clock. **No round has ever
   measured the flash's period.**
3. **§16's overlay lead is falsified** by its own key: the census read 1, hanging off the very
   trap being held. Overlays are not leaking; the flash is not ours.

Round ten watches the UNHELD twin's OBJECT GRAPH per frame — the one class never measured on a
prop that is not frozen — with every change timestamped so the flash's period can finally be
read off. **If that reports 0 changes over a full loop, the state-probe era is over and the
next round must measure the PICTURE, not the state. Do not invent an eleventh state probe.**
Read [§17](#17-round-ten--2026-09-06-against-the-modbuild-455-log-the-material-class-is-out-the-overlay-lead-is-dead-and-the-5-s-loop-was-never-the-flash) first,
then **§15, the obituary** of every strand and probe deleted on 2026-09-06.

---

## §14 AND §16 — ROUND NINE (2026-09-06): THE USER NAMED THE TRIGGER

**§16 is the lead.** The user says every trap flashes white at once. A sweep of the game and
of this mod found exactly ONE oscillator that brightens prop meshes with NO per-instance
phase, and it is OURS: `OverlayPulse.Update` drives an ADDITIVE warm-amber tint from
`Time.unscaledTime` (`FigureOverlay.cs:1293`), so every live instance pulses in exact lockstep
by construction — and additive amber over lit bronze washes to IVORY. It should exist at most
twice (one per hand). **Read the OVERLAY PULSE CENSUS count first: above 2 means overlays are
leaking and the flash on every trap is ours; 0 or 1 kills the reading outright.**
**§15 is the obituary** — every strand and probe deleted on 2026-09-06 and the reading that
retired it. Do not re-propose one without new evidence.

> *"Das weiße in der Hand tritt immer auf wenn ich die Falle/Truhe aufhebe kurz nachdem der
> weiße flash auf allen Fallen kam."*

A white flash runs across EVERY trap at once, and grabbing shortly after it ALWAYS leaves the
held one white. So the white is a value **latched at the instant of the grab**, and nothing
during the hold sustains or removes it — which is why every instrument in this file reads zero.
**They all ask whether something MOVES. None has ever asked whether the value is the RIGHT one,
and a latched wrong value is constant.** Round nine ships the comparison that discriminates:
the held table against a prop of the same kind still on its hex, read on the SAME TICK.
Read [§14](#14-round-nine--2026-09-06-against-the-modbuild-454-log-the-user-named-the-trigger-and-a-room-full-of-zeroes-became-a-finding)
first. §14.1 closes four leads on measurement (our overlay, the reflection probe, property
blocks, keywords) and §14.4 names the phase bug in the obvious fix.

---

## §13 — ROUND EIGHT (2026-09-06) KILLED TWO LEADS BY MEASUREMENT AND SHIPPED NO FIX

The ModBuild 453 log falsifies §12's own premise: `REWOUND FIRST` reads `0 of 67 slot(s)
changed value`, which is strand 6 INERT by the falsifier §12.4 wrote for it, so the freeze was
latching nothing. Texture mip streaming — the lead round eight was briefed to test — is dead
too, killed by this repo's own `] [Perf] TEX` line (`streamingMipmaps=False`, forced off by
this mod). Every state probe in this file reads dark over 169 frames while the user's video
shows a real decay. **And the photometry of that video is itself partly a movement artefact —
the ring is white when seen FACE-ON and bronze when seen EDGE-ON, so nothing shipped so far
can tell a TIME ramp from a POSE ramp.** Round eight shipped one instrument and no remedy;
read [§13](#13-round-eight--2026-09-06-against-the-modbuild-453-log-and-the-users-video-two-leads-killed-by-measurement-and-every-remaining-reading-is-an-identity-a-count-cannot-give)
first, then §12 for what strand 6 has left to answer.

---

## §12 — ROUND SEVEN (2026-09-06) THOUGHT THE PAINTER WAS **US** (superseded by §13)

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

---

## 13. Round eight — 2026-09-06, against the ModBuild 453 log and the user's video: two leads killed by measurement, and every remaining reading is an identity a COUNT cannot give

**User, verbatim, after testing ModBuild 453:** *"Das weisse aufblitzen ist nach wie vor in der Hand
zu sehen. In der Hand ist es allerdings deutlich langsamer als wäre es zeitlupe."*

This round shipped **NO REMEDY**. It is instrument-only, and §13.6 says why that is forced rather
than cautious.

### 13.1 The two things the ModBuild 453 log settles, and both are negative

Anchored on `^\[Info   :GloomhavenVR\] \[FigureGrab\] \[Props\] HELD-PROP ` and
`^\[Info   :GloomhavenVR\] \[Perf\] TEX `. Single-player session, `Player.log` line 40 reads
`[Core] GloomhavenVR ModBuild 453`. Two `Trap` grabs and one `OneHexObstacle` grab.

**(a) STRAND 6 IS INERT BY ITS OWN FALSIFIER, so ModBuild 453's premise is dead on measurement as
well as on the video.** `ANIMATION HUSH`, section `REWOUND FIRST`, for the trap:

```
1 of them were taken back to their BOUND DEFAULT VALUES … and 0 of 67 (material, property)
slot(s) changed value because of it.   0 could not be rewound.
```

§12.4 wrote the reading of that zero in advance: rewound non-zero **and** changed **zero** is INERT
— the clip was already at rest at the grab, so the freeze was latching nothing and removing the
latch can be neither an improvement nor a regression. §12.4 also named a *third* reading of that
zero (`WriteDefaultValues` not landing as an immediate write, distinguishable by a non-zero
`anyLayerRate home`), and the 453 `ANIMATION A/B` line reads `anyLayerRate home=0.201/s` — so that
third reading is **live and unexcluded**. It does not matter for this round, because the picture in
the video is a ramp and a latch is a constant either way, but it is the one thing strand 6 has left
to answer and `Animator.Rebind()` is still its lever.

**(b) TEXTURE MIP STREAMING IS FALSIFIED, and by an instrument that was already in this repository.**
Round eight was briefed to test mip streaming as its lead — a monotonic multi-second settle, local
to one object, beginning when that object suddenly fills the eye, is the classic shape. It is not
what is happening here. `] [Perf] TEX`, six lines this session, verbatim:

```
masterTextureLimit=0 (FULL — mip 0 is live, so no global mip drop is in play …)
streamingMipmaps=False (off BECAUSE THIS MOD TURNED IT OFF — [RenderQuality]
   ForceTextureStreamingOff, asserted per frame by Rig/RenderQuality.ApplyTextureStreaming …)
```

That is exactly the reading the brief named as the one that kills the lead: nothing streams, so
nothing can settle. **Do not re-propose it, do not pin `requestedMipmapLevel`, and do not touch a
global streaming or quality dial — that is a user dial.**

**BUT RECORD THIS, BECAUSE THE SAME LINE CONTRADICTS ITSELF.** Its prose asserts *"Every mipped
texture is resident in full here"* while its own population clause, twenty words later in the same
string, counts:

```
STREAMING: 68 of them are streamed, 68 currently BELOW their desired mip level
   ⇒ EVERY STREAMED TEXTURE IN VIEW IS BEHIND
```

One of those two is wrong. The prose is an **assertion in a log string** and the 68 is a
**measurement**, and this project's own standing rule is that the first is a hypothesis. Neither is
a reading of the held prop. Round eight therefore closes it *locally* with two integers on the new
line (§13.5) rather than by argument, and the whole-scene question is left where it belongs, with
the `Perf/TEX` lane.

**(c) Every state probe in this file is dark, over 169 frames, while the video shows a ramp.**
`PAINT AFTER HUSH for 'Trap' Trap`, 169 frames, closed because the prop was put down: animators
`at most 0 enabled`, outlines `at most 0 enabled`, lights `0 of 0`, particles `0 of 0`, occlusion
`1 volume, 1 enabled beforehand, at most 0 still registered` with `_ObjectOcclusion re-bound … on
168 frame(s)`, material properties `2 material(s) … 67 slot(s) … 0 — NONE of the 67 slot(s) moved
on any frame`. That is the "STILL BEYOND THE INSTRUMENT" branch the 453 commit names for itself.

### 13.2 A CORRECTION TO THE PHOTOMETRY THAT BRIEFED THIS ROUND, and it changes what must be measured

The brief's reading of `.planning/debug/fallen_weisses_aufblitzen.mp4` is **ONE MONOTONIC ~3.4 s RAMP
from over-bright white down to no glow, beginning at the grab**, measured as the percentage of
pixels with Y>175 inside a *fixed* 520x520 window. The decay is real. **The curve is not
trustworthy as a curve, because that instrument measures a product and reports one factor.**

Open the crops in order. At **t=1.8, 2.2, 2.6, 3.0, 3.6** the trap's jaw ring is unambiguously
ivory white. At **t=4.4** the brightness fraction has fallen to 0.32 % — a factor of fifteen below
the peak — and the ring **is still ivory white in the picture**; what changed is that the hand has
rotated and the ring now occupies a fraction of the window it filled at 2.6. By **t=5.4** it is
dark bronze. So the fraction fell for two reasons at once and the line cannot separate them, which
is this project's recorded shape: *a diagnostic modelling a SUBSET of what the eye sees agrees with
the eye and names the wrong cause.*

**And there is a second term in the same pictures that nobody has proposed.** The ring is white when
it is seen **face-on** (a full circle, t=2.0–3.6) and bronze when it is seen **edge-on** (an
ellipse, t=5.4–6.6). A view-angle-dependent term — a specular lobe, a fresnel/rim, a reflection
probe — produces exactly that and produces it with **no clock in it at all**. The transition in the
mosaic tracks the wrist, not the second hand.

**So the round-eight question is not "what ramps over 3.4 s".** It is: *is the decay a function of
TIME or of POSE?* Nothing shipped in seven rounds can tell those apart, and picking a remedy before
that is settled is picking between two different fixes by coin toss. The new line prints the view
angle and the head distance **on the same quarter-second timebase as the photometry**, which is the
whole point.

### 13.3 THE TWO NUMBERS IN THE 453 LOG THAT ARE IDENTITIES, NOT QUANTITIES

Both are printed by instruments this file already ships, both are non-zero, and neither has ever
been chased — because a count cannot answer either of them.

| the reading | why a count cannot answer it |
|---|---|
| `RENDERERS: 3 sampled of 3 found, at most 1 drawing and 1 reported isVisible` (169 frames), and `HELD? two frames after the grab: 1 of 2 renderer(s) are actually drawing (2 on active objects)` | Two of the prop's renderers never drew. **Which two?** The `HELD?` line's own prose says *"drawn < total on renderers this class never touched means something DISABLED them, and the only thing in this mod that disables airborne scenery is WallSegmentFade"* — that falsifier has been firing in every log for several rounds and nobody read it, because the line prints a fraction and not a name. It may also be perfectly normal (a bear trap prefab has an armed mesh and a sprung mesh). Only a name settles it. |
| `mat1 'GloomhavenVR/Overlay' 10 declared / 10 tracked` | **One of the held prop's two materials is THIS MOD'S OWN SHADER**, and `OverlayPulse x1` is in the prop's MonoBehaviour histogram. §11.6 argues it is the pre-grab `FigureHighlight` clone caught in its deferred-`Destroy` frame and therefore already condemned. That argument is **an assertion in a comment**, and this project has three recorded incidents of its own machinery being the churn it was measuring. Its colour did not move on any of 169 frames — but a *static* additive overlay is still a white wash, and "nothing moved" is exactly what one prints. Rule it in or out by name. |

The recorded lesson is the title of one of this project's own memory entries: **name the blocker,
not the number** — six rounds once tuned a coverage FRACTION where one field naming WHICH renderer
would have ended it. That is what shipped.

### 13.4 What shipped — `] [Props] HELD-PROP ROSTER`, and it is an instrument, not a fix

A second `VRLog.Note` line beside `PAINT AFTER HUSH`, emitted from `CloseVerdict`, with its own grep
token so a hardware round can pull it alone. Sampled every frame of the same post-hush window.
Sections:

1. **ROSTER.** Every renderer under the prop by **hierarchy path** and component type, with each
   material's name and shader, a `MOD-OWNED` / `game-owned` verdict taken from the shader
   namespace (`GloomhavenVR/`), and per renderer: frames DRAWING, frames `isVisible`, frames
   carrying a `MaterialPropertyBlock`, worst overridden-slot count, and the frame it was
   destroyed at. Each entry carries the index it was taken from, so a null at arm time cannot
   shift the roster against the array it reads (a set that shrank under a per-entry read is a
   recorded incident here).
2. **PROPERTY BLOCKS, READ EVERY FRAME.** Section-5 item 1 of the brief, and the 453 verdict's own
   first still-open item. The existing `HELD?` probe reads blocks **once**, two frames after the
   grab, and a snapshot cannot see a ramp. This asks each block, every frame, which of the
   shader's own properties it overrides, names them, and reads the **dissolve channels by name and
   by value** — every declared property whose name contains *dissolve* or *cutout*, plus
   `_Toggle_Dissolve` and `_Dissolve` whether or not the shader declares them, because a block can
   carry an id the material never had. A dissolve **is** a ramp, which is the one shape the video
   shows.
3. **SHADER KEYWORDS**, on a 15-frame cadence because `Material.shaderKeywords` allocates. Section-5
   item 2. A keyword switches a whole branch with no property moving, so *"none of the 67 slots
   moved"* is not evidence that the shader did the same thing on every frame.
4. **LIGHTING BINDINGS UNITY RE-PICKS WHEN A RENDERER MOVES.** Section-5 item 3. The interpolated
   light probe at the drawing renderer's bounds centre (`LightProbes.GetInterpolatedProbe`, printed
   as the Rec.709 luminance of the L0 band, first / last / range) and the reflection probe
   (`Renderer.GetClosestReflectionProbes` — count, closest by hierarchy path and weight, and how
   many times the bound probe **changed identity**), plus both usage enums. Neither is a component,
   a material property or a global, so every instrument in this file is blind to both by
   construction — and a metal ring that leaves its authored probe volume and starts reflecting the
   ambient is bright, view-dependent, and correct again the moment it turns away.
5. **STALE WORLD ANCHORS.** Every VECTOR slot's value against the prop's live world position, with
   the distance between them. See §13.7 — this is a correction to §12.5.
6. **THE POSE CONFOUND**, §13.2: head-to-prop distance and the angle between the drawing renderer's
   own forward axis and the view ray.
7. **THE TIMELINE.** Quarter-second buckets, twenty-four of them, printing probe luminance, view
   angle, head distance, renderers drawing and block-overridden slots **as a series**. This is the
   half no earlier round had: the user's word is *zeitlupe* and his video is a ramp, and a single
   aggregate per hold can neither agree nor disagree with a ramp.
8. **MIP RESIDENCY ON THIS PROP'S OWN TEXTURES** — two integers, closing §13.1(b) locally.

### 13.5 The grep token and how to read it

```
grep -n '^\[Info   :GloomhavenVR\] \[FigureGrab\] \[Props\] HELD-PROP ROSTER' Player.log
```

Anchor on `] `. This file quotes other instruments' tokens inside its own prose and an unanchored
grep counts the explanation as an occurrence; the integrator made exactly that mistake on this file.

* **IT NAMES THE PAINTER** if a `MOD-OWNED` renderer drew for a stretch of the window, or a property
  block overrode a slot on a stretch of it, or a dissolve channel carried a value that **walked**.
* **IT NAMES A BINDING** if the light-probe luminance or the reflection probe identity changed
  across the window while nothing else did.
* **IT KILLS THIS ROUND'S OWN LEADS** if every renderer that drew is game-owned with no block, no
  keyword changed, the probe luminance is flat and the reflection probe never re-bound. In that
  case read the POSE line and the TIMELINE together: **if the view angle swept while every value
  above held still, the decay in the video is the object TURNING**, and the next round must ask the
  user to grab a trap and hold it **dead still** before it measures anything at all — which is a
  test instruction, not a build.
* **IT IS INERT** only if no renderer ever drew (`_rCount` 0 or every DRAWING count 0), in which
  case the window did not contain a held prop and says nothing either way.

### 13.6 Why no remedy shipped this round, and it is forced rather than cautious

Every candidate left is measured *by* the thing that would fix it. Pin a mip and the mip reading is
of the pinned state. Suppress our overlay and the roster reads a subtree with the overlay already
gone. Clear a property block and the per-frame block reading is of a cleared block. This project's
recorded rule is that **a fix gated behind the instrument shipped to test it never runs, so "no
improvement" carries no information** — and the symmetric failure is just as fatal: a remedy
shipped *beside* its own instrument makes that instrument a reading of the post-remedy world.
Round 1 took the same decision in the same words: *"I DID NOT PIN THE CAUSE, SO I SHIPPED NOTHING
THAT CLAIMS TO FIX IT."*

**Multiplayer.** No wire field, no second code path, no per-sub-feature sync setting. This is a
read-and-print instrument that writes nothing at all, so there is nothing to mirror; it rides the
existing post-hush window, which `NetProps` reaches through `PropAnimBelt.Engage` (`NetProps.cs:288`)
and `Release` (`NetProps.cs:550`) for a REMOTE hold exactly as a local one. **Verified from evidence
on the LOCAL side only — the ModBuild 453 log is a single-player session; the mirrored half is
reasoned from those two call sites, not measured.**

### 13.7 A correction to §12.5, and the candidates a decompiled sweep turned up

**§12.5 dismissed `ObjectPosToMaterial` on half of it.** It says the trap's one feeder *"writes only
in `OnEnable` and through `GetComponent<Projector>()`, and the census counts 0 Projectors under this
prop"*. `ObjectPosToMaterial` has **two** branches: `:29` is the Projector one and **`:34` is a
`SkinnedMeshRenderer` branch**, and the 453 hush census reads `2 skin(s)` under this trap. The
`OnEnable`-only part still stands, so it is not a live per-frame writer — but what it bakes is the
object's **world position** into a shader vector, once. A bake taken on the hex and carried into a
palm is a **constant wrong value**: it never moves, so every "did anything move" reading in this
file is consistent with it, which is the same blind spot strand 6 was built for. Section 5 of the
new line measures it, against the prop's live position, in world units.

Other mechanisms found in the decompiled tree with the right *shape* (a multi-second, self-correcting
appearance change on a prop) and their distinguishing facts, none of them yet ruled in or out for
this trap:

| candidate | file:line | what distinguishes it |
|---|---|---|
| `RFX4_ReplaceMaterialByTime` | `GH.Runtime/RFX4_ReplaceMaterialByTime.cs:29-30,36` | Restores the original material and re-arms `Invoke("ReplaceObject", TimeDelay)` on **every `OnEnable`** — a literal "wrong material for N seconds, then correct" machine. **Not in the trap's 15-type histogram**, so it needs a subtree the census did not reach to be live here. It would show on the new line as a **shader/material name change between the roster (arm) and the material table**. |
| `MaterialLoaderData` | `GH.Runtime/MaterialLoaderData.cs:36,71-72` | `Renderer.enabled = false` at `:36`, `sharedMaterials` + `enabled = true` at `:71-72`. Its failure mode is **invisible, never white** — it never shows a placeholder. It is the best explanation on offer for `1 of 2 renderer(s) drawing`, and the roster names which one. |
| mod-side `MaterialLoaderHeal` | `src/GloomhavenVR/Core/MaterialLoaderHeal.cs:435,442,1076` | `ScanInterval=1f` + `MinStuckSeconds=3f` ⇒ a **~4 s** heal latency, which is a suspiciously exact match for the video. But it assigns real loaded materials and carries no white fallback, so it can produce a **dark→correct** settle and not a **white→correct** one. Timing coincidence unless the roster puts the white renderer under a `MaterialLoaderData` entry. |
| `Bootstrap.cs:30` `Texture.streamingTextureDiscardUnusedMips = true` | `GH.Runtime/Bootstrap.cs:30` | The game evicts unused mips process-wide. It is the mechanism that would have made the mip lead right — and it is **moot while this mod forces streaming off**, §13.1(b). Recorded so the next reader does not find it and think it is new. |

**Ruled out by that sweep, with what was grepped:** realtime GI convergence (`DynamicGI`,
`UpdateGIMaterials`, `realtimeLightmap` — none game-side), light-probe re-tetrahedralisation
(`Tetrahedralize` — zero hits tree-wide; `CreateLightProbes.cs:13` runs once per scene load from
`UnityGameEditorRuntime.cs:185`), runtime reflection-probe rendering with time slicing
(`timeSlicingMode` — none; the two `RenderProbe` call sites are unused Rene-Fx demo assets), and
`requestedMipmapLevel` / `ClearRequestedMipmapLevel` / `QualitySettings.streamingMipmaps*` (no game
call site at all).

### 13.8 What round eight did NOT do, and why

* It did **not** build a mip instrument or pin a mip level — §13.1(b) killed the lead by
  measurement before a line was written.
* It did **not** ship the per-eye picture difference §11.4 prescribes. That prescription is
  conditional on every state probe being exhausted, and this round found three unexhausted ones
  inside the existing window (per-frame blocks, keywords, lighting bindings) plus an identity
  question (§13.3) that no picture can answer.
* It did **not** suppress the mod's own overlay on a held prop, which is the obvious move if §13.3's
  second row is the answer. Suppressing it would make the reading that decides it impossible to
  take — see §13.6.
* It did **not** bump `NetProtocol.ModBuild`; the integrator does that once per build.

---

## 14. Round nine — 2026-09-06, against the ModBuild 454 log: the user named the trigger, and a room full of zeroes became a finding

**User, verbatim, after testing ModBuild 454:**

> *"Tritt immer noch auf. Eventuell wichtiger hinweis: Das weiße in der Hand tritt immer auf wenn ich
> die Falle/Truhe aufhebe kurz nachdem der weiße flash auf allen Fallen kam. Es hat also sehr sicher
> was damit zu tun - ist also abhängig zu welchem Zeitpunkt ich es aufhebe."*

Two facts in one sentence, and neither was available to any earlier round. **A white flash runs
across EVERY trap AT ONCE**, and **grabbing shortly after it ALWAYS** produces the white in the hand.

### 14.1 What ModBuild 454's `] [Props] HELD-PROP ROSTER` says, and it is all zero

One hold, `'Trap' Trap`, 360 frames. Anchored on
`^\[Info   :GloomhavenVR\] \[FigureGrab\] \[Props\] HELD-PROP ROSTER`.

| reading | value |
|---|---|
| renderers named | 3 of 3 |
| `[0] …/Trap_BearTrap_PR/Beartrap [SkinnedMeshRenderer]`, game-owned, `'Trap_BearTrap_MAT'` on `Amp_Char_Shader` | **DRAWING 360/360**, isVisible 360, block on **0** frames |
| `[1] …/OcclusionVolume [MeshRenderer]`, game-owned, materials **`<none>`** | DRAWING 0/360 |
| `[2] …/VRFigureHighlight/VROverlay [SkinnedMeshRenderer]` | **MOD-OWNED**, DRAWING **0/360**, **DESTROYED at frame 1** |
| property blocks | **0 of 360** frames, worst 0 slots; all 8 dissolve/cutout channels seen in a block on **0** frames |
| shader keywords | **0 changes** over 24 samples; `mat0 Amp_Char_Shader: 0..0 keyword(s), first [<none>]` |
| light probe L0 luminance | **flat 0.0252** first, last and both ends of the range, over 72 samples |
| reflection probe | **at most 0 probes influenced** this renderer; identity changed on 0 samples; usage `BlendProbes / BlendProbes` |
| stale world anchors | the single vector slot is zero — nothing is baked |
| pose | view angle swept **52.4..130.7 deg**, head distance 4.696..6.815 wu, while every value above held still |

**Three questions are closed by that table and must not be re-opened.**

1. **Our own overlay is exonerated.** Renderer `[2]` is ours, it drew on **zero** of 360 frames and
   was **destroyed at frame 1**. §11.6's argument was right and it is now a measurement rather than
   an argument. Delete the suspicion.
2. **The trap has exactly ONE drawing renderer with ONE material.** `[1]` is the occlusion volume's
   proxy `MeshRenderer` and carries **no material at all**, so it can never draw a picture. Whatever
   the white is, it is on `Trap_BearTrap_MAT` / `Amp_Char_Shader`, or on something feeding that
   shader, or it is a renderer/child that is not present in this hold.
3. **The lighting-binding lead (§12.4 item 3) is dead.** `at most 0 probe(s) influenced this
   renderer` — this renderer has no reflection probe on the hex or in the hand, so nothing is
   re-picked when it moves, and the light probe is flat to four decimal places across the hold.

**And the pose line did its job.** The angle swept 78 degrees while every value held still, which
confirms §13.2: the decay in the video is confounded by the object turning. That makes the pose
line the **control** that lets a real difference be read as a real difference, so it stays.

### 14.2 THE INSIGHT THE USER'S SENTENCE FORCES, AND IT IS THE ONE THIS FILE HAS NEVER HAD

Put the zeroes beside his trigger and they stop being an absence.

The white is **a value LATCHED AT THE INSTANT OF THE GRAB**, and nothing during the hold either
sustains it or removes it. That is exactly what every reading above describes.

**Every instrument in this file — all nine rounds of them, this lane's included — asks whether
something MOVES or CHANGES. Not one of them has ever asked whether the value is the RIGHT ONE.** A
latched wrong value is constant, and constant is what all of them print. `0 of 67 slots moved`,
`0 blocks`, `0 keyword changes`, `flat 0.0252` — every one of those is exactly what a prop frozen
white prints, and exactly what a prop frozen correctly prints too. **They do not discriminate.**

That is why round seven's diagnosis (a latch) was right in SHAPE and was then buried by a
measurement (`0 of 67 changed`) that could not have detected it either way.

### 14.3 What shipped — `] [Props] HELD-PROP HOME TWIN`, a comparison and not a series

The measurement that discriminates is not another series. It is a **comparison**: read the whole
property table of the **held** prop and of **another instance of the same prop kind still standing
on its hex**, **in the same frame**.

* Immune to **phase**, because both sides are read on the same tick — and they must be, because
  every trap is in phase with every other, which is the user's own observation.
* Immune to **pose**, because a material value has no view angle in it.
* It is the one comparison eight rounds never took, and it hands over **the value a fix must
  write** at the same time as it names the channel.

Sections of the new line:

1. **GRAB PHASE.** The frozen animator's `normalizedTime` on the frame this class froze it, captured
   inside `RewindAndStop` before the disable — a disabled `Animator` reports `layerCount 0`, so that
   is the only place it can be read at all. This is the axis the user's sentence is about and no
   round has ever recorded it. Correlated across sessions, a defect that clusters in one band of the
   loop **is** the latch.
2. **HOME TWIN identity**, by hierarchy path, with candidates-of-scanned so a truncation or an empty
   population is visible. Matched on the drawing renderer's **object name and shader**, never on the
   material name — `Renderer.material` appends `" (Instance)"` to a clone, so a material-name compare
   would silently refuse the exact case this round exists to detect. Any candidate that is itself
   under a live belt is refused: a second held prop is hushed and latched the same way, and would
   make the comparison agree for the wrong reason.
3. **SHARED OR INSTANCED**, by material instance id. This settles a tension nobody could resolve by
   argument: if all traps drew one shared asset, freezing our animator could not stop that value
   moving, yet the held table reads static — so either the material is instanced per prop (and a
   per-prop latch is possible) or the flash is not on the material at all.
4. **WHAT MOVES ON THE UNHELD TWIN — the flash itself**, per slot, with move count and the home
   range, and the held prop's own value printed beside it.
5. **HELD vs HOME, SAME TICK** — every slot that ever differed, with **both** values and the home
   range. Each one is a candidate channel carrying its own correct value.
6. **THE REWIND'S OTHER HALF** (§14.5).

**Grep token:** `] [Props] HELD-PROP HOME TWIN`. Anchor on `] `.

* **IT NAMES THE CHANNEL** if a slot DIFFERS between held and home while the twin's own value MOVES
  on that same slot: that slot is the flash, the held prop is stuck at one point of it, and the home
  range gives the value the fix must write.
* **IT EXCLUDES THE WHOLE MATERIAL CLASS** if nothing on the unheld twin moves either. The flash the
  user sees on every trap at once is then not a material property, and the next place to look is
  §14.5 or a global shader value every trap samples.
* **IT IS INERT** if no twin was found — only one instance of the kind in the scenario, or every
  other is itself held. That is a population fact and excludes nothing; the line says so in those
  words rather than printing a clean-looking zero.

### 14.4 A TRAP IN THE OBVIOUS FIX, and it is created by this round's own finding

The natural remedy is "write the home value onto the held prop through the `Engage`/`Release`
ledger". **It has a phase bug.** Every trap is in phase, so at the instant of a grab **the twin is
bright too** — copying its current value copies the flash. Writing the twin's value *every frame*
is worse still: it restores the animation in the hand, which is the thing §6 removed on the user's
explicit instruction.

The value a fix must write is the twin's **RESTING** value, which is the end of the home range the
held prop is **not** stuck at — and that requires watching the twin across a full ~5 s loop, which
is what the new line does. **This is why round nine ships no remedy**: not caution, but that the
number the remedy needs does not exist yet, and a remedy shipped beside its own instrument makes
that instrument a reading of the post-remedy world (§13.6).

### 14.5 A HOLE IN THIS FILE'S OWN REWIND MEASUREMENT, closed this build

`MeasureRewind` has counted **(material, property) slots only** for two builds. An `AnimationClip`
can drive **`GameObject.m_IsActive`** and **`Renderer.m_Enabled`**, so an attention flash authored as
*"switch the glow mesh on for half a second"* carries **no material property at all** — and
`0 of 67 (material, property) slot(s) changed` is what that prints, identically to a clip already at
rest. **That zero has never distinguished the two.** `SnapshotRewindState` / `MeasureRewindState` now
read every renderer's `enabled` and its object's `activeSelf` either side of `WriteDefaultValues` and
print `N flag(s) read, M CHANGED` on the HOME TWIN line. A non-zero count names a different fix from
a material one.

Note this also keeps §12.4's **third** reading of the zero alive and unexcluded: `WriteDefaultValues`
may simply not be landing as an immediate write, distinguishable by a non-zero `anyLayerRate home`
— which the A/B line prints as `0.201/s`. Its lever is `Animator.Rebind()`, and `Rebind()` re-enters
the state machine and fires every `StateMachineBehaviour` on it, which is precisely why §12.3 refused
`Play(hash, layer, 0f)`. **Do not ship `Rebind()` on a prop whose controller can send a rules
message** without excluding that path first.

### 14.6 The mip contradiction, recorded and NOT chased

Round eight's two-integer check reads, on the held trap:

```
5 texture(s) bound, 5 of them streamed, 5 currently BELOW their desired mip level, worst gap 1
```

Round eight's own key says a streamed count of **0** would close the lead for good. **It is 5.** So
per-texture streaming is provably active on this prop while `] [Perf] TEX` reports
`streamingMipmaps=False` because this mod forces it off scene-wide — the same contradiction §13.1(b)
recorded, now confirmed on the prop itself rather than on the scene.

**It is still not the cause and is not being chased**, for two independent reasons: one mip level of
gap cannot turn bronze into white, and the user's trigger is a **clock** (the synchronised flash),
not a load. What it *is* is a live disagreement between two of this repository's own instruments,
and it belongs to the `Perf/TEX` lane. One caveat on round eight's own figure: it is sampled at the
CLOSE of the window, several seconds after the grab, so it does not exclude a larger gap at the
grab itself.

### 14.7 What round nine did NOT do

* It did **not** ship a remedy — §14.4.
* It did **not** ship `Animator.Rebind()` — §14.5.
* It did **not** re-open the overlay, the reflection probe, the property block or the keyword
  leads: §14.1 closes all four on measurement.
* It did **not** bump `NetProtocol.ModBuild`.

**Multiplayer.** Nothing on the wire and nothing to mirror: this build reads and prints and writes
no state at all. It rides the existing post-hush window, which `NetProps` reaches through
`PropAnimBelt.Engage` (`NetProps.cs:288`) and `Release` (`NetProps.cs:550`) for a REMOTE hold exactly
as for a local one. **Verified from evidence on the LOCAL side only** — the ModBuild 454 log is a
single-player session; the mirrored half is reasoned from those two call sites, not measured.

---

## 15. The cleanup — 2026-09-06, what was DELETED and the reading that retired each one

> *"Bitte räume auch direkt den Code auf von allen Versuchen die sich nicht bewahrheitet haben,
> damit wir den Code sauber halten"*

Nine rounds left `PropAnimBelt.cs` at 3401 lines and `PropAnimWatch.cs` at 1944 — **5345 lines for
one unresolved defect**, most of it strands that never fired and probes whose questions were
answered builds ago. After this pass: `PropAnimBelt.cs` **1999 lines**, `PropAnimWatch.cs` **gone**.
**3786 lines removed, 717 added.**

**This section is the obituary. A strand deleted here must not be re-proposed without new
evidence** — that is the whole reason the list is written down rather than left to `git log`.

### 15.1 The rule applied

* **DELETE a strand whose live PRE-COUNT is zero on every prop kind ever measured.** It has never
  had anything to switch off, so by this file's own attribution rule it cannot be why anything
  changed in either direction.
* **DELETE every probe that has ANSWERED, in either direction.** A probe that answered is spent;
  this project has a recorded incident of one blitting every frame for 44,200 ticks after its
  question was settled. The answers live in this document. **The code is not an archive.**
* **KEEP a suppression that answers a complaint the user actually made**, even where it is not the
  painter of the white flash.
* **KEEP anything a gate, a wire test or another class depends on.** Checked before cutting.

**THE SAFETY RULE, applied to every deletion:** the body of each removed method was grepped for
writes. This project nearly latched the wall fade off for ever by deleting a spent `Log*` method
that carried a state write inside it. `PropAnimWatch.cs` was grepped whole for `.enabled =`,
`.material`, `SetFloat/SetColor/SetVector`, `Destroy(`, `.speed =`, `cullingMode =`,
`updateWhenOffscreen =`, `.Play(`, `.Stop(`, `SetActive`, `activeSelf`, `transform.` and
`Shader.Set*`: **not one write**. Its only `transform.` hits are two `r.bounds.center -
r.transform.position` reads and one `lossyScale.x` read. It is a pure instrument, exactly as its
header claimed, and `scripts/check-instrument-writes.py` still passes at its 66-field baseline.

### 15.2 STRANDS — kept and deleted

| strand | verdict | the reading |
|---|---|---|
| 1 — `Animator.enabled = false` (+ `WriteDefaultValues` first) | **KEEP** | Pre-count `1 under the visual, 1 switched off` on every trap reading. It is also the user's own explicit instruction (§6) and the live latch suspect (§14.2). |
| 2 — `Outlinable.enabled = false` | **KEEP** | Pre-count `1 Outlinable, 1 switched off`. **And it answers a complaint the user actually made and repeated**: *"Dieser highlighting/Licht effekt von props ist immer noch in der Hand bemerkbar. Wiederholt!"* It is not the painter of the white flash; it stays anyway, because it is a response to a real report. |
| 3 — `Behaviour`-derived emitters (`Light`, `Projector`, `LensFlare`) | **KEEP, AS A WHOLE CLASS** | The trap reads `0 Light(s) of which 0` on ModBuild 454, but the pre-count is **not** zero on every prop kind: §10.2 records `'GoldPile' MoneyToken` at **3 lights** and §11.1 reads `lights 3 of 3 found, 3 ENABLED before this build wrote anything`. Same user complaint as strand 2. **`Projector` and `LensFlare` have read 0 everywhere and are still kept**, because the class was taken whole on purpose — `Light` derives from `Behaviour` and not from `MonoBehaviour`, ModBuild 151 lost a build to exactly that hole, and re-narrowing the sweep to the one type that has fired so far re-creates it. |
| 4 — particle systems that were PLAYING | **DELETED** | Its PLAYING pre-count is **0 on every reading ever taken**: ModBuild 447 trap `0 playing at the grab` of 8 systems, ModBuild 448 GoldPile `7 of 7 found, 0 playing`, ModBuild 454 trap `0 under the visual of which 0 were PLAYING`. It only ever writes on a system found playing, so it has **never written anything**. Gone with it: `Belt.Particles`, `ParticlesFound`/`ParticlesPlaying0`, `RestoreForeignParticles` and `ParticleScratch`. |
| 5 — `ObjectOcclusionVolume` registration | **KEEP** | Pre-count `1 volume of which 1 ENABLED` on the trap — non-zero, so the delete rule does not reach it. It read its WORKING shape (§11–12) and the defect stood, so it is **neither proven nor disproven**; it is one ledgered bool taken through the game's own `OnEnable`/`OnDisable` pair, and removing a suppression that DID fire would reintroduce a wash nobody has re-tested for. Flagged here so round ten can decide deliberately. |
| — `SkinnedMeshRenderer.updateWhenOffscreen = true` | **KEEP** | Not an animation term at all: it keeps a held prop DRAWN when its stale root-bone bounds leave the frustum. |
| 6 — the rewind (`Animator.WriteDefaultValues`) | **KEEP the call, DELETE its material measurement** | §14.5: `0 of 67 (material, property) slot(s) changed` cannot distinguish "the clip was at rest" from "the clip drives no material", and §12.4's third reading (the write not landing) is still live. The call stays; `RewindTable`, `RwBefore`/`RwAfter`/`RwValid`/`RwValidAfter`, `MeasureRewind`, `AppendRewind`, `Belt.RewindSlotsRead`/`RewindSlotsChanged`/`RewindNamed` are gone, replaced by the renderer/`activeSelf` half that has not answered. |

### 15.3 PROBES — every one deleted, and what it answered

**`PropAnimWatch.cs` — 1944 lines, deleted whole.** Every question it was built to ask is answered
and recorded above:

| its question | its answer | recorded in |
|---|---|---|
| does the clip run in the hand vs on the hex? | `advancing hand=0/1028 home=359/360`, `anyLayerRate home=0.201/s` | §10, §12.1 |
| Chronos / `AreaClock` / `Timeline` | `scene holds 0 AreaClock(s) and 0 Timeline(s)`; `globalClock.timeScale hand=1 home=1` | §3 |
| the SMB `animator.speed` latch | `speed hand=1 home=1` | §3 |
| pose stomps — is our own per-frame pose write erasing the animation? | `poseStomps=0 of 511` (435) and `0 of 169` (454) | §3 |
| `lossyScale`, `cullingMode`, `updateMode`, `Renderer.isVisible`, particle scaling modes | all steady; `worst bounds-vs-transform gap hand=0.237 home=0.237 wu`, identical | §4 |
| its 20-of-57 material table | superseded by the 57/57 read, which then read `0 of 67 moved` | §10.2, §14.1 |
| its HOME window | **superseded** by the HOME TWIN, which compares a *different instance on the same tick* instead of the *same instance seconds later* — strictly better, because the props are in phase | §14.3 |

Its call sites went with it: `PropGrab.Tick`, and six in `GrabbableProp` (`NotifyGrab`,
`WatchingHand`/`NotePoseStomp`, `NotifyGone`, two `NotifyLanded`, `Reset`). The pose-stomp probe's
three write-only fields — `_wrotePos`, `_wroteRot`, `_wroteScale` — went with their only reader.

**Deleted from `PropAnimBelt.cs`:**

| probe | its answer, on which build |
|---|---|
| `EmitPostHushVerdict` and every aggregate behind it (`VPrev`/`VSeen`/`VMoves`/`VLo`/`VHi`, `VLight*`, `VAnim*`, `_vAnimOnMax`…`_vLightIntensityMax`, `_vAnimators`/`_vOutlines`/`_vLights`/`_vParticles`) | 448, 450, 453: animators `0 enabled`, outlines `0 enabled`, lights `0 lit` with a non-zero pre-count, particles `0`, `0 of 67 slots moved`. The subtree is dark and it has been said three times. |
| `AppendOutward` + `SampleOutward` + the occlusion probe state | 450 and 453: `1 volume, 1 enabled beforehand, at most 0 still registered`, `_EnableOcclusionMap 1..1`, `_ObjectOcclusion re-bound on 168 of 169 frames`. Strand 5 reads its working shape; the probe has nothing left to say. **The strand stays; the probe goes.** |
| `SweepForeignEmitters` (a `FindObjectsOfType<Light>()` per verdict) | 450 and 453: `1 near of 40 in the scene, 1 of them lit`, named — a Forest Imp elite's idle point light **on the board**, not on the hand or the rig. Answered. |
| the round-8 property-block arm (`RBlock`, `BlkNamed`, `RBlockFrames`, `RBlockOverrideMax`) | 454: `0 of 360 frame(s) had at least one renderer carrying a block, worst frame overrode 0 slot(s)`. |
| the dissolve-channel arm (`DissolveIds`/`Names`/`Lo`/`Hi`/`Seen`, `AddDissolveChannel`) | 454: all eight channels — `_Cutout`, `_DeathDissolveTop`, `_DeathDissolveBottom`, `_Toggle_Dissolve`, `_Toggle_FlipDissolveDirection`, `_ToggleDissolveFromCenter`, `_DeathDissolvePos`, `_Cutout_Offset` — `seen in a block on 0 frame(s)`. |
| the shader-keyword arm (`SampleKeywords`, `KwMin`/`KwMax`/`KwFirst`/`KwLast`) | 454: `0 change(s) across all materials`; `mat0 Amp_Char_Shader: 0..0 keyword(s), first [<none>]`. |
| the lighting-binding arm (`SampleLightingBindings`, `ReflectScratch`, `_sh*`, `_reflect*`) | 454: `at most 0 probe(s) influenced this renderer`, identity changed on 0 samples, light-probe L0 luminance `first 0.0252, last 0.0252, range 0.0252..0.0252`. §12.4's third still-open item, closed. |
| `AppendStaleAnchors` | 454: `all 1 vector slot(s) are zero, so nothing is baked`. |
| `AppendTimeline` and the 24-bucket series (`Tl*`, `_tlT0`) | 454: every bucket identical on every column except the pose. A series of one value is not a series. **The pose min/max survives as the CONTROL** — the coordinator's call, and the right one: the 454 sweep of `52.4..130.7 deg` with everything else flat is what proves the video's curve is confounded. |
| `AppendMipResidency` | 454: `5 texture(s) bound, 5 of them streamed, 5 currently BELOW their desired mip level, worst gap 1`. Recorded in §14.6 as a live contradiction with `] [Perf] TEX`, and handed to the `Perf/TEX` lane. One mip level cannot turn bronze into white. |
| `EmitRoster`'s own line, `AppendMovers`, `MaxOf`, `Tally` + `TypeNames`/`TypeCounts`/`BehaviourScratch` (the MonoBehaviour histogram) | The histogram answered in §12.5: all 15 distinct types under the trap enumerated, and the one it settled (`CustomObjectPositionToChildMaterials` is NOT on this prop) is written down. |

**What survives of the round-8 roster is its identity half only** — names, shaders, the
MOD-OWNED/game-owned verdict, drawing/visible frame counts — because *that* has not answered: it is
how the twin's lead renderer is picked, and identity is what §14 is about.

### 15.4 Instrument surface after the pass

**Two lines, and that is all.**

* `] [Props] HELD-PROP ANIMATION HUSH` — the grab-edge **pre-counts**. Kept because the pre-count is
  what attributes a strand and what this very section used to decide which strands could go.
* `] [Props] HELD-PROP HOME TWIN` — the round-nine comparison, carrying the roster, the pose
  control, the grab phase, the rewind's renderer/active half, and the held-vs-home diff.
* `] [Props] HELD-PROP HUSH RESTORE` — kept because it **guards a write**, not because it probes the
  defect. Its three falsifier counts must read 0.

`] [Props] HELD-PROP ANIMATION A/B`, `] [Props] HELD-PROP PAINT AFTER HUSH` and
`] [Props] HELD-PROP ROSTER` **no longer exist**. A future round grepping for them will find
nothing; their findings are §§3–14 of this document.

### 15.5 Checked and NOT touched

* **`PropLift.cs`, `HeldProps.cs`, `PropVisualLookup.cs`** — nothing from the flash rounds in them.
* **`PropGrab.cs`** — one call and its comment removed; the rest is the grab feature.
* **`GrabbableProp.cs`** — six call sites and the three pose-stomp fields removed. `EmitWatch` /
  `TickWatch` / `BeginWatch` are the ModBuild 341 **flicker** watch (`] [Props] HOLD WATCH`), a
  different defect that is SOLVED and whose watch guards a fix; left alone.
* **Config dials** — `PropAnimWatch` read none (grepped for `Cfg.`, `Config`, `Bind(`,
  `[FigureGrab]`: no hits). No orphaned key.
* **Wire fields** — none: nothing in nine rounds ever went on the wire (§8, §10.6).
* **`docs/`** — untouched; another lane is working there.

Gates after the pass: `build.sh` 0 warnings, `EXPECT_WARNINGS=0 ci-build.sh`, `check-mirrors.sh`,
`check-frame-order.sh` (11 locked orderings still verify — removing `PropAnimWatch.Tick()` from
`PropGrab.Tick` moved no locked ordering), `patch-inventory.sh check`, `check-hw-verify.py`
(401 marked sites), `check-instrument-writes.py` (66-field baseline unchanged), `wire-tests.sh`
(208 006 assertions).

---

## 16. Round nine, addendum — the only oscillator with no per-instance phase is OURS

A sweep of the decompiled game and of this mod, run to answer one question — *what makes every
trap flash white at the same time?* — returned exactly one mechanism, and it is not the game's.

### 16.1 The finding, verified in our own source rather than taken on report

`OverlayPulse.Update`, `src/GloomhavenVR/Board/FigureGrab/FigureOverlay.cs:1293`:

```csharp
float s = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseHz * Mathf.PI * 2f);
float k = Mathf.Lerp(Floor, Ceil, s);
_mat.color = _base * k;
```

**`Time.unscaledTime` with no per-instance phase offset and no per-instance start time.** Every live
`OverlayPulse` anywhere in the scene therefore evaluates the *identical* argument on the *identical*
frame: they are in **exact lockstep by construction**, not by coincidence of start times.

* `FigureOverlay.cs:1279-1281` — `PulseHz = 0.7f`, `Floor = 0.45f`, `Ceil = 1.0f`.
* `FigureOverlay.cs:1298` — a second absolute-time term, `mainTextureOffset` scrolled from
  `Time.unscaledTime * 0.15f`.
* `FigureHighlight.cs:141` — `GlowTint = new Color(1.0f, 0.62f, 0.26f)`.
* `FigureHighlight.cs:346` — `MakeOverlayMaterial(GlowTint, additive: true)`.
* `FigureHighlight.cs:353` — `var root = new GameObject("VRFigureHighlight");`, parented under the
  prop's visual; `:441` — `root.AddComponent<OverlayPulse>()`.

**A warm amber ADDITIVE pass at full strength over an already-lit bronze trap washes to IVORY** —
which is the word the photometry of the user's video uses for what he is complaining about.

**And nothing else in either codebase has that shape.** Every time-driven prop writer found in the
game carries a per-instance accumulator or start time — `SpawnObjectAnimateMaterial_SMB.cs:30`
(`t += Timekeeper…deltaTime / animTime`), `RFX4_ShaderFloatCurve.cs:77,92` and
`RFX4_ShaderColorGradient.cs:111` (`startTime = Time.time` seeded in `OnEnable`),
`RFX4_LightCurves.cs:38`. **No game code writes a time-varying GLOBAL shader value at all**: every
`Shader.SetGlobal*` in the decompiled tree is a post-FX blit, a wind constant, or the wall-fade
gate. And the outline system, the obvious "all props at once" candidate, is a **held-key** toggle
writing a bool — `WorldspaceUITools.cs:93-100,140-154` — with no schedule and no colour.

### 16.2 The thing that does NOT fit, which is why this is a census and not a fix

The overlay is **single-winner**: the grab driver suppresses every non-winner, so at most **two**
should exist, one per hand. And ModBuild 454 measured the held prop's own overlay
`DRAWING 0/360, DESTROYED at frame 1` — §14.1 exonerated it *for that hold*, and that reading
stands.

For **every** trap to pulse together, overlays would have to be **LEAKING** — one left behind per
prop the player has ever hovered — with each leaked one pulsing in phase because the phase is
absolute time. Ownership is held by the `GrabbableProp` **component**, not by the visual
(`FigureHighlight.cs:189`, `public bool Active => _overlayRoot != null;`), while
`PropVisualLookup.cs` records that `ObjectCacheService._propsCache` is **reference-keyed and
re-keyed on every state sync**, and `PropGrab.cs:243-247` responds by calling `ReleaseAll()` and
rebuilding. Any path that replaces the `GrabbableProp` for a still-living visual hands the new
instance `_overlayRoot == null` while the old `VRFigureHighlight` child is still parented under the
prop, still pulsing, with nothing left holding a reference to destroy it.

**That is a population question with a one-number answer, and no round has ever asked it.**

### 16.3 What shipped — the census, on the HOME TWIN line

`SweepOverlayPulses` counts every live `OverlayPulse` in the scene when the window arms and again
when it closes, and names up to six **by hierarchy path** (every one of these objects is called
`VRFigureHighlight`, so the only thing that distinguishes a leaked one is whose child it is). A
typed `FindObjectsOfType` over one of this mod's own components, twice per verdict and never per
frame.

* **IT NAMES THE FLASH AS OURS** if the count is **above 2**, or if any named entry hangs off a prop
  the player is not currently hovering. Every leaked overlay pulses in phase with every other, which
  is the user's *"weisser flash auf allen Fallen"* exactly — and it would be this mod's own doing.
  Then the fix is the leak, not the flash.
* **IT KILLS THE READING OUTRIGHT** if the count is 0 or 1. The flash on every trap is then the
  game's, and §14's HOME TWIN comparison beside it says whether it lives on a material.

**An assertion in a comment is a hypothesis.** §16.1 is one, and it is written into the source as
one. The count is not.

### 16.4 Why no fix shipped for it

The same reason as §14.4, and one more. Destroying leaked overlays before the census has counted
them makes the census a reading of the post-fix world — and a leak whose *mechanism* is unknown
cannot be fixed by sweeping up its output: the sweep would run for ever, every session, hiding the
path that creates them. **Count first, then fix the path.** If the count comes back at 0 or 1 the
whole reading is dead and nothing was spent on it.

Note also what this does **not** disturb: §11.6 and §14.1 both remain correct that the **held**
prop's own overlay is destroyed at the grab. This lead is about overlays on the props the player is
**not** holding.

---

## 17. Round ten — 2026-09-06, against the ModBuild 455 log: the material class is out, the overlay lead is dead, and the ~5 s loop was never the flash

**User, verbatim, after testing ModBuild 455:** *"Problem besteht unverändert weiterhin."*

Both lines fired and both answered. `] [Props] HELD-PROP HOME TWIN for 'Trap' Trap`, 194 frames,
closed because the prop was put down.

### 17.1 §16's overlay lead is FALSIFIED, by the key §16 shipped with it

```
OVERLAY PULSE CENSUS: 1 OverlayPulse component(s) alive in the SCENE when this window armed
and 1 when it closed, of which 1 were enabled on an active object, named by hierarchy path:
'Maps/J : (2b7572d0-…)/Trap : (efc33169-bc0c-afa1-16c9-3b8cec7433f4)/VRFigureHighlight' PULSING.
```

One. And the single entry hangs off the very trap the player was hovering — GUID
`efc33169-…`, the same GUID as roster renderer `[0]`, i.e. the held prop's own overlay and not a
leaked one. §16.3's key: *"A count of 0 or 1 kills that reading outright."* **It reads 1.**

**Overlays are not leaking and the flash on every trap is not ours.** The reasoning in §16.1 was
sound and remains true as a fact about `OverlayPulse` — it *is* the only oscillator in either
codebase with no per-instance phase — but the population it needed does not exist. The census is
spent and was deleted in the same build; honouring one's own falsifier rather than re-interpreting
it after the fact is the whole discipline this document exists for.

**One bounded caveat, stated so it is not rediscovered as a loophole:** this session contained a
single grab, so the census cannot speak to accumulation across a *long* session. If the user ever
reports the flash getting worse the longer he plays, re-add it deliberately — `git log` has it.
Nothing in the present evidence suggests that, and it is not a reason to keep the probe.

### 17.2 THE REAL RESULT: the whole material class is excluded

| reading | value |
|---|---|
| `SHARED OR INSTANCED` | **1 of the held prop's materials is THE SAME ASSET as the twin's, 0 per-prop instances** |
| `THE TWIN'S ANIMATOR` | **advancing on 193 of 194 frames, loop fraction swept 0.001..0.997** — a full loop |
| `WHAT MOVES ON THE UNHELD TWIN` | **0 of 57 tracked slot(s). NOTHING.** |
| `HELD vs HOME, SAME TICK` | **0 slot(s) EVER DIFFERED** |
| `THE REWIND'S OTHER HALF` | 6 renderer/object flag(s) read, **0 CHANGED** |
| twin population | found among **3 candidates** of 7365 renderers scanned |

A trap standing on its own hex — not held, not hushed, not frozen — runs its **entire** loop and
moves **not one material property**, on a material asset it **shares with every other trap**. And
the held prop's material state is byte-for-byte a board prop's, on every one of 194 frames.

**The picture is not made of this prop's material values, and no fix written on them can change
it.** That is the line's own words and it is now a measurement.

### 17.3 TWO ASSUMPTIONS DIE WITH IT, and both were load-bearing since round seven

1. **The whole material class is out.** Every remaining material hypothesis in §§3–14 — a latched
   slot, a stale bake, a shared-vs-instanced write, a dissolve ramp — is excluded by a single
   reading taken on a population that was never suppressed.

2. **THE ~5 s IDLE LOOP IS NOT THE FLASH.** This has been carried unexamined for four rounds. The
   clip advances through its **entire** range (`0.001..0.997`) while **nothing about the material
   changes**, so what it drives is **BONES on a SkinnedMeshRenderer** — a bear trap's jaws opening
   and closing — and not a brightness. `clipRate 0.202/s` is the jaws' clock, not the flash's.
   **Stop treating it as the flash's period.** Whatever flashes every trap at once has a different
   cause and possibly a different period, and **no round has ever measured that period** — which is
   why every change the new arm records carries a timestamp.

   This also re-frames round seven retrospectively. Strand 6 (`WriteDefaultValues` before the
   freeze) was built on the premise that the freeze latches a bright frame of that clip. The clip
   has no bright frame. Strand 6's `INERT` reading (§14) was correct and now has a reason.

### 17.4 What shipped — the twin's OBJECT GRAPH, per frame, and the lighting comparison

The previous build's own reading key named the next place to look: *"a renderer or child GameObject
being ENABLED — an animator can drive `m_IsActive` and `m_Enabled`"*. It then measured exactly that
**on the held prop, across the rewind** — a two-sample read on an object that was already frozen.
**It has never been measured on the TWIN, over time.** If the flash is authored as *"switch the
glow mesh on"*, the twin is the one place it is visible and a hushed prop in a hand is precisely
where it is not.

`AppendTwinGraph` reports, per frame over the whole window, on the **unheld** twin:

* every child object's `activeSelf`, tracked **by identity** and named on change;
* every renderer's `enabled` and `activeInHierarchy`, likewise;
* the child-object COUNT and the renderer COUNT, min..max — so an object **instantiated** on the
  flash is caught as well as one toggled;
* each renderer's material COUNT and its material 0 **instance id** — a swapped `sharedMaterials`
  entry changes nothing in a property table and is invisible to every read-back in this file;
* and **every change is stamped with its frame, its elapsed time and its absolute
  `Time.unscaledTime`**, so the flash's own clock is read off the line rather than assumed, and two
  prop kinds' lines in one session can be compared for simultaneity — which is what *"auf allen
  Fallen"* claims.

`AppendLightingCompare` re-adds an arm deleted in §15, **deliberately, and as a different
measurement**. The deleted one read the light probe **during the hold only** and reported it flat.
**Flat is not the same as correct.** A prop carried to the eye leaves the probe volume it was
authored inside, and a constant-but-*wrong* ambient is invisible to every "did it move" reading in
this file — the same blindness §14.2 named for material values. Read as a **comparison against the
twin on the same tick** it costs one more column on a comparison that already exists: the
interpolated light-probe L0 luminance for both props, their ranges, the worst per-tick difference,
and the reflection-probe count on each side (the held prop read `at most 0` on ModBuild 454; if the
twin reads more, the prop in the hand lost a probe when it was carried).

**Grep token:** `] [Props] HELD-PROP HOME TWIN`, sections `THE TWIN'S OBJECT GRAPH` and
`LIGHTING, HELD vs HOME`.

* **WORKING / IT NAMES THE FLASH** — the graph arm reports a non-zero CHANGES count. The named
  events say which object, which direction and when; the interval between repeats of the same event
  **is the flash's period**, measured for the first time.
* **WORKING / IT NAMES A DIFFERENT FIX** — the lighting arm reports a large worst-difference, or a
  HOME reflection-probe count above a HELD count of 0. The ivory is then **lighting, not paint**,
  which no round has costed.
* **INERT** — no twin was found (a population fact, excluding nothing), or no drawing renderer on
  one side, in which case the lighting arm says `NOT TAKEN` rather than printing a clean zero.
* **STILL BEYOND THE INSTRUMENT — and this is the branch that ends the state-probe era.** If the
  twin's graph reports **0 changes** over a full loop *and* the lighting difference is near zero,
  then together with §17.2 **nothing about this prop's own object graph or its lighting flashes**.
  At that point the exclusion is complete and **the next round must measure the PICTURE rather than
  the state** — a sampled read-back of the rendered pixels over the prop, held versus home. **Do
  not invent an eleventh state probe.** This document has ten of them and every one reads zero.

### 17.5 Carried forward, and what one sample is worth

* **THE GRAB PHASE recorded its first sample: `normalizedTime 1.3407`, loop fraction `0.341`**, on a
  hold the user reports as white. **One sample decides nothing** — the field exists to be correlated
  across sessions, and a single number cannot distinguish a band from a coincidence. Keep printing
  it. (And note §17.3(2): the loop it is a fraction *of* is the jaws' loop, so this number is only
  a proxy for "when in the prop's own cycle" and not for "when in the flash".)
* **The pose control did its job again:** the view angle swept `78.8..133.8 deg` while every value
  held still. It stays.
* **The §15 cleanup landed as ModBuild 455 and is not undone.** One arm deleted there was re-added
  this round — the light probe — and §17.4 says why it is a different measurement rather than a
  reversal.
* **The overlay-pulse census was deleted this round**, having answered. §17.1 is its obituary.

### 17.6 What round ten did NOT do

* It did **not** ship a remedy. There is nothing left to write a remedy *on*: the material class is
  excluded, the object-graph class is unmeasured, and a fix aimed at an unmeasured class is the
  shape this document has recorded nine times.
* It did **not** re-open the overlay, the reflection probe as a *during-hold* reading, property
  blocks, keywords, mip streaming or the occlusion map. §§13.1, 14.1, 17.1 close all of them on
  measurement.
* It did **not** bump `NetProtocol.ModBuild`.

**Multiplayer.** Nothing on the wire and nothing to mirror: this build reads and prints and writes
no state. It rides the existing window, which `NetProps` reaches through `PropAnimBelt.Engage`
(`NetProps.cs:288`) and `Release` (`NetProps.cs:550`) for a REMOTE hold exactly as for a local one.
**Verified from evidence on the LOCAL side only** — the ModBuild 455 log is a single-player session;
the mirrored half is reasoned from those two call sites, not measured.

---

## 18. Round eleven — 2026-09-06, against the ModBuild 456 log: the "latched value" model is WRONG, and the search moves into the game's own source

Two user reports arrived this round and each one overturns something.

> **(a)** *"Wenn ich es länger in der Hand halte tritt es auch so auf das der prop weiß wird in der
> hand. Es ist wie der flash nur deutlich verlangsamt und nicht ganz flüssig wie beim flash auf dem
> Spielbrett."*
>
> **(b)** *"Ich mein dieser 'Blitz' ist ja ein voll spiel gewolltes Highlighting kein Fehler um auf
> die Fallen und Truhen aufmerksam zu machen. Also müsstest du es doch auch im originalspiel finden
> können"*

### 18.1 What ModBuild 456 read, and it is the "still beyond the instrument" shape

| arm | reading |
|---|---|
| `THE TWIN'S OBJECT GRAPH` | 10 child objects, 2 renderers at arm; counts **10..10** and **2..2**; **CHANGES: 0** |
| `LIGHTING, HELD vs HOME` | 29 samples; HELD L0 `0.0252` over `0.0252..0.0252`; HOME identical; **WORST DIFFERENCE 0**; reflection probes **0 both sides** |
| `THE REWIND'S OTHER HALF` | 6 flags read, **0 CHANGED** |
| `GRAB PHASE` | `normalizedTime 2.2641`, fraction **0.264** (second sample; §17.5's first was 0.341) |

So the object graph is out and lighting is out, on top of §17's material exclusion.

### 18.2 REPORT (a) CONTRADICTS THE MODEL THIS FILE HAS RUN ON SINCE ROUND NINE

Read it term by term, because every term matters and three of them are new:

* **"tritt es auch so auf"** — the effect is **NOT absent** in the hand. It happens.
* **"wie der flash"** — it is the **same** flash, not a different artefact.
* **"deutlich verlangsamt"** — **distinctly slowed**. That is a RATE.
* **"nicht ganz flüssig"** — it **stutters**, where on the board it is smooth.

**A LATCHED VALUE CANNOT STUTTER.** §14.2's model — *"the white is a value latched at the instant of
the grab, and nothing during the hold either sustains it or removes it"* — is **wrong**. It was the
best reading available of a room full of zeroes, and the user's own observation retires it.
Something drives the effect in the hand at a **reduced and irregular update rate**. That is a
signature with a NUMBER in it.

**And it retires §14's reading of the zeroes with it.** "Nothing moved" is equally consistent with a
term that moves *between* our sample points, or on a class we sample at a coarser cadence than it
updates, or on a class we do not sample at all. The zeroes never proved stillness; they proved our
sampling did not catch movement.

### 18.3 REPORT (b) IS THE METHODOLOGICAL CORRECTION, AND IT IS FAIR

Eleven rounds have tried to catch a **deliberate game feature** with instruments, when the mechanism
is sitting in `decompiled/`. The game was read this round rather than the logs, and three leads came
out of it. All three are measured by this build; none is assumed.

**Excluded first, so nothing is spent there:**

* **The hold-to-highlight system is not it.** `WorldspaceUITools.Update:91-99` turns
  `ActivateAllOutlines(true)` on while `InputManager.GetIsPressed(KeyAction.HIGHLIGHT)` and off on
  release — a held key with no schedule. And this mod never references `KeyAction.HIGHLIGHT`,
  `ActivateAllOutlines` or `OutlinesEnabled`; a grep of `src/` returns zero hits, so we are not
  pressing it either.
* **No time-varying GLOBAL shader property exists in the game.** Every `Shader.SetGlobal*` in
  `GH.Runtime` is a texture bind, `ToggleWallFade`, or a grab-texture scale. §16.1's claim holds,
  verified independently.

### 18.4 The three game-source leads, and a correction to the brief that raised them

**(1) CHRONOS.** `decompiled/ThirdParty/Chronos/` — `Timekeeper`, `Timeline`, `GlobalClock`,
`AreaClock`, and `AnimatorTimeline` / `AnimationTimeline` / `RewindableParticleSystemTimeline`. It is
a per-object time-control library and **the only mechanism found anywhere that can make an effect
run slower and not fluid while nothing about its state changes** — which is report (a) word for word.

**CORRECTION TO THE RECORD: round one DID look at Chronos.** §3 records
`scene holds 0 AreaClock(s) and 0 Timeline(s)` and `globalClock.timeScale hand=1 home=1` from the
ModBuild 435 log, and that is why the lead is small rather than new. What that reading did **not**
cover is a `Timeline` on a **PARENT** of the prop — the census that produced it was rooted at the
prop's visual, and a Chronos clock governs a subtree **from above**, which is precisely the shape a
bottom-rooted census cannot see. It was also taken in a different scenario. Both gaps are closed
this build, by walking **up** with `GetComponentInParent<Timeline>()` on the held prop *and* on the
twin and printing both clocks side by side.

**(2) `IdleSMB` WRITES `animator.speed`, AND OUR HUSH CAN STRAND IT.**
`decompiled/GH.Runtime/IdleSMB.cs:30,36,40` — `ChangeSpeed()` writes `m_Animator.speed` from
`Timekeeper.instance.m_GlobalClock.timeScale` or `1f / GameSpeedIncreaseAmount`; `OnStateEnter`
calls it; `OnStateExit:50-53` resets `animator.speed = 1f`; and `Awake:11` subscribes to
`SaveData.Instance.Global.GameSpeedChanged`, so it can write **at any time during a hold**.
**Disabling an Animator mid-state means `OnStateExit` never fires**, so whatever speed was last
written stands for the whole hold. §3 measured `speed hand=1 home=1` on ModBuild 435 — *before* the
hush existed. It is one float, and this build reads it per frame on both sides.

**(3) THE PHASE MAY BE SPATIAL.** The game seeds shader effects from **world position**:
`ObjectPosToMaterial` (`_ObjPos`, OnEnable only), `PosToMat` (`_ObjPosY`, every frame),
`ZephyrAnim`, `CustomObjectPositionToChildMaterials` (`_FadeSourcePos`). Their existence is evidence
that these shaders compute time-varying effects **seeded by a per-object world position**. The trap
carries `ObjectPosToMaterial`, whose `rendererTypeProjector` path goes through
`GetComponent<Projector>().material` and **cannot run here — 0 Projectors censused** — and whose
vector slot ModBuild 454 read as **zero**. A shader phase seeded by a position that was never
written is invisible to every reading this file has ever taken. `Amp_Char_Shader` is compiled into
the bundles and its source is not in the repository, so this stays a hypothesis. **One experiment
distinguishes it: hold the prop DEAD STILL for several seconds, then move it.** If the whiteness
runs on regardless the phase is a clock (leads 1–2); if it tracks the movement the phase is spatial.

### 18.5 What shipped — five arms, all cheap reads, no capture subsystem

**A. THE RE-ASSERT WATCH.** Per frame over the ledger: did anything this class switched off come
back **ON**? Every edge is named with its frame and its clock, and the **intervals between rising
edges** are printed. This tests the first suspect for the stutter, which is **ours**:
`RescanFrames = 45` re-applies the suppression on a cadence, and a suppression re-applied on a
cadence against a writer that re-asserts in between **is** a staircase.

**And it closes a hole the §15 cleanup opened.** That pass deleted the per-frame enabled-state counts
(`_vAnimOnMax`, `_vOutOnMax`, `_vEmitterOnMax`) as spent, on readings that said 0. Those readings
were correct about the hold **as a whole** and structurally blind to a rising edge between two
rescans, because *a maximum over a window says nothing about when*. Re-added as **edges with clocks**
rather than as maxima.

**B. THE STANDING BOARD OBSERVER — `] [Props] BOARD PROP STANDING WATCH`.** Every other arm in this
file is gated on a **hold** and runs for under three seconds, but the flash is a **board event the
player watches and then reacts to by grabbing**. **If it recurs less often than the window is long,
`CHANGES: 0` means "the flash did not happen while we were looking" — an ABSENCE, not an
EXCLUSION**, and this file has confused those before. The twin found for a comparison is **promoted
when that window closes** to a standing watch on the same board prop, running 3600 frames (~40 s)
with **no grab required**. Promotion is free — the prop was already found — and the cost is a handful
of component reads per frame on one prop. It watches renderer/active state, material identity,
animator state, child and renderer counts, and **`Outlinable.OutlineParameters.Enabled` and its
Colour** — the boolean the game writes in nine places, an instantaneous *"Aufblitzen"* rather than a
curve, which round five killed **on the held prop** (where we disable the component outright) and
which **has never been measured on a prop standing on the board**. It prints its own frame count and
duration so **silence cannot be mistaken for a watch that never armed**.

**C. THE KNOWN ASYMMETRIES.** Ten rounds compared what the **game** writes and every one of those now
reads identical. What has never been printed is the set of differences **we ourselves create**:
layer, shadow casting, receive shadows, light/reflection probe usage, motion vectors,
`allowOcclusionWhenDynamic`, `probeAnchor`, material count, `updateWhenOffscreen`,
`skinnedMotionVectors`, skinning quality, `lossyScale`, bounds size, and the **sign of the
`localToWorldMatrix` determinant** — a held prop is mirrored for the left hand, and a negative
determinant flips every normal and every winding. **And the section names the suppression to read
first:** strand 5 unregisters the held prop from `TilesOcclusionGenerator`, so the prop is no longer
*drawn into* the global `_ObjectOcclusion` map while it still *samples* it — and if that map darkens,
a prop absent from it is **undarkened, i.e. brighter than the same prop on the board**. That is a
whiteness with no material, no component and no lighting behind it, it is **ours**, and it is the
only asymmetry in the list this class created for a defect it did not fix.

**D. THE CLOCKS AND THE ANIMATOR SPEED** — leads (1) and (2), measured: Timekeeper presence,
`globalClock.timeScale`, scene-wide `Timeline` and `AreaClock3D` counts, the clock governing the held
prop and the twin walked **up** from each, and `Animator.speed` per frame on both sides with its
range and change count.

**E. THE MOTION TRACE** — lead (3) and the hardware experiment: path length, worst single-frame step,
the **longest run of frames moving under a millimetre** (in frames and in seconds), and the first and
last world position, so the user's "hold it dead still" report and this log can be lined up.

**AND THE FRAME RATE, printed beside every period.** The game's `IEffectBlink`
(`GH.Runtime/WorldspaceUI/IEffectBlink.cs`) loops `m_BlinkInterval = 0.5f` through Chronos, and our
own 45-frame rescan is **0.5 s at 90 Hz** — within a frame or two of each other. Any measured period
near half a second is therefore ambiguous unless it is reported in **both units**. It is: a period
locked to **45 frames** as the frame rate varies is **ours**; one locked to **0.5 s** as the frame
rate varies is the **game's**.

### 18.6 How to read it

**Grep tokens:** `] [Props] HELD-PROP HOME TWIN` (sections `THE RE-ASSERT WATCH`,
`THE GAME'S OWN MACHINERY`, `THE KNOWN ASYMMETRIES`) and `] [Props] BOARD PROP STANDING WATCH`.

* **WORKING / NAMES THE STUTTER AS OURS** — rising edges in the ledger at intervals locked to 45
  frames. The fix is then in this file.
* **WORKING / NAMES A CLOCK** — two different Chronos timelines, or a HELD `Animator.speed` below the
  HOME speed. Lead (1) or (2), with a number.
* **WORKING / NAMES THE BOARD FLASH** — non-zero changes on the standing watch. The intervals are
  **the board flash's own period, measured for the first time**; four rounds assumed the idle clip's
  ~5 s and §17 proved that clip drives bones. The hand's slowed version can then be stated as a
  **ratio** against it, which is exactly what report (a) claims.
* **WORKING / NAMES AN ASYMMETRY** — a differing renderer setting, a negative determinant, or the
  occlusion registration.
* **INERT** — no twin (a population fact), no animator on one side, or the standing watch never
  arming (in which case no line prints at all, which is distinguishable).
* **STILL BEYOND THE INSTRUMENT** — every arm zero, both clocks `<none>`, both speeds 1, the standing
  watch silent over 40 s. Then the board flash is not state at all, and the next round measures the
  **picture**: a sampled read-back of the rendered pixels over the prop, held versus board, at the
  right stage (after post, not from a camera of our own), asynchronously, on a tight cadence, with
  **the cost printed rather than asserted**. That is deferred deliberately this round: the period it
  would serve is obtainable more cheaply from state we already touch, and a new capture subsystem
  shipped beside four other new arms would be unattributable.

### 18.7 What round eleven did NOT do

* It did **not** ship a remedy. Report (a) says the effect has a **rate**, and no rate has been
  measured yet; a fix aimed at an unmeasured rate is the shape this document has recorded ten times.
* It did **not** build the picture read-back — §18.6, last bullet, with its design constraints.
* It did **not** re-open the material class, the object graph, lighting, the overlay, property
  blocks, keywords, mip streaming or the occlusion **map** (the occlusion **registration** is named
  as an asymmetry, which is a different claim). §§13.1, 14.1, 17.1, 17.2, 18.1 close them.
* It did **not** bump `NetProtocol.ModBuild`.

**Multiplayer.** Nothing on the wire and nothing to mirror: this build reads and prints and writes no
state. The standing observer runs above the feature gate in `PropGrab.Tick` and watches a board prop
that is nobody's hand; the hold-gated arms ride the existing window, which `NetProps` reaches through
`PropAnimBelt.Engage` (`NetProps.cs:288`) and `Release` (`NetProps.cs:550`) for a REMOTE hold exactly
as for a local one. **Verified from evidence on the LOCAL side only** — the ModBuild 456 log is a
single-player session; the mirrored half is reasoned from those two call sites, not measured.

---

## 19. Round twelve — 2026-09-06, against the ModBuild 457 log: an EXPERIMENT, not a probe. Strand 5 is OFF.

**User, after testing ModBuild 457:** *"Tritt immer noch auf."*

Twelve rounds of instruments have excluded every class they could reach. This round stops measuring
the one asymmetry **we created ourselves** and **removes it**, with the expected outcomes written
down here *before* the test.

### 19.1 What the five arms returned, and what each one closes

| arm | reading | verdict |
|---|---|---|
| `THE RE-ASSERT WATCH` | 4 rescans (window frames 45, 90, 135, 180); **LEDGER EDGES 0 total, 0 RISING** | **THE WRITE WAR IS CLEARED.** Nothing re-enables what the hush switches off, so the stutter is not our rescan fighting a foreign writer. It clears the *war*, not the *cadence*. **ANSWERED — retired this build.** |
| frame rate | **38.5 fps measured**, so 45 frames = **1.168 s** | The `IEffectBlink` ambiguity (§18.5) dissolves at this rate: 0.5 s and 1.168 s are not confusable. Printing the rate was the thing that settled it. |
| `THE KNOWN ASYMMETRIES` | `lossyScale` 1,1,1 both sides; **determinant 1 vs 1**; bounds 1.124 vs 1.33 wu; **SETTINGS THAT DIFFER: 1**, namely `updateWhenOffscreen held=True home=False` | **THE MIRRORING HYPOTHESIS IS DEAD** — no negative determinant, so no flipped normals and no flipped winding. |
| `MOTION` | 188 samples, path 6.308 wu, worst step 0.7837 wu, **longest sub-millimetre run = 2 frames (0.05 s)** | **The user never held it still**, so his experiment did not happen and the clock-versus-spatial question (§18.4 lead 3) is **still open**. Nothing may be read into this. |
| `] [Props] BOARD PROP STANDING WATCH` | **NOT ONE LINE IN THE LOG** | §19.2. |

### 19.2 WHY THE STANDING WATCH NEVER PRINTED, AND IT IS MY OWN DEFECT

It armed. `PropAnimBelt.TickBoard()` is wired into `PropGrab.Tick` (line 197) and the twin was
found. **What it never did was CLOSE.** The window was `BoardFrames = 3600`, and 3600 frames was
chosen against an assumed **90 Hz**. The rig measured **38.5 fps** *on the very same log line*, so
the window was **93 seconds** — and the session ended first. Every escape path then discarded the
observation **silently**: `Reset` cleared `_boardArmed`/`_boardLead` without emitting.

**That is exactly the failure the arm was written to prevent**, committed inside the arm itself. A
frame budget is a time budget with an unstated assumption about the frame rate inside it.

Three fixes, all the same lesson:

1. **The window is in SECONDS now** — `BoardSeconds = 20f`, frame-rate independent.
2. **It arms at TWIN-FIND, not at window close.** Waiting for the verdict to close means a hold that
   ends the session never promotes at all.
3. **`Reset` emits before it clears**, and `EmitBoard` zeroes its own frame count so a later `Reset`
   cannot print a stale observation twice.

The line itself now states all of this, so the next reader does not have to rediscover it.

### 19.3 THE EXPERIMENT: STRAND 5 IS OFF

**What strand 5 did.** For the length of a hold it unregistered the prop from
`TilesOcclusionGenerator` (through `ObjectOcclusionVolume.enabled = false`, whose `OnDisable` *is*
`RemoveObjectRenderer`). So the held prop was **no longer DRAWN INTO** the global
`_ObjectOcclusion` map while it went on **SAMPLING** that map. **A prop absent from a darkening map
is UNDARKENED — brighter than the same prop standing on the board**, with no material, no
component and no lighting behind the difference. Every one of those three has now been measured
identical (§17.2, §18.1), which is what leaves this standing.

**It is the only asymmetry in this investigation that this mod created itself**, it is live on the
trap (pre-count 1 volume, 1 enabled), and it was created for a defect it did not fix.

**THE ATTRIBUTION, VERIFIED FROM `git log` RATHER THAN ASSUMED.** `30058ced`
*"fix(props): the held-prop light effect is painted by a camera, not by the prop"* was authored
against `ModBuild = 448` and shipped to hardware in `d9005f02`, **ModBuild 449**.

**THE VOCABULARY CORRELATION, STATED PRECISELY RATHER THAN FLATTERINGLY.** Against 447 the user
wrote *"die Spiel-Highlighting Animation von Fallen und Truhen (dieser **weisse Schimmer**)"*;
against 448, *"dieser **highlighting/Licht effekt** … Wiederholt!"*; and after 449 shipped, the
first single-player test produced *"Fallen und Truhen **werden** immer noch manchmal **weiß**"*.
The word *weiss* is present at 447 — as an **adjective on a shimmer**, i.e. a movement. What
changes after 449 is the grammar: the props **become white**, a **state**. That is a real shift and
it is worth recording; **it is a correlation and not a proof**, and it would be worth exactly
nothing without the mechanism above.

**WHY SWITCHING IT OFF RISKS NOTHING THAT HAS EVER BEEN DEMONSTRATED.** Round six's *"the painter is
a camera"* was a hypothesis. Round eight read strand 5's own **WORKING** shape — `1 volume, 1
enabled beforehand, at most 0 still registered`, the map live on 168 of 169 frames — **and the
defect stood**. Nothing it was introduced for was ever confirmed.

**HOW IT IS OFF.** Not behind a dial the user has to find, and not as an instrument: `Apply` now
calls `CountEmitters<ObjectOcclusionVolume>` instead of `TakeEmitters<…>`. The volumes are still
**counted** — the pre-count is what proves the experiment ran on a prop that actually had one — and
they are **not written and not ledgered**, so the restore has nothing to hand back and cannot leave
anything behind.

**THE SINGLE LINE A READER GREPS TO CONFIRM IT:**

```
grep '^\[Info   :GloomhavenVR\] \[FigureGrab\] \[Props\] HELD-PROP ANIMATION HUSH' Player.log \
  | grep 'STRAND 5 OFF - NULL PERTURBATION'
```

The clause reads `*** STRAND 5 OFF - NULL PERTURBATION ***` and states the found/enabled counts and
that **0** were switched off. The asymmetry section on the HOME TWIN line carries the same fact from
the other side: **`ObjectOcclusionVolume registrations SUPPRESSED: 0`** of N found.

### 19.4 THE EXPECTED OUTCOMES, WRITTEN DOWN BEFORE THE TEST

| what the user reports | what it means | what happens next |
|---|---|---|
| **the white is GONE** | strand 5 was the painter. A prop unregistered from a darkening map is undarkened, and that is the whole defect. | Twelve rounds end. Strand 5 is deleted permanently, with §19 as the reason, and §11's account is corrected: the occlusion map was not the painter *on the board* — **removing the prop from it was the painter in the hand**. |
| **the white is UNCHANGED** | strand 5 is excluded **by experiment** rather than by argument. | It is deleted outright under §15.1's rule ("a strand whose live pre-count is zero on every prop kind" does not reach it, but "a probe that answered" does — and an experiment that returns *no effect* answers). The search then moves to the **picture** (§18.6, last bullet), which is now the only unexhausted class. |
| **the white is WORSE, or appears where it did not** | strand 5 was masking something. | That is information too, and it is the only outcome that argues for keeping the strand. Say so; do not quietly revert. |

**A precondition on all three:** the hush line must read a **non-zero found count**. A prop that
never carried an `ObjectOcclusionVolume` could not have been affected either way, and a report about
such a prop says nothing about the experiment. The 457 log reads `1` for the trap, so the trap is a
valid subject.

### 19.5 `updateWhenOffscreen` — the other asymmetry, and it cannot brighten anything

`SETTINGS THAT DIFFER: 1, namely updateWhenOffscreen held=True home=False`. This is the last
remaining thing we make different, so it deserves a plain answer rather than a hanging thread.

**It cannot brighten anything.** `SkinnedMeshRenderer.updateWhenOffscreen` changes how the renderer's
**bounds** are computed — from the true skinned vertices every frame instead of from the root bone's
authored bounds. Bounds feed **culling**: whether the renderer is submitted at all. They do not feed
shading, lighting, materials, keywords or any shader input, and a renderer that *is* drawn is drawn
with identical shading either way. That is also exactly why it is set: it keeps a held prop DRAWN
when its stale root-bone bounds leave the frustum, and a frozen animator makes those bounds staler
still. The `bounds 1.124 vs 1.33 wu` difference on the same line is the *consequence* of that
setting and is consistent with it.

**One caveat, and it is already measured:** bounds also decide the point at which light-probe and
reflection-probe selection is sampled. The lighting comparison reads `WORST DIFFERENCE on any
sampled tick 0` with `0` reflection probes on both sides, so that path is closed too.

### 19.6 What round twelve did NOT do

* It did **not** build the picture read-back. The experiment supersedes it this round: if the white
  goes, no picture is needed; if it stays, the picture is the next round with its constraints
  already written in §18.6.
* It did **not** put strand 5 behind a config dial. An experiment the user has to opt into is an
  experiment that does not get run.
* It did **not** delete strand 5 outright yet. Deleting before the result would make the result
  unreadable — "we removed it and also removed the ability to tell whether removing it mattered".
* It did **not** widen the standing watch's trigger blindly: §19.2 names the actual cause.
* It did **not** bump `NetProtocol.ModBuild`.

**Multiplayer.** Strand 5 going quiet reaches a peer's mirrored copy **by construction** and needs no
wire field: `NetProps` calls `PropAnimBelt.Engage` (`NetProps.cs:288`) and `Release`
(`NetProps.cs:550`) for a REMOTE hold, so a mirrored prop goes through the same `Apply` and simply
has one fewer thing done to it. There is nothing to keep in step because there is now nothing
written. **Verified from evidence on the LOCAL side only** — the ModBuild 457 log is single-player;
the mirrored half is reasoned from those two call sites.
