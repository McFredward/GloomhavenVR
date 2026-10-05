# Actual full Campaign Vulkan compute proof

The complete-game manifest uses `graphicsApi: "Vulkan"`. Startup-only/probe
builders keep their existing graphics recipe. Shader identity, source paths,
GUIDs, localIDs, original kernel order, instructions and allocation mathematics
are unchanged. Schema one and `stage(...)` remain compatible.

`QuestCampaignComputeValidation` selects the manifest's exact Android graphics
API. It retains legacy GLES manifest support, rejects unknown APIs and validates
all original source identities before builds and actual platform kernel banks
after builds. Full-game compute bytes are raw SPIR-V 1.0, native renderer 21 and
level zero. This differs from Unity's combined graphics-stage SMOL-V container.

`compiled.validate_objects` dispatches to a strict executable audit by manifest
API. Vulkan reflection verifies actual `GLCompute/main`, `LocalSize`, all native
resource properties, descriptor set/binding coordinates, sampled/storage image
dimensions, allocation-derived image formats, uniform scalar types/member
byte offsets and structured-array stride/offset. Unity's cooked packed binding
coordinates, rather than original D3D register numbers, bind the SPIR-V resources.
Sampler bindings are audited separately, including the compiler's point-clamp
sampler used for integer reads. Integer read query/fetch/branch and Vectorscope
buffer-count/atomic/branch evidence must remain in the executable.

The generic receipt field is `actualExecutableBytesVerified`; Vulkan also emits
`actualVulkanSpirvBytesVerified`. Existing GLES receipts retain
`actualGles31BytesVerified`. A build must use the field for its actual backend.
No source-only readiness flag or image-format substitution is used.

## Original capability and edge behavior

The unchanged original MSVO gate rejects Android OpenGL, defined in the actual
original assembly as Android with an API other than Vulkan. Consequently Vulkan
may use the original 18 MSVO kernels when actual compute support and original
R32_SFloat/R16_SFloat/R8_UNorm LoadStore format checks pass. The HDR 3D LUT baker
likewise retains its original capability gate. The manifest explicitly records
Vulkan reachability subject to these original capabilities. No synthetic feature
disable switch or fabricated device support value is introduced.

Both backend paths retain explicit D3D-zero integer Load guards and native
Vectorscope invalid-element atomic discard. Vulkan texel output validation
already discards out-of-range image stores; no guessed coordinate clamp is
added. The generic SPIR-V instruction reference alone is insufficient to infer
Vulkan image-write semantics: the Vulkan environment defines those validations.
([Vulkan image operations, Texel Output Validation and Integer Texel Coordinate Validation](https://docs.vulkan.org/spec/latest/chapters/textures.html),
[Microsoft Load](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-to-load))

The actual native formats remain RHalf/RGHalf/R8/RG16/ARGBHalf where originally
allocated. They require the Vulkan extended-storage-image-format feature and
actual per-format support. Storage image shader format and bound image format
must agree; changing RHalf to RFloat changes both memory size and conversion.
([Vulkan storage image format requirements](https://github.khronos.org/Vulkan-Site/guide/latest/storage_image_and_texel_buffers.html))

Vulkan permits a finite float32 value outside half-float range to become maximum
finite or infinity. The real host MSVO fixture currently produces native D3D's
65504 for its finite 100000 sentinel. No conversion clamp or quality/math change
was introduced merely from the specification; a broader original/device parity
claim requires original D3D and actual Quest readback. The original D3D finite
storage conversion requirement and observed host result are separate evidence.
([Vulkan floating-point format conversions](https://docs.vulkan.org/spec/latest/chapters/fundamentals.html),
[Microsoft original data conversion](https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-resources-data-conversion))

## Repeatable host proof

`host_vulkan.py` builds the owned host-only C harness against a supplied pinned
Vulkan-Headers tree and the host Vulkan loader, validates every original program
with `spirv-val --target-env vulkan1.0`, then creates all 36 real compute pipelines.
It dispatches the actual Android-cooked SPIR-V without language/body edits.
Descriptor bindings come from the audited executable/native metadata. Vulkan
robustBufferAccess is deliberately disabled, so the fixture cannot use driver
buffer robustness to hide a missing original atomic guard. Original typed image
formats are queried for support before their actual allocations.

```sh
OWNER_BUILDER_PYTHON tools/quest-compute/host_vulkan.py \
  --bundle ACTUAL_ANDROID_VULKAN_BANK \
  --manifest OVERLAY/QuestCampaignEvidence/compute-recovery.json \
  --cache PRIVATE_PROOF_DIRECTORY --headers PINNED_VULKAN_HEADERS/include \
  --icd /usr/share/vulkan/icd.d/lvp_icd.json --receipt PRIVATE_RECEIPT
```

The 2026-10-05 actual lavapipe result passed all 36 pipeline creations and these
bounded functional fixtures:

- Original Texture3DLerp: 5×3×2 output/From and 2×2×1 To; 26 invalid To reads
  return zero and all 120 original ARGBHalf components match.
- Original Vectorscope: Clear resets 25 nonzero counters; Gather discards eight
  pure-red index-26 writes outside 25 elements and produces seven black samples
  at index 12. The projection/rounding is unchanged.
- Original MSVODownsample1: 17×19 depth source within a 32×32 covered dispatch;
  all five native RHalf/RFloat and array outputs match the instruction-derived
  formula, including 701 invalid zero-source reads and finite-overflow sentinels.

The private receipts explicitly retain `hardwareVerified: false` and
`originalPixelParityVerified: false`. All-pipeline creation plus these fixtures
establish host compilation and these particular values, not exhaustive original
D3D pixel parity, every campaign dispatch or Quest hardware output.
