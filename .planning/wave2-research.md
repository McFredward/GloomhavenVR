# Wave 2 research — the cellar draught, and handprints that read as blood

Research lane. Nothing here is implemented; this file is the only thing this lane wrote.
Every licence quoted below was verified at its own source by this lane on **2026‑08‑15**
unless the entry says otherwise.

Two user verdicts drive it:

> "Die Luftströme im Keller sind zu cartoonig, zu grob — diese weißen Linien gefallen mir
> so nicht. Schau dir nochmal im Internet an wie das Problem sonst gelöst wird und nutze
> eventuell assets."

> "Ich mag die Handabdrücke gar nicht: a) Sie sind keine wirklichen Hände b) es schwebt
> über den Mauern c) es ist kackbraun statt blutig d) Die Position ist nicht gut, da ein
> Teil davon über dem Eingang schwebt wo gar keine Mauer ist. Nutze hier irgendwelche
> Texturen aus dem Internet die tatsächlich Horror verursachen könnten aber nicht das."

---

# 1. THE DRAUGHT

## 1.1 What is on screen now, measured from our own bake

Two Shuriken emitters draw the cellar's air, both `ParticleSystemRenderMode.Stretch`,
both alpha‑blended `GloomhavenVR/EnvParticleAlpha` on `Env_Wisp.png`, both tinted
`(0.46, 0.52, 0.62)`:

| emitter | file:line | max alive | authored width | stretched LENGTH | where |
| --- | --- | --- | --- | --- | --- |
| `ElemGust` (cellar branch) | `BuildEnvironmentRooms.cs:1962` | **34** | 1.4–3.4 cm | `speed × 0.200 × 2.6` = **39–78 cm** | a 5.4 × 2.6 × 0.6 m slab 2.6 m upwind of room centre, at **1.75 m — head height** |
| `ElemDraughtMouth` | `BuildEnvironmentRooms.cs:8069` | **92** | 1.2–2.6 cm | `speed × 0.165 × 2.3` = **17–38 cm** at birth speed, more with the shared push | a 12° cone at the window |

So at any instant up to **126 pale line segments between 17 and 78 cm long**, crossing the
whole room at head height, drawn **just as brightly in unlit air as in the moonbeam** —
nothing in a particle shader knows where the light is. The file's own comment at
`BuildEnvironments.cs:2246` already argues this about the previous round's dots.

**That is the entire diagnosis.** A pale, elongated streak parallel to the direction of
travel is not a rendering of air; it is the *motion line*, the comic/manga speed‑line
convention (Wikipedia, *Motion lines*: "abstract lines that appear behind a moving object
… parallel to its direction of movement, to make it appear as if it is moving quickly …
common in Japanese manga and anime"), the same device Persona 5 and Neon White use on
purpose to read as stylised. The user is not describing a tuning problem. He is naming a
genre. "Zu cartoonig" is literally correct.

And note the shape of the two rejections together:

* ModBuild 143 — "diese Pünktchen erinnern eher an weiße Funken" → **dots** rejected.
* ModBuild 147 — "diese weißen Linien" → **lines** rejected.

The invariant across both is not the sprite. It is **bright matter floating in unlit air**.
Neither a dot nor a line has any business glowing in a black cellar. That is the finding
this section is built on.

## 1.2 What the industry actually does — five families

### A. Dust confined to a lit volume — *the mainstream answer*

Air is invisible. What you see is the **Tyndall effect**: light scattered by suspended
particles, "for example, smoke or dust in a room — making a light beam entering a window
visible", and critically "the particles appear as points of light **against a dark
background**" (Britannica / standard optics). No light on the dust, no dust.

Shipped implementations do exactly this and nothing more:

* **Source engine (Half‑Life 2 and every Source game)** — `func_dustmotes` is a **brush
  entity**: "a brush entity … that spawns sparkling dust motes within its volume", "affected
  by `env_wind`", default material `particle/sparkles.vmt`. The light shaft itself is
  *separate hand‑placed geometry* (`Effects/vol_light.mdl`, or a non‑solid brush with
  `models/effects/vol_light001`). The mapper puts the motes volume where the shaft is. Two
  objects, one place. (Valve Developer Community, *func_dustmotes* / *Dust, Fog, & Smoke*.)
* **Unity, Volumetric Light Beam** (the most widely used BiRP light‑shaft asset) ships a
  dedicated **Volumetric Dust Particles** component whose entire job is confinement: "The
  distance range (from the light source) where the particles are spawned", with Min/Max
  bound along the beam and a distance‑culling system it "highly recommend[s]" enabling.
* The cheap shader variant everyone reaches for next: **billboarded motes whose alpha is
  multiplied by a shadowmap sample**, so a mote standing in shadow is simply not drawn.

### B. Volumetric fog with animated 3‑D noise scrolled along a wind vector

UE5 volumetric fog (froxel grid + temporal reprojection), Frostbite's SIGGRAPH 2015
physically‑based volumetrics. The art recipe is always the same: "volumetric materials use
3D noise … multiple layers of 3D noises are stacked", animated by scrolling the sample
position. **Not affordable here** — BiRP has no froxel pipeline, a 3‑D noise volume is a
texture read per raymarch step, and temporal reprojection is stereo‑hostile.

But its *art lesson* is exactly what `EnvBeam.shader` already does analytically: the wind is
**density modulation inside a lit medium**, evaluated per sample along the view ray, with the
pattern **translating** along the draught (`pa = dot(P, wDir) - wPhase`) rather than
wobbling. We already ship the modern answer; we just also ship the cartoon one on top of it.

### C. Screen‑space refraction / heat‑haze distortion

**Rejected on three independent grounds.**
1. Cost. A GrabPass "takes a screenshot every frame, which will always cause some amount of
   performance hit" (Poiyomi docs). On Quest's Adreno "SceneColor breaks tile rendering", and
   reading a texture from main memory is "approximately one order of magnitude slower than
   reading from a texture tile in GPU memory" (Meta, *Mobile GPUs and impaired algorithms*).
2. MultiPass doubles it — the grab happens per eye.
3. It is a **screen‑space** effect, therefore per‑eye, therefore a candidate for exactly the
   stereo rivalry this project already had to park once
   (`.planning/wall-fade-stereo-rivalry.md`).

### D. Nothing in the air at all — the draught is what it MOVES

Standard art direction and the answer most horror interiors actually ship. Sucker Punch on
Ghost of Tsushima: the goal was that "everything needed to move, with many systems working
in concert to provide the illusion of wind actually blowing", and "global wind direction is
integrated into nearly every effect in the game". Trees, cloth, rope, flame, smoke — the wind
is a *direction shared by every animated thing*, never a sprite.

**We already own the whole machinery and it is already gated on Air:**

| cue | where | current Air response |
| --- | --- | --- |
| the three candle flames lean and gust | `EnvFlame.shader` `_AirGust` | `1.0 + 3.4 × air` at the flame nearest the window, graded by distance |
| every cobweb sways harder | `EnvRoomCutout.shader:197` | `s *= 1.0 + 1.20 × air` |
| foliage / growth cards fold | `EnvRoomCutout` `_ElemWind` | authored per card |
| the shaft's own density streams | `EnvBeam.shader` | `dens *= 1 + 0.95 × air × f`, translating at 1.35 m/s |

### E. Stylised streaks — what we ship now. Rejected twice. Not a family we may stay in.

## 1.3 Cost, brutally

* Under **MultiPass every transparent quad is submitted and shaded twice.**
* Meta's own guidance: "Keep alpha blended transparency to a minimum"; the transparent queue
  is drawn "back to front **without depth testing** and [is] subject to overdraw"; "avoid
  overlapping alpha‑blended geometry (e.g. dense particle effects)"; and "additive blending is
  MUCH cheaper than alpha blending on mobile".
* Current draught bill: **126 stretched quads × 2 eyes = 252 shaded quads/frame**, and their
  *stretched footprint is the expensive part* — a 78 cm streak seen at 2 m covers roughly
  20° of arc. Alpha‑blended, in linear space, in the transparent queue, i.e. pure overdraw
  with no early‑Z rejection. This is the single largest transparent‑fill item in the cellar
  and it is drawing the thing the user rejected.
* `EnvBeam` is **already paid for**: 24 taps over one 1.2 m‑radius hull in one corner, and
  the Air term is 3 sines per sample on a *uniform* branch. Making the striation louder
  costs literally zero additional instructions — it is a constant multiply that is already
  in the shipped code path.
* A replacement in‑beam mote population of 24–32 **round, 1–2 cm** sprites: at 2 m each is
  5–8 px across. Against the current bill that is **an order of magnitude less fill**.
* Grab pass / refraction: forbidden, see 1.2 C.

## 1.4 Ranked recommendation

### R1 — DELETE both free‑air streak emitters. *(the whole of the fix, ~90% of the effect)*

`ElemGust`'s cellar branch and `ElemDraughtMouth` both go. Not retuned — deleted. Every
previous round retuned them (dots → longer dots → filaments → breathing filaments) and each
retune was rejected. The property being rejected is not reachable by tuning, because it is
"bright matter in unlit air".

### R2 — Make `EnvBeam`'s striation the draught's loud statement

It already is the right construction and it already streams along `_DraftDir`. What changes
is only that it is now the *only* thing, so it may be pushed: `_Shimmer` and the in‑integral
`0.95 × air` filament amplitude become the tuning surface, not the particle count. Zero new
instructions.

The shader's own note (`EnvBeam.shader`, "THE WIND, CARRIED BY THE ONLY LIT AIR") already
contains the correct argument for why the ridges must be long filaments and not specks: a
line integral averages an isotropic high‑frequency field to its mean, so specks integrate
away — only crests that run *along* the wind survive a broadside ray.

### R3 — A SMALL in‑beam‑only mote population *(do ship this; do not skip it)*

The industry ships dust as well as shafts, and it is right to. A striation alone is a
texture on a volume and reads as a shader; discrete matter is what says *there is stuff in
this air*. Specification:

* **Round, not stretched.** `velocityScale = 0`, `lengthScale = 1`. A real mote at 1 m/s
  moves 1.1 cm in an 11.1 ms frame — there is no motion streak to draw. The streak was an
  invention.
  *VR‑safety note:* the house rule forbids things that re‑orient with the head. Preferred
  construction is `ParticleSystemRenderMode.Mesh` with a tiny crossed‑quad mesh — the fire's
  own precedent — which has no camera term at all. If Stretch is kept, it must stay at
  `cameraVelocityScale = 0` as it is today.
* **Confined to the shaft, by construction and by shader.**
  (i) Emit inside the hull — a thin cone along `_BeamDir` from `_BeamOrg`, length `_Len`,
  radius `_W0 + _WK·s`, so no mote is ever born in unlit air; **and**
  (ii) add a beam mask to `EnvParticleAlpha`'s **vertex** shader: hand it `_BeamOrg`,
  `_BeamDir`, `_W0`, `_WK`, `_Decay` and multiply the vertex colour's alpha by
  `exp(-(dperp/w)^2 - s·_Decay)`. That is ~10 ALU on **4 vertices per particle** — free —
  and it makes the confinement *physically true* rather than approximate: a mote drifting out
  of the shaft fades out exactly where the light stops, instead of popping at an emitter
  boundary. It also means the two lit things in the room agree by construction, because they
  read the same five uniforms.
* **Slow.** Visible drift 0.10–0.30 m/s inside the shaft, not 1.0–1.8. Dust motes in a
  sunbeam are dominated by **Brownian motion** — "the jerky, fluttering motion of particles
  in fluids" — not by transport. Keep the existing `ps.noise` curl; drop the speed.
* **They must WINK.** A mote is a flat flake tumbling; it catches the light intermittently.
  `EnvParticleAlpha` already has `_ElemSpark` ("fast twinkle amount"). This is the single
  strongest cue that separates *dust* from *dots*, and it is the one thing neither rejected
  round had.
* **Count 24–32**, in a 1.2 m‑radius × 4 m shaft. That is a believable density. 92 is a
  stream; a stream out of a window is a machine.
* **Colour stays the moon's.** `(0.46, 0.52, 0.62)` is right and should not move. Dust in a
  moonbeam is the moon's own colour at a fraction of its intensity, never white.

### R4 — Close the element hole: Air must still speak when the beam is dark

Under full **Dark** the beam is `0.0275×` — deliberately, per the ModBuild 143 ruling. If
the draught lives *only* inside the beam, then the subsets `{Air, Dark}`, `{Air, Dark, …}`
show **nothing at all**, and the 64‑subset requirement fails. The answer is family D, which
costs nothing because it is already built and already gated on `air`: under Dark the
draught's evidence is the **flames guttering** (`_AirGust`), the **webs swaying**
(`EnvRoomCutout:197`) and the drip/puddle. State it as a requirement on the implementation
lane: verify `{Air}`, `{Air, Dark}`, `{Air, Light}` and `{Air, Dark, Ice}` in the preview
harness and confirm each one still reads as moving air.

### R5 — REJECT: heat haze / grab‑pass refraction, volumetric fog, and any further tuning of streaks.

## 1.5 Explicit verdict on the "put it inside the beam" hypothesis

**The hypothesis is right, and the industry agrees with it — with one amendment.**

* *"Real moving air indoors is only visible where light catches it"* — **unanimous.** It is
  the Tyndall effect; it is why `func_dustmotes` is a bounded volume a mapper places in a
  shaft; it is why Volumetric Light Beam's dust component's only parameters are *where along
  the beam*; it is why the cheap shader trick is to multiply mote alpha by a shadowmap sample.
* *"Delete the free‑floating streaks entirely"* — **yes, unconditionally.** They are drawn in
  unlit air, they are 17–78 cm long, and they are the comic‑book convention for speed. Both
  rejections point at this and only this.
* *"Put the draught inside the beam as animated density"* — **yes, and we already have the
  best available implementation of it**, an analytic line integral with a translating
  plane‑wave striation. Nothing needs inventing; it needs to become the only statement.
* **The amendment.** Do **not** delete the motes entirely — relocate them. Everyone who ships
  a shaft also ships motes inside it, because a modulated volume is a surface effect and
  discrete matter is what makes it *air*. The correct read of the hypothesis is *"a few slow
  dust motes that only exist inside the shaft's volume"* — which the brief already said, and
  which is right.
* **The second amendment.** Because the shaft dies under Dark, the beam cannot be the *only*
  home of Air. R4 is not optional.

## 1.6 Assets for the draught: none needed

Nothing third‑party is required. A dust mote is a radially symmetric soft dot, and
`BuildEnvironments.MakeWisp`/`MakeFogPuff` already generate that class of sprite
procedurally, seeded, so every client bakes the same texture. If the lane wants a
ready‑made soft particle anyway, **Kenney's Particle Pack is CC0** and `circle_01…05` are
exactly it (section 3, entry 7) — but a 512² PNG for a 6‑pixel dot is not a good trade,
and a procedural sprite avoids a bundle entry.

---

# 2. THE HANDPRINTS

## 2.1 The four faults, located in our own source

| user fault | cause, with the line |
| --- | --- |
| **(a) "keine wirklichen Hände"** | `BuildEnvironments.HauntHands()` (`:1065`) builds each print from **signed‑distance capsules**: one capsule "palm pad" (`HSegDist(px,py,0,-0.30,0,0.14) - 0.30`), *n* finger capsules radiating from it, one thumb capsule, all `min`‑unioned and smoothed. That grammar can only ever produce a **mitten with sausages**. It is the same structural failure the atlas's own header already diagnosed for the SDF faces: "A handful of SDF primitives can only produce a SILHOUETTE WITH FEATURES DRAWN ON IT, and a silhouette with features drawn on it is exactly the grammar of a pictogram." The prints were the one card that did not get the fix. |
| **(a′) SIZE — and this is probably the bigger half** | Measured out of the shipped `Env_Haunt.png`, tile 8: three prints across a **1.24 m × 1.24 m** card (`BuildEnvironmentRooms.cs:9161`, `Vector3.up * (c.height * 0.5f)` with `height = 1.24`). Ink bounding boxes: **81 × 204 px**, **85 × 225 px**, **80 × 142 px** of 256 → the middle print, which has no drag smear, is **≈ 0.41 m wide × 1.09 m tall**. An adult handprint is ~19 cm long and ~9–10 cm wide. **These are roughly 5.5× life size.** In VR the player has their own hands in the frame for comparison; a five‑times‑lifesize hand cannot read as a hand under any texture. |
| **(b) "es schwebt über den Mauern"** | `AddHauntMark` is placed at `at.x = -hw + 0.03f` — the card stands **3 cm proud of the wall plane**, with its own comment saying so ("3 cm proud of it so nothing z‑fights"). At any grazing angle that is visible parallax between the mark and the stone. It is also a **soft‑edged rectangle**: the atlas applies `HSStep(1.00, 0.92, max(|x|,|y|))` (`:1116`), i.e. the card fades out at its own border, which is the classic tell of a sticker. |
| **(c) "kackbraun statt blutig"** | `key = new Color(0.105f, 0.090f, 0.070f)` (`BuildEnvironmentRooms.cs:8968`). In HSV that is **hue ≈ 30°, saturation 0.33, value 0.105** — an unsaturated dark orange. That is mud. Worse, it is *realistic*: dried blood really does go "dark reddish‑brown, resembling cocoa powder or dark chocolate" and then "dark brown or black". Realism was the trap. See 2.3. |
| **(d) partly over the doorway** | The file already fought this once — the comment at `:8959` records that the first two bakes put the prints on the stair opening and moved z by 1.05 m. He is still seeing overlap, so the placement is still a **hand‑typed offset** rather than a *measured* clearance against `StairHole` and the wall's real extent. That is the implementation lane's half; the art half is that a 1.24 m card is simply too big to fit anywhere clean on that wall, and shrinking to life size removes most of the problem by itself. |

Visual confirmation: rendering the shipped atlas's alpha shows three recognisably
hand‑shaped blobs — solid palms with **no missing arch**, **fused thumbs**, rounded capsule
fingertips, and a downward drag on the left one. Exactly a pictogram of a hand.

## 2.2 What the reference sources give us

Full source enumeration with licences is section 3. Summary of what exists:

* **A photorealistic *blood‑on‑masonry* handprint does not exist under CC0.** The two best
  genuine‑blood handprint photographs on Wikimedia Commons are both **CC BY‑SA** and are
  therefore rejected under this project's standard. Poly Haven (849 textures, full catalogue
  dumped) and ambientCG (2000 materials, full catalogue dumped) have **zero** blood or
  handprint assets — recorded so no future round repeats that search.
* **But real photographed handprints with the right physics do exist under CC0, at usable
  resolution**, and they carry precisely the properties the current SDF cannot fake.
  The primary find is a 2730 × 1820 CC0 photograph of four paint handprints on white with
  gravity drips (section 3, entry 2), verified and measured by this lane.
* A CC0 **scanned hand contact/grease print** overlay also exists (cgbookcase *Handprint 01*,
  3K, entry 1) — useful as a contact‑pressure mask, marked partly uncertain because the CDN
  would not serve the file to a non‑browser client.

**Recommendation: derive our own atlas from the photograph, exactly as the fungus atlas was
derived from four Commons photographs.** The project has done this three times
(`fire_atlas_pipeline.py`, `cobweb_pipeline.py`, the fungus atlas) and the precedent and the
documentation standard are already in `Environments/License.md`. The photograph keys out of
white in one step (measured: 91.8 % background, 8.2 % ink), so this is a cheap pipeline, not
a hand‑traced one.

## 2.3 What the texture must carry

`EnvHaunt.shader`'s decal path reads the atlas as **R = key mask, G = rim, B = fill,
A = coverage**, and composes `col = i.color.rgb * T.r + _Fill.rgb * T.b` (`:591‑599`).
For blood that channel assignment must change meaning:

### A — coverage. The contact mask.
* **Hard, ragged, photographic edges. No border vignette.** Delete the
  `HSStep(1.00, 0.92, …)` card fade — a print does not fade out towards a rectangle.
* Alpha must go to **fully transparent between the prints and at the tile border**, with a
  one‑texel ramp, the same standard the fungus atlas records.

### R — THICKNESS, not "lit". *This is the single most important change.*
Blood's colour is a function of **optical depth**: "the degree of darkness often relates to
the thickness of the deposit, as concentrated material absorbs more light… thin films might
appear bright red while thick deposits appear darker or black." A flat brown fill can never
read as blood because blood is not one colour. The shader must map thickness through a
**three‑stop ramp**, brightest and most saturated where the film is thinnest:

| optical depth | suggested sRGB | what it is |
| --- | --- | --- |
| thinnest edge / the outer 1–2 mm of every mark | `(0.62, 0.10, 0.06)` | where it reads *red* |
| bulk of the print | `(0.20, 0.020, 0.014)` | dark, barely chromatic |
| deepest — the heel of the palm, the head of a drip | `(0.06, 0.005, 0.005)` | near black |

For calibration: this lane measured the CC0 reference photograph's ink. Darkest 5 % of ink
pixels = sRGB **(142, 13, 6)**; mean ink = **(174, 85, 68)**; mean saturation **0.62**.
The current shipped key is `(27, 23, 18)` at saturation 0.33 — an order of magnitude less
chroma, and no ramp at all. *Any* thickness ramp beats it.

### G — WETNESS / specular mask.
Fresh blood is glossy — drying blood "acquires a more glossy, varnish‑like appearance" — and
a wet mark in a dark room **catches the key light**. This is the cue that separates blood
from rust, paint and dirt more decisively than hue does. High on the drips and on the
freshest bulk, low at the thin dried edges. The room already has the light to catch: the
moon key and three candles.

### B — the drip‑run mask.
A separate downward‑running channel so the drips can *extend* over the event's lifetime
(2.4) rather than being painted on from the first frame.

### The SHAPE, which is where the current one dies
A print is a **partial contact**, and every one of these is visible in the CC0 reference and
absent from our SDF:

1. **The arch of the palm is MISSING.** A flat hand pressed to a wall touches at the heel,
   the thenar and hypothenar pads and the finger pads; the hollow of the palm often does not
   touch at all. The reference has a clean white hole in the middle of every palm.
2. **The thumb is DETACHED** — a separate island, offset and rotated, in essentially every
   real print.
3. **Fingers break into pad segments**, with gaps at the joints. Not continuous capsules.
4. **Drips run DOWN, from the lowest points of the mass**, thinning as they go, wandering
   rather than running straight, sometimes with a bead at the head. The reference has two
   ~40 cm drips.
5. **At least one SMEAR** — a hand that slid. The smear direction is the story: it says
   which way the thing was moving.
6. **Handedness.** Left *and* right. Three prints of the same hand is a stencil, which is
   what the current three (all built from one spec with different scales) look like.
7. **Life size. 19 cm long, 9–10 cm across.** Non‑negotiable in VR.

### What to drop
The sixth finger on the middle print. It is a good instinct badly aimed: a wrongness the
player must *count* to notice is a joke, not a fright, and at 5× life size it reads as a
cartoon. If an anomaly is wanted, make it a **child‑sized** print among adult ones, or one
print at a height nothing standing could reach — anomalies of scale and place work
pre‑attentively; anomalies of digit count do not.

## 2.4 The event shape

**Recommendation: a CONTACT, not a bloom. Three impacts in sequence, then drips that run for
the whole hold.**

Why:

* **A slow fade‑in is the grammar of a ghost image.** It is a photograph developing. The
  *physical* event that makes a bloody handprint is instantaneous — a hand hits a wall. The
  current envelope is `reveal 2.8 / hold 2.2 / fade 3.0`; a 2.8 s reveal tells the player
  "this is an apparition", which is the register he called "kein echter Horror".
* **A contact gives the room a cause.** Three prints appearing in sequence, spaced along the
  wall and descending, is a *body moving along the wall*, off‑screen, in a cellar the player
  is standing in. That is the horror, and it costs one `floor()` in the shader — the atlas is
  already laid out in thirds precisely so the shader can bloom them one at a time.
* **The drips are the only continuous motion, and they are the "wet" statement.** Slow,
  gravity‑driven, still running while the player looks — a mark that is still moving is a
  mark that was made *just now*.

Proposed schedule inside the existing 8 s envelope (numbers for the implementation lane to
tune, not to copy):

```
t = 0.00   print 1 lands in <= 0.12 s, with a wet slap on the same clock (EnvSound)
t = 0.90   print 2, ~40 cm along the wall and lower
t = 1.80   print 3, a SMEAR — it slid — lower again, drag consistent with the direction
t = 1.8-6  the drips grow downward, ~4-8 cm/s, decelerating
t = 6.0+   fade over the existing ~3 s.  Fading OUT is right: "it was never there" is the
           ghost half, and it is the half a fade actually expresses well.
```

**Why not "it is simply there when you turn around"**, which the literature does favour —
Layers of Fear's whole method is that "every time you look again at something behind you
[it changes]", and "developers can load stuff behind the player's back":

> **It is not multiplayer‑compatible and therefore not available to us.** Gating on gaze
> needs per‑client gaze, and two players in the same cellar are looking in different
> directions. Under this project's standing rule — everything animated rides a shared clock
> so two players see the same thing at the same instant — a gaze‑gated event either desyncs
> or has to be net‑arbitrated, which makes it unreliable in exactly the moment it must be
> reliable.

The **slap sound is the honest substitute** and it delivers the same experience
deterministically: the player who is not looking is *told* to look, and turns to find the
prints already on the wall. Same beat, no gaze dependency, and it uses machinery the room
already has.

## 2.5 The house rule on painted‑on effects — and why a print is the exception

> "A painted‑on effect has NO SILHOUETTE — a function of the albedo computes inside the
> object's own outline, which is the definition of a stain."

A handprint **is a stain**, in the strict sense the rule means. A blood film is on the order
of 0.1 mm thick; it has no silhouette at any viewing distance, casts no shadow, and occludes
nothing. The moss failure was the opposite case — moss *does* stand off the surface, it does
have a silhouette, and painting it denied a real property. Here the rule points the other
way: painting it into the wall is not a compromise, it is the correct model, **and it is the
direct fix for fault (b).** Concretely, any of:

* a term in the wall's own shader over the wall's own UVs (truest, costs ALU on every wall
  fragment — or a variant material on one wall sub‑mesh); or
* a **coincident** decal with `Offset -1, -1` and `ZWrite Off` instead of a 3 cm push‑out;
  and in either case
* **mask the print out of the mortar grooves** using the masonry's own height/cavity map.
  `medieval_blocks_05` has deep mortar; a hand pressed to a block wall marks the *faces* and
  skips the recesses. That one multiply is what will make it look pressed into the stone
  rather than laid over it, and it is the same "no silhouette, computes inside the surface"
  logic the rule is stating.

Never a card standing proud of the wall. That is what "es schwebt über den Mauern" is.

---

# 3. ASSETS FOUND

Standard applied: **CC0 / public domain only**, per `Environments/License.md`. Anything
CC‑BY, CC‑BY‑SA, "free for personal use", or with a no‑redistribution‑in‑collections clause
is rejected, because this mod is publicly downloadable and its bundle is extractable. A CC0
asset may live in this repository *and* ship in the bundle — unlike the fire atlas, which is
the one Asset‑Store item and is fenced off in `License.md`.

## Recommended

### 1. cgbookcase — *Handprint 01* — **CC0** — *partly uncertain*
* Page: <https://www.cgbookcase.com/textures/handprint-01>
* Category "Surface Imperfections", type `pbr-approximated`, **max 3K** (1K / 2K / 3K
  offered), released 2019‑05‑01, tags `handprint, overlay, imperfection`.
* **Licence, verified two independent ways by this lane:**
  * the live page's own machine‑readable metadata — `<meta name="tex1:license" content="cc0">`
    — and its preview alt‑text `"preview render of the free PBR material Handprint 01 (cc0
    texture)"`;
  * the site's licence page (now removed from the live SPA, retrieved from the Internet
    Archive, *Last updated: May 1, 2019*), **verbatim**: *"All textures on cgbookcase.com are
    licensed as CC0. … In short, this means you can use the textures for anything, even
    without giving credit."* With an explicit carve‑out: *"Keep in mind that while the
    texture maps are licensed as CC0, the preview renders of them and the content from the
    tutorials on how to use the textures are not."*
* **Redistributable in an extractable bundle: YES** (CC0), the texture maps only — never the
  preview render.
* **UNCERTAIN, flagged:** the CDN (`https://cgbookcase-volume.b-cdn.net/t/<Name>/<Name>_<n>K.png`,
  the pattern the site's own JS builds) returned **403/404 to every non‑browser request** this
  lane made, so **the actual pixels were not inspected**. Judgement is from the preview render
  only: a glossy black sphere carrying grease/contact prints — i.e. this is a **contact‑pressure
  overlay**, valuable as a *mask*, not as a blood albedo, and possibly low‑contrast. The
  implementation lane must download it through the site's own Download button and look before
  committing to it.

### 2. Flickr — *"handprints 4"* by **lisafree54** — **CC0 1.0** — **PRIMARY RECOMMENDATION**
* Page: <https://www.flickr.com/photos/136594255@N06/26383403281>
* **Original file: <https://live.staticflickr.com/1679/26383403281_ba8bfae8de_o.jpg> —
  2730 × 1820** (downloaded and verified by this lane; Canon PowerShot SX260 HS EXIF intact).
  *Note: a `_b` URL circulating at 1024 × 683 is not the ceiling — the `_h` 1600, `_k` 2048
  and `_o` 2730 sizes all exist and serve.*
* **Licence, verbatim from the photo page's own embedded JSON:**
  `"license": "https://creativecommons.org/publicdomain/zero/1.0/"`, Flickr licence id `9`
  (= Public Domain Dedication, CC0).
* **Content, measured by this lane:** four handprints on plain white. Ink coverage **8.17 %**;
  darkest 5 % of ink sRGB **(142, 13, 6)**; mean ink **(174, 85, 68)**; mean saturation **0.62**.
  Keys out of white in one threshold, no rotoscoping. Each print ≈ 700 px in the original.
  Carries, visibly and unambiguously: **missing palm arches**, **detached thumbs**,
  **fingers broken into pads**, **two ~40 cm gravity drips**, **specular highlights on the
  wet paint**, and four distinct hands.
* Caveats to record: it is red paint, not blood, so the hue is terracotta rather than
  blood‑dark and the colour must be re‑authored (2.3); and the baked‑in white specular blobs
  must either be kept deliberately as the wetness cue or flattened and replaced by a shader
  specular term, since they were lit by a different light than our cellar's.
* **Redistributable in an extractable bundle: YES** (CC0).

### 3. Flickr — *"handprints 2"* by **lisafree54** — **CC0 1.0** — shape library
* Page: <https://www.flickr.com/photos/136594255@N06/25844671504> ·
  original <https://live.staticflickr.com/1447/25844671504_e490989a0e_o.jpg> — **2080 × 2310**.
* Same verbatim licence evidence: `"license": "https://creativecommons.org/publicdomain/zero/1.0/"`,
  Flickr licence id `9`.
* A white plank door carrying **~60 distinct real handprints** in ochre mud — adult and child,
  left and right, full and partial, smeared and clean. ~200 px per print, so not a decal
  source, but an outstanding **variety reference** and a source of authentic contact patterns
  to trace. **Redistributable: YES.**

### 4. Wikimedia Commons — *Serbian bloody handprint symbol.svg* — **CC0 1.0** — *use last*
* Page: <https://commons.wikimedia.org/wiki/File:Serbian_bloody_handprint_symbol.svg> ·
  file <https://upload.wikimedia.org/wikipedia/commons/2/26/Serbian_bloody_handprint_symbol.svg>
* 190.571 × 250.5 px SVG (**vector, resolution‑free**), author **ImStevan**, uploaded 2025‑03‑30.
* **Licence, verbatim from the Commons API `extmetadata` (queried by this lane):**
  `LicenseShortName = 'CC0'`, `UsageTerms = 'Creative Commons Zero, Public Domain Dedication'`,
  `LicenseUrl = 'http://creativecommons.org/publicdomain/zero/1.0/deed.en'`, `Restrictions = ''`.
* Rendered and inspected: a genuine print silhouette — **detached thumb**, **broken palm
  interior**, irregular finger pads. Alpha is the path fill, so no keying at all.
* **Caveat this lane adds, and it is why this is ranked last:** the file's own Commons
  description reads *"Bloody hand print, commonly used by Serbian protesters during the
  2024—2025 protests."* It is a **live political emblem**. Shipping a recognisable protest
  symbol inside a horror mod is a needless association, and the interior mottling is stylised
  rather than photographic. Use only if entries 1–3 fail, and only reworked past recognition.
* **Redistributable: YES** (CC0), licence notwithstanding the above.

### 5. StockSnap — *"Hands Print"* — **CC0** — low priority
* Page: <https://stocksnap.io/photo/hands-print-631FG503IR> — **6016 × 4000**.
* StockSnap licence, verbatim: *"All photos on StockSnap fall under the CC0 license… you can
  copy, modify, distribute and publish the work, all without asking permission."*
  *(verified by the search sub‑lane, not re‑verified first‑hand by this lane — treat as
  needing one confirmation before use.)*
* Red‑ochre positive handprints on rock at very high resolution, but with modern graffiti
  names ("NIM", "NICK") overlapping that would have to be cloned out. **Redistributable: YES.**

### 6. ambientCG — *Fingerprints001–009*, *SurfaceImperfections014–020*, *Smear002/004/005/006* — **CC0**
* Catalogue: <https://ambientcg.com/> · e.g. <https://ambientcg.com/a/Fingerprints001>
* **Licence, verbatim from <https://ambientcg.com/license> (fetched by this lane):**
  *"All ambientCG assets are provided under the Creative Commons CC0 1.0 Universal License.
  This applies to the downloadable asset files and the material preview renders shown for
  each asset on the site. … You can copy, modify, distribute and perform the assets, even for
  commercial purposes, all without asking permission. **You can include the raw files in your
  project, for example a video game.**"*
* Useful for grime, smear and wetness *detail* layered under a print. Up to 4K PNG.
  **Not** a blood or handprint source. **Redistributable: YES.**

### 7. Kenney — *Particle Pack* — **CC0** — only if a ready soft mote is wanted
* Page: <https://kenney.nl/assets/particle-pack> ·
  <https://kenney.nl/media/pages/assets/particle-pack/f8fe0f8cb8-1677578741/kenney_particle-pack.zip>
* **Licence, verbatim from the asset page (fetched by this lane):** `License Creative Commons
  CC0`. The sub‑lane additionally extracted the archive and quotes its bundled licence file:
  *"License (Creative Commons Zero, CC0) http://creativecommons.org/publicdomain/zero/1.0/ —
  You may use these assets in personal and commercial projects. Credit (Kenney or
  www.kenney.nl) would be nice but is not mandatory."*
* 512 × 512 transparent PNGs; `circle_01…05` are soft dust motes. **Redistributable: YES.**
* **Not recommended** — see 1.6: a 512² texture for a 6‑pixel dot is a bad trade against our
  existing procedural sprite generator.

## Searched and empty — recorded so no future round repeats it

| source | method | result |
| --- | --- | --- |
| **Poly Haven** | full catalogue dumped via `api.polyhaven.com/assets?t=textures`, **849 textures**, regex `blood\|hand\|decal\|smear\|stain` over ids, tags and categories | **zero** blood/hand/decal assets. All 37 matches are word fragments (`stained_pine`, `climbing_wall`). Poly Haven has no decal category at all. |
| **ambientCG** | full catalogue dumped via `api/v2/full_json?type=Material&limit=2000`, **2000 materials**, same regex | **zero** blood or handprint assets. `Fingerprints001–009` and the `SurfaceImperfections` set are the only relevant hits (entry 6). |
| **TextureCan** | catalogue + terms | CC0 confirmed, but no blood / decal / stain category. |
| **Wikimedia Commons** | API `generator=search`, 11 query terms (`handprint`, `bloody handprint`, `hand print blood`, `blood smear`, `blood stain`, `palm print`, `hand stencil`, `Handabdruck`, `bloody hand`, `blood spatter`, …), `gsrnamespace=6` | one CC0 vector (entry 4). Every genuine‑blood photograph is CC‑BY‑SA — see rejections. |
| **Openverse** | API, `license=cc0,pdm`, 10 query terms | surfaced the Flickr CC0 set (entries 2–3). Anonymous `page_size` caps at 20. |
| **archive.org** | advancedsearch `bloody handprint AND mediatype:image` | 1 result, `licenseurl: None`. Nothing. |

## Rejected, with the clause that killed each

* **Commons — "Bloody Handprint from Good‑Luck Sacrifice — Shop Wall, Ura Tepe, Tajikistan"**
  (3648 × 2736). The single best *real blood on a wall* handprint found anywhere.
  `LicenseShortName = 'CC BY-SA 2.0'`. **REJECT — attribution + share‑alike.**
* **Commons — "Menstrual hand print with menstruation blood"** (1802 × 2400). Literally real
  blood, ideal subject. `LicenseShortName = 'CC BY-SA 4.0'`,
  `UsageTerms = 'Creative Commons Attribution-Share Alike 4.0'`. **REJECT.**
* **Commons — "White handprint on a concrete wall"** (3000 × 4000), Kent Madsen,
  `CC BY-SA 2.0`. **REJECT.**
* **Unsplash.** Not CC0. Verbatim: *"Images cannot be sold without significant modification"*
  and prohibits *"Compiling images from Unsplash to replicate a similar or competing
  service."* An extractable bundle is a redistribution vector. **REJECT** — same class of
  clause as the ShareTextures rejection already on record.
* **Pexels.** Not CC0. Verbatim: *"Don't redistribute or sell the photos and videos on other
  stock photo or wallpaper platforms"*, *"Don't sell unaltered copies of a photo or video…"*
  **REJECT.**
* **Rawpixel.** **UNVERIFIABLE** — Cloudflare 403 on both `curl` and WebFetch for
  `/services/terms` and `/services/license`. Tempting hits exist (4000 × 4000 CC0‑tagged
  blood‑splatter PNGs with alpha) but the licence could not be read at source.
  **DO NOT USE without first‑hand verification.**
* **Prehistoric cave hand stencils** (Cueva de las Manos / El Castillo class; e.g. Commons
  *"Open hand stencil on ceiling"*, Google Art Project, ~2449 × 3265, **Public domain**).
  Licence is fine; **REJECT ON CONTENT** — confirmed by inspection that they are **negative
  stencils**: pigment sprayed *around* the hand, so the hand is the *unpainted* rock. That is
  the inverse of a print, and inverting leaves a hand‑shaped hole welded to heavy rock noise.
* **Grauman's Chinese Theatre / Hollywood / Dublin Gaiety / Ueno Park handprints** (many, PD
  or CC0, up to 5472 × 3648). **REJECT ON CONTENT** — concave impressions pressed into
  cement, read as shadow‑and‑highlight relief with no pigment and therefore no usable
  silhouette.
* **Commons — "Elmer Wayne Henley Handprint Artwork"** (822 × 640, `LicenseShortName = 'CC0'`,
  uploader claims own work). **REJECT** on two independent grounds: the licence chain is
  doubtful (the uploader dedicates CC0 as "own work" but the artwork is a third party's), and
  the provenance is a convicted serial killer's prison artwork — a reputational hazard in a
  publicly distributed mod, and cheap to avoid.

---

# 4. WHAT I COULD NOT ANSWER

1. **I could not inspect the cgbookcase *Handprint 01* pixels.** The licence is verified two
   ways and is solid; the *image* is judged from a preview render only, because the CDN
   refuses non‑browser requests (403/404 on every path variant, including the exact pattern
   the site's own JavaScript constructs). Its usefulness — contrast, whether the palm arch is
   present, whether it is one hand or several — is **UNCERTAIN**. Someone must open it in a
   browser before it is planned in.
2. **No published breakdown exists of how a specific shipped horror game renders an indoor
   DRAUGHT**, as distinct from dust motes or god rays. I looked; the technical literature
   covers volumetrics and dust, and the art literature covers wind‑as‑motion, but nobody
   publishes "here is how we made moving air visible in a cellar". My section 1 conclusion is
   *assembled* — from the optics (Tyndall), from two concrete confinement implementations
   (`func_dustmotes`, Volumetric Light Beam's dust component), from Meta's cost guidance, and
   from wind art‑direction talks. It is well supported but it is a synthesis, not a citation.
3. **I did not measure the current draught's real GPU cost on device.** The overdraw argument
   is from Meta's published guidance plus the authored particle counts, sizes and stretch
   factors read out of our bake — not from a RenderDoc/OVR capture. The *direction* (126
   large alpha quads × 2 eyes is the room's biggest transparent item) is certain; the
   millisecond figure is not stated because I do not have one.
4. **I could not confirm the exact geometry of fault (d).** I read the placement constants
   (`at = (-hw + 0.03, 0.85, PuddleAt.z - 1.05)`, card 1.24 × 1.24 m) and the record of the
   earlier fix, but proving what overlaps the stair opening needs the preview harness, which
   is the implementation lane's. What I *can* state as measured fact is the size: the prints
   are ~5.5× life size, and that alone will make any placement look wrong.
5. **No CC0 photograph of *wet blood on stone* was found**, so the three‑stop colour ramp in
   2.3 is derived — from the physics of optical depth plus the measured pixel statistics of
   the CC0 paint prints — and not sampled from a blood photograph. Treat the numbers as a
   starting point to tune on hardware, not as ground truth.
6. **StockSnap's licence (entry 5) was verified by the search sub‑lane, not by me.** Every
   other licence quoted in section 3 was fetched first‑hand. Confirm before use.
