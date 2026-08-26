# The cellar window's view, and the wood that is heard through it

ModBuild 296. Branch `worktree-agent-abe61413533b43564`.

> **USER, verbatim:** "Ich möchte, dass du in der Kellerumgebung den Blick aus dem Fenster
> modellierst. Dort soll auch Wald sein und sehr dunkel, dass man nur wenig erkennt. Baue nur
> was vom Fenster her sichtbar ist — es muss performant bleiben. Die Tiergeräusche kannst du von
> diesem Wald aus auch triggern, in der selben Intensität wie im Wald, d.h. im Keller hört man
> sie weniger und immer von dem Fenster aus lokalisiert."

Three things: a wood beyond the barred window, built only where the slot points, and the wood's
animals heard from the window at the wood's own intensity.

**Multiplayer:** all of it is LOCAL PRESENTATION. The wood is baked geometry in a room prefab —
every client's cellar contains the identical mesh, exactly as every client's wall does. The animal
calls ride the schedule that already existed, which is a pure function of the slot index and the
bake and carries ZERO wire bytes; two clients place the same call at the same point on the same
frame. Nothing was added to the protocol and `NetProtocol.ModBuild` is untouched.

---

## 1. THE COST TABLE

Measured on the shipped prefab by `EnvironmentsPreview.LogRoomCost`, which walks every
`MeshFilter`/`MeshRenderer` in the instantiated `Env_Cellar.prefab`. Draw calls are counted as one
per renderer with no batching assumed — the pessimistic reading.

| | triangles | vertices | draw calls | textures |
|---|---|---|---|---|
| **Env_Cellar, whole room** | 149,810 | 147,016 | 89 | — |
| `WoodBark` (trunks) | 2,940 | 2,016 | 1 | pine_bark_alb + pine_bark_nrm |
| `WoodFoliage` (crowns + mass) | 6,922 | 13,844 | 1 | fir_twig_alb |
| **the wood, total** | **9,862 (6.6 %)** | **15,860 (10.8 %)** | **2 (2.2 %)** | **0 new bytes** |
| `NightSky` patch, widened this round | 440 (was 288) | — | 0 new | 0 new |

For scale, the four heaviest single nodes already in this room are `StarField` 16,808 tris and the
three barrels at 10,820 each. **The whole wood is cheaper than one barrel and a bit.**

**Texture memory: zero.** `pine_bark_alb`, `pine_bark_nrm` and `fir_twig_alb` are `Env_Swamp`'s
imported CC0 atlases and are already in the bundle. Two new `.mat` files were created and no image
was.

**Fill: the aperture's screen area and nothing else.** The north wall is 4.5 m from the room's
centre and the wood is 8.5–17 m beyond it; Unity sorts opaque geometry front-to-back, so the wall
writes depth first and every wood fragment outside the opening is z-rejected before it is shaded.
This is the same argument the night sky patch already makes for itself.

**Per-frame CPU: nothing.** No `MonoBehaviour`, no `ParticleSystem`, no vertex motion, no script of
any kind is added to the prefab. The sound side adds one integer division and one `long` compare per
frame, on a path this room already ran for the drip and the rat (see §4).

**Bundle size: not measured, deliberately.** The integrator owns the bundle build. The honest
estimate is the vertex buffer: 15,860 verts × 52 bytes (pos + normal + tangent + uv + colour) ≈
825 KB, plus ~59 KB of 16-bit indices, before LZ4. **The biggest lever if that is too much is the
2,501 loose foliage cards** (`AddWoodOutsideWindow`'s mass loop, `2600` candidates) — they are 70 %
of the triangles and 87 % of the vertices, and halving them is a one-constant change.

**The room did not get bigger, and that was checked rather than assumed.**
`SkyAlternative.MeasureAuthoredRoomExtent` turns the prefab's total mesh AABB into the CAMERA'S FAR
PLANE, so geometry that reaches further than everything already out there widens the depth range of
every stereo frame in the scene. The bake now throws if any wood vertex leaves the night sky patch's
26 m sphere, which is already this room's widest object. Measured: the farthest wood vertex is
**22.22 m** from the opening. The far plane is unchanged.

---

## 2. WHAT IS BUILT

42 conifers on a polar grid around the window's outer face, in four bands at 8.6 / 11.0 / 13.6 /
16.2 m, 7–12.5 m tall, plus 2,501 loose foliage cards filling the sky between and behind them. The
azimuth fan is **derived**, not typed: it is the sightline bundle's own hull (az −70…+70°) plus 3°
of seam.

Trunks are tapered, lobed and leaning, in the same idiom as the forest room's — but from a
dedicated builder (`AddWoodTrunk`), because the forest's `AddTrunk` buries its foot 30 cm and always
starts at the ground, and this wood is allowed not to.

### THE LIGHTING, WHICH TOOK THREE PASSES AND THE FIRST TWO ARE ON THE RECORD

The wood is **BACK-LIT by construction**: the player looks north through this window and `MoonDir`
bears 40° east of north at 40° up, so the faces turned toward the room are the faces turned away
from the moon and `saturate(dot(N, _DirDir))` is ~0 on almost all of them. There is no tuning that
makes this wood's faces bright.

So the design is a silhouette with a rim, and the number that sets both is one the harness prints
every run: **with the aperture haze off, the shipped cellar's opening reads a mean linear luminance
of 0.0024 over the pixels this wood lands on.** That is the sky patch's airglow-and-haze floor with
stars in it — very dim, but not zero.

* the **mass** of the wood belongs under it: crown cards land at 0.0004–0.0009, trunk faces at
  0.0011–0.0025, i.e. 3–5× darker than the sky they stand in front of;
* the **rim** belongs well over it, and it is not scaled down with the body. `_RimCol`
  (0.200, 0.255, 0.375) at `_RimPow` 4.2 is `S_TrunkA`'s (0.21, 0.27, 0.40) at 4.2 — the value the
  forest room's own trunks carry, whose comment calls it "the single most important lighting cue in
  the whole room".

**Two earlier passes were wrong and both looked plausible.** Pass 1 (ambient 0.030, directional
0.070) put the wood AT the sky's own value: it was drawn, it was in the line of sight, and it changed
the rendered aperture by 0.2 %. Pass 2 (ambient 0.026, directional 0.105) was built on the theory
that the sky was pure black and the wood therefore had to be lit rather than cut out; the harness
measured +11 % and threw. Neither was findable by looking at a dark PNG.

---

## 3. WHAT IS DELIBERATELY NOT BUILT

1. **No ground, and nothing standing on one.** The outside ground is the OUTER CILL, 2.343 m up the
   room's wall; the tallest head in this room is `CellarEyeStanding` = 2.02 m. The eye is BELOW the
   ground outside, so every sightline out of this window RISES — the minimum elevation over the
   whole floor is +1.7° and it is never negative at any head height. **The earth out there cannot be
   seen from inside this room at all.** No undergrowth, no path, no litter, no props, no animals.
   (This also means `AddNightOutsideWindow`'s black ground plane, wound +Y with Cull Back, is
   invisible from every player pose — a pre-existing object that does no work. It is left alone.)
2. **Nothing past 23 m of the opening.** `EnvStars` is `ZWrite Off` but is drawn at queue 2450 —
   AFTER the opaque wood — and it ZTESTS, so a trunk deeper than the patch's 26 m sphere is painted
   over by it. The first version of this wood ran to 22 m on the ground and 14 m up, i.e. 27.6 m from
   the window at its crown, and the gate caught it. Pulling the band in bought the picture back
   twice over: the trees subtend more through a 1.2 m slot and they parallax harder as the head
   moves.
3. **No sway.** `EnvRoomCutout`'s `_Sway` defaults to 0 and is left there, and the bake asserts it.
   *Culling cannot see vertex shaders* — displaced geometry is culled against its UNDISPLACED bounds
   — and the cheapest way to be right about that is to have no displacement. It also costs nothing
   visually: a 15 m silhouette through a slot at this light level has no readable motion.
4. **No trunk truncation, and the rule is there anyway.** `WoodLowestSeenY` finds the lowest point in
   each trunk's column that any sightline reaches, and the trunk starts there. At this range it saves
   **0 rings**, and that is the correct answer: the lowest sightline clears the cill only 0.3–0.6 m
   above the ground at 8.5–17 m, so the feet ARE in view. At 40 m they would not be (1.7 m), which is
   what the rule is for.

### "ONLY WHAT IS VISIBLE" — AS A MEASUREMENT, NOT AN INTENTION

The per-candidate cull is a safety net and nothing more: candidates are generated inside the visible
fan, so it drops 0 of 42 and **that number proves nothing.** The first version of this wood reported
exactly that and read as a success.

The real statement is the **per-vertex audit**: every welded vertex is put back through the exact
sightline test at r = 0 — can anyone, from anywhere in this room, see THAT POINT through the
embrasure? — against all 1,134 head positions.

> **14,196 of 15,860 vertices (90 %) are in some player's line of sight through the opening.**

It is not 100 % and must not be: the far side of every trunk is hidden by its own near side and the
eye cannot see the back of a bough. What it rules out is a wood built to the sides or behind, where
the slot can never point.

---

## 4. THE SOUND

### The user's sentence contains its own mechanism, and one clause decides the architecture

* **"in der selben Intensität wie im Wald"** — the source is unchanged. Same clips, same 41 s slot,
  same 16-card deck at the same salt, same 22 % skip, same authored `Gain` per animal. Nothing in
  `NightCalls` was touched and **there is no "cellar volume" anywhere in the file.**
* **"immer von dem Fenster aus lokalisiert"** — this rules out the obvious implementation, and it is
  where the brief I was given was wrong (see §7). "Place the emitters out in that forest and let the
  distance model do the work" fails this clause by a wide margin: an animal on the perch ring at 10
  authored m and 60° off the window's normal is **55° away from the window as seen from the seat**.
  You would hear a fox through the east wall.

  **THE WINDOW IS THE ONLY DIRECTION THE SOUND CAN COME FROM, and that is the physics, not a cheat.**
  An opening in a heavy wall is a SECONDARY SOURCE: everything outside reaches the room through that
  hole and re-radiates from it, which is exactly why a fox two hundred metres off heard through a
  slot is localised at the slot. The user's sentence is a correct description of the acoustics.
* **"im Keller hört man sie weniger"** — the reduction is the wall and the distance, computed.

### What was implemented

The perch is still drawn — the same three hash channels off the same slot index, on the same
per-animal rings, in a frame the bake places at the wall's outer face at outside ground level with
+Z outward (`WindowWood`, a marker with no geometry so a later visual round cannot silently move
every fox in the room). The azimuth is restricted to the OUTWARD half-plane, because a bearing behind
the player would put an animal inside the cellar.

That perch is used for exactly one thing: **the length of the outside leg.** The shot is played from
`WindowGlow`, the opening's own mouth.

The gain is two legs of a real path and no knob:

```
  gain = voice.Gain                                  the wood's own, unchanged
       × min(1, MinMeters / d_perch→window)          the voice's OWN rolloff curve, over the open
                                                     air outside — not a new model, the same
                                                     expression Unity applies in the wood
       × WindowInsertion                             the wall
```

`WindowInsertion` is ISO 12354-3's facade term, reduced to the one part that matters. 0.55 m of
rubble stone has R = 55–60 dB, so the wall transmits nothing and every decibel comes through the
hole, whose R is zero. What is left is `10 log10(S/A)`:

```
  S  the OUTER opening        1.2283 × 0.6427 m                     =  0.789 m²
  A  the cellar's absorption (Sabine, mid frequencies):
       flagstone floor        10.5 × 9.0      α 0.03                =  2.835
       plank ceiling          10.5 × 9.0      α 0.10                =  9.450
       rubble stone walls     2(10.5+9.0)×3.3 α 0.03                =  3.861
                                                              A     = 16.15 m² sabins
  10 log10(0.789/16.15) = −13.1 dB  →  amplitude ×0.221
```

The third leg — opening to ear — is Unity's own, off the source's unchanged
`minDistance`/`maxDistance`. It is FLAT across the whole room by construction (the smallest
`MinMeters` in the table is the owl's 8 perceived m and the cellar is ~9 perceived m corner to
corner), and that is not a rounding convenience: the `S/A` term is a DIFFUSE level, the same
everywhere in the receiving room, so a call that does not attenuate across the cellar is what the
model actually says.

**Result:** an owl's authored 0.050 lands near **0.009–0.011** in the cellar against **0.041** in the
wood — about 12 dB down, and comparable with this room's own drip.

### THE REVERB / OCCLUSION QUESTION — a decision, not an omission

A fox heard through a slot in stone is also FILTERED, and the room rings underneath it. **Neither is
modelled.**

* **No low-pass on the call.** Every clip in this bank is BAND-LIMITED WHEN IT IS SYNTHESISED —
  each voice's formants and its two-to-four-pole envelope are baked into the buffer — so a runtime
  filter would be a second, coarser copy of a shaping decision already made once, with taste, per
  animal. This file makes that argument twice already (the candle flutter and the fire) and both
  times the runtime filter was REMOVED rather than added. It would also cost a component on the
  shared one-shot pool, which is the one place in this file where per-event allocation was
  deliberately designed out.
* **No reverb.** This project ships none anywhere and sets `bypassReverbZones` on every source it
  owns (the game's zones are sized for the game's world, not a 20× diorama). A reverb added for this
  one cue would be the only one in the mod, and the loudest thing in a room whose resting ambience is
  a draught delivering 0.00076.

What IS modelled is the level and the direction, which are the two things the user's sentence names.
**If a future round wants the muffling, the honest place for it is a second BAKED variant of each
clip, not a filter on the live one.**

### It does not double up, and it cannot fire without the window

The drip, the rat and the apparitions index three different clocks (a drip period, a rat slot, an
83 s haunt slot) against this schedule's 41 s, and two indices that are never compared cannot
correlate. **One doc comment in `EnvSound.cs` said "the DRIP and the RAT are cellar-only and can
never run in the same session's room as these" — that is now false and has been corrected rather
than left standing.** The conclusion survives on the other half of the argument, which was always the
load-bearing one.

`TickNightCall` self-gates on `_perchFrame`. If the bake gives the room no `WindowWood` marker or no
`WindowGlow` node, both are cleared, the calls never fire, and the room sounds exactly as it did
before — with one loud warning saying which node is missing and **why a fallback must not be added**
(every other node at the window is a welded mesh sitting at the room's origin; a fallback would put
the animals in the middle of the floor).

**Per-frame cost:** one integer slot division and one `long` compare, on the path this room already
ran. No allocation, no scene query, no new voice — the calls take the shot pool this room already
builds, so the emitter count is unchanged.

---

## 5. VERIFICATION — AND HOW "CORRECTLY DARK" WAS TOLD FROM "NOTHING WAS BUILT"

This is the part the brief was most right about. **"Sehr dunkel, dass man nur wenig erkennt" makes a
preview harder to judge, and the failure mode runs BOTH ways** — I mistook empty for dark once, and
mistook correct for empty and tuned it brighter until it was wrong once.

**The frame is not the instrument. The difference is.** `PreviewEnvironments.AssertWindowWood`
renders every window station **twice from the same pose in the same run**: once with `WoodBark` and
`WoodFoliage` active and once with them switched off, which is the SHIPPED cellar pixel for pixel. It
also renders a third and fourth time with the aperture glow switched off, to separate the view from
the haze in front of it. Every read is over the opening's OWN projected rectangle, in LINEAR
luminance, and the run FAILS if:

* fewer than 8 % of the opening's pixels differ between wood-on and wood-off — that is "nothing was
  built, or it is culled, or it is behind the sky patch, or the slot does not point at it". **A dark
  picture cannot pass by being dark.**
* fewer than 50 % of the pixels the wood reaches move by more than a quarter of what they were
  showing — that is "present, drawn, in the line of sight, and invisible", which two passes of this
  material really were.

The second bar is **per pixel and not a mean**, and that is a correction: this wood is a silhouette
with a bright rim, so its two halves pull a mean in opposite directions and cancel, and at the
`WinWalk` station the moonbeam's own mouth is inside the opening and outweighs everything the trees
do.

**And the pair of PNGs is the other half.** 0.0024 linear is 17/255 on the headset's 8-bit sRGB; the
whole picture lives in a handful of levels and no scalar over an aperture can tell "a wood" from "a
grey wash" inside them. Every window station is therefore written twice more, at the true exposure
and at **8× linear gain** (`_x8`). The unlifted frame is what the LEVEL is judged from; the lifted one
is what the SHAPE is judged from; they are never confused for one another.

### The measurements, from the shipped bake

```
  station    fov  opening px   mean L  wood ON / OFF   glow share   pixels the wood reaches,
                                                                    and how many move >25%
  WinSeat     60   182 x 111   0.00338 / 0.00331          17 %      14.3 %   87 %
  WinHead     60   184 x 110   0.00333 / 0.00320          18 %      11.5 %   91 %
  WinNarrow   24   496 x 302   0.00334 / 0.00327          17 %      17.5 %   88 %
  WinWalk     55   636 x 306   0.00453 / 0.00448          14 %      14.5 %   59 %
```

### The renders

All under `.planning/debug/env-previews/` (gitignored, per the project's workflow rule). 678 frames
in the full cellar run; the ones this round is judged on:

| file | what it is |
|---|---|
| `env_cellar_WinSeat.png` | **the shot the whole feature is about** — the player's own seat, seated eye height 1.66 m authored, at the rig's real scale, looking at the window |
| `env_cellar_WinSeat_x8.png` | the same frame at 8× exposure: the trunks, the bars and the cobweb are legible |
| `env_cellar_WinSeat_woodoff.png` / `_woodoff_x8.png` | **the before/after pair** — the shipped cellar, same camera, same run |
| `env_cellar_WinHead*.png` | standing head, 2.02 m authored |
| `env_cellar_WinNarrow*.png` | seated head at 24°, the frame in which what is beyond the opening resolves at all |
| `env_cellar_WinWalk*.png` | two steps toward the window — the pose `AssertMoonThroughWindow` says the moon is visible from |
| `env_cellar_*_noglow*.png` | the same four with the aperture haze off, wood on and off |
| `env_cellar_WinCheat_x8.png` | **the wider shot: the cheat as a cheat.** From outside the room, above and behind: the cellar, the black night-ground plane, the sky dome with the moon — and one compact clump of 42 trees north of the window with nothing anywhere else |
| `env_cellar_WinPlan_x8.png` | straight down on the same thing; the fan's two edges in one frame |

`.planning/cellar-window/frustum.py` recomputes the window's solid angle from the bake's own
constants and prints the per-eye-height elevation band. `.planning/cellar-window/window-diff.py` amplifies
the difference between any two harness frames.

### The stations are derived and mirrored, not typed

Every window camera is aimed by `LookAtWindow` off `EnvRoomBuilder.CellarWindowCentre()` — a window
that moves re-aims the cameras that watch it. And the harness ASSERTS that its eye heights are the
bake's: the bake culls the wood against `CellarEyeSeated`/`CellarEyeStanding` and the harness would
otherwise photograph geometry built for somebody else's head.

---

## 6. WHAT I CHANGED THAT I WAS NOT ASKED TO — TWO THINGS, BOTH STATED SO THEY CAN BE VETOED

### (a) The aperture glow, 0.085 → 0.006 alpha

`C_GlowMoon` is an ellipsoid of cold haze scaled to 0.467 × 0.541 of the opening and sitting 6 cm
inside it. Its own comment says it exists so "the window reads as **the source and not as a hole with
something bright behind it**" — which is the exact opposite of what this round was asked for.

Measured on the 0.085 bake, over four player poses: **the glow was 70–77 % of ALL the light in the
aperture.** A view through a slot cannot survive that; the wood behind it, which the same instrument
shows filling the whole opening between the bars, changed the rendered mean by 0.2 %.

Both halves of the object's own history point the same way. Its comment was written when there was
NOTHING behind the window; there is now a real sky, a real moon and a real wood, so the window does
not need haze to read as a source. And ModBuild 149's user report — *"Bei Dunkelheit im Keller über
dem Kellerfenster ist noch etwas helles zu sehen entferne das"* — was about this object being too
visible, so a smaller one is the safe direction for that finding too. It is now 14–19 % of the
aperture's light. **What is kept is the air in the opening glowing; what is given up is its filling
the opening.** One line to revert.

### (b) The night sky patch's coverage — a pre-existing void, fixed

The patch's own sweep took the four directions from each eye to the four corners of the OUTER
opening, over the PLAY-SPACE DISC only. Both halves of that were wrong, in opposite directions,
which is why it looked reasonable and was not.

* **The eye set was too small.** The disc is where a player stands to reach the BOARD, and this
  file's own `AssertMoonThroughWindow` says the interesting window poses are not in it: *"two steps
  toward the window, not from the table"*. Measured: sightlines span az −58.1…31.2° over the disc and
  **−67.0…66.7° over the whole floor**. The patch ended 34.7° short of the eastmost real sightline,
  and past that edge a player standing off the board in the north-WEST corner, looking east through
  the slot, **saw the camera's clear colour** — the exact failure that object exists to remove.
* **The sweep was too coarse, and that is what hid it.** A direction from an eye to an outer corner
  need not clear the INNER opening, so the old hull contained bearings nothing can be seen along —
  and with the honest eye set it blows past what a patch can span at all (az −90…83, which fails the
  file's own gate).

Both are fixed by giving the patch the exact sightline bundle the wood is culled against — **one
instrument, two readers**, so the sky and the wood in front of it cannot disagree about where the
window points. The patch is 152 × 67° instead of ~107 × 62°, at a cost of **152 triangles and no
fill whatsoever**.

---

## 7. WHERE MY BRIEF WAS WRONG

1. **"Trunks and canopy read against a slightly-less-black sky."** Half right, and the half that was
   wrong cost two passes. The sky behind this window is `Swamp_StarDome` — 0.0024 linear, i.e.
   17/255, which IS "slightly less black" and IS something to silhouette against. But the brief's
   framing led me to test the wood's brightness against a mean over the whole aperture, where the
   stone, the bars and the moonbeam swamp it. The number that settles it is the mean over the pixels
   the wood actually reaches, and it had to be found the hard way.
2. **"A hint of moon rim on one edge."** Right, and it is not a hint — **it is the entire picture.**
   The moon is BEHIND this wood (the player looks north, the moon bears 40° east of north), so the
   faces turned toward the room get `saturate(dot(N, _DirDir)) ≈ 0`. There is no lit side to see. The
   rim is not a garnish on the silhouette, it is the only thing that makes a trunk a trunk, and it
   carries the forest room's own value undiminished while everything else is cut 4×.
3. **"Place the emitters out in that forest, at the window, and let the existing distance model do
   the work."** These are two different things and only one of them satisfies the user. Emitters out
   in the forest are up to 55° off the window as heard from the seat, which fails "immer von dem
   Fenster aus lokalisiert" outright. Emitters at the window with an unchanged gain are LOUDER than
   in the wood, because every voice's rolloff is flat inside 8–12 perceived m and the whole cellar is
   inside that — which fails "im Keller hört man sie weniger". **Some explicit attenuation was
   unavoidable.** What was avoidable, and avoided, is a taste knob: it is the voice's own rolloff
   over the real outside leg times a derived facade insertion loss.
4. **"State the triangle count, the draw calls and the texture memory, and compare them against what
   the cellar already costs."** Nothing existed that could answer that, so `LogRoomCost` was written.
   It is now printed for both rooms on every preview run.
5. **"A capture/preview camera aimed at nothing renders happily."** Correct, and worse than stated
   for a deliberately dark subject: a station aimed at nothing and a station aimed at a correct dark
   wood produce the SAME frame to the eye. Nothing short of a difference between two renders in one
   run distinguishes them. The brief's warning is what made me build the falsifier before believing
   any image.
6. **"Culling cannot see vertex shaders … if your trees sway, fix the bounds with an arc sweep."**
   Taken, and sidestepped: `_Sway` is 0 and asserted to be 0, so there is no displacement to bound.
   Stated as a decision in §3.

---

## 8. WHAT I COULD NOT VERIFY WITHOUT HARDWARE

1. **Everything about the sound.** No sound is rendered by the preview harness. The gains, the
   −13.1 dB insertion loss and the claim "an owl lands near 0.009–0.011 against 0.041 in the wood"
   are arithmetic over this file's own tables, not a measurement. **`WindowInsertion` is the one
   number to tune if it is too quiet or too loud, and it is one line.**
2. **That the calls really localise at the window on a head that turns.** The emitter is at the
   opening and Unity's spatialiser is fully 3D with `spread = 25°`, so this follows from the
   geometry — but "follows from the geometry" is what this project has been wrong about before.
   **It needs one scenario in the cellar with the head turned away from the window while a call
   fires.**
3. **Stereo.** Every frame in §5 is monoscopic. The rim term is view-dependent and therefore differs
   slightly between the eyes — which is physically what a rim IS, and the gradient is smooth, so it
   should fuse. The forest's trunks have carried the same term since ModBuild 134 without a report.
   Not confirmed here.
4. **The level of the whole thing on an OLED at 90 Hz through Virtual Desktop.** The window lives in
   a handful of 8-bit levels. Whether 17/255 sky against ~10/255 wood reads as a forest or as noise
   on the real panel, with the real compression in the link, is a hardware question. **The two knobs
   are `C_WoodBark`/`C_WoodFoliage`'s `_AmbUp` (the mass) and `_RimCol` (the edges), and they move
   independently.**
5. **Whether the aperture-glow cut (§6a) is welcome.** It changes a value the user tuned himself. The
   measurement says the old one hid the feature he just asked for; whether the room is now too plain
   at the window is his call, and it is one constant.
6. **Frame time.** 9,862 triangles and 2 draw calls is 6.6 % / 2.2 % of a room that already renders
   inside budget, and the fill is bounded by the aperture — but **no headset has confirmed a
   millisecond figure for this room** and none is claimed.
7. **Parallax and the "is it really out there" feel.** The wood is 8.5–17 m out, which was chosen
   partly so that head movement swings it noticeably against the opening. That is the whole point of
   putting real geometry there rather than a card, and it is exactly the thing a still frame cannot
   show.
