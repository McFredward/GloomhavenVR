# Quest integration of finalized dev734

## Scope and ancestry

The maintainer requested this rebase after both concurrent dev integrators had
finished. Their explicit ready notifications preceded integration. The frozen
upstream is `fdbd372e30ed3ab49fb3e3e69af267f181edb800` (Build734, 1.2.2), including
city/quest Build733 `952e8cfc0d1126a9378262055c2ad29ffbf9133d` and the final
test-policy commit `40e1a83f1311593f92d379ba108408b3e22c992d`.

Quest remains on `feature/quest3-standalone`, as repeatedly requested by the
maintainer. The previous published tip is `595791b7825c28b87069db8b2037386b755197ef`
(Build717), with shared base `4f10ddd60524911bf4cf39777df46f95d7691d83`.
Its exact tree was consolidated and rebased onto finalized dev. An ancestry-only
merge of the old public feature tip permits a normal fast-forward push without
rewriting published history. The new Quest build stamp is **735**; no wire schema
or semantic version bump is introduced by this integration.

This is test class **C**: a broad platform rebase with new capture/menu lifecycle
and compiler/resource consumers. Run the complete local gate once on the union;
bounded subsequent repairs use their affected scopes and preserve failed runs.
The Windows installation workflow builds the Builder archive; no local public ZIP
or complete APK is produced for this task (maintainer's 2026-10-27 ruling).

## Necessary Android integrations

* Preserve dev's menu/window coordinator, original lifecycle observers, shared
  city/quest state, character rotation, text/figure quality and tooltip geometry.
  The two native character-field accessors retain their validated, read-only
  field contracts on Android without constructing `DynamicMethod`.
* The static Quest weaver qualifies the complete lifecycle selector/descriptor
  tables and native targets, preserves the selector's enumeration, and removes
  only the proved unreachable desktop emitter blocks. Unknown IL remains a gate.
* The desktop menu bank contains native D3D11 card bytecode. A private, separately
  keyed Android/Vulkan producer rebuilds only four small menu shaders from the
  exact owned native card shader and authored programs. The public Builder
  contains their source/provenance, never the original-derived desktop bank.
  Closed local outputs or matching embedded APK resources serve later updates.
* The PC's 515 existing numeric Story GMD fields and their manifest are loose
  Content. Deliver them below the configured Quest mod-resource root and change
  only the converted catalogue getter. No artwork or depth model is regenerated.
  Both installation and the actual compiled Quest bootstrap accept this bounded
  suffix after the unchanged Android mod-bank prefix.

## Minimum-work update and resume

The original Campaign conversion, environment/art producers and separately
qualified figures/audio/movies retain their existing recipes. Loose Story fields
are not native compiler input. A Story-only change is a data update, while actual
new C#/shader/weaver inputs still require a matched IL2CPP code/metadata pair.

Existing APK updates obtain the two native Resources contracts, retain only the
old mod archive, copy its compressed bank records unchanged, and append the
supplied fields through an independent closed cache. They never open/reconvert
the original Campaign bank for this addition. Source snapshots contain the tiny
shader-tool closure needed for a cold menu-bank cache, not duplicate Story/media.

Full preparation retains its exact completed old prefix and appends one late
`menu-story-content` owner. A changed completed tail is refreshed transactionally
with durable undo; earlier owners are never truncated. Finite reviewed AST/byte
profiles admit only these known late changes. Unknown original producers still
invalidate their scope. Installed-game resume hashes also exclude the transient
local compiler-reference path consistently with the producer.

Current menu resource/content caches participate in workspace cleanup. Proven
superseded payloads can be pruned; incomplete dependency metadata conservatively
preserves the current fast-update basis. Secrets, save data and another agent's
active inputs are outside this cleanup.

## Evidence and acceptance

Ignored evidence root:
`/home/claw/quest3-local/build/evidence/quest-dev734-rebase-20261028/`.
It retains source/commit bindings, failed and corrected checks, actual native
outputs, controls, and per-run common cleanup reports. The compiled baseline is
a private copy of finalized dev734's actual reviewed DLL/decompile; the shared
baseline is untouched. Its public-surface snapshot was generated read-only from
the same clean upstream commit.

Relevant lane reviews:

* [Shared rendering and lifecycle parity](QUEST-DEV734-SHARED-PARITY.md)
* [Android menu resources](QUEST734-MENU-RESOURCES.md)
* [Update ownership and minimum-work audit](QUEST-DEV734-UPDATE-REVIEW.md)

The Android shader bank was actually compiled and decoded as target Android13 /
Vulkan18. The final managed assembly embeds that bank and provenance byte for
byte; its actual static audit/weave completes without open gates. The real
compiled startup consumer has positive legacy/Story cases and causal rejection
controls. Desktop head-pixel and motion evidence applies to the tested desktop
renderer/coordinator; it does not establish final Quest headset pixels.

Final integration checks and storage cleanup are recorded below after completion.
Hardware acceptance remains pending the maintainer's Windows-built APK test.
