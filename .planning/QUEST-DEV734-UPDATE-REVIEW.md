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
