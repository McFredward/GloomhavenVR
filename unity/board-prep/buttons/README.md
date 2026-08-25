# The keycap pipeline — symbols, per-board cap materials, and the instrument that judges them

This directory builds the three **per-board keycap atlases** the mod's control-board buttons wear:

    unity/GloomhavenVR.Assets/Assets/Bundle/Table/Keycap{Oak,Steel,Bronze}_{albedo,normal}.png

Read `../img2img/README.md` and `../../../.planning/BOARD-REBUILD-HANDOVER.md` first — the traps
recorded there are inherited wholesale, and two of them bite here.

## What the round was for

> "Statt einfach nur Text, möchte ich ein Symbol (und Text dazu) … Pro Board soll es auch ein
> anderes passendes Aussehen der buttons sein, das zu dem board und seinem Aussehen passt. Nutze
> hierbei auch die Hilfe von gpt-image-2 um dafür passende Texturen zu finden."
> — user, 2026-08-25

Before this round every keycap on all three boards shared ONE material, `KeycapGrain_albedo.png`,
and carried text and nothing else.

## The chain

    gen_capref.py    crop each style's OWN board albedo face band to 1024^2 -> ref_face_<style>.png
                     (a palette/grain REFERENCE for the generation prompt; never composited)
    cap_object.py --init   render the GEOMETRY the model is shown: a square signet plate and a
                     round one, bands at their exact fractions, shaded by a dot product against
                     BoardLit's own baked key, on a flat backdrop with margin
    <gpt-image-2>    1 symbol sheet (3x3, 1:1, round 1) + 4 OBJECT plates (3:2, round 3; each
                     carries both shapes, each sent with its init frame and its ref_face).
                     The asks are VERBATIM in cap_object.PROMPTS.
    cap_object.py --ingest  segment each plate against the backdrop, cache the crops
    cap_atlas.py     register the crop onto the mesh's own bands -> carve the stencil in through
                     an exact distance transform. Writes the six PNGs and their .meta files.
                     `--no-objects` rebuilds from round 2's material swatches: the A/B control.
                     **THE SHIPPED ATLAS IS THE DEFAULT (`objects=True`) PATH, from round 3's
                     `keycap3_object_<style>.png`.** Round 6 re-ran this file with no arguments
                     and got all six PNGs back BYTE-IDENTICAL (md5), which settles it: the
                     round-4 note's claim that the atlas was rebuilt `--no-objects` from
                     `keycap4_material_<style>.png` is WRONG, and those four generated images in
                     `out/` are read by nothing. Either wire them in or delete them -- a
                     generated asset the docs say is used and no code reads is how round 3's
                     random crop survived two rounds.
    cap_check.py     the acceptance instrument, with a null input and a known-positive control.
    cap_rough.py     ROUND 6 -- how much rougher than the board does the cap read? See below.
    cap_onboard.py   the caps in their own seats on their own board. `--capnormal` binds the cap
                     _BumpMap (round 6); `--restshot` frames the two ROUND rest discs, which no
                     on-board picture contained at all before round 6.
    cap_sheet_rough.py  round 6's three before/after sheets.
    plate_forensics.py  what separates the ACCEPTED board backs from the REJECTED cap plates
    cap_round3.py    the three sheets that judge the round
    cap_sheets.py    contact sheets from the Unity render station's output (PreviewKeycaps.cs).
    engrave_preview.py  what the BOARD ENGRAVING looks like -- see below.

## ROUND 3: the cell is a REGISTERED PICTURE OF THE BUTTON, not a crop of a material

Two rounds of material were rejected with the same word, "einheitlich", and the second of them
had measured a real improvement first. `plate_forensics.py` says why, against the one thing from
this generator the user volunteered praise for (the board BACKS, ModBuild 276):

    REG -- mean luminance against distance-to-border, peak-to-trough, per cent of the mean
        accepted board backs         89.8 / 57.3 / 106.6
        round-2 plates as generated  18.0 / 14.6 /  30.9
        a STATIONARY NOISE SWATCH    17.4 +- 3.9      <- the null control
        the SHIPPED atlas cells       3.7 /  9.9 /   3.8

The shipped cap face carried LESS border structure than random noise. Two causes, and the second
one was in this directory: **`material_cell` took a random crop at a random offset**, which
ground whatever registration a plate had back down to a swatch (REG 18.0 -> 3.7 on oak).

**One atlas cell IS the whole button.** The meshes UV planar over their own footprint, so cell
(0,0)..(1,1) is the cap's outline and the signet profile's bands have exact known positions in
it -- 0.060 / 0.105 / 0.135 of the cap's SHORT side, which is 0.1206 / 0.1331 / 0.1114 of the
cell's *u* on oak / steel / bronze against 0.135 of its *v*. `cap_object.register_square` imposes
those positions on the art with a band-preserving gather, so the rim is on the rim land whatever
the model drew.

**And the submesh split is not the obvious one.** Submesh [0] (the recessed FIELD) takes the ROLE
cell; submeshes [1] (bezel) and [2] (walls) take `CapRole.Plain` -- cell 0 -- on EVERY cap. So a
role cell is only sampled at d > 0.135 and the plain cell only at d < 0.135, and cell 0 gets its
own "bezel" build. One cell cannot serve both shapes' bands (registering it on
`min(d_square, d_round)` breaks ~74 % of the square cap's rim land; on `max`, ~38 % of the round
cap's bezel), so it is exact for the square cap and fills its unused interior with rim-land
material for the round cap's benefit. The cost is stated in
`.planning/BOARD-BUTTON-OVERHAUL.md`.

**ROUND 6 CLOSED THAT, AND THE USER FOUND IT FIRST.** Every word above is true and the conclusion
drawn from it was not: one cell cannot serve both shapes, and the atlas has SEVEN SPARE CELLS, so
it never had to be one cell. Filling a round cap's bezel with square rim-land material paints a
square gold band with mitred corners inside a circular button, which is exactly what he
photographed in `viereckige_texturen.jpg` -- *"als sei hier eine Textur fuer eigentlich einen
viereckigen Button genutzt worden"*. **Cell `ROUND_BEZEL_CELL` = 9 = `Cards.CapRole.PlainRound`**
is the round sibling of cell 0, and a ROUND cap's bezel and wall submeshes take it. It needs no
angular sweep: a round cell's distance-to-outline IS its radius, so the unread interior is simply
faded to the rim band's own mean, with no diagonal for a seam to run along.

A cost written down as a cost is still a defect. This one sat in this file for three rounds
describing precisely the thing the user would eventually report.

## ROUND 6: ROUGHNESS -- the one thing none of these instruments measured

`cap_rough.py`. The user, on caps he otherwise likes: *"ABER alle Buttons sind so extrem rau, dass
es schon fast wie Noise erscheint."* Five instruments were already in this directory and **not one
of them measures roughness**: `cap_check` measures symbol contrast, `cap_deltae`/`cap_belong`
measure colour, `plate_forensics`/`cap_regmove` measure whether a picture has an inside-outside
order. A cap made of sandpaper and a cap made of glass score identically on all five.

The quantity, from his own relational bar -- the cap must not read rougher than the board it sits
on -- is `100 * RMS(band-passed luminance) / mean`, on BOTH surfaces, resampled to the same
pixels-per-millimetre (2.4, a Quest 3 at arm's length) and band-limited to the 1.5-6 screen-pixel
octave. It is a RELATIVE contrast, so `alb = tex2D(...) * _Color` cancels exactly and a cap can be
compared to a board at a different brightness.

**TWO TERMS, ALWAYS REPORTED SEPARATELY**, because they are separate knobs: the ALBEDO grain
(`cap_atlas.GRAIN_TEMPER`) and the NORMAL-MAP relief (`GRAIN_RELIEF_SLOPE`). The relief amplitude
is pinned, so tempering the albedo does not move it.

**What it found, and it falsified the round's own brief.** The brief blamed ModBuild 291's
contrast rise. Measured: albedo **0.93x / 1.03x / 1.51x** of each cap's own board -- already at the
bar -- and normal-map relief **15.9x / 45.5x / 53.7x**. The noise was entirely the normal map.

**And the cause was a pinned factor of a product.** `GRAIN_RELIEF_STD` pins the micro-relief's
HEIGHT standard deviation; a normal map carries its GRADIENT, and slope is amplitude x frequency.
Round 3 pinned the amplitude for a good reason (see the next section) and left the frequency free;
between ModBuild 286 and 289 the material became a registered photograph at the same cell size,
its grain moved up in frequency, and the slope went with it. `GRAIN_RELIEF_SLOPE` pins the Sobel
gradient instead -- measured with the SAME operator `normal_from_height` applies downstream, so
what is held fixed is what becomes nx/ny.

**A structural term nobody had named:** the board's face carries 1186-1246 texels/m and a keycap
cell carries 4122-5831, so **the cap stores its material at 3.3-4.9x the board's density**. At
equal contrast its grain lands far higher up the frequency axis at the same distance. That is why
the albedo remedy is a low-pass at the board's own resolution limit and not a contrast reduction.

Six self-checks, and **leg 5 failed first and the instrument was wrong, not the world**: it
compared a coarse and a dense storage of one surface at two different samplings, because
`resample` refused to magnify. Bilinear magnification is half the finding -- a magnified texture
has nothing at the top of the eye's band, which is a real reason a board reads smooth.

## The micro-relief is where three defects lived, and they were all the same defect

`carve` puts the material's own grain into the height field. Fed a REGISTERED plate it built a
second, painted bevel on top of the real 45 degree geometry -- the doubled edge, authored here
rather than by the model. High-passing at cell/16 did not remove it and neither did cell/48,
because a band EDGE is a step and a step has energy at every frequency. Subtracting the cell's
RADIAL band profile came out flat to +-0.001 against a height sigma of 0.024 **and the ridges
were still there**: the gather tilts outward at every edge, so the artefact is +y at the top and
-y at the bottom and a radial mean cancels it exactly while leaving every ridge in place. The
profile is taken PER SIDE now. And the relief AMOUNT was pinned to `GRAIN_RELIEF_STD = 0.02376`,
measured over 24 cells of the ModBuild 286 chain, rather than left as a gain that would have
doubled the bump simply because the new art has more contrast.

**ROUND 6 SUPERSEDED THAT PINNING AND THE REASONING BEHIND IT WAS STILL RIGHT.** Pinning an
amplitude instead of a gain was the correct move against the defect round 3 faced. It pinned the
wrong factor: a normal map carries the height's GRADIENT, the art's grain frequency then rose, and
the slope rose with it until the caps read as sandpaper. `GRAIN_RELIEF_SLOPE` (per board, solved
against each board's own face) replaces it. The shipped rule produced a Sobel-gradient RMS of
0.03332; the round-6 targets are 0.00143 / 0.00056 / 0.00041, and the field's per-texel nx sigma
falls from 0.39 to 0.026 / 0.008 / 0.008 -- with the normal maps shrinking from 0.52-0.54 MB to
0.14-0.22 MB, because what was removed was high-entropy noise PNG could not compress.

## The engraving preview, and the picture that could not answer the question

`src/GloomhavenVR/Cards/BoardEngraving.cs` cuts localized text into the board with a TMP
distance-field material. **Nothing in this repository can render that**: TextMeshPro is not in the
companion Unity project's package manifest, so `Assets/Editor/PreviewKeycaps.cs` draws a STAND-IN
out of legacy `TextMesh` -- and its stand-in for the underlay is a FULL SECOND COPY of the glyph
rather than the thin sliver TMP actually leaves visible. That picture came back showing pale ghost
text on the plate, which is precisely what a full bright copy behind a dark glyph looks like and
says nothing about the shipped material. **A picture that cannot show the thing is not evidence
either way** -- reading one as if it were is how this project has lost rounds.

`engrave_preview.py` models the ONE term the stand-in gets wrong: TMP's layer compositing
(underlay behind, outline ring, face on top), from the same colours, offsets, softness and widths
the C# writes. Measured over all three styles and both languages, the glyph body comes out
**52 % darker** than the board immediately around it, with the lit lip at **1.00-1.02x** the
board's own luminance over **9-12 %** of the area -- a cut with a lip, not pale text lying on the
surface. The glyph SHAPES are not the shipped ones (the board uses the game's harvested
MarcellusSC face) and are not what is in question.

## The generated inputs

ROUND 1 asked for a symbol sheet and three MATERIAL swatches, all `1:1`, all delivered 1024x1024.
The sheet is still in use (9 of 9 cells usable: check, return arrow, skip bar, hand, anchor, a
footprint pair and three alternates); the three swatches are superseded.

ROUND 3 asked for four OBJECT plates, all `3:2`, all delivered exactly 1536x1024 and all read
back with PIL rather than assumed. Each carries BOTH shapes, so four images cover six plates.

| image | outcome |
|---|---|
| `../out/keycap3_object_oak.png` | KEPT, first try — quarter-sawn oak, ray fleck, thumb polish on the lower rim, wax in the inner chamfer, a ding and a split |
| `../out/keycap3_object_steel.png` | KEPT, first try — draw-filed blued steel, temper bloom, rim worn to bare metal, oxide creeping out of the inner chamfer |
| `../out/keycap3_object_bronze.png` | KEPT, first try — sand-cast bronze, casting seam, burnished lower rim, verdigris pooled in the chamfer and the bottom field corners, a foundry punch |
| `../out/keycap3_object_bronze_b.png` | **DISCARDED** — a louder field: contrast 7.0 -> 9.2 % there, but REG 23.7 -> 18.5 and the WHOLE cap's rendered contrast 17.3 -> 15.3 % with clipping 2.4 -> 7.1 %. Optimising the one term an instrument was pointed at is the error this round exists to undo |

**THE PROMPTS ARE IN THE FILE, VERBATIM.** `cap_object.PROMPTS` holds all four asks; `CHOSEN` and
`DISCARDED` name every generated file; `_check_manifest()` runs at import and RAISES if a
`keycap3_object_*.png` in `../out/` is unaccounted for or is listed both ways. Round 2 lost the
wording of two shipped plates and had to record "substance, not prompts"; this is the correction.

**The 3:2 trap cannot fire on a 3:2 ask, and the delivered sizes were read back anyway.** The trap
is real for 16:9 and 2:1 asks (see `../img2img/README.md`).


**Two symbols were NOT generated.** `rest_short` (crescent-and-embers) and `rest_long` (spoked
wheel) are the motifs already carved into the board's own rest PADS, and they exist as binary
stencils in `../out/motifs_a/` and `../out/motifs_b/`. The design asks the cap symbol to "echo the
pad it sits beside"; reusing the pad's own stencil is not an echo, it is the same shape. Each style
takes them from the sheet ITS board took them from (`../tex_atlas.py:STYLE_MOTIFS`: oak and steel
from sheet_a's guild woodcut, bronze from sheet_b's Norse interlace), so a bronze cap carries the
plaited crescent its bronze pad carries.

**One hand for all three boards.** The five ROLE symbols are drawn in the guild-woodcut hand only,
not once per board hand. They are functional icons, not ornament: a check mark is a check mark, and
a player who switches boards must not have to relearn the controls. The per-board difference is
carried by the MATERIAL and by the two rest motifs, which do differ.

## Why the symbol is CARVED and not raised

Measured, not stylistic, and it is the round-2 finding cashed. On `BoardLit` the specular is a
**bevel term, not a surface term**: with the baked key and a viewer in front of the board the
half-vector sits ~32 degrees off a flat face, and `_SpecStrength` 0 vs 0.85 moves the flat-on mean
by **0.001**. A raised symbol reads by its highlight, and the highlight is the one thing this
surface cannot deliver. A recessed one reads by **ambient occlusion**, which is baked into the
albedo and is view-independent — so it reads at every angle and every distance.

## The generated image is never the shading

Inherited verbatim: a generated image is a **binary stencil**, the bevel is rebuilt from an exact
distance transform, and the motif is carved into a procedural material. `cap_atlas.py` reuses
`../tex_symbols.py`'s own reader (`_cell_binary` -> `_resize_mask` -> the SDF resize) rather than
re-implementing it, so the thresholding, the morphology and the sub-texel edge are the same code the
board's motifs went through — including the two bugs already found and fixed in it (the transposed
cell map and the Otsu units).

## Nothing here has to tile

The single hardest constraint of the board texture job does not exist in this one. Cells never touch
on the mesh — each cap face samples exactly one cell — so there is no seam to close and no
repetition to hide, which is why no generated image is asked to tile. Round 1 and round 2 cashed
that as an independent RANDOM CROP per cell; round 3 cashes it as a REGISTERED picture per cell
plus a per-cell wear jitter confined to the field, which buys the same freedom and a rim as well.

## The normal map ships at HALF the albedo's resolution

A measurement, not a saving. Written at full resolution it came out **2.5 MB per style, bigger than
the albedo**, because the material's micro-grain is high-entropy noise PNG cannot compress. What
that grain buys on a flat cap face is nearly nothing, and the CARVE is a smooth low-frequency field.
Half resolution: **0.63-0.65 MB**, and the groove walls lose nothing visible. The gradient is taken
AFTER the box decimation with the strength doubled, so a wall's slope in world terms is unchanged.

**The red channel is inverted relative to `UnpackNormal`, deliberately** — every shipped board
normal map carries that inversion and the caps sit on those boards under the same key. Mixing
conventions would light a cap's carving from the opposite side to the board's own. If
`tex_common.normal_from_height` is ever fixed at source, `cap_atlas.py` must be fixed in the same
commit.

## The level is re-based, and this is the defect that made it necessary

`BoardLit` does `alb = tex2D(_MainTex, uv) * _Color`, so a keycap texture MODULATES the state
colour. The texture it replaces is a near-white greyscale grain (mean **0.837**); these plates are
photographs (means **0.27-0.42**). Dropped in unchanged the rendered cap face went from **1.75x**
the luminance of the well it sits in to **0.95x / 0.85x / 0.57x** (oak / bronze / STEEL) -- the
"invisible button, only the text visible" shape, which `WorldUI.ButtonTuning.SeatedCapColor` exists
to prevent **and cannot see**, because it floors `_Color` and `_Color` had not moved.

`normalise_plate` re-bases each plate to the grain's mean with a UNIFORM RGB gain (hue ratios exactly
preserved) and a soft knee at 0.80 (bright grain compresses instead of clipping flat and taking the
wood structure with it). The gain is SOLVED over a few iterations rather than computed once, because
the knee moves the mean. Result: **1.74-1.81x** the well, i.e. the shipped ratio, with each board's
own cast and structure intact.

**ROUND 3 MEASURED WHAT THAT ACTUALLY COSTS, and left it alone anyway.** The uniform gain preserves
hue ratios; the knee and the final clip do not, and at these gains they are most of what happens —
**97.4 % of oak's shipped texels reach >= 0.996 in at least one channel**, so its modulator is very
nearly a CONSTANT in its own brightest channel (per-channel field contrast R 4.7 %, G 8.6 %,
B 22.3 %). Compressing each texel's brightest channel instead, so nothing clips and hue is exact,
was built and is WORSE: a warm plate cannot reach mean 0.837 with its brightest channel under 1, so
the level has to be bought with desaturation, and a fully greyscale modulator raises rendered
contrast to 13.8 / 10.8 / 11.0 % while collapsing the ModBuild 286 per-board separation from
min pairwise dE 17.0 to **6.8**. The clipping is load-bearing: it is what lets a warm plate reach
0.837 AND keep a cast. The one change round 3 did make is that the solve now targets the recessed
FIELD (`mask=`), because the FACE is submesh [0] and samples only the field. The lever that would
free this properly — lower the target and raise `[ButtonColors] BoardCapTint` by the reciprocal, so
the product is unchanged — is a config round across four tint families, their wire defaults and
`KeycapGrain` itself.

**A chroma boost was tried and rejected on measurement.** It does not separate oak from bronze at
any gain -- CIELAB dE 3.7 -> 3.9 -> 3.1 from k = 1.0 to 2.6, because their separation lives in the
blue channel, which the parchment state colour has already crushed -- and it clips 99.99 % of oak's
texels at k = 1.6. The three rendered idle faces sit at dE 13.8 (oak-steel), 10.3 (steel-bronze) and
3.7 (oak-bronze): steel is clearly a different material, oak and bronze are told apart by their
grain rather than their hue. The lever that would change that is the idle face colour, which is
`[ButtonColors]` and is the user's own tuning.

## The acceptance instrument, and the bar it got wrong first

`cap_check.py --symbols` gates on **contrast measured after resampling the cell to the size the cap
occupies in the headset** — 96 px at arm's length, 32 px across the table, argued from a 40 mm cap
and ~20 px/degree on a Quest 3.

Its first version gated on **limb width against twice the chamfer** and failed 19 of 24 cells while
every one of them was measuring 35-46 % contrast. Two things were wrong: the chamfer is a smoothstep
rather than a ramp, so a limb at 79 % of that width still reaches 90 % of full depth; and, much more
to the point, **depth is a property of the mechanism and what a player needs is contrast on screen.**
The limb and its achieved depth are still printed — as diagnostics that explain a low contrast when
there is one — but they do not decide.

`--selfcheck` runs the measurement on a NULL input (a plain cell: it must refuse to measure) and on
an uncarved plate with a symbol-shaped mask (it must report a drop below the floor: −0.21 %), then
on the same disc actually carved (it must fire: 33.9 %). A new instrument's first output is a
hypothesis.

### Results, shipped atlases, 256-texel cells

Every cell of every style lands at **34-47 % contrast at arm's length** and **25-44 % across the
table**. The thinnest limb anywhere is `rest_long` on oak at 2.8 texels, reaching only 34 % of the
groove depth — and it still delivers 34.9 % at 96 px, because the AO carries it.

## What the runtime does with the atlas

`src/GloomhavenVR/Cards/CapCellMath.cs` holds the cell arithmetic (and `CapRole`, whose numbering IS
the cell index); `CapSymbols.cs` loads the atlas; `PlayTray.NewKeycapMaterial` is the single call
BOTH the owner's board and every peer's mirror of it mint their cap materials through, so the two
cannot resolve a different cell. `tests/GloomhavenVR.WireTests/BoardCapSymbolVectors.cs` pins the
grid, the row-from-the-bottom flip and the role numbering.

**If `GRID`, `CELLS` or a cell size changes here, `CapCellMath` and those vectors change in the same
commit.** A mismatch puts the wrong symbol on every cap on every board at once, on both sides of the
wire, and nothing in the build will say so.
