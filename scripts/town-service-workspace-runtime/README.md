# Merchant visitor workspace checks

Run `python3 scripts/check-town-service-workspace.py`. This compiles production
`TownServiceWorkspace` and `TownServicePlacement` into separate test assemblies and
runs them in real Unity 2021.3.5 with the shipping town bundle. Native roster, clock,
canonical map-frame/seat state and bundle location are explicit boundaries. Floor
sampling, original mesh bounds, transforms and materials execute actual production.

Ordinal zero retains the original merchant counter. Its duplicate furniture remains
inactive. Three full-size extra counters occupy the free southern clearing ring at
radius 2.35 m and map-frame yaws -124°, 180°, +124°, facing inward. The conservative
footprint |x| <= 0.9 m, |z| <= 0.422 m has outer radius 2.915 m, inside the nearest
solid scenery radius 3.033 m, and inner radius 1.928 m, outside the map/seat radius
1.05 m. SAT tests compare each counter against every original station pose produced
by the actual placement source, and against other counters. Shipping furniture
mesh bounds must fit this footprint. These checks do not certify hanging foliage
or headset readability.

Native connection IDs are sparse and can exceed four after reconnects. Full native
roster ordinals include flat/unassigned users and avoid divergent arrival-history
caches. No network allocation protocol or purchase serialization is introduced.
Only the owner resolves each pose; observers consume the actual published widget,
card, furniture, material and transform state.

Roster, environment and canonical frame changes are checked on the existing 250 ms
cadence. Floor geometry is sampled only when one changes. Relocation waits for held
original cards and return flights to finish. The entire owner's workspace then fades
out, changes pose in a guaranteed fully invisible frame, and fades in over 220 ms.
It never sweeps a counter through the map or another permanent NPC. Original card
sizes, native selection and transactions stay unchanged. Mod-owned CanvasGroups
block new input during relocation without changing native Selectable availability.

Checks cover actual geometry, rotated/scaled map coordinates, sloped original ground,
sparse rosters, full opacity synchronization, delayed relocation, native assets,
private materials, no cloned NPC or active furniture colliders, late joins, reconnects,
offline placement, fifth-user rejection and immediate idempotent disposal. Eight
compiled negative controls must fail their corresponding behavioral assertions.
The catalog suite additionally binds the actual visibility/input gate and moving-card
condition; the interaction suite checks the production presentation handoff.
