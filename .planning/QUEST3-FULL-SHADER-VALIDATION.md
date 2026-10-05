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
module. The controlled DIRECTIONAL forward-bank comparison subsequently passed:
eight original/candidate D3D11 pictures are byte-identical (geometry, lighting,
diffuse texture, UVs, opacity, vertex-noise animation and camera eye offset,
including baseline), with error-shader and one-sided texture negative controls.
That evidence is private `amp-d3d-v5/windows-pixels.json`; it does not establish
bone-animation, other native keyword banks or headset picture parity.

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

Nineteen tests pass, including planted identity/coverage/receipt defects and a real
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
`restore_project(project, inventory_path, cache, output, preserved_sources=None, graphics_api="Vulkan")`.
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

The complete source overlay now emits all 688 original shader identities without
an instruction/interface recovery failure. Its private manifest retains original
per-stage keyword/hardware-tier banks independently from rendering fixtures.
Original exact-one light and shadow keyword choices are reconstructed from their
actual native occurrence graph; the builder does not invent an all-disabled
ForwardAdd or ShadowCaster bank. Four serialized fallback pass shells in
PostProcessing Uber/FinalPass contain zero original programs. Their original
state and pass ordinal are preserved, and no source math is attributed to them.

Native instancing buffers are flexible GPU arrays despite Unity's two-element
compiler minimum. Recovered reads address the original runtime array element
directly; they never copy only elements zero and one. Native structured-resource
strides come from original DXBC declarations, including 4-byte histogram and
16-byte waveform words. Missing stride/register identity, unexplained padding,
ambiguous sampler ownership and unsupported resource reads fail conversion.

An actual Android instanced Amp bank exposed Unity HLSLcc's restricted reflection
pattern for arrays of structures. Its front-end requires the native index
definition to remain IMUL/ISHL. The emitter disables only that GLES front-end
reoptimization for passes with observed original instance structures; native
GLES driver optimization remains enabled. The exact original array data and math
are retained, and the previously failing actual bank now compiles. Complete
Campaign bank compilation and hardware performance remain outstanding gates.

Both Windows converter executables have actually executed under Wine against
26 original DXBC samples, including native graphics and structured-buffer cases.
All SPIR-V bytes and normalized HLSL text match the pinned Linux converter
output. `converter-tools/win-parity-v1/converter-byte-parity.json` records that
bounded parity check; it is not an all-program or headset parity claim.

The original D3D stage signatures can pack several semantics into one register.
The emitter splits/reassembles those native semantics and preserves their
component types; vertex and fragment register numbers need not match. A fresh
graphics-enabled Unity compiler project passed 624 complete bank aliases across
Amp characters, Zephyr, map hatch, low GUI ghost and TMP distance-field families.
The complete source inventory is 688 shaders, 9,187 materials, 97,224 native
stage/tier aliases and 11,099 distinct original DXBC streams. The emitted compiler
manifest includes 103,128 complete native/Quest stereo aliases. Full compilation
remains in progress; those totals are source coverage, not a completed gate.

An actual GLES driver test then found the native point-shadow comparison bank
emitted an invalid explicit-LOD cube-shadow call. The platform adapter follows
the exact original Unity2021.3.5 `HLSLSupport.cginc` convention:
`UNITY_SAMPLE_TEXCUBE_SHADOW` uses `SampleCmp` on GLES/GL/Vulkan/Switch and
`SampleCmpLevelZero` elsewhere. Only witnessed `TextureCube` comparison calls
receive this change; original sampler and texture bindings, depth arguments and
2D shadow instructions remain intact. The manifest records each such adapter.
This is native engine platform behavior, not replacement shadow math. The
hardware picture and actual multiview driver remain separate outstanding checks.

The actual driver also exposed a flat/smooth mismatch in instanced deferred SH.
Its original fragment signature retained unused SV_Position/TEX6 declarations
that SPIRV-Cross omitted. Restoring every native declaration in exact original
signature order preserves Unity's native stage packing/interpolation metadata;
SV-prefixed user varyings are identified by original systemValue, not their name.
No input read or shader math is invented. The five-family gate now passes all
624 actual compiler aliases and all 312 mono aliases link on the real local GLES
driver (92 unique emitted banks), including the two previously failing banks.
An undefined-instruction copy of a real bank is rejected as a negative control.
Actual multiview is unavailable on that EGL device and is not claimed.

Native mixed input registers can contain an ordinary SV_InstanceID user varying
and the real front-face system value. SPIRV-Cross emits an anonymous field for
the user component; its unchanged TEXCOORD location witnesses the original
register. The restorer now uses that exact location rather than requiring a
numbered variable, and rejects anonymous fields without native identity.

DXBC MOV instructions carry raw bits, even when SPIRV-Cross represents their
temporary registers as floats. Subsequent HLSL compilation can flush subnormal
integer payloads or canonicalize NaNs. `integer_bits.restore` keeps parallel
uint carriers for the actual static and local float registers whose bits are
extracted, including raw MOV dependencies and exact float-literal bits. It
captures before each original write, so loop/self-assignment ordering and the
original floating consumers remain intact. Native arithmetic is retained.

The only two unique graphics programs using typed Texture.Load contain four
actual mip-zero Texture2D reads. `load_bounds.restore` validates that native mip
and dimension, evaluates the coordinates once, and returns zero outside the
actual texture extent, matching D3D instead of relying on undefined GLES fetch
results. Unknown dimensions, mips and overloads fail; no coordinate is clamped.

The corrected complete overlay emits all 688 shaders. A fresh five-family gate
with these integer carriers passed all 624 Android compiler aliases and its
312 mono aliases again linked on the actual GLES driver (92 distinct banks).
The full 1,432-pass census and all 103,128 compiler aliases remain independent
outstanding checks; these counts do not establish a headset result or all-pixel
parity with the PC game.

The all-family census exposed a fixed/half builtin conversion that the carrier
must retain: copying Unity's `_LightColor0` into an original float register
implicitly converts its selected lanes to float. The parallel uint capture now
applies that same assignment-width conversion before extracting bits. It does
not reinterpret a different builtin storage type. The emitted manifest records
the source hashes of the producer, native binding helper, integer carrier and
typed-load adapters; generated source is regenerated on every prepare operation.

With the integer carrier enabled, a freshly rebuilt Windows candidate still
matches the original native Amp DXBC byte-for-byte in all eight D3D11 pictures
(baseline, geometry, lighting, diffuse, UV, opacity, vertex animation and camera
eye). Both executable negative controls are rejected. This remains the bounded
original forward-pass fixture; native bones, every material/program, Android
pixels and headset performance are not inferred from it.

### Full game Vulkan platform contract

The full Campaign target uses Vulkan. Original Windows DXBC already contains
Unity's reversed-Z shadow-bias, fog/depth and top-origin texture branches.
Unity 2021.3.5's exact `HLSLSupport.cginc` enables `UNITY_REVERSED_Z` and
`UNITY_UV_STARTS_AT_TOP` for both D3D11 and Vulkan; GLES has different near-clip
and depth conventions. Compiling those baked Windows branches unchanged for
GLES is insufficient. The full target keeps the original math and uses its
matching Vulkan conventions. The separate startup/probe retains GLES.

`QuestVulkanShaderValidation.Compile(shader, subshader, pass, keywords, tier)`
accepts `int` or `GraphicsTier` and returns the real combined bank, decoded
`vertex` and `fragment` SPIR-V bytes and SHA-256 values, image descriptors and
stage interfaces. Unity's Vulkan Vertex query contains both stages; an empty
Fragment query is expected. The six-entry Unity stage table is bounded and its
SMOL-V payloads are decoded with the pinned official modern format, classified
by their actual `OpEntryPoint` rather than table order. Resource debug names
are stripped, so texture property identities join actual
`CompileVariant.TextureBindings` packed descriptor coordinates to decoded
`OpTypeImage` dimensions. No name guessing supplies missing properties.
`RequirePosition`, `RequireColorOutput`, and `RequirePlain2D` enforce the narrow
original contracts used by the retained UI/video/world gates.

Actual original Amp trial: 23,916-byte vertex and 98,116-byte fragment SPIR-V
modules compiled for Android Vulkan. The managed SMOL-V decoder produced
byte-identical modules to independently built official smol-v C++ at commit
`55000efe742f56d8b51223b9ea7775a8f0501881`. All ten used original sampled
resources matched their actual cooked descriptor/property/dimension bindings.
The separate eight-source legacy fixture also passed 27 Vulkan banks/54 stages.
These are compiler/format evidence, not headset image validation or a full
Campaign native driver census.

The full-only producer defaults to `graphics_api="Vulkan"`; explicit `GLES3`
is available for the separate bounded historical probe. The compiler manifest
records `graphicsApi` and `compilerPlatform`. The Campaign Editor gate emits
actual decoded stage SHA-256 values and property/type reflections, validates
original typed target signatures and linked varying locations, and retains
content-addressed evidence bytes to avoid duplicating native stage payloads
for tens of thousands of aliases. Native Windows `_DepthSlice` layer outputs
recover their witnessed `SV_RenderTargetArrayIndex` on Vulkan rather than the
GLES interface relocation. The original layer calculation remains unchanged.

Full Vulkan retention/compiler coverage is the exact original alias graph;
`coverageKind="original-native"` and `requiredOriginalNativeAliasCount` record
its provenance. Extra future multiview aliases remain source options but do
not enter the shipping MultiPass acceptance gate. The historical GLES probe
may separately retain `coverageKind="quest-synthetic"` aliases. This filtering
changes the evidence/retention scope only; reconstructed ShaderLab/HLSL bytes
and every original native keyword/tier/pass remain unchanged.

Actual Vulkan all-family milestone: all 688 source identities, 9,187 material
shader bindings and 1,432 original pass representatives passed the real Unity
Android Vulkan gate. Independent SPIR-V validation and lavapipe driver creation
passed all 1,432 aliases/544 distinct graphics pipelines, with the missing
fragment entry-point negative control rejected. A rebuilt native Linux Vulkan
Amp candidate matched the existing original Windows/D3D11 readbacks byte for
byte in all eight baseline/geometry/light/diffuse/UV/opacity/vertex-animation/
camera-position cases; both planted texture/error-shader controls rejected.
The CPU Vulkan device was selected explicitly (`-force-device-index 0`) and
reported by Unity; no device spoofing was used. This bounded family fixture
does not prove original bones, full Campaign materials, Android stereo or a
Quest display. A native shadow/depth witness and full original alias sweep
remain independent follow-up gates.

The authoritative native Vulkan sweep also caught a signed interface mismatch in
`Hidden/PostProcessing/FinalPass` with `FXAA`, `FXAA_KEEP_ALPHA` and
`STEREO_INSTANCING_ENABLED`. Its original vertex DXBC
`e05376780aeac23832d154e456d37dc27878a5654ec78c11c84e2bc1b8443bfc`
explicitly declares `SV_RenderTargetArrayIndex` as scalar uint in OSGN.
SPIRV-Cross exposes its internal Vulkan Layer carrier as int. The wrapper now
requires that exact original signature, retains the signed internal carrier and
all of its instructions, and writes its bits through `asuint` to the original
uint interface. A real Unity Android Vulkan retest compiled all 27 affected
FinalPass banks, including the previously rejected alias. The focused test rejects
an unproven signed original signature and verifies the unchanged internal math.
Evidence: private `full-production-vulkan-shard-2/QuestPostLayerEvidence` and
`unity-post-layer.log`. This does not assert a headset picture.

The actual full-builder preservation fixture confirmed Unity's first-import
`UnityObjectToClipPos` upgrade changes exactly the three legacy Bloom sources.
The Campaign source gate now recognizes only their pinned original GUID/name/
pathID, original source hash, exact upgraded hash, canonical native recipe,
replacement count, retained-source role and unchanged recovery receipt. Other
source changes remain rejected. Both Python source validation and the actual
Editor gate enforce this contract. The real six-source fixture passed all72
native Vulkan banks/144stages and five original material consumers. Twelve real
negative controls rejected arbitrary source hashes, reassigned GUIDs, wrong
source roles and missing receipts. Evidence lives in the asset worker's private
`preserved-ui-vulkan-v1/unity-vulkan-corrected.log` and `ProbeOutput/results.json`.

The native alias sweep caught an FXC internal abort in the instanced
`Amp_Low/Amp_CharShader_Low` fragment. The original fragment DXBC is
`2732a0dd6ccc6376f06d46a97d5635234edd512f36b3767105b74aabdbd8ef8c`,
interface `1f8658c98da87ec209557e2dfc9bea08333d31ef0525b824309b10150b6442be`.
The failing read is the original `_InvisibilityControl` field at byte offset92
in `UnityInstancing_Amp_CharShader_Low`, inside an `isnan`/min/max statement.
Capturing that exact readonly floating field into a local before the predicate
makes the same original Vulkan alias compile. The CB index expression, its
integer overflow/division, field identity, original float consumers and NaN
rules remain unchanged. Constant-index and instrumentation-removal experiments
were diagnostic only and were discarded.

`instance_nan.py` applies this transformation only to actual original instance
fields inside real NaN operands. It rejects unaudited field types and repeated
loop/header evaluations. Each adapted source records its exact original buffer,
structure, field, byte offset and index expression. The generator hashes the
helper with the other source-producing modules, preventing stale output reuse.
An actual complete producer execution emitted all688physical shaders,
11,656bound includes and exactly51,564original Vulkan aliases;120typed native
read sites qualified. The formerly failing alias passed the real Unity Vulkan
gate. Its complete Campaign/shard census and affected-material pixel evidence
remain separate ongoing checks.
