# Town input corrections — build 550

Hardware reference: maintainer's 2026-09-24 `LogOutput.log`, ModBuild 549. These are
source/runtime corrections; smoothness and controller feel still need headset verification.

- The merchant's body remains a pointer occluder but no longer acts as a native shop
  button. Hover, laser press and touch do not play button feedback or open the shop.
- Ability cards and item cards now execute one shared wrist-local pose solver. Stock
  inspection must parent the actual physical card to the grab anchor; deriving a local
  pose from the previous world pose while leaving it parented to the cabinet caused
  wrist motion to enter interpolation and trail the fingers.
- The owned-item wrist fan uses the existing ability/item physical contact slab and
  expiring laser stand-down. Only the other hand is probed, not the supporting palm.
- Aimed cabinet stock with more than one page claims the pointing hand's existing
  `UiScrollFocus`: stick down advances, stick up goes back. The same physical cassette
  transaction drives shutters, crank, original card membership and multiplayer clocks.
  A held stick cannot restart a turn. Foreground windows, handles, solid blockers,
  held objects and tracking loss prevent cabinet scrolling. One-page stock leaves
  locomotion available. No input trace was promoted to normal logging.
- Parked sale cards leave fan elections and resting contact geometry, preventing
  phantom touch at the old fan slot. Authority loss cancels every moving stock sample,
  including cards retained for a native confirmation.

Validation in the input worker: strict Release zero warnings/errors; resident input
87 assertions and five negative controls; original catalog/input 6,766 assertions
and twenty-three negative controls. The latter executes actual contact geometry,
`ItemCardHold`, `UiScrollFocus` and drawer transitions inside Unity 2021.3.5, including
rapid wrist rotation at scales 0.05/1/198.12, foreground blocker precedence and reverse
turns with three pages. The first reverse-turn negative correctly revealed a weak
two-page test (both directions coincide); three-page coverage now rejects it.

The first-hover audit found an update-order race: locomotion could run before the
cabinet's presentation driver had stamped its first hover. `UiScrollFocus` now polls
the catalog's read-only physical-hover predicate synchronously. Public stock registers
and unregisters this probe with its lifetime, checking live map/feature/commit gates.
The query shares the exact same hit/occlusion code as paging but cannot request a turn
or claim public authority. Tests cover both update orders, nearer handles, disabling
the feature and disposing the producer; native window scrolling retains its existing
stamps and grace. Current collider queries deliberately remain uncached within a frame:
a foreground handle may move without changing the pointing ray.

Actual stock tests also cover taking a parked original card back out of the offered
palm, restoring grab-anchor parenting, and cancelling a pending offer on authority
loss. Each original callback runs once; the original cabinet home remains intact.
The shared item-transfer suite passes its five negative controls.
