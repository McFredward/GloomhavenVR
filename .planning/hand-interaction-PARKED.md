# PARKED — hand interaction with scene VFX and hangings (ModBuilds 430–432)

**Status: PARKED by user ruling, 2026-09-05, at ModBuild 432.** The code below existed and built
clean at `49be2a67`. A separate lane removes it. This file is the record that makes it resumable:
what each subsystem did, every number that was *derived* rather than chosen, every defect that was
paid for once, and every hazard that would otherwise be re-learnt the hard way.

**The ruling, verbatim:**

> *"Das ganze System macht zu viele Probleme. Dokumentier was du bisher gemacht hast, falls wir es
> später wieder aufnehmen wollen, dann entferne jegliche FX-Interaktion außer die erste mit dem
> Stoff die schon implementiert war."*

**"Die erste mit dem Stoff die schon implementiert war" was settled with the user after the brief:
it means `FigureClothHands` — a HELD figure's own clothing reacting to the other hand, ModBuild
288 — and that lane alone. The scenery-curtain lane (`SceneClothHands`, ModBuild 322) goes with the
rest.** Note that it is therefore documented below as a *removed* subsystem even though it was never
itself reported broken; the user chose the figure-clothing system alone.

A second report arrived from hardware on the ModBuild 432 build and is part of the same ruling:
**the hanging solver adopted a DOOR LEAF** — see §7.0, which is the single most useful entry in
this document for anyone resuming the work.

This is the continuation of candidate **3b** of [`scene-interactables-PARKED.md`](scene-interactables-PARKED.md)
(the ModBuild-287 survey that was itself parked mid-question). Read that file first if you are
picking this up cold: it holds the source-level reconnaissance — what a `Cloth` can and cannot be
attached to, why `PhysicsController`'s scene-wide cloth sweep is dead code, and why vines and
banners sway from **global vertex-shader wind** that no collider can touch. Two of its conclusions
were later **overturned by measurement** and both corrections are recorded in §8 below.

---

## 1. What survived and what was removed

| subsystem | file(s) | verdict |
|---|---|---|
| Free-hand disturbs a **held figure's** clothing | `Board/FigureGrab/FigureClothHands.cs` | **KEPT** — the original, ModBuild 288. This and only this. |
| Hands disturb **scenery `Cloth`** (curtains) | `Hands/SceneClothHands.cs` | **REMOVED** — ModBuild 322 |
| Hands disturb **particle effects** | `Hands/SceneVfxHands.cs`, `Hands/VfxFlow.cs` | **REMOVED** — ModBuilds 328, 430, 431, 432 |
| Hands swing **hangings / banners** | `Hands/SceneHangingHands.cs` | **REMOVED** — ModBuild 432 |
| "Is this object part of a figure?" | `Hands/SceneryActors.cs` | **REMOVED with its callers** — see below |

`SceneryActors.cs` was written in ModBuild 432 to fix a real defect (§7.8) and its only two callers
are `SceneClothHands` and `SceneHangingHands`. With both of those gone it has no consumer and goes
with them — but **its finding must not go with it**, which is why §7.8 records the derivation in
full. The two-writer corruption it guarded against is moot only for as long as `FigureClothHands` is
the sole writer of `Cloth.sphereColliders` in the tree. **Any future class that writes that array
needs §7.8 back before it writes a line.**

### What is NOT being removed, and must not be confused with it

The removal commit will sit directly beside this work in the history. None of the following is FX
interaction and none of it is in scope:

- **Grabbable health doors** — `Board/FigureGrab/ActorPropBody.cs`. A door with an attached actor is
  a grab target, not an FX interaction.
- **The ModBuild 428/429 door-open restoration** and its `[Compat] DoorAnimateOffscreen` belt. That
  round *removed* a hiding workaround and gave the game its own door animation back; it is the
  opposite of this work.
- **`DoorLightPlates`** — the user's own 2026-08-25 request (*"die fliegenden leuchtenden Vierecke
  im gesamten Spiel unsichtbar machen, aber ohne ihr Licht zu entfernen"*).
- **The floor-never-fades rule** (`Core/WallFade/WallFloorTile.cs` and the four write primitives it
  guards) — a wall-fade rule from the same builds, on a permanent user ruling.

Tick and shutdown wiring lives in `Hands/HandsDriver.cs` (lines ~56-58 and ~312-320) and
`Hands/HandsModule.cs` (~75-78). Config lives in `Defaults/Defaults.Hands.cs` and `Hands/HandsConfig.cs`.

---

## 2. The cloth machinery — the baseline the rest was built around

Everything in ModBuilds 430–432 was written against this contract, so read §2a and §2b before any
other section. **§2a survives the removal. §2b does not** — the user chose the figure-clothing lane
alone — but its measurements and its establishment work are what the parked subsystems stand on, and
they are recorded here rather than in §3–§5 for that reason.

### 2a. `FigureClothHands` (ModBuild 288) — **THE ONE THING THAT SURVIVES**

User request, ModBuild 286 hardware: *"Manche Elemente an einer Figur reagieren auf meine
Handbewegungen wenn ich die figur grabbe zB Umhänge … sie sollen auch auf meine andere Hand
reagieren, wenn ich mit der freien VR hand diese elemente berühre."*

**Mechanism.** `Cloth.sphereColliders` takes an array of `ClothSphereColliderPair`, and a *pair* of
two spheres is a **conic capsule** between them — the shape of a hand from palm to fingertip. One
pair therefore covers the whole hand instead of approximating it with a ball. Both spheres sit on a
single scene-root object at unit scale on Unity's built-in **Ignore Raycast** layer (2), so a radius
written in world units *is* that radius in the world and nothing can pick the probe. Their world
positions are written from `Rig.PalmCenter` and `Rig.IndexTip` every frame the probe is attached.
The probe attaches only while **exactly one** hand holds a figure and the free hand is within reach.

**The cost measurements that make the whole family viable** (Unity 2021.3.5f1 Linux player, 8 reps
per cell, with a null control at 0.0000–0.0001 ms and a known-positive control `AddComponent<Cloth>`
at 17.09 / 24.67 / 40.24 ms for 1681 / 3721 / 6561 vertices):

| operation | cost |
|---|---|
| `sphereColliders = pairs`, first assignment | 0.0165 ms |
| `sphereColliders = pairs`, re-assignment | 0.0083–0.0100 ms |
| `capsuleColliders = one capsule` | 0.0086–0.0103 ms |
| `sphereColliders = empty` (clearing) | 0.0047–0.0099 ms |
| moving an assigned collider's transform | 0.0041 ms |

**Assigning the collider arrays does not re-cook.** Three orders of magnitude below an enable
transition (20.1 / 34.3 / 56.4 ms at the same vertex counts) and — the part that settles it —
**flat in vertex count** (0.0165 ms at both 3721 and 6561) while a cook is linear in it.

**`Physics.autoSyncTransforms = false` does not break it, and that was measured.** A sphere pair
swept through a settled cloth, moved in `Update`, `fixedDeltaTime = 1/30` against a 90 Hz loop.
Worst cloth displacement from rest: null control (no collider assigned) 0.01923 m; auto-sync ON
0.08774 m; **auto-sync OFF with nothing else done 0.08585 m**; auto-sync off plus
`Physics.SyncTransforms()` 0.08805 m. If the collider were not reaching the solver, row three would
read the null control's 0.019. It reads 0.086. The `scene-interactables-PARKED.md` §7 hazard
"`autoSyncTransforms` unresolved" is therefore **closed**.

### 2b. `SceneClothHands` (ModBuild 322) — **REMOVED, but read it anyway**

User request, 2026-08-29, choosing from the six-candidate interaction survey: *"Ich will die 6"* —
the environment reacting to the hands.

Same probe, pointed at the room instead of at a held figure. It adds no assets and authors no
cloth: whatever `UnityEngine.Cloth` a room has, it finds; a room with none costs a dictionary
lookup and a return. Three differences from the figure version, each a hazard the figure version
does not have: **two hands** (so a cloth can be touched by both at once, which makes the
capture/restore bookkeeping the hard part of the file); **many cloths**, capped at
`MaxClothsPerHand = 4` per hand, nearest first; and **no owner to hang the scan off**, so it keeps a
registry refreshed every `RescanSeconds = 3` s.

**Established, not assumed:** a full sweep of `decompiled/` finds exactly two `Cloth` writes in the
whole game — `ActorBehaviour` (its own actors' `clothSolverFrequency` at spawn, and `enabled` around
a teleport) and `PhysicsController.SetClothesFrequency` (from two methods with no call sites). There
is **no occurrence anywhere** of `sphereColliders`, `capsuleColliders`, `coefficients`, `useGravity`,
`worldVelocityScale`, `stiffness` or `damping`. So "the game re-writes the array each frame" is dead
as an explanation for anything.

Dial: `[Hands] SceneryClothHandRadiusMillimeters`, default 35, range 10–120 — how thick the hand is
to the fabric, which is the number that decides how strongly a reach disturbs it. The fingertip
keeps its 10-of-35 share as a **ratio** rather than a second dial, so the taper survives every
setting of the one dial there is. The attach reach never falls below 3x the sphere, so the gate
cannot end up behind the thing it gates.

**Log token:** `Scenery cloth scan #<n>: <k> simulating scenery cloth(s) of <m> found (<a> skipped
as actor cloth)`.

---

## 3. Parked subsystem A — hands disturb particle effects

Files: `Hands/SceneVfxHands.cs` (2280 lines), `Hands/VfxFlow.cs` (663 lines).
History: created ModBuild 328 (`f1471df9`); made to actually work in 430; mechanism replaced in 431;
bounded in 432.

### 3a. What it did

The scenario's own particle effects — torch fire, smoke, embers, dust, spell wash — flow around the
player's hands while the hands are inside them, and are restored to the authored settings **field
for field** the moment the hands leave.

**The mechanism changed once, and that is the single most important thing in this section.**

*Until ModBuild 430* it was a **collision module**: switch on the collision module the game leaves
off, with a `collidesWith` mask naming exactly the mod's own layer, and let particles bounce off two
spheres carried on the palm and fingertip.

*From ModBuild 431* it is a **`ParticleSystemForceField`**: a volume that ACCELERATES the particles
that opt into it through their `externalForces` module. Each hand carries a soft sphere on the palm
(0.045 real m) and a smaller one on the index fingertip (0.014 real m, `TipRadiusFraction = 0.45`
of the palm field). Three live terms:

- a **directional push** driven by the hand's own velocity — a still hand disturbs nothing, a swipe
  wafts;
- a **vortex** about the direction the palm is travelling and about the direction the finger points,
  which is the term that makes smoke curl *around* a finger. No reflection can produce this and no
  drag can either;
- a **drag** that makes the effect hang around a hand rather than flee it.

Collision is kept but **inverted**: it is no longer the mechanism, it is the backstop that stops a
particle passing straight through the palm. Bounce near zero, dampen high, lifetime loss zero on
everything but flame.

**The reason the mechanism changed** is one user sentence and it is worth quoting because a tuning
round would have been the wrong answer: *"Der Rauch reagiert nun auf die Hände aber überhaupt nicht
immersiv — er weicht einfach super schnell unnatürlich zurück. Ich will das er sich um die Hand bzw
dem Finger legt — wie wenn man einmal durch den Rauch 'weht'."* A collision module **reflects** a
particle. A reflection is a billiard ball. There is no coefficient at which a reflection becomes a
flow, because they are different operations.

### 3b. The five classes, and why classification is kinematic

The other half of that request was *"Das Selbe soll auch für andere Partikeleffekte gelten …
überlege dir für jedes System ein immersives Erlebnis"*. One behaviour for every effect was never
going to be right, because fire and dust do not move alike. So five classes, five force profiles:

| class | feel | drag | vortex | attract | push | repel | radius | dampen | loss | **drift** |
|---|---|---|---|---|---|---|---|---|---|---|
| Smoke | wraps and settles back | 1.50 | 2.40 | 0.60 | 1.00 | 0.12 | 1.35 | 0.90 | 0.00 | 3.0 |
| Flame | carved open, leans, springs up; never blown sideways | 0.60 | 0.50 | 0.25 | 0.15 | 0.90 | 0.90 | 0.75 | 0.06 | 0.8 |
| Sparks | scatter and drift | 0.18 | 0.90 | 0.15 | 2.20 | 0.30 | 1.10 | 0.35 | 0.00 | 10.0 |
| Motes | hang in the wake, settle slowly | 3.00 | 1.60 | 0.40 | 1.60 | 0.05 | 1.60 | 0.95 | 0.00 | 6.0 |
| Aura | near field disturbed, stays owned by its figure | 1.10 | 3.00 | 1.40 | 0.25 | 0.10 | 0.80 | 0.80 | 0.00 | 1.0 |

The **ratios** are the designed part. The absolute magnitudes were an admitted first guess — Unity
documents neither "drag" nor "rotation speed" against anything — and the 432 round did **not**
change them; it added the `drift` column and the bound built on it (§4a), which converts each
absolute into a ceiling measured against the effect's own size and lifetime.

**Classification reads what a system DOES, never what it is called.** No asset name, no prefab
prefix, no shader name. This project has shipped a name-substring partition twice on doors and paid
for it both times. The material's blend mode was considered and **deliberately left out** for a
related reason: "is this material additive" is answered by reading `_DstBlend`, a property the
legacy particle shaders in a 2015-era kit do not declare at all, and a property-existence test that
silently flips is exactly the defect that killed the ghost hand for forty builds.

Six rules, first match wins (`VfxFlow.Classify`):

1. **it HANGS** — lifetime ≥ 3.0 s, diameter < 0.10 real m, travel ≤ 6 own widths → **Motes**
2. **it FLIES** — travel ≥ 12 own widths → **Sparks**
3. **owned and local** — an `ActorBehaviour` owns it AND it simulates in the figure's own space → **Aura**
4. **big and short** — diameter ≥ 0.10 real m and lifetime ≤ 1.8 s → **Flame**
5. **big** — diameter ≥ 0.10 real m → **Smoke**
6. anything else → **Motes** (the gentlest row, not a guess)

**The one scale-free trick.** "Travel" is `startSpeed x startLifetime / startSize`: how many of its
OWN WIDTHS a particle crosses in its life. All three quantities scale with the emitter transform in
the same way, so the ratio does not — it reads the same at x4 and at x198 and needs no rig scale.
**Exactly one term needs the rig scale**: `DiameterMetres`, an authored world size divided by the
live scale, tested against `BulkDiameterMetres = 0.10 m`. That single fact is the whole of §7.4.

### 3c. Adoption, grading and safety

- Reach `ReachRealMeters = 0.30`, release hysteresis 1.5x. Cap `MaxSystemsPerHand = 6` (an aura, a
  cast wash and ground dust is already three, and a brazier is four systems).
- Registry rescan every `RescanSeconds = 1` s with a 0.25 s clamp; census change-gated with a
  `CensusFloorSeconds = 30` s heartbeat.
- **Graded collision quality instead of a density cap**: ≤ 400 particles → High, ≤ 4000 → Medium,
  ≤ 20000 → Low, above that refused by name. `High` raycasts per particle per frame; Medium and Low
  resolve against a cached voxel grid of collision planes, so their cost stops scaling with the
  particle count. The voxel grid is sized at **one palm radius** (0.41 wu at the ModBuild-429 log's
  9.05 wu/m), because Unity's default 0.5 is just coarser than the hand. `ApproximateCollisionShapes = 16`.
- **Only modules the game leaves OFF are adopted**, so restore is exact. `sendCollisionMessages` is
  forced false on everything adopted. `collidesWith` is exactly `VRLayers.ModLayer` — the first
  unnamed layer, so the game never authored anything on it.
- **External forces has two paths.** Off (the overwhelming majority): switched on with
  `influenceFilter = List` holding exactly the hand fields for this effect's class. Already on: the
  takeover is **additive** — filter becomes `LayerMaskAndList` (a union) and our fields are
  appended; nothing the game configured is removed, its `multiplier` is left alone, and the system
  is **named in the log** because that multiplier scales our push. Both paths capture the module
  unconditionally and restore it field for field, entry by entry, because a held system can be
  re-graded while held and "we did not write it this time" is not a property a restore may lean on.
- **A field for a class no hand is holding carries ZERO in every force term** (`VfxFlowField.Idle`).
  That is a safety property, not tidiness — see the `influenceFilter` hazard in §8.

**Log tokens:** `] VFX scan #<n>`, `Hands disturb VFX: adopted '<name>' at collision quality …`,
`Hands disturb VFX: '<name>' is treated as <class> because …`, `Hands disturb VFX: first system
adopted`, `Hands disturb VFX: the registry sweep …`.

---

## 4. The measurements — every number that was derived rather than chosen

### 4a. The drift ceiling, and the 60 m coast that started it

User on ModBuild 431: *"Beim Rauch sieht man wie es immer noch super ruckartig zurückweicht.
Schlimmer ist es bei der Flamme beim Altar: Dort glitcht die Flamme in der Gegend rum."*

**A `ParticleSystemForceField` is an ACCELERATION and nothing in Unity's model ever takes back the
speed it gave.** No drag acts outside `endRange`, and these systems author none, so a particle
leaves the field carrying whatever velocity it picked up and coasts on that until it dies.

The altar effect the user filmed is `PrimeAltar_FX`: a **Sparks** system with a **5.00 s** lifetime.

```
terminal speed = acceleration / drag = 2.20 m/s² / 0.18 = 12 m/s
12 m/s x 5.00 s = 60 m of travel
```

That is the arithmetic. Worst case over the whole logged population was **69 m**; after the fix,
**0.25 m**.

**The remedy is a ceiling per system, and it needed an anchor.** The obvious anchor — express the
force as a fraction of the effect's own travel budget, `startSpeed x lifetime` — **does not exist on
most of the population**: 21 of the 33 classified systems in the 431 log read *"travel 0.0 of its
own widths per life"*, i.e. `startSpeedMultiplier` is 0 and every pixel of their motion comes from a
curve, a noise module or nothing at all. `GroundFog`, `GroundFogFar`, `ElemSpores`, `ElemGust`,
`ElemEmbers`, `center`, `P_CastHealRadial (4)`, `fx_sparks_drop` and the brazier's `Particle System`
are all in that group. A fraction of zero is zero, so a force scaled that way would make two thirds
of the room inert.

**So the anchor is the particle's own diameter, which is never zero**, times the class's `Drift`
allowance, under an **absolute** real-metre cap:

```
drift metres = min( profile.Drift x max(diameter, 0.01 m),  HandsVfxDriftMeters )
speed cap    = drift metres / max(lifetime, 0.25 s)
```

Floors: `MinDiameterMetres = 0.01` (below the smallest measured, `center` at 0.011 real m) and
`MinLifeSeconds = 0.25` (below the shortest measured, `fx_sparks_drop` at 0.70 s).

**The absolute cap is what stops `GroundFogFar`**, whose single particle measures **17.133 real
metres across** — the pale wash the altar video shows sliding across the room. One enormous soft
billboard whose centre only has to enter a 0.19 m field for the whole quad to move. A rule about its
own widths would have allowed it seventeen metres.

**The ceiling is enforced through the field's DRAG, and that is why a drag floor exists.** Unity's
force-field drag with `multiplyDragByParticleVelocity` on decelerates at `drag x |v|`, so a particle
under constant acceleration `a` asymptotes to `a / drag`. Pick the speed the effect is allowed to
reach, multiply by the drag actually written, and that product is the acceleration budget.

**`MinDrag = 1.2`.** Without a floor the identity is useless on exactly the class that needed it:
**Sparks carries drag 0.18**, so its terminal speed is 5.5x its acceleration. 1.2 is roughly a
one-second e-fold inside the field — mild enough that a particle is not frozen in mid-air (the
failure mode the cling paragraph warns about, which needs a drag several times this) and firm enough
that the budget is a real number on every row of the table.

After the fix the brazier flame moves **0.216 m**, four fifths of one of its own particle widths, so
it leans and stays attached.

**Two competing hypotheses were killed by measurement rather than argued down:** every one of the 33
classified systems reads `simulationSpace = Local`, so simulation space partitions nothing; and
start-speed-x-lifetime does not exist as an anchor for the reason above.

### 4b. The sweep that was refused

`FindObjectsOfType<Renderer>()` was the obvious way to reach plain-mesh hangings. It was **refused
on this project's own measurement**: the wall fade logs its own worst case for exactly that call —

```
FRESH FindObjectsOfType<Renderer> sweep (worst 0.60ms … 2.95ms)
```

**2.95 ms every 3 s is a quarter of an 11.11 ms frame**, handed to a player who asked for a flag to
move. The reference figure on the other side is **0.012–0.023 ms for `FindObjectsOfType<Cloth>`**,
which is what the memoized-lookup walk of an already-maintained population was benchmarked against:
at the top of the range the wall fade logs (**2540 renderers**) the mesh lane is a few thousand
dictionary probes, the same order as the Cloth figure and **20–100x under the sweep it avoids**.

### 4c. The rig scale walks inside one session

`23.49 → 15.49 → 7.42 → 3.72 → 8.46 → 3.32 → 7.16 → 2.14` world units per real metre, inside the
**one** ModBuild-431 session. The rig scale is not a property of the room. It is the **player's live
zoom**.

Five assets in that log therefore carry two different class verdicts, and each pair recovers the
**same authored world size to within 0.9 %**, which is what proves it is the divisor and not two
instances of different size:

| asset | verdict A | verdict B | recovered world size |
|---|---|---|---|
| `distort` | Motes 0.043 m @ 23.49 | Flame 0.269 m @ 3.72 | 1.010 / 1.001 wu |
| `Fog (3)` | Flame 0.215 m @ 3.72 | Motes 0.095 m @ 8.46 | 0.800 / 0.804 wu |
| `ElemGust` | Motes 0.036 m @ 23.49 | Smoke 0.256 m @ 3.32 | 0.846 / 0.850 wu |
| `ElemEmbers` | Motes 0.049 m @ 23.49 | Smoke 0.542 m @ 2.14 | 1.151 / 1.160 wu |
| `HexHighlight(Clone)` | Motes 0.043 m @ 23.49 | Smoke 0.269 m @ 3.72 | 1.010 / 1.001 wu |

**The 0.10 m bulk line.** A particle 0.10 real metres across is about half a palm. Below it, a
single particle is not something you can see the *shape* of, which is exactly when the mote
treatment (drag it into the wake) reads better than the smoke treatment (wrap it around the hand).
It is the only boundary the rig scale can move an effect across.

**The 1.10 re-classification threshold is derived from that boundary, not from the scale.** A drift
of ratio `r` multiplies every diameter by `1/r`, so a verdict can only be stale for a system within
a factor `r` of the bulk line. At `r = 1.10` that band is **0.091–0.110 m — nine millimetres around
a line ten centimetres from zero**. Inside it either verdict is defensible; outside it no drift this
small can misclassify anything. Cost is bounded by the per-hand cap and not by the zoom: a
continuous 23.49 → 2.14 crosses the threshold about **25 times**, each crossing a walk of at most
twelve held systems.

### 4d. Reach, re-derived rather than trusted

At the ModBuild-429 log's fixed **9.05 world units per real metre**, in a board volume
`x -13.0..20.2` by `z -8.2..25.0` whose walls stand 4.09 wu = 0.45 m tall: **0.30 real metres is
2.7 world units** — two thirds of a wall's height, several hexes across, and far larger than the
aura on a single figure it has to find. The reach was **not** the blocker and was left alone. (This
project has shipped a bound named `…Meters` clamped against a WORLD-unit product at 198x rig scale,
which is why it was re-derived at all and why every census prints both units with the scale beside
them.)

### 4e. The hanging population's own geometry

| measurement | value | source |
|---|---|---|
| banner rail AABB | `s(0.9, 0.1, 0.1)` at y 2.49 | 431 wall-fade census |
| banner fabric drop | foot 0.68 / top 2.52 = **1.84 wu** | same |
| curtain fabric | `s(1.6, 2.6, 0.2)`, foot **0.57 wu** over floor | same |
| wall faces | `s(2.0, 3.3, 0.1)` and `s(1.5, 3.4, 0.1)`, feet **on** the floor | same |
| `HangFootClearanceWu` | **0.35** — sits between 0.00 and 0.57 with margin both sides | derived |
| `SheetThicknessWu` | 0.45 (fabric measures 0.2, rail 0.1) | derived |
| `MinDropWu` / `DropShareOfSpan` / `MaxSpanWu` | 0.5 / 0.7 / 3.5 | derived from the above |

The wall fade's own "airborne bar" is 1.00 wu and answers a **different** question ("is this thing
floating?") — both hangings fail it, which is why a separate 0.35 constant had to exist.

### 4f. The floor-tile audit cost (adjacent, same rounds)

**0.38 µs per sweep over 32 floor renderers** on a CI box; the widest population the log reports in
a window is 21. Not part of the hand feature, recorded because it is in the same commits and is the
number that justified moving a one-shot latch onto a per-observation one.

---

## 5. Parked subsystem B — hands swing hangings and banners

Files: `Hands/SceneHangingHands.cs` (1373 lines), `Hands/SceneryActors.cs` (197 lines).
Created ModBuild 432 (`e7e28fb5`); plain-mesh lane added in `621ca2a8`.

### 5a. Why a second mechanism had to exist at all

User, ModBuild 431: *"Die Flaggen reagieren immer noch nicht auf meine Hand. Check die Logs nach dem
Szenario was ich gerade teste und finde heraus welche Flaggen dort sind und fix es dann für alle
Flaggen."*

**The banners carry no `UnityEngine.Cloth`.** The 431 scenery-cloth census reads *"6 simulating
scenery cloth(s) of 6 found"* and names all six: three Mindthief cloths, a Brute skirt, a Brute
cloak, a Prime Demon cloth — **every one a figure**, at palm distances of 11–14 world units
(5.4–6.5 real metres). In the very same scenario the wall-fade census names
`EN_CR_Hanging_01_Cloth_Post` (39 rows), `EN_CR_Hanging_01_Mesh`, `EN_CR_Hanging_01_Cloth (1)`,
`PCG_CR_Curtain_Red`, `EN_CR_Curtain_Cloth` and `CR_ThemeBanner_04` — none of them in the Cloth
registry. `FindObjectsOfType<Cloth>` is not a sampling instrument; it returns every one. So **no
radius, reach or bounds fix in `SceneClothHands` could ever have moved a banner**, and six builds of
tuning that file were spent on a population that was never there.

That also killed the "stale bounds" hypothesis. A banner is **two renderers**: the **rail**
(`EN_CR_Hanging_01_Cloth_Post`, the `s(0.9, 0.1, 0.1)` slab that looked broken) and the **fabric**
(`EN_CR_Hanging_01_Mesh`, a full 1.84 wu drop with entirely correct bounds). Both `[skinned]`.

### 5b. The mechanism: one solver over a driven set

Each bone of the renderer is rotated about the hanging's **own top rail** by an angle proportional
to how far below the rail it sits, on a **critically damped spring**, in `LateUpdate`. A bone at the
rail moves by nothing, a bone at the hem by the full angle, everything between bends like a sheet
pushed from one side. Presentation only: no physics, no colliders, no cook, no per-frame allocation.

**A whole-object swing is INERT on a `SkinnedMeshRenderer`** — its vertices follow `bones[]` and
ignore the renderer's transform entirely — and fully **ALIVE** on a `MeshRenderer`, whose vertices
follow nothing else. So the two routes are not two mechanisms: they are **one solver over a DRIVEN
SET**, which is the bind bones for a rigged hanging and the renderer's own transform for a plain
one. Same spring, same pivot, same dials, same exact restore, same census; `Build()` branches on the
renderer type in one place and `Push/Step/Apply/Restore` are untouched. A rigged hanging with a
single bone is the degenerate case of the same line of code.

**Rotate about a pivot, not along a chain.** The obvious implementation walks the bone chain and
hinges each bone in its parent's frame. It is wrong here: `SkinnedMeshRenderer.bones` is a **flat
array in bind order** and says nothing about parentage — a cloth rig may be a chain, a grid, or a
fan off one root, and a walk-the-chain solver is silently inert on two of those three. Writing each
bone's WORLD pose from its own captured home pose is topology- and order-independent: whatever a
parent bone does to its children is overwritten when those children are written from their own
homes.

**Two captures, on purpose.** The RESTORE capture is per-bone **LOCAL** position and rotation, so
writing it back is exact however the wall, the room or the board has moved. The SOLVE capture is
per-bone **WORLD** pose plus pivot and bounds, re-taken on the registry sweep whenever the hanging
is at rest, so a moved prop cannot leave the solver working from a frame that no longer exists.

A runtime `Cloth` was rejected outright: it cooks (linear in vertex count, on the frame the hand
arrives — see `.planning/perf/CLOTH-COOK.md`, ~5.3 µs/vertex), it fights the game's own skinning,
and it would need the same bones anyway.

### 5c. Population, by what these objects ARE

Never by a name. Eight terms; the census prints candidates seen, qualified, and rejected **by which
term** with the first several named:

1. a renderer this class can move: a `SkinnedMeshRenderer` with bones, or a `MeshRenderer` with a
   mesh. *Skinned is the art's own declaration that a mesh was built to deform* — in the whole 431
   log every wall, pillar, floor, rock, torch and crystal is a plain `MeshRenderer`, so this single
   term is why the **skinned** lane needs no defence against selecting a wall.
2. not actor-owned (`SceneryActors`) — a Brute's cloak and a demon's foliage are skinned too and
   belong to `FigureClothHands`.
3. no `Cloth` on it — that is `SceneClothHands`'s, and two owners on one object is the defect this
   file exists beside.
4. no `Rigidbody` above it — a physics body owns its own transform.
5. **opaque**: render queue < 3000. A light shaft or god-ray is a transparent skinned quad; this is
   what keeps the doorway light meshes out **without naming them**.
6. a **SHEET**: thinnest **oriented** extent ≤ 0.45 wu, measured in the renderer's own frame
   (`localBounds x lossyScale`), never from a world AABB — a banner hung at 45° has a fat AABB in
   both horizontal axes and would fail a world-space thickness test while being exactly as thin as
   it looks.
7. a **DROP**: ≥ 0.5 wu tall, ≥ 0.7 of its own width, ≤ 3.5 wu wide. The rail fails on the first
   clause (0.1 wu tall) and therefore never swings on its own, which is correct — a rail is masonry.
8. **plain mesh only — AIRBORNE**: foot clears its room's floor by ≥ 0.35 wu. See §4e: geometry
   alone *cannot* separate a hanging from a wall face, because wall faces pass every sheet-and-drop
   test a curtain passes. What differs is the FOOT. Floor height comes from the room's own central
   tile, which is where the wall fade gets its floors too.
9. **plain mesh only — a LEAF, and a SINGLE SUBMESH**: the mesh route rotates a **transform**, so
   any renderer parented under it would be dragged along; and a multi-submesh mesh is the shape a
   combined or batched prop takes. A hanging welded to its OWN rail is not refused and need not be —
   the pivot IS the top edge, so the rail end does not move.

**THE TERM THAT IS MISSING, AND IT IS WHY THIS WAS PARKED.** There is no door refusal. A door leaf
is rigged, flat and hangs, so it passes terms 1, 6 and 7 and never reaches 8 or 9. The ModBuild 432
log shows `'CR_ST_Door_01_Right'` being driven as a hanging. Read **§7.0** before touching this
list.

### 5d. The plain-mesh lane costs no sweep

`EN_CR_Curtain_Mesh` and `CR_BT_BanditBanner_Wall` are hangings the art shipped as plain
`MeshRenderer`s. `FindObjectsOfType<Renderer>()` was refused on the measurement in §4b. Instead the
lane reads populations that **already exist**:

- **`SceneRegistry.Volumes`** — the mod's self-maintaining `TilesOcclusionVolume` registry, enrolled
  by a Harmony postfix on the volume's own `Start`. Each volume hands over the room's
  `MeshRenderer[]` **and** its `CentralTile`, whose height is term 8's floor.
- **`TilesOcclusionGenerator.s_Instance.m_ObjectRenderers`** — the game's own live list.

Neither read costs a scan. Recurring cost is one walk with a memoized verdict probe per entry.

**The lane STANDS DOWN rather than falling back.** `ComponentRegistry.Collect` **fails OPEN** — a
disarmed registry runs the very `FindObjectsOfType` it exists to replace — so calling it blind would
smuggle that sweep back in through the back door. The lane is gated on
`SceneRegistry.Volumes.Count > 0`, which is reachable only through the enrolment postfix
(`Arm(false)` returns before seeding), so **a non-zero count PROVES armed**. At zero the lane does
nothing and the census says which of the two reasons it was.

### 5e. `SceneryActors` — "is this object part of a figure?"

Three terms, each reporting **which one fired**:

1. an `ActorBehaviour` **above** — correct wherever it fires, costs one call;
2. an **`ActorEvents`** on or above — **the term that actually fires**, and it is the game's own
   definition (see §7.8);
3. an `ActorBehaviour` under the nearest *driving* `Animator` ancestor — the game's own
   `GetActorBehaviour` shape, bounded so it cannot degrade into a scene sweep from a high ancestor.

`MaxDepth = 12`. Never called per frame: `SceneClothHands` asks once per cloth per 3 s sweep;
`SceneHangingHands` asks once per renderer, ever, and memoizes.

### 5f. Dials and constants

`[Hands] SceneryHangingSwingDegrees` default **30** (range 0–80; 0 = flags still, curtains still
react) and `[Hands] SceneryHangingSettleSeconds` default **0.7** (range 0.05–3). Both live-tunable,
both ignored while `[Hands] HandsDisturbScenery` is off — **no new master toggle, because one
user-facing idea stays one setting**.

Internal: `ReachRealMeters 0.2` (never below 4x the hand sphere), `HandMotionShare 0.5`,
`HandMotionFullSpeed 1.5` real m/s, `RescanSeconds 3`, `CensusHeartbeatSeconds 30`,
`MaxStepSeconds 0.05` (a loading hitch integrated at its true length throws the solver across the
room), `RestEpsilonWu 0.0005`.

**Log tokens:** `Scenery hanging scan #<n>: …` and `Scenery hanging: a hand is inside a banner for
the first time — …`.

---

## 6. Config surface added by the parked work

All under `[Hands]`, all live-tunable, all defined in `Defaults/Defaults.Hands.cs`:

| key | default | added |
|---|---|---|
| `HandsDisturbVfx` | `true` | pre-430 (ModBuild 328) |
| `HandsVfxPushStrength` | 1.0 | 431 |
| `HandsVfxWakeSpeed` | 0.6 m/s | 431 |
| `HandsVfxClingStrength` | 1.0 | 431 |
| `HandsVfxCurlStrength` | 1.0 | 431 |
| `HandsVfxSettleSeconds` | 0.35 | 431 |
| `HandsVfxReachMeters` | 0.14 | 431 |
| `HandsVfxBounce` | 0.05 | 431 — the reported defect's own dial |
| `HandsVfxDriftMeters` | 0.25 | 432 |
| `HandsVfxWakeAttackSeconds` | 0.12 | 432 — **0 restores 431 behaviour for A/B** |
| `SceneryHangingSwingDegrees` | 30 | 432 |
| `SceneryHangingSettleSeconds` | 0.7 | 432 |

**Also removed, because their subsystems go too:** `HandsDisturbScenery` (`true`) — the master
switch for the scenery-cloth AND hanging lanes, both removed — and
`SceneryClothHandRadiusMillimeters` (35), the scenery-cloth hand thickness.

**Surviving:** nothing in the `[Hands]` group above. `FigureClothHands` is dialled by
`FigureGrabConfig.ClothHandReachRealMeters` and by the figure-grab pick radius, neither of which is
part of this work.

Every one of these is presentation-only and **carries zero wire bytes**. Particles, cloth vertices
and bone poses have never been networked; nothing here is authoritative; a peer on an older build
sees their own room hang still. There is no per-sub-feature sync setting and none was added.

---

## 7. The defects found and fixed — the expensive part

These are the reason this document exists. Each one cost at least one build and several cost more.
**§7.0 is the exception: it was found and never fixed, and it is the most useful entry here.**

### 7.0 THE ONE THAT WAS NEVER FIXED — the hanging solver adopted a DOOR LEAF

From the ModBuild 432 hardware log, anchored on `] `:

```
] Scenery hanging scan #88: 17 hanging(s) driven of 105 skinned candidate(s) in 0.106 ms …
HANGINGS (naming 5 of 17, and a truncated list is not absence):
  'CR_ST_Door_01_Right' at 'Maps/J : (…)/ThickDoor : (ca61e151-…)/Generated Content/
   CR_ST_Door_02/CR_ST_Door_01/CR_ST_Door_01_Right' [skinned] frame 'Door_Root'
   residual 0 wu … 9 bone(s), 0 blend shapes …
```

The user saw it and reported it: *"Im neusten Test hat die Tür auch mit der Hand interagiert - das
soll auch raus."*

**Why it passed every term, and why every term was individually correct.** A door leaf is
**rigged** (9 bones, `frame 'Door_Root'`), **flat**, and **hangs**. So it satisfies term 1 (a
`SkinnedMeshRenderer` with bones), term 6 (a sheet by its own oriented extents), term 7 (a drop),
and it never even reaches terms 8 and 9, which are plain-mesh only. Not one of those thresholds was
mis-derived. §4e shows each of them being read off a measurement.

**The rule was a claim about the objects somebody thought of.** The population rule in §5c was built
to answer one question — *"how do I select fabric without selecting a wall?"* — and it answers it
correctly. **Nobody asked whether it excluded doors.** "Skinned is the art's own declaration that a
mesh was built to deform" was measured against the 431 log's walls, pillars, floors, rocks, torches
and crystals, all plain `MeshRenderer`s, and the conclusion drawn was "so this term cannot select a
wall". That conclusion is true and it is not the same sentence as "so this term selects only
fabric".

**The guard that should have been there already exists in this codebase, three times over.** The
game's own door identity is `UnityGameEditorDoorProp` on an ancestor, and the exact walk
`GetComponentInParent<UnityGameEditorDoorProp>()` is already used by `Core/MaterialLoaderHeal.cs`
(:654, :803) and by `WallSegmentFade.CollectAdoptedSiblings` — the latter added in ModBuild 429 for
*precisely* this class of defect, one round before `SceneHangingHands` was written. It was not
applied here because the hanging lane never considered doors part of its problem.

**This is the general lesson this project keeps paying for, in its most concrete form yet: a
structural population rule is a claim about the objects you thought of.** A rule derived from a
census of the things you were trying to exclude tells you nothing about the things that were never
in the census. The census counts and the near-miss list in `Scenery hanging scan #<n>` were built to
make exactly this visible — and they did, on the first hardware run — but the rule shipped first.

If this is resumed, the door refusal is the **first** line to write, before the reach gate and
before the spring.

### 7.1 A force field that accelerated but never braked
See §4a. The single largest defect in the feature. Symptom: *"die Flamme glitcht in der Gegend
rum."* Cause: a force field is an acceleration and Unity never takes the velocity back. 69 m worst
case. Fixed by a per-system ceiling enforced through drag, with a drag floor.

### 7.2 The jerk was two step functions
`hand.PalmVelocity` was read **raw** once a frame and used for two things:

- **`_wake`**, the push strength, which rose **instantly** to whatever that frame's value was; and
- **`_wakeDirection`**, which is both the push direction **and the axis the entire vortex turns
  about**, rewritten every frame the hand moved faster than a `1e-4` wu/s epsilon — i.e. **every
  frame there is**.

A tracked hand's per-frame velocity is a difference of two poses over a frame time and it jitters.
The first path turned that jitter into the reported stutter. The second turned it into the **thin
curved white filaments** visible in `rauch_effekt.mp4`, because a vortex whose axis moves each frame
drags every particle along a different arc.

Fixed with one exponential filter on the velocity **vector**
(`HandsVfxWakeAttackSeconds = 0.12 s`), a direction gate at a tenth of `HandsVfxWakeSpeed` (about
6 cm/s — slower than a hand a player believes is still), and a per-class attack envelope so a class
a hand has just taken hold of fades its field in over the same constant.

### 7.3 The adoption ranking scored a room fog at 0.0
The census named it outright: `nearest refused: 'p_fire_torch (10)' by CapFull`, the same for
`'p_fire_torch (8)'`, `'PrimeAltar_FX'` and `'ElemEmbers'` — while the six slots were held by
`'P_SewerFog'`, `'MeshEmitterFog'`, `'Fog (3)'` and `'Cloud (4)'`.

**Ranking was distance to the DRAWN BOUNDS, and a room fog's bounds CONTAIN the hand**, so it scores
**0.0** and beats a torch ten centimetres away every single time. The user's premise ("torch effects
do not react at all") was inverted by the same lines: the scan reports **201 adoptable systems of
202, with 0 on figures** — every effect in that scenario is scenery and the torches *were* adopted.

Fixed by adding `EmitterTieWeight = 0.05` x palm-to-emitter distance to the **ordering** while
eligibility still uses true bounds distance, so fog is adopted **after** fire. The cap of 6 was
**not** raised — a brazier is four systems and the cap was not the defect. The same change made an
existing token honest: `nearest refused` had been the first refusal in `FindObjectsOfType` order,
not the nearest.

### 7.4 A classifier verdict that depended on the player's zoom
See §4c. Five assets carried two verdicts. **Verified before fixing**, because the competing reading
(two instances at different `lossyScale`, in which case both verdicts are correct) would have made
it a non-defect — each pair recovers the same authored world size to within 0.9 %.

**The staleness was narrower than either party first said.** `Rescan` re-measures every second at
the live scale, so an **unheld** system's verdict was never able to go stale. What goes stale is the
pair a hand **stamps at adoption** — `_flowClass` (which picks the force profile) and `_driftCap`
beside it (whose numerator is that same scale-dependent diameter) — held untouched for as long as
the hand holds on. A hand on a brazier while the player zoomed out 6x kept treating a 27 cm flame
tongue as 4 cm dust.

Fixed with `RefreshScaleGeneration` in `Tick` before the sweep and before either hand ticks (one
float compare a frame in the common case) and `ReclassifyHeld` re-measuring the held set (at most
twelve). The **ceiling is refreshed even when the class does not move**, because a particle that has
doubled in real metres has doubled its own drift allowance without changing class. `_feelAt.Remove`
on a class change, because the collision backstop's dampen and lifetime loss are per class and
`ApplyFeel` early-outs on a generation match.

A **wrong premise was corrected in the same round**: "it classifies with a fallback scale of 1" was
false — that path is unreachable, because `Tick` returns through `Clear()` before the sampling line
whenever neither hand is tracked and samples before calling `Rescan`. The `_rigScaleSampled` check
is therefore a **guard against a future refactor**, not a mechanism, and its safe state is a
deliberately **EMPTY registry**: nothing adopted, no module touched, every field `Idle()`.

### 7.5 The reach gate read the transform its own class drives
In `SceneHangingHands`, the reach gate read `_frame` **live** — and `_frame` is a transform this
class **drives** (always for a plain mesh, and for any rig whose `rootBone` is one of its own
bones). A hand that pushed a banner would find it out of reach on the next frame, the spring would
return it, and **the pair would oscillate**. Caught in review before hardware; it would have bitten
the skinned lane too.

Fixed by running the gate against the home `localToWorld` / `worldToLocal` pair frozen by `Anchor` —
i.e. the question is *"is the hand where this banner HANGS"*, which is a question about the authored
pose.

### 7.6 The push went toward the hand
A sign error in the deflection direction, caught in review of `e7e28fb5`.

### 7.7 The swing axis sent the hem through the wall
The axis was `cross(up, dir)`, which sends the hem **backwards through the wall** it hangs on.
Caught in the same review.

### 7.8 `GetComponentInParent<ActorBehaviour>()` could never fire
`SceneClothHands` excluded actor cloth with `cloth.GetComponentInParent<ActorBehaviour>()`. The 431
log reports `6 simulating scenery cloth(s) of 6 found (0 skipped as actor cloth)` while all six sit
at `Board/<guid>/HE_Mindthief_PR(Clone)/…` and `HE_Brute_PR(Clone)/…` — i.e. **all six ARE actor
cloth and none was excluded.** This class was eligible to write `sphereColliders` on cloth
`FigureClothHands` owns, which is the two-writers corruption both class headers warn about: both
write the WHOLE array, so two owners clobber each other's capture and leave a stale probe in it
forever.

**The test did not return a wrong answer for a subtle reason; it asked a question whose answer is
structurally always no.** `Choreographer` builds a figure like this
(`decompiled/GH.Runtime/Choreographer.cs:1065-1082`, same shape again at `:880-897`):

```csharp
GameObject gameObject = Object.Instantiate(m_ActorPrefab);      // carries ActorBehaviour
Animator animator = characterInstance.GetComponentsInChildren<Animator>()
                       .FirstOrDefault(x => layer is "Hero" or "Monster");
gameObject.transform.SetParent(animator.transform);             // UNDER the animated object
animator.gameObject.AddComponent<ActorEvents>();
```

The `ActorBehaviour` is parented **UNDER** the animated object, so it is the cloth's **SIBLING
SUBTREE, never its ancestor**. The game itself never looks up for it: `ActorBehaviour.SetActor`
collects cloth with `m_Animator.gameObject.GetComponentsInChildren<Cloth>()`
(`ActorBehaviour.cs:122`), `ActorBehaviour.GetActorBehaviour` falls back to `GetComponentInChildren`
(`:221-233`), and `IdleSMB` reads `m_Animator.gameObject.GetComponentInChildren<ActorBehaviour>()`
(`IdleSMB.cs:33`). **Every one of them looks DOWN.** A parent walk was looking the wrong way.

`ActorEvents` is added at runtime by exactly those two `Choreographer` sites **and nowhere else** in
the whole decompiled game — every other mention is a lookup — and it is added to
`animator.gameObject`, which is exactly the object `ActorBehaviour` then sweeps for cloth. So *"an
`ActorEvents` on me or above me"* **is** the game's own definition of actor-owned, expressed as a
component test with no name matching in it.

**`SceneryActors.cs` goes with its callers, but this derivation must not.** Any future class that
has to tell scenery from a figure — or that writes `Cloth.sphereColliders` alongside
`FigureClothHands` — needs the three terms above before it writes a line. Recover the file with
`git show e7e28fb5:src/GloomhavenVR/Hands/SceneryActors.cs`.

### 7.9 `&& _scene.Count > 0` put `FindObjectsOfType` on a per-frame path — in THREE classes
The cadence early-out read `if (now < _nextScanAt && _scene.Count > 0) return;`, so an **empty**
population bypassed the throttle entirely and ran the sweep **once per frame**. Found in:

- **`SceneVfxHands`** (ModBuild 430) — also why its three timed scans burned out in three
  consecutive frames and why their cost fell 0.012 → 0.002 → 0.001 ms across them;
- **`SceneClothHands`** (ModBuild 431) — corroborated by menu perf spikes at frames 843/856/881
  attributing 0.01–0.02 ms to `Hands.SceneCloth` in the **main menu**, where the registry is empty
  and the class should have been doing nothing at all;
- a third class with the same shape, found in the same sweep.

The population term is gone from all of them, and `SceneHangingHands` was written from the start
with **no population term in the cadence test** and a clamp that cannot be dialled to zero.

### 7.10 Three ways an instrument said nothing for six builds
All three in `SceneClothHands`, and they are why six builds of log said nothing about the feature:

- **The census never measured a scenario.** It was gated on `_scansTimed < 3` — the first three
  sweeps EVER — and those three always land in the **main menu**, before a room exists. The 430 log
  carries six of them (two runs of three, the counter reset by `Shutdown`) and every single one
  reads *"0 simulating scenery cloth(s) of 0 found"*. Then silence for the whole session. Now
  change-triggered on the population signature with a 30 s heartbeat and an **unconditional SWEEP
  counter**, so silence means STOPPED and can never mean EMPTY.
- **It logged below the default level.** Both lines were `VRLog.Info`, which the ModBuild-331
  mapping puts on the DEBUG tier. The 430 log happens to have been captured at Debug so the lines
  survived; at the shipped default the class was **mute**. Both are `Note` and `// HW-VERIFY` now.
- **The same three-scan blindness in `SceneVfxHands`** — all three scans read "0 adoptable of 0
  found" between the menu rig build and the main-menu video.

### 7.11 It armed the first four cloths in range, not the nearest four
`Probe.Acquire` walked `_scene` in `FindObjectsOfType` order and stopped at the cap of four. With a
0.25 real-metre reach and a row of banners **0.9 world units apart**, a hand inside ONE banner can
have more than four in range and the four it arms need not include the one it is touching. **The
class doc had claimed "nearest first" since the day it was written and the code had never done it.**
The identical defect existed in `SceneVfxHands`'s adoption loop, whose own comment said "nearest
first" while iterating `FindObjectsOfType` order.

### 7.12 The feature had never once done anything visible
The ModBuild-429 log: **one** system adopted in the entire session, `'Waypoint_Path'`, with **0 live
particles**. The two effects a hand actually reached were **refused by the density cap**:
`'MeshEmitterBits (1)'` at 998 and `'MeshEmitterBits'` at 985 against a cap of **900**. The cap was
refusing precisely the effects worth feeling. Replaced by graded quality (§3c) and
`MaxSystemsPerHand` 3 → 6, `RescanSeconds` 3 → 1 (a cast wash can be born and die between scans).

### 7.13 Smaller ones, recorded so they are not re-found
- A system whose collision the game already owns was refused in `Adopt` with **no log line at
  all** — a completely silent population. Now a named census bucket.
- `SceneClothHands` kept no cached renderer for the distance gate — the class doc had described that
  cache since it was written and the code had never built it; it was a `GetComponent` **per cloth
  per hand per frame**.
- Both probes shared one distance scratch buffer, which would hand the census the right hand's
  distances as the left hand's.
- A cloth destroyed under a hand left a dead Unity object as a dictionary key.
- `Cloth.coefficients` marshals **one struct per vertex per read**, so the profile is read through
  `GetCoefficients(List<>)` into one reused buffer, once per cloth, never per frame.
- The vortex was the **only** force in the feature not multiplied by the rig scale, so the same
  authored swirl meant twenty times as much of a room at x9 as at x198.

---

## 8. Hazards — do not re-learn these

**The two RFX4 collision scripts must be skipped entirely.** The `scene-interactables-PARKED.md`
survey nominated exactly these as "the cheapest candidate — a visible reaction with zero cook".
Reading them in full shows why they must be left alone:

- **`RFX4_ParticleCollisionHandler.OnParticleCollision`** does `Instantiate` **once per collision
  event per prefab, every frame, with no cap and no rate limit.** A hand parked in a burning brazier
  is an unbounded spawn loop. This project has paid for that shape twice already.
- **`RFX4_CollisionPropertyDeactiavtion.Update`** writes `collisionModule.enabled = false` **every
  frame** once its delay has passed — no guard flag, no early-out. Adopting one of those systems is
  a **write war that cannot be won**, whatever one thinks of the spawning. The standing rule is
  concede the flag, own the number; here there is no number to own.

Systems carrying either component are skipped by name of *component*, never by asset name.

**`multiplyDragByParticleSize` multiplies by WORLD size.** It is **OFF on purpose**. This mod runs
from about x4 to about x198, so the same authored smoke would carry roughly **fifty times** the drag
in one room as in another. "Big things feel more drag" is a per-class decision in the profile table
instead.

**`influenceFilter` defaults to `Everything`, so a LayerMask cannot bound a force field.** Unity's
default filter is a **layer mask set to Everything**, and "scoped to our own layer" is not something
a mask set to Everything can be scoped by. Any particle system in the game that has its own external
forces switched on is reachable by every force field in the scene, ours included, whether this mod
adopted it or not. **The bound is therefore `VfxFlowField.Idle`**: a field for a class no hand is
currently holding carries **zero in every force term**. The influence list is always an explicit
`List` (or `LayerMaskAndList` union when the game already used the module), never a bare mask.

**Rotating a transform is inert on a `SkinnedMeshRenderer`.** Its vertices follow `bones[]`. A
whole-object swing on a rigged banner does nothing at all and looks exactly like "the feature never
ran".

**`SkinnedMeshRenderer.bones` is a flat bind-order array that says nothing about parentage.** A rig
may be a chain, a grid, or a fan off one root. A walk-the-chain solver is silently inert on two of
those three. Write each bone's world pose from its own captured home.

**`ComponentRegistry.Collect` fails OPEN.** A disarmed registry performs the very
`FindObjectsOfType` it exists to replace. Never call it blind on a path that exists to avoid a
sweep. Gate on a population count that can only be non-zero if the registry is armed.

**A structural population rule is a claim about the objects you thought of.** Deriving every
threshold from a census of the things you are trying to exclude tells you nothing about the things
that were never in the census. The concrete instance is §7.0 — a door leaf is rigged, flat and
hangs. Refuse the game's own identity components explicitly, by component, in the same commit as
the selector.

**A collision module is a reflection and cannot be tuned into a flow.** Recorded again here because
it cost a whole build to learn: no bounce coefficient turns a bounce into a waft.

**`FindObjectsOfType<Renderer>` costs 2.95 ms worst case in this project.** Measured, not felt. Do
not put it on any recurring path.

**Two claims in `scene-interactables-PARKED.md` were later overturned by measurement:**

1. its §3b called the RFX4 route "the cheapest, zero cook" — see the first hazard above;
2. its §7 left `Physics.autoSyncTransforms` as an unresolved hazard — §2a closes it with a measured
   null control.

---

## 9. Open questions at the moment of parking

Each one names the **log field** that would decide it. All of these are live in the shipped 432
instrumentation and are lost when the code is removed.

| # | question | the field that decides it |
|---|---|---|
| 1 | **Does the swirl still read once it is bounded?** The vortex is clamped to the drift ceiling directly. If effects now merely *slow down* near a hand and never wrap, the clamp ate the wrap — and `HandsVfxCurlStrength` cannot buy past it while `HandsVfxDriftMeters` can. | The `SINCE ModBuild 432 IT ALSO GETS A CEILING, AND THIS EFFECT'S IS <x> REAL METRES` clause on `Hands disturb VFX: '<name>' is treated as …`, read against the user's description of the wrap. |
| 2 | **Is the outward (repel) term's sign right?** It is written as a NEGATIVE force-field gravity with the focus at the centre, which should push particles away from the palm. No reading of the API settles it. | Visual: if effects visibly collapse INTO the hand instead of parting around it, `VfxFlowField.Apply`'s sign is inverted and nothing else in the feature is implicated. The `THE OUTWARD TERM CARRIES THE ONE QUESTION NO READING OF THE API COULD SETTLE` clause on the same line says so in place. |
| 3 | **Are the drag and vortex magnitudes right?** The ratios are designed; the absolutes were a first guess and Unity documents neither term's units. | `Hands disturb VFX: '<name>' is treated as …` prints `drag`, `vortex`, `rotation attraction`, `push`, `outward`, `radius`, `dampen`, `lifetime loss` for the class. Too much drag freezes an effect in mid-air, which is as wrong as the bounce it replaced; `HandsVfxClingStrength` is the dial. |
| 4 | **Does Medium/Low collision quality still deflect off a hand?** *Formally RETIRED in 431* — a force field is a volume and consults no collider, so the wrap, cling and waft are identical at every quality. What the band can still cost is how crisply a particle STOPS at the skin. The retirement is kept readable against the old text rather than deleted. | `Hands disturb VFX: adopted '<name>' at collision quality <q>`. |
| 5 | **Do the banners actually move?** PARTLY ANSWERED by the 432 log: the skinned lane **drove 17 hangings of 105 skinned candidates in 0.106 ms**, so it runs and it is cheap — but at least one of the 17 was a door leaf (§7.0), and nothing in the log says whether a *banner* visibly moved. | `Scenery hanging: a hand is inside a banner for the first time — …`. Its own text names the three failure modes if it fires and nothing moved: a one-bone rig (the sheet turns rigidly and may be hidden by the wall behind it), a drop far larger than the swing, or a rig whose bones do not weight the visible fabric. |
| 6 | **Does the plain-mesh lane find anything?** Still open — the 432 census excerpt names only the skinned candidate count. | `Scenery hanging scan #<n>` — read **per lane**: `RIGGED <n>` vs `PLAIN-MESH <n>`, the `PLAIN-MESH LANE <stand-down reason>` clause, and the rejection term counts. `not airborne` and `not a leaf` can only ever come from the mesh lane; a wall can only ever reach the mesh lane. Candidates zero = nothing found and the room is not loaded; candidates non-zero with 0 driven = every one was rejected and the term counts say which test to loosen. |
| 7 | **Is `SceneRegistry.Volumes` ever armed in a real scenario?** The whole mesh lane is gated on it. | The `PLAIN-MESH LANE` stand-down clause names **which of the two reasons** it stood down. |
| 8 | **Were the banners authored to move in wind?** `SceneHangingHands` reports whether each hanging's shader declares `_AddVertexAnim`, `_VertexAnim_Intensity`, `_NoiseSpeed`, `_WindStrength` or `_Wind`. Nothing gates on it; the answer changes what a future round would build. | The wind-property clause in the per-hanging `Describe()` output inside `Scenery hanging scan #<n>`. |
| 9 | **Does any hanging have an `Animator` above it?** If one ever does, our `LateUpdate` write is simply the last one written (concede the flag, own the number) — but nobody has checked. | The census prints it per hanging. |

---

## 10. The commits — recover the code with `git show`

The feature's history is **ModBuilds 430, 431 and 432**. ModBuilds 428 and 429 are in the same date
range but are **door** work and contain no hand-interaction change; they are listed for completeness
because the round numbering runs through them.

**Origin (before this round):**

| SHA | subject |
|---|---|
| `bb1c0cdc` | `fix(figuregrab): ModBuild 288 — the cape is a cape again, the free hand disturbs it, and the glow stands down inside the diorama` — creates `FigureClothHands.cs` |
| `a65268f1` | `feat(hands): ModBuild 322 — reach through a curtain and it moves` — creates `SceneClothHands.cs` |
| `f1471df9` | `feat(hands): ModBuild 328 — put a hand in the smoke and the smoke goes round it` — creates `SceneVfxHands.cs` |

**ModBuild 428** (doors; no hand-interaction change):

| SHA | subject |
|---|---|
| `39293a07` | `fix(doors): the game's own door animation is culled off-camera, so VR never sees it open` |
| `f2522dce` | `fix(doors): a rebuilt door could never be hidden again, and every line after the first rebuild said so wrongly` |
| `4f147416` | `chore(net): ModBuild 428 — give the game its own doors back: animate them off-camera, and stop the hide latching through a rebuild` |

**ModBuild 429** (doors; no hand-interaction change):

| SHA | subject |
|---|---|
| `b485bbdc` | `fix(doors): delete the leaf-hide stand-in and the last unguarded wall lane onto a door; the probe was measuring the Idle clip` |
| `cdd29750` | `chore(net): ModBuild 429 — the hide is gone, the doors are the game's again, and the probe that condemned them was watching the Idle clip` |

**ModBuild 430 — the hands first reach the effects at all:**

| SHA | subject |
|---|---|
| `efe79c6f` | `fix(hands): the smoke cap was refusing exactly the effects worth feeling, and the census was blind before the scenario existed` |
| `71bf5390` | `fix(wall-fade): a floor tile occludes nothing, so no lane may ever fade one` (unrelated lane, same build) |
| `be5ae64e` | `chore(net): ModBuild 430 — a floor tile never fades, and the hands finally reach the effects worth feeling` |

**ModBuild 431 — the mechanism becomes a force field:**

| SHA | subject |
|---|---|
| `c62165a0` | `fix(hands): the scenery-cloth census measured the menu, and the probe armed the wrong four banners` |
| `7a09287c` | `feat(hands): the smoke wraps round a finger instead of bouncing off it` — **creates `VfxFlow.cs`** |
| `ef3af381` | `fix(walls): a refusal is not a restoration, and the one shot was spent on a healthy tile` (unrelated lane) |
| `a7a7e770` | `refactor(walls): delete the fifth enable-write path before anyone wires it up` (unrelated lane) |
| `42221abf` | `chore(net): ModBuild 431 — a refusal is not a restoration, the hand is a pocket of air instead of a bat, and the cloth probe armed the wrong four banners` |

**ModBuild 432 — bounded, re-ranked, and the banners get their own mechanism:**

| SHA | subject |
|---|---|
| `dce0248c` | `fix(hands/vfx): a force field never takes back what it gave, and the six slots were spent on fog` |
| `e7e28fb5` | `feat(hands): the flags are not cloth, and the actor exclusion could never fire` — **creates `SceneHangingHands.cs` and `SceneryActors.cs`** |
| `a9a328a9` | `fix(hands/vfx): the verdict depended on the zoom, and a held system never looked again` |
| `621ca2a8` | `feat(hands): plain-mesh hangings swing too, off a population nobody has to sweep for` |
| `49be2a67` | `chore(net): ModBuild 432 — a force field never took back what it gave, the six slots were spent on fog, and the flags were never cloth` |

The commit **messages** for 430–432 are unusually detailed and carry evidence this document
compresses. If you resume this, read `git log 42221abf~6..49be2a67` in full before reading any code.

**None of ModBuilds 430–432's hand work was ever confirmed working on hardware.** 430 was reported
as "the smoke reacts but not immersively"; 431 as "still jerky, and the altar flame glitches around
the place"; **432 was tested and produced one new defect and the parking ruling** — the hanging
solver drove a door leaf (§7.0), *"Im neusten Test hat die Tür auch mit der Hand interagiert - das
soll auch raus."* No round of this feature was ever accepted.

---

## 11. If you resume this, start here

An honest assessment of what the next attempt should do differently.

**0. Write the exclusion list before the mechanism, and derive it from the objects you are NOT
thinking about.** §7.0 is the whole of this. Every threshold in the population rule was measured and
every one was correct, and the rule still adopted a door on its first hardware run, because it was
derived from a census of *walls*. Before writing a selector, enumerate the game's own identity
components for everything a hand can reach — `UnityGameEditorDoorProp`, `ActorBehaviour` /
`ActorEvents`, `Cloth`, `Rigidbody`, `TilesOcclusionVolume` — and refuse each one explicitly, by
component, in the same commit as the selector. This codebase already had the door walk in three
places when this shipped.

**1. Do the banners first, and do them alone.** The particle work and the banner work were carried
in the same builds and they are unrelated mechanisms with unrelated failure modes. The banner lane
is the smaller, more bounded piece: one solver, one spring, an exact local restore, no Unity
subsystem whose model has to be reasoned about — and the 432 log shows it running at **0.106 ms over
105 candidates**, which is a cost nobody has to argue about. Ship `SceneHangingHands` +
`SceneryActors` on their own, **with the door refusal from point 0**, read one log, and decide.

**2. The particle feature never had a bounded model, and the bound arrived last.** Three rounds
tuned an unbounded acceleration. The ceiling in §4a is the right shape and it is the thing that
should have been designed **first**: pick the displacement an effect is allowed, derive the speed,
derive the acceleration from the drag. If this is resumed, start from the bound and spend it — do
not start from a profile table and add a bound in round three.

**3. Distrust every threshold that touches the rig scale.** One term in six needed it and that one
term produced two of the round's defects (§7.4, and the `…Meters`-named-bound family this project
has been bitten by before). A future attempt should isolate scale-dependent terms into one place and
print the scale on every verdict that used one. The existing code already does the second half.

**4. The four-defect pattern in §7.9 and §7.10 is the real lesson.** Three separate classes shipped
`&& _scene.Count > 0` in a cadence early-out, and three separate instruments were mute, blind or
spent before the scenario existed. **A feature is not shipped until its instrument has produced one
line from a real room.** Every one of these classes now carries an unconditional sweep counter for
exactly that reason; keep it.

**5. Ask what a banner *is* before building anything that touches one.** Six builds of `SceneClothHands`
tuning were spent on a population that structurally could not contain a banner, and the answer was
one census line away the whole time. Before the next mechanism, get one hardware log that enumerates
the target population by component type.

**6. Bring `SceneryActors.cs` back with the first class that needs it, and read §7.8 first.** It is
removed here only because both its callers are. Its finding — that
`GetComponentInParent<ActorBehaviour>()` is structurally always false, and that `ActorEvents` is the
game's own marker — applies to *any* future class that has to tell scenery from a figure. Nothing in
`FigureClothHands` needs it, because that class starts from a held figure and walks down.

**7. Consider whether the feature is worth it at all.** That is the user's call and this document
does not argue it. What the record supports is narrower: **the particle feature consumed five
builds, produced no accepted result, and its last two rounds were spent fixing defects introduced by
the round before.** The banner feature consumed one build, was tested once, and the one thing that
run proved is that it moved a door. If it is resumed, resume the banners — with the exclusion list
written first.
