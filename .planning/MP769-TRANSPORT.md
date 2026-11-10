# Build 769 Results full-picture starvation

Base: `dev` `d18f3cb7c`, ModBuild 768, initialized worker
`/tmp/gvr-mp769-transport`. Root authorized this production scope after the exact
native-sampler counterexample: only `ExtrasSendQueue.EnqueueResults` may change.
The root-owned Results driver and `ScenarioResultsProgress` helper are borrowed
validation inputs and are not committed by this lane.

## Source-proven failure and change

The existing Results queue aborted its in-flight full original whenever a newer
full original arrived, including one from the same still-open window. A full
original is necessary whenever property membership changes. Actual
`TownServiceBinding.Read(..., includeInactiveGraphics: true)` includes the
Material property only while its native `Graphic.enabled` is true. A native
Graphic enable transition can therefore require a full original even when the
part shape, node census and module structure are unchanged.

The picture lane exported two frozen, unmodified outputs of actual
`ScenarioResultsPresentation.Capture` and `ScenarioResultsCodec.Write`: 12,796
and 12,785 bytes, key 221, author 1, identical part shape, one changed node key
set, with the original Viewport Image enabled then disabled. Their exact source
transition report and copied bytes are retained with this lane's evidence.
Both need 16 actual transport pages and are delta-incompatible.

Alternating these real samples at the actual publisher's capture cadence
reproduces the reported symptom class through exact production publisher,
queue, scheduler, compression, socket emission, receiver dispatch, assembler and
Results ingress. Over 793 bounded events and 40.7 simulated seconds, the old
code completes **only the initial small heading**, never a Stats original. There
is no injected packet loss, malformed payload, codec change or rendering error.
This proves a production-reachable starvation condition. The supplied logs do
not record the changing Graphic/property membership, so they do not independently
prove that exact toggle sequence occurred on the user's headset.

The bounded repair finishes an already active visible full original of the same
author/opening. Waiting full originals still coalesce to one latest full, plus
at most its named latest cumulative delta. A different author/key, a native
close, or obsolete sparse/pose output retains immediate supersession. No new
protocol, bytes, event budget, send cadence, native authority or queue is added.
Under the same real samples the repaired code completes **45 originals**, with
the first complete Stats at step 25, approximately **1.275 simulated seconds**.
The rendering lanes own final native-window and head-pixel acceptance.

The source capture cadence and every in-progress enable/property transition stay
live. The reproduction alternates them continuously and never waits for a
settled final result before sending. Native controllers, opening animations and
sound playback are untouched by this queue-only change.

## Earlier hypothesis and native transport audit

The simpler suggested loss of an admitted, otherwise stable Stats baseline was
not reproduced. The first source audit found that enqueue admission itself was
already protected: cumulative deltas retain their named pending full baseline,
`_heldPage` is delivered first, and native admission failures retry an exact
Results page before later output. Stable topology plus changing values and
heartbeats converges even with all public lanes occupied.

`SendToken` recognizes Results in standalone, batched and outer-compressed pages
and requests ReliableOrdered delivery. Read-only native
`decompiled/GH.Runtime/FFSNet/Synchronizer.cs` confirms that
`canBeUnreliable=false` selects `ReliabilityModes.ReliableOrdered`.
`CustomDataToken` retains the supplied bytes with its extra compression disabled.
The native method can skip emission after `FFSNetwork.HasDesynchronized`; the
continuing shared input is not evidence for that condition.

The assembler retains the newest snapshot sequence per sender. Arbitrarily
putting an entire newer snapshot ahead of an incomplete original can retire that
original. That violates the selected native ordered-delivery contract and is not
the reproduced failure. The reordering control reverses pages within each
snapshot while preserving snapshot order. No retransmission-protocol change is
justified. `QueueResults` retains two full revisions, expands dependencies before
coalescing, and preserves author/revision checks.

## Hardware intake

Both current logs identify ModBuild 768 and `d18f3cb7c`, built
2026-11-09 20:44:05 UTC. Main log: 14,318,347 bytes, 12,693 lines,
SHA-256 `d287be46a7e225b3fa105ae2b5e233bedd6308f9dbd96e0442bd637a616f8182`.
Guest log: 1,641,811 bytes, 984 lines,
SHA-256 `79c65553bf841149818c809ae0a5ddf0046ee75882b90e6ecd47c13feb6b4781`.
Intake selections and original hashes are under the main checkout's
`.planning/validation/build769/transport-lane/`.

Neither log contains a current Results presentation-wait warning or
`NativeResultsSample`/`NativeResultsApply` failure. EXTRAS reports aggregate all
streams and cannot establish receipt of a specific Results basis. The guest
initially allocates a 4067x710 supersample target at line 474; camera shapes
change at 500–502. Observer input chrome exists at 505, while native interactive
control counts change from three at 518 to five at 552. These observations were
passed to the rendering lanes and do not establish a screenshot cause.

## Validation and limits

The existing runner now projects actual `TickResultsSend`, `CopyResultsPose`,
`SameResults`, `TrySendResults`, `TickFragments`, `SendToken`, `RaiseReceived`, all
assembler declarations, `QueueResults`, `QueueResultsReady` and the root's
progress helper. The wire project compiles actual codecs, compression,
scheduling and assembly. Root progress logging and native phase acquisition use
explicit fixture ports. No Photon or native rendering is claimed here.

The stable-topology workflow has two independent receiver/assembler/driver
instances. Both display a one-part heading, receive a new Stats original over
20,000 bytes, and converge on its final exact transform after coalescing and
heartbeat updates, with 24 native-slot queues plus eight other public queues
continuously occupied. The second receiver reverses pages within each snapshot.
There are 69 Results-bearing events, 1,723 other events and two full/35 delta
completions per receiver. Background bytes are opaque future TLVs within the
actual stream caps; they occupy the real scheduler without claiming those
streams' native capture semantics.

Queue lifecycle checks exercise an older active full finishing once before the
latest full and its exact delta; one waiting full or at most full+delta; duplicate
full preservation; final revision without rewind; immediate new key, new author
and native close; and an empty final backlog. Existing retry and reliability
controls remain in the ordinary workflow.

Final native-sample command:
`python3 scripts/check-results-transport-runtime.py --output-dir .planning/debug/results-transport769 --native-samples <enabled-full.bin> <disabled-full.bin>`.
Final parenthesized-source run `run-ppfpvkr5`: **961 production assertions**, old-cancellation counterexample
and four compiling causal controls fail at the intended assertion; six cases
pass, **33.335 seconds**, zero warnings/errors, unchanged source pins. The four
controls remove new-key priority, new-author priority, close priority and full
waiter coalescing. All generated caches are removed by canonical completion
cleanup after each run; logs, source projections, source pins and samples remain.

Ordinary transport command on the repaired candidate, `run-rpqcjkd6`: **2,176
production assertions and nine compiling causal controls**, ten cases pass in
**38.164 seconds**, zero warnings/errors. This checkpoint precedes only the
explicit parentheses around the equivalent cancellation subexpression; the
native-source checkpoint exercises the final spelling. The root's integrated
complete gate validates the final union once. The earlier native checkpoint
`run-ol4pnix2` passed the same 961 assertions/five controls in 36.432 seconds.

Earlier checkpoints are retained: stable workflow 2,148 assertions/eight controls
with a fixture router; 2,104/eight controls after switching to actual
`RaiseReceived`; 2,104/ten controls before the production repair. The former
lost-full-tracking control is no longer a valid negative control because the
repair deliberately restores progress even when every capture is sent as a full
original. The delta-as-full-basis corruption remains a causal control.
Three early bounded-input fixture failures, one duplicate linked-source build
failure and one nullable fixture build failure are preserved. Compilation
failures are never accepted as causal counterexamples. The first external-path
run was moved into the owned debug run tree for canonical cleanup before
archiving. No shared caches or read-only game/reference inputs were changed.

All 13 attempts are copied to the main checkout's
`.planning/validation/build769/transport-lane/workflow/`. Its archive manifest
verifies 1,270 files; canonical completion cleanup removed 356,072,524 cache
bytes across the owned runs, retaining failed evidence and sampled binaries.

The root classifies the integrated shared-queue change as C and owns the final
complete local checkpoint, wire goldens and strict build. This lane runs affected
coverage only and claims no full-gate pass or hardware acceptance.
