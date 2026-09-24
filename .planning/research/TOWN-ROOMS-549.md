# Town placement 549 — front semicircle and shared cabinet

The build-548 hardware log seats the player on the map's authored −X side,
looking along +X (`MAP ROOM SPAWN`, yaw 90 degrees). The old priestess bearing
−85 degrees therefore placed her behind that player. The revised resident
semicircle uses the same common parchment/room frame and never reads a viewer's
current head orientation. Local room choice still cannot move shared residents.

| Reservation | Radius | Bearing | Furniture yaw |
| --- | ---: | ---: | ---: |
| Merchant | 2.30 m | 15° | 15° |
| Priestess | 2.30 m | 160° | 160° |
| Enchantress | 2.30 m | 90° | 90° |
| Visitor 1 | 2.50 m | −140° | −140° |
| Visitor 2 | 2.50 m | −90° | −90° |
| Visitor 3 | 2.60 m | −45° | −75° |

The merchant now uses one persistent shared cabinet at the resident station.
Private merchant transactions do not create additional cabinets. Visitor
reservations therefore cover the union of church/enhancement furniture and
its lantern, while resident envelopes also include the actor. The cabinet
has its own conservative envelope: X −1.64..−.44 m, Z −.09..+.72 m, height
2.10 m. The small ledger lectern is measured separately. Every envelope has
an additional five-centimetre collision margin.

The authoring export previously mirrored Unity's X axis, which was not apparent
in symmetric furniture. An actual Unity render of the asymmetric cabinet
exposed the mismatch with its runtime anchors. The exporter now compensates
that handedness before FBX import; only this revision's merchant assets were
regenerated. The imported cassette, crank and folding shutters align with the
static cabinet. Motion tests read the actual imported mesh vertices and opaque
card backing/shutter ray intersections, not only nominal anchor values.

## Evidence

- Production layout in Unity: 454 assertions and six negative controls, including
  the historical priestess position behind the player. Evidence:
  `/tmp/town549-root-clearance/run-q2b1l0bx`.
- Independent original-mesh audit: zero contacts across six simultaneous
  reservations / fourteen padded component envelopes in each original custom
  room; zero contacts with native furniture in levels 4, 5, 9 and 10. Native
  source hashes are verified against read-only GH_Data. Evidence:
  `/tmp/town549-scene-audit.json`.
- Environment bundle remains byte-identical:
  `fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.
- Initial combined asset rendering and cabinet motion check: 888,814 assertions
  and nine visual negatives in `/tmp/town549-root-cabinet-assets/town-assets-fvi9ra1w`.
  This run predates the revised motion skin and is not final integrated evidence.

These checks establish conservative geometric clearance and imported-asset
alignment. They do not establish headset quality, foliage alpha appearance,
dynamic hand clearance or multiplayer network timing.
