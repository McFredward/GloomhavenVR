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
only the standard build handshake advances to663.

## Validation and remaining hardware scope

Final terrain production executes7,021 assertions in actual Unity2021.3.5f1,
including all10 original pillars, cap0/66/67/100, radial orbits, yaw, board scale,
live independent/manual selection, figure-toggle/presence independence, exact
hysteresis edges, source-read budgets, nonempty pixels and original/coarse topology.
The final focused run passes production and three affected causal controls with
all64 bound inputs unchanged. Those exact64 hashes match the integration tree.

Terrain control evidence is explicitly **composed106**, not a new107-variant final
pass. The first106-variant full attempt passed6,999 production assertions and104
of105 controls. One new toggle-coupling control escaped because its far test point
selected coarse geometry under both independent and manual policies. A stronger
near point now exposes that defect. A narrow degenerate-Y fallback repair retains
original geometry rather than borrowing a current AABB radius. The final four
variants pass7,021 assertions plus the repaired toggle control, new degenerate-Y
control and existing invalid-size control.103 other controls inherit their
unchanged passing evidence. The initial exit1 and source-stable receipt remain.

The unchanged figure selector executes2,719 native assertions and five causal
controls, including72 legacy-boundary comparisons and36 effective-tier checks.
Both final helper/fixture hashes match; ActorBudget and CoreModule are byte-identical
to661. EN/DE settings help passes2,305 assertions and its content/lookup controls.
Strict Debug/Release pass with zero errors/warnings, and direct portable wire
goldens pass300419 assertions. Source16 passes on the integrated production tree;
unchanged NPC/general scopes inherit661's recorded evidence. The parallel wrist
controls shipped as662 during this work, so the final delivery is663 and preserves
those changes. Its merged actual wrist/menu proof freshly passes324 assertions
and three controls alongside the unchanged2,305 settings-help assertions.
Source16, strict builds and300419 goldens also pass again on that merged tree.
This is bounded integration, not a fresh complete206-suite gate.

The final private662 (`bb2443c97`) comparison covers1252 compiled types: eight
intended behavior units, eight verified662→663 constant-only consumers, and one private
branch stamp. No type is added/removed and no change is unexplained. Config keys
665→666 add only the new independent quality choice;235 Harmony-text patches and
4795 log tokens remain, without removals. Builds and decompilation are serialized
so XML documentation cannot contaminate the comparison.

Ignored receipts and verified compact archives live in
`.planning/debug/frame-pillar-figure-lod/`. Logs, source/fixture copies, original
asset hashes and pixel receipts remain; generated Unity projects, compiled
outputs and duplicate originals are regenerable caches. The superseded unshipped
party-reference evidence is separated under `rejected-v1/`; it is not final
acceptance. Only this task's worktrees/caches, including the additional private662
baseline checkout, are removed after verification.
Other agents' worktrees and supplied hardware inputs remain intact.

No automated check establishes Steam Frame picture or FPS acceptance. The next
hardware run should approach and circle the same pillar, compare a figure at a
similar range, and repeat after changing table scale. Pillar detail must not change
when figure LOD is disabled or the party changes.
