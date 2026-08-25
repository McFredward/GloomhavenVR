# ROUND 6 (2026-08-25): THE NOISE WAS NEVER IN THE COLOUR, AND THE ROUND CAP WAS WEARING CELL 0

**The first round the user has said he likes.** Verbatim:

> "Zu den Buttons: Sie gefallen mir schon richtig gut! Deutlich besser als zuvor. ABER alle
> Buttons sind so extrem rau, dass es schon fast wie Noise erscheint. Siehe noisy_buttons.jpg.
> Das gerne etwas weniger. Weiterhin erscheint mir bei der Textur der runden Buttons es so, als
> sei hier eine Textur für eigentlich einen viereckigen Button genutzt worden. Siehe
> viereckige_texturen.jpg. Weiterhin braucht es noch einen Text 'Lange Rast' und 'Kurze Rast'
> (lokalisiert und im Stil des Boards wie der Rundentext) über dem jeweiligen Button, damit der
> User sicher weiß was das ist. Den Text soll ich jeweils auch verschieben können in den
> Pro-Board-Einstellungen."

So: three corrections to an accepted result, not a fourth restart. Nothing about the design, the
construction or the colour was reopened.

---

## THE BRIEF'S HYPOTHESIS WAS WRONG, AND THE INSTRUMENT THAT SAYS SO IS NEW

The round was briefed on this: ModBuild 291 bought the colour fix partly by raising field
contrast from 4.12 % to 10.69 % on oak — a 2.6× rise — and "it is very likely also the noise".

It is not. **`cap_rough.py` was built first and it falsified the brief before a line of the
remedy was written.** Cap against the board it sits on, both resampled to a Quest 3's arm's-length
2.4 px/mm, band-passed to the 1.5–6 screen-pixel octave that reads as speckle:

| board | ALBEDO grain, cap/board | NORMAL-MAP relief, cap/board |
|---|---|---|
| oak | 3.08 / 3.30 = **0.93×** | 17.70 / 1.11 = **15.9×** |
| steel | 2.84 / 2.77 = **1.03×** | 21.17 / 0.46 = **45.5×** |
| bronze | 2.64 / 1.74 = **1.51×** | 18.22 / 0.34 = **53.7×** |

**The albedo was already at its board's level. The normal map was sixteen to fifty-four times
it.** Oak — the board in both screenshots, and the cap the brief singled out as worst — was
*below* its bar on the term the brief named. Undoing round 5's contrast would have cost the
colour and bought nothing.

Corroborated without this instrument in the loop, straight off the files: the shipped cap map's
field carries per-texel **nx σ 0.39 / ny σ 0.35**, against 0.06–0.11 on all three board maps and
0.07 / 0.20 on `KeycapGrain_normal.png`, the map these replaced. (Raw σ is not comparable across
maps at different texel densities — that is exactly why the instrument resamples first — but in
world slope it is still ~9×, and it is the same finding.)

---

## THE CAUSE: A PINNED FACTOR OF A PRODUCT

`GRAIN_RELIEF_STD = 0.02376` pins the micro-relief's **height** standard deviation. A normal map
carries the height's **gradient**, and slope is amplitude × frequency.

Round 3 pinned the height for a good, stated reason: the registered art carries roughly twice a
swatch's luminance contrast, and leaving `GRAIN_RELIEF` as a gain "would have doubled the bump for
a reason that has nothing to do with how rough the material is". Correct — and it fixed the
amplitude while leaving the **frequency** free. Between ModBuild 286 and 289 the material stopped
being a random crop of a swatch and became a registered photograph at the same 256-texel cell, so
its grain moved up in spatial frequency, and the same pinned height became a much larger slope.
Nothing in the loop was looking at the slope. **Measure the product, not one factor.**

A second, smaller contribution in the same direction, recorded because it is real: `build_style`
writes the normal at half resolution and doubles the strength "so a wall's slope in world terms is
unchanged". Exact for the CARVE, which is a smooth low-frequency field the decimation genuinely
halves. Not exact for the GRAIN, which is high-passed at cell/48 (~5 texels) and which a box blur
of radius 1 barely touches — so the ×2 is very nearly a straight doubling of its slope. Pinning
the slope absorbs that too, which is why it was not fixed separately: one knob that holds the
measured quantity beats two that model it.

### AND A TERM NOBODY HAD NAMED, WHICH IS STRUCTURAL

    board face      1213 / 1246 / 1186 texels per metre
    keycap cell     4547 / 4122 / 5831 texels per metre  (a 256-texel cell over the cap's
                                                          short side: 56.3 / 62.1 / 43.9 mm)

**The cap carries its material at 3.3–4.9× the board's texel density** (2.8–3.6× for the larger
round caps). At equal texture contrast the cap's grain therefore lands far higher up the frequency
axis at the same viewing distance, which is the difference between "wood" and "sandpaper". This is
a property of the asset pipeline, not of one board — all three land within 5 % of each other — and
no amount of colour work would ever have touched it. It is why the albedo remedy is a **low-pass
at the board's own resolution limit** rather than a contrast reduction.

---

## WHAT SHIPPED FOR ASK 1

Two knobs, reported separately because they are separate — the brief was right to insist.

* **`GRAIN_RELIEF_SLOPE`**, per board, replaces `GRAIN_RELIEF_STD`'s role. The RMS of the
  micro-relief's own Sobel gradient — the same operator `tex_common.normal_from_height` applies
  downstream, so what is held fixed is what becomes nx/ny. Per board because the requirement is
  per board: the three faces measure 1.11 / 0.46 / 0.34 % relief and one shared constant left oak
  at 0.31× its own board (glassy, on the board he is actually looking at) while bronze still sat
  over its bar. Each is solved for 0.9× its own board. The solve is linear — halving the constant
  halves the measured relief to within 2 %, checked at 0.002 / 0.0005 / 0.00015 first — so each is
  one measurement, not a search. The shipped rule produced **0.03332**; these are 0.00143 /
  0.00056 / 0.00041.
* **`GRAIN_TEMPER`** (1.00 / 0.94 / 0.45) with **`GRAIN_TEMPER_SIGMA`** (1.88 / 1.66 / 2.46 cell
  texels). A mean-preserving low-pass blend, `lo + keep × (img − lo)`, applied to the material
  **before** the level solve so the gain re-hits `FIELD_TARGET_LUM` on the material that actually
  ships. The cut-off is that board's own resolution limit — half the period the board's face could
  itself have recorded — so what is attenuated is exactly the band the cap has and the board does
  not. **Oak's is 1.00, a no-op**, because oak was already below its bar.

### THE NUMBERS, before → after

Square caps, cap / board grain contrast at 2.4 px/mm:

| board | albedo | relief | **combined** |
|---|---|---|---|
| oak | 0.93× → 0.93× | 15.87× → **0.88×** | 5.16× → **0.93×** |
| steel | 1.03× → 1.00× | 45.53× → **0.91×** | 7.61× → **1.00×** |
| bronze | 1.51× → 1.27× | 53.70× → **1.01×** | 10.36× → **1.26×** |

Round caps: 4.64× → **0.84×**, 8.73× → **1.19×**, 8.26× → **0.87×**.

Across the table (0.8 px/mm): 1.60 / 2.07 / 1.58 → **0.64 / 0.61 / 0.79**.

**Two of twelve still sit above 1.00 and they are not hidden: bronze square at 1.26× and steel
round at 1.19×**, both on the albedo term, both against unusually smooth boards. Pushing them
under would have cost material character — bronze's verdigris mottle is most of what says
"sand-cast" — on a cap that is no longer within an order of magnitude of the complaint.

Per-texel nx σ in the cap field: **0.39 → 0.026** (oak), 0.33 → 0.008 (steel), 0.34 → 0.008
(bronze). The normal maps also got **smaller**, 0.52/0.54/0.53 MB → 0.22/0.16/0.14 MB, because
what was removed was high-entropy noise PNG could not compress.

---

## ASK 2: THE ROUND CAP WAS WEARING CELL 0, AND THIS FILE HAD ALREADY WRITTEN IT DOWN

Submeshes [1] (bezel) and [2] (wall) of **every** cap take `CapRole.Plain` = atlas cell 0, and
cell 0 is built by `cap_object.register_square(..., continue_rim=True)` — the **square**
registration. On a square cap that is exact. On a round cap it paints a square gold band with
mitred corners inside a circular button, which is precisely what the user photographed.

`unity/board-prep/buttons/README.md` had recorded it as an accepted cost:

> "One cell cannot serve both shapes' bands ... so it is exact for the square cap and fills its
> unused interior with rim-land material for the round cap's benefit."

One cell cannot — **and the atlas has seven spare cells, so it never had to be one cell.**

**Cell 9 = `CapRole.PlainRound`**, `register_round(..., continue_rim=True)`. It needs no angular
sweep and is four lines where its square sibling is forty: a round cell's distance-to-outline IS
its radius, the bands ARE annuli, and a round cap's bezel reads only `b ≤ 0.135`, so the unread
interior is simply faded to the rim band's own mean. The square version's whole complexity — the
angular sweep that replaced a nearest-edge push, after the push produced four triangular sectors
meeting in seams along the diagonals — exists because a square cell is anisotropic and *the
diagonals were exactly where a round cap sampled*. That reader now has its own cell.

**And the round plate's own bands land where the mesh's are**, which round 3's `register_round`
docstring bet on and nobody had checked. `cap_object.py --cells` on the new cell: oak's brightest
ring at d = 0.074 and darkest inner ring at 0.129, against a rim land of 0.060-0.105 and an inner
chamfer of 0.105-0.135; bronze 0.074 / 0.152. So no radial correction is needed and none was
added. Steel reports its brightest ring at d = 0.004, which is NOT a registration error: its band
means are 0.986 / 0.958 / 1.050 / 1.003, i.e. a nearly flat profile on a dark weathered-iron disc,
and the extremum of a flat profile is noise. An instrument's argmax on a flat input means nothing
and is reported here rather than quoted as a defect.

**The field/bezel seam on a round cap is now exact by construction.** Cells 5, 6 and 9 are all
`register_round` of the SAME crop under the SAME solved gain, and the interior wash on cell 9 does
not begin until d = 0.18 — well past the bezel's inner edge at 0.135. So the two submeshes' textures
are literally the same texels where they meet. The square path cannot say that.

### THE REPRODUCTION, AND WHY IT COULD BE TRUSTED

Before changing anything, `cap_atlas.py` was re-run from its inputs and the six shipped PNGs came
back **byte-identical (md5)**. So the defect was reproduced from source rather than inferred from
a screenshot, and the `--no-objects` path the round-4 note describes is not what built the shipped
atlas (see the corrections below).

### AND ROUND 4'S WINDING FIX DOES REACH WHAT RENDERS

`cap_onboard.check_winding` on the exported round meshes, which now go through this renderer:
oak's three submeshes give signed volumes **+2.86e-06 / +1.24e-05 / +2.38e-05**, all positive,
UV and geometry agreeing in sign on every one. The 384-of-640 inversion is gone from the mesh that
actually draws. The reason the brief suspected otherwise — "the round seat at the top of the board
looks EMPTY" in `noisy_buttons.jpg` — is a different thing entirely: that is the CONFIRM seat with
its cap hidden, not a rest pad.

---

## ASK 3: THE CAPTIONS ALREADY SHIPPED, AND THEY DO NOT RENDER

This is the round's largest correction to its own brief. `RestControls.EnsureBuilt` has built
`_shortCaption` / `_longCaption` through `BoardEngraving.Create` since **ModBuild 281**;
`RefreshLabels()` sets them to `Loc.Mod("short_rest")` / `Loc.Game("GUI_LONG_REST")` upper-cased —
i.e. **"KURZE RAST" and "LANGE RAST" in German, already** — and `SetOffset` places each at its own
disc's clamped pose ± `BoardEngraving.RestCaptionOffsetY`. The peer mirror builds the same two from
the **viewer's** own `Loc`, deliberately, and they do not ride
`NetProtocol.CapLabelMaxBytes = 48`: that record (ExtId 13) carries only Confirm / Skip / Undo /
ItemUse. So the answer to the brief's wire question is **no, these captions do not ride that path
and nothing is truncated peer-side**.

They are simply **not on the board**. From the user's own ModBuild 291 run, the same session as
his screenshots:

* `RestControls: built 2 ROUND rest button(s) for Oak (size 0.074×0.074 m …)` — the discs exist;
* `Rest buttons visibility: short=True … long=True` — so the captions are `SetActive(true)`;
* `BOARD ENGRAVING (Steel): …` fires — so `BoardEngraving.Restyle` ran and the material recipe
  was applied;
* and a high-pass of `viereckige_texturen.jpg`, which resolves every millimetre of board relief,
  shows **no caption anywhere near either disc**, and no `FIXIERT` beside the follow toggle
  either. The only engraving that renders on that board is `RUNDE 1`.

So the machinery is built, styled, localised, active — and invisible. `_roundLabel` renders and
the three **anchor-parented** engravings do not, which is the shape of the lead.

*(The diagnosis and the fix, plus the per-board position dials the user asked for, are the C#
half of this round — see the commit and the report that goes with it.)*

---

## WHAT THE COLOUR ROUND MUST NOT HAVE LOST — and it did not

`cap_belong.py --report`, all five self-checks passing first:

| board | hue err vs its board | hue+chroma ΔE2000 | C\* cap / board | round 5's values |
|---|---|---|---|---|
| oak | **−1.3°** | **1.12** | 27.2 / 29.4 | −1.3° · 1.12 · 27.2/29.4 |
| steel | **+7.5°** | **0.65** | 3.8 / 3.8 | +7.5° · 0.65 · 3.8/3.8 |
| bronze | **+3.3°** | **5.48** | 12.8 / 22.5 | +3.4° · 5.40 · 13.0/22.5 |

Oak and steel reproduce round 5 to the third significant figure. Bronze moves by **0.08 ΔE2000**,
two orders of magnitude below a just-noticeable difference.

Cap-to-cap separation: 14.54 / 13.68 / 7.91 against round 5's 14.54 / 13.67 / 8.01 — no collapse.
The seat guard still holds on the darkest state: disabled luminance 0.1253 / 0.1255 / 0.1245
against the 0.1220 bar, i.e. 1.028× / 1.029× / 1.021×.

**And the strongest form of the claim, which needs no instrument at all: oak's albedo atlas is
byte-identical to round 5 in every cell except the new cell 9.** Per-cell diff, max |Δ| = 0 on
cells 0–8 and 10–15. Oak's colour cannot have moved, because oak's texels did not. Steel's mean
|Δ| is 0.6/255 (0.24 %) and bronze's 5.7/255 (2.2 %), from their tempers.

Symbol legibility, `cap_check.py --symbols`: every cell of every style at **33.6–46.1 %** contrast
at 96 px, against round 4/5's 34–47 %. `--selfcheck` passes all three legs (null refuses, uncarved
reads −0.21 %, carved fires at 33.91 %).

---

## WHAT THE INSTRUMENTS CANNOT SEE

**Nothing in this directory measured ROUGHNESS until this round, and that is why five rounds
missed it.** `cap_check` measures symbol contrast, `cap_deltae` and `cap_belong` measure colour,
`plate_forensics` and `cap_regmove` measure whether a picture has an inside-outside order. A cap
made of sandpaper and a cap made of glass score identically on all five, provided they are the
same colour with their rims in the same place. The user was looking at a quantity no number in
this project described.

**And no picture here contained a round cap.** `cap_onboard.py` placed the SQUARE cap only, in
`ButtonSeat1..3`. `CELL_SHORT_REST = 5` was defined in that file and never used. So the two rest
discs — half the caps on the board — appeared in no on-board render of round 4 or round 5, which
means (a) round 4's fix for "the round cap's entire bezel has been invisible since round 2" was
never shown landing on a board, and (b) the square-rim-on-a-round-cap defect **could not have
appeared in any sheet either**. Two rounds of pictures were argued over with the defective control
outside the frame. *The blind spot is the lead.*

**And every one of those pictures had the noisy term switched off.** `cap_onboard` shaded the caps
from their geometric normals with no normal map, on the argument that "since ModBuild 290 the
relief IS geometry, so a picture that needed a normal map to show the bezel would be showing
something the mesh does not have". That is right about the **bezel** and wrong about the
**material**: `BoardLit` samples `_BumpMap` at the same `i.uv`, and the micro-grain lives there and
nowhere else. `--capnormal` now binds it and both columns of every round-6 sheet use it. *An
instrument that models a subset of what the eye sees agrees with every broken build.*

**`cap_rough`'s own first version was wrong and its own selfcheck caught it.** Leg 5 asserts that
one physical surface stored at a keycap cell's texel density must read rougher, at the same
viewing distance, than the same surface stored at a board's. It FAILED, reporting the denser
storage as the *smoother* — because `resample` refused to magnify and silently returned coarse
inputs untouched, so the two surfaces being compared were at two different pixels-per-millimetre
while the report claimed one. Bilinear magnification is not a formality here: it is half the
finding, because a magnified texture has nothing at the top of the eye's band and that is a real
reason a board reads smooth. *A new instrument's first output is a hypothesis.*

**The board's bar is a MEDIAN over patches and it is patch-size dependent.** The face band also
carries the frame, the seat pockets and the carved rest motifs; a mean over it would let those set
a bar the plain wood never asked for. The quartiles are printed beside every median for exactly
that reason. But the window over which a band-pass and a mean are taken changes the number: the
same board reads 3.30 % at a 98 px patch and 3.79 % at 128 px. Both columns of every comparison use
the same patch size, so the ratio is sound — but **no board figure here should be quoted on its
own**, and the round/square rows are not comparable to each other.

**Roughness is not measured on the RENDER.** Terms A and B are measured on the textures, at a
common px/mm, and combined in quadrature. That is invariant to `_Color` (selfcheck 3) and needs no
renderer, but it assumes the two contributions are independent and it does not model the geometry's
own shading, the mip chain the GPU actually selects, or MSAA. The renders are pictures for a person
to judge; they are not where the numbers come from, and the two must not be confused.

**Whether "less rough" is as much less as he meant.** "Das gerne etwas weniger" has no number in
it. This round chose the one relational bar he did state — the cap must not read rougher than the
board it sits on — and hit it on ten of twelve cases. If he wanted *some* grain and this reads as
plastic, the fix is `GRAIN_RELIEF_SLOPE` and `GRAIN_TEMPER` upward, both per board, both one
measurement each.

**Hardware.** None of this has been in a headset. Every picture is Blender EEVEE reproducing
`BoardLit`'s two baked directions, which is a replica and not the shader. And the bundle has not
been rebuilt — six PNGs changed and the integrator owns that step.

---

## WHERE THE BRIEF WAS WRONG

1. **"The contrast rise is very likely also the noise."** No. The albedo was at 0.93× / 1.03× /
   1.51× of its own board; the normal map was at 15.9× / 45.5× / 53.7×. Oak, named as worst, was
   *below* its bar on the albedo term.
2. **"Round 4 rebuilt the atlas with `--no-objects` from `keycap4_material_<style>.png`."** It did
   not. Re-running `cap_atlas.py` with its defaults reproduces all six shipped PNGs byte for byte,
   which is the `objects=True` path registering `keycap3_object_<style>.png` — round **3**'s
   plates. Round 4's four generated material images are in `buttons/out/` and **nothing reads
   them**. The README's chain description is wrong on this point and is corrected.
3. **"The round cap appears to be filled with the SQUARE design's registration."** Right about the
   symptom, wrong about the location: the round cap's own FIELD cells (5 and 6) are correctly
   round-registered and always were. It is the shared PLAIN cell 0, taken by the bezel and wall
   submeshes of every cap, that is square.
4. **"Check the winding fix reaches what renders, because the round seat looks EMPTY."** The
   winding is correct on every shipped round mesh (positive volumes, UV and geometry agreeing).
   The empty seat in `noisy_buttons.jpg` is the CONFIRM seat with its cap hidden — a different
   control, on the other side of the board from the rest pads.
5. **"There needs to be a 'Lange Rast' / 'Kurze Rast' text — the machinery already exists, reuse
   it."** The machinery does not merely exist, **the captions are already built, localised in
   German, styled and active** — and have been since ModBuild 281. They render nowhere. The work
   was never to add them; it was to find out why five rounds of screenshots never showed them.
6. **"`SlotOverlayOffset_*` never reached `ConfigSteps`, so make sure your new dials do."** The
   trap is real and the hole is already closed at source: `ConfigCatalog.Classify` now guards the
   flat `BaseStep` write with `if (t == typeof(Color))` and `ResolveSteps` admits `Components`
   except `Color`, so Vector3 dials go through the same resolver as scalars.

---

## WHAT SHIPPED

| file | change |
|---|---|
| `unity/board-prep/buttons/cap_rough.py` | **new** — the roughness instrument, six self-checks including a null, a known positive, `_Color` invariance and the texel-density leg that failed first |
| `unity/board-prep/buttons/cap_atlas.py` | `GRAIN_RELIEF_SLOPE` (per board) replaces the height pinning; `GRAIN_TEMPER` / `GRAIN_TEMPER_SIGMA`; cell 9 `PlainRound`; `_slope_rms` |
| `unity/board-prep/buttons/cap_object.py` | `register_round(..., continue_rim=)`; the `roundbezel` kind through `cell_art` / `normalised_cells` / `field_jitter` |
| `unity/board-prep/buttons/cap_onboard.py` | `--capnormal` binds the cap `_BumpMap`; the ROUND cap seated at `ShortRestToken` / `LongRestToken`; `--roundbezel`; `--restshot` |
| `unity/board-prep/buttons/cap_sheet_rough.py` | **new** — the three sheets |
| `unity/.../Keycap{Oak,Steel,Bronze}_{albedo,normal}.png` | rebuilt |

`FIELD_TARGET_LUM`, `BoardIdleColor`, `BoardCapTint`, `CapWellColor`, `CapSeatContrast` and every
mesh are **unchanged**.

## THE PICTURES

* `.planning/debug/round6/round6_roughness.png` — square caps, before/after, three boards
* `.planning/debug/round6/round6_round_caps.png` — the rest discs, before/after, three boards
* `.planning/debug/round6/round6_boards.png` — both shapes on all three whole boards
* `.planning/debug/round6/round6_engraving.png` — "KURZE RAST" / "LANGE RAST" as cuts, DE and EN,
  on all three board materials (glyph body 48.6–54.6 % darker than the board around it, lit lip at
  0.99–1.23× over 12–14 % of the area)

## STILL OPEN

* **Bronze square 1.26× and steel round 1.19×** on the albedo term, stated above rather than tuned
  away.
* **The seat guard still cannot see the modulator** — round 5's finding, unchanged, and still the
  binding constraint on cap contrast.
* **Round 4's four material plates are dead inputs.** Either wire them in or delete them; leaving
  a generated asset that the README says is used and that nothing reads is how round 3's random
  crop survived two rounds.
* **The bundle has not been rebuilt.**

---
# ROUND 5 (2026-08-25): THE GEOMETRY SURVIVED THE PIPELINE AND THE COLOUR DID NOT

Round 4 did the right thing on the generation side. It showed gpt-image-2 the **rendered board
itself**, got eighteen options back, and they are good — oak reads as real turned and carved wood,
steel as dark weathered iron with rivets, bronze as green-patinated bronze. Two per board were
taken forward and their three-dimensional features were rebuilt as actual geometry.

**And the cap that shipped did not look like the option it was built from.** On the board all three
read as a similar pale yellow-olive: the oak cap was not brown, the bronze cap was not green, and a
yellow-tan cap against a green-patina bronze board is exactly the "does not belong" the user has now
said three times.

Measured, against the board each cap sits on, in CIELAB hue angle:

| board | its own board's hue | the cap that shipped | error |
|---|---|---|---|
| oak | 65.6° | 91.4° | **+27.5°** |
| steel | 58.5° | 223.8° | **+164.0°** |
| bronze | 91.8° | 65.6° | **−26.2°** |

The steel cap was **blue**. The bronze cap was **orange**, on a board that is green.

---

## THE CAUSE WAS TWO THINGS, AND THE ROUND WAS BRIEFED ON ONE OF THEM

`BoardLit` computes `alb = tex2D(_MainTex, uv) * _Color`. A cap's colour is therefore the PRODUCT
of the atlas and the state colour, and both terms were wrong in the same direction.

### Cause 1 — `normalise_plate` clipped the hue out (this was the briefed one, and it is real)

`cap_atlas.normalise_plate` re-based every plate to mean **0.837**, and reaching it took gain 3.46
on oak. **68.5 % of oak's shipped field texels clipped in at least one channel**, so oak's modulator
was very nearly a constant and contributed almost no colour at all. That is exactly what the round-4
lane reported and what the ModBuild 289 note recorded from the other side.

### Cause 2 — the state colour had been solved to an AUTHORED PALETTE nobody had validated

This one was not in the brief and it is the larger of the two on two of the three boards.
`Cards/PlayTray.BoardIdleColor` had three per-board colours, solved in ModBuild 286 over a hue ×
chroma grid maximising the minimum pairwise ΔE subject to staying inside `cap_deltae.PALETTE`:

    oak    -> "parchment / pale honey"  h 78-92    C* 14-20
    steel  -> "pewter, cool and quiet"  h 220-285  C*  7-14
    bronze -> "brass / warm gold"       h 66-80    C* 24-32

Those windows were authored **before round 4 existed** — before there was any picture of what a
button on that board should look like. The boards themselves sit at h 65.6 / 58.5 / 91.8, so the
**steel window excludes its own board's hue by 161°** and the **bronze window excludes its own
board's by 12–26°**.

**The solver hit its target exactly and the target was wrong.** The shipped bronze cap landed at
h 65.6 C* 27.3 — inside its authored window, on a board at h 91.8. Bronze barely clipped at all
(0.32 %), so `normalise_plate` is not what made it orange; the palette is.

That is the finding worth keeping: *a solver that maximises separation inside an unvalidated
palette produces caps that are maximally different from each other and belong to nothing.* The
number it was optimising — min pairwise ΔE 17.0 — went up while every cap moved away from its
own board, and nothing in the loop could see that, because nothing in the loop was looking at
the board.

---

## THE THIRD OPTION: 0.837 WAS NEVER A REQUIREMENT

Two rounds were spent on a trade-off with two known horns, both bad:

* the shipped normaliser clips the hue out;
* the chroma-safe replacement collapses the boards into each other, min pairwise ΔE 17.0 → 6.8.

Both horns share a premise, and **the premise is what was wrong.** 0.837 is the mean of
`KeycapGrain_albedo.png`, the greyscale texture these plates replaced. It was copied across because
it was there. Nothing requires the modulator to have that mean. What the mod actually requires is:

1. the cap reads proud of its own well in every state — that is `SeatedCapColor`'s job;
2. the cap face is the colour of its own board.

Requirement 1 was being satisfied *by accident* through a copied constant, and satisfying it that
way made requirement 2 impossible, because a warm plate cannot reach 0.837 without clipping its
brightest channel flat.

So the gain **stays uniform** — that property was always right, a uniform gain moves no hue ratio —
and only the number it is solved for changes: from a copied constant to a level solved per board
against the guarantee that actually constrains it. The colour is then bought in `BoardIdleColor`,
which had headroom nobody had measured: the shipped values sit at 0.41–0.75 against a ceiling of 1.

    board    FIELD_TARGET_LUM   field clip         rendered field contrast
    oak      0.837 -> 0.730     68.46 % -> 32.94 %   4.12 % -> 10.69 %   (2.59x)
    steel    0.837 -> 0.850      8.57 % ->  4.92 %   6.64 % -> 10.36 %   (1.56x)
    bronze   0.837 -> 0.780      0.32 % ->  5.54 %   4.21 % ->  9.05 %   (2.15x)

Steel's target went **up** and its contrast improved anyway; bronze's clipping went up and its
contrast more than doubled. The level and the contrast are not the same knob once the state colour
is free to move with the plate.

### `BoardCapTint` was NOT needed, and reaching for it first would have been wrong

The ModBuild 289 note names `[ButtonColors] BoardCapTint` as "the lever that would free this
properly", at the cost of a config round across four tint families, their wire defaults and
`KeycapGrain`. It is a real lever and it is the wrong one to reach for: it is the **user's own
tuning surface**, a peer **clamps it to [0, 1]** on the mirror path (`RemoteBoardFurniture:837`),
and the headroom this needed was already sitting unused in a hard-coded constant that nobody has to
configure and no peer clamps. Four tint families were nearly changed to avoid changing three
numbers.

---

## THE ARITHMETIC

    rendered_face_sRGB = modulator x SeatedCapColor(IdleColor_board x BoardCapTint) x shade

with `BoardCapTint` 0.5 (**unchanged**) and `shade` 0.87373 (`BoardLit`'s baked key against a flat
front-facing plateau — derived in `cap_deltae`, not guessed). Every term is a raw sRGB number
multiplied by another raw sRGB number, because the rig renders in **Gamma** colorspace and
`BoardLit`'s fragment is a product of two colours; only the result is decoded to CIELAB, because
CIELAB is defined on light and the framebuffer value is an encoding of light.

**The target** is the option each cap was built from (`out/keycap4_material_<style>.png`, the flat
face material of the chosen square option), **re-exposed to the cap's own luminance**. The cap
cannot be the option's colour — the option is a studio product shot at L\* 36–42, the cap renders on
a dim board at L\* 20–24 — and the re-exposure is done **in linear light**, because that is what a
darker exposure of one material physically is. Scaling the encoded numbers instead is a fade toward
black through the transfer curve and it desaturates: a gamma-space fade loses oak 3.1 C\* and bronze
1.7 C\* against a true exposure. Small, free to get right, and the difference between a target that
is a claim about the material and one that is an artefact of the encoding.

**The idle colour is then exact, not optimised:**

    IdleColor_board = target_colour / (field_median x shade x BoardCapTint)

    oak     (0.550, 0.514, 0.564) -> (0.759, 0.539, 0.456)
    steel   (0.407, 0.541, 0.607) -> (0.588, 0.557, 0.506)
    bronze  (0.753, 0.471, 0.224) -> (0.549, 0.547, 0.529)

Note what happened to their shape: three strongly-cast state colours became three nearly-neutral
ones. That is the decomposition changing hands. The **material** now carries the board's colour,
which is where a photograph of a material should carry it, and the state colour carries the level
and the state — which is what a state colour is for.

Every channel stays inside `[CapWellColor x CapSeatContrast / BoardCapTint, 1]`, so `SeatedCapColor`
never lifts one channel and not another. **A floor that engages asymmetrically is a hue shift nobody
solved for**, and that was checked rather than assumed.

---

## THE ACCEPTANCE TABLE — `cap_belong.py --report`, CIEDE2000

Both columns are read off a PNG: `before` is ModBuild 290's atlas (restored from git into
`.planning/debug/round5/atlas_before/`) under ModBuild 286's idle colours; `now` is what is in the
bundle under the colours above.

**1. The cap against the option it was built from**

| board | full ΔE2000 | hue+chroma ΔE2000 | vs the RE-EXPOSED option |
|---|---|---|---|
| oak | 21.04 → 13.49 | 16.11 → **4.37** | 13.28 → **0.024** |
| steel | 15.14 → 10.32 | 11.19 → **1.19** | 10.01 → **0.028** |
| bronze | 19.91 → 16.96 | 12.25 → **4.11** | 13.23 → **0.024** |

The last column is the round's headline and it is exact by construction. The full-ΔE column stays
large because the cap is half the option's brightness — that difference is the scene, not the
material, and is why the hue+chroma form is the one the complaint is about.

**2. The cap against its own board** — the number that decides "belongs"

| board | hue error vs its board | hue+chroma ΔE2000 | C\* cap / C\* board |
|---|---|---|---|
| oak | +27.5° → **−1.3°** | 13.57 → **1.12** | 11.8/29.4 → **27.2**/29.4 |
| steel | +164.0° → **+7.5°** | 10.33 → **0.65** | 5.6/3.8 → **3.8**/3.8 |
| bronze | −26.2° → **+3.4°** | 10.83 → **5.40** | 27.3/22.5 → **13.0**/22.5 |

The after column is not "close to" the bar — it **is** the bar. The options' own hue errors against
their boards are −1.4° / +7.2° / +3.4°, and the cap now is that material at a different exposure,
so it inherits them exactly.

The band being aimed for on cap-vs-board is *same hue, lower chroma* — a cap at its board's hue with
somewhat less chroma reads as the same material, worked differently, catching less light in a
recess. A cap identical to its board disappears; a cap unrelated to it is the defect being fixed.
Bronze's C\* 13.0 against its board's 22.5 is the widest of the three and is why its hue+chroma ΔE
of 5.40 is the largest number in the table.

**3. Between the three boards' caps** — the collapse trap

| pair | before | after | the options themselves |
|---|---|---|---|
| oak ↔ steel | 13.61 | **14.54** | 17.54 |
| oak ↔ bronze | 12.96 | **13.67** | 14.93 |
| steel ↔ bronze | 23.85 | **8.01** | 12.73 |

Two pairs improved. **Steel ↔ bronze fell from 23.85 to 8.01, and that number is not the 6.8
failure it resembles.** 6.8 was three boards converging on one hue because a neutral modulator left
only a shared state colour to tell them apart. 8.01 is the separation pewter and patinated bronze
genuinely have at this brightness: the caps sit at hue 64.2° / 65.7° / 95.2°, and steel is told from
oak by chroma (C\* 3.8 vs 27.2), not by hue.

The 23.85 was **manufactured**. It was steel rotated to blue and bronze rotated to orange — a
separation invented by the state colours and present in neither material. Three caps that are far
apart and all wrong is the defect, not the bar. This is the round's one number that got worse and
it was not worth defending.

**4. What the colour round must not have broken**

| board | idle luminance | disabled vs the guard bar | confirm luminance |
|---|---|---|---|
| oak | 0.2175 → 0.2176 (1.001×) | 0.1398 (1.146×) → 0.1253 (1.028×) | 0.2251 → 0.2025 |
| steel | 0.2104 → 0.2105 (1.000×) | 0.1364 (1.118×) → 0.1255 (1.029×) | 0.2186 → 0.2010 |
| bronze | 0.2024 → 0.2023 (1.000×) | 0.1307 (1.072×) → 0.1250 (1.025×) | 0.2101 → 0.2014 |

Idle luminance is held to a tenth of a per cent: **this round changes colour and nothing else.**

---

## WHAT THE INSTRUMENTS CANNOT SEE

**REG cannot see hue, and this round's complaint is entirely hue.** `plate_forensics.REG` — mean
luminance against distance-to-border — has a null control and a known-positive control and it is a
good instrument for "does this picture have an inside/outside order". It moved 3.7 → 28.3 on oak in
round 3 and the round was rejected anyway. **A cap that is the wrong colour for its board scores
exactly like a cap that is the right one.** No REG number is quoted for this round and none should
be.

**`SeatedCapColor` promises something it cannot deliver, and it is now the binding constraint on
this whole round.** It floors `_Color` at `CapWellColor × CapSeatContrast`, i.e. it promises a cap
face at 1.35× the luminance of the well it sits in. `BoardLit` then multiplies that by the texture,
which the guard never sees — the guard was written when the texture was a near-white grain, and a
term it was calibrated against silently became a variable. Measured on the product:

* the well renders at luminance **0.0903**, so the promise is a cap face at **0.1220**;
* ModBuild 290's *disabled* caps sat at 0.1398 / 0.1364 / 0.1307 — the promise held, by 7–15 %;
* it held **because** the modulator was near-white, not because anything checked.

Every one of this round's three field targets **sits on that bar**, enforced by hand in
`cap_belong.solve`. Lower is better on both numbers that matter — oak at target 0.62 clips 1.7 % and
carries 14.3 % contrast against the 32.94 % / 10.69 % actually shipped — and what stops it is that
the disabled cap would then render **darker than the well it sits in**, which is the "invisible
button, only the text visible" defect the guard was written for after a hardware report.

**So the single highest-value change available to the next round is to make that guard see the
modulator**: floor the state colour's *luminance* so that `(state × modulator)` clears the well,
scaling the state colour uniformly to preserve its hue. It must be per-modulator, not global —
`KeycapGrain` (the dash and rest caps) is still near-white and a doubled floor would over-lift it —
so it means a signature change across six call sites including the peer mirror and the preview
station. That is why this round enforced the bar by hand instead, and said so.

**The solve's first version measured the wrong artifact and was wrong by an order of magnitude.**
It read the pre-carve cell array and predicted oak's field would clip 3.9 %; the atlas built from
the same target clipped **40.5 %**. Two terms sit between the normalisation and the file —
`cap_object.field_jitter`, which multiplies the field by up to 1.055 and pushes texels already at
0.97 over the top, and the 8-bit quantisation — and neither exists in the array. `cap_belong` now
BUILDS each candidate and reads the result back off the PNG.

**The report's first version compared neither of the two things that ever shipped.** The atlas had
already been overwritten when `--report` first ran, so its "before" column was the *new* atlas under
the *old* idle colours. The old atlas is now restored from git and kept.

**The dE2000 implementation's first selfcheck failed and the code was right.** Nine of ten Sharma
reference pairs matched to 1e-4, including all four blue-region cases that exercise the Rt rotation
term; the tenth was a value typed from memory that belongs to a different pair. The reference was
corrected, not the code — and the tempting move at that moment was the other one.

**The winding assertion's first version cried wolf on a correct mesh.** It demanded a positive
signed UV area and fired on the cap's WALL submesh at −0.98. These caps UV-project planar over
their own footprint and the wall is the vertical skirt — a surface perpendicular to the projection
plane, whose UV "area" is the signed area of the outline traced by the ring and whose sign says
nothing about mirroring. It now compares the UV winding against **the geometry's own winding in the
same plane** and judges only their product. That distinction matters beyond this file: an assertion
that cries wolf gets disabled, and a disabled assertion is how the previous eight got out.

**What no instrument here can answer:** whether the option is the colour the *user* wants. Every
number in this round measures agreement with round 4's chosen option and with the board. Both are
pictures we produced. Only hardware answers the question behind them.

---

## THE WINDING GUARD

Winding has now shipped wrong on **eight** meshes in this project, and the most recent was in this
directory — 384 of the round cap's 640 triangles faced inward for two rounds, so the bezel round 3
built was back-face culled and invisible in every picture taken to judge round 3.

`cap_onboard.check_winding` now runs on **every** OBJ the sheet pipeline reads, which is every mesh
that becomes a picture anyone argues about, and raises on:

* **signed volume** < 0 per submesh, by the divergence theorem over the closed hull — a fully
  reversed mesh gives exactly the negative of the right answer;
* **signed UV area × signed geometric area in the same plane** < 0 — a mesh can be wound correctly
  in space and have its UVs mirrored, and no volume test can see that.

It costs microseconds and it is in the READER rather than in `Assets/Editor/ExportCapMeshes.cs`
deliberately: the reader is the last point before a mesh becomes evidence. If the exporter is ever
given the same assertion, keep both — they guard different steps. All six shipped caps pass:
volumes +2.2e-05 / +2.0e-05 / +6.1e-05 on oak's three submeshes, UV and geometry agreeing in sign
on all of them.

**No geometry was changed this round.** ModBuild 290's rebuilt caps are what both columns of the
sheet wear.

---

## WHAT SHIPPED

| file | change |
|---|---|
| `unity/board-prep/buttons/cap_atlas.py` | `FIELD_TARGET_LUM` per board replaces the copied `GRAIN_TARGET_LUM` on the object path; `normalise_plate`'s docstring records how the trade was resolved rather than restating it |
| `unity/board-prep/buttons/cap_belong.py` | **new** — the round's instrument: CIEDE2000, the re-exposed target, the solve, five self-checks |
| `unity/board-prep/buttons/cap_onboard.py` | `--atlas` / `--idle` so a BEFORE column goes through this renderer; `check_winding` on every OBJ |
| `unity/board-prep/buttons/cap_sheet_colour.py` | **new** — the two sheets |
| `unity/GloomhavenVR.Assets/.../Keycap{Oak,Steel,Bronze}_{albedo,normal}.png` | rebuilt at the new targets |
| `src/GloomhavenVR/Cards/PlayTray.6.Build.cs` | `BoardIdleColor` — three values, with the derivation and the superseded solve kept and marked |

`[ButtonColors] BoardCapTint`, `CapWellColor`, `CapSeatContrast`, `ConfirmedColor`, `DisabledColor`
and every mesh are **unchanged**.

## STILL OPEN

* **The seat guard cannot see the modulator** (above). It is the binding constraint on cap contrast
  and it is a real, measured defect in a hardware-won guard. Fixing it makes every number in the
  contrast table better at no cost to anything else.
* **Bronze's chroma** is the weakest of the three at C\* 13.0 against its board's 22.5 — inside the
  intended band but at its edge, and the largest cap-vs-board ΔE in the table at 5.40.
* The **bundle has not been rebuilt** — the six PNGs changed and the integrator owns that step.

---

# ROUND 4 (2026-08-25): THE GENERATOR HAD NEVER BEEN SHOWN THE BOARD

ModBuild 289 was rejected. That is three rejections, twice with the same word.

**The user, verbatim:**

> "Ich bin immer noch überhaupt nicht damit zufrieden wie die buttons aussehen. Ich mag die
> Textur gar nicht. **Sie passt überhaupt nicht zu dem jeweiligen board.** Bitte gehe das anders
> an: **Gib gpt-image-2 die boards** und lass die Optionen für eckige und runde Knöpfe generieren
> die zum Design des Boards passen. **Baue die dann nach.** Verwirf deine aktuellen — das führt
> zu nichts."

---

## THE ONE THING ROUNDS 1, 2 AND 3 ALL HAD IN COMMON

Every previous round showed the generator a picture of a **material** or a picture of a **shape**,
and never once a picture of **the board**.

| round | what the model was shown | what that is |
|---|---|---|
| 1 | `gen_capref.py` → `ref_face_<style>.png` | a 1024² band cropped out of the board's own albedo — a swatch of its SURFACE |
| 2 | the same swatch, ask re-worded for feature SIZE | the same swatch |
| 3 | `cap_object.py --init` | a bare grey render of the signet profile — a picture of the mesh we already had |

A swatch carries a board's palette and its grain and none of its construction: no border, no
corner, no hardware, no edge, no proportion. **The complaint was relational and the input never
was.** The proof is in `cap_object.PROMPTS`: read all four and notice that the word *board* does
not appear in any of them. They ask for an oak plate, a steel plate and a bronze plate — three
materials, vividly and correctly described, with no reference to the three objects those plates
have to sit in.

**And the mesh was never in question either.** Round 3's prompt opens with *"The supplied grey
image is the exact GEOMETRY of these two plates and must be followed band for band."* So all three
rounds poured a different material into ONE profile, identical on all three boards. That is,
accurately, the same mesh with a different texture on it — which is what
*"wie zusammengewürfelte assets mit standard meshs draufgeklatscht"* describes.

---

## WHAT WAS GENERATED

`unity/board-prep/buttons/cap_options.py`. The input is
`unity/asset-preview/build_asset_strips.sh`'s own board block at 1536 px in two views (yaw 35 /
pitch 50, and yaw 12 / pitch 72 for the border profile and the seat pockets), flags inherited
unchanged — the normal map is bound, there is no `--cull` because every tray material is
`_Cull: 0`, and all three are pinned to one ortho width. Board identity from the source, not the
look: oak = `PlayTray_prepped.fbx`, steel = `PlayTray_9capjqp6*`, bronze = `PlayTray_16vm268h*`.

**Three sheets, eighteen options**, each a 3 × 2 grid — three ROUND on the top row, three SQUARE
on the bottom — asked to differ in **construction** (edge profile, mounting hardware, face
treatment) rather than in colour, faces completely empty, no text anywhere in the frame. All three
delivered exactly 1536 × 1024 and read back with PIL. All three landed first try; none was
re-rolled.

The grid is not assumed. `cap_sheet_options.verify()` measures it: the gutters between cells must
be flatter than the cells themselves (gutter σ 0.9 / 0.9 / 3.0 against weakest cell σ 12.3 / 14.4 /
12.8 — confirmed).

**Contact sheet:** `.planning/debug/round4/options_contact_sheet.png` — each board rendered beside
its own six options, because a sheet of buttons on grey cannot be judged against *"passt nicht zu
dem jeweiligen board"*.

### Kept — six of eighteen, one round and one square per board

Both shapes are the user's *Vorgabe* ("Rund und Viereckig sind Vorgabe"), so every board keeps one
of each. The criterion is not which button is nicest; it is which one is a claim about **that
board**.

| board | kept | why, against the board |
|---|---|---|
| oak | **S3** | the board's square dentil border, at cap scale. The oak board's one unmistakable signature is a run of small raised blocks around its frame. Nothing else on the oak sheet is a claim about the oak board rather than about oak. |
| oak | **R2** | the round sibling of a stepped, blocked border; keeps the clipped-corner joinery family so the two shapes read as one set. |
| steel | **S1** | four **dome rivet heads**. The steel board's border is dentils with small round rivet heads among them. |
| steel | **R2** | plate stacked on plate with visible corner hardware — how the steel board's own seats are built. |
| bronze | **S2** | a raised inner plateau ringed by four dome bosses, every corner rounded: the bronze board's seat pocket, feature for feature. |
| bronze | **R2** | the cast stepped terrace. Concentric raised rings are the bronze board's own language. |

### Discarded — twelve, each with its reason

Recorded machine-readably in `cap_options.DISCARDED_OPTIONS`; `_check_manifest()` runs at import
and RAISES if any option is unaccounted for, if a board keeps fewer than one of each shape, or if
a sheet on disk is not in the record. Driven negative on all three failure modes before it was
believed.

The reasons fall into three groups, and the largest is the point of the round:

* **Not of this board** (oak R1, steel R1, steel R3, bronze R1) — correct for the material and
  true of any button made of it. This is rounds 1–3's failure shape exactly.
* **Wrong hardware for this board** (oak S2, steel S3) — oak S2 puts metal-shaped pegs on the one
  board whose whole story is that its face carries no fittings; steel S3's slotted screws are the
  better-looking cap and the worse match, because there is not one screw slot anywhere on that
  board.
* **Fouls the field** (oak R3, bronze R3, steel S2) — a dished or domed face bends the caption,
  which is solved on a flat viewer-facing plane; steel S2's retaining ring is a large circle in
  exactly the 25 % of the cap the carved role symbol occupies.

### The second ask: the FACE MATERIAL, flat

`cap_options.MATERIAL_PROMPTS`, one image per board, 1024², referencing **the chosen square option
and the board**. Since the bezel is geometry now, the texture must not paint one again — a painted
rim on top of a real one is the doubled-edge defect this pipeline already recorded once. What is
left for the atlas is material, wear and engraving, which is a flat sample.

**This is not a return to rounds 1 and 2.** Those asked for a flat sample and put it on a mesh with
no structure, so the cap had structure nowhere. This asks for a flat sample of a **named button
that was designed against this board**, to put on a mesh that now carries the structure.

The atlases are built through the existing chain unchanged — `cap_atlas.py --plates … --no-objects`
— so the symbol carving, the level re-basing, the mip guard and the `.meta` writing are the same
code as ModBuild 289. Level re-based at gain 2.5–2.7 to the shipped `KeycapGrain` mean of 0.837,
because a keycap texture MODULATES the state colour.

---

## WHAT THE MESH GAINED

`Cards/CapFaceLayout.CapConstruction`, derived from `ControlBoard`, built by `Cards/CardMesh`.

| | oak | steel | bronze |
|---|---|---|---|
| corner | **clipped** 45° (joinery) 0.100 | **filleted** 0.100 | **filleted** 0.160 |
| outer zone | square-edged **flat land** | stepped **terrace** | stepped **terrace** |
| hardware | **7 dentil blocks per edge**, 0.034 proud | 4 **dome rivets** r 0.030 | 4 **dome bosses** r 0.034 |
| base | **undercut skirt** 0.085 | undercut 0.070 | undercut 0.095 |

All fractions are of the cap's SHORT side. `PlainConstruction` — a cap with no board, i.e. the map
room's keycap-skinned furniture — is byte for byte the ModBuild 289 mesh, and the plain path in
`BuildBeveledKeycap` is the untouched original code.

**Vertex counts** (the real shipped meshes, exported by reflection into the built DLL):

    oak    square  72 →  738 verts,  36 →  376 tris     round  642 → 2130,  640 → 1512
    steel  square  72 → 1674,        36 →  850          round  642 → 1746,  640 → 1360
    bronze square  72 → 2090,        36 → 1056          round  642 → 1746,  640 → 1360

### THREE CONSTRAINTS THAT SHAPED IT

1. **Everything inside the bezel band, d ∈ [0, `BezelTotal`], or on the WALL — never in the
   field.** `FieldLo` does not move, so the caption solver's asserted cases, `CapCellMath`, the
   atlas's `TEXT_*` mirrors and `BoardCapSymbolVectors` are all still true without one of them
   being touched, and the guarantee the user actually stated — *"Der Text muss immer voll lesbar
   sein"* — is untouched. Widening the bezel would have bought a nicer rim by shrinking the caption
   box, which is how "AUSWAHL BEEN" happened.
2. **Derived from `ControlBoard`, so it is not a new wire field.** The construction is a property
   of the board exactly as its atlas is, and both sides already agree on the board
   (`RemoteBoardFurniture._style` is handed to every cap it builds). Wire coverage is unchanged at
   172 / 85 / 257: no dial added, nothing new to sync, no way for a peer's mirror to disagree
   without already having the wrong board.
3. **The wall is where the area is, and the first cut of this round put everything in the wrong
   place.** The bezel is 0.135 of the short side — 5.9 mm on bronze — while the cap is
   `[BoardButtons] Depth` thick, shipped at **36 mm**. The first on-board render showed exactly
   what that implies: a tall plain slab with a hairline of detail along its top edge. Every option
   on all three sheets is a LOW WIDE button whose edge profile is most of what you see. The
   undercut skirt spends the wall, which costs nothing, because the crown keeps the full footprint
   and the cap only ever gets narrower below the shoulder.

---

## TWO REAL BUGS FOUND, BOTH PRE-EXISTING, BOTH SHIPPED

### 1. The round cap's entire bezel has been invisible since round 2

`BuildRoundKeycap.AddBand` asserted a fixed winding with the comment *"taken from
BuildRoundCap's side wall, which is the one this project has already proved outward-facing on
hardware"*. That is true of the WALL — the one band with no radial step — and false of every band
that steps inward. On the outer chamfer, the **rim land** and the inner chamfer the same order
gives a right-hand normal of +Z, pointing away from the viewer, against a shading normal pointing
toward them.

Every cap material is `new Material(BoardLit)` and keeps the shader's default `_Cull = Back`, so
all three bezel bands were back-face culled.

* **Measured:** 384 of the round cap's 640 triangles wound against their own normals — exactly
  64 segments × 6.
* **Seen:** `.planning/debug/round4/station/Oak_ShortRest_after_rake.png` shows the recessed
  field, a crescent of the far wall, and no bezel ring at all, while `Oak_Confirm_after_rake.png`
  beside it shows the square cap's full bright frame.

The round rest pads are Round on all three boards by default, so this affected every board.
`AddBand` now derives its winding from the band's own normal, which is what the square cap does
and why the square cap never had this bug.

### 2. A one-quad seam at every rounded corner

The rounded ring closed with a duplicate vertex (the last arc point equals the first), giving one
zero-length segment that `AddRingBand`'s degeneracy guard then skipped in every band — a small but
genuine hole in the outer chamfer, the rim land and the inner chamfer alike. Caught as the only
two non-flat open edges on the bronze cap, on the inner chamfer, exactly at (−cx, Y).

---

## THE INSTRUMENTS

### A new one: the mesh audit

`Assets/Editor/ExportCapMeshes.cs` reflects into the **built GloomhavenVR.dll** and calls the real
`CardMesh`, so there is no port to drift — `PreviewKeycaps.cs` carries a hand port guarded by a
vertex-COUNT check, and round 4 makes the counts differ per board, so that guard had nothing fixed
left to compare against. While it holds real geometry it audits it: every triangle's winding
against its own normals, and every edge against the shell.

**All twelve meshes: winding OK. `_before` closed shell, `_after` open only at hardware
footprints.**

Two of its own first outputs were wrong and both were fixed rather than explained away:

* the edge test keyed on **index**, so it reported 72 of 90 edges "open" on the known-good
  ModBuild 289 square cap. Both builders emit four fresh vertices per quad — they must, the
  normals are flat per face — so it was measuring vertex reuse and calling it watertightness. It
  keys on welded **position** now, and the ModBuild 289 caps read as closed shells.
* counting open edges without **classifying** them reported "112 open edges" on a mesh with no
  holes. Raised hardware omits its base face; that footprint is not a hole, and the number that
  means something is how many open edges are off the flat.

### The old one, and the number that FELL

`plate_forensics.py` REG on the **atlas cell**:

    round 3 shipped     28.3 / 35.3 / 23.7
    round 4 shipped     13.0 / 10.5 /  2.2

**That fall is correct and is the point.** REG asks "does this picture have an inside-outside
order", the cell no longer carries one because the bezel is geometry, and reading the drop as a
regression is the instrument pointed one stage too early. `cap_regmove.py` therefore applies the
same function to the thing a player sees — the cap RENDERED, square-on, alone, cropped to its own
alpha silhouette so the frame is the cap's footprint exactly as the cell is, with the same atlas
under both constructions:

    board    BEFORE (289 slab)   AFTER (round 4)   change
    oak                   23.6              23.6     +0.0
    steel                 17.9              23.9     +6.0
    bronze                23.3              28.1     +4.8

    controls: null noise swatch ~17-20,  accepted board backs 57-107

**Reported as it stands rather than dressed up.** Its own first version measured the FRAME and not
the cap — with black around the cap the d≈0 bins were pure background, and it produced 133 / 124 /
196, authoritative-looking and meaningless.

`cap_check.py --selfcheck` still passes on all three legs: null refuses to measure, uncarved plate
reads −0.21 % (below the floor), the carved disc fires at 33.91 %.

---

## WHAT THE INSTRUMENTS CANNOT SEE

* **Whether the button belongs to the board.** Nothing here measures it. REG scores a rim in the
  wrong place exactly as it scores a rim in the right place; that is why round 3 improved REG by
  7× and was rejected. The only evidence for the actual question is the two sheets, and they are
  pictures for a person to judge.
* **The undercut and the terrace, at the angle REG is taken.** REG on the rendered cap is measured
  **square-on**, and `CapFaceLayout` already records why that is nearly blind here: on `BoardLit` a
  45° chamfer and a flat face are two FIXED values, not a highlight that moves. The undercut reads
  by its silhouette and its shadow line at a RAKE; the terrace reads by its steps at a rake. Seen
  from directly above, most of what round 4 added contributes almost no luminance variation. So
  the +0.0 / +6.0 / +4.8 above is a floor on the improvement and not a measure of it — and equally,
  nobody should quote it as proof the change worked.
* **Colour, at the level the atlas is re-based to.** Oak's caps render distinctly yellow-olive
  against a warm brown board. This is the documented cost of `normalise_plate`: reaching the
  0.837 modulator mean needs gain ≈ 2.5–2.7, the knee and the clip are most of what happens at
  that gain, and a warm plate loses its red channel first. It is **not** a regression — round 3's
  plates needed the same gain — and the lever that would fix it properly is named and unchanged:
  lower the target and raise `[ButtonColors] BoardCapTint` by the reciprocal so the product is
  unchanged. That is a config round across four tint families and their wire defaults.
* **The wall's UV.** `CardMesh` has always documented that a vertical wall spans a constant y (or
  x) and varies only in z, so it samples a thin stretched strip of the cell. The undercut makes the
  wall a much larger share of the cap, so that stretch is now a much larger share of the picture.
  Visible in every close render as vertical streaking on the skirt. Nothing measures it.
* **Anything at the across-the-table 32 px view.** The dentil blocks are 1.9 mm on a 63 mm cap.
  At arm's length they read; at 32 px across the whole cap they cannot, and the same is true of the
  dome rivets. No sheet here is rendered at that size.
* **Hardware.** None of this has been in a headset. Every picture is Blender EEVEE reproducing
  `BoardLit`'s two baked directions, which is the same replica `render_asset.py` uses and is not
  the shader.

---

## WHAT IS STILL OPEN

* **The user has not chosen.** He asked for OPTIONS; six of eighteen were taken on a reading of
  each board, and that reading is stated above so it can be disagreed with. Switching any board to
  a different option from the sheet is a row of `CapFaceLayout._byBoard` plus a re-crop, not a
  rewrite — the construction is data.
* **`PreviewKeycaps.cs` still hand-ports the mesh builders** and its `MeshInvariant()` still
  asserts the ModBuild 289 counts, so its pictures are of the PLAIN construction on every board.
  `ExportCapMeshes.cs` is the path that cannot drift; the station should be moved onto it.
* **The 36 mm cap depth** is `[BoardButtons] Depth`, the user's own tuning, and it is the single
  biggest reason the construction reads less strongly than the option sheets do. Not changed here.
* **`cap_object.py` and round 3's registration chain are still in the tree**, unused by the
  shipped atlas. Kept, not deleted: `plate_forensics.py`'s comparison of the two is the record of
  why the cell stopped being a picture of a button.

---
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
