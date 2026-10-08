# Frame642 steady-render CPU integration

Build642 integrates the twelve reviewed CPU checkpoints from
`3ec50414fa5935141b47e3d0e583e8ddcef3809a` onto the verified published crossplay641
`a791faa97444e976d4a94149096af774c50d0c52`.

Twenty-one owned source/test/document files are byte-identical to the checked
candidate. The older native-state623 test-only checkpoint is excluded: upstream
639/641 already retains its corrected baseline and dictionary mutation bindings.
No worker branch is pushed. Exact hashes and source reuse live under
`.planning/debug/frame642/source-reuse.json`.

## Reviewed behavior

The existing shared-read setting gates current-pass terrain/material/prop and
wall-query sharing. Terrain releases previous leases before stopping at its
existing admission cap; the unexamined remainder retains native output. Exact
registered, locally held and remotely held prop membership is captured once per
synchronous pass. Shader metadata is invalidated at native writes and nested
camera boundaries; per-renderer and per-slot property blocks remain current.
Prepared terrain ownership is separate from actual live substitute leases.
Refused live/queued consumers are revoked without repeatedly invalidating
unchanged prepared-only sources. Walls retain election, native fades and report
cadence while avoiding unused home maps and repeated membership/bounds sampling.

These changes are general PC/standalone optimizations, not a Frame-only binary.
Existing settings permit an independent-read comparison. There is no new room
limit, quality/default change, mesh bank, wire layout or asset bundle. NPC639
cards, native originals, receipt clock and full-render proof, Frame640 camera
policy and crossplay641 input/continuation remain outside the five changed CPU
runtime types.

## Evidence and limits

Actual Unity worker world623 assertions/four affected controls, terrain352/one
control, source-bound bridge20 contracts/13controls and broader world46/terrain64
variants are inherited through exact source hashes. Walls use explicit boundary
surrogates; their checks cannot prove native pixels. The worker's original red
154-suite record and separate bounded repairs remain honestly retained. Upstream
641 executed all170 local suites, with167 initial passes and three successful
focused fixture repairs; it is not a fresh green single-run170 gate for642.

Integration source16/16, strict Debug/Release with zero warnings/errors, five
bilingual-doc pairs and current wire golden299713 assertions pass. Actual
compiler/decompiler comparison retains all1237 types, with1221 byte-identical:
only five intended CPU runtime types, two description tables, eight numeric build
consumers and one generated branch field change. No unrelated changes exist. The
first decompiler invocation lacked DOTNET_ROOT and failed before producing a
snapshot; that tool failure is retained separately from the successful retry.
Only the affected comparison was retried and the remaining golden tail executed;
passed builds and source
checks were not repeated as a full gate.

The final scope/count receipts are in `.planning/debug/frame642/integration-ledger.json`.
See [the original CPU evidence](../../.planning/FRAME-642-STEADY-CPU.md) and its
verified compact proof archive under `.planning/debug/frame638-steady-followup`.

No new Frame hardware run or measured post-fix FPS improvement is claimed.
The selected prior five-room terrain/world/wall26.070ms/frame is an optimization
target, not a guaranteed saving; nested timers cannot be added again. Both VR
peers install642. Compare fully loaded views with shared reads on/off and inspect
complete frame/mod timing, native fades, both eyes, multiple rooms and held props.
