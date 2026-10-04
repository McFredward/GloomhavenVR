# Build 619: complete bar depth and native HIGH wall delivery

This review uses the immutable Build 618 capture in the main checkout's
`.planning/debug/frame619/inputs/`. Both banners identify Build 618. Source and
real Unity fixture evidence are distinguished below from unverified headset
results. No new supplied wall video accompanies this run.

## What the captured wall draws establish

Player.log has 94 `DRAW DELIVERY` records: 42 HIGH, 42 toggle-native and ten
mixed HIGH/toggle-native. None delivers `ScenarioSimpleEnvironment`; none
reports `lateToDrawChanged=True`. Nine restored `fade=0/block=False` endpoints
are now present (physical lines 8909, 9217, 9236, 9410, 22466–22469 and 26759).
The initial scenario's environment reports also show zero simpler materials;
the later scenario reports one. These counters exclude an observed replacement
wall shader as the explanation for the initial native wall pop. They do not
measure GPU pixels, native global uniforms or the headset image.

The optional cheaper non-wall shader can be disabled through
`dev.gloomhavenvr.perf.cfg`, `[Optimize] ScenarioSimpleEnvironmentShading=false`
(“Einfachere Umgebungsschattierung”). That restores original environment
materials. It is not a switch between different wall shaders in this capture:
the delivered wall shaders already are the original native PC shaders.

## Source-proven native inputs and branch discontinuity

The original `misc_high_shaders_assets_all.bundle` SHA-256 is
`9b01a64659be31bffe4d3ed7c803b4081f901a0c40e578af9f9f64a2f1fad9c7`.
Read-only extraction with `tools/ShaderDisasm` and DXDecompiler at commit
`e7c32662bbf5909e0a80f12e9d07f510bfd0e220` provides these original programs:

| Original shader | Asset path ID | ForwardBase blob | Compiled route |
| --- | --- | --- | --- |
| `Amp_Basic_WallFade` | 4740134886656557863 | 216 | DX11PixelSM50, DIRECTIONAL |
| `Amp_Basic_N_MRAO` | -3647566309600488322 | 388 | DX11PixelSM50, WALLFADE_ON, DIRECTIONAL, LIGHTPROBE_SH |

The legacy extractor filename says `GLES3`; parsed program metadata and the
`ps_5_0` DXBC header identify Direct3D11. The label is not the executed platform.
Private evidence retains the metadata, original DXBC, assembly, binding tails,
their hashes and exact tool revision in `frame619-native-shader-provenance.json`.
The corresponding bytecode hashes are respectively
`4d6ae64bfd34ab96f4234f66993e231262cb2359c005b08ef854927897a6bc0b` and
`d6d021cea96a9eae2787bc9e590bc943734b79b91fc7aba2df65b1f60cbe7b25`.

Original serialized binding tails resolve the HIGH map scale to
`_EnableOcclusionMap` at byte 96 (`cb0[6].x`), enable integer `ToggleWallFade` at
byte 100 and `_Cutoff` at byte 104. N_MRAO uses the same fields at bytes 160,
164 and 168. The historical `_ToggleWallfade` alias written by the mod is absent
from these two original native programs.

Native `TilesOcclusionGenerator.UpdateCommandBuffers` publishes
`_EnableOcclusionMap=1` **inside its camera command buffer**, attached at
`BeforeGBuffer`. Disabling that camera prevents the command buffer from
publishing the value. Build 619 now supplies the actual native map scale in the
renderer MPB instead of depending on a flat-camera global producer. This closes
a source-proven dependency gap. Whether the captured Build 618 global was zero
is unmeasured; attributing its particular hardware symptom to that missing
producer remains an inference.

The exact original HIGH discard branch also uses:

```
M = native map term * _EnableOcclusionMap
S = native smoothed foundation/distance/screen-edge term
A = max(M,S) + 42*noise*(1-max(M,S))
B = M > 0 ? 1 : S
clip(1 + ToggleWallFade*(A*B-1) - cutoff)
```

The original N_MRAO route saturates the value before subtracting cutoff; with
the authored `0 < cutoff < 1` this has the same discard boundary. Both assembly
programs directly contain the `M>0` comparison and conditional multiplier.
For a valid positive noise sample `.03` above the foundation, continuous
positive M can retain almost the whole wall until the held map makes M exactly
zero. Correct MPB/cutoff intermediates alone cannot prevent that discontinuity.

For these **exact verified shader families** Build 619 progressively supplies
native solid (`r=0,a=0`) or native held (`r=1,a=0`) texels with the existing
ranked Perlin ordering and unchanged authored cutoff. Both native endpoints,
native foundation/vignette, original shader/material, opaque depth and original
geometry remain intact. Native and swapped wall attachments use the same
delivery. Unknown/themed shaders and LOW keep their prior paths. Point filtering
is necessary: bilinear filtering reintroduces positive M between endpoint texels.

The cached bank is 64 maps of 64×64 RGBA32, no mipmaps: at most 1 MiB. The scenario
preparation coordinator calls idempotent `PreparePresentationMasks` while the
genuine loading symbol is visible, in a separate stage before card/figure bakes.
Room reveals reuse it. Normal animation allocates/uploads no texture and does no
readback; driver teardown destroys the owned bank.

## Real pixel proof and its limits

`scripts/check-environment-budget-runtime.py` binds complete production wall
Apply/EnsureTextures, native prop MPB delivery, the shared visual clock and new
map helpers. Actual Unity 2021.3.5f1 GL graphics execute the original HIGH and
toggle-native **clip-equation surrogates** with full native S basis and a valid
parameterized noise sample. The original Windows bytecode, simplex-noise
function, lighting, artwork, procedural scenes and HMD are not executed.

The full-screen fixture avoids the earlier failed receipt's tiny 100-pixel
central sample, which happened to miss the first Perlin rank step. That failure
is retained rather than replaced with a false pass. Production actual pixel
counts across OUT are:

```
2304 → 2162 → 2005 → 1733 → 1437 → 1145 → 869 → 576 → 287 → 146 → 41 → 0
```

IN returns `146 → 576 → 1145 → 1733 → 2005 → 2162 → 2304`. The native foundation
retains all 2304 pixels through every intermediate and held state. All four
HIGH/toggle-native × wall/prop routes have the same tested coverage. PNGs and
complete count curves are retained privately.

The causal controls collect **both full curves before failing**:

- Prior continuous map: OUT `2304,2304,2302,2302,2302,2302,2302,2302,2302,2302,0`;
  IN `2302,2302,2302,2302,2304,2304,2304`. This demonstrates the old endpoint pop
  under the exact native branch boundary, independently of map-enable delivery.
- Missing actual `_EnableOcclusionMap` override with a disabled producer: every
  intermediate is zero; only final removal of the block restores 2304 pixels.
- Bilinear rank-map filtering rejects the exact binary native-texel contract.

This establishes corrected original inputs and a progressive source-backed
native fragment delivery. It does **not** establish the current headset picture,
prove this is the sole cause of the reported Frame symptom or claim a hardware
performance improvement. Native shader variants, low headset frame rate and
both eyes remain subjects for the next hardware capture. Existing bounded Debug
draw records now include renderer `enableMap` and `globalEnableMap`, while normal
player logging stays unchanged.

## Whole-bar depth: original widget and actual TMP bindings

Native `WorldspaceUI.InfoBar.m_InfoNumber` and label are `TextMeshProUGUI`.
In the shipped TMP runtime its `materialForRendering` reads `m_sharedMaterial`,
which is written by `fontSharedMaterial`. The previous `Graphic.material` write
affected an unrelated inherited field. Thus Images could show through a wall
while the circled number retained native depth. A submesh must likewise use its
passive `sharedMaterial` binding; material getters that allocate are avoided.

Build 619 uses the existing `PanelGraphicMaterial.Read` and matching type-specific
write for Images, TMP text and TMP inline-glyph/sprite submeshes. Every owned clone
gets the chosen ZTest. Native font/material replacement is re-adopted at the next
existing scan; restoration checks exact ownership and preserves a later foreign
native replacement. Native text/layout, stencil/clipping, original widget,
health/effect callbacks and coroutines remain unchanged.

The original `resources.assets` TMP shaders independently verify the actual
depth alias: Distance Field path ID 568, Mobile Distance Field 640 and Sprite
617 all use `ZTest=[unity_GUIZTestMode]`, ZWrite off. The asset SHA-256 is
`bdbb12b62374aee0a00a07f07e162c7a558c052996ea7be360a2d59bd0c8c34a`.
`check-actor-bar-depth-runtime.py` cross-checks this real metadata before running.

The suite loads the unchanged shipped TMP/UI DLLs into real Unity and exercises
native `materialForRendering` and CanvasRenderer behind an opaque wall. Test
rectangles replace font/glyph tessellation and an explicit shader models the
depth/stencil contract; the native Windows font shader/atlas is **not** executed.
Fourteen runtime/pixel assertions pass, with four causal defect controls for the
wrong TMP field, omitted inline symbol, stale native replacement and destructive
restore. Source/DLL hashes, pixel images and native shader metadata are retained.

First adoption and live mode change scan immediately. The existing staggered
two-second scan remains the fallback for newly pooled graphics/materials; the
fixture proves correctness **when scanned**, not same-frame discovery for every
native pooling/mesh-regeneration lifecycle. No per-frame hierarchy scan was
introduced. That remaining discovery bound must not be described as an
immediate whole-widget lifecycle guarantee.

## Focused validation

```
bash scripts/ci-build.sh Release
python3 scripts/check-actor-bar-depth-runtime.py
python3 scripts/check-environment-budget-runtime.py --case production \
  --case native-high-continuous-map --case native-high-enable-not-supplied \
  --case native-high-bilinear-map
```

The worker's strict build has zero errors/warnings. Bar validation is five
production/control variants. The environment run has 462 runtime assertions
plus the three causal native controls; it is partial evidence, not the complete
repository gate. The integrator runs the full final gate after all worker commits.
