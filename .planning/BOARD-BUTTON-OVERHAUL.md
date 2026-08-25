# ROUND 3 (2026-08-25): THE SWATCH THAT WAS NEVER AN OBJECT

ModBuild 286 went to hardware. The mesh was accepted; the textures were rejected for the third
time, and for the second time with the same word.

**The user, verbatim:**

> "Mir gefällt dass du die buttons etwas anders vom mesh her designt hast. **Aber mir gefallen
> diese texturen überhaupt nicht. Die sind einheitlich und so sieht das aus wie aus den
> 90'igern.** Nochmal: Lass dir für die Textur der buttons je nach board eine passende Textur
> von gpt-image-2 generieren und nutze die. So dass der button pro board immersiv dazugehörig
> aussieht und auch mehr **"Real"** und nicht wie **zusammengewürfelte assets mit standard meshs
> draufgeklatscht** wie aktuell."

---

## THE MEASUREMENT FIRST — what actually separates the accepted board BACKS from the rejected cap plates

Round 2 raised cap-scale STORY contrast by +60 / +71 / +147 % and was rejected with the same
word. A metric that rises while the picture stays wrong is measuring one term of what the eye
sees, so round 3 started by measuring the difference against something from the SAME generator
that the user volunteered praise for — the board backs, *"Die Rückseite der boards gefällt mir
sehr gut!"* (ModBuild 276).

**`unity/board-prep/buttons/plate_forensics.py`** puts every source into OBJECT-NORMALISED
coordinates (short side resampled to 256 texels, so "a sixth of the frame" is 43 texels on a
480 mm board back and on a 53 mm keycap alike) and reports four things: the octave spectrum,
non-stationarity, contour coherence, and **REG** — mean luminance against distance-to-border,
peak-to-trough, per cent of the mean. REG is the one a swatch cannot have: translate a swatch
and it is the same swatch.

| source | REG | total σ | COH | GINI |
|---|---|---|---|---|
| accepted board backs (oak / steel / bronze) | 89.8 / 57.3 / 106.6 | 51.2 / 26.2 / 52.7 % | 0.78 / 0.54 / 0.82 | 0.63 / 0.39 / 0.68 |
| round-2 plates as generated | 18.0 / 14.6 / 30.9 | 20.0 / 14.4 / 24.6 % | 0.46 / 0.31 / 0.45 | 0.42 / 0.34 / 0.39 |
| **the SHIPPED atlas cells** | **3.7 / 9.9 / 3.8** | 6.1 / 9.6 / 9.8 % | 0.47 / 0.35 / 0.39 | 0.37 / 0.34 / 0.39 |
| a stationary NOISE SWATCH (null control) | 17.4 ± 3.9 | 20.0 % | 0.20 | 0.29 |
| a drawn OBJECT on the same noise (known positive) | 68.6 / 72.4 | 30.3 / 31.1 % | 0.57 | 0.40 |

**The shipped cap face carries LESS border structure than random noise, and the backs carry
four times as much.** On total contrast the round-2 plates sit at 19.6 % against the noise
swatch's 20.0 % — round 2 bought exactly as much contrast as a random field, and exactly as much
layout. Against the null-to-backs gap, round 2 closed **23 % on STORY and 5.7 % on REG.**

**THE BRIEF'S DIAGNOSIS WAS RIGHT AND IT WAS HALF THE CAUSE.** The other half is in this
repository, not in the prompt: **`cap_atlas.material_cell` took a RANDOM CROP at a RANDOM
OFFSET.** Even the little registration round 2's plates had did not survive it — REG 18.0 → 3.7
(oak), 30.9 → 3.8 (bronze). An unregistered crop cannot carry a rim, because a rim is a
statement about WHERE. Two rounds of better asks would have kept being ground back to a swatch
by that one line.

**WHAT THE INSTRUMENT CANNOT SEE, stated because round 2's did not state it.** None of the four
metrics knows what the picture DEPICTS: a rim drawn in the wrong place scores exactly like a rim
drawn in the right place, and REG in particular is blind to whether the registration agrees with
the MESH (that is what `cap_object.report_cells` is for). All four are computed on the albedo;
the state colour is a uniform multiply and cannot move any of them, but the normal map can and
is not modelled. And the phase-randomised SURROGATE null is **uninformative for these images**:
their spectra are so red that a surrogate lands at REG 27–69 by chance, so the z-scores come out
at 0.8–2.1 for the backs and for round 3 alike. **The absolute comparison is what carries the
finding; the surrogate z does not, and it is printed anyway rather than quietly dropped.**

---

## THE STRUCTURAL FACT THAT MADE THE FIX POSSIBLE — one cell IS one button

The keycap meshes UV planar over their own footprint (`CardMesh`:
`Uv(p) = (p.x/width + 0.5, p.y/height + 0.5)` on EVERY vertex, walls included), so an atlas
cell's (0,0)..(1,1) is **exactly the cap's outline, edge to edge**. The signet profile's bands
therefore have exact known positions in the cell:

    outer chamfer   d in [0.000, 0.060)     d = distance to the cap's outline, in units of the
    rim land        d in [0.060, 0.105)         cap's SHORT side (CapFaceLayout.Bezel*)
    inner chamfer   d in [0.105, 0.135)
    recessed field  d in [0.135, 0.500]

The cap is not square, so the same absolute band is 0.1206 (oak) / 0.1331 (steel) / 0.1114
(bronze) of the cell's **u** against 0.135 of its **v**. That anisotropy is why round 3
generates the art ONCE per board on a square template and REGISTERS it per board in Python,
rather than hoping the model lands it.

**AND THE SPLIT IS NOT THE OBVIOUS ONE.** `PlayTray.7.Nested.cs` gives submesh [0] — the
recessed FIELD — the ROLE cell, and gives submeshes [1] (bezel ring) and [2] (walls)
**`CapRole.Plain`, cell 0, on every cap**. So a role cell is only ever sampled at d > 0.135, and
the plain cell is only ever sampled at d < 0.135 — by a SQUARE cap. A ROUND cap's bezel is an
annulus, and at 45° an annulus sits at square-distance 0.146…0.242, out in the middle of the
cell.

**ONE CELL CANNOT CARRY BOTH SHAPES' BANDS, and that was checked rather than assumed.**
Registering the plain cell on `min(d_square, d_round)` puts chamfer material over ~74 % of the
SQUARE cap's rim land; on `max`, field material over ~38 % of the ROUND cap's bezel. So the
plain cell is registered EXACTLY for the square cap out to d = 0.135, and everything further in
— which no square cap ever samples, and which is never a cap FACE (the only two buttons built
with the default `CapRole.Plain` are `CombatLogSurface`'s pin and close, and both pass
`capStyle: null`, which `NewKeycapMaterial` routes to the shared KeycapGrain with no cell
transform at all) — is filled with RIM-LAND material. A round cap's bezel then reads
bezel-family material at every angle: exact on the four axes, drifting to plain rim material at
the four diagonals. **That drift is a stated cost of this round.**

---

## THE IMAGES — four, three kept, one discarded, prompts VERBATIM in the file

Every request was `3:2` — a NATIVE aspect for this tool, so the 16:9-into-3:2 rescale trap
cannot fire — and every delivery was read back with PIL and came in at **exactly 1536 × 1024**.
Each image carries BOTH shapes, so four images cover six plates.

| image | ask | outcome |
|---|---|---|
| `keycap3_object_oak.png` | quarter-sawn oak signet plate: ray fleck at a sixth of the frame crossing the field, thumb polish on the bottom rim land, wax and dirt in the inner chamfer, one ding on the outer chamfer, a split in the wood | **KEPT**, first try |
| `keycap3_object_steel.png` | draw-filed blued steel: parallel tool marks, temper-bloom clouds at a sixth of the frame, rim worn to bare metal along its bottom edge, red-brown oxide creeping out of the inner chamfer, one peening dent | **KEPT**, first try |
| `keycap3_object_bronze.png` | sand-cast bronze: casting seam over the chamfer and rim, burnished gold lower rim, verdigris pooled in the inner chamfer and into the bottom field corners, planishing dishes, a foundry punch mark | **KEPT**, first try |
| `keycap3_object_bronze_b.png` | the same with the field's planishing dishes enlarged to a fifth of the frame and the verdigris flooded a quarter of the way in | **DISCARDED** — see below |

**THE DISCARD IS ROUND 2'S OWN MISTAKE, OFFERED AGAIN.** `caps_on_his_bronze_well.png` redraws
only the recessed FIELD, and on that panel round 3's bronze is CALMER than the shipped cap
(field contrast 13.4 % → 9.1 %). Bronze is the board in his screenshot, so a fourth roll was
asked for with a much louder field, and it delivered exactly that:

| roll | REG | cell σ | rendered σ WHOLE cap | rendered σ FIELD | clipped |
|---|---|---|---|---|---|
| `keycap3_object_bronze` | **23.7** | **18.7** | **17.3 %** | 7.0 % | **2.4 %** |
| `keycap3_object_bronze_b` | 18.5 | 17.3 | 15.3 % | **9.2 %** | 7.1 % |

It buys 31 % more contrast in the field and gives back 22 % of the REGISTRATION and 12 % of the
contrast of the WHOLE cap, which is what a player looks at. **Taking it would have been round
2's error in a new place: optimising the one term an instrument happened to be pointed at.**

**THE PROMPT RECORD IS THE FILE THIS TIME.** Round 2 lost the exact wording of two shipped
plates and had to write "substance, not prompts". `cap_object.PROMPTS` holds the four asks
verbatim, `CHOSEN` / `DISCARDED` name every generated file with its reason, and
`_check_manifest()` runs at import and RAISES if a `keycap3_object_*.png` in `../out/` is
unaccounted for or is listed both ways. **The guard was driven negative before it was believed:**
emptying `DISCARDED` makes it raise by name.

**THE MODEL WAS SHOWN THE GEOMETRY, not told about it.** `cap_object.init_frames()` renders
each board's two plates with the four bands at their exact fractions, tinted to that board's own
albedo face band (`gen_capref.py`), on a flat backdrop with margin — the margin exists so the
crop is MEASURABLE afterwards. The template's band values are a **dot product against BoardLit's
own baked key** `normalize(0.35, 0.85, -0.45)`, not a hand-picked ramp: the first version
hand-signed them and got BOTH chamfers backwards, which would have taught the model to paint its
relief lit from below, in direct opposition to the mesh.

---

## WHAT SHIPPED — a registered picture of a button, not a crop of a material

`cap_object.py` (new) segments each generated frame, registers the crops onto the mesh's own
bands, and hands `cap_atlas.build_style` three kinds of cell: **square** (the four generic caps
and the follow/pin toggle), **round** (cells 5 and 6, the rest pads), and **bezel** (cell 0,
which every cap's bevel ring and side walls sample). `material_cell`'s random crop is gone from
the shipped path and kept only behind `--no-objects`, which is the A/B control for every sheet.

**THE CROP IS THE SILHOUETTE, AND TWO CLEVERER THINGS WERE TRIED FIRST.** Forcing the crop
square about the silhouette's centre cut 28 px off oak's bottom chamfer, because the model
photographs each plate with a trace of perspective and a soft contact shadow (oak's square plate
measures 643 × 698 for an object that is square). Fitting the inner-chamfer contour and scaling
out through the known 0.73 field span locked onto the plate-to-backdrop edge and "fitted" an
805 px plate inside a 643 px silhouette — an answer falsified by its own input. Cross-correlating
the ridge profile against the template got correlation 0.89–0.98 and per-axis scales that
disagreed by up to 13.5 %, the same edge dominating again. **The residual is measured instead of
eliminated:** `cap_object.report_cells` reads the bands off the FINISHED cell, and on all nine
cells the rim land is the brightest band and the inner chamfer the darkest, which is what the
ask asked for.

**PER-CELL WEAR, NOT PER-CELL CLONE.** Seven caps cut from one plate would be seven clones, so
each cell gets a low-frequency stain/burnish jitter — confined to the FIELD and faded to nothing
before it reaches the bezel, because the bezel of every cap comes from the PLAIN cell while the
field comes from the ROLE cell, and a jitter that reached the boundary would put a step exactly
on the seam between two submeshes' textures.

### THREE PIPELINE DEFECTS FOUND BY THE RENDER, all in the NORMAL map, all the same shape

`carve` fed the material's whole luminance deviation into the height field as micro-relief. On a
swatch that is harmless — a swatch has almost no low-frequency content, which is what
"einheitlich" meant. On a REGISTERED plate it builds **a second, painted bevel on top of the real
45° geometry**, which is precisely the doubled edge this round was warned about, authored by the
pipeline rather than by the model.

1. **High-passing at cell/16 was not enough**, and neither was cell/48: a band EDGE is a step,
   and a step has energy at every frequency. Both cut-offs left the rim's and the chamfer's
   edges in the height field, and the rendered normal map showed four bright/dark ridge pairs
   tracking the four band boundaries.
2. **Subtracting the cell's RADIAL band profile came out flat to ±0.001 against a height σ of
   0.024 — and the ridges were still there.** Both were true: the gather tilts the surface
   OUTWARD at every edge, so the artefact is +y at the top and −y at the bottom and a radial
   mean cancels it exactly while leaving every ridge in place. *An instrument that averages over
   the axis the defect lives on agrees with every broken build.* The profile is taken **per
   side** now, and the ridges are gone.
3. **The relief AMOUNT is pinned, not inherited.** `GRAIN_RELIEF` was a gain on the plate's own
   luminance deviation, and round 3's registered art carries roughly twice a swatch's contrast —
   so the same gain would have doubled the bump for a reason that has nothing to do with how
   rough the material is. `GRAIN_RELIEF_STD = 0.02376` is the amplitude the ModBuild 286 chain
   actually produced, measured over 24 cells (three boards × eight crops), and round 3 hits it
   exactly. **The only thing that changed this round is the albedo, which is the variable under
   test.**

### A LEVEL DEFECT MEASURED, AND A FIX MEASURED AND *NOT* TAKEN

The re-base to the shipped grain's mean of 0.837 clips, and it clips much harder than anyone had
noticed:

    texels at >= 0.996 in ANY channel, SHIPPED ModBuild 286 atlases, Confirm cell
        oak 97.4 %      steel 0.35 %      bronze 36.3 %
    per-channel contrast of oak's field, same cell
        R 4.70 %        G 8.63 %          B 22.30 %

**Oak's shipped modulator is very nearly a CONSTANT in its own brightest channel**, and what
structure it has lives in the channel the warm state colour attenuates most. The obvious
correction — compress each texel's brightest channel and scale the other two with it, so hue
ratios are exact and nothing clips — was built, and it is **worse**, because a modulator's mean
and its saturation trade off directly: a warm plate whose channels sit near (1.00, 0.75, 0.45)
of its own maximum cannot have a mean luminance above 0.73 with its brightest channel under 1.
Measured through the whole shipped chain:

| chroma spent | rendered σ oak / steel / bronze | min pairwise ΔE |
|---|---|---|
| shipped recipe | 8.04 / 11.04 / 9.82 % | **17.0** |
| 0 % (hue exact) | 7.48 / 8.35 / 3.69 % | 13.7 (oak 19 % darker) |
| 50 % | 4.26 / 9.46 / 5.48 % | 15.8 |
| 100 % (greyscale) | **13.79 / 10.77 / 10.99 %** | **6.8** |

A greyscale modulator wins the contrast handsomely and **collapses the per-board separation
ModBuild 286 solved for** — oak's rendered chroma falls from C\* 15.3 to 3.5 and oak↔steel ΔE
from 21.6 to 6.8 — because with a neutral plate the boards differ only by their idle colour, and
those were solved WITH the plates' casts in the product. **The clipping is load-bearing: it is
what lets a warm plate reach 0.837 and keep a cast.** `normalise_plate` is therefore left exactly
as it shipped, with one addition — the solve now targets the **FIELD**, because the FACE is
submesh [0] and samples only the field, so the field is what the cap-to-well ratio turns on.

**The lever that would actually free this is out of this lane and is written down rather than
taken:** lower the modulator's target and raise `[ButtonColors] BoardCapTint` by the reciprocal.
The product is unchanged, so nothing renders differently, and the texture gets its headroom back.
That is four tint families × three channels in `Defaults`, the wire defaults that mirror them,
and `KeycapGrain` itself for every cap that does not use a board atlas. It is a config round.

---

## THE RESULT, through the real `BoardLit` from a rebuilt bundle

**Object-normalised statistics of the shipped cell (Confirm), ModBuild 286 → round 3:**

| board | REG | total σ | COH | GINI |
|---|---|---|---|---|
| oak | 6.2 → **28.3** | 7.0 → **15.6 %** | 0.48 → **0.82** | 0.50 → **0.58** |
| steel | 10.3 → **35.3** | 10.3 → **16.5 %** | 0.38 → **0.77** | 0.41 → **0.54** |
| bronze | 5.9 → **23.7** | 11.2 → **19.3 %** | 0.41 → **0.83** | 0.45 → **0.62** |

For comparison, a crop of the accepted board BACK the size of a cap reads REG 13.6 / 10.6 / 9.4
and σ 15.7 / 17.9 / 23.5 %. **Round 3's caps now match the backs' contrast and exceed their
border structure** — which they should, because a cap is a smaller object whose border is a
larger fraction of it.

**Through the shipped chain (plate × idle × `BoardCapTint` 0.5 × shade), whole cap:**

| | ModBuild 286 | round 3 |
|---|---|---|
| rendered σ oak | 7.06 % | **15.62 %** (×2.21) |
| rendered σ steel | 10.26 % | **16.75 %** (×1.63) |
| rendered σ bronze | 9.16 % | **18.00 %** (×1.97) |
| min pairwise ΔE | 17.2 | 16.8 |
| cap ÷ well, oak / steel / bronze | 2.30 / 2.21 / 2.22 | 2.24 / 2.17 / 2.08 |
| L\* oak / steel / bronze | 23.33 / 19.83 / 23.35 | 21.80 / 19.50 / 21.60 |

**The ModBuild 286 idle colours are untouched and their separation survives**: min pairwise ΔE
16.8 against 17.2, still far above the 10.0 the solver was gated on. The cap-to-well ratio moves
2–6 % and stays at 2.08–2.24, nowhere near the ≤ 1.0 that produces the "invisible button". The
caps are 0.3–1.8 L\* darker, because the bezel now contains real dark chamfer bands that were not
there before. *(This instrument's absolute well ratio is 2.1–2.3 where the ModBuild 281 record
says 1.75; round 2 recorded the same discrepancy and could not reproduce that record's well with
four definitions. What is asserted here is the CHANGE, where the well term cancels.)*

**Symbol legibility is unharmed.** `cap_check.py --symbols` puts every cell of every style at
33.7–45.7 % contrast at arm's length and 22.5–38.4 % across the table, against round 2's
34–47 % / 25–44 %. `--selfcheck` still refuses the null, reports −0.21 % on the uncarved control
and fires at 33.9 % on the carved one.

**The render station's mesh invariant still MATCHES** — 72 verts / 36 tris / [6, 72, 30] on the
square cap, 642 / 640 / 3 submeshes on the round one, 72 of 72 and 642 of 642 tangents — and its
colour calibration reproduces the gamma chain to 0.002 of 255.

---

## THE PICTURES

All in `.planning/debug/keycaps3/`:

| file | what it answers |
|---|---|
| `back_vs_cap.png` | **the sheet that decides this round.** A crop of each shipped board BACK, the ModBuild 286 cap cell and the round-3 cell, all the same number of millimetres across, with the forensics numbers under each |
| `renders_r2_vs_r3.png` | ModBuild 286 beside round 3, every role on every board, through the real `BoardLit` — one station, run twice, with only the six atlas PNGs changed between the runs |
| `caps_on_his_bronze_well.png` | his own frame, four panels, with a CONTROL that must be invisible against his untouched pixels |
| `plate_forensics.txt` | the whole derivation, its null control, its known positive and its matched surrogate |
| `<Style>_<Role>_{before,after}_{front,rake}.png` | every role on every board through the real shader |
| `<Style>_<Role>_zoom_{near96,far32}.png` | reading distance and across the table, NEAREST-upscaled so the pixels can be counted |
| `caps_before_after_*.png`, `caps_all_boards.png`, `caps_zoom_near_far.png` | the station's own contact sheets |
| `r2ref/` | the entire ModBuild 286 render, kept so the comparison is against files rather than memory |
| `preview-round3.log` | the mesh invariant, the colour calibration and the tangent A/B |

**THE COMPOSITE'S CONTROL PANEL IS HONEST ABOUT ITS OWN ERROR.** Panel 2 redraws the SHIPPED
atlas through the whole chain and should be invisible against panel 1: its field mean matches
his own pixels to 0.006 per channel (0.174/0.188/0.085 against 0.174/0.188/0.079), but its field
CONTRAST comes out 13.4 % against his 10.8 %. Round 2 fitted `NORMAL_MIP_SIGMA = 1.25` against
the round-1 atlas, and this panel is the round-2 one; the level reproduces, the contrast is 25 %
high, and no comparison on the sheet depends on it because every panel uses the same value.

**AND WHAT NO PICTURE HERE SHOWS.** The composite redraws only the recessed FIELD — the bevel,
the recess wall and the board are his own pixels — so it cannot show the new BEZEL, which is half
of what round 3 changed. **On the field alone round 3's bronze is CALMER than the shipped cap
(9.1 % against 13.4 %); on the whole cap it is nearly twice as contrasty (18.0 % against 9.2 %).**
Both are true and the second is what a player sees.

---

## WHAT THIS COSTS, stated

* **A round cap's bezel reads the SQUARE cell's bands.** Exact on the four axes, drifting to
  plain rim material at the four diagonals. One atlas cell cannot carry both shapes' bands (the
  arithmetic is above), and the alternative — a spare cell for the round bezel — needs
  `PlayTray.7.Nested.cs` to pass a different role, which is outside this lane.
* **62 % of oak's field texels still clip in at least one channel** (down from 97 % shipped), and
  2.4 % of bronze's (down from 36 %). That is the price of a warm modulator at mean 0.837 and it
  is not fixable inside this lane; the config-round lever is written down above.
* **The four STATE colours are still per ROLE, not per board.** While a cap is accented the three
  boards are told apart by their PLATE alone — a term that is now much stronger (REG 3.7 → 23.7
  on bronze) but is not ΔE 17.
* **The caps are 0.3–1.8 L\* darker** and the cap-to-well ratio is 2–6 % lower, because the bezel
  now contains real dark chamfer bands.
* **The registration is a gather with a kink along the cell's diagonals**, where the nearest edge
  switches. It is visible in the normal map as four faint corner seams and is an order of
  magnitude below the band ridges it replaced.
* **The bezel cell's deep interior is a wash.** The angular sweep that fills it keeps its grain
  out to square-distance 0.26 — past the deepest any reader goes — and fades to the rim band's
  own mean colour beyond, where only the MIP average can still see it.

## STILL OPEN

* **Nothing here has been on hardware.** The render station compiles the PROJECT's BoardLit for
  OpenGL in a LINEAR project while the rig runs GAMMA; the station emulates that and proves the
  chain to 0.002 of 255, but only the rig proves the bundle's D3D11 variants.
* The bronze plate's foundry punch mark sits in the lower-left of the field, which is inside the
  caption band. It is ~2 px at reading distance and reads as a mark rather than as a glyph, but
  it is a thing in the picture that was not there before.
* `NetProtocol.CapLabelMaxBytes = 48` still truncates a mirrored caption silently — unchanged
  from round 2, and still a wire constant nobody in a texture round should move.
# ROUND 2 (2026-08-25): THE TRUNCATED CAPTION, AND THE SHAPE THAT PAID FOR IT

ModBuild 281 went to hardware and came back with three rejections. This section is the record of
what round 2 changed and what it measured. Round 1's record follows below, unedited.

**The user, verbatim:**

> "Die Buttons gefallen mir noch nicht wirklich. Die Textur die dort gewählt ist, ist einheitlich
> und passt sonst nicht wirklich zum Styl. **Generiere eine echte unique Textur für die buttons mit
> gpt-image-2.** Auch die Form der Buttons gefällt mir noch nicht. Rund und Viereckig sind vorgabe,
> aber ansonsten darfst du gerne kreativ werden. **WICHTIG: Der Text muss immer voll lesbar sein.**
> Beim Test hatte ich einen Text der mitten drin abgeschnitten war 'AUSWAHL BEEN' (siehe
> abgeschnitter_text.jpg). Das darf nicht passieren. Du kannst eventuell auch mit Zeilenumbrüchen
> arbeiten, wenn es Sinn ergibt."

---

## ITEM 1 — THE TRUNCATION. Root cause confirmed by arithmetic, not by inspection.

The string was `"Auswahl beenden"` (`Player.log:5208`,
`TOGGLE 0 = True = EREADYBUTTONENDSELECTION = Auswahl beenden = True`). It rendered as
`"AUSWAHL BEEN"` — **twelve characters of fifteen.**

**FOUR LINKS, AND THE THIRD IS ROUND 1'S OWN REGRESSION.**

1. `Core/TmpFit.Fit` set `overflowMode = TextOverflowModes.Truncate`. Truncate DISCARDS glyphs and
   raises no flag any caller reads. The class doc stated the policy outright — *"past the
   readability floor the text truncates — text is always fully inside its plate"* — and the user has
   now overruled it.
2. The auto-size floor was `fontSizeMin = 0.08`. That was never a readability floor; it was the size
   at which the mod stopped shrinking and started cutting.
3. **ModBuild 281 is what made it bite.** The new carved symbol took the caption's room:
   `CapSymbols.LabelBox` went from `(0.92, 0.85)` to `(0.92, 0.36)` of the cap — a **58 % cut in
   height**. On BRONZE, whose seat recess forces the cap to 53.2 × 43.9 mm, that is a **15.8 mm**
   band.
4. `BoardButton.SetLabel` wrote `_label.text` and **never re-fitted**, so even a correct fit computed
   at build time was the wrong fit for the next wording — and these captions change every pick.

**THE ARITHMETIC THAT CLOSES IT, and it reproduces the screenshot exactly.** Two lines at the 0.08
floor need `2 × 1.2 em × 8 mm = 19.2 mm`; the band was 15.8 mm, so auto-size could not wrap. One line
of 15 characters at 0.08 needs ≈ 74 mm; the box was 48.9 mm, so it could not shrink either (it was
already at the floor). Truncate then cut at the 12th character. **"AUSWAHL BEEN" is 12 characters.**

### WHY THIS COULD NOT BE FIXED BY TUNING TO A CORPUS

The corpus was harvested from every `readyButton.Toggle` / `m_UndoButton` / `m_SkipButton.Toggle` /
pick-`DialogOption` call site in `decompiled/`, plus every mod `Loc` string that reaches a keycap.
26 fixed strings; the longest is **23 characters, DE `"Wähle eine andere Karte"`**
(`GUI_CHOOSE_OTHER_CARD`, `CardsHandUI.cs:2099`). The longest string ever *proven on the rig* is
`"Auswahl beenden"` (15).

**But three of the keys these caps display are format strings with runtime insertions, so the corpus
is unbounded:**

| key | what is interpolated | site |
|---|---|---|
| `GUI_END_TURN` / `GUI_END_EXTRA_TURN` | the actor's **class name** | `Choreographer.cs:12709` |
| `GUI_LOSE_CARD` / `GUI_DISCARD_CARD` | the **ability-card title** | `CardsHandUI.cs:2034/2038` — and that pair is exactly what `PickDialogOptionLabel(cancel:false)` reads |
| `GUI_CONFIRM_TARGETS` | a live `{1}/{2}` counter | `Choreographer.cs:4918` |

**There is no longest string.** A fix measured against a list is wrong the first time somebody ends a
turn as a long-named class. (Also worth recording: the game's own DE/EN string table is NOT on this
build machine — the game runs on a Windows box, `Player.log:1`. Four of the German strings in the
corpus are transcriptions living in mod comments, marked TRANSCRIBED in the test, and nothing asserts
a width for any of them.)

### WHAT SHIPPED — a property, not a patch

**`Cards/CapFaceLayout.cs` (new, Unity-free, linked into the wire suite).** It owns the cap's whole
face budget — the bezel profile, the symbol band, the caption band — and it solves the caption:

* **Nothing is ever discarded.** The solver only inserts line breaks. `Truncate` is gone from the
  entire mod, and a source lint in the wire suite fails the build if it comes back.
* **The floor dropped 0.08 → 0.045**, roughly doubling the shrink runway before anything can overflow.
* **Whole words first.** The size is solved TWICE — once forbidding mid-word breaks, once allowing
  them — and the whole-word answer wins whenever it fits. Measured: `"Bewegung überspringen"` on the
  OAK cap solves to font **0.072 with breaks** (`Bewegung / überspringe / n`) and **0.057 without**
  (`Bewegung / überspringen`). The bigger one is worse: a line holding one orphan letter is the exact
  SHAPE of the defect being fixed, and a player cannot tell a break from a cut.
* **The failure is loud.** `Caption.Overflows` / `Caption.HardBroke` reach
  `Core/TmpFit.FitCapLabel`, which names the string, the box, the size it needed and how far it
  overhangs — and the text is still drawn in full. The next report will name the caption instead of
  a screenshot doing it.
* **The metrics are the real font's, and the outcome is read back.** `FitCapLabel` measures through
  `TMP.GetPreferredValues` at font size 1, applies the layout, then forces the mesh and compares the
  RENDERED extent against the solved block. A disagreement over 15 % logs a SEPARATE line accusing
  the solver's own metric model — because "the caption does not fit" and "my ruler is wrong" have
  different fixes.
* **`SetLabel` re-fits, on both boards.** Link 4 closed on the owner (`BoardButton.SetLabel` →
  `ApplyLabelLayout`) and on the peer mirror (`InertCap.SetLabel`, which keeps its fit box for the
  purpose). `check-mirrors.sh` still passes.

**THE ASSERTION** (`tests/GloomhavenVR.WireTests/CapLabelFitVectors.cs`, 1 395 no-drop cases):
`StripWhitespace(out) == StripWhitespace(in)` for every corpus string × 3 boards × 3 font-metric
models × 5 box scales down to 2 % of the real box. Plus a NULL control (empty / whitespace / a
zero-area box), a KNOWN-POSITIVE control (a 200-glyph token, and the interpolated end-turn caption on
Bronze under the pessimistic metric model — both must raise `Overflows` AND still contain every
character), determinism, the one-permitted-size-drop property, the face-budget geometry, and a
source lint reading `cap_atlas.py` as TEXT so the carve and the caption can never again disagree
about where the field is. **That last lint is the one that would have caught ModBuild 281.**

**THE METRIC MODEL IS STATED BECAUSE IT IS A MODEL.** MarcellusSC is harvested from the game at
runtime and is not on this machine, so the tests drive a uniform-advance model at 0.50 / 0.62 / 0.75
em. Measured against a real serif face (DejaVu Serif): mean advance **0.597 em**, line step
**0.1168** — so the 0.62 / 0.1200 the suite asserts against is genuinely conservative. The runtime
uses no model at all.

### RESULT ON THE REPORTED CASE

`"Auswahl beenden"` on the BRONZE cap: **two lines, font 0.082** — an 8.2 mm em, a 5.8 mm glyph,
**13 arcmin at 1.5 m**. The 23-character worst case fits on all three boards (Bronze: 2 lines at
0.058, 9 arcmin). Contact sheet: `.planning/debug/keycaps2/standin_captions.png`, drawn from the
shipped solver's own output — a Python port of the wrap is cross-checked row by row against the wire
suite's dump (279 rows, 0 disagreements) before the script will draw anything.

---

## ITEM 2 — THE SHAPE. A signet plate, and it is what paid for item 1.

> "Rund und Viereckig sind Vorgabe, aber ansonsten darfst du gerne kreativ werden."

Round stays round (rest pads), square stays square (generic). Everything else changed. Outward to
inward the cap is now: vertical **WALL** → 45° outer **CHAMFER** → flat **RIM LAND** at the frontmost
plane → 45° inner chamfer **STEPPING DOWN** → the recessed **FIELD** that carries the symbol and the
caption. It was one flat plateau behind a single 7 mm chamfer.

**WHY A RIM LAND, measured rather than chosen.** `BoardLit` shades by world normal against two baked
studio directions and never reads a scene light. On that surface a 45° chamfer is a FIXED value, not
a highlight that travels — so the strongest "raised" cue available is the largest area that both
faces the viewer squarely and carries the bright bevel tint, which is exactly a flat land at the
front plane. The two chamfers either side give the silhouette its value steps, and the field sits in
a lit frame instead of behind a single edge.

**THE OLD 7 mm CHAMFER WAS ITSELF A DEFECT.** It is a length in METRES applied to caps from
53.2 × 43.9 mm to 63.0 × 62.1 mm — **0.159 of Bronze's short side and 0.113 of Steel's.** One
constant was never one proportion. The bezel is a fraction now:
`CapFaceLayout.BezelChamfer 0.060 + BezelRim 0.045 + BezelStep 0.030 = 0.135` of the cap's SHORT
side. Because the short side is the HEIGHT on all three fitted caps, the field band is **exactly
[0.135, 0.865] on both axes of every board** (horizontally the same absolute bezel is 0.111 / 0.121 /
0.133 of the width — all inside 0.135). One band, six numbers, no per-board case. `SquareCapBevel`
and its mirror `RemoteBoardFurniture.CapBevel` are both **deleted**, and `check-mirrors.sh` lost that
group: 19 → **18**.

**THE FACE BUDGET.** Caption band `v 0.135 .. 0.585` (0.45 of the cap height), gap, symbol band
`v 0.615 .. 0.865` (0.25). The caption grew 0.36 → 0.45; the symbol shrank 0.32 → 0.235 of its cell.
On Bronze that is 19.8 mm of caption band where 15.8 mm could not hold two lines at the old floor —
**one line-count, and it is the whole difference between "Auswahl beenden" and "AUSWAHL BEEN".**
The caption box also stopped being **wider than the plate it sits on**: `(0.92, 0.85)` was 0.92 of
the cap WIDTH, so the old caption overhung the chamfer on both sides and floated past the cap's own
edge — visible in the user's screenshot. It is the field now, on both axes.

**THE COST, STATED.** Round 1's render sheet already found the square caps' symbols to be "a 3–4 px
dark smudge — present, not identifiable" at the across-the-table 32 px view. A 12 % smaller symbol is
12 % worse there. The requirement the user actually stated is that the TEXT is always readable, and
it wins.

### THE TANGENT PROPERTY — preserved, not recomputed

`CardMesh.KeycapTangent` writes `(1, 0, 0, -1)` on every vertex and that is EXACT only while `u` is a
pure function of object `x` and `v` of `y` on every vertex. **The signet profile keeps the property
exactly**: every ring is a rectangle (or a circle) in XY and every vertex is UV'd through the one
planar `Uv(p) = (p.x/width + 0.5, p.y/height + 0.5)`. Nothing needs a per-vertex basis, and
`RecalculateTangents` remains the wrong answer for the same reason as before (the UV gradient is
degenerate on the walls).

**And the render station now proves the stream reaches the pixel.** Its normal-map A/B used to report
`0 tangents` and argue about what the API might bind. It now reports **72 of 72 tangents** on the
square cap and **642 of 642** on the round one, and `_NormalStrength` 1 vs 0 moves **mean 0.053, max
0.298** over 37 755 cap pixels — the bump reaches the picture through an authored basis.

### COUNTS AND THE ROUND CAP

Square signet cap: **72 verts, 36 tris, submesh index counts [6, 72, 30]** (was 40/20/[6,24,30]).
`BoardButton.LogCapDiagnostics` and the render station's ported invariant both assert the new five
numbers, and the station reports **MATCH**.

The ROUND board caps became signet plates too — `CardMesh.BuildRoundKeycap`, 642 verts / 640 tris,
the **same three submeshes** as the square cap, so both shapes read as one family and the
field/bezel/wall materials drive either. It is a NEW mesh rather than a change to `BuildRoundCap`,
because the plain disc still ships three other places (the cap's own backing ring on both boards, and
the map room's button rail) and every one of them assigns a single `sharedMaterial` — giving the
shared disc three submeshes would have left two of them with a null material on furniture nobody was
looking at this round.

---

## ONE FIX OUTSIDE THE OWNED SET, declared

`Net/RemoteBoardTooltip.cs:210` also set `TextOverflowModes.Truncate`, on a frame whose own comment
says *"the FRAME fits the text, never the text the frame"*. Truncate there could only ever fire on a
tooltip whose sizing had gone wrong, and it would hide that by dropping the tail mid-word. Changed to
`Overflow` (one line plus a comment) so the sizing defect shows as text past the frame and gets
reported. `Ellipsis` is deliberately NOT linted: three labels use it, and it drops characters while
PRINTING A MARK saying so — a screenshot of it is a report rather than a mystery. Truncate is the one
that lies.

---

## ITEM 3 — A REAL, UNIQUE TEXTURE PER BOARD, and the colour that was doing the actual harm

> "Die Textur die dort gewählt ist, ist einheitlich und passt sonst nicht wirklich zum Styl.
> **Generiere eine echte unique Textur für die buttons mit gpt-image-2.**"

### THE PLATES — gpt-image-2, seven images for three surfaces

| board | shipped (round 1) | round 2 | what it is |
|---|---|---|---|
| Oak | flat sawn plank, even grain | 1 discarded, **2nd kept** | quarter-sawn oak: ray fleck, a knot, waxed sheen |
| Steel | smooth grey mottle | **1, kept** | cold-blued steel: temper bloom, draw-file scratches |
| Bronze | fine even patina speckle | 3 discarded, **4th kept** | hand-planished bronze: hammer facets, verdigris only in the low facets |

The images are 1024² (all seven returned exactly 1024×1024 — the tool's 16:9-into-3:2 rescale trap
did not fire, and the dimensions were read back with PIL rather than assumed), saved as
`unity/board-prep/out/keycap2_plate_*.png`; the kept ones are copied to
`keycap_plate_{oak,steel,bronze}.png` and the round-1 originals are preserved beside them as
`*_r1.png`, so the "before" column of every sheet is the real shipped surface and not a memory of it.

**THE PROMPT LEVER THAT MATTERED IS ARITHMETIC, NOT TASTE — and it is the reusable finding of this
item.** `material_cell` crops 62 % of the plate into a 256-texel cell and the cap is ~96 px on the
rig, so a feature *f* texels wide in the 1024² plate arrives at **f × 0.151 screen pixels**. Round 1's
prompts asked for MATERIALS and never for a FEATURE SIZE; every one of them came back as micro-mottle
that averages to a flat field at cap scale — which is exactly what "einheitlich" describes. These
prompts size each named feature as a fraction of the frame (ray fleck at 1/5, temper bloom at 1/6,
planishing dishes at 1/6). The prompts are now constants in `unity/board-prep/buttons/plates.py`
rather than living only in a transcript, which round 1's were not.

**AND THE PROMPT RECORD IS HONEST ABOUT ITS OWN HOLE.** `plates.py` originally claimed the shipped
prompts were in the file; they were not — the file's narrative described an earlier iteration ("five
images", `oak` and `bronze_b` kept) while its own `CHOSEN` dict correctly named `oak_b` and
`bronze_d`, and `PROMPTS` held only the superseded asks. That is the "an audit is a snapshot" shape
exactly: a record that reads perfectly and is no longer true. It is corrected — the narrative matches
the seven files, every discard carries its reason and its measured STORY ratio, the two revised asks
whose exact wording was NOT captured are recorded as `SHIPPED_ASK_SUBSTANCE` and labelled as
substance rather than as prompts, and a `_check_manifest()` runs at import and RAISES if any
generated plate is unaccounted for or is listed both ways. The guard was driven negative before it
was believed: removing one discard from the table makes it raise by name.

**THE CONTRAST THE USER IS ACTUALLY COMPLAINING ABOUT IS THE LOW-FREQUENCY ONE.** A single σ over a
cap-sized crop mixes two different things — the fine GRAIN, which the shipped plates already had,
and the large-scale STORY (figure, facets, blooms) whose absence is what "einheitlich" means. Split:

| board | total σ before → after | **STORY** before → after | grain before → after |
|---|---|---|---|
| Oak | 11.89 % → **14.10 %** | 2.98 % → **4.78 %** (+60 %) | 11.11 % → 12.46 % |
| Steel | 16.29 % → **20.12 %** | 5.82 % → **9.97 %** (+71 %) | 13.42 % → 14.58 % |
| Bronze | 10.61 % → **16.03 %** | 3.43 % → **8.46 %** (+147 %) | 9.25 % → 12.10 % |

Every plate is re-based through the EXISTING `cap_atlas.normalise_plate` (imported, not
re-implemented) to the shipped grain's mean of 0.837 — because `BoardLit` computes
`alb = tex2D(_MainTex, uv) * _Color` and a keycap texture is therefore a MODULATOR, not a colour.
That is the ModBuild 281 finding and the one that produced the "invisible button" shape.

### THE COLOUR — per board, because a plate alone could never have fixed it

ModBuild 281 measured the three idle faces at ΔE **13.8 / 10.3 / 3.7**, reported the last as an open
limitation, and named the lever it would not pull: the idle face colour, *"which is the user's
tuning"*. **His report is the authorisation.**

**`IdleColor` was `(0.600, 0.510, 0.350)` on all three boards. It is now:**

| board | old | **new** | rendered face | L\* | C\* | h |
|---|---|---|---|---|---|---|
| Oak | (0.600, 0.510, 0.350) | **(0.550, 0.514, 0.564)** | (0.239, 0.211, 0.131) | 22.73 | 14.0 | 92° |
| Steel | (0.600, 0.510, 0.350) | **(0.407, 0.541, 0.607)** | (0.149, 0.195, 0.225) | 19.93 | 7.0 | 247° |
| Bronze | (0.600, 0.510, 0.350) | **(0.753, 0.471, 0.224)** | (0.317, 0.189, 0.060) | 23.32 | 28.7 | 66° |

A cap with **no** board (the map room's keycap-skinned furniture) keeps the original parchment.
`PlayTray.BoardIdleColor` is the single definition and the peer mirror now **calls** it instead of
copying it — one mirrored constant fewer, on the one value that just went three ways.

**The three were SOLVED, not chosen**: over a hue × chroma grid, maximising the minimum pairwise ΔE
subject to (a) each cap's rendered LUMINANCE staying within ±2 % of today's — so nothing gets
brighter or darker, only differently coloured — (b) under 0.5 % clipped texels, and (c) staying
inside the board's own authored palette band.

| pair | ΔE before | ΔE after |
|---|---|---|
| oak ↔ steel | 12.4 | **20.8** |
| steel ↔ bronze | 9.5 | **35.8** |
| **oak ↔ bronze** | **2.8** | **17.2** |

Luminance moved −0.27 % (oak), +0.05 % (steel), −0.28 % (bronze) against the caps on the board
today. **Zero texels clip**, and not by luck: the brightest channel an admissible face can reach is
`IDLE_MAX × BoardCapTint × shade = 0.415`, and `normalise_plate` clips the plate to [0, 1].

**A CHROMA BOOST WAS TRIED IN ROUND 1 AND REJECTED ON MEASUREMENT** (ΔE 3.7 → 3.9 → 3.1 for
k = 1.0 … 2.6, 99.99 % of oak's texels clipped at k = 1.6). It is not re-proposed.

### THE INSTRUMENT WAS VALIDATED BEFORE ANY OF THOSE NUMBERS WERE BELIEVED

Fed the ROUND-1 plates and the shipped single idle colour, the ΔE model reproduces the ModBuild 281
record's **13.8 / 10.3 / 3.7 to within 0.04 ΔE** — and in doing so identifies what those recorded
numbers were measured on: **the ALBEDO PRODUCT, not the shaded framebuffer value.** The shaded form
is a uniform 0.897× of it — one scalar, applied to all three styles alike, so it cannot reorder the
pairs — and everything above gates on the shaded form, which is stricter. NULL control: each style
against itself, ΔE 0.000000.

**AND ONE NUMBER IS EXPLICITLY NOT CLAIMED.** This instrument's WELL comes out 1.19–1.29× darker
than the 281 record's; four well definitions were tried and none reproduces that record's 1.75×. So
the ABSOLUTE cap-to-well ratio is not asserted here. What IS gated is each new cap against the SAME
board's current cap, where the well term cancels exactly — which is the comparison the "invisible
button" defect actually turns on.

### JUDGED AGAINST HIS SCREENSHOT, NOT AGAINST A NEUTRAL PLATE

`plates_on_bronze.png` redraws ONLY the recessed field inside the real well of
`abgeschnitter_text.jpg` — the bevel, the recess wall, the board and the game's own caption are his
own pixels — in four panels: his original, a CONTROL (the round-1 plate through the colour the
screenshot is in, which must be invisible and is), the new material at the old colour, and the new
material at the new colour. `caps_on_his_bronze_board.png` puts the whole signet cap in that well.

### THE CAP IN HIS SCREENSHOT IS NOT IDLE — and that changes what item 3 can promise

Found while validating the composite, not assumed: a card selection was pending in that frame, so the
Confirm cap is wearing the **accent** colour `(0.35, 0.46, 0.28)` (`PlayTray.6.Build.cs:427`), not
`IdleColor`. That is why it reads olive-green. The model predicts the accented face at
**(0.167, 0.186, 0.078)** against the screenshot's measured **(0.172, 0.188, 0.085)** — every channel
within 0.007, on a JPEG of a headset frame, which is a far stronger check on the whole chain than the
ΔE reproduction is.

**The consequence is a limitation and it is stated rather than glossed:** the per-board idle colour
separates the three boards in the RESTING state. The four STATE colours — accent, confirmed,
disabled, dwell — are per ROLE and stay shared, because they are a state signal and a signal that
means something different on each board is a worse control. So while a cap is accented, the three
boards are told apart by their PLATE alone, which is the ±20 % term round 1 measured. That term is
now much stronger (STORY contrast up 60–147 %) but it is not ΔE 17. If he reports the accented caps
as still looking alike, the fix is a per-board accent family and it is the same one-line shape as
`BoardIdleColor`.

### WHAT THIS COSTS, stated

* A high-structure plate competes with the carved symbol. On bronze the check mark is legible at
  reading distance and quieter than it was on the flat plate; at the 32 px across-the-table view it
  is a smudge either way (the ModBuild 281 finding, unchanged) — but the three boards are now
  distinguishable AT 32 px, which they were not.
* Steel's idle face is frankly COOL (hue 247°) and bronze's frankly WARM (66°). That is what buys
  ΔE 17–36 between three keys that sat at 2.8. If either reads as too far on hardware, the three
  values are one edit in `PlayTray.BoardIdleColor` and the old one is written beside them.
* **The exact prompt wording for two of the three shipped plates is gone.** Their intent is
  recorded and the arithmetic that made them work is recorded; the strings are not. Re-rolling oak
  or bronze from scratch means re-deriving the ask, not re-running it.
* Oak's plate carries a KNOT, and on the Confirm cell it lands near the check mark. It reads as
  wood rather than as a defect, but it is a thing in the picture that was not there before.

---

## THE PICTURES

All in `.planning/debug/keycaps2/`:

| file | what it answers |
|---|---|
| `caps_on_his_bronze_board.png` | does the new cap fit HIS board — the same well, his own pixels |
| `plates_on_bronze.png` | the material and colour change isolated, with a must-be-invisible control |
| — | *one fitted term: `NORMAL_MIP_SIGMA = 1.25` (≈ one mip, which the ~2× minification predicts) brings the CONTROL panel's relief from 20.4 % to 10.4 % against the screenshot's own 10.8 %. The same value is used in every panel, so no comparison depends on it, but the absolute relief level rests on a fit.* |
| `plates_before_after.png` | the six plates at the same scale, with the STORY/grain split |
| `plates_deltae.txt` | the whole ΔE derivation, its known-positive validation and its null control |
| `standin_captions.png` | eleven captions × three boards at real cap size, from the shipped solver |
| `<Style>_<Role>_{before,after}_{front,rake}.png` | every role on every board, through the real `BoardLit` |
| `<Style>_<Role>_zoom_{near96,far32}.png` | reading distance and across-the-table |
| `preview-round2.log` | the mesh invariant, the colour calibration and the tangent A/B |

**AND WHAT NO PICTURE HERE SHOWS.** TextMeshPro is not in the companion Unity project's package
manifest, so no station in this repository can render the shipped SDF material. `standin_captions`
is DejaVu Serif driven by the shipped solver's own output (a Python port of the wrap, cross-checked
row by row against the wire suite's dump — 279 rows, 0 disagreements, and the script refuses to draw
if that fails). It is evidence for LAYOUT and for TYPE SIZE; it is not evidence about glyph
rendering, and the runtime does not use its metric model at all.

## STILL OPEN

* **`NetProtocol.CapLabelMaxBytes = 48` still truncates a mirrored caption silently**, on a UTF-8
  character boundary. Every fixed corpus string is under 24 bytes, so it cannot bite today — but the
  interpolated end-turn / discard captions are unbounded and a long class name or card title can
  reach it. It was NOT touched: it is a wire constant, and changing it moves the golden vectors.
  Whoever raises it should raise the TLV budget with it (`1 + 4 × (1 + N)` must stay under 255).
* The caption box is EXACTLY the recessed field on both axes, so the longest string's glyphs can
  touch the inner chamfer. A margin would cost the 23-character worst case a font size on Bronze;
  it was left flush deliberately, and it is one number (`CapFaceLayout.CaptionBoxWithSymbol`).
* The three `Ellipsis` labels (wrist HUD peer name, options rows, map-room wrist name) still drop
  characters. They PRINT A MARK saying so, which is a different contract from Truncate, and they are
  not board captions — but if "immer voll lesbar" is meant to cover them too, that is a next round.

---

# ROUND 1 (2026-08-25): WHAT WAS BUILT, AND THE TWO PREMISES THAT WERE WRONG

The design below was written before the work started and is kept verbatim. This section is the
RECORD: what shipped, what the design got wrong, and what is still open.

## THE PREMISE THAT WAS FALSIFIED — "a TMP mesh using BoardLit"

The design's TECHNICAL CRUX specifies engraved text as *"a TMP mesh laid flat ON the board surface
… using the board's own lit material family (`BoardLit`) rather than a UI shader"*. **That cannot
be built, and the reason is not a difficulty, it is an impossibility.**

* A TMP glyph is not a shape. It is a **signed distance field** in the font atlas, decoded by the
  TMP shader's own smoothstep against a per-glyph gradient scale. Any shader that samples that
  atlas as an ordinary texture — `BoardLit` included — draws a grey blur, not a letter.
* `BoardLit` is strictly **opaque**: `Tags { Queue=Geometry }`, `return fixed4(col, 1.0)`, no blend
  state and no cutout. It has no way to leave the board showing between the strokes.

The design's own fallback (shallow recesses in `gen_board.py`) was closed this round by the lane
split — the board-texture lane owns that file and the three FBXes.

**What was built instead**, `src/GloomhavenVR/Cards/BoardEngraving.cs`: the carve is done by the TMP
distance-field material itself, with the recipe **inverted** from the one the keycap labels wear.
A cap label sits PROUD on a key, so it is lit on top and drops its shadow down-right — bright
parchment fill, dark keyline, dark underlay offset down-RIGHT. A board label is CUT IN, so the
inside of the stroke is in shadow and the far lip catches the light — dark fill the colour of that
board's own material in shadow, a darker keyline for the shaded wall, and a LIGHT underlay offset
down-LEFT for the lit lip. Same machinery, opposite physics.

**The light direction was read off the shader, not assumed.** `BoardLit`'s baked key is
`normalize(0.35, 0.85, -0.45)` in world space: above, toward the viewer, slightly right. On a board
facing the player that puts the lit lips of an incision on the BOTTOM and LEFT of every stroke,
which is why the underlay is offset down and left and the cap labels' shadow is offset down and
right. They are visibly consistent with the one light the board is actually under.

## THE OTHER CORRECTION — the plate was removed from BOTH boards, and the first fix was right

`Net/RemoteStatusReadouts.cs` records defect (c) of the 1:1 round: *"die Runden-Anzeige sitzt auf
einem grauen Kasten, den der Besitzer nicht hat"*. The answer THEN was correct — the owner did have
a plate, and parity meant building the owner's plate rather than deleting the peer's. The user has
now ruled on the plate itself (*"Das gilt übrigens auch für den Rundentext"*), so it is deleted from
the owner's board and from the mirror in one change. There is nothing drawn behind the glyphs on
either board any more, so the two can no longer disagree about what that something looks like.

## THE DECISIONS AS SHIPPED

| control | cap carries | board carries |
|---|---|---|
| Confirm | bold check mark **+ the live game text** | — |
| Undo | counter-clockwise return arrow **+ the live game text** | — |
| Skip | two triangles against an upright bar **+ the live game text** | — |
| Item use | open hand with rays **+ the live game text** | — |
| Short rest | the SHORT-REST PAD's own crescent-and-embers stencil | engraved "KURZE RAST" above the pad |
| Long rest | the LONG-REST PAD's own spoked wheel | engraved "LANGE RAST" below the pad |
| Fixed / Folgen | an anchor / two footprints, swapped live | engraved "FIXIERT" / "FOLGEN" above the toggle |
| Round readout | — | engraved into the board, no plate |

The generic four keep their text because the caption is the only thing that says what THIS press
will do — it changes every pick and no symbol can carry it. The rest pads and the toggle mean the
same thing for ever, so their caption is a one-time teaching aid, and a teaching aid belongs on the
board where it teaches once and then stops competing with the cap for attention. A player who knows
neither the symbol nor the game still learns it: the word is engraved directly beside the pad.

The two rest symbols are **not merely similar to the pads beside them, they are the pads' own
stencils**, taken from the sheet that style's board took them from — so the pairing is exact rather
than evocative, and it cost no generated image.

## THE MECHANISM — one atlas per board, one cell per role

`unity/board-prep/buttons/` builds `Keycap{Oak,Steel,Bronze}_{albedo,normal}.png`: a 4x4 grid of
256-texel cells, one per `CapRole`. `BoardLit` already runs `o.uv = TRANSFORM_TEX(v.uv, _MainTex)`
and samples `_BumpMap`/`_MRSMap` with that same `i.uv`, so a material's `mainTextureScale/Offset`
picks the cell for every map at once. A role is therefore two floats, not a texture — which is what
makes the follow/pin toggle's live symbol swap free, and what keeps three styles times seven roles
down to six files.

**The symbol is CARVED, not raised, and that is the round-2 finding cashed.** On this shader the
specular is a bevel term, not a surface term: `_SpecStrength` 0 vs 0.85 moves the flat-on mean by
0.001. A raised symbol reads by a highlight this surface cannot deliver; a recessed one reads by
ambient occlusion baked into the albedo, which is view-independent. See
`unity/board-prep/buttons/README.md`.

## THE MIRROR, AND WHY IT CANNOT DRIFT

Every cap material on both boards is minted by ONE call — `PlayTray.NewKeycapMaterial(shader,
colour, role, style)` — which the peer mirror already called before this round. The peer's board
STYLE is `RemoteBoardTuning.Style`, already on record 28 because the board prefab is chosen from it,
so a bronze player is drawn with bronze keys carrying the bronze board's own plaited crescent with
**no new wire field**. The engraved captions are cut, placed and lettered by the owner's own
`BoardEngraving`, whose caption offsets live in that one class rather than as a mirrored pair.

**A mirror group was DELETED, not added.** The press spring used to be an inline `Time.deltaTime *
6f` on the owner's cap and a named `RemoteCapFx.PressDecayPerSecond = 6f` on the peer's. Both sides
now advance a phase in seconds and call `WorldUI.ButtonStroke.Depth01`, so the mirrored press IS the
owner's press. (`check-mirrors.sh` still reports 19 — that pair was described in its prose and was
never one of the nineteen machine-checked groups, because the local half was not a named constant.)

## THE PRESS STROKE

`WorldUI/ButtonStroke.cs`: attack 35 ms (ease-out onto the bottom), detent 30 ms, spring-back 120 ms
with a 10 % overshoot past rest. It replaced an instantaneous drop with a linear 6/s decay — a cap
that teleported to the bottom in one frame and rose back at constant speed.

**Its first draft could not overshoot at all.** The release leg was a decaying sine added to an
ease-out, and the arithmetic says such a sum never crosses rest: the ease dominates while the
oscillation is largest, and the oscillation has died by the time the ease has not. The comment would
have described motion that was not there. It is two explicit smoothsteps now, and
`tests/GloomhavenVR.WireTests/BoardCapSymbolVectors.cs` asserts the crossing count, the peak and the
endpoint.

**It runs on the UNSCALED clock now, on both sides.** The old spring used `Time.deltaTime` and the
mirror's comment defended that explicitly. The game stops simulation time behind menus and dialogs
and during card phases — which is exactly when a player presses board buttons — so a scaled stroke
froze the cap at whatever depth it had reached. Same defect the surface-fade watchdog exists for,
one animation over.

## THE DEFECT THE RENDER CAUGHT — a keycap texture is a MODULATOR, not a colour

Worth recording because nothing in the build would have said so, and because the guard that
exists for exactly this shape could not see it.

`BoardLit` computes `alb = tex2D(_MainTex, uv) * _Color`. A keycap texture therefore MODULATES the
state colour rather than being a colour in its own right, and the one it replaces —
`KeycapGrain_albedo.png` — is a near-white greyscale grain with mean **0.837**, i.e. a modulator
that passes the state colour through almost untouched. The generated material plates are
photographs of materials, means **0.27–0.42**. Dropped in unchanged, the rendered cap face went
from **1.75×** the luminance of the mod-drawn WELL it sits in to **0.95× (oak), 0.85× (bronze) and
0.57× (STEEL)** — from clearly proud of its own recess to level with it or darker.

That is precisely the "invisible button, only the text still visible" shape
`WorldUI.ButtonTuning.SeatedCapColor` was written for after a hardware report — **and the seat
floor cannot see it.** It floors the material's `_Color`, and `_Color` had not moved: the
darkening arrived in the TEXTURE, a term that did not exist when that guard was written.

**What caught it was the render sheet**, which showed the steel caps reading nearly black; the
measurement is what turned "looks dark" into a cause. `cap_atlas.normalise_plate` now re-bases each
plate to the shipped grain's mean before anything is carved into it, with a uniform RGB gain (hue
ratios preserved exactly) and a soft knee at 0.80 so bright grain does not clip flat. The caps come
back at **1.74–1.81×** their well — the shipped ratio — with their own colour casts and structures
intact (oak 1.21:1.14:0.65, steel 1.00:0.98:1.01, bronze 1.13:1.11:0.76).

### AND THE LIMITATION THAT LEAVES, measured rather than glossed

With the level correct, the three IDLE cap faces render at CIELAB **ΔE 13.8 (oak↔steel), 10.3
(steel↔bronze) and only 3.7 (oak↔bronze)**. Steel is clearly a different material; oak and bronze
are within the distance at which two colours read as the same colour under different light, and are
told apart by their GRAIN STRUCTURE rather than by their hue.

The cause is not the re-basing (a uniform gain cannot change a hue ratio) but the STATE COLOUR: the
idle face is the parchment `(0.60, 0.51, 0.35)` × the shipped `[ButtonColors] BoardCapTint` of 0.5,
a strong warm tint applied identically on all three boards, and a material whose own cast is ±20 %
cannot survive being multiplied by it. **A chroma boost was tried on paper and rejected on
measurement**, not on taste: it does not separate oak from bronze at any gain (ΔE 3.7 → 3.9 → 3.1
from k = 1.0 to 2.6, because the separation lives in the blue channel, which is already crushed) and
it clips 99.99 % of oak's texels at k = 1.6.

The lever that WOULD separate them is the idle face colour itself, which is `[ButtonColors]` and is
the user's tuning. It was not touched.

## THREE MORE DEFECTS, ALL FOUND BY THE RENDER STATION AFTER THE FIRST REPORT

**1. THE KEYCAP MESHES WRITE NO TANGENTS, AND `BoardLit` BUILDS ITS WHOLE BASIS FROM THEM.**
Measured: `mesh.tangents.Length == 0` on both `CardMesh.BuildBeveledKeycap` and `BuildRoundCap`,
while the shader does `o.wt = UnityObjectToWorldDir(v.tangent.xyz)` and
`o.wb = cross(o.wn, o.wt) * v.tangent.w`. With no TANGENT stream bound that is whatever the
graphics API supplies for a missing vertex attribute — undefined, and not guaranteed to agree
between the editor's GL and the rig's D3D11. It predates this round and survived because the only
map bound was a low-contrast shared grain; this round binds a per-board map whose carved symbol is
the entire point. `CardMesh.KeycapTangent` now writes an explicit `(1, 0, 0, -1)` on every vertex,
which is exact rather than approximate: both meshes map UV as a pure function of object XY, so the
direction of increasing u is `+X` everywhere and `w = -1` is what makes `cross(n, t) * w` come out
`+Y` on the front-facing plateau. `RecalculateTangents` was the obvious alternative and is worse —
it solves from the UV gradient, which is degenerate on eight of the twenty triangles.
**The picture does not change in the editor**, which is the point: it was working by luck in one
API and had no guarantee in the other.

**2. THE ENGRAVING PALETTE WAS DERIVED FROM THE WRONG SURFACE.** The three colours were taken from
the KEYCAP material plate, on the reasoning that a board and its keys are the same material family.
For oak and bronze that is very nearly true (the cap plate is 0.96x and 1.06x the board's own face
band). **For steel it is wrong by 0.68**: the cap is dark blued iron (0.275) and the board's face
is bright brushed silver (0.418). Cut into the real steel board the plate-derived fill would have
landed a 70 % drop where 55 % was intended — black text, not a groove — and the "lit lip" at 0.372
would have been DARKER than the 0.418 board around it, **inverting the one cue that says the mark
is cut in rather than raised.** The palette is now derived from each board's own albedo face band
(`cap_check.py --palette` reads `ref_face_<style>.png`), and `engrave_preview.py` composites onto
that same surface rather than onto a keycap: 49-53 % drop with the lip at 0.97-1.13x on all three.

**3. THE @32 px CONTRAST NUMBER DOES NOT MEAN WHAT THE FIRST REPORT SAID IT MEANT.**
`cap_check.py` reports 25-44 % luminance drop at the across-the-table size, and that was written up
as the symbols surviving. The render says otherwise: at 32 px the square caps' symbols are a 3-4 px
dark smudge — *present, not identifiable*. Both are true, and the instrument is measuring one term
of what the eye needs. A mean-luminance drop over a symbol's footprint says "there is a mark here";
it cannot say "you can tell which mark". The round rest DISCS survive better (bigger symbol, bigger
cap). At 96 px — the board pulled in to read — everything reads clearly, and the MSAA-off column
shows the far-view loss is not the sample count hiding it.

**AND ONE CORRECTION TO THE PRESS STROKE'S CLAIM.** Measured off the rendered frames, the full
4 mm press moves the cap silhouette 6 px in a 360 px frame; the 0.4 mm rebound moves it 1 px.
Scaled to the real viewing sizes that is 3.5 px of press and 0.35 px of rebound at 0.5 m. **The
rebound is sub-pixel at every distance a player will use.** What the stroke actually buys is the
attack and the detent — 35 ms of ramp instead of a one-frame teleport, i.e. two or three frames of
visible motion at 72-90 Hz. The overshoot is a feel detail carried by timing, not something anyone
will see, and it should not be described as though it were.

## WHAT IS STILL OPEN

* **The engraved text has not been seen on hardware.** TextMeshPro is not in the companion Unity
  project's package manifest, so no station in this repository can render the shipped TMP material.
  What was rendered is a three-layer STAND-IN (legacy `TextMesh`), and every such file is named
  `standin_*` for that reason. It is evidence for LAYOUT, COLOUR and DEPTH BEHAVIOUR, not for glyph
  rendering. The depth-honesty claim rests on arithmetic and on the shader, not on that picture.
* **No new config dial was added, deliberately.** The engraving palette, the caption boxes and the
  stroke timings are authored constants. Adding dials would have meant `Defaults` entries, DE+EN
  `Loc` name and description pairs, `ConfigSteps` entries and wire coverage for each — and a second
  lane was appending to `Loc.Config*.cs` and `ConfigSteps.cs` in the same round. If the user wants
  to tune the engraving, that is the next round's work and it is cheap: every value is already a
  named constant in one class.
* **No new `Loc` key was added either.** The engravings reuse `Loc.Mod("short_rest")`,
  `Loc.Game("GUI_LONG_REST")`, `Loc.Mod("follow")` and `Loc.Mod("pinned")` — the exact strings the
  caps used to carry — upper-cased in the presenter. `Loc.Mod` has no fallback, so every new key is
  a chance to render a raw key on the board; there was no wording that needed one.
* The `[ButtonColors] Label*` dials still style the CAP labels only. The board engraving has its own
  palette (measured from the generated plates) and deliberately does not read them: a keyline tuned
  to read on a bright brass key is the wrong keyline for a cut in dark wood.
* Two spare symbol cells were generated and are unused (`C1` a check inside a ring, `C2` a plain arc
  arrow, `B1` a phial as an alternate item-use device). They are insurance, already paid for.

---

# THE BOARD BUTTON OVERHAUL — THE DESIGN AS IT WAS DECIDED (kept below as written, corrections above)

User request, 2026-08-25. Not yet implemented; two lanes were in flight on the same files when it
was written (the button-group unification, and the board-texture side/back round). **Read this
before starting, and read `BOARD-REBUILD-HANDOVER.md` for the board pipeline itself.**

## WHAT HE ASKED FOR

> "Statt einfach nur Text, möchte ich ein Symbol (und Text dazu), aber der Text soll sich in den
> button nativ einfinden. Pro Board soll es auch ein anderes passendes Aussehen der buttons sein,
> das zu dem board und seinem Aussehen passt. Nutze hierbei auch die Hilfe von gpt-image-2 …
> Die Rast buttons sollen weiterhin Rund sein (auf der linken Seite des boards) und die generischen
> buttons auf der rechten Seite (viereckig). Aber auch Buttons wie 'Fixed' soll zum board passen und
> immersiv und gut aussehen. Auch Drück-Animation wenn der button nach unten gedrückt wird soll gut
> funktionieren. Die 'Verschwinden' und 'Auftauchen' Animation kannst du beibehalten."

And, when asked whether every cap needs a caption:

> "Entscheide das selber pro Fall. Versetze dich in einen Spieler der die Symbolik und eventuell auch
> das Spiel noch nicht kennt. Es soll klar sein was die buttons bedeuten. Eventuell kannst du auch
> Text dynamisch in das board mit einarbeiten? Zb über dem long-rest button. Wichtig ist hierbei nur
> das a) es lokalisiert sein kann (deutsch, englisch) und b) es nativ und immersiv in dem board
> verarbeitet ist, nicht einfach als schwebender Text darüber. Das gilt übrigens auch für den
> Rundentext … Die generischen Buttons haben immer unterschiedlichen Text darauf, d.h. auf denen
> sollte der Text auch erhalten bleiben, da es sich immer ändert."

## THE CONSTRAINT THAT SHAPES EVERYTHING — measured, not assumed

**The generic caps' labels are LIVE GAME TEXT, not a fixed set.** `CardsGameApi.PickDialogOptionLabel`
(`CardsGameApi.cs:533-555`) reads the caption straight off the option button a 2D player would
click — `InputButton.ExtendedButton.buttonText` — so it is whatever the game chose for this pick,
in the player's language: *"Karten abwerfen"*, *"Wähle eine andere Karte"*. It is also mirrored to
peers (`NetProtocol.ExtIdCapLabels`) so a teammate reads the same wording.

Therefore:
* **A SYMBOL may be a generated texture.** Symbols belong to the ROLE, and the roles are fixed.
* **TEXT MAY NEVER BE BAKED INTO AN ATLAS.** It changes at runtime and with the language. Any
  design that paints a caption into a texture is wrong the moment the dialog changes or the user
  switches to English.

## THE DECISIONS — per control, decided for a player who knows neither the symbols nor the game

| control | shape / side | cap carries | board carries |
|---|---|---|---|
| Confirm | square, right | **symbol + the live game text** | — |
| Undo | square, right | **symbol + the live game text** | — |
| Skip | square, right | **symbol + the live game text** | — |
| Item use | square, right | **symbol + the live game text** | — |
| Short rest | round, left | **symbol only** | engraved caption beside/above the pad |
| Long rest | round, left | **symbol only** | engraved caption beside/above the pad |
| Fixed / FIXIERT | its own | **symbol only** | engraved caption |
| Round readout | — | — | **engraved into the board frame**, no backing plate |

**Why the generic four keep their text:** the caption is the only thing that says what THIS press
will do, it changes every pick, and a symbol cannot carry it. His instruction is explicit.

**Why the rest pads and Fixed do not:** their meaning never changes, so the caption is a one-time
teaching aid — which is exactly what an engraved board label is for. It teaches a new player once
and then stops competing with the cap for attention. The board already carries the motifs: the
crescent-and-flames pad and the wheel pad are on all three boards. The cap symbol should echo the
pad it sits beside, so the pairing is self-explanatory.

**The round readout** is a floating plate today (`RemoteStatusReadouts.cs:76` records that its grey
backing plate was itself a reported defect). It becomes carved frame text.

## THE TECHNICAL CRUX — engraved text that is still localized

Carved-looking text that must change at runtime cannot come from the atlas. Preferred route, and it
needs no mesh change:

* A TMP mesh laid flat ON the board surface, inset slightly along the board normal, using the
  board's own lit material family (`BoardLit`) rather than a UI shader, so it takes the same light
  and the same per-board material response.
* The carved read comes from a generated **gutter/AO strip** under the glyphs — a small generated
  texture, per board style — plus the inset. That is the part gpt-image-2 is for.
* **Verify it is depth-honest and does not z-fight** at the shipped board tilt and at the zoom
  range the player actually uses. A label that shimmers is worse than a floating one.

Fallback if the inset cannot be made to read: cut shallow recesses for the fixed-position labels in
`gen_board.py` and inset the TMP into them. This changes the mesh, so it must keep every seat, pad
and slot anchor bit-identical — they are on the wire-test harness
(`tests/GloomhavenVR.WireTests/BoardSeatVectors.cs`).

**Localization:** every engraved caption goes through `Loc` like any other string. `Loc.Mod` has NO
fallback — a missing key renders as the raw key — so each caption needs its DE/EN pair added.
Layout must survive the longer of the two: "Kurze Rast" against "Short Rest", "Lange Rast" against
"Long Rest".

## PER-BOARD LOOK
The caps today share ONE material for all three boards — `KeycapGrain_albedo.png` /
`KeycapGrain_normal.png` with `BoardLit` (`PlayTray.6.Build.cs:820-821`). That is why they look
identical everywhere. Split it per style: carved oak, forged steel, cast bronze with verdigris,
matching each board's own atlas. Same treatment for the symbol set and the engraving gutters.

## WHAT ALREADY EXISTS AND MUST NOT BE REINVENTED
* **The press animation exists**: `PlayTray.BoardButton._press` decays at 6/s, ~170 ms, and it is
  MIRRORED TO PEERS — `BoardCapPress` publishes it in five bits of `ExtIdHalfHover` because at the
  5 Hz extras cadence an event that short falls between packets. Improve the feel; do not rebuild
  the mechanism, and do not break the wire contract.
* **The appear/disappear dust dissolve stays** — his words, explicitly.
* Two press commit points report: `PlayTray.BoardButton.Press` and
  `WorldUI.ButtonCluster.PhysicalButton.Fire`. If the button-unification round removes the second,
  the surviving one must still report.

## SEQUENCING — why this was not started immediately
The button-group unification lane owns `PlayTray.*`, `ButtonCluster`, `CardsConfig` and `Defaults`;
the board-texture lane owns `unity/board-prep/` and REBUILDS THE BUNDLE. Two lanes rebuilding
`prebuilt/gloomhavenvr.bundle` conflict unconditionally — it is a binary file and there is no merge.
Start this only after both have landed, and inherit their result: the unification moves the caps
into the three mesh seats and collapses the per-board offsets onto the mesh anchors, which is the
structure these visuals attach to.

## IMAGE BUDGET
gpt-image-2 is REQUIRED for the symbol set and the material/gutter textures, not optional — the user
has said twice that handling it responsibly does not mean avoiding it. Prepare on paper first, then
generate what the job needs, and report the count.
