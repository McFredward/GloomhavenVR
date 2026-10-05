# Complete native Campaign compute recovery

This developer-only pipeline restores all 13 original class72 objects and 36
kernels from the owner's recovered Unity 2021 assets. It includes the core
Resources `Shaders/EyeHistogram` object and all 12 PostProcessResources compute
references. No game asset, proprietary bytecode, compiled bank or recovered
shader source is included in this repository.

The original instruction path is DXBC → pinned vkd3d/SPIRV-Cross → HLSL. Original
names, order, thread groups, native constant-buffer fields, textures, UAV names,
structured-buffer strides, GUIDs and local fileID 7200000 are retained. No modern
PostProcessing shader revision is substituted for unknown original mathematics.

GLES also needs two interfaces that a literal DXBC register translation loses:

- Typed image storage is restored from the unchanged native allocation/binding
  bodies. RFloat/RHalf, RGFloat/RGHalf, R8/RG16 and ARGBHalf require their matching
  scalar/vector and precision qualifiers. The exact original post-processing
  DLL SHA-256 is required; a changed game DLL must be audited again. Gaussian's
  unused original four-channel output retains its DXBC type.
- Integer bit patterns carried through DXBC float registers are retained in
  integer temporaries. `asfloat` assignments capture the original integer
  expression once; known subsequent `asint`/`asuint` extractions use those bits.
  Actual floating point consumers and arithmetic remain in place. This avoids
  FXC denormal/NaN assumptions incorrectly deleting histogram/blur texture
  inputs. Reaching definitions are per-component and invalidate conditional
  writes. A new loop shape or changed pure bitfield helper requires review.

Unity's image qualifier contract is documented in the
[official compute shader manual](https://docs.unity3d.com/2022.3/Documentation/Manual/class-ComputeShader.html).
The allocation evidence comes from the owner's original
`Unity.Postprocessing.Runtime.dll`, not from a presumed package version.

## Builder integration

Load this directory as an isolated Python package with
`spec_from_file_location(name, '__init__.py', submodule_search_locations=[directory])`
and place that module in `sys.modules` before executing it. The public API is:

```python
manifest = module.stage(
    source_project, empty_overlay_directory,
    graphics_module=quest_builder_full_shaders_path,
    vkd3d=pinned_vkd3d_command,
    spirv_cross=pinned_spirv_cross_command,
)
```

`manifest` uses schema 1. Apply only `files` (26 source/meta files) and
`removePaths` (26 original asset/meta paths) to a disposable generated project.
Rewrite every catalog and native binding path using `pathMap`. The original
source project remains read-only and is checked again after conversion.

Copy the manifest to `Assets/QuestOriginalCampaign/campaign-computes.json`.
`shaders[*]` carries `assetPath`, `guid`, `localFileId`, source/meta hashes and
ordered `kernels`. Each kernel carries original DXBC/SPIRV/HLSL hashes, dispatch,
original interfaces and integer/image restoration evidence.

Do **not** copy `QuestCampaignEvidence/ComputeInstructionProof` into Assets or a
public tool archive. It is a private owner's intermediate bytecode cache. Only
the declared generated assets and the manifest belong in the generated project.

Run `QuestCampaignComputeValidation.ValidateSources()` before Addressables and
`Validate()` after the Android bank/player compilation. The default receipt is
`Temp/QuestCampaignComputeValidation/compiled.json`; optional
`GHVR_QUEST_COMPUTE_MANIFEST` and `GHVR_QUEST_COMPUTE_RECEIPT` override those paths.
The gate requires Unity 2021.3.5f1, Android and exactly OpenGLES3, matching original
GUID/localID, ordered source dispatch declarations, no compiler errors and a
native GLES bank containing every original kernel. The headless editor never
executes `ComputeShader.FindKernel` or `GetKernelThreadGroupSizes`: those device
APIs attempt to compile the current Null renderer under `-nographics`.

## Focused proof

```sh
python3 -m unittest discover -s tests/quest-compute -v
python3 tools/quest-compute/recovery.py --source-project ORIGINAL_STAGE \
  --overlay EMPTY_OVERLAY --graphics-module tools/quest-builder/full_shaders.py
python3 tools/quest-compute/proof.py --overlay OVERLAY --project EMPTY_PROOF \
  --editor-source unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignComputeValidation.cs
GHVR_QUEST_COMPUTE_OUTPUT=OUTPUT Unity -batchmode -nographics -quit \
  -projectPath PROOF -executeMethod QuestCampaignComputeValidation.BuildProof -logFile UNITY_LOG
OWNER_BUILDER_PYTHON tools/quest-compute/compiled.py \
  --manifest OVERLAY/QuestCampaignEvidence/compute-recovery.json \
  --bundle OUTPUT/quest-compute-proof.bundle --receipt OUTPUT/cooked-byte-validation.json
```

The last command uses the builder's existing UnityPy dependency, inspects actual
cooked class72 bytes, requires GLES renderer11/level3 executable GLSL, verifies
all 36 native and textual dispatch extents, original input/output properties,
structured buffer bindings and the allocation-derived GLSL image qualifiers.
It rejects a missing kernel, empty/non-GLES code or a misleading rgba32f image
substitution for an R/RG native allocation.

Actual owner-data proof on 2026-10-05 passed both the Unity Android gate and the
cooked-byte gate for 13 shaders / 36 kernels. The receipts explicitly keep
`hardwareVerified` and `originalPixelParityVerified` false. Compilation and
binding closure do not establish Quest GPU output or complete image parity.
