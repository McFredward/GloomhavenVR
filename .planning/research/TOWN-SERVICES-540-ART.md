# Town service art: build 539 hardware diagnosis and correction

The supplied `LogOutput.log` and `Player.log` identify build 539 and successful
`TOWN SERVICE OPEN service=1 session=1` creation. The remote log is build 500 and
cannot establish current multiplayer behavior. No current screenshot was supplied
for the town-service failure. Asset loading alone did not prove visible rendering.

## Source and pixel evidence

1. All three shipped actor LOD groups had approximately **0.0175 m** world size,
   while their rendered skinned bodies were **1.75 m** tall. The FBX renderer child
   carries a 100x transform; its group does not. Unity 2021's `RecalculateBounds`
   serialized the skin-local extent without that transform, even after sampling
   idle. Automatic LOD therefore culled every actor at ordinary viewing distance.
   Correct group bounds explicitly transform the renderer's world-bound corners
   into group space, plus a 0.12 m gesture margin. Group sizes are now 1.869894,
   1.870189 and 1.870982 m. The existing three LOD meshes and thresholds remain.
2. The Standard surface shader required scene lighting. The supplied map census
   reports zero enabled lights and a pixel-light cap of zero. A black-ambient,
   zero-light render reproduces black NPC/furniture silhouettes with the old shader.
   The corrected single-pass shader follows the existing board's self-contained
   studio-light approach: an ambient floor, fixed key/fill directions, original
   albedo/normal/metallic detail and eye-correct specular response. It adds no lights,
   does not change scene lighting, and retains stereo routing and synchronized
   object-space dissolve. Other environments and the main bundle are unchanged.

The merchant countertop is 1.65 m wide and approximately 0.80 m deep so the new
physical catalogue and tray fit. Its purely decorative coins move behind the ledger,
away from selectable cards. Shrine and enchantress furniture dimensions are unchanged.
Only intended prefab transform/LOD values changed; meshes, rigs, clips and textures
were not regenerated and no paid API calls were made.

## Validation and limits

`python3 scripts/check-town-service-assets.py` creates an isolated Unity 2021.3.5
project, builds the actual source assets into a Linux review bundle, and renders them
through a mod-layer camera with zero scene lights and black ambient. The shipping
Windows/D3D bundle is built separately from the same inputs. Linux shader pixels do
not establish Windows stereo/headset output; this distinction is explicit because the
Windows bundle has no GL shader bytecode and appears magenta in a Linux editor.

The retained run passes **44 assertions and six visual negative controls** across all
three actors: human-sized automatic LOD, visible textured actors and furniture,
intermediate/zero dissolve, greeting pixel changes and equivalent visibility at the
198x map world scale. Merchant geometry and decorative coin clearance are checked.
Restoring the old tiny LOD removes the actor from rendered pixels; restoring the exact
historical shader blacks out both actor and furniture. The actor pixel region excludes
the shrine candles so their surviving flame pixels cannot falsify the culling control.

Rendering uses actual Editor frame boundaries. Multiple camera renders in one frame
reuse skinned output, so the earlier same-frame animation experiment was an invalid
instrument, not evidence of a runtime animation defect. Production animation sampling
was left unchanged.

Evidence: `.planning/debug/town-service-assets-540/`. Build/package hashes are recorded
in its manifest. Headset confirmation of NPC visibility, textures, counter card/tray
clearance, animation and shared multiplayer presentation remains necessary.
