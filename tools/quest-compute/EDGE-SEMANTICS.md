# Original compute resource edges on Android backends

The adapter retains all 13 original compute objects and 36 kernels. This audit
uses their original DXBC instructions and the owner's unchanged
`Unity.Postprocessing.Runtime.dll`, SHA-256
`ce62c0bc9c421f45fea6b81a0869761b749365dce5a9dffd5d639b2f170c788c`.
Changing that assembly requires another native allocation/platform audit.
Source and host GPU evidence do not establish Quest hardware output.

## Required differences between APIs

Direct3D integer texture loads outside the bound texture return zero.
GLES `texelFetch` outside the valid coordinates has undefined results. Every
original LD in this bank therefore queries its own bound texture's dimensions,
returns all-zero RGBA outside those dimensions, and retains the original Load
inside them. Coordinates are evaluated once. A viewport guard alone cannot
prove that the actual bound texture has the same dimensions. The native census
contains exactly 18 LD instructions: only float4 2D/3D textures, literal mip
zero and no offset operand. Unknown mip/overload/dimension or multisample/UAV
load fails recovery for re-audit. Normalized samples and gathers keep their
original sampler addressing.
([Microsoft Load](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-to-load),
[GLES 3.1 specification, section 11.1.3.2](https://registry.khronos.org/OpenGL/specs/es/3.1/es_spec_3.1.pdf))

Invalid GLES image stores already have no effect. The negative/padded output
addresses in original MSVO upsampling retain that behavior without an added
clamp or quality change. SSBO accesses outside their storage have undefined
GLES behavior. Native Vectorscope uses `ATOMIC_IADD` (opcode 0xad), stride four
and byte offset zero: invalid element addresses are discarded by D3D. Only its
flattened element index is guarded against the actual bound buffer count. The
original colour projection, rounding and valid atomics remain unchanged; no
plot coordinate is clamped. The native instruction returns no result, and the
translator's introduced out operand is proven unused.
([GLES sections 6.4 and 8.22](https://registry.khronos.org/OpenGL/specs/es/3.1/es_spec_3.1.pdf),
[Microsoft atomic_iadd](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/atomic-iadd--sm5---asm-))

## Complete native kernel/caller audit

| Original object | Kernels | Integer texture reads and resource bounds |
| --- | ---: | --- |
| EyeHistogram | 1 | One LD, guarded against actual source size. Native viewport/thread bounds, 64 bins and histogram weighting retained. |
| ExposureHistogram | 2 | No LD; normalized sampling unchanged. Original 128-bin clear and bounded bin calculation retained. |
| Histogram | 2 | One LD guarded; original clear bounds, saturated channels and 256 bins retained. |
| AutoExposure | 2 | Progressive one LD guarded; fixed kernel has none. Original 128-bin shared reduction and one final output writer retained. |
| Waveform | 2 | One LD guarded; native input bounds, rounded channel positions in `height-1`, `width*height` elements and 16-byte RGB counter stride retained. |
| Vectorscope | 2 | One LD guarded. Original clear bounds retained; gather's flattened atomic index additionally checked against real stride-four element count. |
| Texture3DLerp | 2 | Three LD instructions total, each actual 3D source independently guarded. Original output dispatch/bounds and lerp math retained. |
| MultiScaleVODownsample1 | 2 | Eight LD instructions total guarded. Original 16/32-pixel tile padding, shared-memory bounds and native output image stores retained. |
| MultiScaleVODownsample2 | 2 | Two LD instructions guarded. Original 8/64-pixel tile padding, shared reduction and output stores retained. |
| MultiScaleVORender | 4 | No LD; original sampled/array atlas addressing and image stores retained. |
| MultiScaleVOUpsample | 10 | No LD; normalized samples/gathers and invalid image-write discard retained. |
| Lut3DBaker | 4 | No LD; original output dimension bounds and colour math retained. |
| GaussianDownsample | 1 | No LD; original samples, output bounds and four-component UAV retained. No direct caller in the original 71-assembly census; no invented allocation substitution. |

The integer restoration also preserves scalar-literal swizzles and independent
known lanes during partial register MOVs. Native dispatch indices carried as
float bits must not become constant zero when the remaining register lanes are
unknown. An actual odd-size Downsample dispatch exposed this issue; the focused
fixture and real five-output test cover it.

The full-game backend now uses Vulkan. Its actual raw-SPIR-V and host driver
proof is recorded in [VULKAN.md](VULKAN.md). Both Vulkan and GLES integer loads
need the explicit D3D-zero bounds guards, and both retain invalid image-store
discard under their API environment rules. The GLES evidence below remains
secondary proof; its platform exclusions do not apply to Vulkan.

## Existing native GLES capability decisions

`AmbientOcclusion.IsEnabledAndSupported` for MSVO requires compute support,
`!RuntimeUtilities.isAndroidOpenGL`, and LoadStore support for R32_SFloat (49),
R16_SFloat (45) and R8_UNorm (5). The original Android OpenGL getter returns
true for Android when the graphics API is not Vulkan. The normal
`PostProcessLayer.BuildCommandBuffers` caller checks this settings method before
`RenderAfterOpaque`/`RenderAmbientOnly`. Its
18 MSVO kernels remain physically retained, but the original normal Android
GLES path excludes their dispatch. The public `BakeMSVOMap` bypass exists;
the original 71-assembly direct-call census has no callers of that method.
No replacement platform decision is introduced.

This matters because native RHalf/RGHalf/R8/RG16 UAVs require r16f/rg16f/r8/rg8
image formats that GLES 3.1 core does not accept. A formatless writeonly image
declaration can be legal ESSL, but `glBindImageTexture` still restricts the bound
format. It does not make the original allocation portable. Replacing RHalf
with RFloat would change size/precision and is not done.
([GLES image-format table, section 8.22](https://registry.khronos.org/OpenGL/specs/es/3.1/es_spec_3.1.pdf),
[ESSL 3.20 image layout qualifiers](https://registry.khronos.org/OpenGL/specs/es/3.2/GLSL_ES_Specification_3.20.html))

`ColorGradingRenderer.Render` requires a graphics API other than OpenGLCore or
OpenGLES3 for HDR's 3D LUT compute baker. Its existing GLES branch uses
`RenderHDRPipeline2D`. All four Lut3DBaker kernels remain present. This does not
exclude the separately used Texture3DLerp kernels or external 3D LUT content.

Schema-one manifests and cooked receipts include the unchanged original method,
assembly hash and relevant guard evidence in `nativePlatformCapabilityEvidence`.
They explicitly set `allKernelsActualGlesDriverValidated: false`. Cooked GLES
source/binding closure is not an all-kernel driver compilation claim.

## Actual bounded host GPU evidence (2026-10-05)

The newly cooked Android bank passes all 13-object/36-kernel Unity/native-byte
checks. Actual Mesa Intel GLES dispatches, using those cooked original kernels,
pass these functional fixtures:

- EyeHistogram: 13x11 source with original 16x16 viewport; 113 invalid reads
  contribute the original zero-colour bin. All 64 uint counters match; sum 11428.
- Texture3DLerp: 5x3x2 From/output and 2x2x1 To; 26 out-of-bounds To reads return
  zero. All 120 output RGBA components match the original lerp formula.
- Vectorscope: 5x3 input and 25 stride-four counters. Eight pure-red endpoints
  project to the original flattened index 26 and are discarded; seven black
  samples increment index 12. Original Clear also resets nonzero initial data.
- Waveform: native RGB atomic/stride fixture; all counters match, total 768.

The five actual MSVO Downsample1 outputs also pass with a 17x19 source and a
32x32 covered tile, including 701 zero-source reads, original RHalf/RFloat
formats and all array layers. Since the unchanged original GLES capability
branch excludes MSVO, this runs on desktop GL4.3 with only the cooked source's
language/unused-extension prologue adapted; its instruction body is unchanged.
It is explicitly not a GLES/device-kernel acceptance claim. Native finite
float32-to-float16 overflow storage uses maximum finite 65504 in this fixture.
([Microsoft data conversion](https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-resources-data-conversion))

Receipts remain private owner-data artifacts. Run `host_edges.py` for the
original Clear/Gather, odd 3D lerp and native-format Downsample fixtures;
`host_histogram.py --source-width 13 --source-height 11` for the viewport/source
mismatch. Both use actual cooked GLSL and retain `hardwareVerified: false`.
