# Environments — provenance

Both ambient environments (`Env_Cellar.prefab`, `Env_Swamp.prefab` — the latter
is a night FOREST since the ModBuild 132 round; the file name is a runtime
contract and does not change) are assembled deterministically by
`Assets/Editor/BuildEnvironments.cs` + `Assets/Editor/BuildEnvironmentRooms.cs`:
FX shells (star dome, catalogue stars, mist, wisps, dust) plus full room
interiors under each prefab's `RoomGeo` node. Shaders (`Env*.shader`), sprite
textures, procedural meshes (tree trunks, crowns, canopy, moonlight shafts, the
star-field point sprites), materials and the prefabs are **original work of this
project**. Third-party assets:

## Room models & PBR texture sets (`Imported/`)

All meshes and photo textures under `Imported/Models` and `Imported/Textures`
are from **Poly Haven** (<https://polyhaven.com>), license **CC0 1.0**
(public domain, <https://polyhaven.com/license>) — no attribution required;
credited here with thanks. Modifications: UV-preserving decimation
(MeshLab quadric-with-texture), AO multiplied into albedo, opacity merged
into albedo alpha, resolution reduced (reproducible pipeline script:
`Assets/Editor/polyhaven_pipeline.py` — python3 + pymeshlab + Pillow).

Models (`https://polyhaven.com/a/<name>`):

- cellar: wine_barrel_01, wooden_crate_01, small_wooden_table_01,
  wooden_stool_02, wooden_bookshelf_worn, wooden_bucket_01, jug_01
- forest: dead_tree_trunk, dead_tree_trunk_02, tree_stump_01, tree_stump_02,
  root_cluster_02, single_root, rock_moss_set_01, rock_moss_set_02,
  dry_branches_medium_01, grass_medium_02, fern_02, shrub_03, moss_01,
  wooden_axe_02

Texture sets: medieval_blocks_05 (cellar walls), monastery_stone_floor
(cellar floor/steps), dark_wooden_planks (ceiling/beams), forest_ground_04 +
forest_leaves_04 (forest floor blend), pine_bark + bark_brown_02 (the
procedural conifer trunks). The candle-flame sprite (`candle_flame_alb.jpg`)
is the `flame_diff` map of Poly Haven's brass_candleholders.

### Conifer foliage (`fir_twig_alb.png`)

- **Asset**: the `twig_diff` + `twig_alpha` maps of *fir_tree_01*
  (<https://polyhaven.com/a/fir_tree_01>), Poly Haven, **CC0 1.0**.
- **Why only the maps**: Poly Haven's scanned conifers are 0.5–1 GB
  multi-material photoscans whose alpha twig cards do not survive decimation.
  Their twig ATLAS, however, is exactly what a card-built forest needs — seven
  isolated fir sprigs and one bare branch on clean transparency. The mesh is
  never downloaded; the trunks, crowns and canopy are grown procedurally by
  `BuildEnvironmentRooms.cs` and textured with these real photoscanned needles.
- **Modifications**: diffuse and alpha merged into one RGBA PNG; sub-rects cut
  by connected-component analysis of the alpha channel.

## Night-sky panorama (`Textures/Env_NightSky.png`)

- **Asset**: *Rogland Clear Night* HDRI
- **Author**: Greg Zaal (Poly Haven)
- **Source**: <https://polyhaven.com/a/rogland_clear_night>
  (16k unclipped linear equirect HDR,
  <https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/16k%2B/rogland_clear_night_16k.hdr>)
- **License**: CC0 1.0 (public domain, <https://polyhaven.com/license>) — no
  attribution required; credited here with thanks anyway.
- **Modifications**: tone-mapped to LDR night exposure, terrain silhouette
  replaced by a starlit mist band, cropped to the -20..+90° elevation band,
  star-core mask synthesized into the alpha channel. The exact processing
  script is embedded as a comment at the end of `Assets/Editor/BuildEnvironments.cs`.
  Since the ModBuild 132 round this layer is used as the **Milky-Way backdrop
  only**: `EnvStars.shader` suppresses its own star cores against that alpha
  mask, so the point stars all come from the catalogue layer below.

## Star catalogue (`Assets/Editor/bsc5_stars.csv` → the star-field mesh)

- **Data**: *Bright Star Catalogue, 5th Revised Edition (Preliminary)* —
  Hoffleit D. & Warren Jr. W.H., 1991, Astronomical Data Center, NSSDC/ADC.
- **Source**: <http://tdc-www.harvard.edu/catalogs/bsc5.dat.gz> (Harvard/NASA
  mirror; CDS mirror <https://cdsarc.cds.unistra.fr/ftp/V/50/catalog.gz>)
- **License**: a NASA/NSSDC product — a US Government work, not subject to
  copyright; star positions and magnitudes are uncopyrightable facts in any
  case. No attribution required; credited here.
- **Use**: fetched and reduced by `Assets/Editor/star_catalogue.py` to
  right ascension / declination / V magnitude / B−V for the 8404 stars down to
  V=6.5. `BuildEnvironments.BuildStarField` turns the 5080 stars brighter than
  V=6.0 into one camera-independent quad each; `EnvStarPoints.shader` rotates
  them about the celestial pole and twinkles them. The raw CSV lives under
  `Assets/Editor/` and is therefore editor-only — it never enters the bundle or
  the player build; only the derived mesh ships.
- **B−V → colour**: Ballesteros' blackbody fit (Ballesteros, F. J., 2012,
  *"New insights into black bodies"*, EPL **97**(3) 34008, arXiv:1201.1809)
  followed by Tanner Helland's Kelvin→RGB approximation
  (<https://tannerhelland.com/2012/09/18/convert-temperature-rgb-algorithm-code.html>).
  Both are published numerical fits, reimplemented from the formulae.
- **Twinkle**: amplitude follows the Rozenberg (1966) airmass approximation.
- **Dither**: the sky gradient is dithered with interleaved gradient noise
  (Jimenez, *Next Generation Post Processing in Call of Duty: Advanced
  Warfare*, SIGGRAPH 2014) — a published one-line hash, reimplemented.

## History

Earlier iterations assembled full low-poly rooms from CC0 (public domain)
Quaternius model packs ("Modular Dungeon Pack", "Ultimate Nature Pack",
quaternius.com). That geometry was removed when the game took over scenario
geometry generation (Apparance); CC0 required no attribution — this note is
kept for provenance only. The night-marsh dressing that preceded the forest
used brown_mud_leaves_01, dead_quiver_trunk and namaqualand_boulder_05
(also Poly Haven CC0); they were dropped with the swamp.
