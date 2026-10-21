# Campaign shader work in local builds

Normal `game` builds use `minimum` shader validation, including the first cold
build. `QuestBuild.PrepareCampaignShaders()` preserves every original manifest
alias through the Resources `ShaderVariantCollection`, verifies native instruction
include and shader source hashes, checks imported GUID/name/pass identities, and
checks all original material-to-shader references. Unity still performs its
required Android bundle and player compilation. Other Campaign asset, texture,
sprite, compute, audio and presentation gates remain in the build.

The complete alias-by-alias `CompileVariant` / native stage decoding and reflection
gate runs only with `GHVR_QUEST_VALIDATE_CAMPAIGN_SHADERS=1`. The companion Builder
CLI exposes this as `--validate-campaign-shaders`; it clears inherited values and
sets `1` only for that explicit option. An unset value, an empty value or `0` means
`minimum`; other values fail. Exhaustive mode uses the separately verified completed
graphics cache when its exact closure and all native outputs match.

`QuestCampaignShaderEvidence/build-mode.json` records the selected mode, manifest
SHA-256 and actual shader/alias/material counts. Both modes record successful
retention and imported identity checks. Only a successful exhaustive invocation
sets `exhaustiveCompilerValidationCompleted`, records `exhaustiveReceiptSha256`,
and reports actual cache reuse and native query count. Minimum mode records these
as false/null/zero even if an earlier native receipt or helper counters exist. It
leaves an older native receipt intact as separate evidence. The APK build receipt
also records mode, exhaustive completion/reuse and invocation query count. Neither
receipt asserts original pixel parity or a headset result.

Run the bounded dispatch controls with:

```sh
python3 -m unittest discover -s tests/quest-shaders -p test_build_modes.py
```

The controls compile the actual production method bodies and execute 48 checks
with spies for the existing shader gates. They cover default/full dispatch,
stale counters/receipts, failures, invalid flags/provenance and manifest drift.
They establish dispatch and receipt behavior, not native shader correctness.
The complete `QuestBuild.cs` also compiled against the real Unity 2021.3.5f1
Editor/Android module and retained package/mod assembly metadata in the private
`build-mode-sdk-v1` fixture (zero errors; existing JsonUtility DTO warnings).
No Unity launch, import or full native sweep was needed for this mode change.
