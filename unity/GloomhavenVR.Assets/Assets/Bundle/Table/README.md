# Bundle content: Table/

Intended assets (Phases 3b `feat/cards` and 3c `feat/world-ui` consume these):

| Asset | Description |
|---|---|
| `CardBacking.prefab` | 3D card body — rounded rectangle, poker-card size (63.5 × 88 mm at 1 unit = 1 m), REAL thickness ~1.5 mm, corner radius ~3 mm. The game's live per-card uGUI Canvas gets re-parented onto its face at runtime (ARCHITECTURE.md §5); front face is a flat area the canvas covers almost edge-to-edge (thin dark edge only), back face carries an opaque decorative card-back material. Candidate source assets + licenses: `unity/CARD-ASSETS.md` |
| `PlayTray.prefab` | Control board (desk-like tray, ~0.64 × 0.32 m board): 2 card slots (first slot = initiative) marked with empty child transforms `Slot1`, `Slot2`; `ShortRestToken` / `LongRestToken` anchor transforms in a visually separated rest zone; **three** button-seat anchor transforms `ButtonSeat1` / `ButtonSeat2` / `ButtonSeat3` in the right-hand zone, one per physical button recess (the mod builds its pokeable keycaps onto them). The legacy spelling `ConfirmButton` / `UndoButton` still resolves as seats 1 and 2 |
| `ReadyButton.prefab` | Physical push button: `Base` + `Cap` child (cap travels ~8 mm on press, animated from code via transform, no Animator). A `LabelAnchor` child positions the world-space TMP label the mod adds at runtime |
| `Button_Ready.prefab` / `Button_Undo.prefab` / `Button_Skip.prefab` | Phase-3c button-cluster variants (probed first; `ReadyButton.prefab` is the shared fallback for all three). Same conventions as `ReadyButton.prefab`: `Base`, travelling `Cap` (put a **BoxCollider** on the Cap — the poke test needs a primitive/convex collider), optional `LabelAnchor`. Suggested cap diameters: Ready ~10 cm, Undo/Skip ~7.6 cm. The mod tints the Cap material at runtime (state accent / disabled dimming), so give the Cap its own material instance |
| `PanelFrame.prefab` (optional) | Decorative frame/backboard for converted world panels (initiative track, element board, combat log, objectives, stat panel). Child `PanelAnchor` marks where the mod parents the panel host canvas; frame should fit a ~1 mm/px canvas (e.g. initiative track ≈ 1.2 × 0.2 m). Purely cosmetic — panels work frameless |
| `WristHud.prefab` (optional) | Watch-style housing for the wrist status HUD (~10 × 6 cm face). Child `FaceAnchor` marks the canvas position; the mod renders its own TMP content onto it |
| `ScreenFrame.prefab` (optional) | Bezel for the floating 2D screen (16:9, ~1.4 m wide at scale 1). Child `ScreenAnchor` marks the RenderTexture quad position |
| `LaserPointer.prefab` | Index-finger ray visual: thin tinted quad/cylinder (~2 mm wide, length scaled from code) + `Dot.prefab` end-point disc. Material must be a self-contained unlit shader (bundled), never built-in Standard |

## P3b runtime contract (feat/cards — what the code actually probes)

Asset paths loaded by `src/GloomhavenVR/Cards/VRCardFactory.cs` (procedural fallback
kicks in when missing, so these are optional but strongly preferred):

| Bundle path | Consumed by |
|---|---|
| `Assets/Bundle/Table/CardBacking.prefab` | instantiated under every `VRCard`'s `Visual` child (procedural fallback: `CardMesh.cs` rounded slab, 1.5 mm thick, procedural back pattern) |
| `Assets/Bundle/Table/PlayTray.prefab` | control-board visual; children looked up by name through `Cards/BoardAnchors.cs`: the four frame anchors `Slot1`, `Slot2`, `ShortRestToken`, `LongRestToken` plus the button seats `ButtonSeat1`, `ButtonSeat2`, `ButtonSeat3` (empty transforms). Seat aliases, tried in this order: seat 1 = `ButtonSeat1` → `ConfirmButton`, seat 2 = `ButtonSeat2` → `UndoButton`, seat 3 = `ButtonSeat3` → `SkipButton`. Every seat is optional: seats 1/2 fall back to procedural anchors, and a board with no `ButtonSeat3` simply runs as the two-seat board it always was |

Orientation convention used by the whole Cards module (must match in the prefabs):

- Card/tray roots are oriented with **+Z pointing away from the player** — the HMD
  is always on the **−Z side**. The live uGUI card face is placed at local
  `z = -0.0012` with identity rotation (uGUI/TMP/Quad all render toward −Z).
- `CardBacking.prefab`: pivot at card center, card plane = local XY, front face
  area flush around `z ≈ 0..+0.0015` (the mod's face canvas floats just in front on
  −Z and covers the front almost edge-to-edge — do NOT paint a wide frame on the
  front); the **opaque card-back material faces +Z**; the rim (edge wall) should be
  a dark neutral. Size 63.5 × 88 mm, thickness ~1.5 mm, corner radius ~3 mm at
  scale 1 (`[Cards] CardWidth` rescales the canvas, not the mesh — keep the asset
  at real poker size).
- `PlayTray.prefab`: the board reads as a desk/lectern — the mod tilts the whole
  root to `[Cards] TrayTilt` degrees from horizontal (default 30) with **−Z facing
  up toward the player**; author the board lying in the local XY plane, board body
  at `z > 0` behind the elements. `Slot1` = initiative slot (left from the player's
  view, gets the numbered badge + INITIATIVE caption at runtime), `Slot2` right;
  slot transforms mark the card CENTER at the card's resting `z = 0` plane.
  `ShortRestToken` / `LongRestToken` anchors get pokeable token widgets attached at
  runtime (keep ~5 cm clearance); place them in a visually separated rest zone
  (left edge recommended). The `ButtonSeat1..3` anchors get the physical keycaps
  (right edge recommended); the mod builds base + travelling cap + label itself,
  so the anchors are plain empties.

### Why THREE button seats (2026-08)

User request, verbatim: *"Wie du mir selbst gesagt hast, ist 3 das Maximum an
gleichzeitigen Knöpfen. Daher möchte alle 3 Boards so umgebaut haben, dass sie auf der
rechten Seite wo die Knöpfe hinkommen 3 statt 2 Slots für die buttons haben."*

Three is a ceiling read off the game, not a round number: `WorldUI/ButtonCluster.cs`
enumerated every `readyButton` / `m_UndoButton` / `m_SkipButton` toggle site in the
decompiled `Choreographer` and found six states where all three are live at once, and
none where a fourth cap the mod draws could join.

Authoring rules for the seats:

- Order them **top to bottom** along the board's short axis: seat 1 is the topmost
  recess, seat 3 the bottom one. The runtime stack is *top-anchored* — seat 1 sits at
  `+spacing/2`, seat 2 at `−spacing/2`, seat 3 at `−3·spacing/2` from the tuned
  `[Cards] ConfirmUndoOffset_{board}` — so seat 1 and seat 2 land exactly where they
  always did and a board that gains seat 3 does not move the other two.
- The runtime cap size is the tuned `[BoardButtons]` W×H (shipped **63 × 65 mm** —
  `Defaults.BoardButtons_Width/Height`, the bound default; `ButtonTuning.DefaultBoardWidth`'s
  0.073 is only a pre-Bind fallback and is never the live cap) **fitted down to this
  board's own recess**. `BuildBoard.cs` measures each recess FLOOR off the mesh and writes
  `SeatExtent1/2/3` empties into the prefab (their localPosition x/y are the recess
  HALF-width and HALF-height in metres, not a position); the mod builds
  `min(tuned, floor − 2 × margin)` per axis, where the margin is `[BoardButtons] Travel`
  clamped to 1–8 mm. The fit only ever SHRINKS — the global stays the ceiling the user
  dialled in. A board with no `SeatExtent` empties (any bundle built before this) gets
  the tuned size unchanged.
- **The same measurement bounds the cap's OFFSET.** `[Cards] ConfirmUndoOffset_{board}`
  and `GenericButtonSpacing_{board}` were tuned against whatever board asset was installed
  at the time — on Steel and Bronze that was a MIRRORED board, so their X is a 46 cm
  relocation, not a nudge. On a board that carries a measurement the in-plane part of the
  offset plus the whole spacing term is clamped to the slack left between the cap and the
  recess wall (Z, the proud depth, is untouched); on a board without one nothing is
  clamped. Same rule, same function, for `RestButtonOffset/Spacing_{board}` against the
  rest pads.
- Measured on the three committed FBXes — recess **floor**, which is what a keycap rests
  on; the rim is 2–10 mm wider and is not usable room:

  | board | seat floor (mm) | fitted cap @4 mm (mm) | in-well slack (± mm) | rest pad (mm) | fitted disc (mm) |
  |---|---|---|---|---|---|
  | Oak | 74.6 × 64.3 | 63.0 × 56.3 | 5.8 × 4.0 | 81.6 | 73.6 (tuned 91) |
  | Steel | 81.0 × 70.1 | 63.0 × 62.1 | 9.0 × 4.0 | 81.7 | 71.0 (unchanged) |
  | Bronze | 61.2 × 51.9 | 53.2 × 43.9 | 4.0 × 4.0 | 68.1 | 60.1 (tuned 71) |

  If a rebuild's log disagrees with the seat/pad columns, the measurement code is wrong,
  not the boards. **VERIFIED against a real rebuild 2026-08-25** (ModBuild 271): every one
  of those six numbers came back exactly, read back off the built prefab at F5 by
  `Editor/PreviewBoard.cs`, from the bundle rather than from the project.
- **The same measurement also bounds the BOARD MESH's own pose.** `[Cards]
  AssetOffset_{board}` and `AssetPitch/Yaw/RollDegrees_{board}` move the mesh while every
  anchor is pinned where it was. `AssetOffset_Bronze` = (0, −0.11, +0.08) and
  `AssetPitchDegrees_Bronze` = 57 were tuned to lay the SHIPPED Bronze — a raked lectern
  301 mm deep — flat. Replayed against the re-authored flat plate they leave three of the
  five seat and rest anchors with no mesh behind them at all and the other two 151.5 /
  167.1 mm off the surface. On a board that carries a `SeatExtent` measurement the pose is
  now clamped so no pinned anchor lifts more than 5 mm (half the budget to the offset, half
  to the tilt, the tilt bound derived as `asin(lift / r)` with `r` the furthest pinned
  anchor); on a board without one nothing is clamped. Re-measured after the clamp: 3.8 mm.

Material:

- `BoardLit` binds `_MainTex`, `_BumpMap` and `_MRSMap`, the last being `<base>_mrs.png`
  packed **R = metallic, G = roughness** and imported as LINEAR data (sRGB off — forced by
  `BuildBoard.ImportAsLinearData`, not trusted to the committed `.meta`). It is NOT the
  glTF ORM packing the texture pipeline's intermediate `<base>_mr.png` uses.
- **Specular is opt-in and the opt-in is the map.** No `_mrs.png` → `_SpecStrength` stays
  at BoardLit's 0 → the shader's specular branch does not execute → bit-identical to the
  build before ModBuild 265. With a map it is set to **0.85**, not the plate gauntlet's 1.0:
  the board is a 0.64 m slab about 40 cm from both eyes, and a sharp view-dependent lobe on
  a 2048² normal map is this project's recurring per-eye aliasing defect. That float is the
  dial to turn first if the rig reports sparkle.
- **Both spellings are shipped in the FBX** (`ButtonSeat1/2/3` plus `ConfirmButton` /
  `UndoButton` / `SkipButton` at bit-identical positions), so one asset works with every
  DLL. `BuildBoard.cs` projects *every* authored anchor empty onto the recess floor, not
  just the canonical one, so an older DLL that resolves only the legacy names seats its
  keycaps in exactly the same place.
- Seat 3 has **no occupant yet**: the mod resolves and poses it, and the turn-flow Skip
  cap moves into it in a later round (it is currently drawn from its own
  `[RoundButtons]` geometry, and a peer's copy of it is derived from wire fields whose
  meaning that move changes). A regenerated board is correct and complete before that
  round lands; the third recess simply reads as empty until then.

Conventions:
- 1 Unity unit = 1 m, real-world sizes; the diorama scaling happens on the rig,
  not on these assets.
- Custom shaders referenced by these materials compile INTO the bundle and are
  safe; do not reference built-in Standard/UI shaders from bundled materials
  (pink-material trap, TOOLCHAIN.md §4.1).
- Everything in this folder except `*.md` / `*.txt` / dotfiles is packed into
  `gloomhavenvr.bundle` by `Assets/Editor/BuildBundles.cs`.
