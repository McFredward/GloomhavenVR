# Actual Editor ownership after capture232406

Quest remains on `feature/quest3-standalone`. This repair changes retained-output
qualification; it does not rebuild an APK locally or restart original conversion.

## Failure and corrected evidence

`quest-build-support-20261009T232406Z-88abfcf5.zip` identifies source
`3f25227a32604e9ee606c48795b63d0dadae3e6b`, Runtime660. It fails during preparation
on the same `Assets/Shader/Hidden_BlendForBloom.shader`, before another Unity
launch. All27 owners are closed, no conversion is pending, and the stable project
is `24f29369a96c0f2d6c9947b8abb5a464e563c9963753cfcd737175c04d6bc1df`.

The previous portable fixtures supplied Shader-meta owners that the actual
producer never records. They also kept SDK-referenced prefab ownership at
`case-paths`, although the later script-order receipt owns those original files.
Those assumptions made the last repair's positive fixture insufficient. The
earlier focused test results remain historical receipts, not evidence that the
Windows ownership graph qualified.

The actual production `manifest_contracts` resolver and retained source receipts
establish the following ownership:

| Output | Final contract and subsequent writer |
|---|---|
| Three audited Bloom Shaders | `case-paths`; Unity imports their exact existing upgrade pins |
| Their three `.meta` files | No individual output row; complete original hashes are frozen in the `post-effects` receipt |
| Serialized original script references | `script-orders`; the final Editor maps original SDK pointers to package scripts |
| `ProjectSettings/ProjectSettings.asset` | `final-settings`; the later Android SDK/Player configuration authoritatively updates build settings |

The private existing project resolves4 Post-Effects outputs,29565 case-path
outputs and35336 script-order outputs. Export-specific counts may differ from
Windows; the producer structure is the proof. Its35258 serialized originals have
356 changed file sizes, all Prefabs, assets or scenes. This metadata census reads
no asset payloads and does not prove same-size files unchanged. All75 original
DLL metas, the other three small retained UI/Blur Shaders and all six Shader metas
match their original source receipts. No additional immutable-output drift was
observed within this bounded independent audit.

## Qualification and build-settings handoff

The Shader reader binds an unowned meta directly to the unchanged source receipt
and its complete `_entry` identity, including GUID and full meta hash. If a meta
has an independent contract, that original row must qualify too. Actual Shader
bytes still require the existing complete Unity-upgrade pins.

Package-script changes can qualify under the actual `script-orders` owner or the
earlier supported owner. The frozen binding manifest and actual package script
identities remain mandatory. Reversing only the exact `m_Script` mapping must
reproduce the retained original file's full size and SHA-256; callbacks and all
other original serialized bytes cannot change through this exception.

The single PlayerSettings file is build configuration owned by the later Editor,
not original gameplay content. Its unchanged bootstrap receipt must qualify under
`final-settings`; the completed27-owner game frontier and exact Android contract
remain mandatory. Current settings must remain bounded ordinary Unity129 YAML
with the required Linear/Vulkan bootstrap fields. Other build fields are set by
the current `QuestBuild.ConfigureAndroid`, including mobile backend, architecture,
GC, input, version and signing. A durable journal marker binds repeated accepted
rows to the same original receipt. No other settings file or original serialized
asset receives this mutable treatment.

Accepted rows retain durable strong file witnesses before journal publication.
Unknown source/receipt/meta/mobile-field changes remain actionable failures, with
the rejected gate and original owner in the support log. No game assets, GUIDs,
original archives or Unity Library are rewritten by these readers. Warm retained
files bypass the readers and their controls. Exact complete source profiles keep
the preceding3f, d58 and first-import8063 producers compatible with the repair.

## Acceptance boundary

Root validation passes177 focused preparation/source/Editor cases, three isolated
Wizard-loader cases and ten source-release cases:190 unique Python cases. The
Editor helper cases include actual pinned SDK compilation and repeated/cut C#
writer execution. Current production source rows match their exact published
compatibility profile; unknown reader changes still invalidate that profile.

The additional private combined proof uses all three real original/imported
Shader pairs, original metas and complete restoration receipt,30 actual package
bindings, two independently retained original/imported Prefabs, and the real
17861-byte original/19907-byte imported PlayerSettings. Its actual owners reproduce
the previous shipped reader's same Shader failure; fixing only that reader then
exposes the old SDK owner guard. The corrected reader passes clean adoption,
cuts before/after journal publication, warm no-payload reads and unchanged Library
and provenance bytes/times. Foreign YAML and concurrent mutation remain rejected.
The portable combined fixture also covers real input-key/metadata migration,
including retention of the settings marker and the transferred byte witnesses.

No runtime C# or asset producer changes in this repair. Published dev660's
composed202-scope/source16/golden300297 evidence and the preceding current660
Release/AOT23/static-weave290 checks remain inherited. No new complete local
wire gate, whole Shader matrix, cold import or whole APK build is claimed.

The Windows export omits Shader, meta, settings and full journal payloads. Local
native sources and independent before/after originals supply additional evidence;
they do not establish whole Windows APK completion or a headset picture. Continue
the existing Wizard session/workspace after replacing the Builder sources.

Private evidence: `B660-resume-232406-20261010` under the Quest build evidence root.

## Published dev661 delivery follow-up

After the bounded Builder repair was delivered, the NPC integration owner
published `1b6e1e95f2efd1777603a553e18880f540fb3dc6` and released its main/dev
reservation. Quest merges that completed commit without taking unpublished worker
changes. Both sides' state notes and suite inventories are retained:232 local
scopes and113 CI scopes. Inventory verification passes16 tests/two expected
skips. The current661 Release builds with zero warnings/errors, current-mod AOT
passes23 assertions, and actual static integration writes290 targets completely.

The Builder's retained-output sources are byte-identical to the repair, so its190
focused cases and native ownership/cut controls are reused. Published NPC661's
composed206, source16 and golden300419 receipts are inherited rather than rerun.
Actual prior release inventories include the immediately delivered repair,3f,
d58 and the first-import8063 source; original preparation scope stays separate
from the newly compiled current mod. Additional delivery evidence lives in
`B661-dev-integration-20261010`. No new complete232-scope gate, Shader matrix,
Unity cold import, whole APK or headset outcome is claimed.
