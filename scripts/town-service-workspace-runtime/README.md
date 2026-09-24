# Church and enchantress visitor workspace checks

Run `python3 scripts/check-town-service-workspace.py`. Production allocation, placement,
layout, original-mesh ground sampling, material ownership and lifecycle execute inside
Unity 2021.3.5 against the shipping bundle. Native roster, clock, canonical map/seat state,
asynchronous decoration acquisition and practical-light ownership are explicit boundaries.
The bundle hash is recorded and must remain unchanged throughout validation.

The merchant uses one persistent resident cabinet; private merchant visitor cabinets are
no longer created. This fixture exercises the actual church and enchantress workspaces,
including both services' complete relocation/material/disposal cycles. Ordinal zero uses
the shared resident; duplicate furniture stays inactive. Three visitors reserve radius
2.40 / 2.50 / 2.65 m at bearings -140 / -90 / -45 degrees, with furniture headings
-155 / -90 / -75 degrees in the common room frame.

Geometry follows the measured envelopes documented in `.planning/research/TOWN-ROOMS-549.md`
and the original-mesh scene audit (`scripts/check-town-scene-layout.py`):

- Merchant resident: cabinet X -1.64..-.44, Z -.09...72 m, and separate ledger lectern
  X -.36...36, Z -.08...52 m.
- Church/enchantress furniture: X -.88...88, Z -.44...50 m.
- Enchantress rear lantern: X -.88..-.48, Z .28...96 m. Each visitor reserves this larger
  union so changing between church and enhancement never invalidates another reservation.
- Every resident also reserves the actor X -.60...60, Z .30..1.20 m.

SAT clearance adds five centimetres around each component and retains the existing
one-centimetre separating gap. The enchantress remains at bearing 90 degrees but
her furniture faces 95 degrees to clear original forest scenery. Resident reservations must
clear one another and the complete native map table. Each visitor must clear all residents,
other visitors and the table. Actual shipping furniture vertices must fit the corresponding
worktop/lantern union. These are conservative geometric checks, not headset readability,
animated-arm clearance or an alpha-tested foliage claim. The original baked actor gate and
sloped-floor/ground-support parity gates remain active for all three resident prefabs.

Sparse connection IDs, flat/unassigned players, late join, reconnect and a rejected fifth
visitor exercise the actual roster allocator. Reading-side changes cannot alter canonical
room geometry or restart fades. A required relocation waits for held/returning cards,
dissolves for 220 ms, changes pose in a fully invisible published frame and fades in.
The tests retain intermediate visibility, input availability, owner-only material clones,
resident-prop animation clocks, no duplicate NPC/collider activation, native-template
immutability, ground support restoration and immediate idempotent disposal checks.

The full run includes fourteen compiled behavioral negative controls. A negative must
compile successfully and then fail the intended invariant; stale source bindings are
reported as test failures, never counted as successful negatives.

Build-549 validation against the final `d5f413b9159c18dd0651ce68e302923c1ac9d72bc2c00ae8d06c79866e9675e5`
bundle and the integrated room-layout source passed 278,277 production assertions and all
14 negative controls. Evidence: `/tmp/town549-workspace-final/run-5ffrxzmx`.
The mesh audit identified the older enchantress export's negative-X rear support; its
measured footprint and the supported native lantern are reserved together. Material-batched
meshes are checked by their actual vertices, because a combined AABB contains empty corners
between a counter and its separate rear perch.
