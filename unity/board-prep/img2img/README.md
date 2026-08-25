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
