# The img2img board-texture pipeline (ModBuild 272 rework)

The user rejected the ModBuild 271 boards: *"Die Controllboards sehen aus, als hätten sie keine
Textur … Es soll immersiv sein! Am Besten auch mit normal map etc."* and prescribed the method:
pass the existing texture map to `gpt-image-2` as the **initial frame**, let the model produce the
material, then repair the seams by hand.

This directory is that pipeline. It replaces nothing in `../tex_*.py` — those still build the
procedural bases and the symbol sheets, and they are what produced the maps this round replaces.

## Why the art was the defect, and how that was established

The complaint was measured against the hardware screenshot `.planning/debug/no_texture.jpg`
(steel board, night-forest scenario) rather than against a render:

* Six flat-metal patches spread over the board, located by a homography from the board's own
  silhouette quad, give `screenshot_linear / atlas_linear = 0.675 ± 0.051`. One global scalar
  fits every patch, which is what `BoardLit`'s `col = alb * shade` predicts for a flat face.
* The shipped steel atlas had **STORY 1.91** (CIELAB, 37 mm low-pass) and **chroma 1.79** over the
  face region: a 100-value grey band with no hue and no large-scale variation.
* `unity/board-prep/out/renders/steel_board.png` — a Blender/Principled render, NOT the shipped
  shader — overstates the board's relative contrast by **1.35x**. That is the station that let the
  atlas pass four rounds running.

**A correction worth keeping.** An early version of this analysis claimed the game shows the
albedo faithfully because `game CV / atlas CV = 1.02`. That is wrong: equal CV means *similar*
contrast, not the *same* contrast. Measured, the albedo accounts for at most ~50 % of the
screenshot's high-pass energy; the rest is relief (mesh plus normal map).

## The instrument: `tex_labstats.py`

Three numbers, all in CIELAB where a unit is roughly a just-noticeable difference:

| | what it is |
|---|---|
| `STORY` | mean CIELAB distance of a 37 mm low-pass from its own mean — *how much does this plate change across a hand's width, in any way the eye sees* |
| `GRAIN` | the same for what the low-pass removed — the micro-material |
| `huevar` | **std** of chroma, not its mean |

**The first version of this instrument was wrong and the mistake is instructive.** It measured
`STORY` on luminance only and reported 7.85 for a bronze plate that visibly runs from pale
blue-green verdigris to warm gold — that variation is almost entirely chromatic at nearly constant
lightness. An instrument that models one term of what the eye sees agrees with a picture that is
wrong in the other term.

The same mistake sat in the acceptance target: **mean chroma is the wrong statistic.** The shipped
oak scores 33.3 and the shipped bronze 34.8 *because* they are uniformly saturated, which is the
defect. A plate that ranges from bleached grey to gold scores lower and looks better. Mean chroma
falls for oak and bronze in this rework and that is the improvement, not a regression; `huevar` is
the number that moves the right way.

## The chain

    gen_initframe.py   Blender: flat-on ORTHOGRAPHIC render, AMBIENT LIGHT ONLY.
                       Ambient only because this becomes the init frame for an img2img pass whose
                       output becomes an ALBEDO -- a baked directional key would be lit a second
                       time by BoardLit.
    gen_initpad.py     level-stretch (gain capped at 2.2) and pad to 3:2.
                       3:2 because the tool's aspect enum has no 2:1 and it silently RESCALES a
                       16:9 frame into 3:2 -- the first steel generation came back at 1.809:1,
                       which is exactly 16/9 divided by 3/2.
    <gpt-image-2>      referenceImages = the padded init frame; prompts in ../../.planning/.
    gen_uvpass.py      Blender: u, v and coverage as SEPARATE 16-bit GREYSCALE PNGs.
                       Separate because PIL silently downconverts a 16-bit RGBA PNG to 8 bits
                       with no error -- Blender reported depth 16 and the array came back uint8,
                       and 8-bit UV is 8 texels of quantisation at a 2048 atlas.
    gen_aopass.py      Blender: WHITE albedo + the shipped normal map under a uniform white world.
                       A true cavity/relief pass. The earlier ambient/albedo divide left
                       0.35-0.62 residual correlation with the old albedo's own streak noise,
                       i.e. it smuggled the defect into the term meant to replace it.
    tex_composite.py   board art + mesh relief -> atlas.
    tex_maps.py        new albedo -> normal + MRS.

## The composite, and why it is not a straight drop-in

`gpt-image-2` preserved the macro layout but **redrew every feature outline 3–9 mm off the mesh's
real geometry and at different radii**. That was established with a block matcher validated
against planted shifts (it recovers a known shift exactly, and found no consistent transform that
registers the generated features onto the mesh ones). The recesses, mouldings, glyphs and seat
rings are real geometry, so a painted edge that misses its geometric edge shows as a doubled edge.

Only the outlines are misregistered. The material — tarnish, grime, grain, pitting, colour — is
spatially unstructured and registers with nothing. So:

    weight   = where the MESH has a feature, from the gradient of a SMOOTHED AO pass
    flat     = LOW + HIGH of the generated art, dropping only the mid band
    material = lerp(generated, flat, weight)
    albedo   = material * AO^gamma

Two details that were got wrong first and are worth not repeating:

* The gradient must be taken on a **smoothed** AO. Raw, it fires on the normal map's brush lines
  and put 45 % of the board above weight 0.5, which would have flattened the material across half
  the plate.
* `flat` must be **LOW + HIGH**, not a plain box blur. A plain blur takes the misplaced outlines
  away and the grain and pitting with them; the rosettes and seat rings came out looking moulded
  in plastic.

**The layout cannot drift**, and not because it was checked afterwards: the art is scattered into
the atlas through the mesh's own UV pass, so every texel receives the colour of the board point
that actually samples it. The mesh, its UVs, the anchors and the seat measurements are untouched.

Board space is rendered at **4096x2048**. At 2048x1024 only 17.6 % of the atlas is reachable —
the atlas is oversampled relative to the board's projected area, and the `rest_pads` region alone
wants 512x512 texels for two discs ~180 px across. Texels no front view can reach (recess walls,
board edges, the back) are filled by push-pull dilation from their neighbours, which is correct:
the wall of a recess is the same metal as its floor.

## Normal and MRS — `tex_maps.py`

`_MRSMap` is **R = metallic, G = roughness**, NOT glTF ORM; the pack is linear.

The user asked for the normal map to be derived from the new art. It is — but only the **material**
band. The feature relief stays with the shipped, mesh-registered map, because the new art's
feature structure is the part that is millimetres off. An ornament mask separates the shipped map's
carving (sparse, locally concentrated) from its grain (dense, near-uniform) by spatial statistics
rather than frequency, after a plain low-pass was found to throw away 3–4 texel rosette spokes and
come back visibly *softer* than the map it replaced.

**The shipped normal maps are RED-INVERTED** relative to `UnpackNormal` — `tex_common.normal_from_height`
writes `nx = +dh/du` where the convention wants `-dh/du`; green is correct. `tex_maps.py` matches
the shipped convention on purpose: mixing conventions would light the grain from the opposite side
to the mouldings on the same board. `--x-convention opengl` exists for when this is fixed at source.

## Standing limits, measured not assumed

* **The specular is a bevel term, not a surface term.** With the baked key and a viewer in front of
  the board the half-vector sits ~32° off a flat face, so the open plate gets essentially no
  highlight. The verdigris/oxide roughness split reads on mouldings and recess walls and much less
  on the field. No amount of roughness contrast buys past that; it would take a shader change (a
  second key nearer the view axis, or a view-dependent term).
* **`BoardLit.shader`'s ModBuild-248 comment is stale.** It says both control boards do not opt into
  specular; all three board materials now set `_SpecStrength: 0.85` and bind a real `_MRSMap`.
* **`_Cull: 0` on all three board materials**, though the shader's default and comment say Back(2)
  for the board and Off(0) only for the fragmented hand glove.
* The station in the round's scratch predicts **albedo only**. It is blind to `_normal` and `_mrs`
  changes, and the two card slots are green in the screenshot because of a game-state tint that the
  measured shade field absorbs — they stay green whatever atlas is passed.

---

# ROUND 3 (ModBuild 275): THE BACK AND THE RIM

Round 2 shipped and the striping complaint that opened this round was **withdrawn** after a
hardware check: *"Ich hab es nun im Spiel geprüft und da sehe ich diese Streifen nicht! Es
war also ein Renderfehler vom Vergleichsbild."* The picture that showed the stripes,
`boards_shader_rake.png`, is lit from a deliberately RAKING angle to make relief legible,
and raking light is exactly the condition that maximises directional relief. **The
anisotropy is real in the data and it does not read where the player stands.**
`tex_aniso.py` keeps measuring it as a regression guard and deliberately enforces nothing —
see its docstring for the four ways earlier drafts of that test were wrong.

What he reported instead: *"Die Seiten und die Rückseite die Textur ist kaputt … Auch dort
soll eine entsprechende Textur sein. Generier dir auch dafür etwas, nicht nur für die
Vorderseite."*

## The UV finding — and the premise it falsified

The obvious reading of the screenshot is a broken unwrap: a flat grey back and a smeared
edge look exactly like faces sampling a degenerate sliver. **They are not.** Rasterising
each mesh's own UV islands into its atlas, per face group:

| board | group | tris | surface | atlas coverage | texel density | overlaps |
|---|---|---|---|---|---|---|
| oak | FRONT | 6500 | 2015.2 cm² (41.8 %) | 18.96 % | 1.99 tex/mm | 0 |
| oak | BACK | 108 | 2010.6 cm² (41.7 %) | 0.97 % | **0.45 tex/mm** | 0 |
| oak | RIM | 2012 | 581.2 cm² (12.1 %) | 3.33 % | 1.53 tex/mm | 0 |
| steel | FRONT | 5392 | 2059.2 cm² | 17.53 % | 1.89 tex/mm | 0 |
| steel | BACK | 304 | 2060.8 cm² | 1.10 % | **0.47 tex/mm** | 0 |
| steel | RIM | 1544 | 546.1 cm² | 2.90 % | 1.49 tex/mm | 0 |
| bronze | FRONT | 10476 | 1888.5 cm² | 18.92 % | 2.04 tex/mm | 0 |
| bronze | BACK | 124 | 2006.7 cm² | 1.02 % | **0.46 tex/mm** | 0 |
| bronze | RIM | 2092 | 540.8 cm² | 3.82 % | 1.72 tex/mm | 0 |

**Zero overlapped texels on any board in any group**, and the RIM's density is 77–84 % of
the front's — not a sliver, and nothing a repack would improve. So the rim was never
mis-unwrapped; it was never **painted**. The back was both: its island is real, and at
0.45 tex/mm it carried ~42 % of the board's surface on ~1 % of the atlas.

**A UV rectangle is not a surface, and neither is a UV island.** These numbers are triangle
coverage rasterised at atlas resolution, not island extents.

## Why nothing was painted there

`tex_composite` renders the board flat-on, generates art at that view and scatters it back
through a UV pass taken from **the same view**. Everything a front camera cannot reach was
then left to `pushpull_fill`. That is right for a recess wall — one or two texels from its
own floor, and made of the same metal — and wrong for a back plate hundreds of texels from
the nearest authored texel, where the fill converges to a flat average. Measured in the
shipped 274 atlas, per island, against that style's own FRONT:

| board | BACK relief (\|slope\| mean) | BACK albedo high-pass rms |
|---|---|---|
| oak | 26.0 % | 34.2 % |
| steel | **6.7 %** | 45.0 % |
| bronze | 11.7 % | 36.5 % |

6.7 % of the front's relief is not "a little soft". It is the flat grey slab in the
screenshot.

## The chain

    tex_uv_dump.py    (existing) triangulated UVs + object-space corners + face normals
    gen_geobuf.py     rasterise the mesh into ATLAS SPACE: pos.npy, nrm.npy, grp.npy.
                      Every texel carries the object-space POSITION and NORMAL of the
                      surface point that samples it, so art placed through it CANNOT DRIFT
                      -- it is addressed by where the surface is, not by where a picture
                      thinks it is. No camera is involved, which is the point: no camera
                      reaches these faces.
    gen_backinit.py   the back plate's init frame -- the mesh's own silhouette, the style's
                      own median FRONT plate colour, 2:1, padded to 3:2.
    <gpt-image-2>      one image per style. referenceImages = [back init frame, the
                      composited FRONT board-space albedo], so the back cannot come back a
                      different material from the front of the same object.
    tex_backfill.py   BACK <- the generated plate at each texel's own board coordinates.
                      RIM  <- a smoothstep blend from the FRONT material to the BACK
                              material across the board's own thickness, which is what a
                              rim physically IS. Both edges then match their neighbour by
                              construction, and there is no fill direction to smear along.
                      INTERIOR untouched: it already measures 90-127 % of the front's
                              high-pass energy, because there push-pull travels two texels
                              and does the physically correct thing.

## Two things worth not repeating

* **The back's relief may be derived from its own art, and the front's may not.** The front
  face is covered in mesh geometry the model redrew 3–9 mm off, so a normal map taken from
  that art embosses a second, displaced copy of every feature. The back plate is
  geometrically FLAT: the plank seams and rivets *are* the relief, and there is no
  mesh-registered version of them to double.
* **A colour round trip is not a no-op.** The first version of `tex_backfill` converted the
  whole atlas to linear and back to write two regions, and the round trip alone moved 1029
  FRONT, 570 INTERIOR and 12680 unmapped texels by ±1. Invisible — and still a lie in any
  diff that claims the front face is untouched. It now keeps the uint8 array whole and
  assigns only BACK and RIM texels; FRONT, INTERIOR and unmapped come out byte-identical on
  all three maps, and that is checked rather than asserted.

## The 3:2 trap, and what it cost this time

The tool's aspect enum still has no 2:1. Oak and bronze came back inside their padded
frames at 1.839:1 and 1.813:1 and are resampled to 2:1 — a 1.09× stretch. **Steel ignored
the pad entirely** and filled the whole 3:2 frame, so its resample is 1.324×: its ~8 mm
dome rivets ship as ~11 × 8 mm ellipses. That is a real, measured distortion, and it is
accepted rather than regenerated. It is a back face, and a blind regeneration on a nudged
prompt is not how this pipeline spends images.

## The station now points at the defect

`PreviewBoard` gained `_back`, `_backrake`, `_edge` and `_corner`; `gen_render.py` gained
`back`, `backquarter` and `edge` modes. A station that points only at the front cannot show
a defect on the faces it does not point at, and this one shipped — the first person to see
it was the user, from behind, in a dark forest. `gen_render.py` is still ~2 stops
overexposed and is an iteration loop, not a verdict; `PreviewBoard.cs` is the calibrated
station.

## THE GENERATED BACK PLATES — the money is spent, do not spend more

    unity/board-prep/out/board_back_oak.png
    unity/board-prep/out/board_back_steel.png
    unity/board-prep/out/board_back_bronze.png

Three images, one per style, all accepted first time. `out/` is gitignored, so they are NOT
in any diff — if they are ever lost, the prompts are in the commit that added
`tex_backfill.py` and reproducing them costs three more generations. Nothing here was
regenerated on a hunch, and the one measured flaw (steel's 1.324x rivet stretch) was
accepted rather than re-rolled.

## The back island was repacked, and the trap that nearly shipped with it

`../gen_backuv.py` rewrites **only** the back plate's UV loops into a free rectangle of the
atlas. The back plate is exactly one whole UV island on all three boards, so moving it
creates no seam and destroys none; the script asserts that and aborts rather than shear a
shared island.

| board | before | after | gain |
|---|---|---|---|
| oak | 0.450 tex/mm, 0.97 % of atlas | **1.213 tex/mm, 7.06 %** | 2.69× linear |
| steel | 0.419 tex/mm, 0.84 % | **1.246 tex/mm, 7.45 %** | 2.97× linear |
| bronze | 0.462 tex/mm, 1.02 % | **1.186 tex/mm, 6.73 %** | 2.57× linear |

1.6 tex/mm — front parity — is **not reachable** and was not faked. An exact free-rectangle
search at an 8-texel gutter finds no 1024×512 hole in any of the three atlases: the unused
74–78 % is fragmented, and the wide holes are shallow (landscape placement scores
0.84–0.94 tex/mm, worse than portrait). Front parity needs a full repack of front + rim,
which is not worth moving locked UVs for.

### A BLENDER-LEVEL BIT-IDENTITY PROOF CANNOT SEE A UNIT-SCALE CHANGE

The first version of the repack exported with `apply_unit_scale=True`. Every Blender-side
check passed — vertices, loop indices, polygon sizes, split normals, UVs outside the back
face, anchor `matrix_world`, island overlap. The harness said ALL PASS.

It was wrong, and Unity said so. The shipped FBXes carry `UnitScaleFactor = 100`; that
export writes `1.0`. Blender normalises the unit on import, so it reads both files back
identically and **no Blender-side comparison can distinguish them**. Unity, with the
unchanged `.meta`, re-imported at a different scale, and `BoardBuilder` rewrote every
anchor override in all three prefabs by a factor of 100 — `-0.23360015` became
`-0.0023359999` — plus a quaternion sign flip from the changed transform decomposition.
That is precisely what a UV-only edit must never do.

The fix is `apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS'`, which leaves
the unit scale where it started. Verified after the fix:

* `UnitScaleFactor` 100 → 100 on all three;
* rebuilt prefabs: the **same 13 modification targets**, and the worst numeric change
  anywhere is **2.0 × 10⁻⁷ m — 0.2 micrometres**, the float32 quantisation of a 0.22 m
  coordinate. Six oak overrides disappear only because they now equal the model's own
  value exactly, so Unity stops storing them;
* `BoardBuilder` re-measures the locked table exactly: seat floors 74.6 × 64.3 / 81.0 ×
  70.1 / 61.2 × 51.9 mm, rest pads 81.6 / 81.7 / 68.1 mm, 7 of 7 anchors, one MeshCollider,
  bounds 0.640 × 0.320;
* `PreviewBoard` run over the built bundle before and after the repack: **every anchor,
  every extent, every bound, every collider count and every bound texture identical**, the
  only differences being single-pixel anti-aliasing counts in the stereo statistics.

**The lesson generalises past FBX: a round-trip proof taken inside one tool cannot see what
that tool normalises on the way in.** What caught it was rebuilding the artefact the OTHER
tool produces and diffing that.

`gen_uvdiff.py` check 8 now reads `UnitScaleFactor` **from the file bytes**, deliberately
not through Blender — a check that goes through the same importer as the thing it is
checking cannot see what that importer normalises. Its positive control is the FBX that
caused this: checks 1–7 all report PASS on it and only check 8 fires.


---

# ROUND 4 (ModBuild 278): THE BACK'S RELIEF BECOMES GEOMETRY

Round 3 shipped and he accepted the art — *"Die Rückseite der boards gefällt mir sehr
gut!"* — and then named what was still wrong with it:

> "Allerdings: Auf den Texturen sind Schrauben und Halzplatten etc zu sehen, also
> eigentlich 3-dimensionale Objekte. Sie werden aber flach nur auf der Textur dargstellt.
> **Ich möchte, dass du das Mesh für die Seiten und Rückseite an die Textur anpasst**, so
> wie du es auch für die Vorderseite bereits sehr erfolgreich gemacht hast."

He is right and the pipeline's own record says so: round 3 deliberately derived the back's
relief **from its own art** (`tex_backfill`, high-pass at 4 texels, p99 tilt 18°) because
the back plate was geometrically flat and there was nothing to double. A normal map has no
silhouette, no occlusion and no stereo parallax, and in a headset at 40 cm that is exactly
what "flach" looks like.

## The registration problem does not exist here, and that is a measurement

The front's whole difficulty was that `gpt-image-2` drew its features 3–9 mm off the mesh.
On the back there is no such problem, for a structural reason that was checked rather than
assumed. `tex_backfill` places the back art at each texel's own **board coordinates**:

    x = (u - 0.5) * LONG        y = (0.5 - v) * SHORT

with (u, v) normalised inside `plate_rect(art)` resampled to 2048 × 1024. Re-running that
assignment reproduces the shipped atlas's BACK texels **exactly** — mean |err| **0.00 of
255**, r = **+1.0000** — while a planted +3 % shift in u gives 17.30/255 at r = +0.18 and a
flipped v gives 16.35/255 at r = +0.24. Both controls fire, so the null is a measurement.

So a feature measured in PLATE PIXELS is built in metres with no fitting step at all. The
route taken is therefore neither of the two the brief offered: not "extract positions and
then register", and not "place parametrically and re-scatter the art" — **the art's own
placement map is an exact affine in board coordinates, so measuring the art and building
there IS the registration.** `gen_board.BACK_ART` carries every figure, at 1 px = 0.3125 mm.

The dome detector for the nails and rivets is a vertical derivative-of-Gaussian matched
filter with a lobe-midpoint centre estimator, validated on planted domes (5 of 5, residual
**1.73 ± 0.10 px**) against a NULL that returns nothing. 1.73 px is 0.54 mm, which is 0.65
atlas texels at the back's 1.21 tex/mm — under the resolution of the map the art lives in.

**Its first version fired 12 times on the NULL and found one of five planted domes**, and
both causes were in the instrument: it took its Gaussian through a uint8 PIL buffer, so its
own quantisation floor sat above the signal, and it used `np.roll`, so the wrap at row 0
produced the largest response in the picture.

## What became geometry, and what deliberately did not

| feature | ships as | why |
|---|---|---|
| oak iron straps, 42.5 × 310 mm, 2.0 mm proud | **geometry** | real silhouette, occlusion and parallax |
| oak forged nails, 12.5 mm square, 1.0 mm | **geometry** | bevels, and the specular here is a BEVEL term |
| oak plank seams, 3 of them | **normal map** | see below |
| steel recessed fields, 3, 1.5 mm deep | **geometry** | makes the border band and both straps real |
| steel dome rivets, 76, 8.4 × 6.6 mm, 1.3 mm | **geometry** | the "Schrauben" he named |
| bronze cast fields, 3, 1.5 mm deep | **geometry** | makes the stiffening ribs real |

**The oak plank seams are the one feature where the map is the better instrument, and the
number says so.** The art paints them 1.6 mm wide. A wall no steeper than 40° — the limit
below — can then carry at most 0.8 × tan 40° = **0.67 mm** of depth, and the two walls meet
in a V with no floor. Widening the slot enough to carry real depth would put a 3 mm groove
where the art paints 1.6 mm: a doubled edge by construction. A groove also has no
silhouette and no occlusion, so both things geometry buys over a map are absent.

## EVERY BACK WALL IS A STRAIGHT CHAMFER OF AT MOST 40°, and neither reason is taste

1. `gen_geobuf` calls a triangle BACK when its normal is within acos(0.7) = **45.57°** of
   the thickness axis. A steeper wall lands in INTERIOR, which `tex_backfill` never paints
   and `pushpull_fill` would smear.
2. The whole back is ONE island under ONE top-down projection. A vertical wall has **zero
   area** under that projection — a degenerate UV island, which no atlas can feed.

`fillet()` is the wrong profile here although it is the right one on the front: it is a
quarter ellipse starting at the pole, so its FIRST step is nearly vertical (**69.7°** at
n = 3, w = 2.2 mm, d = 1.6 mm). A straight chamfer has one slope by construction and
`back_stack()` asserts it. Measured on the built meshes, the BACK group's tilt distribution
is oak p99 40.6° / max 40.6°, bronze 32.5 / 32.5, steel p90 41.7 with 204 triangles above
44° — **and those 204 are the rim CHAMFER the round-3 record already names**, not new
relief. INTERIOR is 3276 / 2728 / 6888 triangles before and after on all three: nothing
leaked out of the BACK group.

## THE ATLAS IS NOT REPACKED, and that was designed in rather than discovered

Every back face — plate, chamfer walls and caps — shares the back's single top-down
projection, so the back island's RAW bounding box is unchanged, `gen_board`'s per-category
shelf packer sees the same input for every region, and `gen_backuv`'s free-rectangle search
sees the same coverage from everything except the back. All three land on **the identical
rectangle the shipped boards already use** (oak 384 × 772 at y[1191:1963] x[1094:1478];
steel 394 × 793; bronze 375 × 755).

Rasterising every NON-BACK UV triangle of the shipped and the new mesh into a 2048² map and
diffing:

| board | non-back tris | non-back texels | **differing texels** | control (+1 texel) |
|---|---|---|---|---|
| oak | 11788 → 11788 | 1010135 → 1010135 | **0** | 20458 |
| steel | 9664 → 9664 | 930519 → 930519 | **0** | 21314 |
| bronze | 19456 → 19456 | 1102983 → 1102983 | **0** | 20616 |

Back density goes 1.213 → 1.210, 1.246 → 1.239, 1.186 → 1.182 tex/mm — the tiny drop is the
added wall SURFACE in the denominator, not a smaller rectangle. **Moving the features out of
the map and into the mesh lowers what the back's texel density has to carry**, so nothing
was bought by raising it and no repack was needed. `Island.__doc__`'s "this face is never
seen" is still a lie and still harmless: `gen_backuv` overrides that weight, and the
comment at the call site now says so.

## The normal map had to give the relief back — `tex_backrelief.py`

Leaving both in place is not "more relief". The painted feature is a dark LINE at a strap's
edge, so its high-pass is a **groove**; the mesh at the same place is a chamfer **ramping
up**. They disagree in sign at a separation of one or two texels. The feature band is
therefore removed from the back's normal map, with the mask taken **from the mesh's own
normals** (`1 - |n · n_back|`, dilated) and never from the picture.

| | back texels | mask | slope in band before → after | slope outside | outside the mask |
|---|---|---|---|---|---|
| oak | 296136 | 14.9 % | 0.1132 → 0.0321 | 0.1281 → 0.1260 | **0 texels changed** |
| steel | 322922 | 32.6 % | 0.1188 → 0.0327 | 0.0776 → 0.0742 | **0** |
| bronze | 282173 | 25.3 % | 0.1490 → 0.0467 | 0.1240 → 0.1201 | **0** |

The edit is written as the DIFFERENCE of two slope fields computed from the same source, so
outside the mask it is identically zero and those texels are byte-identical **by
construction** rather than by hope — the same discipline the round-3 colour round trip
earned. **The ALBEDO is not touched at all**: the paint is already registered to the new
geometry to better than one texel, and at flat-on viewing it is the only thing that draws
the feature, because the half-vector sits ~32° off a flat face.

## The sides: measured, a fix built, and DELIBERATELY NOT APPLIED

`tex_backfill` builds a rim texel from `lerp(front(p - n·t·W), back(p - n·(1-t)·W),
smoothstep(t))` with W the board's own thickness. W = 35.6 mm on steel reaches past the
rivet row at 8.9 mm, so the rim paints the back's screws down the side of the board. Share
of rim texels whose back-sample lands on a back feature, weighted by the blend: oak 2.5 %,
**steel 15.5 %**, bronze 2.0 %.

`tex_rimfill.py` corrects it with one parameter — a scale on the back term's walk — written
as a difference so the front term cancels and k = 1 reproduces the input exactly. At
k = 0.20 steel goes **15.5 % → 4.2 %**, 109 518 of 121 816 rim texels rewritten, nothing
outside the rim moved.

**And the picture barely changes** (`.planning/debug/board278/rim_steel_compare.png`). So it
was not applied. Two things came out of chasing it:

* The prominent three-dimensional objects on the steel side are **real** — the band across
  the top of an edge shot is the FRONT frame's studded border seen edge-on, mesh geometry
  that was always correct. What the walk ghosts is far fainter than the statistic implies.
* Splitting the statistic by TERM killed the obvious follow-up. "It must be the FRONT term
  then" is wrong where it matters: weighted by the blend, steel is FRONT 10.1 % against
  BACK 15.5 %, so the term the fix already corrects is the dominant one. (oak 4.1 / 2.5;
  bronze 23.5 / 2.0.)

Changing an accepted texture to move a number the player cannot see is not an improvement.

## The ghost ratio, and why the round-2 number is not the right one here

The round-2 harness was never committed and its scale cannot be reproduced from the record.
It also would not answer this question: on the BACK a HIGH ghost ratio is the GOOD outcome,
because the geometry was built at the art's own measured positions and paint and mesh are
SUPPOSED to share their edges. `tex_backghost.py` therefore reports the PROFILE — albedo-edge
density against distance from the nearest geometric wall — and a decisive second statistic:
the offset at which the mesh's own height field best correlates with the albedo.

    board  registration (u, v)   r      control: planted +8 texels recovered as
    oak    ( 0, +2)              0.065  (-8, +2)
    steel  ( 0, -3)              0.112  (-8, -3)
    bronze (+9, -12)             0.058  (+1, -12)     <- argmax on the search boundary

u = 0 exactly on oak and steel with the control recovering the planted shift to the texel.
**Bronze is inconclusive and the instrument says so**: its argmax runs into the ±12 search
boundary, and so does the FRONT face's, which this round does not touch. So the picture was
looked at instead — `registration_{oak,steel,bronze}.png` draws the mesh's wall band over
the atlas's own back albedo, and on all three the red band traces the painted feature
exactly: every oak nail inside its square, every steel rivet ringed, every bronze rib's
gold crest just outside the bevel where a crest belongs.

The ghost ratio itself: measured 1.542 / 4.594 / 1.271 against a +8-texel control at
1.400 / 4.266 / 1.156 and a 43-texel null at 1.210 / 3.051 / 1.266, peaking at distance 0
on oak and steel. The null does not reach 1.0 because a feature region simply carries more
texture than an open plate — a confound a single ratio cannot remove, which is why the
correlation and the picture are the load-bearing evidence and the ratio is continuity only.

**Its first null control was a bad one**: a random mask of the same texel count leaves a
mean spacing of 7 texels, so no texel is ever far from one, the far buckets emptied and the
null "fired" at 1.325 with a peak at 13. A null control has to preserve the geometry of the
thing it nulls.

## Triangles, and what they cost

| board | before | after | budget | back thickness | dims |
|---|---|---|---|---|---|
| oak | 11896 | **12976** (+9.1 %) | 24000 | 3.0 mm proud | 0.0356 → 0.0386 |
| steel | 9968 | **15716** (+57.7 %) | 24000 | 1.6 mm proud | 0.0343 → 0.0356 |
| bronze | 19580 | **20660** (+5.5 %) | 24000 | recessed only | 0.0354 unchanged |

Steel pays for 76 rivets and it is the right place to spend: they are the feature he named.
All three keep 0 boundary edges, 0 hole loops, 0 non-manifold edges, 0 loose verts and
0 inward-wound faces; signed volumes 5211.4 / 4642.8 / 5289.9 cm³, all positive.

**Oak's bbox is now 38.6 mm against the contract's 40 mm ceiling** — 1.4 mm of headroom, and
the next feature that stands proud of the oak back has to buy its height from the straps.

## Two failures worth not repeating

* **The steel back was modelled wrong first, and the MESH reported it, not the eye.** The
  first version made the two vertical straps separate raised bars on the floor of one big
  recess. A bar inset 3 px from the recess OUTLINE still crosses the recess FLOOR, which
  sits a further `panel_run` = 2.0 mm in, so its footprint was not a hole in the surface it
  was welded into: 10 boundary edges, 2 hole loops, 2 non-manifold edges. Three separate
  recessed fields is also the better reading of the art — the straps have the band's colour
  and the band's rivets because they ARE the band.
* **A clamp that never fired.** A `push_clear` was written for four steel corner rivets
  whose bases looked like they crossed the panel outline in x. They do — and they are ABOVE
  the panel in y, so there was never an overlap, and the clamp reported 0 moves while the
  real defect was somewhere else entirely. It ships anyway, because it is cheap and the next
  art might need it, and because a clamp that PRINTS every move it makes is honest where a
  silent one would be a lie about where the art is.

## The unit-scale trap, checked again with its positive control

`gen_uvdiff` check 8 reads `UnitScaleFactor` from the FILE BYTES. All three new FBXes:
100.0 → 100.0. The positive control is an FBX re-exported with `apply_unit_scale=True`:
checks **1, 2, 3a, 3b, 3c, 4, 6, 6b all PASS and only 8 fires**, exactly as recorded. (Check
5 is vacuous on this control — it expects a back-face UV edit and a pure re-export makes
none.)

And the proof was taken the other way round as well, by rebuilding what the OTHER tool
produces: `BoardBuilder` re-measures the locked table unchanged (seat floors 74.6 × 64.3 /
81.0 × 70.1 / 61.2 × 51.9 mm, rest pads 81.6 / 81.7 / 68.1 mm, 7 of 7 anchors, one
MeshCollider, bounds 0.640 × 0.320) and **the three prefabs come out byte-identical** — not
one anchor override moved. `gen_uvdiff` shipped-vs-new confirms it from the other side:
checks 3a and 3b PASS on all three boards, 0 of 10 empties differ, worst |d| = 0.000e+00 m.

## `gen_backuv`'s interlock moved from the seed to the result, and that STRENGTHENED it

It used to require that the −Y-extreme polygons are exactly one whole UV island, which was
only ever true because the back was flat. On these boards the seed is the nail / rivet caps
(40 / 380 / 173 polys) and it grows, correctly, to the whole back island (635 / 3240 / 632).
What the repack actually needs is that the moved set is a COMPLETE island and that it is all
back geometry; both are now asserted on the grown set, and the second was not checked at all
before. `gen_uvdiff`'s own back-face finder had the same flat-back assumption and was
reporting oak's back plate as 480 × 263 mm of a 636 × 316 mm face; it now agrees.

## The pictures

All in `.planning/debug/board278/` (gitignored, local):

    board278_back_rake.png      all three, back, before | after, raking light 11 deg
    board278_back_flat.png      all three, back, before | after, flat-on 55 deg
    registration_{style}.png    the mesh's wall band drawn over the atlas's back albedo
    shader/{style}_backrake.png THE CALIBRATED ONE -- the real prefabs through the real
    shader/{style}_back.png     BoardLit, from the rebuilt bundle, via PreviewBoard
    rim_steel_compare.png       the rim fix that was measured and not applied
    edge_steel_{after,rimfix}.png

`gen_backshot.py` is the new A/B station and it is an ITERATION LOOP, not a verdict: one sun
at a stated elevation above the back plane, because `gen_render.py`'s three lights are placed
relative to the board's FRONT axis and its `back` mode lights the plate with a single 90-energy
fill. Relief is a directional effect — under a light that does not rake it, geometry and a
normal map and a flat plate look the same. The calibrated picture is still `PreviewBoard.cs`
against the built bundle, and this round used both.
