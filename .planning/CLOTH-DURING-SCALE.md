# The cape during the resize — ModBuild 290

> "Ich habe dir ein Video figure_scale_problem.mp4 angelegt in dem ich eine Figur einmal größer und
> wieder kleiner mache. Achte auf die Umhänge. **Beim größer machen werden sie steif und beim
> kleiner machen wird da ein Polygon matsch draus.** Sobald ich loslasse ist alles wieder ok, aber
> ich würde gerne das auch während dem skallieren alles intakt bleibt, **inklusive der Reaktion**.
> Ist das möglich?"

Two answers, both measured:

* **"intakt bleibt"** — yes, and it shipped. The mush on the way down is gone: normal incoherence
  over the whole shrink window falls from **0.3040 to 0.0325** (mean) and from **0.4565 to 0.0663**
  (worst frame), against a positive control that reads 0.027. The settled cape is bit-for-bit what
  ModBuild 289 shipped.
* **"inklusive der Reaktion"** — **no**, and here is the number that says no. A cape that reacts
  during the gesture needs its fabric to follow the scale; a fabric only follows the scale when it
  is re-cooked; a cook is **25–165 ms** on his rig, against an 11.11 ms budget. There is no
  throttle that makes 25 ms fit in 11 ms. Details in *Why the reaction cannot come back* below.

Everything here was run in a Unity **2021.3.5f1 Linux standalone player** (`.planning/cloth-cook-harness/`,
`DirectionArmsBench.cs`, `CookDriversBench.cs`, `GestureBench.cs`). These are desktop milliseconds,
not his rig — read the ratios.

---

## 1. What the video shows

`ffmpeg` frames of `figure_scale_problem.mp4`, brightened, cropped to the robe. The figure is an
Elite Savvas Icestorm; the "Umhang" is the teal coat with two long front panels.

| t | what the robe does |
|---|---|
| 9–13 s (growing, then held large) | the coat panels hang dead straight — no folds anywhere. "Steif". |
| 13.5–15.5 s (shrinking) | the whole skirt breaks into jagged outward spikes. "Polygon matsch". |
| after release | normal again. |

The asymmetry is the lead: **the same pin is on the cape in both directions and it only looks
broken in one of them.**

## 2. The mechanism — one cause, opposite signs

PhysX bakes a cloth's **fabric** (its edge rest lengths) in world units at enable time and never
re-derives it from a transform scale. While the size moves, the fabric is still baked at the size
the figure last *settled* at. And `maxDistance` — the pin — is a **soft** constraint the solver
satisfies *alongside* the fabric's stretching constraint, not instead of it.

* **Growing.** The rest lengths are too SHORT for the body. Pulling a taut sheet onto its skinned
  pose leaves it taut and flat. Nothing buckles; there is simply no fold left in it. → **"steif"**.
* **Shrinking.** The rest lengths are too LONG. The surplus length has to go somewhere, and what it
  does is buckle through the surface. → **"Polygon matsch"**.

**The round brief said "a fully pinned cloth cannot mush". It can, and it is the single most
important thing this round found.** On the way down the pin is not merely innocent-but-useless — it
is the *worst* arm measured, worse than not pinning at all.

## 3. The arm table — both directions, with controls

29×29 = 841 cloth vertices (his widest cape is 771), authored `maxDistance` 0 at the pinned edge
ramping to 0.35 m at the free edge, 300 settle frames, **every position divided by the root scale
before comparison**. `(A → B)` = fabric cooked at A, root driven to B.

*Incoherence* = mean `1 − dot` between neighbouring quad normals. A smooth drape ≈ 0; a surface
folded back through itself is not. *Close pairs* = non-neighbour vertex pairs within 0.35 × the
authored edge — polygons sitting inside each other.

### GROW (1.000 → 1.345)

| arm | mean edge | incoherence | close pairs | vs POSITIVE (worst / mean, m) | reaction (m) |
|---|---|---|---|---|---|
| **NULL** (positive run twice) | 0.9882 | 0.0714 | 0 | 0.00000 / 0.00000 | 0.94339 |
| **POSITIVE** (born at 1.345) | 0.9882 | 0.0714 | 0 | — | 0.94339 |
| re-cooked (the settle, shipped) | 0.9872 | 0.0610 | 0 | 0.01900 / 0.00314 | 0.94120 |
| stale fabric, simulating | 0.7992 | 0.0996 | 10 | 0.59697 / 0.25604 | 0.78513 |
| **PINNED — ModBuild 289** | 0.9894 | **0.0000** | 0 | 0.47906 / 0.23458 | **0.00133** |
| PINNED + bendingStiffness 0 | 0.9894 | 0.0000 | 0 | 0.47906 / 0.23458 | 0.00133 |
| PINNED + useTethers false | 0.9894 | 0.0000 | 0 | 0.47906 / 0.23458 | 0.00133 |
| **PINNED + stretchingStiffness 0** | 1.0019 | **0.0000** | 0 | 0.47716 / 0.23468 | 0.00146 |
| stretchStiff 0, coeff × 1.00 | 0.8864 | 0.0464 | 0 | 0.55029 / 0.24744 | 0.66840 |
| stretchStiff 0, coeff × 0.50 | 0.8362 | 0.0060 | 0 | 0.49932 / 0.21097 | 0.45565 |
| stretchStiff 0, coeff × 0.25 | 0.9026 | 0.0095 | 0 | 0.48438 / 0.21791 | 0.22436 |
| stretchStiff 0, coeff × 0.10 | 0.9585 | 0.0108 | 0 | 0.48059 / 0.22882 | 0.08832 |
| stretchStiff 0, uniform 0.25 × edge | 0.9835 | 0.0017 | 0 | 0.47752 / 0.23087 | 0.02396 |
| stretchStiff 0, uniform 0.50 × edge | 0.9633 | 0.0020 | 0 | 0.47837 / 0.22624 | 0.04813 |
| stretchStiff 0, uniform 1.00 × edge | 0.9258 | 0.0059 | 0 | 0.47983 / 0.21889 | 0.09551 |

### SHRINK (1.345 → 1.000) — the direction no previous round measured

| arm | mean edge | incoherence | close pairs | vs POSITIVE (worst / mean, m) | reaction (m) |
|---|---|---|---|---|---|
| **NULL** (positive run twice) | 1.0221 | 0.0267 | 0 | 0.00000 / 0.00000 | 0.58207 |
| **POSITIVE** (born at 1.000) | 1.0221 | 0.0267 | 0 | — | 0.58207 |
| re-cooked (the settle, shipped) | 1.0221 | 0.0267 | 0 | **0.00038 / 0.00009** | 0.57954 |
| stale fabric, simulating | 1.3329 | 0.3771 | 0 | 0.59188 / 0.29560 | 0.69257 |
| **PINNED — ModBuild 289** | 1.2859 | **0.4589** | 0 | 0.36211 / 0.16981 | 0.06359 |
| PINNED + bendingStiffness 0 | 1.2860 | 0.4583 | 0 | 0.36211 / 0.16981 | 0.06355 |
| PINNED + useTethers false | 1.2859 | 0.4588 | 0 | 0.36212 / 0.16981 | 0.06356 |
| **PINNED + stretchingStiffness 0** | 1.1119 | **0.0372** | 0 | 0.35282 / 0.16894 | 0.02404 |
| stretchStiff 0, coeff × 1.00 | **3.7446** | 1.0818 | 24 | 0.33324 / 0.11749 | 0.68855 |
| stretchStiff 0, coeff × 0.50 | 2.6820 | 0.9686 | 17 | 0.38516 / 0.13723 | 0.35687 |
| stretchStiff 0, coeff × 0.25 | 1.6728 | 0.7669 | 4 | 0.39261 / 0.17523 | 0.18913 |
| stretchStiff 0, coeff × 0.10 | 1.3645 | 0.5834 | 0 | 0.37545 / 0.17130 | 0.08381 |
| stretchStiff 0, uniform 0.25 × edge | 1.3711 | 0.6502 | 0 | 0.35578 / 0.17083 | 0.06383 |
| stretchStiff 0, uniform 0.50 × edge | 1.3522 | 0.6152 | 0 | 0.35897 / 0.17131 | 0.06925 |
| stretchStiff 0, uniform 1.00 × edge | 1.4060 | 0.6899 | 0 | 0.36329 / 0.16496 | 0.08958 |

Picture: `.planning/cloth-cook-harness/shots/arms-both-directions.png` (software wireframe, no
shader dependency). `shrink-Pinned` is a jagged band; `shrink-PinnedNoStretch` is a line;
`shrink-Cooked` is indistinguishable from `shrink-Born`.

### What the table says

1. **`stretchingStiffness = 0` while pinned is the fix.** Shrink incoherence 0.4589 → **0.0372**, a
   12.3× reduction, within 1.4× of the positive control's own 0.0267. On the way up it changes
   nothing (0.0000 either way).
2. **It is the stretching constraint specifically.** `bendingStiffness = 0` and `useTethers = false`
   move the number by less than 0.001. Naming the constraint mattered; "the fabric" would not have.
3. **The re-cook is exact in both directions.** 0.00038 m worst against the positive control on the
   shrink. The settle behaviour is right and was not touched.
4. **The arm the brief most wanted — `stretchingStiffness = 0` with a rescaled small
   `maxDistance` — FAILS, and it fails on the direction the brief could not have known about.** On
   the way up it is merely wrong-but-tidy (mean edge 0.84–0.96, incoherence 0.006–0.011, but the
   drape only reaches half the correct depth). On the way DOWN it is catastrophic: mean edge 3.74,
   worst 12.5, 24 self-intersecting pairs. It tears open exactly as ModBuild 286 found — the small
   `maxDistance` does not save it, because with the fabric gone there is nothing to keep the surplus
   area of a too-large cape from folding onto itself. **Reported as dead.**
5. **A uniform absolute wander bound** (`maxDistance = k × authored edge`, scale-corrected, with the
   fabric out of the way) is clean on the way up — incoherence 0.0017 at k = 0.25 with a real
   0.024 m of reaction — and is 0.65 on the way down. Also dead, for the same reason.

## 4. Why the reaction cannot come back — the cost of a cook

His ModBuild 289 log:

```
FigureGrab.ClothCooks   0/s (total 19, worst frame 1)      <- the stagger works, one cook per frame
FigureGrab.Cloth.Cook   32.249ms avg, worst 167.51ms, frames 19
FigureGrab.Cloth.Cook   53.478ms avg, worst 165.44ms, frames 12
FigureGrab.Cloth.Cook   44.297ms avg, worst 140.09ms, frames  6
FigureGrab.Cloth.Cook   33.577ms avg, worst  97.41ms, frames  9
```

Subtracting each window's single worst frame leaves a **typical cook of 25–43 ms**. That is 2.3–3.9×
the 11.11 ms budget *per cook*, before the outliers. A mid-gesture re-cook — throttled, quantised,
however scheduled — cannot fit, because the unit of work does not divide. **The answer to "inklusive
der Reaktion" is no.**

### The 40× — hunted, and not found in the Cloth

The round brief's arithmetic predicted single-digit milliseconds for a 771-vertex cloth. Every knob
was measured at a fixed 29×29 = 841, 8 reps, median, with a NULL control (0.000 ms) and a known
positive (`AddComponent<Cloth>`, 10.2 ms):

| cell | cook | vs baseline |
|---|---|---|
| baseline | 10.566 ms | 1.00× |
| selfCollisionDistance 0.005 / 0.02 / 0.05 | 12.7 / 10.4 / 15.1 ms | 1.20 / 0.99 / **1.43×** |
| useVirtualParticles on / off | 13.1 / 13.8 ms | 1.24 / 1.31× |
| useTethers = false | 15.6 ms | 1.48× |
| enableContinuousCollision | 14.0 ms | 1.33× |
| clothSolverFrequency 300 / 30 | 13.3 / 14.2 ms | 1.26 / 1.35× |
| stretchingStiffness = 0 | 15.3 ms | 1.45× |
| bendingStiffness = 1 | 10.9 ms | 1.03× |
| 0 / 2 / 4 / 8 / 16 sphere collider pairs | 15.2 / 15.3 / 14.9 / 15.0 / 14.7 ms | ≈1.4× flat |
| 2 / 8 capsule colliders | 8.3 / 15.0 ms | 0.79 / 1.42× |

**Nothing multiplies a cook by forty. Nothing multiplies it by two.** The whole spread is inside the
instrument's own reproducibility: two *identical* cells run in different blocks of the same session
came out 10.566 ms and 7.027 ms, a **1.5× floor**, so no row above is distinguishable from baseline.

Three things were settled, though:

* **The vertex law is 10–20 µs per cloth vertex on this machine**, not 5.3:
  9.0 / 14.5 / 24.8 / 49.7 / 92.9 ms at 441 / 841 / 1681 / 3721 / 6561. *The brief quoted 5.3 µs,
  which comes from the header's `SetEnabledFading` table; the header's own `enabled = true` table
  gives 9–12 µs. The header contains two per-vertex laws that disagree by 2.5×, and the smaller one
  was quoted.* At the right law, 771 vertices is 7–12 ms, not 4 ms — still not 32–53 ms.
* **The census reports the right number.** Brief hypothesis (a) is **falsified**: 841 welded cloth
  particles cost the same whether the mesh carries 841, 1682 or 3364 vertices (15.1 / 15.4 /
  14.9 ms). `Cloth.coefficients.Length` — which is what the census prints — *is* the driver.
* **The coefficient content does matter, as already known**: an enable with everything at
  `maxDistance = 0` costs 0.5 ms and does not cook; at the pin floor 5.4–11.5 ms; at authored slack
  7.0–13.1 ms.

**So the 40× is unresolved, and the honest reading is that the 167 ms cape is not the cape the
census printed.** 167 ms ÷ 15 µs ≈ 11 000 particles. `LogOnce` fires **once per session, on the
first figure resized**, and his session resized several. This build therefore ships the instrument
that closes it in one line next round: **`FigureGrab.ClothCookVerts`**, counted at every cook with
that cloth's vertex count — its `worst frame` is the largest cape ever cooked, `total ÷ ClothCooks`
the mean.

Two other candidates cannot be excluded from here and are named rather than dismissed: his machine
is not this one (that window ran 82.8 % over-budget frames), and this log demonstrably attributes
foreign stalls to whatever scope is open — `Hands.VRHand` reads **worst 131.17 ms** and
**106.15 ms** in windows where its own average is 0.09–0.16 ms, which cannot be VRHand's work.
But a stall explains an outlier, not a mean of 32–53 ms over 46 events.

## 5. A second defect, found by reading and confirmed by measuring

Through ModBuild 289, `Advance` handed every frame to `StepCook` for as long as `Cooking` was set,
while `Note` could set `Suspended` underneath it. So **the pin never re-engaged during a stagger**:
a cloth the sequence had already cooked sat at full settled coefficients, unpinned and simulating,
while the player went on changing the size — the STALE row of the table (incoherence 0.377). And
because `Release` re-entered with `CookIndex` back at 0, the sequence restarted from the first
cloth, so the window lasted as long as the gesture did. At 25–165 ms per cook frame, six of them is
a third of a second of wall clock with no pin on the figure in his hand.

`AbortCook` stands the sequence down instead. Its one unavoidable cost: a cloth caught between the
two frames of its own cook is `enabled = false`, and a disabled Cloth never simulates again unless
somebody enables it — so the abort pays that one enable (which is a cook, at a size that may already
be stale). At most one per abort, against up to `CookCount − CookIndex` it prevents.

**That fix immediately exposed a third bug, and the harness caught it as a regression in the one
state the user says is fine.** `CookedFactor` was a single number per figure. That is only sound
while the stagger is all-or-nothing; once it can stop halfway, "the size this figure's fabric is
baked at" is not one value. Measured: a gesture whose shrink began mid-stagger left cloth 0 cooked
at 1.345 while the figure finished at 1, the figure-wide number still read 1, the release therefore
decided no cook was owed, and the **settled** cape came out at incoherence **0.2782** against
ModBuild 289's 0.0468. `CookedFactor` is now **per cloth**, and `StepCook` skips a cloth whose
fabric already matches — so the abort costs fewer cooks, not more.

## 6. End to end — the two state machines over a whole gesture

`GestureBench.cs`. Three cloths of 841 vertices (his figure carries three, which is what makes the
stagger's unpinned window reachable). Script: grow 1 → 1.345, pause, shrink 1.345 → 1, release.
Incoherence sampled **every frame** of each moving window, on cloth 0 — the first the stagger cooks
and therefore the one left unpinned longest.

| script | arm | GROW mean | SHRINK worst | SHRINK mean | SETTLED after release | cooks | aborts | uploads |
|---|---|---|---|---|---|---|---|---|
| A — the stagger finishes before the shrink | 289 | 0.0006 | 0.4651 | 0.3147 | 0.0468 | 6 | 0 | 72 |
| A | **290** | 0.0008 | **0.2228** | **0.0456** | **0.0468** | 6 | 0 | 72 |
| B — the shrink starts MID-stagger | 289 | 0.0008 | 0.4565 | 0.3040 | 0.0468 | 6 | 0 | 72 |
| B | **290** | 0.0011 | **0.0663** | **0.0325** | **0.0468** | 6 | 1 | 42 |

Script B, 290, event trace (frames are gesture-relative):

```
f40 settle@1.335 COOK   f41 cook c0@1.335   f43 cook c1@1.335
f45 cook c2@1.335 (abort must re-enable it)   f45 ABORT at c2
f102 settle@1.000 COOK  f103 cook c0@1.000  f105 cook c1@1.000  f107 cook c2@1.000
```

* Shrink mean incoherence **0.3040 → 0.0325** (9.4×), worst frame **0.4565 → 0.0663** (6.9×).
* Grow is unchanged to within the run-to-run spread (0.0006–0.0011 either way).
* **Settled after release is 0.0468 in all four arms** — identical to ModBuild 289. The state he
  says is already fine is untouched.
* Same cook count; **fewer** coefficient uploads (42 vs 72).

Picture: `.planning/cloth-cook-harness/shots/gesture-289-vs-290.png` — the worst shrink frame of
each arm side by side. 289 is a fuzzy jagged band; 290 is a line.

**Timing script B cost two runs and is worth recording.** A "no pause at all" script never settles
and never cooks (`Note`'s relative epsilon is measured against the last *accepted* factor, so while
the gesture moves it is re-crossed every two or three frames and `QuietFrames` never reaches 12) —
it reads a flawless 0.0000 that says nothing. And a 45-frame grow settles *during the grow*, because
an exponential approach flattens below the epsilon around frame 37. Only a short grow plus a
14-frame pause puts the shrink inside the stagger.

## 7. What shipped

All of it in `src/GloomhavenVR/Board/FigureGrab/FigureCloth.cs`.

1. **`ApplyStretch`** — `stretchingStiffness = authored × pinWeight`, change-latched. At weight 1 the
   authored float is written back verbatim, so the settled cape is bit-for-bit ModBuild 289.
   **Measured at 0.004 ms on a live enabled cloth and it does not cook** — the component still reads
   `enabled == true`, and the two anchors in the same run were 0.004 ms for a full coefficients
   upload and 10.8 ms for an enable transition.
2. **`AbortCook`** — a size change arriving mid-stagger stands the sequence down and returns to
   pinning on the same frame.
3. **`CookedFactor` per cloth**, plus a skip in `StepCook` for a cloth whose fabric already matches.
4. **`FigureGrab.ClothCookVerts`** and **`FigureGrab.ClothCookAborts`** counters, and the census line
   now tells the next reader how to use them.

`StepCook` restores **one** cloth's authored stiffness on its own down-frame rather than all of
them, because a cook frame is 25–165 ms and restoring all of them would put the not-yet-cooked
cloths back into the fight for the several hundred milliseconds the rest of the stagger takes.

**No config dial was added, and that is a deviation from the round brief.** A `[FigureGrab]` dial
needs a `Defaults.Board.cs` constant plus `Loc.ConfigNames.cs` and
`Loc.ConfigDescriptions.German.cs` entries — three files this lane does not own. The change is also
a defect fix rather than a preference: at weight 1 it is a no-op, so there is no settled state for a
dial to choose between. The exact three-line patch is in the hand-off report if the integrator wants
it anyway.

## WHAT I COULD NOT MEASURE WITHOUT HARDWARE

* **The real capes.** Every number here is a 29×29 grid sheet with one bone, uniform authored slack
  and no colliders. A Savvas Icestorm coat is two long panels, skinned to many bones, with
  artist-painted per-vertex slack and the game's own collider set. The *mechanism* (a soft
  `maxDistance` arguing with a stale fabric) does not depend on any of that, and the *direction* of
  every effect is a property of the sign of the mismatch — but the magnitudes are a sheet's.
* **The 40× itself.** Unresolved. `ClothCookVerts` closes it next round; until then the claim
  "his 167 ms cape is ~11 000 particles" is an inference from a re-measured vertex law, not a
  measurement of his figure.
* **Whether 0.0325 reads as "intakt" to him.** It is 9.4× better than what he filmed and within
  1.4× of a cape born at the target size, but the acceptance test is his eyes on a figure at arm's
  length. If the shrink still shows a shimmer, the next thing to try is holding the pin's stiffness
  at 0 for a few frames *after* the ramp completes rather than restoring it on the same frame.
* **How the stiffness ramp reads in motion.** It ramps with the pin over the same 0.12 s, so nothing
  pops on either edge, but a constraint ramp is not a position-space blend and only he can say.
* **Whether the abort makes the "Hänger" better or worse in practice.** It removes cooks in one
  place (the skip) and adds at most one in another (the forced re-enable). In script B the totals
  were identical. On a long gesture with many micro-pauses it should be a net reduction, but that is
  an argument, not a measurement — `ClothCookAborts` next to `ClothCooks` will say.
* **The remaining cook stall.** 19 cooks in one 30 s window at 25–165 ms each is ~600 ms of stall
  per 30 s of resizing. This build does not fix that and does not claim to. It is a separate
  shipping problem and the lever is `SettleFrames` (12 → more) or the cook itself.

## Re-running the harness

```
mkdir -p /tmp/clothbench289/Assets/Scripts /tmp/clothbench289/Assets/Editor
cp .planning/cloth-cook-harness/*.cs      /tmp/clothbench289/Assets/Scripts/
mv /tmp/clothbench289/Assets/Scripts/Builder.cs /tmp/clothbench289/Assets/Editor/
/home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics -quit \
    -projectPath /tmp/clothbench289 -executeMethod Builder.BuildLinux -logFile /tmp/clothbench289/build.log
cd /tmp/clothbench289
xvfb-run -a ./Build/clothbench -batchmode --bench=drivers -logFile drivers.log   # grep '\[DRV\]'
xvfb-run -a ./Build/clothbench -batchmode --bench=dir     -logFile dir.log       # grep '\[DIR\]', writes shots/
xvfb-run -a ./Build/clothbench -batchmode --bench=gesture -logFile gesture.log   # grep '\[GST\]', writes shots/
```

Three things that cost time here and are worth not rediscovering:

* `-batchmode -nographics` **segfaults** this player before the first scene loads. Run under
  `xvfb-run -a` with `-batchmode` only. (Already in the ModBuild 284 notes; still true.)
* **`ClothBench` and `ScaleShapeBench` boot by default and call `Application.Quit`.** A
  `--bench=drivers` run therefore exited 0 after 502 frames with not one line of the bench that was
  asked for. Both now stand down for any `--bench=` flag; a new bench must add its flag to
  `ScaleShapeBench`'s list, and `ClothBench` refuses them all.
* The renders are a **software wireframe rasteriser**, not a Camera. A built player only carries the
  shaders that were packed into it, and a bench that silently draws magenta — or nothing — is
  exactly the instrument that agrees with every broken build.
