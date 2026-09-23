# Build 547 — embodied town services

## Requested revision

The maintainer resumes NPC work after releasing hardware-confirmed build 546 as 1.0.7.
Integration and publication of development commits use `feature/immersive-town-services`,
never dev/main. The feature targets 1.1.0. The existing feature commits are ancestors of
dev but were undone by 3c263d6b. Rebase fast-forwards to current dev, then reverting that
removal restores the feature while retaining the later hotfixes. No history is force-pushed.

- Replace merchant drawers with a furnished sales counter exposing the full catalog.
- Preserve physical card visibility and free inspection when gripping merchandise.
- Offer an enchantress palm handoff for an owned hand card; show that card's native
  enhancement options, return it on departure or let the player reclaim it to close.
- Author purposeful occupation motion with real object contact, wrist/palm orientation,
  coordinated body movement and smooth visitor interruptions. Merchant counts actual
  coins; enchantress experiments with varied spells.
- Rebuild all three stands from generated concepts as detailed irregular furniture,
  using original game decoration. Place them farther out in a semicircle around the map.
- Restore the priestess head/hair under the hood, merchant portrait proportions and
  matching nostril geometry/texture on every NPC. Inspect front, oblique and moving poses.

## Evidence

The maintainer explicitly supplies no new logs. Inspected screenshots are
`debug/glatze.jpg` and `debug/händler_gesicht2.jpg`; original game portraits remain the
identity authority. Existing automated art validation does not establish headset quality.

## Validation

In progress. This is not yet a hardware candidate.
