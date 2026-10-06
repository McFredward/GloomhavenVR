# Frame634: bound terrain presentation CPU work

The immutable Build633/902fa Frame capture still shows roughly 98–103 ms fully
loaded three-room frames. Terrain pre-cull consumes approximately 37–42 ms per
frame across two camera callbacks. The late work window has 478 cheap source
leases per frame: approximately 239 private substitutes per eye. These are
already private scene-local MeshRenderers, not Graphics.DrawMesh calls. The
screenshots retain the complete three-dimensional board and all three rooms;
there is no authorization to replace it with a flat board or disappear rooms.

The previous geometry/shader work therefore has a substantial CPU submission cost
of its own. Every source rereads native renderer fields, current material slots and
property blocks and walks the actual ancestry/component ownership. A safer cache
cannot simply retain that native eligibility across eyes: component replacement,
reparenting, visibility, mesh/material/MPB changes and native callbacks remain live.
Unity2021's GameObject metadata does not expose a public component-count/index
shortcut; count-only snapshots would also miss same-count component replacement.

`Optimize.ScenarioTerrainCameraSourceLimitCount` bounds the number of expensive
source evaluations per camera invocation. Zero retains unlimited evaluation.
The agreed common implementation uses fresh PC default zero and fresh Frame /
explicit Standalone default 64, with all later individual choices preserved by
normal configuration behavior. Unsupported/rejected evaluations consume budget as
well; otherwise a large refused group could still create an unbounded CPU pass.
Preparation keeps a stable list ordered by immutable original triangle cost, an
upper bound on possible savings, with original renderer identity as a stable tie
breaker. It sorts only after preparation membership changes. It does not favor
room visibility or hide any part of the board.

Every source beyond the cap keeps its original enabled/active state, native
geometry, materials, shader, animation, collider and room visibility. This is an
explicit CPU/GPU tradeoff: fewer private substitutes can increase original mesh /
shader work. Existing detail and cheap-wall-shading settings apply to the admitted
subset. Native originals retain the rest. No native mesh/material array or template
is rewritten; no new objects become children of native cloning roots.

`SharedEnvironmentMaterialReads` additionally controls exact managed type/name
classification reuse, one owner matrix/scale read inside the synchronous camera
pass, and conservative camera-frustum rejection before expensive source work.
Only immutable type roles and exact current name strings are retained. Actual
component lists, parent chains, names and native renderer/material/MPB state are
still read for every evaluated source each eye. Same-count component replacement
cannot inherit a prior verdict. The exact-name cache is bounded to 1,024 strings.
Off runs the old full classification and outside-view admission path for comparison.

Frustum rejection reads current native renderer bounds and keeps the union of both
stereo projection/view matrices AND the camera's authored culling matrix. A source
visible to either eye or that authored override cannot be dropped. Invalid or
unavailable projections fail open to full admission. Plane data is rebuilt for
this invocation only. A rejected substitute leaves the original source untouched.
Nested/foreign cameras, native command buffers, writer/clone boundaries, native
mesh replacements, host disable, exceptions and shutdown retain their existing
mask restoration. Floors, doors, figures, interactions and foundations retain
existing admission vetoes and original representation.

Debug work rows record actual evaluated candidates, admitted substitutes, budget
fallback and frustum fallback. They describe work/admission, not GPU draws; the
existing post-render triangle/cheap counters still count only surviving leases.
No new frequent normal-level stream is introduced.

## Validation boundaries

A source-bound Unity fixture builds 96 independent native renderer sources. Its
complete production pass confirms 12 actual material getter calls under limit12,
84 originals retaining the native fallback, stable selection and immediate live
zero restoring all96 calls. It also tests component addition and same-count
replacement between actual camera invocations, native source rename, current
frustum rejection, and shared-read Off restoring the complete legacy path.
Further controls cover second-eye-only frustum membership and invalid camera
planes. Original material/MPB/native visibility/clone/morph tests remain.

The root-owned shader change bypasses inactive dissolve map/pow/simplex work.
The original HIGH/N_MRAO equation still reduces exactly to clip(1-cutoff), including
cutoffs above1; original LOW remains unclipped while inactive. The shader branch
ordering is bound to its production source. Native HIGH branch pixel comparisons
cover active continuous dissolution and inactive cutoffs, with causal controls for
inverted/unconditional early-out and missing authored cutoff. The root builds the
Unity2021 environment bundle; this worker changes no shader or asset bundle.

Focused native Unity/GL proofs and strict builds verify code/work boundaries. They
are not a new Frame FPS, stereo compositor, Windows/D3D shader or multiplayer
acceptance. The next hardware run should compare the fully loaded same viewpoint
at limit0/64 and inspect terrain scope timing, evaluated/fallback work and all rooms,
including near geometry and native wall fade. Original GPU cost may rise as native
sources take over; remaining native/scene workload still needs its own reductions.

## Completed worker evidence

The final focused run executes production plus all 46 negative variants in actual
Unity2021.3.5/GL, with 316 production assertions. Every negative variant compiles
strictly and reaches its intended runtime assertion; compile or shader errors do
not count as passing controls. Source hashes remain unchanged throughout the run.
The independent mesh audit verifies ten original structural definitions, original
channel/index bytes and every coarse digest. The 96-source case measures an 87.5%
reduction in actual live material reads (96 to12), with all84 omitted native sources
retained. This measures source operations, not CPU milliseconds or headset FPS.

Evidence lives in the worker's private debug directory:

- `frame634-terrain-verified/run-3uxgvpsl`: 47 variants, runtime report, source
  snapshots/hashes, stability manifest, native mesh audit, shader samples and Unity
  log; production316 and negative46 pass.
- `frame634-terrain-release.log` and `frame634-terrain-debug.log`: final strict
  solution builds, both zero warnings/errors.
- `frame634-terrain-source/results.json`: all14 source suites pass.
- `frame634-terrain-initial-proof`, `frame634-terrain-complete` and
  `frame634-terrain-final`: preserved initial fixture compile failures and the
  earlier foundation-control ordering failure. The final rename case uses the
  independent Preview boundary, preserving the original captured-foundation
  negative control and its specific oracle.

These are focused worker checks. The integrator still runs the complete final-tree
gate, including wire golden vectors, after combining all owned changes.
