# Architectural detail density: source and runtime contract

**Historical647 experiment; withdrawn in648 after a measured Frame regression.**
The active renderer and asset bank are restored to646. New647 controls are INERT
saved-value storage. See [rollback review](../.planning/FRAME-648-REVIEW.md). The following records647's experiment and proof limits.

`ScenarioSceneryBudget.ConfigureArchitectureDetailDensity(Func<int>)` is an independent
live visual compromise. The caller supplies 100 when its master switch is off. Values clamp
to 0–100; a failing settings reader retains native detail and emits one bounded note.
Legacy grass, vegetation and small-decoration sliders retain their own semantics.

Admission requires an exact positive original-mesh signature and native source-bundle SHA
from the closed whole-game/DLC ornament catalog. Bank membership, a name such as `WallTop`,
a convenient procedural parent, or a distant decorative collider is insufficient. Unknown
metadata, missing source assets, controllers, gameplay/held/grabbable objects, lights and
unrepresented colliders fail open. Floors, required structural bodies, whole furniture and
pillars are retained. Original meshes, materials, native renderer enablement, property blocks,
colliders and lights are never assigned or disabled by this option.

Discovery promotes each non-LOD or LOD ornament to its largest wholly ornamental safe native
subtree before a generated/native room boundary. Every member uses that same path hash, so
partial density cannot select half of a multipart ornament. A current retained non-ornament
bank role 1 floor or role 2 structural core must exist. Authored composite boxes additionally
need a retained body inside their own prefab; a lone DLC tendril box remains native.
Role 3 floor supports do not qualify as core witnesses.

Exact native core renderer/filter/mesh/ancestry witnesses are captured during discovery.
The existing 64-record rolling guard restores dependent masks when a witness is disabled,
forced off, inactive, destroyed, replaced or reparented. Settings retunes check the witness
before acquiring a new mask. Whole-room rescans occur at existing placement/reveal/loading
boundaries, never as a new per-frame hierarchy sweep. Local and remote held visual roots use
the existing same-frame renderer identity rescue. Only this owner's false-to-true
`forceRenderingOff` transitions can be restored; foreign masks remain owned by their author.

The focused runtime fixture runs the complete production classifier and driver, the actual
bank identity/source-SHA reader and original mesh decoder in Unity 2021.3.5. It reconstructs
original PCG mesh-use transform/component chains from `catalog.json.gz`; unknown sibling
geometry is exported read-only from original bundles. Game generation/controller/lifecycle
callbacks and registry/config transport are explicit inert boundaries. This demonstrates
source admission, native Unity component state and mask ownership, not original recipe
execution, native shader pixels or headset performance.

Worker proof against the frozen catalog examined 46 prefab/source contexts, 139 original
mesh identities and 1,242 mesh-use chains across 14 verified bundles. Of 62 positive ornament
uses, 27 complete units were admitted and 35 refused. The admitted uses comprise 20,332
original triangles across those census contexts and are additional to the old decoration
path. They are not counts of concurrently visible hardware geometry. Current native cores
forced off at validation remain conservative refusals, including temporary terrain masks.

Run `python3 scripts/check-scenario-scenery-runtime.py` for complete runtime controls, or
select `--variant` for an explicitly focused subset. The runner requires the actual Unity
editor, readonly original game bundles and a Python interpreter with UnityPy. It snapshots
the delivered index privately, records original input and generated source hashes, verifies
closed positive catalog equality and rejects input drift during a run. The portable
`check-scenario-scenery-budget.py` fixture still tests the complete legacy classifier;
its deliberately unavailable bank does not claim architectural native-catalog coverage.
