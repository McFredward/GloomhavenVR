# Original Campaign shader validation

Worker base: `2fcd0904` on the isolated Quest feature branch. This lane owns only
`tools/quest-shaders/`, `tests/quest-shaders/` and the new
`QuestCampaignShaderValidation.cs` Editor gate. Original game data and the asset
lane's recovered Campaign remain read-only. Generated game shader code, original
textures, DXBC and native bundles stay in private build evidence, not Git.

## Distinct evidence boundaries

The Python identity gate accepts only captured original CAB/path-ID provenance,
exact recovered GUID/path/source hashes, complete declared material bindings,
per-pass original DXBC hashes, explicit keyword/eye modes and original public
bundle addresses. Private materials/meshes use a witnessed prefab child and native
component ordinal plus material slot. Display names never select replacements.

The production Editor gate validates imported material/shader associations and
compiles every declared Android GLES3 bank through Unity **2021.3.5f1**. GLES
returns both native stages through `CompileVariant(ShaderType.Vertex)`; the
Fragment query is empty by contract. Real emitted stages, geometry output,
original color/depth/no-color contract and multiview eye routing are checked.
Compilation receipts explicitly leave original pixel parity and headset pixels
unverified.

The standalone native Windows Mono host loads original Windows bundles through
their captured public addresses, loads candidate Windows bundles independently,
and renders both with the same actual Unity D3D11 renderer/camera/light/input.
It does not instantiate original game prefabs or run original gameplay callbacks.
Native prefab assets are read only to resolve exact private material/mesh
references. Controlled feature probes must change original pixels as well as
match candidate pixels. A blank fixture, wrong/error shader, missing original
address, shader error, unresolved reference, unobserved hypothesis or mismatching
picture stops validation. Error-shader and one-sided texture controls are
required. Headset and Android multiview pictures remain separate evidence.

Native controlled fixtures own their temporary objects and destroy them
immediately. Deferred destruction across a synchronous multi-case loop would
leave older fixture renderers visible and invalidate later images.

## Actual first-program evidence

The asset lane translated the original `Amp_Char_Shader` forward vertex/fragment
DXBC, reconstructed every used original constant-buffer scalar and restored
original texture, sampler and vertex semantic bindings. The first wrapper needed
two real corrections discovered through Unity compilation:

- Native light-probe-volume interfaces require
  `UNITY_LIGHT_PROBE_PROXY_VOLUME` before the Unity include.
- `CGPROGRAM` implicitly includes Unity variables before an in-body define;
  `HLSLPROGRAM`, explicit includes and the original native feature define retain
  the required original interface.

The actual Android bank compiles at private
`/home/claw/quest3-local/full-shader-validation/amp-forward-v4/`.
Its bank SHA-256 is
`4d9d6a44e90ab63b90325be1d8488564f4d52cde427c1c4fd3096e62d0ca0b1f`.
This is one original program pair, not full Campaign shader coverage.

The exact bank executes visible pixels through actual EGL/GLES on the local
Intel Mesa GLES3.2 driver. Six independent input changes are observed in the GPU
readback: geometry, directional lighting, diffuse texture, UV coordinates,
original opacity/dither and original vertex-noise animation. A planted undefined
vertex input is rejected by the real driver compiler. Receipts/raw RGBA pixels
are private `amp-gles-v3/` evidence. Serialized Unity bank output uses explicit
uniform locations with a GLSL300 header; this host probe promotes only that header
to GLSL310 and records the exact adaptation and effective stage hashes. It does
not claim byte-identical Android driver submission or original D3D pixel parity.

The original Brute public-root route is captured from actual PPtrs in
`original-brute-routes.json` (nine typed material/mesh routes). The primary body
uses original CAB `CAB-5292d648e57ecbdc085db6e1e54cadf6`, root path-ID
`-141919125716479919`, children `[0,3]`, native `SkinnedMeshRenderer` ordinal zero
and material slot zero. Its material and mesh identities are
`-4431307056467184847` and `1242016263417856804`. The shader points to original
`CAB-57dce2df1a2ab69aa6943a9cbfd21be8`, path-ID `-8907028974385735907`.

The native D3D11 executable has actually built and started under Wine/Xvfb. Its
first trial correctly rejected a malformed candidate bundle: the tiny trial
project omitted Unity's AssetBundle builtin module. `BuildAssetBundles` returned
a nonnull manifest despite logging that missing module; a built file alone is
therefore insufficient evidence. The trial setup now explicitly enables the
module. Original/candidate D3D pixel comparison remains pending at this
checkpoint; no full Campaign readiness is asserted.

## Root integration API

Stage the new Editor gate with the current Quest project. The exact producer
manifest is `Assets/QuestOriginalCampaign/campaign-shaders.json` (schema 1,
scope `campaign`); `manifest.py` defines the identity contract. Compile only
after the recovered project has been fully staged, not while the asset lane is
still constructing its immutable source checkpoint.

```bash
python3 tools/quest-shaders/run.py compile --project PRIVATE_QUEST_PROJECT \
  --manifest PRIVATE_QUEST_PROJECT/Assets/QuestOriginalCampaign/campaign-shaders.json \
  --output PRIVATE_EVIDENCE/android
python3 tools/quest-shaders/run.py candidates --project PRIVATE_QUEST_PROJECT \
  --manifest PRIVATE_QUEST_PROJECT/Assets/QuestOriginalCampaign/campaign-shaders.json \
  --output PRIVATE_EVIDENCE/candidates
python3 tools/quest-shaders/run.py host --output PRIVATE_EVIDENCE/host
python3 tools/quest-shaders/run.py reference \
  --executable PRIVATE_EVIDENCE/host/host-project/Windows/QuestShaderReference.exe \
  --manifest PRIVATE_QUEST_PROJECT/Assets/QuestOriginalCampaign/campaign-shaders.json \
  --candidate-bundle PRIVATE_EVIDENCE/candidates/quest-campaign-shader-candidates \
  --output PRIVATE_EVIDENCE/reference
```

`--color-space` defaults to Linear; use the actual original contract consistently
for the native host and reference invocation. Linux reference execution uses an
owned Wine prefix and Xvfb when no display exists. Windows executes the same
native player directly. No store, Horizon or network service is used.

Focused tests:

```bash
python3 -m unittest discover -s tests/quest-shaders -v
```

Eight tests pass, including planted identity/coverage/receipt defects and a real
positive/negative GLES driver compilation. Full recovered Campaign shader,
material, pass, variant and native-reference coverage is still required before
the root treats this lane as completed port evidence.
## Production orchestration checkpoint

The recovery helper now extracts and binds all 688 physical Shader objects,
97,224 original stage/tier aliases and 11,099 distinct DXBC programs. The private
native inventory has 9,187 material identities and zero instruction/interface
failures. This establishes source recovery, not complete Android compilation.
The production Amp fixture has exposed additional exact-engine declaration and
sampler adaptations; these remain a compiler gate, never a generic shader fallback.

`tools/quest-shaders/produce.py` exports
`restore_project(project, inventory_path, cache, output, preserved_sources=None)`.
`project` is the private recovered/repaired Unity project, `inventory_path` is
the native `original-shader-inventory.json`, and `cache` is its native binding
cache. `output` is a disjoint private overlay. `preserved_sources` maps original
GUIDs to `{ "sourceSha256": "<exact staged source hash>", "originalProvenance":
{ "recipe": "<the existing source/receipt contract>", ... } }`. Those sources
are copied byte-for-byte from `project`; a changed hash or absent provenance
fails. The original pass/keyword/tier coverage is still retained in the manifest.
Pass aliases and source includes use native object/register identities.

`tools/quest-shaders/bootstrap.py` exports
`prepare(overlay, original_project, output)` for an isolated compiler-only Unity
2021.3.5f1 project. It does not invent rendering fixtures. The Editor methods
`QuestCampaignShaderValidation.Validate(manifestPath, outputPath)` and
`PrepareVariantCollection(manifestPath)` respectively compile every required
Android/GLES bank and retain witnessed build variants in a Resources collection.
The collection is not a startup warmup; compiler receipts explicitly leave
original pixels and headset outcomes false.

`tools/quest-shaders/converters.py` exports
`ensure(cache, tool_archive=None) -> { "vkd3d": Path, "spirv_cross": Path }`.
The Windows builder archive should include the checked open-source-only
`quest-converters-win64-v1.zip`, or pass its location explicitly. The package
contains static Windows executables, their complete official source archives,
licenses and the source build recipe. It contains no proprietary game bytes.
The package SHA-256 is
`61f7d664384b12663fb4fb799ffb8566bf11e99e15ce72c7afb00d5b62199f2d`.
The reproducible developer source recipe is `build_converters.py --cache PATH`;
Windows players do not need MinGW, Visual Studio or a Windows SDK.

The new native `QuestSpriteReferenceOracle` reads an exact public original Sprite
route, follows its native atlas binding with `CanBindTo`, and exports all packed
rectangles, geometry, UVs and GPU texture readback. The BattleOverlayCanvas proof
contains 895 source sprites, a 4096x4096 original packed texture, and nonzero
22-vertex AA_Immune UV data. It does not invoke native game Mono callbacks.
