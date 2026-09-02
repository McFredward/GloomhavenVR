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

`.planning/debug/renders/cellar-2026-09/` (gitignored — user ruling 2026-09-02, renders do not
go in the tracked planning tree):

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

---

## 5. The corrections — 2026-09-02, second hardware test

He tested §1 and §2 and kept both. Two corrections.

> **Defect 3, verbatim.** "Ich mag den neuen Eingang, mir gefallen nur diese zwei herausragenden
> Steine nicht auf der Seite links und rechts - entferne die."
>
> **Defect 4, verbatim.** "Macht den Waldboden noch etwas tiefer unter dem Fenster (nur ein Meter
> ca.)."

### 5.1 The two protruding stones — the imposts, and how that is known

`AddStairArch` had exactly **one pair of blocks that was one-on-the-left, one-on-the-right AND stood
proud of the wall**: the **IMPOSTS**. Two hewn bands at `y = ySpring − 0.045` (1.46 m, i.e. right at
eye level from the board), 0.30 m and 0.20 m wide, set at `z = −(Proud + 0.045)` so they
cantilevered **5.1 cm** into the room off a ring that is otherwise 6 mm proud. Nothing else in the
dressing has that shape:

| the other relief | why it is not what he means |
|---|---|
| 11 voussoirs | they run **over the arch**, not down the sides |
| the keystone | one block, at the **crown** |
| the threshold | one block, at the **foot**, and set *back* into the bore |
| 5 fallen blocks | on the **floor**, and they are five |
| the missing stone per side | a **void**, not a protrusion |

**And the frames say the same thing.** `DoorClose` at 8× lift, before and after, same camera:
`.planning/debug/renders/cellar-before/env_cellar_DoorClose_x8.png` against
`.planning/debug/renders/cellar-after/env_cellar_DoorClose_x8.png`. In the "before" frame there are
two horizontal slabs standing out of the jambs at the same height on each side, one at x≈345–490 px
and one at x≈800–870 px. In the "after" frame they are gone and **every other pixel of the doorway
is unchanged** — arch, unequal springings, hashed jamb courses, missing stone per side, voussoirs,
dropped keystone, threshold, return.

**The springing is not gone.** What carries the arch is the **top jamb course**, whose inset is
forced to the arch's own springing inset in `Courses()` — that is the impost as a piece of masonry,
it is flush with the jamb, and it is untouched. What went is the ornamental band that stood in front
of it.

**The door is still a door.** Measured off the profile that was really built, not off constants:

```
17 hewn blocks (11 voussoirs on a radial axis, a dropped keystone, a threshold, 5 fallen —
and NO projecting imposts, removed on the user's order after the 2026-09-02 test);
215 ring/return faces, 0 wound backwards. Free passage 1.29 x 2.28 m
```

19 blocks → **17**, 217 faces → **215**, winding gate still clean, and the free passage is
**1.29 × 2.28 m — the same number as before**, because the imposts never stood inside the opening.

### 5.2 The forest floor, one metre down

**It is one function now and it was two disagreeing places before.** The heightfield lived as a
local `Hgt` inside `AddNightOutsideWindow`; the trunks were bedded 0.28 m under the **flat base
level**. Since the mounds are ±0.42 m, a trunk standing where the ground dips could already be
**0.14 m clear of the earth** — a floating tree, the exact defect §1 exists to fix, hidden only by
the fact that nothing had ever framed the feet. The mesh and every trunk foot now read the same
`CellarWoodGroundY(x, z)`.

**The drop rides its own gate, and the gate is another lane's geometry.** `HauntFigures`' cellar
window card stands a real game monster at room `z = 5.35` — 0.30 m off the outer face — sunk so that
only its head and shoulders clear `y = 2.343`, and the whole staging of that event is *"the outside
ground hides everything below the outer cill"* (`HauntFigures.Events.cs`, THE FIGURE IS SUNK). A
flat 1 m drop everywhere would put a whole figure standing in a field, seen from the tabletop
vantage. So:

```
ground(x,z) = gy0
            - WoodGroundDrop · smoothstep(WoodDropIn, WoodDropOut, d)     // 1.00 m, 1.10 .. 4.20 m
            + moundGate(d) · (0.84·fbm + 0.018·d)                         // unchanged, 5 .. 11 m
```

with `d` the distance from the window in the ground plane. Dead flat at the shipped 2.343 m out to
1.10 m, full metre by 4.20 m. The mound gate is untouched and exists for the same reason.

**Measured, from the bake:**

```
THE FLOOR WENT DOWN 1.00 m. The drop rides its own gate — flat at the outer cill 2.343 m out to
1.10 m from the opening, full by 4.20 m — so the earth under HauntFigures' window figure
(z = 5.35, 0.30 m out) is untouched and its legs stay hidden. Under the trees the real floor runs
1.244..1.836 m, i.e. 0.51..1.10 m below the cill once the mounds are in it. EVERY trunk is BEDDED
IN IT: the tightest foot on the whole wood still sits 15.4 cm under the lowest corner of the
ground cell it stands in, measured against the built triangles and not against the height field.
```

Two things in that are worth stating plainly:

* **0.51 .. 1.10 m, not a flat 1.00.** The drift term (`+0.018·d`, which keeps the far ground from
  collapsing to a mathematical line) partly cancels the drop at the far edge of the patch, and the
  mounds do the rest. Under the near band it is the full metre; at 20 m it is about half of one.
  "Nur ein Meter ca." is what that is.
* **The bedding is a GATE, not a claim.** It is measured against the **mesh** — the lowest of the
  four grid corners of the cell each trunk stands in — because a linear triangle sags below the
  field it was sampled from, and the cell minimum is the only safe bound. It throws if any trunk
  stands on or over the earth instead of in it. Worst clearance on the whole wood: **15.4 cm**.

**The trees did not move.** Their origin, their heights, their crowns, their lean, their value ramp
and the `WindowWood` marker the sound lane measures its animals from are all exactly where they
were — the wood he approved is unchanged above the old ground line. What changed is that each trunk
now reaches **down** to the real floor with its root flare relocated to it (the flare's decay is
keyed off `foot` rather than off 0, or a 15 m tree would have carried a bulge floating in mid-trunk
a metre above the earth), and 49 of the 60 trunks got **one extra ring** so the flare is still a
curve and not a single facet. **+10 triangles per affected trunk; 10 266 → 10 800 tris, still two
draw calls, no new material, no new texture.**

**And it is provably invisible from inside the room**, which is what makes it safe: `AssertWindowWood`
before and after, same four stations —

| station | pixels changed, before → after | of the pixels the wood reaches, share moving > ¼ |
|---|---|---|
| WinSeat | 16.9 % → **16.9 %** | 84 % → 84 % |
| WinHead | 14.8 % → **14.3 %** | 86 % → 86 % |
| WinNarrow | 19.9 % → **19.8 %** | 85 % → 86 % |
| WinWalk | 15.4 % → **15.4 %** | 59 % → 59 % |

The ground is above every eye in the room and every sightline out of the window rises, so the drop
shows only in the pose he asked about.

### 5.3 The frames

`.planning/debug/renders/cellar-before/` and `.../cellar-after/` (gitignored — user ruling
2026-09-02, renders do not go in the tracked planning tree):

* `env_cellar_DoorClose_x8.png` — **the pair defect 3 is judged on.** Two slabs, then none.
* `env_cellar_WinFly_x8.png` — **the pair defect 4 is judged on**, at the fly-up pose. The floor is
  visibly a metre further down and every trunk still swells into it.
* `env_cellar_WinFoot_x8.png` — ground contact, 0.5 m over the earth: every foot flares into the
  ground, none stands on a line.

### 5.4 What is still not settled without a headset

1. **Whether one metre is the right metre.** `WoodGroundDrop` is the single dial and the log states
   what the ground really ends up at, mounds included.
2. **Whether the doorway now reads as too plain** without the impost bands. He asked for their
   removal and nothing else was touched, but the springings are now marked only by the jamb course's
   own step.
3. **The window figure.** The gate keeps the earth under it at exactly the level its staging was
   authored against, and that is an argument from the geometry — the apparition itself is spawned at
   runtime and this harness cannot photograph it.
