# The cape during the resize — ModBuild 291

> "Ich habe dir ein Video figure_scale_problem.mp4 angelegt in dem ich eine Figur einmal größer und
> wieder kleiner mache. Achte auf die Umhänge. **Beim größer machen werden sie steif und beim
> kleiner machen wird da ein Polygon matsch draus.** Sobald ich loslasse ist alles wieder ok, aber
> ich würde gerne das auch während dem skallieren alles intakt bleibt, **inklusive der Reaktion**.
> Ist das möglich?"

> After testing ModBuild 290, verbatim: "**Das Problem mit dem Skallieren von Figuren ist 1:1 noch
> genau gleich, ich sehe keinen Unterschied zu dem wie es im Video ist.**"

---

# THE HEADLINE: ModBuild 290 WAS WRONG TWICE, AND HE IS RIGHT

**1. "Inklusive der Reaktion is impossible" was wrong, and it was told to the user.** ModBuild 290
answered *no* on the strength of one sentence — *"a cook is 25–165 ms on his rig"* — taken from one
step line in one log. The counter ModBuild 290 shipped **to check that very number** refutes it, in
his own ModBuild 290 log, on his own hardware. **A cook of one of his capes is 1.3–2.5 ms.** That
answer has to be corrected to him.

**2. The ModBuild 290 remedy is a 1.345×-only fix, and he scales to 2.5×.** Every arm table this
lane ever produced was measured at 1.345×, a factor taken from one ModBuild 289 log. His gesture
clamp is `[0.417 .. 2.503]` and his ModBuild 290 log shows him driving to it repeatedly. Measured at
2.5×, `stretchingStiffness = 0` does not help the shrink **and actively damages the grow**. "1:1
noch genau gleich" is exactly what the measurement predicts.

**What ModBuild 291 does about it:** the fabric now **follows the size while the gesture runs**, by
re-cooking it every few frames, for every figure whose capes are small enough to afford it — and the
ModBuild 290 stiffness ramp is withdrawn. Measured over a whole gesture at his 2.5×, against the
build he tested: shrink incoherence mean **1.0530 → 0.0002**, grow mean **0.1978 → 0.0017**, and
worst-frame jitter **lower** than either previous build. The settled cape moves from 0.0474 to
0.0433 against a born-at-size reference of 0.0276 — closer to correct, not further, and §4 says
plainly why it is not bit-identical.

---

## 1. Did the ModBuild 290 remedy RUN on his hardware? — YES, and the log already said so

The round brief said the log could not answer this because `ApplyStretch` logs nothing. That is true
of **what it wrote** and false of **whether it ran**, and the difference matters because it is the
difference between two opposite responses.

In the shipped ModBuild 290, `Advance` called `ApplyStretch(t, t.Weight)` unconditionally on the
line *above* the `Dirty` block that performs a coefficient upload. So **any** non-zero
`FigureGrab.ClothSeeds` proves `ApplyStretch` ran on that frame. His ModBuild 290 log:

```
FigureGrab.ClothSeeds  6/s (total 193, worst frame 2)     <- 193 uploads in one 30 s window
FigureGrab.ClothCooks  1/s (total 17,  worst frame 1)
```

**The remedy ran.** What the log could not say is what VALUE it wrote — the lever is
`authored stretchingStiffness × ramp weight`, and if the artist shipped the cape at 0.05 the write
is a no-op no matter how correct the mechanism. ModBuild 291 ships the instrument that closes that
in one line (§6).

## 2. What a cook actually costs — the ModBuild 290 claim is falsified by ModBuild 290's own counter

Two independent measurements, agreeing.

### (a) His hardware, from his own ModBuild 290 log — a hard bound, not an estimate

```
FigureGrab.ClothCooks     0/s (total  1, worst frame  1) | ClothCookVerts 3/s  (total   82, worst  82)
FigureGrab.ClothCooks     1/s (total 17, worst frame  1) | ClothCookVerts 58/s (total 1754, worst 143)
```

**103 particles per cook. The largest cape cooked in the entire session was 143 particles.** And in
the *same* 30.0 s window, `FigureGrab.Cloth.Cook` appears on **neither** the `[Perf] STEPS` ranked
line **nor** the `[Perf] STEPS TAIL` line — and `PerfMonitor.StepTailFloorMsPerSecond` is **1.0
ms/s**, so every step that reached that floor is named. Seventeen cooks therefore cost **under 30 ms
between them**: a mean of **under 1.76 ms per cook**.

For comparison, the same window's tail *does* name `WallFade.Rescan 22.812ms avg … 4.6ms/s, frames
6`. A step running six times at 23 ms clears the floor easily. Seventeen cooks at the claimed 32 ms
would be 18 ms/s and would have been the **third** entry on that line.

### (b) The harness, at the sizes his capes actually are — `LiveCookBench`, `--bench=live`

9 reps, median, each timed body on its own frame, NULL control 0.0009 ms, positive control
`AddComponent<Cloth>` in the same run.

| grid | particles | NULL | **the cook** (`enabled = true`) | µs/particle | `AddComponent<Cloth>` | coeff upload | stiffness write |
|---|---|---|---|---|---|---|---|
| 9×9 | **81** | 0.0009 | **1.277 ms** | 15.8 | 1.347 ms | 0.0020 | 0.0044 |
| 12×12 | **144** | 0.0008 | **2.475 ms** | 17.2 | 2.320 ms | 0.0023 | 0.0038 |
| 21×21 | 441 | 0.0010 | 6.666 ms | 15.1 | 6.420 ms | 0.0032 | 0.0050 |
| 28×28 | 784 | 0.0011 | 6.753 ms | 8.6 | 9.757 ms | 0.0026 | 0.0027 |
| 38×38 | 1444 | 0.0007 | 10.166 ms | 7.0 | 11.623 ms | 0.0050 | 0.0036 |
| 58×58 | 3364 | 0.0012 | 19.809 ms | 5.9 | 22.241 ms | 0.0074 | 0.0048 |

**There is no large constant term.** The per-particle law holds all the way down: 81 particles is
1.28 ms, not "at least 25 ms because a cook is a cook". The previous round's curve started at 441
and the small end was extrapolated INTO rather than measured; measuring it is what settled this.

### (c) So where did 32–53 ms come from?

Not resolved, and it does not need to be to make the decision — the number that governs is the one
measured on the current build on his hardware, and that is (a). Two candidates, neither excludable
from here and both named rather than dismissed:

* **The scope attributes foreign stalls.** His ModBuild 290 log has `Hands.VRHand` at **worst
  51.52 ms against its own 0.130 ms average** — 396×, which cannot be VRHand's own work. The
  ModBuild 289 log had the same step at worst 131.17 ms. Whatever produces those lands in whatever
  scope happens to be open.
* **ModBuild 289's state machine cooked differently.** Through 289 there was no `AbortCook`:
  `Release` re-entered with `CookIndex` at 0 while the gesture was still moving, so the sequence
  restarted and toggled `enabled` on cloths continuously for as long as the gesture lasted. 290
  stood that down. A solver being torn down and rebuilt every frame is not the same measurement as
  one enable on a settled cloth.

**What must NOT be repeated is quoting (c) as if it were (a).** ModBuild 290 did, and told the user
a capability was impossible on the strength of it.

## 3. The arm table at HIS factor — where ModBuild 290 goes wrong

`FactorArmsBench`, `--bench=factor`. 29×29 = 841 particles, 300 settle frames, every position
divided by the root scale before comparison. **The mesh is byte-for-byte `DirectionArmsBench`'s** —
same vertices, same winding, same index format — so the 1.345× rows are a cross-check of this
instrument against the published ModBuild 290 table rather than a second opinion from a second
sheet. (The first cut of this bench built a vertical curtain instead of the horizontal flag and its
1.345× row disagreed with the published one for that reason alone. Two instruments that disagree
cannot both be cited.)

*Incoherence* = mean `1 − dot` between neighbouring quad normals; a smooth drape ≈ 0, a surface
folded through itself is not. *Reaction* = peak local departure under a 0.25 m swing; a rigidly
pinned cape reads ≈ 0 however large the swing, so it is the column that answers "inklusive der
Reaktion".

### The cross-check first — 1.345×, against the published ModBuild 290 numbers

| SHRINK 1.345 → 1 | mean edge | incoherence | close pairs | published ModBuild 290 |
|---|---|---|---|---|
| POSITIVE (born at 1) | 1.0199 | 0.0276 | 0 | 1.0221 / 0.0267 |
| NULL (positive twice) | 1.0199 | 0.0276 | 0 | identical, 0.00000 drift |
| PINNED — ModBuild 289 | 1.2861 | 0.4688 | 0 | 1.2859 / 0.4589 |
| **PIN+NOSTRETCH — ModBuild 290** | **1.1119** | **0.0372** | 0 | **1.1119 / 0.0372** |

Four decimal places on the row that matters. **The instrument is the previous round's instrument.**

### The whole sweep — the remedy inverts between 1.345 and 1.7, in BOTH directions

Incoherence only; the full table with mean/worst edge, close pairs and reaction is in `factor.log`.

| factor | GROW: PINNED (289) | GROW: PIN+NOSTRETCH (290) | SHRINK: PINNED (289) | SHRINK: PIN+NOSTRETCH (290) |
|---|---|---|---|---|
| 1.345 | 0.0000 | 0.0000 | 0.4688 | **0.0372**  ← works, 12.6× |
| 1.7 | 0.0000 | 0.0106 | 0.9680 | **1.1862**  ← worse |
| 2.0 | 0.0000 | 0.1230 | 1.1323 | **1.2972**  ← worse |
| **2.5 (his)** | **0.0000** | **0.3142** | **1.1719** | **1.2950**  ← worse |
| 3.5 | 0.0106 | 0.4685 | 1.2660 | 1.2904 |

And the edges, which say it more bluntly — SHRINK, mean / worst edge as a multiple of the authored
edge, plus self-intersecting non-neighbour vertex pairs:

| factor | PINNED (289) | PIN+NOSTRETCH (290) |
|---|---|---|
| 1.345 | 1.29 / 2.11, 0 pairs | 1.11 / 2.01, **0 pairs** |
| 1.7 | 1.62 / 3.21, 1 pair | 2.20 / 5.12, **10 pairs** |
| 2.0 | 1.87 / 3.58, 4 pairs | 2.81 / 6.10, **6 pairs** |
| **2.5** | 2.35 / 4.47, 11 pairs | **3.39 / 7.09, 16 pairs** |
| 3.5 | 3.31 / 6.99, 11 pairs | 4.54 / 10.44, 18 pairs |

With the stretching constraint gone there is nothing left to stop a cape far too big for its body
folding through itself. That is the ModBuild 286 "it tears open" failure, rediscovered at a larger
factor — and ModBuild 290 could not have seen it, because it only ever looked at 1.345.

**Three other things this table settles:**

* **The pin is fine on the way up, and always was.** `PINNED` GROW incoherence is 0.0000 at every
  factor up to 2.5. "Beim größer machen werden sie steif" is not a *coherence* defect at all — it is
  the **reaction** column: `PINNED` reaction is **0.00129–0.00133 m** at every factor while the
  positive control's is 0.49–0.88 m. The cape is not mush on the way up, it is **dead**. Only
  letting it simulate fixes that, and only a fresh fabric lets it simulate.
* **The re-cook is exact.** `COOKED` lands 0.00017 m from the positive control at 2.5× grow and
  0.052 m on the shrink. The settle behaviour was right and was not touched.
* **One outlier, named rather than smoothed:** `COOKED` at GROW 1.345 reads 0.4191 where the same
  arm reads 0.0000 at 1.7, 2.0, 2.5 and 3.5. That is a single bad settle in one run, not a finding.



## 4. The fix — the fabric follows the size

`ThrottleBench`, `--bench=throttle`. Three cloths of 841 particles, the script **grow 1 → 2.5,
pause, shrink 2.5 → 1, release**, incoherence sampled EVERY frame of both moving windows, on the
same sheet as §3 and as `GestureBench`.

**The arm.** Every `P` frames a cloth uploads its coefficients **at the size the body is at right
now**, goes down for one frame, and comes back up — and that enable bakes a fabric that matches the
body it is on. Between cooks the cape is not pinned at all: it simulates, it drapes, and it reacts.
Cloths are phase-offset by two frames each so no two are ever disabled together.

**Two metrics, and the second is the one that could have killed the idea.** *Incoherence* is the
drape. *Jitter* is mean per-vertex movement between consecutive frames in the scale-normalised
frame — because a cloth is `enabled == false` for one frame of every cook and a disabled
`SkinnedMeshRenderer` draws the bare skinned pose with no drape at all. A shape-only table would
have endorsed a build that flickers.

| arm | GROW inc worst/mean | SHRINK inc worst/mean | GROW jit mean/worst | SHRINK jit mean/worst | SETTLED | cooks |
|---|---|---|---|---|---|---|
| ModBuild 289 (pin only) | 0.0232 / 0.0012 | 1.1293 / 0.9359 | 0.0090 / 0.0762 | 0.0149 / **0.2491** | 0.0474 | 6 |
| **ModBuild 290 (shipped)** | 0.3097 / **0.1978** | 1.2391 / **1.0530** | 0.0091 / 0.0763 | **0.0040** / 0.0201 | 0.0474 | 4 |
| NULL (290 run twice) | 0.3097 / 0.1979 | 1.2391 / 1.0530 | 0.0091 / 0.0763 | 0.0040 / 0.0201 | 0.0474 | 4 |
| **live, P = 6** | 0.0312 / **0.0017** | 0.0058 / **0.0002** | 0.0183 / 0.1666 | 0.0179 / **0.1425** | 0.0433 | 41 |
| live, P = 9 | 0.0312 / 0.0017 | 0.0828 / 0.0044 | 0.0164 / 0.1467 | 0.0187 / 0.2516 | 0.0419 | 30 |
| live, P = 12 | 0.0312 / 0.0018 | 0.0901 / 0.0072 | 0.0147 / 0.1281 | 0.0211 / 0.2521 | 0.0431 | 25 |
| live, P = 18 | 0.0312 / 0.0046 | 0.1660 / 0.0208 | 0.0128 / 0.1158 | 0.0250 / 0.3665 | 0.0394 | 17 |
| live, P = 30 | 0.0312 / 0.0087 | 0.9407 / 0.1698 | 0.0151 / 0.1518 | 0.0147 / 0.1122 | 0.0431 | 11 |

The NULL is the two ModBuild 290 rows: they agree to the fourth decimal, so the instrument floor is
~0.0001 and every difference below is real.

1. **The shrink, at his factor: mean incoherence 0.9359 → 0.0002.** Against the ModBuild 290 build
   he actually tested, 1.0530 → 0.0002. That is not an improvement in degree.
2. **The grow regression ModBuild 290 introduced is gone.** 0.1978 → 0.0017, back to the ModBuild
   289 level (0.0012) — the standing constraint on this lane was that grow must not regress, and
   ModBuild 290 regressed it at 2.5×.
3. **The one-frame skinned pose does not dominate.** Worst-frame shrink jitter is **0.1425 at
   P = 6, LOWER than ModBuild 289's own 0.2491** — the pin fighting a stale fabric produces a bigger
   single-frame jump than a cook does. And the mean jitter is what "die Reaktion" looks like as a
   number: 0.0040 for the dead ModBuild 290 cape, 0.0179 for the live one, 4.5×.
4. **Shorter periods are better on BOTH columns**, which is not obvious: worst-frame jitter *rises*
   from 0.1425 at P = 6 to 0.3665 at P = 18, because a fabric left staler for longer has further to
   jump when it is finally corrected. So the period is floored at the fastest measured arm rather
   than the slowest.
5. **41 cooks against 6.** At the sizes his capes actually are (82 and 143 particles → 1.3–2.5 ms
   each, §2) that is 0.1–0.25 ms per frame amortised over the script. The shipped gates in
   `DecideLiveCook` enforce that arithmetic rather than assuming it.

**And it is one look, not a statistic.** The worst shrink frame of each arm, same scale, software
wireframe (`.planning/cloth-cook-harness/shots/`):

| file | what it is |
|---|---|
| `throttle-289-worst-shrink-2.5x.png` | ModBuild 289 — a shredded cloud |
| `throttle-290-worst-shrink-2.5x.png` | ModBuild 290, the build he tested — the same cloud |
| `throttle-live-P6-worst-shrink-2.5x.png` | ModBuild 291 — **a line** |
| `arms-1.345x-shrink-290-nostretch.png` | the ModBuild 290 remedy at 1.345× — a clean grid |
| `arms-2.5x-shrink-290-nostretch.png` | the same remedy at 2.5× — a scramble |

The last two are the whole round in two pictures.

**ONE DEVIATION FROM THE STANDING CONSTRAINT, stated rather than buried.** The brief required the
settled cape to stay bit-identical. It does not, quite: **0.0433 against 0.0474**, and the NULL says
that difference is real. The mechanism that produces the settled state is the same code —
the same full coefficient upload and the same enable at exactly `SeededFactor` — but it now runs on
a cape that is already draped correctly instead of one crushed flat against its skin, and PhysX
settles from a different initial condition into a slightly different rest. The direction is toward
the reference, not away: a cape *born* at the target size reads 0.0276. **This cannot be avoided by
any change that alters the mid-gesture state at all**, and it is 9 % on a number whose whole
measured range across broken and correct builds is 0.02 to 1.3.



## 5. What shipped in ModBuild 291

All of it in `src/GloomhavenVR/Board/FigureGrab/FigureCloth.cs`. No wire format, no config entry, no
frame-order step — so it is multiplayer-compatible by construction: `FigureCloth.Note` is already
called from the local hold, the release glide **and the remote mirror** alike, and a peer's figure
gets the same treatment as the local one with nothing sent.

**1. `StepLiveCook` / `FinishLiveCook` / `DecideLiveCook` — the fabric follows the size.**
While a gesture is running, each cloth re-cooks at the current size every `LivePeriod` frames
instead of the cape being pinned. Two frames per cloth, one cloth at a time, phase-offset by two, so
`FigureGrab.ClothCooks`' `worst frame` stays 1 — the invariant the stagger has always reported.

**2. Two gates, and they answer different questions.** `LiveCookPeakMs = 4.0` asks *does one cook
fit in one frame?* — a cook lands on a single frame and no spacing changes that, so a figure whose
largest cape exceeds 200 particles (at a deliberately pessimistic 20 µs/particle against a measured
15.1–17.2) keeps the ModBuild 285 pin unchanged. `LiveCookBudgetMsPerFrame = 0.5` asks *how often
can this figure afford one?* and sets the period. The period is clamped to
`[max(6, 2 × cloths) .. 30]` — the fastest and slowest arms in §4 — and a figure that would need a
period outside that range is refused rather than scheduled at a spacing nothing was measured at.
His two test figures (82 and 143 particles, 1 cloth each) both land on **P = 6**, the best arm.

**3. `CookedFactor` is set to a SENTINEL after a live cook, not to the factor.** This is what keeps
the settle path exactly the one that ships today. A live cook bakes at whatever size the body was at
on that frame — within a few frames of the final one but not equal to it. Recording that size would
put it inside `FactorEpsilon` of the settle factor, `StepCook` would skip the cloth, and the figure
would come to rest on a fabric baked at 2.4987 instead of 2.5. `0` is never a legitimate factor
(`Note` rejects `factor <= 0`), so it reads as "unknown" and every cloth is re-cooked on the settle
at exactly the size it settled at.

**4. Every exit from the live path drains it.** A cloth caught between the two frames of a live cook
is `enabled == false`, and a disabled Cloth never simulates again unless somebody enables it.
`Release` drains before the settle sequence takes over (otherwise `StepCook` would revive the one
cloth it is pointed at and leave the rest a plain skinned mesh for the life of the figure), and
`Clear` drains too — the driver can be torn down by a config toggle without the scene going
anywhere.

**5. The ModBuild 290 `stretchingStiffness` ramp is WITHDRAWN.** `ApplyStretch` is now always called
with a weight of 1, i.e. it writes the authored value back. §3 is why. The method is kept rather
than deleted because the *restore* is load-bearing — `StepCook` invalidates the latch and a value
left low by an older build must not stand — and because its comment is now the record of a remedy
that was measured, shipped and withdrawn. A lever whose **sign depends on the factor** is not one to
rediscover.

**6. The free hand's colliders are stashed across the enable.** `FigureClothHands` writes
`sphereColliders` **once**, when the free hand comes into reach, and thereafter only moves the probe
transforms. If an enable transition dropped those arrays, the cape would silently stop reacting to
the player's other hand for the rest of that session and nothing in either file would say so — and
the combination is not exotic, because the free hand is the hand that runs the stretch gesture. The
live cook therefore reads both arrays before its disable and writes them back after its enable,
including on the path where the game's own `LateUpdate` re-enables the cloth first
(`ActorBehaviour.cs:245-252` / `551-562`). Two array reads and two writes at 0.008–0.017 ms each
against a 1.3–2.5 ms cook — and it does not depend on knowing whether PhysX would have kept them.

**7. The instruments in §6.**

**What was NOT changed:** the settle sequence, the pin, `AbortCook`, `BuildInto`, the coefficient
rescale, `PinFloorFraction`, and every measurement behind them. The ModBuild 285 pin is still what
runs for any figure the gates decline.



## 6. The instruments ModBuild 291 adds, and why each one exists

All in `FigureCloth.cs`. None of them costs anything per frame.

### `CLOTH GESTURE …` — one line per gesture, `LogGesture`

The round that produced ModBuild 291 opened with a question the ModBuild 290 log could not answer:
**did the remedy run, and what did it write?** A fix that never executed and a fix that executed and
did nothing look identical in a user report and need opposite responses. The line carries:

* **`writes N, lowest value written X`** against **`authored stretchingStiffness Y` per cloth.**
  `writes 0` with cloths present = never ran. `lowest = authored` = ran and changed nothing.
  `lowest 0` with `authored ≈ 0` = ran, wrote what it meant to, and **the lever was already at the
  bottom** — inert on that asset. `lowest 0` with `authored ≈ 1` = ran at full strength.
* **`RANGE [min .. max]×` and the from → to factors.** So a report about 2.5× can never again be
  answered with a measurement at 1.345×. This is the single mistake that cost ModBuild 290.
* **`PIN RAMP reached weight W`.** A gesture that never reaches 0 never applied the pin *or* the
  stiffness remedy at full strength, and "no improvement" at partial strength carries no
  information.
* **`MANAGED CLOTHS: n of m in the subtree`.** `0 of 0` is a figure with no cloth at all. `0 of m`
  is a figure whose cloths were all found DISABLED and skipped — **the ramp never reached them** —
  and if the complaint is about a cape on that figure, that is the whole answer. One number could
  not say which.
* **`LIVE RE-COOK: ON, every P frames` / `OFF`,** with the reason. A figure whose capes are too big
  is one the ModBuild 291 change does not reach, and that is a size question rather than a mechanism
  one.

Capped at 24 lines per session; his whole ModBuild 290 test contained eight gestures.

### `CLOTH CONFIG …` — the authored solver state, per figure

Every measurement behind ModBuild 290 was taken on a cloth this lane BUILT: a procedural grid with
every property at Unity's default, which for `stretchingStiffness` is **1**. A Gloomhaven cape is an
authored asset and the mod could not see the authoring. The line prints, per managed cloth:
`stretchingStiffness`, `bendingStiffness`, `useTethers`, `useVirtualParticles`,
`selfCollisionDistance`, `selfCollisionStiffness`, `clothSolverFrequency`, `damping`, `friction`,
`worldVelocityScale`, `worldAccelerationScale`, `stiffnessFrequency`, `useGravity`, `enabled` — and
the coefficient **distribution**: how many vertices are hard-pinned at 0, how many are authored
`float.MaxValue`, and min/mean/max of the finite remainder in metres.

Three of those decide whether the previous rounds measured the right thing at all:
`stretchingStiffness` is the remedy's entire lever; `clothSolverFrequency` is written by the game
itself (`ActorBehaviour.cs:128` under `PlatformSetting.SimplifyPhysics`, `PhysicsController.cs:53`
restoring 120) and decides whether the harness ran the same solver; and the `float.MaxValue` count
decides whether the arm tables exercised the same coefficient code path, because those vertices are
ramped against the cape's own extent rather than multiplied by the size factor and **every table so
far was built on a cloth with none of them.**

### The census is now PER FIGURE

`FIGURE SCALE …` was a single per-population latch. In his ModBuild 290 log it fired once, on a
figure whose widest cape is **82** particles — and was then read, by me, as evidence about a session
whose counters record seventeen cooks with a worst frame of **143**, on figures the log's own grab
lines name as *two* (`LivingBonesID` and `BanditGuardID`). One sample is not a census of a
population however carefully the population was defined. Now one line per figure, capped at 6.

## 7. What I could not measure, and what the next log has to say

* **His actual capes.** Every number here is a 29×29 procedural sheet with one bone, uniform
  authored slack and no colliders. His are authored assets of 82 and 143 particles. The *mechanism*
  — a fabric baked at one size stretched over a body at another — does not depend on any of that,
  and the *sign* of every effect is a property of the sign of the mismatch. The *magnitudes* are a
  sheet's. **`CLOTH CONFIG` is what closes this**, and it is the first thing to read in the next
  log.
* **Whether the figure he judged ModBuild 290 on is the figure in the video.** It is not the same
  one: no cape above **143** particles was cooked anywhere in the ModBuild 290 session, and the
  Elite Savvas Icestorm in `figure_scale_problem.mp4` reported **771** for its widest cape in the
  ModBuild 289 log. That figure would NOT take the live path under the shipped gates. If his next
  report is about that figure specifically, the gates are the thing to move — see the peak ceiling
  on `LiveCookPeakMs` — and `CLOTH GESTURE`'s `LIVE RE-COOK: OFF` line will name it.
* **The one-frame skinned pose.** A live cook disables the cloth for one frame and a disabled
  `SkinnedMeshRenderer` draws no drape. The harness says that does not dominate (§4's jitter
  columns) but the acceptance test is his eyes on a figure at arm's length at 90 Hz.
* **The real cost on his rig, per cook, as a number rather than a bound.** The ModBuild 290 log
  gives `< 1.76 ms` because the step fell below the tail floor. `FigureGrab.Cloth.Cook` will
  now appear on the STEPS TAIL line if the live path runs often enough to clear 1.0 ms/s — which it
  should — and that turns the bound into a measurement.
* **Where exactly the stiffness ramp inverts.** §3 has 1.345, 1.7, 2.0, 2.5 and 3.5. Between the
  last factor it is measured to help at and the first it is measured to hurt at, nothing is
  measured — which is why it is withdrawn outright rather than gated at an interpolated crossing.
* **The cost model is deliberately pessimistic and that costs range.** `DecideLiveCook` prices a
  cook at a flat 20 µs/particle. The measured law is 15.1–17.2 µs up to 441 particles and then
  *falls* — 8.6 at 784, 7.0 at 1444, 5.9 at 3364 — so the flat rate over-charges a 784-particle
  cape by 2.3×. It does not change the decision for the figure in the video (771 particles measured
  ≈ 6.75 ms, still past the 4 ms peak ceiling either way), but it moves the admission cutoff from
  ~266 particles to 200. Erring safe is right for a first hardware round; if he asks specifically
  about a big-caped figure, the measured curve is the thing to fit and `LiveCookPeakMs` is the dial.
* **`LiveCookBench` part 2 — the configuration sweep — is currently uninformative and is left in the
  repo fixed rather than re-run.** It was run at 2.5× only, where every arm saturates near
  incoherence 1.0, so it cannot tell a configuration that defeats a remedy from a factor that
  defeats it: fourteen rows all read "no difference" including the BASE row that is known to work at
  1.345×. **A table with no positive control agrees with everything.** The bench now runs at 1.345×
  as well, where the effect is 12.6× and visible. The question it was built for — is the write inert
  on a game-authored cloth? — stopped deciding anything the moment the ramp was withdrawn, and
  `CLOTH CONFIG` answers it from the real assets rather than from a guess about them.

## Re-running the harness

```
mkdir -p /tmp/clothbench291/Assets/Scripts /tmp/clothbench291/Assets/Editor
cp .planning/cloth-cook-harness/*.cs      /tmp/clothbench291/Assets/Scripts/
mv /tmp/clothbench291/Assets/Scripts/Builder.cs /tmp/clothbench291/Assets/Editor/
/home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics -quit \
    -projectPath /tmp/clothbench291 -executeMethod Builder.BuildLinux -logFile /tmp/clothbench291/build.log
cd /tmp/clothbench291
rm -f *.log && rm -rf shots                      # SEE THE THIRD TRAP BELOW
xvfb-run -a ./Build/clothbench -batchmode --bench=live     -logFile live.log      # grep '\[LIV\]'
xvfb-run -a ./Build/clothbench -batchmode --bench=factor   -logFile factor.log    # grep '\[FAC\]'
xvfb-run -a ./Build/clothbench -batchmode --bench=throttle -logFile throttle.log  # grep '\[THR\]'
xvfb-run -a ./Build/clothbench -batchmode --bench=dir      -logFile dir.log       # grep '\[DIR\]'
xvfb-run -a ./Build/clothbench -batchmode --bench=gesture  -logFile gesture.log   # grep '\[GST\]'
```

Traps, in the order they cost time:

* `-batchmode -nographics` **segfaults** this player before the first scene loads. Run under
  `xvfb-run -a` with `-batchmode` only.
* `ClothBench` and `ScaleShapeBench` boot by DEFAULT. **Both now stand down for ANY `--bench=`
  flag.** `ScaleShapeBench` carried an explicit allow-list through ModBuild 290 — the worst shape an
  instrument guard can have, because a bench written later is silently pre-empted and the run exits
  0 with a full page of the wrong output. Three new benches this round would each have had to
  remember to edit that list.
* **Delete the logs before a re-run.** Every bench buffers its whole output and prints it in one
  block at the end, so a watcher looking for the `END` marker matches the PREVIOUS run's marker
  instantly. That happened this round.
* **Run `--bench=live` on its own.** It is the only bench that measures TIME; a second player beside
  it inflates every cell. The shape benches can share a machine.
* The renders are a **software wireframe rasteriser**, not a Camera. A built player only carries the
  shaders packed into it, and a bench that silently draws magenta is exactly the instrument that
  agrees with every broken build.
