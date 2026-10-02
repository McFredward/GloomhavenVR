# Build 605: native scenery creation, ambient figure effects and held-figure presentation

## Supplied hardware evidence

Both new logs identify **ModBuild 604 / 253e89378**. The three logs and all seven
screenshots were copied with SHA-256 hashes before implementation to
`.planning/debug/frame605-hardware-evidence/`. The scenario UI identifies
Windumtostes Hochland. Its native scene is ProcGen; boot-time scene
registration is not proof of a campaign-map visit.

Player.log line 6334 records 17 admitted actors, 8/8 owned native mesh LOD caps and
15 disabled cloth solvers with both figure sliders at zero. Eight of the 17 actors
have authored coarse bodies. Original/selected near-mesh vertex totals for the
eligible LOD tables are 95,227/46,748. Changing enemy detail to 100 gives 77,433 selected vertices; changing
player detail to 100 gives 64,542. These are actual native table choices, not a
measurement of the mesh already selected at the current distance. Build 604's
actor-scope and cloth corrections execute in this run. The lack of an obvious
picture change does not establish an inert slider: several monsters have no
original lower-detail body, and distant figures may already use coarse meshes.

The settled scenery census inspects 9,077 unique meshes across four tiles and masks
all 3,189 admitted meshes: 683 grass, 1,620 vegetation and 886 dressing. Screenshots
still show conifers and dense wall/edge grass. Rejection examples include native
floor-grass composites sharing a collider, FR_Wall_Grassy_Verge_Thin_Narrow_01 and
the plant-only FR_CW_UnderWall_01_Roots child. Counts prove coverage of admitted
branches, not complete removal of vegetation.

The sleeping Spitting Drake screenshot shows a curled native figure and a flying
home ghost. Elemental-demon pickup screenshots show opaque rectangular VFX planes.
Original Wind Demon cutout materials retain invisible portions of mesh; replacing
those materials with unmasked additive overlay exposes their whole rectangular
surface. Native Animator cloning also restarts its default state rather than
preserving the sleeping source pose, and can retain state-machine callbacks.

Thirteen post-load performance windows at 3408 pixels per eye average 65.61–88.37 ms
per frame, with mean logic 34.38–53.27 ms, render-loop spans 12.99–23.26 ms and
measured mod work 18.46–34.71 ms. Views and reprojection rates vary. These are not
matched A/B measurements, GPU busy time or a predicted gain for Build 605. Ignore
initial loading when judging playability. The game SDK also repeatedly reports
Hydra destination-host resolution errors; those are recorded separately from the
renderer defects. There is one bounded actor-bar adoption warning with deferred
recovery, not an uncaught progression exception.

## Configurable rendering policy

`[Optimize] ScenarioFigureEffectsDensityPercent` in `dev.gloomhavenvr.perf.cfg` is
exposed under VR Options → Graphics in English and German. It controls decorative
ambient figure effects independently of body mesh detail. PC defaults to 100%; a
fresh standalone Frame configuration defaults to 0%. Saved choices are retained.
Gameplay attack/healing/condition indicators, body meshes, cards and UI are outside
this compromise. Geometry budgets and original generation-quality controls remain
available on every platform.

## Source changes and limits

The scenery classifier now admits the original all-decorative forest-bay assemblies
and their shared picking box, separate native grass blades, and verified wall roots,
plants and detached log attachments. Solid floor-grass plates and actual wall cores
remain: their green ground texture is not a separate optional grass renderer. A bay
box is suppressed only while every original visual member is suppressed; mixed
gameplay/structural assemblies and unknown callbacks retain native behavior.

For positively proved pure decorative prefabs with every applicable density at zero,
native explicit leaf placement returns a transform-only handle instead of instantiating
meshes, colliders or visual scripts. The original recipe is retained and restored with
the same pose when the setting increases. Native groups, mesh-instancing paths and
gameplay props are excluded. This skips original visual-instance construction and
its MaterialLoader.Start work; the already-resolved source prefab/resource bundle
still loads. Remaining classification finishes before native loading-screen closure;
material completion and room reveal also apply the mask before camera presentation.

The native material loader completes each renderer independently; WaitForProcGen
waits for placement notifications rather than these material handles. Review reproduced
grass-before-floor completion after loading-screen closure: with 9,000 unrelated
queued nodes, the ready grass waited 95 ordinary driver updates after its solid floor
became visible. That is a causal fixture result, not a measured headset duration.
Material completion now immediately rechecks the nearest affected shared-collider
composite under Generated Content when a solid member becomes visible. No queued
tile traversal or native material-wait barrier is needed for this sibling case;
unrelated map/NPC/UI material callbacks exit through an explicit scenario-tile gate.

The new figure-effects control pauses only identified original ambient solvers and
masks their optional renderers, including native Wind/Flame supplemental alpha shells.
Base and elite model provenance is checked. Originally paused/stopped systems, body
meshes and native combat/condition effects remain untouched. Late material completion
uses the cached actor binding and does not trigger a recurring scene scan.

Home silhouettes copy evaluated original bones, active states and blend shapes each
frame. They never instantiate native scripts, Animator controllers or state-machine
callbacks, so sleeping/condition poses and their phase do not restart at flying idle.
The same source handles local and remote holds. The home-pose owner remains the sole
root-transform author during zoom/recenter and immediate non-idle release. Highlights
and ghost depth use independent original per-submesh cutout textures, UV transforms
and cutoff values; animated glow UVs cannot displace the native alpha silhouette.
Other overlay users keep the neutral shader default. The game-exact Unity 2021.3.5
bundle rebuild preserves all 532 asset paths and 2,768 serialized object IDs: only
OverlayShader serialized content changed. Town banks and original game assets are
unchanged.

## Validation

The final integrated runtime tree `909c227c` passed **14/14 source gates, 97/97
local suites, 286,755 wire/golden assertions and all three bundle-format checks**.
The complete local run took 614.955 s with eight jobs. Source/local suite IDs,
manifest and every report log hash were independently verified. Strict Release
builds with zero warnings/errors; five bilingual document pairs remain consistent.
The historical compiled guard returns 1 for its explained behavior changes, not
for a failed source/runtime check. Relative to the preserved reviewed Build604
snapshot, twelve existing types contain the intended behavior/config changes,
eight differ only in the inlined 604→605 build constant, and eight reviewed helper/
patch types are added. No type or embedded resource is removed or unexpectedly changed.

Actual Unity production proofs include scenery **179 assertions/37 negative
controls**, native ambient figure effects **93/21**, and figure overlays **164/12**.
The overlay proof uses the original Spitting Drake rig, Sleeping_Idle/Flying_Idle
curves and rendered local/remote pose and alpha pixels. Portable scenery passes
96 assertions/18 controls; next-load generation passes 19/6; physical figure cloth
passes 72/12 with no secondary OFF deformation through 50 moving frames. Assertions
do not substitute for a headset picture or a measured Frame frame rate.

Exact source hashes, source/local reports, full guard/Release output, reviewed
compiled snapshot and protected runtime evidence paths are retained under
`.planning/debug/frame605-final-validation/`; packed shader comparison and build
evidence are retained under `.planning/debug/frame605-bundle-proof/`. An earlier
complete run was cancelled after discovering the late floor-material ordering gap;
it is superseded by this final gate. Its unchanged NPC mirror fixture reported one
capture-cast failure, then passed both its isolated complete rerun and this final
eight-job gate without source/test relaxation. The earlier partial/cancelled run
is not counted as successful validation. Hardware logs remain separately immutable.
