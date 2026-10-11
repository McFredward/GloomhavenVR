# Merchant palm handoff and owned-item fan (build 549)

## Behavior and source ownership

Approaching the resident replaces the locally controlled selected character's ability fan
with `CMapCharacter.AllCharacterItems`: equipped and bound items, preserving individual
object identity and including readable nontradeable items. There is no count cap.

The proximity volume uses the actual face rig eye point and optical front, the same
2.4 m enter / 2.9 m exit attention distances, and the same native obstruction mask.
Each owner inside that volume may inspect their cards; another nearby player winning
the shared resident's gaze does not withdraw their hand. The resident's actual final-IK
`ActivityOfferingPalm` determines the zone, never a duplicated authored hand coordinate.

Inspection uses the existing `ItemsPile.ItemChip` native face/backing, card grip and
hand-to-hand transfer, fan shape, sweep arbitration, laser pluck, emergence and return
animations. The host does not publish scenario `ItemsPile.Current` or `RecessOwner`,
does not invent a scenario actor, and never invokes an item-use gameplay route.
Closing fan surfaces remain published through their complete return; held cards survive
wrist closure. The map fan resumes on leave, owner switch, option/context loss or reset.

Approach and pickup never open a native service or transact. Only a valid release into
the actual palm requests the original merchant window. The pending request waits for its
inventory to be bound to the same character and session, then invokes the existing native
transaction adapter to open the ordinary explicit confirmation. Inventory eligibility is
revalidated on release and on native dispatch. Leaving cancels only the exact confirmation
callback this handoff created. Other confirmations remain untouched.

## Integration hooks

`TownServiceMerchantHandoff.Tick()` after resident population preparation, `LateTick()`
after activity IK, and `Reset()` before population teardown. The catalog's three release
callbacks are assigned only for a live local inspection. Its `HeldOfferAvailable` controls
the buy mark; actual owned ItemChips control the sell mark. The town source publisher reads
`Active`, `Session`, `SessionAge`, `StationRoot`, `Zone` and `OwnedChips`. `NativeItemCard`,
`InspectionMount` and `InspectionBody` are the original visible outputs, not recreated UI.

The inspection backing template factory calls the same original ItemChip backing builder;
its caller must use the source item's measured face width/height to preserve geometry.

## Verification and boundaries

- `scripts/check-town-merchant-handoff.py`: 87 real Unity runtime assertions and six
  negative controls. Final evidence: `/tmp/town549-handoff-attention/run-j6whdwxr`.
- Production controller, inspection lifecycle and map ownership/suspension code are compiled
  directly. The actual attention method is compiled unchanged against Unity camera/physics.
- Tested 31 equipped/bound card copies including repeated IDs, held-card persistence,
  closing-source retention, exact sale identity, delayed/mismatched native inventory,
  purchase vs sale, authority refusal, leave/character/reset, unrelated confirmation,
  front/distance/hysteresis/occlusion, .05 / 1 / 198.12 scale and 100x glove armature.
- Six mutations must fail: bypass ownership, bypass palm, cap inventory, dispatch into
  another character's inventory, open native service on approach, drop closing sources.
- Native game models/transaction adapter and the established ItemChip rendering/grabbing
  are explicit boundaries in this focused controller suite. The strict complete plugin
  build verifies actual integration types; it does not prove headset appearance or native
  multiplayer callbacks. Cards lane validates the original confirmation adapter and wire.
- Ordinary logging adds no per-frame or per-card stream. Inventory snapshots poll at 5 Hz;
  overlay eligibility caches for 120 ms, with uncached checks at the actual release.

## Follow-up: bounded census, real layout/lifecycle and teardown

The first implementation still scanned every owned item for every chip and rebuilt the
layout once per new card, plus once every frame. Membership now has an explicit revision
from the 5 Hz model poll; reference order comparisons advance it only on actual changes.
A reusable desired-membership set and dirty census run on revisions, reveal edges,
actual releases, and completed closing waves. All new cards receive their final batch
homes in one layout before their original emergence starts. Steady layout changes are
limited to membership, hover split or live geometry settings. Publication lists change
only with membership; original chip transforms/effects continue to update normally.

Reset withdraws its fan, zone and confirmation ownership before invoking native `OnCancel`.
Nested `onHidden`/reset/tick calls cannot recreate or destroy the inspection twice. Cleanup
and normal-fan restoration run in `finally`, including a native callback failure.

The expanded Unity test compiles the original production `Relayout`, `ItemChip.SetHome`,
`BeginEmerge`, `TickEmerge`, and `BeginCollapse` methods against real transforms. Native
ItemCardUI pooling, physical sweep/input and per-card renderer upkeep remain boundaries.
The benchmark therefore measures host census/layout overhead, not total renderer/GPU cost.
It exercises the actual emergence to its final batch home for all 31 and 512 item copies.
The retained historical host fixture (from f12602bf) must fail the census invariant; it
needs no worker-branch commit object at CI runtime.

Measured in Unity Editor 2021.3.5, 1000 warm ticks:

| Owned copies | Previous host ms/tick | Revised host ms/tick | Initial layouts before/after | Revised managed allocations |
| --- | ---: | ---: | ---: | ---: |
| 31 | 0.032583 | 0.000613 | 32 / 1 | 0 bytes |
| 512 | 1.795890 | 0.000635 | 513 / 1 | 0 bytes |

Evidence: `/tmp/town549-handoff-benchmark/run-c6wg78x9`; 1184 assertions plus seven
negative controls. Subsequent verification adds a live-settings layout invalidation case.
These timings are not hardware frame-rate predictions. In particular, hand sweep, native
pooled artwork construction and real per-card frame updates still have their existing costs.

With the map hand preference disabled, merchant inspection remains disabled as requested.
The integration must retain the classic merchant (as for enchantment), including its
normal destination control and original window: masking a native merchant without an
available physical owned-card input would remove the buying/selling path. Root owns that
Presentation/VisitTarget fallback, independently of this host.

Final run: `/tmp/town549-handoff-final-perf/run-ovaq3z9a`, 1186 assertions and seven
negative controls. Live geometry tuning invalidates layout exactly once. Final repeated
measurements were 0.030266 -> 0.000693 ms/tick (31 cards) and 2.171280 -> 0.000719
ms/tick (512); unchanged new-host ticks still allocate zero managed bytes.
