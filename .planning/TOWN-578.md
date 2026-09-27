# NPC hardware follow-up — ModBuild 578

The supplied local Debug logs identify feature ModBuild 577. The two new
`npc_probleme` videos show the priestess's prayer-to-neutral elbow kink and
the enchantress card's native aura losing its left and right edges during a
held offer. The merchant and enchantress reports concern the same run. No
remote log was supplied. The separate `abgeschnitten.jpg` shows the native
character creator's right edge cut off; its correction and the reported
overhead-bar settings are recorded in [BARS-CREATOR-557.md](BARS-CREATOR-557.md)
and merged from `dev` into this feature build.

## Source-grounded corrections

- The offered enchantment card still participated in the physical ray hit,
  but a UI-only laser distance override could draw the beam past that solid
  card. The visible beam now ends at the nearest live UI or physical hit.
  Card-wide trigger pickup remains disabled; only the game's original
  enhancement-area controls handle the trigger. An empty part of the card
  stops the beam without taking the card or choosing an area.
- The supersample capture shrank to the offered card's narrower host while
  the native `CardHilight` aura continued to pulse wider. The active offer
  retains the original animated aura bounds, expands this capture only for
  that offer, and releases the expansion when the card leaves the hand.
  This fixes the side crop without replacing the original effect.
- The priestess's earlier transition still bent through an unreachable
  intermediate arm pose. The hand path and elbow pole now follow a continuous
  anatomical descent from prayer into neutral. A 90 Hz rendered contact sheet
  was inspected frame by frame against the imported skin; the revised
  403-pose scan found no arm/torso intersections. The accepted neutral and
  unavailable bowl-cover endpoints remain intact.
- The merchant's visible hand float could not be corrected by merely moving
  the palms inward: the imported hand mesh then cut into the coat. The arm,
  elbow and wrist targets now move together around the actual belly surface.
  Front, oblique and overhead renders show both hands seated against the
  belly; a 91-frame imported-mesh scan found zero arm/torso triangle
  intersections (63 frames intersected in the prior pose). Final palm gaps
  are about 14 mm on the left and 6.7 mm on the right.

## Validation and headset limits

The focused enchantress Unity fixture passed 1,197 runtime assertions and
40 active negative controls; its strict Release build had zero warnings or
errors. The priestess and merchant imported-rig production suite passed
628,564 assertions; all 47 active negative controls were demonstrated,
including a corrected control that previously masked the prayer transition.
The integrated checks include both independent changes.
Automated geometry and screenshots cannot prove the
headset appearance or controller interaction. The next headset test must
check the priestess transition at normal speed, both resting merchant hands,
empty-card laser stopping, selection and physical input on each enhancement
area, and the full aura throughout its pulse and card rotation. The creator
edge, wall-visibility toggle and both overhead-bar scaling modes also require
headset confirmation.

The final integrated run passed all 14 source gates and 80/80 runtime suites,
including `town-residents` after its test fixture stopped assuming the former
0.70-second transition. The wire tests passed 286,578 assertions; the three
Unity bundles passed format validation; the strict Release build finished with
zero warnings and errors; and the English/German documentation check passed.
The refactor guard's compiled-form comparison returns 1 because its available
baseline predates the NPC feature: it reports 130 changed and 218 added/removed
types. Its source, runtime, wire, bundle and surface gates passed without a
removed setting, patch registration or log marker.
