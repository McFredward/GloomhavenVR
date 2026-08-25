# BOARD ASSET CONTRACT (ModBuild 271 rebuild, 2026-08-25)

One contract, three boards. Every lane reads THIS file; nothing here may be changed by a lane
without saying so in its report.

## Why the rebuild — measured, not asserted

| asset | tris | boundary edges | HOLE LOOPS | loose verts |
|---|---|---|---|---|
| hands `VRHandArcane_L` (the reference) | 20654 | **0** | **0** | **0** |
| Oak `PlayTray_prepped` | 20000 | 20268 | **1288** | 0 |
| Steel `PlayTray_9capjqp6` | 20000 | 21674 | **1639** | 4098 |
| Bronze `PlayTray_16vm268h` | 20000 | 22110 | **1628** | 4559 |

More boundary edges than faces: these are shattered decimations of an AI photogrammetry mesh, not
authored geometry. They are not repairable — they are replaced by authored geometry.

## Identity

| style | FBX today | prefab today | code const |
|---|---|---|---|
| Oak | `PlayTray_prepped.fbx` | `Assets/Bundle/Table/PlayTray.prefab` | (default path) |
| Steel | `PlayTray_9capjqp6.fbx` | `…/PlayTray_9capjqp6.prefab` | `VRCardFactory.SteelTrayPath` |
| Bronze | `PlayTray_16vm268h.fbx` | `…/PlayTray_16vm268h.prefab` | `VRCardFactory.BronzeTrayPath` |

**File names and prefab paths DO NOT CHANGE.** The mod resolves boards by these exact paths.

## Geometry contract (unchanged from the shipped boards)

- Long edge exactly **0.640 m**; short edge **0.320 m** (2:1). Thickness 0.030–0.040 m.
- Authored in Blender lying in the **XY plane, decorated face toward −Z**, body z ≥ 0, pivot centred.
  `BuildBoard.cs` re-derives orientation from the anchor frame, so anchors must be correct.
- Quad-dominant, **watertight: 0 hole loops, 0 non-manifold edges, 0 loose verts.**
- **BACK-FACE RELIEF: no wall steeper than 40° off the back plane** (added ModBuild 278 by
  the mesh lane, and said out loud here as this file requires). Two measured reasons, both
  in `unity/board-prep/img2img/README.md` §ROUND 4: `gen_geobuf` classifies a triangle as
  BACK only within acos(0.7) = 45.57° of the thickness axis, and anything steeper lands in
  INTERIOR where `tex_backfill` never paints it; and the whole back is ONE island under ONE
  top-down projection, in which a vertical wall has zero area. `gen_board.back_stack()`
  asserts it. Oak's bbox is now **38.6 mm against the 40 mm ceiling above** — the next
  feature that stands proud of the oak back must buy its height from the straps.
- **SIDE RELIEF: no step steeper than 26° off the thickness axis, and NOTHING PROUD OF THE
  SILHOUETTE** (added ModBuild 280 by the side lane, and said out loud here as this file
  requires). Two measured reasons, both in `unity/board-prep/img2img/README.md` §ROUND 5.
  (1) `gen_geobuf` classifies a triangle RIM only when its normal is within acos(0.5) = 60°
  of the board PLANE; a 45° chamfer sits at axial 0.707 and lands in FRONT (which the front
  camera never painted) or BACK (which `tex_backfill` paints with the BACK PLATE ART at that
  texel's board coordinates). So a side step spends z, not inset: 2.0 mm of rebate costs
  4.5 mm of the band's height. `Board.side_stack()` asserts it. (2) The long/short edges
  above are read by `BoardBuilder` to place seats and docks, so side features are CUT IN and
  a feature that must read as proud — oak's cross battens — is the place where the
  surrounding field is cut back and the feature stays flush at the nominal silhouette. The
  bounding box is then unchanged by construction, and `gen_stats` reports it.
- Budget **≤ 24 000 tris** per board (the hands are 20 654 — same league, this is the bar).
- One material, one UV layer, **no overlapping UV islands**, ≥ 8 px padding at 2048².

## Anchors — SEVEN now, not six

Empties parented under the board root, exact names:

    Slot1  Slot2  ShortRestToken  LongRestToken  ButtonSeat1  ButtonSeat2  ButtonSeat3

`ButtonSeat1/2/3` replace the old `ConfirmButton` / `UndoButton` pair (the user: three buttons can
be live at once, so the board needs three physical seats on the right). **The old two names must
still resolve** — `BuildBoard.cs` and the mod both look them up by name, so ship an alias or keep
`ConfirmButton`/`UndoButton` as secondary empties at Seat1/Seat2 until the code lane lands. Lane C
owns that decision; state which you did.

Seat layout: right-hand zone, three seats evenly spaced on the short axis, in-style recesses that
read as part of the board, not as three holes punched into it.

## UV atlas region map — the shared contract between the mesh lane and the texture lane

The mesh lane writes `unity/board-prep/out/<style>_uv.json`; the texture lane composites against it.
Schema (normalised 0..1, origin bottom-left, y up — Blender/Unity UV convention):

```json
{
  "style": "oak",
  "atlas": 2048,
  "regions": {
    "face":        {"u0":0.0,"v0":0.0,"u1":1.0,"v1":0.5,"kind":"decorated top face"},
    "frame":       {"u0":0.0,"v0":0.5,"u1":0.5,"v1":0.75,"kind":"outer frame + corner brackets"},
    "slot_floor":  {"u0":0.5,"v0":0.5,"u1":0.75,"v1":0.75,"kind":"card recess floors"},
    "rest_pads":   {"u0":0.75,"v0":0.5,"u1":1.0,"v1":0.75,"kind":"the two round rest pads"},
    "button_seats":{"u0":0.0,"v0":0.75,"u1":0.5,"v1":1.0,"kind":"the THREE button recesses"},
    "sides":       {"u0":0.5,"v0":0.75,"u1":1.0,"v1":1.0,"kind":"edges + back"}
  },
  "symbols": [
    {"name":"centre_rose","u":0.5,"v":0.25,"size_uv":0.18,"region":"face"}
  ]
}
```
The region rectangles above are the DEFAULT layout; the mesh lane may change them, but it must
write what it actually produced and keep the six region names. `symbols[]` lists every place a
generated decorative image is stamped, in UV space, so the texture lane never guesses.

## Textures

Three 2048² maps per board: `<base>_albedo.png`, `<base>_normal.png` and `<base>_mrs.png`.

**THE PACKED MAP IS `_mrs`, NOT `_mr`, AND THE DIFFERENCE IS NOT COSMETIC.** The shipped shader
(`Assets/Bundle/Table/BoardLit.shader`) reads `_MRSMap` as **R = metallic, G = roughness**, B
unused. The pipeline's own intermediate `<base>_mr.png` is glTF ORM — R = occlusion, G = roughness,
**B = metallic** — which is what `tex_render.py`'s Principled BSDF binds. Feeding the ORM map
straight to `_MRSMap` reads OCCLUSION (≈0.97 everywhere) as metallic and makes every board fully
metal. Repack before installing; `unity/hand-prep/pack_mrs.py` documents the same packing for the
hands. The pack is LINEAR data — `BuildBoard.ImportAsLinearData` forces sRGB off on the importer.

**A BOARD WITHOUT AN `_mrs.png` HAS NO SPECULAR AT ALL.** `_SpecStrength` defaults to 0 and the
shader's specular branch then does not execute. That is the opt-in, and it is also how this went
wrong for four rounds: the pipeline authored metallic and roughness, every render bound them, and
the shipped material bound neither — steel is 99.3 % metallic in the map and rendered as white
plaster in game. Judge material through `Editor/PreviewBoard.cs`, which renders the real prefab
through the real shader, NOT through a Principled BSDF.

- Bases are PROCEDURAL (wood grain / brushed steel / patinated bronze). No AI for material bases.
- AI (`gpt-image-2`) is for **decorative symbols only**, generated on a flat neutral field, stamped
  by us into the atlas. Every generated image costs money: batch the whole symbol set into as few
  calls as possible and reuse across styles where the motif is shared.
- Seams: dilate/pad every island by ≥ 8 px after compositing; verify no island bleeds into another.

## Verification — every lane runs these, no exceptions

    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/gen_stats.py -- <fbx>          # 0 holes / 0 loose / tris ≤ 24000
    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/gen_render.py -- <fbx> <albedo> <normal> <out.png> [front|quarter]

A render is not optional and not a formality: LOOK at it and say what you see. This project has a
standing lesson that a preview station aimed at nothing renders happily.

**AND FOR MATERIAL, THE BLENDER RENDERS ARE NOT THE SHIPPED THING.** `gen_render.py` is ~2 stops
overexposed and `tex_render.py` binds a Principled BSDF the game does not run. The only picture of
what the player sees comes out of the assembler:

    BOARD_PREVIEW_OUT=<dir> xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode \
        -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
        -executeMethod GloomhavenVR.BoardPreview.RenderAll -logFile board-preview.log

It opens the BUILT bundle at the paths `VRCardFactory` asks for, dumps every anchor, seat extent
and material binding at F5, replays the user's live `AssetOffset`/`AssetPitch` dials against the
mesh, and renders through `BoardLit`. Read its header before trusting any of its output: the
bundle pass draws MAGENTA on Linux and that is a viewer artifact, not a broken bundle.

## What is NOT in scope
The bundle rebuild changes `gloomhavenvr.bundle`, which has been byte-identical since ModBuild 250
(every test since then was a DLL-only install). The integrator handles the rebuild and tells the
user he must copy the bundle this time.
