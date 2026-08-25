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
