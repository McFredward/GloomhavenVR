# Town revision 548 — integration evidence

The maintainer's build-547 screenshots and logs supersede the previous asset-review
conclusions. Work remains exclusively on `feature/immersive-town-services`, version
1.1.0. Dev retains its separate hotfix history. This document records implementation
and measured evidence; neither source checks nor offline rendering prove headset quality.

## Confirmed causes and implementation

- The prior NPC clearance implementation expanded both custom rooms horizontally by
  3.5 and rewrote forest geometry. It has been removed completely. The original room
  bundle remains byte-identical; the six station/workspace reservations now fit inside
  the original scenery. See [TOWN-ROOMS-548.md](TOWN-ROOMS-548.md).
- The NPCs had abrupt, unfaded LOD thresholds. The revised shipping prefabs retain
  only their high-detail body mesh and four optical meshes. Compatibility handling
  also pins older imported actors to their highest detail. Scenery LOD is unchanged.
  The previous three highest-detail actors totalled 428,458 triangles including eyes;
  this measurement alone does not establish headset frame cost.
- The oversized horizontal merchant inventory is replaced with a compact travelling
  cabinet and two upright 4-by-4 revolving racks. Physical cranks turn the racks;
  the card page changes behind the opaque reverse side. Cards retain native prices,
  original artwork, inspection and purchase/sale confirmation. Holding or returning
  a card blocks its rack from turning. See [TOWN-MERCHANT-548.md](TOWN-MERCHANT-548.md).
- The merchant's work coin failed to load because its original material was requested
  through an unavailable GUID. The narrow replacement resolves the game's registered
  `GoldCoinMat` subasset, shared by work and offering coins. No game data is changed.
  See [TOWN-DECOR-548.md](TOWN-DECOR-548.md).
- Campaign city-event caps lack the optional Guildmaster button field. The previous
  sampler dereferenced it, repeatedly aborting the rest of the map update. The sampler
  now guards that optional field while retaining the original event eligibility.
- Merchant likeness was re-authored against the original game portrait. Revised facial
  proportions, eye apertures and ear UVs replace accumulated deformations. Final eyelid
  fitting preserves layer separation through blinks; Unity renders cover neutral faces,
  profiles, gaze and closed lids on all three actors. See [TOWN-FACES-548.md](TOWN-FACES-548.md).
- Four NVIDIA Kimodo-generated body phrases replace mechanical motion loops. They are
  baked offline, synchronized through the existing occupation clock, and combined with
  prop-contact IK, planted feet and smooth attention transitions. They are generated
  motion, not motion capture. The runtime requires no model or API. See
  [TOWN-MOTION-548.md](TOWN-MOTION-548.md).
- The multiplayer rack publishes an explicit owner clock and causal card membership
  through additive TLV85. Hidden artwork, skipped turns, late joins and held cards share
  the rack's visibility boundary. A live catalog preserves stable IDs across hidden
  pages: 1,100 test turns retain 384 IDs and publish 192 warm modules. Replaced cards,
  removed roots and unrelated popups are still retired.

## Integration status

The final Windows town bundle is 95,727,401 bytes, SHA256
`afdd76b364ecf35e10dd3f9f74beb038713e8324573fea1d38a45b08316b88c9`.
The original environment bundle is unchanged, SHA256
`fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.
The exact combined source assets pass 2,044 render assertions and nine visual negative
controls. Actual imported activity/contact tests pass 185,546 assertions and sixteen
negative controls. Maximum planted-foot drift is below 0.35 mm in these offline cases.
The final bundle also passes 3,646 production facial-runtime assertions and four negative
controls. The binder now recognizes a complete single-skin rig instead of falsely requiring
the removed three LOD skins. Strict Release passes with zero warnings and errors; docs
localization passes four English/German document pairs. All fourteen source suites passed. The complete local run passed 65/66 suites; its only
failure was a stale 4.8-metre expectation in the placement fixture. Updating that expectation
and its compiled unsafe-radius mutation produced 1,886 assertions and sixteen passing
negative controls on the targeted repeat. All 66 runtime suites are therefore covered.
The final compiled wire runner passes 286,090 assertions. Remaining bundle-format and
surface gates pass; the compiled comparison reports historical baseline differences
(118 changed types, 155 added/removed), not an unchanged-program claim.

Whole-run evidence is `debug/test-runs/20260924-012915-fae19c55/results.json`; the targeted
repeat is `/tmp/town548-root-setting-repeat.log`, wire evidence is
`/tmp/town548-root-wire-final.log`, and the explicitly resumed bundle/surface/compiled
comparison is `/tmp/town548-root-guard-resume.log`. No suite was silently skipped or recorded
as passing in the failed whole-run report. Package verification is recorded separately in
`debug/town548-package-verification.json` after packaging. No hardware outcome is claimed.
Paid generation API spend in this revision is zero.

## Hardware checks after the final package

1. Cellar and forest keep their familiar proportions; residents and all visitor stands
   clear original scenery without changing environment geometry.
2. Approach and retreat from each NPC: no geometry or lighting jumps. Check the eyes,
   merchant likeness, head turns and neck/collar joins from normal stereo distances.
3. Inspect, return, buy and sell merchant cards; turn both physical racks repeatedly,
   including late-game inventory and simultaneous visitors. Peers must see the same
   rack motion, complete pages, held cards and return flights.
4. Watch complete work cycles, close approach, attention handoff and return to work:
   hands contact props, feet remain planted, and transitions do not snap.
5. Open campaign city events and the other map services repeatedly. Disabling immersive
   town services must still restore the original windows and native continuation.
