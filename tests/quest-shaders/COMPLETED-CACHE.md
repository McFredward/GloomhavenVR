# Completed native shader validation cache

`QuestCampaignShaderValidation.Validate` now reuses an atomic completed
`QuestCampaignShaderEvidence/completed-validation.json` only after checking the
exact graphics closure and the complete actual compiler receipt. A missing,
partial, malformed, changed, or unknown receipt triggers real validation.

The closure binds the manifest, all physical shaders/includes/metas, actual
ShaderImporter and dependency hashes, recursive shader dependencies, XR settings,
graphics/tier/editor settings, effective Android graphics APIs, color space and
stereo mode, exact Unity Editor/Android module/UnityShaderCompiler/tool libraries,
CGIncludes, and all four validator sources. Unknown PlayerSettings fields remain
included. The explicit packaging/CIL/profile exclusions do not affect the
queried native programs; unrelated mod C# and APK version changes preserve this
graphics key. Changes to any known helper bytes also invalidate it.

The production Player step creates Addressables groups after the graphics gate.
Only generated `.asset` group/schema/settings files and their metas/directory
metas under `Assets/Quest/Settings/Addressables` are excluded from the generic
settings-directory scan. Manifest and actual asset dependencies are bound
first. Shader/include/compute and unknown files in this subtree remain hashed:
the actual same-version witness showed that `AssetDatabase.GetDependencies`
alone omits a referenced HLSL include. Quest and XR graphics settings remain
bound. This exception concerns content-container outputs, not shader sources.

Reuse checks every original alias and material census and rehashes every unique
actual `.vulkan`, `.vertex.spv`, and `.fragment.spv` output. The full game retains
688 shaders, 51,564 native aliases, and 9,187 material identity checks. Source
identity, imported shader/pass checks, and all material shader associations still
run. No native compilation, SMOL-V decode, or SPIR-V reflection query is issued
on a valid hit. This proves reuse of the completed compiler result, not original
pixel parity or headset correctness.

If an owned hashed evidence file is damaged, the miss performs real native
queries. Only the freshly verified payload's exact hash-addressed evidence leaf
can be replaced. A unique temporary file is renamed into place after removing
that damaged leaf; other output files remain untouched. A fresh completed marker
is published only after all aliases, outputs and unchanged inputs pass.

## Actual bounded fixture

The fixture uses an authored shader with two Vulkan aliases and one material.
It loads no original game content. Prepare a **new empty** private project and
run both bounded methods with the pinned Android-capable Unity Editor:

```sh
python tests/quest-shaders/run_completed_cache_fixture.py \
  --unity /path/to/2021.3.5f1/Editor/Unity \
  --project /private/empty/cache-fixture
```

On 2026-10-05, the actual Unity 2021.3.5f1/OpenGL host/Android Vulkan fixture
completed with two initial native queries, zero on unchanged reuse, five bounded
real revalidations, and 26 controls. Missing/duplicate/null aliases, changed
keywords/tier/material count/stage flags, unsafe output address, corrupt outputs,
malformed/partial receipts and markers, changed inputs during completion, and
helper drift were rejected. Shader/include/meta/graphics settings changes
performed actual queries. Material identity still failed on a cache hit, while
unrelated mod/profile/APK version changes preserved it. A second repair-only
method damaged three owned native output files, then verified two real native
queries repaired them, unrelated output bytes remained intact, and subsequent
reuse made zero queries.

A follow-up real Unity method verifies that new/changed unrelated Addressables
`.asset` groups and folder metas preserve the key; changed Quest/XR files and the
actual stereo API invalidate it. A physically referenced HLSL include inside
that subtree stays in the closure and performs real revalidation after drift.
The failed broad-exclusion probe is retained as negative evidence; the final
code excludes only generated content metadata and retains the include.

Private results:

```
/home/claw/quest3-local/full-shader-validation/cache-witness-v1/unity/CacheWitnessProof/result.json
/home/claw/quest3-local/full-shader-validation/cache-witness-v1/unity/CacheWitnessProof/repair.json
/home/claw/quest3-local/full-shader-validation/cache-witness-v1/unity-cache-witness.log
/home/claw/quest3-local/full-shader-validation/cache-witness-v1/unity-cache-repair.log
/home/claw/quest3-local/full-shader-validation/cache-witness-v1/unity/CacheWitnessProof/addressables-xr.json
/home/claw/quest3-local/full-shader-validation/cache-witness-v1/unity-cache-addressables-xr-final.log
```

The actual Unity module SDK compilation also passed. Focused Python controls
pin the known migration source/verifiers and ensure the fixture never overwrites
an existing project. These bounded fixtures do not claim game/headset pixels.

## Explicit migration of the in-progress f905 full result

The currently running f905 full gate has the previous exact consumer and native
verifiers. It cannot automatically create the new completed marker. Do **not**
apply source changes while that Unity process runs, and do **not** seal a partial
receipt. First retain its actual completed `android-compiler.json`, output banks,
full PASS log, launch build provenance and the independently captured live input
snapshot. After its actual full PASS and a safe process boundary, install the
two cache-enabled Editor sources with an honest derivative Editor-source ledger.
Keep the manifest, game shader/includes/metas, settings, verifiers, toolchain and
existing Library unchanged.

Exact retained source identities:

| Role | SHA-256 |
| --- | --- |
| Previous consumer used by f905 | `d1e5de9d9c1743e25bbc891c91cefef53d527bd8a1b95918abb49e787bd9a3d3` |
| Historical preparation consumer, before earlier authorized overrides | `4e005bc601180ca8d6d9a6c338587bc556d97548030ec7723c8bf78f5ed6fc17` |
| New cache-enabled consumer | `c43e6ee16a80512f1411a8e91bd0a0e6d72fcb25205e65a8fda956bbf8ecfc95` |
| New added cache helper, no historical preparation baseline | `a37041b427c9a301abbb05960988a97cad1d18a70a18961e4807e8868d8b8a0d` |
| Unchanged Vulkan native verifier | `3e85b950523bd4a4ef9ef58140836bc047d9d27263f1fd0966347841dfbce7a7` |
| Unchanged SMOL-V decoder | `bdfff94920bb7ffdfed224cad4126f9c987cbf8d68ef8b7ae904f5a38fdbe0fc` |

Set these three explicit inputs and execute
`GloomhavenVR.Quest.Editor.QuestCampaignShaderCache.SeedLegacyCompleted` in the
retained project with the same real graphics host/Android target:

```
GHVR_QUEST_SHADER_LEGACY_SNAPSHOT=/home/claw/quest3-local/full-shader-validation/cache-witness-v1/f905-live-inputs.json
GHVR_QUEST_SHADER_LEGACY_LOG=/home/claw/quest3-local/build/logs/unity-build-f9053689b680.log
GHVR_QUEST_SHADER_LEGACY_PROVENANCE=/home/claw/quest3-local/build/builds/f9053689b6808718c66395f27c40368238e664c62079da01939985e3d1fbee52/build-provenance.json
```

Keep `GHVR_QUEST_SHADER_MANIFEST` unset or use its original project-relative
`Assets/QuestOriginalCampaign/campaign-shaders.json`; keep
`GHVR_QUEST_SHADER_OUTPUT` unset or point to the original complete evidence
directory. The sealer requires the actual full PASS, the exact old source hashes
in both launch provenance and the stable live snapshot, all 688/51,564 aliases,
the unchanged physical dependency/tool/settings closure, current pinned
cache-enabled consumer, unchanged native verifiers, imported identities and
materials, and all actual receipt/output hashes. Only after those checks does it
publish the completed marker and bind the prior snapshot/log/provenance hashes.
Unknown drift rejects migration and requires actual validation. No fabricated
completed receipt is accepted and no native alias sweep is repeated solely to
introduce this cache.

The private snapshot contains 24,781 stable file identities/1,123,767,230 bytes,
captured twice while f905 ran. Its own SHA is
`14edf214ffc1200d0a6822dd11d863f4768a344504017a7d7fa6e53bf53a18df`;
`receiptCompletionObserved=false` records that the snapshot itself made no PASS
claim. The real f905 gate subsequently passed at 15:07:50 UTC. Its private
completed evidence lives in the sibling `f905-completed` directory. The captured
receipt SHA is `32a2fdcb487e20ee35ed87e63029a51e0e4c4c1ae4ab5fac3fa33062e7182922`:
all 688 shaders/51,564 aliases/9,187 materials and all 20,930 unique native output
hashes (693,121,845 bytes) were verified. All 24,781 previous graphics files
remained identical after PASS, including raw PlayerSettings. The later generated
Addressables content metadata is covered by the narrow exclusion above. Actual
migration still waits for a safe Player process boundary; the completed native
result does not claim that the full APK or headset tests have passed.
