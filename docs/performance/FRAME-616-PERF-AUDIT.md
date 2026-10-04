# Build 615 Steam Frame capture: improvement and remaining stalls

The supplied files identify **ModBuild 615 / 64db88dc9**, Unity 2021.3.5, D3D11
through Turnip Adreno 750 and wineopenxr64, with SteamVR/OpenXR 2.17.10 in
MultiPass. Frame standalone defaults are positively selected. Both eyes remain
3408 × 3408 at resolutionScale 1.00, one MSAA sample. `Player.log` contains the
same 29 FRAME summaries plus Debug-severity readbacks; it is another sink for
this run, not another sample population. No new screenshot/video is present
in the supplied `steam_frame` directory.

Immutable input copies, hashes, the original repository parser and a reproducible
supplemental extraction are retained under
`.planning/debug/frame616-review/log-audit/`. Line references below use LogOutput unless
explicitly labelled Player. The extraction never combines sinks or window p95 values.

## Loaded gameplay comparison

Unlike Build 612, this capture actually visits CampaignMap before the scenario.
Its map spinner hides at line 1831; the scenario spinner hides at line 6533.
Scenario FRAME 6841 still covers initial publication/cold work and is excluded
from the steady comparison. Seven following summaries, lines 7106–9374, cover
2223 frames over 110.2 s. Loading, preparations and the partial end interval are
not included in this aggregate.

| Loaded scenario | Frame mean | Mod scopes | Logic span | Render span | Unbracketed work/waits |
| --- | ---: | ---: | ---: | ---: | ---: |
| Build 612, 7164 frames | 53.20 ms | 17.81 ms | 31.67 ms | 7.76 ms | 13.77 ms |
| Build 615, 2223 frames | 49.60 ms | 15.53 ms | 28.13 ms | 6.14 ms | 15.33 ms |

The observed frame mean improves by 3.60 ms, about 6.8%; measured mod scopes fall
by 2.28 ms, about 12.8%. This agrees with the maintainer's subjective scenario
improvement. View motion and game actions remain legitimate evidence, but the
different sessions do not isolate each change's saving. The reciprocal mean is
about 20 application frames/s, not headset/compositor FPS. Mod scopes are inside
logic and must not be added to it. The unbracketed residual becomes larger,
not smaller; it includes unmeasured engine work, waits and scheduling.

Each window's p95 remains 75.25–98.21 ms. The worst summarized frame is 1000.71 ms.
The log continues after the final summary with severe individual frames up to
5952.09 ms; these are retained separately, never silently folded into this
aggregate or reconstructed percentiles. There is no measured application GPU
busy counter. XR's `gpu` field remains interval/wait evidence; neither it nor
the residual establishes a GPU cause.

Fourteen loaded map windows, lines 1875–5345, contain 4737 frames over 219.5 s:
mean 46.45 ms, mod 23.00 ms, logic 31.76 ms and render 4.49 ms. Their changing resident
detail/interaction population makes the declining cost over this map visit
unsuitable as a per-setting A/B test. No earlier Build 612 map measurement is
available for comparison.

## Which Build 615 optimizations actually applied

- **Wall invalidation:** thirteen post-first-window budget reports, lines 6966–9393,
  show 24 of 24 judged cycles skipping the atomic table publication. There is no
  stable-play atomic commit in those reports, compared with twelve measured
  commits in Build 612's longer scenario capture. This is strong evidence that
  redundant publications no longer dominate this captured interval, rather than
  proof that every avoided commit was caused by one specific exemption.
  Earlier initial/first-use commits still cost 312.81 and 272.53 ms; their memo
  reports 3916/3967 exact native mesh-bound reads and 10073/10426 reused reads.
  Same-publication caching works but does not turn atomic publication into an
  incremental operation. The actor-particle signature exemption is unconditional;
  the older `FigureExemptSkip=OFF` message describes a different optional gate.

- **Health bars:** 6991 of 37689 pose-check opportunities skip bone verification,
  about 18.5%, compared with zero skipped checks in Build 612. ActorBars.Late
  measures 1.903 ms/frame here versus about 2.06 ms/frame previously. There are 11085
  eligible and 26604 unsafe-rig evaluations. Five native actors are admitted;
  twelve remain conservative. Repeated refusals identify `CInteractableActor`
  on three heroes, `CInteractableActor` plus `SummonAppear` on Elementalist,
  and `SummonAppear` on eight demons. Interval 0.100 s is actually reported.

- **Optional idle transform culling:** 37689 tracked evaluations all report
  authored culling, zero original AlwaysAnimate and zero newly applied transform
  culling. The option does not buy a new native-idle saving in this scenario.
  Do not interpret the tracked count as the number of newly optimized actors.

- **UI maintenance:** the cached paths execute. InitiativeTrackSurface.Late costs
  about 0.189 ms/frame; HiddenWindowVeil.Discovery 0.717 ms/frame and
  PanelMipBake.Arrivals 0.472 ms/frame remain steady work. CanvasConversion.Fit
  runs on 1265 of 2223 frames, about 57%, with an inclusive mean contribution of
  0.602 ms/frame. The incomplete GFX inventory supplies no saved-key readback for
  the new UI intervals, so their precise effective values cannot be inferred
  from shipped defaults. These scopes are nested and must not be summed.

- **Diagnostic slicing:** Perf.SceneProfileSlice measures 0.182 ms/frame,
  worst 0.78 ms, instead of Build 612's isolated synchronous 91.6–119.9 ms walks.
  However, it completes **no SCENE/SIM/GFX inventory at all** in this capture:
  all 23 loaded summaries say `incremental census in progress`. This is an
  instrumentation defect discussed below, not proof that inventory populations
  dropped to zero.

- **Structural chunks:** Player 15409 reports 17 compatible surfaces, 17 unreadable
  original meshes, zero source renderers/chunks and one simpler material.
  Structural batching is requested but admits no chunk here. It contributes no
  demonstrated renderer-submission saving. No instancing application report
  establishes that the separate experiment was enabled.

The existing scenario reductions are still applied: 17 actors, 36 verified
derivatives, 278708 original/current admitted body vertices reduced to 120300,
20 far5 slots, 15 cloth solvers disabled, 53 optional FX renderers masked and 38
particle solvers paused (Player 15436). These are mesh/inventory counts, not
actual eye triangles or draw-call measurements. No repeated global skin-weight
repair storm appears in this run.

## Specific remaining stalls

Frame 9779 lasts 217.14 ms with 159.07 ms of named mod work. The new hand scopes
resolve an actual pickup: NearGrip.Pickup 105.12 ms and
GhostConstruction 48.32 ms. These are inclusive/nested timings, not separate
costs to add. Frames 9786 and 9890 contain StatPanelSurface bursts of 68.25 and
78.53 ms. Cold original figure/preview construction remains a plausible target
for earlier preparation; delaying hover or contact would change interaction.

Other substantial stalls have very little named mod work: frame 10394 is
660.45 ms with 19.16 ms mod work; 11804 is 1000.71 ms with 19.23 ms; 11805 is 664.32 ms
with 37.06 ms. The summary covering frame 11804 has a render maximum of
15.37 ms and a logic maximum of 287.38 ms. These maxima need not occur on the same
frame; neither bracket alone accounts for that one-second frame. The unfinished tail reaches 5952.09 ms with 96.06 ms named mod work.
The log does not identify the responsible native subsystem, driver wait,
compositor pause or OS scheduling event. A stopwatch scope can also contain
preemption; a simultaneously slow collection of unrelated scopes is not proof
that all their algorithms became expensive.

Camera suspension is effective: every loaded scenario SPLIT contains only the
two HeadCamera eye passes, not a repeatedly discarded flat camera. All 37 native
managed camera readers retain their original bridged projection. Extra
PanelSSCam passes during map/options artwork capture have a visible purpose;
they are not a demonstrated camera leak. The sampled native callback block
does not reveal a comparable hundred-millisecond culprit in stable play.

Player logs nine Hydra endpoint DNS failures, including three after scenario
loading. They belong to the game's native online-service registration/retries;
no mod exception/deadlock is established. Their ordering alone cannot assign
the unexplained long frames to networking. Heap increases and the single
session do not establish a memory leak.

## A diagnostic lifetime conflict to correct

`PerfMonitor.MarkChange` unconditionally calls `PerfSceneProfile.Cancel` before
closing its timing window. The runtime reports frequent display-rate changes,
often after 10–30 s. Each display-only boundary therefore destroys the unfinished
incremental traversal, even when the loaded scene and profiling configuration
are unchanged. Inventory work is budgeted per node, component and child, at 64
units/frame; a low-FPS large hierarchy can outlive that boundary. No scene
profile fault latch is logged. This ordering provides a source-supported
explanation for the persistent progress summaries; the capture has no iterator
progress counters to exclude every additional cancellation cause.

Preserve the diagnostic job across display-only timing boundaries while retaining
true scene/load/config cancellation and exact FRAME resets. Bounded Debug
progress should report stage, completed units, slices and elapsed capture span.
It must not add per-frame logging or interpret asynchronous counts as one instant.
This restores evidence; no hardware saving is claimed before the next run.

## Native admission and wall-fade follow-up

The exact native `CInteractableActor` contains empty Awake/Update methods and
inherits empty hover callbacks from `CInteractable`. Start only resolves its
CharacterManager actor reference or destroys the component outside a scenario;
ShowNormalInterface dispatches the native tile-selection callback. It writes
no skeleton, root transform, scale, skin, material or constraint. Exact-type
admission is source-backed, preserving unknown-subclass refusal and the existing
root/state/palette invalidators. `SummonAppear` instead controls native material
cutout, particle creation and coroutines; a disabled-component shortcut is not
safe because native CharacterRevealScript directly restarts its coroutine.
Any separate admission needs the original effect-path fixture rather than
assuming a name proves harmless behavior.

The wall pop is not an approved compromise. At line 9158 / Player 18326, Wall 25
actually includes `GloomhavenVR/ScenarioSimpleEnvironment(toggle-native)` in its
shader set; its delivery report has no dissolve/alpha channel. Structural chunk
count is zero, so chunk replacement cannot explain this appearance. Settled
`HELD` animation samples alone do not prove a broken transition; this live
simple-shader wall membership is the concrete renderer/material investigation
target. Preserve animated wall presentation while keeping cheaper static
surfaces behind their proven admission boundary.
