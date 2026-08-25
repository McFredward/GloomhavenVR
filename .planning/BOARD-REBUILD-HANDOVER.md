# CONTROL-BOARD REBUILD — state of the world

## ROUND 4 (2026-08-25): THE BACK'S SCREWS AND PLATES ARE GEOMETRY NOW

He tested ModBuild 277 and accepted the art — *"Die Rückseite der boards gefällt mir sehr
gut!"* — then named what was still wrong:

> "Allerdings: Auf den Texturen sind Schrauben und Halzplatten etc zu sehen, also eigentlich
> 3-dimensionale Objekte. Sie werden aber flach nur auf der Textur dargstellt. **Ich möchte,
> dass du das Mesh für die Seiten und Rückseite an die Textur anpasst**, so wie du es auch
> für die Vorderseite bereits sehr erfolgreich gemacht hast."

He is right, and round 3's own record says why: it derived the back's relief FROM its own
art because the plate was geometrically flat and there was nothing to double. A normal map
has no silhouette, no occlusion and no stereo parallax. **Full detail in
`unity/board-prep/img2img/README.md`, "ROUND 4".**

**No registration step was needed, and that is measured rather than assumed.** `tex_backfill`
places the back art at each texel's own BOARD COORDINATES — `x = (u-0.5)·LONG`,
`y = (0.5-v)·SHORT` — so a feature measured in plate pixels is built in metres directly.
Re-running that assignment reproduces the shipped atlas's BACK texels EXACTLY (mean |err|
**0.00/255**, r = **+1.0000**) against controls at 15.6–21.7/255 that fire. `gen_board.BACK_ART`
carries every figure at 1 px = 0.3125 mm.

**What became geometry:** oak two iron straps (42.5 × 310 mm, 2.0 mm proud) with eight
forged square nails (12.5 mm, 1.0 mm); steel three recessed fields (1.5 mm) and **76 dome
rivets** (8.4 × 6.6 mm, 1.3 mm); bronze three cast fields (1.5 mm), which makes its
stiffening ribs real. **What did not:** oak's three plank seams, and the reason is a number
— the art paints them 1.6 mm wide, a wall no steeper than 40° can then carry 0.67 mm of
depth and the two walls meet in a V with no floor, and widening the slot would put a 3 mm
groove where the art paints 1.6 mm.

**Every back wall is a straight chamfer of at most 40°**, for two measured reasons:
`gen_geobuf` calls a triangle BACK only within 45.57° of the thickness axis (steeper lands
in INTERIOR, which nothing paints), and a vertical wall has ZERO area under the back's
top-down projection. INTERIOR triangle counts are unchanged on all three boards, so nothing
leaked.

| board | tris | back proud | dims | holes / non-manifold / loose / inward |
|---|---|---|---|---|
| Oak | 11896 → **12976** | 3.0 mm | 0.0356 → **0.0386** | 0 / 0 / 0 / 0 |
| Steel | 9968 → **15716** | 1.6 mm | 0.0343 → **0.0356** | 0 / 0 / 0 / 0 |
| Bronze | 19580 → **20660** | recessed | 0.0354 unchanged | 0 / 0 / 0 / 0 |

Budget 24000. **Oak is now 38.6 mm against the contract's 40 mm ceiling** — 1.4 mm of
headroom, and the next thing that stands proud of the oak back buys its height from the
straps. Signed volumes 5211.4 / 4642.8 / 5289.9 cm³, all positive.

### THE ATLAS IS NOT REPACKED — designed in, then measured

Plate, chamfer walls and caps all share the back's single top-down projection, so the back
island's raw bbox is unchanged, every category's shelf packing is unchanged, and
`gen_backuv` lands on the identical rectangle the shipped boards already use. Rasterising
every NON-BACK UV triangle of shipped and new into a 2048² map: **0 differing texels** on
all three, against a one-texel control that moves 20458 / 21314 / 20616. Back density
1.213 → 1.210, 1.246 → 1.239, 1.186 → 1.182 tex/mm — the drop is the added wall SURFACE in
the denominator. Moving relief out of the map and into the mesh *lowers* what the back's
density has to carry, so no repack was bought and none was needed.

### The prefabs are BYTE-IDENTICAL, and the unit-scale trap was re-checked

`BoardBuilder` re-measures the locked table unchanged (seat floors 74.6 × 64.3 / 81.0 × 70.1
/ 61.2 × 51.9 mm, rest pads 81.6 / 81.7 / 68.1 mm, 7 of 7 anchors, one MeshCollider, bounds
0.640 × 0.320) and **all three prefabs come out with no diff at all**. `gen_uvdiff`
shipped-vs-new: checks 3a/3b PASS on every board, 0 of 10 empties differ, worst |d| =
0.000e+00 m. Check 8 (UnitScaleFactor from the FILE BYTES) 100.0 → 100.0 on all three; its
positive control — an FBX re-exported with `apply_unit_scale=True` — passes checks 1, 2, 3a,
3b, 3c, 4, 6 and 6b and **fires only on 8**, exactly as recorded.

`gen_backuv`'s interlock moved from the SEED to the RESULT and that strengthened it: the
−Y-extreme polys are now the nail/rivet caps and grow to the whole back island, and the
assertion is that the grown set is one complete island AND all back geometry — the second
half was never checked before. `gen_uvdiff`'s back-face finder had the same flat-back
assumption and was reporting oak's back plate as 480 × 263 mm of a 636 × 316 mm face.

### The normal map had to give the relief back — `img2img/tex_backrelief.py`

A painted strap edge is a dark LINE, so its high-pass is a GROOVE; the mesh there is a
chamfer RAMPING UP. They disagree in sign one or two texels apart. The feature band is
removed from the back's normal map with a mask taken from the MESH's own normals, never
from the picture. Slope inside the band 0.113 → 0.032 (oak), 0.119 → 0.033 (steel),
0.149 → 0.047 (bronze); **0 texels outside the mask changed on any board**, by construction —
the edit is the DIFFERENCE of two slope fields from the same source. **The ALBEDO is not
touched**: the paint is already registered to within a texel, and flat-on it is the only
thing that draws the feature (the half-vector sits ~32° off a flat face).

### THE SIDES: a fix was built, measured and DELIBERATELY NOT APPLIED

The rim blend walks up to a full board thickness inward, so it paints the back's rivets down
the side: weighted by the blend, oak 2.5 %, **steel 15.5 %**, bronze 2.0 % of rim texels.
`img2img/tex_rimfill.py` corrects it with one parameter and takes steel to **4.2 %** —
**and the picture barely changes** (`rim_steel_compare.png`). Not applied. Two findings kept:
the prominent 3-D objects on the steel side are REAL (the front frame's studded border seen
edge-on), and the obvious follow-up hypothesis is false — split by term, steel is FRONT
10.1 % against BACK 15.5 %, so the term the fix already corrects is the dominant one.

**This is the one place a literal reading of his sentence was not followed**, and he should
be told: making those side screws real would mean drilling a rivet row into the edge of a
plate that has none, i.e. adapting the object to a sampling artefact. The screws he is
looking at are real and they are on the back; this round makes them real there. If he wants
a genuinely riveted edge band, that is a different feature and a different round — the rim
is a wrapped STRIP island, so relief on it needs new UV islands and therefore a repack.

### Instruments added this round

    unity/board-prep/gen_backshot.py            one-sun BACK station with a stated raking
                                                elevation; an A/B, never a verdict
    unity/board-prep/img2img/tex_backrelief.py  take the painted relief out of the back normal
    unity/board-prep/img2img/tex_backghost.py   registration: the offset at which the mesh's
                                                own height field best fits the albedo
    unity/board-prep/img2img/tex_rimfill.py     the rim walk correction (built, not applied)

Registration: oak (0, +2) texels, steel (0, −3), both with a planted +8 recovered exactly.
**Bronze is inconclusive and the instrument says so** — its argmax hits the ±12 search
boundary, and so does the untouched FRONT face's. The picture settled it:
`registration_{style}.png` draws the mesh's wall band over the atlas's own back albedo and
the band traces the paint on all three.

### Pictures — `.planning/debug/board278/` (gitignored)

    board278_back_rake.png / _flat.png   all three, before | after, identical lighting
    registration_{style}.png             mesh wall band over the back albedo
    shader/{style}_backrake.png          THE CALIBRATED ONE: real prefabs, real BoardLit,
                                         rebuilt bundle, via Editor/PreviewBoard
    rim_steel_compare.png                the rim fix that was not applied

### Bundle

Locally rebuilt at **68,577,168 bytes** (was 68,522,833). **NOT a DLL-only install.**
`check-bundle-format.sh` asserts no byte count; it prints the size.
`prebuilt/gloomhavenvr.bundle` is deliberately NOT in this lane's diff.

### STILL OPEN after round 4

* **The rim ghost is measured, fixable in one command, and unshipped.** Ask him.
* Everything under "STILL OPEN" from rounds 1–3 below, including **SEAT 3 HAS NO OCCUPANT**
  (superseded by the ModBuild 277 button work — check before repeating it) and the two card
  slots' teal game-state tint.
* Oak's 1.4 mm of remaining thickness headroom.

---

## ROUND 2 (2026-08-25): THE TEXTURES WERE REJECTED AND RE-AUTHORED WITH gpt-image-2

The user tested ModBuild 271 on hardware and rejected the boards: *"Die Controllboards sehen aus,
als hätten sie keine Textur (siehe no_texture.jpg) … Es soll immersiv sein! Am Besten auch mit
normal map etc. gpt-image-2 kann sehr gut immersive texturen erstellen, wenn du die texture-map
als initiales frame übergibst."* And, when the cost rule was read too conservatively: *"Nutze
gpt-image-2 für die texturen! … Verantwortungsvoll umgehen heißt nicht, gar nicht benutzten."*

**The pipeline was fine. The art was the defect, and it was measured against his own screenshot.**
Six flat-metal patches, located by a homography from the board's silhouette quad, give
`screenshot_linear / atlas_linear = 0.675 ± 0.051` — ONE global scalar fits every patch. The
shipped steel atlas had STORY 1.91 and chroma 1.79 over the face region: a 100-value grey band.

**Why it passed four rounds:** `out/renders/steel_board.png` is a Blender/Principled render, not
the shipped shader, and it overstates the board's relative contrast by **1.35x**.

**A claim from this round that was RETRACTED before anything was built on it.** An early pass said
the game shows the albedo faithfully because `game CV / atlas CV = 1.02`. Wrong. Equal CV means
*similar* contrast, not the *same* contrast; measured, the albedo accounts for at most ~50 % of
the screenshot's high-pass energy and the rest is relief.

**Four generated images, and what each bought.** Steel v1 proved the model preserves the macro
layout and transforms the material, and failed two stated targets; it also proved the tool
silently rescales a 16:9 init frame into 3:2 (the board came back at 1.809:1, exactly 16/9 ÷ 3/2).
Steel v2 on a 3:2 init frame came back at 2.0000:1 and hit 3 of 5 targets. Oak and bronze took the
corrected prompt first time. Nothing was regenerated on a hunch.

**The art does not drop in.** The model kept the macro layout but redrew every feature outline
3–9 mm off the mesh's real geometry and at different radii. The pipeline, the measurements and the
traps are documented in **`unity/board-prep/img2img/README.md`** — read that before touching the
textures again. Highlights:

* The composite keeps the generated MATERIAL everywhere and takes edges from the mesh only where
  the mesh has edges. **The layout cannot drift**: art is scattered into the atlas through the
  mesh's own UV pass, so every texel gets the colour of the board point that samples it.
* Normal and MRS come from the new art, but only the MATERIAL band; feature relief stays with the
  shipped mesh-registered map.
* **The shipped normal maps are RED-INVERTED** relative to `UnpackNormal`. The new maps match that
  convention deliberately.
* Only **23.3–27.3 %** of each atlas is covered by triangles at all. A UV rectangle is not a
  surface, quantified.

**Results, face region, shipped → new:** steel std 21.4→48.5 (2.27x), STORY 1.91→15.38 (8.07x),
chroma 1.79→5.66; oak std 21.9→39.4, STORY 3.32→11.85; bronze std 9.1→29.0 (3.20x),
STORY 1.96→8.82. In his own framing the steel board's contrast CV goes 0.156→0.296 and the
BETWEEN-patch spread of the mean goes **9.4 → 26.1 levels**.

**Mean chroma FALLS for oak and bronze (33.3→25.7, 34.8→20.5) and that is the improvement** — the
shipped ones score high because they are uniformly saturated. `huevar` is the right statistic.

**The doubled-edge falsifier, with a working positive control.** Ghost ratio (albedo-edge density
peak near a geometric edge over the plate background): raw generated art 1.67 / 3.14 / 4.12
(oak/steel/bronze) — the control fires. After the composite: 1.10 / 1.30 / 0.62 against the
shipped boards' own 1.04 / 1.26 / 4.63.

**Pictures** (gitignored, local): `.planning/debug/boards_before_after.png` (flat-on, identical
lighting), `boards_ingame_before_after.png` (all three in the screenshot's own scene),
`boards_shader_rake.png` (real prefabs through the real shader from the rebuilt bundle),
`boards_reuse_error_{oak,bronze}.png` (which pixels of the in-scene picture to distrust).

**TWO TRAPS IN THE IN-SCENE STATION, both hit and both caught by something other than its own
self-test.**

1. **"The old atlas" stopped being old the moment the new maps were installed.** A second lane
   was pointed at `Assets/Bundle/Table/*_albedo.png` for the BEFORE picture after those files had
   already been overwritten, so its shade field divided the screenshot by the very atlas it was
   previewing. **Its round-trip test passed anyway** — the round trip is exact by construction and
   cannot see this. What caught it was a selection over nine candidate atlases: the working-tree
   file scored r = -0.055 and implied-shade cv 124.9, the `git show HEAD:` blob r = +0.334 and
   cv 0.576. Pin the old atlas to a HEAD blob; never read the working tree for a "before".
2. **The oak and bronze in-scene pictures reuse STEEL's relief.** There is no oak/bronze
   screenshot, so the illumination is the steel measurement reused per screen pixel. The albedo is
   placed through each style's own UV map and is correct (verified to <=0.001 atlas texels), but
   the rim and bevel highlights are steel's: **20 % of oak and 34 % of bronze board pixels** carry
   a doubled edge at rims and bevels. Judge colour, material and motif there; do not judge edges.

A picture built from an intermediate state of that lane was published for ~10 minutes and then
replaced. The STEEL numbers in this record are unaffected: they came from the first station, whose
cached shade field is bit-identical to one built from the HEAD blob.

**Bundle 65,626,956 → 67,234,683 bytes.** NOT a DLL-only install. `check-bundle-format.sh` asserts
NO byte count — only wrapper format 7 and the writing editor; it prints the size.
**Wire tests are 147379, not 147378.**

### STILL OPEN after round 2

* **The two card slots render bright teal in game.** That is a game-state tint drawn over the
  recess, not the board texture, and it now clashes badly with three restrained materials. It was
  not touched here and it is the most likely next complaint.
* **The specular is a bevel term, not a surface term** — the half-vector sits ~32° off a flat face,
  so the roughness split reads on mouldings and recess walls and much less on the open plate. Not
  buyable with more roughness contrast; it needs a shader change (a second key nearer the view
  axis, or a view-dependent term).
* `BoardLit.shader`'s ModBuild-248 comment is stale (it says the boards do not opt into specular;
  all three now do). `_Cull: 0` on all three board materials though the comment says Back(2).
* Everything under "STILL OPEN" from round 1 below, EXCEPT **SEAT 3 HAS NO OCCUPANT** — that
  one is CLOSED (2026-08-25, the button-group unification lane). The turn-flow SKIP sits in
  `ButtonSeat3` on the owner's board and on every peer's mirror of it; `WorldUI/ButtonCluster.cs`,
  the whole `[RoundButtons]` config section, the `[WorldUI] ButtonCluster` switch, the cluster
  dock mount and its two per-board dials are deleted, and record 28's ids 81..88 / 228 / 16 / 133
  / 52 are retired-and-reserved. The same round collapsed the per-board keycap SEAT family onto
  the mesh anchors (see `CardsConfig`'s seat-family note).

---

# ROUND 1 RECORD (mesh + first texture lane)

This file was the owner handover. It is now the RECORD of what the owner lane did, what it
measured, and what is still open. The task's original brief and the user's verbatim request are
kept at the bottom; everything above is the current state.

**Branch:** `boards-rebuild`. Never touch `dev` — the integrator merges.

## THE USER'S TASK, verbatim

> "Ich möchte, dass du das Mesh und die Textur von allen 3 Controllboards überarbeitest. Aktuell hat
> das Mesh viele Lücken, die Symbole darauf sehen nicht clean sondern KI-generiert aus (was es ja
> auch ist). Vergleiche einmal das alte Mesh der Hände mit den neuen. Die neuen haben ein deutlich
> cleaneres Mesh. In der Liga möchte ich, dass du die Controllbaords auch hinbekommst. Auch die
> Texturen können nachmal cleaner sein. Nutze dafür gpt-image-2. … Jedes generierte Bild kostet Geld,
> daher mache das eher selten wenn du viel Vorbereitung getroffen hast. … Kümmere dich aber auch
> darum, dass die Texturübergänge passen. Render dir die Ergebnisse und begutachte sie. Weiterhin
> noch eine größere Teilaufgabe: Wie du mir selbst gesagt hast, ist 3 das Maximum an gleichzeitigen
> Knöpfen. Daher möchte alle 3 Boards so umgebaut haben, dass sie auf der rechten Seite wo die Knöpfe
> hinkommen 3 statt 2 Slots für die buttons habe. Die slots sollen zum Styl des boards passen und
> sich natlos einfügen. Mach selbständig so lange weiter bis du dein Ziel bei allen drei boards
> erreicht hast - parallelisier deine tasks wie immer."

He answers in German; code, comments and log strings are English. Any report that reaches him is
German.

## THE MESH — DONE, and verified three independent ways

| board | tris | holes | non-manifold | loose | inward-wound faces | dims (m) |
|---|---|---|---|---|---|---|
| Oak | 20000 → 11896 | 1288 → **0** | **0** | **0** | **0** | 0.640 × 0.0356 × 0.320 |
| Steel | 20000 → 9968 | 1639 → **0** | **0** | 4098 → **0** | **0** | 0.640 × 0.0343 × 0.320 |
| Bronze | 20000 → 19580 | 1628 → **0** | **0** | 4559 → **0** | **0** | 0.640 × 0.0354 × 0.320 |

Signed volume 5160.5 / 4877.3 / 5511.2 cm³, all positive. Reference: the hands are 20 654 tris.
UV islands 1046 → 121/156/107. Three button seats on every board, each with both spellings of its
anchor at bit-identical positions.

Run the checks with:

    /home/claw/blender-4.2/blender -b --factory-startup --python unity/board-prep/gen_stats.py -- <fbx>
    /home/claw/blender-4.2/blender -b --factory-startup --python unity/board-prep/gen_winding.py -- <fbx>

## THE PREFABS AND THE BUNDLE — BUILT, and the measurements match exactly

`BuildBoard.cs` had been edited four times and never compiled or executed by any gate. It compiled
and ran first try. Every measurement it logs equals the expected table it carries:

| board | seat floor (mm) | rest pad (mm) |
|---|---|---|
| Oak | 74.6 × 64.3 | 81.6 |
| Steel | 81.0 × 70.1 | 81.7 |
| Bronze | 61.2 × 51.9 | 68.1 |

7 of 7 anchors resolved per board, every anchor projected 0.5 mm (i.e. already exactly on the face
plane), bounds 0.640 × 0.320 to five decimals, one non-convex MeshCollider each.

**`check-bundle-format.sh` DOES NOT ASSERT A BYTE COUNT — the brief that said so was wrong.** It
checks the UnityFS wrapper format (must be 7) and the writing editor (2021.3.5f1) and PRINTS the
size. The 72,966,925 figure lives only in the ModBuild notes in `Net/NetProtocol.cs`, and the new
build note carries the new number. Nothing in the gate needed changing.

## THE INSTRUMENT — `Editor/PreviewBoard.cs`, new this round

Renders and measures the BUILT BUNDLE, not the source, at the asset paths `VRCardFactory` asks for.

    BOARD_PREVIEW_OUT=<dir> xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode \
        -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
        -executeMethod GloomhavenVR.BoardPreview.RenderAll -logFile board-preview.log

Read its header before trusting it. **A Win64 bundle opened in a Linux editor draws MAGENTA** — no
compatible shader variant, `HasProperty` false for everything — and that looks exactly like the
pink-material trap. It is a viewer artifact. The run therefore does a BUNDLE pass (structure) and a
PROJECT pass (the picture), and they cross-check each other.

## WHAT THE RENDER FOUND — three defects the earlier rounds could not have seen

1. **The boards had no specular at all.** The texture pipeline authors albedo + height + roughness
   + metallic per style and `tex_render.py` judges through a Principled BSDF that binds all four.
   `BoardLit` bound two. Fixed: `_MRSMap` bound (R = metallic, G = roughness, LINEAR import),
   `_SpecStrength` 0.85. Opt-in is still the map: no `*_mrs.png` → 0 → bit-identical to before.

   **But the specular is NOT what fixed the white plaster, and saying so was my error.** Measured
   independently by both lanes: the baked key sits ~32° off the flat face's normal once a viewer is
   in front of the board, so the half-vector never lands on the open plate — `_SpecStrength` 0 vs
   0.85 moves the flat-on mean by **0.001**. The 3.4–3.8 % of board that does move (p99 0.047–0.075)
   is the **bevels and mouldings**: recess walls, seat rings, the studded border. Steel read as
   plaster because its albedo had a **1.35× dynamic range and a blue cast**; the albedo fixed it.
   The specular is a bevel term, worth its weight as one. If metal should read on the open plate
   too, that is a SHADER change (a second key nearer the view axis, or a view-dependent term) —
   no texture can put a highlight where the half-vector does not go.
2. **Bronze would have shipped standing on edge with its controls in mid-air.** See below.
3. `PlayTray_mr.png` was glTF-ORM, bound by nothing, 1.4 MB of bundle. Deleted.

## THE BRONZE ASSET-POSE TRAP — measured, not inferred

`AssetOffset_Bronze` = (0, −0.11, +0.08) and `AssetPitchDegrees_Bronze` = 57 are in the user's live
cfg, where they beat any shipped default. They move the board MESH while the anchors stay pinned,
and they were correct for the shipped Bronze — a raked lectern 301 mm deep. Replayed against the
re-authored flat plate (`PreviewBoard.ApplyAssetPose`, the mod's own algorithm):

    bronze  ButtonSeat1   OFF THE MESH ENTIRELY
            ButtonSeat2   OFF THE MESH ENTIRELY
            ButtonSeat3   151.5 mm off the surface
            LongRestToken 167.1 mm off the surface
            ShortRestToken OFF THE MESH ENTIRELY
    oak / steel (dials are identity)  0.5 mm — the assembler's own `proud` offset. The control.

Fixed by `BoardAnchors.ClampAssetPose`: bounds the lift any pinned anchor takes to 5 mm, half to
the offset and half to the tilt (the two terms add), the tilt bound derived as `asin(lift / r)` with
`r` MEASURED as the furthest pinned anchor. Gate is the seat clamp's gate — does the board carry a
measured recess. Old bundle: unbounded, bit-identical. **Re-measured after the clamp: 2.7 mm, every
anchor back on the mesh.** (3.8 mm with the first, per-axis version of the clamp — the wire-test
sweep caught that it bounded a CUBE and not a BALL, so a pose with all three axes dialled came out
sqrt(3) too long; each half is scaled as a vector now, which also keeps the direction of his tuning.) The peer calls the same function with terms it already has, so no wire
field and no host/peer divergence.

## THE SEAT CLAMP HOLDS — arithmetic from the built prefabs

Worst excursion of any cap or disc from its anchor: **9.0 mm** (Steel). Outermost drawn edge
anywhere: **|x| = 0.2744 m against a 0.320 m half-width, 45.6 mm of clearance.** The +0.462 /
−0.445 mirror compensations are reduced to ≤ 9 mm, which is the intended outcome. Two dials go
quiet as a side effect and the user should be told: `RestButtonSpacing_Bronze` becomes fully inert
(both discs clamp to the same anchor-local offset, staying distinct only because their anchors are
105 mm apart), and `GenericButtonSpacing` is mostly absorbed by the anchor pitch.

## FOOTPRINT CHANGE — his tuned `BoardScale` does not compensate, and should not be "fixed"

`BoardScale` is a uniform scalar, so the change passes through 1:1. Long edge unchanged on all
three; the short edge changes:

| board | old world size (m) | new world size (m) | change |
|---|---|---|---|
| Oak | 0.2716 × 0.1354 | 0.2720 × 0.1360 | none meaningful |
| Steel | 0.5647 × 0.3254 | 0.5647 × 0.2824 | −43 mm (−13.2 %) |
| Bronze | 0.2720 × 0.0927 | 0.2720 × 0.1360 | **+43 mm (+46.7 %)** |

His cfg wins over any shipped default, so this cannot be silently corrected and must not be: the
board is now correctly proportioned and the growth is the point. Tell him, let him retune if he
wants.

## THE TEXTURES — measured before and after, on the installed maps

| | linear luma mean | dynamic range (p95/p05) | saturation | verdict |
|---|---|---|---|---|
| Oak before / after | 0.168 / 0.170 | 4.66x / 4.71x | 0.590 / 0.589 | unchanged, correctly |
| Steel before / after | 0.350 / 0.216 | **1.38x / 3.57x** | 0.088 / 0.029 | flat blue-grey paint -> brushed metal |
| Bronze before / after | 0.211 / 0.169 | 2.12x / 1.81x | 0.646 / 0.561 | pepper noise -> cavity-driven patina |

Metallic/roughness packs now ship for all three: metal 0.000 / 0.995 / 0.885, roughness floored at
0.42-0.43 so the tightest Blinn lobe is 7.8-8.1 degrees. Specular contribution through the real
shader: p99 0.047 / 0.075 / 0.051, 3.4-3.8 % of the board moving by more than 0.02. Per eye at
63 mm IPD the highlight adds 0.0009 / 0.0030 / 0.0017 against a geometric L-R difference of
0.0630 / 0.0687 / 0.0498 — 1.4 to 4.4 %, so no rivalry risk at these settings.

Bronze's range going DOWN is not a regression: the old number was inflated by the hard-edged
pepper it no longer has.

## THE SHIPPED BUILD

ModBuild **271**. Bundle **65,626,956 bytes** (was 72,966,925), UnityFS format 7, Unity 2021.3.5f1,
installed at `prebuilt/gloomhavenvr.bundle` so `scripts/install.ps1` picks it up. Wire tests are
now **147,378** (was 146,857; +521 for the two board clamps, and no script gates that number).
**NOT a DLL-only install.**

## STILL OPEN

- ~~**SEAT 3 HAS NO OCCUPANT.**~~ **CLOSED 2026-08-25** by the button-group unification lane, and
  the way it closed is worth recording because it is not what this entry predicted. The entry read:
  seat 2 is empty, the turn-flow SKIP it exists for is drawn by `WorldUI.ButtonCluster` from its own
  `[RoundButtons]` column at board-local (0.148, −0.124), and moving it "changes what record 28's
  ids 81..88 MEAN". The user then asked for the group itself to go ("Ich möchte daher, dass die
  Button-Gruppe der 'Überspringen Buttons' komplett verschwindet … so dass all diese buttons gleich
  aussehen und untereinander in den jeweiligen Slots sitzen"), which turns out to be the cheaper
  change rather than the more expensive one: with the skip built by the same `GenericCap` /
  `BuildButtons` call as Confirm and Undo, there is no second geometry solve left to keep in step,
  so ids 81..88 (and 228) were RETIRED rather than re-interpreted. `WorldUI/ButtonCluster.cs`, the
  `[RoundButtons]` section, the `[WorldUI] ButtonCluster` switch and the cluster dock mount are all
  deleted. **Both halves of the user's request are finished.**
- `Cull Off` is kept on all three boards although its justification is stale (the meshes are closed
  and correctly wound). Taking the overdraw back needs a rig round; the measurement is in the
  commit that added `gen_winding.py`.
- Oak ships a 0.5 MB `_mrs.png` whose metallic channel is uniformly 0. It buys a dielectric varnish
  sheen (3.45 % of the board moves by >0.02) and may or may not be worth the bytes.

## TRAPS — all measured, none hypothetical

- **The user's tuned offsets are mirror compensation**, and his cfg beats every shipped default.
  Do not "fix" his config; contain it in code, keyed on a property of the ASSET.
- **A clamp fallback is not a default.** `ButtonTuning.DefaultBoardWidth = 0.073` is the pre-Bind
  fallback; the bound value is `Defaults.BoardButtons_Width/Height = 0.063/0.065`.
- **`gen_render.py` is ~2 stops overexposed**; `tex_render.py` binds a shader the game does not run.
  For MATERIAL, only `PreviewBoard.cs` is a picture of what ships.
- **A self-test that builds its own input proves nothing.** `tex_symbols.py --selftest` passed 9/9
  while the real sheets came out shattered.
- **A mean is the wrong statistic for a highlight.** Reporting one made this lane print "the
  specular is doing nothing" for three boards; the p99 and max say otherwise.
- **A pixel census cannot tell a hole from an edge-on wall.** It said "something IS showing through"
  at three meshes with 0 inward-wound faces.
- **Census/log lists in this repo truncate with an ellipsis and no marker.**
- `unity/board-prep/out/` is gitignored — renders, sheets and UV JSONs are NOT in any diff.
- **The board source was not in the repository until this round.** `3682a037` shipped three FBXes
  and no author. `gen_board.py` and its three checkers were rescued out of a dead agent worktree
  and verified to reproduce the shipped geometry exactly.

## THE GENERATED SHEETS — the money is spent, do not spend more

`unity/board-prep/out/sheet_a.png` (medieval guild woodcut) and `sheet_b.png` (Norse interlace),
1024² each, nine motifs per sheet, both good. Sheet A's `bracket_alt` came back as a hammer; sheet
B's covers it. **Do not use the `rest_alt` cell from either sheet** — both are a crescent enclosing
a six-pointed star, i.e. two real-world religious symbols combined. It is a spare cell.

Image generation is allowed (user, 2026-08-25) but: only after the consuming pipeline is proven,
batched into sheets, never for anything procedural or anything that must tile, and **a generated
image never becomes albedo** — it is a binary stencil, the bevel is rebuilt from an exact distance
transform, and the motif is carved into a procedural material. That is the whole reason the result
stops reading as AI.

## RULES INHERITED (non-negotiable)

- Presentation only; never write game state from presentation code. Never patch
  `ScenarioRuleLibrary`, Photon Bolt or `FFSNet.NetworkManager`. `ressources/` and `libs/` are
  read-only symlinks; `decompiled/` is read-only reference.
- Every feature must be multiplayer-compatible; peers derive board layout locally from the same
  prefab, so anything you change must be derivable on every client or it is a picture desync.
- Never destructive git or `gh`. Never force-push.
- Bump `NetProtocol.ModBuild` +1 on every build handed to the user.

## GATES — every one, every time

```
./scripts/build.sh          # 0 errors, EXACTLY 6 warnings
./scripts/wire-tests.sh     # 146857 assertions
./scripts/check-mirrors.sh ; ./scripts/check-frame-order.sh ; ./scripts/check-bundle-format.sh
./scripts/patch-inventory.sh check   # 78 classes / 130 methods
python3 ./scripts/check-wire-coverage.py ; python3 ./scripts/rebase-defaults.py check ; python3 ./scripts/check-refasm.py
```
The 6 expected warnings: CS8602 `ButtonCluster.cs:459`/`:1638`, `RemotePickBanner.cs:113`,
`RemoteHandFan.cs:1810`; CS8604 `StatPanelSurface.cs:360`/`:362`.
