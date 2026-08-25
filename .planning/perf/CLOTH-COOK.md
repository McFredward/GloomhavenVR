# FigureGrab.HeldSize — the 172.93 ms frame (ModBuild 283 report, fixed in 284)

> "Ich habe in meinem neusten Test einen weiteres Performanceproblem festgestellt: **Wenn ich die
> Figuren in meiner Hand größer skaliere hat es immer angefangen zu hängen bzw. hatte ich kleinere
> Hänger.** Versuch das nachzuvollziehen. Logs liegen ab."

## What the log said

`.planning/debug/Player.log`, ModBuild 283, final 30 s window:

```
[Perf] STEPS 30.0s … FigureGrab.HeldSize 2.046ms avg, worst 172.93ms, 139.3ms/s, frames 2044 | …
[Perf] FRAME 30.0s n=2044 … p95 22.40 p99 178.39 max 183.57ms … over-budget 1693/2044 (82.8%)
                          … spikes 104 (49 lines rate-limited) … view height p50 9.1 (7.6..14.3)
```

Ten `SPIKE` lines name `FigureGrab.HeldSize` at 151.70–172.93 ms. **Ten is not the count.** That
window rate-limited 49 spike lines, and `p99 = 178.39` over `n = 2044` means ~20 frames sat above
178 ms. 20 × 171 ms ≈ 3.4 s against the step's 4.18 s window total — i.e. the spikes are essentially
the whole of it.

**The steady component is not a thing.** In the three preceding 30 s windows
`FigureGrab.HeldSize` does not appear in the `STEPS` top-6 **or** in the `TAIL` line, whose floor is
`1.0 ms/s` — so its cost with nothing being resized is under 0.011 ms/frame at 90 Hz, in windows
where `FigureGrab.OffsetAnchorSelect` (1.3–1.9 ms/s) shows figure-grab was active. The `2.046 ms
avg` is 4.18 s of spikes divided across 2044 frames, not a per-frame cost.

## Which call cost it

Measured, not reasoned at, in a Unity **2021.3.5f1 Linux standalone player** (the same editor the
bundle is built with), each call on its own frame, with a NULL control (empty timed body) and a
known-positive control (`AddComponent<Cloth>`, which must cook). At 3721 cloth vertices:

| call | ms |
|---|---|
| NULL control (instrument floor) | 0.0001 |
| `coefficients = next` (cloth enabled) | 0.0069 |
| `coefficients = next` (cloth disabled) | 0.0013 |
| `SetEnabledFading(false, .12)` | 0.0008 |
| `enabled = false` | 0.0023 |
| `ClearTransformMotion()` | 0.0006 |
| **`SetEnabledFading(TRUE, .12)`** | **19.37** |
| known-positive: `AddComponent<Cloth>` first cook | 19.2–20.3 |

Cost is linear in vertex count — **2.8 / 8.0 / 18.6 / 34.7 ms at 441 / 1681 / 3721 / 6561**, about
**5.3 µs per cloth vertex**. His 172.93 ms ÷ 3 cloths ÷ 5.3 µs puts his capes near **11 000
vertices** each.

**The attribution in the round brief was wrong.** It named `Cloth.coefficients` as the re-cook. The
coefficients write is three to four orders of magnitude cheaper than the enable. The mechanism:
`SetEnabledFading(false, …)` lets the component go `enabled = false` **by itself** once the blend
finishes (measured: it reads `False` 40 frames later with nobody writing it), so
`SetEnabledFading(true, …)` is an **enable transition** and PhysX re-cooks the fabric on the main
thread. The `if (!c.enabled) c.enabled = true;` backstop underneath it measured 0.0031 ms — it
looked innocent only because the line above had already paid.

**Dropping the `enabled` writes and keeping the fades does nothing** — measured at 20.45 ms,
unchanged, for exactly the reason above. The `enabled` writes were never the cost.

## The replacement, and why it is the same picture

Writing `maxDistance = 0` pins every particle onto its skinned position. Four arms, same authored
slack, root scaled 1.345×, drift against the authored mesh vertices (which for one identity bone
*are* the skinned positions), sampled at +1/+2/+5/+15/+45/+90 frames:

| arm | +1f | +2f | +5f | +15f | +45f | +90f |
|---|---|---|---|---|---|---|
| A cloth DISABLED (shipped behaviour) | 0.48790 | 0.48790 | 0.48790 | 0.48790 | 0.48790 | 0.48790 |
| B cloth PINNED, `maxDistance = 0` | 0.48788 | 0.48793 | 0.48790 | 0.48790 | 0.48790 | 0.48790 |
| C as B plus `ClearTransformMotion()` | 0.48788 | 0.48793 | 0.48790 | 0.48790 | 0.48790 | 0.48790 |
| D SIMULATING, coefficients untouched | 0.40827 | 0.54187 | 0.52367 | 0.41744 | 0.43625 | 0.42217 |

A and B agree to five decimals; D does not. A pinned cloth is indistinguishable from a disabled one
across a transform scale — which is the entire property the disable was bought for — at the price
of a coefficients write instead of a fabric cook. (D is also the ModBuild 137 defect stated as a
number: up to 0.080 off the reference, ~1.2× the rescaled slack, oscillating rather than settling.)

The pin engages in **one** frame (drift 0.04880 → 0.00121 at +1f, flat for 52 more), which is why
the shipped code ramps it over `FadeSeconds` instead of writing it once.

## End to end

The shipped algorithm transcribed into the same player and run head to head against the one it
replaces, same 3721-vertex cloth, scripted stretch (exponential 1 → 1.345 over 45 frames, then 60
quiet), third arm = cloth simply disabled for the whole gesture:

| arm | worst frame | total (105 f) | uploads | ENABLE transitions | worst drift |
|---|---|---|---|---|---|
| REFERENCE (disabled) | 0.45 ms | 0.47 ms | 0 | 0 | 0.00000 |
| OLD (137–283) | **29.12 ms** | 32.17 ms | 2 | **1** | 1.72018 |
| NEW (284) | **0.97 ms** | 3.77 ms | 27 | **0** | 0.06880 |

The NEW arm's 0.97 ms is its cold first upload; across the 43 frames it does any work the
distribution is **p50 0.101 ms, p90 0.119 ms**. Thirteen times the uploads, an eighth of the total,
zero enable transitions, and it holds the cape **25× closer** to the skinned pose during the resize
than the mechanism it replaces (the OLD arm's 1.72 is the frame its re-cook lands on and re-anchors
the cape in one step). Settled coefficients equal `pristine × the factor that actually settled` to
within 1.0e-9.

**These are desktop Linux milliseconds, not Quest 3 over Virtual Desktop.** Read the ratio, which is
a property of the algorithm; expect both columns larger on his rig, and roughly 3× larger again
because his capes are ~11k vertices rather than 3721.

## A latent bug found on the way

**Unity's "unconstrained" sentinel in a `ClothSkinningCoefficient` is `float.MaxValue`, not
`Infinity`.** Read back from a freshly added Cloth: every one of 1681 / 3721 / 6561 default
coefficients came back as `3.402823E+38` for both `maxDistance` and `collisionSphereDistance`, and
**none** as `Infinity`. Builds 137–283 guarded with `float.IsInfinity(max) ? max : max * factor`,
which therefore did not catch it and multiplied `float.MaxValue` by the factor — **overflowing to a
real `+Infinity`** on every unpainted vertex. Both values read as "unconstrained" to the solver so
nothing visibly broke, but the guard was not doing what its comment claimed. Fixed.

## Re-running the harness

`cloth-cook-harness/` holds the two files. To reproduce:

```
mkdir -p /tmp/clothbench/Assets/Scripts /tmp/clothbench/Assets/Editor
cp .planning/perf/cloth-cook-harness/ClothBench.cs /tmp/clothbench/Assets/Scripts/
cp .planning/perf/cloth-cook-harness/Builder.cs   /tmp/clothbench/Assets/Editor/
/home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics -quit \
    -projectPath /tmp/clothbench -executeMethod Builder.BuildLinux -logFile /tmp/clothbench/build.log
cd /tmp/clothbench && xvfb-run -a ./Build/clothbench -batchmode -logFile ./run.log
grep '\[BENCH\]' /tmp/clothbench/run.log
```

Two things that cost time and are worth not rediscovering:

* `-batchmode -nographics` **segfaults** this player before the first scene loads (NullGfxDevice).
  Run it under `xvfb-run -a` with `-batchmode` only.
* `Cloth.vertices` and `SkinnedMeshRenderer.BakeMesh` both report in the **scaled** frame. An early
  version of the drift metric normalised only one of them and reported the transform scale itself
  as cloth drift — which made a working pin look as broken as no pin at all. Normalise both, or
  compare two arms in the same frame and never an arm against an absolute.

## What still needs his hardware

The ramp is a **constraint** ramp, not the position-space blend `SetEnabledFading` performed. Both
take 0.12 s and neither can be seen from here. If it reads wrong on a figure held at arm's length,
the dial is `FigureCloth.FadeSeconds`, not the mechanism.

The number to read in his next log is the one that was already there:
`[Perf] STEPS … FigureGrab.HeldSize … worst`. It was **172.93 ms**. Beside it, two new rows say what
replaced it — `[Perf] STEPS FigureGrab.Cloth.Seed` (the upload cost) and `[Perf] COUNTS
FigureGrab.ClothSeeds` (how many uploads, and how many landed on the worst single frame), the
latter registered to **print even at zero** so an absent row cannot be mistaken for a silent
instrument.

---

# ModBuild 285 broke the cape. This is the measurement that says why (ModBuild 286 report)

> "Regression beim Skallieren: **Die Klamotten skallieren leider nicht mehr richtig mit**, siehe
> klamotten_problem.jpg. Wie man hier sieht hängt der ursprüngliche Umhang jetzt tiefer und kann
> nicht mehr als Umhang bezeichnet werden. **Auch die physics sollen beim skallieren (und danach)
> erhalten bleiben.**"

## The instrument was blind, and not for the reason anyone guessed

The whole ModBuild 286 log holds **one** `FIGURE SCALE` line and it reads `0 simulated, 0
constrained vertices`. That is not "FigureCloth never saw a cloth". `LogOnce` was a single
session-wide `_logged` latch and it fired on the FIRST figure resized in the session — which
happened to have no enabled `Cloth`. The same log's counters say what was really happening:

```
[Perf] COUNTS … FigureGrab.ClothSeeds  1/s (total  36, worst frame 3)
[Perf] COUNTS … FigureGrab.ClothSeeds 33/s (total 984, worst frame 3)
```

`Advance` returns before `PerfMonitor.Count` when `Cloths.Count == 0`, so **984 uploads is 984
proofs that cloths were captured and pinned** — on figures that one census line never described,
with three cloths landing on its worst frame. The latch is now per-population: one line for the
cloth-bearing case, one for the cloth-less case.

## The fabric is cooked in WORLD units, and 285 stopped cooking it

PhysX bakes a cloth's edge rest lengths when the component is enabled and never re-derives them
from a transform scale. Builds 137-283 paid for a re-cook by accident — their resume was an enable
transition. 285 removed the enable to remove the 172.93 ms frame, and removed the cook with it.

New arms, same Unity 2021.3.5f1 Linux player, a 1681-vertex sheet pinned along one edge and draped
under gravity, 300 settle frames per arm, **every position divided by the root scale before
comparison**:

| arm | mean edge / authored edge | worst vs POSITIVE | mean vs POSITIVE |
|---|---|---|---|
| NULL control (REF run twice) | identical to REF | 0.00000 | 0.00000 |
| REF, scale 1, settled | 1.036 | — | — |
| **POSITIVE: cloth BORN at 1.345** | **1.019** | — | — |
| **285 SHIPPED (no cook)** | **0.793** | **0.271 m** | **0.075 m** |
| OLD 137-283 (cooked) | 1.017 | 0.038 m | 0.015 m |
| **THIS FIX (cook on settle)** | **1.021** | **0.040 m** | **0.018 m** |

`0.793` is `1.036 / 1.345` — **the number IS the missing scale.** The fabric squeezes a cape a third
larger than the fabric believes it is, which is "hängt jetzt tiefer und kann nicht mehr als Umhang
bezeichnet werden" written as a number.

**There is no threshold below which skipping the cook is free.** At S = 1.08 the shipped arm reads
mean edge 0.941 against the positive control's 1.025; at S = 1.04, 0.974 against 1.029. The error
saturates almost immediately instead of scaling with the mismatch, so "only re-cook for big changes"
buys frames by shipping a visibly wrong cape.

**Two no-cook alternatives were tried and both are worse than the defect.** `stretchingStiffness = 0`
gives mean edge 1.308 with a most-deviating edge at 5.3x authored — it tears open instead of
bunching up. `useTethers = false` changes essentially nothing (0.938 against 0.941).

## The ORDER of the re-cook is the whole thing, and four arms died finding it

The settled coefficients must go up **before** the component goes down.

| sequence | enable cost | outcome |
|---|---|---|
| `enabled=false; enabled=true` in ONE frame | 2.6-3.1 ms | **dead** — flat 0.00000 sag to +300f |
| `enabled=false`, 1 frame, `enabled=true`, coefficients after | 0.8-2.7 ms | **dead** |
| `SetEnabledFading(false,0)` … `SetEnabledFading(true,0)`, coefficients after | 1.1-2.2 ms | **dead** |
| `SetEnabledFading(true)` with no disable at all | 0.002 ms | no cook, unchanged |
| **coefficients FIRST, then down, then up** | **14.6-21.1 ms** | **correct** |

An enable transition performed while every `maxDistance` is 0 **does not cook** — the cost says so —
and leaves the cloth permanently non-simulating. No later coefficient write revives it. This is a
property of the zero coefficients, not of which API is used.

## That same finding is a live hazard, which is why the pin no longer writes zero

The game disables every `m_Clothes` entry in `ActorBehaviour.ForceSetLocoIntermediateTarget`
(ActorBehaviour.cs:245-252) and re-enables them two frames later in `LateUpdate`
(ActorBehaviour.cs:551-562). **A figure being carried is exactly the figure whose locomotion target
gets forced**, so that pair lands inside the pin window — an enable transition this mod does not own.

| arm | outcome |
|---|---|
| foreign disable+enable mid-gesture, pin at **exactly 0** | **cape permanently dead**, 0.00000 sag to +300f |
| the same, pin floored at **1e-3 x the cape extent** | alive, and lands **0.0355** from POSITIVE — better than the OLD path's 0.0378 |

With the floor, the game's own re-enable cooks the fabric for us, correctly and for free.

## What a cook costs, and how often it is paid

8 reps per cell, median. NULL control 0.0000-0.0001 ms; known-positive `AddComponent<Cloth>` in the
same run:

| | 1681 verts | 3721 verts | 6561 verts |
|---|---|---|---|
| `enabled = false` | 0.025 ms | 0.021 ms | 0.023 ms |
| **`enabled = true` after a proper down** | **20.06 ms** | **34.29 ms** | **56.45 ms** |
| positive control: first cook | 17.09 ms | 24.67 ms | 40.24 ms |

Paid **once per settled size change per cloth, never during the gesture, and ONE CLOTH PER FRAME.**
His ModBuild 283 worst frame was 172.93 ms and that was three cloths cooking together; staggered,
the same work is three frames of about a third that each. `SettleFrames` went 3 → 12 for the same
reason: a settle used to cost 0.1 ms and now costs a cook.

## Assigning the collider arrays does NOT cook (the free-hand feature)

| call | 1681 | 3721 | 6561 |
|---|---|---|---|
| `sphereColliders = pairs` (first) | 0.1355 ms | 0.0165 ms | 0.0165 ms |
| `sphereColliders = pairs` (re-assign) | 0.0100 ms | 0.0089 ms | 0.0083 ms |
| `capsuleColliders = one capsule` | 0.0086 ms | 0.0103 ms | 0.0100 ms |
| `sphereColliders = empty` | 0.0047 ms | 0.0099 ms | 0.0080 ms |
| moving an assigned collider's transform | 0.0044 ms | 0.0041 ms | 0.0041 ms |

Three orders of magnitude under a cook and — the part that settles it — **flat in vertex count**,
while a cook is linear in it. The arrays could be written every frame. They are written once per
attach anyway.

## `Physics.autoSyncTransforms = false` does NOT break a cloth collider — measured

`PhysicsController.Setup` (PhysicsController.cs:18-31) writes `Physics.autoSyncTransforms = false`
when `PlatformLayer.Setting.SimplifyPhysics` is on AND the scene is `"Game_gamepad"`
(`SceneController.cs:1069` passes exactly that predicate; `SceneController.cs:211` picks that scene
name when `InputManager.GamePadInUse`). **His ModBuild 286 log contains `Added scene: Game_gamepad`,
so the scene half of the predicate IS met on his rig.** The `SimplifyPhysics` half is a serialized
platform setting and cannot be read from a log — it is NOT checked and is NOT assumed either way.

It does not matter for `Cloth`. A sphere-pair collider swept through a settled cloth, moved in
`Update` exactly as `FigureClothHands` moves it, at `fixedDeltaTime = 1/30` against a 90 Hz render
loop — the shape that makes a missed sync visible rather than theoretical:

| arm | worst cloth displacement from rest |
|---|---|
| NULL control (same sweep, NO collider assigned) | 0.01923 m |
| POSITIVE (`autoSyncTransforms = true`, Unity's default) | 0.08774 m |
| **HAZARD (`autoSyncTransforms = false`, nothing else done)** | **0.08585 m** |
| REMEDY (`= false` + `Physics.SyncTransforms()` after the move) | 0.08805 m |

If the collider were not reaching the solver, HAZARD would read the NULL control's 0.019. It reads
0.086 — within 2 % of POSITIVE, against a 0.019 noise floor. **Unity's `Cloth` takes its collider
poses from the managed transform, not from the PhysX scene pose, so no `Physics.SyncTransforms()` is
added and none is needed.** `Physics.SyncTransforms()` measured 0.0380 ms median in a three-object
scene, which is a floor, not the number it would cost in a scenario.

**This result does NOT transfer to particle-system collision**, which goes through
`OnParticleCollision` and the PhysX scene. Anything built on that must re-run this arm for itself.

## Re-running the three new benches

`ScaleShapeBench.cs` (settled shape), `CookCostBench.cs` (costs) and `ColliderSyncBench.cs`
(autoSyncTransforms) drop into the same project as `ClothBench.cs`:

```
mkdir -p /tmp/clothscale/Assets/Scripts /tmp/clothscale/Assets/Editor
cp .planning/perf/cloth-cook-harness/*Bench.cs /tmp/clothscale/Assets/Scripts/
cp .planning/perf/cloth-cook-harness/Builder.cs /tmp/clothscale/Assets/Editor/
/home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics -quit \
    -projectPath /tmp/clothscale -executeMethod Builder.BuildLinux -logFile /tmp/clothscale/build.log
cd /tmp/clothscale
xvfb-run -a ./Build/clothbench -batchmode                     -logFile ./shape.log
xvfb-run -a ./Build/clothbench -batchmode --S=1.08            -logFile ./s108.log
xvfb-run -a ./Build/clothbench -batchmode --arms=min --S=1.04 -logFile ./s104.log
xvfb-run -a ./Build/clothbench -batchmode --bench=cost        -logFile ./cost.log
xvfb-run -a ./Build/clothbench -batchmode --bench=sync        -logFile ./sync.log
```

The `-nographics` segfault and the `Cloth.vertices` scaled-frame trap recorded above both still
apply. **A third trap:** every bench auto-boots, so they gate on `--bench=` — `--bench=cook` selects
the old round-6 harness, `--bench=cost` the cost sweep, `--bench=sync` the collider sync arms, and
no flag at all runs the shape bench.

**A fourth:** the shape metrics are not all equally robust. `worst per-vertex` compares WRINKLE
PATTERNS of a large sheet and a drape has many metastable folds, so it is noisy between arms that
are physically equivalent. **`mean edge / authored edge` is the discriminator** — it is the fabric's
own rest length read back, it has no basin dependence, and it is what separates 0.793 from 1.021.

## What still needs his hardware

* Whether one 20-56 ms frame per cloth at the END of a resize reads as acceptable where nine of
  them during it did not. The numbers to read are the two new rows: `[Perf] COUNTS
  FigureGrab.ClothCooks` — whose **`worst frame` must be 1**, which is the entire claim of the
  stagger — and `[Perf] STEPS FigureGrab.Cloth.Cook`.
* Whether `SettleFrames = 12` (0.13 s) reads as "it waits" or as nothing at all.
* The free hand's two radii, and whether the free-hand collider fights the two-hand stretch gesture,
  which arms in the same place. Dial: `[FigureGrab] ClothFollowsFreeHand`.
