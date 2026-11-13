# Native ForwardAdd compiler products

The actual Android Player build `dc3c56bb091b` reported missing original banks
for `DIRECTIONAL SHADOWS_CUBE` in recovered Amp, DynamicFog and UnseenGround
shaders. Its platform defines establish `UNITY_PASS_FORWARDADD`; the repeated
`FORWARD` pass name alone cannot distinguish ForwardBase from ForwardAdd.

The original manifest has 688 physical shader identities and 51,564 aliases.
Exactly 75 original DXBC shader identities contain ForwardAdd: 44 retain 13
distinct light/shadow projections, and 31 retain five. Reconstructed independent
light/shadow pragmas can generate 80 products. The original graph contains no
directional cube-shadow bank. Unity2021's `AutoLight.cginc` identifies cube
shadows as point-light shadows, consistent with the native graph.

`QuestCampaignShaderStrip` runs only under `GHVR_QUEST_GAME && UNITY_EDITOR`.
It audits the original manifest, maps physical GUIDs to their imported paths and
names, and removes Vulkan ForwardAdd products whose nine native light/shadow
keywords have no original ForwardAdd projection. Original authored features,
instancing and Unity-added XR keywords are not rewritten. Other GUIDs, source
roles, passes and compiler backends are untouched. Unknown authored-feature
combinations still reach the original strict stage selection; no approximate
program or keyword fallback is introduced. Manifest drift fails closed.

The bounded same-version fixture uses the exact original N_MRAO_Trans shader,
its 93 native instruction includes, and unchanged metas. It audits all 51,564
manifest aliases, forces all 80 light/shadow products plus a multiview negative
through a real Android Vulkan AssetBundle build, and observes the public
preprocessor callback. It excludes 67 unsupported products, observed as 201
tier/stage compiler entries. Three actual valid DIRECTIONAL, POINT+CUBE and
SPOT+DEPTH vertex/fragment SPIR-V pairs remain byte-identical before/after the
bundle. Nine negative controls cover other passes, native projections and a
contradictory native manifest. Fifteen focused C# controls additionally cover
unknown GUIDs, source roles, GLES, imported identity and manifest drift. The
source also compiles against the exact Unity2021.3.5f1 SDK.

Private actual proof: `full-shader-validation/cube-strip-v1/unity/` beneath the
Quest local cache; `CubeStripWitness/result.json` and the adjacent `unity.log`.
The full matrix was not repeated. This proves the compiler callback and native
bank retention, not headset imagery or completion of the complete Player.

For a retained project, add only the new Editor helper from the checkpoint to
`Assets/Quest/Editor/QuestCampaignShaderStrip.cs`; no manifest, ShaderLab,
instruction include, material or meta rewrite is needed. Record the added helper
in the actual build provenance; it has no historical preparation-output baseline.
The next normal native Player build remains the complete compilation check.
