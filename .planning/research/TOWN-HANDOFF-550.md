# Upright resident hand offerings — build 550

The maintainer's build-549 hardware report asks for the drop outline and offered
card to float upright above the merchant/enchantress hand. Previously the outline
inherited the palm's pitch/roll, the enhancement card rested at 75 degrees, and
merchant items immediately returned to their fan/cabinet while confirmation opened.

## Behavior

- One owner-authored seat places the complete portrait above the final animated palm.
  It remains upright even while the wrist rolls, faces the offering player, and has
  a gentle 6 mm bob plus 1.5-degree yaw movement. Observers use captured owner poses;
  they do not billboard at their own camera or run an independent idle clock.
- Merchant stock and owned items retain the actual inspected card while native
  purchase/sale confirmation is pending. A finite 250 ms settle brings the item
  into the floating frame. There is no duplicate card left on the shelf/fan.
- Original native callbacks remain the sole transaction authority. Taking the card
  back cancels the owned confirmation, without tearing a newly held card away.
  Rejection, cancellation, timeout, leaving and character changes return the sample.
  Closed wrist fans and a native inventory revision cannot retire a pending offering.
- Public rack `Detached` membership already derives from `IsMoving`; that now also
  includes a parked offering. Peers cannot claim the cabinet or change its page while
  an offer is pending. Losing public authority nevertheless cancels and returns the
  offering, covering claim races and teardown.
- Enhancement continues to park/reclaim the real ability card, retaining native
  selection, upgrades, return flight and original widget provenance. Its seat is
  updated after activity IK and before multiplayer capture.
- Both outlines and offered cards use the existing prioritized native presentation
  lane. No protocol layout or new record is needed.

The stock token now becomes a child of the tracked grab anchor while held, and
restores its original cabinet parent before return. This is the parent-space half
of the shared ability/item held-pose correction in the input lane.

## Validation and limits

- Strict Release compiles with zero warnings/errors.
- Merchant runtime: 1,210 assertions, ten negative controls. The production handoff
  and census cover upright pose through wrist roll, bob, closed-fan retention,
  native inventory mutation, ownership, native-window readiness and reentrant exit.
- Enhancement runtime: 761 assertions, fifteen negative controls, including the
  formerly flat card pose; selection/reclaim/return/native ownership remain covered.
- Interaction runtime: 1,055 assertions and 37 negative controls; native callback,
  modal ownership and held/release cancellation behavior remain covered.
- Actual native mirror capture/codec/playback: 122 basic assertions and seven
  negative controls, including five owner animation samples after reparenting the
  displayed surface to the floating palm frame. The public-catalog fixture also
  runs the production publisher on an owned card outside the fan hierarchy and
  checks that its face/body remain unique and prioritized (1,104 assertions, eight
  negative controls).
- The input lane's actual catalog/token fixture covers stock regrab, original parent
  restoration, shared held pose and authority-loss cancellation.

These are source/runtime and offline Unity results. Headset comfort, readability
and multiplayer visual timing remain hardware checks; no test count proves the
real headset picture.
