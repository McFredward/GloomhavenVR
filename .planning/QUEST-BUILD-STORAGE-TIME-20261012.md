# Quest Builder storage and time review — 2026-10-12

The maintainer deleted the Windows workspace after observing almost 400 GB and
reports cold builds taking at least seven hours. The next run is intentionally a
fresh build. This review covers fresh creation, resume, mod/profile updates,
portable import/export and installation. It records source-proven changes, not a
new Windows end-to-end benchmark or a guaranteed disk-space ceiling.

## Evidence and causes

Capture `quest-build-support-20261012T114856Z-4f46c8ca.zip` records 6,531.928
seconds (about 109 minutes) for the raw original export alone, followed by copies
of 189,701 recovered and 191,488 prepared files. It also shows an unnecessary
full migration for reviewed audit/observer changes. The later workspace evidence
contains multiple game/cache owners; one prior game-inventory change was an
unconsumed desktop Unity OpenXR subsystem manifest. There is no complete physical
400 GB inventory from the deleted workspace, so attributing all 400 GB to one
cache or assuming every logical reference was another physical copy is unwarranted.

Source inspection identified additional costs:

- All sixteen exporter groups ran sequentially. Their temporary export trees,
  private serialized inputs and repeated core recipe YAML remained until the
  end of recovery. Each batch's evidence included another large core recipe tree.
- Normal Unity builds performed seventeen pre-build tasks and six post-bank
  tasks. Nine pre-build and five post-bank tasks were optional diagnostic QA,
  including opening every original scene, loading whole object closures and
  reading back texture mips. A second membership census revisited 51,564 shader
  aliases already constructed by the mandatory retention path.
- Host delivery parsed the whole native bank again for optional shader and
  compute diagnostics. Raw and staged recovery also repeated whole GUID graphs.
- Snapshot copies reread their destinations for SHA. Content/source proofs did
  not consistently survive compatible receipt-owner changes. Native packing
  inflated and recompressed retained ZIP entries and reread all entries on reuse.
- Plugin and content searches excluded StreamingAssets/Quest files only after
  recursively entering their large Proton/Wine runtime trees.

## Implemented policy

Normal builds retain actual conversion, required script-pointer mutations,
required scene/plugin/package/native ABI contracts, native compilation, owned PC
input/DLC admission and APK signature checks. Optional graph/scene/image/whole-bank
readback is omitted. Skipped audit receipts explicitly say unevaluated; they do
not fabricate zero missing references, pixel parity or verified headset outcomes.
Developers can select `--full-validation` for full file/import/graph/delivered-bank
QA. The exhaustive original shader compiler audit remains independently selectable
with `--validate-campaign-shaders` and is off by default.

| Operation | Required retained work | Changes to avoid extra time or storage |
| --- | --- | --- |
| Fresh build | One frozen PC input scope; raw recovery; derived Android assets; one mutable Unity project/Library per compatible game/compiler scope; compiled APK and content bank. | RAM/CPU-bounded isolated exporter processes; deterministic parent merge; per-process CPU quotas; remove each committed batch's Export/Input and duplicate recipe YAML immediately. Remove private core staging/evidence after recovery closes. Minimum Unity task plan, no broad normal QA or duplicate alias inspection. |
| Resume | Exact control/native identity/index records and completed journals; required actual input consumption. | Reuse strong closed-byte proofs by file ID/change time, including compatible receipt namespaces; bounded raw checkpoint/export samples instead of full optional readback. Unknown/changed proof metadata falls back to real reads. Earlier committed batches survive cancellation/allocation retries. |
| Compatible mod update | Current Builder mod/Quest code and changed authored art; matching owned PC copy, script/package/signing contracts. | Keep original banks; use the existing small code compiler/Library. Reviewed scheduling/audit/archive-wrapper changes retain compatible original scopes. Unknown actual producers/native/package changes still request only the necessary cached build. |
| Profile edit | Owned PC copy/DLC admission; changed profile resources; APK signing. | No Unity/game conversion and no original-bank copy. Reuse an existing bank locator and matching closed-byte proof. |
| Portable ZIP import | Selected package manifest; APK bytes/signature; bank locator. | Import the APK only; read the bank through its existing nested locator rather than extract another whole bank. Closed import witnesses remain reusable. |
| Export | One actual portable transfer of APK and referenced bank. | Outer ZIP_STORED, hashing while copying, no Unity, bank decompression or recompression. Export is a requested deliverable and is not auto-deleted as a cache. |
| Installation | APK installation and missing/changed content transfer; device connection, compact saves when required by a signing-key replacement. | Retain the existing installer receipt/chunk reuse, USB-first connection recovery and targeted save policy; do not reintroduce a full game-tree backup or raw/build preparation. |

Initial file-backed packaging now hashes during transfer, uses fast Deflate for
plain data and STORED for already compressed media, and publishes an atomic
replacement. Native repacking copies qualified compressed ZIP records, regenerates
local/central ZIP64 headers and checks source identity before publication. It does
not inflate retained entries on the ordinary path. Full QA keeps legacy full-entry
readback. Windows transaction-preimage hardlinks use a read-only directory
FileID/ChangeTime observation without relaxing persistent hardlink trust.

Private exporter input hardlinks originate only from a private immutable core
copy, never from the player's installation, frozen PC snapshot, mutable Unity
asset or another worktree. Filesystems without hardlinks use bounded temporary
copies. Parallelism never shares a mutable Unity Library between Editors.
Resource pressure reduces concurrency and retries the unfinished exporter;
successful sibling outputs are retained. Arbitrary SIGKILL is not called OOM
without allocation diagnostics or an actual kernel OOM witness.

## Remaining space and unavoidable work

The snapshot, raw recovered project, prepared Android project and Unity Library
are different useful representations. Keeping them permits targeted repairs and
fast later builds; deleting them after successful export trades that capability
for space. Native delivery still retains a finished bank independently of the
mutable project. A user-requested portable ZIP is another physical deliverable.
The storage UI continues to distinguish shared caches, individual runs and final
outputs. Its explicit cleanup can remove intermediate work while preserving APKs,
exports, logs, signing keys and saves. No live run or another agent's cache is
swept automatically.

A fresh workspace must still provision tools/packages, recover real original
assets, import Android assets, build native Addressables, compile IL2CPP and
package/sign Android. CPU concurrency is bounded by actual available RAM/commit
and the size of each job; using every core without those limits can cause paging
or allocation failure. Unity performs the actual native import/compilation; a
whole shader-variant audit is not substituted for that required compiler work.
The independent original cache/import keys remain separate from profile/mod
identity. This release does not globally ignore unknown PC-file changes merely
to force two potentially different game installations to share a cache.

## Validation and measurement scope

Evidence is retained under
`/home/claw/quest3-local/build/evidence/B672-build-cost-400gb-20261012/`.

- Storage fixture: three repeated 64 MiB content qualifications read
  192 MiB before versus zero payload bytes after proof reuse, approximately
  0.133 s versus 0.0026 s on this host. Fresh-copy elapsed times varied; the proven
  gain is removing the extra destination hash read, not a fixed speed percentage.
- Packer fixture: a 32 MiB retained Deflate member plus an 8 MiB native bank took
  approximately 0.569 s with full readback/recompression versus 0.085 s with raw
  record reuse. Decoded output asset hashes match. This is a Linux small fixture,
  not a Windows full-game duration or native bundle size guarantee.
- Parallel fixture: three controlled batches took approximately 0.277 s serial
  versus 0.126 s parallel. Finish order differs while canonical publication order
  and output hashes remain identical. This validates overlap and deterministic
  merge, not a promised seven-hour reduction factor.
- Focused suites cover cancellation, allocation-pressure retry, committed sibling
  reuse, unchanged original input stamps, changed-byte rejection, ZIP descriptors,
  ZIP64 headers/end records, atomic interruptions, full/minimum policies, actual
  Unity SDK/Roslyn compilation, exact catalog alias/label reuse, update admission
  and package/source profiles. Strict composed mod Release compilation passes
  with zero warnings/errors. Final release qualification records the precise
  passing/inherited scope; it is not a new whole-game/headset gate.

The next Windows build should export a support package after completion or a
problem. Its actual per-phase timings/resource evidence will establish the new
bottleneck and peak storage. No new local game/APK build was run for this review.
