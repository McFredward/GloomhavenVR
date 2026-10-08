# Quest Windows native overlay retry, 2026-10-08

## Observed failure

Input: `quest-build-support-20261008T075121Z-022bc617.zip`. The diagnostic
release identifies published source `2fc28e31d5f3623ea28de6e2e5752d5ea507b5a8`,
ModBuild642. Current `wizard/build.log` confirms reuse of all16 original packages
and six closed staging phases: catalog, canonical, copy, runtime, GUID and layout.
The preceding CRLF parser repair succeeds through128223 files. Native overlay
preparation returns, then application rejects `GreyBoltDissolve_Mat_0.mat` with
an exact input/output byte mismatch. Unity import and APK export have not begun.
Rotated recovery logs describe older attempts, not this current failure.

The pointer producer used `read_text()` and hashed its newline-normalized string;
application correctly checked actual source bytes. CRLF therefore failed without
corruption. Packed-Sprite restoration had the same producer defect. Both now
read explicit UTF-8 bytes, retain newline/Unicode coordinates and write binary
UTF-8 overlays. Pointer YAML headers retain CRLF and stripped/signed offsets.
The null-managed-registry parser fallback remains bounded. Application still
checks every input/output before changing staged files; no hash check is removed.

## Reuse and measured progress

Only the complete reviewed producer pair retains the preceding recipe key and
raw export contract for these exact records:

| Helper | Published642 SHA256 / bytes | Fixed SHA256 / bytes |
|---|---|---|
| pointer_recovery.py | `bfa225a6657012c784215dc07214ea695852bdbc23d8a9e113e45b8c9ea4ebd6` / 21460 | `346f1c48627bac95a03501d7061d351589c009295e3f39907ce419758696ed41` / 23171 |
| packed_sprites.py | `a11ec97298a274886cf73e873558f695787c8a733ce930d5e683c37c019bc609` / 9806 | `add7402c47346647beed79bf07f103e0c7631e8bbdf933b9e44ee6ac52b320eb` / 10254 |

Actual immutable source qualification uses the new bytes/hashes. Both helpers
remain in the full recipe/raw contract. Unknown or mixed producers do not gain
the exception. Existing binding bytes stay unchanged;16 accepted raw packages
and six completed transforms remain eligible. Native resumes at its uncommitted
boundary. Reuse still qualifies retained bytes/owners; a large existing project
does not restart instantly.

The existing container schedule now emits actual completed/total counts without
another scan or extra hashing. The Wizard assigns preparatory reference counts
and container reads to the open native phase. Previously that reference counter
advanced to final audit and falsely marked intervening phases complete. Work
revision6 removes implicit old closures, retains explicit complete/reuse proofs
and preserves the global bar's high-water. Final audit keeps its own boundary;
counter100% cannot close native application or claim whole-build completion.

## Current mod and verification

Quest remains on `feature/quest3-standalone`. Reviewed merge
`3eb56d766fb94ca1e07ce9d31dee9c09cc5ce2df` preserves published Quest history
and current dev643 `29d4b6dd85e6ce3b3d19b531bde79d2fcc44e587`. This Python
repair needs no runtime edit. The composed renderer retains Quest camera
ownership and643 saved startup capacity; all four owned-content banks remain.

Evidence: `/home/claw/quest3-local/build/evidence/B642-support-075121/`.

- Native tests25/25 and four existing Sprite-consumer cases pass. A real original
  Material CAB is joined through owner CAB/pathID, field and dependency
  CAB/pathID to the exported target. Actual prepare/application passes LF,
  CRLF and mixed endings. The old producer reproduces the exact failure for
  CRLF/mixed input. Every non-pointer byte and original CAB stays unchanged.
- Integrated focused gate245/245 in14 affected parser, producer, reuse, staging,
  release and Wizard modules. Real Builder/raw selection/Journal cases preserve
  16 receipts, six phases and older binding bytes; genuine output/owner corruption
  is refused. Progress tests preserve later pending phases, parent completion,
  current retry rows and migrated saved percentage.
- Fresh dev643 composition: strict Release/Debug0 warnings/errors, source16/16,
  renderer124 assertions/25 controls plus two startup controls. Platform3081/17,
  menu26/8, native dev643 and bank evidence is inherited only by exact declared
  inputs. This is not a new complete local/CI/wire or native OpenXR/headset pass.
- Publication requires actual extracted-ZIP discovery, preserved-session startup
  on two ports, offline artwork/branding delivery and exact actual-source/old
  staging identity checks. Its manifest carries ModBuild643 and committed source.

Unpack the corrected Builder over the existing folder, keep the selected work
folder and continue the same session. No Unity import, exhaustive shader audit,
host APK, whole Windows conversion or headset run is claimed. Public packaging
excludes game/decompiled code, DLLs, original-derived geometry, APKs, credentials
and local build/state caches. Cleanup removes only this repair's completed
worker worktrees and duplicate private archives; supplied evidence and other
agents' work remain intact.
