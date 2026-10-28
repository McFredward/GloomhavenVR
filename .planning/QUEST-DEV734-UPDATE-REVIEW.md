# Quest integration with finalized dev Build734: update audit

Date: 2026-10-28. Scope: host Builder/Wizard update classification, source
distribution, portable original compiler seed, device-source and resume retention.

The integration is class C under `docs/TEST-STRATEGY.md`: the integrator owns
the complete final-union gate once. This lane performs focused host checks only;
the initial audit below is read-only and has not run a test or Android build.
No local ZIP, APK, Unity conversion or game-bank operation is authorized here.

## Immutable inputs

- Finalized dev: `fdbd372e30ed3ab49fb3e3e69af267f181edb800` (Build734).
- Previous Quest: `595791b7825c28b87069db8b2037386b755197ef`.
- Common base: `4f10ddd60524911bf4cf39777df46f95d7691d83`.
- Worker starts at finalized dev in
  `/home/claw/worktrees/quest734-update-audit`.
- Candidate Quest integration: `8f3921adc`, rebased on finalized dev.

Read AGENTS.md, CLAUDE.md, STATE.md, current NetProtocol Build734/733/732
notes and the binding testing strategy before review. The source confirms that
Build734 changes the menu rendering/resource contract while preserving the
published city/quest authority fixes. Headset appearance remains unverified.

## Initial findings

### Required: preserve portable-seed plan identity during native resume

Previous Quest `update_driver.inspect()` intentionally excludes
`baseApk`, `sourcePackage` and `compilerGameRoot` from `updateKey` so relocating
the selected APK and extracted original Mono compiler references does not change
the immutable update identity. `native_update_resume._plan()` reconstructs that
identity while excluding only `updateKey`, `baseApk` and `sourcePackage`.
An installed-game code update with its required `compilerGameRoot` therefore
fails `current_plan()` before compilation with "Retained compact Player plan
changed". Existing installed-update tests exercise unchanged-code publication
and seed extraction, but do not reach this actual native plan admission.

Minimal repair: reconstruct the same identity exclusions and add a causal test
covering an actual installed-game plan, local seed-path relocation and rejection
of changed immutable inputs. Retain all exact native-pair, source, package ABI,
signature and seed qualification. This is a host repair, not authorization to
reuse changed managed code or to rebuild original content.

### Required: close the new menu resource distribution/delivery contract

dev adds `Assets/MenuDepth/Rendering/menu-depth.bundle` as a mandatory embedded
resource in `GloomhavenVR.csproj`. The previous Quest release selector excludes
all `.bundle` files except its finite `AUTHORED_RESOURCES` roster; the new menu
bundle is absent from that roster. Consequently an otherwise accepted new
source Builder would omit a required compiler input.

The current bundle's provenance declares StandaloneWindows64 and a D3D11-only
native-card program. The Android resource lane must establish an appropriate
Quest producer and exact delivery contract before the host roster is changed.
Do not simply whitelist the desktop bundle or claim Android rendering from its
desktop provenance. Coordinate ownership with that lane.

Separate story `Content` also requires explicit delivery: dev adds 515 GMD
fields plus a manifest, 159,023,660 bytes total. The previous Quest source-release
selector admits these source files, but its compact update writer has no
MenuDepth content replacement family. IL2CPP compilation alone cannot publish
separate copied desktop Content. Preserve exact supplied numeric fields and
their original-source bindings; no inference/conversion is justified merely by
the rebase. Rendering source/provenance/bundle adds eight files, 106,809 bytes.

### Preserve: compact update qualification and independent content ownership

Previous Quest already selects the current bundled mod source instead of a PC
installation's mod DLL. It retains the immutable original INPUT and records the
actual current source in UPDATE. Host-only Wizard/release metadata is excluded
from compiler identity through narrow reviewed rows; unknown producer changes
still request a concrete migration decision instead of silently reconverting.

Original Campaign assets, authored mod/environment banks, figure banks, combat
audio and movie banks have independent source/recipe identities. Code updates
keep a stable game-input/package-ABI compiler project and matched native code /
metadata pair. Unchanged banks and separate movie files must remain reusable.
Do not replace these boundaries with a full-source-key invalidation or blanket
historical alias while integrating menu resources.

The installed-Quest path qualifies a bounded, exact original Mono seed from its
signed APK and preserves the installed profile/DLC roster. It never treats
IL2CPP metadata as original IL. Missing old seeds request one PC-assisted update;
profile-only edits do not need seed extraction. Device save/signing handling is
outside this lane; source evidence does not establish Android/headset execution.

## First bounded implementation and validation

After the integrator supplied the rebased candidate, repair native plan admission
with the identical `compilerGameRoot` exclusion. The new fixture invokes actual
seed extraction, `inspect()` and `current_plan()` together. It reproduces the
unmodified production failure, then proves admission, local seed relocation and
unchanged bytes; an immutable profile edit still fails. Installed update, native
resume and original seed modules pass 53 tests in 39.901 seconds.

The source-release roster now includes the separate Android producer/editor and
the exact public rendering recipes. The desktop DXBC-derived bundle remains
excluded. A bounded manifest closure qualifies every supplied GMD field against
already measured source rows, with no inference or second depth read. Source
release/overlay/support tests pass 77 tests in 2.835 seconds, including omission,
changed numeric hash, duplicate/path controls and an actual tiny archive proving
that the native-derived desktop bank is excluded and its public producer remains.

Both standalone test processes were reaped before `cleanup_completed_run` was
called. Fixture temporary directories remove themselves; the common cleanup
returns no generated project caches. Logs/results remain under ignored
`/home/claw/quest3-local/build/evidence/quest-dev734-rebase-20261028/update/`,
including the failed causal pre-repair run. No Unity, APK delivery, source ZIP
deliverable or original Campaign conversion has run.

These are focused host proofs, not a complete-gate or headset result. The
integrator owns orchestration and the final union gate; the resource worker owns
the new Android shader producer. Independent loose Story delivery into the
existing mod-content archive is the next explicitly assigned bounded scope.

## Independent loose Story delivery

`menu_depth_content.augment()` accepts the already qualified mod archive,
content inventory and native-bank receipt, then returns a closed archive and
matching content/bundle metadata. It copies existing compressed bank records
directly, preserving their exact payloads, order, source records and dependency
contract. Only the finite supplied Story manifest/GMD fields are appended.
The current numeric roster, exact retained archive/metadata and actual writer
sources identify its independent cache; unchanged retries do not rewrite
Story or decompress banks. A damaged owned delivery cache repairs just that
archive, and interruption preserves original inputs and previous outputs.

The bundle metadata adds `menuDepthStory` with schema, complete file records
and a content key. Its existing bundle/source records remain unchanged.
The full and compact orchestration callers belong to the integrator.

`installation_manifest` preserves legacy three-bank and current four-bank
admission. Its only new path scope is a manifest-first, lexically ordered
`StreamingAssets/MenuDepth/Story/` roster of exact 32-hex GMD filenames. The
actual nested ZIP, per-entry bytes and original-art manifest must close that
same set. Extra paths, missing fields, modified depth bytes and reordered
native banks remain rejected; game-bank validation remains metadata-only.

The final focused run passes 108 tests in 7.362 seconds: Story producer and
archive consumers, signed installation, source release/overlay and support.
It includes actual compressed-record comparisons, a complete tiny signed
installation-consumer proof, unchanged cache reuse without ZIP decompression,
targeted damage repair, interrupted transfer and invalid-content controls.
Resource changes from `5ee2bca24` and `b195b474c` are local test dependencies;
the resource lane owns their source and independent Android/Vulkan proof.
The earlier 97-test intermediate pass is retained separately. Every run reaps
its process and invokes common cleanup; no regenerable Unity project exists.

## Actual previous Builder source comparison

The retained B717 production audit identifies source
`6f5c94aceead0031720ac35868dcfa2c391aafe6` and its exact 3,648-file
`source-release-manifest.json` (SHA256
`6b5470867b6d78f4c5db05ce4e15afde3fe9c4eae2d48988f269a06f8dc3bbfd`).
Compare that source to lane commit `3fc15a0b6`: current selection contains
4,221 files / 2,254,410,079 bytes. Reuse 3,537 previously measured rows only
with the original report's source-commit binding and identical immutable Git
objects; freshly measure 684 changed rows / 173,256,206 bytes. Do not rescan
unchanged multi-GB movie recipes just to compare their source manifest.

Execute the actual production requirements planner against these source rows
and the same controlled original-file metadata used by the retained B717
producer audit. Authored art, native helpers, figure, audio and movie producers
are unchanged; all corresponding changed flags are false. No separate movie
pixel input changed. The original Builder producer AST remains exactly
`6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1`.
Managed code changes and the new shader/depth delivery remain independently
required. This establishes source/recipe compatibility; the original metadata
is controlled audit input, not a new claim about a selected device APK.

On this intermediate lane commit the only remaining full-build reason is the
delivery-only `installation_manifest.py` validator change. The integrator owns
its precise classification along with the newly assigned orchestration; this
intermediate result does not claim the final integrated route is fixed.
The read-only audit completes in 1.469 seconds and retains its source rows,
requirements and cleanup result under the lane evidence directory. A preceding
audit-harness attempt lacked installer-captured bank modules and failed before
planning; its correction uses the existing production driver helper loaders.
That failed instrumentation attempt remains separate from the successful audit.
