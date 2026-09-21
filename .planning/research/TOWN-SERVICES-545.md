# Build 545 — physical town services and portrait identity

## Current scope

The maintainer accepted the eleven build-544 screenshot findings and requested fixes,
closer identity to the original flat-game portraits, richer stands using original game
assets, entirely physical merchant/temple interactions, minimal interface elements for
enhancement, and a substantially more visible enchantress spell. Build 544's automated
passes do not establish acceptable hardware appearance. This work is in progress.

The hardware evidence is the eleven `npc_probleme/VirtualDesktop.Android-20260921-21*.jpg`
images and current local logs identifying ModBuild 544. Remote logs still identify 500
and are not evidence about this test. Visible defects include damaged hands, hovering
claw-like attentive poses, stretched necks/hollow costume joins, poorly integrated dark
eyes, floating wall-mounted lights, blank books, plain purple slabs and poorly lit faces.
The merchant's cards also failed to lift in the reported test.

## Interaction contracts

- Merchant: all available native catalog entries have stable physical rack locations,
  including the fully unlocked catalog. No page buttons, category tabs or flat menu on
  the counter. Lift to inspect with hand or laser. Deliberate release into an indicated
  buy/sell area submits the original native transaction after revalidating identity,
  owner, stock and cost. Grip, hover, cancellation and unrelated release never spend.
- Temple: physical offerings represent the actual native blessing options and prices.
  Deliberately deposit the chosen offering; preserve character selection, affordability,
  already-purchased restrictions, devotion progress and every original reward/continuation.
- Enchantress: physical ability cards, legal rune choices and a work surface replace list
  and page controls. Native rules still decide legal slots, prices, capacity, preview,
  purchase and removal/refund. Necessary original card symbols and readable quotations
  stay attached to the objects; do not silently omit detailed rules or permission reasons.
- Every physical interaction preserves native server validation and permissions. Original
  controllers stay the backend; presentation never writes gameplay state. Immersive mode
  remains default-on and disabling it restores the original window workflow.
- All owner-visible objects, intermediate poses and feedback must have multiplayer parity.
  Artwork/identity disclosure keeps the existing authorized item/card channels. A larger
  catalog must not be silently truncated by prior six-card or transport bounds.

## Art direction and work ownership

Original exported portraits under `.planning/debug/npc-references/` are identity authority;
generated variants are secondary references. Hand topology, eye/lid integration and neck/
costume deformation require actual close-up and oblique review, not only marker assertions.

The built-in imagegen skill produced one internal stand concept sheet, saved privately as
`.planning/debug/town545-concept/stations-concept.png`. It is design guidance, not game
geometry or a promise to replace original game decoration with generated assets.

Workers start from dev `b3d47378` in separate initialized worktrees. Faces owns actor
authoring/final assets; merchant owns catalog/token interaction; stands owns original
decoration, lighting and work motions. Root owns temple/enhancement integration, shared
presentation, input hooks, transport, localization and final review. No release is requested.
