# Bundle content: Table/

Intended assets (Phases 3b `feat/cards` and 3c `feat/world-ui` consume these):

| Asset | Description |
|---|---|
| `CardBacking.prefab` | 3D card backing mesh — rounded rectangle, poker-card aspect (63.5 × 88 mm), slight thickness (~0.6 mm at asset scale 1 unit = 1 m). The game's live per-card uGUI Canvas gets re-parented onto its face at runtime (ARCHITECTURE.md §5); front face is a flat quad area, back face carries a neutral card-back material |
| `PlayTray.prefab` | Floating play tray with 2 card slots (first slot = initiative). Slot positions marked with empty child transforms `Slot1`, `Slot2`, plus `ShortRestToken` / `LongRestToken` anchor transforms |
| `ReadyButton.prefab` | Physical push button: `Base` + `Cap` child (cap travels ~8 mm on press, animated from code via transform, no Animator). A `LabelAnchor` child positions the world-space TMP label the mod adds at runtime |
| `LaserPointer.prefab` | Index-finger ray visual: thin tinted quad/cylinder (~2 mm wide, length scaled from code) + `Dot.prefab` end-point disc. Material must be a self-contained unlit shader (bundled), never built-in Standard |

Conventions:
- 1 Unity unit = 1 m, real-world sizes; the diorama scaling happens on the rig,
  not on these assets.
- Custom shaders referenced by these materials compile INTO the bundle and are
  safe; do not reference built-in Standard/UI shaders from bundled materials
  (pink-material trap, TOOLCHAIN.md §4.1).
- Everything in this folder except `*.md` / `*.txt` / dotfiles is packed into
  `gloomhavenvr.bundle` by `Assets/Editor/BuildBundles.cs`.
