# MP-492 performance evidence review

Initial performance review of dev `050e8801`; the subsequently delegated native-element correction is documented below.

## Evidence and limits

Both current hardware logs identify **ModBuild 491**, assembly 0.1.1.0:

- `.planning/debug/LogOutput.log`: 119,776,617 bytes, 95,282 lines.
- `.planning/debug/remote/LogOutput.log`: 80,216,973 bytes, 56,308 lines.

These are the main checkout's ignored evidence files. The previous 490 test logs have been overwritten. The available `second_logs/LogOutput.log` identifies 472 and `Player-prev.log` identifies 463; neither is a matched pre-change baseline. This review therefore establishes current performance problems, **not a measured 490→491 regression**. There are no `[Error` entries in either current LogOutput file.

Extraction accepts both decimal separators (main uses dots; remote uses commas). Only actual `] [Perf] FRAME/SPLIT/STEPS` records count, not startup help mentioning those strings. Aggregate means below are frame-count weighted; no average of window medians is presented as a global percentile. Reported windows omit unfinished tails and can miss observations around instrumentation resets: for example, remote line 327 reports an 8,671.27 ms startup SPIKE that does not survive into a FRAME maximum. Windows are not a complete wall-clock trace.

Phase attribution uses the last preceding ten-second peer face census. A performance window can cross phases; the phase breakdown is contextual, not a controlled experiment. `view` availability does not establish a scenario: the map also has a view. Distances are game world units; they must not be interpreted as metres or compared directly between clients.

## Measured frame cost

| Completed windows | Main | Remote |
| --- | ---: | ---: |
| Windows / reported seconds | 102 / 3,026.2 s | 132 / 2,659.5 s |
| Sampled frames | 212,002 | 126,083 |
| Mean frame time | 14.26 ms | 21.05 ms |
| Named mod scopes, depth-zero total | 5.11 ms | 5.83 ms |
| Update→LateUpdate logic span | 6.87 ms | 7.75 ms |
| Camera cull+submit span | 2.04 ms | 2.22 ms |
| Residual outside those spans | 5.35 ms | 11.08 ms |
| Runtime GPU mean | unavailable | 8.62 ms |
| Reported over-budget frames | 154,107 (72.7%) | 60,044 (47.6%) |
| Frames above dynamic spike threshold | 13,809 | 8,107 |
| Suppressed individual SPIKE lines | 9,671 | 6,593 |
| Logic spans over 100 ms | 42 | 37 |
| Render spans over 100 ms | 0 | 0 |
| Generation 0 collection increments | 669 | 500 |
| Positive managed-heap deltas / second | 9.93 MB/s | 8.33 MB/s |

The remote over-budget percentage is especially easy to misread: the budget changes with the runtime's reported presentation rate. After startup, the main reports 90 Hz throughout (11.11 ms). Remote logs contain **100 presentation-rate change messages**, including startup. Completed remote windows comprise:

| Reported remote rate | Windows | Reported duration | Mean frame | Mean logic | Mean GPU |
| --- | ---: | ---: | ---: | ---: | ---: |
| 72 Hz | 57 | 1,100.8 s | 17.53 ms | 6.31 ms | 6.19 ms |
| 36 Hz | 63 | 1,430.2 s | 24.49 ms | 9.19 ms | 11.19 ms |
| 18 Hz | 6 | 60.0 s | 24.73 ms | 10.15 ms | 10.43 ms |
| Other reduced rates: 24, 12, 10.3, 9 Hz | 5 | 50.0 s | mixed | mixed | mixed |
| Desktop startup 60 Hz | 1 | 18.5 s | 16.65 ms | 1.65 ms | 0.58 ms |

The runtime probe runs every ten seconds, so a labelled window need not have run at that exact cadence throughout. Reduced presentation rates, many approximately 27.78 ms medians, and frequent transitions are concrete evidence of unstable pacing. They do not identify whether encoding/streaming, compositor policy, CPU work, or GPU pressure initiated a transition. The game exposes no compositor/encoder timing here, and the main exposes no GPU counter. The remote GPU is not always negligible: its maximum recorded window GPU peak is 161.63 ms. A single average-headroom example cannot exclude intermittent GPU pressure.

`PerfFrameSplit` labels residual time as blocked, but its own output explains that canvas rebuilds after LateUpdate are also in this residual. Consequently, the automatic prose claiming this is necessarily idle waiting, or that CPU optimization cannot help, is stronger than the measurement supports. `PerfMonitor`'s “NOT the mod” SPIKE verdict is also limited to named scopes; it cannot exonerate uninstrumented mod work or its later canvas rebuild cost.

## Gameplay contexts

Last sampled phase, including windows that may cross a boundary:

| Context | Main duration / mean frame / logic / mod | Remote duration / mean frame / logic / mod |
| --- | --- | --- |
| SelectAbilityCardsOrLongRest | 780.0 s / 14.96 / 7.13 / 5.68 ms | 800.3 s / 20.91 / 7.36 / 5.83 ms |
| Action | 882.2 s / 15.86 / 8.55 / 6.25 ms | 770.4 s / 23.08 / 9.36 / 6.94 ms |
| ActionSelection | 630.0 s / 14.30 / 7.79 / 5.67 ms | 690.0 s / 21.75 / 8.56 / 6.34 ms |

The slowdown is not confined to selection or a single burn animation. After removing windows attributed to unknown/startup and phase None, the recorded logic-stall totals are still **27 main and 24 remote** spans exceeding 100 ms. Rate-limited SPIKE lines cannot reconstruct every stall's cause.

Representative main gameplay window, lines **43580–43587**:

- 30 s / 1,948 frames, 90 Hz; frame mean 15.40, p50 14.99, p95 23.81, p99 31.91, maximum 55.35 ms; 88.6% over its 11.11 ms budget.
- Logic 8.46 ms, camera render 2.30 ms, residual 4.64 ms; named mod 5.96 ms. This is substantial recurring CPU work, not merely an isolated load.
- Head distance p50 36.1 wu; median visible count 2,541. Its near/far thirds have frame medians 11.65/15.66 ms and logic medians 5.82/7.95 ms, with 854 more visible renderers in the far third. This is an association within changing views, not proof that distance alone causes the cost.
- Fantastic quality, shadows enabled, 150-unit shadow distance, LOD bias 2.0, AA4, multipass. Eye diagnostic elsewhere reports 3072×3264 per eye: 80.2 million MSAA samples/frame. Main GPU time remains unavailable.

Representative remote gameplay window, lines **27790–27797**:

- 30 s / 1,626 frames, 72 Hz; frame mean 18.46, p50 14.12, p95 31.80, p99 57.39, maximum 124.54 ms.
- Logic 7.37 ms, camera render 1.63 ms, residual 9.46 ms; named mod 5.89 ms; GPU mean 6.56 ms, maximum 18.65 ms.
- Head distance p50 19.2 wu. The near/far thirds differ strongly in visible population (2,330 versus 1,013), despite similar distance thresholds. Do not treat the generated zoom verdict as a controlled distance test.
- Fastest quality, shadows disabled, LOD bias 0.3, AA4, multipass, vSyncCount 2. These settings differ from the main and prevent a direct machine-to-machine quality comparison.

Whole-log distance bands also mix scenes and viewpoints. Main windows with median distance 20–40 wu average 15.58 ms (62 windows), whereas distances ≥100 wu average 11.37 ms (20 windows). Remote distances <20 wu average 22.17 ms (103 windows); ≥100 wu average 15.64 ms (8 windows). The only explicit card-build `hand WorldScale` samples found are main 10.49 at lines 1093–1094 and remote 198.12 at 1899–1900. They are early build-time values, **not a current per-window scale trace**; carrying them through the session would fabricate scale evidence.

## Measured mod cost and source boundaries

STEPS scopes nest: **do not add Net.Avatar, Net.TickAvatars and Net.Board together**. The parent Net.Avatar includes the latter lanes. Some steps appear only above the log's reporting cut, so sums across printed STEPS are lower bounds when a step is omitted.

In the main representative window, Net.Avatar costs 2.648 ms/frame, Net.Board 2.539 ms/frame, BoardMirrors 0.870 ms/frame, and NativeRevision 0.996 ms/frame. Remote's matching representative figures are 2.408, 2.274, 0.819 and 0.781 ms/frame. WallFade.Late adds a separately measured 1.105 ms main / 1.135 ms remote, with respective worst calls 40.28 / 69.35 ms.

`RemoteControlBoard.NativeContentRevision` calls `RemoteBoardContent.NativeRevision` for **four distinct roots**: mission objectives, scenario modifiers, initiative and elements. The 7,792 calls over 1,948 main frames and 6,504 calls over 1,626 remote frames are therefore expected four-root work, not four redundant scans of one root. `RemoteBoardContent` already shares each source revision within `Time.frameCount`. Its actual scan recursively reads hierarchy/rect/activity/graphic/text state, including inactive descendants, each frame. The measured 0.8–1.0 ms is the combined cost of all four roots. Another same-frame cache would not solve it; any optimization must preserve detection of native content and intermediate animation changes.

Source-proven allocation candidate, **not yet separately timed**:

- `CardAppearanceSampler.Sample` creates a state list and per-card state graph before comparing it with the previous snapshot. An unchanged final result still allocates the candidate graph.
- `CardAppearanceBindings.Capture` refreshes CanvasGroups with an array-returning `GetComponentsInChildren`, allocates a node list, each `CardAppearanceNode` and its `float[33]`, and final node arrays. Each group capture also allocates/sorts a key list, allocates its node list and output array.
- `NetAvatarDriver.TickNativePresentationSend` samples it each owner presentation publish. The half-second refresh interval limits unchanged wire sends, **not sampling**. Neither this specific sampler nor its clone counterpart has its own visible named Perf scope in this revision. It cannot be assigned a millisecond share from these logs.

Safe follow-up: instrument capture/apply/build separately; reuse scratch lists and comparison nodes, and allocate immutable published snapshots only on change. Preserve snapshot immutability and every-frame output comparison; reducing the sample rate would introduce an unapproved parity delay. Source allocations are enough to justify removing redundant allocations, but not enough to promise a particular frame-rate improvement.

Specific individual hitches show multiple causes:

- Main **79865**, frame 189766: 340.31 ms, named mod 153.43 ms, Cards.Driver 143.23 ms. The remaining duration is not explained by that scope.
- Main **44816**, frame 122269: 207.71 ms, mod 83.39 ms, Net.Board 49.76 ms and Cards.Driver 24.60 ms.
- Main **73842**, frame 181435: 124.22 ms, mod 114.50 ms, Sky/Env.HauntFigures about 107.97 ms. This is a directly attributed environment burst, not a card-face-only issue.
- Remote **51905**, frame 117567: 153.68 ms, mod 59.38 ms, Net.Board 53.53 ms, but NativeRevision only 1.68 ms. NativeRevision is not the cause of this board burst.
- Remote **38008**, frame 95339: 139.92 ms, mod 69.82 ms, Net.Board 50.97 ms.

These broad board/card bursts warrant narrower scopes inside rebuild/apply paths. They do not uniquely implicate a particular 491 source change.

## GC, diagnostics and logging

`PerfMonitor.cs` computes “alloc” by adding only positive changes of `GC.GetTotalMemory(false)`. It is a memory-churn proxy/lower bound, **not a precise allocation profiler**. Both logs increment generations 0/1/2 together. They contain collection counts but no per-collection pause duration, so assigning a particular hitch to GC would be speculation.

The diagnostics themselves are measurable occasional work. Main SIM at line **27883** reports 19.6 ms combined SIM/SCENE census cost; remote **25060** reports 20.8 ms. These run on a reduced cadence (skip-next-window logic). Main counts 7,159 active-GameObject MonoBehaviours / 6,585 enabled, 79 animators and 81 particle systems; remote 7,925 / 7,109, 74 animators and 86 particle systems. Counts alone do not establish leaking gameplay controllers or explain a new regression.

Log payload is large and predominantly diagnostics:

| Category payload (UTF-8 decoded lines, excluding CR differences) | Main | Remote |
| --- | ---: | ---: |
| WallSegmentFade | 44.82 MB | 44.64 MB |
| Cards | 33.11 MB | 8.67 MB |
| WorldUI | 23.09 MB | 10.25 MB |
| Net | 8.26 MB | 8.98 MB |
| Perf | 3.02 MB | 2.58 MB |

The repeated Cards `BOARD ANCHOR` message alone accounts for approximately 30.18 MB main / 6.07 MB remote. Log formatting, allocation and sink I/O are plausible avoidable costs, but no log-sink timings or controlled diagnostic-on/off comparison exist. 200 MB of files does not by itself prove disk or streaming contention. Preserve useful event evidence while reducing repeated prose and rate-limiting unchanged diagnostics; measure the sink if claiming an I/O bottleneck.

## Integration recommendations

1. Preserve the measured distinction between recurring mod CPU cost, occasional broad board/card/environment bursts, and remote runtime pacing changes. Streaming remains an unverified explanation, not an established cause.
2. Add narrow CardAppearance capture/apply/build scopes and remove its source-proven redundant temporary allocations without delaying animation sampling or mutating published snapshots.
3. Keep NativeRevision's existing per-source/per-frame cache. Profile its hierarchy/property reads before changing them; never optimize by hiding native changes for a polling interval.
4. Narrow the board/card rebuild scopes so the next 50–140 ms burst identifies clone construction, binding, appearance application, widget sampling or another operation.
5. Reduce repeated diagnostic payload and measure diagnostics separately. Do not use diagnostic boilerplate's “not the mod”, “waiting” or “session dead” claims as measured root causes.

The initial performance review was documentation only. Hardware improvement, absence of rendering regressions, and a causal comparison against a pre-change build remain unverified.


## Follow-up: native element playback material provenance

Additional delegated investigation found one native-element warning in the remote log at **17358**, around frame 46935, during gameplay. The following line explicitly reports **MIRROR BLANK (3 of 3), via=NATIVE-PENDING**. Line 17366 again reports MIRRORED-WIDGET. This was a real transient presentation loss, not just startup initialization. The log does not identify the failing element/material, nor precisely count blank frames.

Source-proven defect: `RemoteNativeElements.Element.ValidateMaterial` checked the clone graphic's current material, and `Graphic` instantiated owner FX from that clone material. `RemoteWidgetMirror.Pair.Apply` deliberately skips inactive viewer branches before copying their materials. Such a clone is not a reliable material source when the owner's frame activates a currently inactive viewer effect: stale/default materials can reject the entire six-element frame. This dependency is proven in source; the sparse warning cannot prove which particular graphic took it in the hardware incident.

The correction uses the exact original graphic retained in `NativeElementBindings.Graphics/Effects` for material validation and clone-owned material creation, independently of viewer branch activity. A changed original material reference invalidates and disposes the old owned copy. All `_FXAnim` writes still target owned copies; no game callback or substitute shader is introduced. Truly unavailable original materials remain a refusal, now naming element, graphic and shader instead of erasing that evidence.

`NativeElementMaterialVectors` guards both binding populations, stale-clone rejection, original-material replacement and disposal, and original-material write isolation, with deliberately broken negative controls. These are source-bound ownership regressions, not a GPU rendering test. Validation: Release build succeeded with zero warnings/errors; the complete WireTests run with temporary worker-local registration passed 251,760 assertions. The registration is master-owned and must be added during integration. The next hardware run must verify element animation continuity.
