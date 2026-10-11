# Original rooms and compact shared town layout — build 548

The hardware report `riesiger_keller.jpg` rejects the horizontal room expansion introduced
in build 547. The feature removes its scenery-scaling and private tree-mesh rewrite classes.
Neither original room geometry nor its placement/scale is changed to make NPC furniture fit.
The main environment bundle remains byte-identical, SHA-256
`fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.

## Shared reservations

All environments resolve the same metre-space poses. This also applies when multiplayer
peers choose different local environments; local environment choice cannot move shared
residents or visitor furniture.

| Reservation | Radius | Bearing | Furniture yaw |
| --- | ---: | ---: | ---: |
| Merchant | 2.30 m | 15° | 15° |
| Priestess | 2.20 m | −85° | −85° |
| Enchantress | 2.30 m | 90° | 90° |
| Visitor 1 | 2.55 m | −140° | −140° |
| Visitor 2 | 2.55 m | 150° | 180° |
| Visitor 3 | 2.70 m | −40° | 5° |

The three residents form a 175-degree semicircle. Independent visitor yaw is necessary:
radial-only visitors could not all fit alongside the residents, untouched native Guildmaster
barrel/bench, and original forest trunks. Visitor reservations include the union of all three
service furniture shapes, not only merchant cabinets. Reservations are conservative even
when all four players use different services simultaneously.

The merchant envelope includes its complete revolving-rack sweep: local X ±.81 m, Z
−.92 .. +.53 m, before the additional five-centimetre collision margin. Other services use
X ±.87 m, Z −.43 .. +.50 m; enchantress lantern placement extends Z to +.86 m. Resident
actor envelopes extend to 2.10 m high. Furniture tests conservatively use 1.55 m height.

## Verification

The replacement clearance suite compiles the production `TownServiceLayout` and executes it
inside Unity 2021.3.5 against both original bundled room prefabs. It verifies original
transform and mesh-reference preservation, readable mesh vertex preservation, room detection,
shared poses, compact radius limits, and rejected invalid identities. It exports actual
production-resolved poses for the independent mesh audit. This is explicitly a layout/room
contract, not a simulation of the complete NPC lifecycle. A separate bounded source guard
rejects reintroduction of the removed scaling/mesh-rewrite mechanism.

- Runtime: **448 assertions and five negative controls passed**, including an injected
  scenery-scale mutation, oversized residents/visitors, environment divergence and an invalid
  reservation. Evidence: `/tmp/town548-clearance-final/run-4ekuiyk2`.
- Original scene audit: **zero contacts** for both custom rooms and all four original native
  map scenes (Guildmaster levels 4/5 and campaign levels 9/10), with all six reservations.
  Cellar: 53 height slices / 134,869 triangle slices; forest: 38 / 73,139. Each room checks
  sixteen padded component envelopes. Native scene hashes are verified against read-only
  `GH_Data` originals. Evidence: `/tmp/town548-compact-production-poses-audit.json`.
- Collision negatives: moving a visitor onto the merchant reports six contacts per room;
  placing it over the native Guildmaster barrel reports eight native contacts in each
  Guildmaster scene. Both audit runs fail as intended and retain their reports.

The old `--room-expansion` and inventory-return audit arguments are removed: testing an
expanded substitute room would certify the hardware regression instead of the restored room.
Native render-bound and solid triangle checks intentionally exclude non-solid foliage,
canopies, floors and particle meshes. They establish placement clearance, not headset
appearance, dynamic hand reach, foliage alpha appearance, or multiplayer timing. Those still
need the next hardware test.
