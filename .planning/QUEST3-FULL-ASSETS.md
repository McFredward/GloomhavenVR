# Quest full Campaign asset recovery

This lane supplies all original build scenes and the full original local asset
catalog for the standalone Campaign port. Asset recovery, Android compilation,
procedural baking, multiplayer and hardware behavior remain separate evidence.

## Native source inventory

The current owned input contains 5,227 files / 17,754,574,362 bytes. Its original
compact catalog has 16,626 keys, 14,814 locations and 10,558 asset locations.
Their exact dependency closure contains 3,255 physical UnityFS bundles totaling
12,544,492,732 bytes. Full recovery processes 16 compressed-input batches bounded
at 768 MiB each. The complete original build order contains 13 scenes, including
CampaignMap, NewAdventureMap, Game and their gamepad variants.

The original catalog emits value/field types as additional typed locations on
native containers. Such locations are explicitly reported as serialized values,
not fabricated UnityEngine.Object assets. Sprite atlases and animator controllers
can expose multiple genuine typed subassets. Their distinct native pathIDs and
names remain separate targets, with original GUID/name aliases retained.

## Reproducible object recovery

`tools/quest-recovery/export_identity.py` downloads and verifies pinned upstream
AssetRipper revision `1ac666f47d8e9dedf96afb0b914c70d7656151ea` and instruments two
audited seams locally. The unchanged original game remains read-only. Every
actual export collection records native serialized collection/pathID, exported
GUID/fileID, native class and original container path. Generated bookkeeping and
DLL script identities are handled separately.

`bundle_recovery.py` joins batch exports through actual serialized object
identities. A display filename never identifies an object. New bundle assets
occupy GUID directories; repeated original CAB/pathID objects preserve the prior
canonical export. Each copied file has a hash and each completed batch has an
export-error receipt. A verified checkpoint supports reuse; an interrupted batch
requires a distinct workspace until its evidence has been reviewed.

`bundle_members.py` reads UnityFS directory metadata without decompressing all
texture/mesh payloads. The current source yields exactly 3,255 original CAB to
physical bundle associations. `full_catalog.py` combines that ownership with the
original catalog container path, requested native type and captured pathID. FBX
mesh/avatar targets follow the original prefab's actual native serialized field.

`canonical_guids.py` preserves existing recovery contracts through witnessed
native build scene indices and corresponding original object graph edges in
otherwise byte-identical serialized documents. Resources.Load keys with actual
native types and original Sprite render-data keys supply additional unique roots.
Unexplained body changes, crossed identities, duplicate semantic keys and missing
objects remain unresolved; they are never merged by a similar name. Renaming also
retains the observed old asset path where required by existing source restorers.

The exact B614 source is the locally recovered `startup-source-project`, identified
by the recovery receipt hash recorded in `startup-project-b614-verified`. The older
`validated-project` predates that recovery and has different random export GUIDs.
Neither private cache is a public builder prerequisite: stable source-proven
contracts and a local original-game recovery driver are required for player builds.

## Serialized layout recovery

The original exporter has 16 core MonoBehaviour layout failures: eight
DimmerUIElements, four UIFollowMapLocationInsideArea, two UIQuestMapMarker and two
UILocationMapMarker. `serialized_repairs.py` consumes every original payload byte,
preserves real PPtrs and Unity SerializeReference registries, and reconstructs
their actual fields. The Dimmer container's empty objects genuinely have zero
serialized fields. Unknown classes, registry versions, pointers or trailing bytes
fail explicitly. Actual original binary parsing succeeded for all 16 payloads;
Unity import/runtime behavior requires separate verification.

## Full staging API

`tools/quest-builder/full_assets.py` exposes:

```python
stage(source, game_data, output, tmp_archive,
      canonical_project=..., canonical_startup=...,
      managed_types=..., cab_bundles=..., unitypy=None)
```

The source must have a completed full recovery checkpoint. Staging verifies actual
file hashes and original managed assemblies, creates a fresh private project,
preserves proven canonical identities, restores serialized fields, retains native
scene order and filesystem rule inputs, restores official compatible TMP sources,
and emits `Assets/QuestOriginalCampaign/campaign-addressables.json` and
`script-bindings.json`. The Campaign report deliberately does not assert Android
build, faithful graphics or playable hardware before those checks actually run.

## Native pointer and packed-atlas closure

The complete export initially left 36,764 pointers at AssetRipper's missing-GUID
sentinel. `pointer_recovery.py` now joins every owning YAML local ID to the actual
original collection/pathID, follows the exact original field/array path and uses
the target's actual dependency CAB/pathID. It restores 36,764/36,764 references:
missing GUID count zero, duplicate GUID count zero and unresolved script count
zero. Source names containing literal "Missing Prefab with guid" are retained;
the closure audit counts actual serialized PPtr mappings instead of such names.

The pinned exporter also records genuine builtin redirects and captures native
packed-atlas/core-managed field YAML. No native MonoScript is selected by its
display name: its serialized assembly, namespace and class must match the same
original DLL bytes and unique metadata type. The two otherwise omitted native
packed atlases retain their original packed members, texture bindings and drawing
maps in NativeFormatImporter assets, avoiding an unsupported atlas repack.

`packed_sprites.py` restores all 895 packed members of each atlas. It retains
original positions and indices and reconstructs the UV stream from the original
float32 position, atlas uvTransform and packed texture dimensions. The bundled
895-sprite set was compared with the actual original Unity 2021.3.5 Windows/D3D11
player and then imported by the same-version Unity Editor: all 895 vertices, UVs,
rectangles and pivots agree exactly, with valid 4096x4096 texture bindings. This
establishes native geometry/import parity; Android and headset pictures remain
separate checks.

`native_evidence.py` captures and merges these recipes during normal local core
and bounded-bundle recovery. `native_stage.py` applies the same exact repairs in
the public staging path, emits exhaustive proofs and a typed packed-sprite
manifest, then removes obsolete duplicate overlay caches. Private B614 exports
and precomputed developer pointer overlays are not public builder dependencies.
Instrumented tools use content-addressed generations so future builder changes
do not mutate older witnessed tools. Metadata inspection accepts a separate
`managed_dotnet`/.NET 8 command while the pinned exporter uses .NET 10.

The new source seam was actually compiled and exercised on the original core:
62,879 original managed field recipes and the core packed atlas were captured.
UIInfoTools (`level1`, pathID 11386) passes its actual native owner/script header
check and retains the original AreaEffectSpriteAtlas PPtr `(4, 10304)`. A separate
original GUI/builtin export captured the omitted bundled atlas and 181 genuine
redirects. Its freshly captured recipe drove an independent 896-object staging
smoke test, which restored the missing atlas reference and all 895 sprites without
the precomputed overlay. The production Editor packed-sprite validator passed
the same 895 imported sprites.

An additional source audit now discovers native YAML by its actual document
header and includes importer metadata. Actual YAML PPtr nodes determine GUID
closure, so quoted names and literal text blocks containing pointer-like text
never count as references. This found five trap Timeline assets skipped by the
historical suffix list during canonical remapping. Their six dangling animation
references have exact targets in the retained native GUID witness map.
`full_assets.repair_reused_stage(generated_project)` repairs those tokens and
derives the packed-sprite manifest from that copy's own retained receipts, allowing
an older immutable verified source stage to be used without private overlays.
Fresh recovery/remapping also covers all actual serialized native asset types.

## Exact compiled shader recovery

Original custom shaders are predominantly stripped D3D11 programs. A generic
Standard/unlit fallback does not preserve their material behavior. The new
`full_shaders.py` recovers the original per-program appended cbuffer/resource
interface and merges partial common parameters. It translates actual DXBC through
open-source vkd3d-shader into SPIR-V and SPIRV-Cross HLSL, then restores original
vertex semantics, uniforms, matrix column packing and texture/sampler bindings.

Every scalar actually read by the translated program must have an original
metadata source. Only unread padding receives zero. Unknown parameter/structure
layouts, used missing fields or lost resource registers block conversion. vkd3d's
unified descriptor binding numbers are distinct from original D3D registers;
the original numeric resource identity is retained by the translated debug names.
Native Unity builtin textures and sampler naming follow Unity's include contracts.

The actual Amp_Char_Shader forward vertex and fragment both translate and bind
their used original scalars. These first candidates remain unverified graphics
until the independent Unity/GLES3/pixel comparison harness succeeds. Production
wrappers must use HLSLPROGRAM and set native feature defines before Unity includes;
CGPROGRAM implicitly includes ShaderVariables too early for recovered probe-volume
interfaces. Full passes, keywords, render states and native texture encodings must
retain original behavior rather than declare a translated fragment alone complete.

## Focused checks

- `test_full_identity.py`: 10 checks for actual native identity joins and strict
  original serialized layouts.
- `test_campaign_contracts.py`: 8 checks for scene/graph GUID witnesses, canonical
  path/pointer preservation, exact used-uniform coverage, original resource
  register recovery, matrix/integer packing and original input semantics.
- `test_native_pointers.py`: 13 checks for exact native field paths, missing-GUID
  parsing, dependency indices, genuine redirect evidence and witnessed packed
  Sprite UV/rectangle/stream rules, arbitrary native suffixes, importer metadata
  and literal scalar text.
- `py_compile` on the new recovery/builder modules and `git diff --check`.

These checks do not establish correct headset pictures or playable Campaign flow.

## Bundled native audio recovery

The six CAB-owned Cragheart AudioClips are Vorbis FSB5 banks, separate from the
1,465 core-resource clips. Four native banks encode four channels in FSB extension
kind 1, while the pinned export decoder previously read only the base one-channel
flag. `full_audio.stage(project, game_data, dotnet=..., tool_cache=...,
cab_bundles=...)` reads exact original CAB/pathID/resource offsets and reconstructs
the Vorbis headers using the original extended channel count. All original
compressed audio packets remain byte-identical; stereo and mono outputs also
remain byte-identical. GUIDs, local object IDs and importer files are retained.

The pure managed private decoder pins Fmod5Sharp 3.1.0 and
AssetRipper.TextureDecoder 2.6.3, both MIT, with the complete NuGet content hashes in
`packages.lock.json`. Builds use the player's selected .NET 8 SDK with
RestoreLockedMode; they require neither a system codec nor a C++ toolchain. Its
private cache receipt hashes the source, lock file and executable outputs.

Actual source recovery on all six clips retained 4/2/4/1/4/4 channels and original
sample rates/counts. `ffprobe` independently confirmed those output headers.
Unity 2021.3.5f1 imported all six files successfully and the production
`QuestCampaignAssetValidation.ValidateBundledAudio` gate checked exact GUID/local
ID, channels, frequency and sample counts against the original bank receipts.
Private evidence is retained in `full-audio-smoke-v1` and
`atlas-import-v1/portable-audio-import.json`. This establishes native source and
Editor import fidelity; it does not establish headset playback.

## Native Cubemap portability and mip fidelity

The complete source contains 31 native Cubemaps: 16 ARGB32, 13 DXT1 and two BC6H
HDR maps. There are no native Texture3D/Texture2DArray/CubemapArray objects. PNG
exports preserve only the base 8-bit image, losing HDR and the original reflection
roughness mip chain. `full_textures.stage(project, game_data, dotnet=...,
tool_cache=..., cab_bundles=...)` reads each exact original native identity and
retains all six faces and all original mip levels in a native Cubemap asset.

ARGB32 data remains byte-identical. The pinned managed decoder expands DXT1 into
RGBA32 and unsigned BC6H into RGBAHalf; it consumes the complete original image
payload and neither regenerates mip levels nor clamps HDR to 8-bit. The original
filter, wrap, color-space and readability settings remain intact. GUID/local ID
stay unchanged; the returned `pathMap` and retained identity/catalog manifests
map the former physical PNG path to its native `.asset` path. Source files remain
read-only. `Assets/QuestOriginalCampaign/native-cubemaps.json` witnesses each
face/mip's byte count and hash plus original container/image provenance.

All 31 actual original maps have been recovered. Each HDR source includes values
up to 36.6875, with 742,329 component values above 1; they are not clamped.
Actual Unity 2021.3.5f1 imported every cube with its original mip count, native
GUID/local ID and portable format. The production `QuestCampaignTextureValidation`
gate verified 1,272 imported face/mip streams. For the two originally nonreadable
HDR maps, 132 exact half-float face/mip streams were independently read back from
the Editor's OpenGL GPU and matched the recovered source bytes. Readability was
not enabled to obtain that proof. Retained receipts are in
`full-textures-smoke-v1` and `atlas-import-v1/portable-texture-import.json`.

The actual original BC6H GPU readback comparison and Quest-device format/picture
validation remain distinct, unverified hardware evidence. The current import gate
states both limitations explicitly rather than treating decoder or Editor success
as original GPU parity.

## Complete ordinary Sprite drawing recovery

The original identity inventory contains 5,159 Sprite objects in 711 owning
containers. The two native packed atlases already cover 1,790 objects. The other
3,369 objects are not atlas-backed. The exporter can replace their authored
rectangle/pivot/offset with the cropped drawing bounds, including the CAB-owned
LoadingBase spinner layer; matching names to core resource sprites is unsafe.

`full_sprites.stage(project, game_data, cab_bundles=...)` reads every remaining
object by exact original CAB/pathID, restores its authored rectangle, offset,
border, pivot and pixels-per-unit, and restores its original render crop/trim
fields. Vertex positions and indices remain the original native geometry; UVs
are reconstructed with the native float32 position/uvTransform/texture-size rule.
Texture pointers resolve through captured native identities. The verified 1,790
packed members are hash-checked and left untouched. Only a generated project
changes. Loading hundreds of source bundles simultaneously is avoided by retaining
only the current owner/dependency closure.

The actual complete source run restored all 3,369 remaining sprites with zero
unresolved native texture identities and preserved all 1,790 packed hashes.
`Assets/QuestOriginalCampaign/native-sprites.json` carries each original object and
container hash plus recovered drawing streams. The production
`QuestCampaignSpriteValidation` gate checks imported GUID/local ID, rectangle,
pivot, border, texture identity/size and vertices/UV. It does not call
`Sprite.textureRect` for arbitrary tight-packed geometry. Its working cache is
bounded; the complete typed Campaign asset gate also releases its validated
native-object cache every 128 associated locations.

The separate root spinner validator was actually run in Unity 2021.3.5f1 with all
four core spinner variants, both promotions and both native atlases. Its
textureRect/padding checks passed for these actual assets. The broader ordinary Sprite Editor fixture subsequently passed every one of the
3,369 imported objects after the actual generated case migration retained all
24 colliding path variants. All native GUID/local IDs, drawing fields, texture
bindings, vertices and UVs passed the production gate. Source and Editor proof
remain separate from unverified headset pictures.


## Remaining native image containers

The source-native audit additionally covers three Alpha8 font textures and four
RenderTexture objects. The font data is uncompressed Alpha8 with one mip and no
platform blob. RenderTextures contain runtime target descriptors, not serialized
Windows texels: color GraphicsFormat 8, depth/stencil formats 90 or 92, 2D shape,
MSAA1 and the original compatible-format fallback enabled. Unknown formats or
native Texture3D/Texture2DArray/CubemapArray classes fail recovery explicitly.

`full_textures.stage` now writes `native-platform-images.json` automatically. The
production texture import gate actually imported all seven original GUID/local
IDs, checked the three native Alpha8 dimensions/formats and successfully created
the four original runtime render-target backings on Editor OpenGL. The combined
receipt still distinguishes those Editor checks from unverified Android/Quest
format support and original BC6H GPU parity. The actual final fixture receipts
are retained under `atlas-import-v1/portable-texture-import.json` (31 cubes,
1,272 face/mips, 132 half GPU readbacks, seven other native images) and
`atlas-import-v1/portable-sprite-import.json` (all 3,369 ordinary sprites).

## Ordinary Texture2D metadata and floating image recovery

`full_texture2d.stage(project, game_data, dotnet=..., tool_cache=...,
cab_bundles=...)` audits every image-exported native Texture2D by original
CAB/pathID, then replaces floating/HDR PNG exports in the generated copy.
Run it after `full_textures.stage`. It writes
`ordinary-texture2d-audit.json` and `native-texture2d.json` under
`Assets/QuestOriginalCampaign`; the latter's `assets[*].assetPath` fields are
physical paths which the later case-normalization stage must migrate.
Captured native identity paths and catalog physical paths change through the
field-specific remapper; original keys, aliases and source paths stay intact.

The actual complete source audit found 14,862 image-exported native Texture2D
objects, including 2,460 native normal maps. Dimensions, maximum import sizes,
sRGB/linear flags, mip enablement, sampler states, readability and streaming
metadata had zero mismatches. All native chains were either one mip or complete,
with zero stripped mips. Sprite importer type 8 is a legitimate nonnormal
classification. Seventeen embedded/native font textures are separate from the
ordinary PNG scope. These results do not establish pixel parity for ordinary
regenerated PNG mip chains or hardware pictures.

Eight blood animation textures are native RGBAHalf, exported lossily as PNG.
Their actual values encode normalized data in 0..1; their original 16-bit
half precision must survive rather than becoming 8-bit normalized components.
They retain their original non-power-of-two height, point sampling, one mip,
linear color space and exact original half byte stream. Twenty-five HDR sky
textures are native rectangular BC6H images: a pinned pure managed decoder
retains every original mip as RGBAHalf, including HDR range. Native class28
serialization retains the original GUID/local ID, dimensions, sampler, color,
streaming and readability flags without a PNG import/rescale step. Core
streamed texels are read from their exact owned .resS offset/size; CAB data
resolves inside its original bundle.

The production `QuestCampaignTextureValidation.ValidateTexture2D` gate actually
imported all 33 replacements in Unity 2021.3.5f1/OpenGLCore and checked every
GUID/local ID and original sampler/color contract. Exact half GPU readbacks
matched all 294 original/decompressed mip witnesses, totaling 431,560,088 pixel
bytes. Original RGBAHalf VAT bytes are unchanged. The source audit and receipt
remain separate from original Windows BC6H GPU decoder parity and Quest GPU
picture evidence, which are not yet verified. Private reproducible proof is
`ordinary-texture2d-audit-v2.json`, `full-texture2d-smoke-v1` and
`atlas-import-v1/portable-texture2d-import.json`.

## Consuming material references after native texture conversion

An actual Unity seam probe established that retaining a PNG PPtr's type 3 when
its target becomes a native `.texture2D`/Cubemap asset leaves the material's
texture unresolved, despite the same GUID/local ID importing successfully on its
own. `full_textures.restore_native_texture_pointer_types(project)` therefore runs
automatically after each native texture conversion stage. It recognizes real
YAML mapping nodes using the native pointer tokenizer, then changes only type 3
to type 2 for the exact converted texture GUID/local ID. Names and scalar strings
that look like references are untouched. Original native-object/source-container
hashes remain immutable; changed generated owner hashes and any affected current
native asset receipt hashes are refreshed explicitly.

`native-texture-references.json` records `targets[*].assetPath`,
`owners[*].assetPath`, current/before SHA hashes, reference counts and exact
GUID/local ID/type tuples. Both assetPath lists are physical paths and must pass
through later case normalization. The production
`QuestCampaignTextureValidation.ValidateTextureReferences` gate examines actual
imported material object references, independently of shader property visibility.
The full original scope has 64 converted texture targets and 69 consuming PPtrs
in 65 original material assets; all 69 required the importer-type correction.
Actual Unity 2021.3.5f1 import verified every corrected consuming reference and
its exact target GUID/local ID/path. That evidence is retained as
`atlas-import-v1/native-texture-reference-import.json` and
`full-texture-ref-smoke-v1/Assets/QuestOriginalCampaign/native-texture-references.json`.
It verifies consuming links, not their eventual headset rendering.
