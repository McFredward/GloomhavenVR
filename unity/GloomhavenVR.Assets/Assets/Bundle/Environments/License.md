# Environments — provenance

Both ambient environments (`Env_Cellar.prefab`, `Env_Swamp.prefab`) are
assembled deterministically by `Assets/Editor/BuildEnvironments.cs` +
`Assets/Editor/BuildEnvironmentRooms.cs`: FX shells (star dome, fog,
fireflies, dust) plus full room interiors under each prefab's `RoomGeo` node.
Shaders (`Env*.shader`), sprite textures, generated/procedural meshes,
materials and the prefabs are **original work of this project**. Third-party
assets:

## Room models & PBR texture sets (`Imported/`)

All meshes and photo textures under `Imported/Models` and `Imported/Textures`
are from **Poly Haven** (<https://polyhaven.com>), license **CC0 1.0**
(public domain, <https://polyhaven.com/license>) — no attribution required;
credited here with thanks. Modifications: UV-preserving decimation
(MeshLab quadric-with-texture), AO multiplied into albedo, opacity merged
into albedo alpha, resolution reduced (reproducible pipeline script:
`Assets/Editor/polyhaven_pipeline.py` — python3 + pymeshlab + Pillow).

Models (`https://polyhaven.com/a/<name>`): wine_barrel_01, wooden_crate_01,
small_wooden_table_01, wooden_stool_02, wooden_bookshelf_worn,
wooden_bucket_01, jug_01, dead_quiver_trunk, dead_tree_trunk,
dead_tree_trunk_02, tree_stump_01, root_cluster_01, rock_moss_set_01,
namaqualand_boulder_05, dry_branches_medium_01, grass_medium_02, fern_02.

Texture sets: medieval_blocks_05 (cellar walls), monastery_stone_floor
(cellar floor/steps), dark_wooden_planks (ceiling/beams),
brown_mud_leaves_01 + forest_leaves_04 (swamp ground). The candle-flame
sprite (`candle_flame_alb.jpg`) is the `flame_diff` map of Poly Haven's
brass_candleholders.

## Night-sky panorama (also Poly Haven CC0):

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
  star-core twinkle mask synthesized into the alpha channel. The exact
  processing script is embedded as a comment at the end of
  `Assets/Editor/BuildEnvironments.cs`.

## History

Earlier iterations assembled full low-poly rooms from CC0 (public domain)
Quaternius model packs ("Modular Dungeon Pack", "Ultimate Nature Pack",
quaternius.com). That geometry was removed when the game took over scenario
geometry generation (Apparance); CC0 required no attribution — this note is
kept for provenance only.
