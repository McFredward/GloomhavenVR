# Build 616 Steam Frame follow-up: unchanged steady pacing and persistent wall pops

This is an audit of the **616 / d52b3c328** hardware capture, not a claim that
Build 617 has already improved the headset. Both LogOutput and Player identify
that build; the seventeen FRAME windows match between sinks. Unity 2021.3.5f1
uses D3D11 through Turnip Adreno 750, SteamVR/OpenXR 2.17.10, MultiPass at
**3408 × 3408 per eye**, scale 1.00 and one swapchain sample. The supplied folder
contains three log files and no new picture/video. Unlike the preceding 615
capture, this run goes directly into the scenario without a CampaignMap visit.

Immutable copies and the reproducible parser/metrics live in
`.planning/debug/frame617-review/inputs/` and `log-audit/`. The input SHA-256s are:

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| LogOutput.log | 10067900 | `c48108b494c24eeb709fbcf97bdcfc5f9d6baf26a9f69578f9c05aec0daa43f0` |
| Player.log | 10466958 | `c8eabb3d38b841aaaac2159b2227369d1584ecaa0281bd53ff4e56b554e3c2d7` |
| openxr-diagnostics.log | 92017 | `7333018796591ad37e8699b3da82321fc1834491d17408cdf680af5ee043bbf7` |

The OpenXR file is append-only history: its final successful opening is
2026-10-04 09:29:29, not a separate performance sample. References below use
LogOutput lines unless labelled Player. Supplemental extraction uses Python
`str.splitlines()`; Player contains embedded line separators, so its normalized
line numbers can differ from physical LF counts. The bytes and hashes are fixed.

## Loaded scenario result

The scenario spinner hides at line 1951. FRAME 2271 still owns first-use wall
publication and is excluded. Six subsequent summaries, 3142–5355 (indices 9–14),
contain **2236 frames over 110.3 s**, with all three scenery controls at zero,
figure detail/effects zero, native cloth off and figure revision 2 steady.
They end before the options activity and later vegetation retunes.

| Fully loaded scenario population | Mean frame | Named mod scopes | Logic span | Render span | Unbracketed engine work/waits |
| --- | ---: | ---: | ---: | ---: | ---: |
| 612, 7164 frames | 53.20 ms | 17.81 ms | 31.67 ms | 7.76 ms | 13.77 ms |
| 615, 2223 frames | 49.60 ms | 15.53 ms | 28.13 ms | 6.14 ms | 15.33 ms |
| 616, 2236 frames | **49.35 ms** | **16.18 ms** | **28.69 ms** | **7.10 ms** | **13.56 ms** |

The 0.25 ms difference from 615 is only **0.5%**. It does not demonstrate an
improvement in the recurring hitches; it agrees with the maintainer's report.
Mod timing is slightly higher, not lower. The different natural VR viewpoints
and interactions remain usable observations, but do not isolate a per-change
saving. No fixed-camera retest is required to draw this limited conclusion.
Mod scopes are inclusive and inside logic; they must not be added to logic.
The reciprocal mean is about 20 application frames/s, not compositor/headset FPS.

Window p95 values remain **78.17–97.22 ms**; the worst summarized frame is
**327.21 ms**. Each window owns its percentiles; there is no pooled p95 in the
capture. Some later large spikes occur during setting preparations or after
user presence is lost, and are excluded from this loaded gameplay aggregate.

## What the new measurements resolve

The refresh-safe census now makes real progress: line 5924 reports **2577 slices,
163812 work units and 28789 visited nodes over 130.37 s**, using 413.13 ms of
cumulative CPU time. Earlier display changes preserve that same job. That fixes
the previously observed refresh cancellation mechanism, but the capture still
contains **zero completed SCENE, SIM or GFX inventories**. An actual vegetation
change at line 5946 invalidates the unfinished sample. Later retune/preparation
boundaries legitimately restart it again. Therefore there is still no complete
renderer/behaviour census with which to identify all remaining engine work.

The steady diagnostic slice contribution is 0.206 ms/frame; its largest slice
is 15.15 ms and the largest native work unit is 14.759 ms. A stopwatch budget
cannot interrupt one native Unity call. The normal traversal is mostly limited
by 64 work units/frame rather than its 0.5 ms CPU budget: at roughly 20 frames/s,
a large hierarchy can require minutes. Raise only the bounded work-unit allowance
or prepare this diagnostic inventory during loading, retaining the CPU cap and
real scene/config invalidation. This is improved evidence, not an established
headset performance saving.

There is a separate source edge: `AppendFrameCensus` calls `Cancel()` when
SceneCensus is false, even if an independent full SceneProfile sample is alive.
However, `PerfMonitor.LogSplit` invokes that method only while SceneCensus is
true. The constant-false configuration in this capture does **not** prove that
this edge caused the late zero progress samples. Source corrections and tests
must retain that distinction; the observed real setting changes already explain
many resets. The live `visible n/a` line positively identifies SceneCensus off;
SceneProfile itself is running, as the progress reports establish.

The selected native callback probe samples only the first 120 frames of each
summary. It sees small individual native callbacks rather than a hundreds-of-ms
culprit in this population. Its calibration estimates around 0.315 ms/frame of
probe-body overhead at the sampled call count, excluding Harmony dispatch.
Peaks after the initial 120 frames can escape it. Unknown Unity internals,
coroutines, jobs, asset work and scheduling remain outside those named scopes.

## Ordinary interaction hitches remain

The clearer hand scopes confirm that the earlier cold-interaction targets have
not disappeared:

- Pickup reaches **109.10 ms**, with **55.89 ms** nested ghost construction.
  These figures must not be added: construction is part of pickup. The earlier
  capture measured 105.12/48.32 ms.
- Frame 5437, line 4161, lasts **311.95 ms**, with **193.73 ms** named mod work
  and **Cards.Driver 172.37 ms**. Immediately preceding lines 4133–4153 build
  the selected Summoner's nine original faces and class artwork. The STEPS
  summary also retains a Cards.Driver maximum of 183.61 ms and VRCardArt 70.79 ms.
- The stat preview path still reaches **65.93 ms** within a loaded summary;
  another logged figure interaction has a 35.54 ms StatPanelSurface component.

Current `CardArtPrewarm` warms original sprite loaders on adoption. That does
not guarantee that wrapper creation/layout/adoption of the original native fan is
prepared for every later character switch. Use the existing original-face pool
and construction paths to prepare likely character hands while loading or in
bounded background work, retaining live card state and immediate fan opening.
Do not introduce a delayed reveal or custom replacement fronts. Ghost/preview
preparation similarly needs original current idle state, including sleeping
figures, without gameplay callbacks on the presentation copy. These are source-
and hardware-supported next targets; no saving is assigned before measurement.

Steady scene work also remains: WallFade.Late contributes 2.457 ms/frame and
ActorBars.Late 1.849 ms/frame. The bar loop skips 6918 of 37944 pose checks,
**18.2%**, similar to 615's 18.5%. All 37944 optional idle evaluations report
already-authored culling; newly applied transform culling remains zero. Existing
figure reductions are active, but have not eliminated cold construction.

## Vegetation after preparations, not slider stalls

The maintainer explicitly accepts stalls while applying a slider. Initial
retune/construction cost is not an optimization target here. The correct question
is whether the resulting live scene is cheaper after preparation has finished.

The final three scenery reports inspect the **same 8970 unique mesh renderers**
and the same eligible counts (639 grass, 1710 vegetation, 893 decoration):

| Finished state | LogOutput receipt | Actually forceRenderingOff |
| --- | ---: | ---: |
| 10% vegetation, 0% grass/decoration | 6354 | 3095 |
| 5% vegetation, 0% grass/decoration | 6490 | 3166 |
| 0% vegetation, 0% grass/decoration | 7097 | 3242 |

Thus the control **really changes rendered eligibility**: 10→5% removes another
71 renderers, and 5→0% another 76, for 147 additional masked renderers overall.
These are retained-scene renderer counts, not head-camera draw calls or timings.
The initial zero state inspected 7835 renderers and masked 2332; compare it
separately because native scenario content later grew to the final population.

There is no full summary after the finished 10% preparation and before the next
change. Its 65.39 ms summary at 6346 closes the preparation interval and is
excluded as a steady 10% measurement. The finished 5% state has one complete
17.5 s summary at 7043: **334 frames, 52.81 ms mean, 90.13 ms p95, 19.95 ms mod,
32.73 ms logic and 7.12 ms render**. Options windows are still visible and figure
revision 3 remains preparing, so this is valid post-vegetation-preparation evidence
but does not isolate a vegetation-only saving. The final 0% state has no completed
FRAME window before the log ends. Partial MARK windows are not substitute timing
samples. The source and counts demonstrate that vegetation is reduced; this
capture does not establish a numerical steady FPS gain specifically for 10→5→0%.

## Wall popping is still a defect

Build 616's timestep clamp really runs: Player has 31 bounded Debug clock reports,
including 188 bounded rendered frames and a largest elapsed frame of 125.53 ms
near the late loaded interval. The sentence claiming visible samples in that
report is a source intention, not pixel evidence. User hardware still sees pops.

The previous incompatible cheap-shader target is absent in the latest environment
reports: **zero compatible surfaces, zero chunks and zero simpler materials**.
The clamp and material veto therefore cannot by themselves explain or resolve the
remaining picture. Settled HELD samples and property-block start/end counters
also do not prove a broken intermediate picture. The next correction must follow
the actual native renderer, shader keywords, texture/ramp and visible draw output
through a transition. An Editor GL surrogate cannot certify the shipped native
Windows shader under the Frame's D3D11 translation. Preserve native animated
wall/foundation presentation; no silent popping compromise is authorized.

In the primary loaded interval **29 of 29 wall cycles skip publication** and no
atomic commit is recorded. Later publications are distinct:

- Line 5753 forces **140.81 ms** of atomic publication solely because the
  staleness ceiling fires; all listed signature/room/board terms remain zero.
  This is before the first vegetation change, with options already open.
  Verify whether an unchanged publication is truly required before removing
  this age-driven path. The final ownership table must remain complete/live.
- Lines 6172 and 6415 publish for changed scene signatures, costing 223.25 and
  208.00 ms. They coincide with vegetation retunes/new preparation and are
  **excluded as optimization targets in this round**. They cannot establish a
  recurring ordinary gameplay wall cost.

Atomic wall work is therefore not the cause of every steady hitch. Raising the
rescan interval further would not shorten a genuine atomic publication and is
unlikely to fix the observed cold card/figure interactions.

## Unresolved causes and priorities

The loaded six-window population allocates about **250.3 MB over 110.3 s**, with
three reported gen0/gen1/gen2 collections. This establishes managed churn, not
a memory leak or a causal GC pause; the log lacks a per-spike GC attribution.
A single increasing heap value during a different action population is not a
leak diagnosis. The player's native Hydra service repeatedly fails DNS, but no
causal alignment with an interactive hitch is established.

The tail reaches 555.44, 489.60 and 985.57 ms with little named mod work, **after
user presence is lost at line 7185**. Keep these as runtime observations, not
proof of worn-headset gameplay hitches. Even before that loss, a 592.48 ms frame
at line 5992 falls inside the accepted setting preparation interval. Neither
population belongs in the headline loaded comparison.

Only the two HeadCamera eye passes occur in ordinary scenario rendering.
Options supersampling cameras perform requested original UI captures while
options are visible; they are not a proven hidden flat-camera leak. Unity
FrameTimingManager returns no samples because this player lacks frame timing
stats. XR's `gpu` field tracks an interval/wait and is **not GPU busy time**.
The roughly 13.6 ms residual does not establish GPU saturation or a driver cause.

Prioritize the actual remaining work in this order:

1. Reproduce and correct the wall's real native transition output, independently
   of its decision cadence and without claiming that a smooth numeric ramp
   proves a smooth headset picture.
2. Prepare original fan construction, figure ghosts and native stat previews
   before the first ordinary interaction. Existing pool/art paths should own
   this work; retain immediate input, sleeping idle correctness and remote parity.
3. Remove only demonstrated redundant ordinary maintenance/publication, including
   the unchanged-table staleness case if its safety audit proves it unnecessary.
   The steady wall/bar contributions are measurable but smaller than cold peaks.
4. Finish the bounded inventory and extend per-spike attribution beyond the first
   120 sampled native frames. Include collection changes and engine main/render
   thread counters when actually exposed. Keep expensive diagnostics bounded at
   Debug, and report unavailable counters as unavailable. Do not label the
   unexplained remainder categorically “not the mod” or “GPU-bound.”
5. Evaluate further optional renderer/animation compromises only against the
   finished scene. Current zero-percent counts and real derivative/effect readbacks
   remain useful; they do not identify how much renderer submission, skinning,
   engine animation or translated graphics work is left without the full census.

The new evidence narrows several targets but does not yet prove the cause of all
occasional stalls. Initial loading and slider preparations remain accepted costs.
