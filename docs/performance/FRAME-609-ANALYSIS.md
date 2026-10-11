# Build 609: Steam Frame resolution and remaining hitches

Both captures identify **609 / 24400128c**, built 2026-10-03 05:58:14 UTC.
They measure the desktop application containing the 608 changes, independently
of later dev/610 work and the separate Quest diagnostic. Within each capture,
Player.log and LogOutput.log duplicate one run. Three inspected normal-resolution
screenshots show Windumtostes Hochland and its drakes; no low-resolution image
was supplied. The user's unwanted blur is a separate visual acceptance result.

Nine immutable input hashes, all 51 paired FRAME windows, original parser
exclusions, settings/application timelines, scope summaries and approximate
comparisons are retained under `.planning/debug/frame609-run-analysis/`.
The extraction supports `--repo-root`, `--input`, `--output` and relocated
checkout discovery. Anchors below are universal-newline `LogOutput / Player`.
No runtime or parser changes were made for this analysis.

## Resolution: a modest, plausible improvement during normal VR motion

Normal uses **3408×3408** per eye; low **2256×2256**, both scale 1.0 and MultiPass.
Low requests **56.18% fewer eye pixels**, 10.18 versus 23.23 million across both
eyes. Both keep Fastest, shadows/AA off, grass/decoration/vegetation/environment
effects zero, floor chunks and simpler shading on, full texture resolution and
forced anisotropic filtering. This is not a measurement of GPU busy time.

Only loaded ProcGen windows with tracked heartbeat evidence, steady figure tags,
players/enemies 0/0, effects 0 and cloth false enter the aggregate. Preparation,
mixed scenes and unknown tracking remain excluded. Natural head motion is part
of the intended workload; it is not a reason to discard these observations.

| Capture | Windows / frames / seconds | Frame-weighted mean | Window-p95 median / range | Mod average |
| --- | --- | ---: | --- | ---: |
| Normal | 22 / 6558 / 358.3 | 54.70 ms | 94.83 / 78.61–161.64 ms | 15.67 ms |
| Low | 8 / 2744 / 140.4 | 51.21 ms | 85.29 / 80.58–117.19 ms | 15.91 ms |

The observed mean is 3.49 ms lower; removing each first steady window gives
54.55 versus 50.13 ms. Window-p95 ranges overlap. These p95 statistics are not a
pooled frame percentile. Normal contains more actions and initially reports
17 admitted actors, later 18; low already reports 18.

The following moving-view approximations keep the same quality, reported XR
rate and later 18-actor source census, median height/distance differences within
3/5 world units and visible-renderer medians within 10%. They support the user's
faster impression without requiring a stationary headset:

| Normal / low windows | Reported Hz | Visible medians | Mean normal → low | Window-p95 normal → low |
| --- | ---: | --- | --- | --- |
| #24 / #15 | 12 | 1555 / 1701 | 55.41 → 52.89 ms | 95.03 → 86.91 ms |
| #26 / #13 | 12 | 1464 / 1326 | 58.61 → 49.94 ms | 104.05 → 84.88 ms |
| #29 / #10 | 24 | 1059 / 1084 | 57.00 → 47.17 ms | 105.03 → 83.96 ms |

Directional confidence is moderate: all three favor low, by roughly **2.5–9.8 ms
in these views**. Exact benefit has low confidence because actions, orientation,
zoom and overlapping view distributions differ; the brief windows have sparse
heartbeat evidence. The strict parser's pose-stability rejections are retained,
while a separate analysis explicitly permits ordinary motion. These are
observed time differences, not isolated FPS percentages.

A small improvement despite such a large pixel reduction suggests several
limiting costs. Measured logic is 29.16/28.02 ms, render-loop 9.42/8.78 ms and
unbracketed remainder 16.12/14.41 ms. Mod costs remain about 16 ms/frame, with
much larger synchronous outliers below. The remainder is unknown engine
work/waits, not GPU busy. Loaded XR refresh reports fluctuate among 12/18/24
and occasionally 72 Hz despite startup reporting 72; this changes budgets and
confounds pacing comparisons, without proving a physical panel-rate change.

## Figure sliders: meaningful geometry reduction, weak timing direction

Actual application reports at the later 18-actor population establish:

| Player/enemy detail | Derivatives | Original/current admitted vertices | Native LOD caps | Player line |
| --- | ---: | --- | --- | ---: |
| 0/0 | 39 | 297913/193890 | 9/9 | 15605, 25574 |
| 0/100 | 16 | 297913/260673 | 4/9 | 23142, 25535 |
| 100/0 | 23 | 297913/231130 | 5/9 | 24001 |
| 100/100 | 0 | 297913/297913 | 0/9 | 24778 |

Thus 0/0 removes **34.92% of admitted body vertices**; the player contribution
is 37240 and enemy contribution 66783 vertices at this population. All retain
15 disabled cloth solvers, 53 masked optional figure-FX renderers and 38 paused
particle solvers. Low's application at Player 6664 matches the later 0/0 state.
These counts include inactive LOD slots and are not submitted GPU vertices.
Transient native action effects still run outside that optional-part budget.

Closest moving-view approximations with the same reported rate and population
have inconsistent timing signs. Enemy-full #32 versus 0/0 #24 costs 1.92 ms
more, but another nearby 0/0 baseline gives 1.28 ms less. Player-full #34 and
full #35 are about 4.7/4.8 ms faster than nearby 0/0 #27, whereas another nearby
baseline makes them 4.3/4.2 ms slower. The geometry benefit is proven and can
reduce rendering work; a general FPS benefit is weakly supported by these
noisy timings. It would be equally unjustified to call the sliders ineffective
or to assign them a precise FPS saving. Low has no slider sweep; #31's missing
tracking evidence remains excluded.

## 608 fixes and measured remaining work

The exact SIGNATURE DELTA prefix yields 21 normal events: two during loading,
one initial setup and **18 after the first complete steady window**. Low has
two loading events and one setup event, none later. No named folded row in
those 18 normal reports repeats the repaired native-named ghost body/LOD or
HexHighlight-root pattern. Remaining sets principally name native stun,
shield, heal, Brute wave and footstep effects, plus native `Title` twice.
This supports the targeted 608 fixes; bounded row lists and different action
sequences prevent a claim of complete wall-hitch removal.

Settled BUDGET records report normal 110 judged / 89 skipped / 21 committed
cycles, versus low 34/32/2. Worst commits remain **170.41/191.80 ms**. The
counts describe reported cycles, not an equal-work experiment. Native effect
membership still invalidates the table; any new exemption needs exact owner
and wall-shader checks.

Source confirms Debug-only census staging and eight-face/1 ms sampling checks
(`Cards/Art/CardHalfTone.cs:1094,1100,1115`), while discovery remains one
synchronous `Resources.FindObjectsOfTypeAll` (`1136`). Loaded hardware discovery
is **46.20–72.85 ms normal, 48.93–51.44 ms low**. The reported normal sample
worst is 0.77 ms; low has a 1.07 ms sample. Missing small Top/TAIL entries do not
mean zero work. Staging removes an amplifier but discovery remains expensive.

These examples exclude loading and immediate setup. Scopes are inclusive:

| Capture / frame | Whole frame | Named work | LogOutput / Player |
| --- | ---: | --- | --- |
| Normal 4425 | 228.14 ms | Cards.Driver 125.89 ms | 2491/7479 |
| Normal 4580 | 222.51 ms | Driver 80.53; VRCardArt 32.03 ms | 2748/7871 |
| Normal 7018 | 220.65 ms | WallFade.Rescan 170.39 ms | 5027/13348 |
| Normal 10820 | 270.76 ms | UiClick 147.46 ms | 9207/23885 |
| Normal 6197 | 97.66 ms | HalfTone.Discovery 58.71 ms | 4437/11872 |
| Low 4791 | 227.09 ms | Cards.Driver 154.44 ms | 3298/8366 |
| Low 4624 | 168.43 ms | Driver 60.55; VRCardArt 35.96 ms | 3127/8065 |
| Low 5003 | 344.63 ms | EnemyRevealSurface 92.00 ms | 3716/9378 |
| Low 4143 | 124.50 ms | NearGrip 58.42 ms | 2658/7540 |
| Low 5030 | 87.20 ms | HalfTone.Discovery 48.93 ms | 3765/9440 |

The two VRCardArt examples immediately follow fresh Elementalist Background
readback and Top/Bottom mip creation after loading. This is real later first-use
work, independently of irrelevant initial loading captures. Arrival scopes also
contain other art maintenance; not every millisecond is proven readback.

Some settled frames remain huge with little named mod time: normal 6509 is
705.14 ms with 12.75 ms mod (`4610/12233`), low 6035 is 658.59 ms with 13.97 ms
mod (`5358/11520`). Their cause is not localized by the historical SPIKE verdict.
An unfinished tail is retained without inventing another FRAME mean. No mod
Error/Fatal is logged. Managed heap samples repeatedly fall; positive heap
changes and window-level collection counters do not establish a leak or the
cause of a particular hitch.

Priority is (1) the measured synchronous Debug discovery, preserving complete
native/pooled/clone coverage; (2) exact native-effect wall ownership and coarse
Driver/UiClick/EnemyReveal callback costs; (3) asset-only loading preparation
for later class-art misses, preserving the first visible frame. These targets
address measured work independently of natural head motion. Resolution can
provide some relief, but the unwanted blur need not be accepted to continue
reducing those costs.
