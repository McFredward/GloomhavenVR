# Android native default-sky resources

This work is isolated resource preparation for Quest Build741. The actual feature
rebase waits for the maintainer's final dev-ready signal. No complete APK, source
archive, whole Campaign import or whole-game shader conversion is produced here.

## Contract and ownership

`tools/quest-builder/sky_resources.py` owns the resource pair. The integrator owns
`builder.py` and the MSBuild properties; the update lane owns source selection,
compiler snapshots and bounded cache retention. The desktop exporter and runtime
sky presentation remain upstream inputs.

The resource names are `GloomhavenVR.DefaultSky.bundle` and
`GloomhavenVR.DefaultSky.provenance.json`. Output filenames are
`native-default-sky-android.bundle` and
`native-default-sky-android.provenance.json`. Both are embedded before every full
or compact managed weave. The runtime keys are exactly:

| Key | Type | Original name |
| --- | --- | --- |
| `native/default-sky/material` | Material | `BG_SkyClouds 2` |
| `native/default-sky/mesh` | Mesh | `GH_SkySphere` |
| `native/default-sky/shader` | Shader | `AMP_SkyShader` |

The closed bank also contains `EN_SkyTest_01`, `PerlinNoiseGray2` and the original
`Legacy Shaders/Diffuse` → `Legacy Shaders/VertexLit` fallback graph: seven render
assets plus one AssetBundle container, no external file pointers.

`plan(source, source_files=None)` returns `recipeKey`, `recipe` and `sources`.
Immutable source rows work without restoring the complete source tree. The
recipe fixes schema1, scope `quest-default-sky-resources-v1`, Android, Vulkan,
Unity2021.3.5f1, MultiPass and enabled type trees. Its `sources` conservatively
close the native restoration tools, converter locks/package, requirements,
producer, Editor, upstream provenance/export recipe and public loading template.
No native desktop bank becomes a game-free source input.

`produce(builder, source, output, *, project=None, game=None, editor=None,
base_apk=None, source_package=None, native_cache=None, source_files=None)` returns
`bundle`, `provenance`, `recipeKey`, `producerKey`, `reused`, `basis` and
`cacheRelativePath`. `project` and `source_package` retain the orchestration API;
existing player assets are inside `base_apk`, so no new external content reader
or complete project import is needed. The root assigns
`args._quest_sky_resource = result['cacheRelativePath']`.

The cache is `cache/default-sky-resources/<recipeKey>/<producerKey>/`.
`recipeKey = value_hash(recipe)` and
`producerKey = value_hash({'recipeKey': recipeKey, 'basis': basis})`.
The provenance is the finite ownership marker and binds both directory keys,
the complete recipe, selected native asset identities, actual bank bytes and
parsed Android/Vulkan proof. APK code/build timestamps are outside the basis.

## Installed-only transition and reuse

An installed matching resource pair is qualified before cache reuse. A valid
matching cache is returned without rewriting its bank or provenance. A caller
without an APK can reuse one unambiguous closed private basis without PC inputs
or an Editor.

The first installed-only transition can recover assets already present in the
Android player. It reads BuildSettings, locates the original ProcGen scene, finds
its exact `GH_SkySphere`, then follows only its MeshFilter, MeshRenderer, material
textures, AMP shader and two fallback shaders. There is no scan of Campaign
artwork or dependency on a literal scene index. Unity split files are seekable;
only chunks covering metadata and selected object ranges are inflated.

The initial actual evidence uses the local **Build625** APK. This is an explicit
architecture/native-data proof, not a Build735 execution claim. That player has
ProcGen at level9; its selected objects need nine of29 shared-asset chunks,
9,044,768 bytes, plus the small manager/scene, 191,452-byte AMP file and
904,724-byte builtin fallback file. All40 material floats/nine colors and the
73,876 vertex bytes, indices, channels, submeshes and bounds match the pinned PC
originals. The two existing Android ETC mip chains are copied byte-for-byte,
including all13/10 levels and sampling/color settings. This retains the current
Quest texture representation; it does not claim that prior ETC compression is
identical to original desktop BC1 pixels.

Repacking uses only the authored17-KB QuestLoading bank as a container template.
The source geometry, material, textures and shader blobs never come from that
template. Replacement ObjectReaders own their newly serialized bytes and the
container keys are sorted lexically. A parsed roundtrip compares every native
geometry/material/mip/program proof with the selected source.

After a real selected APK resource/asset basis or PC basis has been qualified,
a corrupt/incomplete pair in its exact ordinary owned cache directory is
repaired atomically. A bounded reason and before hashes are retained in
`output/logs/default-sky-repair-<recipe12>-<producer12>.json`; the embedded pair
contains no repair entropy. An existing selected APK pair is restored byte-for-byte,
including its provenance. Unrecognized children are preserved. Unknown source identities,
closures, graphics platforms, symlinks and directories at file positions are
errors. Direct `inspect_bank` and `_validate` remain fail-closed.

## First PC generation

Only pinned `sharedassets8.assets`, its `.resS`, and
`Resources/unity_builtin_extra` are read. Exactly three original shaders are
restored through the existing DXBC/interface machinery and compiled in a private
tiny Android project. The prepared complete project is never imported. Native
cache seeding copies only the exact selected program/interface artifacts into
the private cache; shared caches remain read-only. Every original compiler alias
must survive the actual Vulkan shader closure check.

PC work lives at `output/work/sky-<producer12>/`. Its `owner.json` contains exactly
schema1, scope `quest-default-sky-work-v1`, full recipeKey and full producerKey.
Only qualified converter bindings in `native-cache` are reusable. Every Editor
invocation gets a fresh `unity-<random8>` project and only the finite manifest's
shader/include/meta files are imported. Original two-level resource-cache paths
are never used as long Win32 shader-work paths. The returned and embedded
`workRelativePath` is deterministic; random attempt paths, host paths, raw work
manifest hashes and repair history stay in private external logs. Work cleanup
requires the matching closed final resource pair and preserves active/incomplete
or unknown-owner workspaces.

The Editor and launcher logs use
`output/logs/default-sky-unity-<recipe12>-<producer12>.log` and
`default-sky-launch-<recipe12>-<producer12>.log`. The existing `builder.command`
publishes and finishes the source-bound Unity work plan exactly once. The Wizard
maps this child to its real `weave`/`update-code` owner and observes native shader
jobs; the outer bank counter completes only after native repack validation.

Mesh and material values retain their original representation. The original BC1
texture levels are decoded individually to RGBA32, preserving native row order,
color encoding, all authored mip levels and filtering/wrap settings. No mip chain
is regenerated and no second lossy texture compression is introduced. Shader
backend compilation is the only Unity import. The final seven-asset bank is
assembled privately around those compiled shaders and original assets.

## Validation status

Focused Python tests cover immutable source closure, conservative transitive
inputs, source-pin drift, bounded split reads, namespace/header bounds, authored
mip decoding, retained resource/cache reuse, targeted repair and unknown-source
refusal. The actual Build625 scope also exercises all three Vulkan shader banks
and ten causal corruptions of version/platform, fallback, material, texture, mesh and
runtime root contracts. Logs, input identities and completed-run cleanup receipts
live under `quest-dev740-rebase-20261028/sky` in the external evidence tree.
The current source checkpoint passes19 tests in24.862 seconds. The actual native
controls include four corrupt/incomplete-cache repairs, byte-identical selected
resource-pair restoration and refusal of unknown paths/owners. A private strict
Release build of the Editor source against the real Unity2021 references passes
with zero warnings/errors in1.39 seconds, without starting Unity.

Actual `AssetBundle.LoadAsset<Mesh/Material/Shader>` readiness, AMP support and
the tiny PC three-shader compilation remain required before completion. They are
currently deferred while the upstream complete dev checkpoint occupies Unity;
no new Unity process is launched without coordination. Serialized platform proof
or a software Vulkan consumer cannot establish headset pixels, real device
performance or hardware acceptance.

The existing menu-depth producer is unchanged and still plans to
`ba75e6e9a5b5ea37c4a3f4b0b48c1f324298875e4aaf67dcdfa8aeccd2116318`.
