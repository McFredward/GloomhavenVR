# Build 496 hardware performance review for build 497

## Evidence and method

Reviewed on 2026-09-10 from dev `1a714ee279d2e01c0d4e8efba6f1e4926e175ace`.
This report contains measurements only; it makes no runtime changes.

- Main checkout `.planning/debug/LogOutput.log`: 151,693,302 bytes, 104,823 lines.
  Build banner at line 17 is **496**, assembly 0.9.1.0; line 70 identifies `1a714ee27`.
- Main checkout `.planning/debug/Player.log`: 195,284,471 bytes; supplementary game/shutdown evidence.
- The local session census changes to **two modded players, both 496**, at LogOutput line 7506.
  This is one remote player/board, not a four-player scaling test. No current remote log was supplied;
  the files under `remote/` and all supplied JPGs predate this run and are excluded.
- There are 173 completed FRAME windows covering 5,153.6 reported seconds (85.9 minutes),
  including loading, an earlier scenario view, and the map/lobby. The sustained multiplayer scenario
  starts after the load around lines 23230–24988. Analysis starts at FRAME line **25384**, the first
  completed window with an established scenario census. Summed report durations are an approximate
  timeline, not a precise wall clock.
- Means below are weighted by FRAME `n`. Named step contribution is `avg * frames / total n`,
  using both STEPS and STEPS TAIL. Nested scopes must never be added to their parents. A step
  below the logging threshold is unreported, not zero; its derived aggregate is a lower bound.
- Prior evidence comes from the retained `perf491-time-review.md`, `sp492-vs-mp491.md` and
  `perf491-memory-review.md` in the ignored debug folder, and [MP-PERFORMANCE-493.md](MP-PERFORMANCE-493.md).
  Their historical input line numbers describe the old files, not today's replaced LogOutput.

## Frame pacing is good for most of this longer session

Normal scenario analysis uses FRAME lines 25384–104406, excluding the explicitly separated runtime
rate-change episode at 61915–62478. This leaves 119 complete 30-second windows (59.5 minutes,
314,499 frames), with **11.35 ms/frame** on average. The 90 Hz budget is 11.11 ms.
The instrument records 1,688 frames above its two-budget spike threshold, **0.54%** of these frames.
This agrees with the user's report of a mostly smooth session, while preserving evidence of brief stalls.

| Frame-weighted metric | Before second room | Second room, settled |
|---|---:|---:|
| FRAME lines | 25384–91859 | 94834–104406 |
| Analyzed duration | 52.5 min | 6.0 min |
| Frame interval | 11.21 ms | 12.69 ms |
| Update-to-LateUpdate logic | 3.44 ms | 5.82 ms |
| Camera cull/submit | 1.29 ms | 2.14 ms |
| Named depth-zero mod work | 2.56 ms | 4.09 ms |
| Frames above twice the 90 Hz budget | 0.36% | 2.11% |
| Largest individual frame | 354.55 ms | 92.36 ms |

The second logical room opens at **92721**. The active-renderer census grows from **2,190**
(line 91865) to **3,345** (96217), while mod-layer renderers stay essentially level, **148 → 147**.
The increased late workload has this concrete scene change; it is not evidence of accumulated boards.
Windows 92829 and 93751 are excluded from the settled post-door column because they include the transition.

The first and last ten pre-door minutes average **11.15 → 11.29 ms/frame**. CPU logic rises
**2.80 → 4.09 ms** and Net.Board **0.80 → 1.19 ms**. These are not identical workloads:
all first-ten-minute windows end in selection; the last ten are mostly action selection, with native
face/effect application active. Camera pose and UI populations also differ. Frame pacing remains
near 90 Hz; the CPU growth should not be dismissed, but elapsed time alone does not establish its cause.

### Isolated runtime-rate episode

At line 61914 the reported display rate changes **90 → 72 Hz**, returning to 90 at 62486.
Two full windows (62098 and 62284) average **100.29 ms/frame**, followed by a mixed 57.26 ms window.
Their logic costs are only **7.42 / 7.59 ms** and render costs **0.64 / 0.64 ms**; approximately
**92 ms** remains outside those measured spans. The next 30-second window returns to **11.20 ms**.

This is a discrete roughly 90-second episode, not progressive gameplay CPU collapse. The logs do
not identify whether the headset/runtime was paused, lost focus or waited for another reason.
The instrument's explanatory text equates a rate change with reprojection; the observed rates alone
cannot prove that interpretation, particularly when the actual interval is around 100 ms rather
than the 72 Hz budget. There is no runtime GPU-time counter in this log, and the residual also
includes unmeasured Unity/canvas work. It must not be described as measured GPU time.

## Multiplayer presentation costs are substantially smaller in this run

| Nested scope, ms per all analyzed frames | Before second room | Second room, settled |
|---|---:|---:|
| Net.Avatar | 1.077 | 1.785 |
| Net.Board | 0.928 | 1.637 |
| Net.Board.NativeRevision | 0.232 | 0.515 |
| Net.BoardMirrors | 0.492 | 0.705 |
| Net.Board.ContentRefresh | 0.068 | 0.148 |
| Net.Board.NativeRefresh | 0.052 | 0.097 |
| Net.Presentation.NativeSend | 0.300 | 0.379 |

All listed scopes appear in every selected window, so their rounded aggregate does not depend on
missing tail entries. `NativeRefresh` is part of `ContentRefresh`; board mirrors/revision/content
are inside `Board`, which is inside the avatar work. NativeSend is separate owner work.
Actor refresh, appearance sampling/application and transport also have new measurements, but are
below the printing threshold in some windows and should not be summed as a complete network budget.

For historical context, the four two-room build 491 comparison windows averaged **Net.Board 3.071 ms**,
including **NativeRevision 1.482 ms**, with **22.37 ms/frame** overall. Current board/revision
readings are clearly lower. However, the old scene had around **5,400 active / 4,000 visible** renderers;
this run has around **3,350 active / 2,000 visible** after the door. Earlier room populations also
roughly halve, from 4,100 to 2,180. The view, scenario, phase mix, original UI hierarchy and session
activity are not matched. The readings are consistent with build 493's removal of redundant work;
they cannot isolate its exact percentage gain or justify a universal FPS improvement claim.

Both runs use Fantastic, AA4, multipass, 3072×3264 per eye and normally 90 Hz. Matching these settings
does not compensate for the different scenario/workload. The old short singleplayer run likewise
cannot supply a matched multiplayer overhead for this new scenario. Four-board Unity/headset scaling
still requires a run with that actual population.

## Memory grows, without evidence of accelerating GC collapse

SPIKE `heap` uses `GC.GetTotalMemory(false)`; it includes retained caches and uncollected garbage,
not process working set, GPU memory or a measured post-collection live-object baseline. Sampling
is biased toward spikes. `alloc` accumulates positive deltas of that counter and is only a churn
proxy. The following quarters split the scenario's reported timeline; the runtime-rate episode
is excluded, so Q3 has fewer analyzed seconds.

| Quarter / FRAME lines | Seconds | Heap sample count | Observed min / median / max MB | Gen0 collections/min | Positive heap delta MB/s |
|---|---:|---:|---:|---:|---:|
| Q1: 25384–46829 | 900 | 85 | 591.2 / 617.8 / 665.1 | 4.87 | 3.96 |
| Q2: 47601–61000 | 930 | 190 | 604.6 / 644.5 / 710.5 | 10.90 | 9.89 |
| Q3: 61131–79832 | 810 | 258 | 616.9 / 657.2 / 732.6 | 12.30 | 10.92 |
| Q4: 80988–104406 | 930 | 599 | 650.2 / 725.5 / 848.6 | 9.16 | 7.50 |

The managed atlas cache grows only **~225 → 235 MB** (last preceding cache events at 24585 and
93052), so that known bounded cache does not account for all sampled heap growth. Its native/GPU
bake budget is a different memory domain and must not be added to the managed heap.

Before the door, active behaviours grow **5,199 → 7,775**, but Update/LateUpdate populations only
**249/31 → 279/39** (SIM lines 25389 and 91864). Mod-layer renderer counts remain **135 → 148**.
Mod-layer graphics fluctuate from **94**, through a burst of **377**, back to **137** immediately
before the door; they do not monotonically accumulate. All active graphics grow **1,260 → 1,927**.
This warrants continued retention/ownership profiling, not an assertion that every new behaviour
is a leaked remote ticking controller. The census excludes inactive pooled objects and does not
inventory all native materials/textures.

Unlike the previous 491 review, this run begins with a long selection period, so the early low
GC rate is not a comparable action-workload baseline. Q4 GC/churn falls from Q3 while the sampled
heap rises. The available counters contain no GC pause durations. They neither establish nor
rule out a memory leak. Good observed frame pacing and unresolved retention can coexist.

## Other log findings

- No `PACKET REJECTED`, `TRANSPORT TIMEOUT`, `PEER STALE` or `BURN ANIM STUCK` occurrence in the
  current LogOutput. The local census establishes one modded peer; this is not a remote-side audit.
- Four real `MaterialLoaderHeal` errors, lines **24841–24842** and **93551–93552**, concern the same
  `ST_Cult_Clutter_Chain` material asset `006219c000f35b743af0e8e17acc9ed4`. The five-retry null-result
  recovery leaves these renderers hidden. They coincide with scene/room construction, not a repeating
  exception each frame. The log does not establish why that game asset fails to load.
- At shutdown, line **104815**, `WorldUI.Shutdown.UseBars` catches a `NullReferenceException` in
  `RemoteOriginalDecisionPrompt.Destroy:178`, through `CharacterDecisionMirror.Destroy:119` and
  `UseBarsSurface.Shutdown:409`: access to an already destroyed Unity component's `gameObject`.
  This is a concrete teardown defect, not evidence of the in-session board motion problem.
  Player.log 782108 has a preceding anonymous shutdown NullReference without an attributable stack.
- Covered-fan `ARC ORDER NOT APPLIED` readings repeatedly report `no fronts` (for example 27986
  and 32301), alternating with `none stated`. They support investigating the reported remote
  selection-pluck ordering; the diagnostics' cumulative stated/applied totals are not failure rates.

Reproduction artifacts are private `/tmp/mp497-perf.py`, `/tmp/mp497-memory.py` and
`/tmp/mp497-summary.py`, with extracted JSON and summaries beside them. They parse decimal comma
or dot and assert FRAME alignment when combining the timing and memory rows. Raw user logs remain
ignored; only this bounded evidence summary is committed.
