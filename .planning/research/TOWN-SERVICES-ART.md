# Original town-service artwork

Extracted and visually inspected 2026-09-20 from the user-supplied `ressources/GH_Data/`.
The developer's game graphics are read-only. Exports remain in gitignored debug storage;
they are reference material, not additions to the mod bundle or public source assets.

Local output: `.planning/debug/npc-references/`.
Downloadable local collection: `.planning/debug/npc-references.zip`.

| NPC | Export | Native texture | Serialized source | Texture path ID | Dimensions |
|---|---|---|---|---:|---|
| Merchant | `merchant-original.png` | `GuildBackground_Merchant` | `sharedassets4.assets` | 119 | 1920 x 1080 |
| Priestess | `priestess-original.png` | `Guild_Background_Temple` | `sharedassets4.assets` | 127 | 1920 x 1080 |
| Enchantress / Magierin | `enchantress-original.png` | `Guild_Background_Enchantress` | `sharedassets4.assets` | 96 | 1920 x 1080 |
| Merchant portrait | `merchant-portrait.png` | `Portrait_Merchant` | `sharedassets1.assets` | 855 | 94 x 118 |
| Priestess portrait | `priestess-portrait.png` | `Portrait_Priestess` | `sharedassets1.assets` | 393 | 94 x 118 |
| Enchantress portrait | `enchantress-portrait.png` | `Portrait_Enchantress` | `sharedassets1.assets` | 165 | 94 x 118 |

Corresponding full-size Sprite IDs are 217, 222 and 196; all reference the full texture
with a 1920 x 1080 rectangle. Portrait Sprite IDs are 1683, 1227 and 1007, likewise full rects.
Pixels live in the matching `sharedassets4.assets.resS` and `sharedassets1.assets.resS` streams.
Full illustrations use native DXT1 compression; portraits use RGB24. These assets have no
source transparency. The foreground figure, background and painted shading are baked together.

These are decoded original pixels with no retouching, scaling, segmentation, screenshot
perspective correction or generated content. PNG export avoids adding JPEG compression,
but cannot restore detail lost in the game's original DXT1 source. The 1920 x 1080 figure
illustrations are the modelling references; the small portraits are supplementary face
references, not higher-resolution replacements.

The art establishes front/three-quarter appearance only. Rear views, rigging, hidden
clothing and topology still need artist decisions. In particular the enchantress's pale
mask-like face should be checked before modelling ordinary human eyes or skin there.
Her source suggests levitation; grounded merchant/priestess and controlled enchantress
hover can each remain faithful to their own source.

## Reproduction

The developer extraction script requires Python 3.11+ and UnityPy. This export used
UnityPy 1.25.2 from the existing local tool environment:

```bash
/home/claw/unitypy-venv/bin/python scripts/extract-town-service-art.py \
  ressources/GH_Data .planning/debug/npc-references
```

Choose a new or empty output directory for another run. The script accepts a `GH_Data`
directory, resolves the six textures by their original names and refuses to write inside
the input game data or overwrite a nonempty export directory. It reports missing or
duplicate names instead of silently presenting partial/ambiguous output as complete.

`manifest.json` records texture names, native dimensions/formats, source file and stream,
path IDs, UnityPy version and SHA-256 hashes for source files and PNGs. The archive includes
that manifest and a short reference README. Its images were checked against the source
decodes, and the archive was checked for CRC errors after creation.

Related: [interaction concept](TOWN-SERVICES-CONCEPT.md),
[native function inventory](TOWN-SERVICES-FUNCTIONS.md),
[environment and multiplayer design](TOWN-SERVICES-INTEGRATION.md).
