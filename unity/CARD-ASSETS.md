# CARD-ASSETS — openly licensed 3D card & table assets for the bundle (research, 2026-07-17)

Hardware test #10: "cards look flat … I want REAL 3D cards" + explicit request to
research suitable assets online. The mod now ships a procedural 3D card body
(`src/GloomhavenVR/Cards/CardMesh.cs`: rounded slab, 1.5 mm thick, dark rim,
procedural card-back pattern) as the fallback; this doc lists verified drop-in
candidates for the companion Unity project (`unity/GloomhavenVR.Assets`). The code
auto-prefers bundle assets when present (`VRCardFactory` probes
`Assets/Bundle/Table/CardBacking.prefab` / `PlayTray.prefab` first — see the asset
contract in `GloomhavenVR.Assets/Assets/Bundle/Table/README.md`).

Licenses were verified per asset (page or Sketchfab API, 2026-07-17). CC-BY-NC and
Sketchfab-standard-license items were excluded.

## Best picks

| Slot | Asset | License |
|---|---|---|
| (a) Card mesh | **KayKit : Board Game Bits** — https://kaylousberg.itch.io/board-game-bits | CC0 |
| (b) Card back / material | **Kenney Playing Cards Pack** faces/backs + ambientCG paper/fabric roughness | CC0 |
| (c) Tray / table | **Poly Haven wooden_table_02** — https://polyhaven.com/a/wooden_table_02 | CC0 |

The whole recommended stack is CC0 — no attribution requirements in the bundle.

## Card meshes

1. **KayKit : Board Game Bits** — https://kaylousberg.itch.io/board-game-bits —
   Kay Lousberg — **CC0** — OBJ/FBX/glTF. Playing cards, dice, dominoes, meeples,
   tokens, coins. Cards use individual textures (easy to swap in our live-canvas
   face/own back). Engine-ready. *Caveat:* verify at download which card texture
   variants sit in the free tier vs the paid "extras" tier (meshes are in the base pack).
2. **Quaternius — 3D Card Kit: Fantasy** — https://quaternius.com/packs/3dcardkitfantasy.html —
   **CC0** — FBX/OBJ/glTF. 60+ fantasy card meshes with real card geometry, single
   1024 atlas; fantasy framing matches Gloomhaven better than poker styling.
3. **52-Card Deck** — https://sketchfab.com/3d-models/52-card-deck-dc2e1196295e45649d4471791ed23f5b —
   Yanez-Designs — **CC-BY** (verified downloadable via Sketchfab API) — glTF+original.
   ~60 tris/card → extruded thickness + rounded corners. *Caveat:* 54 materials /
   55 textures — atlas before bundling; needs a credits line.
4. **Card deck (Tarotscope VR prototype)** — https://sketchfab.com/3d-models/8cffe1ddf73a4195926e7400261e65a1 —
   carolinaask — **CC-BY** — glTF. Tarot proportions, 2,176 faces, authored for VR
   hand interaction.
5. **Game Ready Playing Card Asset Pack** — https://sketchfab.com/3d-models/game-ready-playing-card-asset-pack-892c388e0e3e494c959205ebb12e8d2d —
   Oxygen3D — **CC-BY** — FBX/glTF. 4 decks (simple/fantasy/anime/cyberpunk),
   9.2k tris total; the fantasy deck is a plausible fit.
6. **Poker Pack** — https://opengameart.org/content/poker-pack — mehrasaur —
   **CC0** — .blend/.obj. Low-poly poker **table**, 11 chips, 1 card mesh.

## Card faces / backs / materials

7. **Kenney — Playing Cards Pack / Board Game Pack** — https://kenney.nl/assets/playing-cards-pack ,
   https://kenney.nl/assets/boardgame-pack — **CC0** — 2D vector/PNG. No 3D meshes in
   Kenney's board-game series (confirmed, all four packs are 2D) — but ideal clean
   back/face textures to project onto the card mesh.
8. **ambientCG Fabric category** (e.g. Fabric019) — https://ambientcg.com/view?id=Fabric019 —
   **CC0** — full PBR map sets 1K-8K. Tint for poker-felt tray lining / card-stock
   roughness.

## Tray / table

9. **Poly Haven — wooden_table_02** — https://polyhaven.com/a/wooden_table_02 —
   Serhii Khromov — **CC0** — blend/glTF/FBX/USD. Complete table, 196 tris, 4K PBR.
10. **Poly Haven — wood_table_001 (texture)** — https://polyhaven.com/a/wood_table_001 —
    Dimitrios Savva / Rico Cilliers — **CC0** — PBR set up to 16K. Varnished tabletop
    for a custom-modeled tray board.

## Rejected after license check

- "Free Playing Cards" by 1Poly (Sketchfab) — not downloadable, no license.
- JDSherbert 3D Playing Cards (itch.io) — paid, license terms unverifiable.
- Sketchfab tarot single-card sculpts — CC-BY but 16k-300k faces, too heavy for VR fans.

## Integration notes (companion project)

- Import the glTF/FBX variant; author `CardBacking.prefab` to the contract in
  `GloomhavenVR.Assets/Assets/Bundle/Table/README.md` (pivot card-center, XY card
  plane, front flush z≈0..+0.0015, back material on +Z, real poker size 63.5×88 mm,
  ~1.5 mm thickness).
- Materials must use bundled/custom shaders, never scene-referenced built-in Standard
  (pink-material trap, TOOLCHAIN.md §4.1).
- CC-BY items (3-5) need a credits line in the mod README if used; the best-pick
  stack is CC0 and needs none.
