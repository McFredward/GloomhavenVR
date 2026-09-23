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
- Merchant likeness is being re-authored against the original game portrait, rather
  than successively deforming earlier generated faces. Actual Unity eye, lid, neck
  and gaze renders are required before promoting the new source assets.

## Integration status

Final facial assets, generated activity clips and durable multiplayer rack playback are
still being integrated. No build-548 hardware package has been published. Final source,
runtime, wire, strict-build, asset-render and package results will be recorded here
before handoff. Paid generation API spend in this revision is currently zero.

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
