# Bundle content: Table/

Intended assets (Phases 3b `feat/cards` and 3c `feat/world-ui` consume these):

| Asset | Description |
|---|---|
| `CardBacking.prefab` | 3D card body — rounded rectangle, poker-card size (63.5 × 88 mm at 1 unit = 1 m), REAL thickness ~1.5 mm, corner radius ~3 mm. The game's live per-card uGUI Canvas gets re-parented onto its face at runtime (ARCHITECTURE.md §5); front face is a flat area the canvas covers almost edge-to-edge (thin dark edge only), back face carries an opaque decorative card-back material. Candidate source assets + licenses: `unity/CARD-ASSETS.md` |
| `PlayTray.prefab` | Control board (desk-like tray, ~0.64 × 0.32 m board): 2 card slots (first slot = initiative) marked with empty child transforms `Slot1`, `Slot2`; `ShortRestToken` / `LongRestToken` anchor transforms in a visually separated rest zone; optional `ConfirmButton` / `UndoButton` anchor transforms (the mod builds its pokeable buttons onto them — right-hand zone recommended, confirm ~11 × 6 cm, undo ~9 × 4 cm) |
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
| `Assets/Bundle/Table/PlayTray.prefab` | control-board visual; children looked up by name: `Slot1`, `Slot2`, `ShortRestToken`, `LongRestToken`, `ConfirmButton`, `UndoButton` (empty transforms; the last two are optional — procedural anchors are created when missing) |

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
  (left edge recommended). `ConfirmButton` / `UndoButton` anchors get the physical
  confirm/undo buttons (right edge recommended); the mod builds base + travelling
  cap + label itself, so the anchors are plain empties.

Conventions:
- 1 Unity unit = 1 m, real-world sizes; the diorama scaling happens on the rig,
  not on these assets.
- Custom shaders referenced by these materials compile INTO the bundle and are
  safe; do not reference built-in Standard/UI shaders from bundled materials
  (pink-material trap, TOOLCHAIN.md §4.1).
- Everything in this folder except `*.md` / `*.txt` / dotfiles is packed into
  `gloomhavenvr.bundle` by `Assets/Editor/BuildBundles.cs`.
