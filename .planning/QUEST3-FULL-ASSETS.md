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
- `test_native_pointers.py`: 11 checks for exact native field paths, missing-GUID
  parsing, dependency indices, genuine redirect evidence and witnessed packed
  Sprite UV/rectangle/stream rules.
- `py_compile` on the new recovery/builder modules and `git diff --check`.

These checks do not establish correct headset pictures or playable Campaign flow.
