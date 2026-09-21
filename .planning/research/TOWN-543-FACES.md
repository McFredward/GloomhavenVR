# Build 543 anatomical town faces

## Hardware evidence and scope

`gesicht1.jpg`, `gesicht2.jpg` and `gesicht3.jpg` show the build-542 photographic eyes offset from the generated eyelid relief, particularly the merchant's second lower-eye ridge. A static projected atlas does not create an eye socket, movable eyeball, sealed eyelid or oral cavity. This work replaces that topology while retaining the reviewed NPC identities, original costume/body, station lighting and floor contracts.

## Authoring source

The anatomical head topology and expression targets come from official MakeHuman/MPFB bundled data, explicitly licensed CC0. Only asset data is used; upstream addon/program code is not copied into the mod or its tools.

- Official repository: https://github.com/makehumancommunity/mpfb2
- Pinned revision: `b58176c661a9680294eb75f127842cb8378e4974`
- Mesh: `src/mpfb/data/3dobjs/base.obj`
- Targets: `src/mpfb/data/targets/expression/units/caucasian/`
- Licensing: https://github.com/makehumancommunity/mpfb2/blob/b58176c661a9680294eb75f127842cb8378e4974/LICENSE.md and `LICENSE.ASSETS.md`.

The original approved build-542 front/side/back portraits remain the identity and texture references. Actual orbital, nasal, lip and chin loops are fitted to measured photographic landmarks. The original CC0 blink and mouth-target deformation is transformed through the same fit; it is not applied to a closed generated shell.

## Runtime asset contract

- Existing `Head` and `Neck` bones remain.
- `EyeLeft` / `EyeRight` are separate actual eye pivots, anatomical left/right, local +Z optical forward / +Y up.
- All facial LODs carry `BlinkLeft`, `BlinkRight`, `JawOpen`, `MouthWide`, `MouthRound`, `Smile`, `BrowRaise`, `LidUpLeft`, `LidDownLeft`, `LidUpRight`, `LidDownRight` shapes, 0–100.
- Gaze applies after body sampling. Full blink suppresses additive lid-follow influence to retain closure.
- Teeth, tongue and oral cavity must remain behind the lips at rest and be visible only through the true mouth opening.
- Corneal highlights use actual scene/practical lights, including ForceVertex point-light data with the existing zero pixel-light budget. No fixed painted glint or self-illuminated face.

Final validation and artifact details are recorded below.

## Final authoring changes

The build-542 generated facial shells are removed. The three replacement faces have fitted orbital loops, separate sclera/iris/pupil geometry, transparent corneal caps, closed eyelid targets, a real oral opening, interior mucosa, upper/lower teeth and tongue. All three LODs retain the same eleven channels. Skin, eye and oral textures retain the approved character references and pinned CC0 eye/oral data. There are no generated greeting clips in the assets or bundle.

The original costume vertices, UVs, weights and four body animations remain. Facial shape position and normal deltas on costume vertices are asserted zero. Unkeyed Blender expressions were exported as constant FBX animation tracks; the importer explicitly removes them and eye-pivot tracks, so body sampling cannot overwrite runtime facial motion.

Facial base normals and posed normal deltas are calculated from the same area-weighted topology with a fixed neutral-position seam map. This prevents both imported normal discontinuities and accidentally welding touching opposite lips/lids during closure. Only the facial submesh is processed; unchanged costume normals are retained.

The photographic eye is removed from lid skin with a feathered, two-dimensional sample of the existing cheek skin. A Blender CustomData handle invalidated by adding a color attribute had silently left the lid UV unchanged; the final authoring code reacquires the layer by name and asserts the actual stored UVs of both lids sample skin beyond the original photographed eye. Actual Unity clay/unlit comparison established that the previous broad dark strip was albedo, not an unclosed orbital hole.

The original CC0 game-engine weights are pinned in `scripts/npc-face-template/rigs/weights.game_engine.json`. A template-space submandibular region keeps the complete skull/chin/beard rigid with Head while preserving a soft neck transition below the jaw. Membership is independent of output weights. It survives subdivision and FBX vertex splitting in a second UV metadata channel; `town-facial-rig-contract.json` records 48 spatially spread skull and 48 jaw probes for each resident/LOD, with exact imported vertex counts. The production gaze fixture uses those indices to test actual skin weights and rigid jaw motion.

## Rendering and costs

Each selected LOD draws the original costume and face material plus four eye renderers: two opaque globes and two single-pass transparent corneal caps. The shared eye renderers do not multiply when the LOD changes. Reading the final Windows bundle confirms 2,566 eye vertices and 4,880 triangles per resident (each globe 962 vertices / 1,840 triangles, each cornea 321 / 600). Eye highlights use the existing four vertex-light positions/colors per fragment; no additional light, fixed glint or self-emission is introduced. All shaders retain stereo transforms and station visibility. The renderer's real lighting path was also independently tested with pixel-light count zero and a ForceVertex point light.

LOD0 contains approximately 101,856 triangles per actor, LOD1 38,140 and LOD2 23,139–23,140, excluding the small eye meshes. Unity mesh memory including eleven facial channels is approximately 18–19 MB / 7–8 MB / 4.7–5 MB across these LODs per actor. The unchanged costume has no dense expression deltas. Exact per-resident imported vertex, triangle and memory counts are in the final evidence `*-facial-metrics.txt` files; these are Unity CPU mesh measurements, not a headset GPU/VRAM measurement.

## Visual review and limits

`source-review8` contains approved actual Unity neutral, closed-blink, small speech, oblique/profile and head/eye-extreme views for all residents. It passed 468 assertions and six visual negative controls. The final bundle check repeats these checks against loaded assets, rather than treating source-prefab rendering as sufficient.

The previous stretched mandible, projected double-eye relief, wrong face/body material ordering, large striped experimental collar and broad dark closed-lid stripe are corrected. Original build-542 garment cut edges at some collar/hood boundaries remain visibly imperfect; this facial change does not redesign those costumes. Root reviewed these separately from the corrected anatomical defects. Headset appearance, stereo corneal highlights and sustained performance with several residents still need the user's hardware test. Automated checks and desktop renders do not establish headset quality.

## Final artifacts

- Windows shipping bundle: `prebuilt/ghvr-town.bundle`, **99,211,283 bytes**, SHA-256 `6df700ed2374c7f0f8ab4652363bce54a70ab54e33ee67c6879a889f533a08e3` (below GitHub's 100 MiB file limit).
- Matching Linux review bundle: **99,233,188 bytes**, SHA-256 `be0f46bbe0241b3c2478b7872d5e2308cb701a0e57330eb34d6c08c3f1906b43`.
- Actual loaded-bundle validation: **469 assertions, six visual negative controls**; no generated AudioClip or Audio asset shipped.
- Evidence: worker `.planning/debug/town543-unity/final-evidence/`; exact current inputs are hashed in the adjacent `source-hashes.json`, rendered PNGs in `image-hashes.json`. Windows build log: `/tmp/town543-windows-bundle.log`.
- Root's independent exact-triangle canopy sample check includes the new ±50° yaw / ±22° pitch poses at 360 station bearings with actual floor heights. Minimum sampled clearance: merchant 91.608 mm, priestess 94.662 mm, enchantress 103.061 mm; all exceed the unchanged 28.52 mm wind displacement bound. This is a geometric sampling result, not a hardware guarantee.
