# Steam Frame Build 606 hardware analysis

Internal engineering report, 2026-10-02. The maintainer reports a much smoother,
nearly playable scenario. This report assesses the supplied measurements; it does
not equate that observation with a controlled before/after FPS result.

## Evidence

Both new game logs identify **ModBuild 606, 8d7b8d7c2**. Immutable copies, input
hashes, parsed windows and independent cost/figure analyses are retained under
`.planning/debug/frame606-run-analysis/`. The previous evidence is retained under
`.planning/debug/frame605-run-analysis/`; see [Build 605](FRAME-605-ANALYSIS.md).
Numbered anchors below use Python universal-newline numbering.

The new run retains 3408 x 3408 per eye, MultiPass and Fastest quality. Figure,
vegetation, decoration and effect budgets start at zero, with cloth disabled.
The spectator is **LEFT EYE**, not black; discarded native flat draws are now
suppressed independently of this spectator choice. New floor chunks and simpler
floor shading are enabled, and the environment-effects budget is zero.
Scene registration alone does not establish a visit to the 3D map. The assessed
loaded windows are in ProcGen; loading/generation windows are excluded.

## Measured geometry effect of the sliders

Detailed mesh evidence appears in **Player.log**, even where LogOutput.log does
not contain the Debug entries. All samples below admit the same 17 actors:

| Player / enemy detail | Derivative mesh slots | Original / current admitted body vertices |
|---|---:|---:|
| 0 / 0 | 36 | 278,708 / 181,529 |
| 100 / 0 | 23 | 278,708 / 211,925 |
| 0 / 100 | 13 | 278,708 / 248,312 |
| 100 / 100 | 0 | 278,708 / 278,708 |

Player.log:6652, 8589, 9436 and 9505 record these respective states. Repeated
zero samples at 8640 and 9693 confirm application after restoration. No missing
index, bank or derivative-load failure was found. At 100/100, the native LOD caps
and exact original meshes are restored.

The player slider removes **30,396 vertices** from 13 admitted mesh slots; the
enemy slider removes **66,783** from 23. Together the new derivative ledger is
**97,179 vertices smaller, or 34.87%**. This ledger includes eligible inactive LOD
slots; it is not a count of vertices submitted to the GPU in each frame, and it
does not establish a 34.87% FPS gain. Native LOD capping is an additional, separate
mechanism; do not add its table counts to this ledger as another measured saving.

At zero, the generator selects the strongest prepared tier. Its nominal 20%
triangle target is constrained by UV seams, material boundaries, skinning and
silhouette preservation; it does not guarantee 20% vertices or remove the body.
The meshes retain original textures, materials, bones and survivor attributes.
Consequently subtle appearance differences are expected and desirable. For
example, the catalog's WindDemon_main derivative reduces 11,120 to 5,326 triangles
and 7,652 to 4,755 vertices. This is a catalog example, not a per-renderer live
identity measurement in the supplied run.

The added package cost is substantial: the 49 banks occupy **294,017,137 bytes**
installed and **250,866,985 compressed bytes** in the current release ZIP,
approximately 56% of that 446,748,051-byte archive. These banks cover 409 sources
across the game and three quality tiers, rather than this one scenario alone.
The initial zero-detail scenario opens four banks totaling **19,658,570 on-disk
bytes**, caching 88 unique derivatives for 36 assigned mesh slots. The explicit
slider experiments subsequently open 13 banks totaling 69,059,124 bytes and cache
265 derivatives. Banks load their whole mesh group and remain resident for process
lifetime; the bank byte totals are **not** runtime RAM/GPU-memory measurements.
See `mesh-package-cost.json` in the retained analysis evidence.

Geometry savings are proved, but the download increase is not yet justified by
an isolated hardware frame-time benefit. Mesh simplification does not reduce
renderer count or eliminate native animation/game logic. A CPU/submission-bound
view may benefit little from fewer triangles. A stable slider A/B measures the
combined slider behavior (authored LOD caps plus derivatives); it cannot isolate
the derivative-only benefit unless those original caps are held identical.

Build 605's visible-body counter read cached original mesh sizes; Build 606 reads
the actual current shared mesh. Therefore the historical 39/125,614 and new
39/87,647 visible-body totals are **not** a clean instrumentation-equivalent A/B.
Within 606, even equal visible mesh counts do not prove identical mesh membership
or view. Slider changes fall inside measurement windows containing movement;
they do not provide a controlled figure-only frame-time comparison. Actual GPU
busy measurements remain unavailable.

## Which changes have timing support

The strongest direct timing support is the removal of discarded native desktop
drawing. DRAW SKIP is explicitly engaged in the new run. In the first loaded
measurement blocks, the approximate frame-weighted camera callback spans are:

| Main-thread callback span | Build 605, windows 7-9 | Build 606, windows 8-10 |
|---|---:|---:|
| Native ScenarioCamera | 3.24 ms | 1.06 ms |
| Native UI Camera | 1.42 ms | 0.25 ms |
| Head camera, including both passes | 4.47 ms | 5.35 ms |

The native camera pair is approximately **3.36 ms cheaper** in these observed
blocks. Across all selected loaded windows its reduction is approximately
3.73 ms. The cameras still execute necessary clear/effect/callback work; they
do not disappear. These are CPU callback brackets, not exclusive GPU timings or
a promised equal reduction in whole-frame time. The head-camera increase also
shows that the views and workload differ.

WallFade.Late is lower: the previous accepted loaded baseline is about
3.57 ms/frame; the new pre-vegetation-retuning windows are about 1.98 ms/frame,
and all new loaded windows about 2.52 ms/frame. The source changes remove
selection-only wall rebuilds and unused preparation for masked scenery and cache
pure geometry. This supports a contribution from cheaper wall processing, but
does not isolate each change from the different view/settings histories. Genuine
geometry/vegetation changes still produce atomic commits: LogOutput.log:5057 and
5234 report approximately 204 and 195 ms. Hitch-free play is not established.

SceneryBudget.Update is also lower in the broad observations, about 1.14 to
0.72 ms/frame, but settled samples remain about 0.17-0.20 ms in both runs.
Different preparation/hold histories prevent attributing that aggregate change
specifically to the targeted held-root rescue. Canvas fit/hit-rectangle work stays
close to its previous cost, and broader CanvasConversion/Late spans can increase.
The run does **not** support
attributing a large improvement to the UI memoization alone. Step timings can be
nested; they must not be added to the total logic span.

Floor batching contributes **no measured draw reduction in this scenario**.
Player.log:6627 reports 17 compatible surfaces, all unreadable, and **zero batched
source renderers / zero chunks**. Originals are safely retained. Simpler shading
applies to one shared floor material; four ambient solvers are identified.
These features are active in scope, but their separate frame-time benefit is not
measured.

## Whole-frame interpretation and next comparison

The first loaded blocks average 51.15 ms in 605 and 47.28 ms in 606, but the
visible population and camera pose differ. Across the broader loaded samples:

| Sample | Frames / seconds | Frame-weighted mean | Render-loop bracket |
|---|---:|---:|---:|
| 605 accepted baseline | 3,236 / 170.4 | 52.67 ms | 11.28 ms |
| 606 before vegetation retuning, windows 8-16 | 3,281 / 170.4 | 52.01 ms | 8.41 ms |
| 606 all loaded/tracked windows 8-22 | 4,257 / 226.6 | 53.28 ms | 8.76 ms |

The full-run mean is therefore not substantially better than 605. Different
views, menus and slider experiments prevent treating either improvement or
regression of these aggregate means as causal. The maintainer's smoother-play
observation is consistent with confirmed geometry reduction and cheaper discarded
camera/wall work, but its magnitude is not reproduced by a matched overall FPS
comparison in these logs. Do not assign an invented percentage of the gain to the
new meshes.

Runtime stalls also remain outside initial loading. An Options-window construction
has a 431.89 ms measured build span inside a 602.33 ms frame; a card-artwork/driver
frame takes 517.60 ms. These are separate residual problems, not evidence of a
failed mesh substitution. Mesh simplification does not by itself reduce renderer
count, native Animator work or unrelated game/UI logic.

For a figure-only comparison, finish loading and keep the same stationary view,
zoom, menus, eye resolution and other budgets. Compare 0/0, 100/100, then 0/0
again, each after closing Options and discarding preparation/transition frames.
Collect at least 30 seconds per stable block. Separate player/enemy comparisons
can then use 100/0 and 0/100. Existing FRAME/SPLIT and mesh diagnostics suffice
for an application-frame comparison; they still cannot supply missing GPU busy
timing. No runtime code, defaults, assets or ModBuild changed for this analysis.
