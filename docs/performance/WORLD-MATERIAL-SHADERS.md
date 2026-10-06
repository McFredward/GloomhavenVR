# Native world material shader stages

`GloomhavenVR/WorldSimpleMaterial` retains the native 3D meshes and implements two
optional material compromises. `_GHVRWorldMaterialMode = 1` uses spherical-harmonic
ambient light and one diffuse main light; `2` uses original albedo/tint with
optional original scene SH ambient evaluated at vertices. Both retain original fog and supported native visibility/cutout math.
Mode 0 restores original materials in the separate runtime owner. PC and Frame
use the same implementation with different configurable profile defaults.

Stage 1 deliberately omits native PBR, normal/MRAO lighting channels, specular,
reflections, received-shadow lighting, additional-light passes, detail and
parallax. Stage 2 removes per-pixel ambient/main-light computation and all main
light work. `_GHVRWorldAmbientWeight` defaults to 1 and retains the original scene
SH ambient at vertices; 0 explicitly restores raw unlit albedo. Intermediate
weights interpolate those two appearances without changing texture, hue, mesh
or visibility. The setting does not affect stage 1. This is an explicit lighting compromise, not a
claim of original lighting parity. The shader does not move vertices, disable
objects, write controllers, change lights or sample idle animations.

## Source provenance and admission

`tests/world-material-shader/native-contract.json` pins each shader object by
source path, serialized asset and path ID, source/raw-object hashes and directly
addressed Windows DXBC programs. Each program records its segment, byte offset,
length, DXBC hash, binding-tail hash and decoded constant-buffer member offsets.
`extract-native.py` verifies addressed segment bounds; it does not merge shader
identities by name or assume that all program payloads occupy segment zero.
The full extraction has 16 objects and 268 bounded programs, including excluded
Prop families retained for exclusion review. Original game binaries, decoded
payloads and assembly are private debug evidence, never tracked distribution.

Primary research decoder: [DXDecompiler](https://github.com/spacehamster/DXDecompiler),
commit `e7c32662bbf5909e0a80f12e9d07f510bfd0e220`. Its private legacy research build
needed nullable analysis disabled and warnings nonfatal; this does not relax the
production or fixture warning policy. Native float32 simplex vectors bind the
previous independently decoded HIGH WallFade assembly SHA-256
`893ad75c5d2d02acac71fa0c03cca8fea930a50e303b40de7689985403954326`.

The independent catalog audit covers the whole installed game, JoTL and Solo
content. Serialized material counts are asset-copy counts, not active renderer
counts, draw calls or performance estimates. This shader's native allowlist is
narrower than the catalog's property-schema candidates. The runtime additionally
requires positive static world geometry/ancestry proof and vetoes actors,
animated/skinned meshes, UI, particles, video, water, held/interactable sources
and live unsupported effects. A shader name alone never proves eligibility.

| Route | Native family | Reviewed main parameter contract |
| --- | --- | --- |
| 1 | `Amp_Basic_N_MRAO` | HIGH tint/UV/desaturation/boost/dim; native continuous wall and saturated wall × alpha clip |
| 2 | `Amp_Low/Amp_Basic_N_MRAO_Low` | LOW tint/ST/desaturation; native world-height and integer wall toggle; LOW `_Cutout` |
| 3 | `Amp_Basic_WallFade` | HIGH projected/UV albedo and native continuous wall threshold |
| 4 | `Amp_Low/Amp_Basic_WallFade_Low` | LOW projected/UV albedo and native world-height wall `_Cutoff` |
| 5 | `Amp_Basic` | HIGH albedo with native dissolve/vertex influence/moss/emission vetoes; solid branch still clips `1 - _Cutoff` |
| 6 | `Amp_Low/Amp_Basic_Low` | LOW albedo; native reverse clip `_Cutout - albedo.a` |
| 7, 8 | HIGH/LOW Prop shader | Unsupported: object-map/highlight/dissolve/death/moss/fresnel contracts remain native |
| 9 | `Standard` | Actual installed native variants: opaque only, fixed admitted blend/depth state; official Unity alpha formula implemented behind an explicit fixture boundary |
| 10 | `Legacy Shaders/Diffuse` | Original `_MainTex_ST` and `_Color.rgb`; installed catalog has no bound serialized materials, only shader/fallback copies |

### Exact compiled keyword intersections

The two same-name HIGH N_MRAO objects have different payloads: the bundle's
308551-byte compressed programs include WORLD branches; `resources.assets:559`
has 83361 bytes and lacks WORLD. Both copies' directly addressed plain, wall and
wall+alpha branches were reviewed independently; matching declarations alone
would not establish this. All 13 exact root559 material consumers have authored
WORLD off, but that static fact does not authorize future dynamic WORLD activation.

The contract additionally verifies 36 addressed **byte-identical DXBC payload**
equivalences: all 12 reviewed root HIGH programs match corresponding HIGH bundle
programs, all 12 root LOW programs (including casters) match LOW bundle programs,
and the eight Standard and four Legacy root programs match their built-in bundle
copies. Binding descriptors remain independently pinned; payload equivalence does
not authorize the compiled keyword branches absent from a stripped copy.

The additional LOW N_MRAO object `sharedassets9.assets:30` lacks WORLD and ALPHA
branches, and strips separately activated desaturation/wall combinations. Both
Standard copies declare ALPHATEST and smoothness-alpha keywords but contain no
compiled main branch for either. The runtime therefore admits the *intersection*
of actually available effect-keyword sets across all objects with the same name.
Do not infer support from the union or from a float toggle alone.

After removing separately approved native lighting keywords, the reviewed sets
are:

| Route | Available effect sets across every same-name object |
| --- | --- |
| 1 | empty; WALL; WALL + ALPHA |
| 2 | empty; DESATURATION + LOW_WALL |
| 3 | empty; WALL_OFF; WORLD |
| 4 | empty; DESATURATION; WALL_OFF; WALL_OFF + DESATURATION; WORLD |
| 5 | empty; WORLD |
| 6 | empty; DESATURATION; WORLD |
| 9, 10 | empty |

WALL=`_WALLFADE_ON_ON`, ALPHA=`_DIFUSE_ALPHA_ON_ON`,
LOW_WALL=`_TOGGLEWALLFADE_ON`, WALL_OFF=`_TOGGLEWALLFADEOFF_ON`,
WORLD=`_WORLDSPACE_ON`, DESATURATION=`_DESATURATION_ON`.

The shader implements some additional separately reconstructed equation branches
for testing/possible future audited admission. That does not expand the runtime
allowlist. In particular, Standard cutout uses Unity 2021.3.5's original
`UnityStandardInput.cginc` Alpha boundary: color alpha alone with
`_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A`, otherwise texture alpha × color alpha.
The supplied native game's stripped Standard cutout fallback is unverified and
remains excluded.

## Exact retained parameter math

HIGH vertex UV uses the hidden `_texcoord_ST`: `(uv * ST.xy + ST.zw) * _UVTiling +
_UV_Offset`. HIGH ignores `_MainTex_ST`; LOW/Unity use `_MainTex_ST` directly.
Hidden `_texcoord` remains a declared property so copying original material
properties also preserves its texture transform. Renderer and individual slot
MPBs retain the original property names, including `_Tint`, `_Color`, `_Cutoff`
and `_Cutout`; the shader never overwrites the original blocks.

Both AMP world projections are signed ZY, XZ and XY, with the Z projection's X
sign reversed. They use the original interpolated vertex world normal, not a
renormalized fragment normal. HIGH weights are `pow(abs(normal), original
_WorldSpace_FallOff)`; LOW weights are `abs(normal)`. Both divide by the weight
sum plus `1e-5`. HIGH world scale is `_WorldSpace_tiling`; LOW uses only
`_MainTex_ST.x`, without its Y scale or offset. These equations were derived
from the family-specific compiled branches rather than presumed from the old
terrain shader.

AMP tint alpha is unused. HIGH desaturation uses luma `.299/.587/.114` before
`_Diffuse_Boost`. HIGH N_MRAO alone adds the native unclamped dim interpolation
toward `.299/.587/.115` grey × `_DimmFactor`, controlled by `_IsDimmed`.
LOW desaturation is active only in its compiled keyword branch and has no HIGH
boost or dim properties. Active emission, moss, fresnel, dissolve and vertex
animation keywords/values must remain vetoes even when a contradictory saved
float appears disabled.

Native wall uniforms stay outside ShaderLab `Properties`: `_TilesOcclusionMap`,
`_EnableOcclusionMap` and integer `ToggleWallFade` must continue to receive native
camera globals, rather than being shadowed by a material default. HIGH uses the
reviewed native continuous simplex/distance/radial/foundation equation, including
world scales 6/7/10 and time drift `.02/-.04/.006`. HIGH N_MRAO clips the saturated
product of continuous wall coverage and optional albedo alpha; two independent
clips would visibly change its intermediate pictures.

LOW gates wall clipping at **world** height >= .4 and any nonzero integer toggle.
LOW N_MRAO remaps *both* sampled R and alpha channels as `1 + enable * (sample-1)`
before its native projected-depth comparison, then clips against `_Cutout`.
LOW WallFade leaves the sampled channels untouched and clips against `_Cutoff`.
HIGH instead multiplies the resulting map mask by enable. GL projected depth is
converted from [-1,1] to [0,1] to reproduce the native DX depth comparison.
`_GHVRWorldNeverFade` defaults to 0; any explicitly proven per-source floor bypass
must still retain independent original albedo cutout. The runtime cannot infer
this source-local property from a shared material alone.

Native HIGH Basic's guarded non-dissolving branch has wall amount 1 and still
clips at authored `_Cutoff > 1`. LOW Basic deliberately retains its original
reverse-alpha inequality, including when its diffuse-alpha toggle is off.

## Render state, depth and shadows

Every reviewed AMP native main pass uses Cull Back, ZWrite On and Blend One Zero.
Native AMP RenderType is `TransparentCutout` with Geometry+0 queue. The runtime
must preserve the original override tag and render queue when replacing a
private material; this shader's Opaque/Geometry defaults are for Unity families.
Standard uses property-bound blend/depth values; only Mode0, SrcBlend1,
DstBlend0, ZWrite1 is admitted. Original renderer shadow flags remain intact.

The new explicit caster uses original vertices and Unity's shadow bias. Native
LOW N_MRAO, LOW WallFade and LOW Basic casters contain no texture or clip at all;
HIGH N_MRAO/WallFade use their opaque Diffuse fallback caster. Their original
shadow silhouettes therefore stay opaque independently of main-view wall/alpha
fades. HIGH Basic's guarded caster retains `clip(1-_Cutoff)` (actual native caster
program516); supported Standard alpha uses the official Unity alpha boundary.
Actual installed Standard admission remains opaque. Main forward depth is still
written by the exact source geometry. This preserves reviewed caster/depth
behavior while omitting native deferred/PBR lighting passes.

## Focused checks and evidence limits

Run `python3 scripts/check-world-material-shader.py`. It verifies pinned native
source/object/program hashes before actual Unity 2021.3.5 GL rendering, records
all source hashes and generated probe shaders, checks compile errors separately,
and asserts the tracked inputs did not change during the run. `--production-only`,
`--case` and `--skip-native` are explicitly partial development runs.

The GL reference independently orders the reviewed original parameter equations.
A source-bound HIGH probe replaces only the simplex sample with a declared
boundary; a separate production-function probe compares actual simplex pixels
to 20 independent original DXBC float32 samples. A source-bound caster probe
changes only the final shadow output to visible green to test the actual caster
fragment's clip decisions. These boundaries are explicit: native Windows bytecode,
original game PBR, game artwork/controller behavior and headset frames are not
executed by the fixture.

Actual generated meshes have two native material slots, nondefault tiling/offset,
signed tilted normals, gradient alpha, per-renderer/per-slot MPBs, original
world transforms, a live occlusion texture and integer globals. Checks cover
route-specific albedo, tint, desaturation, dim, alpha, projected depth, continuous
wall pictures, global toggles/enable, per-slot color/cutoff, fog, separate stage
lighting, rear-face culling, depth occlusion and native caster opacity. Negative
controls mutate specific complete production-source rules and must fail at a
matching rendered assertion; shader/C# compilation failures never count as
successful controls. The retained source controls demonstrate detection rather
than merely reimplementing a candidate-count formula.

The initial production checkpoint passed 314 rendered assertions. The expanded
original stage shader had 318 rendered assertions and 25 source causal controls.
The appearance follow-up adds 99 rendered assertions (417 total) and one ambient
omission control (26 total).
The final run and every causal result are recorded in the worker's private
`.planning/debug/world-material-shader/` evidence. Automated pixels establish
fixture behavior only. They do not establish FPS/GPU improvement, Steam Frame
appearance, game camera integration, multiplayer performance or original HMD
wall-mask parity; those require the maintainer's next hardware run.

The independent reconstruction also exposed historical `ScenarioCheapTerrain`
differences in HIGH UV/ST/projection/dim and LOW foundation/cutout handling.
Those legacy files are outside this worker's ownership and remain unchanged.
The new world-material stage uses the independently reviewed equations; parent
integration owns precedence over the older coarse terrain/environment shaders.


## Frame636 pale-wall regression

The supplied four Frame636 screenshots retain detailed pale stone/plaster/wood
patterns. Its effective setting is stage2. The actual `CR_INT_Plaster_Wall_01`
texture census binds `CR_INT_Wooden_Int_Wall`, not a missing texture. Its addressed
original material has tint `(0.8192,0.8091,0.8208)`, diffuse boost1, desaturation0
and dim0. The exported original texture contains the same pale plaster, stone and
wood artwork. Raw stage2 deliberately removed the scene lighting that normally
darkens it. Terrain substitution alone selected only some sources, allowing
native dark output and bright unlit output to alternate with camera admission.
The renderer-scope/substitution correction lives in the separate runtime lane.

The shader correction multiplies stage2 albedo by the original renderer SH
ambient at vertices. `ShadeSH9` uses the engine's native SH coefficients and
active color-space conversion; no guessed darkening constant, altered material
tint, main light or per-pixel lighting is introduced. The original game
`globalgamemanagers` PlayerSettings is Gamma, which the new fixture pins by full
source and raw object hashes. The asset-builder project's own Linear setting does
not establish the game's runtime color space.

`appearance-contract.json` additionally pins seven original material/texture
objects and their resolved native shader identities: the reported interior wall
and mausoleum, HIGH/LOW Crypt stone, and HIGH/LOW DLC ship wood. Read-only
`extract-appearance.py` verifies original source/object hashes, local albedo
PPtrs, texture color space and authored properties; converted images remain
private debug evidence. Actual Unity renders use those original color images,
tints, texture transforms and keywords. Independent Unity CPU SH evaluation
provides the expected ambient modulation at weights0/.4/1 over three camera
views. A bounded colored scene probe exposes the pale-albedo/ambient distinction;
its values are fixture inputs, never a production brightness constant. The
ambient-omitted causal mutation produces a visible RGB error on the reported
interior wall. Additional comparisons prove stage1 ignores this setting.

These are source-bound color/ambient and view-independent modulation proofs.
They do not execute native Windows PBR, reproduce the measured scene's actual
probe, establish HMD pictures or measure GPU/frame-time gains. The next Frame
review must confirm the native/proxy coverage correction and acceptable scene
brightness across rooms, other biomes, DLC and multiplayer.
