# Build650: cheaper current world-material validation

The maintainer requested the first follow-up strategy after the647 regression:
reduce the CPU cost of the existing native-renderer material path, preserve
presentation and immediate native changes, and establish an expected net gain
before delivery. This is universal work removal, with no new quality setting.

The comparison baseline is the648 render implementation, which restores646.
The separate649 wrist-settings integration does not change these render sources.
The645 fully loaded closed-options hardware windows measured approximately
23.94ms/frame in `WorldMaterial.PreCull`, including both eyes. Those windows
contain approximately875 candidates,747 variant slots,57 native slots and1160
scope-node reads per frame, but only46 unique material refreshes. They do not
establish a GPU bottleneck or the effect of this new candidate on headset FPS.

## Changes and validity boundaries

Each existing `Surface` retains its renderer's exact owning `GameObject` and
`Transform`. A component cannot acquire a different owner during its lifetime.
The current layer, active state, parent chain, component inventory, mesh filter,
mesh, held roots and materials are still read at their original boundaries.
Destroyed renderers are discarded before the owner references are used; newly
adopted renderers get their own current references. Scene/settings teardown clears
the existing surface inventory. This adds two references per surface, about7KiB
for440 sources on a64-bit runtime, without new renderers, materials or lookup
collections.

Each ancestry iteration reads the current node name once for its two predicates.
The intervening component/type checks and `ProceduralStyle.AnimateStyle` field
read do not invoke callbacks. The next pass still reads the current name again.
No ancestry verdict is retained between eyes or across a native write boundary.

After fetching the actual renderer and indexed material property blocks, effect
validation tests their current `isEmpty` state. An empty block cannot contain
a video texture, animated scalar or native blend override. Its otherwise repeated
`HasTexture`/`HasFloat` native calls are omitted; populated blocks keep the same
typed presence/value checks, including explicitly present zero-valued floats.
Both blocks remain freshly read. Empty verdicts are local to the effect method,
after callback-capable source resolution, so nested camera work cannot leave a
cached empty verdict from an earlier slot.

The source removes repeated native component-owner getters, one native name
getter per visited scope node, and empty-block property lookups. An AMP slot with
one populated block and one empty block trades12 absent-property lookups for two
current emptiness checks, a net10 fewer native calls. Both-populated blocks incur
the extra emptiness checks, so complete-method benchmarks must cover that case.

The implementation deliberately does not cache mutable shader schemas, material
values, pass enablement, names, component inventories or eligibility between
camera invocations. Camera masks and command-buffer counts stay current inside
the loop because material preparation can invoke callbacks. No renderer
admission, shader, mesh, floor path, fade, profile, asset bank, wire or gameplay
behavior changes.

## Validation and limits

The independent proof lane compares the exact648 and candidate world sources as
separate assemblies in the same Unity2021.3.5 session. It invokes the complete
`Driver.PreCull` through a bound delegate, without per-call reflection or timing
instrumentation on native API calls. Workloads include one/many originals,
one/four slots, empty/populated blocks, native effects and writes, inactive
sources, excluded cameras, Off and Debug/options paths. The representative
workload follows the hardware census with440 candidates per eye,64 inactive
sources and23 unique originals. CPU timing is a local runtime result, not a
measurement of the original game scenario, native Windows rendering, GPU time,
multiplayer overhead or Steam Frame FPS.

The final21-round,24-frame-per-round comparison samples both-eye invocations.
Paired bootstrap intervals use5000 deterministic resamples. Representative
results from the exact `a68f268319a8abd5686214491698ef9551c54402` source:

| Current workload |648 median ms/frame|650 candidate median ms/frame|Paired CPU reduction,95% interval|
|---|---:|---:|---:|
|440 candidates/23 originals, no MPB|3.4810|2.8996|16.52% [15.95,17.21]|
|Same census, renderer-wide MPB only|4.1858|3.4114|18.54% [18.14,19.23]|
|Same census, indexed MPB only|4.2169|3.4122|18.99% [18.62,19.64]|
|Same census, both block levels populated|4.2224|3.6942|12.46% [11.64,12.65]|
|Same census, Debug/options camera path|3.7470|3.1123|16.96% [16.80,17.46]|
|440 distinct originals|16.0098|15.6560|2.59% [1.36,3.16]|

The first five cases win all21 paired rounds and reduce the observed frame P95.
Additional populated four-slot, Standard blend-state, native-video and animated
MPB veto workloads also reduce complete-path CPU medians. The empty fast path is
not accepted solely because its helper executes fewer calls: populated-block and
early-veto cases include its extra emptiness calls in their measured totals.
Settled Off remains unchanged within timer quantization.

One artificial shared two-slot/both-populated workload initially improves its
paired median8.12% but raises P95 from4.5590 to5.1583ms. That adverse sample remains
part of the evidence. Two fresh isolated repeats improve paired medians9.64%
[7.91,10.67] and9.54% [9.01,11.06], with P95 falling9.5358→8.0744 and7.1644→7.0210ms.
The earlier P95 rise was not reproduced; the evidence supports an expected CPU
gain, not a guarantee that every individual sample or tail is faster. Absolute
times from different sessions must not be compared as a hardware speed trend.

Final benchmark, correctness, build and negative-control receipts are maintained
by the integrator. The independent actual-source Unity correctness lane passes765
assertions and all68 variants, retaining the previous64 cases and adding current
empty-block, owner-lifetime and name/parent mutation guards. Source hashes remain
unchanged throughout the completed run. The failed earlier expected-message
attempt and adverse initial tail remain archived rather than being relabeled as
passes. See [the independent proof ledger](FRAME-650-WORLD-PROOF.md).

For the440-source/64-inactive/581-scope-node-per-eye fixture, source-derived
accounting removes2384 component-owner/transform getters and1162 duplicate name
getters per both-eye frame. These are counts derived from the actual source and
fixture shape, not observed native profiler counters.

The worker's exact-source strict Debug and Release builds pass
with0 warnings/errors. Focused partial-order checking retains49 multi-part types,
300 parts and no cross-part static-initializer dependency; the exact baseline/current
source-surface comparison retains663 config keys,232 patches and4790 log tokens.
These bounded checks do not constitute a new complete local gate.

A supported allocation measurement is required before claiming
measured GC savings: Unity's `GC.GetAllocatedBytesForCurrentThread` returned zero
even for a deliberate65536-byte allocation in the initial probe. Native owner/name
read removal is source-proven; that broken probe cannot quantify allocated bytes.

The shipped hardware outcome still requires a fully loaded, closed-options
Frame comparison with the same scenario, view and quality settings. Local checks
must show a robust complete-path CPU improvement and intact native presentation
before this candidate is integrated; isolated helper timings are insufficient.
