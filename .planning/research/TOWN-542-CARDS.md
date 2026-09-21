# Build 542: physical merchant cards

The maintainer's build-541 screenshot `händler_augenbrauen.jpg` shows the original
item widgets lying flat as a catalog on the merchant counter. This lane changes the
merchant samples into individual rigid cards, while retaining the native item faces,
prices, filters, selection callbacks and purchase confirmation.

## Presentation and interaction

- Six cards per page keep the original native row order. Each card has a separate
  25-degree raised face, the existing `CardBodyKind.Item` contour/rim/reverse, and
  an unchanged native price row. Card bodies are 1.5 mm thick independently of size.
- Cards slide forward over 0.24 seconds on presentation, with a 25 ms stagger.
- Grabbing moves the actual face/body presentation; there is no replacement face,
  temporary card-back loading step or duplicate item left on the counter.
- Physical inspection is allowed even when native purchase selection is disabled,
  for example by affordability. Inspection neither selects, buys nor equips.
- Releasing anywhere returns the card to its own slot over 0.22 seconds. Dropping
  on the former merchant tray does not dispatch selection. The merchant tray's old
  drop-to-select caption must therefore be omitted by the integrated presentation.
- Explicit pointer selection still dispatches the original row's native callback,
  with all original permission checks and confirmation. Pointer selection is
  fenced while that sample is held or returning.
- Identity/context changes, closed services and recycled rows cancel inspection
  before returning borrowed widgets. Other service tokens retain their original
  drop-to-select semantics.

## Integration contract

`Entry.CardRoot` is still the original `ItemCardUI` transform and is always the
same publication identity on its rack, in either hand and during return. Publish
`Entry.BodyRoot` separately with the `TownServiceCardBody.Create` original template.
The helper uses the established item mesh/material pipeline, not generated artwork.
Its unit X/Y dimensions receive the measured card width/height; Z stays one.

`TownServiceToken.IsPhysical` identifies merchant samples. Do not additionally
publish their generic held mirror: the original item module already moves with
the owner's physical card. `IsMoving` fences pointer clicks and presentation
animation while held or returning. Original pooled card hierarchy and input flags
are restored on teardown; no shell is inserted into the native widget topology.

Catalog coordinates are relative to the merchant counter at station `(0,.970,0)`:
columns X `-.205, 0, +.205`; rows Z `+.105, -.105`; face rotation X=65 degrees.
The lower edge is at counter Y+.006 and row Z minus half the actual card height
multiplied by sin(65 degrees). Functional supports belong to shared counter furniture
so additional multiplayer workspaces receive identical support geometry.

## Validation

- Strict Release build: zero warnings/errors.
- Real Unity 2021.3.5 catalog fixture: **339 assertions**, **16 compiled negative
  controls**, all pass (`/tmp/town542-card-catalog/run-p54odbyn`).
- Real Unity interaction fixture: **896 assertions**, **30 compiled negative
  controls**, all pass (`/tmp/town542-card-interactions-final/run-3y82um60`).
- New interaction cases cover both hands, unavailable-to-buy inspection, original
  card identity, no duplicate mirror, safe drop over the previous selection tray,
  intermediate return pose, exact completed pose, immediate regrab, context changes
  and closure. A real native Button/EventSystem negative control proves that
  accidentally reusing the old tray-selection path is detected.
- Catalog fixture retains explicit stand-ins for game card construction and hand
  tracking; interaction fixture uses real Unity transforms, Button and EventSystem,
  with hardware tracking as a boundary. These prove lifecycle and geometry contracts,
  not headset readability or comfort. Integrated multiplayer and bundle validation
  remain the primary agent's responsibility.

Hardware checks: approach the merchant; lift/turn each side's card without a purchase;
verify its original slot is empty; release above and away from the counter; compare
owner and observer face/body poses during lift/return; use native selection and confirm;
change page/character/service while holding; test a visibly unaffordable item.
