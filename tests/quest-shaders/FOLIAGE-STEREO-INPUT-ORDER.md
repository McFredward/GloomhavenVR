# Native fragment FrontFace and generated stereo declaration order

The first actual full Unity2021.3.5 Android/Vulkan bundle build encountered26
`Amp_Basic_Foliage` errors: an ordinary input cannot follow a system-generated
input. These Unity-generated multiview keyword combinations are outside the
original native alias graph; the earlier51,564-alias PASS remains valid evidence
for that graph, but cannot establish all additional bundle combinations.

The native input signature witnesses90 fragment instruction/interface pairs with
systemValue9 / `SV_IsFrontFace`. Unity's generated `UNITY_VERTEX_OUTPUT_STEREO`
expands to a user `BLENDINDICES0` input under multiview. Appending it after the
native `SV_IsFrontFace` fails Unity's D3D shader front-end. The adapter moves only
that macro line immediately before FrontFace, after the original user fields.
Every original declaration, relative declaration order and instruction byte in
the recovered HLSL remains unchanged. Vertex headers, ShaderLab, keyword/pass
identities, original DXBC/interface hashes, GUIDs, metas and materials are retained.

Moving the macro to the beginning of the struct was tested and rejected: its
different packing caused a smooth native UV to have incompatible flat/smooth
interpolation between actual Vulkan stages. The final adapter preserves matching
original user-field packing. Do not replace this with a struct-start insertion.

The bounded same-version fixture reproduced26 original failures, compiled all26
corrected combinations and seven actual native controls, checked matching native
stage locations/types/interpolation, and built a real scoped Android/Vulkan
Foliage asset bundle. All seven original mono control vertex/fragment SHA256s are
byte-identical. SPIR-V validation and33 actual Mesa Vulkan pipelines pass; a
missing native entry-point negative is rejected. This proves compiler/driver
compatibility. It does not establish original pixel parity or headset appearance.

Private evidence is under
`/home/claw/quest3-local/full-shader-validation/foliage-stereo-order-v1/`:
`unity-order2.log`, `unity/FoliageWitness/results.json`,
`actual-driver/actual-vulkan-pipelines.json`. The previous `unity.log` is the
explicitly incomplete struct-start experiment. No full shader sweep was repeated.

## Apply to a stopped retained project

Fresh reconstruction already uses the adapter. For an existing imported project,
call the actual witnessed repair using the current checked-in producer:

```python
import sys
sys.path.insert(0, 'tools/quest-shaders')
import produce
receipt = produce.repair_fragment_stereo_inputs(
    '/path/to/stopped/retained/project',
    compiler_witness='/home/claw/quest3-local/full-shader-validation/foliage-stereo-order-v1/unity')
```

The repair plans and verifies all bytes before mutation. It changes only the90
witnessed include files and their manifest `sourceSha256`/adapter proof fields.
All688 shader records,9,187 material records and51,564 original aliases remain
exactly unchanged. The manifest also registers the repair receipt by portable
relative path/scope/count. The receipt binds the before/after manifest hashes,
exact native instruction/interface pairs, include/metas/rest-byte hashes,
actual compiler/driver witness files and the current producer/helper hashes.
Its separate identity ledger physically binds all688 unchanged ShaderLab sources,
9,187 materials,11,566 other includes and their metas/GUIDs. The parent builder
captures this derivative provenance. It does not rewrite historical preparation
or full native-validation receipts.

The files are atomically replaced and the complete receipt is written last. A
caught write error restores the original include/manifest bytes. A killed process
leaves an explicit transaction marker and fails closed; inspect and restore its
recorded original bytes before retrying. Completed repairs can be checked again
without rewriting them; changed receipt/output/identity bytes are rejected.

The private complete-copy application proof is under
`/home/claw/quest3-local/full-shader-validation/foliage-stereo-repair-v1/project/`.
The operation preserves the retained include filenames. The separately committed
Windows filename shortening belongs to fresh reconstruction, not this derivative.

The previous exhaustive native gate/cache cannot be reused after the90 include
hashes change. A normal retry retains imported identity checks and performs real
Unity bundle/player compilation under the minimum shader mode; it must not claim
a new full51,564-alias validation PASS from the scoped fixture.
