# Merchant cabinet asset review

`front-concept.png` and `mechanisms-concept.png` are visual targets. The empty-front concept guided the carved housing; the original item cards, six category controls, crank, and lantern remain separate runtime objects.

`implemented-controls-proxy.png` is a Blender render from the final exported meshes at close viewing distance. Its six letter/number glyphs and white card rectangles are **layout proxies**, not game artwork. At runtime, `TownServiceCatalogCategory` puts the original item-slot sprites on the six physical controls, and the catalog creates the original item cards. Those original sprites and cards cannot load in the isolated asset render; their final headset appearance still needs hardware verification.

The housing was generated once with Hunyuan3D v3 from the selected concept views, then reduced, cleaned, UV mapped, textured, and fitted to the native catalog layout. It contains no baked cards or controls. The authored controls and lantern support use separate meshes and transforms so input and animation remain functional.

Provider spending for this cabinet lane was approximately $0.975: a $0.30 Trellis2 exploration that was rejected because it fused cards and controls to the shell, followed by the $0.675 Hunyuan3D v3 multiview/PBR generation used here. No provider credentials or raw responses are committed.

Final exported presentation: 74,900 triangles including six controls, 54 mesh renderers before batching, and five functional material classes. The housing itself is 42,142 triangles with one renderer and one PBR material. The isolated Unity 2021.3 asset validation passed 951,257 assertions and nine visual negative controls after rebuilding the merchant prefab from these assets. The catalog interaction harness passed 22,895 production assertions. These checks cover import, layout, and interaction; they do not establish stereo headset quality or the game's original sprite appearance.
