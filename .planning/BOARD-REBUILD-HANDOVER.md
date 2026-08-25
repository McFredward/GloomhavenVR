# CONTROL-BOARD REBUILD — state of the world (2026-08-25, owner lane)

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

- **SEAT 3 HAS NO OCCUPANT.** The boards have three recesses; the mod fills two. Confirm (or the
  item "Use" cap) sits in seat 0, Undo in seat 1, and seat 2 is empty — the turn-flow SKIP that it
  exists for is still drawn by `WorldUI.ButtonCluster` from its own `[RoundButtons]` column at
  board-local (0.148, −0.124). Moving it is a PlayTray + ButtonCluster + RemoteBoardFurniture round
  that changes what record 28's ids 81..88 MEAN (`RemoteBoardFurniture.cs:880-884` states this),
  and it was deliberately not attempted here. **This is the half of the user's request that is not
  finished, and he must be told so plainly.** The mesh half is done.
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
