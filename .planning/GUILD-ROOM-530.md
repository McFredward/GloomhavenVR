# Guildmaster room geometry — build 530

## Hardware evidence

The owner tested build 529 (`579ee1f6` integration base). Remote build 500 logs are stale.
`schwebende_buttons.jpg` confirms the native tabletop now appears, but the generic near-edge
rail hangs beyond it. The knife occupies the near right corner. `zu hoch.jpg` shows daylight
under native furniture against the cellar floor. No native furniture bounds census accompanies
these screenshots, so exact contact geometry remains a headset verification item.

## Source repair

- Campaign rail placement remains unchanged. Guildmaster uses the table renderer already
  selected by `MapTableLegs.TryFindTable`, projected into the stable map seat frame. The caps
  occupy the strip to the right of the parchment, farther away than the measured knife,
  dagger or sword footprint. All socket/glow extents stay inside that rectangle. Column count
  adapts to available room and caps never grow beyond their established size. Declared button
  order, native callbacks, highlights and hit geometry are preserved.
- Room placement previously always dropped the floor `0.75 * parchment width` below the map,
  including Guildmaster's native floor-standing furniture. Guildmaster now derives the initial
  room floor from native table/bench/barrel bases, using the same small floor-relief contact
  allowance as table legs. Campaign/scenario placement is untouched. Native map, furniture,
  input and player transforms never move. No later room correction or head following occurs.
- Scene and named-prop checks reject the mod's environment, synthetic furniture and remote
  objects. All calculations use common native scene geometry, not a player's gaze.
- A compact existing normal rail-order record describes the actual side grid. Extra placement
  measurements remain Debug-only, once per successful initial placement/build.

## Validation and limits

`scripts/guildmaster-room-tests.sh`: 6,273 assertions execute production layout arithmetic over
300 translated/scaled layouts, changing cap counts and narrow/wide tabletops; nine source
bindings and three runtime negative controls cover cap footprint, row direction and floor
contact. Strict Release build: zero errors, zero warnings.

Hardware must confirm both native furniture-name discovery and the knife footprint. The safe
rail build retries while no measurable support exists rather than dispatching against moved
native gameplay objects. Test direct Guildmaster startup, all rail buttons, close/reopen, and
both bundled rooms. Confirm campaign buttons remain at the front. The base-plane correction
cannot prove exact mesh contact on nonplanar environment ground from source alone.
