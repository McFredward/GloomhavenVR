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

## Implemented behavior

Three generated concept references guide the authored, UV-mapped furniture meshes in
[town547-concepts](town547-concepts/README.md). Open curved merchant terraces expose the
complete native catalogue; furnished side returns accommodate owned stock. The lower
rear terrace leaves the merchant's actual coin-counting workspace visible. All physical
card faces and original price strips clear their supporting surfaces. Buying and selling
still require a deliberate eligible drop and the original confirmation.

The enchantress offers her hand on approach and accepts the controlled character's actual
map-fan card at her anatomical palm. Proximity hysteresis respects deliberate window closure,
other open services and native availability. An explicitly disabled map fan keeps the original
native enhancement window usable. Taking it back clears native selection; departure returns it through the existing
card flight. Return presentation survives native window closure and keeps the flying card
out of the static remote fan until arrival. An additive public map loadout count (83)
keeps the remaining fan's original artwork correctly indexed while a card is parked.

Merchant occupation now moves real original coins through contact, pinch, lift and rest.
The enchantress uses several coordinated poses and palm-up effects. Both blend into
visitor attention. Faces retain the original portrait identity with narrower merchant
proportions, fitted priestess scalp/hair and registered anatomical nostrils. Actual wrist
geometry extends under the sleeves without severing the arm.

The three resident stands form a semicircle at radius 4.8 in canonical map units. Extra
visitor workspaces have separate measured poses. The custom room clearing expands to
contain the maximum catalogue; discrete props and individual tree trunks retain their
proportions. Room shells, crowns and baked shadows need close hardware inspection.
Disabling the feature restores original room geometry and original service windows.

The expanded catalogue exposed transport defects: byte-sized manifest counts, interrupted
fragment completion and starvation behind repeated baselines. Bounded lossless record84
bundles, full-sized manifest counts and held-card priority preserve original native output
under the unchanged overall traffic cap. Delayed manifests cannot discard newer cards.
Performance numbers in [TOWN-MOTION-547.md](TOWN-MOTION-547.md) are synthetic desktop
measurements, not VR frame-time evidence.

## Validation

The complete required coverage is verified, with the original failed runs retained:

- All 14 source suites passed (`test-runs/20260923-232301-5054f2f7`).
- The complete 66-suite runtime run (`test-runs/20260923-232318-6c9a2d35`)
  passed 63 suites and exposed three outdated isolated fixture bindings. After correcting
  those bindings, the full focused activity, catalogue and mirror suites passed. No
  production behavior changed between the complete run and these repetitions. This is
  combined coverage, not a claim that the original full invocation returned green.
  Replacement evidence: `town-activity/run-x0un66xh` (58481 assertions, 13 negatives),
  `town-service-catalog/run-ynqcyyon` (6945 assertions, 13 negatives), and
  `town-service-mirror/run-isau8933` (complete mirror suite).
- Golden wire tests passed 286042 assertions. Strict Release passed with zero errors and
  warnings. Both bundle-format checks, documentation localisation and whitespace checks
  passed. Surface comparison found no removed keys, patches or log tokens; the historical
  compiled baseline differs as expected (118 changed, 155 added/removed compiled files).
- Actual final-source asset validation passed 3037 assertions and six visual negative
  controls under Unity 2021.3.5. The Windows town bundle is 103942692 bytes, SHA256
  `51791c7d197e6a08526ea21c5e104bafc0c347f93dce60385f16ff589ce71bed`.
- Install the complete matching version 1.1.0 / build 547 package on every VR peer,
  including both bundles. ZIP CRC and DLL/bundle hashes are recorded in the private
  `debug/town547-package-verification.json` after final packaging.

This is a hardware candidate. Headset appearance, tracking latency, crowded-catalogue
network convergence and VR frame timing remain unverified. Detailed focused evidence:
[TOWN-MERCHANT-547.md](TOWN-MERCHANT-547.md),
[TOWN-ENHANCEMENT-HANDOFF-547.md](TOWN-ENHANCEMENT-HANDOFF-547.md),
[TOWN-MOTION-547.md](TOWN-MOTION-547.md),
[TOWN-CLEARANCE-547.md](TOWN-CLEARANCE-547.md), and
[TOWN-GROUNDING-547.md](TOWN-GROUNDING-547.md).

## Hardware checklist

- In campaign and Guildmaster maps, approach each resident; inspect the floor contact,
  semicircle, room scenery, hair, nostrils, sleeves and close oblique facial views.
- Watch merchant coin handling and all enchantress spell phases; approach/leave during
  motion to check continuous attention transitions and readable practical lighting.
- Pick up stock in either hand, inspect freely, return it, then deliberately buy/sell.
  Check a large unlocked stock and multiple simultaneous visitors.
- Offer an owned fan card to the enchantress, select an eligible enhancement, reclaim it,
  walk away, close the native service and change controlled characters. Verify original
  confirmation remains required and no card disappears or appears twice.
- Repeat with another VR observer using a different map environment; compare actual card
  faces, bodies, prices, palm targets, return flights and ongoing NPC movement.
- Toggle immersive town services off/on and switch map environments. Original windows
  must remain usable, with no stale geometry, held objects or hidden selections.
