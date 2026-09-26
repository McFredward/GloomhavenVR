# Resident motion, short cloth contacts and merchant cabinet — ModBuild 571

The supplied Debug log and hardware images are from ModBuild 570. The priestess video shows a
hard prayer/cover transition and a stiff arm silhouette. The screenshots show enhancement
controls before a card reaches the enchantress and the merchant's former cabinet hardware.
The cloth log recorded real hand contact but zero visible displacement at the end of a short
touch. These are build-570 observations; the following corrections still need a headset test.

## Changes

- The cloth solver now keeps the approach snapshot on the first contact frame. A fingertip
  crossing both proximity thresholds in one frame uses the previous solver surface. After
  release settles, the next contact captures a fresh baseline once.
- The priestess blends through revised prayer, attention, cover and blessing poses. A committed
  donation takes priority over her unavailable response; that response is reserved for a later
  visit. The existing shared donation revision starts the same seeded particle, light and voice
  sequence for each peer without predicting payment.
- An enchantress visit retries a briefly blocked native open at 10 Hz while the visitor remains
  nearby. Physical card release works with trigger or grip. Original enhancement choices and
  printed-card hotspots appear only after the matching owned card is accepted onto her palm.
  The original native selection logic remains authoritative.
- The merchant cabinet is re-authored around its existing physical card cassette, six category
  inputs and page crank. A textured carved shell and matching detailed hardware replace the
  visible prototype geometry. The native item cards and original category icons remain live
  components; controls and their hitboxes move together to the revised seats.
- The cabinet source maps remain at 2048x2048. Windows imports use a 2048 cabinet albedo
  and smaller normal/metallic maps so the single ordinary-Git bundle can still be pushed.
  The 100 MiB boundary comes from GitHub's file limit for regular Git blobs, not an art
  specification. If headset inspection needs more texture detail, use another delivery
  strategy rather than treating the current mip ceiling as a quality requirement.

No wire grammar, voice cue ids, setting defaults or release version changed. ModBuild increments
for the new hardware candidate. The town asset bundle changed and requires a full install.

## Validation

- Release compilation passed with zero warnings and errors. The native Unity asset check
  passed 951,257 assertions and nine visual negative controls. The Windows bundle builds
  twice byte-identically at 104,395,074 bytes (SHA-256
  `abb9da2c79c7b483442f2d13fa9562204ceca72f441df3d610f0fead50464b3d`), with
  UnityFS format 7, Unity 2021.3.5f1 and TypeTrees enabled.
- The integrated local suite, source/wire guard and remaining focused checks are recorded
  after their final run below.
- A native Unity cloth negative control reproduces the old one-frame reset at zero displacement;
  the corrected short contact moves 7.904 cm in that harness. The measured value establishes
  solver response, not visible feel on a particular headset.
- The priestess's imported-rig sequence has been rendered from front and oblique views and
  inspected for the hard transition. The final cabinet is reviewed from front and oblique views
  with its moving controls before the bundle is built.

## Hardware checklist

1. Tap and drag each table cloth with a fingertip. Confirm an immediate visible displacement,
   gravity and return to the table without passing through it.
2. Approach the priestess before and after a donation. Verify continuous shoulder/elbow motion,
   immediate cover when unavailable, and gratitude plus blessing on a successful donation.
   A later revisit may explain why the bowl is covered.
3. Approach the enchantress during map-card loading, offer and reclaim a card with either grip
   or trigger, then leave and return. Her controls should appear only while she holds a card;
   ability hotspots should lie on that physical card.
4. Inspect the merchant cabinet from several angles. Use all categories and the page crank;
   confirm their visible mesh, icons and touch/laser targets coincide as cards move.
