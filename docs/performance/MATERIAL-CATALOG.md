# Complete native material source catalog

The 2026-10-06 audit at `dev` base `9fb092a72` reads the complete supplied
`ressources/GH_Data` serialized content, including both installed DLC content
roots. It is not limited to the Crypt, the current scenario, or materials that
happened to be visible in a hardware run. It changes no game data.

The source census contains **3,286 sources / 12,628,549,949 bytes**: all **3,255
Addressables bundles**, 15 root `.assets` files, 13 levels, `globalgamemanagers`
and both Unity builtin resource files. All **9,197 Material objects**, **760
Shader objects** and **156 material families** are counted. There are **zero
object-read errors** and no silently skipped serialized source. Raw texture/audio
sidecars, artwork, procedural configuration, managed libraries and videos are not
serialized Material containers and are not exported. Dynamically constructed
runtime materials and active scene usage remain separate runtime concerns.

All **9,142 non-null shader PPtrs** resolve by their exact owner source,
serialized file, external file index and object path ID. **55 materials contain
an explicit null shader pointer** and remain excluded. Consequently
`objectScanComplete=true`, while `shaderResolutionComplete=false`; the latter
is deliberately not presented as universal successful shader resolution. Global
path-ID/name matching, guessed externals and conflicting duplicate CAB payloads
are rejected. Identical repeated source targets keep every provenance identity.

The original Addressables catalog has **16,626 keys / 14,814 locations** and
references exactly all 3,255 physical bundles: **zero missing** and **zero
uncatalogued** bundles. Exact dependency associations cover:

| Native catalog path group | Entries | Bundle dependency closure |
|---|---:|---:|
| `DLC_JoTL` | 1,389 | 392 |
| `DLC_Solo` | 252 | 50 |
| DLC-labelled paths outside a product directory | 423 | 80 |

Closures overlap and are not additive. These are original catalog path groups,
not inferred licence ownership. Every referenced dependency was scanned.

The tracked [catalog](../../tools/material-audit/catalog.json) records every
source hash, shader family, saved property schema/range, keyword, render queue,
shader raw/program signature and overlapping exclusion count. The
[material index](../../tools/material-audit/material-index.jsonl) records every
material individually, including excluded/null cases and exact shader target
indices into the catalog's `shaderIdentityTable`. **All 149 material families
outside the seven populated reviewed families remain excluded**; their exact
names, counts and reasons are retained in these machine-readable records.
Unused reviewed `Legacy Shaders/Diffuse` shaders have no bound Material object.
No artwork, textures, meshes, shader payload or game code is tracked.

## Positive simplification contract

The offline screen finds **1,044 candidates**; it does not grant runtime renderer
admission or promise a performance improvement. Counts are serialized asset
copies, not distinct GPU materials, visible objects or draw calls.

| Family | Material objects | Offline candidates |
|---|---:|---:|
| `Amp_Basic_N_MRAO` | 394 | 311 |
| `Amp_Low/Amp_Basic_N_MRAO_Low` | 386 | 355 |
| `Amp_Basic_WallFade` | 172 | 119 |
| `Amp_Low/Amp_Basic_WallFade_Low` | 173 | 125 |
| `Amp_Basic` | 31 | 14 |
| `Amp_Low/Amp_Basic_Low` | 32 | 17 |
| `Standard` | 119 | 103 |

[Runtime contracts](../../tools/material-audit/runtime-contracts.json) specify
the positive requirements and exclusions. Emission, advanced emission, moss,
Fresnel and vertex animation are vetoed by both serialized scalars and actual
active keywords. A disabled scalar does not invalidate an enabled compiled
keyword branch. Unknown/transparent queues, disabled native passes and malformed
non-finite values remain native. Prop, character, foliage, panning, unseen,
transparent, decal, particle/VFX, UI/font, video, water and world-map families
remain native until their independent equations and usage are supported.

HIGH and LOW bindings differ. HIGH uses native texcoord ST before scalar UV
tiling/offset, its own signed triplanar weighting, tint/desaturation/boost and
native dim state. LOW uses MainTex ST and native global UVTile/weighting; it has
different alpha and emission controls. WallFade, Basic and N_MRAO routes require
their own native clipping equations, global map/depth channels and caster rules.
The independently owned WorldSimpleMaterial shader lane derives the actual
native equations and owns pixel/causal proof; this source catalog does not
substitute property schemas for that proof.

`Standard` admission is **opaque Mode0 only**, with source-one/destination-zero
blend and depth writes. Both actual native Standard objects lack compiled
ALPHATEST main programs despite exposing the keyword/property. Mode1, ALPHATEST,
emission, detail, parallax, alpha blending and premultiplication stay native. An
official Unity cutout equation is not proof of this shipped native branch.

Runtime admission also requires proven original static-world ancestry and exact
source ownership. Preserve geometry, native renderer visibility, controllers,
per-renderer/per-slot MPBs, globals and dynamic fade/dim state. Actor, animated,
interactive, UI, particle and mod/clone descendants are excluded. Restore owned
originals on option-off, unload, source mutation, clone preparation or failed
proof. Floor never-fade requires immediate native identity **and** floor-plane
proof: Frame615 already showed that a floor-labelled material can carry a live
native fade channel. The previous missing-room combination experiment provides
no permission to broaden visibility ownership.

## Compiled source variants and limitations

Program metadata was independently reparsed for all **760 Shader objects in
404 source files**. Every material family's raw variants include compressed
program SHA-256 signatures and byte extents. Different blobs are never assumed
semantically equal. Shader property/keyword declarations do not prove that a
specific compiled branch survived stripping.

The source-proven differences reported by the shader lane are explicit:

- HIGH `resources.assets:559` has an 83,361-byte program blob
  (`9d3049783115c8208091b901185805c347918492a2a7a83db8edc000b4a8421f`),
  versus 308,551 bytes in the full bundle
  (`e3128acda96b1b555cce6599a3c776a207fad55f1fa56e75ca7a8ab50633709a`).
  The root has no WORLDSPACE main branch. All 13 actual consumers in resources,
  sharedassets4 and sharedassets9 currently have world projection disabled.
- LOW `sharedassets9.assets:30` has a 41,672-byte blob
  (`93cdbecf762c46f1d3366568529bb3cf5a36045d6703aab7fd3b8355f5320cb2`),
  versus 245,167 bytes in the full bundle
  (`b1eada485cff0be4fb8c5c0b61dce0aa8f1fbeacf313a7961d7bf7199a16334d`).
  The root has no WORLDSPACE/diffuse-alpha main branches. Its five city/table
  material consumers currently have both states disabled.
- These HIGH pairs share 3 passes/32 declared keywords/39 properties; the LOW
  pairs share 6 passes/32 keywords/17 properties. Public schema/pass-count
  checks cannot identify compiled availability. Unsupported future combinations
  require proven variant/consumer identity or conservative native fallback.

These current consumer values do not authorize future dynamic activation. Game
source updates require a new census and program review. The shader lane's
separate finding that native LOW fade uses **world Y** conflicts with the older
CheapTerrain path's object-Y threshold; the legacy discrepancy is recorded
separately and was not edited in this audit.

**177 particle/VFX materials contain non-finite saved values.** Their exact
paths and raw hashes are retained, represented as explicit Infinity/NaN metadata
and excluded. The first strict JSON export failed on native Infinity after the
complete source scan; its partial file/log remain preserved. Recovery reparsed
all 2,702 material-bearing sources, checked original hashes/counts and exported
all 9,197 materials. No malformed value was silently clamped or skipped.

## Validation and evidence

The focused checker passes **17 reference/discovery/feature controls** and
**40,053 catalog/source assertions**, including fresh hashes of all 3,286 native
sources. Six actual root/base/DLC/material-family scopes are reparsed with
**48 assertions / 12 causal controls**, checking actual shader references,
saved fields/raw hashes and invalid-index/missing-object refusal. The fixture
controls also reject cross-file path-ID collisions and conflicting CAB copies.

Private evidence is retained in the worker's
`.planning/debug/material-catalog/`: the immutable original census, complete
saved-material JSONL, serialized external tables, program-signature enrichment,
native controls and final hash receipt. The parent must archive it before
removing this worktree. [Tool instructions](../../tools/material-audit/README.md)
reproduce the stages without a shared cache or game mutation.

These checks establish source coverage and identity safety. They do not establish
active scene coverage, dynamic gameplay/clone correctness, headset pixels or FPS.
