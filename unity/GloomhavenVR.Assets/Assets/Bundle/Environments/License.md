# Environments — provenance

Both ambient environments (`Env_Cellar.prefab`, `Env_Swamp.prefab`) are FX-only
shells: shaders (`Env*.shader`), procedural textures (`Textures/`), generated
meshes (`Meshes/`), materials and the prefabs are **original work of this
project**, generated deterministically by `Assets/Editor/BuildEnvironments.cs`.
No third-party assets remain in this folder.

History: earlier iterations assembled full low-poly rooms from CC0 (public
domain) Quaternius model packs ("Modular Dungeon Pack", "Ultimate Nature Pack",
quaternius.com). That geometry was removed when the game took over scenario
geometry generation (Apparance); CC0 required no attribution — this note is
kept for provenance only.
