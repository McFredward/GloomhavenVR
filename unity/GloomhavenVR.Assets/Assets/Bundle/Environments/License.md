# Environments — provenance

Both ambient environments (`Env_Cellar.prefab`, `Env_Swamp.prefab`) are FX-only
shells assembled deterministically by `Assets/Editor/BuildEnvironments.cs`.
Shaders (`Env*.shader`), sprite textures, generated meshes, materials and the
prefabs are **original work of this project** — with one third-party asset:

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
