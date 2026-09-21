# Town-service source record

Furniture geometry and bounded station motions are authored by the project scripts. Wood and stone reuse the existing Poly Haven CC0 dark_wooden_planks and monastery_stone_floor assets; see `Assets/Bundle/Environments/License.md`. Native tabletop decoration is loaded from original game assets at runtime.

Build 542 replaces the neutral heads while retaining the original costumes and body proportions. The new heads have separate 4K reference-projected albedo and dedicated LOD geometry. The merchant uses a reviewed Trellis 2 reconstruction with closed surface repair; the women use Hunyuan3D v3 multiview sources. Raw provider files remain outside the bundle and source control. Detailed acquisition, rejected-source diagnosis and visual validation are in `.planning/research/TOWN-542-FACES.md`.

The existing rig provides bounded station motions only. No facial animation, mouth interior, eye tracking, general locomotion or validated finger grasping is claimed. These are first headset-review assets, not a statement of final photoreal quality.

## merchant

- Raw head GLB SHA-256: `9837b103eaf7796550559d6ac90208498e2242cdb769416bcd5bb28551b51b16`.
- Head request receipt SHA-256: `fd086cc7a4c86cbfd46dee554d88e2a07f73e74a194e27dc361f5c99746287ef`.
- Combined preparation manifest SHA-256: `c10641e2bf7a12175ab0805e3c39987cdbd825d5301c1b3457d8348ca807147f`.
- Bundled rig FBX SHA-256: `62bb9723cecb883b89b0b9dfdcbf0ced6d0f190aa87162148de6ae339bd31cff`.
- Original costume GLB SHA-256: `27a85d8b6e4217219b475a5685072a0431444c0233b65a0eef56e9ff6822cf28`.

## priestess

- Raw head GLB SHA-256: `d20868f08d1f8d57da533382451dcf4c100561d890b16c98b284979a2353d5f8`.
- Head request receipt SHA-256: `f080181d443a5a20788142e605f540270252f6ced12e5788138fcde15de2d96e`.
- Combined preparation manifest SHA-256: `b18ebe07c61aaeeaef4147631cc47bc0d03d577d6ac4207322531c123a5d1936`.
- Bundled rig FBX SHA-256: `cf15b960c4658f018a03a50721eba847fb7a08a00fb03025715c104dfdfae23f`.
- Original costume GLB SHA-256: `b6956c95689bde14127ccdda2ead71022d4def2521e266c120634aeec095bb2a`.

## enchantress

- Raw head GLB SHA-256: `c5f19c4b8aaa7496679c8ae5ec1fcb76299803513cadcd197303b77fdc005fdc`.
- Head request receipt SHA-256: `2cd1ea383b9b4b0d20f7fc05344fa568f2f3f5360df7bfea6d48f8c9499407ba`.
- Combined preparation manifest SHA-256: `8cc8eca898bb6e9238ed9030d5e4db2a943484aa146cc2a46274c4690a8c6e32`.
- Bundled rig FBX SHA-256: `023950a14df567ba2405e5a209a3196d92c0a6e99a828734bed8b6eb911f787d`.
- Original costume GLB SHA-256: `6923aceada531767169e9071c07057379a372f11dbb242d3843e50424f8f8f0e`.

## Import and validation

- Blender 4.2.22 LTS; Unity 2021.3.5f1; StandaloneWindows64 shipping bundle with type trees.
- One skinned renderer per LOD, two materials (costume and head). Intended triangle budgets: 100k / 40k / 13k; final imported counts are recorded in the build-542 evidence.
- Face and costume albedo: 4K BC7. Costume normal: 2K BC5. Costume metallic/smoothness mask: 1K DXT5. Repeated furniture tiles: 1K.
- Face corner normals use area-weighted spatial smoothing within 3mm (merchant) / 2.5mm (women), preserving positions, UVs and costume normals.
- Scene ambient/main/vertex lighting, original visibility dissolve and stereo support; no artificial studio-light floor.
- Original flame atlases use the shared station clock and central-eye billboard.
- Posed soles are measured with explicit CPU skinning, cross-checked against an explicitly scaled Unity bake. Runtime terrain offsets remain additive.
