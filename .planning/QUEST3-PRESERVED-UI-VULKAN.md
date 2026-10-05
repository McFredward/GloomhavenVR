# Preserved UI source and native consumer validation

The complete Campaign producer intentionally preserves six audited original UI
and Bloom sources instead of replacing them with generated native shader forms.
On 2026-10-05, a bounded private fixture exercised the current root restoration
helpers, `campaign_shaders.preserve_sources`, and `produce.restore_project` against
the canonical recovered original identities. It copied five actual consuming
materials and their four original backing/noise textures. No owned PC input,
canonical recovered asset or desktop source was modified.

Private evidence: `/home/claw/quest3-local/full-assets/preserved-ui-vulkan-v1`.
`prepare.py`, the generated project, source snapshots and `provenance.json` retain
the recipe; `evidence.json` summarizes the final checks. Original proprietary
assets and native compiler artifacts remain outside Git.

| Actual evidence | Count |
| --- | ---: |
| Preserved original shader identities | 6 |
| Original material consumers | 5 |
| Original backing/noise textures | 4 |
| Imported texture GUID/localID/scale/offset links | 8 |
| Original hardware-tier/pass compiler banks | 72 |
| Actual Android Vulkan SPIR-V stages | 144 |
| Targeted negative controls | 15 |
| Native host Vulkan pixel cases | 10 |

The final `QuestCampaignShaderValidation` passes against real Unity 2021.3.5f1
Android Vulkan compiler output. Stage bytes, reflection and hashes are retained
under `unity/ProbeOutput/campaign/`. The four textures retain the native dimensions,
mip counts, sRGB flags, filtering, wrapping, anisotropy, mip bias, readability and
streaming policy. The 1920×1080 menu backing correctly uses its original Sprite
importer classification; this does not turn its material texture into an XR array.

The first actual import exposed a real integration blocker: Unity upgrades the
three preserved official Bloom sources to `UnityObjectToClipPos`, changing their
file hashes. The older Bloom gate already allowed those exact audited upgrades;
the Campaign gate initially rejected them. The shader worker's correction accepts
only those three GUID/name/native-object/source/imported-source tuples, with the
exact retained-source role and legacy source-receipt proof. All other source hashes
remain strict. Each shader rejects arbitrary hashes, reassigned GUIDs, a different
source role and a missing receipt; further controls reject a native texture width
mismatch, an absent compiled texture binding and an array sampler substitution.

`unity-vulkan.log` retains the initial Campaign rejection.
`unity-vulkan-imports.log` retains the fixture's overly narrow Default-importer
assumption, corrected using the original Sprite classification.
`unity-vulkan-corrected.log` records the final successful native gate, import and
consumer checks. The actual post-import shader snapshots are fingerprinted in
`evidence.json`; the three pre-import sources remain in the producer overlay.

The Vulkan host was Intel Graphics (RPL-P). Both original Splash materials render
nonuniform, opaque textured backings and respond to diffuse-texture substitution.
Both dissolve materials retain their noise inputs, thresholded transparency and
black-mask discard. Pixel captures and floating-point readbacks are retained.
These are native **host Vulkan** draws of the preserved sources; they do not claim
direct execution of the saved Android compiler banks, original Windows pixel
parity or a correct headset picture.

The focused experiment found no additional source-proven blocker after the narrow
Bloom acceptance fix. Whole-project asset compilation and hardware testing remain
the responsibility of their separate final gates. The fixture's rebuildable Unity
Library and Temp are removed after evidence retention.
