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
    <gpt-image-2>    1 symbol sheet (3x3, 1:1) + 3 material plates (1:1, each with that style's
                     ref_face as referenceImages). Prompts are in the commit that added this file.
    cap_atlas.py     stencil -> exact distance transform -> carve into the generated material.
                     Writes the six PNGs and their .meta files.
    cap_check.py     the acceptance instrument, with a null input and a known-positive control.
    cap_sheets.py    contact sheets from the Unity render station's output (PreviewKeycaps.cs).
    engrave_preview.py  what the BOARD ENGRAVING looks like -- see below.

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

## Four images, all accepted first try, none re-rolled

| image | what it bought |
|---|---|
| `../out/keycap_symbols_sheet.png` | the five ROLE symbols (check, return arrow, skip bar, hand, anchor) plus a footprint pair and three alternates, 9 of 9 cells usable |
| `../out/keycap_plate_oak.png` | the oak cap's material — honey grain, slightly darker and redder than the board so a key reads as a separate piece of wood |
| `../out/keycap_plate_steel.png` | the steel cap's material — dark blued iron with planishing and oxide |
| `../out/keycap_plate_bronze.png` | the bronze cap's material — gold-brown with verdigris in the low spots |

**The 3:2 trap did not fire and that was checked rather than assumed.** Every request was `1:1` and
every image came back exactly 1024x1024. The trap is real for 16:9 and 2:1 asks (see
`../img2img/README.md`), and it is why nothing here asks for either.

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
on the mesh — each cap face samples exactly one cell — so each cell takes an independent random crop
of the generated plate. There is no seam to close and no repetition to hide, which is also why no
generated image is asked to tile.

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
