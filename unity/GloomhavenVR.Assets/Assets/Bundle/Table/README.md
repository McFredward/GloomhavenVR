# Bundle content: Table/

Intended assets (Phases 3b `feat/cards` and 3c `feat/world-ui` consume these):

| Asset | Description |
|---|---|
| `CardBacking.prefab` | 3D card backing mesh — rounded rectangle, poker-card aspect (63.5 × 88 mm), slight thickness (~0.6 mm at asset scale 1 unit = 1 m). The game's live per-card uGUI Canvas gets re-parented onto its face at runtime (ARCHITECTURE.md §5); front face is a flat quad area, back face carries a neutral card-back material |
| `PlayTray.prefab` | Floating play tray with 2 card slots (first slot = initiative). Slot positions marked with empty child transforms `Slot1`, `Slot2`, plus `ShortRestToken` / `LongRestToken` anchor transforms |
| `ReadyButton.prefab` | Physical push button: `Base` + `Cap` child (cap travels ~8 mm on press, animated from code via transform, no Animator). A `LabelAnchor` child positions the world-space TMP label the mod adds at runtime |
| `Button_Ready.prefab` / `Button_Undo.prefab` / `Button_Skip.prefab` | Phase-3c button-cluster variants (probed first; `ReadyButton.prefab` is the shared fallback for all three). Same conventions as `ReadyButton.prefab`: `Base`, travelling `Cap` (put a **BoxCollider** on the Cap — the poke test needs a primitive/convex collider), optional `LabelAnchor`. Suggested cap diameters: Ready ~10 cm, Undo/Skip ~7.6 cm. The mod tints the Cap material at runtime (state accent / disabled dimming), so give the Cap its own material instance |
| `PanelFrame.prefab` (optional) | Decorative frame/backboard for converted world panels (initiative track, element board, combat log, objectives, stat panel). Child `PanelAnchor` marks where the mod parents the panel host canvas; frame should fit a ~1 mm/px canvas (e.g. initiative track ≈ 1.2 × 0.2 m). Purely cosmetic — panels work frameless |
| `WristHud.prefab` (optional) | Watch-style housing for the wrist status HUD (~10 × 6 cm face). Child `FaceAnchor` marks the canvas position; the mod renders its own TMP content onto it |
| `ScreenFrame.prefab` (optional) | Bezel for the floating 2D screen (16:9, ~1.4 m wide at scale 1). Child `ScreenAnchor` marks the RenderTexture quad position |
| `LaserPointer.prefab` | Index-finger ray visual: thin tinted quad/cylinder (~2 mm wide, length scaled from code) + `Dot.prefab` end-point disc. Material must be a self-contained unlit shader (bundled), never built-in Standard |

Conventions:
- 1 Unity unit = 1 m, real-world sizes; the diorama scaling happens on the rig,
  not on these assets.
- Custom shaders referenced by these materials compile INTO the bundle and are
  safe; do not reference built-in Standard/UI shaders from bundled materials
  (pink-material trap, TOOLCHAIN.md §4.1).
- Everything in this folder except `*.md` / `*.txt` / dotfiles is packed into
  `gloomhavenvr.bundle` by `Assets/Editor/BuildBundles.cs`.
