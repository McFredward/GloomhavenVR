# Independent pillar LOD with the figure distance rules

## Hardware evidence and clarified request

Both newest Steam Frame log sinks identify Build661 / `1b6e1e95f`, with
`terrainNear=0% terrainFar=0% terrainDistance=0.75m` and figure distance LOD on.
Figure measurements identify saved player/enemy caps0. No new screenshots are
present in this hardware drop. The maintainer still reports that pillars lose
detail much earlier than figures and require an excessive approach to recover.

The earlier Build659 repair retained original pillars inside the saved75cm
radius. Its comparison used the figures' inner8/7 band. With these saved figure
caps that band changes no visible mesh: the first effective reduction is20/18.
The native figure bank has its actual tier boundary at67%, not45%.

The maintainer explicitly clarified during this repair that every figure and
pillar must select its own LOD from its own VR-camera distance. Present figures
must not control a pillar. An initial unshipped design sampled current party
bounds; that design, its configuration key and all ActorBudget/Core bridge
changes have been discarded. Its receipts are retained as superseded evidence,
not final validation. The final change has no active-party dependency.

## Independent calculation and cost

Every admitted pillar uses its own original bounds, transformed by the single
matrix already read in its existing Update. The enclosing cylinder gives the
same gaze-independent radial surface distance as before. Its full diagonal
radius supplies the size-relative thresholds shared with the unchanged figure
selector:8/7 for a near cap at least67,20/18 below67. Each surface keeps its own
hysteresis state. Equality retains that state. Near pillars remain original
even with terrain caps0/0; distant pillars use the selected cap minimum.

This shares a mathematical distance rule, not an actor, current LOD result,
scene-dependent reference, or native controller. Changing figure caps, holding
or removing a figure, or disabling figure distance LOD cannot control a pillar.
The original `ScenarioFigureDetailBudget` and Core wiring are unchanged.

Read-only native audits cover all10 crypt/cave/city pillar definitions admitted
by the existing immutable-bank policy. Original full radii are1.703–1.925 world
units, compared with1.127–1.702 for the10 audited original figure prefab unions.
The pillars therefore have a comparable, slightly larger detailed range rather
than an exact common metre radius. Using only their horizontal thickness would
repeat the defect: those radii are only0.295–0.874 world units. No arbitrary
minimum/maximum calibration or live figure census is introduced. Other families
retain their existing native presentation; this does not broaden admission.

`Optimize/ScenarioTerrainPillarDistanceLod` is default-on on every platform and
appears in Graphics with English/German captions and help. Off restores the
existing saved `ScenarioTerrainDistanceMeters` radius. Existing keys and values
are preserved. The same PC/Frame/Standalone binary supports both choices.

Ordinary walls, hidden-wall skipping, doorway exclusions, native masks, hand
protection, source caps, camera order, native mesh ownership, continuous morphs
and gameplay retain their contracts. Invalid geometry retains original detail.
The size calculation adds scalar math to the existing source Update; it adds no
actor scan, bounds read, matrix read, sort, allocation or per-eye distance walk.
Off skips the extra radius square root. Retaining more original triangles can
increase GPU work: this is the requested quality correction, not an FPS-saving
optimization or a promise of zero extra CPU/GPU cost.

The existing bounded Debug quality snapshot adds `pillarDistanceLOD`. No new
normal-log diagnostic stream is introduced. Multiplayer wire layout is unchanged;
only the standard build handshake advances to662.

## Validation and remaining hardware scope

Final counts, composed input hashes, compiled scope and verified archive/cleanup
receipts are recorded below after the focused integration checks finish. The
unshipped party-reference proof is not reused as acceptance of this independent
implementation. No automated check establishes Steam Frame picture or FPS
acceptance; that remains the next hardware run.
