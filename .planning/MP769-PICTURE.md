# Build769 Results widget/data lane

Base: `d18f3cb7c`, Build768. Ownership: `ScenarioResultsPresentation.cs`,
the existing Results picture fixture/runner and this report. The only production
change is the root-requested bounded successful-application receipt; this lane
changes no render/input behavior. This is a class B fixture/consumer audit; the
integrator owns the final union's classification and required gate.

## Conclusion and evidence boundary

The corrected hardware intake identifies Build768 on both peers. The guest's
Results scroll descriptors progress from `eligible=0,resolved=0` at line467 to
`eligible=2,resolved=2` at line503. There is no new Results-picture/asset refusal.
The previous `noise_01` diagnosis must therefore not be reused as the explanation
for the reported Victory/Continue-only guest picture.

The guest's `1342x157` frame at line504 is an intermediate observation. Lines518,
552 and later coverage reports identify a persistent `1474x1024` host frame. The
hardware logs do not contain the captured/received node properties, so they do
not establish the actual arriving Stats active/Graphic/Renderer/CanvasGroup
values. A resolved scroll descriptor and a corrected frame establish neither
the painted body nor its final head pixels.

Source review found no newly demonstrated loss in the complete-picture capture,
cumulative delta, node properties, module ancestry or clone draw-state application.
A fresh actual-Unity probe was constructed for an omitted fixture
boundary: independently created Canvases and privately hidden/disabled guest
Stats. **Unmodified Build768 production passes it with zero differing image
bytes.** This eliminates that particular cause; it is not a successful
reproduction of the hardware defect and does not justify a runtime patch.

The actual supersample/coordinator/head path is owned by the separate capture
lane. Its results must remain distinct from this lane's raw-camera result.
Known canonical-retirement, registry-context and same-shape rollback gaps from
MP768-REVIEW-PICTURE remain reachability-qualified findings; none was relabeled
the current hardware cause or patched speculatively here.

## Reviewed data and drawing path

* `TickResultsSend` captures only the selected common author's output. Publisher
  takeover resets its former observer lease before sampling. `Capture` publishes
  cloned node arrays and the current converted `HostRect` frame.
* `SameResults` compares FrameSize and each part/node/property. Delta headers
  carry current FrameSize; cumulative node changes refer to one immutable full
  basis. `Expand` reconstructs against that exact author/key/revision before
  receiver coalescing. A frame-only change is not omitted as unchanged output.
* Each module carries activeSelf and its parent binding; the preserved hierarchy
  supplies effective active state. Graphics carry enabled/color and independent
  CanvasRenderer color. CanvasGroup carries enabled/alpha/ignoreParentGroups;
  Canvas carries enabled, sorting, pixel and channel metadata. Masks and original
  RectMask2D padding/softness remain explicit properties.
* Clones retain original names and graph identity, remove other module roots and
  mod-owned children, and neutralize native controller/callback/layout/animation
  behavior. Original widgets retain their gameplay/native permissions. Author
  node values and RootPose supply geometry, active flags and sibling order.
* Detached Ready is mapped into the native root frame with its original world
  pose; its virtual sibling index is after the root children. The exact native
  root backdrop is sampled through its audited immutable sprite slot.
* A possible ordering gap was investigated: Build normalizes existing clone
  Canvas mode/camera before Apply, but a source-only Canvas can be created later
  by `Require<Canvas>`, including a same-transform-shape update. Actual Unity
  reports the resulting Canvas as WorldSpace with the host camera inherited
  from its common parent. Its rendered pixels match the source. No speculative
  extra normalization was added.

## Fresh fixture and controls

`NativeWidgetState.cs` keeps source and observer alive simultaneously on separate
camera layers. The observer's original Stats is inactive before the first clone;
its graphics are disabled with CanvasRenderer alpha0 and it has an extra private
CanvasGroup alpha0. The author later adds a real Canvas to its Stats module,
changes a source CanvasRenderer alpha, captures the complete production picture,
crosses the production codec and updates the already existing same-shape observer.

The final nine assertions cover actual captured Canvas presence, codec admission,
existing-picture retention, active recreated Canvas, stretched roots under a
changing frame, complete native pixel parity, preservation of original private
visibility/alpha and the actual sampler's full-basis transition. The final
raw-camera comparison has **zero differing bytes**, not a relaxed screenshot
tolerance. The imported original graphic/material graph is rendered on both
sides; it is not replaced with colored test rectangles. Native controllers,
bootstrap, text population and animation timing remain explicit fixture inputs;
the actual source image still contains original prefab placeholder text. The
existing import does not run the complete native mask/controller lifecycle.

The strengthened probe changes the source frame from `1474x157` to `1474x1024`
with a root stretching from anchor0 to anchor1. The private guest Stats root and
viewport are zero-size before cloning, with a real additional RectMask2D, explicit
`CanvasRenderer.cull=true` and an empty clipping rectangle. The source has its
own full-size RectMask2D. The initial clone's 367 graphics all have cull=false and
no retained clipping; these private CanvasRenderer flags did not survive clone
creation. The full settled picture has 61 unclipped-by-cull rect-clipped graphics,
eight properly culled rect-clipped graphics and 298 unculled/unclipped graphics,
with the same complete pixels as the source.

A direct post-Accept geometry assertion initially failed. Its retained values
show why: HostRect immediately grew to 1024, while the root still interpolated
from 157 under the existing at-most 250 ms same-shape blend. After 300 ms the root is
the full 1024 despite its candidate mount remaining 157. This does not reproduce
a persistent clipping defect. Immediate and settled PNGs and numeric receipts
are retained separately; the settled pass must not be called immediate parity.

The sampler bridge now exports two **actual production Capture/codec** payloads
for the transport lane. Toggling exactly the original Viewport Image.enabled
changes one node's property-key set while preserving every part's Address,
Structure and node count. `ScenarioResultsDelta.Compatible` becomes false;
the full payloads are 12796 and 12785 bytes, both requiring multiple fragments.
The raw bytes and `sampler-stage-transition.json` were handed to the independent
transport lane for unchanged production publisher/scheduler/receiver execution.
This establishes the sampler prerequisite. The toggle is a fixture input; this
lane does not claim to have observed its exact hardware timing or the transport
consequence by itself.

Two independently compiled negative controls prove sensitivity:

* Suppressing source Canvas application fails the active common-Canvas assertion.
* Retaining the cloned private `Graphic.enabled` flags fails complete native
  pixel parity. The expected body can therefore not be silently omitted while
  this check passes.

The default containing fixture now includes these nine assertions and both
controls. `--widget-state-only` runs this bounded scope. The optional reflective
`RESULTS_HEAD_ONLY` dispatch permits the capture lane's separately compiled partial
fixture without introducing a compile dependency into the default fixture.

Executed runs, all retained:

| Run | Scope and result | Elapsed |
| --- | --- | --- |
| `run-sme_k43a` | First probe; private native state changed after initial clone; 6 assertions PASS, zero pixel differences | 26.560s |
| `run-09l9etox` | Strengthened cold-clone probe; 6 assertions PASS, zero pixel differences | 26.827s |
| `run-wmcqqp7q` | Final strengthened fixture, production 6 plus 2 compiling negative controls PASS | 38.556s |
| `run-ri7ppm07` | FAILED fixture compile: helper method `Rect` shadowed the `Rect.zero` type reference; qualified as `UnityEngine.Rect.zero` | 13.540s |
| `run-1u96b_4k` | FAILED immediate root-size assertion before the intentional interpolation elapsed; all three cases stopped at that assertion | 37.803s |
| `run-rkjc3yvb` | Root stretch, zero-size native Stats/viewport and stale cull/clip probe: production 8 plus 2 controls PASS after the explicit blend interval | 33.558s |
| `run-wyxyzu8v` | Final sampler bridge and clip/frame probe: production 9 plus 2 compiling controls PASS | 34.002s |
| `run-aiinzfmk` | True root-owned progress helper included; production 9 plus 2 controls PASS; superseded runner binding noted below | 34.667s |
| `run-0tcptmz4` | Final immutable fixture/runner with true progress helper; production 9 plus 2 compiling controls PASS | 37.594s |

The compile and immediate-geometry failures remain failed records, with the
bounded repairs above. The final fixture passes unmodified old production.
No complete containing-suite pass, complete local gate, golden,
Photon, split-process cold asset loading, Windows D3D11 or headset result is
claimed by this lane. The Build768 2407-assertion/22-control record applies only
to its recorded unchanged-input scope; the nine new assertions are fresh bounded
evidence. The integrator must account for the changed fixture/runner in its final
union rather than label the old record a fresh complete pass.

## Bounded successful-application receipt

The integrator requested one normal-level lifecycle receipt after successful
Paint. `ScenarioResultsPresentation` owns a readonly `ScenarioResultsProgress`
instance named `displayed`, calls Observe only after the complete Paint succeeds
and the picture is activated, and resets it with the existing lifecycle Reset.
The root owns the new pure-DTO helper source and its independent tests. The helper
deduplicates the same state reference and unchanged census, caps at eight reports
per author/opening, checks log admission before scanning or formatting, and never
reads or changes a native actor/window/graphic/callback.

This receipt reports the applied target DTO's part/node/property census and
active root flags. It does not sample live clone alpha/culling, capture completion
or head pixels, and must never be used as that evidence. The consumer fixture
compiles the actual helper and executes its admitted DTO path; existing logger
output remains a disclosed no-op port. The first helper run was superseded by
the final immutable-runner invocation because the equivalent optional-file
fallback was added to the runner while that earlier invocation was running.
Both runner texts and that run's original manifest remain retained.

## Bounded runtime receipt proposal

If the combined source-real transport and actual-head probes still fail to
reproduce the report, add a bounded stage census at the actual Capture/Accept
boundaries before proposing another rendering patch. Suggested Debug receipt:
author/key/revision/base, packet bytes/part/node count, Stats root active flag,
source and clone enabled/active graphics, positive effective-alpha and culled
counts, native/clone Canvas enabled counts, mask/clip counts, current FrameSize,
HostRect/root/mount sizes, capture layer and picture-accept success. Compare
received source values separately with live clone values. A missing body should
be traceable to capture, publication/completion, acceptance, live draw output or
final composition without treating a scroll descriptor as picture proof.

Guard the scan and formatting with the existing Debug-level check. Emit the first
accepted picture and meaningful heading/Stats/frame/retirement changes, with an
opening-scoped budget and minimum interval, not every revision/frame. Retain
normal-level failures and a bounded actual significant-anomaly report if source
Stats is drawable while the accepted clone has no drawable Stats. This is a
proposal only. The implemented normal-level successful-application receipt above
is intentionally limited to its pure DTO evidence.

## Preservation and cleanup

The main checkout's ignored
`.planning/validation/build769/picture-lane/` retains all nine runs, complete
exported native inputs, compiled fixture artifacts, raw packets, captures,
Unity/build logs, results and manifests. All 402 original pinned source files are
frozen and byte-verified against each completed run's SHA256 manifest, including earlier
exploratory fixture versions. The failed pre-Unity compile retains its compiler
inputs/log and an explicitly identified reconstructed fixture snapshot; it never
wrote the runner's later source-pin manifest. The archive verification record is
stored beside the archive. The first unverified expanded tar attempt was stopped
and replaced with a content-deduplicated archive; no test/evidence file was lost.

All Unity invocations used private ADB 5082 and their processes were reaped by the
runner. Each run's canonical `cleanup_completed_run` receipt records removal of
owned generated bin/obj/Unity Library caches after completion. No shared native
reference, foreign run, hardware input or pinned capture was removed. Local diff
whitespace and Python syntax checks passed. The capture lane uses ADB 5083.
