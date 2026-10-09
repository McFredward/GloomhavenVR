# Radial proximity for private structural pillars

This is a bounded correction of a visible quality defect, not a new FPS-saving
optimization. It applies to every pillar definition already admitted by the
existing immutable-bank/structural-owner policy, wherever the game or DLC uses
those originals. Other pillar families retain their existing native rendering.
The sections below record the Build656 metric correction. The subsequent
[viewing-radius repair](../../.planning/FRAME-PILLAR-DISTANCE-REVIEW.md) expands
original pillar detail to the adjustable viewing radius instead of only18cm.

## Cause and scope

The supplied Frame logs identify Build 654 / deb989570 in both sinks and report
`terrainNear=0% terrainFar=0%`. In Build656 those settings kept coarse geometry
until the existing 18 cm head or 12 cm tracked-hand protection applied. The private
owner already measures head distance during Update, independently of head gaze.
Its prior closest-point distance to a **world axis-aligned box** nevertheless
changes while a player circles a square column at a constant physical radius.
In the reproduced box case, an enclosing-cylinder gap of 15 cm is 35.7 cm from the
world box on axis and 15 cm on its diagonal. This crosses the 18 cm protection without
the player approaching. Actual production execution reproduces that defect when
the old metric is planted as a causal control.

Both the native Unity LOD census and camera-budget fallback are separate
mechanisms. The scene-wide AutomaticLOD count does not establish that a particular
structural pillar belongs to a native LODGroup. Audited city pillar prefab chains
contain original MeshFilter/MeshRenderer sources without authored LODGroup
ancestry (263 authored city pillar references, zero LODGroup ancestors);
runtime procedural ownership is an explicit harness boundary. No native
LOD controller, ForceLOD setting or native mesh/material source is written here.

The main log audit reports no terrain cap exhaustion in first-room windows,
with no more than 6 candidates/frame and no deferred sources. Late multi-room
windows do exhaust the 64-source-per-camera cap. That later fallback can still
change distant private versus native detail with frustum membership. This repair
does not remove the CPU cap or frustum savings, and does not claim to eliminate
all distant angular presentation differences. A settled protected nearby pillar
uses exact original topology whether its private source is admitted or deferred.

## Correction and cost

Only positively admitted pillar surfaces use a cylinder enclosing their authored
mesh bounds. The current source matrix places its centre and axes, including an
offset mesh origin, diorama tilt and nonuniform source scale. The radial extent
encloses both transformed horizontal diagonal corners; axial projections also
retain a conservative enclosure through shear. Distance to this enclosure has no
view-angle input. Existing configured near/far details, 75 cm threshold, 4 cm
hysteresis, 18 cm full-detail protection, 12 cm hand guard and continuous morph timing
remain. The enclosing circle is conservative, so it restores detail around the
entire column rather than only around world-box corners.

The existing bounded Update traverses prepared sources already. There is no new
per-eye distance walk, sort, renderer discovery, configuration or native LOD
override. Matrix-derived distance is additional math. Without tracked hands, the
single current matrix read replaces the old native bounds getter; tracked hands
retain the bounds getter as well for their original touch semantics. Authored
bounds are captured once at source preparation and native mesh replacement keeps
the existing dispose/requeue boundary. Hidden-wall sources still skip all of this
work through their exact existing policy.

Initial paired software-GL native primitive measurements for all 10 originals:
old median 0.000104–0.000140ms/source, corrected tracked 0.000586–0.000831ms, corrected
untracked 0.000537–0.000737ms. These 21 alternating batches of 512 calls include the
new method's delegate/observer overhead. The added cost is about 0.00048–0.00069ms
per pillar on this machine, independent of the number of eyes. This is measured
CPU overhead of the quality repair; restored close geometry can also increase GPU
work as required by the user's visible-detail request. It is not a headset FPS
prediction or an assertion of zero additional work. The separate complete
two-source Update timer is diagnostic only, not a paired non-regression proof.

## Validation

The complete focused terrain suite executes the actual three production sources
and production shader in Unity 2021.3.5f1 with real meshes, transforms, renderers,
MPBs, masks, Camera.Render and readback pixels. Native scene/config/bank delivery
controllers are explicit boundaries. All 10 admitted pillar definitions are
independently read from original crypt/cave/city family bundles; original channel
and index bytes and exact/coarse stream digests match the immutable bank.

The runtime cases cover eight orbit directions for every original, head yaw,
retained near/far details, tilted/nonuniform and offset source bounds, unchanged
touch guards, disabled sources and foreign masks. Native cap fallback and exact
private endpoint retain equal rendered silhouettes near the column. Existing
camera-order, previous-lease recovery, genuine native writers, door/foundation
boundaries, wall hiding/recovery and morph/pixel controls remain.

Four new causal mutations restore the old AABB, ignore authored centre, use
world-Y instead of the source axis or insert head-gaze dependence. All must fail
named visual/geometry assertions; unrelated exceptions do not pass controls.
An initial full attempt exposed a too-weak tilt-only-near control and expected
failure-order interference. The final fixture adds a coarse-radius tilt case and
runs original hidden-wall regressions first. Its receipts retain the initial
failed attempt honestly.

The next full attempt also exposed an existing hidden-wall fixture's un-restored
camera culling mask after moving it earlier: subsequent native sources were
correctly culled. The fixture now restores that mask, and the new near fallback
pixel comparison explicitly requires nonempty coverage. That run additionally
invalidated its source-stability receipt because its README was edited during
execution. Both invalid attempts remain retained; source inputs are frozen for
the final complete focused run.

Final frozen worker result: **818 production runtime assertions plus all 91
causal controls pass**, 92 total compiled variants, actual Unity exit code0 and
unchanged source-stability receipt. The run is
`.planning/debug/pillar-radial/focused-final-frozen/run-_26hbgwr/`.
All16 source checks and the strict Debug build also pass (zero errors/warnings).
These are the affected worker scopes, not a fresh complete integration gate.

Worker receipts live under `.planning/debug/pillar-radial/`; the primary agent
records the final integrated gate scope and archive location. Automated software
GL evidence establishes geometry/lifecycle contracts, not actual headset
appearance or performance. The next Frame run should circle a nearby column at
constant radius and repeat the camera pose with all rooms open.
