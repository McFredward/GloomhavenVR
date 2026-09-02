# The cellar's window wood, and its entrance — 2026-09-02 hardware test

> **User, verbatim, defect 7.** "Mach die Bäume im Keller am Fenster etwas tiefer. Ich will das du
> dir vorstellst das ein Spieler bis zu dem Fenster direkt fliegen kann, das was er sieht soll dann
> sinn machen und keine kleinen schwebenden Bäume."
>
> **User, verbatim, defect 8.** "Am Keller gefällt mir der rechteckige Eingang nicht - das ist zu
> künstlich. Runde es ab, mach Unregelmäßigkeiten rein, das es nicht so aussieht wie ein perfekt
> rechteckiger Eingang."

Both are geometry, both are in `Assets/Editor/BuildEnvironmentRooms.cs`, both are verified without
hardware. Nothing in `src/` changed.

---

## 0. The station that did not exist, and why that is the whole story

Defect 7 names a **pose**: *"imagine a player can fly right up to the window."* Before this round
the harness had six window stations and **every one of them shoots from inside the room**, where
the opening is a 1.2 m slot seen from 2–5 m away and the wood inside it is a few hundred pixels.
That framing is the right one for "is the window worth turning your head for" and it is the exact
framing in which "kleine schwebende Bäume" **cannot be seen**: at 90 px of slot, a floating tree
and a planted one are the same four grey pixels.

So the first change was three new stations in `PreviewEnvironments.Views`, all derived from
`EnvRoomBuilder`'s own opening and its own outside-ground level:

| station | where | what it is for |
|---|---|---|
| `WinNose` | 30 cm off the inner face, dead centre, 72° | the closest pose a head really takes inside the room — the whole fan at once instead of the middle tenth |
| `WinFly` | **through** the opening, 1.2 m out, at a standing eye over the outside ground, 70° | scale. A 9 m tree 9 m away either reads as a tree or it does not |
| `WinFoot` | 50 cm over the outside ground, level, 66° | **ground contact.** From inside the room every sightline RISES and the trunk feet are behind the cill — which is how a wood could float for five ModBuilds unremarked |

…and three for the doorway (`DoorHead`, `DoorClose`, `DoorGraze`), which had **no station of its
own at all**: it appeared incidentally, 5 m away, in the corner of a frame about a dark corner.

This project's own lesson — *a preview station agrees with you* — is that a camera aimed at the
wrong thing renders happily. The corollary this round adds: **a defect that no station frames is a
defect no review can find.**

---

## 1. Defect 7 — the wood

### What it was

Four bands of trees 8.5–17.0 m out, butt radii 0.17–0.38 m, on a pitch-black ground plane.
Rendered from `WinFly` that is a **picket fence**: every trunk within a factor of two of every
other trunk's diameter, every one carrying the same moon rim at the same brightness, all of them
bottoming out on a razor-straight horizon with absolute black below it. There is no depth in a
uniform field, and a tree standing on a void is a floating tree.

### What it is now

| | before | now |
|---|---|---|
| band | 8.5 – 17.0 m, 4 rows | **6.5 – 20.0 m, 6 rows** |
| nearest row | 8.6 m, 8.5–12.5 m tall, butt r 0.24–0.38 | **6.9 m, 11.0–15.5 m tall, butt r 0.42–0.62** |
| farthest row | 16.2 m, 7.0–11.0 m tall, butt r 0.17–0.27 | **19.4 m, 7.0–10.0 m tall, butt r 0.14–0.21** |
| trees built | 42 | **60** |
| value ramp floor at the far bound | 0.42 | **0.26** |
| trunk foot | bottom ring on the ground plane | **bedded 0.28 m under it, with a ×1.55 root flare** |
| ground | one flat black quad, `_Tint = black` | **26 × 20 heightfield, forest_ground_04, ≈ 0.0010 linear** |
| **total** | 9 862 tris, 2 draw calls | **10 266 tris, 2 draw calls** |

The two ends of the band now differ by a factor of **three in girth** and **two in apparent
height**, which is what actually produces depth. Band 0 is deliberately **sparse** (4.6 m spacing
against band 5's 3.3): three or four trees, not a row — its job is to be the thing the eye measures
the rest against.

### Three things that are worth stating because they were not obvious

**a. The value ramp scales the RIM, not only the body.** `EnvRoom` multiplies the whole shaded
colour by the vertex colour *after* adding the rim (`EnvRoom.shader:627` then `:633`), so dropping
the ramp's floor from 0.42 to 0.26 dims a far trunk's bright edge along with its mass. "Every trunk
wears the same blue stripe" was the single loudest thing in the `WinFly` frame and it goes with the
floor. The floor had to move anyway: the ramp is over `InverseLerp(WoodNear, WoodFar, dist)` and
that interval grew from 8.5 m to 13.5 m, so holding the floor would have spread the same total fall
over half again the distance — the opposite of *tiefer*.

**b. Six bands cost less per tree than four did.** Tessellation now follows the band: the two near
rows keep 7 sides and gain a ring for the flare, the four far ones drop to 5 sides and 4 rings. A
band-5 trunk is four pixels wide; seven sides and five rings are five of them wasted. Net +4 % tris
for +43 % trees.

**c. The new ground is provably invisible from inside the room — and the harness proved it.** The
plane is at 2.343 m, the tallest head in the room is 2.02 m, every face has a `+Y` normal and the
shader culls back faces. The *evidence* rather than the argument: `AssertWindowWood`'s four station
readings are **bit-identical** before and after the ground went from `Color.black` to a lit,
undulating heightfield. That is what made it safe to build a floor for a pose the room does not
have.

The mound amplitude is **gated to zero for the first 5 m**, because `HauntFigures` stands a real
game monster in the window well at z = 5.35 (0.30 m off the outer face) and its whole staging is
"the black ground hides the legs". A mound under it would move a figure another lane owns.

### The levels, and how the first two passes went dark

`EnvRoom` is `col = alb · light` with `alb = _MainTex · _Tint`. The first two passes of the ground
were written by analogy with the **wood's** numbers and came out at 3.3 × 10⁻⁵ — a quarter of one
8-bit step even at the harness's 8× lift, i.e. indistinguishable from the black plane they replaced.
The mistake: **the wood's body is not what makes the wood visible — its rim is**, and a rim is added
*after* the albedo multiply, so it is three orders of magnitude over the body it sits on. Copying a
body level from something that is read by its edge is how both passes went dark.

The shipped level is derived instead: `forest_ground_04`'s albedo is ≈ 0.031 linear, so at
`_Tint 1.0` under a moon 40° up a flat patch is `0.031 × (0.0090 + 0.0220 × sin 40°) = 7.2 × 10⁻⁴`
— **30 % of the sky patch's measured 0.0024**. Dark earth under a night sky, legible against both
the void it replaced and the trunks standing on it.

### The gates, after

| station | pixels changed (bar: ≥ 8 %) | of the pixels the wood reaches, share moving > ¼ (bar: ≥ 50 %) |
|---|---|---|
| WinSeat | 16.9 % (was 14.3) | 84 % (was 87) |
| WinHead | 14.8 % (was 11.5) | 86 % (was 91) |
| WinNarrow | 19.9 % (was 17.5) | 85 % (was 88) |
| WinWalk | 15.4 % (was 14.5) | 59 % (was 59) |

The wood reaches **more** of the aperture from inside than it did, so the change is not only for the
fly-up pose.

### The frames

`.planning/cellar-2026-09/` (downscaled to 960 px for the repo; full-size in the render dir):

* `win_before_fly_x8.png` / `win_after_fly_x8.png` — **the pair the defect is judged on.**
* `win_before_foot_x8.png` / `win_after_foot_x8.png` — ground contact.
* `win_before_walk_x8.png` / `win_after_walk_x8.png` — from inside, the shipped station.

---

## 2. Defect 8 — the entrance

### What it was

`WallMesh`'s bare cut: whole 0.158 m cells dropped out of the west wall. Two dead-straight jambs,
one dead-straight lintel, four right angles — and, because `WallMesh` is a **single plane**, no
thickness at all. An opening with zero reveal depth is a shape painted on a wall, and stereo gives
that away at 4 m the way it gave away the rat hole's flat black rectangle in ModBuild 140.

### What it is now

`AddStairArch()` — the **rat hole's construction at human scale**, deliberately and not by
coincidence: the same user made the same complaint about the same defect two rooms ago ("Das 'Loch'
aus dem die Ratte kommt … ist ein Viereckiges schwarzes Rechteck"), and `AddRatHole` is the answer
that was accepted. Welded into the room's one stonework mesh — **no new material, no new draw call,
no new texture, no vertex motion.**

Five cues, because "runde es ab" and "mach Unregelmäßigkeiten rein" are two different requests:

1. **A segmental arch**, springing at 1.50 m and crowning at 2.28 m, with 7 cm of haunch left over
   it — so the arch reads against the wall above it rather than being the top of the hole. The
   exponent on `sin` flattens it: a true half-round over a 1.3 m span would crown 0.65 m up and read
   as a tunnel mouth.
2. **It is not symmetric.** Springings at 63 vs 66 cm half-width, the crown skewed 6 cm off the
   centre line, and a bounded fbm of the sweep angle moving the intrados — weighted by `sin θ` so it
   dies at both springings and the arch still meets its imposts cleanly.
3. **The jambs are courses, not lines.** 7 stones one side, 6 the other, hashed heights and hashed
   insets, different seeds — so **no course on the left lines up with a course on the right.** That
   is what actually kills the rectangle: a straight edge is legible from across a dark room, a
   stepped one is not. One stone per side is missing, with its rubble on the floor below.
4. **The opening has a thickness.** A 16 cm dark return runs from the ring back into the wall around
   the whole profile, wound to face its own axis.
5. **Stone on top of stone.** 19 hewn blocks: 11 voussoirs on a *radial* axis (a box laid flat is
   not a voussoir), a dropped and tilted keystone, two unequal imposts at the springings, a worn
   threshold set back into the bore, and five fallen blocks off to the sides.

### The gates

* **Winding.** Every ring and return face states the point it must be visible from, exactly as
  `AddRatHole` does. It fired on the first run — 123 of 217 faces backwards (both jambs and the
  whole return), which is precisely the "a backwards recess draws as a stone plug and still looks
  like geometry" failure the gate exists for.
* **The door must still be a door.** Free passage is measured off the profile that was really
  built, never off the constants: **1.29 × 2.28 m**, against a 1.05 × 2.00 m bar set by the alcove
  behind it (1.5 m steps, 2.6 m shaft, the figure `HauntFigures` walks across the doorway).
* **NaN.** `sin(π)` returns −8.7 × 10⁻⁸ and `Mathf.Pow(negative, 0.72)` is NaN — which does not
  throw, does not draw wrong, and does not even survive the next `Mathf.Max` (`Max(NaN, x)` returns
  `x`), so it showed up as a free-height reading that was quietly the *springing* instead of the
  crown. Clamped at the source, and the walk that reads it now rejects NaN instead of silently
  resetting on it.

### The frames

* `door_before_darkcornernw.png` / `door_after_darkcornernw.png` — **the frame the defect was
  reported from**, at true exposure. Before: a hard black rectangle. After: an arch.
* `door_after_head_x8.png` — a standing head at the board, lifted 8×: the arch, the stepped jambs.
* `door_after_graze_x8.png` — along the wall at a raking angle, which is the only frame in which the
  voussoirs' relief shows. Face-on, stone standing 5 cm proud of stone is invisible.

---

## 3. Reproducing

```sh
# bake (the geometry — build-bundles.sh only PACKS)
xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics \
  -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
  -executeMethod GloomhavenVR.EnvironmentsBuilder.BuildAll -logFile env-build.log -quit
grep -a 'stair doorway DRESSED' env-build.log
grep -a -A9 'WOOD OUTSIDE THE WINDOW' env-build.log

# the frames (WITHOUT -nographics; the filters keep it to ~6 min)
ENV_PREVIEW_OUT=$PWD/render/cellar ENV_PREVIEW_ENVS=Env_Cellar \
ENV_PREVIEW_VIEWS=Win,Door,DarkCornerNW ENV_PREVIEW_NOTIME=1 ENV_PREVIEW_NOFIRE=1 \
xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode \
  -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
  -executeMethod GloomhavenVR.EnvironmentsPreview.RenderAll -logFile env-preview.log
grep -a -A9 'WINDOW WOOD — the same pose' env-preview.log
```

---

## 4. What is NOT settled without a headset

1. **Whether the near band is too near.** A 15 m tree at 6.9 m is a scale anchor on a monitor; in
   stereo through a 1.2 m slot it may read as a wall. `WoodNear` is the dial.
2. **The ground's absolute level.** 30 % of the sky is a ratio measured on the preview station,
   which is known to be brighter than the headset. If the floor reads as *lit* rather than as
   *earth*, `C_NightGround`'s `_AmbUp`/`_DirCol` are the two numbers.
3. **Whether the arch is legible at true exposure.** The `DarkCornerNW` frame says yes on a monitor;
   that corner is the darkest in the room and the headset is dimmer.
4. **The threshold block.** It is set 7 cm back into the bore so it is stepped over rather than
   tripped on. Nobody walks through this door, but the figure that crosses the doorway does — and
   its feet are authored to the cut, not to this profile.
