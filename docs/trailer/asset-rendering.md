# Original-asset trailer staging

`scripts/trailer/render_assets.py` is an offline Blender 4.2 staging prototype.
It reads the shipped mod FBX meshes and their original loose albedo/normal/MRS
textures from `unity/GloomhavenVR.Assets/Assets/Bundle`. It never writes to source
assets, game references, Unity prefabs, the bundle or game state. No paid API is
used by this tool.

The intended plates are a steel-mask closeup, physical hands at the original play
tray, a three-mask multiplayer ensemble and a transparent asset reference sheet.
The avatars remain floating masks and hands; no bodies or invented costumes are
added. The wall, table and candles are trailer set dressing. Table and masonry
surfaces use original environment textures, but the staging is not a recreation
of the game's authored environment prefab.

## Presentation and provenance

This is cinematic staging, not a gameplay screenshot or a visual parity test.
Principled PBR, scene lighting, depth of field and restrained bloom are adapted
for film; the runtime uses HeadUnlit and BoardLit. The original mask and hand
topology, UVs and albedo are retained. A staging copy can be posed through the
shipped hand armature. No remeshing, subdivision, image retouch or generated
lettering is performed. Painted cyan and purple pixels can emit through an
albedo-derived mask; no additional runes are authored.

Each invocation produces `render-manifest.json` with absolute input/output paths,
SHA-256 hashes, source sizes, mesh instances, imported bounds, staging transforms,
Blender version and render settings. A supplied card texture is also hashed. A
separate provenance manifest should accompany decoded native game artwork and
identify its original serialized asset and path ID. Keep these manifests beside
the private captures.

## Running

Run from the repository root with an output directory outside read-only game
references:

```sh
/home/claw/blender-4.2/blender --background \
  --python scripts/trailer/render_assets.py -- \
  --output .planning/trailer/render/review \
  --shots mask_hero,hands_table,party_ensemble,asset_reference \
  --width 1920 --samples 128 --save-blend
```

`--assets` selects another checkout's bundled-source directory. `--card-texture`
accepts an exact decoded native card-art PNG, with no invented card name, value
or text. Omit it when complete approved artwork is unavailable. `--save-blend`
preserves editable staged scenes. `--shots` limits work to individual approved
shots; prototype checks can use `--width 960 --samples 48`.

Outputs belong in private planning evidence, not the source commit. After Blender
exits, preserve the PNGs, manifest, render log, exported artwork, source identity
and any current `.blend` deliverable. Remove only owned regenerable caches and
obsolete Blender backup files; do not sweep shared source files, borrowed game
references, captures, `.keep` pins or another worker's active run.

## Current checkpoint: concept review pending

The maintainer requested a full scene concept before further image or video
production. Rendering stopped at local 960x540 pilots. The latest private set is
`.planning/trailer/render/pilot-v2/`: all three intended composition studies plus
an RGBA asset sheet. These are prototypes, not approved final plates. No paid
generation or finished 1920x1080 plate is claimed.

The steel-mask pilot is the strongest current composition. The hand insert still
needs wider framing and a review of the original play tray's decorated-face
orientation: the current view shows its plain surface. The ensemble clips the
central mask and the near tray. The reference sheet needs a palm/back orientation
review for the arcane runes. Resolve these visually after concept feedback before
using the plates as image-to-video inputs.

An exact `AC_Brute_Background` texture was decoded read-only from the original
`character_gui_assets_ac_brute_background.bundle`, with source/PNG hashes and
path ID recorded privately. It is an unpopulated native background, not a
complete ability card. It was not placed in the pilot renders. Do not present it
as a completed in-game card.

Validation at this checkpoint consists of successful Blender smoke renders,
visual inspection of all four pilots and manifest/source identity review. No
runtime implementation changed; no game build, headset result or complete local
gate is claimed.
